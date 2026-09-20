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

        // ボーン編集（PlayerBoneEditorSubPanel が読む項目）
        /// <summary>ポーズ層 "Manual" があるか。</summary>
        bool    HasManualPoseLayer { get; }
        /// <summary>ポーズ層 "Manual" の移動量（無ければ 0）。</summary>
        Vector3 ManualPoseDeltaPosition { get; }
        /// <summary>ポーズ層 "Manual" の回転（オイラー角 0..360。無ければ 0）。</summary>
        Vector3 ManualPoseDeltaRotationEuler { get; }
        /// <summary>ワールド位置（WorldMatrix の平行移動成分）。</summary>
        Vector3 WorldPosition { get; }

        // Humanoid の可動域（PlayerHumanLimitSubPanel が読む項目）
        /// <summary>割り当てた Humanoid ボーン名（MeshObject.HumanBodyBone。未割当は空）。</summary>
        string  HumanBodyBone { get; }
        /// <summary>可動域を持てるボーンか（HumanLimitOps.IsCarrier）。</summary>
        bool    IsHumanLimitCarrier { get; }
        /// <summary>既定以外の可動域を持つか（HumanLimitOps.HasCustomLimit）。</summary>
        bool    HasCustomHumanLimit { get; }
        /// <summary>可動域（MeshObject.HumanLimit）を持つか。</summary>
        bool    HasHumanLimit { get; }
        /// <summary>可動域の下限・上限・中心（ラジアン）と軸長。持たなければ 0。</summary>
        Vector3 HumanLimitMin { get; }
        Vector3 HumanLimitMax { get; }
        Vector3 HumanLimitCenter { get; }
        float   HumanLimitAxisLength { get; }

        // 揺れものの当たり判定（PlayerSpringBoneColliderSubPanel が読む項目）
        /// <summary>揺れもの（当たり判定）を付けられるか（SpringBoneOps.IsCarrier）。</summary>
        bool IsSpringBoneCarrier { get; }
        /// <summary>このオブジェクトの当たり判定（MeshObject.SpringBoneColliders）の写し。無ければ空。</summary>
        IReadOnlyList<SpringBoneColliderView> SpringBoneColliders { get; }

        /// <summary>揺れの根元（MeshObject.SpringBoneChainRoot）があるか。</summary>
        bool   HasSpringBoneChainRoot { get; }
        /// <summary>揺れの根元の名前・中心ボーン名・当たり判定のまとまり番号（根元でなければ空）。</summary>
        string SpringBoneChainName { get; }
        string SpringBoneChainCenterBone { get; }
        int[]  SpringBoneChainGroupIndices { get; }

        /// <summary>VRM 一人称の種類（MeshObject.VrmFirstPerson）。</summary>
        Poly_Ling.Data.VrmFirstPersonType VrmFirstPerson { get; }
        /// <summary>一人称を設定できるか（VrmSettingsOps.IsFirstPersonCarrier）。</summary>
        bool IsVrmFirstPersonCarrier { get; }

        /// <summary>パーツ ID の診断（PartsIdAssignOps.Inspect）。本体でだけ計算する。スナップショットでは null。</summary>
        PartsIdReportView InspectPartsIds();
    }

    /// <summary>揺れものの当たり判定 1 つぶんの写し。</summary>
    public sealed class SpringBoneColliderView
    {
        public Poly_Ling.Data.SpringBoneColliderShape Shape;
        public Vector3 Offset;
        public float   Radius;
        public Vector3 Tail;
        public Vector3 Normal;
        public int[]   GroupIndices;

        public static IReadOnlyList<SpringBoneColliderView> ListOf(List<Poly_Ling.Data.SpringBoneColliderData> src)
        {
            var list = new List<SpringBoneColliderView>();
            if (src == null) return list;
            foreach (var c in src)
            {
                // null の枠も位置を保つ（スロット番号＝並びの位置）。
                list.Add(c == null ? null : new SpringBoneColliderView
                {
                    Shape = c.Shape, Offset = c.Offset, Radius = c.Radius, Tail = c.Tail, Normal = c.Normal,
                    GroupIndices = c.SpringBoneGroupIndices?.ToArray() ?? System.Array.Empty<int>(),
                });
            }
            return list;
        }
    }

    /// <summary>パーツ ID の診断結果（PartsIdAssignOps の報告の写し）。</summary>
    public sealed class PartsIdReportView
    {
        public string Summary;
        /// <summary>現在のパーツ数が連結成分数と一致し、サブ ID が連番か。</summary>
        public bool   IsConsistent;
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

        /// <summary>
        /// オブジェクトグループの一覧（表示用の写し。要更新・出力先なしの判定と詳細文も本体で作る）。
        /// 作れない実装（スナップショットなど）は空。
        /// </summary>
        IReadOnlyList<ObjectGroupView> ObjectGroups { get; }

        // マテリアル（操作経路統一計画.md E、M-1）
        /// <summary>マテリアルスロット数。スナップショットでは 0。</summary>
        int MaterialCount { get; }
        /// <summary>現在のマテリアルスロット。スナップショットでは -1。</summary>
        int CurrentMaterialIndex { get; }
        /// <summary>マテリアルスロットの表示用の写し。範囲外・スナップショットでは null。</summary>
        MaterialSlotView GetMaterialSlot(int slot);

        /// <summary>モーフエクスプレッションの写し。スナップショットでは空。</summary>
        IReadOnlyList<MorphExpressionView> MorphExpressions { get; }

        // Tポーズ（PlayerTPoseSubPanel が読む項目）
        /// <summary>Humanoid ボーンマッピングの件数（未設定は 0）。スナップショットでは 0。</summary>
        int    HumanoidMappingCount { get; }
        /// <summary>スキンウェイトを持つメッシュがあるか（TPoseConverter.HasAnySkinWeight）。スナップショットでは false。</summary>
        bool   HasAnySkinWeight { get; }
        /// <summary>Tポーズ変換前のバックアップがあるか。スナップショットでは false。</summary>
        bool   HasTPoseBackup { get; }
        /// <summary>Tポーズ変換で何が起きるかの診断文（TPoseConverter.Diagnose）。本体でだけ作る。スナップショットでは空。</summary>
        string DiagnoseTPose();

        // Humanoid マッピング（PlayerHumanoidMappingSubPanel が読む項目）
        /// <summary>必須の Humanoid ボーンのうち未割当の数（マッピング未設定なら 0）。スナップショットでは 0。</summary>
        int HumanoidMissingRequiredCount { get; }
        /// <summary>Avatar リターゲット設定の写し（未設定なら既定値で IsSet=false）。スナップショットでは null。</summary>
        AvatarRetargetView AvatarRetarget { get; }

        /// <summary>揺れものの当たり判定のまとまりの名前（ModelContext.SpringBoneColliderGroupNames）。スナップショットでは空。</summary>
        IReadOnlyList<string> SpringBoneColliderGroupNames { get; }

        // VRM 設定（PlayerVrmSettingsSubPanel が読む項目）
        /// <summary>作者情報を持つか。スナップショットでは false。</summary>
        bool HasVrmMeta { get; }
        /// <summary>作者情報の写し（未設定ならモデル名だけ入れた既定値。VrmSettingsOps.GetMetaOrNew）。スナップショットでは null。</summary>
        Poly_Ling.Data.VrmMetaData VrmMetaCopy { get; }
        /// <summary>視線設定を持つか。スナップショットでは false。</summary>
        bool HasVrmLookAt { get; }
        /// <summary>視線設定の写し（未設定なら既定値。VrmSettingsOps.GetLookAtOrNew）。スナップショットでは null。</summary>
        Poly_Ling.Data.VrmLookAtData VrmLookAtCopy { get; }
    }

    /// <summary>Avatar リターゲット設定の写し。</summary>
    public sealed class AvatarRetargetView
    {
        public bool  IsSet;
        public float UpperArmTwist, LowerArmTwist, UpperLegTwist, LowerLegTwist;
        public float ArmStretch, LegStretch, FeetSpacing;
        public bool  HasTranslationDoF;
    }

    /// <summary>モーフエクスプレッションの表示用の写し。</summary>
    public sealed class MorphExpressionView
    {
        public string Name;
        public string NameEnglish;
        public string TypeName;
        public int    Panel;
        public int    MeshCount;
        public List<MorphEntryView> Entries = new List<MorphEntryView>();
    }

    /// <summary>モーフエクスプレッションのエントリ（モーフメッシュとウェイト）の写し。</summary>
    public sealed class MorphEntryView
    {
        public int    MeshIndex;
        public string MeshName;
        public float  Weight;
    }

    /// <summary>
    /// マテリアルスロットの表示用の写し（M-1）。値は起きている Material から読む（画面に見えているもの）。
    /// 保存データ（MaterialData）の値は Saved* に持つ（プレビューの取消・確定後の食い違い検出用）。
    /// </summary>
    public sealed class MaterialSlotView
    {
        public int     Slot;
        public bool    HasMaterial;
        public string  Name;
        public string  ShaderName;
        public Poly_Ling.Materials.ShaderType DetectedShaderType;

        public bool    HasColor;
        public Color   Color;
        public Color   SavedColor;

        /// <summary>メインテクスチャのプロパティ名（_BaseMap / _MainTex）。無ければ null。</summary>
        public string  MainTexProperty;
        public string  MainTexName;
        /// <summary>メインテクスチャ（サムネイル用。本体でだけ入る）。</summary>
        public Texture2D MainTexture;

        public bool    HasMetallic;
        public float   Metallic;
        public float   SavedMetallic;
        public bool    HasSmoothness;
        public float   Smoothness;
        public float   SavedSmoothness;

        public bool    IsTransparent;
    }

    /// <summary>オブジェクトグループの表示用の写し。</summary>
    public sealed class ObjectGroupView
    {
        public string Name;
        public string Action;
        public int    StepCount;
        public bool   AutoUpdate;
        /// <summary>入力が出力作成時から変わっている（ObjectGroupOps.IsStale）。</summary>
        public bool   Stale;
        /// <summary>出力先が無い、または 1 つでも引けない。</summary>
        public bool   OutputMissing;
        /// <summary>詳細表示の文（ステップ・入力・退避）。</summary>
        public string Detail;
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

        /// <summary>
        /// 頂点データ転送の下見（VertexDataTransferOps.Preview）。本体でだけ計算する。スナップショットでは null。
        /// </summary>
        VertexTransferPreviewView PreviewVertexTransfer(
            int srcModelIndex, int srcMeshIndex, int dstModelIndex, int dstMeshIndex,
            Poly_Ling.Ops.VertexMatchMode mode);

        /// <summary>作業軸ライブラリの登録名（プロジェクト共有）。無ければ空。</summary>
        IReadOnlyList<string> WorkAxisLibraryNames { get; }
    }

    /// <summary>作業軸の値の写し（WorkAxisContext から作る）。</summary>
    public sealed class WorkAxisView
    {
        public Vector3 Origin;
        public Vector3 EulerAngles;
        public float   Length;
        public bool    IsVisible;
        public Vector3 AxisX, AxisY, AxisZ;
        /// <summary>この作業軸を持つ作業軸オブジェクトの MeshContextList 索引。無ければ -1。</summary>
        public int     MasterIndex = -1;

        public static WorkAxisView From(Poly_Ling.Context.WorkAxisContext wa, int masterIndex)
            => wa == null ? null : new WorkAxisView
            {
                Origin      = wa.Origin,
                EulerAngles = wa.EulerAngles,
                Length      = wa.Length,
                IsVisible   = wa.IsVisible,
                AxisX       = wa.AxisX,
                AxisY       = wa.AxisY,
                AxisZ       = wa.AxisZ,
                MasterIndex = masterIndex,
            };
    }

    /// <summary>頂点データ転送の下見結果（VertexTransferResult の写し）。</summary>
    public sealed class VertexTransferPreviewView
    {
        public int    Matched;
        public int    Unmatched;
        public string Summary;
    }
}
