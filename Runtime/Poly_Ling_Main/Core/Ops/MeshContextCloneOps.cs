// MeshContextCloneOps.cs
// MeshContext の複製を 1 本にまとめる。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【なぜ 1 本にするか】
//   複製は 3 箇所で別々に書かれていて、写す項目が食い違っていた。
//     ・MeshListRecords.CloneMeshContext  … Undo/Redo 用。ほぼ全項目を写す
//     ・BlendOperation.CloneContext       … ブレンド用。ミラー・モーフ関係を写さない
//     ・PlayerCommandDispatcher の複製     … 5 項目しか写さない
//   3 番目は MeshContext 側の実体フィールド（BindPose / BonePoseData /
//   MorphBaseData / ミラー設定ほか）を丸ごと落としていた。
//   フィールドを 1 つ足すたびに 3 箇所へ手で足す形になっていたのが原因なので、
//   写す規則をここへ集める。
//
// 【MeshObject を最初に入れること】
//   MeshContext の Name / Type / Depth / ParentIndex / HierarchyParentIndex /
//   BoneTransform / SkinKind / MirrorBoneIndex / IgnorePoseInArmature /
//   PreserveNormals / NormalRecalcExcludeList / IsMirrorBranchRoot は
//   MeshObject への委譲プロパティで、MeshObject が null のとき setter は
//   黙って何もしない（MeshContext.cs:32-36 ほか）。
//   オブジェクト初期化子は書いた順に走るため、MeshObject より前に Name を
//   書くと代入が捨てられる。ここでは MeshObject を先に入れてから残りを書く。
//
// 【ObjectId と EditorName】
//   Kind で分ける。
//     StateSnapshot … 同一オブジェクトの状態複写（Undo/Redo）。引き継ぐ。
//     NewObject     … 別オブジェクトとしての複製。引き継がない（0 / 空）。
//   NewObject のときは ModelContext.Add / Insert が新しい ObjectId を振る
//   （ModelContext.cs:1419-1422, 1441-1443）。
//
// 【写さないもの】
//   ・UnityMesh          … 呼び出し側が MeshObject から作る（作り方が経路ごとに違う）
//   ・WorkingPositions   … モーフ等の一時オーバーライド。複製へ持ち越さない
//   ・選択状態           … 複製した先で選び直す
//   ・ParentModelContext … ModelContext.Add / Insert が入れる

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>複製の目的。ObjectId と EditorName を引き継ぐかが変わる。</summary>
    public enum MeshContextCloneKind
    {
        /// <summary>
        /// 同一オブジェクトの状態複写（Undo/Redo のスナップショット）。
        /// ObjectId と EditorName を引き継ぐ。担当や外部からの参照が維持される。
        /// </summary>
        StateSnapshot = 0,

        /// <summary>
        /// 別オブジェクトとしての複製。ObjectId と EditorName は引き継がない。
        /// ObjectId で参照している枠組み（ObjectGroup など）から見て、
        /// 複製物は非メンバーになる。
        /// </summary>
        NewObject = 1,
    }

    /// <summary>MeshContext の複製。</summary>
    public static class MeshContextCloneOps
    {
        /// <summary>
        /// MeshContext をディープコピーする。
        /// newName が空でないときは複製の名前をそれに差し替える。
        /// </summary>
        public static MeshContext Clone(
            MeshContext src, MeshContextCloneKind kind, string newName = null)
        {
            if (src == null) return null;

            var mo = src.MeshObject?.Clone();
            if (mo != null && !string.IsNullOrEmpty(newName)) mo.Name = newName;

            // MeshObject を先に入れる。委譲プロパティの setter が効く状態を作るため。
            var dst = new MeshContext { MeshObject = mo };

            if (mo == null && !string.IsNullOrEmpty(newName))
            {
                // MeshObject が無いと Name も持てない。ここに来るのは壊れた入力だけ。
                Debug.LogWarning($"[MeshContextCloneOps] MeshObject が無いので名前を設定できない: {newName}");
            }

            // ── 識別と担当
            if (kind == MeshContextCloneKind.StateSnapshot)
            {
                dst.ObjectId   = src.ObjectId;
                dst.EditorName = src.EditorName;
            }

            // ── 表示・編集状態
            dst.IsVisible = src.IsVisible;
            dst.IsLocked  = src.IsLocked;
            dst.IsFolding = src.IsFolding;

            // ── 変換
            dst.BindPose           = src.BindPose;
            dst.WorldMatrix        = src.WorldMatrix;
            dst.WorldMatrixInverse = src.WorldMatrixInverse;
            dst.BoneModelRotation  = src.BoneModelRotation;
            dst.BonePoseData       = src.BonePoseData?.Clone();
            dst.OriginalPositions  = src.OriginalPositions != null
                ? (Vector3[])src.OriginalPositions.Clone()
                : null;

            // ── ミラー
            //   設定値（型・軸・距離・材質オフセット）は写す。
            //   1 対 1 でしか成り立たない対の参照は NewObject では写さない。
            //   写すと「1 つの実体に対してベイクドミラーが 2 つ」「切り離した
            //   ミラー側を 2 つのオブジェクトが自分の相棒だと主張する」状態になる。
            dst.MirrorType            = src.MirrorType;
            dst.MirrorAxis            = src.MirrorAxis;
            dst.MirrorDistance        = src.MirrorDistance;
            dst.MirrorMaterialOffset  = src.MirrorMaterialOffset;
            dst.MirrorGeometryDerived = src.MirrorGeometryDerived;

            if (kind == MeshContextCloneKind.StateSnapshot)
            {
                dst.BakedMirrorSourceIndex = src.BakedMirrorSourceIndex;
                dst.HasBakedMirrorChild    = src.HasBakedMirrorChild;
                dst.DetachedMirrorObjectId = src.DetachedMirrorObjectId;
            }

            // ── モーフ
            dst.MorphBaseData      = src.MorphBaseData?.Clone();
            dst.MorphParentIndex   = src.MorphParentIndex;
            dst.MorphMirrorPolicy  = src.MorphMirrorPolicy;
            dst.MirrorOfMorphIndex = src.MirrorOfMorphIndex;

            // ── エクスポート
            dst.ExcludeFromExport = src.ExcludeFromExport;
            dst.PMXMaterialNames  = src.PMXMaterialNames != null
                ? new List<string>(src.PMXMaterialNames)
                : new List<string>();

            return dst;
        }
    }
}
