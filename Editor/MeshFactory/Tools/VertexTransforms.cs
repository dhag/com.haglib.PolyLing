// Assets/Editor/MeshCreators/VertexTransforms.cs
// 頂点変形操作のクラス群
// 選択頂点に対する移動・マグネット等の変形を抽象化

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshFactory.Data;

namespace MeshFactory.Transforms
{
    // ================================================================
    // 減衰タイプ
    // ================================================================
    public enum FalloffType
    {
        Linear,     // 1 - d/r（直線的）
        Smooth,     // smoothstep（S字カーブ）
        Sphere,     // sqrt(1 - (d/r)^2)（球面的）
        Sharp,      // (1 - d/r)^2（急な減衰）
        Root,       // sqrt(1 - d/r)（緩やかな減衰）
        Constant    // 1（半径内は全て同じ）
    }

    // ================================================================
    // インターフェース
    // ================================================================
    public interface IVertexTransform
    {
        /// <summary>
        /// 変形開始
        /// </summary>
        /// <param name="meshData">対象メッシュデータ</param>
        /// <param name="selectedIndices">選択中の頂点インデックス</param>
        /// <param name="originalPositions">変形開始時の全頂点位置</param>
        void Begin(MeshData meshData, HashSet<int> selectedIndices, Vector3[] originalPositions);

        /// <summary>
        /// 変形適用（ドラッグ中、毎フレーム呼ばれる）
        /// </summary>
        /// <param name="worldDelta">ワールド座標系での移動量</param>
        void Apply(Vector3 worldDelta);

        /// <summary>
        /// 変形終了
        /// </summary>
        void End();

        /// <summary>
        /// 影響を受けた頂点のインデックス（Undo記録用）
        /// </summary>
        int[] GetAffectedIndices();

        /// <summary>
        /// 影響を受けた頂点の元の位置（Undo記録用）
        /// </summary>
        Vector3[] GetOriginalPositions();

        /// <summary>
        /// 影響を受けた頂点の現在の位置（Undo記録用）
        /// </summary>
        Vector3[] GetCurrentPositions();
    }

    // ================================================================
    // 単純移動
    // ================================================================
    public class SimpleMoveTransform : IVertexTransform
    {
        private MeshData _meshData;
        private HashSet<int> _selectedIndices;
        private Vector3[] _originalPositions;
        private Vector3 _totalDelta;

        public void Begin(MeshData meshData, HashSet<int> selectedIndices, Vector3[] originalPositions)
        {
            _meshData = meshData;
            _selectedIndices = new HashSet<int>(selectedIndices);
            _originalPositions = originalPositions;
            _totalDelta = Vector3.zero;
        }

        public void Apply(Vector3 worldDelta)
        {
            _totalDelta += worldDelta;

            foreach (int idx in _selectedIndices)
            {
                if (idx >= 0 && idx < _meshData.VertexCount)
                {
                    _meshData.Vertices[idx].Position = _originalPositions[idx] + _totalDelta;
                }
            }
        }

        public void End()
        {
            // 特に何もしない
        }

        public int[] GetAffectedIndices()
        {
            return _selectedIndices.ToArray();
        }

        public Vector3[] GetOriginalPositions()
        {
            return _selectedIndices.Select(idx => _originalPositions[idx]).ToArray();
        }

        public Vector3[] GetCurrentPositions()
        {
            return _selectedIndices.Select(idx => _meshData.Vertices[idx].Position).ToArray();
        }
    }

    // ================================================================
    // マグネット移動
    // ================================================================
    public class MagnetMoveTransform : IVertexTransform
    {
        private MeshData _meshData;
        private HashSet<int> _selectedIndices;
        private Vector3[] _originalPositions;
        private Vector3 _totalDelta;

        // マグネット設定
        private float _radius;
        private FalloffType _falloffType;

        // 非選択頂点のうち影響を受けるもの
        // Key: 頂点インデックス, Value: 減衰係数（0〜1）
        private Dictionary<int, float> _affectedNonSelected;

        // 全影響頂点（選択 + マグネット影響）
        private HashSet<int> _allAffectedIndices;

        public MagnetMoveTransform(float radius, FalloffType falloffType)
        {
            _radius = radius;
            _falloffType = falloffType;
        }

        public float Radius
        {
            get => _radius;
            set => _radius = Mathf.Max(0.001f, value);
        }

        public FalloffType Falloff
        {
            get => _falloffType;
            set => _falloffType = value;
        }

        public void Begin(MeshData meshData, HashSet<int> selectedIndices, Vector3[] originalPositions)
        {
            _meshData = meshData;
            _selectedIndices = new HashSet<int>(selectedIndices);
            _originalPositions = originalPositions;
            _totalDelta = Vector3.zero;

            // 非選択頂点に対する影響係数を計算
            CalculateAffectedVertices();
        }

        private void CalculateAffectedVertices()
        {
            _affectedNonSelected = new Dictionary<int, float>();
            _allAffectedIndices = new HashSet<int>(_selectedIndices);

            if (_selectedIndices.Count == 0 || _meshData == null)
                return;

            // 選択頂点の位置リスト
            var selectedPositions = _selectedIndices
                .Where(idx => idx >= 0 && idx < _originalPositions.Length)
                .Select(idx => _originalPositions[idx])
                .ToList();

            if (selectedPositions.Count == 0)
                return;

            // 全頂点をチェック
            for (int i = 0; i < _meshData.VertexCount; i++)
            {
                // 選択頂点はスキップ
                if (_selectedIndices.Contains(i))
                    continue;

                Vector3 pos = _originalPositions[i];

                // 最も近い選択頂点への距離を計算
                float minDist = float.MaxValue;
                foreach (var selPos in selectedPositions)
                {
                    float dist = Vector3.Distance(pos, selPos);
                    if (dist < minDist)
                        minDist = dist;
                }

                // 半径内なら影響を受ける
                if (minDist < _radius)
                {
                    float normalizedDist = minDist / _radius;
                    float falloff = CalculateFalloff(normalizedDist);

                    if (falloff > 0.0001f)
                    {
                        _affectedNonSelected[i] = falloff;
                        _allAffectedIndices.Add(i);
                    }
                }
            }
        }

        private float CalculateFalloff(float t)
        {
            // t: 0（選択頂点上）〜 1（半径境界）
            t = Mathf.Clamp01(t);

            switch (_falloffType)
            {
                case FalloffType.Linear:
                    return 1f - t;

                case FalloffType.Smooth:
                    // Smoothstep: 3t^2 - 2t^3
                    float s = 1f - t;
                    return s * s * (3f - 2f * s);

                case FalloffType.Sphere:
                    // 球面的: sqrt(1 - t^2)
                    return Mathf.Sqrt(1f - t * t);

                case FalloffType.Sharp:
                    // 急な減衰: (1 - t)^2
                    return (1f - t) * (1f - t);

                case FalloffType.Root:
                    // 緩やかな減衰: sqrt(1 - t)
                    return Mathf.Sqrt(1f - t);

                case FalloffType.Constant:
                    return 1f;

                default:
                    return 1f - t;
            }
        }

        public void Apply(Vector3 worldDelta)
        {
            _totalDelta += worldDelta;

            // 選択頂点: フル移動
            foreach (int idx in _selectedIndices)
            {
                if (idx >= 0 && idx < _meshData.VertexCount)
                {
                    _meshData.Vertices[idx].Position = _originalPositions[idx] + _totalDelta;
                }
            }

            // 非選択だが影響を受ける頂点: 減衰付き移動
            foreach (var kvp in _affectedNonSelected)
            {
                int idx = kvp.Key;
                float falloff = kvp.Value;

                if (idx >= 0 && idx < _meshData.VertexCount)
                {
                    _meshData.Vertices[idx].Position = _originalPositions[idx] + _totalDelta * falloff;
                }
            }
        }

        public void End()
        {
            // 特に何もしない
        }

        public int[] GetAffectedIndices()
        {
            return _allAffectedIndices.ToArray();
        }

        public Vector3[] GetOriginalPositions()
        {
            return _allAffectedIndices.Select(idx => _originalPositions[idx]).ToArray();
        }

        public Vector3[] GetCurrentPositions()
        {
            return _allAffectedIndices.Select(idx => _meshData.Vertices[idx].Position).ToArray();
        }
    }

    // ================================================================
    // ファクトリ / ヘルパー
    // ================================================================
    public static class VertexTransformFactory
    {
        public static IVertexTransform CreateSimpleMove()
        {
            return new SimpleMoveTransform();
        }

        public static IVertexTransform CreateMagnetMove(float radius, FalloffType falloff = FalloffType.Smooth)
        {
            return new MagnetMoveTransform(radius, falloff);
        }
    }
}
