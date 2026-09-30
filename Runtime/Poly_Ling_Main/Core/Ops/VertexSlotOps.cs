// VertexSlotOps.cs
// 頂点の UV・法線スロットの整理。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【規約】（MeshBridgeDefault）UVs.Count == Normals.Count、面の UVIndices[k] == NormalIndices[k]。
//   UV と法線は同じ番号の 1 組なので、消すときは両方を消し、両方の番号を付け替える。
//   ToUnityMesh は (頂点, スロット) ごとに Unity の頂点を 1 つ作るので、どの面も使わない
//   スロットを残すと、使われない Unity の頂点が増える。

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class VertexSlotOps
    {
        /// <summary>
        /// 候補のスロットのうち、今どの面も使っていないものを消し、その頂点を指す全ての面の
        /// スロット番号を付け替える。候補に無いスロットは、使われていなくても残す
        /// （呼び出し側の操作と関係なく元から使われていないスロットには触れない）。
        /// </summary>
        /// <param name="candidates">頂点番号 → 消してよいスロット番号の組。</param>
        public static void CompactUnusedSlots(MeshObject mo, Dictionary<int, HashSet<int>> candidates)
        {
            if (mo == null || candidates == null || candidates.Count == 0) return;

            // 候補の頂点を指す角の一覧（全ての面。線分も含む）。
            var cornersOf = new Dictionary<int, List<(Face face, int k)>>();
            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                for (int k = 0; k < face.VertexCount; k++)
                {
                    int vi = face.VertexIndices[k];
                    if (!candidates.ContainsKey(vi)) continue;
                    if (!cornersOf.TryGetValue(vi, out var list)) cornersOf[vi] = list = new List<(Face, int)>();
                    list.Add((face, k));
                }
            }

            foreach (var kv in candidates)
            {
                int vi = kv.Key;
                if (vi < 0 || vi >= mo.VertexCount) continue;
                cornersOf.TryGetValue(vi, out var corners);
                var usedNow = new HashSet<int>();
                if (corners != null)
                    foreach (var (face, k) in corners)
                    {
                        if (k < face.UVIndices.Count)     usedNow.Add(face.UVIndices[k]);
                        if (k < face.NormalIndices.Count) usedNow.Add(face.NormalIndices[k]);
                    }

                var remove = new List<int>();
                foreach (int slot in kv.Value) if (!usedNow.Contains(slot)) remove.Add(slot);
                if (remove.Count == 0) continue;
                remove.Sort();
                remove.Reverse();   // 大きい番号から消すと、小さい番号の付け替えがずれない

                var v = mo.Vertices[vi];
                foreach (int slot in remove)
                {
                    if (slot < 0 || slot >= v.UVs.Count) continue;
                    v.UVs.RemoveAt(slot);
                    if (slot < v.Normals.Count) v.Normals.RemoveAt(slot);
                    if (corners == null) continue;
                    foreach (var (face, k) in corners)
                    {
                        if (k < face.UVIndices.Count     && face.UVIndices[k]     > slot) face.UVIndices[k]--;
                        if (k < face.NormalIndices.Count && face.NormalIndices[k] > slot) face.NormalIndices[k]--;
                    }
                }
            }
        }

        /// <summary>
        /// 頂点の組ごとに、その頂点を使う面（線分も含む）が指しているスロット番号の組を集める。
        /// 面を 1 回だけ走査する。
        /// </summary>
        public static Dictionary<int, HashSet<int>> UsedSlots(MeshObject mo, ICollection<int> vertices)
        {
            var result = new Dictionary<int, HashSet<int>>();
            if (mo == null || vertices == null) return result;
            foreach (int vi in vertices) result[vi] = new HashSet<int>();
            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                for (int k = 0; k < face.VertexCount; k++)
                {
                    if (!result.TryGetValue(face.VertexIndices[k], out var set)) continue;
                    if (k < face.UVIndices.Count)     set.Add(face.UVIndices[k]);
                    if (k < face.NormalIndices.Count) set.Add(face.NormalIndices[k]);
                }
            }
            return result;
        }
    }
}
