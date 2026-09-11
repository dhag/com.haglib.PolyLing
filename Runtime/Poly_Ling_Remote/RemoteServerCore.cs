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
//
// 【分割先】このファイルから次へ分けてある。
//   RemoteServerCore.Messages.cs  リモートサーバ：メッセージ処理（クエリ・コマンド）とバッチフレームの組み立て。
//   RemoteServerCore.Push.cs      リモートサーバ：Push イベント・担当変更／選択変更の push・受信・クライアントタイプ登録・レスポンス・ヘルパー。

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
    public partial class RemoteServerCore
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
        /// PanelCommandディスパッチコールバック。実行結果を返す。
        /// PolyLingPlayerServer.Initialize が PlayerCommandDispatcher.Dispatch を渡す。
        /// </summary>
        public Func<PanelCommand, CommandResult> DispatchCommand;

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
    }
}
