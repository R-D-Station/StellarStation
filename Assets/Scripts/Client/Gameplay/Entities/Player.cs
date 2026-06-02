using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Client.Gameplay.Input;
using Client.Gameplay.Util.AdvancedValues;
using Client.Gameplay.Fsm;

namespace Client.Gameplay.Entities
{
    public class Player : Entity
    {
        [SerializeField] PlayerControl _playerControls;

        private void Start()
        {
            Speed = new AdvancedValue(500.0f);
            CreateFSM();
        }
        private void OnEnable()
        {
            if (_playerControls == null)
            {
                _playerControls = new PlayerControl();

                _playerControls.Player.Move.performed += OnMovementPerformed;
                _playerControls.Player.Move.canceled += OnMovementCanceled;

                _playerControls.Player.ToggleLaying.performed += OnToggleLaying;

                _playerControls.Player.Sprint.performed += OnSprintPerformed;
                _playerControls.Player.Sprint.canceled += OnSprintCanceled;
            }

            _playerControls.Enable();
        }
        private void OnDisable()
        {
            _playerControls.Disable();
        }

        protected override void CreateFSM()
        {
            base.CreateFSM();

            Fsm.AddState(new FSM_StateStandPlayer(Fsm, this));
            Fsm.AddState(new FSM_StateMovePlayer(Fsm, this));
            Fsm.AddState(new FSM_StateStunPlayer(Fsm, this));
            Fsm.AddState(new FSM_StateLayingPlayer(Fsm, this));
            Fsm.AddState(new FSM_StateUnconsciousPlayer(Fsm, this));
            Fsm.AddState(new FSM_StateDeadPlayer(Fsm, this));

            Fsm.SetState<FSM_StateStandPlayer>();
        }

        private void Update()
        {
            Fsm.Update();
            UpdateFacing();
        }

        public PlayerControl GetPlayerControl()
        {
            return _playerControls;
        }
        private void UpdateFacing()
        {
            if (MoveDirection == Vector3.zero) return;

            if (Mathf.Abs(MoveDirection.x) > Mathf.Abs(MoveDirection.z))
            {
                Facing = MoveDirection.x > 0 ? Direction.East : Direction.West;
            }
            else
            {
                Facing = MoveDirection.z > 0 ? Direction.North : Direction.South;
            }
        }
        private void OnMovementPerformed(InputAction.CallbackContext context)
        {
            // Считываем вектор движения (значение от -1 до 1 по осям X и Z)
            Vector2 input = context.ReadValue<Vector2>();
            MoveDirection = new Vector3(input.x, 0, input.y);
        }

        private void OnMovementCanceled(InputAction.CallbackContext context)
        {
            MoveDirection = Vector3.zero;
        }
        private void OnToggleLaying(InputAction.CallbackContext context)
        {
            // Если уже лежим добровольно — встаём
            if (Fsm.StateCurrent is FSM_StateLayingPlayer
                && CurrentLayingReason == LayingReason.Voluntary)
            {
                Fsm.SetState<FSM_StateStandPlayer>();
                return;
            }

            // Если стоим/двигаемся — ложимся
            if (Fsm.StateCurrent is FSM_StateStandPlayer
                || Fsm.StateCurrent is FSM_StateMovePlayer)
            {
                CurrentLayingReason = LayingReason.Voluntary;
                Fsm.SetState<FSM_StateLayingPlayer>();
            }

            // Если в стане / нокдауне / без сознания / мёртв — F игнорируется
        }

        private void OnSprintPerformed(InputAction.CallbackContext context)
        {
            IsSprintHeld = true;
            Speed.UpdateScaleBaseValue(0.4f);
        }

        private void OnSprintCanceled(InputAction.CallbackContext context)
        {
            IsSprintHeld = false;
            Speed.UpdateScaleBaseValue(-0.4f);
        }
    }
}
