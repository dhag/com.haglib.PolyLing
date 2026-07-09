// Assets/Editor/Poly_Ling/Core/Serialization/FolderSerializer/AttachSidecarCsv.cs
// ============================================================
// 付帯データ・サイドカーCSV（attach.csv）— IK 専用
// ============================================================
//
// 【役割】
//   Unity 階層の往復で失われる per-bone 付帯のうち IK（root/link）を、
//   プレファブと同居する attach.csv に**ボーン名キー**で入出力する。
//   Humanoid 割当・HumanLimit は Avatar が正本（案X）のため本CSVには含めない。
//   SpringBone は VRM 側へ移管のため対象外。
//
// 【形式】
//   #PolyLing_Attach,version,1.0
//   ikRoot,<bone>,<effectorBoneName>,<loop>,<limitAngle>
//   ikLinkBone,<bone>,<hasLimit>,minX,minY,minZ,maxX,maxY,maxZ
//   ※角度はラジアン（内部生値・変換なし）。カンマ/引用符を含む名前は "..." で囲み " は "" に倍化。
//
// 【依存】
//   Runtime・純データ（#if UNITY_EDITOR / AssetDatabase を含まない）。
//   参照解決（IKData.Links / TargetIndex の再構築）は呼び出し側で
//   IKChainResolver.RebuildLinksFromPerBone を実行すること。
//
// ============================================================

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Serialization.FolderSerializer
{
    /// <summary>IK per-bone 付帯の attach.csv 入出力（ボーン名キー）。</summary>
    public static class AttachSidecarCsv
    {
        // ── Write ─────────────────────────────────────────────
        public static void Write(ModelContext model, string path)
        {
            if (model == null || string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_Attach,version,1.0");

            var list = model.MeshContextList;
            for (int i = 0; i < list.Count; i++)
            {
                var mo = list[i]?.MeshObject;
                if (mo == null || mo.Type != MeshType.Bone) continue;
                string bone = mo.Name ?? "";
                if (string.IsNullOrEmpty(bone)) continue;

                var ik = mo.IKData;
                if (ik != null && ik.IsIK)
                {
                    sb.AppendLine(
                        $"ikRoot,{Esc(bone)},{Esc(ik.EffectorBoneName ?? "")},{ik.LoopCount},{F(ik.LimitAngle)}");
                }

                var lk = mo.IKLink;
                if (lk != null)
                {
                    sb.AppendLine(
                        $"ikLinkBone,{Esc(bone)},{lk.HasLimit}," +
                        $"{F(lk.LimitMin.x)},{F(lk.LimitMin.y)},{F(lk.LimitMin.z)}," +
                        $"{F(lk.LimitMax.x)},{F(lk.LimitMax.y)},{F(lk.LimitMax.z)}");
                }
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        // ── Read ──────────────────────────────────────────────
        //   ボーン名一致で MeshObject.IKData / IKLink へ適用。
        //   TargetIndex / IKData.Links は呼び出し側の RebuildLinksFromPerBone で再構築。
        public static void Read(ModelContext model, string path)
        {
            if (model == null || string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            // ボーン名 → MeshObject（Type==Bone・先勝ち）
            var boneByName = new Dictionary<string, MeshObject>();
            var list = model.MeshContextList;
            for (int i = 0; i < list.Count; i++)
            {
                var mo = list[i]?.MeshObject;
                if (mo == null || mo.Type != MeshType.Bone) continue;
                if (!string.IsNullOrEmpty(mo.Name) && !boneByName.ContainsKey(mo.Name))
                    boneByName[mo.Name] = mo;
            }

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Count < 2) continue;

                string bone = Unesc(cols[1]);
                if (!boneByName.TryGetValue(bone, out var mo)) continue;

                switch (cols[0])
                {
                    case "ikRoot":
                        if (mo.IKData == null) mo.IKData = new IKData();
                        mo.IKData.IsIK = true;
                        mo.IKData.EffectorBoneName = cols.Count > 2 ? Unesc(cols[2]) : "";
                        mo.IKData.LoopCount  = PInt(cols, 3);
                        mo.IKData.LimitAngle = PF(cols, 4);
                        break;
                    case "ikLinkBone":
                        mo.IKLink = new IKLinkData
                        {
                            HasLimit = PB(cols, 2),
                            LimitMin = new Vector3(PF(cols, 3), PF(cols, 4), PF(cols, 5)),
                            LimitMax = new Vector3(PF(cols, 6), PF(cols, 7), PF(cols, 8))
                        };
                        break;
                }
            }
        }

        // ── 小ヘルパ（CSV/数値。既存書式と同等） ─────────────────
        private static string F(float v) => v.ToString("G9", CultureInfo.InvariantCulture);

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2).Replace("\"\"", "\"");
            return s;
        }

        private static List<string> Split(string line)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else q = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') q = true;
                    else if (c == ',') { res.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            res.Add(sb.ToString());
            return res;
        }

        private static int PInt(List<string> cols, int idx, int def = 0)
            => idx < cols.Count && int.TryParse(cols[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

        private static float PF(List<string> cols, int idx, float def = 0f)
            => idx < cols.Count && float.TryParse(cols[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        private static bool PB(List<string> cols, int idx, bool def = false)
            => idx < cols.Count && bool.TryParse(cols[idx], out var v) ? v : def;
    }
}
