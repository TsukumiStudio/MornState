using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
namespace MornLib {
    [Serializable]
    public abstract class StateBehaviour {
        [SerializeField,HideInInspector] private int _stateID;
        [NonSerialized] private MornStateMachine _owner;
        [NonSerialized] private CancellationTokenSource _cts;
        [NonSerialized] private List<StateLink> _linkCache;
        public int StateID {
            get => _stateID;
            set => _stateID = value;
        }
        public MornStateMachine Owner => _owner;
        public GameObject gameObject => _owner != null ? _owner.gameObject : null;
        public Transform transform => _owner != null ? _owner.transform : null;
        public string name => _owner != null ? _owner.name : string.Empty;
        public CancellationToken CancellationTokenOnEnd {
            get {
                if(_cts == null) _cts = new CancellationTokenSource();
                return _cts.Token;
            }
        }
        internal void SetOwner(MornStateMachine owner) {
            _owner = owner;
        }
        internal void InternalBegin() {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            OnStateBegin();
        }
        internal void InternalUpdate() {
            OnStateUpdate();
        }
        internal void InternalEnd() {
            OnStateEnd();
            if(_cts != null) {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }
        public virtual void OnStateBegin() {}
        public virtual void OnStateUpdate() {}
        public virtual void OnStateEnd() {}
        protected virtual void OnValidate() {}
        public Coroutine StartCoroutine(IEnumerator routine) => _owner != null ? _owner.StartCoroutine(routine) : null;
        public void StopCoroutine(Coroutine routine) { if(_owner != null && routine != null) _owner.StopCoroutine(routine); }
        public void StopCoroutine(IEnumerator routine) { if(_owner != null && routine != null) _owner.StopCoroutine(routine); }
        public T GetComponent<T>() => _owner != null ? _owner.GetComponent<T>() : default;
        protected static void Destroy(UnityEngine.Object obj) { if(obj != null) UnityEngine.Object.Destroy(obj); }
        protected static void DestroyImmediate(UnityEngine.Object obj) { if(obj != null) UnityEngine.Object.DestroyImmediate(obj); }
        public void Transition(StateLink link) {
            if(link == null) return;
            if(_owner == null) return;
            link.transitionCount++;
            _owner.Transition(link.stateID);
        }
        public void RebuildStateLinkCache() {
            _linkCache ??= new List<StateLink>();
            _linkCache.Clear();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach(var field in GetType().GetFields(flags)) {
                if(field.FieldType != typeof(StateLink)) continue;
                if(field.GetValue(this) is StateLink link) _linkCache.Add(link);
            }
        }
        public IReadOnlyList<StateLink> GetStateLinks() {
            if(_linkCache == null) RebuildStateLinkCache();
            return _linkCache;
        }
        public IEnumerable<T> GetBehaviours<T>() where T : StateBehaviour {
            if(_owner == null) yield break;
            foreach(var n in _owner.Nodes) {
                foreach(var b in n.behaviours) {
                    if(b is T t) yield return t;
                }
            }
        }
    }
}
