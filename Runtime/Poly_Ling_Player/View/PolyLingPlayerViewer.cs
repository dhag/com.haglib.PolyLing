// PolyLingPlayerViewer.cs
// プレイヤービルド用メッシュビューア
// PolyLingPlayerClient からデータを受信し、シーン上にメッシュを生成して表示する
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Core;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;
using Poly_Ling.Core.Rendering;

namespace Poly_Ling.Player
{
    public class PolyLingPlayerViewer : MonoBehaviour
    {
        // ================================================================
        // Inspector設定
        // ================================================================

        [SerializeField] private PolyLingPlayerClient _client;

        /// <summary>メッシュ生成の親Transform。nullの場合はthis.transform</summary>
        [SerializeField] private Transform _sceneRoot;

        /// <summary>カメラオービット対象カメラ。nullの場合はCamera.main</summary>
        [SerializeField] private Camera _camera;

        // ================================================================
        // デバッグ表示フラグ（Inspector）
        // ================================================================

        [Header("Display Flags")]
        [SerializeField] private bool _showSelectedMesh            = true;   // A
        [SerializeField] private bool _showUnselectedMesh          = true;   // B
        [SerializeField] private bool _showSelectedVertices        = true;   // C
        [SerializeField] private bool _showUnselectedVertices      = true;   // D
        [SerializeField] private bool _showSelectedWireframe       = true;   // E
        [SerializeField] private bool _showUnselectedWireframe     = true;   // F
        [SerializeField] private bool _showSelectedBone            = false;  // E(bone)
        [SerializeField] private bool _showUnselectedBone          = false;  // F(bone)
        [SerializeField] private bool _showSelectedVertexIndex     = false;  // G
        [SerializeField] private bool _showSelectedTransform       = false;  // H/I

        // ================================================================
        // カメラオービット
        // ================================================================

        private float _orbitRotX    =  20f;
        private float _orbitRotY    =   0f;
        private float _orbitDist    =   3f;
        private Vector3 _orbitTarget = Vector3.zero;

        private const float OrbitSensitivity = 0.5f;
        private const float ZoomSensitivity  = 0.05f;
        private const float PanSensitivity   = 0.002f;
        private const float ZoomMin          = 0.05f;
        private const float ZoomMax          = 100f;

        private Vector2 _prevOrbitPos;
        private Vector2 _prevPanPos;
        private bool    _isDragging;
        private bool    _isPanning;

        // ================================================================
        // データ
        // ================================================================

        private ProjectContext _project;
        private int            _fetchingModelIndex = -1;
        private int            _modelCount;

        // ================================================================
        // デフォルトマテリアル（フォールバック用）
        // ================================================================

        private Material _defaultMaterial;

        // ================================================================
        // レンダラー（モデル単位）
        // ================================================================

        /// <summary>モデル単位の UnifiedSystemAdapter。インデックスはモデルインデックスと一致</summary>
        private readonly List<UnifiedSystemAdapter> _adapters = new List<UnifiedSystemAdapter>();

        /// <summary>モデル単位の選択中MeshContextListインデックス（-1=なし）</summary>
        private readonly List<int> _selectedContextIndices = new List<int>();

        // ================================================================
        // ステータス
        // ================================================================

        private string _status = "未接続";

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void Awake()
        {
            if (_sceneRoot == null) _sceneRoot = transform;
        }

        private void Start()
        {
            if (_client == null)
            {
                _client = GetComponent<PolyLingPlayerClient>();
                if (_client == null)
                {
                    Debug.LogError("[PolyLingPlayerViewer] PolyLingPlayerClient が見つかりません");
                    return;
                }
            }

            _client.OnConnected    += OnConnected;
            _client.OnDisconnected += OnDisconnected;
            _client.OnPushReceived += OnPushReceived;
        }

        private void OnDestroy()
        {
            if (_client != null)
            {
                _client.OnConnected    -= OnConnected;
                _client.OnDisconnected -= OnDisconnected;
                _client.OnPushReceived -= OnPushReceived;
            }
            ClearScene();
        }

        private void Update()
        {
            UpdateOrbitCamera();
        }

        private void LateUpdate()
        {
            DrawMeshes();
            DrawWireframeAndVertices();
        }

        // ================================================================
        // カメラオービット
        // ================================================================

        private void UpdateOrbitCamera()
        {
            var cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return;

            // 左/右ドラッグ: 回転
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                _isDragging  = true;
                _prevOrbitPos = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1))
                _isDragging = false;

            if (_isDragging && (Input.GetMouseButton(0) || Input.GetMouseButton(1)))
            {
                Vector2 delta = (Vector2)Input.mousePosition - _prevOrbitPos;
                _orbitRotY   += delta.x * OrbitSensitivity;
                _orbitRotX   -= delta.y * OrbitSensitivity;
                _orbitRotX    = Mathf.Clamp(_orbitRotX, -89f, 89f);
                _prevOrbitPos = Input.mousePosition;
            }

            // 中ドラッグ: パン
            if (Input.GetMouseButtonDown(2))
            {
                _isPanning   = true;
                _prevPanPos  = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(2))
                _isPanning = false;

            if (_isPanning && Input.GetMouseButton(2))
            {
                Vector2 delta = (Vector2)Input.mousePosition - _prevPanPos;
                Quaternion rot = Quaternion.Euler(_orbitRotX, _orbitRotY, 0f);
                Vector3 right = rot * Vector3.right;
                Vector3 up    = rot * Vector3.up;
                float panScale = _orbitDist * PanSensitivity;
                _orbitTarget  -= right * delta.x * panScale;
                _orbitTarget  -= up    * delta.y * panScale;
                _prevPanPos    = Input.mousePosition;
            }

            // スクロール: ズーム
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _orbitDist *= 1f + scroll * ZoomSensitivity;
                _orbitDist  = Mathf.Clamp(_orbitDist, ZoomMin, ZoomMax);
            }

            Quaternion camRot = Quaternion.Euler(_orbitRotX, _orbitRotY, 0f);
            cam.transform.position = _orbitTarget + camRot * (Vector3.back * _orbitDist);
            cam.transform.LookAt(_orbitTarget);
        }

        // ================================================================
        // メッシュ描画（ViewportCore.DrawMeshes と同ロジック）
        // ================================================================

        private void DrawMeshes()
        {
            if (_project == null) return;
            var cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return;

            for (int mi = 0; mi < _project.ModelCount; mi++)
            {
                var model = _project.Models[mi];
                var drawables = model.DrawableMeshes;
                if (drawables == null) continue;

                int selectedCtx = (mi < _selectedContextIndices.Count) ? _selectedContextIndices[mi] : -1;

                for (int i = 0; i < drawables.Count; i++)
                {
                    bool isSelected = (drawables[i].MasterIndex == selectedCtx);

                    if (isSelected && !_showSelectedMesh)   continue;
                    if (!isSelected && !_showUnselectedMesh) continue;

                    var ctx = drawables[i].Context;
                    if (ctx?.UnityMesh == null || !ctx.IsVisible) continue;

                    var mesh = ctx.UnityMesh;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        Material mat = (sub < model.MaterialCount) ? model.GetMaterial(sub) : null;
                        if (mat == null) mat = GetDefaultMaterial();
                        if (mat == null) continue;
                        Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, cam, sub);
                    }
                }
            }
        }

        private Material GetDefaultMaterial()
        {
            if (_defaultMaterial != null) return _defaultMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                      ?? Shader.Find("Standard")
                      ?? Shader.Find("Unlit/Color");
            if (shader == null) return null;
            _defaultMaterial = new Material(shader);
            _defaultMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f));
            _defaultMaterial.SetColor("_Color",     new Color(0.7f, 0.7f, 0.7f));
            return _defaultMaterial;
        }

        // ================================================================
        // ワイヤーフレーム・頂点描画
        // ================================================================

        private void DrawWireframeAndVertices()
        {
            var cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return;

            cam.cullingMask = -1;

            float pointSize = ShaderColorSettings.Default.VertexPointScale;

            for (int mi = 0; mi < _adapters.Count; mi++)
            {
                var adapter = _adapters[mi];
                if (adapter == null || !adapter.IsInitialized) continue;

                adapter.CleanupQueued();

                bool showWire    = _showSelectedWireframe;
                bool showVerts   = _showSelectedVertices;
                bool showUnsWire = _showUnselectedWireframe;
                bool showUnsVert = _showUnselectedVertices;

                adapter.PrepareDrawing(
                    cam,
                    showWireframe:            showWire,
                    showVertices:             showVerts,
                    showUnselectedWireframe:  showUnsWire,
                    showUnselectedVertices:   showUnsVert,
                    selectedMeshIndex:        -1,
                    pointSize:                pointSize);
                adapter.ConsumeNormalMode();

                adapter.DrawQueued(cam);
            }
        }

        // ================================================================
        // OnGUI
        // ================================================================

        private void OnGUI()
        {
            float x = 10f, y = 10f, w = 200f, h = 30f, pad = 6f;

            GUI.Label(new Rect(x, y, w * 2f, h), $"Status: {_status}");
            y += h + pad;

            if (_client == null) return;

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

            if (_project != null)
            {
                y += h + pad;
                GUI.Label(new Rect(x, y, w * 2f, h), $"Project: {_project.Name}  Models: {_project.ModelCount}");

                for (int mi = 0; mi < _project.ModelCount; mi++)
                {
                    y += h + pad * 0.5f;
                    var model = _project.Models[mi];
                    GUI.Label(new Rect(x + 10f, y, w * 2f, h),
                        $"[{mi}] {model.Name}  Meshes: {model.Count}");
                }
            }

            float hy = Screen.height - h - 10f;
            GUI.Label(new Rect(x, hy, 400f, h), "右ドラッグ: 回転 / 中ドラッグ: 移動 / スクロール: ズーム");
        }

        // ================================================================
        // クライアントイベント
        // ================================================================

        private void OnConnected()
        {
            _status = "接続済み";
            Debug.Log("[PolyLingPlayerViewer] 接続");
        }

        private void OnDisconnected()
        {
            _status = "切断";
            _fetchingModelIndex = -1;
            Debug.Log("[PolyLingPlayerViewer] 切断");
        }

        private void OnPushReceived(string json)
        {
            if (json.Contains("\"event\":\"mesh_changed\"") ||
                json.Contains("\"event\":\"model_changed\""))
            {
                Debug.Log("[PolyLingPlayerViewer] Push受信 → 再フェッチ");
                FetchProject();
            }
        }

        // ================================================================
        // フェッチフロー
        // ================================================================

        private void FetchProject()
        {
            if (!_client.IsConnected) return;
            _status = "project_header フェッチ中...";
            _project = null;
            _modelCount = 0;
            _fetchingModelIndex = -1;
            ClearScene();

            _client.FetchProjectHeader((json, bin) =>
            {
                if (bin == null || bin.Length < 4)
                {
                    _status = "project_header 失敗";
                    return;
                }
                ProcessBatch(bin);
                if (_modelCount > 0)
                    FetchAllModelsBatch(0);
            });
        }

        private void FetchAllModelsBatch(int mi)
        {
            if (mi >= _modelCount) return;
            _fetchingModelIndex = mi;
            _status = $"メッシュフェッチ中... [{mi}/{_modelCount - 1}]";

            FetchMeshDataBatch(mi, "bone", () =>
            FetchMeshDataBatch(mi, "drawable", () =>
            FetchMeshDataBatch(mi, "morph", () =>
            {
                // モデル単位で全batch完了後に1回だけAdapter構築
                if (_project != null && mi < _project.ModelCount)
                    RebuildAdapter(mi, _project.Models[mi]);

                int next = mi + 1;
                if (next < _modelCount)
                    FetchAllModelsBatch(next);
                else
                    _status = $"完了 ({_project?.Name})";
            })));
        }

        private void FetchMeshDataBatch(int mi, string cat, Action done)
        {
            _client.FetchMeshDataBatch(mi, cat, (json, bin) =>
            {
                if (bin != null && bin.Length >= 4)
                    ProcessBatch(bin);
                done?.Invoke();
            });
        }

        // ================================================================
        // バッチ分解 → フレームディスパッチ
        // ================================================================

        private void ProcessBatch(byte[] data)
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

        private void DispatchFrame(uint magic, byte[] data)
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
            if (r == null) { Debug.LogWarning("[PolyLingPlayerViewer] PLRH失敗"); return; }

            var (name, mc, ci) = r.Value;
            _project = new ProjectContext { Name = name };
            for (int i = 0; i < mc; i++)
                _project.Models.Add(new ModelContext($"Model{i}"));
            _project.CurrentModelIndex = ci;
            _modelCount = mc;

            Debug.Log($"[PolyLingPlayerViewer] PLRH: \"{name}\" {mc}モデル");
        }

        private void ReceiveModelMeta(byte[] data)
        {
            var r = RemoteProgressiveSerializer.DeserializeModelMeta(data);
            if (r == null || _project == null) return;

            var (mi, model) = r.Value;
            EnsureModelSlot(mi);
            _project.Models[mi] = model;
            Debug.Log($"[PolyLingPlayerViewer] PLRM: [{mi}] \"{model.Name}\"");
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
                Destroy(mc.UnityMesh);
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

            Debug.Log($"[PolyLingPlayerViewer] PLRD: [{mi}][{si}] \"{savedName}\" V={mesh?.VertexCount ?? 0}");

            ApplyMeshContextToScene(mi, si, mc);
        }

        // ================================================================
        // シーン生成
        // ================================================================

        private void ApplyMeshContextToScene(int mi, int si, MeshContext mc)
        {
            // mc.UnityMesh は ReceiveMeshData で生成済み。
            // 描画は LateUpdate の DrawMeshes() で Graphics.DrawMesh を使って行う。
            // ここではオービットターゲットの初期化のみ行う。
            if (mc.UnityMesh == null) return;
            if (mc.Type != MeshType.Mesh && mc.Type != MeshType.BakedMirror) return;

            if (mi == 0 && si == 0)
            {
                _orbitTarget = mc.UnityMesh.bounds.center;
                _orbitDist   = Mathf.Clamp(mc.UnityMesh.bounds.size.magnitude * 1.5f, ZoomMin, ZoomMax);
            }
        }

        // ================================================================
        // Adapter管理
        // ================================================================

        /// <summary>モデルのメッシュ受信完了後にAdapterを再構築する</summary>
        private void RebuildAdapter(int mi, ModelContext model)
        {
            // 既存Adapterを破棄
            while (_adapters.Count <= mi) _adapters.Add(null);
            _adapters[mi]?.Dispose();
            _adapters[mi] = null;

            // MeshObjectが1つも揃っていなければスキップ
            bool hasAnyMesh = false;
            foreach (var mc in model.MeshContextList)
                if (mc.MeshObject != null && mc.MeshObject.VertexCount > 0) { hasAnyMesh = true; break; }
            if (!hasAnyMesh) return;

            var adapter = new UnifiedSystemAdapter();
            if (!adapter.Initialize())
            {
                Debug.LogWarning($"[PolyLingPlayerViewer] Adapter初期化失敗 [{mi}]");
                adapter.Dispose();
                return;
            }

            adapter.SetSelectionState(new SelectionState());
            adapter.SetSymmetrySettings(new SymmetrySettings());
            adapter.SetModelContext(model);

            // SetModelContext → ProcessTopologyUpdate → SetActiveMesh(0,0) により
            // SelectedMeshIndex=0 が設定され全ラインに MeshSelected フラグが立つ。
            // 先頭 Drawable の unified インデックス(=0)を選択メッシュとして確定し
            // _selectedContextIndices に記録する。
            while (_selectedContextIndices.Count <= mi) _selectedContextIndices.Add(-1);

            var drawables = model.DrawableMeshes;
            int firstContextIdx = -1;
            if (drawables != null)
            {
                foreach (var entry in drawables)
                {
                    var ctx = entry.Context;
                    if (ctx?.MeshObject != null && ctx.MeshObject.VertexCount > 0 && ctx.IsVisible)
                    {
                        firstContextIdx = entry.MasterIndex;
                        break;
                    }
                }
            }
            _selectedContextIndices[mi] = firstContextIdx;

            // firstContextIdx を unified インデックスに変換して SetActiveMesh
            int firstUnified = (firstContextIdx >= 0)
                ? (adapter.BufferManager?.ContextToUnifiedMeshIndex(firstContextIdx) ?? 0)
                : 0;
            adapter.BufferManager?.SetActiveMesh(0, firstUnified);
            adapter.BufferManager?.UpdateAllSelectionFlags();

            _adapters[mi] = adapter;
        }

        // ================================================================
        // クリア
        // ================================================================

        private void ClearScene()
        {
            foreach (var adapter in _adapters)
            {
                adapter?.CleanupQueued();
                adapter?.Dispose();
            }
            _adapters.Clear();
            _selectedContextIndices.Clear();

            _project = null;

            if (_defaultMaterial != null)
            {
                DestroyImmediate(_defaultMaterial);
                _defaultMaterial = null;
            }
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
