// CanonMotionBuilder.cs
// ============================================================
// 関節のワールド姿勢の列 → PolyLing モーション（正準ローカル回転＋腰の正規化位置 → マッスル＋body）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。BVH 読み込み（BvhCodec.Read.cs）と FBX のモーション読み込み
// （FbxMotionReader.cs）が共通に使う。2026-10-14 に BvhCodec.ToMotion から切り出した。
// 規則の正本は PolyLing_BVH対応計画.md 2～4 と PolyLing_FBX対応計画.md 段 5。
//
// ■ 入力（ファイルの座標：右手系・Y 上・+Z 正面。Unity へは VrmaCodec.PosFromGltf / RotFromGltf の X 反転）
//   ・レストの関節のワールド位置 RestPos と ワールド回転 RestRot（null なら全部単位＝BVH）
//   ・床の高さ GroundY（レストの最低の点）
//   ・Humanoid → 関節の割り当て
//   ・フレームごとの関節のワールド回転 W とワールド位置 P（SampleWorld）
//
// ■ 流れ
//   1. T ポーズ整列 A（腕 8 本＋脚 4 本。UnityClipApplier.CanonAlignment が正本）をレストの位置から求める。
//   2. 各フレームで Δ = (W·R⁻¹)·A（R はレストのワールド回転）。
//   3. 正準ローカル回転 = Δ_親⁻¹·Δ_自分（親は正準階層で割り当てのある一番近い祖先）と、
//      腰の正規化位置（レストを床 Y = 0 に置いたときの Hips 位置からの差 ÷ 腰の高さ）を bakedBones に入れる。
//   4. CanonBoneMotion.BakeMuscles でマッスル＋body に焼く（.plmotion の bakedBones 読込と同じ経路）。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.UnityClip;

namespace Poly_Ling.Motion
{
    /// <summary>CanonMotionBuilder.Build の入力。</summary>
    public sealed class CanonMotionSource
    {
        public string Name;
        public float  FrameRate;
        public float  FrameTime;
        public int    FrameCount;
        public int    JointCount;

        /// <summary>レストの関節のワールド位置（ファイルの座標）。</summary>
        public Vector3[]    RestPos;
        /// <summary>レストの関節のワールド回転（ファイルの座標）。null なら全部単位。</summary>
        public Quaternion[] RestRot;
        /// <summary>床の高さ（レストの最低の点の Y）。</summary>
        public float        GroundY;

        /// <summary>Humanoid → 関節索引。Hips を含むこと。</summary>
        public Dictionary<HumanBodyBones, int> BoneJoint;

        /// <summary>フレーム f の関節のワールド回転・ワールド位置（ファイルの座標）を配列へ書く。</summary>
        public Action<int, Quaternion[], Vector3[]> SampleWorld;

        public string CreatedWith;
        /// <summary>失敗のときの理由コード（MotionFileCodes）。</summary>
        public string ErrorCode;
    }

    public static class CanonMotionBuilder
    {
        /// <summary>作れないときは null（理由は r）。hipsHeight はレストの腰の高さ（ファイルの単位）。</summary>
        public static MotionClipDTO Build(CanonMotionSource src, MotionClipLoadResult r, out float hipsHeight)
        {
            hipsHeight = 0f;
            int hipsJ = src.BoneJoint[HumanBodyBones.Hips];
            float hipsH = src.RestPos[hipsJ].y - src.GroundY;
            hipsHeight = hipsH;
            if (hipsH <= 1e-6f)
            { r.Error(src.ErrorCode, $"レストの腰の高さが {hipsH} です（床はレストの最低の点）"); return null; }
            Vector3 hipsBase = src.RestPos[hipsJ] - new Vector3(0f, src.GroundY, 0f);

            // ── T ポーズ整列（腕 8 本＋脚 4 本。算出は UnityClipApplier が正本）─────
            var restUnity = new Dictionary<string, Vector3>();
            foreach (var kv in src.BoneJoint) restUnity[kv.Key.ToString()] = VrmaCodec.PosFromGltf(src.RestPos[kv.Value]);
            var align = new Dictionary<HumanBodyBones, Quaternion>();
            foreach (var kv in src.BoneJoint)
                align[kv.Key] = UnityClipApplier.CanonAlignment(restUnity, kv.Key.ToString(), includeLegs: true);

            // ── レストの回転の逆（R⁻¹）。RestRot が null なら掛けない ─────
            Dictionary<HumanBodyBones, Quaternion> restInv = null;
            if (src.RestRot != null)
            {
                restInv = new Dictionary<HumanBodyBones, Quaternion>();
                foreach (var kv in src.BoneJoint) restInv[kv.Key] = Quaternion.Inverse(src.RestRot[kv.Value]);
            }

            // ── 正準の親（割り当てのある一番近い祖先）───────────────
            var parentOf = new Dictionary<HumanBodyBones, HumanBodyBones>();
            foreach (var row in CanonSkeleton.Rows) if (row.HasParent) parentOf[row.Bone] = row.Parent;
            var canonParent = new Dictionary<HumanBodyBones, HumanBodyBones>();
            foreach (var b in src.BoneJoint.Keys)
            {
                var p = b;
                while (parentOf.TryGetValue(p, out p))
                    if (src.BoneJoint.ContainsKey(p)) { canonParent[b] = p; break; }
            }

            var bones = new List<HumanBodyBones>();
            foreach (var row in CanonSkeleton.Rows) if (src.BoneJoint.ContainsKey(row.Bone)) bones.Add(row.Bone);
            var baked = new Dictionary<HumanBodyBones, MotionTrackDTO>();
            foreach (var b in bones)
                baked[b] = new MotionTrackDTO { id = b.ToString(), targetKind = MotionClipDTO.TargetHumanoid };

            // ── フレーム ────────────────────────────────────────
            var worldR = new Quaternion[src.JointCount];
            var worldP = new Vector3[src.JointCount];
            var delta  = new Dictionary<HumanBodyBones, Quaternion>();
            for (int f = 0; f < src.FrameCount; f++)
            {
                float t = f * src.FrameTime;
                src.SampleWorld(f, worldR, worldP);

                // Δ = (W·R⁻¹)·A
                foreach (var b in bones)
                {
                    var w = worldR[src.BoneJoint[b]];
                    if (restInv != null) w = w * restInv[b];
                    delta[b] = VrmaCodec.RotFromGltf(w) * align[b];
                }
                foreach (var b in bones)
                {
                    var q = canonParent.TryGetValue(b, out var p) ? Quaternion.Inverse(delta[p]) * delta[b] : delta[b];
                    q = Norm(q);
                    var keys = baked[b].keys;
                    if (keys.Count > 0)
                    {
                        var pr = keys[keys.Count - 1].rot;
                        if (pr[0] * q.x + pr[1] * q.y + pr[2] * q.z + pr[3] * q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    }
                    var key = new MotionKeyDTO { t = t, rot = new[] { q.x, q.y, q.z, q.w } };
                    if (b == HumanBodyBones.Hips)
                    {
                        var off = VrmaCodec.PosFromGltf((worldP[hipsJ] - hipsBase) / hipsH);
                        key.pos = new[] { off.x, off.y, off.z };
                    }
                    keys.Add(key);
                }
            }

            var dto = new MotionClipDTO
            {
                name      = src.Name,
                frameRate = src.FrameRate,
                duration  = (src.FrameCount - 1) * src.FrameTime,
                space     = "local",
                loop      = false,
                metadata  = new MotionMetadataDTO { createdWith = src.CreatedWith },
                rotationSource = MotionClipDTO.RotationSourceBakedBones,   // 読んだ回転が正本
            };
            foreach (var b in bones) dto.bakedBones.Add(baked[b]);

            // マッスル＋body（.plmotion の bakedBones 読込と同じ経路）
            var ignored = new List<string>();
            if (!CanonBoneMotion.BakeMuscles(dto, ignored, out bool _, out string bakeReason))
            { r.Error(src.ErrorCode, bakeReason); return null; }
            if (ignored.Count > 0)
                r.Warning(MotionFileCodes.UnknownBone, $"正準骨格に無いボーンは使いません: {string.Join(", ", ignored)}");
            return dto;
        }

        /// <summary>
        /// 正準骨格で扱えない（.vrma に載らない）ボーンの割り当てを外し、体の主要なボーンの欠けを警告する。
        /// BVH・FBX の読み込みで共通。
        /// </summary>
        public static void FilterAndCheck(Dictionary<HumanBodyBones, int> boneJoint, string tableName,
                                          string warnCode, MotionClipLoadResult r)
        {
            var inSkeleton = new HashSet<HumanBodyBones>();
            foreach (var row in CanonSkeleton.Rows) if (CanonSkeleton.IsVrmaBone(row.Bone)) inSkeleton.Add(row.Bone);
            var dropped = new List<string>();
            foreach (var b in new List<HumanBodyBones>(boneJoint.Keys))
                if (!inSkeleton.Contains(b)) { boneJoint.Remove(b); dropped.Add(b.ToString()); }
            if (dropped.Count > 0)
                r.Warning(warnCode, $"正準骨格で扱えないボーンの割り当てを使いません: {string.Join(", ", dropped)}");

            var missing = new List<string>();
            foreach (var b in new[]
            {
                HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
            })
                if (!boneJoint.ContainsKey(b)) missing.Add(b.ToString());
            if (missing.Count > 0)
                r.Warning(warnCode, $"割り当ての無い主要ボーン（{tableName}）: {string.Join(", ", missing)}");
        }

        /// <summary>
        /// 割り当て CSV（1 行目は見出し、各行「Humanoid 名,関節名[,関節名…]」）で上書き・追加する。
        /// 戻り値は使った行数。使えない行は bad へ。
        /// </summary>
        public static int ApplyMappingCsv(Dictionary<HumanBodyBones, int> boneJoint, IList<string> lines,
                                          IReadOnlyDictionary<string, int> jointIndex, List<string> bad)
        {
            int applied = 0;
            bool header = true;
            foreach (var raw in lines)
            {
                if (header) { header = false; continue; }
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;
                var col = line.Split(',');
                string h = col[0].Trim().Replace(" ", "");
                if (!Enum.TryParse(h, out HumanBodyBones b) || b == HumanBodyBones.LastBone)
                { bad.Add($"{h}（Humanoid 名でない）"); continue; }
                int found = -1;
                for (int k = 1; k < col.Length && found < 0; k++)
                    if (jointIndex.TryGetValue(col[k].Trim(), out int j)) found = j;
                if (found < 0) { bad.Add($"{h}（関節が無い）"); continue; }
                boneJoint[b] = found;
                applied++;
            }
            return applied;
        }

        private static Quaternion Norm(Quaternion q)
        {
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return n > 1e-8f ? new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n) : Quaternion.identity;
        }
    }
}
