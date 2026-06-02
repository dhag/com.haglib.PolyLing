// Runtime/Poly_Ling_Main/Core/Serialization/AuxiliaryBackupWriter.cs
// MQO/PMX エクスポート時に、隣接して出力する復元用バックアップ。
//
// 既存の CsvModelSerializer をそのまま使い、出力ファイルと同階層に
//   "<出力名>_backup/"
// フォルダとしてモデル全体（頂点ID・VertexFlags/FaceFlags・ウェイト・
// ミラーウェイト・Type・階層・ミラー/モーフ設定・選択セット・IK・BindPose 等）を
// 保存する。MQO/PMX の往復で失われる編集側の情報を丸ごと残し、本体が破損しても
// このフォルダから復元できるようにするための補助データ。
//
// 補助データなので、書き込み失敗は本体（MQO/PMX）保存を巻き込まない（warningログのみ）。

using System;
using System.IO;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Serialization.FolderSerializer;

namespace Poly_Ling.Serialization
{
    /// <summary>
    /// MQO/PMX 出力に付随する復元用バックアップフォルダの書き出し。
    /// </summary>
    public static class AuxiliaryBackupWriter
    {
        /// <summary>バックアップフォルダ名の接尾辞</summary>
        public const string FolderSuffix = "_backup";

        /// <summary>
        /// 出力ファイルパスから、隣接するバックアップフォルダのパスを算出する。
        /// 例: C:/out/Foo.pmx → C:/out/Foo_backup
        /// 算出できない場合は null。
        /// </summary>
        public static string ResolveBackupFolder(string exportFilePath)
        {
            if (string.IsNullOrEmpty(exportFilePath)) return null;

            string dir = Path.GetDirectoryName(exportFilePath);
            string baseName = Path.GetFileNameWithoutExtension(exportFilePath);
            if (string.IsNullOrEmpty(baseName)) return null;

            string folderName = baseName + FolderSuffix;
            return string.IsNullOrEmpty(dir) ? folderName : Path.Combine(dir, folderName);
        }

        /// <summary>
        /// 復元用バックアップを書き出す。
        /// 失敗しても false を返すのみで、例外は呼び出し元へ伝播させない。
        /// </summary>
        /// <param name="model">保存対象モデル</param>
        /// <param name="exportFilePath">MQO/PMX の出力先パス（このパスから隣接フォルダ名を算出）</param>
        /// <returns>保存に成功したら true</returns>
        public static bool Save(ModelContext model, string exportFilePath)
        {
            if (model == null) return false;

            string folder = ResolveBackupFolder(exportFilePath);
            if (string.IsNullOrEmpty(folder)) return false;

            try
            {
                CsvModelSerializer.SaveModel(folder, model);
                Debug.Log($"[AuxiliaryBackupWriter] 補助データ保存: {folder}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[AuxiliaryBackupWriter] 補助データ保存に失敗（本体保存は完了済み）: {ex.Message}");
                return false;
            }
        }
    }
}
