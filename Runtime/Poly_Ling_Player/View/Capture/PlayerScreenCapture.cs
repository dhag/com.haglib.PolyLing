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

    /// <summary>
    /// 撮影画像に描く矢印の位置の指定。実際のカーソルは撮らない（OS のカーソルは画面キャプチャに写らず、
    /// UI 自動操作はマウスを使わない）ので、PolyLing が画像へ描く。
    /// </summary>
    public readonly struct CaptureCursor
    {
        private enum Space { None, ScreenPixel, RangeRatio }

        private readonly Space   _space;
        private readonly Vector2 _value;

        private CaptureCursor(Space space, Vector2 value)
        {
            _space = space;
            _value = value;
        }

        /// <summary>描かない。</summary>
        public static CaptureCursor None => default;

        /// <summary>画面のピクセル位置（左下原点）。ボタンや入力欄の中心とマウスの位置に使う。</summary>
        public static CaptureCursor AtScreenPixel(Vector2 pixel) => new CaptureCursor(Space.ScreenPixel, pixel);

        /// <summary>撮影範囲に対する比率（左上原点）。</summary>
        public static CaptureCursor AtRangeRatio(float x, float y) => new CaptureCursor(Space.RangeRatio, new Vector2(x, y));

        public bool IsNone => _space == Space.None;

        /// <summary>
        /// 切り出し後の画像の中のピクセル位置（左下原点）を返す。
        /// origin は切り出し矩形の左下（ウインドウ全体なら 0,0）。
        /// </summary>
        internal Vector2 ToImagePixel(Vector2Int origin, int width, int height)
        {
            if (_space == Space.RangeRatio)
                return new Vector2(_value.x * width, (1f - _value.y) * height);
            return new Vector2(_value.x - origin.x, _value.y - origin.y);
        }
    }

    /// <summary>矢印の位置の結果。Inside が false なら描いていない。X・Y は撮影範囲に対する比率（左上原点）。</summary>
    public readonly struct CaptureCursorResult
    {
        public readonly float X;
        public readonly float Y;
        public readonly bool  Inside;

        public CaptureCursorResult(float x, float y, bool inside)
        {
            X = x;
            Y = y;
            Inside = inside;
        }
    }

    public static class PlayerScreenCapture
    {
        /// <summary>矢印の高さ[px]。保存する画像の上でこの大きさになるよう、縮小の前に大きめに描く。</summary>
        private const float CursorHeight = 24f;

        /// <summary>矢印の縁取りの太さ[px]。保存する画像の上での値。</summary>
        private const float CursorBorder = 1.5f;

        /// <summary>矢印の形。先端が原点で、x は右、y は下。高さ 1 に揃えてある。</summary>
        private static readonly Vector2[] CursorShape =
        {
            new Vector2(0f,    0f),
            new Vector2(0f,    0.85f),
            new Vector2(0.2f,  0.65f),
            new Vector2(0.35f, 1f),
            new Vector2(0.5f,  0.95f),
            new Vector2(0.35f, 0.6f),
            new Vector2(0.6f,  0.6f),
        };

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
            => Capture(crop, folder, baseName, maxLongEdge, format, quality, CaptureCursor.None,
                       (ok, msg, _) => onDone?.Invoke(ok, msg));

        /// <summary>
        /// 矢印を描いて撮る。cursor が None なら画像は変えない。
        /// 矢印の結果は onDone の第 3 引数で返す（描く指定が無い・撮れなかったときは null）。
        /// </summary>
        public static void Capture(
            VisualElement crop, string folder, string baseName,
            int maxLongEdge, CaptureFormat format, int quality,
            CaptureCursor cursor, Action<bool, string, CaptureCursorResult?> onDone)
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
                    onDone?.Invoke(false,
                        $"切り出し範囲を取得できませんでした（{(string.IsNullOrEmpty(crop.name) ? crop.GetType().Name : crop.name)}: "
                        + (crop.panel == null ? "パネルに載っていません" : $"矩形 {crop.worldBound}")
                        + "）。", null);
                    return;
                }
                rect = r;
            }

            PlayerCaptureRunner.Instance.RunAtEndOfFrame(
                () => Shoot(rect, folder, baseName, maxLongEdge, format, quality, cursor, onDone));
        }

        // ================================================================
        // 撮影・保存
        // ================================================================

        private static void Shoot(
            RectInt? rect, string folder, string baseName,
            int maxLongEdge, CaptureFormat format, int quality,
            CaptureCursor cursor, Action<bool, string, CaptureCursorResult?> onDone)
        {
            Texture2D shot   = null;
            Texture2D cut    = null;
            Texture2D scaled = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                if (shot == null)
                {
                    onDone?.Invoke(false, "画面のキャプチャに失敗しました。", null);
                    return;
                }

                Texture2D src = shot;
                Vector2Int origin = Vector2Int.zero;
                if (rect.HasValue)
                {
                    var r = ClampRect(rect.Value, shot.width, shot.height);
                    if (r.width <= 0 || r.height <= 0)
                    {
                        onDone?.Invoke(false, "切り出し範囲が画面外です。", null);
                        return;
                    }
                    cut = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false);
                    cut.SetPixels(shot.GetPixels(r.x, r.y, r.width, r.height));
                    cut.Apply();
                    src = cut;
                    origin = new Vector2Int(r.x, r.y);
                }

                CaptureCursorResult? cursorResult = null;
                if (!cursor.IsNone)
                {
                    Vector2 tip = cursor.ToImagePixel(origin, src.width, src.height);
                    bool inside = tip.x >= 0f && tip.x <= src.width && tip.y >= 0f && tip.y <= src.height;
                    if (inside) DrawCursor(src, tip, maxLongEdge);
                    cursorResult = new CaptureCursorResult(tip.x / src.width, 1f - tip.y / src.height, inside);
                }

                if (maxLongEdge > 0 && Mathf.Max(src.width, src.height) > maxLongEdge)
                {
                    scaled = Downscale(src, maxLongEdge);
                    if (scaled != null) src = scaled;
                }

                byte[] bytes = format == CaptureFormat.Jpeg ? src.EncodeToJPG(quality) : src.EncodeToPNG();
                if (bytes == null || bytes.Length == 0)
                {
                    onDone?.Invoke(false, $"{(format == CaptureFormat.Jpeg ? "JPEG" : "PNG")} への変換に失敗しました。", null);
                    return;
                }

                Directory.CreateDirectory(folder);
                string path = NextPath(folder, baseName, format == CaptureFormat.Jpeg ? ".jpg" : ".png");
                File.WriteAllBytes(path, bytes);
                onDone?.Invoke(true, path, cursorResult);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayerScreenCapture] キャプチャ失敗: {e.Message}");
                onDone?.Invoke(false, e.Message, null);
            }
            finally
            {
                if (scaled != null) UnityEngine.Object.Destroy(scaled);
                if (cut  != null) UnityEngine.Object.Destroy(cut);
                if (shot != null) UnityEngine.Object.Destroy(shot);
            }
        }

        /// <summary>
        /// 矢印を描く。先端が tip（左下原点のピクセル）に来る。白地に黒の縁取りで、縁取りは形の内側に取る
        /// （外側に取ると先端が指定の位置より先へ出る）。
        /// 縮小で同じ大きさに見えるよう、保存する画像での大きさから描く大きさを決める。
        /// </summary>
        private static void DrawCursor(Texture2D tex, Vector2 tip, int maxLongEdge)
        {
            int longEdge = Mathf.Max(tex.width, tex.height);
            float scale = maxLongEdge > 0 && longEdge > maxLongEdge ? (float)longEdge / maxLongEdge : 1f;
            float height = CursorHeight * scale;
            float border = CursorBorder * scale;

            // 形を画像の向き（x は右、y は下）のまま大きさだけ合わせる。y の反転は画素を読むときに行う。
            var shape = new Vector2[CursorShape.Length];
            float maxX = 0f;
            for (int i = 0; i < shape.Length; i++)
            {
                shape[i] = CursorShape[i] * height;
                if (shape[i].x > maxX) maxX = shape[i].x;
            }

            int x0 = Mathf.Max(0, Mathf.FloorToInt(tip.x) - 1);
            int x1 = Mathf.Min(tex.width,  Mathf.CeilToInt(tip.x + maxX) + 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(tip.y - height) - 1);
            int y1 = Mathf.Min(tex.height, Mathf.CeilToInt(tip.y) + 1);
            int w = x1 - x0, h = y1 - y0;
            if (w <= 0 || h <= 0) return;

            var pixels = tex.GetPixels(x0, y0, w, h);
            for (int j = 0; j < h; j++)
            {
                float py = tip.y - (y0 + j + 0.5f);
                int row = j * w;
                for (int i = 0; i < w; i++)
                {
                    float d = SignedDistance(new Vector2(x0 + i + 0.5f - tip.x, py), shape);
                    float cover = Mathf.Clamp01(0.5f - d);
                    if (cover <= 0f) continue;
                    float white = Mathf.Clamp01(-d - border + 0.5f);
                    var c = new Color(white, white, white, 1f);
                    pixels[row + i] = Color.Lerp(pixels[row + i], c, cover);
                }
            }
            tex.SetPixels(x0, y0, w, h, pixels);
            tex.Apply();
        }

        /// <summary>多角形までの符号付き距離。内側が負。</summary>
        private static float SignedDistance(Vector2 p, Vector2[] poly)
        {
            float best = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                float dist = (p - (a + ab * t)).sqrMagnitude;
                if (dist < best) best = dist;
                if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) * ab.x / ab.y)
                    inside = !inside;
            }
            best = Mathf.Sqrt(best);
            return inside ? -best : best;
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

        /// <summary>
        /// パネル座標（左上原点・ポイント単位）を画面ピクセル（左下原点）へ変換する。
        /// 切り出し矩形と同じ式。panelOwner はパネルに載っている要素。
        /// </summary>
        public static bool TryPanelToScreenPixel(VisualElement panelOwner, Vector2 panelPoint, out Vector2 pixel)
        {
            pixel = default;
            if (panelOwner == null || panelOwner.panel == null) return false;

            float ppp = panelOwner.panel.scaledPixelsPerPoint;
            if (ppp <= 0f) ppp = 1f;

            pixel = new Vector2(panelPoint.x * ppp, Screen.height - panelPoint.y * ppp);
            return true;
        }

        /// <summary>
        /// 画面ピクセル（左下原点）を撮影範囲に対する比率（左上原点）へ変換する。
        /// crop が null ならウインドウ全体。撮影のときと同じ矩形で計算する。
        /// </summary>
        public static bool TryScreenPixelToRangeRatio(VisualElement crop, Vector2 pixel, out Vector2 ratio, out bool inside)
        {
            ratio  = default;
            inside = false;

            var r = new RectInt(0, 0, Screen.width, Screen.height);
            if (crop != null)
            {
                if (!TryGetScreenRect(crop, out var cropRect)) return false;
                r = ClampRect(cropRect, Screen.width, Screen.height);
                if (r.width <= 0 || r.height <= 0) return false;
            }

            float x = pixel.x - r.x;
            float y = pixel.y - r.y;
            inside = x >= 0f && x <= r.width && y >= 0f && y <= r.height;
            ratio  = new Vector2(x / r.width, 1f - y / r.height);
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
