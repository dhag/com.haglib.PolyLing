// Assets/Editor/Poly_Ling/Data/MirrorPair.cs
// ミラーペア: 実体側メッシュとミラー側メッシュのペアリングと同期
// 頂点マップ、ボーンペアマップを保持し、編集結果の同期を担当

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
{
    /// <summary>
    /// 実体側メッシュとミラー側メッシュのペア。
    /// 頂点ペアリング、ボーンペアリング、編集同期を管理する。
    /// </summary>
    [Serializable]
    public class MirrorPair
    {
        // ================================================================
        // 参照（オブジェクト参照、インデックスではない）
        // ================================================================

        /// <summary>実体側MeshContext</summary>
        public MeshContext Real { get; set; }

        /// <summary>ミラー側MeshContext</summary>
        public MeshContext Mirror { get; set; }

        // ================================================================
        // マッピング
        // ================================================================

        /// <summary>
        /// 実体側頂点index → ミラー側頂点index
        /// -1 = 対応する頂点なし
        /// </summary>
        public int[] VertexMap { get; set; }

        /// <summary>
        /// ボーンindex → 反対側ボーンindex
        /// 中央ボーンは自分自身を指す
        /// </summary>
        public Dictionary<int, int> BonePairMap { get; set; }

        /// <summary>ミラー軸</summary>
        public SymmetryAxis Axis { get; set; } = SymmetryAxis.X;

        // ================================================================
        // 状態
        // ================================================================

        /// <summary>ペアリングが完了しているか</summary>
        public bool IsValid =>
            Real != null && Mirror != null &&
            VertexMap != null && VertexMap.Length > 0 &&
            BonePairMap != null && BonePairMap.Count > 0;

        /// <summary>ペアリング構築時のログ</summary>
        public string BuildLog { get; private set; } = "";

        // ================================================================
        // ペアリング構築
        // ================================================================

        /// <summary>
        /// 頂点マップとボーンペアマップを構築し、MirrorBoneWeightを設定する。
        /// Real/Mirrorが設定済みの状態で呼び出すこと。
        /// </summary>
        /// <returns>成功した場合true</returns>
        public bool Build()
        {
            BuildLog = "";

            if (Real?.MeshObject == null || Mirror?.MeshObject == null)
            {
                BuildLog = "Real or Mirror MeshObject is null";
                return false;
            }

            bool vertexOk = BuildVertexMap();
            if (!vertexOk)
                return false;

            bool boneOk = BuildBonePairMap();
            if (!boneOk)
                return false;

            ApplyMirrorBoneWeights();

            return true;
        }

        // ================================================================
        // 頂点マップ構築
        // ================================================================

        /// <summary>
        /// 実体側の各頂点をミラー反転した位置で、ミラー側の最近傍頂点を検索しペアリングする。
        /// 空間ハッシュで高速化。
        /// </summary>
        private bool BuildVertexMap()
        {
            var realMesh = Real.MeshObject;
            var mirrorMesh = Mirror.MeshObject;

            int realCount = realMesh.VertexCount;
            int mirrorCount = mirrorMesh.VertexCount;

            VertexMap = new int[realCount];
            for (int i = 0; i < realCount; i++)
                VertexMap[i] = -1;

            if (mirrorCount == 0)
            {
                BuildLog += $"Mirror mesh has 0 vertices\n";
                return false;
            }

            // ミラー側頂点を空間ハッシュに登録
            float cellSize = EstimateCellSize(mirrorMesh);
            var spatialHash = new Dictionary<long, List<int>>();

            for (int i = 0; i < mirrorCount; i++)
            {
                Vector3 pos = mirrorMesh.Vertices[i].Position;
                long key = HashPosition(pos, cellSize);
                if (!spatialHash.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    spatialHash[key] = list;
                }
                list.Add(i);
            }

            // 実体側頂点をミラー反転して最近傍検索
            float threshold = cellSize * 0.5f;
            float thresholdSq = threshold * threshold;
            int matchedCount = 0;
            int unmatchedCount = 0;

            for (int i = 0; i < realCount; i++)
            {
                Vector3 realPos = realMesh.Vertices[i].Position;
                Vector3 mirroredPos = MirrorPosition(realPos);

                int bestIdx = FindNearest(mirroredPos, spatialHash, mirrorMesh, cellSize, thresholdSq);
                VertexMap[i] = bestIdx;

                if (bestIdx >= 0)
                    matchedCount++;
                else
                    unmatchedCount++;
            }

            BuildLog += $"VertexMap: matched={matchedCount}, unmatched={unmatchedCount}, " +
                         $"real={realCount}, mirror={mirrorCount}, threshold={threshold:F6}\n";

            // 8割以上マッチしていれば成功とする
            if (matchedCount < realCount * 0.8f)
            {
                BuildLog += $"Too few matches ({matchedCount}/{realCount}), aborting\n";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 空間ハッシュから最近傍頂点を検索
        /// </summary>
        private int FindNearest(Vector3 pos, Dictionary<long, List<int>> spatialHash,
            MeshObject mirrorMesh, float cellSize, float thresholdSq)
        {
            int bestIdx = -1;
            float bestDistSq = thresholdSq;

            // 近傍27セルを検索
            int cx = Mathf.FloorToInt(pos.x / cellSize);
            int cy = Mathf.FloorToInt(pos.y / cellSize);
            int cz = Mathf.FloorToInt(pos.z / cellSize);

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                long key = PackHash(cx + dx, cy + dy, cz + dz);
                if (!spatialHash.TryGetValue(key, out var list))
                    continue;

                for (int j = 0; j < list.Count; j++)
                {
                    int idx = list[j];
                    float distSq = (mirrorMesh.Vertices[idx].Position - pos).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestIdx = idx;
                    }
                }
            }

            return bestIdx;
        }

        // ================================================================
        // ボーンペアマップ構築
        // ================================================================

        /// <summary>
        /// VertexMapとBoneWeightの対応からボーンペアを自動検出する。
        /// 実体側の頂点のBoneWeight.boneIndexNと、対応するミラー側頂点のBoneWeight.boneIndexNを
        /// 照合し、ウェイト値が一致するスロット同士のボーンインデックスをペアとして登録する。
        /// </summary>
        private bool BuildBonePairMap()
        {
            BonePairMap = new Dictionary<int, int>();

            var realMesh = Real.MeshObject;
            var mirrorMesh = Mirror.MeshObject;

            // 投票カウント: (realBone, mirrorBone) → count
            var votes = new Dictionary<(int, int), int>();

            for (int i = 0; i < VertexMap.Length; i++)
            {
                int mi = VertexMap[i];
                if (mi < 0) continue;

                var realV = realMesh.Vertices[i];
                var mirrorV = mirrorMesh.Vertices[mi];

                if (!realV.HasBoneWeight || !mirrorV.HasBoneWeight)
                    continue;

                var rw = realV.BoneWeight.Value;
                var mw = mirrorV.BoneWeight.Value;

                // 各スロットのウェイト値が近いペアを検出
                VoteSlots(rw, mw, votes);
            }

            // 投票結果からボーンペアを確定（最多得票を採用）
            // realBone → (mirrorBone, count) の最大値
            var bestMatch = new Dictionary<int, (int mirrorBone, int count)>();

            foreach (var kv in votes)
            {
                int realBone = kv.Key.Item1;
                int mirrorBone = kv.Key.Item2;
                int count = kv.Value;

                if (!bestMatch.TryGetValue(realBone, out var current) || count > current.count)
                {
                    bestMatch[realBone] = (mirrorBone, count);
                }
            }

            foreach (var kv in bestMatch)
            {
                BonePairMap[kv.Key] = kv.Value.mirrorBone;
            }

            BuildLog += $"BonePairMap: {BonePairMap.Count} pairs from {votes.Count} vote entries\n";

            return BonePairMap.Count > 0;
        }

        /// <summary>
        /// BoneWeight4スロット同士で、ウェイト値が近いペアを投票する。
        /// </summary>
        private static void VoteSlots(BoneWeight rw, BoneWeight mw, Dictionary<(int, int), int> votes)
        {
            const float weightEpsilon = 0.01f;

            // 実体側4スロット
            float[] rWeights = { rw.weight0, rw.weight1, rw.weight2, rw.weight3 };
            int[] rBones = { rw.boneIndex0, rw.boneIndex1, rw.boneIndex2, rw.boneIndex3 };

            // ミラー側4スロット
            float[] mWeights = { mw.weight0, mw.weight1, mw.weight2, mw.weight3 };
            int[] mBones = { mw.boneIndex0, mw.boneIndex1, mw.boneIndex2, mw.boneIndex3 };

            // 同じスロット位置のウェイト値が近ければ投票
            for (int s = 0; s < 4; s++)
            {
                if (rWeights[s] < weightEpsilon) continue;

                // 同じスロット位置が最も信頼性が高い
                if (Mathf.Abs(rWeights[s] - mWeights[s]) < weightEpsilon)
                {
                    var pair = (rBones[s], mBones[s]);
                    votes.TryGetValue(pair, out int c);
                    votes[pair] = c + 2; // 同スロットは高信頼で+2
                }

                // ウェイト値が一致する他スロットも検索
                for (int ms = 0; ms < 4; ms++)
                {
                    if (ms == s) continue;
                    if (Mathf.Abs(rWeights[s] - mWeights[ms]) < weightEpsilon)
                    {
                        var pair = (rBones[s], mBones[ms]);
                        votes.TryGetValue(pair, out int c);
                        votes[pair] = c + 1;
                    }
                }
            }
        }

        // ================================================================
        // MirrorBoneWeight設定
        // ================================================================

        /// <summary>
        /// BonePairMapを使って、Real側の各頂点にMirrorBoneWeightを設定する。
        /// 実体側のBoneWeightのboneIndexを対応するミラー側ボーンに差し替えた値を格納。
        /// </summary>
        private void ApplyMirrorBoneWeights()
        {
            var realMesh = Real.MeshObject;
            int applied = 0;

            for (int i = 0; i < realMesh.VertexCount; i++)
            {
                var vertex = realMesh.Vertices[i];
                if (!vertex.HasBoneWeight) continue;

                var bw = vertex.BoneWeight.Value;
                var mirrorBw = new BoneWeight
                {
                    boneIndex0 = MapBone(bw.boneIndex0),
                    boneIndex1 = MapBone(bw.boneIndex1),
                    boneIndex2 = MapBone(bw.boneIndex2),
                    boneIndex3 = MapBone(bw.boneIndex3),
                    weight0 = bw.weight0,
                    weight1 = bw.weight1,
                    weight2 = bw.weight2,
                    weight3 = bw.weight3
                };

                vertex.MirrorBoneWeight = mirrorBw;
                applied++;
            }

            BuildLog += $"MirrorBoneWeight: applied to {applied}/{realMesh.VertexCount} vertices\n";
        }

        /// <summary>
        /// ボーンインデックスを反対側に変換。マップにない場合はそのまま返す。
        /// </summary>
        private int MapBone(int boneIndex)
        {
            if (BonePairMap != null && BonePairMap.TryGetValue(boneIndex, out int mapped))
                return mapped;
            return boneIndex;
        }

        // ================================================================
        // 同期メソッド
        // ================================================================

        /// <summary>
        /// Real側の頂点位置をミラー反転してMirror側に書き込む。
        /// マウスアップ後に呼び出す。
        /// </summary>
        public void SyncPositions()
        {
            if (!IsValid) return;

            var realMesh = Real.MeshObject;
            var mirrorMesh = Mirror.MeshObject;

            for (int i = 0; i < VertexMap.Length; i++)
            {
                int mi = VertexMap[i];
                if (mi < 0 || mi >= mirrorMesh.VertexCount) continue;

                mirrorMesh.Vertices[mi].Position = MirrorPosition(realMesh.Vertices[i].Position);
            }
        }

        /// <summary>
        /// Real側の法線をミラー反転してMirror側に書き込む。
        /// </summary>
        public void SyncNormals()
        {
            if (!IsValid) return;

            var realMesh = Real.MeshObject;
            var mirrorMesh = Mirror.MeshObject;

            for (int i = 0; i < VertexMap.Length; i++)
            {
                int mi = VertexMap[i];
                if (mi < 0 || mi >= mirrorMesh.VertexCount) continue;

                var realNormals = realMesh.Vertices[i].Normals;
                var mirrorNormals = mirrorMesh.Vertices[mi].Normals;

                if (realNormals == null || realNormals.Count == 0) continue;

                Vector3 mirroredNormal = MirrorDirection(realNormals[0]);

                if (mirrorNormals != null && mirrorNormals.Count > 0)
                    mirrorNormals[0] = mirroredNormal;
                else if (mirrorNormals != null)
                    mirrorNormals.Add(mirroredNormal);
                else
                    mirrorMesh.Vertices[mi].Normals = new List<Vector3> { mirroredNormal };
            }
        }

        /// <summary>
        /// 対称モーフの同期: Real側のオフセットをX反転してMirror側に適用。
        /// </summary>
        /// <param name="realMorphBase">Real側のMorphBaseData</param>
        /// <param name="mirrorMorphBase">Mirror側のMorphBaseData</param>
        /// <param name="realMesh">Real側の現在のMeshObject</param>
        /// <param name="mirrorMesh">Mirror側のMeshObject</param>
        public void SyncMorphSymmetric(
            MorphBaseData realMorphBase,
            MorphBaseData mirrorMorphBase,
            MeshObject realMesh,
            MeshObject mirrorMesh)
        {
            if (!IsValid || realMorphBase == null || mirrorMorphBase == null) return;
            if (realMesh == null || mirrorMesh == null) return;

            for (int i = 0; i < VertexMap.Length; i++)
            {
                int mi = VertexMap[i];
                if (mi < 0 || mi >= mirrorMesh.VertexCount) continue;
                if (i >= realMorphBase.VertexCount || mi >= mirrorMorphBase.VertexCount) continue;

                // Real側のオフセット = 現在位置 - 基準位置
                Vector3 realOffset = realMesh.Vertices[i].Position - realMorphBase.BasePositions[i];

                // ミラー反転: (dx, dy, dz) → (-dx, dy, dz)
                Vector3 mirrorOffset = MirrorDirection(realOffset);

                // Mirror側に適用: 基準位置 + ミラーオフセット
                mirrorMesh.Vertices[mi].Position = mirrorMorphBase.BasePositions[mi] + mirrorOffset;
            }
        }

        // ================================================================
        // ミラー変換
        // ================================================================

        /// <summary>
        /// 位置をミラー反転する
        /// </summary>
        public Vector3 MirrorPosition(Vector3 pos)
        {
            switch (Axis)
            {
                case SymmetryAxis.X: return new Vector3(-pos.x, pos.y, pos.z);
                case SymmetryAxis.Y: return new Vector3(pos.x, -pos.y, pos.z);
                case SymmetryAxis.Z: return new Vector3(pos.x, pos.y, -pos.z);
                default: return new Vector3(-pos.x, pos.y, pos.z);
            }
        }

        /// <summary>
        /// 方向ベクトルをミラー反転する（法線、オフセット等）
        /// </summary>
        public Vector3 MirrorDirection(Vector3 dir)
        {
            switch (Axis)
            {
                case SymmetryAxis.X: return new Vector3(-dir.x, dir.y, dir.z);
                case SymmetryAxis.Y: return new Vector3(dir.x, -dir.y, dir.z);
                case SymmetryAxis.Z: return new Vector3(dir.x, dir.y, -dir.z);
                default: return new Vector3(-dir.x, dir.y, dir.z);
            }
        }

        // ================================================================
        // 空間ハッシュユーティリティ
        // ================================================================

        /// <summary>
        /// メッシュのバウンディングボックスからセルサイズを推定
        /// </summary>
        private static float EstimateCellSize(MeshObject mesh)
        {
            if (mesh.VertexCount < 2) return 0.01f;

            Vector3 min = mesh.Vertices[0].Position;
            Vector3 max = min;

            for (int i = 1; i < mesh.VertexCount; i++)
            {
                Vector3 p = mesh.Vertices[i].Position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            Vector3 size = max - min;
            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            // 頂点数に応じた解像度。一般的にPMXモデルは1万〜10万頂点
            // セルサイズが小さすぎるとメモリ過多、大きすぎると検索効率低下
            float resolution = Mathf.Max(maxDim / 100f, 0.001f);
            return resolution;
        }

        private static long HashPosition(Vector3 pos, float cellSize)
        {
            int x = Mathf.FloorToInt(pos.x / cellSize);
            int y = Mathf.FloorToInt(pos.y / cellSize);
            int z = Mathf.FloorToInt(pos.z / cellSize);
            return PackHash(x, y, z);
        }

        private static long PackHash(int x, int y, int z)
        {
            // 21ビットずつ使用（-1048576 ~ 1048575）
            long lx = (long)(x & 0x1FFFFF);
            long ly = (long)(y & 0x1FFFFF);
            long lz = (long)(z & 0x1FFFFF);
            return (lx << 42) | (ly << 21) | lz;
        }

        // ================================================================
        // デバッグ
        // ================================================================

        public override string ToString()
        {
            string realName = Real?.Name ?? "null";
            string mirrorName = Mirror?.Name ?? "null";
            int mapCount = VertexMap?.Length ?? 0;
            int pairCount = BonePairMap?.Count ?? 0;
            return $"MirrorPair[{realName} ↔ {mirrorName}]: vertexMap={mapCount}, bonePairs={pairCount}";
        }
    }
}
