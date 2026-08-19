// ObjExportSettings.cs
// OBJ エクスポート設定。
// Runtime/Poly_Ling_Main/OBJ/Export/ に配置
//
// 【座標系】
//   インポート側と同じ軸反転を指定する。AxisFlip は自己逆元なので、
//   同じ設定がそのまま逆変換になる（FlipX = true が既定）。
//
// 【ワールド座標】
//   OBJ は階層もオブジェクト変換も持たない。PolyLing の階層で累積した
//   ワールド行列を頂点へ畳んでから書く必要がある。

using System;
using UnityEngine;
using Poly_Ling.Ops;

namespace Poly_Ling.OBJ
{
    [Serializable]
    public class ObjExportSettings
    {
        // ================================================================
        // 座標系変換
        // ================================================================

        /// <summary>スケール係数（Unity 1m → OBJ 何単位か）。</summary>
        [Tooltip("エクスポート時のスケール係数")]
        public float Scale = 1f;

        /// <summary>X軸反転（Unity 左手系 → OBJ 右手系）。</summary>
        [Tooltip("X軸を反転（OBJ は右手系のため既定 ON）")]
        public bool FlipX = true;

        /// <summary>Z軸反転。</summary>
        [Tooltip("Z軸を反転")]
        public bool FlipZ = false;

        /// <summary>軸反転指定。インポート側と同一。</summary>
        public AxisFlip Flip => new AxisFlip(FlipX, FlipZ);

        /// <summary>UV V座標反転（1-V）。</summary>
        [Tooltip("UV の V を反転（OBJ / Unity とも原点は左下のため既定 OFF）")]
        public bool FlipUV_V = false;

        // ================================================================
        // 出力対象
        // ================================================================

        /// <summary>UV（vt）を出力。</summary>
        [Tooltip("UV を出力する")]
        public bool ExportUVs = true;

        /// <summary>法線（vn）を出力。</summary>
        [Tooltip("法線を出力する")]
        public bool ExportNormals = true;

        /// <summary>マテリアル（usemtl / mtllib と .mtl ファイル）を出力。</summary>
        [Tooltip("マテリアルを出力し、同じ場所へ .mtl を書く")]
        public bool ExportMaterials = true;

        /// <summary>不可視メッシュも出力。</summary>
        [Tooltip("非表示のメッシュも出力する")]
        public bool ExportInvisibleObjects = false;

        /// <summary>非表示の面も出力。</summary>
        [Tooltip("非表示フラグの付いた面も出力する（OBJ に非表示の概念は無い）")]
        public bool ExportHiddenFaces = false;

        /// <summary>補助線を l 行として出力。</summary>
        [Tooltip("3頂点未満の補助線を l 行として出力する")]
        public bool ExportLines = true;

        /// <summary>頂点をワールド座標で出力（OBJ に階層が無いため既定 ON）。</summary>
        [Tooltip("階層のワールド行列を頂点へ畳んで出力する")]
        public bool ExportVerticesInWorldSpace = true;

        // ================================================================
        // 出力形式
        // ================================================================

        /// <summary>小数点以下の桁数。</summary>
        [Tooltip("座標・UV・法線の小数点以下桁数")]
        [Range(1, 9)]
        public int DecimalPrecision = 6;

        // ================================================================
        // 生成
        // ================================================================

        public static ObjExportSettings CreateDefault() => new ObjExportSettings();

        public ObjExportSettings Clone()
        {
            return new ObjExportSettings
            {
                Scale                      = this.Scale,
                FlipX                      = this.FlipX,
                FlipZ                      = this.FlipZ,
                FlipUV_V                   = this.FlipUV_V,
                ExportUVs                  = this.ExportUVs,
                ExportNormals              = this.ExportNormals,
                ExportMaterials            = this.ExportMaterials,
                ExportInvisibleObjects     = this.ExportInvisibleObjects,
                ExportHiddenFaces          = this.ExportHiddenFaces,
                ExportLines                = this.ExportLines,
                ExportVerticesInWorldSpace = this.ExportVerticesInWorldSpace,
                DecimalPrecision           = this.DecimalPrecision,
            };
        }
    }
}
