// VrmaCodec.Read.cs
// ============================================================
// VRM アニメーション（.vrma）→ PolyLing モーション（MotionClipDTO）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。UniVRM に依存しない。
//
// ■ 読込の規則（PolyLing_VRMA整備計画.md 2.4）
//   ・最初の animation だけを読む。
//   ・サンプラは LINEAR / STEP / CUBICSPLINE を仕様どおりに評価する。accessor は sparse・正規化整数・
//     byteStride に対応。ノードは TRS と matrix の両方。
//   ・回転：ファイルのレスト階層（任意の回転・拡大縮小を許す）でレストのワールド回転 R を求め、
//     各時刻のワールド回転 W から Δ = W・R⁻¹（レストからのワールド差分）を作る。
//     正準骨格はレストの回転が単位なので、正準ローカル回転 ＝ Δ_親⁻¹・Δ_自分
//     （親は正準階層でファイルにある一番近い祖先）。親子が直に並ぶ普通の場合は
//     「親 Humanoid のレストワールド回転・q・自分のレストワールド回転⁻¹」と同じになる。
//     途中の Humanoid でないノードの回転チャンネルもワールド回転には効かせる。
//   ・Hips の位置：レスト位置からのワールドの差 ÷ レストの腰の高さ（Hips のレストのワールド Y）。
//   ・glTF ⇔ Unity は X 反転（位置 (-x, y, z)、回転 (x, -y, -z, w)）。
//   ・表情：translation の X を [0,1] に丸める。provider は "vrm1"、名前は VRM の表情名。
//   ・視線：lookAt ノードの局所回転を lookAt トラックへ、offsetFromHeadBone をクリップへ。
//     さらに目のマッスルへも反映する（CanonBoneMotion.SolveEyeMuscles）。
//   ・マッスル＋body：正準ローカル回転と Hips を正準骨格へ入れて GetHumanPose（CanonBoneMotion）。
//     GetHumanPose の値は SetHumanPose で回転に戻らない（ねじりが半分になる等）ので、
//     CanonBoneMotion.RefineMuscles で回転を再現する値へ詰める。
//     書くマッスルは、ファイルにあるボーンのもの（目は lookAt があるときだけ）。
//     ファイルに無いボーンのマッスルは書かない（再生では駆動されないボーン＝祖先に付いていく）。
//   ・拡大縮小チャンネル、Hips 以外の translation、weights、未対応の拡張は読まずに件数を報告する。
//
// ■ 時刻
//   使うチャンネルの入力時刻をすべて合わせた格子で評価し、線形のキーにする。
//   STEP は段の直前（1e-4 秒前）にも点を置いて段を保つ。CUBICSPLINE は区間を 4 等分した点も足す。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Poly_Ling.Motion
{
    public static partial class VrmaCodec
    {
        /// <summary>.vrma ファイルを読んで検査する。</summary>
        public static MotionClipLoadResult Load(string path)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                var r = new MotionClipLoadResult();
                r.Error(MotionFileCodes.FileReadFailed, ex.Message);
                return r;
            }
            string name = Path.GetFileNameWithoutExtension(path ?? "");
            return Decode(bytes, name);
        }

        /// <summary>GLB（または埋め込みバッファの .gltf）のバイト列を読む。</summary>
        public static MotionClipLoadResult Decode(byte[] bytes, string name)
        {
            var result = new MotionClipLoadResult();
            try
            {
                var reader = new GltfReader();
                if (!reader.Open(bytes, out string reason)) { result.Error(MotionFileCodes.VrmaInvalid, reason); return result; }
                var dto = reader.ToMotion(name, result);
                if (dto == null) return result;
                MotionClipSerializer.Validate(dto, result);
                if (result.ErrorCount == 0) result.Dto = dto;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VrmaCodec] {ex}");
                result.Error(MotionFileCodes.VrmaInvalid, ex.Message);
            }
            return result;
        }

        private static Dictionary<string, HumanBodyBones> _boneByVrmName;

        /// <summary>VRM 1.0 の humanBones 名 → HumanBodyBones。無ければ false。</summary>
        public static bool TryGetBone(string vrmName, out HumanBodyBones bone)
        {
            if (_boneByVrmName == null)
            {
                var d = new Dictionary<string, HumanBodyBones>(StringComparer.Ordinal);
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                    d[VrmBoneName((HumanBodyBones)i)] = (HumanBodyBones)i;
                _boneByVrmName = d;
            }
            return _boneByVrmName.TryGetValue(vrmName ?? "", out bone);
        }

        /// <summary>glTF の位置 → Unity 左手系（X 反転）。</summary>
        public static Vector3 PosFromGltf(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        /// <summary>glTF の回転 → Unity 左手系（X 反転）。</summary>
        public static Quaternion RotFromGltf(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        // ================================================================
        // glTF の読み手
        // ================================================================
        private sealed class GltfReader
        {
            private JObject _root;
            private readonly List<byte[]> _buffers = new List<byte[]>();

            // ノード（glTF の空間）
            private int          _nodeCount;
            private int[]        _parent;
            private Vector3[]    _restT;
            private Quaternion[] _restR;
            private Vector3[]    _restS;

            private sealed class Sampler
            {
                public float[] Input;
                public float[] Output;   // CUBICSPLINE は (in, value, out) × キー
                public int     Comps;
                public string  Interp;
            }

            public bool Open(byte[] bytes, out string reason)
            {
                reason = null;
                if (bytes == null || bytes.Length < 12) { reason = "ファイルが短すぎます"; return false; }

                byte[] bin = null;
                string json;
                if (BitConverter.ToUInt32(bytes, 0) == 0x46546C67u)
                {
                    uint ver = BitConverter.ToUInt32(bytes, 4);
                    if (ver != 2) { reason = $"GLB の版が 2 ではありません（{ver}）"; return false; }
                    int len = (int)Math.Min(BitConverter.ToUInt32(bytes, 8), (uint)bytes.Length);
                    int o = 12;
                    json = null;
                    while (o + 8 <= len)
                    {
                        int clen = (int)BitConverter.ToUInt32(bytes, o);
                        uint ctype = BitConverter.ToUInt32(bytes, o + 4);
                        o += 8;
                        if (clen < 0 || o + clen > len) { reason = "GLB のチャンクが壊れています"; return false; }
                        if (ctype == 0x4E4F534Au && json == null) json = Encoding.UTF8.GetString(bytes, o, clen);
                        else if (ctype == 0x004E4942u && bin == null) { bin = new byte[clen]; Buffer.BlockCopy(bytes, o, bin, 0, clen); }
                        o += clen;
                    }
                    if (json == null) { reason = "GLB に JSON チャンクがありません"; return false; }
                }
                else
                {
                    json = Encoding.UTF8.GetString(bytes);
                }

                _root = JObject.Parse(json);

                // バッファ
                if (_root["buffers"] is JArray bufs)
                    for (int i = 0; i < bufs.Count; i++)
                    {
                        string uri = bufs[i].Value<string>("uri");
                        if (uri == null)
                        {
                            if (i != 0 || bin == null) { reason = $"buffers[{i}] の中身がありません"; return false; }
                            _buffers.Add(bin);
                        }
                        else if (uri.StartsWith("data:", StringComparison.Ordinal))
                        {
                            int comma = uri.IndexOf(',');
                            if (comma < 0 || !uri.Substring(0, comma).EndsWith(";base64", StringComparison.Ordinal))
                            { reason = $"buffers[{i}] の data URI が base64 ではありません"; return false; }
                            _buffers.Add(Convert.FromBase64String(uri.Substring(comma + 1)));
                        }
                        else { reason = $"buffers[{i}] は外部ファイル（{uri}）なので読めません"; return false; }
                    }

                // ノード
                var nodes = _root["nodes"] as JArray;
                _nodeCount = nodes?.Count ?? 0;
                _parent = new int[_nodeCount];
                _restT = new Vector3[_nodeCount];
                _restR = new Quaternion[_nodeCount];
                _restS = new Vector3[_nodeCount];
                for (int i = 0; i < _nodeCount; i++) _parent[i] = -1;
                for (int i = 0; i < _nodeCount; i++)
                {
                    var n = (JObject)nodes[i];
                    if (n["matrix"] is JArray m && m.Count == 16)
                    {
                        var f = new float[16];
                        for (int k = 0; k < 16; k++) f[k] = m[k].Value<float>();
                        Decompose(f, out _restT[i], out _restR[i], out _restS[i]);
                    }
                    else
                    {
                        _restT[i] = V3(n["translation"], Vector3.zero);
                        _restR[i] = Q(n["rotation"]);
                        _restS[i] = V3(n["scale"], Vector3.one);
                    }
                    if (n["children"] is JArray ch)
                        foreach (var c in ch)
                        {
                            int ci = c.Value<int>();
                            if (ci >= 0 && ci < _nodeCount) _parent[ci] = i;
                        }
                }
                return true;
            }

            // ------------------------------------------------------------
            // DTO へ
            // ------------------------------------------------------------
            public MotionClipDTO ToMotion(string name, MotionClipLoadResult r)
            {
                var ext = _root["extensions"]?[ExtensionName] as JObject;
                if (ext == null) { r.Error(MotionFileCodes.VrmaInvalid, $"{ExtensionName} 拡張がありません"); return null; }
                string spec = ext.Value<string>("specVersion");
                if (spec != "1.0") r.Warning(MotionFileCodes.VrmaNotRead, $"specVersion が \"{spec}\" です（1.0 として読みます）");

                // 未対応の拡張
                var otherExt = new List<string>();
                if (_root["extensionsUsed"] is JArray used)
                    foreach (var e in used) { string s = e.Value<string>(); if (s != ExtensionName) otherExt.Add(s); }
                if (otherExt.Count > 0)
                    r.Warning(MotionFileCodes.VrmaNotRead, $"未対応の拡張 {otherExt.Count} 件を読みません（{string.Join(", ", otherExt)}）");

                // Humanoid
                var boneNode = new Dictionary<HumanBodyBones, int>();
                var ignoredBones = new List<string>();
                if (ext["humanoid"]?["humanBones"] is JObject hb)
                    foreach (var p in hb.Properties())
                    {
                        int node = p.Value?["node"]?.Value<int>() ?? -1;
                        if (node < 0 || node >= _nodeCount) { ignoredBones.Add($"{p.Name}（ノード {node} が無い）"); continue; }
                        if (!TryGetBone(p.Name, out var b)) { ignoredBones.Add($"{p.Name}（名前を知らない）"); continue; }
                        if (!CanonSkeleton.IsVrmaBone(b)) { ignoredBones.Add($"{p.Name}（仕様外か正準骨格に無い）"); continue; }
                        boneNode[b] = node;
                    }
                if (ignoredBones.Count > 0)
                    r.Warning(MotionFileCodes.VrmaNotRead, $"humanBones のうち {ignoredBones.Count} 件を読みません（{string.Join(", ", ignoredBones)}）");
                if (!boneNode.TryGetValue(HumanBodyBones.Hips, out int hipsNode))
                { r.Error(MotionFileCodes.VrmaInvalid, "humanBones に hips がありません"); return null; }

                // 表情
                var exprNode = new List<(string name, int node)>();
                if (ext["expressions"] is JObject ex)
                    foreach (string kind in new[] { "preset", "custom" })
                        if (ex[kind] is JObject eo)
                            foreach (var p in eo.Properties())
                            {
                                int node = p.Value?["node"]?.Value<int>() ?? -1;
                                if (node < 0 || node >= _nodeCount)
                                { r.Warning(MotionFileCodes.VrmaNotRead, $"表情 {p.Name} のノード {node} がありません"); continue; }
                                exprNode.Add((p.Name, node));
                            }

                // 視線
                int lookNode = -1;
                float[] lookOffset = null;
                if (ext["lookAt"] is JObject la)
                {
                    lookNode = la["node"]?.Value<int>() ?? -1;
                    if (lookNode >= _nodeCount) { r.Warning(MotionFileCodes.VrmaNotRead, $"lookAt のノード {lookNode} がありません"); lookNode = -1; }
                    if (la["offsetFromHeadBone"] is JArray ofs && ofs.Count == 3)
                    {
                        var v = PosFromGltf(new Vector3(ofs[0].Value<float>(), ofs[1].Value<float>(), ofs[2].Value<float>()));
                        lookOffset = new[] { v.x, v.y, v.z };
                    }
                }

                // アニメーション
                var anims = _root["animations"] as JArray;
                if (anims == null || anims.Count == 0) { r.Error(MotionFileCodes.VrmaInvalid, "animations がありません"); return null; }
                if (anims.Count > 1) r.Warning(MotionFileCodes.VrmaNotRead, $"animations は最初の 1 つだけ読みます（ほか {anims.Count - 1} 件）");
                var anim = (JObject)anims[0];
                var samplersJ = anim["samplers"] as JArray ?? new JArray();
                var samplers = new Sampler[samplersJ.Count];

                var exprSet = new Dictionary<int, int>();   // ノード → exprNode の索引
                for (int i = 0; i < exprNode.Count; i++) exprSet[exprNode[i].node] = i;

                var rotCh   = new Dictionary<int, Sampler>();
                Sampler hipsT = null;
                var exprCh  = new Dictionary<int, Sampler>();
                int nScale = 0, nTrans = 0, nWeights = 0, nOther = 0;
                if (anim["channels"] is JArray chans)
                    foreach (JObject c in chans)
                    {
                        int si = c.Value<int?>("sampler") ?? -1;
                        int node = c["target"]?["node"]?.Value<int?>() ?? -1;
                        string path = c["target"]?.Value<string>("path");
                        if (si < 0 || si >= samplers.Length || node < 0 || node >= _nodeCount) { nOther++; continue; }
                        switch (path)
                        {
                            case "rotation":
                                rotCh[node] = samplers[si] ?? (samplers[si] = ReadSampler((JObject)samplersJ[si], 4));
                                break;
                            case "translation":
                                if (node == hipsNode) hipsT = samplers[si] ?? (samplers[si] = ReadSampler((JObject)samplersJ[si], 3));
                                else if (exprSet.TryGetValue(node, out int ei)) exprCh[ei] = samplers[si] ?? (samplers[si] = ReadSampler((JObject)samplersJ[si], 3));
                                else nTrans++;
                                break;
                            case "scale":   nScale++;   break;
                            case "weights": nWeights++; break;
                            default:        nOther++;   break;
                        }
                    }
                if (nScale > 0)   r.Warning(MotionFileCodes.VrmaNotRead, $"拡大縮小チャンネル {nScale} 本を読みません");
                if (nTrans > 0)   r.Warning(MotionFileCodes.VrmaNotRead, $"Hips・表情以外の translation チャンネル {nTrans} 本を読みません");
                if (nWeights > 0) r.Warning(MotionFileCodes.VrmaNotRead, $"weights チャンネル {nWeights} 本を読みません");
                if (nOther > 0)   r.Warning(MotionFileCodes.VrmaNotRead, $"読めないチャンネル {nOther} 本を読みません");

                // 回転チャンネルのうち、Humanoid・視線に効くもの（自分か子孫に Humanoid があるノード、lookAt）
                var affects = new bool[_nodeCount];
                foreach (var kv in boneNode)
                    for (int n = kv.Value; n >= 0; n = _parent[n]) affects[n] = true;
                if (lookNode >= 0) affects[lookNode] = true;

                // 時刻の格子
                var timeSet = new List<float>();
                void AddTimes(Sampler s)
                {
                    if (s == null) return;
                    var t = s.Input;
                    for (int k = 0; k < t.Length; k++)
                    {
                        timeSet.Add(t[k]);
                        if (s.Interp == "STEP" && k > 0) timeSet.Add(Mathf.Max(t[k - 1], t[k] - 1e-4f));
                        if (s.Interp == "CUBICSPLINE" && k > 0)
                            for (int q = 1; q < 4; q++) timeSet.Add(Mathf.Lerp(t[k - 1], t[k], q / 4f));
                    }
                }
                foreach (var kv in rotCh) if (affects[kv.Key]) AddTimes(kv.Value);
                AddTimes(hipsT);
                foreach (var kv in exprCh) AddTimes(kv.Value);
                timeSet.Sort();
                var times = new List<float>();
                foreach (var t in timeSet) if (times.Count == 0 || t - times[times.Count - 1] > 1e-6f) times.Add(t);
                if (times.Count == 0) times.Add(0f);
                if (times[0] < 0f)
                {
                    r.Warning(MotionFileCodes.VrmaNotRead, $"時刻が負のキー（最小 {times[0]} 秒）は 0 秒より前なので捨てます");
                    times.RemoveAll(t => t < 0f);
                    if (times.Count == 0) times.Add(0f);
                }
                int nT = times.Count;

                // レストのワールド
                var restW  = WorldRotations(n => _restR[n]);
                var restHipsP = WorldPosition(hipsNode, n => _restT[n], n => _restR[n]);
                float hipsH = restHipsP.y;
                if (hipsH <= 1e-6f)
                {
                    r.Warning(MotionFileCodes.VrmaNotRead, $"Hips のレストの高さが {hipsH} なので、腰の高さを 1 とみなして位置を正規化します");
                    hipsH = 1f;
                }

                // 正準の親（ファイルにある一番近い祖先）
                var canonParent = new Dictionary<HumanBodyBones, HumanBodyBones>();
                var parentOf = new Dictionary<HumanBodyBones, HumanBodyBones>();
                foreach (var row in CanonSkeleton.Rows) if (row.HasParent) parentOf[row.Bone] = row.Parent;
                foreach (var b in boneNode.Keys)
                {
                    var p = b;
                    while (parentOf.TryGetValue(p, out p))
                        if (boneNode.ContainsKey(p)) { canonParent[b] = p; break; }
                }

                // トラックを作る
                var bones = new List<HumanBodyBones>();
                foreach (var row in CanonSkeleton.Rows) if (boneNode.ContainsKey(row.Bone)) bones.Add(row.Bone);
                var baked = new Dictionary<HumanBodyBones, MotionTrackDTO>();
                foreach (var b in bones)
                    baked[b] = new MotionTrackDTO { id = b.ToString(), targetKind = MotionClipDTO.TargetHumanoid };
                var lookTrack = lookNode >= 0 ? new MotionTrackDTO { id = MotionClipDTO.LookAtTrackId, targetKind = MotionClipDTO.TargetLookAt } : null;
                var exprTracks = new List<MotionExpressionTrackDTO>();
                foreach (var e in exprNode) exprTracks.Add(new MotionExpressionTrackDTO { name = e.name, provider = "vrm1" });
                int clamped = 0;

                var localNow = new Quaternion[_nodeCount];
                var hipsOff = new Vector3[nT];
                var lookQ = lookTrack != null ? new Quaternion[nT] : null;
                var localsPerFrame = new Dictionary<HumanBodyBones, Quaternion>[nT];

                for (int f = 0; f < nT; f++)
                {
                    float t = times[f];
                    for (int n = 0; n < _nodeCount; n++)
                        localNow[n] = rotCh.TryGetValue(n, out var s) && (affects[n] || n == lookNode)
                            ? Norm(Q4(Eval(s, t, true))) : _restR[n];
                    var w = WorldRotations(n => localNow[n]);

                    var delta = new Dictionary<HumanBodyBones, Quaternion>();
                    foreach (var b in bones) delta[b] = w[boneNode[b]] * Quaternion.Inverse(restW[boneNode[b]]);
                    var locals = new Dictionary<HumanBodyBones, Quaternion>();
                    foreach (var b in bones)
                    {
                        var q = canonParent.TryGetValue(b, out var p) ? Quaternion.Inverse(delta[p]) * delta[b] : delta[b];
                        q = Norm(RotFromGltf(q));
                        var keys = baked[b].keys;
                        if (keys.Count > 0)
                        {
                            var pr = keys[keys.Count - 1].rot;
                            if (pr[0] * q.x + pr[1] * q.y + pr[2] * q.z + pr[3] * q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        }
                        locals[b] = q;
                        var key = new MotionKeyDTO { t = t, rot = new[] { q.x, q.y, q.z, q.w } };
                        baked[b].keys.Add(key);
                    }
                    localsPerFrame[f] = locals;

                    var hp = WorldPosition(hipsNode,
                        n => n == hipsNode && hipsT != null ? V3f(Eval(hipsT, t, false)) : _restT[n],
                        n => localNow[n]);
                    hipsOff[f] = PosFromGltf((hp - restHipsP) / hipsH);
                    var hk = baked[HumanBodyBones.Hips].keys;
                    hk[hk.Count - 1].pos = new[] { hipsOff[f].x, hipsOff[f].y, hipsOff[f].z };

                    if (lookTrack != null)
                    {
                        var q = Norm(RotFromGltf(localNow[lookNode]));
                        if (f > 0 && Quaternion.Dot(lookQ[f - 1], q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        lookQ[f] = q;
                        lookTrack.keys.Add(new MotionKeyDTO { t = t, rot = new[] { q.x, q.y, q.z, q.w } });
                    }

                    for (int ei = 0; ei < exprNode.Count; ei++)
                    {
                        float v = exprCh.TryGetValue(ei, out var es) ? Eval(es, t, false)[0] : _restT[exprNode[ei].node].x;
                        if (v < 0f || v > 1f) { clamped++; v = Mathf.Clamp01(v); }
                        exprTracks[ei].keys.Add(new MotionScalarKeyDTO { t = t, v = v });
                    }
                }
                if (clamped > 0) r.Warning(MotionFileCodes.VrmaNotRead, $"表情の重みを [0,1] に丸めました（{clamped} 件）");

                // マッスル＋body（Unity に解かせる）
                int mc = HumanTrait.MuscleCount;
                var write = new bool[mc];
                for (int mi = 0; mi < mc; mi++)
                {
                    var b = (HumanBodyBones)HumanTrait.BoneFromMuscle(mi);
                    write[mi] = CanonBoneMotion.IsEyeMuscle(mi) ? lookTrack != null : boneNode.ContainsKey(b);
                }
                var muscleTracks = new MotionScalarTrackDTO[mc];
                var mnames = HumanTrait.MuscleName;
                for (int mi = 0; mi < mc; mi++) if (write[mi]) muscleTracks[mi] = new MotionScalarTrackDTO { name = mnames[mi] };
                var body = new MotionTrackDTO { id = MotionClipDTO.BodyTrackId, targetKind = MotionClipDTO.TargetBody };

                var cbm = new CanonBoneMotion();
                try
                {
                    if (!cbm.Build(out string reason)) { r.Error(MotionFileCodes.VrmaInvalid, reason); return null; }
                    var buf = new float[mc];
                    Quaternion prevQ = Quaternion.identity;
                    for (int f = 0; f < nT; f++)
                    {
                        var locals = localsPerFrame[f];
                        Func<HumanBodyBones, Quaternion?> target = b => locals.TryGetValue(b, out var q) ? q : (Quaternion?)null;
                        cbm.SetLocals(target, hipsOff[f]);
                        cbm.GetMuscles(buf, out var bp, out var br);
                        cbm.RefineMuscles(buf, ref bp, br, target, hipsOff[f]);   // GetHumanPose の値を、回転と Hips を再現する値へ詰める
                        if (lookQ != null) cbm.SolveEyeMuscles(lookQ[f], buf, out _);
                        for (int mi = 0; mi < mc; mi++)
                            if (write[mi]) muscleTracks[mi].keys.Add(new MotionScalarKeyDTO { t = times[f], v = buf[mi] });
                        if (f > 0 && Quaternion.Dot(prevQ, br) < 0f) br = new Quaternion(-br.x, -br.y, -br.z, -br.w);
                        prevQ = br;
                        body.keys.Add(new MotionKeyDTO
                        {
                            t = times[f],
                            pos = new[] { bp.x, bp.y, bp.z },
                            rot = new[] { br.x, br.y, br.z, br.w },
                        });
                    }
                }
                finally { cbm.Dispose(); }

                var dto = new MotionClipDTO
                {
                    name      = string.IsNullOrEmpty(anim.Value<string>("name")) ? name : anim.Value<string>("name"),
                    frameRate = EstimateFps(times),
                    duration  = times[nT - 1],
                    space     = "local",
                    loop      = false,
                    body      = body,
                    lookAt    = lookTrack,
                    lookAtOffsetFromHeadBone = lookOffset,
                    metadata  = new MotionMetadataDTO { createdWith = "PolyLing VrmaCodec (read)" },
                    rotationSource = MotionClipDTO.RotationSourceBakedBones,   // 読んだ回転が正本
                };
                foreach (var m in muscleTracks) if (m != null) dto.muscles.Add(m);
                foreach (var b in bones) dto.bakedBones.Add(baked[b]);
                dto.expressions.AddRange(exprTracks);
                return dto;
            }

            // ------------------------------------------------------------
            // 階層
            // ------------------------------------------------------------
            private Quaternion[] WorldRotations(Func<int, Quaternion> local)
            {
                var w = new Quaternion[_nodeCount];
                var done = new bool[_nodeCount];
                for (int i = 0; i < _nodeCount; i++) WorldRot(i, local, w, done, 0);
                return w;
            }

            private Quaternion WorldRot(int i, Func<int, Quaternion> local, Quaternion[] w, bool[] done, int depth)
            {
                if (done[i]) return w[i];
                int p = _parent[i];
                var q = (p >= 0 && depth < _nodeCount) ? WorldRot(p, local, w, done, depth + 1) * local(i) : local(i);
                w[i] = Norm(q);
                done[i] = true;
                return w[i];
            }

            private Vector3 WorldPosition(int i, Func<int, Vector3> t, Func<int, Quaternion> rot)
            {
                var m = Matrix4x4.TRS(t(i), rot(i), _restS[i]);
                int guard = _nodeCount;
                for (int p = _parent[i]; p >= 0 && guard-- > 0; p = _parent[p])
                    m = Matrix4x4.TRS(t(p), rot(p), _restS[p]) * m;
                return new Vector3(m.m03, m.m13, m.m23);
            }

            // ------------------------------------------------------------
            // サンプラ
            // ------------------------------------------------------------
            private Sampler ReadSampler(JObject sj, int comps)
            {
                var s = new Sampler
                {
                    Input  = ReadAccessor(sj.Value<int>("input"), out _),
                    Output = ReadAccessor(sj.Value<int>("output"), out int oc),
                    Comps  = comps,
                    Interp = sj.Value<string>("interpolation") ?? "LINEAR",
                };
                if (oc != comps) throw new InvalidDataException($"サンプラの出力の成分数が {oc} です（{comps} のはず）");
                int per = s.Interp == "CUBICSPLINE" ? 3 * comps : comps;
                if (s.Output.Length < s.Input.Length * per)
                    throw new InvalidDataException("サンプラの出力がキーの数より少ない");
                return s;
            }

            private static float[] Eval(Sampler s, float t, bool rotation)
            {
                int n = s.Input.Length, c = s.Comps;
                var outv = new float[c];
                bool cubic = s.Interp == "CUBICSPLINE";
                int stride = cubic ? 3 * c : c;
                int vo = cubic ? c : 0;   // 値の位置（in の次）
                if (n == 0) { if (rotation) outv[3] = 1f; return outv; }
                if (n == 1 || t <= s.Input[0]) { Array.Copy(s.Output, vo, outv, 0, c); return outv; }
                if (t >= s.Input[n - 1]) { Array.Copy(s.Output, (n - 1) * stride + vo, outv, 0, c); return outv; }

                int k = Array.BinarySearch(s.Input, t);
                if (k >= 0) { Array.Copy(s.Output, k * stride + vo, outv, 0, c); return outv; }
                k = ~k - 1;   // t は Input[k]～Input[k+1] の間
                float t0 = s.Input[k], t1 = s.Input[k + 1], td = t1 - t0;
                if (td <= 0f) { Array.Copy(s.Output, (k + 1) * stride + vo, outv, 0, c); return outv; }
                float u = (t - t0) / td;

                if (s.Interp == "STEP") { Array.Copy(s.Output, k * stride + vo, outv, 0, c); return outv; }
                if (cubic)
                {
                    float u2 = u * u, u3 = u2 * u;
                    float h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u, h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
                    int a = k * stride, b = (k + 1) * stride;
                    for (int i = 0; i < c; i++)
                    {
                        float v0 = s.Output[a + c + i], b0 = s.Output[a + 2 * c + i];   // 値・出の接線
                        float v1 = s.Output[b + c + i], a1 = s.Output[b + i];           // 値・入の接線
                        outv[i] = h00 * v0 + h10 * td * b0 + h01 * v1 + h11 * td * a1;
                    }
                    return outv;
                }
                // LINEAR
                if (rotation)
                {
                    var q0 = Q4(s.Output, k * stride);
                    var q1 = Q4(s.Output, (k + 1) * stride);
                    var q = Quaternion.Slerp(q0, q1, u);
                    outv[0] = q.x; outv[1] = q.y; outv[2] = q.z; outv[3] = q.w;
                    return outv;
                }
                for (int i = 0; i < c; i++)
                    outv[i] = Mathf.Lerp(s.Output[k * stride + i], s.Output[(k + 1) * stride + i], u);
                return outv;
            }

            // ------------------------------------------------------------
            // accessor（sparse・正規化整数・byteStride）
            // ------------------------------------------------------------
            private float[] ReadAccessor(int index, out int comps)
            {
                var acc = (JObject)((JArray)_root["accessors"])[index];
                int count = acc.Value<int>("count");
                comps = CompsOf(acc.Value<string>("type"));
                int ctype = acc.Value<int>("componentType");
                bool normalized = acc.Value<bool?>("normalized") ?? false;
                var data = new float[count * comps];

                if (acc["bufferView"] != null)
                    ReadView(acc.Value<int>("bufferView"), acc.Value<int?>("byteOffset") ?? 0,
                             count, comps, ctype, normalized, data, null);

                if (acc["sparse"] is JObject sp)
                {
                    int sc = sp.Value<int>("count");
                    var idx = sp["indices"];
                    var idxBuf = new float[sc];
                    ReadView(idx.Value<int>("bufferView"), idx.Value<int?>("byteOffset") ?? 0,
                             sc, 1, idx.Value<int>("componentType"), false, idxBuf, null);
                    var val = sp["values"];
                    var valBuf = new float[sc * comps];
                    ReadView(val.Value<int>("bufferView"), val.Value<int?>("byteOffset") ?? 0,
                             sc, comps, ctype, normalized, valBuf, null);
                    for (int i = 0; i < sc; i++)
                    {
                        int at = (int)idxBuf[i];
                        if (at < 0 || at >= count) continue;
                        Array.Copy(valBuf, i * comps, data, at * comps, comps);
                    }
                }
                return data;
            }

            private void ReadView(int viewIndex, int accOffset, int count, int comps, int ctype, bool normalized,
                                  float[] into, object _)
            {
                var view = (JObject)((JArray)_root["bufferViews"])[viewIndex];
                var buf = _buffers[view.Value<int>("buffer")];
                int baseOff = (view.Value<int?>("byteOffset") ?? 0) + accOffset;
                int csize = ctype == 5126 || ctype == 5125 ? 4 : (ctype == 5122 || ctype == 5123 ? 2 : 1);
                int stride = view.Value<int?>("byteStride") ?? 0;
                if (stride <= 0) stride = csize * comps;
                for (int i = 0; i < count; i++)
                    for (int c = 0; c < comps; c++)
                    {
                        int o = baseOff + i * stride + c * csize;
                        float v;
                        switch (ctype)
                        {
                            case 5126: v = BitConverter.ToSingle(buf, o); break;
                            case 5125: v = BitConverter.ToUInt32(buf, o); break;
                            case 5123: { ushort x = BitConverter.ToUInt16(buf, o); v = normalized ? x / 65535f : x; break; }
                            case 5122: { short x = BitConverter.ToInt16(buf, o);   v = normalized ? Mathf.Max(x / 32767f, -1f) : x; break; }
                            case 5121: { byte x = buf[o];                          v = normalized ? x / 255f : x; break; }
                            case 5120: { sbyte x = (sbyte)buf[o];                  v = normalized ? Mathf.Max(x / 127f, -1f) : x; break; }
                            default: throw new InvalidDataException($"componentType {ctype} は読めません");
                        }
                        into[i * comps + c] = v;
                    }
            }

            private static int CompsOf(string type)
            {
                switch (type)
                {
                    case "SCALAR": return 1;
                    case "VEC2":   return 2;
                    case "VEC3":   return 3;
                    case "VEC4":   return 4;
                    case "MAT4":   return 16;
                    default: throw new InvalidDataException($"accessor の type \"{type}\" は読めません");
                }
            }

            // ------------------------------------------------------------
            // 小物
            // ------------------------------------------------------------
            private static void Decompose(float[] m, out Vector3 t, out Quaternion r, out Vector3 s)
            {
                // glTF の matrix は列優先
                var c0 = new Vector3(m[0], m[1], m[2]);
                var c1 = new Vector3(m[4], m[5], m[6]);
                var c2 = new Vector3(m[8], m[9], m[10]);
                t = new Vector3(m[12], m[13], m[14]);
                float sx = c0.magnitude, sy = c1.magnitude, sz = c2.magnitude;
                if (Vector3.Dot(Vector3.Cross(c0, c1), c2) < 0f) sx = -sx;
                s = new Vector3(sx, sy, sz);
                if (Mathf.Abs(sx) < 1e-12f || sy < 1e-12f || sz < 1e-12f) { r = Quaternion.identity; return; }
                r = Quaternion.LookRotation(c2 / sz, c1 / sy);
            }

            private static Vector3 V3(JToken tok, Vector3 fallback)
                => tok is JArray a && a.Count == 3
                    ? new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>()) : fallback;

            private static Quaternion Q(JToken tok)
                => tok is JArray a && a.Count == 4
                    ? Norm(new Quaternion(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>(), a[3].Value<float>()))
                    : Quaternion.identity;

            private static Vector3 V3f(float[] v) => new Vector3(v[0], v[1], v[2]);
            private static Quaternion Q4(float[] v) => new Quaternion(v[0], v[1], v[2], v[3]);
            private static Quaternion Q4(float[] v, int o) => new Quaternion(v[o], v[o + 1], v[o + 2], v[o + 3]);

            private static Quaternion Norm(Quaternion q)
            {
                float sq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                if (float.IsNaN(sq) || sq < 1e-12f) return Quaternion.identity;
                float inv = 1f / Mathf.Sqrt(sq);
                return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
            }

            private static float EstimateFps(List<float> times)
            {
                if (times.Count < 2) return 30f;
                var d = new List<float>();
                for (int i = 1; i < times.Count; i++) d.Add(times[i] - times[i - 1]);
                d.Sort();
                float med = d[d.Count / 2];
                if (med <= 1e-5f) return 30f;
                return Mathf.Clamp(Mathf.Round(1f / med), 1f, 240f);
            }
        }
    }
}
