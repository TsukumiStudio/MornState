using UnityEngine;
using Random = UnityEngine.Random;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Transition")]
    internal class WaitRandomTimeCurveState : MornStateBehaviour
    {
        [SerializeField] private AnimationCurve _curve;
        [SerializeField] private Connection _next;
        private float _transitionTime;

        public override void OnStateBegin()
        {
            _transitionTime = Time.time +_curve.Evaluate(Random.value);
        }

        public override void OnStateUpdate()
        {
            if (Time.time >= _transitionTime) Transition(_next);
        }
    }
}