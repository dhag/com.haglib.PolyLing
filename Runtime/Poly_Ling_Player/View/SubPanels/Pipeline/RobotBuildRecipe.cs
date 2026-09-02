// RobotBuildRecipe.cs
// ロボ組み立て自動検証で置く部位の定義表。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【数値の出どころ】
//   手作業で作った StickRobot（六角棒ろぼ_Mesh_蓋なし）の頂点座標から逆算した。
//   各メッシュの頂点を軸方向で 2 つのリングに分け、リング間距離を高さ、
//   軸に垂直な成分の長さを半径として求めている。
//   足以外の 16 メッシュはリング内のばらつきが 0 で、素の正六角柱と完全に一致する。
//
//   足だけは半径が 0.0567〜0.0634 とばらついていた（手で頂点を動かした跡）。
//   自動検証は素の図形から積むので、中間の 0.06 を採る。
//
// 【回転を焼き込む理由】
//   手作業データは BoneTransform.Rotation が全部位ゼロで、腕は X 方向・足は
//   下向きに伸びている。つまり回転は頂点へ焼き込まれている。
//   同じ状態にするため PrimitivePlacement.BakeRotation = true で入れる。
//
// 【胴を小判型にしたときの寸法】
//   もとの六角柱の外接円半径から Depth を決めると Length と同値になり、
//   a = Length/2 - Depth/2 が 0 になって直線部が消える（StadiumBoxMeshGenerator.cs:180）。
//   胴として横長にするため Depth を小さく採る。
//
// 【分割数】
//   LengthSegments = 3（奇数）… センター下部を X 中央で仕切るとき、
//     前縁・後縁とも x=0 に辺の中点が来て左右対称に割れる。偶数だとずれる。
//   CapSegments = 3          … 下部開口が 12 点になり、仕切ると 6 / 6 に割れる。
//   上半身2 の HeightSegments = 3 … 側面の中段だけを落とせば、腕用の穴が
//     上蓋とも下部開口ともつながらずに独立して開く（左右 8 点ずつ）。
//     1 段だと下部開口と一続きに破れ、最上段だと上蓋の穴とつながる。

using UnityEngine;

namespace Poly_Ling.Player
{
    /// <summary>部位ひとつぶんの定義。</summary>
    public struct RobotPart
    {
        public string Name;

        /// <summary>階層の親。空ならルート。</summary>
        public string Parent;

        /// <summary>オブジェクト原点（親からの相対）。</summary>
        public Vector3 Origin;

        /// <summary>頂点へ焼き込む回転（度）。</summary>
        public Vector3 BakeRotation;

        /// <summary>小判型か（false なら六角柱）。</summary>
        public bool Stadium;

        // ── 六角柱 ──
        public float Radius;

        // ── 小判型 ──
        public float Length;
        public float Depth;
        public int   HeightSegments;
        public bool  CapTop;

        /// <summary>共通。軸方向の長さ。</summary>
        public float Height;

        /// <summary>ピボット。底なら (0,-0.5,0)、天なら (0,+0.5,0)。</summary>
        public Vector3 Pivot;
    }

    /// <summary>組み立てる部位の一覧と、系統ごとの取捨。</summary>
    public static class RobotBuildRecipe
    {
        /// <summary>小判型の共通分割数。</summary>
        public const int StadiumLengthSegments = 3;
        public const int StadiumCapSegments    = 3;

        /// <summary>六角柱の円周分割数。</summary>
        public const int PrismRadialSegments = 6;

        /// <summary>
        /// 穴つなぎの中間分割数。
        /// 0 だと 2 つの縁を面 1 枚でつなぐだけになり、曲げたときに折れる。
        /// 中間の輪を入れておくと、関節側のボーンへウェイトを配れる。
        /// </summary>
        public const int BridgeSubdivisions = 3;

        private static readonly Vector3 PivotBottom = new Vector3(0f, -0.5f, 0f);
        private static readonly Vector3 PivotTop    = new Vector3(0f,  0.5f, 0f);

        private static RobotPart Stadium(
            string name, string parent, Vector3 origin,
            float length, float height, float depth, int heightSeg, bool capTop)
            => new RobotPart
            {
                Name = name, Parent = parent, Origin = origin,
                BakeRotation = Vector3.zero,
                Stadium = true,
                Length = length, Height = height, Depth = depth,
                HeightSegments = heightSeg, CapTop = capTop,
                Pivot = PivotBottom,
            };

        private static RobotPart Prism(
            string name, string parent, Vector3 origin,
            float radius, float height, Vector3 bakeRotation, bool pivotTop = false)
            => new RobotPart
            {
                Name = name, Parent = parent, Origin = origin,
                BakeRotation = bakeRotation,
                Stadium = false,
                Radius = radius, Height = height,
                Pivot = pivotTop ? PivotTop : PivotBottom,
            };

        // ================================================================
        // 部位一覧
        //   並び順がそのまま生成順・階層の親子付け順になる。親は必ず先に置く。
        // ================================================================

        public static readonly RobotPart[] All = new RobotPart[]
        {
            // ── 胴（小判型）──
            Stadium("センター", "",       new Vector3( 0f,     0.90f, 0f), 0.2200f, 0.1000f, 0.1200f, 1, false),
            Stadium("上半身",   "センター", new Vector3( 0f,     0.19f, 0f), 0.2000f, 0.1800f, 0.1100f, 1, false),
            Stadium("上半身2",  "上半身",   new Vector3( 0f,     0.27f, 0f), 0.2200f, 0.1800f, 0.1200f, 3, true),

            // ── 首・頭 ──
            Prism("首", "上半身2", new Vector3(0f, 0.25f, 0f), 0.0350f, 0.0800f, Vector3.zero),
            Prism("頭", "首",      new Vector3(0f, 0.15f, 0f), 0.1000f, 0.2000f, Vector3.zero),

            // ── 腕（Z 軸まわりに倒して X 方向へ）──
            Prism("左腕",   "上半身2", new Vector3(-0.1853f, 0.16f, 0f), 0.0450f, 0.1500f, new Vector3(0f, 0f,  90f)),
            Prism("左ひじ", "左腕",    new Vector3(-0.2400f, 0f,    0f), 0.0400f, 0.1350f, new Vector3(0f, 0f,  90f)),
            Prism("左手首", "左ひじ",  new Vector3(-0.2250f, 0f,    0f), 0.0450f, 0.0950f, new Vector3(0f, 0f,  90f)),
            Prism("右腕",   "上半身2", new Vector3( 0.1853f, 0.16f, 0f), 0.0450f, 0.1500f, new Vector3(0f, 0f, -90f)),
            Prism("右ひじ", "右腕",    new Vector3( 0.2400f, 0f,    0f), 0.0400f, 0.1350f, new Vector3(0f, 0f, -90f)),
            Prism("右手首", "右ひじ",  new Vector3( 0.2250f, 0f,    0f), 0.0450f, 0.0950f, new Vector3(0f, 0f, -90f)),

            // ── 脚 ──
            //   円柱は底ピボットで +Y へ伸びる。Z 軸 180° で下向きにする。
            //   ピボットを天にすると生成時点で既に下を向くため、回転と二重に効いて
            //   上を向いてしまう。手作業と同じく「底で作って 180 回す」に揃える。
            //
            //   足首だけは前向き。+Y を +Z へ向けるので X 軸 +90°。
            //   （Rx(+90): (0,1,0) → (0,0,1)。手作業データも z が 0〜0.1709 で前向き）
            Prism("左足",   "センター", new Vector3(-0.0900f, -0.09f, 0f), 0.0600f, 0.3100f, new Vector3(0f, 0f, 180f)),
            Prism("左ひざ", "左足",     new Vector3(-0.0100f, -0.40f, 0f), 0.0500f, 0.3100f, new Vector3(0f, 0f, 180f)),
            Prism("左足首", "左ひざ",   new Vector3( 0f,      -0.40f, 0f), 0.0400f, 0.1709f, new Vector3(90f, 0f, 0f)),
            Prism("右足",   "センター", new Vector3( 0.0900f, -0.09f, 0f), 0.0600f, 0.3100f, new Vector3(0f, 0f, 180f)),
            Prism("右ひざ", "右足",     new Vector3( 0.0100f, -0.40f, 0f), 0.0500f, 0.3100f, new Vector3(0f, 0f, 180f)),
            Prism("右足首", "右ひざ",   new Vector3( 0f,      -0.40f, 0f), 0.0400f, 0.1709f, new Vector3(90f, 0f, 0f)),
        };

        // ================================================================
        // 系統ごとの取捨
        // ================================================================

        /// <summary>片側上半身のみ。ミラーは使わず、左だけを置く。</summary>
        public static readonly string[] LeftUpperBody =
        {
            "センター", "上半身", "上半身2", "左腕", "左ひじ", "左手首",
        };

        /// <summary>両側上半身のみ。左を置いてミラーで右を出す。</summary>
        public static readonly string[] BothUpperBody =
        {
            "センター", "上半身", "上半身2", "首", "頭", "左腕", "左ひじ", "左手首",
        };

        /// <summary>両側全身。左を置いてミラーで右を出す。</summary>
        public static readonly string[] BothFullBody =
        {
            "センター", "上半身", "上半身2", "首", "頭",
            "左腕", "左ひじ", "左手首",
            "左足", "左ひざ", "左足首",
        };

        /// <summary>ミラーの分岐ルートにする部位。ここから先が枝として左右に出る。</summary>
        public static readonly string[] MirrorBranchRoots = { "左腕", "左足" };

        /// <summary>名前で引く。見つからなければ Name が空の既定値。</summary>
        public static RobotPart Find(string name)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Name == name) return All[i];
            return default;
        }

        /// <summary>
        /// 親をたどって積み上げたワールド絶対位置。
        ///
        /// 定義表の Origin は親からの相対値。階層を張る前は親が居ないので、
        /// 姿勢の段ではこの絶対位置を入れる。階層を張った時点で
        /// ReorderMeshesCommand.PreserveWorldTransform により相対値へ組み直される。
        /// </summary>
        public static Vector3 WorldOrigin(string name)
        {
            Vector3 sum = Vector3.zero;
            string cur = name;
            for (int guard = 0; guard < 64; guard++)
            {
                var p = Find(cur);
                if (string.IsNullOrEmpty(p.Name)) break;
                sum += p.Origin;
                if (string.IsNullOrEmpty(p.Parent)) break;
                cur = p.Parent;
            }
            return sum;
        }

        /// <summary>親をたどって数えた階層の深さ。ルートは 0。</summary>
        public static int Depth(string name)
        {
            int depth = 0;
            string cur = name;
            for (int guard = 0; guard < 64; guard++)
            {
                var p = Find(cur);
                if (string.IsNullOrEmpty(p.Name) || string.IsNullOrEmpty(p.Parent)) return depth;
                depth++;
                cur = p.Parent;
            }
            return 0;
        }
    }
}
