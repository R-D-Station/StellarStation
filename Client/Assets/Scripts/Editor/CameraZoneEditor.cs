#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Client.Map;

namespace Client.Editor.Inspectors
{
    [CustomEditor(typeof(CameraZone))]
    [CanEditMultipleObjects]
    public sealed class CameraZoneEditor : UnityEditor.Editor
    {
        private static readonly GUIContent LBoxCenter = new GUIContent("Центр бокса",
            "Центр бокса в ЛОКАЛЬНЫХ координатах префаба (поворот блока учитывается сам).");
        private static readonly GUIContent LBoxSize = new GUIContent("Размер бокса",
            "Размер бокса в локальных координатах.");
        private static readonly GUIContent LOverrideOffset = new GUIContent("Менять смещение",
            "Переопределять смещение камеры внутри зоны.");
        private static readonly GUIContent LOffset = new GUIContent("Смещение",
            "Смещение камеры при повороте 0 (крутится клавишей поворота вместе с базовым).");
        private static readonly GUIContent LOverridePitch = new GUIContent("Менять наклон",
            "Переопределять наклон камеры внутри зоны.");
        private static readonly GUIContent LPitch = new GUIContent("Наклон",
            "Наклон (тангаж) в градусах.");
        private static readonly GUIContent LPriority = new GUIContent("Приоритет",
            "Больший приоритет побеждает при перекрытии зон.");
        private static readonly GUIContent LBlendSpeed = new GUIContent("Скорость схода",
            "Скорость схождения к параметрам зоны.");
        private static readonly GUIContent LExitMargin = new GUIContent("Запас выхода",
            "Зона держится, пока игрок не отойдёт дальше (антидребезг на границе).");

        private SerializedProperty _boxCenter;
        private SerializedProperty _boxSize;
        private SerializedProperty _overrideOffset;
        private SerializedProperty _offset;
        private SerializedProperty _overridePitch;
        private SerializedProperty _pitch;
        private SerializedProperty _priority;
        private SerializedProperty _blendSpeed;
        private SerializedProperty _exitMargin;

        private void OnEnable()
        {
            _boxCenter = serializedObject.FindProperty("_boxCenter");
            _boxSize = serializedObject.FindProperty("_boxSize");
            _overrideOffset = serializedObject.FindProperty("_overrideOffset");
            _offset = serializedObject.FindProperty("_offset");
            _overridePitch = serializedObject.FindProperty("_overridePitch");
            _pitch = serializedObject.FindProperty("_pitch");
            _priority = serializedObject.FindProperty("_priority");
            _blendSpeed = serializedObject.FindProperty("_blendSpeed");
            _exitMargin = serializedObject.FindProperty("_exitMargin");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Draw(_boxCenter, LBoxCenter);
            Draw(_boxSize, LBoxSize);

            EditorGUILayout.Space(4);
            Draw(_overrideOffset, LOverrideOffset);
            if (Enabled(_overrideOffset))
                Draw(_offset, LOffset);

            Draw(_overridePitch, LOverridePitch);
            if (Enabled(_overridePitch))
                Draw(_pitch, LPitch);

            if (!Enabled(_overrideOffset) && !Enabled(_overridePitch))
                EditorGUILayout.HelpBox("Оба переопределения выключены — зона ничего не меняет.", MessageType.Warning);

            EditorGUILayout.Space(4);
            Draw(_priority, LPriority);
            Draw(_blendSpeed, LBlendSpeed);
            Draw(_exitMargin, LExitMargin);

            serializedObject.ApplyModifiedProperties();
        }

        private static bool Enabled(SerializedProperty p)
            => p == null || p.hasMultipleDifferentValues || p.boolValue;

        private static void Draw(SerializedProperty p, GUIContent label)
        {
            if (p != null)
                EditorGUILayout.PropertyField(p, label);
        }
    }
}
#endif
