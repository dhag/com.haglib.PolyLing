// PlayerBoneEditorSubPanel.cs
// ボーンエディタサブパネル（Player ビルド用）。
// BoneEditorPanelV2 の機能を UIToolkit サブパネルとして移植。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class PlayerBoneEditorSubPanel
    {
        // ================================================================
        // コールバック
        // ================================================================

        public Func<ModelContext>     GetModel;
        public Func<MeshUndoController> GetUndoController;
        public Action                 OnRepaint;
        public Action<Vector3>        OnFocusCamera;

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
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            root.Add(_warningLabel);

            _selectionCountLabel = new Label();
            _selectionCountLabel.style.color = new StyleColor(Color.white);
            _selectionCountLabel.style.marginBottom = 4;
            _selectionCountLabel.style.fontSize     = 10;
            root.Add(_selectionCountLabel);

            // ボタン行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 6;
            var btnReset = new Button(OnResetPose)  { text = "ポーズリセット" };
            btnReset.style.flexGrow   = 1;
            btnReset.style.marginRight = 4;
            btnReset.style.height     = 24;
            var btnFocus = new Button(OnFocusBone) { text = "フォーカス" };
            btnFocus.style.flexGrow = 1;
            btnFocus.style.height   = 24;
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
            _boneDetail.Add(MakeSecLabel("ワールド座標"));
            AddRow(_boneDetail, "位置", out _worldPosLabel);

            // ステータス
            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(Color.white);
            _statusLabel.style.marginTop = 6;
            root.Add(_statusLabel);
        }

        // ================================================================
        // Refresh（Viewer から呼ぶ）
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;

            var model = GetModel?.Invoke();
            if (model == null)
            {
                SetWarning("モデルがありません");
                _boneDetail.style.display = DisplayStyle.None;
                return;
            }

            var bones = model.Bones;
            if (bones == null || bones.Count == 0)
            {
                SetWarning("モデルにボーンがありません");
                _boneDetail.style.display = DisplayStyle.None;
                return;
            }

            if (!model.HasBoneSelection)
            {
                SetWarning("");
                _selectionCountLabel.text     = "未選択 — ビューポートでクリックして選択";
                _boneDetail.style.display     = DisplayStyle.None;
                _statusLabel.text             = $"Bones: {bones.Count}";
                return;
            }

            SetWarning("");
            _boneDetail.style.display = DisplayStyle.Flex;

            var indices = model.SelectedBoneIndices;
            int count   = indices.Count;
            int first   = indices[0];

            _selectionCountLabel.text = count == 1 ? "1 ボーン選択中" : $"{count} ボーン選択中";

            var ctx = model.GetMeshContext(first);
            if (ctx == null) return;

            _boneNameLabel.text    = ctx.Name ?? "(no name)";
            _masterIndexLabel.text = first.ToString();

            int boneIdx = model.TypedIndices?.MasterToBoneIndex(first) ?? -1;
            _boneIndexLabel.text = boneIdx >= 0 ? boneIdx.ToString() : "-";

            int parentIdx = ctx.ParentIndex;
            if (parentIdx >= 0 && parentIdx < model.MeshContextCount)
            {
                var parentCtx = model.GetMeshContext(parentIdx);
                _parentBoneLabel.text = $"{parentCtx?.Name ?? "-"} [{parentIdx}]";
            }
            else
            {
                _parentBoneLabel.text = "(なし)";
            }

            var wm = ctx.WorldMatrix;
            _worldPosLabel.text = $"({wm.m03:F4}, {wm.m13:F4}, {wm.m23:F4})";
            _statusLabel.text   = $"Bones: {bones.Count}  Selected: {count}";
        }

        // ================================================================
        // ボタンアクション
        // ================================================================

        private void OnResetPose()
        {
            var model = GetModel?.Invoke();
            if (model == null || !model.HasBoneSelection) return;

            var indices = new List<int>(model.SelectedBoneIndices);
            var beforeSnapshots = new Dictionary<int, BonePoseDataSnapshot>();
            var contexts = new List<(int idx, MeshContext ctx)>();

            foreach (var idx in indices)
            {
                var ctx = model.GetMeshContext(idx);
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

            var undo = GetUndoController?.Invoke();
            if (undo != null)
            {
                var record = new MultiBonePoseChangeRecord();
                foreach (var (idx, ctx) in contexts)
                {
                    record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                    {
                        MasterIndex = idx,
                        OldSnapshot = beforeSnapshots.TryGetValue(idx, out var b)
                            ? b : (BonePoseDataSnapshot?)null,
                        NewSnapshot = ctx.BonePoseData.CreateSnapshot(),
                    });
                }
                undo.MeshListStack.Record(record, "ボーンポーズリセット");
                undo.FocusMeshList();
            }

            model.OnListChanged?.Invoke();
            OnRepaint?.Invoke();
            Refresh();
        }

        private void OnFocusBone()
        {
            var model = GetModel?.Invoke();
            if (model == null || !model.HasBoneSelection) return;
            int first = model.SelectedBoneIndices[0];
            var ctx   = model.GetMeshContext(first);
            if (ctx == null) return;
            var wm = ctx.WorldMatrix;
            OnFocusCamera?.Invoke(new Vector3(wm.m03, wm.m13, wm.m23));
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private void SetWarning(string text)
        {
            if (_warningLabel == null) return;
            _warningLabel.text          = text;
            _warningLabel.style.display =
                string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static void AddRow(VisualElement parent, string labelText, out Label valueLabel)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var key = new Label(labelText + ": ");
            key.style.width    = 76;
            key.style.color    = new StyleColor(Color.white);
            key.style.fontSize = 10;
            var val = new Label();
            val.style.color = new StyleColor(Color.white);
            val.style.flexGrow = 1;
            val.style.fontSize = 10;
            row.Add(key); row.Add(val);
            parent.Add(row);
            valueLabel = val;
        }

        private static Label MakeSecLabel(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement MakeSep()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 3;
            v.style.marginBottom    = 3;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }
    }
}
