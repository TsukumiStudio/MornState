using System;
using UnityEngine;
#if USE_VCONTAINER
using VContainer.Unity;
#endif
namespace MornLib {
    [Serializable]
    [MornStateMenu("SubState")]
    public sealed class SubUIState : SubBase {
        [SerializeField] private MornStateMachineInternal _prefab;
        [SerializeField] private bool _forceAutoDestroy;
#if USE_VCONTAINER
        [VContainer.Inject] private VContainer.IObjectResolver _resolver;
#endif
        private MornStateMachineInternal _runtime;
        protected override MornStateMachineInternal AcquireMachine() {
            if(_runtime != null) return _runtime;
            if(_prefab == null) return null;
            var parent = UnityEngine.Object.FindAnyObjectByType<UIParent>();
            if(parent == null) {
                Debug.LogError("[SubUIState] UIParent not found in scene.");
                return null;
            }
#if USE_VCONTAINER
            if(_resolver != null) {
                _runtime = _resolver.Instantiate(_prefab, parent.transform);
                return _runtime;
            }
#endif
            _runtime = UnityEngine.Object.Instantiate(_prefab, parent.transform);
            return _runtime;
        }
        protected override MornStateMachineInternal GetExitSourceMachine() => _prefab;
        protected override void ReleaseMachine(bool autoDestroy) {
            if(_runtime == null) return;
            var shouldDestroy = autoDestroy || _forceAutoDestroy;
            if(!shouldDestroy) return;
            _runtime.enabled = false;
            Destroy(_runtime.gameObject);
            _runtime = null;
        }
    }
}
