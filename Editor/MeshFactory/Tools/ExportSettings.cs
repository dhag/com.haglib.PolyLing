// Assets/Editor/MeshCreators/ExportSettings.cs
// エクスポート時のローカルトランスフォーム設定
// ヒエラルキー/プレファブへのエクスポート時に適用されるPosition, Rotation, Scale
// 全操作がUndo対応

using System;
using UnityEditor;
using UnityEngine;

namespace MeshFactory.Tools
{
    // ================================================================
    // スナップショット（Undo用）
    // ================================================================

    /// <summary>
    /// ExportSettingsの状態スナップショット（Undo用）
    /// </summary>
    [Serializable]
    public struct ExportSettingsSnapshot
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale;
        public bool UseLocalTransform;

        /// <summary>
        /// 他のスナップショットと異なるかどうか
        /// </summary>
        public bool IsDifferentFrom(ExportSettingsSnapshot other)
        {
            return Vector3.Distance(Position, other.Position) > 0.0001f ||
                   Vector3.Distance(Rotation, other.Rotation) > 0.0001f ||
                   Vector3.Distance(Scale, other.Scale) > 0.0001f ||
                   UseLocalTransform != other.UseLocalTransform;
        }

        /// <summary>
        /// 変更内容の説明を取得
        /// </summary>
        public string GetChangeDescription(ExportSettingsSnapshot before)
        {
            if (UseLocalTransform != before.UseLocalTransform)
                return UseLocalTransform ? "Enable Local Transform" : "Disable Local Transform";
            if (Vector3.Distance(Position, before.Position) > 0.0001f)
                return "Change Export Position";
            if (Vector3.Distance(Rotation, before.Rotation) > 0.0001f)
                return "Change Export Rotation";
            if (Vector3.Distance(Scale, before.Scale) > 0.0001f)
                return "Change Export Scale";
            return "Change Export Settings";
        }
    }

    // ================================================================
    // エクスポート設定
    // ================================================================

    /// <summary>
    /// エクスポート設定
    /// ヒエラルキー/プレファブへのエクスポート時のローカルトランスフォームを定義
    /// </summary>
    [Serializable]
    public class ExportSettings
    {
        // === フィールド ===
        [SerializeField] private Vector3 _position = Vector3.zero;
        [SerializeField] private Vector3 _rotation = Vector3.zero;  // Euler angles
        [SerializeField] private Vector3 _scale = Vector3.one;
        [SerializeField] private bool _useLocalTransform = false;

        // UI状態（シリアライズ不要）
        private bool _isExpanded = false;

        // === プロパティ ===

        public Vector3 Position
        {
            get => _position;
            set => _position = value;
        }

        public Vector3 Rotation
        {
            get => _rotation;
            set => _rotation = value;
        }

        public Vector3 Scale
        {
            get => _scale;
            set => _scale = value;
        }

        /// <summary>ローカルトランスフォームを使用するか</summary>
        public bool UseLocalTransform
        {
            get => _useLocalTransform;
            set => _useLocalTransform = value;
        }

        /// <summary>回転をQuaternionで取得</summary>
        public Quaternion RotationQuaternion => Quaternion.Euler(_rotation);

        /// <summary>変換行列を取得</summary>
        public Matrix4x4 TransformMatrix => Matrix4x4.TRS(_position, RotationQuaternion, _scale);

        public bool IsExpanded
        {
            get => _isExpanded;
            set => _isExpanded = value;
        }

        // === コンストラクタ ===

        public ExportSettings()
        {
            ResetInternal();
        }

        public ExportSettings(ExportSettings other)
        {
            CopyFrom(other);
        }

        // === メソッド ===

        /// <summary>
        /// デフォルト状態にリセット（内部用、Undo記録なし）
        /// </summary>
        private void ResetInternal()
        {
            _position = Vector3.zero;
            _rotation = Vector3.zero;
            _scale = Vector3.one;
            _useLocalTransform = false;
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
        /// 他のExportSettingsからコピー
        /// </summary>
        public void CopyFrom(ExportSettings other)
        {
            if (other == null) return;
            _position = other._position;
            _rotation = other._rotation;
            _scale = other._scale;
            _useLocalTransform = other._useLocalTransform;
        }

        /// <summary>
        /// スナップショットを作成
        /// </summary>
        public ExportSettingsSnapshot CreateSnapshot()
        {
            return new ExportSettingsSnapshot
            {
                Position = _position,
                Rotation = _rotation,
                Scale = _scale,
                UseLocalTransform = _useLocalTransform
            };
        }

        /// <summary>
        /// スナップショットから復元
        /// </summary>
        public void ApplySnapshot(ExportSettingsSnapshot snapshot)
        {
            _position = snapshot.Position;
            _rotation = snapshot.Rotation;
            _scale = snapshot.Scale;
            _useLocalTransform = snapshot.UseLocalTransform;
        }

        /// <summary>
        /// GameObjectにトランスフォームを適用
        /// </summary>
        public void ApplyToGameObject(GameObject go, bool asLocal = true)
        {
            if (go == null) return;

            if (!_useLocalTransform)
            {
                // デフォルト値を適用
                if (asLocal)
                {
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                }
                else
                {
                    go.transform.position = Vector3.zero;
                    go.transform.rotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                }
                return;
            }

            if (asLocal)
            {
                go.transform.localPosition = _position;
                go.transform.localRotation = RotationQuaternion;
                go.transform.localScale = _scale;
            }
            else
            {
                go.transform.position = _position;
                go.transform.rotation = RotationQuaternion;
                go.transform.localScale = _scale;
            }
        }

/// <summary>
        /// 現在の選択からトランスフォームを取得
        /// </summary>
        public void CopyFromSelection()
        {
            if (UnityEditor.Selection.activeTransform != null)
            {
                _position = UnityEditor.Selection.activeTransform.localPosition;
                _rotation = UnityEditor.Selection.activeTransform.localEulerAngles;
                _scale = UnityEditor.Selection.activeTransform.localScale;
            }
        }

        // === デバッグ ===

        public override string ToString()
        {
            return $"ExportSettings(Use:{_useLocalTransform}, P:{_position}, R:{_rotation}, S:{_scale})";
        }
    }

    // ================================================================
    // UI描画クラス
    // ================================================================

    /// <summary>
    /// ExportSettings用UI描画
    /// WorkPlaneUIと同様の設計
    /// </summary>
    public static class ExportSettingsUI
    {
        // === イベント ===
        
        /// <summary>設定変更時（Undo記録用）</summary>
        public static event Action<ExportSettingsSnapshot, ExportSettingsSnapshot, string> OnChanged;
        
        /// <summary>リセットボタンクリック時</summary>
        public static event Action OnResetClicked;

        /// <summary>「From Selection」ボタンクリック時</summary>
        public static event Action OnFromSelectionClicked;

        // === スタイル ===
        private static GUIStyle _headerStyle;
        private static GUIStyle _compactLabelStyle;

        /// <summary>
        /// UIを描画
        /// </summary>
        /// <returns>変更があったか</returns>
        public static bool DrawUI(ExportSettings settings)
        {
            if (settings == null) return false;

            InitStyles();

            bool changed = false;
            string changeDescription = null;
            ExportSettingsSnapshot before = settings.CreateSnapshot();

            // === ヘッダー + 折りたたみ ===
            EditorGUILayout.BeginHorizontal();
            {
                settings.IsExpanded = EditorGUILayout.Foldout(
                    settings.IsExpanded,
                    "Export Transform",
                    true
                );

                // 有効/無効トグル（ヘッダー右側）
                bool newUse = EditorGUILayout.Toggle(settings.UseLocalTransform, GUILayout.Width(20));
                if (newUse != settings.UseLocalTransform)
                {
                    settings.UseLocalTransform = newUse;
                    changed = true;
                    changeDescription = newUse ? "Enable Local Transform" : "Disable Local Transform";
                }
            }
            EditorGUILayout.EndHorizontal();

            // === 展開時のコンテンツ ===
            if (settings.IsExpanded)
            {
                EditorGUI.BeginDisabledGroup(!settings.UseLocalTransform);
                {
                    EditorGUI.indentLevel++;

                    // Position
                    EditorGUILayout.LabelField("Position", EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUIUtility.labelWidth = 14;
                        float px = EditorGUILayout.FloatField("X", settings.Position.x);
                        float py = EditorGUILayout.FloatField("Y", settings.Position.y);
                        float pz = EditorGUILayout.FloatField("Z", settings.Position.z);
                        EditorGUIUtility.labelWidth = 0;

                        Vector3 newPos = new Vector3(px, py, pz);
                        if (newPos != settings.Position)
                        {
                            settings.Position = newPos;
                            changed = true;
                            changeDescription = "Change Export Position";
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    // Rotation
                    EditorGUILayout.LabelField("Rotation", EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUIUtility.labelWidth = 14;
                        float rx = EditorGUILayout.FloatField("X", settings.Rotation.x);
                        float ry = EditorGUILayout.FloatField("Y", settings.Rotation.y);
                        float rz = EditorGUILayout.FloatField("Z", settings.Rotation.z);
                        EditorGUIUtility.labelWidth = 0;

                        Vector3 newRot = new Vector3(rx, ry, rz);
                        if (newRot != settings.Rotation)
                        {
                            settings.Rotation = newRot;
                            changed = true;
                            changeDescription = "Change Export Rotation";
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    // Scale
                    EditorGUILayout.LabelField("Scale", EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUIUtility.labelWidth = 14;
                        float sx = EditorGUILayout.FloatField("X", settings.Scale.x);
                        float sy = EditorGUILayout.FloatField("Y", settings.Scale.y);
                        float sz = EditorGUILayout.FloatField("Z", settings.Scale.z);
                        EditorGUIUtility.labelWidth = 0;

                        Vector3 newScale = new Vector3(sx, sy, sz);
                        if (newScale != settings.Scale)
                        {
                            settings.Scale = newScale;
                            changed = true;
                            changeDescription = "Change Export Scale";
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(4);

                    // ボタン群
                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("From Selection", GUILayout.Height(18)))
                        {
                            OnFromSelectionClicked?.Invoke();
                        }
                        if (GUILayout.Button("Reset", GUILayout.Height(18)))
                        {
                            OnResetClicked?.Invoke();
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUI.indentLevel--;
                }
                EditorGUI.EndDisabledGroup();
            }

            // 変更があればコールバック
            if (changed)
            {
                ExportSettingsSnapshot after = settings.CreateSnapshot();
                if (before.IsDifferentFrom(after))
                {
                    OnChanged?.Invoke(before, after, changeDescription);
                }
            }

            return changed;
        }

        /// <summary>
        /// コンパクトUI（折りたたみなし、1行版）
        /// </summary>
        public static bool DrawCompactUI(ExportSettings settings)
        {
            if (settings == null) return false;

            bool changed = false;
            ExportSettingsSnapshot before = settings.CreateSnapshot();

            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("Export Transform:", GUILayout.Width(100));
                
                bool newUse = EditorGUILayout.Toggle(settings.UseLocalTransform, GUILayout.Width(20));
                if (newUse != settings.UseLocalTransform)
                {
                    settings.UseLocalTransform = newUse;
                    changed = true;
                }

                if (settings.UseLocalTransform)
                {
                    EditorGUILayout.LabelField(
                        $"P:{settings.Position:F1} R:{settings.Rotation:F1} S:{settings.Scale:F1}",
                        EditorStyles.miniLabel
                    );
                }
                else
                {
                    EditorGUILayout.LabelField("(Default)", EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (changed)
            {
                ExportSettingsSnapshot after = settings.CreateSnapshot();
                if (before.IsDifferentFrom(after))
                {
                    OnChanged?.Invoke(before, after, "Change Export Settings");
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
    }
}

// ================================================================
// Undoシステム統合
// ================================================================

namespace MeshFactory.UndoSystem
{
    using MeshFactory.Tools;

    /// <summary>
    /// ExportSettings変更記録
    /// </summary>
    public class ExportSettingsChangeRecord : IUndoRecord<ExportSettings>
    {
        public UndoOperationInfo Info { get; set; }

        public ExportSettingsSnapshot Before;
        public ExportSettingsSnapshot After;
        public string Description;

        public ExportSettingsChangeRecord(
            ExportSettingsSnapshot before, 
            ExportSettingsSnapshot after, 
            string description = null)
        {
            Before = before;
            After = after;
            Description = description ?? after.GetChangeDescription(before);
        }

        public void Undo(ExportSettings context)
        {
            context?.ApplySnapshot(Before);
        }

        public void Redo(ExportSettings context)
        {
            context?.ApplySnapshot(After);
        }
    }
}
