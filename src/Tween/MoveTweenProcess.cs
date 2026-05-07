using System;
using UnityEngine;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Tween")]
    internal class MoveTweenProcess : ProcessBase
    {
        [SerializeField] private Transform _target;
        [SerializeField] private float _duration;
        [SerializeField] private Vector3 _endValue;
        [SerializeField] private MornEaseType _easeType;
        [SerializeField] private Connection _nextState;
        private float _startTime;
        private Vector3 _startValue;
        public override float Progress => Mathf.Clamp01((Time.time - _startTime) / _duration);

        public override void OnStateBegin()
        {
            _startTime = Time.time;
            _startValue = _target.position;
        }

        public override void OnStateUpdate()
        {
            var t = Mathf.Clamp01((Time.time - _startTime) / _duration);
            t = t.Ease(_easeType);
            var pos = Vector3.Lerp(_startValue, _endValue, t);
            _target.position = pos;
            if (t >= 1)
            {
                Transition(_nextState);
            }
        }
    }
}