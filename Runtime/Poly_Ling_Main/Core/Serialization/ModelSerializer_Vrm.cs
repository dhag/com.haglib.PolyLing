// ModelSerializer_Vrm.cs
// IK／剛体／JOINT・VRM 1.0 設定・スプリングボーン・TPoseBackup の変換。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Materials;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;
using Poly_Ling.Ops;

namespace Poly_Ling.Serialization
{
    public static partial class ModelSerializer
    {
        // ================================================================
        // 永続化拡張（DTO単一真実源化）：IK / BindPose / BoneModelRotation / 剛体 / JOINT
        //   POCO（Poly_Ling.Data）⇔ DTO（フィールド型）の変換。
        // ================================================================

        public static void SaveIKDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var ik = mc?.MeshObject?.IKData;
            if (ik == null) { dto.ikData = null; return; }

            dto.ikData = new IKDataDTO
            {
                isIK = ik.IsIK,
                effectorBoneName = ik.EffectorBoneName ?? "",
                loopCount = ik.LoopCount,
                limitAngle = ik.LimitAngle
            };
        }

        public static void LoadIKDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.ikData == null || mc?.MeshObject == null) return;

            var d = dto.ikData;
            // Links / TargetIndex は読込後に IKChainResolver.RebuildLinksFromPerBone で
            // 再構築する（本段階では per-bone 表現のみ復元）。
            mc.MeshObject.IKData = new IKData
            {
                IsIK = d.isIK,
                EffectorBoneName = d.effectorBoneName ?? "",
                LoopCount = d.loopCount,
                LimitAngle = d.limitAngle,
                Links = new List<IKLinkInfo>()
            };
        }

        public static void SaveIKLinkDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var lk = mc?.MeshObject?.IKLink;
            if (lk == null) { dto.ikLink = null; return; }
            dto.ikLink = new IKLinkDataDTO
            {
                hasLimit = lk.HasLimit,
                limitMin = SerVec3(lk.LimitMin),
                limitMax = SerVec3(lk.LimitMax)
            };
        }

        public static void LoadIKLinkDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.ikLink == null || mc?.MeshObject == null) return;
            var d = dto.ikLink;
            mc.MeshObject.IKLink = new IKLinkData
            {
                HasLimit = d.hasLimit,
                LimitMin = SerVec3(d.limitMin),
                LimitMax = SerVec3(d.limitMax)
            };
        }

        // ================================================================
        // VRM 1.0 設定 POCO⇔DTO 変換
        //   enum は int で持つ。定義外の値が来ても落とさず既定へ丸める
        //   （手で CSV/JSON を書き換えたときに読めなくなるのを避ける）。
        // ================================================================

        /// <summary>int → VrmFirstPersonType。範囲外は Auto。</summary>
        public static VrmFirstPersonType ToVrmFirstPersonType(int v)
        {
            switch (v)
            {
                case 1:  return VrmFirstPersonType.Both;
                case 2:  return VrmFirstPersonType.ThirdPersonOnly;
                case 3:  return VrmFirstPersonType.FirstPersonOnly;
                default: return VrmFirstPersonType.Auto;
            }
        }

        /// <summary>VRM メタ情報 POCO → DTO。null は null のまま。</summary>
        public static VrmMetaDTO ToVrmMetaDTO(VrmMetaData m)
        {
            if (m == null) return null;
            return new VrmMetaDTO
            {
                name                 = m.Name ?? "",
                version              = m.Version ?? "",
                authors              = (m.Authors != null)
                                       ? new List<string>(m.Authors) : new List<string>(),
                copyrightInformation = m.CopyrightInformation ?? "",
                contactInformation   = m.ContactInformation ?? "",
                references           = (m.References != null)
                                       ? new List<string>(m.References) : new List<string>(),
                thirdPartyLicenses   = m.ThirdPartyLicenses ?? "",
                thumbnailPath        = m.ThumbnailPath ?? "",

                avatarPermission          = (int)m.AvatarPermission,
                violentUsage              = m.ViolentUsage,
                sexualUsage               = m.SexualUsage,
                commercialUsage           = (int)m.CommercialUsage,
                politicalOrReligiousUsage = m.PoliticalOrReligiousUsage,
                antisocialOrHateUsage     = m.AntisocialOrHateUsage,

                creditNotation  = (int)m.CreditNotation,
                redistribution  = m.Redistribution,
                modification    = (int)m.Modification,
                otherLicenseUrl = m.OtherLicenseUrl ?? "",
            };
        }

        /// <summary>VRM メタ情報 DTO → POCO。null は null のまま。</summary>
        public static VrmMetaData FromVrmMetaDTO(VrmMetaDTO d)
        {
            if (d == null) return null;
            return new VrmMetaData
            {
                Name                 = d.name ?? "",
                Version              = d.version ?? "",
                Authors              = (d.authors != null)
                                       ? new List<string>(d.authors) : new List<string>(),
                CopyrightInformation = d.copyrightInformation ?? "",
                ContactInformation   = d.contactInformation ?? "",
                References           = (d.references != null)
                                       ? new List<string>(d.references) : new List<string>(),
                ThirdPartyLicenses   = d.thirdPartyLicenses ?? "",
                ThumbnailPath        = d.thumbnailPath ?? "",

                AvatarPermission          = ToVrmAvatarPermission(d.avatarPermission),
                ViolentUsage              = d.violentUsage,
                SexualUsage               = d.sexualUsage,
                CommercialUsage           = ToVrmCommercialUsage(d.commercialUsage),
                PoliticalOrReligiousUsage = d.politicalOrReligiousUsage,
                AntisocialOrHateUsage     = d.antisocialOrHateUsage,

                CreditNotation  = ToVrmCreditNotation(d.creditNotation),
                Redistribution  = d.redistribution,
                Modification    = ToVrmModification(d.modification),
                OtherLicenseUrl = d.otherLicenseUrl ?? "",
            };
        }

        /// <summary>int → VrmAvatarPermission。範囲外は OnlyAuthor。</summary>
        public static VrmAvatarPermission ToVrmAvatarPermission(int v)
        {
            switch (v)
            {
                case 1:  return VrmAvatarPermission.OnlySeparatelyLicensedPerson;
                case 2:  return VrmAvatarPermission.Everyone;
                default: return VrmAvatarPermission.OnlyAuthor;
            }
        }

        /// <summary>int → VrmCommercialUsage。範囲外は PersonalNonProfit。</summary>
        public static VrmCommercialUsage ToVrmCommercialUsage(int v)
        {
            switch (v)
            {
                case 1:  return VrmCommercialUsage.PersonalProfit;
                case 2:  return VrmCommercialUsage.Corporation;
                default: return VrmCommercialUsage.PersonalNonProfit;
            }
        }

        /// <summary>int → VrmCreditNotation。範囲外は Required。</summary>
        public static VrmCreditNotation ToVrmCreditNotation(int v)
            => (v == 1) ? VrmCreditNotation.Unnecessary : VrmCreditNotation.Required;

        /// <summary>int → VrmModification。範囲外は Prohibited。</summary>
        public static VrmModification ToVrmModification(int v)
        {
            switch (v)
            {
                case 1:  return VrmModification.AllowModification;
                case 2:  return VrmModification.AllowModificationRedistribution;
                default: return VrmModification.Prohibited;
            }
        }

        /// <summary>VRM 視線設定 POCO → DTO。null は null のまま。</summary>
        public static VrmLookAtDTO ToVrmLookAtDTO(VrmLookAtData l)
        {
            if (l == null) return null;
            return new VrmLookAtDTO
            {
                offsetFromHead  = SerVec3(l.OffsetFromHead),
                lookAtType      = (int)l.LookAtType,
                horizontalInner = ToRangeMapDTO(l.HorizontalInner),
                horizontalOuter = ToRangeMapDTO(l.HorizontalOuter),
                verticalDown    = ToRangeMapDTO(l.VerticalDown),
                verticalUp      = ToRangeMapDTO(l.VerticalUp),
            };
        }

        /// <summary>VRM 視線設定 DTO → POCO。null は null のまま。</summary>
        public static VrmLookAtData FromVrmLookAtDTO(VrmLookAtDTO d)
        {
            if (d == null) return null;
            return new VrmLookAtData
            {
                // 欄が無い場合だけ UniVRM の既定 (0, 0.06, 0) に戻す。
                // SerVec3(null) は原点になるが、原点は「頭ボーンそのもの」で
                // 目の基準点としては別の意味になるため、ここでは使わない。
                OffsetFromHead  = (d.offsetFromHead != null && d.offsetFromHead.Length >= 3)
                                  ? SerVec3(d.offsetFromHead)
                                  : new Vector3(0f, 0.06f, 0f),
                LookAtType      = (d.lookAtType == 1) ? VrmLookAtType.Expression : VrmLookAtType.Bone,
                HorizontalInner = FromRangeMapDTO(d.horizontalInner),
                HorizontalOuter = FromRangeMapDTO(d.horizontalOuter),
                VerticalDown    = FromRangeMapDTO(d.verticalDown),
                VerticalUp      = FromRangeMapDTO(d.verticalUp),
            };
        }

        private static VrmLookAtRangeMapDTO ToRangeMapDTO(VrmLookAtRangeMap m)
        {
            var src = m ?? new VrmLookAtRangeMap();
            return new VrmLookAtRangeMapDTO
            {
                inputMaxDegrees = src.InputMaxDegrees,
                outputScale     = src.OutputScale,
            };
        }

        private static VrmLookAtRangeMap FromRangeMapDTO(VrmLookAtRangeMapDTO d)
        {
            if (d == null) return new VrmLookAtRangeMap();
            return new VrmLookAtRangeMap(d.inputMaxDegrees, d.outputScale);
        }

        /// <summary>Avatar リターゲット設定 POCO → DTO。null は null のまま。</summary>
        public static AvatarRetargetDTO ToAvatarRetargetDTO(AvatarRetargetData a)
        {
            if (a == null) return null;
            return new AvatarRetargetDTO
            {
                upperArmTwist     = a.UpperArmTwist,
                lowerArmTwist     = a.LowerArmTwist,
                upperLegTwist     = a.UpperLegTwist,
                lowerLegTwist     = a.LowerLegTwist,
                armStretch        = a.ArmStretch,
                legStretch        = a.LegStretch,
                feetSpacing       = a.FeetSpacing,
                hasTranslationDoF = a.HasTranslationDoF,
            };
        }

        /// <summary>PMX / MQO 座標規約 POCO → DTO。null は null のまま。</summary>
        public static CoordinateConventionDTO ToCoordinateConventionDTO(CoordinateConventionData c)
        {
            if (c == null) return null;
            return new CoordinateConventionDTO
            {
                pmxUnityRatio = c.PmxUnityRatio,
                pmxFlipX      = c.PmxFlipX,
                pmxFlipZ      = c.PmxFlipZ,
                mqoUnityRatio = c.MqoUnityRatio,
                mqoFlipX      = c.MqoFlipX,
                mqoFlipZ      = c.MqoFlipZ,
            };
        }

        /// <summary>PMX / MQO 座標規約 DTO → POCO。null は null のまま。</summary>
        public static CoordinateConventionData FromCoordinateConventionDTO(CoordinateConventionDTO d)
        {
            if (d == null) return null;
            return new CoordinateConventionData
            {
                PmxUnityRatio = d.pmxUnityRatio,
                PmxFlipX      = d.pmxFlipX,
                PmxFlipZ      = d.pmxFlipZ,
                MqoUnityRatio = d.mqoUnityRatio,
                MqoFlipX      = d.mqoFlipX,
                MqoFlipZ      = d.mqoFlipZ,
            };
        }

        /// <summary>Avatar リターゲット設定 DTO → POCO。null は null のまま。</summary>
        public static AvatarRetargetData FromAvatarRetargetDTO(AvatarRetargetDTO d)
        {
            if (d == null) return null;
            return new AvatarRetargetData
            {
                UpperArmTwist     = d.upperArmTwist,
                LowerArmTwist     = d.lowerArmTwist,
                UpperLegTwist     = d.upperLegTwist,
                LowerLegTwist     = d.lowerLegTwist,
                ArmStretch        = d.armStretch,
                LegStretch        = d.legStretch,
                FeetSpacing       = d.feetSpacing,
                HasTranslationDoF = d.hasTranslationDoF,
            };
        }

        public static void SaveHumanLimitDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var hl = mc?.MeshObject?.HumanLimit;
            if (hl == null) { dto.humanLimit = null; return; }
            dto.humanLimit = new HumanLimitDataDTO
            {
                min = SerVec3(hl.Min),
                max = SerVec3(hl.Max),
                center = SerVec3(hl.Center),
                axisLength = hl.AxisLength,
                useDefaultValues = hl.UseDefaultValues
            };
        }

        public static void LoadHumanLimitDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.humanLimit == null || mc?.MeshObject == null) return;
            var d = dto.humanLimit;
            mc.MeshObject.HumanLimit = new HumanLimitData
            {
                Min = SerVec3(d.min),
                Max = SerVec3(d.max),
                Center = SerVec3(d.center),
                AxisLength = d.axisLength,
                UseDefaultValues = d.useDefaultValues
            };
        }

        public static void SaveBindPoseToDTO(MeshContext mc, MeshDTO dto)
        {
            if (mc == null) return;
            var m = mc.BindPose;
            if (m == Matrix4x4.identity) { dto.bindPose = null; return; }
            dto.bindPose = new[]
            {
                m.m00, m.m01, m.m02, m.m03,
                m.m10, m.m11, m.m12, m.m13,
                m.m20, m.m21, m.m22, m.m23,
                m.m30, m.m31, m.m32, m.m33
            };
        }

        public static void LoadBindPoseFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.bindPose == null || dto.bindPose.Length < 16 || mc == null) return;
            var a = dto.bindPose;
            var m = new Matrix4x4();
            m.m00 = a[0];  m.m01 = a[1];  m.m02 = a[2];  m.m03 = a[3];
            m.m10 = a[4];  m.m11 = a[5];  m.m12 = a[6];  m.m13 = a[7];
            m.m20 = a[8];  m.m21 = a[9];  m.m22 = a[10]; m.m23 = a[11];
            m.m30 = a[12]; m.m31 = a[13]; m.m32 = a[14]; m.m33 = a[15];
            mc.BindPose = m;
        }

        public static void SaveBoneModelRotationToDTO(MeshContext mc, MeshDTO dto)
        {
            if (mc == null) return;
            var q = mc.BoneModelRotation;
            if (q == Quaternion.identity) { dto.boneModelRotation = null; return; }
            dto.boneModelRotation = new[] { q.x, q.y, q.z, q.w };
        }

        public static void LoadBoneModelRotationFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.boneModelRotation == null || dto.boneModelRotation.Length < 4 || mc == null) return;
            var a = dto.boneModelRotation;
            mc.BoneModelRotation = new Quaternion(a[0], a[1], a[2], a[3]);
        }

        public static void SaveRigidBodyDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var rb = mc?.MeshObject?.RigidBodyData;
            if (rb == null) { dto.rigidBodyData = null; return; }
            dto.rigidBodyData = new RigidBodyDataDTO
            {
                nameEnglish = rb.NameEnglish ?? "",
                relatedBoneName = rb.RelatedBoneName ?? "",
                boneIndex = rb.BoneIndex,
                group = rb.Group,
                collisionMask = rb.CollisionMask,
                shape = (int)rb.Shape,
                size = SerVec3(rb.Size),
                position = SerVec3(rb.Position),
                rotation = SerVec3(rb.Rotation),
                mass = rb.Mass,
                linearDamping = rb.LinearDamping,
                angularDamping = rb.AngularDamping,
                restitution = rb.Restitution,
                friction = rb.Friction,
                physicsMode = (int)rb.PhysicsMode
            };
        }

        public static void LoadRigidBodyDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.rigidBodyData == null || mc?.MeshObject == null) return;
            var d = dto.rigidBodyData;
            mc.MeshObject.RigidBodyData = new RigidBodyData
            {
                NameEnglish = d.nameEnglish ?? "",
                RelatedBoneName = d.relatedBoneName ?? "",
                BoneIndex = d.boneIndex,
                Group = d.group,
                CollisionMask = (ushort)d.collisionMask,
                Shape = (RigidBodyShape)d.shape,
                Size = SerVec3(d.size),
                Position = SerVec3(d.position),
                Rotation = SerVec3(d.rotation),
                Mass = d.mass,
                LinearDamping = d.linearDamping,
                AngularDamping = d.angularDamping,
                Restitution = d.restitution,
                Friction = d.friction,
                PhysicsMode = (RigidBodyPhysicsMode)d.physicsMode
            };
        }

        public static void SaveJointDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var jd = mc?.MeshObject?.JointData;
            if (jd == null) { dto.jointData = null; return; }
            dto.jointData = new JointDataDTO
            {
                nameEnglish = jd.NameEnglish ?? "",
                jointType = jd.JointType,
                bodyAName = jd.BodyAName ?? "",
                bodyBName = jd.BodyBName ?? "",
                rigidBodyIndexA = jd.RigidBodyIndexA,
                rigidBodyIndexB = jd.RigidBodyIndexB,
                position = SerVec3(jd.Position),
                rotation = SerVec3(jd.Rotation),
                translationMin = SerVec3(jd.TranslationMin),
                translationMax = SerVec3(jd.TranslationMax),
                rotationMin = SerVec3(jd.RotationMin),
                rotationMax = SerVec3(jd.RotationMax),
                springTranslation = SerVec3(jd.SpringTranslation),
                springRotation = SerVec3(jd.SpringRotation)
            };
        }

        public static void LoadJointDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.jointData == null || mc?.MeshObject == null) return;
            var d = dto.jointData;
            mc.MeshObject.JointData = new JointData
            {
                NameEnglish = d.nameEnglish ?? "",
                JointType = d.jointType,
                BodyAName = d.bodyAName ?? "",
                BodyBName = d.bodyBName ?? "",
                RigidBodyIndexA = d.rigidBodyIndexA,
                RigidBodyIndexB = d.rigidBodyIndexB,
                Position = SerVec3(d.position),
                Rotation = SerVec3(d.rotation),
                TranslationMin = SerVec3(d.translationMin),
                TranslationMax = SerVec3(d.translationMax),
                RotationMin = SerVec3(d.rotationMin),
                RotationMax = SerVec3(d.rotationMax),
                SpringTranslation = SerVec3(d.springTranslation),
                SpringRotation = SerVec3(d.springRotation)
            };
        }

        // ================================================================
        // スプリングボーン（VRM SpringBone）POCO⇔DTO 変換
        //   コライダー(複数)・ジョイント・チェーンルート をまとめて往復させる。
        //   いずれか非nullのボーンのみDTOに書き出す（null=当該属性なし）。
        // ================================================================

        public static void SaveSpringBoneDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var mo = mc?.MeshObject;
            if (mo == null)
            {
                dto.springBoneColliders = null;
                dto.springBoneJoint = null;
                dto.springBoneChainRoot = null;
                return;
            }

            // コライダー（複数）
            if (mo.SpringBoneColliders != null && mo.SpringBoneColliders.Count > 0)
            {
                var list = new List<SpringBoneColliderDataDTO>(mo.SpringBoneColliders.Count);
                foreach (var c in mo.SpringBoneColliders)
                {
                    if (c == null) continue;
                    list.Add(new SpringBoneColliderDataDTO
                    {
                        shape = (int)c.Shape,
                        offset = SerVec3(c.Offset),
                        radius = c.Radius,
                        tail = SerVec3(c.Tail),
                        normal = SerVec3(c.Normal),
                        groupIndices = c.SpringBoneGroupIndices != null
                            ? new List<int>(c.SpringBoneGroupIndices)
                            : new List<int>()
                    });
                }
                dto.springBoneColliders = list.Count > 0 ? list : null;
            }
            else
            {
                dto.springBoneColliders = null;
            }

            // ジョイント
            var j = mo.SpringBoneJoint;
            dto.springBoneJoint = (j == null) ? null : new SpringBoneJointDataDTO
            {
                hitRadius = j.HitRadius,
                stiffnessForce = j.StiffnessForce,
                gravityPower = j.GravityPower,
                gravityDir = SerVec3(j.GravityDir),
                dragForce = j.DragForce,
                angleLimitType = (int)j.AngleLimitType,
                limitRotation = SerQuat(j.LimitRotation),
                pitch = j.Pitch,
                yaw = j.Yaw
            };

            // チェーンルート
            var ch = mo.SpringBoneChainRoot;
            dto.springBoneChainRoot = (ch == null) ? null : new SpringBoneChainDataDTO
            {
                name = ch.Name ?? "",
                colliderGroupIndices = ch.SpringBoneColliderGroupIndices != null
                    ? new List<int>(ch.SpringBoneColliderGroupIndices)
                    : new List<int>(),
                centerBoneName = ch.CenterBoneName ?? ""
            };
        }

        public static void LoadSpringBoneDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (dto == null || mo == null) return;

            // コライダー（複数）
            if (dto.springBoneColliders != null && dto.springBoneColliders.Count > 0)
            {
                var list = new List<SpringBoneColliderData>(dto.springBoneColliders.Count);
                foreach (var d in dto.springBoneColliders)
                {
                    if (d == null) continue;
                    list.Add(new SpringBoneColliderData
                    {
                        Shape = (SpringBoneColliderShape)d.shape,
                        Offset = SerVec3(d.offset),
                        Radius = d.radius,
                        Tail = SerVec3(d.tail),
                        Normal = SerVec3(d.normal),
                        SpringBoneGroupIndices = d.groupIndices != null
                            ? new List<int>(d.groupIndices)
                            : new List<int>()
                    });
                }
                mo.SpringBoneColliders = list.Count > 0 ? list : null;
            }
            else
            {
                mo.SpringBoneColliders = null;
            }

            // ジョイント
            var jd = dto.springBoneJoint;
            mo.SpringBoneJoint = (jd == null) ? null : new SpringBoneJointData
            {
                HitRadius = jd.hitRadius,
                StiffnessForce = jd.stiffnessForce,
                GravityPower = jd.gravityPower,
                GravityDir = SerVec3(jd.gravityDir),
                DragForce = jd.dragForce,
                AngleLimitType = (SpringBoneAngleLimitType)jd.angleLimitType,
                LimitRotation = SerQuat(jd.limitRotation),
                Pitch = jd.pitch,
                Yaw = jd.yaw
            };

            // チェーンルート
            var cd = dto.springBoneChainRoot;
            mo.SpringBoneChainRoot = (cd == null) ? null : new SpringBoneChainData
            {
                Name = cd.name ?? "",
                SpringBoneColliderGroupIndices = cd.colliderGroupIndices != null
                    ? new List<int>(cd.colliderGroupIndices)
                    : new List<int>(),
                CenterBoneName = cd.centerBoneName ?? ""
            };
        }

        // ================================================================
        // ノード制約（VRMC_node_constraint）POCO⇔DTO 変換
        //   規約は VrmNodeConstraintData.cs 冒頭を正典とする。
        //   null＝制約なし。旧 JSON は欄を持たず null のまま。
        // ================================================================

        public static void SaveVrmConstraintToDTO(MeshContext mc, MeshDTO dto)
        {
            if (dto == null) return;
            var c = mc?.MeshObject?.VrmConstraint;
            dto.vrmConstraint = (c == null) ? null : new VrmNodeConstraintDTO
            {
                kind       = (int)c.Kind,
                sourceName = c.SourceName ?? "",
                weight     = c.Weight,
                rollAxis   = (int)c.RollAxis,
                aimAxis    = (int)c.AimAxis,
            };
        }

        public static void LoadVrmConstraintFromDTO(MeshDTO dto, MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (dto == null || mo == null) return;

            var d = dto.vrmConstraint;
            mo.VrmConstraint = (d == null) ? null : new VrmNodeConstraintData
            {
                Kind       = (VrmConstraintKind)d.kind,
                SourceName = d.sourceName ?? "",
                Weight     = d.weight,
                RollAxis   = (VrmRollAxis)d.rollAxis,
                AimAxis    = (VrmAimAxis)d.aimAxis,
            };
        }

        // Vector3 ⇔ float[3]（本拡張専用の小ヘルパ）
        private static float[] SerVec3(Vector3 v) => new[] { v.x, v.y, v.z };
        private static Vector3 SerVec3(float[] a) =>
            (a != null && a.Length >= 3) ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        // Quaternion ⇔ float[4]。旧 JSON には無いので、
        // 欠けているときと長さ 0 のときは無回転に直す。
        private static float[] SerQuat(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
        private static Quaternion SerQuat(float[] a)
        {
            if (a == null || a.Length < 4) return Quaternion.identity;
            var q = new Quaternion(a[0], a[1], a[2], a[3]);
            if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 1e-12f) return Quaternion.identity;
            return q;
        }

        // ================================================================
        // TPoseBackup ⇔ TPoseBackupDTO（規約4：CSV/JSON 対称）
        //   参照は MeshContext index（実体が index キー）。座標系変換なし（生値）。
        // ================================================================

        // Matrix4x4 ⇔ float[16]（row-major）
        private static float[] SerMat(Matrix4x4 m) => new[]
        {
            m.m00, m.m01, m.m02, m.m03,
            m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23,
            m.m30, m.m31, m.m32, m.m33
        };
        private static Matrix4x4 SerMat(float[] a)
        {
            var m = new Matrix4x4();
            if (a == null || a.Length < 16) return m;
            m.m00 = a[0];  m.m01 = a[1];  m.m02 = a[2];  m.m03 = a[3];
            m.m10 = a[4];  m.m11 = a[5];  m.m12 = a[6];  m.m13 = a[7];
            m.m20 = a[8];  m.m21 = a[9];  m.m22 = a[10]; m.m23 = a[11];
            m.m30 = a[12]; m.m31 = a[13]; m.m32 = a[14]; m.m33 = a[15];
            return m;
        }

        public static TPoseBackupDTO ToTPoseBackupDTO(TPoseBackup backup)
        {
            if (backup == null) return null;

            var dto = new TPoseBackupDTO();

            if (backup.BoneRotations != null)
                foreach (var kv in backup.BoneRotations)
                    dto.boneRotations.Add(new TPoseBoneRotDTO { index = kv.Key, rot = SerVec3(kv.Value) });

            if (backup.WorldMatrices != null)
                foreach (var kv in backup.WorldMatrices)
                    dto.worldMatrices.Add(new TPoseMatrixDTO { index = kv.Key, m = SerMat(kv.Value) });

            if (backup.BindPoses != null)
                foreach (var kv in backup.BindPoses)
                    dto.bindPoses.Add(new TPoseMatrixDTO { index = kv.Key, m = SerMat(kv.Value) });

            if (backup.VertexPositions != null)
            {
                foreach (var kv in backup.VertexPositions)
                {
                    var arr = kv.Value;
                    var flat = new float[(arr?.Length ?? 0) * 3];
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            flat[i * 3]     = arr[i].x;
                            flat[i * 3 + 1] = arr[i].y;
                            flat[i * 3 + 2] = arr[i].z;
                        }
                    }
                    dto.vertexPositions.Add(new TPoseVtxPosDTO { index = kv.Key, p = flat });
                }
            }

            return dto;
        }

        public static TPoseBackup FromTPoseBackupDTO(TPoseBackupDTO dto)
        {
            if (dto == null) return null;

            var backup = new TPoseBackup();

            if (dto.boneRotations != null)
                foreach (var d in dto.boneRotations)
                    if (d != null) backup.BoneRotations[d.index] = SerVec3(d.rot);

            if (dto.worldMatrices != null)
                foreach (var d in dto.worldMatrices)
                    if (d != null) backup.WorldMatrices[d.index] = SerMat(d.m);

            if (dto.bindPoses != null)
                foreach (var d in dto.bindPoses)
                    if (d != null) backup.BindPoses[d.index] = SerMat(d.m);

            if (dto.vertexPositions != null)
            {
                foreach (var d in dto.vertexPositions)
                {
                    if (d == null) continue;
                    var flat = d.p ?? System.Array.Empty<float>();
                    int count = flat.Length / 3;
                    var arr = new Vector3[count];
                    for (int i = 0; i < count; i++)
                        arr[i] = new Vector3(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]);
                    backup.VertexPositions[d.index] = arr;
                }
            }

            return backup;
        }
    }
}
