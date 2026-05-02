using System;
using UnityEngine;
namespace MornLib {
    public enum TransitionTiming {
        Immediate,
        LateUpdate,
        NextFrame,
    }
    [Serializable]
    public class StateLink {
        [SerializeField] private int _stateID;
        [SerializeField] private TransitionTiming _transitionTiming = TransitionTiming.Immediate;
        [SerializeField] private Color _lineColor = Color.white;
        [SerializeField] private string _name = string.Empty;
        [NonSerialized] private int _transitionCount;
        public event Action onTransitionCountChanged;
        public int stateID {
            get => _stateID;
            set => _stateID = value;
        }
        public TransitionTiming transitionTiming {
            get => _transitionTiming;
            set => _transitionTiming = value;
        }
        public Color lineColor {
            get => _lineColor;
            set => _lineColor = value;
        }
        public string name {
            get => _name;
            set => _name = value;
        }
        public int transitionCount {
            get => _transitionCount;
            set {
                if(_transitionCount == value) return;
                _transitionCount = value;
                onTransitionCountChanged?.Invoke();
            }
        }
    }
}
