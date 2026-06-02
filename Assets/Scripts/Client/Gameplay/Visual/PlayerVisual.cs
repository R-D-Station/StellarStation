using UnityEngine;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Visual
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerVisual : MonoBehaviour
    {
        [SerializeField] private Player _player;
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Header("Direction Sprites")]
        [SerializeField] private Sprite _northSprite;
        [SerializeField] private Sprite _southSprite;
        [SerializeField] private Sprite _eastSprite;
        [SerializeField] private Sprite _westSprite;

        private Entity.Direction _lastFacing;

        private void Reset()
        {
            // јвтозаполнение в редакторе
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _player = GetComponentInParent<Player>();
        }

        private void Awake()
        {
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_player == null) _player = GetComponentInParent<Player>();
        }

        private void LateUpdate()
        {
            // LateUpdate Ч после Update, чтобы Facing уже обновилс€
            if (_player.Facing != _lastFacing)
            {
                _spriteRenderer.sprite = GetSpriteForDirection(_player.Facing);
                _lastFacing = _player.Facing;
            }
        }

        private Sprite GetSpriteForDirection(Entity.Direction dir)
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
