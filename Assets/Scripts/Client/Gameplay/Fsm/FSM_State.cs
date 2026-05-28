namespace Client.Gameplay.Fsm;

public class FSM_State
{
    protected readonly FSM fsm;

    public FSM_State(FSM fsm)
    {
        this.fsm = fsm;
    }

    public virtual void Enter() { }
    public virtual void Update() { }
    public virtual void Exit() { }
}