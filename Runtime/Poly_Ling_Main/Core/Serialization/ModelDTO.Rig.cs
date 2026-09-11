// ModelDTO.Rig.cs
// DTO：IK／剛体／JOINT・スプリングボーン・VRM 1.0 モデルレベル設定・Avatar リターゲット・座標規約。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置（ModelDTO.cs と同じ名前空間。ModelDTO.cs から分割）

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Serialization
{
    // ================================================================
    // IK / 剛体 / JOINT 用DTO（フィールド型・[Serializable]）
    //   既存POCO（Poly_Ling.Data の IKData/RigidBodyData/JointData）は
    //   プロパティ主体のため、JsonUtility互換のフィールド型DTOを別途用意する。
    //   POCO⇔DTO の変換は ModelSerializer の Save/Load ヘルパで行う。
    // ================================================================

    /// <summary>
    /// IKリンクの per-bone データDTO（各リンクボーンに付帯）。
    /// 非null ⇔ そのボーンはIKリンク。所属チェーン・順序は保持しない
    /// （IKルートの effectorBoneName ＋ ボーン階層から IKChainResolver で導出）。
    /// </summary>
    [Serializable]
    public class IKLinkDataDTO
    {
        public bool hasLimit;
        public float[] limitMin; // [x,y,z]（ラジアン）
        public float[] limitMax; // [x,y,z]（ラジアン）
    }

    /// <summary>IKデータDTO（IKルート）。ターゲットは effectorBoneName（name主）。</summary>
    [Serializable]
    public class IKDataDTO
    {
        public bool isIK = true;
        public string effectorBoneName = ""; // エフェクタ（先端）ボーン名・name主
        public int loopCount = 0;
        public float limitAngle = 0f;        // ラジアン
    }

    /// <summary>
    /// Humanoid マッスル可動域DTO（per-bone・null=既定使用）。
    /// min/max/center は [x,y,z]（ラジアン）。3マッスル軸に対応。
    /// </summary>
    [Serializable]
    public class HumanLimitDataDTO
    {
        public float[] min;    // [x,y,z]（ラジアン）
        public float[] max;    // [x,y,z]
        public float[] center; // [x,y,z]
        public float axisLength = 0f;
        public bool useDefaultValues = true;
    }

    /// <summary>剛体データDTO。</summary>
    [Serializable]
    public class RigidBodyDataDTO
    {
        public string nameEnglish = "";
        public string relatedBoneName = "";
        public int boneIndex = -1;
        public int group = 0;
        public int collisionMask = 0;       // ushort を int で保持
        public int shape = 0;               // RigidBodyShape
        public float[] size;                // [x,y,z]
        public float[] position;            // [x,y,z]（working空間）
        public float[] rotation;            // [x,y,z]（working空間・ラジアン）
        public float mass = 1f;
        public float linearDamping = 0f;
        public float angularDamping = 0f;
        public float restitution = 0f;
        public float friction = 0f;
        public int physicsMode = 0;         // RigidBodyPhysicsMode
    }

    /// <summary>JOINTデータDTO。</summary>
    [Serializable]
    public class JointDataDTO
    {
        public string nameEnglish = "";
        public int jointType = 0;
        public string bodyAName = "";
        public string bodyBName = "";
        public int rigidBodyIndexA = -1;
        public int rigidBodyIndexB = -1;
        public float[] position;            // [x,y,z]（working空間）
        public float[] rotation;            // [x,y,z]（working空間・ラジアン）
        public float[] translationMin;      // [x,y,z]（raw）
        public float[] translationMax;      // [x,y,z]（raw）
        public float[] rotationMin;         // [x,y,z]（raw・ラジアン）
        public float[] rotationMax;         // [x,y,z]（raw・ラジアン）
        public float[] springTranslation;   // [x,y,z]（raw）
        public float[] springRotation;      // [x,y,z]（raw）
    }

    // ================================================================
    // スプリングボーン用DTO（VRM SpringBone。フィールド型・[Serializable]）
    //   POCO（Poly_Ling.Data の SpringBoneColliderData/SpringBoneJointData/
    //   SpringBoneChainData）はプロパティ主体のため、JsonUtility互換の
    //   フィールド型DTOを別途用意する。POCO⇔DTO 変換は ModelSerializer が行う。
    // ================================================================

    /// <summary>スプリングボーン・コライダーDTO。</summary>
    [Serializable]
    public class SpringBoneColliderDataDTO
    {
        public int shape = 0;                       // SpringBoneColliderShape
        public float[] offset;                      // [x,y,z]（付帯ボーンのローカル）
        public float radius = 0.05f;
        public float[] tail;                        // [x,y,z]（カプセル終点）
        public float[] normal;                      // [x,y,z]（平面法線）
        public List<int> groupIndices = new List<int>();  // 所属group index（複数可）
    }

    /// <summary>スプリングボーン・ジョイントDTO。</summary>
    [Serializable]
    public class SpringBoneJointDataDTO
    {
        public float hitRadius = 0.02f;
        public float stiffnessForce = 1.0f;
        public float gravityPower = 0f;
        public float[] gravityDir;                  // [x,y,z]
        public float dragForce = 0.4f;

        // 角度制限（VRMC_springBone_limit）。後から足したので、
        // 旧 JSON には無い。既定値は「制限なし・無回転・π・0」。
        public int angleLimitType = 0;              // SpringBoneAngleLimitType
        public float[] limitRotation;               // [x,y,z,w]
        public float pitch = 3.14159265f;
        public float yaw = 0f;
    }

    /// <summary>スプリングボーン・チェーンルートDTO。</summary>
    [Serializable]
    public class SpringBoneChainDataDTO
    {
        public string name = "";
        public List<int> colliderGroupIndices = new List<int>();  // 衝突group index
        public string centerBoneName = "";          // name主（空=World）
    }

    // ================================================================
    // VRM 1.0 モデルレベル設定用DTO（フィールド型・[Serializable]）
    //   POCO（Poly_Ling.Data の VrmMetaData / VrmLookAtData）は
    //   プロパティ主体のため、JsonUtility互換のフィールド型DTOを別途用意する。
    //   enum は int で保持する（並びは POCO の定義順）。
    // ================================================================

    /// <summary>VRM メタ情報DTO。</summary>
    [Serializable]
    public class VrmMetaDTO
    {
        public string name = "";
        public string version = "";
        public List<string> authors = new List<string>();
        public string copyrightInformation = "";
        public string contactInformation = "";
        public List<string> references = new List<string>();
        public string thirdPartyLicenses = "";
        public string thumbnailPath = "";

        public int  avatarPermission = 0;           // VrmAvatarPermission
        public bool violentUsage = false;
        public bool sexualUsage = false;
        public int  commercialUsage = 0;            // VrmCommercialUsage
        public bool politicalOrReligiousUsage = false;
        public bool antisocialOrHateUsage = false;

        public int  creditNotation = 0;             // VrmCreditNotation
        public bool redistribution = false;
        public int  modification = 0;               // VrmModification
        public string otherLicenseUrl = "";
    }

    /// <summary>視線の対応づけ 1 本ぶんのDTO。</summary>
    [Serializable]
    public class VrmLookAtRangeMapDTO
    {
        public float inputMaxDegrees = 90f;
        public float outputScale = 10f;
    }

    /// <summary>VRM 視線設定DTO。</summary>
    [Serializable]
    public class VrmLookAtDTO
    {
        public float[] offsetFromHead;              // [x,y,z]（Unity 左手系のまま）
        public int lookAtType = 0;                  // VrmLookAtType
        public VrmLookAtRangeMapDTO horizontalInner;
        public VrmLookAtRangeMapDTO horizontalOuter;
        public VrmLookAtRangeMapDTO verticalDown;
        public VrmLookAtRangeMapDTO verticalUp;
    }

    /// <summary>
    /// VRM ノード制約DTO（VRMC_node_constraint）。
    /// 規約は VrmNodeConstraintData.cs 冒頭を正典とする。
    /// </summary>
    [Serializable]
    public class VrmNodeConstraintDTO
    {
        public int kind = 2;                        // VrmConstraintKind（既定 Rotation）
        public string sourceName = "";              // name主
        public float weight = 1f;
        public int rollAxis = 0;                    // VrmRollAxis
        public int aimAxis = 0;                     // VrmAimAxis（Unity 側の値）
    }

    // ================================================================
    // Avatar リターゲット設定用DTO（フィールド型・[Serializable]）
    //   既定値は Unity の既定と同じ。旧データで欄が無いときは
    //   ModelDTO.avatarRetarget ごと null になり、未設定として扱われる。
    // ================================================================

    /// <summary>Avatar リターゲット設定DTO。</summary>
    [Serializable]
    public class AvatarRetargetDTO
    {
        public float upperArmTwist = 0.5f;
        public float lowerArmTwist = 0.5f;
        public float upperLegTwist = 0.5f;
        public float lowerLegTwist = 0.5f;
        public float armStretch = 0.05f;
        public float legStretch = 0.05f;
        public float feetSpacing = 0f;
        public bool  hasTranslationDoF = false;
    }

    // ================================================================
    // PMX / MQO 座標規約用DTO（フィールド型・[Serializable]）
    //   既定値は CoordinateConventionData と同じ。
    // ================================================================

    /// <summary>PMX / MQO の座標規約DTO。</summary>
    [Serializable]
    public class CoordinateConventionDTO
    {
        public float pmxUnityRatio = 0.1f;
        public bool  pmxFlipX = true;
        public bool  pmxFlipZ = true;
        public float mqoUnityRatio = 0.01f;
        public bool  mqoFlipX = true;
        public bool  mqoFlipZ = false;
    }
}
