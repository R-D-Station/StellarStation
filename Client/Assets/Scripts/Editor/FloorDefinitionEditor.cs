#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Client.Map;

namespace Client.Editor.Inspectors
{
    /// <summary>
    /// Кастомный инспектор <see cref="FloorDefinition"/>. Зеркало <see cref="StructureDefinitionEditor"/>,
    /// но проще: у пола нет категории, блок autotiling-соединений показывается всегда, а 6 мешей —
    /// только когда соединения включены. Класс чисто редакторный: рантайм-данные не меняются,
    /// поля берутся через SerializedProperty по их именам.
    /// </summary>
    [CustomEditor(typeof(FloorDefinition))]
    public sealed class FloorDefinitionEditor : UnityEditor.Editor
    {
        // Короткие человеческие подписи. Кешируются один раз (а не new GUIContent на repaint —
        // правило CLAUDE «без аллокаций в OnGUI»); тултипы перенесены из [Tooltip] исходных полей.
        private static readonly GUIContent LType = new GUIContent("ID", "Значение Tile.FloorType. 0 = пола нет.");
        private static readonly GUIContent LDisplayName = new GUIContent("Название");
        private static readonly GUIContent LSprite = new GUIContent("Спрайт", "Спрайт клетки редактора; при пустом префабе рисуется и в игре.");
        private static readonly GUIContent LPrefab = new GUIContent("Префаб", "Инстансится в игре. Пусто → fallback на Sprite.");
        private static readonly GUIContent LSideSprite = new GUIContent("Спрайт боков", "Боковые грани меша пола. MapRenderer кладёт в _SideTex материала.");
        private static readonly GUIContent LTopSprite = new GUIContent("Спрайт верха", "Верх при выключенном autotiling. При UseConnections верх берётся по форме.");
        private static readonly GUIContent LTopMap = new GUIContent("Грид верха (TileView)", "Грид-текстура этого типа пола. MapRenderer кладёт в _TopMap материала TileView; форму выбирает _i.");
        private static readonly GUIContent LBlocksSight = new GUIContent("Держит обзор ↓", "Сплошной пол не просвечивает на этаж ниже (FOV). Решётка/стекло = false.");
        private static readonly GUIContent LSeals = new GUIContent("Герметичен ↓", "Не пропускает газ вниз. Решётка = false.");
        private static readonly GUIContent LUseConnections = new GUIContent("Автотайлинг", "Выбирать меш по 4 соседям.");
        private static readonly GUIContent LSameType = new GUIContent("С тем же типом");
        private static readonly GUIContent LOtherFloors = new GUIContent("С другими полами");
        private static readonly GUIContent LOnlyTypes = new GUIContent("Только с типами", "Пусто = соединять по флагу выше.");
        private static readonly GUIContent LMeshSingle = new GUIContent("Одиночный");
        private static readonly GUIContent LMeshEnd = new GUIContent("Конец");
        private static readonly GUIContent LMeshStraight = new GUIContent("Прямая");
        private static readonly GUIContent LMeshCorner = new GUIContent("Угол");
        private static readonly GUIContent LMeshT = new GUIContent("Т-образная");
        private static readonly GUIContent LMeshCross = new GUIContent("Крест");

        private SerializedProperty _type;
        private SerializedProperty _displayName;
        private SerializedProperty _sprite;
        private SerializedProperty _prefab;
        private SerializedProperty _sideSprite;
        private SerializedProperty _topSprite;
        private SerializedProperty _topMap;
        private SerializedProperty _blocksVerticalSight;
        private SerializedProperty _sealsVertical;

        // Вложенный FloorConnectionData: флаги — всегда, 6 мешей — под UseConnections.
        private SerializedProperty _connection;
        private SerializedProperty _useConnections;
        private SerializedProperty _connectsToSameType;
        private SerializedProperty _connectsToOtherFloors;
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
            _sprite = serializedObject.FindProperty("Sprite");
            _prefab = serializedObject.FindProperty("Prefab");
            _sideSprite = serializedObject.FindProperty("SideSprite");
            _topSprite = serializedObject.FindProperty("TopSprite");
            _topMap = serializedObject.FindProperty("TopMap");
            _blocksVerticalSight = serializedObject.FindProperty("BlocksVerticalSight");
            _sealsVertical = serializedObject.FindProperty("SealsVertical");

            // Connection — [Serializable]-класс, всегда инстанцирован (= new FloorConnectionData()),
            // поэтому свойство и его relative-пути присутствуют структурно. Кэшируем по разу.
            _connection = serializedObject.FindProperty("Connection");
            if (_connection != null)
            {
                _useConnections = _connection.FindPropertyRelative("UseConnections");
                _connectsToSameType = _connection.FindPropertyRelative("ConnectsToSameType");
                _connectsToOtherFloors = _connection.FindPropertyRelative("ConnectsToOtherFloors");
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

            EditorGUILayout.PropertyField(_type, LType);
            EditorGUILayout.PropertyField(_displayName, LDisplayName);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Визуал", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_sprite, LSprite);
            EditorGUILayout.PropertyField(_prefab, LPrefab);
            EditorGUILayout.PropertyField(_sideSprite, LSideSprite);
            EditorGUILayout.PropertyField(_topSprite, LTopSprite);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Флаги симуляции", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_blocksVerticalSight, LBlocksSight);
            EditorGUILayout.PropertyField(_sealsVertical, LSeals);

            // У пола нет категории — блок соединений релевантен всегда; меши гейтятся UseConnections.
            if (_connection != null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Верх пола (TileView)", EditorStyles.boldLabel);
                // null-гард: если поле TopMap ещё не в рантайме (зона AuxCoder) — не падаем (PropertyField(null) бросает).
                if (_topMap != null)
                    EditorGUILayout.PropertyField(_topMap, LTopMap);

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Соединение пола (autotiling)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Форма пола по 4 соседям — читается рендером. 6 мешей нужны при UseConnections.",
                    EditorStyles.miniLabel);

                EditorGUILayout.PropertyField(_useConnections, LUseConnections);
                EditorGUILayout.PropertyField(_connectsToSameType, LSameType);
                EditorGUILayout.PropertyField(_connectsToOtherFloors, LOtherFloors);
                EditorGUILayout.PropertyField(_connectOnlyToTypes, LOnlyTypes, true);

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
