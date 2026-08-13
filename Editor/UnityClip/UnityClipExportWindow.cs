// Editor/UnityClip/UnityClipExportWindow.cs
// ============================================================
// UnityClip 一括書き出し（旧 AnimationClipExtractWindow ＋ UnityBoneCsvExporter を統合）
// ------------------------------------------------------------
// 「生データをそのまま出す」方針。加工・推測はしない。
//
// ■ 出力（セットしたものだけ書き出す）
//   1) <clip>.json          … UnityClipDTO。bones（Transform カーブ）＋ muscles（生）。
//                             ※ bakedBones は出力しない（焼き込みは行わない）。
//   2) <root>_bones.csv     … UnityBone CSV v3。先頭16列は固定（Runtime の
//                             UnityClipApplier.LoadSourceRestCsv がこの並びを前提とする）。
//   3) <root>_limits.csv    … UnityLimit CSV v1。humanDescription.human[].limit を
//                             全 Humanoid ボーン分（既定値のボーンも展開して明示）。
//                             加えて、マッスルを -1 / 0 / +1 に振った実測ローカル回転を
//                             同じ行に持つ（pre/post 回転・sign を含む実測値）。
//
// ■ 座標系
//   すべて Unity 左手系のまま。座標変換は一切行わない。
//
// ■ レスト姿勢について
//   bones.csv の Rest 列は「書き出し時のシーン上の姿勢」をそのまま書く。
//   実行前にモデルをレスト（束ね）姿勢にしておくこと。
//   実測（limits.csv）はシーンのポーズを一時的に変更するが、書き出し後に
//   全 Transform の TRS を復元する。
// ============================================================

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Poly_Ling.UnityClip;

namespace Poly_Ling.UnityClip.Editor
{
    public class UnityClipExportWindow : EditorWindow
    {
        // ── 入力 ──────────────────────────────────────────────────────────
        private AnimationClip _clip;
        private Animator      _animator;
        private string        _outDir = "";

        // ── オプション ────────────────────────────────────────────────────
        private bool _skinnedOnly    = true;   // Skinned ボーン + Humanoid 骨のみ（false = 全 Transform）
        private bool _writeHeader    = true;   // 人間可読なヘッダ行
        private bool _utf8Bom        = true;   // UTF-8 BOM（Excel 想定）
        private bool _measureMuscles = true;   // マッスル実測（limits.csv の実測列）

        [MenuItem("PolyLing/UnityClip/UnityClip 一括書き出し")]
        public static void Open()
        {
            var w = GetWindow<UnityClipExportWindow>(true, "UnityClip 一括書き出し", true);
            w.minSize = new Vector2(420, 320);
            var sel = UnityEditor.Selection.activeGameObject;
            if (sel != null && w._animator == null)
            {
                var a = sel.GetComponent<Animator>();
                if (a == null) a = sel.GetComponentInChildren<Animator>(true);
                w._animator = a;
            }
            w.Show();
        }

        // ================================================================
        // UI
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.LabelField("UnityClip 一括書き出し", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _clip = (AnimationClip)EditorGUILayout.ObjectField(
                "AnimationClip", _clip, typeof(AnimationClip), false);

            _animator = (Animator)EditorGUILayout.ObjectField(
                "Avatar (Animator)", _animator, typeof(Animator), true);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                _outDir = EditorGUILayout.TextField("出力フォルダ", _outDir);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string sel = EditorUtility.SaveFolderPanel("出力フォルダを選択", _outDir, "");
                    if (!string.IsNullOrEmpty(sel)) _outDir = sel;
                }
            }

            EditorGUILayout.Space();
            _skinnedOnly    = EditorGUILayout.ToggleLeft("Skinned ボーン + Humanoid 骨のみ（推奨）", _skinnedOnly);
            _writeHeader    = EditorGUILayout.ToggleLeft("ヘッダ行を付ける", _writeHeader);
            _utf8Bom        = EditorGUILayout.ToggleLeft("UTF-8 BOM を付ける", _utf8Bom);
            _measureMuscles = EditorGUILayout.ToggleLeft("マッスルを実測する（limits.csv の実測列）", _measureMuscles);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "AnimationClip をセットすると <clip>.json を書き出します。\n" +
                "Animator をセットすると <root>_bones.csv と <root>_limits.csv を書き出します。\n" +
                "bones.csv の Rest 列はシーン上の現在姿勢です。実行前にレスト（束ね）姿勢にしてください。\n" +
                "実測はポーズを一時変更しますが、完了後に TRS を復元します。",
                MessageType.Info);

            EditorGUILayout.Space();
            bool ready = (_clip != null || _animator != null) && !string.IsNullOrEmpty(_outDir);
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("書き出し", GUILayout.Height(30)))
                {
                    ExportAll();
                }
            }
        }

        // ================================================================
        // 書き出し本体
        // ================================================================

        private void ExportAll()
        {
            if (string.IsNullOrEmpty(_outDir) || !Directory.Exists(_outDir))
            {
                EditorUtility.DisplayDialog("エラー", "出力フォルダが存在しません:\n" + _outDir, "OK");
                return;
            }

            var written = new List<string>();
            var notes   = new List<string>();

            // ── 1) クリップ JSON ──────────────────────────────────────────
            if (_clip != null)
            {
                var dto = BuildClipDto(_clip);
                string name = string.IsNullOrEmpty(_clip.name) ? "clip" : _clip.name;
                string path = Path.Combine(_outDir, SanitizeFileName(name) + ".json");
                UnityClipSerializer.SaveJson(dto, path);
                written.Add($"{Path.GetFileName(path)}  (bones {dto.bones.Count} / muscles {dto.muscles.Count} / type {dto.clipType})");
            }

            // ── 2) 3) 骨 CSV・リミット CSV ────────────────────────────────
            if (_animator != null)
            {
                bool isHuman = _animator.avatar != null && _animator.avatar.isHuman;
                Transform rootT = _animator.transform;
                string baseName = SanitizeFileName(rootT.name);

                // レスト姿勢のまま骨 CSV 用の行を作る（実測より先に確定させる）
                var humanMap = BuildHumanoidMap(_animator);
                var bones    = CollectBones(rootT, humanMap);
                var ordered  = new List<Transform>();
                DfsOrder(rootT, bones, ordered);
                string boneCsv = BuildBoneCsv(rootT, ordered, bones, humanMap, out int humCount);

                string bonePath = Path.Combine(_outDir, baseName + "_bones.csv");
                File.WriteAllText(bonePath, boneCsv, new UTF8Encoding(_utf8Bom));
                written.Add($"{Path.GetFileName(bonePath)}  ({ordered.Count} bones / Humanoid {humCount})");

                if (isHuman)
                {
                    string limitCsv = BuildLimitCsv(_animator, out int limitRows, out bool measured);
                    string limitPath = Path.Combine(_outDir, baseName + "_limits.csv");
                    File.WriteAllText(limitPath, limitCsv, new UTF8Encoding(_utf8Bom));
                    written.Add($"{Path.GetFileName(limitPath)}  ({limitRows} bones / measured={measured})");
                }
                else
                {
                    notes.Add("Humanoid アバターではないため limits.csv は書き出していません。");
                }

                if (!isHuman) notes.Add("bones.csv の Humanoid 列は空になります。");
            }

            AssetDatabase.Refresh();

            var sb = new StringBuilder();
            sb.Append("書き出し完了:\n");
            foreach (var w in written) sb.Append("  ").Append(w).Append('\n');
            if (notes.Count > 0)
            {
                sb.Append("\n注意:\n");
                foreach (var n in notes) sb.Append("  ").Append(n).Append('\n');
            }
            sb.Append('\n').Append(_outDir);

            Debug.Log("[UnityClipExportWindow] " + sb.ToString());
            EditorUtility.DisplayDialog("UnityClip 一括書き出し", sb.ToString(), "OK");
        }

        // ================================================================
        // 1) AnimationClip -> UnityClipDTO
        //    - Transform カーブ: m_LocalPosition / m_LocalRotation / m_LocalScale
        //    - Animator バインディング（マッスル/ルート等）は生のまま muscles へ
        //    - 接線は捨て、キー時刻で Evaluate してサンプル化（t は秒・丸めなし）
        //    - 座標変換なし。bakedBones は作らない
        // ================================================================

        private class PathCurves
        {
            // [channel][component] : 0=pos(3) 1=rot(4) 2=scl(3)
            public readonly AnimationCurve[][] C =
            {
                new AnimationCurve[3],
                new AnimationCurve[4],
                new AnimationCurve[3]
            };

            public void Set(int ch, int comp, AnimationCurve curve) { C[ch][comp] = curve; }

            public bool Has(int ch)
            {
                var arr = C[ch];
                for (int i = 0; i < arr.Length; i++) if (arr[i] != null) return true;
                return false;
            }
        }

        private static UnityClipDTO BuildClipDto(AnimationClip clip)
        {
            float fps = clip.frameRate > 0f ? clip.frameRate : 30f;

            var dto = new UnityClipDTO
            {
                name      = clip.name,
                clipType  = "Generic",
                frameRate = fps,
                loop      = clip.isLooping
            };

            var map = new Dictionary<string, PathCurves>();

            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type != typeof(Transform))
                {
                    // Humanoid の Animator バインディング（マッスル・RootT/RootQ 等）は
                    // 分類も変換もせず、propertyName をそのまま名前にして格納する。
                    if (b.type == typeof(Animator))
                    {
                        var mcurve = AnimationUtility.GetEditorCurve(clip, b);
                        var mtrack = BuildMuscleTrack(b.propertyName, mcurve);
                        if (mtrack != null) dto.muscles.Add(mtrack);
                    }
                    continue;
                }

                int ch = ClassifyProperty(b.propertyName, out int comp);
                if (ch < 0) continue;   // localEulerAnglesRaw 等は未対応

                var curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null) continue;

                if (!map.TryGetValue(b.path, out var pc))
                {
                    pc = new PathCurves();
                    map[b.path] = pc;
                }
                pc.Set(ch, comp, curve);
            }

            if (dto.muscles.Count > 0) dto.clipType = "Humanoid";

            foreach (var kv in map)
            {
                var track = BuildTrack(kv.Key, kv.Value);
                if (track != null) dto.bones.Add(track);
            }

            return dto;
        }

        // property 名 -> (channel, component)。対象外は -1。
        private static int ClassifyProperty(string prop, out int comp)
        {
            comp = -1;
            if (string.IsNullOrEmpty(prop)) return -1;

            int dot = prop.LastIndexOf('.');
            if (dot < 0 || dot >= prop.Length - 1) return -1;
            string head = prop.Substring(0, dot);
            char   c    = prop[prop.Length - 1];

            int ch;
            if (head == "m_LocalPosition")      ch = 0;
            else if (head == "m_LocalRotation") ch = 1;
            else if (head == "m_LocalScale")    ch = 2;
            else return -1;

            switch (c)
            {
                case 'x': comp = 0; break;
                case 'y': comp = 1; break;
                case 'z': comp = 2; break;
                case 'w': comp = 3; break;
                default: return -1;
            }
            if (ch != 1 && comp == 3) return -1;
            return ch;
        }

        private static UnityBoneTrackDTO BuildTrack(string path, PathCurves pc)
        {
            // 全成分カーブのキー時刻を union（丸めなし）
            var times = new SortedSet<float>();
            for (int ch = 0; ch < 3; ch++)
            {
                var arr = pc.C[ch];
                for (int i = 0; i < arr.Length; i++)
                {
                    var c = arr[i];
                    if (c == null) continue;
                    var keys = c.keys;
                    for (int k = 0; k < keys.Length; k++) times.Add(keys[k].time);
                }
            }
            if (times.Count == 0) return null;

            bool hasPos = pc.Has(0);
            bool hasRot = pc.Has(1);
            bool hasScl = pc.Has(2);

            var track = new UnityBoneTrackDTO { path = path };
            foreach (float t in times)
            {
                var key = new UnityBoneKeyDTO { t = t };
                if (hasPos) key.pos = Sample(pc.C[0], t, 3);
                if (hasRot) key.rot = Sample(pc.C[1], t, 4);
                if (hasScl) key.scl = Sample(pc.C[2], t, 3);
                track.keys.Add(key);
            }
            return track;
        }

        private static float[] Sample(AnimationCurve[] comps, float t, int n)
        {
            var v = new float[n];
            for (int i = 0; i < n; i++)
                v[i] = comps[i] != null ? comps[i].Evaluate(t) : 0f;
            return v;
        }

        private static UnityMuscleTrackDTO BuildMuscleTrack(string name, AnimationCurve curve)
        {
            if (curve == null) return null;
            var keys = curve.keys;
            if (keys == null || keys.Length == 0) return null;

            var track = new UnityMuscleTrackDTO { name = name };
            for (int i = 0; i < keys.Length; i++)
            {
                float t = keys[i].time;
                track.w.Add(new UnityWeightKeyDTO { t = t, v = curve.Evaluate(t) });
            }
            return track;
        }

        // ================================================================
        // 2) UnityBone CSV v3
        //    先頭16列は固定。Runtime の LoadSourceRestCsv がこの並びを前提にする:
        //      f[0]=="UnityBone" / f[3]=Humanoid名 / f[5..7]=Pos / f[12..15]=RestW
        //    可動域は limits.csv 側に分離したため、ここでは付けない。
        // ================================================================

        private string BuildBoneCsv(
            Transform rootT,
            List<Transform> ordered,
            HashSet<Transform> set,
            Dictionary<Transform, string> humanMap,
            out int humCount)
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

                Vector3    lp = rootT.InverseTransformPoint(t.position);              // ルート基準の絶対位置
                Quaternion rl = t.localRotation;                                       // レスト・ローカル回転
                Quaternion rw = Quaternion.Inverse(rootT.rotation) * t.rotation;       // レスト・ワールド回転（ルート相対）

                sb.Append("UnityBone,")
                  .Append(Esc(t.name)).Append(',')
                  .Append("\"\"").Append(',')          // NameEn（未使用）
                  .Append(hum).Append(',')
                  .Append(Esc(parent)).Append(',')
                  .Append(F(lp.x)).Append(',').Append(F(lp.y)).Append(',').Append(F(lp.z)).Append(',')
                  .Append(F(rl.x)).Append(',').Append(F(rl.y)).Append(',').Append(F(rl.z)).Append(',').Append(F(rl.w)).Append(',')
                  .Append(F(rw.x)).Append(',').Append(F(rw.y)).Append(',').Append(F(rw.z)).Append(',').Append(F(rw.w))
                  .Append('\n');
            }
            return sb.ToString();
        }

        // ================================================================
        // 3) UnityLimit CSV v1
        //    1 行 = 1 Humanoid ボーン。既定値のボーンも HumanTrait の既定を展開して書く。
        //    実測列: マッスルを 0 / -1 / +1 に振ったときのローカル回転（クォータニオン）。
        //    実測値には Muscle Referential の pre/post 回転・sign が含まれる。
        // ================================================================

        private string BuildLimitCsv(Animator animator, out int rowCount, out bool measured)
        {
            rowCount = 0;
            measured = false;

            // humanDescription の custom limit を HumanTrait 名で引けるようにする
            var customByTrait = new Dictionary<string, HumanLimit>();
            var human = animator.avatar.humanDescription.human;
            if (human != null)
            {
                foreach (var hb in human)
                {
                    if (string.IsNullOrEmpty(hb.humanName)) continue;
                    if (hb.limit.useDefaultValues) continue;
                    customByTrait[hb.humanName] = hb.limit;
                }
            }

            // 実測
            Quaternion[] zeroRot = null;                 // [boneIndex]
            Quaternion[,] minRot = null, maxRot = null;  // [boneIndex, dof]
            if (_measureMuscles)
                measured = MeasureMuscles(animator, out zeroRot, out minRot, out maxRot);

            var sb = new StringBuilder();
            sb.Append(";UnityLimitCSV,version,1,units,deg,space,unity,root,")
              .Append(Esc(animator.transform.name))
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

            int boneCount = HumanTrait.BoneCount;
            var muscleNames = HumanTrait.MuscleName;

            for (int bi = 0; bi < boneCount; bi++)
            {
                var tr = animator.GetBoneTransform((HumanBodyBones)bi);
                if (tr == null) continue;

                string traitName = HumanTrait.BoneName[bi];              // 例 "Left Upper Arm"
                string enumName  = ((HumanBodyBones)bi).ToString();      // 例 "LeftUpperArm"

                bool hasCustom = customByTrait.TryGetValue(traitName, out var lim);

                // 可動域（度）。custom があればそれ、無ければ HumanTrait の既定を展開する。
                Vector3 mn, mx, ce;
                float   axis;
                if (hasCustom)
                {
                    mn = lim.min; mx = lim.max; ce = lim.center; axis = lim.axisLength;
                }
                else
                {
                    mn = Vector3.zero; mx = Vector3.zero; ce = Vector3.zero; axis = 0f;
                    for (int dof = 0; dof < 3; dof++)
                    {
                        int mi = HumanTrait.MuscleFromBone(bi, dof);
                        if (mi < 0) continue;
                        mn[dof] = HumanTrait.GetMuscleDefaultMin(mi);
                        mx[dof] = HumanTrait.GetMuscleDefaultMax(mi);
                    }
                }

                var row = new List<string>(53);
                row.Add("UnityLimit");
                row.Add(enumName);
                row.Add(Esc(traitName));
                row.Add(Esc(tr.name));
                row.Add(hasCustom ? "false" : "true");
                row.Add(F(mn.x)); row.Add(F(mn.y)); row.Add(F(mn.z));
                row.Add(F(mx.x)); row.Add(F(mx.y)); row.Add(F(mx.z));
                row.Add(F(ce.x)); row.Add(F(ce.y)); row.Add(F(ce.z));
                row.Add(F(axis));

                // dof -> マッスル名 / 既定可動域（度）
                var dofMuscle = new int[3];
                for (int dof = 0; dof < 3; dof++)
                {
                    int mi = HumanTrait.MuscleFromBone(bi, dof);
                    dofMuscle[dof] = mi;
                    row.Add(mi >= 0 && muscleNames != null && mi < muscleNames.Length
                        ? Esc(muscleNames[mi]) : "\"\"");
                }
                for (int dof = 0; dof < 3; dof++)
                {
                    int mi = dofMuscle[dof];
                    if (mi >= 0)
                    {
                        row.Add(F(HumanTrait.GetMuscleDefaultMin(mi)));
                        row.Add(F(HumanTrait.GetMuscleDefaultMax(mi)));
                    }
                    else
                    {
                        row.Add("0"); row.Add("0");
                    }
                }

                // 実測列
                bool hasMeasure = measured && zeroRot != null;
                row.Add(hasMeasure ? "true" : "false");
                AppendQuat(row, hasMeasure ? zeroRot[bi] : Quaternion.identity);
                for (int dof = 0; dof < 3; dof++)
                {
                    bool ok = hasMeasure && dofMuscle[dof] >= 0;
                    AppendQuat(row, ok ? minRot[bi, dof] : Quaternion.identity);
                    AppendQuat(row, ok ? maxRot[bi, dof] : Quaternion.identity);
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

        // ----------------------------------------------------------------
        // マッスル実測
        //   HumanPoseHandler で muscles を直接与え、GetBoneTransform().localRotation を読む。
        //   0 / -1 / +1 の 3 状態。-1/+1 は該当マッスルのみ振り、他は 0 に固定する。
        //   シーンのポーズを変更するため、全 Transform の TRS を退避して最後に復元する。
        // ----------------------------------------------------------------
        private static bool MeasureMuscles(
            Animator animator,
            out Quaternion[] zeroRot,
            out Quaternion[,] minRot,
            out Quaternion[,] maxRot)
        {
            int boneCount = HumanTrait.BoneCount;
            zeroRot = new Quaternion[boneCount];
            minRot  = new Quaternion[boneCount, 3];
            maxRot  = new Quaternion[boneCount, 3];
            for (int i = 0; i < boneCount; i++)
            {
                zeroRot[i] = Quaternion.identity;
                for (int d = 0; d < 3; d++)
                {
                    minRot[i, d] = Quaternion.identity;
                    maxRot[i, d] = Quaternion.identity;
                }
            }

            if (animator.avatar == null || !animator.avatar.isHuman) return false;

            Transform rootT = animator.transform;

            // TRS 退避
            var all   = rootT.GetComponentsInChildren<Transform>(true);
            var savedP = new Vector3[all.Length];
            var savedR = new Quaternion[all.Length];
            var savedS = new Vector3[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                savedP[i] = all[i].localPosition;
                savedR[i] = all[i].localRotation;
                savedS[i] = all[i].localScale;
            }

            HumanPoseHandler handler = null;
            try
            {
                handler = new HumanPoseHandler(animator.avatar, rootT);

                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                if (pose.muscles == null || pose.muscles.Length == 0) return false;

                int muscleCount = pose.muscles.Length;

                // 0 姿勢
                for (int m = 0; m < muscleCount; m++) pose.muscles[m] = 0f;
                handler.SetHumanPose(ref pose);
                for (int bi = 0; bi < boneCount; bi++)
                {
                    var tr = animator.GetBoneTransform((HumanBodyBones)bi);
                    if (tr != null) zeroRot[bi] = tr.localRotation;
                }

                // 各マッスルを単独で -1 / +1
                for (int bi = 0; bi < boneCount; bi++)
                {
                    var tr = animator.GetBoneTransform((HumanBodyBones)bi);
                    if (tr == null) continue;

                    for (int dof = 0; dof < 3; dof++)
                    {
                        int mi = HumanTrait.MuscleFromBone(bi, dof);
                        if (mi < 0 || mi >= muscleCount) continue;

                        for (int m = 0; m < muscleCount; m++) pose.muscles[m] = 0f;

                        pose.muscles[mi] = -1f;
                        handler.SetHumanPose(ref pose);
                        minRot[bi, dof] = tr.localRotation;

                        pose.muscles[mi] = 1f;
                        handler.SetHumanPose(ref pose);
                        maxRot[bi, dof] = tr.localRotation;
                    }
                }

                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[UnityClipExportWindow] マッスル実測に失敗しました: " + e);
                return false;
            }
            finally
            {
                if (handler != null) handler.Dispose();

                // TRS 復元
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    all[i].localPosition = savedP[i];
                    all[i].localRotation = savedR[i];
                    all[i].localScale    = savedS[i];
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
                if (smr.bones != null)
                    foreach (var b in smr.bones) if (b != null) set.Add(b);
            }
            foreach (var kv in humanMap) set.Add(kv.Key);

            // 親チェーンを root まで補完（Parent 列が途切れないように）
            var withAncestors = new HashSet<Transform>(set);
            foreach (var t in set)
            {
                var p = t.parent;
                while (p != null)
                {
                    withAncestors.Add(p);
                    if (p == rootT) break;
                    p = p.parent;
                }
            }
            withAncestors.Add(rootT);

            // Skinned が無いモデルの保険
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
            s = s.Replace("\"", "\"\"");
            return "\"" + s + "\"";   // 名前は常に引用（安全側）
        }

        private static string SanitizeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "out";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
