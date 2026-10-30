// ModifierInventory.cs
// 生成コマンドを全件、モディファイアとしての型（A 単発・B 入力あり・C 組立）に分け、
// グループに記録されるか・書き戻せるか・読む入力・逆方向を一覧にする。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【何のために要るか】
//   作り直し整備計画（PolyLing_作り直し整備計画.md）段 0 の棚卸しを、
//   コードを読んで手で書いた表ではなく、型と属性から機械的に出すため。
//
// 【どこから何を取るか】
//   対象        … Category が "geometry.create" で始まるもの、または PLModifier を持つもの（抽象は除く）
//   記録・逆方向 … PLModifierAttribute（受け口のコードが根拠。型からは導けない）
//   書き戻せるか … PanelCommandFactory.RebuildKeys（PLParam.RebuildRole）
//   読む入力    … InputKeys。PanelCommandFactory.MeshRefKeys から、出力先（RebuildRole の TargetIndex と
//                  placement.*）を除いたもの。プロファイルの取り込み元（ProfileSourceIndex）は
//                  ProfileKeys を持つときだけ入力に数える（全図形が受け取る共通の引数のため）。
//                  評価器（Ops/ModifierEvaluator）も同じ規則で入力を引く
//   型          … C = Capture が Assembly / B = 読む入力かプロファイルがある / A = それ以外

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Poly_Ling.Data
{
    /// <summary>生成コマンドのモディファイアとしての棚卸し。</summary>
    public static class ModifierInventory
    {
        /// <summary>1 コマンドぶんの結果。</summary>
        public sealed class Entry
        {
            public string Action      = "";
            public string TypeName    = "";
            public string Category    = "";
            /// <summary>"A" / "B" / "C"。</summary>
            public string Kind        = "";
            public PLGroupCapture Capture;
            /// <summary>書き戻せるか（RebuildKeys.IsSupported）。</summary>
            public bool   Rebuild;
            /// <summary>書き戻し先のキー。書き戻せないときは空。</summary>
            public string RebuildKey  = "";
            /// <summary>読む入力。"キー(Read)" の並び。</summary>
            public List<string> Inputs = new List<string>();
            /// <summary>プロファイル本体のキー。</summary>
            public List<string> Profiles = new List<string>();
            public string Inverse     = "";
            public string InverseOf   = "";
            /// <summary>逆方向で保つもの・失うもの（PLModifierAttribute.InverseKeeps / InverseLoses）。</summary>
            public string InverseKeeps = "";
            public string InverseLoses = "";
            /// <summary>入力のドラッグ中にも追随するか（PLModifierAttribute.FollowsDrag）。</summary>
            public bool   FollowsDrag;

            public string InputsText   => Inputs.Count   == 0 ? "" : string.Join(", ", Inputs);
            public string ProfilesText => Profiles.Count == 0 ? "" : string.Join(", ", Profiles);
        }

        /// <summary>対象の全コマンドを棚卸しする。並びは型・action 名の順。</summary>
        public static List<Entry> Build()
        {
            var list = new List<Entry>();

            foreach (var t in PLParamAudit.FindCommandTypes())
            {
                var cmdAttr = t.GetCustomAttribute<PLCommandAttribute>(inherit: false);
                var modAttr = t.GetCustomAttribute<PLModifierAttribute>(inherit: true);
                string cat = cmdAttr?.Category ?? "";

                bool isCreate = cat.StartsWith("geometry.create", StringComparison.Ordinal);
                if (!isCreate && modAttr == null) continue;

                var e = new Entry
                {
                    Action    = PanelCommandFactory.ActionOf(t),
                    TypeName  = t.Name,
                    Category  = cat,
                    Capture   = modAttr?.Capture ?? PLGroupCapture.None,
                    Inverse   = modAttr?.Inverse ?? "",
                    InverseOf = modAttr?.InverseOf ?? "",
                    InverseKeeps = modAttr?.InverseKeeps ?? "",
                    InverseLoses = modAttr?.InverseLoses ?? "",
                };

                var rb = PanelCommandFactory.RebuildKeys(t);
                e.Rebuild    = rb.IsSupported;
                e.RebuildKey = rb.IsSupported ? rb.TargetIndexKey : "";

                e.FollowsDrag = modAttr?.FollowsDrag ?? false;

                foreach (var pk in PanelCommandFactory.ProfileKeys(t)) e.Profiles.Add(pk.Key);

                foreach (var mr in InputKeys(t))
                    e.Inputs.Add($"{mr.Key}({mr.Access})");

                if (e.Capture == PLGroupCapture.Assembly) e.Kind = "C";
                else if (e.Inputs.Count > 0 || e.Profiles.Count > 0) e.Kind = "B";
                else e.Kind = "A";

                list.Add(e);
            }

            list.Sort((a, b) =>
            {
                int k = string.CompareOrdinal(a.Kind, b.Kind);
                return k != 0 ? k : string.CompareOrdinal(a.Action, b.Action);
            });
            return list;
        }

        /// <summary>
        /// このコマンド型が読む入力のキー（IsMeshRef のうち出力先でないもの）。宣言順。
        ///
        /// 除くもの
        ///   書き戻し先（RebuildRole の TargetIndex）・置き場所（placement.*）。
        ///   プロファイルの取り込み元（ProfileSourceIndex）は ProfileKeys を持たない型では除く
        ///   （全図形が受け取る共通の引数で、プロファイルを使わない図形では読まない）。
        /// 棚卸し（Build）と評価器（ModifierEvaluator）が同じ規則を使う。
        /// </summary>
        public static List<PanelCommandFactory.MeshRefKey> InputKeys(Type t)
        {
            var result = new List<PanelCommandFactory.MeshRefKey>();
            if (t == null) return result;

            var rb = PanelCommandFactory.RebuildKeys(t);
            int profileCount = PanelCommandFactory.ProfileKeys(t).Count;
            string profileSrcKey = PanelCommandFactory.KeyOfProperty(
                t, nameof(CreatePrimitiveMeshCommand.ProfileSourceIndex));

            foreach (var mr in PanelCommandFactory.MeshRefKeys(t))
            {
                if (rb.IsSupported && string.Equals(mr.Key, rb.TargetIndexKey, StringComparison.Ordinal)) continue;
                if (mr.Key.StartsWith("placement.", StringComparison.Ordinal)) continue;
                if (!string.IsNullOrEmpty(profileSrcKey)
                    && string.Equals(mr.Key, profileSrcKey, StringComparison.Ordinal)
                    && profileCount == 0) continue;

                result.Add(mr);
            }
            return result;
        }

        /// <summary>人が読む一覧（タブ区切り）。</summary>
        public static string Report(List<Entry> list)
        {
            var sb = new StringBuilder();
            int a = 0, b = 0, c = 0, captured = 0, rebuild = 0, inverse = 0;
            foreach (var e in list)
            {
                if (e.Kind == "A") a++; else if (e.Kind == "B") b++; else c++;
                if (e.Capture != PLGroupCapture.None) captured++;
                if (e.Rebuild) rebuild++;
                if (!string.IsNullOrEmpty(e.Inverse) || !string.IsNullOrEmpty(e.InverseOf)) inverse++;
            }

            sb.Append("[ModifierInventory] 生成コマンド ").Append(list.Count)
              .Append(" / A ").Append(a).Append(" / B ").Append(b).Append(" / C ").Append(c)
              .Append(" / 記録される ").Append(captured)
              .Append(" / 書き戻せる ").Append(rebuild)
              .Append(" / 逆方向あり ").Append(inverse);
            sb.Append('\n').Append("型\taction\t記録\t書き戻し\t読む入力\tプロファイル\t逆方向\tドラッグ追随\tカテゴリ\t逆方向で保つもの\t逆方向で失うもの");

            foreach (var e in list)
            {
                string inv = !string.IsNullOrEmpty(e.Inverse) ? "→" + e.Inverse
                           : !string.IsNullOrEmpty(e.InverseOf) ? "←" + e.InverseOf : "";
                sb.Append('\n')
                  .Append(e.Kind).Append('\t')
                  .Append(e.Action).Append('\t')
                  .Append(e.Capture).Append('\t')
                  .Append(e.Rebuild ? e.RebuildKey : "不可").Append('\t')
                  .Append(e.InputsText).Append('\t')
                  .Append(e.ProfilesText).Append('\t')
                  .Append(inv).Append('\t')
                  .Append(e.FollowsDrag ? "追随" : "").Append('\t')
                  .Append(e.Category).Append('\t')
                  .Append(e.InverseKeeps).Append('\t')
                  .Append(e.InverseLoses);
            }
            return sb.ToString();
        }
    }
}
