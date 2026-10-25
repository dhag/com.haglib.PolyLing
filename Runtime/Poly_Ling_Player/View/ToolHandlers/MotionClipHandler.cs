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
        /// <summary>再生で time 秒のフレームを当てた後に呼ぶ（playFrame のときだけ。配信用）。</summary>
        public Action<float>      OnPlaybackFrame;

        private MotionClipDTO        _dto;
        private MotionClipApplier    _applier;
        private MotionClipLoadResult _loadResult;
        private int                  _limitBoneCount;

        public bool HasClip => _dto != null;

        [PLToolState(Description = "クリップを読み込んでいるか")]
        public bool   ClipLoaded => _dto != null;
        [PLToolState(Description = "直近の load・loadLimit の失敗理由（成功なら空）")]
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
        [PLToolState(Description = "ボーン（bones）トラックの一覧（先頭 50 件。✓ 対応あり / ⚠ 競合 / ✗ 対応なし）。bakedBones は再生に使わないので載せない")]
        public string[] TrackLines => BuildTrackLines(out _);
        [PLToolState(Description = "トラックの一覧の各行が対応ありか（TrackLines と同じ並び）")]
        public bool[]   TrackMatched { get { BuildTrackLines(out var m); return m; } }
        [PLToolState(Description = "ボーン（bones）トラックの総数")]
        public int      TrackTotal => _dto?.bones?.Count ?? 0;
        [PLToolState(Description = "直近の loadLimit で読んだボーン数（0 は未読込）")]
        public int      LimitBoneCount => _limitBoneCount;
        [PLToolState(Description = "読み込んだ可動域 CSV が実測列を持つか")]
        public bool     LimitMeasured => _applier != null && _applier.HasMuscleMeasured;
        [PLToolState(Description = "現在のモデルの Humanoid 割り当て数（VRMA 書き出しに要る）")]
        public int      HumanoidBoneCount => GetModel?.Invoke()?.HumanoidMapping?.Count ?? 0;

        /// <summary>
        /// クリップを読み込み、対応付けて time のフレームを当てる。
        /// sourceKind は MotionSourceKind の値：0 = VMD、1 = PolyLing モーション（.plmotion.json）、
        /// 2 = VRM アニメーション（.vrma）、3 = BVH（関節名の割り当て表は自動選択）、4 = FBX。
        /// FBX は fbxTake 番のテイクを読み、fbxRestPath（空でなければ）のバインドポーズをレストにする。
        /// 位置の倍率は種類の既定値にする（VMD は PmxUnityRatio。ほかは 1）。
        /// </summary>
        [PLToolAction(Description = "クリップを読み込み、対応付けて time 秒のフレームを当てる（sourceKind: 0=VMD / 1=PolyLing モーション / 2=VRM アニメーション / 3=BVH / 4=FBX）。FBX は fbxTake 番のテイクを読み、fbxRestPath が空でなければそのモデル FBX のバインドポーズをレストにする")]
        public void Load(string path, int sourceKind, float time, string fbxRestPath = "", int fbxTake = 0)
        {
            Error = "";
            if (string.IsNullOrEmpty(path)) { Error = "ファイルパスを指定してください"; return; }
            if (!File.Exists(path))        { Error = $"ファイルが見つかりません: {Path.GetFileName(path)}"; return; }
            if (!Enum.IsDefined(typeof(MotionSourceKind), sourceKind)) { Error = $"sourceKind が {sourceKind} です"; return; }
            Poly_Ling.Fbx.FbxMotionReadOptions fbx = null;
            if (sourceKind == (int)MotionSourceKind.Fbx)
            {
                if (!string.IsNullOrEmpty(fbxRestPath) && !File.Exists(fbxRestPath))
                { Error = $"レスト用の FBX が見つかりません: {Path.GetFileName(fbxRestPath)}"; return; }
                fbx = new Poly_Ling.Fbx.FbxMotionReadOptions
                {
                    TakeIndex   = fbxTake,
                    RestFbxPath = string.IsNullOrEmpty(fbxRestPath) ? null : fbxRestPath,
                };
            }
            try
            {
                _loadResult = null;
                var loaded = MotionFileLoad.Load(path, (MotionSourceKind)sourceKind, null, fbx);
                if (loaded.Dto == null) throw new InvalidDataException(loaded.FormatIssues(8));
                // VMD は検査を通らないので、報告に載せる検査結果は持たない（以前と同じ）。
                SetClipCore(loaded.Dto, sourceKind == (int)MotionSourceKind.Vmd ? null : loaded,
                            sourceKind == 0 ? (GetPmxUnityRatio?.Invoke() ?? 0.1f) : 1f, time);
                SourcePath = path;
                SourceKind = sourceKind;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                UnityEngine.Debug.LogError($"[MotionClipHandler] {ex}");
            }
        }

        /// <summary>
        /// PolyLing モーション JSON の文字列（.plmotion.json と同じ中身）を MotionClipSerializer.Parse で読み、
        /// 読み込んだクリップにして time 秒のフレームを当てる（Load の sourceKind=1 と同じ扱い。位置の倍率は 1）。
        /// 元ファイルは無いので SourcePath は空になる。読めなければ今のクリップはそのままで、Error に理由が入る。
        /// 成功したら OnClipLoadedFromJson を呼ぶ（パネルの表示を合わせる）。
        /// コマンド loadMotionClipJson（Blender ブリッジのモーション送信）が使う。
        /// </summary>
        public MotionClipLoadResult LoadJson(string json, float time)
        {
            Error = "";
            var loaded = MotionClipSerializer.Parse(json);
            if (loaded.Dto == null) { Error = loaded.FormatIssues(8); return loaded; }
            try
            {
                SetClipCore(loaded.Dto, loaded, 1f, time);
                SourcePath = "";
                SourceKind = (int)MotionSourceKind.PolyLingMotionJson;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                UnityEngine.Debug.LogError($"[MotionClipHandler] {ex}");
                return loaded;
            }
            OnClipLoadedFromJson?.Invoke(time);
            return loaded;
        }

        /// <summary>LoadJson で読み込んだ後に呼ぶ（引数は当てた時刻）。パネルの表示を合わせる。</summary>
        public Action<float> OnClipLoadedFromJson;

        /// <summary>読み込んでいるクリップ（無ければ null）。書き換えないこと。</summary>
        public MotionClipDTO Clip => _dto;

        // 【元ファイルの持ち主】読み込んだクリップの元ファイルとその種類はこの窓口だけが持つ。
        //   パネル（PlayerMotionClipTestSubPanel）は自分では持たず、この状態を読む
        //   （以前はパネルが別に持ち、窓口へ外から読み込んだクリップでは食い違った。PolyLing_Blender不具合修正計画.md の B）。
        [PLToolState(Description = "読み込んだクリップの元ファイル（load で読んだもの、または saveJson で書いたもの。JSON の文字列から読んで保存していないときと未読込は空）")]
        public string SourcePath { get; private set; } = "";

        [PLToolState(Description = "元ファイルの種類（MotionSourceKind の値：0=VMD / 1=PolyLing モーション / 2=VRM アニメーション / 3=BVH / 4=FBX）。JSON の文字列から読んだものは 1")]
        public int SourceKind { get; private set; } = 0;

        /// <summary>
        /// 読み込んでいるクリップを PolyLing モーション JSON（MotionClipSerializer.ToSaveJson。queryMotionClipJson と同じ中身）で
        /// path へ書き、そのファイルを元ファイルにする（SourcePath = path、SourceKind = PolyLing モーション）。
        /// path は関門を通した実際の経路を渡すこと。失敗したら false で、Error に理由が入る。
        /// コマンド saveMotionClipJson と、パネルの JSON 保存（元ファイルが無いクリップのとき）が使う。
        /// </summary>
        public bool SaveJson(string path)
        {
            Error = "";
            if (_dto == null) { Error = "クリップを読み込んでください"; return false; }
            if (string.IsNullOrEmpty(path)) { Error = "保存先を指定してください"; return false; }
            var r = MotionClipSerializer.ToSaveJson(_dto, out string json);
            if (json == null) { Error = r.FormatIssues(8); return false; }
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                // MotionClipSerializer.Save と同じく BOM なしの UTF-8
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                UnityEngine.Debug.LogError($"[MotionClipHandler] {ex}");
                return false;
            }
            SourcePath = path;
            SourceKind = (int)MotionSourceKind.PolyLingMotionJson;
            OnSourceChanged?.Invoke();
            return true;
        }

        /// <summary>SaveJson で元ファイルが変わった後に呼ぶ（パネルの表示を合わせる）。</summary>
        public Action OnSourceChanged;

        /// <summary>
        /// 読み込んでいるクリップを edit で書き換え、対応付けをやり直して time 秒のフレームを当てる。
        /// クリップが無ければ空のクリップ（newName）を作ってから書き換える（元ファイルは無し）。
        /// 書き換えの後 OnClipLoadedFromJson を呼ぶ（パネルの表示を合わせる）。
        /// コマンド createLipSyncFromVoicevox が使う。
        /// </summary>
        public void EditClip(Action<MotionClipDTO> edit, string newName, float time)
        {
            Error = "";
            bool created = false;
            var dto = _dto;
            if (dto == null)
            {
                dto = new MotionClipDTO
                {
                    name = newName ?? "",
                    metadata = new MotionMetadataDTO { createdWith = "PolyLing" },
                };
                created = true;
            }
            edit?.Invoke(dto);
            float scale = _applier?.PositionScale ?? 1f;
            try
            {
                SetClipCore(dto, _loadResult, scale, time);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                UnityEngine.Debug.LogError($"[MotionClipHandler] {ex}");
                return;
            }
            if (created)
            {
                SourcePath = "";
                SourceKind = (int)MotionSourceKind.PolyLingMotionJson;
            }
            OnClipLoadedFromJson?.Invoke(time);
        }

        private void SetClipCore(MotionClipDTO dto, MotionClipLoadResult report, float positionScale, float time)
        {
            _loadResult = report;
            _dto = dto;
            EnsureApplier();
            _applier.PositionScale = positionScale;
            _applier.SetClip(_dto);
            var model = GetModel?.Invoke();
            if (model != null) { _applier.BuildMapping(model); ApplyFrameCore(time); }
        }

        [PLToolAction(Description = "姿勢を初期へ戻し、クリップを捨てる")]
        public void Clear()
        {
            ResetPoseCore();
            _dto = null;
            _loadResult = null;
            SourcePath = "";
            SourceKind = 0;
        }

        [PLToolAction(Description = "姿勢を初期へ戻す（クリップは残す）")]
        public void ResetPose() => ResetPoseCore();

        [PLToolAction(Description = "time 秒のフレームを当てる")]
        public void SetTime(float time) => ApplyFrameCore(time);

        /// <summary>再生中のフレームを当てる。setTime と同じに当てたうえで、配信用に OnPlaybackFrame を呼ぶ。</summary>
        [PLToolAction(Description = "再生中の time 秒のフレームを当てる（setTime と同じに当て、ライブ受信の受け入れ中なら配信する）")]
        public void PlayFrame(float time)
        {
            if (!ApplyFrameCore(time)) return;
            OnPlaybackFrame?.Invoke(time);
        }

        [PLToolState(Description = "読み込んだクリップがマッスルのトラックを持つか（持つものだけ再生時に配信できる）")]
        public bool HasMuscles => _applier != null && _dto != null && _applier.HasMuscleTracks;

        /// <summary>読み込んだクリップのマッスルとルートを time 秒で評価して into に入れる（配信用）。</summary>
        public bool TrySampleMuscleFrame(float time, IReadOnlyDictionary<string, int> indexOf, int count, MotionLiveFrame into)
            => _applier != null && _dto != null && _applier.TrySampleMuscleFrame(time, indexOf, count, into);

        [PLToolState(Description = "読み込んだクリップの absentMuscles（駆動されているボーンのトラックの無い dof の値。zero = 0 / rest = 正準 T ポーズの値）")]
        public string AbsentMuscles => _dto == null ? "" : (MotionClipDTO.AbsentIsRest(_dto) ? MotionClipDTO.AbsentMusclesRest : MotionClipDTO.AbsentMusclesZero);

        [PLToolAction(Description = "トラックの無い dof の値（mode: zero / rest）を変え、time 秒のフレームを当て直す。JSON 保存でファイルにも書く")]
        public void SetAbsentMuscles(string mode, float time)
        {
            Error = "";
            if (mode != MotionClipDTO.AbsentMusclesZero && mode != MotionClipDTO.AbsentMusclesRest)
            {
                Error = $"mode は {MotionClipDTO.AbsentMusclesZero} か {MotionClipDTO.AbsentMusclesRest}（\"{mode}\"）";
                return;
            }
            if (_dto == null || _applier == null) { Error = "クリップを読み込んでください"; return; }
            _applier.SetAbsentMuscles(mode);
            ApplyFrameCore(time);
        }

        [PLToolState(Description = "T ポーズ合わせを脚（太もも・すね）にも掛けるか（false は腕だけ）")]
        public bool AlignLegs => _alignLegs;
        private bool _alignLegs;

        [PLToolAction(Description = "T ポーズ合わせを脚（太もも・すね）にも掛けるかを切り替え（enabled=false は腕だけ）、time 秒のフレームを当て直す")]
        public void SetAlignLegs(bool enabled, float time)
        {
            _alignLegs = enabled;
            EnsureApplier();
            if (_dto != null) ApplyFrameCore(time);
        }

        [PLToolAction(Description = "位置に掛ける倍率を変え、time 秒のフレームを当て直す")]
        public void SetPositionScale(float scale, float time)
        {
            EnsureApplier();
            _applier.PositionScale = scale;
            if (_dto != null) ApplyFrameCore(time);
        }

        /// <summary>外部 UnityLimit CSV（マッスル可動域・実測）を読む。ベイク元アバターの limits.csv を渡す。</summary>
        [PLToolAction(Description = "マッスル可動域 CSV（UnityLimit）を読み、time 秒のフレームを当て直す")]
        public void LoadLimit(string path, float time)
        {
            Error = "";
            _limitBoneCount = 0;
            if (string.IsNullOrEmpty(path)) { Error = "ファイルパスを指定してください"; return; }
            if (!File.Exists(path))        { Error = $"ファイルが見つかりません: {Path.GetFileName(path)}"; return; }
            try
            {
                EnsureApplier();
                _limitBoneCount = _applier.LoadMuscleLimitCsv(File.ReadAllText(path));
                if (_dto != null) ApplyFrameCore(time);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                UnityEngine.Debug.LogError($"[MotionClipHandler] {ex}");
            }
        }

        /// <summary>読み込んだ UnityLimit CSV を捨て、既定の可動域へ戻す。</summary>
        [PLToolAction(Description = "マッスル可動域 CSV を外して既定値へ戻し、time 秒のフレームを当て直す")]
        public void ClearLimit(float time)
        {
            _limitBoneCount = 0;
            if (_applier == null) return;
            _applier.ClearMuscleLimits();
            if (_dto != null) ApplyFrameCore(time);
        }

        private void EnsureApplier()
        {
            if (_applier == null) _applier = new MotionClipApplier();
            _applier.SyncMeshPositions = SyncMeshPositions;
            _applier.AlignLegs = _alignLegs;
        }

        private bool ApplyFrameCore(float time)
        {
            var model = GetModel?.Invoke();
            if (_dto == null || model == null || _applier == null) return false;
            _applier.ApplyFrame(model, time);
            OnFrameApplied?.Invoke();
            return true;
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
                if (_dto.bones != null) all.AddRange(_dto.bones);
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
