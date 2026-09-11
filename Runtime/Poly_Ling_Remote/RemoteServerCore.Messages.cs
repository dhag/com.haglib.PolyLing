// RemoteServerCore.Messages.cs
// リモートサーバ：メッセージ処理（クエリ・コマンド）とバッチフレームの組み立て。
// Runtime/Poly_Ling_Remote/ に配置

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
    public partial class RemoteServerCore
    {
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
                // 受信コマンドは全て PanelCommand 経由で処理する。
                return ProcessCommandViaPanelCommand(msg, channel);
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
            PanelCommand cmd = BuildPanelCommand(msg, modelIndex, out string buildError);
            if (cmd == null)
            {
                string reason = string.IsNullOrEmpty(buildError)
                    ? $"Unknown action: {msg.Action}" : buildError;
                Log($"組み立て失敗: {reason}");
                return BuildErrorResponse(msg.Id, reason);
            }

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
            CommandResult result = DispatchWithSelectionOf(requester, cmd);

            Log($"cmd: {msg.Action} model={modelIndex} user=\"{requester}\" → {result}");

            // 担当が動いた可能性があるので差分があれば全体へ通知
            CheckOwnershipChanged();

            if (!result.Success)
                return BuildErrorResponse(msg.Id, result.Reason);

            return BuildSuccessResponse(msg.Id, BuildResultJson(result));
        }

        /// <summary>実行結果を応答の data 部へ載せる JSON にする。</summary>
        private static string BuildResultJson(CommandResult result)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            if (result.MasterIndices != null && result.MasterIndices.Length > 0)
                jb.KeyValue("masterIndices", string.Join(",", result.MasterIndices));
            if (result.ObjectIds != null && result.ObjectIds.Length > 0)
                jb.KeyValue("objectIds", string.Join(",", result.ObjectIds));
            jb.EndObject();
            return jb.ToString();
        }

        /// <summary>
        /// 要求者の選択を ModelContext へ一時適用してコマンドを実行し、
        /// 実行後にホストの選択へ戻す。
        /// 差し替えが起きた場合はホストUIが他ユーザーの選択で描画されているため、
        /// 復帰後に再同期を要求する。
        /// </summary>
        /// <summary>
        /// DispatchCommand を呼び、結果を必ず非 null で返す。
        /// null が返るのは配線側の不備なので、成功に丸めず失敗として扱う。
        /// </summary>
        private CommandResult Invoke(PanelCommand cmd)
            => DispatchCommand(cmd) ?? CommandResult.Fail("dispatcher returned no result");

        private CommandResult DispatchWithSelectionOf(string userName, PanelCommand cmd)
        {
            var model = Context?.Model;
            var slot  = _selectionStore.Find(userName);

            if (model == null || slot == null)
                return Invoke(cmd);

            var proj = Context?.Project;
            int mi = proj?.CurrentModelIndex ?? 0;

            bool swapped;
            CommandResult result;
            using (var scope = SelectionScope.Apply(model, mi, slot))
            {
                swapped = scope.Swapped;
                result  = Invoke(cmd);
            }

            if (swapped)
                (RequestPanelRefresh ?? OnRepaint)?.Invoke();

            return result;
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
        /// <summary>
        /// action と params から PanelCommand を作る。
        ///
        /// 組み立ての規則はどのコマンドでも同じなので、本体は
        /// PanelCommandFactory がリフレクションで行う。コマンドを 1 本足しても
        /// ここは触らなくてよい。
        ///
        /// ここに手書きで残すのは、規則で書けない 2 件だけ。
        ///   selectMesh … 単数の index を受ける後方互換がある
        ///                （RemoteHtmlClient.cs:284-285 が index だけを送る）
        ///   applyBlend … BlendSourceSpec[] 1 本を 3 列に分けて送っている
        ///                （PanelCommandRouter.cs:145-147）
        ///
        /// undo / redo は型名と action 名がずれるだけなので、
        /// PanelCommandFactory.ActionAliases が引き受ける。
        /// </summary>
        private static PanelCommand BuildPanelCommand(
            RemoteMessage msg, int modelIndex, out string error)
        {
            error = null;

            switch (msg.Action)
            {
                // ── 選択（単数 index の後方互換） ──────────────────────
                case "selectMesh":
                {
                    var indices = GetIndices(msg, "indices");
                    if (indices.Length == 0)
                    {
                        int idx = GetParamInt(msg, "index", -1);
                        indices = idx >= 0 ? new[] { idx } : System.Array.Empty<int>();
                    }
                    var category = (MeshCategory)GetParamInt(msg, "category", (int)MeshCategory.Drawable);
                    return new SelectMeshCommand(modelIndex, category, indices);
                }

                // ── メッシュブレンド（ソース 3 列の組み直し） ──────────
                // ソースの 3 列は同じ並びで届く。長さが揃わない要求は
                // 対応関係が決まらないので短いほうに合わせて切る。
                case "applyBlend":
                {
                    var srcModels  = GetIndices(msg, "srcModelIndices");
                    var srcMasters = GetIndices(msg, "srcMasterIndices");
                    var srcWeights = GetFloats(msg, "srcWeights");

                    int n = Math.Min(srcModels.Length, Math.Min(srcMasters.Length, srcWeights.Length));
                    if (n > ApplyBlendCommand.MaxSources) n = ApplyBlendCommand.MaxSources;

                    // ApplyBlendCommand は平行配列で受けるので、
                    // ここで BlendSourceSpec[] へ束ねる必要はない。長さは n で揃える。
                    var srcModelsN  = new int[n];
                    var srcMastersN = new int[n];
                    var srcWeightsN = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        srcModelsN[i]  = srcModels[i];
                        srcMastersN[i] = srcMasters[i];
                        srcWeightsN[i] = srcWeights[i];
                    }

                    return new ApplyBlendCommand(
                        modelIndex,
                        srcModelsN, srcMastersN, srcWeightsN,
                        GetParamInt(msg, "destMasterIndex", -1),
                        GetParamString(msg, "createNewObject", "false") == "true",
                        GetParamString(msg, "recalcNormals",   "true")  == "true",
                        GetParamString(msg, "selectedOnly",    "false") == "true",
                        (Poly_Ling.UI.BlendMatchMode)GetParamInt(
                            msg, "matchMode", (int)Poly_Ling.UI.BlendMatchMode.Index));
                }
            }

            // 一般化した経路。作れない理由は呼び出し元へ返し、応答に載せる。
            return PanelCommandFactory.Create(msg.Action, modelIndex, msg.Params, out error);
        }

        /// <summary>
        /// int[] パラメータ（"1,2,3" 形式）。
        /// 手書きで残した 2 件が使う。一般化した経路は PanelCommandFactory が読む。
        /// </summary>
        private static int[] GetIndices(RemoteMessage msg, string key)
        {
            if (msg.Params == null || !msg.Params.TryGetValue(key, out var s) || string.IsNullOrEmpty(s))
                return System.Array.Empty<int>();
            var parts = s.Split(',');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                int.TryParse(parts[i].Trim(), out result[i]);
            return result;
        }

        /// <summary>
        /// float[] パラメータ。送信側（PanelCommandRouter.FloatCsv）が
        /// InvariantCulture で書くので、読む側もロケールを固定する。
        /// </summary>
        private static float[] GetFloats(RemoteMessage msg, string key)
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
    }
}
