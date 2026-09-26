// Editor/UnityClip/UnityClipExportWindow.cs
// ============================================================
// Unity クリップ → PolyLing モーション（.plmotion.json）書き出し
// ------------------------------------------------------------
// モーションのファイル形式は PolyLing モーション 1 種類だけ。この窓がその唯一の入口。
//
// ■ 入力
//   ・クリップ 1 本、またはフォルダ（サブフォルダを含む全クリップ。FBX 等の中のクリップも対象、
//     "__preview__" で始まるものは除く）。
//   ・Avatar (Animator)：任意。シーン上の Humanoid アバター。
//
// ■ 出力
//   ・クリップごとに <クリップ名>.plmotion.json。フォルダ入力ではサブフォルダ構成を再現する。
//     同じ出力先で名前が重なったときは <アセットファイル名>_<クリップ名> にする。
//   ・アバター指定時は <root>_bones.csv（UnityBone v3）と <root>_limits.csv（UnityLimit v1・実測）を 1 回。
//     ベイクしたマッスルを PolyLing で正しく戻すには、元アバターの実測 limits.csv が要る
//     （無いボーンは定数表 CanonMuscleTable で近似される。UnityClipApplier 冒頭参照）。
//
// ■ 変換
//   Transform カーブ   → bones（targetKind "path"）。接線とラップモードを写す。
//                        成分カーブのキー時刻が揃わない／成分が欠けるチャンネルは、時刻の和集合で値を取り
//                        接線なし（線形）。欠けた成分は pos=0 / scl=1 / rot=恒等の成分で埋める。
//   Animator カーブ    → muscles。名前は HumanTrait.MuscleName に合わせる
//                        （クリップ内の指の名前 "LeftHand.Thumb.1 Stretched" → "Left Thumb 1 Stretched"）。
//                        RootT / RootQ はそのまま。IK ゴール（LeftFootT.x 等）とその他は書かない（報告する）。
//   blendShape カーブ  → expressions（provider "blendShape"）。値と接線は 1/100（0〜1 に換算）。
//
// ■ マッスルにベイク（チェックボックス。アバター指定時のみ）
//   マッスルを持たないクリップだけをベイクする。既にマッスルを持つクリップは元のマッスルを使い、報告する。
//   ベイクは毎フレーム SampleAnimation → HumanPoseHandler.GetHumanPose で 95 マッスルと
//   RootT / RootQ（bodyPosition / bodyRotation）を取り、線形キーにする。
//   Humanoid ボーンの Transform トラックは書かない（マッスルで表す）。それ以外のボーンはカーブから写す。
//   終了後、アバター配下の全 Transform の TRS を復元する。
//
// ■ 座標系
//   Unity 左手系のまま。変換はしない。
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Core;
using Poly_Ling.EditorTools;
using Poly_Ling.Motion;
using Poly_Ling.UnityClip;

namespace Poly_Ling.UnityClip.Editor
{
    public class UnityClipExportWindow : EditorWindow
    {
        // ── 入力 ──────────────────────────────────────────────────────────
        private UnityEngine.Object _input;      // AnimationClip またはフォルダ（DefaultAsset）
        private Animator           _animator;
        private string             _outDir = "";

        // ── オプション ────────────────────────────────────────────────────
        private bool _bakeMuscles = true;    // マッスルにベイク（アバター指定時のみ有効。既定オン）
        private bool _skinnedOnly = true;    // bones.csv: Skinned ボーン + Humanoid 骨のみ
        private bool _writeHeader = true;    // CSV のヘッダ行
        private bool _utf8Bom     = true;    // CSV の UTF-8 BOM

        [MenuItem("PolyLing/UnityClip/Unity クリップ書き出し（PolyLing モーション）")]
        public static void Open()
        {
            var w = GetWindow<UnityClipExportWindow>(false, "Unity クリップ書き出し", true);
            w.minSize = new Vector2(480, 330);
            w.Show();
        }

        private void OnEnable()
        {
            _outDir = EditorSaveDestField.Load(SaveDest.Keys.Motion);
        }

        private bool AvatarIsHuman => _animator != null && _animator.avatar != null && _animator.avatar.isHuman;

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Unity クリップ → PolyLing モーション", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _input = EditorGUILayout.ObjectField("クリップまたはフォルダ", _input, typeof(UnityEngine.Object), false);
            string inputError = InputError(_input);
            if (inputError != null) EditorGUILayout.HelpBox(inputError, MessageType.Warning);

            _animator = (Animator)EditorGUILayout.ObjectField("Avatar (Animator)", _animator, typeof(Animator), true);
            if (_animator != null && !AvatarIsHuman)
                EditorGUILayout.HelpBox("Humanoid アバターではありません。ベイクと limits.csv は使えません。", MessageType.Warning);

            EditorGUILayout.Space();
            _outDir = EditorSaveDestField.Draw(
                "書き込み先フォルダ", _outDir, SaveDest.Keys.Motion,
                "PolyLing モーションの書き込み先", "motion.plmotion.json", "json");

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!AvatarIsHuman))
            {
                // アバター未指定のあいだは表示だけ外し、設定値（既定オン）は保つ。
                bool shown = EditorGUILayout.ToggleLeft(
                    "マッスルにベイク（マッスルを持たないクリップだけ）", _bakeMuscles && AvatarIsHuman);
                if (AvatarIsHuman) _bakeMuscles = shown;
            }
            _skinnedOnly = EditorGUILayout.ToggleLeft("bones.csv は Skinned ボーン + Humanoid 骨のみ（推奨）", _skinnedOnly);
            _writeHeader = EditorGUILayout.ToggleLeft("CSV にヘッダ行を付ける", _writeHeader);
            _utf8Bom     = EditorGUILayout.ToggleLeft("CSV に UTF-8 BOM を付ける", _utf8Bom);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "クリップ（またはフォルダ内の全クリップ）を .plmotion.json で書き出します。\n" +
                "アバターを指定すると <root>_bones.csv と <root>_limits.csv（実測）も書き出します。\n" +
                "bones.csv のレストはシーン上の現在姿勢です。実行前にレスト姿勢にしてください。\n" +
                "ベイクと実測はポーズを一時変更しますが、完了後に TRS を復元します。",
                MessageType.Info);

            bool ready = (_input != null && inputError == null || _animator != null) && !string.IsNullOrEmpty(_outDir);
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("書き出し", GUILayout.Height(30))) ExportAll();
            }
        }

        private static string InputError(UnityEngine.Object o)
        {
            if (o == null || o is AnimationClip) return null;
            string p = AssetDatabase.GetAssetPath(o);
            if (!string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p)) return null;
            return "AnimationClip かフォルダを指定してください。";
        }

        // ================================================================
        // 書き出し本体
        // ================================================================
        private void ExportAll()
        {
            try { Directory.CreateDirectory(_outDir); }
            catch (Exception ex) { EditorUtility.DisplayDialog("エラー", "書き込み先フォルダを作れません:\n" + ex.Message, "OK"); return; }

            var log = new StringBuilder();
            int ok = 0, fail = 0, baked = 0;
            var notBaked = new List<string>();

            // ── アバター：骨 CSV・リミット CSV ────────────────────────────
            if (_animator != null)
            {
                Transform rootT = _animator.transform;
                string baseName = SanitizeFileName(rootT.name);
                var humanMap = BuildHumanoidMap(_animator);
                var boneSet  = CollectBones(rootT, humanMap);
                var ordered  = new List<Transform>();
                DfsOrder(rootT, boneSet, ordered);
                string boneCsv = BuildBoneCsv(rootT, ordered, boneSet, humanMap, out int humCount);
                string bonePath = Path.Combine(_outDir, baseName + "_bones.csv");
                File.WriteAllText(bonePath, boneCsv, new UTF8Encoding(_utf8Bom));
                log.AppendLine($"{Path.GetFileName(bonePath)}  ({ordered.Count} bones / Humanoid {humCount})");

                if (AvatarIsHuman)
                {
                    string limitCsv = BuildLimitCsv(_animator, out int rows, out bool measured);
                    string limitPath = Path.Combine(_outDir, baseName + "_limits.csv");
                    File.WriteAllText(limitPath, limitCsv, new UTF8Encoding(_utf8Bom));
                    log.AppendLine($"{Path.GetFileName(limitPath)}  ({rows} bones / 実測 {(measured ? "あり" : "失敗")})");
                }
            }

            // ── クリップ ──────────────────────────────────────────────────
            var items = CollectClips(_input, out string baseDir);
            var used  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var (clip, assetPath) = items[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Unity クリップ書き出し",
                            $"{clip.name}  ({i + 1}/{items.Count})", (float)i / Mathf.Max(1, items.Count)))
                    {
                        log.AppendLine("中断しました。");
                        break;
                    }

                    string outPath = MakeOutPath(baseDir, assetPath, clip.name, used);
                    try
                    {
                        var notes = new List<string>();
                        var dto = ConvertClip(clip, _bakeMuscles && AvatarIsHuman ? _animator : null, notes, out bool didBake, out bool hadMuscles);
                        if (didBake) baked++;
                        if (_bakeMuscles && AvatarIsHuman && hadMuscles) notBaked.Add(clip.name);

                        var r = MotionClipSerializer.Save(dto, outPath);
                        if (r.Dto == null)
                        {
                            fail++;
                            log.AppendLine($"[失敗] {clip.name}: {r.FormatIssues(10)}");
                            continue;
                        }
                        ok++;
                        log.AppendLine($"{Path.GetFileName(outPath)}  (bones {dto.bones.Count} / muscles {dto.muscles.Count} / 表情 {dto.expressions.Count}{(didBake ? " / ベイク" : "")})");
                        foreach (var n in notes) log.AppendLine("  " + n);
                        if (r.WarningCount > 0) log.AppendLine("  " + r.FormatIssues(10).Replace("\n", "\n  "));
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        log.AppendLine($"[失敗] {clip.name}: {ex.Message}");
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            var sb = new StringBuilder();
            sb.AppendLine($"クリップ: 成功 {ok} / 失敗 {fail}（全 {items.Count}）  ベイク {baked}");
            if (notBaked.Count > 0)
                sb.AppendLine($"マッスルを既に持つためベイクしなかったクリップ: {string.Join(", ", notBaked)}");
            sb.AppendLine(_outDir);
            Debug.Log("[UnityClipExportWindow] " + sb + "\n" + log);
            EditorUtility.DisplayDialog("Unity クリップ書き出し", sb + "\n詳細はコンソールを参照してください。", "OK");
        }

        // 入力 → (クリップ, アセットパス) の一覧。baseDir はサブフォルダ再現の基準（フォルダ入力のときだけ）。
        private static List<(AnimationClip clip, string assetPath)> CollectClips(UnityEngine.Object input, out string baseDir)
        {
            baseDir = null;
            var items = new List<(AnimationClip, string)>();
            if (input == null) return items;
            if (input is AnimationClip single)
            {
                items.Add((single, AssetDatabase.GetAssetPath(single)));
                return items;
            }
            string folder = AssetDatabase.GetAssetPath(input);
            if (!AssetDatabase.IsValidFolder(folder)) return items;
            baseDir = folder.TrimEnd('/');
            var paths = AssetDatabase.FindAssets("", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !AssetDatabase.IsValidFolder(p))
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal);
            foreach (string p in paths)
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                    if (o is AnimationClip c && !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                        items.Add((c, p));
            return items;
        }

        private string MakeOutPath(string baseDir, string assetPath, string clipName, HashSet<string> used)
        {
            string dir = _outDir;
            if (baseDir != null)
            {
                string rel = (Path.GetDirectoryName(assetPath) ?? "").Replace('\\', '/');
                rel = rel.Length > baseDir.Length ? rel.Substring(baseDir.Length).TrimStart('/') : "";
                if (rel.Length > 0) dir = Path.Combine(_outDir, rel);
            }
            Directory.CreateDirectory(dir);

            string stem = SanitizeFileName(clipName);
            string path = Path.Combine(dir, stem + ".plmotion.json");
            if (used.Add(Path.GetFullPath(path))) return path;
            string alt = SanitizeFileName(Path.GetFileNameWithoutExtension(assetPath) + "_" + clipName);
            path = Path.Combine(dir, alt + ".plmotion.json");
            for (int n = 2; !used.Add(Path.GetFullPath(path)); n++)
                path = Path.Combine(dir, alt + "_" + n + ".plmotion.json");
            return path;
        }

        // ================================================================
        // AnimationClip → MotionClipDTO
        // ================================================================

        private static readonly string[] ChannelNames = { "pos", "rot", "scl" };
        private static readonly string[] IkGoalPrefixes = { "LeftFootT.", "LeftFootQ.", "RightFootT.", "RightFootQ.", "LeftHandT.", "LeftHandQ.", "RightHandT.", "RightHandQ." };

        /// <param name="bakeAvatar">ベイクに使うアバター。null ならベイクしない。</param>
        private static MotionClipDTO ConvertClip(AnimationClip clip, Animator bakeAvatar, List<string> notes,
                                                 out bool didBake, out bool hadMuscles)
        {
            didBake = false;
            var dto = new MotionClipDTO
            {
                name      = clip.name,
                frameRate = clip.frameRate > 0f ? clip.frameRate : 30f,
                duration  = clip.length,
                space     = "local",
                loop      = clip.isLooping,
                metadata  = new MotionMetadataDTO { createdWith = "PolyLing UnityClipExportWindow" }
            };

            var muscleNames = new HashSet<string>(HumanTrait.MuscleName, StringComparer.Ordinal);
            var clipToTrait = BuildClipMuscleNameMap();
            var pathCurves  = new SortedDictionary<string, AnimationCurve[][]>(StringComparer.Ordinal);
            var skipped     = new SortedSet<string>(StringComparer.Ordinal);
            var exprNames   = new HashSet<string>(StringComparer.Ordinal);

            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type == typeof(Animator))
                {
                    string name = clipToTrait.TryGetValue(b.propertyName, out var trait) ? trait : b.propertyName;
                    if (muscleNames.Contains(name) || IsRootName(name))
                    {
                        var tr = BuildScalarTrack(name, AnimationUtility.GetEditorCurve(clip, b), 1f);
                        if (tr != null) dto.muscles.Add(tr);
                    }
                    else skipped.Add(IkGoalPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))
                                     ? "IK ゴール " + name : "Animator " + name);
                    continue;
                }
                if (b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                {
                    string ename = b.propertyName.Substring("blendShape.".Length);
                    if (!exprNames.Add(ename)) { notes.Add($"表情 {ename}: 同名が複数のレンダラーにあり、先のものを使いました（{b.path}）"); continue; }
                    var st = BuildScalarTrack(ename, AnimationUtility.GetEditorCurve(clip, b), 0.01f);
                    if (st != null)
                        dto.expressions.Add(new MotionExpressionTrackDTO
                        {
                            name = st.name, provider = "blendShape", keys = st.keys,
                            preWrapMode = st.preWrapMode, postWrapMode = st.postWrapMode
                        });
                    continue;
                }
                if (b.type != typeof(Transform)) { skipped.Add($"{b.type.Name} {b.propertyName}"); continue; }

                int ch = ClassifyTransformProperty(b.propertyName, out int comp);
                if (ch < 0) { skipped.Add("Transform " + b.propertyName); continue; }
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null || curve.length == 0) continue;
                if (!pathCurves.TryGetValue(b.path, out var pc))
                {
                    pc = new[] { new AnimationCurve[3], new AnimationCurve[4], new AnimationCurve[3] };
                    pathCurves[b.path] = pc;
                }
                pc[ch][comp] = curve;
            }

            hadMuscles = dto.muscles.Any(m => muscleNames.Contains(m.name));

            // ベイク：マッスルを持たないクリップだけ
            HashSet<string> humanPaths = null;
            if (bakeAvatar != null && !hadMuscles)
            {
                BakeMuscles(clip, bakeAvatar, dto);
                didBake = true;
                humanPaths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kv in BuildHumanoidMap(bakeAvatar))
                    humanPaths.Add(AnimationUtility.CalculateTransformPath(kv.Key, bakeAvatar.transform));
            }

            foreach (var kv in pathCurves)
            {
                if (humanPaths != null && humanPaths.Contains(kv.Key)) continue;   // マッスルで表す
                var track = BuildBoneTrack(kv.Key, kv.Value, notes);
                if (track != null) dto.bones.Add(track);
            }

            if (skipped.Count > 0) notes.Add("書かなかったカーブ: " + string.Join(", ", skipped));
            return dto;
        }

        private static bool IsRootName(string n)
            => n == UnityClipRootMotion.NameTx || n == UnityClipRootMotion.NameTy || n == UnityClipRootMotion.NameTz
            || n == UnityClipRootMotion.NameQx || n == UnityClipRootMotion.NameQy
            || n == UnityClipRootMotion.NameQz || n == UnityClipRootMotion.NameQw;

        // クリップ内の指マッスル名（"LeftHand.Thumb.1 Stretched" / "LeftHand.Thumb.Spread"）→ HumanTrait 名。
        private static Dictionary<string, string> BuildClipMuscleNameMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            foreach (string trait in HumanTrait.MuscleName)
            {
                foreach (string side in new[] { "Left", "Right" })
                {
                    foreach (string f in fingers)
                    {
                        string head = side + " " + f + " ";
                        if (!trait.StartsWith(head, StringComparison.Ordinal)) continue;
                        string rest = trait.Substring(head.Length);          // "1 Stretched" / "Spread"
                        string clipName = side + "Hand." + f + "." + rest;   // "LeftHand.Thumb.1 Stretched"
                        map[clipName] = trait;
                    }
                }
            }
            return map;
        }

        // ------------------------------------------------------------
        // ベイク
        // ------------------------------------------------------------
        private static void BakeMuscles(AnimationClip clip, Animator animator, MotionClipDTO dto)
        {
            // Animator 由来（RootT/RootQ 等）は作り直すので捨てる
            dto.muscles.Clear();

            Transform rootT = animator.transform;
            var all = rootT.GetComponentsInChildren<Transform>(true);
            var sp = new Vector3[all.Length]; var sr = new Quaternion[all.Length]; var ss = new Vector3[all.Length];
            for (int i = 0; i < all.Length; i++) { sp[i] = all[i].localPosition; sr[i] = all[i].localRotation; ss[i] = all[i].localScale; }

            float fps = clip.frameRate > 0f ? clip.frameRate : 30f;
            int frames = Mathf.Max(1, Mathf.CeilToInt(clip.length * fps - 1e-4f) + 1);
            var names = HumanTrait.MuscleName;
            int mc = HumanTrait.MuscleCount;
            var muscleTracks = new MotionScalarTrackDTO[mc];
            for (int m = 0; m < mc; m++) muscleTracks[m] = new MotionScalarTrackDTO { name = names[m] };
            string[] rootNames =
            {
                UnityClipRootMotion.NameTx, UnityClipRootMotion.NameTy, UnityClipRootMotion.NameTz,
                UnityClipRootMotion.NameQx, UnityClipRootMotion.NameQy, UnityClipRootMotion.NameQz, UnityClipRootMotion.NameQw
            };
            var rootTracks = rootNames.Select(n => new MotionScalarTrackDTO { name = n }).ToArray();

            HumanPoseHandler handler = null;
            try
            {
                handler = new HumanPoseHandler(animator.avatar, rootT);
                var pose = new HumanPose();
                Quaternion prevQ = Quaternion.identity; bool hasPrev = false;
                for (int f = 0; f < frames; f++)
                {
                    float t = Mathf.Min(f / fps, clip.length);
                    clip.SampleAnimation(animator.gameObject, t);
                    handler.GetHumanPose(ref pose);
                    for (int m = 0; m < mc && m < pose.muscles.Length; m++)
                        muscleTracks[m].keys.Add(new MotionScalarKeyDTO { t = t, v = pose.muscles[m] });

                    Quaternion q = pose.bodyRotation;
                    if (hasPrev && Quaternion.Dot(prevQ, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    prevQ = q; hasPrev = true;
                    float[] rv = { pose.bodyPosition.x, pose.bodyPosition.y, pose.bodyPosition.z, q.x, q.y, q.z, q.w };
                    for (int k = 0; k < 7; k++) rootTracks[k].keys.Add(new MotionScalarKeyDTO { t = t, v = rv[k] });
                }
            }
            finally
            {
                if (handler != null) handler.Dispose();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    all[i].localPosition = sp[i]; all[i].localRotation = sr[i]; all[i].localScale = ss[i];
                }
            }

            dto.muscles.AddRange(muscleTracks);
            dto.muscles.AddRange(rootTracks);
        }

        // ------------------------------------------------------------
        // ボーントラック（接線つき）
        // ------------------------------------------------------------
        private static MotionTrackDTO BuildBoneTrack(string path, AnimationCurve[][] pc, List<string> notes)
        {
            var chKeys = new Dictionary<float, (float[] v, MotionTangentDTO[] tan)>[3];
            AnimationCurve wrapSrc = null;

            for (int ch = 0; ch < 3; ch++)
            {
                var comps = pc[ch];
                int present = 0;
                foreach (var c in comps) if (c != null) { present++; if (wrapSrc == null) wrapSrc = c; }
                if (present == 0) continue;

                bool complete  = present == comps.Length;
                bool sameTimes = complete && SameKeyTimes(comps);
                var dict = new Dictionary<float, (float[] v, MotionTangentDTO[] tan)>();

                if (sameTimes)
                {
                    for (int i = 0; i < comps[0].length; i++)
                    {
                        var v = new float[comps.Length];
                        var tan = new MotionTangentDTO[comps.Length];
                        for (int c = 0; c < comps.Length; c++) { v[c] = comps[c][i].value; tan[c] = ToTangent(comps[c], i, 1f); }
                        dict[comps[0][i].time] = (v, tan);
                    }
                }
                else
                {
                    var times = new SortedSet<float>();
                    foreach (var c in comps) if (c != null) foreach (var k in c.keys) times.Add(k.time);
                    foreach (float t in times)
                    {
                        var v = new float[comps.Length];
                        for (int c = 0; c < comps.Length; c++)
                            v[c] = comps[c] != null ? comps[c].Evaluate(t) : DefaultComponent(ch, c);
                        dict[t] = (v, null);
                    }
                    string label = $"{(path.Length == 0 ? "(root)" : path)} {ChannelNames[ch]}";
                    notes.Add(complete
                        ? $"{label}: 成分のキー時刻が揃わないため接線なし（線形）"
                        : $"{label}: 成分カーブが {present}/{comps.Length} 本のため欠けた成分を既定値で埋め、接線なし（線形）");
                }
                chKeys[ch] = dict;
            }
            if (wrapSrc == null) return null;

            var allTimes = new SortedSet<float>();
            for (int ch = 0; ch < 3; ch++) if (chKeys[ch] != null) foreach (var t in chKeys[ch].Keys) allTimes.Add(t);

            var track = new MotionTrackDTO
            {
                id = path, targetKind = "path",
                preWrapMode = ToWrap(wrapSrc.preWrapMode), postWrapMode = ToWrap(wrapSrc.postWrapMode)
            };
            foreach (float t in allTimes)
            {
                var key = new MotionKeyDTO { t = t };
                if (chKeys[0] != null && chKeys[0].TryGetValue(t, out var p)) { key.pos = p.v; key.posTan = p.tan; }
                if (chKeys[1] != null && chKeys[1].TryGetValue(t, out var r)) { key.rot = r.v; key.rotTan = r.tan; }
                if (chKeys[2] != null && chKeys[2].TryGetValue(t, out var s)) { key.scl = s.v; key.sclTan = s.tan; }
                track.keys.Add(key);
            }
            return track;
        }

        // 1 本のカーブ → スカラートラック。scale は値と傾きに掛ける倍率（表情は 0.01）。
        private static MotionScalarTrackDTO BuildScalarTrack(string name, AnimationCurve curve, float scale)
        {
            if (curve == null || curve.length == 0) return null;
            var tr = new MotionScalarTrackDTO
            {
                name = name, preWrapMode = ToWrap(curve.preWrapMode), postWrapMode = ToWrap(curve.postWrapMode)
            };
            for (int i = 0; i < curve.length; i++)
                tr.keys.Add(new MotionScalarKeyDTO { t = curve[i].time, v = curve[i].value * scale, tan = ToTangent(curve, i, scale) });
            return tr;
        }

        private static bool SameKeyTimes(AnimationCurve[] comps)
        {
            int n = comps[0].length;
            for (int c = 1; c < comps.Length; c++) if (comps[c].length != n) return false;
            for (int i = 0; i < n; i++)
                for (int c = 1; c < comps.Length; c++) if (comps[c][i].time != comps[0][i].time) return false;
            return true;
        }

        private static MotionTangentDTO ToTangent(AnimationCurve curve, int index, float scale)
        {
            var k = curve[index];
            var tn = new MotionTangentDTO
            {
                // 無限大（Constant）は JSON に書けないので null にし、モードで表す。
                inTangent        = IsFinite(k.inTangent)  ? k.inTangent  * scale : (float?)null,
                outTangent       = IsFinite(k.outTangent) ? k.outTangent * scale : (float?)null,
                weightedMode     = k.weightedMode.ToString(),
                leftTangentMode  = AnimationUtility.GetKeyLeftTangentMode(curve, index).ToString(),
                rightTangentMode = AnimationUtility.GetKeyRightTangentMode(curve, index).ToString()
            };
            if (k.weightedMode == WeightedMode.In  || k.weightedMode == WeightedMode.Both) tn.inWeight  = Mathf.Clamp01(k.inWeight);
            if (k.weightedMode == WeightedMode.Out || k.weightedMode == WeightedMode.Both) tn.outWeight = Mathf.Clamp01(k.outWeight);
            return tn;
        }

        private static string ToWrap(WrapMode m)
            => m == WrapMode.Loop ? "Loop" : m == WrapMode.PingPong ? "PingPong" : "Clamp";

        private static float DefaultComponent(int ch, int comp)
            => ch == 2 ? 1f : (ch == 1 && comp == 3 ? 1f : 0f);

        private static int ClassifyTransformProperty(string prop, out int comp)
        {
            comp = -1;
            if (string.IsNullOrEmpty(prop)) return -1;
            int dot = prop.LastIndexOf('.');
            if (dot < 0 || dot >= prop.Length - 1) return -1;
            string head = prop.Substring(0, dot);
            int ch = head == "m_LocalPosition" ? 0 : head == "m_LocalRotation" ? 1 : head == "m_LocalScale" ? 2 : -1;
            if (ch < 0) return -1;
            comp = "xyzw".IndexOf(prop[prop.Length - 1]);
            if (comp < 0 || comp >= (ch == 1 ? 4 : 3)) return -1;
            return ch;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        // ================================================================
        // UnityBone CSV v3
        //   先頭16列は固定。Runtime の LoadSourceRestCsv がこの並びを前提にする:
        //     f[0]=="UnityBone" / f[3]=Humanoid名 / f[5..7]=Pos / f[12..15]=RestW
        // ================================================================
        private string BuildBoneCsv(Transform rootT, List<Transform> ordered, HashSet<Transform> set,
                                    Dictionary<Transform, string> humanMap, out int humCount)
        {
            humCount = 0;
            var sb = new StringBuilder();
            sb.Append(";UnityBoneCSV,version,3,space,unity,units,m,root,").Append(Esc(rootT.name)).Append('\n');
            if (_writeHeader)
                sb.Append("UnityBone,Name,NameEn,Humanoid,Parent,PosX,PosY,PosZ,")
                  .Append("RestLX,RestLY,RestLZ,RestLW,RestWX,RestWY,RestWZ,RestWW\n");

            foreach (var t in ordered)
            {
                string hum = humanMap.TryGetValue(t, out var h) ? h : "";
                if (hum.Length > 0) humCount++;
                string parent = (t.parent != null && set.Contains(t.parent) && t != rootT) ? t.parent.name : "";
                Vector3    lp = rootT.InverseTransformPoint(t.position);
                Quaternion rl = t.localRotation;
                Quaternion rw = Quaternion.Inverse(rootT.rotation) * t.rotation;
                sb.Append("UnityBone,").Append(Esc(t.name)).Append(',').Append("\"\"").Append(',')
                  .Append(hum).Append(',').Append(Esc(parent)).Append(',')
                  .Append(F(lp.x)).Append(',').Append(F(lp.y)).Append(',').Append(F(lp.z)).Append(',')
                  .Append(F(rl.x)).Append(',').Append(F(rl.y)).Append(',').Append(F(rl.z)).Append(',').Append(F(rl.w)).Append(',')
                  .Append(F(rw.x)).Append(',').Append(F(rw.y)).Append(',').Append(F(rw.z)).Append(',').Append(F(rw.w))
                  .Append('\n');
            }
            return sb.ToString();
        }

        // ================================================================
        // UnityLimit CSV v1（1 行 = 1 Humanoid ボーン。実測列つき）
        // ================================================================
        private string BuildLimitCsv(Animator animator, out int rowCount, out bool measured)
        {
            rowCount = 0;
            var customByTrait = new Dictionary<string, HumanLimit>();
            var human = animator.avatar.humanDescription.human;
            if (human != null)
                foreach (var hb in human)
                    if (!string.IsNullOrEmpty(hb.humanName) && !hb.limit.useDefaultValues)
                        customByTrait[hb.humanName] = hb.limit;

            measured = MeasureMuscles(animator, out var zeroRot, out var minRot, out var maxRot);

            var sb = new StringBuilder();
            sb.Append(";UnityLimitCSV,version,1,units,deg,space,unity,root,").Append(Esc(animator.transform.name))
              .Append(",measured,").Append(measured ? "true" : "false").Append('\n');
            if (_writeHeader)
                sb.Append("UnityLimit,Humanoid,TraitName,BoneName,UseDefault,")
                  .Append("MinX,MinY,MinZ,MaxX,MaxY,MaxZ,CenX,CenY,CenZ,AxisLength,")
                  .Append("Dof0Muscle,Dof1Muscle,Dof2Muscle,")
                  .Append("Dof0Min,Dof0Max,Dof1Min,Dof1Max,Dof2Min,Dof2Max,")
                  .Append("Measured,ZeroLX,ZeroLY,ZeroLZ,ZeroLW,")
                  .Append("D0MinX,D0MinY,D0MinZ,D0MinW,D0MaxX,D0MaxY,D0MaxZ,D0MaxW,")
                  .Append("D1MinX,D1MinY,D1MinZ,D1MinW,D1MaxX,D1MaxY,D1MaxZ,D1MaxW,")
                  .Append("D2MinX,D2MinY,D2MinZ,D2MinW,D2MaxX,D2MaxY,D2MaxZ,D2MaxW\n");

            var muscleNames = HumanTrait.MuscleName;
            for (int bi = 0; bi < HumanTrait.BoneCount; bi++)
            {
                var tr = animator.GetBoneTransform((HumanBodyBones)bi);
                if (tr == null) continue;
                string traitName = HumanTrait.BoneName[bi];
                string enumName  = ((HumanBodyBones)bi).ToString();
                bool hasCustom = customByTrait.TryGetValue(traitName, out var lim);

                Vector3 mn = Vector3.zero, mx = Vector3.zero, ce = Vector3.zero; float axis = 0f;
                if (hasCustom) { mn = lim.min; mx = lim.max; ce = lim.center; axis = lim.axisLength; }
                else
                    for (int dof = 0; dof < 3; dof++)
                    {
                        int mi = HumanTrait.MuscleFromBone(bi, dof);
                        if (mi < 0) continue;
                        mn[dof] = HumanTrait.GetMuscleDefaultMin(mi);
                        mx[dof] = HumanTrait.GetMuscleDefaultMax(mi);
                    }

                var row = new List<string>(53)
                {
                    "UnityLimit", enumName, Esc(traitName), Esc(tr.name), hasCustom ? "false" : "true",
                    F(mn.x), F(mn.y), F(mn.z), F(mx.x), F(mx.y), F(mx.z), F(ce.x), F(ce.y), F(ce.z), F(axis)
                };
                var dofMuscle = new int[3];
                for (int dof = 0; dof < 3; dof++)
                {
                    int mi = HumanTrait.MuscleFromBone(bi, dof);
                    dofMuscle[dof] = mi;
                    row.Add(mi >= 0 && mi < muscleNames.Length ? Esc(muscleNames[mi]) : "\"\"");
                }
                for (int dof = 0; dof < 3; dof++)
                {
                    int mi = dofMuscle[dof];
                    row.Add(mi >= 0 ? F(HumanTrait.GetMuscleDefaultMin(mi)) : "0");
                    row.Add(mi >= 0 ? F(HumanTrait.GetMuscleDefaultMax(mi)) : "0");
                }
                row.Add(measured ? "true" : "false");
                AppendQuat(row, measured ? zeroRot[bi] : Quaternion.identity);
                for (int dof = 0; dof < 3; dof++)
                {
                    bool okm = measured && dofMuscle[dof] >= 0;
                    AppendQuat(row, okm ? minRot[bi, dof] : Quaternion.identity);
                    AppendQuat(row, okm ? maxRot[bi, dof] : Quaternion.identity);
                }
                sb.Append(string.Join(",", row.ToArray())).Append('\n');
                rowCount++;
            }
            return sb.ToString();
        }

        private static void AppendQuat(List<string> row, Quaternion q)
        {
            row.Add(F(q.x)); row.Add(F(q.y)); row.Add(F(q.z)); row.Add(F(q.w));
        }

        // マッスル実測：0 / -1 / +1 に振ったときのローカル回転。終了後に TRS を復元する。
        private static bool MeasureMuscles(Animator animator, out Quaternion[] zeroRot,
                                           out Quaternion[,] minRot, out Quaternion[,] maxRot)
        {
            int boneCount = HumanTrait.BoneCount;
            zeroRot = new Quaternion[boneCount];
            minRot  = new Quaternion[boneCount, 3];
            maxRot  = new Quaternion[boneCount, 3];
            for (int i = 0; i < boneCount; i++)
            {
                zeroRot[i] = Quaternion.identity;
                for (int d = 0; d < 3; d++) { minRot[i, d] = Quaternion.identity; maxRot[i, d] = Quaternion.identity; }
            }
            if (animator.avatar == null || !animator.avatar.isHuman) return false;

            Transform rootT = animator.transform;
            var all = rootT.GetComponentsInChildren<Transform>(true);
            var sp = new Vector3[all.Length]; var sr = new Quaternion[all.Length]; var ss = new Vector3[all.Length];
            for (int i = 0; i < all.Length; i++) { sp[i] = all[i].localPosition; sr[i] = all[i].localRotation; ss[i] = all[i].localScale; }

            HumanPoseHandler handler = null;
            try
            {
                handler = new HumanPoseHandler(animator.avatar, rootT);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                if (pose.muscles == null || pose.muscles.Length == 0) return false;
                int muscleCount = pose.muscles.Length;

                for (int m = 0; m < muscleCount; m++) pose.muscles[m] = 0f;
                handler.SetHumanPose(ref pose);
                for (int bi = 0; bi < boneCount; bi++)
                {
                    var tr = animator.GetBoneTransform((HumanBodyBones)bi);
                    if (tr != null) zeroRot[bi] = tr.localRotation;
                }
                for (int bi = 0; bi < boneCount; bi++)
                {
                    var tr = animator.GetBoneTransform((HumanBodyBones)bi);
                    if (tr == null) continue;
                    for (int dof = 0; dof < 3; dof++)
                    {
                        int mi = HumanTrait.MuscleFromBone(bi, dof);
                        if (mi < 0 || mi >= muscleCount) continue;
                        for (int m = 0; m < muscleCount; m++) pose.muscles[m] = 0f;
                        pose.muscles[mi] = -1f; handler.SetHumanPose(ref pose); minRot[bi, dof] = tr.localRotation;
                        pose.muscles[mi] =  1f; handler.SetHumanPose(ref pose); maxRot[bi, dof] = tr.localRotation;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[UnityClipExportWindow] マッスル実測に失敗しました: " + e);
                return false;
            }
            finally
            {
                if (handler != null) handler.Dispose();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    all[i].localPosition = sp[i]; all[i].localRotation = sr[i]; all[i].localScale = ss[i];
                }
            }
        }

        // ================================================================
        // ボーン集合
        // ================================================================
        private static Dictionary<Transform, string> BuildHumanoidMap(Animator a)
        {
            var map = new Dictionary<Transform, string>();
            if (a == null || a.avatar == null || !a.avatar.isHuman) return map;
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var hbb = (HumanBodyBones)i;
                var tr  = a.GetBoneTransform(hbb);
                if (tr != null && !map.ContainsKey(tr)) map[tr] = hbb.ToString();
            }
            return map;
        }

        private HashSet<Transform> CollectBones(Transform rootT, Dictionary<Transform, string> humanMap)
        {
            var set = new HashSet<Transform>();
            if (!_skinnedOnly)
            {
                foreach (var t in rootT.GetComponentsInChildren<Transform>(true)) set.Add(t);
                return set;
            }
            foreach (var smr in rootT.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.rootBone != null) set.Add(smr.rootBone);
                if (smr.bones != null) foreach (var b in smr.bones) if (b != null) set.Add(b);
            }
            foreach (var kv in humanMap) set.Add(kv.Key);

            var withAncestors = new HashSet<Transform>(set);
            foreach (var t in set)
                for (var p = t.parent; p != null; p = p.parent) { withAncestors.Add(p); if (p == rootT) break; }
            withAncestors.Add(rootT);
            if (withAncestors.Count <= 1)
            {
                withAncestors.Clear();
                foreach (var t in rootT.GetComponentsInChildren<Transform>(true)) withAncestors.Add(t);
            }
            return withAncestors;
        }

        private static void DfsOrder(Transform t, HashSet<Transform> set, List<Transform> outList)
        {
            if (set.Contains(t)) outList.Add(t);
            for (int i = 0; i < t.childCount; i++) DfsOrder(t.GetChild(i), set, outList);
        }

        // ================================================================
        // ユーティリティ
        // ================================================================
        private static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static string SanitizeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "out";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
