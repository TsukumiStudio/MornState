using UnityEngine;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Action")]
    internal class DestroyState : MornStateBehaviour
    {
        [SerializeField] private GameObject _target;

        public override void OnStateBegin()
        {
            if (_target == null)
            {
                return;
            }

            Destroy(_target);
        }
    }
}