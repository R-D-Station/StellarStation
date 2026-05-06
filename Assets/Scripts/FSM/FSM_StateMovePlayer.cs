

using UnityEngine;

namespace FinalStateMachine
{
    public class FSM_StateMovePlayer : FSM_State
    {
        protected CharacterController _characterController;

        public FSM_StateMovePlayer(Fsm fsm, CharacterController characterController) : base(fsm)
        {
            _characterController = characterController;
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

