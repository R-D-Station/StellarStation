using UnityEngine;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Fsm
{
    public class FSM_StateMovePlayer : FSM_State
    {
        protected Player entity;

        public FSM_StateMovePlayer(FSM fsm, Entity entity) : base(fsm)
        {
            this.entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            entity.Moved = true;
        }

        public override void Update()
        {
            if (entity.DisableMovement)
            {
                fsm.SetState<FSM_StateStandPlayer>();
                return;
            }

            // если игрок отпустил клавиши Ч возвращаемс€ в Stand
            if (entity.MoveDirection == Vector3.zero)
            {
                fsm.SetState<FSM_StateStandPlayer>();
                return;
            }

            Vector3 move = entity.MoveDirection * entity.Speed.CurrentValue * Time.deltaTime;
            entity.Rigidbody.linearVelocity = move;
        }

        public override void Exit()
        {
            entity.Rigidbody.linearVelocity = Vector3.zero;
            entity.Moved = false;
        }
    }
}