// EditorBridge.cs
// UnityEditor依存APIへのRuntimeアクセサ。
// Editor起動時にEditorBridgeImplが[InitializeOnLoad]で自動登録される。
// 未登録時（Runtime）はEditorBridgeNullが使われ、呼び出し時にエラーを出力する。

using UnityEngine;

namespace Poly_Ling.EditorBridge
{
    public static class PLEditorBridge
    {
        private static IEditorBridge _instance;


        //public Tools.ToolContext _ToolContext => null;


        /// <summary>
        /// ブリッジインスタンスを取得。
        /// 未登録時はEditorBridgeNull（警告出力スタブ）を返す。
        /// </summary>
        public static IEditorBridge I
        {
            get
            {
                if (_instance == null)
                    _instance = new EditorBridgeNull();
                return _instance;
            }
        }

        /// <summary>
        /// 実装を登録する。EditorBridgeImplのstaticコンストラクタから呼ばれる。
        /// </summary>
        public static void Register(IEditorBridge impl)
        {
            _instance = impl;
        }
    }
}
