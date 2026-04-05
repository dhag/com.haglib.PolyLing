#if POLY_LING_VRM10

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UniVRM10;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// BlendShapeSync CSV から VRM10 Expression アセットを生成するロジックのEditorCore実装。
    /// BlendShapeSyncToVRM10（EditorWindow）はUIと状態を保持し、本クラスを呼び出す。
    /// </summary>
    public static class EditorBlendShapeSyncToVRM10
    {
        // ================================================================
        // CSV 解析
        // ================================================================

        /// <summary>CSV文字列をExpressionName → エントリリストに解析する</summary>
        public static Dictionary<string, List<(string meshName, string shapeName, float weight)>>
            ParseCSV(string csv)
        {
            var result = new Dictionary<string, List<(string, string, float)>>();
            var lines = csv.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                var parts = trimmed.Split(',');
                if (parts.Length < 4) continue;

                string expressionName = parts[0].Trim();
                var targets = new List<(string, string, float)>();

                for (int i = 1; i + 2 < parts.Length; i += 3)
                {
                    string meshName  = parts[i].Trim();
                    string shapeName = parts[i + 1].Trim();
                    if (float.TryParse(parts[i + 2].Trim(), out float weight))
                        targets.Add((meshName, shapeName, weight));
                }

                if (targets.Count > 0) result[expressionName] = targets;
            }
            return result;
        }

        // ================================================================
        // BlendShapeマップ構築
        // ================================================================

        /// <summary>VRM10ルート以下のSMRからBlendShapeマップを構築する</summary>
        public static Dictionary<(string smrName, string shapeName), (string relativePath, int index)>
            BuildBlendShapeMap(Transform root)
        {
            var map = new Dictionary<(string, string), (string, int)>();
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh == null) continue;
                string relativePath = GetRelativePath(root, smr.transform);
                int count = smr.sharedMesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                {
                    string shapeName = smr.sharedMesh.GetBlendShapeName(i);
                    var key = (smr.name, shapeName);
                    if (!map.ContainsKey(key)) map[key] = (relativePath, i);
                }
            }
            return map;
        }

        /// <summary>root から target への相対パスを返す</summary>
        public static string GetRelativePath(Transform root, Transform target)
        {
            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ================================================================
        // マッチング
        // ================================================================

        public struct MatchResult
        {
            public string expressionName;
            public List<(string meshName, string shapeName, float weight, string relativePath, int blendShapeIndex, bool matched)> entries;
        }

        /// <summary>
        /// CSV定義とBlendShapeマップをマッチングし、結果を返す。
        /// statusMessage にはサマリ文字列が入る。
        /// </summary>
        public static Dictionary<string, List<(string, string, float, string, int, bool)>> Analyze(
            Poly_Ling.Runtime.BlendShapeSync sync,
            Vrm10Instance vrm10,
            out string statusMessage)
        {
            statusMessage = "";

            if (string.IsNullOrEmpty(sync?.MappingCSV))
            {
                statusMessage = "MappingCSVが空です";
                return null;
            }

            var clipDefinitions = ParseCSV(sync.MappingCSV);
            if (clipDefinitions.Count == 0)
            {
                statusMessage = "CSVにExpressionが見つかりません";
                return null;
            }

            var blendShapeMap = BuildBlendShapeMap(vrm10.transform);
            var matchResults = new Dictionary<string, List<(string, string, float, string, int, bool)>>();

            foreach (var kv in clipDefinitions)
            {
                var entries = new List<(string, string, float, string, int, bool)>();
                foreach (var (meshName, shapeName, weight) in kv.Value)
                {
                    var key = (meshName, shapeName);
                    if (blendShapeMap.TryGetValue(key, out var info))
                        entries.Add((meshName, shapeName, weight, info.relativePath, info.index, true));
                    else
                        entries.Add((meshName, shapeName, weight, "", -1, false));
                }
                matchResults[kv.Key] = entries;
            }

            int totalEntries   = matchResults.Values.Sum(e => e.Count);
            int matchedEntries = matchResults.Values.Sum(e => e.Count(x => x.Item6));
            statusMessage = $"解析完了: {clipDefinitions.Count}Expression, {matchedEntries}/{totalEntries}エントリ マッチ";
            return matchResults;
        }

        // ================================================================
        // Expression生成
        // ================================================================

        /// <summary>マッチ結果からVRM10Expressionアセットを生成してoutputFolderに保存する</summary>
        public static string GenerateExpressions(
            Dictionary<string, List<(string meshName, string shapeName, float weight, string relativePath, int blendShapeIndex, bool matched)>> matchResults,
            string outputFolder)
        {
            if (matchResults == null || matchResults.Count == 0)
                return "マッチ結果がありません";

            if (!AssetDatabase.IsValidFolder(outputFolder))
            {
                string parent     = Path.GetDirectoryName(outputFolder);
                string folderName = Path.GetFileName(outputFolder);
                if (!AssetDatabase.IsValidFolder(parent))
                    AssetDatabase.CreateFolder("Assets", parent.Replace("Assets/", ""));
                AssetDatabase.CreateFolder(parent, folderName);
            }

            int created = 0;
            foreach (var kv in matchResults)
            {
                var matchedEntries = kv.Value.Where(e => e.matched).ToList();
                if (matchedEntries.Count == 0) continue;

                var expression = ScriptableObject.CreateInstance<VRM10Expression>();
                expression.name = kv.Key;

                var bindings = matchedEntries.Select(e => new MorphTargetBinding
                {
                    RelativePath = e.relativePath,
                    Index        = e.blendShapeIndex,
                    Weight       = e.weight
                }).ToList();
                expression.MorphTargetBindings = bindings.ToArray();

                string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{kv.Key}.asset");
                AssetDatabase.CreateAsset(expression, assetPath);
                created++;
                Debug.Log($"[EditorBlendShapeSyncToVRM10] Created: {assetPath} ({bindings.Count} bindings)");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return $"Expression生成完了: {created}アセット → {outputFolder}";
        }
    }
}

#endif
