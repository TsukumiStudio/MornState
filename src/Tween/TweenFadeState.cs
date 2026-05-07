using UnityEngine;
using System;
using UnityEngine.UI;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Tween")]
    internal class TweenFadeState : MornStateBehaviour
    {
        [SerializeField] private Image _image;
        [SerializeField] private float _duration;
        [SerializeField] private float _endValue;
        [SerializeField] private Connection _nextState;
        private float _startTime;
        private float _startValue;

        public override void OnStateBegin()
        {
            _startTime = Time.time;
            _startValue = _image.color.a;
        }

        public override void OnStateUpdate()
        {
            var t = Mathf.Clamp01((Time.time - _startTime) / _duration);
            var alpha = Mathf.Lerp(_startValue, _endValue, t);
            _image.color = new Color(_image.color.r, _image.color.g, _image.color.b, alpha);
            if (t >= 1)
            {
                Transition(_nextState);
            }
        }
    }
}