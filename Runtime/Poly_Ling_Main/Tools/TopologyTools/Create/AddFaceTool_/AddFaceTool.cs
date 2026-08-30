// Tools/AddFaceTool.cs
// 面追加ツール（2点=Line、3点=Triangle、4点=Quad）
// マルチマテリアル対応 + Line描画対応

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;
using static Poly_Ling.Gizmo.GLGizmoDrawer;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 面の追加モード
    /// </summary>
    public enum AddFaceMode
    {
        Line = 2,       // 2点（補助線）
        Triangle = 3,   // 3点（三角形）
        Quad = 4        // 4点（四角形）
    }

    /// <summary>
    /// 点の情報（既存頂点 or 新規作成点）
    /// </summary>
    public struct PointInfo
    {
        public bool IsExistingVertex;
        public int ExistingVertexIndex;
        public Vector3 Position;

        public static PointInfo FromExisting(int vertexIndex, Vector3 position)
        {
            return new PointInfo
            {
                IsExistingVertex = true,
                ExistingVertexIndex = vertexIndex,
                Position = position
            };
        }

        public static PointInfo FromNew(Vector3 position)
        {
            return new PointInfo
            {
                IsExistingVertex = false,
                ExistingVertexIndex = -1,
                Position = position
            };
        }
    }

    /// <summary>
    /// 面追加ツール
    /// </summary>
    public partial class AddFaceTool : IEditTool
    {
        public string Name => "Add Face";
        public string DisplayName => "Add Face";
        //public ToolCategory Category => ToolCategory.Topology;  
        /// <summary>
        /// ローカライズされた表示名を取得
        /// </summary>
        public string GetLocalizedDisplayName() => L.Get("Tool_Add Face");

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private AddFaceSettings _settings = new AddFaceSettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        private AddFaceMode Mode
        {
            get => _settings.Mode;
            set => _settings.Mode = value;
        }

        private float DefaultDistance
        {
            get => _settings.DefaultDistance;
            set => _settings.DefaultDistance = value;
        }

        private bool ContinuousLine
        {
            get => _settings.ContinuousLine;
            set => _settings.ContinuousLine = value;
        }

        // ================================================================
        // Player ビュー用公開 API
        // ================================================================
        public AddFaceMode ModePublic    { get => Mode; set { Mode = value; } }
        public bool ContinuousLinePublic { get => ContinuousLine; set => ContinuousLine = value; }
        public int  PlacedPointCount     => _points.Count;
        public int  RequiredPointsPublic => RequiredPoints;
        public void ClearPointsPublic()  { _points.Clear(); _lastLinePoint = null; }

        /// <summary>
        /// Quad モードで3点配置済みのとき、その3点で三角形を作って確定する。
        /// 条件を満たさないときは何もせず false を返す。右クリック／Escape から呼ぶ。
        /// </summary>
        public bool FinishAsTriangle(ToolContext ctx)
        {
            if (ctx == null || Mode != AddFaceMode.Quad || _points.Count != 3) return false;
            CreateFace(ctx);
            _points.Clear();
            ctx.Repaint?.Invoke();
            return true;
        }

        /// <summary>
        /// 直前に指定した点を 1 つ取り消す。Backspace / Delete から呼ぶ。
        /// 1 点も指定されていなければ何もせず false を返す。
        /// 連続線分モードの開始点（_lastLinePoint）は対象外（既に確定済みの点のため）。
        /// </summary>
        public bool RemoveLastPoint()
        {
            if (_points.Count == 0) return false;
            _points.RemoveAt(_points.Count - 1);
            return true;
        }

        // ================================================================
        // Player オーバーレイ描画用プレビューデータ
        // ================================================================

        /// <summary>Player の UIToolkit オーバーレイが使うプレビューデータ</summary>
        public struct AddFacePreviewData
        {
            /// <summary>配置済み点（Position, IsExistingVertex）</summary>
            public PointInfo[] PlacedPoints;
            /// <summary>プレビュー点が有効か</summary>
            public bool PreviewValid;
            /// <summary>プレビュー点ワールド座標</summary>
            public Vector3 PreviewPoint;
            /// <summary>プレビューが既存頂点にスナップしているか</summary>
            public bool PreviewSnapped;
            /// <summary>スナップ先が非選択オブジェクトの頂点か（色を変えて示す）</summary>
            public bool PreviewSnappedUnselected;
            /// <summary>スナップ先の既存頂点インデックス（未スナップは -1）</summary>
            public int PreviewVertexIndex;
            /// <summary>連続線分モードの開始点（nullなら不使用）</summary>
            public PointInfo? ContinuousLineStart;
            /// <summary>Quad モードで3点配置済み、かつホバー中の既存頂点が1点目と同じ</summary>
            public bool CloseToStart;
        }

        public AddFacePreviewData GetPreviewData()
        {
            PointInfo? contStart = null;
            if (Mode == AddFaceMode.Line && ContinuousLine && _points.Count == 0 && _lastLinePoint.HasValue)
                contStart = _lastLinePoint;
            return new AddFacePreviewData
            {
                PlacedPoints       = _points.ToArray(),
                PreviewValid       = _previewValid,
                PreviewPoint       = _previewPoint,
                // 他メッシュ頂点への吸着でもスナップ表示にする。
                // その場合 PreviewVertexIndex は -1 のままで、
                // オーバーレイ側は PreviewPoint（ローカル座標）を使って描画する。
                PreviewSnapped     = _previewHitVertex >= 0 || _previewSnappedOther,
                PreviewSnappedUnselected = _previewSnappedOther && _snapWorldFromUnselected,
                PreviewVertexIndex = _previewHitVertex,
                ContinuousLineStart = contStart,
                CloseToStart       = IsQuadCloseToStart(),
            };
        }

        /// <summary>
        /// Quad モードで3点配置済み、かつホバー中の既存頂点が1点目と同じかを返す。
        /// この状態で左クリックすると4点目を置かずに三角形を作る。
        /// 1点目が新規点のときはまだメッシュに頂点が無く番号で一致させられないため false。
        /// </summary>
        private bool IsQuadCloseToStart()
        {
            if (Mode != AddFaceMode.Quad || _points.Count != 3) return false;
            var start = _points[0];
            if (!start.IsExistingVertex) return false;
            return _previewHitVertex >= 0 && _previewHitVertex == start.ExistingVertexIndex;
        }

        /// <summary>
        /// 深さの基準に使う「直前に指定された点」を返す。未指定なら null。
        /// 連続線分モードで確定点が無い場合は、その開始点（前回の終点）を返す。
        /// Position は操作対象メッシュのローカル座標。
        /// </summary>
        public PointInfo? GetLastPoint()
        {
            if (_points.Count > 0) return _points[_points.Count - 1];
            if (Mode == AddFaceMode.Line && ContinuousLine && _lastLinePoint.HasValue)
                return _lastLinePoint;
            return null;
        }

        /// <summary>配置済み点のラベルリストを返す（SubPanel 表示用）</summary>
        public System.Collections.Generic.List<string> GetPointLabels()
        {
            var labels = new System.Collections.Generic.List<string>();
            for (int i = 0; i < _points.Count; i++)
            {
                var p = _points[i];
                labels.Add(p.IsExistingVertex
                    ? $"  [{i + 1}] 既存頂点 #{p.ExistingVertexIndex}"
                    : $"  [{i + 1}] 新規点");
            }
            return labels;
        }

        // === 状態 ===
        private List<PointInfo> _points = new List<PointInfo>();
        private PointInfo? _lastLinePoint = null;  // 連続線分の最後の点
        private Vector3 _previewPoint;          // 現在のマウス位置での候補点
        private bool _previewValid = false;
        private int _previewHitVertex = -1;     // プレビュー時に既存頂点にヒットしている場合
        private bool _previewSnappedOther = false;  // プレビューが他メッシュの頂点へ吸着している場合
        // 吸着先が非選択オブジェクトかどうか。ハンドラが吸着座標と一緒に指定する。
        private bool _snapWorldFromUnselected = false;

        // === モード名 ===
        private static readonly string[] ModeNames = { "Line (2)", "Triangle (3)", "Quad (4)" };
        private static readonly AddFaceMode[] ModeValues = { AddFaceMode.Line, AddFaceMode.Triangle, AddFaceMode.Quad };

        public int RequiredPoints => (int)Mode;

        // === IEditTool実装 ===

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            // 右クリックは点を1つ戻す
            if (ctx.CurrentButton == 1)
            {
                if (_points.Count > 0)
                {
                    _points.RemoveAt(_points.Count - 1);
                    ctx.Repaint?.Invoke();
                }
                else if (_lastLinePoint.HasValue)
                {
                    // 連続線分モードの開始点もクリア
                    _lastLinePoint = null;
                    ctx.Repaint?.Invoke();
                }
                return true;
            }

            // 左クリックのみ処理
            if (ctx.CurrentButton != 0)
                return false;

            // 点を追加
            PointInfo point = GetPointAtScreenPos(ctx, mousePos);

            // デバッグログ
            if (point.IsExistingVertex)
            {
                Debug.Log($"[AddFaceTool] Point added: Existing vertex V{point.ExistingVertexIndex} at {point.Position}");
            }
            else
            {
                Debug.Log($"[AddFaceTool] Point added: NEW vertex at {point.Position}");
            }

            // Quad モードで3点配置済み、4点目が1点目と同じ既存頂点なら三角形として確定する。
            // 1点目が新規点のときはまだ頂点番号が無いので、この判定には入らない。
            if (Mode == AddFaceMode.Quad && _points.Count == 3 &&
                point.IsExistingVertex && _points[0].IsExistingVertex &&
                point.ExistingVertexIndex == _points[0].ExistingVertexIndex)
            {
                CreateFace(ctx);
                _points.Clear();
                ctx.Repaint?.Invoke();
                return true;
            }

            // 連続線分モードの場合
            if (Mode == AddFaceMode.Line && ContinuousLine && _lastLinePoint.HasValue)
            {
                // 前回の最後の点と今回の点で線分を作成
                _points.Clear();
                _points.Add(_lastLinePoint.Value);
                _points.Add(point);
                var createdIndices = CreateFace(ctx);

                // 作成された頂点インデックスで_lastLinePointを更新
                if (createdIndices.Count >= 2)
                {
                    int lastIdx = createdIndices[1];
                    Vector3 lastPos = ctx.ActiveMeshObject.Vertices[lastIdx].Position;
                    _lastLinePoint = PointInfo.FromExisting(lastIdx, lastPos);
                    Debug.Log($"[AddFaceTool] Continuous line: next start = V{lastIdx}");
                }

                _points.Clear();
                ctx.Repaint?.Invoke();
                return true;
            }

            _points.Add(point);

            // 必要な点数に達したら面を作成
            if (_points.Count >= RequiredPoints)
            {
                var createdIndices = CreateFace(ctx);

                // 連続線分モードの場合、最後の頂点インデックスを保存
                if (Mode == AddFaceMode.Line && ContinuousLine && createdIndices.Count >= 2)
                {
                    int lastIdx = createdIndices[createdIndices.Count - 1];
                    Vector3 lastPos = ctx.ActiveMeshObject.Vertices[lastIdx].Position;
                    _lastLinePoint = PointInfo.FromExisting(lastIdx, lastPos);
                }

                _points.Clear();
            }

            ctx.Repaint?.Invoke();
            return true;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            // プレビュー更新
            UpdatePreview(ctx, mousePos);
            ctx.Repaint?.Invoke();
            return false;  // ドラッグは他の処理に委譲
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            return false;
        }

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        public void DrawGizmo(ToolContext ctx) { }
    

        public void OnActivate(ToolContext ctx)
        {
            _points.Clear();
            _previewValid = false;
            _lastLinePoint = null;

            // 選択された頂点を最初の点として使用
            if (ctx.SelectedVertices != null && ctx.SelectedVertices.Count > 0 && ctx.ActiveMeshObject != null)
            {
                var selectedList = new List<int>(ctx.SelectedVertices);

                // Lineモードで2頂点選択されていれば即座に線分作成
                if (Mode == AddFaceMode.Line && selectedList.Count == 2)
                {
                    foreach (int idx in selectedList)
                    {
                        if (idx >= 0 && idx < ctx.ActiveMeshObject.VertexCount)
                        {
                            Vector3 pos = ctx.ActiveMeshObject.Vertices[idx].Position;
                            _points.Add(PointInfo.FromExisting(idx, pos));
                        }
                    }

                    if (_points.Count == 2)
                    {
                        var createdIndices = CreateFace(ctx);

                        // 連続モードなら最後の点を保持
                        if (ContinuousLine && createdIndices.Count >= 2)
                        {
                            int lastIdx = createdIndices[1];
                            Vector3 lastPos = ctx.ActiveMeshObject.Vertices[lastIdx].Position;
                            _lastLinePoint = PointInfo.FromExisting(lastIdx, lastPos);
                        }
                        _points.Clear();
                    }
                    return;
                }

                // 1頂点選択の場合、それを最初の点として使用
                if (selectedList.Count == 1)
                {
                    int selectedIdx = selectedList[0];
                    if (selectedIdx >= 0 && selectedIdx < ctx.ActiveMeshObject.VertexCount)
                    {
                        Vector3 pos = ctx.ActiveMeshObject.Vertices[selectedIdx].Position;
                        var startPoint = PointInfo.FromExisting(selectedIdx, pos);

                        if (Mode == AddFaceMode.Line && ContinuousLine)
                        {
                            // 連続線分モードの場合は開始点として設定
                            _lastLinePoint = startPoint;
                        }
                        else
                        {
                            // 通常モードの場合は最初の点として追加
                            _points.Add(startPoint);
                        }
                    }
                }
            }
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _points.Clear();
            _previewValid = false;
            _lastLinePoint = null;
            _gpuHoverVertex = -1;
            _gpuHoverSnapWorld = null;
            _previewSnappedOther = false;
            _snapWorldFromUnselected = false;
        }

        public void Reset()
        {
            _points.Clear();
            _previewValid = false;
            _previewHitVertex = -1;
            _lastLinePoint = null;
            _gpuHoverVertex = -1;
            _gpuHoverSnapWorld = null;
            _previewSnappedOther = false;
            _snapWorldFromUnselected = false;
        }

        // === 内部メソッド ===

        // GPU ホバー由来の既存頂点（Player でハンドラが click/hover 毎に設定）。未ヒットは -1。
        private int _gpuHoverVertex = -1;

        /// <summary>
        /// 次回クリック／プレビューでスナップ対象にする既存頂点を GPU ホバー由来で指定する。
        /// Player のハンドラが OnMouseDown / UpdateHover 直前に呼ぶ。未ヒットは -1。
        /// ここで指定できるのは「操作対象メッシュ（ctx.ActiveMeshObject）内の頂点」だけ。
        /// 面の頂点として番号をそのまま再利用するため、他メッシュの番号は渡せない。
        /// </summary>
        public void SetGpuHoverVertex(int vertex) => _gpuHoverVertex = vertex;

        // 操作対象メッシュ以外の頂点へ吸着する場合のワールド座標。未ヒットは null。
        private Vector3? _gpuHoverSnapWorld = null;

        /// <summary>
        /// 操作対象メッシュ以外のメッシュの頂点へ「位置だけ」吸着させる指定。
        /// 頂点番号はメッシュごとに意味が違うため再利用できない。
        /// よって吸着点は操作対象メッシュ側の新規頂点として作られ、
        /// 座標のみがホバー先の頂点と一致する（頂点の結合は行わない）。
        /// Player のハンドラが OnMouseDown / UpdateHover 直前に呼ぶ。未ヒットは null。
        /// SetGpuHoverVertex が有効なとき（同一メッシュ内ヒット）は必ず null を渡すこと。
        /// </summary>
        public void SetGpuHoverSnapWorld(Vector3? world) => SetGpuHoverSnapWorld(world, false);

        /// <summary>
        /// 吸着座標に加えて、その吸着先が非選択オブジェクトかどうかを指定する。
        /// 表示色を分けるためだけに使い、座標の扱いは fromUnselected によらず同じ。
        /// </summary>
        public void SetGpuHoverSnapWorld(Vector3? world, bool fromUnselected)
        {
            _gpuHoverSnapWorld       = world;
            _snapWorldFromUnselected = world.HasValue && fromUnselected;
        }

        /// <summary>
        /// スクリーン位置から点を取得（戻り値 Position は常に「ローカル座標」）
        /// 優先順位:
        ///   1. GPU ホバー既存頂点（操作対象メッシュ内 → 頂点番号を再利用）
        ///   2. 他メッシュ頂点への吸着（座標のみ一致する新規点）
        ///   3. WorkPlane 交点
        /// </summary>
        private PointInfo GetPointAtScreenPos(ToolContext ctx, Vector2 screenPos)
        {
            // GPU ホバー由来の既存頂点があればスナップ。CPU ヒットテスト（FindNearestVertexAtScreen）は使用禁止。
            var mo = ctx.ActiveMeshObject;
            if (mo != null && _gpuHoverVertex >= 0 && _gpuHoverVertex < mo.VertexCount)
            {
                Vector3 pos = mo.Vertices[_gpuHoverVertex].Position;
                return PointInfo.FromExisting(_gpuHoverVertex, pos);
            }

            // 他メッシュの頂点への吸着。ワールド座標を操作対象メッシュのローカルへ落とす。
            // ActiveWorldToLocal を通すのは GetLocalPositionFromScreen と同じ基準に
            // 揃えるため。CreateFace の RebasePositionToSource がこの基準を前提に
            // ActiveWorldMatrix でワールドへ戻すので、ここでずらすと座標が壊れる。
            if (_gpuHoverSnapWorld.HasValue)
            {
                return PointInfo.FromNew(ctx.ActiveWorldToLocal(_gpuHoverSnapWorld.Value));
            }

            // WorkPlane との交点（ワールド）をローカルへ変換して保持する。
            // Vertices[].Position はローカル座標なので、ここでワールドのまま保持すると
            // 面生成時に WorldMatrix が二重適用され、頂点が別の場所に現れる。
            Vector3 localPos = GetLocalPositionFromScreen(ctx, screenPos);
            return PointInfo.FromNew(localPos);
        }

        /// <summary>
        /// スクリーン位置から最も近い頂点を検索
        /// 【CPUヒットテスト禁止。これもバグあり使用禁止】CPU ヒットテスト（WorldToScreen 投影＋画面距離）。呼出しは全撤去済み・本体はソース保持。
        /// 深度/遮蔽/WorldMatrix 非考慮で Player では誤選択する。GPU ホバー経路を使うこと。
        /// </summary>
        private int FindNearestVertexAtScreen(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.ActiveMeshObject == null) return -1;

            // ヒット半径（少し大きめに設定）
            float hitRadius = 15f;
            if (ctx.HandleRadius > 0) hitRadius = Mathf.Max(hitRadius, ctx.HandleRadius);

            int nearest = -1;
            float minDist = hitRadius;

            for (int i = 0; i < ctx.ActiveMeshObject.VertexCount; i++)
            {
                Vector2 vertScreen = ctx.WorldToScreenPos(
                    ctx.ActiveMeshObject.Vertices[i].Position,
                    ctx.PreviewRect,
                    ctx.CameraPosition,
                    ctx.CameraTarget);

                float dist = Vector2.Distance(screenPos, vertScreen);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }

        /// <summary>
        /// スクリーン位置から「操作対象メッシュのローカル座標」を取得する。
        /// レイ／WorkPlane はワールド空間なので、最後に ActiveWorldToLocal で変換する。
        /// </summary>
        private Vector3 GetLocalPositionFromScreen(ToolContext ctx, Vector2 screenPos)
        {
            // スクリーン座標からレイを作成
            Ray ray;
            if (ctx.ScreenPosToRay != null)
            {
                ray = ctx.ScreenPosToRay(screenPos);
            }
            else
            {
                // フォールバック（通常は使われない）
                Vector3 forward = (ctx.CameraTarget - ctx.CameraPosition).normalized;
                ray = new Ray(ctx.CameraPosition, forward);
            }

            // WorkPlaneとの交点を試みる（交点はワールド座標）
            if (ctx.WorkPlane != null)
            {
                if (ctx.WorkPlane.RayIntersect(ray.origin, ray.direction, out Vector3 hitPoint))
                {
                    return ctx.ActiveWorldToLocal(hitPoint);
                }
            }

            // 交差しない場合は、カメラから適当な距離の点
            return ctx.ActiveWorldToLocal(ray.origin + ray.direction * DefaultDistance * ctx.CameraDistance);
        }

        /// <summary>
        /// プレビュー点を更新
        /// </summary>
        private void UpdatePreview(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.ActiveMeshObject == null || !ctx.PreviewRect.Contains(screenPos))
            {
                _previewValid = false;
                _previewSnappedOther = false;
                return;
            }

            // 優先順位は GetPointAtScreenPos と同一にすること。
            // ずれるとプレビュー位置と実際に置かれる点が食い違う。
            var mo = ctx.ActiveMeshObject;
            if (mo != null && _gpuHoverVertex >= 0 && _gpuHoverVertex < mo.VertexCount)
            {
                // GPU ホバー由来の既存頂点。CPU ヒットテスト（FindNearestVertexAtScreen）は使用禁止。
                _previewHitVertex    = _gpuHoverVertex;
                _previewPoint        = mo.Vertices[_gpuHoverVertex].Position;
                _previewSnappedOther = false;
            }
            else if (_gpuHoverSnapWorld.HasValue)
            {
                // 他メッシュ頂点への吸着。番号は再利用しないので _previewHitVertex は -1。
                _previewHitVertex    = -1;
                _previewPoint        = ctx.ActiveWorldToLocal(_gpuHoverSnapWorld.Value);
                _previewSnappedOther = true;
            }
            else
            {
                _previewHitVertex    = -1;
                _previewPoint        = GetLocalPositionFromScreen(ctx, screenPos);
                _previewSnappedOther = false;
            }

            _previewValid = true;
        }

        /// <summary>
        /// 面を作成し、作成された頂点インデックスのリストを返す
        /// </summary>
        private List<int> CreateFace(ToolContext ctx)
        {
            var createdIndices = new List<int>();

            if (ctx.ActiveMeshObject == null || _points.Count < 2)
                return createdIndices;

            var meshObject = ctx.ActiveMeshObject;
            var newVertexIndices = new List<int>();
            var addedVertices = new List<(int Index, Vertex Vertex)>();

            // ============================================================
            // 【ウェイト継承ルール】
            // ============================================================
            // スキンドメッシュに追加する頂点には BoneWeight が必須である。
            // BoneWeight を持たない頂点は GPU 側でメッシュ自身の context 索引を使い
            // （UnifiedBufferManager_Build.cs:356-362）、メッシュの SkinningMatrix で
            // 変換される（UnifiedBufferManager_Update.cs:1513-1515）。周囲の頂点は
            // ボーンの SkinningMatrix なので、その頂点だけ別の場所に置かれる。
            // ナイフ等がその頂点を含む辺を扱うと計算が狂う。
            //
            // 継承元の決め方は 2 通り。
            //
            //   (A) 既存頂点が 1 つ以上選ばれている場合
            //       生成する多角形の環（_points の並び）に沿った段数が最小の
            //       既存頂点からコピーする。同数なら _points 中で先に現れる方。
            //       3D の直線距離ではなく、辺をたどった段数で決める。
            //
            //   (B) すべて新規点の場合
            //       メッシュ内の既存頂点のうち、ワールド空間で最も近いものから
            //       コピーする。座標は GPU 値（ctx.GetVertexWorldPosition）を使う。
            //
            // 併せて座標の基準も継承元に揃える。point.Position は
            // GetLocalPositionFromScreen が ActiveWorldToLocal（メッシュの
            // WorldMatrixInverse）で作った値なので、BoneWeight を与えるだけでは
            // 格納した座標の基準と実際に適用される行列がずれる。
            // ActiveWorldMatrix で一度ワールドへ戻し、継承元の行列の逆でローカルへ入れ直す。
            //
            // 継承元の BoneWeight が null（メッシュがスキンドでない）の場合は
            // BoneWeight を設定せず座標変換も行わない。従来と同じ挙動になる。
            // ============================================================

            int pointCount = _points.Count;
            int originalVertexCount = meshObject.Vertices.Count;

            // ループ中に _points を書き換えるため、入口の状態を控える。
            var wasExisting = new bool[pointCount];
            var existingIdx = new int[pointCount];
            bool anyExisting = false;
            for (int i = 0; i < pointCount; i++)
            {
                wasExisting[i] = _points[i].IsExistingVertex;
                existingIdx[i] = _points[i].ExistingVertexIndex;
                if (wasExisting[i]) anyExisting = true;
            }

            // 各点について、既存頂点を使用するか新規作成
            for (int i = 0; i < pointCount; i++)
            {
                var point = _points[i];
                if (point.IsExistingVertex)
                {
                    newVertexIndices.Add(point.ExistingVertexIndex);
                    createdIndices.Add(point.ExistingVertexIndex);
                }
                else
                {
                    int srcVertex = anyExisting
                        ? FindSourceByRingDistance(wasExisting, existingIdx, i)
                        : FindSourceByWorldDistance(ctx, meshObject, originalVertexCount, point.Position);

                    Vector3 localPos = RebasePositionToSource(ctx, meshObject, srcVertex, point.Position);

                    // 新規頂点を作成
                    var vertex = new Vertex(localPos);
                    vertex.UVs.Add(Vector2.zero);  // デフォルトUV
                    vertex.Normals.Add(Vector3.up);  // 仮の法線（後で計算）

                    if (srcVertex >= 0 && srcVertex < meshObject.Vertices.Count)
                        vertex.BoneWeight = meshObject.Vertices[srcVertex].BoneWeight;

                    int newIndex = meshObject.Vertices.Count;
                    meshObject.Vertices.Add(vertex);
                    newVertexIndices.Add(newIndex);
                    addedVertices.Add((newIndex, vertex));
                    createdIndices.Add(newIndex);

                    // _pointsの情報も更新（次回使用時のため）
                    _points[i] = PointInfo.FromExisting(newIndex, localPos);
                }
            }

            // 今回増えた頂点を 1 つの部品として扱う。
            // 部品IDは既存の最大値 + 1、サブIDはその中で 0 から。既存頂点は書き換えない。
            Poly_Ling.Ops.PartsIdOps.AssignNewVertices(meshObject, originalVertexCount);

            // 面を作成
            Face newFace = null;
            switch (Mode)
            {
                case AddFaceMode.Line:
                    // 2点の補助線を作成（2頂点のFace）
                    if (newVertexIndices.Count >= 2)
                    {
                        newFace = new Face();
                        newFace.VertexIndices.Add(newVertexIndices[0]);
                        newFace.VertexIndices.Add(newVertexIndices[1]);
                        newFace.UVIndices.Add(0);
                        newFace.UVIndices.Add(0);
                        newFace.NormalIndices.Add(0);
                        newFace.NormalIndices.Add(0);
                        newFace.MaterialIndex = ctx.CurrentMaterialIndex;
                    }
                    break;

                case AddFaceMode.Triangle:
                    if (newVertexIndices.Count >= 3)
                    {
                        newFace = new Face(
                            newVertexIndices[0],
                            newVertexIndices[1],
                            newVertexIndices[2],
                            ctx.CurrentMaterialIndex);
                    }
                    break;

                case AddFaceMode.Quad:
                    if (newVertexIndices.Count >= 4)
                    {
                        newFace = new Face(
                            newVertexIndices[0],
                            newVertexIndices[1],
                            newVertexIndices[2],
                            newVertexIndices[3],
                            ctx.CurrentMaterialIndex);
                    }
                    else if (newVertexIndices.Count == 3)
                    {
                        // 3点で確定した場合（4点目に1点目をクリック／右クリック／Escape）は三角形にする。
                        newFace = new Face(
                            newVertexIndices[0],
                            newVertexIndices[1],
                            newVertexIndices[2],
                            ctx.CurrentMaterialIndex);
                    }
                    break;
            }

            if (newFace != null)
            {
                // 3頂点以上の場合は法線を計算
                if (newFace.VertexCount >= 3)
                {
                    // 面法線がカメラ（クリック時の視点）と逆向きなら巻き順を反転し、
                    // 常に表面がカメラを向くようにする。
                    // 法線と描画三角形の巻き順は共に VertexIndices 順から算出されるため、
                    // VertexIndices を反転すると両者が同時に反転して整合が保たれる。
                    //
                    // 判定はワールド空間で行う。カメラ位置をローカルへ変換する方式は
                    // 逆行列の選択を誤りやすく、スキンド化後に表裏が反転していた。
                    // 既存頂点のワールド座標は GPU が計算済みのものを
                    // ctx.GetVertexWorldPosition 経由で受け取る。CPU で計算し直さない。
                    Vector3 faceCenterWorld = Vector3.zero;
                    for (int vi = 0; vi < newFace.VertexIndices.Count; vi++)
                        faceCenterWorld += GetVertexWorld(
                            ctx, meshObject, addedVertices, newFace.VertexIndices[vi]);
                    faceCenterWorld /= newFace.VertexIndices.Count;

                    Vector3 faceNormalWorld = CalculateFaceNormalWorld(
                        ctx, meshObject, addedVertices, newFace);

                    if (Vector3.Dot(faceNormalWorld, ctx.CameraPosition - faceCenterWorld) < 0f)
                    {
                        newFace.VertexIndices.Reverse();
                        newFace.UVIndices.Reverse();
                        newFace.NormalIndices.Reverse();
                    }

                    // Vertex.Normals はローカル空間に格納する（GPU 側で変換される）。
                    Vector3 faceNormal = CalculateFaceNormal(meshObject, newFace);

                    // 【重要】既存頂点の Normals[0] を書き換えてはならない。
                    // Face のコンストラクタは NormalIndices を全て 0 で埋めるため
                    // （MeshObject.cs:329-346）、その頂点を共有する既存の面もすべて
                    // Normals[0] を参照する。上書きすると周囲の面の陰影が壊れる。
                    // この呼び出しで新規作成した頂点にだけ法線を設定する。
                    foreach (int vi in newFace.VertexIndices)
                    {
                        bool isNewlyAdded = false;
                        for (int ai = 0; ai < addedVertices.Count; ai++)
                            if (addedVertices[ai].Index == vi) { isNewlyAdded = true; break; }
                        if (!isNewlyAdded) continue;

                        var vertex = meshObject.Vertices[vi];
                        if (vertex.Normals.Count == 0)
                        {
                            vertex.Normals.Add(faceNormal);
                        }
                        else
                        {
                            vertex.Normals[0] = faceNormal;
                        }
                    }
                }

                int faceIndex = meshObject.Faces.Count;
                meshObject.Faces.Add(newFace);

                Debug.Log($"[AddFaceTool] Created {Mode}: VertexCount={newFace.VertexCount}, MaterialIndex={newFace.MaterialIndex}");

                // メッシュを更新
                ctx.SyncMesh?.Invoke();

                // Undo記録
                if (ctx.UndoController != null)
                {
                    ctx.UndoController.RecordAddFaceOperation(newFace, faceIndex, addedVertices);
                }
            }

            ctx.Repaint?.Invoke();

            return createdIndices;
        }

        /// <summary>
        /// 面の法線を計算
        /// </summary>
        /// <summary>
        /// 規則 (A)。生成する多角形の環に沿った段数が最小の既存頂点を返す。
        /// 段数は min(|i-j|, n-|i-j|)。同数のときは _points 中で先に現れる方を選ぶ。
        /// 既存頂点が 1 つも無い場合は -1。
        /// </summary>
        private static int FindSourceByRingDistance(bool[] wasExisting, int[] existingIdx, int pointIndex)
        {
            int n = wasExisting.Length;
            if (n == 0) return -1;

            int best = -1;
            int bestStep = int.MaxValue;

            for (int j = 0; j < n; j++)
            {
                if (!wasExisting[j]) continue;

                int diff = Mathf.Abs(pointIndex - j);
                int step = Mathf.Min(diff, n - diff);

                if (step < bestStep)
                {
                    bestStep = step;
                    best = existingIdx[j];
                }
            }

            return best;
        }

        /// <summary>
        /// 規則 (B)。メッシュ内の既存頂点のうちワールド空間で最も近いものを返す。
        /// 比較対象は今回の呼び出しで追加する前の頂点のみ（originalVertexCount 未満）。
        /// 座標は GPU が計算した値（ctx.GetVertexWorldPosition）を優先する。
        /// 頂点が 1 つも無い場合は -1。
        /// </summary>
        private static int FindSourceByWorldDistance(
            ToolContext ctx, MeshObject meshObject, int originalVertexCount, Vector3 newLocalPos)
        {
            if (originalVertexCount <= 0) return -1;

            Matrix4x4 meshMat = ctx.ActiveWorldMatrix;
            Vector3 targetWorld = meshMat.MultiplyPoint3x4(newLocalPos);

            int best = -1;
            float bestSqr = float.MaxValue;

            for (int vi = 0; vi < originalVertexCount && vi < meshObject.Vertices.Count; vi++)
            {
                Vector3 w;
                var gpu = ctx.GetVertexWorldPosition?.Invoke(vi);
                if (gpu.HasValue) w = gpu.Value;
                else               w = meshMat.MultiplyPoint3x4(meshObject.Vertices[vi].Position);

                float sqr = (w - targetWorld).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = vi; }
            }

            return best;
        }

        /// <summary>
        /// point.Position（メッシュの WorldMatrix 基準のローカル座標）を、
        /// 継承元頂点と同じ基準のローカル座標へ入れ直す。
        /// ActiveWorldMatrix で一度ワールドへ戻し、継承元の行列の逆で戻す。
        /// 継承元が無い、または継承元が BoneWeight を持たない場合は変換しない。
        /// </summary>
        private static Vector3 RebasePositionToSource(
            ToolContext ctx, MeshObject meshObject, int srcVertex, Vector3 localPos)
        {
            if (srcVertex < 0 || srcVertex >= meshObject.Vertices.Count) return localPos;

            var srcVtx = meshObject.Vertices[srcVertex];
            if (srcVtx == null || !srcVtx.HasBoneWeight) return localPos;

            Vector3 world = ctx.ActiveWorldMatrix.MultiplyPoint3x4(localPos);
            return ctx.ActiveVertexMatrix(srcVertex).inverse.MultiplyPoint3x4(world);
        }

        /// <summary>
        /// 面の頂点のワールド座標を返す。
        /// 既存頂点は GPU が計算済みの値（ctx.GetVertexWorldPosition）を使う。
        /// この呼び出しで新規作成した頂点は GPU バッファにまだ存在しないため、
        /// ActiveWorldMatrix を掛ける。新規点の Position は
        /// GetLocalPositionFromScreen 内の ActiveWorldToLocal で作られており
        /// （AddFaceTool.cs の該当箇所）、この往復は閉じている。
        /// </summary>
        private static Vector3 GetVertexWorld(
            ToolContext ctx,
            MeshObject meshObject,
            List<(int Index, Vertex Vertex)> addedVertices,
            int vertexIndex)
        {
            if (vertexIndex < 0 || vertexIndex >= meshObject.Vertices.Count)
                return Vector3.zero;

            Vector3 local = meshObject.Vertices[vertexIndex].Position;

            bool isNewlyAdded = false;
            for (int ai = 0; ai < addedVertices.Count; ai++)
                if (addedVertices[ai].Index == vertexIndex) { isNewlyAdded = true; break; }

            if (!isNewlyAdded && ctx.GetVertexWorldPosition != null)
            {
                var w = ctx.GetVertexWorldPosition(vertexIndex);
                if (w.HasValue) return w.Value;
            }

            return ctx.ActiveWorldMatrix.MultiplyPoint3x4(local);
        }

        /// <summary>面法線をワールド空間で算出する（巻き順の規約は CalculateFaceNormal と同一）。</summary>
        private static Vector3 CalculateFaceNormalWorld(
            ToolContext ctx,
            MeshObject meshObject,
            List<(int Index, Vertex Vertex)> addedVertices,
            Face face)
        {
            if (face.VertexCount < 3)
                return Vector3.up;

            Vector3 p0 = GetVertexWorld(ctx, meshObject, addedVertices, face.VertexIndices[0]);
            Vector3 p1 = GetVertexWorld(ctx, meshObject, addedVertices, face.VertexIndices[1]);
            Vector3 p2 = GetVertexWorld(ctx, meshObject, addedVertices, face.VertexIndices[2]);

            return NormalHelper.CalculateFaceNormal(p0, p1, p2);
        }

        private Vector3 CalculateFaceNormal(MeshObject meshObject, Face face)
        {
            if (face.VertexCount < 3)
                return Vector3.up;

            Vector3 p0 = meshObject.Vertices[face.VertexIndices[0]].Position;
            Vector3 p1 = meshObject.Vertices[face.VertexIndices[1]].Position;
            Vector3 p2 = meshObject.Vertices[face.VertexIndices[2]].Position;

            return NormalHelper.CalculateFaceNormal(p0, p1, p2);
        }

        // === 公開メソッド ===

        /// <summary>
        /// 現在の点をクリア
        /// </summary>
        public void ClearPoints()
        {
            _points.Clear();
            _previewValid = false;
        }

        /// <summary>
        /// 点の数
        /// </summary>
        public int PointCount => _points.Count;
    }
}