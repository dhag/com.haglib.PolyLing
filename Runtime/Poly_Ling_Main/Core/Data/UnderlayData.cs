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
//   従来どおり画面ピクセル基準（左上位置・拡大縮小の原点・2D スケール）で置く。
//
// 【2 隅】
//   Corner0 / Corner1 は画像の向かい合う 2 隅のモデル座標。どちらがどの隅かは問わない。
//   表示ではそのビューへ投影した 2 点が張る矩形に画像を敷く。
//   視線方向の成分は表示に使わない。
//
// 【依存】
//   #if UNITY_EDITOR を含まない。

using System;
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

        // ── 画面ピクセル基準（Persp / Ortho） ──

        /// <summary>パネル左上からの表示位置（px）。</summary>
        public Vector2 TopLeft = Vector2.zero;

        /// <summary>拡大縮小の原点（画像ローカル px、Y 下向き）。</summary>
        public Vector2 ScaleOrigin = Vector2.zero;

        /// <summary>2D スケール（x, y）。</summary>
        public Vector2 Scale = Vector2.one;

        // ── モデル座標基準（上下前後左右） ──

        /// <summary>画像の 1 隅のモデル座標。</summary>
        public Vector3 Corner0 = Vector3.zero;

        /// <summary>Corner0 と向かい合う隅のモデル座標。</summary>
        public Vector3 Corner1 = Vector3.zero;

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
            Corner0     = Corner0,
            Corner1     = Corner1,
        };
    }

    /// <summary>8 方向分の下絵設定。ModelContext.Underlay として 1 つ持つ。null＝下絵なし。</summary>
    [Serializable]
    public class UnderlayData
    {
        /// <summary>方向の数。</summary>
        public const int Count = 8;

        private readonly UnderlaySlotData[] _slots;

        public UnderlayData()
        {
            _slots = new UnderlaySlotData[Count];
            for (int i = 0; i < Count; i++) _slots[i] = new UnderlaySlotData();
        }

        /// <summary>指定方向のスロット。必ず非 null。</summary>
        public UnderlaySlotData Get(UnderlayDirection dir) => _slots[(int)dir];

        /// <summary>指定方向のスロットを差し替える。null は空のスロットにする。</summary>
        public void Set(UnderlayDirection dir, UnderlaySlotData slot)
            => _slots[(int)dir] = slot != null ? slot.Clone() : new UnderlaySlotData();

        /// <summary>どの方向にも画像が無いか。</summary>
        public bool IsEmpty
        {
            get
            {
                foreach (var s in _slots) if (!s.IsEmpty) return false;
                return true;
            }
        }

        /// <summary>ディープコピー。</summary>
        public UnderlayData Clone()
        {
            var d = new UnderlayData();
            for (int i = 0; i < Count; i++) d._slots[i] = _slots[i].Clone();
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
