// PLDiag.cs
// 診断ログの入口を1箇所にまとめる。
//
// 【方針】
//   ・Console にだけ出す。ファイルへは書かない。
//     （操作のたびにファイルが増える ReorderDiagLog はこれに置き換えて廃止した）
//   ・既定で有効。コードを書き換えずにそのまま採取できる。
//     止めたいカテゴリだけ false にする。
//   ・出力は「どの操作 → どのコマンド → どの通知 → どの再構築経路 → 何が変わったか」を
//     1本の流れで追えることを目的とする。個々の関数の実行報告は出さない。
//
// 【カテゴリ】
//   Command  [PL/Cmd]      PlayerCommandDispatcher.Dispatch に来たコマンド
//   Notify   [PL/Notify]   NotifyPanels の ChangeKind と、選んだ描画更新経路
//   Viewport [PL/Viewport] EnterTopologyChanged / EnterSelectionChanged の実際の入口
//   Attr     [PL/Attr]     メッシュ属性の変更（前後値）
//   Undo     [PL/Undo]     Undo スタックへ積んだレコード
//
// Runtime/Poly_Ling_Main/Core/Misc/ に配置

using UnityEngine;

namespace Poly_Ling.Diagnostics
{
    public static class PLDiag
    {
        // ================================================================
        // スイッチ
        // ================================================================

        /// <summary>診断ログ全体の有効/無効。false ならカテゴリ設定に関わらず何も出ない。</summary>
        public static bool Enabled = true;

        public static bool Command  = true;
        public static bool Notify   = true;
        public static bool Viewport = true;
        public static bool Attr     = true;
        public static bool Undo     = true;

        /// <summary>全カテゴリをまとめて切り替える。</summary>
        public static void SetAll(bool on)
        {
            Enabled  = on;
            Command  = on;
            Notify   = on;
            Viewport = on;
            Attr     = on;
            Undo     = on;
        }

        // ================================================================
        // 出力
        // ================================================================

        /// <summary>Dispatch に来たコマンド。1コマンドにつき1行。</summary>
        public static void Cmd(string text)
        {
            if (!Enabled || !Command) return;
            Debug.Log("[PL/Cmd] " + text);
        }

        /// <summary>NotifyPanels の ChangeKind と、選んだ描画更新経路。</summary>
        public static void NotifyKind(string kind, string route)
        {
            if (!Enabled || !Notify) return;
            Debug.Log($"[PL/Notify] kind={kind} route={route}");
        }

        /// <summary>
        /// 描画更新の入口。NotifyPanels 以外から直接呼ばれた場合もここで捕まる。
        /// caller には呼び出し元の識別名を渡す。
        /// </summary>
        public static void ViewportEnter(string entry, string caller)
        {
            if (!Enabled || !Viewport) return;
            Debug.Log($"[PL/Viewport] {entry} from={caller}");
        }

        /// <summary>メッシュ属性の変更。前後の値を必ず入れる。</summary>
        public static void AttrChange(string what, int index, string name, string before, string after)
        {
            if (!Enabled || !Attr) return;
            Debug.Log($"[PL/Attr] {what} idx={index} name=\"{name}\" {before} -> {after}");
        }

        /// <summary>まとめて変更したときの件数。個々の行は AttrChange が出す。</summary>
        public static void AttrBatch(string what, int count, string value)
        {
            if (!Enabled || !Attr) return;
            Debug.Log($"[PL/Attr] {what} batch count={count} value={value}");
        }

        /// <summary>Undo スタックへ積んだレコード。</summary>
        public static void UndoRecord(string stack, string desc, object record)
        {
            if (!Enabled || !Undo) return;
            Debug.Log($"[PL/Undo] {stack} desc=\"{desc}\" type={(record?.GetType().Name ?? "<null>")}");
        }

        // ================================================================
        // 整形補助
        // ================================================================

        /// <summary>int 配列を "1,2,3" 形式にする。長い場合は先頭だけ出して件数を添える。</summary>
        public static string Ids(System.Collections.Generic.IReadOnlyList<int> ids, int max = 16)
        {
            if (ids == null || ids.Count == 0) return "[]";
            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            int n = ids.Count < max ? ids.Count : max;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ids[i]);
            }
            if (ids.Count > n) sb.Append(",... x").Append(ids.Count);
            sb.Append(']');
            return sb.ToString();
        }
    }
}
