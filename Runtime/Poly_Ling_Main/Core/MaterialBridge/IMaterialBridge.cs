using UnityEngine;

namespace Poly_Ling.MaterialBridge
{
    // ================================================================
    // IMaterialBridge
    // ----------------------------------------------------------------
    // Material に対する「生の Unity ランタイムAPI呼び出し」を1か所に集約する境界。
    // 現状は複製(Clone)のみだが、将来 Material 系のランタイム操作が増えても
    // ここに集める。
    //
    // ■ なぜ IEditorBridge ではなく独立した Material ブリッジなのか（重要・恒久メモ）
    //   IEditorBridge は「UnityEditor 依存API（AssetDatabase 等）を Runtime から隔離する」
    //   ためのインターフェースで、実装は Editor有無で“振る舞いが変わる”ことを前提とする。
    //   一方 Material の複製 `new Material(src)` は UnityEngine ランタイムAPIであり、
    //   Editor でも Player でも“まったく同じ動作”になる。Editor/非Editor で差が出ない処理を
    //   IEditorBridge に載せると、本来の目的（Editor依存の隔離）と意図がぶれ、
    //   後から読んだ人が「なぜ複製が Editor ブリッジに？」と混乱する。
    //   そのため Material 複製はこのランタイム専用ブリッジに置き、
    //   アセット保存(AssetDatabase.CreateAsset 等)の Editor 依存処理だけを
    //   IEditorBridge(PLEditorBridge) に残して責務を分離する。
    //   ※ この分離意図は崩さないこと。IEditorBridge へ統合し直さない。
    // ================================================================
    public interface IMaterialBridge
    {
        /// <summary>
        /// Material を複製する（UnityEngine の複製コンストラクタ相当）。
        /// src が null の場合は null を返す。
        /// </summary>
        Material Clone(Material src);
    }
}
