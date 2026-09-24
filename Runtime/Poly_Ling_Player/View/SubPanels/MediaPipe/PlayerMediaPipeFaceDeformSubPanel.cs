// PlayerMediaPipeFaceDeformSubPanel.cs
// MediaPipeFaceDeformPanel の Player 版サブパネル。UXML/AssetDatabase 除去。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Tools.MediaPipe;
using Poly_Ling.Core;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Player
{
    public class PlayerMediaPipeFaceDeformSubPanel
    {
        /// <summary>プロジェクトの窓口（操作経路統一計画.md E）。</summary>
        public Func<Poly_Ling.View.IProjectView> GetView;
        public Action<PanelCommand> SendCommand;
        /// <summary>ツールの窓口（表情転写 "faceTransfer" の撮影・状態に使う）。</summary>
        public IToolSurface Surface;

        private const string TransferTool = "faceTransfer";

        // 他のファイル読込パネルと同じく RecentPaths に端末ローカル保存する。
        private const string BeforePathKey = "MediaPipe.Before";
        private const string AfterPathKey  = "MediaPipe.After";
        private const string TriPathKey    = "MediaPipe.Triangles";

        // UI 自動操作の ID は "mediaPipe.<下の Id>"（UiControlAttribute.cs）。
        // 3 つの JSON はどれもダイアログの初期値として使う。
        [UiControl("warning", Safety = UiSafety.ReadOnly, Description = "警告（出ていないときは非表示）")]
        private Label         _warningLabel;
        [UiControl("fileStatus", Safety = UiSafety.ReadOnly, Description = "指定した 3 つのファイルの状態")]
        private Label         _fileStatusLabel;
        [UiControl("run", Safety = UiSafety.SafeWrite, Description = "ランドマークの差でモデルを変形する")]
        private Button        _btnExecute;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label         _statusLabel;

        [UiControl("beforePath", Description = "変形前ランドマーク JSON のパス")]
        private TextField     _beforeField;
        [UiControl("afterPath", Description = "変形後ランドマーク JSON のパス")]
        private TextField     _afterField;
        [UiControl("triPath", Description = "面インデックス JSON のパス")]
        private TextField     _triField;
        [UiControl("browseBefore", Safety = UiSafety.UserOnly, Description = "変形前ランドマーク JSON の [...]（ファイル選択ダイアログを開く）")]
        private Button        _btnBrowseBefore;
        [UiControl("browseAfter", Safety = UiSafety.UserOnly, Description = "変形後ランドマーク JSON の [...]（ファイル選択ダイアログを開く）")]
        private Button        _btnBrowseAfter;
        [UiControl("browseTri", Safety = UiSafety.UserOnly, Description = "面インデックス JSON の [...]（ファイル選択ダイアログを開く）")]
        private Button        _btnBrowseTri;

        // 表情転写（BEFORE＝メイン 3D 画面を撮って MediaPipe クライアントで検出、AFTER＝クライアントから届いた最新の顔）
        [UiControl("transfer.captureBefore", Safety = UiSafety.SafeWrite, Description = "メイン 3D 画面を BEFORE として撮り、MediaPipe クライアントへ顔検出を頼む")]
        private Button        _btnCaptureBefore;
        [UiControl("transfer.run", Safety = UiSafety.SafeWrite, Description = "表情転写を実行する（新しいメッシュとして足す）")]
        private Button        _btnTransfer;
        [UiControl("transfer.status", Safety = UiSafety.ReadOnly, Description = "表情転写の状態")]
        private Label         _transferStatusLabel;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("MediaPipe フェイス変形"));

            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            root.Add(_warningLabel);

            root.Add(new HelpBox(
                "変形前後のランドマークJSONと、面インデックスJSONを指定してください。\n" +
                "カレントメッシュの頂点XYをMediaPipe変形に基づいて変形し、新メッシュを生成します。\n" +
                "面インデックスは4頂点以上の多角形も読み込めます（内部で三角形へ分解します）。",
                HelpBoxMessageType.None));

            // ── ファイル指定（他のIOパネルと同じ [...] + パス欄 + RecentPaths） ──
            _beforeField = AddPathRow(root, "変形前ランドマークJSON", BeforePathKey, "変形前ランドマークJSONを選択", out _btnBrowseBefore);
            _afterField  = AddPathRow(root, "変形後ランドマークJSON", AfterPathKey,  "変形後ランドマークJSONを選択", out _btnBrowseAfter);
            _triField    = AddPathRow(root, "面インデックスJSON",     TriPathKey,    "面インデックスJSONを選択",     out _btnBrowseTri);

            _fileStatusLabel = new Label();
            _fileStatusLabel.style.color = new StyleColor(Color.white);
            _fileStatusLabel.style.fontSize     = 10;
            _fileStatusLabel.style.marginBottom = 4;
            _fileStatusLabel.style.whiteSpace   = WhiteSpace.Normal;
            root.Add(_fileStatusLabel);

            _btnExecute = new Button(OnExecute) { text = "実行" };
            _btnExecute.style.height    = 28;
            _btnExecute.style.marginTop = 6;
            root.Add(_btnExecute);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.color      = new StyleColor(Color.white);
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_statusLabel);

            BuildTransferSection(root);
        }

        private void BuildTransferSection(VisualElement root)
        {
            var head = SecLabel("表情転写（MediaPipe クライアント）");
            head.style.marginTop = 8;
            root.Add(head);
            root.Add(new HelpBox(
                "1. モーションパネルのライブ受信を受け入れ許可にし、MediaPipe クライアントを接続する。\n" +
                "2. メイン 3D 画面にターゲットの顔を映し（ワイヤー・頂点などの表示は切る）、「BEFORE を撮る」。\n" +
                "3. MediaPipe クライアントから人の顔を送る（1 枚送る／リアルタイム）。\n" +
                "4. 変形するメッシュを選び（複数可）、「転写」。面インデックス JSON は空なら組み込みを使う。",
                HelpBoxMessageType.None));

            _btnCaptureBefore = new Button(() =>
            {
                Surface?.Invoke(TransferTool, "captureBefore");
                RefreshTransfer();
            }) { text = "BEFORE を撮る（メイン 3D 画面）" };
            root.Add(_btnCaptureBefore);

            _transferStatusLabel = new Label();
            _transferStatusLabel.style.fontSize   = 10;
            _transferStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_transferStatusLabel);

            _btnTransfer = new Button(OnTransfer) { text = "転写" };
            _btnTransfer.style.height = 28;
            root.Add(_btnTransfer);

            RefreshTransfer();
        }

        /// <summary>表情転写の状態表示とボタンの有効/無効を更新する（faceTransfer の状態が変わったとき・AFTER を受けたときに呼ぶ）。</summary>
        public void RefreshTransfer()
        {
            if (_transferStatusLabel == null || Surface == null) return;
            bool hasBefore = Surface.GetBool(TransferTool, "hasBefore");
            bool waiting   = Surface.GetBool(TransferTool, "waitingBefore");
            bool hasAfter  = Surface.GetBool(TransferTool, "hasAfter");
            string size    = Surface.GetString(TransferTool, "beforeImageSize");
            _transferStatusLabel.text =
                $"{Mark(hasBefore)} BEFORE{(string.IsNullOrEmpty(size) ? "" : $"（{size}）")}{(waiting ? "　検出待ち" : "")}\n" +
                $"{Mark(hasAfter)} AFTER\n" +
                $"{TriMark()} 面インデックス{(TriEmpty() ? "（組み込み）" : "")}\n" +
                Surface.GetString(TransferTool, "status");
            _btnTransfer?.SetEnabled(hasBefore && hasAfter && (TriEmpty() || Exists(_triField)));
        }

        // 面インデックス欄が空か（空なら組み込みの三角形を使う）
        private bool TriEmpty() => _triField == null || string.IsNullOrWhiteSpace(_triField.value);
        private string TriMark() => TriEmpty() || Exists(_triField) ? "✓" : "×";

        private void OnTransfer()
        {
            if (!TriEmpty() && !Exists(_triField)) { SetStatus("面インデックス JSON が見つかりません（空にすると組み込みを使います）"); return; }
            var view  = GetView?.Invoke();
            var model = view?.CurrentModel;
            if (model == null) { SetStatus("モデルがありません"); return; }

            // 選択中の描画オブジェクト全部。無ければ編集対象メッシュ 1 つ。
            int[] indices = model.SelectedDrawableIndices;
            if (indices == null || indices.Length == 0)
                indices = model.ActiveMeshIndex >= 0 ? new[] { model.ActiveMeshIndex } : Array.Empty<int>();
            if (indices.Length == 0) { SetStatus("メッシュが選択されていません"); return; }

            SendCommand?.Invoke(new FaceExpressionTransferCommand(
                view.CurrentModelIndex, indices, TriEmpty() ? "" : _triField.value));
            SetStatus($"表情転写コマンドを送信しました（{indices.Length} 個）");
        }

        public void Refresh()
        {
            if (_warningLabel == null) return;
            var mesh = GetView?.Invoke()?.CurrentModel?.ActiveMesh;
            if (mesh == null)
            {
                _warningLabel.text          = "メッシュが選択されていません";
                _warningLabel.style.display = DisplayStyle.Flex;
                return;
            }
            _warningLabel.style.display = DisplayStyle.None;

            UpdateFileStatus();
        }

        /// <summary>指定された3ファイルの存在を表示し、実行ボタンの有効/無効を更新する。</summary>
        private void UpdateFileStatus()
        {
            if (_fileStatusLabel == null || _btnExecute == null) return;

            bool beforeOk = Exists(_beforeField);
            bool afterOk  = Exists(_afterField);
            bool triOk    = Exists(_triField);

            _fileStatusLabel.text =
                $"{Mark(beforeOk)} 変形前ランドマーク\n" +
                $"{Mark(afterOk)} 変形後ランドマーク\n" +
                $"{Mark(triOk)} 面インデックス";
            _btnExecute.SetEnabled(beforeOk && afterOk && triOk);
            RefreshTransfer();
        }

        private static bool Exists(TextField f) =>
            f != null && !string.IsNullOrEmpty(f.value) && File.Exists(f.value);

        private static string Mark(bool ok) => ok ? "✓" : "×";

        /// <summary>
        /// 他のIOパネルと同じ「セクション見出し + [...] + パス欄」の1組を追加する。
        /// 値は RecentPaths に write-through し、変更時にファイル存在表示を更新する。
        /// </summary>
        private TextField AddPathRow(VisualElement parent, string label, string prefKey, string dialogTitle,
                                     out Button browseButton)
        {
            parent.Add(PlayerIoUiKit.SectionLabel(label));

            var field = new TextField();
            field.RegisterValueChangedCallback(e =>
            {
                RecentPaths.Set(prefKey, e.newValue);
                UpdateFileStatus();
            });

            parent.Add(PlayerIoUiKit.PathRow(field, () =>
            {
                string path = PlayerIoUiKit.AskLoadPath(dialogTitle, prefKey, field.value, "json");
                if (!string.IsNullOrEmpty(path)) field.value = path;
            }, out browseButton));

            field.SetValueWithoutNotify(RecentPaths.Get(prefKey));
            return field;
        }

        private void OnExecute()
        {
            string beforePath = _beforeField?.value ?? string.Empty;
            string afterPath  = _afterField?.value  ?? string.Empty;
            string triPath    = _triField?.value    ?? string.Empty;

            if (!Exists(_beforeField) || !Exists(_afterField) || !Exists(_triField))
            {
                SetStatus("ファイルが指定されていません");
                return;
            }

            var view      = GetView?.Invoke();
            int masterIdx = view?.CurrentModel?.ActiveMeshIndex ?? -1;
            if (masterIdx < 0) { SetStatus("メッシュが選択されていません"); return; }

            // 実行はコマンドだけで行う（パネルからの直接実行の経路は持たない。操作経路統一計画.md J）。
            SendCommand?.Invoke(new MediaPipeFaceDeformCommand(
                view.CurrentModelIndex, masterIdx, beforePath, afterPath, triPath));
            SetStatus("MediaPipe変形コマンドを送信しました");
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
