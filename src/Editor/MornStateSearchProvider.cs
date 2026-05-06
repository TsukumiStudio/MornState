using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
namespace MornLib {
    public class MornStateSearchProvider : ScriptableObject,ISearchWindowProvider {
        private enum Mode { CreateState,AddBehaviour }
        private const string SessionKeyAddBehaviour = "MornState.SearchFilter.AddBehaviour";
        private const string SessionKeyCreateState = "MornState.SearchFilter.CreateState";
        private MornStateMachineGraphView _view;
        private Vector2 _graphPos;
        private int _stateID;
        private Mode _mode;
        public void Setup(MornStateMachineGraphView view,Vector2 graphPos) {
            _view = view;
            _graphPos = graphPos;
            _mode = Mode.CreateState;
        }
        public void SetupAddBehaviour(MornStateMachineGraphView view,int stateID) {
            _view = view;
            _stateID = stateID;
            _mode = Mode.AddBehaviour;
        }
        public string SavedFilter => SessionState.GetString(_mode == Mode.AddBehaviour ? SessionKeyAddBehaviour : SessionKeyCreateState,"");
        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context) {
            var types = new List<Type>();
            foreach(var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                Type[] all;
                try { all = asm.GetTypes(); } catch { continue; }
                foreach(var t in all) {
                    if(t.IsAbstract) continue;
                    if(typeof(MornStateBehaviour).IsAssignableFrom(t) == false) continue;
                    types.Add(t);
                }
            }
            types.Sort((a,b) => string.Compare(a.FullName,b.FullName,StringComparison.Ordinal));
            var root = new Node { Name = _mode == Mode.AddBehaviour ? "Add Behaviour" : "Create State" };
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
            SaveCurrentFilter();
            if(entry.userData is Type t == false) return false;
            if(_view == null) return false;
            if(_mode == Mode.AddBehaviour) _view.AddBehaviourToState(t,_stateID);
            else _view.CreateStateAt(t,_graphPos);
            return true;
        }
        public static void ApplyFilterToOpenWindow(string saved) {
            if(string.IsNullOrEmpty(saved)) return;
            var win = GetActiveSearchWindow();
            if(win == null) return;
            var t = win.GetType();
            var search = t.GetField("m_Search",BindingFlags.NonPublic | BindingFlags.Instance);
            var delayed = t.GetField("m_DelayedSearch",BindingFlags.NonPublic | BindingFlags.Instance);
            if(search == null) return;
            search.SetValue(win,saved);
            if(delayed != null) delayed.SetValue(win,saved);
            var rebuild = t.GetMethod("RebuildSearch",BindingFlags.NonPublic | BindingFlags.Instance);
            if(rebuild != null) rebuild.Invoke(win,null);
            win.Repaint();
        }
        private void SaveCurrentFilter() {
            var win = GetActiveSearchWindow();
            if(win == null) return;
            var field = win.GetType().GetField("m_Search",BindingFlags.NonPublic | BindingFlags.Instance);
            if(field == null) return;
            var text = field.GetValue(win) as string ?? "";
            SessionState.SetString(_mode == Mode.AddBehaviour ? SessionKeyAddBehaviour : SessionKeyCreateState,text);
        }
        private static EditorWindow GetActiveSearchWindow() {
            var arr = Resources.FindObjectsOfTypeAll(typeof(SearchWindow));
            return arr.Length == 0 ? null : arr[arr.Length - 1] as EditorWindow;
        }
        private class Node {
            public string Name;
            public Type Type;
            public readonly SortedDictionary<string,Node> Children = new(StringComparer.Ordinal);
        }
    }
}
