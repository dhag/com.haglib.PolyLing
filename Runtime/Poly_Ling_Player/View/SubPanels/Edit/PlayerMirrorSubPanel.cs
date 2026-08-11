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
        public Func<ToolContext>     GetToolContext;
        public Action<PanelCommand> SendCommand;
        public Func<ModelContext>   GetModel;
        public Func<int>            GetModelIndex;

        // ── 設定値 ────────────────────────────────────────────────────────
        private int           _mirrorAxis    = 0;    // 0=X, 1=Y, 2=Z
        private float         _threshold     = 0.0001f;
        private float         _planeOffset   = 0f;
        private bool          _flipU         = false;
        private MirrorBoundaryMode _boundaryMode = MirrorBoundaryMode.Threshold;
        private bool          _projectBoundary = true;
        private WriteBackMode _writeBackMode = WriteBackMode.OriginalSideOnly;

        // ── UI ────────────────────────────────────────────────────────────
        private Label         _statusLabel;
        private Label         _infoLabel;
        private Button        _btnBake, _btnUnbake;
        private VisualElement _threshRow;
        private Toggle        _projectToggle;

        private MeshContext ActiveMeshContext
            => GetToolContext?.Invoke()?.ActiveMeshContext ?? GetModel?.Invoke()?.ActiveMeshContext;

        /// <summary>実体化中なら現在の状態、未実体化なら null。</summary>
        private MirrorBakeResult BakeState => ActiveMeshContext?.MeshObject?.MirrorBakeState;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("Mirror Edit"));
            root.Add(new HelpBox(
                "選択メッシュ自身に反対側の実体を一時的に生やします。対称面をまたぐ処理"
                + "（法線スムージング等）を正しく効かせるための作業用機能で、別オブジェクトは作りません。"
                + "メッシュが見た目用のミラーモードだった場合は実体化と同時に解除されます。",
                HelpBoxMessageType.Info));

            // ── Step 1: Bake Mirror ───────────────────────────────────────
            root.Add(MakeSep("Step 1: ミラー実体化"));

            var axisChoices = new List<string> { "X", "Y", "Z" };
            var axisDd = new DropdownField("ミラー軸", axisChoices, _mirrorAxis);
            axisDd.style.color = new StyleColor(Color.white);
            axisDd.RegisterValueChangedCallback(e => _mirrorAxis = axisChoices.IndexOf(e.newValue));
            root.Add(axisDd);

            var offsetRow = new VisualElement(); offsetRow.style.flexDirection = FlexDirection.Row; offsetRow.style.marginBottom = 3;
            var offsetLbl = new Label("平面オフセット"); offsetLbl.style.width = 90; offsetLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            offsetLbl.style.color = new StyleColor(Color.white);
            var offsetField = new FloatField { value = _planeOffset }; offsetField.style.flexGrow = 1;
            offsetField.style.color = new StyleColor(Color.black);
            offsetField.tooltip = "ミラー平面をローカル座標でずらす。オブジェクト原点が対称面上に無いときに使う。";
            offsetField.RegisterValueChangedCallback(e => _planeOffset = e.newValue);
            offsetRow.Add(offsetLbl); offsetRow.Add(offsetField);
            root.Add(offsetRow);

            var bmChoices = new List<string> { "しきい値", "選択頂点" };
            var bmValues  = new[] { MirrorBoundaryMode.Threshold, MirrorBoundaryMode.SelectedVertices };
            var bmDd = new DropdownField("境界の決め方", bmChoices, 0);
            bmDd.style.color = new StyleColor(Color.white);
            bmDd.tooltip = "しきい値: ミラー平面からの距離で境界を判定 / 選択頂点: 選択している頂点を境界とみなす";
            bmDd.RegisterValueChangedCallback(e =>
            {
                int i = bmChoices.IndexOf(e.newValue);
                if (i < 0) return;
                _boundaryMode = bmValues[i];
                // しきい値モードは境界が平面上に居るので射影が既定、選択頂点モードは形が変わるため既定 OFF。
                _projectBoundary = _boundaryMode == MirrorBoundaryMode.Threshold;
                _projectToggle?.SetValueWithoutNotify(_projectBoundary);
                UpdateBoundaryRows();
                Refresh();
            });
            root.Add(bmDd);

            _threshRow = new VisualElement(); _threshRow.style.flexDirection = FlexDirection.Row; _threshRow.style.marginBottom = 3;
            var threshLbl = new Label("境界閾値"); threshLbl.style.width = 90; threshLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            threshLbl.style.color = new StyleColor(Color.white);
            var threshField = new FloatField { value = _threshold }; threshField.style.flexGrow = 1;
            threshField.style.color = new StyleColor(Color.black);
            threshField.RegisterValueChangedCallback(e => _threshold = Mathf.Max(0.00001f, e.newValue));
            _threshRow.Add(threshLbl); _threshRow.Add(threshField);
            root.Add(_threshRow);

            _projectToggle = new Toggle("境界頂点をミラー平面へ寄せる") { value = _projectBoundary };
            _projectToggle.style.color = new StyleColor(Color.white);
            _projectToggle.tooltip = "OFF にすると境界頂点の位置を動かさない。選択頂点が平面上に無いときは OFF が安全。";
            _projectToggle.RegisterValueChangedCallback(e => _projectBoundary = e.newValue);
            root.Add(_projectToggle);

            var flipUToggle = new Toggle("UV の U を反転") { value = _flipU };
            flipUToggle.style.color = new StyleColor(Color.white);
            flipUToggle.RegisterValueChangedCallback(e => _flipU = e.newValue);
            root.Add(flipUToggle);

            _btnBake = new Button(OnBakeMirror) { text = "ミラー実体化" };
            _btnBake.style.height = 28; _btnBake.style.marginTop = 4; _btnBake.style.marginBottom = 8;
            root.Add(_btnBake);

            // ── Step 2: 編集 ──────────────────────────────────────────────
            root.Add(MakeSep("Step 2: 編集"));
            var editHelp = new HelpBox(
                "実体化中は同じメッシュの中に反対側の頂点・面が入っています。"
                + "対称面をまたぐ処理（法線スムージング等）はこの状態で実行してください。",
                HelpBoxMessageType.None);
            editHelp.style.color = new StyleColor(Color.white);
            editHelp.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            editHelp.style.marginBottom = 8;
            root.Add(editHelp);

            // ── Step 3: 解除 ──────────────────────────────────────────────
            root.Add(MakeSep("Step 3: 解除（半身に戻す）"));

            var wbChoices = new List<string> { "元側を採用", "ミラー側を採用", "両側の平均" };
            var wbValues  = new[] { WriteBackMode.OriginalSideOnly, WriteBackMode.MirroredSideOnly, WriteBackMode.Average };
            var wbDd = new DropdownField("残す編集結果", wbChoices, 0);
            wbDd.style.color = new StyleColor(Color.white);
            wbDd.tooltip = "解除して半身に戻すとき、どちら側で行った編集を採用するか。";
            wbDd.RegisterValueChangedCallback(e => { int i = wbChoices.IndexOf(e.newValue); if (i >= 0) _writeBackMode = wbValues[i]; });
            root.Add(wbDd);

            var unbakeHelp = new HelpBox(
                "解除すると強制的に見た目・エクスポート用のミラーモード（結合）になります。",
                HelpBoxMessageType.None);
            unbakeHelp.style.color = new StyleColor(Color.white);
            unbakeHelp.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            unbakeHelp.style.marginBottom = 4;
            root.Add(unbakeHelp);

            _btnUnbake = new Button(OnUnbake) { text = "解除（半身に戻す）" };
            _btnUnbake.style.height = 28; _btnUnbake.style.marginTop = 4; _btnUnbake.style.marginBottom = 8;
            root.Add(_btnUnbake);

            _infoLabel = new Label();
            _infoLabel.style.fontSize   = 10;
            _infoLabel.style.whiteSpace = WhiteSpace.Normal;
            _infoLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            _infoLabel.style.marginTop  = 4;
            root.Add(_infoLabel);

            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(new Color(0.6f, 0.8f, 0.6f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);

            UpdateBoundaryRows();
            Refresh();
        }

        private void UpdateBoundaryRows()
        {
            bool useThreshold = _boundaryMode == MirrorBoundaryMode.Threshold;
            if (_threshRow != null)
                _threshRow.style.display = useThreshold ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Refresh()
        {
            if (_infoLabel == null) return;

            var mc = ActiveMeshContext;
            string meshName = mc?.Name ?? "(メッシュ未選択)";
            int selVerts    = mc?.Selection?.Vertices.Count ?? 0;
            var mo          = mc?.MeshObject;

            var bake = mo?.MirrorBakeState;

            string stateLine;
            if (mo == null)
            {
                stateLine = "状態: -";
            }
            else if (bake == null)
            {
                stateLine = $"状態: 未実体化   MirrorType={mc.MirrorType}   V:{mo.VertexCount} F:{mo.FaceCount}";
            }
            else
            {
                string boundaryDesc = bake.BoundaryVertices == null
                    ? "しきい値 " + bake.Threshold
                    : "選択頂点 " + bake.BoundaryVertices.Length + " 点";
                stateLine =
                    $"状態: 実体化中   MirrorType={mc.MirrorType}   V:{mo.VertexCount} F:{mo.FaceCount}\n" +
                    $"元 {bake.OriginalVertexCount} 頂点 / 元 {bake.OriginalFaceCount} 面 / 境界 {boundaryDesc}";
            }

            _infoLabel.text = $"対象: {meshName}   選択頂点: {selVerts}\n{stateLine}";

            bool baked = bake != null;
            _btnBake?.SetEnabled(mo != null && !baked);
            _btnUnbake?.SetEnabled(baked);
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnBakeMirror()
        {
            var model = GetModel?.Invoke();
            if (model == null) { SetStatus("モデルがありません"); return; }

            var mc = ActiveMeshContext;
            if (mc?.MeshObject == null) { SetStatus("メッシュを選択してください"); return; }

            if (mc.MeshObject.MirrorBakeState != null)
            { SetStatus("既に実体化されています"); return; }

            if (_boundaryMode == MirrorBoundaryMode.SelectedVertices
                && (mc.Selection?.Vertices.Count ?? 0) == 0)
            { SetStatus("境界にする頂点を選択してください"); return; }

            if (SendCommand == null) { SetStatus("コマンド送信先が未設定です"); return; }

            SendCommand.Invoke(new BakeMirrorCommand(
                GetModelIndex?.Invoke() ?? 0, model.IndexOf(mc),
                _mirrorAxis, _threshold, _flipU,
                _planeOffset, _boundaryMode, _projectBoundary));

            var bake = mc.MeshObject.MirrorBakeState;
            SetStatus(bake == null
                ? "実体化に失敗しました（コンソールの [MirrorBake] ログを確認）"
                : $"実体化: {bake.OriginalVertexCount} → {mc.MeshObject.VertexCount} 頂点");
            Refresh();
        }

        private void OnUnbake()
        {
            var model = GetModel?.Invoke();
            if (model == null) { SetStatus("モデルがありません"); return; }

            var mc = ActiveMeshContext;
            if (mc?.MeshObject == null) { SetStatus("メッシュを選択してください"); return; }

            if (mc.MeshObject.MirrorBakeState == null)
            { SetStatus("このメッシュは実体化されていません"); return; }

            if (SendCommand == null) { SetStatus("コマンド送信先が未設定です"); return; }

            int vertsBefore = mc.MeshObject.VertexCount;

            SendCommand.Invoke(new UnbakeMirrorCommand(
                GetModelIndex?.Invoke() ?? 0, model.IndexOf(mc), _writeBackMode));

            SetStatus(mc.MeshObject.MirrorBakeState != null
                ? "解除に失敗しました（コンソールの [MirrorBake] ログを確認）"
                : $"解除: {vertsBefore} → {mc.MeshObject.VertexCount} 頂点");
            Refresh();
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
