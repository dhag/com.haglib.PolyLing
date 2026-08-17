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
        public Func<ToolContext>     GetToolContext;
        public Action<PanelCommand> SendCommand;
        public Func<ModelContext>   GetModel;
        public Func<int>            GetModelIndex;

        // 他のファイル読込パネルと同じく RecentPaths に端末ローカル保存する。
        private const string BeforePathKey = "MediaPipe.Before";
        private const string AfterPathKey  = "MediaPipe.After";
        private const string TriPathKey    = "MediaPipe.Triangles";

        private Label         _warningLabel;
        private Label         _fileStatusLabel;
        private Button        _btnExecute;
        private Label         _statusLabel;

        private TextField     _beforeField;
        private TextField     _afterField;
        private TextField     _triField;

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
            _beforeField = AddPathRow(root, "変形前ランドマークJSON", BeforePathKey, "変形前ランドマークJSONを選択");
            _afterField  = AddPathRow(root, "変形後ランドマークJSON", AfterPathKey,  "変形後ランドマークJSONを選択");
            _triField    = AddPathRow(root, "面インデックスJSON",     TriPathKey,    "面インデックスJSONを選択");

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
        }

        public void Refresh()
        {
            if (_warningLabel == null) return;
            var tc = GetToolContext?.Invoke();
            if (tc?.ActiveMeshContext?.MeshObject == null)
            {
                _warningLabel.text          = tc == null ? "ToolContext 未設定" : "メッシュが選択されていません";
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
        }

        private static bool Exists(TextField f) =>
            f != null && !string.IsNullOrEmpty(f.value) && File.Exists(f.value);

        private static string Mark(bool ok) => ok ? "✓" : "✗";

        /// <summary>
        /// 他のIOパネルと同じ「セクション見出し + [...] + パス欄」の1組を追加する。
        /// 値は RecentPaths に write-through し、変更時にファイル存在表示を更新する。
        /// </summary>
        private TextField AddPathRow(VisualElement parent, string label, string prefKey, string dialogTitle)
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
                string path = PlayerIoUiKit.AskLoadPath(dialogTitle, field.value, "json");
                if (!string.IsNullOrEmpty(path)) field.value = path;
            }));

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

            var model = GetModel?.Invoke();
            var tc    = GetToolContext?.Invoke();
            var mc    = tc?.ActiveMeshContext ?? model?.ActiveMeshContext;
            if (mc?.MeshObject == null) { SetStatus("メッシュが選択されていません"); return; }

            int masterIdx = model?.IndexOf(mc) ?? -1;
            int modelIdx  = GetModelIndex?.Invoke() ?? 0;

            if (SendCommand != null && masterIdx >= 0)
            {
                SendCommand.Invoke(new MediaPipeFaceDeformCommand(
                    modelIdx, masterIdx, beforePath, afterPath, triPath));
                SetStatus("MediaPipe変形コマンドを送信しました");
                return;
            }
            // フォールバック
            try
            {
                var sourceMesh    = mc.MeshObject;
                var beforeLM      = MediaPipeFaceDeformer.LoadLandmarks(beforePath);
                var afterLM       = MediaPipeFaceDeformer.LoadLandmarks(afterPath);
                var triangles     = MediaPipeFaceDeformer.ParseTrianglesJson(File.ReadAllText(triPath));
                int vertexCount   = sourceMesh.VertexCount;
                var positions     = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++) positions[i] = sourceMesh.Vertices[i].Position;
                var deformer = new MediaPipeFaceDeformer();
                deformer.SetBaseMesh(beforeLM, triangles);
                int bindCount = deformer.Bind(positions);
                deformer.Apply(afterLM, positions);
                MeshObject cloned = sourceMesh.Clone();
                cloned.Name = sourceMesh.Name + "_MP";
                for (int i = 0; i < vertexCount; i++) cloned.Vertices[i].Position = positions[i];
                var newMc = new MeshContext
                {
                    MeshObject = cloned,
                    Materials  = new List<Material>(mc.Materials ?? new List<Material>()),
                };
                newMc.UnityMesh = cloned.ToUnityMesh(); newMc.UnityMesh.name = cloned.Name; newMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                tc?.AddMeshContext?.Invoke(newMc);
                SetStatus($"変形メッシュを作成しました。バインド: {bindCount}/{vertexCount} 頂点");
                tc?.Repaint?.Invoke();
            }
            catch (Exception ex) { SetStatus($"エラー: {ex.Message}"); UnityEngine.Debug.LogException(ex); }
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
