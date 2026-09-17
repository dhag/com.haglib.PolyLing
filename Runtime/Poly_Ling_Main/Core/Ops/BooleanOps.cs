// BooleanOps.cs
// メッシュオブジェクト同士のブーリアン演算（和 / 差 / 積）。
//
// 実体の CSG アルゴリズムは Runtime/ThirdParty/ParaboxCSG に取り込んだ
// pb_CSG（csg.js の C# 移植, MIT）を使う。BSP ツリーによる clipTo / invert の
// 組み合わせで Union / Subtract / Intersect を構成している。
//
// ここは「MeshObject <-> CSG.Model」の変換と、演算空間の決定だけを担う。
// GameObject / Transform / Material には一切触れない。
//
//   MeshObject A ──ToUnityMesh(ma)──┐
//                                   ├─ CSG.Model ─ BSP ─ CSG.Model ─ Mesh ─┐
//   MeshObject B ──ToUnityMesh(mb)──┘                                      │
//                                                                          │
//   結果 MeshObject ◄── FromUnityMesh ◄── （任意）同一位置頂点マージ ◄──────┘
//
// 【演算空間】
//   呼び出し側が worldToResult を渡す。A の姿勢をそのまま引き継ぐ場合は
//   A の WorldMatrixInverse を渡せばよい（メッシュマージと同じ規約）。
//
// 【スキンドメッシュを受け付けない理由】
//   CSG の頂点はボーンウェイトを持たない。通せばウェイトが全て失われるうえ、
//   スキンド系は頂点がワールド（バインド）空間にあるため演算空間の意味も変わる。
//   種別が Skinned のメッシュは失敗として返す。

using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>ブーリアン演算の種類。</summary>
    public enum BooleanOpKind
    {
        /// <summary>和（A ∪ B）</summary>
        Union = 0,
        /// <summary>差（A − B）</summary>
        Subtract = 1,
        /// <summary>積（A ∩ B）</summary>
        Intersect = 2
    }

    /// <summary>ブーリアン演算の結果。</summary>
    public struct BooleanResult
    {
        public bool Success;
        public string Message;

        /// <summary>成功時のみ非 null。演算空間のローカル座標を持つ新規メッシュ。</summary>
        public MeshObject Mesh;

        /// <summary>実際に採用した頂点結合距離と後処理の試行回数。結合しない場合は0。</summary>
        public float ActualMergeThreshold;
        public int PostprocessAttempts;
    }

    public static partial class BooleanOps
    {
        /// <summary>epsilon の既定値。pb_CSG の既定と同じ。</summary>
        public const float DefaultEpsilon = 0.00001f;

        /// <summary>同一位置頂点マージのしきい値の既定値。</summary>
        public const float DefaultMergeThreshold = 0.00001f;

        // ================================================================
        // Perform
        // ================================================================

        /// <summary>
        /// 2 つのメッシュオブジェクトにブーリアン演算を行い、新規 MeshObject を返す。
        /// 入力は変更しない。
        /// </summary>
        /// <param name="op">演算の種類。</param>
        /// <param name="a">左辺。差では「削られる側」。</param>
        /// <param name="aToWorld">a のローカル → ワールド行列。</param>
        /// <param name="b">右辺。差では「削る側」。</param>
        /// <param name="bToWorld">b のローカル → ワールド行列。</param>
        /// <param name="worldToResult">ワールド → 結果のローカル行列。</param>
        /// <param name="epsilon">
        /// 正規化した平面からの符号付き距離に対する許容量（演算空間の単位）。
        /// </param>
        /// <param name="mergeVertices">演算後に同一位置頂点をマージするか。</param>
        /// <param name="mergeThreshold">マージのしきい値の上限。接続が壊れる場合は小さくして再試行する。</param>
        /// <param name="resultName">結果メッシュの名前。null なら自動生成。</param>
        public static BooleanResult Perform(
            BooleanOpKind op,
            MeshObject a, Matrix4x4 aToWorld,
            MeshObject b, Matrix4x4 bToWorld,
            Matrix4x4 worldToResult,
            float epsilon = DefaultEpsilon,
            bool mergeVertices = true,
            float mergeThreshold = DefaultMergeThreshold,
            string resultName = null)
        {
            // ------------------------------------------------------------
            // 1. 入力検査
            // ------------------------------------------------------------
            if (a == null || b == null)
                return Fail("メッシュが指定されていない");

            if (a.FaceCount == 0 || b.FaceCount == 0)
                return Fail("面を持たないメッシュは対象にできない");

            if (a.IsSkinnedKind || b.IsSkinnedKind)
                return Fail("スキンドメッシュは対象にできない（ボーンウェイトが失われるため）");

            if (mergeVertices && (float.IsNaN(mergeThreshold) || float.IsInfinity(mergeThreshold) || mergeThreshold < 0f))
                return Fail("頂点結合距離は有限の非負値で指定する");

            // ------------------------------------------------------------
            // 2. 演算空間への変換行列
            // ------------------------------------------------------------
            Matrix4x4 ma = worldToResult * aToWorld;
            Matrix4x4 mb = worldToResult * bToWorld;

            // ------------------------------------------------------------
            // 3. 変換 → CSG → 変換
            //    途中で作る Unity Mesh は 3 つ。すべてこの関数内で破棄する。
            // ------------------------------------------------------------
            Mesh meshA = null;
            Mesh meshB = null;
            Mesh meshResult = null;

            float savedEpsilon = Poly_Ling.CSG.CSG.epsilon;

            try
            {
                // ToUnityMesh は Mesh を新規生成し PLResStat.LiveMesh を加算するため、
                // 破棄は MeshContext.DestroyMesh（減算あり）で行う。
                meshA = a.ToUnityMesh(ma, a.SubMeshCount);
                meshB = b.ToUnityMesh(mb, b.SubMeshCount);

                if (meshA.vertexCount == 0 || meshB.vertexCount == 0)
                    return Fail("変換後の頂点が 0 個になった");

                Poly_Ling.CSG.CSG.epsilon = epsilon;

                // Model の変換行列は恒等。位置合わせは ToUnityMesh 側で済ませている。
                var modelA = new Poly_Ling.CSG.Model(meshA, Matrix4x4.identity);
                var modelB = new Poly_Ling.CSG.Model(meshB, Matrix4x4.identity);

                Poly_Ling.CSG.Model modelResult;
                switch (op)
                {
                    case BooleanOpKind.Union:
                        modelResult = Poly_Ling.CSG.CSG.Union(modelA, modelB);
                        break;
                    case BooleanOpKind.Subtract:
                        modelResult = Poly_Ling.CSG.CSG.Subtract(modelA, modelB);
                        break;
                    case BooleanOpKind.Intersect:
                        modelResult = Poly_Ling.CSG.CSG.Intersect(modelA, modelB);
                        break;
                    default:
                        return Fail("未知の演算種別: " + op);
                }

                if (modelResult == null)
                    return Fail("CSG が結果を返さなかった");

                // Model -> Mesh。これは new Mesh() で作られ PLResStat には計上されない。
                meshResult = modelResult.mesh;

                if (meshResult == null || meshResult.vertexCount == 0)
                    return Fail("結果が空になった（交差していない可能性がある）");

                // ------------------------------------------------------------
                // 4. MeshObject へ戻す
                //    mergeVertices:false で 1 対 1 に写し、統合は
                //    MeshMergeHelper（しきい値付き）に任せる。
                //    CSG は分割点で浮動小数点誤差を含むため、完全一致前提の
                //    FromUnityMesh 側のマージでは取りこぼす。
                // ------------------------------------------------------------
                string name = string.IsNullOrEmpty(resultName)
                    ? MakeDefaultName(op, a, b)
                    : resultName;

                var result = new MeshObject(name);
                result.FromUnityMesh(meshResult, mergeVertices: false, includeBoneWeights: false);

                if (result.VertexCount == 0 || result.FaceCount == 0)
                    return Fail("結果が空になった（交差していない可能性がある）");

                float actualMergeThreshold = mergeVertices ? mergeThreshold : 0f;
                int postprocessAttempts = 0;
                if (mergeVertices)
                {
                    while (true)
                    {
                        postprocessAttempts++;
                        MeshMergeHelper.MergeAllVerticesAtSamePosition(result, actualMergeThreshold);
                        TJunctionOps.Resolve(result, actualMergeThreshold);
                        // Face.Triangulate の扇形分割では、辺上の分割点が縮退三角形に
                        // 含まれてしまう。次の描画/CSG変換でも接続を保つよう確定する。
                        bool triangulated = TriangulateResult(result);
                        var topology = BoundaryEdgeOps.AnalyzeTopology(result);
                        if (triangulated && result.FaceCount > 0 && topology.BoundaryEdges == 0 &&
                            topology.NonManifoldEdges == 0 && topology.InconsistentWindingEdges == 0)
                            break;

                        // 大きすぎる結合距離は別の頂点まで潰してしまう。
                        // BSPを再演算せず、未結合の結果から後処理だけを最大4回試す。
                        // 破れた結果を正常扱いして A を置き換えたり B を削除したりしない。
                        if (postprocessAttempts >= 4 || actualMergeThreshold <= 1e-7f)
                            return Fail($"接続検証に失敗: 三角形分割 {(triangulated ? "成功" : "失敗")} / 境界辺 {topology.BoundaryEdges} / " +
                                $"3面以上の共有辺 {topology.NonManifoldEdges} / " +
                                $"同方向の共有辺 {topology.InconsistentWindingEdges} " +
                                $"（結合距離 {actualMergeThreshold:G6}）");

                        actualMergeThreshold = Mathf.Max(1e-7f, actualMergeThreshold * 0.1f);
                        result = new MeshObject(name);
                        result.FromUnityMesh(meshResult, mergeVertices: false, includeBoneWeights: false);
                    }
                }

                // 頂点 ID・面 ID は付けない。ID は他モデルとのモーフ等の対応付けに使うもので、
                // 自動で振ると一意性がメッシュ内にしか無い番号が混ざる（AssignMissingIds を勝手に呼ばない規約）。
                // 結果の受け口（booleanMesh）も ID を読まない。

                return new BooleanResult
                {
                    Success = true,
                    Message = $"頂点 {result.VertexCount} / 面 {result.FaceCount}",
                    Mesh = result,
                    ActualMergeThreshold = actualMergeThreshold,
                    PostprocessAttempts = postprocessAttempts,
                };
            }
            finally
            {
                Poly_Ling.CSG.CSG.epsilon = savedEpsilon;

                // ToUnityMesh 由来の 2 つは計上済みなので DestroyMesh を通す。
                MeshContext.DestroyMesh(meshA);
                MeshContext.DestroyMesh(meshB);

                // CSG 由来の 1 つは計上していないので直接破棄する。
                if (meshResult != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(meshResult);
                    else                       UnityEngine.Object.DestroyImmediate(meshResult);
                }
            }
        }

        // ================================================================
        // ヘルパ
        // ================================================================

        private static BooleanResult Fail(string message)
        {
            return new BooleanResult { Success = false, Message = message, Mesh = null };
        }

        private static string MakeDefaultName(BooleanOpKind op, MeshObject a, MeshObject b)
        {
            string suffix;
            switch (op)
            {
                case BooleanOpKind.Union:     suffix = "_union";     break;
                case BooleanOpKind.Subtract:  suffix = "_subtract";  break;
                case BooleanOpKind.Intersect: suffix = "_intersect"; break;
                default:                      suffix = "_boolean";   break;
            }

            string baseName = string.IsNullOrEmpty(a?.Name) ? "Mesh" : a.Name;
            return baseName + suffix;
        }

        /// <summary>表示用の演算名。</summary>
        public static string DisplayName(BooleanOpKind op)
        {
            switch (op)
            {
                case BooleanOpKind.Union:     return "和 (A ∪ B)";
                case BooleanOpKind.Subtract:  return "差 (A − B)";
                case BooleanOpKind.Intersect: return "積 (A ∩ B)";
                default:                      return op.ToString();
            }
        }
    }
}
