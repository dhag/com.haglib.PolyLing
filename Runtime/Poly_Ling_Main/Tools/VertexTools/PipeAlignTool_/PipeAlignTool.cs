// PipeAlignTool.cs
// パイプ群専用のツール「パイプの整列」。
// 実処理は PipeAlignOps / PipeSmoothOps。ここは対象の集約・Undo 記録・反映だけを担う。
// マウス入力を持たず、パネルの「開始」ボタンから TriggerExecute() で実行する。
// Runtime/Poly_Ling_Main/Tools/VertexTools/PipeAlignTool_/ に配置
//
// 【3 つのモード】
//   ・Auto   … パーツIDの昇順の端から順に対にして左右対称化する
//   ・Manual … 「元ID, 先ID」を列挙して左右対称化する
//   ・Smooth … パイプ列に沿った重み付き平均でスムージングする
//
// 【対象】選択中の描画オブジェクト全部（マルチセレクト対応）。
//   オブジェクトごとに独立して処理する。頂点選択は使わない。
//   ボーン表示用メッシュは編集対象外。
//
// 【Undo】複数メッシュの頂点座標だけを書き換えるため、
//   MultiMeshVertexSnapshot / MultiMeshVertexSnapshotRecord を MeshListStack へ
//   1 件だけ記録する（メッシュが何個でも Undo は1手）。
//
// 【反映】位相は変わらないので OnTopologyChanged() は使わない。
//   InvalidatePositionCache() → SyncMeshContextPositionsOnly → Repaint。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    /// <summary>ツールの動作モード。</summary>
    public enum PipeAlignMode
    {
        /// <summary>パーツIDの昇順の端から順に対にする。</summary>
        Auto = 0,

        /// <summary>ペアを手で列挙する。</summary>
        Manual = 1,

        /// <summary>パイプ列に沿った重み付き平均でスムージングする。</summary>
        Smooth = 2,
    }

    /// <summary>パイプ群専用の左右対称化・スムージングツール。</summary>
    public class PipeAlignTool : IEditTool
    {
        public string Name        => "PipeAlign";
        public string DisplayName => "Pipe Align";

        // ================================================================
        // 設定
        // ================================================================

        private PipeAlignSettings _settings = new PipeAlignSettings();
        public IToolSettings Settings => _settings;

        /// <summary>動作モード。</summary>
        public PipeAlignMode Mode { get => _settings.Mode; set => _settings.Mode = value; }

        /// <summary>1 段の頂点数 M。</summary>
        public int RingVertexCount
        {
            get => _settings.RingVertexCount;
            set => _settings.RingVertexCount = value;
        }

        /// <summary>開始側が先端頂点で閉じているか。</summary>
        public bool CapStart { get => _settings.CapStart; set => _settings.CapStart = value; }

        /// <summary>終了側が先端頂点で閉じているか。</summary>
        public bool CapEnd { get => _settings.CapEnd; set => _settings.CapEnd = value; }

        /// <summary>コピーの向き。</summary>
        public PipeAlignDirection Direction
        {
            get => _settings.Direction;
            set => _settings.Direction = value;
        }

        /// <summary>手動ペアの指定（1 行 1 エントリ）。</summary>
        public string PairText { get => _settings.PairText; set => _settings.PairText = value; }

        /// <summary>スムージングの重み（個数は奇数）。</summary>
        public string WeightText { get => _settings.WeightText; set => _settings.WeightText = value; }

        /// <summary>スムージング対象のパーツID。空欄なら全パーツ。</summary>
        public string TargetText { get => _settings.TargetText; set => _settings.TargetText = value; }

        /// <summary>スムージングで窓が外へ出るときの扱い。</summary>
        public PipeSmoothEdgeMode EdgeMode
        {
            get => _settings.EdgeMode;
            set => _settings.EdgeMode = value;
        }

        // ================================================================
        // 実行結果（SubPanel 表示用）
        // ================================================================

        /// <summary>直前の実行結果の文言。</summary>
        public string LastResult { get; private set; } = "";

        /// <summary>対象になる描画オブジェクトの数。</summary>
        public int TargetMeshCount => EnumerateTargets().Count;

        // ================================================================
        // コンテキスト
        // ================================================================

        private ToolContext _context;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)                => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)                  => false;

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)   { _context = ctx; }
        public void OnDeactivate(ToolContext ctx) { _context = null; }

        public void Reset()
        {
            _settings.Mode            = PipeAlignMode.Auto;
            _settings.RingVertexCount = 6;
            _settings.CapStart        = true;
            _settings.CapEnd          = true;
            _settings.Direction       = PipeAlignDirection.PlusToMinus;
            _settings.PairText        = "";
            _settings.WeightText      = "1,2,4,2,1";
            _settings.TargetText      = "";
            _settings.EdgeMode        = PipeSmoothEdgeMode.Skip;
            LastResult                = "";
        }

        // ================================================================
        // 対象の集約
        // ================================================================

        /// <summary>1 メッシュぶんの対象。</summary>
        public struct AlignTarget
        {
            public int         MeshIndex;
            public MeshContext MeshContext;
        }

        /// <summary>
        /// 選択中の描画オブジェクトを走査する。ボーン表示用メッシュは編集対象外。
        /// </summary>
        private List<AlignTarget> EnumerateTargets()
        {
            var list = new List<AlignTarget>();

            var model = _context?.Model;
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;

                list.Add(new AlignTarget { MeshIndex = idx, MeshContext = mc });
            }

            return list;
        }

        // ================================================================
        // 公開 API（SubPanel / Handler から呼び出し）
        // ================================================================

        public void TriggerExecute() => Execute();

        // ================================================================
        // 実行
        // ================================================================

        private void Execute()
        {
            var model   = _context?.Model;
            var targets = EnumerateTargets();

            if (model == null || targets.Count == 0)
            {
                LastResult = "対象オブジェクトがありません";
                Debug.LogWarning($"[PipeAlignTool] 実行中止: {LastResult}");
                return;
            }

            // モードごとの入力を先に読む。ここで落ちたら 1 メッシュも触らない。
            List<PipePair> pairs      = null;
            List<float>    weights    = null;
            HashSet<int>   smoothIds  = null;

            if (_settings.Mode == PipeAlignMode.Manual)
            {
                if (!PipeAlignOps.ParsePairs(_settings.PairText, out pairs, out string perr))
                {
                    LastResult = $"ペアの指定が読めません: {perr}";
                    Debug.LogWarning($"[PipeAlignTool] {LastResult}");
                    return;
                }
            }
            else if (_settings.Mode == PipeAlignMode.Smooth)
            {
                if (!PipeSmoothOps.ParseWeights(_settings.WeightText, out weights, out string werr))
                {
                    LastResult = $"重みの指定が読めません: {werr}";
                    Debug.LogWarning($"[PipeAlignTool] {LastResult}");
                    return;
                }
                if (!PipeSmoothOps.ParseTargets(_settings.TargetText, out smoothIds, out string terr))
                {
                    LastResult = $"対象パーツの指定が読めません: {terr}";
                    Debug.LogWarning($"[PipeAlignTool] {LastResult}");
                    return;
                }
            }

            var undo   = _context.UndoController;
            var before = undo != null ? MultiMeshVertexSnapshot.Capture(model) : null;

            int okMeshes   = 0;
            int partTotal  = 0;
            int movedTotal = 0;
            var failures   = new List<string>();

            foreach (var t in targets)
            {
                bool   ok;
                int    parts;
                int    moved;
                string reason;

                switch (_settings.Mode)
                {
                    case PipeAlignMode.Manual:
                        ok = PipeAlignOps.ExecuteManualPairs(
                            t.MeshContext.MeshObject,
                            _settings.RingVertexCount, _settings.CapStart, _settings.CapEnd,
                            _settings.Direction, pairs,
                            out parts, out moved, out reason);
                        break;

                    case PipeAlignMode.Smooth:
                        ok = PipeSmoothOps.Execute(
                            t.MeshContext.MeshObject,
                            weights, smoothIds, _settings.EdgeMode,
                            out parts, out moved, out reason);
                        break;

                    default:
                        ok = PipeAlignOps.Execute(
                            t.MeshContext.MeshObject,
                            _settings.RingVertexCount, _settings.CapStart, _settings.CapEnd,
                            _settings.Direction,
                            out parts, out moved, out reason);
                        break;
                }

                if (!ok)
                {
                    failures.Add($"{t.MeshContext.Name}: {reason}");
                    Debug.LogWarning($"[PipeAlignTool] {t.MeshContext.Name}: {reason}");
                    continue;
                }

                okMeshes   ++;
                partTotal  += parts;
                movedTotal += moved;

                t.MeshContext.MeshObject.InvalidatePositionCache();
                _context.SyncMeshContextPositionsOnly?.Invoke(t.MeshContext);
            }

            if (okMeshes == 0)
            {
                LastResult = failures.Count > 0 ? string.Join(" / ", failures) : "対象がありません";
                return;
            }

            if (undo != null)
            {
                var after = MultiMeshVertexSnapshot.Capture(model);

                // MeshListStack の Context を今回のモデルに合わせる（Undo 時の復元先）。
                undo.SetModelContext(model);

                string desc = $"{ModeLabel(_settings.Mode)} ({okMeshes} objs / {partTotal} parts / {movedTotal} verts)";
                var record  = new MultiMeshVertexSnapshotRecord(before, after, desc);
                PLDiag.UndoRecord("MeshList", desc, record);
                undo.MeshListStack.Record(record, desc);
            }

            _context.Repaint?.Invoke();

            string unit = _settings.Mode == PipeAlignMode.Smooth ? "スムージング" : "書き換え";
            LastResult = $"完了: オブジェクト {okMeshes} / {unit}パーツ {partTotal} / 移動 {movedTotal} 頂点";
            if (failures.Count > 0)
                LastResult += $"（除外 {failures.Count} 件）";

            Debug.Log($"[PipeAlignTool] {LastResult}");
        }

        private static string ModeLabel(PipeAlignMode mode)
        {
            switch (mode)
            {
                case PipeAlignMode.Manual: return "Pipe Align (manual)";
                case PipeAlignMode.Smooth: return "Pipe Smooth";
                default:                   return "Pipe Align";
            }
        }
    }

    // ================================================================
    // 設定クラス
    // ================================================================

    public class PipeAlignSettings : IToolSettings
    {
        /// <summary>動作モード。</summary>
        public PipeAlignMode Mode = PipeAlignMode.Auto;

        /// <summary>1 段の頂点数 M。</summary>
        public int RingVertexCount = 6;

        /// <summary>開始側が先端頂点で閉じているか。</summary>
        public bool CapStart = true;

        /// <summary>終了側が先端頂点で閉じているか。</summary>
        public bool CapEnd = true;

        /// <summary>コピーの向き。</summary>
        public PipeAlignDirection Direction = PipeAlignDirection.PlusToMinus;

        /// <summary>手動ペアの指定（1 行 1 エントリ）。</summary>
        public string PairText = "";

        /// <summary>スムージングの重み（個数は奇数）。</summary>
        public string WeightText = "1,2,4,2,1";

        /// <summary>スムージング対象のパーツID。空欄なら全パーツ。</summary>
        public string TargetText = "";

        /// <summary>スムージングで窓が外へ出るときの扱い。</summary>
        public PipeSmoothEdgeMode EdgeMode = PipeSmoothEdgeMode.Skip;

        public IToolSettings Clone() => new PipeAlignSettings
        {
            Mode            = Mode,
            RingVertexCount = RingVertexCount,
            CapStart        = CapStart,
            CapEnd          = CapEnd,
            Direction       = Direction,
            PairText        = PairText,
            WeightText      = WeightText,
            TargetText      = TargetText,
            EdgeMode        = EdgeMode,
        };

        public void CopyFrom(IToolSettings other)
        {
            if (other is PipeAlignSettings s)
            {
                Mode            = s.Mode;
                RingVertexCount = s.RingVertexCount;
                CapStart        = s.CapStart;
                CapEnd          = s.CapEnd;
                Direction       = s.Direction;
                PairText        = s.PairText;
                WeightText      = s.WeightText;
                TargetText      = s.TargetText;
                EdgeMode        = s.EdgeMode;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is PipeAlignSettings s)
                return Mode            != s.Mode
                    || RingVertexCount != s.RingVertexCount
                    || CapStart        != s.CapStart
                    || CapEnd          != s.CapEnd
                    || Direction       != s.Direction
                    || PairText        != s.PairText
                    || WeightText      != s.WeightText
                    || TargetText      != s.TargetText
                    || EdgeMode        != s.EdgeMode;
            return true;
        }
    }
}
