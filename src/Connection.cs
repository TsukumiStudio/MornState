using System;
using UnityEngine;
namespace MornLib {
    [Serializable]
    public class Connection {
        [SerializeField] private int _stateID;
        public int stateID {
            get => _stateID;
            set => _stateID = value;
        }
        public bool IsConnected => _stateID != 0;
    }
}
