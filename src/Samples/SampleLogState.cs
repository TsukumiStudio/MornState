using UnityEngine;
using System;
namespace MornLib.Samples {
    [Serializable]
    public class SampleLogState : MornStateBehaviour {
        [SerializeField] private string _message = "Hello from MornState";
        [SerializeField] private Connection _next;
        public override void OnStateBegin() {
            Debug.Log($"[MornState] {_message}",Owner);
            Transition(_next);
        }
    }
}
