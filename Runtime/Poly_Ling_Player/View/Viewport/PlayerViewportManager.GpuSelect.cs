// PlayerViewportManager.GpuSelect.cs
// ビューポート管理：矩形選択用の GPU データ取得。
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
        // 矩形選択用 GPU データ取得（MoveToolHandler コールバック用）
        // ================================================================

        /// <summary>
        /// GPU が計算したスクリーン座標配列（全頂点グローバルインデックス）を返す。
        /// ReadBackVertexFlags() の後に呼ぶこと。
        ///
        /// MoveToolHandler.GetScreenPositions コールバックに設定して使う。
        /// </summary>
        public Vector2[] GetScreenPositions()
        {
            return _renderer?.GetAdapter(0)?.BufferManager?.GetScreenPositions();
        }

        /// <summary>
        /// 指定コンテキストインデックスのメッシュの頂点グローバルオフセットを返す。
        /// GetScreenPositions() の配列インデックス計算に使う。
        ///
        /// MoveToolHandler.GetVertexOffset コールバックに設定して使う。
        /// </summary>
        public int GetVertexOffset(int meshContextIndex)
        {
            return _renderer?.GetAdapter(0)?.GetVertexOffset(meshContextIndex) ?? 0;
        }

        /// <summary>
        /// グローバル頂点インデックスが背面カリングで可視かどうかを返す。
        /// ReadBackVertexFlags() の後に有効。
        ///
        /// 内部で IsVertexCulled(meshIndex, localIndex) を使う。
        /// グローバルインデックスからメッシュ・ローカルインデックスへの変換を行う。
        ///
        /// MoveToolHandler.IsVertexVisible コールバックに設定して使う。
        /// </summary>
        public bool IsVertexVisible(int globalVertexIndex)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null) return true;
            if (!adapter.BackfaceCullingEnabled) return true;

            // グローバルインデックス → メッシュインデックス + ローカルインデックス
            if (adapter.BufferManager?.GlobalToLocalVertexIndex(
                    globalVertexIndex, out int meshIdx, out int localIdx) == true)
            {
                // IsVertexBackfaceCulled は ReadBackVertexCulled でキャッシュされた
                // _VertexCulledBuffer (per-slot) の結果を参照する。
                // IsVertexCulled (_vertexFlags & FLAG_CULLED) は画面外用なので使わない。
                return !adapter.IsVertexBackfaceCulled(meshIdx, localIdx);
            }
            return true;
        }

        /// <summary>
        /// GPU ホバー結果を PlayerHoverElement（種別付き）に変換して返す。
        /// SelectionState.Mode に応じた要素種別を返す。
        ///
        /// 優先順位: 頂点 > 辺/補助線分 > 面
        /// （Mode で無効な種別はスキップ）
        /// </summary>
        public PlayerHoverElement GetHoverElement(
            Poly_Ling.Selection.MeshSelectMode mode,
            Poly_Ling.Context.ModelContext model)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized)
                return PlayerHoverElement.None;

            // ---- 頂点 ----
            if (mode.Has(Poly_Ling.Selection.MeshSelectMode.Vertex))
            {
                int gv = adapter.HoverVertexIndex;
                if (gv >= 0 && adapter.BufferManager?.GlobalToLocalVertexIndex(
                        gv, out int vm, out int vl) == true)
                {
                    int ctxV = UnifiedToContextIndex(adapter, model, vm);
                    if (ctxV >= 0)
                        return new PlayerHoverElement
                        {
                            Kind        = PlayerHoverKind.Vertex,
                            MeshIndex   = ctxV,
                            VertexIndex = vl,
                        };
                }
            }

            // ---- 辺 / 補助線分 ----
            bool wantEdge = mode.Has(Poly_Ling.Selection.MeshSelectMode.Edge);
            bool wantLine = mode.Has(Poly_Ling.Selection.MeshSelectMode.Line);
            if (wantEdge || wantLine)
            {
                int gl = adapter.HoverLineIndex;
                if (gl >= 0 && adapter.BufferManager?.GlobalToLocalLineIndex(
                        gl, out int lm, out int ll) == true)
                {
                    int ctxL = UnifiedToContextIndex(adapter, model, lm);
                    if (ctxL >= 0)
                    {
                        // IsAuxLine 判定
                        var lines = adapter.BufferManager.Lines;
                        bool isAux = gl < lines.Length && lines[gl].IsAuxLine;

                        if (isAux && wantLine)
                        {
                            // 補助線分。FaceIndex はグローバル面インデックス
                            // → ローカル面インデックスに変換
                            uint gfi = gl < lines.Length ? lines[gl].FaceIndex : uint.MaxValue;
                            int localFaceIdx = -1;
                            if (gfi != uint.MaxValue)
                                adapter.BufferManager.GlobalToLocalFaceIndex(
                                    (int)gfi, out _, out localFaceIdx);
                            return new PlayerHoverElement
                            {
                                Kind      = PlayerHoverKind.Line,
                                MeshIndex = ctxL,
                                FaceIndex = localFaceIdx,
                            };
                        }
                        else if (!isAux && wantEdge)
                        {
                            // 辺。V1/V2 をローカルインデックスに変換
                            var meshInfos = adapter.BufferManager.MeshInfos;
                            uint vStart = lm < meshInfos.Length
                                ? meshInfos[lm].VertexStart : 0u;
                            uint gv1 = lines[gl].V1;
                            uint gv2 = lines[gl].V2;
                            return new PlayerHoverElement
                            {
                                Kind      = PlayerHoverKind.Edge,
                                MeshIndex = ctxL,
                                EdgeV1    = (int)(gv1 - vStart),
                                EdgeV2    = (int)(gv2 - vStart),
                            };
                        }
                    }
                }
            }

            // ---- 面 ----
            if (mode.Has(Poly_Ling.Selection.MeshSelectMode.Face))
            {
                int gf = adapter.HoverFaceIndex;
                if (gf >= 0 && adapter.BufferManager?.GlobalToLocalFaceIndex(
                        gf, out int fm, out int fl) == true)
                {
                    int ctxF = UnifiedToContextIndex(adapter, model, fm);
                    if (ctxF >= 0)
                        return new PlayerHoverElement
                        {
                            Kind      = PlayerHoverKind.Face,
                            MeshIndex = ctxF,
                            FaceIndex = fl,
                        };
                }
            }

            return PlayerHoverElement.None;
        }

        /// <summary>
        /// 吸着用ヒットテスト（メッシュ選択を無視）の有効/無効を切り替える。
        /// 有効な間だけ追加のディスパッチと頂点数ぶんの読み戻しが走るため、
        /// 使うツールが有効な間だけ true にすること。
        /// </summary>
        public void SetSnapHitTestEnabled(bool enabled)
        {
            _snapHitTestEnabled = enabled;
            ApplySnapHitTestEnabled();
        }

        // 吸着用ヒットテストの要求状態。
        // RebuildAdapter は UnifiedSystemAdapter を Dispose して作り直し、
        // 新しい adapter の EnableSnapHitTest は既定 false で始まる。
        // 面追加は点を置くたびに EnterTopologyChanged（= RebuildAdapter）を通るため、
        // 要求状態をここで保持し、作り直しのたびに復元する。
        private bool _snapHitTestEnabled;

        /// <summary>
        /// 保持している吸着ヒットテストの要求状態を現在の adapter へ適用する。
        /// adapter を作り直す入口から必ず呼ぶこと。
        /// </summary>
        private void ApplySnapHitTestEnabled()
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null) return;
            adapter.EnableSnapHitTest = _snapHitTestEnabled;
        }

        /// <summary>
        /// 吸着用ヒットテストのホバー頂点を PlayerHoverElement として返す。
        ///
        /// GetHoverElement と違い、選択されていないメッシュの頂点も返る。
        /// 面追加ツールが他オブジェクトの頂点へ位置を合わせるために使う。
        /// SetSnapHitTestEnabled(true) を先に呼んでいないと常に未ヒットになる。
        /// </summary>
        public PlayerHoverElement GetSnapHoverElement(
            Poly_Ling.Context.ModelContext model)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized)
                return PlayerHoverElement.None;

            int gv = adapter.SnapHoverVertexIndex;
            if (gv < 0) return PlayerHoverElement.None;

            if (adapter.BufferManager?.GlobalToLocalVertexIndex(
                    gv, out int vm, out int vl) != true)
                return PlayerHoverElement.None;

            int ctxV = UnifiedToContextIndex(adapter, model, vm);
            if (ctxV < 0) return PlayerHoverElement.None;

            return new PlayerHoverElement
            {
                Kind        = PlayerHoverKind.Vertex,
                MeshIndex   = ctxV,
                VertexIndex = vl,
            };
        }

        // unified メッシュインデックス → context インデックス 変換ヘルパー
        private static int UnifiedToContextIndex(
            Poly_Ling.Core.UnifiedSystemAdapter adapter,
            Poly_Ling.Context.ModelContext model,
            int unifiedIdx)
        {
            if (model == null) return -1;
            for (int ci = 0; ci < model.Count; ci++)
                if (adapter.ContextToUnifiedMeshIndex(ci) == unifiedIdx)
                    return ci;
            return -1;
        }

        /// <summary>
        /// 通常面描画パイプライン (UnifiedBufferManager_Update.ComputeScreenPositions) と
        /// 同じ cam.pixelWidth / cam.pixelHeight (RenderTexture 解像度) 基準で投影する共通ヘルパー。
        /// cam.WorldToScreenPoint は Screen.width/Screen.height (メインディスプレイ解像度) を
        /// 使うため RenderTexture カメラでは panel サイズと不整合になる (ウィンドウ拡大時に
        /// overlay 座標がずれる)。これを回避するため cam.pixelWidth / cam.pixelHeight
        /// に対して直接射影する。
        /// 返す座標は cam.WorldToScreenPoint と同じ系 (Y=0 が下)。
        /// Painter2D 側 (OnGenerateFaceOverlay) は panelH - y の反転を行って UIToolkit 系 (Y=0 が上) に変換する。
        /// </summary>
        /// <returns>投影成功時はスクリーン座標、カメラ背面の場合は NaN を含む Vector2。</returns>
        private static Vector2 ProjectWorldToCameraScreen(Camera cam, Vector3 worldPos)
        {
            Matrix4x4 vpMat = cam.projectionMatrix * cam.worldToCameraMatrix;
            Vector4 clip = vpMat * new Vector4(worldPos.x, worldPos.y, worldPos.z, 1f);
            if (clip.w <= 0f) return new Vector2(float.NaN, float.NaN);
            float ndcX = clip.x / clip.w;
            float ndcY = clip.y / clip.w;
            float screenX = (ndcX * 0.5f + 0.5f) * cam.pixelWidth;
            // Unity の cam.WorldToScreenPoint と同じ系 (Y=0 が下)。
            // Painter2D 側で panelH - y により反転される前提。
            float screenY = (ndcY * 0.5f + 0.5f) * cam.pixelHeight;
            return new Vector2(screenX, screenY);
        }

        /// <summary>
        /// 指定メッシュの指定頂点について、GPU が計算したワールド座標を返す。
        ///
        /// 参照するのは GetDisplayPositions()（UnifiedBufferManager.cs:369-376）で、
        /// これは _worldPositions をそのまま返すだけで GetData を行わない。
        /// _worldPositions は DispatchTransformVertices の readbackToCPU 分岐
        /// （UnifiedBufferManager_Update.cs:1609-1616）が座標変化時に更新している。
        /// したがって本メソッドは GPU 同期を伴わない。
        ///
        /// 手順は GetHoverFaceScreenPts / GetSelectedFacesScreenPts と同一。
        /// スキニング規則を CPU 側で再実装してはならない。
        /// </summary>
        public bool TryGetVertexWorld(
            Poly_Ling.Context.ModelContext model,
            Poly_Ling.Data.MeshContext mc,
            int localVertexIndex,
            out UnityEngine.Vector3 world)
        {
            world = UnityEngine.Vector3.zero;

            if (model == null || mc?.MeshObject == null) return false;
            if (localVertexIndex < 0 || localVertexIndex >= mc.MeshObject.VertexCount) return false;

            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return false;

            int ctxIdx = model.MeshContextList.IndexOf(mc);
            if (ctxIdx < 0) return false;

            int unifiedIdx = adapter.ContextToUnifiedMeshIndex(ctxIdx);
            if (unifiedIdx < 0) return false;

            var bm = adapter.BufferManager;
            var meshInfos = bm?.MeshInfos;
            if (bm == null || meshInfos == null || unifiedIdx >= meshInfos.Length) return false;

            var worldPositions = bm.GetDisplayPositions();
            if (worldPositions == null) return false;

            int globalIdx = (int)meshInfos[unifiedIdx].VertexStart + localVertexIndex;
            if (globalIdx < 0 || globalIdx >= worldPositions.Length) return false;

            world = worldPositions[globalIdx];
            return true;
        }

        /// <summary>
        /// 指定メッシュの全頂点について、GPU が計算したワールド座標を配列で返す。
        ///
        /// 参照経路は TryGetVertexWorld と同一（GetDisplayPositions は _worldPositions を
        /// 返すだけで GetData を行わない）。1頂点ずつ呼ぶ代わりに一括で取り出すための版で、
        /// シュリンカーの衝突計算のように「そのときだけ全頂点が要る」用途に使う。
        /// スキニング規則を CPU 側で再実装してはならない。
        ///
        /// ワールド座標の鮮度が必要な場合は、呼び出し前に UpdateTransform() を1回だけ呼ぶこと。
        /// 毎フレーム呼んではならない。
        /// </summary>
        public bool TryGetMeshWorldPositions(
            Poly_Ling.Context.ModelContext model,
            Poly_Ling.Data.MeshContext mc,
            out UnityEngine.Vector3[] world)
        {
            world = null;

            if (model == null || mc?.MeshObject == null) return false;
            int vertexCount = mc.MeshObject.VertexCount;
            if (vertexCount <= 0) return false;

            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return false;

            int ctxIdx = model.MeshContextList.IndexOf(mc);
            if (ctxIdx < 0) return false;

            int unifiedIdx = adapter.ContextToUnifiedMeshIndex(ctxIdx);
            if (unifiedIdx < 0) return false;

            var bm = adapter.BufferManager;
            var meshInfos = bm?.MeshInfos;
            if (bm == null || meshInfos == null || unifiedIdx >= meshInfos.Length) return false;

            var worldPositions = bm.GetDisplayPositions();
            if (worldPositions == null) return false;

            int start = (int)meshInfos[unifiedIdx].VertexStart;
            if (start < 0 || start + vertexCount > worldPositions.Length) return false;

            world = new UnityEngine.Vector3[vertexCount];
            System.Array.Copy(worldPositions, start, world, 0, vertexCount);
            return true;
        }

        /// <summary>
        /// 指定頂点のクリップ空間 w を返す。
        ///
        /// 透視投影ではスクリーン上の線形パラメータと 3D 上の線形パラメータが一致せず、
        /// 変換には両端の w が要る。ワールド座標は TryGetVertexWorld と同じく
        /// GPU が計算した値を使い、投影のみカメラ行列で行う。
        /// 正投影では w が常に 1 になるため補正は恒等になる。
        /// </summary>
        public bool TryGetVertexClipW(
            Poly_Ling.Context.ModelContext model,
            Poly_Ling.Data.MeshContext mc,
            int localVertexIndex,
            PlayerViewport vp,
            out float clipW)
        {
            clipW = 0f;

            var cam = vp?.Cam;
            if (cam == null) return false;
            if (!TryGetVertexWorld(model, mc, localVertexIndex, out var world)) return false;

            Matrix4x4 vpMat = cam.projectionMatrix * cam.worldToCameraMatrix;
            Vector4 clip = vpMat * new Vector4(world.x, world.y, world.z, 1f);
            if (clip.w <= 0f) return false;

            clipW = clip.w;
            return true;
        }

        /// <summary>
        /// ホバー中の面のスクリーン座標を返す。
        /// 頂点位置は GPU DisplayPositions（GetDisplayPositions）を参照する。
        /// 座標投影は通常面描画パイプラインと同じ cam.pixelWidth/pixelHeight 基準で行う。
        /// </summary>
        public Vector2[] GetHoverFaceScreenPts(
            PlayerViewport vp, Poly_Ling.Context.ModelContext model)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return null;
            int gf = adapter.HoverFaceIndex;
            if (gf < 0) return null;
            if (adapter.BufferManager?.GlobalToLocalFaceIndex(gf, out int mi, out int lf) != true) return null;
            int ci = UnifiedToContextIndex(adapter, model, mi);
            if (ci < 0) return null;
            var mc = model.GetMeshContext(ci);
            if (mc?.MeshObject == null || lf < 0 || lf >= mc.MeshObject.FaceCount) return null;
            var face = mc.MeshObject.Faces[lf];
            if (face.VertexCount < 3) return null;
            var cam = vp?.Cam;
            if (cam == null) return null;

            // GPU 計算済みの最新ワールド座標を取得（頂点ドラッグ・ボーン変形反映済み）。
            UnityEngine.Vector3[] worldPositions = null;
            int vertexStart = 0;
            var bm = adapter.BufferManager;
            if (bm != null)
            {
                var meshInfos = bm.MeshInfos;
                if (meshInfos != null && mi < meshInfos.Length)
                {
                    vertexStart = (int)meshInfos[mi].VertexStart;
                    worldPositions = bm.GetDisplayPositions();
                }
            }
            int totalVertexCount = worldPositions?.Length ?? 0;

            var pts = new Vector2[face.VertexCount];
            for (int i = 0; i < face.VertexCount; i++)
            {
                int vi = face.VertexIndices[i];
                if (vi < 0 || vi >= mc.MeshObject.VertexCount) return null;

                UnityEngine.Vector3 worldPos;
                int globalIdx = vertexStart + vi;
                if (worldPositions != null && globalIdx < totalVertexCount)
                    worldPos = worldPositions[globalIdx];
                else
                    worldPos = mc.WorldMatrix.MultiplyPoint3x4(mc.MeshObject.Vertices[vi].Position);

                Vector2 sp = ProjectWorldToCameraScreen(cam, worldPos);
                if (float.IsNaN(sp.x)) return null;
                pts[i] = sp;
            }
            return pts;
        }

        /// <summary>選択面のスクリーン座標リストを返す。</summary>
        public List<Vector2[]> GetSelectedFacesScreenPts(
            PlayerViewport vp, Poly_Ling.Context.ModelContext model)
        {
            var cam = vp?.Cam;
            if (cam == null || model == null) return null;
            var mc = model.ActiveMeshContext;
            if (mc?.MeshObject == null) return null;
            var sel = mc.Selection;
            if (sel.Faces.Count == 0) return null;

            // GPU計算済みのワールド座標（ボーン変形込み）を取得
            var adapter = _renderer?.GetAdapter(0);
            UnityEngine.Vector3[] worldPositions = null;
            int vertexStart = 0;
            if (adapter != null && adapter.IsInitialized)
            {
                int ctxIdx = model.MeshContextList.IndexOf(mc);
                if (ctxIdx >= 0)
                {
                    int unifiedIdx = adapter.ContextToUnifiedMeshIndex(ctxIdx);
                    var bm = adapter.BufferManager;
                    if (unifiedIdx >= 0 && bm != null)
                    {
                        var meshInfos = bm.MeshInfos;
                        if (meshInfos != null && unifiedIdx < meshInfos.Length)
                        {
                            vertexStart = (int)meshInfos[unifiedIdx].VertexStart;
                            worldPositions = bm.GetDisplayPositions();
                        }
                    }
                }
            }

            var result = new List<Vector2[]>();
            var mo = mc.MeshObject;
            int totalVertexCount = worldPositions?.Length ?? 0;

            foreach (int fi in sel.Faces)
            {
                if (fi < 0 || fi >= mo.FaceCount) continue;
                var face = mo.Faces[fi];
                if (face.VertexCount < 3) continue;
                var pts = new Vector2[face.VertexCount];
                bool valid = true;
                for (int i = 0; i < face.VertexCount; i++)
                {
                    int vi = face.VertexIndices[i];
                    if (vi < 0 || vi >= mo.VertexCount) { valid = false; break; }

                    UnityEngine.Vector3 worldPos;
                    int globalIdx = vertexStart + vi;
                    if (worldPositions != null && globalIdx < totalVertexCount)
                        worldPos = worldPositions[globalIdx];
                    else
                        worldPos = mc.WorldMatrix.MultiplyPoint3x4(mo.Vertices[vi].Position);

                    // 通常面描画パイプライン (ComputeScreenPositions) と同一の cam.pixel 基準投影。
                    Vector2 sp = ProjectWorldToCameraScreen(cam, worldPos);
                    if (float.IsNaN(sp.x)) { valid = false; break; }
                    pts[i] = sp;
                }
                if (valid) result.Add(pts);
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// GPU ホバー結果から PlayerHitResult を生成する。
        /// マウスダウン時に PlayerVertexInteractor.GetHoverHit コールバックから呼ばれる。
        ///
        /// UpdateFrame（ポインター移動時）が GPU ヒットテストを完了済みの前提。
        /// CPU による最近傍探索は行わない。
        /// </summary>
        public PlayerHitResult GetHoverHit()
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized)
                return PlayerHitResult.Miss;

            // グローバルホバー頂点インデックス
            int globalVertex = adapter.HoverVertexIndex;
            if (globalVertex < 0)
                return PlayerHitResult.Miss;

            // グローバル → メッシュコンテキストインデックス + ローカル頂点インデックス
            if (adapter.BufferManager?.GlobalToLocalVertexIndex(
                    globalVertex, out int meshIdx, out int localIdx) == true)
            {
                return new PlayerHitResult
                {
                    HasHit      = true,
                    MeshIndex   = meshIdx,
                    VertexIndex = localIdx,
                };
            }
            return PlayerHitResult.Miss;
        }

        /// <summary>
        /// スカルプトブラシ用ヒットテスト。
        /// Normal モード（ドラッグなし）: UpdateFrame 算出済みの HoverVertexIndex を再利用（二重計算なし）。
        /// TransformDragging 中: _screenPositions から直接検索してブラシ中心をマウスに追従させる。
        ///
        /// 【_screenPositions の更新元について（2026-08-28 訂正）】
        ///   旧コメントは「DrawViewport が毎イベント更新済み」と書いていたが、
        ///   DrawViewport は呼出元 0 件の死んだ経路で、撤去した。
        ///   実際の更新元は PresentAll 末尾のアクティブ slot 最終確定
        ///   （DispatchCullingForDisplay(readback: true)）ただ 1 か所である。
        ///   EnterVerticesMoved の Dragging フェーズで syncMc != null の軽量経路を
        ///   通る間は PresentAll を呼ばないため、_screenPositions はドラッグ開始時の
        ///   値のまま据え置かれる。これは本変更で生じたものではなく従来からの挙動。
        /// </summary>
        public PlayerHitResult GetBrushHit(Vector2 screenPos, float hitRadius)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized)
                return PlayerHitResult.Miss;

            int globalVertex;

            // HoverVertexIndex が有効なら UpdateFrame の結果を再利用（二重計算なし）
            int hoverIdx = adapter.HoverVertexIndex;
            if (hoverIdx >= 0)
            {
                globalVertex = hoverIdx;
            }
            else
            {
                // TransformDragging 中: _screenPositions は DrawViewport で更新済み
                var bm = adapter.BufferManager;
                if (bm == null) return PlayerHitResult.Miss;
                globalVertex = bm.FindNearestVertex(screenPos, hitRadius, adapter.BackfaceCullingEnabled);
                if (globalVertex < 0) return PlayerHitResult.Miss;
            }

            if (adapter.BufferManager?.GlobalToLocalVertexIndex(
                    globalVertex, out int meshIdx, out int localIdx) == true)
            {
                return new PlayerHitResult
                {
                    HasHit      = true,
                    MeshIndex   = meshIdx,
                    VertexIndex = localIdx,
                };
            }
            return PlayerHitResult.Miss;
        }
    }
}
