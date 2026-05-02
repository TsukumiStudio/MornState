using System;
using UnityEngine;
namespace MornLib {
    [Serializable]
    public class StateLink {
        [SerializeField] private int _stateID;
        public int stateID {
            get => _stateID;
            set => _stateID = value;
        }
    }
}
