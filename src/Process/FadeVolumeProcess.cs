using UnityEngine;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Process")]
    internal class FadeVolumeProcess : ProcessBase
    {
        [SerializeField] private AudioSource _source;
        [SerializeField] [Range(0, 1f)] private float _endValue;
        [SerializeField] private float _duration = 1;
        private float _startTime = -1;
        private float _startValue;
        public override float Progress => _startTime < 0 ? 0 :
            _duration > 0 ? Mathf.Clamp01((Time.time - _startTime) / _duration) : 1;

        public override void OnStateBegin()
        {
            _startTime = Time.time;
            _startValue = _source.volume;
        }

        public override void OnStateUpdate()
        {
            var t = Mathf.Clamp01((Time.time - _startTime) / _duration);
            _source.volume = Mathf.Lerp(_startValue, _endValue, t);
        }
    }
}