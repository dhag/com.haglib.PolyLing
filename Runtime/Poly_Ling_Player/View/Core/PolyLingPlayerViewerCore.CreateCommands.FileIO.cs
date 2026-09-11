// PolyLingPlayerViewerCore.CreateCommands.FileIO.cs
// 生成系コマンドの受け口：ファイル読み込み・書き出し・プロジェクト入出力・VRMA 書き出し・
// プロジェクト初期化。配線は PolyLingPlayerViewerCore.CreateCommands.cs。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        // ================================================================
        // ファイル読み込み
        // ================================================================

        /// <summary>
        /// PMX 読み込みコマンド。
        ///
        /// 【なぜ既存の OnImportPmx を呼ぶか】
        ///   実処理（Poly_Ling.Commands.ImportPmxCommand の組み立てと
        ///   CommandQueue への投入、読込後オプションの適用）は
        ///   パネル経路が正典。第 2 実装を作らない。
        ///
        /// 【関門】
        ///   PLSandbox.TryResolveRead を通す。作業フォルダの外は読めない。
        ///   原点 CSV も指定があれば同じ関門を通す。
        ///
        /// 【非同期】
        ///   実際の読み込みは CommandQueue が後で流す。ここでは投入の成否だけを返す。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteImportPmxFile(Poly_Ling.Data.ImportPmxFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            if (!TryBuildImportPostOptions(
                    cmd.HumanoidAutoMap, cmd.ApplyOriginCsv,
                    cmd.OriginCsvPath, cmd.OriginCsvIncludeRotation,
                    out var post, out string postReason))
                return postReason;

            OnImportPmx(path, cmd.Settings, post);
            return null;
        }

        /// <summary>
        /// 読込後オプションを組む。原点 CSV の指定があれば関門を通す。
        ///
        /// PMX / MQO / OBJ の 3 経路で同じなので 1 本にまとめてある。
        /// </summary>
        /// <returns>組めたか。false のとき reason に理由が入る。</returns>
        private static bool TryBuildImportPostOptions(
            bool humanoidAutoMap, bool applyOriginCsv,
            string originCsvPath, bool originCsvIncludeRotation,
            out PlayerImportSubPanel.PostOptions post, out string reason)
        {
            post   = null;
            reason = null;

            string originCsv = "";
            if (applyOriginCsv)
            {
                if (string.IsNullOrEmpty(originCsvPath))
                {
                    reason = "ApplyOriginCsv が true ですが OriginCsvPath が空です";
                    return false;
                }

                if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                        originCsvPath, out originCsv, out reason))
                    return false;
            }

            post = new PlayerImportSubPanel.PostOptions
            {
                HumanoidAutoMap          = humanoidAutoMap,
                ApplyOriginCsv           = applyOriginCsv,
                OriginCsvPath            = originCsv,
                OriginCsvIncludeRotation = originCsvIncludeRotation,
            };
            return true;
        }

        // ================================================================
        // ファイル書き出し・プロジェクト入出力
        //
        // 【なぜ既存の OnExport* / On*Project を呼ぶか】
        //   エクスポータの呼び出し・状態表示・例外の扱いはパネル経路が正典。
        //   第 2 実装を作らない。
        //
        // 【関門】
        //   出力は PLSandbox.TryResolveWrite、入力は TryResolveRead を通す。
        //   設定型で Ignore にしてある入力パスと List<string> は、コマンドが
        //   持っているものをここで設定へ入れる。
        // ================================================================

        /// <summary>PMX 書き出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteExportPmxFile(Poly_Ling.Data.ExportPmxFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            var settings = cmd.Settings ?? Poly_Ling.PMX.PMXExportSettings.CreateFullExport();

            settings.ReplaceMaterialNames = new List<string>(
                cmd.ReplaceMaterialNames ?? System.Array.Empty<string>());

            if (!string.IsNullOrEmpty(cmd.SourcePmxPath))
            {
                if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                        cmd.SourcePmxPath, out string srcPath, out string srcReason))
                    return srcReason;
                settings.SourcePMXPath = srcPath;
            }

            return OnExportPmx(path, settings);
        }

        /// <summary>MQO 読み込みコマンド。実際の読み込みは CommandQueue が後で流す。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteImportMqoFile(Poly_Ling.Data.ImportMqoFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            var settings = cmd.Settings ?? Poly_Ling.MQO.MQOImportSettings.CreateDefault();

            if (!string.IsNullOrEmpty(cmd.BoneWeightCsvPath))
            {
                if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                        cmd.BoneWeightCsvPath, out string bwPath, out string bwReason))
                    return bwReason;
                settings.BoneWeightCSVPath = bwPath;
            }

            if (!string.IsNullOrEmpty(cmd.BoneCsvPath))
            {
                if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                        cmd.BoneCsvPath, out string bcPath, out string bcReason))
                    return bcReason;
                settings.BoneCSVPath = bcPath;
            }

            if (!TryBuildImportPostOptions(
                    cmd.HumanoidAutoMap, cmd.ApplyOriginCsv,
                    cmd.OriginCsvPath, cmd.OriginCsvIncludeRotation,
                    out var post, out string postReason))
                return postReason;

            OnImportMqo(path, settings, post);
            return null;
        }

        /// <summary>MQO 書き出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteExportMqoFile(Poly_Ling.Data.ExportMqoFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            return OnExportMqo(
                path,
                cmd.Settings ?? Poly_Ling.MQO.MQOExportSettings.CreateFromCoordinate(
                    0.01f, flipZ: false, flipX: true));
        }

        /// <summary>OBJ 読み込みコマンド。実際の読み込みは CommandQueue が後で流す。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteImportObjFile(Poly_Ling.Data.ImportObjFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            if (!TryBuildImportPostOptions(
                    cmd.HumanoidAutoMap, cmd.ApplyOriginCsv,
                    cmd.OriginCsvPath, cmd.OriginCsvIncludeRotation,
                    out var post, out string postReason))
                return postReason;

            OnImportObj(path, cmd.Settings ?? Poly_Ling.OBJ.ObjImportSettings.CreateDefault(), post);
            return null;
        }

        /// <summary>OBJ 書き出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteExportObjFile(Poly_Ling.Data.ExportObjFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            return OnExportObj(
                path, cmd.Settings ?? Poly_Ling.OBJ.ObjExportSettings.CreateDefault());
        }

        /// <summary>VRM 1.0 書き出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteExportVrmFile(Poly_Ling.Data.ExportVrmFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            var settings = cmd.Settings ?? Poly_Ling.Vrm.Vrm10ExportSettings.CreateDefault();

            settings.Authors = new List<string>(cmd.Authors ?? System.Array.Empty<string>());

            return OnExportVrm(path, settings);
        }

        /// <summary>
        /// VRM 読み込みコマンド。実際の読み込みは CommandQueue が後で流す。
        ///
        /// 【関門】
        ///   入力は TryResolveRead。テクスチャを書き出す場合は、書き出し先フォルダ
        ///   （VRM と同じフォルダの「VRM名_textures」）も TryResolveFolder に通す。
        ///   外から書き出し先は指定させない（Vrm10ImportSettings.TextureFolder は Ignore）。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteImportVrmFile(Poly_Ling.Data.ImportVrmFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Vrm.PLVrm10ImportBridge.I.IsAvailable)
                return "VRM インポータが利用できません（VRM パッケージ未導入、または Play 中でない）";

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            var settings = (cmd.Settings ?? Poly_Ling.Vrm.Vrm10ImportSettings.CreateDefault()).Clone();
            settings.TextureFolder = "";

            if (settings.ExtractTextures)
            {
                string texFolder = Poly_Ling.Vrm.Vrm10ImportSettings.DefaultTextureFolder(path);
                if (!Poly_Ling.Core.PLSandbox.TryResolveFolder(
                        texFolder, out string texResolved, out string texReason))
                    return texReason;
                settings.TextureFolder = texResolved;
            }

            OnImportVrm(path, settings, new PlayerImportSubPanel.PostOptions());
            return null;
        }

        /// <summary>プロジェクト（.mfproj）保存コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSaveProjectFile(Poly_Ling.Data.SaveProjectFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            return OnSaveProject(path);
        }

        /// <summary>プロジェクト（.mfproj）読み込みコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteLoadProjectFile(Poly_Ling.Data.LoadProjectFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            return OnLoadProject(path);
        }

        /// <summary>プロジェクト CSV 保存コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSaveProjectCsv(Poly_Ling.Data.SaveProjectCsvCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            return OnSaveCsvProject(path);
        }

        /// <summary>
        /// プロジェクト CSV 読み込みコマンド。
        ///
        /// Merge が true のときは、プロジェクトが無い状態では何も起きない。
        /// MergeCsvFromFolder（PolyLingPlayerViewerCore.cs:9746-9747）が
        /// CurrentModel を要求し、「モデルがありません」を返して戻る。
        /// 足す先が無いのだから正しい挙動であり、門は通してよい。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteLoadProjectCsv(Poly_Ling.Data.LoadProjectCsvCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            return OnLoadCsvProject(path, cmd.Merge);
        }

        // ================================================================
        // VRM アニメーション（.vrma）書き出し
        //
        // 入力は Unity クリップの JSON。フレームごとにモデルへ適用しながら
        // 骨格を写し取るので、終了後はパネルの表示フレームへ戻す。
        // ================================================================

        /// <summary>VRM アニメーション（.vrma）書き出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteExportVrmAnimation(Poly_Ling.Data.ExportVrmAnimationCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var model = ActiveProject?.CurrentModel;
            if (model == null) return "モデルがありません";

            if (string.IsNullOrEmpty(cmd.FilePath))     return "FilePath が空です";
            if (string.IsNullOrEmpty(cmd.ClipFilePath)) return "ClipFilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string outPath, out string outReason))
                return outReason;

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.ClipFilePath, out string clipPath, out string clipReason))
                return clipReason;

            string limitText = null;
            if (!string.IsNullOrEmpty(cmd.MuscleLimitCsvPath))
            {
                if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                        cmd.MuscleLimitCsvPath, out string limitPath, out string limitReason))
                    return limitReason;
                limitText = System.IO.File.ReadAllText(limitPath);
            }

            Poly_Ling.UnityClip.UnityClipDTO clip;
            try
            {
                clip = Poly_Ling.UnityClip.UnityClipSerializer.LoadJson(clipPath);
            }
            catch (System.Exception ex)
            {
                return $"クリップの読込みに失敗: {ex.Message}";
            }
            if (clip == null) return "クリップを読み取れません";

            var settings = new Poly_Ling.Vrm.VrmAnimationExportSettings
            {
                Scale    = cmd.Scale,
                Fps      = cmd.Fps,
                StartSec = cmd.StartSec,
                EndSec   = cmd.EndSec,
            };

            var result = Poly_Ling.UnityClip.UnityClipVrmAnimationSource.ExportToFile(
                model, clip, limitText, outPath, settings);

            // 書き出しはモデルへ実際にフレームを適用する。
            // ポーズ層は ExportToFile が戻すので、パネルの表示フレームを引き直す。
            _unityClipTestSubPanel?.ReapplyCurrentFrame();
            _viewportManager.UpdateTransform();
            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging);

            if (result == null)     return "書き出し結果がありません";
            if (!result.Success)    return result.ErrorMessage;

            Debug.Log($"[PolyLing] VRMA 書き出し: {result.OutputPath} " +
                      $"(Humanoid {result.HumanoidBoneCount} / {result.FrameCount} frames / {result.DurationSec:F2}s)");
            return null;
        }

        /// <summary>Unity クリップ → VRMA 変換コマンド（モデル非依存）。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteConvertUnityClipToVrma(Poly_Ling.Data.ConvertUnityClipToVrmaCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            if (string.IsNullOrEmpty(cmd.FilePath))     return "FilePath が空です";
            if (string.IsNullOrEmpty(cmd.ClipFilePath)) return "ClipFilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string outPath, out string outReason))
                return outReason;

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.ClipFilePath, out string clipPath, out string clipReason))
                return clipReason;

            Poly_Ling.UnityClip.UnityClipDTO clip;
            try
            {
                clip = Poly_Ling.UnityClip.UnityClipSerializer.LoadJson(clipPath);
            }
            catch (System.Exception ex)
            {
                return $"クリップの読込みに失敗: {ex.Message}";
            }
            if (clip == null) return "クリップを読み取れません";

            var settings = new Poly_Ling.Vrm.VrmAnimationExportSettings
            {
                Fps      = cmd.Fps,
                StartSec = cmd.StartSec,
                EndSec   = cmd.EndSec,
            };

            var result = Poly_Ling.UnityClip.UnityClipCanonVrmAnimation.ConvertToFile(
                clip, outPath, settings, cmd.BoneLength, cmd.UseRoot);

            if (result == null)  return "変換結果がありません";
            if (!result.Success) return $"{result.ErrorMessage}（出力先: {outPath}）";

            Debug.Log($"[PolyLing] VRMA 変換: {result.OutputPath} " +
                      $"(Humanoid {result.HumanoidBoneCount} / {result.FrameCount} frames / {result.DurationSec:F2}s)");
            return null;
        }

        /// <summary>VMD → VRMA 書き出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteExportVmdToVrma(Poly_Ling.Data.ExportVmdToVrmaCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var model = ActiveProject?.CurrentModel;
            if (model == null) return "モデルがありません";

            if (string.IsNullOrEmpty(cmd.FilePath))    return "FilePath が空です";
            if (string.IsNullOrEmpty(cmd.VmdFilePath)) return "VmdFilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string outPath, out string outReason))
                return outReason;

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.VmdFilePath, out string vmdPath, out string vmdReason))
                return vmdReason;

            Poly_Ling.VMD.VMDData vmd;
            try
            {
                vmd = Poly_Ling.VMD.VMDData.LoadFromFile(vmdPath);
            }
            catch (System.Exception ex)
            {
                return $"VMD の読込みに失敗: {ex.Message}";
            }
            if (vmd == null) return "VMD を読み取れません";

            var settings = new Poly_Ling.Vrm.VrmAnimationExportSettings
            {
                Scale    = cmd.Scale,
                Fps      = cmd.Fps,
                StartSec = cmd.StartSec,
                EndSec   = cmd.EndSec,
            };

            // 座標変換と位置倍率は EditorState を正本にする。
            // PlayerVMDTestSubPanel.LoadVMD と同じ規則で、ここでは新しい変換を足さない。
            var options = Poly_Ling.VMD.VmdVrmAnimationOptions.CreateDefault();
            options.EnableIK          = cmd.EnableIK;
            options.AlignScope        = cmd.AlignScope;
            options.DiagnosticLog     = cmd.DiagnosticLog;
            options.IgnoreAngleLimits = cmd.IgnoreAngleLimits;
            options.KneePreBend       = cmd.KneePreBend;

            // IK の残差 CSV の出力先。指定があるときだけ関門を通す。
            if (!string.IsNullOrEmpty(cmd.IkTraceDirectory))
            {
                if (!Poly_Ling.Core.PLSandbox.TryResolveFolder(
                        cmd.IkTraceDirectory, out string traceDir, out string traceReason))
                    return traceReason;
                options.IkTraceDirectory = traceDir;
            }
            var es = _editOps?.UndoController?.EditorState;
            if (es != null)
            {
                options.PositionScale = es.PmxUnityRatio;
                options.FlipX         = es.PmxFlipX;
                options.FlipZ         = es.PmxFlipZ;
            }

            var result = Poly_Ling.VMD.VmdVrmAnimationExport.ExportToFile(
                model, vmd, outPath, settings, options);

            // 書き出しはモデルへ実際にフレームを適用する。
            // ポーズ層は ExportToFile が戻すので、表示を引き直す。
            _viewportManager.UpdateTransform();
            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging);

            if (result == null)  return "書き出し結果がありません";
            if (!result.Success) return $"{result.ErrorMessage}（出力先: {outPath}）";

            Debug.Log($"[PolyLing] VMD→VRMA 書き出し: {result.OutputPath} " +
                      $"(Humanoid {result.HumanoidBoneCount} / {result.FrameCount} frames / {result.DurationSec:F2}s)");
            if (!string.IsNullOrEmpty(result.Warning))
                Debug.LogWarning($"[PolyLing] VMD→VRMA: {result.Warning}");
            return null;
        }

        // ================================================================
        // 穴頂点数合わせ
        // ================================================================

        // ================================================================
        // プロジェクト初期化
        // ================================================================

        /// <summary>
        /// プロジェクトのモデルを全部捨てて 1 つだけ作り直す。
        ///
        /// CsvProjectSerializer.Export はプロジェクト内の全モデルをフォルダへ書く
        /// （CsvProjectSerializer.cs:238-250）。前のモデルが残っていると、
        /// 保存したフォルダに関係ないモデルが同梱されて中身が読めなくなる。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteResetProject(ResetProjectCommand cmd)
        {
            _localLoader.EnsureProject();

            var project = ActiveProject;
            if (project == null) return "プロジェクトを用意できませんでした";

            // 後ろから消す。前から消すと索引がずれる。
            for (int i = project.ModelCount - 1; i >= 0; i--)
                project.RemoveModelAt(i);

            string name = string.IsNullOrEmpty(cmd?.ModelName) ? "Model" : cmd.ModelName;
            var model = project.CreateNewModel(name);
            if (model == null) return $"モデル \"{name}\" を作れませんでした";

            EnsureDefaultMaterialSlot(model);

            _viewportManager.EnterSceneReset(project, clearScene: true);
            RebuildModelList();
            NotifyPanels(ChangeKind.ListStructure);
            return null;
        }
    }
}
