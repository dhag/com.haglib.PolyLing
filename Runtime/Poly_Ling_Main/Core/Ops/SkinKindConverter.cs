// Runtime/Poly_Ling_Main/Core/Ops/SkinKindConverter.cs
// 描画オブジェクト単位での MeshFilter 系 ⇔ SkinnedMesh 系の相互変換。
//
// 【MeshFilterToSkinnedConverter との違い】
//   あちらは「モデル全体からボーン階層を生成する」一括変換で、ボーンが 1 本でも
//   あると実行できない。こちらは既にボーンがあるモデルで、選んだ描画オブジェクトだけを
//   種別変換する。ボーンの生成も破棄も行わない。
//
// 【なぜ頂点を焼き直すのか】
//   2 つの種別は頂点の格納空間が違う。
//     MeshFilter 系 … 頂点はローカル空間。描画は WorldMatrix を直接掛ける。
//     SkinnedMesh 系 … 頂点はワールド（バインド）空間。描画は
//                      SkinningMatrix = WorldMatrix × BindPose を通り、静止時は単位。
//   ウェイトを足し引きするだけで種別を切り替えると、描画側が選ぶ行列だけが変わり、
//   頂点はそのままなので形が飛ぶ。変換のたびに頂点を目的の空間へ焼き直す。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>
    /// MeshFilter へ戻すときの階層の扱い。
    /// </summary>
    public enum UnskinParentMode
    {
        /// <summary>ルート直下へ移す（既定）。ボーンから完全に切り離す。</summary>
        MoveToRoot = 0,

        /// <summary>現在の親（多くはボーン）のまま残す。ボーンに剛体追従する。</summary>
        KeepParent = 1
    }

    /// <summary>1 オブジェクト分の変換結果。</summary>
    public struct SkinKindConvertEntry
    {
        public int    MasterIndex;
        public string Name;
        public bool   Converted;
        /// <summary>変換しなかった理由（Converted==false のとき）。空なら理由なし。</summary>
        public string Reason;
    }

    /// <summary>
    /// 描画オブジェクト単位の種別変換。
    /// ボーンの生成・破棄は行わない（それは MeshFilterToSkinnedConverter の仕事）。
    /// </summary>
    public static class SkinKindConverter
    {
        // ================================================================
        // 対象の抽出
        // ================================================================

        /// <summary>
        /// 種別変換の対象になり得るか。
        ///
        /// ボーン・モーフ・剛体・JOINT・グループは頂点を持たないか別扱いのため除く。
        /// ミラー側（MirrorSide / BakedMirror）は実頂点を持つが、実体側と対で
        /// 扱わないと片側だけ空間が変わって左右がずれるため、ここでは対象外にする。
        /// ミラーは SetMirrorEnabledCommand で作り直す。
        /// </summary>
        public static bool IsConvertible(MeshContext mc)
        {
            if (mc?.MeshObject == null) return false;
            if (mc.Type != MeshType.Mesh) return false;
            return mc.MeshObject.VertexCount > 0;
        }

        // ================================================================
        // SkinnedMesh 系 → MeshFilter 系
        // ================================================================

        /// <summary>
        /// 選んだオブジェクトのウェイトを破棄して MeshFilter 系へ戻す。
        ///
        /// 【手順】
        ///   1. 現在の見た目（＝頂点のワールド座標）を控える。
        ///      スキンドの頂点はワールド（バインド）空間なので、格納値がそのまま静止時の
        ///      ワールド座標である。
        ///   2. 階層親を決める（parentMode）。
        ///   3. 新しい親の下での WorldMatrix を求め、その逆行列で頂点をローカル化する。
        ///      これを省くと、描画が WorldMatrix 直接経路へ切り替わった瞬間に
        ///      親の姿勢が二重に掛かる。
        ///   4. ウェイトを破棄し、種別を MeshFilter にする（ClearAllBoneWeights）。
        ///
        /// 【姿勢】
        ///   BoneTransform は単位のまま触らない。スキンド変換（Phase 4）が単位に
        ///   潰しており、そこへ値を入れると 3 で求めたローカル座標と食い違う。
        /// </summary>
        /// <returns>オブジェクトごとの結果。</returns>
        public static List<SkinKindConvertEntry> ToMeshFilter(
            ModelContext model, IList<int> masterIndices, UnskinParentMode parentMode)
        {
            var results = new List<SkinKindConvertEntry>();
            if (model == null || masterIndices == null || masterIndices.Count == 0)
                return results;

            // 変換前のワールド行列を確定させる。呼び出し側の状態に依存させない。
            model.ComputeWorldMatrices();

            // 手順 1: 変換対象の現在のワールド座標を先に控える。
            // 親を付け替えると WorldMatrix が変わるため、後から取ると別の値になる。
            var worldPositions = new Dictionary<int, Vector3[]>();

            foreach (int idx in masterIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (!IsConvertible(mc))
                {
                    results.Add(new SkinKindConvertEntry
                    {
                        MasterIndex = idx,
                        Name        = mc?.Name ?? $"#{idx}",
                        Converted   = false,
                        Reason      = "描画メッシュではない、または頂点が無い"
                    });
                    continue;
                }

                if (!mc.IsSkinned)
                {
                    results.Add(new SkinKindConvertEntry
                    {
                        MasterIndex = idx,
                        Name        = mc.Name,
                        Converted   = false,
                        Reason      = "既に MeshFilter 系"
                    });
                    continue;
                }

                var mo  = mc.MeshObject;
                var buf = new Vector3[mo.VertexCount];

                // スキンドの頂点はワールド（バインド）空間。VertexToWorldMatrix は
                // その事実を織り込んで単位行列を返すので、これを通しておけば
                // 実体側・ミラー側どちらの持ち方でも同じ式で書ける。
                Matrix4x4 toWorld = mc.VertexToWorldMatrix;
                for (int v = 0; v < mo.VertexCount; v++)
                    buf[v] = toWorld.MultiplyPoint3x4(mo.Vertices[v].Position);

                worldPositions[idx] = buf;
            }

            if (worldPositions.Count == 0) return results;

            // 手順 2: 階層親を付け替える。
            foreach (var kv in worldPositions)
            {
                var mc = model.GetMeshContext(kv.Key);
                if (mc == null) continue;

                if (parentMode == UnskinParentMode.MoveToRoot)
                {
                    mc.HierarchyParentIndex = -1;
                    mc.Depth                = 0;
                }
                // KeepParent は何もしない。ボーンの子のまま残し、剛体追従させる。
            }

            // 親の付け替えを反映したワールド行列を組み直す。
            model.ComputeWorldMatrices();

            // 手順 3 と 4。
            foreach (var kv in worldPositions)
            {
                int idx = kv.Key;
                var mc  = model.GetMeshContext(idx);
                var mo  = mc?.MeshObject;
                if (mo == null) continue;

                Matrix4x4 inv = mc.WorldMatrixInverse;

                var world = kv.Value;
                int n = Mathf.Min(world.Length, mo.VertexCount);
                for (int v = 0; v < n; v++)
                    mo.Vertices[v].Position = inv.MultiplyPoint3x4(world[v]);

                mo.InvalidatePositionCache();

                // ウェイト破棄と種別変更は同時に行う（ClearAllBoneWeights が両方やる）。
                mo.ClearAllBoneWeights();

                // スキンド用の BindPose は意味を失う。MeshFilter 系の BindPose は
                // ComputeMeshFilterBindPoses が WorldMatrix.inverse で入れ直す。
                mc.BindPose = Matrix4x4.identity;

                mc.ReplaceUnityMesh(mo.ToUnityMesh());
                if (mc.UnityMesh != null) mc.UnityMesh.name = mc.Name;
                mc.OriginalPositions = (Vector3[])mo.Positions.Clone();

                results.Add(new SkinKindConvertEntry
                {
                    MasterIndex = idx,
                    Name        = mc.Name,
                    Converted   = true,
                    Reason      = ""
                });
            }

            model.ComputeWorldMatrices();
            return results;
        }

        // ================================================================
        // MeshFilter 系 → SkinnedMesh 系
        // ================================================================

        /// <summary>
        /// 選んだオブジェクトを、指定したボーンへウェイト 1.0 でバインドして
        /// SkinnedMesh 系にする。
        ///
        /// 【手順】ToMeshFilter の逆。
        ///   1. 現在の見た目（頂点のワールド座標）を控える。
        ///   2. 頂点をワールド座標そのものに置き換える（スキンドの格納空間）。
        ///   3. 全頂点へ BoneWeight{ boneMasterIndex, 1.0 } を書く。
        ///   4. BoneTransform を単位に潰し、階層親をボーンにする。
        ///   5. 種別を Skinned にする。
        ///
        /// 【BindPose】
        ///   静止時に SkinningMatrix = WorldMatrix × BindPose が単位になる必要がある。
        ///   頂点が指すのはボーン側の欄なので、ボーンの BindPose を
        ///   ModelContext.ComputeBindPoses が WorldMatrix.inverse で入れる。
        ///   ここではメッシュ側の BindPose を単位に戻すだけでよい。
        /// </summary>
        /// <param name="boneMasterIndex">バインド先ボーンの MeshContextList 索引。</param>
        public static List<SkinKindConvertEntry> ToSkinned(
            ModelContext model, IList<int> masterIndices, int boneMasterIndex)
        {
            var results = new List<SkinKindConvertEntry>();
            if (model == null || masterIndices == null || masterIndices.Count == 0)
                return results;

            var boneCtx = model.GetMeshContext(boneMasterIndex);
            if (boneCtx == null || boneCtx.Type != MeshType.Bone)
                return results;   // 呼び出し側が事前に弾く。ここは保険。

            model.ComputeWorldMatrices();

            // 手順 1。
            var worldPositions = new Dictionary<int, Vector3[]>();

            foreach (int idx in masterIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (!IsConvertible(mc))
                {
                    results.Add(new SkinKindConvertEntry
                    {
                        MasterIndex = idx,
                        Name        = mc?.Name ?? $"#{idx}",
                        Converted   = false,
                        Reason      = "描画メッシュではない、または頂点が無い"
                    });
                    continue;
                }

                if (mc.IsSkinned)
                {
                    results.Add(new SkinKindConvertEntry
                    {
                        MasterIndex = idx,
                        Name        = mc.Name,
                        Converted   = false,
                        Reason      = "既に SkinnedMesh 系"
                    });
                    continue;
                }

                var mo  = mc.MeshObject;
                var buf = new Vector3[mo.VertexCount];

                Matrix4x4 toWorld = mc.VertexToWorldMatrix;
                for (int v = 0; v < mo.VertexCount; v++)
                    buf[v] = toWorld.MultiplyPoint3x4(mo.Vertices[v].Position);

                worldPositions[idx] = buf;
            }

            if (worldPositions.Count == 0) return results;

            var bw = new BoneWeight { boneIndex0 = boneMasterIndex, weight0 = 1f };

            foreach (var kv in worldPositions)
            {
                int idx = kv.Key;
                var mc  = model.GetMeshContext(idx);
                var mo  = mc?.MeshObject;
                if (mo == null) continue;

                // 手順 2。
                var world = kv.Value;
                int n = Mathf.Min(world.Length, mo.VertexCount);
                for (int v = 0; v < n; v++)
                    mo.Vertices[v].Position = world[v];

                mo.InvalidatePositionCache();

                // 手順 3。ミラー側ウェイトは持たせない。左右対のボーンが判るのは
                // MirrorPair.Build（MirrorBoneIndex 経由）なので、そちらに任せる。
                foreach (var vtx in mo.Vertices)
                {
                    vtx.BoneWeight       = bw;
                    vtx.MirrorBoneWeight = null;
                }

                // 手順 4。
                if (mc.BoneTransform == null) mc.BoneTransform = new BoneTransform();
                mc.BoneTransform.Position          = Vector3.zero;
                mc.BoneTransform.Rotation          = Vector3.zero;
                mc.BoneTransform.Scale             = Vector3.one;
                mc.BoneTransform.UseLocalTransform = false;

                mc.HierarchyParentIndex = boneMasterIndex;
                mc.Depth                = boneCtx.Depth + 1;

                mc.BindPose = Matrix4x4.identity;

                // 手順 5。
                mo.SetSkinKind(SkinKind.Skinned);

                mc.ReplaceUnityMesh(mo.ToUnityMesh());
                if (mc.UnityMesh != null) mc.UnityMesh.name = mc.Name;
                mc.OriginalPositions = (Vector3[])mo.Positions.Clone();

                results.Add(new SkinKindConvertEntry
                {
                    MasterIndex = idx,
                    Name        = mc.Name,
                    Converted   = true,
                    Reason      = ""
                });
            }

            // ボーンの BindPose を入れ直す。これが無いと静止時の
            // SkinningMatrix が単位にならず、メッシュがボーンのワールド位置ぶん飛ぶ。
            model.ComputeWorldAndBindPoses();

            return results;
        }
    }
}
