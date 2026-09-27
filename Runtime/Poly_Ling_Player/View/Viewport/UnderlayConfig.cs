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
        }
    }
}
