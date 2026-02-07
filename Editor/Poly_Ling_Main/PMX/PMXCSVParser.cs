// Assets/Editor/Poly_Ling/PMX/Core/PMXCSVParser.cs
// PMX CSVファイルパーサー — PMXEditor互換フォーマット

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.CSV;

namespace Poly_Ling.PMX
{
    public static class PMXCSVParser
    {
        public static PMXDocument ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"PMX CSV file not found: {filePath}");
            string content = File.ReadAllText(filePath, Encoding.UTF8);
            var document = Parse(content);
            document.FilePath = filePath;
            document.FileName = Path.GetFileName(filePath);
            return document;
        }

        public static PMXDocument Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                throw new ArgumentException("Content is null or empty");

            var document = new PMXDocument();
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            PMXDisplayFrame currentFrame = null;

            foreach (var line in lines)
            {
                if (line.StartsWith(";") || string.IsNullOrWhiteSpace(line))
                    continue;

                var fields = ParseCSVLine(line);
                if (fields.Count == 0) continue;
                string rt = fields[0];

                try
                {
                    switch (rt)
                    {
                        case "PmxHeader":   ParseHeader(fields, document); break;
                        case "PmxModelInfo":ParseModelInfo(fields, document); break;
                        case "PmxVertex":   ParseVertex(fields, document); break;
                        case "PmxFace":     ParseFace(fields, document); break;
                        case "PmxMaterial": ParseMaterial(fields, document); break;
                        case "PmxBone":     ParseBone(fields, document); break;
                        case "PmxIKLink":   ParseIKLink(fields, document); break;
                        case "PmxMorph":    ParseMorph(fields, document); break;
                        case "PmxVertexMorph":   ParseVertexMorph(fields, document); break;
                        case "PmxUVMorph":       ParseUVMorph(fields, document); break;
                        case "PmxBoneMorph":     ParseBoneMorph(fields, document); break;
                        case "PmxMaterialMorph": ParseMaterialMorph(fields, document); break;
                        case "PmxGroupMorph":    ParseGroupMorph(fields, document); break;
                        case "PmxImpulseMorph":  ParseImpulseMorph(fields, document); break;
                        case "PmxMorphOffset":   ParseLegacyMorphOffset(fields, document); break;
                        case "PmxNode":
                            currentFrame = ParseNode(fields, document);
                            break;
                        case "PmxNodeItem":
                            ParseNodeItem(fields, document, currentFrame);
                            break;
                        case "PmxBody":     ParseBody(fields, document); break;
                        case "PmxJoint":    ParseJoint(fields, document); break;
                        case "PmxSoftBody": ParseSoftBody(fields, document); break;
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
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    { sb.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }

        private static string DecodeEscapedString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\r\\n", "\r\n").Replace("\\n", "\n").Replace("\\r", "\r")
                    .Replace("\\ ", " ").Replace("\\.", ".").Replace("\\\\", "\\");
        }

        // ================================================================
        // レコードパース
        // ================================================================

        private static void ParseHeader(List<string> fields, PMXDocument doc)
        {
            if (fields.Count >= 2) doc.Version = PF(fields[1]);
            if (fields.Count >= 3) doc.CharacterEncoding = PI(fields[2]);
            if (fields.Count >= 4) doc.AdditionalUVCount = PI(fields[3]);
        }

        private static void ParseModelInfo(List<string> fields, PMXDocument doc)
        {
            if (fields.Count >= 2) doc.ModelInfo.Name = fields[1];
            if (fields.Count >= 3) doc.ModelInfo.NameEnglish = fields[2];
            if (fields.Count >= 4) doc.ModelInfo.Comment = DecodeEscapedString(fields[3]);
            if (fields.Count >= 5) doc.ModelInfo.CommentEnglish = DecodeEscapedString(fields[4]);
        }

        private static void ParseVertex(List<string> fields, PMXDocument doc)
        {
            // PmxVertex,[1]Index,[2]px,[3]py,[4]pz,[5]nx,[6]ny,[7]nz,[8]edge,[9]u,[10]v,
            // [11-26]追加UV(4xVector4),[27]WeightType,
            // [28]bone1,[29]w1,[30]bone2,[31]w2,[32]bone3,[33]w3,[34]bone4,[35]w4,
            // [36]Cx,[37]Cy,[38]Cz,[39]R0x,[40]R0y,[41]R0z,[42]R1x,[43]R1y,[44]R1z
            if (fields.Count < 11) return;

            var v = new PMXVertex
            {
                Index = PI(fields[1]),
                Position = new Vector3(PF(fields[2]), PF(fields[3]), PF(fields[4])),
                Normal = new Vector3(PF(fields[5]), PF(fields[6]), PF(fields[7])),
                EdgeScale = PF(fields[8]),
                UV = new Vector2(PF(fields[9]), PF(fields[10]))
            };

            // 追加UV
            if (fields.Count >= 27)
            {
                v.AdditionalUVs = new Vector4[4];
                for (int i = 0; i < 4; i++)
                {
                    int bi = 11 + i * 4;
                    if (bi + 3 < fields.Count)
                        v.AdditionalUVs[i] = new Vector4(PF(fields[bi]), PF(fields[bi+1]), PF(fields[bi+2]), PF(fields[bi+3]));
                }
            }

            // WeightType
            if (fields.Count >= 28)
                v.WeightType = PI(fields[27]);

            // BoneWeights
            if (fields.Count >= 36)
            {
                var bw = new List<PMXBoneWeight>();
                for (int i = 0; i < 4; i++)
                {
                    int bi = 28 + i * 2;
                    if (bi + 1 < fields.Count)
                    {
                        string bn = fields[bi];
                        float w = PF(fields[bi + 1]);
                        bw.Add(new PMXBoneWeight { BoneName = bn, Weight = w });
                    }
                }
                v.BoneWeights = bw.ToArray();
            }

            // SDEF
            if (fields.Count >= 45)
            {
                v.SDEF_C = new Vector3(PF(fields[36]), PF(fields[37]), PF(fields[38]));
                v.SDEF_R0 = new Vector3(PF(fields[39]), PF(fields[40]), PF(fields[41]));
                v.SDEF_R1 = new Vector3(PF(fields[42]), PF(fields[43]), PF(fields[44]));
            }

            doc.Vertices.Add(v);
        }

        private static void ParseFace(List<string> fields, PMXDocument doc)
        {
            if (fields.Count < 6) return;
            doc.Faces.Add(new PMXFace
            {
                MaterialName = fields[1],
                FaceIndex = PI(fields[2]),
                VertexIndex1 = PI(fields[3]),
                VertexIndex2 = PI(fields[4]),
                VertexIndex3 = PI(fields[5])
            });
        }

        private static void ParseMaterial(List<string> fields, PMXDocument doc)
        {
            // PmxMaterial,[1]名,[2]名英,[3-6]Diffuse RGBA,[7-9]Specular RGB,[10]SpecPow,
            // [11-13]Ambient RGB,[14]両面,[15]地面影,[16]セルフ影マップ,[17]セルフ影,[18]頂点色,
            // [19]描画,[20]エッジ,[21]エッジサイズ,[22-25]エッジ色RGBA,
            // [26]テクスチャ,[27]スフィア,[28]スフィアモード,[29]Toon,[30]メモ
            if (fields.Count < 14) return;

            var mat = new PMXMaterial
            {
                Name = fields[1],
                NameEnglish = S(fields, 2),
                Diffuse = new Color(PF(fields[3]), PF(fields[4]), PF(fields[5]), PF(fields[6])),
                Specular = new Color(PF(fields[7]), PF(fields[8]), PF(fields[9]), 1),
                SpecularPower = PF(fields[10]),
                Ambient = new Color(PF(fields[11]), PF(fields[12]), PF(fields[13]), 1)
            };

            // DrawFlags復元
            if (fields.Count >= 21)
            {
                int flags = 0;
                if (PI(fields[14]) != 0) flags |= 0x01; // 両面
                if (PI(fields[15]) != 0) flags |= 0x02; // 地面影
                if (PI(fields[16]) != 0) flags |= 0x04; // セルフ影マップ
                if (PI(fields[17]) != 0) flags |= 0x08; // セルフ影
                // fields[18]: 頂点色 (拡張) — 未使用
                // fields[19]: 描画モード (拡張) — 未使用
                if (PI(fields[20]) != 0) flags |= 0x10; // エッジ
                mat.DrawFlags = flags;
            }

            if (fields.Count >= 26)
            {
                mat.EdgeSize = PF(fields[21]);
                mat.EdgeColor = new Color(PF(fields[22]), PF(fields[23]), PF(fields[24]), PF(fields[25]));
            }

            if (fields.Count >= 27) mat.TexturePath = fields[26];
            if (fields.Count >= 28) mat.SphereTexturePath = fields[27];
            if (fields.Count >= 29) mat.SphereMode = PI(fields[28]);
            if (fields.Count >= 30) mat.ToonTexturePath = fields[29];
            if (fields.Count >= 31) mat.Memo = fields[30];

            doc.Materials.Add(mat);
        }

        private static void ParseBone(List<string> fields, PMXDocument doc)
        {
            // PmxBoneCSVParser と同一ロジックを使うため、CSVRow経由でパースして変換
            var row = new CSVRow(fields.ToArray());
            var schema = _boneSchema;

            if (!schema.IsValidDataRow(row)) return;

            var data = ParseBoneRow(row, schema);
            if (data == null) return;

            var bone = ConvertToPmxBone(data);
            doc.Bones.Add(bone);
        }

        private static void ParseIKLink(List<string> fields, PMXDocument doc)
        {
            // PmxIKLink,[1]親ボーン名,[2]Linkボーン名,[3]角度制限,[4]XL,[5]XH,[6]YL,[7]YH,[8]ZL,[9]ZH
            if (fields.Count < 4 || doc.Bones.Count == 0) return;

            string parentName = fields[1];
            PMXBone parent = null;
            for (int i = doc.Bones.Count - 1; i >= 0; i--)
            {
                if (doc.Bones[i].Name == parentName) { parent = doc.Bones[i]; break; }
            }
            if (parent == null) return;

            float d2r = Mathf.PI / 180f;
            var link = new PMXIKLink
            {
                BoneName = fields[2],
                HasLimit = PI(fields[3]) != 0
            };
            if (link.HasLimit && fields.Count >= 10)
            {
                link.LimitMin = new Vector3(PF(fields[4]) * d2r, PF(fields[6]) * d2r, PF(fields[8]) * d2r);
                link.LimitMax = new Vector3(PF(fields[5]) * d2r, PF(fields[7]) * d2r, PF(fields[9]) * d2r);
            }
            parent.IKLinks.Add(link);
        }

        // ================================================================
        // PmxBoneData パース（PmxBoneCSVSchema共有）
        // ================================================================

        private static readonly PmxBoneCSVSchema _boneSchema = new PmxBoneCSVSchema();

        /// <summary>PmxBone行をPmxBoneDataにパース（スキーマ共有）</summary>
        private static PmxBoneData ParseBoneRow(CSVRow row, PmxBoneCSVSchema schema)
        {
            try
            {
                var data = new PmxBoneData
                {
                    Name = row.Get(schema.BoneName),
                    NameEn = row.Get(schema.BoneNameEn),
                    DeformHierarchy = row.GetInt(schema.DeformHierarchy, 0),
                    IsPhysicsAfter = row.GetBool(schema.PhysicsAfter),
                    Position = schema.GetPosition(row),
                    CanRotate = row.GetBool(schema.CanRotate, true),
                    CanMove = row.GetBool(schema.CanMove),
                    IsIK = row.GetBool(schema.IsIK),
                    IsVisible = row.GetBool(schema.IsVisible, true),
                    IsControllable = row.GetBool(schema.IsControllable, true),
                    ParentName = row.Get(schema.ParentName)
                };

                if (schema.HasExtendedColumns(row))
                {
                    data.HasExtendedData = true;
                    data.ConnectType = row.GetInt(schema.ConnectType, 0);
                    data.ConnectBoneName = row.Get(schema.ConnectBoneName);
                    data.ConnectOffset = schema.GetConnectOffset(row);
                    data.LocalGrant = row.GetBool(schema.LocalGrant);
                    data.GrantRotation = row.GetBool(schema.GrantRotation);
                    data.GrantTranslation = row.GetBool(schema.GrantTranslation);
                    data.GrantRate = row.GetFloat(schema.GrantRate);
                    data.GrantParentName = row.Get(schema.GrantParentName);
                    data.HasFixedAxis = row.GetBool(schema.HasFixedAxis);
                    data.FixedAxis = schema.GetFixedAxis(row);
                    data.HasLocalAxis = row.GetBool(schema.HasLocalAxis);
                    data.LocalAxisX = schema.GetLocalAxisX(row);
                    data.LocalAxisZ = schema.GetLocalAxisZ(row);
                    data.HasExternalParent = row.GetBool(schema.HasExternalParent);
                    data.ExternalParentKey = row.GetInt(schema.ExternalParentKey);
                    data.IKTargetName = row.Get(schema.IKTargetName);
                    data.IKLoopCount = row.GetInt(schema.IKLoopCount);
                    data.IKLimitAngleDeg = row.GetFloat(schema.IKLimitAngleDeg);
                }

                return data;
            }
            catch { return null; }
        }

        /// <summary>PmxBoneData → PMXBone 変換</summary>
        public static PMXBone ConvertToPmxBone(PmxBoneData data)
        {
            var bone = new PMXBone
            {
                Name = data.Name,
                NameEnglish = data.NameEn ?? "",
                TransformLevel = data.DeformHierarchy,
                Position = data.Position,
                ParentBoneName = data.ParentName,
                Flags = data.BuildPmxFlags()
            };

            if (data.HasExtendedData)
            {
                bone.ConnectBoneName = data.ConnectBoneName;
                bone.ConnectOffset = data.ConnectOffset;
                bone.GrantRate = data.GrantRate;
                bone.GrantParentBoneName = data.GrantParentName;
                bone.FixedAxis = data.FixedAxis;
                bone.LocalAxisX = data.LocalAxisX;
                bone.LocalAxisZ = data.LocalAxisZ;
                bone.ExternalParentKey = data.ExternalParentKey;
                bone.IKTargetBoneName = data.IKTargetName;
                bone.IKLoopCount = data.IKLoopCount;
                bone.IKLimitAngle = data.IKLimitAngleDeg * (Mathf.PI / 180f);

                // IKLinks
                foreach (var lk in data.IKLinks)
                {
                    float d2r = Mathf.PI / 180f;
                    bone.IKLinks.Add(new PMXIKLink
                    {
                        BoneName = lk.BoneName,
                        HasLimit = lk.HasLimit,
                        LimitMin = lk.LimitMinDeg * d2r,
                        LimitMax = lk.LimitMaxDeg * d2r
                    });
                }
            }

            return bone;
        }

        private static void ParseMorph(List<string> fields, PMXDocument doc)
        {
            if (fields.Count < 5) return;
            doc.Morphs.Add(new PMXMorph
            {
                Name = fields[1],
                NameEnglish = S(fields, 2),
                Panel = PI(fields[3]),
                MorphType = PI(fields[4])
            });
        }

        private static PMXMorph FindMorph(PMXDocument doc, string parentName)
        {
            for (int i = doc.Morphs.Count - 1; i >= 0; i--)
                if (doc.Morphs[i].Name == parentName) return doc.Morphs[i];
            return null;
        }

        private static void ParseVertexMorph(List<string> fields, PMXDocument doc)
        {
            // PmxVertexMorph,[1]親モーフ名,[2]offsetIdx,[3]vertexIdx,[4]x,[5]y,[6]z
            if (fields.Count < 7) return;
            var morph = FindMorph(doc, fields[1]);
            if (morph == null) return;
            morph.Offsets.Add(new PMXVertexMorphOffset
            {
                VertexIndex = PI(fields[3]),
                Offset = new Vector3(PF(fields[4]), PF(fields[5]), PF(fields[6]))
            });
        }

        private static void ParseUVMorph(List<string> fields, PMXDocument doc)
        {
            // PmxUVMorph,[1]親,[2]idx,[3]vertexIdx,[4]x,[5]y,[6]z,[7]w
            if (fields.Count < 8) return;
            var morph = FindMorph(doc, fields[1]);
            if (morph == null) return;
            morph.Offsets.Add(new PMXUVMorphOffset
            {
                VertexIndex = PI(fields[3]),
                Offset = new Vector4(PF(fields[4]), PF(fields[5]), PF(fields[6]), PF(fields[7]))
            });
        }

        private static void ParseBoneMorph(List<string> fields, PMXDocument doc)
        {
            // PmxBoneMorph,[1]親,[2]idx,[3]ボーン名,[4-6]移動xyz,[7-10]回転xyzw
            if (fields.Count < 11) return;
            var morph = FindMorph(doc, fields[1]);
            if (morph == null) return;
            morph.Offsets.Add(new PMXBoneMorphOffset
            {
                BoneName = fields[3],
                Translation = new Vector3(PF(fields[4]), PF(fields[5]), PF(fields[6])),
                Rotation = new Quaternion(PF(fields[7]), PF(fields[8]), PF(fields[9]), PF(fields[10]))
            });
        }

        private static void ParseMaterialMorph(List<string> fields, PMXDocument doc)
        {
            // PmxMaterialMorph,[1]親,[2]idx,[3]材質名,[4]演算タイプ,
            // [5-8]Diffuse,[9-11]Specular,[12]SpecPow,[13-15]Ambient,
            // [16]EdgeSize,[17-20]EdgeColor,[21-24]Tex,[25-28]Sphere,[29-32]Toon
            if (fields.Count < 33) return;
            var morph = FindMorph(doc, fields[1]);
            if (morph == null) return;
            morph.Offsets.Add(new PMXMaterialMorphOffset
            {
                MaterialName = fields[3],
                Operation = (byte)PI(fields[4]),
                Diffuse = new Color(PF(fields[5]), PF(fields[6]), PF(fields[7]), PF(fields[8])),
                Specular = new Color(PF(fields[9]), PF(fields[10]), PF(fields[11]), 1),
                SpecularPower = PF(fields[12]),
                Ambient = new Color(PF(fields[13]), PF(fields[14]), PF(fields[15]), 1),
                EdgeSize = PF(fields[16]),
                EdgeColor = new Color(PF(fields[17]), PF(fields[18]), PF(fields[19]), PF(fields[20])),
                TextureCoef = new Color(PF(fields[21]), PF(fields[22]), PF(fields[23]), PF(fields[24])),
                SphereCoef = new Color(PF(fields[25]), PF(fields[26]), PF(fields[27]), PF(fields[28])),
                ToonCoef = new Color(PF(fields[29]), PF(fields[30]), PF(fields[31]), PF(fields[32]))
            });
        }

        private static void ParseGroupMorph(List<string> fields, PMXDocument doc)
        {
            // PmxGroupMorph,[1]親,[2]idx,[3]モーフ名,[4]影響度
            if (fields.Count < 5) return;
            var morph = FindMorph(doc, fields[1]);
            if (morph == null) return;
            morph.Offsets.Add(new PMXGroupMorphOffset
            {
                MorphName = fields[3],
                Weight = PF(fields[4])
            });
        }

        private static void ParseImpulseMorph(List<string> fields, PMXDocument doc)
        {
            // PmxImpulseMorph,[1]親,[2]idx,[3]剛体Index,[4]ローカル,[5-7]速度,[8-10]トルク
            if (fields.Count < 11) return;
            var morph = FindMorph(doc, fields[1]);
            if (morph == null) return;
            morph.Offsets.Add(new PMXImpulseMorphOffset
            {
                RigidBodyIndex = PI(fields[3]),
                IsLocal = PI(fields[4]) != 0,
                Velocity = new Vector3(PF(fields[5]), PF(fields[6]), PF(fields[7])),
                Torque = new Vector3(PF(fields[8]), PF(fields[9]), PF(fields[10]))
            });
        }

        // 旧形式互換: MorphOffset → VertexMorph
        private static void ParseLegacyMorphOffset(List<string> fields, PMXDocument doc)
        {
            if (fields.Count < 5 || doc.Morphs.Count == 0) return;
            var morph = doc.Morphs[doc.Morphs.Count - 1];
            morph.Offsets.Add(new PMXVertexMorphOffset
            {
                VertexIndex = PI(fields[1]),
                Offset = new Vector3(PF(fields[2]), PF(fields[3]), PF(fields[4]))
            });
        }

        private static PMXDisplayFrame ParseNode(List<string> fields, PMXDocument doc)
        {
            // PmxNode,[1]名,[2]名英
            if (fields.Count < 2) return null;
            var frame = new PMXDisplayFrame
            {
                Name = fields[1],
                NameEnglish = S(fields, 2)
            };
            doc.DisplayFrames.Add(frame);
            return frame;
        }

        private static void ParseNodeItem(List<string> fields, PMXDocument doc, PMXDisplayFrame currentFrame)
        {
            // PmxNodeItem,[1]親表示枠名,[2]対象(0:ボーン/1:モーフ),[3]名前
            if (fields.Count < 4) return;

            // 親フレーム探索
            PMXDisplayFrame frame = currentFrame;
            if (frame == null || frame.Name != fields[1])
            {
                frame = null;
                for (int i = doc.DisplayFrames.Count - 1; i >= 0; i--)
                    if (doc.DisplayFrames[i].Name == fields[1]) { frame = doc.DisplayFrames[i]; break; }
            }
            if (frame == null) return;

            frame.Elements.Add(new PMXDisplayElement
            {
                IsMorph = PI(fields[2]) != 0,
                Name = fields[3]
            });
        }

        private static void ParseBody(List<string> fields, PMXDocument doc)
        {
            if (fields.Count < 5) return;
            var body = new PMXRigidBody
            {
                Name = fields[1],
                NameEnglish = S(fields, 2),
                RelatedBoneName = fields[3],
                PhysicsMode = PI(fields[4])
            };
            if (fields.Count >= 6) body.Group = PI(fields[5]);
            if (fields.Count >= 7) body.NonCollisionGroups = fields[6];
            if (fields.Count >= 8) body.Shape = PI(fields[7]);
            if (fields.Count >= 11) body.Size = new Vector3(PF(fields[8]), PF(fields[9]), PF(fields[10]));
            if (fields.Count >= 14) body.Position = new Vector3(PF(fields[11]), PF(fields[12]), PF(fields[13]));
            if (fields.Count >= 17) body.Rotation = new Vector3(PF(fields[14]), PF(fields[15]), PF(fields[16]));
            if (fields.Count >= 22)
            {
                body.Mass = PF(fields[17]); body.LinearDamping = PF(fields[18]);
                body.AngularDamping = PF(fields[19]); body.Restitution = PF(fields[20]); body.Friction = PF(fields[21]);
            }
            doc.RigidBodies.Add(body);
        }

        private static void ParseJoint(List<string> fields, PMXDocument doc)
        {
            if (fields.Count < 6) return;
            var j = new PMXJoint
            {
                Name = fields[1], NameEnglish = S(fields, 2),
                BodyAName = fields[3], BodyBName = fields[4], JointType = PI(fields[5])
            };
            if (fields.Count >= 9) j.Position = new Vector3(PF(fields[6]), PF(fields[7]), PF(fields[8]));
            if (fields.Count >= 12) j.Rotation = new Vector3(PF(fields[9]), PF(fields[10]), PF(fields[11]));
            if (fields.Count >= 18)
            {
                j.TranslationMin = new Vector3(PF(fields[12]), PF(fields[13]), PF(fields[14]));
                j.TranslationMax = new Vector3(PF(fields[15]), PF(fields[16]), PF(fields[17]));
            }
            if (fields.Count >= 24)
            {
                j.RotationMin = new Vector3(PF(fields[18]), PF(fields[19]), PF(fields[20]));
                j.RotationMax = new Vector3(PF(fields[21]), PF(fields[22]), PF(fields[23]));
            }
            if (fields.Count >= 30)
            {
                j.SpringTranslation = new Vector3(PF(fields[24]), PF(fields[25]), PF(fields[26]));
                j.SpringRotation = new Vector3(PF(fields[27]), PF(fields[28]), PF(fields[29]));
            }
            doc.Joints.Add(j);
        }

        private static void ParseSoftBody(List<string> fields, PMXDocument doc)
        {
            // PmxSoftBody,[1]名,[2]名英,[3]形状,[4]材質名,[5]グループ,[6]非衝突,[7]BLink,[8]BLink距離,
            // [9]クラスタ,[10]クラスタ数,[11]リンク交雑,[12]総質量,[13]マージン,[14]Aero,
            // [15-22]VCF~MT,[23-26]CHR~AHR,[27-29]SRHR~SSHR,[30-32]SR~SS_SPLT,
            // [33-36]V_IT~C_IT,[37-39]LST,AST,VST
            if (fields.Count < 15) return;
            var s = new PMXSoftBody
            {
                Name = fields[1], NameEnglish = S(fields, 2),
                Shape = (byte)PI(fields[3]),
                // MaterialIndex will need resolution from name
                Group = (byte)PI(fields[5]),
                BendingLinkDistance = PI(fields[8]),
                ClusterCount = PI(fields[10]),
                TotalMass = PF(fields[12]),
                Margin = PF(fields[13]),
                AeroModel = PI(fields[14])
            };
            byte fl = 0;
            if (PI(fields[7]) != 0) fl |= 0x01;
            if (PI(fields[9]) != 0) fl |= 0x02;
            if (PI(fields[11]) != 0) fl |= 0x04;
            s.Flags = fl;

            if (fields.Count >= 23) { s.VCF=PF(fields[15]); s.DP=PF(fields[16]); s.DG=PF(fields[17]); s.LF=PF(fields[18]); s.PR=PF(fields[19]); s.VC=PF(fields[20]); s.DF=PF(fields[21]); s.MT=PF(fields[22]); }
            if (fields.Count >= 27) { s.CHR=PF(fields[23]); s.KHR=PF(fields[24]); s.SHR=PF(fields[25]); s.AHR=PF(fields[26]); }
            if (fields.Count >= 30) { s.SRHR_CL=PF(fields[27]); s.SKHR_CL=PF(fields[28]); s.SSHR_CL=PF(fields[29]); }
            if (fields.Count >= 33) { s.SR_SPLT_CL=PF(fields[30]); s.SK_SPLT_CL=PF(fields[31]); s.SS_SPLT_CL=PF(fields[32]); }
            if (fields.Count >= 37) { s.V_IT=PI(fields[33]); s.P_IT=PI(fields[34]); s.D_IT=PI(fields[35]); s.C_IT=PI(fields[36]); }
            if (fields.Count >= 40) { s.LST=PF(fields[37]); s.AST=PF(fields[38]); s.VST=PF(fields[39]); }

            doc.SoftBodies.Add(s);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static float PF(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float r);
            return r;
        }

        private static int PI(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int.TryParse(s, out int r);
            return r;
        }

        private static string S(List<string> fields, int idx)
        {
            return fields.Count > idx ? fields[idx] : "";
        }
    }
}
