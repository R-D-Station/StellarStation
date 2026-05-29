using UnityEngine;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Fsm
{
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
            // Если зашли из-за нокдауна — запускаем таймер
            if (entity.CurrentLayingReason == Entity.LayingReason.KnockedDown)
            {
                _knockdownTimer = entity.KnockdownDuration;
            }
            else
            {
                _knockdownTimer = 0f;
            }

            entity.Speed.UpdateScaleCurrentValue(-CrawlAdvancedValueMultiplier);
            // тут позже: переключить спрайт на "лежачий", уменьшить хитбокс и т.д.
        }

        public override void Update()
        {
            // Ползание
            if (entity.MoveDirection != Vector3.zero)
            {
                Vector2 move = entity.MoveDirection * entity.Speed.CurrentValue * Time.deltaTime;
                entity.Rigidbody.linearVelocity = move;
            }
            else
            {
                entity.Rigidbody.linearVelocity = Vector3.zero;
            }

            // Если лежим из-за нокдауна — отсчитываем таймер
            if (entity.CurrentLayingReason == Entity.LayingReason.KnockedDown)
            {
                _knockdownTimer -= Time.deltaTime;
                if (_knockdownTimer <= 0f)
                {
                    // Таймер истёк — встаём автоматически
                    fsm.SetState<FSM_StateStandPlayer>();
                }
            }
            // Если лежим добровольно — ждём нажатия F (обрабатывается в Player.OnToggleLaying)
        }

        public override void Exit()
        {
            entity.Rigidbody.linearVelocity = Vector3.zero;
            entity.CurrentLayingReason = Entity.LayingReason.None;
            entity.Speed.UpdateScaleCurrentValue(CrawlAdvancedValueMultiplier);
        }
    }
}