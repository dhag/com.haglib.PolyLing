// PlayerPrimitiveMeshSubPanel.EdgeRibbonFace.cs
// 図形生成サブパネル：辺から帯面（高度な図形）。
//
// 【形の決まり方】
//   選択中の描画オブジェクトそれぞれの選択辺を中心線にして、一定幅の帯面を組む。
//   パラメータだけでは形が決まらないので、生成は PrimitiveMeshFactory ではなく
//   EdgeRibbonFaceToolHandler が行う。プレビューと実生成は同じ実装を通る。
//
// 【置き方】
//   組んだメッシュはワールド座標。追加先モードと材質スロットは他の図形と同じ。
//   姿勢（位置・回転・拡大）は持たない。座標が選択辺の既存頂点で決まるため。
//
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        /// <summary>選択中の描画オブジェクトの masterIndex 配列。</summary>
        public Func<int[]> GetSelectedDrawableIndices;

        /// <summary>選択中の描画オブジェクトが持つ選択辺の合計本数。</summary>
        public Func<int> GetSelectedEdgeCount;

        /// <summary>帯面のメッシュを組む。引数は帯の幅。組めないときは null。</summary>
        public Func<float, MeshObject> BuildEdgeRibbonFaceMesh;

        // ================================================================
        // 状態
        // ================================================================

        /// <summary>帯の幅（ワールド単位）。既定は EdgeRibbonFaceSettings と同じ。</summary>
        private float _ribbonFaceWidth = 0.05f;

        /// <summary>幅の入力範囲。ワールド単位なので上限は控えめに取る。</summary>
        private const float RibbonFaceWidthMin = 0.001f;
        private const float RibbonFaceWidthMax = 1.0f;

        [UiControl("edgeRibbonFace.info", Safety = UiSafety.ReadOnly, Description = "辺リボン面の情報")]
        private Label _ribbonFaceInfo;

        // ================================================================
        // UI
        // ================================================================

        private void BuildEdgeRibbonFaceUI(VisualElement c)
        {
            if (c == null) return;

            c.Add(ShapeTitle(T("EdgeRibbonFace")));
            c.Add(GearHint(T("EdgeRibbonFaceHint")));

            c.Add(SR(T("EdgeRibbonFaceWidth"),
                RibbonFaceWidthMin, RibbonFaceWidthMax,
                () => _ribbonFaceWidth,
                v => { _ribbonFaceWidth = v; D(); RefreshEdgeRibbonFaceInfo(); }));

            _ribbonFaceInfo = SL("");
            c.Add(_ribbonFaceInfo);

            RefreshEdgeRibbonFaceInfo();
        }

        /// <summary>選択辺の本数表示を引き直す。</summary>
        private void RefreshEdgeRibbonFaceInfo()
        {
            if (_ribbonFaceInfo == null) return;
            _ribbonFaceInfo.text = T("EdgeRibbonFaceSelected", EdgeRibbonFaceSelectedEdges);
        }

        /// <summary>選択辺の本数。結線が無いときは 0。</summary>
        private int EdgeRibbonFaceSelectedEdges => GetSelectedEdgeCount?.Invoke() ?? 0;

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// プレビュー・ライブワイヤ・生成前確認で使うメッシュ。
        /// 実生成と同じ EdgeRibbonFaceTool を通すので、見えているものがそのまま出る。
        /// </summary>
        private MeshObject GenerateEdgeRibbonFaceMesh()
            => BuildEdgeRibbonFaceMesh?.Invoke(_ribbonFaceWidth);

        /// <summary>生成ボタンから呼ぶ。置き方は他の図形と同じ配置経路へ載せる。</summary>
        private void InvokeEdgeRibbonFaceGenerate()
        {
            if (SendCommand == null)
            {
                if (_statusLabel != null)
                    _statusLabel.text = "配線が足りません（SendCommand）";
                return;
            }

            int[] targets = GetSelectedDrawableIndices?.Invoke() ?? Array.Empty<int>();
            if (targets.Length == 0)
            {
                if (_statusLabel != null) _statusLabel.text = T("EdgeRibbonFaceNoTarget");
                return;
            }

            // 頂点は既にワールド座標なので、姿勢は入れない。
            // 追加先モード・材質スロット・重複頂点の結合はそのまま使う。
            var pl = CurrentPlacement();
            pl.WorldPosition = Vector3.zero;
            pl.PlaceRotation = Vector3.zero;
            pl.PlaceScale    = Vector3.one;

            SendCommand(new EdgeRibbonFaceCommand(
                ModelIndex(), targets, _ribbonFaceWidth, pl));

            RefreshEdgeRibbonFaceInfo();
        }
    }
}
