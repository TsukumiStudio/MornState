using UnityEngine;

namespace MornLib
{
    public class MornUIParent : MonoBehaviour
    {
        [SerializeField, NoLabel] private MornStateUILayerType _layerType = new() { Key = "Main" };
        public MornStateUILayerType LayerType => _layerType;
    }
}
