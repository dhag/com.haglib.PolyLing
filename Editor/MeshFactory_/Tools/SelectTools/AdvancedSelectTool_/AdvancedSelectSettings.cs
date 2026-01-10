// Assets/Editor/MeshFactory/Tools/Settings/AdvancedSelectSettings.cs
// AdvancedSelectTool用の設定クラス

using System;
using UnityEngine;

namespace MeshFactory.Tools
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
        ShortestPath
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
            set => _edgeLoopThreshold = Mathf.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// true: 選択に追加, false: 選択から削除
        /// </summary>
        public bool AddToSelection
        {
            get => _addToSelection;
            set => _addToSelection = value;
        }

        public AdvancedSelectSettings() { }

        public AdvancedSelectSettings(AdvancedSelectMode mode, float edgeLoopThreshold, bool addToSelection)
        {
            _mode = mode;
            _edgeLoopThreshold = Mathf.Clamp(edgeLoopThreshold, 0f, 1f);
            _addToSelection = addToSelection;
        }

        public IToolSettings Clone()
        {
            return new AdvancedSelectSettings(_mode, _edgeLoopThreshold, _addToSelection);
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is AdvancedSelectSettings src)
            {
                _mode = src._mode;
                _edgeLoopThreshold = src._edgeLoopThreshold;
                _addToSelection = src._addToSelection;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is AdvancedSelectSettings src)
            {
                return _mode != src._mode
                    || !Mathf.Approximately(_edgeLoopThreshold, src._edgeLoopThreshold)
                    || _addToSelection != src._addToSelection;
            }
            return true;
        }
    }
}
