// PlayerPrimitiveMeshSubPanel.ImagePlate.cs
// 図形生成サブパネル：画像板（基本図形）。画像の縦横比で厚みのある板を作り、表面に画像を貼る。
// 生成そのものは ImagePlateMeshGenerator（PrimitiveMeshFactory 経由）、材質の作成はコマンドの受け口。
// ここは諸元 UI と画像の選択だけを持つ。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【画像の読み込み】
//   選んだときに 1 回だけ読み、画素数を引数へ書き込む（プレビューの縦横比に使う）。
//   読んだテクスチャはすぐ捨てる。材質に貼るテクスチャは受け口が読み直す。
//
// 【作業フォルダの外の画像】
//   生成ボタンを押すたびに、選んだパスを 1 回だけ許可してから送る（PLSandbox.AllowOnceFromDialog）。

using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private ImagePlateMeshGenerator.ImagePlateParams _imagePlateP = ImagePlateMeshGenerator.ImagePlateParams.Default;

        /// <summary>画像選択ダイアログの最近使った場所のキー。</summary>
        private const string ImagePlateRecentKey = "PrimitiveMesh.ImagePlate.Image";

        /// <summary>画像を選べているか。生成ボタンの有効条件。</summary>
        private bool ImagePlateReady =>
            !string.IsNullOrEmpty(_imagePlateP.ImagePath)
            && _imagePlateP.ImageWidth > 0 && _imagePlateP.ImageHeight > 0;

        private void BuildImagePlateUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("ImagePlate")));
            c.Add(NF(() => _imagePlateP.MeshName, v => _imagePlateP.MeshName = v));
            c.Add(GearHint(T("ImagePlateHint")));

            // ── 画像 ──
            c.Add(SL(T("ImagePlateImage")));
            var pickRow = new VisualElement(); pickRow.style.flexDirection = FlexDirection.Row; pickRow.style.marginBottom = 2;
            SB(pickRow, T("ImagePlatePick"), PickImagePlateImage);
            c.Add(pickRow);

            c.Add(GearHint(ImagePlateInfoText()));

            // ── 大きさ ──
            c.Add(SR(T("ImagePlateLongSide"),
                ImagePlateMeshGenerator.ImagePlateParams.LongSideMin, ImagePlateMeshGenerator.ImagePlateParams.LongSideMax,
                () => _imagePlateP.LongSide, v => { _imagePlateP.LongSide = v; D(); }));
            c.Add(SR(T("Thickness"),
                ImagePlateMeshGenerator.ImagePlateParams.ThicknessMin, ImagePlateMeshGenerator.ImagePlateParams.ThicknessMax,
                () => _imagePlateP.Thickness, v => { _imagePlateP.Thickness = v; D(); }));

            // ── 分割 ──
            c.Add(IR(T("ImagePlateSegmentsX"),
                ImagePlateMeshGenerator.ImagePlateParams.SegmentsMin, ImagePlateMeshGenerator.ImagePlateParams.SegmentsMax,
                () => _imagePlateP.SegmentsX, v => { _imagePlateP.SegmentsX = v; D(); }));
            c.Add(IR(T("ImagePlateSegmentsY"),
                ImagePlateMeshGenerator.ImagePlateParams.SegmentsMin, ImagePlateMeshGenerator.ImagePlateParams.SegmentsMax,
                () => _imagePlateP.SegmentsY, v => { _imagePlateP.SegmentsY = v; D(); }));

            BuildPivotXYZ(c,
                () => _imagePlateP.Pivot, v => { _imagePlateP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);
        }

        /// <summary>選んでいる画像の名前と画素数、出来る板の大きさ。</summary>
        private string ImagePlateInfoText()
        {
            if (!ImagePlateReady) return T("ImagePlateNoImage");
            Vector2 s = ImagePlateMeshGenerator.PlateSize(_imagePlateP);
            return $"{Path.GetFileName(_imagePlateP.ImagePath)}  {_imagePlateP.ImageWidth} × {_imagePlateP.ImageHeight} px  →  {s.x:0.###} × {s.y:0.###}";
        }

        /// <summary>
        /// 画像を選ぶ。読めたときだけパスと画素数を書き込み、諸元 UI を作り直す。
        /// </summary>
        private void PickImagePlateImage()
        {
            string path = PlayerIoUiKit.AskLoadPath(
                T("ImagePlatePick"), ImagePlateRecentKey, _imagePlateP.ImagePath, "png,jpg,jpeg,tga,bmp");
            if (string.IsNullOrEmpty(path)) return;

            string why = Poly_Ling.Materials.MaterialEditOps.LoadTextureFile(path, out var tex);
            if (why != null) { _statusLabel.text = why; return; }

            _imagePlateP.ImagePath   = path;
            _imagePlateP.ImageWidth  = tex.width;
            _imagePlateP.ImageHeight = tex.height;
            Object.Destroy(tex);

            RebuildSettings();
            D();
        }

        /// <summary>
        /// 生成ボタンで送る直前に呼ぶ。画面で選んだ画像を 1 回だけ読めるようにする。
        /// </summary>
        private void AllowImagePlateImageOnce()
        {
            if (string.IsNullOrEmpty(_imagePlateP.ImagePath)) return;
            if (!Path.IsPathRooted(_imagePlateP.ImagePath)) return;
            Poly_Ling.Core.PLSandbox.AllowOnceFromDialog(_imagePlateP.ImagePath);
        }
    }
}
