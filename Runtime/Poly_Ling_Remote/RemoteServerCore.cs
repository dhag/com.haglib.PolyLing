// Remote/RemoteServerCore.cs
// WebSocketサーバーのコアロジック。UnityEditor非依存。
// EditorWindow（RemoteServer）またはスタンドアロンアプリからホストされる。
//
// 使用方法:
//   var core = new RemoteServerCore(() => toolContext, port: 8765);
//   core.OnLog     = msg => Debug.Log(msg);
//   core.OnRepaint = () => editorWindow.Repaint();   // または独自UIの更新
//   core.Start();
//   // ゲームループ/EditorApplication.updateから毎フレーム呼ぶ
//   core.Tick();

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using HagLib.NET.Duplex;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Remote
{
    /// <summary>
    /// WebSocketサーバーコア。EditorWindow非依存。
    /// スタンドアロン化の際はこのクラスをそのまま使用できる。
    /// </summary>
    public class RemoteServerCore
    {
        // ================================================================
        // 設定・状態
        // ================================================================

        public int  Port      { get; set; }
        public bool IsRunning { get; private set; }

        public int ClientCount => _wsServer?.Clients.Length ?? 0;

        // ================================================================
        // コールバック（ホスト側が設定）
        // ================================================================

        /// <summary>ログ出力コールバック。nullなら無視。</summary>
        public Action<string> OnLog;

        /// <summary>UI再描画要求コールバック（EditorWindow.Repaint等）。</summary>
        public Action OnRepaint;

        /// <summary>
        /// PanelCommandディスパッチコールバック。
        /// PanelContext.SendCommand を渡すとDispatchPanelCommandを通じて全コマンドが処理される。
        /// nullの場合はlegacyの直接ToolContext操作にフォールバックする。
        /// </summary>
        public Action<PanelCommand> DispatchCommand;

        // ================================================================
        // コンテキスト注入
        // ================================================================

        private readonly Func<ToolContext> _contextProvider;

        private ToolContext Context => _contextProvider?.Invoke();

        // ================================================================
        // WebSocket（com.haglib.net_duplexchannel）
        // ================================================================

        private WebSocketDuplexServer        _wsServer;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        // Phase 1: Tick による毎フレームポーリング禁止のため、
        // 背景スレッドからのメインスレッドディスパッチは SynchronizationContext 経由で行う。
        // Start() でメインスレッドから呼ばれるタイミングでキャプチャする。
        private SynchronizationContext _syncCtx;

        // ================================================================
        // プッシュ／画像
        // ================================================================

        private ModelContext   _subscribedModel;
        private readonly List<ImageEntry> _capturedImages = new List<ImageEntry>();
        private ushort         _nextImageId;

        /// <summary>バッチ送信用：テキスト応答直後に送るバイナリフレーム（1回使い切り）</summary>
        private List<byte[]> _pendingBinaryResponses;

        // ================================================================
        // コンストラクタ
        // ================================================================

        /// <param name="contextProvider">ToolContextを返すデリゲート（毎回動的取得）</param>
        /// <param name="port">待ち受けポート番号（デフォルト8765）</param>
        public RemoteServerCore(Func<ToolContext> contextProvider, int port = 8765)
        {
            _contextProvider = contextProvider;
            Port = port;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        public void Start()
        {
            if (IsRunning) return;
            try
            {
                // メインスレッドから呼ばれる想定。
                // 背景スレッドからのディスパッチ用に UnitySynchronizationContext をキャプチャする。
                _syncCtx = SynchronizationContext.Current;

                _wsServer = new WebSocketDuplexServer
                {
                    // 既定はJSON(Text)。バイナリ送信時のみ kind=Binary を明示する。
                    DefaultFrame      = WebSocketFrameKind.Text,
                    // 非WSのHTTP GET(/)にはブラウザ用クライアントHTMLを返す。
                    IndexHtmlProvider = () => RemoteHtmlClient.GetHtml(Port),
                };
                _wsServer.OnReceived          += OnDuplexReceived;
                _wsServer.OnClientConnected    += _ => RunOnMainThread(() => { Log("クライアント接続"); OnRepaint?.Invoke(); });
                _wsServer.OnClientDisconnected += _ => RunOnMainThread(() => { Log("クライアント切断"); OnRepaint?.Invoke(); });

                IsRunning = true;
                SubscribeModel();
                _ = _wsServer.StartAsync($"http://localhost:{Port}/");

                Log($"サーバー起動: http://localhost:{Port}/");
            }
            catch (Exception ex)
            {
                Log($"起動失敗: {ex.Message}");
                IsRunning = false;
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            UnsubscribeModel();

            try { _ = _wsServer?.StopAsync(); } catch { }
            _wsServer = null;
            IsRunning = false;

            Log("サーバー停止");
        }

        /// <summary>
        /// メインスレッドキューを処理する。
        /// EditorApplication.update またはスタンドアロンのUpdate()から毎フレーム呼ぶ。
        /// </summary>
        public void Tick()
        {
            int processed = 0;
            while (_mainThreadQueue.TryDequeue(out var action) && processed < 10)
            {
                try { action(); }
                catch (Exception ex) { Log($"メインスレッドエラー: {ex.Message}"); }
                processed++;
            }
        }

        /// <summary>
        /// 背景スレッドからメインスレッドへ action を event 駆動でディスパッチする。
        /// SynchronizationContext が使えない場合はフォールバックとして _mainThreadQueue に積む。
        /// </summary>
        private void RunOnMainThread(Action action)
        {
            if (action == null) return;
            if (_syncCtx != null)
            {
                _syncCtx.Post(_ =>
                {
                    try { action(); }
                    catch (Exception ex) { Log($"メインスレッドエラー: {ex.Message}"); }
                }, null);
            }
            else
            {
                _mainThreadQueue.Enqueue(action);
            }
        }

        // ================================================================
        // 画像管理（Texture2D変換はホスト側で実施）
        // ================================================================

        /// <summary>
        /// 既にシリアライズ済みのImageEntryを送信リストに追加。
        /// Texture2D → ImageEntry 変換はRemoteServer（EditorWindow側）で行う。
        /// </summary>
        public void AddCapturedImageEntry(ImageEntry entry)
        {
            if (entry == null) return;
            _capturedImages.Add(entry);
            Log($"キャプチャ追加: ID={entry.Id} {entry.Width}x{entry.Height} ({entry.Data.Length}B)");
            OnRepaint?.Invoke();
        }

        public List<ImageEntry> CapturedImages => _capturedImages;

        // ================================================================
        // 公開送信API
        // ================================================================

        public void SendProjectHeader()
        {
            var project = GetProjectContext();
            if (project == null) { Log("プロジェクトなし"); return; }

            var frames = new List<byte[]>();
            var header = RemoteProgressiveSerializer.SerializeProjectHeader(project);
            if (header != null) frames.Add(header);

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                var mm = RemoteProgressiveSerializer.SerializeModelMeta(model, mi);
                if (mm != null) frames.Add(mm);
                for (int si = 0; si < model.Count; si++)
                {
                    var ms = RemoteProgressiveSerializer.SerializeMeshSummary(
                        model.MeshContextList[si], mi, si);
                    if (ms != null) frames.Add(ms);
                }
            }

            foreach (var f in frames) BroadcastBinaryAsync(f);
            Log($"プロジェクトヘッダ送信: {frames.Count}フレーム");
        }

        public void SendCapturedImages()
        {
            if (_capturedImages.Count == 0) return;
            byte[] data = RemoteImageSerializer.Serialize(_capturedImages);
            if (data != null)
            {
                BroadcastBinaryAsync(data);
                Log($"キャプチャ画像送信: {_capturedImages.Count}枚 ({data.Length}B)");
            }
        }

        public void ClearCapturedImages()
        {
            _capturedImages.Clear();
            Log("キャプチャリストクリア");
        }

        private byte[] ProcessBinaryMessage(byte[] data)
        {
            var header = RemoteBinarySerializer.ReadHeader(data);
            if (header == null) { Log("バイナリ: 無効なヘッダ"); return null; }

            var h = header.Value;
            Log($"バイナリ受信: type={h.MessageType} flags={h.FieldFlags} V={h.VertexCount} F={h.FaceCount}");

            switch (h.MessageType)
            {
                case BinaryMessageType.MeshData:
                {
                    var meshObject = RemoteBinarySerializer.Deserialize(data);
                    if (meshObject != null && Context != null)
                    {
                        Context.CreateNewMeshContext?.Invoke(meshObject, "RemoteMesh");
                        Context.Repaint?.Invoke();
                        Log($"メッシュ作成: V={meshObject.VertexCount} F={meshObject.FaceCount}");
                    }
                    return null;
                }
                case BinaryMessageType.PositionsOnly:
                {
                    if (Context?.FirstSelectedMeshObject != null)
                    {
                        RemoteBinarySerializer.Deserialize(data, Context.FirstSelectedMeshObject);
                        Context.SyncMesh?.Invoke();
                        Context.Repaint?.Invoke();
                        Log("位置更新適用");
                    }
                    return null;
                }
                case BinaryMessageType.RawFile:
                {
                    var (fileData, ext) = RemoteBinarySerializer.ExtractRawFile(data);
                    if (fileData != null)
                        Log($"ファイル受信: {ext} ({fileData.Length} bytes)");
                    return null;
                }
                default:
                    Log($"未知のバイナリタイプ: {h.MessageType}");
                    return null;
            }
        }

        // ================================================================
        // メッセージ処理（クエリ・コマンド）
        // ================================================================

        private string ProcessMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            RemoteMessage msg;
            try { msg = JsonParser.Parse(json); }
            catch (Exception ex)
            {
                Log($"パースエラー: {ex.Message}");
                return BuildErrorResponse(null, "Parse error");
            }

            Log($"受信: type={msg.Type} target={msg.Target} action={msg.Action}");
            Debug.Log($"受信: type={msg.Type} target={msg.Target} action={msg.Action}");

            if (msg.Type == "query")   return ProcessQuery(msg);
            if (msg.Type == "command") return ProcessCommand(msg);
            return BuildErrorResponse(msg.Id, $"Unknown type: {msg.Type}");
        }

        private string ProcessQuery(RemoteMessage msg)
        {
            switch (msg.Target)
            {
                case "meshList":
                    return BuildSuccessResponse(msg.Id,
                        RemoteDataProvider.QueryMeshList(Context, msg.Fields));

                case "meshData":
                    return BuildSuccessResponse(msg.Id,
                        RemoteDataProvider.QueryMeshData(Context, GetParamInt(msg, "index", 0), msg.Fields));

                case "modelInfo":
                    return BuildSuccessResponse(msg.Id,
                        RemoteDataProvider.QueryModelInfo(Context));

                case "availableFields":
                    return BuildSuccessResponse(msg.Id,
                        RemoteDataProvider.QueryAvailableFields());

                case "project_header":    return ProcessProjectHeaderQuery(msg);
                case "model_meta":        return ProcessModelMetaQuery(msg);
                case "mesh_data":         return ProcessMeshDataQuery(msg);
                case "mesh_data_batch":   return ProcessMeshDataBatchQuery(msg);

                default:
                    return BuildErrorResponse(msg.Id, $"Unknown target: {msg.Target}");
            }
        }

        private string ProcessProjectHeaderQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null) return BuildErrorResponse(msg.Id, "No project");

            var binaries = new List<byte[]>();
            byte[] header = RemoteProgressiveSerializer.SerializeProjectHeader(project);
            if (header == null) return BuildErrorResponse(msg.Id, "Serialize failed");
            binaries.Add(header);

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                var mm = RemoteProgressiveSerializer.SerializeModelMeta(model, mi);
                if (mm != null) binaries.Add(mm);
                for (int si = 0; si < model.Count; si++)
                {
                    var ms = RemoteProgressiveSerializer.SerializeMeshSummary(
                        model.MeshContextList[si], mi, si);
                    if (ms != null) binaries.Add(ms);
                }
            }

            _pendingBinaryResponses = binaries;

            int totalMeshes = 0;
            for (int mi = 0; mi < project.ModelCount; mi++) totalMeshes += project.Models[mi].Count;
            Log($"project_header: {project.ModelCount}モデル {totalMeshes}メッシュ ({binaries.Count}フレーム)");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("projectName", project.Name);
            jb.KeyValue("modelCount",  project.ModelCount);
            jb.KeyValue("meshCount",   totalMeshes);
            jb.KeyValue("frameCount",  binaries.Count);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        private string ProcessModelMetaQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null) return BuildErrorResponse(msg.Id, "No project");

            int modelIndex = GetParamInt(msg, "modelIndex", project.CurrentModelIndex);
            if (modelIndex < 0 || modelIndex >= project.ModelCount)
                return BuildErrorResponse(msg.Id, $"Invalid modelIndex: {modelIndex}");

            var model = project.Models[modelIndex];
            var binaries = new List<byte[]>();

            var mm = RemoteProgressiveSerializer.SerializeModelMeta(model, modelIndex);
            if (mm == null) return BuildErrorResponse(msg.Id, "Serialize failed");
            binaries.Add(mm);

            for (int si = 0; si < model.Count; si++)
            {
                var ms = RemoteProgressiveSerializer.SerializeMeshSummary(
                    model.MeshContextList[si], modelIndex, si);
                if (ms != null) binaries.Add(ms);
            }

            _pendingBinaryResponses = binaries;
            Log($"model_meta: [{modelIndex}] {model.Name} meshes={model.Count}");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex", modelIndex);
            jb.KeyValue("modelName",  model.Name);
            jb.KeyValue("meshCount",  model.Count);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        private string ProcessMeshDataQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null) return BuildErrorResponse(msg.Id, "No project");

            int modelIndex = GetParamInt(msg, "modelIndex", project.CurrentModelIndex);
            int meshIndex  = GetParamInt(msg, "meshIndex",  -1);

            if (modelIndex < 0 || modelIndex >= project.ModelCount)
                return BuildErrorResponse(msg.Id, $"Invalid modelIndex: {modelIndex}");

            var model = project.Models[modelIndex];
            if (meshIndex < 0 || meshIndex >= model.Count)
                return BuildErrorResponse(msg.Id, $"Invalid meshIndex: {meshIndex}");

            var mc       = model.MeshContextList[meshIndex];
            var flags    = (MeshFieldFlags)(uint)GetParamInt(msg, "flags", (int)MeshFieldFlags.All);
            var binData  = RemoteProgressiveSerializer.SerializeMeshData(mc, modelIndex, meshIndex, flags);
            if (binData == null) return BuildErrorResponse(msg.Id, "Serialize failed");

            _pendingBinaryResponses = new List<byte[]> { binData };
            Log($"mesh_data: [{modelIndex}][{meshIndex}] {mc.Name} V={mc.VertexCount} ({binData.Length}B)");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex",  modelIndex);
            jb.KeyValue("meshIndex",   meshIndex);
            jb.KeyValue("meshName",    mc.Name);
            jb.KeyValue("vertexCount", mc.VertexCount);
            jb.KeyValue("faceCount",   mc.FaceCount);
            jb.KeyValue("binarySize",  binData.Length);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        private string ProcessMeshDataBatchQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null) return BuildErrorResponse(msg.Id, "No project");

            int modelIndex = GetParamInt(msg, "modelIndex", project.CurrentModelIndex);
            if (modelIndex < 0 || modelIndex >= project.ModelCount)
                return BuildErrorResponse(msg.Id, $"Invalid modelIndex: {modelIndex}");

            var model    = project.Models[modelIndex];
            string category = GetParamString(msg, "category", "drawable");

            IReadOnlyList<TypedMeshEntry> entries;
            switch (category)
            {
                case "bone":  entries = model.Bones;  break;
                case "morph": entries = model.Morphs; break;
                case "all":   entries = model.TypedIndices.GetEntries(MeshCategory.All); break;
                default:      entries = model.DrawableMeshes; break;
            }

            var frames = new List<byte[]>();
            foreach (var entry in entries)
            {
                var mc = entry.Context;
                if (mc?.MeshObject == null || mc.MeshObject.VertexCount == 0) continue;
                var data = RemoteProgressiveSerializer.SerializeMeshData(
                    mc, modelIndex, entry.MasterIndex, MeshFieldFlags.All);
                if (data != null) frames.Add(data);
            }

            if (frames.Count == 0)
            {
                _pendingBinaryResponses = new List<byte[]> { BuildBatch(new List<byte[]>()) };
                Log($"mesh_data_batch: [{modelIndex}] {category} → 0件");
                var jbEmpty = new JsonBuilder();
                jbEmpty.BeginObject();
                jbEmpty.KeyValue("modelIndex", modelIndex);
                jbEmpty.KeyValue("category",   category);
                jbEmpty.KeyValue("meshCount",  0);
                jbEmpty.KeyValue("binarySize", 0);
                jbEmpty.EndObject();
                return BuildSuccessResponse(msg.Id, jbEmpty.ToString());
            }

            _pendingBinaryResponses = new List<byte[]> { BuildBatch(frames) };

            int totalBytes = frames.Sum(f => f.Length);
            Log($"mesh_data_batch: [{modelIndex}] {category} {frames.Count}件 ({totalBytes}B)");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex", modelIndex);
            jb.KeyValue("category",   category);
            jb.KeyValue("meshCount",  frames.Count);
            jb.KeyValue("binarySize", totalBytes);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        private string ProcessCommand(RemoteMessage msg)
        {
            try
            {
                // DispatchCommandが設定されている場合はPanelCommand経由で全処理
                if (DispatchCommand != null)
                    return ProcessCommandViaPanelCommand(msg);

                // フォールバック: ToolContext直接操作（後方互換）
                if (Context == null) return BuildErrorResponse(msg.Id, "No ToolContext");
                return ProcessCommandLegacy(msg);
            }
            catch (Exception ex)
            {
                Log($"コマンドエラー: {ex.Message}");
                return BuildErrorResponse(msg.Id, ex.Message);
            }
        }

        /// <summary>
        /// PanelCommand経由のコマンド処理。
        /// JSON → PanelCommand に変換してDispatchCommandに流す。
        /// DispatchPanelCommand（SummaryNotify）が実処理を担う。
        /// </summary>
        private string ProcessCommandViaPanelCommand(RemoteMessage msg)
        {
            int modelIndex = GetParamInt(msg, "modelIndex", 0);
            PanelCommand cmd = BuildPanelCommand(msg, modelIndex);
            if (cmd == null)
                return BuildErrorResponse(msg.Id, $"Unknown action: {msg.Action}");

            DispatchCommand(cmd);
            Log($"cmd: {msg.Action} model={modelIndex}");
            return BuildSuccessResponse(msg.Id, "true");
        }

        /// <summary>
        /// RemoteMessageからPanelCommandを組み立てる。
        /// 対応するコマンドがない場合はnullを返す。
        /// </summary>
        private static PanelCommand BuildPanelCommand(RemoteMessage msg, int modelIndex)
        {
            // int[]パラメータ取得ヘルパー（"1,2,3" 形式）
            int[] GetIndices(string key)
            {
                if (msg.Params == null || !msg.Params.TryGetValue(key, out var s) || string.IsNullOrEmpty(s))
                    return System.Array.Empty<int>();
                var parts = s.Split(',');
                var result = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    int.TryParse(parts[i].Trim(), out result[i]);
                return result;
            }

            switch (msg.Action)
            {
                // ── 選択 ──────────────────────────────────────────────
                case "selectMesh":
                {
                    var indices  = GetIndices("indices");
                    if (indices.Length == 0)
                    {
                        int idx = GetParamInt(msg, "index", -1);
                        indices = idx >= 0 ? new[] { idx } : System.Array.Empty<int>();
                    }
                    var category = (MeshCategory)GetParamInt(msg, "category", (int)MeshCategory.Drawable);
                    return new SelectMeshCommand(modelIndex, category, indices);
                }

                // ── 属性変更 ──────────────────────────────────────────
                case "toggleVisibility":
                    return new ToggleVisibilityCommand(
                        modelIndex, GetParamInt(msg, "masterIndex", 0));

                case "setBatchVisibility":
                    return new SetBatchVisibilityCommand(
                        modelIndex,
                        GetIndices("masterIndices"),
                        GetParamString(msg, "visible", "true") == "true");

                case "toggleLock":
                    return new ToggleLockCommand(
                        modelIndex, GetParamInt(msg, "masterIndex", 0));

                case "cycleMirrorType":
                    return new CycleMirrorTypeCommand(
                        modelIndex, GetParamInt(msg, "masterIndex", 0));

                case "renameMesh":
                    return new RenameMeshCommand(
                        modelIndex,
                        GetParamInt(msg, "masterIndex", 0),
                        GetParamString(msg, "name", ""));

                // ── リスト操作 ────────────────────────────────────────
                case "addMesh":
                    return new AddMeshCommand(modelIndex);

                case "deleteMeshes":
                    return new DeleteMeshesCommand(modelIndex, GetIndices("masterIndices"));

                case "duplicateMeshes":
                    return new DuplicateMeshesCommand(modelIndex, GetIndices("masterIndices"));

                // ── BonePose ──────────────────────────────────────────
                case "initBonePose":
                    return new InitBonePoseCommand(modelIndex, GetIndices("masterIndices"));

                case "setBonePoseActive":
                    return new SetBonePoseActiveCommand(
                        modelIndex,
                        GetIndices("masterIndices"),
                        GetParamString(msg, "active", "true") == "true");

                case "resetBonePoseLayers":
                    return new ResetBonePoseLayersCommand(modelIndex, GetIndices("masterIndices"));

                case "bakePoseToBindPose":
                    return new BakePoseToBindPoseCommand(modelIndex, GetIndices("masterIndices"));

                // ── モデル操作 ────────────────────────────────────────
                case "switchModel":
                    return new SwitchModelCommand(
                        GetParamInt(msg, "targetModelIndex", 0));

                case "renameModel":
                    return new RenameModelCommand(
                        modelIndex, GetParamString(msg, "name", ""));

                case "deleteModel":
                    return new DeleteModelCommand(modelIndex);

                default:
                    return null;
            }
        }

        /// <summary>後方互換: ToolContext直接操作（DispatchCommandなし時）</summary>
        private string ProcessCommandLegacy(RemoteMessage msg)
        {
            switch (msg.Action)
            {
                case "selectMesh":
                {
                    int index = GetParamInt(msg, "index", -1);
                    if (index < 0) return BuildErrorResponse(msg.Id, "Invalid index");
                    Context.SelectMeshContext?.Invoke(index);
                    Context.OnMeshSelectionChanged?.Invoke();
                    Context.Repaint?.Invoke();
                    Log($"selectMesh(legacy): {index}");
                    return BuildSuccessResponse(msg.Id, "true");
                }
                case "updateAttribute":
                {
                    int index = GetParamInt(msg, "index", -1);
                    if (index < 0) return BuildErrorResponse(msg.Id, "Invalid index");
                    var change = new MeshAttributeChange { Index = index };
                    if (msg.Params.TryGetValue("name",    out var n)) change.Name      = n;
                    if (msg.Params.TryGetValue("visible", out var v)) change.IsVisible = v == "true";
                    if (msg.Params.TryGetValue("locked",  out var l)) change.IsLocked  = l == "true";
                    Context.UpdateMeshAttributes?.Invoke(new List<MeshAttributeChange> { change });
                    Context.Repaint?.Invoke();
                    Log($"updateAttribute(legacy): [{index}]");
                    return BuildSuccessResponse(msg.Id, "true");
                }
                default:
                    return BuildErrorResponse(msg.Id, $"Unknown action: {msg.Action}");
            }
        }

        // ================================================================
        // バッチフレーム組み立て
        // [4B Magic=PLRB][1B Version][3B padding][4B FrameCount]{ [4B Len][Data] }×N
        // ================================================================

        private static byte[] BuildBatch(List<byte[]> frames)
        {
            if (frames == null || frames.Count == 0)
            {
                using (var ms = new System.IO.MemoryStream(12))
                using (var w  = new System.IO.BinaryWriter(ms))
                {
                    w.Write(RemoteMagic.Batch);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((uint)0);
                    return ms.ToArray();
                }
            }
            if (frames.Count == 1) return frames[0];

            int totalBody = 0;
            foreach (var f in frames) totalBody += 4 + f.Length;

            using (var ms = new System.IO.MemoryStream(12 + totalBody))
            using (var w  = new System.IO.BinaryWriter(ms))
            {
                w.Write(RemoteMagic.Batch);
                w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                w.Write((uint)frames.Count);
                foreach (var f in frames) { w.Write((uint)f.Length); w.Write(f); }
                return ms.ToArray();
            }
        }

        // ================================================================
        // Pushイベント（モデル変更通知）
        // ================================================================

        private void SubscribeModel()
        {
            UnsubscribeModel();
            var model = Context?.Model;
            if (model == null) return;
            model.OnListChanged += OnModelListChanged;
            _subscribedModel = model;
        }

        private void UnsubscribeModel()
        {
            if (_subscribedModel != null)
            {
                _subscribedModel.OnListChanged -= OnModelListChanged;
                _subscribedModel = null;
            }
        }

        private void OnModelListChanged()
        {
            string data     = RemoteDataProvider.QueryMeshList(Context, null);
            string pushJson = BuildPushMessage("meshListChanged", data);
            BroadcastAsync(pushJson);
        }

        private void BroadcastAsync(string json)
        {
            var server = _wsServer;
            if (server == null) return;
            // JSONメッセージは Text フレーム（TypedPayload の Json アイテム）で配信。
            _ = server.BroadcastAsync(TypedPayload.FromJson(json).ToMessage(), WebSocketFrameKind.Text);
        }

        private void BroadcastBinaryAsync(byte[] data)
        {
            var server = _wsServer;
            if (server == null || data == null) return;
            // PLRx バイナリは Binary フレーム（DuplexPacket + TypedPayload の Binary アイテム）で配信。
            _ = server.BroadcastAsync(TypedPayload.FromBinary(data).ToMessage(), WebSocketFrameKind.Binary);
        }

        // ================================================================
        // 受信（WebSocketDuplexServer.OnReceived）
        // ================================================================

        /// <summary>
        /// DuplexChannel の受信ハンドラ（背景スレッド）。
        /// TypedPayload のアイテムを既存アプリ層（ProcessMessage / ProcessBinaryMessage）へ委譲し、
        /// 応答を items（Json + Binary×n）にまとめて同一チャネルへ返す。
        /// </summary>
        private void OnDuplexReceived(IDuplexChannel channel, DuplexMessage message)
        {
            // アプリ層はメインスレッド前提のため、必ず RunOnMainThread 経由で処理する。
            RunOnMainThread(() =>
            {
                TypedPayload incoming;
                try { incoming = message.ToTypedPayload(); }
                catch { return; }

                bool isRequest = message.Type == MessageType.Request;

                foreach (var item in incoming)
                {
                    if (item.Type == ContentType.Json || item.Type == ContentType.Text)
                    {
                        string json = item.DataString ?? "";
                        if (string.IsNullOrEmpty(json)) continue;

                        _pendingBinaryResponses = null;
                        string response = ProcessMessage(json);
                        var pending = _pendingBinaryResponses;
                        _pendingBinaryResponses = null;

                        if (response == null) continue;

                        var reply = new TypedPayload().AddJson(response);
                        if (pending != null)
                            foreach (var bin in pending)
                                if (bin != null) reply.AddBinary(bin);

                        SendReply(channel, message, reply, isRequest);
                    }
                    else if (item.Type == ContentType.Binary || item.Type == ContentType.Image
                             || item.Type == ContentType.Custom)
                    {
                        byte[] response = ProcessBinaryMessage(item.Data);
                        if (response == null) continue;

                        var reply = new TypedPayload().AddBinary(response);
                        SendReply(channel, message, reply, isRequest);
                    }
                }
            });
        }

        /// <summary>
        /// 応答を返す。JSONは Text、バイナリを含む場合は Binary フレームで送出する。
        /// </summary>
        private void SendReply(IDuplexChannel channel, DuplexMessage request, TypedPayload reply, bool isRequest)
        {
            bool hasBinary = false;
            foreach (var it in reply)
                if (it.Type != ContentType.Json && it.Type != ContentType.Text) { hasBinary = true; break; }

            var kind = hasBinary ? WebSocketFrameKind.Binary : WebSocketFrameKind.Text;
            var wsChannel  = channel as WebSocketDuplexChannel;
            var tcpChannel = channel as TcpDuplexServerChannel;

            try
            {
                if (isRequest)
                {
                    if (wsChannel != null)
                        _ = wsChannel.ReplyAsync(request, reply.ToMessage(), kind);
                    else if (tcpChannel != null)
                        _ = tcpChannel.ReplyAsync(request, reply.ToMessage(), kind);
                    else
                        _ = channel.ReplyAsync(request, reply.ToMessage());
                }
                else
                {
                    if (wsChannel != null)
                        _ = wsChannel.SendAsync(reply.ToMessage(), kind);
                    else if (tcpChannel != null)
                        _ = tcpChannel.SendAsync(reply.ToMessage(), kind);
                    else
                        _ = channel.SendAsync(reply.ToMessage());
                }
            }
            catch { }
        }

        // ================================================================
        // レスポンスビルダー
        // ================================================================

        private static string BuildSuccessResponse(string id, string dataJson)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("id",      id);
            jb.KeyValue("type",    "response");
            jb.KeyValue("success", true);
            jb.KeyRaw("data",      dataJson);
            jb.EndObject();
            return jb.ToString();
        }

        private static string BuildErrorResponse(string id, string error)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("id",      id);
            jb.KeyValue("type",    "response");
            jb.KeyValue("success", false);
            jb.KeyValue("error",   error);
            jb.EndObject();
            return jb.ToString();
        }

        private static string BuildPushMessage(string eventName, string dataJson)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("id",    (string)null);
            jb.KeyValue("type",  "push");
            jb.KeyValue("event", eventName);
            jb.KeyRaw("data",    dataJson);
            jb.EndObject();
            return jb.ToString();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private ProjectContext GetProjectContext() => Context?.Project;

        private static int GetParamInt(RemoteMessage msg, string key, int def)
        {
            if (msg.Params != null && msg.Params.TryGetValue(key, out var val) &&
                int.TryParse(val, out int r)) return r;
            return def;
        }

        private static string GetParamString(RemoteMessage msg, string key, string def)
        {
            if (msg.Params != null && msg.Params.TryGetValue(key, out var val) && val != null)
                return val;
            return def;
        }

        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            OnLog?.Invoke(line);
        }

    }
}
