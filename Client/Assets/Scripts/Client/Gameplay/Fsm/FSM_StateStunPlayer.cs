using UnityEngine;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Fsm
{
    public class FSM_StateStunPlayer : FSM_State
    {
        protected Player entity;
        private float _timer;

        public FSM_StateStunPlayer(FSM fsm, Entity entity) : base(fsm)
        {
            this.entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            _timer = entity.StunDuration;
            entity.Rigidbody.linearVelocity = Vector3.zero;
            entity.DisableMovement = true;
        }

        public override void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                fsm.SetState<FSM_StateStandPlayer>();
            }
        }

        public override void Exit()
        {
            entity.DisableMovement = false;
        }
    }
}