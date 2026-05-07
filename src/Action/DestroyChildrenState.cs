using UnityEngine;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Action")]
    internal class DestroyChildrenState : MornStateBehaviour
    {
        [SerializeField] private Transform _parent;

        public override void OnStateBegin()
        {
            for (var i = _parent.childCount - 1; i >= 0; i--)
            {
                Destroy(_parent.GetChild(i).gameObject);
            }
        }
    }
}