using UnityEngine;
using Shared.Messages.Core;
using Client.Gameplay.Entities; // нужен enum Entity.Direction

namespace Client.Net.View
{
    /// <summary>
    /// Визуальное представление одной сетевой сущности. Позиция приходит из
    /// снапшотов и интерполируется. Нет Rigidbody-движения, нет клиентского
    /// FSM и нет ссылки на Player — это просто отображение состояния сервера.
    ///
    /// Маппинг осей: сервер мыслит плоскостью XY (Y = земля), Z = дискретный этаж.
    /// Unity мыслит XZ (Y = высота). Перевод делается в одной точке — здесь.
    /// 1 тайл = 1 юнит по X/Y сервера.
    /// </summary>
    public class NetEntityView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Tooltip("Высота этажа в юнитах Unity по оси Y (высота). Для плоского теста = 0.")]
        [SerializeField] private float FloorHeight = 0f;

        [Header("Direction Sprites")]
        [SerializeField] private Sprite _northSprite;
        [SerializeField] private Sprite _southSprite;
        [SerializeField] private Sprite _eastSprite;
        [SerializeField] private Sprite _westSprite;

        private readonly SnapshotBuffer _buffer = new SnapshotBuffer();
        private byte _lastFacing = 255;
        private bool _isLocal;

        // Сглаживание визуальной позиции своего игрока. Логическая (предсказанная)
        // позиция точная, а спрайт догоняет её плавно — резкие коррекции
        // reconciliation не видны как телепорт.
        [Tooltip("Скорость, с которой спрайт догоняет предсказанную позицию. Больше = резче/точнее, меньше = плавнее.")]
        [SerializeField] private float _smoothing = 20f;
        private Vector3 _targetPos;
        private bool _hasTarget;

        public int NetId { get; private set; }

        public void Init(int netId)
        {
            NetId = netId;
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>Принять новый снапшот сущности.</summary>
        public void Receive(in EntitySnapshot snap, float now)
        {
            _buffer.Push(now, snap);
        }

        /// <summary>
        /// Прямая установка позиции для СВОЕГО игрока (предсказание). В обход
        /// интерполяционного буфера: свой игрок не интерполируется, он ведётся
        /// предсказанием + reconciliation в NetworkRunner.
        /// </summary>
        public void SetPredicted(float x, float y, int z, byte facing, float floorHeight)
        {
            _isLocal = true;
            _targetPos = new Vector3(x, z * floorHeight, y);

            // Первый кадр — ставим сразу, без сглаживания (иначе спрайт приедет
            // из (0,0,0)). Дальше визуал плавно догоняет цель в Update.
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
                // Свой игрок: визуал плавно догоняет предсказанную цель.
                if (_hasTarget)
                {
                    float t = 1f - Mathf.Exp(-_smoothing * Time.deltaTime);
                    transform.position = Vector3.Lerp(transform.position, _targetPos, t);
                }
                return;
            }

            if (!_buffer.HaveSample(Time.time, out float x, out float y, out float z, out byte facing))
                return;

            // Сервер (X, Y=земля, Z=этаж) -> Unity (X, высота, Y_земля).
            // Высота: пока Z дискретный, Unity-Y = z * FloorHeight (тест-режим 1:1).
            transform.position = new Vector3(x, z * FloorHeight, y);

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