// PolyLingPlayerViewerCore.ButtonHighlight.cs
// Player ビューアのコア：ボタンのアクティブ色・ハイライトと、汎用のコールバック。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.Serialization;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.EditorBridge;
using Poly_Ling.View;
using Poly_Ling.MeshListV2;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectArray;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        // ================================================================
        // ボタンアクティブ色
        // ================================================================

        // ================================================================
        // ボタンハイライト 2 系統 (段階 2)
        //
        // カテゴリ 1 ボタン (VertexMove 等): InteractionMode と RightPanel の両方を担う
        // カテゴリ 2 ボタン (AlignVertices 等): RightPanel のみ。InteractionMode は維持
        // カテゴリ 3 ボタン (Mirror 等): RightPanel のみ。InteractionMode=None
        //
        // 同一ボタンが両系統 active になる場合は BothActiveBtnColor で表示する。
        // ================================================================

        // 非 active 色 (既存)。PlayerLayoutRoot.ApplyDarkTheme が入れる値と同一。
        private static readonly StyleColor InactiveBtnColor         = PlayerLayoutRoot.BtnInactiveColor;
        // InteractionMode のみ active (青)
        private static readonly StyleColor InteractionActiveBtnColor = PlayerLayoutRoot.BtnActiveColor;
        // RightPanel のみ active (緑系)
        private static readonly StyleColor PanelActiveBtnColor       = new StyleColor(new Color(0.3f,  0.75f, 0.4f));
        // 両方 active (α: 混色の青緑)
        private static readonly StyleColor BothActiveBtnColor        = new StyleColor(new Color(0.3f,  0.625f, 0.7f));

        // 旧 _activeBtn を 2 つに分割
        private Button _activeInteractionBtn;   // InteractionMode を示すボタン
        private Button _activePanelBtn;         // 現在開いている RightPanel を示すボタン
        private VisualElement _activeRightSection;  // 現在開いている RightPanel のセクション

        /// <summary>
        /// InteractionMode に対応するボタンを取得。ない (None / 未割当) なら null。
        /// </summary>
        private Button GetButtonForInteractionMode(InteractionMode mode)
        {
            if (_layoutRoot == null) return null;
            switch (mode)
            {
                case InteractionMode.VertexMove:      return _layoutRoot.ToolVertexMoveBtn;
                case InteractionMode.ObjectMove:      return _layoutRoot.ToolObjectMoveBtn;
                case InteractionMode.PivotOffset:     return _layoutRoot.ToolPivotOffsetBtn;
                case InteractionMode.Sculpt:          return _layoutRoot.ToolSculptBtn;
                case InteractionMode.AdvancedSelect:  return _layoutRoot.ToolAdvancedSelBtn;
                case InteractionMode.SkinWeightPaint: return _layoutRoot.ToolSkinWeightPaintBtn;
                case InteractionMode.SkinWeightNumeric: return _layoutRoot.SkinWeightNumericBtn;
                case InteractionMode.DeleteFace:      return _layoutRoot.ToolDeleteFaceBtn;
                // AddFace / EdgeBevel / EdgeExtrude / FaceExtrude / EdgeTopology / Knife
                // はツールボタンを持たない (右ペインから起動) ため null のまま。
                default: return null;
            }
        }

        /// <summary>
        /// 全ツールボタン/パネルボタンの背景色を _activeInteractionBtn / _activePanelBtn
        /// の状態から再計算する。両系統を同時に反映するため単一の経路にまとめる。
        /// </summary>
        private void RepaintButtonHighlights()
        {
            // 候補ボタン集合 (null 安全に列挙)
            var btns = new System.Collections.Generic.List<Button>();
            if (_layoutRoot != null)
            {
                void Add(Button b) { if (b != null) btns.Add(b); }
                // InteractionMode 側
                Add(_layoutRoot.ToolVertexMoveBtn);
                Add(_layoutRoot.ToolObjectMoveBtn);
                Add(_layoutRoot.ToolPivotOffsetBtn);
                Add(_layoutRoot.ToolSculptBtn);
                Add(_layoutRoot.ToolAdvancedSelBtn);
                Add(_layoutRoot.ToolSkinWeightPaintBtn);
                Add(_layoutRoot.SkinWeightNumericBtn);
                Add(_layoutRoot.ToolDeleteFaceBtn);
                // 現在パネルを示す可能性があるボタンは、_activePanelBtn が非 null のとき
                // それ 1 つだけなので個別列挙は不要 (下の色設定で扱う)
            }

            // InteractionMode ボタンのデフォルト色: _activeInteractionBtn なら青、そうでなければ非 active
            foreach (var b in btns)
            {
                bool isInteraction = (b == _activeInteractionBtn);
                bool isPanel       = (b == _activePanelBtn);
                if (isInteraction && isPanel) b.style.backgroundColor = BothActiveBtnColor;
                else if (isInteraction)       b.style.backgroundColor = InteractionActiveBtnColor;
                else if (isPanel)             b.style.backgroundColor = PanelActiveBtnColor;
                else                          b.style.backgroundColor = InactiveBtnColor;
            }

            // _activePanelBtn が btns 以外 (カテゴリ 3 のパネル専用ボタン等) のとき単独着色
            if (_activePanelBtn != null && !btns.Contains(_activePanelBtn))
            {
                // InteractionMode ボタンと同時 active は別ボタンに分離されているので緑のみ
                _activePanelBtn.style.backgroundColor = PanelActiveBtnColor;
            }
        }

        /// <summary>
        /// InteractionMode ボタンのハイライトを現在の _interactionMode に基づき更新する。
        /// SetInteractionMode 末尾で呼ぶ。
        /// </summary>
        private void UpdateInteractionButtonHighlight()
        {
            // 旧 _activeInteractionBtn が別パネルの _activePanelBtn と重なっていたら、
            // その重なりを外すためにも Repaint で一括処理する。
            _activeInteractionBtn = GetButtonForInteractionMode(_interactionMode);
            RepaintButtonHighlights();
        }

        /// <summary>
        /// RightPanel ボタンのハイライトを設定。null で解除。
        /// </summary>
        private void SetActivePanelButton(Button btn)
        {
            // 以前の _activePanelBtn が候補外 (カテゴリ 3 系) の場合、そのボタンだけは
            // 個別に非 active 色へ戻す。
            if (_activePanelBtn != null && _activePanelBtn != btn)
                _activePanelBtn.style.backgroundColor = InactiveBtnColor;
            _activePanelBtn = btn;
            RepaintButtonHighlights();
        }

        /// <summary>
        /// RightPanel の標準切替: HideAllRightPanels → section 表示 → パネルボタンをハイライト。
        /// カテゴリ 1/2/3 共通に使える。SetInteractionMode とは独立。
        /// </summary>
        private void ShowRightPanel(VisualElement section, Button panelBtn)
        {
            HideAllRightPanels();
            if (section != null) section.style.display = DisplayStyle.Flex;
            _activeRightSection = section;
            SetActivePanelButton(panelBtn);
            PLPerfLog.SetPanel(panelBtn?.text);
            RefreshObjectOverlays();
        }

        /// <summary>
        /// 原点マーカー（水色ダイヤ）とギズモを、今のモード・パネルで組み直す。
        ///
        /// 【なぜ要るか】
        ///   この 2 つの更新は PlayerViewportManager の Enter〜（データが変わる出来事）
        ///   からしか呼ばれていなかった。パネルやツールを切り替えただけでは何も
        ///   起きないため、視点を動かすなど別の理由で再描画が走るまで
        ///   「マーカーが出ない」「前のギズモが残る」状態になっていた。
        ///   切替もマーカー・ギズモの出し分けを変える出来事なので、ここで組み直す。
        /// </summary>
        private void RefreshObjectOverlays()
        {
            UpdateBoneOverlay();
            UpdateGizmoOverlay();
        }

        /// <summary>
        /// パネルごとの「ビューポートで選択する」チェックをセクションへ差し込む。
        /// サブパネルの Build 直後に1回だけ呼ぶ。チェックの変更は、そのパネルを
        /// 開いている間だけ InteractionMode へ即時反映する。
        /// </summary>
        private void AttachPanelSelectToggle(VisualElement section, string key)
        {
            var toggle = PanelSelectToggle.Attach(section, key, on =>
            {
                if (section == null || _activeRightSection != section) return;
                SetInteractionMode(on ? InteractionMode.SelectOnly : InteractionMode.None);
            });
            // UI 自動操作で "<パネル ID>.selectInViewport" として登録するために覚えておく
            // （PolyLingPlayerViewerCore.UiAutomation.cs の RegisterUiPanel）。
            if (section != null && toggle != null) _panelSelectToggles[section] = toggle;
        }

        /// <summary>セクション → そのセクションに付けた「ビューポートで選択する」トグル。</summary>
        private readonly System.Collections.Generic.Dictionary<VisualElement, Toggle> _panelSelectToggles
            = new System.Collections.Generic.Dictionary<VisualElement, Toggle>();

        /// <summary>
        /// カテゴリ3のパネルを、選択許可チェック付きで開く。
        /// ON なら SelectOnly（移動ギズモなしの選択のみ）、OFF なら None（3D操作無効）。
        /// </summary>
        private void ShowRightPanelSelectable(VisualElement section, Button panelBtn, string key)
        {
            SetInteractionMode(
                PanelSelectToggle.IsEnabled(key) ? InteractionMode.SelectOnly : InteractionMode.None);
            ShowRightPanel(section, panelBtn);
        }

        // ================================================================
        // コールバック / イベントハンドラ
        // ================================================================

        /// <summary>
        /// 格子変形モードのビューポート入力先を格子の状態で切り替える。
        ///
        ///   Idle / Placement … MoveToolHandler(SelectOnly) へ流し、メッシュ頂点を選び直せる。
        ///                       選び直した後は「選択フィット」で格子を合わせ直す。
        ///   Deform          … LatticeToolHandler へ流し、格子点を選択・操作する。
        ///
        /// LatticeToolHandler.OnStateChanged からも呼ぶため、格子変形モード以外では何もしない。
        /// </summary>
        private void ApplyLatticeToolRouting()
        {
            if (_interactionMode != InteractionMode.Lattice) return;

            bool deform = _latticeHandler != null
                && _latticeHandler.State == LatticeToolHandler.LatticeState.Deform;

            if (deform)
            {
                if (_moveToolHandler != null) _moveToolHandler.SelectOnly = false;
                _vertexInteractor?.SetToolHandler(_latticeHandler);
                _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _latticeHandler?.UpdateHover(pos, ctx));
                return;
            }

            // 選択専用。組み込み移動ギズモは出さない（InteractionMode.Deform と同じ方式）。
            if (_moveToolHandler != null) _moveToolHandler.SelectOnly = true;
            _vertexInteractor?.SetToolHandler(_moveToolHandler);
            _viewportManager?.RegisterActiveToolHandler(null);
        }
    }
}
