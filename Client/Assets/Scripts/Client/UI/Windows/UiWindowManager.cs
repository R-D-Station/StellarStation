using System.Collections.Generic;
using UnityEngine;

namespace Client.UI.Windows
{
    /// <summary>Общий менеджер плавающих окон на Canvas — контент-агностичный (инстанс/каскад/фокус/закрытие).</summary>
    public sealed class UiWindowManager : MonoBehaviour
    {
        [SerializeField] private RectTransform _root;

        private readonly List<UiWindow> _open = new List<UiWindow>();

        /// <summary>Инстанциирует окно с каскадным смещением от числа уже открытых и выводит его наверх.</summary>
        public UiWindow Open(UiWindow prefab)
        {
            if (prefab == null || _root == null) return null;

            var w = Instantiate(prefab, _root, false);
            if (w.transform is RectTransform rt)
                rt.anchoredPosition = new Vector2(24f, -24f) * _open.Count;

            w.Init(this);
            _open.Add(w);
            BringToFront(w);
            return w;
        }

        /// <summary>Закрывает (уничтожает) окно.</summary>
        public void Close(UiWindow w)
        {
            if (w == null) return;
            _open.Remove(w);
            Destroy(w.gameObject);
        }

        /// <summary>Выводит окно поверх остальных (последний sibling).</summary>
        public void BringToFront(UiWindow w)
        {
            if (w != null) w.transform.SetAsLastSibling();
        }
    }
}
