#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Shared.World;
using Client.Map;

namespace Client.Editor.MapTools
{
    /// <summary>Окно-редактор тайловой карты (Tools → Station → Map Editor): правит один Z-слой через Shared.GridMap.</summary>
    public class MapEditorWindow : EditorWindow
    {
        // Пресеты палитры (fallback без каталога).
        private enum Brush { Floor, Wall, Grate, Space }

        private GridMap _map;
        private string _currentPath;     // путь последнего save/load, для быстрого пересохранения
        private bool _dirty;             // есть несохранённые правки

        private int _activeZ;
        private Brush _brush = Brush.Floor;

        // Каталог тайлов (id ↔ спрайт/флаги); персист по GUID ассета.
        private TileCatalog _catalog;
        private const string CatalogPrefKey = "Station.MapEditor.CatalogGuid";

        // Выбор кисти при каталоге (0 = слой не трогаем).
        private byte _selFloor = 1;
        private byte _selWall = 0;
        private byte _selDoor = 0;
        private TileSpecial _selSpecial = TileSpecial.None;

        private GUIStyle _markerStyle; // ленивый стиль для буквы маркера в клетке

        // Advanced: ручное редактирование полей тайла вместо пресета.
        private bool _advanced;
        private byte _advFloorType = 1;
        private byte _advWallType;
        private bool _advSupport = true;
        private bool _advHBlock;
        private bool _advVBlock = true;
        private bool _advSealH;
        private bool _advSealV = true;

        // Вид сетки.
        private const int CellSize = 24;
        private int _viewTilesX = 32;    // сколько тайлов рисуем по ширине от origin
        private int _viewTilesY = 32;
        private int _originX;            // тайловые координаты левого-нижнего угла вида
        private int _originY;

        private Vector2 _scroll;

        [MenuItem("Tools/Station/Map Editor")]
        public static void Open()
        {
            var w = GetWindow<MapEditorWindow>("Map Editor");
            w.minSize = new Vector2(520, 480);
        }

        private void OnEnable()
        {
            if (_map == null) _map = new GridMap();
            LoadCatalogFromPrefs();
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(4);
            DrawPalette();
            EditorGUILayout.Space(6);
            DrawGrid();
        }

        // ---- Toolbar: файл, каталог, активный Z, размеры вида --------------

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    NewMap();
                if (GUILayout.Button("Load", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    Load();
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    Save(saveAs: false);
                if (GUILayout.Button("Save As", EditorStyles.toolbarButton, GUILayout.Width(64)))
                    Save(saveAs: true);

                GUILayout.FlexibleSpace();

                GUILayout.Label(_dirty ? "● unsaved" : "saved", GUILayout.Width(70));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Catalog", GUILayout.Width(54));
                EditorGUI.BeginChangeCheck();
                _catalog = (TileCatalog)EditorGUILayout.ObjectField(_catalog, typeof(TileCatalog), false, GUILayout.Width(180));
                if (EditorGUI.EndChangeCheck())
                {
                    SaveCatalogToPrefs();
                    _catalog?.InvalidateCache();
                }
                if (_catalog == null)
                    GUILayout.Label("— нет: цвета+хардкод кисти", EditorStyles.miniLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Floor (Z)", GUILayout.Width(60));
                if (GUILayout.Button("−", GUILayout.Width(24))) _activeZ--;
                _activeZ = EditorGUILayout.IntField(_activeZ, GUILayout.Width(50));
                if (GUILayout.Button("+", GUILayout.Width(24))) _activeZ++;

                GUILayout.Space(16);
                EditorGUILayout.LabelField("View", GUILayout.Width(34));
                EditorGUILayout.LabelField("origin", GUILayout.Width(42));
                _originX = EditorGUILayout.IntField(_originX, GUILayout.Width(44));
                _originY = EditorGUILayout.IntField(_originY, GUILayout.Width(44));
                EditorGUILayout.LabelField("size", GUILayout.Width(30));
                _viewTilesX = Mathf.Clamp(EditorGUILayout.IntField(_viewTilesX, GUILayout.Width(40)), 1, 128);
                _viewTilesY = Mathf.Clamp(EditorGUILayout.IntField(_viewTilesY, GUILayout.Width(40)), 1, 128);
            }
        }

        // ---- Palette: из каталога (или fallback-пресеты) + advanced ---------

        private void DrawPalette()
        {
            if (_catalog != null && !_advanced)
                DrawCatalogPalette();
            else
                DrawPresetPalette();

            DrawSpecialRow();

            _advanced = EditorGUILayout.Foldout(_advanced, "Advanced (edit tile flags)", true);
            if (_advanced)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    _advFloorType = (byte)EditorGUILayout.IntSlider("Floor Type", _advFloorType, 0, 255);
                    _advWallType = (byte)EditorGUILayout.IntSlider("Wall Type", _advWallType, 0, 255);
                    _advSupport = EditorGUILayout.Toggle("Support (stand)", _advSupport);
                    _advHBlock = EditorGUILayout.Toggle("Blocks Horizontal Sight (wall)", _advHBlock);
                    _advVBlock = EditorGUILayout.Toggle("Blocks Vertical Sight (floor/ceiling)", _advVBlock);
                    _advSealH = EditorGUILayout.Toggle("Seals Horizontal (gas)", _advSealH);
                    _advSealV = EditorGUILayout.Toggle("Seals Vertical (gas)", _advSealV);
                    EditorGUILayout.HelpBox(
                        "Advanced paints this exact tile. Presets/catalog above ignore these fields.",
                        MessageType.None);
                }
            }
        }

        // Палитра по каталогу: ряд полов + ряд стен, у каждого «None». ЛКМ кладёт
        // выбранную комбинацию (Compose), ПКМ стирает в космос.
        private void DrawCatalogPalette()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Floor", GUILayout.Width(40));
                DrawSelectButton("None", _selFloor == 0, () => _selFloor = 0);
                foreach (var f in _catalog.Floors)
                {
                    if (f == null) continue;
                    byte id = f.Type;
                    DrawSelectButton(string.IsNullOrEmpty(f.DisplayName) ? id.ToString() : f.DisplayName,
                        _selFloor == id, () => _selFloor = id);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Wall", GUILayout.Width(40));
                DrawSelectButton("None", _selWall == 0, () => _selWall = 0);
                foreach (var w in _catalog.Walls)
                {
                    if (w == null) continue;
                    byte id = w.Type;
                    DrawSelectButton(string.IsNullOrEmpty(w.DisplayName) ? id.ToString() : w.DisplayName,
                        _selWall == id, () => _selWall = id);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Door", GUILayout.Width(40));
                DrawSelectButton("None", _selDoor == 0, () => _selDoor = 0);
                foreach (var d in _catalog.Doors)
                {
                    if (d == null) continue;
                    byte id = d.Type;
                    DrawSelectButton(string.IsNullOrEmpty(d.DisplayName) ? id.ToString() : d.DisplayName,
                        _selDoor == id, () => _selDoor = id);
                }
            }
        }

        private void DrawPresetPalette()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Brush", GUILayout.Width(40));
                DrawBrushButton(Brush.Floor, "Floor");
                DrawBrushButton(Brush.Wall, "Wall");
                DrawBrushButton(Brush.Grate, "Grate");
                DrawBrushButton(Brush.Space, "Space");
            }
        }

        // Спец-маркер тайла (поверх пола/стены). Пока — точка спавна.
        private void DrawSpecialRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Special", GUILayout.Width(54));
                DrawSelectButton("None", _selSpecial == TileSpecial.None, () => _selSpecial = TileSpecial.None);
                DrawSelectButton("Spawn", _selSpecial == TileSpecial.Spawn, () => _selSpecial = TileSpecial.Spawn);
                DrawSelectButton("Stair Up", _selSpecial == TileSpecial.StairUp, () => _selSpecial = TileSpecial.StairUp);
                DrawSelectButton("Stair Down", _selSpecial == TileSpecial.StairDown, () => _selSpecial = TileSpecial.StairDown);
            }
            if (_selSpecial == TileSpecial.StairUp || _selSpecial == TileSpecial.StairDown)
                EditorGUILayout.HelpBox("Лестница: парная авто-ставится на соседнем этаже (та же клетка). Выбери ещё и Floor.", MessageType.None);
        }

        private void DrawSelectButton(string label, bool on, System.Action onClick)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = on ? Color.cyan : prev;
            if (GUILayout.Button(label, GUILayout.Height(22)))
                onClick();
            GUI.backgroundColor = prev;
        }

        private void DrawBrushButton(Brush b, string label)
        {
            bool on = _brush == b && !_advanced;
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = on ? Color.cyan : prev;
            if (GUILayout.Button(label, GUILayout.Height(22)))
            {
                _brush = b;
                _advanced = false;
            }
            GUI.backgroundColor = prev;
        }

        // ---- Grid: отрисовка + кисть ---------------------------------------

        private void DrawGrid()
        {
            float w = _viewTilesX * CellSize;
            float h = _viewTilesY * CellSize;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // Резервируем прямоугольник под всю сетку.
            Rect area = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            Event e = Event.current;

            for (int ty = 0; ty < _viewTilesY; ty++)
            {
                for (int tx = 0; tx < _viewTilesX; tx++)
                {
                    int worldX = _originX + tx;
                    int worldY = _originY + ty;

                    // Экран: Y растёт вниз, тайловый Y — вверх. Инвертируем, чтобы
                    // север был сверху (как игрок видит мир).
                    float px = area.x + tx * CellSize;
                    float py = area.y + (_viewTilesY - 1 - ty) * CellSize;
                    var cell = new Rect(px, py, CellSize, CellSize);

                    Tile t = _map.GetTile(worldX, worldY, _activeZ);
                    DrawCell(cell, in t);

                    HandleCellInput(e, cell, worldX, worldY);
                }
            }

            DrawGridLines(area);

            EditorGUILayout.EndScrollView();

            if (e.type == EventType.MouseUp)
                _painting = false;
        }

        private bool _painting;

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
                MarkDirty();
                e.Use();
            }
            else if (e.button == 1)
            {
                _map.SetTile(worldX, worldY, _activeZ, Tile.Space);
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
                    WallType = _advWallType,
                    Support = _advSupport,
                    BlocksHorizontalSight = _advHBlock,
                    BlocksVerticalSight = _advVBlock,
                    SealsHorizontal = _advSealH,
                    SealsVertical = _advSealV
                };
            }
            else if (_catalog != null)
            {
                // С каталогом флаги выводятся из выбранных видов пола/стены/двери.
                t = _catalog.Compose(_selFloor, _selWall, _selDoor);
            }
            else
            {
                // Fallback-пресеты (без каталога).
                switch (_brush)
                {
                    case Brush.Floor:
                        // Сплошной пол.
                        t = new Tile { FloorType = 1, WallType = 0, Support = true, BlocksHorizontalSight = false, BlocksVerticalSight = true, SealsHorizontal = false, SealsVertical = true };
                        break;
                    case Brush.Wall:
                        // Стена на полу.
                        t = new Tile { FloorType = 1, WallType = 1, Support = true, BlocksHorizontalSight = true, BlocksVerticalSight = true, SealsHorizontal = true, SealsVertical = true };
                        break;
                    case Brush.Grate:
                        // Решётка: видно и газ проходит вниз.
                        t = new Tile { FloorType = 2, WallType = 0, Support = true, BlocksHorizontalSight = false, BlocksVerticalSight = false, SealsHorizontal = false, SealsVertical = false };
                        break;
                    default:
                        t = Tile.Space;
                        break;
                }
            }

            t.Special = _selSpecial; // спец-маркер (напр. точка спавна) поверх выбранного тайла
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
                ? _catalog.Compose(floor, 0, 0)
                : new Tile { FloorType = floor, WallType = 0, Support = true, BlocksHorizontalSight = false, BlocksVerticalSight = true, SealsHorizontal = false, SealsVertical = true };
            t.Special = special;
            _map.SetTile(x, y, z, t);
        }

        // ---- Рисование клетки ----------------------------------------------

        private void DrawCell(Rect r, in Tile t)
        {
            // База — цвет от данных (читается, даже если спрайт не задан).
            EditorGUI.DrawRect(r, CellColor(in t));

            bool wallDrawn = false;
            bool doorDrawn = false;
            if (_catalog != null)
            {
                if (t.FloorType != 0)
                    DrawSprite(r, _catalog.GetFloor(t.FloorType)?.Sprite);
                if (t.WallType != 0)
                    wallDrawn = DrawSprite(r, _catalog.GetWall(t.WallType)?.Sprite);
                if (t.DoorType != 0)
                    doorDrawn = DrawSprite(r, _catalog.GetDoor(t.DoorType)?.ClosedSprite);
            }

            // Стена без спрайта — толстая тёмная рамка, чтобы читалась поверх цвета.
            if (t.WallType != 0 && !wallDrawn)
                DrawBorder(r, new Color(0.12f, 0.12f, 0.14f), 3);
            // Дверь без спрайта — синяя рамка, чтобы было видно разметку.
            if (t.DoorType != 0 && !doorDrawn)
                DrawBorder(r, new Color(0.20f, 0.50f, 0.70f), 3);

            // Спец-маркеры (спавн/лестницы/лифт) — подсветка с буквой.
            if (t.Special != TileSpecial.None)
                DrawSpecialMarker(r, t.Special);
        }

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
            if (t.WallType != 0)
                return new Color(0.30f, 0.30f, 0.34f);          // стена — серая

            if (!t.Support)
                return new Color(0.05f, 0.05f, 0.07f);          // дырка/космос — тёмный провал

            if (!t.BlocksVerticalSight)
                return new Color(0.20f, 0.45f, 0.55f);          // решётка/стекло — сине-зелёная

            return new Color(0.55f, 0.52f, 0.45f);              // сплошной пол — песочный
        }

        // Рисует спрайт в клетке (учитывает атлас через texCoords). true, если нарисовал.
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
