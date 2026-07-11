using System.Collections.Generic;

namespace Shared.World.Blocks
{
    /// <summary>
    /// Палитра секции: список встречающихся BlockType, индекс — byte в массиве секции.
    /// Слот 0 всегда Air; палитра только растёт (сжатие не требуется). Лимит — 256 записей.
    /// </summary>
    public sealed class BlockPalette
    {
        public const int MaxEntries = 256;

        private readonly List<ushort> _types = new() { 0 };
        private readonly Dictionary<ushort, int> _lookup = new() { [0] = 0 };

        public int Count => _types.Count;

        public ushort TypeAt(int index) => _types[index];

        /// <summary>Индекс типа в палитре; -1, если типа нет.</summary>
        public int IndexOf(ushort type) => _lookup.TryGetValue(type, out int i) ? i : -1;

        /// <summary>Добавить тип (должен отсутствовать); -1 при переполнении — секции пора в raw-режим.</summary>
        public int Add(ushort type)
        {
            if (_types.Count >= MaxEntries)
                return -1;
            _types.Add(type);
            _lookup[type] = _types.Count - 1;
            return _types.Count - 1;
        }

        /// <summary>Прямой доступ для сериализации (порядок вставки = порядок на диске).</summary>
        public IReadOnlyList<ushort> Types => _types;
    }
}
