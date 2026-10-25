// Editor/UnityClip/PolyLingMotionToUnityClip.cs
// ============================================================
// PolyLing モーション（.plmotion.json）/ VRM アニメーション（.vrma）→ Unity クリップ（.anim）
// ------------------------------------------------------------
// UnityClipExportWindow（.anim → .plmotion.json）の逆向き。
//
// ■ 入力
//   ・.plmotion.json か .vrma のファイル 1 本、またはフォルダ（サブフォルダを含む全ファイル）。
//   ・読込は MotionFileLoad.Load（.vrma は VrmaCodec、他は MotionClipSerializer。版の引き上げもここ）。
//
// ■ 出力
//   ・元ファイルと同じ場所の <名前>.anim（名前は ".plmotion.json" / ".vrma" を除いたもの）。
//   ・既に .anim があれば、そのアセットのカーブを消して書き直す（GUID はそのまま）。
//
// ■ 変換
//   muscles        → Animator カーブ。指はクリップ内の表記（"Left Thumb 1 Stretched" → "LeftHand.Thumb.1 Stretched"）。
//   body           → RootT.x/y/z・RootQ.x/y/z/w。
//   トラックの無い dof → トラックを 1 本以上持つボーンの、トラックの無い dof に定数カーブを書く。
//                    absentMuscles が "zero"（既定）なら 0、"rest" なら CanonBoneMotion.RestMuscles
//                    （再生側 CanonBoneMotion のマッスル標本化と同じ規則）。
//   bones "path"   → Transform カーブ（m_LocalPosition / m_LocalRotation / m_LocalScale）。
//   bones "boneName" → 書かない（報告）。
//   expressions    → path のある blendShape トラック（provider "blendShape"）は SkinnedMeshRenderer の
//                    blendShape.<名前> カーブ（値と接線は ×100。UnityClipExportWindow の 1/100 の逆）。
//                    path の無いトラック・blendShape でないトラックは書かない（報告）。
//   bakedBones     → 書かない（.vrma は読込でマッスル＋body が添えられる。VrmaCodec.Read）。
//   接線           → MotionTangentDTO を Keyframe と左右の接線モードへ。tan が null のキーは Linear。
//   ラップ・ループ・frameRate を写す。
//
// ■ 座標系
//   Unity 左手系のまま。変換はしない。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Motion;

namespace Poly_Ling.UnityClip.Editor
{
    public static class PolyLingMotionToUnityClip
    {
        public const string MotionSuffix = ".plmotion.json";
        public const string VrmaSuffix   = ".vrma";

        public sealed class FileResult
        {
            public string SourcePath;
            public string OutputPath;   // 失敗時は null
            public bool   Success;
            public readonly List<string> Notes = new List<string>();
        }

        // ------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------

        /// <summary>対象になるファイルか（.plmotion.json / .vrma）。</summary>
        public static bool IsSourceFile(string path)
            => !string.IsNullOrEmpty(path)
               && (path.EndsWith(MotionSuffix, StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(VrmaSuffix, StringComparison.OrdinalIgnoreCase));

        /// <summary>フォルダ（Assets 相対）以下の全ファイルを変換する。</summary>
        public static List<FileResult> ConvertFolder(string assetFolder)
        {
            var results = new List<FileResult>();
            if (!Directory.Exists(assetFolder))
            {
                var r = new FileResult { SourcePath = assetFolder };
                r.Notes.Add("フォルダがありません");
                results.Add(r);
                return results;
            }
            var files = Directory.GetFiles(assetFolder, "*", SearchOption.AllDirectories)
                                 .Where(IsSourceFile)
                                 .Select(p => p.Replace('\\', '/'))
                                 .OrderBy(p => p, StringComparer.Ordinal)
                                 .ToList();
            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("PolyLing モーション → Unity クリップ",
                            Path.GetFileName(files[i]), (float)i / Mathf.Max(1, files.Count)))
                        break;
                    results.Add(ConvertFile(files[i]));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.SaveAssets();
            return results;
        }

        /// <summary>1 本変換する。assetPath は Assets 相対。</summary>
        public static FileResult ConvertFile(string assetPath)
        {
            var res = new FileResult { SourcePath = assetPath };
            try
            {
                bool isVrma = assetPath.EndsWith(VrmaSuffix, StringComparison.OrdinalIgnoreCase);
                var load = MotionFileLoad.Load(Path.GetFullPath(assetPath),
                                               isVrma ? MotionSourceKind.Vrma : MotionSourceKind.PolyLingMotionJson);
                if (load.Dto == null)
                {
                    res.Notes.Add("読めません: " + load.FormatIssues(5));
                    return res;
                }
                if (load.WarningCount > 0) res.Notes.Add("読込の警告: " + load.FormatIssues(5));

                string stem = Path.GetFileName(assetPath);
                stem = stem.Substring(0, stem.Length - (isVrma ? VrmaSuffix.Length : MotionSuffix.Length));
                string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
                string outPath = dir + "/" + stem + ".anim";

                var dto = load.Dto;
                var bindings = new List<EditorCurveBinding>();
                var curves   = new List<AnimationCurve>();
                if (!BuildCurves(dto, bindings, curves, res.Notes)) return res;

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
                bool created = clip == null;
                if (created) clip = new AnimationClip();
                else clip.ClearCurves();

                clip.name      = stem;
                clip.frameRate = dto.frameRate > 0f ? dto.frameRate : 30f;
                AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
                clip.EnsureQuaternionContinuity();

                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = dto.loop;
                float len = MotionClipSerializer.Length(dto);
                if (len > 0f) { settings.startTime = 0f; settings.stopTime = len; }
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                if (created) AssetDatabase.CreateAsset(clip, outPath);
                else
                {
                    EditorUtility.SetDirty(clip);
                    AssetDatabase.SaveAssetIfDirty(clip);
                }

                res.OutputPath = outPath;
                res.Success = true;
                res.Notes.Insert(0, $"カーブ {curves.Count} 本（{(created ? "新規" : "上書き")}）");
            }
            catch (Exception ex)
            {
                res.Notes.Add("例外: " + ex.Message);
            }
            return res;
        }

        /// <summary>結果を 1 つの文字列にまとめる。</summary>
        public static string FormatResults(List<FileResult> results)
        {
            var sb = new StringBuilder();
            int ok = results.Count(r => r.Success);
            sb.Append($"成功 {ok} / {results.Count}");
            foreach (var r in results)
            {
                sb.Append('\n').Append(r.Success ? "○ " : "× ").Append(r.SourcePath);
                foreach (var n in r.Notes) sb.Append("\n    ").Append(n);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------
        // カーブ作成
        // ------------------------------------------------------------

        private static readonly string[] PosProps = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
        private static readonly string[] RotProps = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        private static readonly string[] SclProps = { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
        private static readonly string[] RootTProps = { UnityClipRootMotion.NameTx, UnityClipRootMotion.NameTy, UnityClipRootMotion.NameTz };
        private static readonly string[] RootQProps = { UnityClipRootMotion.NameQx, UnityClipRootMotion.NameQy, UnityClipRootMotion.NameQz, UnityClipRootMotion.NameQw };

        private static bool BuildCurves(MotionClipDTO dto, List<EditorCurveBinding> bindings, List<AnimationCurve> curves,
                                        List<string> notes)
        {
            float len = MotionClipSerializer.Length(dto);

            // ---- マッスル ----
            var traitToClip = BuildTraitToClipNameMap();
            var names = HumanTrait.MuscleName;
            int mc = HumanTrait.MuscleCount;
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < mc; i++) index[names[i]] = i;

            var has = new bool[mc];
            var unknown = new List<string>();
            if (dto.muscles != null)
                foreach (var tr in dto.muscles)
                {
                    if (tr?.keys == null || tr.keys.Count == 0 || tr.name == null) continue;
                    if (!index.TryGetValue(tr.name, out int mi)) { unknown.Add(tr.name); continue; }
                    var c = ScalarCurve(tr.keys, tr.preWrapMode, tr.postWrapMode);
                    AddAnimator(bindings, curves, ClipMuscleName(traitToClip, tr.name), c);
                    has[mi] = true;
                }
            if (unknown.Count > 0) notes.Add("マッスル名でないため書かなかった: " + string.Join(", ", unknown));

            // トラックの無い dof（駆動されているボーンのもの）
            int boneCount = (int)HumanBodyBones.LastBone;
            var driven = new bool[boneCount];
            for (int b = 0; b < boneCount; b++)
                for (int dof = 0; dof < 3; dof++)
                {
                    int mi = HumanTrait.MuscleFromBone(b, dof);
                    if (mi >= 0 && mi < mc && has[mi]) driven[b] = true;
                }
            bool restMode = MotionClipDTO.AbsentIsRest(dto);
            float[] rest = null;
            if (restMode)
            {
                using (var cbm = new CanonBoneMotion())
                {
                    if (!cbm.Build(out string reason)) { notes.Add("正準骨格を作れません: " + reason); return false; }
                    rest = (float[])cbm.RestMuscles.Clone();
                }
            }
            int filled = 0;
            float end = len > 0f ? len : 0f;
            for (int mi = 0; mi < mc; mi++)
            {
                if (has[mi]) continue;
                int b = HumanTrait.BoneFromMuscle(mi);
                if (b < 0 || b >= boneCount || !driven[b]) continue;
                float v = restMode && rest != null && mi < rest.Length ? rest[mi] : 0f;
                AddAnimator(bindings, curves, ClipMuscleName(traitToClip, names[mi]), AnimationCurve.Constant(0f, end, v));
                filled++;
            }
            if (filled > 0) notes.Add($"トラックの無い dof {filled} 本を {(restMode ? "rest" : "0")} の定数で補った");

            // ---- body ----
            if (dto.body?.keys != null && dto.body.keys.Count > 0)
            {
                AddVectorCurves(dto.body, 0, RootTProps, (p, c) => AddAnimator(bindings, curves, p, c));
                AddVectorCurves(dto.body, 1, RootQProps, (p, c) => AddAnimator(bindings, curves, p, c));
            }

            // ---- bones ----
            int skippedBoneName = 0;
            var otherKinds = new SortedSet<string>(StringComparer.Ordinal);
            if (dto.bones != null)
                foreach (var tr in dto.bones)
                {
                    if (tr?.keys == null || tr.keys.Count == 0) continue;
                    if (tr.targetKind == MotionClipDTO.TargetBoneName) { skippedBoneName++; continue; }
                    if (tr.targetKind != MotionClipDTO.TargetPath) { otherKinds.Add(tr.targetKind ?? "(null)"); continue; }
                    string path = tr.id ?? "";
                    void Add(string prop, AnimationCurve c)
                    {
                        bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), prop));
                        curves.Add(c);
                    }
                    AddVectorCurves(tr, 0, PosProps, Add);
                    AddVectorCurves(tr, 1, RotProps, Add);
                    AddVectorCurves(tr, 2, SclProps, Add);
                }
            if (skippedBoneName > 0) notes.Add($"骨名（boneName）トラック {skippedBoneName} 本は書かなかった");
            if (otherKinds.Count > 0) notes.Add("targetKind が path でないボーントラックは書かなかった: " + string.Join(", ", otherKinds));

            // ---- 表情（blendShape）----
            int exprWritten = 0, noPath = 0, notBlend = 0;
            if (dto.expressions != null)
                foreach (var tr in dto.expressions)
                {
                    if (tr?.keys == null || tr.keys.Count == 0 || string.IsNullOrEmpty(tr.name)) continue;
                    if (tr.provider != "blendShape") { notBlend++; continue; }
                    if (string.IsNullOrEmpty(tr.path)) { noPath++; continue; }
                    var sk = new List<MotionScalarKeyDTO>(tr.keys.Count);
                    foreach (var k in tr.keys)
                        if (k != null) sk.Add(new MotionScalarKeyDTO { t = k.t, v = k.v * 100f, tan = ScaleTangent(k.tan, 100f) });
                    bindings.Add(EditorCurveBinding.FloatCurve(tr.path, typeof(SkinnedMeshRenderer), "blendShape." + tr.name));
                    curves.Add(ScalarCurve(sk, tr.preWrapMode, tr.postWrapMode));
                    exprWritten++;
                }
            if (exprWritten > 0) notes.Add($"表情 {exprWritten} 本を blendShape カーブにした");
            if (noPath > 0)   notes.Add($"表情 {noPath} 本は書かなかった（blendShape のレンダラーのパスがファイルに無い）");
            if (notBlend > 0) notes.Add($"表情 {notBlend} 本は書かなかった（provider が blendShape でない）");
            if (dto.lookAt?.keys != null && dto.lookAt.keys.Count > 0)
                notes.Add("lookAt トラックは書かなかった（目のマッスルで表す）");

            if (curves.Count == 0) { notes.Add("書けるカーブがありません"); return false; }
            return true;
        }

        private static void AddAnimator(List<EditorCurveBinding> bindings, List<AnimationCurve> curves, string prop, AnimationCurve c)
        {
            bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), prop));
            curves.Add(c);
        }

        // ch: 0 = pos、1 = rot、2 = scl。そのチャンネルを持つキーだけで成分ごとのカーブを作る。
        private static void AddVectorCurves(MotionTrackDTO tr, int ch, string[] props, Action<string, AnimationCurve> add)
        {
            int n = props.Length;
            var keys = new List<MotionKeyDTO>();
            foreach (var k in tr.keys)
            {
                if (k == null) continue;
                float[] v = ch == 0 ? k.pos : ch == 1 ? k.rot : k.scl;
                if (v != null && v.Length == n) keys.Add(k);
            }
            if (keys.Count == 0) return;
            for (int c = 0; c < n; c++)
            {
                var sk = new List<MotionScalarKeyDTO>(keys.Count);
                foreach (var k in keys)
                {
                    float[] v = ch == 0 ? k.pos : ch == 1 ? k.rot : k.scl;
                    var tans = ch == 0 ? k.posTan : ch == 1 ? k.rotTan : k.sclTan;
                    sk.Add(new MotionScalarKeyDTO
                    {
                        t = k.t, v = v[c],
                        tan = tans != null && c < tans.Length ? tans[c] : null
                    });
                }
                add(props[c], ScalarCurve(sk, tr.preWrapMode, tr.postWrapMode));
            }
        }

        // キー列 → AnimationCurve（接線・接線モード・重み・ラップ）。
        private static AnimationCurve ScalarCurve(List<MotionScalarKeyDTO> src, string pre, string post)
        {
            var ks = src.Where(k => k != null).OrderBy(k => k.t).ToList();
            var frames = new Keyframe[ks.Count];
            for (int i = 0; i < ks.Count; i++)
            {
                var k = ks[i];
                var f = new Keyframe(k.t, k.v);
                var tn = k.tan;
                if (tn != null)
                {
                    if (tn.inTangent.HasValue)  f.inTangent  = tn.inTangent.Value;
                    if (tn.outTangent.HasValue) f.outTangent = tn.outTangent.Value;
                    f.weightedMode = ParseWeighted(tn.weightedMode);
                    if (tn.inWeight.HasValue)  f.inWeight  = tn.inWeight.Value;
                    if (tn.outWeight.HasValue) f.outWeight = tn.outWeight.Value;
                }
                frames[i] = f;
            }
            var curve = new AnimationCurve(frames)
            {
                preWrapMode  = ParseWrap(pre),
                postWrapMode = ParseWrap(post)
            };
            for (int i = 0; i < ks.Count; i++)
            {
                var tn = ks[i].tan;
                var left  = tn == null ? AnimationUtility.TangentMode.Linear : ParseMode(tn.leftTangentMode);
                var right = tn == null ? AnimationUtility.TangentMode.Linear : ParseMode(tn.rightTangentMode);
                // Free はキーの接線値をそのまま使う。
                if (left  != AnimationUtility.TangentMode.Free) AnimationUtility.SetKeyLeftTangentMode(curve, i, left);
                if (right != AnimationUtility.TangentMode.Free) AnimationUtility.SetKeyRightTangentMode(curve, i, right);
            }
            return curve;
        }

        // 傾きだけを倍する（重み・モードはそのまま）。
        private static MotionTangentDTO ScaleTangent(MotionTangentDTO t, float scale)
        {
            if (t == null) return null;
            return new MotionTangentDTO
            {
                inTangent        = t.inTangent  * scale,
                outTangent       = t.outTangent * scale,
                inWeight         = t.inWeight,
                outWeight        = t.outWeight,
                weightedMode     = t.weightedMode,
                leftTangentMode  = t.leftTangentMode,
                rightTangentMode = t.rightTangentMode,
            };
        }

        private static AnimationUtility.TangentMode ParseMode(string s)
        {
            switch (s)
            {
                case MotionCurveMath.ModeAuto:        return AnimationUtility.TangentMode.Auto;
                case MotionCurveMath.ModeClampedAuto: return AnimationUtility.TangentMode.ClampedAuto;
                case MotionCurveMath.ModeLinear:      return AnimationUtility.TangentMode.Linear;
                case MotionCurveMath.ModeConstant:    return AnimationUtility.TangentMode.Constant;
                default:                              return AnimationUtility.TangentMode.Free;
            }
        }

        private static WeightedMode ParseWeighted(string s)
        {
            switch (s)
            {
                case MotionCurveMath.WeightIn:   return WeightedMode.In;
                case MotionCurveMath.WeightOut:  return WeightedMode.Out;
                case MotionCurveMath.WeightBoth: return WeightedMode.Both;
                default:                         return WeightedMode.None;
            }
        }

        private static WrapMode ParseWrap(string s)
        {
            switch (s)
            {
                case MotionCurveMath.WrapLoop:     return WrapMode.Loop;
                case MotionCurveMath.WrapPingPong: return WrapMode.PingPong;
                default:                           return WrapMode.ClampForever;
            }
        }

        // HumanTrait 名 → クリップ内の指マッスル名（UnityClipExportWindow.BuildClipMuscleNameMap の逆）。
        private static Dictionary<string, string> BuildTraitToClipNameMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            foreach (string trait in HumanTrait.MuscleName)
                foreach (string side in new[] { "Left", "Right" })
                    foreach (string f in fingers)
                    {
                        string head = side + " " + f + " ";
                        if (!trait.StartsWith(head, StringComparison.Ordinal)) continue;
                        map[trait] = side + "Hand." + f + "." + trait.Substring(head.Length);
                    }
            return map;
        }

        private static string ClipMuscleName(Dictionary<string, string> map, string trait)
            => map.TryGetValue(trait, out var c) ? c : trait;
    }

    // ================================================================
    // 窓
    // ================================================================
    public class PolyLingMotionToUnityClipWindow : EditorWindow
    {
        private UnityEngine.Object _input;
        private string _report = "";
        private Vector2 _scroll;

        [MenuItem("PolyLing/UnityClip/PolyLing モーション → Unity クリップ（.anim）")]
        public static void Open()
        {
            var w = GetWindow<PolyLingMotionToUnityClipWindow>(false, "モーション → Unity クリップ", true);
            w.minSize = new Vector2(480, 300);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("PolyLing モーション / VRMA → Unity クリップ", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            _input = EditorGUILayout.ObjectField("ファイルまたはフォルダ", _input, typeof(UnityEngine.Object), false);
            string path = _input != null ? AssetDatabase.GetAssetPath(_input) : null;
            bool isFolder = !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path);
            bool isFile   = !isFolder && PolyLingMotionToUnityClip.IsSourceFile(path);
            if (_input != null && !isFolder && !isFile)
                EditorGUILayout.HelpBox(".plmotion.json か .vrma のファイル、またはフォルダを指定してください。", MessageType.Warning);

            EditorGUILayout.HelpBox(
                "元ファイルと同じ場所に <名前>.anim を書きます（Humanoid のマッスル＋Root、Transform カーブ）。\n" +
                "既にある .anim はカーブを書き直します（GUID はそのまま）。表情はレンダラーのパスがある blendShape だけ書きます。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(!isFolder && !isFile))
            {
                if (GUILayout.Button("変換", GUILayout.Height(30)))
                {
                    var results = isFolder
                        ? PolyLingMotionToUnityClip.ConvertFolder(path)
                        : new List<PolyLingMotionToUnityClip.FileResult> { PolyLingMotionToUnityClip.ConvertFile(path) };
                    if (isFile) AssetDatabase.SaveAssets();
                    _report = PolyLingMotionToUnityClip.FormatResults(results);
                    Debug.Log("[PolyLingMotionToUnityClip] " + _report);
                }
            }

            if (!string.IsNullOrEmpty(_report))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }
    }
}
