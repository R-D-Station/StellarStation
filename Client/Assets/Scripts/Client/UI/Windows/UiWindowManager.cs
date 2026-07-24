using System.Collections.Generic;
using UnityEngine;

namespace Client.UI.Windows
{
    public sealed class UiWindowManager : MonoBehaviour
    {
        [SerializeField] private RectTransform _root;

        private readonly List<UiWindow> _open = new List<UiWindow>();

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

        public void Close(UiWindow w)
        {
            if (w == null) return;
            _open.Remove(w);
            Destroy(w.gameObject);
        }

        public void BringToFront(UiWindow w)
        {
            if (w != null) w.transform.SetAsLastSibling();
        }
    }
}
