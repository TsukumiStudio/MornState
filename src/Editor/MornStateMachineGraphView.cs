using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
namespace MornLib {
    public class MornStateMachineGraphWindow : EditorWindow {
        [MenuItem("Tools/MornState/Graph")]
        private static MornStateMachineGraphWindow Open() {
            var win = GetWindow<MornStateMachineGraphWindow>(typeof(SceneView));
            win.titleContent = new GUIContent("MornState Graph");
            return win;
        }
        public static void OpenFor(MornStateMachine fsm) {
            var win = Open();
            win._pinned = fsm;
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
            _hint.style.color = new Color(0.8f,0.8f,0.8f);
            _hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            var hintWrapper = new VisualElement();
            hintWrapper.style.position = Position.Absolute;
            hintWrapper.style.left = 0;
            hintWrapper.style.right = 0;
            hintWrapper.style.top = 0;
            hintWrapper.style.bottom = 0;
            hintWrapper.style.justifyContent = Justify.Center;
            hintWrapper.style.alignItems = Align.Center;
            hintWrapper.pickingMode = PickingMode.Ignore;
            hintWrapper.Add(_hint);
            rootVisualElement.Add(hintWrapper);
            var toolbar = new Toolbar();
            var refreshBtn = new ToolbarButton(Reload) { text = "Refresh" };
            toolbar.Add(refreshBtn);
            rootVisualElement.Insert(0,toolbar);
            Selection.selectionChanged += Reload;
            Undo.undoRedoPerformed += Reload;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Reload();
        }
        private void OnDisable() {
            Selection.selectionChanged -= Reload;
            Undo.undoRedoPerformed -= Reload;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }
        private void OnPlayModeChanged(PlayModeStateChange c) {
            EditorApplication.delayCall += () => {
                if(_view != null) _view.ForceFullReload();
                Reload();
            };
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
        private readonly Dictionary<int,Vector2> _layoutPositions = new();
        private readonly List<EdgeRecord> _edgeRecords = new();
        private VisualElement _edgesLayer;
        private struct EdgeRecord {
            public Port outputPort;
            public Node targetNode;
            public MornStateBehaviour source;
            public Connection link;
        }
        public MornStateMachineGraphView() {
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ContentZoomer());
            var bg = new GridBackground();
            Insert(0,bg);
            bg.StretchToParentSize();
            _edgesLayer = new VisualElement();
            _edgesLayer.style.position = Position.Absolute;
            _edgesLayer.style.left = 0;
            _edgesLayer.style.top = 0;
            _edgesLayer.style.width = 100000;
            _edgesLayer.style.height = 100000;
            _edgesLayer.pickingMode = PickingMode.Ignore;
            _edgesLayer.generateVisualContent += DrawCustomEdges;
            contentViewContainer.Insert(0,_edgesLayer);
            _edgesLayer.schedule.Execute(() => {
                if(_edgesLayer.parent != null && _edgesLayer.parent.IndexOf(_edgesLayer) != 0) {
                    _edgesLayer.SendToBack();
                }
            }).Every(100);
            graphViewChanged += OnGraphViewChanged;
            RegisterCallback<PointerMoveEvent>(OnEdgePointerMove,TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(_ => StopEdgeDrag(),TrickleDown.TrickleDown);
            RegisterCallback<MouseMoveEvent>(OnEdgeDragMove,TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(_ => StopEdgeDrag(),TrickleDown.TrickleDown);
            serializeGraphElements = SerializeForClipboard;
            unserializeAndPaste = UnserializeAndPaste;
            canPasteSerializedData = data => string.IsNullOrEmpty(data) == false && data.StartsWith("{");
            EditorApplication.update += OnEditorUpdate;
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
        private bool _hasLoadedOnce;
        private bool _isClearingForReload;
        private readonly Dictionary<int,string> _nodeSig = new();
        private static string ComputeNodeSig(MornStateMachine.StateNode meta) {
            if(meta.behaviours == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach(var b in meta.behaviours) {
                if(b == null) sb.Append("|null"); else sb.Append("|").Append(b.GetType().FullName);
            }
            return sb.ToString();
        }
        private bool IsStructurallySame(MornStateMachine fsm) {
            if(fsm == null || _target != fsm) return false;
            if(_hasLoadedOnce == false) return false;
            if(fsm.Nodes.Count != _nodeByID.Count) return false;
            foreach(var meta in fsm.Nodes) {
                if(_nodeByID.ContainsKey(meta.id) == false) return false;
                var sig = ComputeNodeSig(meta);
                if(_nodeSig.TryGetValue(meta.id,out var old) == false || old != sig) return false;
            }
            return true;
        }
        private void RebuildEdgeRecords(MornStateMachine fsm) {
            _edgeRecords.Clear();
            foreach(var meta in fsm.Nodes) {
                if(_nodeByID.TryGetValue(meta.id,out var fromNode) == false) continue;
                foreach(var b in meta.behaviours) {
                    if(b == null) continue;
                    foreach(var (_,link) in EnumerateConnectionFields(b)) {
                        if(link == null || link.stateID == 0) continue;
                        if(_nodeByID.TryGetValue(link.stateID,out var toNode) == false) continue;
                        var outPort = FindOutputPortFor(fromNode,b,link);
                        if(outPort == null) continue;
                        _edgeRecords.Add(new EdgeRecord { outputPort = outPort,targetNode = toNode,source = b,link = link });
                    }
                }
            }
            _edgesLayer.MarkDirtyRepaint();
        }
        public void ForceFullReload() {
            _hasLoadedOnce = false;
        }
        public void LoadStateMachine(MornStateMachine fsm) {
            if(IsStructurallySame(fsm)) {
                _target = fsm;
                fsm.ReinjectOwners();
                var positionsFast = ComputeAutoLayout(fsm);
                _layoutPositions.Clear();
                foreach(var kv in positionsFast) _layoutPositions[kv.Key] = kv.Value;
                foreach(var meta in fsm.Nodes) {
                    if(_nodeByID.TryGetValue(meta.id,out var node) == false) continue;
                    var pos = positionsFast.TryGetValue(meta.id,out var p) ? p : new Vector2(40,40);
                    var cur = new Vector2(node.transform.position.x,node.transform.position.y);
                    if(Vector2.Distance(cur,pos) > 0.5f) {
                        _animations[node] = new AnimState { start = cur,end = pos,elapsed = 0f,duration = 0.25f };
                    }
                }
                RebuildEdgeRecords(fsm);
                UpdateHighlights();
                return;
            }
            _isClearingForReload = true;
            var oldPositions = new Dictionary<int,Vector2>();
            foreach(var pair in _nodeByID) {
                var tp = pair.Value.transform.position;
                oldPositions[pair.Key] = new Vector2(tp.x,tp.y);
            }
            _target = fsm;
            graphElements.ForEach(RemoveElement);
            _nodeByID.Clear();
            _animations.Clear();
            _portRowInfo.Clear();
            _edgeRecords.Clear();
            _layoutPositions.Clear();
            _nodeSig.Clear();
            _edgesLayer?.MarkDirtyRepaint();
            if(fsm == null) return;
            fsm.ReinjectOwners();
            var positions = ComputeAutoLayout(fsm);
            _layoutPositions.Clear();
            foreach(var kv in positions) _layoutPositions[kv.Key] = kv.Value;
            var animate = _hasLoadedOnce;
            foreach(var meta in fsm.Nodes) {
                var pos = positions.TryGetValue(meta.id,out var p) ? p : new Vector2(40,40);
                Vector2 initialPos;
                if(animate == false) {
                    initialPos = pos;
                } else if(oldPositions.TryGetValue(meta.id,out var old)) {
                    initialPos = Vector2.Distance(old,pos) <= 0.5f ? pos : old;
                } else {
                    initialPos = ResolveSpawnPosition(fsm,meta.id,positions,oldPositions,pos);
                }
                var node = CreateNode(fsm,meta,initialPos,positions);
                AddElement(node);
                _edgesLayer.SendToBack();
                _nodeByID[meta.id] = node;
                node.transform.position = new Vector3(initialPos.x,initialPos.y,0);
                node.RegisterCallback<GeometryChangedEvent>(_ => _edgesLayer?.MarkDirtyRepaint());
                if(animate && Vector2.Distance(initialPos,pos) > 0.5f) {
                    _animations[node] = new AnimState { start = initialPos,end = pos,elapsed = 0f,duration = 0.25f };
                }
            }
            _hasLoadedOnce = true;
            _nodeSig.Clear();
            foreach(var meta in fsm.Nodes) _nodeSig[meta.id] = ComputeNodeSig(meta);
            RebuildEdgeRecords(fsm);
            UpdateHighlights();
            _isClearingForReload = false;
        }
        private void DrawCustomEdges(MeshGenerationContext ctx) {
            var p = ctx.painter2D;
            p.lineWidth = 2.5f;
            p.strokeColor = new Color(0.85f,0.85f,0.85f,0.65f);
            foreach(var rec in _edgeRecords) {
                if(rec.outputPort == null || rec.targetNode == null) continue;
                if(_isDraggingEdge && rec.outputPort == _draggingPort) continue;
                var sourceNode = rec.outputPort.GetFirstAncestorOfType<Node>();
                if(sourceNode == null) continue;
                var sourceCenter = sourceNode.worldBound.center;
                var portCenter = rec.outputPort.worldBound.center;
                var sourcePortOnLeft = portCenter.x < sourceCenter.x;
                var portEdgeWorld = sourcePortOnLeft
                    ? new Vector2(rec.outputPort.worldBound.x,portCenter.y)
                    : new Vector2(rec.outputPort.worldBound.xMax,portCenter.y);
                var isSelfLoop = sourceNode == rec.targetNode;
                Vector2 inWorld;
                bool toReceivesFromRight;
                if(isSelfLoop) {
                    toReceivesFromRight = sourcePortOnLeft == false;
                    var targetEdgeY = rec.targetNode.worldBound.center.y;
                    inWorld = sourcePortOnLeft
                        ? new Vector2(rec.targetNode.worldBound.x,targetEdgeY)
                        : new Vector2(rec.targetNode.worldBound.xMax,targetEdgeY);
                } else {
                    var targetCenter = rec.targetNode.worldBound.center;
                    toReceivesFromRight = targetCenter.x < sourceCenter.x;
                    inWorld = toReceivesFromRight
                        ? new Vector2(rec.targetNode.worldBound.xMax,rec.targetNode.worldBound.center.y)
                        : new Vector2(rec.targetNode.worldBound.x,rec.targetNode.worldBound.center.y);
                }
                var fromLocal = _edgesLayer.WorldToLocal(portEdgeWorld);
                var toLocal = _edgesLayer.WorldToLocal(inWorld);
                DrawCurveWithArrow(p,fromLocal,toLocal,sourcePortOnLeft,toReceivesFromRight);
            }
            if(_hasDragGhost && _draggingPort != null) {
                var sourceNode = _draggingPort.GetFirstAncestorOfType<Node>();
                if(sourceNode != null) {
                    var sourceCenter = sourceNode.worldBound.center;
                    var cursorWorld = _edgesLayer.LocalToWorld(_dragGhostPos);
                    var dragsLeft = cursorWorld.x < sourceCenter.x;
                    var portY = _draggingPort.worldBound.center.y;
                    var portEdgeWorld = dragsLeft
                        ? new Vector2(sourceNode.worldBound.x,portY)
                        : new Vector2(sourceNode.worldBound.xMax,portY);
                    Vector2 toLocalDrag;
                    bool toReceivesFromRightDrag;
                    if(_dropTargetNode != null && _dropTargetNode != sourceNode) {
                        var targetIsLeftOfSource = _dropTargetNode.worldBound.center.x < sourceCenter.x;
                        var inWorld = targetIsLeftOfSource
                            ? new Vector2(_dropTargetNode.worldBound.xMax,_dropTargetNode.worldBound.center.y)
                            : new Vector2(_dropTargetNode.worldBound.x,_dropTargetNode.worldBound.center.y);
                        toLocalDrag = _edgesLayer.WorldToLocal(inWorld);
                        toReceivesFromRightDrag = targetIsLeftOfSource;
                    } else {
                        toLocalDrag = _dragGhostPos;
                        toReceivesFromRightDrag = false;
                    }
                    DrawCurveWithArrow(p,_edgesLayer.WorldToLocal(portEdgeWorld),toLocalDrag,dragsLeft,toReceivesFromRightDrag);
                }
            }
        }
        private void DrawCurveWithArrow(UnityEngine.UIElements.Painter2D p,Vector2 fromLocal,Vector2 toLocal,bool fromOnLeft,bool toReceivesFromRight) {
            var dx = Mathf.Max(50f,Mathf.Abs(toLocal.x - fromLocal.x) * 0.5f);
            var fromOutDir = fromOnLeft ? -1f : 1f;
            var toInDir = toReceivesFromRight ? 1f : -1f;
            var c1 = new Vector2(fromLocal.x + fromOutDir * dx,fromLocal.y);
            var c2 = new Vector2(toLocal.x + toInDir * dx,toLocal.y);
            p.BeginPath();
            p.MoveTo(fromLocal);
            p.BezierCurveTo(c1,c2,toLocal);
            p.Stroke();
            var arrowDir = (toLocal - c2).normalized;
            if(arrowDir.sqrMagnitude < 0.001f) arrowDir = new Vector2(toReceivesFromRight ? 1 : -1,0);
            var perp = new Vector2(-arrowDir.y,arrowDir.x);
            var tip = toLocal;
            var basePt = toLocal - arrowDir * 8f;
            var left = basePt + perp * 4f;
            var right = basePt - perp * 4f;
            p.fillColor = new Color(0.85f,0.85f,0.85f,0.65f);
            p.BeginPath();
            p.MoveTo(tip);
            p.LineTo(left);
            p.LineTo(right);
            p.ClosePath();
            p.Fill();
        }
        private static Vector2 ResolveSpawnPosition(MornStateMachine fsm,int nodeID,Dictionary<int,Vector2> positions,Dictionary<int,Vector2> oldPositions,Vector2 fallback) {
            foreach(var n in fsm.Nodes) {
                foreach(var b in n.behaviours) {
                    if(b == null) continue;
                    foreach(var f in b.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                        if(f.FieldType != typeof(Connection)) continue;
                        if(f.GetValue(b) is not Connection link) continue;
                        if(link == null || link.stateID != nodeID) continue;
                        if(oldPositions.TryGetValue(n.id,out var op)) return op;
                        if(positions.TryGetValue(n.id,out var np)) return np;
                    }
                }
            }
            return fallback;
        }
        private struct AnimState { public Vector2 start; public Vector2 end; public float elapsed; public float duration; }
        private readonly Dictionary<Node,AnimState> _animations = new();
        private double _lastAnimTime;
        private void TickAnimations() {
            if(_animations.Count == 0) { _lastAnimTime = EditorApplication.timeSinceStartup; return; }
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastAnimTime);
            _lastAnimTime = now;
            if(dt <= 0 || dt > 0.25f) dt = 0.016f;
            var finished = new List<Node>();
            var keys = new List<Node>(_animations.Keys);
            foreach(var node in keys) {
                var a = _animations[node];
                a.elapsed += dt;
                var t = Mathf.Clamp01(a.elapsed / a.duration);
                var eased = 1f - Mathf.Pow(1f - t,3f);
                var cur = Vector2.Lerp(a.start,a.end,eased);
                node.transform.position = new Vector3(cur.x,cur.y,0);
                if(t >= 1f) finished.Add(node);
                else _animations[node] = a;
            }
            foreach(var n in finished) _animations.Remove(n);
            _edgesLayer?.MarkDirtyRepaint();
        }
        private int _lastCurrentSnapshot = int.MinValue;
        private void OnEditorUpdate() {
            TickAnimations();
            var snapshot = Application.isPlaying && _target != null ? _target.CurrentStateID : 0;
            if(snapshot != _lastCurrentSnapshot) {
                _lastCurrentSnapshot = snapshot;
                UpdateHighlights();
            }
            PollDragGhost();
            ReconcilePortSides();
            _edgesLayer?.MarkDirtyRepaint();
        }
        private readonly List<Port> _portsToUpdate = new();
        private void ReconcilePortSides() {
            if(_portRowInfo.Count == 0) return;
            _portsToUpdate.Clear();
            foreach(var pair in _portRowInfo) {
                if(pair.Key == _draggingPort) continue;
                if(pair.Value.link == null || pair.Value.link.stateID == 0) continue;
                var sourceNode = pair.Key.GetFirstAncestorOfType<Node>();
                if(sourceNode == null) continue;
                if(_nodeByID.TryGetValue(pair.Value.link.stateID,out var targetNode) == false) continue;
                if(sourceNode == targetNode) continue;
                var isBack = targetNode.worldBound.center.x < sourceNode.worldBound.center.x;
                if(isBack != pair.Value.placeOnLeft) _portsToUpdate.Add(pair.Key);
            }
            foreach(var port in _portsToUpdate) {
                var info = _portRowInfo[port];
                var sourceNode = port.GetFirstAncestorOfType<Node>();
                if(sourceNode == null) continue;
                if(_nodeByID.TryGetValue(info.link.stateID,out var targetNode) == false) continue;
                var isBack = targetNode.worldBound.center.x < sourceNode.worldBound.center.x;
                ApplyPortRowSide(info.row,port,info.label,isBack);
                _portRowInfo[port] = (info.row,info.label,isBack,info.link);
            }
        }
        private Vector2 _dragGhostPos;
        private bool _hasDragGhost;
        private void PollDragGhost() {
            _hasDragGhost = false;
            if(_isDraggingEdge == false) return;
            Edge ghost = null;
            foreach(var e in edges) {
                if(e.input == null || e.output == null) { ghost = e; break; }
            }
            if(ghost == null) {
                if(_dropTargetNode != null) ClearDropTargetHighlight();
                return;
            }
            ghost.style.visibility = Visibility.Hidden;
            var pos = ghost.candidatePosition;
            _dragGhostPos = _edgesLayer.WorldToLocal(pos);
            _hasDragGhost = true;
            _edgesLayer.MarkDirtyRepaint();
            Node target = null;
            foreach(var pair in _nodeByID) {
                if(pair.Value.worldBound.Contains(pos)) { target = pair.Value; break; }
            }
            if(target == _dropTargetNode) return;
            ClearDropTargetHighlight();
            _dropTargetNode = target;
            if(target != null) ApplyDropTargetHighlight(target);
        }
        private const int HighlightBorderWidth = 4;
        private void UpdateHighlights() {
            if(_target == null) return;
            var startID = _target.startStateID;
            var currentID = Application.isPlaying ? _target.CurrentStateID : 0;
            foreach(var pair in _nodeByID) {
                var n = pair.Value;
                var isStart = pair.Key == startID;
                var isCurrent = pair.Key == currentID;
                var borderColor = isCurrent ? new Color(1.00f,0.70f,0.20f) : new Color(0f,0f,0f,0f);
                ApplyBorder(n,borderColor);
                n.titleContainer.style.backgroundColor = isStart ? new Color(0.20f,0.55f,0.30f,0.55f) : new StyleColor(StyleKeyword.Null);
            }
        }
        private static void ApplyBorder(Node n,Color color) {
            n.style.borderTopColor = color;
            n.style.borderBottomColor = color;
            n.style.borderLeftColor = color;
            n.style.borderRightColor = color;
            n.style.borderTopWidth = HighlightBorderWidth;
            n.style.borderBottomWidth = HighlightBorderWidth;
            n.style.borderLeftWidth = HighlightBorderWidth;
            n.style.borderRightWidth = HighlightBorderWidth;
        }
        private static IEnumerable<(string fieldName,Connection link)> EnumerateConnectionFields(MornStateBehaviour state) {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach(var f in state.GetType().GetFields(flags)) {
                if(f.FieldType != typeof(Connection)) continue;
                if(f.GetValue(state) is Connection link) yield return (f.Name,link);
            }
        }
        private static Port FindInputPort(Node node,bool right) {
            var container = right ? node.outputContainer : node.inputContainer;
            for(var i = 0;i < container.childCount;i++) {
                if(container[i] is Port p && p.direction == Direction.Input) return p;
            }
            return null;
        }
        private static Port FindOutputPortFor(Node node,MornStateBehaviour state,Connection link) {
            Port found = null;
            node.Query<Port>().ForEach(p => {
                if(found != null) return;
                if(p.direction != Direction.Output) return;
                if(p.userData is System.ValueTuple<MornStateBehaviour,Connection> tup && tup.Item1 == state && tup.Item2 == link) {
                    found = p;
                }
            });
            return found;
        }
        private static float EstimateNodeHeight(MornStateMachine.StateNode meta) {
            var h = 90f;
            if(meta == null || meta.behaviours == null) return h;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach(var b in meta.behaviours) {
                if(b == null) continue;
                h += 50f;
                var fieldCount = 0;
                foreach(var f in b.GetType().GetFields(flags)) {
                    if(f.FieldType == typeof(Connection)) h += 22f;
                    else fieldCount++;
                }
                h += fieldCount * 22f;
            }
            return h;
        }
        private Dictionary<int,Vector2> ComputeAutoLayout(MornStateMachine fsm) {
            const float colWidth = 320f;
            const float spacing = 30f;
            var positions = new Dictionary<int,Vector2>();
            var allIDs = new HashSet<int>();
            foreach(var n in fsm.Nodes) allIDs.Add(n.id);
            if(allIDs.Count == 0) return positions;
            var adj = new Dictionary<int,List<int>>();
            foreach(var n in fsm.Nodes) adj[n.id] = new List<int>();
            foreach(var n in fsm.Nodes) {
                foreach(var b in n.behaviours) {
                    if(b == null) continue;
                    foreach(var (_,link) in EnumerateConnectionFields(b)) {
                        if(link == null || link.stateID == 0) continue;
                        if(allIDs.Contains(link.stateID) && adj[n.id].Contains(link.stateID) == false) {
                            adj[n.id].Add(link.stateID);
                        }
                    }
                }
            }
            var start = fsm.startStateID != 0 && allIDs.Contains(fsm.startStateID) ? fsm.startStateID : 0;
            if(start == 0) foreach(var n in fsm.Nodes) { start = n.id; break; }
            var depths = new Dictionary<int,int>();
            var bfsOrder = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            depths[start] = 0;
            bfsOrder.Add(start);
            while(queue.Count > 0) {
                var cur = queue.Dequeue();
                var d = depths[cur];
                foreach(var next in adj[cur]) {
                    if(depths.ContainsKey(next)) continue;
                    depths[next] = d + 1;
                    bfsOrder.Add(next);
                    queue.Enqueue(next);
                }
            }
            var reverseAdj = new Dictionary<int,List<int>>();
            foreach(var id in allIDs) reverseAdj[id] = new List<int>();
            foreach(var pair in adj) {
                foreach(var to in pair.Value) reverseAdj[to].Add(pair.Key);
            }
            var backQueue = new Queue<int>();
            backQueue.Enqueue(start);
            while(backQueue.Count > 0) {
                var cur = backQueue.Dequeue();
                var d = depths[cur];
                foreach(var prev in reverseAdj[cur]) {
                    if(depths.ContainsKey(prev)) continue;
                    depths[prev] = d - 1;
                    bfsOrder.Add(prev);
                    backQueue.Enqueue(prev);
                }
            }
            var orphans = new HashSet<int>();
            foreach(var id in allIDs) {
                if(depths.ContainsKey(id)) continue;
                orphans.Add(id);
                depths[id] = 0;
                bfsOrder.Add(id);
            }
            var bfsDepths = new Dictionary<int,int>(depths);
            var cap = allIDs.Count + 2;
            for(var pass = 0;pass < cap;pass++) {
                var changed = false;
                foreach(var n in fsm.Nodes) {
                    if(orphans.Contains(n.id)) continue;
                    if(n.id == start) continue;
                    if(depths[n.id] < 0) continue;
                    var maxParent = -1;
                    foreach(var n2 in fsm.Nodes) {
                        if(n2.id == n.id) continue;
                        if(adj[n2.id].Contains(n.id) == false) continue;
                        if(bfsDepths.TryGetValue(n2.id,out var bfsP) && bfsDepths.TryGetValue(n.id,out var bfsN) && bfsP > bfsN) continue;
                        if(depths[n2.id] >= 0 && depths[n2.id] > maxParent) maxParent = depths[n2.id];
                    }
                    var desired = maxParent + 1;
                    if(desired > depths[n.id] && desired < cap) {
                        depths[n.id] = desired;
                        changed = true;
                    }
                }
                if(changed == false) break;
            }
            var maxNonOrphan = int.MinValue;
            foreach(var kv in depths) {
                if(orphans.Contains(kv.Key)) continue;
                if(kv.Value > maxNonOrphan) maxNonOrphan = kv.Value;
            }
            var orphanDepth = maxNonOrphan == int.MinValue ? 1 : maxNonOrphan + 1;
            foreach(var id in orphans) depths[id] = orphanDepth;
            var byDepth = new SortedDictionary<int,List<int>>();
            foreach(var id in bfsOrder) {
                var d = depths[id];
                if(byDepth.TryGetValue(d,out var list) == false) {
                    list = new List<int>();
                    byDepth[d] = list;
                }
                list.Add(id);
            }
            var orderedByDepth = new SortedDictionary<int,List<int>>();
            var bcByID = new Dictionary<int,double>();
            foreach(var pair in byDepth) {
                if(pair.Key == 0) {
                    orderedByDepth[0] = new List<int>(pair.Value);
                    for(var i = 0;i < pair.Value.Count;i++) bcByID[pair.Value[i]] = i;
                    continue;
                }
                var bcList = new List<(int id,double bc)>();
                foreach(var nodeId in pair.Value) {
                    double sum = 0; var count = 0;
                    foreach(var pn in fsm.Nodes) {
                        if(bcByID.TryGetValue(pn.id,out var pBC) == false) continue;
                        var outs = adj[pn.id];
                        for(var j = 0;j < outs.Count;j++) {
                            if(outs[j] != nodeId) continue;
                            var portFrac = outs.Count > 1 ? (double)j / (outs.Count - 1) : 0.0;
                            sum += pBC + portFrac;
                            count++;
                        }
                    }
                    bcList.Add((nodeId,count > 0 ? sum / count : 0));
                }
                bcList.Sort((a,b) => a.bc.CompareTo(b.bc));
                var ordered = new List<int>();
                foreach(var entry in bcList) {
                    ordered.Add(entry.id);
                    bcByID[entry.id] = entry.bc;
                }
                orderedByDepth[pair.Key] = ordered;
            }
            var minDepth = 0;
            foreach(var pair in orderedByDepth) if(pair.Key < minDepth) minDepth = pair.Key;
            foreach(var pair in orderedByDepth) {
                var x = 40f + (pair.Key - minDepth) * colWidth;
                var y = 40f;
                foreach(var id in pair.Value) {
                    positions[id] = new Vector2(x,y);
                    var meta = fsm.FindNode(id);
                    y += EstimateNodeHeight(meta) + spacing;
                }
            }
            return positions;
        }
        private Node CreateNode(MornStateMachine fsm,MornStateMachine.StateNode meta,Vector2 pos,Dictionary<int,Vector2> allPositions) {
            var displayName = string.IsNullOrEmpty(meta.name) == false
                ? meta.name
                : meta.behaviours.Count > 0 && meta.behaviours[0] != null
                    ? meta.behaviours[0].GetType().Name
                    : $"State {meta.id}";
            var node = new Node { userData = meta.id };
            var titleLabel = node.titleContainer.Q<Label>("title-label");
            if(titleLabel != null) titleLabel.style.display = DisplayStyle.None;
            node.titleContainer.style.height = 26;
            node.titleContainer.style.minHeight = 26;
            node.titleContainer.style.paddingTop = 4;
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
            node.transform.position = new Vector3(pos.x,pos.y,0);
            node.style.width = 260;
            node.style.minWidth = 260;
            node.style.maxWidth = 260;
            node.style.backgroundColor = new Color(0.20f,0.20f,0.20f,0.85f);
            node.mainContainer.style.backgroundColor = new Color(0,0,0,0);
            node.extensionContainer.style.backgroundColor = new Color(0,0,0,0);
            var topDivider = node.Q("divider");
            if(topDivider != null) topDivider.style.backgroundColor = new Color(0,0,0,0);
            node.capabilities &= ~Capabilities.Movable;
            node.capabilities &= ~Capabilities.Collapsible;
            var collapseButton = node.titleContainer.Q("title-button-container");
            if(collapseButton != null) collapseButton.style.display = DisplayStyle.None;
            var inPort = node.InstantiatePort(Orientation.Horizontal,Direction.Input,Port.Capacity.Multi,typeof(MornStateBehaviour));
            inPort.portName = "";
            inPort.style.visibility = Visibility.Hidden;
            inPort.style.width = 0;
            inPort.style.height = 0;
            node.inputContainer.Add(inPort);
            node.inputContainer.style.display = DisplayStyle.None;
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
            return node;
        }
        private void AddBehaviourSection(VisualElement parent,Node node,MornStateMachine fsm,int stateID,int behaviourIndex,MornStateBehaviour state,SerializedObject so) {
            var section = new VisualElement();
            section.style.marginBottom = 4;
            section.style.borderTopWidth = 1;
            section.style.borderTopColor = new Color(1f,1f,1f,0.1f);
            section.style.paddingTop = 2;
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 2;
            var script = FindScriptAsset(state.GetType());
            var titleField = new ObjectField {
                value = script,
                objectType = typeof(MonoScript),
                allowSceneObjects = false,
            };
            titleField.SetEnabled(false);
            titleField.style.flexGrow = 1;
            titleField.style.marginTop = 0;
            titleField.style.marginBottom = 0;
            titleField.style.height = 18;
            titleField.style.minHeight = 18;
            var titleFieldInput = titleField.Q(className: "unity-object-field__input");
            if(titleFieldInput != null) {
                titleFieldInput.style.height = 18;
                titleFieldInput.style.minHeight = 18;
            }
            header.Add(titleField);
            var metaForButtons = fsm.FindNode(stateID);
            var totalCount = metaForButtons != null ? metaForButtons.behaviours.Count : 0;
            var btnUp = new Button(() => MoveBehaviour(stateID,behaviourIndex,-1)) { text = "↑" };
            btnUp.style.width = 22; btnUp.style.height = 18;
            btnUp.SetEnabled(behaviourIndex > 0);
            header.Add(btnUp);
            var btnDown = new Button(() => MoveBehaviour(stateID,behaviourIndex,1)) { text = "↓" };
            btnDown.style.width = 22; btnDown.style.height = 18;
            btnDown.SetEnabled(behaviourIndex < totalCount - 1);
            header.Add(btnDown);
            var del = new Button(() => RemoveBehaviour(stateID,state)) { text = "x" };
            del.style.width = 22; del.style.height = 18;
            header.Add(del);
            section.Add(header);
            var nodeIndex = fsm.NodesMutable.FindIndex(n => n.id == stateID);
            if(nodeIndex >= 0) {
                var bProp = so.FindProperty("_nodes")
                    .GetArrayElementAtIndex(nodeIndex)
                    .FindPropertyRelative("behaviours")
                    .GetArrayElementAtIndex(behaviourIndex);
                MornStateBehaviourPropertyDrawer.BuildFields(section,bProp,skipConnections: true);
                MornStateBehaviourPropertyDrawer.BuildMethodAttributes(section,bProp);
                section.Bind(so);
            }
            foreach(var (fieldName,link) in EnumerateConnectionFields(state)) {
                var isBack = link.stateID != 0
                    && link.stateID != stateID
                    && _layoutPositions.TryGetValue(link.stateID,out var tp)
                    && _layoutPositions.TryGetValue(stateID,out var sp)
                    && tp.x < sp.x;
                section.Add(CreateOutputPortRow(node,state,link,fieldName,isBack));
            }
            parent.Add(section);
        }
        private VisualElement CreateOutputPortRow(Node node,MornStateBehaviour state,Connection link,string fieldName,bool placeOnLeft) {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2;
            var label = new Label(fieldName);
            label.style.flexGrow = 1;
            var port = node.InstantiatePort(Orientation.Horizontal,Direction.Output,Port.Capacity.Single,typeof(MornStateBehaviour));
            port.portName = "";
            port.userData = (state,link);
            port.RegisterCallback<MouseDownEvent>(e => { if(e.button == 0) { _isDraggingEdge = true; _draggingPort = port; } },TrickleDown.TrickleDown);
            port.RegisterCallback<PointerDownEvent>(e => { if(e.button == 0) { _isDraggingEdge = true; _draggingPort = port; } },TrickleDown.TrickleDown);
            port.RegisterCallback<PointerMoveEvent>(e => { if(_isDraggingEdge) HandleDragMove(e.position); },TrickleDown.TrickleDown);
            port.RegisterCallback<MouseMoveEvent>(e => { if(_isDraggingEdge) HandleDragMove(e.mousePosition); },TrickleDown.TrickleDown);
            port.RegisterCallback<PointerUpEvent>(_ => StopEdgeDrag(),TrickleDown.TrickleDown);
            port.RegisterCallback<MouseUpEvent>(_ => StopEdgeDrag(),TrickleDown.TrickleDown);
            if(port.edgeConnector != null) port.RemoveManipulator(port.edgeConnector);
            port.AddManipulator(new EdgeConnector<Edge>(new NodeDropConnectorListener(this)));
            port.RegisterCallback<MouseDownEvent>(e => {
                if(e.button == 1 && link.stateID != 0) {
                    Undo.RegisterCompleteObjectUndo(_target,"Disconnect");
                    link.stateID = 0;
                    EditorUtility.SetDirty(_target);
                    EditorApplication.delayCall += () => LoadStateMachine(_target);
                    e.StopPropagation();
                }
            });
            ApplyPortRowSide(row,port,label,placeOnLeft);
            _portRowInfo[port] = (row,label,placeOnLeft,link);
            return row;
        }
        private bool _isDraggingEdge;
        private Port _draggingPort;
        private Node _dropTargetNode;
        private readonly Dictionary<Port,(VisualElement row,Label label,bool placeOnLeft,Connection link)> _portRowInfo = new();
        private static void ApplyPortRowSide(VisualElement row,Port port,Label label,bool placeOnLeft) {
            if(port.parent != row) row.Add(port);
            if(label.parent != row) row.Add(label);
            row.style.flexDirection = placeOnLeft ? FlexDirection.Row : FlexDirection.RowReverse;
            label.style.unityTextAlign = placeOnLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            label.style.marginLeft = placeOnLeft ? 4 : 0;
            label.style.marginRight = placeOnLeft ? 0 : 4;
        }
        private void OnEdgeDragMove(MouseMoveEvent e) {
            if(_isDraggingEdge == false) return;
            if((e.pressedButtons & 1) == 0) {
                StopEdgeDrag();
                return;
            }
            HandleDragMove(e.mousePosition);
        }
        private void OnEdgePointerMove(PointerMoveEvent e) {
            if(_isDraggingEdge == false) return;
            HandleDragMove(e.position);
        }
        private int _dragMoveCount;
        private void HandleDragMove(Vector2 pos) {
            if(_draggingPort != null && _portRowInfo.TryGetValue(_draggingPort,out var info)) {
                var sourceNode = _draggingPort.GetFirstAncestorOfType<Node>();
                if(sourceNode != null) {
                    var dragsLeft = pos.x < sourceNode.worldBound.center.x;
                    if(dragsLeft != info.placeOnLeft) {
                        ApplyPortRowSide(info.row,_draggingPort,info.label,dragsLeft);
                        _portRowInfo[_draggingPort] = (info.row,info.label,dragsLeft,info.link);
                    }
                }
            }
            Node target = null;
            foreach(var pair in _nodeByID) {
                if(pair.Value.worldBound.Contains(pos)) { target = pair.Value; break; }
            }
            if(target == _dropTargetNode) return;
            ClearDropTargetHighlight();
            _dropTargetNode = target;
            if(target != null) ApplyDropTargetHighlight(target);
        }
        private void StopEdgeDrag() {
            if(_isDraggingEdge == false) return;
            _isDraggingEdge = false;
            _draggingPort = null;
            ClearDropTargetHighlight();
            _edgesLayer?.MarkDirtyRepaint();
        }
        private static void ApplyDropTargetHighlight(Node node) {
            ApplyBorder(node,new Color(1.00f,0.20f,0.20f));
        }
        private void ClearDropTargetHighlight() {
            if(_dropTargetNode == null) return;
            _dropTargetNode = null;
            UpdateHighlights();
        }
        public void ConnectFromOutputToNode(Port outputPort,Node targetNode) {
            if(outputPort == null || targetNode == null || _target == null) return;
            if(outputPort.userData is not System.ValueTuple<MornStateBehaviour,Connection> tup) return;
            if(targetNode.userData is not int targetID) return;
            var input = targetNode.inputContainer.Q<Port>();
            if(input == null) return;
            Undo.RegisterCompleteObjectUndo(_target,"Connect Edge");
            tup.Item2.stateID = targetID;
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        public void DisconnectOutput(Port outputPort) {
            if(outputPort == null || _target == null) return;
            if(outputPort.userData is not System.ValueTuple<MornStateBehaviour,Connection> tup) return;
            if(tup.Item2.stateID == 0) return;
            Undo.RegisterCompleteObjectUndo(_target,"Disconnect Edge");
            tup.Item2.stateID = 0;
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        private class NodeDropConnectorListener : IEdgeConnectorListener {
            private readonly MornStateMachineGraphView _view;
            public NodeDropConnectorListener(MornStateMachineGraphView view) { _view = view; }
            public void OnDropOutsidePort(Edge edge,Vector2 position) {
                var picked = new List<VisualElement>();
                _view.panel.PickAll(position,picked);
                Node target = null;
                foreach(var elem in picked) {
                    for(VisualElement cur = elem;cur != null;cur = cur.parent) {
                        if(cur is Node n) { target = n; break; }
                    }
                    if(target != null) break;
                }
                if(target == null) {
                    _view.DisconnectOutput(edge.output);
                    return;
                }
                _view.ConnectFromOutputToNode(edge.output,target);
            }
            public void OnDrop(GraphView graphView,Edge edge) {
                var change = new GraphViewChange { edgesToCreate = new List<Edge> { edge } };
                ((MornStateMachineGraphView)graphView).InvokeGraphViewChanged(change);
            }
        }
        public void InvokeGraphViewChanged(GraphViewChange change) {
            OnGraphViewChanged(change);
            if(change.edgesToCreate != null) {
                foreach(var e in change.edgesToCreate) AddElement(e);
            }
        }
        private void RenameNode(int stateID,string newName) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            if(meta == null) return;
            Undo.RecordObject(_target,"Rename State");
            meta.name = newName;
            EditorUtility.SetDirty(_target);
        }
        private static readonly Dictionary<System.Type,MonoScript> _scriptCache = new();
        private static MonoScript FindScriptAsset(System.Type type) {
            if(type == null) return null;
            if(_scriptCache.TryGetValue(type,out var cached)) return cached;
            MonoScript found = null;
            foreach(var guid in AssetDatabase.FindAssets($"t:MonoScript {type.Name}")) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if(script != null && script.GetClass() == type) { found = script; break; }
            }
            _scriptCache[type] = found;
            return found;
        }
        private void MoveBehaviour(int stateID,int fromIndex,int delta) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            if(meta == null) return;
            var toIndex = fromIndex + delta;
            if(toIndex < 0 || toIndex >= meta.behaviours.Count) return;
            Undo.RegisterCompleteObjectUndo(_target,"Reorder Behaviour");
            (meta.behaviours[fromIndex],meta.behaviours[toIndex]) = (meta.behaviours[toIndex],meta.behaviours[fromIndex]);
            EditorUtility.SetDirty(_target);
            EditorApplication.delayCall += () => LoadStateMachine(_target);
        }
        private void RemoveBehaviour(int stateID,MornStateBehaviour state) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            if(meta == null) return;
            Undo.RegisterCompleteObjectUndo(_target,"Remove Behaviour");
            meta.behaviours.Remove(state);
            EditorUtility.SetDirty(_target);
            EditorApplication.delayCall += () => LoadStateMachine(_target);
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
                name = "New Node",
            });
            if(_target.startStateID == 0) _target.startStateID = newID;
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        public void CreateStateAt(System.Type type,Vector2 graphPos) {
            if(_target == null) return;
            var newID = AllocateUniqueStateID();
            Undo.RegisterCompleteObjectUndo(_target,"Create State");
            var instance = (MornStateBehaviour)System.Activator.CreateInstance(type);
            instance.StateID = newID;
            var meta = new MornStateMachine.StateNode {
                id = newID,
                name = "New Node",
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
            var instance = (MornStateBehaviour)System.Activator.CreateInstance(type);
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
            if(_isClearingForReload) return change;
            if(change.edgesToCreate != null) {
                foreach(var edge in change.edgesToCreate) {
                    if(edge.output.userData is System.ValueTuple<MornStateBehaviour,Connection> tup
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
                        case Edge e when e.output != null && e.output.userData is System.ValueTuple<MornStateBehaviour,Connection> tup:
                            tup.Item2.stateID = 0;
                            anyEdge = true;
                            break;
                        case Node n when n.userData is int sid:
                            removedIDs.Add(sid);
                            break;
                    }
                }
                if(anyEdge) {
                    EditorUtility.SetDirty(_target);
                    EditorApplication.delayCall += () => LoadStateMachine(_target);
                }
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
                        if(f.FieldType != typeof(Connection)) continue;
                        if(f.GetValue(b) is not Connection link) continue;
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
                    var entry = new NodeEntry { name = meta.name };
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
                };
                foreach(var b in entry.behaviours) {
                    var type = System.Type.GetType(b.typeName);
                    if(type == null) continue;
                    var instance = (MornStateBehaviour)System.Activator.CreateInstance(type);
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
