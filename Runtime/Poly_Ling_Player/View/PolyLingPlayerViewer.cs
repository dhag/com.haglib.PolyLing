// PolyLingPlayerViewer.cs
// プレイヤービルド用メッシュビューア（全体統括MonoBehaviour）
// 役割:
//   - ライフサイクル管理 (Awake/Start/Update/LateUpdate/OnDestroy)
//   - カメラオービット操作
//   - OnGUI（ステータス・ボタン表示）
//   - RemoteProjectReceiver / MeshSceneRenderer / PlayerEditOps の配線
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.UndoSystem;

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
        // 描画フラグ（Inspector）
        // ================================================================

        [Header("Display Flags")]
        [SerializeField] private bool _showSelectedMesh            = true;
        [SerializeField] private bool _showUnselectedMesh          = true;
        [SerializeField] private bool _showSelectedVertices        = true;
        [SerializeField] private bool _showUnselectedVertices      = true;
        [SerializeField] private bool _showSelectedWireframe       = true;
        [SerializeField] private bool _showUnselectedWireframe     = true;
        [SerializeField] private bool _showSelectedBone            = true;
        [SerializeField] private bool _showUnselectedBone          = false;

        [Header("Rendering")]
        [SerializeField] private bool _backfaceCullingEnabled      = true;

        // ================================================================
        // カメラオービット
        // ================================================================

        private float   _orbitRotX   =  20f;
        private float   _orbitRotY   =   0f;
        private float   _orbitDist   =   3f;
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
        // サブクラス
        // ================================================================

        private RemoteProjectReceiver _receiver;
        private MeshSceneRenderer     _renderer;
        private readonly UndoManager  _undoManager = UndoManager.CreateNew();
        private PlayerEditOps         _editOps;

        // ================================================================
        // ステータス
        // ================================================================

        private string _status = "未接続";
        private int    _fetchingModelIndex = -1;
        private int    _modelCount;

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

            _receiver = new RemoteProjectReceiver();
            _renderer = new MeshSceneRenderer();
            _editOps  = new PlayerEditOps(_undoManager);

            // レンダラーにフラグを反映
            SyncRendererFlags();

            // 受信イベント配線
            _receiver.OnProjectHeaderReceived += OnProjectHeaderReceived;
            _receiver.OnModelMetaReceived     += OnModelMetaReceived;
            _receiver.OnMeshSummaryReceived   += OnMeshSummaryReceived;
            _receiver.OnMeshDataReceived      += OnMeshDataReceived;

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

            if (_receiver != null)
            {
                _receiver.OnProjectHeaderReceived -= OnProjectHeaderReceived;
                _receiver.OnModelMetaReceived     -= OnModelMetaReceived;
                _receiver.OnMeshSummaryReceived   -= OnMeshSummaryReceived;
                _receiver.OnMeshDataReceived      -= OnMeshDataReceived;
            }

            _editOps?.Dispose();
            _editOps = null;
            _renderer?.Dispose();
            _renderer = null;
        }

        private void Update()
        {
            _editOps?.Tick();
            UpdateOrbitCamera();
            SyncRendererFlags();
        }

        private void LateUpdate()
        {
            var project = _receiver?.Project;
            var cam = ActiveCamera;
            _renderer?.DrawMeshes(project, cam);
            _renderer?.DrawWireframeAndVertices(cam);
            _renderer?.DrawBones(project, cam);
        }

        // ================================================================
        // カメラ
        // ================================================================

        private Camera ActiveCamera => _camera != null ? _camera : Camera.main;

        private void UpdateOrbitCamera()
        {
            var cam = ActiveCamera;
            if (cam == null) return;

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            { _isDragging = true; _prevOrbitPos = Input.mousePosition; }
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

            if (Input.GetMouseButtonDown(2))
            { _isPanning = true; _prevPanPos = Input.mousePosition; }
            if (Input.GetMouseButtonUp(2))
                _isPanning = false;

            if (_isPanning && Input.GetMouseButton(2))
            {
                Vector2 delta = (Vector2)Input.mousePosition - _prevPanPos;
                Quaternion rot = Quaternion.Euler(_orbitRotX, _orbitRotY, 0f);
                float panScale = _orbitDist * PanSensitivity;
                _orbitTarget  -= rot * Vector3.right * delta.x * panScale;
                _orbitTarget  -= rot * Vector3.up    * delta.y * panScale;
                _prevPanPos    = Input.mousePosition;
            }

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

                y += h + pad;
                bool prevEnabled = GUI.enabled;
                GUI.enabled = _editOps != null && _editOps.CanUndo;
                if (GUI.Button(new Rect(x, y, w * 0.5f - 2f, h), "Undo"))
                    _editOps?.PerformUndo();
                GUI.enabled = _editOps != null && _editOps.CanRedo;
                if (GUI.Button(new Rect(x + w * 0.5f + 2f, y, w * 0.5f - 2f, h), "Redo"))
                    _editOps?.PerformRedo();
                GUI.enabled = prevEnabled;
            }

            var project = _receiver?.Project;
            if (project != null)
            {
                y += h + pad;
                GUI.Label(new Rect(x, y, w * 2f, h),
                    $"Project: {project.Name}  Models: {project.ModelCount}");

                for (int mi = 0; mi < project.ModelCount; mi++)
                {
                    y += h + pad * 0.5f;
                    var model = project.Models[mi];
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
        // 受信イベント
        // ================================================================

        private void OnProjectHeaderReceived(ProjectContext project)
        {
            _modelCount = project.ModelCount;
            _fetchingModelIndex = -1;
            _renderer?.ClearScene();
        }

        private void OnModelMetaReceived(int mi, ModelContext model) { }

        private void OnMeshSummaryReceived(int mi, int si, Data.MeshContext mc) { }

        private void OnMeshDataReceived(int mi, int si, Data.MeshContext mc)
        {
            var project = _receiver?.Project;
            if (project == null) return;

            // 初回メッシュでカメラ位置を初期化
            if (mi == 0 && si == 0 && mc.UnityMesh != null)
            {
                _orbitTarget = mc.UnityMesh.bounds.center;
                _orbitDist   = Mathf.Clamp(mc.UnityMesh.bounds.size.magnitude * 1.5f, ZoomMin, ZoomMax);
            }
        }

        // ================================================================
        // フェッチフロー
        // ================================================================

        private void FetchProject()
        {
            if (!_client.IsConnected) return;
            _status = "project_header フェッチ中...";
            _receiver?.Reset();
            _modelCount = 0;
            _fetchingModelIndex = -1;
            _renderer?.ClearScene();

            _client.FetchProjectHeader((json, bin) =>
            {
                if (bin == null || bin.Length < 4) { _status = "project_header 失敗"; return; }
                _receiver?.ProcessBatch(bin);
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
                var project = _receiver?.Project;
                if (project != null && mi < project.ModelCount)
                    _renderer?.RebuildAdapter(mi, project.Models[mi]);

                int next = mi + 1;
                if (next < _modelCount)
                    FetchAllModelsBatch(next);
                else
                    _status = $"完了 ({project?.Name})";
            })));
        }

        private void FetchMeshDataBatch(int mi, string cat, Action done)
        {
            _client.FetchMeshDataBatch(mi, cat, (json, bin) =>
            {
                if (bin != null && bin.Length >= 4)
                    _receiver?.ProcessBatch(bin);
                done?.Invoke();
            });
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void SyncRendererFlags()
        {
            if (_renderer == null) return;
            _renderer.ShowSelectedMesh        = _showSelectedMesh;
            _renderer.ShowUnselectedMesh      = _showUnselectedMesh;
            _renderer.ShowSelectedVertices    = _showSelectedVertices;
            _renderer.ShowUnselectedVertices  = _showUnselectedVertices;
            _renderer.ShowSelectedWireframe   = _showSelectedWireframe;
            _renderer.ShowUnselectedWireframe = _showUnselectedWireframe;
            _renderer.ShowSelectedBone        = _showSelectedBone;
            _renderer.ShowUnselectedBone      = _showUnselectedBone;
            _renderer.BackfaceCullingEnabled  = _backfaceCullingEnabled;
        }
    }
}
