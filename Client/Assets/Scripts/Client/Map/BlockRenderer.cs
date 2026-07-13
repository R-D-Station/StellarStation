using System.Collections.Generic;
using UnityEngine;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace Client.Map
{
    /// <summary>Рендер блок-мира: пуленый визуал на блок (префаб/куб) посекционно + cut-away поверх слоя.</summary>
    public sealed class BlockRenderer : MonoBehaviour
    {
        /// <summary>Радиус кольца скрытия потолка вокруг игрока (блоков, chebyshev по плану).</summary>
        private const int CutRingRadius = 10;
        private const float CutMoveThreshold = 0.2f;

        private float _lastEyeX = float.MinValue, _lastEyeY, _lastEyeZ;
        private bool _cutDirty;

        // Карта выреза (пасс 1): минимальный Y скрытого блока в каждой колонне окна вокруг игрока.
        // Пасс 2 обрезает по ней стены (8-смежность — ловит углы). MaxValue = в колонне выреза нет.
        private const int CutWindow = CutRingRadius * 2 + 1;
        private readonly int[] _cutStartY = new int[CutWindow * CutWindow];
        private int _cutOriginX, _cutOriginZ;

        /// <summary>Глубина спуска по solid-стеку при поиске основания блока.</summary>
        private const int StackScanDepth = 12;

        // Основание стека: спускаемся, пока под блоком solid-верх (стена стоит на полу — их база общая;
        // плита/мебель/стены ВЕРХНЕГО этажа стоят на его перекрытии — база выше нашего уровня → режутся вместе).
        private int FindStackBase(int x, int y, int z)
        {
            int b = y;
            for (int i = 0; i < StackScanDepth && HasSolidTop(x, b - 1, z); i++)
                b--;
            return b;
        }

        private bool HasSolidTop(int x, int y, int z)
        {
            var boxes = _shapes.GetBoxes(_grid.GetBlock(x, y, z), _grid.GetState(x, y, z));
            for (int i = 0; i < boxes.Length; i++)
                if (boxes[i].MaxYf >= 0.999f)
                    return true;
            return false;
        }

        private BlockGrid _grid;
        private IBlockShapes _shapes;
        private Transform _root;
        private readonly Dictionary<long, List<BlockView>> _sections = new();
        private readonly Stack<GameObject> _cubePool = new();
        private readonly Dictionary<GameObject, Stack<GameObject>> _prefabPools = new();

        // Кэш материалов по цвету: не мутируем материал примитива и не плодим инстанс на куб.
        private static readonly Dictionary<Color, Material> _materials = new();

        public void Init(BlockGrid grid, IBlockShapes shapes)
        {
            _grid = grid;
            _shapes = shapes;

            if (_root != null)
            {
                Destroy(_root.gameObject); // повторный Init (переподключение) — мир и пулы заново
                _cubePool.Clear();
                _prefabPools.Clear();
            }
            _sections.Clear();
            _root = new GameObject("BlockWorld").transform;
            _root.SetParent(transform, false);

            foreach (var key in grid.Sections.Keys)
            {
                BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                ApplySection(cx, cy, cz);
            }
        }

        /// <summary>Перестроить визуал одной секции (пришла/ушла/изменилась).</summary>
        public void ApplySection(int cx, int cy, int cz)
        {
            if (_grid == null)
                return;

            long key = BlockGrid.Key(cx, cy, cz);
            if (_sections.TryGetValue(key, out var visuals))
            {
                for (int i = 0; i < visuals.Count; i++)
                    ReturnToPool(visuals[i]);
                visuals.Clear();
                _cutDirty = true; // визуалы пересозданы с Hidden=false — cut-away обязан пересчитаться и стоя на месте
            }

            var section = _grid.GetSection(cx, cy, cz);
            if (section == null)
                return;
            _cutDirty = true;

            if (visuals == null)
            {
                visuals = new List<BlockView>();
                _sections[key] = visuals;
            }

            for (int ly = 0; ly < ChunkSection.Size; ly++)
                for (int lz = 0; lz < ChunkSection.Size; lz++)
                    for (int lx = 0; lx < ChunkSection.Size; lx++)
                    {
                        ushort type = section.GetBlock(ChunkSection.LocalIndex(lx, ly, lz));
                        if (type == 0 || BlockCatalog.Get(type).IsMarker)
                            continue; // маркеры — только в редакторе

                        SpawnBlock(visuals, cx * 16 + lx, cy * 16 + ly, cz * 16 + lz, type);
                    }
        }

        /// <summary>Точечная дельта: перестроить секцию блока и секции-соседи, чьи визуалы от него зависят.</summary>
        public void ApplyBlockChange(int x, int y, int z)
        {
            BlockGrid.UnpackKey(BlockGrid.KeyOfBlock(x, y, z), out int cx, out int cy, out int cz);
            ApplySection(cx, cy, cz);

            int lx = ((x % 16) + 16) % 16;
            int ly = ((y % 16) + 16) % 16;
            int lz = ((z % 16) + 16) % 16;
            if (lx == 0) ApplySection(cx - 1, cy, cz);
            if (lx == 15) ApplySection(cx + 1, cy, cz);
            if (lz == 0) ApplySection(cx, cy, cz - 1);
            if (lz == 15) ApplySection(cx, cy, cz + 1);
            if ((lx == 0 || lx == 15) && (lz == 0 || lz == 15)) // диагональ: маска углов верха соседа
                ApplySection(cx + (lx == 0 ? -1 : 1), cy, cz + (lz == 0 ? -1 : 1));
            if (ly == 0) // верх-гейт квада у блока снизу
                ApplySection(cx, cy - 1, cz);
            if (ly >= ChunkSection.Size - StackScanDepth) // FindStackBase секции выше сканирует вниз до нас
                ApplySection(cx, cy + 1, cz);
        }

        /// <summary>Дельта «изменился только Open» у двери: тоггл аниматора на живом инстансе без пересборки секции.</summary>
        public void ApplyDoorState(int x, int y, int z, ushort type, byte state)
        {
            var info = BlockCatalog.Get(type);
            Shared.World.Blocks.MultiBlock.AnchorOf(x, y, z, Shared.World.Blocks.BlockState.GetPart(state),
                info.SizeX, info.SizeZ, Shared.World.Blocks.BlockState.GetFacing(state),
                out int ax, out int ay, out int az);

            BlockGrid.UnpackKey(BlockGrid.KeyOfBlock(ax, ay, az), out int cx, out int cy, out int cz);
            if (!_sections.TryGetValue(BlockGrid.Key(cx, cy, cz), out var visuals))
                return;

            bool open = Shared.World.Blocks.BlockState.GetOpen(state);
            for (int i = 0; i < visuals.Count; i++)
            {
                var v = visuals[i];
                if (v.X != ax || v.Y != ay || v.Z != az || v.DoorAnim == null)
                    continue;
                if (v.DoorOpen != open)
                    v.PlayDoor(open);
                return;
            }
        }

        private static BlockView EnsureView(GameObject go)
            => go.GetComponent<BlockView>() ?? go.AddComponent<BlockView>();

        // Делегирует BlockAutoTex.FeedGrids (верх+бок через BlockMaterials); пишет top-параметры в view (дебаг).
        private void FeedTopMesh(GameObject instance, BlockDefinition def, ushort type, int x, int y, int z,
                                 int rotSteps, BlockView view)
        {
            var p = BlockAutoTex.FeedGrids(instance, _grid, def, type, x, y, z, rotSteps, out byte corners);
            if (view != null)
                view.SetTopDebug(in p, corners);
        }

        private void SpawnBlock(List<BlockView> visuals, int x, int y, int z, ushort type)
        {
            int baseY = FindStackBase(x, y, z); // статично до изменения мира — секция тогда перестроится

            var def = BlockDefinitionResolver.Find(type);

            // Автотайл: меш и поворот по 4 план-соседям (BlockShapeResolver, база — соединением на север).
            GameObject prefab = BlockAutoTex.ResolveMesh(_grid, def, type, x, y, z, out int rotSteps);
            bool autotiled = prefab != null;
            if (prefab == null)
                prefab = def?.Prefab;

            if (prefab != null)
            {
                float pivotY = def != null ? def.PivotYOffset : 0f; // Низ = 0; Центр = половина высоты объекта
                Vector3 pos = new Vector3(x + 0.5f, y + pivotY, z + 0.5f);
                Quaternion rot = Quaternion.Euler(0f, 90f * rotSteps, 0f);

                // Мульти-блок: рисует только якорная часть (0) — по центру низа футпринта с facing-поворотом.
                if (def != null && def.Size.x * def.Size.y * def.Size.z > 1)
                {
                    byte st = _grid.GetState(x, y, z);
                    if (Shared.World.Blocks.BlockState.GetPart(st) != 0)
                        return;
                    int facing = Shared.World.Blocks.BlockState.GetFacing(st);
                    pos = MultiBlockVisual.FootprintBottomCenter(x, y, z, def.Size.x, def.Size.z, facing)
                          + Vector3.up * pivotY;
                    if (!autotiled) // автотайл-меш повёрнут формой соседей, facing не применяем
                        rot = MultiBlockVisual.FacingRotation(facing);
                }

                var go = RentPrefab(prefab);
                go.transform.SetPositionAndRotation(pos, rot);

                var view = EnsureView(go);
                view.Bind(x, y, z, baseY, prefab); // данные + кэш рендереров
                FeedTopMesh(go, def, type, x, y, z, rotSteps, view); // грид + top-debug
                if (def != null && def.Openable) // дверь-якорь: аниматор для тоггла без пересборки (снап на спавне — внутри SetDoor)
                    view.SetDoor(go.GetComponentInChildren<Animator>(true),
                                 Shared.World.Blocks.BlockState.GetOpen(_grid.GetState(x, y, z)));
                visuals.Add(view);
                return;
            }

            var boxes = _shapes.GetBoxes(type, _grid.GetState(x, y, z));
            float t = 0.35f + 0.5f * ((type * 0.6180339887f) % 1f); // оттенок по типу
            for (int i = 0; i < boxes.Length; i++)
            {
                ref readonly var b = ref boxes[i];
                var cube = RentCube();
                cube.transform.localScale = new Vector3(b.MaxXf - b.MinXf, b.MaxYf - b.MinYf, b.MaxZf - b.MinZf);
                cube.transform.position = new Vector3(
                    x + (b.MinXf + b.MaxXf) * 0.5f,
                    y + (b.MinYf + b.MaxYf) * 0.5f,
                    z + (b.MinZf + b.MaxZf) * 0.5f);
                SetColor(cube, new Color(t, t, t));
                var view = EnsureView(cube);
                view.Bind(x, y, z, baseY, null);
                visuals.Add(view);
            }
        }

        /// <summary>Cut-away: скрыть блоки выше глаз чужого стека в кольце вокруг игрока (звать каждый кадр).</summary>
        public void UpdateCutaway(float px, float py, float pz)
        {
            float eyeY = py + Shared.Simulation.Blocks.BlockMovementConfig.StandHeight; // срез над головой
            if (!_cutDirty
                && Mathf.Abs(px - _lastEyeX) < CutMoveThreshold
                && Mathf.Abs(eyeY - _lastEyeY) < CutMoveThreshold
                && Mathf.Abs(pz - _lastEyeZ) < CutMoveThreshold)
                return;
            _lastEyeX = px;
            _lastEyeY = eyeY;
            _lastEyeZ = pz;
            _cutDirty = false;

            float refY = Mathf.Floor(py + 0.001f); // квантованный уровень ног — прыжок не мигает срезом

            // Пасс 1: правило базы стека → карта выреза (с какого Y колонна «вскрыта»).
            _cutOriginX = Mathf.FloorToInt(px) - CutRingRadius;
            _cutOriginZ = Mathf.FloorToInt(pz) - CutRingRadius;
            for (int i = 0; i < _cutStartY.Length; i++)
                _cutStartY[i] = int.MaxValue;

            foreach (var kv in _sections)
            {
                var visuals = kv.Value;
                for (int i = 0; i < visuals.Count; i++)
                {
                    var v = visuals[i];
                    if (!BaseRuleHide(v, eyeY, refY, px, pz))
                        continue;
                    int lx = v.X - _cutOriginX;
                    int lz = v.Z - _cutOriginZ;
                    if (lx < 0 || lz < 0 || lx >= CutWindow || lz >= CutWindow)
                        continue;
                    int idx = lz * CutWindow + lx;
                    if (v.Y < _cutStartY[idx])
                        _cutStartY[idx] = v.Y;
                }
            }

            // Пасс 2: база + кап стен (сосед вскрыт не выше — гасим, чтобы стены не торчали колодцем).
            foreach (var kv in _sections)
            {
                var visuals = kv.Value;
                for (int i = 0; i < visuals.Count; i++)
                {
                    var v = visuals[i];
                    bool hide = BaseRuleHide(v, eyeY, refY, px, pz)
                                || (v.Y >= eyeY && NeighborCutAtOrBelow(v.X, v.Z, v.Y));
                    v.SetHidden(hide); // no-op если не изменилось; инвариант enabled — внутри
                }
            }
        }

        // Базовое правило: гасим верхний этаж целиком (база стека выше нашего +1), кольцо ограничивает разлёт.
        private bool BaseRuleHide(BlockView v, float eyeY, float refY, float px, float pz)
            => v.Y >= eyeY
               && v.BaseY >= refY + 2
               && Mathf.Max(Mathf.Abs(v.X + 0.5f - px), Mathf.Abs(v.Z + 0.5f - pz)) <= CutRingRadius;

        // Есть ли в 8 соседних колоннах вырез, начавшийся не выше y.
        private bool NeighborCutAtOrBelow(int x, int z, int y)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int lx = x + dx - _cutOriginX;
                    int lz = z + dz - _cutOriginZ;
                    if (lx < 0 || lz < 0 || lx >= CutWindow || lz >= CutWindow)
                        continue;
                    if (_cutStartY[lz * CutWindow + lx] <= y)
                        return true;
                }
            return false;
        }


        private GameObject RentCube()
        {
            if (_cubePool.Count > 0)
            {
                var pooled = _cubePool.Pop();
                pooled.SetActive(true);
                return pooled;
            }
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>()); // коллизия — серверная/предикт, не Unity-физика
            cube.transform.SetParent(_root, false);
            return cube;
        }

        private GameObject RentPrefab(GameObject prefab)
        {
            if (_prefabPools.TryGetValue(prefab, out var pool) && pool.Count > 0)
            {
                var pooled = pool.Pop();
                pooled.SetActive(true);
                return pooled;
            }
            var go = Instantiate(prefab, _root);
            return go;
        }

        private void ReturnToPool(BlockView v)
        {
            if (v == null || v.gameObject == null)
                return;
            // Инвариант пула: возврат с ВКЛЮЧЁННЫМИ рендерерами (cut-away гасит выборочно — иначе утечка
            // «навсегда невидимых» блоков, урок pool-renderer-enabled-leak) — внутри ResetForPool.
            v.ResetForPool();
            v.gameObject.SetActive(false);
            if (v.PrefabKey == null)
            {
                _cubePool.Push(v.gameObject);
            }
            else
            {
                if (!_prefabPools.TryGetValue(v.PrefabKey, out var pool))
                {
                    pool = new Stack<GameObject>();
                    _prefabPools[v.PrefabKey] = pool;
                }
                pool.Push(v.gameObject);
            }
        }

        private static void SetColor(GameObject cube, Color color)
        {
            var r = cube.GetComponent<Renderer>();
            if (r == null) return;
            if (!_materials.TryGetValue(color, out var mat) || mat == null)
            {
                mat = new Material(r.sharedMaterial) { color = color };
                _materials[color] = mat;
            }
            r.sharedMaterial = mat;
        }
    }
}
