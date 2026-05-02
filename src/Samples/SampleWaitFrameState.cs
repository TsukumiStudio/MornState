using UnityEngine;
using System;
namespace MornLib.Samples {
    [Serializable]
    public class SampleWaitFrameState : MornStateBehaviour {
        [SerializeField] private int _frames = 30;
        [SerializeField] private Connection _next;
        private int _elapsed;
        public override void OnStateBegin() {
            _elapsed = 0;
        }
        public override void OnStateUpdate() {
            _elapsed++;
            if(_elapsed >= _frames) Transition(_next);
        }
    }
}
