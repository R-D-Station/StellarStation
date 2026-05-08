using FinalStateMachine;
using System;
using UnityEngine;
using ValuesEye;

public class Entity : MonoBehaviour
{
	[SerializeField] public bool DisableMovement;
	[SerializeField] public bool IgnoreCollision;

	[SerializeField] public bool CanBeDragCarried = true;

	[SerializeField] public Rigidbody2D _rigidbody2D;

	protected Vector2 _currentTile;
	protected string _playerName = "";

	[Header("BaseValue")]
    public AdvansedValue _speed = new AdvansedValue(100.0f);
	public Vector2 MoveDirection;
	public bool Moved = false;

    public Fsm Fsm;

	protected virtual void CreateFSM()
	{
		Fsm = new Fsm();
	}
}
