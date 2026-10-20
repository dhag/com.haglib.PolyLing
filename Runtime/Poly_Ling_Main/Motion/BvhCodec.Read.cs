// BvhCodec.Read.cs
// ============================================================
// BVH → PolyLing モーション（MotionClipDTO）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。規則の正本は PolyLing_BVH対応計画.md 2～4。
//
// ■ 流れ
//   1. BvhDocument で構文を読む。
//   2. 関節名を Humanoid に割り当てる（BVH 専用の表を完全一致で引く。CSV で上書き）。
//   3〜5 は CanonMotionBuilder（FBX のモーション読み込みと共通。2026-10-14 に本ファイルから切り出した）。
//   3. 各フレームのワールド回転 W から、ワールドの差分 Δ = W·R⁻¹·A を作る
//      （R はレストのワールド回転＝単位、A は T ポーズ整列。BVH は常に腕 8 本＋脚 4 本。
//      範囲と算出は UnityClipApplier.IsAlignBone / CanonAlignment が正本）。
//      割り当てのない中間の関節の回転もワールド回転には効く。
//   4. 正準ローカル回転 = Δ_親⁻¹·Δ_自分（親は正準階層で割り当てのある一番近い祖先）と、
//      腰の正規化位置（レストを床 Y = 0 に置いたときの Hips 位置からの差 ÷ 腰の高さ）を bakedBones に入れる。
//   5. CanonBoneMotion.BakeMuscles でマッスル＋body に焼く（.plmotion の bakedBones 読込と同じ経路）。
//
// ■ 座標
//   BVH は右手系・Y 上・+Z 正面（キャラクターの左が +X）。glTF と同じなので
//   VrmaCodec.PosFromGltf / RotFromGltf（X 反転）で Unity 左手系へ移す。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UnityClip;

namespace Poly_Ling.Motion
{
    /// <summary>BVH の読込の設定。</summary>
    public sealed class BvhReadOptions
    {
        /// <summary>関節名の割り当て表。</summary>
        public BvhSkeletonKind Skeleton = BvhSkeletonKind.Auto;

        /// <summary>割り当て CSV の行（1 行目は見出し）。null なら使わない。</summary>
        public IList<string> MappingCsvLines;
    }

    public static partial class BvhCodec
    {
        // ================================================================
        // 割り当て表（PolyLing_BVH対応計画.md 3）
        // ================================================================

        // 「Humanoid 名|関節名」。左右のある行は {S}（Left / Right）と {s}（関節名の左右の印）を置き換える。
        private static readonly string[] MocopiRows =
        {
            "Hips|root", "Spine|torso_3", "Chest|torso_5", "UpperChest|torso_6", "Neck|neck_1", "Head|head",
            "{S}Shoulder|{s}_shoulder", "{S}UpperArm|{s}_up_arm", "{S}LowerArm|{s}_low_arm", "{S}Hand|{s}_hand",
            "{S}UpperLeg|{s}_up_leg", "{S}LowerLeg|{s}_low_leg", "{S}Foot|{s}_foot", "{S}Toes|{s}_toes",
        };

        private static readonly string[] CmuRows =
        {
            "Hips|Hips", "Spine|LowerBack", "Chest|Spine", "UpperChest|Spine1", "Neck|Neck1", "Head|Head",
            "{S}Shoulder|{s}Shoulder", "{S}UpperArm|{s}Arm", "{S}LowerArm|{s}ForeArm", "{S}Hand|{s}Hand",
            "{S}UpperLeg|{s}UpLeg", "{S}LowerLeg|{s}Leg", "{S}Foot|{s}Foot", "{S}Toes|{s}ToeBase",
        };

        private static Dictionary<HumanBodyBones, string> Table(BvhSkeletonKind kind)
        {
            var rows = kind == BvhSkeletonKind.Mocopi ? MocopiRows : CmuRows;
            var mark = kind == BvhSkeletonKind.Mocopi ? new[] { "l", "r" } : new[] { "Left", "Right" };
            var side = new[] { "Left", "Right" };
            var d = new Dictionary<HumanBodyBones, string>();
            foreach (var row in rows)
            {
                var col = row.Split('|');
                int n = col[0].Contains("{S}") ? 2 : 1;
                for (int k = 0; k < n; k++)
                {
                    string h = col[0].Replace("{S}", side[k]);
                    string j = col[1].Replace("{s}", mark[k]);
                    d[(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), h)] = j;
                }
            }
            return d;
        }

        // ================================================================
        // 読込
        // ================================================================

        /// <summary>.bvh ファイルを読んで検査する。</summary>
        public static MotionClipLoadResult Load(string path, BvhReadOptions options)
        {
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                var r = new MotionClipLoadResult();
                r.Error(MotionFileCodes.FileReadFailed, ex.Message);
                return r;
            }
            return Decode(text, Path.GetFileNameWithoutExtension(path ?? ""), options);
        }

        /// <summary>BVH の文字列を読む。</summary>
        public static MotionClipLoadResult Decode(string text, string name, BvhReadOptions options)
        {
            var result = new MotionClipLoadResult();
            options = options ?? new BvhReadOptions();
            try
            {
                var doc = BvhDocument.Parse(text, out string reason);
                if (doc == null) { result.Error(MotionFileCodes.BvhInvalid, reason); return result; }
                foreach (var w in doc.Warnings) result.Warning(MotionFileCodes.BvhNotRead, w);
                var dto = ToMotion(doc, name, options, result);
                if (dto == null) return result;
                MotionClipSerializer.Validate(dto, result);
                if (result.ErrorCount == 0) result.Dto = dto;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BvhCodec] {ex}");
                result.Error(MotionFileCodes.BvhInvalid, ex.Message);
            }
            return result;
        }

        private static MotionClipDTO ToMotion(BvhDocument doc, string name, BvhReadOptions opt, MotionClipLoadResult r)
        {
            if (doc.FrameCount <= 0) { r.Error(MotionFileCodes.BvhInvalid, "フレームがありません"); return null; }
            int nJ = doc.Joints.Count;

            // ── 割り当て ───────────────────────────────────────────
            var boneJoint = ResolveMapping(doc, opt, r, out string tableName);
            if (boneJoint == null) return null;
            if (!boneJoint.ContainsKey(HumanBodyBones.Hips))
            { r.Error(MotionFileCodes.BvhInvalid, "Hips に当たる関節がありません（割り当て CSV で指定してください）"); return null; }

            // ── レスト（ファイルの座標）。BVH のレストのワールド回転は全部単位 ─────
            var restP = new Vector3[nJ];
            for (int j = 0; j < nJ; j++)
            {
                var jt = doc.Joints[j];
                restP[j] = (jt.Parent >= 0 ? restP[jt.Parent] : Vector3.zero) + jt.Offset;
            }
            float groundY = float.PositiveInfinity;
            for (int j = 0; j < nJ; j++) groundY = Mathf.Min(groundY, restP[j].y);

            float dt = doc.FrameTime;
            var src = new CanonMotionSource
            {
                Name        = name,
                FrameRate   = FrameRateOf(dt),
                FrameTime   = dt,
                FrameCount  = doc.FrameCount,
                JointCount  = nJ,
                RestPos     = restP,
                RestRot     = null,
                GroundY     = groundY,
                BoneJoint   = boneJoint,
                CreatedWith = $"PolyLing BvhCodec (read, {tableName})",
                ErrorCode   = MotionFileCodes.BvhInvalid,
                SampleWorld = (f, worldR, worldP) =>
                {
                    int o = f * doc.ChannelCount;
                    for (int j = 0; j < nJ; j++)
                    {
                        var jt = doc.Joints[j];
                        Quaternion lr = Quaternion.identity;
                        Vector3 lt = jt.Offset;
                        bool isRoot = jt.Parent < 0;
                        for (int c = 0; c < jt.Channels.Length; c++)
                        {
                            float v = doc.Values[o + jt.ChannelStart + c];
                            switch (jt.Channels[c])
                            {
                                case BvhChannel.Xrotation: lr *= Quaternion.AngleAxis(v, Vector3.right);   break;
                                case BvhChannel.Yrotation: lr *= Quaternion.AngleAxis(v, Vector3.up);      break;
                                case BvhChannel.Zrotation: lr *= Quaternion.AngleAxis(v, Vector3.forward); break;
                                case BvhChannel.Xposition: if (isRoot) lt.x = v; break;
                                case BvhChannel.Yposition: if (isRoot) lt.y = v; break;
                                case BvhChannel.Zposition: if (isRoot) lt.z = v; break;
                            }
                        }
                        if (isRoot) { worldR[j] = lr; worldP[j] = lt; }
                        else
                        {
                            worldP[j] = worldP[jt.Parent] + worldR[jt.Parent] * lt;
                            worldR[j] = worldR[jt.Parent] * lr;
                        }
                    }
                },
            };

            var dto = CanonMotionBuilder.Build(src, r, out float hipsH);
            if (dto == null) return null;

            // 根以外の位置チャンネルは読まない。OFFSET と食い違う関節を数える。
            CountIgnoredPositions(doc, hipsH, r);
            return dto;
        }

        // 割り当て：表（Auto は一致数が最大の表）→ CSV で上書き・追加。関節名は完全一致。
        private static Dictionary<HumanBodyBones, int> ResolveMapping(
            BvhDocument doc, BvhReadOptions opt, MotionClipLoadResult r, out string tableName)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < doc.Joints.Count; j++)
                if (!doc.Joints[j].IsEndSite && !index.ContainsKey(doc.Joints[j].Name)) index[doc.Joints[j].Name] = j;

            var kinds = opt.Skeleton == BvhSkeletonKind.Auto
                ? new[] { BvhSkeletonKind.Mocopi, BvhSkeletonKind.Cmu }
                : new[] { opt.Skeleton };
            Dictionary<HumanBodyBones, int> best = null;
            BvhSkeletonKind bestKind = kinds[0];
            foreach (var kind in kinds)
            {
                var m = new Dictionary<HumanBodyBones, int>();
                foreach (var kv in Table(kind))
                    if (index.TryGetValue(kv.Value, out int j)) m[kv.Key] = j;
                if (best == null || m.Count > best.Count) { best = m; bestKind = kind; }
            }
            tableName = best.Count > 0 ? bestKind.ToString() : "表なし";

            if (opt.MappingCsvLines != null)
            {
                var bad = new List<string>();
                int applied = CanonMotionBuilder.ApplyMappingCsv(best, opt.MappingCsvLines, index, bad);
                tableName += $" + CSV {applied} 行";
                if (bad.Count > 0)
                    r.Warning(MotionFileCodes.BvhNotRead, $"割り当て CSV のうち {bad.Count} 行を使いません（{string.Join(", ", bad)}）");
            }

            // 正準骨格に無い・.vrma に載らないボーン（目・あご・指など表に無いもの）は使わず、主要なボーンの欠けを警告する
            CanonMotionBuilder.FilterAndCheck(best, tableName, MotionFileCodes.BvhNotRead, r);
            return best;
        }

        // 根以外の位置チャンネルで、値が OFFSET から外れる関節の数を警告する（値は読まない）。
        private static void CountIgnoredPositions(BvhDocument doc, float hipsH, MotionClipLoadResult r)
        {
            float tol = hipsH * 1e-4f;
            var names = new List<string>();
            foreach (var jt in doc.Joints)
            {
                if (jt.Parent < 0 || !jt.HasPosition) continue;
                bool differs = false;
                for (int f = 0; f < doc.FrameCount && !differs; f++)
                {
                    int o = f * doc.ChannelCount + jt.ChannelStart;
                    for (int c = 0; c < jt.Channels.Length && !differs; c++)
                    {
                        float v = doc.Values[o + c];
                        switch (jt.Channels[c])
                        {
                            case BvhChannel.Xposition: differs = Mathf.Abs(v - jt.Offset.x) > tol; break;
                            case BvhChannel.Yposition: differs = Mathf.Abs(v - jt.Offset.y) > tol; break;
                            case BvhChannel.Zposition: differs = Mathf.Abs(v - jt.Offset.z) > tol; break;
                        }
                    }
                }
                if (differs) names.Add(jt.Name);
            }
            if (names.Count > 0)
                r.Warning(MotionFileCodes.BvhNotRead,
                    $"根以外の位置チャンネルは読みません。値が OFFSET と違う関節 {names.Count} 個: {string.Join(", ", names)}");
        }

        // 1/FrameTime が整数に近ければ整数にする（0.016667 → 60）。
        private static float FrameRateOf(float frameTime)
        {
            float fps = 1f / frameTime;
            float rounded = Mathf.Round(fps);
            return Mathf.Abs(fps - rounded) < 1e-2f ? rounded : fps;
        }

        private static Quaternion Norm(Quaternion q)
        {
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return n > 1e-8f ? new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n) : Quaternion.identity;
        }
    }
}
