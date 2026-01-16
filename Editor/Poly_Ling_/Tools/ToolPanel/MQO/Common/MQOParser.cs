// Assets/Editor/Poly_Ling/MQO/Core/MQOParser.cs
// MQOファイルパーサー
// テキスト形式のMQOファイルを解析してMQODocumentを生成

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQOファイルパーサー
    /// </summary>
    public static class MQOParser
    {
        // ================================================================
        // 正規表現パターン
        // ================================================================

        private static class Patterns
        {
            // 数値パターン
            public const string Decimal = @"(-?\d+(?:\.\d+)?)";

            // コンパイル済み正規表現
            public static readonly Regex VersionLine = new Regex(
                @"^Format Text Ver (\d+(?:\.\d+)?)$",
                RegexOptions.Compiled);

            public static readonly Regex ObjectHeader = new Regex(
                @"^Object\s+""(.+)""\s+\{$",
                RegexOptions.Compiled);

            public static readonly Regex MaterialLine = new Regex(
                @"^""(.*)""\s+(.+)$",
                RegexOptions.Compiled);

            public static readonly Regex AttributeLine = new Regex(
                @"^(\w+)\s+(.+)$",
                RegexOptions.Compiled);

            public static readonly Regex ParamPattern = new Regex(
                @"(?<key>\w+)\((?:""(?<val>[^""]*)""|(?<val>[^\)]+))\)",
                RegexOptions.Compiled);

            public static readonly Regex FaceHeader = new Regex(
                @"^([1234])\s+(.+)$",
                RegexOptions.Compiled);

            public static readonly Regex VertexHeader = new Regex(
                @"^vertex\s+(\d+)\s+\{$",
                RegexOptions.Compiled);

            public static readonly Regex FaceBlockHeader = new Regex(
                @"^face\s+(\d+)\s+\{$",
                RegexOptions.Compiled);

            public static readonly Regex BackImageLine = new Regex(
                $@"^(\w+)\s+""(.*)""\s+{Decimal}\s+{Decimal}\s+{Decimal}\s+{Decimal}",
                RegexOptions.Compiled);

            public static readonly Regex DecimalValue = new Regex(
                Decimal,
                RegexOptions.Compiled);
        }

        // ================================================================
        // パブリックAPI
        // ================================================================

        /// <summary>
        /// ファイルからMQOドキュメントをパース
        /// </summary>
        /// <param name="filePath">MQOファイルパス</param>
        /// <returns>パース結果のMQODocument</returns>
        public static MQODocument ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"MQO file not found: {filePath}");

            // Shift-JISで読み込み
            Encoding encoding = GetShiftJISEncoding();
            string content = File.ReadAllText(filePath, encoding);

            var document = Parse(content);
            document.FilePath = filePath;
            document.FileName = Path.GetFileName(filePath);

            return document;
        }

        /// <summary>
        /// 文字列からMQOドキュメントをパース
        /// </summary>
        /// <param name="content">MQOテキスト</param>
        /// <returns>パース結果のMQODocument</returns>
        public static MQODocument Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                throw new ArgumentException("Content is null or empty");

            var lines = content.Split('\n');
            var queue = new Queue<string>(lines);

            var document = new MQODocument();

            // ヘッダーパース
            if (!ParseHeader(queue, document))
                throw new FormatException("Invalid MQO header");

            // ボディパース
            ParseBody(queue, document);

            return document;
        }

        // ================================================================
        // ヘッダーパース
        // ================================================================

        private static bool ParseHeader(Queue<string> queue, MQODocument document)
        {
            if (queue.Count == 0) return false;

            // 1行目: "Metasequoia Document"
            string line1 = queue.Dequeue().Trim();
            if (line1 != "Metasequoia Document")
                return false;

            if (queue.Count == 0) return false;

            // 2行目: "Format Text Ver X.X"
            string line2 = queue.Dequeue().Trim();
            var match = Patterns.VersionLine.Match(line2);
            if (!match.Success)
                return false;

            document.Version = decimal.Parse(match.Groups[1].Value);
            return true;
        }

        // ================================================================
        // ボディパース
        // ================================================================

        private static void ParseBody(Queue<string> queue, MQODocument document)
        {
            while (queue.Count > 0)
            {
                string line = queue.Dequeue().Trim();

                // 終端
                if (line.StartsWith("Eof"))
                    return;

                // 空行スキップ
                if (string.IsNullOrEmpty(line))
                    continue;

                // Scene
                if (line.StartsWith("Scene"))
                {
                    document.Scene = ParseScene(queue);
                    continue;
                }

                // BackImage
                if (line.StartsWith("BackImage"))
                {
                    ParseBackImages(queue, document.BackImages);
                    continue;
                }

                // Material
                if (line.StartsWith("Material"))
                {
                    ParseMaterials(queue, document.Materials);
                    continue;
                }

                // Object
                var objectMatch = Patterns.ObjectHeader.Match(line);
                if (objectMatch.Success)
                {
                    var obj = ParseObject(queue, objectMatch.Groups[1].Value);
                    if (obj != null)
                        document.Objects.Add(obj);
                    continue;
                }

                // 未知のブロック
                if (line.EndsWith("{"))
                {
                    SkipBlock(queue);
                    continue;
                }
            }
        }

        // ================================================================
        // Sceneパース
        // ================================================================

        private static MQOScene ParseScene(Queue<string> queue)
        {
            var scene = new MQOScene();
            int depth = 0;

            while (queue.Count > 0)
            {
                string line = queue.Dequeue().Trim();

                if (line.EndsWith("{"))
                {
                    depth++;
                    continue;
                }

                if (line.EndsWith("}"))
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                    else
                    {
                        return scene;
                    }
                    continue;
                }

                // depth=0の属性のみ読み込み
                if (depth == 0)
                {
                    var attr = ParseAttribute(line);
                    if (attr != null)
                        scene.Attributes.Add(attr);
                }
            }

            return scene;
        }

        // ================================================================
        // BackImageパース
        // ================================================================

        private static void ParseBackImages(Queue<string> queue, List<MQOBackImage> backImages)
        {
            while (queue.Count > 0)
            {
                string line = queue.Dequeue().Trim();

                if (line.EndsWith("}"))
                    return;

                var match = Patterns.BackImageLine.Match(line);
                if (match.Success)
                {
                    backImages.Add(new MQOBackImage
                    {
                        Part = match.Groups[1].Value,
                        Path = match.Groups[2].Value,
                        X = float.Parse(match.Groups[3].Value),
                        Y = float.Parse(match.Groups[4].Value),
                        Width = float.Parse(match.Groups[5].Value),
                        Height = float.Parse(match.Groups[6].Value)
                    });
                }
            }
        }

        // ================================================================
        // Materialパース
        // ================================================================

        private static void ParseMaterials(Queue<string> queue, List<MQOMaterial> materials)
        {
            while (queue.Count > 0)
            {
                string line = queue.Dequeue().Trim();

                if (line.EndsWith("}"))
                    return;

                if (string.IsNullOrEmpty(line))
                    continue;

                var match = Patterns.MaterialLine.Match(line);
                if (match.Success)
                {
                    var material = ParseMaterial(match.Groups[1].Value, match.Groups[2].Value);
                    materials.Add(material);
                }
            }
        }

        private static MQOMaterial ParseMaterial(string name, string paramString)
        {
            var material = new MQOMaterial { Name = name };

            foreach (Match param in Patterns.ParamPattern.Matches(paramString))
            {
                string key = param.Groups["key"].Value;
                string val = param.Groups["val"].Value;

                switch (key)
                {
                    case "col":
                        var colValues = ParseFloatArray(val);
                        if (colValues.Length >= 4)
                            material.Color = new Color(colValues[0], colValues[1], colValues[2], colValues[3]);
                        break;
                    case "dif":
                        material.Diffuse = ParseFloat(val);
                        break;
                    case "amb":
                        material.Ambient = ParseFloat(val);
                        break;
                    case "emi":
                        material.Emissive = ParseFloat(val);
                        break;
                    case "spc":
                        material.Specular = ParseFloat(val);
                        break;
                    case "power":
                        material.Power = ParseFloat(val);
                        break;
                    case "tex":
                        material.TexturePath = val;
                        break;
                    case "aplane":
                        material.AlphaMapPath = val;
                        break;
                    case "bump":
                        material.BumpMapPath = val;
                        break;
                }
            }

            return material;
        }

        // ================================================================
        // Objectパース
        // ================================================================

        private static MQOObject ParseObject(Queue<string> queue, string name)
        {
            var obj = new MQOObject { Name = name };

            while (queue.Count > 0)
            {
                string line = queue.Dequeue().Trim();

                // vertex
                if (line.StartsWith("vertex "))
                {
                    ParseVertices(queue, obj.Vertices);
                    continue;
                }

                // face
                if (line.StartsWith("face "))
                {
                    ParseFaces(queue, obj.Faces);
                    continue;
                }

                // vertexattr
                if (line.StartsWith("vertexattr "))
                {
                    obj.VertexAttrRaw = ParseVertexAttr(queue);
                    continue;
                }

                // 終了
                if (line.EndsWith("}"))
                {
                    // ネストしたブロックでない場合は終了
                    if (!line.Contains("{"))
                        return obj;
                }

                // 未知のブロック
                if (line.EndsWith("{"))
                {
                    SkipBlock(queue);
                    continue;
                }

                // 属性
                var attr = ParseAttribute(line);
                if (attr != null)
                    obj.Attributes.Add(attr);
            }

            return obj;
        }

        // ================================================================
        // Vertexパース
        // ================================================================

        private static void ParseVertices(Queue<string> queue, List<MQOVertex> vertices)
        {
            int index = 0;

            while (queue.Count > 0)
            {
                string line = queue.Dequeue().Trim();

                if (line.EndsWith("}"))
                    return;

                if (string.IsNullOrEmpty(line))
                    continue;

                var matches = Patterns.DecimalValue.Matches(line);
                if (matches.Count >= 3)
                {
                    vertices.Add(new MQOVertex
                    {
                        Position = new Vector3(
                            float.Parse(matches[0].Value),
                            float.Parse(matches[1].Value),
                            float.Parse(matches[2].Value)
                        ),
                        Index = index++
                    });
                }
            }
        }

        // ================================================================
        // Faceパース
        // ================================================================

        private static void ParseFaces(Queue<string> queue, List<MQOFace> faces)
        {
            while (queue.Count > 0)
            {
                string line = queue.Dequeue().Trim();

                if (line.EndsWith("}"))
                    return;

                if (string.IsNullOrEmpty(line))
                    continue;

                var face = ParseFace(line);
                if (face != null)
                    faces.Add(face);
            }
        }

        private static MQOFace ParseFace(string line)
        {
            var match = Patterns.FaceHeader.Match(line);
            if (!match.Success)
                return null;

            int vertexCount = int.Parse(match.Groups[1].Value);
            string paramString = match.Groups[2].Value;

            var face = new MQOFace
            {
                VertexIndices = new int[vertexCount],
                UVs = new Vector2[vertexCount]
            };

            foreach (Match param in Patterns.ParamPattern.Matches(paramString))
            {
                string key = param.Groups["key"].Value;
                string val = param.Groups["val"].Value;
                var values = Patterns.DecimalValue.Matches(val);

                switch (key)
                {
                    case "V":
                        if (values.Count >= vertexCount)
                        {
                            for (int i = 0; i < vertexCount; i++)
                                face.VertexIndices[i] = int.Parse(values[i].Value);
                        }
                        break;

                    case "M":
                        face.MaterialIndex = int.Parse(val);
                        break;

                    case "UV":
                        if (values.Count >= vertexCount * 2)
                        {
                            for (int i = 0; i < vertexCount; i++)
                            {
                                face.UVs[i] = new Vector2(
                                    float.Parse(values[i * 2].Value),
                                    float.Parse(values[i * 2 + 1].Value)
                                );
                            }
                        }
                        break;

                    case "COL":
                        if (values.Count >= vertexCount)
                        {
                            face.VertexColors = new uint[vertexCount];
                            for (int i = 0; i < vertexCount; i++)
                            {
                                if (uint.TryParse(values[i].Value, out uint c))
                                    face.VertexColors[i] = c;
                            }
                        }
                        break;
                }
            }

            return face;
        }

        // ================================================================
        // VertexAttrパース（生テキスト保持）
        // ================================================================

        private static string ParseVertexAttr(Queue<string> queue)
        {
            var sb = new StringBuilder();

            while (queue.Count > 0)
            {
                string line = queue.Dequeue();
                sb.AppendLine(line);

                if (line.Trim().EndsWith("}"))
                {
                    // ネストを考慮
                    // 簡易実装: 最初の } で終了
                    return sb.ToString();
                }
            }

            return sb.ToString();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static MQOAttribute ParseAttribute(string line)
        {
            var match = Patterns.AttributeLine.Match(line);
            if (!match.Success)
                return null;

            string name = match.Groups[1].Value;
            string valueString = match.Groups[2].Value;

            var values = ParseFloatArray(valueString);
            if (values.Length == 0)
                return null;

            return new MQOAttribute
            {
                Name = name,
                Values = values
            };
        }

        private static float[] ParseFloatArray(string text)
        {
            var matches = Patterns.DecimalValue.Matches(text);
            var result = new float[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                result[i] = float.Parse(matches[i].Value);
            }
            return result;
        }

        private static float ParseFloat(string text)
        {
            if (float.TryParse(text, out float result))
                return result;
            return 0f;
        }

        private static void SkipBlock(Queue<string> queue)
        {
            int depth = 1;
            while (queue.Count > 0 && depth > 0)
            {
                string line = queue.Dequeue().Trim();
                if (line.EndsWith("{"))
                    depth++;
                else if (line.EndsWith("}"))
                    depth--;
            }
        }

        private static Encoding GetShiftJISEncoding()
        {
            // Unity/.NET Core用: CodePagesEncodingProviderを登録
#if NETCOREAPP || NET5_0_OR_GREATER
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch { }
#endif

            try
            {
                return Encoding.GetEncoding("Shift_JIS");
            }
            catch
            {
                try
                {
                    // 代替: コードページ番号で試行
                    return Encoding.GetEncoding(932);
                }
                catch
                {
                    // 最終フォールバック: UTF8
                    Debug.LogWarning("[MQOParser] Shift-JIS encoding not available, using UTF-8");
                    return Encoding.UTF8;
                }
            }
        }
    }
}
