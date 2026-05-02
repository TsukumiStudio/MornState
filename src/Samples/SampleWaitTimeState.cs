using UnityEngine;
namespace MornLib.Samples {
    public class SampleWaitTimeState : StateBehaviour {
        [SerializeField] private float _waitSeconds = 1f;
        [SerializeField] private StateLink _next;
        private float _elapsed;
        public override void OnStateBegin() {
            _elapsed = 0f;
        }
        public override void OnStateUpdate() {
            _elapsed += Time.deltaTime;
            if(_elapsed >= _waitSeconds) Transition(_next);
        }
    }
}
