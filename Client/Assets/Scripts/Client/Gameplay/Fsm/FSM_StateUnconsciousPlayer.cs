using UnityEngine;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Fsm
{
    /// <summary>
    /// Состояние без сознания: выход только извне (медик, эпинефрин).
    /// </summary>
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
            // WIP: чёрный экран, отключение HUD
        }

        public override void Update()
        {
            // WIP: выход вызывается внешней системой через Fsm.SetState
        }

        public override void Exit()
        {
            entity.DisableMovement = false;
        }
    }
}