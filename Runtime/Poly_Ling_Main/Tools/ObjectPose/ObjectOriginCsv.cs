// ObjectOriginCsv.cs
// オブジェクト原点CSV（#PolyLing_ObjectOrigin）の本文を組み立て・解析する。
// Runtime/Poly_Ling_Main/Tools/ObjectPose/ に配置
//
// 【この場所に置く理由】
//   同じ書式を PlayerBoneEditorSubPanel（原点CSV書出）と
//   PlayerTPoseSubPanel（Tポーズ後の姿勢保存）の2箇所が書き、
//   PlayerBoneEditorSubPanel（原点CSV読込）と
//   PolyLingPlayerViewerCore（MQO/OBJ 読込後の原点適用）の2箇所が読む。
//   書式が離れて育つのを防ぐため、本文の組み立て（Build）と解析（Parse）を
//   ここに置く。ファイル入出力・ダイアログは呼び出し側の責務。
//
// 【除外規則】
//   MirrorSide / BakedMirror : 実体側と BoneTransform を共有する前提（H_M = H_R）。
//                              別の原点を持てないので書き出さない。
//   姿勢くさび               : 表示用の生成物で、原点は常に単位。
//   名前が空                 : 読込側は名前一致で適用するため、突き合わせ不能。
//   MeshType.Bone            : includeBones が false のときだけ除外。
//
// 【回転を位置に変換して書く場合（bakeRotationToPosition）】
//   書き出す位置は「読込後にそのオブジェクトのワールド原点が現在と同じ場所に来る
//   ローカル位置」。回転は 0 を書く。
//
//   読込側（ApplyObjectOrigins）は代入した値を
//     world = 親のワールド × TRS(位置, 0, Scale) [× BonePoseData]
//   として積むので、ワールド差分をそのまま書くと親の回転・スケールが二重に掛かる。
//   そこで読込後の階層を親から順にシミュレートし、
//     位置 = (読込後の親ワールド)⁻¹ · 現在のワールド原点 − Scale ⊙ (ポーズ層の原点)
//   を書く。親にボーンの回転が残っていても、その親の読込後ワールドで割った値に
//   なるため原点は一致する。
//
//   読込側はボーンを適用対象から外す（PlayerCommandDispatcher: indexByName の構築）。
//   適用されない行は現在の BoneTransform の値をそのまま記録する。
//   回転列を 0 で書かないと読込側に既存の回転が残るので、この場合は
//   withRotation を true に強制する。

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Tools.ObjectPose
{
    /// <summary>オブジェクト原点CSVの本文組み立て・解析。</summary>
    public static class ObjectOriginCsv
    {
        /// <summary>先頭行（書式の識別）。</summary>
        public const string Header = "#PolyLing_ObjectOrigin,version,1.0";

        /// <summary>列見出し（位置のみ）。</summary>
        public const string Columns = "name,posX,posY,posZ";

        /// <summary>列見出し（回転あり）。回転列は後ろに足すだけなので、位置だけの旧CSVもそのまま読める。</summary>
        public const string ColumnsRot = "name,posX,posY,posZ,rotX,rotY,rotZ";

        /// <summary>
        /// モデル内のオブジェクト原点を CSV 本文にする。
        /// </summary>
        /// <param name="model">対象モデル。null なら見出しだけを返す。</param>
        /// <param name="withRotation">
        /// rotX,rotY,rotZ 列を付けるか。bakeRotationToPosition が true のときは true 扱い。
        /// </param>
        /// <param name="includeBones">MeshType.Bone の行も書き出すか。</param>
        /// <param name="bakeRotationToPosition">
        /// 回転を位置に変換して書くか。読込後の階層をシミュレートして値を決める。
        /// モデルは変更しない（LocalMatrix を読むだけでキャッシュも書かない）。
        /// </param>
        /// <param name="count">書き出した行数。</param>
        /// <param name="skippedMirror">ミラー側として除外した件数。</param>
        /// <param name="skippedWedge">姿勢くさびとして除外した件数。</param>
        public static string Build(
            ModelContext model,
            bool withRotation,
            bool includeBones,
            bool bakeRotationToPosition,
            out int count,
            out int skippedMirror,
            out int skippedWedge)
        {
            count         = 0;
            skippedMirror = 0;
            skippedWedge  = 0;

            // 回転を位置に畳む場合、回転列が無いと読込側で既存の回転が残る。
            bool withRot = withRotation || bakeRotationToPosition;

            var sb = new StringBuilder();
            sb.AppendLine(Header);
            sb.AppendLine(withRot ? ColumnsRot : Columns);

            if (model == null) return sb.ToString();

            HashSet<int> wedgeIndices = ObjectPoseWedgeReader.CollectWedgeIndices(model);

            // 読込後の階層をシミュレートして、書くべきローカル位置を先に決める。
            Dictionary<int, Vector3> bakedPositions = null;
            Dictionary<string, int>  ownerByName    = null;
            if (bakeRotationToPosition)
                SimulateBakedPositions(model, wedgeIndices, out bakedPositions, out ownerByName);

            var writtenNames = new HashSet<string>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                if (!includeBones && mc.Type == MeshType.Bone) continue;
                if (mc.Type == MeshType.MirrorSide || mc.Type == MeshType.BakedMirror)
                { skippedMirror++; continue; }
                if (wedgeIndices.Contains(i)) { skippedWedge++; continue; }
                if (string.IsNullOrEmpty(mc.Name)) continue;

                Vector3 p, r;
                if (bakeRotationToPosition)
                {
                    // 同じ名前の行が複数あると、読込側は後の行で上書きする。
                    // 適用先の1件（ownerByName）以外は書かない。
                    if (ownerByName.TryGetValue(mc.Name, out int owner))
                    {
                        if (owner != i) continue;
                    }
                    else if (!writtenNames.Add(mc.Name))
                    {
                        continue;
                    }

                    if (bakedPositions.TryGetValue(i, out Vector3 baked))
                    {
                        p = baked;
                        r = Vector3.zero;
                    }
                    else
                    {
                        // 読込側が適用しない行（ボーンなど）。現在の値をそのまま記録する。
                        bool ul = mc.BoneTransform != null && mc.BoneTransform.UseLocalTransform;
                        p = ul ? mc.BoneTransform.Position : Vector3.zero;
                        r = ul ? mc.BoneTransform.Rotation : Vector3.zero;
                    }
                }
                else
                {
                    bool useLocal = mc.BoneTransform != null && mc.BoneTransform.UseLocalTransform;
                    p = useLocal ? mc.BoneTransform.Position : Vector3.zero;
                    r = useLocal ? mc.BoneTransform.Rotation : Vector3.zero;
                }

                sb.Append(EscapeCsvField(mc.Name));
                sb.Append($",{p.x:R},{p.y:R},{p.z:R}");
                if (withRot) sb.Append($",{r.x:R},{r.y:R},{r.z:R}");
                sb.AppendLine();
                count++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// 読込後の階層をシミュレートし、各オブジェクトに書くべきローカル位置を求める。
        ///
        /// 現在の階層ワールド（親のワールド × LocalMatrix。ComputeWorldMatrices と同じ規則）
        /// から目標となるワールド原点を取り、親から順に
        ///   位置 = (読込後の親ワールド)⁻¹ · 目標 − Scale ⊙ (ポーズ層の原点)
        /// を解いていく。読込側が触らないオブジェクトは現在の LocalMatrix のまま積む。
        /// </summary>
        /// <param name="bakedPositions">読込側が適用する索引 → 書くべきローカル位置。</param>
        /// <param name="ownerByName">
        /// 名前 → 読込側が適用先とする索引。読込側の indexByName と同じ規則で作る。
        /// </param>
        private static void SimulateBakedPositions(
            ModelContext model,
            HashSet<int> wedgeIndices,
            out Dictionary<int, Vector3> bakedPositions,
            out Dictionary<string, int> ownerByName)
        {
            bakedPositions = new Dictionary<int, Vector3>();
            ownerByName    = new Dictionary<string, int>();

            int n = model.MeshContextCount;

            // 読込側 ApplyObjectOrigins の適用先判定と同じ規則。
            for (int i = 0; i < n; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type == MeshType.Bone) continue;
                if (mc.Type == MeshType.MirrorSide || mc.Type == MeshType.BakedMirror) continue;
                if (wedgeIndices.Contains(i)) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (mc.BoneTransform == null) continue;
                if (!ownerByName.ContainsKey(mc.Name)) ownerByName[mc.Name] = i;
            }

            var applied = new HashSet<int>(ownerByName.Values);

            var order   = model.TopologicalSortByHierarchy();
            var current = new Matrix4x4[n];   // 現在の階層ワールド
            var sim     = new Matrix4x4[n];   // 読込後の階層ワールド
            for (int i = 0; i < n; i++) { current[i] = Matrix4x4.identity; sim[i] = Matrix4x4.identity; }

            foreach (int idx in order)
            {
                if (idx < 0 || idx >= n) continue;
                var mc = model.GetMeshContext(idx);
                if (mc == null) continue;

                int parent = mc.HierarchyParentIndex;
                bool hasParent = parent >= 0 && parent < n && parent != idx;

                Matrix4x4 curParent = hasParent ? current[parent] : Matrix4x4.identity;
                Matrix4x4 simParent = hasParent ? sim[parent]     : Matrix4x4.identity;

                Matrix4x4 local = mc.LocalMatrix;
                current[idx] = curParent * local;

                if (!applied.Contains(idx))
                {
                    sim[idx] = simParent * local;
                    continue;
                }

                // ポーズ層は読込で消えないので、その原点ぶんを差し引いた位置を書く。
                //   原点 = 親 · TRS(位置, 0, Scale) · (ポーズ層の原点)
                //        = 親 · (位置 + Scale ⊙ ポーズ層の原点)
                var pose = (mc.BonePoseData != null && mc.BonePoseData.IsActive)
                    ? mc.BonePoseData.LocalMatrix
                    : Matrix4x4.identity;
                Vector3 poseOrigin = new Vector3(pose.m03, pose.m13, pose.m23);

                Vector3 scale  = mc.BoneTransform.Scale;
                Vector3 target = new Vector3(current[idx].m03, current[idx].m13, current[idx].m23);

                Vector3 pos = simParent.inverse.MultiplyPoint3x4(target)
                            - Vector3.Scale(scale, poseOrigin);

                bakedPositions[idx] = pos;

                Matrix4x4 newLocal = Matrix4x4.TRS(pos, Quaternion.identity, scale) * pose;
                sim[idx] = simParent * newLocal;
            }
        }

        // ================================================================
        // 読み込み
        // ================================================================

        /// <summary>
        /// オブジェクト原点CSVの本文を解析する。ファイル入出力・ダイアログは呼び出し側の責務。
        ///
        /// 行の扱いは書式そのままで、
        ///   先頭が # の行（識別行を含む）・"name," で始まる列見出し行・空行 … 読み飛ばす
        ///   列が 4 未満、または posX/posY/posZ が数値でない行           … 読み飛ばす
        ///   回転列（rotX,rotY,rotZ）は任意。withRotation が false の行、
        ///   列が 7 未満の行、数値でない行は「回転の指定なし」(null) にする
        /// とする。位置だけの旧CSVをそのまま読めるようにするため。
        /// </summary>
        /// <param name="lines">CSV の全行。</param>
        /// <param name="withRotation">回転列を読む対象にするか。</param>
        /// <param name="names">行の名前。</param>
        /// <param name="positions">行の位置。</param>
        /// <param name="rotations">行の回転(°)。指定なしの行は null。</param>
        /// <param name="rotRows">回転を読み取れた行数。</param>
        /// <returns>読み取れた行数（names.Count と同じ）。</returns>
        public static int Parse(
            IEnumerable<string> lines,
            bool withRotation,
            out List<string> names,
            out List<Vector3> positions,
            out List<Vector3?> rotations,
            out int rotRows)
        {
            names     = new List<string>();
            positions = new List<Vector3>();
            rotations = new List<Vector3?>();
            rotRows   = 0;

            if (lines == null) return 0;

            foreach (string raw in lines)
            {
                string line = raw?.Trim('\uFEFF', ' ', '\t');
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("#")) continue;
                if (line.StartsWith("name,")) continue;   // 見出し行

                var cols = SplitCsvLine(line);
                if (cols.Count < 4) continue;

                if (!float.TryParse(cols[1], out float x)) continue;
                if (!float.TryParse(cols[2], out float y)) continue;
                if (!float.TryParse(cols[3], out float z)) continue;

                // 回転列は任意。無い行・読めない行は「回転の指定なし」として位置だけ適用する。
                Vector3? rot = null;
                if (withRotation && cols.Count >= 7 &&
                    float.TryParse(cols[4], out float rx) &&
                    float.TryParse(cols[5], out float ry) &&
                    float.TryParse(cols[6], out float rz))
                {
                    rot = new Vector3(rx, ry, rz);
                    rotRows++;
                }

                names.Add(cols[0]);
                positions.Add(new Vector3(x, y, z));
                rotations.Add(rot);
            }

            return names.Count;
        }

        /// <summary>引用符付きフィールドを含む 1 行をカンマで分割する。</summary>
        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var sb     = new StringBuilder();
            bool inQuote = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuote)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuote = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') inQuote = true;
                else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }

            result.Add(sb.ToString());
            return result;
        }

        /// <summary>カンマ・引用符を含む名前を CSV フィールドとして囲む。</summary>
        public static string EscapeCsvField(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
