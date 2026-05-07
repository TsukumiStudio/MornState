using UnityEditor;
using UnityEngine;
namespace MornLib {
    [CustomPropertyDrawer(typeof(ExitCode))]
    internal sealed class ExitCodePropertyDrawer : PropertyDrawer {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            var value = property.FindPropertyRelative("_value");
            value.stringValue = EditorGUI.TextField(position, label, value.stringValue);
        }
    }
    [CustomPropertyDrawer(typeof(ExitCodeLink))]
    internal sealed class ExitCodeLinkDrawer : PropertyDrawer {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            var exitCodeProperty = property.FindPropertyRelative("_exitCode");
            var valueProperty = exitCodeProperty.FindPropertyRelative("_value");
            if(!string.IsNullOrEmpty(valueProperty.stringValue)) {
                label.text = valueProperty.stringValue;
            }
            EditorGUI.PropertyField(position, exitCodeProperty, label);
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
