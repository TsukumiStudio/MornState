using UnityEngine;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Action")]
    internal class SetActiveState : MornStateBehaviour
    {
        [SerializeField] private GameObject _target;
        [SerializeField] private bool _isActive;

        public override void OnStateBegin()
        {
            _target.SetActive(_isActive);
        }
    }
}