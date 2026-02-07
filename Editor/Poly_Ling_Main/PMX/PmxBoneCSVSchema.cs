// Assets/Editor/Poly_Ling/PMX/CSV/PmxBoneCSVSchema.cs
// =====================================================================
// PmxBone形式CSVの列定義（PMXEditor 40カラム完全互換）
// 
// 【CSVフォーマット — PMXEditor互換40カラム】
// ;PmxBone,ボーン名,ボーン名(英),変形階層,物理後(0/1),
//   位置_x,位置_y,位置_z,回転(0/1),移動(0/1),IK(0/1),表示(0/1),操作(0/1),
//   親ボーン名,表示先(0:オフセット/1:ボーン),表示先ボーン名,
//   表示先オフセット_x,表示先オフセット_y,表示先オフセット_z,
//   ローカル付与(0/1),回転付与(0/1),移動付与(0/1),付与率,付与親名,
//   軸制限(0/1),制限軸_x,制限軸_y,制限軸_z,
//   ローカル軸(0/1),ローカルX軸_x,ローカルX軸_y,ローカルX軸_z,
//   ローカルZ軸_x,ローカルZ軸_y,ローカルZ軸_z,
//   外部親(0/1),外部親Key,IKTarget名,IKLoop,IK単位角[deg]
// 
// 【後方互換】
// 先頭14カラムは旧形式と完全一致のため、
// MinimumFieldCount=14 で旧14カラムCSVもそのまま読み込み可能
// 15カラム以上ある場合は追加フィールドを読み取る
// 
// 【注意】
// データクラス（PmxBoneData）はPMX/CSV/PmxBoneCSVParser.csで定義
// このファイルはスキーマ（列定義）のみ
// =====================================================================

using System;
using System.Collections.Generic;
using UnityEngine;


namespace Poly_Ling.CSV
{
    /// <summary>
    /// PmxBone形式CSVのスキーマ定義
    /// PMXEditor 40カラム完全対応・旧14カラムも後方互換
    /// </summary>
    public class PmxBoneCSVSchema : CSVSchemaBase
    {
        // =====================================================================
        // 列定義 — 基本14カラム（旧形式と共通）
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
        // 列定義 — 拡張26カラム（PMXEditor互換、列14-39）
        // =====================================================================

        /// <summary>列14: 表示先タイプ（0:オフセット/1:ボーン）</summary>
        public readonly CSVColumn ConnectType;

        /// <summary>列15: 表示先ボーン名</summary>
        public readonly CSVColumn ConnectBoneName;

        /// <summary>列16: 表示先オフセットX</summary>
        public readonly CSVColumn ConnectOffsetX;

        /// <summary>列17: 表示先オフセットY</summary>
        public readonly CSVColumn ConnectOffsetY;

        /// <summary>列18: 表示先オフセットZ</summary>
        public readonly CSVColumn ConnectOffsetZ;

        /// <summary>列19: ローカル付与（0/1）</summary>
        public readonly CSVColumn LocalGrant;

        /// <summary>列20: 回転付与（0/1）</summary>
        public readonly CSVColumn GrantRotation;

        /// <summary>列21: 移動付与（0/1）</summary>
        public readonly CSVColumn GrantTranslation;

        /// <summary>列22: 付与率</summary>
        public readonly CSVColumn GrantRate;

        /// <summary>列23: 付与親ボーン名</summary>
        public readonly CSVColumn GrantParentName;

        /// <summary>列24: 軸制限（0/1）</summary>
        public readonly CSVColumn HasFixedAxis;

        /// <summary>列25: 制限軸X</summary>
        public readonly CSVColumn FixedAxisX;

        /// <summary>列26: 制限軸Y</summary>
        public readonly CSVColumn FixedAxisY;

        /// <summary>列27: 制限軸Z</summary>
        public readonly CSVColumn FixedAxisZ;

        /// <summary>列28: ローカル軸（0/1）</summary>
        public readonly CSVColumn HasLocalAxis;

        /// <summary>列29: ローカルX軸X</summary>
        public readonly CSVColumn LocalAxisXX;

        /// <summary>列30: ローカルX軸Y</summary>
        public readonly CSVColumn LocalAxisXY;

        /// <summary>列31: ローカルX軸Z</summary>
        public readonly CSVColumn LocalAxisXZ;

        /// <summary>列32: ローカルZ軸X</summary>
        public readonly CSVColumn LocalAxisZX;

        /// <summary>列33: ローカルZ軸Y</summary>
        public readonly CSVColumn LocalAxisZY;

        /// <summary>列34: ローカルZ軸Z</summary>
        public readonly CSVColumn LocalAxisZZ;

        /// <summary>列35: 外部親（0/1）</summary>
        public readonly CSVColumn HasExternalParent;

        /// <summary>列36: 外部親Key</summary>
        public readonly CSVColumn ExternalParentKey;

        /// <summary>列37: IKターゲットボーン名</summary>
        public readonly CSVColumn IKTargetName;

        /// <summary>列38: IKループ回数</summary>
        public readonly CSVColumn IKLoopCount;

        /// <summary>列39: IK単位角（度）</summary>
        public readonly CSVColumn IKLimitAngleDeg;

        // =====================================================================
        // 定数
        // =====================================================================

        /// <summary>行タイプ識別子</summary>
        public const string RowTypeValue = "PmxBone";

        /// <summary>最低限必要な列数（旧14カラム形式も許容）</summary>
        public override int MinimumFieldCount => 14;

        /// <summary>拡張形式の列数</summary>
        public const int ExtendedFieldCount = 40;

        /// <summary>データ行のプレフィックス</summary>
        public override string DataRowPrefix => RowTypeValue;

        // =====================================================================
        // コンストラクタ
        // =====================================================================

        public PmxBoneCSVSchema()
        {
            // 基本14カラム（旧形式と共通）
            RowType          = RegisterColumn(new CSVColumn(0,  "RowType", RowTypeValue));
            BoneName         = RegisterColumn(new CSVColumn(1,  "ボーン名"));
            BoneNameEn       = RegisterColumn(new CSVColumn(2,  "英名"));
            DeformHierarchy  = RegisterColumn(new CSVColumn(3,  "変形階層", "0"));
            PhysicsAfter     = RegisterColumn(new CSVColumn(4,  "物理後", "0"));
            PositionX        = RegisterColumn(new CSVColumn(5,  "位置X", "0"));
            PositionY        = RegisterColumn(new CSVColumn(6,  "位置Y", "0"));
            PositionZ        = RegisterColumn(new CSVColumn(7,  "位置Z", "0"));
            CanRotate        = RegisterColumn(new CSVColumn(8,  "回転", "1"));
            CanMove          = RegisterColumn(new CSVColumn(9,  "移動", "0"));
            IsIK             = RegisterColumn(new CSVColumn(10, "IK", "0"));
            IsVisible        = RegisterColumn(new CSVColumn(11, "表示", "1"));
            IsControllable   = RegisterColumn(new CSVColumn(12, "操作", "1"));
            ParentName       = RegisterColumn(new CSVColumn(13, "親ボーン名"));

            // 拡張26カラム（PMXEditor互換、列14-39）
            ConnectType      = RegisterColumn(new CSVColumn(14, "表示先", "0"));
            ConnectBoneName  = RegisterColumn(new CSVColumn(15, "表示先ボーン名"));
            ConnectOffsetX   = RegisterColumn(new CSVColumn(16, "表示先オフセットX", "0"));
            ConnectOffsetY   = RegisterColumn(new CSVColumn(17, "表示先オフセットY", "0"));
            ConnectOffsetZ   = RegisterColumn(new CSVColumn(18, "表示先オフセットZ", "0"));
            LocalGrant       = RegisterColumn(new CSVColumn(19, "ローカル付与", "0"));
            GrantRotation    = RegisterColumn(new CSVColumn(20, "回転付与", "0"));
            GrantTranslation = RegisterColumn(new CSVColumn(21, "移動付与", "0"));
            GrantRate        = RegisterColumn(new CSVColumn(22, "付与率", "0"));
            GrantParentName  = RegisterColumn(new CSVColumn(23, "付与親名"));
            HasFixedAxis     = RegisterColumn(new CSVColumn(24, "軸制限", "0"));
            FixedAxisX       = RegisterColumn(new CSVColumn(25, "制限軸X", "0"));
            FixedAxisY       = RegisterColumn(new CSVColumn(26, "制限軸Y", "0"));
            FixedAxisZ       = RegisterColumn(new CSVColumn(27, "制限軸Z", "0"));
            HasLocalAxis     = RegisterColumn(new CSVColumn(28, "ローカル軸", "0"));
            LocalAxisXX      = RegisterColumn(new CSVColumn(29, "ローカルX軸X", "1"));
            LocalAxisXY      = RegisterColumn(new CSVColumn(30, "ローカルX軸Y", "0"));
            LocalAxisXZ      = RegisterColumn(new CSVColumn(31, "ローカルX軸Z", "0"));
            LocalAxisZX      = RegisterColumn(new CSVColumn(32, "ローカルZ軸X", "0"));
            LocalAxisZY      = RegisterColumn(new CSVColumn(33, "ローカルZ軸Y", "0"));
            LocalAxisZZ      = RegisterColumn(new CSVColumn(34, "ローカルZ軸Z", "1"));
            HasExternalParent= RegisterColumn(new CSVColumn(35, "外部親", "0"));
            ExternalParentKey= RegisterColumn(new CSVColumn(36, "外部親Key", "0"));
            IKTargetName     = RegisterColumn(new CSVColumn(37, "IKTarget名"));
            IKLoopCount      = RegisterColumn(new CSVColumn(38, "IKLoop", "0"));
            IKLimitAngleDeg  = RegisterColumn(new CSVColumn(39, "IK単位角", "0"));
        }

        // =====================================================================
        // データ取得ヘルパー
        // =====================================================================

        /// <summary>行から位置ベクトルを取得</summary>
        public Vector3 GetPosition(CSVRow row)
        {
            return new Vector3(
                row.GetFloat(PositionX),
                row.GetFloat(PositionY),
                row.GetFloat(PositionZ));
        }

        /// <summary>行から表示先オフセットを取得</summary>
        public Vector3 GetConnectOffset(CSVRow row)
        {
            return new Vector3(
                row.GetFloat(ConnectOffsetX),
                row.GetFloat(ConnectOffsetY),
                row.GetFloat(ConnectOffsetZ));
        }

        /// <summary>行から軸固定方向を取得</summary>
        public Vector3 GetFixedAxis(CSVRow row)
        {
            return new Vector3(
                row.GetFloat(FixedAxisX),
                row.GetFloat(FixedAxisY),
                row.GetFloat(FixedAxisZ));
        }

        /// <summary>行からローカルX軸方向を取得</summary>
        public Vector3 GetLocalAxisX(CSVRow row)
        {
            return new Vector3(
                row.GetFloat(LocalAxisXX),
                row.GetFloat(LocalAxisXY),
                row.GetFloat(LocalAxisXZ));
        }

        /// <summary>行からローカルZ軸方向を取得</summary>
        public Vector3 GetLocalAxisZ(CSVRow row)
        {
            return new Vector3(
                row.GetFloat(LocalAxisZX),
                row.GetFloat(LocalAxisZY),
                row.GetFloat(LocalAxisZZ));
        }

        /// <summary>拡張カラム（列14以降）が存在するか判定</summary>
        public bool HasExtendedColumns(CSVRow row)
        {
            return row.FieldCount >= ExtendedFieldCount;
        }

        /// <summary>行がルートボーンかどうか判定</summary>
        public bool IsRootBone(CSVRow row)
        {
            return string.IsNullOrEmpty(row.Get(ParentName));
        }

        /// <summary>ヘッダーコメント行を生成（40カラム版）</summary>
        public string GenerateHeaderComment()
        {
            return ";PmxBone,\"ボーン名\",\"英名\",変形階層,物理後,位置X,位置Y,位置Z," +
                "回転,移動,IK,表示,操作,\"親ボーン名\"," +
                "表示先(0:オフセット/1:ボーン),\"表示先ボーン名\"," +
                "表示先オフセットX,表示先オフセットY,表示先オフセットZ," +
                "ローカル付与,回転付与,移動付与,付与率,\"付与親名\"," +
                "軸制限,制限軸X,制限軸Y,制限軸Z," +
                "ローカル軸,ローカルX軸X,ローカルX軸Y,ローカルX軸Z," +
                "ローカルZ軸X,ローカルZ軸Y,ローカルZ軸Z," +
                "外部親,外部親Key,\"IKTarget名\",IKLoop,IK単位角[deg]";
        }

        /// <summary>ヘッダーコメント行を生成（旧14カラム版、後方互換）</summary>
        public string GenerateBasicHeaderComment()
        {
            return ";PmxBone,\"ボーン名\",\"英名\",変形階層,物理後,位置X,位置Y,位置Z,回転,移動,IK,表示,操作,\"親ボーン名\"";
        }
    }
}
