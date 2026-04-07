// PlayerMirrorSubPanel.cs
// MirrorEditTool（IMGUI）を UIToolkit サブパネルとして移植。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Ops;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerMirrorSubPanel
    {
        public Func<ToolContext> GetToolContext;

        // ── 設定値 ────────────────────────────────────────────────────────
        private int           _mirrorAxis    = 0;    // 0=X, 1=Y, 2=Z
        private float         _threshold     = 0.0001f;
        private bool          _flipU         = false;
        private WriteBackMode _writeBackMode = WriteBackMode.OriginalSideOnly;
        private float         _blendWeight   = 0.5f;

        // ── 実行状態 ──────────────────────────────────────────────────────
        private MirrorBakeResult _lastBakeResult   = null;
        private string           _sourceMeshName   = null;
        private string           _bakedMeshName    = null;
        private string           _writeBackMeshName = null;

        // ── UI ────────────────────────────────────────────────────────────
        private Label         _statusLabel;
        private Button        _btnWriteBack, _btnBlend;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("Mirror Edit"));
            root.Add(new HelpBox("選択メッシュのミラーを実体化し、編集後に元メッシュへ書き戻します。", HelpBoxMessageType.Info));

            // ── Step 1: Bake Mirror ───────────────────────────────────────
            root.Add(MakeSep("Step 1: Bake Mirror"));

            var axisChoices = new List<string> { "X", "Y", "Z" };
            var axisDd = new DropdownField("ミラー軸", axisChoices, _mirrorAxis);
            axisDd.style.color = new StyleColor(Color.white);
            axisDd.RegisterValueChangedCallback(e => _mirrorAxis = axisChoices.IndexOf(e.newValue));
            root.Add(axisDd);

            var threshRow = new VisualElement(); threshRow.style.flexDirection = FlexDirection.Row; threshRow.style.marginBottom = 3;
            var threshLbl = new Label("境界閾値"); threshLbl.style.width = 70; threshLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            threshLbl.style.color = new StyleColor(Color.white);
            var threshField = new FloatField { value = _threshold }; threshField.style.flexGrow = 1;
            threshField.style.color = new StyleColor(Color.black);
            threshField.RegisterValueChangedCallback(e => _threshold = Mathf.Max(0.00001f, e.newValue));
            threshRow.Add(threshLbl); threshRow.Add(threshField);
            root.Add(threshRow);

            var flipUToggle = new Toggle("UV の U を反転") { value = _flipU };
            flipUToggle.style.color = new StyleColor(Color.white);
            flipUToggle.RegisterValueChangedCallback(e => _flipU = e.newValue);
            root.Add(flipUToggle);

            var bakeBtn = new Button(OnBakeMirror) { text = "Bake Mirror" };
            bakeBtn.style.height = 28; bakeBtn.style.marginTop = 4; bakeBtn.style.marginBottom = 8;
            root.Add(bakeBtn);

            // ── Step 2: Edit ───────────────────────────────────────────────
            root.Add(MakeSep("Step 2: Edit"));
            var editHelp = new HelpBox("Bake後のメッシュをメッシュリストで選択して編集してください。", HelpBoxMessageType.None);
            editHelp.style.color = new StyleColor(Color.white);
            editHelp.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            editHelp.style.marginBottom = 8;
            root.Add(editHelp);

            // ── Step 3: Write Back ────────────────────────────────────────
            root.Add(MakeSep("Step 3: Write Back"));

            var wbChoices = new List<string> { "OriginalSideOnly", "MirroredSideOnly", "Average" };
            var wbValues  = new[] { WriteBackMode.OriginalSideOnly, WriteBackMode.MirroredSideOnly, WriteBackMode.Average };
            var wbDd = new DropdownField("書き戻しモード", wbChoices, 0);
            wbDd.style.color = new StyleColor(Color.white);
            wbDd.RegisterValueChangedCallback(e => { int i = wbChoices.IndexOf(e.newValue); if (i >= 0) _writeBackMode = wbValues[i]; });
            root.Add(wbDd);

            _btnWriteBack = new Button(OnWriteBack) { text = "Write Back" };
            _btnWriteBack.style.height = 28; _btnWriteBack.style.marginTop = 4; _btnWriteBack.style.marginBottom = 8;
            root.Add(_btnWriteBack);

            // ── Step 4: Blend ─────────────────────────────────────────────
            root.Add(MakeSep("Step 4: Blend"));

            var blendRow = new VisualElement(); blendRow.style.flexDirection = FlexDirection.Row; blendRow.style.marginBottom = 4;
            var blendLbl = new Label("Blend"); blendLbl.style.width = 50; blendLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            blendLbl.style.color = new StyleColor(Color.white);
            var blendSlider = new Slider(0f, 1f) { value = _blendWeight }; blendSlider.style.flexGrow = 1;
            blendSlider.style.color = new StyleColor(Color.white);
            blendSlider.RegisterValueChangedCallback(e => _blendWeight = e.newValue);
            blendRow.Add(blendLbl); blendRow.Add(blendSlider);
            root.Add(blendRow);

            _btnBlend = new Button(OnBlend) { text = "Blend" };
            _btnBlend.style.height = 28; _btnBlend.style.marginBottom = 4;
            root.Add(_btnBlend);

            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(new Color(0.6f, 0.8f, 0.6f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);
        }

        public void Refresh() { }

        // ── Operations ───────────────────────────────────────────────────
        private void OnBakeMirror()
        {
            var tc = GetToolContext?.Invoke();
            if (tc == null) { SetStatus("ToolContext 未設定"); return; }
            var mc = tc.FirstSelectedMeshContext;
            if (mc?.MeshObject == null) { SetStatus("メッシュを選択してください"); return; }

            _sourceMeshName = mc.Name;
            var (bakedMesh, bakeResult) = MirrorBaker.BakeMirror(mc.MeshObject, _mirrorAxis, 0f, _threshold, _flipU);
            if (bakedMesh == null || bakeResult == null) { SetStatus("Bake に失敗しました"); return; }

            _lastBakeResult = bakeResult;
            _bakedMeshName  = bakedMesh.Name;

            var newMc = new MeshContext
            {
                Name       = bakedMesh.Name,
                MeshObject = bakedMesh,
                Materials  = new List<Material>(mc.Materials ?? new List<Material>()),
            };
            newMc.UnityMesh           = bakedMesh.ToUnityMesh();
            newMc.UnityMesh.name      = bakedMesh.Name;
            newMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
            tc.AddMeshContext?.Invoke(newMc);

            SetStatus($"Bake 完了: {mc.MeshObject.VertexCount} → {bakedMesh.VertexCount} 頂点");
            tc.Repaint?.Invoke();
        }

        private void OnWriteBack()
        {
            var tc = GetToolContext?.Invoke();
            if (tc == null || _lastBakeResult == null) { SetStatus("先に Bake を実行してください"); return; }
            var editedMc = tc.FirstSelectedMeshContext;
            if (editedMc == null || editedMc.Name != _bakedMeshName)
            { SetStatus($"Bake 済みメッシュ '{_bakedMeshName}' を選択してください"); return; }

            var originalMc = FindMeshContextByName(tc, _sourceMeshName);
            if (originalMc?.MeshObject == null) { SetStatus($"元メッシュ '{_sourceMeshName}' が見つかりません"); return; }

            var resultMesh = MirrorBaker.WriteBack(editedMc.MeshObject, originalMc.MeshObject, _lastBakeResult, _writeBackMode);
            if (resultMesh == null) { SetStatus("WriteBack に失敗しました"); return; }

            string newName    = _sourceMeshName + "_WriteBack";
            resultMesh.Name   = newName;
            _writeBackMeshName = newName;

            var newMc = new MeshContext
            {
                Name          = newName, MeshObject = resultMesh,
                Materials     = new List<Material>(originalMc.Materials ?? new List<Material>()),
                MirrorType    = originalMc.MirrorType,
                MirrorAxis    = originalMc.MirrorAxis,
                MirrorDistance = originalMc.MirrorDistance,
            };
            newMc.UnityMesh           = resultMesh.ToUnityMesh();
            newMc.UnityMesh.name      = newName;
            newMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
            tc.AddMeshContext?.Invoke(newMc);

            SetStatus($"WriteBack 完了: '{newName}'");
            tc.Repaint?.Invoke();
        }

        private void OnBlend()
        {
            var tc = GetToolContext?.Invoke();
            if (tc == null) { SetStatus("ToolContext 未設定"); return; }

            var sourceMc    = FindMeshContextByName(tc, _sourceMeshName);
            var writeBackMc = FindMeshContextByName(tc, _writeBackMeshName);
            if (sourceMc?.MeshObject == null || writeBackMc?.MeshObject == null)
            { SetStatus("ソースと WriteBack のメッシュが必要です"); return; }

            var srcMesh = sourceMc.MeshObject;
            var wbMesh  = writeBackMc.MeshObject;
            if (srcMesh.VertexCount != wbMesh.VertexCount)
            { SetStatus($"頂点数不一致: {srcMesh.VertexCount} vs {wbMesh.VertexCount}"); return; }

            var blended = srcMesh.Clone();
            string blendName = $"{_sourceMeshName}_Blend{Mathf.RoundToInt(_blendWeight * 100)}";
            blended.Name = blendName;
            for (int i = 0; i < blended.VertexCount; i++)
                blended.Vertices[i].Position = Vector3.Lerp(srcMesh.Vertices[i].Position, wbMesh.Vertices[i].Position, _blendWeight);
            blended.RecalculateSmoothNormals();

            var newMc = new MeshContext
            {
                Name = blendName, MeshObject = blended,
                Materials = new List<Material>(sourceMc.Materials ?? new List<Material>()),
                MirrorType = sourceMc.MirrorType, MirrorAxis = sourceMc.MirrorAxis, MirrorDistance = sourceMc.MirrorDistance,
            };
            newMc.UnityMesh           = blended.ToUnityMesh();
            newMc.UnityMesh.name      = blendName;
            newMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
            tc.AddMeshContext?.Invoke(newMc);

            SetStatus($"Blend 完了: '{blendName}'");
            tc.Repaint?.Invoke();
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private static MeshContext FindMeshContextByName(ToolContext tc, string name)
        {
            if (tc?.Model == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < tc.Model.MeshContextCount; i++)
            {
                var mc = tc.Model.GetMeshContext(i);
                if (mc != null && mc.Name == name) return mc;
            }
            return null;
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static VisualElement MakeSep(string title = null)
        {
            var container = new VisualElement(); container.style.marginTop = 4; container.style.marginBottom = 4;
            var line = new VisualElement(); line.style.height = 1; line.style.backgroundColor = new StyleColor(Color.white);
            container.Add(line);
            if (title != null)
            {
                var l = new Label(title); l.style.fontSize = 10; l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.marginTop = 3;
                l.style.color = new StyleColor(Color.white);
                container.Add(l);
            }
            return container;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginBottom = 3; return l;
        }
    }
}
