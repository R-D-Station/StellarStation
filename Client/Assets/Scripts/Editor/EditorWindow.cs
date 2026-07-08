#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Shared.World;
using Client.Map;

namespace Client.Editor.MapTools
{
    /// <summary>
    /// Окно-редактор тайловой карты (Tools → Station → Map Editor): правит один Z-слой
    /// через Shared.GridMap. UI разложен на зоны: Файл · Слой и вид · Кисть · Сетка.
    /// </summary>
    public class MapEditorWindow : EditorWindow
    {
        // Пресеты палитры (fallback без каталога).
        private enum Brush { Floor, Wall, Grate, Space }

        // ---- Данные карты ----
        private GridMap _map;
        private string _currentPath;     // путь последнего save/load
        private bool _dirty;             // есть несохранённые правки

        // ---- Слой / вид ----
        private int _activeZ;
        private const int CellSize = 24;
        private const int MaxViewTiles = 128;   // потолок размера вида (в т.ч. авто-фита «По карте» — перф)
        private const int PanStep = 1;          // шаг сдвига вида по WASD (тайлы за нажатие)
        private int _viewTilesX = 32;
        private int _viewTilesY = 32;
        private int _originX;
        private int _originY;
        private Vector2 _scroll;

        // Превью autotiling-соединений стен и пола (W4b/F-editor): оверлей поверх клетки, данные не трогает.
        private bool _showConnections;

        // ---- Каталог тайлов ----
        private TileCatalog _catalog;
        private const string CatalogPrefKey = "Station.MapEditor.CatalogGuid";

        // ---- Кисть ----
        // Настенный объект (стена/дверь/люк/окно) — один слот StructureType.
        private byte _selFloor = 1;
        private byte _selStructure = 0;
        private TileSpecial _selSpecial = TileSpecial.None;
        private Brush _brush = Brush.Floor;   // только когда каталога нет

        // Категория-таргет мазка (режим каталога): какие слои клетки пишем; невыбранные — сохраняем (read-modify-write).
        private bool _paintFloor = true;
        private bool _paintStructure;

        // Потолок: при включённом флаге краска кладёт пол на z+1 над клеткой (закрытая комната).
        private bool _withCeiling;
        private byte _ceilingFloor;          // 0 = «как пол кисти», иначе явный вид пола

        // ---- Эксперт: ручные флаги тайла ----
        private bool _showExpert;
        private bool _advanced;          // true → красить ручными флагами, игнорируя палитру
        private byte _advFloorType = 1;
        private byte _advStructureType;
        private bool _advOpenable;
        private bool _advSupport = true;
        private bool _advHBlock;
        private bool _advVBlock = true;
        private bool _advSealH;
        private bool _advSealV = true;

        // ---- Рантайм UI ----
        private bool _painting;
        private bool _hasHover;
        private int _hoverX, _hoverY;
        private GUIStyle _markerStyle;
        private GUIStyle _shapeStyle;
        private static readonly Color WallSel = Color.cyan;
        private static readonly Color DoorSel = new Color(0.35f, 0.65f, 0.95f);
        private static readonly Color ConnStub = new Color(1f, 0.82f, 0.20f, 0.95f);
        private static readonly Color FloorStub = new Color(0.25f, 0.85f, 0.70f, 0.90f);
        private static readonly GUIContent CeilingToggle = new GUIContent("Потолок (пол z+1)",
            "Дополнительно кладёт пол выбранного типа на этаж z+1 над каждой закрашенной клеткой.");
        private static readonly GUIContent ManualFlagsToggle = new GUIContent("Ручные флаги",
            "Игнорировать палитру пола/структуры, выставлять флаги тайла напрямую.");
        private static readonly GUIContent PaintFloorToggle = new GUIContent("Пол",
            "Красить слой пола. Выкл — существующий пол клетки сохраняется. «Нет» в ряду пола очищает только пол.");
        private static readonly GUIContent PaintStructureToggle = new GUIContent("Объект",
            "Красить слой объекта (стена/дверь/люк/окно). Выкл — существующий объект сохраняется. «Нет» очищает только объект.");

        [MenuItem("Tools/Station/Map Editor")]
        public static void Open()
        {
            var w = GetWindow<MapEditorWindow>("Map Editor");
            w.minSize = new Vector2(560, 520);
        }

        private void OnEnable()
        {
            if (_map == null) _map = new GridMap();
            wantsMouseMove = true;            // чтобы инфо клетки под курсором обновлялось живо
            LoadCatalogFromPrefs();
        }

        private void OnGUI()
        {
            HandlePanKeys();   // WASD-сдвиг вида (до отрисовки, чтобы полей не касалось)

            DrawFileToolbar();
            DrawLayerAndView();
            DrawBrushSection();
            DrawGrid();

            if (Event.current.type == EventType.MouseMove) Repaint();
        }

        // ===== Зона 1: Файл ==================================================

        private void DrawFileToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(46))) NewMap();
                if (GUILayout.Button("Load", EditorStyles.toolbarButton, GUILayout.Width(46))) Load();
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(46))) Save(saveAs: false);
                if (GUILayout.Button("Save As", EditorStyles.toolbarButton, GUILayout.Width(60))) Save(saveAs: true);

                GUILayout.Space(8);
                string name = string.IsNullOrEmpty(_currentPath)
                    ? "<без файла>"
                    : System.IO.Path.GetFileName(_currentPath);
                GUILayout.Label((_dirty ? "● " : "") + name, EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();
                GUILayout.Label(_dirty ? "unsaved" : "saved", EditorStyles.miniLabel, GUILayout.Width(56));
            }
        }

        // ===== Зона 2: Слой и вид ===========================================

        private void DrawLayerAndView()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Этаж, который правим — отдельная строка, не теряется среди параметров вида.
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Этаж Z", GUILayout.Width(56));
                    if (GUILayout.Button("−", GUILayout.Width(24))) _activeZ--;
                    _activeZ = EditorGUILayout.IntField(_activeZ, GUILayout.Width(52));
                    if (GUILayout.Button("+", GUILayout.Width(24))) _activeZ++;
                    GUILayout.FlexibleSpace();
                }

                // Окно просмотра — отдельная строка.
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Вид", GUILayout.Width(56));
                    EditorGUILayout.LabelField("origin", GUILayout.Width(40));
                    _originX = EditorGUILayout.IntField(_originX, GUILayout.Width(46));
                    _originY = EditorGUILayout.IntField(_originY, GUILayout.Width(46));
                    GUILayout.Space(10);
                    EditorGUILayout.LabelField("size", GUILayout.Width(30));
                    _viewTilesX = Mathf.Clamp(EditorGUILayout.IntField(_viewTilesX, GUILayout.Width(44)), 1, MaxViewTiles);
                    _viewTilesY = Mathf.Clamp(EditorGUILayout.IntField(_viewTilesY, GUILayout.Width(44)), 1, MaxViewTiles);
                    GUILayout.Space(10);
                    if (GUILayout.Button("к 0,0", GUILayout.Width(50))) { _originX = 0; _originY = 0; }
                    if (GUILayout.Button("По карте", GUILayout.Width(64))) FitViewToMap();
                    GUILayout.FlexibleSpace();
                }

                // Превью формы autotiling поверх грида — только вид, краску/данные не меняет.
                _showConnections = EditorGUILayout.ToggleLeft(
                    "Показывать соединения (стены/пол)", _showConnections);
            }
        }

        // Кадрирует вид по фактическому содержимому карты (все Z): origin = мин. занятый тайл, size = размах, клампится
        // до MaxViewTiles (большие карты досматриваются WASD). Пустая карта — вид не трогаем. Зовётся с «По карте» и после Load.
        private void FitViewToMap()
        {
            if (_map == null) return;
            bool any = false;
            int minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (var ch in _map.Chunks)
            {
                for (int ly = 0; ly < Chunk.Size; ly++)
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    var t = ch[lx, ly];
                    if (t.FloorType == 0 && t.StructureType == 0 && t.Special == TileSpecial.None) continue;
                    int wx = ch.ChunkX * Chunk.Size + lx;
                    int wy = ch.ChunkY * Chunk.Size + ly;
                    if (!any) { minX = maxX = wx; minY = maxY = wy; any = true; }
                    else
                    {
                        if (wx < minX) minX = wx; else if (wx > maxX) maxX = wx;
                        if (wy < minY) minY = wy; else if (wy > maxY) maxY = wy;
                    }
                }
            }
            if (!any) return;
            _originX = minX;
            _originY = minY;
            _viewTilesX = Mathf.Clamp(maxX - minX + 1, 1, MaxViewTiles);
            _viewTilesY = Mathf.Clamp(maxY - minY + 1, 1, MaxViewTiles);
        }

        // WASD-сдвиг вида (origin). Грид рисуется севером вверх: W=север(+Y), S=юг, A=запад(−X), D=восток. Не перехватываем,
        // пока правится текстовое поле (ввод чисел origin/size/Z). Ключ гасим (e.Use), чтобы не ушёл в контролы.
        private void HandlePanKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            int dx = 0, dy = 0;
            switch (e.keyCode)
            {
                case KeyCode.W: dy = PanStep; break;
                case KeyCode.S: dy = -PanStep; break;
                case KeyCode.A: dx = -PanStep; break;
                case KeyCode.D: dx = PanStep; break;
                default: return;
            }
            _originX += dx;
            _originY += dy;
            e.Use();
            Repaint();
        }

        // ===== Зона 3: Кисть =================================================

        private void DrawBrushSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Кисть", EditorStyles.boldLabel);

                // Каталог — источник палитры. Лежит здесь, т.к. определяет, чем красим.
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Каталог", GUILayout.Width(56));
                    EditorGUI.BeginChangeCheck();
                    _catalog = (TileCatalog)EditorGUILayout.ObjectField(_catalog, typeof(TileCatalog), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SaveCatalogToPrefs();
                        _catalog?.InvalidateCache();
                    }
                }
                if (_catalog == null)
                    EditorGUILayout.LabelField("нет каталога — пресеты + цвета", EditorStyles.miniLabel);

                EditorGUILayout.Space(2);

                if (_catalog != null)
                {
                    DrawPaintTargetRow();
                    if (_paintFloor) DrawFloorRow();
                    if (_paintStructure) DrawStructureRow();
                }
                else
                {
                    DrawPresetRow();
                }

                DrawSpecialRow();
                DrawCeilingRow();

                EditorGUILayout.Space(2);
                DrawBrushPreview();

                EditorGUILayout.Space(2);
                DrawExpert();
            }
        }

        // Категория-таргет: какие слои несёт мазок. Невыбранный слой read-modify-write сохраняет из клетки.
        private void DrawPaintTargetRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Красить", GUILayout.Width(56));
                _paintFloor = GUILayout.Toggle(_paintFloor, PaintFloorToggle, EditorStyles.miniButton, GUILayout.Height(20));
                _paintStructure = GUILayout.Toggle(_paintStructure, PaintStructureToggle, EditorStyles.miniButton, GUILayout.Height(20));
                GUILayout.FlexibleSpace();
            }
            if (!_paintFloor && !_paintStructure)
                EditorGUILayout.LabelField("нет цели: мазок сохранит пол и объект (можно ставить только спец)",
                    EditorStyles.miniLabel);
        }

        // Пол: «Нет» + виды из каталога.
        private void DrawFloorRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Пол", GUILayout.Width(56));
                DrawSelectButton("Нет", _selFloor == 0, () => _selFloor = 0, null);
                foreach (var f in _catalog.Floors)
                {
                    if (f == null || f.Type == 0) continue;
                    byte id = f.Type;
                    DrawSelectButton(KindLabel(f.DisplayName, id), _selFloor == id, () => _selFloor = id, null);
                }
            }
        }

        // Настенный объект: стена/дверь/люк/окно — один слот. Открываемые подкрашены иначе.
        private void DrawStructureRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Объект", GUILayout.Width(56));
                DrawSelectButton("Нет", _selStructure == 0, () => _selStructure = 0, null);
                foreach (var s in _catalog.Structures)
                {
                    if (s == null || s.Type == 0) continue;
                    byte id = s.Type;
                    DrawSelectButton(KindLabel(s.DisplayName, id), _selStructure == id,
                        () => _selStructure = id, s.Openable ? DoorSel : WallSel);
                }
            }
        }

        // Fallback без каталога: хардкод-кисти.
        private void DrawPresetRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Кисть", GUILayout.Width(56));
                DrawBrushButton(Brush.Floor, "Пол");
                DrawBrushButton(Brush.Wall, "Стена");
                DrawBrushButton(Brush.Grate, "Решётка");
                DrawBrushButton(Brush.Space, "Космос");
            }
        }

        // Спец-маркер тайла (поверх пола/структуры).
        private void DrawSpecialRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Спец", GUILayout.Width(56));
                DrawSelectButton("Нет", _selSpecial == TileSpecial.None, () => _selSpecial = TileSpecial.None, null);
                DrawSelectButton("Спавн", _selSpecial == TileSpecial.Spawn, () => _selSpecial = TileSpecial.Spawn, null);
                DrawSelectButton("Лестница ▲", _selSpecial == TileSpecial.StairUp, () => _selSpecial = TileSpecial.StairUp, null);
                DrawSelectButton("Лестница ▼", _selSpecial == TileSpecial.StairDown, () => _selSpecial = TileSpecial.StairDown, null);
            }
            if (_selSpecial == TileSpecial.StairUp || _selSpecial == TileSpecial.StairDown)
                EditorGUILayout.HelpBox(
                    "Лестница: парная авто-ставится на соседнем этаже (та же клетка). Выбери ещё и Пол.",
                    MessageType.None);
        }

        // Потолок: краска дополнительно кладёт пол на z+1. «как кисть» — берёт пол текущей кисти,
        // иначе явный вид. Без каталога потолок всегда = пол кисти.
        private void DrawCeilingRow()
        {
            _withCeiling = EditorGUILayout.ToggleLeft(CeilingToggle, _withCeiling);
            if (!_withCeiling) return;

            using (new EditorGUI.IndentLevelScope())
            {
                if (_catalog != null)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Потолок", GUILayout.Width(56));
                        DrawSelectButton("как кисть", _ceilingFloor == 0, () => _ceilingFloor = 0, null);
                        foreach (var f in _catalog.Floors)
                        {
                            if (f == null || f.Type == 0) continue;
                            byte id = f.Type;
                            DrawSelectButton(KindLabel(f.DisplayName, id), _ceilingFloor == id, () => _ceilingFloor = id, null);
                        }
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Потолок = пол кисти (нет каталога).", EditorStyles.miniLabel);
                }
                EditorGUILayout.LabelField("ПКМ-стирание также убирает потолок над клеткой.", EditorStyles.miniLabel);
            }
        }

        // Превью собранной кисти (на пустой клетке) + текстовая сводка. Второй строкой — что мазок пишет / что сохраняет.
        private void DrawBrushPreview()
        {
            Tile empty = Tile.Space;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Превью", GUILayout.Width(56));
                Rect box = GUILayoutUtility.GetRect(CellSize + 8, CellSize + 8,
                    GUILayout.Width(CellSize + 8), GUILayout.Height(CellSize + 8));
                var cell = new Rect(box.x + 4, box.y + 4, CellSize, CellSize);
                Tile t = MakeTile(in empty);
                DrawCell(cell, in t, 0, 0, hasWorld: false);   // превью на пустой клетке → форма Single
                DrawBorder(cell, new Color(0f, 0f, 0f, 0.45f), 1);

                GUILayout.Space(8);
                EditorGUILayout.LabelField(TileSummary(in t), EditorStyles.miniLabel);
            }
            if (!_advanced && _catalog != null)
                EditorGUILayout.LabelField(PaintTargetSummary(), EditorStyles.miniLabel);
        }

        // Сводка per-category мазка: какой слой пишем/очищаем и какой сохраняем (режим каталога).
        private string PaintTargetSummary()
        {
            string floor = _paintFloor ? (_selFloor == 0 ? "пол → очистить" : $"пол → {_selFloor}") : "пол: сохранить";
            string obj = _paintStructure ? (_selStructure == 0 ? "объект → очистить" : $"объект → {_selStructure}") : "объект: сохранить";
            string spec = _selSpecial != TileSpecial.None ? $"спец → {_selSpecial}" : "спец: сохранить";
            return $"{floor}  ·  {obj}  ·  {spec}";
        }

        // Эксперт: ручные флаги под свёрнутой секцией (раньше — открытая панель Advanced).
        private void DrawExpert()
        {
            _showExpert = EditorGUILayout.Foldout(_showExpert, "Эксперт: ручные флаги", true);
            if (!_showExpert) return;

            using (new EditorGUI.IndentLevelScope())
            {
                _advanced = EditorGUILayout.ToggleLeft(ManualFlagsToggle, _advanced);

                using (new EditorGUI.DisabledScope(!_advanced))
                {
                    _advFloorType = (byte)EditorGUILayout.IntSlider("Floor Type", _advFloorType, 0, 255);
                    _advStructureType = (byte)EditorGUILayout.IntSlider("Structure Type", _advStructureType, 0, 255);
                    _advOpenable = EditorGUILayout.Toggle("Openable (door/hatch)", _advOpenable);
                    _advSupport = EditorGUILayout.Toggle("Support (stand)", _advSupport);
                    _advHBlock = EditorGUILayout.Toggle("Blocks Horizontal Sight", _advHBlock);
                    _advVBlock = EditorGUILayout.Toggle("Blocks Vertical Sight", _advVBlock);
                    _advSealH = EditorGUILayout.Toggle("Seals Horizontal (gas)", _advSealH);
                    _advSealV = EditorGUILayout.Toggle("Seals Vertical (gas)", _advSealV);
                }

                EditorGUILayout.HelpBox(
                    "Ручной режим красит ровно эти поля. Пол/структура из палитры не накладываются; " +
                    "Special — накладывается.",
                    MessageType.None);
            }
        }

        private void DrawSelectButton(string label, bool on, System.Action onClick, Color? onColor)
        {
            var prev = GUI.backgroundColor;
            if (on) GUI.backgroundColor = onColor ?? Color.cyan;
            if (GUILayout.Button(label, GUILayout.Height(22)))
                onClick();
            GUI.backgroundColor = prev;
        }

        private void DrawBrushButton(Brush b, string label)
        {
            bool on = _brush == b && !_advanced;
            var prev = GUI.backgroundColor;
            if (on) GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button(label, GUILayout.Height(22)))
            {
                _brush = b;
                _advanced = false;
            }
            GUI.backgroundColor = prev;
        }

        private static string KindLabel(string displayName, byte id)
            => string.IsNullOrEmpty(displayName) ? id.ToString() : displayName;

        private static string TileSummary(in Tile t)
        {
            string spec = t.Special == TileSpecial.None ? "—" : t.Special.ToString();
            string pass = t.Walkable ? "проходим" : "стоп";
            string obj = t.StructureType == 0 ? "—" : (t.Openable ? $"{t.StructureType}(откр)" : t.StructureType.ToString());
            return $"пол {t.FloorType} · объект {obj} · спец {spec} · {pass}";
        }

        // ===== Зона 4: Сетка =================================================

        private void DrawGrid()
        {
            // Статус-строка: координаты и состав клетки под курсором.
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (_hasHover)
                {
                    Tile h = _map.GetTile(_hoverX, _hoverY, _activeZ);
                    EditorGUILayout.LabelField(
                        $"Курсор ({_hoverX}, {_hoverY}) z{_activeZ}   ·   {TileSummary(in h)}",
                        EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "ЛКМ — красить · ПКМ — стереть · WASD — двигать вид · наведи на клетку для инфо",
                        EditorStyles.miniLabel);
                }
            }

            float w = _viewTilesX * CellSize;
            float h2 = _viewTilesY * CellSize;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            Rect area = GUILayoutUtility.GetRect(w, h2, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            Event e = Event.current;

            _hasHover = false;
            for (int ty = 0; ty < _viewTilesY; ty++)
            {
                for (int tx = 0; tx < _viewTilesX; tx++)
                {
                    int worldX = _originX + tx;
                    int worldY = _originY + ty;

                    // Экран: Y вниз, тайловый Y — вверх. Инвертируем, чтобы север был сверху.
                    float px = area.x + tx * CellSize;
                    float py = area.y + (_viewTilesY - 1 - ty) * CellSize;
                    var cell = new Rect(px, py, CellSize, CellSize);

                    Tile t = _map.GetTile(worldX, worldY, _activeZ);
                    DrawCell(cell, in t, worldX, worldY, hasWorld: true);
                    if (_showConnections) DrawConnectionStubs(cell, worldX, worldY, in t);

                    if (cell.Contains(e.mousePosition))
                    {
                        _hasHover = true;
                        _hoverX = worldX;
                        _hoverY = worldY;
                    }

                    HandleCellInput(e, cell, worldX, worldY);
                }
            }

            DrawGridLines(area);

            EditorGUILayout.EndScrollView();

            if (e.type == EventType.MouseUp)
                _painting = false;
        }

        private void HandleCellInput(Event e, Rect cell, int worldX, int worldY)
        {
            if (!cell.Contains(e.mousePosition)) return;

            bool down = e.type == EventType.MouseDown;
            bool drag = e.type == EventType.MouseDrag;

            if (down) _painting = true;
            if (!_painting || (!down && !drag)) return;

            // ЛКМ (0) — рисуем выбранным; ПКМ (1) — стираем в космос.
            if (e.button == 0)
            {
                Tile existing = _map.GetTile(worldX, worldY, _activeZ);
                Tile painted = MakeTile(in existing);
                _map.SetTile(worldX, worldY, _activeZ, painted);
                // Авто-пара — по интенту кисти (_selSpecial), не по сохранённому спецу клетки.
                AutoPairStair(worldX, worldY, _activeZ, _selSpecial);
                if (_withCeiling) PaintCeiling(worldX, worldY, _activeZ, in painted);
                MarkDirty();
                e.Use();
            }
            else if (e.button == 1)
            {
                _map.SetTile(worldX, worldY, _activeZ, Tile.Space);
                if (_withCeiling) _map.SetTile(worldX, worldY, _activeZ + 1, Tile.Space);
                MarkDirty();
                e.Use();
            }
        }

        // Собирает тайл для записи в клетку. Режим каталога — per-category read-modify-write: пишет только
        // слои с включённым тумблером «Красить», остальные слои и Special берёт из existing (не обнуляет).
        // Ручные флаги/пресеты (без каталога) — прежний whole-tile мазок.
        private Tile MakeTile(in Tile existing)
        {
            if (_advanced)
            {
                var adv = new Tile
                {
                    FloorType = _advFloorType,
                    StructureType = _advStructureType,
                    Openable = _advOpenable,
                    Support = _advSupport,
                    BlocksHorizontalSight = _advHBlock,
                    BlocksVerticalSight = _advVBlock,
                    SealsHorizontal = _advSealH,
                    SealsVertical = _advSealV
                };
                adv.Special = _selSpecial;   // whole-tile: явный выбор, «Нет» очищает
                return adv;
            }

            if (_catalog != null)
            {
                // Слой берём из кисти только при включённом тумблере; иначе сохраняем из клетки. Compose
                // (источник флагов) пересобирает флаги из пары id — сохранённый слой участвует как есть.
                byte floorId = _paintFloor ? _selFloor : existing.FloorType;
                byte structureId = _paintStructure ? _selStructure : existing.StructureType;
                var t = _catalog.Compose(floorId, structureId);
                // per-category: спец пишем только при явном выборе; «Нет» сохраняет существующий (не обнуляем).
                t.Special = _selSpecial != TileSpecial.None ? _selSpecial : existing.Special;
                return t;
            }

            Tile p;
            switch (_brush)
            {
                case Brush.Floor:
                    p = new Tile { FloorType = 1, Support = true, BlocksVerticalSight = true, SealsVertical = true };
                    break;
                case Brush.Wall:
                    p = new Tile { FloorType = 1, StructureType = 1, Support = true, BlocksHorizontalSight = true, BlocksVerticalSight = true, SealsHorizontal = true, SealsVertical = true };
                    break;
                case Brush.Grate:
                    p = new Tile { FloorType = 2, Support = true };
                    break;
                default:
                    p = Tile.Space;
                    break;
            }
            p.Special = _selSpecial;   // whole-tile: явный выбор, «Нет» очищает
            return p;
        }

        // Авто-пара лестниц: StairUp на z ⇒ StairDown на z+1 (та же клетка), и наоборот.
        private void AutoPairStair(int x, int y, int z, TileSpecial special)
        {
            if (special == TileSpecial.StairUp)
                PaintPairedStair(x, y, z + 1, TileSpecial.StairDown);
            else if (special == TileSpecial.StairDown)
                PaintPairedStair(x, y, z - 1, TileSpecial.StairUp);
        }

        // Парная лестница на соседнем этаже. Для ladder-кисти пару НЕ пишем: верх и низ лестницы — ДВЕ разные SO
        // (низ сплошной BlocksVerticalSight=true, верх see-through =false); авто-пара не знает парную SO → положила бы
        // не тот конец и сломала reveal. Человек ставит оба конца вручную. Не-ladder (floor-based) лестницы — как раньше.
        private void PaintPairedStair(int x, int y, int z, TileSpecial special)
        {
            // Кисть несёт ladder-структуру? (гейт по _paintStructure — как в MakeTile). Тогда пару не авто-парим.
            bool ladderBrush = _paintStructure && _catalog != null && _selStructure != 0
                && _catalog.GetStructure(_selStructure)?.Category == StructureCategory.Ladder;
            if (ladderBrush) return;

            byte floor = _selFloor != 0 ? _selFloor : (byte)1;
            Tile t = _catalog != null
                ? _catalog.Compose(floor, 0)
                : new Tile { FloorType = floor, Support = true, BlocksVerticalSight = true, SealsVertical = true };
            t.Special = special;
            _map.SetTile(x, y, z, t);
        }

        // Потолок этажа z — это пол этажа z+1. Кладём выбранный (или «как кисть») пол сверху,
        // чтобы комната была закрыта. Лестницам потолок не ставим — им нужен вертикальный проём.
        private void PaintCeiling(int x, int y, int z, in Tile painted)
        {
            if (painted.Special == TileSpecial.StairUp || painted.Special == TileSpecial.StairDown)
                return;

            byte cf = _ceilingFloor != 0 ? _ceilingFloor : painted.FloorType;
            if (cf == 0) return;   // в кисти нет пола и явный потолок не выбран — класть нечего

            Tile ceiling = _catalog != null
                ? _catalog.Compose(cf, 0)
                : new Tile { FloorType = cf, Support = true, BlocksVerticalSight = true, SealsVertical = true };
            _map.SetTile(x, y, z + 1, ceiling);
        }

        // ---- Рисование клетки ----------------------------------------------

        // worldX/worldY/hasWorld — контекст формы верха: грид даёт реальные соседей; brush-preview → hasWorld=false → Single.
        private void DrawCell(Rect r, in Tile t, int worldX, int worldY, bool hasWorld)
        {
            EditorGUI.DrawRect(r, CellColor(in t));

            bool structDrawn = false;
            if (_catalog != null)
            {
                if (t.FloorType != 0)
                {
                    var f = _catalog.GetFloor(t.FloorType);
                    if (!DrawFloorTop(r, f, worldX, worldY, hasWorld))
                        DrawSprite(r, f?.Sprite);
                }
                if (t.StructureType != 0)
                {
                    var s = _catalog.GetStructure(t.StructureType);
                    bool openNow = s != null && s.Openable && t.Open;
                    structDrawn = DrawStructureTop(r, s, openNow, worldX, worldY, hasWorld)
                        || DrawSprite(r, openNow ? s.OpenSprite : s?.Sprite);
                }
            }

            if (t.StructureType != 0 && !structDrawn)
                DrawBorder(r, t.Openable ? new Color(0.20f, 0.50f, 0.70f) : new Color(0.12f, 0.12f, 0.14f), 3);

            if (t.Special != TileSpecial.None)
                DrawSpecialMarker(r, t.Special);
        }

        // ---- Верх тайла грид-атласом TopMap (полная композиция рантайм-шейдера TileReader) ----
        // Атлас TopMap материала TileTop: 5 колонок форм × 2 ряда (верх — основной слой по _cur, низ — углы по _cur_corner).
        private const int TopAtlasCountX = 5;   // _count_x — сверено с TileTop.mat
        private const int TopAtlasCountY = 2;   // _count_y
        private static readonly Rect FullTexUV = new Rect(0f, 0f, 1f, 1f);

        // ⚠ ВИЗУАЛЬНЫЕ КНОБЫ поворота верха (зеркало саги WallCornerCode — правятся ПО КАРТИНКЕ, человек):
        // RotateSign — общий экранный знак: мешевый поворот North→East (вверх→вправо) = по часовой = +1 в IMGUI (Y вниз).
        //   Весь верх (форма+углы) повёрнут зеркально (CW↔CCW) → поставь -1.
        private const int RotateSign = 1;

        // ОСНОВНОЙ слой: per-shape база от UV-развёртки меша формы (в 2D-превью меша нет → добавляем сами). Итог осн. = steps + это.
        // Индекс = (int)WallShape: Single=0,End=1,Straight=2,Corner=3,T=4,Cross=5. Single/Straight/Cross 0; End/Corner +180°(2),
        // T −90°(3). Косит ФОРМА (носик End/T/Corner) → правь тут (четверти 0..3).
        private static readonly int[] ShapeBaseQuarter = { 0, 2, 0, 2, 3, 0 };

        // УГЛОВОЙ слой: поправка базовой ориентации углового спрайта по паре (форма × _cur_corner), четверти (×90°).
        // Итог угла = cornerRotate + это (×RotateSign в DrawAtlasQuarter). Стены и полы — РАЗНЫЕ атласы (угловой арт нарисован
        // с разной базой) → ДВЕ таблицы; сейчас расходятся на T. Строка = (int)WallShape, столбец = _cur_corner (0..5; 0 не исп.).
        // Косит угол на клетке (превью vs сцена) — правь ЕЁ ячейку: стены → CornerCorrection, полы → FloorCornerCorrection.
        private static readonly int[,] CornerCorrection =    // СТЕНЫ
        {
            //            cc0 cc1 cc2 cc3 cc4 cc5   (_cur_corner)
            /* Single  */ {  0,  0,  0,  0,  0,  0 },
            /* End     */ {  0,  0,  0,  0,  0,  0 },
            /* Straight*/ {  0,  0,  0,  0,  0,  0 },
            /* Corner  */ {  0,  1,  0,  0,  0,  0 },   // cc1 +90°
            /* T       */ {  0,  1,  0,  0,  0,  0 },   // cc1 +90°, cc2 0°
            /* Cross   */ {  0,  1,  0,  0,  2,  2 },   // cc1 +90°, cc2/cc3 0°, cc4/cc5 +180°
        };
        private static readonly int[,] FloorCornerCorrection =    // ПОЛЫ (свой атлас → своя база; сейчас = стеновой)
        {
            //            cc0 cc1 cc2 cc3 cc4 cc5   (_cur_corner)
            /* Single  */ {  0,  0,  0,  0,  0,  0 },
            /* End     */ {  0,  0,  0,  0,  0,  0 },
            /* Straight*/ {  0,  0,  0,  0,  0,  0 },
            /* Corner  */ {  0,  1,  0,  0,  0,  0 },   // cc1 +90°
            /* T       */ {  0,  1,  0,  0,  0,  0 },   // cc1 +90°, cc2 0°
            /* Cross   */ {  0,  1,  0,  0,  2,  2 },   // cc1 +90°, cc2/cc3 0°, cc4/cc5 +180°
        };

        // Верх пола атласом TopMap по форме+углам соседей-полов (как рантайм). Угловой слой — по СВОЕЙ таблице
        // FloorCornerCorrection (floor-атлас нарисован с иной базой угла, чем стеновой). false → плоский Sprite-фолбэк.
        private bool DrawFloorTop(Rect r, FloorDefinition f, int worldX, int worldY, bool hasWorld)
        {
            if (f == null || f.TopMap == null || f.Connection == null || !f.Connection.UseConnections)
                return false;
            var shape = WallShape.Single;
            int steps = 0;
            byte cornerMask = 0;
            if (hasWorld)
            {
                (shape, steps) = FloorConnectivity.ResolveAt(_catalog, _map, f, worldX, worldY, _activeZ);
                cornerMask = FloorConnectivity.ResolveCornersAt(_catalog, _map, f, worldX, worldY, _activeZ);
            }
            return DrawTopAtlas(r, f.TopMap, f.BackingMap, shape, steps, cornerMask, FloorCornerCorrection);
        }

        // Верх стены атласом TopMap по форме+углам соседей-стен. Как рантайм: только глухая (не открытая) стена-Wall с
        // UseConnections; двери/окна/открытые/без autotiling → false (плоский Sprite-фолбэк).
        private bool DrawStructureTop(Rect r, StructureDefinition s, bool openNow, int worldX, int worldY, bool hasWorld)
        {
            if (s == null || openNow || s.Category != StructureCategory.Wall
                || s.TopMap == null || s.Connection == null || !s.Connection.UseConnections)
                return false;
            var shape = WallShape.Single;
            int steps = 0;
            byte cornerMask = 0;
            if (hasWorld)
            {
                (shape, steps) = WallConnectivity.ResolveAt(_catalog, _map, s, worldX, worldY, _activeZ);
                cornerMask = WallConnectivity.ResolveCornersAt(_catalog, _map, s, worldX, worldY, _activeZ);
            }
            return DrawTopAtlas(r, s.TopMap, s.BackingMap, shape, steps, cornerMask, CornerCorrection);
        }

        // Композиция верха = как рантайм-frag TileReader: (1) подложка _DownTex → (2) основной слой _cur (верх-ряд),
        // повёрнут на 90°·(steps + ShapeBaseQuarter) → (3) слой углов _cur_corner (низ-ряд), повёрнут на cornerRotate + cornerTable[...].
        // UV колонок сверены с шейдером: осн. x=(uv.x+_cur-1)/_count_x,y=(uv.y+1)/_count_y; угол — та же схема, низ-ряд.
        // cornerTable = CornerCorrection (стены) / FloorCornerCorrection (полы). _cur==0 (Cross) шейдер гейтит основной слой
        // на подложку → колонку не рисуем. false → рисовать нечего → плоский фолбэк.
        private static bool DrawTopAtlas(Rect r, Texture2D topMap, Texture2D backing, WallShape shape, int steps, byte cornerMask, int[,] cornerTable)
        {
            bool drew = false;

            // 1. Подложка (_DownTex): база под всё, на всю клетку; крутится мешем на steps (шейдер сэмплит сырым мешевым uv).
            if (backing != null)
            {
                DrawAtlasQuarter(r, backing, FullTexUV, steps);
                drew = true;
            }

            // 2. Основной слой: колонка _cur, верх-ряд; поворот мешем на steps + per-shape база (ShapeBaseQuarter).
            //    Cross (_cur==0) — колонки нет (гейт шейдера).
            int cur = WallCornerCode.CurFromShape(shape);
            if (cur != 0)
            {
                DrawAtlasQuarter(r, topMap,
                    new Rect((cur - 1f) / TopAtlasCountX, 1f / TopAtlasCountY, 1f / TopAtlasCountX, 1f / TopAtlasCountY),
                    steps + ShapeBaseQuarter[(int)shape]);
                drew = true;
            }

            // 3. Слой углов: колонка _cur_corner, низ-ряд; поворот = cornerRotate + cornerTable[форма, _cur_corner]
            //    (cornerTable: стены — CornerCorrection, полы — FloorCornerCorrection). steps сворачивается — см. вывод.
            if (cornerMask != 0)
            {
                var (curCorner, cornerRotate) = WallCornerCode.EncodeCorners(cornerMask);
                if (curCorner != 0)
                {
                    DrawAtlasQuarter(r, topMap,
                        new Rect((curCorner - 1f) / TopAtlasCountX, 0f, 1f / TopAtlasCountX, 1f / TopAtlasCountY),
                        cornerRotate + cornerTable[(int)shape, curCorner]);
                    drew = true;
                }
            }

            return drew;
        }

        // Рисует под-прямоугольник атласа с поворотом на turns·90° по часовой вокруг центра клетки. Клетка квадратная →
        // поворот на кратный 90° не искажает. Экранный знак — RotateSign. GUI.matrix сохраняется/восстанавливается (scroll цел).
        private static void DrawAtlasQuarter(Rect r, Texture2D tex, Rect uv, int turns)
        {
            int q = ((turns * RotateSign) % 4 + 4) % 4;
            if (q == 0)
            {
                GUI.DrawTextureWithTexCoords(r, tex, uv);
                return;
            }
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(90f * q, r.center);
            GUI.DrawTextureWithTexCoords(r, tex, uv);
            GUI.matrix = saved;
        }

        // Превью autotiling (W4b + F-editor): для стены и/или пола с UseConnections рисует стабы из центра
        // к каждой соединённой стороне (соседство — из WallConnectivity/FloorConnectivity, та же логика, что
        // у рантайм-рендера) + букву базовой формы. Пол — своим цветом и тоньше, буква в нижнем углу, чтобы
        // не сливаться со стеной (клетка-стена обычно несёт и пол). Чистый оверлей: ни Tile, ни карту не
        // меняет. Без каталога/карты — тихо выходит (без падений).
        private void DrawConnectionStubs(Rect cell, int worldX, int worldY, in Tile t)
        {
            if (_catalog == null || _map == null) return;

            // Стена: жёлтые стабы, буква в верхнем-левом углу.
            if (t.StructureType != 0)
            {
                var def = _catalog.GetStructure(t.StructureType);
                if (def != null && def.Category == StructureCategory.Wall
                    && def.Connection != null && def.Connection.UseConnections)
                {
                    bool n = WallConnectivity.Connects(_catalog, _map, def, worldX, worldY + 1, _activeZ);
                    bool e = WallConnectivity.Connects(_catalog, _map, def, worldX + 1, worldY, _activeZ);
                    bool s = WallConnectivity.Connects(_catalog, _map, def, worldX, worldY - 1, _activeZ);
                    bool w = WallConnectivity.Connects(_catalog, _map, def, worldX - 1, worldY, _activeZ);
                    DrawStubs(cell, n, e, s, w, ConnStub, 2f);
                    var (shape, _) = WallConnection.Resolve(n, e, s, w);
                    DrawShapeLetter(new Rect(cell.x + 2, cell.y + 1, cell.width, 12), shape, ConnStub);
                }
            }

            // Пол: бирюзовые стабы тоньше, буква в нижнем-левом углу.
            if (t.FloorType != 0)
            {
                var f = _catalog.GetFloor(t.FloorType);
                if (f != null && f.Connection != null && f.Connection.UseConnections)
                {
                    bool n = FloorConnectivity.Connects(_catalog, _map, f, worldX, worldY + 1, _activeZ);
                    bool e = FloorConnectivity.Connects(_catalog, _map, f, worldX + 1, worldY, _activeZ);
                    bool s = FloorConnectivity.Connects(_catalog, _map, f, worldX, worldY - 1, _activeZ);
                    bool w = FloorConnectivity.Connects(_catalog, _map, f, worldX - 1, worldY, _activeZ);
                    DrawStubs(cell, n, e, s, w, FloorStub, 1f);
                    var (shape, _) = WallConnection.Resolve(n, e, s, w);
                    DrawShapeLetter(new Rect(cell.x + 2, cell.yMax - 13, cell.width, 12), shape, FloorStub);
                }
            }
        }

        // Стабы из центра к соединённым сторонам. Грид рисуется севером вверх (Y инвертирован):
        // N=вверх клетки, S=вниз, E=вправо, W=влево. new Rect — struct, без GC.
        private static void DrawStubs(Rect cell, bool n, bool e, bool s, bool w, Color color, float th)
        {
            float cx = cell.center.x;
            float cy = cell.center.y;
            float half = th * 0.5f;
            if (n) EditorGUI.DrawRect(new Rect(cx - half, cell.y, th, cy - cell.y), color);
            if (s) EditorGUI.DrawRect(new Rect(cx - half, cy, th, cell.yMax - cy), color);
            if (e) EditorGUI.DrawRect(new Rect(cx, cy - half, cell.xMax - cx, th), color);
            if (w) EditorGUI.DrawRect(new Rect(cell.x, cy - half, cx - cell.x, th), color);
            EditorGUI.DrawRect(new Rect(cx - half, cy - half, th, th), color); // узел в центре (виден и Single)
        }

        // Буква базовой формы (сверка с рантайм-WallConnection.Resolve). Цвет — через contentColor,
        // стиль кэшируется один раз (без аллокаций на repaint).
        private void DrawShapeLetter(Rect rect, WallShape shape, Color color)
        {
            _shapeStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white }
            };
            var prev = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, ShapeLetter(shape), _shapeStyle);
            GUI.contentColor = prev;
        }

        private static string ShapeLetter(WallShape shape) => shape switch
        {
            WallShape.Single => "S",
            WallShape.End => "E",
            WallShape.Straight => "I",
            WallShape.Corner => "C",
            WallShape.T => "T",
            WallShape.Cross => "X",
            _ => "?"
        };

        private void DrawSpecialMarker(Rect r, TileSpecial s)
        {
            Color tint;
            string label;
            switch (s)
            {
                case TileSpecial.Spawn: tint = new Color(0.15f, 0.80f, 0.30f, 0.40f); label = "S"; break;
                case TileSpecial.StairUp: tint = new Color(0.90f, 0.75f, 0.15f, 0.40f); label = "▲"; break;
                case TileSpecial.StairDown: tint = new Color(0.90f, 0.45f, 0.15f, 0.40f); label = "▼"; break;
                case TileSpecial.Lift: tint = new Color(0.40f, 0.45f, 0.90f, 0.40f); label = "L"; break;
                default: return;
            }
            EditorGUI.DrawRect(r, tint);
            _markerStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(r, label, _markerStyle);
        }

        private static Color CellColor(in Tile t)
        {
            if (t.StructureType != 0 && !t.Openable)
                return new Color(0.30f, 0.30f, 0.34f);          // стена/окно — серая
            if (!t.Support)
                return new Color(0.05f, 0.05f, 0.07f);          // дырка/космос — провал
            if (!t.BlocksVerticalSight)
                return new Color(0.20f, 0.45f, 0.55f);          // решётка/стекло — сине-зелёная
            return new Color(0.55f, 0.52f, 0.45f);              // сплошной пол — песочный
        }

        private static bool DrawSprite(Rect r, Sprite s)
        {
            if (s == null || s.texture == null) return false;
            var tex = s.texture;
            Rect tr = s.textureRect;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            GUI.DrawTextureWithTexCoords(r, tex, uv);
            return true;
        }

        private static void DrawBorder(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        private void DrawGridLines(Rect area)
        {
            var line = new Color(0, 0, 0, 0.25f);
            for (int x = 0; x <= _viewTilesX; x++)
                EditorGUI.DrawRect(new Rect(area.x + x * CellSize, area.y, 1, _viewTilesY * CellSize), line);
            for (int y = 0; y <= _viewTilesY; y++)
                EditorGUI.DrawRect(new Rect(area.x, area.y + y * CellSize, _viewTilesX * CellSize, 1), line);
        }

        // ---- Каталог: персист по GUID --------------------------------------

        private void LoadCatalogFromPrefs()
        {
            string guid = EditorPrefs.GetString(CatalogPrefKey, "");
            if (string.IsNullOrEmpty(guid)) return;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return;
            _catalog = AssetDatabase.LoadAssetAtPath<TileCatalog>(path);
            _catalog?.InvalidateCache();
        }

        private void SaveCatalogToPrefs()
        {
            if (_catalog == null)
            {
                EditorPrefs.DeleteKey(CatalogPrefKey);
                return;
            }
            string path = AssetDatabase.GetAssetPath(_catalog);
            string guid = AssetDatabase.AssetPathToGUID(path);
            EditorPrefs.SetString(CatalogPrefKey, guid);
        }

        // ---- Файлы ----------------------------------------------------------

        private void NewMap()
        {
            if (!ConfirmDiscard()) return;
            _map = new GridMap();
            _currentPath = null;
            _dirty = false;
            Repaint();
        }

        private void Load()
        {
            if (!ConfirmDiscard()) return;
            string path = EditorUtility.OpenFilePanel("Load station map", Application.dataPath, "smap");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                _map = MapSerializer.LoadFromFile(path);
                _currentPath = path;
                _dirty = false;
                FitViewToMap();   // кадрируем вид по загруженной карте («по максимальным значениям»)
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Load failed", ex.Message, "OK");
            }
            Repaint();
        }

        private void Save(bool saveAs)
        {
            string path = _currentPath;
            if (saveAs || string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanel("Save station map", Application.dataPath, "station", "smap");
                if (string.IsNullOrEmpty(path)) return;
            }
            try
            {
                MapSerializer.SaveToFile(path, _map);
                _currentPath = path;
                _dirty = false;
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Save failed", ex.Message, "OK");
            }
            Repaint();
        }

        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;
            return EditorUtility.DisplayDialog(
                "Unsaved changes",
                "The current map has unsaved changes. Discard them?",
                "Discard", "Cancel");
        }

        private void MarkDirty()
        {
            _dirty = true;
            Repaint();
        }
    }
}
#endif
