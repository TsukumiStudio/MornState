using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MornLib
{
    /// <summary>
    /// MornStateMachine をプログラマブルに編集するユーティリティ。
    /// AI / CLI から GraphView を経由せずに State 追加 / 接続変更 / behaviour 追加ができる。
    /// 全 API は Editor 専用、Undo 登録 + Dirty 設定 + 親シーン保存マーク済み。
    /// </summary>
    public static class MornStateEditUtil
    {
        private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // ---- Lookup ----

        public static MornStateMachine FindFsmByName(string fsmName)
        {
            return UnityEngine.Object.FindObjectsByType<MornStateMachine>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .FirstOrDefault(f => f.name == fsmName);
        }

        public static MornStateMachine.StateNode FindStateByName(MornStateMachine fsm,string stateName)
        {
            return fsm == null ? null : fsm.NodesMutable.FirstOrDefault(n => n.name == stateName);
        }

        // ---- State ops ----

        /// <summary>新しい State を追加して node を返す。stateID は既存の最大+1 で自動採番。</summary>
        public static MornStateMachine.StateNode AddState(MornStateMachine fsm,string stateName)
        {
            if(fsm == null) throw new ArgumentNullException(nameof(fsm));
            Undo.RegisterCompleteObjectUndo(fsm,$"Add State: {stateName}");
            var newID = (fsm.Nodes.Count == 0 ? 0 : fsm.Nodes.Max(n => n.id)) + 1;
            var node = new MornStateMachine.StateNode { id = newID,name = stateName };
            fsm.NodesMutable.Add(node);
            MarkDirty(fsm);
            return node;
        }

        public static void RemoveState(MornStateMachine fsm,int stateID)
        {
            if(fsm == null) return;
            Undo.RegisterCompleteObjectUndo(fsm,$"Remove State: id={stateID}");
            fsm.UnregisterNode(stateID);
            // 残った Connection.stateID が消えた id を指していたら 0 にクリア
            foreach(var n in fsm.NodesMutable)
            {
                foreach(var b in n.behaviours)
                {
                    if(b == null) continue;
                    foreach(var f in b.GetType().GetFields(FieldFlags))
                    {
                        if(f.GetValue(b) is Connection c && c.stateID == stateID) c.stateID = 0;
                    }
                }
            }
            MarkDirty(fsm);
        }

        public static void RenameState(MornStateMachine fsm,int stateID,string newName)
        {
            var node = fsm?.FindNode(stateID);
            if(node == null) return;
            Undo.RegisterCompleteObjectUndo(fsm,"Rename State");
            node.name = newName;
            MarkDirty(fsm);
        }

        // ---- Behaviour ops ----

        /// <summary>type 名 (full or short) を指定して behaviour インスタンスを生成・追加する。</summary>
        public static MornStateBehaviour AddBehaviour(MornStateMachine fsm,int stateID,string behaviourTypeName)
        {
            var node = fsm?.FindNode(stateID);
            if(node == null) throw new ArgumentException($"State id={stateID} not found");
            var type = ResolveBehaviourType(behaviourTypeName);
            if(type == null) throw new ArgumentException($"Type '{behaviourTypeName}' not found among MornStateBehaviour subclasses");
            Undo.RegisterCompleteObjectUndo(fsm,$"Add Behaviour: {type.Name}");
            var b = (MornStateBehaviour)Activator.CreateInstance(type);
            node.behaviours.Add(b);
            MarkDirty(fsm);
            return b;
        }

        public static void RemoveBehaviour(MornStateMachine fsm,int stateID,int behaviourIndex)
        {
            var node = fsm?.FindNode(stateID);
            if(node == null || behaviourIndex < 0 || behaviourIndex >= node.behaviours.Count) return;
            Undo.RegisterCompleteObjectUndo(fsm,"Remove Behaviour");
            node.behaviours.RemoveAt(behaviourIndex);
            MarkDirty(fsm);
        }

        // ---- Connection ops ----

        /// <summary>fromState の指定 behaviour の指定 Connection field を toState に接続する。fieldName は Connection 型 field の名前。</summary>
        public static void Connect(MornStateMachine fsm,int fromStateID,int behaviourIndex,string fieldName,int toStateID)
        {
            var node = fsm?.FindNode(fromStateID);
            if(node == null) throw new ArgumentException($"From state id={fromStateID} not found");
            if(behaviourIndex < 0 || behaviourIndex >= node.behaviours.Count) throw new ArgumentException($"behaviourIndex {behaviourIndex} out of range");
            if(toStateID != 0 && fsm.FindNode(toStateID) == null) throw new ArgumentException($"To state id={toStateID} not found");
            var b = node.behaviours[behaviourIndex];
            if(b == null) throw new ArgumentException("behaviour is null");
            var field = b.GetType().GetField(fieldName,FieldFlags);
            if(field == null || field.FieldType != typeof(Connection)) throw new ArgumentException($"Field '{fieldName}' is not a Connection on {b.GetType().Name}");
            Undo.RegisterCompleteObjectUndo(fsm,"Connect");
            var c = (Connection)field.GetValue(b);
            if(c == null) { c = new Connection(); field.SetValue(b,c); }
            c.stateID = toStateID;
            MarkDirty(fsm);
        }

        public static void Disconnect(MornStateMachine fsm,int fromStateID,int behaviourIndex,string fieldName)
        {
            Connect(fsm,fromStateID,behaviourIndex,fieldName,0);
        }

        // ---- Field set ----

        /// <summary>behaviour の serialized field に値を設定。primitive / Enum / string / UnityEngine.Object 系を想定。</summary>
        public static void SetField(MornStateMachine fsm,int stateID,int behaviourIndex,string fieldName,object value)
        {
            var node = fsm?.FindNode(stateID);
            if(node == null) throw new ArgumentException("state not found");
            var b = node.behaviours[behaviourIndex];
            var field = b.GetType().GetField(fieldName,FieldFlags);
            if(field == null) throw new ArgumentException($"Field '{fieldName}' not found");
            Undo.RegisterCompleteObjectUndo(fsm,"SetField");
            // Enum coercion
            object coerced = value;
            if(field.FieldType.IsEnum && value is string str)
            {
                coerced = Enum.Parse(field.FieldType,str,true);
            }
            else if(field.FieldType == typeof(float) && value is double d) coerced = (float)d;
            else if(field.FieldType == typeof(int) && value is long lv) coerced = (int)lv;
            field.SetValue(b,coerced);
            MarkDirty(fsm);
        }

        // ---- Listing ----

        /// <summary>MornStateBehaviour 派生 type を全列挙 (uloop からの "what types are available?" 用)。</summary>
        public static string[] ListAvailableBehaviourTypes()
        {
            return TypeCache.GetTypesDerivedFrom<MornStateBehaviour>()
                .Where(t => t.IsAbstract == false)
                .Select(t => t.FullName)
                .OrderBy(n => n)
                .ToArray();
        }

        // ---- Helpers ----

        private static Type ResolveBehaviourType(string typeName)
        {
            var all = TypeCache.GetTypesDerivedFrom<MornStateBehaviour>().Where(t => t.IsAbstract == false).ToArray();
            return all.FirstOrDefault(t => t.FullName == typeName)
                ?? all.FirstOrDefault(t => t.Name == typeName);
        }

        private static void MarkDirty(MornStateMachine fsm)
        {
            EditorUtility.SetDirty(fsm);
            if(fsm.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(fsm.gameObject.scene);
            }
        }
    }
}
