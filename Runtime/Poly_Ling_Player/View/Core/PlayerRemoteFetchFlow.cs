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
        // Phase 2a-2g-2 (設計 Z) 以降、以下 2 フィールドは直接参照しなくなった。
        // GPU 反映・選択状態同期は EnterSceneReset 経由で ViewportManager が担う。
        // シグネチャ互換性のためフィールド・コンストラクタ引数は保持する。
        #pragma warning disable CS0414
        private readonly MeshSceneRenderer     _renderer;
        private readonly PlayerSelectionOps    _selectionOps;
        #pragma warning restore CS0414
        private readonly Action<ChangeKind>    _notifyPanels;
        private readonly Action<string>        _setStatus;
        public Action<ModelContext>             OnModelContextReady;

        // フェッチ受信中フラグを Viewer 側へ伝える（true=受信中/false=完了・中断）。
        public Action<bool>                     SetFetchActive;

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

            // 受信中フル GPU 再構築の抑止を開始。
            // このログは受信中抑止の動作確認用。完成時も残す。
            SetFetchActive?.Invoke(true);
            Debug.Log("[Fetch] 開始");
            // Phase 2a-2g-2 (設計 Z): ClearScene はフェッチ完了時の EnterSceneReset(clearScene: true) に統合。
            // ここでの ClearScene 呼出しは削除。

            _client.FetchProjectHeader((json, bin) =>
            {
                if (bin == null || bin.Length < 4)
                {
                    // 中断: 抑止を解除する。
                    // このログは完成時も残す。
                    SetFetchActive?.Invoke(false);
                    Debug.Log("[Fetch] 中断 (project_header 失敗)");
                    _setStatus("project_header 失敗");
                    return;
                }
                _receiver?.ProcessBatch(bin);
                if (ModelCount > 0)
                {
                    FetchAllModelsBatch(0);
                }
                else
                {
                    // モデル0件: 抑止を解除する。このログは完成時も残す。
                    SetFetchActive?.Invoke(false);
                    Debug.Log("[Fetch] 完了 (モデル0件)");
                }
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

                    // ────────────────────────────────────────────────
                    // Phase 2a-2g-2 (設計 Z): データの初期選択設定のみ行う。
                    // GPU 反映 (RebuildAdapter / SetSelectionState / UpdateSelectedDrawableMesh /
                    // カメラ通知) はループ完了後の 1 回にまとめる。
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
                                model.SelectMesh(entry.MasterIndex);
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

                    // DrawWireframeAndVertices 用の selectedMeshIndex を更新
                    OnModelContextReady?.Invoke(model);
                }

                int next = mi + 1;
                if (next < ModelCount)
                {
                    FetchAllModelsBatch(next);
                }
                else
                {
                    // 受信中抑止を解除してから最終再構築を1回だけ行う。
                    // このログは完成時も残す。
                    SetFetchActive?.Invoke(false);
                    Debug.Log($"[Fetch] 完了 ({project?.Name})");

                    // Phase 2a-2g-2 (設計 Z): 全モデルフェッチ完了後、先頭モデルを
                    // CurrentModel にしてから EnterSceneReset で slot 0 に反映。
                    // 「画面に表示するのは常に 1 モデル (CurrentModel)」規約に揃う。
                    if (project != null && project.ModelCount > 0)
                    {
                        project.SelectModel(0);
                        _viewportManager.EnterSceneReset(project, clearScene: true);
                        _viewportManager.EnterCameraChanged(
                            _viewportManager.PerspectiveViewport,
                            CameraChangePhase.Committed);
                    }
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
