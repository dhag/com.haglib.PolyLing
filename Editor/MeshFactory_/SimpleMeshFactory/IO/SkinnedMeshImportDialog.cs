// Assets/Editor/MeshFactory/SimpleMeshFactory/SkinnedMeshImportDialog.cs
// SkinnedMeshRenderer取り込み時のオプションダイアログ

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeshFactory
{
    /// <summary>
    /// SkinnedMeshRenderer取り込みダイアログ
    /// </summary>
    public class SkinnedMeshImportDialog : EditorWindow
    {
        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>
        /// インポート実行時のコールバック
        /// (importMesh, importBones, selectedRootBone)
        /// </summary>
        public Action<bool, bool, Transform> OnImport;

        // ================================================================
        // 設定
        // ================================================================

        private GameObject _rootObject;
        private Transform _detectedRootBone;
        private Transform _selectedRootBone;
        private int _boneCount;
        private int _meshCount;
        private List<Transform> _rootBoneCandidates = new List<Transform>();

        private bool _importMesh = true;
        private bool _importBones = true;

        // ================================================================
        // 表示
        // ================================================================

        /// <summary>
        /// ダイアログを表示
        /// </summary>
        public static SkinnedMeshImportDialog Show(
            GameObject rootObject,
            Transform detectedRootBone,
            int boneCount,
            int meshCount)
        {
            var dialog = GetWindow<SkinnedMeshImportDialog>(true, "スキンメッシュ インポート", true);
            dialog._rootObject = rootObject;
            dialog._detectedRootBone = detectedRootBone;
            dialog._selectedRootBone = detectedRootBone;
            dialog._boneCount = boneCount;
            dialog._meshCount = meshCount;

            // ルートボーン候補を収集
            dialog.CollectRootBoneCandidates();

            dialog.minSize = new Vector2(350, 220);
            dialog.maxSize = new Vector2(500, 300);
            dialog.ShowUtility();

            return dialog;
        }

        private void CollectRootBoneCandidates()
        {
            _rootBoneCandidates.Clear();

            if (_detectedRootBone != null)
            {
                _rootBoneCandidates.Add(_detectedRootBone);

                // 子ボーンも候補に追加（1階層のみ）
                foreach (Transform child in _detectedRootBone)
                {
                    _rootBoneCandidates.Add(child);
                }
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            // ヘッダー
            EditorGUILayout.LabelField("インポート対象", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUI.indentLevel++;

            // オブジェクト名
            EditorGUILayout.LabelField("オブジェクト", _rootObject?.name ?? "(なし)");
            EditorGUILayout.LabelField("メッシュ数", _meshCount.ToString());

            if (_detectedRootBone != null)
            {
                EditorGUILayout.LabelField("検出ボーン", $"{_detectedRootBone.name} ({_boneCount}ボーン)");
            }
            else
            {
                EditorGUILayout.LabelField("検出ボーン", "(なし)");
            }

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // オプション
            EditorGUILayout.LabelField("オプション", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUI.indentLevel++;

            _importMesh = EditorGUILayout.Toggle("メッシュを取り込む", _importMesh);

            EditorGUI.BeginDisabledGroup(_detectedRootBone == null);
            _importBones = EditorGUILayout.Toggle("ボーンを取り込む", _importBones);

            if (_importBones && _rootBoneCandidates.Count > 0)
            {
                // ルートボーン選択
                int currentIndex = _rootBoneCandidates.IndexOf(_selectedRootBone);
                if (currentIndex < 0) currentIndex = 0;

                string[] options = new string[_rootBoneCandidates.Count];
                for (int i = 0; i < _rootBoneCandidates.Count; i++)
                {
                    var bone = _rootBoneCandidates[i];
                    int count = CountDescendants(bone) + 1;
                    options[i] = $"{bone.name} ({count}ボーン)";
                }

                int newIndex = EditorGUILayout.Popup("ルートボーン", currentIndex, options);
                if (newIndex >= 0 && newIndex < _rootBoneCandidates.Count)
                {
                    _selectedRootBone = _rootBoneCandidates[newIndex];
                    _boneCount = CountDescendants(_selectedRootBone) + 1;
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(15);

            // ボタン
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("キャンセル", GUILayout.Width(100)))
            {
                Close();
            }

            EditorGUI.BeginDisabledGroup(!_importMesh && !_importBones);
            if (GUILayout.Button("インポート", GUILayout.Width(100)))
            {
                OnImport?.Invoke(_importMesh, _importBones, _selectedRootBone);
                Close();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
        }

        private int CountDescendants(Transform t)
        {
            if (t == null) return 0;
            int count = 0;
            foreach (Transform child in t)
            {
                count += 1 + CountDescendants(child);
            }
            return count;
        }
    }
}
