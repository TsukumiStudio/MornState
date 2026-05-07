using UnityEngine;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Process")]
    internal class WaitFrameProgress : ProcessBase
    {
        [SerializeField] private int _frame;
        [SerializeField] private Connection _next;
        private int _elapsedFrame;
        public override float Progress => _frame > 0 ? Mathf.Clamp01((float)_elapsedFrame / _frame) : 1;

        public override void OnStateBegin()
        {
            _elapsedFrame = 0;
        }

        public override void OnStateUpdate()
        {
            _elapsedFrame++;
            if (_elapsedFrame >= _frame)
            {
                Transition(_next);
            }
        }
    }
}