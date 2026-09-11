// PlayerViewportManager.Renderer.cs
// ビューポート管理：スキンド変換・MeshSceneRenderer への委譲・カメラ初期位置のリセット。
// Runtime/Poly_Ling_Player/View/Viewport/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    public partial class PlayerViewportManager
    {
        // ================================================================
        // スキンド変換
        // ================================================================

        /// <summary>
        /// スキンドメッシュのワールド変換をGPUバッファに反映する。
        /// model.ComputeWorldMatrices() を呼んだ後に呼ぶこと。
        /// Viewer.Update() で毎フレーム呼ぶことで矩形選択・ホバーの
        /// スクリーン座標がスキンドポーズに追従する。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void UpdateTransform()
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;
            adapter.UpdateTransform(useWorldTransform: true);
            adapter.WritebackTransformedVertices();
        }

        // ================================================================
        // MeshSceneRenderer 委譲
        // ================================================================

        /// <summary>
        /// 展開済み UnityMesh（UV分割で頂点数 > MeshObject.VertexCount）の
        /// 頂点座標を MeshObject.Vertices から直接更新する。
        ///
        /// GPU バッファの _expandedToOriginal マッピングと同じロジックを CPU で再現し、
        /// 展開後 Unity 頂点 → 元頂点インデックスを解決して position をコピーする。
        /// </summary>
        public void UpdateExpandedUnityMesh(
            Poly_Ling.Data.MeshContext mc,
            Poly_Ling.Context.ModelContext model)
        {
            if (mc?.MeshObject == null || mc.UnityMesh == null || model == null) return;

            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            // contextIndex を解決
            int ctxIdx = -1;
            for (int i = 0; i < model.MeshContextCount; i++)
                if (ReferenceEquals(model.GetMeshContext(i), mc)) { ctxIdx = i; break; }
            if (ctxIdx < 0) return;

            int unifiedIdx = adapter.ContextToUnifiedMeshIndex(ctxIdx);
            if (unifiedIdx < 0) return;

            var bm = adapter.BufferManager;
            if (bm == null) return;

            var meshInfos = bm.MeshInfos;
            if (meshInfos == null || unifiedIdx >= meshInfos.Length) return;

            var positions = bm.Positions; // CPU側 _positions 配列
            if (positions == null) return;

            uint vertexStart = meshInfos[unifiedIdx].VertexStart;
            int meshVertCount = mc.MeshObject.VertexCount;

            // ToUnityMesh/ToUnityMeshShared と同じ展開順序でUnityMesh頂点を更新する。
            // 孤立頂点を除外し、頂点順 → UV順 で展開。
            //
            // 【以前ここだけ規則が違っていた】
            //   孤立頂点の判定に face.IsHidden を含めていたため、面を非表示に
            //   すると ToUnityMesh / GPU 展開バッファと展開頂点数が食い違った。
            //   面の非表示は編集補助であり、展開 index 空間を変えてはならない。
            //   現在は MeshExpansion に一本化してある。
            var mo = mc.MeshObject;

            int totalUnity = mc.UnityMesh.vertexCount;
            int expectedExpanded = Poly_Ling.MeshBridge.MeshExpansion.CountExpanded(mo);
            if (expectedExpanded != totalUnity)
            {
                // UnityMesh と MeshObject の位相が食い違っている。このまま書くと
                // 別の頂点へ座標を入れるので何もしない。作り直しは
                // EnterTopologyChanged（RebuildSelectedUnityMeshes）の責務。
                return;
            }

            var unityVerts = new UnityEngine.Vector3[totalUnity];
            var worldMatrix = mc.WorldMatrix;

            Poly_Ling.MeshBridge.MeshExpansion.Enumerate(mo, (vIdx, uvIdx, expIdx) =>
            {
                unityVerts[expIdx] = worldMatrix.MultiplyPoint3x4(mo.Vertices[vIdx].Position);
            });

            mc.UnityMesh.vertices = unityVerts;
            mc.UnityMesh.RecalculateBounds();
        }

        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void RebuildAdapter(int mi, ModelContext model)
        {
#pragma warning disable CS0618
            _renderer?.RebuildAdapter(mi, model);
#pragma warning restore CS0618
            if (mi == 0) ApplySnapHitTestEnabled();
        }

        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void ClearScene()
        {
#pragma warning disable CS0618
            _renderer?.ClearScene();
#pragma warning restore CS0618
        }

        /// <summary>
        /// CPU の MeshObject 位置を GPU バッファに同期する。
        ///
        /// 【呼び出しタイミング】
        ///   MoveToolHandler.OnSyncMeshPositions（頂点ドラッグ中の毎フレーム位置更新）。
        ///
        /// 【処理順序】
        ///   1. MeshObject.Positions → GPU _positionsBuffer（UpdatePositions）
        ///   2. NotifyTransformChanged で次フレームのワイヤー/頂点メッシュ再構築を予約
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void SyncMeshPositionsAndTransform(
            Poly_Ling.Data.MeshContext mc,
            Poly_Ling.Context.ModelContext model)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized)
            {
                PLDiag.Sync($"SyncPos adapter=null_or_uninit ({(adapter == null ? "null" : "uninit")})");
                return;
            }
            if (mc?.MeshObject == null || model == null) return;

            var bm = adapter.BufferManager;
            if (bm == null) { PLDiag.Sync("SyncPos bm=null"); return; }

            // MeshContext → contextIndex → unifiedMeshIndex
            int ctxIdx = -1;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (ReferenceEquals(model.GetMeshContext(i), mc)) { ctxIdx = i; break; }
            }
            if (ctxIdx < 0) { PLDiag.Sync("SyncPos ctxIdx=-1 (mc not found)"); return; }

            int unifiedIdx = adapter.ContextToUnifiedMeshIndex(ctxIdx);
            if (unifiedIdx < 0) { PLDiag.Sync($"SyncPos unifiedIdx=-1 (ctxIdx={ctxIdx})"); return; }

            // 頂点ドラッグ中は毎フレーム通る。既定では出さない（PLDiag.EditSync）。
            PLDiag.Sync($"SyncPos UpdatePositions ctxIdx={ctxIdx} unifiedIdx={unifiedIdx} V={mc.MeshObject.VertexCount}");
            // ① CPU MeshObject.Positions（またはWorkingPositions）→ GPU _positionsBuffer
            bm.UpdatePositions(mc, unifiedIdx);

            // ② ミラーバッファも同期（ミラー無効時は内部で早期リターン）
            bm.UpdateMirrorPositions(unifiedIdx);

            // ③ ミラーメッシュが存在する場合、座標をMeshObjectに反映してGPUバッファ同期
            // MirrorPair方式（BakeMirror=false, MirrorSide型）
            var mirrorPair = model.GetMirrorPair(mc);
            if (mirrorPair != null && mirrorPair.Real == mc && mirrorPair.Mirror?.MeshObject != null)
            {
                var mirrorMesh = mirrorPair.Mirror.MeshObject;
                if (mc.WorkingPositions != null && mirrorPair.Mirror.WorkingPositions == null)
                {
                    // Mirror側に自前のWorkingPositionsがない場合のみ、Real側のWorkingPositions込みでミラー反映。
                    // Mirror側がWorkingPositionsを持つ場合（PMX両側モーフ等）はSyncPositionsに留め、
                    // 後続のSyncMeshContextPositionsOnly(Mirror)でUpdatePositions(MeshContext)が処理する。
                    for (int i = 0; i < mirrorPair.VertexMap.Length && i < mc.MeshObject.VertexCount; i++)
                    {
                        int mi = mirrorPair.VertexMap[i];
                        if (mi < 0 || mi >= mirrorMesh.VertexCount) continue;
                        var offset = (i < mc.WorkingPositions.Length) ? mc.WorkingPositions[i] : UnityEngine.Vector3.zero;
                        // Vertices[].Position も WorkingPositions もローカル座標。
                        // ミラーはローカル空間で行う（Real / Mirror は同一の WorldMatrix を共有する）。
                        var localPos = mc.MeshObject.Vertices[i].Position + offset;
                        mirrorMesh.Vertices[mi].Position = mirrorPair.MirrorPosition(localPos);
                    }
                }
                else
                {
                    mirrorPair.SyncPositions();
                }
                int mirrorCtxIdx = model.MeshContextList.IndexOf(mirrorPair.Mirror);
                if (mirrorCtxIdx >= 0)
                {
                    int mirrorUnifiedIdx = adapter.ContextToUnifiedMeshIndex(mirrorCtxIdx);
                    if (mirrorUnifiedIdx >= 0)
                        bm.UpdatePositions(mirrorPair.Mirror, mirrorUnifiedIdx);
                }
            }

            // BakedMirror方式（BakeMirror=true, BakedMirror型）
            // WorkingPositions が設定されている場合（モーフプレビュー等）はスキップ。
            // BakedMirrorメッシュは自身の WorkingPositions を使い SyncMeshContextPositionsOnly で
            // 独立してGPUを更新するため、ここで上書きすると二重適用になる。
            // WorkingPositions == null（通常の頂点編集）の場合のみ実行する。
            if (mc.WorkingPositions == null)
            {
                var mirrorMatrix = bm.GetMirrorMatrix();
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mirrorCtx = model.GetMeshContext(i);
                    if (mirrorCtx?.BakedMirrorSourceIndex != ctxIdx) continue;
                    if (mirrorCtx.MeshObject == null) continue;

                    int mirrorUnifiedIdx = adapter.ContextToUnifiedMeshIndex(i);
                    if (mirrorUnifiedIdx < 0) continue;

                    int count = Mathf.Min(mc.MeshObject.VertexCount, mirrorCtx.MeshObject.VertexCount);
                    for (int v = 0; v < count; v++)
                    {
                        var basePos = mc.MeshObject.Vertices[v].Position;
                        mirrorCtx.MeshObject.Vertices[v].Position = mirrorMatrix.MultiplyPoint3x4(basePos);
                    }
                    bm.UpdatePositions(mirrorCtx.MeshObject, mirrorUnifiedIdx);
                }
            }

            // ④ 次フレームのワイヤー/頂点メッシュ再構築を予約
            adapter.NotifyTransformChanged();
        }

        /// <summary>
        /// 頂点移動後に呼ぶ。次フレームのGPUバッファ更新を予約する。
        /// </summary>
        public void NotifyTransformChanged()
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null) return;
            adapter.NotifyTransformChanged();
        }

        // ================================================================
        // カメラ初期位置リセット
        // ================================================================

        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void ResetToMesh(Bounds bounds)
        {
            PerspectiveViewport?.ResetToMesh(bounds);
            TopViewport        ?.ResetToMesh(bounds);
            FrontViewport      ?.ResetToMesh(bounds);
            SideViewport       ?.ResetToMesh(bounds);

            // リセットは Perspective vp 経由で呼ばれるため、ortho の遅延ズーム解決
            // （pixelHeight 確定時）を確実に行うべく、全ビューの transform を適用する。
            ApplyAllViewportCameraTransforms();
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>
        /// カメラパラメータの変化が、この slot のカリング結果を変え得るかを返す。
        /// false を返せる場合だけ DispatchCullingForDisplay を省略できる。
        ///
        /// 【false を返してよい根拠】
        ///   面の表裏は UnifiedCompute.compute の ComputeFaceVisibility が
        ///   スクリーン空間の符号付き面積（ショエレース公式）の**符号だけ**で決める。
        ///   真の正射影では
        ///     ・パン         … スクリーン空間の平行移動
        ///     ・ズーム       … スクリーン空間の正の一様拡大
        ///   のいずれも符号を変えない。よってカメラ姿勢が同じなら結果は同一になる。
        ///   線分カリング（ComputeLineVisibility）は _FaceCulledBuffer に従属するので
        ///   同じ結論。頂点カリングも ComputeFaceVisibility が書く。
        ///
        /// 【true を返さなければならない場合】
        ///   ・透視投影（Perspective ビュー、および 3 面の「パース表示」ON）
        ///       平行移動でも視線が変わり、符号が変わり得る。
        ///   ・カメラ姿勢が変わった
        ///       Perspective のオービット、3 面の斜め 45°（RigRotation）、
        ///       フリップ（Top↔Bottom 等）が該当する。
        ///   ・この slot でまだ一度もカリングを計算していない
        ///   ・前回は透視で今回は正射影（またはその逆）
        ///
        /// 【この判定はカメラ dirty にしか使えない】
        ///   ジオメトリ・FLAG_HIDDEN・FLAG_MESH_SELECTED・ミラー表示設定・
        ///   背面カリング ON-OFF の変更はカメラと無関係に結果を変える。
        ///   それらは _slotContentDirty 側で扱い、本判定を通さない。
        /// </summary>
        private bool CameraChangeAffectsCulling(int slot, Camera cam)
        {
            if (cam == null) return true;
            if (slot < 0 || slot >= _slotCullValid.Length) return true;

            // まだ一度も計算していない slot は比較対象が無い。
            if (!_slotCullValid[slot]) return true;

            // 透視投影は平行移動でも符号が変わり得る。
            if (!cam.orthographic) return true;

            // 投影種別が切り替わった（パース表示トグル等）。
            if (!_slotCullOrthographic[slot]) return true;

            // 姿勢が変わっていなければ、パン・ズームだけの変化とみなせる。
            // しきい値は「同一値の再計算による浮動小数点の揺れ」を吸収する程度に取る。
            const float RotationEpsilonDeg = 1e-3f;
            return Quaternion.Angle(_slotCullRotation[slot], cam.transform.rotation)
                   > RotationEpsilonDeg;
        }

        /// <summary>
        /// 【event 駆動・内部】指定 slot の描画準備（計算・Prepare）を実行する。
        /// display settings の適用・カリング Dispatch・CPU Mesh 再構築・Queue 登録を行う。
        /// Submit は一切行わない（そちらは SubmitForCamera が担当）。
        /// </summary>
        private void PrepareViewport(ProjectContext project, PlayerViewport vp, int slot)
        {
            if (vp == null || !vp.IsReady) return;

            // 軸/グリッドはモデル未ロード時も表示するため adapter 判定より前に行う。
            // 設定が変化していなければ Prepare 内で即 return する。
            EnsureGridPrepared();

            var cam     = vp.Cam;
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            // 面ごとの表示設定をレンダラーに適用する。
            // Prepare 呼び出しはシーケンシャルなので面をまたいだ競合はない。
            var ds = _displaySettings[slot];
            _renderer.BackfaceCullingEnabled    = ds.BackfaceCulling;
            _renderer.ShowSelectedMesh          = ds.ShowSelectedMesh;
            _renderer.ShowSelectedWireframe     = ds.ShowSelectedWireframe;
            _renderer.ShowSelectedVertices      = ds.ShowSelectedVertices;
            _renderer.ShowSelectedBone          = ds.ShowSelectedBone;
            _renderer.ShowUnselectedMesh        = ds.ShowUnselectedMesh;
            _renderer.ShowUnselectedWireframe   = ds.ShowUnselectedWireframe;
            _renderer.ShowUnselectedVertices    = ds.ShowUnselectedVertices;
            _renderer.ShowUnselectedBone        = ds.ShowUnselectedBone;
            _renderer.ShowSelectedMirror        = ds.ShowSelectedMirror;
            _renderer.ShowUnselectedMirrorMesh  = ds.ShowUnselectedMirrorMesh;
            _renderer.ShowSelectedMeshOrigin    = ds.ShowSelectedMeshOrigin;
            _renderer.ShowUnselectedMeshOrigin  = ds.ShowUnselectedMeshOrigin;
            _renderer.ShowMirrorMeshOrigin      = ds.ShowMirrorMeshOrigin;
            _renderer.ShowNormals               = ds.ShowNormals;

            // くさび（ボーン／メッシュ原点マーカー）の大きさは 4 面共通のため
            // ViewportDisplaySettings ではなく ViewportGridSettings から取る。
            _renderer.BoneMarkerScale           = _gridSettings.BoneMarkerScale;

            // adapter の BackfaceCullingEnabled もここで同期する
            // （DispatchCullingForDisplay の引数に使用するため）。
            adapter.BackfaceCullingEnabled = ds.BackfaceCulling;

            // 永続ミラーの per-slot 表示状態を bufMgr へ設定する。
            // （直後の DispatchCullingForDisplay(slot) と、後段のアクティブ slot 最終カリングが
            //   slot ごとの正しい値を参照できるようにするため。）
            // 選択側は UI トグルを持たず選択 Mesh に従属するため、頂点・辺に同じ値を渡す。
            // 非選択側は WithMirrorClamped が親（ShowUnselectedMirror）を既に反映済みなので、
            // ここでは子の値をそのまま渡す。AND を二重に書かない。
            adapter.BufferManager?.SetMirrorDisplay(
                slot,
                ds.ShowSelectedMirror,           ds.ShowSelectedMirror,
                ds.ShowUnselectedMirrorVertices, ds.ShowUnselectedMirrorWireframe);

            // ここが per-slot カリングを発行する唯一の場所。
            //
            // 再計算する条件:
            //   ・内容 dirty       … 無条件で再計算
            //   ・カメラ dirty     … カリング結果が変わり得るときだけ再計算
            //
            // readback は既定の false。ここで読み戻すと 4 slot ぶんの
            // GetData が走り、しかも単一配列 _screenPositions を
            // 最後の slot のカメラで上書きしてしまう。
            // CPU が読む値は PresentAll 末尾の最終確定 1 回で作る。
            bool recomputeCulling =
                _slotContentDirty[slot]
                || (_slotCameraDirty[slot] && CameraChangeAffectsCulling(slot, cam));

            if (recomputeCulling)
            {
                adapter.DispatchCullingForDisplay(cam, adapter.BackfaceCullingEnabled, slot);

                // 次回の比較用に、いま計算に使ったカメラ姿勢と投影種別を控える。
                _slotCullRotation[slot]     = cam.transform.rotation;
                _slotCullOrthographic[slot] = cam.orthographic;
                _slotCullValid[slot]        = true;
            }

            // 判定が済んだら両方とも降ろす。飛ばした場合も、飛ばした根拠は
            // 「カリング結果が変わらない」なので持ち越す必要はない。
            _slotContentDirty[slot] = false;
            _slotCameraDirty[slot]  = false;

            // Prepare 系のみ呼び出す。Submit は OnRenderObject / SubmitForCamera で行う。
            // PrepareNormals は PrepareWireframeAndVertices より前に呼ぶこと。
            // PrepareWireframeAndVertices は末尾で adapter.ConsumeNormalMode() を呼び
            // Normal モードを Idle へ降格させるため、後に置くと法線メッシュが
            // 二度と再構築されなくなる。
            // PrepareWeightVisualization はここでは呼ばない。
            // slot 非依存の処理であり、PresentAll 側で 1 回だけ実行する。
            _renderer.PrepareNormals(project);
            _renderer.PrepareWireframeAndVertices(cam, project, slot);
            _renderer.PrepareBones(project);
        }
    }
}
