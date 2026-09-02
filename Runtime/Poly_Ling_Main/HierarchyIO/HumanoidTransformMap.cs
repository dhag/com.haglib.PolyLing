// Runtime/Poly_Ling_Main/HierarchyIO/HumanoidTransformMap.cs
// ============================================================
// Humanoid 割当 → 生成済み GameObject 名の対応表
// ============================================================
//
// 【分離規約】規約は HierarchyBuilder.cs 冒頭のコメントを正典とする。
//
// 【役割】
//   ModelContext.HumanoidMapping と HierarchyBuildResult の索引→Transform 表から、
//   humanName（HumanTrait.BoneName 形式）→ 生成済み GameObject 名の対応表を作る。
//   Avatar 生成（Editor）も UniHumanoid.Humanoid への割当（VRM 経路）も
//   同じ表を消費できるよう、Avatar そのものはここでは作らない。
//
// 【索引で引く理由】
//   割当先を ctx.Name で引くと、HierarchyBuilder の MakeUniqueName による
//   改名（"X_skinned" / "X_1"）やミラー枝の接尾辞 "+" を取りこぼす。
//   HierarchyBuildResult が記録した索引→Transform を引き、実際の GameObject 名を使う。
//
// 【半身モデルのミラー補完】
//   ミラー側コンテキストは頂点を持つメッシュにしか作られない
//   （MirrorBranchOps.CreateDerivedMirrorContext が頂点ゼロで null を返す）。
//   よって頂点ゼロの関節はミラー側に索引が無く、humanoid.csv には
//   実体側（半身）ぶんしか書けない。ミラー側の関節が実体になるのは
//   HierarchyBuilder の mirror:true 側だけなので、その対応付けはここで補う。
//   左右名を入れ替えた humanName をミラー側 GO に割り当てる。
//   明示割当が既にある場合は常にそちらを優先する（正本はモデル側）。
//
// 【移植元】
//   Editor/HierarchyIO/HierarchyExportWindow.cs の BuildAvatarMapsFromModel ほか。
//   ロジックは移設時に変更していない。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.HierarchyIO
{
    /// <summary>Humanoid 対応表の構築結果。</summary>
    public class HumanoidMapResult
    {
        /// <summary>humanName(HumanTrait.BoneName 形式) → 生成済み GameObject 名。</summary>
        public readonly Dictionary<string, string> Map = new Dictionary<string, string>();

        /// <summary>
        /// humanName(HumanTrait.BoneName 形式) → 生成済み Transform。
        /// Map と同じ内容を Transform で持つ。Avatar 生成は名前で引くが、
        /// UniHumanoid.Humanoid.AssignBones は Transform を要求するため両方持つ。
        /// </summary>
        public readonly Dictionary<string, Transform> TransformMap = new Dictionary<string, Transform>();

        /// <summary>humanName → HumanLimit（度・custom のみ）。</summary>
        public readonly Dictionary<string, HumanLimit> Limits = new Dictionary<string, HumanLimit>();

        /// <summary>警告（呼び出し側がログ・ダイアログへ流す）。</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>左右補完した内容（"RightUpperArm → 右腕" 形式）。</summary>
        public readonly List<string> SupplementedLog = new List<string>();
    }

    /// <summary>Humanoid 割当 → 生成済み GameObject 名の対応表を作る。</summary>
    public static class HumanoidTransformMap
    {
        /// <summary>
        /// model の Humanoid 割当・可動域から対応表を構築する。
        /// hierarchy は HierarchyBuilder.Build の戻り値。
        /// </summary>
        public static HumanoidMapResult Build(ModelContext model, HierarchyBuildResult hierarchy)
        {
            var result = new HumanoidMapResult();

            var mapping = model?.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty || hierarchy == null) return result;

            var map    = result.Map;
            var limits = result.Limits;

            // ── 1) 明示割当（モデルが正本）────────────────────────────
            var mappedIndexByTrait = new Dictionary<string, int>();
            var missing = new List<string>();

            foreach (var kv in mapping.BoneIndexMap)
            {
                string traitName = ToHumanTraitName(kv.Key);
                var ctx = model.GetMeshContext(kv.Value);
                if (ctx == null) continue;

                // 実体側を優先。MirrorSide メッシュは実体側に居ないのでミラー枝側で引く。
                Transform tf = null;
                if (!hierarchy.RealTransformByIndex.TryGetValue(kv.Value, out tf))
                    hierarchy.MirrorTransformByIndex.TryGetValue(kv.Value, out tf);

                if (tf == null)
                {
                    missing.Add($"{traitName} → [{kv.Value}] {ctx.Name}");
                    continue;
                }

                map[traitName] = tf.name;
                result.TransformMap[traitName] = tf;
                mappedIndexByTrait[traitName] = kv.Value;

                var limit = ToHumanLimitDeg(ctx);
                if (limit.HasValue) limits[traitName] = limit.Value;
            }

            if (missing.Count > 0)
            {
                result.Warnings.Add(
                    "Humanoid 割当先が階層に出力されていない（無視）:\n  " +
                    string.Join("\n  ", missing) +
                    "\n（非表示のため「可視メッシュのみ」で除外された可能性があります）");
            }

            // ── 2) ミラー枝の関節を左右反転して補完 ──────────────────
            // ミラー枝が1つも展開されていないと補完しようがない。
            // 分岐ルート未設定（MQO 名の "@@…ミラー分岐ルート" か手動設定）を疑う。
            if (hierarchy.MirrorTransformByIndex.Count == 0 && HasMirrorSideContext(model))
            {
                result.Warnings.Add(
                    "ミラー側コンテキストはあるがミラー枝が展開されていません。" +
                    "ミラー分岐ルートが未設定の可能性があります（左右の補完は行いません）。");
            }

            foreach (var kv in new List<KeyValuePair<string, int>>(mappedIndexByTrait))
            {
                string traitName = kv.Key;
                int    index     = kv.Value;

                // 両表に同じ索引がある＝そのノードが両側に出ている。
                // ミラー側の実現方法（関節の両側複製／別 MeshContext の MirrorSide）は
                // HierarchyBuilder の末尾で実体側索引へ寄せてあるため、ここでは区別しない。
                if (!hierarchy.RealTransformByIndex.ContainsKey(index)) continue;
                if (!hierarchy.MirrorTransformByIndex.TryGetValue(index, out var mirrorTf)) continue;
                if (mirrorTf == null) continue;

                string otherTrait = SwapLeftRightTraitName(traitName);
                if (string.IsNullOrEmpty(otherTrait))
                {
                    // 左右を持たない名前（Spine/Head 等）が両側に複製されている。
                    // 体幹がミラー枝に入っているモデル構造の誤りなので補完しない。
                    result.Warnings.Add(
                        $"'{traitName}' がミラー枝内で両側に複製されています。" +
                        "左右を持たない Humanoid 名のため補完しません。");
                    continue;
                }

                // 明示割当が優先。ただし左右が同じ Transform を指している場合だけは
                // 例外で、ミラー側へ振り直す（半身の同一関節へ左右両方を当てた入力）。
                // そのままでは 1 つの Transform が 2 つの Humanoid ボーンを主張して
                // AvatarBuilder が失敗する。
                // どちらを動かすかは Right 側に固定して決定論にする
                // （実体側 ＝ 半身として作られた側を残す）。
                if (map.TryGetValue(otherTrait, out string existingName))
                {
                    if (!string.Equals(existingName, map[traitName], System.StringComparison.Ordinal))
                        continue;
                    if (!otherTrait.StartsWith("Right")) continue;
                }

                // 左右反転はミラー軸が X のときだけ意味を持つ。
                // 軸の正本は ApplyJointTransform と同じく当該コンテキスト自身。
                var ctx = model.GetMeshContext(index);
                int axis = ctx?.MirrorAxis ?? 1;
                if (axis != 1)
                {
                    result.Warnings.Add(
                        $"'{traitName}' のミラー軸が X ではないため " +
                        $"(MirrorAxis={axis}) 左右補完をスキップします。");
                    continue;
                }

                map[otherTrait] = mirrorTf.name;
                result.TransformMap[otherTrait] = mirrorTf;

                // 可動域は Unity のマッスル空間が左右対称のため、そのまま同値を使う。
                if (limits.TryGetValue(traitName, out var lim)) limits[otherTrait] = lim;

                result.SupplementedLog.Add($"{otherTrait} → {mirrorTf.name}");
            }

            return result;
        }

        // ================================================================
        // 検査・変換
        // ================================================================

        /// <summary>
        /// Humanoid 割当先のボーン名が root 配下で一意かを検査する。
        ///   重複していると AvatarBuilder が Ambiguous Transform で失敗するため、
        ///   Unity 側のエラーより先に該当名を通知する。
        /// </summary>
        public static bool ValidateHumanoidBoneNames(
            GameObject root, Dictionary<string, string> map, out string duplicatedNames)
        {
            duplicatedNames = string.Empty;
            if (root == null || map == null || map.Count == 0) return true;

            var count = new Dictionary<string, int>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                count.TryGetValue(t.name, out int c);
                count[t.name] = c + 1;
            }

            var dup = new List<string>();
            foreach (var kv in map)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (count.TryGetValue(kv.Value, out int c) && c > 1 && !dup.Contains(kv.Value))
                    dup.Add(kv.Value);
            }

            if (dup.Count == 0) return true;

            duplicatedNames = string.Join(", ", dup);
            return false;
        }

        /// <summary>モデルにミラー側コンテキスト（MirrorSide / BakedMirror）が1件でもあるか。</summary>
        public static bool HasMirrorSideContext(ModelContext model)
        {
            if (model == null) return false;

            for (int i = 0; i < model.MeshContextCount; i++)
                if (MirrorBranchOps.IsMirrorSideContext(model.GetMeshContext(i))) return true;

            return false;
        }

        /// <summary>
        /// MeshContext の per-bone HumanLimit（ラジアン）→ Avatar 用 HumanLimit（度）。
        /// 既定値・未保持なら null。
        /// </summary>
        public static HumanLimit? ToHumanLimitDeg(MeshContext ctx)
        {
            var hl = ctx?.MeshObject?.HumanLimit;
            if (hl == null || hl.UseDefaultValues) return null;

            return new HumanLimit
            {
                useDefaultValues = false,
                min    = hl.Min * Mathf.Rad2Deg,
                max    = hl.Max * Mathf.Rad2Deg,
                center = hl.Center * Mathf.Rad2Deg,
                axisLength = hl.AxisLength
            };
        }

        /// <summary>
        /// HumanTrait.BoneName 形式の左右を入れ替える。
        ///   "LeftUpperArm" ⇔ "RightUpperArm" / "Left Thumb Proximal" ⇔ "Right Thumb Proximal"
        /// 左右を持たない名前、入れ替え結果が Humanoid 名でない場合は null。
        /// </summary>
        public static string SwapLeftRightTraitName(string traitName)
        {
            if (string.IsNullOrEmpty(traitName)) return null;

            string swapped;
            if (traitName.StartsWith("Left"))
                swapped = "Right" + traitName.Substring("Left".Length);
            else if (traitName.StartsWith("Right"))
                swapped = "Left" + traitName.Substring("Right".Length);
            else
                return null;

            return System.Array.IndexOf(HumanTrait.BoneName, swapped) >= 0 ? swapped : null;
        }

        /// <summary>
        /// HumanTrait.BoneName 形式 → HumanBodyBones。
        /// BoneName の並びは HumanBodyBones の値と一対一なので index から戻す。
        /// 解釈できなければ HumanBodyBones.LastBone（AssignBones が無視する値）。
        /// </summary>
        public static HumanBodyBones ToHumanBodyBones(string traitName)
        {
            if (string.IsNullOrEmpty(traitName)) return HumanBodyBones.LastBone;

            int i = System.Array.IndexOf(HumanTrait.BoneName, traitName);
            if (i >= 0) return (HumanBodyBones)i;

            // 念のため列挙名としても解釈を試す（"LeftUpperArm" などはどちらでも通る）。
            if (System.Enum.TryParse<HumanBodyBones>(traitName, out var hbb)) return hbb;

            return HumanBodyBones.LastBone;
        }

        /// <summary>HumanBodyBones 列挙形 → HumanTrait.BoneName 形式（解釈できなければそのまま）。</summary>
        public static string ToHumanTraitName(string enumName)
        {
            if (!string.IsNullOrEmpty(enumName) &&
                System.Enum.TryParse<HumanBodyBones>(enumName, out var hbb))
            {
                int i = (int)hbb;
                if (i >= 0 && i < HumanTrait.BoneName.Length)
                    return HumanTrait.BoneName[i];
            }
            return enumName;
        }
    }
}
