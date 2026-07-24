// ShortcutMap.cs
// ショートカット対応表: 「キー組 (ShortcutBinding) → コマンドID (string)」。
//
// - デフォルトはハードコード (CreateDefault)。ここを見れば既定割当が全て分かる。
// - CSV があれば読み込み、同一キー組は上書き・新規は追加する。
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

            // 図形生成: プレフィックス G を押してから形状キー (サブメニューを開くだけ)。
            //   例) G → C = 立方体。G の後の 2キー目は上の単発割当とは独立。
            var g = NoMod(KeyCode.G);
            m.SetSequence(g, NoMod(KeyCode.C), CmdShapeCube);       // G C : Cube
            m.SetSequence(g, NoMod(KeyCode.S), CmdShapeSphere);     // G S : Sphere
            m.SetSequence(g, NoMod(KeyCode.Y), CmdShapeCylinder);   // G Y : Cylinder
            m.SetSequence(g, NoMod(KeyCode.A), CmdShapeCapsule);    // G A : Capsule
            m.SetSequence(g, NoMod(KeyCode.L), CmdShapePlane);      // G L : Plane
            m.SetSequence(g, NoMod(KeyCode.P), CmdShapePyramid);    // G P : Pyramid
            m.SetSequence(g, NoMod(KeyCode.R), CmdShapeRevolution); // G R : Revolution
            m.SetSequence(g, NoMod(KeyCode.F), CmdShapeProfile2D);  // G F : Profile2D
            m.SetSequence(g, NoMod(KeyCode.N), CmdShapeNohMask);    // G N : NohMask
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
        // CSV 読込
        //   形式: Command,Key,Ctrl,Shift,Alt[,Key2,Ctrl2,Shift2,Alt2]
        //     - 先頭 '#' の行と空行は無視
        //     - Key   : Unity KeyCode 名 (Z, V, F1 ...)
        //     - Ctrl/Shift/Alt : true / false (省略時は false)
        //     - Key2 が空/無し   → 従来の単発割当
        //     - Key2 が指定あり → 2キー連続 (1キー目=Key…, 2キー目=Key2…)
        //   読み込んだ行は同一キー組を上書き、無ければ追加する。
        //   戻り値: 反映した行数 (ファイルが無ければ 0)。
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

            int applied = 0;
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

                    SetSequence(
                        new ShortcutBinding(key, ctrl, shift, alt),
                        new ShortcutBinding(key2, ctrl2, shift2, alt2),
                        cmd);
                }
                else
                {
                    Set(key, ctrl, shift, alt, cmd);
                }
                applied++;
            }

            return applied;
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
