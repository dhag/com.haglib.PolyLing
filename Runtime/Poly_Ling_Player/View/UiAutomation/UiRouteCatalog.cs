// UiRouteCatalog.cs
// コマンドの型から、人が画面で行うときの経路（ボタンや入力欄の並び）を引く。
// Runtime/Poly_Ling_Player/View/UiAutomation/ に配置
//
// 【経路の出どころ】
//   1. コマンドに付いた PLUiRoute（PLUiRouteAttribute.cs）。Order の小さい順。
//   2. PLUiRouteByShape の付いた型（図形生成）は図形名から割り出す。
//      図形名 → カテゴリ（PlayerPrimitiveMeshSubPanel.TryGetCategoryOfKey）→ 左ペインのボタンと UI 自動操作のパネル ID。
//      カテゴリとボタン・パネル ID の対応は、左ペインのボタン（PlayerLayoutRoot.LeftPaneButtons.cs）と
//      パネル登録（PolyLingPlayerViewerCore.UiAutomation.cs の RegisterUiPanel）に合わせてここに置く。
//
// 【経路の選び方（Choose）】
//   前の項目の経路に近いものを選ぶ。近さは次の順：
//     2 … 最後のボタンや入力欄が同じパネルにある（同じパネルの中で続けて操作できる）
//     1 … 同じボタンや入力欄を含む（左ペインの同じ折り畳みなど）
//   どれも 0 なら既定（先頭）の経路。同点なら先に並んだもの。
//
// 【ShapeName の読み方】
//   ShapeName は派生ごとの定数（=> "Cube" のような式本体）で、フィールドを読まない。
//   型から引くために、コンストラクタを通さずに作った実体から 1 回だけ読み、型ごとに控える。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    /// <summary>画面での経路 1 本。</summary>
    public sealed class UiRoute
    {
        public string   Name  = "";
        public string[] Items = new string[0];
        public string   Note  = "";
        public int      Order;

        /// <summary>最後のボタンや入力欄のパネル ID。ボタンや入力欄が無ければ空。</summary>
        public string LastPanel => Items.Length > 0 ? UiRouteCatalog.PanelOf(Items[Items.Length - 1]) : "";
    }

    public static class UiRouteCatalog
    {
        private static readonly Dictionary<Type, List<UiRoute>> _cache = new Dictionary<Type, List<UiRoute>>();
        private static readonly List<UiRoute> Empty = new List<UiRoute>();

        /// <summary>ボタン・入力欄の ID のパネル部分（先頭の "." より前）。</summary>
        public static string PanelOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "";
            int dot = itemId.IndexOf('.');
            return dot < 0 ? itemId : itemId.Substring(0, dot);
        }

        /// <summary>UI を持たないと宣言されたコマンドか（PLCommand.NoUi）。</summary>
        public static bool IsNoUi(Type t)
        {
            if (t == null) return false;
            var a = t.GetCustomAttribute<PLCommandAttribute>(inherit: false);
            return a != null && a.NoUi;
        }

        /// <summary>コマンドの型の経路。無ければ空の一覧。</summary>
        public static IReadOnlyList<UiRoute> RoutesOf(Type t)
        {
            if (t == null) return Empty;
            if (_cache.TryGetValue(t, out var cached)) return cached;

            var list = new List<UiRoute>();
            foreach (var a in t.GetCustomAttributes<PLUiRouteAttribute>(inherit: false))
            {
                list.Add(new UiRoute
                {
                    Name  = a.Name ?? "",
                    Items = a.Items ?? new string[0],
                    Note  = a.Note ?? "",
                    Order = a.Order,
                });
            }
            // 並べ替えは安定にする（同じ Order なら書いた順）。
            for (int i = 0; i < list.Count; i++) list[i].Order = list[i].Order * 1000 + i;
            list.Sort((x, y) => x.Order.CompareTo(y.Order));

            if (list.Count == 0 && t.GetCustomAttribute<PLUiRouteByShapeAttribute>(inherit: true) != null)
            {
                var r = ShapeRoute(t);
                if (r != null) list.Add(r);
            }

            _cache[t] = list;
            return list;
        }

        /// <summary>
        /// 前の項目の経路 prev に近い経路を選ぶ。経路が無ければ null。
        /// </summary>
        public static UiRoute Choose(Type t, UiRoute prev)
        {
            var routes = RoutesOf(t);
            if (routes.Count == 0) return null;
            if (prev == null || routes.Count == 1) return routes[0];

            UiRoute best = routes[0];
            int bestScore = 0;
            foreach (var r in routes)
            {
                int s = Closeness(r, prev);
                if (s > bestScore) { best = r; bestScore = s; }
            }
            return best;
        }

        private static int Closeness(UiRoute r, UiRoute prev)
        {
            string last = r.LastPanel;
            if (!string.IsNullOrEmpty(last) && last == prev.LastPanel) return 2;
            foreach (var a in r.Items)
                foreach (var b in prev.Items)
                    if (a == b) return 1;
            return 0;
        }

        // ================================================================
        // 図形生成
        // ================================================================

        private static UiRoute ShapeRoute(Type t)
        {
            string shape = ShapeNameOf(t);
            if (string.IsNullOrEmpty(shape)) return null;
            if (!PlayerPrimitiveMeshSubPanel.TryGetCategoryOfKey(shape, out var cat)) return null;

            string fold, button, panel, label;
            switch (cat)
            {
                case PlayerPrimitiveMeshSubPanel.ShapeCategory.Basic:
                    fold = "leftPane.fold.Primitive"; button = "leftPane.livePrimitiveBtn";
                    panel = "primitiveBasic"; label = "基本図形"; break;
                case PlayerPrimitiveMeshSubPanel.ShapeCategory.Advanced:
                    fold = "leftPane.fold.Primitive"; button = "leftPane.liveAdvancedPrimitiveBtn";
                    panel = "primitiveAdvanced"; label = "高度な図形"; break;
                case PlayerPrimitiveMeshSubPanel.ShapeCategory.Mechanism:
                    fold = "leftPane.fold.Primitive"; button = "leftPane.liveMechanismPrimitiveBtn";
                    panel = "primitiveMechanism"; label = "機構部品A"; break;
                case PlayerPrimitiveMeshSubPanel.ShapeCategory.MechanismB:
                    fold = "leftPane.fold.Primitive"; button = "leftPane.liveMechanismBPrimitiveBtn";
                    panel = "primitiveMechanismB"; label = "機構部品B"; break;
                case PlayerPrimitiveMeshSubPanel.ShapeCategory.SpringBone:
                    fold = "leftPane.fold.Primitive"; button = "leftPane.liveSpringBonePrimitiveBtn";
                    panel = "primitiveSpringBone"; label = "揺れものボーン"; break;
                case PlayerPrimitiveMeshSubPanel.ShapeCategory.Sandbox:
                    fold = "leftPane.fold.McpSandbox"; button = "leftPane.mcpSandboxBtn";
                    panel = "primitiveSandbox"; label = "MCP用サンドボックス"; break;
                default:
                    return null;
            }

            return new UiRoute
            {
                Name  = $"図形生成（{label}）",
                Items = new[] { fold, button, $"{panel}.shape.{shape}", $"{panel}.create" },
                Note  = "図形を選び、諸元を合わせてから作成を押す",
            };
        }

        private static readonly Dictionary<Type, string> _shapeNames = new Dictionary<Type, string>();

        private static string ShapeNameOf(Type t)
        {
            if (t == null || t.IsAbstract || !typeof(CreatePrimitiveMeshCommand).IsAssignableFrom(t)) return null;
            if (_shapeNames.TryGetValue(t, out var name)) return name;
            try
            {
                var inst = (CreatePrimitiveMeshCommand)RuntimeHelpers.GetUninitializedObject(t);
                name = inst.ShapeName;
            }
            catch (Exception)
            {
                name = null;
            }
            _shapeNames[t] = name;
            return name;
        }
    }
}
