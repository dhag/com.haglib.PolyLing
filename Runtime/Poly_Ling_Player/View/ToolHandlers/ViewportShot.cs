// ViewportShot.cs
// メイン 3D 画面（ビューポート）の RenderTexture をそのまま読んで PNG にし、撮ったカメラを控える。
// 表情転写（FaceTransferHandler）とヒューマノイドボーンの当てはめ（HumanoidFitHandler）が使う。
// フレーム終端（PlayerCaptureRunner.RunAtEndOfFrame）から呼ぶこと。
//
// 読むのは画面に描かれている内容そのものなので、ワイヤー・頂点・グリッドなどを表示していれば写り込む。
// カメラは worldToCameraMatrix・projectionMatrix と画像の大きさ（FaceCaptureCamera）。

using System;
using UnityEngine;
using Poly_Ling.Tools.MediaPipe;

namespace Poly_Ling.Player
{
    public static class ViewportShot
    {
        /// <summary>ビューポートを撮る。失敗すれば false（error に理由）。</summary>
        public static bool TryShoot(PlayerViewport vp, out byte[] png, out FaceCaptureCamera camera, out string error)
        {
            png = null;
            camera = null;
            error = "";
            var rt  = vp?.RT;
            var cam = vp?.Cam;
            if (rt == null || cam == null) { error = "メイン 3D 画面がありません"; return false; }

            Texture2D tex = null;
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                png = tex.EncodeToPNG();
                camera = new FaceCaptureCamera
                {
                    View       = cam.worldToCameraMatrix,
                    Projection = cam.projectionMatrix,
                    Width      = rt.width,
                    Height     = rt.height,
                };
                return true;
            }
            catch (Exception e)
            {
                error = "撮影に失敗しました: " + e.Message;
                png = null;
                camera = null;
                return false;
            }
            finally
            {
                RenderTexture.active = prev;
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }
    }
}
