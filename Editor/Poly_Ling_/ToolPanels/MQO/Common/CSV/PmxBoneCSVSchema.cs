// Assets/Editor/Poly_Ling/MQO/Common/CSV/PmxBoneCSVSchema.cs
// =====================================================================
// PmxBone形式CSVの列定義
// 
// 【CSVフォーマット】
// コメント行: ;PmxBone,"ボーン名","英名",変形階層,物理後,位置X,位置Y,位置Z,回転,移動,IK,表示,操作,"親ボーン名"
// データ行:   PmxBone,"全ての親","",0,0,0.000000,0.000000,0.000000,1,0,0,1,1,""
// 
// 【列の意味】
// - 列0: "PmxBone" （行タイプ識別子）
// - 列1: ボーン名（日本語名）
// - 列2: 英名（空でも可）
// - 列3: 変形階層（0が最初、値が大きいほど後で計算）
// - 列4: 物理後フラグ（0/1）
// - 列5-7: ボーンの位置（X, Y, Z）
// - 列8: 回転可能フラグ（0/1）
// - 列9: 移動可能フラグ（0/1）
// - 列10: IKフラグ（0/1）
// - 列11: 表示フラグ（0/1）
// - 列12: 操作可能フラグ（0/1）
// - 列13: 親ボーン名（空文字はルートボーン）
// 
// 【備考】
// - PMXエディタと互換性のある形式
// - ボーン階層はParentNameで構築
// - 位置はワールド座標
// 
// 【注意】
// データクラス（PmxBoneData）はImport/PmxBoneCSVParser.csで定義
// このファイルはスキーマ（列定義）のみ
// =====================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.MQO.CSV
{
    /// <summary>
    /// PmxBone形式CSVのスキーマ定義
    /// 列位置と名前を宣言的に定義
    /// </summary>
    public class PmxBoneCSVSchema : CSVSchemaBase
    {
        // =====================================================================
        // 列定義
        // =====================================================================

        /// <summary>列0: 行タイプ識別子 "PmxBone"</summary>
        public readonly CSVColumn RowType;

        /// <summary>列1: ボーン名（日本語）</summary>
        public readonly CSVColumn BoneName;

        /// <summary>列2: 英名</summary>
        public readonly CSVColumn BoneNameEn;

        /// <summary>列3: 変形階層（0から）</summary>
        public readonly CSVColumn DeformHierarchy;

        /// <summary>列4: 物理後フラグ（0/1）</summary>
        public readonly CSVColumn PhysicsAfter;

        /// <summary>列5: 位置X</summary>
        public readonly CSVColumn PositionX;

        /// <summary>列6: 位置Y</summary>
        public readonly CSVColumn PositionY;

        /// <summary>列7: 位置Z</summary>
        public readonly CSVColumn PositionZ;

        /// <summary>列8: 回転可能（0/1）</summary>
        public readonly CSVColumn CanRotate;

        /// <summary>列9: 移動可能（0/1）</summary>
        public readonly CSVColumn CanMove;

        /// <summary>列10: IKフラグ（0/1）</summary>
        public readonly CSVColumn IsIK;

        /// <summary>列11: 表示フラグ（0/1）</summary>
        public readonly CSVColumn IsVisible;

        /// <summary>列12: 操作可能（0/1）</summary>
        public readonly CSVColumn IsControllable;

        /// <summary>列13: 親ボーン名（空でルート）</summary>
        public readonly CSVColumn ParentName;

        // =====================================================================
        // 定数
        // =====================================================================

        /// <summary>行タイプ識別子</summary>
        public const string RowTypeValue = "PmxBone";

        /// <summary>最低限必要な列数</summary>
        public override int MinimumFieldCount => 14;

        /// <summary>データ行のプレフィックス</summary>
        public override string DataRowPrefix => RowTypeValue;

        // =====================================================================
        // コンストラクタ
        // =====================================================================

        public PmxBoneCSVSchema()
        {
            // 列定義を登録（インデックス順）
            RowType          = RegisterColumn(new CSVColumn(0, "RowType", RowTypeValue));
            BoneName         = RegisterColumn(new CSVColumn(1, "ボーン名"));
            BoneNameEn       = RegisterColumn(new CSVColumn(2, "英名"));
            DeformHierarchy  = RegisterColumn(new CSVColumn(3, "変形階層", "0"));
            PhysicsAfter     = RegisterColumn(new CSVColumn(4, "物理後", "0"));
            PositionX        = RegisterColumn(new CSVColumn(5, "位置X", "0"));
            PositionY        = RegisterColumn(new CSVColumn(6, "位置Y", "0"));
            PositionZ        = RegisterColumn(new CSVColumn(7, "位置Z", "0"));
            CanRotate        = RegisterColumn(new CSVColumn(8, "回転", "1"));
            CanMove          = RegisterColumn(new CSVColumn(9, "移動", "0"));
            IsIK             = RegisterColumn(new CSVColumn(10, "IK", "0"));
            IsVisible        = RegisterColumn(new CSVColumn(11, "表示", "1"));
            IsControllable   = RegisterColumn(new CSVColumn(12, "操作", "1"));
            ParentName       = RegisterColumn(new CSVColumn(13, "親ボーン名"));
        }

        // =====================================================================
        // データ取得ヘルパー
        // =====================================================================

        /// <summary>
        /// 行から位置ベクトルを取得
        /// </summary>
        public Vector3 GetPosition(CSVRow row)
        {
            return new Vector3(
                row.GetFloat(PositionX),
                row.GetFloat(PositionY),
                row.GetFloat(PositionZ)
            );
        }

        /// <summary>
        /// 行がルートボーンかどうか判定
        /// </summary>
        public bool IsRootBone(CSVRow row)
        {
            string parent = row.Get(ParentName);
            return string.IsNullOrEmpty(parent);
        }

        /// <summary>
        /// ヘッダーコメント行を生成
        /// </summary>
        public string GenerateHeaderComment()
        {
            return ";PmxBone,\"ボーン名\",\"英名\",変形階層,物理後,位置X,位置Y,位置Z,回転,移動,IK,表示,操作,\"親ボーン名\"";
        }
    }
}
