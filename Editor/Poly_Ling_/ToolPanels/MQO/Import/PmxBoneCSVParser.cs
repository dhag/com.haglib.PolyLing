// Assets/Editor/Poly_Ling/MQO/Import/PmxBoneCSVParser.cs
// =====================================================================
// PmxBone形式のCSVをパースしてボーン情報を読み取る
// 
// 【CSVフォーマット】
// コメント行: ;PmxBone,"ボーン名","英名",変形階層,物理後,位置X,位置Y,位置Z,回転,移動,IK,表示,操作,"親ボーン名"
// データ行:   PmxBone,"全ての親","",0,0,0.000000,0.000000,0.000000,1,0,0,1,1,""
// 
// 【列の意味】（PmxBoneCSVSchemaで定義）
// - 列0: "PmxBone" （行タイプ識別子）
// - 列1: ボーン名（日本語名）
// - 列2: 英名
// - 列3: 変形階層
// - 列4: 物理後フラグ（0/1）
// - 列5-7: ボーンの位置（X, Y, Z）
// - 列8-12: 各種フラグ
// - 列13: 親ボーン名
// =====================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.MQO.CSV;

namespace Poly_Ling.MQO
{
    // =========================================================================
    // データクラス
    // =========================================================================

    /// <summary>
    /// PmxBoneデータ
    /// 1つのボーンの情報を保持
    /// </summary>
    public class PmxBoneData
    {
        /// <summary>ボーン名（日本語）</summary>
        public string Name { get; set; }

        /// <summary>英名</summary>
        public string NameEn { get; set; }

        /// <summary>ワールド位置</summary>
        public Vector3 Position { get; set; }

        /// <summary>親ボーン名（空でルート）</summary>
        public string ParentName { get; set; }

        /// <summary>変形階層</summary>
        public int DeformHierarchy { get; set; }

        /// <summary>物理後フラグ</summary>
        public bool IsPhysicsAfter { get; set; }

        /// <summary>回転可能</summary>
        public bool CanRotate { get; set; }

        /// <summary>移動可能</summary>
        public bool CanMove { get; set; }

        /// <summary>IKフラグ</summary>
        public bool IsIK { get; set; }

        /// <summary>表示フラグ</summary>
        public bool IsVisible { get; set; }

        /// <summary>操作可能</summary>
        public bool IsControllable { get; set; }
    }

    // =========================================================================
    // パーサー
    // =========================================================================

    /// <summary>
    /// PmxBone CSVパーサー
    /// </summary>
    public static class PmxBoneCSVParser
    {
        // スキーマ定義（列位置の宣言的定義）
        private static readonly PmxBoneCSVSchema _schema = new PmxBoneCSVSchema();

        /// <summary>
        /// CSVファイルをパース
        /// </summary>
        /// <param name="filePath">CSVファイルパス</param>
        /// <returns>ボーンデータリスト、失敗時は空リスト</returns>
        public static List<PmxBoneData> ParseFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Debug.LogWarning($"[PmxBoneCSVParser] File not found: {filePath}");
                return new List<PmxBoneData>();
            }

            try
            {
                string content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                return Parse(content);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PmxBoneCSVParser] Failed to read file: {e.Message}");
                return new List<PmxBoneData>();
            }
        }

        /// <summary>
        /// CSV文字列をパース
        /// </summary>
        /// <param name="content">CSV文字列</param>
        /// <returns>ボーンデータリスト</returns>
        public static List<PmxBoneData> Parse(string content)
        {
            var bones = new List<PmxBoneData>();
            
            if (string.IsNullOrEmpty(content))
                return bones;

            // ステップ1: CSVHelperで行リストに変換
            var rows = CSVHelper.ParseString(content);

            // ステップ2: 各行をパース
            foreach (var row in rows)
            {
                // コメント行をスキップ
                if (CSVHelper.IsCommentLine(row.OriginalLine))
                    continue;

                // スキーマによる有効性チェック（"PmxBone,"で始まるか）
                if (!_schema.IsValidDataRow(row))
                    continue;

                // ステップ3: スキーマを使ってボーンデータに変換
                var bone = ParseRow(row);
                if (bone != null)
                {
                    bones.Add(bone);
                }
            }

            Debug.Log($"[PmxBoneCSVParser] Parsed {bones.Count} bones");
            return bones;
        }

        /// <summary>
        /// 1行をパースしてPmxBoneDataを生成
        /// スキーマの列定義を使用して意味のある列アクセス
        /// </summary>
        private static PmxBoneData ParseRow(CSVRow row)
        {
            try
            {
                // スキーマの列定義を使ってデータ取得
                var bone = new PmxBoneData
                {
                    // 列1: ボーン名
                    Name = row.Get(_schema.BoneName),
                    
                    // 列2: 英名
                    NameEn = row.Get(_schema.BoneNameEn),
                    
                    // 列3: 変形階層
                    DeformHierarchy = row.GetInt(_schema.DeformHierarchy, 0),
                    
                    // 列4: 物理後フラグ
                    IsPhysicsAfter = row.GetBool(_schema.PhysicsAfter),
                    
                    // 列5-7: 位置（スキーマのヘルパーメソッドを使用）
                    Position = _schema.GetPosition(row),
                    
                    // 列8: 回転可能
                    CanRotate = row.GetBool(_schema.CanRotate, true),
                    
                    // 列9: 移動可能
                    CanMove = row.GetBool(_schema.CanMove),
                    
                    // 列10: IKフラグ
                    IsIK = row.GetBool(_schema.IsIK),
                    
                    // 列11: 表示フラグ
                    IsVisible = row.GetBool(_schema.IsVisible, true),
                    
                    // 列12: 操作可能
                    IsControllable = row.GetBool(_schema.IsControllable, true),
                    
                    // 列13: 親ボーン名
                    ParentName = row.Get(_schema.ParentName)
                };

                return bone;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PmxBoneCSVParser] Failed to parse line: {e.Message}\n{row.OriginalLine}");
                return null;
            }
        }
    }
}
