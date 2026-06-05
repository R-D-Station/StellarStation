using UnityEngine;
using Shared.Messages.Core;
using Client.Gameplay.Entities; // только ради enum Entity.Direction

namespace Client.Net.View
{
    /// <summary>
    /// Представление ОДНОЙ сетевой сущности на клиенте. Позиция приходит из
    /// снапшотов и интерполируется. НЕТ Rigidbody-движения, НЕТ клиентского
    /// FSM — это не офлайн-Player, а чистый приёмник серверного состояния.
    ///
    /// Маппинг тайл->мир: 1 тайл = 1 юнит Unity по X/Y. Этаж (Z) — пока
    /// просто разносим по миру/слоям позже (этап 3). Сейчас Z только хранится.
    /// </summary>
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

        public int NetId { get; private set; }

        public void Init(int netId)
        {
            NetId = netId;
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>Принять новое состояние из снапшота.</summary>
        public void Receive(in EntitySnapshot snap, float now)
        {
            _buffer.Push(now, snap);
        }

        private void Update()
        {
            if (!_buffer.HaveSample(Time.time, out float x, out float y, out float z, out byte facing))
                return;

            // 1 тайл = 1 юнит. Дробная позиция -> плавный визуал.
            transform.position = new Vector3(x, y, 0f);

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