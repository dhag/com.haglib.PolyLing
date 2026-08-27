// Runtime/Poly_Ling_Main/Tools/Deformers/DeformerRegistry.cs
// 全デフォーマの登録を一箇所で管理する。
// ToolRegistry.ToolFactories と同じ配列登録方式。追加は DeformerFactories へ1行。
//
// 【拡張の指針】
//   新しい変形は「作業軸ローカル空間で θ(s) や scale(s) をどう与えるか」に
//   帰着する。IMeshDeformer を1つ実装してここへ1行足せば増える。
//     ・ツイスト          : +Y まわりに θ(s) = k·s で回す
//     ・風になびく        : θ(s) = A·sin(ω·s + φ)。φ を時間で進めればアニメになる
//     ・オタマジャクシの尾: scale(s) のテーパーと曲げの合成
//
// Runtime/Poly_Ling_Main/Tools/Deformers/ に配置

using System;
using System.Collections.Generic;

namespace Poly_Ling.Tools.Deformers
{
    /// <summary>デフォーマの一元登録。</summary>
    public static class DeformerRegistry
    {
        // ================================================================
        // ファクトリ定義（登録順 = UI 表示順）
        // ================================================================

        public static readonly Func<IMeshDeformer>[] DeformerFactories = new Func<IMeshDeformer>[]
        {
            () => new RotateDeformer(),
            () => new MoveDeformer(),
            () => new ScaleDeformer(),
            () => new BendDeformer(),
            () => new TwistDeformer(),
            () => new WaveDeformer(),
        };

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>登録順に全デフォーマを生成する。</summary>
        public static List<IMeshDeformer> CreateAll()
        {
            var list = new List<IMeshDeformer>(DeformerFactories.Length);
            foreach (var factory in DeformerFactories)
            {
                var d = factory();
                if (d != null) list.Add(d);
            }
            return list;
        }

        /// <summary>Name で1つ生成する。見つからなければ null。</summary>
        public static IMeshDeformer Create(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            foreach (var factory in DeformerFactories)
            {
                var d = factory();
                if (d != null && d.Name == name) return d;
            }
            return null;
        }

        /// <summary>
        /// 登録済み Name の一覧。Name は内部 ID なので、UI へ出すのではなく
        /// 選択結果を Create へ渡すために使う。表示は GetDisplayNames。
        /// 両者の並び順は DeformerFactories と同じで、index が対応する。
        /// </summary>
        public static List<string> GetNames()
        {
            var names = new List<string>(DeformerFactories.Length);
            foreach (var factory in DeformerFactories)
            {
                var d = factory();
                if (d != null) names.Add(d.Name);
            }
            return names;
        }

        /// <summary>
        /// 登録済み DisplayName の一覧（UI のドロップダウン表示用）。
        /// GetNames と同じ並び順なので、選ばれた index で GetNames を引ける。
        /// </summary>
        public static List<string> GetDisplayNames()
        {
            var names = new List<string>(DeformerFactories.Length);
            foreach (var factory in DeformerFactories)
            {
                var d = factory();
                if (d != null) names.Add(d.DisplayName);
            }
            return names;
        }
    }
}
