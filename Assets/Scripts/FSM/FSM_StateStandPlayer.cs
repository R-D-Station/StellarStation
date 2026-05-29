using UnityEngine;

namespace FinalStateMachine
{
    public class FSM_StateStandPlayer : FSM_State
    {
        public Player _entity;

        public FSM_StateStandPlayer(Fsm fsm, Entity entity) : base(fsm)
        {
            _entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            _entity.Rigidbody.linearVelocity = Vector3.zero;
        }

        public override void Update()
        {
            if (_entity.DisableMovement) return;

            if (_entity.MoveDirection != Vector3.zero)
            {
                Fsm.SetState<FSM_StateMovePlayer>();
            }
        }

        public override void Exit() { }
    }
}