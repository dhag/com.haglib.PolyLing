// CsvMeshSerializer.Read.cs
// CSV メッシュ入出力：1 メッシュ分の読み込みと、BoneTransform・BindPose・剛体／JOINT・SpringBone・BonePose。
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

            // skinKind 行の有無。無い（旧形式）ときだけ頂点ウェイトから求め直す。
            bool sawSkinKind = false;

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
                    case "objectId":
                        // 旧形式のファイルにはこの行が無い。その場合 0 のままで、
                        // ModelContext.Add/Insert または ObjectIdAllocator.EnsureIds が新IDを振る。
                        if (cols.Length > 1 && ulong.TryParse(cols[1], out var __oid))
                        {
                            mc.ObjectId = __oid;
                            Poly_Ling.Data.ObjectIdAllocator.Observe(__oid);
                        }
                        break;
                    case "editorName":
                        mc.EditorName = cols.Length > 1 ? UnescapeCsv(cols[1]) : "";
                        break;
                    case "isTriangulated":
                        meshObject.IsTriangulated = ParseBool(cols, 1);
                        break;
                    case "preserveNormals":
                        meshObject.PreserveNormals = ParseBool(cols, 1);
                        break;
                    case "skinKind":
                        if (cols.Length > 1 && Enum.TryParse<SkinKind>(cols[1], out var __sk))
                        {
                            meshObject.SetSkinKind(__sk);
                            sawSkinKind = true;
                        }
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
                    case "mirrorGeometryDerived":
                        mc.MirrorGeometryDerived = ParseBool(cols, 1);
                        break;
                    case "bakedMirrorSourceIndex":
                        mc.BakedMirrorSourceIndex = ParseInt(cols, 1, -1);
                        break;
                    case "hasBakedMirrorChild":
                        mc.HasBakedMirrorChild = ParseBool(cols, 1);
                        break;
                    case "detachedMirrorObjectId":
                        if (cols.Length >= 2 && ulong.TryParse(cols[1], out ulong __dmoid))
                            mc.DetachedMirrorObjectId = __dmoid;
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
                    case "mirrorBranchRoot":
                        mc.IsMirrorBranchRoot = ParseBool(cols, 1);
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
                    case "mirrorBoneIndex":
                        // mirrorBoneIndex,<左右対のボーンの MeshContextList 索引>
                        meshObject.MirrorBoneIndex = ParseInt(cols, 1, -1);
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
                    case "vrmFirstPerson":
                        // vrmFirstPerson,<VrmFirstPersonType の値>。行が無い旧ファイルは Auto。
                        meshObject.VrmFirstPerson =
                            ModelSerializer.ToVrmFirstPersonType(ParseInt(cols, 1));
                        break;
                    case "morphParentIndex":
                        mc.MorphParentIndex = ParseInt(cols, 1, -1);
                        break;
                    case "morphMirror":
                        // morphMirror,policy,mirrorOfMorphIndex
                        // 行が無い旧ファイルは FollowParent / -1 のまま。
                        mc.MorphMirrorPolicy  = (MorphMirrorPolicy)ParseInt(cols, 1, 0);
                        mc.MirrorOfMorphIndex = ParseInt(cols, 2, -1);
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
                    case "nx":
                        ReadNormalExcludeSet(cols, meshObject);
                        break;
                    case "v":
                        meshObject.Vertices.Add(ReadVertex(cols));
                        break;
                    case "vcp":
                        ReadVertexControlPoints(cols, meshObject);
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

            // 描画オブジェクトの種別。
            //   skinKind 行があれば保存された明示状態をそのまま使う。
            //   無ければ旧形式なので頂点ウェイトから求め直す。ここで求め直さないと
            //   旧プロジェクトのスキンドメッシュが MeshFilter 扱いになり、
            //   WorldMatrix が二重に掛かって位置が飛ぶ。
            //
            //   名前ベースCSVもこの時点で BoneWeight 自体は入っている
            //   （ReadVertexNameBased がダミーのボーン番号で値を作る。番号の解決は
            //    ResolveNameReferences で後から行う）ので、ここで判定できる。
            if (!sawSkinKind)
                meshObject.RecomputeSkinKind();

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
            // sbJoint,hitRadius,stiffness,gravityPower,gdX,gdY,gdZ,dragForce,
            //         limitType,lrX,lrY,lrZ,lrW,pitch,yaw
            //
            // 列 8 以降は後から足したので、旧プロジェクトには無い。
            // 既定値（制限なし・無回転・π・0）で埋める。
            var rot = new Quaternion(
                ParseFloat(cols, 9),  ParseFloat(cols, 10),
                ParseFloat(cols, 11), ParseFloat(cols, 12, 1f));
            if (rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w < 1e-12f)
                rot = Quaternion.identity;

            return new SpringBoneJointData
            {
                HitRadius      = ParseFloat(cols, 1, 0.02f),
                StiffnessForce = ParseFloat(cols, 2, 1.0f),
                GravityPower   = ParseFloat(cols, 3),
                GravityDir     = new Vector3(ParseFloat(cols, 4), ParseFloat(cols, 5, -1f), ParseFloat(cols, 6)),
                DragForce      = ParseFloat(cols, 7, 0.4f),
                AngleLimitType = (SpringBoneAngleLimitType)ParseInt(cols, 8),
                LimitRotation  = rot,
                Pitch          = ParseFloat(cols, 13, Mathf.PI),
                Yaw            = ParseFloat(cols, 14, 0f)
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
    }
}
