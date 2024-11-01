using System.Collections.Generic;

/// <summary>
/// Light weight state machine
/// </summary>
/// <typeparam name="T"></typeparam>
class StateMachine<T>
{
    private class State
    {
        public T Id;
        public readonly StateFunc Enter;
        public readonly StateFunc Update;
        public readonly StateFunc Leave;

        public State(T id, StateFunc enter, StateFunc update, StateFunc leave)
        {
            Id = id;
            Enter = enter;
            Update = update;
            Leave = leave;
        }
    }

    private State _currentState;
    private readonly Dictionary<T, State> _states = new Dictionary<T, State>();

    public delegate void StateFunc();

    public void Add(T id, StateFunc enter, StateFunc update, StateFunc leave)
    {
        _states.Add(id, new State(id, enter, update, leave));
    }

    public T CurrentState()
    {
        return _currentState.Id;
    }

    public void Update()
    {
        _currentState.Update();
    }

    public void Shutdown()
    {
        if (_currentState != null && _currentState.Leave != null)
        {
            _currentState.Leave();
        }

        _currentState = null;
    }

    public void SwitchTo(T state)
    {
        GameDebug.Assert(_states.ContainsKey(state), $"Trying to switch to unknown state {state}");
        GameDebug.Assert(_currentState == null || !_currentState.Id.Equals(state),
            $"Trying to switch to {state} but that is already current state");

        var newState = _states[state];
        GameDebug.Log("Switching state: " + (_currentState != null ? _currentState.Id.ToString() : "null") + " -> " +
                      state);

        if (_currentState != null && _currentState.Leave != null)
        {
            _currentState.Leave();
        }

        if (newState.Enter != null)
        {
            newState.Enter();
        }

        _currentState = newState;
    }
}