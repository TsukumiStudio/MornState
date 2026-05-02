using UnityEditor;
using UnityEngine;
namespace MornLib {
    [CustomEditor(typeof(MornStateMachine),true)]
    [CanEditMultipleObjects]
    public class MornStateMachineEditor : Editor {
        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if(GUILayout.Button("Open Graph",GUILayout.Height(28))) {
                var fsm = target as MornStateMachine;
                MornStateMachineGraphWindow.OpenFor(fsm);
            }
        }
    }
}
