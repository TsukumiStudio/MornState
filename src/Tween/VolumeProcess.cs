using System;
using UnityEngine;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Tween")]
    internal class VolumeProcess : ProcessBase
    {
        [SerializeField] private AudioSource _source;
        [SerializeField] [Range(0, 1f)] private float _endValue;
        [SerializeField] private float _duration = 1f;
        [SerializeField] private bool _toPlay;
        [SerializeField] private bool _toStop;
        private float _startTime;
        private float _startValue;
        public override float Progress => _duration > 0 ? Mathf.Clamp01((Time.time - _startTime) / _duration) : 1;

        public override void OnStateBegin()
        {
            _startTime = Time.time;
            _startValue = _source.volume;
            if (_toPlay)
            {
                _source.Play();
            }
        }

        public override void OnStateUpdate()
        {
            var t = Mathf.Clamp01((Time.time - _startTime) / _duration);
            _source.volume = Mathf.Lerp(_startValue, _endValue, t);
        }

        public override void OnStateEnd()
        {
            _source.volume = _endValue;
            if (_toStop)
            {
                _source.Stop();
            }
        }
    }
}