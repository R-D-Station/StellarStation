using UnityEngine;
using static Entity;

namespace FinalStateMachine
{
    public class FSM_StateLayingPlayer : FSM_State
    {
        protected Player _entity;
        private const float CrawlAdvancedValueMultiplier = 0.3f;

        private float _knockdownTimer;

        public FSM_StateLayingPlayer(Fsm fsm, Entity entity) : base(fsm)
        {
            _entity = entity.GetComponent<Player>();
        }

        public override void Enter()
        {
            // Если зашли из-за нокдауна — запускаем таймер
            if (_entity.CurrentLayingReason == LayingReason.KnockedDown)
            {
                _knockdownTimer = _entity.KnockdownDuration;
            }
            else
            {
                _knockdownTimer = 0f;
            }

            _entity.Speed.UpdateScaleCurrentValue(-CrawlAdvancedValueMultiplier);
            // тут позже: переключить спрайт на "лежачий", уменьшить хитбокс и т.д.
        }

        public override void Update()
        {
            // Ползание
            if (_entity.MoveDirection != Vector3.zero)
            {
                Vector2 move = _entity.MoveDirection * _entity.Speed.CurrentValue * Time.deltaTime;
                _entity.Rigidbody.linearVelocity = move;
            }
            else
            {
                _entity.Rigidbody.linearVelocity = Vector3.zero;
            }

            // Если лежим из-за нокдауна — отсчитываем таймер
            if (_entity.CurrentLayingReason == LayingReason.KnockedDown)
            {
                _knockdownTimer -= Time.deltaTime;
                if (_knockdownTimer <= 0f)
                {
                    // Таймер истёк — встаём автоматически
                    Fsm.SetState<FSM_StateStandPlayer>();
                }
            }
            // Если лежим добровольно — ждём нажатия F (обрабатывается в Player.OnToggleLaying)
        }

        public override void Exit()
        {
            _entity.Rigidbody.linearVelocity = Vector3.zero;
            _entity.CurrentLayingReason = LayingReason.None;
            _entity.Speed.UpdateScaleCurrentValue(CrawlAdvancedValueMultiplier);
        }
    }
}