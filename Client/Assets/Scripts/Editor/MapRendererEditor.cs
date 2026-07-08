#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Client.Net.View;

namespace Client.Editor.Inspectors
{
    /// <summary>
    /// Кастомный инспектор <see cref="MapRenderer"/>. Дефолтный инспектор валит все
    /// [SerializeField] плоской кучей, где вперемешку лежат вещи разного назначения.
    /// Здесь они разложены по смыслу:
    ///   1) Данные      — каталог тайлов (единственная обязательная зависимость);
    ///   2) Этаж (Z)    — рантайм-состояние (в игре ведёт NetworkRunner);
    ///   3) Просвечивание— материал/тумблеры видимости соседних этажей;
    ///   4) Тюнинг      — мелкие сдвиги/сортировки рендера, выставляются один раз → свёрнуто;
    ///   5) Отладка     — загрузка .smap без сервера → свёрнуто.
    /// Класс чисто редакторный: рантайм-MapRenderer не меняется, поля берутся через
    /// SerializedProperty по их именам.
    /// </summary>
    [CustomEditor(typeof(MapRenderer))]
    public sealed class MapRendererEditor : UnityEditor.Editor
    {
        // Все подписи — static readonly (без аллокаций на repaint); длинные пояснения в tooltip, не в подписи.
        private static readonly GUIContent LRevealBase = new GUIContent("Радиус (далеко)", "Базовый радиус кольца просвета вдали от проёма.");
        private static readonly GUIContent LRevealMax = new GUIContent("Радиус (макс)", "Максимальный радиус кольца вплотную к проёму.");
        private static readonly GUIContent LRevealProximity = new GUIContent("Дистанция роста", "Дистанция до проёма, на которой радиус растёт от базового к максимуму.");
        private static readonly GUIContent LWallRevealAlpha = new GUIContent("Альфа стен", "Единая альфа стен reveal-уровня (без cheb/глубины), 0..1.");
        private static readonly GUIContent LRevealMaxFloors = new GUIContent("Глубина этажей", "Макс число этажей просвета вверх/вниз.");
        private static readonly GUIContent LRevealDepthDim = new GUIContent("Затемнение/этаж", "Множитель яркости на этаж глубже, 0..1.");
        private static readonly GUIContent LCatalog = new GUIContent("Каталог", "Каталог тайлов (id → префаб/спрайт). Без него рисовать нечем.");
        private static readonly GUIContent LStartZ = new GUIContent("Стартовый этаж", "Активный Z-этаж на старте (в игре ведёт NetworkRunner).");
        private static readonly GUIContent LFadeMat = new GUIContent("Прозр. материал", "URP/Lit Surface=Transparent, ZWrite Off — для колец верхнего этажа.");
        private static readonly GUIContent LDrawReveal = new GUIContent("Этаж выше", "Показывать этаж выше кольцами вокруг проёмов в потолке.");
        private static readonly GUIContent LXray = new GUIContent("Рентген", "Показывать весь верхний этаж полупрозрачным, не только кольца у проёмов.");
        private static readonly GUIContent LXrayAlpha = new GUIContent("Альфа рентгена", "Непрозрачность верхнего этажа в режиме рентгена (0..1).");
        private static readonly GUIContent LFadeRings = new GUIContent("Кольца", "Градиент непрозрачности кольца по абсолютному индексу (cheb + floorStep·depthDim).");
        private static readonly GUIContent LFloorYOffset = new GUIContent("Сдвиг пола Y", "Доп. сдвиг пола по Y над уровнем этажа (обычно 0).");
        private static readonly GUIContent LWallYOffset = new GUIContent("Сдвиг стены Y", "Сдвиг стены по Y над полом — против z-fighting плоских квадов.");
        private static readonly GUIContent LFloorSort = new GUIContent("Sorting пола");
        private static readonly GUIContent LWallSort = new GUIContent("Sorting стены");
        private static readonly GUIContent LSeam = new GUIContent("Анти-шов", "Лёгкое расширение тайлов пола против шва на стыках (1.0 = выкл).");
        private static readonly GUIContent LTestMap = new GUIContent("Тест .smap", "Относительный путь к .smap для загрузки при старте. Пусто = не грузить.");

        private SerializedProperty _catalog;
        private SerializedProperty _activeZ;
        private SerializedProperty _floorYOffset;
        private SerializedProperty _wallYOffset;
        private SerializedProperty _floorSortingOrder;
        private SerializedProperty _wallSortingOrder;
        private SerializedProperty _floorSeamScale;
        private SerializedProperty _floorFadeMaterial;
        private SerializedProperty _drawCeilingReveal;
        private SerializedProperty _ceilingSemiTransparent;
        private SerializedProperty _ceilingXrayAlpha;
        private SerializedProperty _fadeRingOpacity;
        private SerializedProperty _revealBaseRadius;
        private SerializedProperty _revealMaxRadius;
        private SerializedProperty _revealProximityDistance;
        private SerializedProperty _wallRevealAlpha;
        private SerializedProperty _revealMaxFloors;
        private SerializedProperty _revealDepthDim;
        private SerializedProperty _testMapPath;

        // Состояние сворачивания живёт между выборами объекта.
        private const string TuningPrefKey = "Station.MapRenderer.ShowTuning";
        private const string DebugPrefKey = "Station.MapRenderer.ShowDebug";
        private bool _showTuning;
        private bool _showDebug;

        private void OnEnable()
        {
            _catalog = serializedObject.FindProperty("_catalog");
            _activeZ = serializedObject.FindProperty("_activeZ");
            _floorYOffset = serializedObject.FindProperty("_floorYOffset");
            _wallYOffset = serializedObject.FindProperty("_wallYOffset");
            _floorSortingOrder = serializedObject.FindProperty("_floorSortingOrder");
            _wallSortingOrder = serializedObject.FindProperty("_wallSortingOrder");
            _floorSeamScale = serializedObject.FindProperty("_floorSeamScale");
            _floorFadeMaterial = serializedObject.FindProperty("_floorFadeMaterial");
            _drawCeilingReveal = serializedObject.FindProperty("_drawCeilingReveal");
            _ceilingSemiTransparent = serializedObject.FindProperty("_ceilingSemiTransparent");
            _ceilingXrayAlpha = serializedObject.FindProperty("_ceilingXrayAlpha");
            _fadeRingOpacity = serializedObject.FindProperty("_fadeRingOpacity");
            _revealBaseRadius = serializedObject.FindProperty("_revealBaseRadius");
            _revealMaxRadius = serializedObject.FindProperty("_revealMaxRadius");
            _revealProximityDistance = serializedObject.FindProperty("_revealProximityDistance");
            _wallRevealAlpha = serializedObject.FindProperty("_wallRevealAlpha");
            _revealMaxFloors = serializedObject.FindProperty("_revealMaxFloors");
            _revealDepthDim = serializedObject.FindProperty("_revealDepthDim");
            _testMapPath = serializedObject.FindProperty("_testMapPath");

            _showTuning = EditorPrefs.GetBool(TuningPrefKey, false);
            _showDebug = EditorPrefs.GetBool(DebugPrefKey, false);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 1. Данные — без каталога рисовать нечем.
            EditorGUILayout.LabelField("Данные", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_catalog, LCatalog);
            if (_catalog.objectReferenceValue == null)
                EditorGUILayout.HelpBox(
                    "Каталог не задан — ApplyChunk нечем рисовать (в рантайме будет ошибка).",
                    MessageType.Error);

            EditorGUILayout.Space(8);

            // 2. Этаж (Z) — рантайм-состояние. В игре его ведёт NetworkRunner.SetActiveZ.
            EditorGUILayout.LabelField("Этаж (Z)", EditorStyles.boldLabel);
            if (Application.isPlaying)
            {
                var mr = (MapRenderer)target;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"Текущий: {_activeZ.intValue}", GUILayout.Width(110));
                    if (GUILayout.Button("−", GUILayout.Width(28))) mr.SetActiveZ(_activeZ.intValue - 1);
                    if (GUILayout.Button("+", GUILayout.Width(28))) mr.SetActiveZ(_activeZ.intValue + 1);
                }
                EditorGUILayout.LabelField(
                    "Ведёт NetworkRunner; кнопки — превью соседних этажей.",
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.PropertyField(_activeZ, LStartZ);
            }

            EditorGUILayout.Space(8);

            // 3. Просвечивание этажей — фича 2.5D-видимости: вниз сквозь дыры/решётки,
            //    вверх сквозь проёмы в потолке; пол вокруг проёма мягко гаснет.
            EditorGUILayout.LabelField("Просвечивание этажей", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Низ виден сквозь дыры обычной отрисовкой. Здесь — только верхний этаж.",
                EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_drawCeilingReveal, LDrawReveal);
            // Под-параметры просвета СКРЫВАЕМ (не грейним), когда просвет выключен.
            if (_drawCeilingReveal.boolValue)
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_ceilingSemiTransparent, LXray);
                    if (_ceilingSemiTransparent.boolValue)
                        EditorGUILayout.PropertyField(_ceilingXrayAlpha, LXrayAlpha);
                    EditorGUILayout.PropertyField(_fadeRingOpacity, LFadeRings, true);

                    EditorGUILayout.LabelField("Динамический просвет (R1)", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(_revealBaseRadius, LRevealBase);
                    EditorGUILayout.PropertyField(_revealMaxRadius, LRevealMax);
                    EditorGUILayout.PropertyField(_revealProximityDistance, LRevealProximity);
                    EditorGUILayout.PropertyField(_wallRevealAlpha, LWallRevealAlpha);
                    EditorGUILayout.PropertyField(_revealMaxFloors, LRevealMaxFloors);
                    EditorGUILayout.PropertyField(_revealDepthDim, LRevealDepthDim);
                }

            EditorGUILayout.Space(8);

            // 4. Тюнинг рендера — задаётся один раз против z-fighting/шва, обычно не трогается.
            bool tuning = EditorGUILayout.Foldout(_showTuning, "Тюнинг рендера (2.5D)", true);
            if (tuning != _showTuning)
            {
                _showTuning = tuning;
                EditorPrefs.SetBool(TuningPrefKey, _showTuning);
            }
            if (_showTuning)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_floorYOffset, LFloorYOffset);
                    EditorGUILayout.PropertyField(_wallYOffset, LWallYOffset);
                    EditorGUILayout.PropertyField(_floorSortingOrder, LFloorSort);
                    EditorGUILayout.PropertyField(_wallSortingOrder, LWallSort);
                    EditorGUILayout.PropertyField(_floorSeamScale, LSeam);
                }
            }

            EditorGUILayout.Space(2);

            // 5. Отладка — грузить .smap локально, без сервера.
            bool dbg = EditorGUILayout.Foldout(_showDebug, "Отладка", true);
            if (dbg != _showDebug)
            {
                _showDebug = dbg;
                EditorPrefs.SetBool(DebugPrefKey, _showDebug);
            }
            if (_showDebug)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_testMapPath, LTestMap);
                    using (new EditorGUI.DisabledScope(
                        !Application.isPlaying || string.IsNullOrEmpty(_testMapPath.stringValue)))
                    {
                        if (GUILayout.Button("Загрузить сейчас"))
                            ((MapRenderer)target).LoadLocal(_testMapPath.stringValue);
                    }
                    if (!Application.isPlaying)
                        EditorGUILayout.HelpBox(
                            "Кнопка грузит в Play Mode (инстансит префабы). Вне игры путь подхватится в Start().",
                            MessageType.Info);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
