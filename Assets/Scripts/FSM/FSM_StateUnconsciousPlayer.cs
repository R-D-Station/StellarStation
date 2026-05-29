using UnityEngine;

namespace FinalStateMachine
{
    public class FSM_StateUnconsciousPlayer : FSM_State
    {
        protected Player _entity;

        public FSM_StateUnconsciousPlayer(Fsm fsm, Entity entity) : base(fsm)
        {
            _entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            _entity.Rigidbody.linearVelocity = Vector3.zero;
            _entity.DisableMovement = true;
            // WIP чёрный экран, отключение HUD, и т.д.
        }

        public override void Update()
        {
            // WIP
            // из этого состояния выход только извне (медик, эпинефрин)
            // другая система вызовет Fsm.SetState<FSM_StateStandPlayer>()
        }

        public override void Exit()
        {
            _entity.DisableMovement = false;
        }
    }
}