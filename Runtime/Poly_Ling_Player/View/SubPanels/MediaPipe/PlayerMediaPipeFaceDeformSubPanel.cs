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

namespace Poly_Ling.Player
{
    public class PlayerMediaPipeFaceDeformSubPanel
    {
        public Func<ToolContext>     GetToolContext;
        public Action<PanelCommand> SendCommand;
        public Func<ModelContext>   GetModel;
        public Func<int>            GetModelIndex;

        private const string BasePath       = "Assets/MediaPipe";
        private const string BeforeFileName = "before.json";
        private const string AfterFileName  = "after.json";
        private const string TriFileName    = "triangles.json";

        private Label         _warningLabel;
        private Label         _fileStatusLabel;
        private Button        _btnExecute;
        private Label         _statusLabel;

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
                $"Assets/MediaPipe/ に before.json / after.json / triangles.json を配置してください。\n" +
                $"カレントメッシュの頂点XYをMediaPipe変形に基づいて変形し、新メッシュを生成します。",
                HelpBoxMessageType.None));

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
            if (tc?.FirstDrawableMeshContext?.MeshObject == null)
            {
                _warningLabel.text          = tc == null ? "ToolContext 未設定" : "メッシュが選択されていません";
                _warningLabel.style.display = DisplayStyle.Flex;
                return;
            }
            _warningLabel.style.display = DisplayStyle.None;

            // ファイル存在確認
            bool beforeOk  = File.Exists(Path.Combine(BasePath, BeforeFileName));
            bool afterOk   = File.Exists(Path.Combine(BasePath, AfterFileName));
            bool triOk     = File.Exists(Path.Combine(BasePath, TriFileName));
            _fileStatusLabel.text =
                $"{(beforeOk ? "✓" : "✗")} before.json\n" +
                $"{(afterOk  ? "✓" : "✗")} after.json\n" +
                $"{(triOk    ? "✓" : "✗")} triangles.json";
            _btnExecute.SetEnabled(beforeOk && afterOk && triOk);
        }

        private void OnExecute()
        {
            string beforePath = Path.Combine(BasePath, BeforeFileName);
            string afterPath  = Path.Combine(BasePath, AfterFileName);
            string triPath    = Path.Combine(BasePath, TriFileName);

            var model = GetModel?.Invoke();
            var tc    = GetToolContext?.Invoke();
            var mc    = tc?.FirstDrawableMeshContext ?? model?.FirstDrawableMeshContext;
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
