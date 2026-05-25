using System;
using UnityEngine;
using UnityEngine.UI;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Tween")]
    internal class FadeState : ProcessBase
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _image;
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private float _duration;
        [SerializeField, Range(0, 1f)] private float _endValue;
        [SerializeField] private MornEaseType _easeType;
        [SerializeField] private StateLink _nextState;
        private float _startTime;
        private float _canvasGroupStartValue;
        private float _imageStartValue;
        private float _spriteRendererStartValue;

        public override float Progress => _duration > 0.0f ? Mathf.Clamp01((Time.time - _startTime) / _duration) : 1.0f;

        public override void OnStateBegin()
        {
            _startTime = Time.time;
            if (_canvasGroup != null)
            {
                _canvasGroupStartValue = _canvasGroup.alpha;
            }

            if (_image != null)
            {
                _imageStartValue = _image.color.a;
            }

            if (_spriteRenderer != null)
            {
                _spriteRendererStartValue = _spriteRenderer.color.a;
            }
        }

        public override void OnStateUpdate()
        {
            var rate = Progress;
            rate = rate.Ease(_easeType);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = Mathf.Lerp(_canvasGroupStartValue, _endValue, rate);
            }

            if (_image != null)
            {
                var color = _image.color;
                color.a = Mathf.Lerp(_imageStartValue, _endValue, rate);
                _image.color = color;
            }

            if (_spriteRenderer != null)
            {
                var color = _spriteRenderer.color;
                color.a = Mathf.Lerp(_spriteRendererStartValue, _endValue, rate);
                _spriteRenderer.color = color;
            }

            if (rate >= 1)
            {
                Transition(_nextState);
            }
        }
    }
}
