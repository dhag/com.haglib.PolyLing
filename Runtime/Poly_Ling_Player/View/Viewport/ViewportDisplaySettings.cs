// ViewportDisplaySettings.cs
// ビューポート単位の描画表示設定。
// Runtime/Poly_Ling_Player/View/Viewport/ に配置

namespace Poly_Ling.Player
{
    /// <summary>
    /// ビューポート1面分の描画表示設定。
    /// PlayerViewportManager が4面分（スロット0〜3）を配列で保持し、
    /// DrawViewport() 内で描画前に MeshSceneRenderer の各フラグに適用する。
    ///
    /// スロット番号は PlayerViewportManager の定数と対応する:
    ///   0 = Perspective、1 = Top、2 = Front、3 = Side
    /// </summary>
    public struct ViewportDisplaySettings
    {
        public bool BackfaceCulling;
        public bool ShowSelectedMesh;
        public bool ShowSelectedWireframe;
        public bool ShowSelectedVertices;
        public bool ShowSelectedBone;
        public bool ShowUnselectedMesh;
        public bool ShowUnselectedWireframe;
        public bool ShowUnselectedVertices;
        public bool ShowUnselectedBone;
        public bool ShowSelectedMirror;
        public bool ShowUnselectedMirror;

        /// <summary>
        /// MeshSceneRenderer のデフォルト値と一致するデフォルト設定を返す。
        /// </summary>
        public static ViewportDisplaySettings Default => new ViewportDisplaySettings
        {
            BackfaceCulling         = true,
            ShowSelectedMesh        = true,
            ShowSelectedWireframe   = true,
            ShowSelectedVertices    = true,
            ShowSelectedBone        = true,
            ShowUnselectedMesh      = true,
            ShowUnselectedWireframe = true,
            ShowUnselectedVertices  = true,
            ShowUnselectedBone      = false,
            ShowSelectedMirror      = true,
            ShowUnselectedMirror    = true,
        };

        // ── 永続化（RecentPaths に int ビットマスク文字列で保存する） ──────────
        public int ToBits()
        {
            int b = 0;
            if (BackfaceCulling)         b |= 1 << 0;
            if (ShowSelectedMesh)        b |= 1 << 1;
            if (ShowSelectedWireframe)   b |= 1 << 2;
            if (ShowSelectedVertices)    b |= 1 << 3;
            if (ShowSelectedBone)        b |= 1 << 4;
            if (ShowUnselectedMesh)      b |= 1 << 5;
            if (ShowUnselectedWireframe) b |= 1 << 6;
            if (ShowUnselectedVertices)  b |= 1 << 7;
            if (ShowUnselectedBone)      b |= 1 << 8;
            if (ShowSelectedMirror)      b |= 1 << 9;
            if (ShowUnselectedMirror)    b |= 1 << 10;
            return b;
        }

        public static ViewportDisplaySettings FromBits(int b) => new ViewportDisplaySettings
        {
            BackfaceCulling         = (b & (1 << 0)) != 0,
            ShowSelectedMesh        = (b & (1 << 1)) != 0,
            ShowSelectedWireframe   = (b & (1 << 2)) != 0,
            ShowSelectedVertices    = (b & (1 << 3)) != 0,
            ShowSelectedBone        = (b & (1 << 4)) != 0,
            ShowUnselectedMesh      = (b & (1 << 5)) != 0,
            ShowUnselectedWireframe = (b & (1 << 6)) != 0,
            ShowUnselectedVertices  = (b & (1 << 7)) != 0,
            ShowUnselectedBone      = (b & (1 << 8)) != 0,
            ShowSelectedMirror      = (b & (1 << 9)) != 0,
            ShowUnselectedMirror    = (b & (1 << 10)) != 0,
        };
    }
}
