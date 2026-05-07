using System;
using UnityEngine;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Action")]
    internal class SetCanvasGroupActiveState : MornStateBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private bool _isActive;

        public override void OnStateBegin()
        {
            _canvasGroup.SetActive(_isActive);
        }
    }
}