// PanelCommandAddonCatalog.cs
// 「呼べるもの」の一覧（tools_search / tools_describe）に Python アドオンを載せるための受け口。
//
// 【なぜ受け口か】
//   アドオンの登録簿と宣言（引数・出力）は Player 側の PyAddonHost / PyAddonSettings が持つ。
//   検索の本体（PanelCommandSchemaIndex）はコマンドとシナリオの索引だけを知っていればよいので、
//   登録簿の中身はビューアがここへ渡す（AddonCatalogProvider）。渡されていなければアドオンは 0 件。
//
// 【何を載せるか】
//   PyAddonHost が describe で受けて保存した宣言（PyAddons.json の specs）。まだ一度も起動していない
//   アドオンは宣言が無いので、名前と表題だけで照合し、hasSpec=false で返す（invokePyAddon action=describe で読める）。
//
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommandSchemaIndex.cs と同じ場所）

using System;
using System.Collections.Generic;

namespace Poly_Ling.Data
{
    /// <summary>一覧に載せるアドオン 1 件。</summary>
    public sealed class AddonCatalogEntry
    {
        public string Id          = "";
        public string Title       = "";
        public string Description = "";
        public bool   Official;
        /// <summary>stopped / starting / running / failed。</summary>
        public string State       = "";
        public bool   HasSpec;
        /// <summary>引数の宣言（JSON 配列の文字列。無ければ "[]"）。</summary>
        public string ArgsJson    = "[]";
        /// <summary>出力の宣言（JSON 配列の文字列。無ければ "[]"）。</summary>
        public string OutputsJson = "[]";
        /// <summary>照合用の本文（名前・表題・説明・引数と出力の名前とラベル）。小文字にしたもの。</summary>
        public string Haystack    = "";
    }

    public static partial class PanelCommandFactory
    {
        /// <summary>検索で返す種類（アドオン）。</summary>
        public const string SearchKindAddon = "addon";

        /// <summary>アドオンの一覧を返す口（ビューアが配線する）。null なら 0 件。</summary>
        public static Func<List<AddonCatalogEntry>> AddonCatalogProvider;

        private static List<AddonCatalogEntry> AddonCatalog()
        {
            try { return AddonCatalogProvider?.Invoke() ?? new List<AddonCatalogEntry>(); }
            catch { return new List<AddonCatalogEntry>(); }
        }

        /// <summary>
        /// tools_describe の addons。1 件ずつ {name, title, description, official, state, hasSpec, args:[…], outputs:[…], invoke}。
        /// 無い名前は notFound へ。
        /// </summary>
        public static string BuildAddonDescribeJson(IList<string> ids, out List<string> notFound)
        {
            notFound = new List<string>();
            var all = AddonCatalog();
            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (var id in ids)
            {
                AddonCatalogEntry e = null;
                foreach (var a in all) if (string.Equals(a.Id, id, StringComparison.Ordinal)) { e = a; break; }
                if (e == null) { notFound.Add(id); continue; }
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"name\":").Append(Quote(e.Id));
                sb.Append(",\"title\":").Append(Quote(e.Title));
                sb.Append(",\"description\":").Append(Quote(e.Description));
                sb.Append(",\"official\":").Append(e.Official ? "true" : "false");
                sb.Append(",\"state\":").Append(Quote(e.State));
                sb.Append(",\"hasSpec\":").Append(e.HasSpec ? "true" : "false");
                sb.Append(",\"args\":").Append(string.IsNullOrEmpty(e.ArgsJson) ? "[]" : e.ArgsJson);
                sb.Append(",\"outputs\":").Append(string.IsNullOrEmpty(e.OutputsJson) ? "[]" : e.OutputsJson);
                sb.Append(",\"invoke\":").Append(Quote(
                    "invokePyAddon addonId=" + e.Id + " argsJson={名前:値…}（シナリオでは argKeys / argValues）。"
                    + "返事は queryPyAddonCall、シナリオの項目なら @prev.<result の欄>"));
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
