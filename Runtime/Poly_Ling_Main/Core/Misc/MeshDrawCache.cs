// Assets/Editor/Poly_Ling/Rendering/MeshDrawCache.cs
// CPU描画の最適化用キャッシュ
// Phase 1: 行列・エッジリスト・スクリーン座標のキャッシュ

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Rendering
{
    /// <summary>
    /// メッシュ描画用キャッシュ
    /// 毎フレームの重複計算を削減
    /// </summary>
    public class MeshDrawCache
    {
        // ================================================================
        // エッジキャッシュ（メッシュ変更時のみ更新）
        // ================================================================

        /// <summary>通常エッジ（3頂点以上の面から抽出）</summary>
        public List<(int v1, int v2)> Edges { get; private set; } = new List<(int, int)>();

        /// <summary>補助線（2頂点の面）</summary>
        public List<(int v1, int v2)> AuxLines { get; private set; } = new List<(int, int)>();

        /// <summary>キャッシュ元のMeshObjectのハッシュ</summary>
        private int _meshObjectHash;

        // ================================================================
        // 座標キャッシュ（毎フレーム更新）
        // ================================================================

        /// <summary>スクリーン座標キャッシュ</summary>
        public Vector2[] ScreenPositions { get; private set; }

        /// <summary>頂点が画面内にあるか</summary>
        public bool[] IsVisible { get; private set; }

        // ================================================================
        // 行列キャッシュ（フレーム内で使い回し）
        // ================================================================

        private Matrix4x4 _viewMatrix;
        private Matrix4x4 _projMatrix;
        private Matrix4x4 _vpMatrix;
        private Rect _previewRect;
        private bool _matricesValid = false;

        /// <summary>行列キャッシュが有効か</summary>
        public bool HasValidMatrices => _matricesValid;

        // ================================================================
        // エッジキャッシュ更新
        // ================================================================

        /// <summary>
        /// メッシュデータからエッジリストを構築
        /// メッシュ変更時のみ呼び出す
        /// </summary>
        public void UpdateEdgeCache(MeshObject meshObject)
        {
            if (meshObject == null)
            {
                Edges.Clear();
                AuxLines.Clear();
                _meshObjectHash = 0;
                return;
            }

            // 簡易ハッシュでメッシュ変更を検出
            int hash = ComputeMeshHash(meshObject);
            if (hash == _meshObjectHash && Edges.Count > 0)
                return;  // 変更なし

            _meshObjectHash = hash;
            Edges.Clear();
            AuxLines.Clear();

            var edgeSet = new HashSet<(int, int)>();

            foreach (var face in meshObject.Faces)
            {
                if (face.VertexCount == 2)
                {
                    // 補助線
                    AuxLines.Add((face.VertexIndices[0], face.VertexIndices[1]));
                }
                else if (face.VertexCount >= 3)
                {
                    // 通常エッジ（重複排除）
                    for (int i = 0; i < face.VertexCount; i++)
                    {
                        int a = face.VertexIndices[i];
                        int b = face.VertexIndices[(i + 1) % face.VertexCount];

                        // 正規化（小さい方を先に）
                        if (a > b) (a, b) = (b, a);

                        if (edgeSet.Add((a, b)))
                        {
                            Edges.Add((a, b));
                        }
                    }
                }
            }

            // スクリーン座標配列のサイズ調整
            EnsureScreenPositionCapacity(meshObject.VertexCount);
        }

        /// <summary>
        /// エッジキャッシュを強制的にクリア
        /// </summary>
        public void InvalidateEdgeCache()
        {
            _meshObjectHash = 0;
            Edges.Clear();
            AuxLines.Clear();
        }

        // ================================================================
        // 行列キャッシュ更新
        // ================================================================

        /// <summary>
        /// カメラ行列を更新（フレームの最初に1回だけ呼び出す）
        /// </summary>
        public void UpdateMatrices(
            Vector3 camPos,
            Vector3 lookAt,
            float rotationZ,
            float fov,
            Rect previewRect)
        {
            _previewRect = previewRect;

            // カメラ回転（Z軸ロール対応）
            Vector3 forward = (lookAt - camPos).normalized;
            Quaternion lookRot = Quaternion.LookRotation(forward, Vector3.up);
            Quaternion rollRot = Quaternion.AngleAxis(rotationZ, Vector3.forward);
            Quaternion camRot = lookRot * rollRot;

            // View行列
            Matrix4x4 camMatrix = Matrix4x4.TRS(camPos, camRot, Vector3.one);
            _viewMatrix = camMatrix.inverse;
            // Unityのカメラは-Z方向を向く
            _viewMatrix.m20 *= -1;
            _viewMatrix.m21 *= -1;
            _viewMatrix.m22 *= -1;
            _viewMatrix.m23 *= -1;

            // Projection行列
            float aspect = previewRect.width / previewRect.height;
            _projMatrix = Matrix4x4.Perspective(fov, aspect, 0.01f, 100f);

            // VP行列（キャッシュ）
            _vpMatrix = _projMatrix * _viewMatrix;

            _matricesValid = true;
        }

        /// <summary>
        /// ワールド座標をスクリーン座標に変換（キャッシュ済み行列を使用）
        /// </summary>
        public Vector2 WorldToScreen(Vector3 worldPos)
        {
            if (!_matricesValid)
                return new Vector2(-10000, -10000);

            Vector4 clipPos = _vpMatrix * new Vector4(worldPos.x, worldPos.y, worldPos.z, 1f);

            if (clipPos.w <= 0.0001f)
                return new Vector2(-10000, -10000);

            Vector2 ndc = new Vector2(clipPos.x / clipPos.w, clipPos.y / clipPos.w);

            float screenX = _previewRect.x + (ndc.x * 0.5f + 0.5f) * _previewRect.width;
            float screenY = _previewRect.y + (1f - (ndc.y * 0.5f + 0.5f)) * _previewRect.height;

            return new Vector2(screenX, screenY);
        }

        // ================================================================
        // スクリーン座標の一括計算
        // ================================================================

        /// <summary>
        /// 全頂点のスクリーン座標を一括計算
        /// </summary>
        public void ComputeAllScreenPositions(MeshObject meshObject)
        {
            if (meshObject == null) return;

            EnsureScreenPositionCapacity(meshObject.VertexCount);

            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                Vector3 worldPos = meshObject.Vertices[i].Position;
                Vector4 clipPos = _vpMatrix * new Vector4(worldPos.x, worldPos.y, worldPos.z, 1f);

                if (clipPos.w <= 0.0001f)
                {
                    // カメラの後ろ
                    ScreenPositions[i] = new Vector2(-10000, -10000);
                    IsVisible[i] = false;
                    continue;
                }

                Vector2 ndc = new Vector2(clipPos.x / clipPos.w, clipPos.y / clipPos.w);

                float screenX = _previewRect.x + (ndc.x * 0.5f + 0.5f) * _previewRect.width;
                float screenY = _previewRect.y + (1f - (ndc.y * 0.5f + 0.5f)) * _previewRect.height;

                ScreenPositions[i] = new Vector2(screenX, screenY);
                IsVisible[i] = _previewRect.Contains(ScreenPositions[i]);
            }
        }

        /// <summary>
        /// 単一頂点のスクリーン座標を取得（キャッシュから）
        /// </summary>
        public Vector2 GetScreenPosition(int vertexIndex)
        {
            if (ScreenPositions == null || vertexIndex < 0 || vertexIndex >= ScreenPositions.Length)
                return new Vector2(-10000, -10000);

            return ScreenPositions[vertexIndex];
        }

        /// <summary>
        /// 頂点が画面内にあるか
        /// </summary>
        public bool IsVertexVisible(int vertexIndex)
        {
            if (IsVisible == null || vertexIndex < 0 || vertexIndex >= IsVisible.Length)
                return false;

            return IsVisible[vertexIndex];
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void EnsureScreenPositionCapacity(int vertexCount)
        {
            if (ScreenPositions == null || ScreenPositions.Length < vertexCount)
            {
                ScreenPositions = new Vector2[vertexCount];
                IsVisible = new bool[vertexCount];
            }
        }

        private int ComputeMeshHash(MeshObject meshObject)
        {
            // 簡易ハッシュ: 頂点数 + 面数 + 最初と最後の頂点位置
            int hash = meshObject.VertexCount * 397 ^ meshObject.FaceCount;

            if (meshObject.VertexCount > 0)
            {
                var first = meshObject.Vertices[0].Position;
                var last = meshObject.Vertices[meshObject.VertexCount - 1].Position;
                hash ^= first.GetHashCode();
                hash ^= last.GetHashCode();
            }

            return hash;
        }

        /// <summary>
        /// キャッシュをクリア
        /// </summary>
        public void Clear()
        {
            Edges.Clear();
            AuxLines.Clear();
            ScreenPositions = null;
            IsVisible = null;
            _meshObjectHash = 0;
            _matricesValid = false;
        }
    }
}
