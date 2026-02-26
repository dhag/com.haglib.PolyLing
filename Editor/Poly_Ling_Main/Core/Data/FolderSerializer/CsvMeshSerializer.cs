// Assets/Editor/Poly_Ling/Serialization/FolderSerializer/CsvMeshSerializer.cs
// メッシュ/ボーン/モーフのCSVファイル読み書き
// 1ファイルに複数メッシュを "---" 区切りで格納

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.Tools;

namespace Poly_Ling.Serialization.FolderSerializer
{
    /// <summary>
    /// メッシュ1個分の読み取り結果
    /// </summary>
    public class CsvMeshEntry
    {
        public int GlobalIndex;
        public MeshContext MeshContext;
    }

    /// <summary>
    /// メッシュ/ボーン/モーフCSVの読み書き
    /// </summary>
    public static class CsvMeshSerializer
    {
        private const string Separator = "---";
        private const string VersionMesh = "#PolyLing_Mesh,version,1.0";
        private const string VersionBone = "#PolyLing_Bone,version,1.0";
        private const string VersionMorph = "#PolyLing_Morph,version,1.0";

        // ================================================================
        // Write
        // ================================================================

        /// <summary>
        /// メッシュリストをCSVファイルに書き込み
        /// </summary>
        public static void WriteFile(string path, List<CsvMeshEntry> entries, string fileType)
        {
            if (entries == null || entries.Count == 0) return;

            var sb = new StringBuilder();

            // ヘッダ
            switch (fileType)
            {
                case "bone": sb.AppendLine(VersionBone); break;
                case "morph": sb.AppendLine(VersionMorph); break;
                default: sb.AppendLine(VersionMesh); break;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var mc = entry.MeshContext;
                if (mc == null) continue;

                sb.AppendLine(Separator);
                WriteMeshHeader(sb, entry.GlobalIndex, mc);

                // ボーン固有データ
                if (mc.Type == MeshType.Bone)
                {
                    WriteBoneData(sb, mc);
                }

                // モーフ固有データ
                if (mc.Type == MeshType.Morph)
                {
                    WriteMorphData(sb, mc);
                }

                // 選択セット
                WriteSelectionSets(sb, mc);

                // 頂点
                if (mc.MeshObject != null)
                {
                    foreach (var v in mc.MeshObject.Vertices)
                    {
                        WriteVertex(sb, v);
                    }

                    // 面
                    foreach (var f in mc.MeshObject.Faces)
                    {
                        WriteFace(sb, f);
                    }
                }
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        // ================================================================
        // Read
        // ================================================================

        /// <summary>
        /// CSVファイルからメッシュリストを読み込み
        /// </summary>
        public static List<CsvMeshEntry> ReadFile(string path)
        {
            var result = new List<CsvMeshEntry>();
            if (!File.Exists(path)) return result;

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            int i = 0;

            // ヘッダ行スキップ
            if (i < lines.Length && lines[i].StartsWith("#PolyLing_"))
                i++;

            while (i < lines.Length)
            {
                // "---" を探す
                if (lines[i].Trim() == Separator)
                {
                    i++;
                    var entry = ReadOneMesh(lines, ref i);
                    if (entry != null)
                        result.Add(entry);
                }
                else
                {
                    i++;
                }
            }

            return result;
        }

        // ================================================================
        // Write: ヘッダ
        // ================================================================

        private static void WriteMeshHeader(StringBuilder sb, int globalIndex, MeshContext mc)
        {
            sb.AppendLine($"name,{EscapeCsv(mc.Name)}");
            sb.AppendLine($"index,{globalIndex}");
            sb.AppendLine($"type,{mc.Type}");
            sb.AppendLine($"parentIndex,{mc.ParentIndex}");
            sb.AppendLine($"depth,{mc.Depth}");
            sb.AppendLine($"hierarchyParentIndex,{mc.HierarchyParentIndex}");
            sb.AppendLine($"isVisible,{mc.IsVisible}");
            sb.AppendLine($"isLocked,{mc.IsLocked}");
            sb.AppendLine($"isFolding,{mc.IsFolding}");
            sb.AppendLine($"isExpanded,{mc.MeshObject?.IsExpanded ?? false}");
            sb.AppendLine($"mirrorType,{mc.MirrorType}");
            sb.AppendLine($"mirrorAxis,{mc.MirrorAxis}");
            sb.AppendLine($"mirrorDistance,{F(mc.MirrorDistance)}");
            sb.AppendLine($"mirrorMaterialOffset,{mc.MirrorMaterialOffset}");
            sb.AppendLine($"bakedMirrorSourceIndex,{mc.BakedMirrorSourceIndex}");
            sb.AppendLine($"hasBakedMirrorChild,{mc.HasBakedMirrorChild}");
            sb.AppendLine($"excludeFromExport,{mc.ExcludeFromExport}");

            // BoneTransform
            var bt = mc.BoneTransform ?? new BoneTransform();
            sb.AppendLine($"boneTransform,{bt.UseLocalTransform},{F(bt.Position.x)},{F(bt.Position.y)},{F(bt.Position.z)},{F(bt.Rotation.x)},{F(bt.Rotation.y)},{F(bt.Rotation.z)},{F(bt.Scale.x)},{F(bt.Scale.y)},{F(bt.Scale.z)}");
        }

        // ================================================================
        // Write: ボーン固有
        // ================================================================

        private static void WriteBoneData(StringBuilder sb, MeshContext mc)
        {
            // BonePoseData
            if (mc.BonePoseData != null)
            {
                var bp = mc.BonePoseData;
                var rp = bp.RestPosition;
                var rr = bp.RestRotation;
                var rs = bp.RestScale;

                sb.Append($"bonePose,{F(rp.x)},{F(rp.y)},{F(rp.z)},{F(rr.x)},{F(rr.y)},{F(rr.z)},{F(rr.w)},{F(rs.x)},{F(rs.y)},{F(rs.z)},{bp.IsActive}");

                // Manual layer
                var manual = bp.GetLayer("Manual");
                if (manual != null && !manual.IsZero)
                {
                    var dp = manual.DeltaPosition;
                    var dr = manual.DeltaRotation;
                    sb.Append($",{F(dp.x)},{F(dp.y)},{F(dp.z)},{F(dr.x)},{F(dr.y)},{F(dr.z)},{F(dr.w)},{F(manual.Weight)},{manual.Enabled}");
                }
                sb.AppendLine();
            }

            // BoneModelRotation
            var bmr = mc.BoneModelRotation;
            if (bmr != Quaternion.identity)
            {
                sb.AppendLine($"boneModelRotation,{F(bmr.x)},{F(bmr.y)},{F(bmr.z)},{F(bmr.w)}");
            }

            // IK
            if (mc.IsIK)
            {
                sb.AppendLine($"ik,{mc.IKTargetIndex},{mc.IKLoopCount},{F(mc.IKLimitAngle)}");
                if (mc.IKLinks != null)
                {
                    foreach (var link in mc.IKLinks)
                    {
                        sb.AppendLine($"ikLink,{link.BoneIndex},{link.HasLimit},{F(link.LimitMin.x)},{F(link.LimitMin.y)},{F(link.LimitMin.z)},{F(link.LimitMax.x)},{F(link.LimitMax.y)},{F(link.LimitMax.z)}");
                    }
                }
            }

            // BindPose (4x4 matrix, 16 values, row-major)
            var bp2 = mc.BindPose;
            if (bp2 != Matrix4x4.identity)
            {
                sb.AppendLine($"bindPose,{F(bp2.m00)},{F(bp2.m01)},{F(bp2.m02)},{F(bp2.m03)},{F(bp2.m10)},{F(bp2.m11)},{F(bp2.m12)},{F(bp2.m13)},{F(bp2.m20)},{F(bp2.m21)},{F(bp2.m22)},{F(bp2.m23)},{F(bp2.m30)},{F(bp2.m31)},{F(bp2.m32)},{F(bp2.m33)}");
            }
        }

        // ================================================================
        // Write: モーフ固有
        // ================================================================

        private static void WriteMorphData(StringBuilder sb, MeshContext mc)
        {
            sb.AppendLine($"morphParentIndex,{mc.MorphParentIndex}");

            if (mc.MorphBaseData != null && mc.MorphBaseData.IsValid)
            {
                var mbd = mc.MorphBaseData;
                sb.AppendLine($"morphName,{EscapeCsv(mbd.MorphName)}");
                sb.AppendLine($"morphPanel,{mbd.Panel}");

                // 基準位置
                if (mbd.BasePositions != null)
                {
                    for (int i = 0; i < mbd.BasePositions.Length; i++)
                    {
                        var p = mbd.BasePositions[i];
                        sb.AppendLine($"mb,{i},{F(p.x)},{F(p.y)},{F(p.z)}");
                    }
                }

                // 基準UV
                if (mbd.HasUVs && mbd.BaseUVs != null)
                {
                    for (int i = 0; i < mbd.BaseUVs.Length; i++)
                    {
                        var uv = mbd.BaseUVs[i];
                        sb.AppendLine($"mbuv,{i},{F(uv.x)},{F(uv.y)}");
                    }
                }
            }
        }

        // ================================================================
        // Write: 選択セット
        // ================================================================

        private static void WriteSelectionSets(StringBuilder sb, MeshContext mc)
        {
            if (mc.PartsSelectionSetList == null || mc.PartsSelectionSetList.Count == 0) return;

            foreach (var ss in mc.PartsSelectionSetList)
            {
                // ss,name,mode,vertexCount,v0,v1,...,edgeCount,e0v1,e0v2,...,faceCount,f0,...,lineCount,l0,...
                sb.Append($"ss,{EscapeCsv(ss.Name)},{ss.Mode}");

                // Vertices
                sb.Append($",{ss.Vertices.Count}");
                foreach (var v in ss.Vertices) sb.Append($",{v}");

                // Edges
                sb.Append($",{ss.Edges.Count}");
                foreach (var e in ss.Edges) sb.Append($",{e.V1},{e.V2}");

                // Faces
                sb.Append($",{ss.Faces.Count}");
                foreach (var f in ss.Faces) sb.Append($",{f}");

                // Lines
                sb.Append($",{ss.Lines.Count}");
                foreach (var l in ss.Lines) sb.Append($",{l}");

                sb.AppendLine();
            }
        }

        // ================================================================
        // Write: 頂点
        // ================================================================

        private static void WriteVertex(StringBuilder sb, Vertex v)
        {
            // v,id,px,py,pz,flags,bwCount(0or8),bw...,uvCount,uv...,nrmCount,nrm...
            sb.Append($"v,{v.Id},{F(v.Position.x)},{F(v.Position.y)},{F(v.Position.z)},{(byte)v.Flags}");

            // BoneWeight (0 or 8 values)
            if (v.BoneWeight.HasValue)
            {
                var bw = v.BoneWeight.Value;
                sb.Append($",8,{bw.boneIndex0},{bw.boneIndex1},{bw.boneIndex2},{bw.boneIndex3},{F(bw.weight0)},{F(bw.weight1)},{F(bw.weight2)},{F(bw.weight3)}");
            }
            else
            {
                sb.Append(",0");
            }

            // MirrorBoneWeight
            if (v.MirrorBoneWeight.HasValue)
            {
                var mbw = v.MirrorBoneWeight.Value;
                sb.Append($",8,{mbw.boneIndex0},{mbw.boneIndex1},{mbw.boneIndex2},{mbw.boneIndex3},{F(mbw.weight0)},{F(mbw.weight1)},{F(mbw.weight2)},{F(mbw.weight3)}");
            }
            else
            {
                sb.Append(",0");
            }

            // UVs
            sb.Append($",{v.UVs.Count}");
            foreach (var uv in v.UVs)
                sb.Append($",{F(uv.x)},{F(uv.y)}");

            // Normals
            sb.Append($",{v.Normals.Count}");
            foreach (var n in v.Normals)
                sb.Append($",{F(n.x)},{F(n.y)},{F(n.z)}");

            sb.AppendLine();
        }

        // ================================================================
        // Write: 面
        // ================================================================

        private static void WriteFace(StringBuilder sb, Face face)
        {
            // f,id,materialIndex,flags,vertCount,vi...,uviCount,uvi...,niCount,ni...
            sb.Append($"f,{face.Id},{face.MaterialIndex},{(byte)face.Flags}");

            // Vertex indices
            sb.Append($",{face.VertexIndices.Count}");
            foreach (var vi in face.VertexIndices) sb.Append($",{vi}");

            // UV indices
            sb.Append($",{face.UVIndices.Count}");
            foreach (var ui in face.UVIndices) sb.Append($",{ui}");

            // Normal indices
            sb.Append($",{face.NormalIndices.Count}");
            foreach (var ni in face.NormalIndices) sb.Append($",{ni}");

            sb.AppendLine();
        }

        // ================================================================
        // Read: 1メッシュ分
        // ================================================================

        private static CsvMeshEntry ReadOneMesh(string[] lines, ref int i)
        {
            var entry = new CsvMeshEntry();
            var mc = new MeshContext();
            var meshObject = new MeshObject("Untitled");
            mc.MeshObject = meshObject;

            // モーフ基準データ用一時バッファ
            var morphBasePositions = new List<Vector3>();
            var morphBaseUVs = new List<Vector2>();
            string morphName = "";
            int morphPanel = 3;
            bool hasMorphBase = false;

            while (i < lines.Length)
            {
                string line = lines[i].Trim();
                if (line == Separator) break; // 次のメッシュ境界
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                {
                    i++;
                    continue;
                }

                var cols = SplitCsvLine(line);
                if (cols.Length == 0) { i++; continue; }

                string key = cols[0];

                switch (key)
                {
                    case "name":
                        mc.Name = cols.Length > 1 ? UnescapeCsv(cols[1]) : "Untitled";
                        meshObject.Name = mc.Name;
                        break;
                    case "index":
                        entry.GlobalIndex = ParseInt(cols, 1);
                        break;
                    case "type":
                        if (cols.Length > 1 && Enum.TryParse<MeshType>(cols[1], out var mt))
                            mc.Type = mt;
                        break;
                    case "parentIndex":
                        mc.ParentIndex = ParseInt(cols, 1, -1);
                        break;
                    case "depth":
                        mc.Depth = ParseInt(cols, 1);
                        break;
                    case "hierarchyParentIndex":
                        mc.HierarchyParentIndex = ParseInt(cols, 1, -1);
                        break;
                    case "isVisible":
                        mc.IsVisible = ParseBool(cols, 1, true);
                        break;
                    case "isLocked":
                        mc.IsLocked = ParseBool(cols, 1);
                        break;
                    case "isFolding":
                        mc.IsFolding = ParseBool(cols, 1);
                        break;
                    case "isExpanded":
                        meshObject.IsExpanded = ParseBool(cols, 1);
                        break;
                    case "mirrorType":
                        mc.MirrorType = ParseInt(cols, 1);
                        break;
                    case "mirrorAxis":
                        mc.MirrorAxis = ParseInt(cols, 1, 1);
                        break;
                    case "mirrorDistance":
                        mc.MirrorDistance = ParseFloat(cols, 1);
                        break;
                    case "mirrorMaterialOffset":
                        mc.MirrorMaterialOffset = ParseInt(cols, 1);
                        break;
                    case "bakedMirrorSourceIndex":
                        mc.BakedMirrorSourceIndex = ParseInt(cols, 1, -1);
                        break;
                    case "hasBakedMirrorChild":
                        mc.HasBakedMirrorChild = ParseBool(cols, 1);
                        break;
                    case "excludeFromExport":
                        mc.ExcludeFromExport = ParseBool(cols, 1);
                        break;
                    case "boneTransform":
                        mc.BoneTransform = ReadBoneTransform(cols);
                        break;
                    case "bonePose":
                        mc.BonePoseData = ReadBonePoseData(cols);
                        break;
                    case "boneModelRotation":
                        mc.BoneModelRotation = new Quaternion(
                            ParseFloat(cols, 1), ParseFloat(cols, 2),
                            ParseFloat(cols, 3), ParseFloat(cols, 4, 1f));
                        break;
                    case "ik":
                        mc.IsIK = true;
                        mc.IKTargetIndex = ParseInt(cols, 1, -1);
                        mc.IKLoopCount = ParseInt(cols, 2);
                        mc.IKLimitAngle = ParseFloat(cols, 3);
                        if (mc.IKLinks == null) mc.IKLinks = new List<IKLinkInfo>();
                        break;
                    case "ikLink":
                        if (mc.IKLinks == null) mc.IKLinks = new List<IKLinkInfo>();
                        mc.IKLinks.Add(ReadIKLink(cols));
                        break;
                    case "bindPose":
                        mc.BindPose = ReadMatrix4x4(cols);
                        break;
                    case "morphParentIndex":
                        mc.MorphParentIndex = ParseInt(cols, 1, -1);
                        break;
                    case "morphName":
                        morphName = cols.Length > 1 ? UnescapeCsv(cols[1]) : "";
                        hasMorphBase = true;
                        break;
                    case "morphPanel":
                        morphPanel = ParseInt(cols, 1, 3);
                        break;
                    case "mb":
                        morphBasePositions.Add(new Vector3(
                            ParseFloat(cols, 2), ParseFloat(cols, 3), ParseFloat(cols, 4)));
                        hasMorphBase = true;
                        break;
                    case "mbuv":
                        morphBaseUVs.Add(new Vector2(ParseFloat(cols, 2), ParseFloat(cols, 3)));
                        break;
                    case "ss":
                        ReadSelectionSet(cols, mc);
                        break;
                    case "v":
                        meshObject.Vertices.Add(ReadVertex(cols));
                        break;
                    case "f":
                        meshObject.Faces.Add(ReadFace(cols));
                        break;
                }

                i++;
            }

            // MorphBaseData組み立て
            if (hasMorphBase && morphBasePositions.Count > 0)
            {
                mc.MorphBaseData = new MorphBaseData
                {
                    MorphName = morphName,
                    Panel = morphPanel,
                    BasePositions = morphBasePositions.ToArray(),
                    BaseUVs = morphBaseUVs.Count > 0 ? morphBaseUVs.ToArray() : null
                };
            }

            // UnityMesh生成
            mc.UnityMesh = meshObject.ToUnityMeshShared();
            mc.OriginalPositions = (Vector3[])meshObject.Positions.Clone();

            entry.MeshContext = mc;
            return entry;
        }

        // ================================================================
        // Read: BoneTransform
        // ================================================================

        private static BoneTransform ReadBoneTransform(string[] cols)
        {
            // boneTransform,useLocal,px,py,pz,rx,ry,rz,sx,sy,sz
            return new BoneTransform
            {
                UseLocalTransform = ParseBool(cols, 1),
                Position = new Vector3(ParseFloat(cols, 2), ParseFloat(cols, 3), ParseFloat(cols, 4)),
                Rotation = new Vector3(ParseFloat(cols, 5), ParseFloat(cols, 6), ParseFloat(cols, 7)),
                Scale = new Vector3(ParseFloat(cols, 8, 1f), ParseFloat(cols, 9, 1f), ParseFloat(cols, 10, 1f))
            };
        }

        // ================================================================
        // Read: BindPose (Matrix4x4)
        // ================================================================

        private static Matrix4x4 ReadMatrix4x4(string[] cols)
        {
            // bindPose,m00,m01,m02,m03,m10,m11,m12,m13,m20,m21,m22,m23,m30,m31,m32,m33
            var m = new Matrix4x4();
            m.m00 = ParseFloat(cols, 1); m.m01 = ParseFloat(cols, 2); m.m02 = ParseFloat(cols, 3); m.m03 = ParseFloat(cols, 4);
            m.m10 = ParseFloat(cols, 5); m.m11 = ParseFloat(cols, 6); m.m12 = ParseFloat(cols, 7); m.m13 = ParseFloat(cols, 8);
            m.m20 = ParseFloat(cols, 9); m.m21 = ParseFloat(cols, 10); m.m22 = ParseFloat(cols, 11); m.m23 = ParseFloat(cols, 12);
            m.m30 = ParseFloat(cols, 13); m.m31 = ParseFloat(cols, 14); m.m32 = ParseFloat(cols, 15); m.m33 = ParseFloat(cols, 16);
            return m;
        }

        // ================================================================
        // Read: BonePoseData
        // ================================================================

        private static BonePoseData ReadBonePoseData(string[] cols)
        {
            // bonePose,rpx,rpy,rpz,rrx,rry,rrz,rrw,rsx,rsy,rsz,isActive[,mdpx,mdpy,mdpz,mdrx,mdry,mdrz,mdrw,mw,me]
            var data = new BonePoseData
            {
                RestPosition = new Vector3(ParseFloat(cols, 1), ParseFloat(cols, 2), ParseFloat(cols, 3)),
                RestRotation = new Quaternion(ParseFloat(cols, 4), ParseFloat(cols, 5), ParseFloat(cols, 6), ParseFloat(cols, 7, 1f)),
                RestScale = new Vector3(ParseFloat(cols, 8, 1f), ParseFloat(cols, 9, 1f), ParseFloat(cols, 10, 1f)),
                IsActive = ParseBool(cols, 11, true)
            };

            // Manual layer (optional)
            if (cols.Length > 12)
            {
                var layer = data.GetOrCreateLayer("Manual");
                layer.DeltaPosition = new Vector3(ParseFloat(cols, 12), ParseFloat(cols, 13), ParseFloat(cols, 14));
                layer.DeltaRotation = new Quaternion(ParseFloat(cols, 15), ParseFloat(cols, 16), ParseFloat(cols, 17), ParseFloat(cols, 18, 1f));
                layer.Weight = ParseFloat(cols, 19, 1f);
                layer.Enabled = ParseBool(cols, 20, true);
            }

            return data;
        }

        // ================================================================
        // Read: IKLink
        // ================================================================

        private static IKLinkInfo ReadIKLink(string[] cols)
        {
            // ikLink,boneIndex,hasLimit,minX,minY,minZ,maxX,maxY,maxZ
            return new IKLinkInfo
            {
                BoneIndex = ParseInt(cols, 1),
                HasLimit = ParseBool(cols, 2),
                LimitMin = new Vector3(ParseFloat(cols, 3), ParseFloat(cols, 4), ParseFloat(cols, 5)),
                LimitMax = new Vector3(ParseFloat(cols, 6), ParseFloat(cols, 7), ParseFloat(cols, 8))
            };
        }

        // ================================================================
        // Read: 頂点
        // ================================================================

        private static Vertex ReadVertex(string[] cols)
        {
            // v,id,px,py,pz,flags,bwCount,[bw...],mbwCount,[mbw...],uvCount,[uv...],nrmCount,[nrm...]
            int idx = 1;
            int id = ParseInt(cols, idx++);
            float px = ParseFloat(cols, idx++);
            float py = ParseFloat(cols, idx++);
            float pz = ParseFloat(cols, idx++);
            byte flags = (byte)ParseInt(cols, idx++);

            var vertex = new Vertex(id, new Vector3(px, py, pz));
            vertex.Flags = (VertexFlags)flags;

            // BoneWeight
            int bwCount = ParseInt(cols, idx++);
            if (bwCount == 8)
            {
                vertex.BoneWeight = new BoneWeight
                {
                    boneIndex0 = ParseInt(cols, idx++),
                    boneIndex1 = ParseInt(cols, idx++),
                    boneIndex2 = ParseInt(cols, idx++),
                    boneIndex3 = ParseInt(cols, idx++),
                    weight0 = ParseFloat(cols, idx++),
                    weight1 = ParseFloat(cols, idx++),
                    weight2 = ParseFloat(cols, idx++),
                    weight3 = ParseFloat(cols, idx++)
                };
            }

            // MirrorBoneWeight
            int mbwCount = ParseInt(cols, idx++);
            if (mbwCount == 8)
            {
                vertex.MirrorBoneWeight = new BoneWeight
                {
                    boneIndex0 = ParseInt(cols, idx++),
                    boneIndex1 = ParseInt(cols, idx++),
                    boneIndex2 = ParseInt(cols, idx++),
                    boneIndex3 = ParseInt(cols, idx++),
                    weight0 = ParseFloat(cols, idx++),
                    weight1 = ParseFloat(cols, idx++),
                    weight2 = ParseFloat(cols, idx++),
                    weight3 = ParseFloat(cols, idx++)
                };
            }

            // UVs
            int uvCount = ParseInt(cols, idx++);
            for (int u = 0; u < uvCount; u++)
            {
                float ux = ParseFloat(cols, idx++);
                float uy = ParseFloat(cols, idx++);
                vertex.UVs.Add(new Vector2(ux, uy));
            }

            // Normals
            int nrmCount = ParseInt(cols, idx++);
            for (int n = 0; n < nrmCount; n++)
            {
                float nx = ParseFloat(cols, idx++);
                float ny = ParseFloat(cols, idx++);
                float nz = ParseFloat(cols, idx++);
                vertex.Normals.Add(new Vector3(nx, ny, nz));
            }

            return vertex;
        }

        // ================================================================
        // Read: 面
        // ================================================================

        private static Face ReadFace(string[] cols)
        {
            // f,id,materialIndex,flags,vertCount,vi...,uviCount,uvi...,niCount,ni...
            int idx = 1;
            int id = ParseInt(cols, idx++);
            int matIdx = ParseInt(cols, idx++);
            byte flags = (byte)ParseInt(cols, idx++);

            var face = new Face
            {
                Id = id,
                MaterialIndex = matIdx,
                Flags = (FaceFlags)flags
            };

            // Vertex indices
            int vCount = ParseInt(cols, idx++);
            face.VertexIndices = new List<int>(vCount);
            for (int v = 0; v < vCount; v++)
                face.VertexIndices.Add(ParseInt(cols, idx++));

            // UV indices
            int uvCount = ParseInt(cols, idx++);
            face.UVIndices = new List<int>(uvCount);
            for (int u = 0; u < uvCount; u++)
                face.UVIndices.Add(ParseInt(cols, idx++));

            // Normal indices
            int niCount = ParseInt(cols, idx++);
            face.NormalIndices = new List<int>(niCount);
            for (int n = 0; n < niCount; n++)
                face.NormalIndices.Add(ParseInt(cols, idx++));

            return face;
        }

        // ================================================================
        // Read: 選択セット
        // ================================================================

        private static void ReadSelectionSet(string[] cols, MeshContext mc)
        {
            // ss,name,mode,vCount,v...,eCount,e1,e2,...,fCount,f...,lCount,l...
            if (mc.PartsSelectionSetList == null) mc.PartsSelectionSetList = new List<PartsSelectionSet>();

            int idx = 1;
            string name = UnescapeCsv(cols.Length > idx ? cols[idx] : "Set"); idx++;
            string modeStr = cols.Length > idx ? cols[idx] : "Vertex"; idx++;

            var ss = new PartsSelectionSet(name);
            if (Enum.TryParse<MeshSelectMode>(modeStr, out var mode))
                ss.Mode = mode;

            // Vertices
            int vCount = ParseInt(cols, idx++);
            for (int v = 0; v < vCount; v++)
                ss.Vertices.Add(ParseInt(cols, idx++));

            // Edges
            int eCount = ParseInt(cols, idx++);
            for (int e = 0; e < eCount; e++)
            {
                int v1 = ParseInt(cols, idx++);
                int v2 = ParseInt(cols, idx++);
                ss.Edges.Add(new VertexPair(v1, v2));
            }

            // Faces
            int fCount = ParseInt(cols, idx++);
            for (int f = 0; f < fCount; f++)
                ss.Faces.Add(ParseInt(cols, idx++));

            // Lines
            int lCount = ParseInt(cols, idx++);
            for (int l = 0; l < lCount; l++)
                ss.Lines.Add(ParseInt(cols, idx++));

            mc.PartsSelectionSetList.Add(ss);
        }

        // ================================================================
        // CSV ユーティリティ
        // ================================================================

        /// <summary>float → 文字列 (InvariantCulture)</summary>
        private static string F(float v)
        {
            return v.ToString("G9", CultureInfo.InvariantCulture);
        }

        /// <summary>CSV値のエスケープ（カンマ・改行を含む場合ダブルクォート囲み）</summary>
        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        /// <summary>CSVエスケープ解除</summary>
        private static string UnescapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
            {
                s = s.Substring(1, s.Length - 2);
                s = s.Replace("\"\"", "\"");
            }
            return s;
        }

        /// <summary>CSV行をカンマ分割（ダブルクォート対応）</summary>
        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '"')
                {
                    // クォート内
                    i++;
                    var sb = new StringBuilder();
                    while (i < line.Length)
                    {
                        if (line[i] == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"')
                            {
                                sb.Append('"');
                                i += 2;
                            }
                            else
                            {
                                i++;
                                break;
                            }
                        }
                        else
                        {
                            sb.Append(line[i]);
                            i++;
                        }
                    }
                    result.Add(sb.ToString());
                    if (i < line.Length && line[i] == ',') i++;
                }
                else
                {
                    int start = i;
                    while (i < line.Length && line[i] != ',') i++;
                    result.Add(line.Substring(start, i - start));
                    if (i < line.Length) i++;
                }
            }
            return result.ToArray();
        }

        private static int ParseInt(string[] cols, int idx, int def = 0)
        {
            if (idx >= cols.Length || string.IsNullOrEmpty(cols[idx])) return def;
            return int.TryParse(cols[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        private static float ParseFloat(string[] cols, int idx, float def = 0f)
        {
            if (idx >= cols.Length || string.IsNullOrEmpty(cols[idx])) return def;
            return float.TryParse(cols[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        private static bool ParseBool(string[] cols, int idx, bool def = false)
        {
            if (idx >= cols.Length || string.IsNullOrEmpty(cols[idx])) return def;
            var s = cols[idx].Trim();
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "True") return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "False") return false;
            return def;
        }
    }
}
