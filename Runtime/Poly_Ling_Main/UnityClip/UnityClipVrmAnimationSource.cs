// UnityClipVrmAnimationSource.cs
// ============================================================
// UnityClipDTO を VRM アニメーション（.vrma）として書き出すための橋渡し
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/UnityClip/ に配置。
//
// ============================================================
// ■ 何をするか
// ============================================================
//
//   1. UnityClipApplier が持つ仮想骨格から「ボーンだけの GameObject 骨格」を組む。
//   2. UnityClipApplier でモデルへ 1 フレームずつクリップを適用し、
//      その結果のノード・ワールド行列を骨格へ写す。
//   3. PLVrmAnimationBridge 経由で .vrma を書き出す。
//
// ============================================================
// ■ 骨格の出どころを UnityClipVirtualSkeleton にする理由（重要・削除禁止）
// ============================================================
//
//   model.HumanoidMapping.BoneIndexMap を素で読んではならない。
//   半身モデル（ミラー枝を持つモデル）では、左右の Humanoid ボーンが
//   同じ MeshContextList 索引を指す。素で読むと索引が衝突し、
//   右半身の肩・腕・ひじ・手首・足・ひざ・足首が丸ごと落ちる。
//   さらに落ちた骨の子が左側にぶら下がって、手指やつま先が反対側へ飛ぶ。
//
//   UnityClipVirtualSkeleton は
//     ・ミラー枝の関節を両側のノードへ複製し（BuildMirrorNodes）
//     ・左右名を入れ替えてミラーノードへ Humanoid を補完する（BuildHumanoidMap）
//   ところまで済ませてある。VRM 1.0 書き出し側（HumanoidTransformMap）も
//   同じ規則で左右を解決しているので、ここを揃えると .vrm と .vrma で
//   Humanoid ボーンの集合と親子関係が一致する。
//
// ============================================================
// ■ レストを RestWorldMatrix から取る理由（重要・削除禁止）
// ============================================================
//
//   ctx.WorldMatrix は BonePoseData を含む「そのときの姿勢」である。
//   これをレストにすると、パネルでクリップを再生した状態のまま書き出したときに
//   レストが T ポーズでなくなり、全 Humanoid ボーンへ一定の回転オフセットが乗る。
//   UnityClipApplier.ResetAllBones は "UnityClip" 層しか消さないので、
//   これだけでは防げない。
//
//   UnityClipVirtualSkeleton.RestWorldMatrix は BoneTransform だけを累積する
//   （BonePoseData を含まない）ため姿勢に依存しない。ミラーノードには
//   共役 S·H·S が掛かった値が返る。
//
// ============================================================
// ■ レスト姿勢を単位回転で組む理由（重要・削除禁止）
// ============================================================
//
//   VRMA の回転は「正規化 Humanoid のローカル回転」として解釈される。
//   受け側は骨格のレストを基準に読むため、レストで各ノードのローカル回転が
//   単位でないとそのぶんずれる。BVH 経路が「レストポーズが TPose であること」を
//   条件にしている（VrmAnimationMenu.cs:53-55）のと同じ理由。
//
//   そこで骨格は次のように組む。
//     レスト   … 位置はレストのワールド位置（× Scale）、回転は単位。
//     フレーム … ワールド回転を  D_j = R_j · R0_j⁻¹  にする。
//                R_j  … フレーム時のノード・ワールド回転
//                R0_j … レスト時のノード・ワールド回転
//
//   このとき VrmAnimationExporter.RotationExporter が出す値は
//     Inverse(parent.rotation) · Node.rotation = D_parent⁻¹ · D_j
//   となり、レストでは単位、フレームでは「親の回転を差し引いた自分の回転」になる。
//
//   ワールド回転を親から順に代入するので、親を回した時点で子の値は一度崩れるが、
//   全ボーンへ明示的に代入し直すため最終値は正しい。位置は Hips 以外書き出さない
//   （VrmAnimationExporter.cs:102-121 は Hips の translation のみ）ため、
//   子の位置がレストから動いても出力に影響しない。
//
// ============================================================
// ■ HierarchyBuilder を使わない理由
// ============================================================
//
//   HierarchyBuilder には「ボーンだけ出す」選択肢が無い
//   （HierarchyBuildOptions.cs:39-51。ExportMeshOnly は逆向き）。
//   VrmAnimationExporter.Export は base.Export()
//   （VrmAnimationExporter.cs:83）を通るので、メッシュを持つ階層を渡すと
//   メッシュとマテリアルまで .vrma に載る。VRMA に要るのはノード階層だけ。
//
// ============================================================
// ■ モデルの姿勢を壊さないこと
// ============================================================
//
//   書き出しはモデルへ実際にフレームを適用する（プレビューと同じ経路）。
//   終わったら UnityClipApplier.ResetAllBones でポーズ層を必ず戻す。
//   パネル側の表示フレームの復帰は呼び出し側（受け口）が行う。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.HierarchyIO;
using Poly_Ling.Vrm;

namespace Poly_Ling.UnityClip
{
    /// <summary>
    /// VRMA 書き出し用の一時骨格。使い終わったら必ず Dispose する。
    /// </summary>
    public sealed class UnityClipVrmAnimationSource : IDisposable
    {
        // ── 骨格 ──────────────────────────────────────────────────────────

        /// <summary>骨格の根。書き出し対象には含まれない。</summary>
        public GameObject Root { get; private set; }

        /// <summary>Humanoid ボーン → 骨格上の Transform。</summary>
        public readonly Dictionary<HumanBodyBones, Transform> HumanBones
            = new Dictionary<HumanBodyBones, Transform>();

        /// <summary>骨格に載せられなかった Humanoid 名（診断用）。</summary>
        public readonly List<string> Dropped = new List<string>();

        // 親から子の順に並べたボーン。ワールド回転の代入順に使う。
        private readonly List<Transform>  _ordered      = new List<Transform>();
        private readonly List<int>        _orderedNode  = new List<int>();
        private readonly List<Quaternion> _restWorldInv = new List<Quaternion>();

        private float     _scale = 1f;
        private Transform _hips;
        private int       _hipsNode = -1;

        private UnityClipVrmAnimationSource() { }

        // ================================================================
        // 構築
        // ================================================================

        /// <summary>
        /// 仮想骨格の Humanoid 割当から VRMA 用の骨格を組む。
        /// レストは BonePoseData を含まない値を使うので、モデルの現在姿勢に依存しない。
        /// </summary>
        /// <returns>失敗時は null。reason に理由が入る。</returns>
        public static UnityClipVrmAnimationSource Build(
            ModelContext model, UnityClipVirtualSkeleton skeleton, float scale, out string reason)
        {
            reason = null;

            if (model == null) { reason = "モデルがありません"; return null; }
            if (skeleton == null || skeleton.Nodes.Count == 0)
            { reason = "骨格が構築されていません"; return null; }
            if (skeleton.HumanoidToNode.Count == 0)
            { reason = "Humanoid 割り当てがありません"; return null; }

            var src = new UnityClipVrmAnimationSource { _scale = (scale > 0f ? scale : 1f) };

            // ── 1. Humanoid 名 → ノード ／ HumanBodyBones ──────────────────
            var boneByNode = new Dictionary<int, HumanBodyBones>();
            foreach (var kv in skeleton.HumanoidToNode)
            {
                int node = kv.Value;
                if (node < 0 || node >= skeleton.Nodes.Count) { src.Dropped.Add(kv.Key + "(ノード無し)"); continue; }

                var hbb = HumanoidTransformMap.ToHumanBodyBones(kv.Key);
                if (hbb == HumanBodyBones.LastBone) { src.Dropped.Add(kv.Key + "(HumanBodyBones 不明)"); continue; }

                // ここでの衝突は仮想骨格側の異常。落とした事実を必ず残す。
                if (boneByNode.ContainsKey(node))
                {
                    src.Dropped.Add($"{kv.Key}(ノード {node} が {boneByNode[node]} と重複)");
                    continue;
                }
                boneByNode[node] = hbb;
            }

            if (boneByNode.Count == 0) { reason = "Humanoid 割り当てを解決できません"; return null; }

            int hipsNode = -1;
            foreach (var kv in boneByNode)
                if (kv.Value == HumanBodyBones.Hips) { hipsNode = kv.Key; break; }

            if (hipsNode < 0) { reason = "Hips が割り当てられていません"; return null; }

            // ── 2. Humanoid 親（骨格をさかのぼって最初に見つかる Humanoid ノード）──
            var parentNode = new Dictionary<int, int>();
            foreach (int node in boneByNode.Keys)
                parentNode[node] = FindHumanoidAncestor(model, skeleton, boneByNode, node);

            // ── 3. 親から子の順に並べる ───────────────────────────────────
            var order  = new List<int>();
            var placed = new HashSet<int>();
            int guard  = boneByNode.Count + 1;
            while (order.Count < boneByNode.Count && guard-- > 0)
            {
                bool added = false;
                foreach (int node in boneByNode.Keys)
                {
                    if (placed.Contains(node)) continue;
                    int p = parentNode[node];
                    if (p >= 0 && !placed.Contains(p)) continue;
                    order.Add(node);
                    placed.Add(node);
                    added = true;
                }
                if (!added) break;
            }
            if (order.Count < boneByNode.Count)
            { reason = "Humanoid ボーンの親子関係が循環しています"; return null; }

            // ── 4. GameObject 骨格を組む ──────────────────────────────────
            src.Root = new GameObject("PolyLingVrmAnimation");
            src.Root.transform.position   = Vector3.zero;
            src.Root.transform.rotation   = Quaternion.identity;
            src.Root.transform.localScale = Vector3.one;

            var trByNode = new Dictionary<int, Transform>();

            foreach (int node in order)
            {
                var hbb = boneByNode[node];

                // ノード名は HumanBodyBones の列挙名。階層内で一意になる。
                // VrmAnimationExporter がノード索引を名前で逆引きするため
                // （VrmAnimationExporter.cs:97, 117, 139）、重複させてはならない。
                var t = new GameObject(hbb.ToString()).transform;

                int p = parentNode[node];
                t.SetParent(p >= 0 && trByNode.ContainsKey(p) ? trByNode[p] : src.Root.transform, false);

                Matrix4x4 w = skeleton.RestWorldMatrix(model, node);
                t.position   = new Vector3(w.m03, w.m13, w.m23) * src._scale;
                t.rotation   = Quaternion.identity;
                t.localScale = Vector3.one;

                trByNode[node] = t;
                src.HumanBones[hbb] = t;
                src._ordered.Add(t);
                src._orderedNode.Add(node);
                src._restWorldInv.Add(Quaternion.Inverse(SafeRotation(w)));

                if (hbb == HumanBodyBones.Hips) { src._hips = t; src._hipsNode = node; }
            }

            return src;
        }

        // 骨格をさかのぼって最初に見つかる Humanoid ノード。無ければ -1。
        // ミラーノードの親は ParentNode（ミラー側）、実体側へ抜けるときは
        // ParentContextIndex で表される（UnityClipVirtualSkeleton.BuildMirrorNodes）。
        private static int FindHumanoidAncestor(
            ModelContext model, UnityClipVirtualSkeleton skeleton,
            Dictionary<int, HumanBodyBones> boneByNode, int node)
        {
            int guard = skeleton.Nodes.Count + 1;
            int cur   = ParentOf(model, skeleton, node);
            while (cur >= 0 && guard-- > 0)
            {
                if (boneByNode.ContainsKey(cur)) return cur;
                cur = ParentOf(model, skeleton, cur);
            }
            return -1;
        }

        // ノードの親ノード。
        //   ミラーノード … ParentNode（ミラー側の親）を持つ。
        //   実体ノード   … ParentContextIndex（MeshContextList 索引）を持つ。
        //                  その索引がノードでない場合があるので、ノードに当たるまで
        //                  MeshContext の階層をさかのぼる。
        private static int ParentOf(ModelContext model, UnityClipVirtualSkeleton skeleton, int node)
        {
            if (node < 0 || node >= skeleton.Nodes.Count) return -1;
            var n = skeleton.Nodes[node];
            if (n.ParentNode >= 0) return n.ParentNode;

            var list = model.MeshContextList;
            int ci = n.ParentContextIndex;
            int guard = (list != null ? list.Count : 0) + 1;
            while (ci >= 0 && list != null && ci < list.Count && guard-- > 0)
            {
                int pn = skeleton.NodeOfContext(ci);
                if (pn >= 0) return pn;
                var c = list[ci];
                if (c == null) break;
                ci = c.HierarchyParentIndex;
            }
            return -1;
        }

        // 行列の回転部。HierarchyBuilder.cs:243 が使う Matrix4x4.rotation にそろえる。
        // 列が縮退しているときだけ単位を返す。
        private static Quaternion SafeRotation(Matrix4x4 m)
        {
            Vector3 fwd = new Vector3(m.m02, m.m12, m.m22);
            Vector3 up  = new Vector3(m.m01, m.m11, m.m21);
            if (fwd.sqrMagnitude <= 1e-12f || up.sqrMagnitude <= 1e-12f)
                return Quaternion.identity;
            return m.rotation;
        }

        // ================================================================
        // 姿勢の転写
        // ================================================================

        /// <summary>
        /// ノード索引から、そのフレームのノード・ワールド行列を返す口。
        /// UnityClip 経路は UnityClipApplier.TryGetNodeWorldMatrix を、
        /// VMD 経路は VmdNodeWorldSampler.TryGetNodeWorldMatrix を渡す。
        /// 骨格の組み方（レスト単位回転・D_j = R_j·R0_j⁻¹）は経路によらず同じ。
        /// </summary>
        public delegate bool NodeWorldGetter(int node, out Matrix4x4 world);

        /// <summary>
        /// 直近の ApplyFrame の結果を骨格へ写す。
        /// 純仮想ミラー関節は MeshContext を持たないため、必ず
        /// UnityClipApplier.TryGetNodeWorldMatrix から取る。
        /// </summary>
        public void PoseFromModel(UnityClipApplier applier)
        {
            if (applier == null) return;
            PoseFrom(applier.TryGetNodeWorldMatrix);
        }

        /// <summary>
        /// 渡された口から読んだノードのワールド行列を骨格へ写す。
        /// 姿勢を作る側（どのアプライヤで動かしたか）に依存しない。
        /// </summary>
        public void PoseFrom(NodeWorldGetter get)
        {
            if (get == null) return;

            // Hips の位置。回転を代入すると子が動くので、位置は先に入れる。
            if (_hips != null && _hipsNode >= 0 &&
                get(_hipsNode, out Matrix4x4 hw))
            {
                _hips.position = new Vector3(hw.m03, hw.m13, hw.m23) * _scale;
            }

            // ワールド回転を親から順に代入する。
            for (int i = 0; i < _ordered.Count; i++)
            {
                if (!get(_orderedNode[i], out Matrix4x4 w)) continue;
                _ordered[i].rotation = SafeRotation(w) * _restWorldInv[i];
            }
        }

        // ================================================================
        // 後始末
        // ================================================================

        public void Dispose()
        {
            if (Root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(Root);
                else                       UnityEngine.Object.DestroyImmediate(Root);
                Root = null;
            }
            _ordered.Clear();
            _orderedNode.Clear();
            _restWorldInv.Clear();
            HumanBones.Clear();
            Dropped.Clear();
            _hips = null;
            _hipsNode = -1;
        }

        // ================================================================
        // 書き出し本体
        // ================================================================

        /// <summary>
        /// クリップをモデルへ適用しながら .vrma を書き出す。
        /// モデルのポーズ層は終了時に必ず戻す。
        /// </summary>
        /// <param name="model">対象モデル。Humanoid 割り当てが要る。</param>
        /// <param name="clip">適用するクリップ。</param>
        /// <param name="muscleLimitCsvText">UnityLimit CSV の中身。空なら既定値を使う。</param>
        /// <param name="outputPath">出力先（実経路）。</param>
        /// <param name="settings">倍率・毎秒枚数・区間。null なら既定値。</param>
        public static VrmAnimationExportResult ExportToFile(
            ModelContext model,
            UnityClipDTO clip,
            string muscleLimitCsvText,
            string outputPath,
            VrmAnimationExportSettings settings)
        {
            if (model == null) return VrmAnimationExportResult.Failed("モデルがありません");
            if (clip == null)  return VrmAnimationExportResult.Failed("クリップがありません");
            if (string.IsNullOrEmpty(outputPath))
                return VrmAnimationExportResult.Failed("出力パスが空です");

            settings = settings ?? VrmAnimationExportSettings.CreateDefault();

            if (!PLVrmAnimationBridge.I.IsAvailable)
                return VrmAnimationExportResult.Failed("VRM アニメーション エクスポータが利用できません");

            float fps = settings.Fps > 0f ? settings.Fps
                      : (clip.frameRate > 0f ? clip.frameRate : 30f);

            float clipEnd = ComputeMaxTime(clip);
            float start   = Mathf.Max(0f, settings.StartSec);
            float end     = settings.EndSec > start ? settings.EndSec : clipEnd;
            if (end < start) end = start;

            int frameCount = Mathf.Max(1, Mathf.RoundToInt((end - start) * fps) + 1);

            var times = new float[frameCount];
            for (int i = 0; i < frameCount; i++)
                times[i] = start + i / fps;

            var applier = new UnityClipApplier();
            if (!string.IsNullOrEmpty(muscleLimitCsvText))
                applier.LoadMuscleLimitCsv(muscleLimitCsvText);
            applier.BuildMapping(model);

            UnityClipVrmAnimationSource src = null;
            try
            {
                // レストは BonePoseData を含まない値から取るので、
                // ここでポーズを外す必要はない。
                src = Build(model, applier.Skeleton, settings.Scale, out string buildReason);
                if (src == null) return VrmAnimationExportResult.Failed(buildReason);

                var localSrc = src;
                var result = PLVrmAnimationBridge.I.Export(
                    src.Root,
                    src.HumanBones,
                    src.Root.transform,
                    times,
                    i =>
                    {
                        applier.ApplyFrame(model, clip, times[i]);
                        localSrc.PoseFromModel(applier);
                    },
                    outputPath);

                if (result != null && result.Success)
                {
                    result.HumanoidBoneCount = src.HumanBones.Count;
                    result.FrameCount        = frameCount;
                    result.DurationSec       = end - start;
                    result.OutputPath        = outputPath;
                    if (src.Dropped.Count > 0)
                        result.Warning = "骨格に載せられなかった Humanoid: "
                                       + string.Join(", ", src.Dropped.ToArray());
                }
                return result ?? VrmAnimationExportResult.Failed("書き出し結果がありません");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityClipVrmAnimationSource] {ex}");
                return VrmAnimationExportResult.Failed(ex.Message);
            }
            finally
            {
                if (src != null) src.Dispose();
                applier.ResetAllBones(model);
            }
        }

        /// <summary>クリップの最終キー時刻（秒）。</summary>
        public static float ComputeMaxTime(UnityClipDTO clip)
        {
            float max = 0f;
            if (clip == null) return 0f;

            if (clip.bones != null)
            {
                foreach (var track in clip.bones)
                {
                    if (track?.keys == null) continue;
                    foreach (var key in track.keys)
                        if (key != null && key.t > max) max = key.t;
                }
            }

            if (clip.muscles != null)
            {
                foreach (var track in clip.muscles)
                {
                    if (track?.w == null) continue;
                    foreach (var key in track.w)
                        if (key != null && key.t > max) max = key.t;
                }
            }

            return max;
        }
    }
}
