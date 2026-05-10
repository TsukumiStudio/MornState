using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Tween")]
    internal class ShakeRotationTweenProcess : ProcessBase
    {
        [SerializeField] private Transform _target;
        [SerializeField] private float _duration;
        [SerializeField] private int _fps;
        [SerializeField] private Vector3 _power;
        [SerializeField] private StateLink _nextState;
        private float _startTime;
        private float _nextTime;
        private Quaternion _baseRotation;
        public override float Progress => Mathf.Clamp01((Time.time - _startTime) / _duration);

        public override void OnStateBegin()
        {
            _startTime = Time.time;
            _nextTime =  Time.time;
            _baseRotation = _target.localRotation;
        }

        public override void OnStateUpdate()
        {
            if (Time.time >= _nextTime)
            {
                var shakeX = Random.Range(-_power.x, _power.x);
                var shakeY = Random.Range(-_power.y, _power.y);
                var shakeZ = Random.Range(-_power.z, _power.z);
                _target.localRotation = Quaternion.Euler(
                    _baseRotation.x + shakeX,
                    _baseRotation.y + shakeY,
                    _baseRotation.z + shakeZ);
                _nextTime += 1f / _fps;
            }

            var t = Mathf.Clamp01((Time.time - _startTime) / _duration);
            if (t >= 1)
            {
                _target.localRotation = _baseRotation;
                Transition(_nextState);
            }
        }
    }
}