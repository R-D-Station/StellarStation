using LiteNetLib;
using Shared.Configs;
using Shared.Messages.Core;
using Shared.Simulation.Blocks;
using Shared.World;
using Shared.World.Blocks;
using Shared.World.Items;
using Server.Network;

namespace Server.Items
{
    public sealed class GroundItemWorld
    {
        private readonly Dictionary<int, IWorldEntity> _entities;
        private readonly NetIdAllocator _netIdAllocator;
        private readonly Dictionary<NetPeer, ClientConnection> _clients;
        private readonly Shared.Simulation.Blocks.DynamicObstacleSet _obstacles = new();

        public Func<ushort, ItemProto?> ProtoLookup = defId =>
            ItemCatalogData.TryGet(defId, out var p) ? p : (ItemProto?)null;

        public Shared.Simulation.Blocks.IDynamicObstacles Obstacles => _obstacles;

        public GroundItemWorld(Dictionary<int, IWorldEntity> entities, NetIdAllocator netIdAllocator,
            Dictionary<NetPeer, ClientConnection> clients)
        {
            _entities = entities;
            _netIdAllocator = netIdAllocator;
            _clients = clients;
        }

        public int SpawnGroundItem(ushort itemDefId, byte stackCount, float cellX, float cellY, float z, byte placement = 0)
        {
            int id = _netIdAllocator.Allocate();
            _entities[id] = new GroundItemEntity(id, itemDefId, stackCount, cellX, cellY, z, placement);
            return id;
        }

        public void SpawnGroundItemWithId(int netId, ushort itemDefId, byte stackCount, float cellX, float cellY, float z, byte placement = 0)
        {
            if (_entities.ContainsKey(netId))
                throw new InvalidOperationException($"NetId {netId} already lives in _entities — double drop/spawn?");
            _entities[netId] = new GroundItemEntity(netId, itemDefId, stackCount, cellX, cellY, z, placement);
        }

        public bool DespawnGroundItem(int netId)
        {
            if (_entities.TryGetValue(netId, out var e) && e is GroundItemEntity)
                return _entities.Remove(netId);
            return false;
        }

        public bool MoveGroundItem(int netId, float x, float y, float z)
        {
            if (_entities.TryGetValue(netId, out var e) && e is GroundItemEntity gi)
            {
                gi.MoveTo(x, y, z);
                return true;
            }
            return false;
        }

        public void RebuildObstacles()
        {
            _obstacles.Clear();
            foreach (var entity in _entities.Values)
            {
                if (entity is not GroundItemEntity gi) continue;
                var p = ProtoLookup(gi.ItemDefId);
                if (!p.HasValue || !p.Value.HasCollision) continue;
                var box = p.Value.CollisionBox;
                float halfW = (box.MaxXf - box.MinXf) * 0.5f;
                float height = box.MaxYf - box.MinYf;
                _obstacles.Add(gi.X, gi.Z + box.MinYf, gi.Y, halfW, height);
            }
        }

        public bool TryFindItemAnyLocation(int netId, out ItemLocationKind location, out ushort itemDefId, out byte stackCount)
        {
            if (_entities.TryGetValue(netId, out var e) && e is GroundItemEntity gi)
            {
                location = ItemLocationKind.Ground;
                itemDefId = gi.ItemDefId;
                stackCount = gi.StackCount;
                return true;
            }

            foreach (var client in _clients.Values)
            {
                for (int c = 0; c < client.Slots.Length; c++)
                {
                    var arr = client.Slots[c];
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (arr[i].NetId != netId) continue;
                        location = ItemLocationKind.Held;
                        itemDefId = arr[i].ItemDefId;
                        stackCount = arr[i].StackCount;
                        return true;
                    }
                }
            }

            location = default;
            itemDefId = 0;
            stackCount = 0;
            return false;
        }

        public List<ItemInstance> GroundItemsInInterest(float cx, float cy, int cz)
        {
            float r = SVars.Instance.EntityInterestRadius;
            int zDepth = SVars.Instance.EntityInterestZDepth;
            var result = new List<ItemInstance>();
            foreach (var entity in _entities.Values)
            {
                if (entity is not GroundItemEntity gi) continue;
                var probe = new EntitySnapshot { X = gi.X, Y = gi.Y, Z = (int)MathF.Floor(gi.Z) };
                if (GameServer.InInterest(cx, cy, cz, in probe, r, zDepth))
                    result.Add(ToInstance(gi));
            }
            return result;
        }

        public void SpawnMapItems(BlockGrid world, IBlockShapes shapes)
        {
            var mapItems = world.ItemSpawns;
            for (int i = 0; i < mapItems.Count; i++)
            {
                int restY = ItemGroundSnap.SnapDown(world, shapes, mapItems[i].X, mapItems[i].Y, mapItems[i].Z);
                SpawnGroundItem(mapItems[i].DefId, mapItems[i].Stack, mapItems[i].X + 0.5f, mapItems[i].Z + 0.5f, restY);
            }
            Console.WriteLine($"[Map] spawned {mapItems.Count} map items");
        }

        public static ItemInstance ToInstance(GroundItemEntity gi) => new ItemInstance
        {
            NetId = gi.NetId,
            ItemDefId = gi.ItemDefId,
            StackCount = gi.StackCount,
            X = gi.CellX,
            Y = gi.CellY,
            Z = gi.Z,
            Placement = gi.Placement
        };
    }
}
