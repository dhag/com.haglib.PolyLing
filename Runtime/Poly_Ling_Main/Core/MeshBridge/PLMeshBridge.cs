namespace Poly_Ling.MeshBridge
{
    // ================================================================
    // PLMeshBridge
    // ----------------------------------------------------------------
    // IMeshBridge 実装への単一アクセスポイント。
    //
    //   PLMeshBridge.I.ToUnityMesh(meshObject) のように使う。
    //
    // 既定では MeshBridgeDefault を遅延生成する。MeshBridgeDefault の中身は
    // すべて UnityEngine ランタイムAPI（Editor専用APIを含まない）なので、
    // Editor でも Player でも追加配線なしで動作する。
    // （AssetDatabase 等の Editor 専用処理は別系統の EditorBridge が担当する。
    //   このブリッジは Mesh 生成専用であり、それらとは責務を分ける。）
    //
    // 命名: 名前空間 Poly_Ling.MeshBridge と型名の衝突を避けるため PL 接頭辞を付ける
    //       （既存の PLEditorBridge と同じ規約）。
    //
    // I に別実装を代入すれば全呼び出し元の変換挙動を差し替えられる:
    //   - テスト用のモック
    //   - Mesh.SetVertexBufferData を使う低レベル高速実装
    //   などへの切り替え点として機能する。
    // ================================================================
    public static class PLMeshBridge
    {
        private static IMeshBridge _instance;

        /// <summary>
        /// 現在の Mesh ブリッジ実装。未設定なら MeshBridgeDefault を生成して返す。
        /// </summary>
        public static IMeshBridge I
        {
            get
            {
                if (_instance == null)
                    _instance = new MeshBridgeDefault();
                return _instance;
            }
            set { _instance = value; }
        }
    }
}
