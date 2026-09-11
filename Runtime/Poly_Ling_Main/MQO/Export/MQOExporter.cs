// Runtime/Poly_Ling_Main/MQO/Export/MQOExporter.cs
// MQOエクスポーター
// Phase 5: ModelContext対応
//
// 【分割先】このファイルから次へ分けてある。
//   MQOExportResult.cs       MQO エクスポートの結果・統計（MQOExporter から分離）。
//   MQOExporter.Document.cs  MQO エクスポート：ドキュメント変換（ワールド行列・ボーン深さと並べ替え・ボーンオブジェクト）。
//   MQOExporter.Legacy.cs    MQO エクスポート：旧ドキュメント変換・既定シーン・マテリアル・オブジェクト変換。
//   MQOExporter.Text.cs      MQO エクスポート：ミラースキップ・マテリアル索引・座標変換・メッシュ統合・テキスト生成。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQOエクスポーター
    /// </summary>
    public static partial class MQOExporter
    {
        // ================================================================
        // パブリックAPI
        // ================================================================

        /// <summary>
        /// ModelContextをMQOファイルに出力（推奨）
        /// Phase 5: ModelContext.Materialsを使用
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            ModelContext model,
            MQOExportSettings settings = null)
        {
            if (model == null)
            {
                return new MQOExportResult
                {
                    Success = false,
                    ErrorMessage = "Model is null"
                };
            }

            return ExportFile(filePath, model.MeshContextList, model.Materials, settings, model.MaterialReferences);
        }

        /// <summary>
        /// MeshContextリストをMQOファイルに出力（マテリアルリスト指定）
        /// Phase 5: グローバルマテリアルリストを明示的に指定
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            IList<MeshContext> meshContexts,
            IList<Material> materials,
            MQOExportSettings settings = null,
            IList<Poly_Ling.Materials.MaterialReference> materialRefs = null)
        {
            var result = new MQOExportResult();

            if (meshContexts == null || meshContexts.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No mesh contexts to export";
                return result;
            }

            settings = settings ?? new MQOExportSettings();

            try
            {
                // MQOドキュメント作成（マテリアルリストとMaterialReferencesを渡す）
                var document = ConvertToDocument(meshContexts, materials, settings, result.Stats, materialRefs);

                // テキスト生成
                string mqoText = GenerateMQOText(document, settings);

                // ファイル出力
                Encoding encoding = settings.UseShiftJIS
                    ? Encoding.GetEncoding("shift_jis")
                    : Encoding.UTF8;

                File.WriteAllText(filePath, mqoText, encoding);

                result.Success = true;
                result.FilePath = filePath;
                result.Stats.ObjectCount = document.Objects.Count;
                result.Stats.MaterialCount = document.Materials.Count;

                Debug.Log($"[MQOExporter] Export successful: {filePath}");
                Debug.Log($"  - Objects: {result.Stats.ObjectCount}");
                Debug.Log($"  - Vertices: {result.Stats.TotalVertices}");
                Debug.Log($"  - Faces: {result.Stats.TotalFaces}");
                Debug.Log($"  - Materials: {result.Stats.MaterialCount}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[MQOExporter] Export failed: {ex}");
            }

            return result;
        }

        /// <summary>
        /// MeshContextリストをMQOファイルに出力（後方互換）
        /// 注意: MeshContext.Materialsを使用（Modelが設定されていればModelContext.Materialsに委譲）
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            IList<MeshContext> meshContexts,
            MQOExportSettings settings = null)
        {
            var result = new MQOExportResult();

            if (meshContexts == null || meshContexts.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No mesh contexts to export";
                return result;
            }

            settings = settings ?? new MQOExportSettings();

            try
            {
                // MQOドキュメント作成（後方互換モード）
                var document = ConvertToDocumentLegacy(meshContexts, settings, result.Stats);

                // テキスト生成
                string mqoText = GenerateMQOText(document, settings);

                // ファイル出力
                Encoding encoding = settings.UseShiftJIS
                    ? Encoding.GetEncoding("shift_jis")
                    : Encoding.UTF8;

                File.WriteAllText(filePath, mqoText, encoding);

                result.Success = true;
                result.FilePath = filePath;
                result.Stats.ObjectCount = document.Objects.Count;
                result.Stats.MaterialCount = document.Materials.Count;

                Debug.Log($"[MQOExporter] Export successful: {filePath}");
                Debug.Log($"  - Objects: {result.Stats.ObjectCount}");
                Debug.Log($"  - Vertices: {result.Stats.TotalVertices}");
                Debug.Log($"  - Faces: {result.Stats.TotalFaces}");
                Debug.Log($"  - Materials: {result.Stats.MaterialCount}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[MQOExporter] Export failed: {ex}");
            }

            return result;
        }

        /// <summary>
        /// 単一のMeshContextをMQOファイルに出力
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            MeshContext meshContext,
            MQOExportSettings settings = null)
        {
            return ExportFile(filePath, new[] { meshContext }, settings);
        }
    }
}