// Assets/Editor/UndoSystem/MeshEditor/ToolSettingsStorage.cs
// EditorStateContextでツール設定を管理するための拡張
// IToolSettings対応
// ToolNameはIEditTool.Nameを使用

using System;
using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Tools;

namespace MeshFactory.Tools
{
    /// <summary>
    /// ツール設定ストレージ
    /// EditorStateContextに埋め込んで使用
    /// </summary>
    [Serializable]
    public class ToolSettingsStorage
    {
        // ================================================================
        // 内部ストレージ
        // ================================================================

        // ツール名 → 設定インスタンス
        private Dictionary<string, IToolSettings> _settings = new Dictionary<string, IToolSettings>();

        // ================================================================
        // 取得・設定
        // ================================================================

        /// <summary>
        /// 指定ツールの設定を取得
        /// </summary>
        public T Get<T>(string toolName) where T : class, IToolSettings
        {
            if (_settings.TryGetValue(toolName, out var settings))
            {
                return settings as T;
            }
            return null;
        }

        /// <summary>
        /// 設定を保存（ツール名指定）
        /// </summary>
        public void Set(string toolName, IToolSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(toolName)) return;
            _settings[toolName] = settings.Clone();
        }

        /// <summary>
        /// 設定が存在するか
        /// </summary>
        public bool Has(string toolName)
        {
            return _settings.ContainsKey(toolName);
        }

        /// <summary>
        /// 設定を削除
        /// </summary>
        public bool Remove(string toolName)
        {
            return _settings.Remove(toolName);
        }

        /// <summary>
        /// 全設定をクリア
        /// </summary>
        public void Clear()
        {
            _settings.Clear();
        }

        /// <summary>
        /// 登録されているツール名一覧
        /// </summary>
        public IEnumerable<string> ToolNames => _settings.Keys;

        /// <summary>
        /// 設定数
        /// </summary>
        public int Count => _settings.Count;

        // ================================================================
        // スナップショット
        // ================================================================

        /// <summary>
        /// 全設定のスナップショットを作成
        /// </summary>
        public ToolSettingsStorage Clone()
        {
            var clone = new ToolSettingsStorage();
            foreach (var kvp in _settings)
            {
                clone._settings[kvp.Key] = kvp.Value.Clone();
            }
            return clone;
        }

        /// <summary>
        /// 他のストレージからコピー
        /// </summary>
        public void CopyFrom(ToolSettingsStorage other)
        {
            if (other == null) return;

            _settings.Clear();
            foreach (var kvp in other._settings)
            {
                _settings[kvp.Key] = kvp.Value.Clone();
            }
        }

        /// <summary>
        /// 差異があるか判定
        /// </summary>
        public bool IsDifferentFrom(ToolSettingsStorage other)
        {
            if (other == null) return _settings.Count > 0;
            if (_settings.Count != other._settings.Count) return true;

            foreach (var kvp in _settings)
            {
                if (!other._settings.TryGetValue(kvp.Key, out var otherSettings))
                    return true;

                if (kvp.Value.IsDifferentFrom(otherSettings))
                    return true;
            }

            return false;
        }

        // ================================================================
        // ツールとの同期
        // ================================================================

        /// <summary>
        /// ツールから設定を同期（tool.Nameをキーとして使用）
        /// </summary>
        public void SyncFromTool(IEditTool tool)
        {
            if (tool?.Settings != null)
            {
                Set(tool.Name, tool.Settings);
            }
        }

        /// <summary>
        /// ツールへ設定を適用（tool.Nameをキーとして使用）
        /// </summary>
        public void ApplyToTool(IEditTool tool)
        {
            if (tool?.Settings == null) return;

            var stored = Get<IToolSettings>(tool.Name);
            if (stored != null)
            {
                tool.Settings.CopyFrom(stored);
            }
        }

        /// <summary>
        /// 複数のツールに設定を一括適用
        /// </summary>
        public void ApplyToTools(params IEditTool[] tools)
        {
            foreach (var tool in tools)
            {
                ApplyToTool(tool);
            }
        }
    }
}
