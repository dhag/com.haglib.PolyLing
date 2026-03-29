// PolyLingPlayerIO.cs
// PMX / MQO インポート・エクスポートのファサード
// 各 Importer/Exporter を直接使わず、ここを経由することで
// PolyLingPlayerViewer からのアクセスを単純にする。
// Runtime/Poly_Ling_Player/IO/ に配置

using System;
using System.IO;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.PMX;
using Poly_Ling.MQO;

namespace Poly_Ling.Player
{
    /// <summary>
    /// PMX / MQO インポートのファサード。
    /// パスを渡すだけでデフォルト設定による ModelContext を返す。
    /// </summary>
    public static class PolyLingPlayerIO
    {
        // ================================================================
        // PMX
        // ================================================================

        /// <summary>
        /// PMX ファイルをデフォルト設定でインポートし ModelContext を返す。
        /// 失敗時は null を返し errorMessage にエラー内容を格納する。
        /// </summary>
        public static ModelContext ImportPmx(string filePath, out string errorMessage)
            => ImportPmx(filePath, null, out errorMessage);

        /// <summary>
        /// PMX ファイルを指定設定でインポートし ModelContext を返す。
        /// 失敗時は null を返し errorMessage にエラー内容を格納する。
        /// </summary>
        public static ModelContext ImportPmx(string filePath, PMXImportSettings settings, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrEmpty(filePath))
            {
                errorMessage = "ファイルパスが空です";
                return null;
            }

            if (!File.Exists(filePath))
            {
                errorMessage = $"ファイルが見つかりません: {filePath}";
                return null;
            }

            PMXImportResult result;
            try
            {
                result = PMXImporter.ImportFile(filePath, settings);
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
                return null;
            }

            if (!result.Success)
            {
                errorMessage = result.ErrorMessage;
                return null;
            }

            return BuildModelContext(filePath, result);
        }

        // ================================================================
        // MQO
        // ================================================================

        /// <summary>
        /// MQO ファイルをデフォルト設定でインポートし ModelContext を返す。
        /// 失敗時は null を返し errorMessage にエラー内容を格納する。
        /// </summary>
        public static ModelContext ImportMqo(string filePath, out string errorMessage)
            => ImportMqo(filePath, null, out errorMessage);

        /// <summary>
        /// MQO ファイルを指定設定でインポートし ModelContext を返す。
        /// 失敗時は null を返し errorMessage にエラー内容を格納する。
        /// </summary>
        public static ModelContext ImportMqo(string filePath, MQOImportSettings settings, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrEmpty(filePath))
            {
                errorMessage = "ファイルパスが空です";
                return null;
            }

            if (!File.Exists(filePath))
            {
                errorMessage = $"ファイルが見つかりません: {filePath}";
                return null;
            }

            MQOImportResult result;
            try
            {
                if (settings == null)
                {
                    settings = MQOImportSettings.CreateDefault();
                    settings.BaseDir = Path.GetDirectoryName(filePath);
                }
                else if (string.IsNullOrEmpty(settings.BaseDir))
                {
                    settings.BaseDir = Path.GetDirectoryName(filePath);
                }
                result = MQOImporter.ImportFile(filePath, settings);
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
                return null;
            }

            if (!result.Success)
            {
                errorMessage = result.ErrorMessage;
                return null;
            }

            return BuildModelContext(filePath, result);
        }

        // ================================================================
        // 拡張子ルーティング
        // ================================================================

        /// <summary>
        /// 拡張子から PMX / MQO を判定してインポートする。
        /// 対応拡張子は .pmx / .mqo のみ。
        /// </summary>
        public static ModelContext ImportAuto(string filePath, out string errorMessage)
        {
            errorMessage = null;

            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            switch (ext)
            {
                case ".pmx": return ImportPmx(filePath, out errorMessage);
                case ".mqo": return ImportMqo(filePath, out errorMessage);
                default:
                    errorMessage = $"非対応の拡張子です: {ext}";
                    return null;
            }
        }

        // ================================================================
        // 内部: PMXImportResult → ModelContext
        // ================================================================

        private static ModelContext BuildModelContext(string filePath, PMXImportResult result)
        {
            var model = new ModelContext
            {
                Name               = Path.GetFileNameWithoutExtension(filePath),
                FilePath           = filePath,
                SourceDocument     = result.Document,
                BoneWorldPositions = result.BoneWorldPositions,
            };

            // マテリアルを移送（テクスチャ含む）
            if (result.MaterialReferences != null && result.MaterialReferences.Count > 0)
                model.MaterialReferences = result.MaterialReferences;

            foreach (var mc in result.MeshContexts)
                model.Add(mc);

            foreach (var morph in result.MorphExpressions)
                model.MorphExpressions.Add(morph);

            foreach (var pair in result.MirrorPairs)
                model.MirrorPairs.Add(pair);

            return model;
        }

        // ================================================================
        // 内部: MQOImportResult → ModelContext
        // ================================================================

        private static ModelContext BuildModelContext(string filePath, MQOImportResult result)
        {
            var model = new ModelContext
            {
                Name           = Path.GetFileNameWithoutExtension(filePath),
                FilePath       = filePath,
                SourceDocument = result.Document,
            };

            // マテリアルを移送（テクスチャ・色含む）
            if (result.MaterialReferences != null && result.MaterialReferences.Count > 0)
                model.MaterialReferences = result.MaterialReferences;

            foreach (var mc in result.MeshContexts)
                model.Add(mc);

            foreach (var mc in result.BoneMeshContexts)
                model.Add(mc);

            foreach (var pair in result.MirrorPairs)
                model.MirrorPairs.Add(pair);

            return model;
        }
    }
}
