// Tools/TopologyTools/Modify/KnifeTool_/KnifeTool.SimpleCut.cs
// シンプルナイフ（KnifeMode.SimpleCut）。既存ラダー系ロジックとは独立。
// 画面上の左クリック2回で自由な2点 P0,P1（IMGUI Y下）を指定し、直線で切断。
// SimpleDragMode が ON のときは、押した位置を P0、離した位置を P1 とするドラッグ式。
// 端点は既存頂点にスナップしない。非カリング面のみ切る（マスクはハンドラが注入）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Tools
{
    public partial class KnifeTool
    {
        private enum SimpleStage { Idle, HasP0 }
        private SimpleStage _simpleStage = SimpleStage.Idle;

        // 1点目（IMGUI Y下スクリーン座標）。
        private Vector2 _simpleP0;

        // 1点目を頂点で指定したときの頂点番号（自由点は -1）。
        private int _simpleV0 = -1;

        /// <summary>
        /// SimpleVertexEndpoints が ON で頂点ホバーがあれば、その頂点番号を返し、
        /// pos をその頂点の画面位置（SimpleCutExecutor の投影と同一）に置き換える。
        /// それ以外は -1 を返し pos は変えない。
        /// </summary>
        private int SnapSimplePoint(ToolContext ctx, MeshObject mo, ref Vector2 pos)
        {
            if (!SimpleVertexEndpoints || ctx == null || mo == null) return -1;
            int v = ResolveVertex(ctx, pos);
            if (v < 0 || v >= mo.VertexCount) return -1;
            pos = SimpleCutExecutor.VertexScreenPos(ctx, mo, v);
            return v;
        }

        // 実行直前に Player ハンドラが注入する面カリングマスク（true=切らない）。null で全面対象。
        private bool[] _simpleFaceCulledMask;

        /// <summary>1点目が確定済みか（次クリックで実行）。マスク注入判定に使用。</summary>
        public bool SimpleCutHasFirstPoint => _simpleStage == SimpleStage.HasP0;

        /// <summary>面カリングマスクを設定する（Player ハンドラが実行直前に呼ぶ。true=切らない）。</summary>
        public void SetFaceCulledMask(bool[] mask) => _simpleFaceCulledMask = mask;

        // ================================================================
        // クリック
        // ================================================================

        /// <summary>
        /// SimpleCut のクリック入口。screenPoint は「頂点投影と同じ座標系
        /// (Y=0 下・原点左下)」で渡すこと。Player の生クリック座標(ToViewportCoord 済み)は
        /// 既にこの系なので、ToImgui を通さずそのまま渡す。
        /// </summary>
        public bool OnSimpleCutClickScreen(ToolContext ctx, Vector2 screenPoint)
        {
            // 操作対象は描画メッシュ(FirstDrawable)。ActiveCategory に依存する FirstSelected は
            // ナイフ使用中に null になり面が0枚になるため使わない。
            var mo = ctx?.ActiveMeshObject;
            if (ctx == null || mo == null) return false;
            return HandleSimpleCutClick(ctx, mo, screenPoint);
        }

        /// <summary>
        /// SimpleCut のホバー入口。screenPoint はクリック入口（OnSimpleCutClickScreen）と
        /// 同じ座標系で渡すこと。P0 もこの系で持っているので、ToImgui を通すと
        /// カーソル側だけ上下が反転する。
        /// </summary>
        public void OnSimpleCutHoverScreen(ToolContext ctx, Vector2 screenPoint)
        {
            var mo = ctx?.ActiveMeshObject;
            if (ctx == null || mo == null) return;
            UpdateSimpleCutHover(ctx, mo, screenPoint);
            ctx.Repaint?.Invoke();
        }

        // ================================================================
        // ドラッグ式（SimpleDragMode）
        //   押下で 1 点目（HandleSimpleCutClick の Idle 段と同じ）、離した位置で 2 点目。
        //   押下位置から SimpleDragMinPixels 未満で離したら切らずに取り消す。
        //   Player は押下・ドラッグ終了・クリックをハンドラ側で振り分けるので、
        //   ここの離し判定を使うのは Editor の OnMouseDown / OnMouseUp 経路。
        // ================================================================

        /// <summary>ドラッグ式で、離したときに切るとみなす押下位置からの最小距離（画素）。</summary>
        private const float SimpleDragMinPixels = 3f;

        // ドラッグ式の押下位置（頂点へ吸着する前の生の位置）。
        private Vector2 _simpleDragOrigin;

        /// <summary>ドラッグ式で 1 点目を置いたまま離されるのを待っているか。</summary>
        public bool SimpleDragPending => SimpleDragMode && _simpleStage == SimpleStage.HasP0;

        /// <summary>シンプル切断の途中の段を取り消す（ドラッグ式で動かさずに離したとき）。</summary>
        public void CancelSimpleCut() => ResetSimpleCut();

        /// <summary>Editor 経路の離し。押下位置から動いていれば 2 点目として切り、動いていなければ取り消す。</summary>
        private bool HandleSimpleCutRelease(ToolContext ctx, MeshObject mo, Vector2 mousePos)
        {
            if (_simpleStage != SimpleStage.HasP0) return false;
            if ((mousePos - _simpleDragOrigin).magnitude < SimpleDragMinPixels)
            {
                ResetSimpleCut();
                ctx.Repaint?.Invoke();
                return false;
            }
            return HandleSimpleCutClick(ctx, mo, mousePos);
        }

        private bool HandleSimpleCutClick(ToolContext ctx, MeshObject mo, Vector2 mousePos)
        {
            switch (_simpleStage)
            {
                case SimpleStage.Idle:
                    _simpleDragOrigin = mousePos;
                    _simpleV0 = SnapSimplePoint(ctx, mo, ref mousePos);
                    _simpleP0 = mousePos;
                    _simpleStage = SimpleStage.HasP0;
                    LastError = "";
                    ctx.Repaint?.Invoke();
                    return true;

                case SimpleStage.HasP0:
                    SnapSimplePoint(ctx, mo, ref mousePos);
                    SimpleCutExecutor.Execute(ctx, mo, _simpleP0, mousePos, _simpleFaceCulledMask, SimpleTriQuad);
                    ctx.NotifyTopologyChanged?.Invoke();
                    Reset();
                    ctx.Repaint?.Invoke();
                    return true;
            }
            return false;
        }

        // ================================================================
        // ホバープレビュー（画面座標で直線を表示）
        // ================================================================

        // デバッグ用: 交差辺ハイライトの再利用バッファ
        private readonly List<(Vector2, Vector2)> _crossSegs = new List<(Vector2, Vector2)>();
        private readonly List<Vector2> _crossPts = new List<Vector2>();

        private void UpdateSimpleCutHover(ToolContext ctx, MeshObject mo, Vector2 mousePos)
        {
            _preview.Clear();

            // 頂点指定 ON：ホバー中の頂点に吸着した位置へカーソル側を置き換え、点を打つ。
            if (SnapSimplePoint(ctx, mo, ref mousePos) >= 0)
                _preview.ScreenDots.Add(mousePos);

            if (_simpleStage != SimpleStage.HasP0) return;

            // 切断線 P0→カーソル
            _preview.ScreenDots.Add(_simpleP0);
            _preview.ScreenLines.Add((_simpleP0, mousePos));

            // デバッグ: この切断線が交差する辺（線分）と交差点をハイライト。
            // カリング/2辺判定は掛けず、幾何的な交差をそのまま表示（検出の可視化）。
            // 操作対象は描画メッシュ(FirstDrawable)。FirstSelected は null になり得る。
            var dm = ctx?.ActiveMeshObject ?? mo;
            _crossSegs.Clear();
            _crossPts.Clear();
            SimpleCutExecutor.CollectCrossedEdges(ctx, dm, _simpleP0, mousePos, _simpleFaceCulledMask, _crossSegs, _crossPts);
            for (int i = 0; i < _crossSegs.Count; i++) _preview.ScreenLines.Add(_crossSegs[i]);
            for (int i = 0; i < _crossPts.Count; i++)  _preview.ScreenDots.Add(_crossPts[i]);
        }

        // ================================================================
        // リセット（KnifeTool.Reset から呼ばれる）
        // ================================================================

        private void ResetSimpleCut()
        {
            _simpleStage = SimpleStage.Idle;
            _simpleP0 = default;
            _simpleV0 = -1;
            _simpleFaceCulledMask = null;
        }
    }
}
