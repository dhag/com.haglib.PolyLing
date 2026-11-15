// MorphCsvIO.cs
// モーフエクスプレッションのCSV Import/Export ロジック
// ファイルダイアログはEditorBridge経由
//
// 【行の形】表情名,オブジェクト名,ブレンドシェイプ名,重み,オブジェクト名,ブレンドシェイプ名,重み,...
//
// 【形で読み書きする】（PolyLing_モーフ整理計画.md 3.12・段 6）
//   書き出し：形を指す表情（同じ名前の形がある頂点の表情・グループの子のうち形）は、その形の差分を持つ
//     オブジェクトごとに「オブジェクト名,形の名前,重み」を書く。表情に無い形（UV の形を除く）は形の名前の行にする。
//     形を指さない表情は書かない。
//   読み込み：ブレンドシェイプ名が形の名前なら、形の子にする。1 つの形について、並んだオブジェクトが
//     その形の差分を持つオブジェクトと一致し、重みが全部同じときだけ読む（違えば読まずに unmatched に数える。
//     オブジェクトごとに違う重みは「形の名前 × 重み」で表せないため）。
//     表情名と同じ名前の形を重み 1 で指すだけの行は形そのものなので、表情は作らない（読んだ数には入れる）。
//     形の名前でないものは読まずに unmatched に数える（段 7 で旧モーフを撤去した）。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Core;
using Poly_Ling.CSV;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.EditorBridge;
using Poly_Ling.MQO;

namespace Poly_Ling.UI
{
    public static class MorphCsvIO
    {
        /// <summary>BlendShapeSync CSV の最近使ったパス（読込・保存で共有）。</summary>
        private const string CsvRecentKey = "Morph.BlendShapeSyncCsv.Path";

        /// <summary>読み込む CSV のパスを尋ねる（パネル用。取消なら null）。</summary>
        public static string AskImportPath()
            => RecentFileDialog.AskLoad("BlendShapeSync CSV読込", CsvRecentKey, "csv");

        /// <summary>保存先 CSV のパスを尋ねる（パネル用。取消なら null）。</summary>
        public static string AskExportPath()
            => SaveDest.AskSavePath("BlendShapeSync CSV保存", SaveDest.Keys.MorphCsv, "", "blendshape_sync.csv", "csv");

        public static (int imported, int overwritten, int unmatched) Import(
            ModelContext model,
            System.Action<string> statusLog)
        {
            string path = RecentFileDialog.AskLoad("BlendShapeSync CSV読込", CsvRecentKey, "csv");
            if (string.IsNullOrEmpty(path)) return (0, 0, 0);
            return ImportFromPath(model, path, statusLog);
        }

        /// <summary>
        /// 指定したパスの CSV を読み込む（ダイアログは開かない。ImportMorphCsvCommand の受け口から呼ぶ）。
        /// </summary>
        public static (int imported, int overwritten, int unmatched) ImportFromPath(
            ModelContext model, string path,
            System.Action<string> statusLog)
        {
            if (model == null || string.IsNullOrEmpty(path)) return (0, 0, 0);

            var rows = CSVHelper.ParseFile(path);
            if (rows.Count == 0) { statusLog?.Invoke("CSVが空です"); return (0, 0, 0); }

            var importedSets   = new Dictionary<string, MorphExpression>();
            int unmatchedCount = 0;
            int selfShapes     = 0;

            foreach (var row in rows)
            {
                if (CSVHelper.IsCommentLine(row.OriginalLine)) continue;
                if (row.FieldCount < 4) continue;

                string expressionName = row[0];
                if (string.IsNullOrEmpty(expressionName)) continue;

                var set = new MorphExpression { Name = expressionName, NameEnglish = "", Panel = 3, Type = MorphType.Vertex };
                // 形の名前 → (並んだオブジェクト, 重み)
                var shapeMeshes  = new Dictionary<string, HashSet<string>>();
                var shapeWeights = new Dictionary<string, List<float>>();
                var shapeOrder   = new List<string>();

                for (int i = 1; i + 2 < row.FieldCount; i += 3)
                {
                    string meshName  = row[i];
                    string shapeName = row[i + 1];
                    string weightStr = row[i + 2];
                    if (string.IsNullOrEmpty(meshName) || string.IsNullOrEmpty(shapeName)) continue;
                    if (!float.TryParse(weightStr,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float w)) continue;

                    var shape = model.FindShapeByName(shapeName);
                    if (shape != null && !shape.IsUV)
                    {
                        if (!shapeMeshes.ContainsKey(shapeName))
                        {
                            shapeMeshes[shapeName]  = new HashSet<string>();
                            shapeWeights[shapeName] = new List<float>();
                            shapeOrder.Add(shapeName);
                        }
                        shapeMeshes[shapeName].Add(meshName);
                        shapeWeights[shapeName].Add(w);
                        continue;
                    }

                    unmatchedCount++;
                }

                bool bad = false;
                foreach (var sn in shapeOrder)
                {
                    var shape = model.FindShapeByName(sn);
                    var expected = ShapeMeshNames(model, shape.Id);
                    var ws = shapeWeights[sn];
                    bool uniform = ws.TrueForAll(x => x == ws[0]);
                    if (!uniform || !expected.SetEquals(shapeMeshes[sn])) { bad = true; break; }
                    set.GroupChildren.Add(new MorphGroupChild(sn, ws[0]));
                }
                if (bad)
                {
                    unmatchedCount++;
                    statusLog?.Invoke($"表情「{expressionName}」は、形のオブジェクトか重みが形と合わないので読みません");
                    continue;
                }

                if (set.GroupChildren.Count > 0)
                {
                    // 同じ名前の形を重み 1 で指すだけ → 形そのもの（表情は作らない）。
                    if (set.GroupChildren.Count == 1
                        && set.GroupChildren[0].Name == expressionName && set.GroupChildren[0].Weight == 1f)
                    {
                        selfShapes++;
                        continue;
                    }
                    set.Type = MorphType.Group;
                }

                if (set.GroupChildren.Count > 0) importedSets[expressionName] = set;
            }

            if (importedSets.Count == 0 && selfShapes > 0)
            {
                statusLog?.Invoke($"CSV読込完了: 形そのものの行 {selfShapes} 件（表情は作りません）");
                return (selfShapes, 0, unmatchedCount);
            }

            if (importedSets.Count == 0)
            { statusLog?.Invoke($"マッチする形が見つかりません (未マッチ: {unmatchedCount})"); return (0, 0, unmatchedCount); }

            var overwriteNames = importedSets.Keys.Where(n => model.FindMorphExpressionByName(n) != null).ToList();
            if (overwriteNames.Count > 0)
            {
                string msg = $"以下のセットは既に存在します。上書きしますか？\n{string.Join(", ", overwriteNames)}";
                if (!PLEditorBridge.I.DisplayDialogYesNo("上書き確認", msg, "上書き", "キャンセル"))
                    return (0, 0, unmatchedCount);
            }

            foreach (var imported in importedSets.Values)
            {
                int existIdx = model.MorphExpressions.FindIndex(s => s.Name == imported.Name);
                if (existIdx >= 0) model.MorphExpressions[existIdx] = imported;
                else               model.MorphExpressions.Add(imported);
            }

            string unmatchMsg = unmatchedCount > 0 ? $" (未マッチ: {unmatchedCount})" : "";
            string selfMsg = selfShapes > 0 ? $" (形そのものの行: {selfShapes})" : "";
            statusLog?.Invoke($"CSV読込完了: {importedSets.Count}セット ({overwriteNames.Count}件上書き){unmatchMsg}{selfMsg}");
            return (importedSets.Count + selfShapes, overwriteNames.Count, unmatchedCount);
        }

        public static void Export(ModelContext model, System.Action<string> statusLog)
        {
            if (model == null || model.MorphExpressionCount == 0)
            { statusLog?.Invoke("保存するモーフエクスプレッションがありません"); return; }

            // 書き込み先はフォルダだけを覚え、ファイル名は毎回この既定から始める。
            // 履歴にフルパスを残すと、次の保存が前回のファイル名で開いて上書き事故になる。
            string path = SaveDest.AskSavePath(
                "BlendShapeSync CSV保存", SaveDest.Keys.MorphCsv, "", "blendshape_sync.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            ExportToPath(model, path, statusLog);
        }

        /// <summary>
        /// 指定したパスへ CSV を書き出す（ダイアログは開かない。ExportMorphCsvCommand の受け口から呼ぶ）。
        /// </summary>
        public static void ExportToPath(ModelContext model, string path, System.Action<string> statusLog)
        {
            if (model == null || (model.MorphExpressionCount == 0 && model.ShapeCount == 0))
            { statusLog?.Invoke("保存するモーフエクスプレッションがありません"); return; }
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var writer = new CSVWriter(MQOExportSettings.DefaultDecimalPrecision);
                writer.AddComment(" ExpressionName,MeshName,BlendShapeName,Weight,...");

                int rowsWritten = 0;
                var exprNames = new HashSet<string>();
                foreach (var set in model.MorphExpressions)
                {
                    if (set == null) continue;
                    if (!string.IsNullOrEmpty(set.Name)) exprNames.Add(set.Name);

                    // 形を指す表情（冒頭の注記）
                    var shapeParts = new List<object> { set.Name };
                    var same = set.Type == MorphType.Vertex ? model.FindShapeByName(set.Name) : null;
                    if (same != null) AddShapeParts(model, same, 1f, shapeParts);
                    else if (set.Type == MorphType.Group && set.GroupChildren != null)
                        foreach (var ch in set.GroupChildren)
                        {
                            var sd = model.FindShapeByName(ch.Name);
                            if (sd != null) AddShapeParts(model, sd, ch.Weight, shapeParts);
                        }
                    if (shapeParts.Count > 1) { writer.AddRow(shapeParts.ToArray()); rowsWritten++; }
                }

                // 表情に無い形（形 1 つ＝頂点の表情 1 つ）
                if (model.Shapes != null)
                    foreach (var sd in model.Shapes)
                    {
                        if (sd == null || sd.IsUV || string.IsNullOrEmpty(sd.Name) || exprNames.Contains(sd.Name)) continue;
                        var parts = new List<object> { sd.Name };
                        AddShapeParts(model, sd, 1f, parts);
                        if (parts.Count > 1) { writer.AddRow(parts.ToArray()); rowsWritten++; }
                    }

                writer.WriteToFile(path);
                statusLog?.Invoke($"CSV保存完了: {rowsWritten}行 → {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex) { statusLog?.Invoke($"CSV保存失敗: {ex.Message}"); }
        }
        /// <summary>形 shapeId の差分を持つ描画オブジェクトの名前。</summary>
        private static HashSet<string> ShapeMeshNames(ModelContext model, int shapeId)
        {
            var names = new HashSet<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null || string.IsNullOrEmpty(mc.Name)) continue;
                if (Poly_Ling.Ops.ShapeOps.GetSparseOffsets(mc.MeshObject, model.CurrentShapeId, shapeId).Count > 0)
                    names.Add(mc.Name);
            }
            return names;
        }

        /// <summary>形の差分を持つオブジェクトごとに「オブジェクト名,形の名前,重み」を足す。</summary>
        private static void AddShapeParts(ModelContext model, ShapeDef shape, float weight, List<object> parts)
        {
            if (shape == null || shape.IsUV) return;
            var names = ShapeMeshNames(model, shape.Id);
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || !names.Contains(mc.Name)) continue;
                parts.Add(mc.Name);
                parts.Add(shape.Name);
                parts.Add(weight);
                names.Remove(mc.Name);
            }
        }
    }
}
