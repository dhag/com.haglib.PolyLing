// MeshModifierAddDefaultOps.cs
// 追加メニューの既定値（IMeshModifierAddDefaults）が使う計算。
// Runtime/Poly_Ling_Main/Core/Modifiers/ に配置
//
// 【正本】PolyLing_モディファイア操作統合計画.md 2.3・段 3

using Poly_Ling.Data;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    public static class MeshModifierAddDefaultOps
    {
        /// <summary>頂点の外接箱の中心と大きさ。頂点が無ければ false。</summary>
        public static bool TryBounds(MeshObject mesh, out Vector3 center, out Vector3 size)
        {
            center = Vector3.zero;
            size   = Vector3.zero;
            if (mesh?.Vertices == null || mesh.Vertices.Count == 0) return false;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var v in mesh.Vertices)
            {
                if (v == null) continue;
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
            }
            center = (min + max) * 0.5f;
            size   = max - min;
            return true;
        }

        /// <summary>いちばん長い辺の長さ。大きさが無ければ fallback。</summary>
        public static float Longest(Vector3 size, float fallback = 1f)
        {
            float l = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            return l > 1e-6f ? l : fallback;
        }
    }
}
