// TreeViewDragDropHelper.cs
// TreeView 用の汎用ドラッグ&ドロップ。Unity のヒエラルキーと同じ操作感。
//
// ── 転用手順（この3ファイルをコピーする: Types / Helper / TreeViewHelper）
//  1. データクラスに ITreeItem<T> を実装する（Id / DisplayName / Parent / Children）
//  2. ルート管理クラスに ITreeRoot<T> を実装する（RootItems / OnTreeChanged）
//  3. makeItem が作る要素に名前を付ける
//         var content = new VisualElement { name = "my-row-content" };
//     bindItem でその要素の userData に「今表示しているアイテム」を控える
//  4. ITreeRowResolver<T> を実装し、行要素からその要素とアイテムを引けるようにする
//  5. new TreeViewDragDropHelper<T>(treeView, treeRoot, validator, resolver).Setup();
//     破棄時は Cleanup();
//
// ── 落ちる位置の規則（Unity のヒエラルキー準拠）
//   行の中央50%   : その行の子として末尾に追加（畳まれていれば開く）
//   行の上下25%   : 行と行の境目。線のすぐ下に来る行の直前に、その行と同じ深さで挿入
//   最終行より下  : ルートの末尾
//
// ── 依存
//   UnityEngine / UnityEngine.UIElements のみ。アプリ固有の型は参照しない。
//   ログが要る場合は DiagLine に差し込む。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace UIList.UIToolkitExtensions
{
    // ============================================================
    // TreeViewDragDropHelper
    // ============================================================

    /// <summary>
    /// TreeView用の汎用ドラッグ&amp;ドロップヘルパー。
    /// 導入手順と落下位置の規則はファイル先頭のコメントを参照。
    /// </summary>
    public class TreeViewDragDropHelper<T> where T : class, ITreeItem<T>
    {
        // --- 依存オブジェクト ---
        private readonly TreeView treeView;
        private readonly ITreeRoot<T> treeRoot;
        private readonly IDragDropValidator<T> validator;  // null可

        // 行要素 → アイテム / 内容要素 の解決器（null可）。
        // 仮想化リストではコンテナの子の並び順が絶対 index と一致しないため、
        // index を数えて GetItemDataForIndex に渡すとドロップ先がずれる。
        private readonly ITreeRowResolver<T> rowResolver;  // null可

        /// <summary>
        /// 診断ログの差し込み口（null可）。アプリ側のロガーへ流す用。
        /// ここに依存を持たせないため、ヘルパー自身は何も出力しない。
        /// </summary>
        public Action<string> DiagLine;

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
        public TreeViewDragDropHelper(TreeView treeView, ITreeRoot<T> treeRoot,
                                      IDragDropValidator<T> validator = null,
                                      ITreeRowResolver<T> rowResolver = null)
        {
            this.treeView = treeView ?? throw new ArgumentNullException(nameof(treeView));
            this.treeRoot = treeRoot ?? throw new ArgumentNullException(nameof(treeRoot));
            this.validator = validator;
            this.rowResolver = rowResolver;
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

            // 他のドラッグ対象の子孫を先に落とす。親を動かせば子は Children ごと
            // 付いてくるため、両方を個別に動かすと子が親から切り離される。
            // 判定と実行で同じ集合を使うため、ここで1回だけ行う。
            RemoveDraggedDescendants();

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

        /// <summary>
        /// ドロップ先。Unity のヒエラルキーと同じ3種。
        ///   Inside  : Reference の子として末尾に追加
        ///   Boundary: Reference の直前に、Reference と同じ深さで挿入
        ///   RootEnd : ルートの末尾に追加
        /// </summary>
        private struct DropTarget
        {
            public bool Valid;
            public bool Inside;
            public bool RootEnd;
            public T    Reference;      // Inside: 親 / Boundary: 直後に来る行
            public VisualElement Row;   // 線を描く基準の行
            public bool LineAtRowBottom; // 線を Row の下端に描くか（false = 上端）
        }

        private void UpdateDropIndicator(Vector2 screenPos)
        {
            var dt = HitTestDropTarget(screenPos);

            if (!dt.Valid || !IsDropAllowed(dt))
            {
                if (dragIndicator != null) dragIndicator.style.display = DisplayStyle.None;
                return;
            }

            if (dragIndicator == null) return;
            dragIndicator.style.display = DisplayStyle.Flex;
            PositionIndicator(dt);
        }

        /// <summary>
        /// 展開状態を加味した可視順リスト。畳まれた子はたどらない。
        /// </summary>
        private List<T> BuildVisibleList()
        {
            var result = new List<T>();
            AppendVisible(treeRoot.RootItems, result);
            return result;
        }

        private void AppendVisible(List<T> items, List<T> result)
        {
            if (items == null) return;
            foreach (var it in items)
            {
                if (it == null) continue;
                result.Add(it);
                if (it.Children != null && it.Children.Count > 0 && treeView.IsExpanded(it.Id))
                    AppendVisible(it.Children, result);
            }
        }

        /// <summary>
        /// マウス位置からドロップ先を判定する。
        /// 行の中央50%は Inside、上下25%は「行と行の境目」として扱う。
        /// 境目は「線のすぐ下に来る行の直前」に正規化するので、
        /// 展開中の親のすぐ下の境目は第一子の直前になる。
        /// </summary>
        private DropTarget HitTestDropTarget(Vector2 screenPos)
        {
            var none = new DropTarget { Valid = false };

            var container = treeView.Q("unity-content-container");
            if (container == null) return none;

            VisualElement hoveredRow = null;
            T hoveredItem = null;
            int index = 0;
            foreach (var rowElement in container.Children())
            {
                if (rowElement.worldBound.Contains(screenPos))
                {
                    // コンテナの子の並び順は絶対 index と一致しない（仮想化・要素の使い回し）。
                    hoveredItem = rowResolver != null
                        ? rowResolver.ResolveItem(rowElement)
                        : treeView.GetItemDataForIndex<T>(index);
                    hoveredRow = rowElement;
                    break;
                }
                index++;
            }

            // どの行にも当たらない = 最終行より下の空白。ルート末尾。
            if (hoveredRow == null || hoveredItem == null)
                return new DropTarget { Valid = true, RootEnd = true, Row = LastVisibleRow(container), LineAtRowBottom = true };

            var rect = hoveredRow.worldBound;
            float relativeY = rect.height > 0f ? (screenPos.y - rect.y) / rect.height : 0.5f;

            // 中央50%: 子として追加
            if (relativeY >= DropZoneRatio && relativeY <= 1f - DropZoneRatio)
                return new DropTarget { Valid = true, Inside = true, Reference = hoveredItem, Row = hoveredRow };

            var visible = BuildVisibleList();
            int at = IndexOfRef(visible, hoveredItem);
            if (at < 0)
                return new DropTarget { Valid = true, Inside = true, Reference = hoveredItem, Row = hoveredRow };

            // 上25% = この行の直前 / 下25% = 次の可視行の直前
            int refIndex = relativeY < DropZoneRatio ? at : at + 1;

            // ドラッグ対象そのもの（とその子孫）は挿入基準にできないので次へ送る。
            while (refIndex < visible.Count && IsDraggedOrDescendant(visible[refIndex]))
                refIndex++;

            if (refIndex >= visible.Count)
                return new DropTarget { Valid = true, RootEnd = true, Row = LastVisibleRow(container), LineAtRowBottom = true };

            var reference = visible[refIndex];
            var refRow = FindRow(container, reference);
            if (refRow != null)
                return new DropTarget { Valid = true, Reference = reference, Row = refRow, LineAtRowBottom = false };

            // 基準行が画面外（仮想化で未生成）のときは、幾何的に同じ位置になる
            // 「ホバー行の下端」に描く。
            return new DropTarget { Valid = true, Reference = reference, Row = hoveredRow, LineAtRowBottom = true };
        }

        private static int IndexOfRef(List<T> list, T item)
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], item)) return i;
            return -1;
        }

        private VisualElement FindRow(VisualElement container, T item)
        {
            if (rowResolver == null) return null;
            foreach (var row in container.Children())
                if (ReferenceEquals(rowResolver.ResolveItem(row), item)) return row;
            return null;
        }

        private VisualElement LastVisibleRow(VisualElement container)
        {
            VisualElement last = null;
            foreach (var row in container.Children())
                if (last == null || row.worldBound.y > last.worldBound.y) last = row;
            return last;
        }

        /// <summary>行の内容要素の左端（ワールド）。取れなければ行の左端。</summary>
        private float ContentLeftOf(VisualElement row)
        {
            if (row == null) return 0f;
            var content = rowResolver?.ResolveContent(row);
            return content != null ? content.worldBound.x : row.worldBound.x;
        }

        /// <summary>
        /// インジケータの位置とスタイルを設定する。
        /// 境目のときは「入る深さ」に合わせて線の左端をずらす。
        /// </summary>
        private void PositionIndicator(DropTarget dt)
        {
            if (dragIndicator == null || dt.Row == null || treeView?.parent == null) return;
            var rowRect = dt.Row.worldBound;
            var parentRect = treeView.parent.worldBound;

            if (dt.Inside)
            {
                dragIndicator.style.top    = rowRect.y - parentRect.y;
                dragIndicator.style.left   = rowRect.x - parentRect.x;
                dragIndicator.style.width  = rowRect.width;
                dragIndicator.style.height = rowRect.height;
                dragIndicator.style.backgroundColor = new Color(0.2f, 0.6f, 1f, 0.2f);
                return;
            }

            float y = dt.LineAtRowBottom ? rowRect.y + rowRect.height : rowRect.y;
            // RootEnd はルート直下なので行の左端。境目は基準行の内容左端に合わせる。
            float leftWorld = dt.RootEnd ? rowRect.x : ContentLeftOf(dt.Row);

            dragIndicator.style.top    = y - parentRect.y;
            dragIndicator.style.left   = leftWorld - parentRect.x;
            dragIndicator.style.width  = Mathf.Max(8f, rowRect.xMax - leftWorld);
            dragIndicator.style.height = 2;
            dragIndicator.style.backgroundColor = new Color(0.2f, 0.6f, 1f, 0.8f);
        }

        /// <summary>ドロップが許可されているか判定</summary>
        private bool IsDropAllowed(DropTarget dt)
        {
            if (!dt.Valid) return false;
            if (draggedItems.Count == 0) return false;
            if (dt.RootEnd) return true;
            if (dt.Reference == null) return false;

            // 自分自身や自分の子孫を基準にはできない。
            if (IsDraggedOrDescendant(dt.Reference)) return false;

            var pos = dt.Inside ? DropPosition.Inside : DropPosition.Before;
            foreach (var dragged in draggedItems)
                if (validator != null && !validator.CanDrop(dragged, dt.Reference, pos)) return false;

            return true;
        }

        /// <summary>item がドラッグ対象そのもの、またはその子孫か。</summary>
        private bool IsDraggedOrDescendant(T item)
        {
            if (item == null) return false;
            foreach (var dragged in draggedItems)
            {
                if (ReferenceEquals(dragged, item)) return true;
                if (IsDescendantOf(dragged, item)) return true;
            }
            return false;
        }

        private bool IsDescendantOf(T ancestor, T target)
        {
            if (ancestor.Children == null) return false;
            foreach (var child in ancestor.Children)
            {
                if (ReferenceEquals(child, target)) return true;
                if (IsDescendantOf(child, target)) return true;
            }
            return false;
        }

        // ============================================================
        // ドロップ実行
        // ============================================================

        private void ExecuteDrop(Vector2 screenPos)
        {
            var dt = HitTestDropTarget(screenPos);
            if (!dt.Valid || !IsDropAllowed(dt)) return;
            if (draggedItems.Count == 0) return;

            // 診断: 何を掴んで、どこへ落としたか。出力先はアプリ側。
            if (DiagLine != null)
            {
                DiagLine("mode = " + (dt.Inside ? "Inside(子として追加)"
                                    : dt.RootEnd ? "RootEnd(ルート末尾)"
                                                 : "Boundary(この行の直前)"));
                DiagLine("reference = " + (dt.Reference != null ? dt.Reference.DisplayName : "<root-end>"));
                DiagLine("dragged count = " + draggedItems.Count);
                for (int di = 0; di < draggedItems.Count; di++)
                {
                    var d = draggedItems[di];
                    DiagLine($"  [{di}] {d.DisplayName} (parent={(d.Parent != null ? d.Parent.DisplayName : "<root>")})");
                }
            }

            // 1. ドラッグ元から削除
            foreach (var item in draggedItems)
                TreeViewHelper.GetSiblings(item, treeRoot.RootItems).Remove(item);

            // 2. 挿入
            if (dt.Inside)
            {
                foreach (var item in draggedItems)
                {
                    dt.Reference.Children.Add(item);
                    item.Parent = dt.Reference;
                }
                // 畳まれたまま子を足すと消えたように見えるので開く。
                treeView.ExpandItem(dt.Reference.Id, false);
            }
            else if (dt.RootEnd)
            {
                foreach (var item in draggedItems)
                {
                    treeRoot.RootItems.Add(item);
                    item.Parent = null;
                }
            }
            else
            {
                var siblings = TreeViewHelper.GetSiblings(dt.Reference, treeRoot.RootItems);
                int at = siblings.IndexOf(dt.Reference);
                if (at < 0) at = siblings.Count;
                foreach (var item in draggedItems)
                {
                    siblings.Insert(at++, item);
                    item.Parent = dt.Reference.Parent;
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
            var picked = new HashSet<T>();
            foreach (var index in treeView.selectedIndices)
            {
                var item = treeView.GetItemDataForIndex<T>(index);
                if (item != null) picked.Add(item);
            }
            if (picked.Count == 0) return new List<T>();

            // selectedIndices は選択した順に返るため、そのまま挿入すると
            // クリック順で並ぶ。ツリーの表示順（深さ優先）に揃える。
            var result = new List<T>();
            foreach (var item in TreeViewHelper.Flatten(treeRoot.RootItems))
                if (picked.Contains(item)) result.Add(item);

            // ツリーに見つからなかったものは末尾へ回す（取りこぼし防止）。
            foreach (var item in picked)
                if (!result.Contains(item)) result.Add(item);

            return result;
        }

        /// <summary>
        /// draggedItems から「他の draggedItem の子孫」を取り除く。
        /// </summary>
        private void RemoveDraggedDescendants()
        {
            if (draggedItems.Count <= 1) return;
            var set = new HashSet<T>(draggedItems);
            var kept = new List<T>();
            foreach (var item in draggedItems)
            {
                bool hasSelectedAncestor = false;
                var p = item.Parent;
                while (p != null)
                {
                    if (set.Contains(p)) { hasSelectedAncestor = true; break; }
                    p = p.Parent;
                }
                if (!hasSelectedAncestor) kept.Add(item);
            }
            draggedItems = kept;
        }
    }
}
