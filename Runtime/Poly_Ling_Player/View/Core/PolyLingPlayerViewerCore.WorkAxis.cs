// PolyLingPlayerViewerCore.WorkAxis.cs
// Player ビューアのコア：作業用ローカル軸（WorkAxis）。
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
        // 作業用ローカル軸（WorkAxis）
        // ================================================================

        /// <summary>
        /// 現在のモデルの作業軸。モデル未選択なら null。
        /// ModelContext.WorkAxis は既定でインスタンスを持つが、
        /// 旧データから復元した ModelContext は null のことがあるためここで補う。
        /// </summary>
        private Poly_Ling.Context.WorkAxisContext CurrentWorkAxis()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return null;
            if (model.WorkAxis == null) model.WorkAxis = new Poly_Ling.Context.WorkAxisContext();
            return model.WorkAxis;
        }

        /// <summary>作業軸ハンドルがボーンへ吸着する当たり半径（px）。</summary>
        private const float WorkAxisBoneSnapRadius = 10f;

        /// <summary>
        /// 作業軸ハンドル（原点 / Y 先端）の吸着先ワールド座標。無ければ null。
        ///
        /// 引数はギズモ判定と同じ ctx 系スクリーン座標（ハンドラ側で ToImgui 済み）。
        /// 頂点は選択されていないオブジェクトも対象にしたいため、通常ホバーではなく
        /// 吸着用 GPU ヒットテスト（GetSnapHoverElement）を使う。座標は GPU が計算した
        /// 表示位置（TryGetVertexWorld）から取る。CPU で WorldMatrix を掛け直すと
        /// スキニング済みメッシュで表示とずれる。
        ///
        /// ボーンは GPU の描画要素ではないので、ボーンオーバーレイと同じく
        /// MeshContext.WorldMatrix の平行移動成分を投影して最近傍を採る。
        /// 走査するのは MeshContext の数だけで、頂点は走査しない。
        /// 頂点が取れたときはそちらを優先する。
        /// </summary>
        private Vector3? WorkAxisSnapTargetWorld(Vector2 imguiPos)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return null;

            // ---- 頂点（GPU 吸着ヒットテスト） ----
            var elem = _viewportManager.GetSnapHoverElement(model);
            if (elem.Kind == PlayerHoverKind.Vertex && elem.MeshIndex >= 0)
            {
                var vmc = model.GetMeshContext(elem.MeshIndex);
                if (vmc != null &&
                    _viewportManager.TryGetVertexWorld(model, vmc, elem.VertexIndex, out var vw))
                    return vw;
            }

            // ---- ボーン（CPU 最近傍） ----
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) return null;

            float    best  = WorkAxisBoneSnapRadius;
            Vector3? found = null;

            for (int i = 0; i < model.Count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;

                var     wm = mc.WorldMatrix;
                Vector3 wp = new Vector3(wm.m03, wm.m13, wm.m23);

                float d = Vector2.Distance(imguiPos, ctx.WorldToScreen(wp));
                if (d < best) { best = d; found = wp; }
            }

            return found;
        }

        /// <summary>
        /// 選択中の全ドローアブルメッシュにまたがる選択頂点の重心（ワールド座標）。
        /// 選択が無ければ null。
        ///
        /// 座標は GPU が計算した表示位置（PlayerViewportManager.TryGetVertexWorld →
        /// GetDisplayPositions）から取る。CPU 側で WorldMatrix を掛け直すと
        /// スキニング済みメッシュで GPU 表示とずれるため、独自計算はしない。
        /// </summary>
        private Vector3? SelectedVerticesCentroidWorld()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return null;

            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (int meshIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(meshIdx);
                var mo = mc?.MeshObject;
                if (mo == null || !mc.HasSelection) continue;

                foreach (int vi in EnumerateSelectedVertexIndices(mc, mo))
                {
                    if (_viewportManager.TryGetVertexWorld(model, mc, vi, out var w))
                    {
                        sum += w;
                        count++;
                    }
                }
            }

            return count > 0 ? (Vector3?)(sum / count) : null;
        }

        /// <summary>
        /// MeshContext の選択（頂点 / 辺 / 面 / 線分）から影響頂点インデックスを列挙する。
        /// 重複は呼び出し側で気にしなくてよいよう HashSet で畳む。
        /// </summary>
        private static HashSet<int> EnumerateSelectedVertexIndices(
            Poly_Ling.Data.MeshContext mc, Poly_Ling.Data.MeshObject mo)
        {
            var set = new HashSet<int>();
            if (mc == null || mo == null) return set;

            if (mc.SelectedVertices != null)
                foreach (int v in mc.SelectedVertices) set.Add(v);

            if (mc.SelectedEdges != null)
                foreach (var e in mc.SelectedEdges) { set.Add(e.V1); set.Add(e.V2); }

            if (mc.SelectedFaces != null)
                foreach (int fi in mc.SelectedFaces)
                    if (fi >= 0 && fi < mo.FaceCount)
                        foreach (int v in mo.Faces[fi].VertexIndices) set.Add(v);

            if (mc.SelectedLines != null)
                foreach (int li in mc.SelectedLines)
                    if (li >= 0 && li < mo.FaceCount)
                    {
                        var f = mo.Faces[li];
                        if (f.VertexCount == 2)
                        {
                            set.Add(f.VertexIndices[0]);
                            set.Add(f.VertexIndices[1]);
                        }
                    }

            return set;
        }

        /// <summary>
        /// 作業軸パネルを開く。カテゴリ 1（3D 操作と右ペインが一体）。
        /// </summary>
        private void ShowWorkAxisPanel()
        {
            ShowCategory1Panel(InteractionMode.WorkAxis);
            UpdateGizmoOverlay();
        }

        /// <summary>
        /// デフォーマパネルを開く。カテゴリ 1（3D 操作と右ペインが一体）。
        /// 作業軸は「作業軸」パネルと共有するため、ここでは編集しない。
        /// </summary>
        private void ShowDeformPanel()
        {
            ShowCategory1Panel(InteractionMode.Deform);
            UpdateGizmoOverlay();
        }

        /// <summary>
        /// 格子変形パネルを開く。カテゴリ 1（3D 操作と右ペインが一体）。
        /// 格子フレームは「作業軸」パネルと共有するため、ここでは編集しない。
        /// </summary>
        private void ShowLatticePanel()
        {
            ShowCategory1Panel(InteractionMode.Lattice);
            UpdateTopologyToolsOverlay();
            UpdateGizmoOverlay();
        }

        private void ShowRotatePanel()
        {
            // カテゴリ 1 化: MoveToolHandler の選択/矩形選択を流用。
            // 現状の回転実行はサブパネルのスライダ経由のまま。
            // 将来的には独自形状ギズモ (回転リング) をビューポートに表示し、
            // MoveToolHandler のフック (OnDragStartExtra 等) 経由で回転操作を
            // 実現する予定。
            ShowCategory1Panel(InteractionMode.Rotate);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _rotateHandler?.Activate(ctx);
        }

        private void ShowScalePanel()
        {
            // カテゴリ 1 化: MoveToolHandler の選択/矩形選択を流用。
            // 現状の拡大縮小実行はサブパネルのスライダ経由のまま。
            // 将来的には独自形状ギズモ (軸端ハンドル等) をビューポートに表示し、
            // MoveToolHandler のフック経由で拡大縮小操作を実現する予定。
            ShowCategory1Panel(InteractionMode.Scale);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _scaleHandler?.Activate(ctx);
        }

        private void ShowEdgeBevelPanel()
        {
            ShowCategory1Panel(InteractionMode.EdgeBevel);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _edgeBevelHandler?.Activate(ctx);
        }

        private void ShowEdgeExtrudePanel()
        {
            ShowCategory1Panel(InteractionMode.EdgeExtrude);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _edgeExtrudeHandler?.Activate(ctx);
        }

        private void ShowFaceExtrudePanel()
        {
            ShowCategory1Panel(InteractionMode.FaceExtrude);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _faceExtrudeHandler?.Activate(ctx);
        }

        private void ShowEdgeTopologyPanel()
        {
            ShowCategory1Panel(InteractionMode.EdgeTopology);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _edgeTopologyHandler?.Activate(ctx);
        }

        private void ShowSolidifyPanel()
        {
            // カテゴリ 1 化: MoveToolHandler の選択/矩形選択を流用し、Selection.Mode を
            // Face のみに絞る。厚み付けの実行はサブパネル経由。
            ShowCategory1Panel(InteractionMode.Solidify);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _solidifyHandler?.Activate(ctx);
        }

        private void ShowKnifePanel()
        {
            ShowCategory1Panel(InteractionMode.Knife);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _knifeHandler?.Activate(ctx);
            // Activate は SetInteractionMode の後に走り、段（開始頂点/セグメント辺）を
            // 初期化し得る。初期段に合わせて override を確定させる。
            _knifeHandler?.ApplyHoverSelectionMode();
        }
        private void ShowAddFacePanel()
        {
            ShowCategory1Panel(InteractionMode.AddFace);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _addFaceHandler?.Activate(ctx);
            // 面追加時は頂点ホバーのみ必要（辺・面のホバーは有害）。
            // 絞り込みは ShowCategory1Panel → SetInteractionMode(AddFace) →
            // ResolveToolSelectModeOverride が行うため、ここでは書かない。
        }

        private void ShowLineExtrudePanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。右ペインのみ切替。
            // 線分の選択は既存のツールで行い、このパネルは実行だけを持つ。
            ShowRightPanel(_layoutRoot?.LineExtrudeSection, _layoutRoot?.LineExtrudeBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _lineExtrudeHandler?.Activate(ctx);
            _lineExtrudeSubPanel?.Refresh();
        }

        private void ShowSplitVerticesPanel()
        {
            // カテゴリ 2
            ShowRightPanel(_layoutRoot?.SplitVerticesSection, _layoutRoot?.SplitVerticesBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _splitVerticesHandler?.Activate(ctx);
            _splitVerticesSubPanel?.Refresh();
        }

        private void ShowVertexHolePanel()
        {
            // 選択許可チェック（既定 ON なら SelectOnly で開く）
            ShowRightPanelSelectable(
                _layoutRoot?.VertexHoleSection, _layoutRoot?.VertexHoleBtn, PanelSelectKeyVertexHole);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _vertexHoleHandler?.Activate(ctx);
            _vertexHoleSubPanel?.Refresh();
        }

        private void ShowHoleRingCountPanel()
        {
            // カテゴリ 3（選択許可チェック付き）。ブリッジと同じく専用の
            // InteractionMode は持たず、頂点／辺を選んでからボタンで取り込む。
            ShowRightPanelSelectable(
                _layoutRoot?.HoleRingCountSection, _layoutRoot?.HoleRingCountBtn,
                PanelSelectKeyHoleRingCount);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _holeRingCountHandler?.Activate(ctx);
            _holeRingCountSubPanel?.Refresh();
        }

        /// <summary>
        /// 辺群ブリッジ。カテゴリ 1（3D 操作と右ペインが一体）。
        /// ナイフと同じく専用の InteractionMode を持ち、ビューポートのクリック／
        /// ドラッグを EdgeBridgeToolHandler が受け取る。
        /// </summary>
        private void ShowEdgeBridgePanel()
        {
            ShowCategory1Panel(InteractionMode.EdgeBridge);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _edgeBridgeHandler?.Activate(ctx);
            _edgeBridgeSubPanel?.Refresh();
        }

        // カテゴリ 1（3D 操作と右ペインが一体）。頂点／面／辺のクリックで即実行する
        // 専用モードへ入る。Selection.Mode の固定とフック設定は SetInteractionMode 側。
        private void ShowVertexDissolvePanel()
        {
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _vertexDissolveHandler?.Activate(ctx);
            ShowCategory1Panel(InteractionMode.VertexDissolve);
        }

        private void ShowTri4To1Panel()
        {
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _tri4To1Handler?.Activate(ctx);
            ShowCategory1Panel(InteractionMode.Tri4To1);
        }

        private void ShowFaceMergePanel()
        {
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _faceMergeHandler?.Activate(ctx);
            ShowCategory1Panel(InteractionMode.FaceMerge);
        }

        private void ShowQuad4To1Panel()
        {
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _quad4To1Handler?.Activate(ctx);
            ShowCategory1Panel(InteractionMode.Quad4To1);
        }

        private void ShowVertexIdPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。
            // 診断・修復は選択中オブジェクトに対する即時操作で、ビューポート入力は使わない。
            ShowRightPanel(_layoutRoot?.VertexIdSection, _layoutRoot?.VertexIdBtn);
            _vertexIdSubPanel?.Refresh();
        }

        private void ShowPartsIdPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。
            // 採番は選んだオブジェクトへの即時操作で、ビューポート入力は使わない。
            ShowRightPanel(_layoutRoot?.PartsIdSection, _layoutRoot?.PartsIdBtn);
            _partsIdSubPanel?.Refresh();
        }

        private void ShowVertexTransferPanel()
        {
            // カテゴリ 3: モデル間の操作でビューポート入力を使わないため 3D 操作は落とす。
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.VertexTransferSection, _layoutRoot?.VertexTransferBtn);
            _vertexTransferSubPanel?.Refresh();
        }

        private void ShowMediaPipePanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MediaPipeSection, _layoutRoot?.MediaPipeBtn);
            _mediaPipeSubPanel?.Refresh();
        }

        private void ShowVMDTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.VMDTestSection, _layoutRoot?.VMDTestBtn);
            _vmdTestSubPanel?.Refresh();
        }

        /// <summary>コマンド定義の検査パネルを開く。</summary>
        private void ShowCommandSchemaPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.CommandSchemaSection, _layoutRoot?.CommandSchemaBtn);
            _commandSchemaSubPanel?.Refresh();
        }

        private void ShowOriginTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.OriginTestSection, _layoutRoot?.OriginTestBtn);
            _originTestSubPanel?.Refresh();
        }

        private void ShowSkinTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.SkinTestSection, _layoutRoot?.SkinTestBtn);
            _skinTestSubPanel?.Refresh();
        }

        private void ShowSpringBoneTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.SpringBoneTestSection, _layoutRoot?.SpringBoneTestBtn);
            _springBoneTestSubPanel?.Refresh();
        }

        private void ShowRevolutionTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.RevolutionTestSection, _layoutRoot?.RevolutionTestBtn);
            _revolutionTestSubPanel?.Refresh();
        }

        private void ShowProfile2DTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.Profile2DTestSection, _layoutRoot?.Profile2DTestBtn);
            _profile2DTestSubPanel?.Refresh();
        }

        private void ShowPmxToMqoTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.PmxToMqoTestSection, _layoutRoot?.PmxToMqoTestBtn);
            _pmxToMqoTestSubPanel?.Refresh();
        }

        private void ShowMqoToPmxTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MqoToPmxTestSection, _layoutRoot?.MqoToPmxTestBtn);
            _mqoToPmxTestSubPanel?.Refresh();
        }

        private void ShowBarnacleTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.BarnacleTestSection, _layoutRoot?.BarnacleTestBtn);
            _barnacleTestSubPanel?.Refresh();
        }

        private void ShowPipeHairTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.PipeHairTestSection, _layoutRoot?.PipeHairTestBtn);
            _pipeHairTestSubPanel?.Refresh();
        }

        private void ShowFrillSkirtTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.FrillSkirtTestSection, _layoutRoot?.FrillSkirtTestBtn);
            _frillSkirtTestSubPanel?.Refresh();
        }

        private void ShowSpringSkinScenarioPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.SpringSkinScenarioSection, _layoutRoot?.SpringSkinScenarioBtn);
            _springSkinScenarioSubPanel?.Refresh();
        }

        private void ShowSpringSkinPipeScenarioPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.SpringSkinPipeScenarioSection, _layoutRoot?.SpringSkinPipeScenarioBtn);
            _springSkinPipeScenarioSubPanel?.Refresh();
        }

        private void ShowRobotBuildTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.RobotBuildTestSection, _layoutRoot?.RobotBuildTestBtn);
            _robotBuildTestSubPanel?.Refresh();
        }

        /// <summary>
        /// 自動検証用のプロジェクトフォルダ読み込み。
        /// 通常の読み込みと同じ経路（CsvProjectSerializer.Import → _localLoader）を通す。
        /// </summary>
        private bool LoadProjectFolderForTest(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return false;

            var loaded = CsvProjectSerializer.Import(folderPath, out _, out _);
            if (loaded == null) return false;

            _localLoader.Clear();
            foreach (var m in loaded.Models)
                _localLoader.LoadModel(m.FilePath ?? loaded.Name, m);
            AdoptWorkAxisLibrary(loaded);
            return true;
        }

        /// <summary>
        /// 自動検証用のブリッジ生成。UI ボタンと同じ経路
        /// （選択 → 穴A 取り込み → 穴B 取り込み → 生成）を通す。
        /// 頂点は呼び出し側が決めた「エッジ上の 1 頂点」を使う。
        /// </summary>
        private bool CreateBridgeForTest(
            int meshA, int vertexA, int meshB, int vertexB, string name, out string message)
        {
            message = "";

            var model = ActiveProject?.CurrentModel;
            var panel = _primitiveSubPanel;
            if (model == null || panel == null) { message = "モデルかパネルが無い"; return false; }

            panel.ClearBridgeSeeds();
            panel.SetBridgeName(name);

            if (!SelectSingleVertexForTest(model, meshA, vertexA)) { message = "穴A の選択に失敗"; return false; }
            if (!panel.ImportBridgeSeedA()) { message = "穴A 取り込み失敗: " + panel.BridgeSeedInfoA; return false; }

            if (!SelectSingleVertexForTest(model, meshB, vertexB)) { message = "穴B の選択に失敗"; return false; }
            if (!panel.ImportBridgeSeedB()) { message = "穴B 取り込み失敗: " + panel.BridgeSeedInfoB; return false; }

            panel.GenerateBridge();
            return true;
        }

        /// <summary>
        /// 指定メッシュだけを選択し、その頂点を 1 個だけ選択状態にする。
        /// PickHoleSeeds は穴ごとに 1 つ拾うので複数選択でも通るが、
        /// 検証では拾われる種を一意にしたいので他の選択は全部落とす。
        /// </summary>
        private bool SelectSingleVertexForTest(ModelContext model, int meshIndex, int vertex)
        {
            var mc = model?.GetMeshContext(meshIndex);
            if (mc?.MeshObject == null) return false;
            if (vertex < 0 || vertex >= mc.MeshObject.VertexCount) return false;

            for (int i = 0; i < model.MeshContextCount; i++)
                model.GetMeshContext(i)?.Selection?.ClearAll();

            model.SelectedDrawableMeshIndices = new List<int> { meshIndex };
            mc.Selection.Vertices.Add(vertex);
            return true;
        }

        /// <summary>自動検証用のプロジェクトフォルダ保存。</summary>
        private bool SaveProjectFolderForTest(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return false;
            var project = ActiveProject;
            if (project == null) return false;
            return CsvProjectSerializer.Export(folderPath, project);
        }

        private void ShowUnityClipTestPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UnityClipTestSection, _layoutRoot?.UnityClipTestBtn);
            _unityClipTestSubPanel?.Refresh();
        }

        private void ShowUnityClipToVrmaPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UnityClipToVrmaSection, _layoutRoot?.UnityClipToVrmaBtn);
            _unityClipToVrmaSubPanel?.Refresh();
        }

        private void ShowVmdToVrmaPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.VmdToVrmaSection, _layoutRoot?.VmdToVrmaBtn);
            _vmdToVrmaSubPanel?.Refresh();
        }

        private void ShowMotionClipTestPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MotionClipTestSection, _layoutRoot?.MotionClipTestBtn);
            _motionClipTestSubPanel?.Refresh();
        }

        private void ShowRemoteServerPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.RemoteServerSection, _layoutRoot?.RemoteServerBtn);
            _remoteServerSubPanel?.Refresh();
        }

        private void ShowLogPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.LogSection, _layoutRoot?.LogBtn);
            _logSubPanel?.Refresh();
        }

        private void ShowUnderlayPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UnderlaySection, _layoutRoot?.UnderlayBtn);
            _underlayActive = true;   // 左ドラッグでオフセット移動を有効化
        }

        private void ShowGridAxisPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.GridAxisSection, _layoutRoot?.GridAxisBtn);
            _gridAxisSubPanel?.Refresh();
        }

        /// <summary>作業フォルダ設定パネルを開く。</summary>
        private void ShowWorkFolderPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.WorkFolderSection, _layoutRoot?.WorkFolderBtn);
            _workFolderSubPanel?.Refresh();
        }
    }
}
