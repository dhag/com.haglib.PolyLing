// VMDTestPanel.cs
// VMDモーションテスト用のシンプルなエディタウィンドウ
// IToolPanelBaseパターン準拠

using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Poly_Ling.Model;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// VMDテスト用ウィンドウ
    /// PolyLing > VMD Test Panel で開く
    /// SimpleMeshFactoryからSetContext()で呼び出される
    /// </summary>
    public class VMDTestPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "VMDTestPanel";
        public override string Title => "VMD Test";

        // ================================================================
        // 状態
        // ================================================================

        private VMDData _vmd;
        private string _filePath;
        private float _currentFrame;
        private VMDApplier _applier;

        // UI状態
        private Vector2 _scrollPos;
        private bool _showBoneList;
        private bool _showMorphList;
        private bool _applyCoordinateConversion = false;

        // ================================================================
        // ウィンドウ
        // ================================================================

        [MenuItem("PolyLing/VMD Test Panel")]
        public static void ShowWindow()
        {
            var window = GetWindow<VMDTestPanel>("VMD Test");
            window.minSize = new Vector2(300, 200);
        }

        /// <summary>
        /// ToolContextからオープン（SimpleMeshFactoryから呼ぶ）
        /// </summary>
        public static void Open(ToolContext ctx)
        {
            var window = GetWindow<VMDTestPanel>("VMD Test");
            window.minSize = new Vector2(300, 200);
            window.SetContext(ctx);
        }

        private void OnEnable()
        {
            _applier = new VMDApplier();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            // リセット
            if (Model != null && _applier != null)
            {
                _applier.ResetAllBones(Model);
            }
        }

        protected override void OnContextSet()
        {
            // コンテキスト設定時にマッピングを再構築
            if (Model != null)
            {
                _applier.BuildMapping(Model);

                // 座標系設定をEditorStateContextから取得
                var editorState = _context?.UndoController?.EditorState;
                if (editorState != null)
                {
                    _applier.PositionScale = editorState.CoordinateScale;
                    _applyCoordinateConversion = editorState.PmxFlipZ;
                    _applier.ApplyCoordinateConversion = _applyCoordinateConversion;
                }

                // VMDがロード済みなら再適用
                if (_vmd != null)
                {
                    ApplyFrame();
                }
            }
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.Space(8);

            // コンテキスト確認
            if (!DrawNoContextWarning("ToolContextが未設定です。\nSimpleMeshFactoryウィンドウから開いてください。"))
            {
                return;
            }

            // モデル情報
            DrawModelInfo();

            EditorGUILayout.Space(8);

            // VMDファイル
            DrawFileSection();

            if (_vmd == null) return;

            EditorGUILayout.Space(8);

            // VMD情報
            DrawVMDInfo();

            EditorGUILayout.Space(8);

            // フレームコントロール
            DrawFrameControl();

            EditorGUILayout.Space(8);

            // ボーン/モーフリスト
            DrawDataLists();
        }

        // ================================================================
        // UI部品
        // ================================================================

        private void DrawModelInfo()
        {
            EditorGUILayout.LabelField("Model", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (Model != null)
                {
                    EditorGUILayout.LabelField($"✓ {Model.Name} ({Model.BoneCount} bones)");
                }
                else
                {
                    EditorGUILayout.LabelField("(No model loaded)");
                }
            }
        }

        private void DrawFileSection()
        {
            EditorGUILayout.LabelField("VMD File", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                string displayName = string.IsNullOrEmpty(_filePath)
                    ? "(None)"
                    : Path.GetFileName(_filePath);

                EditorGUILayout.TextField(displayName);

                if (GUILayout.Button("Open", GUILayout.Width(60)))
                {
                    OpenVMDFile();
                }

                EditorGUI.BeginDisabledGroup(_vmd == null);
                if (GUILayout.Button("Clear", GUILayout.Width(50)))
                {
                    ClearVMD();
                }
                if (GUILayout.Button("Reload", GUILayout.Width(60)))
                {
                    ReloadVMD();
                }
                EditorGUI.EndDisabledGroup();
            }
        }

        private void DrawVMDInfo()
        {
            EditorGUILayout.LabelField("VMD Info", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Model Name: {_vmd.ModelName}");
                EditorGUILayout.LabelField($"Total Frames: {_vmd.MaxFrameNumber}");
                EditorGUILayout.LabelField($"Duration: {_vmd.MaxFrameNumber / 30f:F1} sec");
                EditorGUILayout.LabelField($"Bone Tracks: {_vmd.BoneNames.Count()}");
                EditorGUILayout.LabelField($"Morph Tracks: {_vmd.MorphNames.Count()}");

                // マッチング情報
                if (Model != null)
                {
                    var report = _applier.DiagnoseMatching(_vmd);
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField($"Matched Bones: {report.MatchedBones.Count}/{_vmd.BoneNames.Count()} ({report.BoneMatchRate:P0})");

                    if (report.UnmatchedVMDBones.Count > 0)
                    {
                        EditorGUILayout.LabelField($"Unmatched: {string.Join(", ", report.UnmatchedVMDBones.Take(5))}", EditorStyles.miniLabel);
                    }
                }
            }
        }

        private void DrawFrameControl()
        {
            EditorGUILayout.LabelField("Frame Control", EditorStyles.boldLabel);

            // フレーム番号表示
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Frame: {Mathf.RoundToInt(_currentFrame)}", GUILayout.Width(100));
                EditorGUILayout.LabelField($"/ {_vmd.MaxFrameNumber}", GUILayout.Width(80));

                string timeStr = $"{_currentFrame / 30f:F2}s";
                EditorGUILayout.LabelField(timeStr, EditorStyles.miniLabel, GUILayout.Width(60));
            }

            // スライダー
            EditorGUI.BeginChangeCheck();
            float newFrame = EditorGUILayout.Slider(_currentFrame, 0, _vmd.MaxFrameNumber);
            if (EditorGUI.EndChangeCheck())
            {
                _currentFrame = newFrame;
                ApplyFrame();
            }

            // ボタン
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("|◀"))
                {
                    _currentFrame = 0;
                    ApplyFrame();
                }
                if (GUILayout.Button("◀"))
                {
                    _currentFrame = Mathf.Max(0, _currentFrame - 1);
                    ApplyFrame();
                }
                if (GUILayout.Button("▶"))
                {
                    _currentFrame = Mathf.Min(_vmd.MaxFrameNumber, _currentFrame + 1);
                    ApplyFrame();
                }
                if (GUILayout.Button("▶|"))
                {
                    _currentFrame = _vmd.MaxFrameNumber;
                    ApplyFrame();
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Reset Pose", GUILayout.Width(80)))
                {
                    ResetPose();
                }
            }

            // 直接入力
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Go to:", GUILayout.Width(45));

                EditorGUI.BeginChangeCheck();
                int inputFrame = EditorGUILayout.IntField(Mathf.RoundToInt(_currentFrame), GUILayout.Width(60));
                if (EditorGUI.EndChangeCheck())
                {
                    _currentFrame = Mathf.Clamp(inputFrame, 0, _vmd.MaxFrameNumber);
                    ApplyFrame();
                }

                GUILayout.FlexibleSpace();

                // クイックジャンプ
                if (GUILayout.Button("0%", GUILayout.Width(35))) { _currentFrame = 0; ApplyFrame(); }
                if (GUILayout.Button("25%", GUILayout.Width(35))) { _currentFrame = _vmd.MaxFrameNumber * 0.25f; ApplyFrame(); }
                if (GUILayout.Button("50%", GUILayout.Width(35))) { _currentFrame = _vmd.MaxFrameNumber * 0.5f; ApplyFrame(); }
                if (GUILayout.Button("75%", GUILayout.Width(35))) { _currentFrame = _vmd.MaxFrameNumber * 0.75f; ApplyFrame(); }
                if (GUILayout.Button("100%", GUILayout.Width(40))) { _currentFrame = _vmd.MaxFrameNumber; ApplyFrame(); }
            }
        }

        private void DrawDataLists()
        {
            // オプション
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _applyCoordinateConversion = EditorGUILayout.Toggle("Coordinate Conversion (Z flip)", _applyCoordinateConversion);
            if (EditorGUI.EndChangeCheck())
            {
                _applier.ApplyCoordinateConversion = _applyCoordinateConversion;
                if (_vmd != null) ApplyFrame();
            }

            EditorGUI.BeginChangeCheck();
            float newScale = EditorGUILayout.FloatField("Position Scale", _applier.PositionScale);
            if (EditorGUI.EndChangeCheck())
            {
                _applier.PositionScale = newScale;
                if (_vmd != null) ApplyFrame();
            }

            EditorGUILayout.Space(8);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            {
                // ボーンリスト
                _showBoneList = EditorGUILayout.Foldout(_showBoneList, $"Bone Tracks ({_vmd.BoneNames.Count()})");
                if (_showBoneList)
                {
                    EditorGUI.indentLevel++;
                    foreach (var boneName in _vmd.BoneNames.Take(50))
                    {
                        var frames = _vmd.BoneFramesByName[boneName];
                        bool matched = Model != null && _applier.GetBoneIndex(boneName) >= 0;
                        string status = matched ? "✓" : "✗";
                        EditorGUILayout.LabelField($"{status} {boneName} ({frames.Count} keys)", EditorStyles.miniLabel);
                    }
                    if (_vmd.BoneNames.Count() > 50)
                    {
                        EditorGUILayout.LabelField($"... and {_vmd.BoneNames.Count() - 50} more", EditorStyles.miniLabel);
                    }
                    EditorGUI.indentLevel--;
                }

                // モーフリスト
                _showMorphList = EditorGUILayout.Foldout(_showMorphList, $"Morph Tracks ({_vmd.MorphNames.Count()})");
                if (_showMorphList)
                {
                    EditorGUI.indentLevel++;
                    foreach (var morphName in _vmd.MorphNames.Take(30))
                    {
                        var frames = _vmd.MorphFramesByName[morphName];
                        EditorGUILayout.LabelField($"  {morphName} ({frames.Count} keys)", EditorStyles.miniLabel);
                    }
                    if (_vmd.MorphNames.Count() > 30)
                    {
                        EditorGUILayout.LabelField($"... and {_vmd.MorphNames.Count() - 30} more", EditorStyles.miniLabel);
                    }
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OpenVMDFile()
        {
            string path = EditorUtility.OpenFilePanel("Open VMD", "", "vmd");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                _vmd = VMDData.LoadFromFile(path);
                _filePath = path;
                _currentFrame = 0;

                if (Model != null)
                {
                    _applier.BuildMapping(Model);
                    ApplyFrame();
                }

                Debug.Log($"[VMDTest] Loaded: {Path.GetFileName(path)}, {_vmd.MaxFrameNumber} frames");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"VMD読み込み失敗:\n{ex.Message}", "OK");
                Debug.LogError($"[VMDTest] Load failed: {ex}");
            }
        }

        private void ClearVMD()
        {
            ResetPose();
            _vmd = null;
            _filePath = null;
            _currentFrame = 0;
        }

        private void ReloadVMD()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            string path = _filePath;
            float frame = _currentFrame;
            ClearVMD();
            try
            {
                _vmd = VMDData.LoadFromFile(path);
                _filePath = path;
                _currentFrame = frame;
                if (Model != null)
                {
                    _applier.BuildMapping(Model);
                    ApplyFrame();
                }
                Debug.Log($"[VMDTest] Reloaded: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VMDTest] Reload failed: {ex}");
            }
        }

        private void ApplyFrame()
        {
            if (_vmd == null || Model == null) return;

            _applier.ApplyFrame(Model, _vmd, _currentFrame);

            // 再描画
            _context?.Repaint?.Invoke();
            SceneView.RepaintAll();
        }

        private void ResetPose()
        {
            if (Model == null) return;

            _applier.ResetAllBones(Model);

            _context?.Repaint?.Invoke();
            SceneView.RepaintAll();
        }
    }
}