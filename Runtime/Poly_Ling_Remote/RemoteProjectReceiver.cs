// RemoteProjectReceiver.cs
// WebSocketで受信したバイナリデータをProjectContextに反映する。
// Runtime/Editor両方から使用可能なplain C#クラス。
// エディタ側(RemoteClientV3)も将来このクラスに委譲する。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Remote
{
    /// <summary>
    /// WebSocket受信バイナリ → ProjectContext反映クラス。
    /// UI固有処理（Repaint、RebuildTree等）はイベント経由で呼び出し元に委ねる。
    /// </summary>
    public class RemoteProjectReceiver
    {
        // ================================================================
        // 状態
        // ================================================================

        private ProjectContext _project;

        public ProjectContext Project => _project;

        // ================================================================
        // イベント（呼び出し元がUI更新等を接続する）
        // ================================================================

        /// <summary>ProjectHeaderを受信してProjectContextを再構築した</summary>
        public event Action<ProjectContext> OnProjectHeaderReceived;

        /// <summary>ModelMetaを受信してModelContextを更新した</summary>
        public event Action<int, ModelContext> OnModelMetaReceived;

        /// <summary>MeshSummaryを受信してMeshContextを更新した</summary>
        public event Action<int, int, MeshContext> OnMeshSummaryReceived;

        /// <summary>
        /// MeshSummary受信時の頂点数/面数（modelIndex, meshIndex, vertexCount, faceCount）。
        /// MeshContext.VertexCount は MeshObject 由来のため、ジオメトリ未取得のクライアントでは
        /// このイベントで頂点数/面数を受け取る。
        /// </summary>
        public event Action<int, int, int, int> OnMeshSummaryCounts;

        /// <summary>MeshDataを受信してMeshContextにMeshObjectを設定した</summary>
        public event Action<int, int, MeshContext> OnMeshDataReceived;

        // ================================================================
        // バッチ処理
        // ================================================================

        public void ProcessBatch(byte[] data)
        {
            if (data == null || data.Length < 4) return;

            uint magic = RemoteMagic.Read(data);
            if (magic != RemoteMagic.Batch)
            {
                DispatchFrame(magic, data);
                return;
            }

            if (data.Length < 12) return;
            int frameCount = (int)BitConverter.ToUInt32(data, 8);
            int offset = 12;
            for (int i = 0; i < frameCount; i++)
            {
                if (offset + 4 > data.Length) break;
                int len = (int)BitConverter.ToUInt32(data, offset); offset += 4;
                if (offset + len > data.Length) break;
                byte[] frame = new byte[len];
                Array.Copy(data, offset, frame, 0, len);
                DispatchFrame(RemoteMagic.Read(frame), frame);
                offset += len;
            }
        }

        public void DispatchFrame(uint magic, byte[] data)
        {
            if      (magic == RemoteMagic.ProjectHeader) ReceiveProjectHeader(data);
            else if (magic == RemoteMagic.ModelMeta)     ReceiveModelMeta(data);
            else if (magic == RemoteMagic.MeshSummary)   ReceiveMeshSummary(data);
            else if (magic == RemoteMagic.MeshData)      ReceiveMeshData(data);
        }

        // ================================================================
        // 受信ハンドラ
        // ================================================================

        private void ReceiveProjectHeader(byte[] data)
        {
            var r = RemoteProgressiveSerializer.DeserializeProjectHeader(data);
            if (r == null) { Debug.LogWarning("[RemoteProjectReceiver] PLRH失敗"); return; }

            var (name, mc, ci) = r.Value;
            _project = new ProjectContext { Name = name };
            for (int i = 0; i < mc; i++)
                _project.Models.Add(new ModelContext($"Model{i}"));
            _project.CurrentModelIndex = ci;

            Debug.Log($"[RemoteProjectReceiver] PLRH: \"{name}\" {mc}モデル");
            OnProjectHeaderReceived?.Invoke(_project);
        }

        private void ReceiveModelMeta(byte[] data)
        {
            var r = RemoteProgressiveSerializer.DeserializeModelMeta(data);
            if (r == null || _project == null) return;

            var (mi, model) = r.Value;
            EnsureModelSlot(mi);

            // 旧モデル差し替え時のリーク対策：旧メッシュを破棄する。
            // 【残存リスク】旧 runtime 材質/テクスチャはここでは破棄しない（destroyMaterials:false）。
            //   ビューワの GPU アダプタ（_pendingMaterialsBySlot）が RebuildAdapter まで旧材質を
            //   参照し続けるため、ここで破棄すると破棄済み材質で描画してしまう。確実な解放には
            //   レンダラが旧材質を手放した後（アダプタ Dispose 後）に DestroyRuntimeMaterial() を
            //   呼ぶ経路が必要（次段の課題）。→ ビューワではフェッチ反復で材質リークが残存する。
            if (mi >= 0 && mi < _project.Models.Count)
                DestroyModelRuntimeObjects(_project.Models[mi], destroyMaterials: false);

            _project.Models[mi] = model;

            Debug.Log($"[RemoteProjectReceiver] PLRM: [{mi}] \"{model.Name}\"");
            OnModelMetaReceived?.Invoke(mi, model);
        }

        private void ReceiveMeshSummary(byte[] data)
        {
            var r = RemoteProgressiveSerializer.DeserializeMeshSummary(data);
            if (r == null || _project == null) return;

            var (mi, si, mc, vc, fc) = r.Value;
            EnsureModelSlot(mi);
            var model = _project.Models[mi];
            while (model.MeshContextList.Count <= si)
                model.MeshContextList.Add(new MeshContext { Name = $"Mesh{model.MeshContextList.Count}" });
            model.MeshContextList[si] = mc;
            model.InvalidateTypedIndices();

            OnMeshSummaryReceived?.Invoke(mi, si, mc);
            OnMeshSummaryCounts?.Invoke(mi, si, vc, fc);
        }

        private void ReceiveMeshData(byte[] data)
        {
            var r = RemoteProgressiveSerializer.DeserializeMeshData(data);
            if (r == null || _project == null) return;

            var (mi, si, mesh) = r.Value;
            EnsureModelSlot(mi);
            var model = _project.Models[mi];

            while (model.MeshContextList.Count <= si)
                model.MeshContextList.Add(new MeshContext { Name = $"Mesh{model.MeshContextList.Count}" });

            var mc = model.MeshContextList[si];
            string savedName = mc.Name;
            MeshType savedType = mc.Type;

            if (mc.UnityMesh != null)
            {
                UnityEngine.Object.Destroy(mc.UnityMesh);
                mc.UnityMesh = null;
            }

            mc.MeshObject = mesh;
            if (mesh != null)
            {
                mesh.Name = savedName;
                mesh.Type = savedType;
                if (mesh.VertexCount > 0)
                    mc.UnityMesh = mesh.ToUnityMesh();
            }

            Debug.Log($"[RemoteProjectReceiver] PLRD: [{mi}][{si}] \"{savedName}\" V={mesh?.VertexCount ?? 0}");
            OnMeshDataReceived?.Invoke(mi, si, mc);
        }

        // ================================================================
        // リセット
        // ================================================================

        public void Reset()
        {
            // list クライアントのフェッチ前リセット（レンダラ/GPU アダプタを持たない）。
            // レンダラが材質を参照していないため、ここで runtime 材質・テクスチャ・メッシュを
            // 破棄しても安全（フェッチ毎の Material/Texture2D/Mesh リークを解消する）。
            // ※ ビューワ（GPU アダプタ持ち）はこの Reset を呼ばない。呼ぶ場合は破棄安全性の再検討が必要。
            if (_project?.Models != null)
                foreach (var m in _project.Models)
                    DestroyModelRuntimeObjects(m, destroyMaterials: true);

            _project = null;
        }

        /// <summary>
        /// モデルが保持する runtime Unity Object を破棄する（リーク対策）。
        /// メッシュ（ToUnityMesh 生成）は常に破棄。ビューワの GPU 描画は GPU バッファから行い
        /// ctx.UnityMesh は bounds 用のため、置換時の破棄は安全（既存 ReceiveMeshData:156 と同方針）。
        /// 材質破棄は destroyMaterials=true のときのみ（レンダラが当該材質を参照していない前提）。
        /// </summary>
        private static void DestroyModelRuntimeObjects(ModelContext m, bool destroyMaterials)
        {
            if (m == null) return;

            if (m.MeshContextList != null)
                foreach (var mc in m.MeshContextList)
                    if (mc?.UnityMesh != null)
                    {
                        UnityEngine.Object.Destroy(mc.UnityMesh);
                        mc.UnityMesh = null;
                    }

            if (destroyMaterials && m.MaterialReferences != null)
                foreach (var mr in m.MaterialReferences)
                    mr?.DestroyRuntimeMaterial();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void EnsureModelSlot(int mi)
        {
            if (_project == null) return;
            while (_project.Models.Count <= mi)
                _project.Models.Add(new ModelContext($"Model{_project.Models.Count}"));
        }
    }
}
