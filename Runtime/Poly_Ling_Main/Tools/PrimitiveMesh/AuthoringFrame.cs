// AuthoringFrame.cs
// 2D 編集面・外部データをワールドへ載せるときの座標規約（唯一の基準）。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// ================================================================
// 【規約】
// ================================================================
// ワールドは Unity 左手系。モデルの正面は +Z。
//
// 各正投影ビューの基底は OrthoViewController.BaseRotation() の
// クォータニオンから算出した実測値。カメラの right / up は
// rot * (1,0,0) / rot * (0,1,0)。
//
//   ビュー    Euler          画面右   画面上   視線
//   Front   (0, 180, 0)      -X      +Y      -Z
//   Back    (0,   0, 0)      +X      +Y      +Z
//   Top     (90, 180, 0)     -X      +Z      -Y
//   Bottom  (-90,180, 0)     -X      -Z      +Y
//   Right   (0, -90, 0)      +Z      +Y      -X
//   Left    (0,  90, 0)      -Z      +Y      +X
//
// つまり「正面から見たときの画面右はワールド -X」。
// モデルが +Z を向いているので、本人の右半身（+X 側）が画面左に出るのが正しい。
//
// ================================================================
// 【帰結】2D 編集面・右手系データの取り込みは行列式 -1 でなければならない
// ================================================================
// 2D の編集面は「x 右・y 上」で描く。これを +Z 側から見るのだから、
// 非鏡像で取り込むには x -> -X, y -> +Y に載せる必要がある（行列式 -1）。
// x -> +X（行列式 +1）で載せると必ず左右が鏡像になる。
//
// 同じ理由で、右手系の外部データ（MediaPipe, MQO 等）を左手系ワールドへ
// 取り込むときも、変換全体の行列式は -1 でなければならない。
// 行列式 +1 の成分割り当ては、たとえ各軸の符号が自然に見えても鏡像になる。
//
// 左右対称な図形ではこの誤りは見えない。非対称な図形（文字・顔・
// 非対称プロファイル）でのみ表面化する。個別の Flip スイッチで
// 打ち消すのではなく、取り込み時にこの規約へ従うこと。
//
// ================================================================
// 【新しい形状を足すときの検査】
// ================================================================
// 次の 3 項目は独立している。1 つ通っても他は保証されない。
//   1. 内部整合 : cross(p1-p0, p2-p0) と宣言頂点法線が同符号か
//                （メッシュ単体で判定可。ただし「どの軸へ載せたか」には無反応）
//   2. 外向き   : 巻き順が立体の外を向くか
//                （閉じた立体でのみ定義。1 枚面や厚み 0 では判定不能）
//   3. 絶対姿勢 : 正面ビュー基底に対して前後・左右が正しいか
//                （外部基準が要る。1 と 2 では絶対に検出できない）

using UnityEngine;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>
    /// 2D 編集面・外部データをワールドへ載せるときの座標規約。
    /// 詳細はファイル冒頭のコメントを参照。
    /// </summary>
    public static class AuthoringFrame
    {
        // ── 正投影ビューの基底（OrthoViewController.BaseRotation() の実測値） ──

        public static readonly Vector3 FrontViewRight  = new Vector3(-1f, 0f,  0f);
        public static readonly Vector3 FrontViewUp     = new Vector3( 0f, 1f,  0f);

        public static readonly Vector3 BackViewRight   = new Vector3( 1f, 0f,  0f);
        public static readonly Vector3 BackViewUp      = new Vector3( 0f, 1f,  0f);

        public static readonly Vector3 TopViewRight    = new Vector3(-1f, 0f,  0f);
        public static readonly Vector3 TopViewUp       = new Vector3( 0f, 0f,  1f);

        public static readonly Vector3 BottomViewRight = new Vector3(-1f, 0f,  0f);
        public static readonly Vector3 BottomViewUp    = new Vector3( 0f, 0f, -1f);

        public static readonly Vector3 RightViewRight  = new Vector3( 0f, 0f,  1f);
        public static readonly Vector3 RightViewUp     = new Vector3( 0f, 1f,  0f);

        public static readonly Vector3 LeftViewRight   = new Vector3( 0f, 0f, -1f);
        public static readonly Vector3 LeftViewUp      = new Vector3( 0f, 1f,  0f);

        /// <summary>
        /// 2D 編集面の点（x 右・y 上）を、正面ビューで編集画面どおりに見える
        /// ワールド座標へ載せる。x -> -X, y -> +Y（行列式 -1）。
        /// </summary>
        public static Vector3 Screen2DToWorld(Vector2 p, float z = 0f)
            => new Vector3(-p.x, p.y, z);

        /// <summary>Screen2DToWorld の逆。ワールド座標を 2D 編集面の座標へ戻す。</summary>
        public static Vector2 WorldToScreen2D(Vector3 v)
            => new Vector2(-v.x, v.y);

        /// <summary>ワールド座標を、指定した基底の画面座標（右, 上）へ射影する。</summary>
        public static Vector2 WorldToViewScreen(Vector3 v, Vector3 viewRight, Vector3 viewUp)
            => new Vector2(Vector3.Dot(v, viewRight), Vector3.Dot(v, viewUp));
    }
}
