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
                    _viewTilesX = Mathf.Clamp(EditorGUILayout.IntField(_viewTilesX, GUILayout.Width(44)), 1, 128);
                    _viewTilesY = Mathf.Clamp(EditorGUILayout.IntField(_viewTilesY, GUILayout.Width(44)), 1, 128);
                    GUILayout.Space(10);
                    if (GUILayout.Button("к 0,0", GUILayout.Width(50))) { _originX = 0; _originY = 0; }
                    GUILayout.FlexibleSpace();
                }

                // Превью формы autotiling поверх грида — только вид, краску/данные не меняет.
                _showConnections = EditorGUILayout.ToggleLeft(
                    "Показывать соединения (стены/пол)", _showConnections);
            }
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
                    DrawFloorRow();
                    DrawStructureRow();
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

        // Превью собранной кисти + текстовая сводка.
        private void DrawBrushPreview()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Превью", GUILayout.Width(56));
                Rect box = GUILayoutUtility.GetRect(CellSize + 8, CellSize + 8,
                    GUILayout.Width(CellSize + 8), GUILayout.Height(CellSize + 8));
                var cell = new Rect(box.x + 4, box.y + 4, CellSize, CellSize);
                Tile t = MakeTile();
                DrawCell(cell, in t);
                DrawBorder(cell, new Color(0f, 0f, 0f, 0.45f), 1);

                GUILayout.Space(8);
                EditorGUILayout.LabelField(TileSummary(in t), EditorStyles.miniLabel);
            }
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
                        "ЛКМ — красить · ПКМ — стереть · наведи на клетку для инфо",
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
                    DrawCell(cell, in t);
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
                Tile painted = MakeTile();
                _map.SetTile(worldX, worldY, _activeZ, painted);
                AutoPairStair(worldX, worldY, _activeZ, painted.Special);
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

        private Tile MakeTile()
        {
            Tile t;
            if (_advanced)
            {
                t = new Tile
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
            }
            else if (_catalog != null)
            {
                // Флаги выводятся из выбранных видов пола и настенного объекта.
                t = _catalog.Compose(_selFloor, _selStructure);
            }
            else
            {
                switch (_brush)
                {
                    case Brush.Floor:
                        t = new Tile { FloorType = 1, Support = true, BlocksVerticalSight = true, SealsVertical = true };
                        break;
                    case Brush.Wall:
                        t = new Tile { FloorType = 1, StructureType = 1, Support = true, BlocksHorizontalSight = true, BlocksVerticalSight = true, SealsHorizontal = true, SealsVertical = true };
                        break;
                    case Brush.Grate:
                        t = new Tile { FloorType = 2, Support = true };
                        break;
                    default:
                        t = Tile.Space;
                        break;
                }
            }

            t.Special = _selSpecial;   // спец-маркер поверх выбранного тайла
            return t;
        }

        // Авто-пара лестниц: StairUp на z ⇒ StairDown на z+1 (та же клетка), и наоборот.
        private void AutoPairStair(int x, int y, int z, TileSpecial special)
        {
            if (special == TileSpecial.StairUp)
                PaintPairedStair(x, y, z + 1, TileSpecial.StairDown);
            else if (special == TileSpecial.StairDown)
                PaintPairedStair(x, y, z - 1, TileSpecial.StairUp);
        }

        // Парная лестница на соседнем этаже: всегда с полом (иначе на неё не перейти).
        private void PaintPairedStair(int x, int y, int z, TileSpecial special)
        {
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

        private void DrawCell(Rect r, in Tile t)
        {
            EditorGUI.DrawRect(r, CellColor(in t));

            bool structDrawn = false;
            if (_catalog != null)
            {
                if (t.FloorType != 0)
                    DrawSprite(r, _catalog.GetFloor(t.FloorType)?.Sprite);
                if (t.StructureType != 0)
                {
                    var s = _catalog.GetStructure(t.StructureType);
                    var sprite = (s != null && s.Openable && t.Open) ? s.OpenSprite : s?.Sprite;
                    structDrawn = DrawSprite(r, sprite);
                }
            }

            if (t.StructureType != 0 && !structDrawn)
                DrawBorder(r, t.Openable ? new Color(0.20f, 0.50f, 0.70f) : new Color(0.12f, 0.12f, 0.14f), 3);

            if (t.Special != TileSpecial.None)
                DrawSpecialMarker(r, t.Special);
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
