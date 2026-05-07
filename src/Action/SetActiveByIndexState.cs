using UnityEngine;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Action")]
    internal class SetActiveByIndexState : MornStateBehaviour
    {
        [SerializeField] private Transform _parent;
        [SerializeField] private int _activeIndex;

        public override void OnStateBegin()
        {
            for (var i = 0; i < _parent.childCount; i++)
            {
                _parent.GetChild(i).gameObject.SetActive(i == _activeIndex);
            }
        }
    }
}