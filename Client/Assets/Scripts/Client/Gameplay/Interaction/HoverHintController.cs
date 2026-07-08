using UnityEngine;
using UnityEngine.InputSystem;
using Shared.Simulation;
using Shared.World;
using Client.Net;

namespace Client.Gameplay.Interaction
{
    /// <summary>Подсказка ЛКМ-действия по наведению: держишь курсор ≥DwellSeconds на интерактивном тайле (в дальности) →
    /// IMGUI-тултип у курсора. Драйвится <see cref="NetworkRunner"/>.Tick(this) из Update. MVP — лестница (см. InteractionHints).</summary>
    public sealed class HoverHintController : MonoBehaviour
    {
        [Tooltip("Держать курсор на цели столько секунд, прежде чем показать подсказку.")]
        [SerializeField] private float _dwellSeconds = 2f;
        [Tooltip("Гейт дальности: подсказка только если тайл в досягаемости (chebyshev≤1, тот же Z).")]
        [SerializeField] private bool _reachGate = true;
        [Tooltip("Гейт видимости (FOV). Пока no-op (задел на будущее).")]
        [SerializeField] private bool _fovGate = false;
        [Tooltip("Смещение тултипа от курсора (экранные пиксели).")]
        [SerializeField] private Vector2 _cursorOffset = new Vector2(16f, 16f);
        [Tooltip("Тултип следует за курсором; иначе фиксируется в точке появления.")]
        [SerializeField] private bool _followCursor = true;
        [Tooltip("Размер шрифта тултипа.")]
        [SerializeField] private int _fontSize = 14;
        [Tooltip("Тон фон-бокса тултипа (RGBA).")]
        [SerializeField] private Color _boxTint = new Color(0f, 0f, 0f, 0.8f);

        [Tooltip("SO-словарь текста подсказок. Пусто → фолбэк на статик HintText.")]
        [SerializeField] private HintTextTable _hintText;

        // Dwell-состояние цели под курсором.
        private int _hx, _hy, _hz;
        private HintKind _kind = HintKind.None;
        private bool _hasTarget;            // курсор на валидной цели (есть подсказка, в дальности)
        private float _dwellStart;          // Time.unscaledTime старта наведения на текущую цель
        private bool _visible;
        private string _text = string.Empty;
        private Vector2 _screenPos;         // экранная позиция (Input, снизу-вверх) для отрисовки

        // Кэш IMGUI (OnGUI бежит несколько раз/кадр — без per-call аллокаций).
        private GUIStyle _labelStyle;
        private GUIStyle _boxStyle;
        private Texture2D _boxTex;
        private readonly GUIContent _content = new GUIContent();

        /// <summary>Прогон hover-hint за кадр: резолв тайла под курсором, dwell-таймер, видимость. Зовётся из NetworkRunner.Update.</summary>
        public void Tick(NetworkRunner runner)
        {
            if (runner == null || Mouse.current == null || !runner.IsInitialized) { Clear(); return; }

            Vector2 cursor = Mouse.current.position.ReadValue();

            // Экран→тайл ОДНИМ impl с кликом (NetworkRunner.TryResolveHoverTile) — чтобы hover и клик не расходились.
            if (!runner.TryResolveHoverTile(cursor, out int hx, out int hy, out int hz)) { Clear(); return; }

            Tile t = runner.GetTileAt(hx, hy, hz);
            if (!InteractionHints.TryResolvePrimary(in t, out HintKind kind)) { Clear(); return; }

            if (_reachGate && !InteractionRules.InReach(runner.PlayerTileX, runner.PlayerTileY, runner.PlayerTileZ, hx, hy, hz))
            {
                Clear();
                return;
            }

            _ = _fovGate; // FOV-гейт — задел: проверка видимости цели пока не реализована (подсказку не гейтим).

            // Смена цели (тайл ИЛИ вид действия) → перезапуск dwell-таймера, скрыть.
            if (!_hasTarget || hx != _hx || hy != _hy || hz != _hz || kind != _kind)
            {
                _hx = hx; _hy = hy; _hz = hz; _kind = kind;
                _hasTarget = true;
                _dwellStart = Time.unscaledTime;
                _visible = false;
            }

            if (_followCursor || !_visible) _screenPos = cursor; // follow: обновляем каждый кадр; иначе фиксируем на показе

            if (!_visible && Time.unscaledTime - _dwellStart >= _dwellSeconds)
            {
                _visible = true;
                _text = _hintText != null ? _hintText.For(kind) : HintText.For(kind);
            }
        }

        // Цель потеряна (нет подсказки / вне дальности / луч мимо / не инициализирован).
        private void Clear()
        {
            _hasTarget = false;
            _visible = false;
            _kind = HintKind.None;
        }

        private void OnGUI()
        {
            if (!_visible || string.IsNullOrEmpty(_text)) return;

            EnsureStyles();
            _content.text = _text;
            Vector2 size = _labelStyle.CalcSize(_content); // включает padding стиля; struct — без heap-alloc

            // Y-флип: Input.position снизу-вверх, IMGUI сверху-вниз.
            float x = _screenPos.x + _cursorOffset.x;
            float y = (Screen.height - _screenPos.y) + _cursorOffset.y;

            // Кламп в экран (правый/нижний край, затем левый/верхний).
            if (x + size.x > Screen.width) x = Screen.width - size.x;
            if (y + size.y > Screen.height) y = Screen.height - size.y;
            if (x < 0f) x = 0f;
            if (y < 0f) y = 0f;

            var rect = new Rect(x, y, size.x, size.y);
            GUI.Box(rect, GUIContent.none, _boxStyle);
            GUI.Label(rect, _content, _labelStyle);
        }

        // Разовая инициализация IMGUI-стилей (нужен GUI-контекст → лениво в OnGUI).
        private void EnsureStyles()
        {
            if (_labelStyle != null) return;

            _boxTex = new Texture2D(1, 1);
            _boxTex.SetPixel(0, 0, _boxTint);
            _boxTex.Apply();

            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = _boxTex;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _fontSize,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                padding = new RectOffset(8, 8, 5, 5)
            };
            _labelStyle.normal.textColor = Color.white;
        }

        private void OnDestroy()
        {
            if (_boxTex != null) Destroy(_boxTex);
        }
    }
}
