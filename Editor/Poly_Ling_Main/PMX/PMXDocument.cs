// PMXDocument.cs - PMXデータ構造
// バイナリ読み込み、CSV読み込み両対応

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXドキュメント全体を表すデータ構造
    /// </summary>
    public class PMXDocument
    {
        /// <summary>フォーマットバージョン</summary>
        public float Version { get; set; } = 2.1f;

        /// <summary>文字エンコード（0:UTF16, 1:UTF8）</summary>
        public int CharacterEncoding { get; set; } = 0;

        /// <summary>追加UV数</summary>
        public int AdditionalUVCount { get; set; } = 0;

        /// <summary>ファイルパス（読み込み元）</summary>
        public string FilePath { get; set; }

        /// <summary>ファイル名</summary>
        public string FileName { get; set; }

        /// <summary>モデル情報</summary>
        public PMXModelInfo ModelInfo { get; set; } = new PMXModelInfo();

        /// <summary>頂点リスト</summary>
        public List<PMXVertex> Vertices { get; } = new List<PMXVertex>();

        /// <summary>面リスト</summary>
        public List<PMXFace> Faces { get; } = new List<PMXFace>();

        /// <summary>テクスチャパスリスト</summary>
        public List<string> TexturePaths { get; } = new List<string>();

        /// <summary>マテリアルリスト</summary>
        public List<PMXMaterial> Materials { get; } = new List<PMXMaterial>();

        /// <summary>ボーンリスト</summary>
        public List<PMXBone> Bones { get; } = new List<PMXBone>();

        /// <summary>モーフリスト</summary>
        public List<PMXMorph> Morphs { get; } = new List<PMXMorph>();

        /// <summary>表示枠リスト</summary>
        public List<PMXDisplayFrame> DisplayFrames { get; } = new List<PMXDisplayFrame>();

        /// <summary>剛体リスト</summary>
        public List<PMXRigidBody> RigidBodies { get; } = new List<PMXRigidBody>();

        /// <summary>ジョイントリスト</summary>
        public List<PMXJoint> Joints { get; } = new List<PMXJoint>();

        /// <summary>ソフトボディリスト（PMX 2.1）</summary>
        public List<PMXSoftBody> SoftBodies { get; } = new List<PMXSoftBody>();

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>マテリアル名からインデックスを取得</summary>
        public int GetMaterialIndex(string name)
        {
            for (int i = 0; i < Materials.Count; i++)
            {
                if (Materials[i].Name == name)
                    return i;
            }
            return -1;
        }

        /// <summary>ボーン名からインデックスを取得</summary>
        public int GetBoneIndex(string name)
        {
            for (int i = 0; i < Bones.Count; i++)
            {
                if (Bones[i].Name == name)
                    return i;
            }
            return -1;
        }

        /// <summary>ボーンインデックスからボーン名を取得</summary>
        public string GetBoneName(int index)
        {
            if (index < 0 || index >= Bones.Count)
                return "";
            return Bones[index].Name ?? "";
        }
    }
    
    /// <summary>
    /// モデル情報
    /// </summary>
    public class PMXModelInfo
    {
        /// <summary>モデル名</summary>
        public string Name { get; set; } = "";

        /// <summary>モデル名（英語）</summary>
        public string NameEnglish { get; set; } = "";

        /// <summary>コメント</summary>
        public string Comment { get; set; } = "";

        /// <summary>コメント（英語）</summary>
        public string CommentEnglish { get; set; } = "";
    }
    
    /// <summary>
    /// 頂点
    /// </summary>
    public class PMXVertex
    {
        /// <summary>頂点インデックス</summary>
        public int Index { get; set; }

        /// <summary>位置</summary>
        public Vector3 Position { get; set; }

        /// <summary>法線</summary>
        public Vector3 Normal { get; set; }

        /// <summary>エッジ倍率</summary>
        public float EdgeScale { get; set; }

        /// <summary>UV座標</summary>
        public Vector2 UV { get; set; }

        /// <summary>追加UV（最大4つ）</summary>
        public Vector4[] AdditionalUVs { get; set; }

        /// <summary>ウェイト変形タイプ（0:BDEF1, 1:BDEF2, 2:BDEF4, 3:SDEF, 4:QDEF）</summary>
        public int WeightType { get; set; }

        /// <summary>ボーンウェイト情報</summary>
        public PMXBoneWeight[] BoneWeights { get; set; }

        /// <summary>SDEF用C座標</summary>
        public Vector3 SDEF_C { get; set; }

        /// <summary>SDEF用R0座標</summary>
        public Vector3 SDEF_R0 { get; set; }

        /// <summary>SDEF用R1座標</summary>
        public Vector3 SDEF_R1 { get; set; }
    }
    
    /// <summary>
    /// ボーンウェイト
    /// </summary>
    public class PMXBoneWeight
    {
        /// <summary>ボーン名</summary>
        public string BoneName { get; set; }

        /// <summary>ボーンインデックス（バイナリ読み込み時）</summary>
        public int BoneIndex { get; set; } = -1;

        /// <summary>ウェイト値</summary>
        public float Weight { get; set; }
    }
    
    /// <summary>
    /// 面（三角形）
    /// </summary>
    public class PMXFace
    {
        /// <summary>親マテリアル名</summary>
        public string MaterialName { get; set; }

        /// <summary>マテリアルインデックス（バイナリ読み込み時）</summary>
        public int MaterialIndex { get; set; } = -1;

        /// <summary>面インデックス</summary>
        public int FaceIndex { get; set; }

        /// <summary>頂点インデックス1</summary>
        public int VertexIndex1 { get; set; }

        /// <summary>頂点インデックス2</summary>
        public int VertexIndex2 { get; set; }

        /// <summary>頂点インデックス3</summary>
        public int VertexIndex3 { get; set; }
    }
    
    /// <summary>
    /// マテリアル
    /// </summary>
    public class PMXMaterial
    {
        /// <summary>マテリアル名</summary>
        public string Name { get; set; }

        /// <summary>マテリアル名（英語）</summary>
        public string NameEnglish { get; set; }

        /// <summary>拡散色</summary>
        public Color Diffuse { get; set; } = Color.white;

        /// <summary>反射色</summary>
        public Color Specular { get; set; } = Color.white;

        /// <summary>反射強度</summary>
        public float SpecularPower { get; set; } = 5f;

        /// <summary>環境色</summary>
        public Color Ambient { get; set; } = new Color(0.5f, 0.5f, 0.5f, 1f);

        /// <summary>描画フラグ</summary>
        public int DrawFlags { get; set; }

        /// <summary>エッジ色</summary>
        public Color EdgeColor { get; set; } = Color.black;

        /// <summary>エッジサイズ</summary>
        public float EdgeSize { get; set; } = 1f;

        /// <summary>テクスチャインデックス</summary>
        public int TextureIndex { get; set; } = -1;

        /// <summary>テクスチャパス</summary>
        public string TexturePath { get; set; }

        /// <summary>スフィアテクスチャインデックス</summary>
        public int SphereTextureIndex { get; set; } = -1;

        /// <summary>スフィアテクスチャパス</summary>
        public string SphereTexturePath { get; set; }

        /// <summary>スフィアモード</summary>
        public int SphereMode { get; set; }

        /// <summary>共有Toonフラグ</summary>
        public bool SharedToon { get; set; }

        /// <summary>Toonテクスチャインデックス</summary>
        public int ToonTextureIndex { get; set; } = -1;

        /// <summary>Toonテクスチャパス</summary>
        public string ToonTexturePath { get; set; }

        /// <summary>メモ</summary>
        public string Memo { get; set; }

        /// <summary>面数</summary>
        public int FaceCount { get; set; }
    }
    
    /// <summary>
    /// ボーン
    /// </summary>
    public class PMXBone
    {
        /// <summary>ボーン名</summary>
        public string Name { get; set; }

        /// <summary>ボーン名（英語）</summary>
        public string NameEnglish { get; set; }

        /// <summary>位置</summary>
        public Vector3 Position { get; set; }

        /// <summary>親ボーンインデックス</summary>
        public int ParentIndex { get; set; } = -1;

        /// <summary>親ボーン名</summary>
        public string ParentBoneName { get; set; }

        /// <summary>変形階層</summary>
        public int TransformLevel { get; set; }

        /// <summary>ボーンフラグ</summary>
        public int Flags { get; set; }

        /// <summary>接続先ボーンインデックス</summary>
        public int ConnectBoneIndex { get; set; } = -1;

        /// <summary>接続先ボーン名</summary>
        public string ConnectBoneName { get; set; }

        /// <summary>接続先オフセット</summary>
        public Vector3 ConnectOffset { get; set; }

        /// <summary>付与親ボーンインデックス</summary>
        public int GrantParentIndex { get; set; } = -1;

        /// <summary>付与親ボーン名</summary>
        public string GrantParentBoneName { get; set; }

        /// <summary>付与率</summary>
        public float GrantRate { get; set; }

        /// <summary>軸固定方向</summary>
        public Vector3 FixedAxis { get; set; }

        /// <summary>ローカルX軸方向</summary>
        public Vector3 LocalAxisX { get; set; }

        /// <summary>ローカルZ軸方向</summary>
        public Vector3 LocalAxisZ { get; set; }

        /// <summary>ローカル軸が自動計算されたかどうか（インポート時に設定、エクスポート時は無視）</summary>
        [System.NonSerialized]
        public bool IsLocalAxisAutoCalculated;

        /// <summary>外部親キー</summary>
        public int ExternalParentKey { get; set; }

        /// <summary>IKターゲットボーンインデックス</summary>
        public int IKTargetIndex { get; set; } = -1;

        /// <summary>IKターゲットボーン名</summary>
        public string IKTargetBoneName { get; set; }

        /// <summary>IKループ回数</summary>
        public int IKLoopCount { get; set; }

        /// <summary>IK角度制限</summary>
        public float IKLimitAngle { get; set; }

        /// <summary>IKリンクリスト</summary>
        public List<PMXIKLink> IKLinks { get; } = new List<PMXIKLink>();
    }

    /// <summary>
    /// IKリンク
    /// </summary>
    public class PMXIKLink
    {
        /// <summary>リンクボーンインデックス</summary>
        public int BoneIndex { get; set; } = -1;

        /// <summary>リンクボーン名</summary>
        public string BoneName { get; set; }

        /// <summary>角度制限あり</summary>
        public bool HasLimit { get; set; }

        /// <summary>角度制限下限</summary>
        public Vector3 LimitMin { get; set; }

        /// <summary>角度制限上限</summary>
        public Vector3 LimitMax { get; set; }
    }

    /// <summary>
    /// モーフ
    /// </summary>
    public class PMXMorph
    {
        /// <summary>モーフ名</summary>
        public string Name { get; set; }

        /// <summary>モーフ名（英語）</summary>
        public string NameEnglish { get; set; }

        /// <summary>パネル（0:眉, 1:目, 2:口, 3:その他）</summary>
        public int Panel { get; set; }

        /// <summary>モーフタイプ（0:グループ, 1:頂点, 2:ボーン, 3:UV, ...）</summary>
        public int MorphType { get; set; }

        /// <summary>モーフオフセットリスト</summary>
        public List<PMXMorphOffset> Offsets { get; } = new List<PMXMorphOffset>();
    }

    /// <summary>
    /// モーフオフセット基底
    /// </summary>
    public class PMXMorphOffset
    {
        /// <summary>オフセットタイプ</summary>
        public int Type { get; set; }
    }
    
    /// <summary>
    /// 頂点モーフオフセット
    /// </summary>
    public class PMXVertexMorphOffset : PMXMorphOffset
    {
        /// <summary>頂点インデックス</summary>
        public int VertexIndex { get; set; }

        /// <summary>移動量</summary>
        public Vector3 Offset { get; set; }
    }

    /// <summary>
    /// UVモーフオフセット
    /// </summary>
    public class PMXUVMorphOffset : PMXMorphOffset
    {
        /// <summary>頂点インデックス</summary>
        public int VertexIndex { get; set; }

        /// <summary>UVオフセット</summary>
        public Vector4 Offset { get; set; }
    }

    /// <summary>
    /// ボーンモーフオフセット
    /// </summary>
    public class PMXBoneMorphOffset : PMXMorphOffset
    {
        /// <summary>ボーンインデックス</summary>
        public int BoneIndex { get; set; }

        /// <summary>移動量</summary>
        public Vector3 Translation { get; set; }

        /// <summary>回転</summary>
        public Quaternion Rotation { get; set; }
    }

    /// <summary>
    /// マテリアルモーフオフセット
    /// </summary>
    public class PMXMaterialMorphOffset : PMXMorphOffset
    {
        public int MaterialIndex { get; set; }
        public byte Operation { get; set; }  // 0:乗算, 1:加算
        public Color Diffuse { get; set; }
        public Color Specular { get; set; }
        public float SpecularPower { get; set; }
        public Color Ambient { get; set; }
        public Color EdgeColor { get; set; }
        public float EdgeSize { get; set; }
        public Color TextureCoef { get; set; }
        public Color SphereCoef { get; set; }
        public Color ToonCoef { get; set; }
    }

    /// <summary>
    /// グループモーフオフセット
    /// </summary>
    public class PMXGroupMorphOffset : PMXMorphOffset
    {
        public int MorphIndex { get; set; }
        public float Weight { get; set; }
    }

    /// <summary>
    /// インパルスモーフオフセット（PMX 2.1）
    /// </summary>
    public class PMXImpulseMorphOffset : PMXMorphOffset
    {
        public int RigidBodyIndex { get; set; }
        public bool IsLocal { get; set; }
        public Vector3 Velocity { get; set; }
        public Vector3 Torque { get; set; }
    }

    /// <summary>
    /// 表示枠
    /// </summary>
    public class PMXDisplayFrame
    {
        public string Name { get; set; }
        public string NameEnglish { get; set; }
        public bool IsSpecial { get; set; }
        public List<PMXDisplayElement> Elements { get; } = new List<PMXDisplayElement>();
    }

    public class PMXDisplayElement
    {
        public bool IsMorph { get; set; }
        public int Index { get; set; }
    }

    /// <summary>
    /// 剛体
    /// </summary>
    public class PMXRigidBody
    {
        /// <summary>剛体名</summary>
        public string Name { get; set; }

        /// <summary>剛体名（英語）</summary>
        public string NameEnglish { get; set; }

        /// <summary>関連ボーンインデックス</summary>
        public int BoneIndex { get; set; } = -1;

        /// <summary>関連ボーン名</summary>
        public string RelatedBoneName { get; set; }

        /// <summary>グループ</summary>
        public int Group { get; set; }

        /// <summary>非衝突グループマスク</summary>
        public ushort CollisionMask { get; set; }

        /// <summary>非衝突グループ文字列（CSV用）</summary>
        public string NonCollisionGroups { get; set; }

        /// <summary>形状（0:球, 1:箱, 2:カプセル）</summary>
        public int Shape { get; set; }

        /// <summary>サイズ</summary>
        public Vector3 Size { get; set; }

        /// <summary>位置</summary>
        public Vector3 Position { get; set; }

        /// <summary>回転</summary>
        public Vector3 Rotation { get; set; }

        /// <summary>質量</summary>
        public float Mass { get; set; }

        /// <summary>移動減衰</summary>
        public float LinearDamping { get; set; }

        /// <summary>回転減衰</summary>
        public float AngularDamping { get; set; }

        /// <summary>反発力</summary>
        public float Restitution { get; set; }

        /// <summary>摩擦力</summary>
        public float Friction { get; set; }

        /// <summary>物理演算タイプ（0:Bone追従, 1:物理演算, 2:物理+Bone）</summary>
        public int PhysicsMode { get; set; }
    }

    /// <summary>
    /// ジョイント
    /// </summary>
    public class PMXJoint
    {
        /// <summary>ジョイント名</summary>
        public string Name { get; set; }

        /// <summary>ジョイント名（英語）</summary>
        public string NameEnglish { get; set; }

        /// <summary>ジョイントタイプ</summary>
        public int JointType { get; set; }

        /// <summary>剛体Aインデックス</summary>
        public int RigidBodyIndexA { get; set; } = -1;

        /// <summary>剛体A名</summary>
        public string BodyAName { get; set; }

        /// <summary>剛体Bインデックス</summary>
        public int RigidBodyIndexB { get; set; } = -1;

        /// <summary>剛体B名</summary>
        public string BodyBName { get; set; }

        /// <summary>位置</summary>
        public Vector3 Position { get; set; }

        /// <summary>回転</summary>
        public Vector3 Rotation { get; set; }

        /// <summary>移動下限</summary>
        public Vector3 TranslationMin { get; set; }

        /// <summary>移動上限</summary>
        public Vector3 TranslationMax { get; set; }

        /// <summary>回転下限</summary>
        public Vector3 RotationMin { get; set; }

        /// <summary>回転上限</summary>
        public Vector3 RotationMax { get; set; }

        /// <summary>バネ定数-移動</summary>
        public Vector3 SpringTranslation { get; set; }

        /// <summary>バネ定数-回転</summary>
        public Vector3 SpringRotation { get; set; }
    }

    /// <summary>
    /// ソフトボディ（PMX 2.1）
    /// </summary>
    public class PMXSoftBody
    {
        public string Name { get; set; }
        public string NameEnglish { get; set; }
        public byte Shape { get; set; }
        public int MaterialIndex { get; set; }
        public byte Group { get; set; }
        public ushort CollisionMask { get; set; }
        public byte Flags { get; set; }
        public int BendingLinkDistance { get; set; }
        public int ClusterCount { get; set; }
        public float TotalMass { get; set; }
        public float Margin { get; set; }
        // Config
        public int AeroModel { get; set; }
        public float VCF, DP, DG, LF, PR, VC, DF, MT;
        public float CHR, KHR, SHR, AHR;
        public float SRHR_CL, SKHR_CL, SSHR_CL;
        public float SR_SPLT_CL, SK_SPLT_CL, SS_SPLT_CL;
        public int V_IT, P_IT, D_IT, C_IT;
        public float LST, AST, VST;
        public List<PMXSoftBodyAnchor> Anchors { get; } = new List<PMXSoftBodyAnchor>();
        public List<int> PinnedVertices { get; } = new List<int>();
    }

    public class PMXSoftBodyAnchor
    {
        public int RigidBodyIndex { get; set; }
        public int VertexIndex { get; set; }
        public bool NearMode { get; set; }
    }
}
