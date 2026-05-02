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
    }
}
