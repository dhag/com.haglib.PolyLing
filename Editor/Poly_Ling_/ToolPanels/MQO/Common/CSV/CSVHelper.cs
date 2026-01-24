// Assets/Editor/Poly_Ling/MQO/Common/CSV/CSVHelper.cs
// =====================================================================
// 汎用CSVヘルパー
// 
// 【目的】
// CSV文字列のパース・生成を一元管理
// - ダブルクォートのエスケープ処理
// - 空白のトリム
// - コメント行・空行の判定
// 
// 【使用例】
// // 読み込み
// var rows = CSVHelper.ParseString(content);
// foreach (var row in rows)
// {
//     string name = row[0];
//     int value = row.GetInt(schema.ValueColumn);
// }
// 
// // 書き出し
// var writer = new CSVWriter();
// writer.AddRow("Name", 123, 45.6f);
// string csv = writer.ToString();
// =====================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.MQO.CSV
{
    /// <summary>
    /// 汎用CSVヘルパー
    /// </summary>
    public static class CSVHelper
    {
        // =====================================================================
        // パース（読み込み）
        // =====================================================================

        /// <summary>
        /// CSV文字列を行リストにパース
        /// </summary>
        /// <param name="content">CSV文字列</param>
        /// <param name="skipEmptyLines">空行をスキップするか（デフォルトtrue）</param>
        /// <param name="trimFields">フィールドをトリムするか（デフォルトtrue）</param>
        /// <returns>CSVRow のリスト</returns>
        public static List<CSVRow> ParseString(string content, bool skipEmptyLines = true, bool trimFields = true)
        {
            var rows = new List<CSVRow>();
            
            if (string.IsNullOrEmpty(content))
                return rows;
            
            // 改行で分割
            var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            
            foreach (var line in lines)
            {
                // 空行スキップ
                if (skipEmptyLines && string.IsNullOrWhiteSpace(line))
                    continue;
                
                // 行をパース
                var fields = ParseLine(line, trimFields);
                rows.Add(new CSVRow(fields, line));
            }
            
            return rows;
        }
        
        /// <summary>
        /// ファイルからCSVをパース
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="encoding">エンコーディング（nullでUTF8）</param>
        public static List<CSVRow> ParseFile(string filePath, Encoding encoding = null)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[CSVHelper] File not found: {filePath}");
                return new List<CSVRow>();
            }
            
            try
            {
                encoding = encoding ?? Encoding.UTF8;
                string content = File.ReadAllText(filePath, encoding);
                return ParseString(content);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CSVHelper] Failed to read file: {filePath}\n{ex.Message}");
                return new List<CSVRow>();
            }
        }
        
        /// <summary>
        /// スキーマに基づいてCSVをパース（データ行のみ返す）
        /// </summary>
        /// <param name="content">CSV文字列</param>
        /// <param name="schema">スキーマ定義</param>
        /// <param name="skipInvalidRows">無効な行をスキップするか</param>
        public static List<CSVRow> ParseWithSchema(string content, CSVSchemaBase schema, bool skipInvalidRows = true)
        {
            var allRows = ParseString(content);
            var validRows = new List<CSVRow>();
            
            foreach (var row in allRows)
            {
                // コメント行をスキップ
                if (IsCommentLine(row.OriginalLine))
                    continue;
                
                // スキーマによる有効性チェック
                if (schema.IsValidDataRow(row))
                {
                    validRows.Add(row);
                }
                else if (!skipInvalidRows)
                {
                    Debug.LogWarning($"[CSVHelper] Invalid row: {row.OriginalLine}");
                }
            }
            
            return validRows;
        }
        
        // =====================================================================
        // 行パース（ダブルクォート対応）
        // =====================================================================

        /// <summary>
        /// CSV行を列に分割（ダブルクォート内のカンマ・改行対応）
        /// </summary>
        /// <param name="line">1行の文字列</param>
        /// <param name="trimFields">フィールドをトリムするか</param>
        /// <returns>フィールド配列</returns>
        public static string[] ParseLine(string line, bool trimFields = true)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                
                if (c == '"')
                {
                    // ダブルクォート処理
                    if (inQuotes)
                    {
                        // クォート内で "" はエスケープされた "
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++; // 次の " をスキップ
                        }
                        else
                        {
                            // クォート終了
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        // クォート開始
                        inQuotes = true;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    // フィールド区切り
                    string field = current.ToString();
                    fields.Add(trimFields ? field.Trim() : field);
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            
            // 最後のフィールド
            string lastField = current.ToString();
            fields.Add(trimFields ? lastField.Trim() : lastField);
            
            return fields.ToArray();
        }
        
        // =====================================================================
        // ヘルパーメソッド
        // =====================================================================

        /// <summary>コメント行かどうか判定（; または # で始まる）</summary>
        public static bool IsCommentLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;
            
            string trimmed = line.TrimStart();
            return trimmed.StartsWith(";") || trimmed.StartsWith("#");
        }
        
        /// <summary>空行かどうか判定</summary>
        public static bool IsEmptyLine(string line)
        {
            return string.IsNullOrWhiteSpace(line);
        }
    }

    // =====================================================================
    // CSVライター
    // 行単位でCSVを構築
    // =====================================================================

    /// <summary>
    /// CSV書き出しヘルパー
    /// </summary>
    public class CSVWriter
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly int _decimalPrecision;
        private readonly string _floatFormat;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="decimalPrecision">小数点以下の桁数（デフォルト6）</param>
        public CSVWriter(int decimalPrecision = 6)
        {
            _decimalPrecision = decimalPrecision;
            _floatFormat = $"F{decimalPrecision}";
        }
        
        // -----------------------------------------------------------------
        // 行追加
        // -----------------------------------------------------------------
        
        /// <summary>コメント行を追加</summary>
        public CSVWriter AddComment(string comment)
        {
            _sb.AppendLine($";{comment}");
            return this;
        }
        
        /// <summary>ヘッダー行を追加（スキーマから）</summary>
        public CSVWriter AddHeader(CSVSchemaBase schema)
        {
            _sb.AppendLine(schema.GenerateHeader());
            return this;
        }
        
        /// <summary>ヘッダー行を追加（文字列配列から）</summary>
        public CSVWriter AddHeader(params string[] columns)
        {
            _sb.AppendLine(string.Join(",", columns));
            return this;
        }
        
        /// <summary>データ行を追加（object配列から自動変換）</summary>
        public CSVWriter AddRow(params object[] values)
        {
            var fields = new List<string>();
            
            foreach (var value in values)
            {
                fields.Add(FormatValue(value));
            }
            
            _sb.AppendLine(string.Join(",", fields));
            return this;
        }
        
        /// <summary>生の行文字列を追加</summary>
        public CSVWriter AddRawLine(string line)
        {
            _sb.AppendLine(line);
            return this;
        }
        
        // -----------------------------------------------------------------
        // 値のフォーマット
        // -----------------------------------------------------------------
        
        /// <summary>値をCSV用文字列に変換</summary>
        public string FormatValue(object value)
        {
            if (value == null)
                return "";
            
            switch (value)
            {
                case string s:
                    return EscapeString(s);
                    
                case float f:
                    return f.ToString(_floatFormat, CultureInfo.InvariantCulture);
                    
                case double d:
                    return d.ToString(_floatFormat, CultureInfo.InvariantCulture);
                    
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
                    
                case long l:
                    return l.ToString(CultureInfo.InvariantCulture);
                    
                case bool b:
                    return b ? "1" : "0";
                    
                case Vector3 v3:
                    return $"{v3.x.ToString(_floatFormat, CultureInfo.InvariantCulture)}," +
                           $"{v3.y.ToString(_floatFormat, CultureInfo.InvariantCulture)}," +
                           $"{v3.z.ToString(_floatFormat, CultureInfo.InvariantCulture)}";
                    
                case Vector2 v2:
                    return $"{v2.x.ToString(_floatFormat, CultureInfo.InvariantCulture)}," +
                           $"{v2.y.ToString(_floatFormat, CultureInfo.InvariantCulture)}";
                    
                default:
                    return EscapeString(value.ToString());
            }
        }
        
        /// <summary>文字列をCSVエスケープ（必要に応じてクォート）</summary>
        public static string EscapeString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            
            // カンマ、ダブルクォート、改行を含む場合はエスケープ
            if (value.Contains(",") || value.Contains("\"") || 
                value.Contains("\n") || value.Contains("\r"))
            {
                // ダブルクォートを二重化してクォートで囲む
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            
            return value;
        }
        
        /// <summary>ダブルクォートで囲まれた文字列用エスケープ</summary>
        public static string EscapeQuoted(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            
            return value.Replace("\"", "\"\"");
        }
        
        // -----------------------------------------------------------------
        // 出力
        // -----------------------------------------------------------------
        
        /// <summary>CSV文字列を取得</summary>
        public override string ToString()
        {
            return _sb.ToString();
        }
        
        /// <summary>ファイルに書き出し</summary>
        public void WriteToFile(string filePath, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.UTF8;
            File.WriteAllText(filePath, _sb.ToString(), encoding);
        }
        
        /// <summary>バッファをクリア</summary>
        public void Clear()
        {
            _sb.Clear();
        }
    }
}
