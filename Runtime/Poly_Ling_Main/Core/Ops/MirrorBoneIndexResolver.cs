// Runtime/Poly_Ling_Main/Core/Ops/MirrorBoneIndexResolver.cs
// ボーンの左右対応（MeshObject.MirrorBoneIndex）を名前から補完する。
//
// 【なぜ要るか】
//   MirrorBoneIndex を書くのは MeshFilterToSkinnedConverter だけである。
//   PMX からインポートしたスキンドモデルは全ボーンが -1 のままで、
//   MirrorPair.BuildBonePairMap が空の対応表しか作れない。その状態でミラーを
//   生成すると、右側のメッシュが左側のボーンで動く。
//
// 【正本を壊さないこと】
//   スキンド変換が確定させた値（-1 以外）は上書きしない。あちらは実体側ボーンと
//   ミラー側ボーンを 1 対 1 で作った瞬間の値で、名前からの推定より確かである。
//   ここで埋めるのは -1 のボーンだけ。
//
// 【推定であることを隠さない】
//   名前からの解決は推定である。解決できた本数と、できなかったボーン名を返し、
//   UI で見せる。黙って埋めると、名前規則が崩れているモデルで
//   「左のボーンで右を動かす」誤りが静かに入り込む。

using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>左右ボーン対応の補完結果。</summary>
    public class MirrorBoneIndexResolveResult
    {
        /// <summary>新たに対応を書き込んだボーンの本数（左右あわせた延べ数）。</summary>
        public int Resolved;

        /// <summary>もともと対応を持っていて触らなかったボーンの本数。</summary>
        public int AlreadySet;

        /// <summary>左右を持たない名前のボーン（センター・上半身など）。</summary>
        public List<string> NoSideNames = new List<string>();

        /// <summary>左右は判るが相方が見つからなかったボーン。</summary>
        public List<string> UnmatchedNames = new List<string>();
    }

    /// <summary>
    /// ボーン名の左右から MirrorBoneIndex を補完する。
    /// </summary>
    public static class MirrorBoneIndexResolver
    {
        /// <summary>
        /// -1 のままのボーンについて、名前の左右を入れ替えた相方を探して
        /// MirrorBoneIndex を双方向に書き込む。
        /// </summary>
        public static MirrorBoneIndexResolveResult Resolve(ModelContext model)
        {
            var result = new MirrorBoneIndexResolveResult();
            if (model?.MeshContextList == null) return result;

            // ボーン名 → 索引。同名ボーンがある場合は先勝ち（後続は相方として引けない）。
            var nameToIndex = new Dictionary<string, int>();
            var boneIndices = new List<int>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;

                boneIndices.Add(i);
                string nm = mc.Name;
                if (string.IsNullOrEmpty(nm)) continue;
                if (!nameToIndex.ContainsKey(nm)) nameToIndex[nm] = i;
            }

            foreach (int i in boneIndices)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                // 確定値には触らない。
                if (mc.MirrorBoneIndex >= 0)
                {
                    result.AlreadySet++;
                    continue;
                }

                string swapped = MirrorNameOps.SwapLeftRight(mc.Name);
                if (string.IsNullOrEmpty(swapped))
                {
                    // センター・上半身など。左右の対を持たないので -1 のままが正しい。
                    result.NoSideNames.Add(mc.Name);
                    continue;
                }

                if (!nameToIndex.TryGetValue(swapped, out int peer) || peer == i)
                {
                    result.UnmatchedNames.Add(mc.Name);
                    continue;
                }

                var peerCtx = model.GetMeshContext(peer);
                if (peerCtx == null || peerCtx.Type != MeshType.Bone)
                {
                    result.UnmatchedNames.Add(mc.Name);
                    continue;
                }

                // 相方が別の確定値を持っているなら壊さない。
                if (peerCtx.MirrorBoneIndex >= 0 && peerCtx.MirrorBoneIndex != i)
                {
                    result.UnmatchedNames.Add(mc.Name);
                    continue;
                }

                mc.MirrorBoneIndex     = peer;
                peerCtx.MirrorBoneIndex = i;
                result.Resolved++;
            }

            return result;
        }
    }
}
