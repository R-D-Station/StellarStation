#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Client.Map;

namespace Client.Editor.Inspectors
{
    [CustomEditor(typeof(BlockColorReaction))]
    [CanEditMultipleObjects]
    public sealed class BlockColorReactionEditor : UnityEditor.Editor
    {
        private static readonly GUIContent LTargets = new GUIContent("Рендереры",
            "Рендереры, которым меняем цвет. Пусто — берётся Renderer этого объекта.");
        private static readonly GUIContent LColorProperty = new GUIContent("Свойство",
            "Имя цветового свойства шейдера. ⚠ При неверном имени цвет молча не изменится, ошибки в консоли НЕ будет.");
        private static readonly GUIContent LFrom = new GUIContent("Цвет покоя",
            "Цвет в покое (реакция не активна).");
        private static readonly GUIContent LTo = new GUIContent("Цвет активный",
            "Цвет в активном состоянии. Для «спрятать» — альфа 0 (нужен ПРОЗРАЧНЫЙ материал, у непрозрачного Lit эффекта не будет).");
        private static readonly GUIContent LMode = new GUIContent("Режим",
            "Как идёт переход: равномерно / с ускорением / по кривой.");
        private static readonly GUIContent LSpeed = new GUIContent("Скорость",
            "Скорость перехода (доля прогресса в секунду).");
        private static readonly GUIContent LAcceleration = new GUIContent("Ускорение",
            "Ускорение перехода (для режима «с ускорением»).");
        private static readonly GUIContent LCurve = new GUIContent("Кривая",
            "Форма перехода для режима «по кривой»: вход 0..1 — прогресс, выход 0..1 — доля цвета.");

        private SerializedProperty _targets;
        private SerializedProperty _colorProperty;
        private SerializedProperty _fromColor;
        private SerializedProperty _toColor;
        private SerializedProperty _mode;
        private SerializedProperty _speed;
        private SerializedProperty _acceleration;
        private SerializedProperty _curve;

        private void OnEnable()
        {
            _targets = serializedObject.FindProperty("_targets");
            _colorProperty = serializedObject.FindProperty("_colorProperty");
            _fromColor = serializedObject.FindProperty("_fromColor");
            _toColor = serializedObject.FindProperty("_toColor");
            _mode = serializedObject.FindProperty("_mode");
            _speed = serializedObject.FindProperty("_speed");
            _acceleration = serializedObject.FindProperty("_acceleration");
            _curve = serializedObject.FindProperty("_curve");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Draw(_targets, LTargets, true);
            Draw(_colorProperty, LColorProperty);
            EditorGUILayout.HelpBox("URP Lit — _BaseColor (пусто = оно же). Неверное имя: цвет молча не изменится.",
                MessageType.Info);

            Draw(_fromColor, LFrom);
            Draw(_toColor, LTo);

            EditorGUILayout.Space(4);
            Draw(_mode, LMode);

            bool mixed = _mode == null || _mode.hasMultipleDifferentValues;
            var mode = _mode != null ? (BlockColorReaction.Mode)_mode.enumValueIndex : BlockColorReaction.Mode.Speed;

            if (mixed)
            {
                Draw(_speed, LSpeed);
                Draw(_acceleration, LAcceleration);
                Draw(_curve, LCurve);
            }
            else
            {
                if (mode == BlockColorReaction.Mode.Accelerated)
                    Draw(_acceleration, LAcceleration);
                else
                    Draw(_speed, LSpeed);
                if (mode == BlockColorReaction.Mode.Curve)
                    Draw(_curve, LCurve);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static void Draw(SerializedProperty p, GUIContent label, bool children = false)
        {
            if (p != null)
                EditorGUILayout.PropertyField(p, label, children);
        }
    }
}
#endif
