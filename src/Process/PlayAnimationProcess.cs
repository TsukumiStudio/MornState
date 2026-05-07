using System;
using UnityEngine;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Process")]
    internal class PlayAnimationProcess : ProcessBase
    {
        [SerializeField] private BindAnimatorClip _bind;
        [SerializeField] private float _duration;
        [SerializeField] private Connection _onComplete;
        private float _startTime;
        public override float Progress =>
            _bind.Clip ? Mathf.Clamp01((Time.time - _startTime) * _bind.Animator.speed / _bind.Clip.length) : 1f;

        public override void OnStateBegin()
        {
            _bind.Animator.CrossFadeInFixedTime(_bind.Clip.name, _duration);
            _startTime = Time.time;
        }

        public override void OnStateUpdate()
        {
            if (Progress >= 1f)
            {
                Transition(_onComplete);
            }
        }
    }
}