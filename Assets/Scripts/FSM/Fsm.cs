using System;
using System.Collections.Generic;


namespace FinalStateMachine
{
    public class Fsm
    {
        public FSM_State StateCurrent { get; private set; }

        private Dictionary<Type, FSM_State> _states = new Dictionary<Type, FSM_State>();

        public void AddState(FSM_State state)
        {
            _states.Add(state.GetType(), state);
        }


        public void SetState<T>() where T : FSM_State
        {
            var type = typeof(T);

            if (StateCurrent != null && StateCurrent.GetType() == type)
            {
                return;
            }

            if (_states.TryGetValue(type, out var newState))
            {
                StateCurrent?.Exit();

                StateCurrent = newState;

                StateCurrent.Enter();
            }
        }

        public void Update()
        {
            StateCurrent?.Update();
        }
    }
}

