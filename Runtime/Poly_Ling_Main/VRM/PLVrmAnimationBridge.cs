// PLVrmAnimationBridge.cs
// VRM アニメーション（.vrma）エクスポータへの Runtime アクセサ。
// 規約は IVrm10Exporter.cs 冒頭のコメントを正典とする。
//
// PLVrm10Bridge（PLVrm10Bridge.cs）と同じ形。
// 未登録時は VrmAnimationExporterNull（IsAvailable == false）を返す。

namespace Poly_Ling.Vrm
{
    public static class PLVrmAnimationBridge
    {
        private static IVrmAnimationExporter _instance;

        /// <summary>
        /// エクスポータ実装を取得。未登録時は VrmAnimationExporterNull を返す。
        /// </summary>
        public static IVrmAnimationExporter I
        {
            get
            {
                if (_instance == null)
                    _instance = new VrmAnimationExporterNull();
                return _instance;
            }
        }

        /// <summary>
        /// 実装を登録する。PolyLing.Vrm10 側の
        /// [RuntimeInitializeOnLoadMethod] から呼ばれる。
        /// </summary>
        public static void Register(IVrmAnimationExporter impl)
        {
            _instance = impl;
        }
    }
}
