// VMDPanel.cs
// VMDモーション制御用のUnity Editor UIパネル
// ファイル選択、再生制御、フレームスライダーを提供

using System;
using System.Collections.Generic;
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

        // 座標系設定
        private static float _positionScale = 0.085f;
        private static bool _flipZ = true;
        private static bool _showCoordinateSettings = false;

        // Humanoidクリップエクスポート用
        private static GameObject _exportRootObject;
        private static GameObject[] _exportCreatedObjects;

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

                        // 座標系設定
                        DrawCoordinateSettings();

                        EditorGUILayout.Space(8);

                        // 再生コントロール
                        DrawPlaybackControls();

                        EditorGUILayout.Space(8);

                        // タイムライン
                        DrawTimeline();

                        EditorGUILayout.Space(8);

                        // マッチング情報
                        DrawMatchingInfo();

                        EditorGUILayout.Space(8);

                        // IKベイク
                        DrawIKBakeButton(model);

                        EditorGUILayout.Space(8);

                        // Humanoidクリップエクスポート
                        DrawHumanoidClipExport(model);
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

        private static void DrawCoordinateSettings()
        {
            _showCoordinateSettings = EditorGUILayout.Foldout(_showCoordinateSettings, "Coordinate Settings");

            if (_showCoordinateSettings)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    _positionScale = EditorGUILayout.FloatField("Position Scale", _positionScale);
                    _flipZ = EditorGUILayout.Toggle("Flip Z", _flipZ);

                    // Applierに反映
                    if (_player?.Applier != null)
                    {
                        _player.Applier.PositionScale = _positionScale;
                        _player.Applier.ApplyCoordinateConversion = _flipZ;
                    }
                }
                EditorGUILayout.EndVertical();
            }
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

        private static void DrawIKBakeButton(Model.ModelContext model)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("IK Bake", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "IKキーフレームを解決し、リンクボーンの回転キーフレームに変換します。\n" +
                    "IKボーン（足IK等）のキーフレームは削除されます。",
                    MessageType.Info);

                EditorGUI.BeginDisabledGroup(_currentVMD == null || model == null);
                if (GUILayout.Button("IKをベイクする", GUILayout.Height(28)))
                {
                    if (EditorUtility.DisplayDialog(
                        "IKベイク確認",
                        "VMDデータのIKキーフレームをボーンキーフレームに変換します。\n" +
                        "この操作は元に戻せません。続行しますか？",
                        "ベイクする", "キャンセル"))
                    {
                        ExecuteIKBake(model);
                    }
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndVertical();
        }

        private static void ExecuteIKBake(Model.ModelContext model)
        {
            if (_currentVMD == null || model == null || _player?.Applier == null)
                return;

            try
            {
                var bakedNames = VMDIKBaker.BakeIK(_currentVMD, model, _player.Applier);

                if (bakedNames.Count > 0)
                {
                    // Applierを再適用して表示を更新
                    _player.Applier.EnableIK = false;
                    _player.Applier.ApplyFrame(model, _currentVMD, _player.CurrentFrame);

                    // マッチングレポートを更新
                    _matchingReport = _player.GetMatchingReport();

                    EditorUtility.DisplayDialog(
                        "IKベイク完了",
                        $"以下のIKボーンをベイクしました:\n{string.Join("\n", bakedNames)}",
                        "OK");

                    SceneView.RepaintAll();
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "IKベイク",
                        "ベイク対象のIKボーンが見つかりませんでした。",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "IKベイクエラー",
                    $"ベイク中にエラーが発生しました:\n{ex.Message}",
                    "OK");
                Debug.LogError($"[VMDPanel] IK Bake failed: {ex}");
            }
        }

        private static void DrawHumanoidClipExport(Model.ModelContext model)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("Humanoid Clip Export", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "エクスポート済みGameObject（Animator+Humanoid Avatar付き）を指定し、\n" +
                    "VMDモーションをHumanoid AnimationClipに変換します。\n" +
                    "IKベイクを先に実行してください。",
                    MessageType.Info);

                _exportRootObject = (GameObject)EditorGUILayout.ObjectField(
                    "Root Object", _exportRootObject, typeof(GameObject), true);

                EditorGUI.BeginDisabledGroup(
                    _currentVMD == null || model == null || _exportRootObject == null);
                if (GUILayout.Button("Humanoid Clipにエクスポート", GUILayout.Height(28)))
                {
                    ExecuteHumanoidClipExport(model);
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndVertical();
        }

        private static void ExecuteHumanoidClipExport(Model.ModelContext model)
        {
            if (_currentVMD == null || model == null || _exportRootObject == null || _player?.Applier == null)
                return;

            // Animator/Avatar検証
            var animator = _exportRootObject.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                EditorUtility.DisplayDialog(
                    "エラー",
                    "指定されたGameObjectにHumanoid Avatarが設定されたAnimatorがありません。",
                    "OK");
                return;
            }

            // createdObjectsを構築（rootの子孫からMeshContextList名で検索）
            var createdObjects = BuildCreatedObjectsFromHierarchy(model, _exportRootObject);

            // 保存先を選択
            string defaultName = !string.IsNullOrEmpty(_currentVMD.ModelName)
                ? _currentVMD.ModelName : "VMDMotion";
            string savePath = EditorUtility.SaveFilePanelInProject(
                "Save Humanoid Animation Clip",
                defaultName + ".anim",
                "anim",
                "AnimationClipの保存先を選択");

            if (string.IsNullOrEmpty(savePath))
                return;

            try
            {
                var clip = VMDToHumanoidClip.Convert(
                    _currentVMD, model, _player.Applier,
                    _exportRootObject, createdObjects);

                if (clip != null)
                {
                    clip.name = System.IO.Path.GetFileNameWithoutExtension(savePath);
                    VMDToHumanoidClip.SaveClip(clip, savePath);

                    EditorUtility.DisplayDialog(
                        "エクスポート完了",
                        $"Humanoid AnimationClipを保存しました:\n{savePath}",
                        "OK");

                    // 保存したクリップを選択
                    var savedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(savePath);
                    if (savedClip != null)
                    {
                        EditorGUIUtility.PingObject(savedClip);
                        UnityEditor.Selection.activeObject = savedClip;
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "エクスポート失敗",
                        "AnimationClipの生成に失敗しました。Consoleログを確認してください。",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "エクスポートエラー",
                    $"エクスポート中にエラーが発生しました:\n{ex.Message}",
                    "OK");
                Debug.LogError($"[VMDPanel] Humanoid Clip export failed: {ex}");
            }
        }

        /// <summary>
        /// エクスポート済みGameObjectの階層から、MeshContextListの各インデックスに
        /// 対応するGameObjectを名前検索で構築する。
        /// </summary>
        private static GameObject[] BuildCreatedObjectsFromHierarchy(
            Model.ModelContext model, GameObject root)
        {
            var result = new GameObject[model.MeshContextList.Count];

            // root以下の全Transformを名前→GameObjectの辞書にする
            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            var nameToGO = new Dictionary<string, GameObject>();
            foreach (var t in allTransforms)
            {
                // 同名がある場合は最初のものを使用
                if (!nameToGO.ContainsKey(t.name))
                    nameToGO[t.name] = t.gameObject;
            }

            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx == null || string.IsNullOrEmpty(ctx.Name))
                    continue;

                if (nameToGO.TryGetValue(ctx.Name, out var go))
                    result[i] = go;
            }

            return result;
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

        /// <summary>
        /// 座標系設定を更新（EditorStateContextから呼び出す）
        /// </summary>
        public static void SetCoordinateSettings(float scale, bool flipZ)
        {
            _positionScale = scale;
            _flipZ = flipZ;
            if (_player?.Applier != null)
            {
                _player.Applier.PositionScale = scale;
                _player.Applier.ApplyCoordinateConversion = flipZ;
            }
        }
    }
}
