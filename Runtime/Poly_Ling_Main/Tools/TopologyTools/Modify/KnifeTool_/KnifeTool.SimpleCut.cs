// Tools/TopologyTools/Modify/KnifeTool_/KnifeTool.SimpleCut.cs
// シンプルナイフ（KnifeMode.SimpleCut）。既存ラダー系ロジックとは独立。
// 画面上の左クリック2回で自由な2点 P0,P1（IMGUI Y下）を指定し、直線で切断。
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

        private bool HandleSimpleCutClick(ToolContext ctx, MeshObject mo, Vector2 mousePos)
        {
            switch (_simpleStage)
            {
                case SimpleStage.Idle:
                    _simpleP0 = mousePos;
                    _simpleStage = SimpleStage.HasP0;
                    LastError = "";
                    ctx.Repaint?.Invoke();
                    return true;

                case SimpleStage.HasP0:
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
            _simpleFaceCulledMask = null;
        }
    }
}
