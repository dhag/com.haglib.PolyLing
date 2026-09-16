// BoneWeightBackup.cs
// 頂点ボーンウェイトの控えと書き戻し。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【何のためにあるか】
//   オブジェクトを消すと、その索引を指していたウェイトは行き先を失う。
//   ModelContext.RemapIndexReferences は削除用の対応表に従って
//   「消したボーンのウェイトは親ボーンへ寄せる」ので、寄せた分は
//   逆写像では戻せない（親に元から乗っていた分と区別が付かない）。
//   Undo で元へ戻せるように、書き換える前の値を控えておく。
//
// 【引き当ては ObjectId】
//   Undo は MeshContext を作り直して挿し戻す（MeshContextSnapshot.ToMeshContext）。
//   実体参照では引けないので ObjectId で引く。頂点数が食い違うときは書き戻さない
//   （別の操作で形が変わっている＝この控えはもう当てにできない）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;

namespace Poly_Ling.Data
{
    /// <summary>描画オブジェクト 1 個ぶんの頂点ウェイトの控え。</summary>
    public sealed class BoneWeightBackupEntry
    {
        /// <summary>控えた描画オブジェクトの安定 ID。</summary>
        public ulong ObjectId;

        /// <summary>頂点ごとの BoneWeight。null は「ウェイト無し」。</summary>
        public BoneWeight?[] Weights;

        /// <summary>頂点ごとの MirrorBoneWeight。null は「ウェイト無し」。</summary>
        public BoneWeight?[] MirrorWeights;
    }

    /// <summary>頂点ウェイトの控えと書き戻し。</summary>
    public static class BoneWeightBackup
    {
        /// <summary>1 個ぶんを控える。対象が無ければ null。</summary>
        public static BoneWeightBackupEntry Capture(MeshContext mc)
        {
            var verts = mc?.MeshObject?.Vertices;
            if (verts == null) return null;

            int n = verts.Count;
            var entry = new BoneWeightBackupEntry
            {
                ObjectId      = mc.ObjectId,
                Weights       = new BoneWeight?[n],
                MirrorWeights = new BoneWeight?[n],
            };
            for (int i = 0; i < n; i++)
            {
                var v = verts[i];
                if (v == null) continue;
                entry.Weights[i]       = v.BoneWeight;
                entry.MirrorWeights[i] = v.MirrorBoneWeight;
            }
            return entry;
        }

        /// <summary>ObjectId を指定して、今の値を控える。</summary>
        public static List<BoneWeightBackupEntry> CaptureByObjectIds(
            ModelContext model, IEnumerable<ulong> objectIds)
        {
            if (model == null || objectIds == null) return null;

            var wanted = new HashSet<ulong>(objectIds);
            if (wanted.Count == 0) return null;

            List<BoneWeightBackupEntry> list = null;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || !wanted.Contains(mc.ObjectId)) continue;

                var entry = Capture(mc);
                if (entry == null) continue;
                (list ??= new List<BoneWeightBackupEntry>()).Add(entry);
            }
            return list;
        }

        /// <summary>控えを書き戻す。頂点数が合わない対象は飛ばす。</summary>
        public static void Apply(ModelContext model, List<BoneWeightBackupEntry> backup)
        {
            if (model == null || backup == null || backup.Count == 0) return;

            var byId = new Dictionary<ulong, MeshContext>(model.MeshContextCount);
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null) byId[mc.ObjectId] = mc;
            }

            foreach (var entry in backup)
            {
                if (entry?.Weights == null) continue;
                if (!byId.TryGetValue(entry.ObjectId, out var mc)) continue;

                var verts = mc?.MeshObject?.Vertices;
                if (verts == null || verts.Count != entry.Weights.Length) continue;

                for (int i = 0; i < verts.Count; i++)
                {
                    var v = verts[i];
                    if (v == null) continue;
                    v.BoneWeight       = entry.Weights[i];
                    v.MirrorBoneWeight = entry.MirrorWeights != null && i < entry.MirrorWeights.Length
                                       ? entry.MirrorWeights[i]
                                       : null;
                }
            }
        }
    }
}
