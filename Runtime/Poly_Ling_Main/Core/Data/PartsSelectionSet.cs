// Assets/Editor/Poly_Ling/Selection/SelectionSet.cs
// 名前付き選択セット
// メッシュ単位で選択状態を保存・復元

using Poly_Ling.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Selection;

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

        /// <summary>
        /// 頂点インデックス → 識別子（頂点ID / 部品ID / サブID）。
        /// Vertices の各要素と 1 対 1 で対応する控え。
        ///
        /// 【後付けなので欠けを許す】
        ///   Vertices に有って本辞書に無い索引は「そのとき控えなかった」の意味。
        ///   引き直し（ResolveByVertexId）で解決できなかった項目は、
        ///   索引が別の項目に取られていない限りそのまま残す。よって
        ///   本辞書に有って Vertices に無い索引も起こりうる。
        ///
        /// 【引き当ては Id だけ】
        ///   PartsId / SubId は保持と往復のみ（VertexIdTriple.cs 参照）。
        /// </summary>
        public Dictionary<int, VertexIdTriple> VertexIds { get; set; }
            = new Dictionary<int, VertexIdTriple>();

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

        /// <summary>控えている識別子の件数。</summary>
        public int VertexIdCount => VertexIds?.Count ?? 0;

        /// <summary>引き当てに使える頂点IDを 1 件でも控えているか。</summary>
        public bool HasResolvableVertexIds
        {
            get
            {
                if (VertexIds == null) return false;
                foreach (var kv in VertexIds)
                    if (kv.Value.HasVertexId) return true;
                return false;
            }
        }

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
                if (VertexIdCount > 0) parts.Add($"ID:{VertexIdCount}");
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
                VertexIds = new Dictionary<int, VertexIdTriple>(VertexIds),
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

            // 同じ索引を両方が控えていたら相手側で上書きする。
            // 索引が同じなら指す頂点も同じなので、どちらを採っても同値のはず。
            if (other.VertexIds != null)
                foreach (var kv in other.VertexIds) VertexIds[kv.Key] = kv.Value;
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

            if (other.Vertices != null)
                foreach (int i in other.Vertices) VertexIds.Remove(i);
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

            PruneVertexIds();
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
            VertexIds.Clear();
        }

        /// <summary>Vertices に無い索引の控えを落とす。</summary>
        private void PruneVertexIds()
        {
            if (VertexIds == null || VertexIds.Count == 0) return;

            var drop = new List<int>();
            foreach (var kv in VertexIds)
                if (!Vertices.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (int k in drop) VertexIds.Remove(k);
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

            // 控えも同じ範囲で切る。索引が範囲外なら引き直しの起点にもならない。
            if (VertexIds != null && VertexIds.Count > 0)
            {
                var drop = new List<int>();
                foreach (var kv in VertexIds)
                    if (kv.Key < 0 || kv.Key >= vertexCount) drop.Add(kv.Key);
                foreach (int k in drop) VertexIds.Remove(k);
            }
        }

        // ================================================================
        // 識別子の控えと引き直し
        // ================================================================

        /// <summary>
        /// いま Vertices が指している頂点から識別子を読んで控え直す。
        /// 既存の控えは捨てる。控えた件数を返す。
        /// 未設定IDの頂点も控える（PartsId / SubId だけ持つことがあるため）。
        /// </summary>
        public int CaptureVertexIds(MeshObject meshObject)
        {
            VertexIds.Clear();
            if (meshObject == null) return 0;

            var list = meshObject.Vertices;
            foreach (int i in Vertices)
            {
                if (i < 0 || i >= list.Count) continue;
                var v = list[i];
                VertexIds[i] = new VertexIdTriple(v.Id, v.PartsId, v.SubId);
            }
            return VertexIds.Count;
        }

        /// <summary>
        /// 控えた頂点IDから Vertices を作り直す。
        ///
        /// 【重複IDは全件採る】
        ///   頂点IDが 1 オブジェクト内で重複するのは正常なので、先勝ちにはしない。
        ///   同じIDの頂点が複数あればその全部を選択に入れる。
        ///
        /// 【未設定IDは引き当てない】
        ///   0 と -1 を有効IDとして扱うと同値の頂点が大量に潰し合う
        ///   （MeshObject.IsUnsetId の注記）。
        ///
        /// 【引き当てられる控えが 1 件も無ければ何もしない】
        ///   選択を黙って空にしないための歯止め。resolved = 0 を返す。
        ///
        /// 【解決できなかった控えは残す】
        ///   索引が別の控えに取られていない限り辞書に残す。消すと、
        ///   もう一度ずれたときに戻せなくなる。
        /// </summary>
        /// <param name="meshObject">引き当て先。</param>
        /// <param name="resolved">解決できた控えの件数。</param>
        /// <param name="lost">頂点IDが見つからなかった控えの件数。</param>
        /// <returns>引き当てを実行したら true。控えが無ければ false。</returns>
        public bool ResolveByVertexId(MeshObject meshObject, out int resolved, out int lost)
        {
            resolved = 0;
            lost     = 0;

            if (meshObject == null || VertexIds == null || VertexIds.Count == 0) return false;
            if (!HasResolvableVertexIds) return false;

            // 有効IDのみで「ID → 索引の一覧」を作る。重複は正常なので潰さない。
            var byId = new Dictionary<int, List<int>>();
            var list = meshObject.Vertices;
            for (int i = 0; i < list.Count; i++)
            {
                int id = list[i].Id;
                if (MeshObject.IsUnsetId(id)) continue;

                if (!byId.TryGetValue(id, out var bucket))
                {
                    bucket = new List<int>();
                    byId[id] = bucket;
                }
                bucket.Add(i);
            }

            var newVertices = new HashSet<int>();
            var newIds      = new Dictionary<int, VertexIdTriple>();
            var unresolved  = new List<VertexIdTriple>();

            foreach (var kv in VertexIds)
            {
                var triple = kv.Value;

                if (!triple.HasVertexId || !byId.TryGetValue(triple.Id, out var hits))
                {
                    unresolved.Add(triple);
                    lost++;
                    continue;
                }

                foreach (int i in hits)
                {
                    newVertices.Add(i);
                    newIds[i] = triple;
                }
                resolved++;
            }

            // 解決できなかった控えは、索引が空いていれば元の位置に残す。
            foreach (var kv in VertexIds)
            {
                if (!unresolved.Contains(kv.Value)) continue;
                if (newIds.ContainsKey(kv.Key)) continue;
                newIds[kv.Key] = kv.Value;
            }

            Vertices  = newVertices;
            VertexIds = newIds;
            return true;
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

        // 識別子の控え。4 本の平行配列で持つ（int[] の入れ子を避けるため）。
        // 4 本は同じ長さにすること。欄が無い旧データは null になり、控え無しとして読む。
        public List<int> vertexIdIndices;   // 対応する頂点インデックス
        public List<int> vertexIds;         // Vertex.Id
        public List<int> vertexPartsIds;    // Vertex.PartsId
        public List<int> vertexSubIds;      // Vertex.SubId

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

            // 識別子の控え
            dto.vertexIdIndices = new List<int>();
            dto.vertexIds       = new List<int>();
            dto.vertexPartsIds  = new List<int>();
            dto.vertexSubIds    = new List<int>();
            if (set.VertexIds != null)
            {
                foreach (var kv in set.VertexIds)
                {
                    dto.vertexIdIndices.Add(kv.Key);
                    dto.vertexIds.Add(kv.Value.Id);
                    dto.vertexPartsIds.Add(kv.Value.PartsId);
                    dto.vertexSubIds.Add(kv.Value.SubId);
                }
            }

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

            // 識別子の控え。4 本の長さがそろっているぶんだけ読む。
            if (vertexIdIndices != null && vertexIds != null
                && vertexPartsIds != null && vertexSubIds != null)
            {
                int n = vertexIdIndices.Count;
                if (vertexIds.Count      < n) n = vertexIds.Count;
                if (vertexPartsIds.Count < n) n = vertexPartsIds.Count;
                if (vertexSubIds.Count   < n) n = vertexSubIds.Count;

                set.VertexIds = new Dictionary<int, VertexIdTriple>(n);
                for (int i = 0; i < n; i++)
                {
                    set.VertexIds[vertexIdIndices[i]] =
                        new VertexIdTriple(vertexIds[i], vertexPartsIds[i], vertexSubIds[i]);
                }
            }

            return set;
        }
    }
}
