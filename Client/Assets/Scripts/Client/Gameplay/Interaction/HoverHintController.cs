using UnityEngine;
using UnityEngine.InputSystem;
using Shared.Simulation;
using Shared.World;
using Client.Net;
using Client.UI.Labels;

namespace Client.Gameplay.Interaction
{
    /// <summary>Подсказка ЛКМ-действия по наведению: dwell на интерактивном тайле в дальности → пул-надпись CursorHint у курсора.</summary>
    public sealed class HoverHintController : MonoBehaviour
    {
        [Tooltip("Держать курсор на цели столько секунд, прежде чем показать подсказку.")]
        [SerializeField] private float _dwellSeconds = 2f;
        [Tooltip("Гейт дальности: подсказка только если тайл в досягаемости (chebyshev≤1, тот же Z).")]
        [SerializeField] private bool _reachGate = true;
        [Tooltip("Гейт видимости (FOV). Пока no-op (задел на будущее).")]
        [SerializeField] private bool _fovGate = false;
        [Tooltip("Тултип следует за курсором; иначе фиксируется в точке появления.")]
        [SerializeField] private bool _followCursor = true;

        [Tooltip("SO-словарь текста подсказок. Пусто → фолбэк на статик HintText.")]
        [SerializeField] private HintTextTable _hintText;

        [Tooltip("Пул экранных надписей. Пусто → подсказка не показывается (без NRE). Стиль/шрифт/цвет/зазор — из LabelStyleTable записи CursorHint.")]
        [SerializeField] private LabelManager _labels;

        // Dwell-состояние цели под курсором.
        private int _hx, _hy, _hz;
        private HintKind _kind = HintKind.None;
        private bool _hasTarget;            // курсор на валидной цели (есть подсказка, в дальности)
        private float _dwellStart;          // Time.unscaledTime старта наведения на текущую цель
        private bool _visible;
        private Vector2 _screenPos;         // экранная позиция курсора (Input, снизу-вверх) — БЕЗ Y-флипа (overlay bottom-left)

        private PooledLabel _handle;        // живой хендл текущей надписи (null — нет)

        /// <summary>Прогон hover-hint за кадр: резолв тайла под курсором, dwell-таймер, показ/следование пул-надписи. Зовётся из NetworkRunner.Update.</summary>
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

            // Смена цели (тайл ИЛИ вид действия) → перезапуск dwell-таймера, снять текущую надпись.
            if (!_hasTarget || hx != _hx || hy != _hy || hz != _hz || kind != _kind)
            {
                _hx = hx; _hy = hy; _hz = hz; _kind = kind;
                _hasTarget = true;
                _dwellStart = Time.unscaledTime;
                _visible = false;
                DismissHandle();
            }

            if (_followCursor || !_visible) _screenPos = cursor; // follow: обновляем каждый кадр; иначе фиксируем на показе

            if (!_visible && Time.unscaledTime - _dwellStart >= _dwellSeconds)
            {
                // ПЕРЕХОД false→true: dwell истёк → показать пул-надпись ОДИН раз (не покадрово — иначе 30+ надписей/сек).
                _visible = true;
                string text = _hintText != null ? _hintText.For(kind) : HintText.For(kind);
                _handle = _labels != null ? _labels.ShowCursorHint(LabelKind.CursorHint, text, _screenPos, _followCursor) : null;
            }
            else if (_visible && _followCursor && _handle != null)
            {
                _labels.UpdateCursorHint(_handle, _screenPos); // толкаем позицию курсора (без Y-флипа)
            }
        }

        // Цель потеряна (нет подсказки / вне дальности / луч мимо / не инициализирован) — снять надпись.
        private void Clear()
        {
            _hasTarget = false;
            _visible = false;
            _kind = HintKind.None;
            DismissHandle();
        }

        // Вернуть текущую надпись в пул (idempotent — LabelManager.Dismiss + PooledLabel double-return guard).
        private void DismissHandle()
        {
            if (_handle == null) return;
            if (_labels != null) _labels.Dismiss(_handle);
            _handle = null;
        }
    }
}
