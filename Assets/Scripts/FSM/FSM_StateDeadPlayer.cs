using UnityEngine;

namespace FinalStateMachine
{
    public class FSM_StateDeadPlayer : FSM_State
    {
        protected Player _entity;

        public FSM_StateDeadPlayer(Fsm fsm, Entity entity) : base(fsm)
        {
            _entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            _entity.Rigidbody.linearVelocity = Vector3.zero;
            _entity.DisableMovement = true;
            _entity.IgnoreCollision = true; // тело можно перетаскивать через других
            // WIP - UI "стать гостом", труп-спрайт, и т.д.
        }

        public override void Update() { }

        public override void Exit()
        {
            // выход — только через дефибриллятор/клонирование
            _entity.DisableMovement = false;
            _entity.IgnoreCollision = false;
        }
    }
}