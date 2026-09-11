// PolyLingPlayerViewerCore.SelectMode.cs
// Player ビューアのコア：選択モード（頂点／辺／面／線分）の単一権限。
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
        // 選択モード（頂点/辺/面/線分）の単一権限
        //
        // 書き込みは ApplySelectMode() だけが行う。他の場所から
        // SelectionState.Mode / MeshContext.SelectMode へ代入してはならない。
        // ================================================================

        /// <summary>
        /// 左ペインのチェックボックスから _userSelectMode を読み取る。
        /// 全 OFF は頂点へフォールバックする（何も選べないモードを作らない）。
        /// </summary>
        private void ReadUserSelectModeFromToggles()
        {
            if (_layoutRoot?.SelModeVertexToggle == null) return;

            MeshSelectMode m = MeshSelectMode.None;
            if (_layoutRoot.SelModeVertexToggle.value) m |= MeshSelectMode.Vertex;
            if (_layoutRoot.SelModeEdgeToggle  .value) m |= MeshSelectMode.Edge;
            if (_layoutRoot.SelModeFaceToggle  .value) m |= MeshSelectMode.Face;
            if (_layoutRoot.SelModeLineToggle  .value) m |= MeshSelectMode.Line;
            if (m == MeshSelectMode.None) m = MeshSelectMode.Vertex;

            _userSelectMode = m;
        }

        /// <summary>
        /// 左ペインのチェックボックスから _expandToVertexKinds を読み取る。
        ///
        /// 返り値は「辺／面／線分のうち、どの種別を頂点選択へ展開するか」の集合。
        /// 選択モード（何を選べるか）とは別物なので、全 OFF のフォールバックは持たない
        /// （全 OFF＝どれも展開しない、が正しい指定）。
        /// </summary>
        private void ReadExpandToVertexKindsFromToggles()
        {
            if (_layoutRoot?.SelExpandEdgeToVertexToggle == null) return;

            MeshSelectMode k = MeshSelectMode.None;
            if (_layoutRoot.SelExpandEdgeToVertexToggle.value) k |= MeshSelectMode.Edge;
            if (_layoutRoot.SelExpandFaceToVertexToggle.value) k |= MeshSelectMode.Face;
            if (_layoutRoot.SelExpandLineToVertexToggle.value) k |= MeshSelectMode.Line;

            _expandToVertexKinds = k;
        }

        /// <summary>現在の実効選択モード。ツール固有 override があればそれが優先。</summary>
        private MeshSelectMode EffectiveSelectMode
        {
            get
            {
                var m = _toolSelectModeOverride ?? _userSelectMode;
                return m == MeshSelectMode.None ? MeshSelectMode.Vertex : m;
            }
        }

        /// <summary>
        /// 実効選択モードを、判定側が実際に読む全ての SelectionState へ書き込む。
        ///
        /// 【書き込み先と、その理由】
        ///   1. 現モデルの全 MeshContext.Selection
        ///      … MoveToolHandler.UpdateAffectedVertices が「メッシュごとの」Mode を読む。
        ///        1 個だけに書くとメッシュを切り替えた瞬間に挙動が変わる。
        ///   2. _selectionState
        ///      … 初期化直後や、メッシュ未選択時に判定側が掴んでいる素の SelectionState。
        ///   3. _selectionOps.SelectionState
        ///      … MoveToolHandler / AdvancedSelect のクリック・矩形選択がここの Mode を読む。
        ///   4. _renderer.CurrentSelectionState
        ///      … GPU ホバーの種別絞り込み (UnifiedMeshSystem.ProcessMouseUpdate) が読む実体。
        ///        1〜3 と別インスタンスになり得るため、ここを外すとホバーだけ効かなくなる。
        ///
        /// 【無効種別の選択解除】
        /// 実効モードが「変化したとき」だけ、無効になった種別の選択を解除する。
        /// 例: 辺を選択中にチェックを頂点のみへ変えた、面追加ツールへ入った、など。
        /// 毎回解除しないのは、モード外の種別を意図的に選ぶ操作
        /// （高度選択の頂点/辺/面同時選択など）の結果まで消さないため。
        /// </summary>
        private void ApplySelectMode()
        {
            var m = EffectiveSelectMode;

            bool modeChanged = !_lastAppliedSelectMode.HasValue
                               || _lastAppliedSelectMode.Value != m;
            _lastAppliedSelectMode = m;

            bool released = false;

            var model = ActiveProject?.CurrentModel;
            if (model?.MeshContextList != null)
            {
                foreach (var mc in model.MeshContextList)
                {
                    if (mc?.Selection == null || mc.Type == MeshType.Bone) continue;
                    mc.Selection.Mode = m;
                    if (modeChanged) released |= ReleaseDisabledSelections(mc.Selection, m);
                }
            }

            released |= WriteSelectMode(_selectionState,               m, modeChanged);
            released |= WriteSelectMode(_selectionOps?.SelectionState, m, modeChanged);
            released |= WriteSelectMode(_renderer?.CurrentSelectionState, m, modeChanged);

            // 解除が起きたときだけ GPU 選択フラグとサブパネルを更新する。
            if (released) _selectionOps?.OnSelectionChanged?.Invoke();

            _activePanel?.MarkDirtyRepaint();
        }

        /// <summary>1 個の SelectionState へモードを書き、必要なら無効種別を解除する。</summary>
        private static bool WriteSelectMode(SelectionState sel, MeshSelectMode m, bool release)
        {
            if (sel == null) return false;
            sel.Mode = m;
            return release && ReleaseDisabledSelections(sel, m);
        }

        /// <summary>モードで無効になった種別の選択を解除する。解除したら true。</summary>
        private static bool ReleaseDisabledSelections(SelectionState sel, MeshSelectMode m)
        {
            if (sel == null) return false;

            bool changed = false;
            if (!m.Has(MeshSelectMode.Vertex) && sel.Vertices.Count > 0) { sel.Vertices.Clear(); changed = true; }
            if (!m.Has(MeshSelectMode.Edge)   && sel.Edges   .Count > 0) { sel.Edges   .Clear(); changed = true; }
            if (!m.Has(MeshSelectMode.Face)   && sel.Faces   .Count > 0) { sel.Faces   .Clear(); changed = true; }
            if (!m.Has(MeshSelectMode.Line)   && sel.Lines   .Count > 0) { sel.Lines   .Clear(); changed = true; }
            return changed;
        }

        /// <summary>
        /// ツール固有 override を設定して即適用する。
        /// ツール内部でクリック対象が切り替わる場合（ナイフの段、EdgeTopology の
        /// サブモード、高度選択のサブモード）にハンドラ側から呼ばれる。
        /// </summary>
        private void SetToolSelectModeOverride(MeshSelectMode mode)
        {
            _toolSelectModeOverride = mode;
            ApplySelectMode();
        }

        /// <summary>
        /// InteractionMode ごとのツール固有選択モードを返す。null はユーザ指定に従う。
        ///
        /// ここが「チェックボックスとは無関係にツールが要求する種別」の唯一の一覧。
        /// 例: 面追加は面を張る点を拾うツールなので、チェックボックスの内容に関わらず頂点のみ。
        /// </summary>
        private MeshSelectMode? ResolveToolSelectModeOverride(InteractionMode mode)
        {
            switch (mode)
            {
                // 面追加: 面を張る頂点だけを拾う。辺・面のホバーは有害。
                case InteractionMode.AddFace:           return MeshSelectMode.Vertex;

                // 辺を対象にするツール
                case InteractionMode.EdgeBevel:         return MeshSelectMode.Edge;
                case InteractionMode.FaceMerge:         return MeshSelectMode.Edge;
                case InteractionMode.EdgeExtrude:       return MeshSelectMode.Edge | MeshSelectMode.Line;
                // 辺群ブリッジ: 拾うのは常に辺。頂点・面のホバーは有害。
                case InteractionMode.EdgeBridge:        return MeshSelectMode.Edge;

                // 面を対象にするツール
                case InteractionMode.FaceExtrude:       return MeshSelectMode.Face;
                case InteractionMode.FlipFace:          return MeshSelectMode.Face;
                case InteractionMode.Solidify:          return MeshSelectMode.Face;
                case InteractionMode.DeleteFace:        return MeshSelectMode.Face;
                case InteractionMode.Tri4To1:           return MeshSelectMode.Face;

                // 頂点を対象にするツール
                case InteractionMode.SkinWeightNumeric: return MeshSelectMode.Vertex;
                case InteractionMode.VertexDissolve:    return MeshSelectMode.Vertex;
                case InteractionMode.Quad4To1:          return MeshSelectMode.Vertex;

                // サブモードで対象が変わるツール
                case InteractionMode.EdgeTopology:
                    return (_edgeTopologyHandler?.ModePublic ?? Poly_Ling.Tools.EdgeTopoMode.Flip)
                               == Poly_Ling.Tools.EdgeTopoMode.Split
                           ? MeshSelectMode.Vertex     // Split は頂点クリックで対角を指定
                           : MeshSelectMode.Edge;      // Flip / Dissolve は辺クリック
                case InteractionMode.Knife:
                    return _knifeHandler?.HoverSelectMode ?? MeshSelectMode.Vertex;
                case InteractionMode.AdvancedSelect:
                    // Belt / EdgeLoop は辺と補助線分、ShortestPath は頂点。
                    // 属性系サブモードは null（ユーザ指定に従う）。
                    return _advancedSelectHandler?.HoverSelectModeOverride;

                // 一時選択サブツール (矩形 / 投げ縄) は、呼び出し元ツールの絞り込みを引き継ぐ。
                // ここでユーザ指定へ戻すと、面ツール中に矩形選択したら頂点が選ばれ、
                // 復帰時にその選択が解除される、という噛み合わない挙動になる。
                case InteractionMode.SelectOnly:        return _toolSelectModeOverride;

                default:
                    // 頂点移動・回転・拡縮・彫刻・格子・オブジェクト移動などは
                    // チェックボックスの指定をそのまま使う。
                    return null;
            }
        }

        /// <summary>
        /// EdgeTopology のサブモード (Flip/Split/Dissolve) 切替に追従して
        /// ツール固有 override を更新する。Split は頂点クリックで対角を指定、
        /// Flip/Dissolve は辺クリックで実行するため、不要な種別のホバーを抑制する。
        /// </summary>
        private void ApplySelectionModeForEdgeTopology(Poly_Ling.Tools.EdgeTopoMode mode)
        {
            if (_interactionMode != InteractionMode.EdgeTopology) return;
            SetToolSelectModeOverride(mode == Poly_Ling.Tools.EdgeTopoMode.Split
                ? MeshSelectMode.Vertex
                : MeshSelectMode.Edge);
        }
    }
}
