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
            _project = null;
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
