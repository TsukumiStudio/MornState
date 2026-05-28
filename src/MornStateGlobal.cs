using System.Collections.Generic;
using UnityEngine;

namespace MornLib
{
    [CreateAssetMenu(fileName = nameof(MornStateGlobal), menuName = "MornState/" + nameof(MornStateGlobal))]
    public sealed class MornStateGlobal : MornGlobalBase<MornStateGlobal>
    {
        protected override string ModuleName => "MornState";
        [SerializeField] private List<string> _uiLayers = new() { "Back", "Main", "Front", "Popup" };
        public string[] UILayers => _uiLayers.ToArray();
    }
}
