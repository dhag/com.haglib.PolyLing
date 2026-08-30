// MeshSceneRenderer.cs
// ProjectContextのメッシュ・ボーン・ワイヤーフレームをシーンに描画するクラス。
// Runtime/Editor両方から使用可能なplain C#クラス（IDisposable）。
// Graphics.DrawMesh ベース（MonoBehaviour不要）。
// エディタ側ViewportCoreも将来このクラスに委譲する。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;
using Poly_Ling.Core;
using Poly_Ling.Core.Rendering;

namespace Poly_Ling.Core
{
    /// <summary>
    /// ProjectContextのメッシュ・ワイヤーフレーム・ボーンをGraphics.DrawMeshで描画する。
    /// LateUpdate相当のタイミングで Draw*() を呼ぶこと。
    /// </summary>
    public class MeshSceneRenderer : IDisposable
    {
        // ================================================================
        // 描画フラグ
        // ================================================================

        public bool ShowSelectedMesh          { get; set; } = true;
        public bool ShowUnselectedMesh        { get; set; } = true;
        public bool ShowSelectedVertices      { get; set; } = true;
        public bool ShowUnselectedVertices    { get; set; } = true;
        public bool ShowSelectedWireframe     { get; set; } = true;
        public bool ShowUnselectedWireframe   { get; set; } = true;
        public bool ShowSelectedBone          { get; set; } = true;
        public bool ShowUnselectedBone        { get; set; } = false;
        public bool ShowSelectedMirror        { get; set; } = true;
        // 非選択ミラーの「面」。マスタ（ViewportDisplaySettings.ShowUnselectedMirror）は
        // WithMirrorClamped が既に織り込むため、レンダラはこの子だけを見る。
        // マスタ相当のプロパティはここに持たない（どちらを見るか曖昧になるため）。
        public bool ShowUnselectedMirrorMesh  { get; set; } = true;
        // メッシュ原点マーカー（MeshType.Bone 以外のピック対象に対して、
        // ボーンと同じ形状のラインメッシュを原点へ描く）。
        public bool ShowSelectedMeshOrigin    { get; set; } = true;
        public bool ShowUnselectedMeshOrigin  { get; set; } = true;
        // 法線表示（頂点スロット単位）。選択メッシュのみ対象。
        // 1 スロット 1 本の線分を頂点位置から法線方向へ描く。
        // TransformDragging 中は SetNormalsSuppressed / PrepareNormals が抑止する。
        public bool ShowNormals               { get; set; } = false;
        // ミラー側（MirrorSide / BakedMirror）の原点マーカー。
        // ミラーは実体側と同じ原点に重なって出るため、既定では描かない。
        // ObjectMoveSettings.PickMirrorSides とセットで運用すること。
        public bool ShowMirrorMeshOrigin      { get; set; } = false;
        public bool BackfaceCullingEnabled    { get; set; } = true;

        // ================================================================
        // 内部状態
        // ================================================================

        // 外部から注入する SelectionState（Player用）
        private SelectionState _selectionState;

        // DrawWireframeAndVertices で PrepareDrawing に渡す selectedMeshIndex（モデルごと）。
        // model.FirstMeshIndex を adapter のコンテキストインデックスに変換した値。
        // RebuildAdapter / UpdateSelectedDrawableMesh で更新する。
        private readonly List<int>                  _selectedMeshIndexForDraw = new List<int>();

        private readonly List<UnifiedSystemAdapter> _adapters       = new List<UnifiedSystemAdapter>();
        private readonly Dictionary<(int, int), Mesh> _boneMeshCache= new Dictionary<(int, int), Mesh>();

        // 法線表示用のラインメッシュ（モデル単位で 1 本にまとめる）。
        // キーはモデルインデックス。選択メッシュが変わっても丸ごと作り直すため、
        // メッシュ単位でキャッシュを持つ場合のような取り残しが起きない。
        private readonly Dictionary<int, Mesh> _normalMeshCache = new Dictionary<int, Mesh>();

        // TransformDragging 中は true。SubmitNormals はこのフラグだけを見て提出を止める
        // （Submit 側で計算・判定を行わないため）。
        private bool _normalsSuppressed;

        // =====================================================================
        // 【重要】法線メッシュ構築用キャッシュリスト - 毎回 new しないこと！
        // UnifiedRenderer の _cachedVertices 等と同じ理由。Clear() して再利用する。
        // =====================================================================
        private readonly List<Vector3> _normalVerts   = new List<Vector3>();
        private readonly List<Color>   _normalColors  = new List<Color>();
        private readonly List<int>     _normalIndices = new List<int>();

        private Material _defaultMaterial;
        private Material _boneMaterial;

        // 診断用: 材質 null ログを1回だけ出すためのフラグ
        private static bool _matDbgLogged = false;
        // Phase 2c-2: 選択/非選択で alpha を変えるため 2 種類保持する。
        // シェーダは Poly_Ling/Bone3D_Overlay (ZTest Always)。
        private Material _boneOverlayMaterialSelected;
        private Material _boneOverlayMaterialUnselected;
        private bool     _disposed;

        // ================================================================
        // ボーン形状定数
        // ================================================================

        // くさび形状（ボーン表示・メッシュオブジェクト原点表示で共用）
        // ----------------------------------------------------------------
        // ■ 軸の規約
        //     ローカル Y … 最も長い。根(0) → 先端(+Y)。長手方向
        //     ローカル X … 次に長い。環の左右幅
        //     ローカル Z … 最も短い。前(+Z)を後(-Z)より広くして非対称にする
        //
        //   Z を非対称にする理由: 前後が同じ幅だと Y 軸まわり 180 度の回転に対して
        //   形が自己対称になり、ロールの向きが見分けられない。前を広くすることで
        //   「長手 = 根→先端」「前 = 環の中で最も張り出した側」の2本で姿勢が読める。
        //
        // ■ 頂点スロット（BoneShapeEdges がこの並びを前提にする）
        //     0 = 環 -Z（後・狭い）   1 = 先端 +Y   2 = 環 +X
        //     3 = 環 +Z（前・広い）   4 = 環 -X     5 = 根
        //   環の巡回順は 0 → 2 → 3 → 4 → 0。対辺は (0,3) と (2,4)。
        private static readonly Vector3[] BoneShapeVertices =
        {
            new Vector3( 0f,    0.5f, -0.15f),
            new Vector3( 0f,    2.5f,  0f),
            new Vector3( 0.4f,  0.5f,  0f),
            new Vector3( 0f,    0.5f,  0.25f),
            new Vector3(-0.4f,  0.5f,  0f),
            new Vector3( 0f,    0f,    0f),
        };
        private static readonly int[,] BoneShapeEdges =
        {
            {0,1},{0,2},{0,4},{0,5},
            {1,2},{1,3},{1,4},
            {2,3},{2,5},
            {3,4},{3,5},
            {4,5},
        };
        /// <summary>
        /// ボーン／メッシュ原点マーカー（くさび）の大きさ（Unity 単位）。
        ///
        /// 【const をやめてプロパティにした理由】 2026-08-28
        ///   以前は private const 0.04f で、設定から変える手段が無かった。
        ///   4 面共通の表示パラメータとして ViewportGridSettings.BoneMarkerScale
        ///   に持たせ、PlayerViewportManager.PrepareViewport がここへ転記する。
        ///
        /// 【既定値を 0.04f → 0.01f にした理由】
        ///   従来の 1/4。マーカーが大きすぎてメッシュを隠していたため。
        ///   値の正本は ViewportGridSettings.Default.BoneMarkerScale で、
        ///   ここの初期値は転記前に描いてしまった場合の保険。両方そろえること。
        ///
        /// 【カメラ距離に依存しない】
        ///   ワールド単位固定なので、モデルのスケールが極端だと見えにくくなる。
        ///   その場合は設定（軸 / グリッドパネル）で調整する。
        /// </summary>
        public float BoneMarkerScale { get; set; } = 0.01f;
        private static readonly Color BoneWireColor     = new Color(0.2f, 0.8f, 1.0f, 0.8f);
        private static readonly Color BoneWireSelColor  = new Color(1.0f, 0.6f, 0.1f, 0.9f);
        // メッシュ原点マーカー色（ボーン色と区別するため緑系）。
        private static readonly Color MeshOriginColor    = new Color(0.4f, 1.0f, 0.4f, 0.8f);
        private static readonly Color MeshOriginSelColor = new Color(1.0f, 1.0f, 0.3f, 0.9f);

        // 法線線分の色（灰青系）。根元を暗く、先端を明るくして向きが読めるようにする。
        // Poly_Ling/Bone3D_Overlay は頂点色をそのまま出力するため、線の 2 頂点に
        // 別の色を入れるだけでグラデーションになり、追加コストは無い。
        // 既存の 3D 線色（エッジ緑・選択エッジ橙・補助線マゼンタ・ボーン水色・原点緑）
        // と色相が衝突しない灰青を選ぶ。
        private static readonly Color NormalRootColor = new Color(0.35f, 0.42f, 0.52f, 0.85f);
        private static readonly Color NormalTipColor  = new Color(0.62f, 0.74f, 0.88f, 0.95f);

        // ================================================================
        // Adapter構築
        // ================================================================

        /// <summary>
        /// 現在レンダラが保持している SelectionState（読み取り専用参照）。
        ///
        /// GPU ホバーの種別絞り込み（UnifiedMeshSystem.ProcessMouseUpdate）は
        /// この参照の Mode を読む。Player 側の選択モード権限が「実際に GPU が見ている
        /// SelectionState」へ確実に書き込めるように公開する。
        /// 差し替えは行わないため、SetSelectionState の規約（正規入口経由）には影響しない。
        /// </summary>
        public SelectionState CurrentSelectionState => _selectionState;

        /// <summary>
        /// 選択状態を設定する。RebuildAdapter より前に呼ぶこと。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (PlayerViewportManager 上の " +
            "EnterProjectChanged / EnterTopologyChanged / EnterCameraChanged / " +
            "EnterVerticesMoved / EnterHoverChanged / EnterDisplaySettingsChanged) " +
            "経由で呼ぶこと。本 API を Player 配下の Core / Dispatcher / RemoteFlow から " +
            "直接呼ぶことは禁止。",
            error: false)]
        public void SetSelectionState(SelectionState selectionState)
        {
            _selectionState = selectionState;
            // 既存アダプターにも反映。
            // SetSelectionState の後に UpdateAllSelectionFlags を呼ばないと
            // GPU の MeshSelected ビットが古いままになりワイヤー・頂点が描画されない。
            foreach (var adapter in _adapters)
            {
                if (adapter == null) continue;
                adapter.SetSelectionState(_selectionState ?? new SelectionState());
                adapter.BufferManager?.UpdateAllSelectionFlags();
                adapter.RequestNormal();
            }
        }

        /// <summary>
        /// 指定モデルインデックスの UnifiedSystemAdapter を取得する。
        ///
        /// 【用途】
        ///   PlayerViewportManager がホバー更新・カメラ更新・矩形選択の
        ///   ReadBackVertexFlags を呼ぶために使う。
        ///   アダプターは RebuildAdapter() 後にのみ存在する。
        ///   存在しない場合（未ロード・ClearScene後）は null を返す。
        /// </summary>
        public UnifiedSystemAdapter GetAdapter(int mi)
        {
            if (mi < 0 || mi >= _adapters.Count) return null;
            return _adapters[mi];
        }

        /// <summary>
        /// 全アダプター数（ビューポートマネージャーがループ走査する際に使う）。
        /// </summary>
        public int AdapterCount => _adapters.Count;

        /// <summary>
        /// 選択描画メッシュが変わったときに呼ぶ。
        /// _selectedMeshIndexForDraw を更新し、PrepareDrawing に正しい index が渡るようにする。
        /// Viewer が model.SelectedDrawableMeshIndices を変更した後に呼ぶこと。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (PlayerViewportManager 上の " +
            "EnterProjectChanged / EnterTopologyChanged / EnterCameraChanged / " +
            "EnterVerticesMoved / EnterHoverChanged / EnterDisplaySettingsChanged) " +
            "経由で呼ぶこと。本 API を Player 配下の Core / Dispatcher / RemoteFlow から " +
            "直接呼ぶことは禁止。",
            error: false)]
        public void UpdateSelectedDrawableMesh(int mi, ModelContext model)
        {
            while (_selectedMeshIndexForDraw.Count <= mi)
                _selectedMeshIndexForDraw.Add(-1);

            // PrepareDrawing の selectedMeshIndex は adapter の unifiedMeshIndex を期待する。
            // SelectedDrawableMeshIndices[0]（MeshContextList インデックス）を変換する。
            // アダプタが無ければ何も出来ない。
            if (mi >= _adapters.Count || _adapters[mi] == null)
            {
                _selectedMeshIndexForDraw[mi] = -1;
                return;
            }

            // 選択が無い（ctxIdx < 0）場合も -1 として下へ流す。
            // 早期 return すると GPU 側の選択フラグが前のまま残る。
            int ctxIdx = model.FirstMeshIndex;
            int unifiedIdx = (ctxIdx >= 0)
                ? (_adapters[mi].BufferManager?.ContextToUnifiedMeshIndex(ctxIdx) ?? -1)
                : -1;
            _selectedMeshIndexForDraw[mi] = unifiedIdx;

            // ActiveMeshIndex と選択フラグを即時更新する。
            // RebuildAdapter は SelectMesh より先に呼ばれるため、ここで再設定が必要。
            //
            // 【unifiedIdx < 0 でも走らせる理由】
            //   不可視・頂点0のメッシュは GPU バッファに載らないので -1 になる
            //   （UnifiedBufferManager_Build.ShouldIncludeInBuffers）。
            //   従来は -1 のとき3つとも飛ばしていたため、前の選択が GPU に残り、
            //   リスト上の選択と画面の選択が食い違っていた。
            //   -1 は「選択なし」として SetActiveMesh へ渡す。FlagManager は
            //   -1 を該当なしとして扱うため、どのメッシュにも選択フラグが立たない。
            var bm = _adapters[mi].BufferManager;
            if (bm != null)
            {
                bm.SyncSelectionFromModel(model);
                bm.SetActiveMesh(0, unifiedIdx);
                bm.UpdateAllSelectionFlags();
            }
        }

        /// <summary>選択変更をGPUバッファに通知する。</summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (PlayerViewportManager 上の " +
            "EnterProjectChanged / EnterTopologyChanged / EnterCameraChanged / " +
            "EnterVerticesMoved / EnterHoverChanged / EnterDisplaySettingsChanged) " +
            "経由で呼ぶこと。本 API を Player 配下の Core / Dispatcher / RemoteFlow から " +
            "直接呼ぶことは禁止。",
            error: false)]
        public void NotifySelectionChanged()
        {
            foreach (var adapter in _adapters)
                adapter?.NotifySelectionChanged();
        }

        /// <summary>
        /// モデルのメッシュ受信完了後にAdapterを再構築する。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (PlayerViewportManager 上の " +
            "EnterProjectChanged / EnterTopologyChanged / EnterCameraChanged / " +
            "EnterVerticesMoved / EnterHoverChanged / EnterDisplaySettingsChanged) " +
            "経由で呼ぶこと。",
            error: false)]
        /// <summary>
        /// ボーン／法線ラインメッシュのキャッシュを破棄する。
        ///
        /// _boneMeshCache は (モデル番号, MeshContext 番号) をキーに Mesh を保持する。
        /// Mesh はネイティブオブジェクトで、Dictionary を Clear するだけでは解放されない。
        /// モデルを読み直すと MeshContext の並びが変わり、旧エントリは二度と参照されない
        /// まま常駐する。プロジェクトを開き直すたびにボーン本数ぶん積み上がるため、
        /// アダプタ再構築のたびにここで破棄する。
        /// </summary>
        public void ClearMeshCaches()
        {
            foreach (var mesh in _boneMeshCache.Values)
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            _boneMeshCache.Clear();

            foreach (var mesh in _normalMeshCache.Values)
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            _normalMeshCache.Clear();
        }

        public void RebuildAdapter(int mi, ModelContext model)
        {
            Poly_Ling.Diagnostics.PLResStat.Report("RebuildAdapter.enter mi=" + mi);

            // 書き戻しが積んだ旧 Mesh をここで解放する。
            // 再構築の入口なので前フレームの描画は完了している。
            Poly_Ling.Data.MeshContext.FlushRetiredMeshes();

            // MeshContext の並びが変わるため、番号をキーにした Mesh キャッシュは
            // ここで必ず捨てる（放置すると読み直しのたびにリークする）。
            ClearMeshCaches();

            while (_adapters.Count <= mi) _adapters.Add(null);
            _adapters[mi]?.Dispose();
            _adapters[mi] = null;

            bool hasAny = false;
            foreach (var mc in model.MeshContextList)
                if (mc?.MeshObject != null && mc.MeshObject.VertexCount > 0) { hasAny = true; break; }
            if (!hasAny) return;

            // [CamDbg] adapter=1 のとき UnifiedSystemAdapter を作らない。診断専用。
            Poly_Ling.Diagnostics.PLCamDbg.EnsureSwitches();
            if (Poly_Ling.Diagnostics.PLCamDbg.SwNoAdapter)
            {
                if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("SW noAdapter skip mi=" + mi);
                return;
            }

            var adapter = new UnifiedSystemAdapter();
            if (!adapter.Initialize())
            {
                Debug.LogWarning($"[MeshSceneRenderer] Adapter初期化失敗 [{mi}]");
                adapter.Dispose();
                return;
            }

            // Initialize 済み adapter は GPU ComputeBuffer を確保済み。以降の構築で例外が出ると
            // adapter が Dispose されずリークするため、try/catch で確保済みバッファを必ず解放する。
            // 【残存リスク】ここでは GPU バッファのリーク防止のみ。例外の根本原因（データ不整合等）は
            //   未解決で、当該モデルは表示欠落として現れる。他の構築/アップロード経路も同様に
            //   個別ガードはしていない（呼び出し側での不正データ抑止が本筋。次段の課題）。
            try
            {
            adapter.SetSelectionState(_selectionState ?? new SelectionState());
            adapter.SetSymmetrySettings(new SymmetrySettings());
            adapter.SetModelContext(model);

            // SetActiveMesh 用に先頭 Drawable のコンテキストインデックスを求める。
            // 選択状態の初期設定（SelectDrawableMesh / SelectBone）は
            // Viewer（PolyLingPlayerViewer）がフェッチ完了後に行う。
            // ここではレンダラー内部の GPU バッファ初期化のみ行う。
            int firstCtxIdx = model.FirstMeshIndex;
            // 選択中メッシュが不可視・空だとバッファに載っていない。
            // その場合 ContextToUnifiedMeshIndex は -1 を返すので、
            // 下のフォールバックへ落として載っているメッシュを探し直す。
            if (firstCtxIdx >= 0 &&
                (adapter.BufferManager?.ContextToUnifiedMeshIndex(firstCtxIdx) ?? -1) < 0)
            {
                firstCtxIdx = -1;
            }
            if (firstCtxIdx < 0)
            {
                // SelectedDrawableMeshIndices が未設定の場合は
                // DrawableMeshes から頂点数 > 0 の先頭を探す（フォールバック）
                var drawables = model.DrawableMeshes;
                if (drawables != null)
                    foreach (var entry in drawables)
                    {
                        var ctx = entry.Context;
                        if (ctx?.MeshObject != null && ctx.MeshObject.VertexCount > 0 && ctx.IsVisible)
                        { firstCtxIdx = entry.MasterIndex; break; }
                    }
            }

            // 見つからないときは SetActiveMesh を呼ばない。
            // -1 をそのまま渡すと、アクティブメッシュ番号として不正な値が
            // GPU 内部の描画フラグの計算へ流れ込む。
            int firstUnified = (firstCtxIdx >= 0)
                ? (adapter.BufferManager?.ContextToUnifiedMeshIndex(firstCtxIdx) ?? -1) : -1;
            if (firstUnified >= 0)
                adapter.BufferManager?.SetActiveMesh(0, firstUnified);
            adapter.BufferManager?.UpdateAllSelectionFlags();

            // WorldMatrix を使って初期表示位置を確定する。
            // MeshFilter は WorldMatrix を直接使用、スキンドは SkinningMatrix を使用。
            // （ComputeMeshFilterBindPoses は呼ばない: BindPose=WorldMatrix.inverse にすると
            //   SkinningMatrix=identity になり全メッシュがローカル原点に表示されてしまうため）
            // [CamDbg] xform=1 のとき GPU 変換と書き戻しを止める。診断専用。
            if (!Poly_Ling.Diagnostics.PLCamDbg.SwNoXform)
            {
                adapter.UpdateTransform(useWorldTransform: true);
                adapter.WritebackTransformedVertices();
            }

            _adapters[mi] = adapter;

            adapter.RequestNormal();
            }
            catch (System.Exception ex)
            {
                // 構築中の例外：確保済み GPU バッファを解放してリークを防ぐ。
                Debug.LogError($"[MeshSceneRenderer] RebuildAdapter 例外 [{mi}]: {ex.Message}");
                if (_adapters[mi] == adapter) _adapters[mi] = null;
                adapter.Dispose();
            }
        }

        // ================================================================
        // 描画（Phase 1: Prepare / Submit 分離）
        //
        // ・Prepare*: event 駆動で呼ぶ。計算・CPU Mesh 再構築・ComputeBuffer 更新・
        //            Dispatch 等を含む。毎フレーム呼ぶのは禁止。
        // ・Submit*:  OnRenderObject() から毎フレーム呼ぶ。Graphics.DrawMesh 提出のみ。
        //            計算処理は一切禁止（厳守）。
        // ================================================================

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 計算処理（バッファ更新、フラグ計算、マテリアル設定等）は一切禁止。
        /// 全ての準備は事前に event 駆動で済ませておくこと。
        /// 面本体の Graphics.DrawMesh 提出のみを担当する。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        /// <summary>[EmptyMeshDbg] 空 UnityMesh の記録残回数。診断専用。</summary>
        private int _emptyMeshDbgLeft = 60;

        public void SubmitMeshes(ProjectContext project, Camera cam)
        {
            if (project == null || cam == null) return;

            var model = project.CurrentModel;
            if (model == null) return;

            var drawables = model.DrawableMeshes;
            if (drawables == null) return;

            var selDrawable = model.SelectedDrawableMeshIndices;

            // ウェイト可視化中のメッシュはベース面を描かない。
            // 同じ UnityMesh を identity で二重に描くことになり、材質の renderQueue
            // （MaterialDataConverter: 不透明 2000 / cutout 2450 / 半透明 3000）が
            // 可視化シェーダ (Geometry+1 = 2001) より後になると、ベース面が
            // ヒートマップ色を上から塗り潰して可視化が一切見えなくなる。
            // 対象の決め方は CollectWeightVisTargets に一本化してある。
            var weightVisTargets = CollectWeightVisTargets(model);

            int __draws = 0, __nullMesh = 0, __nullMat = 0, __badMesh = 0, __vtxSum = 0;

            for (int i = 0; i < drawables.Count; i++)
            {
                var ctx = drawables[i].Context;
                // 頂点 0 の UnityMesh は null ではないため、vertexCount も見ないと
                // 空メッシュを Graphics.DrawMesh へ渡してしまう。
                if (ctx?.UnityMesh == null || ctx.UnityMesh.vertexCount <= 0 || !ctx.IsVisible)
                {
                    // [EmptyMeshDbg] 空 UnityMesh の発生源を特定するための一時記録。
                    // 先頭 60 件のみ。恒久コードではない。
                    if (_emptyMeshDbgLeft > 0 && ctx != null && ctx.UnityMesh != null && ctx.UnityMesh.vertexCount <= 0)
                    {
                        _emptyMeshDbgLeft--;
                        var __mo = ctx.MeshObject;
                        if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("EmptyMesh master=" + drawables[i].MasterIndex
                            + " name=\"" + ctx.Name + "\""
                            + " type=" + ctx.Type
                            + " vis=" + ctx.IsVisible
                            + " uSub=" + ctx.UnityMesh.subMeshCount
                            + " moVtx=" + (__mo == null ? -1 : __mo.VertexCount)
                            + " moFace=" + (__mo == null ? -1 : __mo.FaceCount));
                    }
                    continue;
                }

                if (weightVisTargets != null && weightVisTargets.Contains(drawables[i].MasterIndex))
                    continue;

                bool isSel = selDrawable.Contains(drawables[i].MasterIndex);
                bool isMirror = ctx.Type == MeshType.BakedMirror || ctx.Type == MeshType.MirrorSide;
                if (isMirror)
                {
                    if ( isSel && !ShowSelectedMirror)       continue;
                    if (!isSel && !ShowUnselectedMirrorMesh) continue;
                }
                else
                {
                    if ( isSel && !ShowSelectedMesh)   continue;
                    if (!isSel && !ShowUnselectedMesh) continue;
                }

                var mesh = ctx.UnityMesh;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material mat = (sub < model.MaterialCount) ? model.GetMaterial(sub) : null;
                    if (mat == null)
                    {
                        // 診断: 描画時に材質が null になる原因を1回だけ記録。
                        if (!_matDbgLogged)
                        {
                            _matDbgLogged = true;
                            Debug.Log($"[MatDbg] MaterialCount={model.MaterialCount} subMeshCount={mesh.subMeshCount} sub={sub} mesh=\"{ctx.Name}\"");
                        }
                        mat = GetDefaultMaterial();
                    }
                    if (mat == null) continue;

                    if (mat.HasProperty("_Cull"))
                    {
                        var matRef = (sub < model.MaterialCount) ? model.GetMaterialReference(sub) : null;
                        bool isMaterialDoubleSide = matRef != null
                            && matRef.Data.CullMode == Poly_Ling.Materials.CullModeType.Off;
                        float cullValue = (!BackfaceCullingEnabled || isMaterialDoubleSide) ? 0f : 2f;
                        mat.SetFloat("_Cull", cullValue);
                    }

                    if (mesh == null) { __nullMesh++; continue; }
                    if (mat == null)  { __nullMat++;  continue; }
                    if (mesh.vertexCount <= 0 || sub >= mesh.subMeshCount) { __badMesh++; continue; }
                    if (sub == 0) __vtxSum += mesh.vertexCount;
                    __draws++;
                    Poly_Ling.Core.PLMeshValidator.Check(mesh, mat, "SubM");
                    Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, cam, sub);
                }
            }

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("SubM draws=" + __draws
                + " nullMesh=" + __nullMesh + " nullMat=" + __nullMat
                + " badMesh=" + __badMesh + " vtx=" + __vtxSum);
        }

        /// <summary>
        /// 【旧 API】面本体を描画する。Phase 1 暫定として残置。
        /// 内部は SubmitMeshes へ委譲。新規コードは SubmitMeshes を直接呼ぶこと。
        /// </summary>
        public void DrawMeshes(ProjectContext project, Camera cam) => SubmitMeshes(project, cam);

        /// <summary>
        /// 【event 駆動で呼ぶ】指定 slot の辺・頂点描画に必要な計算を行う。
        /// CPU Mesh 再構築 / ComputeBuffer 更新 / Dispatch / Queue 登録を含む重い処理。
        /// Phase 1: カメラ操作・選択変更・トポロジ変更イベント等から呼び出される想定。
        /// Submit と分離されているため、毎フレーム呼ぶのは禁止。
        /// <param name="project">
        /// ProjectContext を渡すと選択状態（VertexSelected 等）を GPU に正しく反映する。
        /// null の場合は選択フラグ更新をスキップする。
        /// </param>
        /// </summary>
        public void PrepareWireframeAndVertices(Camera cam, ProjectContext project = null, int cullingSlot = 0)
        {
            if (cam == null) return;
            float pointSize = ShaderColorSettings.Default.VertexPointScale;

            for (int mi = 0; mi < _adapters.Count; mi++)
            {
                var adapter = _adapters[mi];
                if (adapter == null || !adapter.IsInitialized) continue;

                adapter.CleanupQueued(cullingSlot);
                adapter.BackfaceCullingEnabled = BackfaceCullingEnabled;

                var profile = adapter.CurrentProfile;

                int selIdx = (mi < _selectedMeshIndexForDraw.Count)
                    ? _selectedMeshIndexForDraw[mi] : -1;

                // ---- AllowSelectedDrawableMeshSync ----
                if (profile.AllowSelectedDrawableMeshSync && project != null && mi < project.ModelCount)
                {
                    var bufMgr = adapter.BufferManager;
                    if (bufMgr != null)
                    {
                        var model = project.Models[mi];
                        bufMgr.SyncSelectionFromModel(model);
                        if (selIdx >= 0) bufMgr.SetActiveMesh(0, selIdx);
                        bufMgr.UpdateAllSelectionFlags();
                    }
                }

                // ---- カリングはここで発行しない（旧 cullSubmit ブロックを撤去） 2026-08-28 ----
                //
                // 【撤去した理由 1: 二重計算】
                //   PlayerViewportManager.PrepareViewport は、本メソッドを呼ぶ直前に
                //   同じ slot・同じカメラで adapter.DispatchCullingForDisplay(slot) を
                //   呼んでいる。そこで
                //     ClearCulledBuffers → ComputeScreenPositions → FaceVisibility
                //     → LineVisibility → ApplyMirrorCull
                //   を済ませているのに、ここで丸ごと同じ手順をもう一度回していた。
                //
                // 【撤去した理由 2: 背面カリング OFF を握り潰していた】
                //   DispatchCullingForDisplay は backfaceCulling == false のとき
                //   ClearCulledFlagsGPU(slot) で全要素を可視(0)にする。ところが
                //   ここは BackfaceCullingEnabled を見ずに必ず
                //   ClearCulledBuffers(全カリング済み=1) → FaceVisibility を実行するため、
                //   直前の「全可視」を必ず上書きし、設定が OFF でもカリングが
                //   適用された状態で終わっていた。
                //   本ブロックは profile.AllowGpuVisibility（Normal モード）でのみ走り、
                //   末尾の ConsumeNormalMode が Idle へ落とすため、
                //   1 回の PresentAll で最初に処理される slot（Perspective）だけが
                //   この影響を受けていた。
                //
                // 【DispatchClearBuffersGPU を失って困らない理由】
                //   同メソッドがクリアするのは _screenPosBuffer4 と各ヒット距離バッファ。
                //   ヒットテストの直前に UnifiedMeshSystem.ProcessMouseUpdate が
                //   毎回クリアし直すので、読み手を失うものは無い。
                //
                // 【SetMirrorDisplay を失って困らない理由】
                //   PrepareViewport が DispatchCullingForDisplay より前に
                //   slot ごとの値で呼んでいる。ここのは同じ値の二度目だった。
                //
                // 【カリングを再計算する条件】
                //   PrepareViewport の dirty 判定が唯一の発行条件になる。
                //   カリング入力（スクリーン座標 / FLAG_HIDDEN / FLAG_MESH_SELECTED /
                //   ミラー表示設定）を変えたら、必ず該当 slot を dirty にすること。

                adapter.PrepareDrawing(
                    cam,
                    showWireframe:           ShowSelectedWireframe,
                    showVertices:            ShowSelectedVertices,
                    showUnselectedWireframe: ShowUnselectedWireframe && profile.AllowUnselectedOverlay,
                    showUnselectedVertices:  ShowUnselectedVertices && profile.AllowUnselectedOverlay,
                    selectedMeshIndex:       selIdx,
                    pointSize:               pointSize,
                    cullingSlot:             cullingSlot);
                adapter.ConsumeNormalMode();
            }
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 計算処理は一切禁止。全ての準備は PrepareWireframeAndVertices で完了させておくこと。
        /// OnRenderObject() から毎フレーム呼ばれる想定。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void SubmitWireframeAndVertices(Camera cam, int cullingSlot = 0)
        {
            if (cam == null) return;
            for (int mi = 0; mi < _adapters.Count; mi++)
            {
                var adapter = _adapters[mi];
                if (adapter == null || !adapter.IsInitialized) continue;
                adapter.DrawQueued(cam, cullingSlot);
            }
        }

        /// <summary>
        /// 【旧 API】辺・頂点を描画する。Phase 1 暫定として残置。
        /// 内部は Prepare + Submit を連続呼びする。新規コードは分離して呼ぶこと。
        /// </summary>
        public void DrawWireframeAndVertices(Camera cam, ProjectContext project = null, int cullingSlot = 0)
        {
            PrepareWireframeAndVertices(cam, project, cullingSlot);
            SubmitWireframeAndVertices(cam, cullingSlot);
        }

        /// <summary>
        /// 【event 駆動で呼ぶ】ボーン描画用のラインメッシュを事前構築・更新する。
        /// 各ボーンの pos/rot/col を抽出し、_boneMeshCache を再構築。
        /// Phase 1: ボーンポーズ変更・ボーン選択変更・モデルロード時に呼び出す想定。
        /// Submit と分離されているため、毎フレーム呼ぶのは禁止。
        /// </summary>
        public void PrepareBones(ProjectContext project)
        {
            if (project == null) return;

            bool anyBone   = ShowSelectedBone       || ShowUnselectedBone;
            bool anyOrigin = ShowSelectedMeshOrigin || ShowUnselectedMeshOrigin;
            if (!anyBone && !anyOrigin) return;

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                var selBones  = model.SelectedBoneIndices;
                var selMeshes = model.SelectedDrawableMeshIndices;

                for (int ci = 0; ci < model.MeshContextCount; ci++)
                {
                    var ctx = model.GetMeshContext(ci);
                    if (ctx == null) continue;

                    Color col;
                    if (ctx.Type == MeshType.Bone)
                    {
                        if (!anyBone) continue;
                        bool isSelBone = selBones.Contains(ci);
                        if ( isSelBone && !ShowSelectedBone)   continue;
                        if (!isSelBone && !ShowUnselectedBone) continue;
                        col = isSelBone ? BoneWireSelColor : BoneWireColor;
                    }
                    else
                    {
                        // メッシュ原点マーカー（ObjectMoveTool のピック対象と同じ集合）
                        if (!anyOrigin) continue;
                        if (!IsMeshOriginTarget(ctx.Type)) continue;
                        if (!ShowMirrorMeshOrigin && IsMirrorSideType(ctx.Type)) continue;
                        bool isSelMesh = selMeshes.Contains(ci);
                        if ( isSelMesh && !ShowSelectedMeshOrigin)   continue;
                        if (!isSelMesh && !ShowUnselectedMeshOrigin) continue;
                        col = isSelMesh ? MeshOriginSelColor : MeshOriginColor;
                    }

                    if (!ExtractBoneTransform(ctx.WorldMatrix, out Vector3 pos, out Quaternion rot)) continue;

                    // 大きさは毎回渡す。キャッシュ済みでも UpdateBoneLineMesh が
                    // 頂点を書き直すため、設定変更後に PresentAll するだけで反映される。
                    // キャッシュを捨てる必要は無い。
                    var key = (mi, ci);
                    if (!_boneMeshCache.TryGetValue(key, out var boneMesh) || boneMesh == null)
                    {
                        boneMesh = BuildBoneLineMesh(pos, rot, col, BoneMarkerScale);
                        _boneMeshCache[key] = boneMesh;
                    }
                    else
                    {
                        UpdateBoneLineMesh(boneMesh, pos, rot, col, BoneMarkerScale);
                    }
                }
            }
        }

        /// <summary>
        /// 原点マーカーを描く対象か（Bone は専用経路で描くため除外）。
        /// 除外条件は ObjectMoveTool.TryPickObject のピック対象フィルタと一致させる。
        /// </summary>
        private static bool IsMirrorSideType(MeshType t)
            => t == MeshType.MirrorSide || t == MeshType.BakedMirror;

        private static bool IsMeshOriginTarget(MeshType t)
        {
            return t != MeshType.Bone
                && t != MeshType.Morph
                && t != MeshType.RigidBody
                && t != MeshType.RigidBodyJoint
                && t != MeshType.Group;
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 計算処理（BuildBoneLineMesh / UpdateBoneLineMesh / ExtractBoneTransform 等）は
        /// 一切禁止。全ての準備は PrepareBones で完了させておくこと。
        /// OnRenderObject() から毎フレーム呼ばれる想定。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void SubmitBones(ProjectContext project, Camera cam)
        {
            if (project == null || cam == null) return;

            bool anyBone   = ShowSelectedBone       || ShowUnselectedBone;
            bool anyOrigin = ShowSelectedMeshOrigin || ShowUnselectedMeshOrigin;
            if (!anyBone && !anyOrigin) return;

            // Phase 2c-2: 選択/非選択で別マテリアル（global alpha が異なる）。
            var matSel   = GetBoneOverlayMaterial(isSelected: true);
            var matUnsel = GetBoneOverlayMaterial(isSelected: false);
            if (matSel == null && matUnsel == null) return;

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                var selBones  = model.SelectedBoneIndices;
                var selMeshes = model.SelectedDrawableMeshIndices;

                for (int ci = 0; ci < model.MeshContextCount; ci++)
                {
                    var ctx = model.GetMeshContext(ci);
                    if (ctx == null) continue;

                    bool isSel;
                    if (ctx.Type == MeshType.Bone)
                    {
                        if (!anyBone) continue;
                        isSel = selBones.Contains(ci);
                        if ( isSel && !ShowSelectedBone)   continue;
                        if (!isSel && !ShowUnselectedBone) continue;
                    }
                    else
                    {
                        if (!anyOrigin) continue;
                        if (!IsMeshOriginTarget(ctx.Type)) continue;
                        if (!ShowMirrorMeshOrigin && IsMirrorSideType(ctx.Type)) continue;
                        isSel = selMeshes.Contains(ci);
                        if ( isSel && !ShowSelectedMeshOrigin)   continue;
                        if (!isSel && !ShowUnselectedMeshOrigin) continue;
                    }

                    var chosenMat = isSel ? matSel : matUnsel;
                    if (chosenMat == null) continue;

                    var key = (mi, ci);
                    if (_boneMeshCache.TryGetValue(key, out var boneMesh) && boneMesh != null)
                        Graphics.DrawMesh(boneMesh, Matrix4x4.identity, chosenMat, 0, cam);
                }
            }
        }

        /// <summary>
        /// 【旧 API】ボーンを描画する。Phase 1 暫定として残置。
        /// 内部は Prepare + Submit を連続呼びする。新規コードは分離して呼ぶこと。
        /// </summary>
        public void DrawBones(ProjectContext project, Camera cam)
        {
            PrepareBones(project);
            SubmitBones(project, cam);
        }

        // ================================================================
        // 法線描画（頂点スロット単位）
        //
        // 【表示単位】
        //   法線は Vertex.Normals（スロット配列）に入っており、面は
        //   Face.NormalIndices でどのスロットを使うかを指す。よって同じスロットは
        //   複数の面から参照される。ここでは (頂点, スロット) 単位で 1 本だけ描く。
        //
        //   【TotalExpandedVertexCount とは一致しない】
        //   ここは mo.Vertices を全走査するので孤立頂点の法線も描く。一方
        //   UnifiedBufferManager.TotalExpandedVertexCount は孤立頂点を除外した
        //   数（MeshExpansion の規則）なので、孤立頂点を持つメッシュでは
        //   本数の方が多くなる。両者を突き合わせないこと。
        //
        // 【ワールド化】
        //   始点は GPU 変換済みのワールド座標（GetDisplayPositions）。
        //   方向はローカル法線に頂点ごとのスキニング行列の 3x3 を掛けて正規化する。
        //   行列の組み立て式は UnifiedCompute.compute の TransformVertices と同一。
        //   非スキン頂点はウェイトが (1,0,0,0) なので行列 1 個の参照に帰着する。
        //
        // 【ドラッグ中】
        //   TransformDragging 中は始点も方向も変わるため位置のみの軽量更新が成立せず、
        //   非表示にする。ドラッグ終了で Normal モードに戻り再構築される。
        // ================================================================

        /// <summary>
        /// 【event 駆動で呼ぶ】法線表示用のラインメッシュを事前構築・更新する。
        /// 全選択メッシュの全頂点スロットを走査する重い処理。
        /// Submit と分離されているため、毎フレーム呼ぶのは禁止。
        ///
        /// ★★★ 呼び出し順の制約（厳守） ★★★
        /// PrepareWireframeAndVertices より前に呼ぶこと。
        /// PrepareWireframeAndVertices は末尾で adapter.ConsumeNormalMode() を呼び
        /// Normal モードを Idle へ降格させるため、後に呼ぶと AllowMeshRebuild が
        /// 常に false になり法線メッシュが二度と更新されない。
        /// ★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void PrepareNormals(ProjectContext project)
        {
            if (!ShowNormals) return;
            if (project == null) return;

            float length = DisplaySettings.GetF(DisplaySettings.KeyNormalLength);

            for (int mi = 0; mi < project.ModelCount && mi < _adapters.Count; mi++)
            {
                var adapter = _adapters[mi];
                if (adapter == null || !adapter.IsInitialized) continue;

                // ドラッグ中は抑止する。キャッシュはそのまま残し、Submit 側で止める。
                if (adapter.CurrentMode == UpdateMode.TransformDragging)
                {
                    _normalsSuppressed = true;
                    continue;
                }
                _normalsSuppressed = false;

                // 再構築が許可されていないモード（Idle / CameraDragging）は
                // 構築済みメッシュをそのまま使う。頂点は動いていない。
                if (!adapter.CurrentProfile.AllowMeshRebuild) continue;

                BuildNormalLineMesh(project.Models[mi], mi, adapter, length);
            }
        }

        /// <summary>
        /// 法線表示の抑止を明示的に切り替える。
        ///
        /// 【必要な理由】
        ///   頂点ドラッグの Dragging フェーズは PresentAll を通らない軽量経路
        ///   （PlayerViewportManager.EnterVerticesMoved の syncMc 経路）を走るため、
        ///   PrepareNormals が呼ばれず TransformDragging を検知できない。
        ///   そのままだと古い法線メッシュが提出され続けるので、
        ///   DragBegin で true、DragEnd で false を明示的に設定する。
        /// </summary>
        public void SetNormalsSuppressed(bool suppressed)
        {
            _normalsSuppressed = suppressed;
        }

        /// <summary>
        /// 1 モデル分の法線ラインメッシュを構築して _normalMeshCache に格納する。
        /// </summary>
        private void BuildNormalLineMesh(
            ModelContext model, int mi, UnifiedSystemAdapter adapter, float length)
        {
            if (model == null) return;

            var bufMgr = adapter.BufferManager;
            if (bufMgr == null) return;

            var positions = bufMgr.GetDisplayPositions();
            var meshInfos = bufMgr.MeshInfos;
            var mats      = bufMgr.TransformMatrices;
            var weights   = bufMgr.BoneWeights;
            var boneIdx   = bufMgr.BoneIndices;
            if (positions == null || meshInfos == null) return;

            int totalVerts = bufMgr.TotalVertexCount;

            _normalVerts.Clear();
            _normalColors.Clear();
            _normalIndices.Clear();

            var selMeshes = model.SelectedDrawableMeshIndices;
            if (selMeshes != null)
            {
                foreach (int ci in selMeshes)
                {
                    if (ci < 0 || ci >= model.MeshContextCount) continue;

                    var ctx = model.GetMeshContext(ci);
                    if (ctx == null || !ctx.IsVisible) continue;

                    var mo = ctx.MeshObject;
                    if (mo == null || mo.VertexCount == 0) continue;

                    int unified = bufMgr.ContextToUnifiedMeshIndex(ci);
                    if (unified < 0 || unified >= meshInfos.Length) continue;

                    int vertStart = (int)meshInfos[unified].VertexStart;
                    int vertCount = (int)meshInfos[unified].VertexCount;

                    for (int v = 0; v < mo.VertexCount && v < vertCount; v++)
                    {
                        var vertex = mo.Vertices[v];
                        int slotCount = vertex.Normals.Count;
                        if (slotCount == 0) continue;

                        int gi = vertStart + v;
                        if (gi < 0 || gi >= totalVerts) continue;

                        Vector3 root = positions[gi];

                        // 頂点ごとのスキニング行列 3x3 を 1 回だけ組み立て、
                        // その頂点の全スロットで使い回す。
                        if (!TryBuildSkinBasis(mats, weights, boneIdx, gi,
                                               out Vector3 bx, out Vector3 by, out Vector3 bz))
                            continue;

                        for (int s = 0; s < slotCount; s++)
                        {
                            Vector3 n = vertex.Normals[s];
                            Vector3 wn = bx * n.x + by * n.y + bz * n.z;

                            float mag = wn.magnitude;
                            if (mag < 1e-6f) continue;
                            wn /= mag;

                            int baseIdx = _normalVerts.Count;
                            _normalVerts.Add(root);
                            _normalVerts.Add(root + wn * length);
                            _normalColors.Add(NormalRootColor);
                            _normalColors.Add(NormalTipColor);
                            _normalIndices.Add(baseIdx);
                            _normalIndices.Add(baseIdx + 1);
                        }
                    }
                }
            }

            if (!_normalMeshCache.TryGetValue(mi, out var mesh) || mesh == null)
            {
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.name = "PolyLingNormalLines";
                _normalMeshCache[mi] = mesh;
            }
            else
            {
                mesh.Clear();
            }

            mesh.SetVertices(_normalVerts);
            mesh.SetColors(_normalColors);
            mesh.SetIndices(_normalIndices, MeshTopology.Lines, 0);
        }

        /// <summary>
        /// 指定グローバル頂点のスキニング行列 3x3 を列ベクトルとして返す。
        /// 式は UnifiedCompute.compute の TransformVertices カーネルと同一
        /// （4 ボーンのウェイト加重和）。
        /// </summary>
        private static bool TryBuildSkinBasis(
            Matrix4x4[] mats, Vector4[] weights, UInt4[] boneIdx, int gi,
            out Vector3 bx, out Vector3 by, out Vector3 bz)
        {
            bx = Vector3.right; by = Vector3.up; bz = Vector3.forward;
            if (mats == null || weights == null || boneIdx == null) return false;
            if (gi >= weights.Length || gi >= boneIdx.Length) return false;

            Vector4 w = weights[gi];
            UInt4   b = boneIdx[gi];

            // 非スキン頂点（ウェイトが (1,0,0,0)）は加重和を回さず行列 1 個で済ませる。
            if (w.x >= 0.99999f)
            {
                int i0 = (int)b.x;
                if (i0 < 0 || i0 >= mats.Length) return false;
                var m = mats[i0];
                bx = new Vector3(m.m00, m.m10, m.m20);
                by = new Vector3(m.m01, m.m11, m.m21);
                bz = new Vector3(m.m02, m.m12, m.m22);
                return true;
            }

            bx = Vector3.zero; by = Vector3.zero; bz = Vector3.zero;
            bool any = false;

            AccumulateSkinBasis(mats, (int)b.x, w.x, ref bx, ref by, ref bz, ref any);
            AccumulateSkinBasis(mats, (int)b.y, w.y, ref bx, ref by, ref bz, ref any);
            AccumulateSkinBasis(mats, (int)b.z, w.z, ref bx, ref by, ref bz, ref any);
            AccumulateSkinBasis(mats, (int)b.w, w.w, ref bx, ref by, ref bz, ref any);

            return any;
        }

        private static void AccumulateSkinBasis(
            Matrix4x4[] mats, int index, float weight,
            ref Vector3 bx, ref Vector3 by, ref Vector3 bz, ref bool any)
        {
            if (weight == 0f) return;
            if (index < 0 || index >= mats.Length) return;

            var m = mats[index];
            bx.x += m.m00 * weight; bx.y += m.m10 * weight; bx.z += m.m20 * weight;
            by.x += m.m01 * weight; by.y += m.m11 * weight; by.z += m.m21 * weight;
            bz.x += m.m02 * weight; bz.y += m.m12 * weight; bz.z += m.m22 * weight;
            any = true;
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 計算処理（BuildNormalLineMesh 等）は一切禁止。
        /// 全ての準備は PrepareNormals で完了させておくこと。
        /// OnRenderObject() から毎フレーム呼ばれる想定。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void SubmitNormals(ProjectContext project, Camera cam)
        {
            if (!ShowNormals) return;
            if (_normalsSuppressed) return;
            if (project == null || cam == null) return;

            // ボーンと同じ overlay マテリアル（ZTest Always、頂点色をそのまま出力）。
            var mat = GetBoneOverlayMaterial(isSelected: true);
            if (mat == null) return;

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                if (_normalMeshCache.TryGetValue(mi, out var mesh)
                    && mesh != null && mesh.vertexCount > 0)
                {
                    Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, cam);
                }
            }
        }

        /// <summary>
        /// 【旧 API】法線を描画する。Prepare + Submit を連続呼びする。
        /// 新規コードは分離して呼ぶこと。
        /// </summary>
        public void DrawNormals(ProjectContext project, Camera cam)
        {
            PrepareNormals(project);
            SubmitNormals(project, cam);
        }

        // ================================================================
        // スキンウェイト可視化描画
        // ================================================================

        /// <summary>
        /// スキンウェイトペイントモード時にウェイトをヒートマップカラーで描画する。
        /// DrawMeshes の直後に呼ぶこと。
        /// </summary>
        /// <summary>
        /// 【event 駆動で呼ぶ】ウェイトヒートマップ用の頂点カラーを事前計算する。
        /// ApplyVisualizationColors は Mesh.colors への書き込みを含む重い処理。
        /// Phase 1: スキンウェイトパネル操作・選択ボーン変更・ターゲットメッシュ変更時に呼び出す。
        /// Submit と分離されているため、毎フレーム呼ぶのは禁止。
        /// </summary>
        public void PrepareWeightVisualization(ProjectContext project)
        {
            var masterIndices = CollectWeightVisTargets(project?.CurrentModel);
            if (masterIndices == null) return;

            var model = project.CurrentModel;

            // 複数ボーン合算表示（数値設定パネルの「色」トグル）が指定されていれば
            // そちらを優先する。指定が無ければ従来の 1 ボーン表示。
            var targetBones = Poly_Ling.Tools.SkinWeightPaintTool.VisualizationTargetBones;
            int targetBone  = Poly_Ling.Tools.SkinWeightPaintTool.VisualizationTargetBone;

            foreach (int masterIdx in masterIndices)
            {
                var ctx = model.GetMeshContext(masterIdx);
                if (ctx?.UnityMesh == null || ctx.UnityMesh.vertexCount <= 0
                 || ctx.MeshObject == null || !ctx.IsVisible) continue;

                if (targetBones != null)
                    Poly_Ling.Tools.SkinWeightPaintTool.ApplyVisualizationColors(
                        ctx.UnityMesh, ctx.MeshObject, targetBones);
                else
                    Poly_Ling.Tools.SkinWeightPaintTool.ApplyVisualizationColors(
                        ctx.UnityMesh, ctx.MeshObject, targetBone);
            }
        }

        /// <summary>CollectWeightVisTargets の戻り値。使い回して毎フレームの確保を避ける。</summary>
        private static readonly List<int> _weightVisTargets = new List<int>();

        /// <summary>
        /// ウェイト可視化の対象メッシュ（MasterIndex）を返す。可視化が無効なら null。
        ///
        /// 【一本化の理由】
        /// 対象の決め方は SubmitMeshes（ベース面をスキップする判定）・
        /// PrepareWeightVisualization（頂点カラーを書く対象）・
        /// SubmitWeightVisualization（可視化を描く対象）の 3 箇所で必ず一致していなければ
        /// ならない。1 箇所でもずれると「ベース面を消したのに可視化は描かれない」
        /// あるいは「両方描かれてベース面が可視化色を上書きする」状態になる。
        /// 判定はこの関数だけが持つこと。
        /// </summary>
        /// <remarks>
        /// SubmitMeshes は毎フレーム・カメラごとに呼ばれるため、戻り値は使い回しの
        /// 静的リストとし、呼び出しごとに新規確保しない。呼び出しは入れ子にならない
        /// （SubmitMeshes → 完了 → SubmitWeightVisualization の順）ので共有して問題ない。
        /// 可視化が無効なときは Clear すら行わず即 null を返す。
        /// </remarks>
        private static List<int> CollectWeightVisTargets(ModelContext model)
        {
            if (!Poly_Ling.Tools.SkinWeightPaintTool.IsVisualizationActive) return null;
            if (model == null) return null;

            _weightVisTargets.Clear();

            int targetMeshIdx = Poly_Ling.Tools.SkinWeightPaintTool.ActivePanel?.CurrentTargetMesh ?? -1;
            if (targetMeshIdx >= 0) _weightVisTargets.Add(targetMeshIdx);
            else                    _weightVisTargets.AddRange(model.SelectedDrawableMeshIndices);

            return _weightVisTargets;
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 計算処理（ApplyVisualizationColors 等）は一切禁止。
        /// 全ての準備は PrepareWeightVisualization で完了させておくこと。
        /// OnRenderObject() から毎フレーム呼ばれる想定。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void SubmitWeightVisualization(ProjectContext project, Camera cam)
        {
            if (cam == null) return;

            var masterIndices = CollectWeightVisTargets(project?.CurrentModel);
            if (masterIndices == null) return;

            var visMat = Poly_Ling.Tools.SkinWeightPaintTool.GetVisualizationMaterial();
            if (visMat == null) return;

            var model = project.CurrentModel;

            foreach (int masterIdx in masterIndices)
            {
                var ctx = model.GetMeshContext(masterIdx);
                if (ctx?.UnityMesh == null || ctx.UnityMesh.vertexCount <= 0
                 || ctx.MeshObject == null || !ctx.IsVisible) continue;

                var mesh = ctx.UnityMesh;
                // 通常描画 SubmitMeshes と同じく identity で描画する。
                // 頂点はワールド化済み（スキンドメッシュ）/GPU compute 側で処理されるため、
                // ここで ctx.WorldMatrix を掛けると二重変換になりずれる。
                var displayMatrix = Matrix4x4.identity;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    Graphics.DrawMesh(mesh, displayMatrix, visMat, 0, cam, sub);
            }
        }

        /// <summary>
        /// 【旧 API】ウェイト可視化を描画する。Phase 1 暫定として残置。
        /// 内部は Prepare + Submit を連続呼びする。新規コードは分離して呼ぶこと。
        /// </summary>
        public void DrawWeightVisualization(ProjectContext project, Camera cam)
        {
            PrepareWeightVisualization(project);
            SubmitWeightVisualization(project, cam);
        }

        // ================================================================
        // シーンクリア
        // ================================================================

        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (PlayerViewportManager 上の " +
            "EnterProjectChanged / EnterTopologyChanged / EnterCameraChanged / " +
            "EnterVerticesMoved / EnterHoverChanged / EnterDisplaySettingsChanged) " +
            "経由で呼ぶこと。本 API を Player 配下の Core / Dispatcher / RemoteFlow から " +
            "直接呼ぶことは禁止。",
            error: false)]
        public void ClearScene()
        {
            foreach (var adapter in _adapters)
            {
                // ClearScene では全 slot を対象にクリア（Dispose 前処理）
#pragma warning disable CS0618
                adapter?.CleanupQueued();
#pragma warning restore CS0618
                adapter?.Dispose();
            }
            _adapters.Clear();
            _selectedMeshIndexForDraw.Clear();

            ClearMeshCaches();
            _normalsSuppressed = false;

            if (_boneMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_boneMaterial);
                _boneMaterial = null;
            }
            // Phase 2c-2: ボーン overlay 用マテリアルも破棄
            if (_boneOverlayMaterialSelected != null)
            {
                UnityEngine.Object.DestroyImmediate(_boneOverlayMaterialSelected);
                _boneOverlayMaterialSelected = null;
            }
            if (_boneOverlayMaterialUnselected != null)
            {
                UnityEngine.Object.DestroyImmediate(_boneOverlayMaterialUnselected);
                _boneOverlayMaterialUnselected = null;
            }
            if (_defaultMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_defaultMaterial);
                _defaultMaterial = null;
            }
        }

        // ================================================================
        // オービットターゲット初期化ヘルパー
        // ================================================================

        /// <summary>最初のDrawableメッシュのboundsを返す。カメラ初期位置計算に使用。</summary>
        public bool TryGetInitialBounds(ProjectContext project, out Bounds bounds)
        {
            bounds = default;
            if (project == null) return false;
            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var drawables = project.Models[mi].DrawableMeshes;
                if (drawables == null) continue;
                foreach (var entry in drawables)
                {
                    var mesh = entry.Context?.UnityMesh;
                    if (mesh != null) { bounds = mesh.bounds; return true; }
                }
            }
            return false;
        }

        // ================================================================
        // Dispose
        // ================================================================

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
#pragma warning disable CS0618
            ClearScene();
#pragma warning restore CS0618
        }

        // ================================================================
        // マテリアルヘルパー
        // ================================================================

        private Material GetDefaultMaterial()
        {
            if (_defaultMaterial != null) return _defaultMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                      ?? Shader.Find("Standard")
                      ?? Shader.Find("Unlit/Color");
            if (shader == null) return null;
            _defaultMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _defaultMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f));
            _defaultMaterial.SetColor("_Color",     new Color(0.7f, 0.7f, 0.7f));
            return _defaultMaterial;
        }

        private Material GetBoneMaterial()
        {
            // Phase 2c-2 以降の新規コードは GetBoneOverlayMaterial(isSelected) を使うこと。
            // 本メソッドは後方互換のため残置。
            return GetBoneOverlayMaterial(isSelected: true);
        }

        /// <summary>
        /// Phase 2c-2: ボーン overlay 用マテリアル（ZTest Always、常に最前面）。
        /// 選択/非選択で global alpha を切り替えて保持する。
        /// </summary>
        private Material GetBoneOverlayMaterial(bool isSelected)
        {
            if (isSelected)
            {
                if (_boneOverlayMaterialSelected != null) return _boneOverlayMaterialSelected;
                var shader = Shader.Find("Poly_Ling/Bone3D_Overlay");
                if (shader == null) return null;
                _boneOverlayMaterialSelected = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                // 選択ボーンは不透明
                _boneOverlayMaterialSelected.SetFloat("_GlobalAlpha", 1.0f);
                return _boneOverlayMaterialSelected;
            }
            else
            {
                if (_boneOverlayMaterialUnselected != null) return _boneOverlayMaterialUnselected;
                var shader = Shader.Find("Poly_Ling/Bone3D_Overlay");
                if (shader == null) return null;
                _boneOverlayMaterialUnselected = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                // 非選択ボーンはボディと干渉しないよう半透明化
                _boneOverlayMaterialUnselected.SetFloat("_GlobalAlpha", 0.5f);
                return _boneOverlayMaterialUnselected;
            }
        }

        // ================================================================
        // ボーンメッシュ構築
        // ================================================================

        /// <param name="scale">
        /// くさびの大きさ。呼出側が BoneMarkerScale を渡す。
        /// 静的メソッドなのでインスタンスのプロパティを直接は読めない。
        /// </param>
        private static Mesh BuildBoneLineMesh(Vector3 pos, Quaternion rot, Color col, float scale)
        {
            int ec = BoneShapeEdges.GetLength(0);
            var verts   = new Vector3[ec * 2];
            var colors  = new Color[ec * 2];
            var uvs     = new Vector2[ec * 2];
            var indices = new int[ec * 2];

            for (int i = 0; i < ec; i++)
            {
                verts[i*2]   = pos + rot * (BoneShapeVertices[BoneShapeEdges[i,0]] * scale);
                verts[i*2+1] = pos + rot * (BoneShapeVertices[BoneShapeEdges[i,1]] * scale);
                colors[i*2] = colors[i*2+1] = col;
                uvs[i*2] = uvs[i*2+1] = Vector2.zero;
                indices[i*2] = i*2; indices[i*2+1] = i*2+1;
            }

            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            return mesh;
        }

        /// <param name="scale">くさびの大きさ。BuildBoneLineMesh と同じ値を渡すこと。</param>
        private static void UpdateBoneLineMesh(Mesh mesh, Vector3 pos, Quaternion rot, Color col, float scale)
        {
            int ec = BoneShapeEdges.GetLength(0);
            var verts  = new Vector3[ec * 2];
            var colors = new Color[ec * 2];
            for (int i = 0; i < ec; i++)
            {
                verts[i*2]   = pos + rot * (BoneShapeVertices[BoneShapeEdges[i,0]] * scale);
                verts[i*2+1] = pos + rot * (BoneShapeVertices[BoneShapeEdges[i,1]] * scale);
                colors[i*2] = colors[i*2+1] = col;
            }
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
        }

        private static bool ExtractBoneTransform(Matrix4x4 m, out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3(m.m03, m.m13, m.m23);
            Vector3 c0 = new Vector3(m.m00, m.m10, m.m20);
            Vector3 c1 = new Vector3(m.m01, m.m11, m.m21);
            Vector3 c2 = new Vector3(m.m02, m.m12, m.m22);
            float sx = c0.magnitude, sy = c1.magnitude, sz = c2.magnitude;
            if (sx < 0.0001f || sy < 0.0001f || sz < 0.0001f) { rot = Quaternion.identity; return false; }
            var r = Matrix4x4.identity;
            r.SetColumn(0, c0/sx); r.SetColumn(1, c1/sy); r.SetColumn(2, c2/sz);
            rot = r.rotation;
            return true;
        }
    }
}
