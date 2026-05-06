using UnityEngine;

namespace FinalStateMachine
{
    public class FSM_StateMovePlayer : FSM_State
    {
        protected Entity _entity;

        public FSM_StateMovePlayer(Fsm fsm, Entity entity) : base(fsm)
        {
            _entity = entity;
        }

        public override void Enter()
        {
            _entity.Moved = true;
        }
        public override void Update()
        {
            if (CheckMove()) { return; }

            Vector2 move = _entity.MoveDirection * _entity._speed.CurrentValue * Time.deltaTime;
        }
        public override void Exit()
        {
            _entity.Moved = false;
        }

        bool CheckMove()
        {
            if (_entity.MoveDirection == Vector2.zero)
            {
                Fsm.SetState<FSM_StateStandPlayer>();
                return true;
            }

            return false;
        }
    }
}

