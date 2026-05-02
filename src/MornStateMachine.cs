using System;
using System.Collections.Generic;
using UnityEngine;
namespace MornLib {
    public class MornStateMachine : MornStateMachineInternal {
        [Serializable]
        public class StateNode {
            public int id;
            public string name;
            public Vector2 graphPosition;
        }
        [SerializeField] private List<StateNode> _nodes = new();
        private readonly Dictionary<int,List<StateBehaviour>> _statesByID = new();
        private readonly List<StateBehaviour> _currentBehaviours = new();
        private readonly List<StateBehaviour> _updateBuffer = new();
        private int _pendingTransition = NotPending;
        private const int NotPending = int.MinValue;
        public IReadOnlyList<StateNode> Nodes => _nodes;
        public IReadOnlyDictionary<int,List<StateBehaviour>> StatesByID => _statesByID;
        public IReadOnlyList<StateBehaviour> CurrentBehaviours => _currentBehaviours;
        private void Awake() {
            CollectStates();
            foreach(var pair in _statesByID) {
                foreach(var b in pair.Value) b.RebuildStateLinkCache();
            }
        }
        private void Start() {
            if(_playOnStart) Transition(_startStateID);
        }
        private void Update() {
            _updateBuffer.Clear();
            _updateBuffer.AddRange(_currentBehaviours);
            foreach(var b in _updateBuffer) {
                if(_currentBehaviours.Contains(b) == false) continue;
                b.InternalUpdate();
            }
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
            if(_statesByID.TryGetValue(nextID,out var nextList) == false) {
                Debug.LogWarning($"[MornState] StateID {nextID} not found on {name}.",this);
                return;
            }
            foreach(var b in _currentBehaviours) b.InternalEnd();
            _currentBehaviours.Clear();
            foreach(var b in nextList) {
                _currentBehaviours.Add(b);
                b.InternalBegin();
            }
        }
        public void CollectStates() {
            _statesByID.Clear();
            foreach(var s in GetComponents<StateBehaviour>()) {
                if(s.StateID == 0) continue;
                if(_statesByID.TryGetValue(s.StateID,out var list) == false) {
                    list = new List<StateBehaviour>();
                    _statesByID[s.StateID] = list;
                }
                list.Add(s);
            }
        }
        public StateNode FindNode(int id) {
            foreach(var n in _nodes) if(n.id == id) return n;
            return null;
        }
        public void RegisterNode(StateNode node) {
            if(FindNode(node.id) != null) return;
            _nodes.Add(node);
        }
        public void UnregisterNode(int id) {
            for(var i = _nodes.Count - 1;i >= 0;i--) if(_nodes[i].id == id) _nodes.RemoveAt(i);
        }
    }
}
