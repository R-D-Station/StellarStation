using System.Collections.Generic;
using UnityEngine;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;
using Client.UI.Labels;

namespace Client.Map
{
    /// <summary>Рендер блок-мира: пуленый визуал на блок (префаб/куб) посекционно + cut-away поверх слоя.</summary>
    public sealed class BlockRenderer : MonoBehaviour
    {
        /// <summary>Радиус кольца скрытия потолка вокруг игрока (блоков, chebyshev по плану).</summary>
        private const int CutRingRadius = 10;
        private const float CutMoveThreshold = 0.2f;

        [SerializeField, Tooltip("Кольца проявления от проёмов; выкл = бинарный фолбэк 0/1.")]
        private bool _revealRings = true;
        [SerializeField, Tooltip("Уровень глаз для среза потолка (блоков от ног).")]
        private float _eyeHeight = 1.2f;
        [SerializeField, Tooltip("Cut-away по зонам сервера; выкл или нет зоны = эвристика.")]
        private bool _zoneCut = true;

        private BlockReveal _reveal;
        private BlockReveal _zoneReveal;
        private float _zoneFadeDistance = BlockReveal.Budget;
        private float _zoneFadeVertical = BlockReveal.VerticalStep;

        public void SetZoneFade(float distance, float vertical)
        {
            _zoneFadeDistance = Mathf.Max(1f, distance);
            _zoneFadeVertical = Mathf.Max(0f, vertical);
            _reveal?.Configure(_zoneFadeDistance, _zoneFadeVertical);
            _zoneReveal?.Configure(_zoneFadeDistance, _zoneFadeVertical);
        }

        private const int JunctionScanDown = 2;
        private const int JunctionScanUp = 5;
        private const float ZoneGraceSec = 1f;
        private ushort _lastPlayerZone;
        private float _lastPlayerZoneTime;
        private static readonly int[] JDirX = { 1, -1, 0, 0, 0, 0 };
        private static readonly int[] JDirY = { 0, 0, 1, -1, 0, 0 };
        private static readonly int[] JDirZ = { 0, 0, 0, 0, 1, -1 };

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

        private const int RoofScanUp = 12;

        private bool HasRoofAbove(int x, int y, int z)
        {
            for (int dy = 2; dy <= RoofScanUp; dy++)
                if (_grid.GetBlock(x, y + dy, z) != 0)
                    return true;
            return false;
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
        private LabelManager _labels;
        private readonly Dictionary<long, List<BlockView>> _sections = new();
        private readonly Stack<GameObject> _cubePool = new();
        private readonly Dictionary<GameObject, Stack<GameObject>> _prefabPools = new();

        // Кэш материалов по цвету: не мутируем материал примитива и не плодим инстанс на куб.
        private static readonly Dictionary<Color, Material> _materials = new();

        public void Init(BlockGrid grid, IBlockShapes shapes)
        {
            _grid = grid;
            _shapes = shapes;
            _reveal ??= new BlockReveal(CutWindow);
            _zoneReveal ??= new BlockReveal(CutWindow);
            _reveal.Configure(_zoneFadeDistance, _zoneFadeVertical);
            _zoneReveal.Configure(_zoneFadeDistance, _zoneFadeVertical);
            if (_labels == null)
                _labels = FindFirstObjectByType<LabelManager>();

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
                view.BottomOpen = !HasSolidTop(x, y - 1, z);
                int sizeY = def != null ? def.Size.y : 1;
                view.TopCellY = y + sizeY;
                view.TopCovered = def != null && def.TopMap != null
                                  && BlockAutoTex.CoveredAbove(_grid, x, y + sizeY, z);
                FeedTopMesh(go, def, type, x, y, z, rotSteps, view); // грид + top-debug
                if (def != null && def.Openable) // дверь-якорь: аниматор для тоггла без пересборки (снап на спавне — внутри SetDoor)
                    view.SetDoor(go.GetComponentInChildren<Animator>(true),
                                 Shared.World.Blocks.BlockState.GetOpen(_grid.GetState(x, y, z)));
                AttachFloorLabel(view, go.transform, x, y, z, type);
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
                view.BottomOpen = !HasSolidTop(x, y - 1, z);
                if (i == 0)
                    AttachFloorLabel(view, cube.transform, x, y, z, type);
                visuals.Add(view);
            }
        }

        private void AttachFloorLabel(BlockView view, Transform anchor, int x, int y, int z, ushort type)
        {
            if (_labels == null || !BlockCatalog.Get(type).IsFloorAnchor || !_grid.TryGetSeed(x, y, z, out var seed))
                return;
            view.FloorLabel = _labels.ShowWorldMessage(LabelKind.FloorLabel, BuildFloorText(in seed),
                                                       anchor, FloorLabelOffset(x, y, z));
        }

        private static string BuildFloorText(in FloorSeed seed)
        {
            string head = string.IsNullOrEmpty(seed.Name) ? string.Empty : $"<size=55%>{seed.Name}</size>\n";
            return head + $"<b>{seed.Floor}</b>";
        }

        // Монтаж лейбла: смежная стена → поднять к стене; иначе пол снизу → над блоком; ни того ни другого → над блоком (невалидно, дроп — сервер, 4b.3).
        private Vector3 FloorLabelOffset(int x, int y, int z)
        {
            if (HasCollisionAt(x + 1, y, z)) return new Vector3(0.45f, 1.6f, 0f);
            if (HasCollisionAt(x - 1, y, z)) return new Vector3(-0.45f, 1.6f, 0f);
            if (HasCollisionAt(x, y, z + 1)) return new Vector3(0f, 1.6f, 0.45f);
            if (HasCollisionAt(x, y, z - 1)) return new Vector3(0f, 1.6f, -0.45f);
            return new Vector3(0f, 0.7f, 0f);
        }

        private bool HasCollisionAt(int x, int y, int z)
            => _shapes.GetBoxes(_grid.GetBlock(x, y, z), _grid.GetState(x, y, z)).Length > 0;

        /// <summary>Cut-away: скрыть блоки выше глаз чужого стека в кольце вокруг игрока (звать каждый кадр).</summary>
        public void UpdateCutaway(float px, float py, float pz)
        {
            float eyeY = py + _eyeHeight;
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
                    if (!CutCandidate(v, eyeY, refY, px, pz))
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

            bool rings = _revealRings && _reveal != null;
            if (rings)
                _reveal.Recompute(_grid, _cutStartY, _cutOriginX, _cutOriginZ);

            ushort playerZone = 0;
            if (_zoneCut && _zoneReveal != null)
            {
                int fx = Mathf.FloorToInt(px), fy = Mathf.FloorToInt(py + 0.001f), fz = Mathf.FloorToInt(pz);
                playerZone = _grid.GetZone(fx, fy, fz);
                if (playerZone == 0)
                    playerZone = _grid.GetZone(fx, fy + 1, fz);
                if (playerZone != 0)
                {
                    _lastPlayerZone = playerZone;
                    _lastPlayerZoneTime = Time.time;
                }
                else if (_lastPlayerZone != 0 && Time.time - _lastPlayerZoneTime < ZoneGraceSec)
                {
                    playerZone = _lastPlayerZone;
                }
            }
            bool zonal = playerZone != 0;
            if (zonal)
                SeedZoneJunctions(playerZone, (int)refY);

            // Пасс 2: кандидаты + кап стен (сосед вскрыт не выше — гасим, чтобы стены не торчали колодцем).
            foreach (var kv in _sections)
            {
                var visuals = kv.Value;
                for (int i = 0; i < visuals.Count; i++)
                {
                    var v = visuals[i];
                    float a = CutAlphaAt(v.X, v.Y, v.Z, v.BottomOpen, v.BaseY, zonal, playerZone, eyeY, refY, px, pz, rings);
                    float tu = 0f;
                    if (v.TopCovered && a > 0.001f)
                        tu = 1f - CutAlphaAt(v.X, v.TopCellY, v.Z, null, int.MinValue, zonal, playerZone, eyeY, refY, px, pz, rings);
                    v.SetAlpha(a, tu);
                    if (v.FloorLabel != null)
                        v.FloorLabel.SetHidden(a <= 0.5f);
                }
            }
        }

        private float CutAlphaAt(int x, int y, int z, bool? bottomOpenKnown, int stackBase,
                                 bool zonal, ushort p, float eyeY, float refY, float px, float pz, bool rings)
        {
            if (zonal)
            {
                ushort below = _grid.GetZone(x, y - 1, z);
                ushort above = _grid.GetZone(x, y + 1, z);
                ushort xp = _grid.GetZone(x + 1, y, z);
                ushort xn = _grid.GetZone(x - 1, y, z);
                ushort zp = _grid.GetZone(x, y, z + 1);
                ushort zn = _grid.GetZone(x, y, z - 1);

                if (below == p || above == p || xp == p || xn == p || zp == p || zn == p)
                {
                    if (y >= eyeY && below == p
                        && (bottomOpenKnown ?? !HasSolidTop(x, y - 1, z)))
                        return _zoneReveal.AlphaFor(x, y, z, above);
                    return 1f;
                }

                if (below != 0 || above != 0 || xp != 0 || xn != 0 || zp != 0 || zn != 0)
                {
                    float best = 0f;
                    if (below != 0) best = Mathf.Max(best, _zoneReveal.AlphaFor(x, y, z, below));
                    if (above != 0 && above != below) best = Mathf.Max(best, _zoneReveal.AlphaFor(x, y, z, above));
                    if (xp != 0) best = Mathf.Max(best, _zoneReveal.AlphaFor(x, y, z, xp));
                    if (xn != 0 && xn != xp) best = Mathf.Max(best, _zoneReveal.AlphaFor(x, y, z, xn));
                    if (zp != 0) best = Mathf.Max(best, _zoneReveal.AlphaFor(x, y, z, zp));
                    if (zn != 0 && zn != zp) best = Mathf.Max(best, _zoneReveal.AlphaFor(x, y, z, zn));
                    return best;
                }

                if (y < eyeY)
                {
                    bool foreignBelow = false;
                    for (int dy = 1; dy <= 2 && !foreignBelow; dy++)
                    {
                        int yy = y - dy;
                        ushort c0 = _grid.GetZone(x, yy, z);
                        ushort c1 = _grid.GetZone(x + 1, yy, z);
                        ushort c2 = _grid.GetZone(x - 1, yy, z);
                        ushort c3 = _grid.GetZone(x, yy, z + 1);
                        ushort c4 = _grid.GetZone(x, yy, z - 1);
                        ushort c5 = _grid.GetZone(x + 1, yy, z + 1);
                        ushort c6 = _grid.GetZone(x + 1, yy, z - 1);
                        ushort c7 = _grid.GetZone(x - 1, yy, z + 1);
                        ushort c8 = _grid.GetZone(x - 1, yy, z - 1);
                        if (c0 == p || c1 == p || c2 == p || c3 == p || c4 == p
                            || c5 == p || c6 == p || c7 == p || c8 == p)
                            return 1f;
                        foreignBelow = c0 != 0 || c1 != 0 || c2 != 0 || c3 != 0 || c4 != 0
                                       || c5 != 0 || c6 != 0 || c7 != 0 || c8 != 0;
                    }
                    if (foreignBelow)
                        return 0f;
                    return (y < refY - 1f && HasRoofAbove(x, y, z)) ? 0f : 1f;
                }
                bool boZ = bottomOpenKnown ?? !HasSolidTop(x, y - 1, z);
                int sbZ = stackBase != int.MinValue ? stackBase : FindStackBase(x, y, z);
                bool cutZ = sbZ >= refY + 2 || boZ || NeighborCutAtOrBelow(x, z, y);
                return cutZ ? 0f : 1f;
            }

            if (y < eyeY)
                return 1f;
            if (Mathf.Max(Mathf.Abs(x + 0.5f - px), Mathf.Abs(z + 0.5f - pz)) > CutRingRadius)
                return 1f;
            bool bottomOpen = bottomOpenKnown ?? !HasSolidTop(x, y - 1, z);
            int sb = stackBase != int.MinValue ? stackBase : FindStackBase(x, y, z);
            bool cut = sb >= refY + 2 || bottomOpen || NeighborCutAtOrBelow(x, z, y);
            return !cut ? 1f : (rings ? _reveal.Alpha(x, y, z) : 0f);
        }

        private void SeedZoneJunctions(ushort p, int refY)
        {
            _zoneReveal.Begin(_cutOriginX, _cutOriginZ);
            int y0 = refY - JunctionScanDown, y1 = refY + JunctionScanUp;
            for (int lz = 0; lz < CutWindow; lz++)
                for (int lx = 0; lx < CutWindow; lx++)
                {
                    int x = _cutOriginX + lx, z = _cutOriginZ + lz;
                    for (int y = y0; y <= y1; y++)
                    {
                        if (_grid.GetZone(x, y, z) != p)
                            continue;
                        for (int d = 0; d < 6; d++)
                        {
                            int nx = x + JDirX[d], ny = y + JDirY[d], nz = z + JDirZ[d];
                            ushort nzone = _grid.GetZone(nx, ny, nz);
                            if (nzone != 0)
                            {
                                if (nzone != p)
                                    _zoneReveal.Seed(nx, nz, ny, nzone);
                                continue;
                            }
                            ushort gate = _grid.GetBlock(nx, ny, nz);
                            if (gate == 0 || !BlockCatalog.Get(gate).Openable)
                                continue;
                            int bx = x + JDirX[d] * 2, by = y + JDirY[d] * 2, bz = z + JDirZ[d] * 2;
                            ushort q = _grid.GetZone(bx, by, bz);
                            if (q != 0 && q != p)
                                _zoneReveal.Seed(bx, bz, by, q);
                        }
                    }
                }
            _zoneReveal.Spread();
        }

        private bool CutCandidate(BlockView v, float eyeY, float refY, float px, float pz)
        {
            if (v.Y < eyeY)
                return false;
            if (Mathf.Max(Mathf.Abs(v.X + 0.5f - px), Mathf.Abs(v.Z + 0.5f - pz)) > CutRingRadius)
                return false;
            return v.BaseY >= refY + 2 || v.BottomOpen;
        }

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
            if (v.FloorLabel != null)
            {
                v.FloorLabel.Dismiss();
                v.FloorLabel = null;
            }
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
