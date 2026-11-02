// EdgeBevelOps.cs
// 辺のベベルの実処理。EdgeBevelTool（マウス・コマンド）と処理列の種類 bevel（BevelMeshModifier）が共用する。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 段 7a（EdgeBevelTool.ExecuteBevel から切り出した）・段 9b（頂点での端点のまとめ方とふた）
//
// 【渡されたメッシュをその場で書き換える】
//   ツールは編集対象の生きたメッシュを、種類は入力の Clone を渡す。
//   孤立頂点の除去は MeshObject.RemoveVertices を通す。選択・パーツ選択辞書の付け替えは
//   その VerticesRemoved イベントを MeshContext が購読して行う（MeshObject.Removal.cs）。
//   Clone はイベントを写さないので、種類が渡した写しでは付け替えは起きない。
//   部品 ID の採番（PartsIdOps.AssignNewVertices）もメッシュの中だけで済むので、ここで行う。
//
// 【ここでしないこと】Undo・選択の辺の消去・GPU への通知・ログ。呼び出し側が受け持つ。
//
// 【ID】新しい頂点・面には ID を振らない（ツールの動作のまま）。
//   写し先（SourceOf）を返すので、ID が要る呼び出し側（処理列の種類）はそれで元の頂点の ID を写す。
//
// 【2 本以上のベベル辺が集まる頂点（段 9b）】
//   頂点の周りの面を「ベベルしない辺で隣り合う面どうし」でつなぎ、領域に分ける（ベベルする辺が仕切り）。
//   1 つの領域に別々の辺の端点が入っていれば、それらを 1 頂点にまとめる。位置はマイター
//   （量 × (d1+d2)/(1+d1·d2)。d は各辺の内向きの向き）で、両方の辺から量だけ離れた点になる。
//   2 辺の両側がそれぞれ同じ領域にあれば（直線の続き・箱の角の 2 辺）中間の段の頂点もまとめ、帯をつなぐ。
//   3 本以上が集まる頂点は、端点をまとめたあと残る穴に、ふたの面を 1 枚張る（Blender の Cutoff と同じ形）。
//   1 本の辺の端は、A 側の面から B 側の面までベベルしない辺でつながる側面の並びに、段の列を分けて差し込む
//   （側面が 1 枚なら列全体。今までの [C] と同じ。2 枚以上なら境の辺ごとに段の頂点を共有させ、割れないようにする）。

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    public static class EdgeBevelOps
    {
        /// <summary>ドラッグ中に位置を更新する頂点（処理後の索引・基準位置・向き）。</summary>
        public struct DragVertex
        {
            public int     Index;
            public Vector3 BasePos;
            public Vector3 OffsetDir;
        }

        public sealed class Result
        {
            /// <summary>辺から集めた対象の数（隣接面の有無を問わない）。0 ならメッシュに触れていない。</summary>
            public int CollectedEdgeCount;

            /// <summary>隣接面が 2 枚そろってベベルした辺の数。</summary>
            public int BeveledEdgeCount;

            /// <summary>3 本以上のベベル辺が集まる頂点に張ったふたの面の数。</summary>
            public int CapFaceCount;

            /// <summary>ドラッグ用の頂点（重複なし）。</summary>
            public readonly List<DragVertex> DragVertices = new List<DragVertex>();

            /// <summary>
            /// 処理後の頂点ごとの、処理前の頂点番号。新しい頂点は複製元（辺の端）を指す。
            /// CollectedEdgeCount が 0 のときは null。
            /// </summary>
            public int[] SourceOf;

            /// <summary>処理後の頂点ごとに、新しく作った頂点か。CollectedEdgeCount が 0 のときは null。</summary>
            public bool[] Created;

            /// <summary>処理前の頂点数。</summary>
            public int OriginalVertexCount;
        }

        private struct EdgeInfo
        {
            public int V0, V1;
            public int FaceA, FaceB;
            /// <summary>FaceB 内での辺端点。共有頂点では V0/V1 と同じ、非共有頂点では異なる。</summary>
            public int FaceB_V0, FaceB_V1;
        }

        private struct EdgeData
        {
            public int V0, V1;
            public int FaceB_V0, FaceB_V1;
            public int FaceAIdx, FaceBIdx;
            public HashSet<int> FaceAOrigVerts;
            public HashSet<int> FaceBOrigVerts;
            public int[] RowV0;
            public int[] RowV1;
            public Vector3 OffsetA;
            public Vector3 OffsetB;
            public bool FaceAReverse;
            public Vector3 P0;
            public Vector3 P1;
        }

        /// <summary>頂点に集まるベベル辺の端（どの辺の、どちらの端か）。</summary>
        private struct EdgeEnd
        {
            public int  EdgeIdx;
            public bool IsV0;
        }

        /// <summary>3 本以上が集まる頂点のふたを張るための控え。</summary>
        private sealed class CapInfo
        {
            public List<EdgeEnd> Ends;
            public Vector3 Outward;
        }

        /// <summary>
        /// 辺をベベルする。mesh はその場で書き換える。
        /// edges の並びで処理する（V1 を V0 側、V2 を V1 側として扱う）。
        /// </summary>
        /// <param name="amount">ベベル量（メッシュのローカル空間の長さ）。</param>
        /// <param name="segments">分割数。</param>
        /// <param name="fillet">分割数 2 以上で弧にする。</param>
        /// <param name="materialIndex">足す面の材質番号。</param>
        public static Result Execute(MeshObject mesh, IEnumerable<VertexPair> edges,
            float amount, int segments, bool fillet, int materialIndex)
        {
            var result = new Result();
            if (mesh == null || edges == null) return result;

            var targets = CollectTargetEdges(mesh, edges);
            result.CollectedEdgeCount = targets.Count;
            if (targets.Count == 0) return result;

            // ベベルで増える頂点の始まり。部品ID / サブIDの採番に使う。
            // 採番は Pass 1 の直後（孤立頂点の除去より前）に行う。除去でインデックスが
            // ずれても、採番済みの値は頂点そのものが持つため影響を受けない。
            int origVertexCount = mesh.VertexCount;
            result.OriginalVertexCount = origVertexCount;

            // 頂点ごとの処理前の番号（新しい頂点は複製元）
            var rawSource = new List<int>(origVertexCount);
            for (int i = 0; i < origVertexCount; i++) rawSource.Add(i);

            var orphanCandidates = new HashSet<int>();
            bool useFillet = fillet && segments >= 2;

            // ============================================================
            // Pass 1: 全エッジの row 頂点を生成する（フェースは変更しない）
            //
            // row の構造:
            //   row[0]        : FaceA 側端点  (FaceA の v を置換する頂点)
            //   row[1..n-1]   : 中間頂点群    (Segments >= 2 のときに存在)
            //   row[segments] : FaceB 側端点  (FaceB の v を置換する頂点)
            //
            // フラット (Fillet=false): offset = Lerp(offsetA*amount, offsetB*amount, t)
            // フィレット (Fillet=true, segments>=2): offset = Slerp(offsetA, offsetB, t) * amount
            // ============================================================
            var bevelDataList = new List<EdgeData>();

            foreach (var edgeInfo in targets)
            {
                if (edgeInfo.FaceA < 0 || edgeInfo.FaceB < 0) continue;

                var faceA = mesh.Faces[edgeInfo.FaceA];
                var faceB = mesh.Faces[edgeInfo.FaceB];

                Vector3 p0 = mesh.Vertices[edgeInfo.V0].Position;
                Vector3 p1 = mesh.Vertices[edgeInfo.V1].Position;

                // FaceA/B それぞれのフェース内側へのオフセット方向
                Vector3 offsetA = GetInwardOffset(mesh, faceA, edgeInfo.V0, edgeInfo.V1);
                Vector3 offsetB = GetInwardOffset(mesh, faceB, edgeInfo.V0, edgeInfo.V1);

                // 巻き順は幾何法線ではなく隣接する元面 faceA の辺並び順から継承する（押し出しと同型）。
                bool faceAReverse = false;
                {
                    int i0 = faceA.VertexIndices.IndexOf(edgeInfo.V0);
                    int i1 = faceA.VertexIndices.IndexOf(edgeInfo.V1);
                    if (i0 >= 0 && i1 >= 0)
                        faceAReverse = (i1 == (i0 + 1) % faceA.VertexCount);
                }

                var rowV0 = new int[segments + 1];
                var rowV1 = new int[segments + 1];

                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / segments; // 0.0 (FaceA側) → 1.0 (FaceB側)

                    Vector3 offset = useFillet
                        ? Vector3.Slerp(offsetA, offsetB, t) * amount   // 弧補間
                        : Vector3.Lerp(offsetA * amount, offsetB * amount, t); // 線形補間

                    rowV0[s] = mesh.VertexCount;
                    {
                        var nv = new Vertex { Position = p0 + offset };
                        var sv0 = mesh.Vertices[edgeInfo.V0];
                        if (sv0.UVs.Count > 0) nv.UVs.Add(sv0.UVs[0]);
                        // 複製元の BoneWeight をコピーする。設定しないと GPU 側で
                        // メッシュ自身の context 索引が使われ（UnifiedBufferManager_Build.cs:356-362）、
                        // 周囲の頂点と別の行列で変換されてこの頂点だけ離れた位置に置かれる。
                        nv.BoneWeight = sv0.BoneWeight;
                        mesh.Vertices.Add(nv);
                        rawSource.Add(edgeInfo.V0);
                    }
                    rowV1[s] = mesh.VertexCount;
                    {
                        var nv = new Vertex { Position = p1 + offset };
                        var sv1 = mesh.Vertices[edgeInfo.V1];
                        if (sv1.UVs.Count > 0) nv.UVs.Add(sv1.UVs[0]);
                        nv.BoneWeight = sv1.BoneWeight;
                        mesh.Vertices.Add(nv);
                        rawSource.Add(edgeInfo.V1);
                    }
                }

                orphanCandidates.Add(edgeInfo.V0);
                orphanCandidates.Add(edgeInfo.V1);
                // 非共有頂点の場合 FaceB 側の対応頂点も orphan 候補に追加
                if (edgeInfo.FaceB_V0 != edgeInfo.V0) orphanCandidates.Add(edgeInfo.FaceB_V0);
                if (edgeInfo.FaceB_V1 != edgeInfo.V1) orphanCandidates.Add(edgeInfo.FaceB_V1);

                bevelDataList.Add(new EdgeData
                {
                    V0 = edgeInfo.V0,
                    V1 = edgeInfo.V1,
                    FaceB_V0 = edgeInfo.FaceB_V0,
                    FaceB_V1 = edgeInfo.FaceB_V1,
                    FaceAIdx = edgeInfo.FaceA,
                    FaceBIdx = edgeInfo.FaceB,
                    // フェース頂点集合は Pass1 の時点でスナップショット。
                    FaceAOrigVerts = new HashSet<int>(faceA.VertexIndices),
                    FaceBOrigVerts = new HashSet<int>(faceB.VertexIndices),
                    RowV0 = rowV0,
                    RowV1 = rowV1,
                    OffsetA = offsetA,
                    OffsetB = offsetB,
                    FaceAReverse = faceAReverse,
                    P0 = p0,
                    P1 = p1,
                });
            }
            result.BeveledEdgeCount = bevelDataList.Count;

            // Pass 1 で増えた頂点を 1 つの部品として扱う。
            PartsIdOps.AssignNewVertices(mesh, origVertexCount);

            // ベベルフェース追加前のフェース数（Pass3 で追加するフェースは走査しない）
            int faceCountBeforeBevel = mesh.FaceCount;

            // ============================================================
            // Pass 1.5: 2 本以上のベベル辺が集まる頂点で端点をまとめる（段 9b）
            //
            //   頂点 v の周りの面を、ベベルしない辺 (v,w) で隣り合う面どうしでつなぎ、領域に分ける。
            //   1 つの領域に別々の辺の端点が入っていれば 1 頂点にまとめ、その領域の面の v を
            //   その頂点で置き換える（faceVertexOverride。Pass 2 が使う）。
            //   2 辺の両側がそれぞれ同じ領域にあれば中間の段もまとめる。
            //   3 本以上なら、まとめた後に残る穴へふたを張る（Pass 3.5）。
            // ============================================================
            var vertexToEdges = new Dictionary<int, List<EdgeEnd>>();
            for (int ei = 0; ei < bevelDataList.Count; ei++)
            {
                var bd = bevelDataList[ei];
                AddEnd(vertexToEdges, bd.V0, ei, true);
                AddEnd(vertexToEdges, bd.V1, ei, false);
            }

            // (面, 元の頂点) → その面で元の頂点を置き換える列（Pass 2 が最優先で使う）
            var faceVertexOverride = new Dictionary<(int face, int vertex), int[]>();
            var mergedDrag = new Dictionary<int, (Vector3 basePos, Vector3 dir)>();
            var caps = new List<CapInfo>();

            foreach (var kv in vertexToEdges)
            {
                int v = kv.Key;
                var ends = kv.Value;

                Vector3 pv = mesh.Vertices[v].Position;

                // v を含む面（ベベル前の面だけ）
                var fan = new List<int>();
                for (int fi = 0; fi < faceCountBeforeBevel; fi++)
                    if (mesh.Faces[fi].VertexIndices.Contains(v)) fan.Add(fi);
                if (fan.Count == 0) continue;

                // 1 本の辺の端：A 側の面から B 側の面まで、ベベルしない辺でつながる側面の並びに段の列を配る
                if (ends.Count == 1)
                {
                    DistributeEndRow(mesh, v, fan, bevelDataList[ends[0].EdgeIdx], ends[0].IsV0, segments, faceVertexOverride);
                    continue;
                }

                // ベベルする辺の、v でない側の頂点
                var bevelOthers = new HashSet<int>();
                foreach (var en in ends)
                {
                    var bd = bevelDataList[en.EdgeIdx];
                    bevelOthers.Add(en.IsV0 ? bd.V1 : bd.V0);
                }

                // 領域分け（ベベルしない辺 (v,w) を共有する面どうしを 1 つにする）
                var parent = new Dictionary<int, int>();
                foreach (int f in fan) parent[f] = f;
                var facesByNeighbor = new Dictionary<int, List<int>>();
                foreach (int f in fan)
                {
                    var vi = mesh.Faces[f].VertexIndices;
                    int n = vi.Count;
                    int pos = vi.IndexOf(v);
                    if (n < 3 || pos < 0) continue;
                    int prev = vi[(pos - 1 + n) % n];
                    int next = vi[(pos + 1) % n];
                    AddFaceByNeighbor(facesByNeighbor, prev, f);
                    if (next != prev) AddFaceByNeighbor(facesByNeighbor, next, f);
                }
                foreach (var nb in facesByNeighbor)
                {
                    if (bevelOthers.Contains(nb.Key)) continue;
                    for (int i = 1; i < nb.Value.Count; i++) Union(parent, nb.Value[0], nb.Value[i]);
                }

                // 各端の両側の領域（-1 は v を含まない＝非共有頂点の面）
                var regionA = new int[ends.Count];
                var regionB = new int[ends.Count];
                for (int k = 0; k < ends.Count; k++)
                {
                    var bd = bevelDataList[ends[k].EdgeIdx];
                    regionA[k] = parent.ContainsKey(bd.FaceAIdx) ? Find(parent, bd.FaceAIdx) : -1;
                    int bv = ends[k].IsV0 ? bd.FaceB_V0 : bd.FaceB_V1;
                    regionB[k] = (bv == v && parent.ContainsKey(bd.FaceBIdx)) ? Find(parent, bd.FaceBIdx) : -1;
                }

                // 領域ごとの端点（どの端の、A 側か B 側か）
                var byRegion = new Dictionary<int, List<(int k, bool sideA)>>();
                for (int k = 0; k < ends.Count; k++)
                {
                    if (regionA[k] >= 0) AddRegionEnd(byRegion, regionA[k], k, true);
                    if (regionB[k] >= 0) AddRegionEnd(byRegion, regionB[k], k, false);
                }

                bool anyMerged = false;
                foreach (var rg in byRegion)
                {
                    var list = rg.Value;
                    var distinct = new HashSet<int>();
                    foreach (var e in list) distinct.Add(ends[e.k].EdgeIdx);
                    if (distinct.Count < 2) continue;

                    // まとめる頂点と、その位置（マイター）
                    var dirs = new List<Vector3>();
                    int keep = -1;
                    foreach (var e in list)
                    {
                        var bd = bevelDataList[ends[e.k].EdgeIdx];
                        int[] row = ends[e.k].IsV0 ? bd.RowV0 : bd.RowV1;
                        int idx = e.sideA ? 0 : segments;
                        dirs.Add(e.sideA ? bd.OffsetA : bd.OffsetB);
                        if (keep < 0) { keep = row[idx]; continue; }
                        if (row[idx] != keep)
                        {
                            orphanCandidates.Add(row[idx]);
                            row[idx] = keep;
                        }
                    }
                    Vector3 m = Miter(dirs);
                    mesh.Vertices[keep].Position = pv + m * amount;
                    mergedDrag[keep] = (pv, m);

                    foreach (int f in fan)
                        if (Find(parent, f) == rg.Key) faceVertexOverride[(f, v)] = new[] { keep };
                    anyMerged = true;
                }

                // 2 本だけのとき：両側がそれぞれ同じ領域なら中間の段もまとめる
                if (ends.Count == 2 && segments >= 2
                    && regionA[0] >= 0 && regionB[0] >= 0 && regionA[0] != regionB[0]
                    && regionA[1] >= 0 && regionB[1] >= 0)
                {
                    bool same    = regionA[0] == regionA[1] && regionB[0] == regionB[1];
                    bool flipped = regionA[0] == regionB[1] && regionB[0] == regionA[1];
                    if (same || flipped)
                    {
                        var bd0 = bevelDataList[ends[0].EdgeIdx];
                        var bd1 = bevelDataList[ends[1].EdgeIdx];
                        int[] row0 = ends[0].IsV0 ? bd0.RowV0 : bd0.RowV1;
                        int[] row1 = ends[1].IsV0 ? bd1.RowV0 : bd1.RowV1;
                        for (int s = 1; s < segments; s++)
                        {
                            int s1 = same ? s : segments - s;
                            float t0 = (float)s / segments;
                            float t1 = (float)s1 / segments;
                            Vector3 d0 = useFillet ? Vector3.Slerp(bd0.OffsetA, bd0.OffsetB, t0) : Vector3.Lerp(bd0.OffsetA, bd0.OffsetB, t0);
                            Vector3 d1 = useFillet ? Vector3.Slerp(bd1.OffsetA, bd1.OffsetB, t1) : Vector3.Lerp(bd1.OffsetA, bd1.OffsetB, t1);
                            int keep = row0[s];
                            if (row1[s1] != keep)
                            {
                                orphanCandidates.Add(row1[s1]);
                                row1[s1] = keep;
                            }
                            Vector3 m = Miter(new List<Vector3> { d0, d1 });
                            mesh.Vertices[keep].Position = pv + m * amount;
                            mergedDrag[keep] = (pv, m);
                        }
                    }
                }

                // 3 本以上：まとめた後に残る穴へふたを張る
                if (anyMerged && ends.Count >= 3)
                {
                    Vector3 outward = Vector3.zero;
                    foreach (int f in fan) outward += CalculateFaceNormal(mesh, mesh.Faces[f]);
                    caps.Add(new CapInfo { Ends = new List<EdgeEnd>(ends), Outward = outward });
                }
            }

            // ドラッグ更新用に (rawIndex, basePos, offsetDir) を収集する。
            // まとめた頂点はマイターの向き（BasePos + 向き × 量 でまとめた位置になる）。
            // orphanCandidates 除去によるインデックスシフトは後で調整する。
            var rawDragVerts = new List<(int rawIdx, Vector3 basePos, Vector3 offsetDir)>();
            foreach (var bd in bevelDataList)
            {
                for (int s = 0; s <= segments; s++)
                {
                    float t = segments > 0 ? (float)s / segments : 0f;
                    Vector3 dir = useFillet
                        ? Vector3.Slerp(bd.OffsetA, bd.OffsetB, t)
                        : Vector3.Lerp(bd.OffsetA, bd.OffsetB, t);
                    AddDragVertex(rawDragVerts, mergedDrag, bd.RowV0[s], bd.P0, dir);
                    AddDragVertex(rawDragVerts, mergedDrag, bd.RowV1[s], bd.P1, dir);
                }
            }

            // ============================================================
            // Pass 2: ベベル前の全フェースを走査し、頂点置換を一括適用する
            //
            //   [O] まとめた頂点のある領域の面（faceVertexOverride）→ その 1 頂点で単一置換（最優先）
            //   [A] faceA に該当  → v を row[0] に単一置換
            //   [B] faceB に該当  → v を row[segments] に単一置換
            //   [C] 側面フェース  → v を row 全体のシーケンスで展開（N角形 → N+segments 角形）
            //       同じ pos に [A][B] が既に登録されている場合は [A][B] を優先する。
            //
            // 置換は後ろの pos から（降順）適用し、前方の pos をずらさない。
            // ============================================================
            for (int fi = 0; fi < faceCountBeforeBevel; fi++)
            {
                var face = mesh.Faces[fi];

                var replacements = new SortedDictionary<int, int[]>(
                    Comparer<int>.Create((a, b) => b - a)); // 降順ソート
                var fixedPos = new HashSet<int>();

                for (int pos = 0; pos < face.VertexIndices.Count; pos++)
                {
                    if (faceVertexOverride.TryGetValue((fi, face.VertexIndices[pos]), out int[] ov))
                    {
                        replacements[pos] = ov;
                        fixedPos.Add(pos);
                    }
                }

                foreach (var bd in bevelDataList)
                {
                    for (int vi = 0; vi < 2; vi++)
                    {
                        // FaceB の場合は FaceB_V0/FaceB_V1 を使う（非共有頂点対応）
                        int v;
                        if (fi == bd.FaceBIdx)
                            v = (vi == 0) ? bd.FaceB_V0 : bd.FaceB_V1;
                        else
                            v = (vi == 0) ? bd.V0 : bd.V1;

                        int[] row = (vi == 0) ? bd.RowV0 : bd.RowV1;

                        int pos = face.VertexIndices.IndexOf(v);
                        if (pos < 0) continue;
                        if (fixedPos.Contains(pos)) continue;

                        if (fi == bd.FaceAIdx)
                        {
                            replacements[pos] = new[] { row[0] };
                        }
                        else if (fi == bd.FaceBIdx)
                        {
                            replacements[pos] = new[] { row[segments] };
                        }
                        else
                        {
                            if (replacements.ContainsKey(pos)) continue;

                            int count = face.VertexIndices.Count;
                            int prev = face.VertexIndices[(pos - 1 + count) % count];

                            if (bd.FaceAOrigVerts.Contains(prev))
                                replacements[pos] = row;
                            else if (bd.FaceBOrigVerts.Contains(prev))
                                replacements[pos] = Reversed(row);
                            else
                                replacements[pos] = new[] { row[row.Length / 2] };
                        }
                    }
                }

                foreach (var kvr in replacements)
                {
                    int pos = kvr.Key;
                    int[] seq = kvr.Value;

                    face.VertexIndices.RemoveAt(pos);
                    face.VertexIndices.InsertRange(pos, seq);

                    if (pos < face.UVIndices.Count)
                    {
                        face.UVIndices.RemoveAt(pos);
                        face.UVIndices.InsertRange(pos, seq);
                    }
                    if (pos < face.NormalIndices.Count)
                    {
                        face.NormalIndices.RemoveAt(pos);
                        face.NormalIndices.InsertRange(pos, seq);
                    }
                }
            }

            // ============================================================
            // Pass 3: 各エッジの row 間を埋める四角フェースを追加する
            //   巻き順は隣接する元面 faceA の辺 (V0,V1) の並び順から継承する（押し出しツールと同型）。
            // ============================================================
            foreach (var bd in bevelDataList)
            {
                for (int s = 0; s < segments; s++)
                {
                    int[] verts = bd.FaceAReverse
                        ? new[] { bd.RowV0[s], bd.RowV0[s + 1], bd.RowV1[s + 1], bd.RowV1[s] }
                        : new[] { bd.RowV0[s], bd.RowV1[s], bd.RowV1[s + 1], bd.RowV0[s + 1] };

                    var bevelFace = new Face { MaterialIndex = materialIndex };
                    bevelFace.VertexIndices.AddRange(verts);
                    bevelFace.UVIndices.AddRange(new[] { 0, 0, 0, 0 });
                    bevelFace.NormalIndices.AddRange(new[] { 0, 0, 0, 0 });
                    mesh.Faces.Add(bevelFace);
                }
            }

            // ============================================================
            // Pass 3.5: 3 本以上が集まる頂点のふた（段 9b）
            //   各辺の段の列を、まとめた端点でつないで 1 周の多角形にする。
            //   向きは元の周りの面の法線の和に合わせる。つながらなければ張らない。
            // ============================================================
            foreach (var cap in caps)
            {
                var loop = BuildCapLoop(cap.Ends, bevelDataList, segments);
                if (loop == null || loop.Count < 3) continue;

                Vector3 n = PolygonNormal(mesh, loop);
                if (Vector3.Dot(n, cap.Outward) < 0f) loop.Reverse();

                var capFace = new Face { MaterialIndex = materialIndex };
                capFace.VertexIndices.AddRange(loop);
                for (int i = 0; i < loop.Count; i++) { capFace.UVIndices.Add(0); capFace.NormalIndices.Add(0); }
                mesh.Faces.Add(capFace);
                result.CapFaceCount++;
            }

            var removedAsc = RemoveOrphanVertices(mesh, orphanCandidates, out int[] map);

            // 写し先：処理後の索引 → 処理前の番号
            var sourceOf = new int[mesh.VertexCount];
            var created  = new bool[mesh.VertexCount];
            for (int raw = 0; raw < rawSource.Count; raw++)
            {
                int to = map == null ? raw : map[raw];
                if (to < 0 || to >= sourceOf.Length) continue;
                sourceOf[to] = rawSource[raw];
                created[to]  = raw >= origVertexCount;
            }
            result.SourceOf = sourceOf;
            result.Created  = created;

            // ドラッグ用の頂点を確定: rawIndex をシフト補正し、重複除外
            var seenIdx = new HashSet<int>();
            foreach (var (rawIdx, basePos, offsetDir) in rawDragVerts)
            {
                int bsResult = removedAsc.BinarySearch(rawIdx);
                if (bsResult >= 0) continue; // このrowがorphan除去された場合はスキップ
                int shift    = ~bsResult;    // rawIdx より小さい除去済みインデックスの数
                int adjusted = rawIdx - shift;
                if (seenIdx.Add(adjusted))
                    result.DragVertices.Add(new DragVertex { Index = adjusted, BasePos = basePos, OffsetDir = offsetDir });
            }

            return result;
        }

        // ================================================================
        // Pass 1.5 / 3.5 の補助（段 9b）
        // ================================================================

        private static void AddEnd(Dictionary<int, List<EdgeEnd>> map, int v, int edgeIdx, bool isV0)
        {
            if (!map.TryGetValue(v, out var list)) { list = new List<EdgeEnd>(); map[v] = list; }
            list.Add(new EdgeEnd { EdgeIdx = edgeIdx, IsV0 = isV0 });
        }

        /// <summary>
        /// 1 本の辺の端 v（段 9b）。v の周りの面を、A 側の面から B 側の面まで、ベベルしない辺 (v,w) で
        /// つながる並び A, C1..Cm, B でたどる。境の辺 e0..em（A|C1, …, Cm|B）に段の番号
        /// round(t·n/m) を割り当て、Cj の v を e(j−1) から ej までの段の列で置き換える。
        /// m=1（v の周りが 3 面）なら C1 に列全体が入り、今までの [C] と同じ。m≥2 で側面に分けて入れ、
        /// 隣り合う側面が境の辺の段の頂点を共有するので割れない。並びが見つからなければ（開いた縁など）何もしない。
        /// </summary>
        private static void DistributeEndRow(
            MeshObject mesh, int v, List<int> fan, EdgeData bd, bool isV0, int segments,
            Dictionary<(int face, int vertex), int[]> faceVertexOverride)
        {
            int faceA = bd.FaceAIdx, faceB = bd.FaceBIdx;
            int bv = isV0 ? bd.FaceB_V0 : bd.FaceB_V1;
            if (bv != v || !fan.Contains(faceA) || !fan.Contains(faceB)) return;
            int other = isV0 ? bd.V1 : bd.V0;   // ベベルする辺の、v でない側

            // 面ごとの v の両隣
            var nb = new Dictionary<int, (int prev, int next)>();
            foreach (int f in fan)
            {
                var vi = mesh.Faces[f].VertexIndices;
                int n = vi.Count, pos = vi.IndexOf(v);
                if (n < 3 || pos < 0) return;
                nb[f] = (vi[(pos - 1 + n) % n], vi[(pos + 1) % n]);
            }

            // A から、ベベルする辺を渡らずに隣の面へ進む
            var path = new List<int> { faceA };
            var shared = new List<int>();          // path[i] と path[i+1] の境の辺の v でない端
            int cur = faceA;
            int from = other;                      // A へ入ってきた辺（ベベルする辺）の端
            var visited = new HashSet<int> { faceA };
            while (cur != faceB)
            {
                var (p, q) = nb[cur];
                int w = p == from ? q : (q == from ? p : -1);
                if (w < 0 || w == other) return;
                int next = -1;
                foreach (int f in fan)
                {
                    if (visited.Contains(f)) continue;
                    var (fp, fq) = nb[f];
                    if (fp == w || fq == w) { next = f; break; }
                }
                if (next < 0) return;
                shared.Add(w);
                path.Add(next);
                visited.Add(next);
                from = w;
                cur = next;
            }
            int m = path.Count - 2;               // 側面の数
            if (m < 1) return;

            int[] row = isV0 ? bd.RowV0 : bd.RowV1;
            for (int j = 1; j <= m; j++)
            {
                int c = path[j];
                int i0 = Mathf.RoundToInt((j - 1) * (float)segments / m);
                int i1 = Mathf.RoundToInt(j * (float)segments / m);
                int wPrev = shared[j - 1];       // A 側の境の辺の端
                var seq = new List<int>();
                for (int s = i0; s <= i1; s++) seq.Add(row[s]);
                // 面の並び [.., prev, v, next, ..] で、prev が A 側の境なら段の小さい方から
                if (nb[c].prev != wPrev) seq.Reverse();
                faceVertexOverride[(c, v)] = seq.ToArray();
            }
        }

        private static void AddFaceByNeighbor(Dictionary<int, List<int>> map, int w, int face)
        {
            if (!map.TryGetValue(w, out var list)) { list = new List<int>(); map[w] = list; }
            if (!list.Contains(face)) list.Add(face);
        }

        private static void AddRegionEnd(Dictionary<int, List<(int k, bool sideA)>> map, int region, int k, bool sideA)
        {
            if (!map.TryGetValue(region, out var list)) { list = new List<(int, bool)>(); map[region] = list; }
            list.Add((k, sideA));
        }

        private static int Find(Dictionary<int, int> parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        private static void Union(Dictionary<int, int> parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[rb] = ra;
        }

        /// <summary>
        /// 2 つの向き d1, d2 のそれぞれへの射影が |d1|, |d2| になる点への向き（マイター）。
        /// m = a·u1 + b·u2（u = d/|d|, c = u1·u2）、a = (l1 − c·l2)/(1 − c²)、b = (l2 − c·l1)/(1 − c²)。
        /// 単位ベクトルなら (d1+d2)/(1+d1·d2)。中間の段（Lerp で長さが 1 未満）もこの式で両方の段の位置に乗る。
        /// ほぼ平行・3 本以上は平均で代える。
        /// </summary>
        private static Vector3 Miter(List<Vector3> dirs)
        {
            if (dirs.Count == 2)
            {
                float l1 = dirs[0].magnitude, l2 = dirs[1].magnitude;
                if (l1 > 1e-6f && l2 > 1e-6f)
                {
                    Vector3 u1 = dirs[0] / l1, u2 = dirs[1] / l2;
                    float c = Vector3.Dot(u1, u2);
                    float den = 1f - c * c;
                    if (den > 1e-4f)
                    {
                        float a = (l1 - c * l2) / den;
                        float b = (l2 - c * l1) / den;
                        return a * u1 + b * u2;
                    }
                }
            }
            Vector3 sum = Vector3.zero;
            foreach (var d in dirs) sum += d;
            return sum / Mathf.Max(1, dirs.Count);
        }

        private static void AddDragVertex(
            List<(int rawIdx, Vector3 basePos, Vector3 offsetDir)> list,
            Dictionary<int, (Vector3 basePos, Vector3 dir)> merged,
            int rawIdx, Vector3 basePos, Vector3 dir)
        {
            if (merged.TryGetValue(rawIdx, out var m)) list.Add((rawIdx, m.basePos, m.dir));
            else list.Add((rawIdx, basePos, dir));
        }

        /// <summary>
        /// 頂点に集まる辺の段の列（A 側→B 側）を、共有する端点でつないで 1 周にする。
        /// つながらなければ null。
        /// </summary>
        private static List<int> BuildCapLoop(List<EdgeEnd> ends, List<EdgeData> bevelDataList, int segments)
        {
            var rows = new List<int[]>();
            foreach (var en in ends)
            {
                var bd = bevelDataList[en.EdgeIdx];
                rows.Add(en.IsV0 ? bd.RowV0 : bd.RowV1);
            }

            var used = new bool[rows.Count];
            var loop = new List<int>();
            used[0] = true;
            for (int s = 0; s <= segments; s++) loop.Add(rows[0][s]);
            int start = rows[0][0];
            int end   = rows[0][segments];

            for (int step = 1; step < rows.Count; step++)
            {
                int next = -1; bool forward = true;
                for (int i = 0; i < rows.Count && next < 0; i++)
                {
                    if (used[i]) continue;
                    if (rows[i][0] == end)        { next = i; forward = true; }
                    else if (rows[i][segments] == end) { next = i; forward = false; }
                }
                if (next < 0) return null;
                used[next] = true;
                if (forward) { for (int s = 1; s <= segments; s++) loop.Add(rows[next][s]); end = rows[next][segments]; }
                else         { for (int s = segments - 1; s >= 0; s--) loop.Add(rows[next][s]); end = rows[next][0]; }
            }

            if (end != start) return null;
            loop.RemoveAt(loop.Count - 1);   // 始点と同じ

            // 連続する同じ頂点を除く
            var dedup = new List<int>();
            foreach (int x in loop) if (dedup.Count == 0 || dedup[dedup.Count - 1] != x) dedup.Add(x);
            if (dedup.Count > 1 && dedup[0] == dedup[dedup.Count - 1]) dedup.RemoveAt(dedup.Count - 1);
            return dedup;
        }

        /// <summary>多角形の法線（先頭の頂点からの扇の三角形の法線の和）。</summary>
        private static Vector3 PolygonNormal(MeshObject mesh, List<int> loop)
        {
            Vector3 sum = Vector3.zero;
            Vector3 p0 = mesh.Vertices[loop[0]].Position;
            for (int i = 1; i + 1 < loop.Count; i++)
                sum += NormalHelper.CalculateFaceNormal(p0, mesh.Vertices[loop[i]].Position, mesh.Vertices[loop[i + 1]].Position);
            return sum;
        }

        private static int[] Reversed(int[] src)
        {
            var r = new int[src.Length];
            for (int i = 0; i < src.Length; i++) r[i] = src[src.Length - 1 - i];
            return r;
        }

        private static List<int> RemoveOrphanVertices(MeshObject mesh, HashSet<int> candidates, out int[] map)
        {
            var usedVertices = new HashSet<int>();
            foreach (var face in mesh.Faces)
            {
                foreach (int vi in face.VertexIndices)
                    usedVertices.Add(vi);
            }

            var toRemove = candidates.Where(v => !usedVertices.Contains(v) && v >= 0 && v < mesh.VertexCount)
                                     .ToList();

            // 索引の詰めと、選択・パーツ選択辞書への付け替えは
            // MeshObject.RemoveVertices が行う（MeshObject.Removal.cs）。
            // UVIndices / NormalIndices は頂点内のスロット番号であって頂点索引ではないため触らない
            // （DeleteSelectionTool.ApplyKill の注記と同じ扱い）。
            map = mesh.RemoveVertices(toRemove);

            // 呼び出し元がインデックスシフト補正に使えるよう昇順で返す
            toRemove.Sort();
            return toRemove;
        }

        private static Vector3 GetInwardOffset(MeshObject mesh, Face face, int v0, int v1)
        {
            Vector3 faceNormal = CalculateFaceNormal(mesh, face);
            Vector3 p0 = mesh.Vertices[v0].Position;
            Vector3 p1 = mesh.Vertices[v1].Position;
            Vector3 edgeDir = (p1 - p0).normalized;
            Vector3 inward = Vector3.Cross(faceNormal, edgeDir).normalized;

            Vector3 faceCenter = CalculateFaceCenter(mesh, face);
            Vector3 edgeCenter = (p0 + p1) * 0.5f;
            Vector3 toCenter = (faceCenter - edgeCenter).normalized;

            if (Vector3.Dot(inward, toCenter) < 0)
                inward = -inward;

            return inward;
        }

        private static Vector3 CalculateFaceNormal(MeshObject mesh, Face face)
        {
            if (face.VertexCount < 3) return Vector3.up;

            Vector3 p0 = mesh.Vertices[face.VertexIndices[0]].Position;
            Vector3 p1 = mesh.Vertices[face.VertexIndices[1]].Position;
            Vector3 p2 = mesh.Vertices[face.VertexIndices[2]].Position;

            return NormalHelper.CalculateFaceNormal(p0, p1, p2);
        }

        private static Vector3 CalculateFaceCenter(MeshObject mesh, Face face)
        {
            Vector3 center = Vector3.zero;
            foreach (int vi in face.VertexIndices)
                center += mesh.Vertices[vi].Position;
            return center / face.VertexCount;
        }

        private static List<EdgeInfo> CollectTargetEdges(MeshObject mesh, IEnumerable<VertexPair> edges)
        {
            var list = new List<EdgeInfo>();

            foreach (var edgePair in edges)
            {
                int v0 = edgePair.V1;
                int v1 = edgePair.V2;

                if (v0 < 0 || v0 >= mesh.VertexCount) continue;
                if (v1 < 0 || v1 >= mesh.VertexCount) continue;

                Vector3 p0 = mesh.Vertices[v0].Position;
                Vector3 p1 = mesh.Vertices[v1].Position;

                FindAdjacentFacesWithEdgeVertices(mesh, v0, v1, p0, p1,
                    out int faceA, out int faceB, out int faceB_V0, out int faceB_V1);

                list.Add(new EdgeInfo
                {
                    V0 = v0,
                    V1 = v1,
                    FaceA = faceA,
                    FaceB = faceB,
                    FaceB_V0 = faceB_V0,
                    FaceB_V1 = faceB_V1,
                });
            }
            return list;
        }

        /// <summary>
        /// edgeの両端(v0,v1)を含む隣接面を最大2つ探す。
        /// まず頂点インデックスで検索し、見つからない場合は位置ベースで探索する（非共有頂点対応）。
        /// faceB_V0/faceB_V1 は FaceB 内での対応頂点インデックス（共有時は v0/v1 と同値）。
        /// </summary>
        private static void FindAdjacentFacesWithEdgeVertices(
            MeshObject mesh, int v0, int v1, Vector3 p0, Vector3 p1,
            out int faceA, out int faceB, out int faceB_V0, out int faceB_V1)
        {
            faceA = -1; faceB = -1;
            faceB_V0 = v0; faceB_V1 = v1; // デフォルト: 共有頂点と同値

            const float eps = 1e-4f;

            for (int i = 0; i < mesh.FaceCount; i++)
            {
                var face = mesh.Faces[i];
                if (face.VertexCount < 3) continue;

                // ── 頂点インデックスによる一致（共有頂点） ──
                if (face.VertexIndices.Contains(v0) && face.VertexIndices.Contains(v1))
                {
                    if (faceA < 0) faceA = i;
                    else if (faceB < 0) { faceB = i; faceB_V0 = v0; faceB_V1 = v1; break; }
                    continue;
                }

                // ── 位置による一致（非共有頂点フォールバック） ──
                int matchV0 = -1, matchV1 = -1;
                foreach (int vi in face.VertexIndices)
                {
                    if (vi < 0 || vi >= mesh.VertexCount) continue;
                    Vector3 vp = mesh.Vertices[vi].Position;
                    if (matchV0 < 0 && (vp - p0).sqrMagnitude < eps) matchV0 = vi;
                    if (matchV1 < 0 && (vp - p1).sqrMagnitude < eps) matchV1 = vi;
                    if (matchV0 >= 0 && matchV1 >= 0) break;
                }
                if (matchV0 < 0 || matchV1 < 0) continue;

                if (faceA < 0) faceA = i;
                else if (faceB < 0)
                {
                    faceB = i;
                    faceB_V0 = matchV0;
                    faceB_V1 = matchV1;
                    break;
                }
            }
        }
    }
}
