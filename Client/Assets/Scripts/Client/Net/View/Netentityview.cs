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

        [Header("Direction Sprites")]
        [SerializeField] private Sprite _northSprite;
        [SerializeField] private Sprite _southSprite;
        [SerializeField] private Sprite _eastSprite;
        [SerializeField] private Sprite _westSprite;

        [Header("Move Sprites (опц.: пусто → берётся Stand-набор выше)")]
        [SerializeField] private Sprite _northMoveSprite;
        [SerializeField] private Sprite _southMoveSprite;
        [SerializeField] private Sprite _eastMoveSprite;
        [SerializeField] private Sprite _westMoveSprite;

        private readonly SnapshotBuffer _buffer = new SnapshotBuffer();
        private byte _lastFacing = 255;
        private byte _lastState = 255;
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

        /// <summary>Задать предсказанную позицию локального игрока; State — авторитетный из снапшота.</summary>
        public void SetPredicted(float x, float y, int z, byte facing, byte state)
        {
            _isLocal = true;
            _targetPos = new Vector3(x, z * RenderConfig.FloorHeight, y);

            // Первый кадр — жёстко, без интерполяции из (0,0,0).
            if (!_hasTarget)
            {
                transform.position = _targetPos;
                _hasTarget = true;
            }

            ApplySprite(state, facing);
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

            if (!_buffer.HaveSample(Time.time, out float x, out float y, out float z, out byte facing, out byte state))
                return;

            // Сервер (X, Y=глубина, Z=этаж) -> Unity (X, высота, Z=глубина).
            transform.position = new Vector3(x, z * RenderConfig.FloorHeight, y);

            ApplySprite(state, facing);
        }

        /// <summary>Перевыбрать спрайт при смене State ИЛИ Facing (оба дискретны).</summary>
        private void ApplySprite(byte state, byte facing)
        {
            if (facing == _lastFacing && state == _lastState) return;
            _spriteRenderer.sprite = GetSprite(state, (Entity.Direction)facing);
            _lastFacing = facing;
            _lastState = state;
        }

        private Sprite GetSprite(byte state, Entity.Direction dir)
        {
            // Move-вариант опционален: пока он не назначен в префабе, Move выглядит как Stand.
            bool moving = state == (byte)PlayerState.Move;
            switch (dir)
            {
                case Entity.Direction.North: return moving && _northMoveSprite != null ? _northMoveSprite : _northSprite;
                case Entity.Direction.South: return moving && _southMoveSprite != null ? _southMoveSprite : _southSprite;
                case Entity.Direction.East:  return moving && _eastMoveSprite != null ? _eastMoveSprite : _eastSprite;
                case Entity.Direction.West:  return moving && _westMoveSprite != null ? _westMoveSprite : _westSprite;
                default:                     return moving && _southMoveSprite != null ? _southMoveSprite : _southSprite;
            }
        }
    }
}
