// SimpleMorphPanel.cs
// 簡易モーフパネル (UIToolkit)
// 2つのリファレンスオブジェクト間でブレンドしてカレントオブジェクトに適用

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Commands;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Symmetry;
using Poly_Ling.Localization;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    /// <summary>
    /// 簡易モーフパネル（UIToolkit版）
    /// </summary>
    public class SimpleMorphPanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/SimpleMorphPanel/SimpleMorphPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/SimpleMorphPanel/SimpleMorphPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/SimpleMorphPanel/SimpleMorphPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/SimpleMorphPanel/SimpleMorphPanel.uss";

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Simple Morph", ["ja"] = "簡易モーフ" },
            ["NoContext"] = new() { ["en"] = "toolContext not set. Open from Poly_Ling window.", ["ja"] = "toolContext未設定。Poly_Lingウィンドウから開いてください。" },
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available", ["ja"] = "モデルがありません" },
            ["NoMeshSelected"] = new() { ["en"] = "No mesh selected as target", ["ja"] = "ターゲットメッシュが未選択" },
            ["SelectReferences"] = new() { ["en"] = "Select reference meshes A and B", ["ja"] = "リファレンスA, Bを選択してください" },
            ["Target"] = new() { ["en"] = "Target", ["ja"] = "ターゲット" },
            ["ReferenceA"] = new() { ["en"] = "Reference A", ["ja"] = "リファレンスA" },
            ["ReferenceB"] = new() { ["en"] = "Reference B", ["ja"] = "リファレンスB" },
            ["BlendRatio"] = new() { ["en"] = "Blend Ratio (A→B)", ["ja"] = "ブレンド率 (A→B)" },
            ["FilterSameVertex"] = new() { ["en"] = "Filter same vertex count", ["ja"] = "同じ頂点数のみ表示" },
            ["RecalcNormals"] = new() { ["en"] = "Recalculate normals", ["ja"] = "法線を再計算" },
            ["Vertices"] = new() { ["en"] = "V", ["ja"] = "V" },
            ["None"] = new() { ["en"] = "(None)", ["ja"] = "(なし)" },
            ["VertexCountMismatch"] = new() { ["en"] = "Vertex count mismatch. Will blend up to {0} vertices.", ["ja"] = "頂点数が異なります。{0}頂点までブレンドします。" },
            ["Apply"] = new() { ["en"] = "Apply", ["ja"] = "適用" },
            ["Reset"] = new() { ["en"] = "Reset to A", ["ja"] = "Aにリセット" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // Settings
        // ================================================================

        private int _referenceAIndex = -1;
        private int _referenceBIndex = -1;
        private float _blendRatio = 0.5f;
        private bool _filterSameVertexCount = true;
        private bool _recalculateNormals = true;

        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        private ModelContext Model => _toolContext?.Model;
        private MeshContext FirstSelectedMeshContext => _toolContext?.FirstSelectedMeshContext;
        private MeshObject FirstSelectedMeshObject => FirstSelectedMeshContext?.MeshObject;
        private bool HasValidSelection => _toolContext?.HasValidMeshSelection ?? false;

        // ================================================================
        // 状態
        // ================================================================

        private bool _isDragging = false;
        private List<(int index, string name, int vertexCount)> _meshOptions = new();

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel;
        private VisualElement _mainContent;
        private Label _targetHeader, _targetNameLabel, _targetVertexLabel;
        private Toggle _toggleFilterVertex, _toggleRecalcNormals;
        private Label _refAHeader, _refBHeader;
        private DropdownField _dropdownRefA, _dropdownRefB;
        private Label _selectRefsLabel, _vertexMismatchLabel;
        private VisualElement _blendSection;
        private Label _blendHeader;
        private Slider _sliderBlend;
        private Button _btnReset, _btnApply;

        // ================================================================
        // Open
        // ================================================================

        public static SimpleMorphPanel Open(ToolContext ctx)
        {
            var panel = GetWindow<SimpleMorphPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(300, 200);
            panel.SetContext(ctx);
            panel.Show();
            return panel;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();
        private void Cleanup() { }

        // ================================================================
        // IToolContextReceiver
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            _toolContext = ctx;
            _referenceAIndex = -1;
            _referenceBIndex = -1;
            _blendRatio = 0.5f;
            Refresh();
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
            Refresh();
        }

        // ================================================================
        // UI バインド
        // ================================================================

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _mainContent = root.Q<VisualElement>("main-content");
            _targetHeader = root.Q<Label>("target-header");
            _targetNameLabel = root.Q<Label>("target-name-label");
            _targetVertexLabel = root.Q<Label>("target-vertex-label");
            _toggleFilterVertex = root.Q<Toggle>("toggle-filter-vertex");
            _toggleRecalcNormals = root.Q<Toggle>("toggle-recalc-normals");
            _refAHeader = root.Q<Label>("ref-a-header");
            _refBHeader = root.Q<Label>("ref-b-header");
            _dropdownRefA = root.Q<DropdownField>("dropdown-ref-a");
            _dropdownRefB = root.Q<DropdownField>("dropdown-ref-b");
            _selectRefsLabel = root.Q<Label>("select-refs-label");
            _vertexMismatchLabel = root.Q<Label>("vertex-mismatch-label");
            _blendSection = root.Q<VisualElement>("blend-section");
            _blendHeader = root.Q<Label>("blend-header");
            _sliderBlend = root.Q<Slider>("slider-blend");
            _btnReset = root.Q<Button>("btn-reset");
            _btnApply = root.Q<Button>("btn-apply");

            // フィルタトグル
            _toggleFilterVertex.value = _filterSameVertexCount;
            _toggleFilterVertex.RegisterValueChangedCallback(e =>
            {
                _filterSameVertexCount = e.newValue;
                _referenceAIndex = -1;
                _referenceBIndex = -1;
                Refresh();
            });

            // 法線再計算トグル
            _toggleRecalcNormals.value = _recalculateNormals;
            _toggleRecalcNormals.RegisterValueChangedCallback(e =>
            {
                _recalculateNormals = e.newValue;
            });

            // ドロップダウン
            _dropdownRefA.RegisterValueChangedCallback(e => OnRefAChanged());
            _dropdownRefB.RegisterValueChangedCallback(e => OnRefBChanged());

            // ブレンドスライダー
            _sliderBlend.RegisterValueChangedCallback(e => OnBlendSliderChanged(e.newValue));

            // ドラッグ終了検出（PointerUpEvent）
            _sliderBlend.RegisterCallback<PointerUpEvent>(e => OnSliderDragEnd(), TrickleDown.TrickleDown);

            // ボタン
            _btnReset.clicked += OnReset;
            _btnApply.clicked += OnApply;
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return;

            if (_toolContext == null)
            {
                ShowWarning(T("NoContext"));
                return;
            }

            if (Model == null)
            {
                ShowWarning(T("ModelNotAvailable"));
                return;
            }

            if (!HasValidSelection)
            {
                ShowWarning(T("NoMeshSelected"));
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display = DisplayStyle.Flex;

            var targetMesh = FirstSelectedMeshObject;
            int targetVertexCount = targetMesh?.VertexCount ?? 0;

            // ローカライズ
            _targetHeader.text = T("Target");
            _toggleFilterVertex.label = T("FilterSameVertex");
            _toggleRecalcNormals.label = T("RecalcNormals");
            _refAHeader.text = T("ReferenceA");
            _refBHeader.text = T("ReferenceB");
            _blendHeader.text = T("BlendRatio");
            _btnReset.text = T("Reset");
            _btnApply.text = T("Apply");

            // ターゲット情報
            _targetNameLabel.text = FirstSelectedMeshContext?.Name ?? "---";
            _targetVertexLabel.text = $"{T("Vertices")}: {targetVertexCount}";

            // ドロップダウン再構築
            RebuildDropdowns(targetVertexCount);

            // ブレンドセクションの表示制御
            RefreshBlendSection(targetVertexCount);
        }

        private void ShowWarning(string message)
        {
            _warningLabel.text = message;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display = DisplayStyle.None;
        }

        // ================================================================
        // ドロップダウン構築
        // ================================================================

        private void RebuildDropdowns(int targetVertexCount)
        {
            _meshOptions = BuildMeshOptions(Model, targetVertexCount);

            var choices = new List<string> { T("None") };
            foreach (var opt in _meshOptions)
                choices.Add($"{opt.name} ({T("Vertices")}:{opt.vertexCount})");

            // RefA
            _dropdownRefA.choices = choices;
            _dropdownRefA.SetValueWithoutNotify(GetDropdownValue(_referenceAIndex, choices));

            // RefB
            _dropdownRefB.choices = choices;
            _dropdownRefB.SetValueWithoutNotify(GetDropdownValue(_referenceBIndex, choices));
        }

        private string GetDropdownValue(int meshIndex, List<string> choices)
        {
            if (meshIndex < 0) return choices[0]; // "(None)"
            for (int i = 0; i < _meshOptions.Count; i++)
            {
                if (_meshOptions[i].index == meshIndex)
                    return choices[i + 1];
            }
            return choices[0];
        }

        private int GetMeshIndexFromDropdown(DropdownField dropdown)
        {
            int popupIndex = dropdown.choices.IndexOf(dropdown.value);
            if (popupIndex <= 0) return -1;
            return _meshOptions[popupIndex - 1].index;
        }

        private List<(int index, string name, int vertexCount)> BuildMeshOptions(ModelContext model, int targetVertexCount)
        {
            var options = new List<(int, string, int)>();
            int currentIndex = model.FirstSelectedIndex;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (i == currentIndex) continue;

                var ctx = model.GetMeshContext(i);
                if (ctx?.MeshObject == null) continue;

                int vertCount = ctx.MeshObject.VertexCount;
                if (_filterSameVertexCount && vertCount != targetVertexCount) continue;

                options.Add((i, ctx.Name, vertCount));
            }
            return options;
        }

        // ================================================================
        // ドロップダウン変更
        // ================================================================

        private void OnRefAChanged()
        {
            _referenceAIndex = GetMeshIndexFromDropdown(_dropdownRefA);
            RefreshBlendSection(FirstSelectedMeshObject?.VertexCount ?? 0);
            if (_referenceAIndex >= 0 && _referenceBIndex >= 0)
                ApplyBlend();
        }

        private void OnRefBChanged()
        {
            _referenceBIndex = GetMeshIndexFromDropdown(_dropdownRefB);
            RefreshBlendSection(FirstSelectedMeshObject?.VertexCount ?? 0);
            if (_referenceAIndex >= 0 && _referenceBIndex >= 0)
                ApplyBlend();
        }

        // ================================================================
        // ブレンドセクション表示制御
        // ================================================================

        private void RefreshBlendSection(int targetVertexCount)
        {
            if (_referenceAIndex < 0 || _referenceBIndex < 0)
            {
                _selectRefsLabel.text = T("SelectReferences");
                _selectRefsLabel.style.display = DisplayStyle.Flex;
                _vertexMismatchLabel.style.display = DisplayStyle.None;
                _blendSection.style.display = DisplayStyle.None;
                return;
            }

            _selectRefsLabel.style.display = DisplayStyle.None;

            var refA = Model.GetMeshContext(_referenceAIndex)?.MeshObject;
            var refB = Model.GetMeshContext(_referenceBIndex)?.MeshObject;
            if (refA == null || refB == null)
            {
                _blendSection.style.display = DisplayStyle.None;
                return;
            }

            // 頂点数チェック
            int minVertexCount = Mathf.Min(targetVertexCount, Mathf.Min(refA.VertexCount, refB.VertexCount));
            if (refA.VertexCount != refB.VertexCount || refA.VertexCount != targetVertexCount)
            {
                _vertexMismatchLabel.text = T("VertexCountMismatch", minVertexCount);
                _vertexMismatchLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                _vertexMismatchLabel.style.display = DisplayStyle.None;
            }

            _blendSection.style.display = DisplayStyle.Flex;
            _sliderBlend.SetValueWithoutNotify(_blendRatio);
        }

        // ================================================================
        // スライダー操作
        // ================================================================

        private void OnBlendSliderChanged(float newValue)
        {
            if (_referenceAIndex < 0 || _referenceBIndex < 0) return;

            var refA = Model?.GetMeshContext(_referenceAIndex)?.MeshObject;
            var refB = Model?.GetMeshContext(_referenceBIndex)?.MeshObject;
            var target = FirstSelectedMeshObject;
            if (refA == null || refB == null || target == null) return;

            // ドラッグ開始
            if (!_isDragging)
            {
                _isDragging = true;
                _toolContext?.EnterTransformDragging?.Invoke();
                _toolContext?.UndoController?.CaptureMeshObjectSnapshot();
            }

            _blendRatio = newValue;
            int minVertexCount = Mathf.Min(target.VertexCount, Mathf.Min(refA.VertexCount, refB.VertexCount));
            ApplyBlendRealtime(refA, refB, target, minVertexCount);
        }

        private void OnSliderDragEnd()
        {
            if (!_isDragging) return;

            _isDragging = false;
            _toolContext?.ExitTransformDragging?.Invoke();

            // ドラッグ終了時に法線再計算
            if (_recalculateNormals)
            {
                FirstSelectedMeshObject?.RecalculateSmoothNormals();
                _toolContext?.SyncMesh?.Invoke();
                _toolContext?.Repaint?.Invoke();
            }
        }

        // ================================================================
        // ボタン操作
        // ================================================================

        private void OnReset()
        {
            _blendRatio = 0f;
            _sliderBlend?.SetValueWithoutNotify(0f);
            ApplyBlend();
        }

        private void OnApply()
        {
            ApplyBlend();
        }

        // ================================================================
        // ブレンド処理
        // ================================================================

        private void ApplyBlend()
        {
            if (_referenceAIndex < 0 || _referenceBIndex < 0) return;

            var model = Model;
            if (model == null) return;

            var refA = model.GetMeshContext(_referenceAIndex)?.MeshObject;
            var refB = model.GetMeshContext(_referenceBIndex)?.MeshObject;
            var target = FirstSelectedMeshObject;
            if (refA == null || refB == null || target == null) return;

            int minVertexCount = Mathf.Min(target.VertexCount, Mathf.Min(refA.VertexCount, refB.VertexCount));

            var localRefA = refA;
            var localRefB = refB;
            float localRatio = _blendRatio;
            bool recalcNormals = _recalculateNormals;

            RecordTopologyChange("Morph Blend", mesh =>
            {
                BlendVertices(localRefA, localRefB, mesh, minVertexCount, localRatio);
                if (recalcNormals)
                    mesh.RecalculateSmoothNormals();
            });

            SyncMirrorMesh();
        }

        private void ApplyBlendRealtime(MeshObject refA, MeshObject refB, MeshObject target, int vertexCount)
        {
            BlendVertices(refA, refB, target, vertexCount, _blendRatio);
            _toolContext?.SyncMesh?.Invoke();
            SyncMirrorMesh();
            _toolContext?.Repaint?.Invoke();
        }

        private void BlendVertices(MeshObject refA, MeshObject refB, MeshObject target, int vertexCount, float ratio)
        {
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 posA = refA.Vertices[i].Position;
                Vector3 posB = refB.Vertices[i].Position;
                target.Vertices[i].Position = Vector3.Lerp(posA, posB, ratio);
            }
        }

        // ================================================================
        // トポロジ変更記録（IToolPanelBaseから移植）
        // ================================================================

        private void RecordTopologyChange(string operationName, Action<MeshObject> action)
        {
            var meshObj = FirstSelectedMeshObject;
            if (meshObj == null) return;

            var undo = _toolContext?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();

            action(meshObj);

            _toolContext?.SyncMesh?.Invoke();

            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                _toolContext?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, operationName));
            }

            _toolContext?.Repaint?.Invoke();
        }

        // ================================================================
        // ミラーメッシュ同期
        // ================================================================

        private void SyncMirrorMesh()
        {
            var model = Model;
            var ctx = FirstSelectedMeshContext;
            if (model == null || ctx?.MeshObject == null) return;

            // MirrorPair方式（PMXインポート）
            var pair = model.GetMirrorPair(ctx);
            if (pair != null && pair.Real == ctx && pair.IsValid)
            {
                pair.SyncPositions();
                _toolContext?.SyncMeshContextPositionsOnly?.Invoke(pair.Mirror);
                return;
            }

            // BakedMirror方式（MQOインポート）
            if (!ctx.HasBakedMirrorChild) return;

            int targetIdx = model.MeshContextList.IndexOf(ctx);
            if (targetIdx < 0) return;

            var targetMo = ctx.MeshObject;
            var axis = ctx.GetMirrorSymmetryAxis();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (mc.BakedMirrorSourceIndex != targetIdx) continue;
                if (mc.MeshObject.VertexCount != targetMo.VertexCount) continue;

                var mirrorMo = mc.MeshObject;
                for (int v = 0; v < targetMo.VertexCount; v++)
                {
                    var pos = targetMo.Vertices[v].Position;
                    switch (axis)
                    {
                        case SymmetryAxis.X:
                            mirrorMo.Vertices[v].Position = new Vector3(-pos.x, pos.y, pos.z);
                            break;
                        case SymmetryAxis.Y:
                            mirrorMo.Vertices[v].Position = new Vector3(pos.x, -pos.y, pos.z);
                            break;
                        case SymmetryAxis.Z:
                            mirrorMo.Vertices[v].Position = new Vector3(pos.x, pos.y, -pos.z);
                            break;
                        default:
                            mirrorMo.Vertices[v].Position = new Vector3(-pos.x, pos.y, pos.z);
                            break;
                    }
                }
                _toolContext?.SyncMeshContextPositionsOnly?.Invoke(mc);
            }
        }
    }
}
