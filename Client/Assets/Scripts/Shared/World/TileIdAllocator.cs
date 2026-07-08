using System.Collections.Generic;

namespace Shared.World
{
    /// <summary>Авто-выдача id вида объекта (Floor/Structure): наименьший свободный 1..255; 0 = «не назначен»/нет свободных.</summary>
    public static class TileIdAllocator
    {
        /// <summary>Наименьший свободный id 1..255, отсутствующий в usedIds; 0, если все 1..255 заняты (сигнал «нет свободных»).</summary>
        public static byte SmallestFreeId(IEnumerable<byte> usedIds)
        {
            var used = new HashSet<byte>();
            if (usedIds != null)
                foreach (var id in usedIds)
                    used.Add(id);

            for (int candidate = 1; candidate <= 255; candidate++)
                if (!used.Contains((byte)candidate))
                    return (byte)candidate;

            return 0; // все 1..255 заняты
        }

        /// <summary>Занят ли ненулевой candidate среди otherIds (0 никогда не «занят» — это маркер «не назначен»).</summary>
        public static bool IsTaken(byte candidate, IEnumerable<byte> otherIds)
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
