// Assets/Editor/Poly_Ling/MQO/Import/PmxBoneCSVParser.cs
// PmxBone形式のCSVをパースしてボーン情報を読み取る

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// PmxBoneデータ
    /// </summary>
    public class PmxBoneData
    {
        public string Name { get; set; }
        public string NameEn { get; set; }
        public Vector3 Position { get; set; }
        public string ParentName { get; set; }
        public int DeformHierarchy { get; set; }
        public bool IsPhysicsAfter { get; set; }
        public bool CanRotate { get; set; }
        public bool CanMove { get; set; }
        public bool IsIK { get; set; }
        public bool IsVisible { get; set; }
        public bool IsControllable { get; set; }
    }

    /// <summary>
    /// PmxBone CSVパーサー
    /// </summary>
    public static class PmxBoneCSVParser
    {
        /// <summary>
        /// CSVファイルをパース
        /// </summary>
        public static List<PmxBoneData> ParseFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Debug.LogWarning($"[PmxBoneCSVParser] File not found: {filePath}");
                return new List<PmxBoneData>();
            }

            try
            {
                string content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                return Parse(content);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PmxBoneCSVParser] Failed to read file: {e.Message}");
                return new List<PmxBoneData>();
            }
        }

        /// <summary>
        /// CSV文字列をパース
        /// </summary>
        public static List<PmxBoneData> Parse(string content)
        {
            var bones = new List<PmxBoneData>();
            
            if (string.IsNullOrEmpty(content))
                return bones;

            var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                // コメント行またはヘッダ行をスキップ
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";"))
                    continue;

                // PmxBone行のみ処理
                if (!line.StartsWith("PmxBone,"))
                    continue;

                var bone = ParseBoneLine(line);
                if (bone != null)
                {
                    bones.Add(bone);
                }
            }

            Debug.Log($"[PmxBoneCSVParser] Parsed {bones.Count} bones");
            return bones;
        }

        /// <summary>
        /// 1行をパースしてPmxBoneDataを作成
        /// フォーマット: PmxBone,"ボーン名","英名",変形階層,物理後,位置X,位置Y,位置Z,回転,移動,IK,表示,操作,"親ボーン名",...
        /// </summary>
        private static PmxBoneData ParseBoneLine(string line)
        {
            try
            {
                var fields = ParseCSVLine(line);
                
                if (fields.Count < 14)
                {
                    Debug.LogWarning($"[PmxBoneCSVParser] Invalid line (not enough fields): {line}");
                    return null;
                }

                // fields[0] = "PmxBone"
                // fields[1] = ボーン名
                // fields[2] = 英名
                // fields[3] = 変形階層
                // fields[4] = 物理後(0/1)
                // fields[5] = 位置X
                // fields[6] = 位置Y
                // fields[7] = 位置Z
                // fields[8] = 回転(0/1)
                // fields[9] = 移動(0/1)
                // fields[10] = IK(0/1)
                // fields[11] = 表示(0/1)
                // fields[12] = 操作(0/1)
                // fields[13] = 親ボーン名

                var bone = new PmxBoneData
                {
                    Name = fields[1],
                    NameEn = fields[2],
                    DeformHierarchy = ParseInt(fields[3], 0),
                    IsPhysicsAfter = ParseInt(fields[4], 0) != 0,
                    Position = new Vector3(
                        ParseFloat(fields[5], 0f),
                        ParseFloat(fields[6], 0f),
                        ParseFloat(fields[7], 0f)
                    ),
                    CanRotate = ParseInt(fields[8], 1) != 0,
                    CanMove = ParseInt(fields[9], 0) != 0,
                    IsIK = ParseInt(fields[10], 0) != 0,
                    IsVisible = ParseInt(fields[11], 1) != 0,
                    IsControllable = ParseInt(fields[12], 1) != 0,
                    ParentName = fields[13]
                };

                return bone;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PmxBoneCSVParser] Failed to parse line: {e.Message}\n{line}");
                return null;
            }
        }

        /// <summary>
        /// CSV行をフィールドに分割（ダブルクォート対応）
        /// </summary>
        private static List<string> ParseCSVLine(string line)
        {
            var fields = new List<string>();
            var current = "";
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        // エスケープされた引用符
                        current += '"';
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current);
                    current = "";
                }
                else
                {
                    current += c;
                }
            }
            
            fields.Add(current);
            return fields;
        }

        private static int ParseInt(string s, int defaultValue)
        {
            if (int.TryParse(s, out int result))
                return result;
            return defaultValue;
        }

        private static float ParseFloat(string s, float defaultValue)
        {
            if (float.TryParse(s, out float result))
                return result;
            return defaultValue;
        }
    }
}
