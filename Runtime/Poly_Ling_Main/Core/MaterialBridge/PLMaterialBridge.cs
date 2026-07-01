namespace Poly_Ling.MaterialBridge
{
    // ================================================================
    // PLMaterialBridge
    // ----------------------------------------------------------------
    // IMaterialBridge 実装への単一アクセスポイント。
    //
    //   PLMaterialBridge.I.Clone(material) のように使う。
    //
    // 既定では MaterialBridgeDefault を遅延生成する（Editor/Player 双方で動作）。
    //
    // 命名: 名前空間 Poly_Ling.MaterialBridge と型名の衝突を避けるため PL 接頭辞を付ける
    //       （既存の PLEditorBridge / PLMeshBridge と同じ規約）。
    //
    // I に別実装を代入すれば全呼び出し元の Material 操作挙動を差し替えられる
    //   （テスト用モック等の差し込み点）。
    // ================================================================
    public static class PLMaterialBridge
    {
        private static IMaterialBridge _instance;

        /// <summary>
        /// 現在の Material ブリッジ実装。未設定なら MaterialBridgeDefault を生成して返す。
        /// </summary>
        public static IMaterialBridge I
        {
            get
            {
                if (_instance == null)
                    _instance = new MaterialBridgeDefault();
                return _instance;
            }
            set { _instance = value; }
        }
    }
}
