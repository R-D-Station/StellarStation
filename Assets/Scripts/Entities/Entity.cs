using FinalStateMachine;
using System;
using UnityEngine;
using ValuesEye;

public class Entity : MonoBehaviour
{
	[SerializeField] public bool DisableMovement;
	[SerializeField] public bool IgnoreCollision;

	[SerializeField] public bool CanBeDragCarried = true;

	[SerializeField] CharacterController _characterController;

	protected Vector2 _currentTile;
	protected string _playerName = "";

	[Header("BaseValue")]
    public AdvansedValue _speed = new AdvansedValue(1.0f);
	public Vector2 MoveDirection;
	public bool Moved = false;

    public Fsm Fsm;


	protected virtual void CreateFSM()
	{
		Fsm = new Fsm();

		Fsm.AddState(new FSM_StateMovePlayer(Fsm, this));
		Fsm.AddState(new FSM_StateStandPlayer(Fsm, this));
	}
}
