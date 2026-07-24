using LiteNetLib;
using Shared.Messages.Interaction;
using Shared.Simulation.Blocks;
using Shared.World;
using Shared.World.Items;
using Server.Network;

namespace Server.Items
{
    /// <summary>Серверно-авторитетные контейнеры: UI-окно (viewer-очередь) и SS14-физический ящик (сос/высып через E), плюс заморозка игроков внутри.</summary>
    public sealed class ServerContainerSystem
    {
        // внутреннее кольцо высыпа (8 направлений, r*0.5)
        private static readonly (float dx, float dz)[] InnerDirs =
        {
            (1f, 0f), (0.7071f, 0.7071f), (0f, 1f), (-0.7071f, 0.7071f),
            (-1f, 0f), (-0.7071f, -0.7071f), (0f, -1f), (0.7071f, -0.7071f)
        };

        // внешнее кольцо высыпа (16 направлений, r*0.9)
        private static readonly (float dx, float dz)[] OuterDirs =
        {
            (1f, 0f), (0.9239f, 0.3827f), (0.7071f, 0.7071f), (0.3827f, 0.9239f),
            (0f, 1f), (-0.3827f, 0.9239f), (-0.7071f, 0.7071f), (-0.9239f, 0.3827f),
            (-1f, 0f), (-0.9239f, -0.3827f), (-0.7071f, -0.7071f), (-0.3827f, -0.9239f),
            (0f, -1f), (0.3827f, -0.9239f), (0.7071f, -0.7071f), (0.9239f, -0.3827f)
        };

        private readonly GameServer _server;
        private readonly ServerInventorySystem _inventory;
        private readonly Dictionary<int, IWorldEntity> _entities;
        private readonly Dictionary<NetPeer, ClientConnection> _clients;
        // ключ — NetId контейнера (стабилен при ground↔hand), не identity сущности
        private readonly Dictionary<int, List<HeldItem>> _contents = new();
        private readonly Dictionary<int, int> _viewerCount = new();
        // ключ — identity GroundItemEntity: пере-дроп того же NetId = новая сущность = крышка снова закрыта
        private readonly Dictionary<int, GroundItemEntity> _lidOpen = new();
        private readonly List<int> _lidPruneScratch = new();

        public ServerContainerSystem(GameServer server, ServerInventorySystem inventory,
            Dictionary<int, IWorldEntity> entities, Dictionary<NetPeer, ClientConnection> clients)
        {
            _server = server;
            _inventory = inventory;
            _entities = entities;
            _clients = clients;
        }

        /// <summary>Дренирует close/open/put/take очереди всех клиентов (flood-guard 32/тик) + чистит протухшие lid/contained записи; вызывается раз в тик.</summary>
        public void ProcessContainerOps()
        {
            const int maxPerTick = 32;
            PruneStaleLids();
            PruneStaleContained();
            foreach (var client in _clients.Values)
            {
                int applied = 0;
                while (applied < maxPerTick && client.CloseContainerQueue.TryDequeue(out var msg))
                {
                    HandleClose(client, msg.NetId);
                    applied++;
                }
                Flood(client, client.CloseContainerQueue, applied, maxPerTick, "close-container");

                applied = 0;
                while (applied < maxPerTick && client.OpenContainerQueue.TryDequeue(out var msg))
                {
                    HandleOpen(client, msg.NetId);
                    applied++;
                }
                Flood(client, client.OpenContainerQueue, applied, maxPerTick, "open-container");

                applied = 0;
                while (applied < maxPerTick && client.PutInContainerQueue.TryDequeue(out var msg))
                {
                    HandlePut(client, msg.NetId);
                    applied++;
                }
                Flood(client, client.PutInContainerQueue, applied, maxPerTick, "put-container");

                applied = 0;
                while (applied < maxPerTick && client.TakeFromContainerQueue.TryDequeue(out var msg))
                {
                    HandleTake(client, msg.NetId, msg.Index);
                    applied++;
                }
                Flood(client, client.TakeFromContainerQueue, applied, maxPerTick, "take-container");
            }
        }

        private static void Flood<T>(ClientConnection client, System.Collections.Concurrent.ConcurrentQueue<T> q, int applied, int max, string label)
        {
            if (applied != max || q.IsEmpty) return;
            int dropped = 0;
            while (q.TryDequeue(out _)) dropped++;
            Console.WriteLine($"[Server] {label} flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
        }

        private void HandleOpen(ClientConnection client, int netId)
        {
            if (!TryResolveContainer(client, netId, out ushort defId)) return;
            var p = _inventory.ProtoLookup(defId);
            if (!p.HasValue || !p.Value.IsContainer) return;

            if (p.Value.ContainerMode == ContainerMode.SS14)
            {
                if (!_entities.TryGetValue(netId, out var e) || e is not GroundItemEntity gi) return;
                if (IsLidOpen(netId))
                {
                    // E на открытой крышке = закрыть → всосать нэрбай предметы и запереть стоящих на клетке игроков
                    _lidOpen.Remove(netId);
                    SuckItems(netId, gi, p.Value);
                    ContainPlayersOnCell(gi);
                }
                else
                {
                    // E на закрытой = открыть → высыпать содержимое кольцами и выпустить запертых
                    _lidOpen[netId] = gi;
                    SpillContents(netId, gi, p.Value);
                    ReleasePlayers(netId, gi);
                }
                return;
            }

            if (client.OpenContainers.Add(netId))
                _viewerCount[netId] = (_viewerCount.TryGetValue(netId, out var n) ? n : 0) + 1;
            _server.SendToClient(client, BuildSync(netId));
        }

        private void SuckItems(int containerNetId, GroundItemEntity box, in ItemProto boxProto)
        {
            var list = Contents(containerNetId);
            int cap = boxProto.MaxContents;
            if (list.Count >= cap) return;
            float rSq = boxProto.SuckRadius * boxProto.SuckRadius;

            var candidates = new List<(float distSq, int netId, ushort defId, byte stack)>();
            foreach (var e in _entities.Values)
            {
                if (e is not GroundItemEntity gi || gi.NetId == containerNetId) continue;
                float dx = gi.X - box.X, dy = gi.Y - box.Y;
                float dSq = dx * dx + dy * dy;
                if (dSq > rSq) continue;
                var ip = _inventory.ProtoLookup(gi.ItemDefId);
                if (!ip.HasValue || !ContainerFilter.Allows(boxProto, ip.Value)) continue;
                candidates.Add((dSq, gi.NetId, gi.ItemDefId, gi.StackCount));
            }
            candidates.Sort((a, b) => a.distSq != b.distSq ? a.distSq.CompareTo(b.distSq) : a.netId.CompareTo(b.netId));

            for (int i = 0; i < candidates.Count && list.Count < cap; i++)
            {
                var c = candidates[i];
                if (!_server.DespawnGroundItem(c.netId)) continue;
                list.Add(new HeldItem { NetId = c.netId, ItemDefId = c.defId, StackCount = c.stack });
            }
        }

        // концентрические кольца от ящика: последний (max-slot) индекс — в центр, следующие 8 — внутреннее кольцо, остаток — внешнее
        private void SpillContents(int containerNetId, GroundItemEntity box, in ItemProto boxProto)
        {
            if (!_contents.TryGetValue(containerNetId, out var list) || list.Count == 0) return;

            float r = boxProto.SuckRadius;
            int center = boxProto.MaxContents - 1;
            for (int i = 0; i < list.Count; i++)
            {
                var h = list[i];
                var ip = _inventory.ProtoLookup(h.ItemDefId);
                bool hasCol = ip.HasValue && ip.Value.HasCollision;
                float halfW = 0.3f, height = 0.4f, feetOffset = 0f;
                if (hasCol)
                {
                    var b = ip.Value.CollisionBox;
                    halfW = (b.MaxXf - b.MinXf) * 0.5f;
                    height = b.MaxYf - b.MinYf;
                    feetOffset = b.MinYf;
                }

                float px, py;
                if (i == center)
                {
                    px = box.X;
                    py = box.Y;
                }
                else if (i < 8)
                {
                    var d = InnerDirs[i % 8];
                    px = box.X + d.dx * (r * 0.5f);
                    py = box.Y + d.dz * (r * 0.5f);
                }
                else
                {
                    var d = OuterDirs[(i - 8) % 16];
                    px = box.X + d.dx * (r * 0.9f);
                    py = box.Y + d.dz * (r * 0.9f);
                }

                // слот кольца перекрыт геометрией — не терять предмет, кинуть у самого ящика
                if (_server.BlockWorld != null &&
                    BlockMovementLogic.CollidesBox(_server.BlockWorld, _server.BlockShapes, px, box.Z + feetOffset, py, halfW, height))
                {
                    px = box.X;
                    py = box.Y;
                }

                _server.SpawnGroundItemWithId(h.NetId, h.ItemDefId, h.StackCount, px, py, box.Z);
            }
            list.Clear();
        }

        // 9-клеточный скан (соседи + сама клетка) под свободное место при выпуске игрока
        private static readonly (int dx, int dz)[] ReleaseCells =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (1, 1), (1, -1), (-1, 1), (-1, -1),
            (0, 0)
        };

        private readonly List<(int x, int z)> _releasePlaced = new();

        // игрок, стоящий на клетке ящика на момент закрытия крышки, — запирается внутрь
        private void ContainPlayersOnCell(GroundItemEntity box)
        {
            int bx = (int)MathF.Floor(box.X);
            int bz = (int)MathF.Floor(box.Y);
            int by = (int)MathF.Floor(box.Z);
            foreach (var c in _clients.Values)
            {
                if (c.ContainedInNetId != 0) continue;
                if ((int)MathF.Floor(c.Mover.X) == bx
                    && (int)MathF.Floor(c.Mover.Z) == bz
                    && (int)MathF.Floor(c.Mover.Y) == by)
                {
                    c.ContainedInNetId = box.NetId;
                    _server.SendToClient(c, new ContainSync { ContainerNetId = box.NetId });
                }
            }
        }

        // выпускает всех запертых в этом ящике на свободную соседнюю клетку (без наложения друг на друга)
        private void ReleasePlayers(int containerNetId, GroundItemEntity box)
        {
            int bx = (int)MathF.Floor(box.X);
            int bz = (int)MathF.Floor(box.Y);
            _releasePlaced.Clear();
            foreach (var c in _clients.Values)
            {
                if (c.ContainedInNetId != containerNetId) continue;
                c.ContainedInNetId = 0;

                int tx = bx, tz = bz;
                for (int i = 0; i < ReleaseCells.Length; i++)
                {
                    int nx = bx + ReleaseCells[i].dx;
                    int nz = bz + ReleaseCells[i].dz;
                    if (PlacedThisCall(nx, nz)) continue;
                    bool standable = _server.BlockWorld == null
                        || BlockMovementLogic.CanStand(_server.BlockWorld, _server.BlockShapes, nx + 0.5f, box.Z, nz + 0.5f);
                    if (standable && !CellOccupied(nx, nz, box.Z, c))
                    {
                        tx = nx;
                        tz = nz;
                        break;
                    }
                }
                _releasePlaced.Add((tx, tz));

                c.Mover.X = tx + 0.5f;
                c.Mover.Z = tz + 0.5f;
                c.X = c.Mover.X;
                c.Y = c.Mover.Z;
                c.Z = (int)MathF.Floor(c.Mover.Y);
                _server.SendToClient(c, new ContainSync { ContainerNetId = 0 });
            }
        }

        private bool PlacedThisCall(int cx, int cz)
        {
            for (int i = 0; i < _releasePlaced.Count; i++)
                if (_releasePlaced[i].x == cx && _releasePlaced[i].z == cz) return true;
            return false;
        }

        private bool CellOccupied(int cx, int cz, float y, ClientConnection except)
        {
            int by = (int)MathF.Floor(y);
            foreach (var o in _clients.Values)
            {
                if (o == except || o.ContainedInNetId != 0) continue;
                if ((int)MathF.Floor(o.Mover.X) == cx
                    && (int)MathF.Floor(o.Mover.Z) == cz
                    && (int)MathF.Floor(o.Mover.Y) == by)
                    return true;
            }
            return false;
        }

        // контейнер, в котором заперт игрок, уничтожен извне — освободить, не дожидаясь HandleOpen
        private void PruneStaleContained()
        {
            foreach (var c in _clients.Values)
                if (c.ContainedInNetId != 0 && !_entities.ContainsKey(c.ContainedInNetId))
                {
                    c.ContainedInNetId = 0;
                    _server.SendToClient(c, new ContainSync { ContainerNetId = 0 });
                }
        }

        private void HandleClose(ClientConnection client, int netId)
        {
            if (!client.OpenContainers.Remove(netId)) return;
            DropViewer(netId);
        }

        private void DropViewer(int netId)
        {
            if (!_viewerCount.TryGetValue(netId, out var n)) return;
            if (n <= 1) _viewerCount.Remove(netId);
            else _viewerCount[netId] = n - 1;
        }

        /// <summary>Открыт ли контейнер миру (UI-вьюер ИЛИ откинутая SS14-крышка) — гейт снапшот-видимости содержимого/скрытия предметов.</summary>
        public bool IsWorldOpen(int netId) => _viewerCount.ContainsKey(netId) || IsLidOpen(netId);

        // identity-check, не только NetId: пере-дроп того же NetId создаёт новую GroundItemEntity → крышка снова закрыта
        private bool IsLidOpen(int netId) =>
            _lidOpen.TryGetValue(netId, out var ent)
            && _entities.TryGetValue(netId, out var cur)
            && ReferenceEquals(cur, ent);

        private void PruneStaleLids()
        {
            if (_lidOpen.Count == 0) return;
            _lidPruneScratch.Clear();
            foreach (var kv in _lidOpen)
                if (!_entities.TryGetValue(kv.Key, out var cur) || !ReferenceEquals(cur, kv.Value))
                    _lidPruneScratch.Add(kv.Key);
            for (int i = 0; i < _lidPruneScratch.Count; i++)
                _lidOpen.Remove(_lidPruneScratch[i]);
        }

        private void HandlePut(ClientConnection client, int netId)
        {
            if (!client.OpenContainers.Contains(netId)) return;
            if (!TryResolveContainer(client, netId, out ushort defId)) return;
            var p = _inventory.ProtoLookup(defId);
            if (!p.HasValue || !p.Value.IsContainer) return;

            var hand = client.Slots[(int)SlotCategory.Hand];
            var held = hand[client.ActiveHand];
            if (held.NetId == 0) return;
            if (held.NetId == netId) return;

            var list = Contents(netId);
            if (list.Count >= p.Value.MaxContents) return;
            if (_contents.TryGetValue(held.NetId, out var inner) && inner.Count > 0) return; // не разрешаем вложенный контейнер с содержимым — без рекурсивного unwrap на suck/spill

            var heldProto = _inventory.ProtoLookup(held.ItemDefId);
            if (!heldProto.HasValue || !ContainerFilter.Allows(p.Value, heldProto.Value)) return;

            list.Add(held);
            hand[client.ActiveHand] = default;

            BroadcastContainerSync(netId);
            _inventory.SendInventorySyncToOwner(client);
        }

        private void HandleTake(ClientConnection client, int netId, ushort index)
        {
            if (!client.OpenContainers.Contains(netId)) return;
            if (!TryResolveContainer(client, netId, out _)) return;
            if (!_contents.TryGetValue(netId, out var list)) return;
            if (index >= list.Count) return;

            var hand = client.Slots[(int)SlotCategory.Hand];
            if (hand[client.ActiveHand].NetId != 0) return;

            var item = list[index];
            list.RemoveAt(index);
            hand[client.ActiveHand] = item;

            BroadcastContainerSync(netId);
            _inventory.SendInventorySyncToOwner(client);
        }

        /// <summary>Снимает клиента-вьюера со всех открытых им UI-контейнеров (содержимое не трогает).</summary>
        public void OnClientDisconnect(ClientConnection client)
        {
            foreach (var netId in client.OpenContainers)
                DropViewer(netId);
            client.OpenContainers.Clear();
        }

        private bool TryResolveContainer(ClientConnection client, int netId, out ushort defId)
        {
            for (int c = 0; c < client.Slots.Length; c++)
            {
                var arr = client.Slots[c];
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i].NetId != netId) continue;
                    defId = arr[i].ItemDefId;
                    return true;
                }
            }

            if (_entities.TryGetValue(netId, out var e) && e is GroundItemEntity gi)
            {
                int px = (int)MathF.Floor(client.X);
                int py = (int)MathF.Floor(client.Y);
                if (_server.Reachable(px, py, client.Z, (int)MathF.Floor(gi.CellX), (int)MathF.Floor(gi.CellY), (int)MathF.Floor(gi.Z)))
                {
                    defId = gi.ItemDefId;
                    return true;
                }
            }

            defId = 0;
            return false;
        }

        private List<HeldItem> Contents(int netId)
        {
            if (!_contents.TryGetValue(netId, out var list))
            {
                list = new List<HeldItem>();
                _contents[netId] = list;
            }
            return list;
        }

        private void BroadcastContainerSync(int netId)
        {
            var msg = BuildSync(netId);
            foreach (var client in _clients.Values)
                if (client.OpenContainers.Contains(netId))
                    _server.SendToClient(client, msg);
        }

        private ContainerSync BuildSync(int netId)
        {
            ContainerItem[] items;
            if (_contents.TryGetValue(netId, out var list))
            {
                items = new ContainerItem[list.Count];
                for (int i = 0; i < list.Count; i++)
                    items[i] = new ContainerItem { ItemDefId = list[i].ItemDefId, StackCount = list[i].StackCount };
            }
            else
            {
                items = Array.Empty<ContainerItem>();
            }
            return new ContainerSync { ContainerNetId = netId, Items = items };
        }
    }
}
