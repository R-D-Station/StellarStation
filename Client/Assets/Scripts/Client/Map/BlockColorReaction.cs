using UnityEngine;
using Client.Gameplay;

namespace Client.Map
{
    /// <summary>Реакция «смена цвета» на события <see cref="TriggerZone"/>: композирует свой цвет с cut-альфой среза этажей.</summary>
    public sealed class BlockColorReaction : MonoBehaviour, IBlockComponent
    {
        public enum Mode { Speed, Accelerated, Curve }

        [Tooltip("Рендереры, которым меняем цвет. Пусто — берётся Renderer этого объекта.")]
        [SerializeField] private Renderer[] _targets = System.Array.Empty<Renderer>();
        [Tooltip("Имя цветового свойства шейдера. ⚠ При неверном имени цвет молча не изменится, ошибки в консоли НЕ будет.")]
        [SerializeField] private string _colorProperty = "_BaseColor";

        [Tooltip("Цвет в покое (реакция не активна).")]
        [SerializeField] private Color _fromColor = Color.white;
        [Tooltip("Цвет в активном состоянии. Для «спрятать» — альфа 0 (нужен ПРОЗРАЧНЫЙ материал, у непрозрачного Lit эффекта не будет).")]
        [SerializeField] private Color _toColor = new Color(1f, 1f, 1f, 0f);

        [Tooltip("Как идёт переход: равномерно / с ускорением / по кривой.")]
        [SerializeField] private Mode _mode = Mode.Speed;
        [Tooltip("Скорость перехода (доля прогресса в секунду).")]
        [SerializeField] private float _speed = 3f;
        [Tooltip("Ускорение перехода (для режима «с ускорением»).")]
        [SerializeField] private float _acceleration = 6f;
        [Tooltip("Форма перехода для режима «по кривой»: вход 0..1 — прогресс, выход 0..1 — доля цвета.")]
        [SerializeField] private AnimationCurve _curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private static MaterialPropertyBlock _mpb;

        private float _progress;
        private float _target;
        private float _velocity;
        private float _cutAlpha = 1f;
        private bool _settled = true;
        private bool _applied;
        private int _colorId = -1;

        public void Activate() => SetTarget(1f);

        public void Deactivate() => SetTarget(0f);

        public void Toggle() => SetTarget(_target > 0.5f ? 0f : 1f);

        private void SetTarget(float target)
        {
            if (_target == target && _settled)
                return;
            _target = target;
            _settled = false;
            _velocity = 0f;
        }

        public void OnSpawn(in BlockContext ctx)
        {
            _progress = 0f;
            _target = 0f;
            _velocity = 0f;
            _cutAlpha = 1f;
            _settled = true;
            _applied = false;
        }

        public void OnDespawn()
        {
            _settled = true;
            _applied = false;
        }

        public void OnVisibility(float alpha)
        {
            _cutAlpha = alpha;
            if (_applied)
                Apply();
        }

        private void LateUpdate()
        {
            if (_settled)
                return;

            float dt = Time.deltaTime;
            switch (_mode)
            {
                case Mode.Accelerated:
                    _progress = TransitionMath.StepAccelerated(_progress, _target, _velocity, _acceleration, dt, out _velocity);
                    break;
                default:
                    _progress = TransitionMath.StepTowards(_progress, _target, _speed, dt);
                    break;
            }

            if (_progress == _target)
            {
                _settled = true;
                _velocity = 0f;
            }
            Apply();
        }

        private void Apply()
        {
            float k = TransitionMath.Clamp01(_mode == Mode.Curve ? _curve.Evaluate(_progress) : _progress);

            var color = new Color(
                TransitionMath.LerpChannel(_fromColor.r, _toColor.r, k),
                TransitionMath.LerpChannel(_fromColor.g, _toColor.g, k),
                TransitionMath.LerpChannel(_fromColor.b, _toColor.b, k),
                TransitionMath.LerpChannel(_fromColor.a, _toColor.a, k) * _cutAlpha);

            if (_colorId < 0)
                _colorId = Shader.PropertyToID(string.IsNullOrEmpty(_colorProperty) ? "_BaseColor" : _colorProperty);
            _mpb ??= new MaterialPropertyBlock();

            var targets = _targets != null && _targets.Length > 0 ? _targets : null;
            if (targets == null)
            {
                var own = GetComponent<Renderer>();
                if (own == null)
                    return;
                Write(own, color);
                _applied = true;
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var r = targets[i];
                if (r != null)
                    Write(r, color);
            }
            _applied = true;
        }

        private void Write(Renderer r, Color color)
        {
            _mpb.Clear();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(_colorId, color);
            r.SetPropertyBlock(_mpb);
        }
    }
}
