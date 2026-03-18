// RemoteClient/RemoteClientV3.cs
// RemoteClientV2 の UIToolkit (UXML/USS) 化
// ビューポート部分は IMGUIContainer でラップ、ロジックは V2 から変更なし

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using PolyLingRemoteClient;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.Remote;
using Poly_Ling.MeshListV2;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Remote
{
    public class RemoteClientV3 : EditorWindow
    {
        // ================================================================
        // 接続設定
        // ================================================================

        private string _host = "localhost";
        private int _port = 8765;

        // ================================================================
        // WebSocket
        // ================================================================

        private RemoteClientWs _ws;
        private CancellationTokenSource _cts;
        private bool _isConnected;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private int _requestId;
        private readonly Dictionary<string, Action<string>> _textCallbacks = new Dictionary<string, Action<string>>();
        private readonly Dictionary<string, Action<string, byte[]>> _binaryCallbacks = new Dictionary<string, Action<string, byte[]>>();
        private string _lastTextResponseId;
        private string _lastTextResponseJson;

        // ================================================================
        // 受信データ
        // ================================================================

        private ProjectContext _project;
        private string _projectStatus = "未受信";

        // ================================================================
        // ビューポート
        // ================================================================

        private ViewportCore _viewport;

        // ================================================================
        // ツール
        // ================================================================

        private ToolContext _toolContext;
        private IEditTool _currentTool;
        private SelectTool _selectTool = new SelectTool();
        private MoveTool _moveTool = new MoveTool();
        private ToolInputHandler _inputHandler;

        // ================================================================
        // 表示設定
        // ================================================================

        private bool _showMesh = true;
        private bool _showWireframe = true;
        private bool _showVertices = false;
        private bool _backfaceCulling = true;
        private bool _showUnselectedWire = true;
        private bool _showBones = true;

        // ================================================================
        // Undo
        // ================================================================

        private MeshUndoController _undoController;
        private CommandQueue _commandQueue;

        // ================================================================
        // 選択システム
        // ================================================================

        private SelectionState _selectionState;
        private TopologyCache _meshTopology;
        private SelectionOperations _selectionOps;

        // ================================================================
        // GUI 状態
        // ================================================================

        private readonly HashSet<int> _expandedModels = new HashSet<int>();
        private int _selectedModelIndex = -1;
        private int _selectedMeshIndex = -1;

        private readonly List<string> _logMessages = new List<string>();
        private const int MaxLogLines = 30;

        // ================================================================
        // UIToolkit 要素参照
        // ================================================================

        private TextField _hostField;
        private IntegerField _portField;
        private Button _btnConnect;
        private Label _lblConnected;
        private Button _btnDisconnect;
        private Button _btnFetchProject;
        private Button _btnFetchModel;
        private Button _btnFetchMesh;

        private Button _btnSelect;
        private Button _btnMove;
        private Button _btnUndo;
        private Button _btnRedo;
        private Label _lblVertexCount;

        private Toggle _toggleMesh;
        private Toggle _toggleWire;
        private Toggle _toggleVert;
        private Toggle _toggleCull;
        private Toggle _toggleBone;

        private Label _lblProjectSummary;
        private VisualElement _treeContainer;
        private ScrollView _logScroll;
        private VisualElement _logContainer;
        private IMGUIContainer _viewportImgui;

        // ================================================================
        // 直結モード（IPolyLingCore）
        // ================================================================
        // WebSocketモード: サーバーのPolyLingCoreからバイナリフレームを受信して表示
        // 直結モード:      同一プロセス内のPolyLingCoreを直接参照して表示（WebSocket不要）
        //
        // ConnectDirect() で直結モードに入る。
        // DisconnectDirect() または OnDisable で解除される。
        // どちらのモードも GetActiveProject() / GetActiveModel() を通じてデータを取得する。
        // ================================================================

        private Poly_Ling.Core.IPolyLingCore _directCore;

        /// <summary>
        /// 直結モードで接続する。
        /// 同一Editorプロセス内のPolyLingCoreを直接参照し、
        /// WebSocket受信なしにビューポート/ツール操作を行う。
        /// </summary>
        public void ConnectDirect(Poly_Ling.Core.IPolyLingCore core)
        {
            if (core == null) return;

            // 既存のWebSocket接続があれば切断
            if (_isConnected) Disconnect();

            _directCore = core;
            _project = core.Project;

            // Coreのイベントを購読してUI更新
            _directCore.OnCurrentModelChanged += OnDirectCoreModelChanged;
            _directCore.OnMeshListChanged      += OnDirectCoreMeshListChanged;

            Log("[直結] PolyLingCore に直接接続しました");
            UpdateConnectionUI();
            RebuildTree();
            Repaint();
        }

        /// <summary>
        /// 直結モードを解除する。
        /// </summary>
        public void DisconnectDirect()
        {
            if (_directCore == null) return;
            _directCore.OnCurrentModelChanged -= OnDirectCoreModelChanged;
            _directCore.OnMeshListChanged      -= OnDirectCoreMeshListChanged;
            _directCore = null;
            Log("[直結] 接続を解除しました");
            UpdateConnectionUI();
        }

        // 直結モード時のイベントハンドラ
        private void OnDirectCoreModelChanged()  => _mainThreadQueue.Enqueue(() => { RebuildTree(); Repaint(); });
        private void OnDirectCoreMeshListChanged() => _mainThreadQueue.Enqueue(() => { RebuildTree(); Repaint(); });

        /// <summary>
        /// 現在有効なProjectContextを返す。
        /// 直結モードではCoreから、WebSocketモードでは受信データから取得する。
        /// </summary>
        private ProjectContext GetActiveProject() => _directCore?.Project ?? _project;

        /// <summary>直結モードで動作中かどうか</summary>
        private bool IsDirectMode => _directCore != null;

        // ================================================================
        // ウィンドウ
        // ================================================================

        [MenuItem("Tools/PolyLing Remote Client V3")]
        public static void Open() => GetWindow<RemoteClientV3>("Remote V3");

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            wantsMouseMove = true;
            _viewport = new ViewportCore();
            SetTool(_selectTool);
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            // 直結モードを先に解除（イベント購読解除）
            DisconnectDirect();
            Disconnect();
            _viewport?.Dispose();
            _viewport = null;
            _undoController?.Dispose();
            _undoController = null;
        }

        // ================================================================
        // UIToolkit 構築
        // ================================================================

        private void CreateGUI()
        {
            const string uxmlPath = "Packages/com.haglib.polyling/Editor/Utility/RemoteClient/V3/RemoteClientV3.uxml";
            const string ussPath = "Packages/com.haglib.polyling/Editor/Utility/RemoteClient/V3/RemoteClientV3.uss";

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            if (visualTree == null)
            {
                rootVisualElement.Add(new Label($"UXML not found: {uxmlPath}"));
                return;
            }
            visualTree.CloneTree(rootVisualElement);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);

            // 要素取得
            _hostField = rootVisualElement.Q<TextField>("host-field");
            _portField = rootVisualElement.Q<IntegerField>("port-field");
            _btnConnect = rootVisualElement.Q<Button>("btn-connect");
            _lblConnected = rootVisualElement.Q<Label>("lbl-connected");
            _btnDisconnect = rootVisualElement.Q<Button>("btn-disconnect");
            _btnFetchProject = rootVisualElement.Q<Button>("btn-fetch-project");
            _btnFetchModel = rootVisualElement.Q<Button>("btn-fetch-model");
            _btnFetchMesh = rootVisualElement.Q<Button>("btn-fetch-mesh");
            _btnSelect = rootVisualElement.Q<Button>("btn-select");
            _btnMove = rootVisualElement.Q<Button>("btn-move");
            _btnUndo = rootVisualElement.Q<Button>("btn-undo");
            _btnRedo = rootVisualElement.Q<Button>("btn-redo");
            _lblVertexCount = rootVisualElement.Q<Label>("lbl-vertex-count");
            _toggleMesh = rootVisualElement.Q<Toggle>("toggle-mesh");
            _toggleWire = rootVisualElement.Q<Toggle>("toggle-wire");
            _toggleVert = rootVisualElement.Q<Toggle>("toggle-vert");
            _toggleCull = rootVisualElement.Q<Toggle>("toggle-cull");
            _toggleBone = rootVisualElement.Q<Toggle>("toggle-bone");
            _lblProjectSummary = rootVisualElement.Q<Label>("lbl-project-summary");
            _treeContainer = rootVisualElement.Q<VisualElement>("tree-container");
            _logScroll = rootVisualElement.Q<ScrollView>("log-scroll");
            _logContainer = rootVisualElement.Q<VisualElement>("log-container");
            _viewportImgui = rootVisualElement.Q<IMGUIContainer>("viewport-imgui");

            // コールバック登録
            _btnConnect.clicked += Connect;
            _btnDisconnect.clicked += Disconnect;
            _btnFetchProject.clicked += FetchProjectHeader;
            _btnFetchModel.clicked += () => FetchModelMeta(_selectedModelIndex >= 0 ? _selectedModelIndex : 0);
            _btnFetchMesh.clicked += () =>
            {
                if (_selectedModelIndex >= 0 && _selectedMeshIndex >= 0)
                    FetchMeshData(_selectedModelIndex, _selectedMeshIndex);
            };

            _btnSelect.clicked += () => SetTool(_selectTool);
            _btnMove.clicked += () => SetTool(_moveTool);
            _btnUndo.clicked += () =>
            {
                if (_undoController != null && _undoController.CanUndo)
                {
                    _undoController.Undo();
                    _viewport?.SyncSelectionState();
                    _viewport?.RequestNormal();
                    Repaint();
                }
            };
            _btnRedo.clicked += () =>
            {
                if (_undoController != null && _undoController.CanRedo)
                {
                    _undoController.Redo();
                    _viewport?.SyncSelectionState();
                    _viewport?.RequestNormal();
                    Repaint();
                }
            };

            _hostField.RegisterValueChangedCallback(e => _host = e.newValue);
            _portField.RegisterValueChangedCallback(e => _port = e.newValue);

            _toggleMesh.RegisterValueChangedCallback(e => { _showMesh = e.newValue; ApplyViewSettings(); });
            _toggleWire.RegisterValueChangedCallback(e => { _showWireframe = e.newValue; ApplyViewSettings(); });
            _toggleVert.RegisterValueChangedCallback(e => { _showVertices = e.newValue; ApplyViewSettings(); });
            _toggleCull.RegisterValueChangedCallback(e => { _backfaceCulling = e.newValue; ApplyViewSettings(); });
            _toggleBone.RegisterValueChangedCallback(e => { _showBones = e.newValue; ApplyViewSettings(); });

            rootVisualElement.Q<Button>("btn-clear-log").clicked += () =>
            {
                _logMessages.Clear();
                RefreshLog();
            };

            // ビューポート（IMGUIContainer）
            _viewportImgui.onGUIHandler = () =>
            {
                Rect rect = GUILayoutUtility.GetRect(
                    GUIContent.none, GUIStyle.none,
                    GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                if (rect.width < 10 || rect.height < 10) return;
                _undoController?.HandleKeyboardShortcuts(Event.current);
                DrawViewport(rect);
            };

            // UIToolkit MouseMoveEvent: IMGUI への MouseMove 配信が不安定なため
            // UIToolkit レベルで受け取り RequestNormal + MarkDirtyRepaint する
            _viewportImgui.RegisterCallback<MouseMoveEvent>(_ =>
            {
                _viewport?.Adapter?.RequestNormal();
                _viewportImgui.MarkDirtyRepaint();
            });

            // 初期 UI 状態を反映
            UpdateConnectionUI();
            UpdateToolUI();

            // RequestRepaint を初期 viewport に設定（ホバー再描画に必要）
            ApplyViewSettings();
        }

        // ================================================================
        // Tick（メインスレッドキュー処理 + 軽量 UI 更新）
        // ================================================================

        private void Tick()
        {
            // 直結モード時: PolyLingCore.Tick() はPolyLing側のEditorApplication.updateで
            // 処理されるため、ここでは重複呼び出しをしない。
            // WebSocketモード時: クライアント独自のCommandQueue/UndoManagerを処理する。
            if (!IsDirectMode)
            {
                _commandQueue?.ProcessAll();
                UndoManager.Instance.ProcessAllQueues();
            }

            int processed = 0;
            while (_mainThreadQueue.TryDequeue(out var action) && processed < 10)
            {
                try { action(); }
                catch (Exception ex) { Log($"エラー: {ex.Message}"); }
                processed++;
            }

            // 選択頂点数は毎フレーム反映
            if (_lblVertexCount != null)
            {
                var model = GetSelectedModel();
                var mc = model?.FirstSelectedMeshContext;
                string cnt = mc != null ? mc.SelectedVertices.Count.ToString() : "0";
                if (_lblVertexCount.text != cnt)
                    _lblVertexCount.text = cnt;
            }

            if (processed > 0) Repaint();
        }

        // ================================================================
        // UI 更新メソッド群
        // ================================================================

        private void UpdateConnectionUI()
        {
            if (_btnConnect == null) return;

            // 直結モード時はWebSocket接続UIを非表示にし、直結中であることを表示する
            bool webSocketVisible = !IsDirectMode;
            _btnConnect.style.display    = (!IsDirectMode && !_isConnected) ? DisplayStyle.Flex : DisplayStyle.None;
            _lblConnected.style.display  = (_isConnected || IsDirectMode)   ? DisplayStyle.Flex : DisplayStyle.None;
            _btnDisconnect.style.display = _isConnected                     ? DisplayStyle.Flex : DisplayStyle.None;
            _hostField.SetEnabled(!_isConnected && !IsDirectMode);
            _portField.SetEnabled(!_isConnected && !IsDirectMode);

            // 直結モードではFetchボタンは不要（データは直接参照するため）
            _btnFetchProject.SetEnabled(_isConnected && !IsDirectMode);
            _btnFetchModel.SetEnabled(_isConnected && !IsDirectMode && GetActiveProject() != null);
            _btnFetchMesh.SetEnabled(_isConnected && !IsDirectMode && GetActiveProject() != null &&
                                      _selectedModelIndex >= 0 && _selectedMeshIndex >= 0);

            if (IsDirectMode && _lblConnected != null)
                _lblConnected.text = "[直結モード] PolyLingCore 接続中";
        }

        private void UpdateToolUI()
        {
            if (_btnSelect == null) return;

            if (_currentTool == _selectTool)
            {
                _btnSelect.AddToClassList("rc3-tool-btn-active");
                _btnMove.RemoveFromClassList("rc3-tool-btn-active");
            }
            else
            {
                _btnSelect.RemoveFromClassList("rc3-tool-btn-active");
                _btnMove.AddToClassList("rc3-tool-btn-active");
            }
        }

        private void RefreshProjectSummary()
        {
            if (_lblProjectSummary == null) return;
            if (GetActiveProject() == null) { _lblProjectSummary.text = _projectStatus; return; }
            int tv = 0, tf = 0;
            foreach (var m in GetActiveProject().Models)
                foreach (var mc in m.MeshContextList)
                { tv += mc.VertexCount; tf += mc.FaceCount; }
            _lblProjectSummary.text =
                $"{GetActiveProject().Name}  {GetActiveProject().ModelCount}M  V:{tv:N0} F:{tf:N0}";
        }

        private void RebuildTree()
        {
            if (_treeContainer == null) return;
            _treeContainer.Clear();
            if (GetActiveProject() == null) return;

            for (int mi = 0; mi < GetActiveProject().ModelCount; mi++)
            {
                var model = GetActiveProject().Models[mi];
                bool isCur = mi == GetActiveProject().CurrentModelIndex;
                bool isExp = _expandedModels.Contains(mi);
                int miCopy = mi;

                // モデル行
                var modelRow = new VisualElement();
                modelRow.AddToClassList("rc3-model-row");
                if (_selectedModelIndex == mi) modelRow.AddToClassList("rc3-selected");

                var foldBtn = new Button(() =>
                {
                    if (_expandedModels.Contains(miCopy)) _expandedModels.Remove(miCopy);
                    else _expandedModels.Add(miCopy);
                    RebuildTree();
                });
                foldBtn.text = $"{(isExp ? "▼" : "▶")} {(isCur ? "★" : " ")} [{mi}] {model.Name}";
                foldBtn.AddToClassList("rc3-model-foldout-btn");

                var selBtn = new Button(() =>
                {
                    _selectedModelIndex = miCopy;
                    _selectedMeshIndex = -1;
                    RebuildTree();
                    UpdateConnectionUI();
                });
                selBtn.text = "▶";
                selBtn.AddToClassList("rc3-model-select-btn");

                modelRow.Add(foldBtn);
                modelRow.Add(selBtn);
                _treeContainer.Add(modelRow);

                if (isExp)
                {
                    foreach (var e in model.DrawableMeshes)
                        AddMeshRowElement(mi, e.MasterIndex, e.Context);
                }
            }
        }

        private void AddMeshRowElement(int modelIndex, int meshIndex, MeshContext mc)
        {
            bool isSel = _selectedModelIndex == modelIndex && _selectedMeshIndex == meshIndex;
            int miCopy = modelIndex;
            int siCopy = meshIndex;

            var row = new VisualElement();
            row.AddToClassList("rc3-mesh-row");
            if (isSel) row.AddToClassList("rc3-selected");
            row.style.paddingLeft = mc.Depth * 8;

            var btn = new Button(() => SelectMesh(miCopy, siCopy));
            btn.text = $"{(mc.IsVisible ? "●" : "○")} {meshIndex}: {mc.Name}";
            btn.AddToClassList("rc3-mesh-btn");

            var vLbl = new Label($"V:{mc.VertexCount}");
            vLbl.AddToClassList("rc3-mesh-vcount");

            row.Add(btn);
            row.Add(vLbl);
            _treeContainer.Add(row);
        }

        private void RefreshLog()
        {
            if (_logContainer == null) return;
            _logContainer.Clear();
            foreach (var msg in _logMessages)
            {
                var lbl = new Label(msg);
                lbl.AddToClassList("rc3-log-line");
                _logContainer.Add(lbl);
            }
            // 末尾へスクロール
            if (_logScroll != null)
                _logScroll.schedule.Execute(() =>
                    _logScroll.scrollOffset = new Vector2(0, float.MaxValue));
        }

        // ================================================================
        // ツール切り替え
        // ================================================================

        private void SetTool(IEditTool tool)
        {
            if (_currentTool == tool) return;
            _currentTool?.OnDeactivate(_toolContext);
            _currentTool = tool;
            _currentTool?.OnActivate(_toolContext);
            UpdateToolUI();
        }

        // ================================================================
        // ToolContext 構築
        // ================================================================

        private void BuildToolContext(ModelContext model)
        {
            if (_toolContext == null)
                _toolContext = new ToolContext();

            _toolContext.Project = GetActiveProject();
            _toolContext.Model = model;

            if (_viewport != null)
            {
                _toolContext.CameraPosition = _viewport.Camera != null
                    ? _viewport.Camera.transform.position
                    : Vector3.zero;
                _toolContext.CameraTarget = _viewport.Target;
                _toolContext.CameraDistance = _viewport.Distance;
            }

            var cam = _viewport?.Camera;
            _toolContext.WorldToScreenPos = (worldPos, rect, _, _2) =>
            {
                if (cam == null) return Vector2.zero;
                // DisplayMatrix が identity 以外の場合は頂点位置を変換して投影
                Vector3 transformed = _toolContext.DisplayMatrix != Matrix4x4.identity
                    ? _toolContext.DisplayMatrix.MultiplyPoint3x4(worldPos)
                    : worldPos;
                Vector3 sp = cam.WorldToScreenPoint(transformed);
                if (sp.z <= 0) return new Vector2(-9999, -9999);
                return new Vector2(sp.x, rect.height - sp.y);
            };

            _toolContext.ScreenDeltaToWorldDelta = (screenDelta, camPos, lookAt, camDist, rect) =>
            {
                Vector3 forward = (lookAt - camPos).normalized;
                Quaternion lookRot = Quaternion.LookRotation(forward, Vector3.up);
                Quaternion rollRot = Quaternion.AngleAxis(_viewport != null ? _viewport.RotZ : 0f, Vector3.forward);
                Quaternion camRot = lookRot * rollRot;
                Vector3 right = camRot * Vector3.right;
                Vector3 up = camRot * Vector3.up;
                float fovRad = (_viewport != null ? _viewport.FOV : 30f) * Mathf.Deg2Rad;
                float worldH = 2f * camDist * Mathf.Tan(fovRad / 2f);
                float px2w = worldH / rect.height;
                return right * screenDelta.x * px2w - up * screenDelta.y * px2w;
            };

            _toolContext.FindVertexAtScreenPos = (screenPos, meshObj, rect, camPos, lookAt, radius) =>
            {
                if (meshObj == null || cam == null) return -1;
                int best = -1;
                float bestDist = radius;
                bool hasMatrix = _toolContext.DisplayMatrix != Matrix4x4.identity;
                for (int i = 0; i < meshObj.VertexCount; i++)
                {
                    Vector3 pos = meshObj.Vertices[i].Position;
                    if (hasMatrix) pos = _toolContext.DisplayMatrix.MultiplyPoint3x4(pos);
                    Vector2 sp = _toolContext.WorldToScreenPos(pos, rect, camPos, lookAt);
                    float d = Vector2.Distance(screenPos, sp);
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                return best;
            };

            _toolContext.SyncMesh = () => RebuildUnityMesh(model);
            _toolContext.SyncMeshPositionsOnly = () => RebuildUnityMesh(model);

            var firstMc = model?.FirstSelectedMeshContext;
            if (firstMc != null && firstMc.OriginalPositions == null && firstMc.MeshObject != null)
                firstMc.OriginalPositions = firstMc.MeshObject.Vertices.Select(v => v.Position).ToArray();
            _toolContext.OriginalPositions = firstMc?.OriginalPositions;

            // ── Undo（クライアント独立スタック） ──
            _undoController?.Dispose();
            _undoController = new MeshUndoController("RemoteClientV3");
            _commandQueue = new CommandQueue();
            _undoController.SetCommandQueue(_commandQueue);

            // MeshListStack.Context を model と同一インスタンスにする
            // → MeshList Undo/Redo 時に model.SelectedMeshIndices が直接復元される
            _undoController.SetModelContext(model);

            // ParentModelContext を設定：MeshUndoContext.ResolvedMeshContext が
            // model.FirstSelectedMeshContext を返すようにする（MeshObject解決に必要）
            _undoController.MeshUndoContext.ParentModelContext = model;

            if (firstMc?.MeshObject != null)
                _undoController.SetMeshObject(firstMc.MeshObject, firstMc.UnityMesh);

            _undoController.OnUndoRedoPerformed += () =>
            {
                var ctx = _undoController.MeshUndoContext;
                var stackType = _undoController.LastUndoRedoStackType;

                if (stackType == MeshUndoController.UndoStackType.VertexEdit)
                {
                    if (ctx.DirtyMeshIndices.Count > 0)
                    {
                        // MultiMeshVertexMoveRecord: PendingMeshMoveEntries を適用
                        if (ctx.PendingMeshMoveEntries != null)
                        {
                            foreach (var entry in ctx.PendingMeshMoveEntries)
                            {
                                var mc = model.GetMeshContext(entry.MeshContextIndex);
                                if (mc?.MeshObject == null) continue;
                                for (int i = 0; i < entry.Indices.Length; i++)
                                {
                                    int idx = entry.Indices[i];
                                    if (idx >= 0 && idx < mc.MeshObject.VertexCount)
                                        mc.MeshObject.Vertices[idx].Position = entry.NewPositions[i];
                                }
                                mc.MeshObject.InvalidatePositionCache();
                                if (mc.OriginalPositions != null)
                                    for (int i = 0; i < entry.Indices.Length; i++)
                                    {
                                        int idx = entry.Indices[i];
                                        if (idx >= 0 && idx < mc.OriginalPositions.Length)
                                            mc.OriginalPositions[idx] = mc.MeshObject.Vertices[idx].Position;
                                    }
                            }
                            ctx.PendingMeshMoveEntries = null;
                        }
                        // 汚染メッシュの UnityMesh を再構築
                        foreach (var idx in ctx.DirtyMeshIndices)
                        {
                            var mc = model.GetMeshContext(idx);
                            if (mc == null) continue;
                            RebuildMeshContext(mc);
                        }
                        ctx.DirtyMeshIndices.Clear();
                    }
                    else
                    {
                        // 単一メッシュ操作（VertexMoveRecord等）
                        RebuildUnityMesh(model);
                    }

                    // 選択スナップショット復元
                    if (ctx.CurrentSelectionSnapshot != null)
                    {
                        var mc = model.FirstSelectedMeshContext;
                        if (mc != null)
                        {
                            mc.SelectedVertices.Clear();
                            if (ctx.CurrentSelectionSnapshot.Vertices != null)
                                foreach (var vi in ctx.CurrentSelectionSnapshot.Vertices)
                                    mc.SelectedVertices.Add(vi);
                        }
                        ctx.CurrentSelectionSnapshot = null;
                    }
                }
                else if (stackType == MeshUndoController.UndoStackType.MeshList)
                {
                    // MeshSelectionChangeRecord が model.SelectedMeshIndices を直接復元済み
                    // (_meshListStack.Context == model のため)
                    // _selectedModelIndex / _selectedMeshIndex を同期してビューを更新
                    _selectedMeshIndex = model.SelectedMeshIndices.Count > 0
                        ? model.SelectedMeshIndices[0] : -1;

                    if (_viewport?.CurrentModel == model)
                    {
                        _viewport.SyncSelectionState();
                        _viewport.RequestNormal();
                    }

                    RebuildTree();
                    UpdateConnectionUI();
                }

                _viewport?.SyncSelectionState();
                _viewport?.RequestNormal();
                Repaint();
            };

            _toolContext.UndoController = _undoController;
            _toolContext.RecordSelectionChange = (oldSel, newSel) =>
                _undoController.RecordSelectionChange(oldSel, newSel);

            // ──────────────────────────────────────────────────────
            // 選択システム初期化（メインパネルの InitializeSelectionSystem 相当）
            // ──────────────────────────────────────────────────────
            var mc0 = model?.FirstSelectedMeshContext;
            _selectionState = mc0?.Selection ?? new SelectionState();
            _meshTopology = new TopologyCache();
            _meshTopology.SetMeshObject(mc0?.MeshObject);
            _selectionOps = new SelectionOperations(_selectionState, _meshTopology);
            _selectionOps.EdgeHitDistance = 18f;
            _selectionState.OnSelectionChanged += () =>
            {
                _viewport?.SyncSelectionState();
                _viewport?.RequestNormal();
                Repaint();
            };

            _toolContext.SelectionState = _selectionState;
            _toolContext.TopologyCache = _meshTopology;
            _toolContext.SelectionOps = _selectionOps;
            _toolContext.SelectedVertices = _selectionState.Vertices;  // mc.SelectedVertices と同一インスタンス

            // ── SyncBoneTransforms ──
            _toolContext.SyncBoneTransforms = () =>
            {
                model?.ComputeWorldMatrices();
                _viewport?.RequestNormal();
            };

            // ── NotifyTopologyChanged ──
            _toolContext.NotifyTopologyChanged = () =>
            {
                _meshTopology?.Invalidate();
                _viewport?.Adapter?.NotifyTopologyChanged();
                _viewport?.RequestNormal();
            };

            // ── CameraFOV ──
            _toolContext.CameraFOV = _viewport != null ? _viewport.FOV : 30f;

            // ── HoverVertexRadius / HoverLineDistance ──
            _toolContext.HoverVertexRadius = 12f;
            _toolContext.HoverLineDistance = 18f;

            // ── WorkPlane ──
            _toolContext.WorkPlane = _undoController?.WorkPlane;

            // ── VertexOffsets / GroupOffsets (PivotOffsetTool等向け) ──
            int vc = mc0?.MeshObject?.VertexCount ?? 0;
            _toolContext.VertexOffsets = vc > 0 ? new Vector3[vc] : null;
            _toolContext.GroupOffsets = vc > 0 ? new Vector3[vc] : null;

            // ── ScreenPosToRay (RotZ込み) ──
            _toolContext.ScreenPosToRay = (screenPos) =>
            {
                var vp = _viewport;
                if (vp == null) return new Ray(Vector3.zero, Vector3.forward);
                var previewRect = _toolContext.PreviewRect;
                var camPos = _toolContext.CameraPosition;
                var lookAt = _toolContext.CameraTarget;
                float rotZ = vp.RotZ;
                float ndcX = ((screenPos.x - previewRect.x) / previewRect.width) * 2f - 1f;
                float ndcY = 1f - ((screenPos.y - previewRect.y) / previewRect.height) * 2f;
                Vector3 forward = (lookAt - camPos).normalized;
                Quaternion lookRot = Quaternion.LookRotation(forward, Vector3.up);
                Quaternion rollRot = Quaternion.AngleAxis(rotZ, Vector3.forward);
                Quaternion camRot = lookRot * rollRot;
                Vector3 right = camRot * Vector3.right;
                Vector3 up = camRot * Vector3.up;
                float halfFovRad = vp.FOV * 0.5f * Mathf.Deg2Rad;
                float aspect = previewRect.width / previewRect.height;
                Vector3 dir = forward
                    + right * (ndcX * Mathf.Tan(halfFovRad) * aspect)
                    + up * (ndcY * Mathf.Tan(halfFovRad));
                return new Ray(camPos, dir.normalized);
            };

            // ── EnterTransformDragging / ExitTransformDragging ──
            _toolContext.EnterTransformDragging = () => _viewport?.Adapter?.EnterTransformDragging();
            _toolContext.ExitTransformDragging = () => _viewport?.Adapter?.ExitTransformDragging();

            // ── SetSuppressHover ──
            _toolContext.SetSuppressHover = (suppress) =>
            {
                if (_viewport?.Adapter?.UnifiedSystem != null)
                    _viewport.Adapter.UnifiedSystem.SuppressHover = suppress;
            };

            // ── Repaint ──
            _toolContext.Repaint = Repaint;

            _currentTool?.OnActivate(_toolContext);
        }

        private void RebuildUnityMesh(ModelContext model)
        {
            if (model == null) return;
            var adapter = _viewport?.Adapter;

            foreach (var mc in model.SelectedMeshContexts)
            {
                if (mc?.MeshObject == null) continue;

                if (mc.UnityMesh == null || mc.UnityMesh.vertexCount != mc.MeshObject.VertexCount)
                {
                    if (mc.UnityMesh != null) DestroyImmediate(mc.UnityMesh);
                    mc.UnityMesh = mc.MeshObject.ToUnityMesh();
                    adapter?.NotifyTopologyChanged();
                }
                else
                {
                    var verts = new Vector3[mc.MeshObject.VertexCount];
                    for (int i = 0; i < verts.Length; i++)
                        verts[i] = mc.MeshObject.Vertices[i].Position;
                    mc.UnityMesh.vertices = verts;
                    mc.UnityMesh.RecalculateBounds();
                    adapter?.NotifyTransformChanged();
                }
            }

            _viewport?.RequestNormal();
        }

        /// <summary>単一 MeshContext の UnityMesh を MeshObject から再構築</summary>
        private void RebuildMeshContext(MeshContext mc)
        {
            if (mc?.MeshObject == null) return;
            var adapter = _viewport?.Adapter;
            if (mc.UnityMesh == null || mc.UnityMesh.vertexCount != mc.MeshObject.VertexCount)
            {
                if (mc.UnityMesh != null) DestroyImmediate(mc.UnityMesh);
                mc.UnityMesh = mc.MeshObject.ToUnityMesh();
                adapter?.NotifyTopologyChanged();
            }
            else
            {
                var verts = new Vector3[mc.MeshObject.VertexCount];
                for (int i = 0; i < verts.Length; i++)
                    verts[i] = mc.MeshObject.Vertices[i].Position;
                mc.UnityMesh.vertices = verts;
                mc.UnityMesh.RecalculateBounds();
                adapter?.NotifyTransformChanged();
            }
        }

        // ================================================================
        // 表示設定適用
        // ================================================================

        private void ApplyViewSettings()
        {
            if (_viewport == null) return;
            _viewport.ShowMesh = _showMesh;
            _viewport.ShowWireframe = _showWireframe;
            _viewport.ShowVertices = _showVertices;
            _viewport.BackfaceCulling = _backfaceCulling;
            _viewport.ShowUnselectedWireframe = _showUnselectedWire;
            _viewport.ShowBones = _showBones;
            _viewport.RequestRepaint = () => { Repaint(); _viewportImgui?.MarkDirtyRepaint(); };
        }

        // ================================================================
        // ビューポート描画（IMGUIContainer から呼ばれる）
        // ================================================================

        private void DrawViewport(Rect rect)
        {
            if (_viewport == null) return;

            var model = GetSelectedModel();

            if (model == null)
            {
                EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));
                var s = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
                    alignment = TextAnchor.MiddleCenter
                };
                GUI.Label(rect, GetActiveProject() == null ? "プロジェクト未受信" : "モデルを選択してください", s);
                return;
            }

            if (_viewport.CurrentModel != model)
            {
                _viewport.Dispose();
                _viewport = new ViewportCore();
                ApplyViewSettings();
                _viewport.Init(model);

                BuildToolContext(model);
                _inputHandler = new ToolInputHandler(this, model);
                _viewport.OnHandleInput = evt => _inputHandler.HandleInput(evt);
                _viewport.OnDrawOverlay = evt => _inputHandler.DrawOverlay(evt);
            }

            _viewport.Draw(rect);

            if (Event.current.type == EventType.Repaint)
            {
                var ls = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
                GUI.Label(new Rect(4, 2, rect.width - 8, 16),
                    $"[{_selectedModelIndex}] {model.Name}  V:{CountTotalV(model):N0}  " +
                    $"Tool: {_currentTool?.DisplayName ?? "-"}", ls);
            }
        }

        private int CountTotalV(ModelContext m)
        {
            int n = 0;
            foreach (var mc in m.MeshContextList) n += mc.VertexCount;
            return n;
        }

        private ModelContext GetSelectedModel()
        {
            if (_project == null || _selectedModelIndex < 0 ||
                _selectedModelIndex >= GetActiveProject().ModelCount) return null;
            return GetActiveProject().Models[_selectedModelIndex];
        }

        // ================================================================
        // メッシュ選択
        // ================================================================

        private void SelectMesh(int modelIndex, int meshIndex)
        {
            _selectedModelIndex = modelIndex;
            _selectedMeshIndex = meshIndex;

            var model = GetSelectedModel();
            if (model != null && _viewport?.CurrentModel == model)
            {
                var oldIndices = new List<int>(model.SelectedMeshIndices);
                model.SelectDrawable(meshIndex);
                var newIndices = new List<int>(model.SelectedMeshIndices);

                if (_undoController != null && !oldIndices.SequenceEqual(newIndices))
                    _undoController.RecordMeshSelectionChange(oldIndices, newIndices);

                _viewport.SyncSelectionState();
                _viewport.RequestNormal();
                if (_toolContext != null) _toolContext.Model = model;
            }

            RebuildTree();
            UpdateConnectionUI();
            Repaint();
        }

        // ================================================================
        // 接続管理
        // ================================================================

        private void Connect()
        {
            if (_isConnected) return;
            _cts = new CancellationTokenSource();
            _ws = new RemoteClientWs();
            _ = ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            try
            {
                bool ok = await _ws.ConnectAsync(_host, _port, _cts.Token);
                _mainThreadQueue.Enqueue(() =>
                {
                    if (ok)
                    {
                        _isConnected = true;
                        Log($"接続: {_host}:{_port}");
                        UpdateConnectionUI();
                    }
                    else Log("接続失敗");
                    Repaint();
                });
                if (ok) await ReceiveLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    Log($"接続エラー: {ex.Message}");
                    _isConnected = false;
                    UpdateConnectionUI();
                    Repaint();
                });
            }
        }

        private void Disconnect()
        {
            _cts?.Cancel(); _ws?.Close(); _ws = null; _isConnected = false;
            _textCallbacks.Clear(); _binaryCallbacks.Clear();
            Log("切断");
            UpdateConnectionUI();
        }

        // ================================================================
        // 受信ループ
        // ================================================================

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _ws != null && _ws.IsConnected)
                {
                    var frame = await _ws.ReceiveFrameAsync(ct);
                    if (frame == null || frame.Value.Type == WsFrameType.Close) break;
                    if (frame.Value.Type == WsFrameType.Ping) continue;
                    var f = frame.Value;
                    if (f.Type == WsFrameType.Text) _mainThreadQueue.Enqueue(() => HandleTextMessage(f.Text));
                    else if (f.Type == WsFrameType.Binary) _mainThreadQueue.Enqueue(() => HandleBinaryMessage(f.Binary));
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    _isConnected = false;
                    Log("切断検知");
                    UpdateConnectionUI();
                    Repaint();
                });
            }
        }

        // ================================================================
        // メッセージ処理
        // ================================================================

        private void HandleTextMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            string id = ExtractJsonString(json, "id");
            string type = ExtractJsonString(json, "type");
            if (type == "push") { Log($"Push: {ExtractJsonString(json, "event")}"); return; }
            if (id != null && _binaryCallbacks.ContainsKey(id)) { _lastTextResponseId = id; _lastTextResponseJson = json; return; }
            if (id != null && _textCallbacks.TryGetValue(id, out var cb)) { _textCallbacks.Remove(id); cb(json); }
        }

        private void HandleBinaryMessage(byte[] data)
        {
            if (_lastTextResponseId != null && _binaryCallbacks.TryGetValue(_lastTextResponseId, out var cb))
            {
                _binaryCallbacks.Remove(_lastTextResponseId);
                cb(_lastTextResponseJson, data);
                _lastTextResponseId = _lastTextResponseJson = null;
                return;
            }
            uint magic = Poly_Ling.Remote.RemoteMagic.Read(data);
            if (magic == Poly_Ling.Remote.RemoteMagic.Batch) { DispatchBatch(data); return; }
            DispatchFrame(magic, data);
        }

        private void DispatchBatch(byte[] data)
        {
            if (data.Length < 12) return;
            int fc = (int)BitConverter.ToUInt32(data, 8), offset = 12;
            for (int i = 0; i < fc; i++)
            {
                if (offset + 4 > data.Length) break;
                int fl = (int)BitConverter.ToUInt32(data, offset); offset += 4;
                if (offset + fl > data.Length) break;
                byte[] fr = new byte[fl]; Array.Copy(data, offset, fr, 0, fl); offset += fl;
                DispatchFrame(Poly_Ling.Remote.RemoteMagic.Read(fr), fr);
            }
        }

        private void DispatchFrame(uint magic, byte[] data)
        {
            if (magic == Poly_Ling.Remote.RemoteMagic.ProjectHeader) ReceiveProjectHeader(data);
            else if (magic == Poly_Ling.Remote.RemoteMagic.ModelMeta) ReceiveModelMeta(data);
            else if (magic == Poly_Ling.Remote.RemoteMagic.MeshSummary) ReceiveMeshSummary(data);
            else if (magic == Poly_Ling.Remote.RemoteMagic.MeshData) ReceiveMeshData(data);
        }

        // ================================================================
        // 受信ハンドラ
        // ================================================================

        private void ReceiveProjectHeader(byte[] data)
        {
            var r = RemoteProgressiveSerializer.DeserializeProjectHeader(data);
            if (r == null) { Log("PLRH失敗"); return; }
            var (name, mc, ci) = r.Value;
            _project = new ProjectContext { Name = name };
            for (int i = 0; i < mc; i++) _project.Models.Add(new ModelContext($"Model{i}"));
            _project.CurrentModelIndex = ci;
            _selectedModelIndex = ci; _selectedMeshIndex = -1;
            _expandedModels.Clear(); for (int i = 0; i < mc; i++) _expandedModels.Add(i);
            _projectStatus = $"受信中... ({name} {mc}モデル)";
            Log($"PLRH: \"{name}\" {mc}モデル");
            RefreshProjectSummary();
            RebuildTree();
            UpdateConnectionUI();
        }

        private void ReceiveModelMeta(byte[] data)
        {
            var r = RemoteProgressiveSerializer.DeserializeModelMeta(data);
            if (r == null || _project == null) return;
            var (mi, model) = r.Value;
            while (_project.Models.Count <= mi) _project.Models.Add(new ModelContext($"Model{_project.Models.Count}"));
            _project.Models[mi] = model;
            Log($"PLRM: [{mi}] \"{model.Name}\" meshes={model.Count}");
            RebuildTree();
        }

        private void ReceiveMeshSummary(byte[] data)
        {
            var r = RemoteProgressiveSerializer.DeserializeMeshSummary(data);
            if (r == null || _project == null) return;
            var (mi, si, mc, _, _2) = r.Value;
            if (mi >= _project.ModelCount) return;
            var model = _project.Models[mi];
            while (model.MeshContextList.Count <= si)
                model.MeshContextList.Add(new MeshContext { Name = $"Mesh{model.MeshContextList.Count}" });
            model.MeshContextList[si] = mc;
            model.InvalidateTypedIndices();
            if (si == model.Count - 1)
            {
                _projectStatus = $"OK ({_project.Name})";
                Log($"PLRS完了: [{mi}] total={model.Count}");
                RefreshProjectSummary();
            }
            RebuildTree();
            Repaint();
        }

        private void ReceiveMeshData(byte[] data)
        {
            var r = RemoteProgressiveSerializer.DeserializeMeshData(data);
            if (r == null || _project == null) return;
            var (mi, si, mesh) = r.Value;
            if (mi >= _project.ModelCount) return;
            var model = _project.Models[mi];
            if (si >= model.Count) return;
            var mc = model.MeshContextList[si];
            if (mc.UnityMesh != null) DestroyImmediate(mc.UnityMesh);
            string sn = mc.Name; MeshType st = mc.Type;
            mc.MeshObject = mesh;
            if (mesh != null) { mesh.Name = sn; mesh.Type = st; }
            if (mesh != null && mesh.VertexCount > 0) mc.UnityMesh = mesh.ToUnityMesh();

            if (_viewport?.CurrentModel == model)
            {
                _viewport.Dispose();
                _viewport = new ViewportCore();
                ApplyViewSettings();
                _viewport.Init(model);
                BuildToolContext(model);
                _inputHandler = new ToolInputHandler(this, model);
                _viewport.OnHandleInput = evt => _inputHandler.HandleInput(evt);
                _viewport.OnDrawOverlay = evt => _inputHandler.DrawOverlay(evt);
            }
            Log($"PLRD: [{mi}][{si}] \"{mc.Name}\" V={mesh?.VertexCount ?? 0}");
            Repaint();
        }

        // ================================================================
        // クエリ送信
        // ================================================================

        private void FetchProjectHeader()
        {
            string id = NextId();
            _projectStatus = "受信中...";
            RefreshProjectSummary();
            SendBinaryQuery($"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"project_header\"}}", (_, bd) =>
            {
                HandleBinaryMessage(bd);
                if (_project != null) FetchAllModelsBatch(0);
            });
            Log("project_header 送信");
        }

        private void FetchAllModelsBatch(int mi)
        {
            if (_project == null || mi >= _project.ModelCount) return;
            FetchMeshDataBatch(mi, "bone", () =>
                FetchMeshDataBatch(mi, "drawable", () =>
                {
                    _projectStatus = $"OK ({_project?.Name})";
                    RefreshProjectSummary();
                    Repaint();
                    FetchMeshDataBatch(mi, "morph", () =>
                    {
                        int next = mi + 1;
                        if (next < (_project?.ModelCount ?? 0)) FetchAllModelsBatch(next); else Repaint();
                    });
                }));
        }

        private void FetchMeshDataBatch(int mi, string cat, Action done = null)
        {
            string id = NextId();
            SendBinaryQuery(
                $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"mesh_data_batch\"," +
                $"\"params\":{{\"modelIndex\":\"{mi}\",\"category\":\"{cat}\"}}}}",
                (_, bd) => { if (bd != null && bd.Length >= 4) HandleBinaryMessage(bd); done?.Invoke(); });
        }

        private void FetchModelMeta(int mi)
        {
            string id = NextId();
            SendBinaryQuery(
                $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"model_meta\"," +
                $"\"params\":{{\"modelIndex\":\"{mi}\"}}}}",
                (_, bd) => HandleBinaryMessage(bd));
            Log($"model_meta [{mi}]");
        }

        private void FetchMeshData(int mi, int si)
        {
            string id = NextId();
            SendBinaryQuery(
                $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"mesh_data\"," +
                $"\"params\":{{\"modelIndex\":\"{mi}\",\"meshIndex\":\"{si}\"}}}}",
                (_, bd) => HandleBinaryMessage(bd));
            Log($"mesh_data [{mi}][{si}]");
        }

        private string NextId() => $"v3_{++_requestId}";

        private void SendBinaryQuery(string json, Action<string, byte[]> onResponse)
        {
            string id = ExtractJsonString(json, "id");
            if (id != null) _binaryCallbacks[id] = onResponse;
            _ = _ws.SendTextAsync(json);
        }

        // ================================================================
        // ログ
        // ================================================================

        private void Log(string m)
        {
            _logMessages.Add($"[{DateTime.Now:HH:mm:ss}] {m}");
            while (_logMessages.Count > MaxLogLines) _logMessages.RemoveAt(0);
            RefreshLog();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static string ExtractJsonString(string json, string key)
        {
            string s = $"\"{key}\"";
            int i = json.IndexOf(s, StringComparison.Ordinal); if (i < 0) return null;
            int c = json.IndexOf(':', i + s.Length); if (c < 0) return null;
            int vs = c + 1; while (vs < json.Length && json[vs] == ' ') vs++;
            if (vs >= json.Length || json[vs] != '"') return null;
            int ve = json.IndexOf('"', vs + 1); if (ve < 0) return null;
            return json.Substring(vs + 1, ve - vs - 1);
        }

        // ================================================================
        // ToolInputHandler — V2 から変更なし
        // ================================================================

        private class ToolInputHandler
        {
            private readonly RemoteClientV3 _owner;
            private readonly ModelContext _model;

            private ViewportInputState _inp = new ViewportInputState();
            private bool _shiftHeld;
            private bool _ctrlHeld;
            private bool _isDraggingCamera;

            public ToolInputHandler(RemoteClientV3 owner, ModelContext model)
            {
                _owner = owner;
                _model = model;
            }

            public void HandleInput(ViewportEvent evt)
            {
                var e = Event.current;
                var ctx = _owner._toolContext;
                var tool = _owner._currentTool;
                var rect = evt.Rect;

                if (ctx == null) return;

                ctx.CameraPosition = evt.CameraPos;
                ctx.CameraTarget = evt.CameraTarget;
                ctx.CameraDistance = evt.CameraDistance;
                ctx.PreviewRect = rect;
                ctx.CameraFOV = evt.CameraFOV;

                _shiftHeld = e.shift;
                _ctrlHeld = e.control;

                var mousePos = e.mousePosition;

                if (!rect.Contains(mousePos) && _inp.EditState == VertexEditState.Idle) return;

                if (e.type == EventType.MouseDown && e.button == 1 && rect.Contains(mousePos))
                {
                    _isDraggingCamera = true;
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    e.Use();
                    return;
                }
                if (e.type == EventType.MouseDrag && e.button == 1)
                {
                    if (_isDraggingCamera)
                    {
                        // RotZ分だけマウスデルタを逆回転（メインパネルと同一仕様）
                        float rotZ = _owner._viewport.RotZ;
                        float zRad = -rotZ * Mathf.Deg2Rad;
                        float cos = Mathf.Cos(zRad);
                        float sin = Mathf.Sin(zRad);
                        float adjX = e.delta.x * cos - e.delta.y * sin;
                        float adjY = e.delta.x * sin + e.delta.y * cos;
                        _owner._viewport.RotY += adjX * 0.5f;
                        _owner._viewport.RotX += adjY * 0.5f;
                        _owner._viewport.RotX = Mathf.Clamp(_owner._viewport.RotX, -89f, 89f);
                        _owner._viewport.RequestNormal();
                        e.Use();
                        _owner.Repaint();
                    }
                    return;
                }
                if (e.type == EventType.MouseUp && e.button == 1 && _isDraggingCamera)
                {
                    _isDraggingCamera = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    return;
                }

                // 中ボタンドラッグ: パン（メインパネルと同一仕様）
                if (e.type == EventType.MouseDown && e.button == 2 && rect.Contains(mousePos))
                {
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    e.Use();
                    return;
                }
                if (e.type == EventType.MouseDrag && e.button == 2)
                {
                    var ctx2 = _owner._toolContext;
                    if (ctx2 != null && ctx2.ScreenDeltaToWorldDelta != null)
                    {
                        float multiplier = e.control ? 0.1f : (e.shift ? 3f : 1f);
                        Vector3 worldDelta = ctx2.ScreenDeltaToWorldDelta(
                            e.delta, ctx2.CameraPosition, ctx2.CameraTarget,
                            _owner._viewport.Distance, rect);
                        _owner._viewport.Target -= worldDelta * multiplier;
                        _owner._viewport.RequestNormal();
                        e.Use();
                        _owner.Repaint();
                    }
                    return;
                }
                if (e.type == EventType.MouseUp && e.button == 2)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                    return;
                }

                if (e.type == EventType.ScrollWheel && rect.Contains(mousePos))
                {
                    float sv = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
                    if (e.shift)
                    {
                        // Shift+ホイール: 注目点をカメラ視線方向に前後移動（メインパネルと同一仕様）
                        float rotZ = _owner._viewport.RotZ;
                        Quaternion r = Quaternion.Euler(_owner._viewport.RotX, _owner._viewport.RotY, rotZ);
                        Vector3 fwd = r * Vector3.forward;
                        float move = sv * _owner._viewport.Distance * 0.05f;
                        _owner._viewport.Target += fwd * move;
                    }
                    else
                    {
                        // 通常: ズーム（Ctrl=低速）
                        float sensitivity = e.control ? 0.01f : 0.05f;
                        _owner._viewport.Distance *= 1f + sv * sensitivity;
                        _owner._viewport.Distance = Mathf.Clamp(_owner._viewport.Distance, 0.05f, 100f);
                    }
                    _owner._viewport.RequestNormal();
                    e.Use();
                    _owner.Repaint();
                    return;
                }

                switch (e.type)
                {
                    case EventType.MouseDown when e.button == 0:
                        OnMouseDown(mousePos, ctx, tool, rect, evt);
                        break;
                    case EventType.MouseDrag when e.button == 0:
                        OnMouseDrag(mousePos, e.delta, ctx, tool, rect, evt);
                        break;
                    case EventType.MouseUp when e.button == 0:
                        OnMouseUp(mousePos, ctx, tool, rect, evt);
                        break;
                }
            }

            private void OnMouseDown(Vector2 mousePos, ToolContext ctx, IEditTool tool, Rect rect, ViewportEvent evt)
            {
                _inp.MouseDownScreenPos = mousePos;
                _inp.EditState = VertexEditState.PendingAction;

                bool toolHandled = tool?.OnMouseDown(ctx, mousePos) ?? false;

                if (!toolHandled)
                {
                    var mc = _model.FirstSelectedMeshContext;
                    if (mc?.MeshObject != null)
                    {
                        int vi = ctx.FindVertexAtScreenPos(mousePos, mc.MeshObject,
                            rect, evt.CameraPos, evt.CameraTarget, ctx.HoverVertexRadius);
                        _inp.HitResultOnMouseDown = vi >= 0
                            ? new HitResult { HitType = MeshSelectMode.Vertex, VertexIndex = vi }
                            : HitResult.None;

                        // 【仕様】メインパネルの hitIsAlreadySelected + ApplySelectionOnMouseDown 相当
                        // 未選択頂点のドラッグ移動が機能するよう、MouseDown時点で選択を更新する。
                        //   ヒット頂点が未選択 → 選択更新（Shift:追加, 修飾なし:クリア+追加）
                        //   ヒット頂点が既選択 → 変更しない（全選択頂点をドラッグ移動）
                        //   空白クリック      → 変更しない（ドラッグ→矩形選択、クリック→MouseUpでクリア）
                        if (vi >= 0 && !mc.SelectedVertices.Contains(vi))
                        {
                            // 未選択頂点 → 選択更新
                            if (!_shiftHeld && !_ctrlHeld)
                                mc.SelectedVertices.Clear();
                            mc.SelectedVertices.Add(vi);
                        }
                        else if (vi < 0 && !_shiftHeld && !_ctrlHeld)
                        {
                            // 空白クリック（非加算） → 選択クリア
                            // メインパネルと同様: クリア後 UpdateAffectedVertices = 0 → MoveTool Idle → 矩形選択可能
                            mc.SelectedVertices.Clear();
                        }
                    }
                    // 選択更新後に再通知（MoveTool が UpdateAffectedVertices で選択を拾えるようにする）
                    tool?.OnMouseDown(ctx, mousePos);
                }
            }
            private void OnMouseDrag(Vector2 mousePos, Vector2 delta, ToolContext ctx, IEditTool tool, Rect rect, ViewportEvent evt)
            {
                bool toolHandled = tool?.OnMouseDrag(ctx, mousePos, delta) ?? false;

                if (!toolHandled)
                {
                    float dist = Vector2.Distance(mousePos, _inp.MouseDownScreenPos);
                    if (dist > ViewportInputState.DragThreshold)
                    {
                        if (_inp.EditState == VertexEditState.PendingAction)
                        {
                            _inp.EditState = _inp.DragSelectMode == DragSelectMode.Lasso
                                ? VertexEditState.LassoSelecting
                                : VertexEditState.BoxSelecting;
                            _inp.BoxSelectStart = _inp.MouseDownScreenPos;
                            if (_inp.EditState == VertexEditState.LassoSelecting)
                            {
                                _inp.LassoPoints.Clear();
                                _inp.LassoPoints.Add(_inp.MouseDownScreenPos);
                            }
                        }

                        if (_inp.EditState == VertexEditState.BoxSelecting)
                            _inp.BoxSelectEnd = mousePos;
                        else if (_inp.EditState == VertexEditState.LassoSelecting)
                            if (_inp.LassoPoints.Count == 0 ||
                                Vector2.Distance(mousePos, _inp.LassoPoints[_inp.LassoPoints.Count - 1]) > 2f)
                                _inp.LassoPoints.Add(mousePos);

                        _owner._viewport?.RequestNormal();
                        _owner.Repaint();
                    }
                }
            }

            private void OnMouseUp(Vector2 mousePos, ToolContext ctx, IEditTool tool, Rect rect, ViewportEvent evt)
            {
                bool toolHandled = tool?.OnMouseUp(ctx, mousePos) ?? false;

                if (!toolHandled)
                {
                    var mc = _model.FirstSelectedMeshContext;
                    if (mc?.MeshObject == null) { ResetInputState(); return; }

                    if (_inp.EditState == VertexEditState.BoxSelecting) FinishBoxSelect(mc, rect, evt);
                    else if (_inp.EditState == VertexEditState.LassoSelecting) FinishLassoSelect(mc, rect, evt);
                    else if (_inp.EditState == VertexEditState.PendingAction) ApplyClickSelection(mc, evt);
                }

                ResetInputState();
                _owner._viewport?.SyncSelectionState();
                _owner._viewport?.RequestNormal();
                _owner.Repaint();
            }

            private void ApplyClickSelection(MeshContext mc, ViewportEvent evt)
            {
                bool shift = _shiftHeld, ctrl = _ctrlHeld;
                var hit = _inp.HitResultOnMouseDown;
                var ctx = _owner._toolContext;

                var oldSel = new HashSet<int>(mc.SelectedVertices);

                if (hit.HitType == MeshSelectMode.Vertex && hit.VertexIndex >= 0)
                {
                    bool hitIsSelected = mc.SelectedVertices.Contains(hit.VertexIndex);
                    if (ctrl)
                    {
                        // Ctrl+既選択 → 除外
                        if (hitIsSelected) mc.SelectedVertices.Remove(hit.VertexIndex);
                    }
                    else if (shift)
                    {
                        mc.SelectedVertices.Add(hit.VertexIndex);
                    }
                    else if (!hitIsSelected)
                    {
                        // 未選択頂点クリック → 新規選択
                        mc.SelectedVertices.Clear();
                        mc.SelectedVertices.Add(hit.VertexIndex);
                    }
                    // else: 既選択頂点クリック（修飾なし） → 変更しない（メインパネルと同一仕様）
                }
                else if (!shift && !ctrl)
                    mc.ClearSelection();

                ctx?.RecordSelectionChange?.Invoke(oldSel, new HashSet<int>(mc.SelectedVertices));
            }
            private void FinishBoxSelect(MeshContext mc, Rect rect, ViewportEvent evt)
            {
                bool shift = _shiftHeld, ctrl = _ctrlHeld;
                bool additive = shift || ctrl;

                var selectRect = new Rect(
                    Mathf.Min(_inp.BoxSelectStart.x, _inp.BoxSelectEnd.x),
                    Mathf.Min(_inp.BoxSelectStart.y, _inp.BoxSelectEnd.y),
                    Mathf.Abs(_inp.BoxSelectEnd.x - _inp.BoxSelectStart.x),
                    Mathf.Abs(_inp.BoxSelectEnd.y - _inp.BoxSelectStart.y));

                var oldSel = new HashSet<int>(mc.SelectedVertices);
                if (!additive) mc.SelectedVertices.Clear();

                var ctx = _owner._toolContext;
                var adapter = _owner._viewport?.Adapter;
                adapter?.ReadBackVertexFlags();
                int meshIdx = _model.FirstSelectedIndex;

                for (int i = 0; i < mc.MeshObject.VertexCount; i++)
                {
                    if (_owner._backfaceCulling && adapter != null &&
                        adapter.IsVertexCulled(meshIdx, i)) continue;
                    Vector2 sp = ctx.WorldToScreenPos(mc.MeshObject.Vertices[i].Position,
                        rect, evt.CameraPos, evt.CameraTarget);
                    if (selectRect.Contains(sp))
                    {
                        if (ctrl) mc.SelectedVertices.Remove(i);
                        else mc.SelectedVertices.Add(i);
                    }
                }

                ctx?.RecordSelectionChange?.Invoke(oldSel, new HashSet<int>(mc.SelectedVertices));
            }
            private void FinishLassoSelect(MeshContext mc, Rect rect, ViewportEvent evt)
            {
                if (_inp.LassoPoints.Count < 3) return;
                bool additive = _shiftHeld || _ctrlHeld;
                bool ctrl = _ctrlHeld;

                var oldSel = new HashSet<int>(mc.SelectedVertices);
                if (!additive) mc.SelectedVertices.Clear();

                var ctx = _owner._toolContext;
                var adapter = _owner._viewport?.Adapter;
                adapter?.ReadBackVertexFlags();
                int meshIdx = _model.FirstSelectedIndex;

                for (int i = 0; i < mc.MeshObject.VertexCount; i++)
                {
                    if (_owner._backfaceCulling && adapter != null &&
                        adapter.IsVertexCulled(meshIdx, i)) continue;
                    Vector2 sp = ctx.WorldToScreenPos(mc.MeshObject.Vertices[i].Position,
                        rect, evt.CameraPos, evt.CameraTarget);
                    if (IsPointInLasso(sp, _inp.LassoPoints))
                    {
                        if (ctrl) mc.SelectedVertices.Remove(i);
                        else mc.SelectedVertices.Add(i);
                    }
                }

                ctx?.RecordSelectionChange?.Invoke(oldSel, new HashSet<int>(mc.SelectedVertices));
            }
            private static bool IsPointInLasso(Vector2 point, List<Vector2> polygon)
            {
                if (polygon == null || polygon.Count < 3) return false;
                bool inside = false;
                int count = polygon.Count, j = count - 1;
                for (int i = 0; i < count; i++)
                {
                    if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                        point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) /
                                  (polygon[j].y - polygon[i].y) + polygon[i].x)
                        inside = !inside;
                    j = i;
                }
                return inside;
            }

            /// <summary>
            /// GPU計算済みホバー結果を ToolContext.LastHoverHitResult に反映（メインパネルと同一ロジック）
            /// </summary>
            private void UpdateLastHoverHitResultFromUnified()
            {
                var ctx = _owner._toolContext;
                var adapter = _owner._viewport?.Adapter;
                if (ctx == null || adapter == null) return;

                var bufferManager = adapter.BufferManager;
                if (bufferManager == null) return;

                int globalVertex = adapter.HoverVertexIndex;
                int globalLine = adapter.HoverLineIndex;
                int globalFace = adapter.HoverFaceIndex;

                int localVertex = -1;
                int localFace = -1;
                int validGlobalLine = -1;
                float vertexDist = float.MaxValue;
                float lineDist = float.MaxValue;
                int hitMeshIndex = -1;

                if (globalVertex >= 0 &&
                    bufferManager.GlobalToLocalVertexIndex(globalVertex, out int vMeshIdx, out int vLocalIdx))
                {
                    localVertex = vLocalIdx;
                    vertexDist = 0f;
                    hitMeshIndex = bufferManager.UnifiedToContextMeshIndex(vMeshIdx);
                }

                if (globalLine >= 0 &&
                    bufferManager.GlobalToLocalLineIndex(globalLine, out int lMeshIdx, out int _))
                {
                    validGlobalLine = globalLine;
                    lineDist = 0f;
                    if (hitMeshIndex < 0)
                        hitMeshIndex = bufferManager.UnifiedToContextMeshIndex(lMeshIdx);
                }

                if (globalFace >= 0 &&
                    bufferManager.GlobalToLocalFaceIndex(globalFace, out int fMeshIdx, out int fLocalIdx))
                {
                    localFace = fLocalIdx;
                    if (hitMeshIndex < 0)
                        hitMeshIndex = bufferManager.UnifiedToContextMeshIndex(fMeshIdx);
                }

                ctx.LastHoverHitResult = new Poly_Ling.Rendering.GPUHitTestResult
                {
                    NearestVertexIndex = localVertex,
                    NearestVertexDistance = vertexDist,
                    NearestVertexDepth = 0f,
                    NearestLineIndex = validGlobalLine,
                    NearestLineDistance = lineDist,
                    NearestLineDepth = 0f,
                    HitFaceIndices = localFace >= 0 ? new int[] { localFace } : null
                };
            }

            private void ResetInputState()
            {
                _inp.EditState = VertexEditState.Idle;
                _inp.LassoPoints.Clear();
                _inp.HitResultOnMouseDown = HitResult.None;
            }

            public void DrawOverlay(ViewportEvent evt)
            {
                // GPU計算済みホバー結果を ToolContext に毎フレーム同期（メインパネルの UpdateLastHoverHitResultFromUnified 相当）
                UpdateLastHoverHitResultFromUnified();

                if (Event.current.type != EventType.Repaint) return;

                if (_inp.EditState == VertexEditState.BoxSelecting)
                {
                    DrawBoxSelectOverlay();
                    return;
                }

                if (_inp.EditState == VertexEditState.LassoSelecting && _inp.LassoPoints.Count > 1)
                {
                    DrawLassoOverlay();
                    return;
                }

                _owner._currentTool?.DrawGizmo(_owner._toolContext);
            }

            private void DrawBoxSelectOverlay()
            {
                Rect r = new Rect(
                    Mathf.Min(_inp.BoxSelectStart.x, _inp.BoxSelectEnd.x),
                    Mathf.Min(_inp.BoxSelectStart.y, _inp.BoxSelectEnd.y),
                    Mathf.Abs(_inp.BoxSelectEnd.x - _inp.BoxSelectStart.x),
                    Mathf.Abs(_inp.BoxSelectEnd.y - _inp.BoxSelectStart.y));

                EditorGUI.DrawRect(r, new Color(0.3f, 0.6f, 1f, 0.12f));
                Handles.BeginGUI();
                Handles.color = new Color(0.4f, 0.7f, 1f, 0.9f);
                Handles.DrawAAPolyLine(1.5f,
                    new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
                    new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax),
                    new Vector2(r.xMin, r.yMin));
                Handles.EndGUI();
            }

            private void DrawLassoOverlay()
            {
                var pts = _inp.LassoPoints;
                Handles.BeginGUI();
                Handles.color = new Color(0.4f, 1f, 0.6f, 0.9f);
                for (int i = 0; i < pts.Count - 1; i++)
                    Handles.DrawAAPolyLine(1.5f, pts[i], pts[i + 1]);
                if (pts.Count > 2)
                {
                    Handles.color = new Color(0.4f, 1f, 0.6f, 0.4f);
                    Handles.DrawAAPolyLine(1f, pts[pts.Count - 1], pts[0]);
                }
                Handles.EndGUI();
            }
        }
    }
}
