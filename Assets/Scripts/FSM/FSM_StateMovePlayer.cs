using UnityEngine;

namespace FinalStateMachine
{
    public class FSM_StateMovePlayer : FSM_State
    {
        protected Player _entity;

        public FSM_StateMovePlayer(Fsm fsm, Entity entity) : base(fsm)
        {
            _entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            _entity.Moved = true;
        }

        public override void Update()
        {
            if (_entity.DisableMovement)
            {
                Fsm.SetState<FSM_StateStandPlayer>();
                return;
            }

            // если игрок отпустил клавиши Ч возвращаемс€ в Stand
            if (_entity.MoveDirection == Vector3.zero)
            {
                Fsm.SetState<FSM_StateStandPlayer>();
                return;
            }

            Vector3 move = _entity.MoveDirection * _entity.Speed.CurrentValue * Time.deltaTime;
            _entity.Rigidbody.linearVelocity = move;
        }

        public override void Exit()
        {
            _entity.Rigidbody.linearVelocity = Vector3.zero;
            _entity.Moved = false;
        }
    }
}