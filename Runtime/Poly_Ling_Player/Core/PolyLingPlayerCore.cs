// PolyLingPlayerCore.cs
// プレイヤービルド用 MonoBehaviour
// PolyLingCore（ロジック）と RemoteServerCore をホストする
// Runtime/Poly_Ling_Player/Core/ に配置

using UnityEngine;
using Poly_Ling.Core;
using Poly_Ling.EditorBridge;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PolyLingPlayerCore : MonoBehaviour
    {
        // ================================================================
        // Inspector設定
        // ================================================================

        [SerializeField] private int _serverPort = 8765;
        [SerializeField] private bool _autoStartServer = true;

        // ================================================================
        // コア
        // ================================================================

        private Poly_Ling.Core.PolyLingCore _core;
        public  Poly_Ling.Core.PolyLingCore Core => _core;

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void Awake()
        {
            // PlayerBridgeを登録（EditorBridgeImplの代わり）
            PLEditorBridge.Register(new PolyLingPlayerBridge());

            // PolyLingCore初期化
            _core = new Poly_Ling.Core.PolyLingCore();
            _core.Initialize(PolyLingCoreConfig.CreateStub());

            Debug.Log("[PolyLingPlayerCore] Core initialized.");
        }

        private void OnDestroy()
        {
            _core?.Dispose();
            _core = null;
        }

        // ================================================================
        // 公開API
        // ================================================================

        public void DispatchCommand(PanelCommand cmd)
        {
            _core?.DispatchPanelCommand(cmd);
        }
    }
}
