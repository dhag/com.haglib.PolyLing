// ViewportLightSettings.cs
// 3Dプレビューを照らすライトの設定（全ビューポート共通・任意個数）。
// PlayerViewportManager が1インスタンス保持し、PlayerLightRig へ渡す。
// 永続化は RecentPaths に 1 キー（"Viewport.Lights"）の文字列で行う。
//   ライト同士は ';'、ライト内の項目は ',' で区切る。
//   0 個のときは "none" を書く（空文字は RecentPaths でキー削除になり、
//   次回起動で既定の 3 灯に戻ってしまうため）。
// Runtime/Poly_Ling_Player/View/Viewport/ に配置

using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Poly_Ling.Player
{
    /// <summary>ライトの種類。</summary>
    public enum PlayerLightKind
    {
        Directional = 0,   // 平行光源
        Point       = 1,   // 点光源
        Spot        = 2,   // スポットライト
    }

    /// <summary>ライト 1 つ分の設定。</summary>
    public class PlayerLightEntry
    {
        public bool            Enabled   = true;
        public PlayerLightKind Kind      = PlayerLightKind.Directional;
        public Color           Color     = new Color(1f, 0.95686275f, 0.8392157f, 1f);
        public float           Intensity = 1f;
        /// <summary>上下の角度（度）。正で下向き（Unity の X 回転）。</summary>
        public float           Pitch     = 50f;
        /// <summary>水平の角度（度）。Unity の Y 回転。</summary>
        public float           Yaw       = 0f;
        /// <summary>位置（点光源・スポットのみ意味を持つ）。</summary>
        public Vector3         Position  = new Vector3(0f, 3f, 0f);
        /// <summary>届く範囲（点光源・スポットのみ）。</summary>
        public float           Range     = 10f;
        /// <summary>スポットの開き角（度）。</summary>
        public float           SpotAngle = 30f;
        public bool            Shadows   = true;

        public PlayerLightEntry Clone() => (PlayerLightEntry)MemberwiseClone();

        public PlayerLightEntry Clamped()
        {
            var e = Clone();
            if (e.Kind < PlayerLightKind.Directional || e.Kind > PlayerLightKind.Spot)
                e.Kind = PlayerLightKind.Directional;
            e.Color = new Color(Mathf.Clamp01(e.Color.r), Mathf.Clamp01(e.Color.g), Mathf.Clamp01(e.Color.b), 1f);
            e.Intensity = Mathf.Clamp(e.Intensity, 0f, 100f);
            e.Pitch     = Mathf.Clamp(e.Pitch, -90f, 90f);
            e.Yaw       = Mathf.Repeat(e.Yaw, 360f);
            e.Range     = Mathf.Clamp(e.Range, 0.001f, 10000f);
            e.SpotAngle = Mathf.Clamp(e.SpotAngle, 1f, 179f);
            return e;
        }

        /// <summary>向き（Quaternion）。</summary>
        public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);
    }

    /// <summary>
    /// ライト一式の設定。個数は任意（0 個も可）。
    /// </summary>
    public class ViewportLightSettings
    {
        public List<PlayerLightEntry> Lights = new List<PlayerLightEntry>();

        /// <summary>
        /// 既定値：これまでシーン（サンプルシーンA）に置いていた平行光源 3 灯と同じ値。
        /// </summary>
        public static ViewportLightSettings Default
        {
            get
            {
                var s = new ViewportLightSettings();
                s.Lights.Add(new PlayerLightEntry { Pitch = 41.09f, Yaw = 307.27f });
                s.Lights.Add(new PlayerLightEntry { Pitch = 49.31f, Yaw = 163.54f });
                s.Lights.Add(new PlayerLightEntry { Pitch = 49.31f, Yaw = 56.13f  });
                return s;
            }
        }

        public ViewportLightSettings Clone()
        {
            var s = new ViewportLightSettings();
            foreach (var e in Lights) s.Lights.Add(e.Clone());
            return s;
        }

        public ViewportLightSettings Clamped()
        {
            var s = new ViewportLightSettings();
            foreach (var e in Lights) if (e != null) s.Lights.Add(e.Clamped());
            return s;
        }

        // ── 永続化 ──────────────────────────────────────────────

        private const string NoneMark = "none";
        private const int FieldCount = 16;

        public string ToCsv()
        {
            if (Lights.Count == 0) return NoneMark;
            var ci = CultureInfo.InvariantCulture;
            var parts = new List<string>();
            foreach (var e in Lights)
            {
                parts.Add(string.Join(",", new string[]
                {
                    e.Enabled ? "1" : "0",
                    ((int)e.Kind).ToString(ci),
                    e.Color.r.ToString("R", ci),
                    e.Color.g.ToString("R", ci),
                    e.Color.b.ToString("R", ci),
                    e.Intensity.ToString("R", ci),
                    e.Pitch.ToString("R", ci),
                    e.Yaw.ToString("R", ci),
                    e.Position.x.ToString("R", ci),
                    e.Position.y.ToString("R", ci),
                    e.Position.z.ToString("R", ci),
                    e.Range.ToString("R", ci),
                    e.SpotAngle.ToString("R", ci),
                    e.Shadows ? "1" : "0",
                    "0", "0",   // 予備（後から項目を足すときの受け皿）
                }));
            }
            return string.Join(";", parts);
        }

        /// <summary>
        /// 文字列から復元する。未保存は Default、"none" は 0 個。
        /// 1 灯でも読めない行があれば Default を返す（中途半端な復元をしない）。
        /// </summary>
        public static ViewportLightSettings FromCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Default;
            var s = new ViewportLightSettings();
            if (csv.Trim() == NoneMark) return s;

            var ci = CultureInfo.InvariantCulture;
            foreach (var row in csv.Split(';'))
            {
                var a = row.Split(',');
                if (a.Length < 14) return Default;
                var f = new float[a.Length];
                for (int i = 0; i < a.Length; i++)
                    if (!float.TryParse(a[i], NumberStyles.Float, ci, out f[i])) return Default;

                s.Lights.Add(new PlayerLightEntry
                {
                    Enabled   = a[0] == "1",
                    Kind      = (PlayerLightKind)Mathf.RoundToInt(f[1]),
                    Color     = new Color(f[2], f[3], f[4], 1f),
                    Intensity = f[5],
                    Pitch     = f[6],
                    Yaw       = f[7],
                    Position  = new Vector3(f[8], f[9], f[10]),
                    Range     = f[11],
                    SpotAngle = f[12],
                    Shadows   = a[13] == "1",
                }.Clamped());
            }
            return s;
        }
    }
}
