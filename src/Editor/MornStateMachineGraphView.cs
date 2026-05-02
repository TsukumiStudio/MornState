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
            serializeGraphElements = SerializeForClipboard;
            unserializeAndPaste = UnserializeAndPaste;
            canPasteSerializedData = data => string.IsNullOrEmpty(data) == false && data.StartsWith("{");
            RegisterCallback<AttachToPanelEvent>(_ => {
                EditorApplication.update += OnEditorUpdate;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
            });
            RegisterCallback<DetachFromPanelEvent>(_ => {
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            });
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
            fsm.ReinjectOwners();
            var index = 0;
            foreach(var meta in fsm.Nodes) {
                var node = CreateNode(fsm,meta,index++);
                AddElement(node);
                _nodeByID[meta.id] = node;
            }
            foreach(var meta in fsm.Nodes) {
                if(_nodeByID.TryGetValue(meta.id,out var fromNode) == false) continue;
                foreach(var b in meta.behaviours) {
                    if(b == null) continue;
                    foreach(var (_,link) in EnumerateStateLinkFields(b)) {
                        if(link == null || link.stateID == 0) continue;
                        if(_nodeByID.TryGetValue(link.stateID,out var toNode) == false) continue;
                        var outPort = FindOutputPortFor(fromNode,b,link);
                        var inPort = FindInputPort(toNode);
                        if(outPort == null || inPort == null) continue;
                        var edge = outPort.ConnectTo(inPort);
                        edge.userData = (b,link);
                        AddElement(edge);
                    }
                }
            }
            UpdateHighlights();
        }
        private int _lastCurrentSnapshot = int.MinValue;
        private void OnEditorUpdate() {
            if(_target == null) return;
            var snapshot = Application.isPlaying ? _target.CurrentStateID : 0;
            if(snapshot == _lastCurrentSnapshot) return;
            _lastCurrentSnapshot = snapshot;
            UpdateHighlights();
        }
        private void OnPlayModeChanged(PlayModeStateChange c) {
            _lastCurrentSnapshot = int.MinValue;
            EditorApplication.delayCall += () => {
                if(_target == null) return;
                LoadStateMachine(_target);
            };
        }
        private void UpdateHighlights() {
            if(_target == null) return;
            var startID = _target.startStateID;
            var currentID = Application.isPlaying ? _target.CurrentStateID : 0;
            foreach(var pair in _nodeByID) {
                var n = pair.Value;
                var isStart = pair.Key == startID;
                var isCurrent = pair.Key == currentID;
                Color color;
                int width;
                if(isCurrent) { color = new Color(1.00f,0.70f,0.20f); width = 3; }
                else if(isStart) { color = new Color(0.30f,0.95f,0.45f); width = 2; }
                else { color = new Color(0.20f,0.20f,0.20f); width = 1; }
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
        private Node CreateNode(MornStateMachine fsm,MornStateMachine.StateNode meta,int index) {
            var displayName = string.IsNullOrEmpty(meta.name) == false
                ? meta.name
                : meta.behaviours.Count > 0 && meta.behaviours[0] != null
                    ? meta.behaviours[0].GetType().Name
                    : $"State {meta.id}";
            var node = new Node { userData = meta.id };
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
            titleField.RegisterValueChangedCallback(e => RenameNode(meta.id,e.newValue));
            node.titleContainer.Insert(0,titleField);
            var pos = meta.graphPosition;
            if(pos == Vector2.zero) pos = new Vector2(40 + index % 5 * 280,40 + index / 5 * 280);
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
            var so = new SerializedObject(fsm);
            for(var bi = 0;bi < meta.behaviours.Count;bi++) {
                var b = meta.behaviours[bi];
                if(b == null) continue;
                AddBehaviourSection(inspector,node,fsm,meta.id,bi,b,so);
            }
            var addBtn = new Button(() => OpenAddBehaviourSearch(meta.id)) { text = "+ Add Behaviour" };
            addBtn.style.marginTop = 4;
            inspector.Add(addBtn);
            node.extensionContainer.Add(inspector);
            node.RefreshExpandedState();
            node.RefreshPorts();
            node.RegisterCallback<GeometryChangedEvent>(_ => SaveNodePosition(meta.id,node));
            return node;
        }
        private void AddBehaviourSection(VisualElement parent,Node node,MornStateMachine fsm,int stateID,int behaviourIndex,StateBehaviour state,SerializedObject so) {
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
            var del = new Button(() => RemoveBehaviour(stateID,state)) { text = "x" };
            del.style.width = 20;
            del.style.height = 18;
            header.Add(del);
            section.Add(header);
            var nodeIndex = fsm.NodesMutable.FindIndex(n => n.id == stateID);
            if(nodeIndex >= 0) {
                var bProp = so.FindProperty("_nodes")
                    .GetArrayElementAtIndex(nodeIndex)
                    .FindPropertyRelative("behaviours")
                    .GetArrayElementAtIndex(behaviourIndex);
                var skipNames = new HashSet<string>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach(var f in state.GetType().GetFields(flags)) {
                    if(f.FieldType == typeof(StateLink)) skipNames.Add(f.Name);
                }
                var prop = bProp.Copy();
                var end = prop.GetEndProperty();
                if(prop.NextVisible(true)) {
                    do {
                        if(SerializedProperty.EqualContents(prop,end)) break;
                        if(skipNames.Contains(prop.name)) continue;
                        section.Add(new PropertyField(prop.Copy()));
                    } while(prop.NextVisible(false));
                }
                section.Bind(so);
            }
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
        private void RenameNode(int stateID,string newName) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            if(meta == null) return;
            Undo.RecordObject(_target,"Rename State");
            meta.name = newName;
            EditorUtility.SetDirty(_target);
        }
        private void RemoveBehaviour(int stateID,StateBehaviour state) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            if(meta == null) return;
            Undo.RegisterCompleteObjectUndo(_target,"Remove Behaviour");
            meta.behaviours.Remove(state);
            EditorUtility.SetDirty(_target);
            EditorApplication.delayCall += () => LoadStateMachine(_target);
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
                Node node = null;
                for(VisualElement cur = ve;cur != null;cur = cur.parent) {
                    if(cur is Node n) { node = n; break; }
                }
                if(node != null && node.userData is int sid) {
                    var captured = sid;
                    evt.menu.AppendAction("Set as Start State",_ => SetAsStart(captured),
                        _ => captured == _target.startStateID ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
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
            var instance = (StateBehaviour)System.Activator.CreateInstance(type);
            instance.StateID = newID;
            var meta = new MornStateMachine.StateNode {
                id = newID,
                name = type.Name,
                graphPosition = graphPos,
            };
            meta.behaviours.Add(instance);
            _target.RegisterNode(meta);
            if(_target.startStateID == 0) _target.startStateID = newID;
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        public void AddBehaviourToState(System.Type type,int stateID) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            if(meta == null) return;
            Undo.RegisterCompleteObjectUndo(_target,"Add Behaviour");
            var instance = (StateBehaviour)System.Activator.CreateInstance(type);
            instance.StateID = stateID;
            meta.behaviours.Add(instance);
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
        private int AllocateUniqueStateID() {
            var existing = new HashSet<int>();
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
                        Undo.RegisterCompleteObjectUndo(_target,"Connect Edge");
                        tup.Item2.stateID = targetID;
                        EditorUtility.SetDirty(_target);
                        edge.userData = tup;
                    }
                }
            }
            if(change.elementsToRemove != null) {
                var removedIDs = new List<int>();
                var anyEdge = false;
                foreach(var elem in change.elementsToRemove) {
                    switch(elem) {
                        case Edge e when e.output != null && e.output.userData is System.ValueTuple<StateBehaviour,StateLink> tup:
                            tup.Item2.stateID = 0;
                            anyEdge = true;
                            break;
                        case Node n when n.userData is int sid:
                            removedIDs.Add(sid);
                            break;
                    }
                }
                if(anyEdge) EditorUtility.SetDirty(_target);
                if(removedIDs.Count > 0) {
                    Undo.RegisterCompleteObjectUndo(_target,"Remove State");
                    foreach(var id in removedIDs) {
                        ClearLinksTargeting(id);
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
            foreach(var n in _target.Nodes) {
                foreach(var b in n.behaviours) {
                    if(b == null) continue;
                    foreach(var f in b.GetType().GetFields(flags)) {
                        if(f.FieldType != typeof(StateLink)) continue;
                        if(f.GetValue(b) is not StateLink link) continue;
                        if(link == null) continue;
                        if(link.stateID == stateID) link.stateID = 0;
                    }
                }
            }
        }
        [System.Serializable]
        private class ClipboardData {
            public List<NodeEntry> nodes = new();
        }
        [System.Serializable]
        private class NodeEntry {
            public string name;
            public Vector2 graphPosition;
            public List<BehaviourEntry> behaviours = new();
        }
        [System.Serializable]
        private class BehaviourEntry {
            public string typeName;
            public string json;
        }
        private string SerializeForClipboard(System.Collections.Generic.IEnumerable<GraphElement> elements) {
            var data = new ClipboardData();
            if(_target == null) return JsonUtility.ToJson(data);
            foreach(var elem in elements) {
                if(elem is Node n && n.userData is int stateID) {
                    var meta = _target.FindNode(stateID);
                    if(meta == null) continue;
                    var entry = new NodeEntry { name = meta.name,graphPosition = meta.graphPosition };
                    foreach(var b in meta.behaviours) {
                        if(b == null) continue;
                        entry.behaviours.Add(new BehaviourEntry {
                            typeName = b.GetType().AssemblyQualifiedName,
                            json = JsonUtility.ToJson(b),
                        });
                    }
                    data.nodes.Add(entry);
                }
            }
            return JsonUtility.ToJson(data);
        }
        private void UnserializeAndPaste(string operationName,string serializedData) {
            if(_target == null) return;
            ClipboardData data;
            try { data = JsonUtility.FromJson<ClipboardData>(serializedData); }
            catch { return; }
            if(data == null || data.nodes == null) return;
            Undo.RegisterCompleteObjectUndo(_target,operationName);
            ClearSelection();
            foreach(var entry in data.nodes) {
                var newID = AllocateUniqueStateID();
                var meta = new MornStateMachine.StateNode {
                    id = newID,
                    name = entry.name,
                    graphPosition = entry.graphPosition + new Vector2(40,40),
                };
                foreach(var b in entry.behaviours) {
                    var type = System.Type.GetType(b.typeName);
                    if(type == null) continue;
                    var instance = (StateBehaviour)System.Activator.CreateInstance(type);
                    JsonUtility.FromJsonOverwrite(b.json,instance);
                    instance.StateID = newID;
                    meta.behaviours.Add(instance);
                }
                _target.RegisterNode(meta);
            }
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
    }
}
