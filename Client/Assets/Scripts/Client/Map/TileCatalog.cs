using System;
using System.Collections.Generic;
using UnityEngine;
using Shared.World;

namespace Client.Map
{
    /// <summary>Каталог тайлов: id из Tile → визуал (префаб/спрайт) и флаги симуляции. Только клиент.</summary>
    [CreateAssetMenu(menuName = "Station/Tile Catalog", fileName = "TileCatalog")]
    public sealed class TileCatalog : ScriptableObject
    {
        /// <summary>Вид пола: id == значение <see cref="Tile.FloorType"/> (0 = пола нет).</summary>
        [Serializable]
        public sealed class FloorKind
        {
            [Tooltip("Значение Tile.FloorType. 0 зарезервирован под «нет пола».")]
            public byte Type = 1;
            public string DisplayName = "Floor";

            [Tooltip("Спрайт для клетки редактора. Если префаб пуст — рисуется и в игре на SpriteRenderer.")]
            public Sprite Sprite;
            [Tooltip("Префаб пола, инстансится в игре. Пусто → fallback на Sprite.")]
            public GameObject Prefab;

            [Header("Флаги симуляции, которые даёт этот пол")]
            [Tooltip("Сплошной пол не просвечивает на этаж ниже (FOV). Решётка/стекло = false.")]
            public bool BlocksVerticalSight = true;
            [Tooltip("Не пропускает газ вниз. Решётка = false.")]
            public bool SealsVertical = true;
        }

        /// <summary>Категория настенного объекта. Дверь/люк — открываемые.</summary>
        public enum StructureCategory : byte { Wall = 0, Door = 1, Hatch = 2, Window = 3 }

        /// <summary>Вид настенного объекта (стена/дверь/люк/окно): id == значение <see cref="Tile.StructureType"/>.</summary>
        [Serializable]
        public sealed class StructureKind
        {
            [Tooltip("Значение Tile.StructureType. 0 = объекта нет.")]
            public byte Type = 1;
            public string DisplayName = "Structure";
            [Tooltip("Стена/дверь/люк/окно. Дверь и люк — открываемые (Openable).")]
            public StructureCategory Category = StructureCategory.Wall;

            [Header("Визуал (у открываемых — закрытый/открытый)")]
            public Sprite Sprite;
            public GameObject Prefab;
            public Sprite OpenSprite;
            public GameObject OpenPrefab;

            [Header("Флаги симуляции")]
            [Tooltip("Держит обзор по горизонтали (в закрытом виде). Стекло/окно = false.")]
            public bool BlocksHorizontalSight = true;
            [Tooltip("Не пропускает газ по горизонтали (герметичность).")]
            public bool SealsHorizontal = true;

            /// <summary>Открываемый объект (дверь/люк), а не глухой (стена/окно).</summary>
            public bool Openable => Category == StructureCategory.Door || Category == StructureCategory.Hatch;
        }

        [SerializeField] private FloorKind[] _floors = Array.Empty<FloorKind>();
        [SerializeField] private StructureKind[] _structures = Array.Empty<StructureKind>();

        public IReadOnlyList<FloorKind> Floors => _floors;
        public IReadOnlyList<StructureKind> Structures => _structures;

        // Ленивые индексы id → вид (сброс через InvalidateCache).
        private Dictionary<byte, FloorKind> _floorById;
        private Dictionary<byte, StructureKind> _structureById;

        public FloorKind GetFloor(byte type)
        {
            EnsureMaps();
            return _floorById.TryGetValue(type, out var f) ? f : null;
        }

        public StructureKind GetStructure(byte type)
        {
            EnsureMaps();
            return _structureById.TryGetValue(type, out var s) ? s : null;
        }

        private void EnsureMaps()
        {
            if (_floorById != null) return;
            _floorById = new Dictionary<byte, FloorKind>();
            _structureById = new Dictionary<byte, StructureKind>();
            foreach (var f in _floors)
                if (f != null && f.Type != 0) _floorById[f.Type] = f;
            foreach (var s in _structures)
                if (s != null && s.Type != 0) _structureById[s.Type] = s;
        }

        /// <summary>Сбросить кэш id→вид (после правки списков в инспекторе/редакторе).</summary>
        public void InvalidateCache()
        {
            _floorById = null;
            _structureById = null;
        }

        /// <summary>Собрать Shared-тайл из id пола и настенного объекта, выводя флаги симуляции (0 = слоя нет).</summary>
        public Tile Compose(byte floorType, byte structureType)
        {
            var t = Tile.Space;
            t.FloorType = floorType;

            if (floorType != 0)
            {
                t.Support = true; // любой пол держит вес; «дыра» — это FloorType==0
                var f = GetFloor(floorType);
                if (f != null)
                {
                    t.BlocksVerticalSight |= f.BlocksVerticalSight;
                    t.SealsVertical |= f.SealsVertical;
                }
            }

            if (structureType != 0)
            {
                t.StructureType = structureType;
                t.Support = true;  // у стены/в проёме стоять можно; стена непроходима через Walkable
                t.Open = false;    // открываемые стартуют закрытыми (открывает сервер)
                var s = GetStructure(structureType);
                if (s != null)
                {
                    t.Openable = s.Openable;
                    t.BlocksHorizontalSight = s.BlocksHorizontalSight;
                    t.SealsHorizontal = s.SealsHorizontal;
                }
                else
                {
                    t.BlocksHorizontalSight = true;
                    t.SealsHorizontal = true;
                }
                if (!t.Openable)
                {
                    // Глухой объект (стена/окно) держит вертикаль и герметичен по Z.
                    t.BlocksVerticalSight = true;
                    t.SealsVertical = true;
                }
            }

            return t;
        }
    }
}
