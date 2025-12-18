// Assets/Editor/MeshCreators/Revolution/RevolutionParams.cs
// 回転体メッシュ生成用のパラメータ構造体

using System;
using UnityEngine;

namespace MeshFactory.Revolution
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
        // 基本パラメータ
        public string MeshName;
        public int RadialSegments;
        public bool CloseTop, CloseBottom, CloseLoop, Spiral;
        public int SpiralTurns;
        public float SpiralPitch;
        public Vector3 Pivot;
        public bool FlipY, FlipZ;
        public float RotationX, RotationY;

        // プロファイル（頂点リスト）
        public Vector2[] Profile;
        public int SelectedPointIndex;

        // プリセット
        public ProfilePreset CurrentPreset;

        // ドーナツ用
        public float DonutMajorRadius, DonutMinorRadius;
        public int DonutTubeSegments;

        // パイプ用
        public float PipeInnerRadius, PipeOuterRadius, PipeHeight;
        public float PipeInnerCornerRadius, PipeOuterCornerRadius;
        public int PipeInnerCornerSegments, PipeOuterCornerSegments;

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
