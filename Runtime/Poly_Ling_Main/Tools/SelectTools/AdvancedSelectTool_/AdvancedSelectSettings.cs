// Assets/Editor/Poly_Ling/Tools/Settings/AdvancedSelectSettings.cs
// AdvancedSelectTool用の設定クラス

using System;
using UnityEngine;
using Poly_Ling.Core;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 特殊選択ツールのモード
    /// </summary>
    public enum AdvancedSelectMode
    {
        /// <summary>接続領域選択</summary>
        Connected,
        /// <summary>ベルト選択</summary>
        Belt,
        /// <summary>連続エッジ選択</summary>
        EdgeLoop,
        /// <summary>最短ルート選択</summary>
        ShortestPath,
        /// <summary>UV/法線スロット数がしきい値より大きい頂点を選択</summary>
        UvNormalCount,
        /// <summary>軸に対応する平面までの距離がしきい値未満の頂点を選択</summary>
        NearAxis
    }

    /// <summary>
    /// AdvancedSelectToolの設定
    /// </summary>
    [Serializable]
    public class AdvancedSelectSettings : IToolSettings
    {
        [SerializeField] private AdvancedSelectMode _mode = AdvancedSelectMode.Connected;
        [SerializeField] private float _edgeLoopThreshold = 0.7f;
        [SerializeField] private bool _addToSelection = true;
        [SerializeField] private int _uvNormalCountThreshold = 0;
        [SerializeField] private float _axisDistanceThreshold = 0.00001f;
        [SerializeField] private SymmetryAxis _axisKind = SymmetryAxis.X;
        [SerializeField] private bool _limitToCurrentSelection = false;

        /// <summary>
        /// 選択モード
        /// </summary>
        public AdvancedSelectMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        /// <summary>
        /// 連続エッジの内積しきい値 (0.0 - 1.0)
        /// </summary>
        public float EdgeLoopThreshold
        {
            get => _edgeLoopThreshold;
            set => _edgeLoopThreshold = Mathf.Clamp(value, ParameterLimits.GetF("AdvancedSelect.EdgeLoopThreshold.Min"), ParameterLimits.GetF("AdvancedSelect.EdgeLoopThreshold.Max"));
        }

        /// <summary>
        /// true: 選択に追加, false: 選択から削除
        /// </summary>
        public bool AddToSelection
        {
            get => _addToSelection;
            set => _addToSelection = value;
        }

        /// <summary>
        /// UvNormalCount モードのしきい値。
        /// max(Vertex.UVs.Count, Vertex.Normals.Count) がこの値より大きい頂点を選ぶ。
        /// </summary>
        public int UvNormalCountThreshold
        {
            get => _uvNormalCountThreshold;
            set => _uvNormalCountThreshold = Mathf.Clamp(value, 0, ParameterLimits.GetI("AdvancedSelect.UvNormalCount.Max"));
        }

        /// <summary>
        /// NearAxis モードのしきい値。
        /// AxisKind に対応する平面までの距離（|Position.x| 等）がこの値未満の頂点を選ぶ。
        /// </summary>
        public float AxisDistanceThreshold
        {
            get => _axisDistanceThreshold;
            set => _axisDistanceThreshold = Mathf.Clamp(value, ParameterLimits.GetF("AdvancedSelect.AxisDistance.Min"), ParameterLimits.GetF("AdvancedSelect.AxisDistance.Max"));
        }

        /// <summary>
        /// NearAxis モードの軸。X なら YZ 平面（=|Position.x|）までの距離を見る。
        /// </summary>
        public SymmetryAxis AxisKind
        {
            get => _axisKind;
            set => _axisKind = value;
        }

        /// <summary>
        /// true: 現在選択中の頂点の中からのみ選ぶ（UvNormalCount / NearAxis 共通オプション）
        /// </summary>
        public bool LimitToCurrentSelection
        {
            get => _limitToCurrentSelection;
            set => _limitToCurrentSelection = value;
        }

        public AdvancedSelectSettings() { }

        public AdvancedSelectSettings(AdvancedSelectMode mode, float edgeLoopThreshold, bool addToSelection)
        {
            _mode = mode;
            _edgeLoopThreshold = Mathf.Clamp(edgeLoopThreshold, ParameterLimits.GetF("AdvancedSelect.EdgeLoopThreshold.Min"), ParameterLimits.GetF("AdvancedSelect.EdgeLoopThreshold.Max"));
            _addToSelection = addToSelection;
        }

        public IToolSettings Clone()
        {
            return new AdvancedSelectSettings(_mode, _edgeLoopThreshold, _addToSelection)
            {
                _uvNormalCountThreshold = _uvNormalCountThreshold,
                _axisDistanceThreshold = _axisDistanceThreshold,
                _axisKind = _axisKind,
                _limitToCurrentSelection = _limitToCurrentSelection
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is AdvancedSelectSettings src)
            {
                _mode = src._mode;
                _edgeLoopThreshold = src._edgeLoopThreshold;
                _addToSelection = src._addToSelection;
                _uvNormalCountThreshold = src._uvNormalCountThreshold;
                _axisDistanceThreshold = src._axisDistanceThreshold;
                _axisKind = src._axisKind;
                _limitToCurrentSelection = src._limitToCurrentSelection;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is AdvancedSelectSettings src)
            {
                return _mode != src._mode
                    || !Mathf.Approximately(_edgeLoopThreshold, src._edgeLoopThreshold)
                    || _addToSelection != src._addToSelection
                    || _uvNormalCountThreshold != src._uvNormalCountThreshold
                    || !Mathf.Approximately(_axisDistanceThreshold, src._axisDistanceThreshold)
                    || _axisKind != src._axisKind
                    || _limitToCurrentSelection != src._limitToCurrentSelection;
            }
            return true;
        }
    }
}
