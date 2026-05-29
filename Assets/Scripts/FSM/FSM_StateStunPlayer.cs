using UnityEngine;

namespace FinalStateMachine
{
    public class FSM_StateStunPlayer : FSM_State
    {
        protected Player _entity;
        private float _timer;

        public FSM_StateStunPlayer(Fsm fsm, Entity entity) : base(fsm)
        {
            _entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            _timer = _entity.StunDuration;
            _entity.Rigidbody.linearVelocity = Vector3.zero;
            _entity.DisableMovement = true;
        }

        public override void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Fsm.SetState<FSM_StateStandPlayer>();
            }
        }

        public override void Exit()
        {
            _entity.DisableMovement = false;
        }
    }
}