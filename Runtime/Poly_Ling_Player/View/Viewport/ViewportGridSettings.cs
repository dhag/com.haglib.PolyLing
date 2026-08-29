// ViewportGridSettings.cs
// 3Dプレビューの「軸」「グリッド平面」の表示設定（全ビューポート共通）。
// PlayerViewportManager が1インスタンス保持し、GridAxisRenderer へ渡す。
// Runtime/Poly_Ling_Player/View/Viewport/ に配置

using System.Globalization;
using UnityEngine;
using Poly_Ling.Core.Rendering;

namespace Poly_Ling.Player
{
    /// <summary>グリッドを敷く平面。</summary>
    public enum GridPlaneKind
    {
        XZ = 0,   // 床面（法線 Y）
        XY = 1,   // 正面（法線 Z）
        YZ = 2,   // 側面（法線 X）
    }

    /// <summary>
    /// 軸・グリッド平面の表示設定。4面のビューポートで共通の値を使う。
    /// 永続化は RecentPaths に CSV 文字列で行う
    /// （<see cref="ViewportDisplaySettings"/> は bool のみなので int ビットだが、
    ///  こちらは float / int を含むため CSV とする）。
    /// </summary>
    public struct ViewportGridSettings
    {
        /// <summary>原点を通る XYZ 軸線を表示する。</summary>
        public bool ShowAxis;

        /// <summary>グリッド平面を表示する。</summary>
        public bool ShowGrid;

        /// <summary>グリッドを敷く平面。</summary>
        public GridPlaneKind Plane;

        /// <summary>グリッド1マスの1辺（Unity単位＝1m）。</summary>
        public float CellSize;

        /// <summary>原点から片側のマス数。全体は (HalfCount*2) マス四方になる。</summary>
        public int HalfCount;

        /// <summary>軸線の片側の長さ（Unity単位）。</summary>
        public float AxisLength;

        /// <summary>
        /// ボーン／メッシュ原点マーカー（くさび）の大きさ（Unity単位）。
        /// MeshSceneRenderer.BoneMarkerScale へ転記される。
        /// 軸・グリッドとは別物だが、4面共通かつ float を含む表示パラメータの
        /// 受け皿がここに揃っているため同じ構造体に持たせる。
        /// </summary>
        public float BoneMarkerScale;

        public static ViewportGridSettings Default => new ViewportGridSettings
        {
            ShowAxis   = true,
            ShowGrid   = true,
            Plane      = GridPlaneKind.XZ,
            CellSize   = 1f,
            HalfCount  = 10,
            AxisLength = 10f,
            // 旧 MeshSceneRenderer.BoneShapeScale(0.04f) の 1/4。
            // MeshSceneRenderer.BoneMarkerScale の初期値とそろえること。
            BoneMarkerScale = 0.01f,
        };

        /// <summary>
        /// 数値を有効範囲へ丸めたコピーを返す（元の値は変更しない）。
        /// CellSize / AxisLength が 0 以下だとメッシュが退化するため必須。
        /// </summary>
        public ViewportGridSettings Clamped()
        {
            var s = this;
            s.CellSize   = Mathf.Clamp(s.CellSize,   0.001f, 1000f);
            s.AxisLength = Mathf.Clamp(s.AxisLength, 0.001f, 10000f);
            s.HalfCount  = Mathf.Clamp(s.HalfCount,  1,      500);
            // 0 以下だとくさびが 1 点に潰れて見えなくなる。
            s.BoneMarkerScale = Mathf.Clamp(s.BoneMarkerScale, 0.0001f, 10f);
            if (s.Plane < GridPlaneKind.XZ || s.Plane > GridPlaneKind.YZ)
                s.Plane = GridPlaneKind.XZ;
            return s;
        }

        /// <summary>GridAxisRenderer へ渡す描画パラメータへ変換する。</summary>
        public GridAxisParams ToParams()
        {
            var s = Clamped();
            return new GridAxisParams
            {
                ShowAxis   = s.ShowAxis,
                ShowGrid   = s.ShowGrid,
                Plane      = (int)s.Plane,
                CellSize   = s.CellSize,
                HalfCount  = s.HalfCount,
                AxisLength = s.AxisLength,
            };
        }

        // ── 永続化（RecentPaths に CSV 文字列で保存する） ──────────────

        public string ToCsv()
        {
            var ci = CultureInfo.InvariantCulture;
            return string.Join(",", new string[]
            {
                ShowAxis ? "1" : "0",
                ShowGrid ? "1" : "0",
                ((int)Plane).ToString(ci),
                CellSize.ToString("R", ci),
                HalfCount.ToString(ci),
                AxisLength.ToString("R", ci),
                BoneMarkerScale.ToString("R", ci),
            });
        }

        /// <summary>
        /// CSV から復元する。先頭 6 要素が欠けている・解析できないときは Default を返す。
        ///
        /// 【7 要素目（BoneMarkerScale）だけ別扱いにする理由】
        ///   この項目は後から足したため、既存の保存データは 6 要素しかない。
        ///   そこで全体を Default に落とすと、利用者が設定済みの軸・グリッドまで
        ///   消えてしまう。欠けている・壊れているときはこの項目だけ既定値にする。
        ///   以後フィールドを足すときも同じ扱いにすること。
        /// </summary>
        public static ViewportGridSettings FromCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Default;
            var a = csv.Split(',');
            if (a.Length < 6) return Default;

            var ci = CultureInfo.InvariantCulture;
            if (!int.TryParse(a[2], NumberStyles.Integer, ci, out int plane))      return Default;
            if (!float.TryParse(a[3], NumberStyles.Float, ci, out float cell))     return Default;
            if (!int.TryParse(a[4], NumberStyles.Integer, ci, out int half))       return Default;
            if (!float.TryParse(a[5], NumberStyles.Float, ci, out float axisLen))  return Default;

            float markerScale = Default.BoneMarkerScale;
            if (a.Length >= 7)
            {
                if (!float.TryParse(a[6], NumberStyles.Float, ci, out markerScale))
                    markerScale = Default.BoneMarkerScale;
            }

            return new ViewportGridSettings
            {
                ShowAxis   = a[0] == "1",
                ShowGrid   = a[1] == "1",
                Plane      = (GridPlaneKind)plane,
                CellSize   = cell,
                HalfCount  = half,
                AxisLength = axisLen,
                BoneMarkerScale = markerScale,
            }.Clamped();
        }
    }
}
