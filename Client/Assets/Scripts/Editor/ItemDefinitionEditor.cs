#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Client.Items;
using Client.Config;

namespace Client.Editor.Inspectors
{
    /// <summary>Инспектор <see cref="ItemDefinition"/>: обычные поля + два СВЯЗАННЫХ поля размера (% ↔ юниты).
    /// Единый источник — сериализованный `_renderScale` (доля тайла): правишь одно поле — второе пересчитывается.</summary>
    [CustomEditor(typeof(ItemDefinition))]
    public sealed class ItemDefinitionEditor : UnityEditor.Editor
    {
        private static readonly GUIContent LPct = new GUIContent("Размер %", "Размер спрайта как % от тайла (100 = целый тайл).");
        private static readonly GUIContent LUnits = new GUIContent("Размер, юниты", "То же в мировых юнитах (= % · TileSize). Правишь одно — второе пересчитывается.");

        private SerializedProperty _renderScale;

        private void OnEnable()
        {
            // null-гард ([[inspector-so-field-coupling]]): `_renderScale` — зона MainCoder; поле может ещё
            // не появиться на диске → FindProperty вернёт null.
            _renderScale = serializedObject.FindProperty("_renderScale");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Обычные поля (id/имя/спрайт); размер рисуем связанной парой ниже, поэтому исключаем.
            DrawPropertiesExcluding(serializedObject, "m_Script", "_renderScale");

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Размер", EditorStyles.boldLabel);

            if (_renderScale == null)
            {
                EditorGUILayout.HelpBox("Поле '_renderScale' не найдено — ItemDefinition ещё не обновлён (зона MainCoder).",
                    MessageType.Info);
                serializedObject.ApplyModifiedProperties();   // сохранить правки обычных полей выше
                return;
            }

            float ts = RenderConfig.TileSize;

            // Оба поля ПИШУТ в один _renderScale → на следующем repaint пере-выводятся из него (без дрейфа float).
            EditorGUI.BeginChangeCheck();
            float pct = EditorGUILayout.FloatField(LPct, _renderScale.floatValue * 100f);
            if (EditorGUI.EndChangeCheck())
                _renderScale.floatValue = Mathf.Max(0f, pct / 100f);

            EditorGUI.BeginChangeCheck();
            float units = EditorGUILayout.FloatField(LUnits, _renderScale.floatValue * ts);
            if (EditorGUI.EndChangeCheck())
                _renderScale.floatValue = Mathf.Max(0f, ts > 0f ? units / ts : _renderScale.floatValue);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
