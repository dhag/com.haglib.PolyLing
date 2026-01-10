// Assets/Editor/MeshFactory/PMX/Core/PMXDocument.cs
// PMX CSVファイルの中間データ構造
// パース結果を保持し、インポートで使用

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshFactory.PMX
{
    /// <summary>
    /// PMXドキュメント全体を表す中間データ構造
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

        /// <summary>マテリアルリスト</summary>
        public List<PMXMaterial> Materials { get; } = new List<PMXMaterial>();

        /// <summary>ボーンリスト</summary>
        public List<PMXBone> Bones { get; } = new List<PMXBone>();

        /// <summary>モーフリスト</summary>
        public List<PMXMorph> Morphs { get; } = new List<PMXMorph>();

        /// <summary>剛体リスト</summary>
        public List<PMXBody> Bodies { get; } = new List<PMXBody>();

        /// <summary>ジョイントリスト</summary>
        public List<PMXJoint> Joints { get; } = new List<PMXJoint>();

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

        /// <summary>テクスチャパス</summary>
        public string TexturePath { get; set; }

        /// <summary>スフィアテクスチャパス</summary>
        public string SphereTexturePath { get; set; }

        /// <summary>スフィアモード</summary>
        public int SphereMode { get; set; }

        /// <summary>共有Toonフラグ</summary>
        public bool SharedToon { get; set; }

        /// <summary>Toonテクスチャインデックス/パス</summary>
        public string ToonTexture { get; set; }

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

        /// <summary>親ボーン名</summary>
        public string ParentBoneName { get; set; }

        /// <summary>変形階層</summary>
        public int TransformLevel { get; set; }

        /// <summary>ボーンフラグ</summary>
        public int Flags { get; set; }

        /// <summary>接続先ボーン名（またはオフセット）</summary>
        public string ConnectBoneName { get; set; }

        /// <summary>接続先オフセット</summary>
        public Vector3 ConnectOffset { get; set; }

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

        /// <summary>外部親キー</summary>
        public int ExternalParentKey { get; set; }

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
    /// 剛体
    /// </summary>
    public class PMXBody
    {
        /// <summary>剛体名</summary>
        public string Name { get; set; }

        /// <summary>剛体名（英語）</summary>
        public string NameEnglish { get; set; }

        /// <summary>関連ボーン名</summary>
        public string RelatedBoneName { get; set; }

        /// <summary>剛体タイプ（0:Bone, 1:物理演算, 2:物理演算+ボーン追従）</summary>
        public int BodyType { get; set; }

        /// <summary>グループ</summary>
        public int Group { get; set; }

        /// <summary>非衝突グループ文字列</summary>
        public string NonCollisionGroups { get; set; }

        /// <summary>形状（0:球, 1:箱, 2:カプセル）</summary>
        public int Shape { get; set; }

        /// <summary>サイズ</summary>
        public Vector3 Size { get; set; }

        /// <summary>位置</summary>
        public Vector3 Position { get; set; }

        /// <summary>回転（度）</summary>
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

        /// <summary>剛体A名</summary>
        public string BodyAName { get; set; }

        /// <summary>剛体B名</summary>
        public string BodyBName { get; set; }

        /// <summary>ジョイントタイプ</summary>
        public int JointType { get; set; }

        /// <summary>位置</summary>
        public Vector3 Position { get; set; }

        /// <summary>回転（度）</summary>
        public Vector3 Rotation { get; set; }

        /// <summary>移動下限</summary>
        public Vector3 TranslationMin { get; set; }

        /// <summary>移動上限</summary>
        public Vector3 TranslationMax { get; set; }

        /// <summary>回転下限（度）</summary>
        public Vector3 RotationMin { get; set; }

        /// <summary>回転上限（度）</summary>
        public Vector3 RotationMax { get; set; }

        /// <summary>バネ定数-移動</summary>
        public Vector3 SpringTranslation { get; set; }

        /// <summary>バネ定数-回転</summary>
        public Vector3 SpringRotation { get; set; }
    }
}
