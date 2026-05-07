using UnityEngine;
using Random = UnityEngine.Random;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Transition")]
    internal class WaitRandomTimeState : MornStateBehaviour
    {
        [SerializeField] private float _min;
        [SerializeField] private float _max;
        [SerializeField] private Connection _next;
        private float _transitionTime;

        public override void OnStateBegin()
        {
            _transitionTime = Time.time + Random.Range(_min, _max);
        }

        public override void OnStateUpdate()
        {
            if (Time.time >= _transitionTime) Transition(_next);
        }
    }
}