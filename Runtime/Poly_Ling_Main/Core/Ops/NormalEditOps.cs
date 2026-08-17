// NormalEditOps.cs
// 選択範囲に対する法線編集操作。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【対象範囲のルール】
//   面選択がある     → その面のコーナーのみ
//   頂点選択のみある → その頂点が参照する全スロット
//   辺選択のみある   → 辺の両端頂点が参照する全スロット
//   選択が無い       → メッシュ全体
//
// 【不変条件（厳守）】
//   Vertex.UVs.Count == Vertex.Normals.Count
//   Face.UVIndices[j] == Face.NormalIndices[j]
//   スロットを増やす操作（Break）は Vertex.GetOrAddUVNormal のみを使う。
//   それ以外の操作は既存スロットの値を書き換えるだけでスロット数を変えない。
//   各操作の末尾で NormalSmoothingOps.ValidateSlotInvariant を実行する。
//
// 【Unify がスロットを畳まない理由】
//   スロットは (UV, 法線) の組で、Unity Mesh の頂点展開と 1:1 に対応している。
//   UV が異なるスロットを 1 本に畳むと UV が壊れるため、Unify は
//   「スロットは残したまま法線の値だけ同一にする」実装にしてある。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>面コーナー（面インデックス + コーナー番号）の指定。</summary>
    public readonly struct FaceCorner
    {
        public readonly int Face;
        public readonly int Corner;

        public FaceCorner(int face, int corner)
        {
            Face = face;
            Corner = corner;
        }
    }

    public static class NormalEditOps
    {
        private const float SlotTolerance = 0.0001f;

        // ================================================================
        // 対象コーナーの決定
        // ================================================================

        /// <summary>
        /// 選択状態から編集対象の面コーナーを列挙する。
        /// 3頂点未満の面（補助線）は常に対象外。
        /// </summary>
        public static List<FaceCorner> CollectTargetCorners(
            MeshObject mesh,
            IReadOnlyCollection<int> selectedFaces,
            IReadOnlyCollection<int> selectedVertices)
        {
            var result = new List<FaceCorner>();
            if (mesh == null) return result;

            // 面選択がある場合はその面のコーナーだけ
            if (selectedFaces != null && selectedFaces.Count > 0)
            {
                foreach (int fi in selectedFaces)
                {
                    if (fi < 0 || fi >= mesh.Faces.Count) continue;
                    var face = mesh.Faces[fi];
                    if (face == null || face.VertexCount < 3) continue;

                    for (int j = 0; j < face.VertexCount; j++)
                        result.Add(new FaceCorner(fi, j));
                }
                return result;
            }

            // 頂点選択がある場合はその頂点を参照するコーナー
            if (selectedVertices != null && selectedVertices.Count > 0)
            {
                var vset = selectedVertices as HashSet<int> ?? new HashSet<int>(selectedVertices);

                for (int fi = 0; fi < mesh.Faces.Count; fi++)
                {
                    var face = mesh.Faces[fi];
                    if (face == null || face.VertexCount < 3) continue;

                    for (int j = 0; j < face.VertexCount; j++)
                    {
                        if (vset.Contains(face.VertexIndices[j]))
                            result.Add(new FaceCorner(fi, j));
                    }
                }
                return result;
            }

            // 選択なし → メッシュ全体
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                if (face == null || face.VertexCount < 3) continue;

                for (int j = 0; j < face.VertexCount; j++)
                    result.Add(new FaceCorner(fi, j));
            }
            return result;
        }

        // ================================================================
        // 共通ヘルパー
        // ================================================================

        /// <summary>コーナーが参照する頂点インデックス。範囲外なら -1。</summary>
        private static int VertexOf(MeshObject mesh, FaceCorner fc)
        {
            if (fc.Face < 0 || fc.Face >= mesh.Faces.Count) return -1;
            var face = mesh.Faces[fc.Face];
            if (face == null || fc.Corner < 0 || fc.Corner >= face.VertexCount) return -1;

            int vi = face.VertexIndices[fc.Corner];
            return (vi >= 0 && vi < mesh.Vertices.Count) ? vi : -1;
        }

        /// <summary>コーナーが参照するスロット番号。無効なら -1。</summary>
        private static int SlotOf(MeshObject mesh, FaceCorner fc, out int vertexIndex)
        {
            vertexIndex = VertexOf(mesh, fc);
            if (vertexIndex < 0) return -1;

            var face = mesh.Faces[fc.Face];
            if (fc.Corner >= face.UVIndices.Count) return -1;

            int slot = face.UVIndices[fc.Corner];
            var vertex = mesh.Vertices[vertexIndex];
            return (slot >= 0 && slot < vertex.Normals.Count) ? slot : -1;
        }

        /// <summary>コーナーの現在の法線を読む。読めない場合は false。</summary>
        private static bool TryReadNormal(MeshObject mesh, FaceCorner fc, out Vector3 normal)
        {
            normal = Vector3.up;
            int slot = SlotOf(mesh, fc, out int vi);
            if (slot < 0) return false;

            normal = mesh.Vertices[vi].Normals[slot];
            return true;
        }

        /// <summary>コーナーの法線を書く。スロット数は変えない。</summary>
        private static void WriteNormal(MeshObject mesh, FaceCorner fc, Vector3 normal)
        {
            int slot = SlotOf(mesh, fc, out int vi);
            if (slot < 0) return;

            if (normal.sqrMagnitude < 1e-12f) return;
            mesh.Vertices[vi].Normals[slot] = normal.normalized;

            var face = mesh.Faces[fc.Face];
            while (face.NormalIndices.Count <= fc.Corner) face.NormalIndices.Add(0);
            face.NormalIndices[fc.Corner] = slot;
        }

        /// <summary>対象コーナーが参照する頂点の重心。</summary>
        public static Vector3 CenterOf(MeshObject mesh, IReadOnlyList<FaceCorner> corners)
        {
            if (mesh == null || corners == null || corners.Count == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            int n = 0;
            var seen = new HashSet<int>();

            foreach (var fc in corners)
            {
                int vi = VertexOf(mesh, fc);
                if (vi < 0 || !seen.Add(vi)) continue;
                sum += mesh.Vertices[vi].Position;
                n++;
            }

            return n > 0 ? sum / n : Vector3.zero;
        }

        private static int Finish(MeshObject mesh, int changed, string context)
        {
            if (changed > 0)
            {
                NormalSmoothingOps.NormalizeSlotCounts(mesh);
                NormalSmoothingOps.ValidateSlotInvariant(mesh, context);
            }
            return changed;
        }

        // ================================================================
        // A. 再計算
        // ================================================================

        /// <summary>
        /// スムージング角で法線を作り直す。メッシュ全体が対象（NormalSmoothingOps に委譲）。
        /// ハードエッジ分だけスロットが増えるため、呼び出し側は展開頂点数の変化を前提にすること。
        /// </summary>
        public static void RecalcByAngle(
            MeshObject mesh, float angleDeg, NormalWeightMode weightMode)
        {
            if (mesh == null) return;
            NormalSmoothingOps.ApplyFacetSmoothing(
                mesh, angleDeg, false, mesh.Name, weightMode);
        }

        /// <summary>
        /// 対象コーナーの法線を、その面の面法線にする（フラット化 / Set to Face 相当）。
        /// スロットを共有する別の面が居る場合は最後に書いたコーナーの値が残るため、
        /// 面ごとに完全に分けたい場合は Break を先に実行すること。
        /// </summary>
        public static int SetFromFaces(MeshObject mesh, IReadOnlyList<FaceCorner> corners)
        {
            if (mesh == null || corners == null) return 0;

            int changed = 0;
            foreach (var fc in corners)
            {
                if (fc.Face < 0 || fc.Face >= mesh.Faces.Count) continue;
                Vector3 fn = NormalSmoothingOps.CalculateFaceNormalNewell(mesh, mesh.Faces[fc.Face]);
                WriteNormal(mesh, fc, fn);
                changed++;
            }

            return Finish(mesh, changed, mesh.Name);
        }

        /// <summary>
        /// 対象コーナーの「面法線」だけを頂点ごとに重み付き平均し、その 1 本を
        /// その頂点の対象コーナー全部へ書く。スロット数は変えない。
        ///
        /// 【Unify との違い】
        ///   Unify は平均の入力が「現在のスロット法線」なので、対象コーナーが
        ///   同じスロットを共有していると全部が同じ値を読み、平均しても変わらない。
        ///   本操作は入力を常に面法線にするため、選択した面だけを使った頂点法線が得られる。
        ///
        /// 【共有スロットの扱い】
        ///   書き込み先は既存スロット（WriteNormal のみを使い GetOrAddUVNormal は通さない）。
        ///   よって対象外の面が同じスロットを参照していれば、その面も同じ値を見ることになる。
        ///   面ごとに分けたい場合は Break を先に実行すること。
        /// </summary>
        public static int AverageFromFaces(
            MeshObject mesh, IReadOnlyList<FaceCorner> corners, NormalWeightMode weightMode)
        {
            if (mesh == null || corners == null) return 0;

            var byVertex = new Dictionary<int, List<FaceCorner>>();
            foreach (var fc in corners)
            {
                int vi = VertexOf(mesh, fc);
                if (vi < 0) continue;
                if (!byVertex.TryGetValue(vi, out var list))
                {
                    list = new List<FaceCorner>();
                    byVertex[vi] = list;
                }
                list.Add(fc);
            }

            int changed = 0;
            foreach (var kvp in byVertex)
            {
                Vector3 sum = Vector3.zero;
                foreach (var fc in kvp.Value)
                {
                    var face = mesh.Faces[fc.Face];
                    Vector3 fn = NormalSmoothingOps.CalculateFaceNormalNewell(mesh, face);
                    float   w  = NormalSmoothingOps.CornerWeight(mesh, face, fc.Corner, weightMode);
                    sum += fn * w;
                }

                if (sum.sqrMagnitude < 1e-12f) continue;
                Vector3 averaged = sum.normalized;

                foreach (var fc in kvp.Value)
                {
                    WriteNormal(mesh, fc, averaged);
                    changed++;
                }
            }

            return Finish(mesh, changed, mesh.Name);
        }

        // ================================================================
        // B. スロット操作
        // ================================================================

        /// <summary>
        /// 統合（Unify）。対象コーナーを頂点ごとにまとめ、重み付き平均法線を
        /// その頂点の対象スロット全部に書く。スロット数は変えない。
        /// 平均の元は「現在のスロット法線」なので、それ以前の編集結果を引き継ぐ。
        /// 読めないコーナーだけ面法線で代用する。
        /// </summary>
        public static int Unify(
            MeshObject mesh, IReadOnlyList<FaceCorner> corners, NormalWeightMode weightMode)
        {
            if (mesh == null || corners == null) return 0;

            var byVertex = new Dictionary<int, List<FaceCorner>>();
            foreach (var fc in corners)
            {
                int vi = VertexOf(mesh, fc);
                if (vi < 0) continue;
                if (!byVertex.TryGetValue(vi, out var list))
                {
                    list = new List<FaceCorner>();
                    byVertex[vi] = list;
                }
                list.Add(fc);
            }

            int changed = 0;
            foreach (var kvp in byVertex)
            {
                Vector3 sum = Vector3.zero;
                foreach (var fc in kvp.Value)
                {
                    var face = mesh.Faces[fc.Face];
                    Vector3 src = TryReadNormal(mesh, fc, out var cur)
                        ? cur
                        : NormalSmoothingOps.CalculateFaceNormalNewell(mesh, face);
                    float w = NormalSmoothingOps.CornerWeight(mesh, face, fc.Corner, weightMode);
                    sum += src * w;
                }

                if (sum.sqrMagnitude < 1e-12f) continue;
                Vector3 unified = sum.normalized;

                foreach (var fc in kvp.Value)
                {
                    WriteNormal(mesh, fc, unified);
                    changed++;
                }
            }

            return Finish(mesh, changed, mesh.Name);
        }

        /// <summary>
        /// 分離（Break）。対象コーナーの法線を面法線に戻し、同一 UV でも面ごとに
        /// 別スロットへ分ける。GetOrAddUVNormal を通すためスロットが増える。
        /// </summary>
        public static int Break(MeshObject mesh, IReadOnlyList<FaceCorner> corners)
        {
            if (mesh == null || corners == null) return 0;

            int changed = 0;
            foreach (var fc in corners)
            {
                int vi = VertexOf(mesh, fc);
                if (vi < 0) continue;

                var face = mesh.Faces[fc.Face];
                if (fc.Corner >= face.UVIndices.Count) continue;

                var vertex = mesh.Vertices[vi];

                int oldSlot = face.UVIndices[fc.Corner];
                Vector2 uv = (oldSlot >= 0 && oldSlot < vertex.UVs.Count)
                    ? vertex.UVs[oldSlot]
                    : Vector2.zero;

                Vector3 fn = NormalSmoothingOps.CalculateFaceNormalNewell(mesh, face);

                int slot = vertex.GetOrAddUVNormal(uv, fn, SlotTolerance);

                face.UVIndices[fc.Corner] = slot;
                while (face.NormalIndices.Count <= fc.Corner) face.NormalIndices.Add(0);
                face.NormalIndices[fc.Corner] = slot;
                changed++;
            }

            return Finish(mesh, changed, mesh.Name);
        }

        // ================================================================
        // C. 平均・平滑
        // ================================================================

        /// <summary>
        /// 対象コーナーの法線を 1 方向（全部の平均）に揃える。
        /// 凹凸のあるシェーディングを平らにするのに使う。
        /// </summary>
        public static int AverageAll(MeshObject mesh, IReadOnlyList<FaceCorner> corners)
        {
            if (mesh == null || corners == null) return 0;

            Vector3 sum = Vector3.zero;
            foreach (var fc in corners)
            {
                if (TryReadNormal(mesh, fc, out var n)) sum += n;
            }
            if (sum.sqrMagnitude < 1e-12f) return 0;

            Vector3 avg = sum.normalized;

            int changed = 0;
            foreach (var fc in corners)
            {
                WriteNormal(mesh, fc, avg);
                changed++;
            }

            return Finish(mesh, changed, mesh.Name);
        }

        /// <summary>
        /// 平滑化。辺で繋がった隣接頂点の法線平均と、元の法線を strength で補間する。
        /// 元の法線は全コーナー分を先に読み出してから使うので、走査順に依存しない。
        /// </summary>
        public static int Smooth(
            MeshObject mesh, IReadOnlyList<FaceCorner> corners, float strength)
        {
            if (mesh == null || corners == null || corners.Count == 0) return 0;

            strength = Mathf.Clamp01(strength);
            if (strength <= 0f) return 0;

            // 頂点ごとの現在法線（その頂点のスロット法線の平均）
            var vertexNormal = new Dictionary<int, Vector3>();
            var vertexCount  = new Dictionary<int, int>();

            for (int vi = 0; vi < mesh.Vertices.Count; vi++)
            {
                var vertex = mesh.Vertices[vi];
                if (vertex.Normals.Count == 0) continue;

                Vector3 sum = Vector3.zero;
                foreach (var n in vertex.Normals) sum += n;
                if (sum.sqrMagnitude < 1e-12f) continue;

                vertexNormal[vi] = sum.normalized;
                vertexCount[vi]  = 0;
            }

            // 辺で繋がった隣接頂点の法線を合算
            var neighborSum = new Dictionary<int, Vector3>();
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                if (face == null || face.VertexCount < 3) continue;

                int n = face.VertexCount;
                for (int j = 0; j < n; j++)
                {
                    int a = face.VertexIndices[j];
                    int b = face.VertexIndices[(j + 1) % n];
                    if (a == b) continue;
                    if (!vertexNormal.ContainsKey(a) || !vertexNormal.ContainsKey(b)) continue;

                    if (!neighborSum.ContainsKey(a)) neighborSum[a] = Vector3.zero;
                    if (!neighborSum.ContainsKey(b)) neighborSum[b] = Vector3.zero;

                    neighborSum[a] += vertexNormal[b];
                    neighborSum[b] += vertexNormal[a];
                }
            }

            int changed = 0;
            foreach (var fc in corners)
            {
                int vi = VertexOf(mesh, fc);
                if (vi < 0) continue;
                if (!TryReadNormal(mesh, fc, out var original)) continue;
                if (!neighborSum.TryGetValue(vi, out var nsum)) continue;
                if (nsum.sqrMagnitude < 1e-12f) continue;

                Vector3 smoothed = Vector3.Slerp(original.normalized, nsum.normalized, strength);
                WriteNormal(mesh, fc, smoothed);
                changed++;
            }

            return Finish(mesh, changed, mesh.Name);
        }

        // ================================================================
        // D. 方向指定
        // ================================================================

        /// <summary>球状化。中心から頂点へ向かう方向を法線にする。</summary>
        public static int Sphereize(
            MeshObject mesh, IReadOnlyList<FaceCorner> corners, Vector3 center)
        {
            if (mesh == null || corners == null) return 0;

            int changed = 0;
            foreach (var fc in corners)
            {
                int vi = VertexOf(mesh, fc);
                if (vi < 0) continue;

                Vector3 dir = mesh.Vertices[vi].Position - center;
                if (dir.sqrMagnitude < 1e-12f) continue;

                WriteNormal(mesh, fc, dir.normalized);
                changed++;
            }

            return Finish(mesh, changed, mesh.Name);
        }

        /// <summary>
        /// ターゲット指向。頂点からターゲットへ向かう方向を法線にする。
        /// alignVectors が true なら、対象の重心からターゲットへの 1 本のベクトルを全体に適用する。
        /// </summary>
        public static int PointToTarget(
            MeshObject mesh, IReadOnlyList<FaceCorner> corners, Vector3 target, bool alignVectors)
        {
            if (mesh == null || corners == null) return 0;

            if (alignVectors)
            {
                Vector3 dir = target - CenterOf(mesh, corners);
                if (dir.sqrMagnitude < 1e-12f) return 0;
                return SetDirection(mesh, corners, dir.normalized);
            }

            int changed = 0;
            foreach (var fc in corners)
            {
                int vi = VertexOf(mesh, fc);
                if (vi < 0) continue;

                Vector3 dir = target - mesh.Vertices[vi].Position;
                if (dir.sqrMagnitude < 1e-12f) continue;

                WriteNormal(mesh, fc, dir.normalized);
                changed++;
            }

            return Finish(mesh, changed, mesh.Name);
        }

        /// <summary>対象コーナーの法線を指定方向で固定する（軸整列に使う）。</summary>
        public static int SetDirection(
            MeshObject mesh, IReadOnlyList<FaceCorner> corners, Vector3 direction)
        {
            if (mesh == null || corners == null) return 0;
            if (direction.sqrMagnitude < 1e-12f) return 0;

            Vector3 dir = direction.normalized;

            int changed = 0;
            foreach (var fc in corners)
            {
                WriteNormal(mesh, fc, dir);
                changed++;
            }

            return Finish(mesh, changed, mesh.Name);
        }

        /// <summary>
        /// 指定軸の成分をゼロにして正規化する（Flatten on Axis）。
        /// 残りが 0 になるコーナーは変更しない。
        /// </summary>
        public static int FlattenOnAxis(
            MeshObject mesh, IReadOnlyList<FaceCorner> corners, int axis)
        {
            if (mesh == null || corners == null) return 0;
            if (axis < 0 || axis > 2) return 0;

            int changed = 0;
            foreach (var fc in corners)
            {
                if (!TryReadNormal(mesh, fc, out var n)) continue;

                n[axis] = 0f;
                if (n.sqrMagnitude < 1e-12f) continue;

                WriteNormal(mesh, fc, n.normalized);
                changed++;
            }

            return Finish(mesh, changed, mesh.Name);
        }

        /// <summary>法線を反転する。</summary>
        public static int Flip(MeshObject mesh, IReadOnlyList<FaceCorner> corners)
        {
            if (mesh == null || corners == null) return 0;

            int changed = 0;
            foreach (var fc in corners)
            {
                if (!TryReadNormal(mesh, fc, out var n)) continue;

                WriteNormal(mesh, fc, -n);
                changed++;
            }

            return Finish(mesh, changed, mesh.Name);
        }
    }
}
