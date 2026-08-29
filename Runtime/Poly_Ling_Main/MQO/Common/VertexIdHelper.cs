// Assets/Editor/Poly_Ling/MQO/Common/VertexIdHelper.cs
// =====================================================================
// 頂点ID操作ヘルパー
// 
// 【頂点IDの3つの用途】
// 1. 頂点位置操作用途
//    - IDで特定の頂点を識別し、位置・UV・法線などを操作
//    - 例: ID=100の頂点を移動
// 
// 2. データ欠損・ミス検出用途
//    - IDの欠損、重複、範囲外などをチェック
//    - 例: ID=50が2回出現している、ID=100-110が欠損している
// 
// 3. オブジェクト間同一頂点認識用途
//    - 異なるMQOオブジェクト間で同一頂点を関連付け
//    - 例: Body と Cloth で境界頂点を共有
// 
// 【ID転送パターン】
// - 重みCSV → MQO: CSVのVertexIDをMQOの特殊面に埋め込み
// - MQO → 重みCSV: MQOの特殊面からVertexIDを抽出してCSVに出力
// - 新規割り振り → MQO, CSV: 連番IDを生成してMQO・CSVに出力
// =====================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Ops;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// 頂点ID操作ヘルパー
    /// </summary>
    public static class VertexIdHelper
    {
        // =====================================================================
        // ID検証
        // =====================================================================

        /// <summary>
        /// 頂点ID検証結果
        /// </summary>
        public class ValidationResult
        {
            /// <summary>有効かどうか</summary>
            public bool IsValid => Errors.Count == 0;

            /// <summary>エラーリスト</summary>
            public List<string> Errors { get; } = new List<string>();

            /// <summary>警告リスト</summary>
            public List<string> Warnings { get; } = new List<string>();

            /// <summary>使用されているID一覧</summary>
            public HashSet<int> UsedIds { get; } = new HashSet<int>();

            /// <summary>重複しているID一覧</summary>
            public HashSet<int> DuplicateIds { get; } = new HashSet<int>();

            /// <summary>欠損しているID一覧（連番チェック時）</summary>
            public List<int> MissingIds { get; } = new List<int>();

            /// <summary>未設定（-1）の頂点数</summary>
            public int UnsetCount { get; set; }

            /// <summary>総頂点数</summary>
            public int TotalVertexCount { get; set; }

            public void AddError(string message) => Errors.Add(message);
            public void AddWarning(string message) => Warnings.Add(message);
        }

        /// <summary>
        /// 頂点IDの検証
        /// </summary>
        /// <param name="ids">頂点IDの配列（-1は未設定）</param>
        /// <param name="checkConsecutive">連番チェックを行うか</param>
        /// <param name="expectedStartId">連番チェック時の開始ID（デフォルト0）</param>
        /// <returns>検証結果</returns>
        public static ValidationResult ValidateIds(IEnumerable<int> ids, bool checkConsecutive = false, int expectedStartId = 0)
        {
            var result = new ValidationResult();
            var seenIds = new Dictionary<int, int>(); // ID → 出現回数
            int index = 0;

            foreach (var id in ids)
            {
                result.TotalVertexCount++;

                if (id < 0)
                {
                    // 未設定
                    result.UnsetCount++;
                }
                else
                {
                    // 有効なID
                    result.UsedIds.Add(id);

                    if (seenIds.ContainsKey(id))
                    {
                        seenIds[id]++;
                        result.DuplicateIds.Add(id);
                    }
                    else
                    {
                        seenIds[id] = 1;
                    }
                }

                index++;
            }

            // 重複エラー
            foreach (var dupId in result.DuplicateIds)
            {
                result.AddError($"頂点ID {dupId} が {seenIds[dupId]} 回重複しています");
            }

            // 連番チェック
            if (checkConsecutive && result.UsedIds.Count > 0)
            {
                int minId = result.UsedIds.Min();
                int maxId = result.UsedIds.Max();

                // 期待される開始IDと異なる場合は警告
                if (minId != expectedStartId)
                {
                    result.AddWarning($"頂点IDが {expectedStartId} からではなく {minId} から開始しています");
                }

                // 欠損チェック
                for (int id = minId; id <= maxId; id++)
                {
                    if (!result.UsedIds.Contains(id))
                    {
                        result.MissingIds.Add(id);
                    }
                }

                if (result.MissingIds.Count > 0)
                {
                    if (result.MissingIds.Count <= 10)
                    {
                        result.AddWarning($"頂点IDが欠損しています: {string.Join(", ", result.MissingIds)}");
                    }
                    else
                    {
                        result.AddWarning($"頂点IDが {result.MissingIds.Count} 件欠損しています " +
                            $"(例: {string.Join(", ", result.MissingIds.Take(5))}...)");
                    }
                }
            }

            // 部分的に未設定がある場合は警告
            if (result.UnsetCount > 0 && result.UnsetCount < result.TotalVertexCount)
            {
                result.AddWarning($"{result.TotalVertexCount} 頂点中 {result.UnsetCount} 頂点のIDが未設定です");
            }

            return result;
        }

        // =====================================================================
        // ID生成
        // =====================================================================

        /// <summary>
        /// 連番IDを生成
        /// </summary>
        /// <param name="count">生成する数</param>
        /// <param name="startId">開始ID（デフォルト0）</param>
        /// <returns>ID配列</returns>
        public static int[] GenerateSequentialIds(int count, int startId = 0)
        {
            var ids = new int[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = startId + i;
            }
            return ids;
        }

        /// <summary>
        /// 未使用のIDを取得（既存IDの最大値+1から開始）
        /// </summary>
        /// <param name="existingIds">既存のID一覧</param>
        /// <param name="count">必要な数</param>
        /// <returns>新規ID配列</returns>
        public static int[] GetUnusedIds(IEnumerable<int> existingIds, int count)
        {
            int maxId = existingIds.Any() ? existingIds.Max() : -1;
            return GenerateSequentialIds(count, maxId + 1);
        }

        // =====================================================================
        // ID変換・マッピング
        // =====================================================================

        /// <summary>
        /// ID→インデックスのマッピングを作成
        /// </summary>
        /// <param name="idsWithIndex">（インデックス, ID）のペア</param>
        /// <returns>ID → インデックス の辞書</returns>
        public static Dictionary<int, int> CreateIdToIndexMap(IEnumerable<(int index, int id)> idsWithIndex)
        {
            var map = new Dictionary<int, int>();

            foreach (var (index, id) in idsWithIndex)
            {
                if (id >= 0 && !map.ContainsKey(id))
                {
                    map[id] = index;
                }
            }

            return map;
        }

        /// <summary>
        /// インデックス→IDのマッピングを作成
        /// </summary>
        /// <param name="ids">インデックス順のID配列</param>
        /// <returns>インデックス → ID の辞書</returns>
        public static Dictionary<int, int> CreateIndexToIdMap(IReadOnlyList<int> ids)
        {
            var map = new Dictionary<int, int>();

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] >= 0)
                {
                    map[i] = ids[i];
                }
            }

            return map;
        }

        // =====================================================================
        // オブジェクト間のID共有
        // =====================================================================

        /// <summary>
        /// 共有頂点検出結果
        /// </summary>
        public class SharedVertexResult
        {
            /// <summary>共有されているID一覧</summary>
            public HashSet<int> SharedIds { get; } = new HashSet<int>();

            /// <summary>オブジェクト名 → そのオブジェクトが持つ共有ID</summary>
            public Dictionary<string, HashSet<int>> ObjectSharedIds { get; } = new Dictionary<string, HashSet<int>>();
        }

        /// <summary>
        /// 複数オブジェクト間で共有されている頂点IDを検出
        /// </summary>
        /// <param name="objectIds">オブジェクト名 → ID一覧 のマッピング</param>
        /// <returns>共有頂点検出結果</returns>
        public static SharedVertexResult FindSharedVertices(Dictionary<string, IEnumerable<int>> objectIds)
        {
            var result = new SharedVertexResult();
            var idToObjects = new Dictionary<int, List<string>>();

            // IDごとに、それを持つオブジェクトを収集
            foreach (var (objectName, ids) in objectIds)
            {
                foreach (var id in ids)
                {
                    if (id < 0) continue;

                    if (!idToObjects.ContainsKey(id))
                    {
                        idToObjects[id] = new List<string>();
                    }
                    idToObjects[id].Add(objectName);
                }
            }

            // 複数オブジェクトに出現するIDを共有IDとする
            foreach (var (id, objects) in idToObjects)
            {
                if (objects.Count > 1)
                {
                    result.SharedIds.Add(id);

                    foreach (var objName in objects)
                    {
                        if (!result.ObjectSharedIds.ContainsKey(objName))
                        {
                            result.ObjectSharedIds[objName] = new HashSet<int>();
                        }
                        result.ObjectSharedIds[objName].Add(id);
                    }
                }
            }

            return result;
        }

        // =====================================================================
        // ID転送（MQO ↔ CSV）
        // =====================================================================

        /// <summary>
        /// 三角形特殊面が運ぶ頂点の識別子一式。COL(PartsID, SubID, ID) の並びに対応する。
        /// </summary>
        public struct MqoVertexIds
        {
            /// <summary>頂点ID。COL[2]。</summary>
            public int Id;

            /// <summary>サブID（部品ローカルなインデックス）。COL[1]。0 = 未設定。</summary>
            public int SubId;

            /// <summary>部品ID。COL[0]。0 = 未設定。</summary>
            public int PartsId;
        }

        /// <summary>
        /// MQOの三角形特殊面から頂点の識別子一式を抽出する。
        ///
        /// 【仕様】
        ///   頂点インデックスが全て同じ三角形が存在し、かつ COL を持つとき、
        ///   その COL を (PartsID, SubID, ID) として読む。
        ///   COL の値によるパターン判定は行わない。
        ///
        ///   以前は COL(1 1 ID) / 白 / COL(1 ID ID) の 3 パターンだけを受理し、
        ///   一致しない面を捨てていたが、いずれの分岐も返す値は COL[2] と同値
        ///   （共有ID分岐は成立条件が COL[1]==COL[2]）だったため、
        ///   無条件に COL[2] を読んでも既存データの読み取り結果は変わらない。
        ///
        /// 【四角形特殊面】
        ///   ボーンウェイト用なのでここでは扱わない
        ///   （ExtractBoneWeightsFromSpecialFaces 参照）。
        /// </summary>
        /// <param name="faces">MQOの面リスト</param>
        /// <returns>頂点インデックス → 識別子一式 のマッピング</returns>
        public static Dictionary<int, MqoVertexIds> ExtractIdsFromSpecialFaces(IEnumerable<MQOFace> faces)
        {
            var vertexIdMap = new Dictionary<int, MqoVertexIds>();
            if (faces == null) return vertexIdMap;

            foreach (var face in faces)
            {
                // 三角形特殊面のみ対象（四角形はボーンウェイト用）
                if (!face.IsSpecialFace || face.VertexCount != 3)
                    continue;
                if (face.VertexColors == null || face.VertexColors.Length < 3)
                    continue;

                int partsId = (int)face.VertexColors[0];
                int subId   = (int)face.VertexColors[1];
                int id      = (int)face.VertexColors[2];

                // uint → int のキャストで負になるのは int.MaxValue を超えた不正値。
                // ID として使えないのでその面ごと捨てる。
                // SubID / PartsID は 0 も有効なので値では弾かない。
                int vertexIndex = face.VertexIndices[0];
                if (vertexIndex < 0 || id < 0)
                    continue;

                vertexIdMap[vertexIndex] = new MqoVertexIds
                {
                    Id      = id,
                    SubId   = subId,
                    PartsId = partsId,
                };
            }
            return vertexIdMap;
        }

        /// <summary>
        /// 頂点の識別子一式を埋め込むための三角形特殊面を生成。
        /// COL(PartsID, SubID, ID) の並びで書く。
        /// </summary>
        /// <param name="vertexIndex">頂点インデックス</param>
        /// <param name="vertexId">頂点ID（COL[2]）</param>
        /// <param name="subId">サブID（COL[1]）。0 = 未設定</param>
        /// <param name="partsId">部品ID（COL[0]）。0 = 未設定</param>
        /// <param name="materialIndex">マテリアルインデックス（デフォルト0）</param>
        /// <returns>特殊面データ</returns>
        /// <remarks>
        /// subId / partsId は既定値を持たせない。既存の呼び出しが
        /// 第3引数へ materialIndex を渡していたため、既定値を置くと
        /// それが subId に読み替えられて無言で壊れる。
        /// </remarks>
        public static MQOFace CreateSpecialFaceForVertexId(
            int vertexIndex, int vertexId, int subId, int partsId, int materialIndex = 0)
        {
            return new MQOFace
            {
                // 3頂点すべて同じインデックス（特殊面の印）
                VertexIndices = new int[] { vertexIndex, vertexIndex, vertexIndex },
                MaterialIndex = materialIndex,
                // UVは (0,0) で統一。
                // MQO の UV は F6 の float として出力されるため、
                // 2^24 を超える整数を往復できない。識別子は必ず COL に載せること。
                UVs = new Vector2[] { Vector2.zero, Vector2.zero, Vector2.zero },
                // COL属性: [PartsID, SubID, ID]
                VertexColors = new uint[] { (uint)partsId, (uint)subId, (uint)vertexId }
            };
        }

        // =====================================================================
        // ボーンウェイト特殊面（四角形）
        // =====================================================================

        /// <summary>
        /// MQOの四角形特殊面からボーンウェイトを抽出
        /// 四角形特殊面: 全頂点インデックスが同じ四角形面
        /// UV[0].x=weight0, UV[0].y=weight1, UV[1].x=weight2, UV[1].y=weight3
        /// UV[3].y=0:実体側, UV[3].y=1:ミラー側
        /// COL(boneIndex0, boneIndex1, boneIndex2, boneIndex3)
        /// </summary>
        /// <param name="faces">MQOの面リスト</param>
        /// <returns>頂点インデックス → ボーンウェイト情報 のマッピング（実体側のみ）</returns>
        public static Dictionary<int, BoneWeightData> ExtractBoneWeightsFromSpecialFaces(IEnumerable<MQOFace> faces)
        {
            var boneWeightMap = new Dictionary<int, BoneWeightData>();

            foreach (var face in faces)
            {
                // 四角形特殊面のみ対象
                if (!face.IsSpecialFace || face.VertexCount != 4)
                    continue;

                // UV属性とCOL属性が両方必要
                if (face.UVs == null || face.UVs.Length < 4)
                    continue;
                if (face.VertexColors == null || face.VertexColors.Length < 4)
                    continue;

                int vertexIndex = face.VertexIndices[0];//0-4のいずれも同じインデックスなので、どれか1つを使用して頂点を特定

                // UV[3].yでミラーフラグを判定（0=実体、1=ミラー）
                bool isMirror = face.UVs[3].y >= 0.5f;

                var boneWeight = new BoneWeightData
                {
                    BoneIndex0 = (int)face.VertexColors[0],
                    BoneIndex1 = (int)face.VertexColors[1],
                    BoneIndex2 = (int)face.VertexColors[2],
                    BoneIndex3 = (int)face.VertexColors[3],
                    Weight0 = face.UVs[0].x,
                    Weight1 = face.UVs[0].y,
                    Weight2 = face.UVs[1].x,
                    Weight3 = face.UVs[1].y,
                    IsMirror = isMirror
                };

                // 実体側のみ返す（ミラー側は別途ExtractMirrorBoneWeightsで取得）
                if (!isMirror)
                {
                    boneWeightMap[vertexIndex] = boneWeight;
                }
            }

            return boneWeightMap;
        }

        /// <summary>
        /// MQOの四角形特殊面からミラー側ボーンウェイトを抽出
        /// </summary>
        /// <param name="faces">MQOの面リスト</param>
        /// <returns>頂点インデックス → ミラー側ボーンウェイト情報 のマッピング</returns>
        public static Dictionary<int, BoneWeightData> ExtractMirrorBoneWeightsFromSpecialFaces(IEnumerable<MQOFace> faces)
        {
            var boneWeightMap = new Dictionary<int, BoneWeightData>();

            foreach (var face in faces)
            {
                // 四角形特殊面のみ対象
                if (!face.IsSpecialFace || face.VertexCount != 4)
                    continue;

                // UV属性とCOL属性が必要
                if (face.UVs == null || face.UVs.Length < 4)
                    continue;
                if (face.VertexColors == null || face.VertexColors.Length < 4)
                    continue;

                int vertexIndex = face.VertexIndices[0];

                // UV[3].yでミラーフラグを判定（0=実体、1=ミラー）
                bool isMirror = face.UVs[3].y >= 0.5f;

                if (!isMirror)
                    continue;

                var boneWeight = new BoneWeightData
                {
                    BoneIndex0 = (int)face.VertexColors[0],
                    BoneIndex1 = (int)face.VertexColors[1],
                    BoneIndex2 = (int)face.VertexColors[2],
                    BoneIndex3 = (int)face.VertexColors[3],
                    Weight0 = face.UVs[0].x,
                    Weight1 = face.UVs[0].y,
                    Weight2 = face.UVs[1].x,
                    Weight3 = face.UVs[1].y,
                    IsMirror = true
                };

                boneWeightMap[vertexIndex] = boneWeight;
            }

            return boneWeightMap;
        }

        /// <summary>
        /// ボーンウェイトを埋め込むための四角形特殊面を生成
        /// </summary>
        /// <param name="vertexIndex">頂点インデックス</param>
        /// <param name="boneWeight">ボーンウェイト情報</param>
        /// <param name="isMirror">ミラー側のウェイトか（UV[3].y=1でミラー）</param>
        /// <param name="materialIndex">マテリアルインデックス（デフォルト0）</param>
        /// <returns>四角形特殊面データ</returns>
        public static MQOFace CreateSpecialFaceForBoneWeight(int vertexIndex, BoneWeightData boneWeight, bool isMirror = false, int materialIndex = 0)
        {
            return new MQOFace
            {
                // 4頂点すべて同じインデックス（四角形特殊面の印）
                VertexIndices = new int[] { vertexIndex, vertexIndex, vertexIndex, vertexIndex },
                MaterialIndex = materialIndex,
                // UV属性: [weight0, weight1], [weight2, weight3], [0, 0], [0, mirrorFlag]
                UVs = new Vector2[]
                {
                    new Vector2(boneWeight.Weight0, boneWeight.Weight1),
                    new Vector2(boneWeight.Weight2, boneWeight.Weight3),
                    Vector2.zero,
                    new Vector2(0, isMirror ? 1f : 0f)
                },
                // COL属性: [boneIndex0, boneIndex1, boneIndex2, boneIndex3]
                VertexColors = new uint[]
                {
                    (uint)boneWeight.BoneIndex0,
                    (uint)boneWeight.BoneIndex1,
                    (uint)boneWeight.BoneIndex2,
                    (uint)boneWeight.BoneIndex3
                }
            };
        }

        /// <summary>
        /// ボーンウェイトデータ（MQO特殊面用）
        /// </summary>
        public class BoneWeightData
        {
            public int BoneIndex0 { get; set; }
            public int BoneIndex1 { get; set; }
            public int BoneIndex2 { get; set; }
            public int BoneIndex3 { get; set; }
            public float Weight0 { get; set; }
            public float Weight1 { get; set; }
            public float Weight2 { get; set; }
            public float Weight3 { get; set; }

            /// <summary>ミラー側のウェイトか（UV[3].y=1でミラー）</summary>
            public bool IsMirror { get; set; }

            /// <summary>有効なウェイトを持つか（合計が0より大きいか）</summary>
            public bool HasWeight => Weight0 + Weight1 + Weight2 + Weight3 > 0;

            /// <summary>
            /// Unity BoneWeightから変換
            /// </summary>
            /// <param name="bw">Unity BoneWeight</param>
            /// <returns>BoneWeightData</returns>
            public static BoneWeightData FromUnityBoneWeight(UnityEngine.BoneWeight bw)
            {
                return new BoneWeightData
                {
                    BoneIndex0 = bw.boneIndex0,
                    BoneIndex1 = bw.boneIndex1,
                    BoneIndex2 = bw.boneIndex2,
                    BoneIndex3 = bw.boneIndex3,
                    Weight0 = bw.weight0,
                    Weight1 = bw.weight1,
                    Weight2 = bw.weight2,
                    Weight3 = bw.weight3
                };
            }

            /// <summary>
            /// Unity BoneWeightに変換
            /// </summary>
            /// <returns>Unity BoneWeight</returns>
            public UnityEngine.BoneWeight ToUnityBoneWeight()
            {
                return new UnityEngine.BoneWeight
                {
                    boneIndex0 = BoneIndex0,
                    boneIndex1 = BoneIndex1,
                    boneIndex2 = BoneIndex2,
                    boneIndex3 = BoneIndex3,
                    weight0 = Weight0,
                    weight1 = Weight1,
                    weight2 = Weight2,
                    weight3 = Weight3
                };
            }
        }

        // =====================================================================
        // ボーンオブジェクト作成（MQOExporter互換）
        // =====================================================================
        // 注意: 将来MQOExporterもこのメソッドを使うように統合予定
        // 現時点ではPMXBoneWeightExportPanelから使用

        /// <summary>
        /// ボーンデータ（MQOオブジェクト生成用）
        /// </summary>
        public class BoneData
        {
            /// <summary>ボーン名</summary>
            public string Name { get; set; }

            /// <summary>親ボーンインデックス（-1はルート）</summary>
            public int ParentIndex { get; set; } = -1;

            /// <summary>位置（ワールド座標）</summary>
            public Vector3 Position { get; set; }

            /// <summary>モデル空間回転（Quaternion、ワールド回転）</summary>
            public Quaternion? ModelRotation { get; set; }

            /// <summary>回転（度数法、暫定で0）</summary>
            public Vector3 Rotation { get; set; }

            /// <summary>スケール（暫定で1,1,1）</summary>
            public Vector3 Scale { get; set; } = Vector3.one;

            /// <summary>表示フラグ</summary>
            public bool IsVisible { get; set; } = true;

            /// <summary>ロックフラグ</summary>
            public bool IsLocked { get; set; } = false;
        }

        /// <summary>
        /// __Armature__オブジェクトを作成（MQOExporter互換）
        /// </summary>
        public static MQOObject CreateArmatureObject()
        {
            var obj = new MQOObject { Name = "__Armature__" };
            obj.Attributes.Add(new MQOAttribute("depth", 0));
            obj.Attributes.Add(new MQOAttribute("visible", 15));
            obj.Attributes.Add(new MQOAttribute("locking", 0));
            obj.Attributes.Add(new MQOAttribute("shading", 1));
            obj.Attributes.Add(new MQOAttribute("facet", 59.5f));
            obj.Attributes.Add(new MQOAttribute("color", 1f, 1f, 1f));
            obj.Attributes.Add(new MQOAttribute("color_type", 0));
            return obj;
        }

        /// <summary>
        /// ボーンオブジェクトを作成（MQOExporter互換）
        /// </summary>
        /// <param name="bone">ボーンデータ</param>
        /// <param name="depth">MQOオブジェクトのdepth（親子関係に基づく）</param>
        /// <param name="scale">スケール係数（PMX→MQO変換用）</param>
        /// <param name="flip">軸反転指定</param>
        /// <returns>MQOオブジェクト</returns>
        /// <remarks>
        /// translation / rotation / scale を出力する。
        /// rotation は MQO 仕様どおり HPB 順（MQOLocalRotationOps が変換）。
        /// PMX にはボーンの回転情報が無いため、PMX 由来では 0 になる。
        /// </remarks>
        public static MQOObject CreateBoneObject(BoneData bone, int depth, float scale , AxisFlip flip)
        {
            var obj = new MQOObject { Name = bone.Name ?? "Bone" };

            // デプス設定
            obj.Attributes.Add(new MQOAttribute("depth", depth));

            // 基本属性
            obj.Attributes.Add(new MQOAttribute("visible", bone.IsVisible ? 15 : 0));
            obj.Attributes.Add(new MQOAttribute("locking", bone.IsLocked ? 1 : 0));
            obj.Attributes.Add(new MQOAttribute("shading", 1));
            obj.Attributes.Add(new MQOAttribute("facet", 59.5f));
            obj.Attributes.Add(new MQOAttribute("color", 1f, 1f, 1f));
            obj.Attributes.Add(new MQOAttribute("color_type", 0));

            // ローカルトランスフォーム
            Vector3 pos = AxisFlipOps.Position(flip, bone.Position, scale);
            obj.Attributes.Add(new MQOAttribute("translation", pos.x, pos.y, pos.z));

            // 回転。MQO の rotation は XYZ ではなく HPB なので並べ替える
            Vector3 rot = MQOLocalRotationOps.ToMqoRotation(bone.Rotation, flip);
            obj.Attributes.Add(new MQOAttribute("rotation", rot.x, rot.y, rot.z));

            // スケール（暫定で1,1,1）
            obj.Attributes.Add(new MQOAttribute("scale", bone.Scale.x, bone.Scale.y, bone.Scale.z));

            return obj;
        }

        /// <summary>
        /// __ArmatureName__オブジェクトを作成（MQOExporter互換）
        /// ボーンインデックス→ボーン名の対応表用
        /// </summary>
        public static MQOObject CreateArmatureNameObject()
        {
            var obj = new MQOObject { Name = "__ArmatureName__" };
            obj.Attributes.Add(new MQOAttribute("depth", 0));
            obj.Attributes.Add(new MQOAttribute("visible", 0));  // 非表示
            obj.Attributes.Add(new MQOAttribute("locking", 1));  // ロック
            obj.Attributes.Add(new MQOAttribute("shading", 1));
            obj.Attributes.Add(new MQOAttribute("facet", 59.5f));
            obj.Attributes.Add(new MQOAttribute("color", 0.5f, 0.5f, 0.5f));
            obj.Attributes.Add(new MQOAttribute("color_type", 0));
            return obj;
        }

        /// <summary>
        /// __ArmatureName__ボーン名オブジェクトを作成（MQOExporter互換）
        /// ボーンインデックス順で出力し、インデックス→名前の対応を保持
        /// </summary>
        /// <param name="boneName">ボーン名</param>
        public static MQOObject CreateBoneNameObject(string boneName)
        {
            var obj = new MQOObject { Name = "__ArmatureName__" + (boneName ?? "Bone") };
            obj.Attributes.Add(new MQOAttribute("depth", 1));
            obj.Attributes.Add(new MQOAttribute("visible", 0));
            obj.Attributes.Add(new MQOAttribute("locking", 1));
            obj.Attributes.Add(new MQOAttribute("shading", 1));
            obj.Attributes.Add(new MQOAttribute("facet", 59.5f));
            obj.Attributes.Add(new MQOAttribute("color", 0.5f, 0.5f, 0.5f));
            obj.Attributes.Add(new MQOAttribute("color_type", 0));
            return obj;
        }

        /// <summary>
        /// ボーンリストからMQOオブジェクトリストを生成（MQOExporter互換）
        /// </summary>
        /// <param name="bones">ボーンデータリスト（インデックス順）</param>
        /// <param name="scale">スケール係数</param>
        /// <param name="flip">軸反転指定</param>
        /// <returns>MQOオブジェクトリスト（__Armature__ + ボーン + __ArmatureName__ + ボーン名）</returns>
        public static List<MQOObject> CreateBoneObjectsForMQO(IList<BoneData> bones, float scale , AxisFlip flip)
        {
            var objects = new List<MQOObject>();

            if (bones == null || bones.Count == 0)
                return objects;

            // 1. __Armature__オブジェクト
            objects.Add(CreateArmatureObject());

            // 2. ボーンのdepthを計算
            var depths = CalculateBoneDepths(bones);

            // 3. ローカル座標・回転を計算
            Vector3[] localPositions;
            Vector3[] localRotations;

            // ModelRotationが設定されているか確認（最初のボーンで判定）
            bool hasModelRotation = bones.Count > 0 && bones[0].ModelRotation.HasValue;

            if (hasModelRotation)
            {
                // 回転を考慮したローカル変換を計算（軸反転をここで適用）
                (localPositions, localRotations) = CalculateLocalTransforms(bones, flip);
            }
            else
            {
                // 従来の方式（回転を無視、後方互換）
                localPositions = CalculateLocalPositions(bones);
                localRotations = new Vector3[bones.Count];
                for (int i = 0; i < bones.Count; i++)
                {
                    localRotations[i] = bones[i].Rotation;
                }
            }

            // 4. ボーンをツリー順（深さ優先）でソート
            var sortedIndices = SortBonesDepthFirst(bones);

            // 5. ボーンオブジェクトを出力（ツリー順）
            foreach (int idx in sortedIndices)
            {
                var bone = bones[idx];
                int depth = depths[idx];
                Vector3 localPos = localPositions[idx];
                Vector3 localRot = localRotations[idx];
                // hasModelRotation=trueの場合、軸反転は既にCalculateLocalTransformsで適用済み
                objects.Add(CreateBoneObjectWithLocalTransform(bone, localPos, localRot, depth, scale, hasModelRotation ? AxisFlip.None : flip));
            }

            // 6. __ArmatureName__オブジェクト
            objects.Add(CreateArmatureNameObject());

            // 7. ボーン名オブジェクト（インデックス順）
            foreach (var bone in bones)
            {
                objects.Add(CreateBoneNameObject(bone.Name));
            }

            return objects;
        }

        /// <summary>
        /// ローカル座標を計算（絶対座標から親からの相対座標へ変換）
        /// 【旧版】回転を考慮しない単純差分
        /// </summary>
        private static Vector3[] CalculateLocalPositions(IList<BoneData> bones)
        {
            var localPositions = new Vector3[bones.Count];

            for (int i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];

                if (bone.ParentIndex >= 0 && bone.ParentIndex < bones.Count)
                {
                    // 親がいる場合：子の絶対座標 - 親の絶対座標 = ローカル座標
                    var parent = bones[bone.ParentIndex];
                    localPositions[i] = bone.Position - parent.Position;
                }
                else
                {
                    // ルートボーン：絶対座標をそのまま使用
                    localPositions[i] = bone.Position;
                }
            }

            return localPositions;
        }

        /// <summary>
        /// ローカル座標・回転を計算（親の回転を考慮）
        /// PMXImporter.ConvertBoneと同等のアルゴリズム
        /// </summary>
        /// <param name="bones">ボーンデータリスト（Position=ワールド座標（PMX座標系）、ModelRotation=ワールド回転（FlipZ変換済み））</param>
        /// <param name="flip">軸反転指定（Positionに適用）</param>
        /// <returns>ローカル位置とローカル回転（オイラー角）の配列</returns>
        private static (Vector3[] localPositions, Vector3[] localRotations) CalculateLocalTransforms(IList<BoneData> bones, AxisFlip flip)
        {
            var localPositions = new Vector3[bones.Count];
            var localRotations = new Vector3[bones.Count];

            for (int i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                Quaternion modelRotation = bone.ModelRotation ?? Quaternion.identity;

                // ワールド位置（軸反転）
                Vector3 worldPosition = AxisFlipOps.Position(flip, bone.Position);

                if (bone.ParentIndex >= 0 && bone.ParentIndex < bones.Count)
                {
                    // 親がいる場合
                    var parent = bones[bone.ParentIndex];
                    Quaternion parentModelRotation = parent.ModelRotation ?? Quaternion.identity;

                    // 親のワールド位置（軸反転）
                    Vector3 parentWorldPos = AxisFlipOps.Position(flip, parent.Position);

                    // MQO: childWorldPos = parentWorldPos + parentRotation * childTranslation
                    // したがって: childTranslation = Inverse(parentRotation) * (childWorldPos - parentWorldPos)
                    Vector3 worldOffset = worldPosition - parentWorldPos;
                    localPositions[i] = Quaternion.Inverse(parentModelRotation) * worldOffset;

                    // 親からの相対回転
                    Quaternion localRotation = Quaternion.Inverse(parentModelRotation) * modelRotation;
                    localRotations[i] = localRotation.eulerAngles;
                }
                else
                {
                    // ルートボーン：ワールド座標・回転をそのまま使用
                    localPositions[i] = worldPosition;
                    localRotations[i] = modelRotation.eulerAngles;
                }
            }

            return (localPositions, localRotations);
        }

        /// <summary>
        /// ボーンオブジェクトを作成（ローカル座標指定版）
        /// </summary>
        private static MQOObject CreateBoneObjectWithLocalPosition(BoneData bone, Vector3 localPosition, int depth, float scale, AxisFlip flip)
        {
            var obj = new MQOObject { Name = bone.Name ?? "Bone" };

            // デプス設定
            obj.Attributes.Add(new MQOAttribute("depth", depth));

            // 基本属性
            obj.Attributes.Add(new MQOAttribute("visible", bone.IsVisible ? 15 : 0));
            obj.Attributes.Add(new MQOAttribute("locking", bone.IsLocked ? 1 : 0));
            obj.Attributes.Add(new MQOAttribute("shading", 1));
            obj.Attributes.Add(new MQOAttribute("facet", 59.5f));
            obj.Attributes.Add(new MQOAttribute("color", 1f, 1f, 1f));
            obj.Attributes.Add(new MQOAttribute("color_type", 0));

            // ローカルトランスフォーム（親からの相対座標）
            Vector3 pos = AxisFlipOps.Position(flip, localPosition, scale);
            obj.Attributes.Add(new MQOAttribute("translation", pos.x, pos.y, pos.z));

            // 回転。MQO の rotation は XYZ ではなく HPB なので並べ替える
            Vector3 rot = MQOLocalRotationOps.ToMqoRotation(bone.Rotation, flip);
            obj.Attributes.Add(new MQOAttribute("rotation", rot.x, rot.y, rot.z));

            // スケール（暫定で1,1,1）
            obj.Attributes.Add(new MQOAttribute("scale", bone.Scale.x, bone.Scale.y, bone.Scale.z));

            return obj;
        }

        /// <summary>
        /// ボーンオブジェクトを作成（ローカル座標・回転指定版）
        /// </summary>
        /// <param name="bone">ボーンデータ</param>
        /// <param name="localPosition">ローカル位置（親の回転考慮済み）</param>
        /// <param name="localRotation">ローカル回転（オイラー角、度数法）</param>
        /// <param name="depth">MQOオブジェクトの深さ</param>
        /// <param name="scale">スケール係数</param>
        /// <param name="flip">軸反転指定（位置に適用、回転には適用しない）</param>
        private static MQOObject CreateBoneObjectWithLocalTransform(BoneData bone, Vector3 localPosition, Vector3 localRotation, int depth, float scale, AxisFlip flip)
        {
            var obj = new MQOObject { Name = bone.Name ?? "Bone" };

            // デプス設定
            obj.Attributes.Add(new MQOAttribute("depth", depth));

            // 基本属性
            obj.Attributes.Add(new MQOAttribute("visible", bone.IsVisible ? 15 : 0));
            obj.Attributes.Add(new MQOAttribute("locking", bone.IsLocked ? 1 : 0));
            obj.Attributes.Add(new MQOAttribute("shading", 1));
            obj.Attributes.Add(new MQOAttribute("facet", 59.5f));
            obj.Attributes.Add(new MQOAttribute("color", 1f, 1f, 1f));
            obj.Attributes.Add(new MQOAttribute("color_type", 0));

            // ローカルトランスフォーム（位置）
            Vector3 pos = AxisFlipOps.Position(flip, localPosition, scale);
            obj.Attributes.Add(new MQOAttribute("translation", pos.x, pos.y, pos.z));

            // ローカル回転。MQO の rotation は XYZ ではなく HPB なので並べ替える
            Vector3 rot = MQOLocalRotationOps.ToMqoRotation(localRotation, flip);
            obj.Attributes.Add(new MQOAttribute("rotation", rot.x, rot.y, rot.z));

            // スケール
            obj.Attributes.Add(new MQOAttribute("scale", bone.Scale.x, bone.Scale.y, bone.Scale.z));

            return obj;
        }

        /// <summary>
        /// ボーンのdepthを計算（親子関係に基づく）
        /// __Armature__の下なのでルートボーンはdepth=1
        /// </summary>
        private static int[] CalculateBoneDepths(IList<BoneData> bones)
        {
            var depths = new int[bones.Count];

            for (int i = 0; i < bones.Count; i++)
            {
                depths[i] = CalculateSingleBoneDepth(i, bones, depths);
            }

            return depths;
        }

        private static int CalculateSingleBoneDepth(int index, IList<BoneData> bones, int[] cachedDepths)
        {
            if (cachedDepths[index] > 0)
                return cachedDepths[index];

            var bone = bones[index];

            // ルートボーン（親がいない）→ depth=1（__Armature__の下）
            if (bone.ParentIndex < 0 || bone.ParentIndex >= bones.Count)
            {
                cachedDepths[index] = 1;
                return 1;
            }

            // 親のdepth + 1
            int parentDepth = CalculateSingleBoneDepth(bone.ParentIndex, bones, cachedDepths);
            cachedDepths[index] = parentDepth + 1;
            return cachedDepths[index];
        }

        /// <summary>
        /// ボーンを深さ優先順でソート
        /// </summary>
        private static List<int> SortBonesDepthFirst(IList<BoneData> bones)
        {
            var sorted = new List<int>();
            var visited = new bool[bones.Count];

            // ルートボーンを見つける
            var rootIndices = new List<int>();
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].ParentIndex < 0 || bones[i].ParentIndex >= bones.Count)
                {
                    rootIndices.Add(i);
                }
            }

            // 各ルートから深さ優先で追加
            foreach (int root in rootIndices)
            {
                AddBoneDepthFirst(root, bones, sorted, visited);
            }

            // 訪問されなかったボーンも追加（循環参照対策）
            for (int i = 0; i < bones.Count; i++)
            {
                if (!visited[i])
                {
                    sorted.Add(i);
                }
            }

            return sorted;
        }

        private static void AddBoneDepthFirst(int index, IList<BoneData> bones, List<int> sorted, bool[] visited)
        {
            if (visited[index])
                return;

            visited[index] = true;
            sorted.Add(index);

            // 子ボーンを探して再帰
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].ParentIndex == index)
                {
                    AddBoneDepthFirst(i, bones, sorted, visited);
                }
            }
        }

        // =====================================================================
        // ログ出力ヘルパー
        // =====================================================================

        /// <summary>
        /// 検証結果をログ出力
        /// </summary>
        public static void LogValidationResult(ValidationResult result, string contextName = "")
        {
            string prefix = string.IsNullOrEmpty(contextName) ? "" : $"[{contextName}] ";

            if (result.IsValid)
            {
                Debug.Log($"{prefix}頂点ID検証: OK " +
                    $"(総数: {result.TotalVertexCount}, " +
                    $"有効: {result.UsedIds.Count}, " +
                    $"未設定: {result.UnsetCount})");
            }
            else
            {
                Debug.LogError($"{prefix}頂点ID検証: エラーあり");
                foreach (var error in result.Errors)
                {
                    Debug.LogError($"  {error}");
                }
            }

            foreach (var warning in result.Warnings)
            {
                Debug.LogWarning($"{prefix}{warning}");
            }
        }
    }
}