// Assets/Editor/Poly_Ling/Selection/SelectionSet.cs
// 名前付き選択セット
// メッシュ単位で選択状態を保存・復元

using Poly_Ling.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Poly_Ling.Selection
{
    /// <summary>
    /// 名前付き選択セット
    /// 頂点/エッジ/面/線の選択状態を保存
    /// </summary>
    [Serializable]
    public class PartsSelectionSet
    {
        /// <summary>セット名</summary>
        public string Name { get; set; } = "PartsSelectionSet";

        /// <summary>作成日時</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>選択モード（保存時のモード）</summary>
        public MeshSelectMode Mode { get; set; } = MeshSelectMode.Vertex;

        /// <summary>選択中の頂点インデックス</summary>
        public HashSet<int> Vertices { get; set; } = new HashSet<int>();

        /// <summary>選択中のエッジ（頂点ペア）</summary>
        public HashSet<VertexPair> Edges { get; set; } = new HashSet<VertexPair>();

        /// <summary>選択中の面インデックス</summary>
        public HashSet<int> Faces { get; set; } = new HashSet<int>();

        /// <summary>選択中の線分インデックス</summary>
        public HashSet<int> Lines { get; set; } = new HashSet<int>();

        /// <summary>色（UI表示用、オプション）</summary>
        public Color Color { get; set; } = Color.yellow;

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>選択要素数の合計</summary>
        public int TotalCount => Vertices.Count + Edges.Count + Faces.Count + Lines.Count;

        /// <summary>空かどうか</summary>
        public bool IsEmpty => TotalCount == 0;

        /// <summary>サマリー文字列（UI表示用）</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (Vertices.Count > 0) parts.Add($"V:{Vertices.Count}");
                if (Edges.Count > 0) parts.Add($"E:{Edges.Count}");
                if (Faces.Count > 0) parts.Add($"F:{Faces.Count}");
                if (Lines.Count > 0) parts.Add($"L:{Lines.Count}");
                return parts.Count > 0 ? string.Join(" ", parts) : "(empty)";
            }
        }

        // ================================================================
        // コンストラクタ
        // ================================================================

        public PartsSelectionSet() { }

        public PartsSelectionSet(string name)
        {
            Name = name;
        }

        // ================================================================
        // ファクトリメソッド
        // ================================================================

        /// <summary>
        /// MeshSelectionSnapshotから作成
        /// </summary>
        public static PartsSelectionSet FromSnapshot(MeshSelectionSnapshot snapshot, string name)
        {
            if (snapshot == null) return new PartsSelectionSet(name);

            return new PartsSelectionSet(name)
            {
                Mode = snapshot.Mode,
                Vertices = new HashSet<int>(snapshot.Vertices ?? new HashSet<int>()),
                Edges = new HashSet<VertexPair>(snapshot.Edges ?? new HashSet<VertexPair>()),
                Faces = new HashSet<int>(snapshot.Faces ?? new HashSet<int>()),
                Lines = new HashSet<int>(snapshot.Lines ?? new HashSet<int>())
            };
        }

        /// <summary>
        /// 現在の選択状態から作成
        /// </summary>
        public static PartsSelectionSet FromCurrentSelection(
            string name,
            HashSet<int> vertices,
            HashSet<VertexPair> edges,
            HashSet<int> faces,
            HashSet<int> lines,
            MeshSelectMode mode)
        {
            return new PartsSelectionSet(name)
            {
                Mode = mode,
                Vertices = new HashSet<int>(vertices ?? new HashSet<int>()),
                Edges = new HashSet<VertexPair>(edges ?? new HashSet<VertexPair>()),
                Faces = new HashSet<int>(faces ?? new HashSet<int>()),
                Lines = new HashSet<int>(lines ?? new HashSet<int>())
            };
        }

        // ================================================================
        // 変換
        // ================================================================

        /// <summary>
        /// MeshSelectionSnapshotに変換
        /// </summary>
        public MeshSelectionSnapshot ToSnapshot()
        {
            return new MeshSelectionSnapshot
            {
                Mode = Mode,
                Vertices = new HashSet<int>(Vertices),
                Edges = new HashSet<VertexPair>(Edges),
                Faces = new HashSet<int>(Faces),
                Lines = new HashSet<int>(Lines)
            };
        }

        /// <summary>
        /// クローン作成
        /// </summary>
        public PartsSelectionSet Clone()
        {
            return new PartsSelectionSet(Name)
            {
                CreatedAt = CreatedAt,
                Mode = Mode,
                Vertices = new HashSet<int>(Vertices),
                Edges = new HashSet<VertexPair>(Edges),
                Faces = new HashSet<int>(Faces),
                Lines = new HashSet<int>(Lines),
                Color = Color
            };
        }

        // ================================================================
        // 集合演算
        // ================================================================

        /// <summary>
        /// 他のセットを追加（Union）
        /// </summary>
        public void Add(PartsSelectionSet other)
        {
            if (other == null) return;
            Vertices.UnionWith(other.Vertices);
            Edges.UnionWith(other.Edges);
            Faces.UnionWith(other.Faces);
            Lines.UnionWith(other.Lines);
        }

        /// <summary>
        /// 他のセットを除外（Subtract）
        /// </summary>
        public void Subtract(PartsSelectionSet other)
        {
            if (other == null) return;
            Vertices.ExceptWith(other.Vertices);
            Edges.ExceptWith(other.Edges);
            Faces.ExceptWith(other.Faces);
            Lines.ExceptWith(other.Lines);
        }

        /// <summary>
        /// 他のセットとの交差（Intersect）
        /// </summary>
        public void Intersect(PartsSelectionSet other)
        {
            if (other == null)
            {
                Clear();
                return;
            }
            Vertices.IntersectWith(other.Vertices);
            Edges.IntersectWith(other.Edges);
            Faces.IntersectWith(other.Faces);
            Lines.IntersectWith(other.Lines);
        }

        /// <summary>
        /// クリア
        /// </summary>
        public void Clear()
        {
            Vertices.Clear();
            Edges.Clear();
            Faces.Clear();
            Lines.Clear();
        }

        // ================================================================
        // 検証
        // ================================================================

        /// <summary>
        /// 無効なインデックスを除去（メッシュ変更後に使用）
        /// </summary>
        public void ValidateAgainstMesh(int vertexCount, int faceCount)
        {
            Vertices.RemoveWhere(i => i < 0 || i >= vertexCount);
            Faces.RemoveWhere(i => i < 0 || i >= faceCount);
            Lines.RemoveWhere(i => i < 0 || i >= faceCount);

            // エッジの検証
            Edges.RemoveWhere(e => e.V1 < 0 || e.V1 >= vertexCount || e.V2 < 0 || e.V2 >= vertexCount);
        }
    }

    // ================================================================
    // シリアライズ用DTO
    // ================================================================

    /// <summary>
    /// SelectionSetのシリアライズ用DTO
    /// </summary>
    [Serializable]
    public class SelectionSetDTO
    {
        public string name;
        public string createdAt;
        public string mode;
        public List<int> vertices;
        public List<int[]> edges;  // [[v1,v2], [v1,v2], ...]
        public List<int> faces;
        public List<int> lines;
        public float[] color;  // [r, g, b, a]

        /// <summary>
        /// SelectionSetからDTOを作成
        /// </summary>
        public static SelectionSetDTO FromSelectionSet(PartsSelectionSet set)
        {
            if (set == null) return null;

            var dto = new SelectionSetDTO
            {
                name = set.Name,
                createdAt = set.CreatedAt.ToString("o"),
                mode = set.Mode.ToString(),
                vertices = set.Vertices.ToList(),
                edges = set.Edges.Select(e => new int[] { e.V1, e.V2 }).ToList(),
                faces = set.Faces.ToList(),
                lines = set.Lines.ToList(),
                color = new float[] { set.Color.r, set.Color.g, set.Color.b, set.Color.a }
            };

            return dto;
        }

        /// <summary>
        /// DTOからSelectionSetを復元
        /// </summary>
        public PartsSelectionSet ToSelectionSet()
        {
            var set = new PartsSelectionSet(name ?? "SelectionSet");

            // CreatedAt
            if (!string.IsNullOrEmpty(createdAt) && DateTime.TryParse(createdAt, out var dt))
            {
                set.CreatedAt = dt;
            }

            // Mode
            if (!string.IsNullOrEmpty(mode) && Enum.TryParse<MeshSelectMode>(mode, out var m))
            {
                set.Mode = m;
            }

            // Vertices
            if (vertices != null)
            {
                set.Vertices = new HashSet<int>(vertices);
            }

            // Edges
            if (edges != null)
            {
                set.Edges = new HashSet<VertexPair>();
                foreach (var e in edges)
                {
                    if (e != null && e.Length >= 2)
                    {
                        set.Edges.Add(new VertexPair(e[0], e[1]));
                    }
                }
            }

            // Faces
            if (faces != null)
            {
                set.Faces = new HashSet<int>(faces);
            }

            // Lines
            if (lines != null)
            {
                set.Lines = new HashSet<int>(lines);
            }

            // Color
            if (color != null && color.Length >= 4)
            {
                set.Color = new Color(color[0], color[1], color[2], color[3]);
            }

            return set;
        }
    }
}
