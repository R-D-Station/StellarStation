using UnityEngine;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Fsm
{
    public class FSM_StateUnconsciousPlayer : FSM_State
    {
        protected Player entity;

        public FSM_StateUnconsciousPlayer(FSM fsm, Entity entity) : base(fsm)
        {
            this.entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            entity.Rigidbody.linearVelocity = Vector3.zero;
            entity.DisableMovement = true;
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
            entity.DisableMovement = false;
        }
    }
}