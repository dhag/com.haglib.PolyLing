// ViewInterfaces.cs
// パネルが参照する統一インタフェース
// 実装A: ProjectSummary/ModelSummary/MeshSummary（リモート用スナップショット）
// 実装B: LiveProjectView/LiveModelView/LiveMeshView（ローカル用、現物を直接読む）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.View
{
    // ================================================================
    // メッシュ単体ビュー
    // ================================================================

    public interface IBonePoseView
    {
        bool HasPose { get; }
        bool IsActive { get; }
        int LayerCount { get; }
        Vector3 ResultPosition { get; }
        Vector3 ResultRotationEuler { get; }
        Vector3 BindPosePosition { get; }
        Vector3 BindPoseRotationEuler { get; }
        Vector3 BindPoseScale { get; }
    }

    public interface IMeshView
    {
        // ID
        int MasterIndex { get; }
        string Name { get; }
        MeshType Type { get; }

        // 協働編集
        /// <summary>位置非依存の安定オブジェクトID（0=未割当）</summary>
        ulong ObjectId { get; }
        /// <summary>現在の編集者名（空文字＝担当者なし）</summary>
        string EditorName { get; }

        // ジオメトリ
        int VertexCount { get; }
        int FaceCount { get; }
        int TriCount { get; }
        int QuadCount { get; }
        int NgonCount { get; }

        // 属性
        bool IsVisible { get; }
        bool IsLocked { get; }

        /// <summary>
        /// SkinnedMesh 系か（MeshObject.SkinKind == Skinned）。
        /// 実頂点のウェイト有無ではなく、描画オブジェクトの種別を表す。
        /// </summary>
        bool HasBoneWeight { get; }
        bool IsFolding { get; }

        // ローカルトランスフォーム（簡易モード用）
        Vector3 LocalPosition { get; }
        Vector3 LocalRotationEuler { get; }
        Vector3 LocalScale { get; }

        // 階層
        int Depth { get; }
        int HierarchyParentIndex { get; }

        // ミラー
        /// <summary>ミラーモード (0:なし, 1:分離, 2:結合)。MQO の mirror 属性と同値。</summary>
        int MirrorType { get; }
        /// <summary>ミラー軸 (1:X, 2:Y, 4:Z)。MQO の mirror_axis 属性と同値。</summary>
        int MirrorAxis { get; }
        bool IsBakedMirror { get; }
        bool IsMirrorSide { get; }
        bool IsRealSide { get; }
        bool HasBakedMirrorChild { get; }
        /// <summary>
        /// 生成ミラーか。true のとき、頂点も姿勢も実体側から作り直されるため
        /// このメッシュを直接編集しても上書きで消える（＝選択させない）。
        /// スキンド変換後のミラーは独立メッシュになるので false。
        /// </summary>
        bool MirrorGeometryDerived { get; }
        /// <summary>
        /// ミラー分岐ルート。エクスポート／スキンド変換で、このノードを含む
        /// 配下を実体側とミラー側の2本の枝に分割する起点。
        /// ボーン生成前のメッシュ属性なので、オブジェクトリストで設定する。
        /// </summary>
        bool IsMirrorBranchRoot { get; }

        // ボーン
        int BoneIndex { get; }
        IBonePoseView BonePose { get; }

        // モーフ
        bool IsMorph { get; }
        int MorphParentIndex { get; }
        string MorphName { get; }
        bool ExcludeFromExport { get; }
        bool IgnorePoseInArmature { get; }
        bool PreserveNormals { get; }

        /// <summary>ビルボード表示（表示だけの姿勢差し替え。保存しない）。</summary>
        Poly_Ling.Data.BillboardMode Billboard { get; }

        // 表示用（計算プロパティ）
        string InfoString { get; }
        string MirrorTypeDisplay { get; }
        bool HasMirrorIcon { get; }

        // パーツ選択辞書
        int PartsSelectionSetCount { get; }
        IReadOnlyList<IPartsSetView> PartsSelectionSets { get; }

        // 現在のパーツ選択状態（件数のみ）
        int SelectedVertexCount { get; }
        int SelectedEdgeCount   { get; }
        int SelectedFaceCount   { get; }
        int SelectedLineCount   { get; }

        /// <summary>非表示の面の数（3 頂点以上の面のうち IsHidden のもの）。</summary>
        int HiddenFaceCount { get; }

        /// <summary>法線再計算の除外セット（MeshObject.NormalRecalcExcludeList）。</summary>
        IReadOnlyList<IPartsSetView> NormalExcludeSets { get; }

        // 一時ミラー（MeshObject.MirrorBakeState）
        /// <summary>一時ミラーで実体化中か。</summary>
        bool IsMirrorBakedState { get; }
        /// <summary>実体化前の頂点数（実体化中でなければ 0）。</summary>
        int MirrorBakeOriginalVertexCount { get; }
        /// <summary>実体化前の面数（実体化中でなければ 0）。</summary>
        int MirrorBakeOriginalFaceCount { get; }
        /// <summary>境界の決め方の説明（"しきい値 x" / "選択頂点 n 点"。実体化中でなければ空）。</summary>
        string MirrorBakeBoundaryDescription { get; }

        /// <summary>頂点 ID の診断（VertexIdOps.Inspect）。本体でだけ計算する。スナップショットでは null。</summary>
        VertexIdReportView InspectVertexIds();
    }

    /// <summary>頂点 ID の診断結果（VertexIdOps の報告の写し）。</summary>
    public sealed class VertexIdReportView
    {
        public int    VertexCount;
        public int    UnsetCount;
        public int    DuplicatedVertexCount;
        public bool   IsHealthy;
        public string Summary;
    }

    /// <summary>パーツ選択セットの軽量サマリ</summary>
    public interface IPartsSetView
    {
        string Name    { get; }
        MeshSelectMode Mode { get; }
        string Summary { get; }
        int VertexCount { get; }
        int EdgeCount   { get; }
        int FaceCount   { get; }
        int LineCount   { get; }

        /// <summary>頂点 ID の控えの件数（PartsSelectionSet.VertexIdCount）。</summary>
        int VertexIdCount { get; }

        /// <summary>引き当てに使える頂点 ID があるか（PartsSelectionSet.HasResolvableVertexIds）。</summary>
        bool HasResolvableVertexIds { get; }
    }

    // ================================================================
    // モデルビュー
    // ================================================================

    public interface IModelView
    {
        string Name { get; }
        string FilePath { get; }
        bool IsDirty { get; }

        int DrawableCount { get; }
        int BoneCount { get; }
        int MorphCount { get; }
        int TotalMeshCount { get; }

        IReadOnlyList<IMeshView> DrawableList { get; }
        IReadOnlyList<IMeshView> BoneList { get; }
        IReadOnlyList<IMeshView> MorphList { get; }
        IReadOnlyList<IMeshView> RigidBodyList { get; }
        IReadOnlyList<IMeshView> RigidBodyJointList { get; }

        int[] SelectedDrawableIndices { get; }
        int[] SelectedBoneIndices { get; }
        int[] SelectedMorphIndices { get; }

        /// <summary>
        /// オブジェクト選択辞書（ModelContext.MeshSelectionSets）の名前一覧。
        /// 並びは MeshSelectionSets と同じで、要素位置が
        /// ApplySelectionDictionaryCommand.SetIndex に対応する。
        /// 辞書が無ければ空配列（null は返さない）。
        /// </summary>
        IReadOnlyList<string> MeshSelectionSetNames { get; }

        /// <summary>
        /// 編集対象メッシュの MeshContextList 索引（ModelContext.ActiveMeshIndex と同じ）。未解決は -1。
        /// </summary>
        int ActiveMeshIndex { get; }

        /// <summary>
        /// 編集対象メッシュのビュー。未解決は null。本体では現物を読む（選択件数・非表示面数も正しい）。
        /// リモートのスナップショットでは選択件数・非表示面数は 0。
        /// </summary>
        IMeshView ActiveMesh { get; }

        /// <summary>
        /// MeshContextList 索引でメッシュのビューを引く。範囲外・無しは null。
        /// 本体では現物を読む（選択件数なども正しい）。リモートは各リストから引く。
        /// </summary>
        IMeshView GetMesh(int masterIndex);
    }

    // ================================================================
    // プロジェクトビュー
    // ================================================================

    public interface IProjectView
    {
        string ProjectName { get; }
        int CurrentModelIndex { get; }
        IModelView CurrentModel { get; }

        /// <summary>プロジェクト内のモデル総数</summary>
        int ModelCount { get; }

        /// <summary>指定インデックスのモデルビューを返す。範囲外は null。</summary>
        IModelView GetModelView(int index);
    }
}
