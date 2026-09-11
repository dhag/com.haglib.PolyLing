// MeshSelectionSnapshot.cs
// MeshContext 用の選択スナップショット（MeshContext.cs から分離）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（MeshContext.cs と同じ名前空間。MeshContext.cs から分割）

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
    // ================================================================
    // MeshContext用選択スナップショット
    // ================================================================

    /// <summary>
    /// MeshContext用の選択状態スナップショット
    /// メッシュ切り替え時の保存/復元、Undo/Redo、シリアライズに使用
    /// </summary>
    [Serializable]
    public class MeshSelectionSnapshot
    {
        /// <summary>選択モード</summary>
        public MeshSelectMode Mode;

        /// <summary>選択中の頂点インデックス</summary>
        public HashSet<int> Vertices;

        /// <summary>選択中のエッジ（頂点ペア）</summary>
        public HashSet<VertexPair> Edges;

        /// <summary>選択中の面インデックス</summary>
        public HashSet<int> Faces;

        /// <summary>選択中の線分インデックス</summary>
        public HashSet<int> Lines;

        /// <summary>デフォルトコンストラクタ</summary>
        public MeshSelectionSnapshot()
        {
            Mode = MeshSelectMode.Vertex;
            Vertices = new HashSet<int>();
            Edges = new HashSet<VertexPair>();
            Faces = new HashSet<int>();
            Lines = new HashSet<int>();
        }

        /// <summary>クローンを作成</summary>
        public MeshSelectionSnapshot Clone()
        {
            return new MeshSelectionSnapshot
            {
                Mode = this.Mode,
                Vertices = new HashSet<int>(this.Vertices ?? new HashSet<int>()),
                Edges = new HashSet<VertexPair>(this.Edges ?? new HashSet<VertexPair>()),
                Faces = new HashSet<int>(this.Faces ?? new HashSet<int>()),
                Lines = new HashSet<int>(this.Lines ?? new HashSet<int>())
            };
        }

        /// <summary>差異があるか判定</summary>
        public bool IsDifferentFrom(MeshSelectionSnapshot other)
        {
            if (other == null) return true;
            if (Mode != other.Mode) return true;
            if (!SetEquals(Vertices, other.Vertices)) return true;
            if (!SetEquals(Edges, other.Edges)) return true;
            if (!SetEquals(Faces, other.Faces)) return true;
            if (!SetEquals(Lines, other.Lines)) return true;
            return false;
        }

        private static bool SetEquals<T>(HashSet<T> a, HashSet<T> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.SetEquals(b);
        }

        /// <summary>選択があるか</summary>
        public bool HasSelection =>
            (Vertices?.Count ?? 0) > 0 ||
            (Edges?.Count ?? 0) > 0 ||
            (Faces?.Count ?? 0) > 0 ||
            (Lines?.Count ?? 0) > 0;

        /// <summary>全てクリア</summary>
        public void Clear()
        {
            Vertices?.Clear();
            Edges?.Clear();
            Faces?.Clear();
            Lines?.Clear();
        }

        /// <summary>
        /// SelectionSnapshotから変換（既存システムとの互換）
        /// </summary>
        public static MeshSelectionSnapshot FromSelectionSnapshot(SelectionSnapshot snapshot)
        {
            if (snapshot == null) return new MeshSelectionSnapshot();

            return new MeshSelectionSnapshot
            {
                Mode = snapshot.Mode,
                Vertices = new HashSet<int>(snapshot.Vertices ?? new HashSet<int>()),
                Edges = new HashSet<VertexPair>(snapshot.Edges ?? new HashSet<VertexPair>()),
                Faces = new HashSet<int>(snapshot.Faces ?? new HashSet<int>()),
                Lines = new HashSet<int>(snapshot.Lines ?? new HashSet<int>())
            };
        }

        /// <summary>
        /// SelectionSnapshotへ変換（既存システムとの互換）
        /// </summary>
        public SelectionSnapshot ToSelectionSnapshot()
        {
            return new SelectionSnapshot
            {
                Mode = this.Mode,
                Vertices = new HashSet<int>(this.Vertices ?? new HashSet<int>()),
                Edges = new HashSet<VertexPair>(this.Edges ?? new HashSet<VertexPair>()),
                Faces = new HashSet<int>(this.Faces ?? new HashSet<int>()),
                Lines = new HashSet<int>(this.Lines ?? new HashSet<int>())
            };
        }
    }
}
