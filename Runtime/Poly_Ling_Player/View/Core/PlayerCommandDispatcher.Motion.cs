// PlayerCommandDispatcher.Motion.cs
// PolyLing モーション JSON の書き出し・検査コマンドの受け口。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【モデルを見ない】
//   どちらのコマンドもファイルだけを扱うので、プロジェクト・モデルの null 門より前で捌く
//   （ConvertMotionToVrmaCommand と同じ扱い）。何も読み込んでいない状態でも使える。
//
// 【受け口を置かない】
//   変換と検査は MotionClipSerializer / MotionClipConverters の静的メソッドで完結し、
//   Viewer 側に実体が無い。QueryCommandAuditCommand と同じくここで直に ReportData する。
//
// 【パスは必ず PLSandbox を通す】

using System;
using System.IO;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Motion;
using Poly_Ling.VMD;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        private const int MotionIssueTextMax = 20;

        // パネルからの発行では結果を受け取れないため、失敗理由をログにも出す。
        private void MotionFail(string reason)
        {
            UnityEngine.Debug.LogError($"[PolyLing] モーション JSON: {reason}");
            Fail(reason);
        }

        /// <summary>モーション JSON のコマンドなら処理して true を返す。</summary>
        private bool DispatchMotionFile(PanelCommand cmd)
        {
            switch (cmd)
            {
                case ExportMotionJsonCommand c: RunExportMotionJson(c); return true;
                case QueryMotionJsonCommand c:  RunQueryMotionJson(c);  return true;
                case ConvertMotionRootToBodyCommand c: RunConvertMotionRootToBody(c); return true;
                case LoadMotionClipJsonCommand c:  RunLoadMotionClipJson(c);  return true;
                case QueryMotionClipJsonCommand _: RunQueryMotionClipJson();  return true;
                case SaveMotionClipJsonCommand c:  RunSaveMotionClipJson(c);  return true;
                default: return false;
            }
        }

        /// <summary>モーションの試し再生の窓口（ビューアが配線する）。loadMotionClipJson・queryMotionClipJson が使う。</summary>
        public Func<MotionClipHandler> GetMotionClipHandler;

        // loadMotionClipJson：モデルが無くてもクリップは読み込む（当てるのはモデルがあるときだけ。MotionClipHandler と同じ）。
        private void RunLoadMotionClipJson(LoadMotionClipJsonCommand cmd)
        {
            var h = GetMotionClipHandler?.Invoke();
            if (h == null) { _pendingResult = CommandResult.Fail("モーションの試し再生の窓口がありません", CommandErrorCodes.NotReady); return; }
            if (string.IsNullOrWhiteSpace(cmd.MotionJson))
            { _pendingResult = CommandResult.Fail("motionJson が空です", CommandErrorCodes.InvalidArgs); return; }

            var r = h.LoadJson(cmd.MotionJson, cmd.Time);
            if (r.Dto == null)
            { _pendingResult = CommandResult.Fail($"読み込めません: {r.FormatIssues(MotionIssueTextMax)}", CommandErrorCodes.InvalidArgs); return; }
            if (!string.IsNullOrEmpty(h.Error))
            { _pendingResult = CommandResult.Fail($"当てられません: {h.Error}", CommandErrorCodes.Exception); return; }

            var dto = r.Dto;
            ReportData(CommandDataJson.New()
                .Text("name",          dto.name ?? "")
                .Num ("durationSec",   MotionClipSerializer.Length(dto))
                .Num ("frameRate",     h.FrameRate)
                .Int ("bakedBones",    dto.bakedBones?.Count ?? 0)
                .Int ("muscles",       dto.muscles?.Count ?? 0)
                .Int ("expressions",   dto.expressions?.Count ?? 0)
                .Int ("matchedTracks", h.MatchedTrackCount)
                .Int ("warnings",      r.WarningCount)
                .Text("issues",        r.FormatIssues(MotionIssueTextMax))
                .Build());
        }

        private void RunQueryMotionClipJson()
        {
            var h = GetMotionClipHandler?.Invoke();
            if (h == null) { _pendingResult = CommandResult.Fail("モーションの試し再生の窓口がありません", CommandErrorCodes.NotReady); return; }
            var dto = h.Clip;
            if (dto == null) { _pendingResult = CommandResult.Fail("モーションの試し再生にクリップを読み込んでいません", CommandErrorCodes.NotReady); return; }

            var r = MotionClipSerializer.ToSaveJson(dto, out string json);
            if (json == null)
            { _pendingResult = CommandResult.Fail($"JSON にできません: {r.FormatIssues(MotionIssueTextMax)}", CommandErrorCodes.Failed); return; }

            ReportData(CommandDataJson.New()
                .Text("motionJson",  json)
                .Text("name",        r.Dto.name ?? "")
                .Num ("durationSec", MotionClipSerializer.Length(r.Dto))
                .Text("sourcePath",  h.SourcePath ?? "")
                .Build());
        }

        // saveMotionClipJson：窓口のクリップを書き、そのファイルを窓口の元ファイルにする（MotionClipHandler.SaveJson）。
        private void RunSaveMotionClipJson(SaveMotionClipJsonCommand cmd)
        {
            var h = GetMotionClipHandler?.Invoke();
            if (h == null) { _pendingResult = CommandResult.Fail("モーションの試し再生の窓口がありません", CommandErrorCodes.NotReady); return; }
            if (h.Clip == null) { _pendingResult = CommandResult.Fail("モーションの試し再生にクリップを読み込んでいません", CommandErrorCodes.NotReady); return; }
            if (!PLSandbox.TryResolveWrite(cmd.FilePath, out string outPath, out string reason)) { MotionFail(reason); return; }

            if (!h.SaveJson(outPath)) { MotionFail($"書き出しに失敗しました: {h.Error}"); return; }

            ReportData(CommandDataJson.New()
                .Text("path",        outPath)
                .Text("name",        h.Clip.name ?? "")
                .Num ("durationSec", MotionClipSerializer.Length(h.Clip))
                .Build());
        }

        private void RunConvertMotionRootToBody(ConvertMotionRootToBodyCommand cmd)
        {
            if (!PLSandbox.TryResolveFolder(cmd.FolderPath, out string dir, out string reason)) { MotionFail(reason); return; }
            if (!Directory.Exists(dir)) { MotionFail($"フォルダがありません: {dir}"); return; }

            int converted = 0, skipped = 0, failed = 0;
            var issues = new System.Text.StringBuilder();
            void Note(string file, string why)
            {
                if (issues.Length < 4000) issues.Append(file).Append(": ").Append(why).Append('\n');
            }

            foreach (string path in Directory.GetFiles(dir, "*.plmotion.json", SearchOption.TopDirectoryOnly))
            {
                string file = Path.GetFileName(path);
                try
                {
                    var jo = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8));
                    if (!MotionClipRootUpgrade.TryMoveRootCurvesToBody(jo, out string note))
                    {
                        skipped++;
                        Note(file, note);
                        continue;
                    }
                    var parsed = MotionClipSerializer.Parse(jo.ToString(Newtonsoft.Json.Formatting.None));
                    if (parsed.Dto == null) { failed++; Note(file, parsed.FormatIssues(3)); continue; }
                    var saved = MotionClipSerializer.Save(parsed.Dto, path);
                    if (saved.Dto == null) { failed++; Note(file, saved.FormatIssues(3)); continue; }
                    converted++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Note(file, ex.Message);
                }
            }

            UnityEngine.Debug.Log($"[PolyLing] Root を body へ移す: 書き換え {converted} / そのまま {skipped} / 失敗 {failed}（{dir}）");
            ReportData(CommandDataJson.New()
                .Int ("converted", converted)
                .Int ("skipped",   skipped)
                .Int ("failed",    failed)
                .Text("issues",    issues.ToString())
                .Build());
        }

        private void RunExportMotionJson(ExportMotionJsonCommand cmd)
        {
            if (!PLSandbox.TryResolveRead(cmd.SourcePath, out string srcPath, out string srcReason)) { MotionFail(srcReason); return; }
            if (!PLSandbox.TryResolveWrite(cmd.FilePath, out string outPath, out string outReason)) { MotionFail(outReason); return; }
            if (!File.Exists(srcPath)) { MotionFail($"読み込むファイルがありません: {srcPath}"); return; }

            if (!BuildBvhReadOptions(cmd.BvhSkeleton, cmd.BvhMappingCsvPath, out var bvhOptions, out string bvhReason))
            { MotionFail(bvhReason); return; }
            if (!BuildFbxReadOptions(bvhOptions, cmd.FbxRestPath, cmd.FbxTake, out var fbxOptions, out string fbxReason))
            { MotionFail(fbxReason); return; }

            MotionClipDTO dto;
            string sourceIssues = null;
            try
            {
                var loaded = MotionFileLoad.Load(srcPath, cmd.SourceKind, bvhOptions, fbxOptions);
                if (loaded.Dto == null)
                {
                    MotionFail($"読み込めません: {loaded.FormatIssues(MotionIssueTextMax)}");
                    return;
                }
                dto = loaded.Dto;
                if (loaded.Issues.Count > 0) sourceIssues = loaded.FormatIssues(MotionIssueTextMax);
            }
            catch (Exception ex)
            {
                MotionFail($"読み込みに失敗しました: {ex.Message}");
                return;
            }

            if (dto == null) { MotionFail("読み込み結果が空です"); return; }
            if (string.IsNullOrEmpty(dto.name))
                dto.name = MotionNameFromPath(outPath);
            if (dto.metadata == null)
                dto.metadata = new MotionMetadataDTO { createdWith = "PolyLing" };
            if (!string.IsNullOrEmpty(cmd.AbsentMuscles))
                dto.absentMuscles = cmd.AbsentMuscles;   // 値の検査は Save の Validate が行う

            var saved = MotionClipSerializer.Save(dto, outPath);
            if (saved.Dto == null)
            {
                MotionFail($"書き出しに失敗しました: {saved.FormatIssues(MotionIssueTextMax)}");
                return;
            }

            string issues = saved.Issues.Count > 0 ? saved.FormatIssues(MotionIssueTextMax) : sourceIssues;
            ReportData(CommandDataJson.New()
                .Int ("errors",      saved.ErrorCount)
                .Int ("warnings",    saved.WarningCount)
                .Int ("bones",       dto.bones?.Count ?? 0)
                .Int ("bakedBones",  dto.bakedBones?.Count ?? 0)
                .Int ("muscles",     dto.muscles?.Count ?? 0)
                .Int ("expressions", dto.expressions?.Count ?? 0)
                .Num ("durationSec", MotionClipSerializer.Length(dto))
                .Text("issues",      issues ?? "")
                .Build());
        }

        private void RunQueryMotionJson(QueryMotionJsonCommand cmd)
        {
            if (!PLSandbox.TryResolveRead(cmd.FilePath, out string path, out string reason)) { MotionFail(reason); return; }
            if (!File.Exists(path)) { MotionFail($"ファイルがありません: {path}"); return; }

            var r   = MotionClipSerializer.Load(path);
            var dto = r.Dto;
            int version = dto == null ? 0 : (r.UpgradedFromVersion > 0 ? r.UpgradedFromVersion : MotionClipDTO.CurrentVersion);

            ReportData(CommandDataJson.New()
                .Flag("valid",       r.Success)
                .Int ("version",     version)
                .Int ("errors",      r.ErrorCount)
                .Int ("warnings",    r.WarningCount)
                .Int ("bones",       dto?.bones?.Count ?? 0)
                .Int ("bakedBones",  dto?.bakedBones?.Count ?? 0)
                .Int ("muscles",     dto?.muscles?.Count ?? 0)
                .Int ("expressions", dto?.expressions?.Count ?? 0)
                .Num ("durationSec", MotionClipSerializer.Length(dto))
                .Text("issues",      r.FormatIssues(MotionIssueTextMax))
                .Build());
        }

        /// <summary>
        /// BVH の読込の設定をコマンドの引数から作る（割り当て CSV は作業フォルダの関門を通す）。
        /// ExportMotionJsonCommand と ExportMotionToBvhCommand の受け口が共用する。
        /// </summary>
        internal static bool BuildBvhReadOptions(BvhSkeletonKind skeleton, string csvPath,
                                                 out BvhReadOptions options, out string reason)
        {
            options = new BvhReadOptions { Skeleton = skeleton };
            reason  = null;
            if (string.IsNullOrEmpty(csvPath)) return true;
            if (!PLSandbox.TryResolveRead(csvPath, out string path, out reason)) return false;
            if (!File.Exists(path)) { reason = $"割り当て CSV がありません: {path}"; return false; }
            try { options.MappingCsvLines = File.ReadAllLines(path, System.Text.Encoding.UTF8); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                reason = $"割り当て CSV を読めません: {ex.Message}";
                return false;
            }
            return true;
        }

        /// <summary>
        /// FBX のモーション読み込みの設定（割り当て CSV は BVH と同じもの、レスト用の FBX は関門を通す）。
        /// restPath が空ならレスト用の FBX は使わない。take は読むテイクの番号（範囲外は読み込み時にエラー）。
        /// </summary>
        internal static bool BuildFbxReadOptions(BvhReadOptions bvh, string restPath, int take,
                                                 out Poly_Ling.Fbx.FbxMotionReadOptions options, out string reason)
        {
            options = new Poly_Ling.Fbx.FbxMotionReadOptions { MappingCsvLines = bvh?.MappingCsvLines, TakeIndex = take };
            reason  = null;
            if (string.IsNullOrEmpty(restPath)) return true;
            if (!PLSandbox.TryResolveRead(restPath, out string path, out reason)) return false;
            if (!File.Exists(path)) { reason = $"レスト用の FBX がありません: {path}"; return false; }
            options.RestFbxPath = path;
            return true;
        }

        // "Walk.plmotion.json" → "Walk"
        private static string MotionNameFromPath(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path ?? "");
            const string suffix = ".plmotion";
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - suffix.Length);
            return name;
        }
    }
}
