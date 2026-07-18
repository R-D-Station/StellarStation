using System.Collections.Generic;
using UnityEngine;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace Client.Map
{
    /// <summary>In-scene редактор блок-карт: держит BlockGrid + живую куб-визуализацию (не сохраняется в сцену).</summary>
    public sealed class BlockMapAuthoring : MonoBehaviour
    {
        [Tooltip("Файл карты v10 (путь от корня проекта).")]
        public string MapPath = "Assets/Map/block-station.smap";

        [Tooltip("Отладка: визуалы видны в иерархии (в сцену не сохраняются); на ячейках — BlockView (данные блока + значения верх-грида + дамп шейдера). Пересборка при переключении.")]
        public bool ShowVisualsInHierarchy = false;

        private HideFlags VisFlags => ShowVisualsInHierarchy ? HideFlags.DontSave : HideFlags.HideAndDontSave;

        [System.NonSerialized] public BlockGrid Grid;

        private Transform _visRoot;
        private readonly Dictionary<long, GameObject> _cells = new();
        private readonly Dictionary<long, BlockGizmo> _gizmoCells = new();

        public bool IsLoaded => Grid != null;

        public void NewMap()
        {
            Grid = new BlockGrid();
            RebuildVisual();
        }

        public void LoadMap()
        {
            Grid = BlockMapSerializer.LoadFromFile(MapPath);
            RebuildVisual();
        }

        public void SaveMap()
        {
            if (Grid != null)
                BlockMapSerializer.SaveToFile(MapPath, Grid);
        }

        /// <summary>Кисть: записать блок (y — высота) и обновить его куб + соседей (автотайл/автотекстуринг).</summary>
        public void PaintBlock(int x, int y, int z, ushort type)
        {
            if (Grid == null || !Grid.SetBlock(x, y, z, type))
                return;
            if (!BlockCatalog.Get(type).IsFloorAnchor)
                Grid.RemoveSeed(x, y, z);
            ApplyAttachFacing(x, y, z, type);
            UpdateCell(x, y, z, type);
            RefreshNeighborsVisual(x, y, z);
        }

        public void PaintSeed(int x, int y, int z, ushort type, string name, int rank, int floor)
        {
            if (Grid == null)
                return;
            PaintBlock(x, y, z, type);
            if (Grid.GetBlock(x, y, z) == type)
                Grid.SetSeed(x, y, z, new FloorSeed(name, rank, floor));
        }

        private static readonly System.Func<ushort, bool> AttachSolid = BlockAttach.DefaultIsSolid;

        private void ApplyAttachFacing(int x, int y, int z, ushort type)
        {
            var info = BlockCatalog.Get(type);
            if (!info.RequiresSupport)
                return;
            if (BlockAttach.Resolve(Grid, AttachSolid, x, y, z, info.AttachTo, out _, out int facing))
                Grid.SetState(x, y, z, BlockState.WithFacing(Grid.GetState(x, y, z), facing));
            else
                Debug.LogWarning($"[Attach] «{info.Name}» в ({x},{y},{z}) без опоры — сервер снесёт при загрузке.");
        }

        // Соседи по плану (8) + верх/низ: их формы верха/автотайл зависят от изменившейся ячейки.
        private void RefreshNeighborsVisual(int x, int y, int z)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    UpdateCell(x + dx, y, z + dz, Grid.GetBlock(x + dx, y, z + dz));
                }
            UpdateCell(x, y + 1, z, Grid.GetBlock(x, y + 1, z));
            UpdateCell(x, y - 1, z, Grid.GetBlock(x, y - 1, z));
        }

        public ushort GetBlock(int x, int y, int z) => Grid?.GetBlock(x, y, z) ?? (ushort)0;

        /// <summary>Поставить объект якорем в (x,y,z); true — если поставлен (все позиции футпринта были свободны).</summary>
        public bool PaintObject(int x, int y, int z, BlockDefinition def, int facing)
        {
            if (Grid == null || def == null || def.Type == 0)
                return false;

            int sx = def.Size.x, sy = def.Size.y, sz = def.Size.z;
            int parts = MultiBlock.PartCount(sx, sy, sz);

            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, sx, sz, facing, out int dx, out int dy, out int dz);
                if (Grid.GetBlock(x + dx, y + dy, z + dz) != 0)
                    return false; // занято — не ставим частично
            }

            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, sx, sz, facing, out int dx, out int dy, out int dz);
                Grid.SetBlock(x + dx, y + dy, z + dz, def.Type);
                Grid.SetState(x + dx, y + dy, z + dz,
                    BlockState.WithPart(BlockState.WithFacing(0, facing), p));
                UpdateCell(x + dx, y + dy, z + dz, def.Type);
                RefreshNeighborsVisual(x + dx, y + dy, z + dz);
            }
            return true;
        }

        /// <summary>Стереть объект по любой его части: мульти-блок уходит целиком (якорь по part+facing).</summary>
        public void EraseObject(int x, int y, int z)
        {
            if (Grid == null)
                return;
            ushort type = Grid.GetBlock(x, y, z);
            if (type == 0)
                return;

            var def = BlockDefinitionResolver.Find(type);
            int sx = def != null ? def.Size.x : 1, sy = def != null ? def.Size.y : 1, sz = def != null ? def.Size.z : 1;
            if (!MultiBlock.IsMulti(sx, sy, sz))
            {
                PaintBlock(x, y, z, 0);
                return;
            }

            byte st = Grid.GetState(x, y, z);
            MultiBlock.AnchorOf(x, y, z, BlockState.GetPart(st), sx, sz, BlockState.GetFacing(st),
                                out int ax, out int ay, out int az);
            int parts = MultiBlock.PartCount(sx, sy, sz);
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, sx, sz, BlockState.GetFacing(st), out int dx, out int dy, out int dz);
                if (Grid.SetBlock(ax + dx, ay + dy, az + dz, 0))
                {
                    UpdateCell(ax + dx, ay + dy, az + dz, 0);
                    RefreshNeighborsVisual(ax + dx, ay + dy, az + dz);
                }
            }
        }

        // ---- запекание (потолки/полы-интерьеры) ----

        [Tooltip("Пауза между установками при зажатой кисти в режиме «Присоединение» (сек).")]
        public float BrushInterval = 0.15f;

        [Tooltip("Показывать запечённый слой потолков (оранжевые пластины на нижних гранях).")]
        public bool ShowBakedCeilings = true;
        [Tooltip("Показывать запечённый слой полов-интерьеров (зелёные пластины на верхних гранях).")]
        public bool ShowBakedFloors = true;

        /// <summary>До скольких блоков вверх ищем крышу, решая «пол — интерьер станции».</summary>
        private const int InteriorRoofScan = 8;

        private Transform _bakeRoot;

        /// <summary>Запечь разметку потолков/полов-интерьеров аддитивно (ручные биты не трогает).</summary>
        public void BakeLayers()
        {
            if (Grid == null) return;

            // Снимок ключей: SetBake мутирует dirty-набор, но не секции; блоки не меняются.
            foreach (var key in new List<long>(Grid.Sections.Keys))
            {
                BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                for (int ly = 0; ly < ChunkSection.Size; ly++)
                    for (int lz = 0; lz < ChunkSection.Size; lz++)
                        for (int lx = 0; lx < ChunkSection.Size; lx++)
                        {
                            int x = cx * 16 + lx, y = cy * 16 + ly, z = cz * 16 + lz;
                            ushort type = Grid.GetBlock(x, y, z);
                            if (type == 0) continue;

                            byte bake = Grid.GetBake(x, y, z); // аддитивно: OR к существующему
                            if (SolidTop(x, y - 1, z) == false && HasAnyBox(type))
                                bake |= ChunkSection.BakeCeiling;
                            if (SolidTop(x, y, z) && Grid.GetBlock(x, y + 1, z) == 0 && HasRoofAbove(x, y, z))
                                bake |= ChunkSection.BakeInteriorFloor;
                            Grid.SetBake(x, y, z, bake);
                        }
            }
            RebuildBakeVisual();
        }

        /// <summary>Полный сброс бейка (ручного и автоматического) по всей карте.</summary>
        [ContextMenu("Remove Bake")]
        public void RemoveBake()
        {
            if (Grid == null) return;
            foreach (var key in new List<long>(Grid.Sections.Keys))
            {
                BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                for (int ly = 0; ly < ChunkSection.Size; ly++)
                    for (int lz = 0; lz < ChunkSection.Size; lz++)
                        for (int lx = 0; lx < ChunkSection.Size; lx++)
                            Grid.SetBake(cx * 16 + lx, cy * 16 + ly, cz * 16 + lz, 0);
            }
            RebuildBakeVisual();
        }

        /// <summary>Кисть бейка: выставить/снять бит блока. true — если изменилось (визуал перестроен).</summary>
        public bool SetBakeBit(int x, int y, int z, byte bit, bool on)
        {
            if (Grid == null) return false;
            byte cur = Grid.GetBake(x, y, z);
            byte next = on ? (byte)(cur | bit) : (byte)(cur & ~bit);
            if (!Grid.SetBake(x, y, z, next))
                return false;
            RebuildBakeVisual();
            return true;
        }

        private bool HasAnyBox(ushort type) => BlockCatalogShapes.Instance.GetBoxes(type, 0).Length > 0;

        // Есть ли у блока solid-верх (top ≈ 1.0).
        private bool SolidTop(int x, int y, int z)
        {
            var boxes = BlockCatalogShapes.Instance.GetBoxes(Grid.GetBlock(x, y, z), Grid.GetState(x, y, z));
            for (int i = 0; i < boxes.Length; i++)
                if (boxes[i].MaxYf >= 0.999f)
                    return true;
            return false;
        }

        private bool HasRoofAbove(int x, int y, int z)
        {
            for (int dy = 2; dy <= InteriorRoofScan; dy++)
                if (BlockCatalogShapes.Instance.GetBoxes(Grid.GetBlock(x, y + dy, z), Grid.GetState(x, y + dy, z)).Length > 0)
                    return true;
            return false;
        }

        /// <summary>Перестроить пластины-визуализацию бейка (editor-only, HideAndDontSave).</summary>
        public void RebuildBakeVisual()
        {
            if (_bakeRoot != null)
                DestroyCell(_bakeRoot.gameObject);
            _bakeRoot = new GameObject("BlockMapBake") { hideFlags = VisFlags }.transform;
            _bakeRoot.SetParent(transform, false);
            if (Grid == null) return;

            foreach (var kv in Grid.Sections)
            {
                BlockGrid.UnpackKey(kv.Key, out int cx, out int cy, out int cz);
                for (int ly = 0; ly < ChunkSection.Size; ly++)
                    for (int lz = 0; lz < ChunkSection.Size; lz++)
                        for (int lx = 0; lx < ChunkSection.Size; lx++)
                        {
                            byte bake = kv.Value.GetBake(ChunkSection.LocalIndex(lx, ly, lz));
                            if (bake == 0) continue;
                            int x = cx * 16 + lx, y = cy * 16 + ly, z = cz * 16 + lz;
                            // Пластины ВНЕ геометрии блока: потолок — под нижней гранью, пол — над верхней.
                            if (ShowBakedCeilings && (bake & ChunkSection.BakeCeiling) != 0)
                                SpawnCube(_bakeRoot, new Vector3(x + 0.5f, y - 0.05f, z + 0.5f),
                                          new Vector3(0.9f, 0.03f, 0.9f), new Color(1f, 0.55f, 0.1f));
                            if (ShowBakedFloors && (bake & ChunkSection.BakeInteriorFloor) != 0)
                                SpawnCube(_bakeRoot, new Vector3(x + 0.5f, y + 1.05f, z + 0.5f),
                                          new Vector3(0.9f, 0.03f, 0.9f), new Color(0.2f, 1f, 0.35f));
                        }
            }
        }

        // ---- визуализация ----

        // Ключ ячейки: 21 бит/ось со знаком (диапазона правки хватает с запасом).
        private static long CellKey(int bx, int by, int bz)
            => ((long)(bx & 0x1FFFFF)) | ((long)(by & 0x1FFFFF) << 21) | ((long)(bz & 0x1FFFFF) << 42);

        private void RebuildVisual()
        {
            if (_visRoot != null)
                DestroyCell(_visRoot.gameObject);
            _cells.Clear();
            _gizmoCells.Clear();

            _visRoot = new GameObject("BlockMapVisual") { hideFlags = VisFlags }.transform;
            _visRoot.SetParent(transform, false);

            if (Grid == null)
                return;

            foreach (var kv in Grid.Sections)
            {
                BlockGrid.UnpackKey(kv.Key, out int cx, out int cy, out int cz);
                for (int lz = 0; lz < ChunkSection.Size; lz++)
                    for (int ly = 0; ly < ChunkSection.Size; ly++)
                        for (int lx = 0; lx < ChunkSection.Size; lx++)
                        {
                            ushort type = kv.Value.GetBlock(ChunkSection.LocalIndex(lx, ly, lz));
                            if (type != 0)
                                UpdateCell(cx * 16 + lx, cy * 16 + ly, cz * 16 + lz, type);
                        }
            }
        }

        private void UpdateCell(int x, int y, int z, ushort type)
        {
            long key = CellKey(x, y, z);
            if (_cells.TryGetValue(key, out var old))
            {
                DestroyCell(old);
                _cells.Remove(key);
                _gizmoCells.Remove(key);
            }
            if (type == 0 || _visRoot == null)
                return;

            var cell = new GameObject($"b({x},{y},{z})") { hideFlags = VisFlags };
            cell.transform.SetParent(_visRoot, false);
            if (ShowVisualsInHierarchy) // рендер-диагностика: выбери ячейку в иерархии → инспектор BlockView
            {
                var cellView = cell.AddComponent<BlockView>();
                cellView.X = x; cellView.Y = y; cellView.Z = z;
            }

            var def = BlockDefinitionResolver.Find(type);
            // Автотайл: меш по 4 план-соседям (как в BlockRenderer); нет меша формы → фолбэк на Prefab.
            var prefab = BlockAutoTex.ResolveMesh(Grid, def, type, x, y, z, out int rotSteps);
            bool autotiled = prefab != null;
            if (prefab == null)
                prefab = def?.Prefab;
            if (prefab != null)
            {
                // Мульти-блок: визуал рисует только якорная часть (0), центрируясь по футпринту с поворотом.
                byte st = Grid.GetState(x, y, z);
                bool multi = def.Size.x * def.Size.y * def.Size.z > 1;
                if (multi && BlockState.GetPart(st) != 0) // не-якорная часть мульти-блока — визуал не рисуем
                {
                    _cells[key] = cell;
                    return;
                }

                var view = Instantiate(prefab, cell.transform);
                view.hideFlags = VisFlags;
                float pivotY = def.PivotYOffset; // Низ = 0; Центр = половина высоты объекта
                var shapeRot = Quaternion.Euler(0f, 90f * rotSteps, 0f);
                if (multi)
                {
                    int facing = BlockState.GetFacing(st);
                    view.transform.SetPositionAndRotation(
                        MultiBlockVisual.FootprintBottomCenter(x, y, z, def.Size.x, def.Size.z, facing)
                            + Vector3.up * pivotY,
                        autotiled ? shapeRot : MultiBlockVisual.FacingRotation(facing)); // автотайл повёрнут формой
                }
                else
                {
                    var rot = shapeRot;
                    if (!autotiled)
                    {
                        int facing = BlockState.GetFacing(st);
                        if (facing != 0)
                            rot = MultiBlockVisual.FacingRotation(facing);
                    }
                    view.transform.SetPositionAndRotation(new Vector3(x + 0.5f, y + pivotY, z + 0.5f), rot);
                }
                FeedTopMesh(view, def, type, x, y, z, rotSteps);
                var gizmo = view.GetComponentInChildren<BlockGizmo>(true);
                if (gizmo != null)
                    _gizmoCells[key] = gizmo;
                _cells[key] = cell;
                return;
            }

            var boxes = BlockCatalogShapes.Instance.GetBoxes(type, Grid.GetState(x, y, z));
            if (boxes.Length == 0)
            {
                SpawnCube(cell.transform, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), // маркер/без коллизии — кубик
                          new Vector3(0.3f, 0.3f, 0.3f), new Color(1f, 0.2f, 0.9f));
            }
            else
            {
                float t = 0.35f + 0.5f * ((type * 0.6180339887f) % 1f); // оттенок по типу (как в BlockDebugRenderer)
                foreach (var b in boxes)
                    SpawnCube(cell.transform,
                        new Vector3(x + (b.MinXf + b.MaxXf) * 0.5f, y + (b.MinYf + b.MaxYf) * 0.5f, z + (b.MinZf + b.MaxZf) * 0.5f),
                        new Vector3(b.MaxXf - b.MinXf, b.MaxYf - b.MinYf, b.MaxZf - b.MinZf), new Color(t, t, t));
            }
            _cells[key] = cell;
        }

        // Делегирует BlockAutoTex.FeedGrids (верх+бок через BlockMaterials); top-параметры — в BlockView при отладке.
        private void FeedTopMesh(GameObject instance, BlockDefinition def, ushort type, int x, int y, int z, int rotSteps)
        {
            var p = BlockAutoTex.FeedGrids(instance, Grid, def, type, x, y, z, rotSteps, out byte corners);
            if (ShowVisualsInHierarchy)
            {
                var bv = instance.GetComponentInParent<BlockView>(); // BlockView висит на ячейке-родителе
                if (bv != null)
                    bv.SetTopDebug(in p, corners);
            }
        }

        // Кэш материалов по цвету: не мутируем дефолтный материал примитива и не плодим инстанс на куб.
        private static readonly Dictionary<Color, Material> _materials = new();

        private void SpawnCube(Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.hideFlags = VisFlags;
            DestroyCell(cube.GetComponent<Collider>());
            cube.transform.SetParent(parent, false);
            cube.transform.position = position;
            cube.transform.localScale = scale;

            var r = cube.GetComponent<Renderer>();
            if (r == null) return;
            if (!_materials.TryGetValue(color, out var mat) || mat == null)
            {
                mat = new Material(r.sharedMaterial) { color = color, hideFlags = HideFlags.HideAndDontSave };
                _materials[color] = mat;
            }
            r.sharedMaterial = mat;
        }

        private static void DestroyCell(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

#if UNITY_EDITOR
        private bool _lastShowVis;
        private bool _calibRebuildQueued;

        private void OnEnable() => TopGridCalibration.Changed += QueueCalibRebuild;

        // Живая перерисовка карты при правке калибровки верха (delayCall: источник — OnValidate ассета).
        private void QueueCalibRebuild()
        {
            if (_calibRebuildQueued || Grid == null) return;
            _calibRebuildQueued = true;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                _calibRebuildQueued = false;
                if (this != null && Grid != null)
                    RebuildVisual();
            };
        }

        // Смена тумблера отладки → пересборка визуалов новыми hideFlags (delayCall: Destroy в OnValidate запрещён).
        private void OnValidate()
        {
            if (_lastShowVis == ShowVisualsInHierarchy) return;
            _lastShowVis = ShowVisualsInHierarchy;
            if (Grid == null) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && Grid != null)
                {
                    RebuildVisual();
                    RebuildBakeVisual();
                }
            };
        }

        // Быстрый дамп рендер-состояния первых визуалов в консоль (не требует тумблера иерархии).
        [ContextMenu("Диагностика рендера (в консоль)")]
        private void DumpRenderDiag()
        {
            if (_cells.Count == 0)
            {
                Debug.Log("[BlockMapAuthoring] нет визуалов — перезагрузи/перекрась карту.");
                return;
            }
            int n = 0;
            foreach (var kv in _cells)
            {
                if (kv.Value == null || kv.Value.GetComponentInChildren<MeshRenderer>(true) == null)
                    continue;
                Debug.Log(BlockView.Dump(kv.Value), kv.Value);
                if (++n >= 5) break;
            }
            if (n == 0)
                Debug.Log("[BlockMapAuthoring] визуалы без MeshRenderer (маркеры/кубы?).");
        }

        // Диагностика автотайла: находит аномалии — форма Single, хотя рядом (план или y±1) есть тот же тип.
        [ContextMenu("Диагностика автотайла (в консоль)")]
        private void DumpAutotileDiag()
        {
            if (Grid == null) { Debug.Log("[Autotile] нет карты."); return; }
            int connectable = 0, shown = 0;
            foreach (var kv in Grid.Sections)
            {
                BlockGrid.UnpackKey(kv.Key, out int cx, out int cy, out int cz);
                var sec = kv.Value;
                for (int ly = 0; ly < ChunkSection.Size; ly++)
                    for (int lz = 0; lz < ChunkSection.Size; lz++)
                        for (int lx = 0; lx < ChunkSection.Size; lx++)
                        {
                            ushort t = sec.GetBlock(ChunkSection.LocalIndex(lx, ly, lz));
                            if (t == 0) continue;
                            var def = BlockDefinitionResolver.Find(t);
                            if (def?.Connection == null || !def.Connection.UseConnections) continue;
                            connectable++;

                            int wx = cx * 16 + lx, wy = cy * 16 + ly, wz = cz * 16 + lz;
                            BlockAutoTex.Resolve(Grid, def, t, wx, wy, wz, out var shape, out _, out _);
                            if (shape != Shared.World.WallShape.Single)
                                continue; // не одиночный — соединился, не аномалия
                            // аномалия только если тот же тип реально рядом (иначе Single корректен)
                            if (!SameTypePlan(wx, wy, wz, t) && !SameTypePlan(wx, wy - 1, wz, t) && !SameTypePlan(wx, wy + 1, wz, t))
                                continue;

                            Debug.Log($"[Autotile] ⚠ ({wx},{wy},{wz}) '{def.DisplayName}' форма=Single, но свой тип рядом. " +
                                $"N[{Col(wx, wy, wz + 1, t)} conn={BlockAutoTex.ConnectsTo(Grid, def, t, wx, wy, wz + 1)}] " +
                                $"E[{Col(wx + 1, wy, wz, t)} conn={BlockAutoTex.ConnectsTo(Grid, def, t, wx + 1, wy, wz)}] " +
                                $"S[{Col(wx, wy, wz - 1, t)} conn={BlockAutoTex.ConnectsTo(Grid, def, t, wx, wy, wz - 1)}] " +
                                $"W[{Col(wx - 1, wy, wz, t)} conn={BlockAutoTex.ConnectsTo(Grid, def, t, wx - 1, wy, wz)}] " +
                                $"| ConnectsToSameType={def.Connection.ConnectsToSameType}");
                            if (++shown >= 12) { Debug.Log("[Autotile] …обрезано на 12."); return; }
                        }
            }
            Debug.Log($"[Autotile] connectable-блоков: {connectable}, аномалий (Single рядом со своими): {shown}.");
        }

        // Есть ли в плане (4 соседа на уровне y) блок типа t.
        private bool SameTypePlan(int x, int y, int z, ushort t)
            => Grid.GetBlock(x, y, z + 1) == t || Grid.GetBlock(x + 1, y, z) == t
               || Grid.GetBlock(x, y, z - 1) == t || Grid.GetBlock(x - 1, y, z) == t;

        // Вертикальный срез соседней колонны (y-1/y/y+1); * помечает свой тип t.
        private string Col(int nx, int wy, int nz, ushort t)
        {
            ushort a = Grid.GetBlock(nx, wy - 1, nz), b = Grid.GetBlock(nx, wy, nz), c = Grid.GetBlock(nx, wy + 1, nz);
            return $"y-1={a}{(a == t ? "*" : "")} y={b}{(b == t ? "*" : "")} y+1={c}{(c == t ? "*" : "")}";
        }
#endif

        private void OnDisable()
        {
#if UNITY_EDITOR
            TopGridCalibration.Changed -= QueueCalibRebuild;
#endif
            if (_visRoot != null)
                DestroyCell(_visRoot.gameObject);
            if (_bakeRoot != null)
                DestroyCell(_bakeRoot.gameObject);
            _cells.Clear();
            _gizmoCells.Clear();
        }

        private void OnDrawGizmos()
        {
            foreach (var kv in _gizmoCells)
                if (kv.Value != null)
                    kv.Value.Draw();
        }
    }
}
