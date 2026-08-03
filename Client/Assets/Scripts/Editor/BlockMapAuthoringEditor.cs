#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Client.Map;

namespace Client.Editor.Inspectors
{
    /// <summary>SceneView-кисть блок-карты <see cref="BlockMapAuthoring"/>: режим (Слой/Присоед./Вставка) = КАК ставим, раздел (Блоки/Предметы/Маркеры) = ЧТО ставим.</summary>
    [CustomEditor(typeof(BlockMapAuthoring))]
    public sealed class BlockMapAuthoringEditor : UnityEditor.Editor
    {
        private static int _layer; // высота Y текущего слоя (общая на сессию редактора)
        private static int _mode;
        private static int _section; // Блоки/Предметы/Маркеры — для Предметов/Маркеров валиден только режим «Вставка»
        private static int _markerIndex; // индекс в MarkerNames (0/1 — бейк потолка/пола, 2+ — маркерный блок)
        private static int _markerBlockIndex; // индекс блока внутри выбранной маркерной категории
        private static int _facing; // поворот мульти-блока (R в SceneView, 90° по часовой)
        private static double _lastBrushTime; // пауза кисти присоединения (BrushInterval)
        private static readonly Vector3[] _cellRect = new Vector3[4]; // подсветка ячейки слоя (без аллокаций)
        private static bool _hasFail;
        private static Vector3Int _failCell;
        private static readonly System.Collections.Generic.List<BlockMapAuthoring.ShaftRect> _shafts = new();
        private static bool _shaftsDirty = true;
        private static readonly Color ShaftWire = new Color(0.2f, 0.95f, 1f);
        private static readonly string[] ModeNames = { "Слой", "Присоед.", "Вставка" };
        private static readonly string[] SectionNames = { "Блоки", "Предметы", "Маркеры" };
        private static readonly string[] MarkerNames = { "Потолки", "Полы", "Marker", "Divider", "MergeMarker", "Точка спавна" };
        private static readonly string[] FacingNames = { "Север", "Восток", "Юг", "Запад" };
        private static readonly Shared.World.Blocks.BlockCategory[] MarkerBlockCats =
        {
            Shared.World.Blocks.BlockCategory.Marker,
            Shared.World.Blocks.BlockCategory.Divider,
            Shared.World.Blocks.BlockCategory.MergeMarker,
            Shared.World.Blocks.BlockCategory.SpawnPoint
        };
        private static int _itemStack = 1; // размер стака кисти предметов (1–255, кламп в DrawItemPicker)

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

        private const int MarkerOverlayCap = 3000;
        private readonly List<Vector3> _dividerCells = new();
        private readonly List<Vector3> _mergeCells = new();
        private bool _markerOverlayDirty = true;

        private BlockDefinition[] _palette;
        private string[] _paletteNames;
        private int _paletteIndex;

        private Client.Items.ItemDefinition[] _itemDefs = System.Array.Empty<Client.Items.ItemDefinition>(); // палитра раздела «Предметы», сорт по ItemDefId
        private string[] _itemNames = System.Array.Empty<string>();
        private int _itemIndex;

        // Палитра, сгруппированная по категории — источник двух списков кисти (Категория → Блок).
        private Shared.World.Blocks.BlockCategory[] _categories;
        private string[] _categoryNames;
        private int[][] _paletteByCategory;
        private string[][] _blockNamesByCategory;
        private int[] _blockCats;
        private string[] _blockCatNames;
        private int _categoryIndex;
        private int _blockIndex;

        private void OnEnable()
        {
            if (_mode > 2)
                _mode = 2;
            BlockDefinitionResolver.Invalidate(); // ассеты могли добавиться/переехать — кэш визуала заново
            var defs = BlockCatalogCodegen.LoadAllDefinitions();
            defs.Sort((a, b) => a.Type.CompareTo(b.Type));
            _palette = defs.ToArray();
            _paletteNames = new string[_palette.Length];
            for (int i = 0; i < _palette.Length; i++)
                _paletteNames[i] = $"{_palette[i].Type} — {_palette[i].DisplayName}";
            BuildCategoryGroups();
            LoadItemDefs();
        }

        private void LoadItemDefs() // палитра раздела «Предметы» — все ItemDefinition-ассеты, сорт по ItemDefId
        {
            var items = new List<Client.Items.ItemDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                var def = AssetDatabase.LoadAssetAtPath<Client.Items.ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null)
                    items.Add(def);
            }
            items.Sort((a, b) => a.ItemDefId.CompareTo(b.ItemDefId));
            _itemDefs = items.ToArray();
            _itemNames = new string[_itemDefs.Length];
            for (int i = 0; i < _itemDefs.Length; i++)
                _itemNames[i] = $"{_itemDefs[i].ItemDefId} — {_itemDefs[i].DisplayName}";
        }

        private string ItemName(ushort defId)
        {
            for (int i = 0; i < _itemDefs.Length; i++)
                if (_itemDefs[i].ItemDefId == defId)
                    return _itemDefs[i].DisplayName;
            return defId.ToString();
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

            var blockCats = new List<int>();
            for (int ci = 0; ci < _categories.Length; ci++)
                if (!IsMarkerCategory(_categories[ci]))
                    blockCats.Add(ci);
            _blockCats = blockCats.ToArray();
            _blockCatNames = new string[_blockCats.Length];
            for (int i = 0; i < _blockCats.Length; i++)
                _blockCatNames[i] = _categoryNames[_blockCats[i]];
        }

        private static bool IsMarkerCategory(Shared.World.Blocks.BlockCategory c)
            => c == Shared.World.Blocks.BlockCategory.Marker
            || c == Shared.World.Blocks.BlockCategory.Divider
            || c == Shared.World.Blocks.BlockCategory.MergeMarker
            || c == Shared.World.Blocks.BlockCategory.SpawnPoint;

        private int CategoryGroupIndex(Shared.World.Blocks.BlockCategory c)
        {
            for (int ci = 0; ci < _categories.Length; ci++)
                if (_categories[ci] == c)
                    return ci;
            return -1;
        }

        private static bool IsBitMarkerSelected() => _section == 2 && (_markerIndex == 3 || _markerIndex == 4);
        private static bool ModeAllowed(int mode) => _section == 0 || mode == 2 || (IsBitMarkerSelected() && mode == 0);
        private static bool RotationApplies() => _section == 0 || (_section == 2 && (_markerIndex == 2 || _markerIndex == 5));

        private int MarkerPaletteIndex() // индекс палитры для маркерного блока (-1 в бейк-режимах Потолки/Полы)
        {
            if (_markerIndex < 2)
                return -1;
            int ci = CategoryGroupIndex(MarkerBlockCats[_markerIndex - 2]);
            if (ci < 0)
                return -1;
            var inCat = _paletteByCategory[ci];
            return inCat[Mathf.Clamp(_markerBlockIndex, 0, inCat.Length - 1)];
        }

        public override void OnInspectorGUI()
        {
            var t = (BlockMapAuthoring)target;

            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < ModeNames.Length; i++)
                    using (new EditorGUI.DisabledScope(!ModeAllowed(i)))
                    {
                        bool on = GUILayout.Toggle(_mode == i, ModeNames[i], "Button");
                        if (on && _mode != i)
                            _mode = i;
                    }
            }
            EditorGUILayout.Space(4);

            int newSection = EditorGUILayout.Popup(
                new GUIContent("Раздел", "Что ставим: блоки, предметы или маркеры."), _section, SectionNames);
            if (newSection != _section)
            {
                _section = newSection;
                if (!ModeAllowed(_mode))
                    _mode = 2;
            }

            if (_section == 0)
                DrawBlockPicker();
            else if (_section == 1)
                DrawItemPicker();
            else
                DrawMarkerPicker();

            if (RotationApplies())
                EditorGUILayout.LabelField(new GUIContent("Поворот", "Крутит R в SceneView: Север/Восток/Юг/Запад."),
                    new GUIContent(FacingNames[_facing & 3]));

            EditorGUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
                t.RebuildBakeVisual(); // тумблеры показа бейк-слоёв

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Новая")) { t.NewMap(); t.RebuildBakeVisual(); _markerOverlayDirty = true; _shaftsDirty = true; if (_showZones) RecomputeZonesPreview(t); }
                using (new EditorGUI.DisabledScope(!System.IO.File.Exists(t.MapPath)))
                    if (GUILayout.Button("Загрузить")) TryIO(() => { t.LoadMap(); t.RebuildBakeVisual(); _markerOverlayDirty = true; _shaftsDirty = true; if (_showZones) RecomputeZonesPreview(t); });
                using (new EditorGUI.DisabledScope(!t.IsLoaded))
                    if (GUILayout.Button("Сохранить")) TryIO(() => { t.SaveMap(); AssetDatabase.Refresh(); });
            }

            using (new EditorGUI.DisabledScope(!t.IsLoaded))
                if (GUILayout.Button("Запечь потолки/полы"))
                    t.BakeLayers();

            using (new EditorGUI.DisabledScope(!t.IsLoaded))
                if (GUILayout.Button(new GUIContent("Маркеры → метки", "Старые блоки Divider/MergeMarker → бейк-биты (блок удаляется).")))
                {
                    ConvertMarkerBlocks(t);
                    _markerOverlayDirty = true;
                    if (_showZones) RecomputeZonesPreview(t);
                }

            EditorGUILayout.Space(6);
            _layer = EditorGUILayout.IntField(new GUIContent("Слой (Y)", "Высота кисти; [ и ] в SceneView."), _layer);

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

        private void DrawBlockPicker()
        {
            if (_palette.Length == 0)
            {
                EditorGUILayout.HelpBox("Нет BlockDefinition-ассетов — создай (Create → Station → Block Definition).", MessageType.Warning);
                return;
            }
            if (_blockCats.Length == 0)
            {
                EditorGUILayout.HelpBox("Нет блоков не-маркерных категорий.", MessageType.Warning);
                return;
            }

            _categoryIndex = Mathf.Clamp(_categoryIndex, 0, _blockCats.Length - 1);
            int newCat = EditorGUILayout.Popup(new GUIContent("Категория"), _categoryIndex, _blockCatNames);
            if (newCat != _categoryIndex) { _categoryIndex = newCat; _blockIndex = 0; }

            int ci = _blockCats[_categoryIndex];
            var inCat = _paletteByCategory[ci];
            _blockIndex = Mathf.Clamp(_blockIndex, 0, inCat.Length - 1);
            _blockIndex = EditorGUILayout.Popup(new GUIContent("Блок"), _blockIndex, _blockNamesByCategory[ci]);
            _paletteIndex = inCat[_blockIndex];

            if (_categories[ci] == Shared.World.Blocks.BlockCategory.FloorAnchor)
            {
                _seedName = EditorGUILayout.TextField(new GUIContent("Имя", "Имя станции/этажа (лейбл блока этажа)."), _seedName);
                _seedRank = EditorGUILayout.IntField(new GUIContent("Ранг", "Меньший = истина. Игрок >0, админ/мапер <1."), _seedRank);
                _seedFloor = EditorGUILayout.IntField(new GUIContent("Этаж", "Номер этажа зоны."), _seedFloor);
            }
        }

        private void DrawItemPicker()
        {
            if (_itemDefs.Length == 0)
            {
                EditorGUILayout.HelpBox("Нет ItemDefinition-ассетов.", MessageType.Warning);
                return;
            }
            _itemIndex = Mathf.Clamp(_itemIndex, 0, _itemDefs.Length - 1);
            _itemIndex = EditorGUILayout.Popup(new GUIContent("Предмет"), _itemIndex, _itemNames);
            _itemStack = Mathf.Clamp(
                EditorGUILayout.IntField(new GUIContent("Стак", "Количество в точке спавна (1–255)."), _itemStack), 1, 255);
        }

        private void DrawMarkerPicker()
        {
            int newMarker = EditorGUILayout.Popup(new GUIContent("Категория"), _markerIndex, MarkerNames);
            if (newMarker != _markerIndex) { _markerIndex = newMarker; _markerBlockIndex = 0; if (!ModeAllowed(_mode)) _mode = 2; }
            if (_markerIndex < 2 || _markerIndex == 3 || _markerIndex == 4)
                return;

            var cat = MarkerBlockCats[_markerIndex - 2];
            int ci = CategoryGroupIndex(cat);
            if (ci < 0)
            {
                EditorGUILayout.HelpBox($"Нет блоков категории {cat}.", MessageType.Warning);
                return;
            }
            var inCat = _paletteByCategory[ci];
            _markerBlockIndex = Mathf.Clamp(_markerBlockIndex, 0, inCat.Length - 1);
            _markerBlockIndex = EditorGUILayout.Popup(new GUIContent("Блок"), _markerBlockIndex, _blockNamesByCategory[ci]);
            _paletteIndex = inCat[Mathf.Clamp(_markerBlockIndex, 0, inCat.Length - 1)];
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
            if (t.IsLoaded)
                DrawItemSpawns(t);
            if (t.IsLoaded)
                DrawMarkerOverlay(t);
            if (!t.IsLoaded)
                return;

            DrawFailHighlight();
            DrawShaftRects(t);

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

            if (_section == 1)
                ItemMode(t, e, ray);
            else if (_section == 2)
                MarkerInsert(t, e, ray);
            else if (_palette.Length > 0 && _blockCats.Length > 0)
            {
                if (_mode == 0)
                    LayerMode(t, e, ray);
                else if (_mode == 1)
                    AttachMode(t, e, ray);
                else
                    BlockInsert(t, e, ray);
            }

            SceneView.RepaintAll();
        }

        // Маршрутизация раздела «Маркеры»: Потолки/Полы — бейк-биты (BakeMode), остальное — обычная вставка блока.
        private void MarkerInsert(BlockMapAuthoring t, Event e, Ray ray)
        {
            if (_markerIndex < 2)
            {
                BakeMode(t, e, ray, ceiling: _markerIndex == 0);
                return;
            }
            if (_markerIndex == 3 || _markerIndex == 4)
            {
                BitMarkerPaint(t, e, ray, merge: _markerIndex == 4);
                return;
            }
            int idx = MarkerPaletteIndex();
            if (idx < 0)
                return;
            _paletteIndex = idx;
            BlockInsert(t, e, ray);
        }

        private void BitMarkerPaint(BlockMapAuthoring t, Event e, Ray ray, bool merge)
        {
            Vector3Int cell;
            if (_mode == 0)
            {
                if (!PlaneCell(ray, out cell))
                    return;
                DrawGrid(cell.x, cell.z);
            }
            else if (RaycastGrid(t, ray, 200f, out var hit, out _))
            {
                cell = hit;
            }
            else
            {
                if (!PlaneCell(ray, out cell))
                    return;
                DrawGrid(cell.x, cell.z);
            }

            bool erase = e.shift;
            Handles.color = erase ? new Color(1f, 0.3f, 0.2f)
                                  : (merge ? new Color(0.25f, 1f, 0.35f) : new Color(1f, 0.3f, 0.3f));
            Handles.DrawWireCube(new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f), Vector3.one);

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
            {
                SetBitMarker(t, cell.x, cell.y, cell.z, merge, on: !erase);
                _markerOverlayDirty = true;
                e.Use();
            }
        }

        private bool PlaneCell(Ray ray, out Vector3Int cell)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, _layer, 0f));
            if (!plane.Raycast(ray, out float enter))
            {
                cell = default;
                return false;
            }
            Vector3 p = ray.GetPoint(enter);
            cell = new Vector3Int(Mathf.FloorToInt(p.x), _layer, Mathf.FloorToInt(p.z));
            return true;
        }

        private static void SetBitMarker(BlockMapAuthoring t, int x, int y, int z, bool merge, bool on)
        {
            if (t.Grid == null)
                return;
            byte bit = merge ? Shared.World.Blocks.ChunkSection.BakeMerge : Shared.World.Blocks.ChunkSection.BakeDivider;
            byte other = merge ? Shared.World.Blocks.ChunkSection.BakeDivider : Shared.World.Blocks.ChunkSection.BakeMerge;
            byte cur = t.Grid.GetBake(x, y, z);
            byte next = on ? (byte)((cur | bit) & ~other) : (byte)(cur & ~bit);
            t.Grid.SetBake(x, y, z, next);
        }

        private void DrawMarkerOverlay(BlockMapAuthoring t)
        {
            if (_markerOverlayDirty)
                BuildMarkerOverlay(t);
            Handles.color = new Color(1f, 0.25f, 0.25f, 0.9f);
            for (int i = 0; i < _dividerCells.Count; i++)
                Handles.DrawWireCube(_dividerCells[i], new Vector3(0.96f, 0.96f, 0.96f));
            Handles.color = new Color(0.25f, 1f, 0.35f, 0.9f);
            for (int i = 0; i < _mergeCells.Count; i++)
                Handles.DrawWireCube(_mergeCells[i], new Vector3(0.96f, 0.96f, 0.96f));
        }

        private void BuildMarkerOverlay(BlockMapAuthoring t)
        {
            _dividerCells.Clear();
            _mergeCells.Clear();
            _markerOverlayDirty = false;
            if (t.Grid == null)
                return;
            byte divider = Shared.World.Blocks.ChunkSection.BakeDivider;
            byte mergeBit = Shared.World.Blocks.ChunkSection.BakeMerge;
            int count = 0;
            foreach (var kv in t.Grid.Sections)
            {
                Shared.World.Blocks.BlockGrid.UnpackKey(kv.Key, out int cx, out int cy, out int cz);
                for (int ly = 0; ly < Shared.World.Blocks.ChunkSection.Size; ly++)
                    for (int lz = 0; lz < Shared.World.Blocks.ChunkSection.Size; lz++)
                        for (int lx = 0; lx < Shared.World.Blocks.ChunkSection.Size; lx++)
                        {
                            if (count >= MarkerOverlayCap)
                                return;
                            byte bake = kv.Value.GetBake(Shared.World.Blocks.ChunkSection.LocalIndex(lx, ly, lz));
                            if ((bake & divider) != 0)
                            {
                                _dividerCells.Add(new Vector3(cx * 16 + lx + 0.5f, cy * 16 + ly + 0.5f, cz * 16 + lz + 0.5f));
                                count++;
                            }
                            else if ((bake & mergeBit) != 0)
                            {
                                _mergeCells.Add(new Vector3(cx * 16 + lx + 0.5f, cy * 16 + ly + 0.5f, cz * 16 + lz + 0.5f));
                                count++;
                            }
                        }
            }
        }

        private static void ConvertMarkerBlocks(BlockMapAuthoring t)
        {
            if (t.Grid == null)
                return;
            var cells = new List<(int x, int y, int z, bool merge)>();
            foreach (var kv in t.Grid.Sections)
            {
                Shared.World.Blocks.BlockGrid.UnpackKey(kv.Key, out int cx, out int cy, out int cz);
                for (int ly = 0; ly < Shared.World.Blocks.ChunkSection.Size; ly++)
                    for (int lz = 0; lz < Shared.World.Blocks.ChunkSection.Size; lz++)
                        for (int lx = 0; lx < Shared.World.Blocks.ChunkSection.Size; lx++)
                        {
                            ushort type = kv.Value.GetBlock(Shared.World.Blocks.ChunkSection.LocalIndex(lx, ly, lz));
                            if (type == 0)
                                continue;
                            var cat = Shared.World.Blocks.BlockCatalog.Get(type).Category;
                            if (cat == Shared.World.Blocks.BlockCategory.Divider)
                                cells.Add((cx * 16 + lx, cy * 16 + ly, cz * 16 + lz, false));
                            else if (cat == Shared.World.Blocks.BlockCategory.MergeMarker)
                                cells.Add((cx * 16 + lx, cy * 16 + ly, cz * 16 + lz, true));
                        }
            }
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                SetBitMarker(t, c.x, c.y, c.z, c.merge, on: true);
                t.EraseObject(c.x, c.y, c.z);
            }
            Debug.Log($"[Markers] Маркеры→метки: сконвертировано {cells.Count}.");
        }

        // Режим «Вставка»: рейкаст в занятую ячейку под курсором, при промахе — фолбэк на плоскость текущего слоя.
        private void BlockInsert(BlockMapAuthoring t, Event e, Ray ray)
        {
            Vector3Int cell;
            if (RaycastGrid(t, ray, 200f, out var hit, out _))
                cell = hit;
            else
            {
                var plane = new Plane(Vector3.up, new Vector3(0f, _layer, 0f));
                if (!plane.Raycast(ray, out float enter))
                    return;
                Vector3 p = ray.GetPoint(enter);
                cell = new Vector3Int(Mathf.FloorToInt(p.x), _layer, Mathf.FloorToInt(p.z));
                DrawGrid(cell.x, cell.z);
            }

            bool erase = e.shift;
            if (erase)
                DrawEraseGhost(t, cell);
            else
                DrawGhost(t, cell.x, cell.y, cell.z, erase: false);

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                PlaceOrErase(t, cell.x, cell.y, cell.z, erase);
                e.Use();
            }
        }

        // Кисть предметов: клик по ячейке слоя — AddItemSpawn, Shift+клик — RemoveItemSpawnsAt (ластик всей ячейки).
        private void ItemMode(BlockMapAuthoring t, Event e, Ray ray)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, _layer, 0f));
            if (!plane.Raycast(ray, out float enter))
                return;
            Vector3 hit = ray.GetPoint(enter);
            int bx = Mathf.FloorToInt(hit.x);
            int bz = Mathf.FloorToInt(hit.z);

            DrawGrid(bx, bz);
            bool erase = e.shift;
            Handles.color = erase ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.75f, 0.2f);
            Handles.DrawWireCube(new Vector3(bx + 0.5f, _layer + 0.25f, bz + 0.5f), new Vector3(0.5f, 0.5f, 0.5f));

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (erase)
                    t.Grid.RemoveItemSpawnsAt(bx, _layer, bz);
                else if (_itemDefs.Length > 0)
                    t.Grid.AddItemSpawn(new Shared.World.Blocks.ItemSpawn(
                        bx, _layer, bz, _itemDefs[Mathf.Clamp(_itemIndex, 0, _itemDefs.Length - 1)].ItemDefId,
                        (byte)_itemStack));
                e.Use();
            }
        }

        // Точки спавна видны всегда при загруженной карте — оранжевые wire-кубы + подпись «Имя ×N», без спавна объектов.
        private void DrawItemSpawns(BlockMapAuthoring t)
        {
            var spawns = t.Grid.ItemSpawns;
            Handles.color = new Color(1f, 0.75f, 0.2f);
            for (int i = 0; i < spawns.Count; i++)
            {
                var s = spawns[i];
                var c = new Vector3(s.X + 0.5f, s.Y + 0.2f, s.Z + 0.5f);
                Handles.DrawWireCube(c, new Vector3(0.4f, 0.4f, 0.4f));
                string label = s.Stack > 1 ? $"{ItemName(s.DefId)} ×{s.Stack}" : ItemName(s.DefId);
                Handles.Label(c + Vector3.up * 0.4f, label);
            }
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
            _shaftsDirty = true;
            if (erase)
            {
                t.EraseObject(x, y, z);
                return;
            }
            var def = _palette[_paletteIndex];
            BlockPlaceResult r;
            if (def.Size.x * def.Size.y * def.Size.z > 1)
                r = t.PaintObject(x, y, z, def, _facing);
            else if (def.Category == Shared.World.Blocks.BlockCategory.FloorAnchor)
                r = t.PaintSeed(x, y, z, def.Type, _seedName, _seedRank, _seedFloor);
            else
                r = t.PaintBlock(x, y, z, def.Type, _facing);

            if (r.Ok)
            {
                _hasFail = false;
                return;
            }
            var cell = new Vector3Int(r.Cx, r.Cy, r.Cz);
            bool repeat = _hasFail && _failCell == cell;
            _hasFail = true;
            _failCell = cell;
            if (!repeat) // протяжка по той же помешавшей клетке не спамит консоль
                Debug.LogWarning($"[BlockMapAuthoring] «{def.DisplayName}» не поставлен в ({x},{y},{z}): {FailText(r)}");
        }

        private static string FailText(BlockPlaceResult r) => r.Reason switch
        {
            PlaceFail.CellOccupied =>
                $"клетка ({r.Cx},{r.Cy},{r.Cz}) занята — «{Shared.World.Blocks.BlockCatalog.Get(r.BlockingType).Name}»",
            PlaceFail.OutOfBounds => $"клетка ({r.Cx},{r.Cy},{r.Cz}) вне пределов карты по высоте",
            PlaceFail.OffsetOutOfRange => $"часть ({r.Cx},{r.Cy},{r.Cz}) вне диапазона смещения структуры",
            _ => "карта не загружена или тип блока не назначен"
        };

        // Каркас плановых прямоугольников шахт: виден, когда в палитре выбрана лифт-часть.
        // Скан карты кэшируется (_shaftsDirty) — OnSceneGUI покадровый.
        private void DrawShaftRects(BlockMapAuthoring t)
        {
            if (_palette == null || _paletteIndex < 0 || _paletteIndex >= _palette.Length)
                return;
            var def = _palette[_paletteIndex];
            if (def == null || !def.IsLiftPart)
                return;
            if (_shaftsDirty)
            {
                t.CollectShafts(_shafts);
                _shaftsDirty = false;
            }
            Handles.color = ShaftWire;
            for (int i = 0; i < _shafts.Count; i++)
            {
                var s = _shafts[i];
                float h = Mathf.Max(1, s.ModuleY);
                Handles.DrawWireCube(
                    new Vector3((s.X0 + s.X1 + 1) * 0.5f, s.RailY + h * 0.5f, (s.Z0 + s.Z1 + 1) * 0.5f),
                    new Vector3(s.X1 - s.X0 + 1, h, s.Z1 - s.Z0 + 1));
            }
        }

        // Клетка последнего отказа: подсвечивается, пока не поставят удачно.
        private void DrawFailHighlight()
        {
            if (!_hasFail)
                return;
            Handles.color = new Color(1f, 0.15f, 0.1f);
            Handles.DrawWireCube(new Vector3(_failCell.x + 0.5f, _failCell.y + 0.5f, _failCell.z + 0.5f), Vector3.one * 1.02f);
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

            if (!t.Grid.AnchorOf(cell.x, cell.y, cell.z, out int ax, out int ay, out int az))
            {
                Handles.DrawWireCube(new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f), Vector3.one);
                return;
            }
            int facing = Shared.World.Blocks.BlockState.GetFacing(t.Grid.GetState(ax, ay, az));
            int parts = Shared.World.Blocks.MultiBlock.PartCount(def.Size.x, def.Size.y, def.Size.z);
            for (int p = 0; p < parts; p++)
            {
                Shared.World.Blocks.MultiBlock.PartWorldOffset(p, def.Size.x, def.Size.z, facing,
                    out int dx, out int dy, out int dz);
                Handles.DrawWireCube(new Vector3(ax + dx + 0.5f, ay + dy + 0.5f, az + dz + 0.5f), Vector3.one);
            }
        }

        // Призрак: одиночный куб либо футпринт мульти-блока (с учётом поворота R).
        private static readonly Color GhostFree = new Color(0.2f, 1f, 0.4f);
        private static readonly Color GhostBusy = new Color(1f, 0.25f, 0.15f);
        private static readonly Color GhostErase = new Color(1f, 0.3f, 0.2f);

        private void DrawGhost(BlockMapAuthoring t, int x, int y, int z, bool erase)
        {
            var def = _palette[_paletteIndex];
            int parts = erase ? 1 : def.Size.x * def.Size.y * def.Size.z;
            if (parts <= 1)
            {
                var c = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                Handles.color = erase ? GhostErase : (t.BlocksSinglePaint(x, y, z) ? GhostBusy : GhostFree);
                Handles.DrawWireCube(c, Vector3.one);
                if (!erase && !def.RequiresSupport)
                    Handles.DrawLine(c, c + FacingDir(_facing) * 0.5f);
                return;
            }
            for (int p = 0; p < parts; p++)
            {
                Shared.World.Blocks.MultiBlock.PartWorldOffset(p, def.Size.x, def.Size.z, _facing,
                    out int dx, out int dy, out int dz);
                Handles.color = t.BlocksObjectPart(x + dx, y + dy, z + dz, dx, dy, dz) ? GhostBusy : GhostFree;
                Handles.DrawWireCube(new Vector3(x + dx + 0.5f, y + dy + 0.5f, z + dz + 0.5f), Vector3.one);
            }
        }

        private static Vector3 FacingDir(int facing) => (facing & 3) switch
        {
            0 => Vector3.forward,
            1 => Vector3.right,
            2 => Vector3.back,
            _ => Vector3.left
        };

        // Режим «Присоединение»: рейкаст в существующий блок, ставим к его грани; Shift — стереть сам объект.
        private void AttachMode(BlockMapAuthoring t, Event e, Ray ray)
        {
            if (!RaycastGrid(t, ray, 200f, out var hit, out var prev))
                return;

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

        // Бейк потолков/полов: зажатая ЛКМ рисует бит, Shift+ЛКМ — стирает.
        private void BakeMode(BlockMapAuthoring t, Event e, Ray ray, bool ceiling)
        {
            if (!RaycastGrid(t, ray, 200f, out var hit, out _))
                return;

            byte bit = ceiling ? Shared.World.Blocks.ChunkSection.BakeCeiling
                               : Shared.World.Blocks.ChunkSection.BakeInteriorFloor;
            bool erase = e.shift;
            Handles.color = erase ? new Color(1f, 0.3f, 0.2f)
                                  : (ceiling ? new Color(1f, 0.55f, 0.1f) : new Color(0.2f, 1f, 0.35f));
            Handles.DrawWireCube(new Vector3(hit.x + 0.5f, hit.y + 0.5f, hit.z + 0.5f), Vector3.one);

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
            {
                t.SetBakeBit(hit.x, hit.y, hit.z, bit, on: !erase);
                e.Use();
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
