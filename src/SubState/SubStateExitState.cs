using System;
using UnityEngine;
namespace MornLib {
    [Serializable]
    [MornStateMenu("SubState")]
    public sealed class SubStateExitState : MornStateBehaviour {
        [SerializeField] private ExitCode _exitCode;
        [SerializeField] private bool _autoDestroy = true;
        public ExitCode ExitCode => _exitCode;
        public bool AutoDestroy => _autoDestroy;
        public override void OnStateBegin() {
            var controller = GetComponent<SubStateController>();
            if(controller != null) {
                controller.NotifyExit(_exitCode, _autoDestroy);
            } else if(_autoDestroy && gameObject != null) {
                Destroy(gameObject);
            }
        }
    }
}
