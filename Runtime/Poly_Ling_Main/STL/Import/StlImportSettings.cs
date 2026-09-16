// StlImportSettings.cs
// STL インポート設定。
// Runtime/Poly_Ling_Main/STL/Import/ に配置
//
// 【座標系】
//   STL は右手系（facet の頂点は外から見て反時計回り）。
//   上方向は規格に無いので UpAxis で指定する（StlDocument.cs の StlAxis）。
//   変換の順序は「上方向を Y へ揃える（回転）→ AxisFlip → スケール」。
//   Y 上に揃えた後は OBJ / メタセコイアと同じ置き方になるので、
//   Unity へは X のみ反転（既定 FlipX = ON）。巻き順は AxisFlipOps.ReverseWinding に従う。

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.STL
{
    [Serializable]
    public class StlImportSettings
    {
        // ================================================================
        // 座標系変換
        // ================================================================

        /// <summary>スケール係数（STL 1 単位 → Unity 何 m か）。</summary>
        [Tooltip("インポート時のスケール係数")]
        [PLParam(Description = "インポート時のスケール係数")]
        public float Scale = 1f;

        /// <summary>ファイルの上方向。</summary>
        [Tooltip("STL の上方向（Z 上は正面 -Y として Y 上へ回転する）")]
        [PLParam(Description = "STL の上方向。Y / Z。Z は正面 -Y として Y 上へ回転する")]
        public StlUpAxis UpAxis = StlUpAxis.Z;

        /// <summary>X軸反転（STL 右手系 → Unity 左手系）。</summary>
        [Tooltip("X軸を反転（STL は右手系のため既定 ON）")]
        [PLParam(Description = "X軸を反転（STL は右手系のため既定 ON）")]
        public bool FlipX = true;

        /// <summary>Z軸反転。</summary>
        [Tooltip("Z軸を反転")]
        [PLParam(Description = "Z軸を反転")]
        public bool FlipZ = false;

        /// <summary>軸反転指定。エクスポート側と同一（自己逆元のため同じ設定が逆変換になる）。</summary>
        public AxisFlip Flip => new AxisFlip(FlipX, FlipZ);

        // ================================================================
        // 頂点
        // ================================================================

        /// <summary>
        /// ファイル上の座標値が完全に一致する頂点を 1 個にまとめる。
        /// STL は三角形ごとに頂点を持つので、OFF だと面どうしがつながらない。
        /// </summary>
        [Tooltip("座標が一致する頂点を共有する（OFF だと三角形がばらばらになる）")]
        [PLParam(Description = "座標が一致する頂点を共有する（OFF だと三角形がばらばらになる）")]
        public bool WeldVertices = true;

        // ================================================================
        // 法線
        // ================================================================

        /// <summary>法線を作るときのスムージング角（度）。</summary>
        [Tooltip("法線を計算するときのスムージング角")]
        [Range(0f, 180f)]
        [PLParam(Description = "法線を計算するときのスムージング角", Min = 0f, Max = 180f)]
        public float SmoothingAngle = 59.5f;

        // ================================================================
        // 生成
        // ================================================================

        public static StlImportSettings CreateDefault() => new StlImportSettings();

        public StlImportSettings Clone()
        {
            return new StlImportSettings
            {
                Scale          = this.Scale,
                UpAxis         = this.UpAxis,
                FlipX          = this.FlipX,
                FlipZ          = this.FlipZ,
                WeldVertices   = this.WeldVertices,
                SmoothingAngle = this.SmoothingAngle,
            };
        }
    }
}
