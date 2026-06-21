using UnityEngine;
using Shared.Messages.Core;
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

        private readonly SnapshotBuffer _buffer = new SnapshotBuffer();
        private byte _lastFacing = 255;
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

        /// <summary>Задать предсказанную позицию локального игрока.</summary>
        public void SetPredicted(float x, float y, int z, byte facing)
        {
            _isLocal = true;
            _targetPos = new Vector3(x, z * RenderConfig.FloorHeight, y);

            // Первый кадр — жёстко, без интерполяции из (0,0,0).
            if (!_hasTarget)
            {
                transform.position = _targetPos;
                _hasTarget = true;
            }

            if (facing != _lastFacing)
            {
                _spriteRenderer.sprite = GetSprite((Entity.Direction)facing);
                _lastFacing = facing;
            }
        }

        private void Update()
        {
            if (_isLocal)
            {
                // Свой игрок: плавно тянемся к предсказанной цели.
                if (_hasTarget)
                {
                    float t = 1f - Mathf.Exp(-_smoothing * Time.deltaTime);
                    transform.position = Vector3.Lerp(transform.position, _targetPos, t);
                }
                return;
            }

            if (!_buffer.HaveSample(Time.time, out float x, out float y, out float z, out byte facing))
                return;

            // Сервер (X, Y=глубина, Z=этаж) -> Unity (X, высота, Z=глубина).
            transform.position = new Vector3(x, z * RenderConfig.FloorHeight, y);

            if (facing != _lastFacing)
            {
                _spriteRenderer.sprite = GetSprite((Entity.Direction)facing);
                _lastFacing = facing;
            }
        }

        private Sprite GetSprite(Entity.Direction dir)
        {
            switch (dir)
            {
                case Entity.Direction.North: return _northSprite;
                case Entity.Direction.South: return _southSprite;
                case Entity.Direction.East: return _eastSprite;
                case Entity.Direction.West: return _westSprite;
                default: return _southSprite;
            }
        }
    }
}
