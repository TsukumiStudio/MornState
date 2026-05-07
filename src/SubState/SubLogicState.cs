using System;
using UnityEngine;
#if USE_VCONTAINER
using VContainer.Unity;
#endif
namespace MornLib {
    [Serializable]
    [MornStateMenu("SubState")]
    public sealed class SubLogicState : SubBase {
        public enum SourceMode {
            Instantiate,
            SceneReference,
        }
        [SerializeField] private SourceMode _mode = SourceMode.Instantiate;
        [SerializeField] private MornStateMachineInternal _prefab;
        [SerializeField] private Transform _parent;
        [SerializeField] private MornStateMachineInternal _sceneInstance;
        [SerializeField] private bool _forceAutoDestroy;
#if USE_VCONTAINER
        [VContainer.Inject] private VContainer.IObjectResolver _resolver;
#endif
        private MornStateMachineInternal _runtime;
        private bool _ownsRuntime;
        protected override MornStateMachineInternal AcquireMachine() {
            switch(_mode) {
                case SourceMode.Instantiate:
                    if(_runtime != null) return _runtime;
                    if(_prefab == null) return null;
                    _runtime = InstantiateMachine(_prefab, _parent);
                    _ownsRuntime = true;
                    return _runtime;
                case SourceMode.SceneReference:
                    if(_sceneInstance == null) return null;
                    _runtime = _sceneInstance;
                    _ownsRuntime = false;
                    return _runtime;
            }
            return null;
        }
        protected override void ReleaseMachine(bool autoDestroy) {
            if(_runtime == null) return;
            var shouldDestroy = autoDestroy || _forceAutoDestroy;
            if(!shouldDestroy) return;
            _runtime.enabled = false;
            if(_ownsRuntime) {
                Destroy(_runtime.gameObject);
            }
            _runtime = null;
            _ownsRuntime = false;
        }
        private MornStateMachineInternal InstantiateMachine(MornStateMachineInternal prefab, Transform parent) {
#if USE_VCONTAINER
            if(_resolver != null) return _resolver.Instantiate(prefab, parent);
#endif
            return parent != null ? UnityEngine.Object.Instantiate(prefab, parent) : UnityEngine.Object.Instantiate(prefab);
        }
    }
}
