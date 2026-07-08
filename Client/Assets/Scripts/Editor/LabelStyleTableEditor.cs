#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Client.UI.Labels;

namespace Client.Editor.Inspectors
{
    /// <summary>Компактный инспектор <see cref="LabelStyleTable"/>: заголовок Kind и поля на каждый Entry, условный показ по «Вечная».</summary>
    [CustomEditor(typeof(LabelStyleTable))]
    public sealed class LabelStyleTableEditor : UnityEditor.Editor
    {
        // Короткие подписи (кэш static readonly — без аллокаций на repaint); детали в tooltip.
        private static readonly GUIContent LKind = new GUIContent("Вид", "Вид надписи — ключ поиска в LabelStyleTable.For().");
        private static readonly GUIContent LEternal = new GUIContent("Вечная", "Lifetime = ∞: не истекает по таймеру, закрывается вручную. Скрывает Жизнь/Появл./Исчез.");
        private static readonly GUIContent LLifetime = new GUIContent("Жизнь", "Время жизни надписи, сек (для конечных видов).");
        private static readonly GUIContent LFadeIn = new GUIContent("Появл.", "Длительность появления (fade-in), сек.");
        private static readonly GUIContent LFadeOut = new GUIContent("Исчез.", "Длительность исчезновения (fade-out), сек.");
        private static readonly GUIContent LFontSize = new GUIContent("Кегль", "Размер шрифта TMP.");
        private static readonly GUIContent LTextColor = new GUIContent("Цвет", "Цвет текста.");
        private static readonly GUIContent LBackground = new GUIContent("Фон", "Цвет подложки-фона.");
        private static readonly GUIContent LOffset = new GUIContent("Смещ.", "Смещение надписи, экранные пиксели.");

        private const float FiniteLifetimeDefault = 3f;   // при снятии «Вечная» — осмысленный конечный дефолт

        private SerializedProperty _entries;

        private void OnEnable()
        {
            _entries = serializedObject.FindProperty("_entries"); // null-гард ниже — см. [[inspector-so-field-coupling]]
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_entries == null)
            {
                EditorGUILayout.HelpBox("Поле '_entries' не найдено — LabelStyleTable ещё не собран (зона MainCoder).",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Стили надписей", EditorStyles.boldLabel);

            int removeAt = -1;
            for (int i = 0; i < _entries.arraySize; i++)
                if (DrawEntry(_entries.GetArrayElementAtIndex(i), i))
                    removeAt = i;
            if (removeAt >= 0)
                _entries.DeleteArrayElementAtIndex(removeAt);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Вид", GUILayout.Width(70)))
                {
                    int added = _entries.arraySize;
                    _entries.arraySize++;        // Unity клонирует последний Entry → назначаем свободный вид
                    AssignUnusedKind(added);
                }
                GUILayout.FlexibleSpace();
            }

            serializedObject.ApplyModifiedProperties();
        }

        // Возвращает true, если нажата «✕» (удалить этот Entry). Каждое поле под null-гардом (имена — зона MainCoder).
        private bool DrawEntry(SerializedProperty entry, int index)
        {
            var kind = entry.FindPropertyRelative("Kind");
            var lifetime = entry.FindPropertyRelative("Lifetime");
            var fadeIn = entry.FindPropertyRelative("FadeIn");
            var fadeOut = entry.FindPropertyRelative("FadeOut");
            var fontSize = entry.FindPropertyRelative("FontSize");
            var textColor = entry.FindPropertyRelative("TextColor");
            var background = entry.FindPropertyRelative("Background");
            var offset = entry.FindPropertyRelative("Offset");

            bool remove;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(EntryTitle(kind, index), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    remove = GUILayout.Button("✕", GUILayout.Width(22));
                }

                if (kind != null) EditorGUILayout.PropertyField(kind, LKind);
                // For() — поиск по Kind: два Entry с одним видом → второй осиротеет (For вернёт первый).
                if (kind != null && IsKindDuplicatedBefore(kind.enumValueIndex, index))
                    EditorGUILayout.HelpBox("Дублирует вид выше — For() вернёт первую запись.", MessageType.Warning);

                if (lifetime != null)
                {
                    // Строго PositiveInfinity — симметрично записи ниже и рантайм-сверке в PooledLabel.
                    bool eternal = float.IsPositiveInfinity(lifetime.floatValue);
                    bool next = EditorGUILayout.Toggle(LEternal, eternal);
                    if (next != eternal)
                        lifetime.floatValue = next ? float.PositiveInfinity : FiniteLifetimeDefault;

                    if (!next)
                    {
                        EditorGUILayout.PropertyField(lifetime, LLifetime);
                        if (fadeIn != null) EditorGUILayout.PropertyField(fadeIn, LFadeIn);
                        if (fadeOut != null) EditorGUILayout.PropertyField(fadeOut, LFadeOut);
                    }
                }
                else
                {
                    // Нет поля Lifetime (рассинхрон имён) — fade показываем безусловно, чтобы не спрятать данные.
                    if (fadeIn != null) EditorGUILayout.PropertyField(fadeIn, LFadeIn);
                    if (fadeOut != null) EditorGUILayout.PropertyField(fadeOut, LFadeOut);
                }

                if (fontSize != null) EditorGUILayout.PropertyField(fontSize, LFontSize);
                if (textColor != null) EditorGUILayout.PropertyField(textColor, LTextColor);
                if (background != null) EditorGUILayout.PropertyField(background, LBackground);
                if (offset != null) EditorGUILayout.PropertyField(offset, LOffset);
            }
            return remove;
        }

        private static string EntryTitle(SerializedProperty kind, int index)
        {
            if (kind == null) return $"Entry {index}";
            var names = kind.enumDisplayNames;
            int i = kind.enumValueIndex;
            return (i >= 0 && i < names.Length) ? names[i] : $"Entry {index}";
        }

        // Есть ли ДО index другой Entry с тем же Kind (For() вернёт первый — дубликат осиротеет).
        private bool IsKindDuplicatedBefore(int enumValueIndex, int index)
        {
            for (int j = 0; j < index; j++)
            {
                var kj = _entries.GetArrayElementAtIndex(j).FindPropertyRelative("Kind");
                if (kj != null && kj.enumValueIndex == enumValueIndex)
                    return true;
            }
            return false;
        }

        // Новому Entry (клон от arraySize++) — наименьший вид, ещё не занятый другими (не плодим дубликат Kind).
        private void AssignUnusedKind(int newIndex)
        {
            var kind = _entries.GetArrayElementAtIndex(newIndex).FindPropertyRelative("Kind");
            if (kind == null) return;
            int count = kind.enumNames.Length;
            var used = new bool[count];
            for (int i = 0; i < _entries.arraySize; i++)
            {
                if (i == newIndex) continue;
                var ki = _entries.GetArrayElementAtIndex(i).FindPropertyRelative("Kind");
                if (ki != null && ki.enumValueIndex >= 0 && ki.enumValueIndex < count)
                    used[ki.enumValueIndex] = true;
            }
            for (int v = 0; v < count; v++)
                if (!used[v]) { kind.enumValueIndex = v; return; }
            // все виды заняты — оставляем клон как есть; предупреждение о дубликате покажет DrawEntry
        }
    }
}
#endif
