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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using HagLib.NET.Duplex;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Serialization.FolderSerializer;

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
        private string         _lastSelSig;
        private readonly List<ImageEntry> _capturedImages = new List<ImageEntry>();
        private ushort         _nextImageId;

        /// <summary>バッチ送信用：テキスト応答直後に送るバイナリフレーム（1回使い切り）</summary>
        private List<byte[]> _pendingBinaryResponses;

        /// <summary>
        /// 応答送出の直後に selectionChanged を送り直す対象ユーザー（1回使い切り）。
        /// project_header 等がホストの選択を含むため、本人の選択で上書きし直す。
        /// </summary>
        private string _pendingSelectionUser;

        // ================================================================
        // クライアントタイプ登録簿
        // register コマンド受信でチャネル→登録情報を追加し、切断で削除する。
        // タイプ宛 push（BroadcastToType）と将来の協働開発（所有者表示・権限等）の基盤。
        // アクセスは全てメインスレッド（OnDuplexReceived/OnClientDisconnected は
        // RunOnMainThread 経由、BroadcastToType は OnModelListChanged 経由）。
        // ================================================================

        private struct ClientRegistration
        {
            public string ClientType;   // 例: "modelList" / "meshList" / "materialList" / "probe"
            public string UserName;     // 既定は空（名前なし）。将来の協働開発で使用。
        }

        private readonly Dictionary<IDuplexChannel, ClientRegistration> _clientRegistry
            = new Dictionary<IDuplexChannel, ClientRegistration>();

        // ================================================================
        // 協働編集: ユーザーごとの選択（案B）
        // 選択は共有 ModelContext ではなくユーザー名ごとのスロットに持つ。
        // コマンド実行の直前だけ SelectionScope で ModelContext へ流し込み、
        // 実行後にホストの選択へ戻す。
        // ================================================================

        private readonly RemoteSelectionStore _selectionStore = new RemoteSelectionStore();

        /// <summary>
        /// ホスト（サーバ本体を操作している人）のユーザー名。
        /// クライアントが同名で register するとホストと選択を共有してしまうため、
        /// 運用上は重複しない名前にすること。
        /// </summary>
        public string HostUserName { get; set; } = "(host)";

        /// <summary>
        /// ホスト側パネルの再同期要求。
        /// 選択スコープの差し替え中に実行されたコマンドが NotifyPanels を呼ぶと
        /// ホストUIが一時的に他ユーザーの選択で描画されるため、復帰後にこれを呼ぶ。
        /// 未設定なら OnRepaint にフォールバックする。
        /// </summary>
        public Action RequestPanelRefresh;

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
                _wsServer.OnClientConnected    += _ => RunOnMainThread(() => { Log("クライアント接続"); _lastSelSig = null; CheckSelectionChanged(); OnRepaint?.Invoke(); });
                // 切断時、選択スロット(_selectionStore)は消さない。
                // 再接続したときに作業対象が消えていると使い勝手が悪いため、
                // ユーザー名をキーに保持し続ける（担当 EditorName と同じ扱い）。
                _wsServer.OnClientDisconnected += ch => RunOnMainThread(() => { _clientRegistry.Remove(ch); Log("クライアント切断"); OnRepaint?.Invoke(); });

                IsRunning = true;
                SubscribeModel();
                _ = _wsServer.StartAsync($"http://localhost:{Port}/");

                Log($"サーバー起動: http://localhost:{Port}/");

                WriteEndpointFile();
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

            DeleteEndpointFile();

            Log("サーバー停止");
        }

        // ================================================================
        // エンドポイント公開ファイル（軽量クライアントの接続先発見用）
        // 保存先: Application.persistentDataPath/PolyLing/endpoint.json
        // 内容:   {"host","port","pid","startedAt"}
        // ================================================================

        private string EndpointFilePath =>
            Path.Combine(Application.persistentDataPath, "PolyLing", "endpoint.json");

        // 自身が書き込んだ内容。Stop 時は自分が書いたファイルのみ削除する。
        private string _lastEndpointJson;

        private void WriteEndpointFile()
        {
            try
            {
                var jb = new JsonBuilder();
                jb.BeginObject();
                jb.KeyValue("host",      "127.0.0.1");
                jb.KeyValue("port",      Port);
                jb.KeyValue("pid",       System.Diagnostics.Process.GetCurrentProcess().Id);
                jb.KeyValue("startedAt", System.DateTime.UtcNow.ToString("o"));
                jb.EndObject();
                string json = jb.ToString();

                string path = EndpointFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json);
                _lastEndpointJson = json;
                Log($"endpoint.json 書込: {path}");
            }
            catch (Exception ex)
            {
                Log($"endpoint.json 書込失敗: {ex.Message}");
            }
        }

        private void DeleteEndpointFile()
        {
            try
            {
                string path = EndpointFilePath;
                if (_lastEndpointJson != null &&
                    File.Exists(path) &&
                    File.ReadAllText(path) == _lastEndpointJson)
                {
                    File.Delete(path);
                    Log("endpoint.json 削除");
                }
            }
            catch (Exception ex)
            {
                Log($"endpoint.json 削除失敗: {ex.Message}");
            }
            _lastEndpointJson = null;
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

        /// <summary>ヒエラルキー書き出しクライアントのタイプ識別子。</summary>
        public const string HierarchyClientType = "hierarchyExport";

        /// <summary>
        /// 現在のプロジェクト全体をプロジェクトファイル形式で一時フォルダへ書き出し、
        /// PLRF 束にして "hierarchyExport" タイプのクライアントへ push する。
        ///
        /// 受け手はこれをフォルダへ展開し、ファイルから読んだときと同じ経路で
        /// Unity ヒエラルキーへ書き出す。
        /// </summary>
        public void SendHierarchyBundle()
        {
            var project = GetProjectContext();
            if (project == null) { Log("ヒエラルキー送信: プロジェクトなし"); return; }

            int targets = 0;
            foreach (var kv in _clientRegistry)
                if (kv.Value.ClientType == HierarchyClientType) targets++;

            if (targets == 0)
            {
                Log("ヒエラルキー送信: 受け手なし（hierarchyExport が未接続）");
                return;
            }

            string bundleName = RemoteFileBundle.SanitizeFolderName(project.Name);
            string sendRoot = Path.Combine(
                Application.persistentDataPath, "PolyLing", "RemoteSend", bundleName);

            byte[] bundle;
            try
            {
                // 前回の残骸を混ぜないため作り直す。
                if (Directory.Exists(sendRoot)) Directory.Delete(sendRoot, true);
                Directory.CreateDirectory(sendRoot);

                if (!CsvProjectSerializer.Export(sendRoot, project))
                {
                    Log("ヒエラルキー送信: プロジェクト書き出しに失敗");
                    return;
                }

                bundle = RemoteFileBundle.Serialize(
                    sendRoot, bundleName, RemoteFileBundle.KindProject, out string serErr);

                if (bundle == null)
                {
                    Log("ヒエラルキー送信: " + serErr);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log("ヒエラルキー送信: 失敗 " + ex.Message);
                return;
            }

            // 先に JSON push で概要を通知し、続けてバイナリ本体を送る。
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("bundleName", bundleName);
            jb.KeyValue("modelCount", project.ModelCount);
            jb.KeyValue("byteCount",  bundle.Length);
            jb.EndObject();

            BroadcastToType(HierarchyClientType, BuildPushMessage("hierarchyBundle", jb.ToString()));
            BroadcastBinaryToType(HierarchyClientType, bundle);

            Log($"ヒエラルキー送信: {project.ModelCount}モデル {bundle.Length}B → {targets}クライアント");
        }

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

        private byte[] ProcessBinaryMessage(byte[] data, string requesterName = "")
        {
            UnityEngine.Debug.Log($"[EditSync] ProcessBinaryMessage enter data={data?.Length ?? 0}");
            Poly_Ling.Remote.BinaryHeader? header;
            try
            {
                header = RemoteBinarySerializer.ReadHeader(data);
                UnityEngine.Debug.Log($"[EditSync] ReadHeader done null={header == null}");
            }
            catch (Exception __ex)
            {
                UnityEngine.Debug.Log($"[EditSync] ReadHeader EX: {__ex.GetType().Name}: {__ex.Message}");
                return null;
            }
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
                    // v2 ヘッダの ObjectId で対象を確定する。
                    // v1（ObjectId=0）は後方互換として先頭描画メッシュへ適用する。
                    var targetCtx = ResolveBinaryTarget(h, out string resolveNote);
                    if (targetCtx?.MeshObject == null)
                    {
                        Log($"位置更新: 対象を解決できません（{resolveNote}）");
                        return null;
                    }

                    if (!targetCtx.IsEditableBy(requesterName))
                    {
                        Log($"位置更新を拒否: {targetCtx.Name} は {targetCtx.EditorName} が担当中"
                            + $"（要求者=\"{requesterName}\"）");
                        return null;
                    }

                    // 頂点数が食い違う場合は適用しない。
                    // 他ユーザーがトポロジを変えた後の古い編集が届くと壊れるため。
                    if (h.VertexCount != (uint)targetCtx.MeshObject.VertexCount)
                    {
                        Log($"位置更新を拒否: 頂点数不一致 {targetCtx.Name} "
                            + $"受信={h.VertexCount} 現在={targetCtx.MeshObject.VertexCount}");
                        return null;
                    }

                    RemoteBinarySerializer.Deserialize(data, targetCtx.MeshObject);
                    Context.SyncMesh?.Invoke();
                    Context.Repaint?.Invoke();
                    Log($"位置更新適用: {targetCtx.Name} ({resolveNote})");
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

        /// <summary>
        /// バイナリヘッダから適用対象の MeshContext を解決する。
        ///
        /// v2 かつ ObjectId!=0 → 安定IDで検索（ModelIndex を優先し、外れたら全モデル走査）。
        /// それ以外              → 先頭描画メッシュ（v1 クライアント互換のフォールバック）。
        ///
        /// 全モデル走査まで行うのは、送信側と受信側で CurrentModelIndex がずれていても
        /// オブジェクトさえ一致すれば正しく当てられるようにするため。
        /// </summary>
        private MeshContext ResolveBinaryTarget(BinaryHeader h, out string note)
        {
            if (!h.HasTarget)
            {
                note = h.Version >= 2 ? "対象未指定→先頭描画メッシュ" : "v1ヘッダ→先頭描画メッシュ";
                return Context?.FirstDrawableMeshContext;
            }

            var proj = GetProjectContext();
            if (proj == null) { note = "プロジェクトなし"; return null; }

            // 指定モデルを先に見る
            if (h.ModelIndex >= 0 && h.ModelIndex < proj.ModelCount)
            {
                var mc = FindByObjectId(proj.Models[h.ModelIndex], h.ObjectId);
                if (mc != null) { note = $"id={h.ObjectId} model={h.ModelIndex}"; return mc; }
            }

            // 外れたら全モデルを走査
            for (int mi = 0; mi < proj.ModelCount; mi++)
            {
                if (mi == h.ModelIndex) continue;
                var mc = FindByObjectId(proj.Models[mi], h.ObjectId);
                if (mc != null) { note = $"id={h.ObjectId} model={mi}(走査)"; return mc; }
            }

            note = $"id={h.ObjectId} 該当なし";
            return null;
        }

        private static MeshContext FindByObjectId(ModelContext model, ulong objectId)
        {
            if (model == null || objectId == 0UL) return null;
            int count = model.MeshContextCount;
            for (int i = 0; i < count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && mc.ObjectId == objectId) return mc;
            }
            return null;
        }

        // ================================================================
        // メッセージ処理（クエリ・コマンド）
        // ================================================================

        private string ProcessMessage(string json, IDuplexChannel channel = null)
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

            if (msg.Type == "query")   return ProcessQuery(msg, channel);
            if (msg.Type == "command") return ProcessCommand(msg, channel);
            return BuildErrorResponse(msg.Id, $"Unknown type: {msg.Type}");
        }

        /// <summary>
        /// チャネルに紐づく register 済みユーザー名を返す。未登録なら空文字。
        /// 所有権判定（RemoteOwnership）の要求者名として使う。
        /// </summary>
        private string ResolveUserName(IDuplexChannel channel)
        {
            if (channel == null) return "";
            return _clientRegistry.TryGetValue(channel, out var reg) ? (reg.UserName ?? "") : "";
        }

        private string ProcessQuery(RemoteMessage msg, IDuplexChannel channel = null)
        {
            // project_header / model_meta は ModelMeta にホストの選択を載せてしまう。
            // そのままだとクライアントの選択がホストのもので上書きされるため、
            // 応答送出の「後」に本人の選択を送り直す予約を立てる。
            // （先に送ると応答が後着して上書きされる。FIFO なので順序が重要。）
            switch (msg.Target)
            {
                case "project_header":
                case "model_meta":
                case "mesh_data_batch":
                    _pendingSelectionUser = ResolveUserName(channel);
                    break;
            }

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

                // probe クライアント用（リスト系とは別データ・テキスト応答）。
                case "server_info":       return BuildSuccessResponse(msg.Id, BuildServerInfoData());

                // 協働編集: 現在の担当状況（接続直後の初期同期用）
                case "ownership":
                {
                    var proj = GetProjectContext();
                    int mi   = GetParamInt(msg, "modelIndex", proj?.CurrentModelIndex ?? 0);
                    var model = (proj != null && mi >= 0 && mi < proj.ModelCount) ? proj.Models[mi] : null;
                    return BuildSuccessResponse(msg.Id,
                        RemoteOwnership.BuildOwnershipJson(model, mi));
                }

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

            _pendingBinaryResponses = new List<byte[]> { BuildBatch(binaries) };

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

            _pendingBinaryResponses = new List<byte[]> { BuildBatch(binaries) };
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

        private string ProcessCommand(RemoteMessage msg, IDuplexChannel channel = null)
        {
            try
            {
                // DispatchCommandが設定されている場合はPanelCommand経由で全処理
                if (DispatchCommand != null)
                    return ProcessCommandViaPanelCommand(msg, channel);

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
        /// JSON → PanelCommand に変換し、所有権判定を通してから DispatchCommand に流す。
        /// DispatchPanelCommand（SummaryNotify）が実処理を担う。
        ///
        /// ここが唯一の書き込み入口なので、協働編集の認可ゲートもここ1箇所に置く。
        /// </summary>
        private string ProcessCommandViaPanelCommand(RemoteMessage msg, IDuplexChannel channel)
        {
            int modelIndex = GetParamInt(msg, "modelIndex", 0);
            PanelCommand cmd = BuildPanelCommand(msg, modelIndex);
            if (cmd == null)
                return BuildErrorResponse(msg.Id, $"Unknown action: {msg.Action}");

            string requester = ResolveUserName(channel);

            // ── 選択はユーザーごとに持つ（共有 ModelContext を書き換えない） ──
            // selectMesh をここで横取りし、本体へは流さない。
            // これをやらないと A の選択が B の画面まで飛び、担当を分けても
            // 同時作業ができなくなる。
            if (cmd is SelectMeshCommand sel)
                return HandleRemoteSelect(msg, channel, requester, sel);

            // ── 所有権ゲート ────────────────────────────────────────
            ulong[] objectIds = RemoteOwnership.ParseIdCsv(GetParamString(msg, "objectIds", null));

            var verdict = RemoteOwnership.TryAuthorize(GetProjectContext(), cmd, requester, objectIds);
            if (!verdict.Allowed)
            {
                Log($"拒否: {msg.Action} user=\"{requester}\" → {verdict.Reason}");

                // 構造ズレの場合は当該クライアントへ再取得を促す
                if (verdict.StaleView)
                    SendToChannel(channel,
                        TypedPayload.FromJson(BuildPushMessage("refreshRequired", "{}")),
                        WebSocketFrameKind.Text);

                return BuildErrorResponse(msg.Id, verdict.Reason);
            }

            // ── 選択スコープを差し替えてから実行 ────────────────────
            // MasterIndex を持たないコマンド（PartsSet系・SkinWeight系など）は
            // 「今の選択」を見て動くため、要求者の選択を一時的に流し込む。
            DispatchWithSelectionOf(requester, cmd);

            Log($"cmd: {msg.Action} model={modelIndex} user=\"{requester}\"");

            // 担当が動いた可能性があるので差分があれば全体へ通知
            CheckOwnershipChanged();

            return BuildSuccessResponse(msg.Id, "true");
        }

        /// <summary>
        /// 要求者の選択を ModelContext へ一時適用してコマンドを実行し、
        /// 実行後にホストの選択へ戻す。
        /// 差し替えが起きた場合はホストUIが他ユーザーの選択で描画されているため、
        /// 復帰後に再同期を要求する。
        /// </summary>
        private void DispatchWithSelectionOf(string userName, PanelCommand cmd)
        {
            var model = Context?.Model;
            var slot  = _selectionStore.Find(userName);

            if (model == null || slot == null)
            {
                DispatchCommand(cmd);
                return;
            }

            var proj = Context?.Project;
            int mi = proj?.CurrentModelIndex ?? 0;

            bool swapped;
            using (var scope = SelectionScope.Apply(model, mi, slot))
            {
                swapped = scope.Swapped;
                DispatchCommand(cmd);
            }

            if (swapped)
                (RequestPanelRefresh ?? OnRepaint)?.Invoke();
        }

        /// <summary>
        /// クライアントからの selectMesh を、そのユーザーのスロットへ記録する。
        /// 本体（共有 ModelContext）は書き換えず、全体 push も行わない。
        /// 応答は本人のチャネルにのみ返す（同名で複数パネルを開いていれば全部に届く）。
        /// </summary>
        private string HandleRemoteSelect(
            RemoteMessage msg, IDuplexChannel channel, string requester, SelectMeshCommand sel)
        {
            if (string.IsNullOrEmpty(requester))
                return BuildErrorResponse(msg.Id,
                    "ユーザー名が未登録のため選択を保持できません。名前を設定して接続し直してください。");

            var model = Context?.Model;
            int mi    = Context?.Project?.CurrentModelIndex ?? 0;

            var slot = _selectionStore.GetOrCreate(requester, UserSelection.Capture(model, mi));
            slot.ModelIndex = sel.ModelIndex;

            var indices = new List<int>(sel.Indices ?? Array.Empty<int>());
            switch (sel.Category)
            {
                case MeshCategory.Bone:
                    slot.Bone = indices;
                    slot.Category = ModelContext.SelectionCategory.Bone;
                    break;
                case MeshCategory.Morph:
                    slot.Morph = indices;
                    slot.Category = ModelContext.SelectionCategory.Morph;
                    break;
                default:
                    slot.Drawable = indices;
                    slot.Category = ModelContext.SelectionCategory.Mesh;
                    break;
            }

            SendSelectionToUser(requester, slot);
            Log($"select: user=\"{requester}\" cat={sel.Category} n={indices.Count}");
            return BuildSuccessResponse(msg.Id, "true");
        }

        /// <summary>予約されていた selectionChanged を送出する（無ければ何もしない）。</summary>
        private void FlushPendingSelection(string userName)
        {
            if (string.IsNullOrEmpty(userName)) return;
            var slot = _selectionStore.Find(userName);
            if (slot != null) SendSelectionToUser(userName, slot);
        }

        /// <summary>指定ユーザーの全チャネルへ selectionChanged を送る。</summary>
        private void SendSelectionToUser(string userName, UserSelection sel)
        {
            if (_clientRegistry.Count == 0 || sel == null) return;

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex", sel.ModelIndex);
            jb.KeyValue("category",   (int)sel.Category);
            jb.KeyValue("drawable",   UserSelection.Csv(sel.Drawable));
            jb.KeyValue("bone",       UserSelection.Csv(sel.Bone));
            jb.KeyValue("morph",      UserSelection.Csv(sel.Morph));
            jb.EndObject();

            string json = BuildPushMessage("selectionChanged", jb.ToString());
            foreach (var kv in _clientRegistry)
                if (kv.Value.UserName == userName)
                    SendToChannel(kv.Key, TypedPayload.FromJson(json), WebSocketFrameKind.Text);
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

            // string[]パラメータ取得ヘルパー。カンマ区切りで、名前自体がカンマ・
            // 引用符・改行を含む場合は二重引用符で包まれている（PanelCommandRouter.EscName）。
            string[] GetNames(string key)
            {
                if (msg.Params == null || !msg.Params.TryGetValue(key, out var s) || string.IsNullOrEmpty(s))
                    return System.Array.Empty<string>();
                return SplitQuotedCsv(s);
            }

            // float[]パラメータ取得ヘルパー。送信側（PanelCommandRouter.FloatCsv）が
            // InvariantCulture で書くので、読む側もロケールを固定する。
            float[] GetFloats(string key)
            {
                if (msg.Params == null || !msg.Params.TryGetValue(key, out var s) || string.IsNullOrEmpty(s))
                    return System.Array.Empty<float>();
                var parts = s.Split(',');
                var result = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out result[i]);
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

                case "setBatchLock":
                    return new SetBatchLockCommand(
                        modelIndex,
                        GetIndices("masterIndices"),
                        GetParamString(msg, "locked", "true") == "true");

                case "setMirrorEnabled":
                    return new SetMirrorEnabledCommand(
                        modelIndex,
                        GetIndices("masterIndices"),
                        GetParamString(msg, "enabled", "true") == "true");

                case "setBatchMirrorType":
                    return new SetBatchMirrorTypeCommand(
                        modelIndex,
                        GetIndices("masterIndices"),
                        GetParamInt(msg, "mirrorType", 0));

                // ── 編集者（担当）の取得・解放 ────────────────────────
                // editorName が空文字なら解放。
                // 「自分以外の名前を設定していないか」「他人の担当を奪っていないか」は
                // RemoteOwnership.TryAuthorize が判定する。
                case "setObjectEditor":
                    return new SetObjectEditorCommand(
                        modelIndex,
                        GetIndices("masterIndices"),
                        GetParamString(msg, "editorName", ""),
                        RemoteOwnership.ParseIdCsv(GetParamString(msg, "objectIds", null)),
                        force: false);

                case "cycleMirrorType":
                    return new CycleMirrorTypeCommand(
                        modelIndex, GetParamInt(msg, "masterIndex", 0));

                case "renameMesh":
                    return new RenameMeshCommand(
                        modelIndex,
                        GetParamInt(msg, "masterIndex", 0),
                        GetParamString(msg, "name", ""));

                // 名称一括変更。名前の重複回避は PlayerCommandDispatcher 側で行う。
                case "renameMeshes":
                    return new RenameMeshesCommand(
                        modelIndex,
                        GetIndices("masterIndices"),
                        GetNames("names"));

                // ── 選択辞書 ──────────────────────────────────────────
                case "applySelectionDictionary":
                    return new ApplySelectionDictionaryCommand(
                        modelIndex,
                        GetParamInt(msg, "setIndex", -1),
                        GetParamString(msg, "addToExisting", "false") == "true");

                // ── リスト操作 ────────────────────────────────────────
                case "addMesh":
                    return new AddMeshCommand(modelIndex);

                case "deleteMeshes":
                    return new DeleteMeshesCommand(modelIndex, GetIndices("masterIndices"));

                case "duplicateMeshes":
                    return new DuplicateMeshesCommand(modelIndex, GetIndices("masterIndices"));

                // ── メッシュブレンド ──────────────────────────────────
                // ソースの3列は同じ並びで届く。長さが揃わない要求は
                // 対応関係が決まらないので短いほうに合わせて切る。
                case "applyBlend":
                {
                    var srcModels  = GetIndices("srcModelIndices");
                    var srcMasters = GetIndices("srcMasterIndices");
                    var srcWeights = GetFloats("srcWeights");

                    int n = Math.Min(srcModels.Length, Math.Min(srcMasters.Length, srcWeights.Length));
                    if (n > ApplyBlendCommand.MaxSources) n = ApplyBlendCommand.MaxSources;

                    var specs = new BlendSourceSpec[n];
                    for (int i = 0; i < n; i++)
                        specs[i] = new BlendSourceSpec(srcModels[i], srcMasters[i], srcWeights[i]);

                    return new ApplyBlendCommand(
                        modelIndex,
                        specs,
                        GetParamInt(msg, "destMasterIndex", -1),
                        GetParamString(msg, "createNewObject", "false") == "true",
                        GetParamString(msg, "recalcNormals",   "true")  == "true",
                        GetParamString(msg, "selectedOnly",    "false") == "true",
                        (Poly_Ling.UI.BlendMatchMode)GetParamInt(
                            msg, "matchMode", (int)Poly_Ling.UI.BlendMatchMode.Index));
                }

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

        /// <summary>
        /// 二重引用符に対応したカンマ区切り分割。
        /// エスケープ規則は MeshSelSetCsvHelper / PanelCommandRouter.EscName と同一。
        /// </summary>
        private static string[] SplitQuotedCsv(string line)
        {
            var result = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '"')
                {
                    i++;
                    var sb = new System.Text.StringBuilder();
                    while (i < line.Length)
                    {
                        if (line[i] == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; }
                            else { i++; break; }
                        }
                        else { sb.Append(line[i]); i++; }
                    }
                    result.Add(sb.ToString());
                    if (i < line.Length && line[i] == ',') i++;
                }
                else
                {
                    int start = i;
                    while (i < line.Length && line[i] != ',') i++;
                    result.Add(line.Substring(start, i - start));
                    if (i < line.Length) i++;
                }
            }
            return result.ToArray();
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

        // ================================================================
        // 担当（編集者）変更 push
        // 一覧の再フェッチを伴わない軽量通知。差分がある時だけ送出する。
        // ================================================================

        private string _lastOwnerSig;

        /// <summary>ホスト側で担当を変更した際に呼ぶ公開トリガ。</summary>
        public void NotifyOwnershipChanged() => CheckOwnershipChanged();

        private void CheckOwnershipChanged()
        {
            if (_wsServer == null || ClientCount == 0) return;

            var proj = Context?.Project;
            if (proj == null) { _lastOwnerSig = null; return; }

            int mi = proj.CurrentModelIndex;
            var model = (mi >= 0 && mi < proj.ModelCount) ? proj.Models[mi] : null;

            string sig = RemoteOwnership.BuildOwnershipSignature(model, mi);
            if (sig == _lastOwnerSig) return;
            _lastOwnerSig = sig;

            BroadcastAsync(BuildPushMessage(
                "ownershipChanged", RemoteOwnership.BuildOwnershipJson(model, mi)));
        }

        private void OnModelListChanged()
        {
            string data     = RemoteDataProvider.QueryMeshList(Context, null);
            string pushJson = BuildPushMessage("meshListChanged", data);
            BroadcastAsync(pushJson);

            // 削除・並べ替えで担当の並びも変わるため合わせて確認する
            CheckOwnershipChanged();

            // probe タイプにのみ server_info 変化を通知（タイプ振り分けの実証）。
            // list 系はこの push を受け取らない。
            BroadcastToType("probe", BuildPushMessage("serverInfoChanged", BuildServerInfoData()));
        }

        /// <summary>probe クライアント用のサーバ情報 JSON（リスト系が取得しないデータ）。</summary>
        private string BuildServerInfoData()
        {
            var project = GetProjectContext();
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("port",              Port);
            jb.KeyValue("modelCount",        project?.ModelCount ?? 0);
            jb.KeyValue("currentModelIndex", project?.CurrentModelIndex ?? -1);
            jb.KeyValue("clientCount",       ClientCount);
            jb.KeyValue("serverTime",        DateTime.Now.ToString("HH:mm:ss"));
            jb.EndObject();
            return jb.ToString();
        }

        // ================================================================
        // 選択変更 push（サーバ→クライアント選択反映）
        // 本体の選択→パネル通知（NotifySelectionChanged）と接続時からイベント駆動で呼ばれる。
        // ポーリングはしない。実送出は _lastSelSig 差分で抑止する。
        // ================================================================

        /// <summary>
        /// ホスト（本体）が選択変更を検知した際に呼ぶ公開トリガ。
        ///
        /// 【変更】以前は全クライアントへ配信していたが、選択をユーザーごとに持つ
        /// 方式（案B）に変えたため、ここではホスト自身のスロットを更新するだけにする。
        /// ホストの選択を他ユーザーへ押し付けない。
        /// </summary>
        public void NotifySelectionChanged() => CheckSelectionChanged();

        private void CheckSelectionChanged()
        {
            var proj  = Context?.Project;
            var model = Context?.Model;
            if (proj == null || model == null) { _lastSelSig = null; return; }

            string sig = BuildSelectionSignature(proj.CurrentModelIndex, model);
            if (sig == _lastSelSig) return;
            _lastSelSig = sig;

            // ホストの選択スロットを最新化する。
            // 差し替えスコープの復帰値もここが基準になる。
            _selectionStore.Set(HostUserName,
                UserSelection.Capture(model, proj.CurrentModelIndex));
        }

        private static string CsvIndices(System.Collections.Generic.List<int> list)
        {
            if (list == null || list.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(list[i]);
            }
            return sb.ToString();
        }

        private static string BuildSelectionSignature(int modelIndex, ModelContext model)
        {
            return modelIndex + "|" + (int)model.ActiveCategory + "|"
                 + CsvIndices(model.SelectedDrawableMeshIndices) + "|"
                 + CsvIndices(model.SelectedBoneIndices) + "|"
                 + CsvIndices(model.SelectedMorphIndices);
        }

        /// <summary>
        /// 【非推奨・単独利用時のみ】現在の選択を全クライアントへ一斉配信する。
        ///
        /// 協働編集では使わない。選択はユーザーごとのスロットで管理し、
        /// SendSelectionToUser で本人にだけ返す（他人の画面を動かさないため）。
        /// 1人で複数パネルを開くだけの旧来の使い方に戻したい場合のみ呼ぶこと。
        /// </summary>
        public void BroadcastSelectionToAll()
        {
            var proj  = Context?.Project;
            var model = Context?.Model;
            if (proj == null || model == null) return;
            BroadcastSelection(proj.CurrentModelIndex, model);
        }

        private void BroadcastSelection(int modelIndex, ModelContext model)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex", modelIndex);
            jb.KeyValue("category",   (int)model.ActiveCategory);
            jb.KeyValue("drawable",   CsvIndices(model.SelectedDrawableMeshIndices));
            jb.KeyValue("bone",       CsvIndices(model.SelectedBoneIndices));
            jb.KeyValue("morph",      CsvIndices(model.SelectedMorphIndices));
            jb.EndObject();

            string pushJson = BuildPushMessage("selectionChanged", jb.ToString());
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

        /// <summary>
        /// 【非推奨】対象未指定で位置を配信する。受信側は先頭描画メッシュへ当ててしまう。
        /// 協働編集では MeshContext を取る方のオーバーロードを使うこと。
        /// </summary>
        public void BroadcastPositions(Poly_Ling.Data.MeshObject mesh)
        {
            if (mesh == null) return;
            var data = RemoteBinarySerializer.SerializePositionsOnly(mesh);
            UnityEngine.Debug.Log($"[EditSync] BroadcastPositions(対象未指定) V={mesh.VertexCount} bytes={data?.Length ?? 0} clients={ClientCount}");
            if (data != null) BroadcastBinaryAsync(data);
        }

        /// <summary>
        /// 対象を明示して位置（PositionsOnly）を全クライアントへ配信する。
        /// ヘッダに ObjectId が載るため、受信側は正しいメッシュへ適用できる。
        /// </summary>
        public void BroadcastPositions(Poly_Ling.Data.MeshContext mc, int modelIndex = -1)
        {
            if (mc?.MeshObject == null) return;
            if (modelIndex < 0) modelIndex = Context?.Project?.CurrentModelIndex ?? 0;

            var data = RemoteBinarySerializer.SerializePositionsOnly(mc, modelIndex);
            UnityEngine.Debug.Log($"[EditSync] BroadcastPositions \"{mc.Name}\" id={mc.ObjectId} "
                + $"V={mc.MeshObject.VertexCount} bytes={data?.Length ?? 0} clients={ClientCount}");
            if (data != null) BroadcastBinaryAsync(data);
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
                catch (Exception __ex)
                {
                    UnityEngine.Debug.Log($"[EditSync] OnDup ToTypedPayload EX: {__ex.Message}");
                    return;
                }

                {
                    int __n = 0; var __sb = new System.Text.StringBuilder();
                    foreach (var __it in incoming) { __sb.Append($"{__it.Type}({__it.Data?.Length ?? 0}) "); __n++; }
                    UnityEngine.Debug.Log($"[EditSync] OnDup items={__n} types=[{__sb}]");
                }

                bool isRequest = message.Type == MessageType.Request;

                foreach (var item in incoming)
                {
                    if (item.Type == ContentType.Json || item.Type == ContentType.Text)
                    {
                        string json = item.DataString ?? "";
                        if (string.IsNullOrEmpty(json)) continue;

                        // クライアントタイプ登録は channel が必要なためここで横取りする
                        // （ProcessMessage は json のみで channel を持たない）。register 以外は従来経路。
                        if (TryHandleRegister(channel, message, isRequest, json)) continue;

                        _pendingBinaryResponses = null;
                        _pendingSelectionUser   = null;
                        // channel を渡すのは所有権判定に要求者名が要るため。
                        string response = ProcessMessage(json, channel);
                        var pending      = _pendingBinaryResponses;
                        var selUser      = _pendingSelectionUser;
                        _pendingBinaryResponses = null;
                        _pendingSelectionUser   = null;

                        if (response == null)
                        {
                            FlushPendingSelection(selUser);
                            continue;
                        }

                        var reply = new TypedPayload().AddJson(response);
                        if (pending != null)
                            foreach (var bin in pending)
                                if (bin != null) reply.AddBinary(bin);

                        SendReply(channel, message, reply, isRequest);

                        // 応答（＝ホスト選択入りの ModelMeta）の後に本人の選択を送り直す
                        FlushPendingSelection(selUser);
                    }
                    else if (item.Type == ContentType.Binary || item.Type == ContentType.Image
                             || item.Type == ContentType.Custom)
                    {
                        UnityEngine.Debug.Log($"[EditSync] OnDup binary-branch enter type={item.Type} data={item.Data?.Length ?? 0}");
                        byte[] response = ProcessBinaryMessage(item.Data, ResolveUserName(channel));
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
        // クライアントタイプ登録 / タイプ宛 push
        // ================================================================

        /// <summary>
        /// register コマンド（{"type":"command","action":"register","params":{"clientType":..,"userName":..}}）
        /// を横取りしてチャネル→登録情報を記録し、ack を返す。register 以外は false。
        /// </summary>
        private bool TryHandleRegister(IDuplexChannel channel, DuplexMessage request, bool isRequest, string json)
        {
            if (PeekJsonString(json, "type") != "command") return false;
            if (PeekJsonString(json, "action") != "register") return false;

            string clientType = PeekJsonString(json, "clientType") ?? "";
            string userName   = PeekJsonString(json, "userName")   ?? "";
            string id         = PeekJsonString(json, "id");

            _clientRegistry[channel] = new ClientRegistration { ClientType = clientType, UserName = userName };
            Log($"register: type=\"{clientType}\" user=\"{userName}\" (clients={_clientRegistry.Count})");

            // 協働編集: このユーザーの選択スロットを用意する。
            // 初回はホストの現在選択を種にして、いきなり無選択にならないようにする。
            // 2枚目以降のパネルは既存スロットを共有する（同じ人の画面は連動）。
            UserSelection slot = null;
            if (!string.IsNullOrEmpty(userName))
            {
                var model = Context?.Model;
                int mi    = Context?.Project?.CurrentModelIndex ?? 0;
                slot = _selectionStore.GetOrCreate(userName, UserSelection.Capture(model, mi));
            }

            var data = new JsonBuilder();
            data.BeginObject();
            data.KeyValue("registered", true);
            data.KeyValue("clientType", clientType);
            data.KeyValue("userName",   userName);
            data.EndObject();

            var reply = new TypedPayload().AddJson(BuildSuccessResponse(id, data.ToString()));
            SendReply(channel, request, reply, isRequest);

            // ack の直後に、このユーザー自身の選択を返して画面を合わせる。
            // 全体 push ではないので他ユーザーの画面は動かない。
            if (slot != null) SendSelectionToUser(userName, slot);

            return true;
        }

        /// <summary>指定タイプのクライアントにのみ JSON push を送る（一斉 BroadcastAsync とは別系統）。</summary>
        private void BroadcastToType(string clientType, string json)
        {
            if (_clientRegistry.Count == 0) return;
            foreach (var kv in _clientRegistry)
                if (kv.Value.ClientType == clientType)
                    SendToChannel(kv.Key, TypedPayload.FromJson(json), WebSocketFrameKind.Text);
        }

        /// <summary>指定タイプのクライアントにのみバイナリ push を送る（BroadcastToType のバイナリ版）。</summary>
        private void BroadcastBinaryToType(string clientType, byte[] data)
        {
            if (data == null || _clientRegistry.Count == 0) return;
            foreach (var kv in _clientRegistry)
                if (kv.Value.ClientType == clientType)
                    SendToChannel(kv.Key, TypedPayload.FromBinary(data), WebSocketFrameKind.Binary);
        }

        /// <summary>単一チャネルへ送出（SendReply のチャネル分岐と同一方針）。</summary>
        private void SendToChannel(IDuplexChannel channel, TypedPayload payload, WebSocketFrameKind kind)
        {
            try
            {
                var wsChannel  = channel as WebSocketDuplexChannel;
                var tcpChannel = channel as TcpDuplexServerChannel;
                if (wsChannel != null)       _ = wsChannel.SendAsync(payload.ToMessage(), kind);
                else if (tcpChannel != null) _ = tcpChannel.SendAsync(payload.ToMessage(), kind);
                else                         _ = channel.SendAsync(payload.ToMessage());
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

        /// <summary>register 判定用の軽量文字列抽出（"key":"value" 形式のみ。無ければ null）。</summary>
        private static string PeekJsonString(string json, string key)
        {
            string s = "\"" + key + "\"";
            int i = json.IndexOf(s, StringComparison.Ordinal); if (i < 0) return null;
            int c = json.IndexOf(':', i + s.Length);            if (c < 0) return null;
            int vs = c + 1;
            while (vs < json.Length && (json[vs] == ' ' || json[vs] == '\t')) vs++;
            if (vs >= json.Length || json[vs] != '"') return null;
            int ve = json.IndexOf('"', vs + 1);                 if (ve < 0) return null;
            return json.Substring(vs + 1, ve - vs - 1);
        }

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
