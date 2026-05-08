namespace FinalStateMachine
{
    public class FSM_State
    {
        protected readonly Fsm Fsm;

        public FSM_State(Fsm fsm)
        {
            Fsm = fsm;
        }

        public virtual void Enter() { }
        public virtual void Update() { }
        public virtual void Exit() { }
    }
}