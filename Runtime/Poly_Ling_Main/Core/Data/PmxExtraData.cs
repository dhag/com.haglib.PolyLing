// PmxExtraData.cs
// PMX 固有の付帯データ。PolyLing が編集しないが、読み書きで失ってはいけない値を持つ。
//
// 【なぜ要るか】
//   PolyLing の内部表現（MaterialData は URP 用、MeshObject.BoneTransform は姿勢のみ）は
//   PMX の材質・ボーンが持つ値を表現しきれない。置き場が無いと、読み込んだ時点で
//   反射色・環境色・描画フラグ・変形階層・付与親などが消え、書き出しで既定値になる。
//
// 【置き場の規約】
//   MeshObject.cs「ボーン付帯データ格納規約」に合わせる。
//   - ボーン付帯 : MeshObject.PmxBone（IKData / RigidBodyData / JointData と同じ並び）
//   - 材質付帯   : MaterialData.Pmx
//   - モデル情報 : ModelContext.PmxModelInfo
//   いずれも #if UNITY_EDITOR を含まない POCO で、null = PMX 由来でないことを表す。
//
// 【参照の持ち方】
//   ボーン・材質・剛体への参照はすべて名前を主、番号を従とする。
//   JointData.cs の規約と同じ。番号はメッシュの並び替えで容易に壊れるため。

using System;
using UnityEngine;

namespace Poly_Ling.Materials
{
    /// <summary>
    /// PMX 材質の付帯データ。MaterialData.Pmx に持つ。
    /// 名前・拡散色・テクスチャパスは MaterialData 側が正で、ここには置かない。
    /// </summary>
    [Serializable]
    public class PmxMaterialData
    {
        /// <summary>材質名（英）。PMX の英語名欄。</summary>
        public string NameEnglish = "";

        /// <summary>反射色 RGB。</summary>
        public float[] Specular = new float[] { 0f, 0f, 0f };

        /// <summary>反射強度。</summary>
        public float SpecularPower = 0f;

        /// <summary>環境色 RGB。</summary>
        public float[] Ambient = new float[] { 0f, 0f, 0f };

        /// <summary>
        /// 描画フラグ。bit0=両面描画 / bit1=地面影 / bit2=セルフ影マップへの描画 /
        /// bit3=セルフ影の描画 / bit4=エッジ描画 / bit5=頂点カラー /
        /// bit6=Point描画 / bit7=Line描画。
        /// </summary>
        public int DrawFlags = 0;

        /// <summary>エッジ色 RGBA。</summary>
        public float[] EdgeColor = new float[] { 0f, 0f, 0f, 1f };

        /// <summary>エッジサイズ。</summary>
        public float EdgeSize = 1f;

        /// <summary>スフィアテクスチャのパス（空 = なし）。</summary>
        public string SphereTexturePath = "";

        /// <summary>スフィアモード。0=無効 / 1=乗算 / 2=加算 / 3=サブテクスチャ。</summary>
        public int SphereMode = 0;

        /// <summary>共有トゥーンを使うか。</summary>
        public bool SharedToon = false;

        /// <summary>共有トゥーン番号（SharedToon が true のとき有効）。</summary>
        public int ToonTextureIndex = -1;

        /// <summary>個別トゥーンテクスチャのパス（SharedToon が false のとき有効）。</summary>
        public string ToonTexturePath = "";

        /// <summary>
        /// メモ欄。PMX 追加仕様の ObjectName / IsMirror はここに入る。
        /// 書き出し時は PMXExporter が ObjectName と depth を作り直すため、
        /// ここに保持するのは元の内容の保存用。
        /// </summary>
        public string Memo = "";

        public PmxMaterialData Clone()
        {
            return new PmxMaterialData
            {
                NameEnglish      = NameEnglish,
                Specular         = (float[])Specular.Clone(),
                SpecularPower    = SpecularPower,
                Ambient          = (float[])Ambient.Clone(),
                DrawFlags        = DrawFlags,
                EdgeColor        = (float[])EdgeColor.Clone(),
                EdgeSize         = EdgeSize,
                SphereTexturePath= SphereTexturePath,
                SphereMode       = SphereMode,
                SharedToon       = SharedToon,
                ToonTextureIndex = ToonTextureIndex,
                ToonTexturePath  = ToonTexturePath,
                Memo             = Memo
            };
        }

        public Color GetSpecular() => new Color(Specular[0], Specular[1], Specular[2], 1f);
        public Color GetAmbient()  => new Color(Ambient[0],  Ambient[1],  Ambient[2],  1f);
        public Color GetEdgeColor()
            => new Color(EdgeColor[0], EdgeColor[1], EdgeColor[2], EdgeColor[3]);

        public void SetSpecular(Color c) { Specular = new[] { c.r, c.g, c.b }; }
        public void SetAmbient(Color c)  { Ambient  = new[] { c.r, c.g, c.b }; }
        public void SetEdgeColor(Color c){ EdgeColor= new[] { c.r, c.g, c.b, c.a }; }
    }

}

namespace Poly_Ling.Data
{
    /// <summary>
    /// PMX ボーンの付帯データ。MeshObject.PmxBone に持つ。
    /// 名前が PmxBoneData でないのは、CSV 解析側に Poly_Ling.CSV.PmxBoneData が
    /// 既にあり、using を並べた箇所で参照が曖昧になるため。
    /// 位置・親子関係・IK は既存の BoneTransform / HierarchyParentIndex / IKData が正で、
    /// ここには置かない。
    /// </summary>
    [Serializable]
    public class PmxBoneAttrData
    {
        /// <summary>ボーン名（英）。</summary>
        public string NameEnglish = "";

        /// <summary>変形階層。</summary>
        public int TransformLevel = 0;

        /// <summary>
        /// ボーンフラグ。bit0=接続先がボーン / bit1=回転可能 / bit2=移動可能 /
        /// bit3=表示 / bit4=操作可 / bit5=IK / bit8=回転付与 / bit9=移動付与 /
        /// bit10=軸固定 / bit11=ローカル軸 / bit12=物理後変形 / bit13=外部親変形。
        /// IK ビットは書き出し時に IKData の有無から立て直す。
        /// </summary>
        public int Flags = 0;

        /// <summary>接続先ボーン名（bit0 が立っているとき有効）。</summary>
        public string ConnectBoneName = "";

        /// <summary>接続先オフセット（bit0 が寝ているとき有効）。</summary>
        public Vector3 ConnectOffset = Vector3.zero;

        /// <summary>付与親ボーン名（bit8 / bit9 のとき有効）。</summary>
        public string GrantParentBoneName = "";

        /// <summary>付与率。</summary>
        public float GrantRate = 0f;

        /// <summary>固定軸（bit10 のとき有効）。</summary>
        public Vector3 FixedAxis = Vector3.zero;

        /// <summary>ローカル軸 X（bit11 のとき有効）。</summary>
        public Vector3 LocalAxisX = Vector3.right;

        /// <summary>ローカル軸 Z（bit11 のとき有効）。</summary>
        public Vector3 LocalAxisZ = Vector3.forward;

        /// <summary>ローカル軸を自動計算したか。</summary>
        public bool IsLocalAxisAutoCalculated = false;

        /// <summary>外部親キー（bit13 のとき有効）。</summary>
        public int ExternalParentKey = 0;

        public PmxBoneAttrData Clone()
        {
            return new PmxBoneAttrData
            {
                NameEnglish               = NameEnglish,
                TransformLevel            = TransformLevel,
                Flags                     = Flags,
                ConnectBoneName           = ConnectBoneName,
                ConnectOffset             = ConnectOffset,
                GrantParentBoneName       = GrantParentBoneName,
                GrantRate                 = GrantRate,
                FixedAxis                 = FixedAxis,
                LocalAxisX                = LocalAxisX,
                LocalAxisZ                = LocalAxisZ,
                IsLocalAxisAutoCalculated = IsLocalAxisAutoCalculated,
                ExternalParentKey         = ExternalParentKey
            };
        }
    }

    /// <summary>
    /// PMX のモデル情報。ModelContext.PmxModelInfo に持つ。
    /// </summary>
    [Serializable]
    public class PmxModelInfoData
    {
        public string Name           = "";
        public string NameEnglish    = "";
        public string Comment        = "";
        public string CommentEnglish = "";

        public PmxModelInfoData Clone()
        {
            return new PmxModelInfoData
            {
                Name           = Name,
                NameEnglish    = NameEnglish,
                Comment        = Comment,
                CommentEnglish = CommentEnglish
            };
        }
    }
}
