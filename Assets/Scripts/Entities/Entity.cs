using FinalStateMachine;
using System;
using UnityEngine;

public class Entity : MonoBehaviour
{
	[SerializeField] public bool DisableMovement;
	[SerializeField] public bool IgnoreCollision;

	[SerializeField] public bool CanBeDragCarried = true;

	protected Vector2 _currentTile;
	protected string _playerName = "";

	public Fsm Fsm;

	protected virtual void CreateFSM()
	{
		Fsm = new Fsm();
	}
}
