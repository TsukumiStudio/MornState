using System;
using UnityEngine;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Action")]
    internal class TransformDeltaState : MornStateBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private Vector3 _positionPerSec;
        [SerializeField] private Vector3 _rotationPerSec;
        [SerializeField] private Vector3 _scalePerSec;
        [SerializeField] private bool _useUnscaledTime;

        public override void OnStateUpdate()
        {
            var dt = _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            _target.localPosition += _positionPerSec * dt;
            _target.localEulerAngles += _rotationPerSec * dt;
            _target.localScale += _scalePerSec * dt;
        }
    }
}
