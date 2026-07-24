using LiteNetLib;
using Shared.Messages.Interaction;
using Shared.Simulation;
using Shared.Simulation.Blocks;
using Shared.World.Items;
using Server.Network;

namespace Server.Items
{
    /// <summary>Авторитетная тяга ящиков (block-mode only): грэб/релиз-тоггл, штраф скорости, следование за игроком.</summary>
    public sealed class ServerPullSystem
    {
        private const float FollowDistance = 1.4f;
        private const float LeashMax = 2.5f;
        private const float HeightLeash = 1.5f;

        private readonly GameServer _server;
        private readonly ServerInventorySystem _inventory;
        private readonly GroundItemWorld _ground;
        private readonly Dictionary<int, IWorldEntity> _entities;
        private readonly Dictionary<NetPeer, ClientConnection> _clients;

        /// <summary>Собирает систему тяги над общими реестрами сервера (сущности/клиенты — общие с остальными системами).</summary>
        public ServerPullSystem(GameServer server, ServerInventorySystem inventory, GroundItemWorld ground,
            Dictionary<int, IWorldEntity> entities, Dictionary<NetPeer, ClientConnection> clients)
        {
            _server = server;
            _inventory = inventory;
            _ground = ground;
            _entities = entities;
            _clients = clients;
        }

        /// <summary>Дренирует очереди PullItem всех клиентов за тик (флуд-защита как у остальных intent-очередей).</summary>
        public void ProcessPullOps()
        {
            const int maxPerTick = 32;
            foreach (var client in _clients.Values)
            {
                int applied = 0;
                while (applied < maxPerTick && client.PullQueue.TryDequeue(out var msg))
                {
                    HandlePull(client, msg.NetId);
                    applied++;
                }
                Flood(client, client.PullQueue, applied, maxPerTick, "pull");
            }
        }

        private static void Flood<T>(ClientConnection client, System.Collections.Concurrent.ConcurrentQueue<T> q, int applied, int max, string label)
        {
            if (applied != max || q.IsEmpty) return;
            int dropped = 0;
            while (q.TryDequeue(out _)) dropped++;
            Console.WriteLine($"[Server] {label} flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
        }

        // Toggle: тот же NetId — отпустить. Иначе валидация грэба (world/reach/pullable/hands/not-taken),
        // затем swap A→B = release старого + grab нового — ровно один скейл скорости, без стека.
        private void HandlePull(ClientConnection client, int netId)
        {
            if (client.PulledNetId == netId)
            {
                Release(client);
                return;
            }

            if (_server.BlockWorld == null) return;
            if (!_entities.TryGetValue(netId, out var e) || e is not GroundItemEntity gi) return;

            int px = (int)MathF.Floor(client.X);
            int py = (int)MathF.Floor(client.Y);
            if (!_server.Reachable(px, py, client.Z, (int)MathF.Floor(gi.CellX), (int)MathF.Floor(gi.CellY), (int)MathF.Floor(gi.Z))) return;

            var p = _inventory.ProtoLookup(gi.ItemDefId);
            if (!p.HasValue || !p.Value.Pullable) return;

            var hand = client.Slots[(int)SlotCategory.Hand];
            if (hand[0].NetId != 0 || hand[1].NetId != 0) return;

            foreach (var other in _clients.Values)
                if (other.PulledNetId == netId) return;

            if (client.PulledNetId != 0) Release(client);
            client.PulledNetId = netId;
            client.AddSpeedScale(0.5f);
            _server.SendToClient(client, new PullSync { PulledNetId = netId, ItemDefId = gi.ItemDefId });
        }

        private void Release(ClientConnection client, bool notify = true)
        {
            if (client.PulledNetId == 0) return;
            client.PulledNetId = 0;
            client.RemoveSpeedScale(0.5f);
            if (notify)
                _server.SendToClient(client, new PullSync { PulledNetId = 0, ItemDefId = 0 });
        }

        /// <summary>Снимает штраф скорости без нотификации — пир уже недоступен; ящик остаётся в мире.</summary>
        public void OnClientDisconnect(ClientConnection client) => Release(client, notify: false);

        /// <summary>Продвигает тянущиеся ящики к своим игрокам за тик (после движения игроков).</summary>
        public void ProcessFollow()
        {
            if (_server.BlockWorld == null) return;
            foreach (var client in _clients.Values)
                if (client.PulledNetId != 0)
                    FollowOne(client);
        }

        // Ящик держит FollowDistance от игрока; за LeashMax/HeightLeash — release, ящик остаётся free-float на месте (F1).
        private void FollowOne(ClientConnection client)
        {
            if (!_entities.TryGetValue(client.PulledNetId, out var e) || e is not GroundItemEntity gi)
            {
                Release(client);
                return;
            }

            var p = _inventory.ProtoLookup(gi.ItemDefId);
            float halfW = 0.5f, boxHeight = 1f, feetOffset = 0f;
            if (p.HasValue && p.Value.HasCollision)
            {
                var box = p.Value.CollisionBox;
                halfW = (box.MaxXf - box.MinXf) * 0.5f;
                boxHeight = box.MaxYf - box.MinYf;
                feetOffset = box.MinYf;
            }

            float dx = client.Mover.X - gi.X;
            float dz = client.Mover.Z - gi.Y;
            float distSq = dx * dx + dz * dz;

            if (distSq > LeashMax * LeashMax || MathF.Abs(client.Mover.Y - gi.Z) > HeightLeash)
            {
                Release(client);
                return;
            }

            if (distSq <= FollowDistance * FollowDistance) return;

            float dist = MathF.Sqrt(distSq);
            float step = MathF.Min(dist - FollowDistance, MovementLogic.StepPerTick);
            float mx = dx / dist * step;
            float mz = dz / dist * step;

            float nx = gi.X;
            float nz = gi.Y;
            float feetY = gi.Z + feetOffset;

            // X→Z раздельно (sweep), высота Y ящика не меняется при следовании.
            if (mx != 0f && !FollowBlocked(nx + mx, feetY, nz, halfW, boxHeight, gi.NetId))
                nx += mx;
            if (mz != 0f && !FollowBlocked(nx, feetY, nz + mz, halfW, boxHeight, gi.NetId))
                nz += mz;

            if (nx == gi.X && nz == gi.Y) return;
            _ground.MoveGroundItem(gi.NetId, nx, nz, gi.Z);
        }

        // Блоки мира + AABB других предметов с коллизией + AABB игроков (себя исключаем по NetId).
        private bool FollowBlocked(float centerX, float feetY, float centerZ, float halfW, float boxHeight, int selfNetId)
        {
            if (BlockMovementLogic.CollidesBox(_server.BlockWorld!, _server.BlockShapes, centerX, feetY, centerZ, halfW, boxHeight))
                return true;

            float minX = centerX - halfW, maxX = centerX + halfW;
            float minZ = centerZ - halfW, maxZ = centerZ + halfW;
            float maxY = feetY + boxHeight;

            foreach (var e in _entities.Values)
            {
                if (e is not GroundItemEntity other || other.NetId == selfNetId) continue;
                var op = _inventory.ProtoLookup(other.ItemDefId);
                if (!op.HasValue || !op.Value.HasCollision) continue;
                var ob = op.Value.CollisionBox;
                float oHalfW = (ob.MaxXf - ob.MinXf) * 0.5f;
                float oMinX = other.X - oHalfW, oMaxX = other.X + oHalfW;
                float oMinZ = other.Y - oHalfW, oMaxZ = other.Y + oHalfW;
                float oMinY = other.Z + ob.MinYf, oMaxY = other.Z + ob.MaxYf;
                if (minX < oMaxX && maxX > oMinX && minZ < oMaxZ && maxZ > oMinZ && feetY < oMaxY && maxY > oMinY)
                    return true;
            }

            foreach (var other in _clients.Values)
            {
                float pMinX = other.Mover.X - BlockMovementConfig.HalfWidth;
                float pMaxX = other.Mover.X + BlockMovementConfig.HalfWidth;
                float pMinZ = other.Mover.Z - BlockMovementConfig.HalfWidth;
                float pMaxZ = other.Mover.Z + BlockMovementConfig.HalfWidth;
                float pMinY = other.Mover.Y;
                float pMaxY = other.Mover.Y + BlockMovementConfig.StandHeight;
                if (minX < pMaxX && maxX > pMinX && minZ < pMaxZ && maxZ > pMinZ && feetY < pMaxY && maxY > pMinY)
                    return true;
            }
            return false;
        }
    }
}
