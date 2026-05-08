using PlayerControls;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FinalStateMachine
{
    public class FSM_StateMovePlayer : FSM_State
    {
        protected Player _entity;

        public FSM_StateMovePlayer(Fsm fsm, Entity entity) : base(fsm)
        {
            _entity = entity.GetComponent<Player>();
            PlayerControl inputActions = _entity.GetPlayerControl();
            inputActions.Player.Move.canceled += StopMove; 
        }

        public override void Enter()
        {
            _entity.Moved = true;
        }
        public override void Update()
        {
            Vector2 move = _entity.MoveDirection * _entity._speed.CurrentValue * Time.deltaTime;

            _entity._rigidbody2D.linearVelocity = move;
        }
        public override void Exit()
        {
            _entity._rigidbody2D.linearVelocity = Vector2.zero;
            _entity.Moved = false;
        }

        public void StopMove(InputAction.CallbackContext context)
        {
            Fsm.SetState<FSM_StateStandPlayer>();
        }
    }
}

