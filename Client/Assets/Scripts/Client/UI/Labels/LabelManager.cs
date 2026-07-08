using UnityEngine;
using UnityEngine.InputSystem;
using Client.Net;
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
        [Tooltip("Опц.: раннер — источник камеры обзора (ViewCamera) для world-anchored надписей.")]
        [SerializeField] private NetworkRunner _runner;

        private PoolMono<PooledLabel> _pool;

        // Ленивый кэш камеры (НЕ в Awake — ClickCamera резолвит Camera.main при 1-м обращении).
        private Camera _cam;
        private Camera Cam => _cam != null ? _cam : (_cam = _runner != null ? _runner.ViewCamera : null);

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

        /// <summary>Надпись, привязанная к ОБЪЕКТУ в мире (FollowWorld: следует за anchor через WorldToScreenPoint; offset —
        /// в SCREEN-пикселях после проекции, 2.5D-safe). Авто-истечение по конечному Lifetime стиля (в PooledLabel).</summary>
        public PooledLabel ShowWorldMessage(LabelKind kind, string text, Transform anchor)
        {
            var s = _style != null ? _style.For(kind) : LabelStyleTable.Default(kind);
            var label = _pool.GetFreeElement();
            label.Configure(PooledLabel.Mode.FollowWorld, text, screenPos: default, s.Lifetime, s.FadeIn, s.FadeOut,
                s.FontSize, s.TextColor, s.Background, s.Offset, worldTarget: anchor, camera: Cam);
            return label;
        }

        /// <summary>Надпись у СТАТИЧНОЙ мировой точки (напр. звук): проекция ОДИН раз → ScreenFixed. sp.z≤0 (за камерой) → null.</summary>
        public PooledLabel ShowWorldMessage(LabelKind kind, string text, Vector3 worldPos)
        {
            Vector3 sp = Cam != null ? Cam.WorldToScreenPoint(worldPos) : worldPos;
            if (sp.z <= 0f) return null; // за спиной камеры — нечего показывать
            var s = _style != null ? _style.For(kind) : LabelStyleTable.Default(kind);
            var label = _pool.GetFreeElement();
            label.Configure(PooledLabel.Mode.ScreenFixed, text, (Vector2)sp, s.Lifetime, s.FadeIn, s.FadeOut,
                s.FontSize, s.TextColor, s.Background, s.Offset);
            return label;
        }

        // Dev-проверка world-anchored (FollowWorld) в Play: переиспользуемый якорь перед камерой → надпись СЛЕДУЕТ за мировой
        // точкой при движении камеры (проверяет DoD «держится над точкой»). Якорь один, под LabelManager (без утечек).
        private Transform _devAnchor;
        [ContextMenu("Debug: spawn test world message")]
        private void DebugSpawnWorld()
        {
            if (_pool == null) { Debug.LogWarning("[LabelManager] пул не создан — запусти Play"); return; }
            if (Cam == null) { Debug.LogWarning("[LabelManager] нет камеры — назначь _runner"); return; }
            if (_devAnchor == null)
            {
                _devAnchor = new GameObject("LabelWorldAnchor(dev)").transform;
                _devAnchor.SetParent(transform, worldPositionStays: true);
            }
            _devAnchor.position = Cam.transform.position + Cam.transform.forward * 10f;
            ShowWorldMessage(LabelKind.ObjectMessage, "test world", _devAnchor);
        }
    }
}
