// Editor/HierarchyIO/HierarchyExportClientWindow.cs
// ============================================================
// ヒエラルキーエクスポートのクライアント（待ち受け専用）
// ============================================================
//
// 【役割】
//   PolyLing 本体（サーバ）からの push を待ち受けるだけのクライアント。
//   自分からクエリ・コマンドは送らない。
//
// 【処理の流れ】
//   1. endpoint.json を探索して WebSocket 接続。
//   2. RegisterClientType("hierarchyExport") で自タイプを登録。
//   3. サーバが Send Hierarchy を実行すると、
//      JSON push "hierarchyBundle"（概要）→ PLRF バイナリ束 の順で届く。
//   4. 束を保存先フォルダへ展開する（プロジェクトファイル形式のまま）。
//   5. 展開後は HierarchyExportWindow.ExportFromFolder() を呼ぶ。
//      これはファイル指定ボタンと同一経路のため、
//      読み取り後の動作（LoadModel → ComputeWorldMatrices → Export）は
//      ファイルから読んだときと完全に同じになる。
//
// 【注意】
//   - PolyLingPlayerClient は SynchronizationContext 駆動のため
//     毎フレームのポーリング（Tick）は不要。
//   - 位置連動等の別バイナリ push も届くため、PLRF 以外は無視する。
//   - 自分のエディタで Play モードが走っている間（コンパイル中・
//     アセット更新中も同様）は書き出しを行わない。GameObject 生成や
//     AssetDatabase 操作が破棄・失敗するため。受信ファイルの保存だけは行い、
//     エラーメッセージを出して書き出しはスキップする。
//
// ============================================================

#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.ListClient;
using Poly_Ling.Player;
using Poly_Ling.Remote;

namespace Poly_Ling.EditorIO
{
    /// <summary>
    /// サーバからの push を待ち受け、受信したプロジェクトファイル一式を
    /// 展開してヒエラルキーへ書き出すクライアント。
    /// </summary>
    public class HierarchyExportClientWindow : EditorWindow
    {
        // サーバ側 RemoteServerCore.HierarchyClientType と一致させること。
        private const string ClientTypeId = "hierarchyExport";

        private const string PrefsKeyDestRoot    = "PolyLing.HierarchyExportClient.DestRoot";
        private const string PrefsKeyUserName    = "PolyLing.HierarchyExportClient.UserName";
        private const string PrefsKeyAutoConnect = "PolyLing.HierarchyExportClient.AutoConnect";
        private const string PrefsKeyAutoExport  = "PolyLing.HierarchyExportClient.AutoExport";

        private PolyLingPlayerClient _client;

        private string _destRoot    = "";
        private string _userName    = "";
        private bool   _autoConnect = true;
        private bool   _autoExport  = true;

        private string _endpointInfo = "";
        private string _status       = "未接続";
        private string _lastNotice   = "";
        private string _lastFolder   = "";

        [MenuItem("PolyLing/IO/Hierarchy Export Client (Remote)")]
        public static void Open()
        {
            GetWindow<HierarchyExportClientWindow>(true, "Hierarchy Export Client", true);
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            _destRoot    = EditorPrefs.GetString(PrefsKeyDestRoot, DefaultDestRoot());
            _userName    = EditorPrefs.GetString(PrefsKeyUserName, "");
            _autoConnect = EditorPrefs.GetBool(PrefsKeyAutoConnect, true);
            _autoExport  = EditorPrefs.GetBool(PrefsKeyAutoExport, true);

            if (_autoConnect) Connect();
        }

        private void OnDisable()
        {
            SavePrefs();
            Disconnect();
        }

        private static string DefaultDestRoot()
        {
            return Path.Combine(Application.persistentDataPath, "PolyLing", "RemoteHierarchy");
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(PrefsKeyDestRoot, _destRoot ?? "");
            EditorPrefs.SetString(PrefsKeyUserName, _userName ?? "");
            EditorPrefs.SetBool(PrefsKeyAutoConnect, _autoConnect);
            EditorPrefs.SetBool(PrefsKeyAutoExport, _autoExport);
        }

        // ================================================================
        // 書き出し可否（自分のエディタの状態）
        // ================================================================

        /// <summary>
        /// いま書き出してよいか。不可なら理由を返す（可なら reason は空）。
        ///
        /// Play モード中に書き出すと、生成した GameObject は Play 終了で破棄され、
        /// プレファブ／メッシュ .asset の生成も想定外の結果になる。
        /// コンパイル中・アセット更新中も AssetDatabase 操作が不安定なため同じ扱いにする。
        /// </summary>
        private static bool CanExportNow(out string reason)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            {
                reason = "Play モード実行中のため書き出しをスキップしました。";
                return false;
            }

            if (EditorApplication.isCompiling)
            {
                reason = "スクリプトコンパイル中のため書き出しをスキップしました。";
                return false;
            }

            if (EditorApplication.isUpdating)
            {
                reason = "アセットデータベース更新中のため書き出しをスキップしました。";
                return false;
            }

            reason = "";
            return true;
        }

        // ================================================================
        // UI（IMGUI）
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.LabelField("ヒエラルキー書き出し（リモート受信）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "待ち受け専用です。PolyLing 本体側の Remote パネルで " +
                "「Send Hierarchy (Project)」を押すと、プロジェクト全体を受信して書き出します。",
                MessageType.None);

            bool canExport = CanExportNow(out string blockReason);
            if (!canExport)
            {
                EditorGUILayout.HelpBox(
                    blockReason + "\n受信したファイルの保存だけは行います。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(6);

            bool connected = _client != null && _client.IsConnected;

            EditorGUILayout.LabelField("状態", _status);
            if (!string.IsNullOrEmpty(_endpointInfo))
                EditorGUILayout.LabelField("接続先", _endpointInfo);

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(connected))
                {
                    if (GUILayout.Button("接続", GUILayout.Height(24))) Connect();
                }
                using (new EditorGUI.DisabledScope(!connected))
                {
                    if (GUILayout.Button("切断", GUILayout.Height(24))) Disconnect();
                }
            }

            EditorGUILayout.Space(8);

            _autoConnect = EditorGUILayout.Toggle("ウィンドウを開いたら接続", _autoConnect);
            _autoExport  = EditorGUILayout.Toggle("受信したら自動で書き出す", _autoExport);

            using (new EditorGUI.DisabledScope(connected))
            {
                _userName = EditorGUILayout.TextField("ユーザー名（任意）", _userName);
            }

            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("受信ファイルの保存先", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _destRoot = EditorGUILayout.TextField(_destRoot);
                if (GUILayout.Button("参照", GUILayout.Width(60))) BrowseDestRoot();
                if (GUILayout.Button("既定", GUILayout.Width(60))) _destRoot = DefaultDestRoot();
            }

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("最終受信", string.IsNullOrEmpty(_lastNotice) ? "(なし)" : _lastNotice);

            bool hasFolder = !string.IsNullOrEmpty(_lastFolder) && Directory.Exists(_lastFolder);
            using (new EditorGUI.DisabledScope(!hasFolder || !canExport))
            {
                if (GUILayout.Button("受信フォルダから書き出す", GUILayout.Height(26)))
                    HierarchyExportWindow.ExportFromFolder(_lastFolder);
            }
        }

        private void BrowseDestRoot()
        {
            string start = Directory.Exists(_destRoot) ? _destRoot : DefaultDestRoot();
            string picked = EditorUtility.OpenFolderPanel("受信ファイルの保存先", start, "");
            if (!string.IsNullOrEmpty(picked))
            {
                _destRoot = picked;
                SavePrefs();
            }
        }

        // ================================================================
        // 接続 / 切断
        // ================================================================

        private void Connect()
        {
            if (_client != null && _client.IsConnected) return;

            if (!EndpointLocator.TryLocate(out string host, out int port, out string foundPath))
            {
                _endpointInfo = "";
                _status = "endpoint.json が見つかりません（サーバ未起動）";
                Repaint();
                return;
            }

            _endpointInfo = $"ws://{host}:{port}/   ({foundPath})";
            _status = "接続中...";
            Repaint();

            Disconnect();

            _client = new PolyLingPlayerClient();
            _client.OnConnected         += HandleConnected;
            _client.OnDisconnected      += HandleDisconnected;
            _client.OnPushReceived      += HandleTextPush;
            _client.OnBinaryPushReceived += HandleBinaryPush;

            // Initialize はメインスレッドから呼ぶこと。
            // ここで捕まえた SynchronizationContext 経由で各コールバックが返る。
            _client.Initialize(host, port, autoConnect: true);
        }

        private void Disconnect()
        {
            if (_client == null) return;

            _client.OnConnected         -= HandleConnected;
            _client.OnDisconnected      -= HandleDisconnected;
            _client.OnPushReceived      -= HandleTextPush;
            _client.OnBinaryPushReceived -= HandleBinaryPush;

            _client.Dispose();
            _client = null;

            _status = "未接続";
            Repaint();
        }

        // ================================================================
        // 受信
        // ================================================================

        private void HandleConnected()
        {
            _status = "接続済み（待ち受け中）";

            string name = string.IsNullOrWhiteSpace(_userName)
                ? SystemInfo.deviceName
                : _userName.Trim();

            _client?.RegisterClientType(ClientTypeId, name);
            Repaint();
        }

        private void HandleDisconnected()
        {
            _status = "切断されました";
            Repaint();
        }

        private void HandleTextPush(string json)
        {
            // 概要通知のみ。実データは直後のバイナリで届く。
            if (json == null) return;
            if (json.IndexOf("\"hierarchyBundle\"", StringComparison.Ordinal) < 0) return;

            _status = "束を受信中...";
            Repaint();
        }

        private void HandleBinaryPush(byte[] data)
        {
            // 位置連動等の別バイナリ push も届くため、PLRF 以外は無視する。
            if (!RemoteFileBundle.IsBundle(data)) return;

            if (string.IsNullOrEmpty(_destRoot))
                _destRoot = DefaultDestRoot();

            try { Directory.CreateDirectory(_destRoot); }
            catch (Exception ex)
            {
                _status = "保存先を作成できません: " + ex.Message;
                Repaint();
                return;
            }

            bool ok = RemoteFileBundle.Deserialize(
                data, _destRoot,
                out string folderPath, out byte rootKind, out int fileCount, out string error);

            if (!ok)
            {
                _status = "受信失敗: " + error;
                Repaint();
                return;
            }

            _lastFolder = folderPath;
            _lastNotice = $"{Path.GetFileName(folderPath)}  {fileCount} ファイル  {DateTime.Now:HH:mm:ss}";
            _status     = "受信完了";
            Repaint();

            Debug.Log($"[HierarchyExportClient] 受信 {fileCount} ファイル → {folderPath}");

            // ファイルの保存はここまでで完了している。
            // 以降の書き出しは自分のエディタの状態に依存するため、
            // 不可なら理由を出してスキップする（受信ファイルは残る）。
            if (!CanExportNow(out string blockReason))
            {
                _status = blockReason;
                Debug.LogError(
                    "[HierarchyExportClient] " + blockReason +
                    " 受信ファイルは保存済みです: " + folderPath);
                Repaint();
                return;
            }

            if (!_autoExport) return;

            // 以降はファイルから読むときと同一経路。
            // プロジェクトフォルダなら配下のモデルを一括、
            // モデルフォルダなら単体として LoadAndExport が判定する。
            HierarchyExportWindow.ExportFromFolder(folderPath);

            _status = "書き出し完了";
            Repaint();
        }
    }
}

#endif
