// RemoteClient/RemoteClientV2.cs
// ワイヤフレーム・頂点・カリング・ツール入力対応のリモートクライアント
// ツール: SelectTool（矩形/投げ縄選択）、MoveTool（頂点移動）

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PolyLingRemoteClient;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.Remote;
using Poly_Ling.MeshListV2;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Remote
{
    public class RemoteClientV2 : EditorWindow
    {
        // ================================================================
        // 接続設定
        // ================================================================

        private string _host = "localhost";
        private int    _port = 8765;

        // ================================================================
        // WebSocket
        // ================================================================

        private RemoteClientWs                              _ws;
        private CancellationTokenSource                    _cts;
        private bool                                        _isConnected;
        private readonly ConcurrentQueue<Action>           _mainThreadQueue = new ConcurrentQueue<Action>();
        private int                                         _requestId;
        private readonly Dictionary<string, Action<string>>           _textCallbacks   = new Dictionary<string, Action<string>>();
        private readonly Dictionary<string, Action<string, byte[]>>   _binaryCallbacks = new Dictionary<string, Action<string, byte[]>>();
        private string _lastTextResponseId;
        private string _lastTextResponseJson;

        // ================================================================
        // 受信データ
        // ================================================================

        private ProjectContext _project;
        private string         _projectStatus = "未受信";

        // ================================================================
        // ビューポート
        // ================================================================

        private ViewportCore _viewport;

        // ================================================================
        // ツール
        // ================================================================

        private ToolContext  _toolContext;
        private IEditTool    _currentTool;
        private SelectTool   _selectTool = new SelectTool();
        private MoveTool     _moveTool   = new MoveTool();

        // 入力処理
        private ToolInputHandler _inputHandler;

        // ================================================================
        // 表示設定
        // ================================================================

        private bool _showMesh           = true;
        private bool _showWireframe      = true;
        private bool _showVertices       = false;
        private bool _backfaceCulling    = true;
        private bool _showUnselectedWire = true;
        private bool _showBones          = true;

        // ================================================================
        // GUI状態
        // ================================================================

        private Vector2 _treeScroll;
        private Vector2 _logScroll;
        private readonly HashSet<int> _expandedModels = new HashSet<int>();
        private int     _selectedModelIndex = -1;
        private int     _selectedMeshIndex  = -1;

        private readonly List<string> _logMessages = new List<string>();
        private const int MaxLogLines = 30;

        private float _splitX       = 260f;
        private bool  _draggingSplit;

        // ================================================================
        // ウィンドウ
        // ================================================================

        [MenuItem("Tools/PolyLing Remote Client V2")]
        public static void Open() => GetWindow<RemoteClientV2>("Remote V2");

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            _viewport = new ViewportCore();
            SetTool(_selectTool);
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            Disconnect();
            _viewport?.Dispose();
            _viewport = null;
        }

        private void Tick()
        {
            int processed = 0;
            while (_mainThreadQueue.TryDequeue(out var action) && processed < 10)
            {
                try { action(); }
                catch (Exception ex) { Log($"エラー: {ex.Message}"); }
                processed++;
            }
            if (processed > 0) Repaint();
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
        }

        // ================================================================
        // ToolContext 構築
        // ================================================================

        private void BuildToolContext(ModelContext model)
        {
            if (_toolContext == null)
                _toolContext = new ToolContext();

            _toolContext.Project = _project;
            _toolContext.Model   = model;

            // カメラ情報は ViewportCore から取得
            if (_viewport != null)
            {
                _toolContext.CameraPosition = _viewport.Camera != null
                    ? _viewport.Camera.transform.position
                    : Vector3.zero;
                _toolContext.CameraTarget   = _viewport.Target;
                _toolContext.CameraDistance = _viewport.Distance;
            }

            // 座標変換: Camera.WorldToScreenPoint ベース（Editor非依存）
            var cam = _viewport?.Camera;
            _toolContext.WorldToScreenPos = (worldPos, rect, _, _2) =>
            {
                if (cam == null) return Vector2.zero;
                Vector3 sp = cam.WorldToScreenPoint(worldPos);
                if (sp.z <= 0) return new Vector2(-9999, -9999);
                return new Vector2(sp.x, rect.height - sp.y);
            };

            // スクリーンデルタ→ワールドデルタ（純粋な行列演算）
            _toolContext.ScreenDeltaToWorldDelta = (screenDelta, camPos, lookAt, camDist, rect) =>
            {
                Vector3 forward  = (lookAt - camPos).normalized;
                Quaternion camRot = Quaternion.LookRotation(forward, Vector3.up);
                Vector3 right    = camRot * Vector3.right;
                Vector3 up       = camRot * Vector3.up;
                float fovRad     = (_viewport != null ? _viewport.FOV : 30f) * Mathf.Deg2Rad;
                float worldH     = 2f * camDist * Mathf.Tan(fovRad / 2f);
                float px2w       = worldH / rect.height;
                return right * screenDelta.x * px2w - up * screenDelta.y * px2w;
            };

            // 頂点ヒットテスト
            _toolContext.FindVertexAtScreenPos = (screenPos, meshObj, rect, camPos, lookAt, radius) =>
            {
                if (meshObj == null || cam == null) return -1;
                int    best     = -1;
                float  bestDist = radius;
                for (int i = 0; i < meshObj.VertexCount; i++)
                {
                    Vector2 sp = _toolContext.WorldToScreenPos(meshObj.Vertices[i].Position, rect, camPos, lookAt);
                    float   d  = Vector2.Distance(screenPos, sp);
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                return best;
            };

            // メッシュ同期: MeshObject → UnityMesh 再構築
            _toolContext.SyncMesh = () => RebuildUnityMesh(model);
            _toolContext.SyncMeshPositionsOnly = () => RebuildUnityMesh(model);

            // OriginalPositions: MoveTool がリセット用に使用
            var firstMc = model?.FirstSelectedMeshContext;
            if (firstMc != null && firstMc.OriginalPositions == null && firstMc.MeshObject != null)
            {
                firstMc.OriginalPositions = firstMc.MeshObject.Vertices
                    .Select(v => v.Position).ToArray();
            }
            _toolContext.OriginalPositions = firstMc?.OriginalPositions;

            // Undo（クライアント側はシンプルに無効化）
            _toolContext.UndoController = null;

            _currentTool?.OnActivate(_toolContext);
        }

        /// <summary>
        /// 頂点位置のみをUnityMeshとGPUバッファに反映（MoveTool中に毎フレーム呼ばれる）。
        /// 頂点数が変わった場合のみフルリビルド。
        /// </summary>
        private void RebuildUnityMesh(ModelContext model)
        {
            if (model == null) return;
            var adapter = _viewport?.Adapter;

            foreach (var mc in model.SelectedMeshContexts)
            {
                if (mc?.MeshObject == null) continue;

                if (mc.UnityMesh == null || mc.UnityMesh.vertexCount != mc.MeshObject.VertexCount)
                {
                    // フルリビルド（トポロジ変化時）
                    if (mc.UnityMesh != null) DestroyImmediate(mc.UnityMesh);
                    mc.UnityMesh = mc.MeshObject.ToUnityMesh();
                    adapter?.NotifyTopologyChanged();
                }
                else
                {
                    // 位置のみ更新（高速パス）
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

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            float totalW = position.width;
            float totalH = position.height;

            HandleSplitDrag(totalH);

            float leftW  = Mathf.Clamp(_splitX, 180f, totalW - 100f);
            float rightW = totalW - leftW - 4f;

            GUILayout.BeginArea(new Rect(0, 0, leftW, totalH));
            DrawLeftPane(leftW, totalH);
            GUILayout.EndArea();

            var splitRect = new Rect(leftW, 0, 4f, totalH);
            EditorGUI.DrawRect(splitRect, new Color(0.15f, 0.15f, 0.15f));
            EditorGUIUtility.AddCursorRect(splitRect, MouseCursor.ResizeHorizontal);

            GUILayout.BeginArea(new Rect(leftW + 4f, 0, rightW, totalH));
            DrawViewport(new Rect(0, 0, rightW, totalH));
            GUILayout.EndArea();
        }

        private void HandleSplitDrag(float totalH)
        {
            float leftW     = Mathf.Clamp(_splitX, 180f, position.width - 100f);
            var   splitRect = new Rect(leftW, 0, 6f, totalH);
            int   id        = GUIUtility.GetControlID(FocusType.Passive);
            var   e         = Event.current;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (splitRect.Contains(e.mousePosition)) { _draggingSplit = true; GUIUtility.hotControl = id; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (_draggingSplit) { _splitX = Mathf.Clamp(e.mousePosition.x, 180f, position.width - 100f); e.Use(); Repaint(); }
                    break;
                case EventType.MouseUp:
                    if (_draggingSplit) { _draggingSplit = false; GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
        }

        // ================================================================
        // 左ペイン
        // ================================================================

        private void DrawLeftPane(float w, float h)
        {
            EditorGUILayout.LabelField("Remote Client V2", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);
            DrawConnectionUI();
            EditorGUILayout.Space(2);
            DrawToolButtons();
            EditorGUILayout.Space(2);
            DrawViewSettings();
            EditorGUILayout.Space(2);
            DrawProjectSummary();
            EditorGUILayout.Space(2);

            float treeH = Mathf.Max(100f, h - 400f);
            DrawModelMeshTree(treeH);

            EditorGUILayout.Space(2);
            DrawLog();
        }

        // ================================================================
        // ツールボタン
        // ================================================================

        private void DrawToolButtons()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Tool", EditorStyles.miniBoldLabel, GUILayout.Width(32));

            var selStyle  = _currentTool == _selectTool ? EditorStyles.miniButtonLeft  : EditorStyles.miniButtonLeft;
            var moveStyle = _currentTool == _moveTool   ? EditorStyles.miniButtonRight : EditorStyles.miniButtonRight;

            var bgSel  = GUI.backgroundColor;
            var bgMove = GUI.backgroundColor;
            if (_currentTool == _selectTool) GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Toggle(_currentTool == _selectTool, "Select", EditorStyles.miniButtonLeft, GUILayout.Width(60)))
                SetTool(_selectTool);
            GUI.backgroundColor = _currentTool == _moveTool ? new Color(0.4f, 0.8f, 1f) : bgMove;
            if (GUILayout.Toggle(_currentTool == _moveTool, "Move", EditorStyles.miniButtonRight, GUILayout.Width(50)))
                SetTool(_moveTool);
            GUI.backgroundColor = bgSel;

            // 選択モード
            var model = GetSelectedModel();
            if (model != null)
            {
                var mc = model.FirstSelectedMeshContext;
                if (mc != null)
                {
                    EditorGUILayout.LabelField("V:", EditorStyles.miniLabel, GUILayout.Width(14));
                    EditorGUILayout.LabelField(mc.SelectedVertices.Count.ToString(), EditorStyles.miniLabel, GUILayout.Width(30));
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // 表示設定
        // ================================================================

        private void DrawViewSettings()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("表示", EditorStyles.miniBoldLabel, GUILayout.Width(32));

            bool nm = GUILayout.Toggle(_showMesh,           "Mesh", EditorStyles.miniButtonLeft,  GUILayout.Width(44));
            bool nw = GUILayout.Toggle(_showWireframe,      "Wire", EditorStyles.miniButtonMid,   GUILayout.Width(38));
            bool nv = GUILayout.Toggle(_showVertices,       "Vert", EditorStyles.miniButtonMid,   GUILayout.Width(38));
            bool nc = GUILayout.Toggle(_backfaceCulling,    "Cull", EditorStyles.miniButtonMid,   GUILayout.Width(38));
            bool nb = GUILayout.Toggle(_showBones,          "Bone", EditorStyles.miniButtonRight, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();

            if (nm != _showMesh || nw != _showWireframe || nv != _showVertices ||
                nc != _backfaceCulling || nb != _showBones)
            {
                _showMesh = nm; _showWireframe = nw; _showVertices = nv;
                _backfaceCulling = nc; _showBones = nb;
                ApplyViewSettings();
                Repaint();
            }
        }

        private void ApplyViewSettings()
        {
            if (_viewport == null) return;
            _viewport.ShowMesh                = _showMesh;
            _viewport.ShowWireframe           = _showWireframe;
            _viewport.ShowVertices            = _showVertices;
            _viewport.BackfaceCulling         = _backfaceCulling;
            _viewport.ShowUnselectedWireframe = _showUnselectedWire;
            _viewport.ShowBones               = _showBones;
            _viewport.RequestRepaint          = Repaint;
        }

        // ================================================================
        // 3Dビューポート（右ペイン）
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
                    normal    = { textColor = new Color(0.5f, 0.5f, 0.5f) },
                    alignment = TextAnchor.MiddleCenter
                };
                GUI.Label(rect, _project == null ? "プロジェクト未受信" : "モデルを選択してください", s);
                return;
            }

            // モデルが切り替わったら再構築
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

            // オーバーレイ: モデル名・ツール名
            if (Event.current.type == EventType.Repaint)
            {
                var ls = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
                GUI.Label(new Rect(4, 2, rect.width - 8, 16),
                    $"[{_selectedModelIndex}] {model.Name}  V:{CountTotalV(model):N0}  " +
                    $"Tool: {_currentTool?.DisplayName ?? "-"}", ls);
            }
        }

        private int CountTotalV(ModelContext m) { int n = 0; foreach (var mc in m.MeshContextList) n += mc.VertexCount; return n; }

        private ModelContext GetSelectedModel()
        {
            if (_project == null || _selectedModelIndex < 0 ||
                _selectedModelIndex >= _project.ModelCount) return null;
            return _project.Models[_selectedModelIndex];
        }

        // ================================================================
        // 接続UI
        // ================================================================

        private void DrawConnectionUI()
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_isConnected))
            {
                _host = EditorGUILayout.TextField(_host, GUILayout.ExpandWidth(true));
                _port = EditorGUILayout.IntField(_port, GUILayout.Width(55));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (!_isConnected)
            {
                if (GUILayout.Button("Connect", GUILayout.Width(80))) Connect();
            }
            else
            {
                EditorGUILayout.LabelField("● Connected", EditorStyles.boldLabel, GUILayout.Width(90));
                if (GUILayout.Button("Cut", GUILayout.Width(40))) Disconnect();
            }
            using (new EditorGUI.DisabledScope(!_isConnected))
            {
                if (GUILayout.Button("Fetch Project")) FetchProjectHeader();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!_isConnected || _project == null))
            {
                if (GUILayout.Button("Fetch Model", GUILayout.Width(90)))
                    FetchModelMeta(_selectedModelIndex >= 0 ? _selectedModelIndex : 0);
                if (GUILayout.Button("Fetch Mesh", GUILayout.Width(86)))
                    if (_selectedModelIndex >= 0 && _selectedMeshIndex >= 0)
                        FetchMeshData(_selectedModelIndex, _selectedMeshIndex);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // プロジェクトサマリー
        // ================================================================

        private void DrawProjectSummary()
        {
            if (_project == null) { EditorGUILayout.LabelField(_projectStatus, EditorStyles.miniLabel); return; }
            int tv = 0, tf = 0;
            foreach (var m in _project.Models) foreach (var mc in m.MeshContextList) { tv += mc.VertexCount; tf += mc.FaceCount; }
            EditorGUILayout.LabelField($"{_project.Name}  {_project.ModelCount}M  V:{tv:N0} F:{tf:N0}", EditorStyles.miniLabel);
        }

        // ================================================================
        // モデル/メッシュツリー
        // ================================================================

        private void DrawModelMeshTree(float height)
        {
            if (_project == null) return;
            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll, GUILayout.Height(height));

            for (int mi = 0; mi < _project.ModelCount; mi++)
            {
                var  model    = _project.Models[mi];
                bool isCur    = mi == _project.CurrentModelIndex;
                bool isExp    = _expandedModels.Contains(mi);
                bool isMSel   = _selectedModelIndex == mi;

                EditorGUILayout.BeginHorizontal();
                var bg = GUI.backgroundColor;
                if (isMSel) GUI.backgroundColor = new Color(0.35f, 0.65f, 1f);

                bool newExp = EditorGUILayout.Foldout(isExp, $"{(isCur ? "★" : " ")} [{mi}] {model.Name}", true);
                if (newExp != isExp) { if (newExp) _expandedModels.Add(mi); else _expandedModels.Remove(mi); }

                if (GUILayout.Button("▶", GUILayout.Width(22))) { _selectedModelIndex = mi; _selectedMeshIndex = -1; }
                GUI.backgroundColor = bg;
                EditorGUILayout.EndHorizontal();

                if (isExp)
                {
                    EditorGUI.indentLevel++;
                    foreach (var e in model.DrawableMeshes)
                        DrawMeshRow(mi, e.MasterIndex, e.Context);
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawMeshRow(int modelIndex, int meshIndex, MeshContext mc)
        {
            bool isSel = _selectedModelIndex == modelIndex && _selectedMeshIndex == meshIndex;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(mc.Depth * 8);
            var bg = GUI.backgroundColor;
            if (isSel) GUI.backgroundColor = new Color(0.35f, 0.55f, 1f);
            if (GUILayout.Button($"{(mc.IsVisible ? "●" : "○")} {meshIndex}: {mc.Name}", EditorStyles.miniButtonLeft))
                SelectMesh(modelIndex, meshIndex);
            GUILayout.Label($"V:{mc.VertexCount}", EditorStyles.miniLabel, GUILayout.Width(50));
            GUI.backgroundColor = bg;
            EditorGUILayout.EndHorizontal();
        }

        private void SelectMesh(int modelIndex, int meshIndex)
        {
            _selectedModelIndex = modelIndex;
            _selectedMeshIndex  = meshIndex;

            var model = GetSelectedModel();
            if (model != null && _viewport?.CurrentModel == model)
            {
                model.SelectDrawable(meshIndex);
                _viewport.SyncSelectionState();
                _viewport.RequestNormal();
                if (_toolContext != null) _toolContext.Model = model;
            }
            Repaint();
        }

        // ================================================================
        // 接続管理
        // ================================================================

        private void Connect()
        {
            if (_isConnected) return;
            _cts = new CancellationTokenSource();
            _ws  = new RemoteClientWs();
            _    = ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            try
            {
                bool ok = await _ws.ConnectAsync(_host, _port, _cts.Token);
                _mainThreadQueue.Enqueue(() =>
                {
                    if (ok) { _isConnected = true; Log($"接続: {_host}:{_port}"); }
                    else Log("接続失敗");
                    Repaint();
                });
                if (ok) await ReceiveLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                _mainThreadQueue.Enqueue(() => { Log($"接続エラー: {ex.Message}"); _isConnected = false; Repaint(); });
            }
        }

        private void Disconnect()
        {
            _cts?.Cancel(); _ws?.Close(); _ws = null; _isConnected = false;
            _textCallbacks.Clear(); _binaryCallbacks.Clear(); Log("切断");
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
                    if      (f.Type == WsFrameType.Text)   _mainThreadQueue.Enqueue(() => HandleTextMessage(f.Text));
                    else if (f.Type == WsFrameType.Binary) _mainThreadQueue.Enqueue(() => HandleBinaryMessage(f.Binary));
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                _mainThreadQueue.Enqueue(() => { _isConnected = false; Log("切断検知"); Repaint(); });
            }
        }

        // ================================================================
        // メッセージ処理
        // ================================================================

        private void HandleTextMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            string id = ExtractJsonString(json, "id"), type = ExtractJsonString(json, "type");
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
            if      (magic == Poly_Ling.Remote.RemoteMagic.ProjectHeader) ReceiveProjectHeader(data);
            else if (magic == Poly_Ling.Remote.RemoteMagic.ModelMeta)     ReceiveModelMeta(data);
            else if (magic == Poly_Ling.Remote.RemoteMagic.MeshSummary)   ReceiveMeshSummary(data);
            else if (magic == Poly_Ling.Remote.RemoteMagic.MeshData)      ReceiveMeshData(data);
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
        }

        private void ReceiveModelMeta(byte[] data)
        {
            var r = RemoteProgressiveSerializer.DeserializeModelMeta(data);
            if (r == null || _project == null) return;
            var (mi, model) = r.Value;
            while (_project.Models.Count <= mi) _project.Models.Add(new ModelContext($"Model{_project.Models.Count}"));
            _project.Models[mi] = model;
            Log($"PLRM: [{mi}] \"{model.Name}\" meshes={model.Count}");
        }

        private void ReceiveMeshSummary(byte[] data)
        {
            var r = RemoteProgressiveSerializer.DeserializeMeshSummary(data);
            if (r == null || _project == null) return;
            var (mi, si, mc, _, _2) = r.Value;
            if (mi >= _project.ModelCount) return;
            var model = _project.Models[mi];
            while (model.MeshContextList.Count <= si) model.MeshContextList.Add(new MeshContext { Name = $"Mesh{model.MeshContextList.Count}" });
            model.MeshContextList[si] = mc;
            model.InvalidateTypedIndices();
            if (si == model.Count - 1) { _projectStatus = $"OK ({_project.Name})"; Log($"PLRS完了: [{mi}] total={model.Count}"); }
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

            // ViewportCore が同じモデルなら再構築
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
                    _projectStatus = $"OK ({_project?.Name})"; Repaint();
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

        private string NextId() => $"v2_{++_requestId}";

        private void SendBinaryQuery(string json, Action<string, byte[]> onResponse)
        {
            string id = ExtractJsonString(json, "id");
            if (id != null) _binaryCallbacks[id] = onResponse;
            _ = _ws.SendTextAsync(json);
        }

        // ================================================================
        // ログ
        // ================================================================

        private void DrawLog()
        {
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(80));
            foreach (var msg in _logMessages) EditorGUILayout.LabelField(msg, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Clear Log", GUILayout.Width(72))) _logMessages.Clear();
        }

        private void Log(string m) { _logMessages.Add($"[{DateTime.Now:HH:mm:ss}] {m}"); while (_logMessages.Count > MaxLogLines) _logMessages.RemoveAt(0); }

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
        // ToolInputHandler — ツール入力・選択処理・オーバーレイ描画
        // ================================================================

        /// <summary>
        /// ViewportCore.OnHandleInput / OnDrawOverlay から呼ばれる入力ハンドラ。
        /// 新しいツールを追加する場合は IEditTool を実装してここに接続するだけ。
        /// </summary>
        private class ToolInputHandler
        {
            private readonly RemoteClientV2 _owner;
            private readonly ModelContext   _model;

            // 入力状態
            private ViewportInputState _inp = new ViewportInputState();
            private bool _shiftHeld;
            private bool _ctrlHeld;
            private bool _isDraggingCamera;

            public ToolInputHandler(RemoteClientV2 owner, ModelContext model)
            {
                _owner = owner;
                _model = model;
            }

            // ----------------------------------------------------------------
            // 入力処理（ViewportCore.OnHandleInput から呼ばれる）
            // ----------------------------------------------------------------

            public void HandleInput(ViewportEvent evt)
            {
                var e       = Event.current;
                var ctx     = _owner._toolContext;
                var tool    = _owner._currentTool;
                var rect    = evt.Rect;

                if (ctx == null) return;

                // ToolContext を最新化
                ctx.CameraPosition = evt.CameraPos;
                ctx.CameraTarget   = evt.CameraTarget;
                ctx.CameraDistance = evt.CameraDistance;
                ctx.PreviewRect    = rect;

                // 修飾キーを記録（Event.current 依存を局所化）
                _shiftHeld = e.shift;
                _ctrlHeld  = e.control;

                var mousePos = e.mousePosition;

                if (!rect.Contains(mousePos) && _inp.EditState == VertexEditState.Idle) return;

                // 右ドラッグ: カメラ回転（ViewportCore は OnHandleInput 設定時に委譲する）
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
                        _owner._viewport.RotY += e.delta.x * 0.5f;
                        _owner._viewport.RotX += e.delta.y * 0.5f;
                        _owner._viewport.RotX  = Mathf.Clamp(_owner._viewport.RotX, -89f, 89f);
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

                // スクロール: ズーム
                if (e.type == EventType.ScrollWheel && rect.Contains(mousePos))
                {
                    float delta = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
                    _owner._viewport.Distance *= 1f + delta * 0.05f;
                    _owner._viewport.Distance  = Mathf.Clamp(_owner._viewport.Distance, 0.05f, 100f);
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

                // ツール先行処理
                bool toolHandled = tool?.OnMouseDown(ctx, mousePos) ?? false;

                if (!toolHandled)
                {
                    // 頂点ヒットテスト（クリック選択）
                    var mc = _model.FirstSelectedMeshContext;
                    if (mc?.MeshObject != null)
                    {
                        int vi = ctx.FindVertexAtScreenPos(mousePos, mc.MeshObject,
                            rect, evt.CameraPos, evt.CameraTarget, ctx.HoverVertexRadius);
                        _inp.HitResultOnMouseDown = vi >= 0
                            ? new HitResult { HitType = MeshSelectMode.Vertex, VertexIndex = vi }
                            : HitResult.None;
                    }
                    // ツール再通知（選択後）
                    tool?.OnMouseDown(ctx, mousePos);
                }
            }

            private void OnMouseDrag(Vector2 mousePos, Vector2 delta, ToolContext ctx, IEditTool tool, Rect rect, ViewportEvent evt)
            {
                bool toolHandled = tool?.OnMouseDrag(ctx, mousePos, delta) ?? false;

                if (!toolHandled)
                {
                    // ドラッグ閾値を超えたら矩形 or 投げ縄
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

                    if (_inp.EditState == VertexEditState.BoxSelecting)
                    {
                        FinishBoxSelect(mc, rect, evt);
                    }
                    else if (_inp.EditState == VertexEditState.LassoSelecting)
                    {
                        FinishLassoSelect(mc, rect, evt);
                    }
                    else if (_inp.EditState == VertexEditState.PendingAction)
                    {
                        // クリック選択
                        ApplyClickSelection(mc, evt);
                    }
                }

                ResetInputState();
                _owner._viewport?.SyncSelectionState();
                _owner._viewport?.RequestNormal();
                _owner.Repaint();
            }

            // ----------------------------------------------------------------
            // クリック選択
            // ----------------------------------------------------------------

            private void ApplyClickSelection(MeshContext mc, ViewportEvent evt)
            {
                bool shift = _shiftHeld, ctrl = _ctrlHeld;
                var  hit   = _inp.HitResultOnMouseDown;

                if (hit.HitType == MeshSelectMode.Vertex && hit.VertexIndex >= 0)
                {
                    if (ctrl)
                        mc.SelectedVertices.Remove(hit.VertexIndex);
                    else if (shift)
                        mc.SelectedVertices.Add(hit.VertexIndex);
                    else
                    {
                        mc.SelectedVertices.Clear();
                        mc.SelectedVertices.Add(hit.VertexIndex);
                    }
                }
                else if (!shift && !ctrl)
                {
                    // 空白クリック → 全解除
                    mc.ClearSelection();
                }
            }

            // ----------------------------------------------------------------
            // 矩形選択
            // ----------------------------------------------------------------

            private void FinishBoxSelect(MeshContext mc, Rect rect, ViewportEvent evt)
            {
                bool shift = _shiftHeld, ctrl = _ctrlHeld;
                bool additive = shift || ctrl;

                var selectRect = new Rect(
                    Mathf.Min(_inp.BoxSelectStart.x, _inp.BoxSelectEnd.x),
                    Mathf.Min(_inp.BoxSelectStart.y, _inp.BoxSelectEnd.y),
                    Mathf.Abs(_inp.BoxSelectEnd.x - _inp.BoxSelectStart.x),
                    Mathf.Abs(_inp.BoxSelectEnd.y - _inp.BoxSelectStart.y));

                if (!additive) mc.SelectedVertices.Clear();

                var ctx     = _owner._toolContext;
                var adapter = _owner._viewport?.Adapter;

                // GPU Readback（カリング用）
                adapter?.ReadBackVertexFlags();

                int meshIdx = _model.FirstSelectedIndex;

                for (int i = 0; i < mc.MeshObject.VertexCount; i++)
                {
                    // カリングチェック
                    if (_owner._backfaceCulling && adapter != null &&
                        adapter.IsVertexCulled(meshIdx, i)) continue;

                    Vector2 sp = ctx.WorldToScreenPos(mc.MeshObject.Vertices[i].Position,
                        rect, evt.CameraPos, evt.CameraTarget);

                    if (selectRect.Contains(sp))
                    {
                        if (ctrl) mc.SelectedVertices.Remove(i);
                        else      mc.SelectedVertices.Add(i);
                    }
                }
            }

            // ----------------------------------------------------------------
            // 投げ縄選択
            // ----------------------------------------------------------------

            private void FinishLassoSelect(MeshContext mc, Rect rect, ViewportEvent evt)
            {
                if (_inp.LassoPoints.Count < 3) return;

                bool additive = _shiftHeld || _ctrlHeld;
                bool ctrl     = _ctrlHeld;

                if (!additive) mc.SelectedVertices.Clear();

                var ctx     = _owner._toolContext;
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
                        else      mc.SelectedVertices.Add(i);
                    }
                }
            }

            private static bool IsPointInLasso(Vector2 point, List<Vector2> polygon)
            {
                if (polygon == null || polygon.Count < 3) return false;
                bool inside = false;
                int  count  = polygon.Count, j = count - 1;
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

            private void ResetInputState()
            {
                _inp.EditState = VertexEditState.Idle;
                _inp.LassoPoints.Clear();
                _inp.HitResultOnMouseDown = HitResult.None;
            }

            // ----------------------------------------------------------------
            // オーバーレイ描画（ViewportCore.OnDrawOverlay から呼ばれる）
            // ----------------------------------------------------------------

            public void DrawOverlay(ViewportEvent evt)
            {
                if (Event.current.type != EventType.Repaint) return;

                // 矩形選択の描画
                if (_inp.EditState == VertexEditState.BoxSelecting)
                {
                    DrawBoxSelectOverlay();
                    return;
                }

                // 投げ縄選択の描画
                if (_inp.EditState == VertexEditState.LassoSelecting && _inp.LassoPoints.Count > 1)
                {
                    DrawLassoOverlay();
                    return;
                }

                // ツールギズモ（MoveTool の軸ハンドル等）
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
                // 閉じる線（薄く）
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
