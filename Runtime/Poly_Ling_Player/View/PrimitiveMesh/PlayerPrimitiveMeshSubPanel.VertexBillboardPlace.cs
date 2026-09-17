// PlayerPrimitiveMeshSubPanel.VertexBillboardPlace.cs
// 図形生成サブパネル：頂点へ藤壺（高度な図形）。
//
// 【形の決まり方】
//   選択中の描画オブジェクトそれぞれの選択頂点の位置へ、配置元オブジェクトを複製する。
//   向きは直前にポインタが乗ったビューポートのカメラに向けたビルボード。
//   配置元の +Z（とげ）をカメラ側へ向けるか、画面の上へ向けるかを選ぶ。
//   位置は GPU のワールド座標から読むので、生成は PrimitiveMeshFactory ではなく
//   VertexBillboardPlaceToolHandler が行う。プレビューと実生成は同じ実装を通る。
//
// 【置き方】
//   組んだメッシュはワールド座標。追加先モードと材質スロットは他の図形と同じ。
//   姿勢（位置・回転・拡大）は持たない。
//
// 【カメラ】
//   生成時のカメラの向きをコマンドへ載せる。オブジェクトグループの作り直しでもこの値を使う。
//   プレビューはカメラ変更の通知（Viewer のオーバーレイ更新）で作り直す。毎フレームは見ない。
//
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.PlaceObject;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        /// <summary>選択中の描画オブジェクトが持つ選択頂点の合計数。</summary>
        public Func<int> GetSelectedVertexCount;

        /// <summary>
        /// 直前にポインタが乗ったビューポートのカメラの [視線, 上方向]（ワールド）。取れなければ null。
        /// </summary>
        public Func<Vector3[]> GetActiveCameraFrame;

        /// <summary>プレビュー用のメッシュを組む。選択中の頂点から組む。組めないときは null。</summary>
        public Func<CreateVertexBillboardPlaceCommand, MeshObject> BuildVertexBillboardPlaceMesh;

        // ================================================================
        // 状態
        // ================================================================

        private string              _vbpMeshName        = "VertexBillboardPlace";
        private MeshSourceMultiPick _vbpSrcPick         = new MeshSourceMultiPick();
        private bool                _vbpIncludeChildren;
        private PlaceSourceMode     _vbpMode            = PlaceSourceMode.Combine;
        private int                 _vbpSeed;
        private float               _vbpScale           = 1f;
        private BillboardZDirection _vbpZDirection      = BillboardZDirection.TowardCamera;

        /// <summary>直近のプレビューを作ったときのカメラの向き。</summary>
        private Vector3 _vbpPreviewForward;
        private Vector3 _vbpPreviewUp;
        private bool    _vbpHasPreviewCamera;

        private const float VbpScaleMin = 0.01f;
        private const float VbpScaleMax = 10f;
        private const int   VbpSeedMin  = 0;
        private const int   VbpSeedMax  = 9999;

        [UiControl("vertexBillboardPlace.info", Safety = UiSafety.ReadOnly, Description = "頂点へ藤壺の情報")]
        private Label _vbpInfo;

        // ================================================================
        // UI
        // ================================================================

        private void BuildVertexBillboardPlaceUI(VisualElement c)
        {
            if (c == null) return;

            c.Add(ShapeTitle(T("VertexBillboardPlace")));
            c.Add(GearHint(T("VertexBillboardPlaceHint")));
            c.Add(NF(() => _vbpMeshName, v => _vbpMeshName = v));

            // ── 配置元オブジェクト（複数選択可） ──
            BuildMeshSourceMultiRow(c, _vbpSrcPick, T("PlaceSource"));

            c.Add(TR(T("PlaceIncludeChildren"),
                () => _vbpIncludeChildren,
                v => { _vbpIncludeChildren = v; D(); }));

            // ── 割り当て方式 ──
            c.Add(DD(T("PlaceMode"),
                new List<string> { T("VertexBillboardModeCombine"), T("VertexBillboardModeSequence"), T("VertexBillboardModeRandom") },
                () => (int)_vbpMode,
                i => { _vbpMode = (PlaceSourceMode)i; D(); }));

            c.Add(IR(T("PlaceSeed"), VbpSeedMin, VbpSeedMax,
                () => _vbpSeed,
                v => { _vbpSeed = v; D(); }));

            // ── 倍率 ──
            c.Add(SR(T("VertexBillboardScale"), VbpScaleMin, VbpScaleMax,
                () => _vbpScale,
                v => { _vbpScale = v; D(); }));

            // ── とげ（+Z）の向き ──
            c.Add(DD(T("VertexBillboardZDirection"),
                new List<string> { T("VertexBillboardZToward"), T("VertexBillboardZUp") },
                () => (int)_vbpZDirection,
                i => { _vbpZDirection = (BillboardZDirection)i; D(); }));

            c.Add(GearHint(T("VertexBillboardCameraHint")));

            _vbpInfo = SL("");
            c.Add(_vbpInfo);

            RefreshVertexBillboardPlaceInfo();
        }

        /// <summary>選択頂点の数と配置元の数の表示を引き直す。</summary>
        private void RefreshVertexBillboardPlaceInfo()
        {
            if (_vbpInfo == null) return;
            _vbpInfo.text = T("VertexBillboardSelected",
                VertexBillboardSelectedVertices, _vbpSrcPick.SelectedMasterIndices().Count);
        }

        /// <summary>選択頂点の数。結線が無いときは 0。</summary>
        private int VertexBillboardSelectedVertices => GetSelectedVertexCount?.Invoke() ?? 0;

        /// <summary>今の設定で生成ボタンを押せるか。</summary>
        private bool VertexBillboardPlaceReady
            => SendCommand != null
            && VertexBillboardSelectedVertices > 0
            && _vbpSrcPick.SelectedMasterIndices().Count > 0;

        /// <summary>
        /// カメラ変更の通知から呼ばれる。プレビューを作ったときとカメラの向きが違えば作り直す。
        /// </summary>
        public void NotifyVertexBillboardCameraChanged()
        {
            if (_current != ShapeKind.VertexBillboardPlace) return;

            var frame = GetActiveCameraFrame?.Invoke();
            if (frame == null || frame.Length < 2) return;

            if (_vbpHasPreviewCamera
                && Vector3.Dot(frame[0], _vbpPreviewForward) >= 0.9999999f
                && Vector3.Dot(frame[1], _vbpPreviewUp)      >= 0.9999999f)
                return;

            _dirty = true;
            RefreshVertexBillboardPlaceInfo();
            RefreshCreateButtonState();
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>今のパネルの状態とカメラからコマンドを組む。カメラが取れなければ null。</summary>
        private CreateVertexBillboardPlaceCommand BuildVertexBillboardPlaceCommand(int[] targets)
        {
            var frame = GetActiveCameraFrame?.Invoke();
            if (frame == null || frame.Length < 2) return null;

            // 頂点の位置は既にワールド座標なので、姿勢は入れない。
            var pl = CurrentPlacement();
            pl.WorldPosition = Vector3.zero;
            pl.PlaceRotation = Vector3.zero;
            pl.PlaceScale    = Vector3.one;

            return new CreateVertexBillboardPlaceCommand(
                ModelIndex(), targets ?? Array.Empty<int>(), "",
                _vbpSrcPick.SelectedMasterIndices().ToArray(),
                _vbpIncludeChildren, _vbpMode, _vbpSeed, _vbpScale, _vbpZDirection,
                frame[0], frame[1], _vbpMeshName, pl);
        }

        /// <summary>プレビュー・ライブワイヤで使うメッシュ。実生成と同じハンドラを通す。</summary>
        private MeshObject GenerateVertexBillboardPlaceMesh()
        {
            int[] targets = GetSelectedDrawableIndices?.Invoke() ?? Array.Empty<int>();
            var cmd = BuildVertexBillboardPlaceCommand(targets);
            if (cmd == null) return null;

            _vbpPreviewForward   = cmd.ViewDirection;
            _vbpPreviewUp        = cmd.ViewUp;
            _vbpHasPreviewCamera = true;

            return BuildVertexBillboardPlaceMesh?.Invoke(cmd);
        }

        /// <summary>生成ボタンから呼ぶ。</summary>
        private void InvokeVertexBillboardPlaceGenerate()
        {
            if (SendCommand == null)
            {
                if (_statusLabel != null) _statusLabel.text = "配線が足りません（SendCommand）";
                return;
            }

            int[] targets = GetSelectedDrawableIndices?.Invoke() ?? Array.Empty<int>();
            if (targets.Length == 0)
            {
                if (_statusLabel != null) _statusLabel.text = T("EdgeRibbonFaceNoTarget");
                return;
            }

            var cmd = BuildVertexBillboardPlaceCommand(targets);
            if (cmd == null)
            {
                if (_statusLabel != null) _statusLabel.text = T("VertexBillboardNoCamera");
                return;
            }

            SendCommand(cmd);

            if (_addMode != PrimitiveAddMode.AddToExisting)
                RefreshMeshNameCandidate();

            RefreshVertexBillboardPlaceInfo();
        }
    }
}
