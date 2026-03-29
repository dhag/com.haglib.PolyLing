using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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

    // ============================================================
    // TreeViewDragDropHelper
    // ============================================================

    /// <summary>
    /// TreeView用の汎用ドラッグ&amp;ドロップヘルパー。
    /// 
    /// 【使い方】
    /// 1. データクラスに ITreeItem&lt;T&gt; を実装
    /// 2. ルート管理クラスに ITreeRoot&lt;T&gt; を実装
    /// 3. ヘルパーを作成してSetup()を呼ぶ
    /// 
    /// var helper = new TreeViewDragDropHelper&lt;MyNode&gt;(treeView, treeRoot);
    /// helper.Setup();
    /// 
    /// // 破棄時（OnDisable等）
    /// helper.Cleanup();
    /// </summary>
    public class TreeViewDragDropHelper<T> where T : class, ITreeItem<T>
    {
        // --- 依存オブジェクト ---
        private readonly TreeView treeView;
        private readonly ITreeRoot<T> treeRoot;
        private readonly IDragDropValidator<T> validator;  // null可

        // --- ドラッグ状態 ---
        private List<T> draggedItems = new();
        private bool isDragging;
        private Vector2 dragStartPos;

        // --- ビジュアル要素 ---
        private VisualElement dragIndicator;  // ドロップ位置を示す線/ハイライト
        private Label dragLabel;              // ドラッグ中のアイテム名表示

        // --- 設定値 ---
        private const float DragThreshold = 5f;     // ドラッグ開始判定の移動距離
        private const float DropZoneRatio = 0.25f;  // Before/After判定領域（上下25%）

        // ============================================================
        // コンストラクタ
        // ============================================================

        /// <summary>
        /// ヘルパーを作成
        /// </summary>
        /// <param name="treeView">対象のTreeView</param>
        /// <param name="treeRoot">ルートアイテムを提供するオブジェクト</param>
        /// <param name="validator">D&D可否判定（nullなら全て許可）</param>
        public TreeViewDragDropHelper(TreeView treeView, ITreeRoot<T> treeRoot, IDragDropValidator<T> validator = null)
        {
            this.treeView = treeView ?? throw new ArgumentNullException(nameof(treeView));
            this.treeRoot = treeRoot ?? throw new ArgumentNullException(nameof(treeRoot));
            this.validator = validator;
        }

        // ============================================================
        // 公開メソッド
        // ============================================================

        /// <summary>
        /// D&D機能を有効化。CreateGUI等で呼ぶ。
        /// </summary>
        public void Setup()
        {
            CreateVisualElements();
            RegisterPointerEvents();
        }

        /// <summary>
        /// D&D機能を無効化。OnDisable/OnDestroy等で呼ぶ。
        /// </summary>
        public void Cleanup()
        {
            UnregisterPointerEvents();
            RemoveVisualElements();
        }

        // ============================================================
        // ビジュアル要素の作成/削除
        // ============================================================

        private void CreateVisualElements()
        {
            // ドロップ位置インジケータ（青い線またはハイライト）
            dragIndicator = new VisualElement
            {
                name = "tree-drag-indicator",
                pickingMode = PickingMode.Ignore  // マウスイベントを透過
            };
            dragIndicator.style.position = Position.Absolute;
            dragIndicator.style.display = DisplayStyle.None;

            // ドラッグ中のラベル（カーソル追従）
            dragLabel = new Label
            {
                name = "tree-drag-label",
                pickingMode = PickingMode.Ignore
            };
            dragLabel.style.position = Position.Absolute;
            dragLabel.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
            dragLabel.style.color = Color.white;
            dragLabel.style.paddingLeft = dragLabel.style.paddingRight = 8;
            dragLabel.style.paddingTop = dragLabel.style.paddingBottom = 4;
            dragLabel.style.borderTopLeftRadius = dragLabel.style.borderTopRightRadius = 4;
            dragLabel.style.borderBottomLeftRadius = dragLabel.style.borderBottomRightRadius = 4;
            dragLabel.style.display = DisplayStyle.None;

            // TreeViewの親要素に追加（TreeView自体だと範囲外で見えなくなる）
            var container = treeView.parent ?? treeView;
            container.Add(dragIndicator);
            container.Add(dragLabel);
        }

        private void RemoveVisualElements()
        {
            dragIndicator?.RemoveFromHierarchy();
            dragLabel?.RemoveFromHierarchy();
            dragIndicator = null;
            dragLabel = null;
        }

        // ============================================================
        // イベント登録/解除
        // ============================================================

        private void RegisterPointerEvents()
        {
            treeView.RegisterCallback<PointerDownEvent>(OnPointerDown);
            treeView.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            treeView.RegisterCallback<PointerUpEvent>(OnPointerUp);
            treeView.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
        }

        private void UnregisterPointerEvents()
        {
            treeView.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            treeView.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            treeView.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            treeView.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
        }

        // ============================================================
        // ポインターイベントハンドラ
        // ============================================================

        /// <summary>マウスダウン：ドラッグ準備</summary>
        private void OnPointerDown(PointerDownEvent evt)
        {
            // 左クリックのみ
            if (evt.button != 0) return;

            // 選択アイテムを取得
            draggedItems = GetSelectedItems();
            if (draggedItems.Count == 0) return;

            // ドラッグ可否チェック
            if (validator != null && !draggedItems.TrueForAll(validator.CanDrag))
            {
                draggedItems.Clear();
                return;
            }

            // ドラッグ開始位置を記録（まだドラッグ中ではない）
            dragStartPos = evt.position;
            isDragging = false;
        }

        /// <summary>マウス移動：ドラッグ中の処理</summary>
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (draggedItems.Count == 0) return;

            // 一定距離動いたらドラッグ開始
            if (!isDragging)
            {
                float distance = ((Vector2)evt.position - dragStartPos).magnitude;
                if (distance > DragThreshold)
                {
                    StartDrag(evt.pointerId);
                }
            }

            if (!isDragging) return;

            // ドラッグラベルをカーソルに追従
            UpdateDragLabelPosition(evt.position);

            // ドロップ先インジケータを更新
            UpdateDropIndicator(evt.position);
        }

        /// <summary>マウスアップ：ドロップ実行</summary>
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (isDragging)
            {
                treeView.ReleasePointer(evt.pointerId);
                ExecuteDrop(evt.position);
            }
            EndDrag();
        }

        /// <summary>マウスがTreeView外に出た：ドラッグキャンセル</summary>
        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (isDragging)
            {
                treeView.ReleasePointer(evt.pointerId);
            }
            EndDrag();
        }

        // ============================================================
        // ドラッグ状態管理
        // ============================================================

        private void StartDrag(int pointerId)
        {
            isDragging = true;
            treeView.CapturePointer(pointerId);

            // ドラッグラベルを表示
            if (dragLabel != null)
            {
                dragLabel.text = draggedItems.Count == 1
                    ? draggedItems[0].DisplayName
                    : $"{draggedItems.Count}個のアイテム";
                dragLabel.style.display = DisplayStyle.Flex;
            }
        }

        private void EndDrag()
        {
            draggedItems.Clear();
            isDragging = false;
            if (dragIndicator != null) dragIndicator.style.display = DisplayStyle.None;
            if (dragLabel != null) dragLabel.style.display = DisplayStyle.None;
        }

        private void UpdateDragLabelPosition(Vector2 screenPos)
        {
            if (dragLabel == null || treeView?.parent == null) return;
            var localPos = treeView.parent.WorldToLocal(screenPos);
            dragLabel.style.left = localPos.x + 15;
            dragLabel.style.top = localPos.y + 15;
        }

        // ============================================================
        // ドロップ先の判定とインジケータ表示
        // ============================================================

        private void UpdateDropIndicator(Vector2 screenPos)
        {
            var (target, dropPos, element) = HitTestDropTarget(screenPos);

            if (target == null || !IsDropAllowed(target, dropPos))
            {
                if (dragIndicator != null) dragIndicator.style.display = DisplayStyle.None;
                return;
            }

            if (dragIndicator == null) return;
            dragIndicator.style.display = DisplayStyle.Flex;
            PositionIndicator(element, dropPos);
        }

        /// <summary>
        /// マウス位置からドロップ先を判定
        /// </summary>
        private (T item, DropPosition pos, VisualElement elem) HitTestDropTarget(Vector2 screenPos)
        {
            var container = treeView.Q("unity-content-container");
            if (container == null) return (null, DropPosition.Inside, null);

            int index = 0;
            foreach (var rowElement in container.Children())
            {
                if (!rowElement.worldBound.Contains(screenPos))
                {
                    index++;
                    continue;
                }

                var item = treeView.GetItemDataForIndex<T>(index);
                if (item == null) return (null, DropPosition.Inside, null);

                // 行内の相対Y位置でドロップ位置を決定
                float relativeY = (screenPos.y - rowElement.worldBound.y) / rowElement.worldBound.height;
                DropPosition pos;
                if (relativeY < DropZoneRatio)
                    pos = DropPosition.Before;      // 上部25%
                else if (relativeY > 1 - DropZoneRatio)
                    pos = DropPosition.After;       // 下部25%
                else
                    pos = DropPosition.Inside;      // 中央50%

                return (item, pos, rowElement);
            }

            return (null, DropPosition.Inside, null);
        }

        /// <summary>
        /// インジケータの位置とスタイルを設定
        /// </summary>
        private void PositionIndicator(VisualElement rowElement, DropPosition dropPos)
        {
            if (dragIndicator == null || rowElement == null || treeView?.parent == null) return;
            var rowRect = rowElement.worldBound;
            var parentRect = treeView.parent.worldBound;

            float left = rowRect.x - parentRect.x;
            float width = rowRect.width;

            switch (dropPos)
            {
                case DropPosition.Before:
                    dragIndicator.style.top = rowRect.y - parentRect.y;
                    dragIndicator.style.left = left;
                    dragIndicator.style.width = width;
                    dragIndicator.style.height = 2;
                    dragIndicator.style.backgroundColor = new Color(0.2f, 0.6f, 1f, 0.8f);
                    break;

                case DropPosition.After:
                    dragIndicator.style.top = rowRect.y - parentRect.y + rowRect.height;
                    dragIndicator.style.left = left;
                    dragIndicator.style.width = width;
                    dragIndicator.style.height = 2;
                    dragIndicator.style.backgroundColor = new Color(0.2f, 0.6f, 1f, 0.8f);
                    break;

                case DropPosition.Inside:
                    dragIndicator.style.top = rowRect.y - parentRect.y;
                    dragIndicator.style.left = left;
                    dragIndicator.style.width = width;
                    dragIndicator.style.height = rowRect.height;
                    dragIndicator.style.backgroundColor = new Color(0.2f, 0.6f, 1f, 0.2f);
                    break;
            }
        }

        /// <summary>
        /// ドロップが許可されているか判定
        /// </summary>
        private bool IsDropAllowed(T target, DropPosition dropPos)
        {
            foreach (var dragged in draggedItems)
            {
                if (dragged.Equals(target)) return false;
                if (IsDescendantOf(dragged, target)) return false;
                if (validator != null && !validator.CanDrop(dragged, target, dropPos))
                    return false;
            }
            return true;
        }

        private bool IsDescendantOf(T ancestor, T target)
        {
            foreach (var child in ancestor.Children)
            {
                if (child.Equals(target)) return true;
                if (IsDescendantOf(child, target)) return true;
            }
            return false;
        }

        // ============================================================
        // ドロップ実行
        // ============================================================

        private void ExecuteDrop(Vector2 screenPos)
        {
            var (target, dropPos, _) = HitTestDropTarget(screenPos);

            if (target == null || !IsDropAllowed(target, dropPos))
                return;

            // 1. ドラッグ元から削除
            foreach (var item in draggedItems)
            {
                TreeViewHelper.GetSiblings(item, treeRoot.RootItems).Remove(item);
            }

            // 2. ドロップ先に挿入
            var targetSiblings = TreeViewHelper.GetSiblings(target, treeRoot.RootItems);
            int targetIndex = targetSiblings.IndexOf(target);

            foreach (var item in draggedItems)
            {
                switch (dropPos)
                {
                    case DropPosition.Before:
                        targetSiblings.Insert(targetIndex++, item);
                        item.Parent = target.Parent;
                        break;

                    case DropPosition.After:
                        targetSiblings.Insert(++targetIndex, item);
                        item.Parent = target.Parent;
                        break;

                    case DropPosition.Inside:
                        target.Children.Add(item);
                        item.Parent = target;
                        break;
                }
            }

            // 3. 変更を通知
            treeRoot.OnTreeChanged();
        }

        // ============================================================
        // ユーティリティ
        // ============================================================

        private List<T> GetSelectedItems()
        {
            var result = new List<T>();
            foreach (var index in treeView.selectedIndices)
            {
                var item = treeView.GetItemDataForIndex<T>(index);
                if (item != null) result.Add(item);
            }
            return result;
        }
    }

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
