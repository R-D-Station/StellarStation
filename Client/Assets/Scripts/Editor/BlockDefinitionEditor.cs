#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Client.Map;
using Shared.World.Blocks;

namespace Client.Editor.Inspectors
{
    /// <summary>Инспектор <see cref="BlockDefinition"/>: авто-ID, цветные сворачиваемые группы, палитра каталога для автосвязей, кодоген.</summary>
    [CustomEditor(typeof(BlockDefinition))]
    public sealed class BlockDefinitionEditor : UnityEditor.Editor
    {
        private static readonly GUIContent LType = new GUIContent("ID", "BlockType в картах v10. Write-once: не перенумеровывать.");
        private static readonly GUIContent LDisplayName = new GUIContent("Название");
        private static readonly GUIContent LCategory = new GUIContent("Категория", "Door/Hatch — открываемые; Marker — невидимый триггер-блок.");
        private static readonly GUIContent LSeals = new GUIContent("Герметичность", "Герметичные грани (атмос). ZPos = верх, ZNeg = низ.");
        private static readonly GUIContent LOpaque = new GUIContent("Непрозрачность", "Непрозрачные грани (обзор/потолок-детект).");
        private static readonly GUIContent LCoversBelow = new GUIContent("Прячет верх снизу", "Скрывает текстурный верх блока ПОД собой (стена на полу). Выкл для тонкой двери — пол под ней виден.");
        private static readonly GUIContent LBoxes = new GUIContent("Боксы", "AABB object-space [0..Size], Y = высота. Пусто = без коллизии.");
        private static readonly GUIContent LBoxesOpen = new GUIContent("Боксы (открыто)", "Коллизия в открытом состоянии. Пусто = проходима насквозь.");
        private static readonly GUIContent LOpening = new GUIContent("Открытие", "Auto — сама при входе в триггер; Interact — взаимодействием.");
        private static readonly GUIContent LTriggers = new GUIContent("Триггеры", "Object-space AABB авто-двери; МОГУТ выходить за габарит. Пусто = нет авто-открытия.");
        private static readonly GUIContent LCloseDelay = new GUIContent("Задержка закрытия", "Сек после выхода игрока из триггера.");
        private static readonly GUIContent LDecon = new GUIContent("Стадии", "Число стадий деконструкции (0 = не разбирается).");
        private static readonly GUIContent LSize = new GUIContent("Размер", "Габарит в блоках (X-ширина, Y-высота, Z-глубина). Оси 1..2, частей ≤ 4. Дверь 2×2×1.");
        private static readonly GUIContent LPrefab = new GUIContent("Префаб", "Пусто → серые кубы. Ассет держать в папке Resources.");
        private static readonly GUIContent LPivot = new GUIContent("Пивот", "Bottom Center — пивот в центре низа объекта. Center — пивот в центре модели (как у Unity-примитивов): система сама поднимет на половину высоты.");
        private static readonly GUIContent LSprite = new GUIContent("Спрайт", "Превью в редакторе; для маркеров — единственный визуал.");
        private static readonly GUIContent LTopMap = new GUIContent("Грид верха", "Грид-текстура верха → _TopMap (шейдер TileReader); форму выбирает автотекстуринг по соседям. Пусто → без текстурного верха.");
        private static readonly GUIContent LBackingMap = new GUIContent("Подложка", "Подложка верха → _DownTex.");
        private static readonly GUIContent LTopCountX = new GUIContent("Колонок", "Колонок в грид-атласе → _count_x (как в TileTop.mat: 5).");
        private static readonly GUIContent LTopCountY = new GUIContent("Рядов", "Рядов в грид-атласе → _count_y (верхний ряд — формы, нижний — углы; TileTop.mat: 2).");
        private static readonly GUIContent LTopCalibration = new GUIContent("Калибровка", "Доп. повороты верха (общий ассет на атлас). Пусто → без поправок.");
        private static readonly GUIContent LUseGrid = new GUIContent("Управление гридом", "Блок использует грид TileReader (верх/бок) — показать грид-поля. Влияет только на видимость полей в инспекторе.");
        private static readonly GUIContent LSideMap = new GUIContent("Грид бока", "Грид-текстура боковины → _TopMap материала WallRenderers. Пусто → без бокового грида.");
        private static readonly GUIContent LConnSame = new GUIContent("С тем же типом", "Соединяться с блоками того же типа.");
        private static readonly GUIContent LMeshSingle = new GUIContent("Меш Single");
        private static readonly GUIContent LMeshEnd = new GUIContent("Меш End");
        private static readonly GUIContent LMeshStraight = new GUIContent("Меш Straight");
        private static readonly GUIContent LMeshCorner = new GUIContent("Меш Corner");
        private static readonly GUIContent LMeshT = new GUIContent("Меш T");
        private static readonly GUIContent LMeshCross = new GUIContent("Меш Cross");

        private static readonly Color CWhite = new Color(0.80f, 0.80f, 0.82f);
        private static readonly Color CGreen = new Color(0.24f, 0.44f, 0.28f);
        private static readonly Color CBlack = new Color(0.15f, 0.15f, 0.17f);
        private static readonly Color CPurple = new Color(0.40f, 0.30f, 0.52f);

        // static — свёрнутость групп сохраняется между выборами разных BlockDefinition в сессии редактора.
        private static bool _gPlay = true, _gCollision = true, _gTiling = true, _gAuto = true;
        private static GUIStyle _hDark, _hLight;

        private SerializedProperty _type;
        private SerializedProperty _displayName;
        private SerializedProperty _category;
        private SerializedProperty _sealsFaces;
        private SerializedProperty _opaqueFaces;
        private SerializedProperty _coversBelow;
        private SerializedProperty _collisionBoxes;
        private SerializedProperty _collisionBoxesOpen;
        private SerializedProperty _opening;
        private SerializedProperty _triggerBoxes;
        private SerializedProperty _doorCloseDelay;
        private SerializedProperty _deconstructStages;
        private SerializedProperty _size;
        private SerializedProperty _prefab;
        private SerializedProperty _pivot;
        private SerializedProperty _editorSprite;
        private SerializedProperty _topMap;
        private SerializedProperty _backingMap;
        private SerializedProperty _topCountX;
        private SerializedProperty _topCountY;
        private SerializedProperty _topCalibration;
        private SerializedProperty _sideMap;
        private SerializedProperty _useGrid;
        private SerializedProperty _connection;
        private SerializedProperty _connUse, _connSame, _connTypes;
        private SerializedProperty _connMeshSingle, _connMeshEnd, _connMeshStraight, _connMeshCorner, _connMeshT, _connMeshCross;
        private ushort[] _catTypes = System.Array.Empty<ushort>();
        private string[] _catNames = System.Array.Empty<string>();

        // Кэш авто-ID/коллизии: скан AssetDatabase дорог — только при смене _type.intValue.
        private int _idScannedFor = int.MinValue;
        private BlockDefinition _idConflict;
        private bool _noFreeIds;

        private void OnEnable()
        {
            _type = serializedObject.FindProperty("Type");
            _displayName = serializedObject.FindProperty("DisplayName");
            _category = serializedObject.FindProperty("Category");
            _sealsFaces = serializedObject.FindProperty("SealsFaces");
            _opaqueFaces = serializedObject.FindProperty("OpaqueFaces");
            _coversBelow = serializedObject.FindProperty("CoversBlockBelow");
            _collisionBoxes = serializedObject.FindProperty("CollisionBoxes");
            _collisionBoxesOpen = serializedObject.FindProperty("CollisionBoxesOpen");
            _opening = serializedObject.FindProperty("Opening");
            _triggerBoxes = serializedObject.FindProperty("TriggerBoxes");
            _doorCloseDelay = serializedObject.FindProperty("DoorCloseDelay");
            _deconstructStages = serializedObject.FindProperty("DeconstructStages");
            _size = serializedObject.FindProperty("Size");
            _prefab = serializedObject.FindProperty("Prefab");
            _pivot = serializedObject.FindProperty("Pivot");
            _editorSprite = serializedObject.FindProperty("EditorSprite");
            _topMap = serializedObject.FindProperty("TopMap");
            _backingMap = serializedObject.FindProperty("BackingMap");
            _topCountX = serializedObject.FindProperty("TopMapCountX");
            _topCountY = serializedObject.FindProperty("TopMapCountY");
            _topCalibration = serializedObject.FindProperty("TopCalibration");
            _sideMap = serializedObject.FindProperty("SideMap");
            _useGrid = serializedObject.FindProperty("UseGrid");
            _connection = serializedObject.FindProperty("Connection");
            if (_connection != null)
            {
                _connUse = _connection.FindPropertyRelative("UseConnections");
                _connSame = _connection.FindPropertyRelative("ConnectsToSameType");
                _connTypes = _connection.FindPropertyRelative("ConnectsToTypes");
                _connMeshSingle = _connection.FindPropertyRelative("MeshSingle");
                _connMeshEnd = _connection.FindPropertyRelative("MeshEnd");
                _connMeshStraight = _connection.FindPropertyRelative("MeshStraight");
                _connMeshCorner = _connection.FindPropertyRelative("MeshCorner");
                _connMeshT = _connection.FindPropertyRelative("MeshT");
                _connMeshCross = _connection.FindPropertyRelative("MeshCross");
            }
            BuildCatalogPalette();
        }

        // Кэш id→имя всего каталога для попапов ConnectsToTypes; пересобирается только в OnEnable (скан ассетов дорог).
        private void BuildCatalogPalette()
        {
            var defs = Client.Editor.BlockCatalogCodegen.LoadAllDefinitions();
            defs.Sort((a, b) => a.Type.CompareTo(b.Type));
            var types = new List<ushort>();
            var names = new List<string>();
            foreach (var d in defs)
                if (d != null && d.Type != 0)
                {
                    types.Add(d.Type);
                    names.Add($"{d.Type} — {d.DisplayName}");
                }
            _catTypes = types.ToArray();
            _catNames = names.ToArray();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var self = target as BlockDefinition;

            if (_type.intValue != _idScannedFor)
            {
                var used = CollectUsedIds(self);
                if (_type.intValue == 0)
                {
                    ushort free = BlockIdAllocator.SmallestFreeId(used);
                    if (free > 0) _type.intValue = free;
                    _noFreeIds = free == 0;
                    _idConflict = null;
                }
                else
                {
                    _idConflict = FindConflict(self, (ushort)_type.intValue);
                    _noFreeIds = false;
                }
                _idScannedFor = _type.intValue;
            }

            EditorGUILayout.PropertyField(_type, LType);
            if (_noFreeIds)
                EditorGUILayout.HelpBox("Нет свободных id (1..65535 заняты).", MessageType.Error);
            else if (_idConflict != null)
                EditorGUILayout.HelpBox($"ID {_type.intValue} уже занят: '{_idConflict.DisplayName}'.", MessageType.Error);

            EditorGUILayout.PropertyField(_displayName, LDisplayName);
            EditorGUILayout.PropertyField(_category, LCategory);
            if (_size != null)
            {
                EditorGUILayout.PropertyField(_size, LSize);
                var sv = _size.vector3IntValue;
                if (sv.x < 1 || sv.y < 1 || sv.z < 1 || sv.x > 2 || sv.y > 2 || sv.z > 2
                    || sv.x * sv.y * sv.z > 4)
                    EditorGUILayout.HelpBox("Оси 1..2 и не больше 4 частей (ёмкость part-бит).", MessageType.Error);
            }

            if (_prefab != null) EditorGUILayout.PropertyField(_prefab, LPrefab);
            if (_pivot != null) EditorGUILayout.PropertyField(_pivot, LPivot);
            EditorGUILayout.PropertyField(_editorSprite, LSprite);

            EditorGUILayout.Space(6);
            bool openable = self != null &&
                (self.Category == BlockCategory.Door || self.Category == BlockCategory.Hatch);

            if (BeginGroup(ref _gPlay, "Игровые настройки", CWhite, true))
            {
                EditorGUILayout.PropertyField(_sealsFaces, LSeals);
                EditorGUILayout.PropertyField(_opaqueFaces, LOpaque);
                EditorGUILayout.PropertyField(_deconstructStages, LDecon);
            }
            EndGroup(_gPlay);

            if (BeginGroup(ref _gCollision, "Коллизия", CGreen, false))
            {
                EditorGUILayout.PropertyField(_collisionBoxes, LBoxes, true);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Full")) SetBoxes(self, BlockDefinition.CollisionBox.Full);
                    if (GUILayout.Button("Слаб 0.25")) SetBoxes(self, BlockDefinition.CollisionBox.SlabTop);
                    if (GUILayout.Button("Empty")) SetBoxes(self);
                }
                if (openable)
                {
                    if (_collisionBoxesOpen != null) EditorGUILayout.PropertyField(_collisionBoxesOpen, LBoxesOpen, true);
                    if (_opening != null) EditorGUILayout.PropertyField(_opening, LOpening);
                    if (_triggerBoxes != null) EditorGUILayout.PropertyField(_triggerBoxes, LTriggers, true);
                    if (_doorCloseDelay != null) EditorGUILayout.PropertyField(_doorCloseDelay, LCloseDelay);
                }
            }
            EndGroup(_gCollision);

            if (BeginGroup(ref _gTiling, "Тайлинг", CBlack, false))
            {
                if (_coversBelow != null) EditorGUILayout.PropertyField(_coversBelow, LCoversBelow);
                if (_useGrid != null) EditorGUILayout.PropertyField(_useGrid, LUseGrid);
                if (_useGrid != null && _useGrid.boolValue)
                {
                    if (_topMap != null) EditorGUILayout.PropertyField(_topMap, LTopMap);
                    if (_backingMap != null) EditorGUILayout.PropertyField(_backingMap, LBackingMap);
                    if (_topCountX != null) EditorGUILayout.PropertyField(_topCountX, LTopCountX);
                    if (_topCountY != null) EditorGUILayout.PropertyField(_topCountY, LTopCountY);
                    if (_topCalibration != null) EditorGUILayout.PropertyField(_topCalibration, LTopCalibration);
                    EditorGUILayout.Space(6);
                    if (_sideMap != null) EditorGUILayout.PropertyField(_sideMap, LSideMap);
                }
            }
            EndGroup(_gTiling);

            EditorGUILayout.Space(4);
            if (_connection != null)
            {
                if (BeginGroupToggle(ref _gAuto, "Автотайлинг", CPurple, false, _connUse))
                {
                    EditorGUILayout.LabelField("База мешей — соединением на север (+Z); поворот 90° по часовой.",
                        EditorStyles.miniLabel);
                    using (new EditorGUI.DisabledScope(_connUse != null && !_connUse.boolValue))
                    {
                        if (_connSame != null) EditorGUILayout.PropertyField(_connSame, LConnSame);
                        if (_connTypes != null) DrawTypeList(_connTypes);
                        if (_connMeshSingle != null)
                        {
                            EditorGUILayout.PropertyField(_connMeshSingle, LMeshSingle);
                            EditorGUILayout.PropertyField(_connMeshEnd, LMeshEnd);
                            EditorGUILayout.PropertyField(_connMeshStraight, LMeshStraight);
                            EditorGUILayout.PropertyField(_connMeshCorner, LMeshCorner);
                            EditorGUILayout.PropertyField(_connMeshT, LMeshT);
                            EditorGUILayout.PropertyField(_connMeshCross, LMeshCross);
                        }
                    }
                }
                EndGroup(_gAuto);
            }

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Сгенерировать каталог (Shared)"))
                BlockCatalogCodegen.Generate();

            serializedObject.ApplyModifiedProperties();
        }

        private static bool BeginGroup(ref bool expanded, string title, Color bg, bool darkText)
        {
            Rect r = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(r, bg);
            Rect fr = new Rect(r.x + 14f, r.y + 1f, r.width - 16f, r.height);
            expanded = EditorGUI.Foldout(fr, expanded, title, true, HeaderStyle(darkText));
            if (expanded) EditorGUI.indentLevel++;
            return expanded;
        }

        /// <summary>Как <see cref="BeginGroup"/>, но с чекбоксом enable в правом углу шапки (для "Автотайлинг").</summary>
        private static bool BeginGroupToggle(ref bool expanded, string title, Color bg, bool darkText, SerializedProperty enabled)
        {
            Rect r = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(r, bg);
            Rect fr = new Rect(r.x + 14f, r.y + 1f, r.width - 40f, r.height);
            expanded = EditorGUI.Foldout(fr, expanded, title, true, HeaderStyle(darkText));
            if (enabled != null)
            {
                Rect tr = new Rect(r.xMax - 22f, r.y + 2f, 16f, 16f);
                enabled.boolValue = EditorGUI.Toggle(tr, enabled.boolValue);
            }
            if (expanded) EditorGUI.indentLevel++;
            return expanded;
        }

        private static void EndGroup(bool expanded)
        {
            if (expanded) EditorGUI.indentLevel--;
            EditorGUILayout.Space(3);
        }

        private static GUIStyle HeaderStyle(bool darkText)
        {
            if (darkText)
                return _hDark ??= MakeHeaderStyle(Color.black);
            return _hLight ??= MakeHeaderStyle(Color.white);
        }

        private static GUIStyle MakeHeaderStyle(Color c)
        {
            var s = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            s.normal.textColor = c; s.onNormal.textColor = c;
            s.hover.textColor = c; s.onHover.textColor = c;
            s.focused.textColor = c; s.onFocused.textColor = c;
            s.active.textColor = c; s.onActive.textColor = c;
            return s;
        }

        private static void SetBoxes(BlockDefinition def, params BlockDefinition.CollisionBox[] boxes)
        {
            Undo.RecordObject(def, "Set collision preset");
            def.CollisionBoxes = boxes;
            EditorUtility.SetDirty(def);
        }

        private static List<ushort> CollectUsedIds(BlockDefinition exclude)
        {
            var used = new List<ushort>();
            foreach (var def in Client.Editor.BlockCatalogCodegen.LoadAllDefinitions())
                if (def != exclude && def.Type != 0)
                    used.Add(def.Type);
            return used;
        }

        private static BlockDefinition FindConflict(BlockDefinition self, ushort id)
        {
            foreach (var def in Client.Editor.BlockCatalogCodegen.LoadAllDefinitions())
                if (def != self && def.Type == id)
                    return def;
            return null;
        }

        /// <summary>Рисует ConnectsToTypes как попапы по палитре каталога с +/-; тип пишется только по явному действию юзера.</summary>
        private void DrawTypeList(SerializedProperty arr)
        {
            EditorGUILayout.LabelField("Соединять с блоками", EditorStyles.miniBoldLabel);
            if (_catTypes.Length == 0)
            {
                EditorGUILayout.HelpBox("В каталоге нет блоков.", MessageType.None);
                return;
            }
            int remove = -1;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var elem = arr.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    int shown = Mathf.Max(0, IndexOfType((ushort)elem.intValue));
                    int sel = EditorGUILayout.Popup(shown, _catNames);
                    if (sel != shown && sel >= 0 && sel < _catTypes.Length)
                        elem.intValue = _catTypes[sel];
                    if (GUILayout.Button("−", GUILayout.Width(24)))
                        remove = i;
                }
            }
            if (remove >= 0)
                arr.DeleteArrayElementAtIndex(remove);
            if (GUILayout.Button("+ блок"))
            {
                int n = arr.arraySize;
                arr.arraySize = n + 1;
                arr.GetArrayElementAtIndex(n).intValue = _catTypes[0];
            }
        }

        private int IndexOfType(ushort t)
        {
            for (int i = 0; i < _catTypes.Length; i++)
                if (_catTypes[i] == t)
                    return i;
            return -1;
        }
    }
}
#endif
