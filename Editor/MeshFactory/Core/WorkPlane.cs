// Assets/Editor/MeshCreators/WorkPlane.cs
// 作業平面（Work Plane）クラス
// 頂点追加時の配置平面、編集時の参照平面を定義
// 全操作がUndo対応

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;

namespace MeshFactory.Tools
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
                return $"Change WorkPlane Mode to {Mode}";
            if (Vector3.Distance(Origin, before.Origin) > 0.0001f)
                return "Change WorkPlane Origin";
            if (Vector3.Distance(AxisU, before.AxisU) > 0.0001f || 
                Vector3.Distance(AxisV, before.AxisV) > 0.0001f)
                return "Change WorkPlane Orientation";
            if (IsLocked != before.IsLocked)
                return IsLocked ? "Lock WorkPlane" : "Unlock WorkPlane";
            if (LockOrientation != before.LockOrientation)
                return LockOrientation ? "Lock WorkPlane Orientation" : "Unlock WorkPlane Orientation";
            if (AutoUpdateOriginOnSelection != before.AutoUpdateOriginOnSelection)
                return AutoUpdateOriginOnSelection ? "Enable Auto-update Origin" : "Disable Auto-update Origin";
            return "Change WorkPlane";
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
    public class WorkPlane
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
        public WorkPlane()
        {
            ResetInternal();
        }

        public WorkPlane(WorkPlane other)
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
        public void CopyFrom(WorkPlane other)
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
        public bool UpdateOriginFromSelection(MeshData meshData, HashSet<int> selectedVertices)
        {
            if (_isLocked)
                return false;

            if (meshData == null || selectedVertices == null || selectedVertices.Count == 0)
                return false;

            Vector3 center = Vector3.zero;
            int count = 0;

            foreach (int idx in selectedVertices)
            {
                if (idx >= 0 && idx < meshData.VertexCount)
                {
                    center += meshData.Vertices[idx].Position;
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
    // UI描画ヘルパー
    // ================================================================
    /// <summary>
    /// WorkPlaneのUI描画
    /// </summary>
    public static class WorkPlaneUI
    {
        private static readonly string[] ModeNames = new[]
        {
            "Camera Parallel",
            "World XY",
            "World XZ (Floor)",
            "World YZ",
            "Custom"
        };

        private static GUIStyle _compactLabelStyle;

        /// <summary>
        /// "From Selection"ボタンクリック時のイベント
        /// 呼び出し側でハンドラを設定して使用
        /// </summary>
        public static event Action OnFromSelectionClicked;

        /// <summary>
        /// 変更発生時のイベント（Undo記録用）
        /// パラメータ: before, after, description
        /// </summary>
        public static event Action<WorkPlaneSnapshot, WorkPlaneSnapshot, string> OnChanged;

        /// <summary>
        /// WorkPlane UIを描画
        /// </summary>
        /// <param name="workPlane">対象のWorkPlane</param>
        /// <returns>変更があったか</returns>
        public static bool DrawUI(WorkPlane workPlane)
        {
            if (workPlane == null) return false;

            InitStyles();

            WorkPlaneSnapshot before = workPlane.CreateSnapshot();
            bool changed = false;
            string changeDescription = "";

            // ヘッダー（折りたたみ + ロック + リセット）
            EditorGUILayout.BeginHorizontal();
            {
                workPlane.IsExpanded = EditorGUILayout.Foldout(workPlane.IsExpanded, "Work Plane", true);

                GUILayout.FlexibleSpace();

                // コンパクト表示（折りたたみ時）
                if (!workPlane.IsExpanded)
                {
                    EditorGUILayout.LabelField($"{ModeNames[(int)workPlane.Mode]}", _compactLabelStyle, GUILayout.Width(70));
                }

                // ロックボタン
                string lockLabel = workPlane.IsLocked ? "🔒" : "🔓";
                if (GUILayout.Button(lockLabel, GUILayout.Width(24), GUILayout.Height(18)))
                {
                    workPlane.IsLocked = !workPlane.IsLocked;
                    changed = true;
                    changeDescription = workPlane.IsLocked ? "Lock WorkPlane" : "Unlock WorkPlane";
                }

                // リセットボタン
                if (GUILayout.Button("⟲", GUILayout.Width(24), GUILayout.Height(18)))
                {
                    workPlane.Reset();
                    changed = true;
                    changeDescription = "Reset WorkPlane";
                }
            }
            EditorGUILayout.EndHorizontal();

            // 展開時の詳細UI
            if (workPlane.IsExpanded)
            {
                EditorGUI.BeginDisabledGroup(workPlane.IsLocked);
                {
                    // モード選択
                    WorkPlaneMode newMode = (WorkPlaneMode)EditorGUILayout.Popup(
                        "Mode",
                        (int)workPlane.Mode,
                        ModeNames
                    );
                    if (newMode != workPlane.Mode)
                    {
                        workPlane.Mode = newMode;
                        changed = true;
                        changeDescription = $"Change WorkPlane Mode to {newMode}";
                    }

                    EditorGUILayout.Space(2);

                    // === Origin（コンパクト表示） ===
                    EditorGUILayout.LabelField("Origin", EditorStyles.miniLabel);
                    Vector3 origin = workPlane.Origin;
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUIUtility.labelWidth = 14;
                        float newX = EditorGUILayout.FloatField("X", origin.x);
                        float newY = EditorGUILayout.FloatField("Y", origin.y);
                        float newZ = EditorGUILayout.FloatField("Z", origin.z);
                        EditorGUIUtility.labelWidth = 0; // reset

                        Vector3 newOrigin = new Vector3(newX, newY, newZ);
                        if (newOrigin != origin)
                        {
                            workPlane.Origin = newOrigin;
                            changed = true;
                            changeDescription = "Change WorkPlane Origin";
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    // From Selection ボタン
                    if (GUILayout.Button("⎔ From Selection", GUILayout.Height(18)))
                    {
                        OnFromSelectionClicked?.Invoke();
                    }

                    EditorGUILayout.Space(2);

                    // === Axis U/V（常に表示、Customモードのみ編集可能） ===
                    bool isCustomMode = workPlane.Mode == WorkPlaneMode.Custom;
                    
                    EditorGUILayout.LabelField("Axis U", EditorStyles.miniLabel);
                    if (isCustomMode)
                    {
                        Vector3 axisU = workPlane.AxisU;
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUIUtility.labelWidth = 14;
                            float uX = EditorGUILayout.FloatField("X", axisU.x);
                            float uY = EditorGUILayout.FloatField("Y", axisU.y);
                            float uZ = EditorGUILayout.FloatField("Z", axisU.z);
                            EditorGUIUtility.labelWidth = 0;

                            Vector3 newAxisU = new Vector3(uX, uY, uZ);
                            if (newAxisU != axisU)
                            {
                                workPlane.AxisU = newAxisU;
                                changed = true;
                                changeDescription = "Change WorkPlane Axis U";
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    else
                    {
                        // 読み取り専用表示
                        Vector3 u = workPlane.AxisU;
                        EditorGUILayout.LabelField($"  ({u.x:F2}, {u.y:F2}, {u.z:F2})", EditorStyles.miniLabel);
                    }

                    EditorGUILayout.LabelField("Axis V", EditorStyles.miniLabel);
                    if (isCustomMode)
                    {
                        Vector3 axisV = workPlane.AxisV;
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUIUtility.labelWidth = 14;
                            float vX = EditorGUILayout.FloatField("X", axisV.x);
                            float vY = EditorGUILayout.FloatField("Y", axisV.y);
                            float vZ = EditorGUILayout.FloatField("Z", axisV.z);
                            EditorGUIUtility.labelWidth = 0;

                            Vector3 newAxisV = new Vector3(vX, vY, vZ);
                            if (newAxisV != axisV)
                            {
                                workPlane.AxisV = newAxisV;
                                changed = true;
                                changeDescription = "Change WorkPlane Axis V";
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    else
                    {
                        // 読み取り専用表示
                        Vector3 v = workPlane.AxisV;
                        EditorGUILayout.LabelField($"  ({v.x:F2}, {v.y:F2}, {v.z:F2})", EditorStyles.miniLabel);
                    }

                    // Normal（読み取り専用）
                    Vector3 n = workPlane.Normal;
                    EditorGUILayout.LabelField("Normal", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"  ({n.x:F2}, {n.y:F2}, {n.z:F2})", EditorStyles.miniLabel);

                    // Customモード時のみOrthonormalizeボタン
                    if (isCustomMode)
                    {
                        if (GUILayout.Button("Orthonormalize", GUILayout.Height(18)))
                        {
                            workPlane.Orthonormalize();
                            changed = true;
                            changeDescription = "Orthonormalize WorkPlane";
                        }
                    }

                    EditorGUILayout.Space(2);

                    // === オプション ===
                    bool newAutoUpdate = EditorGUILayout.ToggleLeft(
                        "Auto-update origin",
                        workPlane.AutoUpdateOriginOnSelection
                    );
                    if (newAutoUpdate != workPlane.AutoUpdateOriginOnSelection)
                    {
                        workPlane.AutoUpdateOriginOnSelection = newAutoUpdate;
                        changed = true;
                        changeDescription = newAutoUpdate ? "Enable Auto-update Origin" : "Disable Auto-update Origin";
                    }

                    // カメラ連動ロック（CameraParallelモード時のみ）
                    if (workPlane.Mode == WorkPlaneMode.CameraParallel)
                    {
                        bool newLockOrientation = EditorGUILayout.ToggleLeft(
                            "Lock orientation",
                            workPlane.LockOrientation
                        );
                        if (newLockOrientation != workPlane.LockOrientation)
                        {
                            workPlane.LockOrientation = newLockOrientation;
                            changed = true;
                            changeDescription = newLockOrientation ? "Lock WorkPlane Orientation" : "Unlock WorkPlane Orientation";
                        }
                    }
                }
                EditorGUI.EndDisabledGroup();
            }

            // 変更があればコールバック
            if (changed)
            {
                WorkPlaneSnapshot after = workPlane.CreateSnapshot();
                if (before.IsDifferentFrom(after))
                {
                    OnChanged?.Invoke(before, after, changeDescription);
                }
            }

            return changed;
        }

        private static void InitStyles()
        {
            if (_compactLabelStyle == null)
            {
                _compactLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight
                };
            }
        }

        /// <summary>
        /// シーンビュー/プレビューにWorkPlaneを描画
        /// </summary>
        public static void DrawGizmo(WorkPlane workPlane, float size = 1f, float alpha = 0.3f)
        {
            if (workPlane == null) return;

            Vector3 origin = workPlane.Origin;
            Vector3 axisU = workPlane.AxisU;
            Vector3 axisV = workPlane.AxisV;
            Vector3 normal = workPlane.Normal;

            // 平面グリッド
            Color gridColor = new Color(0.5f, 0.8f, 1f, alpha);
            Handles.color = gridColor;

            int gridLines = 5;
            float halfSize = size * 0.5f;

            for (int i = -gridLines; i <= gridLines; i++)
            {
                float t = i / (float)gridLines;
                // U方向の線
                Vector3 startU = origin + axisV * (t * size) - axisU * halfSize;
                Vector3 endU = origin + axisV * (t * size) + axisU * halfSize;
                Handles.DrawLine(startU, endU);

                // V方向の線
                Vector3 startV = origin + axisU * (t * size) - axisV * halfSize;
                Vector3 endV = origin + axisU * (t * size) + axisV * halfSize;
                Handles.DrawLine(startV, endV);
            }

            // 軸
            Handles.color = Color.red;
            Handles.DrawLine(origin, origin + axisU * size * 0.3f);
            Handles.color = Color.green;
            Handles.DrawLine(origin, origin + axisV * size * 0.3f);
            Handles.color = Color.blue;
            Handles.DrawLine(origin, origin + normal * size * 0.15f);

            // 原点マーカー
            Handles.color = Color.yellow;
            Handles.DrawWireDisc(origin, normal, size * 0.05f);
        }
    }
}

// ================================================================
// Undoシステム統合
// ================================================================
namespace MeshFactory.UndoSystem
{
    using MeshFactory.Tools;

    /// <summary>
    /// WorkPlane変更記録
    /// </summary>
    public class WorkPlaneChangeRecord : IUndoRecord<WorkPlane>
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

        public void Undo(WorkPlane context)
        {
            context?.ApplySnapshot(Before);
        }

        public void Redo(WorkPlane context)
        {
            context?.ApplySnapshot(After);
        }
    }
}
