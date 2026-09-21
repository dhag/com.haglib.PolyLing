// PLCommandCategories.cs
// 機能カテゴリ（PLCommand.Category）の正典。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【何を表すか】
//   コマンドが「何をするか」だけを表す主分類。1 コマンドに 1 つ。
//   用途（何に使いやすいか）は Tags、壊れ得るものは Hazards、作業の目的は利用シーンで表す
//   （PolyLing_利用シーン_カテゴライズ設計方針.md 3 節）。
//
// 【階層】
//   "." で区切る。利用シーンや検索で "geometry" を指定すると "geometry.topology" なども含む
//   （SceneDefinition.CategoryMatches）。
//
// 【足すとき】
//   ここに 1 行足してから、コマンドの属性に使う。ここに無い分類を属性に書くと
//   監査（PanelCommandFactoryAudit）が「正典に無い分類」として数える。
//   分類を足したら、既存の利用シーンの categories を見直すこと（監査が一覧を出す）。

using System;
using System.Collections.Generic;

namespace Poly_Ling.Data
{
    public static class PLCommandCategories
    {
        /// <summary>正典の分類と、その説明。並びは表示順。</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> All = new List<KeyValuePair<string, string>>
        {
            Pair("query",                      "モデルを変えずに数える・調べる"),
            Pair("query.data",                 "生データの取得・書き戻し・選択の書き出し"),
            Pair("selection",                  "選択する"),
            Pair("selection.set",              "選択集合の保存・読み出し・管理"),
            Pair("transform.object",           "オブジェクトを動かす・回す"),
            Pair("workaxis",                   "作業軸の設定・登録・読み書き"),
            Pair("geometry.create.primitive",  "基本形状を作る"),
            Pair("geometry.create.mechanical", "機械部品を作る"),
            Pair("geometry.create.special",    "髪・文字・回転体など特殊な形状を作る"),
            Pair("geometry.create.derived",    "既存の形状から派生させて作る（帯・橋・配列など）"),
            Pair("geometry.position",          "既存の頂点の位置を変える（位相は変えない）"),
            Pair("geometry.topology",          "頂点・面の増減や接続を変える"),
            Pair("geometry.attribute",         "法線・パーツ ID・面の表示など頂点面の属性を変える"),
            Pair("geometry.deform",            "まとめて変形させる（デフォーマ・ブレンド）"),
            Pair("linegroup",                  "線分群の作成・編集"),
            Pair("material",                   "材質スロットと材質の設定"),
            Pair("uv",                         "UV の展開・編集"),
            Pair("mirror",                     "ミラーの設定・焼き込み・対称化"),
            Pair("object.list",                "オブジェクトの追加・削除・複製・並べ替え・親子・統合"),
            Pair("object.attribute",           "オブジェクト単位の属性（表示・ロック・名前・原点など）"),
            Pair("object.group",               "オブジェクトグループの管理"),
            Pair("object.model",               "モデルの切替・削除・初期化"),
            Pair("rig.skeleton",               "ボーンの階層操作"),
            Pair("rig.skeleton.pose",          "ポーズ・バインドポーズ・T ポーズ"),
            Pair("rig.skeleton.humanoid",      "Humanoid 割当・可動域・リターゲット"),
            Pair("rig.skinning",               "スキンウェイトと種別変換"),
            Pair("dynamics.spring",            "スプリングボーンとコライダ"),
            Pair("morph",                      "モーフの作成・編集・プレビュー"),
            Pair("animation",                  "モーション・クリップの書き出しと変換"),
            Pair("io.import",                  "取り込み"),
            Pair("io.export",                  "書き出し"),
            Pair("io.project",                 "プロジェクトの保存・読み込み"),
            Pair("vrm",                        "VRM 固有の設定（メタ・視線・一人称）"),
            Pair("scenario",                   "手本（シナリオ）の作成・実行・記録"),
            Pair("mcp",                        "利用シーン・版・監査など AI 連携のための照会"),
            Pair("ui",                         "UI 自動操作と画面更新の通知"),
            Pair("tool",                       "ツール面（汎用のパラメータ設定と起動）"),
            Pair("undo",                       "取り消し・やり直し"),
            Pair("verify",                     "実装検証用（利用者向けではない）"),
        };

        private static KeyValuePair<string, string> Pair(string k, string v) => new KeyValuePair<string, string>(k, v);

        private static HashSet<string> _names;

        /// <summary>正典にある分類か。大小文字は区別する（属性に書く綴りを揃えるため）。</summary>
        public static bool IsKnown(string category)
        {
            if (string.IsNullOrEmpty(category)) return false;
            if (_names == null)
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kv in All) set.Add(kv.Key);
                _names = set;
            }
            return _names.Contains(category);
        }

        /// <summary>
        /// 利用シーンの categories に書ける値か。正典の分類そのもの、またはその上位の区切り
        /// （"geometry" や "geometry.create"）なら有効。
        /// </summary>
        public static bool IsValidRequest(string requested)
        {
            if (string.IsNullOrEmpty(requested)) return false;
            foreach (var kv in All)
                if (SceneDefinition.CategoryMatches(kv.Key, requested)) return true;
            return false;
        }
    }
}
