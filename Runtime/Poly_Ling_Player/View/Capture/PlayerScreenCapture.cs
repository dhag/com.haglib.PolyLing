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

        /// <summary>
        /// キャプチャを実行する。フレーム終端で1回だけ撮影し、PNG を保存する。
        /// crop が null ならウインドウ全体、非 null ならその要素の矩形で切り出す。
        /// 結果（保存パス、または失敗理由）は onDone へ返す。
        /// </summary>
        public static void Capture(
            VisualElement crop, string folder, string baseName, Action<bool, string> onDone)
        {
            if (string.IsNullOrWhiteSpace(folder))   folder   = DefaultFolder;
            if (string.IsNullOrWhiteSpace(baseName)) baseName = DefaultFileName;

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

            PlayerCaptureRunner.Instance.RunAtEndOfFrame(() => Shoot(rect, folder, baseName, onDone));
        }

        // ================================================================
        // 撮影・保存
        // ================================================================

        private static void Shoot(RectInt? rect, string folder, string baseName, Action<bool, string> onDone)
        {
            Texture2D shot = null;
            Texture2D cut  = null;
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

                byte[] png = src.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    onDone?.Invoke(false, "PNG への変換に失敗しました。");
                    return;
                }

                Directory.CreateDirectory(folder);
                string path = NextPath(folder, baseName);
                File.WriteAllBytes(path, png);
                onDone?.Invoke(true, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayerScreenCapture] キャプチャ失敗: {e.Message}");
                onDone?.Invoke(false, e.Message);
            }
            finally
            {
                if (cut  != null) UnityEngine.Object.Destroy(cut);
                if (shot != null) UnityEngine.Object.Destroy(shot);
            }
        }

        /// <summary>
        /// "&lt;baseName&gt;_0001.png" 形式で、まだ存在しない番号のパスを返す。
        /// 既存ファイルを上書きしない。
        /// </summary>
        private static string NextPath(string folder, string baseName)
        {
            string safe = SanitizeName(baseName);
            for (int i = 1; i <= 9999; i++)
            {
                string p = Path.Combine(folder, $"{safe}_{i:0000}.png");
                if (!File.Exists(p)) return p;
            }
            // 9999 まで埋まっている場合は時刻で一意化する。
            return Path.Combine(folder, $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
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
