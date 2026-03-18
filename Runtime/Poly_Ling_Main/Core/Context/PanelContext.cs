// PanelContext.cs
// 全パネルに共有される唯一のコンテキスト
// メインルーチンが1つだけ生成し、全パネルに同じインスタンスを渡す
// パネルはこれを通じてIProjectViewを受け取り、コマンドを送る

using System;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.Context
{
    public class PanelContext
    {
        /// <summary>最新のビュー（パネルはいつでも読める）</summary>
        public IProjectView CurrentView { get; private set; }

        /// <summary>ビュー更新通知（ビュー, 変更種別）</summary>
        public event Action<IProjectView, ChangeKind> OnViewChanged;

        /// <summary>コマンド送信（パネル → メインルーチン）</summary>
        public Action<PanelCommand> SendCommand { get; }

        public PanelContext(Action<PanelCommand> sendCommand)
        {
            SendCommand = sendCommand ?? throw new ArgumentNullException(nameof(sendCommand));
        }

        /// <summary>
        /// ビューを更新して全パネルに通知する。メインルーチンからのみ呼ぶ。
        /// </summary>
        public void Notify(IProjectView view, ChangeKind kind)
        {
            CurrentView = view;
            OnViewChanged?.Invoke(view, kind);
        }

        // ================================================================
        // 後方互換（段階移行用。移行完了後に削除可）
        // ================================================================

        /// <summary>旧API互換。ProjectSummaryを渡す場合。</summary>
        [Obsolete("Use Notify(IProjectView, ChangeKind) instead")]
        public ProjectSummary CurrentSummary => CurrentView as ProjectSummary;

        /// <summary>旧API互換。Summary変更通知。</summary>
        [Obsolete("Use OnViewChanged instead")]
        public event Action<ProjectSummary> OnSummaryChanged;

        /// <summary>旧API互換。</summary>
        [Obsolete("Use Notify(IProjectView, ChangeKind) instead")]
        public void Notify(ProjectSummary summary)
        {
            CurrentView = summary;
            OnSummaryChanged?.Invoke(summary);
            OnViewChanged?.Invoke(summary, ChangeKind.ListStructure);
        }
    }
}
