// PolyLingPlayerViewerCore.Layout.TestPanels.cs
// Player ビューアのコア：BuildLayout の段（MediaPipe・VMD・自動検証・Unity クリップ・モーション）。
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
        /// <summary>BuildLayout の段：MediaPipe・VMD・システムデバッグの自動検証・Unity クリップ・VRMA 変換・統合モーション。</summary>
        private void BuildTestAndMotionPanels()
        {
            _mediaPipeSubPanel = new PlayerMediaPipeFaceDeformSubPanel
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
            };
            _mediaPipeSubPanel.Build(_layoutRoot.MediaPipeSection);

            _vmdTestSubPanel = new PlayerVMDTestSubPanel
            {
                GetModel          = () => ActiveProject?.CurrentModel,
                GetToolContext    = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetUndoController = () => _editOps?.UndoController,
                OnFrameApplied    = () =>
                {
                    _viewportManager.UpdateTransform();
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging);
                },
            };
            _vmdTestSubPanel.Build(_layoutRoot.VMDTestSection);

            // パイプライン自動検証。パネルが押されたときと同じ PanelCommand を送るので、
            // ディスパッチャ側の欠陥もそのまま検査に掛かる。

            // コマンド定義の検査。実行時の状態（ParameterLimits / PLSandbox）を
            // 含むため Editor のメニューではなくここへ置く。
            _commandSchemaSubPanel = new PlayerCommandSchemaSubPanel();
            _commandSchemaSubPanel.Build(_layoutRoot.CommandSchemaSection);

            // 原点CSV自動検証。MQO 読込も CSV 適用も実経路（ImportMqoCommand /
            // ApplyObjectOriginsCommand）へ流すので、ディスパッチャ側の欠陥も検査に掛かる。
            _originTestSubPanel = new PlayerOriginTestSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
                ImportMqo     = path => OnImportMqo(path, null, null),
                // 書き出しはエクスポートパネルと同じ経路。検査に使うので結果をそのまま返す。
                ExportVrm     = (path, settings) =>
                {
                    var m = ActiveProject?.CurrentModel;
                    if (m == null)
                        return Poly_Ling.Vrm.Vrm10ExportResult.Failed("モデルがありません");
                    return Poly_Ling.Vrm.PLVrm10Bridge.I.Export(m, path, settings);
                },
            };
            _originTestSubPanel.Build(_layoutRoot.OriginTestSection);

            // スキン生成自動検証。原点CSVは使わず、ConvertMeshFilterToSkinnedCommand と
            // ApplyHumanoidMappingCommand を実経路へ流す。
            _skinTestSubPanel = new PlayerSkinTestSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
                ImportMqo     = path => OnImportMqo(path, null, null),
                // 書き出しはエクスポートパネルと同じ経路。検査に使うので結果をそのまま返す。
                ExportVrm     = (path, settings) =>
                {
                    var m = ActiveProject?.CurrentModel;
                    if (m == null)
                        return Poly_Ling.Vrm.Vrm10ExportResult.Failed("モデルがありません");
                    return Poly_Ling.Vrm.PLVrm10Bridge.I.Export(m, path, settings);
                },
            };
            _skinTestSubPanel.Build(_layoutRoot.SkinTestSection);

            // スプリングボーン検証。揺れデータのオーサリング UI が無いので、
            // ダミー装備を BuildSpringBoneTestRigCommand で生成してから
            // ApplyHumanoidMappingCommand / ApplyTPoseCommand を実経路へ流す。
            _springBoneTestSubPanel = new PlayerSpringBoneTestSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
                ImportPmx     = path => OnImportPmx(path, null, null),

                // 書き出しはエクスポートパネルと同じ経路。
                // 検査に使うので結果をそのまま返す。
                ExportVrm     = (path, settings) =>
                {
                    var m = ActiveProject?.CurrentModel;
                    if (m == null)
                        return Poly_Ling.Vrm.Vrm10ExportResult.Failed("モデルがありません");
                    return Poly_Ling.Vrm.PLVrm10Bridge.I.Export(m, path, settings);
                },
            };
            _springBoneTestSubPanel.Build(_layoutRoot.SpringBoneTestSection);

            // フリルスカート自動検証。オブジェクトグループの使い方が UI からは
            // 追いにくいので、同じ手順をコマンドだけで通してログに残す。
            // 参照の解決がモデルをまたぐため ProjectContext を渡す。
            _frillSkirtTestSubPanel = new PlayerFrillSkirtTestSubPanel
            {
                GetProject    = () => ActiveProject,
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
            };
            _frillSkirtTestSubPanel.Build(_layoutRoot.FrillSkirtTestSection);

            // 揺れもの→スキンド→VRM 自動検証。
            // ボーンを先に作ってから一括スキンド化する順が通ることを見る。
            // 塗りの契機がスキンド化 1 か所に集約されていることの検証も兼ねる。
            _springSkinScenarioSubPanel = new PlayerSpringSkinScenarioSubPanel
            {
                GetProject    = () => ActiveProject,
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
            };
            _springSkinScenarioSubPanel.Build(_layoutRoot.SpringSkinScenarioSection);

            // 揺れもの（パイプ）→スキンド→VRM 自動検証。
            // フリル版と同じ MQO・同じ順で、フリルの段だけをパイプへ置き換えたもの。
            // パイプははしご 1 本ごとに作って Append で連結するので、
            // 連結を越えてウェイトが残るかをここで見る。
            _springSkinPipeScenarioSubPanel = new PlayerSpringSkinPipeScenarioSubPanel
            {
                GetProject    = () => ActiveProject,
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
            };
            _springSkinPipeScenarioSubPanel.Build(_layoutRoot.SpringSkinPipeScenarioSection);

            // 前髪パイプ自動検証。四分球を梯子にしてパイプを生やす。
            // 開始タグ三角形・終了三角形を足して梯子の自動検出を通す経路の確認も兼ねる。
            _pipeHairTestSubPanel = new PlayerPipeHairTestSubPanel
            {
                GetProject    = () => ActiveProject,
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
            };
            _pipeHairTestSubPanel.Build(_layoutRoot.PipeHairTestSection);

            // 藤壺自動検証。球を梯子にして、円錐＋土台を各 rung へ配置する。
            // 梯子の元だけでなく配置元も ObjectId で追随することの確認を兼ねる。
            _barnacleTestSubPanel = new PlayerBarnacleTestSubPanel
            {
                GetProject    = () => ActiveProject,
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
            };
            _barnacleTestSubPanel.Build(_layoutRoot.BarnacleTestSection);

            // 回転体・2D押し出しの自動検証。梯子を使わず、プロファイルの
            // 線オブジェクトだけを入力にする経路の確認。
            _revolutionTestSubPanel = new PlayerRevolutionTestSubPanel
            {
                GetProject    = () => ActiveProject,
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
            };
            _revolutionTestSubPanel.Build(_layoutRoot.RevolutionTestSection);

            _profile2DTestSubPanel = new PlayerProfile2DTestSubPanel
            {
                GetProject    = () => ActiveProject,
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
            };
            _profile2DTestSubPanel.Build(_layoutRoot.Profile2DTestSection);

            // PMX位置→MQO保存 自動検証。PMX はソースにするだけでモデルには載せない
            // （載せると直後の MQO 読込で ModelContext ごと差し替わって消える）ため、
            // ImportPmxCommand を自前でキューへ積み、onResult だけを受け取る。
            _pmxToMqoTestSubPanel = new PlayerPmxToMqoTestSubPanel
            {
                GetModel          = () => ActiveProject?.CurrentModel,
                EnqueueCommand    = cmd  => _editOps?.CommandQueue.Enqueue(cmd),
                ImportMqo         = path => OnImportMqo(path, null, null),
                GetUndoController = () => _editOps?.UndoController,

                // 書き出しはエクスポートパネルと同じ経路。検査に使うので結果をそのまま返す。
                ExportMqo = (path, settings) =>
                {
                    var m = ActiveProject?.CurrentModel;
                    if (m == null)
                        return new Poly_Ling.MQO.MQOExportResult
                        { Success = false, ErrorMessage = "モデルがありません" };

                    var r = Poly_Ling.MQO.MQOExporter.ExportFile(path, m, settings);
                    if (r != null && r.Success) AuxiliaryBackupWriter.Save(m, path);
                    return r;
                },

                // 頂点を書き換えたあとの再構築。部分インポート完了時と同じ処理。
                RefreshAfterReplace = () =>
                {
                    var m = ActiveProject?.CurrentModel;
                    if (m == null) return;
                    _viewportManager.EnterSceneReset(ActiveProject, clearScene: true);
                },
            };
            _pmxToMqoTestSubPanel.Build(_layoutRoot.PmxToMqoTestSection);

            // MQO位置UV→PMX保存 自動検証。土台の PMX はモデルに載せる。
            // MQO は生座標のまま読む必要があるため（座標変換は転送側が掛ける）、
            // ImportMqoCommand ではなく MQOPartialMatchHelper をパネル内で直接使う。
            _mqoToPmxTestSubPanel = new PlayerMqoToPmxTestSubPanel
            {
                GetModel          = () => ActiveProject?.CurrentModel,
                ImportPmx         = path => OnImportPmx(path, null, null),
                GetUndoController = () => _editOps?.UndoController,

                // 書き出しはエクスポートパネルと同じ経路。検査に使うので結果をそのまま返す。
                ExportPmx = (path, settings) =>
                {
                    var m = ActiveProject?.CurrentModel;
                    if (m == null)
                        return new Poly_Ling.PMX.PMXExportResult
                        { Success = false, ErrorMessage = "モデルがありません" };

                    var r = Poly_Ling.PMX.PMXExporter.Export(m, path, settings);
                    if (r != null && r.Success) AuxiliaryBackupWriter.Save(m, path);
                    return r;
                },

                // 面まで作り直すので、部分インポート完了時と同じ再構築を通す。
                RefreshAfterReplace = () =>
                {
                    var m = ActiveProject?.CurrentModel;
                    if (m == null) return;
                    _viewportManager.EnterSceneReset(ActiveProject, clearScene: true);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _mqoToPmxTestSubPanel.Build(_layoutRoot.MqoToPmxTestSection);

            // ロボ組み立て自動検証。基本図形の生成から VRM 書き出しまでを
            // 5 系統ぶん流し、段ごとにフォルダへ保存する。
            // 生成もブリッジも面削除も、パネルが押されたときと同じコマンドを送る。
            _robotBuildTestSubPanel = new PlayerRobotBuildTestSubPanel
            {
                GetModel          = () => ActiveProject?.CurrentModel,
                GetModelIndex     = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand       = cmd => _commandDispatcher?.Dispatch(cmd),
                SaveProjectFolder = SaveProjectFolderForTest,

                // 書き出しはエクスポートパネルと同じ経路。
                ExportVrm = (path, settings) =>
                {
                    var m = ActiveProject?.CurrentModel;
                    if (m == null)
                        return Poly_Ling.Vrm.Vrm10ExportResult.Failed("モデルがありません");
                    return Poly_Ling.Vrm.PLVrm10Bridge.I.Export(m, path, settings);
                },

                RefreshAfterTopologyChange = () =>
                {
                    _viewportManager.EnterTopologyChanged(ActiveProject);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _robotBuildTestSubPanel.Build(_layoutRoot.RobotBuildTestSection);

            _unityClipTestSubPanel = new PlayerUnityClipTestSubPanel
            {
                GetModel          = () => ActiveProject?.CurrentModel,
                GetModelIndex     = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand       = cmd => _panelContext?.SendCommand(cmd),
                GetToolContext    = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetUndoController = () => _editOps?.UndoController,
                OnFrameApplied    = () =>
                {
                    _viewportManager.UpdateTransform();
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging);
                },
            };
            _unityClipTestSubPanel.Build(_layoutRoot.UnityClipTestSection);

            // モデルを見ない変換専用パネル。GetModel は持たせない。
            _unityClipToVrmaSubPanel = new PlayerUnityClipToVrmaSubPanel
            {
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
            };
            _unityClipToVrmaSubPanel.Build(_layoutRoot.UnityClipToVrmaSection);

            // VMD をモデルへ適用しながら書き出す。Humanoid 割り当てが要る。
            _vmdToVrmaSubPanel = new PlayerVmdToVrmaSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
            };
            _vmdToVrmaSubPanel.Build(_layoutRoot.VmdToVrmaSection);

            _motionClipTestSubPanel = new PlayerMotionClipTestSubPanel
            {
                GetModel          = () => ActiveProject?.CurrentModel,
                GetToolContext    = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetUndoController = () => _editOps?.UndoController,
                OnFrameApplied    = () =>
                {
                    _viewportManager.UpdateTransform();
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging);
                },
            };
            _motionClipTestSubPanel.Build(_layoutRoot.MotionClipTestSection);
        }
    }
}
