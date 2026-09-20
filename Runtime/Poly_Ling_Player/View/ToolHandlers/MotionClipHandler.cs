// MotionClipHandler.cs
// モーションクリップの試し再生（クリップの読み込み・ボーンとの対応付け・フレーム適用・姿勢の初期化）を
// 受け持つ（操作経路統一計画.md E）。
//
// PlayerMotionClipTestSubPanel が MotionClipDTO と MotionClipApplier を持ち、ModelContext へ直接
// ポーズを当てていたものを移した。当てるのは再生用の姿勢（保存しない表示状態）で、Undo は持たない
// （従来と同じ）。パネルは時刻・再生位置の UI だけを持ち、setTime で頼む。

using System;
using System.Collections.Generic;
using System.IO;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Motion;
using Poly_Ling.UnityClip;
using Poly_Ling.VMD;

namespace Poly_Ling.Player
{
    [PLTool("motionClip", Description = "モーションクリップの試し再生（読み込み・対応付け・フレーム適用）")]
    public sealed class MotionClipHandler
    {
        public Func<ModelContext> GetModel;
        /// <summary>PMX 単位から Unity 単位への倍率（EditorState.PmxUnityRatio）。VMD の位置の既定倍率。</summary>
        public Func<float>        GetPmxUnityRatio;
        /// <summary>表情適用で WorkingPositions を変えた基準メッシュの表示を更新させる。</summary>
        public Action<MeshContext> SyncMeshPositions;
        /// <summary>フレームを当てた・姿勢を戻した後に呼ぶ（表示の更新）。</summary>
        public Action             OnFrameApplied;

        private MotionClipDTO        _dto;
        private MotionClipApplier    _applier;
        private MotionClipLoadResult _loadResult;
        private int                  _bindBoneCount;

        public bool HasClip => _dto != null;

        [PLToolState(Description = "クリップを読み込んでいるか")]
        public bool   ClipLoaded => _dto != null;
        [PLToolState(Description = "直近の load・loadBind の失敗理由（成功なら空）")]
        public string Error { get; private set; } = "";
        [PLToolState(Description = "クリップ名")]
        public string ClipName => _dto?.name ?? "";
        [PLToolState(Description = "クリップの長さ（秒。MotionClipSerializer.Length）")]
        public float  ClipLength => _dto != null ? MotionClipSerializer.Length(_dto) : 0f;
        [PLToolState(Description = "フレームレート（未指定は 30）")]
        public float  FrameRate => _dto != null && _dto.frameRate > 0f ? _dto.frameRate : 30f;
        [PLToolState(Description = "ボーン・焼き込みボーン・マッスル・表情のトラック数（この順）")]
        public int[]  TrackCounts => _dto == null ? Array.Empty<int>() : new[]
        {
            _dto.bones?.Count ?? 0, _dto.bakedBones?.Count ?? 0, _dto.muscles?.Count ?? 0, _dto.expressions?.Count ?? 0,
        };
        [PLToolState(Description = "ボーンと対応が取れたトラック数")]
        public int    MatchedTrackCount => _applier?.MatchedTrackCount ?? 0;
        [PLToolState(Description = "位置に掛ける倍率")]
        public float  PositionScale => _applier?.PositionScale ?? 1f;
        [PLToolState(Description = "読み込み検査と対応付けの報告の文")]
        public string ReportText
        {
            get
            {
                string text = "";
                if (_loadResult != null && _loadResult.Issues.Count > 0)
                    text = $"検査: エラー {_loadResult.ErrorCount} / 警告 {_loadResult.WarningCount}\n{_loadResult.FormatIssues(8)}\n";
                if (_applier != null) text += _applier.BindingReport.ToText(8);
                return text;
            }
        }
        [PLToolState(Description = "トラックの一覧（先頭 50 件。✓ 対応あり / ⚠ 競合 / ✗ 対応なし）")]
        public string[] TrackLines => BuildTrackLines(out _);
        [PLToolState(Description = "トラックの一覧の各行が対応ありか（TrackLines と同じ並び）")]
        public bool[]   TrackMatched { get { BuildTrackLines(out var m); return m; } }
        [PLToolState(Description = "ボーン・焼き込みボーンのトラックの総数")]
        public int      TrackTotal => (_dto?.bones?.Count ?? 0) + (_dto?.bakedBones?.Count ?? 0);
        [PLToolState(Description = "直近の loadBind で読んだボーン数")]
        public int      BindBoneCount => _bindBoneCount;

        /// <summary>
        /// クリップを読み込み、対応付けて time のフレームを当てる。
        /// sourceKind: 0 = VMD、1 = UnityClip JSON、2 = 統合 JSON。位置の倍率は種類の既定値にする
        /// （VMD は PmxUnityRatio。他は 1。統合 JSON は生成元の単位系を DTO から判別できないため 1）。
        /// </summary>
        [PLToolAction(Description = "クリップを読み込み、対応付けて time 秒のフレームを当てる（sourceKind: 0=VMD / 1=UnityClip JSON / 2=統合 JSON）")]
        public void Load(string path, int sourceKind, float time)
        {
            Error = "";
            if (string.IsNullOrEmpty(path)) { Error = "ファイルパスを指定してください"; return; }
            if (!File.Exists(path))        { Error = $"ファイルが見つかりません: {Path.GetFileName(path)}"; return; }
            try
            {
                _loadResult = null;
                MotionClipDTO dto;
                switch (sourceKind)
                {
                    case 0:  dto = MotionClipConverters.FromVMD(VMDData.LoadFromFile(path)); break;
                    case 1:  dto = MotionClipConverters.FromUnityClipDTO(UnityClipSerializer.LoadJson(path)); break;
                    default:
                        _loadResult = MotionClipSerializer.Load(path);
                        if (_loadResult.Dto == null) throw new InvalidDataException(_loadResult.FormatIssues(8));
                        dto = _loadResult.Dto;
                        break;
                }
                if (dto == null) { Error = "読込み結果が空です"; return; }

                _dto = dto;
                EnsureApplier();
                _applier.PositionScale = sourceKind == 0 ? (GetPmxUnityRatio?.Invoke() ?? 0.1f) : 1f;
                _applier.SetClip(_dto);
                var model = GetModel?.Invoke();
                if (model != null) { _applier.BuildMapping(model); ApplyFrameCore(time); }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                UnityEngine.Debug.LogError($"[MotionClipHandler] {ex}");
            }
        }

        [PLToolAction(Description = "姿勢を初期へ戻し、クリップを捨てる")]
        public void Clear()
        {
            ResetPoseCore();
            _dto = null;
            _loadResult = null;
        }

        [PLToolAction(Description = "姿勢を初期へ戻す（クリップは残す）")]
        public void ResetPose() => ResetPoseCore();

        [PLToolAction(Description = "time 秒のフレームを当てる")]
        public void SetTime(float time) => ApplyFrameCore(time);

        [PLToolAction(Description = "位置に掛ける倍率を変え、time 秒のフレームを当て直す")]
        public void SetPositionScale(float scale, float time)
        {
            EnsureApplier();
            _applier.PositionScale = scale;
            if (_dto != null) ApplyFrameCore(time);
        }

        /// <summary>外部 UnityBone CSV v2（ソースの rest）を読み、リターゲット経路を有効にする。</summary>
        [PLToolAction(Description = "外部 UnityBone CSV（バインドポーズ）を読み、対応付けし直して time 秒のフレームを当てる")]
        public void LoadBind(string path, float time)
        {
            Error = "";
            _bindBoneCount = 0;
            if (string.IsNullOrEmpty(path)) { Error = "ファイルパスを指定してください"; return; }
            if (!File.Exists(path))        { Error = $"ファイルが見つかりません: {Path.GetFileName(path)}"; return; }
            try
            {
                string text = File.ReadAllText(path);
                EnsureApplier();
                _bindBoneCount = _applier.LoadSourceRestCsv(text);
                var model = GetModel?.Invoke();
                if (model != null) _applier.BuildMapping(model);
                if (_dto != null) ApplyFrameCore(time);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                UnityEngine.Debug.LogError($"[MotionClipHandler] {ex}");
            }
        }

        private void EnsureApplier()
        {
            if (_applier == null) _applier = new MotionClipApplier();
            _applier.SyncMeshPositions = SyncMeshPositions;
        }

        private void ApplyFrameCore(float time)
        {
            var model = GetModel?.Invoke();
            if (_dto == null || model == null || _applier == null) return;
            _applier.ApplyFrame(model, time);
            OnFrameApplied?.Invoke();
        }

        private void ResetPoseCore()
        {
            var model = GetModel?.Invoke();
            if (model == null || _applier == null) return;
            _applier.ResetAllBones(model);
            OnFrameApplied?.Invoke();
        }

        private string[] BuildTrackLines(out bool[] matchedFlags)
        {
            var lines = new List<string>();
            var flags = new List<bool>();
            if (_dto != null)
            {
                var all = new List<MotionTrackDTO>();
                if (_dto.bones != null)      all.AddRange(_dto.bones);
                if (_dto.bakedBones != null) all.AddRange(_dto.bakedBones);
                bool hasModel = GetModel?.Invoke() != null;
                int n = 0;
                foreach (var track in all)
                {
                    if (n >= 50) break;
                    n++;
                    if (track == null) continue;
                    bool matched    = hasModel && _applier != null && _applier.IsTrackMatched(track);
                    bool conflicted = _applier != null && _applier.IsTrackConflicted(track);
                    int keys = track.keys?.Count ?? 0;
                    lines.Add($"{(matched ? "✓" : conflicted ? "⚠" : "✗")} [{track.targetKind}] {track.id} ({keys} keys){(conflicted ? " 競合" : "")}");
                    flags.Add(matched);
                }
            }
            matchedFlags = flags.ToArray();
            return lines.ToArray();
        }
    }
}
