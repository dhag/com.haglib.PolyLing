// IVrm10Importer.cs
// ============================================================
// VRM 1.0 インポータのインターフェース
// ============================================================
//
// 【分離規約】VRM パッケージとの依存関係の規約は IVrm10Exporter.cs 冒頭を正典とする。
//   読み込み側も同じ規約に従う。
//     ・PolyLing.Runtime は VRM パッケージに依存しない。
//     ・このインターフェースに VRM の型を出さない。
//       やり取りは PolyLing の型（Vrm10ImportSettings / Vrm10ImportResult）だけで行う。
//     ・実装は PolyLing.Vrm10 に置き、[RuntimeInitializeOnLoadMethod] で
//       PLVrm10ImportBridge.Register する。Play 時にしか走らないため、
//       Editor 拡張（非 Play）からの読み込みは当面できない。
//
// 【GameObject を作らない】
//   実装は GLB を解析して VrmLib.Model（UniVRM が Unity 座標へ直したもの）と
//   VRM 拡張データから ModelContext の材料を直接組む。
//   Unity の Material / GameObject は UniVRM に作らせない。
//
// ============================================================

namespace Poly_Ling.Vrm
{
    /// <summary>
    /// VRM 1.0 インポータ。規約は本ファイル冒頭のコメントを正典とする。
    /// </summary>
    public interface IVrm10Importer
    {
        /// <summary>
        /// 実際に読み込める実装が登録されているか。
        /// false のとき UI は VRM 読み込みを選ばせないこと。
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// VRM（.vrm / GLB）を読み込み、ModelContext の材料を返す。
        /// </summary>
        /// <param name="filePath">読み込むファイル。</param>
        /// <param name="settings">読み込み設定。null なら既定値。</param>
        Vrm10ImportResult Import(string filePath, Vrm10ImportSettings settings);
    }
}
