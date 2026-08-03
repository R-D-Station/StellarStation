using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Client.UI.Labels
{
    /// <summary>Одна пул-надпись (TMP) на Screen-Space-Overlay Canvas: alpha-рампа, позиция с клампом, double-return guard.</summary>
    public sealed class PooledLabel : MonoBehaviour, IPooledLabel
    {
        public enum Mode { ScreenFixed, FollowScreen, FollowWorld }

        [SerializeField] private TMP_Text _text;
        [SerializeField] private CanvasGroup _group;
        [Tooltip("Опц.: фон-Image (для LabelStyleTable.Background). Пусто → фон не красим.")]
        [SerializeField] private Image _background;
        [Tooltip("Зазор рамки вокруг текста, экранные пиксели: размер RectTransform = preferred size текста + это.")]
        [SerializeField] private Vector2 _padding = new Vector2(16f, 10f);

        private RectTransform _rt;

        // Рантайм-поля (перезаписываются в Configure целиком).
        private float _bornUnscaled;
        private float _lifetime;
        private float _fadeIn;
        private float _fadeOut;
        private Mode _mode;
        private Vector2 _screenPos;
        private Vector2 _offset;
        private Transform _worldTarget;
        private Camera _camera;
        private bool _returned;

        private void Awake() => _rt = (RectTransform)transform;

        /// <summary>Настроить надпись при выдаче из пула — безусловно перезаписывает все поля.</summary>
        public void Configure(Mode mode, string text, Vector2 screenPos, float lifetime, float fadeIn, float fadeOut,
            float fontSize, Color textColor, Color background, Vector2 offset,
            Transform worldTarget = null, Camera camera = null)
        {
            _mode = mode;
            _screenPos = screenPos;
            _offset = offset;
            _worldTarget = worldTarget;
            _camera = camera;
            _lifetime = lifetime; // PositiveInfinity → без таймер-возврата (CursorHint)
            _fadeIn = fadeIn;
            _fadeOut = fadeOut;
            _bornUnscaled = Time.unscaledTime;
            _returned = false;

            if (_text != null)
            {
                _text.text = text;
                _text.fontSize = fontSize;
                _text.color = textColor;
                _rt.sizeDelta = _text.GetPreferredValues(text) + _padding;
            }
            if (_background != null) _background.color = background;
            if (_group != null) _group.alpha = fadeIn > 0f ? 0f : 1f;

            UpdatePosition(); // сразу на место — без кадра на старой позиции
        }

        /// <summary>Сброс до pristine при выдаче из пула (страховка поверх Configure — забытое поле течёт, см. pool-leak).</summary>
        public void OnAcquire()
        {
            _returned = false;
            _mode = Mode.ScreenFixed;
            _worldTarget = null;
            _camera = null;
            _lifetime = float.PositiveInfinity;
            Generation++; // новая выдача — старые хендлы обязаны перестать считаться своими
            if (_text != null) _text.text = string.Empty;
            if (_group != null) _group.alpha = 0f;
        }

        /// <summary>Токен владения выдачей. Метка возвращает себя в пул САМА (истёк lifetime, пропала камера/цель),
        /// а владелец продолжает держать ссылку — без сверки токена он испортил бы ЧУЖУЮ надпись, уже выданную
        /// следующему потребителю.</summary>
        public int Generation { get; private set; }

        /// <summary>Всё ещё моя ли это выдача (метка не вернулась в пул и не переиспользована).</summary>
        public bool IsOwnedBy(int generation) => !_returned && Generation == generation;

        /// <summary>Сменить текст уже показанной надписи. Владелец обязан сперва сверить <see cref="IsOwnedBy"/>.</summary>
        public void SetText(string text)
        {
            if (_text == null) return;
            _text.text = text;
            _rt.sizeDelta = _text.GetPreferredValues(text) + _padding;
        }

        /// <summary>Прокинуть экранную позицию курсора (FollowScreen), БЕЗ Y-флипа (overlay bottom-left = Input).</summary>
        public void SetScreenPos(Vector2 p) => _screenPos = p;

        /// <summary>Снять надпись немедленно (владелец потерял цель). Идёт через общий Return (double-return guard).</summary>
        public void Dismiss() => Return();

        private void Update()
        {
            float age = Time.unscaledTime - _bornUnscaled;

            if (_group != null) _group.alpha = ComputeAlpha(age);

            UpdatePosition();

            // Само-возврат: конечное время жизни истекло (с учётом хвоста fadeOut).
            if (!float.IsInfinity(_lifetime) && age >= _lifetime + _fadeOut)
                Return();
        }

        private float ComputeAlpha(float age)
        {
            if (_fadeIn > 0f && age < _fadeIn) return Mathf.Clamp01(age / _fadeIn);
            if (!float.IsInfinity(_lifetime) && _fadeOut > 0f && age > _lifetime)
                return Mathf.Clamp01(1f - (age - _lifetime) / _fadeOut);
            return 1f;
        }

        private void UpdatePosition()
        {
            if (_mode == Mode.FollowWorld)
            {
                if (_camera == null || _worldTarget == null) { Return(); return; } // Unity fake-null: цель/камера уничтожены
                Vector3 sp = _camera.WorldToScreenPoint(_worldTarget.position);
                if (sp.z <= 0f) { if (_group != null) _group.alpha = 0f; return; } // за спиной камеры — прячем
                SetClampedPosition((Vector2)sp + _offset);
                return;
            }
            // ScreenFixed / FollowScreen: позиция от _screenPos (её обновляет SetScreenPos для follow).
            SetClampedPosition(_screenPos + _offset);
        }

        // Кламп в границы экрана по размеру RectTransform (пивот bottom-left → надпись растёт вправо-вверх, как старый IMGUI-кламп).
        private void SetClampedPosition(Vector2 pos)
        {
            Vector2 size = _rt.sizeDelta;
            pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, Screen.width - size.x));
            pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, Screen.height - size.y));
            _rt.position = pos;
        }

        // Единственный путь возврата в пул. Первый вызов гасит; повтор — no-op (как PooledTile.InPool — два владельца одной надписи).
        private void Return()
        {
            if (_returned) return;
            _returned = true;
            gameObject.SetActive(false);
        }
    }
}
