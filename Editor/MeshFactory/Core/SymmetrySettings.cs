// Assets/Editor/MeshFactory/Symmetry/SymmetrySettings.cs
// 対称モード設定クラス
// Phase1: 表示ミラーのみ（MeshObjectは変更しない）

using System;
using UnityEngine;

namespace MeshFactory.Symmetry
{
    /// <summary>
    /// 対称軸
    /// </summary>
    public enum SymmetryAxis
    {
        /// <summary>X軸対称（YZ平面でミラー）</summary>
        X,
        /// <summary>Y軸対称（XZ平面でミラー）</summary>
        Y,
        /// <summary>Z軸対称（XY平面でミラー）</summary>
        Z
    }

    /// <summary>
    /// 対称モード設定
    /// </summary>
    [Serializable]
    public class SymmetrySettings
    {
        // ================================================================
        // フィールド
        // ================================================================

        [SerializeField] private bool _isEnabled = false;
        [SerializeField] private SymmetryAxis _axis = SymmetryAxis.X;
        [SerializeField] private float _planeOffset = 0f;  // 対称平面のオフセット

        // 表示設定
        [SerializeField] private bool _showMirrorMesh = true;
        [SerializeField] private bool _showMirrorWireframe = true;
        [SerializeField] private bool _showSymmetryPlane = true;
        [SerializeField] private float _mirrorAlpha = 0.5f;  // ミラー側の透明度

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>対称モードが有効か</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        /// <summary>対称軸</summary>
        public SymmetryAxis Axis
        {
            get => _axis;
            set => _axis = value;
        }

        /// <summary>対称平面のオフセット</summary>
        public float PlaneOffset
        {
            get => _planeOffset;
            set => _planeOffset = value;
        }

        /// <summary>ミラーメッシュを表示するか</summary>
        public bool ShowMirrorMesh
        {
            get => _showMirrorMesh;
            set => _showMirrorMesh = value;
        }

        /// <summary>ミラーワイヤーフレームを表示するか</summary>
        public bool ShowMirrorWireframe
        {
            get => _showMirrorWireframe;
            set => _showMirrorWireframe = value;
        }

        /// <summary>対称平面を表示するか</summary>
        public bool ShowSymmetryPlane
        {
            get => _showSymmetryPlane;
            set => _showSymmetryPlane = value;
        }

        /// <summary>ミラー側の透明度（0-1）</summary>
        public float MirrorAlpha
        {
            get => _mirrorAlpha;
            set => _mirrorAlpha = Mathf.Clamp01(value);
        }

        // ================================================================
        // メソッド
        // ================================================================

        /// <summary>
        /// ミラー変換行列を取得
        /// </summary>
        public Matrix4x4 GetMirrorMatrix()
        {
            Vector3 scale = Vector3.one;
            Vector3 offset = Vector3.zero;

            switch (_axis)
            {
                case SymmetryAxis.X:
                    scale.x = -1f;
                    offset.x = _planeOffset * 2f;
                    break;
                case SymmetryAxis.Y:
                    scale.y = -1f;
                    offset.y = _planeOffset * 2f;
                    break;
                case SymmetryAxis.Z:
                    scale.z = -1f;
                    offset.z = _planeOffset * 2f;
                    break;
            }

            // TRS: 平面オフセット分移動 → スケール（反転）→ 戻す
            return Matrix4x4.TRS(offset, Quaternion.identity, scale);
        }

        /// <summary>
        /// 点をミラー変換
        /// </summary>
        //public Vector3 MirrorPoint(Vector3 point)
        //{
        //    return GetMirrorMatrix().MultiplyPoint3x4(point);
        //}

        /// <summary>
        /// 法線をミラー変換（スケールのみ適用）
        /// </summary>
        public Vector3 MirrorNormal(Vector3 normal)
        {
            Vector3 scale = Vector3.one;

            switch (_axis)
            {
                case SymmetryAxis.X:
                    scale.x = -1f;
                    break;
                case SymmetryAxis.Y:
                    scale.y = -1f;
                    break;
                case SymmetryAxis.Z:
                    scale.z = -1f;
                    break;
            }

            return Vector3.Scale(normal, scale).normalized;
        }

        /// <summary>
        /// 対称平面の法線を取得
        /// </summary>
        public Vector3 GetPlaneNormal()
        {
            switch (_axis)
            {
                case SymmetryAxis.X: return Vector3.right;
                case SymmetryAxis.Y: return Vector3.up;
                case SymmetryAxis.Z: return Vector3.forward;
                default: return Vector3.right;
            }
        }

        /// <summary>
        /// 対称平面上の点を取得
        /// </summary>
        public Vector3 GetPlanePoint()
        {
            return GetPlaneNormal() * _planeOffset;
        }

        /// <summary>
        /// 点が対称平面上にあるか（許容誤差付き）
        /// </summary>
        public bool IsOnPlane(Vector3 point, float tolerance = 0.0001f)
        {
            float dist = DistanceToPlane(point);
            return Mathf.Abs(dist) < tolerance;
        }

        /// <summary>
        /// 点から対称平面までの符号付き距離
        /// </summary>
        public float DistanceToPlane(Vector3 point)
        {
            Vector3 normal = GetPlaneNormal();
            return Vector3.Dot(point - GetPlanePoint(), normal);
        }

        /// <summary>
        /// 対称軸の表示名を取得
        /// </summary>
        public string GetAxisDisplayName()
        {
            switch (_axis)
            {
                case SymmetryAxis.X: return "X (YZ Plane)";
                case SymmetryAxis.Y: return "Y (XZ Plane)";
                case SymmetryAxis.Z: return "Z (XY Plane)";
                default: return "X";
            }
        }

        /// <summary>
        /// 対称軸の色を取得
        /// </summary>
        public Color GetAxisColor()
        {
            switch (_axis)
            {
                case SymmetryAxis.X: return new Color(1f, 0.3f, 0.3f, 0.8f);  // 赤
                case SymmetryAxis.Y: return new Color(0.3f, 1f, 0.3f, 0.8f);  // 緑
                case SymmetryAxis.Z: return new Color(0.3f, 0.3f, 1f, 0.8f);  // 青
                default: return Color.white;
            }
        }

        /// <summary>
        /// デフォルト設定にリセット
        /// </summary>
        public void Reset()
        {
            _isEnabled = false;
            _axis = SymmetryAxis.X;
            _planeOffset = 0f;
            _showMirrorMesh = true;
            _showMirrorWireframe = true;
            _showSymmetryPlane = true;
            _mirrorAlpha = 0.5f;
        }

        /// <summary>
        /// 設定をコピー
        /// </summary>
        public SymmetrySettings Clone()
        {
            return new SymmetrySettings
            {
                _isEnabled = _isEnabled,
                _axis = _axis,
                _planeOffset = _planeOffset,
                _showMirrorMesh = _showMirrorMesh,
                _showMirrorWireframe = _showMirrorWireframe,
                _showSymmetryPlane = _showSymmetryPlane,
                _mirrorAlpha = _mirrorAlpha
            };
        }

        /// <summary>
        /// 他の設定からコピー
        /// </summary>
        public void CopyFrom(SymmetrySettings other)
        {
            if (other == null) return;

            _isEnabled = other._isEnabled;
            _axis = other._axis;
            _planeOffset = other._planeOffset;
            _showMirrorMesh = other._showMirrorMesh;
            _showMirrorWireframe = other._showMirrorWireframe;
            _showSymmetryPlane = other._showSymmetryPlane;
            _mirrorAlpha = other._mirrorAlpha;
        }
    }
}
