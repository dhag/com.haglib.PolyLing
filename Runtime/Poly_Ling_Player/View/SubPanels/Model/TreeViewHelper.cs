// TreeViewHelper.cs
// ITreeItem<T> のツリーに対する静的ユーティリティ。
// TreeViewDragDropHelper から分離。単体でも使える。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace UIList.UIToolkitExtensions
{
    // ============================================================
    // TreeViewHelper（静的ユーティリティ）
    // ============================================================

    /// <summary>
    /// TreeView操作の汎用ヘルパーメソッド集。
    /// ITreeItem&lt;T&gt;を実装したデータに対して使える。
    /// 
    /// 【使用例】
    /// // TreeViewデータ構築
    /// var treeData = TreeViewHelper.BuildTreeData(model.rootObjects);
    /// treeView.SetRootItems(treeData);
    /// 
    /// // アイテム移動
    /// TreeViewHelper.MoveItems(selectedItems, rootList, direction: -1);  // 上へ
    /// TreeViewHelper.Outdent(item, rootList);  // 階層を上げる
    /// TreeViewHelper.Indent(item, rootList);   // 階層を下げる
    /// </summary>
    public static class TreeViewHelper
    {
        // ============================================================
        // データ構築
        // ============================================================

        /// <summary>
        /// TreeViewItemDataのリストを構築（再帰）。
        /// treeView.SetRootItems() に渡す用。
        /// </summary>
        public static List<TreeViewItemData<T>> BuildTreeData<T>(List<T> items) where T : class, ITreeItem<T>
        {
            var result = new List<TreeViewItemData<T>>();
            if (items == null) return result;

            foreach (var item in items)
            {
                var children = BuildTreeData(item.Children);
                result.Add(new TreeViewItemData<T>(item.Id, item, children));
            }
            return result;
        }

        /// <summary>
        /// 親参照を再構築。
        /// CSVロード後など、Parentが設定されていない時に呼ぶ。
        /// </summary>
        public static void RebuildParentReferences<T>(List<T> rootItems) where T : class, ITreeItem<T>
        {
            if (rootItems == null) return;
            foreach (var root in rootItems)
            {
                RebuildParentReferencesRecursive(root, null);
            }
        }

        private static void RebuildParentReferencesRecursive<T>(T item, T parent) where T : class, ITreeItem<T>
        {
            item.Parent = parent;
            if (item.Children == null) return;
            foreach (var child in item.Children)
            {
                RebuildParentReferencesRecursive(child, item);
            }
        }

        // ============================================================
        // 構造操作
        // ============================================================

        /// <summary>
        /// アイテムの兄弟リストを取得
        /// </summary>
        public static List<T> GetSiblings<T>(T item, List<T> rootItems) where T : class, ITreeItem<T>
        {
            return item.Parent != null ? item.Parent.Children : rootItems;
        }

        /// <summary>
        /// アイテムを上下に移動
        /// </summary>
        /// <param name="items">移動するアイテム（同じ親を持つこと）</param>
        /// <param name="rootItems">ルートリスト</param>
        /// <param name="direction">負:上、正:下</param>
        /// <returns>成功したらtrue</returns>
        public static bool MoveItems<T>(List<T> items, List<T> rootItems, int direction) where T : class, ITreeItem<T>
        {
            if (items == null || items.Count == 0) return false;

            // 同じ親かチェック
            var firstParent = items[0].Parent;
            if (!items.All(i => Equals(i.Parent, firstParent))) return false;

            var siblings = GetSiblings(items[0], rootItems);

            // インデックス順にソート
            var sorted = items.OrderBy(i => siblings.IndexOf(i)).ToList();

            if (direction < 0)
            {
                // 上へ移動：先頭が0番目なら移動不可
                int firstIndex = siblings.IndexOf(sorted[0]);
                if (firstIndex <= 0) return false;

                foreach (var item in sorted)
                {
                    int idx = siblings.IndexOf(item);
                    (siblings[idx], siblings[idx - 1]) = (siblings[idx - 1], siblings[idx]);
                }
            }
            else
            {
                // 下へ移動：末尾が最後なら移動不可
                int lastIndex = siblings.IndexOf(sorted.Last());
                if (lastIndex >= siblings.Count - 1) return false;

                // 後ろから処理しないと位置がずれる
                for (int i = sorted.Count - 1; i >= 0; i--)
                {
                    int idx = siblings.IndexOf(sorted[i]);
                    (siblings[idx], siblings[idx + 1]) = (siblings[idx + 1], siblings[idx]);
                }
            }

            return true;
        }

        /// <summary>
        /// 階層を上げる（親の兄弟になる）
        /// </summary>
        /// <returns>成功したらtrue</returns>
        public static bool Outdent<T>(T item, List<T> rootItems) where T : class, ITreeItem<T>
        {
            // ルートアイテムはこれ以上上げられない
            if (item == null || item.Parent == null) return false;

            var oldParent = item.Parent;
            var grandParent = oldParent.Parent;

            // 元の親から削除
            oldParent.Children.Remove(item);

            // 挿入先を決定（祖父の子 or ルート）
            var targetList = grandParent != null ? grandParent.Children : rootItems;
            int insertIndex = targetList.IndexOf(oldParent) + 1;

            targetList.Insert(insertIndex, item);
            item.Parent = grandParent;

            return true;
        }

        /// <summary>
        /// 階層を下げる（直上の兄の子になる）
        /// </summary>
        /// <returns>成功したらtrue</returns>
        public static bool Indent<T>(T item, List<T> rootItems) where T : class, ITreeItem<T>
        {
            if (item == null) return false;

            var siblings = GetSiblings(item, rootItems);
            int index = siblings.IndexOf(item);

            // 上に兄弟がいなければ不可
            if (index <= 0) return false;

            // 直上の兄を新しい親にする
            var newParent = siblings[index - 1];
            siblings.Remove(item);
            newParent.Children.Add(item);
            item.Parent = newParent;

            return true;
        }

        // ============================================================
        // CRUD操作
        // ============================================================

        /// <summary>
        /// アイテムを削除
        /// </summary>
        public static bool Delete<T>(T item, List<T> rootItems) where T : class, ITreeItem<T>
        {
            if (item == null) return false;
            return GetSiblings(item, rootItems).Remove(item);
        }

        /// <summary>
        /// 複数アイテムを削除
        /// </summary>
        /// <returns>削除した件数</returns>
        public static int DeleteMany<T>(List<T> items, List<T> rootItems) where T : class, ITreeItem<T>
        {
            if (items == null) return 0;
            int count = 0;
            foreach (var item in items)
            {
                if (Delete(item, rootItems)) count++;
            }
            return count;
        }

        // ============================================================
        // ID管理
        // ============================================================

        /// <summary>
        /// ツリー全体から最大IDを取得。
        /// 新規アイテム作成時に GetMaxId() + 1 で新IDを生成できる。
        /// </summary>
        public static int GetMaxId<T>(List<T> rootItems) where T : class, ITreeItem<T>
        {
            if (rootItems == null) return 0;
            int max = 0;
            foreach (var root in rootItems)
            {
                max = Math.Max(max, GetMaxIdRecursive(root));
            }
            return max;
        }

        private static int GetMaxIdRecursive<T>(T item) where T : class, ITreeItem<T>
        {
            int max = item.Id;
            if (item.Children != null)
            {
                foreach (var child in item.Children)
                {
                    max = Math.Max(max, GetMaxIdRecursive(child));
                }
            }
            return max;
        }

        // ============================================================
        // 検索
        // ============================================================

        /// <summary>
        /// IDでアイテムを検索
        /// </summary>
        public static T FindById<T>(List<T> rootItems, int id) where T : class, ITreeItem<T>
        {
            if (rootItems == null) return null;
            foreach (var root in rootItems)
            {
                var found = FindByIdRecursive(root, id);
                if (found != null) return found;
            }
            return null;
        }

        private static T FindByIdRecursive<T>(T item, int id) where T : class, ITreeItem<T>
        {
            if (item.Id == id) return item;
            if (item.Children == null) return null;
            foreach (var child in item.Children)
            {
                var found = FindByIdRecursive(child, id);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// 全アイテムをフラットなリストで取得
        /// </summary>
        public static List<T> Flatten<T>(List<T> rootItems) where T : class, ITreeItem<T>
        {
            var result = new List<T>();
            if (rootItems == null) return result;
            foreach (var root in rootItems)
            {
                FlattenRecursive(root, result);
            }
            return result;
        }

        private static void FlattenRecursive<T>(T item, List<T> result) where T : class, ITreeItem<T>
        {
            result.Add(item);
            if (item.Children == null) return;
            foreach (var child in item.Children)
            {
                FlattenRecursive(child, result);
            }
        }
    }
}
