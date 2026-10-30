// Assets/Editor/Poly_Ling/Utilities/MeshMergeHelper.cs
// 頂点結合・削除ロジックのヘルパークラス
// MergeVerticesTool, MeshCreatorWindowBase, SimpleMeshFactory_Selection から使用

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Ops
{
    /// <summary>
    /// 頂点マージ結果
    /// </summary>
    public struct MergeResult
    {
        public bool Success;
        public int RemovedVertexCount;
        public string Message;
    }

    /// <summary>
    /// 頂点結合・削除のヘルパークラス
    /// </summary>
    public static class MeshMergeHelper
    {
        // ================================================================
        // 頂点マージ（しきい値ベース）
        // ================================================================

        /// <summary>
        /// 指定された頂点のうち、しきい値以下の距離にあるものをマージする
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="targetVertices">マージ対象の頂点インデックス</param>
        /// <param name="threshold">距離しきい値</param>
        /// <param name="removeClosedFaces">true のとき、結合後に閉じた面ペア（逆巻きの重なり面）を削除する</param>
        /// <returns>マージ結果</returns>
        public static MergeResult MergeVerticesAtSamePosition(MeshObject meshObject, HashSet<int> targetVertices, float threshold = 0.001f, bool removeClosedFaces = false)
        {
            var result = new MergeResult { Success = false };

            if (meshObject == null || targetVertices == null || targetVertices.Count < 2)
            {
                result.Message = "Not enough vertices selected";
                return result;
            }

            var validSelected = targetVertices
                .Where(v => v >= 0 && v < meshObject.VertexCount)
                .ToList();

            if (validSelected.Count < 2)
            {
                result.Message = "Not enough valid vertices";
                return result;
            }

            // Union-Find
            var parent = new int[meshObject.VertexCount];
            var rank = new int[meshObject.VertexCount];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            int Find(int x)
            {
                if (parent[x] != x) parent[x] = Find(parent[x]);
                return parent[x];
            }

            void Unite(int x, int y)
            {
                int rx = Find(x), ry = Find(y);
                if (rx == ry) return;
                if (rank[rx] < rank[ry]) parent[rx] = ry;
                else if (rank[rx] > rank[ry]) parent[ry] = rx;
                else { parent[ry] = rx; rank[rx]++; }
            }

            // 距離計算
            // 総当たり（n²）だと 1 万頂点で数秒かかるので、一辺 threshold の格子に入れ、
            // 隣り合う 27 格子の中だけを比べる。threshold 以内の組はかならず隣接格子に入るので、
            // 結ばれる組は総当たりと同じ。
            float cellSize = Mathf.Max(threshold, 1e-7f);
            var grid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) CellOf(Vector3 p)
                => (Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize), Mathf.FloorToInt(p.z / cellSize));

            foreach (int vi in validSelected)
            {
                Vector3 p = meshObject.Vertices[vi].Position;
                var c = CellOf(p);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!grid.TryGetValue((c.Item1 + dx, c.Item2 + dy, c.Item3 + dz), out var cellList)) continue;
                    foreach (int vj in cellList)
                        if (Vector3.Distance(p, meshObject.Vertices[vj].Position) <= threshold)
                            Unite(vi, vj);
                }
                if (!grid.TryGetValue(c, out var own)) grid[c] = own = new List<int>();
                own.Add(vi);
            }

            // グループ収集
            var groups = new Dictionary<int, List<int>>();
            foreach (int v in validSelected)
            {
                int root = Find(v);
                if (!groups.ContainsKey(root))
                    groups[root] = new List<int>();
                groups[root].Add(v);
            }

            var mergeGroups = groups.Values.Where(g => g.Count >= 2).ToList();

            if (mergeGroups.Count == 0)
            {
                result.Message = "No vertices within threshold";
                result.Success = true;  // エラーではない
                result.RemovedVertexCount = 0;
                return result;
            }

            // 頂点リマップを構築
            var vertexRemap = new Dictionary<int, int>();
            var verticesToRemove = new HashSet<int>();
            var mergedRepresentatives = new HashSet<int>();

            foreach (var group in mergeGroups)
            {
                int representative = group.Min();
                mergedRepresentatives.Add(representative);

                // 重心を計算
                Vector3 centroid = Vector3.zero;
                foreach (int v in group)
                    centroid += meshObject.Vertices[v].Position;
                centroid /= group.Count;

                meshObject.Vertices[representative].Position = centroid;

                foreach (int v in group)
                {
                    if (v != representative)
                    {
                        vertexRemap[v] = representative;
                        verticesToRemove.Add(v);
                    }
                }
            }

            // Faceの頂点インデックスを更新
            foreach (var face in meshObject.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (vertexRemap.TryGetValue(face.VertexIndices[i], out int newIdx))
                        face.VertexIndices[i] = newIdx;
                }
            }

            var __sw = System.Diagnostics.Stopwatch.StartNew(); // TEMP-TIMING
            // figure-8型自己交差面を分割してから縮退面を削除
            SplitSelfIntersectingFaces(meshObject);
            long __tSplit = __sw.ElapsedMilliseconds; // TEMP-TIMING
            RemoveDegenerateFaces(meshObject);
            long __tDegen = __sw.ElapsedMilliseconds - __tSplit; // TEMP-TIMING

            // 閉じた面ペア（逆巻きの重なり面）を削除。
            // 結合に関与した頂点（各グループの代表）を含む面だけを見る。
            // 索引は RemoveVertices の前なので代表索引がそのまま通じる。
            if (removeClosedFaces)
                RemoveClosedFacePairs(meshObject, mergedRepresentatives);
            long __tMark = __sw.ElapsedMilliseconds; // TEMP-TIMING

            // 不要頂点を削除。
            //   旧索引→新索引の表（結合された頂点は代表の新索引）を作り、面を新しい索引へ
            //   書き換えてから、面を触らない入口 RemoveVerticesWithMap で頂点を詰める。
            //   この入口を通すことで、線分群（LineGroups）・選択・ID 表・除外セットも同じ表で
            //   付け替わる（自前で Vertices を詰めるとこれらが取り残される）。
            if (verticesToRemove.Count > 0)
            {
                int vcount = meshObject.VertexCount;
                var keptIndex = new int[vcount];
                int w = 0;
                for (int i = 0; i < vcount; i++)
                    keptIndex[i] = verticesToRemove.Contains(i) ? -1 : w++;

                var map = new int[vcount];
                for (int i = 0; i < vcount; i++)
                {
                    int rep = vertexRemap.TryGetValue(i, out int r) ? r : i;
                    map[i] = keptIndex[rep];
                }

                foreach (var face in meshObject.Faces)
                {
                    var vidx = face.VertexIndices;
                    for (int i = 0; i < vidx.Count; i++)
                    {
                        int ov = vidx[i];
                        if (ov >= 0 && ov < vcount) vidx[i] = map[ov];
                    }
                }

                meshObject.RemoveVerticesWithMap(verticesToRemove, map);

                // 逆方向の控え（ModifierFrameTrace）も同じ表で付け替える。
                ModifierFrameTrace.Remapped(meshObject, map);
            }
            UnityEngine.Debug.Log($"[PL/Timing] Merge groups={mergeGroups.Count} removed={verticesToRemove.Count} split={__tSplit}ms degenerate={__tDegen}ms closedPairs={__tMark - __tSplit - __tDegen}ms removeVertices={__sw.ElapsedMilliseconds - __tMark}ms"); // TEMP-TIMING

            result.Success = true;
            result.RemovedVertexCount = verticesToRemove.Count;
            result.Message = $"Merged {verticesToRemove.Count} vertices into {mergeGroups.Count} groups";

            return result;
        }

        /// <summary>
        /// メッシュ内の全頂点を対象に、しきい値以下の距離にあるものをマージする
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="threshold">距離しきい値</param>
        /// <returns>マージ結果</returns>
        public static MergeResult MergeAllVerticesAtSamePosition(MeshObject meshObject, float threshold = 0.001f)
        {
            if (meshObject == null || meshObject.VertexCount < 2)
                return new MergeResult { Success = false, Message = "Not enough vertices" };

            var allVertices = new HashSet<int>(Enumerable.Range(0, meshObject.VertexCount));
            return MergeVerticesAtSamePosition(meshObject, allVertices, threshold);
        }

        // ================================================================
        // 頂点マージ（重心に統合）
        // ================================================================

        /// <summary>
        /// 選択された頂点を重心位置に統合する
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="verticesToMerge">マージ対象の頂点インデックス</param>
        /// <returns>マージ後の頂点インデックス（失敗時は-1）</returns>
        public static int MergeVerticesToCentroid(MeshObject meshObject, HashSet<int> verticesToMerge)
        {
            if (verticesToMerge == null || verticesToMerge.Count < 2) return -1;
            int originalCount = meshObject.VertexCount;
            if (originalCount == 0) return -1;

            // 1. マージ先の頂点を決定（重心を計算）
            Vector3 centroid = Vector3.zero;
            int validCount = 0;
            foreach (int idx in verticesToMerge)
            {
                if (idx >= 0 && idx < originalCount)
                {
                    centroid += meshObject.Vertices[idx].Position;
                    validCount++;
                }
            }
            if (validCount < 2) return -1;
            centroid /= validCount;

            // 最小インデックスの頂点をマージ先とし、位置を重心に更新
            int targetVertex = verticesToMerge.Where(i => i >= 0 && i < originalCount).Min();
            meshObject.Vertices[targetVertex].Position = centroid;

            // 2. インデックスマッピングを作成
            var indexMap = new int[originalCount];
            int newIndex = 0;

            for (int i = 0; i < originalCount; i++)
            {
                if (verticesToMerge.Contains(i) && i != targetVertex)
                {
                    indexMap[i] = -2; // 後でtargetの新インデックスに更新
                }
                else
                {
                    indexMap[i] = newIndex++;
                }
            }

            // targetVertexの新インデックスを取得
            int targetNewIndex = indexMap[targetVertex];

            // マージ対象のインデックスをtargetNewIndexに更新
            for (int i = 0; i < originalCount; i++)
            {
                if (indexMap[i] == -2)
                {
                    indexMap[i] = targetNewIndex;
                }
            }

            // 3. 面を処理
            //    消す面はここでは控えるだけにして、実際の削除は
            //    MeshObject.RemoveFaces にまとめて任せる（索引の詰めと
            //    選択・パーツ選択辞書への付け替えがそこで行われる）。
            var facesToRemove = new List<int>();
            for (int f = meshObject.FaceCount - 1; f >= 0; f--)
            {
                var face = meshObject.Faces[f];
                var newVertexIndices = new List<int>();
                var newUVIndices = new List<int>();
                var newNormalIndices = new List<int>();

                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    int oldIdx = face.VertexIndices[i];
                    if (oldIdx >= 0 && oldIdx < originalCount)
                    {
                        int mappedIdx = indexMap[oldIdx];

                        // 連続する同じ頂点を避ける（縮退した辺を防ぐ）
                        if (newVertexIndices.Count == 0 || newVertexIndices[newVertexIndices.Count - 1] != mappedIdx)
                        {
                            newVertexIndices.Add(mappedIdx);
                            if (i < face.UVIndices.Count) newUVIndices.Add(face.UVIndices[i]);
                            if (i < face.NormalIndices.Count) newNormalIndices.Add(face.NormalIndices[i]);
                        }
                    }
                }

                // 最初と最後が同じなら最後を除去
                if (newVertexIndices.Count > 1 && newVertexIndices[0] == newVertexIndices[newVertexIndices.Count - 1])
                {
                    newVertexIndices.RemoveAt(newVertexIndices.Count - 1);
                    if (newUVIndices.Count > 0) newUVIndices.RemoveAt(newUVIndices.Count - 1);
                    if (newNormalIndices.Count > 0) newNormalIndices.RemoveAt(newNormalIndices.Count - 1);
                }

                // 元が 2 頂点の面（線分）は 2 頂点残れば成立している。
                // 3 未満で一律に消すと、結合と無関係な線分まで全部消える。
                int minCount = face.VertexIndices.Count == 2 ? 2 : 3;
                if (newVertexIndices.Count < minCount)
                {
                    // 成立しなくなった面を削除（控えるだけ）。
                    // 消す面も新しい索引へ書き換えておく（詰めずに 1 対 1 で写す）。
                    // 面の削除は頂点を詰めた後に行うので、線分群（新しい索引）との
                    // 照合（LineGroupOps.SplitAtRemovedFaces）が同じ索引どうしになる。
                    var mapped = new List<int>(face.VertexIndices.Count);
                    foreach (int oldIdx in face.VertexIndices)
                        mapped.Add(oldIdx >= 0 && oldIdx < originalCount ? indexMap[oldIdx] : -1);
                    face.VertexIndices = mapped;
                    facesToRemove.Add(f);
                }
                else
                {
                    // 面を更新
                    face.VertexIndices = newVertexIndices;
                    face.UVIndices = newUVIndices;
                    face.NormalIndices = newNormalIndices;
                }
            }

            // 4. 頂点を削除（マージ先以外）
            //    面は上で新しい索引へ書き換え済みなので、面を触らない入口を使う。
            //    表（indexMap）をそのまま渡すことで、選択とパーツ選択辞書は
            //    マージ先の索引へ寄り、線分群は潰れた区間が畳まれる（MeshObject.Removal.cs）。
            var verticesToRemove = verticesToMerge
                .Where(i => i != targetVertex && i >= 0 && i < meshObject.VertexCount)
                .ToList();

            meshObject.RemoveVerticesWithMap(verticesToRemove, indexMap);

            // 5. 成立しなくなった面を消す。
            //    潰れた線分（両端が同じ頂点）は線分群側で既に畳まれているので、
            //    SplitAtRemovedFaces はその区間で群を切らない。
            meshObject.RemoveFaces(facesToRemove);

            return targetNewIndex;
        }

        // ================================================================
        // 頂点削除
        // ================================================================

        /// <summary>
        /// 指定された頂点を削除し、面のインデックスを更新する
        ///
        /// 【デッドコード】新規に使わないこと。
        /// - 呼び出し元は PolyLingCore_Selection.DeleteSelectedVertices のみで、
        ///   そちらも Runtime アセンブリ内に呼び出し元が 0 件 (grep 済み)。
        ///   Editor アセンブリ側からの呼び出しは未確認。
        ///
        /// 【既知の問題】下の面ループは残存頂点数が 3 未満の面を一律削除する。
        /// 線分 (2頂点の面) は削除頂点に触れていなくても残存 2 になるため、
        /// メッシュ内の線分が全て消える。
        /// 選択削除には使わないこと。Poly_Ling.Tools.DeleteSelectionTool は
        /// 「削除頂点を参照する面は丸ごと削除する」方式でこの問題を回避している。
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="verticesToDelete">削除する頂点インデックス</param>
        public static void DeleteVertices(MeshObject meshObject, HashSet<int> verticesToDelete)
        {
            if (meshObject == null || verticesToDelete == null || verticesToDelete.Count == 0)
                return;

            int originalCount = meshObject.VertexCount;
            if (originalCount == 0) return;

            // 1. 新しいインデックスへのマッピングを作成
            var indexMap = new int[originalCount];
            int newIndex = 0;
            for (int i = 0; i < originalCount; i++)
            {
                if (verticesToDelete.Contains(i))
                {
                    indexMap[i] = -1; // 削除される
                }
                else
                {
                    indexMap[i] = newIndex++;
                }
            }

            // 2. 面を処理（インデックス更新＆無効な面の削除）
            //    消す面は控えるだけにして、削除は MeshObject.RemoveFaces に任せる。
            var facesToRemove2 = new List<int>();
            for (int f = meshObject.FaceCount - 1; f >= 0; f--)
            {
                var face = meshObject.Faces[f];
                var newVertexIndices = new List<int>();
                var newUVIndices = new List<int>();
                var newNormalIndices = new List<int>();

                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    int oldIdx = face.VertexIndices[i];
                    if (oldIdx >= 0 && oldIdx < originalCount)
                    {
                        int mappedIdx = indexMap[oldIdx];
                        if (mappedIdx >= 0)
                        {
                            newVertexIndices.Add(mappedIdx);
                            if (i < face.UVIndices.Count) newUVIndices.Add(face.UVIndices[i]);
                            if (i < face.NormalIndices.Count) newNormalIndices.Add(face.NormalIndices[i]);
                        }
                    }
                }

                if (newVertexIndices.Count < 3)
                {
                    // 頂点数が3未満なら面を削除（控えるだけ）
                    facesToRemove2.Add(f);
                }
                else
                {
                    // 面を更新
                    face.VertexIndices = newVertexIndices;
                    face.UVIndices = newUVIndices;
                    face.NormalIndices = newNormalIndices;
                }
            }

            meshObject.RemoveFaces(facesToRemove2);

            // 3. 頂点を削除
            //    面は上で新しい索引へ書き換え済みなので、面を触らない入口を使う。
            //    表を渡すことで選択とパーツ選択辞書も追随する（MeshObject.Removal.cs）。
            meshObject.RemoveVerticesWithMap(verticesToDelete, indexMap);
        }

        // ================================================================
        // 縮退面削除
        // ================================================================

        /// <summary>
        /// 縮退した面（同じ頂点を複数参照する面）を削除
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <returns>削除した面の数</returns>
        /// <summary>
        /// 頂点リストに重複頂点が現れる面（figure-8型自己交差）を、
        /// 重複頂点の位置で複数の面に再帰的に分割する。
        /// マージ後に呼ぶこと。RemoveDegenerateFaces の前に実行する。
        /// </summary>
        //
        // 【全面を 1 回なめる】
        //   以前は分割する面を見つけるたびに RemoveFaces（全面の索引表・配列の詰め・ID 集合の
        //   作り直しなど）を 1 回ずつ呼んでいて、分割回数 × 面数の手間がかかっていた
        //   （小さい文字で 400 秒超）。
        //   いまは各面の頂点の並びを手元のリストだけで分け切り、分けた元の面の番号を控えて
        //   最後に RemoveFaces を 1 回だけ呼ぶ。分けてできた面は従来どおり末尾に足す。
        //   Blender の重複頂点の結合（bmo_weld_verts_exec）も、面を作り直したあとで
        //   古い面を最後にまとめて消している。
        //
        // 【分け方は従来と同じ】
        //   最初に重複が見つかる位置 a（a より後ろに同じ頂点がある最小の a）と、その後ろで
        //   最初に同じ頂点が現れる位置 b で、[a..b-1] と [b..末尾]+[0..a-1] の 2 つに分ける。
        //   分けた片方にまだ重複があれば同じ規則で分け続ける。頂点が 3 つ未満の片は捨てる。
        //   UV・法線の番号は頂点と同じ位置で切り出す（足りないときの扱いも従来と同じ）。
        public static void SplitSelfIntersectingFaces(MeshObject meshObject)
        {
            if (meshObject == null) return;

            var kill  = new List<int>();
            var added = new List<Face>();
            var work  = new Stack<(List<int> v, List<int> u, List<int> n)>();

            int faceCount = meshObject.FaceCount;
            for (int i = 0; i < faceCount; i++)
            {
                var face = meshObject.Faces[i];
                if (!FindSplit(face.VertexIndices, out _, out _)) continue;

                kill.Add(i);
                work.Push((face.VertexIndices, face.UVIndices, face.NormalIndices));

                while (work.Count > 0)
                {
                    var (verts, uvs, norms) = work.Pop();

                    if (!FindSplit(verts, out int splitStart, out int splitEnd))
                    {
                        // 重複が無くなった片。元の面そのもの（i）はここへ来ない（重複があるので）。
                        var f = new Face { MaterialIndex = face.MaterialIndex };
                        f.VertexIndices.AddRange(verts);
                        f.UVIndices.AddRange(uvs);
                        f.NormalIndices.AddRange(norms);
                        added.Add(f);
                        continue;
                    }

                    // ループ1: [splitStart .. splitEnd-1]
                    // ループ2: [splitEnd  .. verts.Count-1] + [0 .. splitStart-1]
                    var loop1V = verts.GetRange(splitStart, splitEnd - splitStart);
                    var loop1U = uvs.Count   >= splitEnd ? uvs.GetRange(splitStart,   splitEnd - splitStart) : new List<int>();
                    var loop1N = norms.Count >= splitEnd ? norms.GetRange(splitStart, splitEnd - splitStart) : new List<int>();

                    var loop2V = new List<int>();
                    var loop2U = new List<int>();
                    var loop2N = new List<int>();
                    for (int k = splitEnd; k < verts.Count; k++) { loop2V.Add(verts[k]); if (k < uvs.Count) loop2U.Add(uvs[k]); if (k < norms.Count) loop2N.Add(norms[k]); }
                    for (int k = 0;        k < splitStart;  k++) { loop2V.Add(verts[k]); if (k < uvs.Count) loop2U.Add(uvs[k]); if (k < norms.Count) loop2N.Add(norms[k]); }

                    // ループ1 を先に取り出すよう、ループ2 を先に積む。
                    // 出来る片の集合は従来と同じ。末尾に並ぶ順は従来（後回しの再検査）と違うことがある。
                    if (loop2V.Count >= 3) work.Push((loop2V, loop2U, loop2N));
                    if (loop1V.Count >= 3) work.Push((loop1V, loop1U, loop1N));
                }
            }

            if (kill.Count == 0) return;

            // 索引の詰めと辞書への付け替えは MeshObject.RemoveFaces が行う。
            meshObject.RemoveFaces(kill);
            meshObject.Faces.AddRange(added);
        }

        /// <summary>
        /// 分ける位置。a より後ろに同じ頂点がある最小の a と、a の後ろで最初に同じ頂点が現れる b。
        /// 重複が無ければ false。
        /// </summary>
        private static bool FindSplit(List<int> verts, out int a, out int b)
        {
            a = -1;
            b = -1;
            int n = verts.Count;

            if (n <= 16)
            {
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                        if (verts[i] == verts[j]) { a = i; b = j; return true; }
                return false;
            }

            // 大きい多角形は、各頂点が最後に現れる位置を表にしてから先頭から見る。
            // 「後ろに同じ頂点がある最小の a」は、最後に現れる位置が自分より後ろの最初の点。
            var last = new Dictionary<int, int>(n);
            for (int i = 0; i < n; i++) last[verts[i]] = i;
            for (int i = 0; i < n; i++)
            {
                if (last[verts[i]] <= i) continue;
                a = i;
                for (int j = i + 1; j < n; j++)
                    if (verts[j] == verts[i]) { b = j; return true; }
            }
            return false;
        }

        /// <summary>
        /// 縮退した面（unique頂点数 &lt; 3 または face.VertexCount と不一致）を削除。
        /// SplitSelfIntersectingFaces の後に呼ぶこと。
        /// </summary>
        public static int RemoveDegenerateFaces(MeshObject meshObject)
        {
            if (meshObject == null) return 0;

            var toRemove = new List<int>();

            for (int i = 0; i < meshObject.FaceCount; i++)
            {
                var face   = meshObject.Faces[i];
                var unique = new HashSet<int>(face.VertexIndices);

                // 2 頂点の面（線分）は、2 頂点が同じ番号に潰れたときだけ縮退とみなす。
                // 3 未満で一律に消すと、結合と無関係な線分まで全部消える。
                if (face.VertexCount == 2)
                {
                    if (unique.Count < 2) toRemove.Add(i);
                    continue;
                }

                // unique頂点が3未満、または重複頂点が残存している面を削除
                if (unique.Count < 3 || unique.Count < face.VertexCount)
                    toRemove.Add(i);
            }

            meshObject.RemoveFaces(toRemove);

            if (toRemove.Count > 0)
                Debug.Log($"[MeshMergeHelper] Removed {toRemove.Count} degenerate faces");

            return toRemove.Count;
        }

        // ================================================================
        // 閉じた面ペアの削除
        // ================================================================

        /// <summary>
        /// 閉じた面ペア（頂点索引の並びが一致し、巻き順だけが逆の面 2 枚）を両方削除する。
        ///
        /// 面の表裏は巻き順だけで決まる（NormalHelper.CalculateFaceNormal）。
        /// 並びの回転ずれ（[0,1,2,3] と [2,3,0,1] など）は吸収する。
        /// 巻き順が同じ完全重複面は対象にしない。
        /// 同じ組み合わせが 3 枚以上あるときは、組にできる分だけ削除して余りは残す。
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="touchedVertices">
        /// この頂点索引を 1 つ以上含む面だけを見る。結合に関与した頂点を渡すこと。
        /// null なら全面を見る。対になる 2 面は頂点集合が同じなので、片方が含めば両方含む。
        /// </param>
        /// <returns>削除した面の数</returns>
        public static int RemoveClosedFacePairs(MeshObject meshObject, HashSet<int> touchedVertices)
        {
            if (meshObject == null || meshObject.FaceCount < 2) return 0;

            // 正規化キー → まだ相方が見つかっていない面の索引
            var pending  = new Dictionary<string, List<int>>();
            var toRemove = new HashSet<int>();

            for (int i = 0; i < meshObject.FaceCount; i++)
            {
                var verts = meshObject.Faces[i].VertexIndices;
                if (verts == null || verts.Count < 3) continue;
                if (touchedVertices != null && !ContainsAny(verts, touchedVertices)) continue;

                // 先に来ている面のうち、この面の逆巻きと一致するものがあれば組にする
                string reversedKey = BuildFaceCycleKey(verts, true);
                if (pending.TryGetValue(reversedKey, out var waiting) && waiting.Count > 0)
                {
                    int partner = waiting[waiting.Count - 1];
                    waiting.RemoveAt(waiting.Count - 1);
                    toRemove.Add(partner);
                    toRemove.Add(i);
                    continue;
                }

                string key = BuildFaceCycleKey(verts, false);
                if (!pending.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    pending[key] = list;
                }
                list.Add(i);
            }

            if (toRemove.Count == 0) return 0;

            meshObject.RemoveFaces(toRemove);

            Debug.Log($"[MeshMergeHelper] Removed {toRemove.Count} closed faces");
            return toRemove.Count;
        }

        /// <summary>
        /// 面の頂点索引列が、指定した集合の要素を 1 つでも含むか。
        /// </summary>
        private static bool ContainsAny(List<int> verts, HashSet<int> set)
        {
            for (int i = 0; i < verts.Count; i++)
                if (set.Contains(verts[i])) return true;
            return false;
        }

        /// <summary>
        /// 面の頂点索引列を、巻き順を保ったまま最小索引が先頭に来るよう回転した文字列にする。
        /// reversed が true のときは列を反転してから回転する（逆巻き照合用）。
        /// </summary>
        private static string BuildFaceCycleKey(List<int> verts, bool reversed)
        {
            int n = verts.Count;
            var seq = new int[n];
            for (int i = 0; i < n; i++)
                seq[i] = reversed ? verts[n - 1 - i] : verts[i];

            int start = 0;
            for (int i = 1; i < n; i++)
                if (seq[i] < seq[start]) start = i;

            var sb = new System.Text.StringBuilder();
            sb.Append(n).Append(':');
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(seq[(start + i) % n]);
            }
            return sb.ToString();
        }

        // ================================================================
        // インデックスリマップ（ユーティリティ）
        // ================================================================

        /// <summary>
        /// 頂点削除用のインデックスリマップを構築
        /// </summary>
        public static Dictionary<int, int> BuildIndexRemap(int vertexCount, HashSet<int> verticesToRemove)
        {
            var remap = new Dictionary<int, int>();
            int newIndex = 0;

            for (int i = 0; i < vertexCount; i++)
            {
                if (!verticesToRemove.Contains(i))
                {
                    remap[i] = newIndex++;
                }
            }

            return remap;
        }

        /// <summary>
        /// 面のインデックスにリマップを適用
        /// </summary>
        public static void ApplyIndexRemapToFaces(MeshObject meshObject, Dictionary<int, int> remap)
        {
            if (meshObject == null || remap == null) return;

            // 消す面は控えるだけにして、削除は MeshObject.RemoveFaces に任せる。
            var facesToRemove = new List<int>();
            for (int f = meshObject.FaceCount - 1; f >= 0; f--)
            {
                var face = meshObject.Faces[f];
                var newVertexIndices = new List<int>();
                var newUVIndices = new List<int>();
                var newNormalIndices = new List<int>();

                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    int oldIdx = face.VertexIndices[i];
                    if (remap.TryGetValue(oldIdx, out int newIdx))
                    {
                        newVertexIndices.Add(newIdx);
                        if (i < face.UVIndices.Count) newUVIndices.Add(face.UVIndices[i]);
                        if (i < face.NormalIndices.Count) newNormalIndices.Add(face.NormalIndices[i]);
                    }
                }

                if (newVertexIndices.Count < 3)
                {
                    facesToRemove.Add(f);
                }
                else
                {
                    face.VertexIndices = newVertexIndices;
                    face.UVIndices = newUVIndices;
                    face.NormalIndices = newNormalIndices;
                }
            }

            meshObject.RemoveFaces(facesToRemove);
        }
    }
}
