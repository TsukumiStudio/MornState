using System.Collections.Generic;
using UnityEngine;
namespace MornLib {
    public class MornStateMachine : MornStateMachineInternal {
        private readonly Dictionary<int,StateBehaviour> _states = new();
        private StateBehaviour _current;
        private int _pendingTransition = NotPending;
        private const int NotPending = int.MinValue;
        public StateBehaviour CurrentState => _current;
        private void Awake() {
            CollectStates();
            foreach(var state in _states.Values) state.RebuildStateLinkCache();
        }
        private void Start() {
            if(_playOnStart) Transition(_startStateID);
        }
        private void Update() {
            if(_current != null) _current.InternalUpdate();
            FlushPending();
        }
        private void LateUpdate() {
            FlushPending();
        }
        public override void Transition(int stateID) {
            _pendingTransition = stateID;
            FlushPending();
        }
        private void FlushPending() {
            if(_pendingTransition == NotPending) return;
            var nextID = _pendingTransition;
            _pendingTransition = NotPending;
            if(_states.TryGetValue(nextID,out var next) == false) {
                Debug.LogWarning($"[MornState] StateID {nextID} not found on {name}.",this);
                return;
            }
            _current?.InternalEnd();
            _current = next;
            _current.InternalBegin();
        }
        private void CollectStates() {
            _states.Clear();
            foreach(var state in GetComponentsInChildren<StateBehaviour>(true)) {
                if(state.Owner != this) continue;
                _states[state.StateID] = state;
            }
        }
        public IReadOnlyDictionary<int,StateBehaviour> States => _states;
    }
}
