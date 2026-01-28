// HumanoidBoneMapping.cs
// Unity Humanoid Avatar用のボーンマッピングデータ
// モデルごとに保持し、Avatar作成時に使用

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>
    /// Humanoidボーンマッピング
    /// Unity Humanoid名 → ボーンインデックス（MeshContextListのインデックス）
    /// </summary>
    [Serializable]
    public class HumanoidBoneMapping
    {
        // ================================================================
        // 静的データ: Humanoidボーン定義
        // ================================================================

        /// <summary>
        /// 全Humanoidボーン名（55個）
        /// HumanBodyBones列挙体の順序に対応
        /// </summary>
        public static readonly string[] AllHumanoidBones = new string[]
        {
            // 体幹 (0-4)
            "Hips",
            "Spine",
            "Chest",
            "UpperChest",
            "Neck",
            
            // 頭 (5-8)
            "Head",
            "LeftEye",
            "RightEye",
            "Jaw",
            
            // 左腕 (9-14)
            "LeftShoulder",
            "LeftUpperArm",
            "LeftLowerArm",
            "LeftHand",
            
            // 右腕 (14-17)
            "RightShoulder",
            "RightUpperArm",
            "RightLowerArm",
            "RightHand",
            
            // 左脚 (18-22)
            "LeftUpperLeg",
            "LeftLowerLeg",
            "LeftFoot",
            "LeftToes",
            
            // 右脚 (23-27)
            "RightUpperLeg",
            "RightLowerLeg",
            "RightFoot",
            "RightToes",
            
            // 左手指 (28-42)
            "Left Thumb Proximal",
            "Left Thumb Intermediate",
            "Left Thumb Distal",
            "Left Index Proximal",
            "Left Index Intermediate",
            "Left Index Distal",
            "Left Middle Proximal",
            "Left Middle Intermediate",
            "Left Middle Distal",
            "Left Ring Proximal",
            "Left Ring Intermediate",
            "Left Ring Distal",
            "Left Little Proximal",
            "Left Little Intermediate",
            "Left Little Distal",
            
            // 右手指 (43-57)
            "Right Thumb Proximal",
            "Right Thumb Intermediate",
            "Right Thumb Distal",
            "Right Index Proximal",
            "Right Index Intermediate",
            "Right Index Distal",
            "Right Middle Proximal",
            "Right Middle Intermediate",
            "Right Middle Distal",
            "Right Ring Proximal",
            "Right Ring Intermediate",
            "Right Ring Distal",
            "Right Little Proximal",
            "Right Little Intermediate",
            "Right Little Distal"
        };

        /// <summary>
        /// 必須ボーン（これがないとHumanoid Avatarは作成できない）
        /// </summary>
        public static readonly HashSet<string> RequiredBones = new HashSet<string>
        {
            "Hips",
            "Spine",
            "Chest",
            "Neck",
            "Head",
            "LeftUpperArm",
            "LeftLowerArm",
            "LeftHand",
            "RightUpperArm",
            "RightLowerArm",
            "RightHand",
            "LeftUpperLeg",
            "LeftLowerLeg",
            "LeftFoot",
            "RightUpperLeg",
            "RightLowerLeg",
            "RightFoot"
        };

        /// <summary>
        /// PMX標準ボーン名 → Unity Humanoid名 のデフォルトマッピング
        /// 注意: PMXの「下半身」と「上半身」は兄弟関係（両方とも「センター」の子）
        /// Unity Humanoidでは Hips → Spine の親子関係が必要なため、
        /// 「センター」をHipsに、「上半身」をSpineにマッピングする
        /// 「下半身」はHumanoidには含めない（足の回転制御用ボーン）
        /// </summary>
        public static readonly Dictionary<string, string> DefaultPMXMapping = new Dictionary<string, string>
        {
            // 体幹
            // 「センター」をHipsに（「下半身」ではなく）
            // これにより「上半身」(Spine)が正しく子孫になる
            { "センター", "Hips" },
            { "グルーブ", "Hips" },      // センターがない場合の代替
            { "全ての親", "Hips" },      // 全ての親をルートとする場合
            // { "下半身", "Hips" },     // 除外: 上半身が下半身の子孫でないためエラーになる
            { "上半身", "Spine" },
            { "上半身2", "Chest" },
            { "上半身3", "UpperChest" },
            { "首", "Neck" },
            { "頭", "Head" },
            
            // 目・顎
            { "左目", "LeftEye" },
            { "右目", "RightEye" },
            { "あご", "Jaw" },
            
            // 左腕
            { "左肩", "LeftShoulder" },
            { "左腕", "LeftUpperArm" },
            { "左ひじ", "LeftLowerArm" },
            { "左手首", "LeftHand" },
            
            // 右腕
            { "右肩", "RightShoulder" },
            { "右腕", "RightUpperArm" },
            { "右ひじ", "RightLowerArm" },
            { "右手首", "RightHand" },
            
            // 左脚
            { "左足", "LeftUpperLeg" },
            { "左ひざ", "LeftLowerLeg" },
            { "左足首", "LeftFoot" },
            { "左つま先", "LeftToes" },
            
            // 右脚
            { "右足", "RightUpperLeg" },
            { "右ひざ", "RightLowerLeg" },
            { "右足首", "RightFoot" },
            { "右つま先", "RightToes" },
            
            // 左手指
            { "左親指０", "Left Thumb Proximal" },
            { "左親指１", "Left Thumb Intermediate" },
            { "左親指２", "Left Thumb Distal" },
            { "左人指１", "Left Index Proximal" },
            { "左人指２", "Left Index Intermediate" },
            { "左人指３", "Left Index Distal" },
            { "左中指１", "Left Middle Proximal" },
            { "左中指２", "Left Middle Intermediate" },
            { "左中指３", "Left Middle Distal" },
            { "左薬指１", "Left Ring Proximal" },
            { "左薬指２", "Left Ring Intermediate" },
            { "左薬指３", "Left Ring Distal" },
            { "左小指１", "Left Little Proximal" },
            { "左小指２", "Left Little Intermediate" },
            { "左小指３", "Left Little Distal" },
            
            // 右手指
            { "右親指０", "Right Thumb Proximal" },
            { "右親指１", "Right Thumb Intermediate" },
            { "右親指２", "Right Thumb Distal" },
            { "右人指１", "Right Index Proximal" },
            { "右人指２", "Right Index Intermediate" },
            { "右人指３", "Right Index Distal" },
            { "右中指１", "Right Middle Proximal" },
            { "右中指２", "Right Middle Intermediate" },
            { "右中指３", "Right Middle Distal" },
            { "右薬指１", "Right Ring Proximal" },
            { "右薬指２", "Right Ring Intermediate" },
            { "右薬指３", "Right Ring Distal" },
            { "右小指１", "Right Little Proximal" },
            { "右小指２", "Right Little Intermediate" },
            { "右小指３", "Right Little Distal" }
        };

        // ================================================================
        // インスタンスデータ
        // ================================================================

        /// <summary>
        /// Unity Humanoid名 → ボーンインデックス（MeshContextListのインデックス）
        /// -1 = 未割り当て
        /// </summary>
        [SerializeField]
        private Dictionary<string, int> _boneIndexMap = new Dictionary<string, int>();

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>マッピング数</summary>
        public int Count => _boneIndexMap.Count;

        /// <summary>マッピングが空か</summary>
        public bool IsEmpty => _boneIndexMap.Count == 0;

        /// <summary>内部辞書への読み取り専用アクセス</summary>
        public IReadOnlyDictionary<string, int> BoneIndexMap => _boneIndexMap;

        // ================================================================
        // 操作
        // ================================================================

        /// <summary>
        /// マッピングを設定
        /// </summary>
        /// <param name="humanoidBone">Unity Humanoidボーン名</param>
        /// <param name="boneIndex">MeshContextListのインデックス（-1で削除）</param>
        public void Set(string humanoidBone, int boneIndex)
        {
            if (string.IsNullOrEmpty(humanoidBone))
                return;

            if (boneIndex < 0)
            {
                _boneIndexMap.Remove(humanoidBone);
            }
            else
            {
                _boneIndexMap[humanoidBone] = boneIndex;
            }
        }

        /// <summary>
        /// マッピングを取得
        /// </summary>
        /// <param name="humanoidBone">Unity Humanoidボーン名</param>
        /// <returns>ボーンインデックス（未設定の場合-1）</returns>
        public int Get(string humanoidBone)
        {
            if (string.IsNullOrEmpty(humanoidBone))
                return -1;

            return _boneIndexMap.TryGetValue(humanoidBone, out int index) ? index : -1;
        }

        /// <summary>
        /// マッピングが存在するか確認
        /// </summary>
        public bool Has(string humanoidBone)
        {
            return !string.IsNullOrEmpty(humanoidBone) && _boneIndexMap.ContainsKey(humanoidBone);
        }

        /// <summary>
        /// 指定ボーンのマッピングを削除
        /// </summary>
        public void Clear(string humanoidBone)
        {
            if (!string.IsNullOrEmpty(humanoidBone))
            {
                _boneIndexMap.Remove(humanoidBone);
            }
        }

        /// <summary>
        /// 全マッピングをクリア
        /// </summary>
        public void ClearAll()
        {
            _boneIndexMap.Clear();
        }

        // ================================================================
        // 自動マッピング
        // ================================================================

        /// <summary>
        /// ボーン名からPMXデフォルトマッピングを使用して自動設定
        /// </summary>
        /// <param name="boneNames">ボーン名リスト（インデックス = MeshContextListのインデックス）</param>
        /// <returns>マッピングされたボーン数</returns>
        public int AutoMapFromPMX(IList<string> boneNames)
        {
            if (boneNames == null)
                return 0;

            int mappedCount = 0;

            for (int i = 0; i < boneNames.Count; i++)
            {
                string boneName = boneNames[i];
                if (string.IsNullOrEmpty(boneName))
                    continue;

                // PMX名 → Unity Humanoid名
                if (DefaultPMXMapping.TryGetValue(boneName, out string humanoidName))
                {
                    // 既にマッピングがある場合は上書きしない（最初に見つかったものを使用）
                    if (!_boneIndexMap.ContainsKey(humanoidName))
                    {
                        _boneIndexMap[humanoidName] = i;
                        mappedCount++;
                    }
                }
            }

            return mappedCount;
        }

        /// <summary>
        /// CSV形式のマッピングデータを読み込み
        /// 形式: UnityHumanoidName,BoneName
        /// </summary>
        /// <param name="csvLines">CSVの行リスト</param>
        /// <param name="boneNames">ボーン名リスト（インデックス = MeshContextListのインデックス）</param>
        /// <returns>マッピングされたボーン数</returns>
        public int LoadFromCSV(IList<string> csvLines, IList<string> boneNames)
        {
            if (csvLines == null || boneNames == null)
                return 0;

            // ボーン名 → インデックス のマップを作成
            var nameToIndex = new Dictionary<string, int>();
            for (int i = 0; i < boneNames.Count; i++)
            {
                if (!string.IsNullOrEmpty(boneNames[i]) && !nameToIndex.ContainsKey(boneNames[i]))
                {
                    nameToIndex[boneNames[i]] = i;
                }
            }

            int mappedCount = 0;
            bool isHeader = true;

            foreach (var line in csvLines)
            {
                // ヘッダー行をスキップ
                if (isHeader)
                {
                    isHeader = false;
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length < 2)
                    continue;

                string unityName = parts[0].Trim();
                string boneName = parts[1].Trim();

                if (string.IsNullOrEmpty(unityName) || string.IsNullOrEmpty(boneName))
                    continue;

                // ボーン名からインデックスを検索
                if (nameToIndex.TryGetValue(boneName, out int index))
                {
                    _boneIndexMap[unityName] = index;
                    mappedCount++;
                }
            }

            return mappedCount;
        }

        // ================================================================
        // 検証
        // ================================================================

        /// <summary>
        /// 必須ボーンが全てマッピングされているか確認
        /// </summary>
        /// <returns>不足している必須ボーン名のリスト</returns>
        public List<string> GetMissingRequiredBones()
        {
            var missing = new List<string>();

            foreach (var required in RequiredBones)
            {
                if (!_boneIndexMap.ContainsKey(required) || _boneIndexMap[required] < 0)
                {
                    missing.Add(required);
                }
            }

            return missing;
        }

        /// <summary>
        /// Humanoid Avatarを作成可能か（必須ボーンが揃っているか）
        /// </summary>
        public bool CanCreateAvatar => GetMissingRequiredBones().Count == 0;

        // ================================================================
        // UNDO用: Clone / CopyFrom
        // ================================================================

        /// <summary>
        /// 深いコピーを作成
        /// </summary>
        public HumanoidBoneMapping Clone()
        {
            var clone = new HumanoidBoneMapping();
            foreach (var kvp in _boneIndexMap)
            {
                clone._boneIndexMap[kvp.Key] = kvp.Value;
            }
            return clone;
        }

        /// <summary>
        /// 他のマッピングからデータをコピー
        /// </summary>
        public void CopyFrom(HumanoidBoneMapping other)
        {
            _boneIndexMap.Clear();
            if (other != null)
            {
                foreach (var kvp in other._boneIndexMap)
                {
                    _boneIndexMap[kvp.Key] = kvp.Value;
                }
            }
        }

        // ================================================================
        // シリアライズ用
        // ================================================================

        /// <summary>
        /// Dictionary形式で取得（シリアライズ用）
        /// </summary>
        public Dictionary<string, int> ToDictionary()
        {
            return new Dictionary<string, int>(_boneIndexMap);
        }

        /// <summary>
        /// Dictionary形式から復元（デシリアライズ用）
        /// </summary>
        public void FromDictionary(Dictionary<string, int> dict)
        {
            _boneIndexMap.Clear();
            if (dict != null)
            {
                foreach (var kvp in dict)
                {
                    _boneIndexMap[kvp.Key] = kvp.Value;
                }
            }
        }
    }
}
