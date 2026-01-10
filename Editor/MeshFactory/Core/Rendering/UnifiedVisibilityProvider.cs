// Assets/Editor/MeshFactory/Core/Rendering/UnifiedVisibilityProvider.cs
// 統合システム用可視性プロバイダー
// 既存のIVisibilityProviderインターフェースとの互換

using System;
using UnityEngine;
using MeshFactory.Rendering;

namespace MeshFactory.Core.Rendering
{
    /// <summary>
    /// 統合システム用可視性プロバイダー
    /// IVisibilityProvider互換
    /// </summary>
    public class UnifiedVisibilityProvider : IVisibilityProvider
    {
        // ============================================================
        // 参照
        // ============================================================

        private UnifiedBufferManager _bufferManager;
        private FlagManager _flagManager;

        // ============================================================
        // キャッシュ
        // ============================================================

        private float[] _vertexVisibilityCache;
        private float[] _lineVisibilityCache;
        private float[] _faceVisibilityCache;
        private bool _cacheDirty = true;

        // ============================================================
        // コンストラクタ
        // ============================================================

        public UnifiedVisibilityProvider(UnifiedBufferManager bufferManager, FlagManager flagManager)
        {
            _bufferManager = bufferManager;
            _flagManager = flagManager;
        }

        // ============================================================
        // キャッシュ管理
        // ============================================================

        /// <summary>
        /// キャッシュを無効化
        /// </summary>
        public void InvalidateCache()
        {
            _cacheDirty = true;
        }

        /// <summary>
        /// キャッシュを更新
        /// </summary>
        private void UpdateCache()
        {
            if (!_cacheDirty || _bufferManager == null)
                return;

            int vertexCount = _bufferManager.TotalVertexCount;
            int lineCount = _bufferManager.TotalLineCount;
            int faceCount = _bufferManager.TotalFaceCount;

            // 配列確保
            if (_vertexVisibilityCache == null || _vertexVisibilityCache.Length < vertexCount)
            {
                _vertexVisibilityCache = new float[Mathf.Max(vertexCount, 1)];
            }

            if (_lineVisibilityCache == null || _lineVisibilityCache.Length < lineCount)
            {
                _lineVisibilityCache = new float[Mathf.Max(lineCount, 1)];
            }

            if (_faceVisibilityCache == null || _faceVisibilityCache.Length < faceCount)
            {
                _faceVisibilityCache = new float[Mathf.Max(faceCount, 1)];
            }

            // フラグから可視性を計算
            var vertexFlags = _bufferManager.VertexFlags;
            var lineFlags = _bufferManager.LineFlags;

            for (int i = 0; i < vertexCount; i++)
            {
                uint flags = vertexFlags[i];
                bool hidden = (flags & (uint)SelectionFlags.Hidden) != 0;
                bool culled = (flags & (uint)SelectionFlags.Culled) != 0;
                _vertexVisibilityCache[i] = (hidden || culled) ? 0f : 1f;
            }

            for (int i = 0; i < lineCount; i++)
            {
                uint flags = lineFlags[i];
                bool hidden = (flags & (uint)SelectionFlags.Hidden) != 0;
                bool culled = (flags & (uint)SelectionFlags.Culled) != 0;
                _lineVisibilityCache[i] = (hidden || culled) ? 0f : 1f;
            }

            // 面の可視性は頂点から計算（簡易実装）
            for (int i = 0; i < faceCount; i++)
            {
                _faceVisibilityCache[i] = 1f; // TODO: 面カリング実装
            }

            _cacheDirty = false;
        }

        // ============================================================
        // IVisibilityProvider実装
        // ============================================================

        public bool IsVertexVisible(int index)
        {
            UpdateCache();

            if (index < 0 || index >= _vertexVisibilityCache.Length)
                return false;

            return _vertexVisibilityCache[index] > 0.5f;
        }

        public bool IsLineVisible(int index)
        {
            UpdateCache();

            if (index < 0 || index >= _lineVisibilityCache.Length)
                return false;

            return _lineVisibilityCache[index] > 0.5f;
        }

        public bool IsFaceVisible(int index)
        {
            UpdateCache();

            if (index < 0 || index >= _faceVisibilityCache.Length)
                return false;

            return _faceVisibilityCache[index] > 0.5f;
        }

        public float[] GetVertexVisibility()
        {
            UpdateCache();
            return _vertexVisibilityCache;
        }

        public float[] GetLineVisibility()
        {
            UpdateCache();
            return _lineVisibilityCache;
        }

        public float[] GetFaceVisibility()
        {
            UpdateCache();
            return _faceVisibilityCache;
        }
    }
}
