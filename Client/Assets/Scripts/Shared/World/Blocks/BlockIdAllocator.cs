using System.Collections.Generic;

namespace Shared.World.Blocks
{
    /// <summary>Авто-выдача id блока: наименьший свободный 1..65535; 0 = «не назначен»/нет свободных.
    /// Write-once: назначенный id запечён в карты v10 — никогда не перенумеровывать.</summary>
    public static class BlockIdAllocator
    {
        public static ushort SmallestFreeId(IEnumerable<ushort> usedIds)
        {
            var used = new HashSet<ushort>();
            if (usedIds != null)
                foreach (var id in usedIds)
                    used.Add(id);

            for (int candidate = 1; candidate <= ushort.MaxValue; candidate++)
                if (!used.Contains((ushort)candidate))
                    return (ushort)candidate;

            return 0; // все заняты
        }

        /// <summary>Занят ли ненулевой candidate среди otherIds (0 никогда не «занят» — маркер «не назначен»).</summary>
        public static bool IsTaken(ushort candidate, IEnumerable<ushort> otherIds)
        {
            if (candidate == 0 || otherIds == null)
                return false;
            foreach (var id in otherIds)
                if (id == candidate)
                    return true;
            return false;
        }
    }
}
