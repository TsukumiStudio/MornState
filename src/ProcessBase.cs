using System;
namespace MornLib {
    [Serializable]
    public abstract class ProcessBase : MornStateBehaviour {
        public abstract float Progress { get; }
    }
}
