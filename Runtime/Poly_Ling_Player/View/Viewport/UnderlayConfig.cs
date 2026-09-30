// UnderlayConfig.cs
// 「下絵」（3D背面に敷く参照画像）の画像を読み込んで保持し、現在モデルの下絵設定と結び付ける。
// Runtime/Poly_Ling_Player/View/Viewport/ に配置
//
// 【設定の正本はモデル】
//   方向ごとの画像パスと置き方は ModelContext.Underlay（UnderlayData）が持ち、
//   モデルと一緒に保存される。ここは設定を持たない。
//   モデルを切り替えれば、読む設定も自動でそのモデルのものになる。
//
// 【画像はパスをキーにして持つ】
//   同じ画像を別の方向・別のモデルで使っても 1 回だけ読む。
//   読めなかったパスも覚えておき、表示のたびに読み直さない（Load の reload で読み直す）。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    /// <summary>下絵の画像の読み込み・保持と、現在モデルの下絵設定への窓口。</summary>
    public sealed class UnderlayConfig
    {
        private readonly Func<ModelContext> _getModel;
        private readonly Dictionary<string, Texture2D> _cache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        public UnderlayConfig(Func<ModelContext> getModel)
        {
            _getModel = getModel;
        }

        /// <summary>現在のモデル。無ければ null。</summary>
        public ModelContext Model => _getModel?.Invoke();

        /// <summary>現在モデルの指定方向の設定（読むだけ）。下絵が無ければ null。</summary>
        public UnderlaySlotData Peek(UnderlayDirection dir) => Model?.Underlay?.Get(dir);

        /// <summary>
        /// 現在モデルの指定方向の設定（書く用）。下絵がまだ無ければ作る。
        /// モデルが無ければ null。
        /// </summary>
        public UnderlaySlotData Edit(UnderlayDirection dir) => Edit(Model, dir);

        /// <summary>指定モデルの指定方向の設定（書く用）。下絵がまだ無ければ作る。</summary>
        public static UnderlaySlotData Edit(ModelContext model, UnderlayDirection dir)
        {
            if (model == null) return null;
            if (model.Underlay == null) model.Underlay = new UnderlayData();
            return model.Underlay.Get(dir);
        }

        /// <summary>現在モデルの指定方向の画像。未設定・読めないときは null。</summary>
        public Texture2D GetTexture(UnderlayDirection dir)
        {
            var s = Peek(dir);
            if (s == null || s.IsEmpty) return null;
            return Load(s.FilePath, reload: false, out _);
        }

        // 方向または作業板スロットごとの表示用画像（コントラスト・明るさを画素へ焼いたもの）。
        // キーは方向なら列挙名、作業板スロットなら "plate:<ObjectId>"。
        private sealed class Adjusted
        {
            public Texture2D Source;
            public float     Contrast;
            public float     Intensity;
            public Texture2D Texture;
        }
        private readonly Dictionary<string, Adjusted> _adjusted =
            new Dictionary<string, Adjusted>();

        /// <summary>
        /// 現在モデルの指定方向の表示用画像。コントラスト・明るさが共に 1 なら元画像そのもの。
        /// それ以外は 表示 = 明るさ × (コントラスト × 画素 + (1 − コントラスト) × 0.5) を画素へ焼いた複製
        /// （色空間や UI の合成のしかたに左右されないよう、画像の値そのものを変える）。
        /// 元画像・値が変わらなければ作り直さない。未設定・読めないときは null。
        /// </summary>
        public Texture2D GetDisplayTexture(UnderlayDirection dir)
        {
            var s   = Peek(dir);
            var src = GetTexture(dir);
            if (s == null || src == null) return null;
            return Adjust(dir.ToString(), src, s.Contrast, s.Intensity);
        }

        /// <summary>作業板スロットの表示用画像（GetDisplayTexture と同じ焼き込み）。読めなければ null。</summary>
        public Texture2D GetPlateDisplayTexture(UnderlayPlateSlotData plate)
        {
            if (plate == null || plate.IsEmpty) return null;
            var src = Load(plate.FilePath, reload: false, out _);
            if (src == null) return null;
            return Adjust("plate:" + plate.ObjectId, src, plate.Contrast, plate.Intensity);
        }

        private Texture2D Adjust(string key, Texture2D src, float contrast, float intensity)
        {
            float c = Mathf.Clamp01(contrast);
            float k = Mathf.Clamp01(intensity);
            if (c >= 1f && k >= 1f) return src;

            if (_adjusted.TryGetValue(key, out var a) && a.Texture != null
                && a.Source == src && a.Contrast == c && a.Intensity == k)
                return a.Texture;

            if (a == null) { a = new Adjusted(); _adjusted[key] = a; }
            if (a.Texture == null || a.Texture.width != src.width || a.Texture.height != src.height)
            {
                if (a.Texture != null) UnityEngine.Object.Destroy(a.Texture);
                a.Texture = new Texture2D(src.width, src.height, TextureFormat.RGBA32, src.mipmapCount > 1);
                a.Texture.name = src.name + "_adjusted";
            }
            a.Texture.filterMode = src.filterMode;
            a.Texture.wrapMode   = src.wrapMode;

            var px = src.GetPixels32();
            float mul = k * c;
            float add = k * (1f - c) * 127.5f;
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i];
                p.r = (byte)Mathf.Clamp(Mathf.RoundToInt(p.r * mul + add), 0, 255);
                p.g = (byte)Mathf.Clamp(Mathf.RoundToInt(p.g * mul + add), 0, 255);
                p.b = (byte)Mathf.Clamp(Mathf.RoundToInt(p.b * mul + add), 0, 255);
                px[i] = p;
            }
            a.Texture.SetPixels32(px);
            a.Texture.Apply(true);

            a.Source    = src;
            a.Contrast  = c;
            a.Intensity = k;
            return a.Texture;
        }

        /// <summary>
        /// 画像を読む（読んであればそれを返す）。reload で読み直す。
        /// 読めなければ null と理由。
        /// </summary>
        public Texture2D Load(string path, bool reload, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "画像のパスが空です"; return null; }

            if (!reload && _cache.TryGetValue(path, out var cached))
            {
                if (cached == null) error = $"画像を読み込めません: {path}";
                return cached;
            }

            if (_cache.TryGetValue(path, out var old) && old != null)
                UnityEngine.Object.Destroy(old);
            _cache.Remove(path);

            Texture2D tex = null;
            try
            {
                if (!File.Exists(path))
                    error = $"画像ファイルがありません: {path}";
                else
                {
                    tex = new Texture2D(2, 2);
                    if (!tex.LoadImage(File.ReadAllBytes(path)))
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                        error = $"画像として読めません: {path}";
                    }
                    else
                    {
                        tex.name = Path.GetFileNameWithoutExtension(path);
                    }
                }
            }
            catch (Exception e)
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                tex = null;
                error = $"画像の読み込みに失敗しました: {e.Message}";
            }

            _cache[path] = tex;   // 読めなかったものも覚えておく（表示のたびに読み直さない）
            return tex;
        }

        /// <summary>読み込み済みの画像を全て破棄する。</summary>
        public void DisposeTextures()
        {
            foreach (var t in _cache.Values)
                if (t != null) UnityEngine.Object.Destroy(t);
            _cache.Clear();
            foreach (var a in _adjusted.Values)
                if (a?.Texture != null) UnityEngine.Object.Destroy(a.Texture);
            _adjusted.Clear();
        }
    }
}
