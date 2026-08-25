// VertexDataTransferOps.cs
// モデル間・オブジェクト間の頂点データ転送。
//
// 【設計方針】
//   1. 対応付けは呼び出し側が明示する。メッシュのペアは 1 組ずつ受け取り、
//      「順序で暗黙に決まる」ことは無い（既存の部分インポートはリスト順依存で、
//      手作業で細かく合わせにくかった）。
//   2. 頂点の対応付けは既定でインデックス。頂点IDは信頼できない状態になりやすいので
//      （未設定・重複・誤付与）、使う場合は呼び出し側が VertexIdOps で事前確認する前提。
//   3. 実行前に必ず件数を返せるようにする（Preview と Execute で同じ対応表を使う）。
//
// 【ボーンウェイトの注意】
//   Vertex.BoneWeight の boneIndex は「その頂点が属するモデルの MeshContextList の
//   インデックス」（MeshObject.cs の Vertex.BoneWeight コメント参照）。
//   モデルをまたぐと同じ番号が別のボーンを指すため、必ずボーン名で引き直す。
//   引き直せないボーンがあるウェイトは転送しない（黙って別のボーンに付けない）。
//
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    /// <summary>頂点の対応付け方式。</summary>
    public enum VertexMatchMode
    {
        /// <summary>頂点インデックスで対応付ける（既定）。頂点数が違う場合は min(N,M) 個。</summary>
        Index,
        /// <summary>頂点IDで対応付ける。未設定・重複IDは対象外（VertexIdOps 参照）。</summary>
        VertexId,
    }

    /// <summary>転送する項目。</summary>
    [Flags]
    public enum VertexDataKind
    {
        None             = 0,
        Position         = 1 << 0,
        UVs              = 1 << 1,
        Normals          = 1 << 2,
        Flags            = 1 << 3,
        BoneWeight       = 1 << 4,
        MirrorBoneWeight = 1 << 5,
        VertexId         = 1 << 6,
        /// <summary>MorphBaseData（BasePositions / BaseNormals / BaseUVs）。</summary>
        MorphBase        = 1 << 7,
        /// <summary>パーツ選択辞書（頂点インデックスを対応表で読み替える）。</summary>
        PartsSelectionSet = 1 << 8,
    }

    /// <summary>1 ペア分の転送結果。</summary>
    public class VertexTransferResult
    {
        public string SourceName = "";
        public string TargetName = "";

        /// <summary>対応が取れた頂点数。</summary>
        public int Matched;
        /// <summary>転送先で対応が取れなかった頂点数。</summary>
        public int Unmatched;

        /// <summary>実際に書き換えた頂点数（項目が 1 つでも入ったもの）。</summary>
        public int Written;

        /// <summary>名前で引き直せずウェイトを転送しなかった頂点数。</summary>
        public int BoneRemapFailed;

        /// <summary>転送した選択辞書の数。</summary>
        public int SelectionSetsCopied;

        public readonly List<string> Warnings = new List<string>();

        public string Summary =>
            $"{SourceName} → {TargetName}: 対応 {Matched} / 未対応 {Unmatched} / 書込 {Written}";
    }

    public static class VertexDataTransferOps
    {
        // ================================================================
        // 対応表の構築
        // ================================================================

        /// <summary>
        /// 転送先頂点インデックス → 転送元頂点インデックス の対応表を作る。
        /// Preview と Execute で同じものを使い、表示と実行がずれないようにする。
        /// </summary>
        public static Dictionary<int, int> BuildVertexMap(
            MeshContext src, MeshContext dst, VertexMatchMode mode, out int unmatched)
        {
            var map = new Dictionary<int, int>();
            unmatched = 0;

            var srcMo = src?.MeshObject;
            var dstMo = dst?.MeshObject;
            if (srcMo == null || dstMo == null) return map;

            if (mode == VertexMatchMode.Index)
            {
                int n = Math.Min(srcMo.VertexCount, dstMo.VertexCount);
                for (int i = 0; i < n; i++) map[i] = i;
                unmatched = dstMo.VertexCount - n;
                return map;
            }

            // VertexId: 未設定・重複を除いた使えるIDだけで突き合わせる。
            var srcIdToIndex = BuildUsableIdMap(srcMo);
            for (int i = 0; i < dstMo.VertexCount; i++)
            {
                int id = dstMo.Vertices[i].Id;
                if (!MeshObject.IsUnsetId(id) && srcIdToIndex.TryGetValue(id, out int si))
                    map[i] = si;
                else
                    unmatched++;
            }
            return map;
        }

        /// <summary>未設定・重複を除いた ID → インデックス の辞書。</summary>
        private static Dictionary<int, int> BuildUsableIdMap(MeshObject mo)
        {
            var map = new Dictionary<int, int>();
            var dup = new HashSet<int>();
            for (int i = 0; i < mo.VertexCount; i++)
            {
                int id = mo.Vertices[i].Id;
                if (MeshObject.IsUnsetId(id)) continue;
                if (map.ContainsKey(id)) { dup.Add(id); continue; }
                map[id] = i;
            }
            // 重複IDはどの頂点か決められないので使わない。
            foreach (var id in dup) map.Remove(id);
            return map;
        }

        // ================================================================
        // プレビュー（書き換えなし）
        // ================================================================

        public static VertexTransferResult Preview(
            MeshContext src, MeshContext dst, VertexMatchMode mode)
        {
            var result = new VertexTransferResult
            {
                SourceName = src?.Name ?? "(none)",
                TargetName = dst?.Name ?? "(none)",
            };
            var map = BuildVertexMap(src, dst, mode, out int unmatched);
            result.Matched   = map.Count;
            result.Unmatched = unmatched;
            return result;
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>
        /// 1 ペア分を転送する。MeshObject の頂点数・面構成は変更しない。
        /// </summary>
        /// <param name="srcModel">転送元モデル（ボーン名解決に使う）</param>
        /// <param name="dstModel">転送先モデル（ボーン名解決に使う）</param>
        public static VertexTransferResult Transfer(
            ModelContext srcModel, MeshContext src,
            ModelContext dstModel, MeshContext dst,
            VertexMatchMode mode, VertexDataKind kinds)
        {
            var result = new VertexTransferResult
            {
                SourceName = src?.Name ?? "(none)",
                TargetName = dst?.Name ?? "(none)",
            };

            var srcMo = src?.MeshObject;
            var dstMo = dst?.MeshObject;
            if (srcMo == null || dstMo == null)
            {
                result.Warnings.Add("メッシュが空です");
                return result;
            }
            if (kinds == VertexDataKind.None)
            {
                result.Warnings.Add("転送項目が選択されていません");
                return result;
            }

            var map = BuildVertexMap(src, dst, mode, out int unmatched);
            result.Matched   = map.Count;
            result.Unmatched = unmatched;
            if (map.Count == 0)
            {
                result.Warnings.Add("対応が 1 件も取れませんでした");
                return result;
            }

            // ボーン番号の読み替え表（転送元 MeshContextList index → 転送先 index）。
            Dictionary<int, int> boneRemap = null;
            bool needBone = kinds.HasFlag(VertexDataKind.BoneWeight)
                         || kinds.HasFlag(VertexDataKind.MirrorBoneWeight);
            if (needBone)
            {
                boneRemap = BuildBoneRemap(srcModel, dstModel, out var boneWarn);
                if (!string.IsNullOrEmpty(boneWarn)) result.Warnings.Add(boneWarn);
            }

            foreach (var kv in map)
            {
                int di = kv.Key, si = kv.Value;
                if (di < 0 || di >= dstMo.VertexCount) continue;
                if (si < 0 || si >= srcMo.VertexCount) continue;

                var s = srcMo.Vertices[si];
                var d = dstMo.Vertices[di];
                bool wrote = false;

                if (kinds.HasFlag(VertexDataKind.Position))
                { d.Position = s.Position; wrote = true; }

                if (kinds.HasFlag(VertexDataKind.UVs))
                { d.UVs = new List<Vector2>(s.UVs); wrote = true; }

                if (kinds.HasFlag(VertexDataKind.Normals))
                { d.Normals = new List<Vector3>(s.Normals); wrote = true; }

                if (kinds.HasFlag(VertexDataKind.Flags))
                { d.Flags = s.Flags; wrote = true; }

                if (kinds.HasFlag(VertexDataKind.BoneWeight))
                {
                    if (TryRemapWeight(s.BoneWeight, boneRemap, out var bw)) { d.BoneWeight = bw; wrote = true; }
                    else if (s.BoneWeight.HasValue) result.BoneRemapFailed++;
                }

                if (kinds.HasFlag(VertexDataKind.MirrorBoneWeight))
                {
                    if (TryRemapWeight(s.MirrorBoneWeight, boneRemap, out var mbw)) { d.MirrorBoneWeight = mbw; wrote = true; }
                    else if (s.MirrorBoneWeight.HasValue) result.BoneRemapFailed++;
                }

                if (kinds.HasFlag(VertexDataKind.VertexId))
                { d.Id = s.Id; wrote = true; }

                if (wrote) result.Written++;
            }

            // ウェイトを転送した場合は転送先の種別を確定させる。
            // 転送先が MeshFilter 系でもウェイトが入り得るため、ここは無 → 有の遷移点。
            if (kinds.HasFlag(VertexDataKind.BoneWeight))
                dstMo.RecomputeSkinKind();

            // UVs / Normals を差し替えた場合、面が参照するスロット番号が
            // 範囲外になり得るのでここで詰め直す。
            if (kinds.HasFlag(VertexDataKind.UVs) || kinds.HasFlag(VertexDataKind.Normals))
            {
                int clamped = ClampFaceSlotIndices(dstMo,
                    kinds.HasFlag(VertexDataKind.UVs),
                    kinds.HasFlag(VertexDataKind.Normals));
                if (clamped > 0)
                    result.Warnings.Add($"面の参照スロットを {clamped} 箇所補正しました");
            }

            if (kinds.HasFlag(VertexDataKind.VertexId))
            {
                dstMo.RebuildIdSets();
                var dupReport = VertexIdOps.Inspect(dst);
                if (dupReport.DuplicateIdCount > 0)
                    result.Warnings.Add(
                        $"転送後に重複IDが {dupReport.DuplicateIdCount} 種 "
                      + $"({dupReport.DuplicatedVertexCount} 頂点) あります");
            }

            if (kinds.HasFlag(VertexDataKind.MorphBase))
                TransferMorphBase(src, dst, map, result);

            if (kinds.HasFlag(VertexDataKind.PartsSelectionSet))
                TransferSelectionSets(src, dst, map, result);

            if (kinds.HasFlag(VertexDataKind.Position))
                dstMo.InvalidatePositionCache();

            if (result.BoneRemapFailed > 0)
                result.Warnings.Add(
                    $"ボーン名を引き直せず {result.BoneRemapFailed} 頂点のウェイトを転送しませんでした");

            return result;
        }

        // ================================================================
        // ボーン番号の読み替え
        // ================================================================

        /// <summary>
        /// 転送元モデルのボーン index → 転送先モデルのボーン index を名前で作る。
        /// 名前が一致しないボーンは表に入れない（＝そのウェイトは転送しない）。
        /// </summary>
        public static Dictionary<int, int> BuildBoneRemap(
            ModelContext srcModel, ModelContext dstModel, out string warning)
        {
            var map = new Dictionary<int, int>();
            warning = null;
            if (srcModel?.MeshContextList == null || dstModel?.MeshContextList == null) return map;

            var dstByName = new Dictionary<string, int>();
            for (int i = 0; i < dstModel.MeshContextList.Count; i++)
            {
                var mc = dstModel.MeshContextList[i];
                if (mc == null || mc.Type != MeshType.Bone) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (!dstByName.ContainsKey(mc.Name)) dstByName[mc.Name] = i;
            }

            int srcBones = 0, missing = 0;
            for (int i = 0; i < srcModel.MeshContextList.Count; i++)
            {
                var mc = srcModel.MeshContextList[i];
                if (mc == null || mc.Type != MeshType.Bone) continue;
                srcBones++;
                if (!string.IsNullOrEmpty(mc.Name) && dstByName.TryGetValue(mc.Name, out int di))
                    map[i] = di;
                else
                    missing++;
            }

            if (missing > 0)
                warning = $"ボーン {srcBones} 本のうち {missing} 本が転送先に見つかりません（名前一致）";
            return map;
        }

        /// <summary>
        /// ウェイトのボーン番号を読み替える。
        /// 重み 0 のスロットは番号を見ない。1 つでも引けないボーンがあれば転送しない。
        /// </summary>
        private static bool TryRemapWeight(
            BoneWeight? srcWeight, Dictionary<int, int> boneRemap, out BoneWeight? remapped)
        {
            remapped = null;
            if (!srcWeight.HasValue) return false;
            if (boneRemap == null) return false;

            var w = srcWeight.Value;
            var idx = new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
            var wgt = new[] { w.weight0,    w.weight1,    w.weight2,    w.weight3    };

            for (int i = 0; i < 4; i++)
            {
                if (wgt[i] <= 0f) { idx[i] = 0; continue; }
                if (!boneRemap.TryGetValue(idx[i], out int di)) return false;
                idx[i] = di;
            }

            remapped = new BoneWeight
            {
                boneIndex0 = idx[0], weight0 = wgt[0],
                boneIndex1 = idx[1], weight1 = wgt[1],
                boneIndex2 = idx[2], weight2 = wgt[2],
                boneIndex3 = idx[3], weight3 = wgt[3],
            };
            return true;
        }

        // ================================================================
        // 面の参照スロット補正
        // ================================================================

        /// <summary>
        /// UVs / Normals を差し替えた後、面が持つスロット番号が頂点の
        /// リスト長を超えていたら 0 に丸める。丸めた箇所数を返す。
        /// </summary>
        private static int ClampFaceSlotIndices(MeshObject mo, bool uv, bool normal)
        {
            int clamped = 0;
            foreach (var face in mo.Faces)
            {
                var vidx = face.VertexIndices;
                for (int c = 0; c < vidx.Count; c++)
                {
                    int vi = vidx[c];
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    var v = mo.Vertices[vi];

                    if (uv && c < face.UVIndices.Count)
                    {
                        int slot = face.UVIndices[c];
                        if (slot < 0 || slot >= v.UVs.Count) { face.UVIndices[c] = 0; clamped++; }
                    }
                    if (normal && c < face.NormalIndices.Count)
                    {
                        int slot = face.NormalIndices[c];
                        if (slot < 0 || slot >= v.Normals.Count) { face.NormalIndices[c] = 0; clamped++; }
                    }
                }
            }
            return clamped;
        }

        // ================================================================
        // MorphBaseData
        // ================================================================

        /// <summary>
        /// モーフ基準データを対応表で読み替えて転送する。
        /// 転送先がモーフでない場合は MorphBaseData を新規に作る。
        /// 対応の取れなかった頂点は転送先の現在値を保つ。
        /// </summary>
        private static void TransferMorphBase(
            MeshContext src, MeshContext dst, Dictionary<int, int> map, VertexTransferResult result)
        {
            var sb = src.MorphBaseData;
            if (sb == null)
            {
                result.Warnings.Add("転送元にモーフ基準データがありません");
                return;
            }

            int dstCount = dst.MeshObject.VertexCount;
            var db = dst.MorphBaseData;
            if (db == null)
            {
                db = new MorphBaseData
                {
                    MorphName = sb.MorphName,
                    Panel     = sb.Panel,
                };
                dst.MorphBaseData = db;
            }

            db.BasePositions = RemapArray(sb.BasePositions, db.BasePositions, dstCount, map,
                                          fallback: i => dst.MeshObject.Vertices[i].Position);
            db.BaseNormals   = RemapArray(sb.BaseNormals,   db.BaseNormals,   dstCount, map,
                                          fallback: i => Vector3.zero);
            db.BaseUVs       = RemapArray(sb.BaseUVs,       db.BaseUVs,       dstCount, map,
                                          fallback: i => Vector2.zero);
        }

        /// <summary>
        /// 頂点インデックス配列を対応表で読み替える。
        /// src が null なら null を返す（その項目は転送元に無い）。
        /// </summary>
        private static T[] RemapArray<T>(
            T[] srcArr, T[] dstArr, int dstCount, Dictionary<int, int> map, Func<int, T> fallback)
        {
            if (srcArr == null) return dstArr;

            var result = new T[dstCount];
            for (int i = 0; i < dstCount; i++)
            {
                if (map.TryGetValue(i, out int si) && si >= 0 && si < srcArr.Length)
                    result[i] = srcArr[si];
                else if (dstArr != null && i < dstArr.Length)
                    result[i] = dstArr[i];          // 対応が無ければ既存値を保つ
                else
                    result[i] = fallback(i);
            }
            return result;
        }

        // ================================================================
        // パーツ選択辞書
        // ================================================================

        /// <summary>
        /// 選択辞書を対応表で読み替えて転送する。同名は上書きする。
        /// 面・線分は頂点対応では読み替えられないため、頂点インデックスが
        /// そのまま通じるインデックス一致のときだけ引き継ぐ。
        /// </summary>
        private static void TransferSelectionSets(
            MeshContext src, MeshContext dst, Dictionary<int, int> map, VertexTransferResult result)
        {
            if (src.PartsSelectionSetList == null || src.PartsSelectionSetList.Count == 0)
            {
                result.Warnings.Add("転送元に選択辞書がありません");
                return;
            }

            // 転送元頂点 → 転送先頂点 の逆引き。
            var srcToDst = new Dictionary<int, int>();
            foreach (var kv in map)
                if (!srcToDst.ContainsKey(kv.Value)) srcToDst[kv.Value] = kv.Key;

            if (dst.PartsSelectionSetList == null)
                dst.PartsSelectionSetList = new List<PartsSelectionSet>();

            int droppedNonVertex = 0;

            foreach (var srcSet in src.PartsSelectionSetList)
            {
                if (srcSet == null) continue;

                var newSet = new PartsSelectionSet(srcSet.Name) { Mode = srcSet.Mode };

                foreach (int v in srcSet.Vertices)
                    if (srcToDst.TryGetValue(v, out int dv)) newSet.Vertices.Add(dv);

                foreach (var e in srcSet.Edges)
                    if (srcToDst.TryGetValue(e.V1, out int a) && srcToDst.TryGetValue(e.V2, out int b))
                        newSet.Edges.Add(new VertexPair(a, b));

                // Faces / Lines は面インデックスであり、頂点対応表では読み替えられない。
                // 面構成が同一である保証がないので引き継がない。
                if (srcSet.Faces.Count > 0 || srcSet.Lines.Count > 0) droppedNonVertex++;

                if (newSet.Vertices.Count == 0 && newSet.Edges.Count == 0) continue;

                var existing = dst.FindSelectionSetByName(newSet.Name);
                if (existing != null)
                {
                    int idx = dst.PartsSelectionSetList.IndexOf(existing);
                    dst.PartsSelectionSetList[idx] = newSet;
                }
                else
                {
                    dst.PartsSelectionSetList.Add(newSet);
                }
                result.SelectionSetsCopied++;
            }

            if (droppedNonVertex > 0)
                result.Warnings.Add(
                    $"面/線分ベースの辞書 {droppedNonVertex} 件は頂点対応では読み替えられないため除外しました");
        }
    }
}
