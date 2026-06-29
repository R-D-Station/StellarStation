using System;
using System.Collections.Generic;
using UnityEngine;
using Shared.World;

namespace Client.Map
{
    /// <summary>Каталог тайлов: id из Tile → визуал (префаб/спрайт) и флаги симуляции. Только клиент.
    /// Виды — отдельные SO-ассеты (<see cref="FloorDefinition"/>/<see cref="StructureDefinition"/>).</summary>
    [CreateAssetMenu(menuName = "Station/Tile Catalog", fileName = "TileCatalog")]
    public sealed class TileCatalog : ScriptableObject
    {
        [SerializeField] private FloorDefinition[] _floors = Array.Empty<FloorDefinition>();
        [SerializeField] private StructureDefinition[] _structures = Array.Empty<StructureDefinition>();

        public IReadOnlyList<FloorDefinition> Floors => _floors;
        public IReadOnlyList<StructureDefinition> Structures => _structures;

        // Ленивые индексы id → вид (сброс через InvalidateCache).
        private Dictionary<byte, FloorDefinition> _floorById;
        private Dictionary<byte, StructureDefinition> _structureById;

        public FloorDefinition GetFloor(byte type)
        {
            EnsureMaps();
            return _floorById.TryGetValue(type, out var f) ? f : null;
        }

        public StructureDefinition GetStructure(byte type)
        {
            EnsureMaps();
            return _structureById.TryGetValue(type, out var s) ? s : null;
        }

        private void EnsureMaps()
        {
            if (_floorById != null) return;
            _floorById = new Dictionary<byte, FloorDefinition>();
            _structureById = new Dictionary<byte, StructureDefinition>();
            foreach (var f in _floors)
            {
                if (f == null || f.Type == 0) continue;
                if (_floorById.ContainsKey(f.Type))
                    Debug.LogWarning($"[TileCatalog] дубль Floor Type={f.Type} ('{f.DisplayName}') — перезапись (last-wins)", this);
                _floorById[f.Type] = f;
            }
            foreach (var s in _structures)
            {
                if (s == null || s.Type == 0) continue;
                if (_structureById.ContainsKey(s.Type))
                    Debug.LogWarning($"[TileCatalog] дубль Structure Type={s.Type} ('{s.DisplayName}') — перезапись (last-wins)", this);
                _structureById[s.Type] = s;
            }
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
