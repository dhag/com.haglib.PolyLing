// FaceGroupSolidifier.cs
// 薄い面群に厚みを付ける（サンドイッチ化）ための純ロジック。
//
// 面群のコピーを2枚作る:
//   表シェル = 元の巻き順のまま、頂点法線方向へ +厚み/2
//   裏シェル = 巻き順を反転、頂点法線方向へ -厚み/2
// この2枚の「孤立エッジ」（対象面同士で共有されない辺）を側面で接続し、
// 閉じた立体を1つの MeshObject として返す。
//
// オリジナルの面には一切触れない。オリジナルは生成された立体の内側に残り、
// これが中央層になる。
//
// ベベル（角処理）は Profile2DExtrudeMeshGenerator と同じ規約:
//   Segments 0 = 無効 / 1 = 平らな面取り / 2以上 = 四分円ラウンド
//   EdgeSize は「面内へのインセット量」と「法線方向の深さ」の両方に使う
//   EdgeInward はラウンドの曲率方向（凹／凸）を入れ替える。位置は変わらない
// シェルの境界リング頂点を面内へインセットするため、立体の外形は変わらない。
//
// 孤立エッジ判定は「対象面のみ」を数える。TopologyCache.GetBoundaryEdges() は
// メッシュ全面から構築されるため使用しない。
//
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Selection;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 面群を厚み付けして閉じた立体の MeshObject を生成する。
    /// </summary>
    public static class FaceGroupSolidifier
    {
        // ================================================================
        // パラメータ
        // ================================================================

        public struct Params
        {
            /// <summary>総厚み。各シェルは ±Thickness/2 移動する</summary>
            public float Thickness;

            /// <summary>表側エッジ分割数（0=無効 / 1=面取り / 2以上=ラウンド）</summary>
            public int SegmentsFront;

            /// <summary>裏側エッジ分割数（0=無効 / 1=面取り / 2以上=ラウンド）</summary>
            public int SegmentsBack;

            /// <summary>表側エッジサイズ（面内インセット量＝法線方向の深さ）</summary>
            public float EdgeSizeFront;

            /// <summary>裏側エッジサイズ</summary>
            public float EdgeSizeBack;

            /// <summary>ラウンドの曲率方向を入れ替える</summary>
            public bool EdgeInward;

            public static Params Default => new Params
            {
                Thickness     = 0.1f,
                SegmentsFront = 0,
                SegmentsBack  = 0,
                EdgeSizeFront = 0.1f,
                EdgeSizeBack  = 0.1f,
                EdgeInward    = false,
            };
        }

        /// <summary>生成結果。</summary>
        public class Result
        {
            /// <summary>生成された立体。失敗時 null。</summary>
            public MeshObject Mesh;

            /// <summary>厚み付けの対象になった面数。</summary>
            public int TargetFaceCount;

            /// <summary>検出された孤立エッジ数。</summary>
            public int BoundaryEdgeCount;

            /// <summary>生成された側面数（ベベル帯を含む）。</summary>
            public int SideFaceCount;

            /// <summary>失敗理由（成功時 null）。</summary>
            public string Error;

            public bool Ok => Mesh != null && string.IsNullOrEmpty(Error);
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// 対象面群から厚み付け立体を生成する。source は変更しない。
        /// </summary>
        public static Result Build(
            MeshObject source,
            ICollection<int> faceIndices,
            Params p,
            string meshName)
        {
            var result = new Result();

            if (source == null)
            {
                result.Error = "source is null";
                return result;
            }
            if (faceIndices == null || faceIndices.Count == 0)
            {
                result.Error = "no target faces";
                return result;
            }
            if (p.Thickness <= 0f)
            {
                result.Error = "thickness must be greater than 0";
                return result;
            }

            // ── 1) 対象面の確定（2頂点 Face＝補助線は対象外） ──────────
            var targets = new List<int>();
            foreach (int fi in faceIndices)
            {
                if (fi < 0 || fi >= source.Faces.Count) continue;
                var f = source.Faces[fi];
                if (f == null || f.VertexCount < 3) continue;
                targets.Add(fi);
            }
            if (targets.Count == 0)
            {
                result.Error = "no face with 3 or more vertices";
                return result;
            }

            // ── 2) 対象頂点の収集（元索引 → ローカル索引） ─────────────
            var srcToLocal = new Dictionary<int, int>();
            var localToSrc = new List<int>();
            foreach (int fi in targets)
            {
                var f = source.Faces[fi];
                for (int i = 0; i < f.VertexIndices.Count; i++)
                {
                    int vi = f.VertexIndices[i];
                    if (vi < 0 || vi >= source.Vertices.Count) continue;
                    if (srcToLocal.ContainsKey(vi)) continue;
                    srcToLocal[vi] = localToSrc.Count;
                    localToSrc.Add(vi);
                }
            }
            if (localToSrc.Count == 0)
            {
                result.Error = "no valid vertex";
                return result;
            }

            int localCount = localToSrc.Count;

            // ── 3) 頂点法線＝その頂点を含む対象面の面法線の平均 ────────
            var normalSum   = new Vector3[localCount];
            var firstNormal = new Vector3[localCount];
            var hasFirst    = new bool[localCount];

            foreach (int fi in targets)
            {
                var f = source.Faces[fi];
                Vector3 n = CalcFaceNormal(source, f);
                for (int i = 0; i < f.VertexIndices.Count; i++)
                {
                    if (!srcToLocal.TryGetValue(f.VertexIndices[i], out int li)) continue;
                    normalSum[li] += n;
                    if (!hasFirst[li]) { firstNormal[li] = n; hasFirst[li] = true; }
                }
            }

            var vertNormal = new Vector3[localCount];
            for (int li = 0; li < localCount; li++)
            {
                if (normalSum[li].sqrMagnitude > 1e-10f)
                    vertNormal[li] = normalSum[li].normalized;
                else if (hasFirst[li])
                    vertNormal[li] = firstNormal[li];   // 表裏が打ち消し合った場合
                else
                    vertNormal[li] = Vector3.up;
            }

            // ── 4) 孤立エッジの抽出（対象面のみで数える） ──────────────
            var edgeCount = new Dictionary<VertexPair, int>();
            var edgeDir   = new Dictionary<VertexPair, EdgeDir>();

            foreach (int fi in targets)
            {
                var f = source.Faces[fi];
                int n = f.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    int a = f.VertexIndices[i];
                    int b = f.VertexIndices[(i + 1) % n];
                    if (a == b) continue;
                    if (!srcToLocal.ContainsKey(a) || !srcToLocal.ContainsKey(b)) continue;

                    var key = new VertexPair(a, b);
                    edgeCount.TryGetValue(key, out int c);
                    edgeCount[key] = c + 1;
                    if (c == 0)
                        edgeDir[key] = new EdgeDir { A = a, B = b, MaterialIndex = f.MaterialIndex };
                }
            }

            var boundary = new List<EdgeDir>();
            foreach (var kv in edgeCount)
            {
                if (kv.Value != 1) continue;
                boundary.Add(edgeDir[kv.Key]);
            }
            result.BoundaryEdgeCount = boundary.Count;

            // ── 5) ベベル量の確定 ──────────────────────────────────────
            float half = p.Thickness * 0.5f;

            // EdgeSize が厚みの半分以上だと (half - EdgeSize) が負になり立体が裏返る。
            // 外形の自己交差は保護しないが、この反転だけは抑える。
            float maxEdge = half * 0.999f;
            float edgeF = Mathf.Clamp(p.EdgeSizeFront, 0f, maxEdge);
            float edgeB = Mathf.Clamp(p.EdgeSizeBack,  0f, maxEdge);

            int segF = (p.SegmentsFront > 0 && edgeF > 1e-6f) ? p.SegmentsFront : 0;
            int segB = (p.SegmentsBack  > 0 && edgeB > 1e-6f) ? p.SegmentsBack  : 0;
            if (segF == 0) edgeF = 0f;
            if (segB == 0) edgeB = 0f;

            bool frontConcave = !p.EdgeInward;
            bool backConcave  =  p.EdgeInward;

            // ── 6) 境界頂点のマイター方向（面内・内向き） ──────────────
            var miter = ComputeMiterDirections(source, srcToLocal, localToSrc, vertNormal, boundary, out bool[] isBoundary);

            // ── 7) 出力メッシュ ────────────────────────────────────────
            var mesh = new MeshObject(string.IsNullOrEmpty(meshName) ? "Solidify" : meshName);

            // 元メッシュの Transform をコピーする。新規オブジェクトとして追加したときに
            // 元メッシュと同じ位置に出るようにするため。
            mesh.BoneTransform        = new BoneTransform(source.BoneTransform);
            mesh.ParentIndex          = source.ParentIndex;
            mesh.HierarchyParentIndex = source.HierarchyParentIndex;

            var frontIdx = new int[localCount];
            var backIdx  = new int[localCount];

            for (int li = 0; li < localCount; li++)
            {
                var sv = source.Vertices[localToSrc[li]];
                Vector3 n = vertNormal[li];
                Vector3 m = miter[li];   // 内部頂点は Vector3.zero

                Vector3 fPos = sv.Position + m * edgeF + n * half;
                Vector3 bPos = sv.Position + m * edgeB - n * half;

                frontIdx[li] = mesh.AddVertex(CloneShellVertex(sv, fPos, false,  n));
                backIdx[li]  = mesh.AddVertex(CloneShellVertex(sv, bPos, true,  -n));
            }

            // ── 8) シェル面（表＝元の巻き順、裏＝逆順） ────────────────
            foreach (int fi in targets)
            {
                var f = source.Faces[fi];
                int n = f.VertexCount;

                var lidx = new int[n];
                var uvs  = new int[n];
                var nrms = new int[n];
                bool ok = true;

                for (int i = 0; i < n; i++)
                {
                    if (!srcToLocal.TryGetValue(f.VertexIndices[i], out int li)) { ok = false; break; }
                    lidx[i] = li;
                    // UVIndices / NormalIndices は Vertex.UVs / Vertex.Normals への
                    // スロット索引。シェル頂点はスロット構成をそのまま複製しているため
                    // 元 Face の値をそのまま使える。
                    uvs[i]  = (i < f.UVIndices.Count)     ? f.UVIndices[i]     : 0;
                    nrms[i] = (i < f.NormalIndices.Count) ? f.NormalIndices[i] : 0;
                }
                if (!ok) continue;

                var front = new Face { MaterialIndex = f.MaterialIndex };
                for (int i = 0; i < n; i++)
                {
                    front.VertexIndices.Add(frontIdx[lidx[i]]);
                    front.UVIndices.Add(uvs[i]);
                    front.NormalIndices.Add(nrms[i]);
                }
                mesh.AddFace(front);

                var back = new Face { MaterialIndex = f.MaterialIndex };
                for (int i = n - 1; i >= 0; i--)
                {
                    back.VertexIndices.Add(backIdx[lidx[i]]);
                    back.UVIndices.Add(uvs[i]);
                    back.NormalIndices.Add(nrms[i]);
                }
                mesh.AddFace(back);
            }

            // ── 9) 境界頂点ごとの縦列（表シェル → … → 裏シェル） ───────
            // 隣り合う孤立エッジで同じ縦列を共有するので、側面は必ず閉じる。
            var columns = new Dictionary<int, int[]>();

            for (int li = 0; li < localCount; li++)
            {
                if (!isBoundary[li]) continue;

                var sv = source.Vertices[localToSrc[li]];
                Vector3 basePos = sv.Position;
                Vector3 n = vertNormal[li];
                Vector3 m = miter[li];

                var col = new List<int>(2 + segF + segB);
                col.Add(frontIdx[li]);

                // 表側ベベル帯（t=0 はシェルなので t=1/segF から）
                for (int k = 1; k <= segF; k++)
                {
                    float t = (float)k / segF;
                    GetBevelLerp(t, frontConcave, out float xy, out float z);
                    Vector3 pos = basePos + m * edgeF * (1f - xy) + n * (half - edgeF * z);
                    col.Add(mesh.AddVertex(CloneBandVertex(sv, pos, n)));
                }

                // 裏側ベベル帯（t=1 の縁から t=1/segB へ下る）
                for (int k = segB; k >= 1; k--)
                {
                    float t = (float)k / segB;
                    GetBevelLerp(t, backConcave, out float xy, out float z);
                    Vector3 pos = basePos + m * edgeB * (1f - xy) - n * (half - edgeB * z);
                    col.Add(mesh.AddVertex(CloneBandVertex(sv, pos, -n)));
                }

                col.Add(backIdx[li]);
                columns[li] = col.ToArray();
            }

            // ── 10) 側面（縦列間を段ごとに quad で張る） ────────────────
            // 表シェル面が a→b を辿る辺なので、側面は逆向きの b→a から下りる。
            foreach (var e in boundary)
            {
                int la = srcToLocal[e.A];
                int lb = srcToLocal[e.B];

                if (!columns.TryGetValue(la, out int[] colA)) continue;
                if (!columns.TryGetValue(lb, out int[] colB)) continue;
                if (colA.Length != colB.Length) continue;

                for (int k = 0; k + 1 < colA.Length; k++)
                {
                    int q0 = colB[k];
                    int q1 = colA[k];
                    int q2 = colA[k + 1];
                    int q3 = colB[k + 1];

                    if (AddSideQuad(mesh, q0, q1, q2, q3, e.MaterialIndex))
                        result.SideFaceCount++;
                }
            }

            result.Mesh = mesh;
            result.TargetFaceCount = targets.Count;
            return result;
        }

        // ================================================================
        // 内部
        // ================================================================

        private struct EdgeDir
        {
            public int A;
            public int B;
            public int MaterialIndex;
        }

        /// <summary>
        /// ベベルの補間係数。Profile2DExtrudeMeshGenerator.GenerateEdgeFaces と同式。
        /// xy = 面内方向の進み、z = 法線方向の進み（どちらも 0→1）。
        /// </summary>
        private static void GetBevelLerp(float t, bool concave, out float xy, out float z)
        {
            float angle = t * Mathf.PI * 0.5f;
            if (concave)
            {
                xy = Mathf.Sin(angle);
                z  = 1f - Mathf.Cos(angle);
            }
            else
            {
                xy = 1f - Mathf.Cos(angle);
                z  = Mathf.Sin(angle);
            }
        }

        /// <summary>
        /// 境界頂点の面内内向き方向（長さはマイター補正込み）。内部頂点は Vector3.zero。
        /// </summary>
        private static Vector3[] ComputeMiterDirections(
            MeshObject source,
            Dictionary<int, int> srcToLocal,
            List<int> localToSrc,
            Vector3[] vertNormal,
            List<EdgeDir> boundary,
            out bool[] isBoundary)
        {
            int localCount = vertNormal.Length;
            var miter = new Vector3[localCount];
            isBoundary = new bool[localCount];

            // 各境界頂点の 出ていく辺 / 入ってくる辺
            var outTo   = new Dictionary<int, List<int>>();  // li -> 相手の元頂点索引
            var inFrom  = new Dictionary<int, List<int>>();

            foreach (var e in boundary)
            {
                int la = srcToLocal[e.A];
                int lb = srcToLocal[e.B];
                isBoundary[la] = true;
                isBoundary[lb] = true;

                if (!outTo.TryGetValue(la, out var ol)) { ol = new List<int>(); outTo[la] = ol; }
                ol.Add(e.B);
                if (!inFrom.TryGetValue(lb, out var il)) { il = new List<int>(); inFrom[lb] = il; }
                il.Add(e.A);
            }

            for (int li = 0; li < localCount; li++)
            {
                if (!isBoundary[li]) continue;

                Vector3 n = vertNormal[li];
                Vector3 vp = source.Vertices[localToSrc[li]].Position;

                outTo.TryGetValue(li, out var outs);
                inFrom.TryGetValue(li, out var ins);

                bool manifold = outs != null && ins != null && outs.Count == 1 && ins.Count == 1;

                if (manifold)
                {
                    Vector3 dOut = TangentDir(vp, source.Vertices[outs[0]].Position, n);
                    Vector3 dIn  = TangentDir(source.Vertices[ins[0]].Position, vp, n);

                    Vector3 inwardOut = Vector3.Cross(n, dOut).normalized;
                    Vector3 inwardIn  = Vector3.Cross(n, dIn).normalized;

                    Vector3 bis = inwardIn + inwardOut;
                    if (bis.sqrMagnitude < 1e-8f)
                    {
                        // 折り返し（180度）: マイターが定義できないので出ていく辺基準
                        miter[li] = inwardOut;
                    }
                    else
                    {
                        Vector3 unit = bis.normalized;
                        float cos = Vector3.Dot(unit, inwardOut);
                        // 鋭角でオフセットが発散しないよう 5 倍で打ち止め
                        float scale = 1f / Mathf.Max(cos, 0.2f);
                        miter[li] = unit * scale;
                    }
                }
                else
                {
                    // 非多様体な境界（1頂点に複数の孤立エッジ）はマイターせず平均で代用
                    Vector3 sum = Vector3.zero;
                    if (outs != null)
                        foreach (int b in outs)
                            sum += Vector3.Cross(n, TangentDir(vp, source.Vertices[b].Position, n));
                    if (ins != null)
                        foreach (int a in ins)
                            sum += Vector3.Cross(n, TangentDir(source.Vertices[a].Position, vp, n));

                    miter[li] = sum.sqrMagnitude > 1e-10f ? sum.normalized : Vector3.zero;
                }
            }

            return miter;
        }

        /// <summary>from → to を法線に垂直な接平面へ投影して正規化。</summary>
        private static Vector3 TangentDir(Vector3 from, Vector3 to, Vector3 normal)
        {
            Vector3 d = Vector3.ProjectOnPlane(to - from, normal);
            if (d.sqrMagnitude < 1e-12f) return Vector3.zero;
            return d.normalized;
        }

        /// <summary>
        /// 側面 quad を追加する。縮退している場合は追加せず false を返す。
        /// </summary>
        private static bool AddSideQuad(MeshObject mesh, int q0, int q1, int q2, int q3, int materialIndex)
        {
            if (q0 == q1 || q1 == q2 || q2 == q3 || q3 == q0) return false;

            Vector3 n = NormalHelper.CalculateFaceNormal(
                mesh.Vertices[q0].Position,
                mesh.Vertices[q1].Position,
                mesh.Vertices[q2].Position);

            var face = new Face { MaterialIndex = materialIndex };
            face.VertexIndices.AddRange(new[] { q0, q1, q2, q3 });

            // 側面はシェルとは別の法線が必要なのでスロットを追加して参照する。
            // 追加は末尾追加なのでシェル面の既存 NormalIndices は壊れない。
            face.NormalIndices.Add(mesh.Vertices[q0].GetOrAddNormal(n));
            face.NormalIndices.Add(mesh.Vertices[q1].GetOrAddNormal(n));
            face.NormalIndices.Add(mesh.Vertices[q2].GetOrAddNormal(n));
            face.NormalIndices.Add(mesh.Vertices[q3].GetOrAddNormal(n));

            // 側面の UV はスロット0を参照する（専用 UV は持たせない）。
            face.UVIndices.AddRange(new[] { 0, 0, 0, 0 });

            mesh.AddFace(face);
            return true;
        }

        /// <summary>
        /// 面法線。既存ツールと同じ規約（先頭3頂点 + NormalHelper）で求める。
        /// </summary>
        private static Vector3 CalcFaceNormal(MeshObject mesh, Face face)
        {
            Vector3 p0 = mesh.Vertices[face.VertexIndices[0]].Position;
            Vector3 p1 = mesh.Vertices[face.VertexIndices[1]].Position;
            Vector3 p2 = mesh.Vertices[face.VertexIndices[2]].Position;
            return NormalHelper.CalculateFaceNormal(p0, p1, p2);
        }

        /// <summary>
        /// シェル用の頂点を作る。UV / 法線のスロット構成は元頂点と同じにする。
        /// </summary>
        private static Vertex CloneShellVertex(Vertex src, Vector3 position, bool flipNormals, Vector3 fallbackNormal)
        {
            var v = new Vertex(position);

            if (src.UVs.Count > 0)
                v.UVs.AddRange(src.UVs);
            else
                v.UVs.Add(Vector2.zero);   // スロット0を必ず用意する

            if (src.Normals.Count > 0)
            {
                for (int i = 0; i < src.Normals.Count; i++)
                    v.Normals.Add(flipNormals ? -src.Normals[i] : src.Normals[i]);
            }
            else
            {
                v.Normals.Add(fallbackNormal);
            }

            // BoneWeight は必ずコピーする。未設定だと GPU 側でメッシュ自身の
            // context 索引が使われ、周囲の頂点と別の行列で変換されて位置がずれる。
            v.BoneWeight       = src.BoneWeight;
            v.MirrorBoneWeight = src.MirrorBoneWeight;

            // Flags は引き継がない（Locked / Auxiliary を生成物へ持ち込まないため）。
            return v;
        }

        /// <summary>
        /// ベベル帯の中間頂点。UV / 法線はスロット0のみ持つ。
        /// </summary>
        private static Vertex CloneBandVertex(Vertex src, Vector3 position, Vector3 normal)
        {
            var v = new Vertex(position);
            v.UVs.Add(src.UVs.Count > 0 ? src.UVs[0] : Vector2.zero);
            v.Normals.Add(normal);
            v.BoneWeight       = src.BoneWeight;
            v.MirrorBoneWeight = src.MirrorBoneWeight;
            return v;
        }
    }
}
