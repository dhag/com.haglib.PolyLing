// PolyLingPlayerViewerCore.ObjectArrayBridge.cs
// Player ビューアのコア：歪み複製・穴つなぎ（ブリッジ）の結線・辺群ブリッジのオーバーレイ・穴の種マーカー。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.Serialization;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.EditorBridge;
using Poly_Ling.View;
using Poly_Ling.MeshListV2;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectArray;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        // ================================================================
        // 歪み複製
        // ================================================================

        /// <summary>
        /// 歪み複製の「生成」。複製元リストを p.Count 組ぶん複製し、
        /// 各組へ歪みを掛けて出力先へ入れる。元のオブジェクトには触らない。
        ///
        /// 呼び元は図形生成パネル（高度な図形 / 新しい高度）の生成ボタン。
        /// パネルは2インスタンスあるため、状態は引数で受け取る。
        ///
        /// 生成そのものは ObjectArrayGenerator、挿入は ObjectArrayInserter が持つ。
        /// ここは Undo 記録とビュー更新だけを担う。
        /// </summary>
        /// <summary>
        /// 歪み複製の本体。コマンドの内容だけで動くよう、パネルから切り離してある。
        /// 状態表示はパネルが開いていれば出す（開いていなくても実行はできる）。
        /// </summary>
        private void ExecuteObjectArrayCore(
            ObjectArrayParams p, int[] sourceMasterIndices,
            Poly_Ling.Tools.Deformers.IMeshDeformer deformer)
        {
            var panel = _objectArraySubPanel;

            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel?.SetStatus("モデルがありません"); return; }

            var axis = CurrentWorkAxis();
            if (axis == null) { panel?.SetStatus("作業軸がありません"); return; }

            if (deformer == null) { panel?.SetStatus("歪みが選ばれていません"); return; }

            if (p == null || p.Count < 1) { panel?.SetStatus("組の数は 1 以上にしてください"); return; }

            var sources = ObjectArrayGenerator.BuildSources(
                model, sourceMasterIndices ?? new int[0]);
            if (sources.Count == 0) { panel?.SetStatus("複製元が選ばれていません"); return; }

            // 「中に生成」で出力先がルートのときは、新規オブジェクトを1つ作って
            // そこへ入れる。その新規オブジェクトはルート直下なので変換は単位。
            bool insideRoot = p.OutputMode == ObjectArrayOutputMode.Inside
                              && ObjectArrayInserter.ResolveTarget(model, p.TargetMasterIndex) == null;

            Matrix4x4 worldToOutputLocal = insideRoot
                ? Matrix4x4.identity
                : ObjectArrayInserter.GetWorldToOutputLocal(model, p.TargetMasterIndex);

            var pieces = ObjectArrayGenerator.Generate(sources, axis, deformer, p, worldToOutputLocal);
            if (pieces.Count == 0) { panel?.SetStatus("生成できませんでした"); return; }

            if (p.OutputMode == ObjectArrayOutputMode.Inside)
                ObjectArrayApplyInside(model, pieces, p, insideRoot, panel);
            else
                ObjectArrayApplyAsChildren(model, pieces, p, panel);
        }

        /// <summary>
        /// モード1: 出力先の子として、生成物ごとに描画オブジェクトを作る。
        /// </summary>
        private void ObjectArrayApplyAsChildren(
            ModelContext model, List<ObjectArrayPiece> pieces,
            ObjectArrayParams p, PlayerObjectArraySubPanel panel)
        {
            var oldSelected = model.CaptureAllSelectedIndices();

            var added = ObjectArrayInserter.InsertAsChildren(
                model, pieces, p.TargetMasterIndex, model.GenerateUniqueMeshName);

            if (added.Count == 0) { panel?.SetStatus("生成できませんでした"); return; }

            model.ComputeWorldMatrices();

            model.ClearMeshSelection();
            foreach (var e in added) model.AddToMeshSelection(e.Index);
            var newSelected = model.CaptureAllSelectedIndices();

            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(model);
                _editOps.UndoController.RecordMeshContextsAdd(added, oldSelected, newSelected);
            }

            PrimitiveMeshFinalize(model);
            panel?.SetStatus($"{added.Count} 個のオブジェクトを生成しました");
        }

        /// <summary>
        /// モード2: 出力先の頂点・面へ統合する。
        /// 出力先がルートのときは統合先が無いので新規オブジェクトを1つ作る。
        /// </summary>
        private void ObjectArrayApplyInside(
            ModelContext model, List<ObjectArrayPiece> pieces,
            ObjectArrayParams p, bool insideRoot, PlayerObjectArraySubPanel panel)
        {
            if (insideRoot)
            {
                // 全部を1つのメッシュへまとめ、新規オブジェクトとしてルートへ置く。
                string baseName = string.IsNullOrEmpty(p.NameBase) ? "ObjectArray" : p.NameBase;
                var combined = ObjectArrayInserter.CombineAll(pieces, baseName);

                var single = new ObjectArrayPiece
                {
                    Mesh          = combined,
                    Name          = baseName,
                    RelativeDepth = 0,
                    CopyIndex     = 0,
                };

                var oldSelected = model.CaptureAllSelectedIndices();

                var added = ObjectArrayInserter.InsertAsChildren(
                    model, new List<ObjectArrayPiece> { single }, -1, model.GenerateUniqueMeshName);

                if (added.Count == 0) { panel?.SetStatus("生成できませんでした"); return; }

                model.ComputeWorldMatrices();

                model.ClearMeshSelection();
                foreach (var e in added) model.AddToMeshSelection(e.Index);
                var newSelected = model.CaptureAllSelectedIndices();

                if (_editOps?.UndoController != null)
                {
                    _editOps.UndoController.SetModelContext(model);
                    _editOps.UndoController.RecordMeshContextsAdd(added, oldSelected, newSelected);
                }

                PrimitiveMeshFinalize(model);
                panel?.SetStatus($"新規オブジェクトへ {pieces.Count} 組ぶんを統合しました");
                return;
            }

            var targetMc = ObjectArrayInserter.ResolveTarget(model, p.TargetMasterIndex);
            if (targetMc?.MeshObject == null) { panel?.SetStatus("出力先が見つかりません"); return; }

            // UNDO: 変更前スナップショット（図形生成の AddToExisting と同じ経路）
            MeshObjectSnapshot before = null;
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetMeshObject(targetMc.MeshObject, targetMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = model;
                before = _editOps.UndoController.CaptureMeshObjectSnapshot();
            }

            ObjectArrayInserter.AppendInto(targetMc.MeshObject, pieces);

            var newUnityMesh = targetMc.MeshObject.ToUnityMesh();
            newUnityMesh.name      = targetMc.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            // Object.Destroy は edit mode では破棄しない。ReplaceUnityMesh は
            // MeshContext.DestroyMesh 経由で isPlaying を見て使い分ける。
            targetMc.ReplaceUnityMesh(newUnityMesh);

            if (_editOps?.UndoController != null && before != null)
            {
                var after = _editOps.UndoController.CaptureMeshObjectSnapshot();
                _editOps.UndoController.RecordTopologyChange(
                    before, after, $"Object Array into {targetMc.Name}");
            }

            model.ComputeWorldMatrices();
            PrimitiveMeshFinalize(model);
            panel?.SetStatus($"{targetMc.Name} へ {pieces.Count} 個ぶんを統合しました");
        }

        // ================================================================
        // 穴つなぎ（ブリッジ）
        // ================================================================

        /// <summary>
        /// 穴つなぎのコールバックを図形生成サブパネルへ配線する。
        /// 2つのインスタンスとも同じ処理を通す（状態はサブパネル側が個別に持つ）。
        /// </summary>
        /// <summary>
        /// 辺から帯面（高度な図形）のコールバックを図形生成パネルへ繋ぐ。
        ///
        /// ハンドラは 2 つのパネルで共有する。設定値（幅）はコマンドが持ち、
        /// 受け口が実行のたびに差し替えて元へ戻すので、状態を分ける必要がない。
        /// </summary>
        private void WireEdgeRibbonFaceCallbacks(PlayerPrimitiveMeshSubPanel panel)
        {
            if (panel == null) return;

            EnsureEdgeRibbonFaceHandler();

            panel.GetSelectedDrawableIndices = () =>
            {
                var sel = ActiveProject?.CurrentModel?.SelectedDrawableMeshIndices;
                return sel != null ? sel.ToArray() : System.Array.Empty<int>();
            };

            panel.GetSelectedEdgeCount = () =>
                _edgeRibbonFaceHandler?.GetSelectedEdgeCount() ?? 0;

            panel.BuildEdgeRibbonFaceMesh = width =>
            {
                if (_edgeRibbonFaceHandler == null) return null;
                return _edgeRibbonFaceHandler.Build(width, out var mo, out _) ? mo : null;
            };
        }

        /// <summary>
        /// 辺から帯面のハンドラを用意する。パネルの配線から呼ぶので、
        /// ハンドラ生成とパネル構築の順序に依存しない。
        /// 中身は _vertexHoleHandler と同じ形。
        /// </summary>
        private void EnsureEdgeRibbonFaceHandler()
        {
            if (_edgeRibbonFaceHandler != null) return;

            _edgeRibbonFaceHandler = new EdgeRibbonFaceToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
            };
            _edgeRibbonFaceHandler.SetProject(ActiveProject);
            _edgeRibbonFaceHandler.SetUndoController(_editOps?.UndoController);
            _edgeRibbonFaceHandler.SetCommandQueue(_editOps?.CommandQueue);
            _edgeRibbonFaceHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
        }

        private void WireBridgeCallbacks(PlayerPrimitiveMeshSubPanel panel)
        {
            if (panel == null) return;

            panel.PickBridgeSeeds      = PickHoleSeeds;
            panel.GetMeshObjectAt      = idx =>
                ActiveProject?.CurrentModel?.GetMeshContext(idx)?.MeshObject;
            panel.GetMeshNameAt        = idx =>
                ActiveProject?.CurrentModel?.GetMeshContext(idx)?.Name ?? $"#{idx}";
            // 頂点をワールドへ出す行列。スキンドは頂点が既にワールド（バインド）空間で、
            // かつ WorldMatrix は親ボーンのワールド行列なので、掛けると位置が飛ぶ。
            // 判定は MeshContext.IsSkinned に集約してある。
            panel.GetMeshWorldMatrixAt = idx =>
                ActiveProject?.CurrentModel?.GetMeshContext(idx)?.VertexToWorldMatrix ?? Matrix4x4.identity;
            // 自動選択の対象。頂点選択ではなく「選択中の描画オブジェクト」を見る。
            // 2つなら別々の物体、1つならその物体内の2つの穴が対象になる。
            panel.GetBridgeAutoMeshIndices = () =>
                ActiveProject?.CurrentModel?.SelectedDrawableMeshIndices;
            // 種の取り込み・破棄・図形切替でマーカーを即時更新する。
            // （視点変更・ホバー変更では PlayerViewportManager.RefreshToolOverlays が拾う）
            panel.OnBridgeSeedsChanged = UpdateTopologyToolsOverlay;
        }

        /// <summary>
        /// 選択中の描画オブジェクトを走査し、穴（エッジグループ）ごとに種を 1 つ拾う。
        /// 最大 2 件で打ち切る。範囲選択などで 1 つの穴に多数の頂点が入っていても、
        /// その穴からは 1 つだけを採る。
        ///
        /// 【拾えなかったとき】
        /// 空リストではなく Ok=false の要素を 1 つだけ入れて返す。理由をパネルの
        /// 情報欄へそのまま出すため。
        ///
        /// 【並びを固定する理由】
        /// SelectionState.Vertices / Edges は HashSet で列挙順が保証されない。
        /// 同じ選択で毎回同じ種が拾えるよう、頂点番号の昇順に並べ直してから走査する。
        /// </summary>
        private List<HoleSeedPick> PickHoleSeeds()
        {
            var picks = new List<HoleSeedPick>();

            HoleSeedPick Fail(string message)
                => new HoleSeedPick
                {
                    Ok = false, Message = message, MeshIndex = -1, Vertex = -1, DirectionHint = -1,
                };

            var model = ActiveProject?.CurrentModel;
            if (model == null) { picks.Add(Fail("モデルがありません")); return picks; }

            bool sawSelection = false;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                if (picks.Count >= 2) break;

                var mc  = model.GetMeshContext(idx);
                var mo  = mc?.MeshObject;
                var sel = mc?.Selection;
                if (mo == null || sel == null) continue;
                if (sel.Vertices.Count == 0 && sel.Edges.Count == 0) continue;
                sawSelection = true;

                // 穴の表はメッシュごとに 1 回だけ作る（頂点 → エッジグループ番号）。
                var groups = BoundaryEdgeOps.BuildGroups(BoundaryEdgeOps.CollectBoundaryEdges(mo));
                if (groups.Count == 0) continue;

                var groupOf = new Dictionary<int, int>();
                for (int g = 0; g < groups.Count; g++)
                    foreach (var be in groups[g])
                    {
                        if (!groupOf.ContainsKey(be.V1)) groupOf[be.V1] = g;
                        if (!groupOf.ContainsKey(be.V2)) groupOf[be.V2] = g;
                    }

                // 候補（種頂点, 進行方向ヒント）。辺は V1 を種、V2 を方向にする
                // （VertexPair は V1 <= V2 に正規化済みなので毎回同じ向きになる）。
                var cands = new List<(int Vertex, int Hint)>();
                foreach (int v in sel.Vertices) cands.Add((v, -1));
                foreach (var e in sel.Edges)    cands.Add((e.V1, e.V2));
                cands.Sort((a, b) => a.Vertex != b.Vertex
                    ? a.Vertex.CompareTo(b.Vertex)
                    : a.Hint.CompareTo(b.Hint));

                var takenGroups = new HashSet<int>();
                foreach (var c in cands)
                {
                    if (picks.Count >= 2) break;
                    if (c.Vertex < 0 || c.Vertex >= mo.VertexCount) continue;
                    if (!groupOf.TryGetValue(c.Vertex, out int gi)) continue;  // エッジ上にない頂点
                    if (!takenGroups.Add(gi)) continue;                        // その穴は採用済み

                    // 方向ヒントは同じ穴の頂点のときだけ活かす。
                    int hint = -1;
                    if (c.Hint >= 0 && groupOf.TryGetValue(c.Hint, out int gh) && gh == gi)
                        hint = c.Hint;

                    picks.Add(new HoleSeedPick
                    {
                        Ok = true, MeshIndex = idx, Vertex = c.Vertex, DirectionHint = hint,
                    });
                }
            }

            if (picks.Count == 0)
                picks.Add(Fail(sawSelection
                    ? "選択はエッジ（1面だけが使う辺）の上にありません"
                    : "エッジ上の頂点または辺を選択してください"));

            return picks;
        }

        // ================================================================
        // 辺群ブリッジのオーバーレイ
        // ================================================================

        /// <summary>辺群①の色。</summary>
        private static readonly Color EdgeBridgeColorA    = new Color(0.20f, 0.90f, 1.00f);
        /// <summary>辺群②の色。</summary>
        private static readonly Color EdgeBridgeColorB    = new Color(1.00f, 0.35f, 0.90f);
        /// <summary>2 群に分けられないときの警告色。</summary>
        private static readonly Color EdgeBridgeColorWarn = new Color(1.00f, 0.75f, 0.15f);

        /// <summary>
        /// 拾った辺を色分けして描く。
        ///
        /// 拾った辺は MeshContext.Selection に入れないため、GPU の選択色では出ない。
        /// 頂点のワールド座標は GPU の値（TryGetVertexWorld）を使う。
        /// スキニング規則を CPU で計算し直すと描画位置と食い違う。
        /// </summary>
        private void UpdateEdgeBridgeOverlay(PlayerViewportPanel panel, Poly_Ling.Tools.ToolContext ctx)
        {
            var h = _edgeBridgeHandler;
            if (h == null || h.PickedEdgeCount == 0 || h.PickedMeshIndex < 0)
            {
                panel.HideTopoToolOverlay();
                return;
            }

            var model = ActiveProject?.CurrentModel;
            var mc    = model?.GetMeshContext(h.PickedMeshIndex);
            if (model == null || mc?.MeshObject == null)
            {
                panel.HideTopoToolOverlay();
                return;
            }

            float hgt = ctx.PreviewRect.height;

            Vector2? ToScreen(int vertex)
            {
                if (vertex < 0 || vertex >= mc.MeshObject.VertexCount) return null;
                if (!_viewportManager.TryGetVertexWorld(model, mc, vertex, out var wp)) return null;
                var sp = ctx.WorldToScreen(wp);
                return new Vector2(sp.x, hgt - sp.y);
            }

            // 2 群に分けられるなら色分けし、分けられないなら全て警告色にする。
            var sum   = h.Inspect();
            var colorOf = new Dictionary<Poly_Ling.Selection.VertexPair, Color>();
            if (sum.Ok)
            {
                foreach (var e in sum.EdgesA) colorOf[e] = EdgeBridgeColorA;
                foreach (var e in sum.EdgesB) colorOf[e] = EdgeBridgeColorB;
            }

            var lines  = new List<(Vector2, Vector2, Color)>();
            var points = new List<(Vector2, Color, float)>();

            foreach (var e in h.PickedEdges)
            {
                var a = ToScreen(e.V1);
                var b = ToScreen(e.V2);
                if (!a.HasValue || !b.HasValue) continue;

                Color col = colorOf.TryGetValue(e, out var c) ? c : EdgeBridgeColorWarn;
                lines.Add((a.Value, b.Value, col));
                points.Add((a.Value, col, 3f));
                points.Add((b.Value, col, 3f));
            }

            if (lines.Count == 0) panel.HideTopoToolOverlay();
            else                  panel.UpdateTopoToolOverlay(lines, points, null);
        }

        // ================================================================
        // 穴の種マーカー（ブリッジ / 穴頂点数合わせで共通）
        // ================================================================

        /// <summary>A 側（ブリッジ＝穴A / 穴頂点数合わせ＝基準穴）の種マーカー色。</summary>
        private static readonly Color BridgeSeedColorA = new Color(0.20f, 0.90f, 1.00f);
        /// <summary>B 側（ブリッジ＝穴B / 穴頂点数合わせ＝対象穴）の種マーカー色。</summary>
        private static readonly Color BridgeSeedColorB = new Color(1.00f, 0.35f, 0.90f);

        /// <summary>
        /// 種マーカーを描くべきパネルを返す。
        /// 図形生成パネルが表示中でブリッジを選んでいるとき、または
        /// 穴頂点数合わせパネルが表示中のときに返す。どちらでもなければ null。
        /// </summary>
        private IHoleSeedSource ActiveHoleSeedSource()
        {
            if (_livePrimitiveSubPanel != null && _livePrimitiveSubPanel.HoleSeedOverlayActive)
                return _livePrimitiveSubPanel;
            if (_primitiveSubPanel != null && _primitiveSubPanel.HoleSeedOverlayActive)
                return _primitiveSubPanel;
            if (_holeRingCountSubPanel != null && _holeRingCountSubPanel.HoleSeedOverlayActive)
                return _holeRingCountSubPanel;
            return null;
        }

        /// <summary>
        /// 取り込み済みの種 A / B を色分けしてビューポートへ出す。
        /// 描けたら true、対象外なら false（呼出し側が他の分岐へ進む）。
        ///
        /// 【座標】頂点のワールド座標は GPU の値（TryGetVertexWorld → GetDisplayPositions）
        /// を使う。スキニング規則を CPU で計算し直すと描画位置と食い違う。
        /// </summary>
        private bool UpdateHoleSeedOverlay(PlayerViewportPanel panel, Poly_Ling.Tools.ToolContext ctx)
        {
            var seedSource = ActiveHoleSeedSource();
            if (seedSource == null) return false;

            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.HideTopoToolOverlay(); return true; }

            float h = ctx.PreviewRect.height;

            Vector2? SeedToScreen(int meshIndex, int vertex)
            {
                if (meshIndex < 0 || vertex < 0) return null;
                var mc = model.GetMeshContext(meshIndex);
                if (mc?.MeshObject == null) return null;
                if (!_viewportManager.TryGetVertexWorld(model, mc, vertex, out var wp)) return null;
                var sp = ctx.WorldToScreen(wp);
                return new Vector2(sp.x, h - sp.y);
            }

            var lines  = new List<(Vector2, Vector2, Color)>();
            var points = new List<(Vector2, Color, float)>();
            var rings  = new List<(Vector2, Color, float)>();

            void AddSeed(int meshIndex, int vertex, int hint, Color col)
            {
                var p = SeedToScreen(meshIndex, vertex);
                if (!p.HasValue) return;

                points.Add((p.Value, col, 6f));
                rings.Add((p.Value, col, 10f));

                // 辺で取り込んだ種は、進行方向側の頂点への線も同色で引く。
                if (hint < 0) return;
                var q = SeedToScreen(meshIndex, hint);
                if (q.HasValue) lines.Add((p.Value, q.Value, col));
            }

            AddSeed(seedSource.HoleSeedMeshIndexA, seedSource.HoleSeedVertexA,
                    seedSource.HoleSeedDirHintA, BridgeSeedColorA);
            AddSeed(seedSource.HoleSeedMeshIndexB, seedSource.HoleSeedVertexB,
                    seedSource.HoleSeedDirHintB, BridgeSeedColorB);

            if (points.Count == 0) panel.HideTopoToolOverlay();
            else                   panel.UpdateTopoToolOverlay(lines, points, rings);
            return true;
        }
    }
}
