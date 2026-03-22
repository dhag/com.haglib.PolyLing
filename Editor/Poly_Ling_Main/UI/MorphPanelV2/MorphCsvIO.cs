// MorphCsvIO.cs
// モーフエクスプレッションのCSV Import/Export ロジック
// ファイルダイアログはEditorBridge経由

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.CSV;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.EditorBridge;
using Poly_Ling.MQO;

namespace Poly_Ling.UI
{
    public static class MorphCsvIO
    {
        public static (int imported, int overwritten, int unmatched) Import(
            ModelContext model,
            System.Action<string> statusLog)
        {
            string path = PLEditorBridge.I.OpenFilePanel("BlendShapeSync CSV読込", "", "csv");
            if (string.IsNullOrEmpty(path)) return (0, 0, 0);

            var rows = CSVHelper.ParseFile(path);
            if (rows.Count == 0) { statusLog?.Invoke("CSVが空です"); return (0, 0, 0); }

            var meshNameToIndex = new Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && !string.IsNullOrEmpty(mc.Name))
                    meshNameToIndex[mc.Name] = i;
            }

            var importedSets   = new Dictionary<string, MorphExpression>();
            int unmatchedCount = 0;

            foreach (var row in rows)
            {
                if (CSVHelper.IsCommentLine(row.OriginalLine)) continue;
                if (row.FieldCount < 4) continue;

                string expressionName = row[0];
                if (string.IsNullOrEmpty(expressionName)) continue;

                var set = new MorphExpression { Name = expressionName, NameEnglish = "", Panel = 3, Type = MorphType.Vertex };

                for (int i = 1; i + 2 < row.FieldCount; i += 3)
                {
                    string meshName  = row[i];
                    string shapeName = row[i + 1];
                    string weightStr = row[i + 2];
                    if (string.IsNullOrEmpty(meshName) || string.IsNullOrEmpty(shapeName)) continue;
                    if (!float.TryParse(weightStr,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float w)) continue;

                    string morphMeshName = $"{meshName}_{shapeName}";
                    if (meshNameToIndex.TryGetValue(morphMeshName, out int meshIndex))
                        set.AddMesh(meshIndex, w);
                    else
                        unmatchedCount++;
                }

                if (set.MeshCount > 0) importedSets[expressionName] = set;
            }

            if (importedSets.Count == 0)
            { statusLog?.Invoke($"マッチするモーフメッシュが見つかりません (未マッチ: {unmatchedCount})"); return (0, 0, unmatchedCount); }

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
            statusLog?.Invoke($"CSV読込完了: {importedSets.Count}セット ({overwriteNames.Count}件上書き){unmatchMsg}");
            return (importedSets.Count, overwriteNames.Count, unmatchedCount);
        }

        public static void Export(ModelContext model, System.Action<string> statusLog)
        {
            if (model == null || model.MorphExpressionCount == 0)
            { statusLog?.Invoke("保存するモーフエクスプレッションがありません"); return; }

            string path = PLEditorBridge.I.SaveFilePanel("BlendShapeSync CSV保存", "", "blendshape_sync.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var writer = new CSVWriter(MQOExportSettings.DefaultDecimalPrecision);
                writer.AddComment(" ExpressionName,MeshName,BlendShapeName,Weight,...");

                foreach (var set in model.MorphExpressions)
                {
                    if (set.Type != MorphType.Vertex || !set.IsValid) continue;
                    var parts = new List<object> { set.Name };

                    foreach (var entry in set.MeshEntries)
                    {
                        if (entry.MeshIndex < 0 || entry.MeshIndex >= model.MeshContextCount) continue;
                        var morphCtx = model.GetMeshContext(entry.MeshIndex);
                        if (morphCtx == null || !morphCtx.IsMorph) continue;

                        int lastUnderscore = morphCtx.Name.LastIndexOf('_');
                        if (lastUnderscore <= 0) continue;

                        parts.Add(morphCtx.Name.Substring(0, lastUnderscore));
                        parts.Add(set.Name);
                        parts.Add(entry.Weight);
                    }

                    if (parts.Count > 1) writer.AddRow(parts.ToArray());
                }

                writer.WriteToFile(path);
                statusLog?.Invoke($"CSV保存完了: {model.MorphExpressionCount}セット → {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex) { statusLog?.Invoke($"CSV保存失敗: {ex.Message}"); }
        }
    }
}
