// PlayerVertexIdSubPanel.cs
// 頂点IDユーティリティ（診断 + 修復）。
//
// 【位置づけ】
//   頂点IDはモデル間・オブジェクト間で「同じ頂点」を突き合わせる唯一の手掛かりだが、
//   実運用では信頼できない状態になりやすい:
//     - 他所製 PMX は頂点IDを持たない（PolyLing が書き出した PMX だけが復元される）
//     - 特殊面を持たない MQO は全頂点が -1 のまま入ってくる
//     - 後から追加した頂点だけIDが無い / コピーで重複した、など混在も起きる
//   ID を使う操作の前に、まずここで状態を数字で確認し、必要なら整える。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerVertexIdSubPanel
    {
        public Func<ProjectContext>  GetView;
        public Action<PanelCommand>  SendCommand;

        private VisualElement _reportList;
        private Label         _totalLabel;
        private Label         _statusLabel;

        private ProjectContext GetProject() => GetView?.Invoke();

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        private int ModelIndex => GetProject()?.CurrentModelIndex ?? 0;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(PlayerIoUiKit.Title("頂点IDユーティリティ"));

            var help = new Label(
                "頂点IDはモデル間・オブジェクト間で同じ頂点を突き合わせるための識別子です。"
              + "未設定や重複があるとIDによる対応付けは正しく動きません。"
              + "IDを使う操作の前にここで状態を確認してください。");
            help.style.fontSize   = 9;
            help.style.whiteSpace = WhiteSpace.Normal;
            help.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            help.style.marginBottom = 4;
            root.Add(help);

            // ── 診断 ─────────────────────────────────────────────────
            root.Add(PlayerIoUiKit.SectionLabel("診断（選択中のオブジェクト）"));

            _totalLabel = new Label();
            _totalLabel.style.fontSize    = 10;
            _totalLabel.style.whiteSpace  = WhiteSpace.Normal;
            _totalLabel.style.marginBottom = 2;
            root.Add(_totalLabel);

            _reportList = new VisualElement();
            root.Add(_reportList);

            root.Add(PlayerIoUiKit.WideBtn("再診断", Refresh));

            // ── 修復 ─────────────────────────────────────────────────
            root.Add(PlayerIoUiKit.Divider());
            root.Add(PlayerIoUiKit.SectionLabel("修復（選択中のオブジェクトに適用）"));

            root.Add(Note("既存のIDを保つ操作："));
            root.Add(PlayerIoUiKit.WideBtn("未設定IDを付与",
                () => Repair(RepairVertexIdsCommand.RepairMode.AssignMissing, "未設定IDを付与")));
            root.Add(PlayerIoUiKit.WideBtn("重複IDを振り直し",
                () => Repair(RepairVertexIdsCommand.RepairMode.ResolveDuplicates, "重複IDを振り直し")));

            root.Add(PlayerIoUiKit.Spacer());
            root.Add(Note("既存のIDによる対応付けが失われる操作："));
            root.Add(PlayerIoUiKit.WideBtn("全IDを連番で振り直し",
                () => Repair(RepairVertexIdsCommand.RepairMode.ReassignSequential, "全IDを連番で振り直し")));
            root.Add(PlayerIoUiKit.WideBtn("全IDを消去",
                () => Repair(RepairVertexIdsCommand.RepairMode.ClearAll, "全IDを消去")));

            _statusLabel = PlayerIoUiKit.StatusLabel();
            root.Add(_statusLabel);

            Refresh();
        }

        // ================================================================
        // 診断表示
        // ================================================================

        public void Refresh()
        {
            if (_reportList == null) return;
            _reportList.Clear();

            var targets = CollectTargets();
            if (targets.Count == 0)
            {
                _totalLabel.text = "オブジェクトが選択されていません";
                return;
            }

            var reports = VertexIdOps.Inspect(targets);

            int totalVerts = 0, totalUnset = 0, totalDupVerts = 0, healthy = 0;
            foreach (var r in reports)
            {
                totalVerts    += r.VertexCount;
                totalUnset    += r.UnsetCount;
                totalDupVerts += r.DuplicatedVertexCount;
                if (r.IsHealthy) healthy++;

                var lbl = new Label("  " + r.Summary);
                lbl.style.fontSize   = 9;
                lbl.style.whiteSpace = WhiteSpace.Normal;
                lbl.style.color      = new StyleColor(r.IsHealthy
                    ? new Color(0.65f, 0.9f, 0.65f)     // 問題なし = 緑
                    : new Color(1f, 0.7f, 0.4f));       // 要修復   = 橙
                _reportList.Add(lbl);
            }

            _totalLabel.text =
                $"{reports.Count} オブジェクト / 頂点 {totalVerts} / 未設定 {totalUnset} / "
              + $"重複 {totalDupVerts} 頂点  （問題なし {healthy}/{reports.Count}）";
            _totalLabel.style.color = new StyleColor(
                (totalUnset == 0 && totalDupVerts == 0)
                    ? new Color(0.65f, 0.9f, 0.65f)
                    : new Color(1f, 0.7f, 0.4f));

            PlayerLayoutRoot.ApplyDarkTheme(_reportList);
        }

        // ================================================================
        // 修復実行
        // ================================================================

        private void Repair(RepairVertexIdsCommand.RepairMode mode, string label)
        {
            var targets = CollectTargets();
            if (targets.Count == 0) { SetStatus("オブジェクトが選択されていません"); return; }

            SendCmd(new RepairVertexIdsCommand(ModelIndex, mode));

            // Dispatch は同期なので、実行後の状態をそのまま診断し直せる。
            Refresh();
            SetStatus($"{label}: {targets.Count} オブジェクトに適用しました");
        }

        /// <summary>選択中の描画メッシュ。未選択時は編集対象メッシュ単体。</summary>
        private List<MeshContext> CollectTargets()
        {
            var list  = new List<MeshContext>();
            var model = GetProject()?.CurrentModel;
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject != null) list.Add(mc);
            }
            if (list.Count == 0)
            {
                var mc = model.ActiveMeshContext;
                if (mc?.MeshObject != null) list.Add(mc);
            }
            return list;
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static Label Note(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 9;
            l.style.color        = new StyleColor(new Color(0.6f, 0.8f, 1f));
            l.style.marginTop    = 2;
            l.style.marginBottom = 2;
            return l;
        }
    }
}
