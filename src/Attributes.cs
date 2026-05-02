using System;
using UnityEngine;
namespace MornLib {
    [AttributeUsage(AttributeTargets.Method,AllowMultiple = false)]
    public class ButtonAttribute : Attribute {
        public string Label { get; }
        public ButtonAttribute(string label) {
            Label = label;
        }
    }
    [AttributeUsage(AttributeTargets.Field,AllowMultiple = false)]
    public class HideIfAttribute : PropertyAttribute {
        public string FieldName { get; }
        public HideIfAttribute(string fieldName) {
            FieldName = fieldName;
        }
    }
    [AttributeUsage(AttributeTargets.Field,AllowMultiple = false)]
    public class ShowIfAttribute : PropertyAttribute {
        public string FieldName { get; }
        public ShowIfAttribute(string fieldName) {
            FieldName = fieldName;
        }
    }
}
