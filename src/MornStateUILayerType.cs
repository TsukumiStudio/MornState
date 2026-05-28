using System;

namespace MornLib
{
    [Serializable]
    public sealed class MornStateUILayerType : MornEnumBase
    {
        public override string[] Values => MornStateGlobal.I.UILayers;
        public override UnityEngine.Object PingTarget => MornStateGlobal.I;
    }
}
