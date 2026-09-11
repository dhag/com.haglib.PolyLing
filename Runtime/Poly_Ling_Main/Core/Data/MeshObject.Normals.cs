// MeshObject.Normals.cs
// MeshObject：法線の再計算と除外セットほかのメソッド。フィールドは MeshObject.cs。
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
        /// <summary>
        /// 除外セットを「頂点集合（辺は両端頂点）」と「面集合」に分解する。
        /// </summary>
        private void CollectNormalRecalcExcludeSets(out HashSet<int> verts, out HashSet<int> faces)
        {
            verts = new HashSet<int>();
            faces = new HashSet<int>();
            if (NormalRecalcExcludeList == null) return;

            foreach (var set in NormalRecalcExcludeList)
            {
                if (set == null) continue;

                foreach (int vi in set.Vertices)
                    if (vi >= 0 && vi < Vertices.Count) verts.Add(vi);

                foreach (var e in set.Edges)
                {
                    if (e.V1 >= 0 && e.V1 < Vertices.Count) verts.Add(e.V1);
                    if (e.V2 >= 0 && e.V2 < Vertices.Count) verts.Add(e.V2);
                }

                foreach (int fi in set.Faces)
                    if (fi >= 0 && fi < Faces.Count) faces.Add(fi);
            }
        }

        /// <summary>
        /// 除外セットが指す頂点インデックス集合。面集合はその構成頂点へ展開する。
        /// Unity Mesh 側は法線を頂点単位でしか持てないため、その復帰用。
        /// </summary>
        public HashSet<int> GetNormalRecalcExcludedVertexIndices()
        {
            CollectNormalRecalcExcludeSets(out var verts, out var faces);
            foreach (int fi in faces)
            {
                foreach (int vi in Faces[fi].VertexIndices)
                    if (vi >= 0 && vi < Vertices.Count) verts.Add(vi);
            }
            return verts;
        }

        /// <summary>
        /// 除外セットが指すコーナーの法線を退避する。対象が無ければ null。
        /// </summary>
        private List<NormalBackupEntry> CaptureNormalRecalcExcluded()
        {
            if (NormalRecalcExcludeList == null || NormalRecalcExcludeList.Count == 0) return null;

            CollectNormalRecalcExcludeSets(out var excludedVerts, out var excludedFaces);
            if (excludedVerts.Count == 0 && excludedFaces.Count == 0) return null;

            var backup = new List<NormalBackupEntry>();
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var face = Faces[fi];
                bool faceExcluded = excludedFaces.Contains(fi);
                int corners = Mathf.Min(face.VertexIndices.Count, face.NormalIndices.Count);

                for (int j = 0; j < corners; j++)
                {
                    int vIdx = face.VertexIndices[j];
                    if (vIdx < 0 || vIdx >= Vertices.Count) continue;
                    if (!faceExcluded && !excludedVerts.Contains(vIdx)) continue;

                    var normals = Vertices[vIdx].Normals;
                    int nIdx = face.NormalIndices[j];
                    if (nIdx < 0 || nIdx >= normals.Count) continue;

                    backup.Add(new NormalBackupEntry
                    {
                        FaceIndex = fi,
                        Corner    = j,
                        Normal    = normals[nIdx]
                    });
                }
            }
            return backup.Count > 0 ? backup : null;
        }

        /// <summary>
        /// 退避した法線を書き戻す。
        ///
        /// UVs.Count == Normals.Count / UVIndices[j] == NormalIndices[j] の不変条件下では、
        /// コーナーの法線スロットは UVサブindex と同一なので、そのスロットへ値を書くだけでよい。
        /// 同一スロットを除外コーナーと非除外コーナーが共有する場合は退避値が優先される。
        /// </summary>
        private void RestoreNormalRecalcExcluded(List<NormalBackupEntry> backup)
        {
            if (backup == null || backup.Count == 0) return;

            foreach (var entry in backup)
            {
                if (entry.FaceIndex < 0 || entry.FaceIndex >= Faces.Count) continue;
                var face = Faces[entry.FaceIndex];
                if (entry.Corner < 0 || entry.Corner >= face.VertexIndices.Count) continue;
                if (entry.Corner >= face.UVIndices.Count) continue;

                int vIdx = face.VertexIndices[entry.Corner];
                if (vIdx < 0 || vIdx >= Vertices.Count) continue;

                var vertex = Vertices[vIdx];
                int slot = face.UVIndices[entry.Corner];
                if (slot < 0 || slot >= vertex.Normals.Count) continue;

                vertex.Normals[slot] = entry.Normal;
                if (entry.Corner < face.NormalIndices.Count)
                    face.NormalIndices[entry.Corner] = slot;
            }
        }

        /// <summary>
        /// UV/法線スロットの不変条件を検証する。
        /// </summary>
        public bool ValidateUVNormalSlots(out string message)
        {
            var sb = new System.Text.StringBuilder();
            int vertexErrors = 0;
            int faceErrors = 0;

            for (int vi = 0; vi < Vertices.Count; vi++)
            {
                var vertex = Vertices[vi];
                if (vertex.UVs.Count != vertex.Normals.Count)
                {
                    if (vertexErrors < 5)
                        sb.AppendLine($"vertex[{vi}] UVs={vertex.UVs.Count} Normals={vertex.Normals.Count}");
                    vertexErrors++;
                }
            }

            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var face = Faces[fi];
                bool bad = face.UVIndices.Count != face.NormalIndices.Count;

                if (!bad)
                {
                    for (int j = 0; j < face.UVIndices.Count; j++)
                    {
                        if (face.UVIndices[j] != face.NormalIndices[j]) { bad = true; break; }
                    }
                }

                if (bad)
                {
                    if (faceErrors < 5)
                        sb.AppendLine($"face[{fi}] UVIndices/NormalIndices mismatch");
                    faceErrors++;
                }
            }

            if (vertexErrors == 0 && faceErrors == 0)
            {
                message = $"[{Name}] OK";
                return true;
            }

            message = $"[{Name}] vertexErrors={vertexErrors} faceErrors={faceErrors}\n{sb}";
            return false;
        }

        /// <summary>
        /// スプリングボーン・コライダーリストのディープコピー（null は null のまま）。
        /// </summary>
        private static List<SpringBoneColliderData> CloneSpringBoneColliders(List<SpringBoneColliderData> src)
        {
            if (src == null) return null;
            var dst = new List<SpringBoneColliderData>(src.Count);
            foreach (var c in src)
                dst.Add(c?.Clone());
            return dst;
        }

        /// <summary>
        /// ディープコピー（IDも保持）
        /// </summary>
        public MeshObject Clone()
        {
            var copy = new MeshObject(Name);
            copy.Type = this.Type;
            copy.SkinKind = this.SkinKind;
            copy.IsTriangulated = this.IsTriangulated;
            copy.Vertices = Vertices.Select(v => v.Clone()).ToList();
            copy.Faces = Faces.Select(f => f.Clone()).ToList();
            copy.ParentIndex = this.ParentIndex;
            copy.Depth = this.Depth;
            copy.HierarchyParentIndex = this.HierarchyParentIndex;
            copy.IgnorePoseInArmature = this.IgnorePoseInArmature;
            copy.IsMirrorBranchRoot   = this.IsMirrorBranchRoot;
            copy.PreserveNormals      = this.PreserveNormals;
            copy.MirrorBakeState      = this.MirrorBakeState?.Clone();
            copy.NormalRecalcExcludeList = this.NormalRecalcExcludeList?.Select(s => s.Clone()).ToList()
                                           ?? new List<Poly_Ling.Selection.PartsSelectionSet>();

            if(this.BoneTransform != null)
            {
                copy.BoneTransform = new BoneTransform();
                copy.BoneTransform.CopyFrom(this.BoneTransform);
            }

            // 付帯データ（IK/剛体/JOINT）をディープコピー（nullはnullのまま）
            copy.IKData = this.IKData?.Clone();
            copy.IKLink = this.IKLink?.Clone();
            copy.RigidBodyData = this.RigidBodyData?.Clone();
            copy.JointData = this.JointData?.Clone();
            copy.PmxBone = this.PmxBone?.Clone();

            // スプリングボーン付帯データをディープコピー（nullはnullのまま）
            copy.SpringBoneColliders = CloneSpringBoneColliders(this.SpringBoneColliders);
            copy.SpringBoneJoint = this.SpringBoneJoint?.Clone();
            copy.SpringBoneChainRoot = this.SpringBoneChainRoot?.Clone();
            copy.HumanBodyBone = this.HumanBodyBone;
            copy.MirrorBoneIndex = this.MirrorBoneIndex;
            copy.HumanLimit = this.HumanLimit?.Clone();
            copy.VrmFirstPerson = this.VrmFirstPerson;
            copy.VrmConstraint = this.VrmConstraint?.Clone();

            // ID管理セットを再構築
            copy.RebuildIdSets();

            return copy;
        }

        /// <summary>
        /// ディープコピー（頂点・面に新しいIDを割り当て）
        /// </summary>
        public MeshObject CloneWithNewIds()
        {
            var copy = new MeshObject(Name);
            copy.Type = this.Type;
            copy.SkinKind = this.SkinKind;
            copy.IsTriangulated = this.IsTriangulated;
            copy.ParentIndex = this.ParentIndex;
            copy.Depth = this.Depth;
            copy.HierarchyParentIndex = this.HierarchyParentIndex;
            copy.IgnorePoseInArmature = this.IgnorePoseInArmature;
            copy.IsMirrorBranchRoot   = this.IsMirrorBranchRoot;
            copy.PreserveNormals      = this.PreserveNormals;
            copy.MirrorBakeState      = this.MirrorBakeState?.Clone();
            copy.NormalRecalcExcludeList = this.NormalRecalcExcludeList?.Select(s => s.Clone()).ToList()
                                           ?? new List<Poly_Ling.Selection.PartsSelectionSet>();

            if (this.BoneTransform != null)
            {
                copy.BoneTransform = new BoneTransform();
                copy.BoneTransform.CopyFrom(this.BoneTransform);
            }

            // 付帯データ（IK/剛体/JOINT）をディープコピー。
            // 内部のボーン/剛体参照は index/name のいずれもオブジェクトIDとは
            // 独立のため、頂点・面のID再割り当てとは無関係にそのまま複製する。
            copy.IKData = this.IKData?.Clone();
            copy.IKLink = this.IKLink?.Clone();
            copy.RigidBodyData = this.RigidBodyData?.Clone();
            copy.JointData = this.JointData?.Clone();
            copy.PmxBone = this.PmxBone?.Clone();

            // スプリングボーン付帯データをディープコピー（頂点/面IDとは独立）。
            copy.SpringBoneColliders = CloneSpringBoneColliders(this.SpringBoneColliders);
            copy.SpringBoneJoint = this.SpringBoneJoint?.Clone();
            copy.SpringBoneChainRoot = this.SpringBoneChainRoot?.Clone();
            copy.HumanBodyBone = this.HumanBodyBone;
            copy.MirrorBoneIndex = this.MirrorBoneIndex;
            copy.HumanLimit = this.HumanLimit?.Clone();
            copy.VrmFirstPerson = this.VrmFirstPerson;
            copy.VrmConstraint = this.VrmConstraint?.Clone();

            // 頂点をコピー（新しいID）
            foreach (var v in Vertices)
            {
                var newV = v.Clone();
                newV.Id = copy.GenerateVertexId();
                copy.Vertices.Add(newV);
            }

            // 面をコピー（新しいID）
            foreach (var f in Faces)
            {
                var newF = f.Clone();
                newF.Id = copy.GenerateFaceId();
                copy.Faces.Add(newF);
            }

            return copy;
        }


        /// <summary>
        /// バウンディングボックスを計算
        /// </summary>
        public Bounds CalculateBounds()
        {
            if (Vertices.Count == 0)
                return new Bounds(Vector3.zero, Vector3.zero);

            Vector3 min = Vertices[0].Position;
            Vector3 max = Vertices[0].Position;

            foreach (var vertex in Vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }

        /// <summary>
        /// マテリアル使用状況を取得
        /// </summary>
        /// <returns>Key: MaterialIndex, Value: 使用面数</returns>
        public Dictionary<int, int> GetMaterialUsage()
        {
            var usage = new Dictionary<int, int>();
            foreach (var face in Faces)
            {
                if (!usage.ContainsKey(face.MaterialIndex))
                    usage[face.MaterialIndex] = 0;
                usage[face.MaterialIndex]++;
            }
            return usage;
        }

        /// <summary>
        /// 指定マテリアルインデックスの面を取得
        /// </summary>
        public IEnumerable<int> GetFacesByMaterial(int materialIndex)
        {
            for (int i = 0; i < Faces.Count; i++)
            {
                if (Faces[i].MaterialIndex == materialIndex)
                    yield return i;
            }
        }

        /// <summary>
        /// 選択した面のマテリアルインデックスを変更
        /// </summary>
        public void SetFacesMaterial(IEnumerable<int> faceIndices, int materialIndex)
        {
            foreach (int idx in faceIndices)
            {
                if (idx >= 0 && idx < Faces.Count)
                {
                    Faces[idx].MaterialIndex = materialIndex;
                }
            }
        }

        /// <summary>
        /// 指定フラグを持つ頂点を取得
        /// </summary>
        public IEnumerable<int> GetVerticesByFlag(VertexFlags flag)
        {
            for (int i = 0; i < Vertices.Count; i++)
            {
                if (Vertices[i].HasFlag(flag))
                    yield return i;
            }
        }

        /// <summary>
        /// 指定フラグを持つ面を取得
        /// </summary>
        public IEnumerable<int> GetFacesByFlag(FaceFlags flag)
        {
            for (int i = 0; i < Faces.Count; i++)
            {
                if (Faces[i].HasFlag(flag))
                    yield return i;
            }
        }

        /// <summary>
        /// 全頂点のフラグをクリア
        /// </summary>
        public void ClearAllVertexFlags()
        {
            foreach (var v in Vertices)
                v.Flags = VertexFlags.None;
        }

        /// <summary>
        /// 全面のフラグをクリア
        /// </summary>
        public void ClearAllFaceFlags()
        {
            foreach (var f in Faces)
                f.Flags = FaceFlags.None;
        }

        /// <summary>
        /// 全頂点のボーンウェイトをクリアし、種別を MeshFilter 系へ戻す。
        ///
        /// ウェイトの破棄と種別の変更は同時に行う。片方だけ行うと、
        /// SkinningMatrix 経路のままウェイトが無い（＝全頂点がボーン 0 に張り付く）
        /// 状態や、ウェイトを持つのに WorldMatrix が二重に掛かる状態を作る。
        /// MirrorBoneWeight も同時に落とす（実体側だけ消すとミラー描画に古い値が残る）。
        /// </summary>
        public void ClearAllBoneWeights()
        {
            foreach (var v in Vertices)
            {
                v.BoneWeight       = null;
                v.MirrorBoneWeight = null;
            }
            SkinKind = SkinKind.MeshFilter;
        }

        /// <summary>
        /// デバッグ情報
        /// </summary>
        public string GetDebugInfo()
        {
            int triCount = Faces.Where(f => f.IsTriangle).Count();
            int quadCount = Faces.Where(f => f.IsQuad).Count();
            int nGonCount = Faces.Count - triCount - quadCount;
            int subMeshCount = SubMeshCount;

            return $"[{Name}] Vertices: {VertexCount}, Faces: {FaceCount} " +
                   $"(Tri: {triCount}, Quad: {quadCount}, NGon: {nGonCount}), SubMeshes: {subMeshCount}";
        }

        /// <summary>
        /// UV展開前後の頂点インデックス対応辞書を構築する。
        /// ToUnityMesh / AppendExpandedVertices と同じ展開順序。
        /// key: (rawVertexIndex, uvSubIndex) → value: 展開後インデックス
        /// </summary>
        public Dictionary<(int vIdx, int uvIdx), int> BuildExpansionMap()
        {
            // 展開規則は MeshExpansion に一本化してある（孤立頂点除外／IsHidden は見ない）。
            var map = new Dictionary<(int vIdx, int uvIdx), int>();
            MeshExpansion.Enumerate(this, (vIdx, uvIdx, expIdx) => map[(vIdx, uvIdx)] = expIdx);
            return map;
        }

        /// <summary>
        /// UV展開後インデックス → (rawVertexIndex, uvSubIndex) の逆引き辞書を構築する。
        /// </summary>
        public Dictionary<int, (int vIdx, int uvIdx)> BuildInverseExpansionMap()
        {
            // 展開規則は MeshExpansion に一本化してある（孤立頂点除外／IsHidden は見ない）。
            var map = new Dictionary<int, (int vIdx, int uvIdx)>();
            MeshExpansion.Enumerate(this, (vIdx, uvIdx, expIdx) => map[expIdx] = (vIdx, uvIdx));
            return map;
        }
    }
}
