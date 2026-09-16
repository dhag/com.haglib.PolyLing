// StlExportSettings.cs
// STL エクスポート設定。
// Runtime/Poly_Ling_Main/STL/Export/ に配置
//
// 【座標系】
//   インポート側と同じ UpAxis / FlipX / FlipZ を指定すれば逆変換になる。
//   変換の順序はインポートの逆で「スケール・AxisFlip → 上方向をファイルの軸へ戻す」。
//
// 【ワールド座標】
//   STL は階層もオブジェクト変換も持たない。OBJ と同じく、階層で累積した
//   ワールド行列を頂点へ畳んでから書く。

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.STL
{
    [Serializable]
    public class StlExportSettings
    {
        // ================================================================
        // 座標系変換
        // ================================================================

        /// <summary>スケール係数（Unity 1m → STL 何単位か）。</summary>
        [Tooltip("エクスポート時のスケール係数")]
        [PLParam(Description = "エクスポート時のスケール係数")]
        public float Scale = 1f;

        /// <summary>ファイルの上方向。</summary>
        [Tooltip("STL の上方向（Z 上は正面 -Y になるよう回転して書く）")]
        [PLParam(Description = "STL の上方向。Y / Z。Z は正面 -Y になるよう回転して書く")]
        public StlUpAxis UpAxis = StlUpAxis.Z;

        /// <summary>X軸反転（Unity 左手系 → STL 右手系）。</summary>
        [Tooltip("X軸を反転（STL は右手系のため既定 ON）")]
        [PLParam(Description = "X軸を反転（STL は右手系のため既定 ON）")]
        public bool FlipX = true;

        /// <summary>Z軸反転。</summary>
        [Tooltip("Z軸を反転")]
        [PLParam(Description = "Z軸を反転")]
        public bool FlipZ = false;

        /// <summary>軸反転指定。インポート側と同一。</summary>
        public AxisFlip Flip => new AxisFlip(FlipX, FlipZ);

        // ================================================================
        // 出力形式
        // ================================================================

        /// <summary>バイナリで書く。OFF なら ASCII。</summary>
        [Tooltip("バイナリ STL で書く（OFF なら ASCII）")]
        [PLParam(Description = "バイナリ STL で書く（false なら ASCII）")]
        public bool Binary = true;

        /// <summary>ASCII の小数点以下の桁数。</summary>
        [Tooltip("ASCII で書くときの小数点以下桁数")]
        [Range(1, 9)]
        [PLParam(Description = "ASCII で書くときの小数点以下桁数", Min = 1, Max = 9)]
        public int DecimalPrecision = 6;

        // ================================================================
        // 出力対象
        // ================================================================

        /// <summary>不可視メッシュも出力。</summary>
        [Tooltip("非表示のメッシュも出力する")]
        [PLParam(Description = "非表示のメッシュも出力する")]
        public bool ExportInvisibleObjects = false;

        /// <summary>非表示の面も出力。</summary>
        [Tooltip("非表示フラグの付いた面も出力する（STL に非表示の概念は無い）")]
        [PLParam(Description = "非表示フラグの付いた面も出力する（STL に非表示の概念は無い）")]
        public bool ExportHiddenFaces = false;

        /// <summary>頂点をワールド座標で出力（STL に階層が無いため既定 ON）。</summary>
        [Tooltip("階層のワールド行列を頂点へ畳んで出力する")]
        [PLParam(Description = "階層のワールド行列を頂点へ畳んで出力する")]
        public bool ExportVerticesInWorldSpace = true;

        // ================================================================
        // 生成
        // ================================================================

        public static StlExportSettings CreateDefault() => new StlExportSettings();

        public StlExportSettings Clone()
        {
            return new StlExportSettings
            {
                Scale                      = this.Scale,
                UpAxis                     = this.UpAxis,
                FlipX                      = this.FlipX,
                FlipZ                      = this.FlipZ,
                Binary                     = this.Binary,
                DecimalPrecision           = this.DecimalPrecision,
                ExportInvisibleObjects     = this.ExportInvisibleObjects,
                ExportHiddenFaces          = this.ExportHiddenFaces,
                ExportVerticesInWorldSpace = this.ExportVerticesInWorldSpace,
            };
        }
    }
}
