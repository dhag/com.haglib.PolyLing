// UnderlayData.cs
// 下絵（ビューの背面に敷く参照画像）の設定。モデルが持つ（ModelContext.Underlay）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜモデルが持つか】
//   下絵はそのモデルを作るための資料なので、モデルと一緒に保存・読込する。
//   MQO の BackImage チャンクもモデル（ドキュメント）単位で持っている。
//   読み込んだ画像（Texture2D）は持たない。画像は Player 側の UnderlayConfig が
//   パスをキーにして読み込み・保持する。
//
// 【2 種類の置き方】
//   上下前後左右の 6 方向（IsModelAnchored が true）はモデル座標の 2 隅で置く。
//   カメラをズーム・移動しても、モデルとの位置関係は変わらない。
//   透視・平行投影の 2 スロット（Persp / Ortho）は視線の向きが決まらないため、
//   画面基準（左上位置・拡大縮小の原点・2D スケール）で置く。
//
// 【画面基準はビュー中央・ビューの高さで持つ】
//   カメラはビュー中央を基準に、ビューの高さに比例した大きさで描く（視野角・平行投影の
//   幅はどちらも縦で決まる）。下絵も同じ基準で持たないと、ビューの大きさが変わったとき
//   メッシュとだけずれる。そこで次の単位で持つ（ViewRelative = true）。
//     TopLeft … 画像の左上の、ビュー中央からの位置。ビューの高さを 1 とする（Y 下向き）。
//     Scale   … 1 で画像の高さがビューの高さと同じになる倍率。
//     ScaleOrigin … 画像ローカルの画素（Y 下向き）。ビューには依らない。
//   ViewRelative = false は以前の形式（ビュー左上からの画素・画素倍率）。
//   最初に表示するときのビューの大きさで今の形式に直す（ConvertLegacyScreen）。
//
// 【2 隅】
//   Corner0 / Corner1 は画像の向かい合う 2 隅のモデル座標。どちらがどの隅かは問わない。
//   表示ではそのビューへ投影した 2 点が張る矩形に画像を敷く。
//   視線方向の成分は表示に使わない。
//
// 【依存】
//   #if UNITY_EDITOR を含まない。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>下絵スロットの方向。並びは保存形式（名前で保存）とは無関係。</summary>
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

    /// <summary>1 方向分の下絵設定。</summary>
    [Serializable]
    public class UnderlaySlotData
    {
        /// <summary>画像ファイルの絶対パス。空なら未設定。</summary>
        public string FilePath = string.Empty;

        // ── 画面基準（Persp / Ortho） ──

        /// <summary>画像の左上の、ビュー中央からの位置。ビューの高さを 1 とする（Y 下向き）。</summary>
        public Vector2 TopLeft = Vector2.zero;

        /// <summary>拡大縮小の原点（画像ローカル px、Y 下向き）。</summary>
        public Vector2 ScaleOrigin = Vector2.zero;

        /// <summary>2D スケール（x, y）。1 で画像の高さがビューの高さと同じ。</summary>
        public Vector2 Scale = Vector2.one;

        /// <summary>TopLeft / Scale が上の単位か。false は以前の画素基準（表示時に直す）。</summary>
        public bool ViewRelative = false;

        // ── モデル座標基準（上下前後左右） ──

        /// <summary>画像の 1 隅のモデル座標。</summary>
        public Vector3 Corner0 = Vector3.zero;

        /// <summary>Corner0 と向かい合う隅のモデル座標。</summary>
        public Vector3 Corner1 = Vector3.zero;

        // ── 表示調整 ──

        /// <summary>コントラスト（0〜1）。1 で元画像、0 で灰色一色。</summary>
        public float Contrast = 1f;

        /// <summary>明るさ（0〜1）。1 で元画像、0 で黒。</summary>
        public float Intensity = 1f;

        /// <summary>画像が設定されていないか。</summary>
        public bool IsEmpty => string.IsNullOrEmpty(FilePath);

        /// <summary>2 隅が置かれているか（同じ点なら置かれていない）。</summary>
        public bool HasCorners => Corner0 != Corner1;

        /// <summary>ディープコピー。</summary>
        public UnderlaySlotData Clone() => new UnderlaySlotData
        {
            FilePath    = FilePath ?? string.Empty,
            TopLeft     = TopLeft,
            ScaleOrigin = ScaleOrigin,
            Scale       = Scale,
            ViewRelative = ViewRelative,
            Corner0     = Corner0,
            Corner1     = Corner1,
            Contrast    = Contrast,
            Intensity   = Intensity,
        };

        /// <summary>
        /// 画面基準の置き方を、指定したビューの画素（左上からの位置・画素倍率）に直す。
        /// texHeight は画像の高さ（画素）、viewW / viewH はビューの画素数。
        ///
        /// 画像の点 p（画像ローカル画素）の表示位置を
        ///   中央 + viewH·TopLeft + k·(ScaleOrigin + (p − ScaleOrigin)·Scale)   （k = viewH / texHeight）
        /// とする。パネル側は 左上 + ScaleOrigin + (p − ScaleOrigin)·倍率 で描くので、
        /// 左上 = 中央 + viewH·TopLeft + (k − 1)·ScaleOrigin、倍率 = k·Scale になる。
        /// これでビューの大きさが変わっても、画像は中央を基準に高さに比例して動く。
        /// </summary>
        public void ToViewPixels(int texHeight, float viewW, float viewH,
                                 out Vector2 topLeftPx, out Vector2 scalePx)
        {
            float k = viewH / Mathf.Max(1, texHeight);
            topLeftPx = new Vector2(viewW * 0.5f, viewH * 0.5f) + TopLeft * viewH + (k - 1f) * ScaleOrigin;
            scalePx   = Scale * k;
        }

        /// <summary>ビューの画素（左上からの位置・画素倍率）から画面基準の置き方を決める。ToViewPixels の逆。</summary>
        public void FromViewPixels(int texHeight, float viewW, float viewH,
                                   Vector2 topLeftPx, Vector2 scalePx)
        {
            float k = viewH / Mathf.Max(1, texHeight);
            TopLeft      = (topLeftPx - new Vector2(viewW * 0.5f, viewH * 0.5f) - (k - 1f) * ScaleOrigin) / viewH;
            Scale        = scalePx / k;
            ViewRelative = true;
        }

        /// <summary>以前の画素基準の値を、指定したビューの大きさで今の形式に直す。今の形式なら何もしない。</summary>
        public void ConvertLegacyScreen(int texHeight, float viewW, float viewH)
        {
            if (ViewRelative) return;
            FromViewPixels(texHeight, viewW, viewH, TopLeft, Scale);
        }

        /// <summary>画面基準の既定の置き方：ビュー中央に、画像の高さをビューの高さに合わせる。</summary>
        public void SetDefaultScreenPlacement(int texWidth, int texHeight)
        {
            float aspect = texWidth / (float)Mathf.Max(1, texHeight);
            TopLeft      = new Vector2(-0.5f * aspect, -0.5f);
            ScaleOrigin  = Vector2.zero;
            Scale        = Vector2.one;
            ViewRelative = true;
        }
    }

    /// <summary>
    /// 作業板スロット（PolyLing_UV_Billboard_Design.md 8.3）。断面系の作業空間の代理オブジェクトに固定する参考画像。
    ///
    /// 【基準】代理オブジェクトのローカル XY 座標で 2 隅を持つ。オブジェクトの移動・回転やビルボードの
    ///   ロック状態によらず、断面との位置関係が保たれる。
    /// 【対象】並び位置ではなく安定 ID（MeshContext.ObjectId）で指す。オブジェクトの追加・削除で
    ///   別の対象を指さない。対象が見つからないスロットも消さず、表示しないまま残す（8.4）。
    /// </summary>
    [Serializable]
    public class UnderlayPlateSlotData
    {
        /// <summary>下絵を固定する代理オブジェクトの安定 ID。</summary>
        public ulong ObjectId;

        /// <summary>画像ファイルの絶対パス。空なら未設定。</summary>
        public string FilePath = string.Empty;

        /// <summary>画像の 1 隅（代理のローカル XY）。</summary>
        public Vector2 Corner0 = Vector2.zero;

        /// <summary>Corner0 と向かい合う隅（代理のローカル XY）。</summary>
        public Vector2 Corner1 = Vector2.zero;

        /// <summary>コントラスト（0〜1）。1 で元画像、0 で灰色一色。</summary>
        public float Contrast = 1f;

        /// <summary>明るさ（0〜1）。1 で元画像、0 で黒。</summary>
        public float Intensity = 1f;

        /// <summary>画像が設定されていないか。</summary>
        public bool IsEmpty => string.IsNullOrEmpty(FilePath);

        /// <summary>2 隅が置かれているか。</summary>
        public bool HasCorners => Corner0.x != Corner1.x && Corner0.y != Corner1.y;

        /// <summary>ディープコピー。</summary>
        public UnderlayPlateSlotData Clone() => new UnderlayPlateSlotData
        {
            ObjectId  = ObjectId,
            FilePath  = FilePath ?? string.Empty,
            Corner0   = Corner0,
            Corner1   = Corner1,
            Contrast  = Contrast,
            Intensity = Intensity,
        };
    }

    /// <summary>
    /// 8 方向分の下絵設定と作業板スロットの一覧。ModelContext.Underlay として 1 つ持つ。null＝下絵なし。
    /// 作業板スロットは方向スロットとは別の一覧（保存も別の一覧・別のファイル）。
    /// </summary>
    [Serializable]
    public class UnderlayData
    {
        /// <summary>方向の数。</summary>
        public const int Count = 8;

        private readonly UnderlaySlotData[] _slots;
        private readonly List<UnderlayPlateSlotData> _plates = new List<UnderlayPlateSlotData>();

        public UnderlayData()
        {
            _slots = new UnderlaySlotData[Count];
            for (int i = 0; i < Count; i++) _slots[i] = new UnderlaySlotData();
        }

        // ── 作業板スロット ──

        /// <summary>作業板スロットの一覧（読むだけ）。</summary>
        public IReadOnlyList<UnderlayPlateSlotData> Plates => _plates;

        /// <summary>指定オブジェクトの作業板スロット。無ければ null。</summary>
        public UnderlayPlateSlotData FindPlate(ulong objectId)
        {
            foreach (var p in _plates) if (p != null && p.ObjectId == objectId) return p;
            return null;
        }

        /// <summary>作業板スロットを置く（同じオブジェクトのものがあれば差し替える）。空のスロットは消す。</summary>
        public void SetPlate(UnderlayPlateSlotData plate)
        {
            if (plate == null) return;
            _plates.RemoveAll(p => p == null || p.ObjectId == plate.ObjectId);
            if (!plate.IsEmpty) _plates.Add(plate.Clone());
        }

        /// <summary>指定オブジェクトの作業板スロットを消す。消したら true。</summary>
        public bool RemovePlate(ulong objectId) => _plates.RemoveAll(p => p == null || p.ObjectId == objectId) > 0;

        /// <summary>どの方向にも画像が無いか（作業板スロットは見ない）。</summary>
        public bool HasNoDirectionImages
        {
            get
            {
                foreach (var s in _slots) if (!s.IsEmpty) return false;
                return true;
            }
        }

        /// <summary>指定方向のスロット。必ず非 null。</summary>
        public UnderlaySlotData Get(UnderlayDirection dir) => _slots[(int)dir];

        /// <summary>指定方向のスロットを差し替える。null は空のスロットにする。</summary>
        public void Set(UnderlayDirection dir, UnderlaySlotData slot)
            => _slots[(int)dir] = slot != null ? slot.Clone() : new UnderlaySlotData();

        /// <summary>どの方向にも作業板スロットにも画像が無いか。</summary>
        public bool IsEmpty
        {
            get
            {
                if (!HasNoDirectionImages) return false;
                foreach (var p in _plates) if (p != null && !p.IsEmpty) return false;
                return true;
            }
        }

        /// <summary>ディープコピー。</summary>
        public UnderlayData Clone()
        {
            var d = new UnderlayData();
            for (int i = 0; i < Count; i++) d._slots[i] = _slots[i].Clone();
            foreach (var p in _plates) if (p != null) d._plates.Add(p.Clone());
            return d;
        }

        /// <summary>モデル座標の 2 隅で置く方向か（上下前後左右）。</summary>
        public static bool IsModelAnchored(UnderlayDirection dir)
            => dir != UnderlayDirection.Persp && dir != UnderlayDirection.Ortho;

        /// <summary>保存用の名前（列挙名）。</summary>
        public static string NameOf(UnderlayDirection dir) => dir.ToString();

        /// <summary>保存用の名前から方向を引く。大文字小文字は無視。</summary>
        public static bool TryParse(string name, out UnderlayDirection dir)
            => Enum.TryParse(name ?? "", true, out dir) && Enum.IsDefined(typeof(UnderlayDirection), dir);

        /// <summary>
        /// 画像を初めて置くときの 2 隅。原点を中心に、画像 1 画素 = unit（モデル座標）の大きさで
        /// その方向の見える 2 軸（前後 = XY、上下 = XZ、左右 = ZY）に広げる。
        /// </summary>
        public static void DefaultCorners(UnderlayDirection dir, int widthPx, int heightPx, float unit,
                                          out Vector3 corner0, out Vector3 corner1)
        {
            float hw = widthPx  * unit * 0.5f;
            float hh = heightPx * unit * 0.5f;
            switch (dir)
            {
                case UnderlayDirection.Top:
                case UnderlayDirection.Bottom:
                    corner0 = new Vector3(-hw, 0f, -hh); corner1 = new Vector3(hw, 0f, hh); return;
                case UnderlayDirection.Left:
                case UnderlayDirection.Right:
                    corner0 = new Vector3(0f, -hh, -hw); corner1 = new Vector3(0f, hh, hw); return;
                default:
                    corner0 = new Vector3(-hw, -hh, 0f); corner1 = new Vector3(hw, hh, 0f); return;
            }
        }
    }
}
