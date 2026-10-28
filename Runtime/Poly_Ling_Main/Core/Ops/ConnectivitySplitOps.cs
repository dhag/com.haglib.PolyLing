// ConnectivitySplitOps.cs
// 面・線のつながり（頂点番号の共有）で 1 つのメッシュを分解し、つながりごとの MeshObject を作る。
// モデルへの挿入と階層づけは PartsIdSplitInserter が持つ（パーツIDによる分解と共通）。
// ここは純粋な分解だけを行う。
//
// 【つながりの判定】
//   PartsIdAssignOps.ComputeConnectivityParts と同じ（パーツIDパネルの①と同じ結果）。
//   面（三角以上）と線（2頂点の Face）で頂点番号を共有するものを 1 つにまとめる。
//   位置が同じでも頂点番号が別なら別のつながりになる。
//
// 【分解の約束】
//   ・つながり同士は頂点を共有しないので、頂点の重複も面の取りこぼしも起きない。
//   ・元のメッシュは 1 頂点も書き換えない。頂点のパーツIDも書き換えない。
//   ・つながりが 1 つしかないときは何も作らずに失敗させる（分解の意味が無いため）。
//
// 【孤立頂点（面にも線にも属さない頂点）】
//   IsolatedVertexPolicy で選ぶ。SingleGroup は面の無い 1 オブジェクトにまとめ、
//   SeparateParts は 1 頂点ずつのオブジェクトにする。
//
// 【引き継ぐもの / 引き継がないもの】 PartsIdSplitOps と同じ。
//   引き継ぐ … Vertex.Clone() / Face.Clone() が持つもの一式。
//   引き継がない … MirrorBakeState / IKData / IKLink / RigidBodyData / JointData /
//                   SpringBone 系 / NormalRecalcExcludeList。子は素のメッシュにする。
//
// 【配置】 Runtime/Poly_Ling_Main/Core/Ops/

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>つながりによる分解の実行結果。</summary>
    public struct ConnectivitySplitResult
    {
        /// <summary>分解できたか。false なら 1 つも作っていない。</summary>
        public bool Success;

        /// <summary>元メッシュの頂点数。</summary>
        public int SourceVertexCount;

        /// <summary>元メッシュの面数（線を含む）。</summary>
        public int SourceFaceCount;

        /// <summary>作ったメッシュの数。</summary>
        public int PartCount;

        /// <summary>元メッシュで、どの面・線にも属していなかった頂点の数。</summary>
        public int IsolatedVertexCount;

        /// <summary>元のオブジェクトを削除したか。</summary>
        public bool SourceDeleted;

        /// <summary>失敗の理由。成功時は空。</summary>
        public string Reason;

        public static ConnectivitySplitResult Fail(string reason)
            => new ConnectivitySplitResult { Success = false, Reason = reason ?? "" };

        public string Summary =>
            $"元 頂点 {SourceVertexCount} / 面 {SourceFaceCount} → {PartCount} 個"
          + (IsolatedVertexCount > 0 ? $" / 孤立頂点 {IsolatedVertexCount}" : "")
          + (SourceDeleted ? " / 元を削除" : " / 元は残す");
    }

    /// <summary>面・線のつながりによるメッシュの分解。元メッシュは書き換えない。</summary>
    public static class ConnectivitySplitOps
    {
        /// <summary>
        /// つながりごとにメッシュを切り出す。
        /// </summary>
        /// <param name="src">元メッシュ。書き換えない。</param>
        /// <param name="isolatedPolicy">孤立頂点の扱い。</param>
        /// <param name="pieces">
        /// つながり番号の昇順（＝つながり内の最小頂点インデックスの昇順。孤立頂点のまとめは末尾）。
        /// PartsId 欄にはつながり番号を入れる（名前付けに使うだけで、頂点には書かない）。
        /// 失敗時は空。
        /// </param>
        public static ConnectivitySplitResult Split(
            MeshObject src, IsolatedVertexPolicy isolatedPolicy,
            out List<PartsIdSplitPiece> pieces)
        {
            pieces = new List<PartsIdSplitPiece>();

            if (src == null || src.Vertices == null || src.Vertices.Count == 0)
                return ConnectivitySplitResult.Fail("対象メッシュに頂点がありません");

            int n         = src.Vertices.Count;
            int faceCount = src.Faces?.Count ?? 0;

            // ── 検査 ────────────────────────────────────────────────────
            // 壊れた面が 1 つでもあれば、1 つも作らずに止める。
            for (int f = 0; f < faceCount; f++)
            {
                var face = src.Faces[f];
                if (face == null || face.VertexIndices == null || face.VertexIndices.Count == 0)
                    return ConnectivitySplitResult.Fail($"面 {f} に頂点がありません");

                for (int k = 0; k < face.VertexIndices.Count; k++)
                {
                    int vi = face.VertexIndices[k];
                    if (vi < 0 || vi >= n)
                        return ConnectivitySplitResult.Fail(
                            $"面 {f} の頂点索引 {vi} が範囲外です（頂点数 {n}）");
                    if (src.Vertices[vi] == null)
                        return ConnectivitySplitResult.Fail($"面 {f} が参照する頂点 {vi} がありません");
                }
            }

            // ── つながり番号 ────────────────────────────────────────────
            var partOf = PartsIdAssignOps.ComputeConnectivityParts(
                src, isolatedPolicy, out int partCount, out int isolatedCount);

            if (partCount <= 1)
                return ConnectivitySplitResult.Fail("つながりが 1 つしかないので分解しません");

            // ── 切り出し ────────────────────────────────────────────────
            var meshes = new MeshObject[partCount];
            var maps   = new Dictionary<int, int>[partCount];
            for (int p = 0; p < partCount; p++)
            {
                meshes[p] = new MeshObject();
                maps[p]   = new Dictionary<int, int>();
            }

            // 頂点は元の並び順のまま各つながりへ入れる（つながり内の順序を保つ）。
            for (int i = 0; i < n; i++)
            {
                int p = partOf[i];
                if (p < 0) continue;
                maps[p][i] = meshes[p].Vertices.Count;
                meshes[p].AddVertexRaw(src.Vertices[i].Clone());
            }

            // 面の頂点はすべて同じつながりに属する（併合の定義）。先頭の頂点で行き先を決める。
            for (int f = 0; f < faceCount; f++)
            {
                var srcFace = src.Faces[f];
                int p = partOf[srcFace.VertexIndices[0]];

                var nf  = srcFace.Clone();
                var map = maps[p];
                for (int k = 0; k < nf.VertexIndices.Count; k++)
                    nf.VertexIndices[k] = map[nf.VertexIndices[k]];
                meshes[p].AddFaceRaw(nf);
            }

            for (int p = 0; p < partCount; p++)
            {
                var mo = meshes[p];

                // AddVertexRaw / AddFaceRaw は使用中IDの集合へ登録しないので作り直す。
                mo.RebuildIdSets();

                // ウェイトを持つ頂点が入ったならスキンドへ上げる。
                mo.RecomputeSkinKind();

                pieces.Add(new PartsIdSplitPiece { PartsId = p, Mesh = mo });
            }

            return new ConnectivitySplitResult
            {
                Success             = true,
                SourceVertexCount   = n,
                SourceFaceCount     = faceCount,
                PartCount           = partCount,
                IsolatedVertexCount = isolatedCount,
                SourceDeleted       = false,
                Reason              = "",
            };
        }
    }
}
