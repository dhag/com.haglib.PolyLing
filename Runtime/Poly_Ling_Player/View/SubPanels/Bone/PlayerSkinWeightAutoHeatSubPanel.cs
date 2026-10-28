// PlayerSkinWeightAutoHeatSubPanel.cs
// スキンW自動（ボーンヒート）パネル（Player ビルド用）。
// 選択中の描画オブジェクトのウェイトを、Blender の自動ウェイトと同じ方式で付け直す
// （SkinWeightAutoHeatCommand → SkinWeightHeatOps）。3D 操作は伴わない。
// Runtime/Poly_Ling_Player/View/SubPanels/Bone/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    public class PlayerSkinWeightAutoHeatSubPanel
    {
        // ================================================================
        // 配線（Viewer が設定する）
        // ================================================================

        /// <summary>
        /// コマンドを流して結果を返す（DispatchHost）。Dispatch は同期なので、
        /// 戻り値で成否と失敗理由をそのまま表示できる。
        /// </summary>
        public Func<PanelCommand, CommandResult> Dispatch;

        /// <summary>直近の実行結果（件数）。PlayerCommandDispatcher が持つ。</summary>
        public Func<HeatRunResult> GetLastResult;

        /// <summary>パネルから出すコマンドのモデル番号（現在のモデル）。</summary>
        public Func<int> GetModelIndex;

        /// <summary>再描画要求。</summary>
        public Action OnRepaint;

        // ================================================================
        // 入力値
        // ================================================================

        private HeatBoneScope _boneScope    = HeatBoneScope.AllExceptIK;
        private bool          _selectedOnly = false;

        // ================================================================
        // UI
        // ================================================================

        // UI 自動操作の ID は "skinWeightAutoHeat.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;

        [UiControl("boneScope", Description = "使うボーン（全ボーン（IK除く）／Humanoid のボーンだけ／選択中のボーンだけ）")]
        private DropdownField _boneScopeDropdown;
        [UiControl("vertexScope", Description = "書き換える頂点（全頂点／選択頂点のみ）。計算はメッシュ全体で行う")]
        private DropdownField _vertexScopeDropdown;
        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "ボーンヒートでウェイトを付け直す")]
        private Button _applyBtn;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果（件数または失敗理由）")]
        private Label _statusLabel;

        // 並びは HeatBoneScope の値と同じ。
        private static readonly List<string> BoneScopeChoices = new List<string>
        {
            "全ボーン（IK除く）", "Humanoid のボーンだけ", "選択中のボーンだけ",
        };
        private static readonly List<string> VertexScopeChoices = new List<string> { "全頂点", "選択頂点のみ" };

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;

            var title = new Label("スキンW自動（ボーンヒート）");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            _root.Add(title);

            var desc = InfoLabel();
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.text = "選択中の描画オブジェクトのウェイトを、Blender の「自動のウェイトで」と同じ方式で付け直します。"
                      + "各ボーンは、関節から階層を下って最初に出会う使うボーンの関節までの線分（無ければ関節の点）として扱います。"
                      + "1 頂点 4 本までに絞り、合計 1 にします。どのボーンからも届かない頂点は変えません。"
                      + "一括スキンド化したモデルは部品ごとのボーンが関節と同じ位置にあるので、「Humanoid のボーンだけ」を使ってください。";
            _root.Add(desc);

            _boneScopeDropdown = new DropdownField("使うボーン", BoneScopeChoices, (int)_boneScope);
            _boneScopeDropdown.RegisterValueChangedCallback(e =>
            {
                int i = BoneScopeChoices.IndexOf(e.newValue);
                if (i >= 0) _boneScope = (HeatBoneScope)i;
            });
            _root.Add(_boneScopeDropdown);

            _vertexScopeDropdown = new DropdownField("書き換える頂点", VertexScopeChoices, 0);
            _vertexScopeDropdown.RegisterValueChangedCallback(e =>
                _selectedOnly = VertexScopeChoices.IndexOf(e.newValue) == 1);
            _root.Add(_vertexScopeDropdown);

            _applyBtn = new Button(OnApply) { text = "実行" };
            _applyBtn.style.height    = 30;
            _applyBtn.style.marginTop = 4;
            _root.Add(_applyBtn);

            _statusLabel = InfoLabel();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_statusLabel);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
            parent.Add(_root);
        }

        public void Refresh() { }

        private void OnApply()
        {
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            var r = Dispatch?.Invoke(new SkinWeightAutoHeatCommand(modelIdx, _boneScope, null, _selectedOnly));

            string msg;
            if (r == null)
                msg = "失敗：コマンドを実行できません。";
            else if (!r.Success)
                msg = $"失敗：{(string.IsNullOrEmpty(r.Reason) ? "理由不明" : r.Reason)}";
            else
                msg = GetLastResult?.Invoke()?.Summary ?? "完了しました（件数を取得できません）。";

            if (_statusLabel != null) _statusLabel.text = msg;
            OnRepaint?.Invoke();
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.color        = new StyleColor(Color.white);
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }
    }
}
