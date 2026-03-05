// LiveViews.cs
// ローカル用ビュー実装
// ProjectContext/ModelContext/MeshContext を直接参照し、
// プロパティアクセス時にリアルタイムで値を返す
// SummaryBuilderによるスナップショット生成を完全にスキップ可能

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Model;

namespace Poly_Ling.Data
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
        public Vector3 RestPosition => _ctx?.BonePoseData?.RestPosition ?? Vector3.zero;

        public Vector3 RestRotationEuler
        {
            get
            {
                var bp = _ctx?.BonePoseData;
                if (bp == null) return Vector3.zero;
                return IsQuatValid(bp.RestRotation) ? bp.RestRotation.eulerAngles : Vector3.zero;
            }
        }

        public Vector3 RestScale => _ctx?.BonePoseData?.RestScale ?? Vector3.one;
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
        public Vector3 RestPosition => Vector3.zero;
        public Vector3 RestRotationEuler => Vector3.zero;
        public Vector3 RestScale => Vector3.one;
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
        public bool IsFolding => _ctx.IsFolding;

        // 階層
        public int Depth => _ctx.Depth;
        public int HierarchyParentIndex => _ctx.HierarchyParentIndex;

        // ミラー
        public int MirrorType => _ctx.MirrorType;
        public bool IsBakedMirror => _ctx.IsBakedMirror;
        public bool IsMirrorSide => _model != null && _model.IsMirrorSide(_ctx);
        public bool IsRealSide => _model != null && _model.IsRealSide(_ctx);
        public bool HasBakedMirrorChild => _ctx.HasBakedMirrorChild;

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

        // 表示用計算プロパティ
        public string InfoString => $"V:{VertexCount} F:{FaceCount}";

        public string MirrorTypeDisplay
        {
            get
            {
                if (IsBakedMirror) return "\U0001FA9E";
                return MirrorType switch { 1 => "\u21C6X", 2 => "\u21C6Y", 3 => "\u21C6Z", _ => "" };
            }
        }

        public bool HasMirrorIcon => MirrorType > 0 || IsBakedMirror || IsMirrorSide || IsRealSide || HasBakedMirrorChild;

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
        public int[] SelectedDrawableIndices => _model.SelectedMeshIndices.ToArray();
        public int[] SelectedBoneIndices => _model.SelectedBoneIndices.ToArray();
        public int[] SelectedMorphIndices => _model.SelectedMorphIndices.ToArray();

        // メッシュリスト（キャッシュ。ListStructure変更時のみ再構築）
        public IReadOnlyList<IMeshView> DrawableList { get { EnsureLists(); return _drawableList; } }
        public IReadOnlyList<IMeshView> BoneList { get { EnsureLists(); return _boneList; } }
        public IReadOnlyList<IMeshView> MorphList { get { EnsureLists(); return _morphList; } }

        /// <summary>リスト構造変更時に呼ぶ。次回アクセス時にリビルド。</summary>
        public void InvalidateLists() { _listsDirty = true; }

        private void EnsureLists()
        {
            if (!_listsDirty) return;
            _drawableList = BuildList(_model.DrawableMeshes);
            _boneList = BuildList(_model.Bones);
            _morphList = BuildList(_model.Morphs);
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
}
