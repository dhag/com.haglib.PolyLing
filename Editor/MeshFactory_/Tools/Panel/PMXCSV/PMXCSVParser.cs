// Assets/Editor/MeshFactory/PMX/Core/PMXCSVParser.cs
// PMX CSVファイルパーサー
// CSV形式のPMXファイルを解析してPMXDocumentを生成

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MeshFactory.PMX
{
    /// <summary>
    /// PMX CSVファイルパーサー
    /// </summary>
    public static class PMXCSVParser
    {
        // ================================================================
        // パブリックAPI
        // ================================================================

        /// <summary>
        /// ファイルからPMXドキュメントをパース
        /// </summary>
        public static PMXDocument ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"PMX CSV file not found: {filePath}");

            // UTF-8で読み込み（BOM付きにも対応）
            string content = File.ReadAllText(filePath, Encoding.UTF8);

            var document = Parse(content);
            document.FilePath = filePath;
            document.FileName = Path.GetFileName(filePath);

            return document;
        }

        /// <summary>
        /// 文字列からPMXドキュメントをパース
        /// </summary>
        public static PMXDocument Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                throw new ArgumentException("Content is null or empty");

            var document = new PMXDocument();
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // コメント行をスキップ
                if (line.StartsWith(";"))
                    continue;

                // 空行をスキップ
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // CSV行をパース
                var fields = ParseCSVLine(line);
                if (fields.Count == 0)
                    continue;

                // レコードタイプで分岐
                string recordType = fields[0];

                try
                {
                    switch (recordType)
                    {
                        case "PmxHeader":
                            ParseHeader(fields, document);
                            break;

                        case "PmxModelInfo":
                            ParseModelInfo(fields, document);
                            break;

                        case "PmxVertex":
                            ParseVertex(fields, document);
                            break;

                        case "PmxFace":
                            ParseFace(fields, document);
                            break;

                        case "PmxMaterial":
                            ParseMaterial(fields, document);
                            break;

                        case "PmxBone":
                            ParseBone(fields, document);
                            break;

                        case "PmxMorph":
                            ParseMorph(fields, document);
                            break;

                        case "PmxMorphOffset":
                            ParseMorphOffset(fields, document);
                            break;

                        case "PmxBody":
                            ParseBody(fields, document);
                            break;

                        case "PmxJoint":
                            ParseJoint(fields, document);
                            break;

                            // その他のレコードタイプは無視
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PMXCSVParser] Failed to parse line: {line}\nError: {ex.Message}");
                }
            }

            return document;
        }

        // ================================================================
        // CSVパース
        // ================================================================

        /// <summary>
        /// CSV行をフィールドに分割（ダブルクォート対応）
        /// </summary>
        private static List<string> ParseCSVLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    // ダブルクォートのエスケープ処理
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++; // 次の"をスキップ
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            // 最後のフィールド
            fields.Add(sb.ToString());

            return fields;
        }

        /// <summary>
        /// エスケープ文字列をデコード（\r\n, \" など）
        /// </summary>
        private static string DecodeEscapedString(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            // PMX CSVのエスケープ: \r\n, \., \\, etc.
            return s
                .Replace("\\r\\n", "\r\n")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\.", ".")
                .Replace("\\\\", "\\");
        }

        // ================================================================
        // レコードパース
        // ================================================================

        private static void ParseHeader(List<string> fields, PMXDocument document)
        {
            // PmxHeader,ver,文字エンコード,追加UV数
            if (fields.Count >= 2)
                document.Version = ParseFloat(fields[1]);
            if (fields.Count >= 3)
                document.CharacterEncoding = ParseInt(fields[2]);
            if (fields.Count >= 4)
                document.AdditionalUVCount = ParseInt(fields[3]);
        }

        private static void ParseModelInfo(List<string> fields, PMXDocument document)
        {
            // PmxModelInfo,モデル名,モデル名(英),コメント,コメント(英)
            if (fields.Count >= 2)
                document.ModelInfo.Name = fields[1];
            if (fields.Count >= 3)
                document.ModelInfo.NameEnglish = fields[2];
            if (fields.Count >= 4)
                document.ModelInfo.Comment = DecodeEscapedString(fields[3]);
            if (fields.Count >= 5)
                document.ModelInfo.CommentEnglish = DecodeEscapedString(fields[4]);
        }

        private static void ParseVertex(List<string> fields, PMXDocument document)
        {
            // PmxVertex,頂点Index,位置_x,位置_y,位置_z,法線_x,法線_y,法線_z,エッジ倍率,UV_u,UV_v,...
            if (fields.Count < 11)
                return;

            var vertex = new PMXVertex
            {
                Index = ParseInt(fields[1]),
                Position = new Vector3(ParseFloat(fields[2]), ParseFloat(fields[3]), ParseFloat(fields[4])),
                Normal = new Vector3(ParseFloat(fields[5]), ParseFloat(fields[6]), ParseFloat(fields[7])),
                EdgeScale = ParseFloat(fields[8]),
                UV = new Vector2(ParseFloat(fields[9]), ParseFloat(fields[10]))
            };

            // 追加UV（fields[11]からfields[26]まで、4つのVector4）
            if (fields.Count >= 27)
            {
                vertex.AdditionalUVs = new Vector4[4];
                for (int i = 0; i < 4; i++)
                {
                    int baseIdx = 11 + i * 4;
                    if (baseIdx + 3 < fields.Count)
                    {
                        vertex.AdditionalUVs[i] = new Vector4(
                            ParseFloat(fields[baseIdx]),
                            ParseFloat(fields[baseIdx + 1]),
                            ParseFloat(fields[baseIdx + 2]),
                            ParseFloat(fields[baseIdx + 3])
                        );
                    }
                }
            }

            // ウェイト変形タイプ（field[27]）
            if (fields.Count >= 28)
                vertex.WeightType = ParseInt(fields[27]);

            // ボーンウェイト（fields[28]からボーン名,ウェイトのペア最大4つ）
            var boneWeights = new List<PMXBoneWeight>();
            int weightBaseIdx = 28;
            for (int i = 0; i < 4; i++)
            {
                int nameIdx = weightBaseIdx + i * 2;
                int weightIdx = nameIdx + 1;

                if (weightIdx < fields.Count)
                {
                    string boneName = fields[nameIdx];
                    float weight = ParseFloat(fields[weightIdx]);

                    if (!string.IsNullOrEmpty(boneName) && weight > 0)
                    {
                        boneWeights.Add(new PMXBoneWeight
                        {
                            BoneName = boneName,
                            Weight = weight
                        });
                    }
                }
            }
            vertex.BoneWeights = boneWeights.ToArray();

            // SDEF座標（fields[36]から）
            int sdefBaseIdx = 36;
            if (fields.Count >= sdefBaseIdx + 9)
            {
                vertex.SDEF_C = new Vector3(
                    ParseFloat(fields[sdefBaseIdx]),
                    ParseFloat(fields[sdefBaseIdx + 1]),
                    ParseFloat(fields[sdefBaseIdx + 2])
                );
                vertex.SDEF_R0 = new Vector3(
                    ParseFloat(fields[sdefBaseIdx + 3]),
                    ParseFloat(fields[sdefBaseIdx + 4]),
                    ParseFloat(fields[sdefBaseIdx + 5])
                );
                vertex.SDEF_R1 = new Vector3(
                    ParseFloat(fields[sdefBaseIdx + 6]),
                    ParseFloat(fields[sdefBaseIdx + 7]),
                    ParseFloat(fields[sdefBaseIdx + 8])
                );
            }

            document.Vertices.Add(vertex);
        }

        private static void ParseFace(List<string> fields, PMXDocument document)
        {
            // CSVフォーマット:
            // [0] PmxFace
            // [1] マテリアル名
            // [2] 面Index
            // [3] 頂点Index1
            // [4] 頂点Index2
            // [5] 頂点Index3
            if (fields.Count < 6)
                return;

            var face = new PMXFace
            {
                MaterialName = fields[1],
                FaceIndex = ParseInt(fields[2]),
                VertexIndex1 = ParseInt(fields[3]),
                VertexIndex2 = ParseInt(fields[4]),
                VertexIndex3 = ParseInt(fields[5])
            };

            document.Faces.Add(face);
        }

        private static void ParseMaterial(List<string> fields, PMXDocument document)
        {
            // CSVフォーマット:
            // [0]  PmxMaterial
            // [1]  材質名
            // [2]  材質名(英)
            // [3-6]   拡散色_RGBA
            // [7-9]   反射色_RGB
            // [10]    反射強度
            // [11-13] 環境色_RGB
            // [14] 両面描画(0/1)
            // [15] 地面影(0/1)
            // [16] セルフ影マップ(0/1)
            // [17] セルフ影(0/1)
            // [18] 頂点色(0/1)
            // [19] 描画(0:Tri/1:Point/2:Line)
            // [20] エッジ(0/1)
            // [21] エッジサイズ
            // [22-25] エッジ色_RGBA
            // [26] テクスチャパス
            // [27] スフィアテクスチャパス
            // [28] スフィアモード
            // [29] Toonテクスチャパス
            // [30] メモ

            if (fields.Count < 3)
                return;

            var material = new PMXMaterial
            {
                Name = fields[1],
                NameEnglish = fields.Count > 2 ? fields[2] : ""
            };

            // 拡散色 [3-6]
            if (fields.Count >= 7)
            {
                material.Diffuse = new Color(
                    ParseFloat(fields[3]),
                    ParseFloat(fields[4]),
                    ParseFloat(fields[5]),
                    ParseFloat(fields[6])
                );
            }

            // 反射色 [7-9] + 反射強度 [10]
            if (fields.Count >= 11)
            {
                material.Specular = new Color(
                    ParseFloat(fields[7]),
                    ParseFloat(fields[8]),
                    ParseFloat(fields[9]),
                    1f
                );
                material.SpecularPower = ParseFloat(fields[10]);
            }

            // 環境色 [11-13]
            if (fields.Count >= 14)
            {
                material.Ambient = new Color(
                    ParseFloat(fields[11]),
                    ParseFloat(fields[12]),
                    ParseFloat(fields[13]),
                    1f
                );
            }

            // 描画フラグをビットフィールドとして構築 [14-20]
            if (fields.Count >= 21)
            {
                int flags = 0;
                if (ParseInt(fields[14]) != 0) flags |= 0x01; // 両面描画
                if (ParseInt(fields[15]) != 0) flags |= 0x02; // 地面影
                if (ParseInt(fields[16]) != 0) flags |= 0x04; // セルフ影マップ
                if (ParseInt(fields[17]) != 0) flags |= 0x08; // セルフ影
                if (ParseInt(fields[18]) != 0) flags |= 0x10; // 頂点色
                // [19] 描画モード (0:Tri/1:Point/2:Line) - 別フィールドで保持してもよい
                if (ParseInt(fields[20]) != 0) flags |= 0x20; // エッジ
                material.DrawFlags = flags;
            }

            // エッジサイズ [21] + エッジ色 [22-25]
            if (fields.Count >= 26)
            {
                material.EdgeSize = ParseFloat(fields[21]);
                material.EdgeColor = new Color(
                    ParseFloat(fields[22]),
                    ParseFloat(fields[23]),
                    ParseFloat(fields[24]),
                    ParseFloat(fields[25])
                );
            }

            // テクスチャパス [26]
            if (fields.Count >= 27)
                material.TexturePath = fields[26];

            // スフィアテクスチャ [27] + スフィアモード [28]
            if (fields.Count >= 29)
            {
                material.SphereTexturePath = fields[27];
                material.SphereMode = ParseInt(fields[28]);
            }

            // Toonテクスチャ [29]
            if (fields.Count >= 30)
            {
                material.ToonTexture = fields[29];
            }

            // メモ [30]
            if (fields.Count >= 31)
                material.Memo = DecodeEscapedString(fields[30]);

            document.Materials.Add(material);
        }

        private static void ParseBone(List<string> fields, PMXDocument document)
        {
            // CSVフォーマット（実際のヘッダー順序）:
            // [0]  PmxBone
            // [1]  ボーン名
            // [2]  ボーン名(英)
            // [3]  変形階層
            // [4]  物理後(0/1)
            // [5]  位置_x
            // [6]  位置_y
            // [7]  位置_z
            // [8]  回転(0/1)
            // [9]  移動(0/1)
            // [10] IK(0/1)
            // [11] 表示(0/1)
            // [12] 操作(0/1)
            // [13] 親ボーン名
            // [14] 表示先(0:オフセット/1:ボーン)
            // [15] 表示先ボーン名
            // [16] 表示先オフセット_x
            // [17] 表示先オフセット_y
            // [18] 表示先オフセット_z
            // [19-37] ローカル付与、軸制限、ローカル軸、外部親、IK設定...

            if (fields.Count < 14)
                return;

            var bone = new PMXBone
            {
                Name = fields[1],
                NameEnglish = fields.Count > 2 ? fields[2] : "",
                TransformLevel = ParseInt(fields[3]),
                Position = new Vector3(
                    ParseFloat(fields[5]),
                    ParseFloat(fields[6]),
                    ParseFloat(fields[7])
                ),
                ParentBoneName = fields[13]
            };

            // フラグをビットフィールドとして構築
            // [8] 回転, [9] 移動, [10] IK, [11] 表示, [12] 操作
            int flags = 0;
            if (ParseInt(fields[8]) != 0) flags |= 0x0002;  // 回転可能
            if (ParseInt(fields[9]) != 0) flags |= 0x0004;  // 移動可能
            if (ParseInt(fields[10]) != 0) flags |= 0x0020; // IK
            if (ParseInt(fields[11]) != 0) flags |= 0x0008; // 表示
            if (ParseInt(fields[12]) != 0) flags |= 0x0010; // 操作可能
            bone.Flags = flags;

            // 接続先
            if (fields.Count > 15)
            {
                bone.ConnectBoneName = fields[15];
            }
            if (fields.Count > 18)
            {
                bone.ConnectOffset = new Vector3(
                    ParseFloat(fields[16]),
                    ParseFloat(fields[17]),
                    ParseFloat(fields[18])
                );
            }

            document.Bones.Add(bone);
        }

        private static void ParseMorph(List<string> fields, PMXDocument document)
        {
            // PmxMorph,モーフ名,モーフ名(英),パネル,モーフ種類,オフセット数
            if (fields.Count < 5)
                return;

            var morph = new PMXMorph
            {
                Name = fields[1],
                NameEnglish = fields.Count > 2 ? fields[2] : "",
                Panel = ParseInt(fields[3]),
                MorphType = ParseInt(fields[4])
            };

            document.Morphs.Add(morph);
        }

        private static void ParseMorphOffset(List<string> fields, PMXDocument document)
        {
            // PmxMorphOffset,親モーフ名,オフセットIndex,...
            // 現在はスキップ（将来の実装用）
        }

        private static void ParseBody(List<string> fields, PMXDocument document)
        {
            // PmxBody,剛体名,剛体名(英),関連ボーン名,剛体タイプ,...
            if (fields.Count < 5)
                return;

            var body = new PMXBody
            {
                Name = fields[1],
                NameEnglish = fields.Count > 2 ? fields[2] : "",
                RelatedBoneName = fields[3],
                BodyType = ParseInt(fields[4])
            };

            if (fields.Count >= 6)
                body.Group = ParseInt(fields[5]);

            if (fields.Count >= 7)
                body.NonCollisionGroups = fields[6];

            if (fields.Count >= 8)
                body.Shape = ParseInt(fields[7]);

            // サイズ
            if (fields.Count >= 11)
            {
                body.Size = new Vector3(
                    ParseFloat(fields[8]),
                    ParseFloat(fields[9]),
                    ParseFloat(fields[10])
                );
            }

            // 位置
            if (fields.Count >= 14)
            {
                body.Position = new Vector3(
                    ParseFloat(fields[11]),
                    ParseFloat(fields[12]),
                    ParseFloat(fields[13])
                );
            }

            // 回転
            if (fields.Count >= 17)
            {
                body.Rotation = new Vector3(
                    ParseFloat(fields[14]),
                    ParseFloat(fields[15]),
                    ParseFloat(fields[16])
                );
            }

            // 物理パラメータ
            if (fields.Count >= 22)
            {
                body.Mass = ParseFloat(fields[17]);
                body.LinearDamping = ParseFloat(fields[18]);
                body.AngularDamping = ParseFloat(fields[19]);
                body.Restitution = ParseFloat(fields[20]);
                body.Friction = ParseFloat(fields[21]);
            }

            document.Bodies.Add(body);
        }

        private static void ParseJoint(List<string> fields, PMXDocument document)
        {
            // PmxJoint,Joint名,Joint名(英),剛体名A,剛体名B,Jointタイプ,...
            if (fields.Count < 6)
                return;

            var joint = new PMXJoint
            {
                Name = fields[1],
                NameEnglish = fields.Count > 2 ? fields[2] : "",
                BodyAName = fields[3],
                BodyBName = fields[4],
                JointType = ParseInt(fields[5])
            };

            // 位置
            if (fields.Count >= 9)
            {
                joint.Position = new Vector3(
                    ParseFloat(fields[6]),
                    ParseFloat(fields[7]),
                    ParseFloat(fields[8])
                );
            }

            // 回転
            if (fields.Count >= 12)
            {
                joint.Rotation = new Vector3(
                    ParseFloat(fields[9]),
                    ParseFloat(fields[10]),
                    ParseFloat(fields[11])
                );
            }

            // 移動制限
            if (fields.Count >= 18)
            {
                joint.TranslationMin = new Vector3(
                    ParseFloat(fields[12]),
                    ParseFloat(fields[13]),
                    ParseFloat(fields[14])
                );
                joint.TranslationMax = new Vector3(
                    ParseFloat(fields[15]),
                    ParseFloat(fields[16]),
                    ParseFloat(fields[17])
                );
            }

            // 回転制限
            if (fields.Count >= 24)
            {
                joint.RotationMin = new Vector3(
                    ParseFloat(fields[18]),
                    ParseFloat(fields[19]),
                    ParseFloat(fields[20])
                );
                joint.RotationMax = new Vector3(
                    ParseFloat(fields[21]),
                    ParseFloat(fields[22]),
                    ParseFloat(fields[23])
                );
            }

            // バネ定数
            if (fields.Count >= 30)
            {
                joint.SpringTranslation = new Vector3(
                    ParseFloat(fields[24]),
                    ParseFloat(fields[25]),
                    ParseFloat(fields[26])
                );
                joint.SpringRotation = new Vector3(
                    ParseFloat(fields[27]),
                    ParseFloat(fields[28]),
                    ParseFloat(fields[29])
                );
            }

            document.Joints.Add(joint);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static float ParseFloat(string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0f;
            if (float.TryParse(s, out float result))
                return result;
            return 0f;
        }

        private static int ParseInt(string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0;
            if (int.TryParse(s, out int result))
                return result;
            return 0;
        }
    }
}