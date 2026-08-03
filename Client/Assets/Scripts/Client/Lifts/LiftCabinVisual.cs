using UnityEngine;
using Client.Map;
using Shared.Messages.Lifts;
using Shared.Simulation.Blocks;

namespace Client.Lifts
{
    public sealed class LiftCabinVisual
    {
        private readonly int _liftId;

        private GameObject _root;
        private BlockView _view;
        private GameObject[] _cubes;

        private BlockDefinition _resolvedDef;
        private ushort _resolvedDefId;
        private bool _resolved;

        private bool _builtAsBoxes;
        private ushort _builtDefId;
        private int _builtBoxCount;
        private BlockComponentPairing _pairing;
        private bool _pivotWarned;

        public LiftCabinVisual(int liftId) => _liftId = liftId;

        public void Sync(in LiftRegistryEntry geom, float y, bool debugBoxes, float alpha, BlockRenderer blocks,
            bool doorOpen)
        {
            var boxes = geom.Boxes;
            if (boxes == null || boxes.Length == 0)
            {
                Destroy();
                return;
            }

            if (!_resolved || _resolvedDefId != geom.CabinDefId)
            {
                _resolvedDef = geom.CabinDefId != 0 ? BlockDefinitionResolver.Find(geom.CabinDefId) : null;
                _resolvedDefId = geom.CabinDefId;
                _resolved = true;
            }

            bool asBoxes = debugBoxes || _resolvedDef == null || _resolvedDef.Prefab == null;
            if (_root == null || asBoxes != _builtAsBoxes || _builtDefId != geom.CabinDefId
                || (asBoxes && _builtBoxCount != boxes.Length))
                Rebuild(in geom, asBoxes, blocks, doorOpen);
            else if (!_pairing.Spawned && blocks != null)
                SpawnComponents(in geom, blocks);

            if (asBoxes)
                PlaceCubes(in geom, y);
            else
                PlacePrefab(in geom, y);

            _view.SetAlpha(alpha);

            if (doorOpen != _view.DoorOpen)
                _view.PlayDoor(doorOpen);
        }

        public void Destroy()
        {
            // Симметрия обязательна: без Despawn зона осталась бы в своём реестре после исчезновения
            // лифта и продолжила бы срабатывать в пустоте.
            if (_pairing.BeginDespawn() && _view != null)
                _view.DespawnComponents();
            if (_root != null)
                Object.Destroy(_root);
            _root = null;
            _view = null;
            _cubes = null;
            _builtBoxCount = 0;
        }

        // Пересборка вьюхи — это Despawn старых компонентов + Spawn новых, а не только замена GameObject.
        private void Rebuild(in LiftRegistryEntry geom, bool asBoxes, BlockRenderer blocks, bool doorOpen)
        {
            Destroy();
            _builtAsBoxes = asBoxes;
            _builtDefId = geom.CabinDefId;
            _builtBoxCount = geom.Boxes.Length;

            if (asBoxes)
            {
                _root = new GameObject($"LiftCabin{_liftId}Boxes");
                _cubes = new GameObject[geom.Boxes.Length];
                for (int i = 0; i < _cubes.Length; i++)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = $"Box{i}";
                    var col = cube.GetComponent<Collider>();
                    if (col != null)
                        Object.Destroy(col);
                    cube.transform.SetParent(_root.transform, worldPositionStays: false);
                    _cubes[i] = cube;
                }
            }
            else
            {
                _root = Object.Instantiate(_resolvedDef.Prefab);
                _root.name = $"LiftCabin{_liftId}";
                WarnOnCenterPivot(_resolvedDef);
            }

            // Кабина живёт ВНЕ пула блоков: BlockView нужен здесь только ради кэша рендереров, гейта enabled,
            // дробной альфы через MPB и уведомления IBlockComponent. ResetForPool() и аренду НЕ звать —
            // их зовёт BlockRenderer на СВОИХ вьюхах, и вызов отсюда вернул бы чужой объект в чужой пул.
            _view = _root.GetComponent<BlockView>();
            if (_view == null)
                _view = _root.AddComponent<BlockView>();
            _view.Bind(CellX(in geom), 0, CellZ(in geom), 0, null);

            // ⚠ Строго ПОСЛЕ Bind: он обнуляет DoorAnim. Аниматор ищем только здесь — GetComponentInChildren
            // в покадровом пути запрещён. Состояние ФАКТИЧЕСКОЕ: пересборка посреди стоянки не должна
            // захлопнуть уже открытые створки.
            _view.SetDoor(_root.GetComponentInChildren<Animator>(true), doorOpen);

            if (blocks != null)
                SpawnComponents(in geom, blocks);
        }

        // ⚠ Координаты в контексте кабины — ЯКОРЬ ПЛАНОВОГО ПРЯМОУГОЛЬНИКА ШАХТЫ, а не клетка объекта:
        // клетки в гриде у кабины нет, это её суть. Следствие-ограничение (не баг): компонент, читающий
        // содержимое клетки (напр. BlockLabel со слотом SeedName), получит данные ТОЙ клетки, а не кабины.
        // Значение берётся то же, что уходит в Bind, чтобы не завести вторую конвенцию.
        private void SpawnComponents(in LiftRegistryEntry geom, BlockRenderer blocks)
        {
            if (_view == null || !_pairing.BeginSpawn())
                return;
            var ctx = blocks.ContextFor(CellX(in geom), 0, CellZ(in geom), geom.CabinDefId);
            _view.SpawnComponents(in ctx);
        }

        private static int CellX(in LiftRegistryEntry geom) => Mathf.FloorToInt(geom.AnchorX);

        private static int CellZ(in LiftRegistryEntry geom) => Mathf.FloorToInt(geom.AnchorZ);

        private void PlacePrefab(in LiftRegistryEntry geom, float y)
        {
            LiftCabinPlacement.WorldPivot(geom.AnchorX, geom.AnchorZ, geom.PlanW, geom.PlanD, y,
                out float px, out float py, out float pz);
            _root.transform.SetPositionAndRotation(new Vector3(px, py, pz),
                Quaternion.Euler(0f, 90f * (geom.Facing & 3), 0f));
        }

        private void PlaceCubes(in LiftRegistryEntry geom, float y)
        {
            var boxes = geom.Boxes;
            for (int i = 0; i < _cubes.Length && i < boxes.Length; i++)
            {
                var box = boxes[i];
                var t = _cubes[i].transform;
                t.localScale = new Vector3(box.HalfX * 2f, box.Height, box.HalfZ * 2f);
                t.position = new Vector3(
                    geom.AnchorX + box.CenterX,
                    y + box.Y + box.Height * 0.5f,
                    geom.AnchorZ + box.CenterZ);
            }
        }

        private void WarnOnCenterPivot(BlockDefinition def)
        {
            if (_pivotWarned || def.Pivot != BlockDefinition.BlockPivot.Center)
                return;
            _pivotWarned = true;
            Debug.LogWarning($"[Lifts] у кабины '{def.name}' Pivot = Center. К кабине PivotYOffset НЕ применяется " +
                             "(корень = план-центр модуля ШАХТЫ на уровне пола кабины) — поставь BottomCenter, " +
                             "иначе модель уедет по высоте на половину Size.y.");
        }
    }
}
