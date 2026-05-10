using System;
using UnityEngine;
namespace MornLib {
    [Serializable]
    public class StateLink {
        [SerializeField] private int _stateID;
        [SerializeField] private string _name;
        public int stateID {
            get => _stateID;
            set => _stateID = value;
        }
        public string name {
            get => _name;
            set => _name = value;
        }
        public bool IsConnected => _stateID != 0;
    }
}
