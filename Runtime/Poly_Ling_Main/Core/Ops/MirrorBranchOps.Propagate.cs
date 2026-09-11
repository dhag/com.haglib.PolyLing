// MirrorBranchOps.Propagate.cs
// ミラー分岐：ミラー側への位相変更の伝播（3 系統共通）。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static partial class MirrorBranchOps
    {
        // ================================================================
        // ミラー側への位相変更の伝播（3系統共通）
        //
        // 【RebuildDerivedMirrorGeometry と別に置く理由】
        //   同関数は MirrorGeometryDerived == true だけを対象にする。この値は
        //   「実効ワールドに共役 S·H·S を掛けるか」という描画側の都合で決まり、
        //   MeshFilterToSkinnedConverter は変換時に無条件で false を書く。
        //   PMX 経路も設定しないため false のまま。その結果、
        //   「ユーザーが重みを塗る側のミラー」が丸ごと対象外になっていた。
        //
        //   ミラーの連結そのものの正本は MirrorPairs と BakedMirrorSourceIndex で、
        //   頂点移動の同期（PlayerViewportManager.SyncMeshPositionsAndTransform）も
        //   この2つしか見ていない。ここでも CollectMirrorPeers に一本化する。
        //
        // 【前提: 添字恒等対応】
        //   real.Vertices[v] ↔ mirror.Vertices[v]
        //   real.Faces[f]    ↔ mirror.Faces[f]
        //   mirror.Faces[f].VertexIndices は real 側の逆順
        //
        //   生成ミラー（CreateDerivedMirrorContext）は必ず成立する。スキンド変換は
        //   頂点の並べ替え・増減をせず Position に行列を掛けるだけなので変換後も
        //   保たれる。ファイル由来のミラーは保証が無いため、位相を変える前に
        //   VerifyIdentityCorrespondence で実測し、成立しないペアは触らない。
        //
        // 【伝播のやり方】
        //   ミラー側の頂点・面を実体側から作り直すことはしない。実体側に掛けたのと
        //   同じ位相操作を、同じ添字でミラー側にも掛ける（ApplyToMirrors）。
        //
        //   前提 A が成り立っていれば、両側で同じ面添字・同じ頂点添字を消し、
        //   生き残った面の VertexIndices に同じ再マップを掛けるだけで
        //     mirror'.Faces[f].VertexIndices[j]
        //       = map[ mirror.Faces[f].VertexIndices[j] ]
        //       = map[ real.Faces[f].VertexIndices[n-1-j] ]
        //       = real'.Faces[f].VertexIndices[n-1-j]
        //   となり前提 A がそのまま保たれる。
        //
        //   UVIndices / NormalIndices は頂点内のスロット番号であって頂点添字では
        //   ないため、頂点の削除・再マップの影響を受けない。ミラー側の頂点
        //   オブジェクト（位置・UV・法線・ボーンウェイト）は生き残ったものを
        //   そのまま残すだけなので、実体側から写す作業は一切要らない。
        //
        // 【面内の巻き順の正規化】
        //   面を張り替える操作（Tri4To1 / FaceMerge / VertexDissolve 等）は
        //   面の巻き順に沿って外周を辿るため、ミラー側では逆回りに辿ることになり、
        //   結果の並びが「実体側の逆順」を巡回回転した形になることがある。
        //   幾何としては同じ面だが前提 A の「厳密な逆順」からは外れるため、
        //   操作後に NormalizeMirrorFaceOrder で回転を戻して厳密形に揃える。
        // ================================================================

        /// <summary>
        /// ミラー側への位相変更の伝播計画。位相を変える前に作ること。
        /// </summary>
        public sealed class MirrorRebuildPlan
        {
            public sealed class Entry
            {
                /// <summary>実体側の MeshContextList 索引。</summary>
                public int RealIndex;

                /// <summary>ミラー側の MeshContextList 索引。</summary>
                public int MirrorIndex;

                /// <summary>添字恒等対応が実測で成立したか。false のペアは触らない。</summary>
                public bool Verified;

                /// <summary>成立しなかった理由（Verified == false のとき）。</summary>
                public string RejectReason;
            }

            public readonly List<Entry> Entries = new List<Entry>();

            /// <summary>検証を通ったペア数。</summary>
            public int VerifiedCount
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < Entries.Count; i++) if (Entries[i].Verified) n++;
                    return n;
                }
            }

            /// <summary>検証を落ちたペア数。</summary>
            public int RejectedCount => Entries.Count - VerifiedCount;
        }

        /// <summary>
        /// 実体側の索引からミラー側を引き当て、添字恒等対応の成否を実測して控える。
        /// 位相を変える「前」に呼ぶこと（変更後では対応の検証ができない）。
        /// </summary>
        public static MirrorRebuildPlan CaptureMirrorRebuildPlan(
            ModelContext model, IEnumerable<int> realIndices)
        {
            var plan = new MirrorRebuildPlan();
            if (model?.MeshContextList == null || realIndices == null) return plan;

            var list = model.MeshContextList;
            var seen = new HashSet<int>();

            foreach (int realIndex in realIndices)
            {
                if (realIndex < 0 || realIndex >= list.Count) continue;

                var peers = new List<int>();
                CollectMirrorPeers(model, realIndex, peers);

                foreach (int mirrorIndex in peers)
                {
                    if (mirrorIndex < 0 || mirrorIndex >= list.Count) continue;
                    if (mirrorIndex == realIndex) continue;
                    if (!seen.Add(mirrorIndex)) continue;   // 同じミラーを二重に扱わない

                    var entry = new MirrorRebuildPlan.Entry
                    {
                        RealIndex   = realIndex,
                        MirrorIndex = mirrorIndex,
                    };

                    var realMo   = list[realIndex]?.MeshObject;
                    var mirrorMo = list[mirrorIndex]?.MeshObject;

                    string reason;
                    entry.Verified     = VerifyIdentityCorrespondence(realMo, mirrorMo, out reason);
                    entry.RejectReason = reason;

                    plan.Entries.Add(entry);
                }
            }

            return plan;
        }

        /// <summary>
        /// 実体側とミラー側が添字恒等対応（面内の巻き順は逆順）になっているかを実測する。
        /// </summary>
        private static bool VerifyIdentityCorrespondence(
            MeshObject realMo, MeshObject mirrorMo, out string reason)
        {
            reason = "";

            if (realMo == null)   { reason = "実体側の MeshObject が null";   return false; }
            if (mirrorMo == null) { reason = "ミラー側の MeshObject が null"; return false; }

            if (realMo.Vertices == null || mirrorMo.Vertices == null)
            { reason = "Vertices が null"; return false; }
            if (realMo.Faces == null || mirrorMo.Faces == null)
            { reason = "Faces が null"; return false; }

            if (realMo.Vertices.Count != mirrorMo.Vertices.Count)
            {
                reason = $"頂点数が不一致 real={realMo.Vertices.Count} mirror={mirrorMo.Vertices.Count}";
                return false;
            }
            if (realMo.Faces.Count != mirrorMo.Faces.Count)
            {
                reason = $"面数が不一致 real={realMo.Faces.Count} mirror={mirrorMo.Faces.Count}";
                return false;
            }

            for (int v = 0; v < realMo.Vertices.Count; v++)
            {
                if (realMo.Vertices[v] == null || mirrorMo.Vertices[v] == null)
                { reason = $"頂点 {v} が null"; return false; }
            }

            for (int f = 0; f < realMo.Faces.Count; f++)
            {
                var rf = realMo.Faces[f];
                var mf = mirrorMo.Faces[f];
                if (rf == null || mf == null) { reason = $"面 {f} が null"; return false; }
                if (rf.VertexIndices == null || mf.VertexIndices == null)
                { reason = $"面 {f} の VertexIndices が null"; return false; }

                int n = rf.VertexIndices.Count;
                if (mf.VertexIndices.Count != n)
                {
                    reason = $"面 {f} の頂点数が不一致 real={n} mirror={mf.VertexIndices.Count}";
                    return false;
                }

                for (int j = 0; j < n; j++)
                {
                    if (mf.VertexIndices[j] != rf.VertexIndices[n - 1 - j])
                    {
                        reason = $"面 {f} が逆順恒等でない "
                               + $"(slot {j}: mirror={mf.VertexIndices[j]} real={rf.VertexIndices[n - 1 - j]})";
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 実体側に掛けたのと同じ位相操作を、検証を通ったミラー側にも掛ける。
        /// MirrorGeometryDerived は見ない（3系統すべてが対象）。
        ///
        /// plan は位相を変える「前」に CaptureMirrorRebuildPlan で取っておくこと。
        /// 本メソッドは実体側の操作が終わった「後」に呼ぶ。ミラー側はまだ変更前の
        /// 状態なので、操作に渡す添字は変更前のものをそのまま使える。
        /// </summary>
        /// <param name="apply">
        /// (実体側の索引, ミラー側の MeshObject) を受け取り、実体側と同じ操作を
        /// ミラー側に掛ける。成功したら true。
        /// </param>
        /// <returns>更新したミラー側の数</returns>
        public static int ApplyToMirrors(
            ModelContext model,
            MirrorRebuildPlan plan,
            Func<int, MeshObject, bool> apply)
        {
            if (model?.MeshContextList == null || plan == null || apply == null) return 0;

            var list = model.MeshContextList;
            int materialCount = model.MaterialCount;
            int applied = 0;

            foreach (var entry in plan.Entries)
            {
                if (!entry.Verified)
                {
                    Debug.LogWarning(
                        "[Mirror] 添字恒等対応が成立しないためミラー側へ伝播しませんでした。"
                      + $" real=[{entry.RealIndex}]\"{SafeContextName(list, entry.RealIndex)}\""
                      + $" mirror=[{entry.MirrorIndex}]\"{SafeContextName(list, entry.MirrorIndex)}\""
                      + $" 理由: {entry.RejectReason}");
                    continue;
                }

                var realCtx   = SafeContext(list, entry.RealIndex);
                var mirrorCtx = SafeContext(list, entry.MirrorIndex);
                var realMo    = realCtx?.MeshObject;
                var mirrorMo  = mirrorCtx?.MeshObject;
                if (realMo == null || mirrorMo == null) continue;

                if (!apply(entry.RealIndex, mirrorMo))
                {
                    Debug.LogError(
                        "[Mirror] ミラー側への操作が失敗しました。左右が食い違ったままです。"
                      + $" real=[{entry.RealIndex}]\"{SafeContextName(list, entry.RealIndex)}\""
                      + $" mirror=[{entry.MirrorIndex}]\"{SafeContextName(list, entry.MirrorIndex)}\"");
                    continue;
                }

                // 面を張り替える操作は巻き順に沿って外周を辿るため、ミラー側の結果が
                // 「実体側の逆順」を巡回回転した形になることがある。厳密形へ戻す。
                int rotated = NormalizeMirrorFaceOrder(realMo, mirrorMo, out string normError);
                if (normError != null)
                {
                    Debug.LogError(
                        $"[Mirror] ミラー側の面の並びを揃えられませんでした: {normError}"
                      + $" mirror=[{entry.MirrorIndex}]\"{SafeContextName(list, entry.MirrorIndex)}\"");
                }

                // 操作後にもう一度、前提が保たれているかを実測する。
                if (!VerifyIdentityCorrespondence(realMo, mirrorMo, out string afterReason))
                {
                    Debug.LogError(
                        "[Mirror] 操作後に添字恒等対応が崩れました。Undo で戻してください。"
                      + $" mirror=[{entry.MirrorIndex}]\"{SafeContextName(list, entry.MirrorIndex)}\""
                      + $" 理由: {afterReason}");
                }

                if (rotated > 0)
                    Debug.Log($"[Mirror] ミラー側の面 {rotated} 枚の並びを厳密な逆順へ揃えました"
                            + $" mirror=\"{SafeContextName(list, entry.MirrorIndex)}\"");

                mirrorMo.InvalidatePositionCache();

                // 消えた頂点・面を指したままの選択を残さない。
                mirrorCtx.Selection?.ClearAll();

                mirrorCtx.ReplaceUnityMesh(mirrorMo.ToUnityMesh(materialCount));
                mirrorCtx.OriginalPositions = (Vector3[])mirrorMo.Positions.Clone();

                applied++;
            }

            if (applied > 0)
            {
                RebuildAffectedMirrorPairs(model, plan);

                // 実体側の形が変わったので、ミラー側モーフを Real 側モーフから作り直す。
                // 頂点編集系ツール（DeleteSelection / FaceMerge / VertexDissolve /
                // Tri4To1 / Quad4To1）は全てここを通るため、
                // モーフ同期の共通の合流点になる。
                // 規約は MorphMirrorPolicy.cs を正典とする。
                model.SyncAllMirrorMorphs();
            }

            return applied;
        }

        /// <summary>
        /// ミラー側の各面の並びを「実体側の厳密な逆順」へ回転で揃える。
        /// 巡回回転で一致しない面があれば error に理由を入れる（回転はしない）。
        /// UVIndices / NormalIndices も同じ回転量で揃える（長さが n のときのみ）。
        /// </summary>
        /// <returns>回転した面の数</returns>
        private static int NormalizeMirrorFaceOrder(
            MeshObject realMo, MeshObject mirrorMo, out string error)
        {
            error = null;

            if (realMo?.Faces == null || mirrorMo?.Faces == null)
            {
                error = "Faces が null";
                return 0;
            }
            if (realMo.Faces.Count != mirrorMo.Faces.Count)
            {
                error = $"面数が不一致 real={realMo.Faces.Count} mirror={mirrorMo.Faces.Count}";
                return 0;
            }

            int rotatedCount = 0;

            for (int f = 0; f < realMo.Faces.Count; f++)
            {
                var rf = realMo.Faces[f];
                var mf = mirrorMo.Faces[f];
                if (rf?.VertexIndices == null || mf?.VertexIndices == null)
                {
                    error = $"面 {f} が null";
                    return rotatedCount;
                }

                int n = rf.VertexIndices.Count;
                if (mf.VertexIndices.Count != n)
                {
                    error = $"面 {f} の頂点数が不一致 real={n} mirror={mf.VertexIndices.Count}";
                    return rotatedCount;
                }
                if (n == 0) continue;

                int r = FindReverseRotation(rf.VertexIndices, mf.VertexIndices, n);
                if (r < 0)
                {
                    error = $"面 {f} が巡回回転を含めても逆順一致しません";
                    return rotatedCount;
                }
                if (r == 0) continue;

                RotateLeft(mf.VertexIndices, r, n);
                if (mf.UVIndices     != null && mf.UVIndices.Count     == n) RotateLeft(mf.UVIndices,     r, n);
                if (mf.NormalIndices != null && mf.NormalIndices.Count == n) RotateLeft(mf.NormalIndices, r, n);

                rotatedCount++;
            }

            return rotatedCount;
        }

        /// <summary>
        /// mirror[j] == real[(n-1-j+r) % n] が全 j で成り立つ r を返す。無ければ -1。
        /// </summary>
        private static int FindReverseRotation(List<int> real, List<int> mirror, int n)
        {
            for (int r = 0; r < n; r++)
            {
                bool ok = true;
                for (int j = 0; j < n; j++)
                {
                    int k = ((n - 1 - j + r) % n + n) % n;
                    if (mirror[j] != real[k]) { ok = false; break; }
                }
                if (ok) return r;
            }
            return -1;
        }

        /// <summary>リストを左へ r 個ぶん回転する（先頭が元の r 番目になる）。</summary>
        private static void RotateLeft(List<int> listToRotate, int r, int n)
        {
            if (r <= 0 || n <= 1) return;

            var tmp = new int[n];
            for (int j = 0; j < n; j++) tmp[j] = listToRotate[(j + r) % n];
            for (int j = 0; j < n; j++) listToRotate[j] = tmp[j];
        }

        private static MeshContext SafeContext(IList<MeshContext> list, int index)
        {
            if (list == null || index < 0 || index >= list.Count) return null;
            return list[index];
        }

        private static string SafeContextName(IList<MeshContext> list, int index)
        {
            var mc = SafeContext(list, index);
            return mc?.Name ?? "<範囲外/null>";
        }

        /// <summary>
        /// 作り直したミラー側を含む MirrorPair の対応表を張り直す。
        /// 位相が変わると VertexMap（件数一致が前提）が古くなるため。
        /// </summary>
        private static void RebuildAffectedMirrorPairs(ModelContext model, MirrorRebuildPlan plan)
        {
            if (model?.MirrorPairs == null) return;

            var list = model.MeshContextList;
            if (list == null) return;

            foreach (var entry in plan.Entries)
            {
                if (!entry.Verified) continue;

                var mirrorCtx = SafeContext(list, entry.MirrorIndex);
                if (mirrorCtx == null) continue;

                foreach (var pair in model.MirrorPairs)
                {
                    if (pair == null || pair.Mirror != mirrorCtx) continue;
                    if (!pair.Build())
                        Debug.LogWarning($"[Mirror] ペアの張り直しに失敗しました mirror=\"{mirrorCtx.Name}\"");
                }
            }
        }

        /// <summary>
        /// ミラー軸で法線を反転する。軸コードは MirrorPoint と同じ体系
        /// （2 = Y / 4 = Z / それ以外 = X）。法線は方向なので距離は使わない。
        /// </summary>
        public static Vector3 MirrorNormal(int mirrorAxis, Vector3 normal)
        {
            switch (mirrorAxis)
            {
                case 2:  return new Vector3(normal.x, -normal.y, normal.z);
                case 4:  return new Vector3(normal.x, normal.y, -normal.z);
                default: return new Vector3(-normal.x, normal.y, normal.z);
            }
        }

        /// <summary>ミラー軸の面で位置のみを鏡像化する。</summary>
        public static Vector3 MirrorPoint(int mirrorAxis, float mirrorDistance, Vector3 pos)
        {
            switch (mirrorAxis)
            {
                case 2:  return new Vector3(pos.x, 2f * mirrorDistance - pos.y, pos.z);
                case 4:  return new Vector3(pos.x, pos.y, 2f * mirrorDistance - pos.z);
                default: return new Vector3(2f * mirrorDistance - pos.x, pos.y, pos.z);
            }
        }
    }
}
