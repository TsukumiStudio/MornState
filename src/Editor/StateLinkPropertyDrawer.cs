using UnityEditor;
using UnityEngine;
namespace MornLib {
    [CustomPropertyDrawer(typeof(StateLink),true)]
    public sealed class StateLinkPropertyDrawer : PropertyDrawer {
        public override float GetPropertyHeight(SerializedProperty property,GUIContent label) => 0f;
        public override void OnGUI(Rect position,SerializedProperty property,GUIContent label) { }
    }
}
