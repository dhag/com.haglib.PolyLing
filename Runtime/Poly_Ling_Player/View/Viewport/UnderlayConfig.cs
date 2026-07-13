// UnderlayConfig.cs
// 「下絵」（3D背面に敷く参照画像）の方向別設定を保持するデータ。
// 8方向: Persp / Ortho / Top / Bottom / Front / Back / Left / Right
// 各ビューポートは現在の表示方向に対応するスロットを表示する。
// Runtime/Poly_Ling_Player/View/Viewport/ に配置

using System;
using UnityEngine;

namespace Poly_Ling.Player
{
    /// <summary>下絵スロットの方向。</summary>
    public enum UnderlayDirection
    {
        Persp = 0,   // Perspective ビュー・透視モード
        Ortho,       // Perspective ビュー・オルソモード
        Top,
        Bottom,
        Front,
        Back,
        Left,
        Right,
    }

    /// <summary>1方向分の下絵設定。</summary>
    public sealed class UnderlaySlot
    {
        /// <summary>読み込んだ画像ファイルのパス（未設定は空）。</summary>
        public string FilePath = string.Empty;

        /// <summary>読み込み済みテクスチャ（未設定は null）。</summary>
        public Texture2D Texture;

        /// <summary>パネル左上からの表示位置（px）。</summary>
        public Vector2 TopLeft = Vector2.zero;

        /// <summary>拡大縮小の原点（要素ローカルpx）。</summary>
        public Vector2 ScaleOrigin = Vector2.zero;

        /// <summary>2Dスケール（x, y）。</summary>
        public Vector2 Scale = Vector2.one;

        public bool HasImage => Texture != null;
    }

    /// <summary>8方向分の下絵設定をまとめて保持する。</summary>
    public sealed class UnderlayConfig
    {
        private const int Count = 8;
        private readonly UnderlaySlot[] _slots;

        public UnderlayConfig()
        {
            _slots = new UnderlaySlot[Count];
            for (int i = 0; i < Count; i++) _slots[i] = new UnderlaySlot();
        }

        /// <summary>指定方向のスロットを返す。</summary>
        public UnderlaySlot Get(UnderlayDirection dir) => _slots[(int)dir];

        /// <summary>読み込み済みテクスチャを全て破棄する。</summary>
        public void DisposeTextures()
        {
            foreach (var s in _slots)
            {
                if (s.Texture != null)
                {
                    UnityEngine.Object.Destroy(s.Texture);
                    s.Texture = null;
                }
            }
        }
    }
}
