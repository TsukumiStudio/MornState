using UnityEngine;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Transition")]
    internal class WaitTimeState : MornStateBehaviour
    {
        [SerializeField] private float _waitDuration;
        [SerializeField] private StateLink _next;
        private float _elapsedFrame;

        public override void OnStateBegin()
        {
            _elapsedFrame = 0;
        }

        public override void OnStateUpdate()
        {
            _elapsedFrame += Time.deltaTime;
            if (_elapsedFrame >= _waitDuration) Transition(_next);
        }
    }
}