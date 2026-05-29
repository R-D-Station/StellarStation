using UnityEngine;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Fsm
{
    public class FSM_StateStandPlayer : FSM_State
    {
        protected Player entity;

        public FSM_StateStandPlayer(FSM fsm, Entity entity) : base(fsm)
        {
            this.entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            entity.Rigidbody.linearVelocity = Vector3.zero;
        }

        public override void Update()
        {
            if (entity.DisableMovement) return;

            if (entity.MoveDirection != Vector3.zero)
            {
                fsm.SetState<FSM_StateMovePlayer>();
            }
        }

        public override void Exit() { }
    }
}