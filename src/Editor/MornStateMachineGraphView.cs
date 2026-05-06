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
        private VisualElement _edgesLayerFront;
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
            _edgesLayerFront = new VisualElement();
            _edgesLayerFront.style.position = Position.Absolute;
            _edgesLayerFront.style.left = 0;
            _edgesLayerFront.style.top = 0;
            _edgesLayerFront.style.width = 100000;
            _edgesLayerFront.style.height = 100000;
            _edgesLayerFront.pickingMode = PickingMode.Ignore;
            _edgesLayerFront.generateVisualContent += DrawEdgeStubsFront;
            contentViewContainer.Add(_edgesLayerFront);
            _edgesLayerFront.schedule.Execute(() => {
                if(_edgesLayerFront.parent != null) _edgesLayerFront.BringToFront();
            }).Every(100);
            graphViewChanged += OnGraphViewChanged;
            RegisterCallback<PointerMoveEvent>(OnEdgePointerMove,TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(e => UpdateBehaviourDragHover(e.position),TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(e => EndBehaviourDrag(e.position),TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(e => EndBehaviourDrag(e.mousePosition),TrickleDown.TrickleDown);
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
        private bool _edgeLayoutPending;
        private int _hoveredNodeID;
        private readonly HashSet<int> _hoveredRelatedIDs = new();
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
            _edgesLayer.MarkDirtyRepaint(); _edgesLayerFront?.MarkDirtyRepaint();
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
                    var cur = GetAbsolutePosition(node);
                    if(Vector2.Distance(cur,pos) > 0.5f) {
                        node.SetPosition(new Rect(pos.x,pos.y,0,0));
                        var off = cur - pos;
                        node.transform.position = new Vector3(off.x,off.y,0);
                        _animations[node] = new AnimState { start = cur,end = pos,elapsed = 0f,duration = 0.25f };
                    }
                }
                RebuildEdgeRecords(fsm);
                UpdateHighlights();
                return;
            }
            _isClearingForReload = true;
            _edgeLayoutPending = true;
            var oldPositions = new Dictionary<int,Vector2>();
            foreach(var pair in _nodeByID) {
                oldPositions[pair.Key] = GetAbsolutePosition(pair.Value);
            }
            _target = fsm;
            graphElements.ForEach(RemoveElement);
            _nodeByID.Clear();
            _sectionsByNode.Clear();
            _animations.Clear();
            _portRowInfo.Clear();
            _edgeRecords.Clear();
            _layoutPositions.Clear();
            _nodeSig.Clear();
            _edgesLayer?.MarkDirtyRepaint(); _edgesLayerFront?.MarkDirtyRepaint();
            if(fsm == null) return;
            fsm.ReinjectOwners();
            var positions = ComputeAutoLayout(fsm);
            _layoutPositions.Clear();
            foreach(var kv in positions) _layoutPositions[kv.Key] = kv.Value;
            var animate = _hasLoadedOnce;
            var pendingLayoutCount = new[] { fsm.Nodes.Count };
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
                var restPos = animate ? pos : initialPos;
                node.SetPosition(new Rect(restPos.x,restPos.y,0,0));
                var offset = initialPos - restPos;
                node.transform.position = new Vector3(offset.x,offset.y,0);
                node.style.visibility = Visibility.Hidden;
                EventCallback<GeometryChangedEvent> firstLayout = null;
                firstLayout = _ => {
                    node.style.visibility = Visibility.Visible;
                    node.UnregisterCallback(firstLayout);
                    pendingLayoutCount[0]--;
                    if(pendingLayoutCount[0] <= 0) {
                        _edgeLayoutPending = false;
                        _edgesLayer?.MarkDirtyRepaint();
                        _edgesLayerFront?.MarkDirtyRepaint();
                    }
                };
                node.RegisterCallback(firstLayout);
                node.RegisterCallback<GeometryChangedEvent>(_ => { _edgesLayer?.MarkDirtyRepaint(); _edgesLayerFront?.MarkDirtyRepaint(); });
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
            if(pendingLayoutCount[0] <= 0) _edgeLayoutPending = false;
        }
        private void DrawCustomEdges(MeshGenerationContext ctx) {
            if(_edgeLayoutPending) return;
            var p = ctx.painter2D;
            p.lineWidth = 2.5f;
            p.strokeColor = new Color(0.85f,0.85f,0.85f,0.65f);
            var backEdgeIndex = ComputeBackEdgeIndices();
            for(var ri = 0;ri < _edgeRecords.Count;ri++) {
                var rec = _edgeRecords[ri];
                if(rec.outputPort == null || rec.targetNode == null) continue;
                if(_isDraggingEdge && rec.outputPort == _draggingPort) continue;
                var sourceNode = rec.outputPort.GetFirstAncestorOfType<Node>();
                if(sourceNode == null) continue;
                var sourceID = sourceNode.userData is int ssid ? ssid : 0;
                var emphasized = _hoveredNodeID != 0 && sourceID == _hoveredNodeID;
                var dimmed = _hoveredNodeID != 0 && emphasized == false;
                p.strokeColor = dimmed
                    ? new Color(0.85f,0.85f,0.85f,0.18f)
                    : new Color(0.85f,0.85f,0.85f,emphasized ? 1f : 0.65f);
                p.lineWidth = emphasized ? 3.2f : 2.5f;
                var sourceCenter = sourceNode.worldBound.center;
                var portCenter = rec.outputPort.worldBound.center;
                var sourcePortOnLeft = portCenter.x < sourceCenter.x;
                var portEdgeWorld = ResolvePortAnchor(rec.outputPort,sourcePortOnLeft,portCenter.y);
                var isSelfLoop = sourceNode == rec.targetNode;
                Vector2 inWorld;
                bool toReceivesFromRight;
                var isBackEdge = false;
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
                    isBackEdge = IsBackEdgeBetween(sourceCenter,targetCenter);
                }
                if(isBackEdge) {
                    inWorld = new Vector2(inWorld.x,rec.targetNode.worldBound.yMax - BackEdgeCornerInset);
                } else if(isSelfLoop == false) {
                    inWorld = new Vector2(inWorld.x,rec.targetNode.worldBound.yMin + BackEdgeCornerInset);
                }
                var fromLocal = _edgesLayer.WorldToLocal(portEdgeWorld);
                var toLocal = _edgesLayer.WorldToLocal(inWorld);
                if(isSelfLoop) {
                    DrawBezierEdge(p,fromLocal,toLocal,sourcePortOnLeft,toReceivesFromRight);
                } else if(isBackEdge && backEdgeIndex.TryGetValue(ri,out var laneIdx)) {
                    var pathYMinWorld = Mathf.Min(portEdgeWorld.y,inWorld.y);
                    var pathYMaxWorld = Mathf.Max(portEdgeWorld.y,inWorld.y);
                    var corridor = ComputeMaxBottomBetween(sourceNode,rec.targetNode,pathYMinWorld,pathYMaxWorld);
                    if(corridor.hasObstacle) {
                        var laneY = corridor.laneLocalY + BackEdgeBaseDepth + laneIdx * BackEdgeStepDepth;
                        DrawBackEdgeL(p,fromLocal,toLocal,sourcePortOnLeft,toReceivesFromRight,laneY,GetBackEdgeStub(laneIdx));
                    } else {
                        DrawForwardEdgeStubCurve(p,fromLocal,toLocal,sourcePortOnLeft,toReceivesFromRight,BackEdgeStub);
                    }
                } else {
                    DrawForwardEdgeStubCurve(p,fromLocal,toLocal,sourcePortOnLeft,toReceivesFromRight,ForwardEdgeStub);
                }
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
                        toReceivesFromRightDrag = dragsLeft;
                    }
                    DrawBezierEdge(p,_edgesLayer.WorldToLocal(portEdgeWorld),toLocalDrag,dragsLeft,toReceivesFromRightDrag);
                }
            }
        }
        private void DrawEdgeStubsFront(MeshGenerationContext ctx) {
            if(_edgeLayoutPending) return;
            var p = ctx.painter2D;
            p.lineWidth = 2.5f;
            p.strokeColor = new Color(0.85f,0.85f,0.85f,0.65f);
            var backEdgeIndex = ComputeBackEdgeIndices();
            for(var ri = 0;ri < _edgeRecords.Count;ri++) {
                var rec = _edgeRecords[ri];
                if(rec.outputPort == null || rec.targetNode == null) continue;
                if(_isDraggingEdge && rec.outputPort == _draggingPort) continue;
                var sourceNode = rec.outputPort.GetFirstAncestorOfType<Node>();
                if(sourceNode == null || sourceNode == rec.targetNode) continue;
                var sourceID = sourceNode.userData is int ssid ? ssid : 0;
                var emphasized = _hoveredNodeID != 0 && sourceID == _hoveredNodeID;
                var dimmed = _hoveredNodeID != 0 && emphasized == false;
                p.strokeColor = dimmed
                    ? new Color(0.85f,0.85f,0.85f,0.18f)
                    : new Color(0.85f,0.85f,0.85f,emphasized ? 1f : 0.65f);
                p.lineWidth = emphasized ? 3.2f : 2.5f;
                var sourceCenter = sourceNode.worldBound.center;
                var portCenter = rec.outputPort.worldBound.center;
                var sourcePortOnLeft = portCenter.x < sourceCenter.x;
                var portEdgeWorld = ResolvePortAnchor(rec.outputPort,sourcePortOnLeft,portCenter.y);
                var targetCenter = rec.targetNode.worldBound.center;
                var toReceivesFromRight = targetCenter.x < sourceCenter.x;
                var inWorld = toReceivesFromRight
                    ? new Vector2(rec.targetNode.worldBound.xMax,rec.targetNode.worldBound.center.y)
                    : new Vector2(rec.targetNode.worldBound.x,rec.targetNode.worldBound.center.y);
                var isBackEdge = IsBackEdgeBetween(sourceCenter,targetCenter);
                if(isBackEdge) inWorld = new Vector2(inWorld.x,rec.targetNode.worldBound.yMax - BackEdgeCornerInset);
                else inWorld = new Vector2(inWorld.x,rec.targetNode.worldBound.yMin + BackEdgeCornerInset);
                var fromLocal = _edgesLayerFront.WorldToLocal(portEdgeWorld);
                var toLocal = _edgesLayerFront.WorldToLocal(inWorld);
                if(isBackEdge) {
                    var pathYMin = Mathf.Min(portEdgeWorld.y,inWorld.y);
                    var pathYMax = Mathf.Max(portEdgeWorld.y,inWorld.y);
                    var corridor = ComputeMaxBottomBetween(sourceNode,rec.targetNode,pathYMin,pathYMax);
                    var stub = (corridor.hasObstacle && backEdgeIndex.TryGetValue(ri,out var idx))
                        ? GetBackEdgeStub(idx)
                        : BackEdgeStub;
                    DrawBackEdgeStubsFront(p,fromLocal,toLocal,sourcePortOnLeft,toReceivesFromRight,stub);
                } else {
                    DrawForwardEdgeStubsFront(p,fromLocal,toLocal,sourcePortOnLeft,toReceivesFromRight,ForwardEdgeStub);
                }
            }
        }
        private (float laneLocalY,bool hasObstacle) ComputeMaxBottomBetween(Node sourceNode,Node targetNode,float pathYMinWorld,float pathYMaxWorld) {
            var minX = Mathf.Min(sourceNode.worldBound.xMax,targetNode.worldBound.xMax);
            var maxX = Mathf.Max(sourceNode.worldBound.xMax,targetNode.worldBound.xMax);
            var maxBottomWorld = float.NegativeInfinity;
            var hasObstacle = false;
            foreach(var pair in _nodeByID) {
                var n = pair.Value;
                if(n == sourceNode || n == targetNode) continue;
                var cx = n.worldBound.center.x;
                if(cx <= minX || cx >= maxX) continue;
                if(n.worldBound.yMax < pathYMinWorld || n.worldBound.yMin > pathYMaxWorld) continue;
                hasObstacle = true;
                if(n.worldBound.yMax > maxBottomWorld) maxBottomWorld = n.worldBound.yMax;
            }
            if(hasObstacle == false) maxBottomWorld = Mathf.Max(sourceNode.worldBound.yMax,targetNode.worldBound.yMax);
            var local = _edgesLayer.WorldToLocal(new Vector2(0,maxBottomWorld)).y;
            return (local,hasObstacle);
        }
        private static Vector2 ResolvePortAnchor(Port port,bool onLeftSide,float fallbackY) {
            var connector = port.Q("connector");
            if(connector != null) {
                var c = connector.worldBound.center;
                return new Vector2(c.x,c.y);
            }
            return onLeftSide
                ? new Vector2(port.worldBound.x,fallbackY)
                : new Vector2(port.worldBound.xMax,fallbackY);
        }
        private const float BackEdgeBaseDepth = 40f;
        private const float BackEdgeStepDepth = 28f;
        private const float BackEdgeDetectThreshold = 40f;
        private const float BackEdgeCornerInset = 12f;
        private const float BackEdgeStub = 32f;
        private const float BackEdgeStubStep = 12f;
        private const float ForwardEdgeStub = 16f;
        private static float GetBackEdgeStub(int laneIdx) => BackEdgeStub + laneIdx * BackEdgeStubStep;
        private static bool IsBackEdgeBetween(Vector2 sourceCenter,Vector2 targetCenter) {
            return targetCenter.x < sourceCenter.x - BackEdgeDetectThreshold;
        }
        private Dictionary<int,int> ComputeBackEdgeIndices() {
            var result = new Dictionary<int,int>();
            var lanes = new List<(int recIndex,float minX,float maxX,float corridorWidth)>();
            for(var ri = 0;ri < _edgeRecords.Count;ri++) {
                var rec = _edgeRecords[ri];
                if(rec.outputPort == null || rec.targetNode == null) continue;
                var sourceNode = rec.outputPort.GetFirstAncestorOfType<Node>();
                if(sourceNode == null || sourceNode == rec.targetNode) continue;
                if(IsBackEdgeBetween(sourceNode.worldBound.center,rec.targetNode.worldBound.center) == false) continue;
                var minX = Mathf.Min(sourceNode.worldBound.xMax,rec.targetNode.worldBound.xMax);
                var maxX = Mathf.Max(sourceNode.worldBound.xMax,rec.targetNode.worldBound.xMax);
                lanes.Add((ri,minX,maxX,maxX - minX));
            }
            lanes.Sort((a,b) => a.corridorWidth.CompareTo(b.corridorWidth));
            for(var i = 0;i < lanes.Count;i++) result[lanes[i].recIndex] = i;
            return result;
        }
        private void DrawBackEdgeL(UnityEngine.UIElements.Painter2D p,Vector2 fromLocal,Vector2 toLocal,bool fromOnLeft,bool toReceivesFromRight,float laneY,float stub) {
            var fromOutDirX = fromOnLeft ? -1f : 1f;
            var toInDirX = toReceivesFromRight ? 1f : -1f;
            var stub1 = new Vector2(fromLocal.x + fromOutDirX * stub,fromLocal.y);
            var corner1 = new Vector2(stub1.x,laneY);
            var stub2 = new Vector2(toLocal.x + toInDirX * stub,toLocal.y);
            var corner2 = new Vector2(stub2.x,laneY);
            p.BeginPath();
            p.MoveTo(stub1);
            p.LineTo(corner1);
            p.LineTo(corner2);
            p.LineTo(stub2);
            p.Stroke();
        }
        private void DrawBackEdgeStubsFront(UnityEngine.UIElements.Painter2D p,Vector2 fromLocal,Vector2 toLocal,bool fromOnLeft,bool toReceivesFromRight,float stub) {
            var fromOutDirX = fromOnLeft ? -1f : 1f;
            var toInDirX = toReceivesFromRight ? 1f : -1f;
            var stub1 = new Vector2(fromLocal.x + fromOutDirX * stub,fromLocal.y);
            var stub2 = new Vector2(toLocal.x + toInDirX * stub,toLocal.y);
            p.BeginPath();
            p.MoveTo(fromLocal);
            p.LineTo(stub1);
            p.MoveTo(stub2);
            p.LineTo(toLocal);
            p.Stroke();
            var arrowDir = (toLocal - stub2).normalized;
            if(arrowDir.sqrMagnitude < 0.001f) arrowDir = new Vector2(toReceivesFromRight ? -1 : 1,0);
            DrawArrowHead(p,toLocal,arrowDir);
        }
        private void DrawForwardEdgeStubCurve(UnityEngine.UIElements.Painter2D p,Vector2 fromLocal,Vector2 toLocal,bool fromOnLeft,bool toReceivesFromRight,float stub) {
            var fromOutDirX = fromOnLeft ? -1f : 1f;
            var toInDirX = toReceivesFromRight ? 1f : -1f;
            var stub1 = new Vector2(fromLocal.x + fromOutDirX * stub,fromLocal.y);
            var stub2 = new Vector2(toLocal.x + toInDirX * stub,toLocal.y);
            var dx = Mathf.Max(40f,Mathf.Abs(stub2.x - stub1.x) * 0.5f);
            var c1 = new Vector2(stub1.x + fromOutDirX * dx,stub1.y);
            var c2 = new Vector2(stub2.x + toInDirX * dx,stub2.y);
            p.BeginPath();
            p.MoveTo(stub1);
            p.BezierCurveTo(c1,c2,stub2);
            p.Stroke();
        }
        private void DrawForwardEdgeStubsFront(UnityEngine.UIElements.Painter2D p,Vector2 fromLocal,Vector2 toLocal,bool fromOnLeft,bool toReceivesFromRight,float stub) {
            var fromOutDirX = fromOnLeft ? -1f : 1f;
            var toInDirX = toReceivesFromRight ? 1f : -1f;
            var stub1 = new Vector2(fromLocal.x + fromOutDirX * stub,fromLocal.y);
            var stub2 = new Vector2(toLocal.x + toInDirX * stub,toLocal.y);
            p.BeginPath();
            p.MoveTo(fromLocal);
            p.LineTo(stub1);
            p.MoveTo(stub2);
            p.LineTo(toLocal);
            p.Stroke();
            var arrowDir = (toLocal - stub2).normalized;
            if(arrowDir.sqrMagnitude < 0.001f) arrowDir = new Vector2(toReceivesFromRight ? -1 : 1,0);
            DrawArrowHead(p,toLocal,arrowDir);
        }
        private void DrawBezierEdge(UnityEngine.UIElements.Painter2D p,Vector2 fromLocal,Vector2 toLocal,bool fromOnLeft,bool toReceivesFromRight) {
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
            DrawArrowHead(p,toLocal,arrowDir);
        }
        private void DrawArrowHead(UnityEngine.UIElements.Painter2D p,Vector2 tip,Vector2 arrowDir) {
            var perp = new Vector2(-arrowDir.y,arrowDir.x);
            var basePt = tip - arrowDir * 8f;
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
        private static Vector2 GetAbsolutePosition(Node node) {
            var p = node.GetPosition().position;
            var t = node.transform.position;
            return new Vector2(p.x + t.x,p.y + t.y);
        }
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
                var off = cur - a.end;
                node.transform.position = new Vector3(off.x,off.y,0);
                if(t >= 1f) finished.Add(node);
                else _animations[node] = a;
            }
            foreach(var n in finished) _animations.Remove(n);
            _edgesLayer?.MarkDirtyRepaint(); _edgesLayerFront?.MarkDirtyRepaint();
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
            UpdateRuntimeBadges();
            _edgesLayer?.MarkDirtyRepaint(); _edgesLayerFront?.MarkDirtyRepaint();
        }
        private readonly Dictionary<(int from,int to),Label> _edgeBadges = new();
        private Vector2 ComputeEdgeBadgeLocal(EdgeRecord rec,Node sourceNode,int recIndex,Dictionary<int,int> backIdx) {
            var sourceCenter = sourceNode.worldBound.center;
            var portCenter = rec.outputPort.worldBound.center;
            var sourcePortOnLeft = portCenter.x < sourceCenter.x;
            var portEdgeWorld = ResolvePortAnchor(rec.outputPort,sourcePortOnLeft,portCenter.y);
            var isSelfLoop = sourceNode == rec.targetNode;
            if(isSelfLoop) {
                var c = (sourceCenter + rec.targetNode.worldBound.center) * 0.5f;
                return _edgesLayerFront.WorldToLocal(c);
            }
            var targetCenter = rec.targetNode.worldBound.center;
            var toReceivesFromRight = targetCenter.x < sourceCenter.x;
            var inWorld = toReceivesFromRight
                ? new Vector2(rec.targetNode.worldBound.xMax,targetCenter.y)
                : new Vector2(rec.targetNode.worldBound.x,targetCenter.y);
            var isBackEdge = IsBackEdgeBetween(sourceCenter,targetCenter);
            if(isBackEdge) inWorld = new Vector2(inWorld.x,rec.targetNode.worldBound.yMax - BackEdgeCornerInset);
            else inWorld = new Vector2(inWorld.x,rec.targetNode.worldBound.yMin + BackEdgeCornerInset);
            var fromOutDirX = sourcePortOnLeft ? -1f : 1f;
            var toInDirX = toReceivesFromRight ? 1f : -1f;
            var stubLen = ForwardEdgeStub;
            var corridorObstacle = false;
            var laneY = 0f;
            if(isBackEdge && backIdx.TryGetValue(recIndex,out var laneIdxLocal)) {
                var pathYMin = Mathf.Min(portEdgeWorld.y,inWorld.y);
                var pathYMax = Mathf.Max(portEdgeWorld.y,inWorld.y);
                var corridor = ComputeMaxBottomBetween(sourceNode,rec.targetNode,pathYMin,pathYMax);
                corridorObstacle = corridor.hasObstacle;
                if(corridorObstacle) {
                    laneY = corridor.laneLocalY + BackEdgeBaseDepth + laneIdxLocal * BackEdgeStepDepth;
                    stubLen = GetBackEdgeStub(laneIdxLocal);
                } else {
                    stubLen = BackEdgeStub;
                }
            }
            var fromLocal = _edgesLayerFront.WorldToLocal(portEdgeWorld);
            var toLocal = _edgesLayerFront.WorldToLocal(inWorld);
            var stub1 = new Vector2(fromLocal.x + fromOutDirX * stubLen,fromLocal.y);
            var stub2 = new Vector2(toLocal.x + toInDirX * stubLen,toLocal.y);
            if(isBackEdge && corridorObstacle) {
                return new Vector2((stub1.x + stub2.x) * 0.5f,laneY);
            }
            return (stub1 + stub2) * 0.5f;
        }
        private void UpdateRuntimeBadges() {
            var play = Application.isPlaying && _target != null;
            foreach(var pair in _nodeByID) {
                var badge = pair.Value.Q<Label>("morn-state-badge");
                if(badge == null) continue;
                if(play && _target.StateEnterCounts.TryGetValue(pair.Key,out var count) && count > 0) {
                    badge.text = count.ToString();
                    badge.style.display = DisplayStyle.Flex;
                } else {
                    badge.style.display = DisplayStyle.None;
                }
            }
            if(play == false) {
                foreach(var b in _edgeBadges.Values) b.style.display = DisplayStyle.None;
                return;
            }
            var seen = new HashSet<(int,int)>();
            var backIdx = ComputeBackEdgeIndices();
            for(var ri = 0;ri < _edgeRecords.Count;ri++) {
                var rec = _edgeRecords[ri];
                if(rec.outputPort == null || rec.targetNode == null) continue;
                var sourceNode = rec.outputPort.GetFirstAncestorOfType<Node>();
                if(sourceNode == null || sourceNode.userData is not int fromID) continue;
                if(rec.targetNode.userData is not int toID) continue;
                var key = (fromID,toID);
                if(_target.TransitionCounts.TryGetValue(key,out var c) == false || c <= 0) continue;
                seen.Add(key);
                if(_edgeBadges.TryGetValue(key,out var label) == false) {
                    label = new Label { pickingMode = PickingMode.Ignore };
                    label.style.position = Position.Absolute;
                    label.style.minWidth = 24;
                    label.style.height = 18;
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                    label.style.color = Color.white;
                    label.style.backgroundColor = new Color(0.20f,0.55f,0.85f,0.95f);
                    label.style.borderTopLeftRadius = 9;
                    label.style.borderTopRightRadius = 9;
                    label.style.borderBottomLeftRadius = 9;
                    label.style.borderBottomRightRadius = 9;
                    label.style.paddingLeft = 6;
                    label.style.paddingRight = 6;
                    label.style.fontSize = 10;
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    _edgesLayerFront.Add(label);
                    _edgeBadges[key] = label;
                }
                label.text = c.ToString();
                label.style.display = DisplayStyle.Flex;
                var midLocal = ComputeEdgeBadgeLocal(rec,sourceNode,ri,backIdx);
                label.style.left = midLocal.x - 12;
                label.style.top = midLocal.y - 9;
            }
            var stale = new List<(int,int)>();
            foreach(var kv in _edgeBadges) if(seen.Contains(kv.Key) == false) stale.Add(kv.Key);
            foreach(var k in stale) {
                _edgeBadges[k].RemoveFromHierarchy();
                _edgeBadges.Remove(k);
            }
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
            _edgesLayer.MarkDirtyRepaint(); _edgesLayerFront?.MarkDirtyRepaint();
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
            var overlay = n.Q("morn-highlight-overlay");
            if(overlay == null) return;
            var width = color.a > 0f ? HighlightBorderWidth : 0;
            overlay.style.borderTopColor = color;
            overlay.style.borderBottomColor = color;
            overlay.style.borderLeftColor = color;
            overlay.style.borderRightColor = color;
            overlay.style.borderTopWidth = width;
            overlay.style.borderBottomWidth = width;
            overlay.style.borderLeftWidth = width;
            overlay.style.borderRightWidth = width;
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
        private const float NodeColumnSpacing = 400f;
        private Dictionary<int,Vector2> ComputeAutoLayout(MornStateMachine fsm) {
            var colWidth = NodeColumnSpacing;
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
            // orphan が non-orphan に向かって出る場合、矢印が左→右になるよう target の左に置く
            // 複数 pass で orphan 同士の連鎖も解決
            for(var pass = 0;pass < cap;pass++) {
                var changed = false;
                foreach(var id in orphans) {
                    var minTarget = int.MaxValue;
                    foreach(var to in adj[id]) {
                        if(to == id) continue;
                        if(depths.TryGetValue(to,out var td) && td < minTarget) minTarget = td;
                    }
                    if(minTarget != int.MaxValue) {
                        var desired = System.Math.Max(0,minTarget - 1);
                        if(depths[id] != desired) {
                            depths[id] = desired;
                            changed = true;
                        }
                    }
                }
                if(changed == false) break;
            }
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
            var capturedNodeID = meta.id;
            var capturedNode = node;
            var nodeMenuBtn = new Button() { text = "⋮" };
            nodeMenuBtn.style.width = 22;
            nodeMenuBtn.style.height = 18;
            nodeMenuBtn.style.minHeight = 18;
            nodeMenuBtn.style.maxHeight = 18;
            nodeMenuBtn.style.paddingLeft = 0;
            nodeMenuBtn.style.paddingRight = 0;
            nodeMenuBtn.style.paddingTop = 0;
            nodeMenuBtn.style.paddingBottom = 0;
            nodeMenuBtn.style.marginLeft = 4;
            nodeMenuBtn.style.marginRight = 4;
            nodeMenuBtn.style.marginTop = 0;
            nodeMenuBtn.style.marginBottom = 0;
            nodeMenuBtn.style.fontSize = 14;
            nodeMenuBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            nodeMenuBtn.style.backgroundColor = new Color(0,0,0,0);
            nodeMenuBtn.style.borderTopWidth = 0;
            nodeMenuBtn.style.borderBottomWidth = 0;
            nodeMenuBtn.style.borderLeftWidth = 0;
            nodeMenuBtn.style.borderRightWidth = 0;
            nodeMenuBtn.clicked += () => ShowNodeContextMenuGeneric(capturedNodeID,capturedNode);
            node.titleContainer.Insert(1,nodeMenuBtn);
            node.SetPosition(new Rect(pos.x,pos.y,0,0));
            node.style.width = 260;
            node.style.minWidth = 260;
            node.style.maxWidth = 260;
            node.style.backgroundColor = new Color(0.20f,0.20f,0.20f,0.85f);
            const float cornerRadius = 6f;
            node.style.borderTopLeftRadius = cornerRadius;
            node.style.borderTopRightRadius = cornerRadius;
            node.style.borderBottomLeftRadius = cornerRadius;
            node.style.borderBottomRightRadius = cornerRadius;
            node.mainContainer.style.backgroundColor = new Color(0,0,0,0);
            node.extensionContainer.style.backgroundColor = new Color(0,0,0,0);
            var topDivider = node.Q("divider");
            if(topDivider != null) topDivider.style.backgroundColor = new Color(0,0,0,0);
            var nodeBorder = node.Q("node-border");
            if(nodeBorder != null) {
                nodeBorder.style.borderTopColor = nodeBorder.style.borderBottomColor = nodeBorder.style.borderLeftColor = nodeBorder.style.borderRightColor = new Color(0,0,0,0);
                nodeBorder.style.borderTopWidth = nodeBorder.style.borderBottomWidth = nodeBorder.style.borderLeftWidth = nodeBorder.style.borderRightWidth = 0;
                nodeBorder.style.borderTopLeftRadius = cornerRadius;
                nodeBorder.style.borderTopRightRadius = cornerRadius;
                nodeBorder.style.borderBottomLeftRadius = cornerRadius;
                nodeBorder.style.borderBottomRightRadius = cornerRadius;
            }
            var selBorder = node.Q("selection-border");
            if(selBorder != null) {
                selBorder.style.borderTopLeftRadius = cornerRadius;
                selBorder.style.borderTopRightRadius = cornerRadius;
                selBorder.style.borderBottomLeftRadius = cornerRadius;
                selBorder.style.borderBottomRightRadius = cornerRadius;
            }
            var highlightOverlay = new VisualElement { name = "morn-highlight-overlay" };
            highlightOverlay.pickingMode = PickingMode.Ignore;
            highlightOverlay.style.position = Position.Absolute;
            highlightOverlay.style.left = 0;
            highlightOverlay.style.right = 0;
            highlightOverlay.style.top = 0;
            highlightOverlay.style.bottom = 0;
            highlightOverlay.style.borderTopLeftRadius = cornerRadius;
            highlightOverlay.style.borderTopRightRadius = cornerRadius;
            highlightOverlay.style.borderBottomLeftRadius = cornerRadius;
            highlightOverlay.style.borderBottomRightRadius = cornerRadius;
            node.Add(highlightOverlay);
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
            inspector.name = "morn-inspector";
            _sectionsByNode[node] = new List<VisualElement>();
            for(var bi = 0;bi < meta.behaviours.Count;bi++) {
                var b = meta.behaviours[bi];
                if(b == null) continue;
                var section = AddBehaviourSection(inspector,node,fsm,meta.id,bi,b,so);
                _sectionsByNode[node].Add(section);
            }
            var addBtn = new Button(() => OpenAddBehaviourSearch(meta.id)) { text = "+ Add Behaviour", name = "morn-add-btn" };
            addBtn.style.marginTop = 4;
            inspector.Add(addBtn);
            node.extensionContainer.Add(inspector);
            node.RefreshExpandedState();
            node.RefreshPorts();
            var capturedID = meta.id;
            node.RegisterCallback<MouseEnterEvent>(_ => { if(_isDraggingEdge == false) SetHoveredNode(capturedID); });
            node.RegisterCallback<MouseLeaveEvent>(_ => { if(_isDraggingEdge == false) SetHoveredNode(0); });
            var stateBadge = new Label("0") { name = "morn-state-badge" };
            stateBadge.pickingMode = PickingMode.Ignore;
            stateBadge.style.position = Position.Absolute;
            stateBadge.style.top = -8;
            stateBadge.style.right = -8;
            stateBadge.style.minWidth = 22;
            stateBadge.style.height = 18;
            stateBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
            stateBadge.style.color = Color.white;
            stateBadge.style.backgroundColor = new Color(0.85f,0.45f,0.15f,0.95f);
            stateBadge.style.borderTopLeftRadius = 9;
            stateBadge.style.borderTopRightRadius = 9;
            stateBadge.style.borderBottomLeftRadius = 9;
            stateBadge.style.borderBottomRightRadius = 9;
            stateBadge.style.paddingLeft = 6;
            stateBadge.style.paddingRight = 6;
            stateBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            stateBadge.style.fontSize = 10;
            stateBadge.style.display = DisplayStyle.None;
            node.Add(stateBadge);
            return node;
        }
        private void SetHoveredNode(int id) {
            if(_hoveredNodeID == id) return;
            _hoveredNodeID = id;
            _hoveredRelatedIDs.Clear();
            if(id != 0 && _target != null) {
                _hoveredRelatedIDs.Add(id);
                var meta = _target.FindNode(id);
                if(meta != null) {
                    foreach(var b in meta.behaviours) {
                        if(b == null) continue;
                        foreach(var (_,link) in EnumerateConnectionFields(b)) {
                            if(link != null && link.stateID != 0) _hoveredRelatedIDs.Add(link.stateID);
                        }
                    }
                }
            }
            ApplyHoverEffects();
            _edgesLayer?.MarkDirtyRepaint();
            _edgesLayerFront?.MarkDirtyRepaint();
        }
        private void ApplyHoverEffects() {
            foreach(var pair in _nodeByID) {
                var n = pair.Value;
                var related = _hoveredNodeID == 0 || _hoveredRelatedIDs.Contains(pair.Key);
                n.style.opacity = related ? 1f : 0.35f;
            }
        }
        private void ShowNodeContextMenuGeneric(int stateID,Node node) {
            if(_target == null) return;
            var menu = new GenericMenu();
            void Item(string label,bool enabled,bool on,System.Action act) {
                if(enabled) menu.AddItem(new GUIContent(label),on,() => act());
                else menu.AddDisabledItem(new GUIContent(label));
            }
            void Sep() => menu.AddSeparator("");
            BuildNodeMenuCommon(stateID,node,Item,Sep);
            menu.ShowAsContext();
        }
        private void AppendNodeMenuItems(DropdownMenu menu,int stateID,Node node) {
            void Item(string label,bool enabled,bool on,System.Action act) {
                menu.AppendAction(label,_ => act(),_ => enabled ? (on ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal) : DropdownMenuAction.Status.Disabled);
            }
            void Sep() => menu.AppendSeparator();
            BuildNodeMenuCommon(stateID,node,Item,Sep);
        }
        private void BuildNodeMenuCommon(int stateID,Node node,System.Action<string,bool,bool,System.Action> Item,System.Action Sep) {
            Item("Set as Start State",true,_target.startStateID == stateID,() => SetAsStartState(stateID));
            Sep();
            Item("Delete State",true,false,() => DeleteStateAndCleanup(stateID));
            Item("Copy State",true,false,() => CopyNodeToClipboard(node));
            Item("Duplicate State",true,false,() => DuplicateNode(node));
            Sep();
            Item("Paste Behaviour As New",TryGetClipboardBehaviourType() != null,false,() => PasteBehaviourAsNew(stateID));
        }
        private void ShowBehaviourContextMenu(int stateID,int behaviourIndex,MornStateBehaviour state) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            var totalCount = meta != null ? meta.behaviours.Count : 0;
            var menu = new GenericMenu();
            void Item(string label,bool enabled,System.Action act) {
                if(enabled) menu.AddItem(new GUIContent(label),false,() => act());
                else menu.AddDisabledItem(new GUIContent(label));
            }
            void Sep() => menu.AddSeparator("");
            BuildBehaviourMenuCommon(stateID,behaviourIndex,state,totalCount,Item,Sep);
            menu.ShowAsContext();
        }
        private void AppendBehaviourMenuItems(DropdownMenu menu,int stateID,int behaviourIndex,MornStateBehaviour state,int totalCount) {
            void Item(string label,bool enabled,System.Action act) {
                menu.AppendAction(label,_ => act(),_ => enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
            void Sep() => menu.AppendSeparator();
            BuildBehaviourMenuCommon(stateID,behaviourIndex,state,totalCount,Item,Sep);
        }
        private void BuildBehaviourMenuCommon(int stateID,int behaviourIndex,MornStateBehaviour state,int totalCount,System.Action<string,bool,System.Action> Item,System.Action Sep) {
            Item("Reset",true,() => ResetBehaviour(stateID,behaviourIndex));
            Sep();
            Item("Remove Behaviour",true,() => RemoveBehaviour(stateID,state));
            Item("Move Up",behaviourIndex > 0,() => MoveBehaviour(stateID,behaviourIndex,-1));
            Item("Move Down",behaviourIndex < totalCount - 1,() => MoveBehaviour(stateID,behaviourIndex,1));
            Sep();
            Item("Copy Behaviour",true,() => CopyBehaviourToClipboard(state));
            var clipType = TryGetClipboardBehaviourType();
            Item("Paste Behaviour Values",clipType != null && clipType == state.GetType(),() => PasteBehaviourValues(stateID,behaviourIndex));
            Item("Paste Behaviour As New",clipType != null,() => PasteBehaviourAsNew(stateID));
            Sep();
            var script = FindScriptAsset(state.GetType());
            if(script != null) {
                Item("Edit Script",true,() => AssetDatabase.OpenAsset(script));
                Item("Find Script in Project",true,() => EditorGUIUtility.PingObject(script));
            }
        }
        [System.Serializable]
        private class BehaviourClipboard { public string typeName; public string json; }
        private const string BehaviourClipboardPrefix = "MORNSTATE_BEHAVIOUR:";
        private static void CopyBehaviourToClipboard(MornStateBehaviour state) {
            var c = new BehaviourClipboard {
                typeName = state.GetType().AssemblyQualifiedName,
                json = JsonUtility.ToJson(state),
            };
            EditorGUIUtility.systemCopyBuffer = BehaviourClipboardPrefix + JsonUtility.ToJson(c);
        }
        private static BehaviourClipboard TryReadBehaviourClipboard() {
            var raw = EditorGUIUtility.systemCopyBuffer;
            if(string.IsNullOrEmpty(raw) || raw.StartsWith(BehaviourClipboardPrefix) == false) return null;
            try { return JsonUtility.FromJson<BehaviourClipboard>(raw.Substring(BehaviourClipboardPrefix.Length)); }
            catch { return null; }
        }
        private static System.Type TryGetClipboardBehaviourType() {
            var c = TryReadBehaviourClipboard();
            if(c == null) return null;
            return System.Type.GetType(c.typeName);
        }
        private void PasteBehaviourValues(int stateID,int behaviourIndex) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            if(meta == null || behaviourIndex < 0 || behaviourIndex >= meta.behaviours.Count) return;
            var c = TryReadBehaviourClipboard();
            if(c == null) return;
            var type = System.Type.GetType(c.typeName);
            if(type == null || meta.behaviours[behaviourIndex] == null || meta.behaviours[behaviourIndex].GetType() != type) return;
            Undo.RegisterCompleteObjectUndo(_target,"Paste Behaviour Values");
            JsonUtility.FromJsonOverwrite(c.json,meta.behaviours[behaviourIndex]);
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        private void PasteBehaviourAsNew(int stateID) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            if(meta == null) return;
            var c = TryReadBehaviourClipboard();
            if(c == null) return;
            var type = System.Type.GetType(c.typeName);
            if(type == null) return;
            Undo.RegisterCompleteObjectUndo(_target,"Paste Behaviour As New");
            var instance = (MornStateBehaviour)System.Activator.CreateInstance(type);
            JsonUtility.FromJsonOverwrite(c.json,instance);
            instance.StateID = stateID;
            meta.behaviours.Add(instance);
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        private void ResetBehaviour(int stateID,int behaviourIndex) {
            if(_target == null) return;
            var meta = _target.FindNode(stateID);
            if(meta == null || behaviourIndex < 0 || behaviourIndex >= meta.behaviours.Count) return;
            var old = meta.behaviours[behaviourIndex];
            if(old == null) return;
            Undo.RegisterCompleteObjectUndo(_target,"Reset Behaviour");
            meta.behaviours[behaviourIndex] = (MornStateBehaviour)System.Activator.CreateInstance(old.GetType());
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        private void DeleteStateAndCleanup(int stateID) {
            if(_target == null) return;
            Undo.RegisterCompleteObjectUndo(_target,"Delete State");
            _target.UnregisterNode(stateID);
            foreach(var n in _target.NodesMutable) {
                if(n.behaviours == null) continue;
                foreach(var b in n.behaviours) {
                    if(b == null) continue;
                    foreach(var f in b.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                        if(f.GetValue(b) is Connection c && c.stateID == stateID) c.stateID = 0;
                    }
                }
            }
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        private VisualElement AddBehaviourSection(VisualElement parent,Node node,MornStateMachine fsm,int stateID,int behaviourIndex,MornStateBehaviour state,SerializedObject so) {
            var section = new VisualElement();
            section.userData = state;
            section.style.marginBottom = 4;
            section.style.borderTopWidth = 1;
            section.style.borderTopColor = new Color(1f,1f,1f,0.1f);
            section.style.paddingTop = 2;
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 2;
            var titleLabel = new Label(state.GetType().Name);
            titleLabel.style.flexGrow = 1;
            titleLabel.style.height = 18;
            titleLabel.style.minHeight = 18;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            titleLabel.style.paddingLeft = 4;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.backgroundColor = new Color(0.25f,0.25f,0.25f,0.6f);
            titleLabel.style.borderTopLeftRadius = 2;
            titleLabel.style.borderTopRightRadius = 2;
            titleLabel.style.borderBottomLeftRadius = 2;
            titleLabel.style.borderBottomRightRadius = 2;
            header.Add(titleLabel);
            var menuBtn = new Button() { text = "⋮" };
            menuBtn.style.position = Position.Absolute;
            menuBtn.style.right = 0;
            menuBtn.style.top = 0;
            menuBtn.style.width = 22;
            menuBtn.style.height = 18;
            menuBtn.style.minHeight = 18;
            menuBtn.style.maxHeight = 18;
            menuBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            menuBtn.style.fontSize = 14;
            menuBtn.style.paddingTop = 0;
            menuBtn.style.paddingBottom = 0;
            menuBtn.style.paddingLeft = 0;
            menuBtn.style.paddingRight = 0;
            menuBtn.style.marginTop = 0;
            menuBtn.style.marginBottom = 0;
            menuBtn.style.backgroundColor = new Color(0,0,0,0);
            menuBtn.style.borderTopWidth = 0;
            menuBtn.style.borderBottomWidth = 0;
            menuBtn.style.borderLeftWidth = 0;
            menuBtn.style.borderRightWidth = 0;
            menuBtn.clicked += () => ShowBehaviourContextMenu(stateID,behaviourIndex,state);
            header.Add(menuBtn);
            titleLabel.RegisterCallback<MouseDownEvent>(e => {
                if(e.button != 0) return;
                StartBehaviourDrag(stateID,behaviourIndex,section);
                UpdateBehaviourDragHover(e.mousePosition);
                e.StopPropagation();
            });
            section.RegisterCallback<MouseDownEvent>(e => {
                if(e.button != 1) return;
                ShowBehaviourContextMenu(stateID,behaviourIndex,state);
                e.StopPropagation();
                e.PreventDefault();
            },TrickleDown.TrickleDown);
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
            if(parent != null) parent.Add(section);
            return section;
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
            port.RegisterCallback<MouseDownEvent>(e => { if(e.button == 0) { _isDraggingEdge = true; _draggingPort = port; SetHoveredNode(0); } },TrickleDown.TrickleDown);
            port.RegisterCallback<PointerDownEvent>(e => { if(e.button == 0) { _isDraggingEdge = true; _draggingPort = port; SetHoveredNode(0); } },TrickleDown.TrickleDown);
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
            port.style.paddingLeft = 0;
            port.style.paddingRight = 0;
            port.style.marginLeft = placeOnLeft ? -6 : 0;
            port.style.marginRight = placeOnLeft ? 0 : -6;
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
            _edgesLayer?.MarkDirtyRepaint(); _edgesLayerFront?.MarkDirtyRepaint();
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
        public void ShowDropOutsideMenu(Port outputPort,Vector2 worldPos) {
            if(outputPort == null || _target == null) return;
            var graphPos = contentViewContainer.WorldToLocal(worldPos);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Create State"),false,() => CreateNodeAndConnect(outputPort,graphPos));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Disconnect"),false,() => DisconnectOutput(outputPort));
            menu.ShowAsContext();
        }
        private void CreateNodeAndConnect(Port outputPort,Vector2 graphPos) {
            if(outputPort == null || _target == null) return;
            if(outputPort.userData is not System.ValueTuple<MornStateBehaviour,Connection> tup) return;
            var newID = AllocateUniqueStateID();
            Undo.RegisterCompleteObjectUndo(_target,"Create State and Connect");
            _target.RegisterNode(new MornStateMachine.StateNode { id = newID,name = "New State" });
            if(_target.startStateID == 0) _target.startStateID = newID;
            tup.Item2.stateID = newID;
            _layoutPositions[newID] = graphPos;
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
                    _view.ShowDropOutsideMenu(edge.output,position);
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
        private bool _isDraggingBehaviour;
        private int _behaviourDragFromStateID;
        private int _behaviourDragFromIndex;
        private VisualElement _behaviourDragSection;
        private Node _behaviourDragHoverNode;
        private VisualElement _behaviourDragGhost;
        private VisualElement _behaviourDropIndicator;
        private int _behaviourDropInsertIndex = -1;
        private Vector2 _behaviourDragGhostSize;
        private readonly Dictionary<Node,List<VisualElement>> _sectionsByNode = new();
        private void StartBehaviourDrag(int stateID,int behaviourIndex,VisualElement section) {
            _isDraggingBehaviour = true;
            _behaviourDragFromStateID = stateID;
            _behaviourDragFromIndex = behaviourIndex;
            _behaviourDragSection = section;
            section.style.opacity = 0.3f;
            section.style.borderLeftWidth = 3;
            section.style.borderLeftColor = new Color(0.3f,0.7f,1.0f);
            var meta = _target?.FindNode(stateID);
            var b = meta != null && behaviourIndex < meta.behaviours.Count ? meta.behaviours[behaviourIndex] : null;
            var typeName = b?.GetType().Name ?? "?";
            var ghostLabel = new Label($"⇆ {typeName}");
            ghostLabel.style.position = Position.Absolute;
            ghostLabel.style.backgroundColor = new Color(0.15f,0.15f,0.15f,0.95f);
            ghostLabel.style.color = new Color(1f,1f,1f);
            ghostLabel.style.borderTopWidth = 2;
            ghostLabel.style.borderBottomWidth = 2;
            ghostLabel.style.borderLeftWidth = 2;
            ghostLabel.style.borderRightWidth = 2;
            ghostLabel.style.borderTopColor = new Color(0.3f,0.7f,1.0f);
            ghostLabel.style.borderBottomColor = new Color(0.3f,0.7f,1.0f);
            ghostLabel.style.borderLeftColor = new Color(0.3f,0.7f,1.0f);
            ghostLabel.style.borderRightColor = new Color(0.3f,0.7f,1.0f);
            ghostLabel.style.borderTopLeftRadius = 4;
            ghostLabel.style.borderTopRightRadius = 4;
            ghostLabel.style.borderBottomLeftRadius = 4;
            ghostLabel.style.borderBottomRightRadius = 4;
            ghostLabel.style.paddingLeft = 8;
            ghostLabel.style.paddingRight = 8;
            ghostLabel.style.paddingTop = 4;
            ghostLabel.style.paddingBottom = 4;
            ghostLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            ghostLabel.pickingMode = PickingMode.Ignore;
            _behaviourDragGhost = ghostLabel;
            Add(_behaviourDragGhost);
            _behaviourDragGhost.BringToFront();
            _behaviourDropIndicator = new VisualElement();
            _behaviourDropIndicator.style.position = Position.Absolute;
            _behaviourDropIndicator.style.height = 3;
            _behaviourDropIndicator.style.backgroundColor = new Color(0.3f,0.7f,1.0f);
            _behaviourDropIndicator.style.borderTopLeftRadius = 2;
            _behaviourDropIndicator.style.borderTopRightRadius = 2;
            _behaviourDropIndicator.style.borderBottomLeftRadius = 2;
            _behaviourDropIndicator.style.borderBottomRightRadius = 2;
            _behaviourDropIndicator.pickingMode = PickingMode.Ignore;
            _behaviourDropIndicator.style.display = DisplayStyle.None;
            Add(_behaviourDropIndicator);
            _behaviourDropIndicator.BringToFront();
        }
        private void UpdateBehaviourDragHover(Vector2 worldPos) {
            if(_isDraggingBehaviour == false) return;
            if(_behaviourDragGhost != null) {
                var local = this.WorldToLocal(worldPos);
                _behaviourDragGhost.style.left = local.x + 4;
                _behaviourDragGhost.style.top = local.y + 4;
            }
            Node target = null;
            foreach(var pair in _nodeByID) {
                if(pair.Value.worldBound.Contains(worldPos)) { target = pair.Value; break; }
            }
            if(target != _behaviourDragHoverNode) {
                if(_behaviourDragHoverNode != null) ApplyBorder(_behaviourDragHoverNode,new Color(0,0,0,0));
                _behaviourDragHoverNode = target;
                if(_behaviourDragHoverNode != null) ApplyBorder(_behaviourDragHoverNode,new Color(0.3f,0.7f,1.0f));
            }
            UpdateDropIndicator(worldPos);
        }
        private void UpdateDropIndicator(Vector2 worldPos) {
            if(_behaviourDropIndicator == null) return;
            if(_behaviourDragHoverNode == null || _sectionsByNode.TryGetValue(_behaviourDragHoverNode,out var sections) == false) {
                _behaviourDropIndicator.style.display = DisplayStyle.None;
                _behaviourDropInsertIndex = -1;
                return;
            }
            var insertIndex = sections.Count;
            float yWorld;
            if(sections.Count == 0) {
                var nb = _behaviourDragHoverNode.worldBound;
                yWorld = nb.yMax - 30;
            } else {
                insertIndex = sections.Count;
                for(var i = 0;i < sections.Count;i++) {
                    var sb = sections[i].worldBound;
                    if(worldPos.y < sb.center.y) { insertIndex = i; break; }
                }
                if(insertIndex == 0) yWorld = sections[0].worldBound.yMin;
                else if(insertIndex >= sections.Count) yWorld = sections[sections.Count - 1].worldBound.yMax;
                else yWorld = (sections[insertIndex - 1].worldBound.yMax + sections[insertIndex].worldBound.yMin) * 0.5f;
            }
            var leftWorld = _behaviourDragHoverNode.worldBound.xMin + 8;
            var widthWorld = _behaviourDragHoverNode.worldBound.width - 16;
            var localTopLeft = this.WorldToLocal(new Vector2(leftWorld,yWorld));
            _behaviourDropIndicator.style.display = DisplayStyle.Flex;
            _behaviourDropIndicator.style.left = localTopLeft.x;
            _behaviourDropIndicator.style.top = localTopLeft.y - 1.5f;
            _behaviourDropIndicator.style.width = widthWorld;
            _behaviourDropInsertIndex = insertIndex;
        }
        private void EndBehaviourDrag(Vector2 worldPos) {
            if(_isDraggingBehaviour == false) return;
            _isDraggingBehaviour = false;
            if(_behaviourDragSection != null) {
                _behaviourDragSection.style.opacity = 1f;
                _behaviourDragSection.style.borderLeftWidth = 0;
            }
            _behaviourDragSection = null;
            if(_behaviourDragGhost != null) { Remove(_behaviourDragGhost); _behaviourDragGhost = null; }
            if(_behaviourDropIndicator != null) { Remove(_behaviourDropIndicator); _behaviourDropIndicator = null; }
            var insertIndex = _behaviourDropInsertIndex;
            _behaviourDropInsertIndex = -1;
            UpdateHighlights();
            Node target = null;
            foreach(var pair in _nodeByID) {
                if(pair.Value.worldBound.Contains(worldPos)) { target = pair.Value; break; }
            }
            if(target == null || target.userData is not int targetID) return;
            MoveBehaviourToState(_behaviourDragFromStateID,_behaviourDragFromIndex,targetID,insertIndex);
        }
        private void MoveBehaviourToState(int fromStateID,int fromIndex,int toStateID,int toIndex) {
            if(_target == null) return;
            var fromMeta = _target.FindNode(fromStateID);
            var toMeta = _target.FindNode(toStateID);
            if(fromMeta == null || toMeta == null) return;
            if(fromIndex < 0 || fromIndex >= fromMeta.behaviours.Count) return;
            Undo.RegisterCompleteObjectUndo(_target,"Move Behaviour");
            var b = fromMeta.behaviours[fromIndex];
            fromMeta.behaviours.RemoveAt(fromIndex);
            // 同一 state 内移動の場合 toIndex を補正 (削除後ずれる)
            if(fromStateID == toStateID && toIndex > fromIndex) toIndex--;
            if(toIndex < 0 || toIndex > toMeta.behaviours.Count) toIndex = toMeta.behaviours.Count;
            toMeta.behaviours.Insert(toIndex,b);
            EditorUtility.SetDirty(_target);
            EditorApplication.delayCall += () => LoadStateMachine(_target);
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
            if(_target == null) return;
            Node hitNode = null;
            MornStateBehaviour hitBehaviour = null;
            if(evt.target is VisualElement ve) {
                for(VisualElement cur = ve;cur != null;cur = cur.parent) {
                    if(hitBehaviour == null && cur.userData is MornStateBehaviour bh) hitBehaviour = bh;
                    if(cur is Node n) { hitNode = n; break; }
                }
            }
            if(hitBehaviour != null && hitNode != null && hitNode.userData is int bsid) {
                var meta = _target.FindNode(bsid);
                if(meta != null) {
                    var idx = meta.behaviours.IndexOf(hitBehaviour);
                    if(idx >= 0) {
                        AppendBehaviourMenuItems(evt.menu,bsid,idx,hitBehaviour,meta.behaviours.Count);
                        return;
                    }
                }
            }
            var selectedNodes = GetSelectedStateNodes();
            var multi = selectedNodes.Count >= 2;
            if(multi && hitNode != null && selectedNodes.Contains(hitNode) == false) multi = false;
            if(multi) {
                var captured = new List<Node>(selectedNodes);
                evt.menu.AppendAction($"Copy {captured.Count} States",_ => CopyNodesToClipboard(captured));
                evt.menu.AppendAction($"Duplicate {captured.Count} States",_ => DuplicateNodes(captured));
                evt.menu.AppendAction($"Delete {captured.Count} States",_ => DeleteStatesAndCleanup(captured));
                return;
            }
            if(hitNode != null && hitNode.userData is int sid) {
                AppendNodeMenuItems(evt.menu,sid,hitNode);
                return;
            }
            var screenMousePos = evt.mousePosition;
            var graphPos = contentViewContainer.WorldToLocal(screenMousePos);
            evt.menu.AppendAction("Create State",_ => CreateEmptyStateAt(graphPos));
            var clipboard = EditorGUIUtility.systemCopyBuffer;
            var canPaste = string.IsNullOrEmpty(clipboard) == false && clipboard.StartsWith("{");
            evt.menu.AppendAction("Paste State",_ => PasteAtGraphPos(clipboard,graphPos),
                _ => canPaste ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }
        private List<Node> GetSelectedStateNodes() {
            var result = new List<Node>();
            foreach(var s in selection) {
                if(s is Node n && n.userData is int) result.Add(n);
            }
            return result;
        }
        private void CopyNodesToClipboard(List<Node> nodes) {
            var data = SerializeForClipboard(nodes.ConvertAll(n => (GraphElement)n));
            EditorGUIUtility.systemCopyBuffer = data;
        }
        private void DuplicateNodes(List<Node> nodes) {
            var data = SerializeForClipboard(nodes.ConvertAll(n => (GraphElement)n));
            UnserializeAndPaste("Duplicate",data);
        }
        private void DeleteStatesAndCleanup(List<Node> nodes) {
            if(_target == null || nodes.Count == 0) return;
            var ids = new HashSet<int>();
            foreach(var n in nodes) {
                if(n.userData is int id) ids.Add(id);
            }
            if(ids.Count == 0) return;
            Undo.RegisterCompleteObjectUndo(_target,"Delete States");
            foreach(var id in ids) _target.UnregisterNode(id);
            foreach(var n in _target.NodesMutable) {
                if(n.behaviours == null) continue;
                foreach(var b in n.behaviours) {
                    if(b == null) continue;
                    foreach(var f in b.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                        if(f.GetValue(b) is Connection c && ids.Contains(c.stateID)) c.stateID = 0;
                    }
                }
            }
            EditorUtility.SetDirty(_target);
            LoadStateMachine(_target);
        }
        public override EventPropagation DeleteSelection() {
            var nodes = GetSelectedStateNodes();
            if(nodes.Count == 0) return EventPropagation.Continue;
            DeleteStatesAndCleanup(nodes);
            return EventPropagation.Stop;
        }
        private void CopyNodeToClipboard(Node node) {
            var data = SerializeForClipboard(new[] { (GraphElement)node });
            EditorGUIUtility.systemCopyBuffer = data;
        }
        private void DuplicateNode(Node node) {
            var data = SerializeForClipboard(new[] { (GraphElement)node });
            UnserializeAndPaste("Duplicate",data);
        }
        private void PasteAtGraphPos(string serializedData,Vector2 graphPos) {
            UnserializeAndPaste("Paste",serializedData);
        }
        public void OpenAddBehaviourSearch(int stateID) {
            if(_target == null) return;
            var provider = ScriptableObject.CreateInstance<MornStateSearchProvider>();
            provider.SetupAddBehaviour(this,stateID);
            var screenPos = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);
            SearchWindow.Open(new SearchWindowContext(screenPos),provider);
            MornStateSearchProvider.ApplyFilterToOpenWindow(provider.SavedFilter);
        }
        public void CreateEmptyStateAt(Vector2 graphPos) {
            if(_target == null) return;
            var newID = AllocateUniqueStateID();
            Undo.RegisterCompleteObjectUndo(_target,"Create State");
            _target.RegisterNode(new MornStateMachine.StateNode {
                id = newID,
                name = "New State",
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
                name = "New State",
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
            if(_nodeByID.TryGetValue(stateID,out var node) && IncrementalAppendBehaviour(node,meta,instance)) {
                _nodeSig[stateID] = ComputeNodeSig(meta);
                RebuildEdgeRecords(_target);
                UpdateHighlights();
                return;
            }
            LoadStateMachine(_target);
        }
        private bool IncrementalAppendBehaviour(Node node,MornStateMachine.StateNode meta,MornStateBehaviour newBehaviour) {
            var inspector = node.Q("morn-inspector");
            var addBtn = node.Q("morn-add-btn");
            if(inspector == null || addBtn == null) return false;
            var bi = meta.behaviours.IndexOf(newBehaviour);
            if(bi < 0) return false;
            var so = new SerializedObject(_target);
            var section = AddBehaviourSection(null,node,_target,meta.id,bi,newBehaviour,so);
            inspector.Insert(inspector.IndexOf(addBtn),section);
            if(_sectionsByNode.TryGetValue(node,out var list) == false) {
                list = new List<VisualElement>();
                _sectionsByNode[node] = list;
            }
            list.Add(section);
            return true;
        }
        public void SetAsStartState(int stateID) {
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
