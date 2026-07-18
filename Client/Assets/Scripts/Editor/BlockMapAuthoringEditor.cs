#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Client.Map;

namespace Client.Editor.Inspectors
{
    /// <summary>SceneView-кисть блок-карты <see cref="BlockMapAuthoring"/>: слой = высота Y, ЛКМ красит / Shift+ЛКМ стирает / [ ] меняют слой.</summary>
    [CustomEditor(typeof(BlockMapAuthoring))]
    public sealed class BlockMapAuthoringEditor : UnityEditor.Editor
    {
        private static int _layer; // высота Y текущего слоя (общая на сессию редактора)
        private static int _mode;  // 0=Слой, 1=Присоединение, 2=Потолки, 3=Полы
        private static int _facing; // поворот мульти-блока (R в SceneView, 90° по часовой)
        private static double _lastBrushTime; // пауза кисти присоединения (BrushInterval)
        private static readonly Vector3[] _cellRect = new Vector3[4]; // подсветка ячейки слоя (без аллокаций)
        private static readonly string[] ModeNames = { "Слой", "Присоед.", "Потолки", "Полы" };

        // Поля сида кисти FloorAnchor (видны в GUI только при выбранной категории FloorAnchor).
        private static string _seedName = "Станция";
        private static int _seedRank = 0;
        private static int _seedFloor = 1;
        private static bool _showZones;

        private const int ZoneQuadCap = 3000; // потолок квадов превью — без него ZoneFlood на большой карте вешает SceneView
        private Shared.World.Blocks.ZoneFloodResult _zonesPreview;
        private readonly List<Vector3> _zoneQuadPos = new();
        private readonly List<Color> _zoneQuadColor = new();
        private readonly HashSet<ushort> _conflictZones = new(); // Id зон с конфликтом этажей — красная подсветка сидов

        private BlockDefinition[] _palette;
        private string[] _paletteNames;
        private int _paletteIndex;

        // Палитра, сгруппированная по категории — источник двух списков кисти (Категория → Блок).
        private Shared.World.Blocks.BlockCategory[] _categories;
        private string[] _categoryNames;
        private int[][] _paletteByCategory;
        private string[][] _blockNamesByCategory;
        private int _categoryIndex;
        private int _blockIndex;

        private void OnEnable()
        {
            BlockDefinitionResolver.Invalidate(); // ассеты могли добавиться/переехать — кэш визуала заново
            var defs = BlockCatalogCodegen.LoadAllDefinitions();
            defs.Sort((a, b) => a.Type.CompareTo(b.Type));
            _palette = defs.ToArray();
            _paletteNames = new string[_palette.Length];
            for (int i = 0; i < _palette.Length; i++)
                _paletteNames[i] = $"{_palette[i].Type} — {_palette[i].DisplayName}";
            BuildCategoryGroups();
        }

        // Одноразовая раскладка палитры по категориям (порядок enum, счётчик блоков в подписи).
        private void BuildCategoryGroups()
        {
            var order = new List<Shared.World.Blocks.BlockCategory>();
            var buckets = new Dictionary<Shared.World.Blocks.BlockCategory, List<int>>();
            for (int i = 0; i < _palette.Length; i++)
            {
                var c = _palette[i].Category;
                if (!buckets.TryGetValue(c, out var list))
                {
                    list = new List<int>();
                    buckets[c] = list;
                    order.Add(c);
                }
                list.Add(i);
            }
            order.Sort((a, b) => ((byte)a).CompareTo((byte)b));

            _categories = order.ToArray();
            _categoryNames = new string[_categories.Length];
            _paletteByCategory = new int[_categories.Length][];
            _blockNamesByCategory = new string[_categories.Length][];
            for (int ci = 0; ci < _categories.Length; ci++)
            {
                var list = buckets[_categories[ci]];
                _categoryNames[ci] = $"{_categories[ci]} ({list.Count})";
                _paletteByCategory[ci] = list.ToArray();
                var names = new string[list.Count];
                for (int k = 0; k < list.Count; k++)
                    names[k] = _paletteNames[list[k]];
                _blockNamesByCategory[ci] = names;
            }
        }

        public override void OnInspectorGUI()
        {
            var t = (BlockMapAuthoring)target;

            _mode = GUILayout.Toolbar(_mode, ModeNames);
            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
                t.RebuildBakeVisual(); // тумблеры показа бейк-слоёв

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Новая")) { t.NewMap(); t.RebuildBakeVisual(); if (_showZones) RecomputeZonesPreview(t); }
                using (new EditorGUI.DisabledScope(!System.IO.File.Exists(t.MapPath)))
                    if (GUILayout.Button("Загрузить")) TryIO(() => { t.LoadMap(); t.RebuildBakeVisual(); if (_showZones) RecomputeZonesPreview(t); });
                using (new EditorGUI.DisabledScope(!t.IsLoaded))
                    if (GUILayout.Button("Сохранить")) TryIO(() => { t.SaveMap(); AssetDatabase.Refresh(); });
            }

            using (new EditorGUI.DisabledScope(!t.IsLoaded))
                if (GUILayout.Button("Запечь потолки/полы"))
                    t.BakeLayers();

            EditorGUILayout.Space(6);
            _layer = EditorGUILayout.IntField(new GUIContent("Слой (Y)", "Высота кисти; [ и ] в SceneView."), _layer);

            if (_palette.Length == 0)
            {
                EditorGUILayout.HelpBox("Нет BlockDefinition-ассетов — создай (Create → Station → Block Definition).", MessageType.Warning);
            }
            else
            {
                _categoryIndex = Mathf.Clamp(_categoryIndex, 0, _categories.Length - 1);
                int newCat = EditorGUILayout.Popup(new GUIContent("Категория"), _categoryIndex, _categoryNames);
                if (newCat != _categoryIndex) { _categoryIndex = newCat; _blockIndex = 0; }

                var inCat = _paletteByCategory[_categoryIndex];
                _blockIndex = Mathf.Clamp(_blockIndex, 0, inCat.Length - 1);
                _blockIndex = EditorGUILayout.Popup(new GUIContent("Блок"), _blockIndex, _blockNamesByCategory[_categoryIndex]);
                _paletteIndex = inCat[_blockIndex];

                if (_categories[_categoryIndex] == Shared.World.Blocks.BlockCategory.FloorAnchor)
                {
                    _seedName = EditorGUILayout.TextField(new GUIContent("Имя", "Имя станции/этажа (лейбл блока этажа)."), _seedName);
                    _seedRank = EditorGUILayout.IntField(new GUIContent("Ранг", "Меньший = истина. Игрок >0, админ/мапер <1."), _seedRank);
                    _seedFloor = EditorGUILayout.IntField(new GUIContent("Этаж", "Номер этажа зоны."), _seedFloor);
                }
            }

            using (new EditorGUI.DisabledScope(!t.IsLoaded))
            using (new EditorGUILayout.HorizontalScope())
            {
                bool show = GUILayout.Toggle(_showZones, new GUIContent("Показать зоны", "Превью флуда зон: пол зоны тонируется цветом, сиды/стыки/конфликты — кубы."), "Button");
                if (show != _showZones)
                {
                    _showZones = show;
                    if (show)
                        RecomputeZonesPreview(t);
                }
                if (GUILayout.Button(new GUIContent("Пересчитать зоны", "Прогнать ZoneFlood по текущей карте заново.")))
                {
                    _showZones = true;
                    RecomputeZonesPreview(t);
                }
            }

            if (!t.IsLoaded)
                EditorGUILayout.HelpBox("Карта не загружена: «Новая» или «Загрузить».", MessageType.Info);
            else
                EditorGUILayout.HelpBox("SceneView: ЛКМ — красить, Shift+ЛКМ — стирать, [ / ] — слой.", MessageType.None);
        }

        private static void TryIO(System.Action action)
        {
            try { action(); }
            catch (System.Exception e) { Debug.LogError($"[BlockMapAuthoring] {e.Message}"); }
        }

        private void RecomputeZonesPreview(BlockMapAuthoring t)
        {
            if (t.Grid == null)
                return;
            _zonesPreview = Shared.World.Blocks.ZoneFlood.Recompute(t.Grid, Shared.World.Blocks.CatalogZoneClassifier.Instance);
            _conflictZones.Clear();
            foreach (var c in _zonesPreview.Conflicts)
                _conflictZones.Add(c.ZoneId);
            BuildZoneDrawList(t);

            Debug.Log($"[Zones] зон: {_zonesPreview.Zones.Count}, стыков: {_zonesPreview.Junctions.Count}, конфликтов: {_zonesPreview.Conflicts.Count}");
            foreach (var z in _zonesPreview.Zones)
                Debug.Log($"[Zones] зона {z.Id}: «{z.Name}» этаж {z.Floor}, ранг {z.Rank}, сидов {z.Seeds.Count}");
            foreach (var c in _zonesPreview.Conflicts)
                Debug.LogWarning($"[Zones] КОНФЛИКТ: зона {c.ZoneId} — номера этажей {string.Join(", ", c.Floors)}");
        }

        private void BuildZoneDrawList(BlockMapAuthoring t)
        {
            _zoneQuadPos.Clear();
            _zoneQuadColor.Clear();
            var g = t.Grid;
            bool capped = false;
            foreach (var kv in g.Sections)
            {
                Shared.World.Blocks.BlockGrid.UnpackKey(kv.Key, out int cx, out int cy, out int cz);
                for (int ly = 0; ly < Shared.World.Blocks.ChunkSection.Size && !capped; ly++)
                    for (int lz = 0; lz < Shared.World.Blocks.ChunkSection.Size; lz++)
                        for (int lx = 0; lx < Shared.World.Blocks.ChunkSection.Size; lx++)
                        {
                            int x = cx * 16 + lx, y = cy * 16 + ly, z = cz * 16 + lz;
                            ushort zid = g.GetZone(x, y, z);
                            if (zid == 0)
                                continue;
                            bool air = g.GetBlock(x, y, z) == 0;
                            if (air && g.GetBlock(x, y - 1, z) == 0)
                                continue; // квад кладём на пол зоны — воздух без пола под ним пропускаем
                            if (_zoneQuadPos.Count >= ZoneQuadCap)
                            {
                                capped = true;
                                break;
                            }
                            _zoneQuadPos.Add(new Vector3(x, y + 0.03f, z));
                            _zoneQuadColor.Add(ZoneColor(zid, 0.22f));
                        }
                if (capped)
                    break;
            }
            if (capped)
                Debug.LogWarning($"[Zones] превью обрезано: показаны первые {ZoneQuadCap} ячеек пола зон.");
        }

        private static Color ZoneColor(ushort id, float alpha)
        {
            var c = Color.HSVToRGB((id * 0.618034f) % 1f, 0.75f, 1f);
            c.a = alpha;
            return c;
        }

        private void DrawZonePreview()
        {
            for (int i = 0; i < _zoneQuadPos.Count; i++)
            {
                var p = _zoneQuadPos[i];
                _cellRect[0] = p;
                _cellRect[1] = new Vector3(p.x + 1f, p.y, p.z);
                _cellRect[2] = new Vector3(p.x + 1f, p.y, p.z + 1f);
                _cellRect[3] = new Vector3(p.x, p.y, p.z + 1f);
                Handles.DrawSolidRectangleWithOutline(_cellRect, _zoneQuadColor[i], Color.clear);
            }

            foreach (var zone in _zonesPreview.Zones)
            {
                bool conflict = _conflictZones.Contains(zone.Id);
                Handles.color = conflict ? Color.red : ZoneColor(zone.Id, 1f);
                foreach (var seed in zone.Seeds)
                    Handles.DrawWireCube(new Vector3(seed.Pos.X + 0.5f, seed.Pos.Y + 0.5f, seed.Pos.Z + 0.5f),
                                         new Vector3(0.9f, 0.9f, 0.9f));
            }

            Handles.color = Color.yellow;
            foreach (var j in _zonesPreview.Junctions)
                foreach (var cell in j.DoorCells)
                    Handles.DrawWireCube(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f),
                                         new Vector3(1.04f, 1.04f, 1.04f));
        }

        private void OnSceneGUI()
        {
            var t = (BlockMapAuthoring)target;
            if (t.IsLoaded && _showZones && _zonesPreview != null)
                DrawZonePreview();
            if (!t.IsLoaded || _palette.Length == 0)
                return;

            Event e = Event.current;

            // Не даём кликам снимать выделение с объекта (стандартный приём кистей).
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.RightBracket) { _layer++; e.Use(); Repaint(); }
                else if (e.keyCode == KeyCode.LeftBracket) { _layer--; e.Use(); Repaint(); }
                else if (e.keyCode == KeyCode.R) { _facing = (_facing + 1) & 3; e.Use(); Repaint(); }
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

            if (_mode == 0)
                LayerMode(t, e, ray);
            else
                RaycastModes(t, e, ray);

            SceneView.RepaintAll();
        }

        // Режим «Слой»: кисть по горизонтальной плоскости высоты _layer.
        private void LayerMode(BlockMapAuthoring t, Event e, Ray ray)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, _layer, 0f));
            if (!plane.Raycast(ray, out float enter))
                return;
            Vector3 hit = ray.GetPoint(enter);
            int bx = Mathf.FloorToInt(hit.x);
            int bz = Mathf.FloorToInt(hit.z);

            DrawGrid(bx, bz);

            // Плоская подсветка ячейки НА плоскости слоя — якорь для глаза (призрак-куб стоит НА ней,
            // занимая объём будущего блока [y..y+1]).
            _cellRect[0] = new Vector3(bx, _layer, bz);
            _cellRect[1] = new Vector3(bx + 1, _layer, bz);
            _cellRect[2] = new Vector3(bx + 1, _layer, bz + 1);
            _cellRect[3] = new Vector3(bx, _layer, bz + 1);
            Handles.DrawSolidRectangleWithOutline(_cellRect, new Color(0.2f, 1f, 0.4f, 0.08f), new Color(1f, 1f, 1f, 0.4f));

            bool erase = e.shift;
            if (erase)
                DrawEraseGhost(t, new Vector3Int(bx, _layer, bz));
            else
                DrawGhost(t, bx, _layer, bz, erase: false);

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
            {
                PlaceOrErase(t, bx, _layer, bz, erase);
                e.Use();
            }
        }

        // Общая установка/стирание для режимов Слой и Присоединение: мульти-блоки — футпринтом с _facing.
        private void PlaceOrErase(BlockMapAuthoring t, int x, int y, int z, bool erase)
        {
            if (erase)
            {
                t.EraseObject(x, y, z);
                return;
            }
            var def = _palette[_paletteIndex];
            if (def.Size.x * def.Size.y * def.Size.z > 1)
                t.PaintObject(x, y, z, def, _facing);
            else if (def.Category == Shared.World.Blocks.BlockCategory.FloorAnchor)
                t.PaintSeed(x, y, z, def.Type, _seedName, _seedRank, _seedFloor);
            else
                t.PaintBlock(x, y, z, def.Type);
        }

        // Призрак стирания: весь объект под курсором (мульти-блок подсвечивается целиком — он единая структура).
        private static void DrawEraseGhost(BlockMapAuthoring t, Vector3Int cell)
        {
            Handles.color = new Color(1f, 0.3f, 0.2f);
            ushort type = t.GetBlock(cell.x, cell.y, cell.z);
            var def = type != 0 ? BlockDefinitionResolver.Find(type) : null;
            if (def == null || def.Size.x * def.Size.y * def.Size.z <= 1)
            {
                Handles.DrawWireCube(new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f), Vector3.one);
                return;
            }

            byte st = t.Grid.GetState(cell.x, cell.y, cell.z);
            int facing = Shared.World.Blocks.BlockState.GetFacing(st);
            Shared.World.Blocks.MultiBlock.AnchorOf(cell.x, cell.y, cell.z,
                Shared.World.Blocks.BlockState.GetPart(st), def.Size.x, def.Size.z, facing,
                out int ax, out int ay, out int az);
            int parts = Shared.World.Blocks.MultiBlock.PartCount(def.Size.x, def.Size.y, def.Size.z);
            for (int p = 0; p < parts; p++)
            {
                Shared.World.Blocks.MultiBlock.PartWorldOffset(p, def.Size.x, def.Size.z, facing,
                    out int dx, out int dy, out int dz);
                Handles.DrawWireCube(new Vector3(ax + dx + 0.5f, ay + dy + 0.5f, az + dz + 0.5f), Vector3.one);
            }
        }

        // Призрак: одиночный куб либо футпринт мульти-блока (с учётом поворота R).
        private void DrawGhost(BlockMapAuthoring t, int x, int y, int z, bool erase)
        {
            Handles.color = erase ? new Color(1f, 0.3f, 0.2f) : new Color(0.2f, 1f, 0.4f);
            var def = _palette[_paletteIndex];
            int parts = erase ? 1 : def.Size.x * def.Size.y * def.Size.z;
            if (parts <= 1)
            {
                Handles.DrawWireCube(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), Vector3.one);
                return;
            }
            for (int p = 0; p < parts; p++)
            {
                Shared.World.Blocks.MultiBlock.PartWorldOffset(p, def.Size.x, def.Size.z, _facing,
                    out int dx, out int dy, out int dz);
                Handles.DrawWireCube(new Vector3(x + dx + 0.5f, y + dy + 0.5f, z + dz + 0.5f), Vector3.one);
            }
        }

        // Режимы по рейкасту в существующие блоки: «Присоединение» (ставим к грани) и правка бейка.
        private void RaycastModes(BlockMapAuthoring t, Event e, Ray ray)
        {
            if (!RaycastGrid(t, ray, 200f, out var hit, out var prev))
                return;

            if (_mode == 1) // Присоединение: призрак на соседней от грани ячейке; Shift — стереть сам объект
            {
                bool erase = e.shift;
                var cell = erase ? hit : prev;
                if (erase)
                    DrawEraseGhost(t, cell);           // весь мульти-объект под курсором
                else
                    DrawGhost(t, cell.x, cell.y, cell.z, erase: false); // футпринт мульти-блока

                if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
                {
                    // Пауза кисти (BrushInterval): при зажатии блоки не сыплются очередью.
                    double now = EditorApplication.timeSinceStartup;
                    if (e.type == EventType.MouseDown || now - _lastBrushTime >= t.BrushInterval)
                    {
                        _lastBrushTime = now;
                        PlaceOrErase(t, cell.x, cell.y, cell.z, erase);
                    }
                    e.Use();
                }
            }
            else // Потолки/Полы: зажатая ЛКМ рисует бит, Shift+ЛКМ — стирает (как в остальных режимах)
            {
                byte bit = _mode == 2 ? Shared.World.Blocks.ChunkSection.BakeCeiling
                                      : Shared.World.Blocks.ChunkSection.BakeInteriorFloor;
                bool erase = e.shift;
                Handles.color = erase ? new Color(1f, 0.3f, 0.2f)
                                      : (_mode == 2 ? new Color(1f, 0.55f, 0.1f) : new Color(0.2f, 1f, 0.35f));
                Handles.DrawWireCube(new Vector3(hit.x + 0.5f, hit.y + 0.5f, hit.z + 0.5f), Vector3.one);

                if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
                {
                    t.SetBakeBit(hit.x, hit.y, hit.z, bit, on: !erase);
                    e.Use();
                }
            }
        }

        // Воксельный DDA (Amanatides-Woo): первый не-Air блок по лучу; prev — ячейка перед ним (грань входа).
        private static bool RaycastGrid(BlockMapAuthoring t, Ray ray, float maxDist, out Vector3Int hit, out Vector3Int prev)
        {
            Vector3 o = ray.origin;
            Vector3 d = ray.direction.normalized;
            var cell = new Vector3Int(Mathf.FloorToInt(o.x), Mathf.FloorToInt(o.y), Mathf.FloorToInt(o.z));
            prev = cell;

            var step = new Vector3Int(d.x > 0 ? 1 : -1, d.y > 0 ? 1 : -1, d.z > 0 ? 1 : -1);
            Vector3 tMax = new Vector3(Bound(o.x, d.x), Bound(o.y, d.y), Bound(o.z, d.z));
            Vector3 tDelta = new Vector3(Delta(d.x), Delta(d.y), Delta(d.z));

            for (float dist = 0f; dist < maxDist;)
            {
                if (t.GetBlock(cell.x, cell.y, cell.z) != 0)
                {
                    hit = cell;
                    return true;
                }
                prev = cell;
                if (tMax.x <= tMax.y && tMax.x <= tMax.z) { cell.x += step.x; dist = tMax.x; tMax.x += tDelta.x; }
                else if (tMax.y <= tMax.z) { cell.y += step.y; dist = tMax.y; tMax.y += tDelta.y; }
                else { cell.z += step.z; dist = tMax.z; tMax.z += tDelta.z; }
            }
            hit = default;
            return false;

            static float Bound(float p, float dir)
            {
                if (Mathf.Approximately(dir, 0f)) return float.PositiveInfinity;
                float cellEdge = dir > 0 ? Mathf.Floor(p) + 1f : Mathf.Floor(p);
                return (cellEdge - p) / dir;
            }

            static float Delta(float dir) => Mathf.Approximately(dir, 0f) ? float.PositiveInfinity : Mathf.Abs(1f / dir);
        }

        // Сетка 9×9 вокруг курсора на плоскости слоя — видно, куда ляжет кисть.
        private static void DrawGrid(int cx, int cy)
        {
            Handles.color = new Color(1f, 1f, 1f, 0.18f);
            const int r = 4;
            for (int i = -r; i <= r + 1; i++)
            {
                Handles.DrawLine(new Vector3(cx + i, _layerF, cy - r), new Vector3(cx + i, _layerF, cy + r + 1));
                Handles.DrawLine(new Vector3(cx - r, _layerF, cy + i), new Vector3(cx + r + 1, _layerF, cy + i));
            }
        }

        private static float _layerF => _layer;
    }
}
#endif
