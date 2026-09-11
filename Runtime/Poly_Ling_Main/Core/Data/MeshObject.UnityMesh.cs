// MeshObject.UnityMesh.cs
// MeshObject：Unity Mesh への変換（頂点共有版）のメソッド。フィールドは MeshObject.cs。
// Runtime/Poly_Ling_Main/Core/Data/ に配置

using Poly_Ling.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.Data
{
    public partial class MeshObject
    {
        // ================================================================
        // Unity Mesh 変換（頂点共有版）
        // ================================================================

        /// <summary>
        /// Unity Meshに変換（頂点共有版）
        /// (頂点インデックス, UVサブインデックス, 法線サブインデックス) の組み合わせで頂点を共有
        /// MQO読み込み時の CreateFaceAndModifyVertex 方式に対応
        /// </summary>
        /// <param name="materialCount">マテリアル数（省略時は自動計算）</param>
        public Mesh ToUnityMeshShared(int materialCount = -1)
        {
            return PLMeshBridge.I.ToUnityMeshShared(this, materialCount);
        }

        /// <summary>
        /// Unity Meshに変換（頂点共有版・座標変換付き）
        /// </summary>
        /// <param name="transform">頂点に適用する変換行列</param>
        /// <param name="materialCount">マテリアル数（省略時は自動計算）</param>
        public Mesh ToUnityMeshShared(Matrix4x4 transform, int materialCount = -1)
        {
            return PLMeshBridge.I.ToUnityMeshShared(this, transform, materialCount);
        }

        /// <summary>
        /// Unity Meshから読み込み
        /// </summary>
        /// <param name="mesh">読み込み元のMesh</param>
        /// <param name="mergeVertices">同一位置の頂点を統合するか</param>
        public void FromUnityMesh(Mesh mesh, bool mergeVertices = true)
        {
            FromUnityMesh(mesh, mergeVertices, false);
        }

        /// <summary>
        /// Unity MeshからMeshObjectを構築
        /// </summary>
        /// <param name="mesh">ソースメッシュ</param>
        /// <param name="mergeVertices">同一位置の頂点を統合するか</param>
        /// <param name="includeBoneWeights">BoneWeight情報を読み込むか（スキンドメッシュ用）</param>
        public void FromUnityMesh(Mesh mesh, bool mergeVertices, bool includeBoneWeights)
        {
            PLMeshBridge.I.FromUnityMesh(this, mesh, mergeVertices, includeBoneWeights);
        }

        /// <summary>
        /// 三角形だけを既存の Unity Mesh へ張り直す。面の表示/非表示切替に使う。
        /// 展開頂点数が変わっている場合は何もせず false を返す。
        /// </summary>
        public bool ApplyTrianglesToUnityMesh(Mesh mesh, int materialCount = -1)
        {
            return PLMeshBridge.I.ApplyTrianglesInPlace(mesh, this, materialCount);
        }

        /// <summary>
        /// 法線だけを既存の Unity Mesh へ反映する。展開頂点数が変わっている場合は
        /// 何もせず false を返す（呼び出し側でメッシュを作り直すこと）。
        /// </summary>
        public bool ApplyNormalsToUnityMesh(Mesh mesh)
        {
            return PLMeshBridge.I.ApplyNormalsInPlace(mesh, this);
        }
        // === ユーティリティ ===

        /// <summary>
        /// データをクリア
        /// </summary>
        public void Clear()
        {
            Vertices.Clear();
            Faces.Clear();
            _positionCacheDirty = true;
        }

        /// <summary>
        /// 全ての面の法線を自動計算（フラット）。UVスロットを分割する既定動作。
        /// </summary>
        public void RecalculateNormals()
        {
            RecalculateNormals(splitSlots: true);
        }

        /// <summary>
        /// 全ての面の法線を自動計算（フラット）。
        ///
        /// splitSlots = true:
        ///   面コーナーの (UV値, 面法線) の一意な組ごとに UV/法線スロットを割り当て直す。
        ///   ハードエッジを正しく表現できるが、UVスロット数（＝展開頂点数）が増える。
        /// splitSlots = false:
        ///   既存の UV スロット数を維持し、各スロットへ面法線を書き込む。
        ///   同じスロットを共有する面が複数ある場合は面順で最後の面法線が残る。
        ///   モーフ関連メッシュ（親子で展開index空間を一致させる必要がある）向け。
        ///
        /// いずれも UVs.Count == Normals.Count と UVIndices[j] == NormalIndices[j] を保つ。
        /// </summary>
        public void RecalculateNormals(bool splitSlots)
        {
            // 除外セットの法線を退避（計算後に書き戻す）
            var normalBackup = CaptureNormalRecalcExcluded();

            var faceNormals = ComputeFaceNormals();

            if (splitSlots)
                RebuildSlotsBySplit(faceNormals);
            else
                WriteFaceNormalsIntoExistingSlots(faceNormals);

            // 除外セットの法線を復帰
            RestoreNormalRecalcExcluded(normalBackup);
        }

        /// <summary>面ごとの面法線。3頂点未満の面は Vector3.up。</summary>
        private Vector3[] ComputeFaceNormals()
        {
            var faceNormals = new Vector3[Faces.Count];
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var face = Faces[fi];
                if (face.VertexCount < 3)
                {
                    faceNormals[fi] = Vector3.up;
                    continue;
                }
                faceNormals[fi] = NormalHelper.CalculateFaceNormal(
                    Vertices[face.VertexIndices[0]].Position,
                    Vertices[face.VertexIndices[1]].Position,
                    Vertices[face.VertexIndices[2]].Position);
            }
            return faceNormals;
        }

        /// <summary>
        /// (UV値, 面法線) の一意な組ごとにスロットを作り直す。
        /// 3頂点未満の面（補助線）は既存法線をそのまま持ち込む。
        /// 面から参照されない頂点は既存スロットを維持する。
        /// </summary>
        private void RebuildSlotsBySplit(Vector3[] faceNormals)
        {
            int vertCount = Vertices.Count;
            var oldUVs     = new List<Vector2>[vertCount];
            var oldNormals = new List<Vector3>[vertCount];
            var newUVs     = new List<Vector2>[vertCount];
            var newNormals = new List<Vector3>[vertCount];

            for (int vi = 0; vi < vertCount; vi++)
            {
                oldUVs[vi]     = new List<Vector2>(Vertices[vi].UVs);
                oldNormals[vi] = new List<Vector3>(Vertices[vi].Normals);
                newUVs[vi]     = new List<Vector2>();
                newNormals[vi] = new List<Vector3>();
            }

            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var face = Faces[fi];
                int corners = face.VertexIndices.Count;

                while (face.UVIndices.Count < corners) face.UVIndices.Add(0);
                while (face.UVIndices.Count > corners) face.UVIndices.RemoveAt(face.UVIndices.Count - 1);
                face.NormalIndices.Clear();

                for (int j = 0; j < corners; j++)
                {
                    int vIdx = face.VertexIndices[j];
                    if (vIdx < 0 || vIdx >= vertCount)
                    {
                        face.UVIndices[j] = 0;
                        face.NormalIndices.Add(0);
                        continue;
                    }

                    int oldSlot = face.UVIndices[j];

                    Vector2 uv = Vector2.zero;
                    var ou = oldUVs[vIdx];
                    if (oldSlot >= 0 && oldSlot < ou.Count) uv = ou[oldSlot];
                    else if (ou.Count > 0) uv = ou[0];

                    Vector3 n;
                    if (face.VertexCount >= 3)
                    {
                        n = faceNormals[fi];
                    }
                    else
                    {
                        var on = oldNormals[vIdx];
                        n = (oldSlot >= 0 && oldSlot < on.Count) ? on[oldSlot]
                          : (on.Count > 0 ? on[0] : Vector3.up);
                    }

                    int slot = FindOrAddSlot(newUVs[vIdx], newNormals[vIdx], uv, n);
                    face.UVIndices[j] = slot;
                    face.NormalIndices.Add(slot);
                }
            }

            for (int vi = 0; vi < vertCount; vi++)
            {
                var vertex = Vertices[vi];
                if (newUVs[vi].Count == 0)
                {
                    vertex.EnsureNormalSlots();
                    continue;
                }
                vertex.UVs     = newUVs[vi];
                vertex.Normals = newNormals[vi];
            }
        }

        /// <summary>(UV値, 法線) の組を検索し、無ければ両リストへ追加して添字を返す。</summary>
        private static int FindOrAddSlot(
            List<Vector2> uvs, List<Vector3> normals, Vector2 uv, Vector3 normal)
        {
            int count = Mathf.Min(uvs.Count, normals.Count);
            for (int i = 0; i < count; i++)
            {
                if (Vector2.Distance(uvs[i], uv) < 0.0001f &&
                    Vector3.Distance(normals[i], normal) < 0.0001f)
                    return i;
            }
            uvs.Add(uv);
            normals.Add(normal);
            return uvs.Count - 1;
        }

        /// <summary>
        /// 既存のUVスロット数を維持したまま面法線を書き込む。
        /// 同一スロットを共有する面が複数ある場合は面順で最後の面法線が残る。
        /// </summary>
        private void WriteFaceNormalsIntoExistingSlots(Vector3[] faceNormals)
        {
            foreach (var vertex in Vertices)
                vertex.EnsureNormalSlots();

            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var face = Faces[fi];
                int corners = face.VertexIndices.Count;

                while (face.UVIndices.Count < corners) face.UVIndices.Add(0);
                while (face.UVIndices.Count > corners) face.UVIndices.RemoveAt(face.UVIndices.Count - 1);
                face.NormalIndices.Clear();

                for (int j = 0; j < corners; j++)
                {
                    int vIdx = face.VertexIndices[j];
                    if (vIdx < 0 || vIdx >= Vertices.Count)
                    {
                        face.UVIndices[j] = 0;
                        face.NormalIndices.Add(0);
                        continue;
                    }

                    var vertex = Vertices[vIdx];
                    int slot = face.UVIndices[j];
                    if (slot < 0 || slot >= vertex.Normals.Count) slot = 0;

                    face.UVIndices[j] = slot;
                    face.NormalIndices.Add(slot);

                    if (face.VertexCount >= 3 && slot < vertex.Normals.Count)
                        vertex.Normals[slot] = faceNormals[fi];
                }
            }
        }

        /// <summary>
        /// スムーズ法線を計算（同一頂点の法線を平均化）。
        /// UVスロット数は変えず、全スロットへ同じ平滑法線を書き込む。
        /// UVs.Count == Normals.Count と UVIndices[j] == NormalIndices[j] を保つ。
        /// </summary>
        public void RecalculateSmoothNormals()
        {
            // 除外セットの法線を退避（計算後に書き戻す）
            var normalBackup = CaptureNormalRecalcExcluded();

            // 頂点ごとに面法線を積算
            var accum = new Vector3[Vertices.Count];
            foreach (var face in Faces)
            {
                if (face.VertexCount < 3)
                    continue;

                Vector3 v0 = Vertices[face.VertexIndices[0]].Position;
                Vector3 v1 = Vertices[face.VertexIndices[1]].Position;
                Vector3 v2 = Vertices[face.VertexIndices[2]].Position;
                Vector3 faceNormal = NormalHelper.CalculateFaceNormal(v0, v1, v2);

                foreach (int vIdx in face.VertexIndices)
                {
                    if (vIdx >= 0 && vIdx < accum.Length)
                        accum[vIdx] += faceNormal;
                }
            }

            // 全スロットへ書き込む（スロット数は維持）
            for (int vi = 0; vi < Vertices.Count; vi++)
            {
                var vertex = Vertices[vi];
                vertex.EnsureNormalSlots();
                if (vertex.Normals.Count == 0)
                    continue;

                Vector3 n = accum[vi].sqrMagnitude > 1e-12f
                    ? accum[vi].normalized
                    : vertex.Normals[0];

                for (int slot = 0; slot < vertex.Normals.Count; slot++)
                    vertex.Normals[slot] = n;
            }

            // 面の法線インデックスをUVサブindexへ合わせる
            foreach (var face in Faces)
            {
                int corners = face.VertexIndices.Count;

                while (face.UVIndices.Count < corners) face.UVIndices.Add(0);
                while (face.UVIndices.Count > corners) face.UVIndices.RemoveAt(face.UVIndices.Count - 1);
                face.NormalIndices.Clear();

                for (int j = 0; j < corners; j++)
                {
                    int vIdx = face.VertexIndices[j];
                    int slotCount = (vIdx >= 0 && vIdx < Vertices.Count)
                        ? Vertices[vIdx].Normals.Count : 0;

                    int slot = face.UVIndices[j];
                    if (slot < 0 || slot >= slotCount) slot = 0;

                    face.UVIndices[j] = slot;
                    face.NormalIndices.Add(slot);
                }
            }

            // 除外セットの法線を復帰
            RestoreNormalRecalcExcluded(normalBackup);
        }
    }
}
