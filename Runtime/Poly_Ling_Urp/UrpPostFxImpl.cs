// UrpPostFxImpl.cs
// PLPostFxBridge の URP 実装（ブルーム）。
// Runtime/Poly_Ling_Urp/ に配置（アセンブリ PolyLing.Urp）
//
// 【Volume】
//   シーンにある有効な Global Volume を使う。無ければ保存しない Global Volume を作る。
//   プロファイルは Volume.profile（実行時の複製）に書くので、シーンや .asset は書き換えない。
//   sharedProfile が空でも Volume.profile は空のプロファイルを作って返す。
//
// 【カメラ】
//   ポストプロセスはカメラごとの設定（UniversalAdditionalCameraData.renderPostProcessing）。

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Poly_Ling.PostFx;

namespace Poly_Ling.Urp
{
    public sealed class UrpPostFxImpl : IPLPostFx
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PLPostFxBridge.Register(new UrpPostFxImpl());
        }

        public void ApplyCamera(Camera cam, bool postProcessing)
        {
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = postProcessing;
        }

        public string ApplyBloom(PLBloomSettings s)
        {
            var volume = FindGlobalVolume();
            if (volume == null)
            {
                var go = new GameObject("PolyLing Global Volume") { hideFlags = HideFlags.DontSave };
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
            }

            var profile = volume.profile;
            if (profile == null) return "Global Volume のプロファイルを用意できませんでした";

            if (!profile.TryGet(out Bloom bloom))
                bloom = profile.Add<Bloom>(true);

            bloom.active = s.Enabled;
            bloom.intensity.Override(Mathf.Max(0f, s.Intensity));
            bloom.threshold.Override(Mathf.Max(0f, s.Threshold));
            bloom.scatter.Override(Mathf.Clamp01(s.Scatter));
            bloom.tint.Override(s.Tint);
            volume.weight = 1f;
            return null;
        }

        private static Volume FindGlobalVolume()
        {
            var all = Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var v in all)
                if (v != null && v.isActiveAndEnabled && v.isGlobal) return v;
            return null;
        }
    }
}
