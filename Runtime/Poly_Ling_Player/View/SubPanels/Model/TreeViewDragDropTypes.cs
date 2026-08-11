// TreeViewDragDropTypes.cs
// TreeView 用ドラッグ&ドロップの共通型。
// 転用時はこのファイルと TreeViewDragDropHelper.cs / TreeViewHelper.cs の3つを持っていけば動く。
// PolyLing 固有の型は一切参照していない。

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace UIList.UIToolkitExtensions
{
    // ============================================================
    // インターフェース定義
    // ============================================================

    /// <summary>
    /// TreeViewで表示・D&D可能なアイテムのインターフェース。
    /// 自分のデータクラスにこれを実装すればTreeViewDragDropHelperが使える。
    /// 
    /// 【実装例】
    /// public class MyNode : ITreeItem&lt;MyNode&gt;
    /// {
    ///     public int Id =&gt; id;
    ///     public string DisplayName =&gt; name;
    ///     public MyNode Parent { get; set; }
    ///     public List&lt;MyNode&gt; Children =&gt; children;
    /// }
    /// </summary>
    public interface ITreeItem<T> where T : class, ITreeItem<T>
    {
        /// <summary>一意なID（TreeViewのitemIdに使用）</summary>
        int Id { get; }

        /// <summary>表示名（ドラッグラベル等に使用）</summary>
        string DisplayName { get; }

        /// <summary>親アイテム（ルートならnull）</summary>
        T Parent { get; set; }

        /// <summary>子アイテムのリスト</summary>
        List<T> Children { get; }
    }

    /// <summary>
    /// ツリーのルートを提供するインターフェース。
    /// D&D完了時の通知も受け取る。
    /// 
    /// 【実装例】
    /// public class MyTreeRoot : ITreeRoot&lt;MyNode&gt;
    /// {
    ///     public List&lt;MyNode&gt; RootItems =&gt; rootNodes;
    ///     public void OnTreeChanged() { Save(); RefreshUI(); }
    /// }
    /// </summary>
    public interface ITreeRoot<T> where T : class, ITreeItem<T>
    {
        /// <summary>ルートレベルのアイテムリスト</summary>
        List<T> RootItems { get; }

        /// <summary>ツリー構造が変更された時に呼ばれる</summary>
        void OnTreeChanged();
    }

    /// <summary>
    /// D&D可否を判定するインターフェース（オプション）。
    /// nullなら全てのD&Dが許可される。
    /// </summary>
    public interface IDragDropValidator<T> where T : class, ITreeItem<T>
    {
        /// <summary>アイテムをドラッグ開始できるか</summary>
        bool CanDrag(T item);

        /// <summary>指定位置にドロップできるか</summary>
        bool CanDrop(T dragged, T target, DropPosition position);
    }

    /// <summary>ドロップ位置</summary>
    public enum DropPosition
    {
        Before,  // ターゲットの前（兄として挿入）
        After,   // ターゲットの後（弟として挿入）
        Inside   // ターゲットの子として追加
    }

    /// <summary>
    /// 「TreeView の行要素」から「その行が表示しているアイテム」と
    /// 「インデント済みの内容要素」を引くための解決器。
    ///
    /// 仮想化リストでは、コンテナの子の並び順は絶対 index と一致しない。
    /// index を数えて GetItemDataForIndex に渡すとドロップ先がずれるため、
    /// bindItem 時に行へ書き込んだ値を直接読む手段を呼び出し側が用意する。
    ///
    /// 【実装例】
    /// // makeItem が作る要素に名前を付け、bindItem で自分のキャッシュにアイテムを控える
    /// public MyNode ResolveItem(VisualElement row)
    ///     =&gt; (row.Q&lt;VisualElement&gt;("my-row-content")?.userData as MyCache)?.Node;
    /// public VisualElement ResolveContent(VisualElement row)
    ///     =&gt; row.Q&lt;VisualElement&gt;("my-row-content");
    /// </summary>
    public interface ITreeRowResolver<T> where T : class, ITreeItem<T>
    {
        /// <summary>行要素が今表示しているアイテム。取れなければ null。</summary>
        T ResolveItem(VisualElement rowElement);

        /// <summary>
        /// 行要素の中の、インデントが効いた内容要素。取れなければ null。
        /// ドロップ位置の線を「入る深さ」に合わせるために使う。
        /// </summary>
        VisualElement ResolveContent(VisualElement rowElement);
    }
}
