// AdvancedSelectToolHandler.cs
// AdvancedSelectTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;
using Poly_Ling.UndoSystem;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    public class AdvancedSelectToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly AdvancedSelectTool _tool = new AdvancedSelectTool();
        private          ProjectContext     _project;
        private          PlayerSelectionOps _selectionOps;

        // TopologyCache はメッシュごとにキャッシュ
        private readonly Dictionary<int, TopologyCache> _topoCaches
            = new Dictionary<int, TopologyCache>();

        // ================================================================
        // 外部コールバック
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action            OnSelectionChanged;

        /// <summary>
        /// GPU ホバー要素取得（Viewer から結線）。指定 SelectMode に対する
        /// 現在ホバー中の頂点/辺（メッシュローカルインデックス）を返す。
        /// クリック開始要素の確定に使う（CPU 探索の誤爆を避ける）。
        /// </summary>
        public Func<Poly_Ling.Selection.MeshSelectMode, PlayerHoverElement> GetHoverElement;

        /// <summary>
        /// ツール固有の選択モード override を Viewer へ通知する（Viewer から結線）。
        /// null は「override なし＝ユーザのチェックボックスに従う」。
        /// GetHoverElement の引数だけを固定するとホバーハイライトと GPU 側の絞り込みが
        /// 追従しないため、モード変更のたびに Viewer 権限へ通知する。
        /// </summary>
        public Action<Poly_Ling.Selection.MeshSelectMode?> OnRequestSelectModeOverride;

        /// <summary>
        /// 現在の高度選択モードが要求するホバー種別。null はユーザ指定に従う。
        /// Belt / EdgeLoop は辺と補助線分、ShortestPath は頂点。
        /// 属性系（Connected / UvNormalCount / NearAxis / BoundaryEdge*）は絞らない。
        /// </summary>
        public Poly_Ling.Selection.MeshSelectMode? HoverSelectModeOverride => Mode switch
        {
            AdvancedSelectMode.Belt         => Poly_Ling.Selection.MeshSelectMode.Edge
                                             | Poly_Ling.Selection.MeshSelectMode.Line,
            AdvancedSelectMode.EdgeLoop     => Poly_Ling.Selection.MeshSelectMode.Edge
                                             | Poly_Ling.Selection.MeshSelectMode.Line,
            AdvancedSelectMode.ShortestPath => Poly_Ling.Selection.MeshSelectMode.Vertex,
            _                               => (Poly_Ling.Selection.MeshSelectMode?)null,
        };

        // ================================================================
        // モード設定公開
        // ================================================================

        public AdvancedSelectMode Mode
        {
            get => ((AdvancedSelectSettings)_tool.Settings)?.Mode ?? AdvancedSelectMode.Connected;
            set
            {
                if (_tool.Settings is AdvancedSelectSettings s) s.Mode = value;
                // サブモードでホバー種別が変わる。Viewer 側の override を追従させる。
                OnRequestSelectModeOverride?.Invoke(HoverSelectModeOverride);
            }
        }

        public bool AddToSelection
        {
            get => ((AdvancedSelectSettings)_tool.Settings)?.AddToSelection ?? false;
            set { if (_tool.Settings is AdvancedSelectSettings s) s.AddToSelection = value; }
        }

        /// <summary>EdgeLoop モードの方向しきい値（0〜1）。</summary>
        public float EdgeLoopThreshold
        {
            get => ((AdvancedSelectSettings)_tool.Settings)?.EdgeLoopThreshold ?? 0.5f;
            set { if (_tool.Settings is AdvancedSelectSettings s) s.EdgeLoopThreshold = Mathf.Clamp01(value); }
        }

        /// <summary>UvNormalCount モードのしきい値。この値より大きい頂点を選ぶ。</summary>
        public int UvNormalCountThreshold
        {
            get => ((AdvancedSelectSettings)_tool.Settings)?.UvNormalCountThreshold ?? 0;
            set { if (_tool.Settings is AdvancedSelectSettings s) s.UvNormalCountThreshold = value; }
        }

        /// <summary>NearAxis モードのしきい値。軸に対応する平面までの距離がこの値未満の頂点を選ぶ。</summary>
        public float AxisDistanceThreshold
        {
            get => ((AdvancedSelectSettings)_tool.Settings)?.AxisDistanceThreshold ?? 0.00001f;
            set { if (_tool.Settings is AdvancedSelectSettings s) s.AxisDistanceThreshold = value; }
        }

        /// <summary>NearAxis モードの軸。X なら |Position.x| を見る。</summary>
        public SymmetryAxis AxisKind
        {
            get => ((AdvancedSelectSettings)_tool.Settings)?.AxisKind ?? SymmetryAxis.X;
            set { if (_tool.Settings is AdvancedSelectSettings s) s.AxisKind = value; }
        }

        /// <summary>属性選択を現在の選択頂点の中だけに限定するか。</summary>
        public bool LimitToCurrentSelection
        {
            get => ((AdvancedSelectSettings)_tool.Settings)?.LimitToCurrentSelection ?? false;
            set { if (_tool.Settings is AdvancedSelectSettings s) s.LimitToCurrentSelection = value; }
        }

        /// <summary>現在のモードがボタン実行型（クリック不要）か。</summary>
        public bool IsAttributeMode => AdvancedSelectTool.IsAttributeMode(Mode);

        /// <summary>
        /// ShortestPath モードで登録されている始点頂点インデックスを返す。未登録は -1。
        /// エディタ版 ShortestPathSelectMode.DrawModeSettingsUI() の始点表示に対応。
        /// </summary>
        public int GetShortestPathFirstVertex() => _tool.GetShortestPathFirstVertex();

        /// <summary>
        /// ShortestPath モードの始点をクリアする。
        /// エディタ版 ClearFirstPoint ボタンに対応。
        /// </summary>
        public void ClearShortestPathFirst() => _tool.Reset();

        /// <summary>
        /// すべての選択（頂点/辺/面/線）を解除する。
        /// 進行中の ShortestPath 始点等もリセットする。全モード共通のクリアボタン用。
        /// </summary>
        public void ClearAllSelection()
        {
            _selectionOps?.ClearAll();     // SelectionState 全解除 + 描画通知（内部 OnSelectionChanged）
            _tool.Reset();                 // ShortestPath 始点など進行中状態も破棄
            OnSelectionChanged?.Invoke();  // renderer 再通知 + RequestNormal
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// UvNormalCount / NearAxis モードの選択を実行する。
        /// クリック非依存のため GPU ホバーは参照しない。
        ///
        /// private にしてある。パネルからの直呼びは塞ぎ、
        /// AdvancedSelectByAttributeCommand 経由に統一するため。
        /// </summary>
        private void ExecuteAttributeSelectCore()
        {
            var ctx = BuildToolContext(default(ModifierKeys), Vector2.zero);
            if (ctx == null) return;

            var oldSnap = _selectionOps?.SelectionState?.CreateSnapshot();
            bool changed = _tool.ExecuteAttributeSelect(ctx);
            if (changed)
            {
                RecordSelectionUndo(ctx, oldSnap);
                OnSelectionChanged?.Invoke();
            }
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// 属性選択コマンドを実行する。
        ///
        /// 【マウス／パネル経路と同じ実装を通す】
        ///   走査は AdvancedSelectTool.ExecuteAttributeSelect が正典。ここは
        ///   コマンドの値をツール設定へ入れてから同じ経路を呼ぶ。
        ///
        /// 【設定の扱い】
        ///   コマンドの値を正典として実行し、終わったらパネルの値へ戻す。
        ///   1 呼び出しがパネルの状態に依存しないようにするため。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(
            Poly_Ling.Data.AdvancedSelectByAttributeCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            if (!AdvancedSelectTool.IsAttributeMode(cmd.Mode))
            { reason = $"{cmd.Mode} は属性モードではありません"; return false; }

            var ctx = BuildToolContext(default(ModifierKeys), Vector2.zero);
            if (ctx == null) { reason = "モデルがありません"; return false; }

            var model = ctx.Model;
            var mc    = model?.ActiveMeshContext;
            if (mc?.MeshObject == null) { reason = "編集対象メッシュがありません"; return false; }

            var indices = cmd.MasterIndices;
            if (indices == null || indices.Length != 1)
            { reason = "MasterIndices は 1 個で指定してください"; return false; }

            int activeMaster = model.IndexOf(mc);
            if (indices[0] != activeMaster)
            {
                reason = $"masterIndex {indices[0]} は編集対象（{activeMaster}）ではありません";
                return false;
            }

            // パネルの設定を退避する。
            var savedMode      = Mode;
            bool savedAdd      = AddToSelection;
            int  savedUvCount  = UvNormalCountThreshold;
            var  savedAxisKind = AxisKind;
            float savedAxisDist = AxisDistanceThreshold;
            bool savedLimit    = LimitToCurrentSelection;

            try
            {
                Mode                    = cmd.Mode;
                AddToSelection          = cmd.AddToSelection;
                UvNormalCountThreshold  = cmd.UvNormalCountThreshold;
                AxisKind                = cmd.AxisKind;
                AxisDistanceThreshold   = cmd.AxisDistanceThreshold;
                LimitToCurrentSelection = cmd.LimitToCurrentSelection;

                ExecuteAttributeSelectCore();
            }
            finally
            {
                Mode                    = savedMode;
                AddToSelection          = savedAdd;
                UvNormalCountThreshold  = savedUvCount;
                AxisKind                = savedAxisKind;
                AxisDistanceThreshold   = savedAxisDist;
                LimitToCurrentSelection = savedLimit;
            }

            return true;
        }

        /// <summary>
        /// BoundaryEdgeInSelection モードの選択を実行する（パネルの「実行」ボタン）。
        /// クリック非依存のため GPU ホバーは参照しない。
        /// </summary>
        public void ExecuteBoundaryEdgeInSelection()
        {
            var ctx = BuildToolContext(default(ModifierKeys), Vector2.zero);
            if (ctx == null) return;

            var oldSnap = _selectionOps?.SelectionState?.CreateSnapshot();
            bool changed = _tool.ExecuteBoundaryEdgeInSelection(ctx);
            if (changed)
            {
                RecordSelectionUndo(ctx, oldSnap);
                OnSelectionChanged?.Invoke();
            }
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// コマンドで指定された種から詳細選択を実行する。
        ///
        /// 【なぜ要るか】
        ///   クリック経路は GPU ホバーで種を決めるので、コマンド経由
        ///   （自動検証・MCP）からは通せない。同じモード実装を通したまま
        ///   種だけを渡せる入口をここに置く。EdgeBridgeToolHandler.SetPicks と同じ形。
        ///
        /// 【実行時と同じ配線を通す】
        ///   種は AdvancedSelectTool.SetGpuStart へ入れ、確定は OnMouseDown を呼ぶ。
        ///   各モードは mousePos を使わず GpuStart* だけを見るので、クリックと同じ
        ///   経路・同じ結果になる。選択アルゴリズムを別に持たない。
        ///
        /// 【選択種別】
        ///   コマンドの SelectVertices / SelectEdges / SelectFaces を
        ///   SelectionState.Mode へ一時的に流し込み、実行後に元へ戻す。
        ///   モードによっては効かないものがある（EdgeLoop は頂点、
        ///   ShortestPath は辺を、Tool 側が意図的に外している）。
        ///
        /// 【対象メッシュ】
        ///   Tool は ctx.ActiveMeshObject に対して動く。コマンドの MasterIndex が
        ///   編集対象と違うときは、黙って別のメッシュを触らずに false を返す。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.AdvancedSelectCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var ctx = BuildToolContext(default(ModifierKeys), Vector2.zero);
            if (ctx == null) { reason = "モデルがありません"; return false; }

            var model = ctx.Model;
            var mc    = model?.ActiveMeshContext;
            if (mc?.MeshObject == null) { reason = "編集対象メッシュがありません"; return false; }

            var cmdIndices = cmd.MasterIndices;
            if (cmdIndices == null || cmdIndices.Length != 1)
            { reason = "MasterIndices は 1 個で指定してください"; return false; }

            int activeMaster = model.IndexOf(mc);
            if (cmdIndices[0] != activeMaster)
            {
                reason = $"masterIndex {cmdIndices[0]} は編集対象（{activeMaster}）ではありません";
                return false;
            }

            if (AdvancedSelectTool.IsAttributeMode(cmd.Mode))
            {
                reason = $"{cmd.Mode} はクリック非依存のモードです。AdvancedSelectByAttributeCommand を使ってください";
                return false;
            }

            // 種の過不足をモードごとに先に弾く。足りないまま流すと
            // モード側が false を返すだけで理由が残らない。
            var seedEdge = (cmd.SeedEdgeV1 >= 0 && cmd.SeedEdgeV2 >= 0)
                ? (Poly_Ling.Selection.VertexPair?)new Poly_Ling.Selection.VertexPair(cmd.SeedEdgeV1, cmd.SeedEdgeV2)
                : null;

            switch (cmd.Mode)
            {
                case AdvancedSelectMode.Connected:
                    if (cmd.SeedVertexIndex < 0 && !seedEdge.HasValue && cmd.SeedFaceIndex < 0)
                    { reason = "Connected は頂点・辺・面のいずれかの種が要ります"; return false; }
                    break;
                case AdvancedSelectMode.Belt:
                case AdvancedSelectMode.EdgeLoop:
                    if (!seedEdge.HasValue)
                    { reason = $"{cmd.Mode} は辺の種（SeedEdgeV1 / SeedEdgeV2）が要ります"; return false; }
                    break;
                case AdvancedSelectMode.ShortestPath:
                    if (cmd.SeedVertexIndex < 0 || cmd.EndVertexIndex < 0)
                    { reason = "ShortestPath は始点と終点の頂点が要ります"; return false; }
                    break;
                default:
                    reason = $"{cmd.Mode} はコマンドから実行できません";
                    return false;
            }

            var sel = _selectionOps?.SelectionState;
            if (sel == null) { reason = "選択状態がありません"; return false; }

            Mode              = cmd.Mode;
            AddToSelection    = cmd.Additive;
            EdgeLoopThreshold = cmd.EdgeLoopThreshold;

            var savedMode = sel.Mode;
            var wantMode  = Poly_Ling.Selection.MeshSelectMode.None;
            if (cmd.SelectVertices) wantMode |= Poly_Ling.Selection.MeshSelectMode.Vertex;
            if (cmd.SelectEdges)    wantMode |= Poly_Ling.Selection.MeshSelectMode.Edge;
            if (cmd.SelectFaces)    wantMode |= Poly_Ling.Selection.MeshSelectMode.Face;
            if (wantMode == Poly_Ling.Selection.MeshSelectMode.None)
            { reason = "SelectVertices / SelectEdges / SelectFaces がすべて false です"; return false; }

            var oldSnap = sel.CreateSnapshot();
            bool changed;

            try
            {
                sel.Mode = wantMode;

                // 進行中の状態（ShortestPath の始点など）を捨ててから始める。
                _tool.Reset();

                if (cmd.Mode == AdvancedSelectMode.ShortestPath)
                {
                    // 1 回目で始点を覚え、2 回目で確定する作り
                    // （ShortestPathSelectMode.cs:30-52）。
                    _tool.SetGpuStart(cmd.SeedVertexIndex, null, -1, -1);
                    _tool.OnMouseDown(ctx, Vector2.zero);

                    _tool.SetGpuStart(cmd.EndVertexIndex, null, -1, -1);
                    changed = _tool.OnMouseDown(ctx, Vector2.zero);
                }
                else
                {
                    _tool.SetGpuStart(cmd.SeedVertexIndex, seedEdge, cmd.SeedFaceIndex, -1);
                    changed = _tool.OnMouseDown(ctx, Vector2.zero);
                }
            }
            finally
            {
                sel.Mode = savedMode;
            }

            if (!changed) { reason = "選択が変わりませんでした"; return false; }

            RecordSelectionUndo(ctx, oldSnap);
            OnSelectionChanged?.Invoke();
            OnRepaint?.Invoke();
            return true;
        }

        /// <summary>
        /// 現在の選択を反転する（パネルの「現在の選択を反転」ボタン）。
        /// SelectionState.Mode で有効なビットのみ対象。
        /// </summary>
        public void InvertSelection()
        {
            var ctx = BuildToolContext(default(ModifierKeys), Vector2.zero);
            if (ctx == null) return;

            var oldSnap = _selectionOps?.SelectionState?.CreateSnapshot();
            bool changed = _tool.InvertSelection(ctx);
            if (changed)
            {
                RecordSelectionUndo(ctx, oldSnap);
                OnSelectionChanged?.Invoke();
            }
            OnRepaint?.Invoke();
        }

        // ================================================================
        // 初期化
        // ================================================================

        /// <summary>
        /// コマンド送信口。クリック確定をコマンド発行に寄せるために使う。
        /// PolyLingPlayerViewerCore が DispatchPanelCommand を刺す。
        /// </summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        public void SetProject(ProjectContext project) => _project = project;
        public void SetSelectionOps(PlayerSelectionOps ops) => _selectionOps = ops;
        public void SetUndoController(MeshUndoController ctrl) => _undoController = ctrl;
        private MeshUndoController _undoController;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            ResolveGpuStart();

            if (TrySendSeedCommand(ctx))
            {
                // ドラッグ状態を残さないため OnMouseUp だけ通す。
                _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
                OnRepaint?.Invoke();
                return;
            }

            var oldSnap = _selectionOps?.SelectionState?.CreateSnapshot();
            bool changed = _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
            if (changed)
            {
                RecordSelectionUndo(ctx, oldSnap);
                OnSelectionChanged?.Invoke();
            }
            OnRepaint?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            ResolveGpuStart();

            if (TrySendSeedCommand(ctx)) return;

            var oldSnap = _selectionOps?.SelectionState?.CreateSnapshot();
            bool changed = _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            if (changed)
            {
                RecordSelectionUndo(ctx, oldSnap);
                OnSelectionChanged?.Invoke();
            }
        }

        /// <summary>
        /// GPU ホバーが返した起点から AdvancedSelectCommand を送る。
        ///
        /// 【なぜ要るか】
        ///   選択アルゴリズムは AdvancedSelectTool が正典で、受け口
        ///   （ExecuteFromCommand）も既にある。発行側だけが無かったので足す。
        ///
        /// 【対象モードを絞っている理由】
        ///   Connected / Belt / EdgeLoop は 1 クリックで起点が確定し、その 1 回が
        ///   確定操作になる。ShortestPath は 1 回目のクリックが始点の仮置きで
        ///   選択は変わらず、その状態は ShortestPathSelectMode._firstVertex に
        ///   あってここからは読めない。BoundaryEdge 系は AdvancedSelectCommand の
        ///   受け口が弾く（ExecuteFromCommand の default 分岐）。
        ///   これらは従来の直呼び経路に残す。
        ///
        /// 【出力フラグ】
        ///   マウス経路は現在の SelectionState.Mode を使う。コマンドの
        ///   SelectVertices / SelectEdges / SelectFaces もそこから起こして、
        ///   両経路で同じ対象になるようにする。
        /// </summary>
        /// <returns>コマンドを送ったら true。false なら呼び出し側が直呼び経路へ落ちる。</returns>
        private bool TrySendSeedCommand(ToolContext ctx)
        {
            if (SendCommand == null) return false;

            if (Mode != AdvancedSelectMode.Connected &&
                Mode != AdvancedSelectMode.Belt &&
                Mode != AdvancedSelectMode.EdgeLoop)
                return false;

            var model = ctx?.Model;
            var mc    = model?.ActiveMeshContext;
            if (mc?.MeshObject == null) return false;

            var sel = _selectionOps?.SelectionState;
            if (sel == null) return false;

            QueryGpuStart(out int gpuVertex, out var gpuEdge, out int gpuFace, out int _);

            // 起点の過不足はモードごとに違う。足りないときは送らず直呼びへ落とす
            // （直呼び経路のモード側が false を返して何も起きない、という従来の挙動）。
            if (Mode == AdvancedSelectMode.Connected)
            {
                if (gpuVertex < 0 && !gpuEdge.HasValue && gpuFace < 0) return false;
            }
            else if (!gpuEdge.HasValue) return false;

            bool wantV = sel.Mode.Has(Poly_Ling.Selection.MeshSelectMode.Vertex);
            bool wantE = sel.Mode.Has(Poly_Ling.Selection.MeshSelectMode.Edge);
            bool wantF = sel.Mode.Has(Poly_Ling.Selection.MeshSelectMode.Face);
            if (!wantV && !wantE && !wantF) return false;

            SendCommand(new Poly_Ling.Data.AdvancedSelectCommand(
                _project?.CurrentModelIndex ?? 0,
                new[] { model.IndexOf(mc) },
                Mode,
                seedVertexIndex:   gpuVertex,
                seedEdgeV1:        gpuEdge.HasValue ? gpuEdge.Value.V1 : -1,
                seedEdgeV2:        gpuEdge.HasValue ? gpuEdge.Value.V2 : -1,
                seedFaceIndex:     gpuFace,
                endVertexIndex:    -1,
                selectVertices:    wantV,
                selectEdges:       wantE,
                selectFaces:       wantF,
                additive:          AddToSelection,
                edgeLoopThreshold: EdgeLoopThreshold));
            return true;
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
            OnRepaint?.Invoke();
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            ResolveGpuHover();
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), Vector2.zero);
            OnRepaint?.Invoke();
        }

        // ================================================================
        // プレビューデータ取得（オーバーレイ描画用）
        // ================================================================

        /// <summary>
        /// AdvancedSelectTool が保持するプレビューコンテキストを返す。
        /// PolyLingPlayerViewer.UpdateAdvancedSelectOverlay から毎フレーム参照する。
        /// </summary>
        public AdvancedSelectContext GetPreviewContext() => _tool.GetPreviewContext();

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        /// <summary>
        /// GPU ホバー結果から次回クリックの開始要素（頂点/辺/面/線）を確定し、_tool に渡す。
        /// 操作対象メッシュ（FirstSelected）と一致するホバーのみ採用する。
        /// 未ヒット時は各インデックス -1 / null。CPU 探索へのフォールバックは行わない。
        /// </summary>
        /// <summary>直近クリックで確定した頂点（辺ヒット時は端点 V1）。未ヒットは -1。クリック強調用。</summary>
        public int LastClickVertex { get; private set; } = -1;

        /// <summary>直近クリックで確定した辺（辺ヒット時のみ）。頂点クリック時は null。辺の強調用。</summary>
        public Poly_Ling.Selection.VertexPair? LastClickEdge { get; private set; }

        private void ResolveGpuStart()
        {
            QueryGpuStart(out int gpuVertex, out var gpuEdge, out int gpuFace, out int gpuLine);
            LastClickEdge   = gpuEdge;
            LastClickVertex = gpuVertex >= 0 ? gpuVertex
                            : (gpuEdge.HasValue ? gpuEdge.Value.V1 : -1);
            _tool.SetGpuStart(gpuVertex, gpuEdge, gpuFace, gpuLine);
        }

        /// <summary>
        /// ホバー用：GPU 開始要素のみ更新する（クリック強調 LastClick* は触らない＝ホバーで
        /// フラッシュが誤爆しないように）。UpdateHover から毎フレーム呼ぶ。
        /// </summary>
        private void ResolveGpuHover()
        {
            QueryGpuStart(out int gpuVertex, out var gpuEdge, out int gpuFace, out int gpuLine);
            _tool.SetGpuStart(gpuVertex, gpuEdge, gpuFace, gpuLine);
        }

        /// <summary>
        /// GPU ホバー要素を問い合わせ、操作対象メッシュ（FirstSelected）に一致するもののみ
        /// 頂点/辺/面/線として返す。未ヒットは vertex=-1 / edge=null / face=-1 / line=-1。
        /// </summary>
        private void QueryGpuStart(out int gpuVertex, out Poly_Ling.Selection.VertexPair? gpuEdge,
                                   out int gpuFace, out int gpuLine)
        {
            gpuVertex = -1; gpuEdge = null; gpuFace = -1; gpuLine = -1;
            if (GetHoverElement == null) return;

            int firstIdx = _project?.CurrentModel?.FirstSelectedIndex ?? -1;
            if (firstIdx < 0) return;

            // モードに応じて問い合わせ種別を決める。Connected 等は現在の選択モードに従う。
            // 固定種別は HoverSelectModeOverride と同じ値を使い、ホバーハイライト
            // （Viewer 権限が適用する override）と問い合わせ種別が食い違わないようにする。
            var queryMode = HoverSelectModeOverride
                            ?? (_selectionOps?.SelectionState?.Mode
                                ?? Poly_Ling.Selection.MeshSelectMode.Vertex);

            var elem = GetHoverElement(queryMode);
            if (elem.MeshIndex != firstIdx) return;

            switch (elem.Kind)
            {
                case PlayerHoverKind.Vertex:
                    gpuVertex = elem.VertexIndex;
                    break;
                case PlayerHoverKind.Edge:
                    gpuEdge = new Poly_Ling.Selection.VertexPair(elem.EdgeV1, elem.EdgeV2);
                    break;
                case PlayerHoverKind.Face:
                    gpuFace = elem.FaceIndex;
                    break;
                case PlayerHoverKind.Line:
                    gpuLine = elem.FaceIndex;
                    break;
            }
        }

        private ToolContext BuildToolContext(ModifierKeys mods, Vector2 screenPosYDown)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;

            var baseCtx = GetToolContext?.Invoke() ?? new ToolContext();
            baseCtx.Model          = model;
            baseCtx.SelectionState = _selectionOps?.SelectionState;
            baseCtx.Repaint        = OnRepaint;
            baseCtx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(screenPosYDown, baseCtx),
            };

            // TopologyCache（メッシュ変更時に自動再構築）
            var mc = model.ActiveMeshContext;
            if (mc?.MeshObject != null)
            {
                int key = mc.MeshObject.GetHashCode();
                if (!_topoCaches.TryGetValue(key, out var topo))
                {
                    topo = new TopologyCache(mc.MeshObject);
                    _topoCaches[key] = topo;
                }
                else
                {
                    topo.SetMeshObject(mc.MeshObject);
                }
                baseCtx.TopologyCache = topo;
            }

            return baseCtx;
        }

        /// <summary>
        /// 選択変更を Undo スタックへ記録する。
        /// </summary>
        /// <remarks>
        /// 【単一メッシュ前提 — 変更時の注意】
        ///
        /// 本ハンドラは BuildToolContext で SelectionState / TopologyCache を
        /// どちらも ActiveMeshContext 由来で組み立てており、拡張選択は
        /// 操作対象メッシュ 1 個しか変更しない。
        /// よって SelectionChangeRecord（復元先が ActiveMeshContext 固定）で整合する。
        ///
        /// 将来この操作を複数メッシュへ広げる場合、本メソッドも
        /// MultiMeshSelectionChangeRecord へ移すこと。
        /// 記録側だけ複数メッシュ化すると Undo が先頭メッシュしか戻さなくなる。
        ///
        /// なお ClearAllSelection() は _selectionOps.ClearAll() で
        /// 選択メッシュ全ての選択を消すが、元から Undo を記録していない。
        /// ここに Undo を足す場合も MultiMeshSelectionChangeRecord を使うこと。
        /// </remarks>
        private void RecordSelectionUndo(ToolContext ctx, SelectionSnapshot oldSnap)
        {
            if (_undoController == null || oldSnap == null) return;
            var newSnap = _selectionOps?.SelectionState?.CreateSnapshot();
            if (newSnap == null) return;
            var model = ctx.Model;
            if (model == null) return;
            _undoController.MeshUndoContext.ParentModelContext = model;
            var record = new SelectionChangeRecord(oldSnap, newSnap);
            {
                string __dbgDesc = "詳細選択";
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _undoController.VertexEditStack.Record(record, __dbgDesc);
            }
            _undoController.FocusVertexEdit();
        }

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
