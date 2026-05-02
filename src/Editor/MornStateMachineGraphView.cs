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
            Undo.undoRedoPerformed += Reload;
            Reload();
        }
        private void OnDisable() {
            Selection.selectionChanged -= Reload;
            Undo.undoRedoPerformed -= Reload;
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
            RegisterCallback<AttachToPanelEvent>(_ => EditorApplication.update += OnEditorUpdate);
            RegisterCallback<DetachFromPanelEvent>(_ => EditorApplication.update -= OnEditorUpdate);
            nodeCreationRequest = ctx => {
                if(_target == null) return;
                var window = EditorWindow.focusedWindow;
                var winLocal = window != null ? ctx.screenMousePosition - window.position.position : ctx.screenMousePosition;
                var viewLocal = this.WorldToLocal(winLocal);
                var graphPos = contentViewContainer.WorldToLocal(this.LocalToWorld(viewLocal));
                CreateEmptyStateAt(graphPos);
            };
        }
        public void LoadStateMachine(MornStateMachine fsm) {
            _target = fsm;
            graphElements.ForEach(RemoveElement);
            _nodeByID.Clear();
            if(fsm == null) return;
            fsm.CollectStates();
            var allIDs = new HashSet<int>();
            foreach(var pair in fsm.StatesByID) allIDs.Add(pair.Key);
            foreach(var n in fsm.Nodes) allIDs.Add(n.id);
            var groupIndex = 0;
            foreach(var id in allIDs) {
                var behaviours = fsm.StatesByID.TryGetValue(id,out var list) ? list : new List<StateBehaviour>();
                var node = CreateNode(fsm,id,behaviours,groupIndex++);
                AddElement(node);
                _nodeByID[id] = node;
            }
            foreach(var pair in fsm.StatesByID) {
                if(_nodeByID.TryGetValue(pair.Key,out var fromNode) == false) continue;
                foreach(var s in pair.Value) {
                    foreach(var (_,link) in EnumerateStateLinkFields(s)) {
                        if(link == null || link.stateID == 0) continue;
                        if(_nodeByID.TryGetValue(link.stateID,out var toNode) == false) continue;
                        var outPort = FindOutputPortFor(fromNode,s,link);
                        var inPort = FindInputPort(toNode);
                        if(outPort == null || inPort == null) continue;
                        var edge = outPort.ConnectTo(inPort);
                        edge.userData = (s,link);
                        AddElement(edge);
                    }
                }
            }
            UpdateHighlights();
        }
        private int _lastCurrentSnapshot = int.MinValue;
        private void OnEditorUpdate() {
            if(Application.isPlaying == false) return;
            if(_target == null) return;
            var snapshot = 0;
            foreach(var b in _target.CurrentBehaviours) {
                if(b != null) snapshot = snapshot * 31 + b.StateID;
            }
            if(snapshot == _lastCurrentSnapshot) return;
            _lastCurrentSnapshot = snapshot;
            UpdateHighlights();
        }
        private void UpdateHighlights() {
            if(_target == null) return;
            var startID = _target.startStateID;
            var currentIDs = new HashSet<int>();
            if(Application.isPlaying) {
                foreach(var b in _target.CurrentBehaviours) {
                    if(b != null) currentIDs.Add(b.StateID);
                }
            }
            foreach(var pair in _nodeByID) {
                var n = pair.Value;
                var isStart = pair.Key == startID;
                var isCurrent = currentIDs.Contains(pair.Key);
                Color color;
                int width;
                if(isCurrent) {
                    color = new Color(1.00f,0.70f,0.20f);
                    width = 3;
                } else if(isStart) {
                    color = new Color(0.30f,0.95f,0.45f);
                    width = 2;
                } else {
                    color = new Color(0.20f,0.20f,0.20f);
                    width = 1;
                }
                n.style.borderTopColor = color;
                n.style.borderBottomColor = color;
                n.style.borderLeftColor = color;
                n.style.borderRightColor = color;
                n.style.borderTopWidth = width;
                n.style.borderBottomWidth = width;
                n.style.borderLeftWidth = width;
                n.style.borderRightWidth = width;
            }
        }
        private static IEnumerable<(string fieldName,StateLink link)> EnumerateStateLinkFields(StateBehaviour state) {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach(var f in state.GetType().GetFields(flags)) {
                if(f.FieldType != typeof(StateLink)) continue;
                if(f.GetValue(state) is StateLink link) yield return (f.Name,link);
            }
        }
        private static Port FindInputPort(Node node) {
            return node.inputContainer.childCount > 0 ? node.inputContainer[0] as Port : null;
        }
        private static Port FindOutputPortFor(Node node,StateBehaviour state,StateLink link) {
            Port found = null;
            node.Query<Port>().ForEach(p => {
                if(found != null) return;
                if(p.direction != Direction.Output) return;
                if(p.userData is System.ValueTuple<StateBehaviour,StateLink> tup && tup.Item1 == state && tup.Item2 == link) {
                    found = p;
                }
            });
            return found;
        }
        private Node CreateNode(MornStateMachine fsm,int stateID,List<StateBehaviour> behaviours,int index) {
            var meta = fsm.FindNode(stateID);
            var displayName = meta != null && string.IsNullOrEmpty(meta.name) == false
                ? meta.name
                : behaviours.Count > 0 ? behaviours[0].GetType().Name : $"State {stateID}";
            var node = new Node {
                userData = stateID,
            };
            var titleLabel = node.titleContainer.Q<Label>("title-label");
            if(titleLabel != null) titleLabel.style.display = DisplayStyle.None;
            node.titleContainer.style.height = 22;
            node.titleContainer.style.minHeight = 22;
            node.titleContainer.style.paddingTop = 0;
            node.titleContainer.style.paddingBottom = 0;
            var titleField = new TextField { value = displayName };
            titleField.style.flexGrow = 1;
            titleField.style.marginLeft = 4;
            titleField.style.marginRight = 4;
            titleField.style.marginTop = 0;
            titleField.style.marginBottom = 0;
            titleField.style.height = 20;
            titleField.style.minHeight = 20;
            var titleInput = titleField.Q(TextField.textInputUssName);
            if(titleInput != null) {
                titleInput.style.paddingTop = 0;
                titleInput.style.paddingBottom = 0;
                titleInput.style.marginTop = 0;
                titleInput.style.marginBottom = 0;
                titleInput.style.height = 20;
                titleInput.style.minHeight = 20;
                titleInput.style.fontSize = 12;
            }
            titleField.RegisterValueChangedCallback(e => RenameNode(stateID,e.newValue,node));
            node.titleContainer.Insert(0,titleField);
            var pos = meta != null ? meta.graphPosition : new Vector2(40 + index % 5 * 280,40 + index / 5 * 280);
            node.SetPosition(new Rect(pos.x,pos.y,260,180));
            var inPort = node.InstantiatePort(Orientation.Horizontal,Direction.Input,Port.Capacity.Multi,typeof(StateBehaviour));
            inPort.portName = "in";
            node.inputContainer.Add(inPort);
            var inspector = new VisualElement();
            inspector.style.minWidth = 240;
            inspector.style.paddingLeft = 6;
            inspector.style.paddingRight = 6;
            inspector.style.paddingTop = 4;
            inspector.style.paddingBottom = 4;
            foreach(var s in behaviours) AddBehaviourSection(inspector,node,s);
            var addBtn = new Button(() => OpenAddBehaviourSearch(stateID)) { text = "+ Add Behaviour" };
            addBtn.style.marginTop = 4;
            inspector.Add(addBtn);
            node.extensionContainer.Add(inspector);
            node.RefreshExpandedState();
            node.RefreshPorts();
            node.RegisterCallback<GeometryChangedEvent>(_ => SaveNodePosition(stateID,node));
            return node;
        }
        private void AddBehaviourSection(VisualElement parent,Node node,StateBehaviour state) {
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
            foreach(var (fieldName,link) in EnumerateStateLinkFields(state)) {
                section.Add(CreateOutputPortRow(node,state,link,fieldName));
            }
            parent.Add(section);
        }
        private static VisualElement CreateOutputPortRow(Node node,StateBehaviour state,StateLink link,string fieldName) {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2;
            var label = new Label(string.IsNullOrEmpty(link.name) ? fieldName : link.name);
            label.style.flexGrow = 1;
            label.style.unityTextAlign = TextAnchor.MiddleRight;
            label.style.marginRight = 4;
            row.Add(label);
            var port = node.InstantiatePort(Orientation.Horizontal,Direction.Output,Port.Capacity.Single,typeof(StateBehaviour));
            port.portName = "";
            port.userData = (state,link);
            row.Add(port);
            return row;
        }
        private void RenameNode(int stateID,string newName,Node node) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            if(meta == null) return;
            Undo.RecordObject(_target,"Rename State");
            meta.name = newName;
            EditorUtility.SetDirty(_target);
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
            if(_target != null && evt.target is VisualElement ve) {
                var node = ve.GetFirstAncestorOfType<Node>();
                if(node != null && node.userData is int sid) {
                    evt.menu.AppendAction("Set as Start State",_ => SetAsStart(sid),
                        _ => sid == _target.startStateID ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                    evt.menu.AppendSeparator();
                }
            }
            base.BuildContextualMenu(evt);
        }
        public void OpenAddBehaviourSearch(int stateID) {
            if(_target == null) return;
            var provider = ScriptableObject.CreateInstance<MornStateSearchProvider>();
            provider.SetupAddBehaviour(this,stateID);
            var screenPos = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);
            SearchWindow.Open(new SearchWindowContext(screenPos),provider);
        }
        public void CreateEmptyStateAt(Vector2 graphPos) {
            if(_target == null) return;
            var newID = AllocateUniqueStateID();
            Undo.RegisterCompleteObjectUndo(_target,"Create State");
            _target.RegisterNode(new MornStateMachine.StateNode {
                id = newID,
                name = $"State {newID}",
                graphPosition = graphPos,
            });
            if(_target.startStateID == 0) _target.startStateID = newID;
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        public void CreateStateAt(System.Type type,Vector2 graphPos) {
            if(_target == null) return;
            var newID = AllocateUniqueStateID();
            Undo.RegisterCompleteObjectUndo(_target,"Create State");
            var comp = (StateBehaviour)Undo.AddComponent(_target.gameObject,type);
            comp.StateID = newID;
            EditorUtility.SetDirty(comp);
            _target.RegisterNode(new MornStateMachine.StateNode {
                id = newID,
                name = type.Name,
                graphPosition = graphPos,
            });
            if(_target.startStateID == 0) _target.startStateID = newID;
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        public void SetAsStart(int stateID) {
            if(_target == null) return;
            Undo.RecordObject(_target,"Set Start State");
            _target.startStateID = stateID;
            EditorUtility.SetDirty(_target);
            UpdateHighlights();
        }
        public void AddBehaviourToState(System.Type type,int stateID) {
            if(_target == null) return;
            Undo.RegisterCompleteObjectUndo(_target,"Add Behaviour");
            var comp = (StateBehaviour)Undo.AddComponent(_target.gameObject,type);
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
                    if(edge.output.userData is System.ValueTuple<StateBehaviour,StateLink> tup
                       && edge.input.node.userData is int targetID) {
                        Undo.RegisterCompleteObjectUndo(tup.Item1,"Connect Edge");
                        tup.Item2.stateID = targetID;
                        EditorUtility.SetDirty(tup.Item1);
                        edge.userData = tup;
                    }
                }
            }
            if(change.elementsToRemove != null) {
                var removedIDs = new List<int>();
                var dirtyEdgeBehaviours = new HashSet<StateBehaviour>();
                foreach(var elem in change.elementsToRemove) {
                    switch(elem) {
                        case Edge e when e.output != null && e.output.userData is System.ValueTuple<StateBehaviour,StateLink> tup:
                            Undo.RegisterCompleteObjectUndo(tup.Item1,"Disconnect Edge");
                            tup.Item2.stateID = 0;
                            dirtyEdgeBehaviours.Add(tup.Item1);
                            break;
                        case Node n when n.userData is int sid:
                            removedIDs.Add(sid);
                            break;
                    }
                }
                foreach(var b in dirtyEdgeBehaviours) EditorUtility.SetDirty(b);
                if(removedIDs.Count > 0) {
                    Undo.RegisterCompleteObjectUndo(_target,"Remove State");
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
                        Undo.RegisterCompleteObjectUndo(s,"Clear Link");
                        link.stateID = 0;
                        dirty = true;
                    }
                }
                if(dirty) EditorUtility.SetDirty(s);
            }
        }
    }
}
