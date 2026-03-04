// MeshSummary.cs
// MeshContextからUI表示に必要なメタデータのみを抽出した構造体

using Poly_Ling.Model;

namespace Poly_Ling.Data
{
    public readonly struct MeshSummary
    {
        // 基本情報
        public readonly int MasterIndex;
        public readonly string Name;
        public readonly MeshType Type;

        // 統計情報
        public readonly int VertexCount;
        public readonly int FaceCount;
        public readonly int TriCount;
        public readonly int QuadCount;
        public readonly int NgonCount;

        // 属性フラグ
        public readonly bool IsVisible;
        public readonly bool IsLocked;
        public readonly bool IsFolding;

        // ミラー情報
        public readonly int MirrorType;
        public readonly bool IsBakedMirror;
        public readonly bool IsMirrorSide;
        public readonly bool IsRealSide;
        public readonly bool HasBakedMirrorChild;

        // ボーン情報
        public readonly int BoneIndex;

        // モーフ情報
        public readonly bool IsMorph;
        public readonly int MorphParentIndex;
        public readonly string MorphName;
        public readonly bool ExcludeFromExport;

        // 表示用
        public string TypeShort => Type switch
        {
            MeshType.Mesh => "Mesh",
            MeshType.Bone => "Bone",
            MeshType.Morph => "Morph",
            MeshType.BakedMirror => "Mirror",
            MeshType.RigidBody => "Rigid",
            MeshType.RigidBodyJoint => "Joint",
            MeshType.Helper => "Help",
            MeshType.Group => "Group",
            MeshType.MirrorSide => "MirSide",
            _ => "?"
        };

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

            return new MeshSummary(masterIndex, ctx.Name ?? "Untitled", ctx.Type,
                vertexCount, faceCount, tri, quad, ngon,
                ctx.IsVisible, ctx.IsLocked, ctx.IsFolding,
                ctx.MirrorType, ctx.IsBakedMirror, isMirrorSide, isRealSide, ctx.HasBakedMirrorChild,
                boneIndex, ctx.IsMorph, ctx.MorphParentIndex, ctx.MorphName, ctx.ExcludeFromExport);
        }

        public MeshSummary(int masterIndex, string name, MeshType type)
        {
            MasterIndex = masterIndex; Name = name ?? "Untitled"; Type = type;
            VertexCount = 0; FaceCount = 0; TriCount = 0; QuadCount = 0; NgonCount = 0;
            IsVisible = true; IsLocked = false; IsFolding = false;
            MirrorType = 0; IsBakedMirror = false; IsMirrorSide = false; IsRealSide = false; HasBakedMirrorChild = false;
            BoneIndex = -1; IsMorph = false; MorphParentIndex = -1; MorphName = ""; ExcludeFromExport = false;
        }

        public MeshSummary(
            int masterIndex, string name, MeshType type,
            int vertexCount, int faceCount, int triCount, int quadCount, int ngonCount,
            bool isVisible, bool isLocked, bool isFolding,
            int mirrorType, bool isBakedMirror, bool isMirrorSide, bool isRealSide, bool hasBakedMirrorChild,
            int boneIndex, bool isMorph, int morphParentIndex, string morphName, bool excludeFromExport)
        {
            MasterIndex = masterIndex; Name = name ?? "Untitled"; Type = type;
            VertexCount = vertexCount; FaceCount = faceCount;
            TriCount = triCount; QuadCount = quadCount; NgonCount = ngonCount;
            IsVisible = isVisible; IsLocked = isLocked; IsFolding = isFolding;
            MirrorType = mirrorType; IsBakedMirror = isBakedMirror;
            IsMirrorSide = isMirrorSide; IsRealSide = isRealSide; HasBakedMirrorChild = hasBakedMirrorChild;
            BoneIndex = boneIndex; IsMorph = isMorph; MorphParentIndex = morphParentIndex;
            MorphName = morphName ?? ""; ExcludeFromExport = excludeFromExport;
        }

        public override string ToString() => $"[{MasterIndex}] {Name} ({Type}) V:{VertexCount} F:{FaceCount}";
    }
}
