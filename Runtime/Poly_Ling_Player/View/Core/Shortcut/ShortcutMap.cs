// ShortcutMap.cs
// ショートカット対応表: 「キー組 (ShortcutBinding) → コマンドID (string)」。
//
// 【対応表の優先順位: CSV 優先】
// - CSV に有効行が 1 行以上あれば、その CSV が唯一の対応表になる。
//   焼き込みの既定表 (CreateDefault) は LoadCsv 内で Clear() され、一切残らない。
//   → CSV に書かれていないコマンドはキー割当なし (無効) になる。これは仕様。
// - CSV が無い / 有効行 0 行 / 読み取り失敗のときだけ、焼き込みの既定表
//   (CreateDefault) を使う。ここを見れば既定割当が全て分かる。
// - つまり実効割当は「CSV があれば CSV だけ」「無ければコードだけ」であり、
//   両方が混ざることはない (マージしていた旧仕様では、CSV で消せない焼き込み
//   割当が残り、特に 2キー連続のプレフィックスが単発割当を食い潰していた)。
// - コマンドID の実行内容 (Action) は PlayerShortcutController 側で登録する
//   (コマンド実体は ViewerCore にあるため、対応表と実行を分離している)。
//
// Runtime/Poly_Ling_Player/View/Core/Shortcut/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Poly_Ling.Player
{
    public class ShortcutMap
    {
        // ----------------------------------------------------------------
        // コマンドID 定数
        // PlayerShortcutController.Register(...) の第1引数と対応させる。
        // ----------------------------------------------------------------
        public const string CmdUndo           = "edit.undo";
        public const string CmdRedo           = "edit.redo";
        public const string CmdToolVertexMove = "tool.vertexMove";
        public const string CmdToolObjectMove = "tool.objectMove";
        public const string CmdToolSculpt     = "tool.sculpt";
        public const string CmdToolAdvSelect  = "tool.advancedSelect";

        // 回転 / 拡大縮小。ShowRotatePanel / ShowScalePanel と同じ処理
        // (ShowCategory1Panel + ハンドラ Activate) を割り当てる。
        public const string CmdToolRotate     = "tool.rotate";
        public const string CmdToolScale      = "tool.scale";

        // 一時サブツール (ショートカットで進入し、1 回の操作確定で直前のツールへ戻る)。
        public const string CmdSubToolBoxSelect   = "subtool.boxSelect";
        public const string CmdSubToolLassoSelect = "subtool.lassoSelect";

        // 選択削除サブツール。マウス操作を伴わない即時実行なので、矩形/投げ縄と違い
        // InteractionMode の退避・復元は行わない (ViewerCore 側のコメント参照)。
        // 既定割当は Delete キー (D は面削除モードに使う)。
        public const string CmdSubToolDelete      = "subtool.delete";

        // 面削除モード。進入後は面のクリックのみを受け付け、クリックされた面を即削除する。
        // 矩形/投げ縄選択と面以外のホバーは無効。Escape または他ツール選択で抜ける。
        public const string CmdToolDeleteFace     = "tool.deleteFace";

        // 選択頂点の結合。どちらもモードを変えない即時実行の単発コマンド。
        //   Centroid  … 距離を見ず、選択頂点を 1 点（重心）へ結合
        //   Threshold … 選択頂点のうち、しきい値以下の距離にあるものだけを結合
        public const string CmdMergeVerticesCentroid  = "edit.mergeVerticesCentroid";
        public const string CmdMergeVerticesThreshold = "edit.mergeVerticesThreshold";

        // 右ペインのオブジェクトリストを開く。
        public const string CmdPanelMeshList      = "panel.meshList";

        // 図形生成 (サブメニューを開くだけ)。2キー連続で使う。
        public const string CmdShapeCube       = "shape.cube";
        public const string CmdShapeSphere     = "shape.sphere";
        public const string CmdShapeCylinder   = "shape.cylinder";
        public const string CmdShapeCapsule    = "shape.capsule";
        public const string CmdShapePlane      = "shape.plane";
        public const string CmdShapePyramid    = "shape.pyramid";
        public const string CmdShapeRevolution = "shape.revolution";
        public const string CmdShapeProfile2D  = "shape.profile2d";
        public const string CmdShapeNohMask    = "shape.nohmask";
        public const string CmdShapeFrill       = "shape.frill";
        public const string CmdShapePipe        = "shape.pipe";
        public const string CmdShapePlaceObject = "shape.placeObject";
        public const string CmdShapeObjectArray = "shape.objectArray";

        // 単発割当: キー組 → コマンドID
        private readonly Dictionary<ShortcutBinding, string> _map = new();

        // 2キー連続割当: (1キー目, 2キー目) → コマンドID
        private readonly Dictionary<(ShortcutBinding First, ShortcutBinding Second), string> _sequence = new();

        // 連続の 1キー目 (プレフィックス) 集合。OnKeyDown での高速判定用。
        private readonly HashSet<ShortcutBinding> _prefixes = new();

        public IReadOnlyDictionary<ShortcutBinding, string> Entries => _map;

        public int SingleCount   => _map.Count;
        public int SequenceCount => _sequence.Count;

        /// <summary>
        /// 既定 CSV パス: &lt;persistentDataPath&gt;/PolyLing/keymap.csv。
        /// このファイルがあれば起動時に読み込む。
        /// </summary>
        public static string DefaultCsvPath
            => Path.Combine(Application.persistentDataPath, "PolyLing", "keymap.csv");

        // ----------------------------------------------------------------
        // デフォルト対応表 (1 行 1 割当。ここが既定の一覧)
        // ----------------------------------------------------------------
        public static ShortcutMap CreateDefault()
        {
            var m = new ShortcutMap();
            //      Key            Ctrl   Shift  Alt    CommandId
            m.Set(KeyCode.Z, true,  false, false, CmdUndo);           // Ctrl+Z       : 元に戻す
            m.Set(KeyCode.Y, true,  false, false, CmdRedo);           // Ctrl+Y       : やり直し
            m.Set(KeyCode.Z, true,  true,  false, CmdRedo);           // Ctrl+Shift+Z : やり直し
            m.Set(KeyCode.V, false, false, false, CmdToolVertexMove); // V            : 頂点移動ツール
            m.Set(KeyCode.B, false, false, false, CmdToolObjectMove); // B            : オブジェクト移動ツール
            m.Set(KeyCode.S, false, false, false, CmdToolSculpt);     // S            : スカルプトツール
            m.Set(KeyCode.A, false, false, false, CmdToolAdvSelect);  // A            : 高度な選択ツール
            m.Set(KeyCode.C, false, false, false, CmdToolRotate);     // C            : 回転ツール
            m.Set(KeyCode.Q, false, false, false, CmdToolScale);      // Q            : 拡大縮小ツール
            m.Set(KeyCode.R, false, false, false, CmdSubToolBoxSelect);   // R : 矩形選択サブツール (一時)
            m.Set(KeyCode.G, false, false, false, CmdSubToolLassoSelect); // G : 投げ縄選択サブツール (一時)
            m.Set(KeyCode.Delete, false, false, false, CmdSubToolDelete); // Delete : 選択削除サブツール
            m.Set(KeyCode.D, false, false, false, CmdToolDeleteFace);     // D      : 面削除モード
            m.Set(KeyCode.J, true,  false, false, CmdMergeVerticesCentroid);  // Ctrl+J       : 選択頂点を重心へ結合
            m.Set(KeyCode.J, true,  true,  false, CmdMergeVerticesThreshold); // Ctrl+Shift+J : しきい値で結合
            m.Set(KeyCode.O, true,  false, false, CmdPanelMeshList);          // Ctrl+O       : オブジェクトリスト

            // 図形生成: プレフィックス P を押してから形状キー (サブメニューを開くだけ)。
            //   例) P → C = 立方体。P の後の 2キー目は上の単発割当とは独立
            //   (単発 R / G / S / A と P R / P S / P A は衝突しない)。
            //   プレフィックスは G から P へ変更した。G は投げ縄サブツールの単発割当に使う。
            //   OnKeyDown はプレフィックス判定を単発判定より先に行うため、同一キーを
            //   両方へ割り当てると単発側が発火しない。
            var p = NoMod(KeyCode.P);
            m.SetSequence(p, NoMod(KeyCode.C), CmdShapeCube);       // P C : Cube
            m.SetSequence(p, NoMod(KeyCode.S), CmdShapeSphere);     // P S : Sphere
            m.SetSequence(p, NoMod(KeyCode.Y), CmdShapeCylinder);   // P Y : Cylinder
            m.SetSequence(p, NoMod(KeyCode.A), CmdShapeCapsule);    // P A : Capsule
            m.SetSequence(p, NoMod(KeyCode.L), CmdShapePlane);      // P L : Plane
            m.SetSequence(p, NoMod(KeyCode.P), CmdShapePyramid);    // P P : Pyramid
            m.SetSequence(p, NoMod(KeyCode.R), CmdShapeRevolution); // P R : Revolution
            m.SetSequence(p, NoMod(KeyCode.F), CmdShapeProfile2D);  // P F : Profile2D
            m.SetSequence(p, NoMod(KeyCode.N), CmdShapeNohMask);    // P N : NohMask
            // 2キー目は「英名の先頭から、P 配下で未使用の最初の文字」で決めている。
            //   Frill       : F(Profile2D) / R(Revolution) 使用済 → I
            //   Pipe        : P(Pyramid) / I(Frill) 使用済       → E
            //   PlaceObject : P,L,A,C,E 使用済                   → O
            //   ObjectArray : O(PlaceObject) 使用済              → B
            m.SetSequence(p, NoMod(KeyCode.I), CmdShapeFrill);       // P I : Frill
            m.SetSequence(p, NoMod(KeyCode.E), CmdShapePipe);        // P E : Pipe
            m.SetSequence(p, NoMod(KeyCode.O), CmdShapePlaceObject); // P O : PlaceObject (接地)
            m.SetSequence(p, NoMod(KeyCode.B), CmdShapeObjectArray); // P B : ObjectArray (歪み複製)
            return m;
        }

        private static ShortcutBinding NoMod(KeyCode key)
            => new ShortcutBinding(key, false, false, false);

        // ---- 単発 ----
        public void Set(KeyCode key, bool ctrl, bool shift, bool alt, string commandId)
            => _map[new ShortcutBinding(key, ctrl, shift, alt)] = commandId;

        public bool TryGet(ShortcutBinding binding, out string commandId)
            => _map.TryGetValue(binding, out commandId);

        // ---- 2キー連続 ----
        public void SetSequence(ShortcutBinding first, ShortcutBinding second, string commandId)
        {
            _sequence[(first, second)] = commandId;
            _prefixes.Add(first);
        }

        public bool IsPrefix(ShortcutBinding first)
            => _prefixes.Contains(first);

        public bool TryGetSequence(ShortcutBinding first, ShortcutBinding second, out string commandId)
            => _sequence.TryGetValue((first, second), out commandId);

        public void Clear()
        {
            _map.Clear();
            _sequence.Clear();
            _prefixes.Clear();
        }

        // ----------------------------------------------------------------
        // CSV 読込 (CSV 優先。有効行があれば既定表を全て捨てて置き換える)
        //   形式: Command,Key,Ctrl,Shift,Alt[,Key2,Ctrl2,Shift2,Alt2]
        //     - 先頭 '#' の行と空行は無視
        //     - Key   : Unity KeyCode 名 (Z, V, F1 ...)
        //     - Ctrl/Shift/Alt : true / false (省略時は false)
        //     - Key2 が空/無し   → 単発割当
        //     - Key2 が指定あり → 2キー連続 (1キー目=Key…, 2キー目=Key2…)
        //
        //   【反映規則】
        //     - 有効行が 1 行以上 → Clear() で既定表を全破棄し、CSV の行だけを反映する。
        //       CSV に書かれていないコマンドはキー割当なし (無効) になる。これは仕様。
        //     - ファイル無し / 有効行 0 行 / 読み取り失敗 → 何も変更せず既定表を維持する
        //       (CSV が壊れて全ショートカットが死ぬのを防ぐ)。
        //     - 反映は全行パース後にまとめて行う。パースしながら Clear すると、
        //       途中の不正行で割当が半端な状態のまま残るため。
        //
        //   戻り値: 反映した行数 (既定表を維持した場合は 0)。
        // ----------------------------------------------------------------
        public int LoadCsv(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return 0;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ShortcutMap] CSV 読込失敗: {path} ({e.Message})");
                return 0;
            }

            // 有効行の退避先。Second が null なら単発、値ありなら 2キー連続。
            var parsed = new List<(string Cmd, ShortcutBinding First, ShortcutBinding? Second)>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var cols = line.Split(',');
                if (cols.Length < 2)
                {
                    Debug.LogWarning($"[ShortcutMap] 列不足 (行 {i + 1}): {line}");
                    continue;
                }

                string cmd = cols[0].Trim();
                if (cmd.Length == 0) continue;

                if (!Enum.TryParse(cols[1].Trim(), true, out KeyCode key))
                {
                    Debug.LogWarning($"[ShortcutMap] 不明な KeyCode (行 {i + 1}): {cols[1].Trim()}");
                    continue;
                }

                bool ctrl  = cols.Length > 2 && ParseBool(cols[2]);
                bool shift = cols.Length > 3 && ParseBool(cols[3]);
                bool alt   = cols.Length > 4 && ParseBool(cols[4]);

                // Key2 があれば 2キー連続、無ければ単発。
                if (cols.Length > 5 && cols[5].Trim().Length > 0)
                {
                    if (!Enum.TryParse(cols[5].Trim(), true, out KeyCode key2))
                    {
                        Debug.LogWarning($"[ShortcutMap] 不明な KeyCode (2キー目, 行 {i + 1}): {cols[5].Trim()}");
                        continue;
                    }
                    bool ctrl2  = cols.Length > 6 && ParseBool(cols[6]);
                    bool shift2 = cols.Length > 7 && ParseBool(cols[7]);
                    bool alt2   = cols.Length > 8 && ParseBool(cols[8]);

                    parsed.Add((
                        cmd,
                        new ShortcutBinding(key, ctrl, shift, alt),
                        new ShortcutBinding(key2, ctrl2, shift2, alt2)));
                }
                else
                {
                    parsed.Add((cmd, new ShortcutBinding(key, ctrl, shift, alt), null));
                }
            }

            // 有効行が 1 行も無ければ既定表を維持する (Clear しない)。
            if (parsed.Count == 0)
                return 0;

            // ここから CSV 優先。既定表 (CreateDefault の焼き込み) は全て破棄する。
            // _map / _sequence / _prefixes をまとめて空にするため、旧仕様で問題に
            // なっていた「CSV で消せない残留プレフィックス」も確実に消える。
            Clear();

            for (int i = 0; i < parsed.Count; i++)
            {
                var e = parsed[i];
                if (e.Second.HasValue)
                    SetSequence(e.First, e.Second.Value, e.Cmd);
                else
                    Set(e.First.Key, e.First.Ctrl, e.First.Shift, e.First.Alt, e.Cmd);
            }

            return parsed.Count;
        }

        private static bool ParseBool(string s)
        {
            s = s.Trim();
            return s.Equals("true", StringComparison.OrdinalIgnoreCase)
                || s == "1"
                || s.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
