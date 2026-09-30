// PanelCommand.Underlay.cs
// 下絵（ビューの背面に敷く参照画像）を設定・削除・照会する要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【下絵はモデルが持つ】
//   ModelContext.Underlay に入り、モデルと一緒に保存される（UnderlayData.cs）。
//   形状ではないので Undo には入れない。
//
// 【置き方は方向で決まる】
//   上下前後左右（Top/Bottom/Front/Back/Left/Right）はモデル座標の 2 隅（corner0 / corner1）。
//   透視・平行投影（Persp/Ortho）は画面基準（topLeft / scaleOrigin / scaleX / scaleY）。
//   画面基準はビュー中央・ビューの高さを単位にする（UnderlayData.cs 冒頭）。
//   方向に合わない側の引数は使わない。
//
// 【実体は Viewer】
//   画像の読み込み（Texture2D）と表示は Player 側の UnderlayConfig が持つので、
//   受け口は PolyLingPlayerViewerCore.Underlay.cs にある。

using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>下絵を設定する。</summary>
    [PLCommand(Category = "underlay", Writes = PLWriteScope.None,
        Description = "現在のモデルの下絵を 1 方向ぶん設定する。上下前後左右はモデル座標の 2 隅で置き、カメラを動かしてもモデルとの位置関係が変わらない。透視・平行投影（Persp/Ortho）は画面基準で置く。画面基準はビュー中央を原点、ビューの高さを 1 とする単位なので、ビューの大きさが変わってもメッシュとの関係が変わらない。keepPlacement で初めて置くときはビュー中央に高さを合わせる。下絵はモデルと一緒に保存される。")]
    [PLResult("direction", PLResultKind.Text,    Description = "設定した方向")]
    [PLResult("filePath",  PLResultKind.Text,    Description = "画像ファイルの絶対パス")]
    [PLResult("width",     PLResultKind.Integer, Description = "画像の幅（画素）")]
    [PLResult("height",    PLResultKind.Integer, Description = "画像の高さ（画素）")]
    public sealed class SetUnderlayCommand : PanelCommand
    {
        [PLParam(Description = "設定する方向", Required = true)]
        public UnderlayDirection Direction { get; }

        [PLParam(Description = "画像ファイルのパス（png / jpg / tga / bmp）。作業フォルダの下だけを読める。空にすると今の画像のまま置き方だけ変える")]
        public string FilePath { get; }

        [PLParam(Description = "置き方の引数を使わず、今の置き方を保つ。まだ置いていなければ既定の置き方（上下前後左右は原点を中心に 1 画素 = MQO の 1 単位）にする")]
        public bool KeepPlacement { get; }

        [PLParam(Description = "上下前後左右のとき、画像の 1 隅のモデル座標。corner1 と同じ点にすると既定の置き方になる")]
        public Vector3 Corner0 { get; }

        [PLParam(Description = "上下前後左右のとき、corner0 と向かい合う隅のモデル座標")]
        public Vector3 Corner1 { get; }

        [PLParam(Description = "Persp / Ortho のとき、ビュー中央から画像の左上までの位置。ビューの高さを 1 とする単位で、下向きが正")]
        public Vector2 TopLeft { get; }

        [PLParam(Description = "Persp / Ortho のとき、拡大縮小の原点（画像の左上からの画素、下向きが正）")]
        public Vector2 ScaleOrigin { get; }

        [PLParam(Description = "Persp / Ortho のとき、横の倍率。1 で画像の高さがビューの高さと同じ大きさ（縦横比は画像のまま）", Min = 0.01)]
        public float ScaleX { get; }

        [PLParam(Description = "Persp / Ortho のとき、縦の倍率。1 で画像の高さがビューの高さと同じ", Min = 0.01)]
        public float ScaleY { get; }

        [PLParam(Description = "コントラスト（0〜1）。1 で元画像、0 で灰色一色。負なら今の値のまま。keepPlacement でも効く")]
        public float Contrast { get; }

        [PLParam(Description = "明るさ（0〜1）。1 で元画像、0 で黒。負なら今の値のまま。keepPlacement でも効く")]
        public float Intensity { get; }

        public SetUnderlayCommand(int modelIndex, UnderlayDirection direction, string filePath = "",
                                  bool keepPlacement = false,
                                  Vector3 corner0 = default, Vector3 corner1 = default,
                                  Vector2 topLeft = default, Vector2 scaleOrigin = default,
                                  float scaleX = 1f, float scaleY = 1f,
                                  float contrast = -1f, float intensity = -1f)
            : base(modelIndex)
        {
            Contrast      = contrast;
            Intensity     = intensity;
            Direction     = direction;
            FilePath      = filePath ?? "";
            KeepPlacement = keepPlacement;
            Corner0       = corner0;
            Corner1       = corner1;
            TopLeft       = topLeft;
            ScaleOrigin   = scaleOrigin;
            ScaleX        = scaleX;
            ScaleY        = scaleY;
        }
    }

    /// <summary>下絵を外す。</summary>
    [PLCommand(Category = "underlay", Writes = PLWriteScope.None,
        Description = "現在のモデルの下絵を外す。1 方向だけ、または全方向。作業板スロット（断面の作業空間の下絵）は外さない（clearEditSpacePlateUnderlay で外す）。")]
    [PLResult("remaining", PLResultKind.Integer, Description = "外した後に残っている下絵の数")]
    public sealed class ClearUnderlayCommand : PanelCommand
    {
        [PLParam(Description = "外す方向。allDirections を立てたときは使わない")]
        public UnderlayDirection Direction { get; }

        [PLParam(Description = "全方向の下絵を外す")]
        public bool AllDirections { get; }

        public ClearUnderlayCommand(int modelIndex, UnderlayDirection direction = UnderlayDirection.Front,
                                    bool allDirections = false)
            : base(modelIndex)
        {
            Direction     = direction;
            AllDirections = allDirections;
        }
    }

    /// <summary>下絵の設定を返す。</summary>
    [PLCommand(Category = "underlay", Writes = PLWriteScope.None,
        Description = "現在のモデルの下絵を一覧する。方向・画像パス・置き方・画像の大きさを同じ並びの配列で返す。モデルは変えない。")]
    [PLResult("count",       PLResultKind.Integer,   Description = "下絵の数")]
    [PLResult("directions",  PLResultKind.TextArray, Description = "方向", Optional = true)]
    [PLResult("filePaths",   PLResultKind.TextArray, Description = "画像ファイルの絶対パス。directions と同じ並び", Optional = true)]
    [PLResult("placements",  PLResultKind.TextArray, Description = "置き方（2 隅のモデル座標、または画面基準の値。画面基準はビュー中央・ビューの高さが単位）。directions と同じ並び", Optional = true)]
    [PLResult("imageSizes",  PLResultKind.TextArray, Description = "画像の大きさ（幅x高さ）。読めなかったものは空。directions と同じ並び", Optional = true)]
    [PLResult("contrasts",   PLResultKind.TextArray, Description = "コントラスト（0〜1）。directions と同じ並び", Optional = true)]
    [PLResult("intensities", PLResultKind.TextArray, Description = "明るさ（0〜1）。directions と同じ並び", Optional = true)]
    public sealed class QueryUnderlayCommand : PanelCommand
    {
        public QueryUnderlayCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }
}
