// Assets/Editor/Poly_Ling/MQO/Common/CSV/BoneWeightCSVSchema.cs
// =====================================================================
// ボーンウェイトCSV列定義
// 
// 【CSVフォーマット】
// ヘッダー: MqoObjectName,VertexID,VertexIndex,Bone0,Bone1,Bone2,Bone3,Weight0,Weight1,Weight2,Weight3
// 
// 【列の意味】
// - MqoObjectName (0): MQOオブジェクト名、ミラー側は末尾に"+"を付与
// - VertexID (1): 頂点ID（オブジェクト間で同一頂点を識別）、-1で未設定
// - VertexIndex (2): 頂点インデックス（オブジェクト内の順序）
// - Bone0-3 (3-6): ボーン名（最大4本）
// - Weight0-3 (7-10): 各ボーンのウェイト値（0.0-1.0）
// 
// 【VertexIDの3つの用途】
// 1. 頂点位置操作: IDで特定の頂点を識別し操作
// 2. エラー検出: IDの欠損・重複でデータ整合性をチェック
// 3. オブジェクト間共有: 異なるオブジェクトの同一頂点を関連付け
// 
// 【注意】
// データクラス（BoneWeightEntry等）はImport/MQOBoneWeightCSV.csで定義
// このファイルはスキーマ（列定義）のみ
// =====================================================================

using System;
using System.Collections.Generic;

namespace Poly_Ling.MQO.CSV
{
    /// <summary>
    /// ボーンウェイトCSVのスキーマ定義
    /// 列位置と名前を宣言的に定義
    /// </summary>
    public class BoneWeightCSVSchema : CSVSchemaBase
    {
        // =====================================================================
        // 列定義
        // 各列のインデックス、名前、型を宣言的に定義
        // =====================================================================

        /// <summary>
        /// 列0: MQOオブジェクト名
        /// ミラー側は末尾に"+"を付与（例: "Body" → "Body+"）
        /// </summary>
        public readonly CSVColumn MqoObjectName;

        /// <summary>
        /// 列1: 頂点ID
        /// オブジェクトをまたいで同一頂点を識別するためのID
        /// -1 は未設定を意味する
        /// </summary>
        public readonly CSVColumn VertexId;

        /// <summary>
        /// 列2: 頂点インデックス
        /// オブジェクト内での頂点順序（0始まり）
        /// </summary>
        public readonly CSVColumn VertexIndex;

        /// <summary>
        /// 列3-6: ボーン名（4本分）
        /// </summary>
        public readonly CSVColumn Bone0;
        public readonly CSVColumn Bone1;
        public readonly CSVColumn Bone2;
        public readonly CSVColumn Bone3;

        /// <summary>
        /// 列7-10: ボーンウェイト（4本分）
        /// </summary>
        public readonly CSVColumn Weight0;
        public readonly CSVColumn Weight1;
        public readonly CSVColumn Weight2;
        public readonly CSVColumn Weight3;

        // =====================================================================
        // 定数
        // =====================================================================

        /// <summary>ボーン数（固定4本）</summary>
        public const int BoneCount = 4;

        /// <summary>最低限必要な列数</summary>
        public override int MinimumFieldCount => 11;

        // =====================================================================
        // コンストラクタ
        // =====================================================================

        public BoneWeightCSVSchema()
        {
            // 列定義を登録（インデックス順）
            MqoObjectName = RegisterColumn(new CSVColumn(0, "MqoObjectName"));
            VertexId      = RegisterColumn(new CSVColumn(1, "VertexID", "-1"));
            VertexIndex   = RegisterColumn(new CSVColumn(2, "VertexIndex", "-1"));
            
            Bone0         = RegisterColumn(new CSVColumn(3, "Bone0"));
            Bone1         = RegisterColumn(new CSVColumn(4, "Bone1"));
            Bone2         = RegisterColumn(new CSVColumn(5, "Bone2"));
            Bone3         = RegisterColumn(new CSVColumn(6, "Bone3"));
            
            Weight0       = RegisterColumn(new CSVColumn(7, "Weight0", "0"));
            Weight1       = RegisterColumn(new CSVColumn(8, "Weight1", "0"));
            Weight2       = RegisterColumn(new CSVColumn(9, "Weight2", "0"));
            Weight3       = RegisterColumn(new CSVColumn(10, "Weight3", "0"));
        }

        // =====================================================================
        // データ取得ヘルパー
        // =====================================================================

        /// <summary>
        /// 行からボーン名配列を取得
        /// </summary>
        public string[] GetBoneNames(CSVRow row)
        {
            return new string[]
            {
                row.Get(Bone0),
                row.Get(Bone1),
                row.Get(Bone2),
                row.Get(Bone3)
            };
        }

        /// <summary>
        /// 行からウェイト配列を取得
        /// </summary>
        public float[] GetWeights(CSVRow row)
        {
            return new float[]
            {
                row.GetFloat(Weight0),
                row.GetFloat(Weight1),
                row.GetFloat(Weight2),
                row.GetFloat(Weight3)
            };
        }

        /// <summary>
        /// ミラー側オブジェクト名かどうか判定
        /// </summary>
        public static bool IsMirrorObject(string objectName)
        {
            return !string.IsNullOrEmpty(objectName) && objectName.EndsWith("+");
        }

        /// <summary>
        /// ミラー側オブジェクト名から実体側オブジェクト名を取得
        /// </summary>
        public static string GetBaseObjectName(string objectName)
        {
            if (IsMirrorObject(objectName))
                return objectName.Substring(0, objectName.Length - 1);
            return objectName;
        }

        /// <summary>
        /// 実体側オブジェクト名からミラー側オブジェクト名を取得
        /// </summary>
        public static string GetMirrorObjectName(string objectName)
        {
            if (IsMirrorObject(objectName))
                return objectName;
            return objectName + "+";
        }
    }
}
