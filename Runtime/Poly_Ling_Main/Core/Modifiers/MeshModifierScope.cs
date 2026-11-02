// MeshModifierScope.cs
// 処理列の項目の処理範囲。保存する形（ID で持つ）と、評価のときに入力のメッシュへ引き直した形。
// Runtime/Poly_Ling_Main/Core/Modifiers/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.3（段 1）
//
// 【持ち方】
//   All      … 全体
//   Ids      … 保存した組。頂点は頂点 ID、辺は両端の頂点 ID の対、
//              面は「面ごとの頂点 ID の並び（平ら）」と「面ごとの頂点数」の 2 本。
//              頂点番号・選択には依存しない。
//   PartsSet … パーツ選択辞書（MeshContext.PartsSelectionSetList）を名前で指す。
//              評価のたびに複製へ ResolveByIds を掛けて引き直す（利用者の辞書は書き換えない）。
//
// 【ID の重複】
//   頂点 ID は 1 オブジェクト内で重複してよい（PartsSelectionSet.ResolveByVertexId の注記）。
//   引き当てで複数当たったら全件を採る。辺は当たった組のうち実際に面の辺であるものだけ、
//   面は頂点 ID の集まりが一致する面を全件採る。
//
// 【ID の無いもの】
//   ID を控えていない要素（未設定 ID の頂点、控えの無いパーツ選択辞書の要素）は範囲に入れない。
//   番号のまま使うと、頂点の並びが変わったときに別の頂点を指すため。件数は NoId に数える。
//
// 【引数の文字列】
//   キーは "scope." で始める（PanelCommandFactory の入れ子のキーと同じドット区切り）。
//   値の文字列は PanelCommandFactory.TryFormatValue / TryParseValue と同じ規則。
//   書き出しは MeshModifierRegistry.ToArgs / FromArgs が行う。

using System;
using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Modifiers
{
    /// <summary>処理範囲の種類。</summary>
    public enum MeshModifierScopeKind
    {
        All      = 0,
        Ids      = 1,
        PartsSet = 2,
    }

    /// <summary>保存する処理範囲。</summary>
    public sealed class MeshModifierScope
    {
        public const string KeyPrefix        = "scope.";
        public const string KeyKind          = "scope.kind";
        public const string KeyVertexIds     = "scope.vertexIds";
        public const string KeyEdgeVertexIds = "scope.edgeVertexIds";
        public const string KeyFaceVertexIds = "scope.faceVertexIds";
        public const string KeyFaceSizes     = "scope.faceSizes";
        public const string KeyPartsSet      = "scope.partsSet";

        public MeshModifierScopeKind Kind = MeshModifierScopeKind.All;

        /// <summary>Ids のとき、頂点の頂点 ID。</summary>
        public int[] VertexIds = Array.Empty<int>();

        /// <summary>Ids のとき、辺の両端の頂点 ID を 2 個ずつ。</summary>
        public int[] EdgeVertexIds = Array.Empty<int>();

        /// <summary>Ids のとき、面の頂点 ID を面の順に平らに並べたもの。</summary>
        public int[] FaceVertexIds = Array.Empty<int>();

        /// <summary>Ids のとき、面ごとの頂点数。FaceVertexIds の区切り。</summary>
        public int[] FaceSizes = Array.Empty<int>();

        /// <summary>PartsSet のとき、パーツ選択辞書の名前。</summary>
        public string PartsSetName = "";

        public static MeshModifierScope All() => new MeshModifierScope();

        public static MeshModifierScope PartsSet(string name)
            => new MeshModifierScope { Kind = MeshModifierScopeKind.PartsSet, PartsSetName = name ?? "" };

        public MeshModifierScope Clone() => new MeshModifierScope
        {
            Kind          = Kind,
            VertexIds     = (int[])VertexIds.Clone(),
            EdgeVertexIds = (int[])EdgeVertexIds.Clone(),
            FaceVertexIds = (int[])FaceVertexIds.Clone(),
            FaceSizes     = (int[])FaceSizes.Clone(),
            PartsSetName  = PartsSetName,
        };

        /// <summary>
        /// メッシュの索引の組（選択など）から、ID で持つ範囲を作る。
        /// 未設定 ID を含む要素は控えない（noId に数える）。
        /// </summary>
        public static MeshModifierScope FromIndices(
            MeshObject mesh, IEnumerable<int> vertices, IEnumerable<VertexPair> edges,
            IEnumerable<int> faces, out int noId)
        {
            noId = 0;
            var s = new MeshModifierScope { Kind = MeshModifierScopeKind.Ids };
            if (mesh == null) return s;

            var vl = mesh.Vertices;
            bool ValidV(int i) => i >= 0 && i < vl.Count;

            var vIds = new List<int>();
            if (vertices != null)
                foreach (int i in vertices)
                {
                    if (!ValidV(i) || MeshObject.IsUnsetId(vl[i].Id)) { noId++; continue; }
                    vIds.Add(vl[i].Id);
                }

            var eIds = new List<int>();
            if (edges != null)
                foreach (var e in edges)
                {
                    if (!ValidV(e.V1) || !ValidV(e.V2) ||
                        MeshObject.IsUnsetId(vl[e.V1].Id) || MeshObject.IsUnsetId(vl[e.V2].Id))
                    { noId++; continue; }
                    eIds.Add(vl[e.V1].Id);
                    eIds.Add(vl[e.V2].Id);
                }

            var fIds  = new List<int>();
            var fSize = new List<int>();
            if (faces != null)
                foreach (int fi in faces)
                {
                    if (fi < 0 || fi >= mesh.Faces.Count) { noId++; continue; }
                    var idx = mesh.Faces[fi].VertexIndices;
                    bool ok = idx.Count > 0;
                    foreach (int vi in idx)
                        if (!ValidV(vi) || MeshObject.IsUnsetId(vl[vi].Id)) { ok = false; break; }
                    if (!ok) { noId++; continue; }
                    foreach (int vi in idx) fIds.Add(vl[vi].Id);
                    fSize.Add(idx.Count);
                }

            s.VertexIds     = vIds.ToArray();
            s.EdgeVertexIds = eIds.ToArray();
            s.FaceVertexIds = fIds.ToArray();
            s.FaceSizes     = fSize.ToArray();
            return s;
        }

        // ================================================================
        // 引き直し
        // ================================================================

        /// <summary>
        /// 入力のメッシュへ引き直す。partsSets は入力の描画オブジェクトのパーツ選択辞書。
        /// 失敗（辞書が見つからない・控えが無い・形が壊れている）なら false と理由。
        /// </summary>
        public bool TryResolve(
            MeshObject mesh, IReadOnlyList<PartsSelectionSet> partsSets,
            out ResolvedMeshModifierScope resolved, out string error)
        {
            resolved = null;
            error    = null;
            if (mesh == null) { error = "入力のメッシュが無い"; return false; }

            switch (Kind)
            {
                case MeshModifierScopeKind.All:
                    resolved = ResolvedMeshModifierScope.CreateAll();
                    return true;

                case MeshModifierScopeKind.Ids:
                    return TryResolveIds(mesh, out resolved, out error);

                case MeshModifierScopeKind.PartsSet:
                    return TryResolvePartsSet(mesh, partsSets, out resolved, out error);
            }
            error = $"範囲の種類 {Kind} は扱えない";
            return false;
        }

        private bool TryResolveIds(MeshObject mesh, out ResolvedMeshModifierScope r, out string error)
        {
            r     = null;
            error = null;

            if ((EdgeVertexIds.Length % 2) != 0)
            { error = $"{KeyEdgeVertexIds} の要素が 2 の倍数でない（{EdgeVertexIds.Length} 個）"; return false; }
            int sum = 0;
            foreach (int n in FaceSizes)
            {
                if (n <= 0) { error = $"{KeyFaceSizes} に 0 以下の値がある"; return false; }
                sum += n;
            }
            if (sum != FaceVertexIds.Length)
            { error = $"{KeyFaceSizes} の合計（{sum}）が {KeyFaceVertexIds} の個数（{FaceVertexIds.Length}）と合わない"; return false; }

            r = new ResolvedMeshModifierScope();
            var byId = BuildVertexIdIndex(mesh);

            // 頂点
            foreach (int id in VertexIds)
            {
                if (byId.TryGetValue(id, out var hits)) { r.Vertices.UnionWith(hits); r.Resolved++; }
                else r.Lost++;
            }

            // 辺（当たった組のうち、実際に面の辺であるものだけ）
            if (EdgeVertexIds.Length > 0)
            {
                var meshEdges = BuildEdgeSet(mesh);
                for (int k = 0; k < EdgeVertexIds.Length; k += 2)
                {
                    bool any = false;
                    if (byId.TryGetValue(EdgeVertexIds[k], out var ha) &&
                        byId.TryGetValue(EdgeVertexIds[k + 1], out var hb))
                    {
                        foreach (int a in ha)
                            foreach (int b in hb)
                            {
                                if (a == b) continue;
                                var e = new VertexPair(a, b);
                                if (!meshEdges.Contains(e)) continue;
                                r.Edges.Add(e);
                                any = true;
                            }
                    }
                    if (any) r.Resolved++; else r.Lost++;
                }
            }

            // 面（頂点 ID の集まりが一致する面）
            if (FaceSizes.Length > 0)
            {
                var byKey = new Dictionary<string, List<int>>();
                var vl = mesh.Vertices;
                for (int fi = 0; fi < mesh.Faces.Count; fi++)
                {
                    var idx = mesh.Faces[fi].VertexIndices;
                    var ids = new int[idx.Count];
                    bool ok = idx.Count > 0;
                    for (int j = 0; j < idx.Count && ok; j++)
                    {
                        int vi = idx[j];
                        if (vi < 0 || vi >= vl.Count || MeshObject.IsUnsetId(vl[vi].Id)) { ok = false; break; }
                        ids[j] = vl[vi].Id;
                    }
                    if (!ok) continue;
                    string key = FaceKey(ids, 0, ids.Length);
                    if (!byKey.TryGetValue(key, out var list)) { list = new List<int>(); byKey[key] = list; }
                    list.Add(fi);
                }

                int pos = 0;
                foreach (int n in FaceSizes)
                {
                    string key = FaceKey(FaceVertexIds, pos, n);
                    pos += n;
                    if (byKey.TryGetValue(key, out var hits)) { r.Faces.UnionWith(hits); r.Resolved++; }
                    else r.Lost++;
                }
            }

            r.FinishAffected(mesh);
            return true;
        }

        private bool TryResolvePartsSet(
            MeshObject mesh, IReadOnlyList<PartsSelectionSet> partsSets,
            out ResolvedMeshModifierScope r, out string error)
        {
            r     = null;
            error = null;

            if (string.IsNullOrEmpty(PartsSetName))
            { error = $"{KeyPartsSet} が空"; return false; }

            PartsSelectionSet src = null;
            if (partsSets != null)
                foreach (var s in partsSets)
                    if (s != null && s.Name == PartsSetName) { src = s; break; }
            if (src == null)
            { error = $"パーツ選択辞書 \"{PartsSetName}\" が無い"; return false; }

            // 利用者の辞書を書き換えないよう複製に掛ける。
            var set = src.Clone();
            if (!set.ResolveByIds(mesh, out int resolvedCount, out int lostCount))
            { error = $"パーツ選択辞書 \"{PartsSetName}\" に ID の控えが無い"; return false; }

            r = new ResolvedMeshModifierScope { Resolved = resolvedCount, Lost = lostCount };
            int vc = mesh.Vertices.Count;
            int fc = mesh.Faces.Count;

            // 引き直した後でも、控えの無い要素は元の番号のまま残る（ResolveByIds の仕様）。
            // 控えのある要素だけを採る。
            if (set.HasResolvableVertexIds)
            {
                foreach (int v in set.Vertices)
                    if (v >= 0 && v < vc) r.Vertices.Add(v);
            }
            else r.NoId += set.Vertices.Count;

            foreach (var e in set.Edges)
            {
                if (set.EdgeVertexIds.ContainsKey(e) && e.V1 >= 0 && e.V2 < vc) r.Edges.Add(e);
                else r.NoId++;
            }
            foreach (int f in set.Faces)
            {
                if (set.FaceIds.ContainsKey(f) && f >= 0 && f < fc) r.Faces.Add(f);
                else r.NoId++;
            }
            // 線分（2 頂点の面）は面の組に入れず、頂点だけを範囲に入れる。
            foreach (int l in set.Lines)
            {
                if (!set.LineIds.ContainsKey(l) || l < 0 || l >= fc) { r.NoId++; continue; }
                foreach (int vi in mesh.Faces[l].VertexIndices)
                    if (vi >= 0 && vi < vc) r.Vertices.Add(vi);
            }

            r.FinishAffected(mesh);
            return true;
        }

        private static Dictionary<int, List<int>> BuildVertexIdIndex(MeshObject mesh)
        {
            var byId = new Dictionary<int, List<int>>();
            var vl = mesh.Vertices;
            for (int i = 0; i < vl.Count; i++)
            {
                int id = vl[i].Id;
                if (MeshObject.IsUnsetId(id)) continue;
                if (!byId.TryGetValue(id, out var bucket)) { bucket = new List<int>(); byId[id] = bucket; }
                bucket.Add(i);
            }
            return byId;
        }

        private static HashSet<VertexPair> BuildEdgeSet(MeshObject mesh)
        {
            var set = new HashSet<VertexPair>();
            foreach (var f in mesh.Faces)
            {
                var idx = f.VertexIndices;
                int n = idx.Count;
                if (n < 2) continue;
                if (n == 2) { set.Add(new VertexPair(idx[0], idx[1])); continue; }
                for (int j = 0; j < n; j++) set.Add(new VertexPair(idx[j], idx[(j + 1) % n]));
            }
            return set;
        }

        /// <summary>面の頂点 ID の集まりのキー（並び順・始点によらない）。</summary>
        private static string FaceKey(int[] ids, int start, int count)
        {
            var a = new int[count];
            Array.Copy(ids, start, a, 0, count);
            Array.Sort(a);
            return string.Join(",", a);
        }
    }

    /// <summary>入力のメッシュへ引き直した処理範囲。索引はそのメッシュの番号。</summary>
    public sealed class ResolvedMeshModifierScope
    {
        /// <summary>全体か。true のとき下の集合は空で、Contains は常に true。</summary>
        public bool IsAll { get; private set; }

        public HashSet<int>        Vertices = new HashSet<int>();
        public HashSet<VertexPair> Edges    = new HashSet<VertexPair>();
        public HashSet<int>        Faces    = new HashSet<int>();

        /// <summary>範囲が触れる頂点（頂点・辺の両端・面の頂点の和）。IsAll のときは空。</summary>
        public HashSet<int> AffectedVertices = new HashSet<int>();

        /// <summary>引き当てた控えの件数。</summary>
        public int Resolved;
        /// <summary>引き当てられなかった控えの件数。</summary>
        public int Lost;
        /// <summary>ID の控えが無いので範囲に入れなかった要素の件数。</summary>
        public int NoId;

        public static ResolvedMeshModifierScope CreateAll() => new ResolvedMeshModifierScope { IsAll = true };

        /// <summary>頂点が範囲に入るか。</summary>
        public bool ContainsVertex(int v) => IsAll || AffectedVertices.Contains(v);

        internal void FinishAffected(MeshObject mesh)
        {
            AffectedVertices.Clear();
            AffectedVertices.UnionWith(Vertices);
            foreach (var e in Edges) { AffectedVertices.Add(e.V1); AffectedVertices.Add(e.V2); }
            if (mesh != null)
                foreach (int f in Faces)
                    if (f >= 0 && f < mesh.Faces.Count)
                        foreach (int vi in mesh.Faces[f].VertexIndices) AffectedVertices.Add(vi);
        }

        public string Summary
            => IsAll ? "all"
               : $"V:{Vertices.Count} E:{Edges.Count} F:{Faces.Count} affected:{AffectedVertices.Count} resolved:{Resolved} lost:{Lost} noId:{NoId}";
    }
}
