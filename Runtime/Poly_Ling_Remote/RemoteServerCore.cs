// Remote/RemoteServerCore.cs
// WebSocketサーバーのコアロジック。UnityEditor非依存。
// EditorWindow（RemoteServer）またはスタンドアロンアプリからホストされる。
//
// 使用方法:
//   var core = new RemoteServerCore(() => toolContext);
//   （待ち受けポートは OS が割り当てる。接続先はサーバ一覧（RemoteDirectory）で公開する）
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

        /// <summary>実際に待ち受けているポート（OS 割り当て）。停止中は 0。</summary>
        public int  Port      { get; private set; }
        public bool IsRunning { get; private set; }

        /// <summary>サーバ一覧（マスター）での役割。停止中は None。</summary>
        public RemoteDirectoryNode.NodeRole DirectoryRole => _directory?.Role ?? RemoteDirectoryNode.NodeRole.None;

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
        /// 担当者判定はディスパッチャ側が操作者（CommandActor）を見て行う。
        /// </summary>
        public Func<PanelCommand, CommandActor, CommandResult> DispatchCommand;

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
        public string HostUserName { get; set; } = CommandActor.DefaultHostUserName;

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
        public RemoteServerCore(Func<ToolContext> contextProvider)
        {
            _contextProvider = contextProvider;
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
                _wsServer.OnClientDisconnected += ch => RunOnMainThread(() =>
                {
                    // 切断した作業者の一時ロックを外す（操作経路統一計画.md L-4）。
                    // 名前は登録簿から消す前に引く。
                    string leftUser = ResolveUserName(ch);
                    _clientRegistry.Remove(ch);
                    Log("クライアント切断");
                    if (RemoteOwnership.ReleaseLocksOf(GetProjectContext(), leftUser))
                        CheckOwnershipChanged();
                    OnRepaint?.Invoke();
                });

                // ポートは OS に割り当てさせる（複数起動で衝突しない）。
                // Bind は同期で行われ、失敗すると例外になる。
                _ = _wsServer.StartAsync(0);
                Port = _wsServer.BoundPort;

                // 待ち受けが立ってから状態を確定し、モデルを購読する。
                IsRunning  = true;
                _startedAt = DateTime.UtcNow.ToString("o");
                SubscribeModel();

                Log($"サーバー起動: http://localhost:{Port}/");

                StartDirectory();
            }
            catch (Exception ex)
            {
                Log($"起動失敗: {ex.Message}");
                try { _ = _wsServer?.StopAsync(); } catch { }
                _wsServer = null;
                Port      = 0;
                IsRunning = false;
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            StopDirectory();

            UnsubscribeModel();

            try { _ = _wsServer?.StopAsync(); } catch { }
            _wsServer = null;
            IsRunning = false;
            Port      = 0;

            Log("サーバー停止");
        }

        // ================================================================
        // サーバ一覧（RemoteDirectory）への参加
        // 起動時にマスターになるか、既存のマスターへ登録する。
        // クライアントはマスターから一覧を得て接続先を決める。
        // ================================================================

        private RemoteDirectoryNode _directory;
        private string              _startedAt = "";

        /// <summary>サーバ一覧での役割が変わったとき（メインスレッドで発火）。</summary>
        public Action OnDirectoryRoleChanged;

        private void StartDirectory()
        {
            StopDirectory();

            var node = new RemoteDirectoryNode(GetDirectoryInfoAsync)
            {
                // 背景スレッドから呼ばれるため、メインスレッドへ回す。
                OnLog         = msg => RunOnMainThread(() => Log(msg)),
                OnRoleChanged = ()  => RunOnMainThread(() =>
                {
                    OnDirectoryRoleChanged?.Invoke();
                    OnRepaint?.Invoke();
                }),
            };
            _directory = node;
            node.Start();
        }

        private void StopDirectory()
        {
            var node = _directory;
            _directory = null;
            node?.Stop();
        }

        /// <summary>
        /// サーバ一覧に載せる自分の情報。背景スレッドから呼ばれるので、
        /// 組み立てはメインスレッドで行う（プロジェクト名は起動後に変わる）。
        /// </summary>
        private Task<RemoteServerInfo> GetDirectoryInfoAsync()
        {
            var tcs = new TaskCompletionSource<RemoteServerInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            RunOnMainThread(() =>
            {
                try
                {
                    tcs.TrySetResult(new RemoteServerInfo
                    {
                        Pid          = System.Diagnostics.Process.GetCurrentProcess().Id,
                        Port         = Port,
                        HostUserName = HostUserName ?? "",
                        ProjectName  = GetProjectContext()?.Name ?? "",
                        StartedAt    = _startedAt,
                    });
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
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
        /// 現在のプロジェクト全体を JSON（ProjectDTO）の UTF-8 バイト列にして、
        /// "hierarchyExport" タイプのクライアントへ push する。ファイルは介さない。
        ///
        /// 受け手はメモリ上で ModelContext に戻し、そのまま Unity ヒエラルキー／プレファブへ書き出す。
        /// 受け手は「サーバからの自動受け入れ」をオンにしている間だけ
        /// "hierarchyExport" で登録する（HierarchyRemoteExportWindow）。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        public string SendHierarchyBundle()
        {
            var project = GetProjectContext();
            if (project == null)
            {
                const string noProject = "ヒエラルキー送信: プロジェクトなし";
                Log(noProject);
                return noProject;
            }

            int targets = 0;
            foreach (var kv in _clientRegistry)
                if (kv.Value.ClientType == HierarchyClientType) targets++;

            if (targets == 0)
            {
                const string noTarget =
                    "ヒエラルキー送信: 受け手なし（自動受け入れがオンのエディタ拡張が未接続）";
                Log(noTarget);
                return noTarget;
            }

            if (!TryBuildProjectJson(project, out string bundleName, out byte[] bundle, out string buildError))
            {
                string failed = "ヒエラルキー送信: " + buildError;
                Log(failed);
                return failed;
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
            return null;
        }

        /// <summary>
        /// 現在のプロジェクト全体を JSON（ProjectDTO）の UTF-8 バイト列にする。ファイルは介さない。
        /// push（SendHierarchyBundle）とクエリ（project_bundle）の共通部。
        /// </summary>
        private bool TryBuildProjectJson(ProjectContext project,
            out string bundleName, out byte[] bundle, out string error)
        {
            bundleName = project.Name ?? "";
            bundle     = null;
            error      = "";

            try
            {
                var dto = Poly_Ling.Serialization.ProjectDTO.Create(project.Name);
                foreach (var model in project.Models)
                {
                    var modelDto = Poly_Ling.Serialization.ModelSerializer.FromModelContext(model);
                    if (modelDto == null) continue;

                    // テクスチャ画像そのものも載せる（JSON はパスしか持たないため）。
                    modelDto.embeddedTextures = Poly_Ling.Serialization.ModelTextureTransfer.ToEmbedded(
                        Poly_Ling.Serialization.ModelTextureTransfer.Collect(model));

                    dto.models.Add(modelDto);
                }

                string json = Poly_Ling.Serialization.ProjectSerializer.ToJson(dto);
                bundle = Encoding.UTF8.GetBytes(json ?? "");
                return true;
            }
            catch (Exception ex)
            {
                error  = "失敗 " + ex.Message;
                bundle = null;
                return false;
            }
        }

        /// <summary>
        /// クエリ project_bundle：要求したクライアントにだけ、プロジェクト全体の JSON（ProjectDTO）を返す。
        /// 応答 JSON に概要（bundleName / modelCount / byteCount）、続くバイナリに JSON 本体（UTF-8）。
        /// </summary>
        private string ProcessProjectBundleQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null) return BuildErrorResponse(msg.Id, "No project");

            if (!TryBuildProjectJson(project, out string bundleName, out byte[] bundle, out string error))
            {
                Log("プロジェクト束の応答: " + error);
                return BuildErrorResponse(msg.Id, error);
            }

            _pendingBinaryResponses = new List<byte[]> { bundle };

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("bundleName", bundleName);
            jb.KeyValue("modelCount", project.ModelCount);
            jb.KeyValue("byteCount",  bundle.Length);
            jb.EndObject();

            Log($"プロジェクト束の応答: {project.ModelCount}モデル {bundle.Length}B");
            return BuildSuccessResponse(msg.Id, jb.ToString());
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
                    // クライアントから届いたメッシュを新規オブジェクトとして足す。
                    // 直接足さず、AddGeneratedMeshCommand を要求者の操作としてディスパッチャへ流す
                    // （担当者判定・Undo・操作者の記録を他のコマンドと揃える。操作経路統一計画.md R-3）。
                    var meshObject = RemoteBinarySerializer.Deserialize(data);
                    var proj = Context?.Project;
                    if (meshObject == null || proj == null) return null;

                    var placement = PrimitivePlacement.Default;
                    placement.AddMode     = Poly_Ling.Player.PrimitiveAddMode.NewObject;
                    placement.KeepAsGroup = false;

                    var addCmd = new AddGeneratedMeshCommand(
                        proj.CurrentModelIndex, meshObject, "RemoteMesh", placement, poseAlreadyBaked: true);
                    var addResult = DispatchWithSelectionOf(
                        requesterName, addCmd, CommandActor.Remote(requesterName, null));

                    Log($"メッシュ作成: V={meshObject.VertexCount} F={meshObject.FaceCount} user=\"{requesterName}\" → {addResult}");
                    CheckOwnershipChanged();
                    return null;
                }
                case BinaryMessageType.PositionsOnly:
                {
                    // 安定 ID の無い旧形式（v1）は対象を運べない。先頭描画メッシュへ当てると
                    // 取り違えて書き込むため受けない（操作経路統一計画.md R-2）。
                    if (!h.HasTarget)
                    {
                        Log("位置更新を拒否: 対象の安定 ID が無い旧形式です");
                        return null;
                    }

                    var targetCtx = ResolveBinaryTarget(h, out string resolveNote);
                    if (targetCtx?.MeshObject == null)
                    {
                        Log($"位置更新: 対象を解決できません（{resolveNote}）");
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

                    if (!TryFindMeshIndex(targetCtx, out int posModel, out int posIndex))
                    {
                        Log($"位置更新: 対象の索引を引けません（{targetCtx.Name}）");
                        return null;
                    }

                    // 届いた位置を写しへ読み、SetVertexPositionsCommand として要求者の操作で流す
                    // （担当者判定・ミラー側・ロックの延長・Undo・記録を他のコマンドと揃える。R-1）。
                    var received = targetCtx.MeshObject.Clone();
                    RemoteBinarySerializer.Deserialize(data, received);
                    int vc = received.VertexCount;
                    var vIdx = new int[vc];
                    var pos  = new float[vc * 3];
                    for (int i = 0; i < vc; i++)
                    {
                        vIdx[i] = i;
                        var p = received.Vertices[i].Position;
                        pos[i * 3] = p.x; pos[i * 3 + 1] = p.y; pos[i * 3 + 2] = p.z;
                    }

                    var posCmd = new SetVertexPositionsCommand(posModel, posIndex, vIdx, pos);
                    var posResult = DispatchWithSelectionOf(
                        requesterName, posCmd,
                        CommandActor.Remote(requesterName, new[] { targetCtx.ObjectId }));

                    Log($"位置更新: {targetCtx.Name} ({resolveNote}) user=\"{requesterName}\" → {posResult}");
                    CheckOwnershipChanged();
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
        /// MeshContext が属するモデルの番号と、そのモデル内の masterIndex を引く。
        /// </summary>
        private bool TryFindMeshIndex(MeshContext target, out int modelIndex, out int masterIndex)
        {
            modelIndex = -1; masterIndex = -1;
            var proj = Context?.Project;
            if (proj == null || target == null) return false;
            for (int mi = 0; mi < proj.ModelCount; mi++)
            {
                var m = proj.GetModel(mi);
                if (m == null) continue;
                for (int i = 0; i < m.MeshContextCount; i++)
                {
                    if (!ReferenceEquals(m.GetMeshContext(i), target)) continue;
                    modelIndex = mi; masterIndex = i;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// バイナリヘッダから適用対象の MeshContext を解決する。
        ///
        /// v2 かつ ObjectId!=0 → 安定IDで検索（ModelIndex を優先し、外れたら全モデル走査）。
        /// それ以外              → null（対象を決められない）。
        ///
        /// 全モデル走査まで行うのは、送信側と受信側で CurrentModelIndex がずれていても
        /// オブジェクトさえ一致すれば正しく当てられるようにするため。
        /// </summary>
        private MeshContext ResolveBinaryTarget(BinaryHeader h, out string note)
        {
            // 安定 ID の無いものは対象を決められない（先頭描画メッシュへ当てる代替は廃止。R-2）。
            if (!h.HasTarget)
            {
                note = "対象の安定 ID なし";
                return null;
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
