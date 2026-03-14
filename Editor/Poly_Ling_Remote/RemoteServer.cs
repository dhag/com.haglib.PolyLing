// Remote/RemoteServer.cs
// EditorWindow薄皮。RemoteServerCoreをホストするだけ。
// TCP/WebSocket/プロトコル処理は RemoteServerCore に委譲。

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Remote
{
    /// <summary>
    /// リモートパネルサーバー（EditorWindow）
    /// コアロジックは RemoteServerCore に委譲。
    /// </summary>
    public class RemoteServer : EditorWindow
    {
        // ================================================================
        // コア
        // ================================================================

        private RemoteServerCore _core;

        /// <summary>
        /// PanelContextのSendCommandデリゲート。
        /// PolyLing_SummaryNotify経由で注入することで全PanelCommandを処理できる。
        /// </summary>
        private System.Action<Poly_Ling.Data.PanelCommand> _dispatchCommand;

        public void SetDispatchCommand(System.Action<Poly_Ling.Data.PanelCommand> dispatch)
        {
            _dispatchCommand = dispatch;
            if (_core != null) _core.DispatchCommand = dispatch;
        }

        private ToolContext Context
        {
            get
            {
                var windows = Resources.FindObjectsOfTypeAll<PolyLing>();
                return (windows != null && windows.Length > 0) ? windows[0].CurrentToolContext : null;
            }
        }

        // ================================================================
        // EditorWindow設定（EditorPrefs管理）
        // ================================================================

        private int  _port      = 8765;
        private bool _autoStart;

        private const string AutoStartPrefKey = "PolyLing.RemoteServer.AutoStart";
        private const string AutoStartPortKey = "PolyLing.RemoteServer.Port";

        // ================================================================
        // ログ（表示用）
        // ================================================================

        private readonly List<string> _logMessages = new List<string>();
        private Vector2 _logScroll;
        private const int MaxLogLines = 50;

        // ================================================================
        // ウィンドウ管理
        // ================================================================

        /// <summary>サーバーが起動中か（SummaryNotify等から参照）</summary>
        public bool IsRunning => _core?.IsRunning ?? false;

        /// <summary>サーバーを起動する（SummaryNotify等から呼び出し）</summary>
        public void StartServer() => _core?.Start();

        public static void Open(ToolContext ctx = null)
        {
            GetWindow<RemoteServer>("Remote Server").Show();
        }

        public void SetContext(ToolContext ctx) { /* 互換用 */ }

        public static RemoteServer FindInstance()
        {
            var windows = Resources.FindObjectsOfTypeAll<RemoteServer>();
            return (windows != null && windows.Length > 0) ? windows[0] : null;
        }

        // ================================================================
        // 画像追加（Texture2D変換はここで行い、コアにはImageEntryを渡す）
        // ================================================================

        public void AddCapturedImage(Texture2D tex)
        {
            if (tex == null || _core == null) return;
            var entry = RemoteImageSerializer.FromTexture2DJPEG(tex, _core.CapturedImages.Count > 0
                ? (ushort)(_core.CapturedImages[_core.CapturedImages.Count - 1].Id + 1)
                : (ushort)0, 85);
            _core.AddCapturedImageEntry(entry);
        }

        // ================================================================
        // EditorWindowライフサイクル
        // ================================================================

        private void OnEnable()
        {
            _autoStart = PLEditorBridge.I.GetPrefBool(AutoStartPrefKey, false);
            _port      = PLEditorBridge.I.GetPrefInt(AutoStartPortKey,  _port);

            _core = new RemoteServerCore(() => Context, _port);
            _core.OnLog          = msg => { AddLog(msg); };
            _core.OnRepaint      = () => Repaint();
            _core.DispatchCommand = _dispatchCommand; // SummaryNotifyから注入済みであれば反映

            EditorApplication.update += Tick;

            if (_autoStart) _core.Start();
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            _core?.Stop();
            _core = null;
        }

        private void Tick() => _core?.Tick();

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            if (_core == null) return;

            EditorGUILayout.LabelField("Remote Panel Server", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            bool hasContext = Context?.Model != null;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("ToolContext", hasContext);
                if (hasContext)
                    EditorGUILayout.IntField("Meshes", Context.Model.Count);
            }

            EditorGUILayout.Space(5);

            using (new EditorGUI.DisabledScope(_core.IsRunning))
            {
                int newPort = EditorGUILayout.IntField("Port", _core.Port);
                if (newPort != _core.Port)
                {
                    _core.Port = newPort;
                    _port      = newPort;
                    PLEditorBridge.I.SetPrefInt(AutoStartPortKey, _port);
                }
            }

            bool newAutoStart = EditorGUILayout.Toggle("エディタ起動時に自動開始", _autoStart);
            if (newAutoStart != _autoStart)
            {
                _autoStart = newAutoStart;
                PLEditorBridge.I.SetPrefBool(AutoStartPrefKey, _autoStart);
            }

            if (!_core.IsRunning)
            {
                if (GUILayout.Button("Start Server")) _core.Start();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"ブラウザで http://localhost:{_core.Port}/ を開いてください",
                    MessageType.Info);

                EditorGUILayout.LabelField($"接続クライアント: {_core.ClientCount}");

                if (GUILayout.Button("Stop Server"))         _core.Stop();
                if (GUILayout.Button("Send Test Image"))     SendTestImages();
                if (GUILayout.Button("Send Project Header")) _core.SendProjectHeader();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Captured Images", EditorStyles.miniBoldLabel);

                var images = _core.CapturedImages;
                if (images.Count == 0)
                {
                    EditorGUILayout.LabelField("  (empty)", EditorStyles.miniLabel);
                }
                else
                {
                    long totalBytes = 0;
                    foreach (var img in images) totalBytes += img.Data.Length;
                    EditorGUILayout.LabelField(
                        $"  {images.Count} 枚 ({totalBytes / 1024}KB)", EditorStyles.miniLabel);
                }

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(images.Count == 0))
                {
                    if (GUILayout.Button("Send Images")) _core.SendCapturedImages();
                    if (GUILayout.Button("Clear"))       _core.ClearCapturedImages();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Log", EditorStyles.miniBoldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(150));
            foreach (var msg in _logMessages)
                EditorGUILayout.LabelField(msg, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Clear Log")) _logMessages.Clear();
        }

        // ================================================================
        // テスト画像生成（UnityEngine依存、EditorWindow側に残す）
        // ================================================================

        private void SendTestImages()
        {
            if (_core == null) return;

            var images = new List<ImageEntry>();
            int[] sizes = { 32, 64, 128 };

            for (int s = 0; s < sizes.Length; s++)
            {
                int size    = sizes[s];
                var tex     = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var baseCol = new Color(
                    Random.Range(0.2f, 1f),
                    Random.Range(0.2f, 1f),
                    Random.Range(0.2f, 1f));

                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        bool grid = ((x / 8) + (y / 8)) % 2 == 0;
                        Color c   = grid ? baseCol : baseCol * 0.6f;
                        c.a = 1f;
                        tex.SetPixel(x, y, c);
                    }
                tex.Apply();

                images.Add(RemoteImageSerializer.FromTexture2D(tex, (ushort)s));
                DestroyImmediate(tex);
            }

            byte[] data = RemoteImageSerializer.Serialize(images);
            if (data != null)
            {
                // コアのBroadcastBinaryはprivateなので、画像はコアのAddCapturedImageEntryを経由
                // テスト用なので直接エントリとして登録→送信
                foreach (var img in images) _core.AddCapturedImageEntry(img);
                _core.SendCapturedImages();
                _core.ClearCapturedImages();
                AddLog($"テスト画像送信: {images.Count}枚 ({data.Length}B)");
            }
        }

        // ================================================================
        // ログ管理
        // ================================================================

        private void AddLog(string message)
        {
            _logMessages.Add(message);
            while (_logMessages.Count > MaxLogLines)
                _logMessages.RemoveAt(0);
            Repaint();
        }
    }
}
