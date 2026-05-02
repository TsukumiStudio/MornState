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
            fsm.CollectStates();
            var groupIndex = 0;
            foreach(var pair in fsm.StatesByID) {
                var node = CreateNode(fsm,pair.Key,pair.Value,groupIndex++);
                AddElement(node);
                _nodeByID[pair.Key] = node;
            }
            foreach(var pair in fsm.StatesByID) {
                if(_nodeByID.TryGetValue(pair.Key,out var fromNode) == false) continue;
                foreach(var s in pair.Value) {
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
        }
        private static IEnumerable<StateLink> EnumerateStateLinks(StateBehaviour state) {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach(var f in state.GetType().GetFields(flags)) {
                if(f.FieldType != typeof(StateLink)) continue;
                if(f.GetValue(state) is StateLink link && link != null) yield return link;
            }
        }
        private Node CreateNode(MornStateMachine fsm,int stateID,List<StateBehaviour> behaviours,int index) {
            var meta = fsm.FindNode(stateID);
            var displayName = meta != null && string.IsNullOrEmpty(meta.name) == false
                ? meta.name
                : behaviours.Count > 0 ? behaviours[0].GetType().Name : $"State {stateID}";
            var node = new Node {
                title = displayName,
                userData = stateID,
            };
            var pos = meta != null ? meta.graphPosition : new Vector2(40 + index % 5 * 280,40 + index / 5 * 280);
            node.SetPosition(new Rect(pos.x,pos.y,260,180));
            var inPort = node.InstantiatePort(Orientation.Horizontal,Direction.Input,Port.Capacity.Multi,typeof(StateBehaviour));
            inPort.portName = "in";
            node.inputContainer.Add(inPort);
            var outPort = node.InstantiatePort(Orientation.Horizontal,Direction.Output,Port.Capacity.Multi,typeof(StateBehaviour));
            outPort.portName = "out";
            node.outputContainer.Add(outPort);
            var inspector = new VisualElement();
            inspector.style.minWidth = 240;
            inspector.style.paddingLeft = 6;
            inspector.style.paddingRight = 6;
            inspector.style.paddingTop = 4;
            inspector.style.paddingBottom = 4;
            foreach(var s in behaviours) AddBehaviourSection(inspector,s);
            var addBtn = new Button(() => OpenAddBehaviourSearch(stateID)) { text = "+ Add Behaviour" };
            addBtn.style.marginTop = 4;
            inspector.Add(addBtn);
            node.extensionContainer.Add(inspector);
            node.RefreshExpandedState();
            node.RefreshPorts();
            node.RegisterCallback<GeometryChangedEvent>(_ => SaveNodePosition(stateID,node));
            return node;
        }
        private void AddBehaviourSection(VisualElement parent,StateBehaviour state) {
            var section = new VisualElement();
            section.style.marginBottom = 4;
            section.style.borderTopWidth = 1;
            section.style.borderTopColor = new Color(1f,1f,1f,0.1f);
            section.style.paddingTop = 2;
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 2;
            var title = new Label(state.GetType().Name);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);
            var del = new Button(() => RemoveBehaviour(state)) { text = "x" };
            del.style.width = 20;
            del.style.height = 18;
            header.Add(del);
            section.Add(header);
            var so = new SerializedObject(state);
            var skipNames = new HashSet<string>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach(var f in state.GetType().GetFields(flags)) {
                if(f.FieldType == typeof(StateLink)) skipNames.Add(f.Name);
            }
            var prop = so.GetIterator();
            prop.NextVisible(true);
            while(prop.NextVisible(false)) {
                if(skipNames.Contains(prop.name)) continue;
                section.Add(new PropertyField(prop.Copy()));
            }
            section.Bind(so);
            parent.Add(section);
        }
        private void RemoveBehaviour(StateBehaviour state) {
            if(state == null) return;
            var owner = state.Owner;
            Undo.DestroyObjectImmediate(state);
            EditorApplication.delayCall += () => LoadStateMachine(owner);
        }
        private void SaveNodePosition(int stateID,Node node) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            if(meta == null) return;
            var rect = node.GetPosition();
            if(meta.graphPosition == new Vector2(rect.x,rect.y)) return;
            Undo.RecordObject(_target,"Move Node");
            meta.graphPosition = new Vector2(rect.x,rect.y);
            EditorUtility.SetDirty(_target);
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
        public void OpenAddBehaviourSearch(int stateID) {
            if(_target == null) return;
            var provider = ScriptableObject.CreateInstance<MornStateSearchProvider>();
            provider.SetupAddBehaviour(this,stateID);
            var screenPos = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);
            SearchWindow.Open(new SearchWindowContext(screenPos),provider);
        }
        public void CreateStateAt(System.Type type,Vector2 graphPos) {
            if(_target == null) return;
            var newID = AllocateUniqueStateID();
            var go = _target.gameObject;
            Undo.RegisterCompleteObjectUndo(_target,"Create State");
            var comp = (StateBehaviour)Undo.AddComponent(go,type);
            comp.StateID = newID;
            EditorUtility.SetDirty(comp);
            _target.RegisterNode(new MornStateMachine.StateNode {
                id = newID,
                name = type.Name,
                graphPosition = graphPos,
            });
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        public void AddBehaviourToState(System.Type type,int stateID) {
            if(_target == null) return;
            var go = _target.gameObject;
            var comp = (StateBehaviour)Undo.AddComponent(go,type);
            comp.StateID = stateID;
            EditorUtility.SetDirty(comp);
            LoadStateMachine(_target);
        }
        private int AllocateUniqueStateID() {
            var existing = new HashSet<int>();
            foreach(var s in _target.GetComponents<StateBehaviour>()) existing.Add(s.StateID);
            foreach(var n in _target.Nodes) existing.Add(n.id);
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
                    var fromID = edge.output.node.userData is int fid ? fid : 0;
                    var toID = edge.input.node.userData is int tid ? tid : 0;
                    if(fromID == 0 || toID == 0) continue;
                    if(_target.StatesByID.TryGetValue(fromID,out var list) == false) continue;
                    foreach(var b in list) {
                        var assigned = AssignFirstEmptyLink(b,toID);
                        if(assigned == null) continue;
                        edge.userData = (b,assigned);
                        break;
                    }
                }
            }
            if(change.elementsToRemove != null) {
                var removedIDs = new List<int>();
                foreach(var elem in change.elementsToRemove) {
                    switch(elem) {
                        case Edge e when e.userData is System.ValueTuple<StateBehaviour,StateLink> tup:
                            tup.Item2.stateID = 0;
                            EditorUtility.SetDirty(tup.Item1);
                            break;
                        case Node n when n.userData is int stateID:
                            removedIDs.Add(stateID);
                            break;
                    }
                }
                if(removedIDs.Count > 0) {
                    foreach(var id in removedIDs) {
                        ClearLinksTargeting(id);
                        if(_target.StatesByID.TryGetValue(id,out var list)) {
                            foreach(var b in list) Undo.DestroyObjectImmediate(b);
                        }
                        _target.UnregisterNode(id);
                        _nodeByID.Remove(id);
                    }
                    EditorUtility.SetDirty(_target);
                    EditorApplication.delayCall += () => LoadStateMachine(_target);
                }
            }
            return change;
        }
        private void ClearLinksTargeting(int stateID) {
            if(_target == null) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach(var s in _target.GetComponents<StateBehaviour>()) {
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
