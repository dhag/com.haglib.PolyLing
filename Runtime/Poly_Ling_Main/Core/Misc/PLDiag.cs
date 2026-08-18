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
//   Pick     [PL/Pick]     ホバー／ピック／頂点ドラッグの時系列（下記「Pick リング」参照）
//
// 【Pick リング】
//   稀にしか再現しない不具合（ホバー色と選択の食い違い、選択と同時の移動が
//   画面に反映されない）を長期採取するための仕組み。
//
//   ・PickRec / PickHover は Pick スイッチに関わらず「常に」リングバッファへ記録する。
//     1 件は構造体で、文字列生成はダンプ時にしか行わない（常時記録でも軽い）。
//   ・Pick == true のときだけ、記録と同時に 1 行ずつ Console へも出す（verbose）。
//   ・PickDump は異常検出時に呼ぶ。リングの中身をまとめて 1 回吐く。
//     同一 reason の連続ダンプは PickDumpCooldownFrames ぶん抑制し、
//     抑制した件数だけを次回ダンプの先頭に添える。
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

        /// <summary>
        /// Pick リングの verbose 出力。既定 false。
        ///
        /// false でも PickRec / PickHover のリング記録と PickDump の自動ダンプは動く。
        /// true にすると 1 件ごとに Console へ出るため、再現手順が判っているときだけ使う。
        /// Player 左ペインの「診断ログ（ピック／移動）」トグルから切り替える。
        /// </summary>
        public static bool Pick = false;

        /// <summary>全カテゴリをまとめて切り替える。</summary>
        public static void SetAll(bool on)
        {
            Enabled  = on;
            Command  = on;
            Notify   = on;
            Viewport = on;
            Attr     = on;
            Undo     = on;
            Pick     = on;
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
        // Pick リング
        // ================================================================

        /// <summary>
        /// Pick リングの 1 件。フィールドの意味はタグごとに異なるため、
        /// 記録側のコメントと下表を突き合わせて読むこと。
        ///
        ///  Tag              A            B            C            D            E            F        X,Y,Z
        ///  "IA.Down"        button       -            -            -            -            -        panelLocalPos(押下位置)
        ///  "Hover"          hoverV前     hoverV後     hoverL前     hoverL後     hoverF前     hoverF後  panelLocalPos
        ///  "HoverPos"       hoverV       dx(整数)     dy(整数)     -            -            -        頂点スクリーン座標X,Y / Z=距離px
        ///  "Present"        hoverV前     hoverV後     hoverL前     hoverL後     hoverF前     hoverF後  -
        ///  "IA.ButtonDown"  HasHit       MeshIndex    VertexIndex  -            -            -        screenPos(押下位置)
        ///  "IA.Click"       HasHit       MeshIndex    VertexIndex  -            -            -        screenPos
        ///  "MTH.Press"      Kind         MeshIndex    VertexIndex  EdgeV1       EdgeV2       FaceIndex 押下位置
        ///  "MTH.PressBegin" transform数  影響メッシュ数 影響頂点数   -            -            -        -
        ///  "MTH.DragBeginPress" Kind     MeshIndex    VertexIndex  transform数  影響メッシュ数 影響頂点数 押下位置
        ///  "MTH.Cancel"     transform数  移動済み?    選択変更済み? -            -            -        screenPos
        ///  "IA.DragBegin"   HasHit       MeshIndex    VertexIndex  -            -            -        screenPos(=押下位置)
        ///  "MTH.Click"      Kind         MeshIndex    VertexIndex  選択メッシュ数 選択要素数(前) 選択要素数(後) screenPos
        ///  "MTH.DragBegin"  Kind         MeshIndex    VertexIndex  EdgeV1       EdgeV2       FaceIndex _mouseDownPos
        ///  "MTH.Pending"    Kind         MeshIndex    既に選択?    選択メッシュ数 影響メッシュ数 影響頂点数 screenPos
        ///  "MTH.BeginMove"  transform数  影響メッシュ数 影響頂点数   -            -            -        -
        ///  "MTH.ApplyDelta" transform数  -            -            -            -            -        worldDelta
        ///  "VMoved"         phase        syncMc有無   -            -            -            -        -
        ///
        ///  "IA.Down" の直後に来る "Hover" が「押下位置のホバー」。
        ///  "Present" は PresentAll 内の UpdateFrame 前後。ポインタ移動を伴わずに
        ///  前後が変わっていれば、キャッシュされたマウス位置での再ヒットテストで
        ///  ホバーが入れ替わったことを意味する。
        /// </summary>
        public struct PickEntry
        {
            public int    Frame;
            public string Tag;
            public int    A, B, C, D, E, F;
            public float  X, Y, Z;
        }

        /// <summary>リング容量。1 操作の時系列が収まる長さにしてある。</summary>
        public const int PickRingCapacity = 256;

        /// <summary>同一 reason のダンプを抑制するフレーム数。</summary>
        public const int PickDumpCooldownFrames = 600;

        private static readonly PickEntry[] _pickRing = new PickEntry[PickRingCapacity];

        /// <summary>リングへの総書き込み数。書き込み位置は _pickWriteCount % PickRingCapacity。</summary>
        private static int _pickWriteCount;

        private static string _lastDumpReason;
        private static int    _lastDumpFrame = int.MinValue;
        private static int    _suppressedDumps;

        // ---- 直近の "Hover" 記録（ダンプ条件の判定に使う） ----

        /// <summary>直近 "Hover" を記録したフレーム。未記録は int.MinValue。</summary>
        public static int LastHoverFrame = int.MinValue;

        public static int LastHoverBeforeVertex = -1;
        public static int LastHoverAfterVertex  = -1;
        public static int LastHoverBeforeLine   = -1;
        public static int LastHoverAfterLine    = -1;
        public static int LastHoverBeforeFace   = -1;
        public static int LastHoverAfterFace    = -1;

        /// <summary>
        /// 直近の "Hover" 記録で、ホバー要素が同一フレーム内に入れ替わったか。
        /// ドラッグ開始イベントと同じフレームでこれが true なら、
        /// 「押下時に見えていた色」と「掴んだ要素」が食い違いうる状態だったことになる。
        /// </summary>
        public static bool LastHoverChanged =>
               LastHoverBeforeVertex != LastHoverAfterVertex
            || LastHoverBeforeLine   != LastHoverAfterLine
            || LastHoverBeforeFace   != LastHoverAfterFace;

        /// <summary>
        /// Pick リングへ 1 件記録する。Pick == true のときは Console へも出す。
        /// タグは必ずリテラルを渡すこと（文字列生成をここで発生させない）。
        /// </summary>
        public static void PickRec(
            string tag,
            int a = 0, int b = 0, int c = 0, int d = 0, int e = 0, int f = 0,
            float x = 0f, float y = 0f, float z = 0f)
        {
            int slot = _pickWriteCount % PickRingCapacity;
            _pickRing[slot] = new PickEntry
            {
                Frame = Time.frameCount,
                Tag   = tag,
                A = a, B = b, C = c, D = d, E = e, F = f,
                X = x, Y = y, Z = z,
            };
            _pickWriteCount++;

            if (Pick) Debug.Log("[PL/Pick] " + FormatPick(_pickRing[slot]));
        }

        /// <summary>
        /// ホバー再計算の前後値を記録する。
        /// UpdateFrame の直前と直後の hover インデックスを渡すこと。
        /// LastHover* も同時に更新する。
        /// </summary>
        public static void PickHover(
            int beforeV, int afterV,
            int beforeL, int afterL,
            int beforeF, int afterF,
            Vector2 panelLocalPos)
        {
            LastHoverFrame        = Time.frameCount;
            LastHoverBeforeVertex = beforeV;
            LastHoverAfterVertex  = afterV;
            LastHoverBeforeLine   = beforeL;
            LastHoverAfterLine    = afterL;
            LastHoverBeforeFace   = beforeF;
            LastHoverAfterFace    = afterF;

            PickRec("Hover", beforeV, afterV, beforeL, afterL, beforeF, afterF,
                    panelLocalPos.x, panelLocalPos.y);
        }

        /// <summary>
        /// リングの中身を Console へまとめて吐く。異常を検出した箇所から呼ぶ。
        /// 同一 reason の連続呼び出しは PickDumpCooldownFrames ぶん抑制する。
        /// </summary>
        public static void PickDump(string reason)
        {
            int frame = Time.frameCount;
            if (reason == _lastDumpReason &&
                frame - _lastDumpFrame < PickDumpCooldownFrames)
            {
                _suppressedDumps++;
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("[PL/Pick] ==== DUMP reason=").Append(reason)
              .Append(" frame=").Append(frame);
            if (_suppressedDumps > 0)
                sb.Append(" (suppressed=").Append(_suppressedDumps).Append(')');
            sb.Append(" ====");

            int total = _pickWriteCount;
            int count = total < PickRingCapacity ? total : PickRingCapacity;
            int start = total - count;
            for (int i = 0; i < count; i++)
            {
                var entry = _pickRing[(start + i) % PickRingCapacity];
                sb.Append('\n').Append(FormatPick(entry));
            }
            Debug.Log(sb.ToString());

            _lastDumpReason  = reason;
            _lastDumpFrame   = frame;
            _suppressedDumps = 0;
        }

        private static string FormatPick(PickEntry e)
        {
            return $"f={e.Frame} {e.Tag} " +
                   $"a={e.A} b={e.B} c={e.C} d={e.D} e={e.E} f={e.F} " +
                   $"xyz=({e.X:F6},{e.Y:F6},{e.Z:F6})";
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
