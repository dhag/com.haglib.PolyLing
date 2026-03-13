// MeshSummary.cs
// MeshContextからUI表示に必要なメタデータのみを抽出した構造体
// IMeshView/IBonePoseViewを実装し、統一インタフェースとして使用可能

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Model;

namespace Poly_Ling.Data
{
    /// <summary>BonePoseDataの表示用サマリー</summary>
    public readonly struct BonePoseSummary : IBonePoseView
    {
        public bool HasPose { get; }
        public bool IsActive { get; }
        public int LayerCount { get; }
        public Vector3 ResultPosition { get; }
        public Vector3 ResultRotationEuler { get; }
        public Vector3 BindPosePosition { get; }
        public Vector3 BindPoseRotationEuler { get; }
        public Vector3 BindPoseScale { get; }

        public static readonly BonePoseSummary Empty = new BonePoseSummary();

        public BonePoseSummary(
            bool isActive,
            int layerCount, Vector3 resultPos, Vector3 resultRotEuler,
            Vector3 bindPosePos, Vector3 bindPoseRotEuler, Vector3 bindPoseScale)
        {
            HasPose = true;
            IsActive = isActive;
            LayerCount = layerCount;
            ResultPosition = resultPos;
            ResultRotationEuler = resultRotEuler;
            BindPosePosition = bindPosePos;
            BindPoseRotationEuler = bindPoseRotEuler;
            BindPoseScale = bindPoseScale;
        }
    }

    public readonly struct MeshSummary : IMeshView
    {
        // 基本情報
        public int MasterIndex { get; }
        public string Name { get; }
        public MeshType Type { get; }

        // 統計情報
        public int VertexCount { get; }
        public int FaceCount { get; }
        public int TriCount { get; }
        public int QuadCount { get; }
        public int NgonCount { get; }

        // 属性フラグ
        public bool IsVisible { get; }
        public bool IsLocked { get; }
        public bool IsSkinned { get; }
        public bool IsFolding { get; }
        public Vector3 LocalPosition { get; }
        public Vector3 LocalRotationEuler { get; }
        public Vector3 LocalScale { get; }

        // 階層情報
        public int Depth { get; }
        public int HierarchyParentIndex { get; }

        // ミラー情報
        public int MirrorType { get; }
        public bool IsBakedMirror { get; }
        public bool IsMirrorSide { get; }
        public bool IsRealSide { get; }
        public bool HasBakedMirrorChild { get; }

        // ボーン情報
        public int BoneIndex { get; }
        public BonePoseSummary BonePoseData { get; }

        // モーフ情報
        public bool IsMorph { get; }
        public int MorphParentIndex { get; }
        public string MorphName { get; }
        public bool ExcludeFromExport { get; }

        // IMeshView.BonePose（IBonePoseViewとして返す）
        IBonePoseView IMeshView.BonePose => BonePoseData;

        // パーツ選択辞書（スナップショットでは常に空）
        int IMeshView.PartsSelectionSetCount => 0;
        IReadOnlyList<IPartsSetView> IMeshView.PartsSelectionSets => System.Array.Empty<IPartsSetView>();
        int IMeshView.SelectedVertexCount => 0;
        int IMeshView.SelectedEdgeCount   => 0;
        int IMeshView.SelectedFaceCount   => 0;
        int IMeshView.SelectedLineCount   => 0;

        // 表示用プロパティ
        public string MirrorTypeDisplay
        {
            get
            {
                if (IsBakedMirror) return "\U0001FA9E";
                return MirrorType switch { 1 => "\u21C6X", 2 => "\u21C6Y", 3 => "\u21C6Z", _ => "" };
            }
        }

        public string InfoString => $"V:{VertexCount} F:{FaceCount}";
        public bool HasMirrorIcon => MirrorType > 0 || IsBakedMirror || IsMirrorSide || IsRealSide || HasBakedMirrorChild;

        // 最小コンストラクタ
        public MeshSummary(int masterIndex, string name, MeshType type)
        {
            MasterIndex = masterIndex; Name = name ?? "Untitled"; Type = type;
            VertexCount = 0; FaceCount = 0; TriCount = 0; QuadCount = 0; NgonCount = 0;
            IsVisible = true; IsLocked = false; IsSkinned = false; IsFolding = false;
            LocalPosition = Vector3.zero; LocalRotationEuler = Vector3.zero; LocalScale = Vector3.one;
            Depth = 0; HierarchyParentIndex = -1;
            MirrorType = 0; IsBakedMirror = false; IsMirrorSide = false; IsRealSide = false; HasBakedMirrorChild = false;
            BoneIndex = -1; BonePoseData = BonePoseSummary.Empty;
            IsMorph = false; MorphParentIndex = -1; MorphName = ""; ExcludeFromExport = false;
        }

        // フルコンストラクタ
        public MeshSummary(
            int masterIndex, string name, MeshType type,
            int vertexCount, int faceCount, int triCount, int quadCount, int ngonCount,
            bool isVisible, bool isLocked, bool isSkinned, bool isFolding,
            Vector3 localPosition, Vector3 localRotationEuler, Vector3 localScale,
            int depth, int hierarchyParentIndex,
            int mirrorType, bool isBakedMirror, bool isMirrorSide, bool isRealSide, bool hasBakedMirrorChild,
            int boneIndex, BonePoseSummary bonePose,
            bool isMorph, int morphParentIndex, string morphName, bool excludeFromExport)
        {
            MasterIndex = masterIndex; Name = name ?? "Untitled"; Type = type;
            VertexCount = vertexCount; FaceCount = faceCount;
            TriCount = triCount; QuadCount = quadCount; NgonCount = ngonCount;
            IsVisible = isVisible; IsLocked = isLocked; IsSkinned = isSkinned; IsFolding = isFolding;
            LocalPosition = localPosition; LocalRotationEuler = localRotationEuler; LocalScale = localScale;
            Depth = depth; HierarchyParentIndex = hierarchyParentIndex;
            MirrorType = mirrorType; IsBakedMirror = isBakedMirror;
            IsMirrorSide = isMirrorSide; IsRealSide = isRealSide; HasBakedMirrorChild = hasBakedMirrorChild;
            BoneIndex = boneIndex; BonePoseData = bonePose;
            IsMorph = isMorph; MorphParentIndex = morphParentIndex;
            MorphName = morphName ?? ""; ExcludeFromExport = excludeFromExport;
        }

        // 移行期互換用ファクトリ
        public static MeshSummary FromContext(MeshContext ctx, ModelContext model, int masterIndex)
        {
            if (ctx == null) return new MeshSummary(masterIndex, "Untitled", MeshType.Mesh);

            var meshObj = ctx.MeshObject;
            int vertexCount = meshObj?.VertexCount ?? 0;
            int faceCount = meshObj?.FaceCount ?? 0;
            int tri = 0, quad = 0, ngon = 0;
            if (meshObj != null)
            {
                foreach (var face in meshObj.Faces)
                {
                    if (face.IsTriangle) tri++;
                    else if (face.IsQuad) quad++;
                    else ngon++;
                }
            }

            bool isMirrorSide = model != null && model.IsMirrorSide(ctx);
            bool isRealSide = model != null && model.IsRealSide(ctx);
            int boneIndex = model?.TypedIndices?.MasterToBoneIndex(masterIndex) ?? -1;

            var bonePose = BonePoseSummary.Empty;
            if (ctx.BonePoseData != null)
            {
                var bp = ctx.BonePoseData;
                var bindPos = (Vector3)ctx.BindPose.GetColumn(3);
                var bindQuat = ctx.BindPose.rotation;
                var bindRot = IsQuatValid(bindQuat) ? bindQuat.eulerAngles : Vector3.zero;
                var bindScl = ctx.BindPose.lossyScale;
                var resultRotEuler = IsQuatValid(bp.Rotation) ? bp.Rotation.eulerAngles : Vector3.zero;
                bonePose = new BonePoseSummary(
                    bp.IsActive,
                    bp.LayerCount, bp.Position, resultRotEuler,
                    bindPos, bindRot, bindScl);
            }

            return new MeshSummary(
                masterIndex, ctx.Name ?? "Untitled", ctx.Type,
                vertexCount, faceCount, tri, quad, ngon,
                ctx.IsVisible, ctx.IsLocked, ctx.MeshObject?.IsSkinned ?? false, ctx.IsFolding,
                ctx.BoneTransform?.Position ?? Vector3.zero,
                ctx.BoneTransform?.Rotation ?? Vector3.zero,
                ctx.BoneTransform?.Scale ?? Vector3.one,
                ctx.Depth, ctx.HierarchyParentIndex,
                ctx.MirrorType, ctx.IsBakedMirror, isMirrorSide, isRealSide, ctx.HasBakedMirrorChild,
                boneIndex, bonePose,
                ctx.IsMorph, ctx.MorphParentIndex, ctx.MorphName, ctx.ExcludeFromExport);
        }

        public override string ToString() => $"[{MasterIndex}] {Name} ({Type}) V:{VertexCount} F:{FaceCount}";

        private static bool IsQuatValid(Quaternion q)
        {
            return !float.IsNaN(q.x) && !float.IsNaN(q.y) && !float.IsNaN(q.z) && !float.IsNaN(q.w)
                && (q.x != 0 || q.y != 0 || q.z != 0 || q.w != 0);
        }
    }
}
