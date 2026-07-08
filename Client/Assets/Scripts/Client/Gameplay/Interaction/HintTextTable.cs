using UnityEngine;
using Shared.Simulation;

namespace Client.Gameplay.Interaction
{
    /// <summary>SO-словарь текста подсказок по <see cref="HintKind"/> (замена статик-<see cref="HintText"/>; задел под локализацию).</summary>
    [CreateAssetMenu(menuName = "Station/Hint Text Table", fileName = "HintTextTable")]
    public sealed class HintTextTable : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public HintKind Kind;
            public string Text;
        }

        [Tooltip("Пары «вид подсказки → текст». Нет записи для вида → пустая строка.")]
        [SerializeField] private Entry[] _entries = System.Array.Empty<Entry>();

        /// <summary>Текст подсказки вида k (линейный поиск; string.Empty, если нет) — не горячий путь, резолв при показе тултипа.</summary>
        public string For(HintKind k)
        {
            if (_entries != null)
                for (int i = 0; i < _entries.Length; i++)
                    if (_entries[i].Kind == k)
                        return _entries[i].Text ?? string.Empty;
            return string.Empty;
        }
    }
}
