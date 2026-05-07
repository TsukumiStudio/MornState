using UnityEngine;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Common")]
    internal class WaitFrameState : MornStateBehaviour
    {
        [SerializeField] private int _frame;
        [SerializeField] private Connection _next;
        private int _elapsedFrame;

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