using UnityEngine;
using Client.Gameplay.Entities;

namespace Client.Gameplay.Fsm;

public class FSM_StateStandPlayer : FSM_State
{
    public Player Entity;

    public FSM_StateStandPlayer(FSM fsm, Entity entity) : base(fsm)
    {
        this.Entity = entity.GetComponent<Player>();
    }

    public override void Enter()
    {
        base.Enter();
    }
    public override void Update()
    {
        if (CheckMove()) { return; }
    }
    public override void Exit()
    {
        base.Exit();
    }

    bool CheckMove()
    {
        if (Entity.MoveDirection != Vector2.zero)
        {
            fsm.SetState<FSM_StateMovePlayer>();
            return true;
        }

        return false;
    }
}

