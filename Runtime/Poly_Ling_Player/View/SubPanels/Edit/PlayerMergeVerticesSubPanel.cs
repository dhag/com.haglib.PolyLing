// PlayerMergeVerticesSubPanel.cs
// 頂点マージツール用サブパネル。エディタ版 MergeVerticesTool.DrawSettingsUI() と同等。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerMergeVerticesSubPanel
    {
        public Func<MergeVerticesToolHandler> GetH;
        public Func<ProjectContext>           GetView;
        public Action<PanelCommand>           SendCommand;

        /// <summary>コマンドに載せるモデル索引。</summary>
        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        /// <summary>
        /// 編集対象メッシュを 1 本だけコマンドの対象として載せる。
        /// 対象が決まらないときは null（呼び出し側が送信を止める）。
        /// </summary>
        private int[] ActiveMasterIndices()
        {
            var model = GetView?.Invoke()?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc == null) return null;
            return new[] { model.IndexOf(mc) };
        }

        // UI 自動操作の ID は "mergeVertices.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("threshold", Description = "結合する頂点どうしの距離のしきい値")]
        private FloatField    _threshField;
        [UiControl("showPreview", Description = "結合候補をビューポートに表示する")]
        private Toggle        _previewToggle;
        [UiControl("stats.groups", Safety = UiSafety.ReadOnly, Description = "結合グループの数")]
        private Label         _groupsLabel;
        [UiControl("stats.vertices", Safety = UiSafety.ReadOnly, Description = "結合で消える頂点の数")]
        private Label         _vertsLabel;
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _detailList;
        [UiControl("thresholdPreset.small", Safety = UiSafety.SafeWrite, Description = "しきい値を 0.001 にする")]
        private Button        _thresholdPreset0001Btn;
        [UiControl("thresholdPreset.medium", Safety = UiSafety.SafeWrite, Description = "しきい値を 0.01 にする")]
        private Button        _thresholdPreset001Btn;
        [UiControl("thresholdPreset.large", Safety = UiSafety.SafeWrite, Description = "しきい値を 0.1 にする")]
        private Button        _thresholdPreset01Btn;
        [UiControl("mergeByThreshold", Safety = UiSafety.SafeWrite, Description = "しきい値以内の頂点を結合する（Ctrl+Shift+J と同じ）")]
        private Button        _mergeThresholdBtn;
        [UiControl("mergeAll", Safety = UiSafety.SafeWrite, Description = "距離を無視して重心へ結合する（Ctrl+J と同じ）")]
        private Button        _mergeCentroidBtn;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            _root.Add(Header("Merge Vertices"));
            _root.Add(new HelpBox(
                "しきい値で結合: 選択頂点のうち距離がしきい値以内のものをグループごとに結合します。\n"
              + "結合（距離無視）: 距離を見ず、選択頂点をまとめて 1 点（重心）へ結合します。",
                HelpBoxMessageType.Info));

            // Threshold FloatField
            var threshRow = new VisualElement();
            threshRow.style.flexDirection = FlexDirection.Row;
            threshRow.style.marginBottom  = 3;
            var threshLbl = new Label("Threshold");
            threshLbl.style.width = 70; threshLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _threshField = new FloatField { value = 0.001f };
            _threshField.style.flexGrow = 1;
            _threshField.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Max(0.0001f, e.newValue);
                _threshField.SetValueWithoutNotify(v);
                var h = GetH(); if (h != null) h.Threshold = v;
            });
            threshRow.Add(threshLbl); threshRow.Add(_threshField);
            _root.Add(threshRow);

            // プリセットボタン
            var presetRow = new VisualElement();
            presetRow.style.flexDirection = FlexDirection.Row;
            presetRow.style.marginBottom  = 4;
            var presetBtns = new Button[3];
            int presetIdx = 0;
            foreach (var (label, val) in new[] { ("0.001", 0.001f), ("0.01", 0.01f), ("0.1", 0.1f) })
            {
                float v = val;
                var b = new Button(() =>
                {
                    _threshField?.SetValueWithoutNotify(v);
                    var h = GetH(); if (h != null) h.Threshold = v;
                }) { text = label };
                b.style.flexGrow = 1;
                presetRow.Add(b);
                presetBtns[presetIdx++] = b;
            }
            _thresholdPreset0001Btn = presetBtns[0];
            _thresholdPreset001Btn  = presetBtns[1];
            _thresholdPreset01Btn   = presetBtns[2];
            _root.Add(presetRow);

            _previewToggle = new Toggle("Show Preview") { value = true };
            _previewToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.ShowPreview = e.newValue; });
            _root.Add(_previewToggle);

            _groupsLabel = InfoLabel(); _root.Add(_groupsLabel);
            _vertsLabel  = InfoLabel(); _root.Add(_vertsLabel);

            // グループ詳細リスト（最大5件）
            _detailList = new VisualElement();
            _root.Add(_detailList);

            // どちらもコマンドへ流す。パネル外のショートカットと同じ経路にすることで
            // 「パネルを開いている間しか動かない」旧経路 (TriggerMerge) への依存を無くす。
            var mergeThreshBtn = new Button(() => SendMerge(MergeVerticesCommand.MergeMode.Threshold))
                { text = "しきい値で結合 Ctrl+Shift+J" };
            mergeThreshBtn.style.height    = 30;
            mergeThreshBtn.style.marginTop = 6;
            _root.Add(mergeThreshBtn);

            var mergeCentroidBtn = new Button(() => SendMerge(MergeVerticesCommand.MergeMode.Centroid))
                { text = "結合（距離無視） Ctrl+J" };
            mergeCentroidBtn.style.height    = 30;
            mergeCentroidBtn.style.marginTop = 4;
            _root.Add(mergeCentroidBtn);

            _mergeThresholdBtn = mergeThreshBtn;
            _mergeCentroidBtn  = mergeCentroidBtn;
        }

        /// <summary>
        /// 頂点結合コマンドを組んで送る。しきい値は Centroid でも載せる
        /// （読まれないが、コマンドを自己完結させるため）。
        /// </summary>
        private void SendMerge(MergeVerticesCommand.MergeMode mode)
        {
            var h = GetH();
            var targets = ActiveMasterIndices();
            if (h == null || targets == null) return;

            SendCommand?.Invoke(new MergeVerticesCommand(
                ModelIndex, targets, mode, h.Threshold));
            Refresh();
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            _threshField?.SetValueWithoutNotify(h.Threshold);
            _previewToggle?.SetValueWithoutNotify(h.ShowPreview);

            var info = h.PreviewInfo;
            if (info.GroupCount > 0)
            {
                _groupsLabel.text = $"Groups: {info.GroupCount}";
                _vertsLabel.text  = $"Vertices to remove: {info.TotalVerticesToMerge}";

                // 詳細リスト（最大5グループ）
                _detailList.Clear();
                int showCount = Mathf.Min(info.Groups.Count, 5);
                for (int i = 0; i < showCount; i++)
                {
                    var group = info.Groups[i];
                    var take  = group.Take(8).ToArray();
                    string indices = string.Join(", ", take) + (group.Count > 8 ? "..." : "");
                    var lbl = new Label($"  [{i}] {group.Count} verts: {indices}");
                    lbl.style.fontSize = 9;
                    lbl.style.color    = new StyleColor(Color.white);
                    _detailList.Add(lbl);
                }
                if (info.Groups.Count > 5)
                {
                    var more = new Label($"  ...他 {info.Groups.Count - 5} グループ");
                    more.style.fontSize = 9;
                    more.style.color    = new StyleColor(Color.white);
                    _detailList.Add(more);
                }
            }
            else
            {
                _groupsLabel.text = "No merge candidates";
                _vertsLabel.text  = "";
                _detailList.Clear();
            }
            PlayerLayoutRoot.ApplyDarkTheme(_detailList);
        }

        private static Label Header(string t) { var l = new Label(t); l.style.marginTop = 4; l.style.marginBottom = 3; return l; }
        private static Label InfoLabel() { var l = new Label(); l.style.fontSize = 10; l.style.marginBottom = 2; return l; }

    }
}