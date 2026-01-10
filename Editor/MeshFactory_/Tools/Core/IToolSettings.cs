// Assets/Editor/MeshFactory/Tools/IToolSettings.cs
// ツール設定の共通インターフェース
// 各ツールが自分のSettingsを自己管理し、統一的にUndo対応
// ToolNameはIEditTool.Nameを使用するため削除

using System;

namespace MeshFactory.Tools
{
    /// <summary>
    /// ツール設定の共通インターフェース
    /// 各ツールはこれを実装した設定クラスを持つ
    /// </summary>
    public interface IToolSettings
    {
        /// <summary>
        /// 設定の複製を作成（Undo用スナップショット）
        /// </summary>
        IToolSettings Clone();

        /// <summary>
        /// 他の設定と異なるかどうか判定
        /// </summary>
        /// <param name="other">比較対象</param>
        /// <returns>差異があればtrue</returns>
        bool IsDifferentFrom(IToolSettings other);

        /// <summary>
        /// 他の設定からコピー（Undo/Redo適用時）
        /// </summary>
        /// <param name="other">コピー元</param>
        void CopyFrom(IToolSettings other);
    }

    /// <summary>
    /// IToolSettings実装のための基底クラス（オプション）
    /// 共通処理を提供
    /// </summary>
    public abstract class ToolSettingsBase : IToolSettings
    {
        public abstract IToolSettings Clone();
        public abstract bool IsDifferentFrom(IToolSettings other);
        public abstract void CopyFrom(IToolSettings other);

        /// <summary>
        /// 型チェック付きで比較
        /// </summary>
        protected bool IsSameType<T>(IToolSettings other, out T typed) where T : class, IToolSettings
        {
            typed = other as T;
            return typed != null;
        }
    }
}
