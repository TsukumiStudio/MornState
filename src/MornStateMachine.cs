using System;
using System.Collections.Generic;
using UnityEngine;
namespace MornLib {
    public class MornStateMachine : MornStateMachineInternal {
        [Serializable]
        public class StateNode {
            public int id;
            public string name;
            [SerializeReference] public List<MornStateBehaviour> behaviours = new();
        }
        [SerializeField] private List<StateNode> _nodes = new();
        private readonly List<MornStateBehaviour> _currentBehaviours = new();
        private readonly List<MornStateBehaviour> _updateBuffer = new();
        private readonly Dictionary<int,int> _stateEnterCounts = new();
        private readonly Dictionary<(int from,int to),int> _transitionCounts = new();
        private int _pendingTransition = NotPending;
        private int _currentStateID;
        private int _transitionFrame = -1;
        private int _transitionCountThisFrame;
        private const int NotPending = int.MinValue;
        private const int MaxTransitionsPerFrame = 64;
        public IReadOnlyList<StateNode> Nodes => _nodes;
        public List<StateNode> NodesMutable => _nodes;
        public IReadOnlyList<MornStateBehaviour> CurrentBehaviours => _currentBehaviours;
        public int CurrentStateID => _currentStateID;
        public IReadOnlyDictionary<int,int> StateEnterCounts => _stateEnterCounts;
        public IReadOnlyDictionary<(int from,int to),int> TransitionCounts => _transitionCounts;
        private void Awake() {
            _currentBehaviours.Clear();
            _updateBuffer.Clear();
            _pendingTransition = NotPending;
            _currentStateID = 0;
            _stateEnterCounts.Clear();
            _transitionCounts.Clear();
            ReinjectOwners();
            foreach(var n in _nodes) foreach(var b in n.behaviours) if(b != null) b.RebuildConnectionCache();
            foreach(var n in _nodes) foreach(var b in n.behaviours) if(b != null) b.OnAwake();
        }
        private void Start() {
            if(_playOnStart) Transition(_startStateID);
        }
        private void Update() {
            _updateBuffer.Clear();
            _updateBuffer.AddRange(_currentBehaviours);
            var stateAtStart = _currentStateID;
            foreach(var b in _updateBuffer) {
                if(_currentStateID != stateAtStart) break;
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
#if USE_VCONTAINER
        [VContainer.Inject]
        public void Construct(VContainer.IObjectResolver resolver) {
            foreach(var n in _nodes) {
                if(n.behaviours == null) continue;
                foreach(var b in n.behaviours) {
                    if(b == null) continue;
                    resolver.Inject(b);
                }
            }
        }
#endif
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
            while(_pendingTransition != NotPending) {
                var frame = Time.frameCount;
                if(_transitionFrame != frame) {
                    _transitionFrame = frame;
                    _transitionCountThisFrame = 0;
                }
                _transitionCountThisFrame++;
                if(_transitionCountThisFrame > MaxTransitionsPerFrame) {
                    Debug.LogError($"[MornState] Transition loop detected on {name}: exceeded {MaxTransitionsPerFrame} transitions in a single frame. Aborting.",this);
                    _pendingTransition = NotPending;
                    return;
                }
                var nextID = _pendingTransition;
                _pendingTransition = NotPending;
                var nextNode = FindNode(nextID);
                if(nextNode == null) {
                    Debug.LogWarning($"[MornState] StateID {nextID} not found on {name}.",this);
                    return;
                }
                var prevID = _currentStateID;
                foreach(var b in _currentBehaviours) b.InternalEnd();
                _currentBehaviours.Clear();
                _currentStateID = nextID;
                _stateEnterCounts.TryGetValue(nextID,out var ec);
                _stateEnterCounts[nextID] = ec + 1;
                if(prevID != 0) {
                    var key = (prevID,nextID);
                    _transitionCounts.TryGetValue(key,out var tc);
                    _transitionCounts[key] = tc + 1;
                }
                foreach(var b in nextNode.behaviours) {
                    if(b == null) continue;
                    if(_currentStateID != nextID) break;
                    _currentBehaviours.Add(b);
                    b.InternalBegin();
                }
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
