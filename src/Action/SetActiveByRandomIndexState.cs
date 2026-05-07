using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Action")]
    internal class SetActiveByRandomIndexState : MornStateBehaviour
    {
        [SerializeField] private Transform _parent;
        [SerializeField] private Vector2Int _range;

        public override void OnStateBegin()
        {
            var activeIndex = Random.Range(_range.x, _range.y + 1);
            for (var i = 0; i < _parent.childCount; i++)
            {
                _parent.GetChild(i).gameObject.SetActive(i == activeIndex);
            }
        }
    }
}
