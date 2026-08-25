// RibbonBowParams.cs
// 蝶結びリボン（梯子群）の生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【方針】この段階では「梯子（四角形の帯）」だけを作る。
//   厚み・断面・波形は Frill / Pipe 側の仕事なので、ここには持たせない。
//   リボン紐の端の切り方（斜め切り・V字切り）も持たせない。
//
// 【寸法の基準】RibbonWidth を基準寸法とし、タグ三角のサイズはその比で指定する。
//   ループ・テール・ノットの寸法だけはモデル長さ単位でそのまま指定する。
//
// 【部品の取捨】BuildLoops / BuildTails / BuildKnot で部品ごとに作る・作らないを選べる。
//   多重リボンは「ループ抜きで1回、ループ付きで1回」と複数回に分けて生成し、
//   別のツールで結合して作る。パラメータを増やして1回で作る方式は採らない。

using System;
using UnityEngine;

namespace Poly_Ling.Ribbon
{
    /// <summary>ループの位相。</summary>
    public enum RibbonLoopTopology
    {
        /// <summary>
        /// 折り返しで面が裏返る型。実物のリボンはこちら。
        /// 帯を長手軸まわりに 180 度ねじり、往路は表・復路は裏が正面を向く平たい筒になる。
        /// 折り返しが幅方向の軸まわりの曲げになるため、帯幅に対してループが小さくても潰れない。
        /// </summary>
        Flip = 0,

        /// <summary>
        /// 面が裏返らない型。ねじりを持たず、ループの全長で表が正面を向く。
        /// 靴紐のように「帯がねじれずにループを作る」ものはこちら。
        /// 折り返しが平面内の曲げになるため、折り返し半径が帯の半幅を下回ると帯が細くなる。
        /// </summary>
        Flat = 1,
    }

    /// <summary>ループ 1つぶんの形状パラメータ。左右で共有する（左右対称固定）。</summary>
    [Serializable]
    public struct RibbonLoopParams : IEquatable<RibbonLoopParams>
    {
        /// <summary>中央から外側への張り出し量。</summary>
        public float Width;
        /// <summary>ループの上下方向の大きさ。</summary>
        public float Height;
        /// <summary>ループの下方向への下がり量。外側点と下側制御点の Y を下げる。</summary>
        public float Sag;
        /// <summary>ループの Z 方向の膨らみ。中間の制御点の Z を前へ出す。</summary>
        public float Depth;
        /// <summary>上側根元と下側根元の Y 間隔。</summary>
        public float RootGap;
        /// <summary>根元付近の幅倍率（1 で幅一定）。上下の根元の両方に掛かる。</summary>
        public float RootPinch;

        /// <summary>
        /// ループ全体の回転角（度）。根元の中点を軸中心に +Z 軸まわりへ回す。
        /// 左右とも正で外側が上がる。0 で回さない。
        /// Sag は折り返し点を下げるだけなので、折り返しをノットより上へ置くにはこちらを使う。
        /// </summary>
        public float Tilt;

        /// <summary>折り返しで面が裏返るか。</summary>
        public RibbonLoopTopology Topology;

        public bool Equals(RibbonLoopParams o)
            => Mathf.Approximately(Width,     o.Width)
            && Mathf.Approximately(Height,    o.Height)
            && Mathf.Approximately(Sag,       o.Sag)
            && Mathf.Approximately(Depth,     o.Depth)
            && Mathf.Approximately(RootGap,   o.RootGap)
            && Mathf.Approximately(RootPinch, o.RootPinch)
            && Mathf.Approximately(Tilt,      o.Tilt)
            && Topology == o.Topology;

        public override bool Equals(object obj) => obj is RibbonLoopParams p && Equals(p);
        public override int GetHashCode() => Width.GetHashCode();
    }

    /// <summary>テール 1本ぶんの形状パラメータ。左右で共有する（左右対称固定）。</summary>
    [Serializable]
    public struct RibbonTailParams : IEquatable<RibbonTailParams>
    {
        /// <summary>根元から先端までの縦方向の落差。</summary>
        public float Length;
        /// <summary>横方向への開き量。Length に対する比で効かせる（0 で真下）。</summary>
        public float Spread;
        /// <summary>途中の曲がり具合。中間制御点の Y を下げる。</summary>
        public float Sag;
        /// <summary>Z 方向の膨らみ。中間制御点の Z を前へ出す。</summary>
        public float Depth;
        /// <summary>先端の幅倍率（1 で幅一定）。</summary>
        public float Taper;

        /// <summary>
        /// 横方向の開きが最大になる位置（0=根元 / 1=先端）。
        /// Close が 0 のときは効かない。
        /// </summary>
        public float CloseAt;

        /// <summary>
        /// 最大開き点から先端までに中央側へ戻る割合。
        /// 0 で戻さない（開くだけ＝従来の形）、1 で根元と同じ X まで戻る。
        /// </summary>
        public float Close;

        public bool Equals(RibbonTailParams o)
            => Mathf.Approximately(Length, o.Length)
            && Mathf.Approximately(Spread, o.Spread)
            && Mathf.Approximately(Sag,    o.Sag)
            && Mathf.Approximately(Depth,   o.Depth)
            && Mathf.Approximately(Taper,   o.Taper)
            && Mathf.Approximately(CloseAt, o.CloseAt)
            && Mathf.Approximately(Close,   o.Close);

        public override bool Equals(object obj) => obj is RibbonTailParams p && Equals(p);
        public override int GetHashCode() => Length.GetHashCode();
    }

    /// <summary>中央のノット。短い帯の梯子1本として作る。</summary>
    [Serializable]
    public struct RibbonKnotParams : IEquatable<RibbonKnotParams>
    {
        /// <summary>帯の幅（rung 長）。</summary>
        public float Width;
        /// <summary>帯の長さ（下端から上端までの Y 方向の長さ）。</summary>
        public float Height;
        /// <summary>中間の Z 方向の膨らみ。</summary>
        public float Depth;

        public bool Equals(RibbonKnotParams o)
            => Mathf.Approximately(Width,  o.Width)
            && Mathf.Approximately(Height, o.Height)
            && Mathf.Approximately(Depth,  o.Depth);

        public override bool Equals(object obj) => obj is RibbonKnotParams p && Equals(p);
        public override int GetHashCode() => Width.GetHashCode();
    }

    /// <summary>蝶結びリボン（梯子群）の生成パラメータ。</summary>
    [Serializable]
    public struct RibbonBowParams : IEquatable<RibbonBowParams>
    {
        public string MeshName;

        /// <summary>帯の基準幅。テール・ループの幅と、タグ三角のサイズ基準になる。</summary>
        public float RibbonWidth;

        public RibbonLoopParams Loop;
        public RibbonTailParams Tail;
        public RibbonKnotParams Knot;

        // ── 部品の取捨 ──
        // 多重リボンは複数回に分けて生成し、別のツールで結合して作る。
        // 例: 1回目はループ抜き、2回目はループだけ、あとで結合。
        /// <summary>左右のループを作る。</summary>
        public bool BuildLoops;
        /// <summary>左右のテールを作る。</summary>
        public bool BuildTails;
        /// <summary>中央のノットを作る。</summary>
        public bool BuildKnot;

        // ── 分割数（梯子の rung 数 - 1） ──
        public int LoopSegments;
        public int TailSegments;
        public int KnotSegments;

        // ── 梯子タグ ──
        /// <summary>
        /// 開始タグ三角（BeltStackDetector の起点）を付ける。
        /// タグは開始三角の頂点 P に1点だけで接する必要があるため、
        /// これが true のときは開始三角も自動で付く。
        /// </summary>
        public bool AddStartTag;
        /// <summary>開始側の先端三角を付ける。</summary>
        public bool AddStartTip;
        /// <summary>終了側の先端三角を付ける。Pipe の点収束キャップの先端になる。</summary>
        public bool AddEndTip;
        /// <summary>先端三角の突き出し長（RibbonWidth 比）。</summary>
        public float TipLengthScale;
        /// <summary>開始タグ三角の大きさ（RibbonWidth 比）。</summary>
        public float TagSizeScale;

        // ── 面の向き ──
        /// <summary>生成後にメッシュ全体の面を反転する。</summary>
        public bool FlipFaces;

        // ── ピボット ──
        /// <summary>AABB サイズ基準のピボット。生成後に -Pivot * サイズ だけ平行移動する。</summary>
        public Vector3 Pivot;

        public static RibbonBowParams Default => new RibbonBowParams
        {
            MeshName    = "Ribbon",
            RibbonWidth = 0.30f,

            Loop = new RibbonLoopParams
            {
                Width     = 0.75f,
                Height    = 0.42f,
                Sag       = 0.06f,
                Depth     = 0.12f,
                RootGap   = 0.10f,
                RootPinch = 0.45f,
                Tilt      = 0f,
                Topology  = RibbonLoopTopology.Flip,
            },
            Tail = new RibbonTailParams
            {
                Length = 0.90f,
                Spread = 0.25f,
                Sag    = 0.15f,
                Depth   = 0.06f,
                Taper   = 0.95f,
                CloseAt = 0.65f,
                Close   = 0f,
            },
            Knot = new RibbonKnotParams
            {
                Width  = 0.16f,
                Height = 0.26f,
                Depth  = 0.09f,
            },

            BuildLoops = true,
            BuildTails = true,
            BuildKnot  = true,

            LoopSegments = 16,
            TailSegments = 10,
            KnotSegments = 4,

            AddStartTag    = true,
            AddStartTip    = true,
            AddEndTip      = true,
            TipLengthScale = 0.50f,
            TagSizeScale   = 0.40f,

            FlipFaces = false,
            Pivot     = Vector3.zero,
        };

        /// <summary>
        /// 異常値を補正した複製を返す。例外停止はせず、可能な範囲で丸める。
        /// </summary>
        public RibbonBowParams Normalized()
        {
            var p = this;

            const float MinWidth = 0.001f;

            // 図形生成パネルは生成時に許容 0.001 で重複頂点を結合する
            // （PlayerPrimitiveMeshSubPanel.Generate）。これを下回る寸法は潰れるため、
            // 頂点間隔として残したい最小値を持つ。
            const float MinMergeSafe = 0.004f;

            if (p.RibbonWidth < MinWidth) p.RibbonWidth = MinWidth;

            if (p.Loop.Width  < MinWidth) p.Loop.Width  = MinWidth;
            if (p.Loop.Height < 0f)       p.Loop.Height = 0f;
            // 上下の根元を同一点にしない（仕様 7.1）。同一点だと梯子の起点も重なり、
            // ループの最初と最後の rung が結合されて閉じた梯子になってしまう。
            float minGap = Mathf.Max(p.RibbonWidth * 0.05f, MinMergeSafe);
            if (p.Loop.RootGap < minGap) p.Loop.RootGap = minGap;
            // RootGap はループの縦幅を超えない（超えると根元がループの外へ出る）。
            if (p.Loop.Height > 0f && p.Loop.RootGap > p.Loop.Height)
                p.Loop.RootGap = p.Loop.Height;
            p.Loop.RootPinch = Mathf.Clamp(p.Loop.RootPinch, 0.05f, 1f);
            p.Loop.Tilt      = Mathf.Clamp(p.Loop.Tilt, -90f, 90f);

            // 絞った先の幅が結合許容を下回ると、その rung の左右2頂点が1点にまとまる。
            float minScale = Mathf.Min(1f, MinMergeSafe / p.RibbonWidth);
            if (p.Loop.RootPinch < minScale) p.Loop.RootPinch = minScale;

            if (p.Tail.Length < MinWidth) p.Tail.Length = MinWidth;
            p.Tail.Taper = Mathf.Clamp(p.Tail.Taper, 0.05f, 2f);
            if (p.Tail.Taper < minScale) p.Tail.Taper = minScale;

            // 最大開き点が端に寄りすぎると、その側のセグメントが潰れて接線が定まらない。
            p.Tail.CloseAt = Mathf.Clamp(p.Tail.CloseAt, 0.05f, 0.95f);
            p.Tail.Close   = Mathf.Clamp01(p.Tail.Close);

            if (p.Knot.Width  < MinWidth) p.Knot.Width  = MinWidth;
            if (p.Knot.Height < MinWidth) p.Knot.Height = MinWidth;

            if (p.LoopSegments < 2) p.LoopSegments = 2;
            if (p.TailSegments < 1) p.TailSegments = 1;
            if (p.KnotSegments < 1) p.KnotSegments = 1;

            p.TipLengthScale = Mathf.Max(0.02f, p.TipLengthScale);
            p.TagSizeScale   = Mathf.Max(0.02f, p.TagSizeScale);

            // 開始タグは開始三角の頂点へ1点で接することで成立する。
            // 開始三角が無いとタグの頂点がどの面にも属さず、検出条件を満たさない。
            if (p.AddStartTag) p.AddStartTip = true;

            return p;
        }

        public bool Equals(RibbonBowParams o)
            => MeshName == o.MeshName
            && Mathf.Approximately(RibbonWidth, o.RibbonWidth)
            && Loop.Equals(o.Loop)
            && Tail.Equals(o.Tail)
            && Knot.Equals(o.Knot)
            && BuildLoops == o.BuildLoops
            && BuildTails == o.BuildTails
            && BuildKnot  == o.BuildKnot
            && LoopSegments == o.LoopSegments
            && TailSegments == o.TailSegments
            && KnotSegments == o.KnotSegments
            && AddStartTag == o.AddStartTag
            && AddStartTip == o.AddStartTip
            && AddEndTip   == o.AddEndTip
            && Mathf.Approximately(TipLengthScale, o.TipLengthScale)
            && Mathf.Approximately(TagSizeScale,   o.TagSizeScale)
            && FlipFaces == o.FlipFaces
            && Pivot == o.Pivot;

        public override bool Equals(object obj) => obj is RibbonBowParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
