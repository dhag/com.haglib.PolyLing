// PlayerPlaceObjectReshapeSubPanel.cs
// PlaceObjectReshapeTool（藤壺の整形）の Player 版サブパネル（UIToolkit）。
// アフィン / 薄板スプライン の 2 方式を 1 つのパネルで切り替える。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置
//
// 【原型オブジェクト】描画オブジェクトのチェックボックス一覧（MeshSourceMultiPick）で選ぶ。
//   複数チェックしたときは一覧の並び順（上から）で 1 つへ結合したものを原型にする。
//   一覧の組み立てはこのパネルが持つ（PlayerPrimitiveMeshSubPanel 側とはテキスト辞書が
//   別なので共有していない）。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.Context;

namespace Poly_Ling.Player
{
    public class PlayerPlaceObjectReshapeSubPanel
    {
        public Func<PlaceObjectReshapeToolHandler> GetH;
        public Func<ProjectContext>                GetView;
        public Action<PanelCommand>                SendCommand;

        /// <summary>コマンドに載せるモデル索引。</summary>
        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        /// <summary>
        /// 実行時点の選択オブジェクトをコマンドの対象として載せる。
        /// 受け口は照合するだけで選択を書き換えない。
        /// </summary>
        private int[] SelectedMasterIndices()
        {
            var sel = GetView?.Invoke()?.CurrentModel?.SelectedDrawableMeshIndices;
            return sel != null ? sel.ToArray() : System.Array.Empty<int>();
        }

        /// <summary>原型の候補一覧。Viewer から設定する。</summary>
        public Func<List<(string Label, int MasterIndex, MeshObject Mesh)>> GetDrawableMeshEntryList;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _targetLabel;

        private RadioButtonGroup _modeGroup;

        // 原型
        private readonly MeshSourceMultiPick _srcPick = new MeshSourceMultiPick();
        private Label _prototypeLabel;

        // 対象パーツ
        private TextField _targetField;

        // 薄板スプライン専用
        private VisualElement _tpsBox;
        private FloatField    _lambdaField;

        private Button _executeBtn;
        private Label  _resultLabel;

        private static readonly List<string> ModeChoices =
            new List<string> { "アフィン変換", "薄板スプライン" };

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("Place Object Reshape / 藤壺の整形"));

            _root.Add(new HelpBox(
                "藤壺（オブジェクト配置）で置いた部品を、原型の形へ張り直します。\n"
                + "パーツID（Vertex.PartsId）ごとに、原型を BEFORE・現在の部品を AFTER として"
                + "変換係数を推定し、その係数で変換した原型で部品を置き換えます。",
                HelpBoxMessageType.Info));

            _targetLabel = InfoLabel();
            _root.Add(_targetLabel);

            BuildPrototypeBox();
            BuildTargetBox();
            BuildModeBox();

            // ── 実行 ───────────────────────────────────────────────────
            _executeBtn = new Button(() =>
            {
                var h = GetH();
                if (h == null) return;

                // 原型は MeshObject ではなく材料の masterIndex 配列で送る。
                // 受け口が同じ MeshObjectAppendOps.Combine で組み立てる。
                SendCommand?.Invoke(new PlaceObjectReshapeCommand(
                    ModelIndex, SelectedMasterIndices(),
                    _srcPick.SelectedMasterIndices().ToArray(),
                    h.Mode,
                    lambda:     h.Lambda,
                    targetText: h.TargetText));
                Refresh();
            }) { text = "開始" };
            _executeBtn.style.height    = 30;
            _executeBtn.style.marginTop = 6;
            _root.Add(_executeBtn);

            _resultLabel = InfoLabel();
            _resultLabel.style.marginTop = 4;
            _root.Add(_resultLabel);

            ApplyModeVisibility(PlaceObjectReshapeMode.Affine);
            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ── 原型オブジェクト ──────────────────────────────────────────

        private void BuildPrototypeBox()
        {
            _root.Add(SmallHeader("原型オブジェクト（複数チェックすると上から順に結合）:"));

            _srcPick.ListContainer = new VisualElement();
            _srcPick.ListContainer.style.marginBottom = 2;
            _root.Add(_srcPick.ListContainer);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;
            row.Add(SmallBtn("一覧を再取得", RefreshSourcePick));
            _root.Add(row);

            _prototypeLabel = InfoLabel();
            _root.Add(_prototypeLabel);

            RefreshSourcePick();
        }

        // ── 対象パーツ ────────────────────────────────────────────────

        private void BuildTargetBox()
        {
            _targetField = new TextField("対象パーツID（空欄で全部）") { isDelayed = true, value = "" };
            _targetField.style.marginBottom = 3;
            _targetField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.TargetText = e.newValue;
            });
            _root.Add(_targetField);

            _root.Add(SmallHeader("「5,6,7」や「5-7」の形式。"));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;
            row.Add(SmallBtn("選択頂点から取得", () =>
            {
                var h = GetH();
                if (h == null) return;

                string text = h.CollectSelectedPartsIdText();
                h.TargetText = text;
                _targetField.SetValueWithoutNotify(text);
                RefreshExecuteEnabled();
            }));
            _root.Add(row);
        }

        // ── 方式 ──────────────────────────────────────────────────────

        private void BuildModeBox()
        {
            _root.Add(SmallHeader("方式:"));
            _modeGroup = new RadioButtonGroup(null, ModeChoices) { value = 0 };
            _modeGroup.style.marginBottom = 4;
            _modeGroup.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.Mode = ToMode(e.newValue);
                ApplyModeVisibility(ToMode(e.newValue));
                RefreshExecuteEnabled();
            });
            _root.Add(_modeGroup);

            _tpsBox = new VisualElement();
            _root.Add(_tpsBox);

            _tpsBox.Add(new HelpBox(
                "平滑化係数を大きく取るほど原型の形へ寄ります。0 に近いと補間になり、"
                + "現在の（壊れた）形をなぞるだけで整形になりません。\n"
                + "適した値はモデルの寸法によって変わるため、初期値は目安です。",
                HelpBoxMessageType.Info));

            _lambdaField = new FloatField("平滑化係数 lambda")
            {
                value = PlaceObjectReshapeSettings.DefaultLambda
            };
            _lambdaField.style.marginBottom = 3;
            _lambdaField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.Lambda = e.newValue;
            });
            _tpsBox.Add(_lambdaField);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH();
            if (h == null) return;

            int targets = h.TargetMeshCount;
            _targetLabel.text = targets > 0
                ? $"対象オブジェクト: {targets} 個"
                : "対象オブジェクトなし（オブジェクトを選択してください）";

            _modeGroup?.SetValueWithoutNotify(ToIndex(h.Mode));
            _lambdaField?.SetValueWithoutNotify(h.Lambda);
            _targetField?.SetValueWithoutNotify(h.TargetText ?? "");

            RefreshSourcePick();

            if (_resultLabel != null) _resultLabel.text = h.LastResult ?? "";

            ApplyModeVisibility(h.Mode);
            RefreshExecuteEnabled();
        }

        /// <summary>チェックされた描画オブジェクトを一覧の並び順で 1 つへ結合して返す。</summary>
        public MeshObject BuildPrototype()
        {
            var list = _srcPick.CurrentList();
            if (list.Count == 0) return null;
            return MeshObjectAppendOps.Combine(list, "PlaceObjectReshapePrototype");
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        /// <summary>描画オブジェクトのチェックボックス一覧を作り直す。</summary>
        private void RefreshSourcePick()
        {
            _srcPick.Candidates = GetDrawableMeshEntryList?.Invoke()
                                  ?? new List<(string, int, MeshObject)>();

            // 一覧から消えたラベルの選択は捨てる。
            var alive = new HashSet<string>();
            foreach (var e in _srcPick.Candidates) alive.Add(e.Label);
            _srcPick.SelectedLabels.RemoveWhere(l => !alive.Contains(l));

            if (_srcPick.ListContainer == null) return;
            _srcPick.ListContainer.Clear();

            if (_srcPick.Candidates.Count == 0)
            {
                var empty = new Label("(候補なし)");
                empty.style.fontSize = 10;
                _srcPick.ListContainer.Add(empty);
                RefreshPrototypeLabel();
                return;
            }

            foreach (var e in _srcPick.Candidates)
            {
                string label = e.Label;
                var tog = new Toggle(label) { value = _srcPick.SelectedLabels.Contains(label) };
                tog.style.fontSize = 10;
                tog.RegisterValueChangedCallback(ev =>
                {
                    if (ev.newValue) _srcPick.SelectedLabels.Add(label);
                    else             _srcPick.SelectedLabels.Remove(label);
                    RefreshPrototypeLabel();
                    RefreshExecuteEnabled();
                });
                _srcPick.ListContainer.Add(tog);
            }

            RefreshPrototypeLabel();
        }

        private void RefreshPrototypeLabel()
        {
            if (_prototypeLabel == null) return;

            int count = PrototypeVertexCount();
            _prototypeLabel.text = count > 0
                ? $"原型の頂点数: {count}"
                : "原型オブジェクトが選ばれていません";
        }

        /// <summary>結合後の原型の頂点数。原型未指定なら 0。</summary>
        private int PrototypeVertexCount()
        {
            int total = 0;
            foreach (var mo in _srcPick.CurrentList()) total += mo.VertexCount;
            return total;
        }

        private void ApplyModeVisibility(PlaceObjectReshapeMode mode)
        {
            Show(_tpsBox, mode == PlaceObjectReshapeMode.ThinPlateSpline);
        }

        private static void Show(VisualElement e, bool visible)
        {
            if (e == null) return;
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshExecuteEnabled()
        {
            if (_executeBtn == null) return;

            var h = GetH();
            if (h == null) { _executeBtn.SetEnabled(false); return; }

            bool can = h.TargetMeshCount > 0
                       && PrototypeVertexCount() >= PlaceObjectReshapeOps.MinimumVertexCount;

            _executeBtn.SetEnabled(can);
        }

        private static PlaceObjectReshapeMode ToMode(int index)
        {
            switch (index)
            {
                case 1:  return PlaceObjectReshapeMode.ThinPlateSpline;
                default: return PlaceObjectReshapeMode.Affine;
            }
        }

        private static int ToIndex(PlaceObjectReshapeMode mode)
        {
            switch (mode)
            {
                case PlaceObjectReshapeMode.ThinPlateSpline: return 1;
                default:                                     return 0;
            }
        }

        // ================================================================
        // ウィジェットファクトリ
        // ================================================================

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop    = 4;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label SmallHeader(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            l.style.whiteSpace   = WhiteSpace.Normal;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            l.style.whiteSpace   = WhiteSpace.Normal;
            return l;
        }

        private static Button SmallBtn(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow    = 1;
            b.style.marginRight = 2;
            b.style.height      = 18;
            b.style.fontSize    = 9;
            return b;
        }
    }
}
