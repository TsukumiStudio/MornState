using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
namespace MornLib {
    public abstract class StateBehaviour : MonoBehaviour {
        [SerializeField,HideInInspector] private int _stateID;
        private MornStateMachine _ownerCache;
        private CancellationTokenSource _cts;
        private List<StateLink> _linkCache;
        public int StateID {
            get => _stateID;
            set => _stateID = value;
        }
        public MornStateMachine Owner {
            get {
                if(_ownerCache == null) _ownerCache = GetComponent<MornStateMachine>();
                if(_ownerCache == null) _ownerCache = GetComponentInParent<MornStateMachine>(true);
                return _ownerCache;
            }
        }
        public CancellationToken CancellationTokenOnEnd {
            get {
                if(_cts == null) _cts = new CancellationTokenSource();
                return _cts.Token;
            }
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
        protected void Transition(StateLink link) {
            if(link == null) return;
            if(Owner == null) return;
            link.transitionCount++;
            Owner.Transition(link.stateID);
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
            if(Owner == null) yield break;
            foreach(var s in Owner.GetComponents<StateBehaviour>()) {
                if(s is T t) yield return t;
            }
        }
    }
}
