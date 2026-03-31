// PlayerRemoteFetchFlow.cs
// リモートフェッチフロー（FetchProject / FetchAllModelsBatch / FetchMeshDataBatch）。
// PolyLingPlayerViewer から分離したクラス。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerRemoteFetchFlow
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly PolyLingPlayerClient  _client;
        private readonly RemoteProjectReceiver _receiver;
        private readonly PlayerLocalLoader     _localLoader;
        private readonly PlayerViewportManager _viewportManager;
        private readonly MeshSceneRenderer     _renderer;
        private readonly PlayerSelectionOps    _selectionOps;
        private readonly Action<ChangeKind>    _notifyPanels;
        private readonly Action<string>        _setStatus;

        // ================================================================
        // 状態
        // ================================================================

        /// <summary>
        /// OnProjectHeaderReceived で Viewer 側から設定される。
        /// </summary>
        public int ModelCount           { get; set; }
        public int FetchingModelIndex   { get; private set; } = -1;

        // ================================================================
        // 初期化
        // ================================================================

        public PlayerRemoteFetchFlow(
            PolyLingPlayerClient  client,
            RemoteProjectReceiver receiver,
            PlayerLocalLoader     localLoader,
            PlayerViewportManager viewportManager,
            MeshSceneRenderer     renderer,
            PlayerSelectionOps    selectionOps,
            Action<ChangeKind>    notifyPanels,
            Action<string>        setStatus)
        {
            _client          = client;
            _receiver        = receiver;
            _localLoader     = localLoader;
            _viewportManager = viewportManager  ?? throw new ArgumentNullException(nameof(viewportManager));
            _renderer        = renderer         ?? throw new ArgumentNullException(nameof(renderer));
            _selectionOps    = selectionOps;
            _notifyPanels    = notifyPanels     ?? throw new ArgumentNullException(nameof(notifyPanels));
            _setStatus       = setStatus        ?? throw new ArgumentNullException(nameof(setStatus));
        }

        // ================================================================
        // フェッチフロー
        // ================================================================

        public void FetchProject()
        {
            if (_client == null || !_client.IsConnected) return;
            _localLoader?.Clear();
            _setStatus("project_header フェッチ中...");
            _receiver?.Reset();
            ModelCount         = 0;
            FetchingModelIndex = -1;
            _viewportManager.ClearScene();

            _client.FetchProjectHeader((json, bin) =>
            {
                if (bin == null || bin.Length < 4) { _setStatus("project_header 失敗"); return; }
                _receiver?.ProcessBatch(bin);
                if (ModelCount > 0) FetchAllModelsBatch(0);
            });
        }

        private void FetchAllModelsBatch(int mi)
        {
            if (mi >= ModelCount) return;
            FetchingModelIndex = mi;
            _setStatus($"メッシュフェッチ中... [{mi}/{ModelCount - 1}]");

            FetchMeshDataBatch(mi, "bone",     () =>
            FetchMeshDataBatch(mi, "drawable", () =>
            FetchMeshDataBatch(mi, "morph",    () =>
            {
                var project = _receiver?.Project;
                if (project != null && mi < project.ModelCount)
                {
                    var model = project.Models[mi];
                    _viewportManager.RebuildAdapter(mi, model);

                    // ────────────────────────────────────────────────
                    // RebuildAdapter 後の初期選択設定
                    //
                    // 【描画メッシュ選択】
                    //   DrawableMeshes から頂点数がゼロでない先頭を選択する。
                    //
                    // 【ボーン選択】
                    //   「首」ボーンを優先し、なければ先頭ボーンを選択する。
                    // ────────────────────────────────────────────────

                    // 先頭の非空 Drawable を選択
                    var drawables = model.DrawableMeshes;
                    if (drawables != null)
                    {
                        foreach (var entry in drawables)
                        {
                            var mc = entry.Context;
                            if (mc?.MeshObject != null
                                && mc.MeshObject.VertexCount > 0
                                && mc.IsVisible)
                            {
                                model.SelectDrawableMesh(entry.MasterIndex);
                                break;
                            }
                        }
                    }

                    // 首ボーン（または先頭ボーン）を選択
                    int neckIdx = -1, firstBoneIdx = -1;
                    for (int ci = 0; ci < model.MeshContextCount; ci++)
                    {
                        var bmc = model.GetMeshContext(ci);
                        if (bmc == null || bmc.Type != MeshType.Bone) continue;
                        if (firstBoneIdx < 0) firstBoneIdx = ci;
                        string n = bmc.Name ?? "";
                        if (n == "首" || n.ToLower() == "neck") { neckIdx = ci; break; }
                    }
                    int selectedBone = neckIdx >= 0 ? neckIdx : firstBoneIdx;
                    if (selectedBone >= 0)
                        model.SelectBone(selectedBone);

                    // SelectionState 同期（選択描画メッシュの Selection を使う）
                    var firstMc = model.FirstSelectedDrawableMesh;
                    if (firstMc != null)
                    {
                        _selectionOps?.SetSelectionState(firstMc.Selection);
                        _renderer?.SetSelectionState(firstMc.Selection);
                    }

                    // DrawWireframeAndVertices 用の selectedMeshIndex を更新
                    _renderer?.UpdateSelectedDrawableMesh(mi, model);

                    // カメラパラメータをアダプターに設定（UpdateFrame 1回）
                    _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);
                }

                int next = mi + 1;
                if (next < ModelCount) FetchAllModelsBatch(next);
                else
                {
                    _setStatus($"完了 ({project?.Name})");
                    _notifyPanels(ChangeKind.ModelSwitch);
                }
            })));
        }

        private void FetchMeshDataBatch(int mi, string cat, Action done)
        {
            _client.FetchMeshDataBatch(mi, cat, (json, bin) =>
            {
                if (bin != null && bin.Length >= 4) _receiver?.ProcessBatch(bin);
                done?.Invoke();
            });
        }
    }
}
