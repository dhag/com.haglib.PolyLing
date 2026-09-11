// PolyLingPlayerViewerCore.Overlay.Tools.cs
// Player ビューアのコア：ツール別のオーバーレイ更新（面追加・辺トポロジー・位相ツール・詳細選択・ナイフ）。
// GPU 由来の座標を扱うときの禁止事項は UpdateAddFaceOverlay の直前にある。
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
        private void UpdateAddFaceOverlay()
        {
        // ================================================================
        // 【禁止事項】GPU 由来の座標を扱うときの拗らせ
        // ================================================================
        // 以下は実際に発生させた失敗である。繰り返さないこと。
        //
        // 1. 調べずに CPU 側で独自計算しない。
        //    GPU が _worldPositionBuffer にワールド座標を出しているのに、
        //    同じ規則を CPU で書き直すと、規則が食い違ったときに表示だけがずれる。
        //    まず GPU の値を使う経路を探すこと。
        //
        // 2.「今は呼ばれていないからできない」と決めつけない。
        //    呼び出し箇所が無いことは、呼び出しを足せない理由にならない。
        //    足せるかどうかを調べてから結論を出すこと。
        //
        // 3. カメラもモデルも動いていないのに読み戻しを毎フレーム呼ばない。
        //    WritebackTransformedVertices / GetWorldPositions は同期 GetData を伴う。
        //    ワールド座標が変わる契機（頂点移動・ボーン移動・再構築）でのみ更新し、
        //    ホバーのようにトポロジ・視点・頂点位置のいずれも変わらない操作では呼ばない。
        // ================================================================

            var panel = _activePanel;
            if (panel == null) return;

            if (_interactionMode != InteractionMode.AddFace || _addFaceHandler == null)
            {
                panel.HideAddFacePreview();
                return;
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideAddFacePreview(); return; }

            var data = _addFaceHandler.GetPreviewData();
            float h = ctx.PreviewRect.height;

            // PointInfo.Position はローカル座標。ctx（ToToolContext 由来）は Model を持たないので
            // 操作対象メッシュの WorldMatrix は実モデルから解決して適用する。
            var afModel = ActiveProject?.CurrentModel;
            var afMc = afModel?.ActiveMeshContext;
            var afL2W = afMc?.WorldMatrix ?? UnityEngine.Matrix4x4.identity;

            // AdvSel と完全に同じパターン:
            // ViewerCore側で h - sp.y を行い、Panel側で panelH - pt.y を行う。
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (local) =>
            {
                var sp = ctx.WorldToScreen(afL2W.MultiplyPoint3x4(local));
                return new UnityEngine.Vector2(sp.x, h - sp.y);
            };

            // 既存頂点を指す点は、GPU が計算済みのワールド座標を使う
            // （PlayerViewportManager.TryGetVertexWorld → GetDisplayPositions）。
            // スキニング規則を CPU 側で計算し直すと GPU の描画位置と食い違い、
            // マーカーだけがずれる。
            // 新規点の Position は AddFaceTool が ActiveWorldToLocal（= WorldMatrix の逆）で
            // 作っているので、こちらは WorldMatrix で往復させる（この往復は閉じている）。
            System.Func<int, UnityEngine.Vector3, UnityEngine.Vector2> vertexToScreen =
                (vertexIndex, fallbackLocal) =>
            {
                if (vertexIndex >= 0 && afModel != null && afMc != null &&
                    _viewportManager.TryGetVertexWorld(afModel, afMc, vertexIndex, out var wp))
                {
                    var spw = ctx.WorldToScreen(wp);
                    return new UnityEngine.Vector2(spw.x, h - spw.y);
                }
                return toScreen(fallbackLocal);
            };

            System.Func<Poly_Ling.Tools.PointInfo, UnityEngine.Vector2> pointToScreen = (pi) =>
                vertexToScreen(pi.IsExistingVertex ? pi.ExistingVertexIndex : -1, pi.Position);

            // プレビュー点。既存頂点にスナップしているときは PreviewVertexIndex が
            // その頂点を指すので、同じく GPU の座標を使う。
            System.Func<UnityEngine.Vector2> previewToScreen = () =>
                vertexToScreen(data.PreviewSnapped ? data.PreviewVertexIndex : -1, data.PreviewPoint);

            // 確定済み点
            var pts = new System.Collections.Generic.List<UnityEngine.Vector2>();
            foreach (var p in data.PlacedPoints)
                pts.Add(pointToScreen(p));

            // 線（配置済み点間）
            var lines = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Vector2)>();
            for (int i = 1; i < data.PlacedPoints.Length; i++)
                lines.Add((pointToScreen(data.PlacedPoints[i - 1]), pointToScreen(data.PlacedPoints[i])));

            // 連続線分モード開始点からプレビューへの線
            if (data.ContinuousLineStart.HasValue && data.PreviewValid)
                lines.Add((pointToScreen(data.ContinuousLineStart.Value), previewToScreen()));

            // 最後の確定済み点からプレビューへの線
            if (data.PlacedPoints.Length > 0 && data.PreviewValid)
                lines.Add((pointToScreen(data.PlacedPoints[data.PlacedPoints.Length - 1]), previewToScreen()));

            // プレビュー点
            // 非選択オブジェクトへの吸着は色を変えて区別する（既定のシアンは選択メッシュ用）。
            var previewPts  = new System.Collections.Generic.List<UnityEngine.Vector2>();
            var previewSnap = new System.Collections.Generic.List<bool>();
            var previewCols = new System.Collections.Generic.List<UnityEngine.Color?>();
            if (data.PreviewValid)
            {
                previewPts.Add(previewToScreen());
                previewSnap.Add(data.PreviewSnapped);
                previewCols.Add(data.PreviewSnappedUnselected
                    ? (UnityEngine.Color?)AddFaceUnselectedSnapColor
                    : null);
            }

            // Quad で3点配置済み、ホバーが1点目の既存頂点のときは開始点を強調する。
            int afHighlight = (data.CloseToStart && pts.Count > 0) ? 0 : -1;

            panel.UpdateAddFacePreview(pts, previewPts, previewSnap, lines, afHighlight, previewCols);
        }

        /// <summary>
        /// EdgeTopology の Split モード用オーバーレイ更新。
        ///
        /// 【設計ポイント: AddFace Overlay API の流用】
        /// 描画は AddFace 専用に作られた PlayerViewportPanel.UpdateAddFacePreview を
        /// そのまま借りて行う。AddFace と EdgeTopology-Split は InteractionMode が
        /// 排他なので同時描画の干渉がない。同一の Painter2D overlay を 2 ツールで共用すると
        /// 「確定点 + 候補ハイライト + マウスまでの線分」という汎用 UI が使い回せる。
        ///
        /// AddFace overlay API へのマッピング:
        ///   - pts         : 第 1 頂点 (確定後のみ。AddFace では配置済み点)
        ///   - lines       : 第 1 頂点 → マウス位置 (確定後のみ)
        ///   - previewPts  : ホバー頂点 または マウス位置 (単一プレビュー点)
        ///   - previewSnap : スナップ表示 (シアン大 + リング) の切替フラグ。以下参照
        ///
        /// 【previewSnap の 2 段階ロジック】
        /// AddFace は「頂点にピッタリ合ったとき」だけ snap=true にする。
        /// Split はクリック前後で意味を切り替えた:
        ///   - 第 1 頂点未確定時 (firstValid=false):
        ///       頂点にホバーしていれば無条件 snap=true
        ///       → 「これから開始点になる候補」を常に強調
        ///   - 第 1 頂点確定後 (firstValid=true):
        ///       ホバー頂点が SplitOpponentCandidates に含まれるときだけ snap=true
        ///       → 「対角に取れる頂点 = 有効な第 2 クリック先」だけを強調
        ///
        /// 【mo の取得: ctx.ActiveMeshObject は使えない】
        /// _viewportManager.GetCurrentToolContext() が返す ToolContext は Model を
        /// 設定しないため、ctx.ActiveMeshObject は常に null を返す罠がある。
        /// AddFace overlay は Handler.GetPreviewData() 経由で世界座標を受け取るため
        /// この罠を踏まないが、Split overlay は頂点座標そのものが必要なので
        /// ActiveProject から直接取得する必要がある。同じ手口で他のオーバーレイを
        /// 作るときも、ctx を世界座標変換 (WorldToScreen / PreviewRect) 専用と
        /// 割り切り、データは ActiveProject / Handler から取ること。
        ///
        /// 【Y 座標変換】
        /// ctx.WorldToScreen は UIToolkit Y (Y=0 上) を返すが、AddFace Overlay API は
        /// overlay Y=0 下を期待する。toScreen で (sp.x, h - sp.y) 変換をかける。
        /// マウス位置 (LastHoverScreenPos) は UpdateHover が UIToolkit Y で受け取って
        /// キャッシュしているので、同様に Y 反転が必要。
        /// </summary>
        private void UpdateEdgeTopologySplitOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;

            if (_edgeTopologyHandler == null
                || _edgeTopologyHandler.ModePublic != Poly_Ling.Tools.EdgeTopoMode.Split)
            {
                panel.HideAddFacePreview();
                return;
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideAddFacePreview(); return; }

            // ActiveProject から直接取る (ctx.ActiveMeshObject は上記注意点で null)
            var stMc = ActiveProject?.CurrentModel?.ActiveMeshContext;
            var mo = stMc?.MeshObject;
            if (mo == null) { panel.HideAddFacePreview(); return; }

            // Vertices[].Position はローカル座標なので WorldMatrix を適用してから投影する。
            var stL2W = stMc.WorldMatrix;
            float h = ctx.PreviewRect.height;
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (local) =>
            {
                var sp = ctx.WorldToScreen(stL2W.MultiplyPoint3x4(local));
                return new UnityEngine.Vector2(sp.x, h - sp.y);
            };

            int firstV    = _edgeTopologyHandler.SplitFirstVertex;
            int hoverV    = _edgeTopologyHandler.SplitHoverVertex;
            var candidates = _edgeTopologyHandler.SplitOpponentCandidates;

            var pts         = new System.Collections.Generic.List<UnityEngine.Vector2>();
            var previewPts  = new System.Collections.Generic.List<UnityEngine.Vector2>();
            var previewSnap = new System.Collections.Generic.List<bool>();
            var lines       = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Vector2)>();

            bool firstValid = firstV >= 0 && firstV < mo.VertexCount;
            bool hoverValid = hoverV >= 0 && hoverV < mo.VertexCount;

            // 確定点: 第 1 頂点
            if (firstValid)
                pts.Add(toScreen(mo.Vertices[firstV].Position));

            // プレビュー点: ホバー頂点があればその位置、なければマウス位置
            // マウス位置は UpdateHover が最後に受け取ったスクリーン座標
            // (UIToolkit Y=0 上) を IMGUI Y に変換して使う。
            UnityEngine.Vector2 previewPoint;
            bool previewSnapped;
            if (hoverValid)
            {
                previewPoint = toScreen(mo.Vertices[hoverV].Position);
                if (!firstValid)
                {
                    // 第 1 頂点未確定時: 頂点にホバーしているなら常にスナップ扱いにする。
                    // (「第 1 頂点に近づいたら大きめのまるで強調」という初期要件)
                    previewSnapped = true;
                }
                else
                {
                    // 第 1 頂点確定後: ホバー頂点が候補集合にあるときだけスナップ扱い
                    // (対向点候補をシアン大 + リングで強調)
                    previewSnapped = candidates != null && candidates.ContainsKey(hoverV);
                }
            }
            else
            {
                var lhp = _edgeTopologyHandler.LastHoverScreenPos;
                previewPoint = new UnityEngine.Vector2(lhp.x, h - lhp.y);
                previewSnapped = false;
            }
            previewPts.Add(previewPoint);
            previewSnap.Add(previewSnapped);

            // 線: 第 1 頂点 → プレビュー点 (確定後のみ)
            if (firstValid)
                lines.Add((toScreen(mo.Vertices[firstV].Position), previewPoint));

            panel.UpdateAddFacePreview(pts, previewPts, previewSnap, lines);
        }

        private void UpdateTopologyToolsOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;

            // Split モードは AddFace overlay API を流用して描画する (別経路)。
            // UpdateTopologyToolsOverlay が担う TopoToolOverlay (色付き線のみ) では
            // AddFace 相当の「確定点 + 候補ハイライト + マウス線」が描けないため、
            // AddFace 専用の Painter2D 経路 (UpdateAddFacePreview) を共用する。
            bool isEdgeTopo = (_interactionMode == InteractionMode.EdgeTopology && _edgeTopologyHandler != null);
            bool isSplit = isEdgeTopo && _edgeTopologyHandler.ModePublic == Poly_Ling.Tools.EdgeTopoMode.Split;
            if (isSplit)
            {
                UpdateEdgeTopologySplitOverlay();
                // TopoToolOverlay は空にして隠す (Flip/Dissolve 用の残留描画を防ぐ)
                panel.HideTopoToolOverlay();
                return;
            }
            // AddFace overlay は AddFace モード専用なので、Split 以外の EdgeTopology
            // モード (Flip/Dissolve) に入っているときは隠す。AddFace モード自体の
            // 管理は UpdateAddFaceOverlay 側に任せる。
            if (_interactionMode == InteractionMode.EdgeTopology
                && _edgeTopologyHandler != null
                && _edgeTopologyHandler.ModePublic != Poly_Ling.Tools.EdgeTopoMode.Split
                && _interactionMode != InteractionMode.AddFace)
            {
                panel.HideAddFacePreview();
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null)
            {
                panel.HideTopoToolOverlay();
                return;
            }

            // ── 辺群ブリッジ ─────────────────────────────────────────
            // 拾った辺は MeshContext.Selection に入らないため、GPU の選択色では
            // 出ない。ここで色分けして描くのが唯一の可視化経路。
            if (_interactionMode == InteractionMode.EdgeBridge)
            {
                UpdateEdgeBridgeOverlay(panel, ctx);
                return;
            }

            // ── 点指定図形 ─────────────────────────────────────────
            // 指定点と、分割数が一致して共有できる既存経路はここで描く。
            if (UpdatePointDefinedOverlay(panel, ctx)) return;

            // ── 穴の種マーカー（ブリッジ / 穴頂点数合わせ）─────────────
            // どちらも専用の InteractionMode を持たず、パネルを開いたままの
            // SelectOnly / None / PrimitivePlace で操作する。以降の分岐は
            // どれも該当せず末尾の HideTopoToolOverlay() まで落ちるため、
            // ここで先に横取りする。
            if (UpdateHoleSeedOverlay(panel, ctx)) return;

            // ── 格子変形 ─────────────────────────────────────────────
            // 格子の線と制御点はメッシュ頂点ではなく作業軸ローカルの制御点を
            // 投影したもの。組み立ては LatticeToolHandler が持つ。
            // 作業軸モードでもセッションが生きている間は描き続ける
            // （格子フレームを動かしている最中に格子が消えないようにする）。
            bool latticeOpen = _latticeHandler != null
                && _latticeHandler.State != LatticeToolHandler.LatticeState.Idle
                && (_interactionMode == InteractionMode.Lattice
                 || _interactionMode == InteractionMode.WorkAxis);

            if (latticeOpen || _interactionMode == InteractionMode.Lattice)
            {
                if (latticeOpen
                    && _latticeHandler.TryBuildOverlay(ctx, out var latLines, out var latPoints))
                    panel.UpdateTopoToolOverlay(latLines, latPoints, null);
                else
                    panel.HideTopoToolOverlay();
                return;
            }

            var mo = ctx.ActiveMeshObject;
            float h = ctx.PreviewRect.height;

            // Vertices[].Position はローカル座標。ctx の操作対象メッシュの WorldMatrix を適用する。
            // LocalToScreen: AddFaceOverlay と同じ変換 (h - sp.y)
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (local) =>
            {
                var sp = ctx.LocalToScreen(local);
                return new UnityEngine.Vector2(sp.x, h - sp.y);
            };

            var lines = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Vector2, UnityEngine.Color)>();

            // ── EdgeBevel ─────────────────────────────────────────────────
            if (_interactionMode == InteractionMode.EdgeBevel && _edgeBevelHandler != null && mo != null)
            {
                var edge = _edgeBevelHandler.HoverEdge;
                if (edge.HasValue)
                {
                    int v0 = edge.Value.V1, v1 = edge.Value.V2;
                    if (v0 >= 0 && v0 < mo.VertexCount && v1 >= 0 && v1 < mo.VertexCount)
                        lines.Add((toScreen(mo.Vertices[v0].Position),
                                   toScreen(mo.Vertices[v1].Position),
                                   UnityEngine.Color.white));
                }
                panel.UpdateTopoToolOverlay(lines);
                return;
            }

            // ── EdgeExtrude ───────────────────────────────────────────────
            if (_interactionMode == InteractionMode.EdgeExtrude && _edgeExtrudeHandler != null && mo != null)
            {
                var edge = _edgeExtrudeHandler.HoverEdge;
                if (edge.HasValue)
                {
                    int v0 = edge.Value.V1, v1 = edge.Value.V2;
                    if (v0 >= 0 && v0 < mo.VertexCount && v1 >= 0 && v1 < mo.VertexCount)
                        lines.Add((toScreen(mo.Vertices[v0].Position),
                                   toScreen(mo.Vertices[v1].Position),
                                   new UnityEngine.Color(0.2f, 0.8f, 1f)));
                }
                panel.UpdateTopoToolOverlay(lines);
                return;
            }

            // ── FaceExtrude ───────────────────────────────────────────────
            if (_interactionMode == InteractionMode.FaceExtrude && _faceExtrudeHandler != null && mo != null)
            {
                int fi = _faceExtrudeHandler.HoverFace;
                if (fi >= 0 && fi < mo.FaceCount)
                {
                    var face = mo.Faces[fi];
                    int n = face.VertexIndices.Count;
                    for (int i = 0; i < n; i++)
                    {
                        int va = face.VertexIndices[i];
                        int vb = face.VertexIndices[(i + 1) % n];
                        if (va >= 0 && va < mo.VertexCount && vb >= 0 && vb < mo.VertexCount)
                            lines.Add((toScreen(mo.Vertices[va].Position),
                                       toScreen(mo.Vertices[vb].Position),
                                       new UnityEngine.Color(1f, 1f, 1f, 0.7f)));
                    }
                }
                panel.UpdateTopoToolOverlay(lines);
                return;
            }

            // ── EdgeTopology (Flip / Dissolve) ───────────────────────────
            // Split モードはメソッド冒頭で UpdateEdgeTopologySplitOverlay() に分岐済み。
            // ここに到達するのは Flip/Dissolve モードのみ。辺ホバーを黄色線で示す。
            if (_interactionMode == InteractionMode.EdgeTopology && _edgeTopologyHandler != null && mo != null)
            {
                if (_edgeTopologyHandler.HasHoverEdge)
                {
                    int v0 = _edgeTopologyHandler.HoverEdgeV1;
                    int v1 = _edgeTopologyHandler.HoverEdgeV2;
                    if (v0 >= 0 && v0 < mo.VertexCount && v1 >= 0 && v1 < mo.VertexCount)
                        lines.Add((toScreen(mo.Vertices[v0].Position),
                                   toScreen(mo.Vertices[v1].Position),
                                   new UnityEngine.Color(1f, 0.8f, 0.2f)));
                }

                panel.UpdateTopoToolOverlay(lines);
                return;
            }

            // ── 頂点溶解 / 三角形4→1 / 面結合 ─────────────────────────────
            // マウス直下の要素だけを実行可否で色分けする（緑=実行できる、赤=できない）。
            // 実行できるときは、その操作で消える面の外周も同色で描いて影響範囲を示す。
            // 候補を全部塗る方式は採らない（内部の頂点・辺はほぼ全部候補になるため）。
            if ((_interactionMode == InteractionMode.VertexDissolve
              || _interactionMode == InteractionMode.Tri4To1
              || _interactionMode == InteractionMode.FaceMerge
              || _interactionMode == InteractionMode.Quad4To1) && mo != null)
            {
                var points   = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Color, float)>();
                var okColor  = new UnityEngine.Color(0.2f, 1f, 0.35f);
                var ngColor  = new UnityEngine.Color(1f, 0.3f, 0.25f);
                var hoverModel = ActiveProject?.CurrentModel;

                // 面の外周を線として積む。
                void AddFaceOutline(int faceIndex, UnityEngine.Color col)
                {
                    if (faceIndex < 0 || faceIndex >= mo.FaceCount) return;
                    var f = mo.Faces[faceIndex];
                    int n = f.VertexIndices.Count;
                    if (n < 2) return;
                    for (int i = 0; i < n; i++)
                    {
                        int va = f.VertexIndices[i];
                        int vb = f.VertexIndices[(i + 1) % n];
                        if (va < 0 || va >= mo.VertexCount || vb < 0 || vb >= mo.VertexCount) continue;
                        lines.Add((toScreen(mo.Vertices[va].Position),
                                   toScreen(mo.Vertices[vb].Position), col));
                    }
                }

                // (v0,v1) を辺として持つ面を列挙する（2頂点の線分は除く）。
                System.Collections.Generic.List<int> FacesOnEdge(int v0, int v1)
                {
                    var result = new System.Collections.Generic.List<int>();
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var f = mo.Faces[fi];
                        int n = f.VertexIndices.Count;
                        if (n < 3) continue;
                        for (int i = 0; i < n; i++)
                        {
                            int a = f.VertexIndices[i];
                            int b = f.VertexIndices[(i + 1) % n];
                            if ((a == v0 && b == v1) || (a == v1 && b == v0)) { result.Add(fi); break; }
                        }
                    }
                    return result;
                }

                bool sameMesh(PlayerHoverElement e) =>
                    hoverModel != null && e.MeshIndex == hoverModel.ActiveMeshIndex;

                if (_interactionMode == InteractionMode.Quad4To1)
                {
                    var elem = _viewportManager.GetHoverElement(MeshSelectMode.Vertex, hoverModel);
                    int v = elem.VertexIndex;
                    if (elem.Kind == PlayerHoverKind.Vertex && sameMesh(elem)
                        && v >= 0 && v < mo.VertexCount)
                    {
                        var info = Quad4To1Ops.Inspect(mo, v);
                        var col  = info.CanExecute ? okColor : ngColor;

                        // 実行できるときは、1枚に統合される四角形4枚の外周を描く。
                        if (info.CanExecute)
                        {
                            for (int fi = 0; fi < mo.FaceCount; fi++)
                                if (mo.Faces[fi].VertexIndices.Contains(v)) AddFaceOutline(fi, col);
                        }
                        points.Add((toScreen(mo.Vertices[v].Position), col, 7f));
                    }
                }
                else if (_interactionMode == InteractionMode.VertexDissolve)
                {
                    var elem = _viewportManager.GetHoverElement(MeshSelectMode.Vertex, hoverModel);
                    int v = elem.VertexIndex;
                    if (elem.Kind == PlayerHoverKind.Vertex && sameMesh(elem)
                        && v >= 0 && v < mo.VertexCount)
                    {
                        var info = VertexDissolveOps.Inspect(mo, v);
                        var col  = info.CanExecute ? okColor : ngColor;

                        // 実行できるときは、1枚に統合される面の外周を描く。
                        if (info.CanExecute)
                        {
                            for (int fi = 0; fi < mo.FaceCount; fi++)
                                if (mo.Faces[fi].VertexIndices.Contains(v)) AddFaceOutline(fi, col);
                        }
                        points.Add((toScreen(mo.Vertices[v].Position), col, 7f));
                    }
                }
                else if (_interactionMode == InteractionMode.Tri4To1)
                {
                    var elem = _viewportManager.GetHoverElement(MeshSelectMode.Face, hoverModel);
                    int fi0  = elem.FaceIndex;
                    if (elem.Kind == PlayerHoverKind.Face && sameMesh(elem)
                        && fi0 >= 0 && fi0 < mo.FaceCount)
                    {
                        var info = Tri4To1Ops.Inspect(mo, fi0);
                        var col  = info.CanExecute ? okColor : ngColor;

                        AddFaceOutline(fi0, col);

                        // 実行できるときは、一緒に消える囲みの3枚も描く。
                        if (info.CanExecute)
                        {
                            var f = mo.Faces[fi0];
                            int n = f.VertexIndices.Count;
                            for (int i = 0; i < n; i++)
                            {
                                var nb = FacesOnEdge(f.VertexIndices[i], f.VertexIndices[(i + 1) % n]);
                                foreach (int g in nb) if (g != fi0) AddFaceOutline(g, col);
                            }
                        }
                    }
                }
                else
                {
                    var elem = _viewportManager.GetHoverElement(MeshSelectMode.Edge, hoverModel);
                    int v0 = elem.EdgeV1, v1 = elem.EdgeV2;
                    if (elem.Kind == PlayerHoverKind.Edge && sameMesh(elem)
                        && v0 >= 0 && v0 < mo.VertexCount && v1 >= 0 && v1 < mo.VertexCount)
                    {
                        // 可否はパネルの「頂点を削除する」に合わせた Ops で調べる。
                        var pair = new VertexPair(v0, v1);
                        bool canMerge = (_faceMergeHandler?.DeleteVertices ?? true)
                            ? FaceMergeCollapseOps.Inspect(mo, pair).CanExecute
                            : FaceMergeOps.Inspect(mo, pair).CanExecute;
                        var col  = canMerge ? okColor : ngColor;

                        // 実行できるときは、結合される2枚の外周を描く。
                        if (canMerge)
                            foreach (int g in FacesOnEdge(v0, v1)) AddFaceOutline(g, col);

                        lines.Add((toScreen(mo.Vertices[v0].Position),
                                   toScreen(mo.Vertices[v1].Position), col));
                        points.Add((toScreen(mo.Vertices[v0].Position), col, 5f));
                        points.Add((toScreen(mo.Vertices[v1].Position), col, 5f));
                    }
                }

                panel.UpdateTopoToolOverlay(lines, points);
                return;
            }

            panel.HideTopoToolOverlay();
        }

        private void UpdateAdvancedSelectOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;

            // ナイフ（ラダー切断）は同じプレビューチャネル（点＋線）を流用する。
            if (_interactionMode == InteractionMode.Knife)
            {
                UpdateKnifePreviewInto(panel);
                return;
            }

            if (_interactionMode != InteractionMode.AdvancedSelect || _advancedSelectHandler == null)
            {
                panel.HideAdvSelPreview();
                return;
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideAdvSelPreview(); return; }

            var previewCtx = _advancedSelectHandler.GetPreviewContext();
            if (previewCtx == null) { panel.HideAdvSelPreview(); return; }

            // ctx（ToToolContext 由来）は Model を持たないため FirstSelectedMeshObject が null。
            // 操作対象メッシュは実モデルから解決する（投影は ctx.WorldToScreen を使用）。
            var ovModel = ActiveProject?.CurrentModel;
            var ovMc = ovModel?.ActiveMeshContext;
            var mo = ovMc?.MeshObject;
            if (mo == null) { panel.HideAdvSelPreview(); return; }

            // Vertices[].Position はローカル座標。WorldMatrix 適用後に投影する。
            var ovL2W = ovMc.WorldMatrix;
            System.Func<Vector3, Vector2> ovToScreen =
                (local) => ctx.WorldToScreen(ovL2W.MultiplyPoint3x4(local));

            var pts = new System.Collections.Generic.List<Vector2>();
            var verts = previewCtx.PreviewVertices;
            if (verts != null)
                foreach (int vi in verts)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    var sp = ovToScreen(mo.Vertices[vi].Position);
                    pts.Add(new Vector2(sp.x, ctx.PreviewRect.height - sp.y));
                }
            var path = previewCtx.PreviewPath;
            if (path != null)
                foreach (int vi in path)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    var sp = ovToScreen(mo.Vertices[vi].Position);
                    pts.Add(new Vector2(sp.x, ctx.PreviewRect.height - sp.y));
                }

            var lines = new System.Collections.Generic.List<(Vector2, Vector2)>();
            var edges = previewCtx.PreviewEdges;
            if (edges != null)
                foreach (var e in edges)
                {
                    if (e.V1 < 0 || e.V1 >= mo.VertexCount || e.V2 < 0 || e.V2 >= mo.VertexCount) continue;
                    var s1 = ovToScreen(mo.Vertices[e.V1].Position);
                    var s2 = ovToScreen(mo.Vertices[e.V2].Position);
                    float h = ctx.PreviewRect.height;
                    lines.Add((new Vector2(s1.x, h - s1.y), new Vector2(s2.x, h - s2.y)));
                }
            if (path != null && path.Count > 1)
                for (int i = 0; i < path.Count - 1; i++)
                {
                    int v1 = path[i], v2 = path[i + 1];
                    if (v1 < 0 || v1 >= mo.VertexCount || v2 < 0 || v2 >= mo.VertexCount) continue;
                    var s1 = ovToScreen(mo.Vertices[v1].Position);
                    var s2 = ovToScreen(mo.Vertices[v2].Position);
                    float h = ctx.PreviewRect.height;
                    lines.Add((new Vector2(s1.x, h - s1.y), new Vector2(s2.x, h - s2.y)));
                }

            // 強調マーカー：最短＝始点、その他＝クリック点／辺のフラッシュ
            Vector2? firstPt = null;
            (Vector2, Vector2)? firstEdge = null;
            int emphVertex = -1;
            if (_advancedSelectHandler.Mode == Poly_Ling.Tools.AdvancedSelectMode.ShortestPath)
                emphVertex = _advancedSelectHandler.GetShortestPathFirstVertex();
            else if (_advSelFlashEdge.HasValue)
            {
                var e = _advSelFlashEdge.Value;
                if (e.V1 >= 0 && e.V1 < mo.VertexCount && e.V2 >= 0 && e.V2 < mo.VertexCount)
                {
                    var s1 = ovToScreen(mo.Vertices[e.V1].Position);
                    var s2 = ovToScreen(mo.Vertices[e.V2].Position);
                    float h = ctx.PreviewRect.height;
                    firstEdge = (new Vector2(s1.x, h - s1.y), new Vector2(s2.x, h - s2.y));
                }
            }
            else if (_advSelFlashVertex >= 0)
                emphVertex = _advSelFlashVertex;

            if (emphVertex >= 0 && emphVertex < mo.VertexCount)
            {
                var fsp = ovToScreen(mo.Vertices[emphVertex].Position);
                firstPt = new Vector2(fsp.x, ctx.PreviewRect.height - fsp.y);
            }

            panel.UpdateAdvSelPreview(pts, lines, _advancedSelectHandler.AddToSelection, firstPt, firstEdge);
        }

        /// <summary>
        /// ナイフ（ラダー切断）のプレビューを AdvSel プレビューチャネルへ流し込む。
        /// 確定済アンカー点・ラング中点・切断線を現在の視点で再投影する。
        /// </summary>
        private void UpdateKnifePreviewInto(PlayerViewportPanel panel)
        {
            if (_knifeHandler == null) { panel.HideAdvSelPreview(); return; }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideAdvSelPreview(); return; }

            // ctx（ToToolContext 由来）は Model を持たないため、操作対象メッシュは実モデルから解決する。
            var kfModel = ActiveProject?.CurrentModel;
            var kfMc = kfModel?.ActiveMeshContext;
            var mo = kfMc?.MeshObject;
            if (mo == null) { panel.HideAdvSelPreview(); return; }

            var prev = _knifeHandler.GetPreview();
            if (prev == null) { panel.HideAdvSelPreview(); return; }

            // prev.DotWorld / prev.Lines はワールド座標（KnifeTool.VW が GPU 値で構築）。
            // ここで行列を掛けてはならない。掛けると二重変換になる。
            float h = ctx.PreviewRect.height;
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (world) =>
            {
                var sp = ctx.WorldToScreen(world);
                return new UnityEngine.Vector2(sp.x, h - sp.y);
            };

            // 頂点インデックスで渡される点は GPU の値を直接引く。
            System.Func<int, UnityEngine.Vector2?> vertexToScreen = (vi) =>
            {
                if (vi < 0 || vi >= mo.VertexCount) return null;
                if (!_viewportManager.TryGetVertexWorld(kfModel, kfMc, vi, out var wp)) return null;
                return toScreen(wp);
            };

            var pts = new System.Collections.Generic.List<UnityEngine.Vector2>();
            foreach (int vi in prev.DotVertices)
            {
                var p2 = vertexToScreen(vi);
                if (p2.HasValue) pts.Add(p2.Value);
            }
            foreach (var w in prev.DotWorld)
                pts.Add(toScreen(w));

            var lines = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Vector2)>();
            foreach (var seg in prev.Lines)
                lines.Add((toScreen(seg.Item1), toScreen(seg.Item2)));

            // SimpleCut: 画面座標で指定された点/線はそのまま追加（投影不要）。
            foreach (var d in prev.ScreenDots)
                pts.Add(d);
            foreach (var s in prev.ScreenLines)
                lines.Add((s.Item1, s.Item2));

            // クリック点フラッシュ強調（AdvSel と共通：辺＝太線／頂点＝リング）
            Vector2? firstPt = null;
            (Vector2, Vector2)? firstEdge = null;
            if (_advSelFlashEdge.HasValue)
            {
                var e = _advSelFlashEdge.Value;
                var a2 = vertexToScreen(e.V1);
                var b2 = vertexToScreen(e.V2);
                if (a2.HasValue && b2.HasValue) firstEdge = (a2.Value, b2.Value);
            }
            else if (_advSelFlashVertex >= 0)
                firstPt = vertexToScreen(_advSelFlashVertex);

            panel.UpdateAdvSelPreview(pts, lines, prev.PlanValid, firstPt, firstEdge);
        }
    }
}
