// PanelCommand.Camera.cs
// ビューのカメラを合わせる・戻す・設定する・照会する要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【2 種類のカメラ】
//   メイン画面 … 注視点・回転 X/Y/Z・距離・画角・オルソ切替（OrbitCameraController）。
//   3 面図     … 上・正面・側面の 3 台で注視点・全体の回転・ズーム・パース切替・画角を共有する
//                （OrthoViewSharedState）。反転だけは 1 台ずつ持つ。
//   どのコマンドも views で対象を選ぶ。
//
// 【形状ではない】
//   Undo には入れない。モデルにも保存しない。
//
// 【実体は Viewer】
//   カメラは Player 側が持つので、受け口は PolyLingPlayerViewerCore.CameraCommands.cs にある。

using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>カメラ系コマンドの対象ビュー。</summary>
    public enum CameraViews
    {
        /// <summary>メイン画面と 3 面図の両方。</summary>
        All = 0,
        /// <summary>メイン画面だけ。</summary>
        Main,
        /// <summary>3 面図（上・正面・側面の 3 台連動）だけ。</summary>
        Tri,
    }

    /// <summary>モデル全体が入るようにカメラを合わせる。</summary>
    [PLCommand(Category = "camera", Writes = PLWriteScope.None,
        Description = "表示中の描画オブジェクト全体が入るようにカメラを合わせる。注視点を範囲の中心へ移し、角度は変えずに距離（3 面図はズーム）を決める。")]
    [PLResult("target",  PLResultKind.Text,    Description = "合わせた注視点（x,y,z）")]
    [PLResult("radius",  PLResultKind.Number,  Description = "範囲の外接球の半径")]
    [PLResult("objects", PLResultKind.Integer, Description = "範囲に入れた描画オブジェクトの数")]
    public sealed class FitCameraToModelCommand : PanelCommand
    {
        [PLParam(Description = "対象のビュー。All はメイン画面と 3 面図の両方")]
        public CameraViews Views { get; }

        public FitCameraToModelCommand(int modelIndex, CameraViews views = CameraViews.All)
            : base(modelIndex)
        {
            Views = views;
        }
    }

    /// <summary>選択オブジェクトが入るようにカメラを合わせる。</summary>
    [PLCommand(Category = "camera", Writes = PLWriteScope.None,
        Description = "選択中の描画オブジェクトが入るようにカメラを合わせる。注視点を範囲の中心へ移し、角度は変えずに距離（3 面図はズーム）を決める。選択が無ければ失敗する。")]
    [PLResult("target",  PLResultKind.Text,    Description = "合わせた注視点（x,y,z）")]
    [PLResult("radius",  PLResultKind.Number,  Description = "範囲の外接球の半径")]
    [PLResult("objects", PLResultKind.Integer, Description = "範囲に入れた描画オブジェクトの数")]
    public sealed class FitCameraToSelectionCommand : PanelCommand
    {
        [PLParam(Description = "対象のビュー。All はメイン画面と 3 面図の両方")]
        public CameraViews Views { get; }

        public FitCameraToSelectionCommand(int modelIndex, CameraViews views = CameraViews.All)
            : base(modelIndex)
        {
            Views = views;
        }
    }

    /// <summary>カメラを起動時の値に戻す。</summary>
    [PLCommand(Category = "camera", Writes = PLWriteScope.None,
        Description = "カメラを起動時の値に戻す。メイン画面は回転 X 20・Y 180・Z 0、距離 3、注視点は原点、画角 60、透視投影。3 面図は注視点は原点、ズーム 0.01、回転なし、正投影、画角 60、反転なし。")]
    public sealed class ResetCameraCommand : PanelCommand
    {
        [PLParam(Description = "対象のビュー。All はメイン画面と 3 面図の両方")]
        public CameraViews Views { get; }

        public ResetCameraCommand(int modelIndex, CameraViews views = CameraViews.All)
            : base(modelIndex)
        {
            Views = views;
        }
    }

    /// <summary>カメラを任意の値にする。</summary>
    [PLCommand(Category = "camera", Writes = PLWriteScope.None,
        Description = "カメラを任意の注視点・角度にする。メイン画面は target・rotationX/Y/Z・distance・fov・orthographic を、3 面図は target・triRotation・triHalfHeight を使う。今の値は queryCamera で読める。")]
    public sealed class SetCameraCommand : PanelCommand
    {
        [PLParam(Description = "対象のビュー。All はメイン画面と 3 面図の両方")]
        public CameraViews Views { get; }

        [PLParam(Description = "注視点（ワールド座標）")]
        public Vector3 Target { get; }

        [PLParam(Description = "メイン画面の回転 X（度。上から見下ろす向きが正）", Min = -89, Max = 89)]
        public float RotationX { get; }

        [PLParam(Description = "メイン画面の回転 Y（度。180 で正面から見る）")]
        public float RotationY { get; }

        [PLParam(Description = "メイン画面の回転 Z（度。視線まわりのロール）")]
        public float RotationZ { get; }

        [PLParam(Description = "メイン画面の注視点からの距離", Min = 0.0001)]
        public float Distance { get; }

        [PLParam(Description = "メイン画面の画角（度）", Min = 1, Max = 179)]
        public float Fov { get; }

        [PLParam(Description = "メイン画面をオルソ表示にする")]
        public bool Orthographic { get; }

        [PLParam(Description = "3 面図全体の回転（度、X・Y・Z のオイラー角）。0 で軸に揃う")]
        public Vector3 TriRotation { get; }

        [PLParam(Description = "3 面図の正面図に映る縦の半分の長さ（ワールド単位）。3 台とも同じ倍率になる", Min = 0.0001)]
        public float TriHalfHeight { get; }

        public SetCameraCommand(int modelIndex, CameraViews views = CameraViews.All,
                                Vector3 target = default,
                                float rotationX = 20f, float rotationY = 180f, float rotationZ = 0f,
                                float distance = 3f, float fov = 60f, bool orthographic = false,
                                Vector3 triRotation = default, float triHalfHeight = 1f)
            : base(modelIndex)
        {
            Views         = views;
            Target        = target;
            RotationX     = rotationX;
            RotationY     = rotationY;
            RotationZ     = rotationZ;
            Distance      = distance;
            Fov           = fov;
            Orthographic  = orthographic;
            TriRotation   = triRotation;
            TriHalfHeight = triHalfHeight;
        }
    }

    /// <summary>カメラの今の値を返す。</summary>
    [PLCommand(Category = "camera", Writes = PLWriteScope.None,
        Description = "メイン画面と 3 面図のカメラの今の値を返す。値は setCamera の引数にそのまま写せる形にしてある。")]
    [PLResult("mainTarget",       PLResultKind.Text,   Description = "メイン画面の注視点（x,y,z）")]
    [PLResult("rotationX",        PLResultKind.Number, Description = "メイン画面の回転 X（度）")]
    [PLResult("rotationY",        PLResultKind.Number, Description = "メイン画面の回転 Y（度）")]
    [PLResult("rotationZ",        PLResultKind.Number, Description = "メイン画面の回転 Z（度）")]
    [PLResult("distance",         PLResultKind.Number, Description = "メイン画面の距離")]
    [PLResult("fov",              PLResultKind.Number, Description = "メイン画面の画角（度）")]
    [PLResult("orthographic",     PLResultKind.Flag,   Description = "メイン画面がオルソ表示か")]
    [PLResult("triTarget",        PLResultKind.Text,   Description = "3 面図の注視点（x,y,z）")]
    [PLResult("triRotation",      PLResultKind.Text,   Description = "3 面図全体の回転（x,y,z のオイラー角、度）")]
    [PLResult("triHalfHeight",    PLResultKind.Number, Description = "3 面図の正面図に映る縦の半分の長さ")]
    [PLResult("triPerspective",   PLResultKind.Flag,   Description = "3 面図がパース表示か")]
    [PLResult("currentView",      PLResultKind.Text,   Description = "カレントビュー（Perspective / Top / Front / Side）。setCurrentView で切り替える")]
    public sealed class QueryCameraCommand : PanelCommand
    {
        public QueryCameraCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }
}
