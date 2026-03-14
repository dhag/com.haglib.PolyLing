// RemoteClient/RemoteClient.cs
// PolyLing Remote Client — EditorWindow
// サーバーからプロジェクト全体(PLRP)を受信し、左ペイン=ツリー、右ペイン=3Dビューポートで表示

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PolyLingRemoteClient;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Remote;

namespace Poly_Ling.Remote
{
    public class RemoteClient : EditorWindow
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
        private readonly Dictionary<string, Action<string>> _textCallbacks
            = new Dictionary<string, Action<string>>();
        private readonly Dictionary<string, Action<string, byte[]>> _binaryCallbacks
            = new Dictionary<string, Action<string, byte[]>>();
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

        private RemoteViewportCore _viewport;

        // ================================================================
        // GUI状態
        // ================================================================

        private Vector2 _treeScroll;
        private Vector2 _logScroll;
        private readonly HashSet<int> _expandedModels = new HashSet<int>();
        private int _selectedModelIndex = -1;
        private int _selectedMeshIndex = -1;

        private readonly List<string> _logMessages = new List<string>();
        private const int MaxLogLines = 30;

        // レイアウト
        private float _splitX = 260f;
        private bool _draggingSplit;

        // ================================================================
        // ウィンドウ
        // ================================================================

        [MenuItem("Tools/Remote Client for PolyLing ")]
        public static void Open()
        {
            GetWindow<RemoteClient>("Remote Client");
        }

        private void OnEnable()
        {
            EditorApplication.update += ProcessMainThreadQueue;
            _viewport = new RemoteViewportCore();
            _viewport.RequestRepaint = Repaint;
            _viewport.Init();
        }

        private void OnDisable()
        {
            EditorApplication.update -= ProcessMainThreadQueue;
            Disconnect();
            _viewport?.Dispose();
            _viewport = null;
        }

        private void ProcessMainThreadQueue()
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
        // GUI
        // ================================================================

        private void OnGUI()
        {
            float totalW = position.width;
            float totalH = position.height;

            // ドラッグでスプリッタ移動
            HandleSplitDrag(totalH);

            float leftW = Mathf.Clamp(_splitX, 180f, totalW - 100f);
            float rightW = totalW - leftW - 4f;

            // 左ペイン
            GUILayout.BeginArea(new Rect(0, 0, leftW, totalH));
            DrawLeftPane(leftW, totalH);
            GUILayout.EndArea();

            // スプリッタ
            var splitRect = new Rect(leftW, 0, 4f, totalH);
            EditorGUI.DrawRect(splitRect, new Color(0.2f, 0.2f, 0.2f));
            EditorGUIUtility.AddCursorRect(splitRect, MouseCursor.ResizeHorizontal);

            // 右ペイン（3Dビューポート）
            GUILayout.BeginArea(new Rect(leftW + 4f, 0, rightW, totalH));
            DrawViewport(new Rect(0, 0, rightW, totalH));
            GUILayout.EndArea();
        }

        private void HandleSplitDrag(float totalH)
        {
            float leftW = Mathf.Clamp(_splitX, 180f, position.width - 100f);
            var splitRect = new Rect(leftW, 0, 6f, totalH);
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;

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
            EditorGUILayout.LabelField("Remote Client", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            DrawConnectionUI();
            EditorGUILayout.Space(2);
            DrawProjectSummary();
            EditorGUILayout.Space(2);

            float treeH = Mathf.Max(100f, h - 340f);
            DrawModelMeshTree(treeH);

            EditorGUILayout.Space(2);
            DrawLog();
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
                var style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                style.alignment = TextAnchor.MiddleCenter;
                GUI.Label(rect, _project == null ? "プロジェクト未受信" : "モデルを選択してください", style);
                return;
            }

            // IMGUIのRect描画
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));

            _viewport.Draw(rect, model);

            // モデル名オーバーレイ
            if (Event.current.type == EventType.Repaint)
            {
                var labelStyle = new GUIStyle(EditorStyles.miniLabel);
                labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
                GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, 16),
                    $"[{_selectedModelIndex}] {model.Name}", labelStyle);
            }
        }

        private ModelContext GetSelectedModel()
        {
            if (_project == null || _selectedModelIndex < 0 || _selectedModelIndex >= _project.ModelCount)
                return null;
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
                if (GUILayout.Button("Connect", GUILayout.Width(80)))
                    Connect();
            }
            else
            {
                EditorGUILayout.LabelField("● Connected", EditorStyles.boldLabel, GUILayout.Width(90));
                if (GUILayout.Button("Cut", GUILayout.Width(40)))
                    Disconnect();
            }

            using (new EditorGUI.DisabledScope(!_isConnected))
            {
                if (GUILayout.Button("Fetch Project"))
                    FetchProjectHeader();
            }
            EditorGUILayout.EndHorizontal();

            // メッシュ単体取得ボタン
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!_isConnected || _project == null))
            {
                if (GUILayout.Button("Fetch Model Meta", GUILayout.Width(120)))
                    FetchModelMeta(_selectedModelIndex >= 0 ? _selectedModelIndex : 0);
                if (GUILayout.Button("Fetch Mesh Data", GUILayout.Width(120)))
                {
                    if (_selectedModelIndex >= 0 && _selectedMeshIndex >= 0)
                        FetchMeshData(_selectedModelIndex, _selectedMeshIndex);
                    else
                        Log("Fetch Mesh Data: モデルとメッシュを選択してください");
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // プロジェクトサマリー
        // ================================================================

        private void DrawProjectSummary()
        {
            if (_project == null)
            {
                EditorGUILayout.LabelField(_projectStatus, EditorStyles.miniLabel);
                return;
            }

            int totalV = 0, totalF = 0;
            foreach (var m in _project.Models)
                foreach (var mc in m.MeshContextList) { totalV += mc.VertexCount; totalF += mc.FaceCount; }

            EditorGUILayout.LabelField(
                $"{_project.Name}  {_project.ModelCount}M  V:{totalV:N0} F:{totalF:N0}",
                EditorStyles.miniLabel);
        }

        // ================================================================
        // モデル/メッシュツリー
        // ================================================================

        private void DrawModelMeshTree(float height)
        {
            if (_project == null) return;

            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll,
                GUILayout.Height(height));

            for (int mi = 0; mi < _project.ModelCount; mi++)
            {
                var model = _project.Models[mi];
                bool isCurrent = mi == _project.CurrentModelIndex;
                bool isExpanded = _expandedModels.Contains(mi);

                EditorGUILayout.BeginHorizontal();
                bool isModelSelected = _selectedModelIndex == mi;
                var bg = GUI.backgroundColor;
                if (isModelSelected) GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);

                string prefix = isCurrent ? "★ " : "  ";
                string modelLabel = $"{prefix}[{mi}] {model.Name}";
                bool newExp = EditorGUILayout.Foldout(isExpanded, modelLabel, true);
                if (newExp != isExpanded)
                {
                    if (newExp) _expandedModels.Add(mi); else _expandedModels.Remove(mi);
                }

                // モデル選択ボタン
                if (GUILayout.Button("▶", GUILayout.Width(22)))
                {
                    _selectedModelIndex = mi;
                    _selectedMeshIndex = -1;
                    Repaint();
                }
                GUI.backgroundColor = bg;
                EditorGUILayout.EndHorizontal();

                if (isExpanded)
                {
                    EditorGUI.indentLevel++;
                    foreach (var entry in model.DrawableMeshes)
                        DrawMeshRow(mi, entry.MasterIndex, entry.Context);
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawMeshRow(int modelIndex, int meshIndex, MeshContext mc)
        {
            bool isSelected = _selectedModelIndex == modelIndex && _selectedMeshIndex == meshIndex;
            int indent = mc.Depth * 8;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent);

            var bg = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);

            string visIcon = mc.IsVisible ? "●" : "○";
            string label = $"{visIcon} {meshIndex}: {mc.Name}";

            if (GUILayout.Button(label, EditorStyles.miniButtonLeft))
            {
                _selectedModelIndex = modelIndex;
                _selectedMeshIndex = meshIndex;
                Repaint();
            }

            GUILayout.Label($"V:{mc.VertexCount}", EditorStyles.miniLabel, GUILayout.Width(50));
            GUI.backgroundColor = bg;
            EditorGUILayout.EndHorizontal();
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
                    if (ok) { _isConnected = true; Log($"接続: {_host}:{_port}"); }
                    else Log("接続失敗");
                    Repaint();
                });

                if (ok)
                    await ReceiveLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    Log($"接続エラー: {ex.Message}");
                    _isConnected = false;
                    Repaint();
                });
            }
        }

        private void Disconnect()
        {
            _cts?.Cancel();
            _ws?.Close();
            _ws = null;
            _isConnected = false;
            _textCallbacks.Clear();
            _binaryCallbacks.Clear();
            Log("切断");
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
                    if (f.Type == WsFrameType.Text)
                        _mainThreadQueue.Enqueue(() => HandleTextMessage(f.Text));
                    else if (f.Type == WsFrameType.Binary)
                        _mainThreadQueue.Enqueue(() => HandleBinaryMessage(f.Binary));
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

            if (id != null && _binaryCallbacks.ContainsKey(id))
            {
                _lastTextResponseId = id;
                _lastTextResponseJson = json;
                return;
            }

            if (id != null && _textCallbacks.TryGetValue(id, out var cb))
            {
                _textCallbacks.Remove(id);
                cb(json);
            }
        }

        private void HandleBinaryMessage(byte[] data)
        {
            if (_lastTextResponseId != null &&
                _binaryCallbacks.TryGetValue(_lastTextResponseId, out var cb))
            {
                _binaryCallbacks.Remove(_lastTextResponseId);
                cb(_lastTextResponseJson, data);
                _lastTextResponseId = null;
                _lastTextResponseJson = null;
                return;
            }

            uint magic = Poly_Ling.Remote.RemoteMagic.Read(data);
            if (magic == Poly_Ling.Remote.RemoteMagic.Batch)
            {
                DispatchBatch(data);
                return;
            }
            DispatchFrame(magic, data);
        }

        private void DispatchBatch(byte[] data)
        {
            // [4B Magic][1B Version][3B padding][4B FrameCount]{ [4B FrameLen][FrameData] }×N
            if (data.Length < 12) { Log($"PLRB: データ短すぎ {data.Length}B"); return; }
            int frameCount = (int)System.BitConverter.ToUInt32(data, 8);
            Log($"PLRB: 展開開始 frameCount={frameCount} totalSize={data.Length}B");
            int offset = 12;
            int dispatched = 0;
            for (int i = 0; i < frameCount; i++)
            {
                if (offset + 4 > data.Length) { Log($"PLRB: フレーム長読み取り失敗 i={i} offset={offset}"); break; }
                int frameLen = (int)System.BitConverter.ToUInt32(data, offset);
                offset += 4;
                if (offset + frameLen > data.Length) { Log($"PLRB: フレームデータ不足 i={i} frameLen={frameLen} remain={data.Length - offset}"); break; }
                byte[] frame = new byte[frameLen];
                System.Array.Copy(data, offset, frame, 0, frameLen);
                offset += frameLen;
                DispatchFrame(Poly_Ling.Remote.RemoteMagic.Read(frame), frame);
                dispatched++;
            }
            Log($"PLRB: 展開完了 dispatched={dispatched}/{frameCount}");
        }

        private void DispatchFrame(uint magic, byte[] data)
        {
            if      (magic == Poly_Ling.Remote.RemoteMagic.ProjectHeader) ReceiveProjectHeader(data);
            else if (magic == Poly_Ling.Remote.RemoteMagic.ModelMeta)     ReceiveModelMeta(data);
            else if (magic == Poly_Ling.Remote.RemoteMagic.MeshSummary)   ReceiveMeshSummary(data);
            else if (magic == Poly_Ling.Remote.RemoteMagic.MeshData)      ReceiveMeshData(data);
            else if (magic == Poly_Ling.Remote.RemoteMagic.Image)         Log($"画像受信: {data.Length}B");
            else Log($"未知バイナリ: magic=0x{magic:X8} {data.Length}B");
        }

        // ================================================================
        // プログレッシブプロトコル 受信ハンドラ
        // ================================================================

        private void ReceiveProjectHeader(byte[] data)
        {
            var result = RemoteProgressiveSerializer.DeserializeProjectHeader(data);
            if (result == null) { Log("PLRH: デシリアライズ失敗"); return; }
            var (name, modelCount, currentModelIndex) = result.Value;

            // ProjectContext を初期化（モデルスロットを空で確保）
            _project = new ProjectContext { Name = name };
            for (int i = 0; i < modelCount; i++)
                _project.Models.Add(new ModelContext($"Model{i}"));
            _project.CurrentModelIndex = currentModelIndex;

            _selectedModelIndex = currentModelIndex;
            _selectedMeshIndex = -1;
            _expandedModels.Clear();
            for (int i = 0; i < modelCount; i++) _expandedModels.Add(i);

            _projectStatus = $"受信中... ({name} {modelCount}モデル)";
            Log($"PLRH受信: \"{name}\" {modelCount}モデル");
        }

        private void ReceiveModelMeta(byte[] data)
        {
            var result = RemoteProgressiveSerializer.DeserializeModelMeta(data);
            if (result == null) { Log("PLRM: デシリアライズ失敗"); return; }
            var (modelIndex, model) = result.Value;

            if (_project == null) { Log($"PLRM: プロジェクト未受信 [{modelIndex}]"); return; }
            while (_project.Models.Count <= modelIndex)
                _project.Models.Add(new ModelContext($"Model{_project.Models.Count}"));

            _project.Models[modelIndex] = model;
            Log($"PLRM受信: [{modelIndex}] \"{model.Name}\" meshes={model.Count}");
        }

        private void ReceiveMeshSummary(byte[] data)
        {
            var result = RemoteProgressiveSerializer.DeserializeMeshSummary(data);
            if (result == null) { Log("PLRS: デシリアライズ失敗"); return; }
            var (modelIndex, meshIndex, mc, vertexCount, faceCount) = result.Value;

            if (_project == null) { Log($"PLRS: プロジェクト未受信 [{modelIndex}][{meshIndex}]"); return; }
            if (modelIndex >= _project.ModelCount) { Log($"PLRS: modelIndex={modelIndex} out of range"); return; }

            var model = _project.Models[modelIndex];
            while (model.MeshContextList.Count <= meshIndex)
                model.MeshContextList.Add(new MeshContext { Name = $"Mesh{model.MeshContextList.Count}" });

            model.MeshContextList[meshIndex] = mc;
            model.InvalidateTypedIndices();

            // 最初の3件と最後の1件だけログ
            if (meshIndex < 3 || meshIndex == model.Count - 1)
                Log($"PLRS[{modelIndex}][{meshIndex}] name={mc.Name} type={mc.Type}");

            // 全メッシュサマリ受信完了（最後のスロットで1回だけ判定）
            if (meshIndex == model.Count - 1)
            {
                int drawCount = model.DrawableMeshes.Count;
                Log($"PLRS完了: total={model.Count} drawable={drawCount} サンプル名=[0]{model.MeshContextList[0].Name} [1]{(model.Count>1?model.MeshContextList[1].Name:"-")}");
                _projectStatus = $"OK ({_project.Name})";
            }

            Repaint();
        }

        private void ReceiveMeshData(byte[] data)
        {
            var result = RemoteProgressiveSerializer.DeserializeMeshData(data);
            if (result == null) { Log("PLRD: デシリアライズ失敗"); return; }
            var (modelIndex, meshIndex, mesh) = result.Value;

            if (_project == null) { Log($"PLRD: プロジェクト未受信"); return; }
            if (modelIndex >= _project.ModelCount) { Log($"PLRD: modelIndex={modelIndex} out of range"); return; }

            var model = _project.Models[modelIndex];
            if (meshIndex >= model.Count) { Log($"PLRD: meshIndex={meshIndex} out of range"); return; }

            var mc = model.MeshContextList[meshIndex];
            if (mc.UnityMesh != null) UnityEngine.Object.DestroyImmediate(mc.UnityMesh);

            // PLRSで設定したName/Typeを退避し、PLRDのMeshObjectで上書き後に復元
            string savedName = mc.Name;
            MeshType savedType = mc.Type;
            mc.MeshObject = mesh;
            if (mesh != null)
            {
                mesh.Name = savedName;
                mesh.Type = savedType;
            }
            if (mesh != null && mesh.VertexCount > 0)
                mc.UnityMesh = mesh.ToUnityMesh();

            Log($"PLRD受信: [{modelIndex}][{meshIndex}] \"{mc.Name}\" V={mesh?.VertexCount ?? 0}");
            Repaint();
        }

        // ================================================================
        // クエリ送信
        // ================================================================

        private void FetchProjectHeader()
        {
            string id = NextId();
            string json = $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"project_header\"}}";
            _projectStatus = "受信中...";
            Repaint();
            SendBinaryQuery(json, (_, binaryData) =>
            {
                HandleBinaryMessage(binaryData);
                // 全モデルに対してボーン→Drawable→モーフの順でバッチフェッチ
                if (_project != null)
                    FetchAllModelsBatch(0);
            });
            Log("project_header クエリ送信");
        }

        /// <summary>
        /// 全モデルをボーン→Drawable→モーフの順で再帰的にフェッチ
        /// </summary>
        private void FetchAllModelsBatch(int modelIndex)
        {
            if (_project == null || modelIndex >= _project.ModelCount) return;

            FetchMeshDataBatch(modelIndex, "bone", () =>
                FetchMeshDataBatch(modelIndex, "drawable", () =>
                {
                    if (modelIndex == _project.CurrentModelIndex)
                    {
                        _projectStatus = $"OK ({_project?.Name})";
                        Repaint();
                    }
                    FetchMeshDataBatch(modelIndex, "morph", () =>
                    {
                        // このモデル完了→次のモデルへ
                        int next = modelIndex + 1;
                        if (next < (_project?.ModelCount ?? 0))
                            FetchAllModelsBatch(next);
                        else
                            Repaint(); // 全モデル完了
                    });
                }));
        }

        private void FetchMeshDataBatch(int modelIndex, string category, System.Action onComplete = null)
        {
            string id = NextId();
            string json = $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"mesh_data_batch\"," +
                          $"\"params\":{{\"modelIndex\":\"{modelIndex}\",\"category\":\"{category}\"}}}}";
            SendBinaryQuery(json, (_, binaryData) =>
            {
                if (binaryData != null && binaryData.Length >= 4)
                    HandleBinaryMessage(binaryData);
                onComplete?.Invoke();
            });
            Log($"mesh_data_batch クエリ送信 [{modelIndex}] {category}");
        }

        private void FetchModelMeta(int modelIndex)
        {
            string id = NextId();
            string json = $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"model_meta\"," +
                          $"\"params\":{{\"modelIndex\":\"{modelIndex}\"}}}}";
            SendBinaryQuery(json, (_, binaryData) => HandleBinaryMessage(binaryData));
            Log($"model_meta クエリ送信 [{modelIndex}]");
        }

        private void FetchMeshData(int modelIndex, int meshIndex)
        {
            string id = NextId();
            string json = $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"mesh_data\"," +
                          $"\"params\":{{\"modelIndex\":\"{modelIndex}\",\"meshIndex\":\"{meshIndex}\"}}}}";
            SendBinaryQuery(json, (_, binaryData) => HandleBinaryMessage(binaryData));
            Log($"mesh_data クエリ送信 [{modelIndex}][{meshIndex}]");
        }

        // ================================================================
        // リクエスト送信
        // ================================================================

        private string NextId() => $"c{++_requestId}";

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
            foreach (var msg in _logMessages)
                EditorGUILayout.LabelField(msg, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Clear Log", GUILayout.Width(72)))
                _logMessages.Clear();
        }

        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _logMessages.Add(line);
            while (_logMessages.Count > MaxLogLines)
                _logMessages.RemoveAt(0);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static string FormatBytes(int bytes)
        {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
            return $"{bytes / (1024.0 * 1024.0):F1}MB";
        }

        private static string ExtractJsonString(string json, string key)
        {
            string search = $"\"{key}\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx + search.Length);
            if (colon < 0) return null;
            int valStart = colon + 1;
            while (valStart < json.Length && json[valStart] == ' ') valStart++;
            if (valStart >= json.Length || json[valStart] != '"') return null;
            int valEnd = json.IndexOf('"', valStart + 1);
            if (valEnd < 0) return null;
            return json.Substring(valStart + 1, valEnd - valStart - 1);
        }
    }
}
