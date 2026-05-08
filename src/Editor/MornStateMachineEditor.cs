using UnityEditor;
using UnityEngine;
namespace MornLib {
    [CustomEditor(typeof(MornStateMachine),true)]
    [CanEditMultipleObjects]
    public class MornStateMachineEditor : Editor {
        public override void OnInspectorGUI() {
            using(new EditorGUI.DisabledScope(true)) {
                var script = MonoScript.FromMonoBehaviour(target as MonoBehaviour);
                EditorGUILayout.ObjectField("Script",script,typeof(MonoScript),false);
            }
            EditorGUILayout.Space();
            if(GUILayout.Button("Export FSM (Copy to Clipboard)",GUILayout.Height(24))) {
                var text = MornStateExportUtil.Export(target as MornStateMachine);
                EditorGUIUtility.systemCopyBuffer = text;
                Debug.Log(text);
            }
            if(GUILayout.Button("Open Graph",GUILayout.Height(28))) {
                MornStateMachineGraphWindow.OpenFor(target as MornStateMachine);
            }
            // 派生クラスの [Button] / [OnInspectorGUI] を描画
            MornEditorDrawerUtil.HandleCustomAttributesForObject(target,target);
        }
    }
}
