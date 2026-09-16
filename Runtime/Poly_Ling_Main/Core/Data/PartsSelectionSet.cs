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

        /// <summary>
        /// 面索引 → Face.Id の控え。Faces の各要素に対応する。
        /// 頂点の VertexIds と同じ役割で、索引が詰められたあとの引き直しに使う。
        /// 後付けなので欠けを許す（控えていない索引は引き直しの対象外）。
        /// </summary>
        public Dictionary<int, int> FaceIds { get; set; } = new Dictionary<int, int>();

        /// <summary>線分（2 頂点の面）の索引 → Face.Id の控え。Lines に対応する。</summary>
        public Dictionary<int, int> LineIds { get; set; } = new Dictionary<int, int>();

        /// <summary>
        /// 辺（頂点索引の対）→ 両端の頂点 ID の対。
        /// 辺そのものには ID が無いので、両端の頂点 ID で引き直す。
        /// </summary>
        public Dictionary<VertexPair, VertexPair> EdgeVertexIds { get; set; }
            = new Dictionary<VertexPair, VertexPair>();

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
                FaceIds   = new Dictionary<int, int>(FaceIds),
                LineIds   = new Dictionary<int, int>(LineIds),
                EdgeVertexIds = new Dictionary<VertexPair, VertexPair>(EdgeVertexIds),
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
            FaceIds.Clear();
            LineIds.Clear();
            EdgeVertexIds.Clear();
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

        // ================================================================
        // 面・線分・辺の控えと引き直し
        // ================================================================

        /// <summary>
        /// いま Faces / Lines が指している面から Face.Id を読んで控え直す。
        /// 既存の控えは捨てる。控えた件数の合計を返す。
        /// </summary>
        public int CaptureFaceIds(MeshObject meshObject)
        {
            FaceIds.Clear();
            LineIds.Clear();
            if (meshObject == null) return 0;

            var faces = meshObject.Faces;
            foreach (int i in Faces)
            {
                if (i < 0 || i >= faces.Count) continue;
                int id = faces[i].Id;
                if (MeshObject.IsUnsetId(id)) continue;   // 引き当てに使えない
                FaceIds[i] = id;
            }
            foreach (int i in Lines)
            {
                if (i < 0 || i >= faces.Count) continue;
                int id = faces[i].Id;
                if (MeshObject.IsUnsetId(id)) continue;
                LineIds[i] = id;
            }
            return FaceIds.Count + LineIds.Count;
        }

        /// <summary>
        /// いま Edges が指している辺の両端から頂点 ID を読んで控え直す。
        /// 既存の控えは捨てる。控えた件数を返す。
        /// 片端でも未設定 ID なら控えない（引き当てに使えないため）。
        /// </summary>
        public int CaptureEdgeVertexIds(MeshObject meshObject)
        {
            EdgeVertexIds.Clear();
            if (meshObject == null) return 0;

            var list = meshObject.Vertices;
            foreach (var e in Edges)
            {
                if (e.V1 < 0 || e.V1 >= list.Count) continue;
                if (e.V2 < 0 || e.V2 >= list.Count) continue;

                int id1 = list[e.V1].Id;
                int id2 = list[e.V2].Id;
                if (MeshObject.IsUnsetId(id1) || MeshObject.IsUnsetId(id2)) continue;

                EdgeVertexIds[e] = new VertexPair(id1, id2);
            }
            return EdgeVertexIds.Count;
        }

        /// <summary>頂点・面・線分・辺の控えをまとめて取り直す。控えた件数の合計を返す。</summary>
        public int CaptureIds(MeshObject meshObject)
            => CaptureVertexIds(meshObject)
             + CaptureFaceIds(meshObject)
             + CaptureEdgeVertexIds(meshObject);

        /// <summary>
        /// 控えた ID から頂点・面・線分・辺をまとめて引き直す。
        ///
        /// 頂点は ResolveByVertexId と同じ規則。面・線分は Face.Id、
        /// 辺は両端の頂点 ID で引く。引き当てられなかったものは落とす
        /// （面と辺は「索引が空いていれば残す」ができない。面 ID は
        /// 1 メッシュ内で一意なので、見つからない＝その面はもう無い）。
        /// </summary>
        /// <param name="resolved">引き当てられた控えの件数（頂点・面・線分・辺の合計）。</param>
        /// <param name="lost">引き当てられなかった控えの件数。</param>
        /// <returns>引き当てを実行したら true。控えが 1 件も無ければ false。</returns>
        public bool ResolveByIds(MeshObject meshObject, out int resolved, out int lost)
        {
            resolved = 0;
            lost     = 0;
            if (meshObject == null) return false;

            bool any = false;

            if (ResolveByVertexId(meshObject, out int vResolved, out int vLost))
            {
                any       = true;
                resolved += vResolved;
                lost     += vLost;
            }

            // ── 面・線分 ─────────────────────────────────────────────
            if (FaceIds.Count > 0 || LineIds.Count > 0)
            {
                any = true;

                var byFaceId = new Dictionary<int, int>();
                var faces    = meshObject.Faces;
                for (int i = 0; i < faces.Count; i++)
                {
                    int id = faces[i].Id;
                    if (MeshObject.IsUnsetId(id)) continue;
                    if (!byFaceId.ContainsKey(id)) byFaceId[id] = i;
                }

                ResolveFaceSet(Faces, FaceIds, byFaceId, ref resolved, ref lost);
                ResolveFaceSet(Lines, LineIds, byFaceId, ref resolved, ref lost);
            }

            // ── 辺 ───────────────────────────────────────────────────
            if (EdgeVertexIds.Count > 0)
            {
                any = true;

                var byVertexId = new Dictionary<int, int>();
                var list       = meshObject.Vertices;
                for (int i = 0; i < list.Count; i++)
                {
                    int id = list[i].Id;
                    if (MeshObject.IsUnsetId(id)) continue;
                    if (!byVertexId.ContainsKey(id)) byVertexId[id] = i;
                }

                var newEdges = new HashSet<VertexPair>();
                var newIds   = new Dictionary<VertexPair, VertexPair>();
                foreach (var kv in EdgeVertexIds)
                {
                    if (!byVertexId.TryGetValue(kv.Value.V1, out int a) ||
                        !byVertexId.TryGetValue(kv.Value.V2, out int b) ||
                        a == b)
                    {
                        lost++;
                        continue;
                    }
                    var edge = new VertexPair(a, b);
                    newEdges.Add(edge);
                    newIds[edge] = kv.Value;
                    resolved++;
                }
                Edges         = newEdges;
                EdgeVertexIds = newIds;
            }

            return any;
        }

        /// <summary>面（または線分）の集合を Face.Id で引き直す。</summary>
        private static void ResolveFaceSet(
            HashSet<int> target, Dictionary<int, int> ids,
            Dictionary<int, int> byFaceId, ref int resolved, ref int lost)
        {
            if (ids == null || ids.Count == 0) return;

            var newSet = new HashSet<int>();
            var newIds = new Dictionary<int, int>();
            foreach (var kv in ids)
            {
                if (!byFaceId.TryGetValue(kv.Value, out int fi)) { lost++; continue; }
                newSet.Add(fi);
                newIds[fi] = kv.Value;
                resolved++;
            }
            target.Clear();
            target.UnionWith(newSet);

            ids.Clear();
            foreach (var kv in newIds) ids[kv.Key] = kv.Value;
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

        // 面・線分・辺の控え。頂点と同じく平行配列で持つ。
        // 欄が無い旧データは null になり、控え無しとして読む。
        public List<int> faceIdIndices;     // 対応する面インデックス（Faces 用）
        public List<int> faceIdValues;      // Face.Id
        public List<int> lineIdIndices;     // 対応する面インデックス（Lines 用）
        public List<int> lineIdValues;      // Face.Id
        public List<int> edgeV1;            // 辺の頂点インデックス 1
        public List<int> edgeV2;            // 辺の頂点インデックス 2
        public List<int> edgeVertexId1;     // 辺の端 1 の Vertex.Id
        public List<int> edgeVertexId2;     // 辺の端 2 の Vertex.Id

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

            // 面・線分・辺の控え
            FillIdBackups(dto, set);

            return dto;
        }

        /// <summary>面・線分・辺の控えを DTO へ写す。</summary>
        private static void FillIdBackups(SelectionSetDTO dto, PartsSelectionSet set)
        {
            dto.faceIdIndices = new List<int>();
            dto.faceIdValues  = new List<int>();
            if (set.FaceIds != null)
                foreach (var kv in set.FaceIds)
                {
                    dto.faceIdIndices.Add(kv.Key);
                    dto.faceIdValues.Add(kv.Value);
                }

            dto.lineIdIndices = new List<int>();
            dto.lineIdValues  = new List<int>();
            if (set.LineIds != null)
                foreach (var kv in set.LineIds)
                {
                    dto.lineIdIndices.Add(kv.Key);
                    dto.lineIdValues.Add(kv.Value);
                }

            dto.edgeV1        = new List<int>();
            dto.edgeV2        = new List<int>();
            dto.edgeVertexId1 = new List<int>();
            dto.edgeVertexId2 = new List<int>();
            if (set.EdgeVertexIds != null)
                foreach (var kv in set.EdgeVertexIds)
                {
                    dto.edgeV1.Add(kv.Key.V1);
                    dto.edgeV2.Add(kv.Key.V2);
                    dto.edgeVertexId1.Add(kv.Value.V1);
                    dto.edgeVertexId2.Add(kv.Value.V2);
                }
        }

        /// <summary>面・線分・辺の控えを DTO から読み戻す。</summary>
        private void RestoreIdBackups(PartsSelectionSet set)
        {
            if (faceIdIndices != null && faceIdValues != null)
            {
                int n = System.Math.Min(faceIdIndices.Count, faceIdValues.Count);
                set.FaceIds = new Dictionary<int, int>(n);
                for (int i = 0; i < n; i++) set.FaceIds[faceIdIndices[i]] = faceIdValues[i];
            }

            if (lineIdIndices != null && lineIdValues != null)
            {
                int n = System.Math.Min(lineIdIndices.Count, lineIdValues.Count);
                set.LineIds = new Dictionary<int, int>(n);
                for (int i = 0; i < n; i++) set.LineIds[lineIdIndices[i]] = lineIdValues[i];
            }

            if (edgeV1 != null && edgeV2 != null && edgeVertexId1 != null && edgeVertexId2 != null)
            {
                int n = edgeV1.Count;
                if (edgeV2.Count        < n) n = edgeV2.Count;
                if (edgeVertexId1.Count < n) n = edgeVertexId1.Count;
                if (edgeVertexId2.Count < n) n = edgeVertexId2.Count;

                set.EdgeVertexIds = new Dictionary<VertexPair, VertexPair>(n);
                for (int i = 0; i < n; i++)
                    set.EdgeVertexIds[new VertexPair(edgeV1[i], edgeV2[i])] =
                        new VertexPair(edgeVertexId1[i], edgeVertexId2[i]);
            }
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

            // 面・線分・辺の控え
            RestoreIdBackups(set);

            return set;
        }
    }
}
