// PartsIdCounter.cs
// 基本図形生成でパーツIDを通し番号で採番するための共有カウンタ。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【使い方】1回の生成につき1個作り、複数の基準ベルトをまたいで同じインスタンスを渡す。
//   生成器はパーツを1つ作るごとに Take() を呼び、返った値をそのパーツの頂点へ書く。
//
// 【null の扱い】生成器へ null を渡した場合はパーツIDを書かない。
//   既存の呼び出し（採番を必要としない経路）の挙動を変えないための約束。

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>パーツIDの採番カウンタ。生成器へ渡して使う。</summary>
    public sealed class PartsIdCounter
    {
        /// <summary>次に割り当てるパーツID。</summary>
        public int Next;

        /// <summary>現在値を返して 1 進める。</summary>
        public int Take()
        {
            int id = Next;
            Next++;
            return id;
        }
    }
}
