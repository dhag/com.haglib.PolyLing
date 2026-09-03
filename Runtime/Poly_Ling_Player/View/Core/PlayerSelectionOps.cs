// PlayerSelectionOps.cs
// プレイヤービルド用の選択共通ロジック。
// クリック選択・矩形選択の SelectionState 操作を担う。
// 各 IPlayerToolHandler から呼び出して再利用する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
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
    /// 「どのメッシュの何番の頂点か」を表す組。
    /// 矩形／投げ縄選択が複数メッシュにまたがるため、
    /// 頂点インデックス単独では対象メッシュを特定できないので使う。
    /// MeshIndex は MeshContextList インデックス（unified インデックスではない）。
    /// </summary>
    public struct MeshVertexRef
    {
        public int MeshIndex;
        public int VertexIndex;

        public MeshVertexRef(int meshIndex, int vertexIndex)
        {
            MeshIndex   = meshIndex;
            VertexIndex = vertexIndex;
        }
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

        /// <summary>
        /// 管理対象の SelectionState が差し替えられた直後に呼ばれる（Viewer から結線）。
        ///
        /// 選択モード（頂点/辺/面/線分）の権限は Viewer 側の一箇所にあり、
        /// SelectionState は経路ごとに別インスタンスへ差し替わる。差し替え直後に
        /// 現在の実効モードを再適用しないと、新しいインスタンスの既定値
        /// （Vertex|Edge|Face|Line）のままになりチェックボックスが効かなくなる。
        /// </summary>
        public Action<SelectionState> OnStateInstalled;

        public void SetSelectionState(SelectionState selectionState)
        {
            _selectionState = selectionState ?? throw new ArgumentNullException(nameof(selectionState));
            OnStateInstalled?.Invoke(_selectionState);
        }

        /// <summary>
        /// 管理中の SelectionState への参照（＝先頭選択メッシュのもの）。
        /// ToolHandler が選択モードや頂点リストを参照する際に使う。
        /// 複数メッシュへ書き込む処理はこのプロパティではなく
        /// ResolveSelection / TargetSelections を経由すること。
        /// </summary>
        public SelectionState SelectionState => _selectionState;

        // ================================================================
        // 複数オブジェクト選択対応
        // ================================================================
        //
        // SelectionState は「メッシュ 1 個ぶんの選択」しか表せない
        // （Vertices などが MeshObject 内ローカル番号のため）。
        // 複数メッシュを選んだ状態では MeshContext ごとの Selection を
        // それぞれ操作する必要がある。GPU 側の選択フラグ更新
        // （UnifiedBufferManager_Update.UpdateAllSelectionFlags 他）は
        // 既に MeshContext 単位で読んでいるため、こちら側を合わせれば済む。
        // ================================================================

        /// <summary>
        /// 現在のモデルを返すコールバック（Viewer から結線）。
        /// 未設定なら従来どおり単一 SelectionState だけを操作する。
        /// </summary>
        public Func<ModelContext> GetModel;

        /// <summary>
        /// 操作対象メッシュ（ModelContext.SelectedDrawableMeshIndices）の
        /// SelectionState を列挙する。
        /// GetModel 未設定・メッシュ未選択の場合は単一 SelectionState を 1 個返す。
        /// </summary>
        private IEnumerable<SelectionState> TargetSelections()
        {
            var model = GetModel?.Invoke();
            if (model == null || model.SelectedDrawableMeshIndices.Count == 0)
            {
                if (_selectionState != null) yield return _selectionState;
                yield break;
            }

            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(ctxIdx);
                if (mc?.Selection != null) yield return mc.Selection;
            }
        }

        /// <summary>
        /// ホバー要素の MeshIndex（MeshContextList インデックス）から
        /// 書き込み先 SelectionState を解決する。
        /// 解決できない場合は単一 SelectionState を返す。
        /// </summary>
        private SelectionState ResolveSelection(int meshContextIndex)
        {
            if (meshContextIndex < 0) return _selectionState;
            var model = GetModel?.Invoke();
            if (model == null) return _selectionState;
            var mc = model.GetMeshContext(meshContextIndex);
            return mc?.Selection ?? _selectionState;
        }

        /// <summary>操作対象メッシュ全ての選択をクリアする（OnSelectionChanged は呼ばない）。</summary>
        private void ClearAllTargetsSilent()
        {
            foreach (var sel in TargetSelections())
                sel.ClearAll();
        }

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
        /// <remarks>
        /// PlayerHitResult.MeshIndex は PlayerViewportManager.GetHoverHit が
        /// GlobalToLocalVertexIndex の戻り値をそのまま入れており unified インデックス。
        /// PlayerHoverElement.MeshIndex（MeshContextList インデックス）とは別系のため、
        /// 本メソッドでは書き込み先メッシュの解決に使わない。
        /// GetHoverElement が結線されている経路では ApplyElementClick が使われる。
        /// </remarks>
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
            if (!ResolveElementClick(elem, mods, out var r)) return;

            ApplyElementSet(
                r.ClearTargets, r.Op,
                r.VertexMeshIndices, r.VertexIndices,
                r.EdgeMeshIndices,   r.EdgePairs,
                r.FaceMeshIndices,   r.FaceIndices,
                r.LineMeshIndices,   r.LineIndices);
        }

        /// <summary>
        /// クリック 1 回の「解釈だけ」を行う。SelectionState には触らない。
        ///
        /// 【なぜ分けるか】
        ///   マウス経路をコマンド発行に寄せるため、修飾キーの解釈結果を
        ///   SelectElementsCommand へ載せられる形で取り出す必要がある。
        ///   書き込みは ApplyElementSet が 1 本で持つ。
        ///
        /// 【解釈の規則】（従来のクリック挙動と同じ）
        ///   ヒット無し・Shift/Ctrl なし → 全解除（Replace で要素 0 個）
        ///   ヒット無し・Shift/Ctrl あり → 何もしない（false を返す）
        ///   ヒット有り・修飾なし        → Replace
        ///   ヒット有り・Shift           → Add
        ///   ヒット有り・Ctrl かつ未選択 → Add
        ///   ヒット有り・Ctrl かつ既選択 → Remove
        /// </summary>
        /// <returns>書き換えるものがあれば true。</returns>
        public bool ResolveElementClick(
            PlayerHoverElement elem, ModifierKeys mods, out ElementClickResult result)
        {
            result = default;
            result.ClearTargets = CollectTargetMasterIndices();

            if (!elem.HasHit)
            {
                if (mods.Shift || mods.Ctrl) return false;
                // 全解除。Replace で要素を 1 個も渡さない。
                result.Op = SelectElementsCommand.SelectOp.Replace;
                return true;
            }

            // 書き込み先は「当たったメッシュ」の SelectionState。
            // elem.VertexIndex / FaceIndex はそのメッシュ内のローカル番号なので、
            // 別メッシュの SelectionState に入れると別の要素を選ぶことになる。
            var target = ResolveSelection(elem.MeshIndex);
            if (target == null) return false;

            bool additive = mods.Shift || mods.Ctrl;
            var op = additive
                ? SelectElementsCommand.SelectOp.Add
                : SelectElementsCommand.SelectOp.Replace;

            int mesh = elem.MeshIndex;

            switch (elem.Kind)
            {
                case PlayerHoverKind.Vertex:
                    if (mods.Ctrl && target.Vertices.Contains(elem.VertexIndex))
                        op = SelectElementsCommand.SelectOp.Remove;
                    result.VertexMeshIndices = new[] { mesh };
                    result.VertexIndices     = new[] { elem.VertexIndex };
                    break;

                case PlayerHoverKind.Edge:
                {
                    var pair = new Poly_Ling.Selection.VertexPair(elem.EdgeV1, elem.EdgeV2);
                    if (mods.Ctrl && target.Edges.Contains(pair))
                        op = SelectElementsCommand.SelectOp.Remove;
                    result.EdgeMeshIndices = new[] { mesh };
                    result.EdgePairs       = new[] { elem.EdgeV1, elem.EdgeV2 };
                    break;
                }

                case PlayerHoverKind.Line:
                    // 補助線分。FaceIndex が MeshObject.Faces[] の添字（VertexCount==2）
                    if (mods.Ctrl && target.Lines.Contains(elem.FaceIndex))
                        op = SelectElementsCommand.SelectOp.Remove;
                    result.LineMeshIndices = new[] { mesh };
                    result.LineIndices     = new[] { elem.FaceIndex };
                    break;

                case PlayerHoverKind.Face:
                    if (mods.Ctrl && target.Faces.Contains(elem.FaceIndex))
                        op = SelectElementsCommand.SelectOp.Remove;
                    result.FaceMeshIndices = new[] { mesh };
                    result.FaceIndices     = new[] { elem.FaceIndex };
                    break;

                default:
                    return false;
            }

            result.Op = op;
            return true;
        }

        /// <summary>ResolveElementClick の戻り。SelectElementsCommand へそのまま載せられる形。</summary>
        public struct ElementClickResult
        {
            public int[] ClearTargets;
            public SelectElementsCommand.SelectOp Op;
            public int[] VertexMeshIndices;
            public int[] VertexIndices;
            public int[] EdgeMeshIndices;
            public int[] EdgePairs;
            public int[] FaceMeshIndices;
            public int[] FaceIndices;
            public int[] LineMeshIndices;
            public int[] LineIndices;
        }

        /// <summary>操作対象メッシュの MasterIndex 一覧。GetModel 未設定なら空。</summary>
        private int[] CollectTargetMasterIndices()
        {
            var model = GetModel?.Invoke();
            if (model == null) return Array.Empty<int>();
            var list = model.SelectedDrawableMeshIndices;
            if (list == null || list.Count == 0) return Array.Empty<int>();
            var arr = new int[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i];
            return arr;
        }

        /// <summary>
        /// 要素の集合を直接指定して選択を書き換える。
        ///
        /// 【なぜ要るか】
        ///   ApplyElementClick は GPU ホバーが返した 1 要素と修飾キーを解釈する。
        ///   コマンド経由（自動検証・MCP）はホバーも修飾キーも持たないので、
        ///   「この集合をこう扱う」という指定を受ける入口をここに置く。
        ///   トグルではないので、同じ要素が 2 回来ても 1 つとして扱う。
        ///   クリック経路も ResolveElementClick 経由でここへ来る。
        ///
        /// 【下請けは共通】
        ///   書き込み先の解決（ResolveSelection）と SelectionState の Select* は
        ///   クリック経路と同じものを通す。ここで持つのは並びの解釈だけ。
        ///
        /// 【索引はメッシュ内ローカル番号】
        ///   要素ごとに属するメッシュを *MeshIndices で受ける。単一 SelectionState へ
        ///   まとめて入れると別メッシュの別要素を選ぶことになる。
        /// </summary>
        /// <param name="clearMasterIndices">
        /// Op が Replace のときに選択を消す対象。空のときは操作対象メッシュ全部
        /// （TargetSelections）を消す。
        /// </param>
        public void ApplyElementSet(
            IReadOnlyList<int> clearMasterIndices,
            SelectElementsCommand.SelectOp op,
            IReadOnlyList<int> vertexMeshIndices, IReadOnlyList<int> vertexIndices,
            IReadOnlyList<int> edgeMeshIndices,   IReadOnlyList<int> edgePairs,
            IReadOnlyList<int> faceMeshIndices,   IReadOnlyList<int> faceIndices,
            IReadOnlyList<int> lineMeshIndices,   IReadOnlyList<int> lineIndices)
        {
            bool remove = op == SelectElementsCommand.SelectOp.Remove;
            bool toggle = op == SelectElementsCommand.SelectOp.Toggle;

            if (op == SelectElementsCommand.SelectOp.Replace)
            {
                if (clearMasterIndices == null || clearMasterIndices.Count == 0)
                {
                    ClearAllTargetsSilent();
                }
                else
                {
                    foreach (int idx in clearMasterIndices)
                        ResolveSelection(idx)?.ClearAll();
                }
            }

            if (vertexIndices != null && vertexMeshIndices != null)
                for (int i = 0; i < vertexIndices.Count && i < vertexMeshIndices.Count; i++)
                {
                    var sel = ResolveSelection(vertexMeshIndices[i]);
                    if (sel == null) continue;
                    if (toggle)      sel.ToggleVertex(vertexIndices[i]);
                    else if (remove) sel.Vertices.Remove(vertexIndices[i]);
                    else             sel.SelectVertex(vertexIndices[i], additive: true);
                }

            if (edgePairs != null && edgeMeshIndices != null)
                for (int i = 0; i < edgeMeshIndices.Count && (i * 2) + 1 < edgePairs.Count; i++)
                {
                    var sel = ResolveSelection(edgeMeshIndices[i]);
                    if (sel == null) continue;
                    var pair = new Poly_Ling.Selection.VertexPair(
                        edgePairs[i * 2], edgePairs[(i * 2) + 1]);
                    if (toggle)      sel.ToggleEdge(pair);
                    else if (remove) sel.DeselectEdge(pair);
                    else             sel.SelectEdge(pair, additive: true);
                }

            if (faceIndices != null && faceMeshIndices != null)
                for (int i = 0; i < faceIndices.Count && i < faceMeshIndices.Count; i++)
                {
                    var sel = ResolveSelection(faceMeshIndices[i]);
                    if (sel == null) continue;
                    if (toggle)      sel.ToggleFace(faceIndices[i]);
                    else if (remove) sel.DeselectFace(faceIndices[i]);
                    else             sel.SelectFace(faceIndices[i], additive: true);
                }

            if (lineIndices != null && lineMeshIndices != null)
                for (int i = 0; i < lineIndices.Count && i < lineMeshIndices.Count; i++)
                {
                    var sel = ResolveSelection(lineMeshIndices[i]);
                    if (sel == null) continue;
                    if (toggle)      sel.ToggleLine(lineIndices[i]);
                    else if (remove) sel.DeselectLine(lineIndices[i]);
                    else             sel.SelectLine(lineIndices[i], additive: true);
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
        /// <summary>
        /// 矩形選択の状態を解除する。
        ///
        /// 選択の書き込みは持たない。走査結果は SelectElementsCommand として
        /// 発行され、ApplyElementSet が 1 本で書き込む
        /// （MoveToolHandler.CommitBoxSelect を参照）。
        /// </summary>
        public void EndBoxSelect()
        {
            IsBoxSelecting = false;
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
        /// <summary>
        /// 投げ縄選択の状態を解除する。
        ///
        /// 選択の書き込みは持たない。走査結果は SelectElementsCommand として
        /// 発行され、ApplyElementSet が 1 本で書き込む
        /// （MoveToolHandler.CommitLassoSelect を参照）。
        /// </summary>
        public void EndLassoSelect()
        {
            IsLassoSelecting = false;
            LassoPoints.Clear();
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

        /// <summary>操作対象メッシュ全ての選択をクリアする。</summary>
        public void ClearAll()
        {
            bool any = false;
            foreach (var sel in TargetSelections())
            {
                if (sel.Vertices.Count > 0 || sel.Edges.Count > 0
                    || sel.Faces.Count > 0 || sel.Lines.Count > 0)
                {
                    any = true;
                    break;
                }
            }
            if (!any) return;

            ClearAllTargetsSilent();
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
