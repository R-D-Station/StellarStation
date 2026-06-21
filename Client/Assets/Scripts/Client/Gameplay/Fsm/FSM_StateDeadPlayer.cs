using UnityEngine;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Fsm
{
    /// <summary>
    /// Состояние смерти игрока.
    /// </summary>
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
            entity.IgnoreCollision = true; // тело можно перетаскивать
            // WIP: UI "стать гостом", труп-спрайт
        }

        public override void Update() { }

        public override void Exit()
        {
            // Выход только через дефибриллятор/клонирование
            entity.DisableMovement = false;
            entity.IgnoreCollision = false;
        }
    }
}