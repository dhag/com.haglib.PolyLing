// RawDataOps.cs
// 生データ（座標・UV・法線・ID・面・ウェイト）の取り出しと書き戻し。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【原則の例外】
//   量のあるものは回線に乗せない、が PolyLing の原則
//   （PLDataStore.cs の冒頭注記）。GetRawData / SetRawData の 2 本だけは
//   生データそのものを運ぶ。取る種別と範囲を絞って呼ぶこと。
//
// 【平坦化】
//   可変長のものは「個数の列」と「連結した値の列」の 2 本で持つ。
//   入れ子の配列を送れないため（JsonParser.ParseFlat が '[' で始まる値を
//   捨てる。RemoteProtocol.cs:227）、送信側と形をそろえてある。
//
// 【書き戻しは位相を変えない】
//   数が合わなければ拒否する。面の作り替えは位相変更のコマンドを使う。
//
// 【安定 ID】
//   ObjectId は DateTime.UtcNow.Ticks から採番する（ObjectIdAllocator.cs:32）ので
//   10^17 台になり double では表せない。10 進の文字列で返す。

using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    /// <summary>生データの取り出しと書き戻し。状態を持たない。</summary>
    internal static class RawDataOps
    {
        // ================================================================
        // 対象の解決
        // ================================================================

        /// <summary>
        /// 範囲から対象の描画オブジェクトを決める。
        /// SelectedParts のときは頂点・面の絞り込みも返す（null なら絞り込みなし）。
        /// </summary>
        /// <param name="vertexFilter">対象にする頂点番号。null なら全部</param>
        /// <param name="faceFilter">対象にする面番号。null なら全部</param>
        public static bool TryResolve(
            ModelContext model, PLRawScope scope, int masterIndex, string setName,
            out List<int> targets,
            out HashSet<int> vertexFilter,
            out HashSet<int> faceFilter,
            out string reason)
        {
            targets      = new List<int>();
            vertexFilter = null;
            faceFilter   = null;
            reason       = null;

            if (model == null) { reason = "no model"; return false; }

            // 辞書指定があるときは、選択の代わりに結果辞書の項目を使う。
            PLDataEntry entry = null;
            if (!string.IsNullOrEmpty(setName))
            {
                entry = model.DataStore.Find(setName);
                if (entry == null) { reason = $"結果辞書に項目がありません: {setName}"; return false; }
            }

            switch (scope)
            {
                case PLRawScope.Object:
                {
                    if (!IsDrawable(model, masterIndex))
                    { reason = $"masterIndex {masterIndex} は描画オブジェクトではありません"; return false; }

                    targets.Add(masterIndex);
                    return true;
                }

                case PLRawScope.Model:
                {
                    foreach (var ent in model.DrawableMeshes) targets.Add(ent.MasterIndex);
                    if (targets.Count == 0) { reason = "描画オブジェクトがありません"; return false; }
                    return true;
                }

                case PLRawScope.SelectedObjects:
                {
                    if (entry != null)
                    {
                        // 辞書の項目は対象を 1 個持つ。-1 はモデル全体の意味なので拒否する。
                        if (entry.MasterIndex < 0)
                        { reason = $"項目 {setName} は対象の描画オブジェクトを持ちません"; return false; }
                        if (!IsDrawable(model, entry.MasterIndex))
                        { reason = $"項目 {setName} の masterIndex {entry.MasterIndex} は描画オブジェクトではありません"; return false; }

                        targets.Add(entry.MasterIndex);
                        return true;
                    }

                    foreach (int idx in model.SelectedDrawableMeshIndices)
                        if (IsDrawable(model, idx)) targets.Add(idx);

                    if (targets.Count == 0) { reason = "描画オブジェクトが選択されていません"; return false; }
                    return true;
                }

                case PLRawScope.SelectedParts:
                {
                    if (entry != null)
                    {
                        if (entry.Kind != PLDataKind.IndexSet || entry.IndexSet == null)
                        { reason = $"項目 {setName} は IndexSet ではありません"; return false; }
                        if (!IsDrawable(model, entry.MasterIndex))
                        { reason = $"項目 {setName} の masterIndex {entry.MasterIndex} は描画オブジェクトではありません"; return false; }

                        targets.Add(entry.MasterIndex);
                        vertexFilter = new HashSet<int>(entry.IndexSet.Vertices);
                        faceFilter   = new HashSet<int>(entry.IndexSet.Faces);

                        // 辺だけを選んだ集合でも頂点として拾えるようにする。
                        foreach (var e in entry.IndexSet.Edges)
                        { vertexFilter.Add(e.V1); vertexFilter.Add(e.V2); }

                        if (vertexFilter.Count == 0 && faceFilter.Count == 0)
                        { reason = $"項目 {setName} は空です"; return false; }
                        return true;
                    }

                    // 現在の選択。要素選択を持つ描画オブジェクトだけを対象にする。
                    var verts = new HashSet<int>();
                    var faces = new HashSet<int>();

                    foreach (var ent in model.DrawableMeshes)
                    {
                        var sel = ent.Context?.Selection;
                        if (sel == null) continue;
                        if (sel.Vertices.Count == 0 && sel.Faces.Count == 0 && sel.Edges.Count == 0) continue;

                        // 絞り込みは頂点番号なので、対象が複数あると番号空間が混ざる。
                        if (targets.Count > 0)
                        { reason = "要素選択を持つ描画オブジェクトが複数あります。1 個に絞ってください"; return false; }

                        targets.Add(ent.MasterIndex);
                        foreach (int v in sel.Vertices) verts.Add(v);
                        foreach (int f in sel.Faces)    faces.Add(f);
                        foreach (var e in sel.Edges)    { verts.Add(e.V1); verts.Add(e.V2); }
                    }

                    if (targets.Count == 0) { reason = "要素が選択されていません"; return false; }

                    vertexFilter = verts;
                    faceFilter   = faces;
                    return true;
                }

                default:
                    reason = $"未対応の範囲: {scope}";
                    return false;
            }
        }

        private static bool IsDrawable(ModelContext model, int masterIndex)
        {
            if (model == null || masterIndex < 0) return false;
            foreach (var ent in model.DrawableMeshes)
                if (ent.MasterIndex == masterIndex) return true;
            return false;
        }

        // ================================================================
        // 取り出し
        // ================================================================

        /// <summary>生データを取り出して戻り値の JSON にする。</summary>
        public static string BuildGet(
            ModelContext model, GetRawDataCommand cmd,
            List<int> targets, HashSet<int> vertexFilter, HashSet<int> faceFilter)
        {
            var masterIndices = new List<int>();
            var objectIds     = new List<string>();
            var names         = new List<string>();
            var vertexCounts  = new List<int>();
            var faceCounts    = new List<int>();

            var vertexIndices = new List<int>();
            var faceIndices   = new List<int>();

            var positions   = new List<float>();
            var vertexIds   = new List<int>();
            var vertexFlags = new List<int>();
            var uvCounts    = new List<int>();
            var uvs         = new List<float>();
            var normalCount = new List<int>();
            var normals     = new List<float>();
            var weightHas   = new List<int>();
            var weightBones = new List<int>();
            var weightVals  = new List<float>();

            var faceSizes     = new List<int>();
            var faceVertices  = new List<int>();
            var faceUVs       = new List<int>();
            var faceNormals   = new List<int>();
            var faceMaterials = new List<int>();
            var faceFlags     = new List<int>();

            bool truncated = false;
            int limit  = cmd.Limit  > 0 ? cmd.Limit  : int.MaxValue;
            int offset = cmd.Offset > 0 ? cmd.Offset : 0;

            foreach (int mi in targets)
            {
                var mc   = model.GetMeshContext(mi);
                var mesh = mc?.MeshObject;
                if (mesh == null) continue;

                masterIndices.Add(mi);
                objectIds.Add(IdText(mc.ObjectId));
                names.Add(mc.Name ?? "");

                // ── 頂点
                var picked = CollectIndices(mesh.Vertices.Count, vertexFilter, offset, limit, ref truncated);
                vertexCounts.Add(picked.Count);

                foreach (int vi in picked)
                {
                    vertexIndices.Add(vi);
                    var v = mesh.Vertices[vi];

                    if (cmd.IncludePositions)
                    {
                        Vector3 p = v != null ? v.Position : Vector3.zero;
                        positions.Add(p.x); positions.Add(p.y); positions.Add(p.z);
                    }

                    if (cmd.IncludeIds)
                    {
                        vertexIds.Add(v?.Id ?? 0);
                        vertexIds.Add(v?.PartsId ?? 0);
                        vertexIds.Add(v?.SubId ?? 0);
                    }

                    if (cmd.IncludeFlags)
                        vertexFlags.Add(v != null ? (int)v.Flags : 0);

                    if (cmd.IncludeUVs)
                    {
                        var list = v?.UVs;
                        int n = list?.Count ?? 0;
                        uvCounts.Add(n);
                        for (int k = 0; k < n; k++) { uvs.Add(list[k].x); uvs.Add(list[k].y); }
                    }

                    if (cmd.IncludeNormals)
                    {
                        var list = v?.Normals;
                        int n = list?.Count ?? 0;
                        normalCount.Add(n);
                        for (int k = 0; k < n; k++)
                        { normals.Add(list[k].x); normals.Add(list[k].y); normals.Add(list[k].z); }
                    }

                    if (cmd.IncludeWeights)
                    {
                        bool has = v != null && v.HasBoneWeight;
                        weightHas.Add(has ? 1 : 0);

                        var w = has ? v.BoneWeight.Value : default(BoneWeight);
                        weightBones.Add(w.boneIndex0); weightBones.Add(w.boneIndex1);
                        weightBones.Add(w.boneIndex2); weightBones.Add(w.boneIndex3);
                        weightVals.Add(w.weight0); weightVals.Add(w.weight1);
                        weightVals.Add(w.weight2); weightVals.Add(w.weight3);
                    }
                }

                // ── 面
                if (!cmd.IncludeFaces) { faceCounts.Add(0); continue; }

                var pickedFaces = CollectIndices(mesh.Faces.Count, faceFilter, offset, limit, ref truncated);
                faceCounts.Add(pickedFaces.Count);

                foreach (int fi in pickedFaces)
                {
                    faceIndices.Add(fi);
                    var f = mesh.Faces[fi];

                    int n = f?.VertexIndices?.Count ?? 0;
                    faceSizes.Add(n);
                    for (int k = 0; k < n; k++)
                    {
                        faceVertices.Add(f.VertexIndices[k]);
                        faceUVs.Add(k < f.UVIndices.Count ? f.UVIndices[k] : 0);
                        faceNormals.Add(k < f.NormalIndices.Count ? f.NormalIndices[k] : 0);
                    }

                    faceMaterials.Add(f?.MaterialIndex ?? 0);
                    if (cmd.IncludeFlags) faceFlags.Add(f != null ? (int)f.Flags : 0);
                }
            }

            return CommandDataJson.New()
                .Int("objects",          masterIndices.Count)
                .Ints("masterIndices",   masterIndices)
                .Texts("objectIds",      objectIds)
                .Texts("names",          names)
                .Ints("vertexCounts",    vertexCounts)
                .Ints("faceCounts",      faceCounts)
                .Ints("vertexIndices",   vertexIndices)
                .Ints("faceIndices",     faceIndices)
                .Nums("positions",       positions)
                .Ints("vertexIds",       vertexIds)
                .Ints("vertexFlags",     vertexFlags)
                .Ints("uvCounts",        uvCounts)
                .Nums("uvs",             uvs)
                .Ints("normalCounts",    normalCount)
                .Nums("normals",         normals)
                .Ints("weightHas",       weightHas)
                .Ints("weightBones",     weightBones)
                .Nums("weightValues",    weightVals)
                .Ints("faceSizes",       faceSizes)
                .Ints("faceVertices",    faceVertices)
                .Ints("faceUVs",         faceUVs)
                .Ints("faceNormals",     faceNormals)
                .Ints("faceMaterials",   faceMaterials)
                .Ints("faceFlags",       faceFlags)
                .Flag("truncated",       truncated)
                .Build();
        }

        /// <summary>
        /// 返す番号を集める。filter が null なら 0..count-1 を、あれば昇順に並べたものを、
        /// それぞれ offset から limit 個だけ取る。
        /// </summary>
        private static List<int> CollectIndices(
            int count, HashSet<int> filter, int offset, int limit, ref bool truncated)
        {
            var all = new List<int>();

            if (filter == null)
            {
                for (int i = 0; i < count; i++) all.Add(i);
            }
            else
            {
                foreach (int i in filter)
                    if (i >= 0 && i < count) all.Add(i);
                all.Sort();
            }

            if (offset >= all.Count) return new List<int>();

            int take = all.Count - offset;
            if (take > limit) { take = limit; truncated = true; }

            return all.GetRange(offset, take);
        }

        // ================================================================
        // 書き戻し
        // ================================================================

        /// <summary>
        /// 生データを書き戻す。位相は変えない。数が合わなければ false を返す。
        /// 呼び出し側は false のとき何も変わっていないことを前提にしてよい
        /// （検証をすべて済ませてから書く）。
        /// </summary>
        public static bool TryApplySet(
            ModelContext model, SetRawDataCommand cmd,
            List<int> targets, HashSet<int> vertexFilter, HashSet<int> faceFilter,
            out string data, out string reason)
        {
            data   = null;
            reason = null;

            if (targets.Count != 1)
            { reason = $"書き戻しの対象は 1 個だけです。解決した数: {targets.Count}"; return false; }

            int mi   = targets[0];
            var mc   = model.GetMeshContext(mi);
            var mesh = mc?.MeshObject;
            if (mesh == null) { reason = $"masterIndex {mi} にメッシュがありません"; return false; }

            // ── 対象の頂点番号を決める
            List<int> vi;
            if (cmd.VertexIndices != null && cmd.VertexIndices.Length > 0)
                vi = new List<int>(cmd.VertexIndices);
            else
            {
                bool dummy = false;
                vi = CollectIndices(mesh.Vertices.Count, vertexFilter, 0, int.MaxValue, ref dummy);
            }

            foreach (int i in vi)
                if (i < 0 || i >= mesh.Vertices.Count)
                { reason = $"頂点番号が範囲外です: {i}（頂点数 {mesh.Vertices.Count}）"; return false; }

            int vn = vi.Count;

            // ── 長さの検証（書く前にすべて見る）
            if (!CheckLen(cmd.Positions,   vn * 3, "positions",   out reason)) return false;
            if (!CheckLen(cmd.VertexIds,   vn * 3, "vertexIds",   out reason)) return false;
            if (!CheckLen(cmd.VertexFlags, vn,     "vertexFlags", out reason)) return false;
            if (!CheckLen(cmd.WeightHas,   vn,     "weightHas",   out reason)) return false;
            if (!CheckLen(cmd.WeightBones, vn * 4, "weightBones", out reason)) return false;
            if (!CheckLen(cmd.WeightValues, vn * 4, "weightValues", out reason)) return false;
            if (!CheckLen(cmd.UvCounts,     vn,    "uvCounts",     out reason)) return false;
            if (!CheckLen(cmd.NormalCounts, vn,    "normalCounts", out reason)) return false;

            bool writeUVs = cmd.Uvs != null && cmd.Uvs.Length > 0;
            if (writeUVs)
            {
                if (cmd.UvCounts == null || cmd.UvCounts.Length != vn)
                { reason = "uvs を送るときは uvCounts も同じ頂点数ぶん要ります"; return false; }

                int sum = 0;
                foreach (int c in cmd.UvCounts)
                {
                    if (c < 0) { reason = "uvCounts に負の値があります"; return false; }
                    sum += c;
                }
                if (cmd.Uvs.Length != sum * 2)
                { reason = $"uvs の長さが合いません。要 {sum * 2} / 実 {cmd.Uvs.Length}"; return false; }
            }

            bool writeNormals = cmd.Normals != null && cmd.Normals.Length > 0;
            if (writeNormals)
            {
                if (cmd.NormalCounts == null || cmd.NormalCounts.Length != vn)
                { reason = "normals を送るときは normalCounts も同じ頂点数ぶん要ります"; return false; }

                int sum = 0;
                foreach (int c in cmd.NormalCounts)
                {
                    if (c < 0) { reason = "normalCounts に負の値があります"; return false; }
                    sum += c;
                }
                if (cmd.Normals.Length != sum * 3)
                { reason = $"normals の長さが合いません。要 {sum * 3} / 実 {cmd.Normals.Length}"; return false; }
            }

            // ── 面
            List<int> fi;
            if (cmd.FaceIndices != null && cmd.FaceIndices.Length > 0)
                fi = new List<int>(cmd.FaceIndices);
            else
                fi = new List<int>();

            foreach (int i in fi)
                if (i < 0 || i >= mesh.Faces.Count)
                { reason = $"面番号が範囲外です: {i}（面数 {mesh.Faces.Count}）"; return false; }

            int fn = fi.Count;
            if (!CheckLen(cmd.FaceMaterials, fn, "faceMaterials", out reason)) return false;
            if (!CheckLen(cmd.FaceFlags,     fn, "faceFlags",     out reason)) return false;

            // UV スロット数を減らすと面の UVIndices が範囲外を指しうる。
            // 位相を壊さないため、面が指している最大スロット番号を下回る変更は拒否する。
            if (writeUVs && !CheckSlotShrink(mesh, vi, cmd.UvCounts, useUV: true, out reason)) return false;
            if (writeNormals && !CheckSlotShrink(mesh, vi, cmd.NormalCounts, useUV: false, out reason)) return false;

            // ── ここから書く
            int uvCursor = 0;
            int nmCursor = 0;

            for (int k = 0; k < vn; k++)
            {
                var v = mesh.Vertices[vi[k]];
                if (v == null) continue;

                if (cmd.Positions != null && cmd.Positions.Length > 0)
                    v.Position = new Vector3(
                        cmd.Positions[k * 3], cmd.Positions[k * 3 + 1], cmd.Positions[k * 3 + 2]);

                if (cmd.VertexIds != null && cmd.VertexIds.Length > 0)
                {
                    v.Id      = cmd.VertexIds[k * 3];
                    v.PartsId = cmd.VertexIds[k * 3 + 1];
                    v.SubId   = cmd.VertexIds[k * 3 + 2];
                }

                if (cmd.VertexFlags != null && cmd.VertexFlags.Length > 0)
                    v.Flags = (VertexFlags)cmd.VertexFlags[k];

                if (writeUVs)
                {
                    int n = cmd.UvCounts[k];
                    var list = new List<Vector2>(n);
                    for (int j = 0; j < n; j++)
                        list.Add(new Vector2(cmd.Uvs[uvCursor + j * 2], cmd.Uvs[uvCursor + j * 2 + 1]));
                    uvCursor += n * 2;
                    v.UVs = list;
                }

                if (writeNormals)
                {
                    int n = cmd.NormalCounts[k];
                    var list = new List<Vector3>(n);
                    for (int j = 0; j < n; j++)
                        list.Add(new Vector3(
                            cmd.Normals[nmCursor + j * 3],
                            cmd.Normals[nmCursor + j * 3 + 1],
                            cmd.Normals[nmCursor + j * 3 + 2]));
                    nmCursor += n * 3;
                    v.Normals = list;
                }

                if (cmd.WeightHas != null && cmd.WeightHas.Length > 0)
                {
                    if (cmd.WeightHas[k] == 0) v.BoneWeight = null;
                    else if (cmd.WeightBones != null && cmd.WeightValues != null
                             && cmd.WeightBones.Length > 0 && cmd.WeightValues.Length > 0)
                    {
                        v.BoneWeight = new BoneWeight
                        {
                            boneIndex0 = cmd.WeightBones[k * 4],
                            boneIndex1 = cmd.WeightBones[k * 4 + 1],
                            boneIndex2 = cmd.WeightBones[k * 4 + 2],
                            boneIndex3 = cmd.WeightBones[k * 4 + 3],
                            weight0    = cmd.WeightValues[k * 4],
                            weight1    = cmd.WeightValues[k * 4 + 1],
                            weight2    = cmd.WeightValues[k * 4 + 2],
                            weight3    = cmd.WeightValues[k * 4 + 3],
                        };
                    }
                }
            }

            for (int k = 0; k < fn; k++)
            {
                var f = mesh.Faces[fi[k]];
                if (f == null) continue;

                if (cmd.FaceMaterials != null && cmd.FaceMaterials.Length > 0)
                    f.MaterialIndex = cmd.FaceMaterials[k];

                if (cmd.FaceFlags != null && cmd.FaceFlags.Length > 0)
                    f.Flags = (FaceFlags)cmd.FaceFlags[k];
            }

            data = CommandDataJson.New()
                .Int("masterIndex",       mi)
                .Int("vertices",          vn)
                .Int("faces",             fn)
                .Flag("positionsWritten", cmd.Positions != null && cmd.Positions.Length > 0)
                .Flag("uvsWritten",       writeUVs)
                .Flag("normalsWritten",   writeNormals)
                .Flag("idsWritten",       cmd.VertexIds != null && cmd.VertexIds.Length > 0)
                .Flag("weightsWritten",   cmd.WeightHas != null && cmd.WeightHas.Length > 0)
                .Build();

            return true;
        }

        /// <summary>長さの検証。null・空は「送っていない」の意味で通す。</summary>
        private static bool CheckLen(System.Array a, int need, string name, out string reason)
        {
            reason = null;
            if (a == null || a.Length == 0) return true;
            if (a.Length == need) return true;

            reason = $"{name} の長さが合いません。要 {need} / 実 {a.Length}";
            return false;
        }

        /// <summary>
        /// スロット数を減らすと面の参照が外れるので、面が指している最大番号を
        /// 下回る変更を拒否する。Face.UVIndices[j] == Face.NormalIndices[j] の
        /// 不変条件を壊さないための門。
        /// </summary>
        private static bool CheckSlotShrink(
            MeshObject mesh, List<int> vi, int[] counts, bool useUV, out string reason)
        {
            reason = null;

            // 頂点番号 → 面が要求する最小スロット数
            var needed = new Dictionary<int, int>();
            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                var face = mesh.Faces[f];
                if (face?.VertexIndices == null) continue;

                var slots = useUV ? face.UVIndices : face.NormalIndices;
                if (slots == null) continue;

                int n = face.VertexIndices.Count;
                for (int j = 0; j < n && j < slots.Count; j++)
                {
                    int v = face.VertexIndices[j];
                    int s = slots[j] + 1;
                    if (!needed.TryGetValue(v, out int cur) || s > cur) needed[v] = s;
                }
            }

            for (int k = 0; k < vi.Count; k++)
            {
                if (!needed.TryGetValue(vi[k], out int need)) continue;
                if (counts[k] >= need) continue;

                reason = useUV
                    ? $"頂点 {vi[k]} の UV スロットを {counts[k]} へ減らすと面の参照が外れます（要 {need}）"
                    : $"頂点 {vi[k]} の法線スロットを {counts[k]} へ減らすと面の参照が外れます（要 {need}）";
                return false;
            }
            return true;
        }

        /// <summary>不変文化圏の 10 進表記。</summary>
        private static string IdText(ulong id)
            => id.ToString(CultureInfo.InvariantCulture);
    }
}
