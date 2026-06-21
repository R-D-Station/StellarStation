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

        /// <summary>Вид стены: id == значение <see cref="Tile.WallType"/> (0 = стены нет).</summary>
        [Serializable]
        public sealed class WallKind
        {
            [Tooltip("Значение Tile.WallType. 0 = стены нет.")]
            public byte Type = 1;
            public string DisplayName = "Wall";

            public Sprite Sprite;
            public GameObject Prefab;

            [Header("Флаги симуляции, которые даёт эта стена")]
            [Tooltip("Держит обзор по горизонтали (FOV на этаже).")]
            public bool BlocksHorizontalSight = true;
            [Tooltip("Не пропускает газ по горизонтали (герметичность). Стекло = true, но видно сквозь.")]
            public bool SealsHorizontal = true;
        }

        /// <summary>Вид двери: id == значение <see cref="Tile.DoorType"/> (0 = двери нет).</summary>
        [Serializable]
        public sealed class DoorKind
        {
            [Tooltip("Значение Tile.DoorType. 0 = двери нет.")]
            public byte Type = 1;
            public string DisplayName = "Door";

            [Header("Визуал: закрыта / открыта")]
            public Sprite ClosedSprite;
            public GameObject ClosedPrefab;
            public Sprite OpenSprite;
            public GameObject OpenPrefab;
        }

        [SerializeField] private FloorKind[] _floors = Array.Empty<FloorKind>();
        [SerializeField] private WallKind[] _walls = Array.Empty<WallKind>();
        [SerializeField] private DoorKind[] _doors = Array.Empty<DoorKind>();

        public IReadOnlyList<FloorKind> Floors => _floors;
        public IReadOnlyList<WallKind> Walls => _walls;
        public IReadOnlyList<DoorKind> Doors => _doors;

        // Ленивые индексы id → вид (сброс через InvalidateCache).
        private Dictionary<byte, FloorKind> _floorById;
        private Dictionary<byte, WallKind> _wallById;
        private Dictionary<byte, DoorKind> _doorById;

        public FloorKind GetFloor(byte type)
        {
            EnsureMaps();
            return _floorById.TryGetValue(type, out var f) ? f : null;
        }

        public WallKind GetWall(byte type)
        {
            EnsureMaps();
            return _wallById.TryGetValue(type, out var w) ? w : null;
        }

        public DoorKind GetDoor(byte type)
        {
            EnsureMaps();
            return _doorById.TryGetValue(type, out var d) ? d : null;
        }

        private void EnsureMaps()
        {
            if (_floorById != null) return;
            _floorById = new Dictionary<byte, FloorKind>();
            _wallById = new Dictionary<byte, WallKind>();
            _doorById = new Dictionary<byte, DoorKind>();
            foreach (var f in _floors)
                if (f != null && f.Type != 0) _floorById[f.Type] = f;
            foreach (var w in _walls)
                if (w != null && w.Type != 0) _wallById[w.Type] = w;
            foreach (var d in _doors)
                if (d != null && d.Type != 0) _doorById[d.Type] = d;
        }

        /// <summary>Сбросить кэш id→вид (после правки списков в инспекторе/редакторе).</summary>
        public void InvalidateCache()
        {
            _floorById = null;
            _wallById = null;
            _doorById = null;
        }

        /// <summary>Собрать Shared-тайл из id слоёв, выводя флаги симуляции (0 = слоя нет).</summary>
        public Tile Compose(byte floorType, byte wallType, byte doorType = 0)
        {
            var t = Tile.Space;
            t.FloorType = floorType;
            t.WallType = wallType;

            if (floorType != 0)
            {
                // Любой пол держит вес; «дыра» — это FloorType==0.
                t.Support = true;
                var f = GetFloor(floorType);
                if (f != null)
                {
                    t.BlocksVerticalSight |= f.BlocksVerticalSight;
                    t.SealsVertical |= f.SealsVertical;
                }
            }

            if (wallType != 0)
            {
                var w = GetWall(wallType);
                // Сплошная стена: держит обзор/газ по горизонтали и блокирует вертикаль.
                t.Support = true;
                t.BlocksVerticalSight = true;
                t.SealsVertical = true;
                if (w != null)
                {
                    t.BlocksHorizontalSight |= w.BlocksHorizontalSight;
                    t.SealsHorizontal |= w.SealsHorizontal;
                }
            }

            if (doorType != 0)
            {
                // Дверь стартует закрытой (открывает сервер); закрытая держит обзор/газ как стена.
                t.DoorType = doorType;
                t.DoorOpen = false;
                t.Support = true;          // в проёме можно стоять
                t.BlocksHorizontalSight = true;
                t.SealsHorizontal = true;
            }

            return t;
        }
    }
}
