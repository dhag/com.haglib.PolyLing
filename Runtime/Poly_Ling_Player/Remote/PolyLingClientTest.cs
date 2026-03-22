// PolyLingClientTest.cs
// PolyLingPlayerClient の動作確認用コンポーネント
// PolyLingPlayerClient と同じ GameObjectにアタッチする

using System.Collections.Generic;
using Poly_Ling.Remote;
using UnityEngine;

namespace Poly_Ling.Player
{
    public class PolyLingClientTest : MonoBehaviour
    {
        private PolyLingPlayerClient _client;
        private int _modelCount;
        private int _currentModelIndex;

        private void Start()
        {
            _client = GetComponent<PolyLingPlayerClient>();
            if (_client == null)
            {
                Debug.LogError("[PolyLingClientTest] PolyLingPlayerClient が見つかりません");
                return;
            }
            _client.OnPushReceived = json => Debug.Log($"[PolyLingClientTest] Push: {json}");
        }

        private void OnGUI()
        {
            float x = 10, y = 10, w = 180, h = 36, pad = 6;

            if (!_client.IsConnected)
            {
                if (GUI.Button(new Rect(x, y, w, h), "Connect"))
                    _client.Connect();
            }
            else
            {
                if (GUI.Button(new Rect(x, y, w, h), "Disconnect"))
                    _client.Disconnect();

                y += h + pad;
                if (GUI.Button(new Rect(x, y, w, h), "Fetch Project"))
                    FetchProject();
            }
        }

        // ================================================================
        // プロジェクト完全フェッチ（RemoteClientV3.FetchAllModelsBatch 相当）
        // ================================================================

        private void FetchProject()
        {
            Debug.Log("[PolyLingClientTest] project_header フェッチ開始");
            _client.FetchProjectHeader((json, bin) =>
            {
                if (bin == null || bin.Length < 4) { Debug.LogWarning("[PolyLingClientTest] バイナリなし"); return; }

                foreach (var frame in SplitBatch(bin))
                {
                    uint magic = RemoteMagic.Read(frame);
                    if (magic == RemoteMagic.ProjectHeader)
                    {
                        var r = RemoteProgressiveSerializer.DeserializeProjectHeader(frame);
                        if (r != null)
                        {
                            var (name, mc, ci) = r.Value;
                            _modelCount = mc;
                            _currentModelIndex = ci;
                            Debug.Log($"[PLRH] project=\"{name}\" models={mc} currentModel={ci}");
                        }
                    }
                    else if (magic == RemoteMagic.ModelMeta)
                    {
                        var r = RemoteProgressiveSerializer.DeserializeModelMeta(frame);
                        if (r != null)
                        {
                            var (mi, model) = r.Value;
                            Debug.Log($"[PLRM] [{mi}] \"{model.Name}\" meshes={model.Count}");
                        }
                    }
                    else if (magic == RemoteMagic.MeshSummary)
                    {
                        var r = RemoteProgressiveSerializer.DeserializeMeshSummary(frame);
                        if (r != null)
                        {
                            var (mi, si, mc, vc, fc) = r.Value;
                            Debug.Log($"[PLRS] [{mi}][{si}] \"{mc.Name}\" V={vc} F={fc}");
                        }
                    }
                }

                // 全モデルのメッシュデータをフェッチ
                if (_modelCount > 0)
                    FetchAllModelsBatch(0);
            });
        }

        private void FetchAllModelsBatch(int mi)
        {
            if (mi >= _modelCount) return;
            FetchMeshDataBatch(mi, "bone", () =>
                FetchMeshDataBatch(mi, "drawable", () =>
                    FetchMeshDataBatch(mi, "morph", () =>
                    {
                        int next = mi + 1;
                        if (next < _modelCount)
                            FetchAllModelsBatch(next);
                        else
                            Debug.Log("[PolyLingClientTest] フェッチ完了");
                    })));
        }

        private void FetchMeshDataBatch(int mi, string cat, System.Action done = null)
        {
            _client.FetchMeshDataBatch(mi, cat, (json, bin) =>
            {
                Debug.Log($"[PolyLingClientTest] mesh_data_batch [{mi}] {cat}: {json}");
                if (bin != null && bin.Length >= 4)
                {
                    foreach (var frame in SplitBatch(bin))
                    {
                        uint magic = RemoteMagic.Read(frame);
                        if (magic == RemoteMagic.MeshData)
                        {
                            var r = RemoteProgressiveSerializer.DeserializeMeshData(frame);
                            if (r != null)
                            {
                                var (rmi, si, mesh) = r.Value;
                                Debug.Log($"[PLRD] [{rmi}][{si}] \"{mesh?.Name}\" V={mesh?.VertexCount} F={mesh?.FaceCount}");
                            }
                        }
                    }
                }
                done?.Invoke();
            });
        }

        // ================================================================
        // バッチ分解
        // ================================================================

        private static List<byte[]> SplitBatch(byte[] data)
        {
            var result = new List<byte[]>();
            if (data == null || data.Length < 4) return result;

            uint magic = RemoteMagic.Read(data);
            if (magic != RemoteMagic.Batch) { result.Add(data); return result; }

            if (data.Length < 12) return result;
            int frameCount = (int)System.BitConverter.ToUInt32(data, 8);
            int offset = 12;
            for (int i = 0; i < frameCount; i++)
            {
                if (offset + 4 > data.Length) break;
                int len = (int)System.BitConverter.ToUInt32(data, offset); offset += 4;
                if (offset + len > data.Length) break;
                byte[] frame = new byte[len];
                System.Array.Copy(data, offset, frame, 0, len);
                result.Add(frame);
                offset += len;
            }
            return result;
        }
    }
}
