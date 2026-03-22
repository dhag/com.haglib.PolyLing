// SkinWeightOps.cs
// BoneWeight操作の純粋静的ユーティリティ
// UnityEditor非依存 → Runtime/に移行可能

using UnityEngine;

namespace Poly_Ling.UI
{
    public static class SkinWeightOps
    {
        public static BoneWeight SetBoneWeight(BoneWeight bw, int boneIndex, float weight)
        {
            weight = Mathf.Clamp01(weight);
            var slots = Extract(bw);
            int t = -1;
            for (int i = 0; i < 4; i++) if (slots[i].idx == boneIndex && slots[i].w > 0f) { t = i; break; }
            if (t < 0) t = FindSlot(slots);
            float otherTotal = 0f;
            for (int i = 0; i < 4; i++) if (i != t) otherTotal += slots[i].w;
            slots[t] = (boneIndex, weight);
            float remaining = 1f - weight;
            if (otherTotal > 0.0001f)
                for (int i = 0; i < 4; i++)
                    if (i != t) slots[i].w *= remaining / otherTotal;
            return Pack(slots);
        }

        public static BoneWeight AddBoneWeight(BoneWeight bw, int boneIndex, float amount)
        {
            var slots = Extract(bw);
            int t = -1;
            for (int i = 0; i < 4; i++) if (slots[i].idx == boneIndex && slots[i].w > 0f) { t = i; break; }
            if (t < 0) t = FindSlot(slots);
            slots[t] = (boneIndex, Mathf.Clamp01(slots[t].w + amount));
            return Pack(slots);
        }

        public static BoneWeight ScaleBoneWeight(BoneWeight bw, int boneIndex, float scale)
        {
            var slots = Extract(bw);
            for (int i = 0; i < 4; i++) if (slots[i].idx == boneIndex) { slots[i].w = Mathf.Clamp01(slots[i].w * scale); break; }
            return Pack(slots);
        }

        public static BoneWeight NormalizeBoneWeight(BoneWeight bw)
        {
            float total = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
            if (total < 0.0001f) return bw;
            float inv = 1f / total;
            bw.weight0 *= inv; bw.weight1 *= inv; bw.weight2 *= inv; bw.weight3 *= inv;
            return bw;
        }

        public static BoneWeight SortBoneWeight(BoneWeight bw)
        {
            var slots = Extract(bw);
            System.Array.Sort(slots, (a, b) => b.w.CompareTo(a.w));
            return Pack(slots);
        }

        public static (int idx, float w)[] Extract(BoneWeight bw) => new[]
        {
            (bw.boneIndex0, bw.weight0), (bw.boneIndex1, bw.weight1),
            (bw.boneIndex2, bw.weight2), (bw.boneIndex3, bw.weight3),
        };

        public static BoneWeight Pack((int idx, float w)[] s) => new BoneWeight
        {
            boneIndex0 = s[0].idx, weight0 = s[0].w,
            boneIndex1 = s[1].idx, weight1 = s[1].w,
            boneIndex2 = s[2].idx, weight2 = s[2].w,
            boneIndex3 = s[3].idx, weight3 = s[3].w,
        };

        public static int FindSlot((int idx, float w)[] slots)
        {
            for (int i = 0; i < 4; i++) if (slots[i].w <= 0f) return i;
            int minSlot = 0;
            for (int i = 1; i < 4; i++) if (slots[i].w < slots[minSlot].w) minSlot = i;
            return minSlot;
        }
    }
}
