// UnityClipCanonVrmAnimation.cs
// ============================================================
// Unity クリップ（UnityClipDTO）→ VRM アニメーション（.vrma）
// モデル非依存・T ポーズ前提の変換
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/UnityClip/ に配置。
//
// ============================================================
// ■ モデルを使わない理由と、それで足りる理由
// ============================================================
//
//   Unity の Humanoid マッスルは正規化された無次元量で、モデルに依存しない。
//   T ポーズ基準の定義値（UnityClipApplier.CanonMuscleTable）から
//   そのままローカル回転を作れるので、ModelContext も Humanoid 割り当ても要らない。
//
//   VRMC_vrm_animation が持つのは humanBones のノード索引だけで
//   （com.vrmc.vrm/Runtime/Format/Animation/Format.g.cs）、骨の長さも
//   プロポーションも入らない。回転チャンネルは Inverse(parent)·node という
//   無次元量である。長さに依存しうるのは Hips の平行移動だけだが、
//   受け側は Hips の高さ比で正規化する。
//   よって骨長は任意でよく、ここでは一律 BoneLength（既定 0.1）を使う。
//
// ============================================================
// ■ ローカル回転を直接入れる
// ============================================================
//
//   正準骨格はレストで全ノードのローカル回転が単位なので、
//   t.localRotation = L_j と置けば VrmAnimationExporter.RotationExporter が出す
//     Inverse(parent.rotation) · Node.rotation
//   がそのまま L_j になる。既存のモデル依存経路が使う
//   D_j = R_j·R0_j⁻¹（ワールド差分）を経由する必要がない。
//
// ============================================================
// ■ 定数は複製しない
// ============================================================
//
//   Zero・dof 軸・可動端・正準階層・T ポーズ方向はすべて
//   UnityClipApplier が唯一の置き場である。ここでは
//   TryGetCanonLocalRotation / TryGetCanonParent / TryGetCanonDir /
//   CanonHumanoidNames を通して引くだけで、値を書き写さない。
//
// ============================================================
// ■ Hips の平行移動と身体の向き（RootT / RootQ）
// ============================================================
//
//   Humanoid クリップは Transform トラックを持たない。代わりに Animator 型の
//   カーブとして RootT.x/y/z・RootQ.x/y/z/w を持ち、書き出し側はこれを
//   dto.muscles へそのまま入れている（UnityClipExportWindow.cs:246-251）。
//
//   RootT / RootQ は HumanTrait.MuscleName に含まれないが、
//   **含まれないことは不要を意味しない。** マッスルが関節の正規化自由度なのに対し、
//   これらは身体全体の位置と向き（HumanPose.bodyPosition / bodyRotation）である。
//   意味と単位は UnityClipRootMotion の冒頭に書いた。
//
//   反映先は 2 つとも Hips。
//     ・平行移動 … 書き出し側は SetPositionBoneAndParent(hips, 正準骨格の根) で
//       Hips の translation を出す（VrmAnimationExporterImpl.cs:109）。
//       Hips の親は正準骨格の根なので、hips.localPosition がそのまま出る。
//     ・向き … 回転チャンネルは Inverse(parent.rotation)·node.rotation なので
//       （VrmAnimationExporterImpl.cs:120）、根を回しても打ち消されて出ない。
//       Hips のローカル回転へ入れるしかない。
//       Hips に対応するマッスルは無い（正準階層の根。UnityClipApplier.cs:903）ので、
//       マッスル由来の回転と衝突しない。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Vrm;

namespace Poly_Ling.UnityClip
{
    /// <summary>
    /// T ポーズ基準の正準骨格。使い終わったら必ず Dispose する。
    /// </summary>
    public sealed class UnityClipCanonVrmAnimation : IDisposable
    {
        /// <summary>骨格の根。書き出し対象には含まれない。</summary>
        public GameObject Root { get; private set; }

        /// <summary>Humanoid ボーン → 骨格上の Transform。</summary>
        public readonly Dictionary<HumanBodyBones, Transform> HumanBones
            = new Dictionary<HumanBodyBones, Transform>();

        // 親から子の順。ローカル回転の代入に使う。
        private readonly List<string>    _orderedName = new List<string>();
        private readonly List<Transform> _ordered     = new List<Transform>();

        /// <summary>Hips のレスト位置（根から見たローカル）。平行移動の基準。</summary>
        public Vector3 HipsRestLocalPosition { get; private set; }

        // Root の反映に使う持ち回り。
        private UnityClipRootMotion _root;
        private float              _rootScale = 1f;
        private bool               _applyRootT;
        private bool               _applyRootQ;
        private Quaternion         _prevRootQ = Quaternion.identity;
        private bool               _hasPrevRootQ;

        private UnityClipCanonVrmAnimation() { }

        // ================================================================
        // 骨格の構築
        // ================================================================

        // CanonDirTable に無いボーンの方向。末端は子を持たないので不要。
        //   Hand … 前腕の延長
        //   Foot … T ポーズでつま先は前方
        //   Head … Neck と同じ
        private static bool ResolveDir(string name, out Vector3 dir)
        {
            if (UnityClipApplier.TryGetCanonDir(name, out dir)) return true;

            if (name == "LeftHand")  return UnityClipApplier.TryGetCanonDir("LeftLowerArm",  out dir);
            if (name == "RightHand") return UnityClipApplier.TryGetCanonDir("RightLowerArm", out dir);
            if (name == "LeftFoot" || name == "RightFoot") { dir = new Vector3(0f, 0f, 1f); return true; }
            if (name == "Head") { dir = new Vector3(0f, 1f, 0f); return true; }

            dir = Vector3.zero;
            return false;
        }

        /// <summary>
        /// T ポーズの正準骨格を組む。位置は「親の方向 × boneLength」。
        /// レストのローカル回転はすべて単位。
        /// </summary>
        public static UnityClipCanonVrmAnimation Build(float boneLength, out string reason)
        {
            reason = null;
            float len = boneLength > 0f ? boneLength : 0.1f;

            var names = UnityClipApplier.CanonHumanoidNames;
            if (names == null || names.Count == 0)
            { reason = "正準テーブルが空です"; return null; }

            // ── 1. HumanBodyBones へ解釈できるものだけ採る ──────────────
            var hbbOf = new Dictionary<string, HumanBodyBones>();
            foreach (var n in names)
            {
                if (!Enum.TryParse<HumanBodyBones>(n, out var hbb)) continue;
                if (hbb == HumanBodyBones.LastBone) continue;
                hbbOf[n] = hbb;
            }
            if (!hbbOf.ContainsKey("Hips")) { reason = "Hips が正準テーブルにありません"; return null; }

            // ── 2. 親から子の順に並べる ───────────────────────────────
            var order  = new List<string>();
            var placed = new HashSet<string>();
            int guard  = hbbOf.Count + 1;
            while (order.Count < hbbOf.Count && guard-- > 0)
            {
                bool added = false;
                foreach (var n in hbbOf.Keys)
                {
                    if (placed.Contains(n)) continue;
                    UnityClipApplier.TryGetCanonParent(n, out string p);
                    if (!string.IsNullOrEmpty(p) && hbbOf.ContainsKey(p) && !placed.Contains(p)) continue;
                    order.Add(n);
                    placed.Add(n);
                    added = true;
                }
                if (!added) break;
            }
            if (order.Count < hbbOf.Count)
            { reason = "正準階層が循環しています"; return null; }

            // ── 3. GameObject を組む ──────────────────────────────────
            var src = new UnityClipCanonVrmAnimation();
            src.Root = new GameObject("PolyLingCanonVrmAnimation");
            src.Root.transform.position   = Vector3.zero;
            src.Root.transform.rotation   = Quaternion.identity;
            src.Root.transform.localScale = Vector3.one;

            var trOf = new Dictionary<string, Transform>();

            foreach (var n in order)
            {
                var hbb = hbbOf[n];

                // ノード名は HumanBodyBones の列挙名。階層内で一意になる。
                // VrmAnimationExporter がノード索引を名前で逆引きするため
                // （VrmAnimationExporter.cs:97, 117, 139）、重複させてはならない。
                var t = new GameObject(hbb.ToString()).transform;

                UnityClipApplier.TryGetCanonParent(n, out string parentName);
                bool hasParent = !string.IsNullOrEmpty(parentName) && trOf.ContainsKey(parentName);
                t.SetParent(hasParent ? trOf[parentName] : src.Root.transform, false);

                if (hasParent && ResolveDir(parentName, out Vector3 dir))
                    t.localPosition = dir * len;
                else if (!hasParent)
                    t.localPosition = new Vector3(0f, len, 0f);   // Hips は根から真上へ
                else
                    t.localPosition = Vector3.zero;               // 方向不明。長さは読まれない

                t.localRotation = Quaternion.identity;
                t.localScale    = Vector3.one;

                trOf[n] = t;
                src.HumanBones[hbb] = t;
                src._orderedName.Add(n);
                src._ordered.Add(t);
            }

            // Hips のレスト位置。RootT の倍率を決める基準になるので控えておく。
            src.HipsRestLocalPosition = trOf.TryGetValue("Hips", out var hipsTr)
                ? hipsTr.localPosition
                : new Vector3(0f, len, 0f);

            return src;
        }

        // ================================================================
        // 姿勢
        // ================================================================

        /// <summary>
        /// timeSec のマッスル値から各ボーンのローカル回転を作って骨格へ入れる。
        /// クリップが駆動していないボーンは単位のまま（レスト＝T ポーズ）。
        /// </summary>
        public void PoseFromMuscles(
            IReadOnlyDictionary<string, UnityMuscleTrackDTO> muscleByName, float timeSec)
        {
            for (int i = 0; i < _ordered.Count; i++)
            {
                _ordered[i].localRotation =
                    UnityClipApplier.TryGetCanonLocalRotation(
                        _orderedName[i], muscleByName, timeSec, out Quaternion local)
                    ? local
                    : Quaternion.identity;
            }
        }

        /// <summary>
        /// Root 情報の反映を仕込む。ConvertToFile が 1 回だけ呼ぶ。
        ///
        /// referenceTimeSec は倍率の基準時刻（書き出しの開始時刻）。
        /// 倍率を決められないときは平行移動を載せない。基準の無いまま掛けると
        /// 寸法が化けるので、黙って 1 倍で掛けてはいけない。
        /// </summary>
        public void SetupRoot(UnityClipRootMotion root, float referenceTimeSec,
                              bool useRoot, out string note)
        {
            _root         = root;
            _applyRootT   = false;
            _applyRootQ   = false;
            _rootScale    = 1f;
            _prevRootQ    = Quaternion.identity;
            _hasPrevRootQ = false;

            if (root == null)                       { note = "Root トラックなし";       return; }
            if (!useRoot)                           { note = "Root を載せない指定";     return; }
            if (!root.HasTranslation && !root.HasRotation)
                                                    { note = "Root トラックなし";       return; }

            var sb = new System.Text.StringBuilder();

            if (root.HasTranslation)
            {
                _applyRootT = root.TryComputeTranslationScale(
                    HipsRestLocalPosition.y, referenceTimeSec, out _rootScale, out string scaleNote);
                sb.Append(_applyRootT ? "RootT: " : "RootT: 載せない（");
                sb.Append(scaleNote);
                if (!_applyRootT) sb.Append('）');
            }
            else sb.Append("RootT: 無し");

            sb.Append(" / ");
            _applyRootQ = root.HasRotation;
            sb.Append(_applyRootQ ? "RootQ: 載せる" : "RootQ: 無し（4 本そろわない）");

            note = sb.ToString();
        }

        /// <summary>
        /// マッスルと Root を両方反映する。書き出しのフレームごとに呼ぶ。
        /// 時刻の昇順で呼ぶこと（RootQ の符号を前フレームへ合わせるため）。
        /// </summary>
        public void Pose(
            IReadOnlyDictionary<string, UnityMuscleTrackDTO> muscleByName, float timeSec)
        {
            PoseFromMuscles(muscleByName, timeSec);
            PoseRoot(timeSec);
        }

        /// <summary>
        /// Root 情報を Hips へ入れる。
        ///   平行移動 … hips.localPosition = RootT(正規化) × 倍率
        ///   向き     … hips.localRotation = RootQ
        /// Hips に対応するマッスルは無いので、マッスル由来の回転を潰すことはない。
        /// </summary>
        public void PoseRoot(float timeSec)
        {
            if (_root == null) return;
            if (!HumanBones.TryGetValue(HumanBodyBones.Hips, out var hips) || hips == null) return;

            if (_applyRootT)
                hips.localPosition = _root.SampleNormalizedTranslation(timeSec) * _rootScale;

            if (_applyRootQ)
            {
                var q = _root.SampleRotation(timeSec);
                if (_hasPrevRootQ) q = UnityClipRootMotion.MatchSign(q, _prevRootQ);
                _prevRootQ    = q;
                _hasPrevRootQ = true;

                hips.localRotation = q;
            }
        }

        public void Dispose()
        {
            if (Root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(Root);
                else                       UnityEngine.Object.DestroyImmediate(Root);
                Root = null;
            }
            _ordered.Clear();
            _orderedName.Clear();
            HumanBones.Clear();
        }

        // ================================================================
        // 変換本体
        // ================================================================

        /// <summary>
        /// クリップを T ポーズ基準の正準骨格へ載せて .vrma を書き出す。
        /// ModelContext を一切参照しない。
        /// </summary>
        public static VrmAnimationExportResult ConvertToFile(
            UnityClipDTO clip, string outputPath, VrmAnimationExportSettings settings, float boneLength,
            bool useRoot = true)
        {
            if (clip == null) return VrmAnimationExportResult.Failed("クリップがありません");
            if (string.IsNullOrEmpty(outputPath))
                return VrmAnimationExportResult.Failed("出力パスが空です");

            settings = settings ?? VrmAnimationExportSettings.CreateDefault();

            if (!PLVrmAnimationBridge.I.IsAvailable)
                return VrmAnimationExportResult.Failed("VRM アニメーション エクスポータが利用できません");

            if (clip.muscles == null || clip.muscles.Count == 0)
                return VrmAnimationExportResult.Failed(
                    "クリップに muscles がありません。Humanoid クリップを指定してください");

            var muscleByName = new Dictionary<string, UnityMuscleTrackDTO>();
            foreach (var m in clip.muscles)
                if (m != null && !string.IsNullOrEmpty(m.name)) muscleByName[m.name] = m;

            float fps = settings.Fps > 0f ? settings.Fps
                      : (clip.frameRate > 0f ? clip.frameRate : 30f);

            float clipEnd = UnityClipVrmAnimationSource.ComputeMaxTime(clip);
            float start   = Mathf.Max(0f, settings.StartSec);
            float end     = settings.EndSec > start ? settings.EndSec : clipEnd;
            if (end < start) end = start;

            int frameCount = Mathf.Max(1, Mathf.RoundToInt((end - start) * fps) + 1);

            var times = new float[frameCount];
            for (int i = 0; i < frameCount; i++)
                times[i] = start + i / fps;

            UnityClipCanonVrmAnimation src = null;
            try
            {
                src = Build(boneLength, out string buildReason);
                if (src == null) return VrmAnimationExportResult.Failed(buildReason);

                // Root 系はマッスルと別系統として抜き出す。
                // 既存 JSON は Root も muscles に入っているので、同じ辞書から引く。
                var root = UnityClipRootMotion.From(muscleByName);
                src.SetupRoot(root, times[0], useRoot, out string rootNote);

                // 何をどの倍率で載せたかを残す。載らなかったときの原因もここに出る。
                Debug.Log($"[UnityClipCanonVrmAnimation] Root: {rootNote}"
                        + $" / 基準 {root.Describe(times[0])}");

                var localSrc = src;
                var result = PLVrmAnimationBridge.I.Export(
                    src.Root,
                    src.HumanBones,
                    src.Root.transform,
                    times,
                    i => localSrc.Pose(muscleByName, times[i]),
                    outputPath);

                if (result != null && result.Success)
                {
                    result.HumanoidBoneCount = src.HumanBones.Count;
                    result.FrameCount        = frameCount;
                    result.DurationSec       = end - start;
                    result.OutputPath        = outputPath;
                }
                return result ?? VrmAnimationExportResult.Failed("書き出し結果がありません");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityClipCanonVrmAnimation] {ex}");
                return VrmAnimationExportResult.Failed(ex.Message);
            }
            finally
            {
                if (src != null) src.Dispose();
            }
        }
    }
}
