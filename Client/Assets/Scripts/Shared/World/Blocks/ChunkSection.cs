using System.Collections.Generic;

namespace Shared.World.Blocks
{
    /// <summary>Секция 16³ блоков: палитра/raw-индексы + разреженные каналы state/bake/zone/seed (Y — высота).</summary>
    public sealed class ChunkSection
    {
        public const int Size = 16;
        public const int BlockCount = Size * Size * Size;

        private BlockPalette _palette = new();
        private byte[] _indices = new byte[BlockCount];
        private ushort[] _raw; // null, пока палитры хватает
        private readonly Dictionary<int, byte> _states = new();
        private readonly Dictionary<int, byte> _bake = new(); // авторская разметка (потолок/пол-интерьер), v11
        private readonly Dictionary<int, ushort> _zone = new();
        private readonly Dictionary<int, ushort> _struct = new(); // v15: смещение до якоря мульти-блока
        private readonly Dictionary<int, FloorSeed> _seeds = new();
        private int _nonAirCount;

        /// <summary>Бейк-биты (запекание карты): нижняя грань — потолок.</summary>
        public const byte BakeCeiling = 1 << 0;
        /// <summary>Бейк-биты: верхняя грань — пол интерьера (комната станции/шатла).</summary>
        public const byte BakeInteriorFloor = 1 << 1;
        /// <summary>Бейк-биты: ручная жёсткая граница зоны (сильнее любых дверей/проходимости, зона не течёт).</summary>
        public const byte BakeDivider = 1 << 2;
        /// <summary>Бейк-биты: ручное принудительное слияние зон (сильнее закрытой двери-ворот).</summary>
        public const byte BakeMerge = 1 << 3;

        /// <summary>Биты-семантика КЛЕТКИ (не блока): переживают установку/снос блока в позиции.</summary>
        public const byte BakeCellMask = BakeDivider | BakeMerge;

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
            _struct.Remove(localIndex); // смена типа рвёт принадлежность к прежней структуре (в т.ч. перезапись не-Air)
            if (type == 0)
            {
                _nonAirCount--;
                _states.Remove(localIndex);
                _seeds.Remove(localIndex);
                byte keep = (byte)(GetBake(localIndex) & BakeCellMask);
                if (keep == 0)
                    _bake.Remove(localIndex);
                else
                    _bake[localIndex] = keep;
            }
            return true;
        }

        /// <summary>Зона клеток без записи в словаре: сжатие «дефолт + исключения» (0 = как до v14).</summary>
        public ushort DefaultZone { get; private set; }

        /// <summary>Сколько клеток отличаются от <see cref="DefaultZone"/> (диагностика/замер сжатия).</summary>
        public int ZoneExceptionCount => _zone.Count;

        /// <summary>Слой принадлежности структур: упакованное смещение до якоря. Запись ЕСТЬ только у НЕ-якорных клеток.</summary>
        public ushort GetStruct(int localIndex) => _struct.TryGetValue(localIndex, out ushort s) ? s : (ushort)0;

        public bool SetStruct(int localIndex, ushort packed)
        {
            ushort old = GetStruct(localIndex);
            if (old == packed)
                return false;
            if (packed == 0)
                _struct.Remove(localIndex);
            else
                _struct[localIndex] = packed;
            return true;
        }

        public ushort GetZone(int localIndex) => _zone.TryGetValue(localIndex, out ushort z) ? z : DefaultZone;

        /// <summary>Записать ZoneId позиции. true — если изменился; в отличие от seed/bake, разрешено на Air (зона течёт через воздух).</summary>
        public bool SetZone(int localIndex, ushort zone)
        {
            ushort old = GetZone(localIndex);
            if (old == zone)
                return false;
            if (zone == DefaultZone)
                _zone.Remove(localIndex);
            else
                _zone[localIndex] = zone;
            return true;
        }

        /// <summary>Сбросить все зоны секции (дефолт и исключения) — полный пересчёт флуда стартует с чистого листа.</summary>
        public void ResetZones()
        {
            _zone.Clear();
            DefaultZone = 0;
        }

        /// <summary>Свернуть зоны: самый частый id по 4096 клеткам становится DefaultZone, в словаре остаются исключения.
        /// При равенстве частот берётся меньший id (детерминизм).</summary>
        public void CompactZones()
        {
            var values = new ushort[BlockCount];
            var counts = new Dictionary<ushort, int>();
            for (int li = 0; li < BlockCount; li++)
            {
                ushort z = GetZone(li);
                values[li] = z;
                counts.TryGetValue(z, out int c);
                counts[z] = c + 1;
            }

            ushort best = 0;
            int bestCount = -1;
            foreach (var kv in counts)
                if (kv.Value > bestCount || (kv.Value == bestCount && kv.Key < best))
                {
                    best = kv.Key;
                    bestCount = kv.Value;
                }

            DefaultZone = best;
            _zone.Clear();
            for (int li = 0; li < BlockCount; li++)
                if (values[li] != best)
                    _zone[li] = values[li];
        }

        public bool TryGetSeed(int localIndex, out FloorSeed seed) => _seeds.TryGetValue(localIndex, out seed);

        /// <summary>Записать сид этажа. false — если не изменился либо позиция Air (сид только на блоке).</summary>
        public bool SetSeed(int localIndex, in FloorSeed seed)
        {
            if (GetBlock(localIndex) == 0)
                return false;
            if (_seeds.TryGetValue(localIndex, out var old) && old.SameAs(in seed))
                return false;
            _seeds[localIndex] = seed;
            return true;
        }

        public bool RemoveSeed(int localIndex) => _seeds.Remove(localIndex);

        public byte GetBake(int localIndex) => _bake.TryGetValue(localIndex, out byte b) ? b : (byte)0;

        /// <summary>Записать бейк-байт (авторская разметка). true — если изменился.</summary>
        public bool SetBake(int localIndex, byte bake)
        {
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
        internal Dictionary<int, ushort> Zone => _zone;
        internal Dictionary<int, ushort> Struct => _struct;
        internal Dictionary<int, FloorSeed> Seeds => _seeds;

        /// <summary>Собрать секцию из десериализованных данных (raw-режим: palette/indices = null).</summary>
        internal static ChunkSection FromData(BlockPalette palette, byte[] indices, ushort[] raw, Dictionary<int, byte> states,
                                              Dictionary<int, byte> bake = null,
                                              Dictionary<int, ushort> zone = null, Dictionary<int, FloorSeed> seeds = null,
                                              ushort defaultZone = 0, Dictionary<int, ushort> structOffsets = null)
        {
            var s = new ChunkSection();
            s.DefaultZone = defaultZone;
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
            if (zone != null)
                foreach (var kv in zone)
                    if (kv.Value != defaultZone)
                        s._zone[kv.Key] = kv.Value;
            if (seeds != null)
                foreach (var kv in seeds)
                    if (s.GetBlock(kv.Key) != 0)
                        s._seeds[kv.Key] = kv.Value;
            if (structOffsets != null)
                foreach (var kv in structOffsets)
                {
                    if (kv.Value == 0)
                        continue;
                    ushort t = s.GetBlock(kv.Key);
                    if (t != 0 && BlockCatalog.Get(t).PartCount > 1)
                        s._struct[kv.Key] = kv.Value;
                }
            return s;
        }
    }
}
