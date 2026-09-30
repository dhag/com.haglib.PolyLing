// UndoScope.cs
// 履歴の範囲：ある時点から後の記録をまとめて扱う仕組み。
// Runtime/UndoSystem/Core/ に配置
//
// 【用途】
//   作業空間（PolyLing_UV_Billboard_Design.md 9.1）のように、一時オブジェクトを足して編集し、
//   終わったら消す操作で使う。範囲の中の記録は一時オブジェクトを参照するので、終了後に
//   通常の履歴へ残してはいけない。
//
// 【仕組み】
//   UndoGroup.BeginScope が配下の全スタックの「今までに振ったグループ ID の最大値」を控える。
//   グループ ID はスタックごとに単調増加で振られる（UndoStack.Record）ので、控えより大きい
//   番号の記録は範囲の中で積まれたもの。
//   範囲を開いている間、そのグループは床（既定は範囲の開始。RaiseScopeFloor で上げられる）より
//   前の記録を Undo せず、床より前から残っている Redo もやり直さない。
//   UndoGroup.EndScope(discard: true) は範囲の中の記録とログ項目を除く。
//   スタックのサイズ上限で古い記録が落ちても、番号で判定するので範囲の境目はずれない。

using System.Collections.Generic;

namespace Poly_Ling.UndoSystem
{
    /// <summary>範囲の控え。UndoGroup.BeginScope が返し、EndScope へ渡す。</summary>
    public sealed class UndoScope
    {
        /// <summary>スタック ID → 範囲開始時点のグループ ID の最大値。</summary>
        internal readonly Dictionary<string, int> Marks = new Dictionary<string, int>();

        /// <summary>
        /// Undo の床。範囲を開いている間、これより前の記録は戻さない。
        /// 既定は範囲の開始（Marks と同じ）。範囲を開いた直後の準備（一時オブジェクトの追加など）を
        /// 戻させないときは UndoGroup.RaiseScopeFloor で今の位置へ上げる。
        /// </summary>
        internal Dictionary<string, int> Floor;

        /// <summary>ログ項目が床より後に積まれたものか。控えに無いスタックは範囲の途中で足されたもの。</summary>
        internal bool AboveFloor(OperationLogEntry e)
        {
            var floor = Floor ?? Marks;
            return !floor.TryGetValue(e.StackId, out int mark) || e.GroupId > mark;
        }
    }

    /// <summary>履歴の範囲に参加するノード（UndoStack・UndoGroup）。</summary>
    public interface IUndoScopeNode
    {
        /// <summary>範囲開始時点の番号を控える。</summary>
        void CaptureScopeMarks(Dictionary<string, int> marks);

        /// <summary>控えた番号より後の記録を除く。</summary>
        void DiscardAfterScopeMarks(IReadOnlyDictionary<string, int> marks);
    }
}
