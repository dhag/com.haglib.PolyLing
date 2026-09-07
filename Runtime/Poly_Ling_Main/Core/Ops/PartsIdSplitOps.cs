// PartsIdSplitOps.cs
// パーツID（Vertex.PartsId）で 1 つのメッシュを分解し、パーツIDごとの MeshObject を作る。
// モデルへの挿入と階層づけは PartsIdSplitInserter が持つ。ここは純粋な分解だけを行う。
//
// 【分解の約束】
//   ・面は必ずどれか 1 つの出力へ入る。取りこぼしも多重化も起こさない。
//   ・頂点は重複してよい。1 つの頂点が複数の出力に現れる。
//   ・元のメッシュは 1 頂点も書き換えない。
//
// 【面をどこへ入れるか】
//   出力 P の候補になる面は「パーツIDが P の頂点を 1 つ以上含む面」。
//   1 つの面が複数の候補を持つときは、処理順で先に来る出力が取る。
//
//   全頂点は必ず int のパーツIDを持つので、どの面も候補を最低 1 つ持つ。
//   したがって処理順を最後まで回せば取りこぼしは原理的に発生しない。
//
// 【処理順】
//   予約値（PartsIdOps.UnweightedPartsId）を最後に回し、残りは降順。
//
//   PartsIdByBoneWeightOps の採番では「組み合わせのID > 単独ボーンのID」が
//   常に成り立つ（群の番号はボーン索引の定義域の外から始まる）。
//   降順に回すと組み合わせの出力が先に面を取るので、
//   例えば「4と5」の出力が、パーツID 4 や 5 の頂点も含む境界の面を受け取る。
//   昇順だと逆になり、境界の面が単独ボーンの出力へ取られる。
//
// 【頂点をどこへ入れるか】
//   (a) その出力が取った面が参照する頂点を全て複製する。
//       面の相手側がよそのパーツIDでも複製する（これが「多重にクローン」）。
//   (b) さらに、パーツIDが P の頂点で (a) に入らなかったものも複製する。
//       (a) だけだと、自分の面が 1 つも残らなかった頂点がどの出力にも現れず、
//       頂点の取りこぼしになる。境界の頂点で実際に起こりうる。
//
// 【引き継ぐもの / 引き継がないもの】
//   引き継ぐ … Vertex.Clone() が持つもの一式（Id / PartsId / SubId / UV / 法線 /
//               Flags / BoneWeight / MirrorBoneWeight / ControlPoints。
//               MeshObject.cs:309-324）と、Face.Clone() が持つもの一式
//               （Id / UVIndices / NormalIndices / MaterialIndex / Flags。
//               MeshObject.cs:544-555）。
//   引き継がない … MirrorBakeState / IKData / IKLink / RigidBodyData / JointData /
//                   SpringBoneColliders / SpringBoneJoint / SpringBoneChainRoot /
//                   NormalRecalcExcludeList。子は素のメッシュにする。
//
//   頂点IDは複製で重複するが、モデル内の重複は異常ではない（MeshObject.cs:80-89）。
//   頂点IDは人が管理するものなので、ここでは振り直さない。
//
// 【配置】 Runtime/Poly_Ling_Main/Core/Ops/

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>分解でできたメッシュ 1 つぶん。</summary>
    public struct PartsIdSplitPiece
    {
        /// <summary>このメッシュのパーツID。</summary>
        public int PartsId;

        /// <summary>切り出したメッシュ。名前はまだ入っていない。</summary>
        public MeshObject Mesh;
    }

    /// <summary>分解の実行結果。</summary>
    public struct PartsIdSplitResult
    {
        /// <summary>分解できたか。false なら 1 つも作っていない。</summary>
        public bool Success;

        /// <summary>元メッシュの頂点数。</summary>
        public int SourceVertexCount;

        /// <summary>元メッシュの面数。</summary>
        public int SourceFaceCount;

        /// <summary>作ったメッシュの数（＝出現したパーツIDの種類数）。</summary>
        public int PartCount;

        /// <summary>作ったメッシュの頂点数の合計（重複を含む）。</summary>
        public int OutputVertexCount;

        /// <summary>重複で増えた頂点数（OutputVertexCount − SourceVertexCount）。</summary>
        public int DuplicatedVertexCount;

        /// <summary>元メッシュで、どの面にも属していなかった頂点の数。</summary>
        public int LooseVertexCount;

        /// <summary>失敗の理由。成功時は空。</summary>
        public string Reason;

        public static PartsIdSplitResult Fail(string reason)
            => new PartsIdSplitResult { Success = false, Reason = reason ?? "" };

        public string Summary =>
            $"元 頂点 {SourceVertexCount} / 面 {SourceFaceCount}"
          + $" → {PartCount} 個"
          + $"（頂点 {OutputVertexCount}・重複 {DuplicatedVertexCount}）"
          + (LooseVertexCount > 0 ? $" / 面に属さない頂点 {LooseVertexCount}" : "");
    }

    /// <summary>パーツIDによるメッシュの分解。元メッシュは書き換えない。</summary>
    public static class PartsIdSplitOps
    {
        /// <summary>
        /// パーツIDごとにメッシュを切り出す。
        /// </summary>
        /// <param name="src">元メッシュ。書き換えない。</param>
        /// <param name="pieces">パーツIDの昇順で並べた切り出し結果。失敗時は空。</param>
        public static PartsIdSplitResult Split(MeshObject src, out List<PartsIdSplitPiece> pieces)
        {
            pieces = new List<PartsIdSplitPiece>();

            if (src == null || src.Vertices == null || src.Vertices.Count == 0)
                return PartsIdSplitResult.Fail("対象メッシュに頂点がありません");

            int n          = src.Vertices.Count;
            int faceCount  = src.Faces?.Count ?? 0;

            // ── 検査 ────────────────────────────────────────────────────
            // 壊れた面が 1 つでもあれば、1 つも作らずに止める。
            // 黙って読み飛ばすと面の取りこぼしになり、分解の前提が崩れる。
            for (int f = 0; f < faceCount; f++)
            {
                var face = src.Faces[f];
                if (face == null || face.VertexIndices == null || face.VertexIndices.Count == 0)
                    return PartsIdSplitResult.Fail($"面 {f} に頂点がありません");

                for (int k = 0; k < face.VertexIndices.Count; k++)
                {
                    int vi = face.VertexIndices[k];
                    if (vi < 0 || vi >= n)
                        return PartsIdSplitResult.Fail(
                            $"面 {f} の頂点索引 {vi} が範囲外です（頂点数 {n}）");
                    if (src.Vertices[vi] == null)
                        return PartsIdSplitResult.Fail($"面 {f} が参照する頂点 {vi} がありません");
                }
            }

            // ── 出現するパーツID ────────────────────────────────────────
            var idSet = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                var v = src.Vertices[i];
                if (v == null) continue;
                idSet.Add(v.PartsId);
            }
            if (idSet.Count == 0)
                return PartsIdSplitResult.Fail("パーツIDを持つ頂点がありません");

            // ── 処理順（降順。予約値だけ末尾） ──────────────────────────
            var order = new List<int>(idSet);
            order.Sort();
            order.Reverse();
            if (order.Count > 1 && order[0] == PartsIdOps.UnweightedPartsId)
            {
                order.RemoveAt(0);
                order.Add(PartsIdOps.UnweightedPartsId);
            }

            var orderOf = new Dictionary<int, int>(order.Count);
            for (int oi = 0; oi < order.Count; oi++) orderOf[order[oi]] = oi;

            // ── 面の割り当て ────────────────────────────────────────────
            // 「処理順で先に来る候補が取る」は、面の頂点が持つパーツIDのうち
            // 処理順の位置が最小のものを選ぶことと同じ。
            var faceOwner  = new int[faceCount];
            var usedByFace = new bool[n];

            for (int f = 0; f < faceCount; f++)
            {
                var face  = src.Faces[f];
                int bestAt = int.MaxValue;
                int bestId = 0;

                for (int k = 0; k < face.VertexIndices.Count; k++)
                {
                    int vi = face.VertexIndices[k];
                    usedByFace[vi] = true;

                    int at = orderOf[src.Vertices[vi].PartsId];
                    if (at < bestAt)
                    {
                        bestAt = at;
                        bestId = src.Vertices[vi].PartsId;
                    }
                }

                faceOwner[f] = bestId;
            }

            int looseCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (src.Vertices[i] == null) continue;
                if (!usedByFace[i]) looseCount++;
            }

            // ── 切り出し（並びはパーツIDの昇順） ────────────────────────
            var listOrder = new List<int>(idSet);
            listOrder.Sort();

            int outVertexCount = 0;

            foreach (int p in listOrder)
            {
                var mo  = new MeshObject();
                var map = new Dictionary<int, int>();

                // (a) 自分が取った面と、その面が参照する頂点
                for (int f = 0; f < faceCount; f++)
                {
                    if (faceOwner[f] != p) continue;

                    var nf = src.Faces[f].Clone();
                    for (int k = 0; k < nf.VertexIndices.Count; k++)
                    {
                        int ov = nf.VertexIndices[k];
                        if (!map.TryGetValue(ov, out int nv))
                        {
                            nv = mo.Vertices.Count;
                            map[ov] = nv;
                            mo.AddVertexRaw(src.Vertices[ov].Clone());
                        }
                        nf.VertexIndices[k] = nv;
                    }
                    mo.AddFaceRaw(nf);
                }

                // (b) パーツIDが自分で、(a) に入らなかった頂点
                for (int i = 0; i < n; i++)
                {
                    var sv = src.Vertices[i];
                    if (sv == null || sv.PartsId != p) continue;
                    if (map.ContainsKey(i)) continue;

                    map[i] = mo.Vertices.Count;
                    mo.AddVertexRaw(sv.Clone());
                }

                // AddVertexRaw / AddFaceRaw は使用中IDの集合へ登録しないので、
                // 元のIDをそのまま残したうえで集合だけ作り直す。
                mo.RebuildIdSets();

                // ウェイトを持つ頂点が入ったならスキンドへ上げる。
                // MeshFilter へ戻すことはない（RecomputeSkinKind の約束）。
                mo.RecomputeSkinKind();

                outVertexCount += mo.VertexCount;
                pieces.Add(new PartsIdSplitPiece { PartsId = p, Mesh = mo });
            }

            return new PartsIdSplitResult
            {
                Success               = true,
                SourceVertexCount     = n,
                SourceFaceCount       = faceCount,
                PartCount             = pieces.Count,
                OutputVertexCount     = outVertexCount,
                DuplicatedVertexCount = outVertexCount - n,
                LooseVertexCount      = looseCount,
                Reason                = "",
            };
        }
    }
}
