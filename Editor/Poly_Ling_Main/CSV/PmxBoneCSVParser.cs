// Assets/Editor/Poly_Ling/PMX/CSV/PmxBoneCSVParser.cs
// =====================================================================
// PmxBone形式のCSVをパースしてボーン情報を読み取る
// PMXEditor 40カラム完全対応・旧14カラムも後方互換
// 
// 【40カラム: PMXEditor完全互換】
// PmxBone,"名","英名",変形階層,物理後,X,Y,Z,回転,移動,IK,表示,操作,"親名",
//   表示先,表示先ボーン名,オフセットXYZ,
//   ローカル付与,回転付与,移動付与,付与率,付与親名,
//   軸制限,制限軸XYZ,ローカル軸,ローカルX軸XYZ,ローカルZ軸XYZ,
//   外部親,外部親Key,IKTarget名,IKLoop,IK単位角[deg]
// 
// 【14カラム: 旧形式後方互換】
// PmxBone,"名","英名",変形階層,物理後,X,Y,Z,回転,移動,IK,表示,操作,"親名"
// =====================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Poly_Ling.CSV
{
    // =========================================================================
    // データクラス
    // =========================================================================

    /// <summary>
    /// PmxBoneデータ
    /// PMXEditor CSVの全40カラムを保持可能
    /// </summary>
    public class PmxBoneData
    {
        // --- 基本14カラム ---

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

        // --- 拡張カラム（PMXEditor互換） ---

        /// <summary>拡張カラムが存在するか（40カラムCSVから読み込まれたか）</summary>
        public bool HasExtendedData { get; set; }

        /// <summary>表示先タイプ（0:オフセット/1:ボーン）</summary>
        public int ConnectType { get; set; }

        /// <summary>表示先ボーン名</summary>
        public string ConnectBoneName { get; set; }

        /// <summary>表示先オフセット</summary>
        public Vector3 ConnectOffset { get; set; }

        /// <summary>ローカル付与（0/1）</summary>
        public bool LocalGrant { get; set; }

        /// <summary>回転付与（0/1）</summary>
        public bool GrantRotation { get; set; }

        /// <summary>移動付与（0/1）</summary>
        public bool GrantTranslation { get; set; }

        /// <summary>付与率</summary>
        public float GrantRate { get; set; }

        /// <summary>付与親ボーン名</summary>
        public string GrantParentName { get; set; }

        /// <summary>軸固定フラグ</summary>
        public bool HasFixedAxis { get; set; }

        /// <summary>軸固定方向</summary>
        public Vector3 FixedAxis { get; set; }

        /// <summary>ローカル軸フラグ</summary>
        public bool HasLocalAxis { get; set; }

        /// <summary>ローカルX軸方向</summary>
        public Vector3 LocalAxisX { get; set; }

        /// <summary>ローカルZ軸方向</summary>
        public Vector3 LocalAxisZ { get; set; }

        /// <summary>外部親フラグ</summary>
        public bool HasExternalParent { get; set; }

        /// <summary>外部親Key</summary>
        public int ExternalParentKey { get; set; }

        /// <summary>IKターゲットボーン名</summary>
        public string IKTargetName { get; set; }

        /// <summary>IKループ回数</summary>
        public int IKLoopCount { get; set; }

        /// <summary>IK単位角（度）</summary>
        public float IKLimitAngleDeg { get; set; }

        /// <summary>IKリンクリスト（PmxIKLink行から読み込み）</summary>
        public List<PmxIKLinkData> IKLinks { get; } = new List<PmxIKLinkData>();

        // --- ビットフラグ構築ヘルパー ---

        /// <summary>
        /// PMX Bone Flags ビットフィールドを構築
        /// PMXBone.Flags に設定可能な値を返す
        /// </summary>
        public int BuildPmxFlags()
        {
            int f = 0;
            if (ConnectType != 0)    f |= 0x0001; // 接続先:ボーン
            if (CanRotate)           f |= 0x0002;
            if (CanMove)             f |= 0x0004;
            if (IsVisible)           f |= 0x0008;
            if (IsControllable)      f |= 0x0010;
            if (IsIK)               f |= 0x0020;
            if (LocalGrant)          f |= 0x0080;
            if (GrantRotation)       f |= 0x0100;
            if (GrantTranslation)    f |= 0x0200;
            if (HasFixedAxis)        f |= 0x0400;
            if (HasLocalAxis)        f |= 0x0800;
            if (IsPhysicsAfter)      f |= 0x1000;
            if (HasExternalParent)   f |= 0x2000;
            return f;
        }
    }

    /// <summary>
    /// IKリンクデータ（PmxIKLink行）
    /// </summary>
    public class PmxIKLinkData
    {
        /// <summary>リンクボーン名</summary>
        public string BoneName { get; set; }

        /// <summary>角度制限あり</summary>
        public bool HasLimit { get; set; }

        /// <summary>角度制限下限（度）</summary>
        public Vector3 LimitMinDeg { get; set; }

        /// <summary>角度制限上限（度）</summary>
        public Vector3 LimitMaxDeg { get; set; }
    }

    // =========================================================================
    // パーサー
    // =========================================================================

    /// <summary>
    /// PmxBone CSVパーサー
    /// PMXEditor 40カラム完全対応・旧14カラム後方互換
    /// PmxIKLink行にも対応
    /// </summary>
    public static class PmxBoneCSVParser
    {
        private static readonly PmxBoneCSVSchema _schema = new PmxBoneCSVSchema();

        /// <summary>CSVファイルをパース</summary>
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

        /// <summary>CSV文字列をパース</summary>
        public static List<PmxBoneData> Parse(string content)
        {
            var bones = new List<PmxBoneData>();

            if (string.IsNullOrEmpty(content))
                return bones;

            var rows = CSVHelper.ParseString(content);

            foreach (var row in rows)
            {
                if (CSVHelper.IsCommentLine(row.OriginalLine))
                    continue;

                string rowType = row[0]?.Trim();

                if (rowType == PmxBoneCSVSchema.RowTypeValue)
                {
                    if (_schema.IsValidDataRow(row))
                    {
                        var bone = ParseBoneRow(row);
                        if (bone != null)
                            bones.Add(bone);
                    }
                }
                else if (rowType == "PmxIKLink")
                {
                    ParseIKLinkRow(row, bones);
                }
            }

            int extCount = 0;
            foreach (var b in bones)
                if (b.HasExtendedData) extCount++;

            Debug.Log($"[PmxBoneCSVParser] Parsed {bones.Count} bones ({extCount} with extended data)");
            return bones;
        }

        /// <summary>PmxBone行をパース</summary>
        private static PmxBoneData ParseBoneRow(CSVRow row)
        {
            try
            {
                var bone = new PmxBoneData
                {
                    // 基本14カラム
                    Name = row.Get(_schema.BoneName),
                    NameEn = row.Get(_schema.BoneNameEn),
                    DeformHierarchy = row.GetInt(_schema.DeformHierarchy, 0),
                    IsPhysicsAfter = row.GetBool(_schema.PhysicsAfter),
                    Position = _schema.GetPosition(row),
                    CanRotate = row.GetBool(_schema.CanRotate, true),
                    CanMove = row.GetBool(_schema.CanMove),
                    IsIK = row.GetBool(_schema.IsIK),
                    IsVisible = row.GetBool(_schema.IsVisible, true),
                    IsControllable = row.GetBool(_schema.IsControllable, true),
                    ParentName = row.Get(_schema.ParentName)
                };

                // 拡張カラム（40カラム形式の場合）
                if (_schema.HasExtendedColumns(row))
                {
                    bone.HasExtendedData = true;

                    bone.ConnectType = row.GetInt(_schema.ConnectType, 0);
                    bone.ConnectBoneName = row.Get(_schema.ConnectBoneName);
                    bone.ConnectOffset = _schema.GetConnectOffset(row);

                    bone.LocalGrant = row.GetBool(_schema.LocalGrant);
                    bone.GrantRotation = row.GetBool(_schema.GrantRotation);
                    bone.GrantTranslation = row.GetBool(_schema.GrantTranslation);
                    bone.GrantRate = row.GetFloat(_schema.GrantRate);
                    bone.GrantParentName = row.Get(_schema.GrantParentName);

                    bone.HasFixedAxis = row.GetBool(_schema.HasFixedAxis);
                    bone.FixedAxis = _schema.GetFixedAxis(row);

                    bone.HasLocalAxis = row.GetBool(_schema.HasLocalAxis);
                    bone.LocalAxisX = _schema.GetLocalAxisX(row);
                    bone.LocalAxisZ = _schema.GetLocalAxisZ(row);

                    bone.HasExternalParent = row.GetBool(_schema.HasExternalParent);
                    bone.ExternalParentKey = row.GetInt(_schema.ExternalParentKey);

                    bone.IKTargetName = row.Get(_schema.IKTargetName);
                    bone.IKLoopCount = row.GetInt(_schema.IKLoopCount);
                    bone.IKLimitAngleDeg = row.GetFloat(_schema.IKLimitAngleDeg);
                }

                return bone;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PmxBoneCSVParser] Failed to parse bone: {e.Message}\n{row.OriginalLine}");
                return null;
            }
        }

        /// <summary>PmxIKLink行をパースして最後のボーンに追加</summary>
        private static void ParseIKLinkRow(CSVRow row, List<PmxBoneData> bones)
        {
            // PmxIKLink,[1]親ボーン名,[2]Linkボーン名,[3]角度制限,[4]XL,[5]XH,[6]YL,[7]YH,[8]ZL,[9]ZH
            if (row.FieldCount < 4 || bones.Count == 0)
                return;

            string parentName = row[1];

            // 親ボーンを後ろから探す
            PmxBoneData parent = null;
            for (int i = bones.Count - 1; i >= 0; i--)
            {
                if (bones[i].Name == parentName)
                {
                    parent = bones[i];
                    break;
                }
            }
            if (parent == null) return;

            var link = new PmxIKLinkData
            {
                BoneName = row[2],
                HasLimit = row[3]?.Trim() == "1"
            };

            if (link.HasLimit && row.FieldCount >= 10)
            {
                float xl = ParseFloat(row[4]);
                float xh = ParseFloat(row[5]);
                float yl = ParseFloat(row[6]);
                float yh = ParseFloat(row[7]);
                float zl = ParseFloat(row[8]);
                float zh = ParseFloat(row[9]);
                link.LimitMinDeg = new Vector3(xl, yl, zl);
                link.LimitMaxDeg = new Vector3(xh, yh, zh);
            }

            parent.IKLinks.Add(link);
        }

        private static float ParseFloat(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float r);
            return r;
        }
    }
}
