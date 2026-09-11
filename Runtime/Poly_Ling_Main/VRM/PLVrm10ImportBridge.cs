// PLVrm10ImportBridge.cs
// VRM 1.0 インポータへの Runtime アクセサ。
// 規約は IVrm10Importer.cs 冒頭のコメントを正典とする。
//
// PLVrm10Bridge（エクスポータ）と同じ形。
// 未登録時は Vrm10ImporterNull（IsAvailable == false）を返す。

namespace Poly_Ling.Vrm
{
    public static class PLVrm10ImportBridge
    {
        private static IVrm10Importer _instance;

        /// <summary>
        /// インポータ実装を取得。未登録時は Vrm10ImporterNull を返す。
        /// </summary>
        public static IVrm10Importer I
        {
            get
            {
                if (_instance == null)
                    _instance = new Vrm10ImporterNull();
                return _instance;
            }
        }

        /// <summary>
        /// 実装を登録する。PolyLing.Vrm10 側の
        /// [RuntimeInitializeOnLoadMethod] から呼ばれる。
        /// </summary>
        public static void Register(IVrm10Importer impl)
        {
            _instance = impl;
        }
    }
}
