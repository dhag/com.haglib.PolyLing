// ObjImportSettings.cs
// OBJ インポート設定。
// Runtime/Poly_Ling_Main/OBJ/Import/ に配置
//
// 【座標系】
//   OBJ は右手系・+Y 上で、メタセコイアと同じ置き方をする。
//   したがって Unity へは X のみ反転（AxisFlip.MqoToUnity と同じ）で揃う。
//   反転軸が奇数個なので面の巻き順も反転する（AxisFlipOps.ReverseWinding）。
//
// 【UV】
//   OBJ も Unity も UV 原点は左下。既定では V を反転しない。

using System;
using UnityEngine;
using Poly_Ling.Ops;

namespace Poly_Ling.OBJ
{
    /// <summary>OBJ をどの単位で MeshContext へ分けるか。</summary>
    public enum ObjGroupingMode
    {
        /// <summary>o（オブジェクト）ごと。o が無ければ g、どちらも無ければ 1 個。</summary>
        Object = 0,

        /// <summary>g（グループ）ごと。g が無ければ o、どちらも無ければ 1 個。</summary>
        Group = 1,

        /// <summary>usemtl（マテリアル）ごと。</summary>
        Material = 2,

        /// <summary>分けずに 1 個へまとめる。</summary>
        Single = 3,
    }

    [Serializable]
    public class ObjImportSettings
    {
        // ================================================================
        // 座標系変換
        // ================================================================

        /// <summary>スケール係数（OBJ 1 単位 → Unity 何 m か）。</summary>
        [Tooltip("インポート時のスケール係数")]
        public float Scale = 1f;

        /// <summary>X軸反転（OBJ 右手系 → Unity 左手系）。</summary>
        [Tooltip("X軸を反転（OBJ は右手系のため既定 ON）")]
        public bool FlipX = true;

        /// <summary>Z軸反転。</summary>
        [Tooltip("Z軸を反転")]
        public bool FlipZ = false;

        /// <summary>軸反転指定。エクスポート側と同一（自己逆元のため同じ設定が逆変換になる）。</summary>
        public AxisFlip Flip => new AxisFlip(FlipX, FlipZ);

        /// <summary>UV V座標反転（1-V）。</summary>
        [Tooltip("UV の V を反転（OBJ / Unity とも原点は左下のため既定 OFF）")]
        public bool FlipUV_V = false;

        // ================================================================
        // 分割
        // ================================================================

        /// <summary>メッシュへの分割単位。</summary>
        [Tooltip("OBJ をどの単位で1オブジェクトにするか")]
        public ObjGroupingMode Grouping = ObjGroupingMode.Object;

        /// <summary>面を持たないオブジェクトをスキップ。</summary>
        [Tooltip("面も線も無いオブジェクトを作らない")]
        public bool SkipEmptyObjects = true;

        /// <summary>折れ線（l）を補助線として取り込む。</summary>
        [Tooltip("l 行を補助線（2頂点の面）として取り込む")]
        public bool ImportLines = true;

        // ================================================================
        // 法線
        // ================================================================

        /// <summary>
        /// ファイルの vn を使う。false、または vn が無いファイルでは
        /// スムージング角から法線を作る（MQO 読込と同じ経路）。
        /// </summary>
        [Tooltip("OBJ の vn をそのまま使う（OFF なら角度で再計算）")]
        public bool UseFileNormals = true;

        /// <summary>vn が無い場合のスムージング角（度）。</summary>
        [Tooltip("法線を再計算するときのスムージング角")]
        [Range(0f, 180f)]
        public float SmoothingAngle = 59.5f;

        // ================================================================
        // マテリアル
        // ================================================================

        /// <summary>mtllib を読み込む。</summary>
        [Tooltip("mtllib で指定された MTL を読み込む")]
        public bool ImportMaterials = true;

        /// <summary>テクスチャを読み込む。</summary>
        [Tooltip("map_Kd などのテクスチャを読み込む")]
        public bool ImportTextures = true;

        /// <summary>OBJ ファイルのあるフォルダ（MTL・テクスチャの相対パス基準）。</summary>
        [NonSerialized]
        public string BaseDir;

        // ================================================================
        // 生成
        // ================================================================

        public static ObjImportSettings CreateDefault() => new ObjImportSettings();

        public ObjImportSettings Clone()
        {
            return new ObjImportSettings
            {
                Scale            = this.Scale,
                FlipX            = this.FlipX,
                FlipZ            = this.FlipZ,
                FlipUV_V         = this.FlipUV_V,
                Grouping         = this.Grouping,
                SkipEmptyObjects = this.SkipEmptyObjects,
                ImportLines      = this.ImportLines,
                UseFileNormals   = this.UseFileNormals,
                SmoothingAngle   = this.SmoothingAngle,
                ImportMaterials  = this.ImportMaterials,
                ImportTextures   = this.ImportTextures,
                BaseDir          = this.BaseDir,
            };
        }
    }
}
