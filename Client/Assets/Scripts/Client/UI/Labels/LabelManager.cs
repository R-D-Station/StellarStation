using UnityEngine;
using UnityEngine.InputSystem;
using Client.Util;

namespace Client.UI.Labels
{
    /// <summary>Менеджер пула экранных надписей (срез 1: только CursorHint). Пул под активным Canvas (Screen-Space-Overlay);
    /// свободная надпись = SetActive(false). World-anchored (над игроком/объектом) — следующий срез.</summary>
    public sealed class LabelManager : MonoBehaviour
    {
        [SerializeField] private PooledLabel _prefab;
        [Tooltip("Transform активного Canvas — контейнер надписей.")]
        [SerializeField] private Transform _canvas;
        [SerializeField] private int _initialPool = 8;
        [SerializeField] private LabelStyleTable _style;

        private PoolMono<PooledLabel> _pool;

        private void Awake()
        {
            _pool = new PoolMono<PooledLabel>(_prefab, _initialPool, _canvas) { autoExpand = true };
        }

        /// <summary>Показать курсор-подсказку: надпись из пула, стиль по виду, follow=за курсором (иначе фикс в screenPos). Хендл — для UpdateCursorHint/Dismiss.</summary>
        public PooledLabel ShowCursorHint(LabelKind kind, string text, Vector2 screenPos, bool follow)
        {
            var s = _style != null ? _style.For(kind) : LabelStyleTable.Default(kind);
            var label = _pool.GetFreeElement();
            label.Configure(follow ? PooledLabel.Mode.FollowScreen : PooledLabel.Mode.ScreenFixed,
                text, screenPos, s.Lifetime, s.FadeIn, s.FadeOut, s.FontSize, s.TextColor, s.Background, s.Offset);
            return label;
        }

        /// <summary>Обновить позицию курсор-подсказки (для follow — звать покадрово из владельца).</summary>
        public void UpdateCursorHint(PooledLabel handle, Vector2 screenPos)
        {
            if (handle != null) handle.SetScreenPos(screenPos);
        }

        /// <summary>Снять подсказку (вернуть в пул).</summary>
        public void Dismiss(PooledLabel handle)
        {
            if (handle != null) handle.Dismiss();
        }

        // Dev-проверка пула в Play (hover не мигрируем → это единственный живой триггер среза): спавн CursorHint у курсора, follow.
        [ContextMenu("Debug: spawn test cursor hint")]
        private void DebugSpawnHint()
        {
            if (_pool == null) { Debug.LogWarning("[LabelManager] пул не создан — запусти Play"); return; }
            Vector2 pos = Mouse.current != null
                ? Mouse.current.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            ShowCursorHint(LabelKind.CursorHint, "Тест: курсор-подсказка", pos, follow: true);
        }
    }
}
