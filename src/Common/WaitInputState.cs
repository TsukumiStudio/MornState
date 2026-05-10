using UnityEngine;
using System;
using UnityEngine.InputSystem;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Common")]
    internal class WaitInputState : MornStateBehaviour
    {
        [SerializeField] private InputActionReference _inputAction;
        [SerializeField] private StateLink _next;

        public override void OnStateUpdate()
        {
            if (_inputAction.action.WasPerformedThisFrame())
            {
                Transition(_next);
            }
        }
    }
}