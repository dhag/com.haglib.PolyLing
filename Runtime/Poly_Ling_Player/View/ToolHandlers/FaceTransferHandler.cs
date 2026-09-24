// FaceTransferHandler.cs
// 表情転写のツールの窓口 "faceTransfer"。BEFORE の撮影・検出依頼と、転写に使う材料（カメラ・BEFORE・AFTER）を持つ。
//
// ■ BEFORE の撮影（captureBefore）
//   フレーム終端（PlayerCaptureRunner）で、メイン 3D 画面（Perspective ビューポート）の RenderTexture を
//   そのまま読み、PNG にする。同時にそのカメラの worldToCameraMatrix・projectionMatrix と画像の大きさを控える。
//   読むのは画面に描かれている内容そのものなので、ワイヤー・頂点・グリッドなどを表示していれば写り込む。
//   撮った PNG は motionLive（受け入れ許可中の WebSocket）で MediaPipe クライアントへ送り、顔検出を頼む。
//   返事（role:'before'、同じ requestId）が来たら BEFORE として持つ。requestId の違う返事は捨てる。
//
// ■ AFTER
//   motionLive が保持している MediaPipe の最新 1 件（顔を含むもの）。転写の実行時に取り出す。
//
// ■ 転写
//   パネルが FaceExpressionTransferCommand を送り、PlayerCommandDispatcher が GetInputs で材料を受け取って実行する。

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools.MediaPipe;

namespace Poly_Ling.Player
{
    /// <summary>転写の材料。</summary>
    public sealed class FaceTransferInputs
    {
        public FaceCaptureCamera Camera;
        public Vector3[]         BeforePixels;
        public Vector3[]         AfterPixels;
    }

    [PLTool("faceTransfer", Description = "表情転写（BEFORE をメイン 3D 画面から撮って MediaPipe クライアントで検出し、AFTER の顔で変形する）")]
    public sealed class FaceTransferHandler
    {
        /// <summary>BEFORE を撮るビューポート（メイン 3D 画面）。</summary>
        public Func<PlayerViewport>    GetViewport;
        /// <summary>検出を頼む先（motionLive）。</summary>
        public MotionLiveHandler       Live;
        /// <summary>状態が変わったときに呼ぶ（パネルの表示更新）。</summary>
        public Action                  OnChanged;

        private FaceCaptureCamera _camera;
        private Vector3[]         _beforePixels;
        private int               _requestId;
        private int               _waitingId = -1;

        [PLToolState(Description = "BEFORE を撮ったカメラを持っているか")]
        public bool   HasCamera => _camera != null;
        [PLToolState(Description = "BEFORE の顔点を持っているか")]
        public bool   HasBefore => _beforePixels != null;
        [PLToolState(Description = "BEFORE の画像の大きさ（幅x高さ）")]
        public string BeforeImageSize => _camera != null ? $"{_camera.Width}x{_camera.Height}" : "";
        [PLToolState(Description = "BEFORE の検出の返事を待っているか")]
        public bool   WaitingBefore => _waitingId >= 0;
        [PLToolState(Description = "AFTER（motionLive が保持する最新の MediaPipe に顔がある）を使えるか")]
        public bool   HasAfter => TryGetAfterPixels(out _, out _);
        [PLToolState(Description = "直近の操作の結果")]
        public string Status { get; private set; } = "";

        /// <summary>BEFORE を撮り、MediaPipe クライアントへ検出を頼む。</summary>
        [PLToolAction(Description = "メイン 3D 画面を BEFORE として撮り、MediaPipe クライアントへ顔検出を頼む")]
        public void CaptureBefore()
        {
            if (Live == null || !Live.Listening) { SetStatus("motionLive を受け入れ許可にしてください"); return; }
            var vp = GetViewport?.Invoke();
            if (vp == null || !vp.IsReady) { SetStatus("メイン 3D 画面がありません"); return; }

            SetStatus("撮影しています…");
            PlayerCaptureRunner.Instance.RunAtEndOfFrame(() => Shoot(vp));
        }

        /// <summary>BEFORE とカメラを捨てる。</summary>
        [PLToolAction(Description = "BEFORE とカメラを捨てる")]
        public void ClearBefore()
        {
            _camera = null;
            _beforePixels = null;
            _waitingId = -1;
            SetStatus("BEFORE を捨てました");
        }

        /// <summary>転写の材料を返す。そろっていなければ null（reason に理由）。</summary>
        public FaceTransferInputs GetInputs(out string reason)
        {
            reason = "";
            if (_camera == null || _beforePixels == null) { reason = "BEFORE がありません（BEFORE を撮ってください）"; return null; }
            if (!TryGetAfterPixels(out var after, out reason)) return null;
            return new FaceTransferInputs { Camera = _camera, BeforePixels = _beforePixels, AfterPixels = after };
        }

        // ---------------- 撮影（フレーム終端） ----------------

        private void Shoot(PlayerViewport vp)
        {
            var rt = vp.RT;
            var cam = vp.Cam;
            if (rt == null || cam == null) { SetStatus("メイン 3D 画面がありません"); return; }

            Texture2D tex = null;
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                byte[] png = tex.EncodeToPNG();

                _camera = new FaceCaptureCamera
                {
                    View       = cam.worldToCameraMatrix,
                    Projection = cam.projectionMatrix,
                    Width      = rt.width,
                    Height     = rt.height,
                };
                _beforePixels = null;
                _waitingId = ++_requestId;
                int sent = Live.SendDetectImage(png, _waitingId);
                SetStatus(sent > 0
                    ? $"撮影しました（{rt.width}x{rt.height}）。MediaPipe クライアントの検出を待っています"
                    : "撮影しましたが、つながっている MediaPipe クライアントがありません");
                if (sent == 0) _waitingId = -1;
            }
            catch (Exception e)
            {
                SetStatus("撮影に失敗しました: " + e.Message);
            }
            finally
            {
                RenderTexture.active = prev;
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        // ---------------- BEFORE の返事（メインスレッド） ----------------

        /// <summary>motionLive から role:'before' の JSON を受ける。</summary>
        public void ReceiveBefore(string json)
        {
            if (_waitingId < 0 || _camera == null) return;
            JObject o;
            try { o = JObject.Parse(json); } catch (Exception) { return; }
            if ((int?)o["requestId"] != _waitingId) return;       // 古い依頼の返事
            _waitingId = -1;

            if (!TryReadFace(o, out var pts, out int w, out int h, out string reason))
            {
                SetStatus("BEFORE の検出に失敗しました: " + reason);
                return;
            }
            if (w != _camera.Width || h != _camera.Height)
            {
                SetStatus($"BEFORE の画像の大きさが撮影時と違います（{w}x{h}）");
                return;
            }
            _beforePixels = FaceExpressionTransfer.ToPixels(pts, w, h, FaceExpressionTransfer.FaceMeshPoints);
            SetStatus($"BEFORE を受け取りました（顔 {_beforePixels.Length} 点）");
        }

        // ---------------- 共通 ----------------

        private bool TryGetAfterPixels(out Vector3[] pixels, out string reason)
        {
            pixels = null;
            reason = "";
            if (Live == null || !Live.TryGetLatestMediaPipe(out string json, out _))
            { reason = "AFTER がありません（MediaPipe クライアントから顔を送ってください）"; return false; }
            JObject o;
            try { o = JObject.Parse(json); } catch (Exception e) { reason = "AFTER が読めません: " + e.Message; return false; }
            if (!TryReadFace(o, out var pts, out int w, out int h, out reason)) { reason = "AFTER: " + reason; return false; }
            pixels = FaceExpressionTransfer.ToPixels(pts, w, h, FaceExpressionTransfer.FaceMeshPoints);
            return true;
        }

        // {image:{w,h}, face:{landmarks:[[x,y,z,visibility],…]}} を読む
        private static bool TryReadFace(JObject o, out List<float[]> pts, out int w, out int h, out string reason)
        {
            pts = null; w = 0; h = 0; reason = "";
            try
            {
                w = (int?)o["image"]?["w"] ?? 0;
                h = (int?)o["image"]?["h"] ?? 0;
                if (w <= 0 || h <= 0) { reason = "画像の大きさがありません"; return false; }
                var lm = o["face"]?["landmarks"] as JArray;
                if (lm == null || lm.Count < FaceExpressionTransfer.FaceMeshPoints) { reason = "顔が検出されていません"; return false; }
                pts = new List<float[]>(lm.Count);
                foreach (var p in lm)
                {
                    var a = (JArray)p;
                    pts.Add(new[] { (float)a[0], (float)a[1], (float)a[2] });
                }
                return true;
            }
            catch (Exception e)
            {
                reason = "形が違います: " + e.Message;
                return false;
            }
        }

        private void SetStatus(string s)
        {
            Status = s;
            OnChanged?.Invoke();
        }
    }
}
