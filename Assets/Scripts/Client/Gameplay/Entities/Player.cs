using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Client.Gameplay.AdvansedValues;
using Client.Gameplay.Fsm;

namespace Client.Gameplay.Entities
{

    public class Player : Entity
    {
        [SerializeField]
        private InputSystem_Actions _inputActions;

        private void Start()
        {
            CreateFSM();
        }

        private void OnEnable()
        {
            if (_inputActions == null)
            {
                _inputActions = new InputSystem_Actions();

                _inputActions.Player.Move.performed += OnMovementPerformed;
                _inputActions.Player.Move.canceled += OnMovementCanceled;
            }
            _inputActions.Enable();
        }

        private void OnDisable()
        {
            _inputActions.Disable();
        }

        protected override void CreateFSM()
        {
            base.CreateFSM();

            Fsm.AddState(new FSM_StateMovePlayer(Fsm, this));
            Fsm.AddState(new FSM_StateStandPlayer(Fsm, this));

            Fsm.SetState<FSM_StateStandPlayer>();
        }

        private void Update()
        {
            Fsm.Update();
        }

        public InputSystem_Actions GetPlayerControl()
        {
            return _inputActions;
        }

        private void OnMovementPerformed(InputAction.CallbackContext context)
        {
            // —читываем вектор движени€ (значение от -1 до 1 по ос€м X и Y)
            MoveDirection = context.ReadValue<Vector2>();
        }

        private void OnMovementCanceled(InputAction.CallbackContext context)
        {
            MoveDirection = Vector2.zero;
        }
    }
}
