using System;
using UnityEngine;
using VContainer;
using VContainer.Unity;
namespace MornLib {
    [Serializable]
    [MornStateMenu("SubState")]
    public sealed class SubUIState : SubBase {
        [Inject] private IObjectResolver _resolver;
        [SerializeField] private MornStateMachineInternal _prefab;
        [SerializeField, NoLabel] private MornStateUILayerType _layerType = new() { Key = "Main" };
        [SerializeField] private bool _forceAutoDestroy;
        private MornStateMachineInternal _runtime;
        protected override MornStateMachineInternal AcquireMachine() {
            if(_runtime != null) return _runtime;
            if(_prefab == null) return null;
            var parent = FindParent();
            if(parent == null) {
                Debug.LogError($"[SubUIState] MornUIParent not found in scene. Layer: {_layerType}");
                return null;
            }
            _runtime = _resolver.Instantiate(_prefab, parent.transform);
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
        private MornUIParent FindParent() {
            var parents = UnityEngine.Object.FindObjectsByType<MornUIParent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach(var parent in parents) {
                if(parent == null) continue;
                if(parent.LayerType != _layerType) continue;
                return parent;
            }
            return null;
        }
    }
}
