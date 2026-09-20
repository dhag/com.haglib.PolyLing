// Editor/HierarchyIO/HierarchyRemoteExportWindow.cs
// ============================================================
// リモートのプロジェクト → Unityヒエラルキー（要求型クライアント）
// ============================================================
//
// 【役割】
//   HierarchyExportWindow（プロジェクトファイル → ヒエラルキー）のリモート版。
//   入力元がモデルフォルダではなく、接続した PolyLing 本体（サーバ）になる。
//   こちらからクエリ project_bundle を送り、プロジェクト全体を PLRF 束で受け取る。
//
// 【処理の流れ】
//   1. サーバ一覧（マスター）に問い合わせて WebSocket 接続（複数あれば選択）。
//   2. RegisterClientType("hierarchyFetch")。
//      "hierarchyExport" で登録すると、本体側の Send Hierarchy の push まで届くため別名にする。
//   3. ボタンで FetchProjectBundle → 応答の束を受信ファイルの書き込み先へ展開。
//   4. 展開したフォルダを HierarchyPrefabExporter.ExportFolder で書き出し、結果をダイアログで出す。
//      これはファイルから読んだときと同じ経路になる。
//
// 【共有部品】
//   オプション欄と設定の読み書き … HierarchyExportOptionsGUI（HierarchyExportWindow と同じ設定）
//   書き出し可否・展開・接続先の選択 … RemoteHierarchyReceive
//
// 【注意】
//   Play 中・コンパイル中・アセット更新中は、展開までで止めて理由を出す（受信ファイルは残る）。
// ============================================================

#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Core;
using Poly_Ling.EditorTools;
using Poly_Ling.Player;
using Poly_Ling.Remote;

namespace Poly_Ling.EditorIO
{
    public class HierarchyRemoteExportWindow : EditorWindow
    {
        private const string ClientTypeId = "hierarchyFetch";

        // 接続まわりの設定はこの窓だけのものなので EditorPrefs。
        private const string PrefsKeyUserName    = "PolyLing.HierarchyRemoteExport.UserName";
        private const string PrefsKeyAutoConnect = "PolyLing.HierarchyRemoteExport.AutoConnect";

        // オプション。既定値は HierarchyExportOptions が持つ。
        private readonly HierarchyExportOptions _opt = new HierarchyExportOptions();

        private PolyLingPlayerClient  _client;
        private RemoteServerConnector _connector;

        private string _destRoot    = "";
        private string _userName    = "";
        private bool   _autoConnect = true;

        private string _endpointInfo = "";
        private string _status       = "未接続";
        private bool   _fetching;

        private Vector2 _scroll;

        [MenuItem("PolyLing/IO/Hierarchy Export (Remote Project → Hierarchy)")]
        public static void Open()
        {
            GetWindow<HierarchyRemoteExportWindow>(true, "Hierarchy Export (Remote)", true);
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            HierarchyExportOptionsGUI.Load(_opt);

            // 受信ファイルの書き込み先は待ち受け型クライアントと共通（SaveDest.Keys.RemoteHierarchy）。
            _destRoot    = EditorSaveDestField.Load(SaveDest.Keys.RemoteHierarchy, RemoteHierarchyReceive.DefaultDestRoot());
            _userName    = EditorPrefs.GetString(PrefsKeyUserName, "");
            _autoConnect = EditorPrefs.GetBool(PrefsKeyAutoConnect, true);

            if (_autoConnect) Connect();
        }

        private void OnDisable()
        {
            HierarchyExportOptionsGUI.Save(_opt);
            SaveDest.SetFolder(SaveDest.Keys.RemoteHierarchy, _destRoot ?? "");
            EditorPrefs.SetString(PrefsKeyUserName, _userName ?? "");
            EditorPrefs.SetBool(PrefsKeyAutoConnect, _autoConnect);
            Disconnect();
        }

        // ================================================================
        // UI（IMGUI）
        // ================================================================

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("リモートのプロジェクト → ヒエラルキー", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "接続した PolyLing 本体からプロジェクト全体を取得し、" +
                "配下のモデルを全て書き出します。",
                MessageType.None);
            EditorGUILayout.Space();

            // ── 接続 ──────────────────────────────────────────────
            bool connected = _client != null && _client.IsConnected;

            EditorGUILayout.LabelField("状態", _status);
            if (!string.IsNullOrEmpty(_endpointInfo))
                EditorGUILayout.LabelField("接続先", _endpointInfo);

            RemoteHierarchyReceive.DrawServerChoices(_connector);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(connected))
                {
                    if (GUILayout.Button("接続", GUILayout.Height(22))) Connect();
                }
                using (new EditorGUI.DisabledScope(!connected))
                {
                    if (GUILayout.Button("切断", GUILayout.Height(22))) Disconnect();
                }
            }

            _autoConnect = EditorGUILayout.Toggle("ウィンドウを開いたら接続", _autoConnect);
            using (new EditorGUI.DisabledScope(connected))
            {
                _userName = EditorGUILayout.TextField("ユーザー名（任意）", _userName);
            }

            // [...] は書き込み先フォルダを決めるだけ。
            _destRoot = EditorSaveDestField.Draw(
                "受信ファイルの書き込み先", _destRoot, SaveDest.Keys.RemoteHierarchy,
                "受信ファイルの書き込み先", "project.csv", "csv");

            // ── オプション（HierarchyExportWindow と共通） ──────────
            EditorGUILayout.Space();
            HierarchyExportOptionsGUI.Draw(_opt);

            // ── 実行 ──────────────────────────────────────────────
            EditorGUILayout.Space();

            bool canExport = RemoteHierarchyReceive.CanExportNow(out string blockReason);
            if (!canExport)
            {
                EditorGUILayout.HelpBox(
                    blockReason + "\n取得したファイルの保存だけは行います。",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!connected || _fetching))
            {
                string label = _opt.SaveAsPrefab ? "取得してプレファブに保存" : "取得してヒエラルキーに書き出し";
                if (GUILayout.Button(label, GUILayout.Height(28)))
                    FetchAndExport();
            }

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // 接続 / 切断
        // ================================================================

        private void Connect()
        {
            if (_client != null && _client.IsConnected) return;

            if (_client == null)
            {
                _client = new PolyLingPlayerClient();
                _client.OnConnected    += HandleConnected;
                _client.OnDisconnected += HandleDisconnected;

                // 接続先はマスターから得る。複数あれば OnGUI で選ばせる。
                // Begin / Choose はメインスレッドから呼ぶこと。
                _connector = new RemoteServerConnector(_client)
                {
                    OnStatus         = s => { _status = s; Repaint(); },
                    OnChoicesChanged = _ => Repaint(),
                };
            }

            _endpointInfo = "";
            _connector.Begin();
        }

        private void Disconnect()
        {
            if (_client == null) return;

            _connector?.Detach();
            _connector = null;

            _client.OnConnected    -= HandleConnected;
            _client.OnDisconnected -= HandleDisconnected;

            _client.Dispose();
            _client = null;

            _fetching     = false;
            _endpointInfo = "";
            _status       = "未接続";
            Repaint();
        }

        private void HandleConnected()
        {
            _status = "接続済み";
            var target = _connector?.Target;
            _endpointInfo = target != null ? $"ws://{RemoteDirectory.Host}:{target.Port}/   {target.Label}" : "";

            string name = string.IsNullOrWhiteSpace(_userName)
                ? SystemInfo.deviceName
                : _userName.Trim();

            _client?.RegisterClientType(ClientTypeId, name);
            Repaint();
        }

        private void HandleDisconnected()
        {
            _fetching = false;
            _status   = "切断されました";
            Repaint();
        }

        // ================================================================
        // 取得 → 展開 → 書き出し
        // ================================================================

        private void FetchAndExport()
        {
            if (_client == null || !_client.IsConnected || _fetching) return;

            _fetching = true;
            _status   = "プロジェクトを取得中...";
            Repaint();

            _client.FetchProjectBundle(OnBundleReceived);
        }

        private void OnBundleReceived(string json, byte[] data)
        {
            _fetching = false;

            if (data == null)
            {
                _status = "取得に失敗しました";
                Debug.LogError("[HierarchyRemoteExport] 取得に失敗しました: " + json);
                EditorUtility.DisplayDialog("取得失敗", "サーバからプロジェクトを取得できませんでした。\n" + json, "OK");
                Repaint();
                return;
            }

            if (!RemoteHierarchyReceive.Expand(data, _destRoot,
                    out string folderPath, out int fileCount, out string error))
            {
                _status = "展開に失敗しました: " + error;
                EditorUtility.DisplayDialog("展開失敗", error, "OK");
                Repaint();
                return;
            }

            Debug.Log($"[HierarchyRemoteExport] 受信 {fileCount} ファイル → {folderPath}");

            // 書き出しは自分のエディタの状態に依存する。不可なら理由を出して止める（受信ファイルは残る）。
            if (!RemoteHierarchyReceive.CanExportNow(out string blockReason))
            {
                _status = blockReason;
                Debug.LogError("[HierarchyRemoteExport] " + blockReason + " 受信ファイルは保存済みです: " + folderPath);
                Repaint();
                return;
            }

            // 以降はファイルから読むときと同一経路（HierarchyExportWindow.LoadAndExport と同じ本体）。
            var outcome = new HierarchyPrefabExporter(_opt.Clone()).ExportFolder(folderPath);
            _status = $"{outcome.Title}  {DateTime.Now:HH:mm:ss}";
            Repaint();
            EditorUtility.DisplayDialog(outcome.Title, outcome.Text, "OK");
        }
    }
}

#endif
