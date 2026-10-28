// PanelCommand.HumanoidFit.cs
// ボーンの無いモデルへ、MediaPipe の姿勢からヒューマノイドボーンを当てはめるための操作要求。
//   captureViewportImage      … メイン 3D 画面の描画結果を PNG に保存し、撮ったカメラを控える（フレーム終端で撮る）
//   queryViewportImageCapture … その結果（保存先・画像の大きさ・view と projection の行列）を返す
//   fitHumanoidBones          … 姿勢の画素位置とカメラから、Humanoid ボーンを作って割り当てる
// 姿勢の検出は外（Python アドオン humanoid_fit）で行う。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）

namespace Poly_Ling.Data
{
    /// <summary>
    /// メイン 3D 画面（Perspective）の RenderTexture をそのまま PNG に保存する。画面の文字や他のパネルは写らない。
    /// 撮影はフレーム終端で行うので、結果は queryViewportImageCapture に captureId を渡して受け取る。
    /// </summary>
    [PLCommand(Category = "camera", Writes = PLWriteScope.None,
        Tags = "capture,mediapipe,humanoid",
        Description = "メイン 3D 画面の描画結果（RenderTexture）を PNG に保存し、撮ったときのカメラ（view・projection の行列）を控える。フレーム終端で撮るので、結果は queryViewportImageCapture で受け取る。作業フォルダの下だけに書ける。")]
    [PLResult("captureId", PLResultKind.Text, Description = "受付番号（queryViewportImageCapture に渡す）")]
    [PLResult("status",    PLResultKind.Text, Description = "pending")]
    public class CaptureViewportImageCommand : PanelCommand
    {
        [PLParam(Description = "保存する PNG のパス。作業フォルダからの相対でも絶対でもよい。無いフォルダは作る", Required = true)]
        public string FilePath { get; }

        public CaptureViewportImageCommand(int modelIndex, string filePath)
            : base(modelIndex)
        {
            FilePath = filePath ?? "";
        }
    }

    /// <summary>captureViewportImage の結果を返す。</summary>
    [PLCommand(Category = "camera", Writes = PLWriteScope.None,
        Tags = "capture,mediapipe,humanoid",
        Description = "captureViewportImage の結果を返す。completed なら保存先・画像の大きさ・撮ったときの view と projection の行列（行優先 16 個）。")]
    [PLResult("captureId",  PLResultKind.Text,        Description = "受付番号")]
    [PLResult("status",     PLResultKind.Text,        Description = "pending / completed / failed")]
    [PLResult("path",       PLResultKind.Text,        Description = "保存した PNG の絶対パス。completed のときだけ")]
    [PLResult("width",      PLResultKind.Integer,     Description = "画像の幅[画素]。completed のときだけ")]
    [PLResult("height",     PLResultKind.Integer,     Description = "画像の高さ[画素]。completed のときだけ")]
    [PLResult("view",       PLResultKind.NumberArray, Description = "Camera.worldToCameraMatrix（行優先 16 個）。completed のときだけ")]
    [PLResult("projection", PLResultKind.NumberArray, Description = "Camera.projectionMatrix（行優先 16 個）。completed のときだけ")]
    [PLResult("error",      PLResultKind.Text,        Description = "失敗理由。failed のときだけ")]
    public class QueryViewportImageCaptureCommand : PanelCommand
    {
        [PLParam(Description = "captureViewportImage が返した captureId", Required = true)]
        public string CaptureId { get; }

        public QueryViewportImageCaptureCommand(int modelIndex, string captureId)
            : base(modelIndex)
        {
            CaptureId = captureId ?? "";
        }
    }

    /// <summary>
    /// ヒューマノイドボーンの当てはめ。T ポーズのモデルを正面から写した画像の MediaPipe 姿勢（33 点の画素位置）と、
    /// その画像を撮ったカメラ（captureViewportImage の view・projection）を受け取る。
    /// 実処理は HumanoidBoneFitter（位置）と PositionCsvNodeBuilder（ボーンの作成）。
    /// </summary>
    [PLCommand(Category = "rig.skeleton.humanoid",
        Effects = PLCommandEffect.CreatesObject | PLCommandEffect.Skeleton | PLCommandEffect.Hierarchy,
        Writes = PLWriteScope.ModelWide,
        Tags = "bone,humanoid,skeleton,mediapipe",
        Description = "MediaPipe の姿勢（33 点の画素位置）と撮影時のカメラから、Hips〜Toes の Humanoid ボーン（指は除く）を作り、Humanoid 割当を付ける。奥行きは対象メッシュの頂点から決める。画像とカメラは captureViewportImage で得る。")]
    [PLResult("created",      PLResultKind.Integer,   Description = "作ったボーンの数")]
    [PLResult("bones",        PLResultKind.TextArray, Description = "作ったボーンの Humanoid 名（作った順）")]
    [PLResult("renamed",      PLResultKind.Integer,   Description = "既存の名前と重なったため名前を変えた数")]
    [PLResult("armsSwapped",  PLResultKind.Flag,      Description = "腕で MediaPipe の左（奇数）を本人の右にしたか")]
    [PLResult("legsSwapped",  PLResultKind.Flag,      Description = "脚で MediaPipe の左（奇数）を本人の右にしたか")]
    [PLResult("radiusPixels", PLResultKind.Number,    Description = "奥行きを決めるのに使った半径（画素）")]
    [PLResult("message",      PLResultKind.Text,      Description = "結果の要約")]
    public class FitHumanoidBonesCommand : PanelCommand
    {
        [PLParam(Description = "MediaPipe 姿勢 33 点の画素位置。x,y の順に 2 個ずつ（66 個）。x 右・y 下、画像の左上が原点", Required = true)]
        public float[] PosePixels { get; }

        [PLParam(Description = "画像の幅[画素]（撮影時と同じ）", Required = true)]
        public int ImageWidth { get; }

        [PLParam(Description = "画像の高さ[画素]（撮影時と同じ）", Required = true)]
        public int ImageHeight { get; }

        [PLParam(Description = "撮影時の Camera.worldToCameraMatrix（行優先 16 個）。queryViewportImageCapture の view", Required = true)]
        public float[] View { get; }

        [PLParam(Description = "撮影時の Camera.projectionMatrix（行優先 16 個）。queryViewportImageCapture の projection", Required = true)]
        public float[] Projection { get; }

        [PLParam(IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "奥行きを決めるのに使う描画オブジェクト（ミラー側を含む）の masterIndex。省くと、頂点を持つ表示中の描画オブジェクトすべて")]
        public int[] TargetMasterIndices { get; }

        [PLParam(Description = "奥行きを決める半径の、肩幅（画素）に対する比。既定 0.15")]
        public float RadiusRatio { get; }

        public FitHumanoidBonesCommand(int modelIndex, float[] posePixels, int imageWidth, int imageHeight,
                                       float[] view, float[] projection,
                                       int[] targetMasterIndices = null, float radiusRatio = 0.15f)
            : base(modelIndex)
        {
            PosePixels          = posePixels ?? System.Array.Empty<float>();
            ImageWidth          = imageWidth;
            ImageHeight         = imageHeight;
            View                = view ?? System.Array.Empty<float>();
            Projection          = projection ?? System.Array.Empty<float>();
            TargetMasterIndices = targetMasterIndices ?? System.Array.Empty<int>();
            RadiusRatio         = radiusRatio;
        }
    }

    /// <summary>
    /// 指ボーンの当てはめ。手を写した画像の MediaPipe Hand（21 点の画素位置）と、撮影時のカメラを受け取る。
    /// 割当に &lt;Side&gt;Hand があればその下に指を作り、無ければ手首に &lt;Side&gt;Hand も作る。
    /// 実処理は HandBoneFitter（位置）と PositionCsvNodeBuilder（ボーンの作成）。
    /// </summary>
    [PLCommand(Category = "rig.skeleton.humanoid",
        Effects = PLCommandEffect.CreatesObject | PLCommandEffect.Skeleton | PLCommandEffect.Hierarchy,
        Writes = PLWriteScope.ModelWide,
        Tags = "bone,humanoid,skeleton,mediapipe,hand,finger",
        Description = "MediaPipe Hand（21 点の画素位置）と撮影時のカメラから、片手の指ボーン（親指〜小指の Proximal・Intermediate・Distal と、割当なしの指先）を作り、Humanoid 割当を付ける。割当に Hand があればその下につなぎ、無ければ手首に Hand も作る。奥行きは対象メッシュの頂点から決める。画像とカメラは captureViewportImage で得る。")]
    [PLResult("created",      PLResultKind.Integer,   Description = "作ったボーンの数")]
    [PLResult("bones",        PLResultKind.TextArray, Description = "作ったボーンの名前（表の名前。作った順）")]
    [PLResult("side",         PLResultKind.Text,      Description = "当てはめた手（Left / Right）")]
    [PLResult("attached",     PLResultKind.Flag,      Description = "既存の Hand ボーンの下につないだか")]
    [PLResult("radiusPixels", PLResultKind.Number,    Description = "奥行きを決めるのに使った半径（画素）")]
    [PLResult("message",      PLResultKind.Text,      Description = "結果の要約")]
    public class FitHumanoidHandBonesCommand : PanelCommand
    {
        [PLParam(Description = "MediaPipe Hand 21 点の画素位置。x,y の順に 2 個ずつ（42 個）。x 右・y 下、画像の左上が原点", Required = true)]
        public float[] HandPixels { get; }

        [PLParam(Description = "画像の幅[画素]（撮影時と同じ）", Required = true)]
        public int ImageWidth { get; }

        [PLParam(Description = "画像の高さ[画素]（撮影時と同じ）", Required = true)]
        public int ImageHeight { get; }

        [PLParam(Description = "撮影時の Camera.worldToCameraMatrix（行優先 16 個）", Required = true)]
        public float[] View { get; }

        [PLParam(Description = "撮影時の Camera.projectionMatrix（行優先 16 個）", Required = true)]
        public float[] Projection { get; }

        [PLParam(Description = "Left / Right / Auto。Auto は T ポーズで手のひらが下向きの前提で、点の並びから決める", Required = true)]
        public string Side { get; }

        [PLParam(IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "奥行きを決めるのに使う描画オブジェクト（ミラー側を含む）の masterIndex。省くと、頂点を持つ表示中の描画オブジェクトすべて")]
        public int[] TargetMasterIndices { get; }

        [PLParam(Description = "奥行きを決める半径の、手首→中指の付け根の画素長に対する比。既定 0.1")]
        public float RadiusRatio { get; }

        [PLParam(Description = "指先の末端ボーン（割当なし）も作る。既定 true")]
        public bool IncludeTips { get; }

        public FitHumanoidHandBonesCommand(int modelIndex, float[] handPixels, int imageWidth, int imageHeight,
                                           float[] view, float[] projection, string side,
                                           int[] targetMasterIndices = null, float radiusRatio = 0.1f,
                                           bool includeTips = true)
            : base(modelIndex)
        {
            HandPixels          = handPixels ?? System.Array.Empty<float>();
            ImageWidth          = imageWidth;
            ImageHeight         = imageHeight;
            View                = view ?? System.Array.Empty<float>();
            Projection          = projection ?? System.Array.Empty<float>();
            Side                = side ?? "";
            TargetMasterIndices = targetMasterIndices ?? System.Array.Empty<int>();
            RadiusRatio         = radiusRatio;
            IncludeTips         = includeTips;
        }
    }
}
