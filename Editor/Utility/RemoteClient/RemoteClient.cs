// RemoteClient/RemoteClient.cs
// PolyLing Remote Client — EditorWindow
// サーバーからプロジェクト全体(PLRP)を受信し、ProjectContextとして復元・表示する
//
// Poly_Ling依存: ProjectContext, ModelContext, MeshContext, RemoteProjectSerializer等を使用

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

        // リクエスト管理
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
        // GUI状態
        // ================================================================

        private Vector2 _treeScroll;
        private Vector2 _logScroll;
        private readonly HashSet<int> _expandedModels = new HashSet<int>();
        private int _selectedModelIndex = -1;
        private int _selectedMeshIndex = -1;

        private readonly List<string> _logMessages = new List<string>();
        private const int MaxLogLines = 30;

        // ================================================================
        // ウィンドウ
        // ================================================================

        [MenuItem("Window/PolyLing Remote Client")]
        public static void Open()
        {
            GetWindow<RemoteClient>("Remote Client");
        }

        private void OnEnable()
        {
            EditorApplication.update += ProcessMainThreadQueue;
        }

        private void OnDisable()
        {
            EditorApplication.update -= ProcessMainThreadQueue;
            Disconnect();
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
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.LabelField("PolyLing Remote Client", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawConnectionUI();
            EditorGUILayout.Space(4);
            DrawProjectSummary();
            EditorGUILayout.Space(4);
            DrawModelMeshTree();
            EditorGUILayout.Space(4);
            DrawMeshDetail();
            EditorGUILayout.Space(4);
            DrawLog();
        }

        // ================================================================
        // 接続UI
        // ================================================================

        private void DrawConnectionUI()
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_isConnected))
            {
                _host = EditorGUILayout.TextField("Host", _host);
                _port = EditorGUILayout.IntField("Port", _port, GUILayout.Width(80));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (!_isConnected)
            {
                if (GUILayout.Button("Connect", GUILayout.Width(100)))
                    Connect();
            }
            else
            {
                EditorGUILayout.LabelField("● Connected", EditorStyles.boldLabel,
                    GUILayout.Width(100));
                if (GUILayout.Button("Disconnect", GUILayout.Width(100)))
                    Disconnect();
            }

            using (new EditorGUI.DisabledScope(!_isConnected))
            {
                if (GUILayout.Button("Fetch Project", GUILayout.Width(110)))
                    FetchProject();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // プロジェクトサマリー
        // ================================================================

        private void DrawProjectSummary()
        {
            EditorGUILayout.LabelField("Project", EditorStyles.miniBoldLabel);

            if (_project == null)
            {
                EditorGUILayout.LabelField($"  Status: {_projectStatus}");
                return;
            }

            EditorGUILayout.LabelField($"  Name: {_project.Name}");
            EditorGUILayout.LabelField($"  Models: {_project.ModelCount}");
            EditorGUILayout.LabelField($"  Current: [{_project.CurrentModelIndex}] " +
                $"{_project.CurrentModel?.Name ?? "none"}");

            int totalMeshes = 0;
            int totalVertices = 0;
            int totalFaces = 0;
            foreach (var model in _project.Models)
            {
                totalMeshes += model.Count;
                foreach (var mc in model.MeshContextList)
                {
                    totalVertices += mc.VertexCount;
                    totalFaces += mc.FaceCount;
                }
            }
            EditorGUILayout.LabelField(
                $"  Total: {totalMeshes} meshes, {totalVertices:N0} verts, {totalFaces:N0} faces");
        }

        // ================================================================
        // モデル/メッシュツリー
        // ================================================================

        private void DrawModelMeshTree()
        {
            EditorGUILayout.LabelField("Model / Mesh Tree", EditorStyles.miniBoldLabel);

            if (_project == null) return;

            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll,
                GUILayout.MinHeight(150), GUILayout.MaxHeight(400));

            for (int mi = 0; mi < _project.ModelCount; mi++)
            {
                var model = _project.Models[mi];
                bool isCurrent = mi == _project.CurrentModelIndex;
                bool isExpanded = _expandedModels.Contains(mi);

                // モデル行
                EditorGUILayout.BeginHorizontal();

                var foldoutStyle = isCurrent ? EditorStyles.boldLabel : EditorStyles.label;
                string prefix = isCurrent ? "★ " : "  ";
                string modelLabel = $"{prefix}[{mi}] {model.Name} ({model.Count} meshes)";

                bool newExpanded = EditorGUILayout.Foldout(isExpanded, modelLabel, true);
                if (newExpanded != isExpanded)
                {
                    if (newExpanded) _expandedModels.Add(mi);
                    else _expandedModels.Remove(mi);
                }

                EditorGUILayout.EndHorizontal();

                // メッシュリスト
                if (isExpanded)
                {
                    EditorGUI.indentLevel++;
                    for (int si = 0; si < model.Count; si++)
                    {
                        var mc = model.MeshContextList[si];
                        DrawMeshRow(mi, si, mc);
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawMeshRow(int modelIndex, int meshIndex, MeshContext mc)
        {
            bool isSelected = _selectedModelIndex == modelIndex && _selectedMeshIndex == meshIndex;
            int indent = mc.Depth * 12;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent);

            // 選択ボタン
            var bgColor = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);

            string visIcon = mc.IsVisible ? "●" : "○";
            string lockIcon = mc.IsLocked ? "🔒" : "";
            string typeStr = mc.Type != MeshType.Mesh ? $"[{mc.Type}]" : "";
            string label = $"{visIcon} {meshIndex}: {mc.Name} {typeStr}{lockIcon}";
            string detail = $"V:{mc.VertexCount} F:{mc.FaceCount}";

            if (GUILayout.Button(label, EditorStyles.miniButtonLeft, GUILayout.MinWidth(180)))
            {
                _selectedModelIndex = modelIndex;
                _selectedMeshIndex = meshIndex;
            }

            GUILayout.Label(detail, EditorStyles.miniLabel, GUILayout.Width(100));
            GUI.backgroundColor = bgColor;

            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // メッシュ詳細
        // ================================================================

        private void DrawMeshDetail()
        {
            if (_project == null || _selectedModelIndex < 0 || _selectedMeshIndex < 0)
                return;

            if (_selectedModelIndex >= _project.ModelCount)
                return;

            var model = _project.Models[_selectedModelIndex];
            if (_selectedMeshIndex >= model.Count)
                return;

            var mc = model.MeshContextList[_selectedMeshIndex];

            EditorGUILayout.LabelField("Mesh Detail", EditorStyles.miniBoldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Name", mc.Name);
                EditorGUILayout.EnumPopup("Type", mc.Type);
                EditorGUILayout.Toggle("Visible", mc.IsVisible);
                EditorGUILayout.Toggle("Locked", mc.IsLocked);
                EditorGUILayout.IntField("Vertices", mc.VertexCount);
                EditorGUILayout.IntField("Faces", mc.FaceCount);
                EditorGUILayout.IntField("Depth", mc.Depth);
                EditorGUILayout.IntField("Parent", mc.ParentIndex);
                EditorGUILayout.IntField("MirrorType", mc.MirrorType);
                EditorGUILayout.Toggle("ExcludeExport", mc.ExcludeFromExport);

                if (mc.IsMorph)
                {
                    EditorGUILayout.LabelField("Morph", EditorStyles.miniBoldLabel);
                    EditorGUILayout.TextField("MorphName", mc.MorphName);
                    EditorGUILayout.IntField("MorphPanel", mc.MorphPanel);
                    EditorGUILayout.IntField("MorphParent", mc.MorphParentIndex);
                }

                if (mc.BoneTransform != null)
                {
                    EditorGUILayout.LabelField("Bone", EditorStyles.miniBoldLabel);
                    EditorGUILayout.Vector3Field("Position", mc.BoneTransform.Position);
                    EditorGUILayout.Vector3Field("Rotation", mc.BoneTransform.Rotation);
                    EditorGUILayout.Vector3Field("Scale", mc.BoneTransform.Scale);
                }

                if (mc.IsIK)
                {
                    EditorGUILayout.LabelField("IK", EditorStyles.miniBoldLabel);
                    EditorGUILayout.IntField("IKTarget", mc.IKTargetIndex);
                    EditorGUILayout.IntField("IKLoopCount", mc.IKLoopCount);
                    EditorGUILayout.FloatField("IKLimitAngle", mc.IKLimitAngle);
                }

                // BindPose/WorldMatrix表示
                EditorGUILayout.LabelField("WorldMatrix", mc.WorldMatrix.ToString());
            }
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
                        Log($"接続成功: {_host}:{_port}");
                        Repaint();
                    }
                    else
                    {
                        Log("接続失敗");
                        Repaint();
                    }
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

            if (type == "push")
            {
                string eventName = ExtractJsonString(json, "event");
                Log($"Push: {eventName}");
                return;
            }

            // binaryCallbackがあれば直後のバイナリ待ち
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
            // リクエストと紐づけ
            if (_lastTextResponseId != null &&
                _binaryCallbacks.TryGetValue(_lastTextResponseId, out var cb))
            {
                _binaryCallbacks.Remove(_lastTextResponseId);
                cb(_lastTextResponseJson, data);
                _lastTextResponseId = null;
                _lastTextResponseJson = null;
                return;
            }

            // 紐づけなし → マジックで判定
            uint magic = RemoteMagic.Read(data);
            if (magic == RemoteMagic.Project)
            {
                ProcessProjectBinary(data);
            }
            else
            {
                Log($"バイナリ受信（未紐づけ）: {data.Length}B magic=0x{magic:X8}");
            }
        }

        // ================================================================
        // プロジェクト受信
        // ================================================================

        private void FetchProject()
        {
            string id = NextId();
            string json = "{" +
                $"\"id\":\"{id}\"," +
                "\"type\":\"query\"," +
                "\"target\":\"project\"" +
            "}";

            _projectStatus = "受信中...";
            Repaint();

            SendBinaryQuery(json, (textResp, binaryData) =>
            {
                ProcessProjectBinary(binaryData);
            });

            Log("project クエリ送信");
        }

        private void ProcessProjectBinary(byte[] data)
        {
            if (data == null || data.Length < 8)
            {
                _projectStatus = "受信エラー: データなし";
                Log("プロジェクトデータなし");
                Repaint();
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            _project = RemoteProjectSerializer.Deserialize(data);
            sw.Stop();

            if (_project != null)
            {
                _projectStatus = $"受信完了 ({FormatBytes(data.Length)}, {sw.ElapsedMilliseconds}ms)";
                _expandedModels.Clear();
                for (int i = 0; i < _project.ModelCount; i++)
                    _expandedModels.Add(i);

                _selectedModelIndex = _project.CurrentModelIndex;
                _selectedMeshIndex = -1;

                int totalV = 0, totalF = 0;
                foreach (var m in _project.Models)
                    foreach (var mc in m.MeshContextList)
                    {
                        totalV += mc.VertexCount;
                        totalF += mc.FaceCount;
                    }

                Log($"プロジェクト受信: \"{_project.Name}\" " +
                    $"{_project.ModelCount}モデル V={totalV:N0} F={totalF:N0} " +
                    $"({FormatBytes(data.Length)}, {sw.ElapsedMilliseconds}ms)");
            }
            else
            {
                _projectStatus = "デシリアライズ失敗";
                Log("プロジェクトのデシリアライズ失敗");
            }

            Repaint();
        }

        // ================================================================
        // リクエスト送信
        // ================================================================

        private string NextId() => $"c{++_requestId}";

        private void SendBinaryQuery(string json, Action<string, byte[]> onResponse)
        {
            string id = ExtractJsonString(json, "id");
            if (id != null)
                _binaryCallbacks[id] = onResponse;
            _ = _ws.SendTextAsync(json);
        }

        // ================================================================
        // 簡易JSONヘルパー
        // ================================================================

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

        // ================================================================
        // ヘルパー
        // ================================================================

        private static string FormatBytes(int bytes)
        {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
            return $"{bytes / (1024.0 * 1024.0):F1}MB";
        }

        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _logMessages.Add(line);
            while (_logMessages.Count > MaxLogLines)
                _logMessages.RemoveAt(0);
        }

        // ================================================================
        // ログ
        // ================================================================

        private void DrawLog()
        {
            EditorGUILayout.LabelField("Log", EditorStyles.miniBoldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(80));
            foreach (var msg in _logMessages)
                EditorGUILayout.LabelField(msg, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Clear Log", GUILayout.Width(80)))
                _logMessages.Clear();
        }
    }
}
