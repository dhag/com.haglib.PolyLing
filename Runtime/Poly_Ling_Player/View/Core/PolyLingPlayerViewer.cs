// PolyLingPlayerViewer.cs
// プレイヤービルド用メッシュビューア MonoBehaviour ラッパー。
// ロジック本体は PolyLingPlayerViewerCore に委譲する。
//
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.EditorBridge;
using Poly_Ling.Remote;

namespace Poly_Ling.Player
{
    public class PolyLingPlayerViewer : MonoBehaviour
    {
        // ================================================================
        // Inspector 設定
        // ================================================================

        [SerializeField] private PolyLingPlayerViewerCore.RemoteMode _remoteMode = PolyLingPlayerViewerCore.RemoteMode.None;

        // 接続先・待ち受けポートはサーバ一覧（RemoteDirectory）で決まるため設定しない。
        [SerializeField] private bool _clientAutoConnect = true;
        [SerializeField] private bool _serverAutoStart   = true;

        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private Transform  _sceneRoot;

        // ================================================================
        // コア
        // ================================================================

        private PolyLingPlayerViewerCore _core;

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void Awake()
        {
            if (_sceneRoot == null) _sceneRoot = transform;

            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null) _uiDocument = gameObject.AddComponent<UIDocument>();

            if (_uiDocument.panelSettings == null)
                Debug.LogError("[PolyLingPlayerViewer] UIDocument に PanelSettings が未設定です。");

            // PlayerBridge を登録
            PLEditorBridge.Register(new PolyLingPlayerBridge());
        }

        private void Start()
        {
            if (_uiDocument == null || _uiDocument.panelSettings == null) return;

            _core = new PolyLingPlayerViewerCore();
            _core.Initialize(
                _uiDocument.rootVisualElement,
                _sceneRoot,
                new PolyLingPlayerViewerCore.RemoteConfig
                {
                    Mode              = ResolveRemoteMode(_remoteMode),
                    ClientAutoConnect = _clientAutoConnect,
                    ServerAutoStart   = _serverAutoStart,
                });
        }

        /// <summary>
        /// 起動引数 --polyling-remote=<役割>（PolyLingLaunchArgs.cs）があれば、シーンの設定より優先する。
        /// 同じビルドをサーバにもクライアントにもして起動するため（MCP の player_launch が渡す）。
        ///   server → サーバ、separate → クライアント（本体ビューアのクライアントは個別作業で登録する）、none → リモートなし。
        ///   host / collaborator は本体ビューアでは扱えないので、エラーを出してリモートなし。
        ///   値が不明なときもエラーを出してリモートなし（指定を黙って取り違えないため）。
        /// 引数が無ければシーンの設定のまま。
        /// </summary>
        private static PolyLingPlayerViewerCore.RemoteMode ResolveRemoteMode(PolyLingPlayerViewerCore.RemoteMode sceneMode)
        {
            const string tag = "[PolyLingPlayerViewer]";
            if (!PolyLingLaunchArgs.TryGetRemoteRole(out bool present, out LaunchRemoteRole role, out string error))
            {
                if (!present) return sceneMode;
                Debug.LogError($"{tag} {error}。リモートなしで起動します");
                return PolyLingPlayerViewerCore.RemoteMode.None;
            }

            switch (role)
            {
                case LaunchRemoteRole.Server:
                    Debug.Log($"{tag} 起動引数でサーバ");
                    return PolyLingPlayerViewerCore.RemoteMode.Server;
                case LaunchRemoteRole.Separate:
                    Debug.Log($"{tag} 起動引数でクライアント（個別作業）");
                    return PolyLingPlayerViewerCore.RemoteMode.Client;
                case LaunchRemoteRole.None:
                    Debug.Log($"{tag} 起動引数でリモートなし");
                    return PolyLingPlayerViewerCore.RemoteMode.None;
                default:
                    Debug.LogError($"{tag} {PolyLingLaunchArgs.RemoteArg}{role.ToString().ToLowerInvariant()} は本体ビューアでは扱えません"
                                 + "（本体ビューアのクライアントは separate 固定。host / collaborator はリストクライアントのシーンで使う）。リモートなしで起動します");
                    return PolyLingPlayerViewerCore.RemoteMode.None;
            }
        }

        // Phase 2a-2f: MonoBehaviour.Update / LateUpdate はイベント駆動規約に違反するため削除。
        // ここから旧 Tick / LateTick が毎フレーム呼ばれ、dead 実装だったにもかかわらず
        // Debug.Log が毎フレーム走る深刻な問題があった。
        // 描画提出は OnBeginCameraRendering 経由でカメラ毎に呼ばれる。
        // 計算処理は各イベント駆動ハンドラに分散済み (Phase 2a-2 系で完了)。

        private void OnEnable()
        {
            // Camera イベント購読（user 承認範囲: Camera.onPreCull 等のコールバック購読は違反ではない）。
            // URP 互換のため RenderPipelineManager.beginCameraRendering を使う。
            // OnRenderObject() は URP 環境で発火タイミング・Camera.current の扱いが
            // 不確実なため、ここでは RenderPipelineManager 経由で確実に各カメラ描画前に呼ぶ。
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            // [CamDbg] フレーム境界の計測。診断専用。
            UnityEngine.Rendering.RenderPipelineManager.beginFrameRendering += OnBeginFrameRendering;
            UnityEngine.Rendering.RenderPipelineManager.endFrameRendering   += OnEndFrameRendering;
        }

        private void OnDisable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            UnityEngine.Rendering.RenderPipelineManager.beginFrameRendering -= OnBeginFrameRendering;
            UnityEngine.Rendering.RenderPipelineManager.endFrameRendering   -= OnEndFrameRendering;
        }

        // [CamDbg] フレーム境界。診断専用。
        private void OnBeginFrameRendering(
            UnityEngine.Rendering.ScriptableRenderContext ctx, Camera[] cams)
        {
            Poly_Ling.Diagnostics.PLCamDbg.Frame++;
            // [CamDbg] flush=2 のとき、溜まったフラッシュをここで 1 回だけ実行する。
            Poly_Ling.Diagnostics.PLCamDbg.FlushIfPending();
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("F1 frameBegin f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " n=" + (cams == null ? -1 : cams.Length));
        }

        private void OnEndFrameRendering(
            UnityEngine.Rendering.ScriptableRenderContext ctx, Camera[] cams)
        {
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("F2 frameEnd");
            // [PLResStat] 組込みカウンタは値が変わったときだけ出力される。
            Poly_Ling.Diagnostics.PLResStat.ReportStat();
        }

        private void OnDestroy()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _core?.Dispose();
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 等の描画提出のみを行う。 ★★★
        /// 計算処理（Mesh 再構築、ComputeBuffer 更新、Dispatch、フラグ計算、
        /// ホバー判定、選択フラグ反映、マテリアル色設定等）は一切禁止。
        /// 違反は重大な規約違反。
        /// 全ての準備は対応するイベント駆動ハンドラ側で完了させておくこと
        /// （具体的には _core.PresentAll() の呼出し契機）。
        ///
        /// RenderPipelineManager.beginCameraRendering は各カメラ描画の直前に
        /// 呼ばれるコールバック（URP 互換）。引数で対象カメラが渡されるため
        /// slot 特定が可能。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        private void OnBeginCameraRendering(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null) return;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("R1 begin cam=" + cam.name);
            _core?.SubmitDrawForCamera(cam);
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("R2 end cam=" + cam.name);
        }
    }
}
