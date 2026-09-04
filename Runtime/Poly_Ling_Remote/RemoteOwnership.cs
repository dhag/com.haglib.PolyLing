// Remote/RemoteOwnership.cs
// 協働編集（グループワーク）のための所有権判定。
//
// 【真値の所在】
//   「誰が担当か」は MeshContext.EditorName が単一の真値（Single Source of Truth）。
//   サーバは別レジストリを持たない。理由:
//     - 担当はプロジェクト保存に含まれる永続情報であり、接続状態と寿命が違う
//     - 二重管理にすると保存/読込・Undo とレジストリがずれる
//   サーバが持つ揮発情報は「どのチャネルがどのユーザー名で register したか」だけで、
//   これは既存の RemoteServerCore._clientRegistry が担う。
//
// 【判定規則】
//   書き込み可 ⟺ EditorName が空（担当者なし） または EditorName == 要求者名
//   担当者なしの編集を禁止したい運用では AllowUnownedEdit=false にする。
//
// 【切断時】
//   担当は解放しない（手動 claim / release 運用のため）。
//   放置された担当は ホスト側の「強制解放」または本人の release で外す。
//
// 【ズレ検出】
//   クライアントは masterIndices と対にして objectIds（安定ID）を送る。
//   サーバは「その位置に本当にそのIDのオブジェクトがあるか」を照合し、
//   食い違えばコマンド全体を拒否して再取得を促す。
//   これが無いと、他人の追加/削除/並べ替え直後に届いた古いビュー由来の
//   インデックスが別オブジェクトへ適用されてしまう。

using System;
using System.Collections.Generic;
using System.Text;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Remote
{
    /// <summary>認可結果。</summary>
    public struct OwnershipVerdict
    {
        public bool   Allowed;
        /// <summary>拒否理由（クライアントへ返すメッセージ）。</summary>
        public string Reason;
        /// <summary>リスト構造のズレを検出した（クライアントに再取得させるべき）。</summary>
        public bool   StaleView;

        public static OwnershipVerdict Ok => new OwnershipVerdict { Allowed = true };

        public static OwnershipVerdict Deny(string reason)
            => new OwnershipVerdict { Allowed = false, Reason = reason };

        public static OwnershipVerdict Stale(string reason)
            => new OwnershipVerdict { Allowed = false, Reason = reason, StaleView = true };
    }

    /// <summary>
    /// PanelCommand が触るオブジェクトを解決し、要求者が編集してよいかを判定する。
    /// 状態を持たない純関数の集まり。
    /// </summary>
    public static class RemoteOwnership
    {
        // ================================================================
        // 設定
        // ================================================================

        /// <summary>
        /// 担当者が設定されていないオブジェクトを誰でも編集できるか。
        /// false にすると claim 必須の厳格運用になる。
        /// </summary>
        public static bool AllowUnownedEdit = true;

        /// <summary>
        /// ユーザー名未登録（register で userName を送っていない）クライアントの
        /// 書き込みを許可するか。false 推奨（名無しの編集は追跡できないため）。
        /// </summary>
        public static bool AllowAnonymousEdit = false;

        // ================================================================
        // 本体
        // ================================================================

        /// <summary>
        /// コマンドの実行可否を判定する。
        /// </summary>
        /// <param name="project">サーバが保持する権威プロジェクト</param>
        /// <param name="cmd">実行しようとしているコマンド</param>
        /// <param name="requesterName">register 済みユーザー名（空＝名無し）</param>
        /// <param name="objectIds">クライアントが申告した安定ID（順序は cmd の masterIndices と対応、null 可）</param>
        public static OwnershipVerdict TryAuthorize(
            ProjectContext project, PanelCommand cmd, string requesterName, ulong[] objectIds)
        {
            if (cmd == null) return OwnershipVerdict.Deny("コマンドがありません");

            // 編集者の設定・解放そのものは専用判定へ
            if (cmd is SetObjectEditorCommand sec)
                return AuthorizeSetEditor(project, sec, requesterName, objectIds);

            // 読み取り専用・所有権と無関係なコマンドは素通し
            if (IsOwnershipExempt(cmd)) return OwnershipVerdict.Ok;

            if (string.IsNullOrEmpty(requesterName) && !AllowAnonymousEdit)
                return OwnershipVerdict.Deny(
                    "ユーザー名が未登録です。名前を設定して接続し直してください。");

            var model = GetModel(project, cmd.ModelIndex);
            if (model == null) return OwnershipVerdict.Deny($"モデルがありません: {cmd.ModelIndex}");

            int[] targets = ResolveTargets(model, cmd);

            // 対象を特定できないコマンド（モデル全体に効くもの等）は
            // 「そのモデルに他人の担当が1つでもあれば拒否」の保守的判定にする。
            if (targets == null)
                return AuthorizeModelWide(model, cmd, requesterName);

            if (targets.Length == 0) return OwnershipVerdict.Ok;

            // 安定IDの照合（ズレ検出）
            var stale = VerifyObjectIds(model, targets, objectIds);
            if (!stale.Allowed) return stale;

            // 担当者チェック
            var blocked = new List<string>();
            foreach (int idx in targets)
            {
                var mc = GetMesh(model, idx);
                if (mc == null) continue;

                if (!AllowUnownedEdit && !mc.HasEditor)
                {
                    blocked.Add($"{mc.Name}（担当者未設定）");
                    continue;
                }
                if (!mc.IsEditableBy(requesterName))
                    blocked.Add($"{mc.Name}（担当: {mc.EditorName}）");
            }

            if (blocked.Count > 0)
                return OwnershipVerdict.Deny("編集できません → " + Join(blocked, 3));

            return OwnershipVerdict.Ok;
        }

        // ================================================================
        // 編集者の設定・解放の判定
        // ================================================================

        private static OwnershipVerdict AuthorizeSetEditor(
            ProjectContext project, SetObjectEditorCommand cmd, string requesterName, ulong[] objectIds)
        {
            if (string.IsNullOrEmpty(requesterName) && !AllowAnonymousEdit)
                return OwnershipVerdict.Deny(
                    "ユーザー名が未登録です。名前を設定して接続し直してください。");

            var model = GetModel(project, cmd.ModelIndex);
            if (model == null) return OwnershipVerdict.Deny($"モデルがありません: {cmd.ModelIndex}");

            // リモートからの強制上書きは認めない（ホストのローカル操作のみ）
            if (cmd.Force)
                return OwnershipVerdict.Deny("強制解放はホスト側でのみ実行できます。");

            // 自分以外の名前を勝手に設定させない（解放は "" なので対象外）
            if (cmd.EditorName.Length > 0 &&
                !string.Equals(cmd.EditorName, requesterName, StringComparison.Ordinal))
                return OwnershipVerdict.Deny("他のユーザー名を設定することはできません。");

            var ids = objectIds ?? cmd.ObjectIds;
            var stale = VerifyObjectIds(model, cmd.MasterIndices, ids);
            if (!stale.Allowed) return stale;

            // 取得は「担当者なし」または「既に自分」のときのみ。
            // 解放は「自分が担当」のときのみ。
            var blocked = new List<string>();
            foreach (int idx in cmd.MasterIndices)
            {
                var mc = GetMesh(model, idx);
                if (mc == null) continue;
                if (!mc.IsEditableBy(requesterName))
                    blocked.Add($"{mc.Name}（担当: {mc.EditorName}）");
            }

            if (blocked.Count > 0)
                return OwnershipVerdict.Deny(
                    (cmd.EditorName.Length == 0 ? "解放できません → " : "取得できません → ")
                    + Join(blocked, 3));

            return OwnershipVerdict.Ok;
        }

        // ================================================================
        // 安定IDの照合
        // ================================================================

        /// <summary>
        /// masterIndices[i] の位置にあるオブジェクトの ObjectId が
        /// objectIds[i] と一致するかを確認する。
        /// objectIds が null / 該当要素が 0 の場合は照合しない（旧クライアント互換）。
        /// </summary>
        public static OwnershipVerdict VerifyObjectIds(
            ModelContext model, int[] masterIndices, ulong[] objectIds)
        {
            if (model == null || masterIndices == null || objectIds == null)
                return OwnershipVerdict.Ok;

            int n = Math.Min(masterIndices.Length, objectIds.Length);
            for (int i = 0; i < n; i++)
            {
                ulong expected = objectIds[i];
                if (expected == 0UL) continue;   // 未申告

                var mc = GetMesh(model, masterIndices[i]);
                if (mc == null || mc.ObjectId != expected)
                    return OwnershipVerdict.Stale(
                        "リスト構造が変化しています。最新の状態を取得してからやり直してください。");
            }
            return OwnershipVerdict.Ok;
        }

        // ================================================================
        // コマンド → 対象オブジェクト（MasterIndex）の解決
        // ================================================================

        /// <summary>
        /// コマンドが書き換える対象の MasterIndex 配列を返す。
        /// 対象を静的に決められないコマンドは null（＝モデル全体判定へ回す）。
        /// 空配列は「対象なし＝素通し」。
        /// </summary>
        public static int[] ResolveTargets(ModelContext model, PanelCommand cmd)
        {
            switch (cmd)
            {
                // ── 単体指定 ──────────────────────────────────────────
                case ToggleVisibilityCommand c: return One(c.MasterIndex);
                case ToggleLockCommand       c: return One(c.MasterIndex);
                case CycleMirrorTypeCommand  c: return One(c.MasterIndex);
                case RenameMeshCommand       c: return One(c.MasterIndex);
                case SetMeshFoldingCommand   c: return One(c.MasterIndex);

                // ── 複数指定 ──────────────────────────────────────────
                case SetBatchVisibilityCommand   c: return c.MasterIndices;
                case DeleteMeshesCommand         c: return c.MasterIndices;
                case InitBonePoseCommand         c: return c.MasterIndices;
                case SetBonePoseActiveCommand    c: return c.MasterIndices;
                case ResetBonePoseLayersCommand  c: return c.MasterIndices;
                case BakePoseToBindPoseCommand   c: return c.MasterIndices;
                case SetIgnorePoseCommand        c: return c.MasterIndices;

                // 原点だけ移動。対象の頂点と BoneTransform を書き換えるので担当判定が要る。
                // 登録しないと default に落ちて AuthorizeModelWide 送りになり、
                // 同じモデル内に他人の担当が 1 つあるだけで実行できなくなる。
                case MovePivotCommand            c: return c.MasterIndices;

                // 選択頂点の移動。対象メッシュの頂点を書き換えるので担当判定が要る。
                case MoveSelectedVerticesCommand c: return c.MasterIndices;

                // スカルプトストローク。対象メッシュの頂点を書き換える。
                case SculptStrokeCommand         c: return c.MasterIndices;

                // 位相編集（パラメータを持たない実行系）。
                // 対象メッシュの面・頂点を書き換えるので担当判定が要る。
                // 登録しないと default に落ちて AuthorizeModelWide 送りになり、
                // 同じモデル内に他人の担当が 1 つあるだけで実行できなくなる。
                case FaceMergeCommand            c: return c.MasterIndices;
                case FaceMergeCollapseCommand    c: return c.MasterIndices;
                case Quad4To1Command             c: return c.MasterIndices;
                case Tri4To1Command              c: return c.MasterIndices;
                case VertexDissolveCommand       c: return c.MasterIndices;
                case SplitVerticesCommand        c: return c.MasterIndices;

                // 位相・頂点編集（パラメータを持つ実行系）。同上。
                case VertexHoleCommand           c: return c.MasterIndices;
                case FlipFaceCommand             c: return c.MasterIndices;
                case AlignVerticesCommand        c: return c.MasterIndices;
                case SmoothEdgesCommand          c: return c.MasterIndices;
                case PlanarizeAlongBonesCommand  c: return c.MasterIndices;
                case MergeVerticesCommand        c: return c.MasterIndices;

                // 位相・頂点編集（対象や生成先の指定を伴う実行系）。同上。
                // SurfaceSnap のリファレンスは読むだけなので担当判定に含めない。
                // PlaceObjectReshape の原型も同じく読むだけ。
                case DeleteSelectionCommand      c: return c.MasterIndices;
                case PipeAlignCommand            c: return c.MasterIndices;
                case PlaceObjectReshapeCommand   c: return c.MasterIndices;
                case SolidifyCommand             c: return c.MasterIndices;
                case LineExtrudeCommand          c: return c.MasterIndices;
                case SurfaceSnapCommand          c: return c.MasterIndices;

                // ドラッグ確定（ベベル・押し出し）。対象メッシュの頂点と面を書き換える。
                case EdgeBevelCommand            c: return c.MasterIndices;
                case EdgeExtrudeCommand          c: return c.MasterIndices;
                case FaceExtrudeCommand          c: return c.MasterIndices;

                // スキンウェイト塗り。対象メッシュの BoneWeight を書き換える。
                case SkinWeightPaintCommand      c: return c.MasterIndices;

                // メッシュブレンドの書き込み先は宛先 1 件。
                // 登録しないと default に落ちて AuthorizeModelWide 送りになり、
                // 同じモデル内に他人の担当が 1 つあるだけで実行できなくなる。
                // ソースは読むだけなので担当判定の対象に含めない（別モデルも指せる）。
                // CreateNewObject のときは既存メッシュを書き換えず複製を足すだけなので、
                // 追加系（DuplicateMeshesCommand）と同じく担当と無関係とみなす。
                case ApplyBlendCommand           c:
                    return c.CreateNewObject ? Array.Empty<int>() : One(c.DestMasterIndex);

                // ── 読むだけ／新規作成なので担当と無関係 ──────────────
                // 作業軸はモデルの頂点・選択を書き換えない。
                case SetWorkAxisCommand     _: return Array.Empty<int>();
                case RecallWorkAxisCommand  _: return Array.Empty<int>();

                case SelectMeshCommand      _: return Array.Empty<int>();
                case SelectElementsCommand  _: return Array.Empty<int>();
                case AdvancedSelectCommand  _: return Array.Empty<int>();
                case AdvancedSelectByAttributeCommand _: return Array.Empty<int>();
                case DuplicateMeshesCommand _: return Array.Empty<int>();
                case AddMeshCommand         _: return Array.Empty<int>();
                case SwitchModelCommand     _: return Array.Empty<int>();

                default:
                    return null;   // 不明＝モデル全体判定
            }
        }

        /// <summary>
        /// 対象を特定できないコマンドの保守的判定。
        /// モデル内に「他人の担当」が1つでもあれば拒否する。
        /// </summary>
        private static OwnershipVerdict AuthorizeModelWide(
            ModelContext model, PanelCommand cmd, string requesterName)
        {
            int count = model.MeshContextCount;
            for (int i = 0; i < count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || !mc.HasEditor) continue;
                if (!mc.IsEditableBy(requesterName))
                    return OwnershipVerdict.Deny(
                        $"このモデルには他ユーザーの担当オブジェクトがあります"
                        + $"（例: {mc.Name} → {mc.EditorName}）。"
                        + $"{cmd.GetType().Name} はモデル全体に影響するため実行できません。");
            }
            return OwnershipVerdict.Ok;
        }

        /// <summary>所有権判定を通す必要のないコマンドか。</summary>
        private static bool IsOwnershipExempt(PanelCommand cmd)
        {
            switch (cmd)
            {
                case SelectMeshCommand _:
                case SwitchModelCommand _:
                case NotifyListStructureChangedCommand _:
                case NotifyDictionaryChangedCommand _:
                    return true;
                default:
                    return false;
            }
        }

        // ================================================================
        // 担当状況のスナップショット（push 用）
        // ================================================================

        /// <summary>
        /// 現在の担当状況を JSON 配列にする。
        /// 形式: [{"id":"12345","index":3,"name":"頭","editor":"hagihara"}, ...]
        /// 担当者なしのオブジェクトは含めない。
        /// </summary>
        public static string BuildOwnershipJson(ModelContext model, int modelIndex)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex", modelIndex);
            jb.Key("owners").BeginArray();

            if (model != null)
            {
                int count = model.MeshContextCount;
                for (int i = 0; i < count; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null || !mc.HasEditor) continue;
                    jb.BeginObject();
                    // ulong は JSON の数値精度を超えうるので文字列で送る
                    jb.KeyValue("id",     mc.ObjectId.ToString());
                    jb.KeyValue("index",  i);
                    jb.KeyValue("name",   mc.Name ?? "");
                    jb.KeyValue("editor", mc.EditorName ?? "");
                    jb.EndObject();
                }
            }

            jb.EndArray();
            jb.EndObject();
            return jb.ToString();
        }

        /// <summary>担当状況の変化検出用シグネチャ（push の抑止に使う）。</summary>
        public static string BuildOwnershipSignature(ModelContext model, int modelIndex)
        {
            var sb = new StringBuilder();
            sb.Append(modelIndex).Append('|');
            if (model == null) return sb.ToString();

            int count = model.MeshContextCount;
            for (int i = 0; i < count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || !mc.HasEditor) continue;
                sb.Append(mc.ObjectId).Append(':').Append(mc.EditorName).Append(';');
            }
            return sb.ToString();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static int[] One(int i) => new[] { i };

        private static ModelContext GetModel(ProjectContext project, int modelIndex)
        {
            if (project == null) return null;
            if (modelIndex < 0 || modelIndex >= project.ModelCount) return null;
            return project.Models[modelIndex];
        }

        private static MeshContext GetMesh(ModelContext model, int masterIndex)
        {
            if (model == null) return null;
            if (masterIndex < 0 || masterIndex >= model.MeshContextCount) return null;
            return model.GetMeshContext(masterIndex);
        }

        private static string Join(List<string> items, int max)
        {
            if (items == null || items.Count == 0) return "";
            var sb = new StringBuilder();
            int n = Math.Min(items.Count, max);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(items[i]);
            }
            if (items.Count > n) sb.Append($" ほか{items.Count - n}件");
            return sb.ToString();
        }

        /// <summary>CSV文字列 "1,2,3" を ulong[] に。空なら null。</summary>
        public static ulong[] ParseIdCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return null;
            var parts = csv.Split(',');
            var result = new ulong[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                ulong.TryParse(parts[i].Trim(), out result[i]);
            return result;
        }

        /// <summary>ulong[] を CSV 文字列に。</summary>
        public static string ToIdCsv(ulong[] ids)
        {
            if (ids == null || ids.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ids[i]);
            }
            return sb.ToString();
        }
    }
}
