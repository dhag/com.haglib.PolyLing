// PlayerLightRig.cs
// ViewportLightSettings に従って Light の GameObject を作り・書き換え・消す。
// 起動時（Initialize）にシーン内の既存ライトを無効にし、破棄時（Dispose）に元へ戻す。
// 設定が変わったときだけ Apply が呼ばれる（毎フレームの処理はしない）。
// Runtime/Poly_Ling_Player/View/Viewport/ に配置

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Player
{
    public class PlayerLightRig
    {
        private Transform _parent;
        private readonly List<Light> _lights = new List<Light>();

        /// <summary>起動時に無効にしたシーン内のライト（破棄時に有効へ戻す）。</summary>
        private readonly List<Light> _disabledSceneLights = new List<Light>();

        public void Initialize(Transform parent, ViewportLightSettings settings)
        {
            _parent = parent;

            // 自分のライトを作る前に、シーンに置かれている有効なライトを止める。
            var existing = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var l in existing)
            {
                if (l == null || !l.enabled) continue;
                l.enabled = false;
                _disabledSceneLights.Add(l);
            }

            Apply(settings);
        }

        /// <summary>設定どおりにライトをそろえる。個数の差は作成・破棄で合わせる。</summary>
        public void Apply(ViewportLightSettings settings)
        {
            var list = settings?.Lights ?? new List<PlayerLightEntry>();

            while (_lights.Count > list.Count)
            {
                int last = _lights.Count - 1;
                if (_lights[last] != null) Object.Destroy(_lights[last].gameObject);
                _lights.RemoveAt(last);
            }
            while (_lights.Count < list.Count)
            {
                var go = new GameObject($"PL_Light_{_lights.Count}") { hideFlags = HideFlags.HideAndDontSave };
                if (_parent != null) go.transform.SetParent(_parent, false);
                _lights.Add(go.AddComponent<Light>());
            }

            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i].Clamped();
                var l = _lights[i];
                if (l == null) continue;

                l.type      = e.Kind == PlayerLightKind.Point ? LightType.Point
                            : e.Kind == PlayerLightKind.Spot  ? LightType.Spot
                            :                                   LightType.Directional;
                l.color     = e.Color;
                l.intensity = e.Intensity;
                l.range     = e.Range;
                l.spotAngle = e.SpotAngle;
                l.shadows   = e.Shadows ? LightShadows.Soft : LightShadows.None;
                l.transform.SetPositionAndRotation(e.Position, e.Rotation);
                l.enabled   = e.Enabled;
            }
        }

        public void Dispose()
        {
            foreach (var l in _lights)
                if (l != null) Object.Destroy(l.gameObject);
            _lights.Clear();

            foreach (var l in _disabledSceneLights)
                if (l != null) l.enabled = true;
            _disabledSceneLights.Clear();
        }
    }
}
