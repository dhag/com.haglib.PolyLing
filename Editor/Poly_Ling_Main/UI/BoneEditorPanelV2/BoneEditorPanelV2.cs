// BoneEditorPanelV2.cs
// ボーンエディタパネル V2（コード構築 UIToolkit）
// PanelContext（選択変更通知）+ ToolContext（実処理）ハイブリッド

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    public class BoneEditorPanelV2 : EditorWindow
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _panelCtx;
        private ToolContext  _toolCtx;

        private ModelContext       Model          => _toolCtx?.Model;
        private MeshUndoController UndoController => _toolCtx?.UndoController;

        // ================================================================
        // UI 要素
        // ================================================================

        private Label         _warningLabel;
        private Label         _selectionCountLabel;
        private VisualElement _boneDetail;
        private Label         _boneNameLabel;
        private Label         _masterIndexLabel;
        private Label         _boneIndexLabel;
        private Label         _parentBoneLabel;
        private Label         _worldPosLabel;
        private Label         _statusLabel;

        // ================================================================
        // Open
        // ================================================================

        public static BoneEditorPanelV2 Open(PanelContext panelCtx, ToolContext toolCtx)
        {
            var w = GetWindow<BoneEditorPanelV2>();
            w.titleContent = new GUIContent("ボーンエディタ");
            w.minSize = new Vector2(280, 300);
            w.SetContexts(panelCtx, toolCtx);
            w.Show();
            return w;
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        private void SetContexts(PanelContext panelCtx, ToolContext toolCtx)
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
            UnregisterUndoCallback();

            _panelCtx = panelCtx;
            _toolCtx  = toolCtx;

            if (_panelCtx != null) _panelCtx.OnViewChanged += OnViewChanged;
            RegisterUndoCallback();

            RefreshAll();
        }

        // ================================================================
        // Undo コールバック
        // ================================================================

        private void RegisterUndoCallback()
        {
            var undo = _toolCtx?.UndoController;
            if (undo != null) undo.OnUndoRedoPerformed += OnUndoRedoPerformed;
        }

        private void UnregisterUndoCallback()
        {
            var undo = _toolCtx?.UndoController;
            if (undo != null) undo.OnUndoRedoPerformed -= OnUndoRedoPerformed;
        }

        private void OnUndoRedoPerformed() => RefreshAll();

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            if (_panelCtx != null)
            {
                _panelCtx.OnViewChanged -= OnViewChanged;
                _panelCtx.OnViewChanged += OnViewChanged;
            }
            RegisterUndoCallback();
        }

        private void OnDisable()
        {
            UnregisterUndoCallback();
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void OnDestroy()
        {
            UnregisterUndoCallback();
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void CreateGUI()
        {
            BuildUI(rootVisualElement);
            RefreshAll();
        }

        // ================================================================
        // ViewChanged
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (kind == ChangeKind.Selection || kind == ChangeKind.ModelSwitch || kind == ChangeKind.Attributes)
                RefreshAll();
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft   = 6;
            root.style.paddingRight  = 6;
            root.style.paddingTop    = 6;
            root.style.paddingBottom = 6;

            // 警告
            _warningLabel = new Label();
            _warningLabel.style.display     = DisplayStyle.None;
            _warningLabel.style.color       = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            // 選択カウント
            _selectionCountLabel = new Label();
            _selectionCountLabel.style.marginBottom = 4;
            root.Add(_selectionCountLabel);

            // ボタン行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection  = FlexDirection.Row;
            btnRow.style.marginBottom   = 8;
            var btnReset = new Button(OnResetPose)  { text = "ポーズリセット" };
            btnReset.style.flexGrow  = 1;
            btnReset.style.marginRight = 4;
            var btnFocus = new Button(OnFocusBone)  { text = "フォーカス" };
            btnFocus.style.flexGrow = 1;
            btnRow.Add(btnReset);
            btnRow.Add(btnFocus);
            root.Add(btnRow);

            // 詳細エリア
            _boneDetail = new VisualElement();
            _boneDetail.style.display = DisplayStyle.None;
            root.Add(_boneDetail);

            AddRow(_boneDetail, "ボーン名",    out _boneNameLabel);
            AddRow(_boneDetail, "マスターIdx", out _masterIndexLabel);
            AddRow(_boneDetail, "ボーンIdx",   out _boneIndexLabel);
            AddRow(_boneDetail, "親ボーン",    out _parentBoneLabel);

            _boneDetail.Add(MakeSep());

            _boneDetail.Add(MakeSectionLabel("ワールド"));
            AddRow(_boneDetail, "位置", out _worldPosLabel);

            // ステータス
            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(Color.gray);
            _statusLabel.style.marginTop = 6;
            root.Add(_statusLabel);
        }

        private static void AddRow(VisualElement parent, string labelText, out Label valueLabel)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var key = new Label(labelText + ": ");
            key.style.width = 80;
            key.style.color = new StyleColor(Color.gray);
            var val = new Label();
            val.style.flexGrow = 1;
            row.Add(key);
            row.Add(val);
            parent.Add(row);
            valueLabel = val;
        }

        private static Label MakeSectionLabel(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement MakeSep()
        {
            var sep = new VisualElement();
            sep.style.height          = 1;
            sep.style.backgroundColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            sep.style.marginTop       = 4;
            sep.style.marginBottom    = 4;
            return sep;
        }

        // ================================================================
        // 表示更新
        // ================================================================

        private void RefreshAll()
        {
            if (_warningLabel == null) return;

            if (_toolCtx == null || Model == null)
            {
                SetWarning("ToolContext が未設定です。PolyLing ウィンドウから開いてください。");
                _boneDetail.style.display = DisplayStyle.None;
                return;
            }

            var bones = Model.Bones;
            if (bones == null || bones.Count == 0)
            {
                SetWarning("モデルにボーンがありません。");
                _boneDetail.style.display = DisplayStyle.None;
                return;
            }

            if (!Model.HasBoneSelection)
            {
                SetWarning("");
                _selectionCountLabel.text       = "未選択 — シーン上でクリックまたはメッシュリストで選択";
                _boneDetail.style.display       = DisplayStyle.None;
                return;
            }

            SetWarning("");
            _boneDetail.style.display = DisplayStyle.Flex;

            var selectedIndices = Model.SelectedBoneIndices;
            int count    = selectedIndices.Count;
            int firstIdx = selectedIndices[0];

            _selectionCountLabel.text = count == 1 ? "1 ボーン選択中" : $"{count} ボーン選択中";

            var ctx = Model.GetMeshContext(firstIdx);
            if (ctx == null) return;

            _boneNameLabel.text    = ctx.Name ?? "(no name)";
            _masterIndexLabel.text = firstIdx.ToString();

            int boneIdx = Model.TypedIndices?.MasterToBoneIndex(firstIdx) ?? -1;
            _boneIndexLabel.text = boneIdx >= 0 ? boneIdx.ToString() : "-";

            int parentIdx = ctx.ParentIndex;
            if (parentIdx >= 0 && parentIdx < Model.MeshContextCount)
            {
                var parentCtx = Model.GetMeshContext(parentIdx);
                _parentBoneLabel.text = $"{parentCtx?.Name ?? "-"} [{parentIdx}]";
            }
            else
            {
                _parentBoneLabel.text = "(なし)";
            }

            var worldMatrix = ctx.WorldMatrix;
            _worldPosLabel.text = FormatVec3(new Vector3(worldMatrix.m03, worldMatrix.m13, worldMatrix.m23));

            _statusLabel.text = $"Bones: {bones.Count}  Selected: {count}";
        }

        private void SetWarning(string text)
        {
            if (_warningLabel == null) return;
            _warningLabel.text          = text;
            _warningLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static string FormatVec3(Vector3 v) => $"({v.x:F4}, {v.y:F4}, {v.z:F4})";

        // ================================================================
        // ボタンアクション
        // ================================================================

        private void OnResetPose()
        {
            if (Model == null || !Model.HasBoneSelection) return;

            var selectedIndices = new List<int>(Model.SelectedBoneIndices);
            var beforeSnapshots = new Dictionary<int, BonePoseDataSnapshot>();
            var contexts        = new List<(int idx, MeshContext ctx)>();

            foreach (var idx in selectedIndices)
            {
                var ctx = Model.GetMeshContext(idx);
                if (ctx == null) continue;
                if (ctx.BonePoseData == null)
                {
                    ctx.BonePoseData = new BonePoseData();
                    ctx.BonePoseData.IsActive = true;
                }
                beforeSnapshots[idx] = ctx.BonePoseData.CreateSnapshot();
                contexts.Add((idx, ctx));
            }

            if (contexts.Count == 0) return;

            foreach (var (_, ctx) in contexts)
            {
                ctx.BonePoseData.ClearAllLayers();
                ctx.BonePoseData.SetDirty();
            }

            if (UndoController != null)
            {
                var record = new MultiBonePoseChangeRecord();
                foreach (var (idx, ctx) in contexts)
                {
                    record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                    {
                        MasterIndex = idx,
                        OldSnapshot = beforeSnapshots.TryGetValue(idx, out var b) ? b : (BonePoseDataSnapshot?)null,
                        NewSnapshot = ctx.BonePoseData.CreateSnapshot(),
                    });
                }
                UndoController.MeshListStack.Record(record, "ボーンポーズリセット");
                UndoController.FocusMeshList();
            }

            Model.OnListChanged?.Invoke();
            _toolCtx?.SyncMesh?.Invoke();
            _toolCtx?.Repaint?.Invoke();
            RefreshAll();
        }

        private void OnFocusBone()
        {
            if (Model == null || !Model.HasBoneSelection) return;

            int firstIdx = Model.SelectedBoneIndices[0];
            var ctx      = Model.GetMeshContext(firstIdx);
            if (ctx == null) return;

            var worldMatrix = ctx.WorldMatrix;
            _toolCtx?.FocusCameraOn?.Invoke(new Vector3(worldMatrix.m03, worldMatrix.m13, worldMatrix.m23));
        }
    }
}
