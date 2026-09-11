// UVVertexId.cs
// UV 頂点識別子（PlayerUVEditorSubPanel から分離）。
// Runtime/Poly_Ling_Player/View/SubPanels/UV/ に配置（PlayerUVEditorSubPanel.cs と同じ名前空間。PlayerUVEditorSubPanel.cs から分割）

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    // ================================================================
    // UV頂点識別子
    // ================================================================

    public readonly struct UVVertexId : IEquatable<UVVertexId>
    {
        public readonly int VertexIndex;
        public readonly int UVIndex;

        public UVVertexId(int vertexIndex, int uvIndex)
        {
            VertexIndex = vertexIndex;
            UVIndex     = uvIndex;
        }

        public bool Equals(UVVertexId other) =>
            VertexIndex == other.VertexIndex && UVIndex == other.UVIndex;

        public override bool Equals(object obj) =>
            obj is UVVertexId o && Equals(o);

        public override int GetHashCode() => (VertexIndex << 16) ^ UVIndex;

        public static bool operator ==(UVVertexId a, UVVertexId b) => a.Equals(b);
        public static bool operator !=(UVVertexId a, UVVertexId b) => !a.Equals(b);
    }
}
