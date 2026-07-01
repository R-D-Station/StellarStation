using UnityEngine;
using Shared.Messages.Core;
using Shared.Simulation;
using Client.Gameplay.Entities;
using Client.Config;

namespace Client.Net.View
{
    /// <summary>Визуал сущности: движение по снапшотам (интерполяция) или предсказанию (свой игрок).</summary>
    public class NetEntityView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Header("Спрайты: Stand")]
        [SerializeField] private Sprite _northSprite;
        [SerializeField] private Sprite _southSprite;
        [SerializeField] private Sprite _eastSprite;
        [SerializeField] private Sprite _westSprite;

        [Header("Спрайты: Move (опц.)")]
        [SerializeField] private Sprite _northMoveSprite;
        [SerializeField] private Sprite _southMoveSprite;
        [SerializeField] private Sprite _eastMoveSprite;
        [SerializeField] private Sprite _westMoveSprite;

        [Header("Спрайты: Stun (опц.)")]
        [SerializeField] private Sprite _northStunSprite;
        [SerializeField] private Sprite _southStunSprite;
        [SerializeField] private Sprite _eastStunSprite;
        [SerializeField] private Sprite _westStunSprite;

        [Header("Спрайты: Laying (опц.)")]
        [SerializeField] private Sprite _northLayingSprite;
        [SerializeField] private Sprite _southLayingSprite;
        [SerializeField] private Sprite _eastLayingSprite;
        [SerializeField] private Sprite _westLayingSprite;

        [Header("Спрайты: KnockedDown (опц.)")]
        [SerializeField] private Sprite _northKnockedDownSprite;
        [SerializeField] private Sprite _southKnockedDownSprite;
        [SerializeField] private Sprite _eastKnockedDownSprite;
        [SerializeField] private Sprite _westKnockedDownSprite;

        [Header("Спрайты: Unconscious (опц.)")]
        [SerializeField] private Sprite _northUnconsciousSprite;
        [SerializeField] private Sprite _southUnconsciousSprite;
        [SerializeField] private Sprite _eastUnconsciousSprite;
        [SerializeField] private Sprite _westUnconsciousSprite;

        [Header("Спрайты: Dead (обязат.)")]
        [SerializeField] private Sprite _northDeadSprite;
        [SerializeField] private Sprite _southDeadSprite;
        [SerializeField] private Sprite _eastDeadSprite;
        [SerializeField] private Sprite _westDeadSprite;

        private readonly SnapshotBuffer _buffer = new SnapshotBuffer();
        private byte _lastFacing = 255;
        private byte _lastState = 255;
        private byte _lastReason = 255;
        private bool _isLocal;

        [Tooltip("Скорость, с которой локальный визуал догоняет предсказанную позицию. Больше = резче.")]
        [SerializeField] private float _smoothing = 20f;
        private Vector3 _targetPos;
        private bool _hasTarget;

        public int NetId { get; private set; }

        public void Init(int netId)
        {
            NetId = netId;
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        public void Receive(in EntitySnapshot snap, float now)
        {
            _buffer.Push(now, snap);
        }

        /// <summary>Задать предсказанную позицию локального игрока; State/Reason — авторитетные из снапшота.</summary>
        public void SetPredicted(float x, float y, int z, byte facing, byte state, byte reason)
        {
            _isLocal = true;
            _targetPos = new Vector3(x, z * RenderConfig.FloorHeight, y);

            // Первый кадр — жёстко, без интерполяции из (0,0,0).
            if (!_hasTarget)
            {
                transform.position = _targetPos;
                _hasTarget = true;
            }

            ApplySprite(state, facing, reason);
        }

        private void Update()
        {
            if (_isLocal)
            {
                if (_hasTarget)
                {
                    float t = 1f - Mathf.Exp(-_smoothing * Time.deltaTime);
                    transform.position = Vector3.Lerp(transform.position, _targetPos, t);
                }
                return;
            }

            if (!_buffer.HaveSample(Time.time, out float x, out float y, out float z, out byte facing, out byte state, out byte reason))
                return;

            // Сервер (X, Y=глубина, Z=этаж) -> Unity (X, высота, Z=глубина).
            transform.position = new Vector3(x, z * RenderConfig.FloorHeight, y);

            ApplySprite(state, facing, reason);
        }

        /// <summary>Перевыбрать спрайт при смене State, Facing ИЛИ Reason (все дискретны).</summary>
        private void ApplySprite(byte state, byte facing, byte reason)
        {
            if (facing == _lastFacing && state == _lastState && reason == _lastReason) return;
            _spriteRenderer.sprite = GetSprite(state, reason, (Direction)facing);
            _lastFacing = facing;
            _lastState = state;
            _lastReason = reason;
        }

        // Спрайт по (State, Reason, Direction) для всех 6 PlayerState. Пустой слот → фолбэк в Stand-набор;
        // у Laying KnockedDown — свой слот с фолбэком в общий Laying → Stand (Voluntary рисуется общим Laying).
        private Sprite GetSprite(byte state, byte reason, Direction dir)
        {
            switch ((PlayerState)state)
            {
                case PlayerState.Move:        return Pick(dir, _northMoveSprite, _southMoveSprite, _eastMoveSprite, _westMoveSprite);
                case PlayerState.Stun:        return Pick(dir, _northStunSprite, _southStunSprite, _eastStunSprite, _westStunSprite);
                case PlayerState.Laying:
                    if ((LayingReason)reason == LayingReason.KnockedDown)
                    {
                        var kd = Dir(dir, _northKnockedDownSprite, _southKnockedDownSprite, _eastKnockedDownSprite, _westKnockedDownSprite);
                        if (kd != null) return kd; // иначе — общий Laying-набор ниже
                    }
                    return Pick(dir, _northLayingSprite, _southLayingSprite, _eastLayingSprite, _westLayingSprite);
                case PlayerState.Unconscious: return Pick(dir, _northUnconsciousSprite, _southUnconsciousSprite, _eastUnconsciousSprite, _westUnconsciousSprite);
                case PlayerState.Dead:        return Pick(dir, _northDeadSprite, _southDeadSprite, _eastDeadSprite, _westDeadSprite);
                default:                      return StandSprite(dir); // Stand
            }
        }

        // Спрайт набора по направлению (без фолбэка).
        private static Sprite Dir(Direction dir, Sprite n, Sprite s, Sprite e, Sprite w) => dir switch
        {
            Direction.North => n,
            Direction.East => e,
            Direction.West => w,
            _ => s,
        };

        // Набор состояния по направлению; пустой слот → фолбэк в Stand-набор.
        private Sprite Pick(Direction dir, Sprite n, Sprite s, Sprite e, Sprite w)
        {
            Sprite chosen = Dir(dir, n, s, e, w);
            return chosen != null ? chosen : StandSprite(dir);
        }

        private Sprite StandSprite(Direction dir) => Dir(dir, _northSprite, _southSprite, _eastSprite, _westSprite);
    }
}
