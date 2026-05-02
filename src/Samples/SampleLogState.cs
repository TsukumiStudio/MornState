using UnityEngine;
namespace MornLib.Samples {
    public class SampleLogState : StateBehaviour {
        [SerializeField] private string _message = "Hello from MornState";
        [SerializeField] private StateLink _next;
        public override void OnStateBegin() {
            Debug.Log($"[MornState] {_message}",Owner);
            Transition(_next);
        }
    }
}
