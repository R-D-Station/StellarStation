#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Shared.World;

namespace Client.Editor.MapTools
{
    /// <summary>
    /// Редактор тайловой карты. Окно Unity (Tools → Station → Map Editor).
    /// Правит ОДИН активный Z-слой за раз. Работает напрямую с Shared.GridMap —
    /// своей модели данных нет: что нарисовал = что сохранил = что загрузит сервер.
    /// Сохранение/загрузка — через Shared.MapSerializer (нейтральный бинарь).
    ///
    /// Цвет клетки выводится из данных тайла (тип/флаги), а не задаётся вручную —
    /// карта читается с одного взгляда. Арт игры здесь не используется: редактор
    /// автономен и зависит только от структур Shared.
    /// </summary>
    public class MapEditorWindow : EditorWindow
    {
        // Пресеты палитры. Каждый — готовая комбинация полей Tile.
        private enum Brush { Floor, Wall, Grate, Space }

        private GridMap _map;
        private string _currentPath;     // путь последнего save/load, для быстрого пересохранения
        private bool _dirty;             // есть несохранённые правки

        private int _activeZ;
        private Brush _brush = Brush.Floor;

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
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(4);
            DrawPalette();
            EditorGUILayout.Space(6);
            DrawGrid();
        }

        // ---- Toolbar: файл, активный Z, размеры вида ------------------------

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

        // ---- Palette: пресеты + advanced флаги ------------------------------

        private void DrawPalette()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Brush", GUILayout.Width(40));
                DrawBrushButton(Brush.Floor, "Floor");
                DrawBrushButton(Brush.Wall, "Wall");
                DrawBrushButton(Brush.Grate, "Grate");
                DrawBrushButton(Brush.Space, "Space");
            }

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
                        "Advanced paints this exact tile. Presets above ignore these fields.",
                        MessageType.None);
                }
            }
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

        // ---- Grid: отрисовка прямоугольниками + кисть -----------------------

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
                _map.SetTile(worldX, worldY, _activeZ, MakeTile());
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
            if (_advanced)
            {
                return new Tile
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

            switch (_brush)
            {
                case Brush.Floor:
                    // Сплошной пол: стоишь, не видно/не дует по вертикали.
                    return new Tile { FloorType = 1, WallType = 0, Support = true, BlocksHorizontalSight = false, BlocksVerticalSight = true, SealsHorizontal = false, SealsVertical = true };
                case Brush.Wall:
                    // Стена на полу: войти нельзя, держит взгляд и газ по горизонтали; пол под ней цел.
                    return new Tile { FloorType = 1, WallType = 1, Support = true, BlocksHorizontalSight = true, BlocksVerticalSight = true, SealsHorizontal = true, SealsVertical = true };
                case Brush.Grate:
                    // Решётка: стоишь, видно сквозь вниз И газ проходит вниз.
                    return new Tile { FloorType = 2, WallType = 0, Support = true, BlocksHorizontalSight = false, BlocksVerticalSight = false, SealsHorizontal = false, SealsVertical = false };
                default:
                    return Tile.Space;
            }
        }

        // ---- Рисование клетки: цвет = функция от данных ---------------------

        private static void DrawCell(Rect r, in Tile t)
        {
            EditorGUI.DrawRect(r, CellColor(in t));

            // Стена — толстая тёмная рамка, чтобы читалась поверх цвета.
            if (t.WallType != 0)
                DrawBorder(r, new Color(0.12f, 0.12f, 0.14f), 3);
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