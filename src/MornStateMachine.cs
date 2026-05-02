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
            [SerializeReference] public List<StateBehaviour> behaviours = new();
        }
        [SerializeField] private List<StateNode> _nodes = new();
        private readonly List<StateBehaviour> _currentBehaviours = new();
        private readonly List<StateBehaviour> _updateBuffer = new();
        private int _pendingTransition = NotPending;
        private int _currentStateID;
        private const int NotPending = int.MinValue;
        public IReadOnlyList<StateNode> Nodes => _nodes;
        public List<StateNode> NodesMutable => _nodes;
        public IReadOnlyList<StateBehaviour> CurrentBehaviours => _currentBehaviours;
        public int CurrentStateID => _currentStateID;
        private void Awake() {
            _currentBehaviours.Clear();
            _updateBuffer.Clear();
            _pendingTransition = NotPending;
            _currentStateID = 0;
            ReinjectOwners();
            foreach(var n in _nodes) foreach(var b in n.behaviours) if(b != null) b.RebuildStateLinkCache();
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
        public void ReinjectOwners() {
            foreach(var n in _nodes) {
                if(n.behaviours == null) continue;
                foreach(var b in n.behaviours) {
                    if(b == null) continue;
                    b.SetOwner(this);
                    b.StateID = n.id;
                }
            }
        }
        private void FlushPending() {
            if(_pendingTransition == NotPending) return;
            var nextID = _pendingTransition;
            _pendingTransition = NotPending;
            var nextNode = FindNode(nextID);
            if(nextNode == null) {
                Debug.LogWarning($"[MornState] StateID {nextID} not found on {name}.",this);
                return;
            }
            foreach(var b in _currentBehaviours) b?.InternalEnd();
            _currentBehaviours.Clear();
            _currentStateID = nextID;
            foreach(var b in nextNode.behaviours) {
                if(b == null) continue;
                _currentBehaviours.Add(b);
                b.InternalBegin();
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
