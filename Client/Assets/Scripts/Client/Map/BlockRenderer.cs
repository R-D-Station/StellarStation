using System.Collections.Generic;
using UnityEngine;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace Client.Map
{
    /// <summary>
    /// Рендер блок-мира (фаза D1): визуал на блок — префаб из BlockDefinition (пивот = центр низа) либо
    /// серые кубы по коллизионным боксам. Корень на секцию, обновление посекционно от стрим-событий;
    /// объекты ПУЛЯТСЯ (дельты не порождают Instantiate/Destroy-шторм). Маркер-блоки в игре не рисуются.
    /// Оси блок-мира = оси Unity. Cut-away (D2) вешается поверх этого слоя.
    /// </summary>
    public sealed class BlockRenderer : MonoBehaviour
    {
        private struct Visual
        {
            public GameObject Go;
            public GameObject PrefabKey; // null = куб из общего пула
            public Renderer[] Renderers; // кэш для cut-away гейта
            public int X, Y, Z;
            public int BaseY;  // основание solid-стека блока: уровень, «на котором он стоит» (фильтр этажей)
            public bool Hidden;
        }

        /// <summary>Радиус кольца скрытия потолка вокруг игрока (блоков, chebyshev по плану). Тюнинг D2.</summary>
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
        private readonly Dictionary<long, List<Visual>> _sections = new();
        private readonly Stack<GameObject> _cubePool = new();
        private GameObject _quadPoolKey; // сентинел PrefabKey квадов верха — пулятся через _prefabPools
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
            if (_quadPoolKey == null)
            {
                _quadPoolKey = new GameObject("QuadPoolKey");
                _quadPoolKey.SetActive(false);
                _quadPoolKey.transform.SetParent(transform, false);
            }

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
                visuals = new List<Visual>();
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

        /// <summary>Точечная дельта: перестраиваем секцию блока и секции-соседи, чьи визуалы зависят от
        /// изменившейся ячейки (автотайл/маска углов — план и диагональ; верх-гейт — низ; базы стеков — верх).</summary>
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

        // Текстурный верх (автотекстуринг): квад TileReader над блоком, если задан TopMap и верх открыт.
        private void SpawnTopQuad(List<Visual> visuals, BlockDefinition def, ushort type, int x, int y, int z, int baseY)
        {
            if (def == null || def.TopMap == null || _grid.GetBlock(x, y + 1, z) != 0)
                return;

            BlockAutoTex.Resolve(_grid, def, type, x, y, z, out var shape, out int steps, out byte corners);
            float topY = y + BlockAutoTex.TopHeight(_shapes, type, _grid.GetState(x, y, z));

            var quad = RentQuad();
            BlockAutoTex.ConfigureQuad(quad, def, x, z, topY, shape, steps, corners);
            visuals.Add(new Visual
            {
                Go = quad, PrefabKey = _quadPoolKey, Renderers = quad.GetComponentsInChildren<Renderer>(true),
                X = x, Y = y, Z = z, BaseY = baseY
            });
        }

        private GameObject RentQuad()
        {
            if (_prefabPools.TryGetValue(_quadPoolKey, out var pool) && pool.Count > 0)
            {
                var pooled = pool.Pop();
                pooled.SetActive(true);
                return pooled;
            }
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_root, false);
            return quad;
        }

        private void SpawnBlock(List<Visual> visuals, int x, int y, int z, ushort type)
        {
            int baseY = FindStackBase(x, y, z); // статично до изменения мира — секция тогда перестроится

            var def = BlockDefinitionResolver.Find(type);
            SpawnTopQuad(visuals, def, type, x, y, z, baseY); // текстурный верх — независимо от тела

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
                BlockFaceTex.Feed(go, def);
                go.transform.SetPositionAndRotation(pos, rot);
                visuals.Add(new Visual
                {
                    Go = go, PrefabKey = prefab, Renderers = go.GetComponentsInChildren<Renderer>(true),
                    X = x, Y = y, Z = z, BaseY = baseY
                });
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
                visuals.Add(new Visual
                {
                    Go = cube, PrefabKey = null, Renderers = cube.GetComponentsInChildren<Renderer>(true),
                    X = x, Y = y, Z = z, BaseY = baseY
                });
            }
        }

        /// <summary>Cut-away: скрыть всё выше глаз, чей стек стоит выше нашего уровня (+1), в кольце вокруг
        /// игрока. Стены своего уровня не гасятся никогда. Звать каждый кадр — пересчёт по порогу движения.</summary>
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
                    if (!BaseRuleHide(in v, eyeY, refY, px, pz))
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

            // Пасс 2: применяем базу + кап стен — блок выше глаз гаснет, если рядом (8-смежность) вырез
            // начался не выше него: стены не торчат «колодцем» над срезанным потолком.
            foreach (var kv in _sections)
            {
                var visuals = kv.Value;
                for (int i = 0; i < visuals.Count; i++)
                {
                    var v = visuals[i];
                    bool hide = BaseRuleHide(in v, eyeY, refY, px, pz)
                                || (v.Y >= eyeY && NeighborCutAtOrBelow(v.X, v.Z, v.Y));
                    if (hide == v.Hidden)
                        continue;
                    v.Hidden = hide;
                    visuals[i] = v;
                    for (int r = 0; r < v.Renderers.Length; r++)
                        if (v.Renderers[r] != null)
                            v.Renderers[r].enabled = !hide;
                }
            }
        }

        // Режем всё выше глаз, чей стек СТОИТ выше нашего уровня (+1 — антресоли свои): потолок, перекрытие,
        // стены и мебель верхнего этажа уходят разом; стены СВОЕГО уровня (база ≤ refY+1) — полные, кольцо
        // ограничивает разлёт.
        private bool BaseRuleHide(in Visual v, float eyeY, float refY, float px, float pz)
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

        private void ReturnToPool(in Visual v)
        {
            if (v.Go == null)
                return;
            // Инвариант пула: возвращаем с ВКЛЮЧЁННЫМИ рендерерами (cut-away гасит их выборочно —
            // иначе утечка «навсегда невидимых» блоков, см. урок pool-renderer-enabled-leak).
            if (v.Hidden && v.Renderers != null)
                for (int r = 0; r < v.Renderers.Length; r++)
                    if (v.Renderers[r] != null)
                        v.Renderers[r].enabled = true;
            v.Go.SetActive(false);
            if (v.PrefabKey == null)
            {
                _cubePool.Push(v.Go);
            }
            else
            {
                if (!_prefabPools.TryGetValue(v.PrefabKey, out var pool))
                {
                    pool = new Stack<GameObject>();
                    _prefabPools[v.PrefabKey] = pool;
                }
                pool.Push(v.Go);
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
