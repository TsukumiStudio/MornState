using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
namespace MornLib {
    public class MornStateMachineGraphWindow : EditorWindow {
        [MenuItem("Tools/MornState/Graph")]
        private static void Open() {
            var win = GetWindow<MornStateMachineGraphWindow>();
            win.titleContent = new GUIContent("MornState Graph");
            win.Show();
        }
        public static void OpenFor(MornStateMachine fsm) {
            var win = GetWindow<MornStateMachineGraphWindow>();
            win.titleContent = new GUIContent("MornState Graph");
            win._pinned = fsm;
            win.Show();
            win.Focus();
            win.Reload();
        }
        private MornStateMachineGraphView _view;
        private Label _hint;
        private MornStateMachine _pinned;
        private void OnEnable() {
            _view = new MornStateMachineGraphView();
            _view.style.flexGrow = 1;
            rootVisualElement.Add(_view);
            _hint = new Label("MornStateMachine を Hierarchy で選択するか、Inspector の Open Graph ボタンを押してください。");
            _hint.style.position = Position.Absolute;
            _hint.style.left = 12;
            _hint.style.top = 12;
            _hint.style.color = new Color(0.8f,0.8f,0.8f);
            rootVisualElement.Add(_hint);
            var toolbar = new Toolbar();
            var refreshBtn = new ToolbarButton(Reload) { text = "Refresh" };
            toolbar.Add(refreshBtn);
            rootVisualElement.Insert(0,toolbar);
            Selection.selectionChanged += Reload;
            Reload();
        }
        private void OnDisable() {
            Selection.selectionChanged -= Reload;
        }
        private void Reload() {
            if(_view == null) return;
            var fsm = _pinned;
            if(fsm == null) {
                var go = Selection.activeGameObject;
                if(go != null) fsm = go.GetComponentInParent<MornStateMachine>(true);
            }
            _view.LoadStateMachine(fsm);
            if(_hint != null) _hint.style.display = fsm == null ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
    public class MornStateMachineGraphView : GraphView {
        private MornStateMachine _target;
        private readonly Dictionary<int,Node> _nodeByID = new();
        public MornStateMachineGraphView() {
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ContentZoomer());
            var bg = new GridBackground();
            Insert(0,bg);
            bg.StretchToParentSize();
            graphViewChanged += OnGraphViewChanged;
            nodeCreationRequest = ctx => {
                if(_target == null) return;
                var window = EditorWindow.focusedWindow;
                var winLocal = window != null ? ctx.screenMousePosition - window.position.position : ctx.screenMousePosition;
                var viewLocal = this.WorldToLocal(winLocal);
                var graphPos = contentViewContainer.WorldToLocal(this.LocalToWorld(viewLocal));
                var provider = ScriptableObject.CreateInstance<MornStateSearchProvider>();
                provider.Setup(this,graphPos);
                SearchWindow.Open(new SearchWindowContext(ctx.screenMousePosition),provider);
            };
        }
        public void LoadStateMachine(MornStateMachine fsm) {
            _target = fsm;
            graphElements.ForEach(RemoveElement);
            _nodeByID.Clear();
            if(fsm == null) return;
            var states = fsm.GetComponentsInChildren<StateBehaviour>(true);
            var i = 0;
            foreach(var s in states) {
                if(s.Owner != fsm) continue;
                var node = CreateNode(s,i++);
                AddElement(node);
                _nodeByID[s.StateID] = node;
            }
            foreach(var s in states) {
                if(s.Owner != fsm) continue;
                if(_nodeByID.TryGetValue(s.StateID,out var fromNode) == false) continue;
                foreach(var link in EnumerateStateLinks(s)) {
                    if(_nodeByID.TryGetValue(link.stateID,out var toNode) == false) continue;
                    var fromPort = (Port)fromNode.outputContainer[0];
                    var toPort = (Port)toNode.inputContainer[0];
                    var edge = fromPort.ConnectTo(toPort);
                    edge.userData = (s,link);
                    AddElement(edge);
                }
            }
        }
        private static IEnumerable<StateLink> EnumerateStateLinks(StateBehaviour state) {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach(var f in state.GetType().GetFields(flags)) {
                if(f.FieldType != typeof(StateLink)) continue;
                if(f.GetValue(state) is StateLink link && link != null) yield return link;
            }
        }
        private static Node CreateNode(StateBehaviour state,int index) {
            var node = new Node {
                title = string.IsNullOrEmpty(state.name) ? state.GetType().Name : state.name,
                userData = state,
            };
            node.SetPosition(new Rect(40 + index % 5 * 280,40 + index / 5 * 240,260,180));
            var inPort = node.InstantiatePort(Orientation.Horizontal,Direction.Input,Port.Capacity.Multi,typeof(StateBehaviour));
            inPort.portName = "in";
            node.inputContainer.Add(inPort);
            var outPort = node.InstantiatePort(Orientation.Horizontal,Direction.Output,Port.Capacity.Multi,typeof(StateBehaviour));
            outPort.portName = state.GetType().Name;
            node.outputContainer.Add(outPort);
            var so = new SerializedObject(state);
            var skipNames = new HashSet<string>();
            const BindingFlags refFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach(var f in state.GetType().GetFields(refFlags)) {
                if(f.FieldType == typeof(StateLink)) skipNames.Add(f.Name);
            }
            var inspector = new VisualElement();
            inspector.style.minWidth = 240;
            inspector.style.paddingLeft = 6;
            inspector.style.paddingRight = 6;
            inspector.style.paddingTop = 4;
            inspector.style.paddingBottom = 4;
            var prop = so.GetIterator();
            prop.NextVisible(true);
            while(prop.NextVisible(false)) {
                if(skipNames.Contains(prop.name)) continue;
                inspector.Add(new PropertyField(prop.Copy()));
            }
            inspector.Bind(so);
            node.extensionContainer.Add(inspector);
            node.RefreshExpandedState();
            node.RefreshPorts();
            return node;
        }
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt) {
            if(_target != null) {
                var localPos = evt.mousePosition;
                evt.menu.AppendAction("Create State...",_ => OpenSearchAtLocal(localPos));
                evt.menu.AppendSeparator();
            }
            base.BuildContextualMenu(evt);
        }
        public void OpenSearchAtLocal(Vector2 viewLocalPos) {
            if(_target == null) return;
            var graphPos = contentViewContainer.WorldToLocal(this.LocalToWorld(viewLocalPos));
            var provider = ScriptableObject.CreateInstance<MornStateSearchProvider>();
            provider.Setup(this,graphPos);
            var window = EditorWindow.focusedWindow;
            var worldPos = this.LocalToWorld(viewLocalPos);
            var screenPos = window != null ? window.position.position + worldPos : worldPos;
            SearchWindow.Open(new SearchWindowContext(screenPos),provider);
        }
        public void CreateStateAt(System.Type type,Vector2 graphPos) {
            CreateStateNode(type,graphPos);
        }
        private void CreateStateNode(System.Type type,Vector2 graphPos) {
            if(_target == null) return;
            var go = new GameObject(type.Name);
            Undo.RegisterCreatedObjectUndo(go,$"Create {type.Name}");
            Undo.SetTransformParent(go.transform,_target.transform,$"Create {type.Name}");
            go.transform.localPosition = Vector3.zero;
            var comp = (StateBehaviour)Undo.AddComponent(go,type);
            comp.StateID = AllocateUniqueStateID();
            EditorUtility.SetDirty(comp);
            LoadStateMachine(_target);
            if(_nodeByID.TryGetValue(comp.StateID,out var node)) {
                node.SetPosition(new Rect(graphPos.x,graphPos.y,200,100));
            }
        }
        private int AllocateUniqueStateID() {
            var existing = new HashSet<int>();
            foreach(var s in _target.GetComponentsInChildren<StateBehaviour>(true)) {
                if(s.Owner == _target) existing.Add(s.StateID);
            }
            var rng = new System.Random();
            while(true) {
                var id = rng.Next(1,int.MaxValue);
                if(existing.Contains(id) == false) return id;
            }
        }
        public override List<Port> GetCompatiblePorts(Port startPort,NodeAdapter nodeAdapter) {
            var compatible = new List<Port>();
            ports.ForEach(p => {
                if(p == startPort) return;
                if(p.node == startPort.node) return;
                if(p.direction == startPort.direction) return;
                compatible.Add(p);
            });
            return compatible;
        }
        private GraphViewChange OnGraphViewChanged(GraphViewChange change) {
            if(_target == null) return change;
            if(change.edgesToCreate != null) {
                foreach(var edge in change.edgesToCreate) {
                    var from = edge.output.node.userData as StateBehaviour;
                    var to = edge.input.node.userData as StateBehaviour;
                    if(from == null || to == null) continue;
                    var assigned = AssignFirstEmptyLink(from,to.StateID);
                    if(assigned != null) edge.userData = (from,assigned);
                }
            }
            if(change.elementsToRemove != null) {
                var removedStates = new List<StateBehaviour>();
                foreach(var elem in change.elementsToRemove) {
                    switch(elem) {
                        case Edge e when e.userData is System.ValueTuple<StateBehaviour,StateLink> tup:
                            tup.Item2.stateID = 0;
                            EditorUtility.SetDirty(tup.Item1);
                            break;
                        case Node n when n.userData is StateBehaviour state:
                            removedStates.Add(state);
                            break;
                    }
                }
                if(removedStates.Count > 0) {
                    foreach(var state in removedStates) {
                        if(state == null) continue;
                        ClearLinksTargeting(state.StateID);
                        if(_nodeByID.TryGetValue(state.StateID,out _)) _nodeByID.Remove(state.StateID);
                        Undo.DestroyObjectImmediate(state.gameObject);
                    }
                    EditorApplication.delayCall += () => LoadStateMachine(_target);
                }
            }
            return change;
        }
        private void ClearLinksTargeting(int stateID) {
            if(_target == null) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach(var s in _target.GetComponentsInChildren<StateBehaviour>(true)) {
                if(s.Owner != _target) continue;
                var dirty = false;
                foreach(var f in s.GetType().GetFields(flags)) {
                    if(f.FieldType != typeof(StateLink)) continue;
                    if(f.GetValue(s) is not StateLink link) continue;
                    if(link == null) continue;
                    if(link.stateID == stateID) {
                        link.stateID = 0;
                        dirty = true;
                    }
                }
                if(dirty) EditorUtility.SetDirty(s);
            }
        }
        private static StateLink AssignFirstEmptyLink(StateBehaviour state,int targetID) {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach(var f in state.GetType().GetFields(flags)) {
                if(f.FieldType != typeof(StateLink)) continue;
                if(f.GetValue(state) is not StateLink link) continue;
                if(link == null) continue;
                if(link.stateID == 0) {
                    link.stateID = targetID;
                    EditorUtility.SetDirty(state);
                    return link;
                }
            }
            return null;
        }
    }
}
