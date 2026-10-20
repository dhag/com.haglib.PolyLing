// PLPostFxBridge.cs
// ビューのポストプロセス（いまはブルームだけ）の橋。
// Runtime/Poly_Ling_Main/Core/Misc/ に配置
//
// 【なぜ橋にするか】
//   ブルームは URP の Volume とカメラの追加データに触る。PolyLing.Runtime は URP を参照しないので、
//   実装は別アセンブリ PolyLing.Urp（Runtime/Poly_Ling_Urp）に置き、起動時にここへ登録する
//   （FBX の PLFbxBridge と同じ形）。
//
// 【カメラ】
//   ビューのカメラ（PlayerViewport）は作ったときに RegisterCamera で載せ、破棄で外す。
//   ブルームの有効／無効に合わせて、載せたカメラ全部のポストプロセスを切り替える。
//   後から作られたカメラにも、そのときの設定を当てる。
//   ブルーム中はビューの描画先を半精度浮動小数（ARGBHalf）にする（ViewportColorFormat）。
//
// 【保存しない】
//   モデルにもセッティングにも保存しない。Undo にも入れない。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.PostFx
{
    /// <summary>ブルームの設定。</summary>
    public struct PLBloomSettings
    {
        public bool  Enabled;
        public float Intensity;
        public float Threshold;
        public float Scatter;
        public Color Tint;
    }

    /// <summary>ポストプロセスの実装（PolyLing.Urp が登録する）。</summary>
    public interface IPLPostFx
    {
        /// <summary>カメラのポストプロセスを入れる／切る。</summary>
        void ApplyCamera(Camera cam, bool postProcessing);

        /// <summary>シーンの Global Volume にブルームを書く。失敗理由を返し、成功時は null。</summary>
        string ApplyBloom(PLBloomSettings settings);
    }

    public static class PLPostFxBridge
    {
        private static IPLPostFx _impl;
        private static readonly List<Camera> _cameras = new List<Camera>();
        private static PLBloomSettings _bloom;

        public static void Register(IPLPostFx impl) { _impl = impl; }

        public static bool IsAvailable => _impl != null;

        /// <summary>今のブルームの設定。</summary>
        public static PLBloomSettings Bloom => _bloom;

        /// <summary>
        /// ブルームの設定が変わった。ビュー（PlayerViewport）はこれを受けて描画先の形式を選び直す。
        /// URP はカメラの描画先テクスチャの形式をそのまま途中の色バッファに使うため
        /// （UniversalRenderPipelineCore.CreateRenderTextureDescriptor）、
        /// 描画先が 8bit だと 1 を超える明るさが残らずブルームが効かない。
        /// </summary>
        public static event System.Action BloomChanged;

        /// <summary>ブルームが効くのに要る、ビューの描画先の形式。</summary>
        public static RenderTextureFormat ViewportColorFormat
            => _bloom.Enabled ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;

        public static void RegisterCamera(Camera cam)
        {
            if (cam == null || _cameras.Contains(cam)) return;
            _cameras.Add(cam);
            _impl?.ApplyCamera(cam, _bloom.Enabled);
        }

        public static void UnregisterCamera(Camera cam)
        {
            if (cam == null) return;
            _cameras.Remove(cam);
        }

        /// <summary>ブルームを設定する。失敗理由を返し、成功時は null。</summary>
        public static string SetBloom(PLBloomSettings settings)
        {
            if (_impl == null) return "ポストプロセスの実装（PolyLing.Urp）が読み込まれていません";

            string reason = _impl.ApplyBloom(settings);
            if (reason != null) return reason;

            _bloom = settings;
            BloomChanged?.Invoke();
            _cameras.RemoveAll(c => c == null);
            foreach (var cam in _cameras) _impl.ApplyCamera(cam, settings.Enabled);
            return null;
        }
    }
}
