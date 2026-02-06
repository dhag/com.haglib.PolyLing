// VMDPlayer.cs
// VMDモーションのアニメーション再生コントローラ
// 再生/停止/シーク機能を提供

using System;
using UnityEngine;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// VMDモーション再生コントローラ
    /// </summary>
    public class VMDPlayer
    {
        // ================================================================
        // 状態
        // ================================================================

        /// <summary>再生状態</summary>
        public enum PlayState
        {
            Stopped,
            Playing,
            Paused
        }

        /// <summary>現在の再生状態</summary>
        public PlayState State { get; private set; } = PlayState.Stopped;

        /// <summary>現在のフレーム番号（浮動小数点）</summary>
        public float CurrentFrame { get; private set; } = 0f;

        /// <summary>現在のフレーム番号（整数）</summary>
        public int CurrentFrameInt => Mathf.RoundToInt(CurrentFrame);

        /// <summary>再生速度（1.0 = 通常速度）</summary>
        public float PlaybackSpeed { get; set; } = 1.0f;

        /// <summary>ループ再生</summary>
        public bool Loop { get; set; } = true;

        /// <summary>フレームレート（30fps標準）</summary>
        public float FrameRate { get; set; } = 30f;

        /// <summary>最大フレーム番号</summary>
        public uint MaxFrame { get; private set; } = 0;

        /// <summary>再生中かどうか</summary>
        public bool IsPlaying => State == PlayState.Playing;

        /// <summary>一時停止中かどうか</summary>
        public bool IsPaused => State == PlayState.Paused;

        /// <summary>停止中かどうか</summary>
        public bool IsStopped => State == PlayState.Stopped;

        // ================================================================
        // 参照
        // ================================================================

        /// <summary>適用対象のModelContext</summary>
        public Model.ModelContext TargetModel { get; private set; }

        /// <summary>再生中のVMDデータ</summary>
        public VMDData CurrentVMD { get; private set; }

        /// <summary>VMDアプライヤ</summary>
        private VMDApplier _applier;

        /// <summary>最後の更新時刻</summary>
        private double _lastUpdateTime;

        // ================================================================
        // イベント
        // ================================================================

        /// <summary>フレーム更新時</summary>
        public event Action<float> OnFrameChanged;

        /// <summary>再生状態変更時</summary>
        public event Action<PlayState> OnStateChanged;

        /// <summary>再生終了時（ループしない場合）</summary>
        public event Action OnPlaybackFinished;

        // ================================================================
        // 初期化
        // ================================================================

        public VMDPlayer()
        {
            _applier = new VMDApplier();
        }

        /// <summary>
        /// VMDをロードして再生準備
        /// </summary>
        public void Load(VMDData vmd, Model.ModelContext model)
        {
            if (vmd == null || model == null) return;

            Stop();

            CurrentVMD = vmd;
            TargetModel = model;
            MaxFrame = vmd.MaxFrameNumber;
            CurrentFrame = 0;

            _applier.BuildMapping(model);

            // 初期フレームを適用
            ApplyCurrentFrame();

            Debug.Log($"[VMDPlayer] Loaded: {vmd.ModelName}, {MaxFrame} frames, " +
                     $"{_applier.MappedBoneCount} bones mapped");
        }

        /// <summary>
        /// VMDファイルをロード
        /// </summary>
        public void LoadFromFile(string path, Model.ModelContext model)
        {
            var vmd = VMDData.LoadFromFile(path);
            Load(vmd, model);
        }

        // ================================================================
        // 再生制御
        // ================================================================

        /// <summary>
        /// 再生開始
        /// </summary>
        public void Play()
        {
            if (CurrentVMD == null || TargetModel == null) return;

            State = PlayState.Playing;
            _lastUpdateTime = GetCurrentTime();

            OnStateChanged?.Invoke(State);
        }

        /// <summary>
        /// 一時停止
        /// </summary>
        public void Pause()
        {
            if (State == PlayState.Playing)
            {
                State = PlayState.Paused;
                OnStateChanged?.Invoke(State);
            }
        }

        /// <summary>
        /// 停止（先頭に戻る）
        /// </summary>
        public void Stop()
        {
            State = PlayState.Stopped;
            CurrentFrame = 0;

            if (CurrentVMD != null && TargetModel != null)
            {
                ApplyCurrentFrame();
            }

            OnStateChanged?.Invoke(State);
        }

        /// <summary>
        /// 再生/一時停止トグル
        /// </summary>
        public void TogglePlayPause()
        {
            if (IsPlaying)
                Pause();
            else
                Play();
        }

        // ================================================================
        // シーク
        // ================================================================

        /// <summary>
        /// 指定フレームにシーク
        /// </summary>
        public void SeekToFrame(float frame)
        {
            CurrentFrame = Mathf.Clamp(frame, 0, MaxFrame);
            ApplyCurrentFrame();
            OnFrameChanged?.Invoke(CurrentFrame);
        }

        /// <summary>
        /// 指定時間（秒）にシーク
        /// </summary>
        public void SeekToTime(float seconds)
        {
            SeekToFrame(seconds * FrameRate);
        }

        /// <summary>
        /// 相対フレーム移動
        /// </summary>
        public void StepFrames(int frames)
        {
            SeekToFrame(CurrentFrame + frames);
        }

        /// <summary>
        /// 次のフレームへ
        /// </summary>
        public void NextFrame() => StepFrames(1);

        /// <summary>
        /// 前のフレームへ
        /// </summary>
        public void PreviousFrame() => StepFrames(-1);

        /// <summary>
        /// 先頭へ
        /// </summary>
        public void GoToStart() => SeekToFrame(0);

        /// <summary>
        /// 末尾へ
        /// </summary>
        public void GoToEnd() => SeekToFrame(MaxFrame);

        // ================================================================
        // 更新
        // ================================================================

        /// <summary>
        /// 毎フレーム呼び出し（EditorApplication.updateなどから）
        /// </summary>
        public void Update()
        {
            if (State != PlayState.Playing) return;
            if (CurrentVMD == null || TargetModel == null) return;

            double currentTime = GetCurrentTime();
            double deltaTime = currentTime - _lastUpdateTime;
            _lastUpdateTime = currentTime;

            // フレーム進行
            float frameDelta = (float)(deltaTime * FrameRate * PlaybackSpeed);
            CurrentFrame += frameDelta;

            // 終端処理
            if (CurrentFrame >= MaxFrame)
            {
                if (Loop)
                {
                    CurrentFrame = CurrentFrame % MaxFrame;
                }
                else
                {
                    CurrentFrame = MaxFrame;
                    Pause();
                    OnPlaybackFinished?.Invoke();
                }
            }

            ApplyCurrentFrame();
            OnFrameChanged?.Invoke(CurrentFrame);
        }

        /// <summary>
        /// 現在のフレームをモデルに適用
        /// </summary>
        private void ApplyCurrentFrame()
        {
            if (CurrentVMD == null || TargetModel == null) return;
            _applier.ApplyFrame(TargetModel, CurrentVMD, CurrentFrame);
        }

        /// <summary>
        /// 現在時刻を取得（Unity Editor対応）
        /// </summary>
        private double GetCurrentTime()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.realtimeSinceStartupAsDouble;
#endif
        }

        // ================================================================
        // 情報取得
        // ================================================================

        /// <summary>
        /// 現在の再生時間（秒）
        /// </summary>
        public float CurrentTimeSeconds => CurrentFrame / FrameRate;

        /// <summary>
        /// 総再生時間（秒）
        /// </summary>
        public float TotalTimeSeconds => MaxFrame / FrameRate;

        /// <summary>
        /// 再生進捗（0-1）
        /// </summary>
        public float Progress => MaxFrame > 0 ? CurrentFrame / MaxFrame : 0f;

        /// <summary>
        /// マッチング診断
        /// </summary>
        public VMDMatchingReport GetMatchingReport()
        {
            if (CurrentVMD == null) return null;
            return _applier.DiagnoseMatching(CurrentVMD);
        }

        /// <summary>
        /// すべてのボーンをリセット
        /// </summary>
        public void ResetPose()
        {
            _applier.ResetAllBones(TargetModel);
        }

        // ================================================================
        // 静的ユーティリティ
        // ================================================================

        /// <summary>
        /// フレーム番号を時間文字列に変換（MM:SS:FF形式）
        /// </summary>
        public static string FrameToTimeString(float frame, float fps = 30f)
        {
            float totalSeconds = frame / fps;
            int minutes = (int)(totalSeconds / 60);
            int seconds = (int)(totalSeconds % 60);
            int frames = (int)(frame % fps);
            return $"{minutes:D2}:{seconds:D2}:{frames:D2}";
        }

        /// <summary>
        /// 時間文字列をフレーム番号に変換
        /// </summary>
        public static float TimeStringToFrame(string timeStr, float fps = 30f)
        {
            var parts = timeStr.Split(':');
            if (parts.Length != 3) return 0;

            if (int.TryParse(parts[0], out int minutes) &&
                int.TryParse(parts[1], out int seconds) &&
                int.TryParse(parts[2], out int frames))
            {
                return (minutes * 60 + seconds) * fps + frames;
            }
            return 0;
        }
    }
}
