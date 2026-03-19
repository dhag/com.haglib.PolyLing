// Tools/MergeVerticesTool.Texts.cs

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    public partial class MergeVerticesTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Merge Vertices Tool", ["ja"] = "頂点マージ", ["hi"] = "てんをくっつける" },
            ["Help"] = new() { ["en"] = "Merge selected vertices that are within threshold distance.\nA-B close, B-C close → A,B,C merged into one.", ["ja"] = "しきい値以下の距離にある選択頂点を統合します。\nA-Bが近い、B-Cが近い → A,B,Cが1つに統合", ["hi"] = "ちかいてんをひとつにするよ\nAとBがちかい、BとCがちかい→A,B,Cがひとつになるよ" },
            ["Threshold"] = new() { ["en"] = "Threshold", ["ja"] = "しきい値", ["hi"] = "きょり" },
            ["ShowPreview"] = new() { ["en"] = "Show Preview", ["ja"] = "プレビュー表示", ["hi"] = "したみ" },
            ["Groups"] = new() { ["en"] = "Groups: {0}", ["ja"] = "グループ数: {0}", ["hi"] = "グループ: {0}" },
            ["VerticesToRemove"] = new() { ["en"] = "Vertices to remove: {0}", ["ja"] = "削除頂点数: {0}", ["hi"] = "けすてん: {0}" },
            ["NoMerge"] = new() { ["en"] = "No vertices to merge", ["ja"] = "マージする頂点なし", ["hi"] = "くっつけるてんがないよ" },
            ["Merge"] = new() { ["en"] = "Merge", ["ja"] = "マージ", ["hi"] = "くっつける" },
            ["MoreGroups"] = new() { ["en"] = "... +{0} more groups", ["ja"] = "... 他{0}グループ", ["hi"] = "... ほか{0}こ" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
