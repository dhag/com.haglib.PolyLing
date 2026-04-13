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
        };
    }
}
