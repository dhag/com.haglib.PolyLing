// UvEditSpaceOps.cs
// 作業空間（UV）の代理メッシュの生成と、元メッシュへの反映。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【座標】
//   代理頂点のローカル座標 = (sU·U, sV·V, 0)。反映は UV = (x/sU, y/sV)。
//   sU・sV は作業倍率で、生成と反映で同じ値を使う（UvEditSpaceBinding.ScaleU / ScaleV）。
//   通常は sU = sV。「画像の縦横比で表示」では sV = sU × 高さ ÷ 幅（画素が正方形に見える）。
//   UV の値そのものは倍率にかかわらず同じ。UV を 0〜1 に収めない。
//
// 【対応は面の角で持つ】
//   代理の面 f の k 番目の角 ↔ 元の面の k 番目の角。
//   代理の面には、元の面番号 + 1 を Face.Id として入れる（代理は本処理が作る
//   作業用メッシュで、利用者のデータではない。AssignMissingIds は呼ばない）。
//   Face.Clone は Id を保つので、Undo（スナップショットの復元）で面の実体が
//   入れ替わっても対応は残る。面の並び位置は対応のキーにしない
//   （頂点マージは面の数・並びを変えうる。MeshMergeHelper）。
//
// 【比べる相手は今の元メッシュ】
//   「変更あり」の判定は、控えの値ではなく今の元メッシュの UV から作る。
//   反映を Undo で戻したとき、代理は反映後の位置のまま残り、元メッシュとの差が
//   「変更あり」として見える（もう一度反映できる）。
//
// 【取消しは代理を作り直す】
//   代理で頂点を結合・分離すると、位置を戻すだけでは元の形に戻らない。
//   取消しは今の元メッシュから同じ対象の面で代理を作り直し、控えも作り直す（ResetProxy）。
//
// 【反映は検査を全部通ってから書く】（設計方針 6.5）
//   元メッシュの構造が開いた時点（または直前の反映）から変わっている、代理の面と元の面が
//   1 対 1 にならない、角の数が違う、のどれかに当たれば元の UV を変えずに失敗を返す。
//   代理側の問題は最初の 1 件で止めずに全部集め、該当する代理の面と元の面を返す。
//   頂点番号がずれたまま書き戻すことや、不足分をゼロ UV で補うことはしない。
//
// 【UV スロットの割り当て】（MeshBridgeDefault の規約：UVs.Count == Normals.Count、
//   面の UVIndices[k] == NormalIndices[k]。UV と法線は同じ番号の 1 組）
//   角のまとまり = （元の頂点, 代理の頂点, その角の元の法線）。まとまり 1 つにスロット 1 つ。
//   ・同じ元の頂点で代理の頂点が違う（代理で分けた）→ 別のスロット。
//   ・代理の頂点が同じでも元の法線が違う（ハードエッジ）→ 別のスロット。UV を結合しても
//     法線の見た目は変えない（設計方針 6.6）。
//   まとまりの角が元々指していたスロットを優先して再利用する。対象外の面が使っている
//   スロットは書き換えない。使えるスロットが無いまとまりには、その法線を持つ新しい
//   スロットを足す（Vertex.AddUVNormalSlot）。
//   反映の前は対象の面が使っていて、反映の後はどの面も使わなくなったスロットは詰める
//   （詰めたぶん、その頂点を指す全ての面の番号を付け替える）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>作業空間（UV）の代理と元メッシュの対応。反映が成功するたびに控えを更新する。</summary>
    public sealed class UvEditSpaceBinding
    {
        /// <summary>U 方向の作業倍率 sU。代理頂点 = (sU·U, sV·V, 0)。</summary>
        public float ScaleU;

        /// <summary>V 方向の作業倍率 sV。</summary>
        public float ScaleV;

        /// <summary>(sU, sV)。</summary>
        public Vector2 Scale => new Vector2(ScaleU, ScaleV);

        /// <summary>開いた時点の元メッシュの頂点数。</summary>
        public int SourceVertexCount;

        /// <summary>同じく面数。</summary>
        public int SourceFaceCount;

        /// <summary>元の面ごとの控え。対象外の面は null。</summary>
        public FaceRecord[] Faces;

        /// <summary>対象の面の数。</summary>
        public int IncludedFaceCount;

        /// <summary>
        /// 元の面 1 枚の控え。控えるのは頂点の並び（構造）だけ。
        /// UV スロットの番号は控えず、使うたびに今の元の面から読む。反映はスロットを足し・詰めるので、
        /// 反映を Undo / Redo すると番号が変わる。控えを持つとその度に食い違う。
        /// </summary>
        public sealed class FaceRecord
        {
            public int[] VertexIndices;
        }

        /// <summary>元の面番号が対象か。</summary>
        public bool IsIncluded(int sourceFace)
            => sourceFace >= 0 && Faces != null && sourceFace < Faces.Length && Faces[sourceFace] != null;

        /// <summary>対象の元の面番号の一覧。</summary>
        public List<int> IncludedFaces()
        {
            var list = new List<int>();
            if (Faces != null)
                for (int f = 0; f < Faces.Length; f++) if (Faces[f] != null) list.Add(f);
            return list;
        }
    }

    /// <summary>作業空間（UV）の代理の生成と反映。</summary>
    public static class UvEditSpaceOps
    {
        /// <summary>代理メッシュの面法線（ローカル）。ローカル XY 面をカメラ側から見る向き。</summary>
        private static readonly Vector3 ProxyNormal = Vector3.back;

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// 元メッシュの UV から代理メッシュを作る。
        /// faceFilter が null または空なら全ての面、materialFilter が負ならマテリアルで絞らない。
        /// 3 頂点未満の面（線分）は対象にしない。
        /// </summary>
        /// <returns>代理メッシュ。作れなければ null（error に理由）。</returns>
        public static MeshObject BuildProxy(
            MeshObject src, ICollection<int> faceFilter, int materialFilter, Vector2 uvScale,
            out UvEditSpaceBinding binding, out string error)
        {
            binding = null;
            error   = null;
            if (src == null) { error = "元メッシュがありません"; return null; }
            if (uvScale.x <= 0f || uvScale.y <= 0f) { error = "作業倍率は正の値にしてください"; return null; }

            bool useFilter = faceFilter != null && faceFilter.Count > 0;

            var b = new UvEditSpaceBinding
            {
                ScaleU            = uvScale.x,
                ScaleV            = uvScale.y,
                SourceVertexCount = src.VertexCount,
                SourceFaceCount   = src.FaceCount,
                Faces             = new UvEditSpaceBinding.FaceRecord[src.FaceCount],
            };

            var mapping     = new Dictionary<(int vi, int slot), int>();
            var newVertices = new List<Vertex>();
            var newFaces    = new List<Face>();

            for (int f = 0; f < src.FaceCount; f++)
            {
                var face = src.Faces[f];
                if (face == null || face.VertexCount < 3) continue;
                if (useFilter && !faceFilter.Contains(f)) continue;
                if (materialFilter >= 0 && face.MaterialIndex != materialFilter) continue;

                var rec = new UvEditSpaceBinding.FaceRecord
                {
                    VertexIndices = face.VertexIndices.ToArray(),
                };

                var proxyFace = new Face
                {
                    Id            = f + 1,
                    MaterialIndex = face.MaterialIndex,
                    Flags         = face.Flags,
                };

                for (int k = 0; k < face.VertexCount; k++)
                {
                    int vi = face.VertexIndices[k];
                    if (vi < 0 || vi >= src.VertexCount)
                    { error = $"面 {f} が範囲外の頂点 {vi} を指しています"; return null; }

                    var v = src.Vertices[vi];
                    int slot = k < face.UVIndices.Count ? face.UVIndices[k] : -1;
                    if (slot < 0 || slot >= v.UVs.Count)
                    { error = $"面 {f} の {k} 番目の角に UV がありません（頂点 {vi}）"; return null; }


                    if (!mapping.TryGetValue((vi, slot), out int pv))
                    {
                        pv = newVertices.Count;
                        mapping[(vi, slot)] = pv;
                        Vector2 uv = v.UVs[slot];
                        var nv = new Vertex(ToLocal(uv, uvScale));
                        nv.UVs.Add(uv);
                        nv.Normals.Add(ProxyNormal);
                        newVertices.Add(nv);
                    }

                    proxyFace.VertexIndices.Add(pv);
                    proxyFace.UVIndices.Add(0);
                    proxyFace.NormalIndices.Add(0);
                }

                b.Faces[f] = rec;
                b.IncludedFaceCount++;
                newFaces.Add(proxyFace);
            }

            if (b.IncludedFaceCount == 0) { error = "対象の面がありません"; return null; }

            var mo = new MeshObject($"{src.Name}_UV")
            {
                Vertices = newVertices,
                Faces    = newFaces,
                Type     = MeshType.Mesh,
            };
            mo.RecomputeSkinKind();

            binding = b;
            return mo;
        }

        /// <summary>UV → 代理頂点のローカル座標。</summary>
        public static Vector3 ToLocal(Vector2 uv, Vector2 uvScale)
            => new Vector3(uv.x * uvScale.x, uv.y * uvScale.y, 0f);

        /// <summary>代理頂点のローカル座標 → UV。</summary>
        public static Vector2 ToUv(Vector3 local, Vector2 uvScale)
            => new Vector2(local.x / uvScale.x, local.y / uvScale.y);

        // ================================================================
        // 変更の有無
        // ================================================================

        /// <summary>
        /// 代理の頂点が、今の元メッシュの UV から作った位置から動いているか。
        /// 対応の取れない代理（構造が変わった等）は「変更あり」とする。
        /// </summary>
        public static bool HasChanges(MeshObject src, MeshObject proxy, UvEditSpaceBinding b)
        {
            if (src == null || proxy == null || b == null) return false;
            var seen = new HashSet<int>();
            foreach (var face in proxy.Faces)
            {
                int sf = face != null ? face.Id - 1 : -1;
                if (!b.IsIncluded(sf)) return true;
                if (!seen.Add(sf)) return true;
                var rec = b.Faces[sf];
                if (face.VertexCount != rec.VertexIndices.Length) return true;
                for (int k = 0; k < face.VertexCount; k++)
                {
                    int pv = face.VertexIndices[k];
                    if (pv < 0 || pv >= proxy.VertexCount) return true;
                    if (!TryCurrentUv(src, rec.VertexIndices[k], CurSlot(src, sf, k), out var uv)) return true;
                    Vector3 built = ToLocal(uv, b.Scale);
                    Vector3 p = proxy.Vertices[pv].Position;
                    if (p.x != built.x || p.y != built.y) return true;
                }
            }
            if (seen.Count != b.IncludedFaceCount) return true;
            return HasTopologyChanges(src, proxy, b);
        }

        /// <summary>今の元の面 f の k 番目の角が指す UV スロット。無ければ -1。</summary>
        private static int CurSlot(MeshObject src, int f, int k)
        {
            if (f < 0 || f >= src.FaceCount) return -1;
            var face = src.Faces[f];
            return face != null && k < face.UVIndices.Count ? face.UVIndices[k] : -1;
        }

        /// <summary>
        /// 代理の接続を今反映すると、元の角のスロット参照が変わるか。
        /// 反映のまとまり（元の頂点, 代理の頂点, 元の法線）とスロットが 1 対 1 なら変わらない。
        /// ・同じ (元の頂点, スロット) の角が別の代理の頂点を指す → 反映でスロットが分かれる。
        /// ・同じまとまりの角が別のスロットを指す → 反映でスロットがまとまる。
        /// 法線の違う角が 1 つの代理の頂点を共有する状態（結合して反映した後）や、
        /// 別の元の頂点が 1 つの代理の頂点を共有する状態は、反映しても変わらないので変更なし。
        /// </summary>
        private static bool HasTopologyChanges(MeshObject src, MeshObject proxy, UvEditSpaceBinding b)
        {
            var slotToPv  = new Dictionary<(int vi, int slot), int>();
            var groupSlot = new Dictionary<CornerKey, int>();
            foreach (var face in proxy.Faces)
            {
                int sf  = face.Id - 1;
                var rec = b.Faces[sf];
                for (int k = 0; k < face.VertexCount; k++)
                {
                    int vi   = rec.VertexIndices[k];
                    int slot = CurSlot(src, sf, k);
                    int pv   = face.VertexIndices[k];
                    if (slotToPv.TryGetValue((vi, slot), out int prevPv) && prevPv != pv) return true;
                    var key = new CornerKey(vi, pv, NormalOf(src, vi, slot));
                    if (groupSlot.TryGetValue(key, out int prevSlot) && prevSlot != slot) return true;
                    slotToPv[(vi, slot)] = pv;
                    groupSlot[key]       = slot;
                }
            }
            return false;
        }

        private static bool TryCurrentUv(MeshObject src, int vi, int slot, out Vector2 uv)
        {
            uv = default;
            if (vi < 0 || vi >= src.VertexCount) return false;
            var uvs = src.Vertices[vi].UVs;
            if (slot < 0 || slot >= uvs.Count) return false;
            uv = uvs[slot];
            return true;
        }

        // ================================================================
        // 取消し（今の元メッシュから代理を作り直す）
        // ================================================================

        /// <summary>
        /// 今の元メッシュから、同じ対象の面で代理を作り直す。代理の MeshObject は差し替えず、
        /// 頂点・面だけを入れ替える（ビルボード・表示の設定は MeshObject が持っているため）。
        /// 成功したら控え（b）も作り直したものに替える。失敗したら何も変えずに理由を返す。
        /// </summary>
        /// <param name="newScale">作業倍率を替えて作り直すときの (sU, sV)。null なら今の倍率。</param>
        public static string ResetProxy(MeshObject src, MeshObject proxy, ref UvEditSpaceBinding b,
                                        Vector2? newScale = null)
        {
            if (src == null || proxy == null || b == null) return "作業空間がありません";

            var faces = b.IncludedFaces();
            foreach (int f in faces)
                if (f >= src.FaceCount) return $"元の面 {f} がありません（元メッシュの面数 {src.FaceCount}）";

            var rebuilt = BuildProxy(src, faces, -1, newScale ?? b.Scale, out var nb, out string error);
            if (rebuilt == null) return error;

            proxy.Clear();
            proxy.Vertices.AddRange(rebuilt.Vertices);
            proxy.Faces.AddRange(rebuilt.Faces);
            proxy.InvalidatePositionCache();
            b = nb;
            return null;
        }

        // ================================================================
        // 反映
        // ================================================================

        /// <summary>角のまとまり（元の頂点, 代理の頂点, その角の元の法線）。</summary>
        private readonly struct CornerKey : System.IEquatable<CornerKey>
        {
            public readonly int     Vi;
            public readonly int     Pv;
            public readonly Vector3 Normal;
            public CornerKey(int vi, int pv, Vector3 n) { Vi = vi; Pv = pv; Normal = n; }
            public bool Equals(CornerKey o) => Vi == o.Vi && Pv == o.Pv && Normal.Equals(o.Normal);
            public override bool Equals(object obj) => obj is CornerKey o && Equals(o);
            public override int GetHashCode()
            {
                unchecked { return (Vi * 397) ^ (Pv * 7919) ^ Normal.GetHashCode(); }
            }
        }

        /// <summary>元の頂点 vi のスロット slot の法線。無ければ先頭、それも無ければ上向き。</summary>
        private static Vector3 NormalOf(MeshObject src, int vi, int slot)
        {
            var ns = src.Vertices[vi].Normals;
            if (slot >= 0 && slot < ns.Count) return ns[slot];
            return ns.Count > 0 ? ns[0] : Vector3.up;
        }

        /// <summary>
        /// 代理の頂点位置を元メッシュの UV へ書き戻す。
        /// 検査を全部通ったときだけ書き、成功したら控え（b）を反映後の状態へ更新する。
        /// </summary>
        /// <param name="problemSourceFaces">失敗の原因になった元の面番号（示すため）。</param>
        /// <param name="problemProxyFaces">失敗の原因になった代理の面番号（選択して示すため）。</param>
        /// <returns>失敗理由。成功なら null。</returns>
        public static string ApplyProxy(
            MeshObject src, MeshObject proxy, UvEditSpaceBinding b,
            List<int> problemSourceFaces, List<int> problemProxyFaces)
        {
            if (src == null || proxy == null || b == null) return "作業空間がありません";
            Vector2 s = b.Scale;

            // ── 1. 元メッシュの構造が開いた時点（直前の反映）から変わっていないか ──
            if (src.VertexCount != b.SourceVertexCount || src.FaceCount != b.SourceFaceCount)
                return "元メッシュの頂点数・面数が作業空間を開いた後に変わっています";

            for (int f = 0; f < b.Faces.Length; f++)
            {
                var rec = b.Faces[f];
                if (rec == null) continue;
                var face = src.Faces[f];
                if (face == null || face.VertexCount != rec.VertexIndices.Length)
                { problemSourceFaces?.Add(f); return $"元の面 {f} の構造が変わっています"; }
                for (int k = 0; k < rec.VertexIndices.Length; k++)
                {
                    if (face.VertexIndices[k] != rec.VertexIndices[k])
                    { problemSourceFaces?.Add(f); return $"元の面 {f} の頂点が変わっています"; }
                    int slot = k < face.UVIndices.Count ? face.UVIndices[k] : -1;
                    if (slot < 0 || slot >= src.Vertices[rec.VertexIndices[k]].UVs.Count)
                    { problemSourceFaces?.Add(f); return $"元の面 {f} の {k} 番目の角に UV がありません"; }
                }
            }

            // ── 2. 代理の面と元の面が 1 対 1 か、角の数が同じか（問題を全部集める） ──
            var proxyOf      = new Face[b.Faces.Length];
            var proxyIndexOf = new int[b.Faces.Length];
            for (int i = 0; i < proxyIndexOf.Length; i++) proxyIndexOf[i] = -1;

            var badProxy  = new SortedSet<int>();
            var badSource = new SortedSet<int>();
            int unknown = 0, duplicated = 0, cornerMismatch = 0, badRef = 0, missing = 0;

            for (int pi = 0; pi < proxy.FaceCount; pi++)
            {
                var pf = proxy.Faces[pi];
                if (pf == null) continue;
                int sf = pf.Id - 1;
                if (!b.IsIncluded(sf)) { unknown++; badProxy.Add(pi); continue; }
                if (proxyOf[sf] != null)
                {
                    duplicated++;
                    badProxy.Add(pi); badProxy.Add(proxyIndexOf[sf]); badSource.Add(sf);
                    continue;
                }
                proxyOf[sf]      = pf;
                proxyIndexOf[sf] = pi;
                if (pf.VertexCount != b.Faces[sf].VertexIndices.Length)
                { cornerMismatch++; badProxy.Add(pi); badSource.Add(sf); continue; }
                foreach (int pv in pf.VertexIndices)
                    if (pv < 0 || pv >= proxy.VertexCount)
                    { badRef++; badProxy.Add(pi); badSource.Add(sf); break; }
            }
            for (int f = 0; f < b.Faces.Length; f++)
                if (b.Faces[f] != null && proxyOf[f] == null) { missing++; badSource.Add(f); }

            if (badProxy.Count > 0 || badSource.Count > 0)
            {
                problemProxyFaces?.AddRange(badProxy);
                problemSourceFaces?.AddRange(badSource);
                var parts = new List<string>();
                if (unknown        > 0) parts.Add($"元の面と対応しない代理の面 {unknown}");
                if (duplicated     > 0) parts.Add($"同じ元の面に対応する代理の面の重複 {duplicated}");
                if (cornerMismatch > 0) parts.Add($"角の数が元の面と違う代理の面 {cornerMismatch}");
                if (badRef         > 0) parts.Add($"範囲外の頂点を指す代理の面 {badRef}");
                if (missing        > 0) parts.Add($"対応する代理の面が無い元の面 {missing}");
                return "反映できません：" + string.Join("、", parts)
                     + "（該当する代理の面を選択しました。Undo で結合前に戻してください）";
            }

            // ── 3. 角のまとまりごとにスロットを決める（まだ書かない） ──
            //   対象外の面が使っているスロットは書き換えない。
            var locked = new Dictionary<int, HashSet<int>>();
            for (int f = 0; f < src.FaceCount; f++)
            {
                if (b.IsIncluded(f)) continue;
                var face = src.Faces[f];
                if (face == null) continue;
                for (int k = 0; k < face.VertexCount; k++)
                {
                    int vi = face.VertexIndices[k];
                    if (vi < 0 || vi >= src.VertexCount) continue;
                    if (k >= face.UVIndices.Count) continue;
                    if (!locked.TryGetValue(vi, out var set)) locked[vi] = set = new HashSet<int>();
                    set.Add(face.UVIndices[k]);
                }
            }

            // 角を集め、まとまりごとの候補スロットを出現順に並べる。
            var keyOf      = new Dictionary<(int f, int k), CornerKey>();
            var candidates = new Dictionary<CornerKey, List<int>>();
            var order      = new List<CornerKey>();
            var usedBefore = new Dictionary<int, HashSet<int>>();   // 反映前に対象の面が使っていたスロット
            for (int f = 0; f < b.Faces.Length; f++)
            {
                var rec = b.Faces[f];
                if (rec == null) continue;
                var pf = proxyOf[f];
                for (int k = 0; k < rec.VertexIndices.Length; k++)
                {
                    int vi   = rec.VertexIndices[k];
                    int slot = CurSlot(src, f, k);
                    var key  = new CornerKey(vi, pf.VertexIndices[k], NormalOf(src, vi, slot));
                    keyOf[(f, k)] = key;
                    if (!candidates.TryGetValue(key, out var list))
                    {
                        candidates[key] = list = new List<int>();
                        order.Add(key);
                    }
                    if (!list.Contains(slot)) list.Add(slot);
                    if (!usedBefore.TryGetValue(vi, out var ub)) usedBefore[vi] = ub = new HashSet<int>();
                    ub.Add(slot);
                }
            }

            var assigned = new Dictionary<CornerKey, int>();     // 既存スロット。新しいスロットは -1
            var finalUv  = new Dictionary<CornerKey, Vector2>();
            var claimed  = new HashSet<(int vi, int slot)>();

            foreach (var key in order)
            {
                locked.TryGetValue(key.Vi, out var lockSet);
                int chosen = -1;
                foreach (int c in candidates[key])
                {
                    if (lockSet != null && lockSet.Contains(c)) continue;
                    if (claimed.Contains((key.Vi, c))) continue;
                    chosen = c;
                    break;
                }

                Vector3 p = proxy.Vertices[key.Pv].Position;
                Vector2 moved = ToUv(p, s);
                if (chosen >= 0)
                {
                    claimed.Add((key.Vi, chosen));
                    // 位置が今の UV から動いていなければ今の UV をそのまま使う（往復で値を変えない）。
                    Vector2 cur   = src.Vertices[key.Vi].UVs[chosen];
                    Vector3 built = ToLocal(cur, s);
                    finalUv[key]  = (p.x == built.x && p.y == built.y) ? cur : moved;
                }
                else
                {
                    finalUv[key] = moved;
                }
                assigned[key] = chosen;
            }

            // ── 4. 書く ──
            foreach (var key in order)
            {
                var v = src.Vertices[key.Vi];
                int slot = assigned[key];
                if (slot >= 0)
                {
                    v.UVs[slot] = finalUv[key];
                }
                else
                {
                    slot = v.AddUVNormalSlot(finalUv[key], key.Normal);
                    assigned[key] = slot;
                }
            }
            foreach (var kv in keyOf)
            {
                var face = src.Faces[kv.Key.f];
                int slot = assigned[kv.Value];
                face.UVIndices[kv.Key.k] = slot;
                if (kv.Key.k < face.NormalIndices.Count) face.NormalIndices[kv.Key.k] = slot;
            }

            // ── 5. 対象の面が使わなくなったスロットを詰める ──
            VertexSlotOps.CompactUnusedSlots(src, usedBefore);

            // ── 6. 代理の位置を、書いた UV から作り直した値にそろえる ──
            //   割り算の丸めで「変更あり」に見えないようにするため。差は浮動小数の末尾だけ。
            foreach (var kv in keyOf)
            {
                int f = kv.Key.f, k = kv.Key.k;
                int vi = b.Faces[f].VertexIndices[k];
                int pv = proxyOf[f].VertexIndices[k];
                proxy.Vertices[pv].Position = ToLocal(src.Vertices[vi].UVs[src.Faces[f].UVIndices[k]], s);
            }

            return null;
        }
    }
}
