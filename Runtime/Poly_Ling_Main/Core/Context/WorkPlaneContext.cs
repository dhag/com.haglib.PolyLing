// Assets/Editor/MeshCreators/WorkPlaneContext.cs
// 作業平面（Work Plane）クラス
// 頂点追加時の配置平面、編集時の参照平面を定義
// 全操作がUndo対応

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Context
{
    // ================================================================
    // 作業平面モード
    // ================================================================
    public enum WorkPlaneMode
    {
        CameraParallel,  // カメラに平行（デフォルト）
        WorldXY,         // XY平面（Z=0）
        WorldXZ,         // XZ平面（Y=0、床）
        WorldYZ,         // YZ平面（X=0）
        Custom           // ユーザー定義
    }

    // ================================================================
    // スナップショット（Undo用、先に定義）
    // ================================================================
    /// <summary>
    /// WorkPlaneの状態スナップショット（Undo用）
    /// </summary>
    [Serializable]
    public struct WorkPlaneSnapshot
    {
        public WorkPlaneMode Mode;
        public Vector3 Origin;
        public Vector3 AxisU;
        public Vector3 AxisV;
        public bool IsLocked;
        public bool LockOrientation;
        public bool AutoUpdateOriginOnSelection;

        /// <summary>
        /// 他のスナップショットと異なるかどうか
        /// </summary>
        public bool IsDifferentFrom(WorkPlaneSnapshot other)
        {
            return Mode != other.Mode ||
                   Vector3.Distance(Origin, other.Origin) > 0.0001f ||
                   Vector3.Distance(AxisU, other.AxisU) > 0.0001f ||
                   Vector3.Distance(AxisV, other.AxisV) > 0.0001f ||
                   IsLocked != other.IsLocked ||
                   LockOrientation != other.LockOrientation ||
                   AutoUpdateOriginOnSelection != other.AutoUpdateOriginOnSelection;
        }

        /// <summary>
        /// 変更内容の説明を取得
        /// </summary>
        public string GetChangeDescription(WorkPlaneSnapshot before)
        {
            if (Mode != before.Mode)
                return $"Change WorkPlaneContext Mode to {Mode}";
            if (Vector3.Distance(Origin, before.Origin) > 0.0001f)
                return "Change WorkPlaneContext Origin";
            if (Vector3.Distance(AxisU, before.AxisU) > 0.0001f || 
                Vector3.Distance(AxisV, before.AxisV) > 0.0001f)
                return "Change WorkPlaneContext Orientation";
            if (IsLocked != before.IsLocked)
                return IsLocked ? "Lock WorkPlaneContext" : "Unlock WorkPlaneContext";
            if (LockOrientation != before.LockOrientation)
                return LockOrientation ? "Lock WorkPlaneContext Orientation" : "Unlock WorkPlaneContext Orientation";
            if (AutoUpdateOriginOnSelection != before.AutoUpdateOriginOnSelection)
                return AutoUpdateOriginOnSelection ? "Enable Auto-update Origin" : "Disable Auto-update Origin";
            return "Change WorkPlaneContext";
        }
    }

    // ================================================================
    // 作業平面
    // ================================================================
    /// <summary>
    /// 作業平面（Work Plane）
    /// 原点と2つの直交軸で平面を定義
    /// </summary>
    [Serializable]
    public class WorkPlaneContext
    {
        // === フィールド ===
        [SerializeField] private WorkPlaneMode _mode = WorkPlaneMode.CameraParallel;
        [SerializeField] private Vector3 _origin = Vector3.zero;
        [SerializeField] private Vector3 _axisU = Vector3.right;   // 平面上の右方向
        [SerializeField] private Vector3 _axisV = Vector3.up;      // 平面上の上方向
        [SerializeField] private bool _isLocked = false;
        [SerializeField] private bool _lockOrientation = false;    // カメラ連動の軸更新ロック
        [SerializeField] private bool _autoUpdateOriginOnSelection = true;

        // UI状態（シリアライズ不要）
        private bool _isExpanded = false;

        // === プロパティ ===
        public WorkPlaneMode Mode
        {
            get => _mode;
            set
            {
                if (_mode != value)
                {
                    _mode = value;
                    if (_mode != WorkPlaneMode.Custom)
                    {
                        UpdateAxesFromMode();
                    }
                }
            }
        }

        public Vector3 Origin
        {
            get => _origin;
            set => _origin = value;
        }

        public Vector3 AxisU
        {
            get => _axisU;
            set => _axisU = value.normalized;
        }

        public Vector3 AxisV
        {
            get => _axisV;
            set => _axisV = value.normalized;
        }

        /// <summary>平面の法線（U × V）</summary>
        public Vector3 Normal => Vector3.Cross(_axisU, _axisV).normalized;

        public bool IsLocked
        {
            get => _isLocked;
            set => _isLocked = value;
        }

        /// <summary>カメラ連動の軸更新ロック（CameraParallelモード用）</summary>
        public bool LockOrientation
        {
            get => _lockOrientation;
            set => _lockOrientation = value;
        }

        public bool AutoUpdateOriginOnSelection
        {
            get => _autoUpdateOriginOnSelection;
            set => _autoUpdateOriginOnSelection = value;
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => _isExpanded = value;
        }

        // === コンストラクタ ===
        public WorkPlaneContext()
        {
            ResetInternal();
        }

        public WorkPlaneContext(WorkPlaneContext other)
        {
            CopyFrom(other);
        }

        // === メソッド ===

        /// <summary>
        /// デフォルト状態にリセット（内部用、Undo記録なし）
        /// </summary>
        private void ResetInternal()
        {
            _mode = WorkPlaneMode.CameraParallel;
            _origin = Vector3.zero;
            _axisU = Vector3.right;
            _axisV = Vector3.up;
            _isLocked = false;
            _lockOrientation = false;
            _autoUpdateOriginOnSelection = true;
        }

        /// <summary>
        /// デフォルト状態にリセット（公開用）
        /// 注：Undo記録は呼び出し側で行う
        /// </summary>
        public void Reset()
        {
            ResetInternal();
        }

        /// <summary>
        /// 他のWorkPlaneからコピー
        /// </summary>
        public void CopyFrom(WorkPlaneContext other)
        {
            if (other == null) return;
            _mode = other._mode;
            _origin = other._origin;
            _axisU = other._axisU;
            _axisV = other._axisV;
            _isLocked = other._isLocked;
            _lockOrientation = other._lockOrientation;
            _autoUpdateOriginOnSelection = other._autoUpdateOriginOnSelection;
        }

        /// <summary>
        /// スナップショットを作成
        /// </summary>
        public WorkPlaneSnapshot CreateSnapshot()
        {
            return new WorkPlaneSnapshot
            {
                Mode = _mode,
                Origin = _origin,
                AxisU = _axisU,
                AxisV = _axisV,
                IsLocked = _isLocked,
                LockOrientation = _lockOrientation,
                AutoUpdateOriginOnSelection = _autoUpdateOriginOnSelection
            };
        }

        /// <summary>
        /// スナップショットから復元
        /// </summary>
        public void ApplySnapshot(WorkPlaneSnapshot snapshot)
        {
            _mode = snapshot.Mode;
            _origin = snapshot.Origin;
            _axisU = snapshot.AxisU;
            _axisV = snapshot.AxisV;
            _isLocked = snapshot.IsLocked;
            _lockOrientation = snapshot.LockOrientation;
            _autoUpdateOriginOnSelection = snapshot.AutoUpdateOriginOnSelection;
        }

        /// <summary>
        /// モードに応じて軸を更新
        /// </summary>
        public void UpdateAxesFromMode()
        {
            switch (_mode)
            {
                case WorkPlaneMode.WorldXY:
                    _axisU = Vector3.right;
                    _axisV = Vector3.up;
                    break;
                case WorkPlaneMode.WorldXZ:
                    _axisU = Vector3.right;
                    _axisV = Vector3.forward;
                    break;
                case WorkPlaneMode.WorldYZ:
                    _axisU = Vector3.forward;
                    _axisV = Vector3.up;
                    break;
                case WorkPlaneMode.CameraParallel:
                    // カメラ情報が必要なので、UpdateFromCamera()を呼ぶ必要がある
                    break;
                case WorkPlaneMode.Custom:
                    // 変更なし
                    break;
            }
        }

        /// <summary>
        /// カメラ情報から軸を更新（CameraParallelモード用）
        /// </summary>
        /// <param name="cameraPosition">カメラ位置</param>
        /// <param name="cameraTarget">カメラ注視点</param>
        /// <returns>更新されたかどうか</returns>
        public bool UpdateFromCamera(Vector3 cameraPosition, Vector3 cameraTarget)
        {
            if (_mode != WorkPlaneMode.CameraParallel || _isLocked || _lockOrientation)
                return false;

            Vector3 forward = (cameraTarget - cameraPosition).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            // ほぼ真上/真下から見ている場合
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.right;
            }

            Vector3 up = Vector3.Cross(forward, right).normalized;

            // 変更があるかチェック
            bool changed = Vector3.Distance(_axisU, right) > 0.0001f ||
                          Vector3.Distance(_axisV, up) > 0.0001f;

            _axisU = right;
            _axisV = up;

            return changed;
        }

        /// <summary>
        /// 選択頂点の重心を原点に設定
        /// 注：Undo記録は呼び出し側で行う
        /// </summary>
        public bool UpdateOriginFromSelection(MeshObject meshObject, HashSet<int> selectedVertices)
        {
            if (_isLocked)
                return false;

            if (meshObject == null || selectedVertices == null || selectedVertices.Count == 0)
                return false;

            Vector3 center = Vector3.zero;
            int count = 0;

            foreach (int idx in selectedVertices)
            {
                if (idx >= 0 && idx < meshObject.VertexCount)
                {
                    center += meshObject.Vertices[idx].Position;
                    count++;
                }
            }

            if (count > 0)
            {
                _origin = center / count;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 軸を直交正規化
        /// 注：Undo記録は呼び出し側で行う
        /// </summary>
        public void Orthonormalize()
        {
            Vector3 u = _axisU.normalized;
            Vector3 v = _axisV;

            // VをUに直交させる
            v = (v - Vector3.Dot(v, u) * u).normalized;

            // 縮退チェック
            if (v.sqrMagnitude < 0.001f)
            {
                // 適当な直交ベクトルを生成
                v = Vector3.Cross(u, Vector3.up).normalized;
                if (v.sqrMagnitude < 0.001f)
                {
                    v = Vector3.Cross(u, Vector3.right).normalized;
                }
            }

            _axisU = u;
            _axisV = v;
        }

        /// <summary>
        /// ワールド座標を平面上のローカル座標(U, V)に変換
        /// </summary>
        public Vector2 WorldToPlane(Vector3 worldPos)
        {
            Vector3 local = worldPos - _origin;
            float u = Vector3.Dot(local, _axisU);
            float v = Vector3.Dot(local, _axisV);
            return new Vector2(u, v);
        }

        /// <summary>
        /// 平面上のローカル座標(U, V)をワールド座標に変換
        /// </summary>
        public Vector3 PlaneToWorld(Vector2 planePos)
        {
            return _origin + _axisU * planePos.x + _axisV * planePos.y;
        }

        /// <summary>
        /// ワールド座標を平面上に投影
        /// </summary>
        public Vector3 ProjectToPlane(Vector3 worldPos)
        {
            Vector2 uv = WorldToPlane(worldPos);
            return PlaneToWorld(uv);
        }

        /// <summary>
        /// レイと平面の交点を計算
        /// </summary>
        /// <param name="rayOrigin">レイの始点</param>
        /// <param name="rayDirection">レイの方向</param>
        /// <param name="hitPoint">交点（出力）</param>
        /// <returns>交差したかどうか</returns>
        public bool RayIntersect(Vector3 rayOrigin, Vector3 rayDirection, out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;
            Vector3 normal = Normal;

            float denom = Vector3.Dot(rayDirection, normal);
            if (Mathf.Abs(denom) < 1e-6f)
                return false; // レイが平面と平行

            float t = Vector3.Dot(_origin - rayOrigin, normal) / denom;
            if (t < 0)
                return false; // 交点がレイの後ろ

            hitPoint = rayOrigin + rayDirection * t;
            return true;
        }
    }

    // ================================================================
    // ギズモ描画ヘルパー
    // ================================================================
    public static class WorkPlaneGizmo
    {
        /// <summary>
        /// シーンビュー/プレビューにWorkPlaneを描画（GLGizmoDrawer使用、Editor非依存）
        /// </summary>
        public static void DrawGizmo(WorkPlaneContext workPlane, float size = 1f, float alpha = 0.3f)
        {
            if (workPlane == null) return;

            Vector3 origin = workPlane.Origin;
            Vector3 axisU = workPlane.AxisU;
            Vector3 axisV = workPlane.AxisV;
            Vector3 normal = workPlane.Normal;

            // 平面グリッド
            Color gridColor = new Color(0.5f, 0.8f, 1f, alpha);
            UnityEditor_Handles.color = gridColor;

            int gridLines = 5;
            float halfSize = size * 0.5f;

            for (int i = -gridLines; i <= gridLines; i++)
            {
                float t = i / (float)gridLines;
                // U方向の線
                Vector3 startU = origin + axisV * (t * size) - axisU * halfSize;
                Vector3 endU = origin + axisV * (t * size) + axisU * halfSize;
                UnityEditor_Handles.DrawLine(startU, endU);

                // V方向の線
                Vector3 startV = origin + axisU * (t * size) - axisV * halfSize;
                Vector3 endV = origin + axisU * (t * size) + axisV * halfSize;
                UnityEditor_Handles.DrawLine(startV, endV);
            }

            // 軸
            UnityEditor_Handles.color = Color.red;
            UnityEditor_Handles.DrawLine(origin, origin + axisU * size * 0.3f);
            UnityEditor_Handles.color = Color.green;
            UnityEditor_Handles.DrawLine(origin, origin + axisV * size * 0.3f);
            UnityEditor_Handles.color = Color.blue;
            UnityEditor_Handles.DrawLine(origin, origin + normal * size * 0.15f);

            // 原点マーカー
            UnityEditor_Handles.color = Color.yellow;
            UnityEditor_Handles.DrawWireDisc(origin, normal, size * 0.05f);
        }
    }
}

// ================================================================
// Undoシステム統合
// ================================================================
namespace Poly_Ling.UndoSystem
{
    using Poly_Ling.Tools;

    /// <summary>
    /// WorkPlane変更記録
    /// </summary>
    public class WorkPlaneChangeRecord : IUndoRecord<WorkPlaneContext>
    {
        public UndoOperationInfo Info { get; set; }

        public WorkPlaneSnapshot Before;
        public WorkPlaneSnapshot After;
        public string Description;

        public WorkPlaneChangeRecord(WorkPlaneSnapshot before, WorkPlaneSnapshot after, string description = null)
        {
            Before = before;
            After = after;
            Description = description ?? after.GetChangeDescription(before);
        }

        public void Undo(WorkPlaneContext context)
        {
            context?.ApplySnapshot(Before);
        }

        public void Redo(WorkPlaneContext context)
        {
            context?.ApplySnapshot(After);
        }
    }
}