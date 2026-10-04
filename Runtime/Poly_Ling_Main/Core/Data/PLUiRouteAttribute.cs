// PLUiRouteAttribute.cs
// コマンドを人が画面で行うときの経路（どのボタンや入力欄を通るか）の宣言。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PLCommandAttribute と同じ場所）
//
// 【何に使うか】
//   シナリオを流すとき、項目のコマンドに当たるボタンへ赤枠を出す（案内バー）。
//   「今なにをしているか」「次に押すボタン」「人間の入力待ち」のどれでも、この宣言からボタンや入力欄を引く。
//
// 【経路】
//   1 つの経路は、関係するボタン・入力欄の ID の並び（UI 自動操作のボタン・入力欄の ID。uiDescribe で引ける）。
//   区切り（何段目に押すか）は持たない。経路に入ったボタンや入力欄は全部同時に枠で囲む。
//   人間の入力待ちは「そのコマンドが実行されたか」で判定するので、最後に押すボタンを特定しなくてよい。
//   ボタンで表せない操作（ビューポートで描く・ショートカット）は Note に書く。枠はそこへ行き着くまでのボタンや入力欄にだけ付く。
//
// 【入口が複数あるとき】
//   経路を複数書く。Order の小さいものが既定。シナリオを流すときは、前の項目の経路に近いもの
//   （最後のボタンや入力欄が同じパネルにある → 同じボタンや入力欄を含む）を選び、近いものが無ければ既定を使う（UiRouteCatalog.Choose）。
//
// 【書かないとき】
//   UI を持たないコマンド（MCP 専用・照会・内部の後始末）は PLCommand の NoUi を立てる。
//   経路も NoUi も無いコマンドは PanelCommandFactoryAudit.RunAll が数える。
//   経路に書いたボタン・入力欄の ID が UI 自動操作に登録されていないものは queryUiAutomationAudit が数える。
//
// 【図形生成】
//   CreatePrimitiveMeshCommand の派生は図形ごとに書かない。PLUiRouteByShape の付いた型は、
//   画面側（UiRouteCatalog）が図形名から左ペインのボタンと図形ボタンを割り出す。

using System;

namespace Poly_Ling.Data
{
    /// <summary>
    /// コマンドを人が画面で行うときの経路 1 本。具象コマンドに複数付けてよい。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class PLUiRouteAttribute : Attribute
    {
        /// <summary>経路の名前（人が読む。案内バーと queryScenarioRun に出す）。</summary>
        public string Name { get; }

        /// <summary>関係するボタン・入力欄の ID。左ペインなら折り畳みの見出しとボタン、右ペインならパネルのボタンや入力欄。</summary>
        public string[] Items { get; }

        /// <summary>並び順。小さいものが既定の経路。</summary>
        public int Order { get; set; }

        /// <summary>ボタンで表せない操作の説明（ビューポートで描く・ショートカットなど）。空でもよい。</summary>
        public string Note { get; set; } = "";

        public PLUiRouteAttribute(string name, params string[] items)
        {
            Name  = name ?? "";
            Items = items ?? new string[0];
        }
    }

    /// <summary>
    /// 経路を図形名から割り出す印。CreatePrimitiveMeshCommand に付け、派生へ継ぐ。
    /// 割り出しは画面側（UiRouteCatalog）が行う（どのボタンがどの図形かを知っているのは画面側だけ）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class PLUiRouteByShapeAttribute : Attribute
    {
    }
}
