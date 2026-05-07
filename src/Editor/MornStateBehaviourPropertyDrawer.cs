using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;

namespace MornLib
{
    [CustomPropertyDrawer(typeof(MornStateBehaviour),true)]
    public sealed class MornStateBehaviourPropertyDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            BuildFields(root,property,skipConnections: false);
            BuildMethodAttributes(root,property);
            return root;
        }

        public static void BuildFields(VisualElement parent,SerializedProperty behaviourProperty,bool skipConnections)
        {
            var captured = behaviourProperty.Copy();
            parent.Add(new IMGUIContainer(() => DrawFieldsImgui(captured,skipConnections)));
        }

        private static void DrawFieldsImgui(SerializedProperty behaviourProperty,bool skipConnections)
        {
            var so = behaviourProperty.serializedObject;
            so.Update();
            var iter = behaviourProperty.Copy();
            var end = iter.GetEndProperty();
            if(iter.NextVisible(true))
            {
                do
                {
                    if(SerializedProperty.EqualContents(iter,end)) break;
                    if(skipConnections && IsConnection(iter)) continue;
                    EditorGUILayout.PropertyField(iter,true);
                } while(iter.NextVisible(false));
            }
            so.ApplyModifiedProperties();
        }

        public static void BuildMethodAttributes(VisualElement parent,SerializedProperty behaviourProperty)
        {
            var target = ResolveTarget(behaviourProperty);
            if(target == null) return;
            if(HasCustomAttributeMethods(target.GetType()) == false) return;
            var ownerObject = behaviourProperty.serializedObject.targetObject;
            parent.Add(new IMGUIContainer(() => MornEditorDrawerUtil.HandleCustomAttributesForObject(target,ownerObject)));
        }

        private static bool HasCustomAttributeMethods(System.Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            foreach(var m in type.GetMethods(flags))
            {
                if(m.GetParameters().Length != 0) continue;
                if(m.GetCustomAttribute<ButtonAttribute>() != null) return true;
                if(m.GetCustomAttribute<OnInspectorGUIAttribute>() != null) return true;
            }
            return false;
        }

        private static bool IsConnection(SerializedProperty prop)
        {
            return prop.propertyType == SerializedPropertyType.Generic
                   && prop.type == nameof(Connection);
        }

        private static object ResolveTarget(SerializedProperty property)
        {
            object obj = property.serializedObject.targetObject;
            var path = property.propertyPath.Replace(".Array.data[","[");
            foreach(var token in path.Split('.'))
            {
                if(obj == null) return null;
                if(token.Contains("["))
                {
                    var name = token.Substring(0,token.IndexOf('['));
                    var idxStr = token.Substring(token.IndexOf('[') + 1,token.IndexOf(']') - token.IndexOf('[') - 1);
                    var idx = int.Parse(idxStr);
                    obj = GetField(obj,name);
                    if(obj is System.Collections.IList list && idx < list.Count) obj = list[idx];
                    else return null;
                }
                else
                {
                    obj = GetField(obj,token);
                }
            }
            return obj;
        }

        private static object GetField(object obj,string name)
        {
            if(obj == null) return null;
            var type = obj.GetType();
            while(type != null)
            {
                var f = type.GetField(name,BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if(f != null) return f.GetValue(obj);
                type = type.BaseType;
            }
            return null;
        }
    }
}
