using UnityEngine;

namespace Client.UI.Labels
{
    /// <summary>SO-стили надписей по <see cref="LabelKind"/>: время жизни, фейды, шрифт, цвета, смещение.</summary>
    [CreateAssetMenu(menuName = "Station/Label Style Table", fileName = "LabelStyleTable")]
    public sealed class LabelStyleTable : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public LabelKind Kind;
            [Tooltip("Время жизни, сек. ∞ (float.PositiveInfinity) → без авто-возврата по таймеру (напр. CursorHint).")]
            public float Lifetime;
            public float FadeIn;
            public float FadeOut;
            public float FontSize;
            public Color TextColor;
            public Color Background;
            [Tooltip("Смещение надписи от точки привязки, экранные пиксели.")]
            public Vector2 Offset;
        }

        [Tooltip("Стили по видам надписей. Нет записи для вида → дефолт-Entry.")]
        [SerializeField] private Entry[] _entries = System.Array.Empty<Entry>();

        /// <summary>Стиль вида k (линейный поиск; безопасный <see cref="Default"/>, если нет) — не горячий путь, резолв при показе.</summary>
        public Entry For(LabelKind k)
        {
            if (_entries != null)
                for (int i = 0; i < _entries.Length; i++)
                    if (_entries[i].Kind == k)
                    {
                        var e = _entries[i];
                        // Запись в таблице — ВЫКЛЮЧАТЕЛЬ, а не косметика: пока её нет, Default даёт Lifetime=∞,
                        // но стоит человеку добавить строку в инспекторе — поле по умолчанию 0 погасит надпись
                        // по таймеру, и баг будут искать в потребителе, а не в SO. Для персистентных видов
                        // Lifetime <= 0 трактуем как бесконечность.
                        if (IsPersistent(k) && !(e.Lifetime > 0f))
                            e.Lifetime = float.PositiveInfinity;
                        return e;
                    }
            return Default(k);
        }

        /// <summary>Виды, которые снимает ВЛАДЕЛЕЦ, а не таймер (иначе исчезнут посреди игры).</summary>
        private static bool IsPersistent(LabelKind k) => k == LabelKind.CursorHint;

        /// <summary>Безопасный дефолт, если записи/таблицы нет: видимая персистентная надпись (Lifetime=∞).</summary>
        // Lifetime=∞ намеренно: мисконфиг заметен (надпись висит), а не тихо исчезает за кадр как default(Entry) (Lifetime=0).
        public static Entry Default(LabelKind k) => new Entry
        {
            Kind = k,
            Lifetime = float.PositiveInfinity,
            FadeIn = 0f,
            FadeOut = 0f,
            FontSize = 18f,
            TextColor = Color.white,
            Background = new Color(0f, 0f, 0f, 0.8f),
            Offset = new Vector2(16f, 16f)
        };
    }
}
