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
            base.Enter();
        }
        public override void Update()
        {
            base.Update();
        }
        public override void Exit()
        {
            base.Exit();
        }
    }
}

