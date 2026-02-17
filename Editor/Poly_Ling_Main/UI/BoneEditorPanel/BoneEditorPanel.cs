// Assets/Editor/Poly_Ling_Main/UI/BoneEditorPanel/BoneEditorPanel.cs
// ボーンエディタパネル
// シーン上でのボーン選択・移動と連携し、選択ボーンの情報を表示
// Model.OnListChangedを購読してTypedMeshListPanelと双方向に選択同期

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Model;

namespace Poly_Ling.UI
{
    public class BoneEditorPanel : EditorWindow
    {
        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/BoneEditorPanel/BoneEditorPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/BoneEditorPanel/BoneEditorPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/BoneEditorPanel/BoneEditorPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/BoneEditorPanel/BoneEditorPanel.uss";

        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        private ModelContext Model => _toolContext?.Model;

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel;
        private Label _selectionCountLabel;
        private VisualElement _boneDetail;
        private Label _boneNameLabel, _masterIndexLabel, _boneIndexLabel, _parentBoneLabel;
        private VisualElement _poseSection, _worldSection;
        private Label _restPosLabel, _restRotLabel, _restSclLabel;
        private Label _worldPosLabel;
        private Label _statusLabel;

        // ================================================================
        // Open
        // ================================================================

        public static BoneEditorPanel Open(ToolContext ctx)
        {
            var window = GetWindow<BoneEditorPanel>();
            window.titleContent = new GUIContent("ボーンエディタ");
            window.minSize = new Vector2(280, 300);
            window.SetContext(ctx);
            window.Show();
            return window;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            UnsubscribeFromModel();
            if (_toolContext?.UndoController != null)
                _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
        }

        // ================================================================
        // CreateGUI
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)
                          ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPathAssets);
            if (visualTree != null)
                visualTree.CloneTree(root);
            else
            {
                root.Add(new Label($"UXML not found: {UxmlPath}"));
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath)
                          ?? AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPathAssets);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            BindUI(root);
        }

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _selectionCountLabel = root.Q<Label>("selection-count-label");
            _boneDetail = root.Q<VisualElement>("bone-detail");
            _boneNameLabel = root.Q<Label>("bone-name-label");
            _masterIndexLabel = root.Q<Label>("master-index-label");
            _boneIndexLabel = root.Q<Label>("bone-index-label");
            _parentBoneLabel = root.Q<Label>("parent-bone-label");
            _poseSection = root.Q<VisualElement>("pose-section");
            _worldSection = root.Q<VisualElement>("world-section");
            _restPosLabel = root.Q<Label>("rest-pos-label");
            _restRotLabel = root.Q<Label>("rest-rot-label");
            _restSclLabel = root.Q<Label>("rest-scl-label");
            _worldPosLabel = root.Q<Label>("world-pos-label");
            _statusLabel = root.Q<Label>("status-label");

            // ボタン
            root.Q<Button>("btn-reset-pose")?.RegisterCallback<ClickEvent>(_ => OnResetPose());
            root.Q<Button>("btn-focus-bone")?.RegisterCallback<ClickEvent>(_ => OnFocusBone());

            RefreshAll();
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            UnsubscribeFromModel();
            if (_toolContext?.UndoController != null)
                _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;

            _toolContext = ctx;

            if (_toolContext?.Model != null)
            {
                SubscribeToModel();
                if (_toolContext.UndoController != null)
                    _toolContext.UndoController.OnUndoRedoPerformed += OnUndoRedoPerformed;
            }

            RefreshAll();
        }

        private void SubscribeToModel()
        {
            if (Model != null)
                Model.OnListChanged += OnModelListChanged;
        }

        private void UnsubscribeFromModel()
        {
            if (_toolContext?.Model != null)
                _toolContext.Model.OnListChanged -= OnModelListChanged;
        }

        private void OnModelListChanged()
        {
            RefreshAll();
        }

        private void OnUndoRedoPerformed()
        {
            RefreshAll();
        }

        // ================================================================
        // 表示更新
        // ================================================================

        private void RefreshAll()
        {
            if (_warningLabel == null) return; // UIバインド前

            // コンテキスト未設定
            if (_toolContext == null || Model == null)
            {
                SetWarning("ToolContextが未設定です。PolyLingウィンドウから開いてください。");
                SetDetailVisible(false);
                return;
            }

            // ボーンなし
            var bones = Model.Bones;
            if (bones == null || bones.Count == 0)
            {
                SetWarning("モデルにボーンがありません。");
                SetDetailVisible(false);
                return;
            }

            // 選択なし
            if (!Model.HasBoneSelection)
            {
                SetWarning("");
                if (_selectionCountLabel != null)
                    _selectionCountLabel.text = "未選択 — シーン上でクリックまたはメッシュリストで選択";
                SetDetailVisible(false);
                return;
            }

            // 選択あり
            SetWarning("");
            SetDetailVisible(true);

            var selectedIndices = Model.SelectedBoneIndices;
            int count = selectedIndices.Count;
            int firstIdx = selectedIndices[0];

            if (_selectionCountLabel != null)
                _selectionCountLabel.text = count == 1
                    ? "1 ボーン選択中"
                    : $"{count} ボーン選択中";

            // 先頭ボーンの情報表示
            var ctx = Model.GetMeshContext(firstIdx);
            if (ctx == null) return;

            if (_boneNameLabel != null) _boneNameLabel.text = ctx.Name ?? "(no name)";
            if (_masterIndexLabel != null) _masterIndexLabel.text = firstIdx.ToString();

            int boneIdx = Model.TypedIndices?.MasterToBoneIndex(firstIdx) ?? -1;
            if (_boneIndexLabel != null) _boneIndexLabel.text = boneIdx >= 0 ? boneIdx.ToString() : "-";

            // 親ボーン
            int parentIdx = ctx.ParentIndex;
            if (_parentBoneLabel != null)
            {
                if (parentIdx >= 0 && parentIdx < Model.MeshContextCount)
                {
                    var parentCtx = Model.GetMeshContext(parentIdx);
                    _parentBoneLabel.text = $"{parentCtx?.Name ?? "-"} [{parentIdx}]";
                }
                else
                {
                    _parentBoneLabel.text = "(なし)";
                }
            }

            // RestPose
            var pose = ctx.BonePoseData;
            if (pose != null)
            {
                if (_restPosLabel != null) _restPosLabel.text = FormatVec3(pose.RestPosition);
                if (_restRotLabel != null) _restRotLabel.text = FormatVec3(pose.RestRotation.eulerAngles);
                if (_restSclLabel != null) _restSclLabel.text = FormatVec3(pose.RestScale);
            }
            else if (ctx.BoneTransform != null)
            {
                if (_restPosLabel != null) _restPosLabel.text = FormatVec3(ctx.BoneTransform.Position) + " (BoneTransform)";
                if (_restRotLabel != null) _restRotLabel.text = FormatVec3(ctx.BoneTransform.Rotation) + " (BoneTransform)";
                if (_restSclLabel != null) _restSclLabel.text = FormatVec3(ctx.BoneTransform.Scale) + " (BoneTransform)";
            }
            else
            {
                if (_restPosLabel != null) _restPosLabel.text = "(未初期化)";
                if (_restRotLabel != null) _restRotLabel.text = "-";
                if (_restSclLabel != null) _restSclLabel.text = "-";
            }

            // WorldPosition
            var worldMatrix = ctx.WorldMatrix;
            Vector3 worldPos = new Vector3(worldMatrix.m03, worldMatrix.m13, worldMatrix.m23);
            if (_worldPosLabel != null) _worldPosLabel.text = FormatVec3(worldPos);

            // ステータス
            if (_statusLabel != null)
                _statusLabel.text = $"Bones: {bones.Count}  Selected: {count}";
        }

        private void SetWarning(string text)
        {
            if (_warningLabel == null) return;
            _warningLabel.text = text;
            _warningLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void SetDetailVisible(bool visible)
        {
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_boneDetail != null) _boneDetail.style.display = display;
            if (_poseSection != null) _poseSection.style.display = display;
            if (_worldSection != null) _worldSection.style.display = display;
        }

        private static string FormatVec3(Vector3 v)
        {
            return $"({v.x:F4}, {v.y:F4}, {v.z:F4})";
        }

        // ================================================================
        // ボタンアクション
        // ================================================================

        private void OnResetPose()
        {
            if (Model == null || !Model.HasBoneSelection) return;

            var selectedIndices = new List<int>(Model.SelectedBoneIndices);
            var beforeSnapshots = new Dictionary<int, BonePoseDataSnapshot>();
            var contexts = new List<(int idx, MeshContext ctx)>();

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

            // Undo記録
            var undoController = _toolContext?.UndoController;
            if (undoController != null)
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
                undoController.MeshListStack.Record(record, "ボーンポーズリセット");
                undoController.FocusMeshList();
            }

            Model.OnListChanged?.Invoke();
            _toolContext?.SyncMesh?.Invoke();
            _toolContext?.Repaint?.Invoke();
            RefreshAll();
        }

        private void OnFocusBone()
        {
            if (Model == null || !Model.HasBoneSelection) return;

            int firstIdx = Model.SelectedBoneIndices[0];
            var ctx = Model.GetMeshContext(firstIdx);
            if (ctx == null) return;

            var worldMatrix = ctx.WorldMatrix;
            Vector3 worldPos = new Vector3(worldMatrix.m03, worldMatrix.m13, worldMatrix.m23);

            // ToolContext経由でカメラ注目点を変更
            _toolContext?.FocusCameraOn?.Invoke(worldPos);
        }
    }
}
