// RemoteServerCore.Push.cs
// リモートサーバ：Push イベント・担当変更／選択変更の push・受信・クライアントタイプ登録・レスポンス・ヘルパー。
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
