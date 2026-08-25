// Assets/Editor/Poly_Ling/Data/MirrorPair.cs
// ミラーペア: 実体側メッシュとミラー側メッシュのペアリングと同期
// 頂点マップ、ボーンペアマップを保持し、編集結果の同期を担当

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Symmetry;
using Poly_Ling.Data;

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
            BonePairMap != null;

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
        /// Real と Mirror は PMX 上で頂点インデックスが 1:1 対応する。
        /// VertexMap[i] = i で直接対応付け。頂点数が異なる場合は失敗。
        /// </summary>
        private bool BuildVertexMap()
        {
            var realMesh = Real.MeshObject;
            var mirrorMesh = Mirror.MeshObject;

            int realCount = realMesh.VertexCount;
            int mirrorCount = mirrorMesh.VertexCount;

            if (realCount != mirrorCount)
            {
                BuildLog += $"Vertex count mismatch: real={realCount}, mirror={mirrorCount}\n";
                return false;
            }

            if (mirrorCount == 0)
            {
                BuildLog += "Mirror mesh has 0 vertices\n";
                return false;
            }

            VertexMap = new int[realCount];
            for (int i = 0; i < realCount; i++)
                VertexMap[i] = i;

            BuildLog += $"VertexMap: direct index mapping, count={realCount}\n";
            return true;
        }

        // ================================================================
        // ボーンペアマップ構築
        // ================================================================

        /// <summary>
        /// VertexMapとBoneWeightの対応からボーンペアを自動検出する。
        /// 実体側の頂点のBoneWeight.boneIndexNと、対応するミラー側頂点のBoneWeight.boneIndexNを
        /// 照合し、ウェイト値が一致するスロット同士のボーンインデックスをペアとして登録する。
        /// </summary>
        /// <summary>
        /// 左右のボーン対応表を、確定値（MeshObject.MirrorBoneIndex）から組む。
        ///
        /// 【ウェイトからの推定を廃止した理由】
        ///   以前は「実体側と鏡側の頂点を並べ、ウェイトの数値が一致するスロット同士の
        ///   ボーン番号を左右の相棒とみなして多数決」で決めていた。左右が対称に
        ///   塗られている間しか当たらない方式で、片側だけ塗った直後に再構築すると
        ///   誤った対応を学習し、MirrorBoneWeight とミラー側 BoneWeight に
        ///   間違ったボーン番号を書き込んでいた（実測: 左腕・左ひじが両方とも
        ///   ボーン Bridge+ に張り付いた）。
        ///
        ///   左右のボーン対応はスキンド変換が枝を分割した時点で 1 対 1 に確定している。
        ///   MeshFilterToSkinnedConverter がその値を MirrorBoneIndex に書くので、
        ///   ここはそれを読むだけにする。推定は一切しない。
        /// </summary>
        private bool BuildBonePairMap()
        {
            BonePairMap = new Dictionary<int, int>();

            var model = Real?.ParentModelContext ?? Mirror?.ParentModelContext;
            var realMesh = Real.MeshObject;

            bool hasBoneWeights = realMesh.Vertices.Any(v => v.HasBoneWeight);

            if (model == null)
            {
                BuildLog += "BonePairMap: ParentModelContext が無いため対応表を作れない\n";
                // 対応が判らないときは写像しない。壊れた値を書くよりも書かないほうが良い。
                return !hasBoneWeights;
            }

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;

                int peer = mc.MirrorBoneIndex;
                if (peer < 0 || peer >= model.MeshContextCount) continue;
                BonePairMap[i] = peer;
            }

            BuildLog += $"BonePairMap: {BonePairMap.Count} pairs from MirrorBoneIndex\n";

            // 対応表が空でもペア自体は成立させる。頂点マップは別に組めているので、
            // 位置・法線の同期は動く。ウェイトだけ写像しない。
            return true;
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

                // 写像できないものは書かない。誤ったボーン番号を残すより無いほうが良い。
                if (!TryMapBoneWeight(vertex.BoneWeight.Value, out var mirrorBw)) continue;

                vertex.MirrorBoneWeight = mirrorBw;
                applied++;
            }

            BuildLog += $"MirrorBoneWeight: applied to {applied}/{realMesh.VertexCount} vertices\n";
        }

        /// <summary>
        /// ボーンインデックスを反対側に変換。対応が無ければ -1 を返す。
        ///
        /// 以前は対応が無いとき元の番号をそのまま返していた。中心線上のボーンなら
        /// それで正しいが、対応表が不完全なだけの場合は「左のボーンで右側を動かす」
        /// 誤りになる。呼び出し側で -1 を見て書き込みを止める。
        /// </summary>
        private int MapBone(int boneIndex)
        {
            if (boneIndex < 0) return boneIndex;
            if (BonePairMap != null && BonePairMap.TryGetValue(boneIndex, out int mapped))
                return mapped;
            return -1;
        }

        /// <summary>
        /// 4 スロット全部の写像を試みる。1 つでも対応が無ければ false。
        /// ウェイト 0 のスロットは対応が無くてもそのまま 0 として通す。
        /// </summary>
        private bool TryMapBoneWeight(BoneWeight src, out BoneWeight dst)
        {
            dst = default;

            int[] bones   = { src.boneIndex0, src.boneIndex1, src.boneIndex2, src.boneIndex3 };
            float[] weights = { src.weight0, src.weight1, src.weight2, src.weight3 };
            var mappedBones = new int[4];

            for (int i = 0; i < 4; i++)
            {
                if (weights[i] <= 0f) { mappedBones[i] = 0; continue; }

                int m = MapBone(bones[i]);
                if (m < 0) return false;
                mappedBones[i] = m;
            }

            dst = new BoneWeight
            {
                boneIndex0 = mappedBones[0],
                boneIndex1 = mappedBones[1],
                boneIndex2 = mappedBones[2],
                boneIndex3 = mappedBones[3],
                weight0 = weights[0],
                weight1 = weights[1],
                weight2 = weights[2],
                weight3 = weights[3]
            };
            return true;
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
        /// Real側のボーンウェイトを、対応ボーンに差し替えて Mirror 側へ書き込む。
        ///
        /// Mirror 側メッシュはファイルに実体を持つ独立メッシュ（スキン変換後は
        /// MirrorGeometryDerived = false）で、自分の BoneWeight を保存する。
        /// 実体側を塗っただけではこれが更新されないため、ウェイト操作のあとに呼ぶ。
        ///
        /// 併せて Real 側頂点の MirrorBoneWeight（GPU のミラー描画が読む値）も
        /// 張り直す。従来これは Build() の ApplyMirrorBoneWeights でしか作られず、
        /// 塗ったあとは古い値のまま残っていた。
        /// </summary>
        public void SyncBoneWeights()
        {
            if (!IsValid) return;

            var realMesh   = Real.MeshObject;
            var mirrorMesh = Mirror.MeshObject;
            if (realMesh == null || mirrorMesh == null) return;

            for (int i = 0; i < VertexMap.Length; i++)
            {
                if (i >= realMesh.VertexCount) break;

                var realVertex = realMesh.Vertices[i];
                if (!realVertex.HasBoneWeight) continue;

                // 写像できないものは書かない。誤ったボーン番号を残すより無いほうが良い。
                if (!TryMapBoneWeight(realVertex.BoneWeight.Value, out var mapped)) continue;

                // GPU のミラー描画用（実体側頂点に持たせる値）
                realVertex.MirrorBoneWeight = mapped;

                // ミラーメッシュ本体（保存される値）
                int mi = VertexMap[i];
                if (mi < 0 || mi >= mirrorMesh.VertexCount) continue;
                mirrorMesh.Vertices[mi].BoneWeight = mapped;
            }
        }

        /// <summary>
        /// Mirror 側のボーンウェイトを Real 側へ写す（SyncBoneWeights の逆向き）。
        ///
        /// ミラー側メッシュも選択して直接塗れる。塗った内容が次の実体側同期で
        /// 消えないよう、ミラー側を書き換えたときはこちらを通す。
        /// </summary>
        public void SyncBoneWeightsFromMirror()
        {
            if (!IsValid) return;

            var realMesh   = Real.MeshObject;
            var mirrorMesh = Mirror.MeshObject;
            if (realMesh == null || mirrorMesh == null) return;

            for (int i = 0; i < VertexMap.Length; i++)
            {
                if (i >= realMesh.VertexCount) break;

                int mi = VertexMap[i];
                if (mi < 0 || mi >= mirrorMesh.VertexCount) continue;

                var mirrorVertex = mirrorMesh.Vertices[mi];
                if (!mirrorVertex.HasBoneWeight) continue;

                // ミラー→実体は同じ対応表を逆向きに引く。BonePairMap は
                // 実体ボーン→ミラーボーンとミラーボーン→実体ボーンの両方を持つ
                // （MirrorBoneIndex を双方向に書いているため）。
                if (!TryMapBoneWeight(mirrorVertex.BoneWeight.Value, out var mapped)) continue;

                var realVertex = realMesh.Vertices[i];
                realVertex.BoneWeight       = mapped;
                realVertex.MirrorBoneWeight = mirrorVertex.BoneWeight.Value;
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
