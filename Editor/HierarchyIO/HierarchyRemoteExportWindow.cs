// Editor/HierarchyIO/HierarchyRemoteExportWindow.cs
// ============================================================
// PolyLing 本体からプロジェクトを取得して Unity ヒエラルキーへ書き出す。
// ============================================================
//
// 【方針】
//   2 つの経路を持つ。
//   ・Pull：エディタ側から project_bundle を要求する（取得ボタン）。
//   ・Push：本体の SendHierarchyBundleCommand が送る束を受ける。
//     「サーバからの自動受け入れ」がオンの間だけ "hierarchyExport" で登録し、
//     オフの間は "hierarchyFetch" で登録する。サーバは "hierarchyExport" にだけ
//     送るので、オフの間はデータが送られない。
//
// 【処理の流れ】
//   1. RemoteDirectory から PolyLing 本体を選び、WebSocket 接続する。
//   2. Pull は project_bundle を要求する。Push は hierarchyBundle の push と
//      続くバイナリを受ける。
//   3. 受信した JSON（ProjectDTO の UTF-8）をメモリ上で ModelContext に戻す。
//      テクスチャは ModelDTO.embeddedTextures の画像を Material に設定する。ファイルは介さない。
//   4. HierarchyPrefabExporter.ExportModels で Hierarchy または Prefab へ書き出す。
//
// ============================================================

#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Core;
using Poly_Ling.Context;
using Poly_Ling.EditorTools;
using Poly_Ling.Player;
using Poly_Ling.Remote;
using Poly_Ling.Serialization;

namespace Poly_Ling.EditorIO
{
    public class HierarchyRemoteExportWindow : EditorWindow
    {
        /// <summary>自動受け入れがオフのときの登録種別。サーバの push 対象外。</summary>
        private const string FetchOnlyClientTypeId = "hierarchyFetch";

        /// <summary>サーバが送ってくる push の event 名（RemoteServerCore.SendHierarchyBundle）。</summary>
        private const string PushEventHierarchyBundle = "hierarchyBundle";

        private const string PrefsKeyUserName =
            "PolyLing.HierarchyRemoteExport.UserName";
        private const string PrefsKeyAutoConnect =
            "PolyLing.HierarchyRemoteExport.AutoConnect";
        private const string PrefsKeyAutoAccept =
            "PolyLing.HierarchyRemoteExport.AutoAccept";

        private readonly HierarchyExportOptions _options =
            new HierarchyExportOptions
            {
                AddToHierarchy = true,
            };

        private PolyLingPlayerClient _client;
        private RemoteServerConnector _connector;

        private string _userName = "";
        private bool _autoConnect = true;
        private bool _autoAccept = true;

        /// <summary>hierarchyBundle の push を受け、続くバイナリ（束本体）を待っている。</summary>
        private bool _pushPending;

        private string _endpointInfo = "";
        private string _status = "未接続";
        private bool _fetching;
        private Vector2 _scroll;

        [MenuItem("PolyLing/IO/Hierarchy Export (PolyLing → Hierarchy)")]
        public static void Open()
        {
            GetWindow<HierarchyRemoteExportWindow>(
                true, "Hierarchy Export from PolyLing", true);
        }

        private void OnEnable()
        {
            HierarchyExportOptionsGUI.Load(_options);

            _userName = EditorPrefs.GetString(PrefsKeyUserName, "");
            _autoConnect = EditorPrefs.GetBool(PrefsKeyAutoConnect, true);
            _autoAccept = EditorPrefs.GetBool(PrefsKeyAutoAccept, true);

            if (_autoConnect)
                Connect();
        }

        private void OnDisable()
        {
            SaveSettings();
            Disconnect();
        }

        private void SaveSettings()
        {
            HierarchyExportOptionsGUI.Save(_options);
            EditorPrefs.SetString(PrefsKeyUserName, _userName ?? "");
            EditorPrefs.SetBool(PrefsKeyAutoConnect, _autoConnect);
            EditorPrefs.SetBool(PrefsKeyAutoAccept, _autoAccept);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawAutoAcceptToggle();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                "PolyLingから取得 → ヒエラルキー", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "接続したPolyLing本体からプロジェクト全体を取得し、" +
                "HierarchyまたはPrefabへ書き出します。送信操作はPolyLing本体側では不要です。",
                MessageType.None);

            EditorGUILayout.Space(6);
            DrawConnectionSection();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("書き出し設定", EditorStyles.boldLabel);
            HierarchyExportOptionsGUI.Draw(_options);

            EditorGUILayout.Space(10);
            DrawExecutionSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawAutoAcceptToggle()
        {
            bool next = EditorGUILayout.ToggleLeft(
                "サーバからの自動受け入れ", _autoAccept, EditorStyles.boldLabel);
            if (next == _autoAccept)
                return;

            _autoAccept = next;
            EditorPrefs.SetBool(PrefsKeyAutoAccept, _autoAccept);
            if (!_autoAccept)
                _pushPending = false;

            // 登録種別を切り替え、サーバの送信対象から外す／戻す。
            RegisterClientType();
        }

        private string CurrentClientTypeId =>
            _autoAccept ? RemoteServerCore.HierarchyClientType : FetchOnlyClientTypeId;

        private void RegisterClientType()
        {
            if (_client == null || !_client.IsConnected)
                return;

            string name = string.IsNullOrWhiteSpace(_userName)
                ? SystemInfo.deviceName
                : _userName.Trim();

            _client.RegisterClientType(CurrentClientTypeId, name);
        }

        private void DrawConnectionSection()
        {
            EditorGUILayout.LabelField("接続", EditorStyles.boldLabel);

            bool connected = _client != null && _client.IsConnected;
            EditorGUILayout.LabelField("状態", _status);

            if (!string.IsNullOrEmpty(_endpointInfo))
                EditorGUILayout.LabelField("接続先", _endpointInfo);

            RemoteHierarchyReceive.DrawServerChoices(_connector);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(connected))
                {
                    if (GUILayout.Button("接続", GUILayout.Height(22)))
                        Connect();
                }

                using (new EditorGUI.DisabledScope(!connected))
                {
                    if (GUILayout.Button("切断", GUILayout.Height(22)))
                        Disconnect();
                }
            }

            _autoConnect = EditorGUILayout.Toggle(
                "ウィンドウを開いたら接続", _autoConnect);

            using (new EditorGUI.DisabledScope(connected))
            {
                _userName = EditorGUILayout.TextField(
                    "ユーザー名（任意）", _userName);
            }
        }

        private void DrawExecutionSection()
        {
            bool connected = _client != null && _client.IsConnected;
            bool canExport = RemoteHierarchyReceive.CanExportNow(
                out string blockReason);
            bool hasOutput = _options.AddToHierarchy || _options.SaveAsPrefab;

            if (!canExport)
            {
                EditorGUILayout.HelpBox(
                    blockReason,
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(
                       !connected || _fetching || !canExport || !hasOutput))
            {
                if (GUILayout.Button(BuildExecuteLabel(), GUILayout.Height(30)))
                    FetchProject();
            }
        }

        private string BuildExecuteLabel()
        {
            if (_options.AddToHierarchy && _options.SaveAsPrefab)
                return "取得してプレファブ保存＋ヒエラルキー追加";
            if (_options.SaveAsPrefab)
                return "取得してプレファブに保存";
            if (_options.AddToHierarchy)
                return "取得してヒエラルキーに追加";
            return "出力方法を選択してください";
        }

        private void Connect()
        {
            if (_client != null && _client.IsConnected)
                return;

            if (_client == null)
            {
                _client = new PolyLingPlayerClient();
                _client.OnConnected += HandleConnected;
                _client.OnDisconnected += HandleDisconnected;
                _client.OnPushReceived += HandlePush;
                _client.OnBinaryPushReceived = HandleBinaryPush;

                _connector = new RemoteServerConnector(_client)
                {
                    OnStatus = value =>
                    {
                        _status = value;
                        Repaint();
                    },
                    OnChoicesChanged = _ => Repaint(),
                };
            }

            _endpointInfo = "";
            _connector.Begin();
        }

        private void Disconnect()
        {
            if (_client == null)
                return;

            _connector?.Detach();
            _connector = null;

            _client.OnConnected -= HandleConnected;
            _client.OnDisconnected -= HandleDisconnected;
            _client.OnPushReceived -= HandlePush;
            _client.OnBinaryPushReceived = null;
            _client.Dispose();
            _client = null;

            _fetching = false;
            _pushPending = false;
            _endpointInfo = "";
            _status = "未接続";
            Repaint();
        }

        private void HandleConnected()
        {
            _status = "接続済み";

            var target = _connector?.Target;
            _endpointInfo = target != null
                ? $"ws://{RemoteDirectory.Host}:{target.Port}/   {target.Label}"
                : "";

            RegisterClientType();
            Repaint();
        }

        private void HandleDisconnected()
        {
            _fetching = false;
            _pushPending = false;
            _status = "切断されました";
            Repaint();
        }

        private void HandlePush(string json)
        {
            if (PolyLingPlayerClient.GetPushEvent(json) != PushEventHierarchyBundle)
                return;

            // オフに切り替えた直後に送られていた分は受けない。
            if (!_autoAccept)
                return;

            _pushPending = true;
            _status = "サーバからプロジェクトを受信中...";
            Repaint();
        }

        private void HandleBinaryPush(byte[] data)
        {
            if (!_pushPending)
                return;
            _pushPending = false;

            if (!_autoAccept)
                return;

            Debug.Log(
                $"[HierarchyRemoteExport] サーバから受信 ({data?.Length ?? 0}B)");
            OnBundleReceived("", data);
        }

        private void FetchProject()
        {
            if (_client == null || !_client.IsConnected || _fetching)
                return;

            SaveSettings();

            _fetching = true;
            _status = "プロジェクトを取得中...";
            Repaint();

            try
            {
                _client.FetchProjectBundle(OnBundleReceived);
            }
            catch (Exception ex)
            {
                _fetching = false;
                _status = "取得要求に失敗しました";
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "取得失敗", ex.Message, "OK");
                Repaint();
            }
        }

        private void OnBundleReceived(string json, byte[] data)
        {
            _fetching = false;

            if (data == null)
            {
                _status = "取得に失敗しました";
                Debug.LogError(
                    "[HierarchyRemoteExport] 取得に失敗しました: " + json);
                EditorUtility.DisplayDialog(
                    "取得失敗",
                    "サーバからプロジェクトを取得できませんでした。\n" + json,
                    "OK");
                Repaint();
                return;
            }

            if (!RemoteHierarchyReceive.CanExportNow(
                    out string blockReason))
            {
                _status = blockReason;
                Debug.LogError("[HierarchyRemoteExport] " + blockReason);
                Repaint();
                return;
            }

            SaveSettings();

            try
            {
                // 受信データ（ProjectDTO の JSON、UTF-8）をメモリ上で ModelContext に戻す。
                var projectDto = ProjectSerializer.FromJson(Encoding.UTF8.GetString(data));
                if (projectDto == null)
                {
                    _status = "受信データを読めませんでした";
                    EditorUtility.DisplayDialog(
                        "受信失敗", "受信データがプロジェクトの JSON ではありません。", "OK");
                    Repaint();
                    return;
                }

                var models = new List<ModelContext>();
                foreach (var modelDto in projectDto.models)
                {
                    var model = ModelSerializer.ToModelContext(modelDto);
                    if (model == null) continue;

                    // テクスチャ画像を Material に設定する（JSON はパスしか持たないため別に運んでいる）。
                    ModelTextureTransfer.Apply(model, modelDto.embeddedTextures);
                    models.Add(model);
                }

                Debug.Log(
                    $"[HierarchyRemoteExport] 受信 {models.Count} モデル（{projectDto.name}）");

                var outcome = new HierarchyPrefabExporter(
                    _options.Clone()).ExportModels(projectDto.name, models);

                _status = $"{outcome.Title}  {DateTime.Now:HH:mm:ss}";
                Repaint();
                EditorUtility.DisplayDialog(
                    outcome.Title, outcome.Text, "OK");
            }
            catch (Exception ex)
            {
                _status = "書き出しに失敗しました";
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "書き出し失敗", ex.Message, "OK");
                Repaint();
            }
        }
    }
}

#endif
