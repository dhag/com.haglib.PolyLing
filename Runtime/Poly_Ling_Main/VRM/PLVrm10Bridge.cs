// PLVrm10Bridge.cs
// VRM 1.0 エクスポータへの Runtime アクセサ。
// 規約は IVrm10Exporter.cs 冒頭のコメントを正典とする。
//
// PLEditorBridge（EditorBridge.cs）と同じ形。
// 未登録時は Vrm10ExporterNull（IsAvailable == false）を返す。

namespace Poly_Ling.Vrm
{
    public static class PLVrm10Bridge
    {
        private static IVrm10Exporter _instance;

        /// <summary>
        /// エクスポータ実装を取得。未登録時は Vrm10ExporterNull を返す。
        /// </summary>
        public static IVrm10Exporter I
        {
            get
            {
                if (_instance == null)
                    _instance = new Vrm10ExporterNull();
                return _instance;
            }
        }

        /// <summary>
        /// 実装を登録する。PolyLing.Vrm10 側の
        /// [RuntimeInitializeOnLoadMethod] から呼ばれる。
        /// </summary>
        public static void Register(IVrm10Exporter impl)
        {
            _instance = impl;
        }
    }
}
