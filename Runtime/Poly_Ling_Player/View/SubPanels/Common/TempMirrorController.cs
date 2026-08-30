// TempMirrorController.cs
// ツール内「一時ミラー」の実体化・解除と、その所有権を管理する。
//
// 【目的】
// 頂点移動・スカルプト・法線編集など多くのツールで「作業中だけ反対側の実体を生やす」
// ニーズがある。ミラー実体化そのものは既存の BakeMirrorCommand / UnbakeMirrorCommand で
// 行えるが、
//   ・どのツールが実体化したのか
//   ・どのメッシュを実体化したのか
// を覚えておかないと「ツールを離れたら自動で解除する」が実現できない。
// その記憶と後始末をこのクラスが持つ。
//
// 【所有権】
// このコントローラが実体化したメッシュだけを所有リストへ入れ、解除はそのリストのみを
// 対象にする。左ペインの「一時ミラー」パネルから実体化されたメッシュ（実行前から
// MirrorBakeState を持つメッシュ）は所有しないので、ツール遷移で勝手に解除されない。
//
// 【対象範囲】
// 実体化の対象は ModelContext.SelectedDrawableMeshIndices の全メッシュ。
// スカルプト等のブラシ系ツールが同じ集合を対象にしているため、それに揃える。
// 実体化した時点の索引を保持するので、実体化中に選択メッシュが変わっても
// 解除対象はずれない。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Common/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    /// <summary>
    /// ツール内「一時ミラー」の状態を持つコントローラ。
    /// 実処理は既存の BakeMirrorCommand / UnbakeMirrorCommand へ委譲する。
    /// </summary>
    public class TempMirrorController
    {
        // ── 外部注入 ──────────────────────────────────────────────────
        public Func<ProjectContext> GetProject;
        public Action<PanelCommand> SendCommand;

        /// <summary>実体化・解除の後に呼ばれる（サブパネルのボタン表示同期用）。</summary>
        public Action OnStateChanged;

        // ── 状態 ─────────────────────────────────────────────────────

        /// <summary>このコントローラが実体化したメッシュの masterIndex。</summary>
        private readonly List<int> _ownedMasterIndices = new List<int>();

        /// <summary>実体化時のモデル索引。解除は同じモデルに対してのみ行う。</summary>
        private int _ownerModelIndex = -1;

        /// <summary>実体化したツール（PolyLingPlayerViewerCore.InteractionMode を int 化した値）。</summary>
        private int _ownerToken = -1;

        /// <summary>Dispatch 経由のパネル通知から再入するのを防ぐ。</summary>
        private bool _busy;

        /// <summary>一時ミラーが有効か。</summary>
        public bool IsActive => _ownedMasterIndices.Count > 0;

        /// <summary>一時ミラーを実体化したツールの識別値。未実体化なら -1。</summary>
        public int OwnerToken => _ownerToken;

        /// <summary>直近の実行結果メッセージ（サブパネル表示用）。</summary>
        public string LastMessage { get; private set; } = "";

        // ── 実体化 ───────────────────────────────────────────────────

        /// <summary>
        /// 選択中の描画メッシュすべてをミラー実体化し、このコントローラの所有にする。
        /// 既に実体化済みのメッシュは対象外（所有もしない）。
        /// </summary>
        /// <param name="ownerToken">実体化したツールの識別値。</param>
        /// <returns>実体化に成功したメッシュ数。</returns>
        public int Bake(int ownerToken)
        {
            if (_busy) return 0;
            if (IsActive) { LastMessage = "既に一時ミラー中です"; return 0; }

            var project = GetProject?.Invoke();
            var model   = project?.CurrentModel;
            if (model == null) { LastMessage = "モデルがありません"; return 0; }
            if (SendCommand == null) { LastMessage = "コマンド送信先が未設定です"; return 0; }

            var targets = new List<int>(model.SelectedDrawableMeshIndices);
            if (targets.Count == 0) { LastMessage = "メッシュを選択してください"; return 0; }

            int modelIndex = project.CurrentModelIndex;

            _busy = true;
            try
            {
                foreach (int masterIndex in targets)
                {
                    var mc = model.GetMeshContext(masterIndex);
                    if (mc?.MeshObject == null) continue;

                    // 既に実体化されているメッシュは触らない。
                    // （左ペインの「一時ミラー」パネルから実体化されたものを奪わないため）
                    if (mc.MeshObject.MirrorBakeState != null) continue;

                    // 選択頂点を境界にするモードでは、そのメッシュに選択頂点が要る。
                    if (TempMirrorSettings.BoundaryMode == MirrorBoundaryMode.SelectedVertices
                        && (mc.Selection?.Vertices.Count ?? 0) == 0)
                        continue;

                    SendCommand.Invoke(new BakeMirrorCommand(
                        modelIndex, masterIndex,
                        TempMirrorSettings.MirrorAxis,
                        TempMirrorSettings.Threshold,
                        TempMirrorSettings.FlipU,
                        TempMirrorSettings.PlaneOffset,
                        TempMirrorSettings.BoundaryMode,
                        TempMirrorSettings.ProjectBoundary));

                    // 実際に実体化できたものだけを所有する。
                    if (mc.MeshObject.MirrorBakeState != null)
                        _ownedMasterIndices.Add(masterIndex);
                }
            }
            finally
            {
                _busy = false;
            }

            if (_ownedMasterIndices.Count > 0)
            {
                _ownerModelIndex = modelIndex;
                _ownerToken     = ownerToken;
                LastMessage     = $"一時ミラー: {_ownedMasterIndices.Count} メッシュを実体化";
            }
            else
            {
                _ownerModelIndex = -1;
                _ownerToken      = -1;
                LastMessage      = "一時ミラー化できるメッシュがありません";
            }

            Debug.Log($"[TempMirror] bake owner={ownerToken} model={modelIndex} "
                    + $"targets={targets.Count} owned={_ownedMasterIndices.Count}");

            OnStateChanged?.Invoke();
            return _ownedMasterIndices.Count;
        }

        // ── 解除 ─────────────────────────────────────────────────────

        /// <summary>
        /// このコントローラが実体化したメッシュだけを解除し、所有リストを空にする。
        /// 解除時は実体化前のミラー設定へ戻す（一時的な作業なので、恒久設定を変えない）。
        /// </summary>
        /// <returns>解除したメッシュ数。</returns>
        public int Unbake()
        {
            if (_busy) return 0;
            if (!IsActive) return 0;

            var project = GetProject?.Invoke();
            var model   = project?.CurrentModel;

            int done = 0;

            _busy = true;
            try
            {
                // 解除できない状況（モデル切替後など）でも所有リストは必ず空にする。
                // 残しておくと次のツールで誤って別メッシュを解除しに行くため。
                if (model != null && SendCommand != null
                    && project.CurrentModelIndex == _ownerModelIndex)
                {
                    // 実体化と逆順に戻す。
                    for (int i = _ownedMasterIndices.Count - 1; i >= 0; i--)
                    {
                        int masterIndex = _ownedMasterIndices[i];
                        var mc = model.GetMeshContext(masterIndex);
                        if (mc?.MeshObject?.MirrorBakeState == null) continue;

                        SendCommand.Invoke(new UnbakeMirrorCommand(
                            _ownerModelIndex, masterIndex,
                            TempMirrorSettings.WriteBack,
                            restoreSavedMirrorSettings: true));

                        if (mc.MeshObject.MirrorBakeState == null) done++;
                    }
                }
                else
                {
                    Debug.LogWarning(
                        "[TempMirror] 解除対象のモデルが現在のモデルと違うため、状態のみ破棄します "
                        + $"owner={_ownerModelIndex} current={(project != null ? project.CurrentModelIndex : -1)}");
                }
            }
            finally
            {
                _busy = false;
            }

            Debug.Log($"[TempMirror] unbake owner={_ownerToken} owned={_ownedMasterIndices.Count} done={done}");

            _ownedMasterIndices.Clear();
            _ownerModelIndex = -1;
            _ownerToken      = -1;
            LastMessage      = done > 0 ? $"一時ミラー解除: {done} メッシュ" : "一時ミラーを解除しました";

            OnStateChanged?.Invoke();
            return done;
        }

        /// <summary>ボタン用トグル。実体化中なら解除、そうでなければ実体化する。</summary>
        public void Toggle(int ownerToken)
        {
            if (IsActive) Unbake();
            else          Bake(ownerToken);
        }
    }
}
