// SpringBoneChainNaming.cs
// 揺れもの鎖を名前から組み立てる。
// Runtime/Poly_Ling_Main/Tools/SpringBoneRig/ に配置
//
// 【命名規則】（SpringBoneLadderPlacer.cs:890-905 と同じ）
//   鎖が 1 本     … {接頭辞}_top → {接頭辞}_1 → {接頭辞}_2 … → {接頭辞}_end
//   鎖が複数本   … {接頭辞}_{番号:00}_top → _1 → _2 … → _end
//
// 【なぜここに置くか】
//   揺れ方を付ける処理は、検証パネル 2 枚（フリル / パイプ）がそれぞれ同じ関数を持ち、
//   索引を 1 本ずつ撃っていた。手本に記録すると索引の直書きが数十段並ぶ。
//   組み立てをここへ寄せ、applySpringBoneByPrefix コマンドとパネルの両方から使う。

using System;
using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Tools.SpringBoneRig
{
    public static class SpringBoneChainNaming
    {
        /// <summary>
        /// 接頭辞から鎖を組み立てる。戻り値は鎖ごとのボーン masterIndex の列（根元→先）。
        /// </summary>
        public static List<List<int>> CollectChainsByName(ModelContext model, string prefix)
        {
            var chains = new List<List<int>>();
            if (model == null || string.IsNullOrEmpty(prefix)) return chains;

            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (!byName.ContainsKey(mc.Name)) byName[mc.Name] = i;
            }

            // 鎖 1 本だけの形も、番号付きの形も同じ手順で拾う。
            var heads = new List<string>();
            if (byName.ContainsKey(prefix + "_top")) heads.Add(prefix);
            for (int c = 0; c < 1000; c++)
            {
                string name = $"{prefix}_{c:00}";
                if (!byName.ContainsKey(name + "_top")) break;
                heads.Add(name);
            }

            foreach (string head in heads)
            {
                var chain = new List<int> { byName[head + "_top"] };
                for (int k = 1; k < 4096; k++)
                {
                    if (!byName.TryGetValue($"{head}_{k}", out int idx)) break;
                    chain.Add(idx);
                }
                if (byName.TryGetValue(head + "_end", out int tail)) chain.Add(tail);
                chains.Add(chain);
            }

            return chains;
        }

        /// <summary>鎖の名前。鎖が 1 本なら接頭辞そのもの。</summary>
        public static string ChainName(string prefix, int index, int chainCount)
            => chainCount > 1 ? $"{prefix}_{index:00}" : prefix;
    }
}
