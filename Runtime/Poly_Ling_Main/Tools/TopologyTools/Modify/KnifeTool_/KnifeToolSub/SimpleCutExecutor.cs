// Tools/TopologyTools/Modify/KnifeTool_/KnifeToolSub/SimpleCutExecutor.cs
// シンプルナイフ: 画面上の2点 P0,P1 を結ぶ直線で面を切る（半平面方式）。
//
//  - 各面の頂点を画面投影（IMGUI Y下）し、切断線 P0-P1 への符号付き距離 d を求める。
//    d = cross(dir, sp[v]-p0)、dir=p1-p0。d>=0 を + 側、d<0 を - 側（tie-break は +）。
//  - 辺で符号が変わる箇所が境界の切断点。凸面は必ず境界2点で交差。
//  - 切断点が既存頂点のごく近傍なら既存頂点を使う（新頂点を作らない）。それ以外は辺上に新頂点。
//    共有辺キーで dedupe（隣接面でウォータータイト）。頂点生成は「切断確定後」に遅延（孤立頂点を作らない）。
//  - 各切断点は線分 P0-P1 範囲内（t∈[0,1]）のもののみ採用。
//  - faceCulledMask[f]==true（背面/非表示）はスキップ。null で全面対象。
//  - triQuad=true で5角以上の面を三角形＋四角形へ再分解。
//  - 診断: culledWouldCut = カリング除外面のうち「本来2点で切れた面」数。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    public static class SimpleCutExecutor
    {
        private const float VertexSnapU = 1e-3f; // 辺端に近い交差は既存頂点にスナップ
        private const float SegTol = 1e-4f;       // 線分範囲判定の許容

        // 境界の切断点（頂点生成前の記述）。
        // ================================================================
        // 【禁止事項】GPU 由来の座標を扱うときの拗らせ
        // ================================================================
        // 以下は実際に発生させた失敗である。繰り返さないこと。
        //
        // 1. 調べずに CPU 側で独自計算しない。
        //    GPU が _worldPositionBuffer にワールド座標を出しているのに、
        //    同じ規則を CPU で書き直すと、規則が食い違ったときに表示だけがずれる。
        //    まず GPU の値を使う経路を探すこと。
        //    ワールド座標は ToolContext.GetVertexWorldPosition、
        //    クリップ空間 w は ToolContext.GetVertexClipW を経由する
        //    （実体は PlayerViewportManager.TryGetVertexWorld / TryGetVertexClipW）。
        //
        // 2.「今は呼ばれていないからできない」と決めつけない。
        //    呼び出し箇所が無いことは、呼び出しを足せない理由にならない。
        //    足せるかどうかを調べてから結論を出すこと。
        //
        // 3. カメラもモデルも動いていないのに読み戻しを毎フレーム呼ばない。
        //    WritebackTransformedVertices / GetWorldPositions は同期 GetData を伴う。
        //    ワールド座標が変わる契機（頂点移動・ボーン移動・再構築）でのみ更新し、
        //    ホバーのようにトポロジ・視点・頂点位置のいずれも変わらない操作では呼ばない。
        //
        // 4. スキンドメッシュに追加する頂点には BoneWeight が必須である。
        //    BoneWeight を持たない頂点は GPU 側でメッシュ自身の context 索引を使い
        //    （UnifiedBufferManager_Build.cs:356-362）、周囲の頂点と別の行列で
        //    変換されてその頂点だけ位置がずれる。
        // ================================================================

        private struct CutPoint
        {
            public float Pos;      // コーナーindex + 辺上比率（頂点なら整数）
            public bool IsVertex;  // true=既存頂点、false=辺上の新頂点
            public int A, B;       // 頂点: A=頂点index。辺: A,B=辺端頂点index
            public float U;        // 辺上比率（辺のとき・スクリーン空間の線形パラメータ）
            public float UGeom;    // 同上を 3D 空間の線形パラメータへ透視補正した値
            public int UVIndex, NormalIndex; // 頂点のとき、その corner の UV/法線 index
        }

        private const string nullName = "null";

        public static void Execute(ToolContext ctx, MeshObject mo, Vector2 p0, Vector2 p1, bool[] faceCulledMask, bool triQuad)
        {
            if (ctx == null || mo == null) return;
            if (ctx.WorldToScreenPos == null) return;

            Vector2 dir = p1 - p0;
            float dlen2 = Vector2.Dot(dir, dir);
            if (dlen2 < 1e-6f) return;

            int origVertexCount = mo.VertexCount;
            int origFaceCount   = mo.FaceCount;
            if (origFaceCount == 0) return;

            // Vertices[].Position はローカル座標。頂点単位の LocalToScreen が
            // 描画側と同一規則（スキンド頂点はボーンの SkinningMatrix ブレンド、
            // 非スキンド頂点はメッシュの WorldMatrix）で変換する。
            float h = ctx.PreviewRect.height;
            var sp = new Vector2[origVertexCount];
            for (int i = 0; i < origVertexCount; i++)
            {
                Vector2 s = ctx.LocalToScreen(i, mo.Vertices[i].Position);
                sp[i] = new Vector2(s.x, h - s.y);
            }

            // 【一時診断】クリック座標と投影済みメッシュ範囲を突き合わせる。原因特定後に削除する。
            if (origVertexCount > 0)
            {
                Vector2 mn = sp[0], mx = sp[0];
                for (int i = 1; i < sp.Length; i++) { mn = Vector2.Min(mn, sp[i]); mx = Vector2.Max(mx, sp[i]); }
                Matrix4x4 wm = ctx.ActiveWorldMatrix;
                var amc = ctx.ActiveMeshContext;
                string mcName = amc != null ? amc.Name : nullName;
                UnityEngine.Debug.Log(
                    $"[SimpleCut2] p0={p0} p1={p1} spMin={mn} spMax={mx} rect={ctx.PreviewRect}"
                    + $" wmT=({wm.m03},{wm.m13},{wm.m23}) mesh={mcName} verts={origVertexCount}");
            }

            MeshObjectSnapshot before = ctx.UndoController != null
                ? MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext)
                : null;

            var edgeCutVertex = new Dictionary<long, int>();
            int cutCount = 0;
            int culledSkip = 0, culledWouldCut = 0, twoCross = 0, hadCross = 0;

            var cps = new List<CutPoint>(4);

            for (int f = 0; f < origFaceCount; f++)
            {
                var face = mo.Faces[f];
                int n = face.VertexIndices.Count;
                if (n < 3) continue;

                bool culled = faceCulledMask != null && f < faceCulledMask.Length && faceCulledMask[f];

                cps.Clear();
                bool anyCross = DetectCutPoints(ctx, face, sp, origVertexCount, p0, dir, dlen2, cps);
                bool cuttable = cps.Count == 2;

                if (culled)
                {
                    culledSkip++;
                    if (cuttable) culledWouldCut++; // カリング除外だが本来は切れた面
                    continue;
                }

                if (anyCross) hadCross++;
                if (!cuttable) continue;
                twoCross++;

                if (SplitFaceAtTwoPoints(mo, f, cps[0], cps[1], edgeCutVertex, triQuad))
                    cutCount++;
            }

            // 今回増えた頂点を 1 つの部品として扱う。
            Poly_Ling.Ops.PartsIdOps.AssignNewVertices(mo, origVertexCount);

            UnityEngine.Debug.Log(
                $"[SimpleCut] faces={origFaceCount} mask={(faceCulledMask != null)} " +
                $"culledSkip={culledSkip} culledWouldCut={culledWouldCut} " +
                $"hadCross={hadCross} twoCross={twoCross} cut={cutCount}");

            if (cutCount == 0) return;

            ctx.SyncMesh?.Invoke();

            if (ctx.UndoController != null && before != null)
            {
                var after = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);
                ctx.UndoController.RecordMeshTopologyChange(before, after, "Knife Simple Cut");
            }
        }

        // 面の境界切断点（最大2）を検出（頂点は生成しない）。戻り値=交差が1つでもあったか。
        private static bool DetectCutPoints(
            ToolContext ctx, Face face, Vector2[] sp, int vcount, Vector2 p0, Vector2 dir, float dlen2, List<CutPoint> cps)
        {
            int n = face.VertexIndices.Count;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                int v = face.VertexIndices[i];
                if (v < 0 || v >= vcount) return false;
                Vector2 rp = sp[v] - p0;
                d[i] = dir.x * rp.y - dir.y * rp.x;
            }

            bool anyCross = false;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int sa = d[i] >= 0f ? 1 : -1;
                int sb = d[j] >= 0f ? 1 : -1;
                if (sa == sb) continue;
                anyCross = true;

                float denom = d[i] - d[j];
                float u = Mathf.Abs(denom) > 1e-12f ? d[i] / denom : 0.5f;
                u = Mathf.Clamp01(u);

                int a = face.VertexIndices[i];
                int b = face.VertexIndices[j];

                Vector2 cx = Vector2.Lerp(sp[a], sp[b], u);
                float t = Vector2.Dot(cx - p0, dir) / dlen2;
                if (t < -SegTol || t > 1f + SegTol) continue;

                CutPoint cp;
                if (u <= VertexSnapU)
                    cp = new CutPoint { Pos = i, IsVertex = true, A = a,
                                        UVIndex = face.UVIndices.Count > i ? face.UVIndices[i] : 0,
                                        NormalIndex = face.NormalIndices.Count > i ? face.NormalIndices[i] : 0 };
                else if (u >= 1f - VertexSnapU)
                    cp = new CutPoint { Pos = j, IsVertex = true, A = b,
                                        UVIndex = face.UVIndices.Count > j ? face.UVIndices[j] : 0,
                                        NormalIndex = face.NormalIndices.Count > j ? face.NormalIndices[j] : 0 };
                else
                    cp = new CutPoint
                    {
                        Pos = i + u, IsVertex = false, A = a, B = b,
                        U = u, UGeom = ScreenUToGeomT(ctx, a, b, u)
                    };

                bool dup = false;
                for (int k = 0; k < cps.Count; k++)
                    if (Mathf.Abs(cps[k].Pos - cp.Pos) < 1e-4f ||
                        (cps[k].IsVertex && cp.IsVertex && cps[k].A == cp.A)) { dup = true; break; }
                if (dup) continue;

                if (cps.Count < 2) cps.Add(cp);
                else { cps.Add(cp); break; } // 3点以上 → cuttable 扱いにしない
            }
            return anyCross;
        }

        /// <summary>
        /// スクリーン上の線形パラメータ u を 3D 上の線形パラメータ t に変換する。
        ///
        /// PlayerToolContext.WorldToScreenPos は clip.x/clip.w で透視除算を行うため、
        /// スクリーン上で等間隔でも 3D 上では等間隔にならない。両者の関係は
        ///     u = t*wB / ((1-t)*wA + t*wB)
        /// であり、t について解くと
        ///     t = u*wA / (wB + u*(wA - wB))
        /// となる。wA / wB は両端頂点のクリップ空間 w。
        ///
        /// w は GPU が計算したワールド座標をカメラ行列で投影して得る
        /// （PlayerViewportManager.TryGetVertexClipW）。CPU でスキニングを再計算しない。
        /// w が取得できない場合と正投影（wA == wB）では u をそのまま返す。
        /// </summary>
        public static float ScreenUToGeomT(ToolContext ctx, int a, int b, float u)
        {
            if (ctx?.GetVertexClipW == null) return u;

            var wa = ctx.GetVertexClipW(a);
            var wb = ctx.GetVertexClipW(b);
            if (!wa.HasValue || !wb.HasValue) return u;

            float denom = wb.Value + u * (wa.Value - wb.Value);
            if (Mathf.Abs(denom) < 1e-12f) return u;

            return Mathf.Clamp01(u * wa.Value / denom);
        }

        // 切断点の頂点index/UV/法線を解決（辺上は新頂点を生成 or 再利用）。
        private static void Resolve(MeshObject mo, Dictionary<long, int> dict, CutPoint cp,
                                    out int vi, out int uvi, out int nmi)
        {
            if (cp.IsVertex) { vi = cp.A; uvi = cp.UVIndex; nmi = cp.NormalIndex; }
            else { vi = GetOrCreateEdgeVertex(mo, dict, cp.A, cp.B, cp.UGeom); uvi = 0; nmi = 0; }
        }

        // 境界2点で面を分割（辺-辺 / 辺-頂点 / 頂点-頂点）。
        private static bool SplitFaceAtTwoPoints(
            MeshObject mo, int faceIdx, CutPoint c0, CutPoint c1, Dictionary<long, int> dict, bool triQuad)
        {
            var face = mo.Faces[faceIdx];
            var V = face.VertexIndices;
            var U = face.UVIndices;
            var N = face.NormalIndices;
            int n = V.Count;
            int mat = face.MaterialIndex;

            if (c0.Pos > c1.Pos) { var t = c0; c0 = c1; c1 = t; }
            float pos0 = c0.Pos, pos1 = c1.Pos;

            Resolve(mo, dict, c0, out int v0, out int uv0, out int nm0);
            Resolve(mo, dict, c1, out int v1, out int uv1, out int nm1);

            var aV = new List<int>(); var aU = new List<int>(); var aN = new List<int>();
            aV.Add(v0); aU.Add(uv0); aN.Add(nm0);
            for (int c = 0; c < n; c++)
                if (c > pos0 && c < pos1) { aV.Add(V[c]); aU.Add(U.Count > c ? U[c] : 0); aN.Add(N.Count > c ? N[c] : 0); }
            aV.Add(v1); aU.Add(uv1); aN.Add(nm1);

            var bV = new List<int>(); var bU = new List<int>(); var bN = new List<int>();
            bV.Add(v1); bU.Add(uv1); bN.Add(nm1);
            for (int c = 0; c < n; c++)
                if (c > pos1) { bV.Add(V[c]); bU.Add(U.Count > c ? U[c] : 0); bN.Add(N.Count > c ? N[c] : 0); }
            for (int c = 0; c < n; c++)
                if (c < pos0) { bV.Add(V[c]); bU.Add(U.Count > c ? U[c] : 0); bN.Add(N.Count > c ? N[c] : 0); }
            bV.Add(v0); bU.Add(uv0); bN.Add(nm0);

            if (aV.Count < 3 || bV.Count < 3) return false;

            var pieces = new List<Face>();
            EmitFace(pieces, aV, aU, aN, mat, triQuad);
            EmitFace(pieces, bV, bU, bN, mat, triQuad);

            mo.Faces[faceIdx] = pieces[0];
            for (int i = 1; i < pieces.Count; i++)
                mo.Faces.Add(pieces[i]);
            return true;
        }

        // 5角以上の面を扇状に四角形＋三角形へ分解（triQuad）。
        private static void EmitFace(List<Face> pieces, List<int> V, List<int> U, List<int> N, int mat, bool triQuad)
        {
            int m = V.Count;
            if (!triQuad || m <= 4)
            {
                pieces.Add(new Face
                {
                    VertexIndices = new List<int>(V),
                    UVIndices = new List<int>(U),
                    NormalIndices = new List<int>(N),
                    MaterialIndex = mat
                });
                return;
            }
            int s = 1;
            while (s + 2 <= m - 1) { AddSub(pieces, V, U, N, mat, 0, s, s + 1, s + 2); s += 2; }
            if (s < m - 1) AddSub(pieces, V, U, N, mat, 0, s, m - 1);
        }

        private static void AddSub(List<Face> pieces, List<int> V, List<int> U, List<int> N, int mat, params int[] corners)
        {
            var fv = new List<int>(corners.Length);
            var fu = new List<int>(corners.Length);
            var fn = new List<int>(corners.Length);
            for (int k = 0; k < corners.Length; k++)
            {
                int c = corners[k];
                fv.Add(V[c]); fu.Add(U[c]); fn.Add(N[c]);
            }
            pieces.Add(new Face { VertexIndices = fv, UVIndices = fu, NormalIndices = fn, MaterialIndex = mat });
        }

        private static long EdgeKey(int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        private static int GetOrCreateEdgeVertex(MeshObject mo, Dictionary<long, int> dict, int a, int b, float uFromA)
        {
            long key = EdgeKey(a, b);
            if (dict.TryGetValue(key, out int existing)) return existing;

            var va = mo.Vertices[a];
            var vb = mo.Vertices[b];
            float t = uFromA;

            var v = new Vertex(Vector3.Lerp(va.Position, vb.Position, t));
            // 新頂点に BoneWeight を与えないと、GPU 側で
            // UnifiedBufferManager_Build.cs:356-362 によりメッシュ自身の context 索引が使われ、
            // 周囲の頂点（ボーンの SkinningMatrix）と異なる行列で変換されて位置がずれる。
            v.BoneWeight = Poly_Ling.UI.SkinWeightOps.LerpNullable(va.BoneWeight, vb.BoneWeight, t);
            if (va.UVs.Count > 0 && vb.UVs.Count > 0)
                v.UVs.Add(Vector2.Lerp(va.UVs[0], vb.UVs[0], t));
            if (va.Normals.Count > 0 && vb.Normals.Count > 0)
                v.Normals.Add(Vector3.Lerp(va.Normals[0], vb.Normals[0], t).normalized);

            int idx = mo.VertexCount;
            mo.Vertices.Add(v);
            dict[key] = idx;
            return idx;
        }

        // ================================================================
        // デバッグ可視化: 切断線が交差する辺（線分）と交差点を収集（カリング反映）
        // ================================================================

        private static bool SegSeg(Vector2 p, Vector2 p2, Vector2 a, Vector2 b, out float t, out float u)
        {
            t = 0f; u = 0f;
            Vector2 r = p2 - p;
            Vector2 s = b - a;
            float rxs = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(rxs) < 1e-9f) return false;
            Vector2 qp = a - p;
            t = (qp.x * s.y - qp.y * s.x) / rxs;
            u = (qp.x * r.y - qp.y * r.x) / rxs;
            return true;
        }

        public static void CollectCrossedEdges(
            ToolContext ctx, MeshObject mo, Vector2 p0, Vector2 p1,
            bool[] faceCulledMask,
            List<(Vector2, Vector2)> outSegs, List<Vector2> outPts)
        {
            if (ctx == null || mo == null || ctx.WorldToScreenPos == null) return;
            if ((p1 - p0).sqrMagnitude < 1e-6f) return;

            int vcount = mo.VertexCount;
            if (vcount == 0) return;

            // Vertices[].Position はローカル座標。頂点単位の LocalToScreen が
            // 描画側と同一規則（スキンド頂点はボーンの SkinningMatrix ブレンド、
            // 非スキンド頂点はメッシュの WorldMatrix）で変換する。
            float h = ctx.PreviewRect.height;
            var sp = new Vector2[vcount];
            for (int i = 0; i < vcount; i++)
            {
                Vector2 s = ctx.LocalToScreen(i, mo.Vertices[i].Position);
                sp[i] = new Vector2(s.x, h - s.y);
            }

            var seen = new HashSet<long>();
            int fc = mo.FaceCount;
            for (int f = 0; f < fc; f++)
            {
                if (faceCulledMask != null && f < faceCulledMask.Length && faceCulledMask[f]) continue;

                var face = mo.Faces[f];
                int n = face.VertexIndices.Count;
                if (n < 3) continue;

                for (int i = 0; i < n; i++)
                {
                    int a = face.VertexIndices[i];
                    int b = face.VertexIndices[(i + 1) % n];
                    if (a < 0 || b < 0 || a >= vcount || b >= vcount) continue;

                    if (!SegSeg(p0, p1, sp[a], sp[b], out float t, out float u)) continue;
                    if (t < 0f || t > 1f) continue;
                    if (u < 0f || u > 1f) continue;

                    outPts.Add(new Vector2(Mathf.Lerp(sp[a].x, sp[b].x, u),
                                           Mathf.Lerp(sp[a].y, sp[b].y, u)));

                    if (seen.Add(EdgeKey(a, b)))
                        outSegs.Add((sp[a], sp[b]));
                }
            }
        }
    }
}
