// ShapeMqoOps.cs
// 形と MQO の連携（PolyLing_モーフ整理計画.md 3.8・段 4）：対応付け・形へ読み込む・MQO へ書き出す。
// Runtime/Poly_Ling_Main/MQO/ に配置
//
// 【読み込みは今の形へ】
//   既存の頂点位置差し替え（MQOPartialImportOps.ExecuteVertexPositionImport）で Vertex.Position を書く。
//   Position は今の形の位置なので、MQO に入っているオブジェクトだけが今の形でその位置になり、
//   入っていないオブジェクトは触らない（基本の形から作った形なら基本の形のまま）。
//
// 【対応付けの順】
//   ① 覚えている組（ShapeSource.Pairs、または呼び出し側が渡した組）を名前で引く
//   ② 名前の一致（両側で同じ名前が 1 つずつのときだけ）
//   ③ 展開頂点数の一致（両側とも相手が 1 つに決まるときだけ）
//   頂点数の合い方は 2 通り（Fits）。MQO から読んだモデル（展開していない）は頂点数が MQO と同じで、
//   頂点番号どうしで写す。PMX から読んだモデル（展開済み）は展開頂点数が MQO と同じで、展開順で写す
//   （写し方は MQOPartialImportOps.TransferVertexAttributes・MQOPartialExportOps.TransferToMQO）。
//   ①②で組めても頂点数が合わなければ読まない（Mismatched）。どれでも組めない MQO のオブジェクトは
//   読まない（Unresolved）。モデル側で組まれなかったオブジェクトは触らないのが正しいので数えない。
//
// 【基本の形を差し替えるとき】（今の形が基本の形）
//   形は絶対位置で持つので、基本の形だけが動くと「形 − 基本の形」が変わる。
//   keepShapeOffsets = true（既定）… 位置が変わった頂点の、形の控え全部へ「新しい位置 − 古い位置」を足す。
//                                    控えを持たない頂点（その形でも基本と同じ）は位置について動くので、
//                                    動かしていない頂点が「形で動いた頂点」になることはない
//                                    （ShapeOps.cs の「一括の変換」の注記）。
//   keepShapeOffsets = false … 控えはそのまま（形は絶対位置のまま残る）。新しい基本の位置と
//                               同じになった控えだけ消す（持たない＝基本と同じ、の決まりに合わせる）。
//
// 【法線】形ごとに持たない（計画書 3.13）。読み込みで法線は再計算しない。
//

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.MQO
{
    /// <summary>対応付けの結果。Models[i] と Mqos[i] が 1 組。</summary>
    public class ShapeMqoMatch
    {
        public readonly List<PartialMeshEntry> Models = new List<PartialMeshEntry>();
        public readonly List<PartialMQOEntry>  Mqos   = new List<PartialMQOEntry>();
        /// <summary>組の決め方（"remembered" / "name" / "vertexCount"）。Models と同じ並び。</summary>
        public readonly List<string> How = new List<string>();
        /// <summary>組めなかった MQO のオブジェクトの名前。</summary>
        public readonly List<string> Unresolved = new List<string>();
        /// <summary>組めたが展開頂点数が合わなかったもの（"MQO名(数) / モデル名(数)"）。</summary>
        public readonly List<string> Mismatched = new List<string>();

        public int Count => Models.Count;

        /// <summary>覚えておく組（名前）。</summary>
        public List<ShapeSourcePair> ToPairs()
        {
            var list = new List<ShapeSourcePair>(Models.Count);
            for (int i = 0; i < Models.Count; i++) list.Add(new ShapeSourcePair(Models[i].Name, Mqos[i].Name));
            return list;
        }
    }

    public static class ShapeMqoOps
    {
        /// <summary>
        /// MQO を読み、モデル側の一覧も作った使い捨てのヘルパを返す。
        /// 一覧の条件は部分読み込みパネルと同じ（焼き込みミラーは対にする・名前が + のミラーは外す）。
        /// visibleOnly は読み込みで true（非表示は読まない）、書き出しで false（全オブジェクトを書き戻す文書）。
        /// </summary>
        public static bool TryLoad(ModelContext model, string resolvedPath, bool visibleOnly,
                                   out MQOPartialMatchHelper helper, out string reason)
        {
            reason = null;
            helper = new MQOPartialMatchHelper();
            if (!helper.LoadMQO(resolvedPath, visibleOnly) || helper.MQODocument == null)
            { reason = $"MQO を読めません: {resolvedPath}"; return false; }
            if (helper.MQOObjects.Count == 0)
            { reason = "MQO から取り込めるオブジェクトが 0 件です"; return false; }

            helper.BuildModelList(model, skipBakedMirror: true, skipNamedMirror: true, pairMirrors: true);
            if (helper.ModelMeshes.Count == 0)
            { reason = "モデル側の描画オブジェクトが 0 件です"; return false; }
            return true;
        }

        // ================================================================
        // 対応付け
        // ================================================================

        /// <summary>① 覚えた組 → ② 名前 → ③ 展開頂点数 の順で組む（冒頭の注記）。</summary>
        public static ShapeMqoMatch Match(MQOPartialMatchHelper helper, IList<ShapeSourcePair> remembered)
        {
            var r = new ShapeMqoMatch();
            var usedModel = new HashSet<PartialMeshEntry>();
            var usedMqo   = new HashSet<PartialMQOEntry>();
            var models = helper.ModelMeshes;
            var mqos   = helper.MQOObjects;

            void TryAdd(PartialMeshEntry m, PartialMQOEntry q, string how)
            {
                usedMqo.Add(q);
                if (!Fits(m, q))
                {
                    r.Mismatched.Add($"{q.Name}(頂点 {q.VertexCount}・展開 {q.ExpandedVertexCountWithMirror}) / " +
                                     $"{m.Name}(頂点 {m.VertexCount}・展開 {m.TotalExpandedVertexCount})");
                    return;
                }
                usedModel.Add(m);
                r.Models.Add(m); r.Mqos.Add(q); r.How.Add(how);
            }

            // ① 覚えた組
            if (remembered != null)
            {
                foreach (var p in remembered)
                {
                    var q = Single(mqos, e => !usedMqo.Contains(e) && e.Name == p.Mqo);
                    var m = Single(models, e => !usedModel.Contains(e) && e.Name == p.Model);
                    if (q != null && m != null) TryAdd(m, q, "remembered");
                }
            }

            // ② 名前の一致
            foreach (var q in mqos)
            {
                if (usedMqo.Contains(q) || string.IsNullOrEmpty(q.Name)) continue;
                if (Count(mqos, e => !usedMqo.Contains(e) && e.Name == q.Name) != 1) continue;
                var m = Single(models, e => !usedModel.Contains(e) && e.Name == q.Name);
                if (m != null) TryAdd(m, q, "name");
            }

            // ③ 頂点数の一致（両側とも 1 つに決まるときだけ）
            foreach (var q in mqos)
            {
                if (usedMqo.Contains(q) || q.VertexCount <= 0) continue;
                var m = Single(models, e => !usedModel.Contains(e) && Fits(e, q));
                if (m == null) continue;
                if (Count(mqos, e => !usedMqo.Contains(e) && Fits(m, e)) != 1) continue;
                TryAdd(m, q, "vertexCount");
            }

            foreach (var q in mqos)
                if (!usedMqo.Contains(q)) r.Unresolved.Add(q.Name ?? "");
            return r;
        }

        /// <summary>
        /// 組の頂点数が合うか。読み込みの写し方（MQOPartialImportOps）と同じ 2 通り：
        ///   頂点番号で対応（MQO から読んだモデル）… 頂点数が同じ（焼き込みミラーの相手があれば相手も同じ）
        ///   展開順で対応（PMX から読んだモデル）  … 展開頂点数が同じ（ミラーは 2 倍で数える）
        /// </summary>
        public static bool Fits(PartialMeshEntry m, PartialMQOEntry q)
        {
            if (m == null || q == null) return false;
            if (m.VertexCount == q.VertexCount)
            {
                var peer = m.BakedMirrorPeer;
                return !q.IsMirrored || peer == null || peer.VertexCount == q.VertexCount;
            }
            return m.TotalExpandedVertexCount == q.ExpandedVertexCountWithMirror;
        }

        private static T Single<T>(List<T> list, System.Func<T, bool> pred) where T : class
        {
            T found = null;
            foreach (var e in list)
            {
                if (!pred(e)) continue;
                if (found != null) return null;   // 2 つ以上 → 決まらない
                found = e;
            }
            return found;
        }

        private static int Count<T>(List<T> list, System.Func<T, bool> pred)
        {
            int n = 0;
            foreach (var e in list) if (pred(e)) n++;
            return n;
        }

        // ================================================================
        // 形へ読み込む
        // ================================================================

        /// <summary>
        /// 組んだオブジェクトへ MQO の頂点位置を今の形として読む。書いた頂点の数を返す。
        /// 今の形が基本の形なら keepShapeOffsets に従って形の控えを直し、動かした控えの数を shiftedEntries に返す。
        /// </summary>
        public static int Import(ModelContext model, ShapeMqoMatch match, ShapeSource src,
                                 bool keepShapeOffsets, out int shiftedEntries, out int droppedEntries)
        {
            shiftedEntries = 0;
            droppedEntries = 0;
            if (model == null || match == null || match.Count == 0 || src == null) return 0;

            bool isBase = model.CurrentShapeId == ShapeOps.BaseShapeId;

            // 書き換わるメッシュ（組んだもの＋焼き込みミラーの相手）の、読む前の位置。
            var targets = new List<MeshObject>();
            foreach (var m in match.Models)
            {
                if (m.Context?.MeshObject != null) targets.Add(m.Context.MeshObject);
                var peer = m.BakedMirrorPeer?.Context?.MeshObject;
                if (peer != null) targets.Add(peer);
            }
            var before = new List<Vector3[]>(targets.Count);
            if (isBase)
                foreach (var mo in targets)
                {
                    var arr = new Vector3[mo.VertexCount];
                    for (int i = 0; i < arr.Length; i++) arr[i] = mo.Vertices[i].Position;
                    before.Add(arr);
                }

            var ops = new MQOPartialImportOps();
            int written = ops.ExecuteVertexPositionImport(
                match.Models, match.Mqos, src.Scale, new AxisFlip(src.FlipX, src.FlipZ),
                position: true, uv: false, flipUV_V: false);

            if (isBase)
            {
                for (int t = 0; t < targets.Count; t++)
                {
                    var mo = targets[t];
                    var old = before[t];
                    int n = Mathf.Min(old.Length, mo.VertexCount);
                    for (int i = 0; i < n; i++)
                    {
                        var v = mo.Vertices[i];
                        if (v.Shapes == null || ShapeOps.Same(v.Position, old[i])) continue;
                        if (keepShapeOffsets)
                        {
                            Vector3 delta = v.Position - old[i];
                            for (int k = 0; k < v.Shapes.Count; k++)
                            {
                                var e = v.Shapes[k];
                                v.Shapes[k] = new ShapePosition(e.ShapeId, e.Position + delta);
                                shiftedEntries++;
                            }
                        }
                        else
                        {
                            for (int k = v.Shapes.Count - 1; k >= 0; k--)
                            {
                                if (!ShapeOps.Same(v.Shapes[k].Position, v.Position)) continue;
                                v.RemoveShape(v.Shapes[k].ShapeId);
                                droppedEntries++;
                            }
                        }
                    }
                }
            }

            foreach (var mo in targets) mo.InvalidatePositionCache();
            return written;
        }

        // ================================================================
        // MQO へ書き出す
        // ================================================================

        /// <summary>
        /// 組んだオブジェクトの今の位置（今の形）を、読み込んだ MQO の文書へ書き戻す。書いた頂点の数を返す。
        /// 既存の部分書き出し（MQOPartialExportOps.ExecuteExport）を組ごとに呼ぶ
        /// （まとめて渡すと、焼き込みミラーの相手の頂点数の分だけ展開位置の数え方がずれるため）。
        /// 文書のファイルへの書き込みは呼び出し側（MQODocumentIO.WriteDocumentToFile）。
        /// </summary>
        public static int Export(MQOPartialMatchHelper helper, ShapeMqoMatch match, float scale, bool flipX, bool flipZ)
        {
            if (helper?.MQODocument == null || match == null) return 0;
            var ops = new MQOPartialExportOps();
            int total = 0;
            for (int i = 0; i < match.Count; i++)
            {
                total += ops.ExecuteExport(
                    new List<PartialMQOEntry>  { match.Mqos[i] },
                    new List<PartialMeshEntry> { match.Models[i] },
                    helper.MQODocument, scale, new AxisFlip(flipX, flipZ),
                    writeBackPosition: true, writeBackUV: false, writeBackBoneWeight: false);
            }
            return total;
        }
    }
}
