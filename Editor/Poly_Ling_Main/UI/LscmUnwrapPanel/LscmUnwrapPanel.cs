// Assets/Editor/Poly_Ling_Main/UI/LscmUnwrapPanel/LscmUnwrapPanel.cs
// アジの開き（LSCM UV展開）パネル
// 選択エッジをSeamとしてLSCMでUV展開する

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Selection;
using Poly_Ling.UI.Lscm;

namespace Poly_Ling.UI
{
    /// <summary>
    /// LscmUnwrapPanel設定
    /// </summary>
    [Serializable]
    public class LscmUnwrapPanelSettings : IToolSettings
    {
        public bool IncludeBoundaryAsSeam = true;
        public int MaxIterations = 3000;

        public IToolSettings Clone()
        {
            return new LscmUnwrapPanelSettings
            {
                IncludeBoundaryAsSeam = this.IncludeBoundaryAsSeam,
                MaxIterations = this.MaxIterations,
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not LscmUnwrapPanelSettings o) return true;
            return IncludeBoundaryAsSeam != o.IncludeBoundaryAsSeam ||
                   MaxIterations != o.MaxIterations;
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not LscmUnwrapPanelSettings o) return;
            IncludeBoundaryAsSeam = o.IncludeBoundaryAsSeam;
            MaxIterations = o.MaxIterations;
        }
    }

    /// <summary>
    /// アジの開き（LSCM UV展開）パネル
    /// 選択エッジをSeam（切れ目）として、共形写像（LSCM）でUV展開する。
    /// </summary>
    public class LscmUnwrapPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel
        // ================================================================

        public override string Name => "LscmUnwrapPanel";
        public override string Title => "LSCM Unwrap";

        private LscmUnwrapPanelSettings _settings = new LscmUnwrapPanelSettings();
        public override IToolSettings Settings => _settings;

        public override string GetLocalizedTitle() => "アジの開き（LSCM）";

        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/LscmUnwrapPanel/LscmUnwrapPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/LscmUnwrapPanel/LscmUnwrapPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/LscmUnwrapPanel/LscmUnwrapPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/LscmUnwrapPanel/LscmUnwrapPanel.uss";

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel, _targetInfo, _seamInfo, _statusLabel;
        private VisualElement _mainSection;
        private Toggle _toggleBoundarySeam;
        private IntegerField _fieldMaxIter;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<LscmUnwrapPanel>();
            panel.titleContent = new GUIContent("アジの開き（LSCM）");
            panel.minSize = new Vector2(300, 280);
            panel.SetContext(ctx);
            panel.Show();
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
            _targetInfo = root.Q<Label>("target-info");
            _seamInfo = root.Q<Label>("seam-info");
            _statusLabel = root.Q<Label>("status-label");
            _mainSection = root.Q<VisualElement>("main-section");

            _toggleBoundarySeam = root.Q<Toggle>("toggle-boundary-seam");
            _fieldMaxIter = root.Q<IntegerField>("field-max-iter");

            // 初期値
            if (_toggleBoundarySeam != null)
            {
                _toggleBoundarySeam.value = _settings.IncludeBoundaryAsSeam;
                _toggleBoundarySeam.RegisterValueChangedCallback(evt =>
                {
                    _settings.IncludeBoundaryAsSeam = evt.newValue;
                });
            }

            if (_fieldMaxIter != null)
            {
                _fieldMaxIter.value = _settings.MaxIterations;
                _fieldMaxIter.RegisterValueChangedCallback(evt =>
                {
                    _settings.MaxIterations = Mathf.Clamp(evt.newValue, 100, 50000);
                });
            }

            // ボタン
            root.Q<Button>("btn-unwrap")?.RegisterCallback<ClickEvent>(_ => ExecuteUnwrap());

            RefreshAll();
        }

        // ================================================================
        // コンテキスト
        // ================================================================

        protected override void OnContextSet()
        {
            RefreshAll();
        }

        protected override void OnUndoRedoPerformed()
        {
            base.OnUndoRedoPerformed();
            RefreshAll();
        }

        // ================================================================
        // 更新
        // ================================================================

        private void RefreshAll()
        {
            bool hasMesh = HasValidSelection;
            bool hasContext = _context != null;

            if (_warningLabel != null)
            {
                if (!hasContext)
                {
                    _warningLabel.text = "コンテキスト未設定";
                    _warningLabel.style.display = DisplayStyle.Flex;
                }
                else if (!hasMesh)
                {
                    _warningLabel.text = "メッシュが選択されていません";
                    _warningLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _warningLabel.style.display = DisplayStyle.None;
                }
            }

            bool showUI = hasContext && hasMesh;
            if (_mainSection != null)
                _mainSection.style.display = showUI ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateTargetInfo();
            UpdateSeamInfo();
        }

        private void UpdateTargetInfo()
        {
            if (_targetInfo == null) return;

            var mc = FirstSelectedMeshContext;
            var meshObj = mc?.MeshObject;
            if (meshObj == null)
            {
                _targetInfo.text = "メッシュ未選択";
                return;
            }

            int vertCount = meshObj.VertexCount;
            int faceCount = meshObj.FaceCount;
            int triCount = 0;
            foreach (var face in meshObj.Faces)
            {
                if (face != null && face.VertexCount >= 3)
                    triCount += face.VertexCount - 2;
            }

            _targetInfo.text = $"{mc.Name}  V:{vertCount} F:{faceCount} Tri:{triCount}";
        }

        private void UpdateSeamInfo()
        {
            if (_seamInfo == null) return;

            var mc = FirstSelectedMeshContext;
            if (mc == null)
            {
                _seamInfo.text = "Seam: -";
                return;
            }

            int seamCount = mc.SelectedEdges?.Count ?? 0;
            _seamInfo.text = $"Seam（選択エッジ）: {seamCount} 辺";
        }

        // ================================================================
        // LSCM展開実行
        // ================================================================

        private void ExecuteUnwrap()
        {
            if (_context == null || Model == null) return;

            var mc = FirstSelectedMeshContext;
            var meshObj = mc?.MeshObject;
            if (meshObj == null)
            {
                SetStatus("メッシュデータがありません");
                return;
            }

            if (meshObj.FaceCount == 0 || meshObj.VertexCount < 3)
            {
                SetStatus("メッシュが空または不十分です");
                return;
            }

            // Seam = 選択中のエッジ
            var seamEdges = mc.SelectedEdges ?? new HashSet<VertexPair>();

            bool includeBoundary = _settings.IncludeBoundaryAsSeam;
            int maxIter = Mathf.Clamp(_settings.MaxIterations, 100, 50000);

            var sw = Stopwatch.StartNew();

            // Undo対応：トポロジ変更として記録
            RecordTopologyChange("LSCM UV展開", (obj) =>
            {
                // Step1: SeamSplit
                var split = SeamSplitter.Build(obj, seamEdges, includeBoundary);

                if (split.VertexCount == 0 || split.TriangleCount == 0)
                {
                    SetStatus("分割結果が空です");
                    return;
                }

                // Step2: LSCM Solve
                var lscmResult = LscmSolver.Solve(split, maxIter);

                if (!lscmResult.Success)
                {
                    SetStatus($"LSCM失敗: {lscmResult.Error}");
                    return;
                }

                // Step3: UV書き戻し
                LscmUvWriter.Apply(obj, split, lscmResult);

                sw.Stop();
                SetStatus($"完了 ({sw.ElapsedMilliseconds}ms)  " +
                          $"UV頂点:{split.VertexCount} Tri:{split.TriangleCount} " +
                          $"島:{lscmResult.IslandCount}");
            });

            RefreshAll();
        }

        // ================================================================
        // ステータス
        // ================================================================

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
                _statusLabel.text = text;
        }

        // ================================================================
        // Update
        // ================================================================

        private int _lastMeshIndex = -1;
        private int _lastEdgeCount = -1;

        private void Update()
        {
            if (_context == null) return;

            int currentMeshIndex = Model?.FirstSelectedIndex ?? -1;
            int edgeCount = FirstSelectedMeshContext?.SelectedEdges?.Count ?? 0;

            if (currentMeshIndex != _lastMeshIndex || edgeCount != _lastEdgeCount)
            {
                _lastMeshIndex = currentMeshIndex;
                _lastEdgeCount = edgeCount;
                RefreshAll();
            }
        }
    }
}
