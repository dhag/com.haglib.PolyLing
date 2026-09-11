// MeshContext.Transform.cs
// MeshContext：階層・トランスフォーム・変換行列・IK データ・頂点単位のローカル→ワールド変換。
// Runtime/Poly_Ling_Main/Core/Data/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.UndoSystem;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;
using Poly_Ling.Selection;
using Poly_Ling.Context;
using Poly_Ling.MeshBridge;
using Poly_Ling.Localization;
using static Poly_Ling.Gizmo.GLGizmoDrawer;
using Poly_Ling.Rendering;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
{
    public partial class MeshContext
    {
        // ================================================================
        // 階層・トランスフォーム（MeshObjectへの参照）
        // 階層には２種類ある。
        // メッシュの編集用階層：モデリングアプリと同様の親子関係
        // ゲームオブジェクト階層：ボーン等に利用する親子関係
        // ================================================================

        /// <summary>親メッシュのインデックス（-1=ルート）</summary>
        /// <summary>
        /// 親の MeshContextList 索引。HierarchyParentIndex と同じ場所を指す。
        ///
        /// 【なぜ委譲か】
        ///   この 2 つは同じ「親」を表しており、値が食い違ってよい場面が無い。
        ///   実際、親を書く箇所（MeshHierarchyOps / ObjectArrayInserter /
        ///   ObjectPoseWedgeInserter / MeshFilterToSkinnedConverter）は
        ///   すべて両方へ同じ値を入れている。
        ///   別々の入れ物にしておくと、片方だけ書く箇所が 1 つ生まれるだけで
        ///   食い違いが保存され、あとから読む側が壊れる
        ///   （実測: 並べ替えが HierarchyParentIndex だけを書き、
        ///    ParentIndex にブリッジ挿入前の古い値が残っていた）。
        ///   入れ物を 1 つにして、食い違いを作れなくする。
        /// </summary>
        public int ParentIndex
        {
            get => HierarchyParentIndex;
            set => HierarchyParentIndex = value;
        }

        /// <summary>階層深度（MQO互換）</summary>
        public int Depth
        {
            get => MeshObject?.Depth ?? 0;
            set { if (MeshObject != null) MeshObject.Depth = value; }
        }

        /// <summary>ゲームオブジェクト階層の親（将来用）</summary>
        public int HierarchyParentIndex
        {
            get => MeshObject?.HierarchyParentIndex ?? -1;
            set { if (MeshObject != null) MeshObject.HierarchyParentIndex = value; }
        }

        /// <summary>
        /// スキンドか（頂点がボーンウェイトを持つ描画オブジェクトか）。
        ///
        /// 【何を分けるための判定か】
        ///   頂点の座標系が違う。
        ///     非スキンド … 頂点はローカル空間。ワールドへ出すには WorldMatrix を掛ける
        ///     スキンド   … 頂点はワールド（バインド）空間。描画は
        ///                  SkinningMatrix = WorldMatrix × BindPose を通し、静止時は単位。
        ///                  ここへ WorldMatrix を掛けると二重に効いて位置が飛ぶ
        ///
        ///   スキンド変換後のメッシュは親がボーンになるため WorldMatrix は
        ///   ボーンのワールド行列になる。「メッシュ自身の姿勢」ではないので、
        ///   頂点へ掛けてはいけない。
        ///
        /// 【この判定が答えるのは「ウェイトを持つか」だけ】
        ///   どの行列を使うかまでは決めない。用途によって意味が違うため。
        ///   例: UnifiedBufferManager.UpdateTransformMatrices の行列表は
        ///   ボーンの欄に SkinningMatrix を要求する（スキンド頂点の boneIndex が
        ///   その欄を引くため）。ここに「頂点の座標系」の判定を持ち込むと、
        ///   ボーンの欄が WorldMatrix になってスキンド頂点が全部飛ぶ。
        ///   型で対象を絞るのは各所の責任とし、ここはウェイトの有無だけを答える。
        ///
        ///   ボーンは頂点を持たないので、結果は常に false。
        ///
        /// 【O(1) である】
        ///   MeshObject.SkinKind の明示状態を読むだけ。以前は全頂点を走査していたため、
        ///   カメラ操作・ドラッグ・行列アップロードのたびに
        ///   全 MeshContext × 全頂点ぶんの走査が走っていた。
        ///   実頂点にウェイトが入っているかを知りたい場合はここではなく
        ///   MeshObject.AnyVertexHasBoneWeight() を呼ぶこと。
        /// </summary>
        public bool IsSkinned => MeshObject?.IsSkinnedKind ?? false;

        /// <summary>
        /// 描画オブジェクトの種別（MeshObject に委譲）。MeshObject が無いときは MeshFilter。
        /// 設定は MeshObject.SetSkinKind と同じく明示操作。
        /// </summary>
        public SkinKind SkinKind
        {
            get => MeshObject?.SkinKind ?? SkinKind.MeshFilter;
            set { if (MeshObject != null) MeshObject.SetSkinKind(value); }
        }

        /// <summary>
        /// 頂点をワールドへ出すために掛ける行列。
        /// スキンドは頂点が既にワールド（バインド）空間なので単位行列。
        /// </summary>
        public Matrix4x4 VertexToWorldMatrix
            => IsSkinned ? Matrix4x4.identity : WorldMatrix;

        /// <summary>
        /// ワールド座標を、このオブジェクトの頂点格納空間へ落とす行列。
        /// VertexToWorldMatrix の逆。
        /// </summary>
        public Matrix4x4 WorldToVertexMatrix
            => IsSkinned ? Matrix4x4.identity : WorldMatrixInverse;

        /// <summary>
        /// 左右で対になるボーンの MeshContextList 索引（-1 = 対なし）。
        /// スキンド変換が確定させた値。ウェイトからの推定に使ってはならない。
        /// </summary>
        public int MirrorBoneIndex
        {
            get => MeshObject?.MirrorBoneIndex ?? -1;
            set { if (MeshObject != null) MeshObject.MirrorBoneIndex = value; }
        }

        /// <summary>
        /// アーマチャ生成時にボーンを生成しない（MeshObject に委譲）
        /// </summary>
        public bool IsMirrorBranchRoot
        {
            get => MeshObject?.IsMirrorBranchRoot ?? false;
            set { if (MeshObject != null) MeshObject.IsMirrorBranchRoot = value; }
        }

        public bool IgnorePoseInArmature
        {
            get => MeshObject?.IgnorePoseInArmature ?? false;
            set { if (MeshObject != null) MeshObject.IgnorePoseInArmature = value; }
        }

        /// <summary>
        /// 頂点法線を保持する（自動再計算を行わない）。実体は MeshObject 側。
        /// </summary>
        public bool PreserveNormals
        {
            get => MeshObject?.PreserveNormals ?? false;
            set { if (MeshObject != null) MeshObject.PreserveNormals = value; }
        }

        /// <summary>
        /// 法線の自動再計算から除外するセット一覧。実体は MeshObject 側。
        /// </summary>
        public List<PartsSelectionSet> NormalRecalcExcludeList
        {
            get => MeshObject?.NormalRecalcExcludeList;
            set
            {
                if (MeshObject != null)
                    MeshObject.NormalRecalcExcludeList = value ?? new List<PartsSelectionSet>();
            }
        }

        /// <summary>エクスポート設定</summary>
        public BoneTransform BoneTransform
        {
            get => MeshObject?.BoneTransform;
            set { if (MeshObject != null) MeshObject.BoneTransform = value ?? new BoneTransform(); }
        }

        /// <summary>
        /// ボーンポーズデータ（エディット＆ランタイムポーズ）
        /// BindPoseと相互変換可能
        /// null = BonePoseData未使用（BoneTransformにフォールバック）
        /// </summary>
        public BonePoseData BonePoseData { get; set; }

        // ================================================================
        // 変換行列（ワールド座標変換用）
        // ================================================================

        /// <summary>
        /// ローカル変換行列
        /// BonePoseDataが有効ならそちら優先、なければBoneTransformにフォールバック
        /// </summary>
        public Matrix4x4 LocalMatrix
        {
            get
            {
                // ベース: BoneTransformのローカル変換（親子関係の基礎）
                Matrix4x4 baseMatrix;
                if (BoneTransform == null || !BoneTransform.UseLocalTransform)
                    baseMatrix = Matrix4x4.identity;
                else
                    baseMatrix = BoneTransform.TransformMatrix;

                // BonePoseDataが有効ならデルタを乗算
                if (BonePoseData != null && BonePoseData.IsActive)
                    return baseMatrix * BonePoseData.LocalMatrix;

                return baseMatrix;
            }
        }

        /// <summary>
        /// ワールド変換行列（親子関係を考慮した累積行列）
        /// ComputeWorldMatrices()で計算される
        /// </summary>
        /// <summary>
        /// ミラー側の形状が、実体側の頂点を素直に鏡像化して「生成された」ものか。
        ///
        /// true のとき v_M = S·v_R が成り立ち、実効ワールドは S·H·S で求まる
        /// （ComputeWorldMatrices が適用する）。姿勢は実体側と共有する。
        ///
        /// false のミラー（PMX のように、ファイル内に実在する独立メッシュを
        /// MirrorSide/BakedMirror に再タイプしただけのもの）は自前の正しい頂点を
        /// 持つため、共役を掛けてはならない。
        /// </summary>
        public bool MirrorGeometryDerived { get; set; } = false;

        /// <summary>
        /// ミラーを解消したときに切り離したミラー側の ObjectId（0 = なし）。
        ///
        /// PMX 系（MirrorGeometryDerived = false）のミラー側はボーンウェイトなど
        /// 実体側から復元できない情報を持つため、解消時に破棄せず独立メッシュとして残す。
        /// 再びミラー化するときに相手を引き当てるための参照がこれ。
        /// インデックスは並べ替え・追加・削除でずれるため、位置非依存の ObjectId を持つ。
        ///
        /// MQO 系（同 true）のミラー側は実体側から再生成できるので解消時に破棄する。
        /// この値は使わない。
        /// </summary>
        public ulong DetachedMirrorObjectId { get; set; } = 0;

        public Matrix4x4 WorldMatrix { get; set; } = Matrix4x4.identity;

        /// <summary>
        /// ワールド変換行列の逆行列（キャッシュ）
        /// </summary>
        public Matrix4x4 WorldMatrixInverse { get; set; } = Matrix4x4.identity;

        /// <summary>
        /// バインドポーズ行列（スキンドメッシュ用）
        /// インポート時のボーンのワールド位置の逆行列
        /// SkinningMatrix = WorldMatrix × BindPose
        /// </summary>
        public Matrix4x4 BindPose { get; set; } = Matrix4x4.identity;

        /// <summary>
        /// モーフ等の一時的な位置オーバーライド用バッファ。
        /// null = 無効（GPU へは MeshObject.Positions を使用）。
        /// 非null = GPU _positionBuffer への書き込みにこちらを優先する。
        /// Vertices[i].Position（頂点移動結果）は変更しないため競合しない。
        /// </summary>
        public Vector3[] WorkingPositions { get; set; } = null;

        /// <summary>
        /// ★★★ PMXインポート時のモデル空間でのローカル軸回転（ワールド累積） ★★★
        /// VMDモーション適用時にローカル軸空間変換 (R⁻¹ * Q * R) で使用する。
        /// BoneTransform.RotationQuaternionは親からの相対回転であり、
        /// VMD変換にはこのワールド空間での累積回転が必要。削除禁止。
        /// </summary>
        public Quaternion BoneModelRotation { get; set; } = Quaternion.identity;

        // ================================================================
        // IKデータ（MeshObject.IKData へ委譲）
        // ================================================================
        //
        // 【設計方針】
        //   IKデータの実体は MeshObject.IKData（純POCO）に統一した。
        //   MeshContext は後方互換のため薄い委譲プロパティのみを公開する
        //   （Type / BoneTransform / ParentIndex と同一パターン）。
        //   これにより既存の全呼び出し元（PMX/MQO/CSV/Remote/CCDIK 等）は
        //   無改修のまま、データ実体だけが MeshObject 側へ移動する。
        //
        // 【不変条件】
        //   MeshObject.IKData != null  ⇔  このボーンはIKボーン。
        //   非IKボーンでは IKData を生成しない（意味とメモリの明確化）。
        //
        // 【遅延生成の安全性】
        //   全インポータ/デシリアライザは IsIK を true にしてからスカラーを
        //   設定する。さらに各スカラー setter は「デフォルト値かつ未生成」の
        //   場合に生成をスキップするため、非IKボーンに IKData が漏れ生成され
        //   ない（Remote の全ボーン一括読み込みでもデフォルト値のため安全）。
        // ----------------------------------------------------------------

        /// <summary>IKDataを遅延生成（MeshObjectが存在する場合のみ）。</summary>
        private void EnsureIKData()
        {
            if (MeshObject != null && MeshObject.IKData == null)
                MeshObject.IKData = new IKData();
        }

        /// <summary>このボーンがIKボーンか（MeshObject.IKData へ委譲）。</summary>
        public bool IsIK
        {
            get => MeshObject?.IKData?.IsIK ?? false;
            set
            {
                // 非IK化要求で未生成なら、IKDataを作らずに済ませる
                if (!value && (MeshObject == null || MeshObject.IKData == null)) return;
                EnsureIKData();
                if (MeshObject?.IKData != null) MeshObject.IKData.IsIK = value;
            }
        }

        /// <summary>IKターゲット（エフェクタ）のMeshContextListインデックス。</summary>
        public int IKTargetIndex
        {
            get => MeshObject?.IKData?.TargetIndex ?? -1;
            set
            {
                if (value == -1 && (MeshObject == null || MeshObject.IKData == null)) return;
                EnsureIKData();
                if (MeshObject?.IKData != null) MeshObject.IKData.TargetIndex = value;
            }
        }

        /// <summary>IKループ回数。</summary>
        public int IKLoopCount
        {
            get => MeshObject?.IKData?.LoopCount ?? 0;
            set
            {
                if (value == 0 && (MeshObject == null || MeshObject.IKData == null)) return;
                EnsureIKData();
                if (MeshObject?.IKData != null) MeshObject.IKData.LoopCount = value;
            }
        }

        /// <summary>IK1回あたりの制限角度（ラジアン）。</summary>
        public float IKLimitAngle
        {
            get => MeshObject?.IKData?.LimitAngle ?? 0f;
            set
            {
                if (value == 0f && (MeshObject == null || MeshObject.IKData == null)) return;
                EnsureIKData();
                if (MeshObject?.IKData != null) MeshObject.IKData.LimitAngle = value;
            }
        }

        /// <summary>
        /// IKリンクチェーン（実体は MeshObject.IKData.Links）。
        /// getter は生成済みIKDataでは常に非null（空リスト）を返す。
        /// </summary>
        public List<IKLinkInfo> IKLinks
        {
            get => MeshObject?.IKData?.Links;
            set
            {
                if (value == null && (MeshObject == null || MeshObject.IKData == null)) return;
                EnsureIKData();
                if (MeshObject?.IKData != null) MeshObject.IKData.Links = value;
            }
        }

        /// <summary>
        /// スキニング行列を取得（WorldMatrix × BindPose）
        /// </summary>
        public Matrix4x4 SkinningMatrix => WorldMatrix * BindPose;

        /// <summary>
        /// ローカル座標をワールド座標に変換
        /// </summary>
        public Vector3 LocalToWorld(Vector3 localPos)
        {
            return WorldMatrix.MultiplyPoint3x4(localPos);
        }

        // ================================================================
        // 頂点単位のローカル→ワールド変換（描画側と同一規則）
        // ================================================================
        //
        // WorldMatrix は「メッシュ 1 個につき行列 1 個」を前提にしている。
        // スキンドメッシュではこの前提が成立しない。GPU は頂点ごとに次の規則で
        // 行列を選ぶため、ツール・オーバーレイ側も同じ規則に従う必要がある。
        //
        //   UnifiedBufferManager_Build.cs:344-363
        //     BoneWeight あり → _boneIndices = 頂点の boneIndex（ボーンの context 索引）
        //     BoneWeight なし → _boneIndices = メッシュ自身の context 索引
        //   UnifiedCompute.compute:911-918
        //     skinMatrix = Σ _TransformMatrixBuffer[boneIds.k] * weights.k
        //   UnifiedBufferManager_Update.cs:1513-1515
        //     ボーン／スキンドメッシュ → SkinningMatrix、非スキンドメッシュ → WorldMatrix
        //
        // 規則の定義はこの 1 箇所だけに置く。ToolContext.ActiveVertexMatrix は
        // ここへ委譲する。
        // ================================================================

        // ================================================================
        // 【禁止事項】GPU 由来の座標を扱うときの拗らせ
        // ================================================================
        // 以下は実際に発生させた失敗である。繰り返さないこと。
        //
        // 1. 調べずに CPU 側で独自計算しない。
        //    GPU が _worldPositionBuffer にワールド座標を出しているのに、
        //    同じ規則を CPU で書き直すと、規則が食い違ったときに表示だけがずれる。
        //    まず GPU の値を使う経路を探すこと。
        //
        // 2.「今は呼ばれていないからできない」と決めつけない。
        //    呼び出し箇所が無いことは、呼び出しを足せない理由にならない。
        //    足せるかどうかを調べてから結論を出すこと。
        //
        // 3. カメラもモデルも動いていないのに読み戻しを毎フレーム呼ばない。
        //    WritebackTransformedVertices / GetWorldPositions は同期 GetData を伴う。
        //    ワールド座標が変わる契機（頂点移動・ボーン移動・再構築）でのみ更新し、
        //    ホバーのようにトポロジ・視点・頂点位置のいずれも変わらない操作では呼ばない。
        // ================================================================

        // 【このメソッドは上記 1 に該当する CPU 独自計算である】
        // GPU が _worldPositionBuffer に出した値を使う経路
        // （UnifiedBufferManager.GetWorldPositions + LocalToGlobalVertexIndex）へ
        // 置き換えるべき対象。新規の呼び出しを増やさないこと。

        /// <summary>
        /// 指定頂点に GPU が実際に適用する変換行列を返す。
        /// BoneWeight を持たない頂点、および解決できない場合は WorldMatrix を返す。
        /// </summary>
        public Matrix4x4 VertexMatrix(int vertexIndex)
        {
            var mo = MeshObject;
            if (mo == null || vertexIndex < 0 || vertexIndex >= mo.Vertices.Count)
                return WorldMatrix;

            var vtx = mo.Vertices[vertexIndex];
            if (vtx == null || !vtx.HasBoneWeight)
                return WorldMatrix;

            var list = ParentModelContext?.MeshContextList;
            if (list == null || list.Count == 0)
                return WorldMatrix;

            var bw = vtx.BoneWeight.Value;
            Matrix4x4 acc = new Matrix4x4();
            float total = 0f;

            total += AccumulateBoneMatrix(ref acc, list, bw.boneIndex0, bw.weight0);
            total += AccumulateBoneMatrix(ref acc, list, bw.boneIndex1, bw.weight1);
            total += AccumulateBoneMatrix(ref acc, list, bw.boneIndex2, bw.weight2);
            total += AccumulateBoneMatrix(ref acc, list, bw.boneIndex3, bw.weight3);

            if (total <= 0f)
                return WorldMatrix;

            return acc;
        }

        /// <summary>
        /// acc に list[boneIndex].SkinningMatrix を weight 倍して加算する。
        /// 加算できたときだけ weight を返す（範囲外・weight 0 は 0）。
        /// </summary>
        private static float AccumulateBoneMatrix(
            ref Matrix4x4 acc, List<MeshContext> list, int boneIndex, float weight)
        {
            if (weight == 0f) return 0f;
            if (boneIndex < 0 || boneIndex >= list.Count) return 0f;

            var boneCtx = list[boneIndex];
            if (boneCtx == null) return 0f;

            Matrix4x4 m = boneCtx.SkinningMatrix;
            acc.m00 += m.m00 * weight; acc.m01 += m.m01 * weight; acc.m02 += m.m02 * weight; acc.m03 += m.m03 * weight;
            acc.m10 += m.m10 * weight; acc.m11 += m.m11 * weight; acc.m12 += m.m12 * weight; acc.m13 += m.m13 * weight;
            acc.m20 += m.m20 * weight; acc.m21 += m.m21 * weight; acc.m22 += m.m22 * weight; acc.m23 += m.m23 * weight;
            acc.m30 += m.m30 * weight; acc.m31 += m.m31 * weight; acc.m32 += m.m32 * weight; acc.m33 += m.m33 * weight;
            return weight;
        }

        /// <summary>ローカル座標をワールド座標に変換（頂点単位・描画側と同一規則）</summary>
        public Vector3 LocalToWorld(int vertexIndex, Vector3 localPos)
        {
            return VertexMatrix(vertexIndex).MultiplyPoint3x4(localPos);
        }

        /// <summary>ワールド座標をローカル座標に変換（頂点単位・描画側と同一規則）</summary>
        public Vector3 WorldToLocal(int vertexIndex, Vector3 worldPos)
        {
            return VertexMatrix(vertexIndex).inverse.MultiplyPoint3x4(worldPos);
        }

        /// <summary>
        /// ワールド座標をローカル座標に変換
        /// </summary>
        public Vector3 WorldToLocal(Vector3 worldPos)
        {
            return WorldMatrixInverse.MultiplyPoint3x4(worldPos);
        }

        /// <summary>
        /// ローカル方向をワールド方向に変換（法線等）
        /// </summary>
        public Vector3 LocalToWorldDirection(Vector3 localDir)
        {
            return WorldMatrix.MultiplyVector(localDir).normalized;
        }

        /// <summary>
        /// ワールド方向をローカル方向に変換
        /// </summary>
        public Vector3 WorldToLocalDirection(Vector3 worldDir)
        {
            return WorldMatrixInverse.MultiplyVector(worldDir).normalized;
        }
    }
}
