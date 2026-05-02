using UnityEngine;
namespace MornLib {
    public abstract class MornStateMachineInternal : MonoBehaviour {
        [SerializeField] protected bool _playOnStart = true;
        [SerializeField] protected int _startStateID;
        public bool playOnStart {
            get => _playOnStart;
            set => _playOnStart = value;
        }
        public int startStateID {
            get => _startStateID;
            set => _startStateID = value;
        }
        public abstract void Transition(int stateID);
    }
}
