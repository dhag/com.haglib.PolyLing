// Runtime/Poly_Ling_Main/Tools/Deformers/IDeformerPhase.cs
// 複製ごとに歪みの「位相」を進めたい呼び出し側のための追加インターフェース。
//
// 【なぜ IMeshDeformer に入れないか】
//   位相は「s の関数として周期を持つ写像」にしか意味が無い。回転や曲げには
//   対応する概念が無いため、IMeshDeformer の必須メンバにすると実装できない
//   デフォーマへ無意味なメンバを強制することになる。
//   歪みは「任意のメソッドにできる」ことが前提なので、必須要件を増やさず、
//   対応できるデフォーマだけがこの小さな契約を追加で満たす形にしてある。
//
// 【呼び出し側の約束】
//   ・as IDeformerPhase で試し、null なら位相ステップは無視する
//     （位置ステップだけが効く。複製は全部同じ形になる）。
//   ・PhaseDeg は「利用者が設定した位相へさらに足すオフセット」ではなく、
//     デフォーマ自身が持つ位相そのもの。呼び出し側は元の値を控えて、
//     使い終わったら必ず書き戻すこと（ObjectArrayGenerator がそうしている）。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/ に配置

namespace Poly_Ling.Tools.Deformers
{
    /// <summary>周期的な歪みの位相を外から進められるデフォーマ。</summary>
    public interface IDeformerPhase
    {
        /// <summary>位相（度）。360 で 1 周。</summary>
        float PhaseDeg { get; set; }
    }
}
