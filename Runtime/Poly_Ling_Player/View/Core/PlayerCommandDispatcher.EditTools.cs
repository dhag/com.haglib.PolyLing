// PlayerCommandDispatcher.EditTools.cs
// コマンドディスパッチャ：選択・移動・スカルプト・位相編集・ツールの確定・ギズモ・デフォーマ・作業軸。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectPose;
using Poly_Ling.Ops;
using Poly_Ling.UI;
using Poly_Ling.Diagnostics;
using Poly_Ling.Serialization;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// DispatchCore の分担：選択・頂点移動・ピボット・スカルプト・詳細選択・属性選択・位相編集・ツールの確定・ギズモ・デフォーマ・作業軸。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchEditTools(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── 頂点・辺・面・線分の選択
                //
                // 書き込み先の解決・線分の扱い・頂点への展開・選択 Undo は
                // PlayerSelectionOps と MoveToolHandler が持つ。以前はここに同じ
                // 選択処理をもう 1 組持っていたが、クリック経路と食い違っていたため
                // 削除して委譲に寄せた。
                // 食い違っていた点: 書き込み先が単一 MasterIndex で非加算でも他メッシュの
                // 選択が残った / 線分(SelectionState.Lines)を扱えなかった /
                // ExpandLinkedVertices を通らず「選んだ要素の頂点も選択する」が効かなかった /
                // 選択 Undo を積まなかった / クリック経路が行わない
                // SetSelectionState の差し替えをしていた。
                case SelectElementsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnSelectElements == null)
                    {
                        Fail("selection handler not wired");
                        return true;
                    }
                    string selReason = OnSelectElements.Invoke(c);
                    if (selReason != null)
                    {
                        Fail(selReason);
                        return true;
                    }
                    // _notifyPanels は呼ばない。
                    // 反映は PlayerSelectionOps.ApplyElementSet 末尾の
                    // OnSelectionChanged が担い、クリック経路と同じ重さになる。
                    // NotifyPanels(Selection) が追加で行うのは
                    // EnterSelectionChanged（全メッシュ×全頂点のフラグ再計算と
                    // 全頂点 GPU 転送）、可視セクション refresh の 2 周目、
                    // PlayerBlendSubPanel.OnSelectionChanged、
                    // PolyLingPlayerServer.NotifySelectionChanged だが、
                    // いずれも要素選択では不要（前 3 者は OnSelectionChanged 側で
                    // 足りる／メッシュ増減しか見ない。最後は選択署名に
                    // 要素選択を含まないため差分なしで早期 return する）。
                    ReportData(BuildSelectionCountsData(model));
                    return true;
                }

                // ── 選択頂点の移動
                //
                // 頂点への展開規則・マグネット・Undo 記録・リモート配信は
                // MoveToolHandler が持つ。以前はここに同じ移動処理をもう 1 組
                // 持っていたが、マウス経路と食い違っていたため削除して委譲に寄せた。
                // 食い違っていた点: 対象が単一 MasterIndex だった / Selection.Vertices
                // しか見ず辺・面・線分の選択を頂点へ展開していなかった /
                // マグネットを通らなかった / OnVerticesCommitted を呼ばないため
                // 他クライアントへ配信されなかった。
                //
                // GPU 反映はここでは行わない。マウス経路と同じく ApplyDelta 内の
                // OnSyncMeshPositions が担う。
                case MoveSelectedVerticesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnMoveSelectedVertices == null)
                    {
                        Fail("move handler not wired");
                        return true;
                    }
                    string moveReason = OnMoveSelectedVertices.Invoke(c);
                    if (moveReason != null)
                    {
                        Fail(moveReason);
                        return true;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── ピボット移動（原点だけ移動）
                //
                // 変形・子 BoneTransform の補償・Undo のグループ化は
                // ObjectMoveTool(OriginOnly) が持つ。以前はここに同じ処理をもう 1 組
                // 持っていたが、マウス経路と食い違っていたため削除して委譲に寄せた。
                // 食い違っていた点: 対象が単一 MasterIndex だった / スキン判定
                // (MeshType.Mesh && !IsSkinned) が無かった / 孤立頂点を除外していた /
                // 直接の子のワールド位置を保つ補償が無かった /
                // MeshListStack のグループ化が無く Undo が対象数だけ分かれていた。
                case MovePivotCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnMovePivot == null)
                    {
                        Fail("pivot handler not wired");
                        return true;
                    }
                    string pivotReason = OnMovePivot.Invoke(c);
                    if (pivotReason != null)
                    {
                        Fail(pivotReason);
                        return true;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── スカルプトストローク
                //
                // 変形は SculptTool が持つ。以前はここに同じブラシ処理をもう 1 組
                // 持っていたが、Draw の視線方向による反転補正が無く、距離モード
                // （リンク距離）の分岐も無かったためマウス経路と結果が食い違っていた。
                // 削除して委譲に寄せた。GPU 反映と Undo 記録はハンドラ側が行う。
                case SculptStrokeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnSculptStroke == null)
                    {
                        Fail("sculpt handler not wired");
                        return true;
                    }
                    string ssoReason = OnSculptStroke.Invoke(c);
                    if (ssoReason != null) { Fail(ssoReason); return true; }
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── 詳細選択
                //
                // 実処理は AdvancedSelectTool（EdgeLoopSelectMode ほか）が持つ。
                // 以前はここに同じ選択アルゴリズムをもう 1 組持っていたが、
                // マウス経路と結果が食い違っていたため削除し、委譲に寄せた。
                // 例: 旧 AdvEdgeLoop は VertexPair が V1<=V2 へ正規化される
                //     （EdgeTypes.cs:21-25）ことで逆方向の探索が初回で break し、
                //     輪の片側しか拾えていなかった。
                case AdvancedSelectCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnAdvancedSelect == null)
                    {
                        Fail("advanced select handler not wired");
                        return true;
                    }
                    string advReason = OnAdvancedSelect.Invoke(c);
                    if (advReason != null) { Fail(advReason); return true; }
                    _notifyPanels(ChangeKind.Selection);
                    ReportData(BuildSelectionCountsData(model));
                    return true;
                }

                // ── 属性選択（クリック非依存）
                //
                // 走査は AdvancedSelectTool.ExecuteAttributeSelect が正典。
                // パネルの「実行」ボタンもこのコマンド経由に統一してある。
                case AdvancedSelectByAttributeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnAdvancedSelectByAttribute == null)
                    {
                        Fail("attribute select handler not wired");
                        return true;
                    }
                    string attrReason = OnAdvancedSelectByAttribute.Invoke(c);
                    if (attrReason != null) { Fail(attrReason); return true; }
                    _notifyPanels(ChangeKind.Selection);
                    ReportData(BuildSelectionCountsData(model));
                    return true;
                }

                // ── 位相編集（パラメータを持たない実行系）
                //
                // 実処理は FaceMergeTool ほかが正典。受け口はハンドラへ委譲し、
                // 対象照合（MasterIndices と現在の選択が一致するか）もハンドラが行う。
                // 位相が変わるので Selection ではなく Topology を通知する。
                case FaceMergeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnFaceMerge == null) { Fail("face merge handler not wired"); return true; }
                    string fmReason = OnFaceMerge.Invoke(c);
                    if (fmReason != null) { Fail(fmReason); return true; }
                    return true;
                }

                case Quad4To1Command c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnQuad4To1 == null) { Fail("quad 4to1 handler not wired"); return true; }
                    string q41Reason = OnQuad4To1.Invoke(c);
                    if (q41Reason != null) { Fail(q41Reason); return true; }
                    return true;
                }

                case Tri4To1Command c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnTri4To1 == null) { Fail("tri 4to1 handler not wired"); return true; }
                    string t41Reason = OnTri4To1.Invoke(c);
                    if (t41Reason != null) { Fail(t41Reason); return true; }
                    return true;
                }

                case VertexDissolveCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnVertexDissolve == null) { Fail("vertex dissolve handler not wired"); return true; }
                    string vdReason = OnVertexDissolve.Invoke(c);
                    if (vdReason != null) { Fail(vdReason); return true; }
                    return true;
                }

                case SplitVerticesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnSplitVertices == null) { Fail("split vertices handler not wired"); return true; }
                    string svReason = OnSplitVertices.Invoke(c);
                    if (svReason != null) { Fail(svReason); return true; }
                    return true;
                }

                // ── 位相・頂点編集（パラメータを持つ実行系）
                //
                // 設定値はコマンドが正典。ハンドラが実行後にパネルの値へ戻すので、
                // リモートから送ってもパネルの表示は変わらない。
                case VertexHoleCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnVertexHole == null) { Fail("vertex hole handler not wired"); return true; }
                    string vhReason = OnVertexHole.Invoke(c);
                    if (vhReason != null) { Fail(vhReason); return true; }
                    return true;
                }

                case FlipFaceCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnFlipFace == null) { Fail("flip face handler not wired"); return true; }
                    string ffReason = OnFlipFace.Invoke(c);
                    if (ffReason != null) { Fail(ffReason); return true; }
                    return true;
                }

                case AlignVerticesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnAlignVertices == null) { Fail("align vertices handler not wired"); return true; }
                    string avReason = OnAlignVertices.Invoke(c);
                    if (avReason != null) { Fail(avReason); return true; }
                    return true;
                }

                case SmoothEdgesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnSmoothEdges == null) { Fail("smooth edges handler not wired"); return true; }
                    string seReason = OnSmoothEdges.Invoke(c);
                    if (seReason != null) { Fail(seReason); return true; }
                    return true;
                }

                case EdgeRibbonFaceCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnEdgeRibbonFace == null) { Fail("edge ribbon face handler not wired"); return true; }
                    string erfReason = OnEdgeRibbonFace.Invoke(c);
                    if (erfReason != null) { Fail(erfReason); return true; }
                    return true;
                }

                // 読み込みはモデルを作る操作なので、現在モデルが無くても通す。
                case ImportPmxFileCommand c:
                {
                    if (OnImportPmxFile == null) { Fail("import pmx handler not wired"); return true; }
                    string ipReason = OnImportPmxFile.Invoke(c);
                    if (ipReason != null) { Fail(ipReason); return true; }
                    return true;
                }

                // 書き出しは現在のモデルを使うので、無ければここで弾く。
                case ExportPmxFileCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnExportPmxFile == null) { Fail("export pmx handler not wired"); return true; }
                    string epReason = OnExportPmxFile.Invoke(c);
                    if (epReason != null) { Fail(epReason); return true; }
                    ReportData(BuildWriteResultData(c.FilePath));
                    return true;
                }

                // 読み込みはモデルを作る操作なので、現在モデルが無くても通す。
                case ImportMqoFileCommand c:
                {
                    if (OnImportMqoFile == null) { Fail("import mqo handler not wired"); return true; }
                    string imqReason = OnImportMqoFile.Invoke(c);
                    if (imqReason != null) { Fail(imqReason); return true; }
                    return true;
                }

                case ExportMqoFileCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnExportMqoFile == null) { Fail("export mqo handler not wired"); return true; }
                    string emqReason = OnExportMqoFile.Invoke(c);
                    if (emqReason != null) { Fail(emqReason); return true; }
                    ReportData(BuildWriteResultData(c.FilePath));
                    return true;
                }

                // 読み込みはモデルを作る操作なので、現在モデルが無くても通す。
                case ImportObjFileCommand c:
                {
                    if (OnImportObjFile == null) { Fail("import obj handler not wired"); return true; }
                    string iobjReason = OnImportObjFile.Invoke(c);
                    if (iobjReason != null) { Fail(iobjReason); return true; }
                    return true;
                }

                case ExportObjFileCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnExportObjFile == null) { Fail("export obj handler not wired"); return true; }
                    string eobjReason = OnExportObjFile.Invoke(c);
                    if (eobjReason != null) { Fail(eobjReason); return true; }
                    ReportData(BuildWriteResultData(c.FilePath));
                    return true;
                }

                case ExportVrmFileCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnExportVrmFile == null) { Fail("export vrm handler not wired"); return true; }
                    string evrmReason = OnExportVrmFile.Invoke(c);
                    if (evrmReason != null) { Fail(evrmReason); return true; }
                    ReportData(BuildWriteResultData(c.FilePath));
                    return true;
                }

                // 読み込みはモデルを作る操作なので、現在モデルが無くても通す。
                case ImportVrmFileCommand c:
                {
                    if (OnImportVrmFile == null) { Fail("import vrm handler not wired"); return true; }
                    string ivrmReason = OnImportVrmFile.Invoke(c);
                    if (ivrmReason != null) { Fail(ivrmReason); return true; }
                    return true;
                }

                // プロジェクトの保存・読込はモデルではなくプロジェクトを見るので、
                // 現在モデルの有無は問わない。判定は受け口が行う。
                case SaveProjectFileCommand c:
                {
                    if (OnSaveProjectFile == null) { Fail("save project handler not wired"); return true; }
                    string spReason = OnSaveProjectFile.Invoke(c);
                    if (spReason != null) { Fail(spReason); return true; }
                    ReportData(BuildWriteResultData(c.FilePath));
                    return true;
                }

                case LoadProjectFileCommand c:
                {
                    if (OnLoadProjectFile == null) { Fail("load project handler not wired"); return true; }
                    string lpReason = OnLoadProjectFile.Invoke(c);
                    if (lpReason != null) { Fail(lpReason); return true; }
                    return true;
                }

                case SaveProjectCsvCommand c:
                {
                    if (OnSaveProjectCsv == null) { Fail("save project csv handler not wired"); return true; }
                    string spcReason = OnSaveProjectCsv.Invoke(c);
                    if (spcReason != null) { Fail(spcReason); return true; }

                    // CSV プロジェクトの経路はファイル（任意名の .csv）。
                    // 受け口 ExecuteSaveProjectCsv が PLSandbox.TryResolveWrite を通し、
                    // CsvProjectSerializer.ExportToFile へ渡す
                    // （PolyLingPlayerViewerCore.CreateCommands.cs:489-498）。
                    // モデルフォルダは同じディレクトリ直下に別途できるが、
                    // ここで数えると無関係なファイルまで拾うので数えない。
                    ReportData(BuildWriteResultData(c.FilePath));
                    return true;
                }

                case LoadProjectCsvCommand c:
                {
                    if (OnLoadProjectCsv == null) { Fail("load project csv handler not wired"); return true; }
                    string lpcReason = OnLoadProjectCsv.Invoke(c);
                    if (lpcReason != null) { Fail(lpcReason); return true; }
                    return true;
                }

                case PlanarizeAlongBonesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnPlanarizeAlongBones == null) { Fail("planarize handler not wired"); return true; }
                    string pabReason = OnPlanarizeAlongBones.Invoke(c);
                    if (pabReason != null) { Fail(pabReason); return true; }
                    return true;
                }

                case MergeVerticesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnMergeVertices == null) { Fail("merge vertices handler not wired"); return true; }
                    string mvReason = OnMergeVertices.Invoke(c);
                    if (mvReason != null) { Fail(mvReason); return true; }
                    return true;
                }

                // ── 位相・頂点編集（対象や生成先の指定を伴う実行系）
                case DeleteSelectionCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnDeleteSelection == null) { Fail("delete selection handler not wired"); return true; }
                    string delReason = OnDeleteSelection.Invoke(c);
                    if (delReason != null) { Fail(delReason); return true; }
                    return true;
                }

                case PipeAlignCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnPipeAlign == null) { Fail("pipe align handler not wired"); return true; }
                    string paReason = OnPipeAlign.Invoke(c);
                    if (paReason != null) { Fail(paReason); return true; }
                    return true;
                }

                case PlaceObjectReshapeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnPlaceObjectReshape == null) { Fail("place object reshape handler not wired"); return true; }
                    string porReason = OnPlaceObjectReshape.Invoke(c);
                    if (porReason != null) { Fail(porReason); return true; }
                    return true;
                }

                case SolidifyCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnSolidify == null) { Fail("solidify handler not wired"); return true; }
                    string solReason = OnSolidify.Invoke(c);
                    if (solReason != null) { Fail(solReason); return true; }
                    return true;
                }

                case LineExtrudeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnLineExtrude == null) { Fail("line extrude handler not wired"); return true; }
                    string leReason = OnLineExtrude.Invoke(c);
                    if (leReason != null) { Fail(leReason); return true; }
                    return true;
                }

                case SurfaceSnapCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnSurfaceSnap == null) { Fail("surface snap handler not wired"); return true; }
                    string ssReason = OnSurfaceSnap.Invoke(c);
                    if (ssReason != null) { Fail(ssReason); return true; }
                    return true;
                }

                // ── ドラッグ確定（ベベル・押し出し）
                case EdgeBevelCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnEdgeBevel == null) { Fail("edge bevel handler not wired"); return true; }
                    string ebReason = OnEdgeBevel.Invoke(c);
                    if (ebReason != null) { Fail(ebReason); return true; }
                    return true;
                }

                case EdgeExtrudeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnEdgeExtrude == null) { Fail("edge extrude handler not wired"); return true; }
                    string eeReason = OnEdgeExtrude.Invoke(c);
                    if (eeReason != null) { Fail(eeReason); return true; }
                    return true;
                }

                case FaceExtrudeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnFaceExtrude == null) { Fail("face extrude handler not wired"); return true; }
                    string feReason = OnFaceExtrude.Invoke(c);
                    if (feReason != null) { Fail(feReason); return true; }
                    return true;
                }

                // ── スキンウェイト塗り
                case SkinWeightPaintCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnSkinWeightPaint == null) { Fail("skin weight paint handler not wired"); return true; }
                    string swpReason = OnSkinWeightPaint.Invoke(c);
                    if (swpReason != null) { Fail(swpReason); return true; }
                    return true;
                }

                // ── 変形ギズモ（選択頂点の回転・スケール）
                case RotateSelectionCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnRotateSelection == null) { Fail("rotate selection handler not wired"); return true; }
                    string rsReason = OnRotateSelection.Invoke(c);
                    if (rsReason != null) { Fail(rsReason); return true; }
                    return true;
                }

                case ScaleSelectionCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnScaleSelection == null) { Fail("scale selection handler not wired"); return true; }
                    string scReason = OnScaleSelection.Invoke(c);
                    if (scReason != null) { Fail(scReason); return true; }
                    return true;
                }

                // ── オブジェクトごと移動・回転（ObjectMove ギズモ）
                case MoveObjectsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnMoveObjects == null) { Fail("move objects handler not wired"); return true; }
                    string moReason = OnMoveObjects.Invoke(c);
                    if (moReason != null) { Fail(moReason); return true; }
                    return true;
                }

                case RotateObjectsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnRotateObjects == null) { Fail("rotate objects handler not wired"); return true; }
                    string roReason = OnRotateObjects.Invoke(c);
                    if (roReason != null) { Fail(roReason); return true; }
                    return true;
                }

                // ── デフォーマ
                //
                // 抽象基底で受ければ派生 6 種を拾える
                // （case CreatePrimitiveMeshCommand と同じ形）。
                case ApplyDeformCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnApplyDeform == null) { Fail("deform handler not wired"); return true; }
                    string adReason = OnApplyDeform.Invoke(c);
                    if (adReason != null) { Fail(adReason); return true; }
                    return true;
                }

                case ApplyLatticeDeformCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnApplyLatticeDeform == null) { Fail("lattice deform handler not wired"); return true; }
                    string aldReason = OnApplyLatticeDeform.Invoke(c);
                    if (aldReason != null) { Fail(aldReason); return true; }
                    return true;
                }

                // ── クリック確定（辺トポロジ・面追加）
                case EdgeTopologyFlipCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnEdgeTopologyFlip == null) { Fail("edge flip handler not wired"); return true; }
                    string etfReason = OnEdgeTopologyFlip.Invoke(c);
                    if (etfReason != null) { Fail(etfReason); return true; }
                    return true;
                }

                case EdgeTopologyDissolveCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnEdgeTopologyDissolve == null) { Fail("edge dissolve handler not wired"); return true; }
                    string etdReason = OnEdgeTopologyDissolve.Invoke(c);
                    if (etdReason != null) { Fail(etdReason); return true; }
                    return true;
                }

                case EdgeTopologySplitCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnEdgeTopologySplit == null) { Fail("edge split handler not wired"); return true; }
                    string etsReason = OnEdgeTopologySplit.Invoke(c);
                    if (etsReason != null) { Fail(etsReason); return true; }
                    return true;
                }

                case AddFaceCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnAddFace == null) { Fail("add face handler not wired"); return true; }
                    string afReason = OnAddFace.Invoke(c);
                    if (afReason != null) { Fail(afReason); return true; }
                    return true;
                }

                case CreatePointDefinedPrimitiveCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnCreatePointDefinedPrimitive == null) { Fail("point defined primitive handler not wired"); return true; }
                    string pdReason = OnCreatePointDefinedPrimitive.Invoke(c);
                    if (pdReason != null) { Fail(pdReason); return true; }
                    return true;
                }

                // ── ナイフ
                case KnifeLadderCutCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnKnifeLadderCut == null) { Fail("knife ladder cut handler not wired"); return true; }
                    string klcReason = OnKnifeLadderCut.Invoke(c);
                    if (klcReason != null) { Fail(klcReason); return true; }
                    ReportData(BuildTopologyCountsData(model, c.MasterIndices));
                    return true;
                }

                case KnifeBeltLoopCutCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnKnifeBeltLoopCut == null) { Fail("knife belt loop handler not wired"); return true; }
                    string kblReason = OnKnifeBeltLoopCut.Invoke(c);
                    if (kblReason != null) { Fail(kblReason); return true; }
                    ReportData(BuildTopologyCountsData(model, c.MasterIndices));
                    return true;
                }

                case KnifeEraseEdgeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnKnifeEraseEdge == null) { Fail("knife erase handler not wired"); return true; }
                    string keeReason = OnKnifeEraseEdge.Invoke(c);
                    if (keeReason != null) { Fail(keeReason); return true; }
                    ReportData(BuildTopologyCountsData(model, c.MasterIndices));
                    return true;
                }

                case KnifeSimpleCutCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnKnifeSimpleCut == null) { Fail("knife simple cut handler not wired"); return true; }
                    string kscReason = OnKnifeSimpleCut.Invoke(c);
                    if (kscReason != null) { Fail(kscReason); return true; }
                    ReportData(BuildTopologyCountsData(model, c.MasterIndices));
                    return true;
                }

                // ── 作業軸
                //
                // 作業軸はモデルに属さないので model の有無を条件にしない。
                case SetWorkAxisCommand c:
                {
                    if (OnSetWorkAxis == null) { Fail("work axis handler not wired"); return true; }
                    string swaReason = OnSetWorkAxis.Invoke(c);
                    if (swaReason != null) { Fail(swaReason); return true; }
                    return true;
                }

                case RecallWorkAxisCommand c:
                {
                    if (OnRecallWorkAxis == null) { Fail("work axis recall handler not wired"); return true; }
                    string rwaReason = OnRecallWorkAxis.Invoke(c);
                    if (rwaReason != null) { Fail(rwaReason); return true; }
                    return true;
                }

                // ── 可視性トグル
                case ToggleVisibilityCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var visCtx = model.GetMeshContext(c.MasterIndex);
                    if (visCtx == null) { Fail($"masterIndex {c.MasterIndex} のオブジェクトがありません"); return true; }
                    ApplyVisibility(model, new[] { c.MasterIndex }, !visCtx.IsVisible, "Toggle Visibility");
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // スカルプト ブラシ ヘルパー（SculptStrokeCommand 用）
        // ================================================================

        // ================================================================
        // 詳細選択 トポロジー ヘルパー（AdvancedSelectCommand 用）
        // ================================================================

        // ── Connected ────────────────────────────────────────────────

        // ── Belt ─────────────────────────────────────────────────────

        // ── EdgeLoop ─────────────────────────────────────────────────

        // ── ShortestPath (Dijkstra) ───────────────────────────────────

        // ── 共通ユーティリティ ────────────────────────────────────────

        // SelectionHelper の BuildEdgeAdjacency は ToolContext を取るが
        // MeshObject のみから辺隣接を構築するオーバーロードを作成
        private static Dictionary<VertexPair, HashSet<VertexPair>> SelectionHelperBuildEdgeAdj(MeshObject mo)
        {
            // 辺→共有する面の辺隣接を構築（SelectionHelper.BuildEdgeAdjacency の MeshObject 版）
            var edgeToFaces = SelectionHelper.BuildEdgeToFacesMap(mo);
            var result      = new Dictionary<VertexPair, HashSet<VertexPair>>();

            foreach (var kv in edgeToFaces)
            {
                if (!result.ContainsKey(kv.Key)) result[kv.Key] = new HashSet<VertexPair>();
                foreach (int fi in kv.Value)
                {
                    var vs = mo.Faces[fi].VertexIndices;
                    int n  = vs.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var e = new VertexPair(vs[i], vs[(i + 1) % n]);
                        if (e != kv.Key)
                        {
                            result[kv.Key].Add(e);
                            if (!result.ContainsKey(e)) result[e] = new HashSet<VertexPair>();
                            result[e].Add(kv.Key);
                        }
                    }
                }
            }
            return result;
        }
    }
}
