// PlayerScreenCapture.cs
// 画面キャプチャ（PNG 保存）。
// - MainView : メイン3D画面（Perspective パネル）だけを切り出す
// - TriView  : 3面図を含むビューポート領域を切り出す
// - Window   : ウインドウ全体（切り出しなし）
//
// RenderTexture を直接読むのではなく画面をキャプチャして切り出すのは、
// 下絵（RT 背面のパネル背景）とギズモ・選択矩形（UIToolkit 側の描画）を
// 含めた「見たままの絵」を保存するため。
//
// Runtime/Poly_Ling_Player/View/Capture/ に配置

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>書き出す形式。</summary>
    public enum CaptureFormat
    {
        Png,
        Jpeg,
    }

    /// <summary>キャプチャ対象。</summary>
    public enum CaptureTarget
    {
        MainView,   // メイン3D画面
        TriView,    // 3面図を含むビューポート領域
        Window,     // ウインドウ全体
    }

    public static class PlayerScreenCapture
    {
        /// <summary>保存先フォルダの既定値。</summary>
        public static string DefaultFolder
            => Path.Combine(Application.persistentDataPath, "PolyLing", "Captures");

        /// <summary>ファイル名の既定値（拡張子なし）。</summary>
        public const string DefaultFileName = "PolyLing";

        /// <summary>jpeg の既定の品質。</summary>
        public const int DefaultJpegQuality = 90;

        /// <summary>
        /// キャプチャを実行する。フレーム終端で1回だけ撮影し、PNG を保存する。
        /// crop が null ならウインドウ全体、非 null ならその要素の矩形で切り出す。
        /// 結果（保存パス、または失敗理由）は onDone へ返す。
        /// </summary>
        public static void Capture(
            VisualElement crop, string folder, string baseName, Action<bool, string> onDone)
            => Capture(crop, folder, baseName, 0, CaptureFormat.Png, DefaultJpegQuality, onDone);

        /// <summary>
        /// 縮小と書き出し形式を指定して撮る。
        ///
        /// maxLongEdge を超える長辺は、その値まで縮めてから保存する（0 以下なら等倍）。
        /// 画像は MCP の応答で最も高くつくので、送る前ではなく保存する前に小さくする。
        /// jpeg は写真的な画面では png より小さくなるが、細い線と文字はにじむ。
        /// </summary>
        public static void Capture(
            VisualElement crop, string folder, string baseName,
            int maxLongEdge, CaptureFormat format, int quality, Action<bool, string> onDone)
        {
            if (string.IsNullOrWhiteSpace(folder))   folder   = DefaultFolder;
            if (string.IsNullOrWhiteSpace(baseName)) baseName = DefaultFileName;
            quality = Mathf.Clamp(quality, 1, 100);

            // 切り出し矩形は撮影前（要素のレイアウトが確定している今）に取る。
            RectInt? rect = null;
            if (crop != null)
            {
                if (!TryGetScreenRect(crop, out var r))
                {
                    onDone?.Invoke(false, "切り出し範囲を取得できませんでした。");
                    return;
                }
                rect = r;
            }

            PlayerCaptureRunner.Instance.RunAtEndOfFrame(
                () => Shoot(rect, folder, baseName, maxLongEdge, format, quality, onDone));
        }

        // ================================================================
        // 撮影・保存
        // ================================================================

        private static void Shoot(
            RectInt? rect, string folder, string baseName,
            int maxLongEdge, CaptureFormat format, int quality, Action<bool, string> onDone)
        {
            Texture2D shot   = null;
            Texture2D cut    = null;
            Texture2D scaled = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                if (shot == null)
                {
                    onDone?.Invoke(false, "画面のキャプチャに失敗しました。");
                    return;
                }

                Texture2D src = shot;
                if (rect.HasValue)
                {
                    var r = ClampRect(rect.Value, shot.width, shot.height);
                    if (r.width <= 0 || r.height <= 0)
                    {
                        onDone?.Invoke(false, "切り出し範囲が画面外です。");
                        return;
                    }
                    cut = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false);
                    cut.SetPixels(shot.GetPixels(r.x, r.y, r.width, r.height));
                    cut.Apply();
                    src = cut;
                }

                if (maxLongEdge > 0 && Mathf.Max(src.width, src.height) > maxLongEdge)
                {
                    scaled = Downscale(src, maxLongEdge);
                    if (scaled != null) src = scaled;
                }

                byte[] bytes = format == CaptureFormat.Jpeg ? src.EncodeToJPG(quality) : src.EncodeToPNG();
                if (bytes == null || bytes.Length == 0)
                {
                    onDone?.Invoke(false, $"{(format == CaptureFormat.Jpeg ? "JPEG" : "PNG")} への変換に失敗しました。");
                    return;
                }

                Directory.CreateDirectory(folder);
                string path = NextPath(folder, baseName, format == CaptureFormat.Jpeg ? ".jpg" : ".png");
                File.WriteAllBytes(path, bytes);
                onDone?.Invoke(true, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayerScreenCapture] キャプチャ失敗: {e.Message}");
                onDone?.Invoke(false, e.Message);
            }
            finally
            {
                if (scaled != null) UnityEngine.Object.Destroy(scaled);
                if (cut  != null) UnityEngine.Object.Destroy(cut);
                if (shot != null) UnityEngine.Object.Destroy(shot);
            }
        }

        /// <summary>
        /// 長辺が maxLongEdge になるまで縮めた写しを作る。縦横の比は保つ。
        /// GetPixelBilinear で読むので、拡大方向には使わない（呼び出し側が大きいときだけ呼ぶ）。
        /// </summary>
        private static Texture2D Downscale(Texture2D src, int maxLongEdge)
        {
            float scale = (float)maxLongEdge / Mathf.Max(src.width, src.height);
            int w = Mathf.Max(1, Mathf.RoundToInt(src.width  * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(src.height * scale));

            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                int row = y * w;
                for (int x = 0; x < w; x++)
                    pixels[row + x] = src.GetPixelBilinear((x + 0.5f) / w, v);
            }

            dst.SetPixels(pixels);
            dst.Apply();
            return dst;
        }

        /// <summary>
        /// "&lt;baseName&gt;_0001&lt;拡張子&gt;" 形式で、まだ存在しない番号のパスを返す。
        /// 既存ファイルを上書きしない。
        /// </summary>
        private static string NextPath(string folder, string baseName, string extension)
        {
            string safe = SanitizeName(baseName);
            for (int i = 1; i <= 9999; i++)
            {
                string p = Path.Combine(folder, $"{safe}_{i:0000}{extension}");
                if (!File.Exists(p)) return p;
            }
            // 9999 まで埋まっている場合は時刻で一意化する。
            return Path.Combine(folder, $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
        }

        private static string SanitizeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Trim();
            return name.Length == 0 ? DefaultFileName : name;
        }

        // ================================================================
        // 座標変換
        // ================================================================

        /// <summary>
        /// UIToolkit 要素の矩形を画面ピクセル矩形（左下原点）へ変換する。
        /// worldBound はパネル座標（左上原点・ポイント単位）なので、
        /// scaledPixelsPerPoint でピクセル化し、Y を反転する。
        /// </summary>
        private static bool TryGetScreenRect(VisualElement e, out RectInt rect)
        {
            rect = default;
            if (e == null || e.panel == null) return false;

            Rect wb = e.worldBound;
            if (wb.width <= 0f || wb.height <= 0f) return false;
            if (float.IsNaN(wb.x) || float.IsNaN(wb.y)) return false;

            float ppp = e.panel.scaledPixelsPerPoint;
            if (ppp <= 0f) ppp = 1f;

            int x = Mathf.RoundToInt(wb.x * ppp);
            int w = Mathf.RoundToInt(wb.width  * ppp);
            int h = Mathf.RoundToInt(wb.height * ppp);
            // パネル座標は上原点、キャプチャ画像は下原点。
            int yTop = Mathf.RoundToInt(wb.yMax * ppp);
            int y    = Screen.height - yTop;

            rect = new RectInt(x, y, w, h);
            return true;
        }

        /// <summary>矩形をテクスチャ範囲へ収める。</summary>
        private static RectInt ClampRect(RectInt r, int texW, int texH)
        {
            int x0 = Mathf.Clamp(r.x, 0, texW);
            int y0 = Mathf.Clamp(r.y, 0, texH);
            int x1 = Mathf.Clamp(r.x + r.width,  0, texW);
            int y1 = Mathf.Clamp(r.y + r.height, 0, texH);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }
    }
}
