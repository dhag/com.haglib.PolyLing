// MQODocumentIO.cs
// 編集済み MQODocument をファイルに書き出すユーティリティ。
// MQOExporter.GenerateMQOText（internal）を呼び出す。
// Runtime/Poly_Ling_Main/MQO/Export/ に配置

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// 編集済み MQODocument をファイルに書き出す。
    /// 部分エクスポート等で MQODocument を外部加工した後の保存に使用する。
    /// </summary>
    public static class MQODocumentIO
    {
        /// <summary>
        /// MQODocument をテキスト形式でファイルに書き出す。
        /// settings が null の場合はデフォルト設定を使用する。
        /// </summary>
        public static MQOExportResult WriteDocumentToFile(
            MQODocument       document,
            string            filePath,
            MQOExportSettings settings = null)
        {
            var result = new MQOExportResult();

            if (document == null)
            {
                result.Success      = false;
                result.ErrorMessage = "document is null";
                return result;
            }

            if (string.IsNullOrEmpty(filePath))
            {
                result.Success      = false;
                result.ErrorMessage = "filePath is empty";
                return result;
            }

            settings = settings ?? new MQOExportSettings();

            try
            {
                string mqoText = MQOExporter.GenerateMQOText(document, settings);

                Encoding encoding = settings.UseShiftJIS
                    ? Encoding.GetEncoding("shift_jis")
                    : Encoding.UTF8;

                File.WriteAllText(filePath, mqoText, encoding);

                result.Success  = true;
                result.FilePath = filePath;
                Debug.Log($"[MQODocumentIO] Saved: {filePath}");
            }
            catch (Exception ex)
            {
                result.Success      = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[MQODocumentIO] WriteDocumentToFile failed: {ex}");
            }

            return result;
        }
    }
}
