using System;
using UnityEngine;
using Client.Gameplay.Util.AdvancedValues;
using Client.Gameplay.Fsm;

namespace Client.Gameplay.Entities
{
    public class Entity : MonoBehaviour
    {
        [SerializeField] public bool DisableMovement;
        [SerializeField] public bool IgnoreCollision;
        [SerializeField] public bool IsSprintHeld;

        [SerializeField] public bool CanBeDragCarried = true;

        [SerializeField] public Rigidbody Rigidbody;

        protected Vector2 _currentTile;
        protected string _playerName = "";

        // В будущем изменить на безопасный класс
        [Header("Status")]
        public float StunDuration = 0f;
        public float KnockdownDuration = 0f;

        public enum LayingReason
        {
            None,
            Voluntary,    // сам лёг по F
            KnockedDown,  // сбили с ног
            Unconscious,  // потерял сознание
        }

        public LayingReason CurrentLayingReason = LayingReason.None;

        [Header("BaseValue")]
        public AdvancedValue Speed;

        public Vector3 MoveDirection;
        public bool Moved = false;
        public enum Direction
        {
            North,  // +Z
            South,  // -Z
            East,   // +X
            West    // -X
        }
        public Direction Facing = Direction.South;

        public FSM Fsm;

        protected virtual void CreateFSM()
        {
            Fsm = new FSM();
        }
    }
}
