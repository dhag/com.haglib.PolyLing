// Runtime/Poly_Ling_Main/Core/Ops/PositionCsvNodeBuilder.cs
// ============================================================
// 位置付き CSV（名前・親・位置）から、ボーンまたは空の描画オブジェクトを作る
// ============================================================
//
// 【何をするか / しないか】
//   するのは「名前・親・位置の表から節を作って親子につなぐ」ことだけ。
//   メッシュの形・ウェイト・Humanoid 割当は付けない。
//   既にある名前は原点 CSV 読込（ApplyObjectOrigins）と違い、動かさずに新しく作る
//   （名前は一意化する）。
//
// 【表の書式】
//   先頭の空でない行（# / // / ; で始まる行は注釈として飛ばす）が見出し行。
//   列は見出しの名前で引く（大小文字は区別しない）。列の並びは問わない。
//   位置はワールド座標。回転は持たない（作る節の回転は 0）。
//
// 【親の決め方】
//   1. 親の列の名前が、今回作る行にあればそれ
//   2. モデルに既に同じ名前のオブジェクトがあればそれ（続きを足していけるように）
//   3. 表にはあるが今回作らない行（絞り込みで外れた行）なら、その行の親をさらに辿る
//   4. どれにも当たらなければ親なし
//
// 【座標】
//   ローカル位置 = 親のワールド行列の逆 × ワールド位置。
//   親が今回作る節なら、その節のワールド行列は「親の親の行列で回転・拡縮を受け継ぎ、
//   平行移動だけがワールド位置」になる（作る節の回転は 0 のため）。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>位置付き CSV を読んだ表。行は表の並び。</summary>
    public sealed class PositionCsvTable
    {
        public readonly List<string>  Names     = new List<string>();
        public readonly List<string>  Parents   = new List<string>();
        public readonly List<Vector3> Positions = new List<Vector3>();
        public int Count => Names.Count;
    }

    /// <summary>作った結果。</summary>
    public sealed class PositionCsvNodeResult
    {
        /// <summary>作った節の索引。作った順（親が先）。</summary>
        public readonly List<int> Created = new List<int>();

        /// <summary>モデルの既存名と重なって名前を変えた件数。</summary>
        public int Renamed;

        /// <summary>親が付かなかった節の数。</summary>
        public int Roots;

        /// <summary>モデルに既にあるオブジェクトを親にした節の数。</summary>
        public int AttachedToExisting;

        public string Message = "";
    }

    /// <summary>位置付き CSV から節を作る。</summary>
    public static class PositionCsvNodeBuilder
    {
        // ================================================================
        // 読み取り
        // ================================================================

        /// <summary>
        /// 本文を表にする。列名が空なら既定（name / x / y / z）を使う。
        /// 親の列名が空なら親を読まない。指定したのに見出しに無ければ失敗にする。
        /// </summary>
        public static bool TryParse(
            string text,
            string nameColumn, string parentColumn,
            string xColumn, string yColumn, string zColumn,
            out PositionCsvTable table, out string reason)
        {
            table  = new PositionCsvTable();
            reason = null;

            if (string.IsNullOrEmpty(nameColumn)) nameColumn = "name";
            if (string.IsNullOrEmpty(xColumn))    xColumn    = "x";
            if (string.IsNullOrEmpty(yColumn))    yColumn    = "y";
            if (string.IsNullOrEmpty(zColumn))    zColumn    = "z";

            if (string.IsNullOrEmpty(text)) { reason = "CSV が空です"; return false; }

            var lines = text.Split('\n');
            List<string> header = null;
            int iName = -1, iParent = -1, iX = -1, iY = -1, iZ = -1;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int ln = 0; ln < lines.Length; ln++)
            {
                string line = lines[ln].TrimEnd('\r');
                if (ln == 0 && line.Length > 0 && line[0] == '﻿') line = line.Substring(1);
                string t = line.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("#", StringComparison.Ordinal) ||
                    t.StartsWith("//", StringComparison.Ordinal) ||
                    t.StartsWith(";", StringComparison.Ordinal)) continue;

                var f = SplitCsvLine(line);

                if (header == null)
                {
                    header  = f;
                    iName   = FindColumn(header, nameColumn);
                    iX      = FindColumn(header, xColumn);
                    iY      = FindColumn(header, yColumn);
                    iZ      = FindColumn(header, zColumn);
                    iParent = string.IsNullOrEmpty(parentColumn) ? -1 : FindColumn(header, parentColumn);

                    var missing = new List<string>();
                    if (iName < 0) missing.Add(nameColumn);
                    if (iX < 0)    missing.Add(xColumn);
                    if (iY < 0)    missing.Add(yColumn);
                    if (iZ < 0)    missing.Add(zColumn);
                    if (!string.IsNullOrEmpty(parentColumn) && iParent < 0) missing.Add(parentColumn);
                    if (missing.Count > 0)
                    {
                        reason = $"見出し行に列がありません: {string.Join(", ", missing)}"
                               + $"（見出し: {string.Join(", ", header)}）";
                        return false;
                    }
                    continue;
                }

                string name = Field(f, iName);
                if (string.IsNullOrEmpty(name)) continue;

                if (!TryFloat(Field(f, iX), out float x) ||
                    !TryFloat(Field(f, iY), out float y) ||
                    !TryFloat(Field(f, iZ), out float z))
                {
                    reason = $"{ln + 1} 行目（{name}）の位置が数値ではありません";
                    return false;
                }

                if (!seen.Add(name))
                {
                    reason = $"名前が重複しています: {name}";
                    return false;
                }

                table.Names.Add(name);
                table.Parents.Add(iParent >= 0 ? Field(f, iParent) : "");
                table.Positions.Add(new Vector3(x, y, z));
            }

            if (header == null) { reason = "見出し行がありません"; return false; }
            if (table.Count == 0) { reason = "データ行がありません"; return false; }
            return true;
        }

        // ================================================================
        // 作成
        // ================================================================

        /// <summary>
        /// 表から節を作る。includeNames が空なら全行、あればその名前の行だけ。
        /// 検証はすべて作る前に済ませるので、失敗したときモデルは変わらない。
        /// </summary>
        public static PositionCsvNodeResult Build(
            ModelContext model, PositionCsvTable table,
            IReadOnlyList<string> includeNames, bool asBones)
        {
            var result = new PositionCsvNodeResult();
            if (model == null) { result.Message = "モデルがありません"; return result; }
            if (table == null || table.Count == 0) { result.Message = "表が空です"; return result; }

            // 表の名前 → 行
            var rowOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < table.Count; i++) rowOf[table.Names[i]] = i;

            // 作る行
            var selected = new HashSet<string>(StringComparer.Ordinal);
            if (includeNames != null && includeNames.Count > 0)
            {
                var unknown = new List<string>();
                foreach (var n in includeNames)
                {
                    string nn = n?.Trim();
                    if (string.IsNullOrEmpty(nn)) continue;
                    if (rowOf.ContainsKey(nn)) selected.Add(nn);
                    else unknown.Add(nn);
                }
                if (unknown.Count > 0)
                {
                    result.Message = $"表に無い名前が指定されています: {string.Join(", ", unknown)}";
                    return result;
                }
            }
            else
            {
                foreach (var n in table.Names) selected.Add(n);
            }
            if (selected.Count == 0) { result.Message = "作る行がありません"; return result; }

            // モデルの既存名 → 索引（最初に当たったもの）
            model.ComputeWorldMatrices();
            var existing = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || string.IsNullOrEmpty(mc.Name)) continue;
                if (!existing.ContainsKey(mc.Name)) existing[mc.Name] = i;
            }

            // 親の解決（作る行の名前 / 既存名 / 空）
            var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var parentIsNew = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var name in selected)
            {
                string p = table.Parents[rowOf[name]];
                var guard = new HashSet<string>(StringComparer.Ordinal) { name };
                string resolved = "";
                bool isNew = false;
                while (!string.IsNullOrEmpty(p))
                {
                    if (!guard.Add(p))
                    {
                        result.Message = $"親の指定が循環しています: {name}";
                        return result;
                    }
                    if (selected.Contains(p)) { resolved = p; isNew = true; break; }
                    if (existing.ContainsKey(p)) { resolved = p; isNew = false; break; }
                    if (rowOf.TryGetValue(p, out int pr)) { p = table.Parents[pr]; continue; }
                    break;
                }
                parentOf[name]    = resolved;
                parentIsNew[name] = isNew;
            }

            // 親が先に来る並び（表の並びを保ちつつ深さ優先）
            var order = new List<string>(selected.Count);
            var placed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in table.Names)
            {
                if (!selected.Contains(name)) continue;
                AppendWithAncestors(name, parentOf, parentIsNew, placed, order);
            }

            // 作る
            var worldOf = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var name in order)
            {
                Vector3 world = table.Positions[rowOf[name]];
                string pName = parentOf[name];

                int parentIndex = -1;
                Matrix4x4 parentWorld = Matrix4x4.identity;
                if (!string.IsNullOrEmpty(pName))
                {
                    if (parentIsNew[name])
                    {
                        parentIndex = indexOf[pName];
                        parentWorld = worldOf[pName];
                    }
                    else
                    {
                        parentIndex = existing[pName];
                        parentWorld = model.GetMeshContext(parentIndex).WorldMatrix;
                        result.AttachedToExisting++;
                    }
                }
                else
                {
                    result.Roots++;
                }

                Vector3 local = parentWorld.inverse.MultiplyPoint3x4(world);

                var w = parentWorld * Matrix4x4.Translate(local);
                worldOf[name] = w;

                string objName = name;
                if (existing.ContainsKey(objName))
                {
                    objName = model.GenerateUniqueMeshName(objName);
                    result.Renamed++;
                }

                int added = AddNode(model, objName, parentIndex, local, asBones);
                indexOf[name] = added;
                result.Created.Add(added);
            }

            model.ComputeWorldMatrices();
            if (asBones)
            {
                foreach (int i in result.Created)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc != null) BindPoseOps.RebindToBind(mc);
                }
            }

            string kind = asBones ? "ボーン" : "空のオブジェクト";
            result.Message = $"{kind} {result.Created.Count} 個を作りました"
                           + $"（親なし {result.Roots} / 既存へ接続 {result.AttachedToExisting} / 改名 {result.Renamed}）";
            return result;
        }

        private static void AppendWithAncestors(
            string name,
            Dictionary<string, string> parentOf, Dictionary<string, bool> parentIsNew,
            HashSet<string> placed, List<string> order)
        {
            if (placed.Contains(name)) return;
            string p = parentOf[name];
            if (!string.IsNullOrEmpty(p) && parentIsNew[name])
                AppendWithAncestors(p, parentOf, parentIsNew, placed, order);
            if (placed.Add(name)) order.Add(name);
        }

        /// <summary>節を 1 つ足す。ボーンの作り方は SpringBoneChainPlacer.AddBone にそろえる。</summary>
        private static int AddNode(
            ModelContext model, string name, int parentIndex, Vector3 localPos, bool asBone)
        {
            var bt = new BoneTransform
            {
                Position          = localPos,
                Rotation          = Vector3.zero,
                Scale             = Vector3.one,
                UseLocalTransform = true,
                HasBoneTransform  = asBone,
            };

            var type = asBone ? MeshType.Bone : MeshType.Mesh;
            var mo = new MeshObject(name)
            {
                Type                 = type,
                HierarchyParentIndex = parentIndex,
                BoneTransform        = bt,
            };

            var mc = new MeshContext
            {
                MeshObject           = mo,
                Name                 = name,
                Type                 = type,
                IsVisible            = true,
                BindPose             = Matrix4x4.identity,
                BoneTransform        = bt,
                HierarchyParentIndex = parentIndex,
            };

            if (asBone)
            {
                mc.BonePoseData = new BonePoseData { IsActive = true };
            }
            else
            {
                mc.UnityMesh         = new Mesh();
                mc.OriginalPositions = new Vector3[0];
            }

            mc.ParentModelContext = model;
            return model.Add(mc);
        }

        // ================================================================
        // CSV の下請け
        // ================================================================

        private static int FindColumn(List<string> header, string column)
        {
            for (int i = 0; i < header.Count; i++)
                if (string.Equals(header[i]?.Trim(), column.Trim(), StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static string Field(List<string> f, int i)
            => (i >= 0 && i < f.Count) ? (f[i] ?? "").Trim() : "";

        private static bool TryFloat(string s, out float v)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        /// <summary>カンマ区切り。二重引用符で囲んだ欄の中のカンマと "" を扱う。</summary>
        private static List<string> SplitCsvLine(string line)
        {
            var r = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else q = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ',') { r.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            r.Add(sb.ToString());
            return r;
        }
    }
}
