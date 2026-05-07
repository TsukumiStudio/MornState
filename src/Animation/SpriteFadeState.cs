using UnityEngine;
using Random = UnityEngine.Random;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Animation")]
    internal class SpriteFadeState : MornStateBehaviour
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private float _minDuration = 1f;
        [SerializeField] private float _maxDuration = 2f;
        [SerializeField] private Connection _end;
        
        private float _startTime;
        private float _duration;
        public override void OnStateBegin()
        {
            _startTime = Time.time;
            _duration = Random.Range(_minDuration, _maxDuration);
        }

        public override void OnStateUpdate()
        {
            var t = Mathf.Clamp01((Time.time - _startTime) / _duration);
            var rate = Mathf.Cos(t * Mathf.PI * 2) + 1;
            rate *= 0.5f;
            _renderer.color = new Color(1, 1, 1, 1 - rate);
            
            if (t >= 1)
            {
                Transition(_end);
            }
        }
    }
}