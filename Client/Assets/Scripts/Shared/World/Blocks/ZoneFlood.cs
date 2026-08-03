using System.Collections.Generic;

namespace Shared.World.Blocks
{
    /// <summary>Инъекция предикатов классификации блоков для <see cref="ZoneFlood"/> (юниттесты без каталога).</summary>
    public interface IZoneClassifier
    {
        bool IsSolid(ushort type);

        /// <summary>Солидность КОНКРЕТНОЙ клетки мульти-блока; дефолт игнорирует координаты (тестовые классификаторы — тип-уровень).</summary>
        bool IsSolid(BlockGrid grid, ushort type, int x, int y, int z) => IsSolid(type);

        bool IsDoor(ushort type);
        bool IsFloorAnchor(ushort type);
        bool IsDivider(ushort type);
        bool IsMergeMarker(ushort type);
    }

    /// <summary>Боевой классификатор поверх <see cref="BlockCatalog"/>: солид = коллизия части и не открывается.</summary>
    public sealed class CatalogZoneClassifier : IZoneClassifier
    {
        public static readonly CatalogZoneClassifier Instance = new CatalogZoneClassifier();

        public bool IsSolid(ushort type)
        {
            var info = BlockCatalog.Get(type);
            return info.HasCollision && !info.Openable;
        }

        public bool IsSolid(BlockGrid grid, ushort type, int x, int y, int z)
        {
            var info = BlockCatalog.Get(type);
            if (info.Openable) return false;
            int part = MultiBlock.PartAt(grid, info.SizeX, info.SizeY, info.SizeZ,
                                         BlockState.GetFacing(grid.GetState(x, y, z)), x, y, z);
            return part >= 0 && info.PartHasCollision(part);
        }

        public bool IsDoor(ushort type) => BlockCatalog.Get(type).Openable;
        public bool IsFloorAnchor(ushort type) => BlockCatalog.Get(type).IsFloorAnchor;
        public bool IsDivider(ushort type) => BlockCatalog.Get(type).Category == BlockCategory.Divider;
        public bool IsMergeMarker(ushort type) => BlockCatalog.Get(type).Category == BlockCategory.MergeMarker;
    }

    /// <summary>Сид зоны с мировой позицией (для метаданных результата флуда).</summary>
    public sealed class ZoneSeedAt
    {
        public BlockCoord Pos;
        public FloorSeed Seed;
    }

    /// <summary>Одна вычисленная зона: метаданные (имя/ранг/этаж — от сида минимального ранга) + все её сиды.</summary>
    public sealed class ZoneRecord
    {
        public ushort Id;
        public string Name;
        public int Rank;
        public int Floor;
        public readonly List<ZoneSeedAt> Seeds = new();
    }

    /// <summary>Дверь-ворота между разноэтажными зонами (не слита) — граница, видимая клиенту/логике перехода.</summary>
    public sealed class ZoneJunction
    {
        public BlockCoord Cell;
        public readonly List<BlockCoord> DoorCells = new();
        public readonly List<ushort> Zones = new();
    }

    /// <summary>Одна зона засеяна сидами с разными номерами этажей одновременно (диагностика, не блокирует флуд).</summary>
    public sealed class ZoneConflict
    {
        public ushort ZoneId;
        public readonly List<int> Floors = new();
    }

    /// <summary>Засеянная зона имеет открытый путь наружу (дыра в космос без двери) — диагностика авторинга.</summary>
    public sealed class ZoneLeak
    {
        public ushort ZoneId;
        public BlockCoord Cell;
    }

    /// <summary>Итог <see cref="ZoneFlood.Recompute"/>: вычисленные зоны + стыки-двери + конфликты сидов + утечки наружу.</summary>
    public sealed class ZoneFloodResult
    {
        public readonly List<ZoneRecord> Zones = new();
        public readonly List<ZoneJunction> Junctions = new();
        public readonly List<ZoneConflict> Conflicts = new();
        public readonly List<ZoneLeak> Leaks = new();
    }

    /// <summary>Детерминированный полный пересчёт зон-этажей по проходимым регионам (двери-ворота через union-find).</summary>
    public static class ZoneFlood
    {
        /// <summary>Зона «улицы»/космоса: регионы, касающиеся границы прогруза. Отличает «снаружи» от zone==0 («зоны нет»).</summary>
        public const ushort ExteriorZoneId = 0xFFFF;

        private const byte KindWall = 0;
        private const byte KindPassable = 1;
        private const byte KindGate = 2;

        private static readonly int[] DirX = { 1, -1, 0, 0, 0, 0 };
        private static readonly int[] DirY = { 0, 0, 1, -1, 0, 0 };
        private static readonly int[] DirZ = { 0, 0, 0, 0, 1, -1 };

        /// <summary>Полный ресет и пересчёт зон грида: BFS проходимых регионов, слияние через двери-ворота
        /// (одноэтажные — тихо, разноэтажные — <see cref="ZoneJunction"/>), запись ZoneId в грид.</summary>
        public static ZoneFloodResult Recompute(BlockGrid grid, IZoneClassifier classifier)
        {
            var result = new ZoneFloodResult();

            var sectionKeys = new List<long>(grid.Sections.Keys);
            sectionKeys.Sort(CompareSectionKeys);

            ClearZones(grid, sectionKeys);

            var cellRegion = new Dictionary<long, int>();
            var regionCells = new List<List<BlockCoord>>();
            var regionOutside = new List<bool>();
            var regionOutsideCell = new List<BlockCoord>();
            var bfs = new Queue<BlockCoord>();

            foreach (long key in sectionKeys)
            {
                BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                for (int li = 0; li < ChunkSection.BlockCount; li++)
                {
                    int x = cx * ChunkSection.Size + (li & 15);
                    int y = cy * ChunkSection.Size + ((li >> 4) & 15);
                    int z = cz * ChunkSection.Size + ((li >> 8) & 15);
                    if (cellRegion.ContainsKey(CellKey(x, y, z)))
                        continue;
                    if (Classify(grid, classifier, x, y, z) != KindPassable)
                        continue;

                    int id = regionCells.Count;
                    var cells = new List<BlockCoord>();
                    regionCells.Add(cells);
                    regionOutside.Add(false);
                    regionOutsideCell.Add(default);
                    cellRegion[CellKey(x, y, z)] = id;
                    bfs.Enqueue(new BlockCoord(x, y, z));
                    while (bfs.Count > 0)
                    {
                        var c = bfs.Dequeue();
                        cells.Add(c);
                        for (int d = 0; d < 6; d++)
                        {
                            int nx = c.X + DirX[d], ny = c.Y + DirY[d], nz = c.Z + DirZ[d];
                            long nk = CellKey(nx, ny, nz);
                            if (cellRegion.ContainsKey(nk))
                                continue;
                            byte kind = Classify(grid, classifier, nx, ny, nz, out bool outside);
                            if (outside && !regionOutside[id])
                            {
                                regionOutside[id] = true;
                                regionOutsideCell[id] = c;
                            }
                            if (kind != KindPassable)
                                continue;
                            cellRegion[nk] = id;
                            bfs.Enqueue(new BlockCoord(nx, ny, nz));
                        }
                    }
                }
            }

            var seedList = new List<ZoneSeedAt>();
            var seedRegion = new List<int>();
            var seedLis = new List<int>();
            foreach (long key in sectionKeys)
            {
                var s = grid.Sections[key];
                if (s.Seeds.Count == 0)
                    continue;
                BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                seedLis.Clear();
                seedLis.AddRange(s.Seeds.Keys);
                seedLis.Sort();
                foreach (int li in seedLis)
                {
                    int x = cx * ChunkSection.Size + (li & 15);
                    int y = cy * ChunkSection.Size + ((li >> 4) & 15);
                    int z = cz * ChunkSection.Size + ((li >> 8) & 15);
                    s.TryGetSeed(li, out var seed);
                    seedList.Add(new ZoneSeedAt { Pos = new BlockCoord(x, y, z), Seed = seed });
                    seedRegion.Add(RegionOfSeed(cellRegion, x, y, z));
                }
            }

            var gateCells = new List<List<BlockCoord>>();
            var gateRegions = new List<List<int>>();
            var gateSeen = new HashSet<long>();
            foreach (long key in sectionKeys)
            {
                BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                for (int li = 0; li < ChunkSection.BlockCount; li++)
                {
                    int x = cx * ChunkSection.Size + (li & 15);
                    int y = cy * ChunkSection.Size + ((li >> 4) & 15);
                    int z = cz * ChunkSection.Size + ((li >> 8) & 15);
                    long ck = CellKey(x, y, z);
                    if (gateSeen.Contains(ck))
                        continue;
                    if (Classify(grid, classifier, x, y, z) != KindGate)
                        continue;

                    var cells = new List<BlockCoord>();
                    var adj = new List<int>();
                    gateSeen.Add(ck);
                    bfs.Enqueue(new BlockCoord(x, y, z));
                    while (bfs.Count > 0)
                    {
                        var c = bfs.Dequeue();
                        cells.Add(c);
                        for (int d = 0; d < 6; d++)
                        {
                            int nx = c.X + DirX[d], ny = c.Y + DirY[d], nz = c.Z + DirZ[d];
                            long nk = CellKey(nx, ny, nz);
                            if (cellRegion.TryGetValue(nk, out int r))
                            {
                                if (!adj.Contains(r))
                                    adj.Add(r);
                                continue;
                            }
                            if (gateSeen.Contains(nk))
                                continue;
                            if (Classify(grid, classifier, nx, ny, nz) != KindGate)
                                continue;
                            gateSeen.Add(nk);
                            bfs.Enqueue(new BlockCoord(nx, ny, nz));
                        }
                    }
                    adj.Sort();
                    gateCells.Add(cells);
                    gateRegions.Add(adj);
                }
            }

            int n = regionCells.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++)
                parent[i] = i;

            int Find(int a)
            {
                while (parent[a] != a)
                {
                    parent[a] = parent[parent[a]];
                    a = parent[a];
                }
                return a;
            }

            void Union(int a, int b) // корень = меньший индекс — детерминизм итога не зависит от порядка union
            {
                a = Find(a);
                b = Find(b);
                if (a == b)
                    return;
                if (a < b)
                    parent[b] = a;
                else
                    parent[a] = b;
            }

            List<int> FloorsOfRoot(int root)
            {
                var floors = new List<int>();
                for (int i = 0; i < seedList.Count; i++)
                {
                    if (seedRegion[i] < 0 || Find(seedRegion[i]) != root)
                        continue;
                    int f = seedList[i].Seed.Floor;
                    if (!floors.Contains(f))
                        floors.Add(f);
                }
                return floors;
            }

            var rootOutside = new bool[n];
            for (int i = 0; i < n; i++)
                rootOutside[i] = regionOutside[i];

            // Фикспойнт: слияние через одну дверь меняет floor-набор корня → следующий проход может слить ещё дверь.
            var isJunction = new bool[gateCells.Count];
            var roots = new List<int>();
            var gateFloors = new List<int>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int g = 0; g < gateCells.Count; g++)
                {
                    roots.Clear();
                    foreach (int r in gateRegions[g])
                    {
                        int root = Find(r);
                        if (!roots.Contains(root))
                            roots.Add(root);
                    }
                    if (roots.Count < 2)
                        continue;

                    bool anyOutside = false;
                    foreach (int root in roots)
                        if (rootOutside[root])
                        {
                            anyOutside = true;
                            break;
                        }
                    if (anyOutside)
                        continue;

                    gateFloors.Clear();
                    bool anySeeded = false;
                    foreach (int root in roots)
                    {
                        var f = FloorsOfRoot(root);
                        if (f.Count > 0)
                            anySeeded = true;
                        foreach (int fl in f)
                            if (!gateFloors.Contains(fl))
                                gateFloors.Add(fl);
                    }
                    if (!anySeeded)
                        continue;

                    if (gateFloors.Count == 1)
                    {
                        for (int i = 1; i < roots.Count; i++)
                            Union(roots[0], roots[i]);
                        changed = true;
                    }
                    else
                    {
                        isJunction[g] = true;
                    }
                }
            }

            var zoneOfRoot = new Dictionary<int, ushort>();
            var rootOrder = new List<int>();
            for (int i = 0; i < n; i++)
                if (Find(i) == i && FloorsOfRoot(i).Count > 0)
                    rootOrder.Add(i);
            ushort nextId = 1;
            for (int i = 0; i < rootOrder.Count; i++)
            {
                if (nextId >= ExteriorZoneId)
                {
                    rootOrder.RemoveRange(i, rootOrder.Count - i);
                    break;
                }
                zoneOfRoot[rootOrder[i]] = nextId++;
            }

            foreach (int root in rootOrder)
            {
                var rec = new ZoneRecord { Id = zoneOfRoot[root] };
                int best = -1;
                for (int i = 0; i < seedList.Count; i++)
                {
                    if (seedRegion[i] < 0 || Find(seedRegion[i]) != root)
                        continue;
                    rec.Seeds.Add(seedList[i]);
                    if (best < 0 || seedList[i].Seed.Rank < seedList[best].Seed.Rank)
                        best = i;
                }
                rec.Name = seedList[best].Seed.Name;
                rec.Rank = seedList[best].Seed.Rank;
                rec.Floor = seedList[best].Seed.Floor;
                result.Zones.Add(rec);

                var floors = FloorsOfRoot(root);
                if (floors.Count > 1)
                {
                    floors.Sort();
                    var conflict = new ZoneConflict { ZoneId = rec.Id };
                    conflict.Floors.AddRange(floors);
                    result.Conflicts.Add(conflict);
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (!zoneOfRoot.TryGetValue(Find(i), out ushort zid))
                {
                    if (!regionOutside[i])
                        continue;
                    zid = ExteriorZoneId;
                }
                var cells = regionCells[i];
                for (int c = 0; c < cells.Count; c++)
                    grid.SetZone(cells[c].X, cells[c].Y, cells[c].Z, zid);
            }

            var gateZones = new List<ushort>();
            for (int g = 0; g < gateCells.Count; g++)
            {
                gateZones.Clear();
                foreach (int r in gateRegions[g])
                    if (zoneOfRoot.TryGetValue(Find(r), out ushort z2) && !gateZones.Contains(z2))
                        gateZones.Add(z2);

                if (isJunction[g])
                {
                    var j = new ZoneJunction { Cell = gateCells[g][0] };
                    j.DoorCells.AddRange(gateCells[g]);
                    gateZones.Sort();
                    j.Zones.AddRange(gateZones);
                    result.Junctions.Add(j);
                }
                else if (gateZones.Count == 1)
                {
                    bool exterior = false;
                    foreach (int r in gateRegions[g])
                        if (regionOutside[r])
                        {
                            exterior = true;
                            break;
                        }
                    if (exterior)
                        continue;

                    var cells = gateCells[g];
                    for (int c = 0; c < cells.Count; c++)
                        grid.SetZone(cells[c].X, cells[c].Y, cells[c].Z, gateZones[0]);
                }
            }

            for (int i = 0; i < regionCells.Count; i++)
            {
                if (!regionOutside[i] || !zoneOfRoot.TryGetValue(Find(i), out ushort leakZone))
                    continue;
                result.Leaks.Add(new ZoneLeak { ZoneId = leakZone, Cell = regionOutsideCell[i] });
            }

            foreach (long key in sectionKeys)
                grid.Sections[key].CompactZones();

            return result;
        }

        private static void ClearZones(BlockGrid grid, List<long> sectionKeys)
        {
            foreach (long key in sectionKeys)
                grid.Sections[key].ResetZones();
        }

        private static int RegionOfSeed(Dictionary<long, int> cellRegion, int x, int y, int z)
        {
            if (cellRegion.TryGetValue(CellKey(x, y, z), out int own))
                return own;
            for (int d = 0; d < 6; d++)
                if (cellRegion.TryGetValue(CellKey(x + DirX[d], y + DirY[d], z + DirZ[d]), out int r))
                    return r;
            return -1;
        }

        private static byte Classify(BlockGrid grid, IZoneClassifier cls, int x, int y, int z)
            => Classify(grid, cls, x, y, z, out _);

        // Приоритет: bake-биты (ручная разметка редактора) перебивают категорию блока — Divider/Merge проверяются раньше.
        // outside — клетка за границей мира (нет секции), т.е. «космос»: результат классификации тот же KindWall.
        private static byte Classify(BlockGrid grid, IZoneClassifier cls, int x, int y, int z, out bool outside)
        {
            outside = false;
            if (!BlockGrid.InBounds(y))
                return KindWall;
            ushort t = grid.GetBlock(x, y, z);
            if (t == 0 && !HasSection(grid, x, y, z))
            {
                outside = true;
                return KindWall; // за границей мира (нет секции) флуд не течёт
            }
            byte bake = grid.GetBake(x, y, z);
            if ((bake & ChunkSection.BakeDivider) != 0)
                return KindWall;
            if ((bake & ChunkSection.BakeMerge) != 0)
                return KindPassable;
            if (t == 0)
                return KindPassable;
            if (cls.IsDivider(t))
                return KindWall;
            if (cls.IsMergeMarker(t))
                return KindPassable;
            if (cls.IsDoor(t))
                return KindGate;
            if (cls.IsSolid(grid, t, x, y, z))
                return KindWall;
            return KindPassable;
        }

        private static bool HasSection(BlockGrid grid, int x, int y, int z)
            => grid.GetSection(Div(x), Div(y), Div(z)) != null;

        private static int Div(int a)
        {
            int q = a / ChunkSection.Size;
            if (a % ChunkSection.Size != 0 && a < 0)
                q--;
            return q;
        }

        private static long CellKey(int x, int y, int z)
            => ((long)(x & 0x1FFFFF)) | ((long)(y & 0x1FFFFF) << 21) | ((long)(z & 0x1FFFFF) << 42);

        private static int CompareSectionKeys(long a, long b)
        {
            BlockGrid.UnpackKey(a, out int ax, out int ay, out int az);
            BlockGrid.UnpackKey(b, out int bx, out int by, out int bz);
            int c = ay.CompareTo(by);
            if (c != 0)
                return c;
            c = az.CompareTo(bz);
            return c != 0 ? c : ax.CompareTo(bx);
        }
    }
}
