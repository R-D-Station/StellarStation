using UnityEngine;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Fsm
{
    public class FSM_StateDeadPlayer : FSM_State
    {
        protected Player entity;

        public FSM_StateDeadPlayer(FSM fsm, Entity entity) : base(fsm)
        {
            this.entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            entity.Rigidbody.linearVelocity = Vector3.zero;
            entity.DisableMovement = true;
            entity.IgnoreCollision = true; // тело можно перетаскивать через других
            // WIP - UI "стать гостом", труп-спрайт, и т.д.
        }

        public override void Update() { }

        public override void Exit()
        {
            // выход — только через дефибриллятор/клонирование
            entity.DisableMovement = false;
            entity.IgnoreCollision = false;
        }
    }
}