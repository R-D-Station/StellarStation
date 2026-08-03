using System;
using System.Collections.Generic;

namespace Shared.World.Blocks
{
    /// <summary>Детерминированный резолв опоры блока (engine-agnostic) и снос неопёртых при загрузке карты.</summary>
    public static class BlockAttach
    {
        private static readonly int[] WDX = { 1, -1, 0, 0 };
        private static readonly int[] WDZ = { 0, 0, 1, -1 };
        // facing ОТ стены к блоку; индексы согласованы с BlockState.GetFacing (0=+Z/1=+X/2=−Z/3=−X) и MultiBlockVisual.FacingRotation
        private static readonly int[] WFacing = { 3, 1, 2, 0 };

        /// <summary>Опора по умолчанию (тип-уровень): есть коллизия ХОТЬ У ОДНОЙ части и блок не открывается.</summary>
        public static bool DefaultIsSolid(ushort type)
        {
            if (type == 0)
                return false;
            var info = BlockCatalog.Get(type);
            return info.HasCollision && !info.Openable;
        }

        /// <summary>Опора по КЛЕТКЕ: коллизия конкретной части (клетки мульти-блока), не открывается — проём двери опорой не служит.</summary>
        public static bool DefaultIsSolid(BlockGrid grid, int x, int y, int z)
        {
            ushort type = grid.GetBlock(x, y, z);
            if (type == 0)
                return false;
            var info = BlockCatalog.Get(type);
            if (info.Openable)
                return false;
            int part = MultiBlock.PartAt(grid, info.SizeX, info.SizeY, info.SizeZ,
                                         BlockState.GetFacing(grid.GetState(x, y, z)), x, y, z);
            return part >= 0 && info.PartHasCollision(part);
        }

        /// <summary>Ищет первую доступную поверхность по приоритету <paramref name="priority"/>; Wall/AnySolid-гориз выставляет facing от стены.</summary>
        public static bool Resolve(BlockGrid grid, Func<ushort, bool> isSolid, int x, int y, int z,
                                   AttachSurface[] priority, out AttachSurface surface, out int facing)
        {
            surface = AttachSurface.Floor;
            facing = 0;
            if (grid == null || isSolid == null || priority == null)
                return false;

            for (int i = 0; i < priority.Length; i++)
            {
                switch (priority[i])
                {
                    case AttachSurface.Floor:
                        if (isSolid(grid.GetBlock(x, y - 1, z))) { surface = AttachSurface.Floor; return true; }
                        break;
                    case AttachSurface.Ceiling:
                        if (isSolid(grid.GetBlock(x, y + 1, z))) { surface = AttachSurface.Ceiling; return true; }
                        break;
                    case AttachSurface.Wall:
                        for (int d = 0; d < 4; d++)
                            if (isSolid(grid.GetBlock(x + WDX[d], y, z + WDZ[d])))
                            {
                                surface = AttachSurface.Wall;
                                facing = WFacing[d];
                                return true;
                            }
                        break;
                    case AttachSurface.AnySolid:
                        for (int d = 0; d < 4; d++)
                            if (isSolid(grid.GetBlock(x + WDX[d], y, z + WDZ[d])))
                            {
                                surface = AttachSurface.AnySolid;
                                facing = WFacing[d];
                                return true;
                            }
                        if (isSolid(grid.GetBlock(x, y - 1, z))) { surface = AttachSurface.AnySolid; return true; }
                        if (isSolid(grid.GetBlock(x, y + 1, z))) { surface = AttachSurface.AnySolid; return true; }
                        break;
                }
            }
            return false;
        }

        private static readonly Func<ushort, bool> CatalogRequiresSupport = t => BlockCatalog.Get(t).RequiresSupport;
        private static readonly Func<ushort, AttachSurface[]> CatalogAttachTo = t => BlockCatalog.Get(t).AttachTo;

        /// <summary>ValidateAll с правилами опоры из BlockCatalog (боевой путь сервера).</summary>
        public static int ValidateAll(BlockGrid grid, Func<ushort, bool> isSolid)
            => ValidateAll(grid, isSolid, CatalogRequiresSupport, CatalogAttachTo);

        /// <summary>Сносит все RequiresSupport-блоки без опоры, повторяя проход до фикспойнта (каскад: опора ушла → зависимый тоже падает); возвращает число снесённых.</summary>
        public static int ValidateAll(BlockGrid grid, Func<ushort, bool> isSolid,
                                      Func<ushort, bool> requiresSupport, Func<ushort, AttachSurface[]> attachTo)
        {
            if (grid == null || isSolid == null || requiresSupport == null || attachTo == null)
                return 0;

            int removed = 0;
            var keys = new List<long>();
            var toRemove = new List<(int x, int y, int z, bool multi)>();
            var seenAnchors = new HashSet<(int, int, int)>();
            bool changed = true;
            while (changed) // повтор, пока снос текущего прохода порождает новые неопёртые блоки
            {
                changed = false;
                keys.Clear();
                keys.AddRange(grid.Sections.Keys);
                keys.Sort(CompareKeys); // фиксированный порядок секций (Y,Z,X) — снос детерминирован между запусками
                toRemove.Clear();
                seenAnchors.Clear();

                foreach (long key in keys)
                {
                    var s = grid.Sections[key];
                    BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                    for (int li = 0; li < ChunkSection.BlockCount; li++)
                    {
                        ushort t = s.GetBlock(li);
                        if (t == 0 || !requiresSupport(t))
                            continue;
                        int x = cx * 16 + (li & 15);
                        int y = cy * 16 + ((li >> 4) & 15);
                        int z = cz * 16 + ((li >> 8) & 15);

                        if (grid.AnchorOf(x, y, z, out int ax, out int ay, out int az)) // мульти-блок: опору решаем ПО ЯКОРЮ, снос — целиком
                        {
                            if (!seenAnchors.Add((ax, ay, az))) // структуру за проход решаем один раз, внутренние клетки не участвуют
                                continue;
                            if (!Resolve(grid, isSolid, ax, ay, az, attachTo(t), out _, out _))
                                toRemove.Add((ax, ay, az, true));
                        }
                        else if (!Resolve(grid, isSolid, x, y, z, attachTo(t), out _, out _))
                            toRemove.Add((x, y, z, false));
                    }
                }

                for (int i = 0; i < toRemove.Count; i++)
                {
                    var p = toRemove[i];
                    bool did = p.multi ? grid.EraseMultiBlock(p.x, p.y, p.z) : grid.SetBlock(p.x, p.y, p.z, 0);
                    if (did)
                    {
                        removed++;
                        changed = true;
                    }
                }
            }
            return removed;
        }

        private static int CompareKeys(long a, long b)
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
