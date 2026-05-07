using UnityEngine;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Action")]
    internal class TransitionState : MornStateBehaviour
    {
        [SerializeField] private Connection _nextState;

        public override void OnStateBegin()
        {
            Transition(_nextState);
        }
    }
}