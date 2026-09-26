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

        // 旧 _activeBtn を分割。下区画に開けるのは一般パネルか 3D 操作パネルのどちらか 1 つだけ（排他）。
        // 種別で操作モードの決め方が違うので、ボタン・セクションは種別ごとに持つ（開いているのは片方だけ）。
        private Button _activeInteractionBtn;         // InteractionMode を示すボタン
        private Button _activeGeneralPanelBtn;        // 開いている一般パネルのボタン
        private Button _activeToolPanelBtn;           // 開いている 3D 操作パネルのボタン
        private VisualElement _activeGeneralSection;  // 開いている一般パネルのセクション
        private VisualElement _activeToolSection;     // 開いている 3D 操作パネルのセクション（null = なし）

        /// <summary>そのセクションが上下どちらかの区画で開いているか（常駐リストを含む）。</summary>
        private bool IsRightSectionActive(VisualElement section)
            => section != null && (section == _activeGeneralSection || section == _activeToolSection
                                   || _pinnedOpenOrder.Contains(section));

        /// <summary>
        /// 一般パネルが決めたい操作モードの適用手順。「操作なし」を指定したパネルでは null
        /// （そのときは常駐リストに任せる）。適用の順番は ApplyRightPaneViewportMode。
        /// </summary>
        private System.Action _generalPanelModeApplier;

        // ================================================================
        // 常駐リスト（モデル／オブジェクト／マテリアル）
        // ================================================================

        /// <summary>常駐リストのセクション → 開閉ボタンと、そのリストが決める操作モードの適用手順。</summary>
        private readonly Dictionary<VisualElement, (Button btn, System.Action applyMode)> _pinnedPanels
            = new Dictionary<VisualElement, (Button btn, System.Action applyMode)>();

        /// <summary>開いている常駐リスト。末尾が最後に開いた（または操作した）もので、操作モードを決める。</summary>
        private readonly List<VisualElement> _pinnedOpenOrder = new List<VisualElement>();

        /// <summary>
        /// 常駐リストを登録する。BuildLayout で 1 回だけ呼ぶ。
        /// その時点で表示されているセクション（起動時のオブジェクトリスト）は開いている扱いにする。
        /// 起動時に操作モードは適用しない（下区画の既定パネルが決める）。
        /// </summary>
        private void RegisterPinnedPanel(VisualElement section, Button btn, System.Action applyMode)
        {
            if (section == null) return;
            _pinnedPanels[section] = (btn, applyMode);
            if (section.style.display == DisplayStyle.Flex && !_pinnedOpenOrder.Contains(section))
                _pinnedOpenOrder.Add(section);
        }

        private bool IsPinnedOpen(VisualElement section)
            => section != null && _pinnedOpenOrder.Contains(section);

        /// <summary>
        /// 常駐リストを開く／閉じる。開くときは開いているリストの末尾へ回すので、
        /// 開いたリストが操作モードを決める側になる。
        /// </summary>
        private void SetPinnedOpen(VisualElement section, bool open)
        {
            if (section == null || !_pinnedPanels.TryGetValue(section, out var p)) return;
            _pinnedOpenOrder.Remove(section);
            if (open) _pinnedOpenOrder.Add(section);
            section.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            _layoutRoot?.RefreshRightAreas();
            if (open) PLPerfLog.SetPanel(p.btn?.text);
            RepaintButtonHighlights();
            ApplyRightPaneViewportMode();
            RefreshObjectOverlays();
        }

        /// <summary>
        /// 開いている常駐リストを末尾へ回して操作モードを適用し直す。
        /// リスト内でビューポート操作の選び方を変えたときに呼ぶ（そのリストが決める側になる）。
        /// </summary>
        private void PromotePinned(VisualElement section)
        {
            if (!IsPinnedOpen(section)) return;
            _pinnedOpenOrder.Remove(section);
            _pinnedOpenOrder.Add(section);
            ApplyRightPaneViewportMode();
        }

        /// <summary>
        /// 右ペインの状態からビューポートの操作モードを決めて適用する。優先順：
        ///   1. 3D 操作パネルが開いていれば、そのパネルが決めているので何もしない。
        ///   2. 一般パネルが「操作なし」以外を指定していれば、それ。
        ///   3. 開いている常駐リストのうち、最後に開いた（操作した）もの。
        ///   4. どれもなければ「操作なし」。
        /// </summary>
        private void ApplyRightPaneViewportMode()
        {
            if (_activeToolSection != null) return;
            if (_generalPanelModeApplier != null) { _generalPanelModeApplier(); return; }
            if (_pinnedOpenOrder.Count > 0
                && _pinnedPanels.TryGetValue(_pinnedOpenOrder[_pinnedOpenOrder.Count - 1], out var p)
                && p.applyMode != null)
            {
                p.applyMode();
                return;
            }
            SetInteractionMode(InteractionMode.None);
        }

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
        /// 全ツールボタン/パネルボタンの背景色を _activeInteractionBtn / 区画ごとのパネルボタン
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
                // 現在パネルを示すボタンは種別ごとに最大 1 つ（一般・3D 操作）なので
                // 個別列挙は不要 (下の色設定で扱う)
            }

            // InteractionMode ボタンのデフォルト色: _activeInteractionBtn なら青、そうでなければ非 active
            foreach (var b in btns)
            {
                bool isInteraction = (b == _activeInteractionBtn);
                bool isPanel       = (b == _activeGeneralPanelBtn || b == _activeToolPanelBtn);
                if (isInteraction && isPanel) b.style.backgroundColor = BothActiveBtnColor;
                else if (isInteraction)       b.style.backgroundColor = InteractionActiveBtnColor;
                else if (isPanel)             b.style.backgroundColor = PanelActiveBtnColor;
                else                          b.style.backgroundColor = InactiveBtnColor;
            }

            // パネルボタンが btns 以外 (カテゴリ 3 のパネル専用ボタン等) のとき単独着色
            // InteractionMode ボタンと同時 active は別ボタンに分離されているので緑のみ
            if (_activeGeneralPanelBtn != null && !btns.Contains(_activeGeneralPanelBtn))
                _activeGeneralPanelBtn.style.backgroundColor = PanelActiveBtnColor;
            if (_activeToolPanelBtn != null && !btns.Contains(_activeToolPanelBtn))
                _activeToolPanelBtn.style.backgroundColor = PanelActiveBtnColor;

            // 常駐リストの開閉ボタン：開いている間は緑、閉じていれば非 active 色。
            foreach (var kv in _pinnedPanels)
            {
                if (kv.Value.btn == null) continue;
                kv.Value.btn.style.backgroundColor =
                    _pinnedOpenOrder.Contains(kv.Key) ? PanelActiveBtnColor : InactiveBtnColor;
            }
        }

        /// <summary>
        /// InteractionMode ボタンのハイライトを現在の _interactionMode に基づき更新する。
        /// SetInteractionMode 末尾で呼ぶ。
        /// </summary>
        private void UpdateInteractionButtonHighlight()
        {
            // 旧 _activeInteractionBtn が別パネルのパネルボタンと重なっていたら、
            // その重なりを外すためにも Repaint で一括処理する。
            _activeInteractionBtn = GetButtonForInteractionMode(_interactionMode);
            RepaintButtonHighlights();
        }

        /// <summary>
        /// RightPanel ボタンのハイライトを区画ごとに設定。null で解除。
        /// </summary>
        private void SetActivePanelButton(Button btn, RightPanelKind kind)
        {
            // 以前のボタンが候補外 (カテゴリ 3 系) の場合、そのボタンだけは
            // 個別に非 active 色へ戻す。もう一方の区画で開いているボタンなら戻さない。
            var prev  = kind == RightPanelKind.Tool3D ? _activeToolPanelBtn    : _activeGeneralPanelBtn;
            var other = kind == RightPanelKind.Tool3D ? _activeGeneralPanelBtn : _activeToolPanelBtn;
            if (prev != null && prev != btn && prev != other)
                prev.style.backgroundColor = InactiveBtnColor;
            if (kind == RightPanelKind.Tool3D) _activeToolPanelBtn    = btn;
            else                               _activeGeneralPanelBtn = btn;
            RepaintButtonHighlights();
        }

        /// <summary>
        /// RightPanel の標準切替。下区画は排他で、開けるのは一般パネルか 3D 操作パネルのどちらか 1 つ：
        /// 下区画の全パネルを隠す → section 表示 → ボタンをハイライト。常駐リストは別扱い（開くだけ）。
        /// カテゴリ 1/2/3 共通に使える。
        /// 一般パネルを開いたときは、閉じた 3D 操作パネルに代わって一般パネル（または常駐リスト）の
        /// 操作モードを適用し直す。3D 操作パネルを開いたときは、一般パネルの操作モードを捨てる
        /// （3D 操作パネルは SetInteractionMode で自分のモードを決める）。
        /// section が null（セクションを持たないツール）のときは 3D 操作パネルを閉じた状態にする。
        /// </summary>
        private void ShowRightPanel(VisualElement section, Button panelBtn)
        {
            var kind = _layoutRoot?.GetRightPanelKind(section) ?? RightPanelKind.Tool3D;
            if (kind == RightPanelKind.Pinned)
            {
                // 常駐リストは排他にしない。開くだけ。
                SetPinnedOpen(section, true);
                return;
            }
            HideRightArea(RightPanelKind.General);
            HideRightArea(RightPanelKind.Tool3D);
            if (section != null) section.style.display = DisplayStyle.Flex;

            bool toolWasOpen = _activeToolSection != null;
            if (kind == RightPanelKind.Tool3D)
            {
                _activeToolSection       = section;
                _activeGeneralSection    = null;
                _generalPanelModeApplier = null;
                SetActivePanelButton(null, RightPanelKind.General);
            }
            else
            {
                _activeGeneralSection = section;
                _activeToolSection    = null;
                SetActivePanelButton(null, RightPanelKind.Tool3D);
            }
            _layoutRoot?.RefreshRightAreas();
            SetActivePanelButton(panelBtn, kind);
            PLPerfLog.SetPanel(panelBtn?.text);
            if (kind == RightPanelKind.General && toolWasOpen) ApplyRightPaneViewportMode();
            RefreshObjectOverlays();
        }

        /// <summary>
        /// 下区画のパネルを閉じる（種別を問わない）。「閉じる」ボタンから呼ぶ。
        /// 常駐リスト（なければ操作なし）の操作モードに戻る。
        /// </summary>
        private void CloseToolArea()
        {
            HideRightArea(RightPanelKind.General);
            HideRightArea(RightPanelKind.Tool3D);
            _activeToolSection       = null;
            _activeGeneralSection    = null;
            _generalPanelModeApplier = null;
            _layoutRoot?.RefreshRightAreas();
            SetActivePanelButton(null, RightPanelKind.General);
            SetActivePanelButton(null, RightPanelKind.Tool3D);
            ApplyRightPaneViewportMode();
            RefreshObjectOverlays();
        }

        /// <summary>
        /// 一般パネルが操作モードを決めるときの唯一の入口。
        /// 3D 操作パネルがないときだけ即時に適用する。開いている間に呼ばれた分は、
        /// ShowRightPanel が一般パネルへ切り替えたときに適用し直す（下区画は排他）。
        /// 一般パネルから SetInteractionMode を直接呼ぶと 3D 操作パネルの操作を奪うので、呼ばないこと。
        /// null（＝操作なし）のときは常駐リストが決める（ApplyRightPaneViewportMode）。
        /// </summary>
        private void ApplyGeneralPanelMode(System.Action apply)
        {
            _generalPanelModeApplier = apply;
            ApplyRightPaneViewportMode();
        }

        /// <summary>「操作なし」は常駐リストに任せる（例：オブジェクトリストの下に一時ミラーを開いても選択が効く）。</summary>
        private void ApplyGeneralPanelMode(InteractionMode mode)
            => ApplyGeneralPanelMode(mode == InteractionMode.None
                ? (System.Action)null
                : () => SetInteractionMode(mode));

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
            UpdateLineCurveOverlay();
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
                if (!IsRightSectionActive(section)) return;
                var mode = on ? InteractionMode.SelectOnly : InteractionMode.None;
                if (_layoutRoot?.GetRightPanelKind(section) == RightPanelKind.General)
                    ApplyGeneralPanelMode(mode);
                else
                    SetInteractionMode(mode);
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
        /// 一般パネルは ApplyGeneralPanelMode 経由（開いた時点で 3D 操作パネルは閉じるので、そこで適用される）。
        /// </summary>
        private void ShowRightPanelSelectable(VisualElement section, Button panelBtn, string key)
        {
            var mode = PanelSelectToggle.IsEnabled(key) ? InteractionMode.SelectOnly : InteractionMode.None;
            if (_layoutRoot?.GetRightPanelKind(section) == RightPanelKind.General)
                ApplyGeneralPanelMode(mode);
            else
                SetInteractionMode(mode);
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
