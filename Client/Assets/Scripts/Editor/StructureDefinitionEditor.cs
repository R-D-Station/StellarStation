#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Client.Map;
using Shared.World;

namespace Client.Editor.Inspectors
{
    /// <summary>
    /// Кастомный инспектор <see cref="StructureDefinition"/>. Дефолтный показывает все поля
    /// сразу, хотя для каждой <see cref="StructureCategory"/> релевантна лишь часть: открытые
    /// спрайт/префаб нужны только дверям/люкам, блок autotiling-соединений — только стенам.
    /// Здесь нерелевантные поля просто не рисуются, чтобы при авторинге ассета не было шума.
    /// Класс чисто редакторный: рантайм-данные не меняются, поля берутся через
    /// SerializedProperty по их именам.
    /// </summary>
    [CustomEditor(typeof(StructureDefinition))]
    public sealed class StructureDefinitionEditor : UnityEditor.Editor
    {
        // Короткие человеческие подписи. Кешируются один раз (а не new GUIContent на repaint —
        // правило CLAUDE «без аллокаций в OnGUI»); тултипы перенесены из [Tooltip] исходных полей.
        private static readonly GUIContent LType = new GUIContent("ID", "Значение Tile.StructureType. 0 = объекта нет.");
        private static readonly GUIContent LDisplayName = new GUIContent("Название");
        private static readonly GUIContent LCategory = new GUIContent("Категория", "Стена/дверь/люк/окно. Дверь и люк — открываемые.");
        private static readonly GUIContent LSprite = new GUIContent("Спрайт");
        private static readonly GUIContent LPrefab = new GUIContent("Префаб");
        private static readonly GUIContent LTopMap = new GUIContent("Грид верха", "Грид-текстура верха → _TopMap материала WallTop. Форму выбирает шейдер по _cur.");
        private static readonly GUIContent LBackingMap = new GUIContent("Подложка", "Подложка → _DownTex материала WallTop.");
        private static readonly GUIContent LSideMap = new GUIContent("Боковина", "Текстура боковины → _TopMap материала TileWall.");
        private static readonly GUIContent LOpenSprite = new GUIContent("Спрайт (открыт)");
        private static readonly GUIContent LOpenPrefab = new GUIContent("Префаб (открыт)");
        private static readonly GUIContent LBlocksSight = new GUIContent("Держит обзор", "Держит обзор по горизонтали (в закрытом виде). Стекло/окно = false.");
        private static readonly GUIContent LSeals = new GUIContent("Герметична", "Не пропускает газ по горизонтали.");
        private static readonly GUIContent LBlocksVSight = new GUIContent("Держит обзор Z",
            "Держит обзор по вертикали (Z). false = see-through проём: видно сквозь этаж (reveal-дыра). Лестница-верх=false; глухая стена=true.");
        private static readonly GUIContent LNoVisibleFloor = new GUIContent("Скрыть пол",
            "Пол под этой структурой не рендерится и считается отсутствующим для автотайлинга соседних полов (пол физически остаётся — walkable не меняется).");
        private static readonly GUIContent LUseConnections = new GUIContent("Автотайлинг", "Выбирать меш по 4 соседям.");
        private static readonly GUIContent LSameType = new GUIContent("С тем же типом");
        private static readonly GUIContent LOtherWalls = new GUIContent("С другими стенами");
        private static readonly GUIContent LWindows = new GUIContent("С окнами");
        private static readonly GUIContent LDoorsHatches = new GUIContent("С дверьми/люками");
        private static readonly GUIContent LOnlyTypes = new GUIContent("Только с типами", "Пусто = соединять по флагам выше.");
        private static readonly GUIContent LMeshSingle = new GUIContent("Одиночный");
        private static readonly GUIContent LMeshEnd = new GUIContent("Конец");
        private static readonly GUIContent LMeshStraight = new GUIContent("Прямая");
        private static readonly GUIContent LMeshCorner = new GUIContent("Угол");
        private static readonly GUIContent LMeshT = new GUIContent("Т-образная");
        private static readonly GUIContent LMeshCross = new GUIContent("Крест");

        private SerializedProperty _type;
        private SerializedProperty _displayName;
        private SerializedProperty _category;
        private SerializedProperty _sprite;
        private SerializedProperty _prefab;
        private SerializedProperty _topMap;
        private SerializedProperty _backingMap;
        private SerializedProperty _sideMap;
        private SerializedProperty _openSprite;
        private SerializedProperty _openPrefab;
        private SerializedProperty _blocksHorizontalSight;
        private SerializedProperty _sealsHorizontal;
        private SerializedProperty _blocksVerticalSight;
        private SerializedProperty _noVisibleFloor;

        // Вложенный WallConnectionData: флаги — всегда внутри блока, 6 мешей — под UseConnections.
        private SerializedProperty _connection;
        private SerializedProperty _useConnections;
        private SerializedProperty _connectsToSameType;
        private SerializedProperty _connectsToOtherWalls;
        private SerializedProperty _connectsToWindows;
        private SerializedProperty _connectsToDoorsHatches;
        private SerializedProperty _connectOnlyToTypes;
        private SerializedProperty _meshSingle;
        private SerializedProperty _meshEnd;
        private SerializedProperty _meshStraight;
        private SerializedProperty _meshCorner;
        private SerializedProperty _meshT;
        private SerializedProperty _meshCross;

        // Кэш авто-ID/коллизии: скан AssetDatabase дорог — считаем только при смене _type.intValue.
        private int _idScannedFor = int.MinValue;
        private StructureDefinition _idConflict;
        private bool _noFreeIds;

        private void OnEnable()
        {
            _type = serializedObject.FindProperty("Type");
            _displayName = serializedObject.FindProperty("DisplayName");
            _category = serializedObject.FindProperty("Category");
            _sprite = serializedObject.FindProperty("Sprite");
            _prefab = serializedObject.FindProperty("Prefab");
            _topMap = serializedObject.FindProperty("TopMap");
            _backingMap = serializedObject.FindProperty("BackingMap");
            _sideMap = serializedObject.FindProperty("SideMap");
            _openSprite = serializedObject.FindProperty("OpenSprite");
            _openPrefab = serializedObject.FindProperty("OpenPrefab");
            _blocksHorizontalSight = serializedObject.FindProperty("BlocksHorizontalSight");
            _sealsHorizontal = serializedObject.FindProperty("SealsHorizontal");
            _blocksVerticalSight = serializedObject.FindProperty("_blocksVerticalSight");
            _noVisibleFloor = serializedObject.FindProperty("_noVisibleFloor");

            // Connection — [Serializable]-класс, всегда инстанцирован (= new WallConnectionData()),
            // поэтому свойство и его relative-пути присутствуют структурно. Кэшируем по разу.
            _connection = serializedObject.FindProperty("Connection");
            if (_connection != null)
            {
                _useConnections = _connection.FindPropertyRelative("UseConnections");
                _connectsToSameType = _connection.FindPropertyRelative("ConnectsToSameType");
                _connectsToOtherWalls = _connection.FindPropertyRelative("ConnectsToOtherWalls");
                _connectsToWindows = _connection.FindPropertyRelative("ConnectsToWindows");
                _connectsToDoorsHatches = _connection.FindPropertyRelative("ConnectsToDoorsHatches");
                _connectOnlyToTypes = _connection.FindPropertyRelative("ConnectOnlyToTypes");
                _meshSingle = _connection.FindPropertyRelative("MeshSingle");
                _meshEnd = _connection.FindPropertyRelative("MeshEnd");
                _meshStraight = _connection.FindPropertyRelative("MeshStraight");
                _meshCorner = _connection.FindPropertyRelative("MeshCorner");
                _meshT = _connection.FindPropertyRelative("MeshT");
                _meshCross = _connection.FindPropertyRelative("MeshCross");
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Авто-ID новой SO (write-once, Type==0) + скан коллизии; кэш по _type.intValue (без аллокаций на repaint).
            var self = target as StructureDefinition;
            if (_type.intValue != _idScannedFor)
            {
                if (_type.intValue == 0)
                {
                    byte free = TileIdEditorUtil.SmallestFreeStructureId(self);
                    if (free > 0) _type.intValue = free;   // присвоить наименьший свободный
                    _noFreeIds = free == 0;                // все 1..255 заняты
                    _idConflict = null;                    // свежий свободный id не конфликтует
                }
                else
                {
                    _idConflict = TileIdEditorUtil.FindStructureIdConflict(self);
                    _noFreeIds = false;
                }
                _idScannedFor = _type.intValue;
            }

            // Общие поля — релевантны любой категории.
            EditorGUILayout.PropertyField(_type, LType);
            if (_noFreeIds)
                EditorGUILayout.HelpBox("Нет свободных id (1..255 заняты). Освободите id у другого ассета.", MessageType.Error);
            else if (_idConflict != null)
                EditorGUILayout.HelpBox($"ID {_type.intValue} уже занят: '{_idConflict.DisplayName}'. Выберите свободный ID.", MessageType.Error);
            EditorGUILayout.PropertyField(_displayName, LDisplayName);
            EditorGUILayout.PropertyField(_category, LCategory);

            // enumValueIndex == значению (StructureCategory непрерывна 0..4): Wall=0,Door=1,Hatch=2,Window=3,Ladder=4.
            var category = (StructureCategory)_category.enumValueIndex;
            bool openable = StructureCategoryInfo.IsOpenable(category);   // единый источник (зеркалит рантайм)
            bool isWall = category == StructureCategory.Wall;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Визуал", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_sprite, LSprite);
            EditorGUILayout.PropertyField(_prefab, LPrefab);
            if (openable)
            {
                // Открытый вид — только у дверей/люков; для глухих (стена/окно) поля бессмысленны.
                EditorGUILayout.PropertyField(_openSprite, LOpenSprite);
                EditorGUILayout.PropertyField(_openPrefab, LOpenPrefab);
            }
            // Текстуры шейдера TileReader — релевантны ЛЮБОЙ структуре (не только стене): верх/подложка/боковина.
            // null-гард (поля — зона coder-main): при рассинхроне не падаем.
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Текстуры (TileReader)", EditorStyles.boldLabel);
            if (_topMap != null) EditorGUILayout.PropertyField(_topMap, LTopMap);
            if (_backingMap != null) EditorGUILayout.PropertyField(_backingMap, LBackingMap);
            if (_sideMap != null) EditorGUILayout.PropertyField(_sideMap, LSideMap);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Флаги симуляции", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_blocksHorizontalSight, LBlocksSight);
            EditorGUILayout.PropertyField(_sealsHorizontal, LSeals);
            // Скрытие пола под структурой — общий флаг для любой категории (стена/дверь/лестница/машина).
            if (_noVisibleFloor != null)
                EditorGUILayout.PropertyField(_noVisibleFloor, LNoVisibleFloor);
            // Вертикальный обзор (Z) — только у глухих (стена/окно/лестница); открываемым (дверь/люк) не нужен.
            // Здесь человек задаёт see-through на ladder-SO (верх лестницы = false → reveal-дыра).
            if (!openable && _blocksVerticalSight != null)
                EditorGUILayout.PropertyField(_blocksVerticalSight, LBlocksVSight);

            // Autotiling-соединения имеют смысл только для стен; у двери/люка/окна форму не считают.
            if (isWall && _connection != null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Соединение стен (autotiling)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Форма стены по 4 соседям (autotiling)",
                    EditorStyles.miniLabel);

                EditorGUILayout.PropertyField(_useConnections, LUseConnections);
                EditorGUILayout.PropertyField(_connectsToSameType, LSameType);
                EditorGUILayout.PropertyField(_connectsToOtherWalls, LOtherWalls);
                EditorGUILayout.PropertyField(_connectsToWindows, LWindows);
                EditorGUILayout.PropertyField(_connectsToDoorsHatches, LDoorsHatches);
                EditorGUILayout.PropertyField(_connectOnlyToTypes, LOnlyTypes, true);

                // 6 базовых мешей нужны только когда autotiling включён.
                if (_useConnections.boolValue)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.PropertyField(_meshSingle, LMeshSingle);
                        EditorGUILayout.PropertyField(_meshEnd, LMeshEnd);
                        EditorGUILayout.PropertyField(_meshStraight, LMeshStraight);
                        EditorGUILayout.PropertyField(_meshCorner, LMeshCorner);
                        EditorGUILayout.PropertyField(_meshT, LMeshT);
                        EditorGUILayout.PropertyField(_meshCross, LMeshCross);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
