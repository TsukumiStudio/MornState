using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MornLib
{
    [CustomPropertyDrawer(typeof(MornStateBehaviour),true)]
    public sealed class MornStateBehaviourPropertyDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            BuildFields(root,property,skipStateLinks: false);
            BuildMethodAttributes(root,property);
            return root;
        }

        public static void BuildFields(VisualElement parent,SerializedProperty behaviourProperty,bool skipStateLinks)
            => BuildFields(parent,behaviourProperty,skipStateLinks,null);

        public static void BuildFields(
            VisualElement parent,
            SerializedProperty behaviourProperty,
            bool skipStateLinks,
            System.Action onChanged)
        {
            var captured = behaviourProperty.Copy();
            parent.Add(new IMGUIContainer(() => DrawFieldsImgui(captured,skipStateLinks,onChanged)));
        }

        private static void DrawFieldsImgui(
            SerializedProperty behaviourProperty,
            bool skipStateLinks,
            System.Action onChanged)
        {
            var so = behaviourProperty.serializedObject;
            if(so == null || so.targetObject == null)
            {
                EditorApplication.delayCall += () => Selection.activeObject = null;
                return;
            }
            try
            {
                so.Update();
                var beforeStateLinkSig = skipStateLinks ? ComputeStateLinkSig(behaviourProperty) : 0;
                EditorGUI.BeginChangeCheck();
                var target = ResolveTarget(behaviourProperty);
                var iter = behaviourProperty.Copy();
                var end = iter.GetEndProperty();
                if(iter.NextVisible(true))
                {
                    do
                    {
                        if(SerializedProperty.EqualContents(iter,end)) break;
                        if(skipStateLinks && IsStateLink(iter)) continue;
                        if(DrawRangeFieldIfNeeded(iter,target) == false)
                        {
                            EditorGUILayout.PropertyField(iter,true);
                        }
                    } while(iter.NextVisible(false));
                }
                var changed = EditorGUI.EndChangeCheck();
                so.ApplyModifiedProperties();
                if(changed && onChanged != null)
                {
                    so.Update();
                    var afterStateLinkSig = skipStateLinks ? ComputeStateLinkSig(behaviourProperty) : 0;
                    if(skipStateLinks == false || beforeStateLinkSig != afterStateLinkSig)
                    {
                        EditorApplication.delayCall += () => onChanged();
                    }
                }
            }
            catch(System.ArgumentException e) when(IsImguiLayoutMismatch(e)) { }
            catch(System.ObjectDisposedException) { }
            catch(System.InvalidOperationException) { }
            catch(System.NullReferenceException) { }
        }

        public static void BuildMethodAttributes(VisualElement parent,SerializedProperty behaviourProperty)
            => BuildMethodAttributes(parent,behaviourProperty,null);

        public static void BuildMethodAttributes(
            VisualElement parent,
            SerializedProperty behaviourProperty,
            System.Action onChanged)
        {
            var target = ResolveTarget(behaviourProperty);
            if(target == null) return;
            if(HasCustomAttributeMethods(target.GetType()) == false) return;
            var ownerObject = behaviourProperty.serializedObject.targetObject;
            parent.Add(new IMGUIContainer(() => {
                try
                {
                    var so = behaviourProperty.serializedObject;
                    if(so == null || so.targetObject == null) return;
                    var beforeStateLinkSig = onChanged != null ? ComputeStateLinkSig(behaviourProperty) : 0;
                    MornEditorDrawerUtil.HandleCustomAttributesForObject(target,ownerObject);
                    if(onChanged == null) return;
                    if(so.targetObject == null) return;
                    so.Update();
                    var afterStateLinkSig = ComputeStateLinkSig(behaviourProperty);
                    if(beforeStateLinkSig != afterStateLinkSig) {
                        EditorApplication.delayCall += () => onChanged();
                    }
                }
                catch(System.ArgumentException e) when(IsDestroyedSerializedObject(e)) { }
                catch(System.ObjectDisposedException) { }
                catch(System.InvalidOperationException) { }
                catch(System.NullReferenceException) { }
            }));
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

        private static bool IsStateLink(SerializedProperty prop)
        {
            return prop.propertyType == SerializedPropertyType.Generic
                   && prop.type == nameof(StateLink);
        }

        private static bool DrawRangeFieldIfNeeded(SerializedProperty prop,object target)
        {
            if(target == null) return false;
            var field = FindField(target.GetType(),prop.name);
            var range = field?.GetCustomAttribute<RangeAttribute>();
            if(range == null) return false;
            var label = new GUIContent(prop.displayName);
            if(prop.propertyType == SerializedPropertyType.Float)
            {
                prop.floatValue = DrawFloatSlider(label,prop.floatValue,range.min,range.max);
                return true;
            }
            if(prop.propertyType == SerializedPropertyType.Integer)
            {
                prop.intValue = DrawIntSlider(label,prop.intValue,Mathf.RoundToInt(range.min),Mathf.RoundToInt(range.max));
                return true;
            }
            return false;
        }

        private static float DrawFloatSlider(GUIContent label,float value,float min,float max)
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label,GUILayout.Width(EditorGUIUtility.labelWidth));
                value = GUILayout.HorizontalSlider(value,min,max,GUILayout.MinWidth(55));
                value = EditorGUILayout.FloatField(value,GUILayout.Width(44));
            }
            return Mathf.Clamp(value,min,max);
        }

        private static int DrawIntSlider(GUIContent label,int value,int min,int max)
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label,GUILayout.Width(EditorGUIUtility.labelWidth));
                value = Mathf.RoundToInt(GUILayout.HorizontalSlider(value,min,max,GUILayout.MinWidth(55)));
                value = EditorGUILayout.IntField(value,GUILayout.Width(44));
            }
            return Mathf.Clamp(value,min,max);
        }

        private static int ComputeStateLinkSig(SerializedProperty behaviourProperty)
        {
            unchecked
            {
                var hash = 17;
                var iter = behaviourProperty.Copy();
                var end = iter.GetEndProperty();
                if(iter.NextVisible(true))
                {
                    do
                    {
                        if(SerializedProperty.EqualContents(iter,end)) break;
                        if(IsStateLink(iter) == false) continue;
                        hash = hash * 31 + iter.propertyPath.GetHashCode();
                        hash = hash * 31 + ReadRelativeInt(iter,"_stateID");
                        hash = hash * 31 + ReadRelativeString(iter,"_name").GetHashCode();
                    } while(iter.NextVisible(false));
                }
                return hash;
            }
        }

        private static int ReadRelativeInt(SerializedProperty prop,string name)
        {
            var child = prop.FindPropertyRelative(name);
            return child != null ? child.intValue : 0;
        }

        private static string ReadRelativeString(SerializedProperty prop,string name)
        {
            var child = prop.FindPropertyRelative(name);
            return child != null ? child.stringValue ?? "" : "";
        }

        private static bool IsImguiLayoutMismatch(System.ArgumentException e)
        {
            return e.Message.Contains("Getting control")
                   && e.Message.Contains("position in a group with only");
        }

        private static bool IsDestroyedSerializedObject(System.ArgumentException e)
        {
            return e.Message.Contains("SerializedObject target has been destroyed");
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
            var field = FindField(type,name);
            return field != null ? field.GetValue(obj) : null;
        }

        private static FieldInfo FindField(System.Type type,string name)
        {
            while(type != null)
            {
                var f = type.GetField(name,BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if(f != null) return f;
                type = type.BaseType;
            }
            return null;
        }
    }
}
