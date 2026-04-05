using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// ヒエラルキーへのリアルタイム同期（LiveSync）の状態とロジックを保持するクラス。
    /// OverwriteSingleMeshToTarget / OverwriteToTarget / ShouldExportAsMesh はPolyLing側に残し、
    /// コールバック経由で呼び出す。
    /// </summary>
    public class EditorLiveSyncHandler
    {
        // ================================================================
        // 状態（PolyLing_ModelExport.cs から移動）
        // ================================================================

        private GameObject _target;
        private bool _autoEnabled;

        // ================================================================
        // コールバック（初期化時にPolyLingから注入）
        // ================================================================

        /// <summary>単一メッシュを同期先GameObjectに書き込む</summary>
        private Action<GameObject, MeshContext> _overwriteSingle;

        /// <summary>全メッシュを同期先GameObjectに書き込む（showDialog, meshOnly）</summary>
        private Action<GameObject, bool, bool> _overwriteAll;

        /// <summary>指定MeshTypeをメッシュとしてエクスポートすべきか判定</summary>
        private Func<MeshType, bool> _shouldExport;

        /// <summary>選択中MeshContextを列挙する</summary>
        private Func<IEnumerable<MeshContext>> _getSelectedContexts;

        // ================================================================
        // プロパティ
        // ================================================================

        public bool HasTarget => _target != null;
        public string TargetName => _target != null ? _target.name : "";
        public bool AutoEnabled
        {
            get => _autoEnabled;
            set => _autoEnabled = value;
        }

        // ================================================================
        // 初期化
        // ================================================================

        /// <summary>
        /// コールバックを注入して初期化する。PolyLing.OnEnable から呼ぶ。
        /// </summary>
        public void Initialize(
            Action<GameObject, MeshContext> overwriteSingle,
            Action<GameObject, bool, bool> overwriteAll,
            Func<MeshType, bool> shouldExport,
            Func<IEnumerable<MeshContext>> getSelectedContexts)
        {
            _overwriteSingle = overwriteSingle;
            _overwriteAll = overwriteAll;
            _shouldExport = shouldExport;
            _getSelectedContexts = getSelectedContexts;
        }

        // ================================================================
        // 操作（PolyLing_ModelExport.cs から移動）
        // ================================================================

        /// <summary>同期先ターゲットを設定する</summary>
        public void SetTarget(GameObject go)
        {
            _target = go;
        }

        /// <summary>LiveSyncターゲットをクリア</summary>
        public void Clear()
        {
            _target = null;
            _autoEnabled = false;
        }

        /// <summary>
        /// LiveSync自動同期: 単一メッシュ更新（SyncMeshFromDataから呼ばれる）
        /// </summary>
        public void AutoUpdate(MeshContext meshContext)
        {
            if (!_autoEnabled || !HasTarget || meshContext == null) return;
            if (_shouldExport == null || !_shouldExport(meshContext.Type)) return;
            _overwriteSingle?.Invoke(_target, meshContext);
        }

        /// <summary>
        /// LiveSync自動同期: 選択メッシュ全体（ExitTransformDraggingから呼ばれる）
        /// </summary>
        public void AutoUpdateSelected()
        {
            if (!_autoEnabled || !HasTarget || _getSelectedContexts == null) return;
            foreach (var mc in _getSelectedContexts())
            {
                if (mc?.MeshObject == null || mc.MeshObject.VertexCount == 0) continue;
                if (_shouldExport == null || !_shouldExport(mc.Type)) continue;
                _overwriteSingle?.Invoke(_target, mc);
            }
        }

        /// <summary>
        /// LiveSync手動反映（Applyボタンから呼ばれる）
        /// </summary>
        public void Apply()
        {
            if (!HasTarget) return;
            _overwriteAll?.Invoke(_target, false, true);
        }
    }
}
