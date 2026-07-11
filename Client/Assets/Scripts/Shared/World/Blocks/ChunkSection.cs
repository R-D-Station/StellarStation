using System.Collections.Generic;

namespace Shared.World.Blocks
{
    /// <summary>
    /// Секция 16×16×16 блоков: палитра + byte-индексы; при переполнении палитры (&gt;256 типов) —
    /// fallback в прямой ushort-массив. State-байты (см. <see cref="BlockState"/>) хранятся разреженно
    /// и существуют только у не-Air блоков. Локальный индекс: lx | ly&lt;&lt;4 | lz&lt;&lt;8 (Y — высота).
    /// </summary>
    public sealed class ChunkSection
    {
        public const int Size = 16;
        public const int BlockCount = Size * Size * Size;

        private BlockPalette _palette = new();
        private byte[] _indices = new byte[BlockCount];
        private ushort[] _raw; // null, пока палитры хватает
        private readonly Dictionary<int, byte> _states = new();
        private readonly Dictionary<int, byte> _bake = new(); // авторская разметка (потолок/пол-интерьер), v11
        private int _nonAirCount;

        /// <summary>Бейк-биты (запекание карты): нижняя грань — потолок.</summary>
        public const byte BakeCeiling = 1 << 0;
        /// <summary>Бейк-биты: верхняя грань — пол интерьера (комната станции/шатла).</summary>
        public const byte BakeInteriorFloor = 1 << 1;

        /// <summary>Секция целиком Air (state на Air не бывает) — хранить/слать её не нужно.</summary>
        public bool IsEmpty => _nonAirCount == 0;

        /// <summary>Секция в raw-режиме (палитра переполнилась).</summary>
        public bool IsRaw => _raw != null;

        public int NonAirCount => _nonAirCount;

        public static int LocalIndex(int lx, int ly, int lz) => lx | (ly << 4) | (lz << 8);

        public ushort GetBlock(int localIndex)
            => _raw != null ? _raw[localIndex] : _palette.TypeAt(_indices[localIndex]);

        /// <summary>Записать тип блока. true — если значение реально изменилось; Air стирает state позиции.</summary>
        public bool SetBlock(int localIndex, ushort type)
        {
            ushort old = GetBlock(localIndex);
            if (old == type)
                return false;

            if (_raw != null)
            {
                _raw[localIndex] = type;
            }
            else
            {
                int idx = _palette.IndexOf(type);
                if (idx < 0)
                    idx = _palette.Add(type);
                if (idx < 0)
                {
                    ConvertToRaw();
                    _raw[localIndex] = type;
                }
                else
                {
                    _indices[localIndex] = (byte)idx;
                }
            }

            if (old == 0) _nonAirCount++;
            if (type == 0)
            {
                _nonAirCount--;
                _states.Remove(localIndex);
                _bake.Remove(localIndex);
            }
            return true;
        }

        public byte GetBake(int localIndex) => _bake.TryGetValue(localIndex, out byte b) ? b : (byte)0;

        /// <summary>Записать бейк-байт (авторская разметка). true — если изменился; на Air запрещён.</summary>
        public bool SetBake(int localIndex, byte bake)
        {
            if (GetBlock(localIndex) == 0)
                return false;
            byte old = GetBake(localIndex);
            if (old == bake)
                return false;
            if (bake == 0)
                _bake.Remove(localIndex);
            else
                _bake[localIndex] = bake;
            return true;
        }

        public byte GetState(int localIndex) => _states.TryGetValue(localIndex, out byte s) ? s : (byte)0;

        /// <summary>Записать state-байт. true — если изменился; на Air-позиции state запрещён (false).</summary>
        public bool SetState(int localIndex, byte state)
        {
            if (GetBlock(localIndex) == 0)
                return false;

            byte old = GetState(localIndex);
            if (old == state)
                return false;

            if (state == 0)
                _states.Remove(localIndex);
            else
                _states[localIndex] = state;
            return true;
        }

        // Палитра переполнена: разворачиваем индексы в прямой ushort-массив, палитру отпускаем.
        private void ConvertToRaw()
        {
            var raw = new ushort[BlockCount];
            for (int i = 0; i < BlockCount; i++)
                raw[i] = _palette.TypeAt(_indices[i]);
            _raw = raw;
            _indices = null;
            _palette = null;
        }

        // --- Доступ сериализатора (порядок данных = порядок на диске, побайтовая стабильность) ---

        internal BlockPalette Palette => _palette;
        internal byte[] Indices => _indices;
        internal ushort[] Raw => _raw;
        internal Dictionary<int, byte> States => _states;
        internal Dictionary<int, byte> Bake => _bake;

        /// <summary>Собрать секцию из десериализованных данных (raw-режим: palette/indices = null).</summary>
        internal static ChunkSection FromData(BlockPalette palette, byte[] indices, ushort[] raw, Dictionary<int, byte> states,
                                              Dictionary<int, byte> bake = null)
        {
            var s = new ChunkSection();
            if (raw != null)
            {
                s._raw = raw;
                s._indices = null;
                s._palette = null;
            }
            else
            {
                s._palette = palette;
                s._indices = indices;
            }

            s._nonAirCount = 0;
            for (int i = 0; i < BlockCount; i++)
                if (s.GetBlock(i) != 0)
                    s._nonAirCount++;

            foreach (var kv in states)
                if (s.GetBlock(kv.Key) != 0) // битый файл: state на Air-позиции отбрасываем (инвариант секции)
                    s._states[kv.Key] = kv.Value;
            if (bake != null)
                foreach (var kv in bake)
                    if (s.GetBlock(kv.Key) != 0)
                        s._bake[kv.Key] = kv.Value;
            return s;
        }
    }
}
