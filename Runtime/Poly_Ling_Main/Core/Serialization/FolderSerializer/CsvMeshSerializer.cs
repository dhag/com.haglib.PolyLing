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
using Poly_Ling.Serialization;

namespace Poly_Ling.Serialization.FolderSerializer
{
    /// <summary>
    /// メッシュ1個分の読み取り結果
    /// </summary>
    public class CsvMeshEntry
    {
        public int GlobalIndex;
        public MeshContext MeshContext;

        // ================================================================
        // 名前ベース参照（読み込み時に一時格納、後でインデックスに解決）
        // ================================================================
        public bool IsNameBased;
        public string ParentName;
        public string HierarchyParentName;
        public string BakedMirrorSourceName;
        public string MorphParentName;
        /// <summary>頂点ごとのBoneWeight参照ボーン名 [name0,name1,name2,name3]</summary>
        public List<string[]> VertexBoneNames;
        /// <summary>頂点ごとのMirrorBoneWeight参照ボーン名</summary>
        public List<string[]> VertexMirrorBoneNames;

        // ================================================================
        // ミラーペア情報（部分インポート時にペア再構築するため）
        // ================================================================
        /// <summary>ミラーペア相手のメッシュ名（Real側に記録）</summary>
        public string MirrorPeerName;
        /// <summary>ミラーペアの軸（0=X, 1=Y, 2=Z）</summary>
        public int MirrorPeerAxis = -1;
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
        /// <param name="useNameBased">名前ベース参照モード</param>
        /// <param name="indexToName">MeshContextListインデックス→名前の辞書（名前ベース時必須）</param>
        public static void WriteFile(string path, List<CsvMeshEntry> entries, string fileType,
            bool useNameBased = false, Dictionary<int, string> indexToName = null)
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

                // === R2-a: DTO単一真実源化（書き出しをMeshDTO経由に）===
                // 保存データを必ず MeshDTO に通す：mc → MeshDTO → mc' とラウンドトリップしてから
                // 既存writerで書き出す。既存writerは不変のため出力フォーマットは保たれ、
                // MeshDTO往復が無損失（構造=R1.5 / population=R1.6）なので出力はバイト一致。
                // buildUnityMesh=false で保存時の不要なUnityメッシュ生成を回避する。
                var roundTripDTO = ModelSerializer.FromMeshContext(mc);
                if (roundTripDTO != null)
                {
                    var roundTripMc = ModelSerializer.ToMeshContext(roundTripDTO, false);
                    if (roundTripMc != null) mc = roundTripMc;
                }

                sb.AppendLine(Separator);
                WriteMeshHeader(sb, entry.GlobalIndex, mc, useNameBased, indexToName,
                    entry.MirrorPeerName, entry.MirrorPeerAxis);

                // ボーン固有データ
                if (mc.Type == MeshType.Bone)
                {
                    WriteBoneData(sb, mc, useNameBased, indexToName);
                    // SpringBone 付帯データ（規約4: CSV/JSON 対称）
                    WriteSpringBoneData(sb, mc);
                }

                // モーフ固有データ
                if (mc.Type == MeshType.Morph)
                {
                    WriteMorphData(sb, mc, useNameBased, indexToName);
                }

                // 選択セット
                WriteSelectionSets(sb, mc);

                // 剛体 / JOINT（Type=RigidBody/RigidBodyJoint の頂点ゼロ・メタデータ）
                WriteRigidJointData(sb, mc);

                // 頂点
                if (mc.MeshObject != null)
                {
                    foreach (var v in mc.MeshObject.Vertices)
                    {
                        if (useNameBased)
                            WriteVertexNameBased(sb, v, indexToName);
                        else
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

        private static void WriteMeshHeader(StringBuilder sb, int globalIndex, MeshContext mc,
            bool useNameBased = false, Dictionary<int, string> indexToName = null,
            string mirrorPeerName = null, int mirrorPeerAxis = -1)
        {
            sb.AppendLine($"name,{EscapeCsv(mc.Name)}");

            if (useNameBased)
            {
                // 名前ベース: indexは省略、参照はすべて名前
                sb.AppendLine($"type,{mc.Type}");
                sb.AppendLine($"parentName,{EscapeCsv(ResolveName(mc.ParentIndex, indexToName))}");
                sb.AppendLine($"depth,{mc.Depth}");
                sb.AppendLine($"hierarchyParentName,{EscapeCsv(ResolveName(mc.HierarchyParentIndex, indexToName))}");
            }
            else
            {
                sb.AppendLine($"index,{globalIndex}");
                sb.AppendLine($"type,{mc.Type}");
                sb.AppendLine($"parentIndex,{mc.ParentIndex}");
                sb.AppendLine($"depth,{mc.Depth}");
                sb.AppendLine($"hierarchyParentIndex,{mc.HierarchyParentIndex}");
            }

            sb.AppendLine($"isVisible,{mc.IsVisible}");
            sb.AppendLine($"isLocked,{mc.IsLocked}");
            sb.AppendLine($"isFolding,{mc.IsFolding}");
            sb.AppendLine($"isTriangulated,{mc.MeshObject?.IsTriangulated ?? false}");
            sb.AppendLine($"mirrorType,{mc.MirrorType}");
            sb.AppendLine($"mirrorAxis,{mc.MirrorAxis}");
            sb.AppendLine($"mirrorDistance,{F(mc.MirrorDistance)}");
            sb.AppendLine($"mirrorMaterialOffset,{mc.MirrorMaterialOffset}");

            if (useNameBased)
            {
                sb.AppendLine($"bakedMirrorSourceName,{EscapeCsv(ResolveName(mc.BakedMirrorSourceIndex, indexToName))}");
            }
            else
            {
                sb.AppendLine($"bakedMirrorSourceIndex,{mc.BakedMirrorSourceIndex}");
            }

            sb.AppendLine($"hasBakedMirrorChild,{mc.HasBakedMirrorChild}");
            sb.AppendLine($"excludeFromExport,{mc.ExcludeFromExport}");

            // ミラーペア情報（Real側のみ出力）
            if (!string.IsNullOrEmpty(mirrorPeerName))
            {
                sb.AppendLine($"mirrorPeer,{EscapeCsv(mirrorPeerName)},{mirrorPeerAxis}");
            }

            // BoneTransform
            var bt = mc.BoneTransform ?? new BoneTransform();
            sb.AppendLine($"boneTransform,{bt.UseLocalTransform},{F(bt.Position.x)},{F(bt.Position.y)},{F(bt.Position.z)},{F(bt.Rotation.x)},{F(bt.Rotation.y)},{F(bt.Rotation.z)},{F(bt.Scale.x)},{F(bt.Scale.y)},{F(bt.Scale.z)}");
        }

        /// <summary>
        /// インデックスから名前を解決。-1や見つからない場合は空文字
        /// </summary>
        private static string ResolveName(int index, Dictionary<int, string> indexToName)
        {
            if (index < 0 || indexToName == null) return "";
            return indexToName.TryGetValue(index, out var name) ? name : "";
        }

        // ================================================================
        // Write: ボーン固有
        // ================================================================

        private static void WriteBoneData(StringBuilder sb, MeshContext mc,
            bool useNameBased = false, Dictionary<int, string> indexToName = null)
        {
            // BonePoseData
            if (mc.BonePoseData != null)
            {
                var bp = mc.BonePoseData;

                sb.Append($"bonePose,{bp.IsActive}");

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

            // IK（per-bone 形式・規約2 name主）
            //   ルート : ikRoot,effectorName,loopCount,limitAngle（name主・mode非依存）
            //   リンク : ikLinkBone,hasLimit,limitMin xyz,limitMax xyz（per-bone マーカー）
            //   ※源泉の集約Links→per-bone同期は CsvModelSerializer.Serialize 冒頭で実施済み。
            var moIk = mc.MeshObject?.IKData;
            if (moIk != null && moIk.IsIK)
            {
                sb.AppendLine($"ikRoot,{EscapeCsv(moIk.EffectorBoneName ?? "")},{moIk.LoopCount},{F(moIk.LimitAngle)}");
            }
            var moLink = mc.MeshObject?.IKLink;
            if (moLink != null)
            {
                sb.AppendLine($"ikLinkBone,{moLink.HasLimit},{F(moLink.LimitMin.x)},{F(moLink.LimitMin.y)},{F(moLink.LimitMin.z)},{F(moLink.LimitMax.x)},{F(moLink.LimitMax.y)},{F(moLink.LimitMax.z)}");
            }

            // Humanoid 割当（per-bone・name主・#5b）
            var human = mc.MeshObject?.HumanBodyBone;
            if (!string.IsNullOrEmpty(human))
            {
                sb.AppendLine($"humanBodyBone,{EscapeCsv(human)}");
            }

            // Humanoid マッスル可動域（per-bone・#5d-1）
            //   humanLimit,useDefault,minXYZ,maxXYZ,centerXYZ,axisLength（ラジアン）
            var hl = mc.MeshObject?.HumanLimit;
            if (hl != null)
            {
                sb.AppendLine(
                    $"humanLimit,{hl.UseDefaultValues}," +
                    $"{F(hl.Min.x)},{F(hl.Min.y)},{F(hl.Min.z)}," +
                    $"{F(hl.Max.x)},{F(hl.Max.y)},{F(hl.Max.z)}," +
                    $"{F(hl.Center.x)},{F(hl.Center.y)},{F(hl.Center.z)},{F(hl.AxisLength)}");
            }

            // BindPose (4x4 matrix, 16 values, row-major)
            var bp2 = mc.BindPose;
            if (bp2 != Matrix4x4.identity)
            {
                sb.AppendLine($"bindPose,{F(bp2.m00)},{F(bp2.m01)},{F(bp2.m02)},{F(bp2.m03)},{F(bp2.m10)},{F(bp2.m11)},{F(bp2.m12)},{F(bp2.m13)},{F(bp2.m20)},{F(bp2.m21)},{F(bp2.m22)},{F(bp2.m23)},{F(bp2.m30)},{F(bp2.m31)},{F(bp2.m32)},{F(bp2.m33)}");
            }
        }

        // ================================================================
        // Write: SpringBone 付帯データ（Type == Bone のボーンに付く）
        //   規約: MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
        //   グループ参照は JSON と同じ index（ModelContext.SpringBoneColliderGroupNames
        //   への index）を ';' 連結で格納する（規約4: CSV/JSON 対称）。
        //   centerBoneName は bone名のまま格納（POCO実体が string）。
        // ================================================================

        private static void WriteSpringBoneData(StringBuilder sb, MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return;

            // コライダー（1ボーンに複数可）
            if (mo.SpringBoneColliders != null)
            {
                foreach (var c in mo.SpringBoneColliders)
                {
                    if (c == null) continue;
                    // sbCollider,shape,offX,offY,offZ,radius,tailX,tailY,tailZ,nX,nY,nZ,grp
                    sb.AppendLine(
                        $"sbCollider,{(int)c.Shape}," +
                        $"{F(c.Offset.x)},{F(c.Offset.y)},{F(c.Offset.z)},{F(c.Radius)}," +
                        $"{F(c.Tail.x)},{F(c.Tail.y)},{F(c.Tail.z)}," +
                        $"{F(c.Normal.x)},{F(c.Normal.y)},{F(c.Normal.z)}," +
                        $"{JoinIndices(c.SpringBoneGroupIndices)}");
                }
            }

            // ジョイント（非null=揺れチェーンのメンバー）
            var j = mo.SpringBoneJoint;
            if (j != null)
            {
                // sbJoint,hitRadius,stiffness,gravityPower,gdX,gdY,gdZ,dragForce
                sb.AppendLine(
                    $"sbJoint,{F(j.HitRadius)},{F(j.StiffnessForce)},{F(j.GravityPower)}," +
                    $"{F(j.GravityDir.x)},{F(j.GravityDir.y)},{F(j.GravityDir.z)},{F(j.DragForce)}");
            }

            // チェーンルート（非null=このボーンがチェーン起点）
            var ch = mo.SpringBoneChainRoot;
            if (ch != null)
            {
                // sbChain,name,centerBoneName,grp
                sb.AppendLine(
                    $"sbChain,{EscapeCsv(ch.Name ?? "")},{EscapeCsv(ch.CenterBoneName ?? "")}," +
                    $"{JoinIndices(ch.SpringBoneColliderGroupIndices)}");
            }
        }

        /// <summary>int リストを ';' 連結（CSVカンマと非衝突）。null/空は空文字。</summary>
        private static string JoinIndices(List<int> indices)
        {
            if (indices == null || indices.Count == 0) return "";
            return string.Join(";", indices);
        }

        /// <summary>';' 連結の index 文字列をパース。空/欠損は空リスト。</summary>
        private static List<int> ParseIndices(string[] cols, int idx)
        {
            var list = new List<int>();
            if (idx >= cols.Length) return list;
            var s = cols[idx];
            if (string.IsNullOrEmpty(s)) return list;
            foreach (var tok in s.Split(';'))
            {
                if (int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    list.Add(v);
            }
            return list;
        }

        // ================================================================
        // Write: モーフ固有
        // ================================================================

        private static void WriteMorphData(StringBuilder sb, MeshContext mc,
            bool useNameBased = false, Dictionary<int, string> indexToName = null)
        {
            if (useNameBased)
            {
                sb.AppendLine($"morphParentName,{EscapeCsv(ResolveName(mc.MorphParentIndex, indexToName))}");
            }
            else
            {
                sb.AppendLine($"morphParentIndex,{mc.MorphParentIndex}");
            }

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
        // Write: 頂点（名前ベース）
        // ================================================================

        private static void WriteVertexNameBased(StringBuilder sb, Vertex v, Dictionary<int, string> indexToName)
        {
            // vn,id,px,py,pz,flags,bwCount(0or8),boneName0..3,w0..3,mbwCount,...,uvCount,uv...,nrmCount,nrm...
            sb.Append($"vn,{v.Id},{F(v.Position.x)},{F(v.Position.y)},{F(v.Position.z)},{(byte)v.Flags}");

            // BoneWeight (名前ベース)
            if (v.BoneWeight.HasValue)
            {
                var bw = v.BoneWeight.Value;
                string n0 = ResolveName(bw.boneIndex0, indexToName);
                string n1 = ResolveName(bw.boneIndex1, indexToName);
                string n2 = ResolveName(bw.boneIndex2, indexToName);
                string n3 = ResolveName(bw.boneIndex3, indexToName);
                sb.Append($",8,{EscapeCsv(n0)},{EscapeCsv(n1)},{EscapeCsv(n2)},{EscapeCsv(n3)},{F(bw.weight0)},{F(bw.weight1)},{F(bw.weight2)},{F(bw.weight3)}");
            }
            else
            {
                sb.Append(",0");
            }

            // MirrorBoneWeight (名前ベース)
            if (v.MirrorBoneWeight.HasValue)
            {
                var mbw = v.MirrorBoneWeight.Value;
                string n0 = ResolveName(mbw.boneIndex0, indexToName);
                string n1 = ResolveName(mbw.boneIndex1, indexToName);
                string n2 = ResolveName(mbw.boneIndex2, indexToName);
                string n3 = ResolveName(mbw.boneIndex3, indexToName);
                sb.Append($",8,{EscapeCsv(n0)},{EscapeCsv(n1)},{EscapeCsv(n2)},{EscapeCsv(n3)},{F(mbw.weight0)},{F(mbw.weight1)},{F(mbw.weight2)},{F(mbw.weight3)}");
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
                    case "isTriangulated":
                        meshObject.IsTriangulated = ParseBool(cols, 1);
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
                    case "mirrorPeer":
                        // mirrorPeer,peerName,axis
                        if (cols.Length >= 2)
                            entry.MirrorPeerName = UnescapeCsv(cols[1]);
                        if (cols.Length >= 3)
                            entry.MirrorPeerAxis = ParseInt(cols, 2, 0);
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
                    case "ikRoot":
                        // ikRoot,effectorName,loopCount,limitAngle（name主）
                        if (meshObject.IKData == null) meshObject.IKData = new IKData();
                        meshObject.IKData.IsIK = true;
                        meshObject.IKData.EffectorBoneName = cols.Length > 1 ? UnescapeCsv(cols[1]) : "";
                        meshObject.IKData.LoopCount = ParseInt(cols, 2);
                        meshObject.IKData.LimitAngle = ParseFloat(cols, 3);
                        // TargetIndex / Links は読込後 IKChainResolver.RebuildLinksFromPerBone で再構築
                        break;
                    case "ikLinkBone":
                        // ikLinkBone,hasLimit,limitMin xyz,limitMax xyz（per-bone マーカー）
                        meshObject.IKLink = new IKLinkData
                        {
                            HasLimit = ParseBool(cols, 1),
                            LimitMin = new Vector3(ParseFloat(cols, 2), ParseFloat(cols, 3), ParseFloat(cols, 4)),
                            LimitMax = new Vector3(ParseFloat(cols, 5), ParseFloat(cols, 6), ParseFloat(cols, 7))
                        };
                        break;
                    case "humanBodyBone":
                        // humanBodyBone,<Unity Humanoid名>（per-bone・#5b）
                        meshObject.HumanBodyBone = cols.Length > 1 ? UnescapeCsv(cols[1]) : "";
                        break;
                    case "humanLimit":
                        // humanLimit,useDefault,minXYZ,maxXYZ,centerXYZ,axisLength（#5d-1）
                        meshObject.HumanLimit = new HumanLimitData
                        {
                            UseDefaultValues = ParseBool(cols, 1, true),
                            Min = new Vector3(ParseFloat(cols, 2), ParseFloat(cols, 3), ParseFloat(cols, 4)),
                            Max = new Vector3(ParseFloat(cols, 5), ParseFloat(cols, 6), ParseFloat(cols, 7)),
                            Center = new Vector3(ParseFloat(cols, 8), ParseFloat(cols, 9), ParseFloat(cols, 10)),
                            AxisLength = ParseFloat(cols, 11)
                        };
                        break;
                    case "bindPose":
                        mc.BindPose = ReadMatrix4x4(cols);
                        break;
                    case "rigidBody":
                        meshObject.RigidBodyData = ReadRigidBodyData(cols);
                        break;
                    case "joint":
                        meshObject.JointData = ReadJointData(cols);
                        break;
                    case "sbCollider":
                        if (meshObject.SpringBoneColliders == null)
                            meshObject.SpringBoneColliders = new List<SpringBoneColliderData>();
                        meshObject.SpringBoneColliders.Add(ReadSpringBoneCollider(cols));
                        break;
                    case "sbJoint":
                        meshObject.SpringBoneJoint = ReadSpringBoneJoint(cols);
                        break;
                    case "sbChain":
                        meshObject.SpringBoneChainRoot = ReadSpringBoneChain(cols);
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

                    // ================================================================
                    // 名前ベース参照（自動判別）
                    // ================================================================
                    case "parentName":
                        entry.IsNameBased = true;
                        entry.ParentName = cols.Length > 1 ? UnescapeCsv(cols[1]) : "";
                        break;
                    case "hierarchyParentName":
                        entry.HierarchyParentName = cols.Length > 1 ? UnescapeCsv(cols[1]) : "";
                        break;
                    case "bakedMirrorSourceName":
                        entry.BakedMirrorSourceName = cols.Length > 1 ? UnescapeCsv(cols[1]) : "";
                        break;
                    case "morphParentName":
                        entry.MorphParentName = cols.Length > 1 ? UnescapeCsv(cols[1]) : "";
                        entry.IsNameBased = true;
                        break;
                    case "vn":
                        var (vtx, boneNames, mirrorBoneNames) = ReadVertexNameBased(cols);
                        meshObject.Vertices.Add(vtx);
                        if (boneNames != null)
                        {
                            if (entry.VertexBoneNames == null) entry.VertexBoneNames = new List<string[]>();
                            entry.VertexBoneNames.Add(boneNames);
                        }
                        else
                        {
                            if (entry.VertexBoneNames != null) entry.VertexBoneNames.Add(null);
                        }
                        if (mirrorBoneNames != null)
                        {
                            if (entry.VertexMirrorBoneNames == null) entry.VertexMirrorBoneNames = new List<string[]>();
                            entry.VertexMirrorBoneNames.Add(mirrorBoneNames);
                        }
                        else
                        {
                            if (entry.VertexMirrorBoneNames != null) entry.VertexMirrorBoneNames.Add(null);
                        }
                        entry.IsNameBased = true;
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
        // 剛体 / JOINT（Type=RigidBody/RigidBodyJoint のメタデータ）
        // ================================================================

        private static void WriteRigidJointData(StringBuilder sb, MeshContext mc)
        {
            var rb = mc.MeshObject?.RigidBodyData;
            if (rb != null)
            {
                // rigidBody,nameEng,relatedBone,boneIdx,group,mask,shape,sx,sy,sz,px,py,pz,rx,ry,rz,mass,linDamp,angDamp,restitution,friction,physMode
                sb.AppendLine(
                    $"rigidBody,{EscapeCsv(rb.NameEnglish)},{EscapeCsv(rb.RelatedBoneName)},{rb.BoneIndex},{rb.Group},{rb.CollisionMask},{(int)rb.Shape}," +
                    $"{F(rb.Size.x)},{F(rb.Size.y)},{F(rb.Size.z)}," +
                    $"{F(rb.Position.x)},{F(rb.Position.y)},{F(rb.Position.z)}," +
                    $"{F(rb.Rotation.x)},{F(rb.Rotation.y)},{F(rb.Rotation.z)}," +
                    $"{F(rb.Mass)},{F(rb.LinearDamping)},{F(rb.AngularDamping)},{F(rb.Restitution)},{F(rb.Friction)},{(int)rb.PhysicsMode}");
            }

            var jd = mc.MeshObject?.JointData;
            if (jd != null)
            {
                // joint,nameEng,jointType,bodyA,bodyB,idxA,idxB,px,py,pz,rx,ry,rz,tMin xyz,tMax xyz,rMin xyz,rMax xyz,springT xyz,springR xyz
                sb.AppendLine(
                    $"joint,{EscapeCsv(jd.NameEnglish)},{jd.JointType},{EscapeCsv(jd.BodyAName)},{EscapeCsv(jd.BodyBName)},{jd.RigidBodyIndexA},{jd.RigidBodyIndexB}," +
                    $"{F(jd.Position.x)},{F(jd.Position.y)},{F(jd.Position.z)}," +
                    $"{F(jd.Rotation.x)},{F(jd.Rotation.y)},{F(jd.Rotation.z)}," +
                    $"{F(jd.TranslationMin.x)},{F(jd.TranslationMin.y)},{F(jd.TranslationMin.z)}," +
                    $"{F(jd.TranslationMax.x)},{F(jd.TranslationMax.y)},{F(jd.TranslationMax.z)}," +
                    $"{F(jd.RotationMin.x)},{F(jd.RotationMin.y)},{F(jd.RotationMin.z)}," +
                    $"{F(jd.RotationMax.x)},{F(jd.RotationMax.y)},{F(jd.RotationMax.z)}," +
                    $"{F(jd.SpringTranslation.x)},{F(jd.SpringTranslation.y)},{F(jd.SpringTranslation.z)}," +
                    $"{F(jd.SpringRotation.x)},{F(jd.SpringRotation.y)},{F(jd.SpringRotation.z)}");
            }
        }

        private static RigidBodyData ReadRigidBodyData(string[] cols)
        {
            return new RigidBodyData
            {
                NameEnglish     = cols.Length > 1 ? UnescapeCsv(cols[1]) : "",
                RelatedBoneName = cols.Length > 2 ? UnescapeCsv(cols[2]) : "",
                BoneIndex       = ParseInt(cols, 3, -1),
                Group           = ParseInt(cols, 4),
                CollisionMask   = (ushort)ParseInt(cols, 5),
                Shape           = (RigidBodyShape)ParseInt(cols, 6),
                Size            = new Vector3(ParseFloat(cols, 7),  ParseFloat(cols, 8),  ParseFloat(cols, 9)),
                Position        = new Vector3(ParseFloat(cols, 10), ParseFloat(cols, 11), ParseFloat(cols, 12)),
                Rotation        = new Vector3(ParseFloat(cols, 13), ParseFloat(cols, 14), ParseFloat(cols, 15)),
                Mass            = ParseFloat(cols, 16),
                LinearDamping   = ParseFloat(cols, 17),
                AngularDamping  = ParseFloat(cols, 18),
                Restitution     = ParseFloat(cols, 19),
                Friction        = ParseFloat(cols, 20),
                PhysicsMode     = (RigidBodyPhysicsMode)ParseInt(cols, 21)
            };
        }

        private static JointData ReadJointData(string[] cols)
        {
            return new JointData
            {
                NameEnglish       = cols.Length > 1 ? UnescapeCsv(cols[1]) : "",
                JointType         = ParseInt(cols, 2),
                BodyAName         = cols.Length > 3 ? UnescapeCsv(cols[3]) : "",
                BodyBName         = cols.Length > 4 ? UnescapeCsv(cols[4]) : "",
                RigidBodyIndexA   = ParseInt(cols, 5, -1),
                RigidBodyIndexB   = ParseInt(cols, 6, -1),
                Position          = new Vector3(ParseFloat(cols, 7),  ParseFloat(cols, 8),  ParseFloat(cols, 9)),
                Rotation          = new Vector3(ParseFloat(cols, 10), ParseFloat(cols, 11), ParseFloat(cols, 12)),
                TranslationMin    = new Vector3(ParseFloat(cols, 13), ParseFloat(cols, 14), ParseFloat(cols, 15)),
                TranslationMax    = new Vector3(ParseFloat(cols, 16), ParseFloat(cols, 17), ParseFloat(cols, 18)),
                RotationMin       = new Vector3(ParseFloat(cols, 19), ParseFloat(cols, 20), ParseFloat(cols, 21)),
                RotationMax       = new Vector3(ParseFloat(cols, 22), ParseFloat(cols, 23), ParseFloat(cols, 24)),
                SpringTranslation = new Vector3(ParseFloat(cols, 25), ParseFloat(cols, 26), ParseFloat(cols, 27)),
                SpringRotation    = new Vector3(ParseFloat(cols, 28), ParseFloat(cols, 29), ParseFloat(cols, 30))
            };
        }

        // ================================================================
        // Read: SpringBone 付帯データ
        //   規約: MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
        // ================================================================

        private static SpringBoneColliderData ReadSpringBoneCollider(string[] cols)
        {
            // sbCollider,shape,offX,offY,offZ,radius,tailX,tailY,tailZ,nX,nY,nZ,grp
            return new SpringBoneColliderData
            {
                Shape                  = (SpringBoneColliderShape)ParseInt(cols, 1),
                Offset                 = new Vector3(ParseFloat(cols, 2),  ParseFloat(cols, 3),  ParseFloat(cols, 4)),
                Radius                 = ParseFloat(cols, 5),
                Tail                   = new Vector3(ParseFloat(cols, 6),  ParseFloat(cols, 7),  ParseFloat(cols, 8)),
                Normal                 = new Vector3(ParseFloat(cols, 9),  ParseFloat(cols, 10), ParseFloat(cols, 11)),
                SpringBoneGroupIndices = ParseIndices(cols, 12)
            };
        }

        private static SpringBoneJointData ReadSpringBoneJoint(string[] cols)
        {
            // sbJoint,hitRadius,stiffness,gravityPower,gdX,gdY,gdZ,dragForce
            return new SpringBoneJointData
            {
                HitRadius      = ParseFloat(cols, 1, 0.02f),
                StiffnessForce = ParseFloat(cols, 2, 1.0f),
                GravityPower   = ParseFloat(cols, 3),
                GravityDir     = new Vector3(ParseFloat(cols, 4), ParseFloat(cols, 5, -1f), ParseFloat(cols, 6)),
                DragForce      = ParseFloat(cols, 7, 0.4f)
            };
        }

        private static SpringBoneChainData ReadSpringBoneChain(string[] cols)
        {
            // sbChain,name,centerBoneName,grp
            return new SpringBoneChainData
            {
                Name                          = cols.Length > 1 ? UnescapeCsv(cols[1]) : "",
                CenterBoneName                = cols.Length > 2 ? UnescapeCsv(cols[2]) : "",
                SpringBoneColliderGroupIndices = ParseIndices(cols, 3)
            };
        }

        // ================================================================
        // Read: BonePoseData
        // ================================================================

        private static BonePoseData ReadBonePoseData(string[] cols)
        {
            // bonePose,isActive[,mdpx,mdpy,mdpz,mdrx,mdry,mdrz,mdrw,mw,me]
            var data = new BonePoseData
            {
                IsActive = ParseBool(cols, 1, true)
            };

            // Manual layer (optional)
            if (cols.Length > 2)
            {
                var layer = data.GetOrCreateLayer("Manual");
                layer.DeltaPosition = new Vector3(ParseFloat(cols, 2), ParseFloat(cols, 3), ParseFloat(cols, 4));
                layer.DeltaRotation = new Quaternion(ParseFloat(cols, 5), ParseFloat(cols, 6), ParseFloat(cols, 7), ParseFloat(cols, 8, 1f));
                layer.Weight = ParseFloat(cols, 9, 1f);
                layer.Enabled = ParseBool(cols, 10, true);
            }

            return data;
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
        // Read: 頂点（名前ベース）
        // ================================================================

        /// <summary>
        /// 名前ベース頂点行を読み込み。ボーン名は一時的にstring[]で返し、後でインデックス解決する
        /// BoneWeightにはダミー値(0)を設定しておく
        /// </summary>
        private static (Vertex vertex, string[] boneNames, string[] mirrorBoneNames) ReadVertexNameBased(string[] cols)
        {
            // vn,id,px,py,pz,flags,bwCount,[boneName0..3,w0..3],mbwCount,[...],uvCount,[uv...],nrmCount,[nrm...]
            int idx = 1;
            int id = ParseInt(cols, idx++);
            float px = ParseFloat(cols, idx++);
            float py = ParseFloat(cols, idx++);
            float pz = ParseFloat(cols, idx++);
            byte flags = (byte)ParseInt(cols, idx++);

            var vertex = new Vertex(id, new Vector3(px, py, pz));
            vertex.Flags = (VertexFlags)flags;

            string[] boneNames = null;
            string[] mirrorBoneNames = null;

            // BoneWeight (名前ベース)
            int bwCount = ParseInt(cols, idx++);
            if (bwCount == 8)
            {
                boneNames = new string[4];
                boneNames[0] = UnescapeCsv(SafeGet(cols, idx++));
                boneNames[1] = UnescapeCsv(SafeGet(cols, idx++));
                boneNames[2] = UnescapeCsv(SafeGet(cols, idx++));
                boneNames[3] = UnescapeCsv(SafeGet(cols, idx++));
                float w0 = ParseFloat(cols, idx++);
                float w1 = ParseFloat(cols, idx++);
                float w2 = ParseFloat(cols, idx++);
                float w3 = ParseFloat(cols, idx++);
                // ダミーインデックスで仮設定（後で名前解決で上書き）
                vertex.BoneWeight = new BoneWeight
                {
                    boneIndex0 = 0, boneIndex1 = 0, boneIndex2 = 0, boneIndex3 = 0,
                    weight0 = w0, weight1 = w1, weight2 = w2, weight3 = w3
                };
            }

            // MirrorBoneWeight (名前ベース)
            int mbwCount = ParseInt(cols, idx++);
            if (mbwCount == 8)
            {
                mirrorBoneNames = new string[4];
                mirrorBoneNames[0] = UnescapeCsv(SafeGet(cols, idx++));
                mirrorBoneNames[1] = UnescapeCsv(SafeGet(cols, idx++));
                mirrorBoneNames[2] = UnescapeCsv(SafeGet(cols, idx++));
                mirrorBoneNames[3] = UnescapeCsv(SafeGet(cols, idx++));
                float w0 = ParseFloat(cols, idx++);
                float w1 = ParseFloat(cols, idx++);
                float w2 = ParseFloat(cols, idx++);
                float w3 = ParseFloat(cols, idx++);
                vertex.MirrorBoneWeight = new BoneWeight
                {
                    boneIndex0 = 0, boneIndex1 = 0, boneIndex2 = 0, boneIndex3 = 0,
                    weight0 = w0, weight1 = w1, weight2 = w2, weight3 = w3
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

            return (vertex, boneNames, mirrorBoneNames);
        }

        private static string SafeGet(string[] cols, int idx) => idx < cols.Length ? cols[idx] : "";

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

        // ================================================================
        // 名前ベース参照の解決
        // ================================================================

        /// <summary>
        /// 名前ベースで読み込んだエントリの参照をインデックスに解決する
        /// </summary>
        /// <param name="entries">読み込んだエントリ一覧</param>
        /// <param name="nameToIndex">名前→インデックスの辞書（呼び出し元で構築）</param>
        public static void ResolveNameReferences(List<CsvMeshEntry> entries, Dictionary<string, int> nameToIndex)
        {
            if (entries == null || nameToIndex == null) return;

            foreach (var entry in entries)
            {
                if (!entry.IsNameBased) continue;
                var mc = entry.MeshContext;
                if (mc == null) continue;

                // ParentIndex
                if (!string.IsNullOrEmpty(entry.ParentName))
                    mc.ParentIndex = LookupIndex(entry.ParentName, nameToIndex);

                // HierarchyParentIndex
                if (!string.IsNullOrEmpty(entry.HierarchyParentName))
                    mc.HierarchyParentIndex = LookupIndex(entry.HierarchyParentName, nameToIndex);

                // BakedMirrorSourceIndex
                if (!string.IsNullOrEmpty(entry.BakedMirrorSourceName))
                    mc.BakedMirrorSourceIndex = LookupIndex(entry.BakedMirrorSourceName, nameToIndex);

                // MorphParentIndex
                if (!string.IsNullOrEmpty(entry.MorphParentName))
                    mc.MorphParentIndex = LookupIndex(entry.MorphParentName, nameToIndex);

                // IK は per-bone 形式（ikRoot/ikLinkBone）で読み込み、
                // 集約 Links / TargetIndex は CsvModelSerializer.Deserialize 末尾の
                // IKChainResolver.RebuildLinksFromPerBone で再構築する（ここでは解決しない）。

                // 頂点 BoneWeight
                if (entry.VertexBoneNames != null)
                {
                    var vertices = mc.MeshObject?.Vertices;
                    if (vertices != null)
                    {
                        for (int vi = 0; vi < entry.VertexBoneNames.Count && vi < vertices.Count; vi++)
                        {
                            var names = entry.VertexBoneNames[vi];
                            if (names == null || !vertices[vi].BoneWeight.HasValue) continue;
                            var bw = vertices[vi].BoneWeight.Value;
                            bw.boneIndex0 = LookupIndex(names[0], nameToIndex);
                            bw.boneIndex1 = LookupIndex(names[1], nameToIndex);
                            bw.boneIndex2 = LookupIndex(names[2], nameToIndex);
                            bw.boneIndex3 = LookupIndex(names[3], nameToIndex);
                            vertices[vi].BoneWeight = bw;
                        }
                    }
                }

                // 頂点 MirrorBoneWeight
                if (entry.VertexMirrorBoneNames != null)
                {
                    var vertices = mc.MeshObject?.Vertices;
                    if (vertices != null)
                    {
                        for (int vi = 0; vi < entry.VertexMirrorBoneNames.Count && vi < vertices.Count; vi++)
                        {
                            var names = entry.VertexMirrorBoneNames[vi];
                            if (names == null || !vertices[vi].MirrorBoneWeight.HasValue) continue;
                            var mbw = vertices[vi].MirrorBoneWeight.Value;
                            mbw.boneIndex0 = LookupIndex(names[0], nameToIndex);
                            mbw.boneIndex1 = LookupIndex(names[1], nameToIndex);
                            mbw.boneIndex2 = LookupIndex(names[2], nameToIndex);
                            mbw.boneIndex3 = LookupIndex(names[3], nameToIndex);
                            vertices[vi].MirrorBoneWeight = mbw;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 名前からインデックスを検索。見つからない場合は-1
        /// </summary>
        private static int LookupIndex(string name, Dictionary<string, int> nameToIndex)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            return nameToIndex.TryGetValue(name, out int idx) ? idx : -1;
        }
    }
}
