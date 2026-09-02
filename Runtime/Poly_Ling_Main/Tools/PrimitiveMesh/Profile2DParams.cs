// Assets/Editor/MeshCreators/Profile2DExtrude/Profile2DParams.cs
// 2D押し出しメッシュ生成用のパラメータ構造体

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;   // PrimitiveMeshPostProcess.PivotMin / PivotMax

namespace Poly_Ling.Profile2DExtrude
{
    /// <summary>
    /// 2Dループ定義
    /// </summary>
    [Serializable]
    public class Loop
    {
        public List<Vector2> Points = new List<Vector2>();
        public bool IsHole = false;

        public Loop() { }

        public Loop(Loop other)
        {
            Points = new List<Vector2>(other.Points);
            IsHole = other.IsHole;
        }
    }

    /// <summary>
    /// 2D押し出しメッシュ生成パラメータ
    /// </summary>
    [Serializable]
    public struct Profile2DParams : IEquatable<Profile2DParams>
    {
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

        /// <summary>拡大率の下限・上限</summary>
        public const float ScaleMin = 0.01f;
        public const float ScaleMax = 10f;

        /// <summary>平行移動の下限・上限</summary>
        public const float OffsetMin = -5f;
        public const float OffsetMax = 5f;

        /// <summary>厚みの下限・上限</summary>
        public const float ThicknessMin = 0f;
        public const float ThicknessMax = 2f;

        /// <summary>エッジ分割数の下限・上限</summary>
        public const int EdgeSegmentsMin = 0;
        public const int EdgeSegmentsMax = 16;

        /// <summary>エッジサイズの下限・上限</summary>
        public const float EdgeSizeMin = 0.01f;
        public const float EdgeSizeMax = 0.5f;

        [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
        public string MeshName;
        [PLParam(Ignore = true, Description = "直前に読み書きした CSV のパス。形状には影響しない")]
        public string CsvPath;
        [PLParam(TextKey = "Scale", Description = "輪郭の拡大率", Min = ScaleMin, Max = ScaleMax)]
        public float Scale;
        [PLParam(TextKey = "Offset", Description = "輪郭の平行移動", Min = OffsetMin, Max = OffsetMax)]
        public Vector2 Offset;
        [PLParam(TextKey = "FlipY", Description = "輪郭の Y を反転する")]
        public bool FlipY;
        [PLParam(TextKey = "Thickness", Description = "押し出しの厚み。0 で板", Min = ThicknessMin, Max = ThicknessMax)]
        public float Thickness;
        [PLParam(TextKey = "FrontSegments", Description = "表側エッジの分割数（0=無効 / 1=面取り / 2以上=ラウンド）",
                 Min = EdgeSegmentsMin, Max = EdgeSegmentsMax, Step = 1)]
        public int SegmentsFront;
        [PLParam(TextKey = "BackSegments", Description = "裏側エッジの分割数（0=無効 / 1=面取り / 2以上=ラウンド）",
                 Min = EdgeSegmentsMin, Max = EdgeSegmentsMax, Step = 1)]
        public int SegmentsBack;
        [PLParam(TextKey = "EdgeSize", Description = "表側エッジのサイズ", Min = EdgeSizeMin, Max = EdgeSizeMax)]
        public float EdgeSizeFront;
        [PLParam(TextKey = "EdgeSize", Description = "裏側エッジのサイズ", Min = EdgeSizeMin, Max = EdgeSizeMax)]
        public float EdgeSizeBack;
        [PLParam(TextKey = "EdgeInward", Description = "ラウンドの曲率方向を入れ替える")]
        public bool EdgeInward;
        [PLParam(TextKey = "SymmetryMode", Description = "左右対称として扱う")]
        public bool SymmetryMode;
        /// <summary>AABB サイズ基準のピボット。生成後に -Pivot * サイズ だけ平行移動する</summary>
        [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                 Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
        public Vector3 Pivot;
        [PLParam(TextKey = "Profile2DLoops", Description = "押し出す輪郭のループ列。生成器が実際に読むのはこの値", Required = true)]
        public LoopData[] Loops;
        [PLParam(Ignore = true, Description = "編集中のループの位置。形状には影響しない")]
        public int SelectedLoopIndex;
        [PLParam(Ignore = true, Description = "編集中の点の位置。形状には影響しない")]
        public int SelectedPointIndex;
        [PLParam(Ignore = true, Description = "プレビューの視点角。形状には影響しない")]
        public float RotationX;
        [PLParam(Ignore = true, Description = "プレビューの視点角。形状には影響しない")]
        public float RotationY;

        [Serializable]
        public struct LoopData
        {
            [PLParam(TextKey = "Profile2DLoopPoints", Description = "ループの点列", Required = true)]
            public Vector2[] Points;
            [PLParam(TextKey = "Profile2DLoopIsHole", Description = "穴として扱う")]
            public bool IsHole;

            public LoopData(Loop loop)
            {
                Points = loop.Points.ToArray();
                IsHole = loop.IsHole;
            }

            public Loop ToLoop()
            {
                var loop = new Loop();
                if (Points != null)
                {
                    loop.Points = new List<Vector2>(Points);
                }
                loop.IsHole = IsHole;
                return loop;
            }
        }

        public static Profile2DParams Default => new Profile2DParams
        {
            MeshName = "Profile2DExtrude",
            CsvPath = "",
            Scale = 1.0f,
            Offset = Vector2.zero,
            FlipY = false,
            Thickness = 0f,
            SegmentsFront = 0,
            SegmentsBack = 0,
            EdgeSizeFront = 0.1f,
            EdgeSizeBack = 0.1f,
            EdgeInward = false,
            SymmetryMode = false,
            Pivot = Vector3.zero,
            Loops = null,
            SelectedLoopIndex = 0,
            SelectedPointIndex = -1,
            RotationX = 30f,
            RotationY = 20f
        };

        public bool Equals(Profile2DParams o)
        {
            if (MeshName != o.MeshName) return false;
            if (CsvPath != o.CsvPath) return false;
            if (!Mathf.Approximately(Scale, o.Scale)) return false;
            if (Offset != o.Offset) return false;
            if (FlipY != o.FlipY) return false;
            if (!Mathf.Approximately(Thickness, o.Thickness)) return false;
            if (SegmentsFront != o.SegmentsFront || SegmentsBack != o.SegmentsBack) return false;
            if (!Mathf.Approximately(EdgeSizeFront, o.EdgeSizeFront)) return false;
            if (!Mathf.Approximately(EdgeSizeBack, o.EdgeSizeBack)) return false;
            if (EdgeInward != o.EdgeInward) return false;
            if (SymmetryMode != o.SymmetryMode) return false;
            if (Pivot != o.Pivot) return false;
            if (SelectedLoopIndex != o.SelectedLoopIndex) return false;
            if (SelectedPointIndex != o.SelectedPointIndex) return false;
            if (!Mathf.Approximately(RotationX, o.RotationX)) return false;
            if (!Mathf.Approximately(RotationY, o.RotationY)) return false;

            // ループ比較
            if (Loops == null && o.Loops == null) return true;
            if (Loops == null || o.Loops == null) return false;
            if (Loops.Length != o.Loops.Length) return false;
            for (int i = 0; i < Loops.Length; i++)
            {
                if (Loops[i].IsHole != o.Loops[i].IsHole) return false;
                if (Loops[i].Points == null && o.Loops[i].Points == null) continue;
                if (Loops[i].Points == null || o.Loops[i].Points == null) return false;
                if (Loops[i].Points.Length != o.Loops[i].Points.Length) return false;
                for (int j = 0; j < Loops[i].Points.Length; j++)
                {
                    if (!Mathf.Approximately(Loops[i].Points[j].x, o.Loops[i].Points[j].x) ||
                        !Mathf.Approximately(Loops[i].Points[j].y, o.Loops[i].Points[j].y))
                        return false;
                }
            }
            return true;
        }

        public override bool Equals(object obj) => obj is Profile2DParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;

        /// <summary>
        /// LoopsをList&lt;Loop&gt;に変換
        /// </summary>
        public List<Loop> ToLoopList()
        {
            var list = new List<Loop>();
            if (Loops != null)
            {
                foreach (var ld in Loops)
                {
                    list.Add(ld.ToLoop());
                }
            }
            return list;
        }

        /// <summary>
        /// List&lt;Loop&gt;からLoopsを設定
        /// </summary>
        public void SetLoops(List<Loop> loops)
        {
            if (loops == null || loops.Count == 0)
            {
                Loops = null;
                return;
            }

            Loops = new LoopData[loops.Count];
            for (int i = 0; i < loops.Count; i++)
            {
                Loops[i] = new LoopData(loops[i]);
            }
        }
    }

    /// <summary>
    /// SessionState用シリアライズラッパー
    /// </summary>
    [Serializable]
    public class Profile2DStateWrapper
    {
        public string MeshName;
        public string CsvPath;
        public float Scale;
        public Vector2 Offset;
        public bool FlipY;
        public float Thickness;
        public int SegmentsFront, SegmentsBack;
        public float EdgeSizeFront, EdgeSizeBack;
        public bool EdgeInward;
        public bool SymmetryMode;
        public Vector3 Pivot;
        public LoopWrapper[] Loops;
        public int SelectedLoopIndex;
        public int SelectedPointIndex;
        public float RotationX, RotationY;

        [Serializable]
        public class LoopWrapper
        {
            public Vector2[] Points;
            public bool IsHole;
        }

        public Profile2DStateWrapper() { }

        public Profile2DStateWrapper(Profile2DParams p)
        {
            MeshName = p.MeshName;
            CsvPath = p.CsvPath;
            Scale = p.Scale;
            Offset = p.Offset;
            FlipY = p.FlipY;
            Thickness = p.Thickness;
            SegmentsFront = p.SegmentsFront;
            SegmentsBack = p.SegmentsBack;
            EdgeSizeFront = p.EdgeSizeFront;
            EdgeSizeBack = p.EdgeSizeBack;
            EdgeInward = p.EdgeInward;
            SymmetryMode = p.SymmetryMode;
            Pivot = p.Pivot;
            SelectedLoopIndex = p.SelectedLoopIndex;
            SelectedPointIndex = p.SelectedPointIndex;
            RotationX = p.RotationX;
            RotationY = p.RotationY;

            if (p.Loops != null)
            {
                Loops = new LoopWrapper[p.Loops.Length];
                for (int i = 0; i < p.Loops.Length; i++)
                {
                    Loops[i] = new LoopWrapper
                    {
                        Points = p.Loops[i].Points,
                        IsHole = p.Loops[i].IsHole
                    };
                }
            }
        }

        public Profile2DParams ToParams()
        {
            var loopData = new Profile2DParams.LoopData[Loops?.Length ?? 0];
            if (Loops != null)
            {
                for (int i = 0; i < Loops.Length; i++)
                {
                    loopData[i] = new Profile2DParams.LoopData
                    {
                        Points = Loops[i].Points,
                        IsHole = Loops[i].IsHole
                    };
                }
            }

            return new Profile2DParams
            {
                MeshName = MeshName ?? "Profile2DExtrude",
                CsvPath = CsvPath ?? "",
                Scale = Scale > 0 ? Scale : 1f,
                Offset = Offset,
                FlipY = FlipY,
                Thickness = Thickness,
                SegmentsFront = SegmentsFront,
                SegmentsBack = SegmentsBack,
                EdgeSizeFront = EdgeSizeFront > 0 ? EdgeSizeFront : 0.1f,
                EdgeSizeBack = EdgeSizeBack > 0 ? EdgeSizeBack : 0.1f,
                EdgeInward = EdgeInward,
                SymmetryMode = SymmetryMode,
                Pivot = Pivot,
                Loops = loopData,
                SelectedLoopIndex = SelectedLoopIndex,
                SelectedPointIndex = SelectedPointIndex,
                RotationX = RotationX,
                RotationY = RotationY
            };
        }
    }
}
