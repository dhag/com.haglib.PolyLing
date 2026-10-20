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
        /// PMX / MQO / OBJ / STL / VRM の 5 経路で同じなので 1 本にまとめてある。
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

            // 置換は未対応（PolyLing_残件.md）。黙って新規読込にしない。
            if (settings.ImportMode == Poly_Ling.MQO.MQOImportMode.Replace)
                return "置換（Replace）は未対応です。NewModel か Append を指定してください";

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

        /// <summary>STL 読み込みコマンド。実際の読み込みは CommandQueue が後で流す。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteImportStlFile(Poly_Ling.Data.ImportStlFileCommand cmd)
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

            OnImportStl(path, cmd.Settings ?? Poly_Ling.STL.StlImportSettings.CreateDefault(), post);
            return null;
        }

        /// <summary>STL フォルダ一括読み込みコマンド。実際の読み込みは CommandQueue が後で流す。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteImportStlFolder(Poly_Ling.Data.ImportStlFolderCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FolderPath)) return "FolderPath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveFolder(
                    cmd.FolderPath, out string path, out string reason))
                return reason;
            if (!System.IO.Directory.Exists(path))
                return $"フォルダがありません: {path}";

            if (!TryBuildImportPostOptions(
                    cmd.HumanoidAutoMap, cmd.ApplyOriginCsv,
                    cmd.OriginCsvPath, cmd.OriginCsvIncludeRotation,
                    out var post, out string postReason))
                return postReason;

            OnImportStlFolder(path, cmd.IncludeSubfolders,
                cmd.Settings ?? Poly_Ling.STL.StlImportSettings.CreateDefault(), post);
            return null;
        }

        /// <summary>STL 書き出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteExportStlFile(Poly_Ling.Data.ExportStlFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            return OnExportStl(
                path, cmd.Settings ?? Poly_Ling.STL.StlExportSettings.CreateDefault());
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

            if (!TryBuildImportPostOptions(
                    cmd.HumanoidAutoMap, cmd.ApplyOriginCsv,
                    cmd.OriginCsvPath, cmd.OriginCsvIncludeRotation,
                    out var post, out string postReason))
                return postReason;

            OnImportVrm(path, settings, post);
            return null;
        }

        /// <summary>glTF（.glb / .gltf）書き出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteExportGltfFile(Poly_Ling.Data.ExportGltfFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Gltf.PLGltfBridge.Exporter.IsAvailable)
                return "glTF エクスポータが利用できません（VRM パッケージ未導入、または Play 中でない）";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            // .gltf は同じフォルダへ「ファイル名.bin」も書くので、それも関門に通す。
            if (string.Equals(System.IO.Path.GetExtension(path), ".gltf", System.StringComparison.OrdinalIgnoreCase))
            {
                string bin = System.IO.Path.ChangeExtension(path, ".bin");
                if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(bin, out _, out string binReason))
                    return binReason;
            }

            return OnExportGltf(
                path, cmd.Settings ?? Poly_Ling.Gltf.GltfExportSettings.CreateDefault());
        }

        /// <summary>
        /// FBX 書き出しコマンド。書き出し先と、テクスチャの書き出し先（「ファイル名_textures」）を関門に通す。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteExportFbxFile(Poly_Ling.Data.ExportFbxFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Fbx.PLFbxBridge.Exporter.IsAvailable)
                return "FBX エクスポータが利用できません（PolyLing.Fbx が未登録）";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            var settings = (cmd.Settings ?? Poly_Ling.Fbx.FbxExportSettings.CreateDefault()).Clone();
            string texFolder = Poly_Ling.Fbx.FbxExportSettings.DefaultTextureFolder(path);
            if (!Poly_Ling.Core.PLSandbox.TryResolveFolder(
                    texFolder, out string texResolved, out string texReason))
                return texReason;
            settings.TextureFolder = texResolved;

            return OnExportFbx(path, settings);
        }

        /// <summary>
        /// glTF（.glb / .gltf）読み込みコマンド。実際の読み込みは CommandQueue が後で流す。
        /// 関門は VRM と同じ（入力は TryResolveRead、テクスチャ書き出し先は TryResolveFolder）。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteImportGltfFile(Poly_Ling.Data.ImportGltfFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Gltf.PLGltfBridge.Importer.IsAvailable)
                return "glTF インポータが利用できません（VRM パッケージ未導入、または Play 中でない）";

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            var settings = (cmd.Settings ?? Poly_Ling.Gltf.GltfImportSettings.CreateDefault()).Clone();
            settings.TextureFolder = "";

            if (settings.ExtractTextures)
            {
                string texFolder = Poly_Ling.Gltf.GltfImportSettings.DefaultTextureFolder(path);
                if (!Poly_Ling.Core.PLSandbox.TryResolveFolder(
                        texFolder, out string texResolved, out string texReason))
                    return texReason;
                settings.TextureFolder = texResolved;
            }

            if (!TryBuildImportPostOptions(
                    cmd.HumanoidAutoMap, cmd.ApplyOriginCsv,
                    cmd.OriginCsvPath, cmd.OriginCsvIncludeRotation,
                    out var post, out string postReason))
                return postReason;

            OnImportGltf(path, settings, post);
            return null;
        }

        /// <summary>
        /// FBX 読み込みコマンド。実際の読み込みは CommandQueue が後で流す。
        /// 関門は glTF と同じ（入力は TryResolveRead、テクスチャ書き出し先は TryResolveFolder）。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteImportFbxFile(Poly_Ling.Data.ImportFbxFileCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            var settings = (cmd.Settings ?? Poly_Ling.Fbx.FbxImportSettings.CreateDefault()).Clone();
            settings.TextureFolder = "";

            if (settings.ExtractTextures)
            {
                string texFolder = Poly_Ling.Fbx.FbxImportSettings.DefaultTextureFolder(path);
                if (!Poly_Ling.Core.PLSandbox.TryResolveFolder(
                        texFolder, out string texResolved, out string texReason))
                    return texReason;
                settings.TextureFolder = texResolved;
            }

            if (!TryBuildImportPostOptions(
                    cmd.HumanoidAutoMap, cmd.ApplyOriginCsv,
                    cmd.OriginCsvPath, cmd.OriginCsvIncludeRotation,
                    out var post, out string postReason))
                return postReason;

            OnImportFbx(path, settings, post);
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

        /// <summary>プロジェクト全体をエディタ拡張（ヒエラルキー書き出し）へ送るコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSendHierarchyBundle(Poly_Ling.Data.SendHierarchyBundleCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (_playerServer == null) return "リモートサーバがありません";
            if (!_playerServer.IsRunning) return "リモートサーバが起動していません";
            return _playerServer.SendHierarchyBundle();
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

        /// <summary>プロジェクトバイナリ（.plrf）保存コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSaveProjectBinary(Poly_Ling.Data.SaveProjectBinaryCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            return OnSaveBinaryProject(path, cmd.ModelIndex, cmd.Scope, cmd.MasterIndices);
        }

        /// <summary>プロジェクトバイナリ（.plrf）読み込みコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteLoadProjectBinary(Poly_Ling.Data.LoadProjectBinaryCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (string.IsNullOrEmpty(cmd.FilePath)) return "FilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                    cmd.FilePath, out string path, out string reason))
                return reason;

            return OnLoadBinaryProject(path, cmd.ModelIndex, cmd.Append);
        }

        // ================================================================
        // VRM アニメーション（.vrma）
        //
        // 出口は「.plmotion → .vrma」1 本（VrmaCodec。PolyLing_VRMA整備計画.md 2.5）。
        // VMD は VmdMotionBake で .plmotion にしてから同じ出口を通す。
        // ================================================================

        /// <summary>PolyLing モーション → VRMA 書き出しコマンド（モデル非依存）。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteConvertMotionToVrma(Poly_Ling.Data.ConvertMotionToVrmaCommand cmd)
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

            var loaded = Poly_Ling.Motion.MotionClipSerializer.Load(clipPath);
            if (loaded.Dto == null) return $"モーションを読み込めません: {loaded.FormatIssues(5)}";
            var clip = loaded.Dto;

            var options = new Poly_Ling.Motion.VrmaExportOptions
            {
                Fps      = cmd.Fps,
                StartSec = cmd.StartSec,
                EndSec   = cmd.EndSec,
                UseBody  = cmd.UseBody,
            };
            if (!Poly_Ling.Motion.VrmaCodec.WriteFile(clip, outPath, options, out var report, out string reason))
                return $"{reason}（出力先: {outPath}）";

            Debug.Log($"[PolyLing] VRMA 変換: {report.OutputPath} / {report.Summary()}");
            return null;
        }

        /// <summary>VMD＋モデル → PolyLing モーション（マッスル＋二次骨＋表情）＋ VRMA 書き出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteExportVmdToMotion(Poly_Ling.Data.ExportVmdToMotionCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var model = ActiveProject?.CurrentModel;
            if (model == null) return "モデルがありません";

            if (string.IsNullOrEmpty(cmd.FilePath))    return "FilePath が空です";
            if (string.IsNullOrEmpty(cmd.VmdFilePath)) return "VmdFilePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    cmd.FilePath, out string outPath, out string outReason))
                return outReason;

            string vrmaPath = null;
            if (!string.IsNullOrEmpty(cmd.VrmaFilePath)
                && !Poly_Ling.Core.PLSandbox.TryResolveWrite(cmd.VrmaFilePath, out vrmaPath, out string vrmaReason))
                return vrmaReason;

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

            // 座標変換と位置倍率は EditorState を正本にする。
            // PlayerVMDTestSubPanel.LoadVMD と同じ規則で、ここでは新しい変換を足さない。
            var options = Poly_Ling.VMD.VmdVrmAnimationOptions.CreateDefault();
            options.EnableIK          = cmd.EnableIK;
            options.DiagnosticLog     = cmd.DiagnosticLog;
            options.IgnoreAngleLimits = cmd.IgnoreAngleLimits;
            options.KneePreBend       = cmd.KneePreBend;
            options.AlignLegs         = cmd.AlignLegs;

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

            var dto = Poly_Ling.VMD.VmdMotionBake.Bake(
                model, vmd, cmd.Fps, cmd.StartSec, cmd.EndSec, options, out var report, out string bakeReason);

            // 焼き込みはモデルへ実際にフレームを適用する。
            // ポーズ層は Bake が戻すので、表示を引き直す。
            _viewportManager.UpdateTransform();
            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging);

            if (dto == null) return $"{bakeReason}（出力先: {outPath}）";

            dto.name = System.IO.Path.GetFileNameWithoutExtension(
                System.IO.Path.GetFileNameWithoutExtension(outPath));   // "x.plmotion.json" → "x"

            var saved = Poly_Ling.Motion.MotionClipSerializer.Save(dto, outPath);
            if (saved.Dto == null) return $"書き出しに失敗しました: {saved.FormatIssues(10)}（出力先: {outPath}）";

            Debug.Log($"[PolyLing] VMD→モーション 書き出し: {outPath} " +
                      $"({report.FrameCount} frames / ボーン {report.BoneTrackCount} / 表情 {report.ExpressionTrackCount})");
            if (report.DroppedHumanoid.Count > 0)
                Debug.LogWarning("[PolyLing] VMD→モーション: 骨格に載せられなかった Humanoid: "
                                 + string.Join(", ", report.DroppedHumanoid.ToArray()));

            // VRMA は保存した内容（bakedBones 込み）から、.plmotion → .vrma の出口で書く。
            if (!string.IsNullOrEmpty(vrmaPath))
            {
                var vopt = new Poly_Ling.Motion.VrmaExportOptions { Fps = saved.Dto.frameRate };
                if (!Poly_Ling.Motion.VrmaCodec.WriteFile(saved.Dto, vrmaPath, vopt, out var vrep, out string vreason))
                    return $"{vreason}（出力先: {vrmaPath}）";
                Debug.Log($"[PolyLing] VMD→VRMA 書き出し: {vrep.OutputPath} / {vrep.Summary()}");
            }
            return null;
        }

        // ================================================================
        // BVH（モーション＋モデル → モデルの骨格の BVH。PolyLing_BVH対応計画.md 5）
        // ================================================================

        /// <summary>モーション＋モデル → BVH 書き出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteExportMotionToBvh(Poly_Ling.Data.ExportMotionToBvhCommand cmd, Poly_Ling.Motion.BvhExportReport report)
        {
            if (cmd == null) return "コマンドが null";

            var model = ActiveProject?.CurrentModel;
            if (model == null) return "モデルがありません";

            if (string.IsNullOrEmpty(cmd.FilePath))   return "FilePath が空です";
            if (string.IsNullOrEmpty(cmd.SourcePath)) return "SourcePath が空です";

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(cmd.FilePath, out string outPath, out string outReason))
                return outReason;
            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(cmd.SourcePath, out string srcPath, out string srcReason))
                return srcReason;
            if (!System.IO.File.Exists(srcPath)) return $"読み込むファイルがありません: {srcPath}";
            if (!PlayerCommandDispatcher.BuildBvhReadOptions(cmd.BvhSkeleton, cmd.BvhMappingCsvPath, out var bvhOptions, out string bvhReason))
                return bvhReason;
            if (!PlayerCommandDispatcher.BuildFbxReadOptions(bvhOptions, cmd.FbxRestPath, cmd.FbxTake, out var fbxOptions, out string fbxReason))
                return fbxReason;

            var loaded = Poly_Ling.Motion.MotionFileLoad.Load(srcPath, cmd.SourceKind, bvhOptions, fbxOptions);
            if (loaded.Dto == null) return $"モーションを読み込めません: {loaded.FormatIssues(5)}";

            // VMD の位置は PMX 単位なので、パネル再生（MotionClipHandler.Load）と同じく PmxUnityRatio を掛けて当てる。
            float clipScale = 1f;
            if (cmd.SourceKind == Poly_Ling.Data.MotionSourceKind.Vmd)
                clipScale = _editOps?.UndoController?.EditorState?.PmxUnityRatio ?? 0.1f;

            var options = new Poly_Ling.Motion.BvhExportOptions
            {
                Fps               = cmd.Fps,
                StartSec          = cmd.StartSec,
                EndSec            = cmd.EndSec,
                PositionScale     = cmd.PositionScale,
                ClipPositionScale = clipScale,
                AlignLegs         = cmd.AlignLegs,
            };
            bool ok = Poly_Ling.Motion.BvhCodec.ExportFromModel(model, loaded.Dto, outPath, options, report, out string reason);

            // 書き出しはモデルへ実際にフレームを当てる。ポーズ層は ExportFromModel が戻すので、表示を引き直す。
            // UpdateTransform は規約で新規に呼ばない入口なので、正規入口だけを通す。
            // パネルから撃ったときはパネルが表示中のフレームを当て直す（motionClip の setTime）。
            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging);

            if (!ok) return $"{reason}（出力先: {outPath}）";
            Debug.Log($"[PolyLing] BVH 書き出し: {report.OutputPath} / {report.Summary()}");
            return null;
        }

        // ================================================================
        // FBX（モーション＋モデル → FBX。PolyLing_FBX対応計画.md 段 3）
        // ================================================================

        /// <summary>モーション＋モデル → FBX 書き出しコマンド。読み込みと関門は BVH 書き出しと同じ。</summary>
        private Poly_Ling.Fbx.FbxExportResult ExecuteExportMotionToFbx(Poly_Ling.Data.ExportMotionToFbxCommand cmd)
        {
            Poly_Ling.Fbx.FbxExportResult Fail(string why) => Poly_Ling.Fbx.FbxExportResult.Failed(why);

            if (cmd == null) return Fail("コマンドが null");
            var model = ActiveProject?.CurrentModel;
            if (model == null) return Fail("モデルがありません");
            if (string.IsNullOrEmpty(cmd.FilePath))   return Fail("FilePath が空です");
            if (string.IsNullOrEmpty(cmd.SourcePath)) return Fail("SourcePath が空です");

            if (!Poly_Ling.Fbx.PLFbxBridge.Exporter.IsAvailable)
                return Fail("FBX エクスポータが利用できません（PolyLing.Fbx が未登録）");

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(cmd.FilePath, out string outPath, out string outReason))
                return Fail(outReason);
            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(cmd.SourcePath, out string srcPath, out string srcReason))
                return Fail(srcReason);
            if (!System.IO.File.Exists(srcPath)) return Fail($"読み込むファイルがありません: {srcPath}");
            if (!PlayerCommandDispatcher.BuildBvhReadOptions(cmd.BvhSkeleton, cmd.BvhMappingCsvPath, out var bvhOptions, out string bvhReason))
                return Fail(bvhReason);

            var settings = (cmd.Settings ?? Poly_Ling.Fbx.FbxExportSettings.CreateDefault()).Clone();
            string texFolder = Poly_Ling.Fbx.FbxExportSettings.DefaultTextureFolder(outPath);
            if (!Poly_Ling.Core.PLSandbox.TryResolveFolder(texFolder, out string texResolved, out string texReason))
                return Fail(texReason);
            settings.TextureFolder = texResolved;

            if (!PlayerCommandDispatcher.BuildFbxReadOptions(bvhOptions, cmd.FbxRestPath, cmd.FbxTake, out var fbxOptions, out string fbxReason))
                return Fail(fbxReason);

            var loaded = Poly_Ling.Motion.MotionFileLoad.Load(srcPath, cmd.SourceKind, bvhOptions, fbxOptions);
            if (loaded.Dto == null) return Fail($"モーションを読み込めません: {loaded.FormatIssues(5)}");

            // VMD の位置は PMX 単位なので、パネル再生（MotionClipHandler.Load）と同じく PmxUnityRatio を掛けて当てる。
            float clipScale = 1f;
            if (cmd.SourceKind == Poly_Ling.Data.MotionSourceKind.Vmd)
                clipScale = _editOps?.UndoController?.EditorState?.PmxUnityRatio ?? 0.1f;

            var motion = new Poly_Ling.Fbx.FbxMotionExportOptions
            {
                Fps               = cmd.Fps,
                StartSec          = cmd.StartSec,
                EndSec            = cmd.EndSec,
                ClipPositionScale = clipScale,
                AlignLegs         = cmd.AlignLegs,
                IncludeMesh       = cmd.IncludeMesh,
                TakeName          = string.IsNullOrEmpty(loaded.Dto.name)
                                    ? System.IO.Path.GetFileNameWithoutExtension(srcPath)
                                    : loaded.Dto.name,
            };
            var result = Poly_Ling.Fbx.PLFbxBridge.Exporter.ExportMotion(model, loaded.Dto, outPath, settings, motion);

            // 書き出しはモデルへ実際にフレームを当てる。ポーズ層は標本化が戻すので、表示を引き直す（BVH と同じ正規入口）。
            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging);

            if (result.Success)
                Debug.Log($"[PolyLing] FBX モーション書き出し: {outPath} / {result.Summary()}");
            else
                result.ErrorMessage = $"{result.ErrorMessage}（出力先: {outPath}）";
            return result;
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

        /// <summary>
        /// 空のモデルを 1 つ足してカレントにする。既存のモデルは残す。
        /// マテリアルスロットは ResetProject と同じく既定の 1 つを用意する。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteCreateEmptyModel(CreateEmptyModelCommand cmd)
        {
            _localLoader.EnsureProject();

            var project = ActiveProject;
            if (project == null) return "プロジェクトを用意できませんでした";

            string name = string.IsNullOrEmpty(cmd?.ModelName) ? null : cmd.ModelName;
            var model = project.CreateNewModel(name);
            if (model == null) return "モデルを作れませんでした";

            EnsureDefaultMaterialSlot(model);

            _viewportManager.EnterTopologyChanged(project);
            RebuildModelList();
            NotifyPanels(ChangeKind.ListStructure);
            return null;
        }
    }
}
