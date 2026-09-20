// LiveViews.cs
// ローカル用ビュー実装
// ProjectContext/ModelContext/MeshContext を直接参照し、
// プロパティアクセス時にリアルタイムで値を返す
// SummaryBuilderによるスナップショット生成を完全にスキップ可能

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Selection;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.View
{
    // ================================================================
    // LiveBonePoseView
    // ================================================================

    public class LiveBonePoseView : IBonePoseView
    {
        private readonly MeshContext _ctx;

        public LiveBonePoseView(MeshContext ctx) { _ctx = ctx; }

        public bool HasPose => _ctx?.BonePoseData != null;
        public bool IsActive => _ctx?.BonePoseData?.IsActive ?? false;
        public int LayerCount => _ctx?.BonePoseData?.LayerCount ?? 0;
        public Vector3 ResultPosition => _ctx?.BonePoseData?.Position ?? Vector3.zero;

        public Vector3 ResultRotationEuler
        {
            get
            {
                var bp = _ctx?.BonePoseData;
                if (bp == null) return Vector3.zero;
                return IsQuatValid(bp.Rotation) ? bp.Rotation.eulerAngles : Vector3.zero;
            }
        }

        public Vector3 BindPosePosition
        {
            get
            {
                if (_ctx == null) return Vector3.zero;
                return (Vector3)_ctx.BindPose.GetColumn(3);
            }
        }

        public Vector3 BindPoseRotationEuler
        {
            get
            {
                if (_ctx == null) return Vector3.zero;
                var q = _ctx.BindPose.rotation;
                return IsQuatValid(q) ? q.eulerAngles : Vector3.zero;
            }
        }

        public Vector3 BindPoseScale
        {
            get
            {
                if (_ctx == null) return Vector3.one;
                return _ctx.BindPose.lossyScale;
            }
        }

        private static bool IsQuatValid(Quaternion q)
        {
            return !float.IsNaN(q.x) && !float.IsNaN(q.y) && !float.IsNaN(q.z) && !float.IsNaN(q.w)
                && (q.x != 0 || q.y != 0 || q.z != 0 || q.w != 0);
        }
    }

    // ================================================================
    // NullBonePoseView（BonePoseDataがないメッシュ用）
    // ================================================================

    public sealed class NullBonePoseView : IBonePoseView
    {
        public static readonly NullBonePoseView Instance = new NullBonePoseView();
        public bool HasPose => false;
        public bool IsActive => false;
        public int LayerCount => 0;
        public Vector3 ResultPosition => Vector3.zero;
        public Vector3 ResultRotationEuler => Vector3.zero;
        public Vector3 BindPosePosition => Vector3.zero;
        public Vector3 BindPoseRotationEuler => Vector3.zero;
        public Vector3 BindPoseScale => Vector3.one;
    }

    // ================================================================
    // LiveMeshView
    // ================================================================

    public class LiveMeshView : IMeshView
    {
        private readonly MeshContext _ctx;
        private readonly ModelContext _model;
        private readonly int _masterIndex;
        private IBonePoseView _bonePoseView;

        /// <summary>現物MeshContext（ローカル専用操作で使用）</summary>
        public MeshContext Context => _ctx;

        public LiveMeshView(MeshContext ctx, ModelContext model, int masterIndex)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _model = model;
            _masterIndex = masterIndex;
        }

        // ID
        public int MasterIndex => _masterIndex;
        public string Name => _ctx.Name ?? "Untitled";
        public MeshType Type => _ctx.Type;

        // 協働編集
        public ulong ObjectId => _ctx.ObjectId;
        public string EditorName => _ctx.EditorName ?? "";

        // ジオメトリ（VertexCount/FaceCountはMeshObject.Countで高速）
        public int VertexCount => _ctx.VertexCount;
        public int FaceCount => _ctx.FaceCount;

        // Tri/Quad/Ngonは詳細パネル用。アクセス頻度低。毎回計算。
        public int TriCount { get { CountFaces(out int t, out _, out _); return t; } }
        public int QuadCount { get { CountFaces(out _, out int q, out _); return q; } }
        public int NgonCount { get { CountFaces(out _, out _, out int n); return n; } }

        // 属性
        public bool IsVisible => _ctx.IsVisible;
        public bool IsLocked => _ctx.IsLocked;
        public bool HasBoneWeight => _ctx.MeshObject?.IsSkinnedKind ?? false;
        public bool IsFolding => _ctx.IsFolding;
        public Vector3 LocalPosition => _ctx.BoneTransform?.Position ?? Vector3.zero;
        public Vector3 LocalRotationEuler => _ctx.BoneTransform?.Rotation ?? Vector3.zero;
        public Vector3 LocalScale => _ctx.BoneTransform?.Scale ?? Vector3.one;

        // 階層
        public int Depth => _ctx.Depth;
        public int HierarchyParentIndex => _ctx.HierarchyParentIndex;

        // ミラー
        public int MirrorType => _ctx.MirrorType;
        public bool IsBakedMirror => _ctx.IsBakedMirror;
        public int MirrorAxis => _ctx.MirrorAxis;
        public bool IsMirrorSide => _model != null && _model.IsMirrorSide(_ctx);
        public bool IsRealSide => _model != null && _model.IsRealSide(_ctx);
        public bool HasBakedMirrorChild => _ctx.HasBakedMirrorChild;
        public bool MirrorGeometryDerived => _ctx.MirrorGeometryDerived;

        // ボーン
        public int BoneIndex => _model?.TypedIndices?.MasterToBoneIndex(_masterIndex) ?? -1;

        public IBonePoseView BonePose
        {
            get
            {
                if (_bonePoseView == null)
                {
                    _bonePoseView = _ctx.BonePoseData != null
                        ? new LiveBonePoseView(_ctx)
                        : (IBonePoseView)NullBonePoseView.Instance;
                }
                return _bonePoseView;
            }
        }

        // モーフ
        public bool IsMorph => _ctx.IsMorph;
        public int MorphParentIndex => _ctx.MorphParentIndex;
        public string MorphName => _ctx.MorphName;
        public bool ExcludeFromExport => _ctx.ExcludeFromExport;
        public bool IgnorePoseInArmature => _ctx.IgnorePoseInArmature;
        public bool IsMirrorBranchRoot => _ctx.IsMirrorBranchRoot;
        public bool PreserveNormals => _ctx.PreserveNormals;
        public Poly_Ling.Data.BillboardMode Billboard => _ctx.Billboard;

        // 表示用計算プロパティ
        public string InfoString => $"V:{VertexCount} F:{FaceCount}";

        // アイコンはミラーの有無だけを示す。
        // モード(0:なし/1:分離/2:結合)と軸(1:X/2:Y/4:Z)はツールチップと詳細欄で扱う。
        public string MirrorTypeDisplay
        {
            get
            {
                if (IsBakedMirror) return "\u25C7";
                return MirrorType > 0 ? "\u21C6" : "";
            }
        }

        public bool HasMirrorIcon => MirrorType > 0 || IsBakedMirror || IsMirrorSide || IsRealSide || HasBakedMirrorChild;

        // パーツ選択辞書
        public int PartsSelectionSetCount => _ctx?.PartsSelectionSetList?.Count ?? 0;
        public IReadOnlyList<IPartsSetView> PartsSelectionSets
        {
            get
            {
                var list = _ctx?.PartsSelectionSetList;
                if (list == null || list.Count == 0) return _emptyPartsSetList;
                var result = new IPartsSetView[list.Count];
                for (int i = 0; i < list.Count; i++)
                    result[i] = new LivePartsSetView(list[i]);
                return result;
            }
        }
        private static readonly IPartsSetView[] _emptyPartsSetList = Array.Empty<IPartsSetView>();

        // 一時ミラー（MeshObject.MirrorBakeState）
        public bool IsMirrorBakedState            => _ctx?.MeshObject?.MirrorBakeState != null;
        public int  MirrorBakeOriginalVertexCount => _ctx?.MeshObject?.MirrorBakeState?.OriginalVertexCount ?? 0;
        public int  MirrorBakeOriginalFaceCount   => _ctx?.MeshObject?.MirrorBakeState?.OriginalFaceCount ?? 0;
        public string MirrorBakeBoundaryDescription
        {
            get
            {
                var bake = _ctx?.MeshObject?.MirrorBakeState;
                if (bake == null) return "";
                return bake.BoundaryVertices == null
                    ? "しきい値 " + bake.Threshold
                    : "選択頂点 " + bake.BoundaryVertices.Length + " 点";
            }
        }

        public VertexIdReportView InspectVertexIds()
        {
            if (_ctx?.MeshObject == null) return null;
            var reports = Poly_Ling.Ops.VertexIdOps.Inspect(new List<MeshContext> { _ctx });
            if (reports == null || reports.Count == 0) return null;
            var r = reports[0];
            return new VertexIdReportView
            {
                VertexCount           = r.VertexCount,
                UnsetCount            = r.UnsetCount,
                DuplicatedVertexCount = r.DuplicatedVertexCount,
                IsHealthy             = r.IsHealthy,
                Summary               = r.Summary,
            };
        }

        // ボーン編集（PlayerBoneEditorSubPanel が読む項目）
        public bool    HasManualPoseLayer           => _ctx?.BonePoseData?.GetLayer("Manual") != null;
        public Vector3 ManualPoseDeltaPosition      => _ctx?.BonePoseData?.GetLayer("Manual")?.DeltaPosition ?? Vector3.zero;
        public Vector3 ManualPoseDeltaRotationEuler => _ctx?.BonePoseData?.GetLayer("Manual")?.DeltaRotation.eulerAngles ?? Vector3.zero;
        public Vector3 WorldPosition
        {
            get
            {
                if (_ctx == null) return Vector3.zero;
                var wm = _ctx.WorldMatrix;
                return new Vector3(wm.m03, wm.m13, wm.m23);
            }
        }

        // Humanoid の可動域（PlayerHumanLimitSubPanel が読む項目）
        public string  HumanBodyBone        => _ctx?.MeshObject?.HumanBodyBone ?? "";
        public bool    IsHumanLimitCarrier  => _model != null && Poly_Ling.Ops.HumanLimitOps.IsCarrier(_model, _masterIndex);
        public bool    HasCustomHumanLimit  => _model != null && Poly_Ling.Ops.HumanLimitOps.HasCustomLimit(_model, _masterIndex);
        public bool    HasHumanLimit        => _ctx?.MeshObject?.HumanLimit != null;
        public Vector3 HumanLimitMin        => _ctx?.MeshObject?.HumanLimit?.Min    ?? Vector3.zero;
        public Vector3 HumanLimitMax        => _ctx?.MeshObject?.HumanLimit?.Max    ?? Vector3.zero;
        public Vector3 HumanLimitCenter     => _ctx?.MeshObject?.HumanLimit?.Center ?? Vector3.zero;
        public float   HumanLimitAxisLength => _ctx?.MeshObject?.HumanLimit?.AxisLength ?? 0f;
        public bool    IsSpringBoneCarrier  => _model != null && Poly_Ling.Ops.SpringBoneOps.IsCarrier(_model, _masterIndex);
        public IReadOnlyList<SpringBoneColliderView> SpringBoneColliders
            => SpringBoneColliderView.ListOf(_ctx?.MeshObject?.SpringBoneColliders);
        public bool   HasSpringBoneChainRoot      => _ctx?.MeshObject?.SpringBoneChainRoot != null;
        public string SpringBoneChainName         => _ctx?.MeshObject?.SpringBoneChainRoot?.Name ?? "";
        public string SpringBoneChainCenterBone   => _ctx?.MeshObject?.SpringBoneChainRoot?.CenterBoneName ?? "";
        public int[]  SpringBoneChainGroupIndices
            => _ctx?.MeshObject?.SpringBoneChainRoot?.SpringBoneColliderGroupIndices?.ToArray() ?? Array.Empty<int>();
        public Poly_Ling.Data.VrmFirstPersonType VrmFirstPerson
            => _ctx?.MeshObject?.VrmFirstPerson ?? Poly_Ling.Data.VrmFirstPersonType.Auto;
        public bool IsVrmFirstPersonCarrier => Poly_Ling.Ops.VrmSettingsOps.IsFirstPersonCarrier(_ctx);

        public PartsIdReportView InspectPartsIds()
        {
            var mo = _ctx?.MeshObject;
            if (mo == null) return null;
            var r = Poly_Ling.Ops.PartsIdAssignOps.Inspect(mo, _ctx.Name ?? "(no name)");
            return new PartsIdReportView
            {
                Summary      = r.Summary,
                IsConsistent = r.CurrentPartCount == r.ConnectedComponentCount && r.SubIdIsSequential,
            };
        }

        /// <summary>法線再計算の除外セット（MeshObject.NormalRecalcExcludeList）。</summary>
        public IReadOnlyList<IPartsSetView> NormalExcludeSets
        {
            get
            {
                var list = _ctx?.MeshObject?.NormalRecalcExcludeList;
                if (list == null || list.Count == 0) return _emptyPartsSetList;
                var result = new IPartsSetView[list.Count];
                for (int i = 0; i < list.Count; i++)
                    result[i] = new LivePartsSetView(list[i]);
                return result;
            }
        }

        // 現在のパーツ選択状態（件数のみ）
        public int SelectedVertexCount => _ctx?.Selection?.Vertices?.Count ?? 0;
        public int SelectedEdgeCount   => _ctx?.Selection?.Edges?.Count   ?? 0;
        public int SelectedFaceCount   => _ctx?.Selection?.Faces?.Count   ?? 0;

        public int HiddenFaceCount
        {
            get
            {
                var mo = _ctx?.MeshObject;
                if (mo == null) return 0;
                int n = 0;
                foreach (var f in mo.Faces)
                    if (f != null && f.VertexCount >= 3 && f.IsHidden) n++;
                return n;
            }
        }
        public int SelectedLineCount   => _ctx?.Selection?.Lines?.Count   ?? 0;

        // 面カウント（詳細パネル用のみ呼ばれるため毎回計算で問題なし）
        private void CountFaces(out int tri, out int quad, out int ngon)
        {
            tri = 0; quad = 0; ngon = 0;
            var meshObj = _ctx.MeshObject;
            if (meshObj == null) return;
            foreach (var face in meshObj.Faces)
            {
                if (face.IsTriangle) tri++;
                else if (face.IsQuad) quad++;
                else ngon++;
            }
        }
    }

    // ================================================================
    // LiveModelView
    // ================================================================

    public class LiveModelView : IModelView
    {
        private readonly ModelContext _model;

        /// <summary>現物ModelContext（ローカル専用操作で使用）</summary>
        public ModelContext ModelContext => _model;

        // メッシュビューリストのキャッシュ
        // RebuildLists()でのみ再構築。Selection/Attributes変更では不要。
        private IMeshView[] _drawableList;
        private IMeshView[] _boneList;
        private IMeshView[] _morphList;
        private IMeshView[] _rigidBodyList;
        private IMeshView[] _rigidBodyJointList;
        private bool _listsDirty = true;

        public LiveModelView(ModelContext model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }

        // 基本情報（毎回ライブで返す）
        public string Name => _model.Name;
        public string FilePath => _model.FilePath;
        public bool IsDirty => _model.IsDirty;

        // カウント
        public int DrawableCount => _model.TypedIndices.DrawableCount;
        public int BoneCount => _model.TypedIndices.BoneCount;
        public int MorphCount => _model.Morphs?.Count ?? 0;
        public int TotalMeshCount => _model.MeshContextCount;

        // 選択（毎回ライブで返す）
        public int[] SelectedDrawableIndices => _model.SelectedDrawableMeshIndices.ToArray();
        public int ActiveMeshIndex => _model.ActiveMeshIndex;

        public IMeshView ActiveMesh
        {
            get
            {
                int i  = _model.ActiveMeshIndex;
                var mc = i >= 0 ? _model.GetMeshContext(i) : null;
                return mc != null ? new LiveMeshView(mc, _model, i) : null;
            }
        }

        public IMeshView GetMesh(int masterIndex)
        {
            var mc = masterIndex >= 0 ? _model.GetMeshContext(masterIndex) : null;
            return mc != null ? new LiveMeshView(mc, _model, masterIndex) : null;
        }

        /// <summary>プロジェクトを持たないので作れない（出力先の解決にプロジェクトが要る）。空。</summary>
        public IReadOnlyList<ObjectGroupView> ObjectGroups => Array.Empty<ObjectGroupView>();

        public int MaterialCount        => _model.MaterialCount;
        public int CurrentMaterialIndex => _model.CurrentMaterialIndex;
        public MaterialSlotView GetMaterialSlot(int slot) => LiveProjectView.BuildMaterialSlotView(_model, slot);
        public IReadOnlyList<MorphExpressionView> MorphExpressions => LiveProjectView.BuildMorphExpressionViews(_model);

        public int    HumanoidMappingCount => (_model.HumanoidMapping == null || _model.HumanoidMapping.IsEmpty) ? 0 : _model.HumanoidMapping.Count;
        public bool   HasAnySkinWeight     => Poly_Ling.Ops.TPoseConverter.HasAnySkinWeight(_model.MeshContextList);
        public bool   HasTPoseBackup       => _model.TPoseBackup != null;
        public string DiagnoseTPose()      => Poly_Ling.Ops.TPoseConverter.Diagnose(_model.MeshContextList, _model.HumanoidMapping);
        public int HumanoidMissingRequiredCount => LiveProjectView.HumanoidMissingRequiredOf(_model);
        public AvatarRetargetView AvatarRetarget => LiveProjectView.BuildAvatarRetargetView(_model);
        public IReadOnlyList<string> SpringBoneColliderGroupNames
            => new List<string>(_model.SpringBoneColliderGroupNames ?? new List<string>());
        public bool HasVrmMeta   => _model.VrmMeta != null;
        public Poly_Ling.Data.VrmMetaData   VrmMetaCopy   => Poly_Ling.Ops.VrmSettingsOps.GetMetaOrNew(_model);
        public bool HasVrmLookAt => _model.VrmLookAt != null;
        public Poly_Ling.Data.VrmLookAtData VrmLookAtCopy => Poly_Ling.Ops.VrmSettingsOps.GetLookAtOrNew(_model);
        public int[] SelectedBoneIndices => _model.SelectedBoneIndices.ToArray();
        public int[] SelectedMorphIndices => _model.SelectedMorphIndices.ToArray();

        // 選択辞書名（毎回ライブで返す。辞書は数個〜数十個で件数が小さいためキャッシュしない）
        public IReadOnlyList<string> MeshSelectionSetNames
        {
            get
            {
                var sets = _model.MeshSelectionSets;
                if (sets == null || sets.Count == 0) return Array.Empty<string>();
                var names = new string[sets.Count];
                for (int i = 0; i < sets.Count; i++) names[i] = sets[i]?.Name ?? "";
                return names;
            }
        }

        // メッシュリスト（キャッシュ。ListStructure変更時のみ再構築）
        public IReadOnlyList<IMeshView> DrawableList { get { EnsureLists(); return _drawableList; } }
        public IReadOnlyList<IMeshView> BoneList { get { EnsureLists(); return _boneList; } }
        public IReadOnlyList<IMeshView> MorphList { get { EnsureLists(); return _morphList; } }
        public IReadOnlyList<IMeshView> RigidBodyList { get { EnsureLists(); return _rigidBodyList; } }
        public IReadOnlyList<IMeshView> RigidBodyJointList { get { EnsureLists(); return _rigidBodyJointList; } }

        /// <summary>リスト構造変更時に呼ぶ。次回アクセス時にリビルド。</summary>
        public void InvalidateLists() { _listsDirty = true; }

        private void EnsureLists()
        {
            if (!_listsDirty) return;
            _drawableList = BuildList(_model.DrawableMeshes);
            _boneList = BuildList(_model.Bones);
            _morphList = BuildList(_model.Morphs);
            _rigidBodyList = BuildList(_model.RigidBodies);
            _rigidBodyJointList = BuildList(_model.RigidBodyJoints);
            _listsDirty = false;
        }

        private IMeshView[] BuildList(IReadOnlyList<TypedMeshEntry> entries)
        {
            if (entries == null || entries.Count == 0) return Array.Empty<IMeshView>();
            var list = new IMeshView[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                list[i] = new LiveMeshView(e.Context, _model, e.MasterIndex);
            }
            return list;
        }
    }

    // ================================================================
    // LiveProjectView
    // ================================================================

    public class LiveProjectView : IProjectView
    {
        private readonly ProjectContext _project;
        private LiveModelView _currentModelView;
        private int _lastModelIndex = -2; // 未初期化を示す値

        /// <summary>現物ProjectContext（ローカル専用操作で使用）</summary>
        public ProjectContext ProjectContext => _project;

        public LiveProjectView(ProjectContext project)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public string ProjectName => _project.Name;
        public int CurrentModelIndex => _project.CurrentModelIndex;
        public int ModelCount => _project.ModelCount;

        public VertexTransferPreviewView PreviewVertexTransfer(
            int srcModelIndex, int srcMeshIndex, int dstModelIndex, int dstMeshIndex,
            Poly_Ling.Ops.VertexMatchMode mode)
            => ComputeVertexTransferPreview(_project, srcModelIndex, srcMeshIndex, dstModelIndex, dstMeshIndex, mode);

        public IReadOnlyList<string> WorkAxisLibraryNames => LibraryNamesOf(_project);

        /// <summary>必須の Humanoid ボーンのうち未割当の数（PlayerHumanoidMappingSubPanel が読んでいた項目）。</summary>
        public static int HumanoidMissingRequiredOf(ModelContext model)
        {
            var m = model?.HumanoidMapping;
            return (m == null || m.IsEmpty) ? 0 : m.GetMissingRequiredBones().Count;
        }

        /// <summary>Avatar リターゲット設定の写しを作る（未設定なら既定値、IsSet=false）。</summary>
        public static AvatarRetargetView BuildAvatarRetargetView(ModelContext model)
        {
            if (model == null) return null;
            var a = Poly_Ling.Ops.AvatarRetargetOps.GetRetargetOrNew(model);
            return new AvatarRetargetView
            {
                IsSet             = model.AvatarRetarget != null,
                UpperArmTwist     = a.UpperArmTwist,
                LowerArmTwist     = a.LowerArmTwist,
                UpperLegTwist     = a.UpperLegTwist,
                LowerLegTwist     = a.LowerLegTwist,
                ArmStretch        = a.ArmStretch,
                LegStretch        = a.LegStretch,
                FeetSpacing       = a.FeetSpacing,
                HasTranslationDoF = a.HasTranslationDoF,
            };
        }

        /// <summary>モーフエクスプレッションの写しを作る（PlayerMorphSubPanel が読んでいた項目）。</summary>
        public static IReadOnlyList<MorphExpressionView> BuildMorphExpressionViews(ModelContext model)
        {
            var list = new List<MorphExpressionView>();
            if (model?.MorphExpressions == null) return list;
            foreach (var s in model.MorphExpressions)
            {
                if (s == null) continue;
                var v = new MorphExpressionView
                {
                    Name        = s.Name ?? "",
                    NameEnglish = s.NameEnglish ?? "",
                    TypeName    = s.Type.ToString(),
                    Panel       = s.Panel,
                    MeshCount   = s.MeshCount,
                };
                foreach (var e in s.MeshEntries)
                {
                    var mc = e.MeshIndex >= 0 && e.MeshIndex < model.MeshContextCount ? model.GetMeshContext(e.MeshIndex) : null;
                    v.Entries.Add(new MorphEntryView { MeshIndex = e.MeshIndex, MeshName = mc?.Name, Weight = e.Weight });
                }
                list.Add(v);
            }
            return list;
        }

        /// <summary>
        /// マテリアルスロットの表示用の写しを作る（PlayerMaterialListSubPanel が Material から読んでいた項目。M-1）。
        /// </summary>
        public static MaterialSlotView BuildMaterialSlotView(ModelContext model, int slot)
        {
            if (model == null || slot < 0 || slot >= model.MaterialCount) return null;
            var mat  = model.GetMaterial(slot);
            var data = model.GetMaterialReference(slot)?.Data;
            var v = new MaterialSlotView { Slot = slot, HasMaterial = mat != null };
            if (mat == null) { v.Name = "(None)"; return v; }

            v.Name               = mat.name;
            v.ShaderName         = mat.shader != null ? mat.shader.name : "";
            v.DetectedShaderType = Poly_Ling.Materials.MaterialDataConverter.DetectShaderType(mat);

            if (mat.HasProperty("_BaseColor") || mat.HasProperty("_Color"))
            {
                v.HasColor   = true;
                v.Color      = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.GetColor("_Color");
                v.SavedColor = data != null ? data.GetBaseColor() : v.Color;
            }

            if (mat.HasProperty("_BaseMap") || mat.HasProperty("_MainTex"))
            {
                v.MainTexProperty = mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                v.MainTexture     = mat.GetTexture(v.MainTexProperty) as UnityEngine.Texture2D;
                v.MainTexName     = v.MainTexture != null ? v.MainTexture.name : "(None)";
            }

            if (mat.HasProperty("_Metallic"))
            {
                v.HasMetallic   = true;
                v.Metallic      = mat.GetFloat("_Metallic");
                v.SavedMetallic = data != null ? data.Metallic : v.Metallic;
            }

            string smoothProp = mat.HasProperty("_Smoothness") ? "_Smoothness"
                              : mat.HasProperty("_Glossiness") ? "_Glossiness" : null;
            if (smoothProp != null)
            {
                v.HasSmoothness   = true;
                v.Smoothness      = mat.GetFloat(smoothProp);
                v.SavedSmoothness = data != null ? data.Smoothness : v.Smoothness;
            }

            v.IsTransparent = Poly_Ling.Materials.MaterialEditOps.IsTransparent(mat);
            return v;
        }

        /// <summary>作業軸ライブラリの登録名（Live・Player の両ビューで共用）。</summary>
        public static IReadOnlyList<string> LibraryNamesOf(ProjectContext project)
        {
            var lib = project?.WorkAxes;
            return lib != null ? new List<string>(lib.Names) : new List<string>();
        }

        /// <summary>本体側の現物から頂点データ転送の下見を計算する（Live・Player の両ビューで共用）。</summary>
        public static VertexTransferPreviewView ComputeVertexTransferPreview(
            ProjectContext project,
            int srcModelIndex, int srcMeshIndex, int dstModelIndex, int dstMeshIndex,
            Poly_Ling.Ops.VertexMatchMode mode)
        {
            var src = project?.GetModel(srcModelIndex)?.GetMeshContext(srcMeshIndex);
            var dst = project?.GetModel(dstModelIndex)?.GetMeshContext(dstMeshIndex);
            if (src?.MeshObject == null || dst?.MeshObject == null) return null;
            var r = Poly_Ling.Ops.VertexDataTransferOps.Preview(src, dst, mode);
            return new VertexTransferPreviewView { Matched = r.Matched, Unmatched = r.Unmatched, Summary = r.Summary };
        }

        /// <summary>
        /// オブジェクトグループの表示用の写しを作る（PlayerObjectGroupSubPanel にあった判定と文面をここへ移した）。
        /// </summary>
        public static IReadOnlyList<ObjectGroupView> BuildObjectGroupViews(ProjectContext project, ModelContext model)
        {
            var list = new List<ObjectGroupView>();
            if (model?.ObjectGroups == null) return list;
            foreach (var g in model.ObjectGroups)
            {
                if (g == null) continue;

                // 出力先はステップごとに複数ありうる。1 つでも引けなければ印を立てる。
                bool outMissing = !g.HasOutput;
                if (!outMissing)
                {
                    foreach (ulong oid in g.OutputObjectIds)
                        if (Poly_Ling.Ops.ObjectGroupOps.Resolve(project, oid) == null) { outMissing = true; break; }
                }

                var stashCtx = g.HasStash ? Poly_Ling.Ops.ObjectGroupOps.Resolve(project, g.StashObjectId) : null;

                var srcNames = new List<string>();
                foreach (ulong id in g.SourceObjectIds)
                {
                    var mc = Poly_Ling.Ops.ObjectGroupOps.Resolve(project, id);
                    srcNames.Add(mc != null ? mc.Name : $"(見つからない: {id})");
                }

                // ステップごとに action と出力先を出す。実行順は並びそのもの。
                var stepLines = new List<string>();
                for (int i = 0; i < g.StepCount; i++)
                {
                    var st = g.GetStep(i);
                    if (st == null) continue;

                    var outNames = new List<string>();
                    foreach (ulong oid in st.OutputObjectIds)
                    {
                        var mc = Poly_Ling.Ops.ObjectGroupOps.Resolve(project, oid);
                        outNames.Add(mc != null ? mc.Name : $"(見つからない: {oid})");
                    }

                    string outText = outNames.Count == 0 ? "なし"
                        : outNames.Count <= 3 ? string.Join(", ", outNames)
                        : $"{outNames[0]} ほか {outNames.Count - 1} 件";

                    stepLines.Add($"  {i + 1}. {st.Action} → {outText}  (パラメータ {st.Args.Count} 件)");
                }

                list.Add(new ObjectGroupView
                {
                    Name          = g.Name,
                    Action        = g.Action,
                    StepCount     = g.StepCount,
                    AutoUpdate    = g.AutoUpdate,
                    OutputMissing = outMissing,
                    Stale         = !outMissing && Poly_Ling.Ops.ObjectGroupOps.IsStale(project, g),
                    Detail        =
                          $"ステップ: {g.StepCount} 件\n"
                        + string.Join("\n", stepLines) + "\n"
                        + $"入力: {(srcNames.Count > 0 ? string.Join(", ", srcNames) : "なし")}\n"
                        + $"退避: {(stashCtx != null ? stashCtx.Name : "なし")}",
                });
            }
            return list;
        }

        public IModelView GetModelView(int index)
        {
            var m = _project.GetModel(index);
            if (m == null) return null;
            if (index == _project.CurrentModelIndex && _currentModelView != null)
                return _currentModelView;
            return new LiveModelView(m);
        }

        public IModelView CurrentModel
        {
            get
            {
                var idx = _project.CurrentModelIndex;
                var model = _project.CurrentModel;

                if (model == null)
                {
                    _currentModelView = null;
                    _lastModelIndex = -1;
                    return null;
                }

                // モデル切り替え時のみ再作成
                if (idx != _lastModelIndex || _currentModelView == null)
                {
                    _currentModelView = new LiveModelView(model);
                    _lastModelIndex = idx;
                }

                return _currentModelView;
            }
        }

        /// <summary>リスト構造変更時に呼ぶ（追加/削除/並べ替え）</summary>
        public void InvalidateLists()
        {
            _currentModelView?.InvalidateLists();
        }
    }

    // ================================================================
    // LivePartsSetView
    // ================================================================

    public class LivePartsSetView : IPartsSetView
    {
        private readonly PartsSelectionSet _set;

        public LivePartsSetView(PartsSelectionSet set)
        {
            _set = set ?? throw new ArgumentNullException(nameof(set));
        }

        public string Name    => _set.Name;
        public MeshSelectMode Mode => _set.Mode;
        public string Summary => _set.Summary;
        public int VertexCount => _set.Vertices?.Count ?? 0;
        public int EdgeCount   => _set.Edges?.Count   ?? 0;
        public int FaceCount   => _set.Faces?.Count   ?? 0;
        public int LineCount   => _set.Lines?.Count   ?? 0;
        public int  VertexIdCount          => _set.VertexIdCount;
        public bool HasResolvableVertexIds => _set.HasResolvableVertexIds;
    }
}
