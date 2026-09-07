// VmdNodeWorldSampler.cs
// ============================================================
// VMD 適用後のモデルから「ノードのワールド行列」を読み取る口
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/VMD/Adapters/ に配置。
//
// ============================================================
// ■ これが要る理由
// ============================================================
//
//   VRMA 書き出しの骨格（UnityClipVrmAnimationSource）は
//   「ノード索引 → そのフレームのワールド行列」だけを見る。
//   UnityClip 経路ではそれを UnityClipApplier が
//   TryGetNodeWorldMatrix（UnityClipApplier.cs）で提供しているが、
//   VMD 経路には対応するものが無い。ここがその代わりになる。
//
// ============================================================
// ■ 骨格を UnityClipVirtualSkeleton で組む理由（重要・削除禁止）
// ============================================================
//
//   model.HumanoidMapping.BoneIndexMap を素で読んではならない。
//   半身モデル（ミラー枝を持つモデル）では左右の Humanoid ボーンが
//   同じ MeshContextList 索引を指すため、索引が衝突して右半身が丸ごと落ちる。
//   UnityClipVirtualSkeleton.Build はミラー枝の関節を両側のノードへ複製し、
//   左右名を入れ替えて Humanoid を補完するところまで済ませてある。
//   UnityClip 経路・VRM 1.0 書き出しと同じ骨格になるので、
//   .vrm / .vrma / Unity クリップの三者で Humanoid の集合と親子関係が一致する。
//
// ============================================================
// ■ ワールド行列の出どころ
// ============================================================
//
//   実体ノード     … MeshContext.WorldMatrix。
//                    VMDApplier.ApplyBonePose が BonePoseData を書いたあと
//                    ModelContext.ComputeWorldMatrices で更新される。
//                    IK を有効にした場合、CCDIKSolver は BonePoseData ではなく
//                    WorldMatrix を直接書き換えるので、その結果もここに入る。
//   ミラーノード   … 同上。ミラー側の実効ワールドは ComputeWorldMatrices が
//                    常に S·H·S として返すため、そのまま読めばよい。
//   純仮想ミラー関節 … MeshContext を持たない。VMD はそこへキーを持てない
//                    （VMDApplier はボーン名で MeshContext を引く）ので
//                    レスト（UnityClipVirtualSkeleton.RestWorldMatrix）で埋める。
//
// ============================================================

using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.UnityClip;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// VMD 適用後のモデルからノードのワールド行列を控える。
    /// UnityClipVrmAnimationSource.NodeWorldGetter へ TryGetNodeWorldMatrix を渡して使う。
    /// </summary>
    public sealed class VmdNodeWorldSampler
    {
        /// <summary>UnityClip 経路と同じ仮想骨格。</summary>
        public UnityClipVirtualSkeleton Skeleton { get; private set; }

        private Matrix4x4[] _world;
        private bool        _valid;

        private VmdNodeWorldSampler() { }

        // ================================================================
        // 構築
        // ================================================================

        /// <summary>
        /// モデルから仮想骨格を組む。姿勢は見ないので、呼ぶ時点のポーズに依存しない。
        /// </summary>
        /// <returns>失敗時は null。reason に理由が入る。</returns>
        public static VmdNodeWorldSampler Build(ModelContext model, out string reason)
        {
            reason = null;

            if (model == null) { reason = "モデルがありません"; return null; }

            var skeleton = UnityClipVirtualSkeleton.Build(model);
            if (skeleton == null || skeleton.Nodes.Count == 0)
            { reason = "骨格を構築できません"; return null; }

            if (skeleton.HumanoidToNode.Count == 0)
            { reason = "Humanoid 割り当てがありません"; return null; }

            return new VmdNodeWorldSampler
            {
                Skeleton = skeleton,
                _world   = new Matrix4x4[skeleton.Nodes.Count],
                _valid   = false,
            };
        }

        // ================================================================
        // 取り込み
        // ================================================================

        /// <summary>
        /// 現在のモデル姿勢からノードのワールド行列を控える。
        /// VMDApplier.ApplyFrame のあとに呼ぶこと。
        /// </summary>
        public void Capture(ModelContext model)
        {
            if (model == null || Skeleton == null || _world == null) { _valid = false; return; }

            int n = Skeleton.Nodes.Count;
            if (_world.Length != n) _world = new Matrix4x4[n];

            for (int i = 0; i < n; i++)
            {
                var ctx = Skeleton.TargetContext(model, i);
                _world[i] = (ctx != null)
                    ? ctx.WorldMatrix
                    : Skeleton.RestWorldMatrix(model, i);   // 純仮想関節は VMD にキーが無い
            }

            _valid = true;
        }

        /// <summary>
        /// 直近の Capture 時点でのノードのワールド行列。
        /// Capture をまだ通していない場合は false。
        /// </summary>
        public bool TryGetNodeWorldMatrix(int node, out Matrix4x4 world)
        {
            if (!_valid || _world == null || node < 0 || node >= _world.Length)
            {
                world = Matrix4x4.identity;
                return false;
            }
            world = _world[node];
            return true;
        }

        /// <summary>控えた値を無効にする。</summary>
        public void Invalidate() { _valid = false; }
    }
}
