// FbxMotionReader.cs
// ============================================================
// FBX のアニメーション（テイク）→ PolyLing モーション（MotionClipDTO）
// ------------------------------------------------------------
// 規則の正本は本ファイル冒頭と PolyLing_FBX対応計画.md 段 5。
//
// ■ 流れ
//   1. FbxScene で読む（座標は右手系・Y 上・+Z 正面・m。FbxImporter と同じ）。
//   2. 関節 = FbxNodeTable のボーン（入れ物・メッシュだけのノードは除く）。レストは各関節のバインド時のワールド（bw）。
//      レスト用の FBX（RestFbxPath。同じ骨格のモデル）を渡すと、そのファイルの bw を関節名で引いてレストにする。
//      モーションだけの FBX はバインドポーズを持たず、bw がノードの既定の変換（T ポーズとは限らない）になるため。
//      T ポーズ整列 A は腕・脚の向きしか直さないので、手首などの向きはレストが T ポーズでないとずれる。
//      Humanoid に割り当てた関節がレスト用の FBX に無いときは、その関節だけ自分の bw を使い、警告する。
//   3. 関節名を Humanoid に割り当てる：HumanoidBoneMapping.AutoMapFromEmbeddedCSV（モデルの Humanoid 自動割当と同じ表。
//      PMX・Mixamo・Unity など）→ 割り当て CSV で上書き・追加（BVH と同じ書式）。
//   4. テイクを t = 始まり + f / fps で評価し（FbxScene.AnimSample）、関節のワールド回転と位置を取る。
//      fps はシーンの設定（無ければ 30）。フレーム数は round((終わり − 始まり)·fps) + 1。
//   5. CanonMotionBuilder（BVH と共通）：Δ = (W·R⁻¹)·A → 正準ローカル回転と腰の正規化位置 → マッスル＋body。
//      R はレストのワールド回転（BVH は単位だが FBX は一般に単位でない）。
//   6. 表情：ブレンドシェイプのチャンネルの重みを表情トラックにする。表情名はチャンネル名から
//      「メッシュのノード名_」を外したもの（FbxImporter と同じ）。同じ表情名は最初のチャンネルを使う。
//      重みがどのフレームでも 0 のチャンネルは書かない。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Motion;

namespace Poly_Ling.Fbx
{
    /// <summary>FBX のモーション読み込みの設定。</summary>
    public sealed class FbxMotionReadOptions
    {
        /// <summary>読むテイクの索引（0 から）。</summary>
        public int TakeIndex;

        /// <summary>割り当て CSV の行（1 行目は見出し、各行「Humanoid 名,関節名」）。null なら使わない。</summary>
        public IList<string> MappingCsvLines;

        /// <summary>
        /// レスト（T ポーズ）として使う同じ骨格のモデル FBX の絶対パス。そのバインドポーズを関節名で使う。
        /// 空なら読み込むファイル自身のバインド姿勢。
        /// </summary>
        public string RestFbxPath;
    }

    public static class FbxMotionReader
    {
        public static MotionClipLoadResult Load(string path, FbxMotionReadOptions options)
        {
            var r = new MotionClipLoadResult();
            options = options ?? new FbxMotionReadOptions();

            using (var scene = FbxScene.Load(path, out string error))
            {
                if (scene == null) { r.Error(MotionFileCodes.FileReadFailed, error); return r; }
                try
                {
                    var dto = ToMotion(scene, Path.GetFileNameWithoutExtension(path ?? ""), options, r);
                    if (dto == null) return r;
                    MotionClipSerializer.Validate(dto, r);
                    if (r.ErrorCount == 0) r.Dto = dto;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FbxMotionReader] {ex}");
                    r.Error(MotionFileCodes.FbxInvalid, ex.Message);
                }
            }
            return r;
        }

        private static MotionClipDTO ToMotion(FbxScene scene, string fileName, FbxMotionReadOptions opt, MotionClipLoadResult r)
        {
            if (scene.AnimStackCount == 0) { r.Error(MotionFileCodes.FbxInvalid, "アニメーション（テイク）がありません"); return null; }
            int take = opt.TakeIndex;
            if (take < 0 || take >= scene.AnimStackCount)
            { r.Error(MotionFileCodes.FbxInvalid, $"テイク {take} がありません（テイクは {scene.AnimStackCount} 個）"); return null; }
            if (scene.AnimStackCount > 1)
                r.Warning(MotionFileCodes.FbxNotRead, $"テイクが {scene.AnimStackCount} 個あります。{take} 番（{scene.AnimStackName(take)}）を読みます");

            // ── 関節 ───────────────────────────────────────────────
            var table  = FbxNodeTable.Read(scene);
            var joints = new List<int>();
            foreach (int n in table.Traverse()) if (table.IsBone(n)) joints.Add(n);
            if (joints.Count == 0) { r.Error(MotionFileCodes.FbxInvalid, "ボーンがありません"); return null; }
            int nJ = joints.Count;

            // ── 割り当て ───────────────────────────────────────────
            var names = new List<string>(nJ);
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < nJ; j++)
            {
                string name = table.Nodes[joints[j]].Name ?? "";
                names.Add(name);
                if (!index.ContainsKey(name)) index[name] = j;
            }
            var boneJoint = AutoMap(names);
            string tableName = $"自動割当 {boneJoint.Count} 本";
            if (opt.MappingCsvLines != null)
            {
                var bad = new List<string>();
                int applied = CanonMotionBuilder.ApplyMappingCsv(boneJoint, opt.MappingCsvLines, index, bad);
                tableName += $" + CSV {applied} 行";
                if (bad.Count > 0)
                    r.Warning(MotionFileCodes.FbxNotRead, $"割り当て CSV のうち {bad.Count} 行を使いません（{string.Join(", ", bad)}）");
            }
            CanonMotionBuilder.FilterAndCheck(boneJoint, tableName, MotionFileCodes.FbxNotRead, r);
            if (!boneJoint.ContainsKey(HumanBodyBones.Hips))
            { r.Error(MotionFileCodes.FbxInvalid, "Hips に当たるボーンがありません（割り当て CSV で指定してください）"); return null; }

            // ── レスト（ファイルの座標）────────────────────────────
            var restP = new Vector3[nJ];
            var restR = new Quaternion[nJ];
            float groundY = float.PositiveInfinity;
            var restWorld = new Matrix4x4[nJ];
            for (int j = 0; j < nJ; j++) restWorld[j] = table.BindWorld[joints[j]];
            if (!string.IsNullOrEmpty(opt.RestFbxPath) &&
                !ApplyRestFbx(opt.RestFbxPath, names, boneJoint, restWorld, r))
                return null;
            for (int j = 0; j < nJ; j++)
            {
                var m = restWorld[j];
                restP[j] = m.GetColumn(3);
                restR[j] = RotationOf(m);
                groundY = Mathf.Min(groundY, restP[j].y);
            }

            // ── 時間 ───────────────────────────────────────────────
            scene.AnimStackInfo(take, out double begin, out double end, out double fpsD);
            float fps = fpsD > 0 ? (float)fpsD : 30f;
            int frames = Mathf.Max(1, (int)Math.Round((end - begin) * fps) + 1);
            double dt = 1.0 / fps;

            // ── 評価（全フレームをまとめて）─────────────────────────
            int nCh = scene.BlendChannelCount;
            var world   = new float[(long)frames * nJ * 12];
            var weights = nCh > 0 ? new float[(long)frames * nCh] : null;
            int done = scene.AnimSample(take, begin, dt, frames, joints.ToArray(), world, weights, out string sampleError);
            if (done < frames)
            { r.Error(MotionFileCodes.FbxInvalid, $"{done} フレーム目を評価できません: {sampleError}"); return null; }

            var src = new CanonMotionSource
            {
                Name        = string.IsNullOrEmpty(scene.AnimStackName(take)) ? fileName : scene.AnimStackName(take),
                FrameRate   = fps,
                FrameTime   = (float)dt,
                FrameCount  = frames,
                JointCount  = nJ,
                RestPos     = restP,
                RestRot     = restR,
                GroundY     = groundY,
                BoneJoint   = boneJoint,
                CreatedWith = $"PolyLing FbxMotionReader ({tableName})",
                ErrorCode   = MotionFileCodes.FbxInvalid,
                SampleWorld = (f, wr, wp) =>
                {
                    long o = (long)f * nJ * 12;
                    for (int j = 0; j < nJ; j++, o += 12)
                    {
                        var m = Matrix4x4.identity;
                        for (int c = 0; c < 4; c++)
                        {
                            m[0, c] = world[o + c * 3 + 0];
                            m[1, c] = world[o + c * 3 + 1];
                            m[2, c] = world[o + c * 3 + 2];
                        }
                        wr[j] = RotationOf(m);
                        wp[j] = m.GetColumn(3);
                    }
                },
            };

            var dto = CanonMotionBuilder.Build(src, r, out _);
            if (dto == null) return null;

            if (weights != null) AddExpressions(scene, table, dto, weights, frames, nCh, (float)dt);
            return dto;
        }

        /// <summary>
        /// レスト用の FBX のバインド時のワールドを、関節名が同じボーンから restWorld へ写す。
        /// Humanoid に割り当てた関節が 1 本も当たらなければ失敗（別の骨格を渡したとみなす）。
        /// </summary>
        private static bool ApplyRestFbx(string path, List<string> names, Dictionary<HumanBodyBones, int> boneJoint,
                                         Matrix4x4[] restWorld, MotionClipLoadResult r)
        {
            using (var rest = FbxScene.Load(path, out string error))
            {
                if (rest == null) { r.Error(MotionFileCodes.FbxInvalid, $"レスト用の FBX を読めません: {error}"); return false; }
                var t = FbxNodeTable.Read(rest);
                var byName = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (int n in t.Traverse())
                    if (t.IsBone(n) && !byName.ContainsKey(t.Nodes[n].Name ?? "")) byName[t.Nodes[n].Name ?? ""] = n;

                int replaced = 0;
                for (int j = 0; j < names.Count; j++)
                    if (byName.TryGetValue(names[j], out int n)) { restWorld[j] = t.BindWorld[n]; replaced++; }

                var missing = new List<string>();
                int mappedHit = 0;
                foreach (var kv in boneJoint)
                {
                    if (byName.ContainsKey(names[kv.Value])) mappedHit++;
                    else missing.Add($"{kv.Key}（{names[kv.Value]}）");
                }
                if (mappedHit == 0)
                { r.Error(MotionFileCodes.FbxInvalid, $"レスト用の FBX に、Humanoid に割り当てた関節の名前が 1 つもありません: {Path.GetFileName(path)}"); return false; }
                if (missing.Count > 0)
                    r.Warning(MotionFileCodes.FbxNotRead,
                        $"レスト用の FBX に無い関節は、読み込むファイル自身のレストを使います: {string.Join(", ", missing)}");
                Debug.Log($"[FbxMotionReader] レストを {Path.GetFileName(path)} のバインド姿勢から取りました（関節 {replaced}/{names.Count}、Humanoid {mappedHit}/{boneJoint.Count}）");
                return true;
            }
        }

        /// <summary>関節名 → Humanoid（HumanoidBoneMapping の埋め込み表。キーは HumanTrait.BoneName）。</summary>
        private static Dictionary<HumanBodyBones, int> AutoMap(List<string> names)
        {
            var mapping = new HumanoidBoneMapping();
            mapping.AutoMapFromEmbeddedCSV(names);
            var byTraitName = new Dictionary<string, HumanBodyBones>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++) byTraitName[HumanTrait.BoneName[i]] = (HumanBodyBones)i;
            var d = new Dictionary<HumanBodyBones, int>();
            foreach (var kv in mapping.BoneIndexMap)
                if (kv.Value >= 0 && kv.Value < names.Count && byTraitName.TryGetValue(kv.Key, out var b)) d[b] = kv.Value;
            return d;
        }

        private static void AddExpressions(FbxScene scene, FbxNodeTable table, MotionClipDTO dto,
                                           float[] weights, int frames, int nCh, float dt)
        {
            var seen = new HashSet<string>();
            for (int c = 0; c < nCh; c++)
            {
                bool any = false;
                for (int f = 0; f < frames && !any; f++) any = weights[(long)f * nCh + c] != 0f;
                if (!any) continue;

                string channel = scene.BlendChannelName(c);
                int node = scene.BlendChannelNode(c);
                string nodeName = node >= 0 ? table.Nodes[node].Name : null;
                string prefix = string.IsNullOrEmpty(nodeName) ? null : nodeName + "_";
                string expr = prefix != null && channel.StartsWith(prefix, StringComparison.Ordinal) && channel.Length > prefix.Length
                    ? channel.Substring(prefix.Length) : channel;
                if (string.IsNullOrEmpty(expr) || !seen.Add(expr)) continue;

                var track = new MotionExpressionTrackDTO { name = expr, provider = "generic" };
                for (int f = 0; f < frames; f++)
                    track.keys.Add(new MotionScalarKeyDTO { t = f * dt, v = weights[(long)f * nCh + c] });
                dto.expressions.Add(track);
            }
        }

        /// <summary>行列の回転（列を正規化して LookRotation。拡大を含んでもよい）。</summary>
        private static Quaternion RotationOf(Matrix4x4 m)
        {
            Vector3 fwd = m.GetColumn(2);
            Vector3 up  = m.GetColumn(1);
            if (fwd.sqrMagnitude < 1e-20f || up.sqrMagnitude < 1e-20f) return Quaternion.identity;
            return Quaternion.LookRotation(fwd.normalized, up.normalized);
        }
    }
}
