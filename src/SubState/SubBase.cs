using System;
using System.Collections.Generic;
using UnityEngine;
namespace MornLib {
    [Serializable]
    public abstract class SubBase : MornStateBehaviour {
        [SerializeField, HideInInspector] private List<ExitCodeLink> _exitCodeLinks = new();
        private readonly Dictionary<string, Connection> _lookup = new();
        private SubStateController _controller;
        private bool _autoDestroy;
        public IReadOnlyList<ExitCodeLink> ExitCodeLinks => _exitCodeLinks;
        public void SetExitCodes(IReadOnlyList<ExitCode> exitCodes) {
            var preserved = new Dictionary<string, Connection>();
            foreach(var link in _exitCodeLinks) {
                var key = link.ExitCode.ToString();
                if(!string.IsNullOrEmpty(key)) preserved[key] = link.Next;
            }
            _exitCodeLinks.Clear();
            foreach(var code in exitCodes) {
                preserved.TryGetValue(code.ToString(), out var next);
                _exitCodeLinks.Add(new ExitCodeLink {
                    ExitCode = code,
                    Next = next ?? new Connection(),
                });
            }
            RebuildConnectionCache();
        }
        public sealed override void OnStateBegin() {
            _autoDestroy = false;
            _lookup.Clear();
            foreach(var link in _exitCodeLinks) {
                var key = link.ExitCode.ToString();
                if(!string.IsNullOrEmpty(key)) _lookup[key] = link.Next;
            }
            var machine = AcquireMachine();
            if(machine == null) {
                Debug.LogError($"[Sub] {GetType().Name} could not acquire a sub state machine.", gameObject);
                return;
            }
            _controller = machine.gameObject.GetComponent<SubStateController>()
                       ?? machine.gameObject.AddComponent<SubStateController>();
            _controller.OnExitRequested += HandleExitRequested;
            machine.playOnStart = true;
            machine.enabled = true;
            machine.Transition(machine.startStateID);
            _controller.NotifyEnter();
        }
        public sealed override void OnStateEnd() {
            if(_controller != null) {
                _controller.OnExitRequested -= HandleExitRequested;
                _controller.NotifyExitCompleted();
                _controller = null;
            }
            ReleaseMachine(_autoDestroy);
            _lookup.Clear();
        }
        private void HandleExitRequested(ExitCode exitCode, bool autoDestroy) {
            _autoDestroy = autoDestroy;
            if(_lookup.TryGetValue(exitCode.ToString(), out var next)) {
                Transition(next);
            } else {
                Debug.LogError($"[Sub] ExitCode '{exitCode}' is not registered on {GetType().Name}.", gameObject);
            }
        }
        protected abstract MornStateMachineInternal AcquireMachine();
        protected abstract void ReleaseMachine(bool autoDestroy);
        /// <summary>Editor の Reload で SubStateExitState を発見するための参照元。
        /// Instantiate モードなら prefab、SceneReference モードなら scene instance を返す実装にする。</summary>
        protected virtual MornStateMachineInternal GetExitSourceMachine() => null;
        [Button("Linkクリア")]
        public void Clear() {
            SetExitCodes(System.Array.Empty<ExitCode>());
        }
        [Button("Link再読み込み")]
        public void Reload() {
            var src = GetExitSourceMachine();
            if(!(src is MornStateMachine fsm)) return;
            var list = new List<ExitCode>();
            foreach(var node in fsm.NodesMutable) {
                if(node.behaviours == null) continue;
                foreach(var b in node.behaviours) {
                    if(b is SubStateExitState exit) list.Add(exit.ExitCode);
                }
            }
            SetExitCodes(list);
        }
    }
}
