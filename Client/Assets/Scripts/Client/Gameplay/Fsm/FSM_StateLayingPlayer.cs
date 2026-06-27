using UnityEngine;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Fsm
{
    /// <summary>
    /// Состояние "лежит": ползание и автоподъём после нокдауна.
    /// </summary>
    public class FSM_StateLayingPlayer : FSM_State
    {
        protected Player entity;
        private const float CrawlAdvancedValueMultiplier = 0.3f;

        private float _knockdownTimer;

        public FSM_StateLayingPlayer(FSM fsm, Entity entity) : base(fsm)
        {
            this.entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            // Нокдаун — запускаем таймер автоподъёма
            if (entity.CurrentLayingReason == Entity.LayingReason.KnockedDown)
            {
                _knockdownTimer = entity.KnockdownDuration;
            }
            else
            {
                _knockdownTimer = 0f;
            }

            entity.Speed.UpdateScaleCurrentValue(-CrawlAdvancedValueMultiplier);
        }

        public override void Update()
        {
            if (entity.MoveDirection != Vector3.zero)
            {
                entity.Moved = true;
                Vector3 move = entity.MoveDirection * entity.Speed.CurrentValue * Time.deltaTime;
                move.y = entity.Rigidbody.linearVelocity.y;
                entity.Rigidbody.linearVelocity = move;
            }
            else
            {
                entity.Moved = false;
                entity.Rigidbody.linearVelocity = Vector3.zero;
            }

            // Нокдаун — по истечении таймера встаём автоматически
            if (entity.CurrentLayingReason == Entity.LayingReason.KnockedDown)
            {
                _knockdownTimer -= Time.deltaTime;
                if (_knockdownTimer <= 0f)
                {
                    fsm.SetState<FSM_StateStandPlayer>();
                }
            }
        }

        public override void Exit()
        {
            entity.Rigidbody.linearVelocity = Vector3.zero;
            entity.CurrentLayingReason = Entity.LayingReason.None;
            entity.Speed.UpdateScaleCurrentValue(CrawlAdvancedValueMultiplier);
        }
    }
}