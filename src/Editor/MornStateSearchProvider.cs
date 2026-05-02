using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
namespace MornLib {
    public class MornStateSearchProvider : ScriptableObject,ISearchWindowProvider {
        private MornStateMachineGraphView _view;
        private EditorWindow _window;
        public void Setup(MornStateMachineGraphView view,EditorWindow window) {
            _view = view;
            _window = window;
        }
        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context) {
            var types = new List<Type>();
            foreach(var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                Type[] all;
                try { all = asm.GetTypes(); } catch { continue; }
                foreach(var t in all) {
                    if(t.IsAbstract) continue;
                    if(typeof(StateBehaviour).IsAssignableFrom(t) == false) continue;
                    types.Add(t);
                }
            }
            types.Sort((a,b) => string.Compare(a.FullName,b.FullName,StringComparison.Ordinal));
            var root = new Node { Name = "Create State" };
            foreach(var t in types) {
                var node = root;
                var parts = string.IsNullOrEmpty(t.Namespace)
                    ? Array.Empty<string>()
                    : t.Namespace.Split('.');
                foreach(var p in parts) {
                    if(node.Children.TryGetValue(p,out var child) == false) {
                        child = new Node { Name = p };
                        node.Children[p] = child;
                    }
                    node = child;
                }
                node.Children[t.Name] = new Node { Name = t.Name,Type = t };
            }
            var entries = new List<SearchTreeEntry>();
            Emit(root,0,entries);
            return entries;
        }
        private static void Emit(Node node,int level,List<SearchTreeEntry> output) {
            if(node.Type != null) {
                output.Add(new SearchTreeEntry(new GUIContent(node.Name)) {
                    level = level,
                    userData = node.Type,
                });
                return;
            }
            output.Add(new SearchTreeGroupEntry(new GUIContent(node.Name),level));
            foreach(var kv in node.Children.OrderBy(kv => kv.Key,StringComparer.Ordinal)) {
                Emit(kv.Value,level + 1,output);
            }
        }
        public bool OnSelectEntry(SearchTreeEntry entry,SearchWindowContext context) {
            if(entry.userData is Type t == false) return false;
            if(_view == null || _window == null) return false;
            var local = _window.rootVisualElement.ChangeCoordinatesTo(
                _window.rootVisualElement.parent,
                context.screenMousePosition - _window.position.position);
            var graphPos = _view.contentViewContainer.WorldToLocal(local);
            _view.CreateStateAt(t,graphPos);
            return true;
        }
        private class Node {
            public string Name;
            public Type Type;
            public readonly SortedDictionary<string,Node> Children = new(StringComparer.Ordinal);
        }
    }
}
