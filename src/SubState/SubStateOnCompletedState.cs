using System;
using UnityEngine;
namespace MornLib {
    [Serializable]
    [MornStateMenu("SubState")]
    public sealed class SubStateOnCompletedState : MornStateBehaviour {
        [SerializeField] private Connection _onExit;
        private SubStateController _controller;
        public override void OnStateBegin() {
            if(gameObject == null) return;
            _controller = GetComponent<SubStateController>() ?? gameObject.AddComponent<SubStateController>();
            _controller.OnExitCompleted += HandleExitCompleted;
        }
        public override void OnStateEnd() {
            if(_controller != null) {
                _controller.OnExitCompleted -= HandleExitCompleted;
                _controller = null;
            }
        }
        private void HandleExitCompleted() {
            Transition(_onExit);
        }
    }
}
