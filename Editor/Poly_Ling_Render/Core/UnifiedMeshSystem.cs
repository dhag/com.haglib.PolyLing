// Assets/Editor/Poly_Ling/Core/UnifiedMeshSystem.cs
// 統合メッシュシステム
// 更新管理、バッファ管理、フラグ管理を統合

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Core
{
    /// <summary>
    /// 統合メッシュシステム
    /// 複数モデル・複数メッシュのデータ管理と描画を統括
    /// </summary>
    public partial class UnifiedMeshSystem : IDisposable
    {
        // ============================================================
        // コンポーネント
        // ============================================================

        private UpdateManager _updateManager;
        private FlagManager _flagManager;
        private UnifiedBufferManager _bufferManager;

        // ============================================================
        // 参照
        // ============================================================

        private ModelContext _currentModel;
        private SelectionState _selectionState;
        private SymmetrySettings _symmetrySettings;

        // カメラ情報
        private Vector3 _cameraPosition;
        private Vector3 _cameraTarget;
        private Matrix4x4 _viewMatrix;
        private Matrix4x4 _projectionMatrix;
        private Rect _viewport;
        private float _rotationZ;

        // マウス情報
        private Vector2 _mousePosition;
        private float _hitRadius = 10f;

        // ============================================================
        // 状態
        // ============================================================

        private bool _isInitialized = false;
        private bool _disposed = false;

        private int _activeModelIndex = 0;
        private int _activeMeshIndex = 0;

        // ホバー状態
        private int _hoveredVertexIndex = -1;
        private int _hoveredLineIndex = -1;
        private int _hoveredFaceIndex = -1;

        // GPU計算フラグ
        private bool _useGpuHitTest = true; // GPU版を使用
        
        // バックフェースカリング設定
        private bool _backfaceCullingEnabled = true;

        // ============================================================
        // プロパティ
        // ============================================================

        public bool IsInitialized => _isInitialized;

        public UpdateManager UpdateManager => _updateManager;
        public FlagManager FlagManager => _flagManager;
        public UnifiedBufferManager BufferManager => _bufferManager;

        public ModelContext CurrentModel => _currentModel;
        public SelectionState SelectionState => _selectionState;

        public int ActiveModelIndex => _activeModelIndex;
        public int ActiveMeshIndex => _activeMeshIndex;

        public int HoveredVertexIndex => _hoveredVertexIndex;
        public int HoveredLineIndex => _hoveredLineIndex;
        public int HoveredFaceIndex => _hoveredFaceIndex;
        
        /// <summary>バックフェースカリング有効/無効</summary>
        public bool BackfaceCullingEnabled
        {
            get => _backfaceCullingEnabled;
            set => _backfaceCullingEnabled = value;
        }

        public bool UseGpuHitTest
        {
            get => _useGpuHitTest;
            set => _useGpuHitTest = value;
        }

        // バッファへの直接アクセス
        public ComputeBuffer PositionBuffer => _bufferManager?.PositionBuffer;
        public ComputeBuffer NormalBuffer => _bufferManager?.NormalBuffer;
        public ComputeBuffer VertexFlagsBuffer => _bufferManager?.VertexFlagsBuffer;
        public ComputeBuffer LineBuffer => _bufferManager?.LineBuffer;
        public ComputeBuffer LineFlagsBuffer => _bufferManager?.LineFlagsBuffer;
        public ComputeBuffer IndexBuffer => _bufferManager?.IndexBuffer;
        public ComputeBuffer MeshInfoBuffer => _bufferManager?.MeshInfoBuffer;

        public int TotalVertexCount => _bufferManager?.TotalVertexCount ?? 0;
        public int TotalLineCount => _bufferManager?.TotalLineCount ?? 0;
        public int MeshCount => _bufferManager?.MeshCount ?? 0;

        // ============================================================
        // コンストラクタ
        // ============================================================

        public UnifiedMeshSystem()
        {
            _flagManager = new FlagManager();
            _updateManager = new UpdateManager();
            _bufferManager = new UnifiedBufferManager(_flagManager, _updateManager);

            // 更新コールバックを設定
            _updateManager.OnBeforeUpdate += OnBeforeUpdate;
            _updateManager.OnAfterUpdate += OnAfterUpdate;
        }

        // ============================================================
        // 初期化
        // ============================================================

        /// <summary>
        /// システムを初期化
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
                return;

            _bufferManager.Initialize();
            _isInitialized = true;
        }

        /// <summary>
        /// モデルを設定
        /// </summary>
        public void SetModel(ModelContext model)
        {
            _currentModel = model;
            _updateManager.MarkTopologyDirty();
        }

        /// <summary>
        /// 選択状態を設定
        /// </summary>
        public void SetSelectionState(SelectionState selectionState)
        {
            // 旧参照のイベント解除
            if (_selectionState != null)
            {
                _selectionState.OnSelectionChanged -= OnSelectionChanged;
            }

            _selectionState = selectionState;
            _flagManager.SelectionState = selectionState;

            if (selectionState != null)
            {
                selectionState.OnSelectionChanged += OnSelectionChanged;
            }
        }

        /// <summary>
        /// 対称設定を適用
        /// </summary>
        public void SetSymmetrySettings(SymmetrySettings settings)
        {
            _symmetrySettings = settings;

            if (settings != null && settings.IsEnabled)
            {
                _bufferManager.SetMirrorSettings(true, (SymmetryAxis)(int)settings.Axis, settings.PlaneOffset);
            }
            else
            {
                _bufferManager.SetMirrorSettings(false, SymmetryAxis.X);
            }

            _updateManager.MarkTransformDirty();
        }

        /// <summary>
        /// アクティブメッシュを設定
        /// </summary>
        public void SetActiveMesh(int modelIndex, int meshIndex)
        {
            if (_activeModelIndex == modelIndex && _activeMeshIndex == meshIndex)
                return;

            _activeModelIndex = modelIndex;
            _activeMeshIndex = meshIndex;

            _flagManager.ActiveModelIndex = modelIndex;
            _flagManager.ActiveMeshIndex = meshIndex;
            _flagManager.SelectedModelIndex = modelIndex;
            _flagManager.SelectedMeshIndex = meshIndex;  // 後方互換

            // v2.0: カテゴリ別選択インデックス設定
            if (_currentModel != null && meshIndex >= 0 && meshIndex < _currentModel.MeshContextCount)
            {
                var meshContext = _currentModel.GetMeshContext(meshIndex);
                if (meshContext != null)
                {
                    switch (meshContext.Type)
                    {
                        case MeshType.Mesh:
                        case MeshType.BakedMirror:
                            _flagManager.SelectedDrawableMeshIndex = meshIndex;
                            break;
                        case MeshType.Bone:
                            _flagManager.SelectedBoneIndex = meshIndex;
                            break;
                        case MeshType.Morph:
                            _flagManager.SelectedMorphIndex = meshIndex;
                            break;
                    }
                }
            }

            _bufferManager.SetActiveMesh(modelIndex, meshIndex);

            _updateManager.MarkSelectionDirty();
        }

        // ============================================================
        // カメラ更新
        // ============================================================

        /// <summary>
        /// カメラ情報を更新
        /// </summary>
        public void UpdateCamera(
            Vector3 cameraPosition,
            Vector3 cameraTarget,
            float fov,
            Rect viewport,
            float rotationZ = 0f)
        {
            bool changed = _cameraPosition != cameraPosition ||
                          _cameraTarget != cameraTarget ||
                          _viewport != viewport ||
                          _rotationZ != rotationZ;

            if (!changed)
                return;

            _cameraPosition = cameraPosition;
            _cameraTarget = cameraTarget;
            _viewport = viewport;
            _rotationZ = rotationZ;

            // ビュー行列計算
            Vector3 forward = (_cameraTarget - _cameraPosition).normalized;
            Quaternion lookRot = Quaternion.LookRotation(forward, Vector3.up);
            Quaternion rollRot = Quaternion.AngleAxis(_rotationZ, Vector3.forward);
            Quaternion camRot = lookRot * rollRot;

            Matrix4x4 camMatrix = Matrix4x4.TRS(_cameraPosition, camRot, Vector3.one);
            _viewMatrix = camMatrix.inverse;
            _viewMatrix.m20 *= -1; _viewMatrix.m21 *= -1; _viewMatrix.m22 *= -1; _viewMatrix.m23 *= -1;

            // プロジェクション行列
            float aspect = viewport.width / viewport.height;
            _projectionMatrix = Matrix4x4.Perspective(fov, aspect, 0.01f, 1000f);

            _updateManager.MarkCameraDirty();
        }

        // ============================================================
        // マウス更新
        // ============================================================

        /// <summary>
        /// マウス位置を更新
        /// </summary>
        public void UpdateMousePosition(Vector2 mousePosition)
        {
            if (_mousePosition == mousePosition)
                return;

            _mousePosition = mousePosition;
            _updateManager.MarkMouseDirty();
        }

        /// <summary>
        /// ヒット半径を設定
        /// </summary>
        public void SetHitRadius(float radius)
        {
            _hitRadius = radius;
        }

        // ============================================================
        // 変更通知
        // ============================================================

        /// <summary>
        /// トポロジー変更を通知
        /// </summary>
        public void NotifyTopologyChanged()
        {
            _updateManager.MarkTopologyDirty();
        }

        /// <summary>
        /// 位置変更を通知
        /// </summary>
        public void NotifyTransformChanged()
        {
            _updateManager.MarkTransformDirty();
        }

        /// <summary>
        /// 選択変更を通知
        /// </summary>
        public void NotifySelectionChanged()
        {
            _updateManager.MarkSelectionDirty();
        }

        /// <summary>
        /// 選択変更コールバック
        /// </summary>
        private void OnSelectionChanged()
        {
            _updateManager.MarkSelectionDirty();
        }

        // ============================================================
        // 更新実行
        // ============================================================

        /// <summary>
        /// フレーム開始
        /// </summary>
        public void BeginFrame()
        {
            _updateManager.BeginFrame();
        }

        /// <summary>
        /// 更新を処理
        /// </summary>
        public DirtyLevel ProcessUpdates()
        {
            if (!_isInitialized)
                Initialize();

            return _updateManager.ProcessUpdates();
        }

        /// <summary>
        /// フレーム終了
        /// </summary>
        public void EndFrame()
        {
            _updateManager.EndFrame();
        }

        /// <summary>
        /// 更新前コールバック
        /// </summary>
        private void OnBeforeUpdate(DirtyLevel level)
        {
            // デバッグログなど
        }

        /// <summary>
        /// 更新後コールバック
        /// </summary>
        private void OnAfterUpdate(DirtyLevel level)
        {
            // 統計更新など
        }

        // ============================================================
        // IDisposable
        // ============================================================

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_selectionState != null)
                    {
                        _selectionState.OnSelectionChanged -= OnSelectionChanged;
                    }

                    _updateManager.OnBeforeUpdate -= OnBeforeUpdate;
                    _updateManager.OnAfterUpdate -= OnAfterUpdate;

                    _bufferManager?.Dispose();
                    _updateManager?.Dispose();
                }

                _disposed = true;
                _isInitialized = false;
            }
        }

        ~UnifiedMeshSystem()
        {
            Dispose(false);
        }
    }
}
