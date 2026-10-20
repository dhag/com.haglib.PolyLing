// BvhCodec.Export.cs
// ============================================================
// モーション＋モデル → BVH（モデルの骨格）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。規則の正本は PolyLing_BVH対応計画.md 5。
//
// ■ 流れ
//   1〜4 は ModelMotionSampler（FBX のモーション書き出しと共通。2026-10-14 に本ファイルから切り出した）。
//   1. MotionClipApplier でモーションをモデルへ当てる（パネル再生と同じ経路）。
//   2. 骨格は UnityClipVirtualSkeleton のノード（半身モデルの仮想ミラーを含む）から IK ボーンを除いたもの。
//      親は IK を飛ばした一番近い祖先。根が複数なら原点に PolyLingRoot を置いてまとめる（ここで足す）。
//   3. 再生経路は Hips から見た姿勢しか当てない（body を当てない。UnityClipApplier.cs の恒久メモ）。
//      マッスルを持つクリップは、CanonBoneMotion で body から Hips の回転 Qc と正規化位置を求め、
//      全関節に剛体変換 T = 移動(Hips のレスト位置 + 正規化位置 × 腰の高さ)·回転 Qc·移動(−Hips のレスト位置) を掛ける。
//      PolyLingRoot の姿勢は T そのもの。
//   4. 各関節：G = 回転(T·W)·回転(レストのワールド)⁻¹、ローカル回転 = G_親⁻¹·G、ローカル位置 = G_親⁻¹·(P − P_親)。
//      BVH のレストは全関節の回転が単位で、OFFSET はレストのワールド位置の差。
//   5. 根は位置＋回転。ほかは回転だけだが、ローカル位置が OFFSET から外れるフレームがある関節は位置も書く。
//      回転の並びは Z→X→Y。座標は X 反転（glTF と同じ）で右手系へ。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Context;

namespace Poly_Ling.Motion
{
    public sealed class BvhExportOptions
    {
        /// <summary>1 秒あたりのフレーム数。0 以下ならクリップの frameRate（それも無ければ 30）。</summary>
        public float Fps;
        /// <summary>区間の開始（秒）。出力では 0 秒になる。</summary>
        public float StartSec;
        /// <summary>区間の終了（秒）。StartSec 以下ならクリップの終端。</summary>
        public float EndSec;
        /// <summary>モデルの長さに掛ける倍率（OFFSET と位置のチャンネル）。</summary>
        public float PositionScale = 1f;
        /// <summary>モーションをモデルへ当てるときの位置の倍率（MotionClipApplier.PositionScale。VMD は PmxUnityRatio）。</summary>
        public float ClipPositionScale = 1f;
        /// <summary>モデルへ当てるとき T ポーズ整列を脚（太もも・すね）にも掛けるか（MotionClipApplier.AlignLegs）。</summary>
        public bool AlignLegs;
    }

    public sealed class BvhExportReport
    {
        public string OutputPath;
        public int    FrameCount;
        public int    JointCount;
        public int    PositionJointCount;
        public int    MatchedTrackCount;
        public float  DurationSec;
        public bool   RootMotion;
        public readonly List<string> SkippedIk = new List<string>();

        public string Summary()
            => $"{FrameCount} フレーム / {DurationSec:F3} 秒 / 関節 {JointCount}（位置も書いた {PositionJointCount}）" +
               $" / 当たったトラック {MatchedTrackCount} / 根の動き {(RootMotion ? "body から" : "なし")}" +
               (SkippedIk.Count > 0 ? $" / 除いた IK {SkippedIk.Count}" : "");
    }

    public static partial class BvhCodec
    {
        private const string SyntheticRootName = "PolyLingRoot";

        /// <summary>
        /// クリップをモデルへ当てながら、モデルの骨格の BVH を書く。モデルの姿勢は終わったら戻す。
        /// 失敗時は false と理由（ファイルは書かない）。
        /// </summary>
        public static bool ExportFromModel(ModelContext model, MotionClipDTO clip, string path,
                                           BvhExportOptions options, BvhExportReport report, out string reason)
        {
            reason = null;
            report = report ?? new BvhExportReport();
            options = options ?? new BvhExportOptions();
            if (model == null) { reason = "モデルがありません"; return false; }
            if (clip == null)  { reason = "モーションがありません"; return false; }
            float scale = options.PositionScale > 0f ? options.PositionScale : 1f;

            // ── 1〜4 の標本（ModelMotionSampler）────────────────────
            bool sampled = ModelMotionSampler.Sample(model, clip, new ModelMotionSampleOptions
            {
                Fps               = options.Fps,
                StartSec          = options.StartSec,
                EndSec            = options.EndSec,
                ClipPositionScale = options.ClipPositionScale,
                AlignLegs         = options.AlignLegs,
            }, out var s, out reason);
            report.SkippedIk.AddRange(s.SkippedIk);
            if (!sampled) return false;

            BvhDocument doc;
            try
            {
                var sk = s.Skeleton;

                // ── 関節（根が複数なら PolyLingRoot を先頭に足す）──────────
                int roots = 0;
                foreach (int p in s.JointParent) if (p < 0) roots++;
                bool synth = roots > 1;
                int sh = synth ? 1 : 0;

                var jNode   = new List<int>();   // 関節 → ノード（-1 は PolyLingRoot）
                var jParent = new List<int>();   // 関節 → 親の関節
                if (synth) { jNode.Add(-1); jParent.Add(-1); }
                for (int i = 0; i < s.JointNode.Count; i++)
                {
                    jNode.Add(s.JointNode[i]);
                    jParent.Add(s.JointParent[i] >= 0 ? s.JointParent[i] + sh : (synth ? 0 : -1));
                }
                int nJ = jNode.Count;

                // ── レスト ─────────────────────────────────────
                var restP = new Vector3[nJ];
                for (int j = 0; j < nJ; j++)
                    restP[j] = jNode[j] < 0 ? Vector3.zero : s.RestPos[j - sh];

                int   nF  = s.FrameCount;
                float fps = s.Fps;
                report.RootMotion        = s.RootMotion;
                report.MatchedTrackCount = s.MatchedTrackCount;

                // ── フレーム ─────────────────────────────────────
                var locR = new Quaternion[nF, nJ];   // BVH の座標のローカル回転
                var locT = new Vector3[nF, nJ];      // BVH の座標のローカル位置（倍率込み）
                var G = new Quaternion[nJ];
                var P = new Vector3[nJ];
                for (int f = 0; f < nF; f++)
                {
                    for (int j = 0; j < nJ; j++)
                    {
                        if (jNode[j] < 0)
                        {
                            var F = s.FrameRoot[f];
                            G[j] = ModelMotionSampler.Rotation(F) * Quaternion.Inverse(Quaternion.identity);
                            P[j] = new Vector3(F.m03, F.m13, F.m23);
                        }
                        else
                        {
                            G[j] = s.Delta[f, j - sh];
                            P[j] = s.Pos[f, j - sh];
                        }
                    }
                    for (int j = 0; j < nJ; j++)
                    {
                        int p = jParent[j];
                        Quaternion lr = p >= 0 ? Quaternion.Inverse(G[p]) * G[j] : G[j];
                        Vector3    lt = p >= 0 ? Quaternion.Inverse(G[p]) * (P[j] - P[p]) : P[j];
                        locR[f, j] = VrmaCodec.RotFromGltf(lr);            // X 反転は自分自身の逆
                        locT[f, j] = VrmaCodec.PosFromGltf(lt) * scale;
                    }
                }

                // ── 関節とチャンネル ──────────────────────────────────
                doc = new BvhDocument { FrameCount = nF, FrameTime = 1f / fps };
                var offset = new Vector3[nJ];
                var hasPos = new bool[nJ];
                float tol = 1e-4f * scale;
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < nJ; j++)
                {
                    int p = jParent[j];
                    offset[j] = p >= 0 ? VrmaCodec.PosFromGltf(restP[j] - restP[p]) * scale : VrmaCodec.PosFromGltf(restP[j]) * scale;
                    hasPos[j] = p < 0;
                    for (int f = 0; f < nF && !hasPos[j]; f++)
                        if ((locT[f, j] - offset[j]).sqrMagnitude > tol * tol) hasPos[j] = true;
                }

                // BvhDocument の関節（End Site を末端に足す。親は子より前）
                var docIndex = new int[nJ];
                var hasChild = new bool[nJ];
                for (int j = 0; j < nJ; j++) if (jParent[j] >= 0) hasChild[jParent[j]] = true;
                for (int j = 0; j < nJ; j++)
                {
                    string nm = jNode[j] < 0 ? SyntheticRootName : JointName(sk.Nodes[jNode[j]].Name, j);
                    while (!names.Add(nm)) nm += "_";
                    var bj = new BvhJoint
                    {
                        Name     = nm,
                        Parent   = jParent[j] >= 0 ? docIndex[jParent[j]] : -1,
                        Offset   = offset[j],
                        Channels = hasPos[j]
                            ? new[] { BvhChannel.Xposition, BvhChannel.Yposition, BvhChannel.Zposition,
                                      BvhChannel.Zrotation, BvhChannel.Xrotation, BvhChannel.Yrotation }
                            : new[] { BvhChannel.Zrotation, BvhChannel.Xrotation, BvhChannel.Yrotation },
                    };
                    docIndex[j] = doc.Joints.Count;
                    doc.Joints.Add(bj);
                    // 子が無ければ End Site（BVH の慣例。末端の印として長さ 0 で置く）
                    if (!hasChild[j])
                        doc.Joints.Add(new BvhJoint { Name = nm + "_End", Parent = docIndex[j], IsEndSite = true, Offset = Vector3.zero });
                }
                // ToText が ChannelStart を振り直すが、値の並びはここで決めるので先に振る
                int cc = 0;
                foreach (var bj in doc.Joints) { bj.ChannelStart = cc; cc += bj.Channels.Length; }
                doc.ChannelCount = cc;
                doc.Values = new float[nF * cc];

                var prev = new Vector3[nJ];
                for (int f = 0; f < nF; f++)
                {
                    int o = f * cc;
                    for (int j = 0; j < nJ; j++)
                    {
                        var bj = doc.Joints[docIndex[j]];
                        int k = o + bj.ChannelStart;
                        if (hasPos[j])
                        {
                            var lt = locT[f, j];
                            doc.Values[k++] = lt.x; doc.Values[k++] = lt.y; doc.Values[k++] = lt.z;
                        }
                        var e = EulerZXY(locR[f, j]);           // (Z, X, Y) の度
                        if (f > 0) e = Unwrap(e, prev[j]);
                        prev[j] = e;
                        doc.Values[k++] = e.x; doc.Values[k++] = e.y; doc.Values[k] = e.z;
                    }
                }

                report.FrameCount = nF;
                report.DurationSec = s.DurationSec;
                report.JointCount = nJ;
                int pj = 0; for (int j = 0; j < nJ; j++) if (hasPos[j]) pj++;
                report.PositionJointCount = pj;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BvhCodec] {ex}");
                reason = ex.Message;
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, doc.ToText(), new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                reason = $"書き込めません: {ex.Message}";
                return false;
            }
            report.OutputPath = path;
            return true;
        }

        // 名前の空白は BVH の区切りなので "_" にする。空なら番号。
        private static string JointName(string name, int j)
        {
            if (string.IsNullOrWhiteSpace(name)) return "joint" + j;
            var sb = new StringBuilder(name.Length);
            foreach (char ch in name) sb.Append(char.IsWhiteSpace(ch) || ch == '{' || ch == '}' ? '_' : ch);
            return sb.ToString();
        }

        // R = Rz(z)·Rx(x)·Ry(y) に分ける（右手系の回転行列）。戻り値は (z, x, y) の度。
        //   R の第 2 行・第 1 列 = sin x、x = asin(m21)、y = atan2(−m20, m22)、z = atan2(−m01, m11)。
        //   |m21| ≈ 1（x = ±90°）では y = 0 として z = atan2(m10, m00)。
        private static Vector3 EulerZXY(Quaternion q)
        {
            var m = Matrix4x4.Rotate(Norm(q));
            float s = Mathf.Clamp(m.m21, -1f, 1f);
            float x = Mathf.Asin(s);
            float y, z;
            if (Mathf.Abs(s) < 0.999999f)
            {
                y = Mathf.Atan2(-m.m20, m.m22);
                z = Mathf.Atan2(-m.m01, m.m11);
            }
            else
            {
                y = 0f;
                z = Mathf.Atan2(m.m10, m.m00);
            }
            return new Vector3(z, x, y) * Mathf.Rad2Deg;
        }

        // 前のフレームに近い値へ 360° 単位でそろえる。
        private static Vector3 Unwrap(Vector3 e, Vector3 prev)
            => new Vector3(Near(e.x, prev.x), Near(e.y, prev.y), Near(e.z, prev.z));

        private static float Near(float a, float prev) => a + 360f * Mathf.Round((prev - a) / 360f);
    }
}
