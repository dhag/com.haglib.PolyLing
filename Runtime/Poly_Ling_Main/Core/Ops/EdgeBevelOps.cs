// EdgeBevelOps.cs
// 辺のベベルの実処理。EdgeBevelTool（マウス・コマンド）と処理列の種類 bevel（BevelMeshModifier）が共用する。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 段 7a（EdgeBevelTool.ExecuteBevel から切り出した）
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

            // ============================================================
            // Pass 1: 全エッジの row 頂点を生成する（フェースは変更しない）
            //
            // 目的: Pass 2 でフェース走査する前に全エッジの row を確定させる。
            //       逐次処理だと連続線分の共有頂点で「すでに v が置換済み」と
            //       なって後続エッジの処理が破綻するため、計算と適用を分離する。
            //
            // row の構造:
            //   row[0]        : FaceA 側端点  (FaceA の v を置換する頂点)
            //   row[1..n-1]   : 中間頂点群    (Segments >= 2 のときに存在)
            //   row[segments] : FaceB 側端点  (FaceB の v を置換する頂点)
            //
            // フラット (Fillet=false):
            //   offset = Lerp(offsetA*amount, offsetB*amount, t)
            //   → FaceA方向とFaceB方向の間を等分に線形補間
            //
            // フィレット (Fillet=true, segments>=2):
            //   offset = Slerp(offsetA, offsetB, t) * amount
            //   → 方向ベクトルを球面線形補間して弧を描く
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
                // faceA 内で V1 が V0 の直後なら、その辺は V0→V1 の向き。
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

                    Vector3 offset = (fillet && segments >= 2)
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
                    // Pass2 でフェースを書き換えた後では使えないため事前に保存する。
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

            // ============================================================
            // Pass 1.5: 連続線分の共有端点をマージする
            //
            // 問題:
            //   e1=(v0,v1), e2=(v1,v2) の連続線分では、共有頂点 v1 に対して
            //   e1 用 rowV1[0..n] と e2 用 rowV0[0..n] が Pass1 で別々の頂点として
            //   生成される。共有フェース側の端点は同一頂点であるべきなので
            //   ここでマージする。
            //
            // アルゴリズム:
            //   1. 原頂点 → 接続エッジリスト (edgeIdx, isV0) のマップを構築
            //   2. 2辺以上が接続する頂点について各エッジペアを処理:
            //      a. 両エッジが共有するフェースを特定
            //         (e1.FaceAIdx == e2.FaceAIdx / FaceAIdx == FaceBIdx / ... の4通り)
            //      b. 共有フェース側の row インデックスを決定
            //         faceA 側 → row[0], faceB 側 → row[segments]
            //      c. e2 の共有フェース端点を e1 の値で上書き
            //         e2 の旧頂点は使われなくなるので orphanCandidates に追加
            // ============================================================

            // 原頂点 → 接続エッジリスト を構築
            // isV0=true のとき対象エッジの V0 側、false のとき V1 側
            var vertexToEdges = new Dictionary<int, List<(int edgeIdx, bool isV0)>>();
            for (int ei = 0; ei < bevelDataList.Count; ei++)
            {
                var bd = bevelDataList[ei];
                if (!vertexToEdges.ContainsKey(bd.V0)) vertexToEdges[bd.V0] = new List<(int, bool)>();
                if (!vertexToEdges.ContainsKey(bd.V1)) vertexToEdges[bd.V1] = new List<(int, bool)>();
                vertexToEdges[bd.V0].Add((ei, true));
                vertexToEdges[bd.V1].Add((ei, false));
            }

            foreach (var kv in vertexToEdges)
            {
                var connections = kv.Value;
                if (connections.Count < 2) continue; // 孤立エッジ → マージ不要

                // 接続エッジの全ペアを処理
                for (int a = 0; a < connections.Count; a++)
                for (int b = a + 1; b < connections.Count; b++)
                {
                    int eiA = connections[a].edgeIdx;
                    bool isV0A = connections[a].isV0;
                    int eiB = connections[b].edgeIdx;
                    bool isV0B = connections[b].isV0;

                    var bdA = bevelDataList[eiA];
                    var bdB = bevelDataList[eiB];

                    // 両エッジが共有するフェースインデックスを探す
                    // (4通りの組み合わせを確認)
                    int sharedFaceIdx = -1;
                    bool sharedIsFaceAofA = false; // bdA 側で sharedFace が FaceA かどうか
                    bool sharedIsFaceAofB = false; // bdB 側で sharedFace が FaceA かどうか

                    if      (bdA.FaceAIdx == bdB.FaceAIdx) { sharedFaceIdx = bdA.FaceAIdx; sharedIsFaceAofA = true;  sharedIsFaceAofB = true;  }
                    else if (bdA.FaceAIdx == bdB.FaceBIdx) { sharedFaceIdx = bdA.FaceAIdx; sharedIsFaceAofA = true;  sharedIsFaceAofB = false; }
                    else if (bdA.FaceBIdx == bdB.FaceAIdx) { sharedFaceIdx = bdA.FaceBIdx; sharedIsFaceAofA = false; sharedIsFaceAofB = true;  }
                    else if (bdA.FaceBIdx == bdB.FaceBIdx) { sharedFaceIdx = bdA.FaceBIdx; sharedIsFaceAofA = false; sharedIsFaceAofB = false; }

                    if (sharedFaceIdx < 0) continue; // 共有フェースなし → 連続していない

                    // 共有フェース側の row インデックス:
                    //   FaceA側 → row[0], FaceB側 → row[segments]
                    int rowIdxA = sharedIsFaceAofA ? 0 : segments;
                    int rowIdxB = sharedIsFaceAofB ? 0 : segments;

                    // bdA/bdB の row 配列は int[] (参照型) なので直接書き換え可
                    int[] rowA = isV0A ? bdA.RowV0 : bdA.RowV1;
                    int[] rowB = isV0B ? bdB.RowV0 : bdB.RowV1;

                    // bdB の端点を bdA の値に統一し、旧頂点を孤立候補に追加
                    int oldVertex = rowB[rowIdxB];
                    if (oldVertex != rowA[rowIdxA])
                    {
                        orphanCandidates.Add(oldVertex);
                        rowB[rowIdxB] = rowA[rowIdxA];
                    }
                }
            }

            // Pass 1.5 終了後: bevelDataList の RowV0/RowV1 はマージ済み最終値。
            // ドラッグ更新用に (rawIndex, basePos, offsetDir) を収集する。
            // orphanCandidates 除去によるインデックスシフトは後で調整する。
            bool useFillet = fillet && segments >= 2;
            var rawDragVerts = new List<(int rawIdx, Vector3 basePos, Vector3 offsetDir)>();
            foreach (var bd in bevelDataList)
            {
                for (int s = 0; s <= segments; s++)
                {
                    float t = segments > 0 ? (float)s / segments : 0f;
                    Vector3 dir = useFillet
                        ? Vector3.Slerp(bd.OffsetA, bd.OffsetB, t)
                        : Vector3.Lerp(bd.OffsetA, bd.OffsetB, t);
                    rawDragVerts.Add((bd.RowV0[s], bd.P0, dir));
                    rawDragVerts.Add((bd.RowV1[s], bd.P1, dir));
                }
            }

            // ============================================================
            // Pass 2: ベベル前の全フェースを走査し、頂点置換を一括適用する
            //
            // 各フェースの各頂点ポジション(pos)について、置換計画を収集してから
            // 後ろの pos 順（降順）に適用する。
            // 降順適用の理由: InsertRange で頂点数が増えると前方の pos がずれるが、
            //                 後ろから処理すれば前方の pos に影響しない。
            //
            // フェースとエッジの関係に応じて3パターンの置換を行う:
            //
            //   [A] faceA に該当  → v を row[0] に単一置換
            //                       (faceA 側端点で差し替え)
            //
            //   [B] faceB に該当  → v を row[segments] に単一置換
            //                       (faceB 側端点で差し替え)
            //
            //   [C] 側面フェース  → v を row 全体のシーケンスで展開
            //                       (N角形 → N+segments 角形)
            //                       ただし、同じ pos に [A][B] が既に登録されている場合は
            //                       [A][B] を優先し [C] は無視する。
            //                       (連続線分の共有頂点で複数エッジが競合する場合の対処)
            //
            // [C] の挿入方向の決め方:
            //   - v の prev 隣接頂点が FaceAOrigVerts に含まれる
            //     → prev 側が faceA 側 → row[0]→row[segments] 順で挿入
            //   - v の prev 隣接頂点が FaceBOrigVerts に含まれる
            //     → prev 側が faceB 側 → row[segments]→row[0] 順（逆順）で挿入
            //   - どちらでもない（連続線分の中間フェース等）
            //     → 中点インデックス row[segments/2] で単一置換（近似）
            // ============================================================

            // ベベルフェース追加前のフェース数（Pass3 で追加するフェースは走査しない）
            int faceCountBeforeBevel = mesh.FaceCount;

            for (int fi = 0; fi < faceCountBeforeBevel; fi++)
            {
                var face = mesh.Faces[fi];

                // このフェースの頂点置換計画: key=pos(降順), value=置換シーケンス
                // 単一置換 → length=1 の配列、シーケンス展開 → length=segments+1 の配列
                var replacements = new SortedDictionary<int, int[]>(
                    Comparer<int>.Create((a, b) => b - a)); // 降順ソート

                foreach (var bd in bevelDataList)
                {
                    // このエッジの両端点 (V0, V1) それぞれについて処理
                    for (int vi = 0; vi < 2; vi++)
                    {
                        // FaceB の場合は FaceB_V0/FaceB_V1 を使う（非共有頂点対応）
                        int v;
                        if (fi == bd.FaceBIdx)
                            v = (vi == 0) ? bd.FaceB_V0 : bd.FaceB_V1;
                        else
                            v = (vi == 0) ? bd.V0 : bd.V1;

                        int[] row = (vi == 0) ? bd.RowV0 : bd.RowV1;

                        // v がこのフェースに含まれていなければスキップ
                        int pos = face.VertexIndices.IndexOf(v);
                        if (pos < 0) continue;

                        if (fi == bd.FaceAIdx)
                        {
                            // [A] faceA: row[0] (FaceA側端点) で単一置換
                            // faceA/faceB 扱いは側面より優先 → 上書きで登録
                            replacements[pos] = new[] { row[0] };
                        }
                        else if (fi == bd.FaceBIdx)
                        {
                            // [B] faceB: row[segments] (FaceB側端点) で単一置換
                            replacements[pos] = new[] { row[segments] };
                        }
                        else
                        {
                            // [C] 側面フェース
                            // 同じ pos に既に [A] or [B] が登録済みなら無視（優先度低）
                            if (replacements.ContainsKey(pos)) continue;

                            int count = face.VertexIndices.Count;
                            int prev = face.VertexIndices[(pos - 1 + count) % count];

                            if (bd.FaceAOrigVerts.Contains(prev))
                            {
                                // prev が FaceA 側 → row[0]..row[segments] 順で展開
                                replacements[pos] = row;
                            }
                            else if (bd.FaceBOrigVerts.Contains(prev))
                            {
                                // prev が FaceB 側 → 逆順で展開
                                replacements[pos] = Reversed(row);
                            }
                            else
                            {
                                // 連続線分の中間フェース等: 隣接情報が不明 → 中点で近似
                                replacements[pos] = new[] { row[row.Length / 2] };
                            }
                        }
                    }
                }

                // 降順の pos で適用（後ろから処理することで前方インデックスがずれない）
                foreach (var kv in replacements)
                {
                    int pos = kv.Key;
                    int[] seq = kv.Value;

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
            //
            // 隣接する row[s] と row[s+1] の間に四角フェースを生成する。
            //
            // 巻き順の決め方:
            //   幾何法線ではなく、隣接する元面 faceA の辺 (V0,V1) の並び順から継承する
            //   （押し出しツールと同型）。これによりメッシュの巻き規約（PMX/MQO）に依存せず
            //   常に隣接面と一貫した向きになる。FaceAReverse は Pass1 で faceA から算出済み。
            // ============================================================
            foreach (var bd in bevelDataList)
            {
                for (int s = 0; s < segments; s++)
                {
                    // faceA の辺が V0→V1（FaceAReverse）なら押し出しの reverseWinding と同型に並べる。
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
