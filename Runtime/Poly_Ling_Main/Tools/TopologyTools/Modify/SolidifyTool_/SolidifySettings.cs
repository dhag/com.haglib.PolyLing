// SolidifySettings.cs
// SolidifyTool（厚み付け）用の設定クラス
// ベベル関連の意味は Profile2DExtrudeMeshGenerator と同じ。

using System;
using UnityEngine;
using Poly_Ling.Core;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// SolidifyTool の設定
    /// </summary>
    [Serializable]
    public class SolidifySettings : IToolSettings
    {
        [SerializeField] private float  _thickness     = 0.1f;
        [SerializeField] private bool   _addToExisting = false;
        [SerializeField] private string _meshName      = "Solidify";

        [SerializeField] private int   _segmentsFront = 0;
        [SerializeField] private int   _segmentsBack  = 0;
        [SerializeField] private float _edgeSizeFront = 0.02f;
        [SerializeField] private float _edgeSizeBack  = 0.02f;
        [SerializeField] private bool  _edgeInward    = false;

        // ================================================================
        // 基本
        // ================================================================

        /// <summary>総厚み。各シェルは ±_thickness/2 移動する</summary>
        public float Thickness
        {
            get => _thickness;
            set => _thickness = Mathf.Clamp(
                value,
                ParameterLimits.GetF("Solidify.Thickness.Min"),
                ParameterLimits.GetF("Solidify.Thickness.Max"));
        }

        /// <summary>true = 既存メッシュに追加 / false = 新規オブジェクト</summary>
        public bool AddToExisting
        {
            get => _addToExisting;
            set => _addToExisting = value;
        }

        /// <summary>生成メッシュ名</summary>
        public string MeshName
        {
            get => _meshName;
            set => _meshName = string.IsNullOrEmpty(value) ? "Solidify" : value;
        }

        // ================================================================
        // ベベル（角処理）
        // ================================================================

        /// <summary>表側エッジ分割数（0=無効 / 1=面取り / 2以上=ラウンド）</summary>
        public int SegmentsFront
        {
            get => _segmentsFront;
            set => _segmentsFront = ClampSegments(value);
        }

        /// <summary>裏側エッジ分割数（0=無効 / 1=面取り / 2以上=ラウンド）</summary>
        public int SegmentsBack
        {
            get => _segmentsBack;
            set => _segmentsBack = ClampSegments(value);
        }

        /// <summary>表側エッジサイズ（面内インセット量＝法線方向の深さ）</summary>
        public float EdgeSizeFront
        {
            get => _edgeSizeFront;
            set => _edgeSizeFront = ClampEdgeSize(value);
        }

        /// <summary>裏側エッジサイズ</summary>
        public float EdgeSizeBack
        {
            get => _edgeSizeBack;
            set => _edgeSizeBack = ClampEdgeSize(value);
        }

        /// <summary>ラウンドの曲率方向を入れ替える（位置は変わらない）</summary>
        public bool EdgeInward
        {
            get => _edgeInward;
            set => _edgeInward = value;
        }

        // ================================================================
        // IToolSettings
        // ================================================================

        public SolidifySettings() { }

        public IToolSettings Clone()
        {
            var c = new SolidifySettings();
            c.CopyFrom(this);
            return c;
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is SolidifySettings src)
            {
                _thickness     = src._thickness;
                _addToExisting = src._addToExisting;
                _meshName      = src._meshName;
                _segmentsFront = src._segmentsFront;
                _segmentsBack  = src._segmentsBack;
                _edgeSizeFront = src._edgeSizeFront;
                _edgeSizeBack  = src._edgeSizeBack;
                _edgeInward    = src._edgeInward;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is SolidifySettings src)
            {
                return !Mathf.Approximately(_thickness, src._thickness)
                    || _addToExisting != src._addToExisting
                    || _meshName != src._meshName
                    || _segmentsFront != src._segmentsFront
                    || _segmentsBack  != src._segmentsBack
                    || !Mathf.Approximately(_edgeSizeFront, src._edgeSizeFront)
                    || !Mathf.Approximately(_edgeSizeBack,  src._edgeSizeBack)
                    || _edgeInward != src._edgeInward;
            }
            return true;
        }

        // ================================================================
        // 内部
        // ================================================================

        private static int ClampSegments(int value)
        {
            int min = Mathf.RoundToInt(ParameterLimits.GetF("Solidify.Segments.Min"));
            int max = Mathf.RoundToInt(ParameterLimits.GetF("Solidify.Segments.Max"));
            return Mathf.Clamp(value, min, max);
        }

        private static float ClampEdgeSize(float value)
        {
            return Mathf.Clamp(
                value,
                ParameterLimits.GetF("Solidify.EdgeSize.Min"),
                ParameterLimits.GetF("Solidify.EdgeSize.Max"));
        }
    }
}
