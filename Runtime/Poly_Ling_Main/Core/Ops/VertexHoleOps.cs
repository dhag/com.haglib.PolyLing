// VertexHoleOps.cs
// 選択した1頂点（頂点A）を消して、そこに穴を開ける。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【手順】
//   1. A に辺でつながる相手（根元 R）ごとに、新頂点 N = Lerp(R, A, ratio) を作る。
//      ratio=1.00 が A の位置、ratio=0 が根元の位置。
//   2. A を含む各面のループ「… prev, A, next …」の A を「N(prev), N(next)」の
//      2頂点に置換する。三角形 (A, Ri, Ri+1) はこれで四角形になり、
//      巻き方向・マテリアル・Flags がそのまま保存される。
//      四角形以上の面では、根元同士が辺でつながっていないため四角形にはならず、
//      1頂点増えた多角形になる（穴の縁は閉じたままで欠けを作らない）。
//   3. 参照されなくなった A を削除し、残りの面の頂点インデックスを詰め直す。
//      穴を塞ぐ面は作らない。
//
// 【不変条件（厳守）】
//   ・Vertex.UVs.Count == Vertex.Normals.Count
//   ・Face.UVIndices[j] == Face.NormalIndices[j]
//   新頂点のスロット確保は Vertex.GetOrAddUVNormal のみを使う。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class VertexHoleOps
    {
        /// <summary>実行可否と対象の規模。パネルの表示・ボタン活性に使う。</summary>
        public struct HoleInfo
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>辺でつながる相手（根元）の数。</summary>
            public int NeighborCount;
            /// <summary>頂点A を含む面の数。</summary>
            public int FaceCount;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        // ================================================================
        // 事前調査
        // ================================================================

        /// <summary>頂点A の周辺を調べ、実行可否を返す。メッシュは変更しない。</summary>
        public static HoleInfo Inspect(MeshObject mo, int apex)
        {
            var info = new HoleInfo();

            if (mo == null || apex < 0 || apex >= mo.Vertices.Count)
            {
                info.Reason = "頂点が指定されていません";
                return info;
            }

            var neighbors = new HashSet<int>();
            int faceCount = 0;

            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var f = mo.Faces[fi];
                int n = f.VertexIndices.Count;

                int occur = 0;
                int p = -1;
                for (int j = 0; j < n; j++)
                {
                    if (f.VertexIndices[j] != apex) continue;
                    occur++;
                    if (p < 0) p = j;
                }
                if (occur == 0) continue;

                if (occur > 1)
                {
                    info.Reason = "同じ面が指定頂点を複数回参照しています";
                    return info;
                }

                faceCount++;

                if (n >= 3)
                {
                    neighbors.Add(f.VertexIndices[(p - 1 + n) % n]);
                    neighbors.Add(f.VertexIndices[(p + 1) % n]);
                }
                else if (n == 2)
                {
                    neighbors.Add(f.VertexIndices[(p + 1) % n]);
                }
            }

            neighbors.Remove(apex);

            info.NeighborCount = neighbors.Count;
            info.FaceCount     = faceCount;

            if (faceCount == 0)
            {
                info.Reason = "指定頂点はどの面にも使われていません";
                return info;
            }
            if (neighbors.Count < 2)
            {
                info.Reason = "指定頂点につながる辺が足りません";
                return info;
            }

            info.CanExecute = true;
            return info;
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>
        /// 頂点A に穴を開ける。成功したら true。
        /// ratio は 0〜1（1 が頂点A の位置）。
        /// </summary>
        public static bool Execute(
            MeshObject mo, int apex, float ratio,
            out int createdVertexCount, out int modifiedFaceCount, out string reason)
        {
            createdVertexCount = 0;
            modifiedFaceCount  = 0;

            var info = Inspect(mo, apex);
            if (!info.CanExecute)
            {
                reason = info.Reason;
                return false;
            }

            float t = Mathf.Clamp01(ratio);
            var apexVertex = mo.Vertices[apex];

            // 根元 → 新頂点インデックス
            var newIdxOf = new Dictionary<int, int>();

            int NewFor(int root)
            {
                if (newIdxOf.TryGetValue(root, out int cached)) return cached;

                var vr = mo.Vertices[root];
                var v  = new Vertex(Vector3.Lerp(vr.Position, apexVertex.Position, t));

                // スキンドメッシュに追加する頂点には BoneWeight が必須。
                // 持たせないと GPU 側でその頂点だけ別行列で変換されて位置がずれる。
                v.BoneWeight       = Poly_Ling.UI.SkinWeightOps.LerpNullable(
                                        vr.BoneWeight, apexVertex.BoneWeight, t);
                v.MirrorBoneWeight = Poly_Ling.UI.SkinWeightOps.LerpNullable(
                                        vr.MirrorBoneWeight, apexVertex.MirrorBoneWeight, t);
                v.Flags = vr.Flags;

                // UV/法線スロットは面ごとに GetOrAddUVNormal で確保するので、ここでは空のまま。
                int idx = mo.AddVertex(v);
                newIdxOf[root] = idx;
                return idx;
            }

            // 面の張り替え（A を含む面だけ）
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var f = mo.Faces[fi];
                int n = f.VertexIndices.Count;

                int p = f.VertexIndices.IndexOf(apex);
                if (p < 0) continue;

                Vector3 faceNormal = NormalSmoothingOps.CalculateFaceNormalNewell(mo, f);

                var nv = new List<int>(n + 1);
                var nu = new List<int>(n + 1);
                var nn = new List<int>(n + 1);

                void EmitOriginal(int c)
                {
                    nv.Add(f.VertexIndices[c]);
                    nu.Add(c < f.UVIndices.Count     ? f.UVIndices[c]     : 0);
                    nn.Add(c < f.NormalIndices.Count ? f.NormalIndices[c] : 0);
                }

                void EmitNew(int rootCorner)
                {
                    int root   = f.VertexIndices[rootCorner];
                    int newIdx = NewFor(root);

                    Vector2 uv = Vector2.Lerp(CornerUV(mo, f, rootCorner), CornerUV(mo, f, p), t);
                    Vector3 nr = Vector3.Lerp(CornerNormal(mo, f, rootCorner, faceNormal),
                                              CornerNormal(mo, f, p,          faceNormal), t);
                    nr = nr.sqrMagnitude < 1e-12f ? faceNormal : nr.normalized;

                    // 不変条件を保つ唯一の追加口
                    int slot = mo.Vertices[newIdx].GetOrAddUVNormal(uv, nr);

                    nv.Add(newIdx);
                    nu.Add(slot);
                    nn.Add(slot);
                }

                for (int j = 0; j < n; j++)
                {
                    if (j != p) { EmitOriginal(j); continue; }

                    if (n >= 3)
                    {
                        // 「… prev, A, next …」の A を「N(prev), N(next)」に置換。
                        // この順序でループの巻き方向が保たれる。
                        EmitNew((p - 1 + n) % n);
                        EmitNew((p + 1) % n);
                    }
                    else
                    {
                        // 線分（2頂点の面）は端点を1つ置き換えるだけ。
                        EmitNew((p + 1) % n);
                    }
                }

                f.VertexIndices = nv;
                f.UVIndices     = nu;
                f.NormalIndices = nn;
                modifiedFaceCount++;
            }

            createdVertexCount = newIdxOf.Count;

            // ここで A はどの面からも参照されていないはず。
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                if (mo.Faces[fi].VertexIndices.IndexOf(apex) < 0) continue;
                reason = "内部エラー: 指定頂点の参照が残りました";
                Debug.LogError($"[VertexHoleOps] 面 {fi} に頂点 {apex} の参照が残っています");
                return false;
            }

            // A を削除し、残存面の頂点インデックスを詰める。
            foreach (var f in mo.Faces)
            {
                var vidx = f.VertexIndices;
                for (int j = 0; j < vidx.Count; j++)
                    if (vidx[j] > apex) vidx[j] = vidx[j] - 1;
            }
            mo.Vertices.RemoveAt(apex);
            mo.InvalidatePositionCache();

            reason = null;
            return true;
        }


        // ================================================================
        // 一括処理（複数頂点）
        // ================================================================

        /// <summary>一括処理の下調べ結果。パネル表示とボタン活性に使う。</summary>
        public struct HoleBatchInfo
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>実行対象の頂点数。</summary>
            public int TargetCount;
            /// <summary>干渉・単独不可で除外した頂点数。</summary>
            public int SkippedCount;
            /// <summary>作られる新頂点の合計。</summary>
            public int NeighborTotal;
            /// <summary>張り替える面の合計。</summary>
            public int FaceTotal;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        /// <summary>
        /// 互いに干渉しない頂点だけを選び出す。
        ///
        /// 除外の規則:
        ///   1. 単独で Inspect が通らない頂点。
        ///   2. 同じ面を共有する選択頂点どうし（その組は全部除外）。
        ///      隣り合う頂点は必ず同じ面を共有するのでこれに含まれる。
        ///      面を共有したまま両方に穴を開けると、片方の張り替えが
        ///      もう片方の削除予定頂点を参照してしまうため。
        /// </summary>
        public static List<int> SelectIndependent(
            MeshObject mo, IEnumerable<int> apexes, out List<int> skipped)
        {
            var accepted = new List<int>();
            skipped      = new List<int>();

            if (mo == null || apexes == null) return accepted;

            // 重複を落としつつ、単独で実行できる頂点だけを候補にする。
            var candidates = new HashSet<int>();
            foreach (int a in apexes)
            {
                if (a < 0 || a >= mo.Vertices.Count) continue;
                if (!candidates.Add(a)) continue;

                if (!Inspect(mo, a).CanExecute)
                {
                    candidates.Remove(a);
                    skipped.Add(a);
                }
            }

            // 同じ面に2つ以上乗っている候補を落とす。
            var conflicted = new HashSet<int>();
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var vidx = mo.Faces[fi].VertexIndices;

                var onFace = new List<int>();
                for (int j = 0; j < vidx.Count; j++)
                {
                    int v = vidx[j];
                    if (candidates.Contains(v) && !onFace.Contains(v)) onFace.Add(v);
                }

                if (onFace.Count >= 2)
                    foreach (int v in onFace) conflicted.Add(v);
            }

            foreach (int a in candidates)
            {
                if (conflicted.Contains(a)) skipped.Add(a);
                else                        accepted.Add(a);
            }

            accepted.Sort();
            skipped.Sort();
            return accepted;
        }

        /// <summary>複数頂点ぶんの下調べ。メッシュは変更しない。</summary>
        public static HoleBatchInfo InspectMany(MeshObject mo, IEnumerable<int> apexes)
        {
            var info = new HoleBatchInfo();

            if (mo == null)
            {
                info.Reason = "メッシュがありません";
                return info;
            }

            var targets = SelectIndependent(mo, apexes, out var skipped);

            info.TargetCount  = targets.Count;
            info.SkippedCount = skipped.Count;

            foreach (int a in targets)
            {
                var one = Inspect(mo, a);
                info.NeighborTotal += one.NeighborCount;
                info.FaceTotal     += one.FaceCount;
            }

            if (targets.Count == 0)
            {
                info.Reason = skipped.Count > 0
                    ? "選択頂点が互いに干渉しているため実行できません"
                    : "頂点を選択してください";
                return info;
            }

            info.CanExecute = true;
            return info;
        }

        /// <summary>
        /// 複数頂点に穴を開ける。互いに干渉する頂点は処理しない。
        /// 1つでも開けられたら true。
        ///
        /// 実行順は「インデックスの降順」。Execute は末尾で apex を削除して
        /// それより大きいインデックスを詰めるので、降順なら残りの対象がずれない。
        /// </summary>
        public static bool ExecuteMany(
            MeshObject mo, IEnumerable<int> apexes, float ratio,
            out int createdVertexCount, out int modifiedFaceCount, out int skippedCount,
            out string reason)
        {
            createdVertexCount = 0;
            modifiedFaceCount  = 0;
            skippedCount       = 0;
            reason             = null;

            if (mo == null) { reason = "メッシュがありません"; return false; }

            var targets = SelectIndependent(mo, apexes, out var skipped);
            skippedCount = skipped.Count;

            if (targets.Count == 0)
            {
                reason = skipped.Count > 0
                    ? "選択頂点が互いに干渉しているため実行できません"
                    : "頂点を選択してください";
                return false;
            }

            int okCount = 0;

            for (int k = targets.Count - 1; k >= 0; k--)
            {
                bool ok = Execute(mo, targets[k], ratio,
                                  out int created, out int modified, out string why);
                if (!ok)
                {
                    skippedCount++;
                    reason = why;
                    continue;
                }

                createdVertexCount += created;
                modifiedFaceCount  += modified;
                okCount++;
            }

            if (okCount == 0) return false;

            reason = null;
            return true;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>面のコーナー c が指す UV。スロットが無ければ Vector2.zero。</summary>
        private static Vector2 CornerUV(MeshObject mo, Face f, int c)
        {
            int vi = f.VertexIndices[c];
            if (vi < 0 || vi >= mo.Vertices.Count) return Vector2.zero;

            var vert = mo.Vertices[vi];
            int s = c < f.UVIndices.Count ? f.UVIndices[c] : 0;
            return (s >= 0 && s < vert.UVs.Count) ? vert.UVs[s] : Vector2.zero;
        }

        /// <summary>面のコーナー c が指す法線。スロットが無ければ面法線。</summary>
        private static Vector3 CornerNormal(MeshObject mo, Face f, int c, Vector3 fallback)
        {
            int vi = f.VertexIndices[c];
            if (vi < 0 || vi >= mo.Vertices.Count) return fallback;

            var vert = mo.Vertices[vi];
            int s = c < f.NormalIndices.Count ? f.NormalIndices[c] : 0;
            return (s >= 0 && s < vert.Normals.Count) ? vert.Normals[s] : fallback;
        }
    }
}
