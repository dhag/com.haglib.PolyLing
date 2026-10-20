// VrmaCodec.cs
// ============================================================
// PolyLing モーション（MotionClipDTO）→ VRM アニメーション（.vrma = GLB ＋ VRMC_vrm_animation 1.0）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。UniVRM に依存しない（GLB を自前で組む）。
//
// ■ 書き出しの規則（PolyLing_VRMA整備計画.md 2.2）
//   ・レスト骨格は CanonSkeleton（目・あごは載せない）。レストのローカル回転は単位、拡大縮小なし。
//   ・回転チャンネル：bakedBones があればそれ、無ければ muscles＋body を Unity で解いた値（CanonBoneMotion）。
//     トラックの無いボーンは単位。
//   ・Hips の translation ＝ Hips のレスト位置＋正規化差分×レストの腰の高さ。Hips 以外の translation、
//     拡大縮小チャンネルは書かない。
//   ・表情：preset 名は preset、それ以外は custom。lookUp/lookDown/lookLeft/lookRight は書かず数える。
//     重みは [0,1] に丸める（丸めたキーの数を数える）。表情ノードの translation の X が重み。
//   ・視線：lookAt トラックがあればそれ、無く目のマッスルがあれば目の向きから作る（CanonBoneMotion）。
//     lookAt ノードは Head の子に置き、その局所回転を書く。offsetFromHeadBone はクリップの値（無ければ書かない）。
//   ・補間は LINEAR、時刻は秒（区間の開始を 0 に寄せる）。
//   ・glTF ⇔ Unity は X 反転（位置 (-x, y, z)、回転 (x, -y, -z, w)）。
//
// ■ ノードの並び（UniVRM の VrmAnimationImporter で読めるようにするため）
//   [骨格（親から子の順）] [lookAt] [表情…]。表情ノードとその channel は必ず最後に、同じ順で置く。
//   UniVRM は表情の channel を後ろから消しながら、対応するノードを nodes から RemoveAt するので
//   （com.vrmc.vrm/Runtime/IO/VrmAnimationImporter.cs の LoadAsync）、途中にあると索引がずれる。
//   シーンの根は Hips を先頭に置く（同 LoadAsync は scenes[0].nodes の先頭 1 つだけを残す）。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Poly_Ling.Motion
{
    public sealed class VrmaExportOptions
    {
        /// <summary>1 秒あたりの枚数。0 以下ならクリップの frameRate（それも無ければ 30）。</summary>
        public float Fps;
        /// <summary>区間の開始（秒）。</summary>
        public float StartSec;
        /// <summary>区間の終了（秒）。StartSec 以下ならクリップの終端。</summary>
        public float EndSec;
        /// <summary>body（RootT / RootQ）を使うか。false ならレストの body で解く。</summary>
        public bool UseBody = true;
    }

    public sealed class VrmaExportReport
    {
        public string OutputPath;
        public int    FrameCount;
        public float  DurationSec;
        public int    HumanBoneCount;
        /// <summary>回転の出どころ："bakedBones" / "muscles"。</summary>
        public string RotationSource;
        /// <summary>視線の出どころ：null（書かない）/ "lookAt" / "eyeMuscles"。</summary>
        public string LookAtSource;
        public readonly List<string> PresetExpressions  = new List<string>();
        public readonly List<string> CustomExpressions  = new List<string>();
        /// <summary>書かなかった視線系 preset（元のトラック名）。</summary>
        public readonly List<string> DroppedLookExpressions = new List<string>();
        /// <summary>書かなかった表情（名前の重複など）と理由。</summary>
        public readonly List<string> DroppedOther = new List<string>();
        /// <summary>[0,1] に丸めた表情のサンプル数。</summary>
        public int ClampedExpressionSamples;

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append($"{FrameCount} フレーム / {DurationSec:F3} 秒 / Humanoid {HumanBoneCount} 本（{RotationSource}）");
            sb.Append($" / 表情 preset {PresetExpressions.Count}・custom {CustomExpressions.Count}");
            if (DroppedLookExpressions.Count > 0)
                sb.Append($" / 視線の表情を書かず {DroppedLookExpressions.Count} 件（{string.Join(", ", DroppedLookExpressions)}）");
            if (DroppedOther.Count > 0)
                sb.Append($" / ほかに書かなかった表情 {DroppedOther.Count} 件（{string.Join(", ", DroppedOther)}）");
            if (ClampedExpressionSamples > 0)
                sb.Append($" / 重みを [0,1] に丸めた {ClampedExpressionSamples} 件");
            sb.Append(LookAtSource == null ? " / 視線なし" : $" / 視線あり（{LookAtSource}）");
            return sb.ToString();
        }
    }

    public static partial class VrmaCodec
    {
        public const string ExtensionName = "VRMC_vrm_animation";

        // ================================================================
        // Humanoid 名（Unity）⇄ VRM 1.0 の humanBones 名
        // ================================================================

        /// <summary>
        /// HumanBodyBones → VRM 1.0 の humanBones 名。親指だけ 1 段ずれる
        /// （Unity の ThumbProximal/Intermediate/Distal ＝ VRM の thumbMetacarpal/Proximal/Distal）。
        /// </summary>
        public static string VrmBoneName(HumanBodyBones b)
        {
            switch (b)
            {
                case HumanBodyBones.LeftThumbProximal:      return "leftThumbMetacarpal";
                case HumanBodyBones.LeftThumbIntermediate:  return "leftThumbProximal";
                case HumanBodyBones.LeftThumbDistal:        return "leftThumbDistal";
                case HumanBodyBones.RightThumbProximal:     return "rightThumbMetacarpal";
                case HumanBodyBones.RightThumbIntermediate: return "rightThumbProximal";
                case HumanBodyBones.RightThumbDistal:       return "rightThumbDistal";
            }
            string s = b.ToString();
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        // ================================================================
        // 書き出し
        // ================================================================

        /// <summary>.vrma ファイルを書く。失敗時は false と理由（ファイルは書かない）。</summary>
        public static bool WriteFile(MotionClipDTO clip, string path, VrmaExportOptions options,
                                     out VrmaExportReport report, out string reason)
        {
            var glb = Encode(clip, options, out report, out reason);
            if (glb == null) return false;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, glb);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                reason = $"書き込めません: {ex.Message}";
                return false;
            }
            report.OutputPath = path;
            return true;
        }

        /// <summary>GLB のバイト列を作る。失敗時は null と理由。</summary>
        public static byte[] Encode(MotionClipDTO clip, VrmaExportOptions options,
                                    out VrmaExportReport report, out string reason)
        {
            report = new VrmaExportReport();
            reason = null;
            if (clip == null) { reason = "クリップがありません"; return null; }
            options = options ?? new VrmaExportOptions();

            bool hasBaked   = false;
            var  bakedById  = new Dictionary<string, MotionTrackDTO>(StringComparer.Ordinal);
            if (clip.bakedBones != null)
                foreach (var tr in clip.bakedBones)
                    if (tr?.keys != null && tr.keys.Count > 0 && !string.IsNullOrEmpty(tr.id))
                    { bakedById[tr.id] = tr; hasBaked = true; }
            bool hasMuscles = clip.muscles != null && clip.muscles.Count > 0;
            if (!hasBaked && !hasMuscles) { reason = "muscles も bakedBones もありません"; return null; }

            var sampler = hasMuscles ? new MotionMuscleSampler(clip, options.UseBody) : null;
            bool lookFromTrack = clip.lookAt?.keys != null && clip.lookAt.keys.Count > 0;
            bool lookFromEyes  = !lookFromTrack && sampler != null && sampler.HasEyeMuscles;
            bool needRig       = !hasBaked || lookFromEyes;
            report.RotationSource = hasBaked ? "bakedBones" : "muscles";
            report.LookAtSource   = lookFromTrack ? "lookAt" : (lookFromEyes ? "eyeMuscles" : null);

            // ── 時刻 ───────────────────────────────────────────
            float fps   = options.Fps > 0f ? options.Fps : (clip.frameRate > 0f ? clip.frameRate : 30f);
            float len   = MotionClipSerializer.Length(clip);
            float start = Mathf.Max(0f, options.StartSec);
            float end   = options.EndSec > start ? options.EndSec : len;
            if (end < start) end = start;
            int n = Mathf.Max(1, Mathf.RoundToInt((end - start) * fps) + 1);
            var times = new float[n];
            for (int i = 0; i < n; i++) times[i] = Mathf.Min(start + i / fps, end);

            // ── 骨 ────────────────────────────────────────────
            var bones = new List<HumanBodyBones>();
            foreach (var r in CanonSkeleton.Rows) if (CanonSkeleton.IsVrmaBone(r.Bone)) bones.Add(r.Bone);
            var rot  = new Quaternion[bones.Count][];
            for (int b = 0; b < bones.Count; b++) rot[b] = new Quaternion[n];
            var hips = new Vector3[n];
            var look = report.LookAtSource != null ? new Quaternion[n] : null;

            CanonBoneMotion cbm = null;
            try
            {
                if (needRig)
                {
                    cbm = new CanonBoneMotion();
                    if (!cbm.Build(out reason)) return null;
                }
                var buf = new float[HumanTrait.MuscleCount];
                bakedById.TryGetValue(nameof(HumanBodyBones.Hips), out var bakedHips);

                for (int f = 0; f < n; f++)
                {
                    float t = times[f];
                    if (needRig)
                    {
                        sampler.Sample(t, buf, cbm.RestMuscles, out var bp, out var br);
                        cbm.SetMuscles(buf, bp, br);
                    }

                    for (int b = 0; b < bones.Count; b++)
                    {
                        var bone = bones[b];
                        Quaternion q;
                        if (hasBaked)
                            q = bakedById.TryGetValue(bone.ToString(), out var tr)
                                && MotionCurveMath.TryEvaluateRotation(tr, t, out var bq) ? bq : Quaternion.identity;
                        else
                            q = (bone == HumanBodyBones.Hips || sampler.IsDriven(bone))
                                ? cbm.GetLocal(bone) : Quaternion.identity;
                        q = Normalize(q);
                        if (f > 0 && Quaternion.Dot(rot[b][f - 1], q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        rot[b][f] = q;
                    }

                    Vector3 off;
                    if (hasBaked)
                        off = bakedHips != null && MotionCurveMath.TryEvaluateVector3(
                                  bakedHips, MotionCurveMath.VectorChannel.Position, t, out var hp) ? hp : Vector3.zero;
                    else
                        off = cbm.GetHipsOffset();
                    hips[f] = CanonSkeleton.HipsRest + off * CanonSkeleton.HipsHeight;

                    if (look != null)
                    {
                        Quaternion lq;
                        if (lookFromTrack)
                        {
                            if (!MotionCurveMath.TryEvaluateRotation(clip.lookAt, t, out lq)) lq = Quaternion.identity;
                        }
                        else if (!cbm.TryGetLookAt(out lq)) lq = Quaternion.identity;
                        lq = Normalize(lq);
                        if (f > 0 && Quaternion.Dot(look[f - 1], lq) < 0f) lq = new Quaternion(-lq.x, -lq.y, -lq.z, -lq.w);
                        look[f] = lq;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VrmaCodec] {ex}");
                reason = ex.Message;
                return null;
            }
            finally
            {
                cbm?.Dispose();
            }

            // ── 表情 ───────────────────────────────────────────
            var exprs = new List<(string vrmName, bool preset, MotionExpressionTrackDTO track)>();
            var used  = new HashSet<string>(StringComparer.Ordinal);
            if (clip.expressions != null)
                foreach (var tr in clip.expressions)
                {
                    if (tr?.keys == null || tr.keys.Count == 0 || string.IsNullOrEmpty(tr.name)) continue;
                    string vrm;
                    bool isPreset = tr.provider == "vrm1"
                        ? VrmExpressionNames.TryGetPreset(tr.name, out vrm)
                        : VrmExpressionNames.TryResolvePreset(tr.name, null, out vrm);
                    if (!isPreset) vrm = tr.name;
                    if (isPreset && VrmExpressionNames.IsLookPreset(vrm))
                    { report.DroppedLookExpressions.Add(tr.name); continue; }
                    if (used.Contains(vrm))
                    {
                        // preset が既に埋まっていれば元の名前で custom に落とす（.vrm 書き出しと同じ先勝ち）
                        if (isPreset && !used.Contains(tr.name) && !VrmExpressionNames.TryGetPreset(tr.name, out _))
                        { vrm = tr.name; isPreset = false; }
                        else { report.DroppedOther.Add($"{tr.name}（名前の重複）"); continue; }
                    }
                    used.Add(vrm);
                    exprs.Add((vrm, isPreset, tr));
                    (isPreset ? report.PresetExpressions : report.CustomExpressions).Add(vrm);
                }

            // ── GLB ───────────────────────────────────────────
            var gb = new GlbBuilder();
            var nodes = new JArray();
            var nodeOf = new Dictionary<HumanBodyBones, int>();
            var childrenOf = new Dictionary<int, JArray>();
            foreach (var r in CanonSkeleton.Rows)
            {
                if (!CanonSkeleton.IsVrmaBone(r.Bone)) continue;
                int idx = nodes.Count;
                nodeOf[r.Bone] = idx;
                nodes.Add(new JObject
                {
                    ["name"] = r.Bone.ToString(),
                    ["translation"] = Arr(PosToGltf(r.LocalPosition)),
                });
                if (r.HasParent) AddChild(nodes, childrenOf, nodeOf[r.Parent], idx);
            }

            int lookNode = -1;
            if (look != null)
            {
                lookNode = nodes.Count;
                var jo = new JObject { ["name"] = "lookAt" };
                var ofs = clip.lookAtOffsetFromHeadBone;
                if (ofs != null && ofs.Length == 3)
                    jo["translation"] = Arr(PosToGltf(new Vector3(ofs[0], ofs[1], ofs[2])));
                nodes.Add(jo);
                AddChild(nodes, childrenOf, nodeOf[HumanBodyBones.Head], lookNode);
            }

            int firstExprNode = nodes.Count;
            foreach (var e in exprs) nodes.Add(new JObject { ["name"] = e.vrmName });

            // 時刻（入力は 1 本を共有）
            var tt = new float[n];
            for (int i = 0; i < n; i++) tt[i] = times[i] - start;
            int inputAcc = gb.AddFloats(tt, "SCALAR", n, new[] { tt[0] }, new[] { tt[n - 1] });

            var samplers = new JArray();
            var channels = new JArray();
            void Channel(int node, string path, int output)
            {
                channels.Add(new JObject
                {
                    ["sampler"] = samplers.Count,
                    ["target"] = new JObject { ["node"] = node, ["path"] = path },
                });
                samplers.Add(new JObject { ["input"] = inputAcc, ["output"] = output, ["interpolation"] = "LINEAR" });
            }

            for (int b = 0; b < bones.Count; b++)
            {
                var data = new float[n * 4];
                for (int f = 0; f < n; f++) PutRot(data, f, rot[b][f]);
                Channel(nodeOf[bones[b]], "rotation", gb.AddFloats(data, "VEC4", n));
            }
            {
                var data = new float[n * 3];
                for (int f = 0; f < n; f++) { var p = PosToGltf(hips[f]); data[f * 3] = p.x; data[f * 3 + 1] = p.y; data[f * 3 + 2] = p.z; }
                Channel(nodeOf[HumanBodyBones.Hips], "translation", gb.AddFloats(data, "VEC3", n));
            }
            if (look != null)
            {
                var data = new float[n * 4];
                for (int f = 0; f < n; f++) PutRot(data, f, look[f]);
                Channel(lookNode, "rotation", gb.AddFloats(data, "VEC4", n));
            }
            for (int ei = 0; ei < exprs.Count; ei++)
            {
                var data = new float[n * 3];
                for (int f = 0; f < n; f++)
                {
                    float w = MotionCurveMath.EvaluateScalar(exprs[ei].track, times[f]);
                    if (w < 0f || w > 1f) { report.ClampedExpressionSamples++; w = Mathf.Clamp01(w); }
                    data[f * 3] = w;
                }
                Channel(firstExprNode + ei, "translation", gb.AddFloats(data, "VEC3", n));
            }

            // 拡張
            var humanBones = new JObject();
            foreach (var b in bones) humanBones[VrmBoneName(b)] = new JObject { ["node"] = nodeOf[b] };
            var ext = new JObject
            {
                ["specVersion"] = "1.0",
                ["humanoid"] = new JObject { ["humanBones"] = humanBones },
            };
            if (exprs.Count > 0)
            {
                var preset = new JObject();
                var custom = new JObject();
                for (int ei = 0; ei < exprs.Count; ei++)
                    (exprs[ei].preset ? preset : custom)[exprs[ei].vrmName] = new JObject { ["node"] = firstExprNode + ei };
                var ex = new JObject();
                if (preset.Count > 0) ex["preset"] = preset;
                if (custom.Count > 0) ex["custom"] = custom;
                ext["expressions"] = ex;
            }
            if (look != null)
            {
                var la = new JObject { ["node"] = lookNode };
                var ofs = clip.lookAtOffsetFromHeadBone;
                if (ofs != null && ofs.Length == 3)
                    la["offsetFromHeadBone"] = Arr(PosToGltf(new Vector3(ofs[0], ofs[1], ofs[2])));
                ext["lookAt"] = la;
            }

            var sceneNodes = new JArray { nodeOf[HumanBodyBones.Hips] };
            for (int ei = 0; ei < exprs.Count; ei++) sceneNodes.Add(firstExprNode + ei);

            var root = new JObject
            {
                ["asset"] = new JObject { ["version"] = "2.0", ["generator"] = "PolyLing VrmaCodec" },
                ["extensionsUsed"] = new JArray { ExtensionName },
                ["scene"] = 0,
                ["scenes"] = new JArray { new JObject { ["nodes"] = sceneNodes } },
                ["nodes"] = nodes,
                ["animations"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = string.IsNullOrEmpty(clip.name) ? "animation" : clip.name,
                        ["samplers"] = samplers,
                        ["channels"] = channels,
                    },
                },
                ["extensions"] = new JObject { [ExtensionName] = ext },
            };
            gb.WriteInto(root);

            report.FrameCount     = n;
            report.DurationSec    = end - start;
            report.HumanBoneCount = bones.Count;
            return gb.ToGlb(root);
        }

        // ================================================================
        // 補助
        // ================================================================

        private static void AddChild(JArray nodes, Dictionary<int, JArray> childrenOf, int parent, int child)
        {
            if (!childrenOf.TryGetValue(parent, out var arr))
            {
                arr = new JArray();
                childrenOf[parent] = arr;
                ((JObject)nodes[parent])["children"] = arr;
            }
            arr.Add(child);
        }

        /// <summary>Unity 左手系の位置 → glTF（X 反転）。</summary>
        public static Vector3 PosToGltf(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        /// <summary>Unity 左手系の回転 → glTF（X 反転）。</summary>
        public static Quaternion RotToGltf(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        private static void PutRot(float[] data, int f, Quaternion q)
        {
            var g = RotToGltf(q);
            data[f * 4] = g.x; data[f * 4 + 1] = g.y; data[f * 4 + 2] = g.z; data[f * 4 + 3] = g.w;
        }

        private static Quaternion Normalize(Quaternion q)
        {
            float s = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (float.IsNaN(s) || s < 1e-12f) return Quaternion.identity;
            float inv = 1f / Mathf.Sqrt(s);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        private static JArray Arr(Vector3 v) => new JArray { D(v.x), D(v.y), D(v.z) };

        /// <summary>float を JSON 用の double へ（float の誤差の桁を出さない）。</summary>
        internal static double D(float f) => Math.Round((double)f, 6);

        // ================================================================
        // GLB の組み立て
        // ================================================================
        private sealed class GlbBuilder
        {
            private readonly MemoryStream _bin = new MemoryStream();
            private readonly JArray _views     = new JArray();
            private readonly JArray _accessors = new JArray();

            /// <summary>float の並びを 1 つの bufferView・accessor として足す。accessor の索引を返す。</summary>
            public int AddFloats(float[] data, string type, int count, float[] min = null, float[] max = null)
            {
                int offset = (int)_bin.Length;
                var bytes = new byte[data.Length * 4];
                Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
                if (!BitConverter.IsLittleEndian)
                    for (int i = 0; i < bytes.Length; i += 4) Array.Reverse(bytes, i, 4);
                _bin.Write(bytes, 0, bytes.Length);

                _views.Add(new JObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = bytes.Length });
                var acc = new JObject
                {
                    ["bufferView"] = _views.Count - 1,
                    ["componentType"] = 5126,
                    ["count"] = count,
                    ["type"] = type,
                };
                if (min != null) { var a = new JArray(); foreach (var v in min) a.Add(D(v)); acc["min"] = a; }
                if (max != null) { var a = new JArray(); foreach (var v in max) a.Add(D(v)); acc["max"] = a; }
                _accessors.Add(acc);
                return _accessors.Count - 1;
            }

            public void WriteInto(JObject root)
            {
                root["buffers"]     = new JArray { new JObject { ["byteLength"] = (int)_bin.Length } };
                root["bufferViews"] = _views;
                root["accessors"]   = _accessors;
            }

            public byte[] ToGlb(JObject root)
            {
                byte[] json = new UTF8Encoding(false).GetBytes(root.ToString(Formatting.None));
                int jsonPad = (4 - json.Length % 4) % 4;
                byte[] bin = _bin.ToArray();
                int binPad = (4 - bin.Length % 4) % 4;
                int total = 12 + 8 + json.Length + jsonPad + 8 + bin.Length + binPad;

                using (var ms = new MemoryStream(total))
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(0x46546C67u);           // "glTF"
                    w.Write(2u);
                    w.Write((uint)total);
                    w.Write((uint)(json.Length + jsonPad));
                    w.Write(0x4E4F534Au);           // "JSON"
                    w.Write(json);
                    for (int i = 0; i < jsonPad; i++) w.Write((byte)0x20);
                    w.Write((uint)(bin.Length + binPad));
                    w.Write(0x004E4942u);           // "BIN\0"
                    w.Write(bin);
                    for (int i = 0; i < binPad; i++) w.Write((byte)0);
                    w.Flush();
                    return ms.ToArray();
                }
            }
        }
    }
}
