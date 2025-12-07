// Assets/Editor/MeshFactory/Selection/EdgeTypes.cs
// エッジ関連の基本型定義

using System;

namespace MeshFactory.Selection
{
    /// <summary>
    /// 頂点ペア（正規化済み）
    /// Edge と AuxLine の共通基盤、幾何学的な一致判定に使用
    /// </summary>
    public readonly struct VertexPair : IEquatable<VertexPair>
    {
        /// <summary>頂点インデックス1（常に V1 &lt;= V2）</summary>
        public readonly int V1;
        
        /// <summary>頂点インデックス2（常に V1 &lt;= V2）</summary>
        public readonly int V2;

        public VertexPair(int a, int b)
        {
            if (a <= b) { V1 = a; V2 = b; }
            else { V1 = b; V2 = a; }
        }

        public bool IsValid => V1 >= 0 && V2 >= 0 && V1 != V2;
        public bool Contains(int vertexIndex) => V1 == vertexIndex || V2 == vertexIndex;

        public int GetSharedVertex(VertexPair other)
        {
            if (V1 == other.V1 || V1 == other.V2) return V1;
            if (V2 == other.V1 || V2 == other.V2) return V2;
            return -1;
        }

        public int GetOtherVertex(int vertexIndex)
        {
            if (V1 == vertexIndex) return V2;
            if (V2 == vertexIndex) return V1;
            return -1;
        }

        public bool Equals(VertexPair other) => V1 == other.V1 && V2 == other.V2;
        public override bool Equals(object obj) => obj is VertexPair other && Equals(other);
        public override int GetHashCode() => unchecked((V1 * 397) ^ V2);
        public static bool operator ==(VertexPair left, VertexPair right) => left.Equals(right);
        public static bool operator !=(VertexPair left, VertexPair right) => !left.Equals(right);
        public override string ToString() => $"({V1}, {V2})";
    }

    /// <summary>
    /// 面のエッジ（トポロジ情報付き）
    /// </summary>
    public readonly struct FaceEdge : IEquatable<FaceEdge>
    {
        public readonly VertexPair Pair;
        public readonly int FaceIndex;
        public readonly int EdgeIndexInFace;

        public int V1 => Pair.V1;
        public int V2 => Pair.V2;

        public FaceEdge(int v1, int v2, int faceIndex, int edgeIndexInFace)
        {
            Pair = new VertexPair(v1, v2);
            FaceIndex = faceIndex;
            EdgeIndexInFace = edgeIndexInFace;
        }

        public FaceEdge(VertexPair pair, int faceIndex, int edgeIndexInFace)
        {
            Pair = pair;
            FaceIndex = faceIndex;
            EdgeIndexInFace = edgeIndexInFace;
        }

        public bool IsValid => Pair.IsValid && FaceIndex >= 0;

        public bool Equals(FaceEdge other) => 
            Pair.Equals(other.Pair) && FaceIndex == other.FaceIndex && EdgeIndexInFace == other.EdgeIndexInFace;
        public override bool Equals(object obj) => obj is FaceEdge other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Pair.GetHashCode();
                hash = (hash * 397) ^ FaceIndex;
                hash = (hash * 397) ^ EdgeIndexInFace;
                return hash;
            }
        }
        public static bool operator ==(FaceEdge left, FaceEdge right) => left.Equals(right);
        public static bool operator !=(FaceEdge left, FaceEdge right) => !left.Equals(right);
        public override string ToString() => $"Edge({V1}-{V2}) in Face[{FaceIndex}]";
    }

    /// <summary>
    /// 補助線分（独立エンティティ）
    /// </summary>
    public readonly struct AuxLine : IEquatable<AuxLine>
    {
        public readonly VertexPair Pair;
        public readonly int FaceIndex;

        public int V1 => Pair.V1;
        public int V2 => Pair.V2;

        public AuxLine(int v1, int v2, int faceIndex)
        {
            Pair = new VertexPair(v1, v2);
            FaceIndex = faceIndex;
        }

        public AuxLine(VertexPair pair, int faceIndex)
        {
            Pair = pair;
            FaceIndex = faceIndex;
        }

        public bool IsValid => Pair.IsValid && FaceIndex >= 0;

        public bool Equals(AuxLine other) => Pair.Equals(other.Pair) && FaceIndex == other.FaceIndex;
        public override bool Equals(object obj) => obj is AuxLine other && Equals(other);
        public override int GetHashCode() => unchecked((Pair.GetHashCode() * 397) ^ FaceIndex);
        public static bool operator ==(AuxLine left, AuxLine right) => left.Equals(right);
        public static bool operator !=(AuxLine left, AuxLine right) => !left.Equals(right);
        public override string ToString() => $"AuxLine({V1}-{V2}) [Face {FaceIndex}]";
    }
}
