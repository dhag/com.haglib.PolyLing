using UnityEngine;

namespace Poly_Ling.MaterialBridge
{
    // ================================================================
    // MaterialBridgeDefault
    // ----------------------------------------------------------------
    // IMaterialBridge の既定実装。
    // 「生の Unity Material API（new Material 等）」を呼ぶ唯一の場所。
    //
    // ここに含まれる処理は UnityEngine ランタイムAPI のみで、UnityEditor 依存を含まない。
    // よって Editor でも Player でも追加配線なしで動作する。
    // （Editor 依存のアセット操作は IEditorBridge/PLEditorBridge が担当する。
    //   両者の責務分離の理由は IMaterialBridge のコメントを参照。）
    // ================================================================
    public class MaterialBridgeDefault : IMaterialBridge
    {
        // 生 Unity API: new Material(src) による複製。
        // 複製は Editor/Player で同一挙動のため、ここに集約して直接呼ぶ。
        public Material Clone(Material src)
        {
            if (src == null) return null;
            return new Material(src);
        }
    }
}
