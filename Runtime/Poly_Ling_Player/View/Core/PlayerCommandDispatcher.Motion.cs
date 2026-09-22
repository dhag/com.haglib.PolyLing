// PlayerCommandDispatcher.Motion.cs
// PolyLing モーション JSON の書き出し・検査コマンドの受け口。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【モデルを見ない】
//   どちらのコマンドもファイルだけを扱うので、プロジェクト・モデルの null 門より前で捌く
//   （ConvertUnityClipToVrmaCommand と同じ扱い）。何も読み込んでいない状態でも使える。
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
                default: return false;
            }
        }

        private void RunExportMotionJson(ExportMotionJsonCommand cmd)
        {
            if (!PLSandbox.TryResolveRead(cmd.SourcePath, out string srcPath, out string srcReason)) { MotionFail(srcReason); return; }
            if (!PLSandbox.TryResolveWrite(cmd.FilePath, out string outPath, out string outReason)) { MotionFail(outReason); return; }
            if (!File.Exists(srcPath)) { MotionFail($"読み込むファイルがありません: {srcPath}"); return; }

            MotionClipDTO dto;
            string sourceIssues = null;
            try
            {
                switch (cmd.SourceKind)
                {
                    case MotionSourceKind.Vmd:
                        dto = MotionClipConverters.FromVMD(VMDData.LoadFromFile(srcPath));
                        break;
                    default:
                    {
                        var loaded = MotionClipSerializer.Load(srcPath);
                        if (loaded.Dto == null)
                        {
                            MotionFail($"読み込めません: {loaded.FormatIssues(MotionIssueTextMax)}");
                            return;
                        }
                        dto = loaded.Dto;
                        if (loaded.Issues.Count > 0) sourceIssues = loaded.FormatIssues(MotionIssueTextMax);
                        break;
                    }
                }
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
