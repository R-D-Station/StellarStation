using LiteNetLib;
using Shared.Configs;
using Shared.Messages.Interaction;
using Shared.World;
using Shared.World.Items;
using Server.Network;

namespace Server.Items
{
    /// <summary>Серверный инвентарь игроков: дренаж очередей pickup/drop/slot-op и правила экипировки/коллизионного дропа.</summary>
    public sealed class ServerInventorySystem
    {
        private readonly GameServer _server;
        private readonly GroundItemWorld _ground;
        private readonly Dictionary<NetPeer, ClientConnection> _clients;
        private readonly Dictionary<int, IWorldEntity> _entities;

        /// <summary>Резолв ItemDefId→ItemProto; дефолт — ItemCatalogData, тесты подменяют лямбдой.</summary>
        public Func<ushort, ItemProto?> ProtoLookup = defId =>
            ItemCatalogData.TryGet(defId, out var p) ? p : (ItemProto?)null;

        public ServerInventorySystem(GameServer server, GroundItemWorld ground, Dictionary<NetPeer, ClientConnection> clients,
            Dictionary<int, IWorldEntity> entities)
        {
            _server = server;
            _ground = ground;
            _clients = clients;
            _entities = entities;
        }

        public void ProcessPickups()
        {
            const int maxPerTick = 32;
            foreach (var client in _clients.Values)
            {
                int applied = 0;
                while (applied < maxPerTick && client.PickupQueue.TryDequeue(out var msg))
                {
                    HandlePickup(client, in msg);
                    applied++;
                }
                if (applied == maxPerTick && !client.PickupQueue.IsEmpty)
                {
                    int dropped = 0;
                    while (client.PickupQueue.TryDequeue(out _)) dropped++;
                    Console.WriteLine($"[Server] pickup flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
                }
            }
        }

        public void ProcessDrops()
        {
            const int maxPerTick = 32;
            foreach (var client in _clients.Values)
            {
                int applied = 0;
                while (applied < maxPerTick && client.DropQueue.TryDequeue(out var msg))
                {
                    HandleDrop(client, in msg);
                    applied++;
                }
                if (applied == maxPerTick && !client.DropQueue.IsEmpty)
                {
                    int dropped = 0;
                    while (client.DropQueue.TryDequeue(out _)) dropped++;
                    Console.WriteLine($"[Server] drop flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
                }
            }
        }

        public void ProcessSlotOps()
        {
            const int maxPerTick = 32;
            foreach (var client in _clients.Values)
            {
                int applied = 0;
                while (applied < maxPerTick && client.SwapQueue.TryDequeue(out var msg))
                {
                    HandleSwapHand(client, in msg);
                    applied++;
                }
                if (applied == maxPerTick && !client.SwapQueue.IsEmpty)
                {
                    int dropped = 0;
                    while (client.SwapQueue.TryDequeue(out _)) dropped++;
                    Console.WriteLine($"[Server] swap-hand flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
                }

                applied = 0;
                while (applied < maxPerTick && client.MoveSlotQueue.TryDequeue(out var msg))
                {
                    HandleMoveSlot(client, in msg);
                    applied++;
                }
                if (applied == maxPerTick && !client.MoveSlotQueue.IsEmpty)
                {
                    int dropped = 0;
                    while (client.MoveSlotQueue.TryDequeue(out _)) dropped++;
                    Console.WriteLine($"[Server] move-slot flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
                }
            }
        }

        private void HandlePickup(ClientConnection client, in PickupItem msg)
        {
            if (client.PulledNetId != 0) return;
            if (!_entities.TryGetValue(msg.TargetNetId, out var e)) return;
            if (e is not GroundItemEntity gi) return;

            int px = (int)MathF.Floor(client.X);
            int py = (int)MathF.Floor(client.Y);
            if (!_server.Reachable(px, py, client.Z, (int)MathF.Floor(gi.CellX), (int)MathF.Floor(gi.CellY), (int)MathF.Floor(gi.Z))) return;

            int span = SlotSpanOf(gi.ItemDefId);
            if (span > InventorySlot.DefaultCount(SlotCategory.Hand)) return;

            var item = new HeldItem { NetId = gi.NetId, ItemDefId = gi.ItemDefId, StackCount = gi.StackCount };

            if (span == 1)
            {
                if (client.Slots[(int)SlotCategory.Hand][client.ActiveHand].NetId != 0) return;
                // Despawn ПОСЛЕ всех return-проверок — проигравший гонку за один NetId в этот тик получит false, без утечки.
                if (!_ground.DespawnGroundItem(msg.TargetNetId)) return;
                client.Slots[(int)SlotCategory.Hand][client.ActiveHand] = item;
            }
            else
            {
                if (FreeCount(client, SlotCategory.Hand) < span) return;
                if (!_ground.DespawnGroundItem(msg.TargetNetId)) return;
                TakeLowestFree(client, SlotCategory.Hand, span, in item);
            }

            SendInventorySyncToOwner(client);
        }

        private void HandleDrop(ClientConnection client, in DropItem msg)
        {
            var slot = client.Slots[(int)msg.Category];
            if (msg.Index >= slot.Length) return;
            var h = slot[msg.Index];
            if (h.NetId == 0) return;

            int cx = (int)MathF.Floor(client.X);
            int cy = (int)MathF.Floor(client.Y);
            int z = client.Z;

            float px = client.X, py = client.Y, pz = z;
            var proto = ProtoLookup(h.ItemDefId);
            if (proto.HasValue && proto.Value.HasCollision)
            {
                if (!TryFindCollisionDropCell(client, proto.Value.CollisionBox, out int ncx, out int ncy, out int nz)) return;
                px = ncx + 0.5f;
                py = ncy + 0.5f;
                pz = nz;
            }
            else
            {
                pz = Shared.World.Blocks.ItemGroundSnap.SnapDown(_server.BlockWorld!, _server.BlockShapes, cx, z, cy);
            }

            ClearItemByNetId(client, msg.Category, h.NetId);
            _ground.SpawnGroundItemWithId(h.NetId, h.ItemDefId, h.StackCount, px, py, pz);
            SendInventorySyncToOwner(client);
        }

        private static readonly (int dx, int dy)[] DropNeighbors = { (0, 1), (1, 0), (0, -1), (-1, 0) };

        private static int FacingToNeighborIndex(byte facing) => facing switch { 0 => 0, 2 => 1, 1 => 2, 3 => 3, _ => 0 };

        // Коллизионный предмет нельзя дропнуть под ноги (занял бы клетку игрока) — сканируем N/E/S/W соседей,
        // facing-клетку первой (UX: предмет падает перед игроком), остальные — fallback по кругу.
        private bool TryFindCollisionDropCell(ClientConnection client, in Shared.World.Blocks.BlockBox box, out int cx, out int cy, out int z)
        {
            int px = (int)MathF.Floor(client.X);
            int py = (int)MathF.Floor(client.Y);
            int first = FacingToNeighborIndex(client.Facing);
            for (int i = -1; i < DropNeighbors.Length; i++)
            {
                int idx = i < 0 ? first : i;
                if (i >= 0 && idx == first) continue;
                int nx = px + DropNeighbors[idx].dx;
                int ny = py + DropNeighbors[idx].dy;
                int restY = Shared.World.Blocks.ItemGroundSnap.SnapDown(_server.BlockWorld!, _server.BlockShapes, nx, client.Z, ny);
                if (CollisionCellFree(nx, ny, restY, in box))
                {
                    cx = nx;
                    cy = ny;
                    z = restY;
                    return true;
                }
            }
            cx = 0; cy = 0; z = 0;
            return false;
        }

        private bool CollisionCellFree(int nx, int ny, int restY, in Shared.World.Blocks.BlockBox box)
        {
            float halfW = (box.MaxXf - box.MinXf) * 0.5f;
            float minX = nx + 0.5f - halfW, maxX = nx + 0.5f + halfW;
            float minZ = ny + 0.5f - halfW, maxZ = ny + 0.5f + halfW;
            float minY = restY + box.MinYf, maxY = restY + box.MaxYf;

            foreach (var e in _entities.Values)
            {
                if (e is not GroundItemEntity gi) continue;
                var p = ProtoLookup(gi.ItemDefId);
                if (!p.HasValue || !p.Value.HasCollision) continue;
                var ob = p.Value.CollisionBox;
                float oHalfW = (ob.MaxXf - ob.MinXf) * 0.5f;
                float oMinX = gi.X - oHalfW, oMaxX = gi.X + oHalfW;
                float oMinZ = gi.Y - oHalfW, oMaxZ = gi.Y + oHalfW;
                float oMinY = gi.Z + ob.MinYf, oMaxY = gi.Z + ob.MaxYf;
                if (minX < oMaxX && maxX > oMinX && minZ < oMaxZ && maxZ > oMinZ && minY < oMaxY && maxY > oMinY)
                    return false;
            }

            foreach (var other in _clients.Values)
            {
                float pMinX = other.Mover.X - Shared.Simulation.Blocks.BlockMovementConfig.HalfWidth;
                float pMaxX = other.Mover.X + Shared.Simulation.Blocks.BlockMovementConfig.HalfWidth;
                float pMinZ = other.Mover.Z - Shared.Simulation.Blocks.BlockMovementConfig.HalfWidth;
                float pMaxZ = other.Mover.Z + Shared.Simulation.Blocks.BlockMovementConfig.HalfWidth;
                float pMinY = other.Mover.Y;
                float pMaxY = other.Mover.Y + Shared.Simulation.Blocks.BlockMovementConfig.StandHeight;
                if (minX < pMaxX && maxX > pMinX && minZ < pMaxZ && maxZ > pMinZ && minY < pMaxY && maxY > pMinY)
                    return false;
            }
            return true;
        }

        private void HandleSwapHand(ClientConnection client, in SwapHandRequest msg)
        {
            if (client.ActiveHand == msg.Hand) return;
            client.ActiveHand = msg.Hand;
            SendInventorySyncToOwner(client);
        }

        private void HandleMoveSlot(ClientConnection client, in MoveSlotRequest msg)
        {
            var from = client.Slots[(int)msg.FromCategory];
            var to = client.Slots[(int)msg.ToCategory];
            if (msg.FromIndex >= from.Length || msg.ToIndex >= to.Length) return;

            var h = from[msg.FromIndex];
            if (h.NetId == 0) return;
            if (!CanEquip(h.ItemDefId, msg.ToCategory)) return;

            int span = SlotSpanOf(h.ItemDefId);

            if (span == 1)
            {
                if (to[msg.ToIndex].NetId != 0) return;
                to[msg.ToIndex] = h;
                from[msg.FromIndex] = default;
            }
            else
            {
                if (span > InventorySlot.DefaultCount(msg.ToCategory)) return;
                int free = FreeCount(client, msg.ToCategory);
                // Same-category move: предмет уже занимает свои N слотов — не считать их занятостью против самого себя.
                if (msg.FromCategory == msg.ToCategory) free += CountItemByNetId(client, msg.ToCategory, h.NetId);
                if (free < span) return;
                ClearItemByNetId(client, msg.FromCategory, h.NetId);
                TakeLowestFree(client, msg.ToCategory, span, in h);
            }

            SendInventorySyncToOwner(client);
        }

        private bool CanEquip(ushort itemDefId, SlotCategory toCategory)
        {
            if (toCategory == SlotCategory.Hand) return true;
            var p = ProtoLookup(itemDefId);
            return p.HasValue && p.Value.Equippable && p.Value.EquipSlot == toCategory;
        }

        private int SlotSpanOf(ushort itemDefId)
        {
            var p = ProtoLookup(itemDefId);
            int span = p.HasValue ? p.Value.SlotSpan : 1;
            return span < 1 ? 1 : span;
        }

        private static int FreeCount(ClientConnection client, SlotCategory cat)
        {
            var arr = client.Slots[(int)cat];
            int free = 0;
            for (int i = 0; i < arr.Length; i++) if (arr[i].NetId == 0) free++;
            return free;
        }

        private static int CountItemByNetId(ClientConnection client, SlotCategory cat, int netId)
        {
            var arr = client.Slots[(int)cat];
            int n = 0;
            for (int i = 0; i < arr.Length; i++) if (arr[i].NetId == netId) n++;
            return n;
        }

        private static void TakeLowestFree(ClientConnection client, SlotCategory cat, int n, in HeldItem item)
        {
            var arr = client.Slots[(int)cat];
            int taken = 0;
            for (int i = 0; i < arr.Length && taken < n; i++)
                if (arr[i].NetId == 0) { arr[i] = item; taken++; }
        }

        private static int ClearItemByNetId(ClientConnection client, SlotCategory cat, int netId)
        {
            var arr = client.Slots[(int)cat];
            int cleared = 0;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i].NetId == netId) { arr[i] = default; cleared++; }
            return cleared;
        }

        /// <summary>Шлёт владельцу полный слепок занятых слотов (только владельцу — held не виден чужим клиентам).</summary>
        public void SendInventorySyncToOwner(ClientConnection owner)
        {
            int occupied = 0;
            for (int c = 0; c < owner.Slots.Length; c++)
            {
                var arr = owner.Slots[c];
                for (int i = 0; i < arr.Length; i++)
                    if (arr[i].NetId != 0) occupied++;
            }

            var records = new SlotRecord[occupied];
            int k = 0;
            for (int c = 0; c < owner.Slots.Length; c++)
            {
                var arr = owner.Slots[c];
                for (byte i = 0; i < arr.Length; i++)
                {
                    var h = arr[i];
                    if (h.NetId == 0) continue;
                    records[k++] = new SlotRecord { Category = (SlotCategory)c, Index = i, ItemDefId = h.ItemDefId, StackCount = h.StackCount };
                }
            }

            _server.SendToClient(owner, new InventorySync
            {
                ActiveHand = owner.ActiveHand,
                Slots = records
            });
        }

        public void DropAllHeldOnDisconnect(ClientConnection client)
        {
            int cx = (int)MathF.Floor(client.X);
            int cy = (int)MathF.Floor(client.Y);
            int z = client.Z;
            z = Shared.World.Blocks.ItemGroundSnap.SnapDown(_server.BlockWorld!, _server.BlockShapes, cx, z, cy);

            var seen = new HashSet<int>(); // span>1 items occupy N slots but share one NetId — drop once, not per-slot
            for (int c = 0; c < client.Slots.Length; c++)
            {
                var arr = client.Slots[c];
                for (int i = 0; i < arr.Length; i++)
                {
                    var h = arr[i];
                    if (h.NetId == 0) continue;
                    if (seen.Add(h.NetId))
                    {
                        float dx = client.X, dy = client.Y, dz = z;
                        var p = ProtoLookup(h.ItemDefId);
                        if (p.HasValue && p.Value.HasCollision)
                        {
                            if (TryFindCollisionDropCell(client, p.Value.CollisionBox, out int nx, out int ny, out int nz))
                            {
                                dx = nx + 0.5f; dy = ny + 0.5f; dz = nz;
                            }
                            else
                            {
                                Console.WriteLine($"[Items] disconnect drop: no free neighbor for collision item {h.NetId} — forced underfoot at ({cx},{cy},{z})");
                            }
                        }
                        _ground.SpawnGroundItemWithId(h.NetId, h.ItemDefId, h.StackCount, dx, dy, dz);
                    }
                    arr[i] = default;
                }
            }
        }
    }
}
