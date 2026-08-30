// ViewportDisplaySettings.cs
// ビューポート単位の描画表示設定。
// Runtime/Poly_Ling_Player/View/Viewport/ に配置

namespace Poly_Ling.Player
{
    /// <summary>
    /// ビューポート1面分の描画表示設定。
    /// PlayerViewportManager が4面分（スロット0〜3）を配列で保持し、
    /// PrepareViewport() 内で描画準備前に MeshSceneRenderer の各フラグに適用する。
    /// （旧記述は DrawViewport() を指していたが、同メソッドは呼出元 0 件のため
    ///   2026-08-28 に撤去した。）
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
        /// <summary>
        /// 非選択ミラーのマスタ。面・辺・頂点の 3 子を一括で落とすためだけに使う。
        ///
        /// 【描画に直接作用しない】 2026-08-28
        ///   以前はこのフラグがマスタと「ミラーの面」を兼ねており、
        ///   面だけ消すと辺・頂点まで巻き添えで消えていた。面を
        ///   ShowUnselectedMirrorMesh へ切り出し、こちらはマスタ専用にした。
        ///   レンダラ側からこのフラグを読まないこと。読むのは 3 子。
        ///
        /// 【ShowUnselectedMesh に従属しない】
        ///   実体側メッシュを隠してミラーだけ見る、ができるようにするため
        ///   従属を切った（独立化）。選択側（ShowSelectedMesh → ShowSelectedMirror）
        ///   は従来どおり従属したままで、非対称である点に注意。
        /// </summary>
        public bool ShowUnselectedMirror;
        /// <summary>非選択ミラーの面を表示するか。ShowUnselectedMirror に従属。既定 true。</summary>
        public bool ShowUnselectedMirrorMesh;
        /// <summary>非選択ミラーの辺を表示するか。ShowUnselectedMirror に従属。既定 true。</summary>
        public bool ShowUnselectedMirrorWireframe;
        /// <summary>非選択ミラーの頂点を表示するか。ShowUnselectedMirror に従属。既定 true。</summary>
        public bool ShowUnselectedMirrorVertices;
        public bool ShowSelectedMeshOrigin;
        public bool ShowUnselectedMeshOrigin;
        /// <summary>ミラー側の原点マーカーを描くか。既定 false。</summary>
        public bool ShowMirrorMeshOrigin;
        /// <summary>法線（頂点スロット単位）を描くか。選択メッシュのみ対象。既定 false。</summary>
        public bool ShowNormals;

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
            ShowUnselectedMirrorMesh      = true,
            ShowUnselectedMirrorWireframe = true,
            ShowUnselectedMirrorVertices  = true,
            ShowSelectedMeshOrigin   = true,
            ShowUnselectedMeshOrigin = true,
            ShowMirrorMeshOrigin     = false,
            ShowNormals              = false,
        };

        /// <summary>
        /// ミラー表示の従属関係を適用したコピーを返す（元の値は変更しない）。
        ///
        /// 従属関係:
        ///   ShowSelectedMesh → ShowSelectedMirror
        ///
        ///   ShowUnselectedMirror（独立。どこにも従属しない）
        ///     ├ ShowUnselectedMirrorMesh
        ///     ├ ShowUnselectedMirrorWireframe
        ///     └ ShowUnselectedMirrorVertices
        ///
        /// 【ShowUnselectedMesh → ShowUnselectedMirror の従属を撤廃した理由】 2026-08-28
        ///   実体側メッシュを隠してミラーだけ確認する、という使い方ができなかった。
        ///   非選択ミラーは独立した表示グループとして扱う。
        ///   選択側は UI トグルを持たず選択 Mesh に従属したままで、非対称である。
        ///   選択側にもトグルを足すなら、そのとき同じ形にそろえること。
        ///
        /// 表示不変条件「親が OFF のとき、子は ON にならない」を保証する。
        /// 従属の判定はここ 1 か所に集約すること。呼出側で個別に AND を書くと、
        /// 片方だけ直したときに UI とレンダラで解釈がずれる。
        /// </summary>
        public ViewportDisplaySettings WithMirrorClamped()
        {
            var s = this;
            if (!s.ShowSelectedMesh) s.ShowSelectedMirror = false;
            if (!s.ShowUnselectedMirror)
            {
                s.ShowUnselectedMirrorMesh      = false;
                s.ShowUnselectedMirrorWireframe = false;
                s.ShowUnselectedMirrorVertices  = false;
            }
            return s;
        }

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
            if (ShowSelectedMeshOrigin)   b |= 1 << 11;
            if (ShowUnselectedMeshOrigin) b |= 1 << 12;
            if (ShowMirrorMeshOrigin)     b |= 1 << 13;
            if (ShowNormals)              b |= 1 << 14;
            // 【ビット15/16 だけ反転して詰める理由】 2026-08-28
            //   この 2 つは既定が true。既存の保存データにはビットが無く 0 で読まれる。
            //   正のまま詰めると「既定は表示なのに復元すると非表示」になる。
            //   反転して「1 = 非表示」で持てば、旧データの 0 が表示を意味して一致する。
            //   ビット13/14（ShowMirrorMeshOrigin / ShowNormals）は既定 false なので
            //   正のままでよい。事情が違うので真似しないこと。
            if (!ShowUnselectedMirrorWireframe) b |= 1 << 15;
            if (!ShowUnselectedMirrorVertices)  b |= 1 << 16;
            // ビット17 も同じ理由で反転格納する（既定 true）。
            if (!ShowUnselectedMirrorMesh)      b |= 1 << 17;
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
            // 選択Mirror は UI トグルを廃止したため復元しない（常に true）。
            // 選択Mesh が OFF のときは WithMirrorClamped() が OFF に落とす。
            // ToBits のビット9は旧データ互換のため書き出しのみ維持する。
            ShowSelectedMirror      = true,
            ShowUnselectedMirror    = (b & (1 << 10)) != 0,
            ShowSelectedMeshOrigin   = (b & (1 << 11)) != 0,
            ShowUnselectedMeshOrigin = (b & (1 << 12)) != 0,
            // 旧データにはビット13が無いので false になる。新既定と一致する。
            ShowMirrorMeshOrigin     = (b & (1 << 13)) != 0,
            // 旧データにはビット14が無いので false になる。新既定と一致する。
            ShowNormals              = (b & (1 << 14)) != 0,
            // ビット15/16 は反転格納（1 = 非表示）。ToBits のコメント参照。
            // 旧データはビットが無く 0 → 表示。既定 true と一致する。
            ShowUnselectedMirrorWireframe = (b & (1 << 15)) == 0,
            ShowUnselectedMirrorVertices  = (b & (1 << 16)) == 0,
            ShowUnselectedMirrorMesh      = (b & (1 << 17)) == 0,
        };
    }
}
