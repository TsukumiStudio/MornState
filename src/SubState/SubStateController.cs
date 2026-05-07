using System;
using UnityEngine;
namespace MornLib {
    public sealed class SubStateController : MonoBehaviour {
        public event Action<ExitCode, bool> OnExitRequested;
        public event Action OnEntered;
        public event Action OnExitCompleted;
        private bool _consumed;
        public void NotifyEnter() { OnEntered?.Invoke(); }
        public void NotifyExit(ExitCode exitCode, bool autoDestroy) {
            if(_consumed) return;
            _consumed = true;
            if(OnExitRequested != null) {
                OnExitRequested.Invoke(exitCode, autoDestroy);
            } else if(autoDestroy) {
                Destroy(gameObject);
            }
        }
        public void NotifyExitCompleted() {
            OnExitCompleted?.Invoke();
            OnExitCompleted = null;
            OnEntered = null;
            OnExitRequested = null;
            _consumed = false;
        }
    }
}
