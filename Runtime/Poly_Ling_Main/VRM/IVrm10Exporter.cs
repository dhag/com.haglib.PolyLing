// IVrm10Exporter.cs
// ============================================================
// VRM 1.0 エクスポートの分離規約（正典）
// ============================================================
//
// 【この規約の位置づけ】
//   VRM パッケージ（com.vrmc.vrm / com.vrmc.gltf）との依存関係についての規約は、
//   本ファイルを正典とする。他のファイルはここを参照し、規約を書き写さない。
//
// ============================================================
// 1. PolyLing.Runtime は VRM パッケージに依存してはならない
// ============================================================
//
//   VRM パッケージが導入されていない環境でも PolyLing 本体は動作する必要がある。
//   したがって PolyLing.Runtime.asmdef の references に
//   UniGLTF / UniHumanoid / VrmLib / VRM10 を入れてはならない。
//   参照を1つでも入れると、パッケージが無い環境でアセンブリ全体が壊れる。
//
//   VRM 実装は別アセンブリ PolyLing.Vrm10 に置き、
//   asmdef の versionDefines で com.vrmc.vrm を検出して POLYLING_HAS_VRM10 を立て、
//   defineConstraints に同シンボルを指定する。
//   これでパッケージが無ければアセンブリごとコンパイル対象から外れ、本体は無傷になる。
//
// ============================================================
// 2. このインターフェースに VRM の型を露出させてはならない
// ============================================================
//
//   引数・戻り値・プロパティに UniGLTF / VrmLib / UniVRM10 の型を出すと、
//   PolyLing.Runtime がそれらの型を解決できなければならなくなり、1. が崩れる。
//   やり取りは UnityEngine の型と PolyLing の型（ModelContext / Vrm10Export*）だけで行う。
//
// ============================================================
// 3. 書き出しに UnityEditor は不要
// ============================================================
//
//   UniVRM の Vrm10Exporter は Runtime アセンブリのクラスで、
//   GLB のバイト列を返す。ファイル書き出しは File.WriteAllBytes で足りる。
//   したがって本経路に UnityEditor 依存を持ち込まないこと。
//   保存ダイアログだけは既存の IEditorBridge を通す（Windows は Player でも動く）。
//
// ============================================================
// 4. 登録は RuntimeInitializeOnLoadMethod のみ（当面）
// ============================================================
//
//   実装は PolyLing.Vrm10 側の [RuntimeInitializeOnLoadMethod] で
//   PLVrm10Bridge.Register する。これは Play 時にしか走らないため、
//   Editor 拡張（非 Play）からの VRM エクスポートは当面できない。
//   これは承知のうえの制限であり、Editor 対応を足す場合は
//   PolyLing.Vrm10.Editor アセンブリを別に作ること
//   （PolyLing.Vrm10 に #if UNITY_EDITOR を入れて解決しない）。
//
// ============================================================

namespace Poly_Ling.Vrm
{
    /// <summary>
    /// VRM 1.0 エクスポータのインターフェース。
    /// 規約は本ファイル冒頭のコメントを正典とする。
    /// </summary>
    public interface IVrm10Exporter
    {
        /// <summary>
        /// 実際に書き出せる実装が登録されているか。
        /// false のとき UI は VRM 出力を選ばせないこと。
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// ModelContext を VRM 1.0（.vrm / GLB）として書き出す。
        /// </summary>
        /// <param name="model">出力元。Poly_Ling.Context.ModelContext。</param>
        /// <param name="outputPath">出力先ファイルパス。</param>
        /// <param name="settings">出力設定。null なら既定値。</param>
        Vrm10ExportResult Export(
            Poly_Ling.Context.ModelContext model, string outputPath, Vrm10ExportSettings settings);
    }
}
