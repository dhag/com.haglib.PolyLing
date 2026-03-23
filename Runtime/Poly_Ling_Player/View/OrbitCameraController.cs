// OrbitCameraController.cs
// カメラオービット・パン・ズーム操作を担うサブクラス。
// 将来の頂点選択との連携を想定し、以下を提供する：
//   - IsDragging : ドラッグ中フラグ（選択ツール側が「ドラッグ中は選択しない」判定に使用）
//   - OnClick    : クリック確定コールバック（ボタン番号 + スクリーン座標）
//   - Update は isPointerOverUI を受け取り UI 上ではオービットを無効化する
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;

namespace Poly_Ling.Player
{
    /// <summary>
    /// カメラオービット・パン・ズーム操作を担うサブクラス。
    /// MonoBehaviour に依存しない純粋クラス。Viewer の Update から Tick する。
    /// </summary>
    public class OrbitCameraController
    {
        // ================================================================
        // 定数
        // ================================================================

        public const float DefaultOrbitSensitivity = 0.5f;
        public const float DefaultZoomSensitivity  = 0.05f;
        public const float DefaultPanSensitivity   = 0.002f;
        public const float DefaultZoomMin          = 0.05f;
        public const float DefaultZoomMax          = 100f;

        /// <summary>
        /// クリック判定のピクセルしきい値。
        /// マウスダウンからアップまでの移動量がこれ以下ならクリックとみなす。
        /// </summary>
        public const float ClickPixelThreshold = 4f;

        // ================================================================
        // パラメータ（Inspector 相当。外部から変更可）
        // ================================================================

        public float OrbitSensitivity = DefaultOrbitSensitivity;
        public float ZoomSensitivity  = DefaultZoomSensitivity;
        public float PanSensitivity   = DefaultPanSensitivity;
        public float ZoomMin          = DefaultZoomMin;
        public float ZoomMax          = DefaultZoomMax;

        // ================================================================
        // 状態（読み取り専用公開）
        // ================================================================

        public float   RotX     { get; private set; } =  20f;
        public float   RotY     { get; private set; } =   0f;
        public float   Distance { get; private set; } =   3f;
        public Vector3 Target   { get; private set; } = Vector3.zero;

        /// <summary>オービットまたはパンのドラッグ中。選択ツール側がドラッグ中選択を抑制するために使う。</summary>
        public bool IsDragging => _isOrbiting || _isPanning;

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>
        /// クリック確定時に呼ばれる。
        /// 引数: (mouseButton, screenPosition)
        /// mouseButton: 0=左, 1=右, 2=中
        /// UI 上のクリックは通知されない。
        /// </summary>
        public Action<int, Vector2> OnClick;

        // ================================================================
        // 内部状態
        // ================================================================

        private bool    _isOrbiting;
        private bool    _isPanning;
        private Vector2 _orbitStartPos;
        private Vector2 _panStartPos;
        private Vector2 _prevOrbitPos;
        private Vector2 _prevPanPos;

        // クリック判定用（ボタン別）
        private readonly Vector2[] _mouseDownPos  = new Vector2[3];
        private readonly bool[]    _mouseDownFlag = new bool[3];

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>
        /// カメラ位置をバウンディングボックスに合わせてリセットする。
        /// ファイルロード・リモートフェッチ後に呼ぶ。
        /// </summary>
        public void ResetToMesh(Bounds bounds)
        {
            Target   = bounds.center;
            Distance = Mathf.Clamp(bounds.size.magnitude * 1.5f, ZoomMin, ZoomMax);
        }

        /// <summary>
        /// 毎フレーム呼ぶ。Input を読み取りカメラを更新する。
        /// </summary>
        /// <param name="cam">対象カメラ</param>
        /// <param name="isPointerOverUI">UI 上にポインタがある場合 true。オービット・クリックを無効化する。</param>
        public void Update(Camera cam, bool isPointerOverUI)
        {
            if (cam == null) return;

            // ---- クリック判定（ボタン 0〜2）----
            for (int btn = 0; btn < 3; btn++)
            {
                if (Input.GetMouseButtonDown(btn))
                {
                    _mouseDownPos[btn]  = Input.mousePosition;
                    _mouseDownFlag[btn] = !isPointerOverUI;
                }

                if (Input.GetMouseButtonUp(btn) && _mouseDownFlag[btn])
                {
                    float moved = Vector2.Distance(Input.mousePosition, _mouseDownPos[btn]);
                    if (moved <= ClickPixelThreshold)
                        OnClick?.Invoke(btn, Input.mousePosition);
                    _mouseDownFlag[btn] = false;
                }
            }

            if (isPointerOverUI)
            {
                // UI 上ではオービット・パンを開始しない。進行中も中断。
                _isOrbiting = false;
                _isPanning  = false;
                return;
            }

            // ---- オービット（左ボタン or 右ボタン）----
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                _isOrbiting   = true;
                _orbitStartPos = Input.mousePosition;
                _prevOrbitPos  = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1))
                _isOrbiting = false;

            if (_isOrbiting && (Input.GetMouseButton(0) || Input.GetMouseButton(1)))
            {
                Vector2 delta = (Vector2)Input.mousePosition - _prevOrbitPos;
                RotY          += delta.x * OrbitSensitivity;
                RotX          -= delta.y * OrbitSensitivity;
                RotX           = Mathf.Clamp(RotX, -89f, 89f);
                _prevOrbitPos  = Input.mousePosition;
            }

            // ---- パン（中ボタン）----
            if (Input.GetMouseButtonDown(2))
            {
                _isPanning   = true;
                _panStartPos  = Input.mousePosition;
                _prevPanPos   = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(2))
                _isPanning = false;

            if (_isPanning && Input.GetMouseButton(2))
            {
                Vector2    delta    = (Vector2)Input.mousePosition - _prevPanPos;
                Quaternion rot      = Quaternion.Euler(RotX, RotY, 0f);
                float      panScale = Distance * PanSensitivity;
                Target     -= rot * Vector3.right * delta.x * panScale;
                Target     -= rot * Vector3.up    * delta.y * panScale;
                _prevPanPos = Input.mousePosition;
            }

            // ---- ズーム（スクロール）----
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                Distance *= 1f + scroll * ZoomSensitivity;
                Distance  = Mathf.Clamp(Distance, ZoomMin, ZoomMax);
            }

            // ---- カメラ配置 ----
            Quaternion camRot      = Quaternion.Euler(RotX, RotY, 0f);
            cam.transform.position = Target + camRot * (Vector3.back * Distance);
            cam.transform.LookAt(Target);
        }
    }
}
