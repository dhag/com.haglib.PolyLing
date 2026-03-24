// PlayerSelectionOps.cs
// プレイヤービルド用の選択共通ロジック。
// クリック選択・矩形選択の SelectionState 操作を担う。
// 各 IPlayerToolHandler から呼び出して再利用する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    /// <summary>
    /// プレイヤービルド用ヒットテスト結果。
    /// </summary>
    public struct PlayerHitResult
    {
        public bool HasHit;
        public int  MeshIndex;
        public int  VertexIndex;

        public static readonly PlayerHitResult Miss = new PlayerHitResult { HasHit = false, MeshIndex = -1, VertexIndex = -1 };
    }

    /// <summary>
    /// クリック選択・矩形選択の共通 SelectionState 操作。
    /// モード特有の処理は各 <see cref="IPlayerToolHandler"/> で実装し、
    /// 移動モード互換の選択が必要な場合はこのクラスのメソッドを呼ぶ。
    /// </summary>
    public class PlayerSelectionOps
    {
        // ================================================================
        // 依存
        // ================================================================

        private SelectionState _selectionState;

        /// <summary>
        /// 選択に変化があったとき呼ばれる。描画更新などに使う。
        /// </summary>
        public Action OnSelectionChanged;

        // ================================================================
        // 矩形選択内部状態
        // ================================================================

        public bool    IsBoxSelecting { get; private set; }
        public Vector2 BoxStart       { get; private set; }
        public Vector2 BoxEnd         { get; private set; }

        /// <summary>矩形選択の Rect（スクリーン座標、Y は下が大）。</summary>
        public Rect BoxRect => MakeRect(BoxStart, BoxEnd);

        // ================================================================
        // 初期化
        // ================================================================

        public PlayerSelectionOps(SelectionState selectionState)
        {
            _selectionState = selectionState ?? throw new ArgumentNullException(nameof(selectionState));
        }

        public void SetSelectionState(SelectionState selectionState)
        {
            _selectionState = selectionState ?? throw new ArgumentNullException(nameof(selectionState));
        }

        /// <summary>管理中の SelectionState への参照。ToolHandler が頂点リストを参照する際に使う。</summary>
        public SelectionState SelectionState => _selectionState;

        // ================================================================
        // クリック選択
        // ================================================================

        /// <summary>
        /// クリックによる頂点選択。移動モード互換の選択挙動。
        /// <list type="bullet">
        ///   <item>ヒット無し：Shift/Ctrl なし → 全解除。</item>
        ///   <item>ヒット有り・Shift なし・Ctrl なし → 単独選択。</item>
        ///   <item>ヒット有り・Shift → 追加選択。</item>
        ///   <item>ヒット有り・Ctrl → トグル選択。</item>
        /// </list>
        /// </summary>
        public void ApplyClick(PlayerHitResult hit, ModifierKeys mods)
        {
            if (!hit.HasHit)
            {
                if (!mods.Shift && !mods.Ctrl)
                    ClearAll();
                return;
            }

            int v = hit.VertexIndex;

            if (!mods.Shift && !mods.Ctrl)
            {
                // 単独選択
                _selectionState.Vertices.Clear();
                _selectionState.Vertices.Add(v);
            }
            else if (mods.Shift)
            {
                // 追加選択
                _selectionState.Vertices.Add(v);
            }
            else // Ctrl
            {
                // トグル
                if (_selectionState.Vertices.Contains(v))
                    _selectionState.Vertices.Remove(v);
                else
                    _selectionState.Vertices.Add(v);
            }

            OnSelectionChanged?.Invoke();
        }

        // ================================================================
        // 矩形選択
        // ================================================================

        /// <summary>矩形選択開始。</summary>
        public void BeginBoxSelect(Vector2 start)
        {
            IsBoxSelecting = true;
            BoxStart       = start;
            BoxEnd         = start;
        }

        /// <summary>矩形更新（ドラッグ中毎フレーム）。</summary>
        public void UpdateBoxSelect(Vector2 current)
        {
            BoxEnd = current;
        }

        /// <summary>
        /// 矩形選択確定。boxVertices は矩形内にある頂点インデックス列。
        /// <para>
        /// Shift → 追加選択。Ctrl → トグル選択。
        /// それ以外 → 既存選択を置き換え。
        /// </para>
        /// </summary>
        public void EndBoxSelect(IEnumerable<int> boxVertices, ModifierKeys mods)
        {
            IsBoxSelecting = false;

            if (!mods.Shift && !mods.Ctrl)
                _selectionState.Vertices.Clear();

            foreach (int v in boxVertices)
            {
                if (mods.Ctrl)
                {
                    if (_selectionState.Vertices.Contains(v))
                        _selectionState.Vertices.Remove(v);
                    else
                        _selectionState.Vertices.Add(v);
                }
                else
                {
                    _selectionState.Vertices.Add(v);
                }
            }

            OnSelectionChanged?.Invoke();
        }

        /// <summary>矩形選択をキャンセル（ドラッグ中断など）。</summary>
        public void CancelBoxSelect()
        {
            IsBoxSelecting = false;
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        public void ClearAll()
        {
            if (_selectionState.Vertices.Count == 0) return;
            _selectionState.Vertices.Clear();
            OnSelectionChanged?.Invoke();
        }

        private static Rect MakeRect(Vector2 a, Vector2 b)
        {
            return new Rect(
                Mathf.Min(a.x, b.x),
                Mathf.Min(a.y, b.y),
                Mathf.Abs(a.x - b.x),
                Mathf.Abs(a.y - b.y));
        }
    }
}
