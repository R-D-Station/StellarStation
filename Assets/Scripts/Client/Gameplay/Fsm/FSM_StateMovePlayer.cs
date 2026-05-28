using UnityEngine;
using UnityEngine.InputSystem;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Fsm
{
    public class FSM_StateMovePlayer : FSM_State
    {
        private Player _player;

        public FSM_StateMovePlayer(FSM fsm, Entity entity) : base(fsm)
        {
            _player = entity as Player;
        }

        public override void Enter()
        {
            if (_player != null)
            {
                _player.Moved = true;

                // Подписываемся на отмену движения
                _player.GetPlayerControl().Player.Move.canceled += StopMove;
            }
        }

        public override void Update()
        {
            if (_player == null) return;

            Vector2 move = _player.MoveDirection * _player.Speed.CurrentValue * Time.deltaTime;
            _player.Rigidbody2D.linearVelocity = move;
        }

        public override void Exit()
        {
            if (_player != null)
            {
                _player.Rigidbody2D.linearVelocity = Vector2.zero;
                _player.Moved = false;

                // Отписываемся
                _player.GetPlayerControl().Player.Move.canceled -= StopMove;
            }
        }

        private void StopMove(InputAction.CallbackContext context)
        {
            fsm.SetState<FSM_StateStandPlayer>();
        }
    }
}