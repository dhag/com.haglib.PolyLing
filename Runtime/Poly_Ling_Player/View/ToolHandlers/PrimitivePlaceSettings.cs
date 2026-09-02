// PrimitivePlaceSettings.cs
// 図形生成パネル（基本図形 / 高度な図形）の配置ギズモと姿勢仮表示の設定。
//
// 【誰が持つか】
//   PolyLingPlayerViewerCore が 1 個だけ生成し、PrimitivePlaceToolHandler と
//   PlayerPrimitiveMeshSubPanel（3D連携インスタンス）が同じ参照を共有する。
//   図形カテゴリ（基本 / 高度）はパネルの表示切替でしかないため、
//   1 個持てば両カテゴリで同じ値になる。
//
// 【ObjectMoveSettings とは別物】
//   「描画オブジェクトの姿勢」のギズモ表示は ObjectMoveSettings の
//   AllowMoveGizmo / AllowRotationGizmo が持つ。こちらとは独立で、
//   片方を変えてももう片方は変わらない。
//
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

namespace Poly_Ling.Player
{
    /// <summary>配置ギズモの表示と、生成予定姿勢の仮表示の設定。</summary>
    public sealed class PrimitivePlaceSettings
    {
        private bool _showMoveGizmo  = true;
        private bool _showScaleGizmo = false;

        /// <summary>
        /// 移動ギズモ（矢印）を表示するか。既定 true。
        ///
        /// 【拡大縮小と排他にしている理由】
        ///   PlayerViewportPanel.GizmoData は軸ギズモの座標を 1 組
        ///   （Origin / XEnd / YEnd / ZEnd）しか持たない。移動の矢印と
        ///   拡大縮小のキューブはその同じ 1 組を先端の形だけ変えて描くため、
        ///   2 つを同時に出すことが構造上できない。片方を ON にしたら
        ///   もう片方を OFF にする。回転リングは別スロット（RingX/Y/Z）なので
        ///   どちらとも同時に出せる。
        /// </summary>
        public bool ShowMoveGizmo
        {
            get => _showMoveGizmo;
            set
            {
                _showMoveGizmo = value;
                if (value) _showScaleGizmo = false;
            }
        }

        /// <summary>
        /// 拡大縮小ギズモ（キューブ）を表示するか。既定 false。移動ギズモと排他。
        ///
        /// 【UI からは操作できない】
        ///   チェックボックスは削除済みで、この値を true にする経路は今のところ無い。
        ///   ハンドラ側の分岐（PrimitivePlaceToolHandler.ArrowIsScale と
        ///   DragKind.Scale）は動くまま残してあるので、必要になったら
        ///   PlayerPrimitiveMeshSubPanel へチェックを1つ足すだけで戻せる。
        ///
        /// 【チェックを外したとき移動を戻さない理由】
        ///   ON にした側を正とする一方向の指定にしてある。両方 ON は
        ///   軸ギズモの座標が 1 組しか無いため描けないので、ここで潰す。
        ///   その代わり「拡大縮小を ON→OFF したら移動も OFF のまま」になるため、
        ///   UI からこの値を触れるようにするときは、移動チェックを
        ///   出し直す（＝利用者が戻せる）ことをセットにすること。
        /// </summary>
        public bool ShowScaleGizmo
        {
            get => _showScaleGizmo;
            set
            {
                _showScaleGizmo = value;
                if (value) _showMoveGizmo = false;
            }
        }

        /// <summary>回転ギズモ（リング）を表示するか。既定 false。</summary>
        public bool ShowRotationGizmo { get; set; } = false;

        /// <summary>生成予定位置に原点マーカー（水色ダイヤ）を仮表示するか。既定 false。</summary>
        public bool ShowOriginMarker { get; set; } = false;

        /// <summary>生成予定姿勢にくさびを仮表示するか。既定 false。</summary>
        public bool ShowWedge { get; set; } = false;
    }
}
