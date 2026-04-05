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
    /// ホバー要素の種別。
    /// </summary>
    public enum PlayerHoverKind { None, Vertex, Edge, Line, Face }

    /// <summary>
    /// GPU ホバー結果から変換した要素情報。
    /// MeshIndex は UnifiedMeshIndex（adapter 内部インデックス）ではなく
    /// MeshContextList の contextIndex。
    /// </summary>
    public struct PlayerHoverElement
    {
        public PlayerHoverKind Kind;
        public int  MeshIndex;   // MeshContextList インデックス
        public int  VertexIndex; // 頂点（Kind=Vertex のみ）
        public int  EdgeV1;      // 辺 V1 ローカル（Kind=Edge のみ）
        public int  EdgeV2;      // 辺 V2 ローカル（Kind=Edge のみ）
        public int  FaceIndex;   // 面インデックス（Kind=Face/Line のみ）
        // Kind=Line のとき FaceIndex が MeshObject.Faces[] の添字（VertexCount==2 の面）

        public bool HasHit => Kind != PlayerHoverKind.None;
        public static readonly PlayerHoverElement None =
            new PlayerHoverElement { Kind = PlayerHoverKind.None, MeshIndex = -1 };
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
        // 投げ縄選択内部状態
        // ================================================================

        public bool         IsLassoSelecting { get; private set; }
        public List<Vector2> LassoPoints     { get; } = new List<Vector2>();

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

        /// <summary>
        /// ホバー要素によるクリック選択。SelectionState.Mode を参照して
        /// 頂点/辺/補助線分/面を選択する。
        ///
        /// 【挙動】
        ///   ヒット無し → 全解除（Shift/Ctrl なし時）
        ///   Shift → 追加、Ctrl → トグル、それ以外 → 単独選択
        /// </summary>
        public void ApplyElementClick(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (!elem.HasHit)
            {
                if (!mods.Shift && !mods.Ctrl) ClearAll();
                return;
            }

            bool additive = mods.Shift || mods.Ctrl;
            if (!additive) _selectionState.ClearAll();

            switch (elem.Kind)
            {
                case PlayerHoverKind.Vertex:
                    if (mods.Ctrl && _selectionState.Vertices.Contains(elem.VertexIndex))
                        _selectionState.Vertices.Remove(elem.VertexIndex);
                    else
                        _selectionState.SelectVertex(elem.VertexIndex, additive);
                    break;

                case PlayerHoverKind.Edge:
                {
                    var pair = new Poly_Ling.Selection.VertexPair(elem.EdgeV1, elem.EdgeV2);
                    if (mods.Ctrl && _selectionState.Edges.Contains(pair))
                        _selectionState.DeselectEdge(pair);
                    else
                        _selectionState.SelectEdge(pair, additive);
                    break;
                }

                case PlayerHoverKind.Line:
                    // 補助線分。FaceIndex が MeshObject.Faces[] の添字（VertexCount==2）
                    if (mods.Ctrl && _selectionState.Lines.Contains(elem.FaceIndex))
                        _selectionState.Lines.Remove(elem.FaceIndex);
                    else
                        _selectionState.SelectLine(elem.FaceIndex, additive);
                    break;

                case PlayerHoverKind.Face:
                    if (mods.Ctrl && _selectionState.Faces.Contains(elem.FaceIndex))
                        _selectionState.DeselectFace(elem.FaceIndex);
                    else
                        _selectionState.SelectFace(elem.FaceIndex, additive);
                    break;
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
        // 投げ縄選択
        // ================================================================

        /// <summary>投げ縄選択開始。</summary>
        public void BeginLassoSelect(Vector2 start)
        {
            IsLassoSelecting = true;
            LassoPoints.Clear();
            LassoPoints.Add(start);
        }

        /// <summary>投げ縄点追加（ドラッグ中に一定距離移動したとき）。</summary>
        public void UpdateLassoSelect(Vector2 current)
        {
            if (LassoPoints.Count == 0 ||
                Vector2.Distance(current, LassoPoints[LassoPoints.Count - 1]) > 2f)
            {
                LassoPoints.Add(current);
            }
        }

        /// <summary>
        /// 投げ縄選択確定。lassoVertices は投げ縄内にある頂点インデックス列。
        /// </summary>
        public void EndLassoSelect(IEnumerable<int> lassoVertices, ModifierKeys mods)
        {
            IsLassoSelecting = false;
            LassoPoints.Clear();

            if (!mods.Shift && !mods.Ctrl)
                _selectionState.Vertices.Clear();

            foreach (int v in lassoVertices)
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

        /// <summary>投げ縄選択をキャンセル。</summary>
        public void CancelLassoSelect()
        {
            IsLassoSelecting = false;
            LassoPoints.Clear();
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        public void ClearAll()
        {
            bool any = _selectionState.Vertices.Count > 0
                    || _selectionState.Edges.Count > 0
                    || _selectionState.Faces.Count > 0
                    || _selectionState.Lines.Count > 0;
            if (!any) return;
            _selectionState.ClearAll();
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
