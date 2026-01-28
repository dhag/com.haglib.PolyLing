// PmxBoneImporter.cs - PMX CSV形式からボーン情報をインポート
// PMXEditor形式のCSVに対応
//
// CSV列構成（PMXEditor形式）:
// [0]  PmxBone
// [1]  ボーン名
// [2]  ボーン名(英)
// [3]  変形階層
// [4]  物理後(0/1)
// [5]  位置_x
// [6]  位置_y
// [7]  位置_z
// [8]  回転(0/1)
// [9]  移動(0/1)
// [10] IK(0/1)
// [11] 表示(0/1)
// [12] 操作(0/1)
// [13] 親ボーン名
// [14] 表示先(0:オフセット/1:ボーン)
// [15] 表示先ボーン名
// [16] 表示先オフセット_x
// [17] 表示先オフセット_y
// [18] 表示先オフセット_z
// [19] ローカル付与(0/1)
// [20] 回転付与(0/1)
// [21] 移動付与(0/1)
// [22] 付与率
// [23] 付与親名
// [24] 軸制限(0/1)
// [25] 制限軸_x
// [26] 制限軸_y
// [27] 制限軸_z
// [28] ローカル軸(0/1)
// [29] ローカルX軸_x
// [30] ローカルX軸_y
// [31] ローカルX軸_z
// [32] ローカルZ軸_x
// [33] ローカルZ軸_y
// [34] ローカルZ軸_z
// [35] 外部親(0/1)
// [36] 外部親Key
// [37] IKTarget名
// [38] IKLoop
// [39] IK単位角[deg]

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMX CSV形式からボーン情報をインポート
    /// PMXEditor形式のCSVに対応
    /// </summary>
    public static class PmxBoneImporter
    {
        // ================================================================
        // CSV列インデックス定数（PMXEditor形式）
        // ================================================================

        private const int COL_BONE_NAME = 1;
        private const int COL_BONE_NAME_EN = 2;
        private const int COL_TRANSFORM_LEVEL = 3;
        private const int COL_AFTER_PHYSICS = 4;
        private const int COL_POS_X = 5;
        private const int COL_POS_Y = 6;
        private const int COL_POS_Z = 7;
        private const int COL_ROTATABLE = 8;
        private const int COL_MOVABLE = 9;
        private const int COL_IS_IK = 10;
        private const int COL_VISIBLE = 11;
        private const int COL_CONTROLLABLE = 12;
        private const int COL_PARENT_NAME = 13;
        private const int COL_CONNECT_TYPE = 14;
        private const int COL_CONNECT_BONE_NAME = 15;
        private const int COL_CONNECT_OFFSET_X = 16;
        private const int COL_CONNECT_OFFSET_Y = 17;
        private const int COL_CONNECT_OFFSET_Z = 18;
        private const int COL_LOCAL_GRANT = 19;
        private const int COL_ROTATION_GRANT = 20;
        private const int COL_TRANSLATION_GRANT = 21;
        private const int COL_GRANT_RATE = 22;
        private const int COL_GRANT_PARENT_NAME = 23;
        private const int COL_FIXED_AXIS = 24;
        private const int COL_FIXED_AXIS_X = 25;
        private const int COL_FIXED_AXIS_Y = 26;
        private const int COL_FIXED_AXIS_Z = 27;
        private const int COL_LOCAL_AXIS = 28;
        private const int COL_LOCAL_X_X = 29;
        private const int COL_LOCAL_X_Y = 30;
        private const int COL_LOCAL_X_Z = 31;
        private const int COL_LOCAL_Z_X = 32;
        private const int COL_LOCAL_Z_Y = 33;
        private const int COL_LOCAL_Z_Z = 34;
        private const int COL_EXTERNAL_PARENT = 35;
        private const int COL_EXTERNAL_PARENT_KEY = 36;
        private const int COL_IK_TARGET_NAME = 37;
        private const int COL_IK_LOOP = 38;
        private const int COL_IK_LIMIT_ANGLE = 39;

        private const int MIN_COLUMNS = 40;

        // ================================================================
        // 座標変換設定
        // ================================================================

        /// <summary>
        /// 座標変換（PMX -> Unity）設定
        /// </summary>
        [Serializable]
        public struct AxisMap
        {
            public bool flipX;
            public bool flipY;
            public bool flipZ;
            public float scale;

            /// <summary>PMX→Unity変換のデフォルト設定（Z反転）</summary>
            public static AxisMap Default => new AxisMap { flipX = false, flipY = false, flipZ = true, scale = 1f };

            /// <summary>変換なし</summary>
            public static AxisMap Identity => new AxisMap { flipX = false, flipY = false, flipZ = false, scale = 1f };

            public Vector3 MapPosition(Vector3 v)
            {
                v *= scale;
                if (flipX) v.x = -v.x;
                if (flipY) v.y = -v.y;
                if (flipZ) v.z = -v.z;
                return v;
            }

            public Vector3 MapDirection(Vector3 v)
            {
                if (flipX) v.x = -v.x;
                if (flipY) v.y = -v.y;
                if (flipZ) v.z = -v.z;
                return v;
            }

            // 後方互換性のためのエイリアス
            public Vector3 Map(Vector3 v) => MapPosition(v);
        }

        // ================================================================
        // CSVボーンデータ
        // ================================================================

        /// <summary>
        /// CSVから読み込んだPMXボーン情報
        /// </summary>
        public sealed class CsvBone
        {
            public string Name;
            public string NameEnglish;
            public string ParentName;
            public Vector3 Position;
            public int TransformLevel;

            public bool HasLocalAxis;
            public Vector3 LocalAxisX;
            public Vector3 LocalAxisZ;

            public bool HasFixedAxis;
            public Vector3 FixedAxis;

            public int ConnectType;  // 0:オフセット, 1:ボーン
            public string ConnectBoneName;
            public Vector3 ConnectOffset;

            public bool HasGrant;
            public string GrantParentName;
            public float GrantRate;

            public bool IsIK;
            public string IKTargetName;
            public int IKLoop;
            public float IKLimitAngle;
        }

        // 後方互換性のためのエイリアス
        public sealed class PmxBone
        {
            public string name;
            public string parentName;
            public Vector3 pos;
            public bool hasLocalAxis;
            public Vector3 localX;
            public Vector3 localZ;

            public static PmxBone FromCsvBone(CsvBone csv)
            {
                return new PmxBone
                {
                    name = csv.Name,
                    parentName = csv.ParentName,
                    pos = csv.Position,
                    hasLocalAxis = csv.HasLocalAxis,
                    localX = csv.LocalAxisX,
                    localZ = csv.LocalAxisZ
                };
            }
        }

        // ================================================================
        // CSVパース
        // ================================================================

        /// <summary>
        /// CSV行からボーン情報をパース
        /// </summary>
        /// <param name="line">CSV行</param>
        /// <returns>パース結果、失敗時はnull</returns>
        public static CsvBone ParseCsvLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            if (!line.StartsWith("PmxBone", StringComparison.OrdinalIgnoreCase)) return null;

            var cols = SplitCsv(line);
            if (cols.Count < MIN_COLUMNS) return null;

            var bone = new CsvBone
            {
                Name = Unquote(cols[COL_BONE_NAME]),
                NameEnglish = Unquote(cols[COL_BONE_NAME_EN]),
                ParentName = Unquote(cols[COL_PARENT_NAME]),
                TransformLevel = ParseInt(cols[COL_TRANSFORM_LEVEL]),
                Position = new Vector3(
                    ParseFloat(cols[COL_POS_X]),
                    ParseFloat(cols[COL_POS_Y]),
                    ParseFloat(cols[COL_POS_Z])
                ),

                HasLocalAxis = ParseInt(cols[COL_LOCAL_AXIS]) != 0,
                LocalAxisX = new Vector3(
                    ParseFloat(cols[COL_LOCAL_X_X]),
                    ParseFloat(cols[COL_LOCAL_X_Y]),
                    ParseFloat(cols[COL_LOCAL_X_Z])
                ),
                LocalAxisZ = new Vector3(
                    ParseFloat(cols[COL_LOCAL_Z_X]),
                    ParseFloat(cols[COL_LOCAL_Z_Y]),
                    ParseFloat(cols[COL_LOCAL_Z_Z])
                ),

                HasFixedAxis = ParseInt(cols[COL_FIXED_AXIS]) != 0,
                FixedAxis = new Vector3(
                    ParseFloat(cols[COL_FIXED_AXIS_X]),
                    ParseFloat(cols[COL_FIXED_AXIS_Y]),
                    ParseFloat(cols[COL_FIXED_AXIS_Z])
                ),

                ConnectType = ParseInt(cols[COL_CONNECT_TYPE]),
                ConnectBoneName = Unquote(cols[COL_CONNECT_BONE_NAME]),
                ConnectOffset = new Vector3(
                    ParseFloat(cols[COL_CONNECT_OFFSET_X]),
                    ParseFloat(cols[COL_CONNECT_OFFSET_Y]),
                    ParseFloat(cols[COL_CONNECT_OFFSET_Z])
                ),

                HasGrant = ParseInt(cols[COL_ROTATION_GRANT]) != 0 || ParseInt(cols[COL_TRANSLATION_GRANT]) != 0,
                GrantParentName = Unquote(cols[COL_GRANT_PARENT_NAME]),
                GrantRate = ParseFloat(cols[COL_GRANT_RATE]),

                IsIK = ParseInt(cols[COL_IS_IK]) != 0,
                IKTargetName = Unquote(cols[COL_IK_TARGET_NAME]),
                IKLoop = ParseInt(cols[COL_IK_LOOP]),
                IKLimitAngle = ParseFloat(cols[COL_IK_LIMIT_ANGLE])
            };

            // ローカル軸がない場合のデフォルト値
            if (!bone.HasLocalAxis)
            {
                bone.LocalAxisX = Vector3.right;
                bone.LocalAxisZ = Vector3.forward;
            }

            return bone;
        }

        /// <summary>
        /// 後方互換: 旧形式でのCSVパース
        /// </summary>
        public static PmxBone ParsePmxBoneCsvLine(string line)
        {
            var csv = ParseCsvLine(line);
            return csv != null ? PmxBone.FromCsvBone(csv) : null;
        }

        /// <summary>
        /// 複数のCSV行からボーンリストをパース
        /// </summary>
        public static List<CsvBone> ParseCsvLines(IEnumerable<string> lines)
        {
            var bones = new List<CsvBone>();
            foreach (var line in lines)
            {
                var bone = ParseCsvLine(line);
                if (bone != null)
                {
                    bones.Add(bone);
                }
            }
            return bones;
        }

        // ================================================================
        // Unity Transform構築
        // ================================================================

        /// <summary>
        /// CSVボーンからUnityボーン階層を構築
        /// </summary>
        /// <param name="meshRoot">ボーン階層のルートTransform</param>
        /// <param name="csvBones">CSVから読み込んだボーンリスト</param>
        /// <param name="map">座標変換設定</param>
        /// <returns>ボーン名→Transformの辞書</returns>
        public static Dictionary<string, Transform> BuildUnityBones(
            Transform meshRoot,
            IEnumerable<CsvBone> csvBones,
            AxisMap map)
        {
            var bones = csvBones.Where(x => x != null).ToList();
            var byName = bones.ToDictionary(x => x.Name, x => x);
            var tfByName = new Dictionary<string, Transform>(bones.Count);

            // フェーズ1: 全Transformを作成
            foreach (var b in bones)
            {
                var go = new GameObject(b.Name);
                tfByName[b.Name] = go.transform;
            }

            // フェーズ2: 親子関係を設定
            foreach (var b in bones)
            {
                var tf = tfByName[b.Name];

                if (!string.IsNullOrEmpty(b.ParentName) && tfByName.TryGetValue(b.ParentName, out var parentTf))
                {
                    tf.SetParent(parentTf, worldPositionStays: false);
                }
                else
                {
                    tf.SetParent(meshRoot, worldPositionStays: false);
                }
            }

            // フェーズ3: モデル空間回転を計算
            var rotModel = new Dictionary<string, Quaternion>(bones.Count);
            foreach (var b in bones)
            {
                Quaternion rot = CalculateModelSpaceRotation(b, byName, map);
                rotModel[b.Name] = rot;
            }

            // フェーズ4: 位置とローカル回転を設定
            foreach (var b in bones)
            {
                var tf = tfByName[b.Name];

                // 位置（親からの相対）
                Vector3 posModel = map.MapPosition(b.Position);
                Vector3 parentPosModel = Vector3.zero;

                if (!string.IsNullOrEmpty(b.ParentName) && byName.TryGetValue(b.ParentName, out var parentBone))
                {
                    parentPosModel = map.MapPosition(parentBone.Position);
                }

                tf.localPosition = posModel - parentPosModel;

                // 回転（親からの相対）
                Quaternion myRot = rotModel[b.Name];

                if (!string.IsNullOrEmpty(b.ParentName) && rotModel.TryGetValue(b.ParentName, out var parentRot))
                {
                    tf.localRotation = Quaternion.Inverse(parentRot) * myRot;
                }
                else
                {
                    tf.localRotation = myRot;
                }
            }

            return tfByName;
        }

        /// <summary>
        /// 後方互換: 旧PmxBone形式でのボーン構築
        /// </summary>
        public static Dictionary<string, Transform> BuildUnityBones(
            Transform meshRoot,
            IEnumerable<PmxBone> pmxBones,
            AxisMap map)
        {
            var csvBones = pmxBones.Where(x => x != null).Select(p => new CsvBone
            {
                Name = p.name,
                ParentName = p.parentName,
                Position = p.pos,
                HasLocalAxis = p.hasLocalAxis,
                LocalAxisX = p.localX,
                LocalAxisZ = p.localZ
            });

            return BuildUnityBones(meshRoot, csvBones, map);
        }

        /// <summary>
        /// BindPosesを計算
        /// </summary>
        public static Matrix4x4[] ComputeBindPoses(Transform meshRoot, Transform[] bones)
        {
            var bindPoses = new Matrix4x4[bones.Length];
            var meshRootL2W = meshRoot.localToWorldMatrix;

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                {
                    bindPoses[i] = bones[i].worldToLocalMatrix * meshRootL2W;
                }
                else
                {
                    bindPoses[i] = Matrix4x4.identity;
                }
            }

            return bindPoses;
        }

        // ================================================================
        // 内部処理
        // ================================================================

        /// <summary>
        /// ボーンのモデル空間回転を計算
        /// </summary>
        private static Quaternion CalculateModelSpaceRotation(
            CsvBone bone,
            Dictionary<string, CsvBone> allBones,
            AxisMap map)
        {
            Vector3 localX, localZ;

            if (bone.HasLocalAxis)
            {
                localX = bone.LocalAxisX;
                localZ = bone.LocalAxisZ;
            }
            else
            {
                // ローカル軸未定義時はデフォルト軸を計算
                localX = CalculateDefaultLocalAxisX(bone, allBones);
                localZ = Vector3.forward;
            }

            // 座標系変換
            localX = map.MapDirection(localX).normalized;
            localZ = map.MapDirection(localZ).normalized;

            // Y軸計算: Y = Z × X
            Vector3 localY = Vector3.Cross(localZ, localX);

            if (localY.sqrMagnitude < 1e-10f)
            {
                Debug.LogWarning($"[PmxBoneImporter] Bone '{bone.Name}' has degenerate local axis. Using default.");
                localY = Vector3.up;
                localZ = Vector3.Cross(localX, localY).normalized;
                localY = Vector3.Cross(localZ, localX).normalized;
            }
            else
            {
                localY = localY.normalized;
                localZ = Vector3.Cross(localX, localY).normalized;
            }

            // デバッグ警告: Y軸が下向きの場合
            if (localY.y < -0.5f)
            {
                Debug.LogWarning($"[PmxBoneImporter] Bone '{bone.Name}' has downward Y axis (Y.y = {localY.y:F3}).");
            }

            return CreateRotationFromAxes(localX, localY, localZ);
        }

        /// <summary>
        /// デフォルトのローカルX軸を計算（ローカル軸未定義時）
        /// </summary>
        private static Vector3 CalculateDefaultLocalAxisX(CsvBone bone, Dictionary<string, CsvBone> allBones)
        {
            // 接続先ボーンがある場合
            if (bone.ConnectType == 1 && !string.IsNullOrEmpty(bone.ConnectBoneName))
            {
                if (allBones.TryGetValue(bone.ConnectBoneName, out var connectBone))
                {
                    Vector3 dir = connectBone.Position - bone.Position;
                    if (dir.sqrMagnitude > 1e-10f)
                    {
                        return dir.normalized;
                    }
                }
            }

            // 接続先オフセットがある場合
            if (bone.ConnectOffset.sqrMagnitude > 1e-10f)
            {
                return bone.ConnectOffset.normalized;
            }

            // 子ボーンを探す
            foreach (var other in allBones.Values)
            {
                if (other.ParentName == bone.Name)
                {
                    Vector3 dir = other.Position - bone.Position;
                    if (dir.sqrMagnitude > 1e-10f)
                    {
                        return dir.normalized;
                    }
                }
            }

            return Vector3.right;
        }

        /// <summary>
        /// 3軸からQuaternionを生成
        /// </summary>
        private static Quaternion CreateRotationFromAxes(Vector3 x, Vector3 y, Vector3 z)
        {
            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4(x.x, x.y, x.z, 0f));
            m.SetColumn(1, new Vector4(y.x, y.y, y.z, 0f));
            m.SetColumn(2, new Vector4(z.x, z.y, z.z, 0f));
            m.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            return m.rotation;
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        private static float ParseFloat(string s)
        {
            s = Unquote(s);
            if (string.IsNullOrEmpty(s)) return 0f;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
            return 0f;
        }

        private static int ParseInt(string s)
        {
            s = Unquote(s);
            if (string.IsNullOrEmpty(s)) return 0;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                return result;
            return 0;
        }

        private static string Unquote(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                return s.Substring(1, s.Length - 2).Replace("\"\"", "\"");
            return s;
        }

        private static List<string> SplitCsv(string line)
        {
            var result = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool inQ = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQ && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cur.Append('"');
                        i++;
                    }
                    else
                    {
                        inQ = !inQ;
                    }
                }
                else if (c == ',' && !inQ)
                {
                    result.Add(cur.ToString());
                    cur.Clear();
                }
                else
                {
                    cur.Append(c);
                }
            }

            result.Add(cur.ToString());
            return result;
        }

        // ================================================================
        // デバッグ
        // ================================================================

        /// <summary>
        /// ボーンのローカル軸情報をデバッグ出力
        /// </summary>
        public static void DebugPrintBoneAxes(IEnumerable<CsvBone> bones, AxisMap map)
        {
            Debug.Log("=== CSV Bone Axes Debug ===");

            foreach (var bone in bones)
            {
                if (!bone.HasLocalAxis) continue;

                Vector3 x = map.MapDirection(bone.LocalAxisX);
                Vector3 z = map.MapDirection(bone.LocalAxisZ);
                Vector3 y = Vector3.Cross(z, x).normalized;

                Debug.Log($"Bone: {bone.Name}");
                Debug.Log($"  LocalX: ({x.x:F4}, {x.y:F4}, {x.z:F4})");
                Debug.Log($"  LocalZ: ({z.x:F4}, {z.y:F4}, {z.z:F4})");
                Debug.Log($"  LocalY (Z×X): ({y.x:F4}, {y.y:F4}, {y.z:F4})");
                Debug.Log($"  Y.y sign: {(y.y >= 0 ? "+" : "-")} ({y.y:F4})");
            }
        }
    }
}
