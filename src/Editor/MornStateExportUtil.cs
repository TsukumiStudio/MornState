using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// MornStateMachine の構造をプレーンテキストにダンプするユーティリティ。
    /// AI / 開発者がシーン YAML を読まずに FSM の全貌を把握できるようにする目的。
    /// </summary>
    public static class MornStateExportUtil
    {
        private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static string Export(MornStateMachine fsm)
        {
            if(fsm == null) return "(null)";
            var sb = new StringBuilder();
            var path = GetHierarchyPath(fsm.transform);
            sb.AppendLine($"FSM: {fsm.name}  (path: {path})");
            sb.AppendLine($"StartState: {ResolveName(fsm,fsm.GetType().GetField("_startStateID",FieldFlags) is FieldInfo f ? (int)f.GetValue(fsm) : 0)}");
            sb.AppendLine($"PlayOnStart: {fsm.GetType().GetField("_playOnStart",FieldFlags)?.GetValue(fsm)}");
            sb.AppendLine($"Nodes: {fsm.Nodes.Count}");
            sb.AppendLine();
            foreach(var node in fsm.Nodes)
            {
                sb.AppendLine($"[{node.name}] (id={node.id})");
                if(node.behaviours == null || node.behaviours.Count == 0)
                {
                    sb.AppendLine("  (no behaviours)");
                    sb.AppendLine();
                    continue;
                }
                foreach(var b in node.behaviours)
                {
                    if(b == null) { sb.AppendLine("  - (null)"); continue; }
                    sb.AppendLine($"  - {b.GetType().Name}");
                    DumpBehaviourFields(sb,fsm,b);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static void DumpBehaviourFields(StringBuilder sb,MornStateMachine fsm,MornStateBehaviour b)
        {
            foreach(var f in b.GetType().GetFields(FieldFlags))
            {
                if(f.IsDefined(typeof(NonSerializedAttribute),true)) continue;
                if(f.IsPrivate && f.IsDefined(typeof(SerializeField),true) == false) continue;
                var v = f.GetValue(b);
                if(v is Connection c)
                {
                    var targetName = c.IsConnected ? ResolveName(fsm,c.stateID) : "(none)";
                    sb.AppendLine($"      {f.Name} -> {targetName}");
                    continue;
                }
                if(v is IList list && list.Count > 0 && list[0] is Connection)
                {
                    sb.Append($"      {f.Name} -> [");
                    for(var i = 0;i < list.Count;i++)
                    {
                        var lc = (Connection)list[i];
                        sb.Append(lc.IsConnected ? ResolveName(fsm,lc.stateID) : "(none)");
                        if(i < list.Count - 1) sb.Append(", ");
                    }
                    sb.AppendLine("]");
                    continue;
                }
                var valStr = FormatValue(v);
                if(valStr != null) sb.AppendLine($"      {f.Name} = {valStr}");
            }
        }

        private static string FormatValue(object v)
        {
            if(v == null) return "null";
            if(v is string s) return $"\"{s}\"";
            if(v is bool || v is int || v is float || v is double || v is Enum) return v.ToString();
            if(v is UnityEngine.Object o) return o == null ? "null" : $"<{o.GetType().Name}: {o.name}>";
            return null;
        }

        private static string ResolveName(MornStateMachine fsm,int id)
        {
            if(id == 0) return "(none)";
            var node = fsm.FindNode(id);
            return node != null ? $"{node.name} (id={id})" : $"(missing id={id})";
        }

        private static string GetHierarchyPath(Transform t)
        {
            var sb = new StringBuilder();
            var cur = t;
            while(cur != null)
            {
                sb.Insert(0,"/" + cur.name);
                cur = cur.parent;
            }
            return sb.ToString();
        }

        [MenuItem("Tools/MornState/Export Selected FSM")]
        public static void ExportSelectedMenu()
        {
            var fsm = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<MornStateMachine>(true) : null;
            if(fsm == null)
            {
                Debug.LogWarning("[MornState] Export: 選択中の GameObject から MornStateMachine が見つからない");
                return;
            }
            var text = Export(fsm);
            Debug.Log(text,fsm);
            EditorGUIUtility.systemCopyBuffer = text;
            Debug.Log("[MornState] Export: クリップボードにコピーしました");
        }

        /// <summary>uloop / CLI 経由で呼びやすい "name 指定 export"。シーン中の MornStateMachine を name で検索して export 文字列を返す。</summary>
        public static string ExportByName(string fsmName)
        {
            var all = UnityEngine.Object.FindObjectsByType<MornStateMachine>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            foreach(var fsm in all)
            {
                if(fsm.name == fsmName) return Export(fsm);
            }
            return $"(no MornStateMachine named '{fsmName}' in active scene)";
        }
    }
}
