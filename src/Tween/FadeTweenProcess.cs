using System;
using UnityEngine;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Tween")]
    internal class FadeTweenProcess : ProcessBase
    {
        [SerializeField] private CanvasGroup _target;
        [SerializeField] private SpriteRenderer _target2;
        [SerializeField] private float _duration;
        [SerializeField] private float _endValue;
        [SerializeField] private MornEaseType _easeType;
        [SerializeField] private StateLink _nextState;
        private float _startTime;
        private float _startValue1;
        private float _startValue2;
        public override float Progress => Mathf.Clamp01((Time.time - _startTime) / _duration);

        public override void OnStateBegin()
        {
            _startTime = Time.time;
            if (_target != null) _startValue1 = _target.alpha;
            if (_target2 != null) _startValue2 = _target2.color.a;
        }

        public override void OnStateUpdate()
        {
            var rate = Mathf.Clamp01((Time.time - _startTime) / _duration);
            rate = rate.Ease(_easeType);
            if (_target2 != null)
            {
                var value = Mathf.Lerp(_startValue2, _endValue, rate);
                var color = _target2.color;
                color.a = value;
                _target2.color = color;
            }

            if (_target != null)
            {
                var value = Mathf.Lerp(_startValue1, _endValue, rate);
                _target.alpha = value;
            }

            if (rate >= 1)
            {
                Transition(_nextState);
            }
        }
    }
}