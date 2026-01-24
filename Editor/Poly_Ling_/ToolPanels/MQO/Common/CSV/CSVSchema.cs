// Assets/Editor/Poly_Ling/MQO/Common/CSV/CSVSchema.cs
// =====================================================================
// CSV列定義の基盤クラス
// 
// 【目的】
// CSVファイルの列位置をハードコードせず、宣言的に定義することで
// - 列位置の意味をコード上で明確化
// - 列順序の変更に対する保守性向上
// - パース処理の可読性向上
// 
// 【使用例】
// var schema = new BoneWeightCSVSchema();
// var row = CSVHelper.ParseRow(line);
// string objectName = row.Get(schema.MqoObjectName);
// int vertexId = row.GetInt(schema.VertexId);
// =====================================================================

using System;
using System.Collections.Generic;

namespace Poly_Ling.MQO.CSV
{
    // =====================================================================
    // 列定義（Column）
    // 1つのCSV列の情報を保持
    // =====================================================================

    /// <summary>
    /// CSVの1列を定義
    /// 列インデックス、列名、デフォルト値を保持
    /// </summary>
    public class CSVColumn
    {
        /// <summary>列インデックス（0始まり）</summary>
        public int Index { get; }
        
        /// <summary>列名（ヘッダー名やドキュメント用）</summary>
        public string Name { get; }
        
        /// <summary>値が空の場合のデフォルト値</summary>
        public string DefaultValue { get; }
        
        /// <summary>コンストラクタ</summary>
        /// <param name="index">列インデックス（0始まり）</param>
        /// <param name="name">列名</param>
        /// <param name="defaultValue">デフォルト値（省略時は空文字）</param>
        public CSVColumn(int index, string name, string defaultValue = "")
        {
            Index = index;
            Name = name;
            DefaultValue = defaultValue;
        }
        
        public override string ToString() => $"Column[{Index}]: {Name}";
    }
    
    /// <summary>
    /// 型付きCSV列（ジェネリック版）
    /// </summary>
    public class CSVColumn<T> : CSVColumn
    {
        /// <summary>文字列から型Tへの変換関数</summary>
        public Func<string, T> Parser { get; }
        
        /// <summary>型Tのデフォルト値</summary>
        public T TypedDefaultValue { get; }
        
        public CSVColumn(int index, string name, Func<string, T> parser, T defaultValue = default)
            : base(index, name, defaultValue?.ToString() ?? "")
        {
            Parser = parser;
            TypedDefaultValue = defaultValue;
        }
    }

    // =====================================================================
    // 行データ（Row）
    // パース済みの1行データを保持し、スキーマに基づいてアクセス
    // =====================================================================

    /// <summary>
    /// パース済みCSV行
    /// スキーマの列定義を使って型安全にアクセス可能
    /// </summary>
    public class CSVRow
    {
        private readonly string[] _fields;
        
        /// <summary>フィールド数</summary>
        public int FieldCount => _fields?.Length ?? 0;
        
        /// <summary>元の行テキスト（デバッグ用）</summary>
        public string OriginalLine { get; }
        
        /// <summary>コンストラクタ</summary>
        public CSVRow(string[] fields, string originalLine = null)
        {
            _fields = fields ?? Array.Empty<string>();
            OriginalLine = originalLine;
        }
        
        // -----------------------------------------------------------------
        // インデックスアクセス
        // -----------------------------------------------------------------
        
        /// <summary>インデックスで直接アクセス</summary>
        public string this[int index]
        {
            get
            {
                if (index < 0 || index >= _fields.Length)
                    return "";
                return _fields[index] ?? "";
            }
        }
        
        // -----------------------------------------------------------------
        // 列定義によるアクセス
        // -----------------------------------------------------------------
        
        /// <summary>列定義から文字列値を取得</summary>
        public string Get(CSVColumn column)
        {
            if (column == null)
                return "";
            
            if (column.Index < 0 || column.Index >= _fields.Length)
                return column.DefaultValue;
            
            string value = _fields[column.Index];
            return string.IsNullOrEmpty(value) ? column.DefaultValue : value;
        }
        
        /// <summary>列定義からint値を取得</summary>
        public int GetInt(CSVColumn column, int defaultValue = 0)
        {
            string s = Get(column);
            return int.TryParse(s, out int result) ? result : defaultValue;
        }
        
        /// <summary>列定義からfloat値を取得</summary>
        public float GetFloat(CSVColumn column, float defaultValue = 0f)
        {
            string s = Get(column);
            return float.TryParse(s, out float result) ? result : defaultValue;
        }
        
        /// <summary>列定義からbool値を取得（0/1または"true"/"false"）</summary>
        public bool GetBool(CSVColumn column, bool defaultValue = false)
        {
            string s = Get(column).Trim().ToLower();
            if (s == "1" || s == "true" || s == "yes")
                return true;
            if (s == "0" || s == "false" || s == "no")
                return false;
            return defaultValue;
        }
        
        /// <summary>型付き列定義から値を取得</summary>
        public T Get<T>(CSVColumn<T> column)
        {
            if (column == null)
                return default;
            
            // 明示的に基底クラス版のGetを呼び出し
            string s = Get((CSVColumn)column);
            if (string.IsNullOrEmpty(s))
                return column.TypedDefaultValue;
            
            try
            {
                return column.Parser(s);
            }
            catch
            {
                return column.TypedDefaultValue;
            }
        }
        
        // -----------------------------------------------------------------
        // 配列アクセス（連続列から配列取得）
        // -----------------------------------------------------------------
        
        /// <summary>連続する列からstring配列を取得</summary>
        public string[] GetStringArray(CSVColumn startColumn, int count)
        {
            var result = new string[count];
            int startIndex = startColumn.Index;
            
            for (int i = 0; i < count; i++)
            {
                int idx = startIndex + i;
                result[i] = (idx >= 0 && idx < _fields.Length) ? _fields[idx] ?? "" : "";
            }
            
            return result;
        }
        
        /// <summary>連続する列からfloat配列を取得</summary>
        public float[] GetFloatArray(CSVColumn startColumn, int count, float defaultValue = 0f)
        {
            var result = new float[count];
            int startIndex = startColumn.Index;
            
            for (int i = 0; i < count; i++)
            {
                int idx = startIndex + i;
                string s = (idx >= 0 && idx < _fields.Length) ? _fields[idx] : "";
                result[i] = float.TryParse(s, out float v) ? v : defaultValue;
            }
            
            return result;
        }
        
        /// <summary>生のフィールド配列を取得</summary>
        public string[] ToArray() => (string[])_fields.Clone();
    }

    // =====================================================================
    // スキーマ基底クラス
    // 各CSV形式のスキーマはこれを継承
    // =====================================================================

    /// <summary>
    /// CSVスキーマ基底クラス
    /// 派生クラスで列定義を宣言的に記述
    /// </summary>
    public abstract class CSVSchemaBase
    {
        /// <summary>すべての列定義</summary>
        protected List<CSVColumn> AllColumns { get; } = new List<CSVColumn>();
        
        /// <summary>最低限必要な列数</summary>
        public abstract int MinimumFieldCount { get; }
        
        /// <summary>ヘッダー行のプレフィックス（コメント行判定用、nullで無効）</summary>
        public virtual string HeaderPrefix => null;
        
        /// <summary>データ行のプレフィックス（nullで制限なし）</summary>
        public virtual string DataRowPrefix => null;
        
        /// <summary>行がデータ行として有効か判定</summary>
        public virtual bool IsValidDataRow(CSVRow row)
        {
            if (row.FieldCount < MinimumFieldCount)
                return false;
            
            if (DataRowPrefix != null)
            {
                string first = row[0]?.Trim();
                if (first != DataRowPrefix)
                    return false;
            }
            
            return true;
        }
        
        /// <summary>ヘッダー行を生成</summary>
        public virtual string GenerateHeader()
        {
            var names = new List<string>();
            foreach (var col in AllColumns)
            {
                names.Add(col.Name);
            }
            return string.Join(",", names);
        }
        
        /// <summary>列を登録（派生クラスのコンストラクタで使用）</summary>
        protected T RegisterColumn<T>(T column) where T : CSVColumn
        {
            AllColumns.Add(column);
            return column;
        }
    }
}
