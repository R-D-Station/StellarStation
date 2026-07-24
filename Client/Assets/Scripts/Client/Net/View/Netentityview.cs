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

        private SpriteRenderer _wornOverlay;
        private Sprite[] _wornSprites;
        /// <summary>DefId надетой формы (0 = нет).</summary>
        public ushort WornDefId { get; private set; }

        public void Init(int netId)
        {
            NetId = netId;
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
            CreateWornOverlay();
        }

        private void CreateWornOverlay()
        {
            if (_spriteRenderer == null || _wornOverlay != null) return;
            var go = new GameObject("WornOverlay");
            go.transform.SetParent(_spriteRenderer.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            _wornOverlay = go.AddComponent<SpriteRenderer>();
            _wornOverlay.sortingLayerID = _spriteRenderer.sortingLayerID;
            _wornOverlay.sortingOrder = _spriteRenderer.sortingOrder + 1;
            _wornOverlay.enabled = false;
        }

        /// <summary>Задать/снять оверлей надетой формы (sprites[4] по Direction; меньше 4 или null — оверлей выключен).</summary>
        public void SetWorn(ushort defId, Sprite[] sprites)
        {
            WornDefId = defId;
            _wornSprites = (sprites != null && sprites.Length >= 4) ? sprites : null;
            UpdateWornOverlay(_lastFacing < 4 ? (Direction)_lastFacing : Direction.South);
        }

        // _culled в show: иначе одежда остаётся видимой поверх срезанного/скрытого тела (плавающая шмотка).
        private void UpdateWornOverlay(Direction dir)
        {
            if (_wornOverlay == null) return;
            bool show = _wornSprites != null && !_culled;
            _wornOverlay.sprite = show ? _wornSprites[(int)dir] : null;
            _wornOverlay.enabled = show;
        }

        public void Receive(in EntitySnapshot snap, float now)
        {
            _buffer.Push(now, snap);
        }

        // Cut-away (D2): сущность на срезанном этаже гасится вместе с крышей. Кэш всех рендереров вьюхи
        // (спрайт + дев-капсула); восстановление enabled — здесь же (инвариант pool-renderer-enabled-leak).
        private Renderer[] _allRenderers;
        private bool _culled;

        public void SetCulled(bool culled)
        {
            if (culled == _culled) return;
            _culled = culled;
            _allRenderers ??= GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < _allRenderers.Length; i++)
                if (_allRenderers[i] != null)
                    _allRenderers[i].enabled = !culled;
            UpdateWornOverlay(_lastFacing < 4 ? (Direction)_lastFacing : Direction.South);
        }

        /// <summary>Блок-мир (B2): снапшот уже в осях Unity (Y — высота) — позиция берётся 1:1. Ставит NetworkRunner.</summary>

        /// <summary>Задать предсказанную позицию локального игрока; State/Reason — авторитетные из снапшота.</summary>
        public void SetPredicted(float x, float y, float z, byte facing, byte state, byte reason)
        {
            _isLocal = true;
            _targetPos = new Vector3(x, y, z);

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

            // Тайлы: (X, Y=глубина, Z=этаж) → Unity (X, высота, Z=глубина); блок-мир: оси уже Unity — 1:1.
            transform.position = new Vector3(x, y, z);

            ApplySprite(state, facing, reason);
        }

        /// <summary>Перевыбрать спрайт при смене State, Facing ИЛИ Reason (все дискретны).</summary>
        private void ApplySprite(byte state, byte facing, byte reason)
        {
            if (_spriteRenderer == null) return; // рантайм-вьюха дев-режима: тело рисует капсула, спрайтов нет
            if (facing == _lastFacing && state == _lastState && reason == _lastReason) return;
            _spriteRenderer.sprite = GetSprite(state, reason, (Direction)facing);
            _lastFacing = facing;
            _lastState = state;
            _lastReason = reason;
            UpdateWornOverlay((Direction)facing);
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
