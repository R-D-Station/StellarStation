using System;
using UnityEngine;
using Client.Gameplay.AdvansedValues;
using Client.Gameplay.Fsm;

namespace Client.Gameplay.Entities;

public class Entity : MonoBehaviour
{
	[SerializeField] 
	public bool DisableMovement;

	[SerializeField] 
	public bool IgnoreCollision;

	[SerializeField] 
	public bool CanBeDragCarried = true;

	[SerializeField] 
	public Rigidbody2D Rigidbody2D;

	protected Vector2 currentTile;
	protected string playerName = "";

	[Header("BaseValue")]
    public AdvansedValue Speed = new AdvansedValue(100.0f);
	public Vector2 MoveDirection;
	public bool Moved = false;

    public FSM Fsm;

    private void Awake()
    {
        // Автоматически получаем Rigidbody2D, если не назначен в инспекторе
        if (Rigidbody2D == null)
            Rigidbody2D = GetComponent<Rigidbody2D>();
    }

    protected virtual void CreateFSM()
	{
		Fsm = new FSM();
	}
}
