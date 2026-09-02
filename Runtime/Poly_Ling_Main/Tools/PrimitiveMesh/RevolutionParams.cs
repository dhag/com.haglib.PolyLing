// Assets/Editor/MeshCreators/Revolution/RevolutionParams.cs
// 回転体メッシュ生成用のパラメータ構造体

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;   // PrimitiveMeshPostProcess.PivotMin / PivotMax

namespace Poly_Ling.Revolution
{
    /// <summary>
    /// プロファイルプリセット
    /// </summary>
    public enum ProfilePreset
    {
        Custom,
        Donut,
        RoundedPipe,
        Vase,
        Goblet,
        Bell,
        Hourglass,
    }

    /// <summary>
    /// 回転体メッシュ生成パラメータ
    /// </summary>
    [Serializable]
    public struct RevolutionParams : IEquatable<RevolutionParams>
    {
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

        /// <summary>円周方向の分割数の下限・上限</summary>
        public const int RadialSegmentsMin = 3;
        public const int RadialSegmentsMax = 64;

        /// <summary>らせんの巻き数の下限・上限</summary>
        public const int SpiralTurnsMin = 1;
        public const int SpiralTurnsMax = 10;

        /// <summary>らせんのピッチの下限・上限</summary>
        public const float SpiralPitchMin = -2f;
        public const float SpiralPitchMax = 2f;

        /// <summary>ドーナツの主半径の下限・上限</summary>
        public const float DonutMajorRadiusMin = 0.2f;
        public const float DonutMajorRadiusMax = 2f;

        /// <summary>ドーナツの管半径の下限・上限</summary>
        public const float DonutMinorRadiusMin = 0.05f;
        public const float DonutMinorRadiusMax = 1f;

        /// <summary>ドーナツの管の分割数の下限・上限</summary>
        public const int DonutTubeSegmentsMin = 4;
        public const int DonutTubeSegmentsMax = 32;

        /// <summary>角丸パイプの内半径の下限・上限</summary>
        public const float PipeInnerRadiusMin = 0.05f;
        public const float PipeInnerRadiusMax = 2f;

        /// <summary>角丸パイプの外半径の下限・上限</summary>
        public const float PipeOuterRadiusMin = 0.06f;
        public const float PipeOuterRadiusMax = 3f;

        /// <summary>角丸パイプの高さの下限・上限</summary>
        public const float PipeHeightMin = 0.1f;
        public const float PipeHeightMax = 3f;

        /// <summary>角丸パイプの角丸半径の下限・上限</summary>
        public const float PipeCornerRadiusMin = 0f;
        public const float PipeCornerRadiusMax = 0.5f;

        /// <summary>角丸パイプの角丸分割数の下限・上限</summary>
        public const int PipeCornerSegmentsMin = 1;
        public const int PipeCornerSegmentsMax = 16;

        // 基本パラメータ
        [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
        public string MeshName;
        [PLParam(TextKey = "RadialSegments", Description = "円周方向の分割数", Min = RadialSegmentsMin,
                 Max = RadialSegmentsMax, Step = 1)]
        public int RadialSegments;
        [PLParam(TextKey = "CloseTop", Description = "上端にフタを張る")]
        public bool CloseTop;
        [PLParam(TextKey = "CloseBottom", Description = "下端にフタを張る")]
        public bool CloseBottom;
        [PLParam(TextKey = "CloseLoop", Description = "プロファイルの終点と始点をつなぐ")]
        public bool CloseLoop;
        [PLParam(TextKey = "Spiral", Description = "らせんにする")]
        public bool Spiral;
        [PLParam(TextKey = "SpiralTurns", Description = "らせんの巻き数", Min = SpiralTurnsMin, Max = SpiralTurnsMax,
                 Step = 1)]
        public int SpiralTurns;
        [PLParam(TextKey = "SpiralPitch", Description = "らせんの 1 巻きあたりの上がり幅", Min = SpiralPitchMin,
                 Max = SpiralPitchMax)]
        public float SpiralPitch;
        [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                 Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
        public Vector3 Pivot;
        [PLParam(TextKey = "FlipY", Description = "生成後に Y を反転する")]
        public bool FlipY;
        [PLParam(TextKey = "FlipZ", Description = "生成後に Z を反転する")]
        public bool FlipZ;
        [PLParam(Ignore = true, Description = "プレビューの視点角。形状には影響しない")]
        public float RotationX;
        [PLParam(Ignore = true, Description = "プレビューの視点角。形状には影響しない")]
        public float RotationY;

        // プロファイル（頂点リスト）
        [PLParam(TextKey = "RevolutionProfile", Description = "回転させる断面の点列。生成器が実際に読むのはこの値", Required = true)]
        public Vector2[] Profile;
        [PLParam(Ignore = true, Description = "編集中の点の位置。形状には影響しない")]
        public int SelectedPointIndex;

        // プリセット
        [PLParam(TextKey = "RevolutionPreset",
                 Description = "プリセット。RevolutionProfileGenerator.CreatePreset が Profile へ展開する。展開はパネル側で行い、生成器は Profile だけを読む")]
        public ProfilePreset CurrentPreset;

        // ドーナツ用
        [PLParam(TextKey = "DonutMajorRadius", Description = "ドーナツの主半径", Min = DonutMajorRadiusMin,
                 Max = DonutMajorRadiusMax)]
        public float DonutMajorRadius;
        [PLParam(TextKey = "DonutMinorRadius", Description = "ドーナツの管半径", Min = DonutMinorRadiusMin,
                 Max = DonutMinorRadiusMax)]
        public float DonutMinorRadius;
        [PLParam(TextKey = "DonutTubeSegs", Description = "ドーナツの管の分割数", Min = DonutTubeSegmentsMin,
                 Max = DonutTubeSegmentsMax, Step = 1)]
        public int DonutTubeSegments;

        // パイプ用
        [PLParam(TextKey = "PipeInnerRadius", Description = "角丸パイプの内半径", Min = PipeInnerRadiusMin,
                 Max = PipeInnerRadiusMax)]
        public float PipeInnerRadius;
        [PLParam(TextKey = "PipeOuterRadius", Description = "角丸パイプの外半径", Min = PipeOuterRadiusMin,
                 Max = PipeOuterRadiusMax)]
        public float PipeOuterRadius;
        [PLParam(TextKey = "PipeHeight", Description = "角丸パイプの高さ", Min = PipeHeightMin, Max = PipeHeightMax)]
        public float PipeHeight;
        [PLParam(TextKey = "CornerRadius", Description = "内側の角丸半径", Min = PipeCornerRadiusMin,
                 Max = PipeCornerRadiusMax)]
        public float PipeInnerCornerRadius;
        [PLParam(TextKey = "CornerRadius", Description = "外側の角丸半径", Min = PipeCornerRadiusMin,
                 Max = PipeCornerRadiusMax)]
        public float PipeOuterCornerRadius;
        [PLParam(TextKey = "CornerSeg", Description = "内側の角丸の分割数", Min = PipeCornerSegmentsMin,
                 Max = PipeCornerSegmentsMax, Step = 1)]
        public int PipeInnerCornerSegments;
        [PLParam(TextKey = "CornerSeg", Description = "外側の角丸の分割数", Min = PipeCornerSegmentsMin,
                 Max = PipeCornerSegmentsMax, Step = 1)]
        public int PipeOuterCornerSegments;

        public static RevolutionParams Default => new RevolutionParams
        {
            MeshName = "Revolution",
            RadialSegments = 24,
            CloseTop = true,
            CloseBottom = true,
            CloseLoop = false,
            Spiral = false,
            SpiralTurns = 3,
            SpiralPitch = 0.35f,
            Pivot = Vector3.zero,
            FlipY = false,
            FlipZ = false,
            RotationX = 20f,
            RotationY = 0f,
            Profile = null,
            SelectedPointIndex = -1,
            CurrentPreset = ProfilePreset.Custom,
            DonutMajorRadius = 0.5f,
            DonutMinorRadius = 0.2f,
            DonutTubeSegments = 12,
            PipeInnerRadius = 0.3f,
            PipeOuterRadius = 0.5f,
            PipeHeight = 1f,
            PipeInnerCornerRadius = 0.05f,
            PipeOuterCornerRadius = 0.05f,
            PipeInnerCornerSegments = 4,
            PipeOuterCornerSegments = 4,
        };

        public bool Equals(RevolutionParams o)
        {
            if (MeshName != o.MeshName) return false;
            if (RadialSegments != o.RadialSegments) return false;
            if (CloseTop != o.CloseTop || CloseBottom != o.CloseBottom) return false;
            if (CloseLoop != o.CloseLoop || Spiral != o.Spiral) return false;
            if (SpiralTurns != o.SpiralTurns) return false;
            if (!Mathf.Approximately(SpiralPitch, o.SpiralPitch)) return false;
            if (Pivot != o.Pivot) return false;
            if (FlipY != o.FlipY || FlipZ != o.FlipZ) return false;
            if (!Mathf.Approximately(RotationX, o.RotationX)) return false;
            if (!Mathf.Approximately(RotationY, o.RotationY)) return false;
            if (CurrentPreset != o.CurrentPreset) return false;
            if (SelectedPointIndex != o.SelectedPointIndex) return false;

            // プロファイル比較
            if (Profile == null && o.Profile == null) { /* OK */ }
            else if (Profile == null || o.Profile == null) return false;
            else if (Profile.Length != o.Profile.Length) return false;
            else
            {
                for (int i = 0; i < Profile.Length; i++)
                {
                    if (!Mathf.Approximately(Profile[i].x, o.Profile[i].x) ||
                        !Mathf.Approximately(Profile[i].y, o.Profile[i].y))
                        return false;
                }
            }

            // ドーナツ
            if (!Mathf.Approximately(DonutMajorRadius, o.DonutMajorRadius)) return false;
            if (!Mathf.Approximately(DonutMinorRadius, o.DonutMinorRadius)) return false;
            if (DonutTubeSegments != o.DonutTubeSegments) return false;

            // パイプ
            if (!Mathf.Approximately(PipeInnerRadius, o.PipeInnerRadius)) return false;
            if (!Mathf.Approximately(PipeOuterRadius, o.PipeOuterRadius)) return false;
            if (!Mathf.Approximately(PipeHeight, o.PipeHeight)) return false;
            if (!Mathf.Approximately(PipeInnerCornerRadius, o.PipeInnerCornerRadius)) return false;
            if (!Mathf.Approximately(PipeOuterCornerRadius, o.PipeOuterCornerRadius)) return false;
            if (PipeInnerCornerSegments != o.PipeInnerCornerSegments) return false;
            if (PipeOuterCornerSegments != o.PipeOuterCornerSegments) return false;

            return true;
        }

        public override bool Equals(object obj) => obj is RevolutionParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;

        /// <summary>
        /// プロファイルのディープコピーを作成
        /// </summary>
        public RevolutionParams DeepCopy()
        {
            var copy = this;
            if (Profile != null)
            {
                copy.Profile = new Vector2[Profile.Length];
                Array.Copy(Profile, copy.Profile, Profile.Length);
            }
            return copy;
        }
    }
}
