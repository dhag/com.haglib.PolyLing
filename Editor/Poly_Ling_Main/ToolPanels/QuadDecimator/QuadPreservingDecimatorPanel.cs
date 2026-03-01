// QuadPreservingDecimatorPanel.cs
// Quadトポロジ優先減数化パネル（IToolPanelBase継承）

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools.Panels.QuadDecimator
{
    public class QuadPreservingDecimatorPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "QuadDecimator";
        public override string Title => "Quad Decimator";
        public override IToolSettings Settings => _settings;

        public override string GetLocalizedTitle() => L.Get("Window_QuadDecimator");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            // ヘッダー
            ["Header"] = new() { ["en"] = "Quad-Preserving Decimator", ["ja"] = "Quad保持 減数化" },
            ["Description"] = new() {
                ["en"] = "Reduces polygon count while preserving quad grid topology.",
                ["ja"] = "Quadグリッドのトポロジを保持しながらポリゴン数を削減します。"
            },

            // パラメータ
            ["TargetRatio"] = new() { ["en"] = "Target Ratio", ["ja"] = "目標比率" },
            ["MaxPasses"] = new() { ["en"] = "Max Passes", ["ja"] = "最大パス数" },
            ["NormalAngle"] = new() { ["en"] = "Normal Angle (°)", ["ja"] = "法線角度 (°)" },
            ["HardAngle"] = new() { ["en"] = "Hard Edge Angle (°)", ["ja"] = "ハードエッジ角度 (°)" },
            ["UvSeamThreshold"] = new() { ["en"] = "UV Seam Threshold", ["ja"] = "UVシーム閾値" },

            // ボタン
            ["Execute"] = new() { ["en"] = "Decimate", ["ja"] = "減数化実行" },

            // 結果
            ["ResultLabel"] = new() { ["en"] = "Result", ["ja"] = "結果" },
            ["OriginalFaces"] = new() { ["en"] = "Original faces: {0}", ["ja"] = "元の面数: {0}" },
            ["ResultFaces"] = new() { ["en"] = "Result faces: {0}", ["ja"] = "結果面数: {0}" },
            ["Reduction"] = new() { ["en"] = "Reduction: {0:F1}%", ["ja"] = "削減率: {0:F1}%" },
            ["Passes"] = new() { ["en"] = "Passes: {0}", ["ja"] = "パス数: {0}" },

            // メッセージ
            ["NoMesh"] = new() { ["en"] = "No mesh selected", ["ja"] = "メッシュが選択されていません" },
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available", ["ja"] = "モデルがありません" },
            ["NoQuads"] = new() { ["en"] = "Selected mesh has no quad faces.", ["ja"] = "選択メッシュにQuad面がありません。" },
            ["Processing"] = new() { ["en"] = "Processing...", ["ja"] = "処理中..." },

            // 情報
            ["MeshInfo"] = new() { ["en"] = "Mesh Info", ["ja"] = "メッシュ情報" },
            ["QuadCount"] = new() { ["en"] = "Quads: {0}", ["ja"] = "Quad数: {0}" },
            ["TriCount"] = new() { ["en"] = "Triangles: {0}", ["ja"] = "三角形数: {0}" },
            ["TotalFaces"] = new() { ["en"] = "Total faces: {0}", ["ja"] = "総面数: {0}" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // Settings
        // ================================================================

        [System.Serializable]
        private class DecimatorSettings : ToolSettingsBase
        {
            public float TargetRatio = 0.5f;
            public int MaxPasses = 5;
            public float NormalAngleDeg = 15f;
            public float HardAngleDeg = 25f;
            public float UvSeamThreshold = 0.01f;

            public override IToolSettings Clone()
            {
                return new DecimatorSettings
                {
                    TargetRatio = TargetRatio,
                    MaxPasses = MaxPasses,
                    NormalAngleDeg = NormalAngleDeg,
                    HardAngleDeg = HardAngleDeg,
                    UvSeamThreshold = UvSeamThreshold,
                };
            }

            public override bool IsDifferentFrom(IToolSettings other)
            {
                if (other is not DecimatorSettings o) return true;
                return TargetRatio != o.TargetRatio || MaxPasses != o.MaxPasses ||
                       NormalAngleDeg != o.NormalAngleDeg || HardAngleDeg != o.HardAngleDeg ||
                       UvSeamThreshold != o.UvSeamThreshold;
            }

            public override void CopyFrom(IToolSettings other)
            {
                if (other is not DecimatorSettings o) return;
                TargetRatio = o.TargetRatio;
                MaxPasses = o.MaxPasses;
                NormalAngleDeg = o.NormalAngleDeg;
                HardAngleDeg = o.HardAngleDeg;
                UvSeamThreshold = o.UvSeamThreshold;
            }
        }

        private readonly DecimatorSettings _settings = new();

        // ================================================================
        // 状態
        // ================================================================

        private Vector2 _scrollPos;
        private DecimatorResult _lastResult;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<QuadPreservingDecimatorPanel>();
            panel.titleContent = new GUIContent(L.Get("Window_QuadDecimator"));
            panel.minSize = new Vector2(320, 400);
            panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            if (!DrawNoContextWarning()) return;

            var model = Model;
            if (model == null)
            {
                EditorGUILayout.HelpBox(T("ModelNotAvailable"), MessageType.Warning);
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // ヘッダー
            EditorGUILayout.LabelField(T("Header"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(T("Description"), MessageType.Info);
            EditorGUILayout.Space(4);

            // メッシュ情報
            DrawMeshInfo();
            EditorGUILayout.Space(8);

            // パラメータ
            DrawParameters();
            EditorGUILayout.Space(8);

            // 実行ボタン
            DrawExecuteButton();
            EditorGUILayout.Space(8);

            // 結果表示
            DrawResult();

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // メッシュ情報
        // ================================================================

        private void DrawMeshInfo()
        {
            var meshObj = FirstSelectedMeshObject;
            if (meshObj == null)
            {
                EditorGUILayout.HelpBox(T("NoMesh"), MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(T("MeshInfo"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            int quads = 0, tris = 0, others = 0;
            foreach (var f in meshObj.Faces)
            {
                if (f.IsQuad) quads++;
                else if (f.IsTriangle) tris++;
                else others++;
            }

            EditorGUILayout.LabelField(T("TotalFaces", meshObj.Faces.Count));
            EditorGUILayout.LabelField(T("QuadCount", quads));
            EditorGUILayout.LabelField(T("TriCount", tris));

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // パラメータ描画
        // ================================================================

        private void DrawParameters()
        {
            EditorGUI.BeginChangeCheck();

            _settings.TargetRatio = EditorGUILayout.Slider(T("TargetRatio"), _settings.TargetRatio, 0.1f, 0.95f);
            _settings.MaxPasses = EditorGUILayout.IntSlider(T("MaxPasses"), _settings.MaxPasses, 1, 20);
            _settings.NormalAngleDeg = EditorGUILayout.Slider(T("NormalAngle"), _settings.NormalAngleDeg, 1f, 45f);
            _settings.HardAngleDeg = EditorGUILayout.Slider(T("HardAngle"), _settings.HardAngleDeg, 5f, 60f);
            _settings.UvSeamThreshold = EditorGUILayout.Slider(T("UvSeamThreshold"), _settings.UvSeamThreshold, 0f, 0.1f);

            if (EditorGUI.EndChangeCheck())
            {
                RecordSettingsChange("QuadDecimator Settings");
            }
        }

        // ================================================================
        // 実行ボタン
        // ================================================================

        private void DrawExecuteButton()
        {
            var meshObj = FirstSelectedMeshObject;
            bool hasQuads = false;
            if (meshObj != null)
            {
                foreach (var f in meshObj.Faces)
                {
                    if (f.IsQuad) { hasQuads = true; break; }
                }
            }

            EditorGUI.BeginDisabledGroup(meshObj == null || !hasQuads);

            if (GUILayout.Button(T("Execute"), GUILayout.Height(30)))
            {
                ExecuteDecimate();
            }

            EditorGUI.EndDisabledGroup();

            if (meshObj != null && !hasQuads)
            {
                EditorGUILayout.HelpBox(T("NoQuads"), MessageType.Info);
            }
        }

        // ================================================================
        // 実行
        // ================================================================

        private void ExecuteDecimate()
        {
            var sourceMeshObj = FirstSelectedMeshObject;
            if (sourceMeshObj == null) return;

            var sourceMeshContext = FirstSelectedMeshContext;

            var prms = new DecimatorParams
            {
                TargetRatio = _settings.TargetRatio,
                MaxPasses = _settings.MaxPasses,
                NormalAngleDeg = _settings.NormalAngleDeg,
                HardAngleDeg = _settings.HardAngleDeg,
                UvSeamThreshold = _settings.UvSeamThreshold,
            };

            _lastResult = QuadPreservingDecimator.Decimate(sourceMeshObj, prms, out MeshObject resultMesh);
            resultMesh.Name = sourceMeshObj.Name + "_decimated";

            // 新しいMeshContextとして追加
            var newMeshContext = new MeshContext
            {
                Name = resultMesh.Name,
                MeshObject = resultMesh,
                Materials = new List<UnityEngine.Material>(sourceMeshContext.Materials ?? new List<UnityEngine.Material>()),
            };

            newMeshContext.UnityMesh = resultMesh.ToUnityMesh();
            newMeshContext.UnityMesh.name = resultMesh.Name;
            newMeshContext.UnityMesh.hideFlags = HideFlags.HideAndDontSave;

            _context.AddMeshContext?.Invoke(newMeshContext);
            _context.Repaint?.Invoke();
            Repaint();
        }

        // ================================================================
        // 結果表示
        // ================================================================

        private void DrawResult()
        {
            if (_lastResult == null) return;

            EditorGUILayout.LabelField(T("ResultLabel"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField(T("OriginalFaces", _lastResult.OriginalFaceCount));
            EditorGUILayout.LabelField(T("ResultFaces", _lastResult.ResultFaceCount));

            float reduction = (_lastResult.OriginalFaceCount > 0)
                ? (1f - (float)_lastResult.ResultFaceCount / _lastResult.OriginalFaceCount) * 100f
                : 0;
            EditorGUILayout.LabelField(T("Reduction", reduction));
            EditorGUILayout.LabelField(T("Passes", _lastResult.PassCount));

            // パスログ
            if (_lastResult.PassLogs.Count > 0)
            {
                EditorGUILayout.Space(4);
                foreach (var log in _lastResult.PassLogs)
                {
                    EditorGUILayout.LabelField(log, EditorStyles.miniLabel);
                }
            }

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // コンテキスト更新
        // ================================================================

        protected override void OnContextSet()
        {
            _scrollPos = Vector2.zero;
            _lastResult = null;
        }
    }
}
