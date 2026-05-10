using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Process")]
    internal class ProcessEnd : MornStateBehaviour
    {
        [SerializeField] private StateLink _nextState;
        private readonly List<ProcessBase> _processList = new();

        public override void OnStateBegin()
        {
            _processList.Clear();
            foreach (var behaviour in GetBehaviours<MornStateBehaviour>())
            {
                if (behaviour is ProcessBase process)
                {
                    _processList.Add(process);
                }
            }
        }

        public override void OnStateUpdate()
        {
            if (_processList.All(x => x.Progress >= 1))
            {
                Transition(_nextState);
            }
        }
    }
}