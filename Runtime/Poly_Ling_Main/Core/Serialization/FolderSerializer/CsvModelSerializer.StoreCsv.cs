// CsvModelSerializer.StoreCsv.cs
// CSV モデル入出力：meshselsets.csv・datastore.csv・objectgroups.csv・mirrorpairs.csv。
// Runtime/Poly_Ling_Main/Core/Serialization/FolderSerializer/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Materials;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Serialization.FolderSerializer
{
    public static partial class CsvModelSerializer
    {
        // ================================================================
        // meshselsets.csv（メッシュ選択セット）
        // ================================================================

        private static void WriteMeshSelSetsCsv(string folderPath, ModelContext model)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_MeshSelSets,version,1.0");

            foreach (var ms in model.MeshSelectionSets)
            {
                // name,category,nameCount,meshName0,meshName1,...
                sb.Append($"{Esc(ms.Name)},{ms.Category},{ms.MeshNames.Count}");
                foreach (var meshName in ms.MeshNames)
                {
                    sb.Append($",{Esc(meshName)}");
                }
                sb.AppendLine();
            }

            File.WriteAllText(Path.Combine(folderPath, "meshselsets.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void ReadMeshSelSetsCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "meshselsets.csv");
            if (!File.Exists(path)) return;

            model.MeshSelectionSets = new List<MeshSelectionSet>();

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 3) continue;

                var ms = new MeshSelectionSet(Unesc(cols[0]));

                if (Enum.TryParse<ModelContext.SelectionCategory>(cols[1], out var cat))
                    ms.Category = cat;

                int nameCount = PInt(cols, 2);
                for (int i = 0; i < nameCount; i++)
                {
                    int ci = 3 + i;
                    if (ci >= cols.Length) break;
                    string meshName = Unesc(cols[ci]);
                    if (!string.IsNullOrEmpty(meshName))
                        ms.MeshNames.Add(meshName);
                }

                model.MeshSelectionSets.Add(ms);
            }
        }

        // ================================================================
        // datastore.csv（コマンドが返した実データの辞書）
        //
        // 【行の形】
        //   e,name,kind,source,masterIndex,objectId,createdAt  … 項目 1 件の見出し
        //   s,mode,vCount,v...,eCount,e1,e2,...,fCount,f...,
        //     lCount,l...,idCount,idx,id,parts,sub,...          … IndexSet の中身
        //   l,vCount,cx,cy,cz,v0,v1,...                         … ループ 1 本
        //   v,key,isText,number,text                            … 値 1 件
        //
        //   s / l / v は直前の e に属する。項目 1 件がループを数十本持つので、
        //   1 行に詰めると列が伸びて読めない。objectgroups.csv と同じく
        //   種別を先頭列に置いて行を分ける。
        //
        // 【s 行の列並び】
        //   CsvMeshSerializer の ss 行から名前だけを外したもの。
        //   名前は e 行が持つので重複させない。並びを合わせてあるので、
        //   片方を直すときはもう片方も見ること。
        //
        // 【ObjectId を書く】
        //   参照は索引と ObjectId の両方で持つ。名前ベース保存でも
        //   ObjectId は 10 進のまま（objectgroups.csv と同じ）。
        // ================================================================

        private static void WriteOrDeleteDataStoreCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "datastore.csv");

            var store = model.DataStore;
            if (store == null || store.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_DataStore,version,1.0");

            foreach (var e in store.Entries)
            {
                if (e == null || string.IsNullOrEmpty(e.Name)) continue;

                sb.Append("e,").Append(Esc(e.Name)).Append(',').Append(e.Kind).Append(',')
                  .Append(Esc(e.Source)).Append(',').Append(e.MasterIndex).Append(',')
                  .Append(e.ObjectId.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(Esc(e.CreatedAt.ToString("o", CultureInfo.InvariantCulture)));
                sb.AppendLine();

                switch (e.Kind)
                {
                    case PLDataKind.IndexSet:
                        WriteDataStoreIndexSet(sb, e.IndexSet);
                        break;

                    case PLDataKind.LoopSet:
                        if (e.Loops != null)
                            foreach (var loop in e.Loops) WriteDataStoreLoop(sb, loop);
                        break;

                    case PLDataKind.ValueSet:
                        if (e.Values != null)
                            foreach (var v in e.Values) WriteDataStoreValue(sb, v);
                        break;
                }
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void WriteDataStoreIndexSet(StringBuilder sb, Poly_Ling.Selection.PartsSelectionSet ss)
        {
            if (ss == null) return;

            sb.Append("s,").Append(ss.Mode);

            sb.Append(',').Append(ss.Vertices.Count);
            foreach (var v in ss.Vertices) sb.Append(',').Append(v);

            sb.Append(',').Append(ss.Edges.Count);
            foreach (var e in ss.Edges) sb.Append(',').Append(e.V1).Append(',').Append(e.V2);

            sb.Append(',').Append(ss.Faces.Count);
            foreach (var f in ss.Faces) sb.Append(',').Append(f);

            sb.Append(',').Append(ss.Lines.Count);
            foreach (var l in ss.Lines) sb.Append(',').Append(l);

            var map = ss.VertexIds;
            if (map == null || map.Count == 0)
            {
                sb.Append(",0");
            }
            else
            {
                sb.Append(',').Append(map.Count);
                foreach (var kv in map)
                    sb.Append(',').Append(kv.Key).Append(',').Append(kv.Value.Id)
                      .Append(',').Append(kv.Value.PartsId).Append(',').Append(kv.Value.SubId);
            }

            sb.AppendLine();
        }

        private static void WriteDataStoreLoop(StringBuilder sb, PLDataLoop loop)
        {
            if (loop == null) return;

            int n = loop.Vertices?.Count ?? 0;
            sb.Append("l,").Append(n).Append(',')
              .Append(Fl(loop.Centroid.x)).Append(',')
              .Append(Fl(loop.Centroid.y)).Append(',')
              .Append(Fl(loop.Centroid.z));

            for (int i = 0; i < n; i++) sb.Append(',').Append(loop.Vertices[i]);

            sb.AppendLine();
        }

        private static void WriteDataStoreValue(StringBuilder sb, PLDataValue v)
        {
            sb.Append("v,").Append(Esc(v.Key)).Append(',').Append(v.IsText ? "true" : "false")
              .Append(',').Append(v.IsText
                  ? "0"
                  : v.Number.ToString("R", CultureInfo.InvariantCulture))
              .Append(',').Append(Esc(v.IsText ? (v.Text ?? "") : ""));
            sb.AppendLine();
        }

        private static void ReadDataStoreCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "datastore.csv");

            var store = model.DataStore;
            if (store == null) return;

            if (!File.Exists(path)) { store.Clear(); return; }

            var entries = new List<PLDataEntry>();
            PLDataEntry current = null;

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 1) continue;

                switch (cols[0])
                {
                    case "e":
                        current = ReadDataStoreEntryHead(cols);
                        if (current != null) entries.Add(current);
                        break;

                    case "s":
                        if (current != null && current.Kind == PLDataKind.IndexSet)
                            current.IndexSet = ReadDataStoreIndexSet(cols, current.Name);
                        break;

                    case "l":
                        if (current != null && current.Kind == PLDataKind.LoopSet)
                        {
                            if (current.Loops == null) current.Loops = new List<PLDataLoop>();
                            current.Loops.Add(ReadDataStoreLoop(cols));
                        }
                        break;

                    case "v":
                        if (current != null && current.Kind == PLDataKind.ValueSet)
                        {
                            if (current.Values == null) current.Values = new List<PLDataValue>();
                            current.Values.Add(ReadDataStoreValue(cols));
                        }
                        break;
                }
            }

            store.ReplaceAll(entries);
        }

        /// <summary>e 行から項目の見出しを作る。中身は後続の行が埋める。</summary>
        private static PLDataEntry ReadDataStoreEntryHead(string[] cols)
        {
            // e,name,kind,source,masterIndex,objectId,createdAt
            string name = Unesc(SafeGet(cols, 1));
            if (string.IsNullOrEmpty(name)) return null;

            var e = new PLDataEntry
            {
                Name        = name,
                Source      = Unesc(SafeGet(cols, 3)),
                MasterIndex = PInt(cols, 4, -1),
            };

            if (Enum.TryParse<PLDataKind>(SafeGet(cols, 2), out var kind))
                e.Kind = kind;

            if (ulong.TryParse(SafeGet(cols, 5), NumberStyles.Integer,
                               CultureInfo.InvariantCulture, out var oid))
                e.ObjectId = oid;

            string created = Unesc(SafeGet(cols, 6));
            if (!string.IsNullOrEmpty(created) &&
                DateTime.TryParse(created, CultureInfo.InvariantCulture,
                                  DateTimeStyles.RoundtripKind, out var dt))
                e.CreatedAt = dt;

            // 中身の入れ物は種類ぶんだけ先に用意する。s / l / v 行が
            // 1 本も無い（空の集合）ときでも null にしないため。
            switch (e.Kind)
            {
                case PLDataKind.IndexSet:
                    e.IndexSet = new Poly_Ling.Selection.PartsSelectionSet(name);
                    break;
                case PLDataKind.LoopSet:
                    e.Loops = new List<PLDataLoop>();
                    break;
                case PLDataKind.ValueSet:
                    e.Values = new List<PLDataValue>();
                    break;
            }

            return e;
        }

        /// <summary>s 行を読む。列並びは CsvMeshSerializer.ReadSelectionSet と同じ（名前だけ無い）。</summary>
        private static Poly_Ling.Selection.PartsSelectionSet ReadDataStoreIndexSet(string[] cols, string name)
        {
            var ss = new Poly_Ling.Selection.PartsSelectionSet(name ?? "");

            int idx = 1;
            if (Enum.TryParse<Poly_Ling.Selection.MeshSelectMode>(SafeGet(cols, idx), out var mode))
                ss.Mode = mode;
            idx++;

            int vCount = PInt(cols, idx++);
            for (int i = 0; i < vCount; i++) ss.Vertices.Add(PInt(cols, idx++));

            int eCount = PInt(cols, idx++);
            for (int i = 0; i < eCount; i++)
            {
                int v1 = PInt(cols, idx++);
                int v2 = PInt(cols, idx++);
                ss.Edges.Add(new Poly_Ling.Selection.VertexPair(v1, v2));
            }

            int fCount = PInt(cols, idx++);
            for (int i = 0; i < fCount; i++) ss.Faces.Add(PInt(cols, idx++));

            int lCount = PInt(cols, idx++);
            for (int i = 0; i < lCount; i++) ss.Lines.Add(PInt(cols, idx++));

            int idCount = PInt(cols, idx++);
            for (int i = 0; i < idCount; i++)
            {
                int vi    = PInt(cols, idx++);
                int id    = PInt(cols, idx++);
                int parts = PInt(cols, idx++);
                int sub   = PInt(cols, idx++);
                ss.VertexIds[vi] = new VertexIdTriple(id, parts, sub);
            }

            return ss;
        }

        /// <summary>l 行を読む。</summary>
        private static PLDataLoop ReadDataStoreLoop(string[] cols)
        {
            // l,vCount,cx,cy,cz,v0,v1,...
            var loop = new PLDataLoop();

            int n = PInt(cols, 1);
            loop.Centroid = new Vector3(PFl(cols, 2), PFl(cols, 3), PFl(cols, 4));

            for (int i = 0; i < n; i++)
            {
                int ci = 5 + i;
                if (ci >= cols.Length) break;
                loop.Vertices.Add(PInt(cols, ci));
            }

            return loop;
        }

        /// <summary>v 行を読む。</summary>
        private static PLDataValue ReadDataStoreValue(string[] cols)
        {
            // v,key,isText,number,text
            string key = Unesc(SafeGet(cols, 1));

            if (PBool(cols, 2))
                return PLDataValue.Str(key, Unesc(SafeGet(cols, 4)));

            double.TryParse(SafeGet(cols, 3), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double num);
            return PLDataValue.Num(key, num);
        }

        // ================================================================
        // objectgroups.csv（オブジェクトグループ）
        //
        // 行の形と版の扱いは ObjectGroupCsv.cs を正典とする。
        //
        // 【索引を書かない】
        //   参照は ObjectId（10 進）そのまま。名前ベース保存でも変換しない。
        //   ObjectId はリスト位置にも名前にも依存しないため、
        //   useNameBased の分岐が要らない。
        // ================================================================

        // 本文の組み立てと読み取りは ObjectGroupCsv が持つ。ここはファイルの
        // 入出力だけ。手本のグループ（ScenarioLibrary）が同じ形のファイルを
        // 読み書きするので、構文解析を 2 か所に置かない。
        private static void WriteObjectGroupsCsv(string folderPath, ModelContext model)
        {
            File.WriteAllText(
                Path.Combine(folderPath, "objectgroups.csv"),
                ObjectGroupCsv.Build(model.ObjectGroups),
                Encoding.UTF8);
        }

        private static void ReadObjectGroupsCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "objectgroups.csv");
            if (!File.Exists(path)) return;

            model.ObjectGroups = ObjectGroupCsv.Parse(File.ReadAllLines(path, Encoding.UTF8));
        }

        // ================================================================
        // mirrorpairs.csv（ミラーペア情報）
        // ================================================================

        private static void WriteMirrorPairsCsv(string folderPath, ModelContext model,
            bool useNameBased = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_MirrorPairs,version,1.0");

            foreach (var pair in model.MirrorPairs)
            {
                if (useNameBased)
                {
                    string realName = pair.Real?.Name ?? "";
                    string mirrorName = pair.Mirror?.Name ?? "";
                    if (string.IsNullOrEmpty(realName) || string.IsNullOrEmpty(mirrorName)) continue;
                    sb.AppendLine($"{Esc(realName)},{Esc(mirrorName)},{(int)pair.Axis}");
                }
                else
                {
                    int realIdx = model.MeshContextList.IndexOf(pair.Real);
                    int mirrorIdx = model.MeshContextList.IndexOf(pair.Mirror);
                    if (realIdx < 0 || mirrorIdx < 0) continue;
                    sb.AppendLine($"{realIdx},{mirrorIdx},{(int)pair.Axis}");
                }
            }

            File.WriteAllText(Path.Combine(folderPath, "mirrorpairs.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void ReadMirrorPairsCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "mirrorpairs.csv");
            if (!File.Exists(path)) return;

            model.MirrorPairs = new List<MirrorPair>();

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 3) continue;

                int axis = PInt(cols, 2, 0);
                MeshContext realCtx = null;
                MeshContext mirrorCtx = null;

                if (int.TryParse(cols[0], out int realIdx) && int.TryParse(cols[1], out int mirrorIdx))
                {
                    // インデックスベース
                    if (realIdx < 0 || realIdx >= model.Count) continue;
                    if (mirrorIdx < 0 || mirrorIdx >= model.Count) continue;
                    realCtx = model.GetMeshContext(realIdx);
                    mirrorCtx = model.GetMeshContext(mirrorIdx);
                }
                else
                {
                    // 名前ベース
                    string realName = Unesc(cols[0]);
                    string mirrorName = Unesc(cols[1]);
                    var nameToIndex = BuildNameToIndex(model);
                    if (nameToIndex.TryGetValue(realName, out int ri))
                        realCtx = model.GetMeshContext(ri);
                    if (nameToIndex.TryGetValue(mirrorName, out int mi))
                        mirrorCtx = model.GetMeshContext(mi);
                }

                if (realCtx == null || mirrorCtx == null) continue;

                var pair = new MirrorPair
                {
                    Real = realCtx,
                    Mirror = mirrorCtx,
                    Axis = (Poly_Ling.Symmetry.SymmetryAxis)axis
                };
                if (pair.Build())
                {
                    model.MirrorPairs.Add(pair);
                    Debug.Log($"[CsvModelSerializer] Restored MirrorPair: {realCtx.Name} ↔ {mirrorCtx.Name}");
                }
                else
                {
                    Debug.LogWarning($"[CsvModelSerializer] Failed to rebuild MirrorPair: {realCtx.Name} ↔ {mirrorCtx.Name}: {pair.BuildLog}");
                }
            }
        }
    }
}
