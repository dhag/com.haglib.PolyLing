// PartsIdByBoneWeightOps.cs
// ボーンウェイトの組み合わせでパーツID（Vertex.PartsId）を振り直す。
//
// 【頂点IDとの関係】
//   このファイルは Vertex.Id を一切読まないし書かない。PartsIdAssignOps と同じ約束。
//
// 【何を見るか】
//   Vertex.BoneWeight（UnityEngine.BoneWeight?）の 4 スロットだけを見る。
//   MirrorBoneWeight は見ない。位置・面・つながりは一切見ない。
//
//   「ボーン索引が設定されているか」はスロットの索引値からは判定できない。
//   未使用スロットは索引 0 / ウェイト 0 へ正規化されるためで
//   （SkinWeightOperations.cs:104-107）、索引 0 は「ルートボーン」と
//   「未使用」の両方を意味する。
//   したがって唯一の判定基準は「ウェイト > 0」であり、このファイルは
//   ウェイト > 0 のスロットだけを有効として数える。
//
// 【採番の規則】
//   有効スロットのボーン索引を重複除去して昇順に並べたものを S とする。
//
//     S が空          … PartsId = PartsIdOps.UnweightedPartsId（予約値）
//                       BoneWeight が null の頂点、4 スロットとも
//                       ウェイト 0 の頂点がここへ入る
//     S の要素が 1 個 … PartsId = そのボーン索引
//                       同じ索引が 2 スロット以上に入っている頂点も、
//                       重複除去後は 1 個なのでここへ入る（単独扱い）
//     S の要素が 2 個以上
//                     … 同じ S を持つ頂点をひとまとめにして
//                       PartsId = GroupIdOffset + 群の連番
//                       S は昇順に並べてから比べるので 4と5 / 5と4 は同じ群になる
//
//   群の連番は「その群に属する最小頂点インデックスの昇順」で 0 から振る。
//   頂点を先頭から走査して初出の群へ番号を配るだけで昇順になる
//   （PartsIdAssignOps.AssignByConnectivity と同じ規約）。
//
// 【GroupIdOffset（番号空間の分離）】
//   単独ウェイトの頂点はボーン索引そのものを PartsId にするため、
//   群の番号を 0 から始めると両者が同じ値を取り、区別できなくなる。
//   そこで群の番号はボーン索引が取り得る範囲の外から始める。
//
//     GroupIdOffset = max(boneCount, 実際に使われたボーン索引の最大値 + 1)
//
//   boneCount は ModelContext.MeshContextList.Count（ボーン索引の定義域。
//   MeshObject.cs:123 の「boneIndex = _meshContextList のインデックス」）。
//   壊れたデータで索引が定義域を超えていても衝突しないよう、実データ側の
//   最大値とも突き合わせる。
//
// 【予約値を int.MaxValue にした理由】
//   -1 は使えない。MQO の頂点識別子は COL(PartsID, SubID, ID) で往復するが、
//   書き出しは (uint)partsId（VertexIdHelper.cs:414）、読み戻しは
//   「負なら 0」（VertexIdHelper.cs:374）なので、-1 は 0 に潰れる。
//   PartsId = 0 は「未設定」の定義（MeshObject.cs:97-100）でもあるため、
//   ウェイト無しの頂点と未設定の頂点が見分けられなくなる。
//   int.MaxValue（2147483647）なら非負なので clamp されず、往復で保たれる。
//   実体は PartsIdOps.UnweightedPartsId。
//
// 【配置】 Runtime/Poly_Ling_Main/Core/Ops/

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>ボーンウェイトによるパーツID採番の実行結果。</summary>
    public struct PartsIdByBoneWeightResult
    {
        /// <summary>書き込んだか。false なら 1 頂点も変更していない。</summary>
        public bool Success;

        /// <summary>対象の頂点数。</summary>
        public int VertexCount;

        /// <summary>有効ボーンが 1 個だった頂点の数。</summary>
        public int SingleVertexCount;

        /// <summary>単独ウェイトで実際に使われたボーンの種類数。</summary>
        public int SingleBoneKindCount;

        /// <summary>有効ボーンが 2 個以上だった頂点の数。</summary>
        public int GroupVertexCount;

        /// <summary>組み合わせの種類数（＝群の数）。</summary>
        public int GroupCount;

        /// <summary>有効ボーンが 1 個も無く、予約値を付けた頂点の数。</summary>
        public int UnweightedVertexCount;

        /// <summary>群の連番の開始値。</summary>
        public int GroupIdOffset;

        /// <summary>失敗の理由。成功時は空。</summary>
        public string Reason;

        public static PartsIdByBoneWeightResult Fail(string reason)
            => new PartsIdByBoneWeightResult { Success = false, Reason = reason ?? "" };

        public string Summary =>
            $"頂点 {VertexCount}"
          + $" / 単独 {SingleVertexCount}（ボーン {SingleBoneKindCount} 種）"
          + $" / 組み合わせ {GroupVertexCount}（{GroupCount} 種・ID {GroupIdOffset} から）"
          + $" / ウェイト無し {UnweightedVertexCount}";
    }

    /// <summary>ボーンウェイトの組み合わせによるパーツID採番。Vertex.Id には触れない。</summary>
    public static class PartsIdByBoneWeightOps
    {
        /// <summary>
        /// ボーンウェイトの組み合わせでパーツIDを振り直し、続けてサブIDを振り直す。
        /// </summary>
        /// <param name="mo">対象メッシュ。</param>
        /// <param name="boneCount">
        /// ボーン索引の定義域。ModelContext.MeshContextList.Count を渡す。
        /// 0 以下でも失敗にはせず、実データ側の最大値だけで GroupIdOffset を決める。
        /// </param>
        public static PartsIdByBoneWeightResult AssignByBoneWeight(MeshObject mo, int boneCount)
        {
            if (mo == null || mo.Vertices == null || mo.Vertices.Count == 0)
                return PartsIdByBoneWeightResult.Fail("対象メッシュに頂点がありません");

            int n = mo.Vertices.Count;

            // ── 1 周目：使われているボーン索引の最大値を取る ─────────────
            // 群の番号をボーン索引と衝突しない位置から始めるために要る。
            int maxUsedBone = -1;
            for (int i = 0; i < n; i++)
            {
                var v = mo.Vertices[i];
                if (v == null || !v.HasBoneWeight) continue;

                var bw = v.BoneWeight.Value;
                if (bw.weight0 > 0f && bw.boneIndex0 > maxUsedBone) maxUsedBone = bw.boneIndex0;
                if (bw.weight1 > 0f && bw.boneIndex1 > maxUsedBone) maxUsedBone = bw.boneIndex1;
                if (bw.weight2 > 0f && bw.boneIndex2 > maxUsedBone) maxUsedBone = bw.boneIndex2;
                if (bw.weight3 > 0f && bw.boneIndex3 > maxUsedBone) maxUsedBone = bw.boneIndex3;
            }

            int offset = (boneCount > maxUsedBone + 1) ? boneCount : maxUsedBone + 1;
            if (offset < 0) offset = 0;

            // 群の番号が予約値へ届く状況では書き込まない。
            // 実際には起きないが、届いた瞬間にウェイト無しと区別できなくなるので止める。
            if (offset >= PartsIdOps.UnweightedPartsId)
                return PartsIdByBoneWeightResult.Fail(
                    $"ボーン索引の範囲 {offset} が大きすぎて群の番号を確保できません");

            // ── 2 周目：採番 ──────────────────────────────────────────────
            var groupIds  = new Dictionary<string, int>();
            var usedBones = new HashSet<int>();
            var slots     = new List<int>(4);

            int singleVertexCount     = 0;
            int groupVertexCount      = 0;
            int unweightedVertexCount = 0;

            for (int i = 0; i < n; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;

                slots.Clear();
                if (v.HasBoneWeight)
                {
                    var bw = v.BoneWeight.Value;
                    AddValidBone(slots, bw.boneIndex0, bw.weight0);
                    AddValidBone(slots, bw.boneIndex1, bw.weight1);
                    AddValidBone(slots, bw.boneIndex2, bw.weight2);
                    AddValidBone(slots, bw.boneIndex3, bw.weight3);
                }

                if (slots.Count == 0)
                {
                    v.PartsId = PartsIdOps.UnweightedPartsId;
                    unweightedVertexCount++;
                    continue;
                }

                if (slots.Count == 1)
                {
                    v.PartsId = slots[0];
                    usedBones.Add(slots[0]);
                    singleVertexCount++;
                    continue;
                }

                // 昇順に並べてから鍵にする。4と5 / 5と4 が同じ群になるのはここ。
                slots.Sort();

                string key = MakeKey(slots);
                if (!groupIds.TryGetValue(key, out int groupId))
                {
                    groupId = groupIds.Count;
                    groupIds[key] = groupId;
                }

                v.PartsId = offset + groupId;
                groupVertexCount++;
            }

            // サブIDはパーツID依存なので、パーツIDを書いた直後に必ず振り直す。
            PartsIdOps.AssignSubIdByPartsId(mo);

            return new PartsIdByBoneWeightResult
            {
                Success               = true,
                VertexCount           = n,
                SingleVertexCount     = singleVertexCount,
                SingleBoneKindCount   = usedBones.Count,
                GroupVertexCount      = groupVertexCount,
                GroupCount            = groupIds.Count,
                UnweightedVertexCount = unweightedVertexCount,
                GroupIdOffset         = offset,
                Reason                = "",
            };
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// 有効なスロットだけを重複なしで足す。
        /// 判定はウェイト > 0 のみ。索引値では判定しない（ファイル頭の注記を参照）。
        /// 負の索引は書き込むと PartsId が負になり MQO 往復で 0 へ潰れるため捨てる。
        /// </summary>
        private static void AddValidBone(List<int> list, int boneIndex, float weight)
        {
            if (weight <= 0f) return;
            if (boneIndex < 0) return;

            for (int i = 0; i < list.Count; i++)
                if (list[i] == boneIndex) return;

            list.Add(boneIndex);
        }

        /// <summary>
        /// 昇順に並んだボーン索引列から群の鍵を作る。要素は最大 4 個。
        /// </summary>
        private static string MakeKey(List<int> sortedBones)
        {
            // 要素数が最大 4 なので、素直に連結する。
            switch (sortedBones.Count)
            {
                case 2:
                    return sortedBones[0] + "," + sortedBones[1];
                case 3:
                    return sortedBones[0] + "," + sortedBones[1] + "," + sortedBones[2];
                default:
                    return sortedBones[0] + "," + sortedBones[1] + ","
                         + sortedBones[2] + "," + sortedBones[3];
            }
        }
    }
}
