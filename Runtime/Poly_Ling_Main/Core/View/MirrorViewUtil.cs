// MirrorViewUtil.cs
// ミラー関連の表示文字列を一箇所にまとめる。
//
// 【値の定義（MeshContext.cs が正典）】
//   MirrorType : 0=なし, 1=分離, 2=結合   … MQO の mirror 属性と同値
//   MirrorAxis : 1=X, 2=Y, 4=Z            … MQO の mirror_axis 属性と同値
//
// MirrorType と MirrorAxis は別物であり、MirrorType を軸名に読み替えてはならない。
// Runtime/Poly_Ling_Main/Core/View/ に配置

namespace Poly_Ling.View
{
    public static class MirrorViewUtil
    {
        /// <summary>ミラーモードの数（なし／分離／結合）。切り替えの循環幅に使う。</summary>
        public const int MirrorTypeCount = 3;

        /// <summary>MirrorAxis を軸名1文字にする。定義外の値は "?"。</summary>
        public static string AxisLetter(int mirrorAxis)
        {
            switch (mirrorAxis)
            {
                case 1:  return "X";
                case 2:  return "Y";
                case 4:  return "Z";
                default: return "?";
            }
        }

        /// <summary>MirrorType の名称。定義外の値は不正であることを明示する。</summary>
        public static string TypeName(int mirrorType)
        {
            switch (mirrorType)
            {
                case 0:  return "なし";
                case 1:  return "分離";
                case 2:  return "結合";
                default: return $"不正({mirrorType})";
            }
        }

        /// <summary>
        /// 次のミラーモード。なし→分離→結合→なし。
        /// MeshContext.MirrorType に 3 以上は存在しないため、
        /// 循環は MirrorTypeCount で行う（3 を作ると MQO へ不正値が書き出される）。
        /// </summary>
        public static int NextType(int mirrorType)
        {
            int t = mirrorType % MirrorTypeCount;
            if (t < 0) t += MirrorTypeCount;
            return (t + 1) % MirrorTypeCount;
        }

        /// <summary>
        /// ミラーの有無だけを切り替える。
        ///   0（なし） → 1（分離）
        ///   1/2（あり） → 0（なし）
        /// 結合(2) にするのは詳細欄のモード選択で行う。⇆ ボタンからは 2 を作らない。
        /// </summary>
        public static int ToggleType(int mirrorType) => mirrorType > 0 ? 0 : 1;

        /// <summary>範囲外の値を 0..2 に丸める。</summary>
        public static int ClampType(int mirrorType)
        {
            int t = mirrorType % MirrorTypeCount;
            if (t < 0) t += MirrorTypeCount;
            return t;
        }
    }
}
