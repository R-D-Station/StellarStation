#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Client.Map;
using Shared.World.Blocks;

namespace Client.Editor.Inspectors
{
    /// <summary>
    /// Инспектор <see cref="BlockDefinition"/>: авто-ID (write-once, ushort через BlockIdAllocator),
    /// контроль коллизии id, пресеты боксов и кнопка кодогена каталога.
    /// </summary>
    [CustomEditor(typeof(BlockDefinition))]
    public sealed class BlockDefinitionEditor : UnityEditor.Editor
    {
        private static readonly GUIContent LType = new GUIContent("ID", "BlockType в картах v10. Write-once: не перенумеровывать.");
        private static readonly GUIContent LDisplayName = new GUIContent("Название");
        private static readonly GUIContent LCategory = new GUIContent("Категория", "Door/Hatch — открываемые; Marker — невидимый триггер-блок.");
        private static readonly GUIContent LSeals = new GUIContent("Герметичность", "Герметичные грани (атмос). ZPos = верх, ZNeg = низ.");
        private static readonly GUIContent LOpaque = new GUIContent("Непрозрачность", "Непрозрачные грани (обзор/потолок-детект).");
        private static readonly GUIContent LBoxes = new GUIContent("Боксы", "AABB [0..1], оси сим-мира (Z = высота). Пусто = без коллизии.");
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
        private static readonly GUIContent LFaceSide = new GUIContent("Бок", "Спрайт боковых граней мешей → _SideTex (TileFaceSprites, кормится per-инстанс). Пусто → белый.");
        private static readonly GUIContent LFaceTop = new GUIContent("Верх граней", "Спрайт верхней грани мешей → _TopTex. Пусто → белый.");

        private SerializedProperty _type;
        private SerializedProperty _displayName;
        private SerializedProperty _category;
        private SerializedProperty _sealsFaces;
        private SerializedProperty _opaqueFaces;
        private SerializedProperty _collisionBoxes;
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
        private SerializedProperty _faceSideTex;
        private SerializedProperty _faceTopTex;
        private SerializedProperty _connection;

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
            _collisionBoxes = serializedObject.FindProperty("CollisionBoxes");
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
            _faceSideTex = serializedObject.FindProperty("FaceSideTex");
            _faceTopTex = serializedObject.FindProperty("FaceTopTex");
            _connection = serializedObject.FindProperty("Connection");
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

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Грани", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_sealsFaces, LSeals);
            EditorGUILayout.PropertyField(_opaqueFaces, LOpaque);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Коллизия", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_collisionBoxes, LBoxes, true);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Full")) SetBoxes(self, BlockDefinition.CollisionBox.Full);
                if (GUILayout.Button("Слаб 0.25")) SetBoxes(self, BlockDefinition.CollisionBox.SlabTop);
                if (GUILayout.Button("Empty")) SetBoxes(self);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Деконструкция", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_deconstructStages, LDecon);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Верх (TileReader)", EditorStyles.boldLabel);
            if (_topMap != null) EditorGUILayout.PropertyField(_topMap, LTopMap);
            if (_backingMap != null) EditorGUILayout.PropertyField(_backingMap, LBackingMap);
            if (_topCountX != null) EditorGUILayout.PropertyField(_topCountX, LTopCountX);
            if (_topCountY != null) EditorGUILayout.PropertyField(_topCountY, LTopCountY);
            if (_topCalibration != null) EditorGUILayout.PropertyField(_topCalibration, LTopCalibration);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Грани мешей", EditorStyles.boldLabel);
            if (_faceSideTex != null) EditorGUILayout.PropertyField(_faceSideTex, LFaceSide);
            if (_faceTopTex != null) EditorGUILayout.PropertyField(_faceTopTex, LFaceTop);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Визуал", EditorStyles.boldLabel);
            if (_prefab != null) EditorGUILayout.PropertyField(_prefab, LPrefab);
            if (_pivot != null) EditorGUILayout.PropertyField(_pivot, LPivot);
            EditorGUILayout.PropertyField(_editorSprite, LSprite);

            if (_connection != null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Соединения (autotiling)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("База мешей — соединением на север (+Z); поворот 90° по часовой.",
                    EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_connection, new GUIContent("Автотайл"), true);
            }

            EditorGUILayout.Space(12);
            if (GUILayout.Button("Сгенерировать каталог (Shared)"))
                BlockCatalogCodegen.Generate();

            serializedObject.ApplyModifiedProperties();
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
    }
}
#endif
