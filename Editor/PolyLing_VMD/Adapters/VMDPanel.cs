// VMDPanel.cs
// VMDモーション制御用のUnity Editor UIパネル
// ファイル選択、再生制御、フレームスライダーを提供

using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// VMDモーション制御パネル（エディタUI）
    /// </summary>
    public static class VMDPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private static VMDPlayer _player;
        private static VMDData _currentVMD;
        private static string _currentFilePath;
        private static bool _isExpanded = true;
        private static Vector2 _scrollPosition;

        // マッチング情報表示
        private static bool _showMatchingInfo = false;
        private static VMDMatchingReport _matchingReport;

        // ================================================================
        // 初期化
        // ================================================================

        /// <summary>
        /// パネル初期化
        /// </summary>
        public static void Initialize()
        {
            if (_player == null)
            {
                _player = new VMDPlayer();
                _player.OnFrameChanged += OnFrameChanged;
                _player.OnStateChanged += OnStateChanged;
                _player.OnPlaybackFinished += OnPlaybackFinished;
            }
        }

        /// <summary>
        /// パネルクリーンアップ
        /// </summary>
        public static void Cleanup()
        {
            if (_player != null)
            {
                _player.Stop();
                _player.OnFrameChanged -= OnFrameChanged;
                _player.OnStateChanged -= OnStateChanged;
                _player.OnPlaybackFinished -= OnPlaybackFinished;
            }
        }

        // ================================================================
        // UI描画
        // ================================================================

        /// <summary>
        /// メインUI描画
        /// </summary>
        public static void DrawUI(Model.ModelContext model)
        {
            Initialize();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                // ヘッダ
                _isExpanded = EditorGUILayout.Foldout(_isExpanded, "VMD Motion", true, EditorStyles.foldoutHeader);

                if (_isExpanded)
                {
                    EditorGUILayout.Space(4);

                    // ファイル選択
                    DrawFileSelector(model);

                    if (_currentVMD != null)
                    {
                        EditorGUILayout.Space(8);

                        // 情報表示
                        DrawVMDInfo();

                        EditorGUILayout.Space(8);

                        // 再生コントロール
                        DrawPlaybackControls();

                        EditorGUILayout.Space(8);

                        // タイムライン
                        DrawTimeline();

                        EditorGUILayout.Space(8);

                        // マッチング情報
                        DrawMatchingInfo();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// コンパクトUI描画（1行版）
        /// </summary>
        public static void DrawCompactUI(Model.ModelContext model)
        {
            Initialize();

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label("VMD:", GUILayout.Width(35));

                if (_currentVMD != null)
                {
                    // 再生/停止ボタン
                    string playLabel = _player.IsPlaying ? "■" : "▶";
                    if (GUILayout.Button(playLabel, GUILayout.Width(25)))
                    {
                        if (_player.IsPlaying)
                            _player.Stop();
                        else
                            _player.Play();
                    }

                    // フレーム表示
                    GUILayout.Label($"{_player.CurrentFrameInt}/{_player.MaxFrame}", 
                        GUILayout.Width(80));

                    // スライダー
                    float newFrame = GUILayout.HorizontalSlider(
                        _player.CurrentFrame, 0, _player.MaxFrame);
                    if (Mathf.Abs(newFrame - _player.CurrentFrame) > 0.5f)
                    {
                        _player.SeekToFrame(newFrame);
                    }
                }
                else
                {
                    if (GUILayout.Button("Load VMD", GUILayout.Width(80)))
                    {
                        LoadVMDFile(model);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // UI部品
        // ================================================================

        private static void DrawFileSelector(Model.ModelContext model)
        {
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("File:", GUILayout.Width(40));

                string displayPath = string.IsNullOrEmpty(_currentFilePath) 
                    ? "(None)" 
                    : Path.GetFileName(_currentFilePath);
                EditorGUILayout.SelectableLabel(displayPath, EditorStyles.textField, 
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    LoadVMDFile(model);
                }

                EditorGUI.BeginDisabledGroup(_currentVMD == null);
                if (GUILayout.Button("✕", GUILayout.Width(25)))
                {
                    UnloadVMD();
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawVMDInfo()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("VMD Info", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Model: {_currentVMD.ModelName}");
                EditorGUILayout.LabelField($"Frames: {_currentVMD.MaxFrameNumber}");
                EditorGUILayout.LabelField($"Duration: {VMDPlayer.FrameToTimeString(_currentVMD.MaxFrameNumber)}");
                EditorGUILayout.LabelField($"Bones: {_currentVMD.BoneNames.Count()}");
                EditorGUILayout.LabelField($"Morphs: {_currentVMD.MorphNames.Count()}");
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawPlaybackControls()
        {
            EditorGUILayout.BeginHorizontal();
            {
                // 先頭へ
                if (GUILayout.Button("|◀", GUILayout.Width(35)))
                {
                    _player.GoToStart();
                }

                // 前フレーム
                if (GUILayout.Button("◀", GUILayout.Width(35)))
                {
                    _player.PreviousFrame();
                }

                // 再生/一時停止
                string playPauseLabel = _player.IsPlaying ? "⏸" : "▶";
                if (GUILayout.Button(playPauseLabel, GUILayout.Width(40)))
                {
                    _player.TogglePlayPause();
                }

                // 停止
                if (GUILayout.Button("■", GUILayout.Width(35)))
                {
                    _player.Stop();
                }

                // 次フレーム
                if (GUILayout.Button("▶", GUILayout.Width(35)))
                {
                    _player.NextFrame();
                }

                // 末尾へ
                if (GUILayout.Button("▶|", GUILayout.Width(35)))
                {
                    _player.GoToEnd();
                }

                GUILayout.FlexibleSpace();

                // ループ
                _player.Loop = GUILayout.Toggle(_player.Loop, "Loop", GUILayout.Width(50));
            }
            EditorGUILayout.EndHorizontal();

            // 再生速度
            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label("Speed:", GUILayout.Width(45));
                _player.PlaybackSpeed = EditorGUILayout.Slider(_player.PlaybackSpeed, 0.1f, 2.0f);
                if (GUILayout.Button("1x", GUILayout.Width(30)))
                {
                    _player.PlaybackSpeed = 1.0f;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawTimeline()
        {
            // 時間表示
            EditorGUILayout.BeginHorizontal();
            {
                string currentTime = VMDPlayer.FrameToTimeString(_player.CurrentFrame);
                string totalTime = VMDPlayer.FrameToTimeString(_player.MaxFrame);
                GUILayout.Label($"{currentTime} / {totalTime}", EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();

                GUILayout.Label($"Frame: {_player.CurrentFrameInt}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            // フレームスライダー
            EditorGUILayout.BeginHorizontal();
            {
                float newFrame = GUILayout.HorizontalSlider(
                    _player.CurrentFrame, 0, _player.MaxFrame);

                if (Mathf.Abs(newFrame - _player.CurrentFrame) > 0.5f)
                {
                    _player.SeekToFrame(newFrame);
                }
            }
            EditorGUILayout.EndHorizontal();

            // フレーム直接入力
            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label("Go to:", GUILayout.Width(45));
                int inputFrame = EditorGUILayout.IntField(_player.CurrentFrameInt, GUILayout.Width(60));
                if (inputFrame != _player.CurrentFrameInt)
                {
                    _player.SeekToFrame(inputFrame);
                }

                GUILayout.FlexibleSpace();

                // クイックジャンプ
                if (GUILayout.Button("0%", GUILayout.Width(35)))
                    _player.SeekToFrame(0);
                if (GUILayout.Button("25%", GUILayout.Width(35)))
                    _player.SeekToFrame(_player.MaxFrame * 0.25f);
                if (GUILayout.Button("50%", GUILayout.Width(35)))
                    _player.SeekToFrame(_player.MaxFrame * 0.5f);
                if (GUILayout.Button("75%", GUILayout.Width(35)))
                    _player.SeekToFrame(_player.MaxFrame * 0.75f);
                if (GUILayout.Button("100%", GUILayout.Width(40)))
                    _player.SeekToFrame(_player.MaxFrame);
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawMatchingInfo()
        {
            _showMatchingInfo = EditorGUILayout.Foldout(_showMatchingInfo, "Bone/Morph Matching");

            if (_showMatchingInfo && _matchingReport != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    // サマリー
                    EditorGUILayout.LabelField(
                        $"Bones: {_matchingReport.MatchedBones.Count} matched / " +
                        $"{_matchingReport.UnmatchedVMDBones.Count} unmatched " +
                        $"({_matchingReport.BoneMatchRate:P0})");

                    EditorGUILayout.LabelField(
                        $"Morphs: {_matchingReport.MatchedMorphs.Count} matched / " +
                        $"{_matchingReport.UnmatchedVMDMorphs.Count} unmatched " +
                        $"({_matchingReport.MorphMatchRate:P0})");

                    // 未マッチリスト
                    if (_matchingReport.UnmatchedVMDBones.Count > 0)
                    {
                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField("Unmatched VMD Bones:", EditorStyles.boldLabel);

                        _scrollPosition = EditorGUILayout.BeginScrollView(
                            _scrollPosition, GUILayout.MaxHeight(100));
                        {
                            foreach (var bone in _matchingReport.UnmatchedVMDBones)
                            {
                                EditorGUILayout.LabelField($"  • {bone}", EditorStyles.miniLabel);
                            }
                        }
                        EditorGUILayout.EndScrollView();
                    }
                }
                EditorGUILayout.EndVertical();
            }
        }

        // ================================================================
        // ファイル操作
        // ================================================================

        private static void LoadVMDFile(Model.ModelContext model)
        {
            string path = EditorUtility.OpenFilePanel("Open VMD File", "", "vmd");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                _currentVMD = VMDData.LoadFromFile(path);
                _currentFilePath = path;

                if (model != null)
                {
                    _player.Load(_currentVMD, model);
                    _matchingReport = _player.GetMatchingReport();
                }

                Debug.Log($"[VMDPanel] Loaded: {path}");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to load VMD file:\n{ex.Message}", "OK");
                Debug.LogError($"[VMDPanel] Load failed: {ex}");
            }
        }

        private static void UnloadVMD()
        {
            _player.Stop();
            _currentVMD = null;
            _currentFilePath = null;
            _matchingReport = null;
        }

        // ================================================================
        // イベントハンドラ
        // ================================================================

        private static void OnFrameChanged(float frame)
        {
            // 必要に応じてシーンビューを再描画
            SceneView.RepaintAll();
        }

        private static void OnStateChanged(VMDPlayer.PlayState state)
        {
            // 再生開始時にEditorUpdateを登録
            if (state == VMDPlayer.PlayState.Playing)
            {
                EditorApplication.update += _player.Update;
            }
            else
            {
                EditorApplication.update -= _player.Update;
            }
        }

        private static void OnPlaybackFinished()
        {
            Debug.Log("[VMDPanel] Playback finished");
        }

        // ================================================================
        // 外部アクセス
        // ================================================================

        /// <summary>
        /// 現在のVMDデータを取得
        /// </summary>
        public static VMDData CurrentVMD => _currentVMD;

        /// <summary>
        /// プレイヤーを取得
        /// </summary>
        public static VMDPlayer Player => _player;

        /// <summary>
        /// VMDがロードされているか
        /// </summary>
        public static bool HasVMD => _currentVMD != null;

        /// <summary>
        /// 外部からVMDをセット
        /// </summary>
        public static void SetVMD(VMDData vmd, Model.ModelContext model, string filePath = null)
        {
            Initialize();
            _currentVMD = vmd;
            _currentFilePath = filePath;

            if (model != null && vmd != null)
            {
                _player.Load(vmd, model);
                _matchingReport = _player.GetMatchingReport();
            }
        }
    }
}
