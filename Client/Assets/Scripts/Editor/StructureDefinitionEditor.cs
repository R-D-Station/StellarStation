#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Client.Map;

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
        private static readonly GUIContent LSideSprite = new GUIContent("Спрайт боков", "Боковые грани меша. MapRenderer кладёт в _SideTex материала.");
        private static readonly GUIContent LTopSprite = new GUIContent("Спрайт верха", "Верх при выключенном autotiling. При UseConnections верх берётся по форме.");
        private static readonly GUIContent LTopMap = new GUIContent("Грид верха (TileView)", "Грид-текстура этого типа стены. MapRenderer кладёт в _TopMap материала TileView; форму выбирает _i.");
        private static readonly GUIContent LOpenSprite = new GUIContent("Спрайт (открыт)");
        private static readonly GUIContent LOpenPrefab = new GUIContent("Префаб (открыт)");
        private static readonly GUIContent LBlocksSight = new GUIContent("Держит обзор", "Держит обзор по горизонтали (в закрытом виде). Стекло/окно = false.");
        private static readonly GUIContent LSeals = new GUIContent("Герметична", "Не пропускает газ по горизонтали.");
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
        private SerializedProperty _sideSprite;
        private SerializedProperty _topSprite;
        private SerializedProperty _topMap;
        private SerializedProperty _openSprite;
        private SerializedProperty _openPrefab;
        private SerializedProperty _blocksHorizontalSight;
        private SerializedProperty _sealsHorizontal;

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

        private void OnEnable()
        {
            _type = serializedObject.FindProperty("Type");
            _displayName = serializedObject.FindProperty("DisplayName");
            _category = serializedObject.FindProperty("Category");
            _sprite = serializedObject.FindProperty("Sprite");
            _prefab = serializedObject.FindProperty("Prefab");
            _sideSprite = serializedObject.FindProperty("SideSprite");
            _topSprite = serializedObject.FindProperty("TopSprite");
            _topMap = serializedObject.FindProperty("TopMap");
            _openSprite = serializedObject.FindProperty("OpenSprite");
            _openPrefab = serializedObject.FindProperty("OpenPrefab");
            _blocksHorizontalSight = serializedObject.FindProperty("BlocksHorizontalSight");
            _sealsHorizontal = serializedObject.FindProperty("SealsHorizontal");

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

            // Общие поля — релевантны любой категории.
            EditorGUILayout.PropertyField(_type, LType);
            EditorGUILayout.PropertyField(_displayName, LDisplayName);
            EditorGUILayout.PropertyField(_category, LCategory);

            // enumValueIndex == значению (StructureCategory непрерывна от 0): Wall=0,Door=1,Hatch=2,Window=3.
            var category = (StructureCategory)_category.enumValueIndex;
            bool openable = category == StructureCategory.Door || category == StructureCategory.Hatch;
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
            EditorGUILayout.PropertyField(_sideSprite, LSideSprite);
            EditorGUILayout.PropertyField(_topSprite, LTopSprite);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Флаги симуляции", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_blocksHorizontalSight, LBlocksSight);
            EditorGUILayout.PropertyField(_sealsHorizontal, LSeals);

            // Autotiling-соединения имеют смысл только для стен; у двери/люка/окна форму не считают.
            if (isWall && _connection != null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Верх стены (TileView)", EditorStyles.boldLabel);
                // null-гард: если поле TopMap ещё не в рантайме (зона coder-main) — не падаем (PropertyField(null) бросает).
                if (_topMap != null)
                    EditorGUILayout.PropertyField(_topMap, LTopMap);

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
