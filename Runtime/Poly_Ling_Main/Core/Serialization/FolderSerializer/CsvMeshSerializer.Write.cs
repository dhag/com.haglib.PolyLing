// CsvMeshSerializer.Write.cs
// CSV メッシュ入出力：書き出し（ヘッダ・ボーン・SpringBone・モーフ・選択セット・除外セット・頂点・面）。
// Runtime/Poly_Ling_Main/Core/Serialization/FolderSerializer/ に配置

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
    public static partial class CsvMeshSerializer
    {
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
            // 協働編集: 安定オブジェクトID と 担当者名
            // objectId は位置非依存の識別子。保存/読込を跨いで同一オブジェクトを指す。
            sb.AppendLine($"objectId,{mc.ObjectId}");
            sb.AppendLine($"editorName,{EscapeCsv(mc.EditorName ?? "")}");
            sb.AppendLine($"isTriangulated,{mc.MeshObject?.IsTriangulated ?? false}");
            sb.AppendLine($"preserveNormals,{mc.MeshObject?.PreserveNormals ?? false}");
            // 描画オブジェクトの種別（MeshFilter 系 / SkinnedMesh 系）。
            // この行が無いファイルは旧形式。読込側で頂点ウェイトから求め直す。
            sb.AppendLine($"skinKind,{mc.MeshObject?.SkinKind ?? SkinKind.MeshFilter}");
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
                sb.AppendLine($"mirrorGeometryDerived,{mc.MirrorGeometryDerived}");
            }

            sb.AppendLine($"hasBakedMirrorChild,{mc.HasBakedMirrorChild}");
            if (mc.DetachedMirrorObjectId != 0)
                sb.AppendLine($"detachedMirrorObjectId,{mc.DetachedMirrorObjectId}");
            sb.AppendLine($"excludeFromExport,{mc.ExcludeFromExport}");
            sb.AppendLine($"mirrorBranchRoot,{mc.IsMirrorBranchRoot}");

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

        /// <summary>
        /// Humanoid の per-bone データ（割当と可動域）を出力する。
        ///
        /// 正本は per-bone の HumanBodyBone であり、humanoid.csv は書き出し専用
        /// （CsvModelSerializer の読込は RebuildMappingFromPerBone で per-bone から復元する）。
        /// ボーンを持たない MeshFilter ツリーをそのまま骨格として扱う場合、
        /// 割当先は MeshType.Mesh のコンテキストになるため、型で絞らずに出力する。
        /// 値が空なら何も書かないので、割当の無いコンテキストでは出力が増えない。
        /// </summary>
        private static void WriteHumanoidPerBone(StringBuilder sb, MeshContext mc)
        {
            // Humanoid 割当（per-bone・name主・#5b）
            var human = mc.MeshObject?.HumanBodyBone;
            if (!string.IsNullOrEmpty(human))
            {
                sb.AppendLine($"humanBodyBone,{EscapeCsv(human)}");
            }

            // 左右対のボーン索引（確定値）。左右のボーン対応をウェイトから
            // 推定しないための正本なので、往復で必ず保つ。
            int mirrorBone = mc.MeshObject?.MirrorBoneIndex ?? -1;
            if (mirrorBone >= 0)
            {
                sb.AppendLine($"mirrorBoneIndex,{mirrorBone}");
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
        }

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

            // Humanoid 割当・可動域（per-bone）
            //   Type=Bone 以外でも出力するため WriteHumanoidPerBone に切り出してある。
            //   ボーンの場合の出力位置は従来どおりここ。
            WriteHumanoidPerBone(sb, mc);

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
            //   角度制限（列 8 以降）は後から足した。旧プロジェクトには
            //   その列が無く、読み側が既定値で埋めるので互換は保たれる。
            var j = mo.SpringBoneJoint;
            if (j != null)
            {
                // sbJoint,hitRadius,stiffness,gravityPower,gdX,gdY,gdZ,dragForce,
                //         limitType,lrX,lrY,lrZ,lrW,pitch,yaw
                sb.AppendLine(
                    $"sbJoint,{F(j.HitRadius)},{F(j.StiffnessForce)},{F(j.GravityPower)}," +
                    $"{F(j.GravityDir.x)},{F(j.GravityDir.y)},{F(j.GravityDir.z)},{F(j.DragForce)}," +
                    $"{(int)j.AngleLimitType}," +
                    $"{F(j.LimitRotation.x)},{F(j.LimitRotation.y)},{F(j.LimitRotation.z)},{F(j.LimitRotation.w)}," +
                    $"{F(j.Pitch)},{F(j.Yaw)}");
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

            // モーフのミラー適用（規約は MorphMirrorPolicy.cs を正典とする）。
            // 既定値のときは行を書かない＝旧ファイルと同じ出力になる。
            if (mc.MorphMirrorPolicy != MorphMirrorPolicy.FollowParent || mc.MirrorOfMorphIndex >= 0)
            {
                sb.AppendLine($"morphMirror,{(int)mc.MorphMirrorPolicy},{mc.MirrorOfMorphIndex}");
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
                // ss,name,mode,vertexCount,v0,v1,...,edgeCount,e0v1,e0v2,...,
                //    faceCount,f0,...,lineCount,l0,...,idCount,idx0,id0,parts0,sub0,...
                // 識別子の控えは末尾に足してある。列並びは変えていない。
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

                WriteSelectionSetVertexIds(sb, ss);

                sb.AppendLine();
            }
        }

        /// <summary>
        /// 選択セットが控えている識別子を行末へ足す。
        /// idCount,idx,id,parts,sub の並びを件数ぶん繰り返す。
        /// </summary>
        private static void WriteSelectionSetVertexIds(StringBuilder sb, PartsSelectionSet ss)
        {
            var map = ss?.VertexIds;
            if (map == null || map.Count == 0) { sb.Append(",0"); return; }

            sb.Append($",{map.Count}");
            foreach (var kv in map)
                sb.Append($",{kv.Key},{kv.Value.Id},{kv.Value.PartsId},{kv.Value.SubId}");
        }

        // ================================================================
        // Write: 法線再計算 除外セット
        // ================================================================

        private static void WriteNormalExcludeSets(StringBuilder sb, MeshContext mc)
        {
            var list = mc.MeshObject?.NormalRecalcExcludeList;
            if (list == null || list.Count == 0) return;

            foreach (var ss in list)
            {
                if (ss == null) continue;

                // nx,name,mode,vertexCount,v0,...,edgeCount,e0v1,e0v2,...,faceCount,f0,...,lineCount,l0,...
                sb.Append($"nx,{EscapeCsv(ss.Name)},{ss.Mode}");

                sb.Append($",{ss.Vertices.Count}");
                foreach (var v in ss.Vertices) sb.Append($",{v}");

                sb.Append($",{ss.Edges.Count}");
                foreach (var e in ss.Edges) sb.Append($",{e.V1},{e.V2}");

                sb.Append($",{ss.Faces.Count}");
                foreach (var f in ss.Faces) sb.Append($",{f}");

                sb.Append($",{ss.Lines.Count}");
                foreach (var l in ss.Lines) sb.Append($",{l}");

                WriteSelectionSetVertexIds(sb, ss);

                sb.AppendLine();
            }
        }

        // ================================================================
        // Write: 頂点
        // ================================================================

        private static void WriteVertex(StringBuilder sb, Vertex v)
        {
            // v,id,px,py,pz,flags,bwCount(0or8),bw...,uvCount,uv...,nrmCount,nrm...,subId,partsId
            //
            // subId / partsId は行末に足す。ReadVertex は idx を先頭から順に進めるだけで、
            // ParseInt が範囲外を 0 で返すため、この 2 列を持たない旧ファイルは
            // 自動的に 0（未設定）になる。
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

            // SubId / PartsId（行末。旧ファイルには無いので読み側で 0 に落ちる）
            sb.Append($",{v.SubId},{v.PartsId}");

            sb.AppendLine();

            WriteVertexControlPoints(sb, v);
        }

        // ================================================================
        // Write: 頂点のオプション位置リスト（制御点位置など）
        //
        // 直前の v / vn 行の頂点に属する。保持していない頂点では行自体を
        // 出力しないため、旧ファイルとの互換は行の有無だけで取れる。
        // ================================================================

        private static void WriteVertexControlPoints(StringBuilder sb, Vertex v)
        {
            if (!v.HasControlPoints) return;

            // vcp,count,x,y,z,...
            sb.Append($"vcp,{v.ControlPoints.Count}");
            foreach (var p in v.ControlPoints)
                sb.Append($",{F(p.x)},{F(p.y)},{F(p.z)}");

            sb.AppendLine();
        }

        // ================================================================
        // Write: 頂点（名前ベース）
        // ================================================================

        private static void WriteVertexNameBased(StringBuilder sb, Vertex v, Dictionary<int, string> indexToName)
        {
            // vn,id,px,py,pz,flags,bwCount(0or8),boneName0..3,w0..3,mbwCount,...,uvCount,uv...,nrmCount,nrm...,subId,partsId
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

            // SubId / PartsId（行末。旧ファイルには無いので読み側で 0 に落ちる）
            sb.Append($",{v.SubId},{v.PartsId}");

            sb.AppendLine();

            WriteVertexControlPoints(sb, v);
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
    }
}
