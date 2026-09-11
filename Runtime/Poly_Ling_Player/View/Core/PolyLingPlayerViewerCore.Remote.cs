// PolyLingPlayerViewerCore.Remote.cs
// Player ビューアのコア：クライアント／受信イベントとフェッチフロー。
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
        // クライアントイベント
        // ================================================================

        private void OnConnected()
        {
            _status = "接続済み";
            // 自タイプをサーバへ登録（list 系と同じ枠組み）。userName 既定は空（名前なし）。
            _client?.RegisterClientType("playerViewer", "");
        }
        private void OnDisconnected() { _status = "切断"; }

        private void OnPushReceived(string json)
        {
            // サーバの実際の push 名に合わせる（旧 mesh_changed/model_changed は発行元なし）。
            // 構造変更（一覧変更）を契機に再フェッチする。
            if (json.Contains("\"event\":\"meshListChanged\""))
                FetchProject();
        }

        /// <summary>
        /// サーバから push された PositionsOnly を適用する（S→C 連動）。
        /// メインスレッドで呼ばれる。
        ///
        /// v2 ヘッダの ObjectId で対象を確定する。
        /// 旧 v1（ObjectId=0）は対象を運べないため、従来どおり選択メッシュへ当てる。
        /// </summary>
        private void ApplyRemotePositions(byte[] data)
        {
            if (data == null) return;
            var header = RemoteBinarySerializer.ReadHeader(data);
            if (header == null || header.Value.MessageType != BinaryMessageType.PositionsOnly) return;

            var h       = header.Value;
            var project = ActiveProject;
            if (project == null) return;

            ModelContext model = null;
            MeshContext  mc    = null;

            if (h.HasTarget)
            {
                // 指定モデル→全モデルの順に安定IDで探す
                if (h.ModelIndex >= 0 && h.ModelIndex < project.ModelCount)
                {
                    model = project.Models[h.ModelIndex];
                    mc    = FindMeshByObjectId(model, h.ObjectId);
                }
                if (mc == null)
                {
                    for (int mi = 0; mi < project.ModelCount; mi++)
                    {
                        var m = project.Models[mi];
                        var found = FindMeshByObjectId(m, h.ObjectId);
                        if (found != null) { model = m; mc = found; break; }
                    }
                }
                if (mc == null) return;   // 未知のオブジェクト。無視する。
            }
            else
            {
                // v1 互換: 対象未指定なので選択メッシュへ当てる
                model = project.CurrentModel;
                mc    = model?.ActiveMeshContext;
            }

            if (mc?.MeshObject == null) return;

            // 頂点数が食い違うなら適用しない（トポロジ変更後の古い更新）
            if (h.VertexCount != (uint)mc.MeshObject.VertexCount) return;

            RemoteBinarySerializer.Deserialize(data, mc.MeshObject);
            _viewportManager.SyncMeshPositionsAndTransform(mc, model);
            _viewportManager.UpdateTransform();
        }

        /// <summary>安定IDで MeshContext を引く。</summary>
        private static MeshContext FindMeshByObjectId(ModelContext model, ulong objectId)
        {
            if (model == null || objectId == 0UL) return null;
            int count = model.MeshContextCount;
            for (int i = 0; i < count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && mc.ObjectId == objectId) return mc;
            }
            return null;
        }

        // ================================================================
        // 受信イベント
        // ================================================================

        private void OnProjectHeaderReceived(ProjectContext project)
        {
            if (_fetchFlow != null) _fetchFlow.ModelCount = project.ModelCount;
            // Phase 2a-2g-3: ヘッダ受信時の即時シーンクリア。軽量操作として据え置き。
            // EnterSceneReset で置換すると RebuildAdapter + PresentAll まで走って過剰。
            // フェッチ完了時に PlayerRemoteFetchFlow.FetchAllModelsBatch 末尾で
            // EnterSceneReset(clearScene: true) が呼ばれる (設計 Z)。
            #pragma warning disable CS0618
            _viewportManager.ClearScene();
            #pragma warning restore CS0618
            RebuildModelList();
        }

        private void OnModelMetaReceived(int mi, ModelContext model) { }
        private void OnMeshSummaryReceived(int mi, int si, MeshContext mc) { }

        private void OnMeshDataReceived(int mi, int si, MeshContext mc)
        {
            if (_receiver?.Project == null) return;
            if (mi == 0 && si == 0 && mc.UnityMesh != null)
                // Phase 2a-2d: ResetToMesh → EnterCameraChanged(Reset) に集約。
                _viewportManager.EnterCameraChanged(
                    _viewportManager.PerspectiveViewport,
                    CameraChangePhase.Reset,
                    mc.UnityMesh.bounds);
            _moveToolHandler?.SetProject(ActiveProject);
            _objectMoveHandler?.SetProject(ActiveProject);
            _pivotOffsetHandler?.SetProject(ActiveProject);
            _sculptHandler?.SetProject(ActiveProject);
            _advancedSelectHandler?.SetProject(ActiveProject);
            _skinWeightPaintHandler?.SetProject(ActiveProject);
            _alignVerticesHandler?.SetProject(ActiveProject);
            _pipeAlignHandler?.SetProject(ActiveProject);
            _planarizeAlongBonesHandler?.SetProject(ActiveProject);
                _mergeVerticesHandler?.SetProject(ActiveProject);
                _splitVerticesHandler?.SetProject(ActiveProject);
                _lineExtrudeHandler?.SetProject(ActiveProject);
                _vertexHoleHandler?.SetProject(ActiveProject);
                _addFaceHandler?.SetProject(ActiveProject);
                _flipFaceHandler?.SetProject(ActiveProject);
                _rotateHandler?.SetProject(ActiveProject);
                _scaleHandler?.SetProject(ActiveProject);
                _edgeBevelHandler?.SetProject(ActiveProject);
                _edgeExtrudeHandler?.SetProject(ActiveProject);
                _faceExtrudeHandler?.SetProject(ActiveProject);
                _edgeRibbonFaceHandler?.SetProject(ActiveProject);
                _edgeTopologyHandler?.SetProject(ActiveProject);
                _knifeHandler?.SetProject(ActiveProject);
                _solidifyHandler?.SetProject(ActiveProject);
                _deleteSelectionHandler?.SetProject(ActiveProject);
                _vertexDissolveHandler?.SetProject(ActiveProject);
                _tri4To1Handler?.SetProject(ActiveProject);
                _faceMergeHandler?.SetProject(ActiveProject);
                _quad4To1Handler?.SetProject(ActiveProject);
                _edgeBridgeHandler?.SetProject(ActiveProject);
                _holeRingCountHandler?.SetProject(ActiveProject);
            // 受信中はフル GPU 再構築を抑止（完了時 EnterSceneReset で1回だけ行う）。
            if (!_suppressRebuildDuringFetch)
            {
                RebuildModelList();
                NotifyPanels(ChangeKind.ListStructure);
            }
        }

        // ================================================================
        // フェッチフロー
        // ================================================================

        private void FetchProject()
        {
            _fetchFlow?.FetchProject();
        }
    }
}
