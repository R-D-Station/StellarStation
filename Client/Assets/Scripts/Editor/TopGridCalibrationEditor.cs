#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Client.Map;

namespace Client.Editor.Inspectors
{
    /// <summary>Инспектор <see cref="TopGridCalibration"/>: таблица доп. поворотов (строки — форма, колонки — основной слой и типы углов).</summary>
    [CustomEditor(typeof(TopGridCalibration))]
    public sealed class TopGridCalibrationEditor : UnityEditor.Editor
    {
        private static readonly string[] Rows = { "Single", "End", "Straight", "Corner", "T", "Cross" };
        private static readonly GUIContent[] Cols =
        {
            new GUIContent("Осн.", "Основной слой: крутит весь квад, угловой слой компенсируется автоматически. У Cross основной слой не рисуется (подложка)."),
            new GUIContent("Угол", "_cur_corner=1: одиночный угол."),
            new GUIContent("Смеж", "_cur_corner=2: пара смежных углов."),
            new GUIContent("Диаг", "_cur_corner=3: пара по диагонали."),
            new GUIContent("Три", "_cur_corner=4: три угла."),
            new GUIContent("Четыре", "_cur_corner=5: все четыре угла.")
        };
        private static readonly GUIContent[] QuarterLabels =
            { new GUIContent("0°"), new GUIContent("90°"), new GUIContent("180°"), new GUIContent("270°") };
        private static readonly int[] QuarterValues = { 0, 1, 2, 3 };

        private const float RowW = 60f;
        private const float CellW = 46f;

        private SerializedProperty _main, _corner;

        private void OnEnable()
        {
            _main = serializedObject.FindProperty("MainQuarter");
            _corner = serializedObject.FindProperty("CornerQuarter");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (_main.arraySize != 6) _main.arraySize = 6;
            if (_corner.arraySize != 36) _corner.arraySize = 36;

            EditorGUILayout.LabelField("Доп. повороты (×90°)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Правки видны на карте сразу (Block Map Authoring).", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(GUIContent.none, GUILayout.Width(RowW));
                for (int c = 0; c < Cols.Length; c++)
                    GUILayout.Label(Cols[c], EditorStyles.miniBoldLabel, GUILayout.MinWidth(CellW));
            }
            for (int s = 0; s < Rows.Length; s++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Rows[s], GUILayout.Width(RowW));
                    DrawQuarter(_main.GetArrayElementAtIndex(s));
                    for (int cc = 1; cc <= 5; cc++)
                        DrawQuarter(_corner.GetArrayElementAtIndex(s * 6 + cc));
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawQuarter(SerializedProperty p)
            => p.intValue = EditorGUILayout.IntPopup(p.intValue & 3, QuarterLabels, QuarterValues, GUILayout.MinWidth(CellW));
    }
}
#endif
