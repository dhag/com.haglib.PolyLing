// Assets/Editor/Poly_Ling/Tools/Panels/SimpleMorphPanel.cs
// 簡易モーフパネル
// 2つのリファレンスオブジェクト間でブレンドしてカレントオブジェクトに適用

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools.Panels
{
    /// <summary>
    /// 簡易モーフパネル設定
    /// </summary>
    [Serializable]
    public class SimpleMorphSettings : IToolSettings
    {
        /// <summary>リファレンスA のメッシュインデックス</summary>
        public int ReferenceAIndex = -1;

        /// <summary>リファレンスB のメッシュインデックス</summary>
        public int ReferenceBIndex = -1;

        /// <summary>ブレンド率 (0=A, 1=B)</summary>
        [Range(0f, 1f)]
        public float BlendRatio = 0.5f;

        /// <summary>頂点数が同じものだけフィルタリング</summary>
        public bool FilterSameVertexCount = true;

        /// <summary>適用後に法線を再計算</summary>
        public bool RecalculateNormals = true;

        public IToolSettings Clone()
        {
            return new SimpleMorphSettings
            {
                ReferenceAIndex = this.ReferenceAIndex,
                ReferenceBIndex = this.ReferenceBIndex,
                BlendRatio = this.BlendRatio,
                FilterSameVertexCount = this.FilterSameVertexCount,
                RecalculateNormals = this.RecalculateNormals
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not SimpleMorphSettings o)
                return true;

            return ReferenceAIndex != o.ReferenceAIndex ||
                   ReferenceBIndex != o.ReferenceBIndex ||
                   !Mathf.Approximately(BlendRatio, o.BlendRatio) ||
                   FilterSameVertexCount != o.FilterSameVertexCount ||
                   RecalculateNormals != o.RecalculateNormals;
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not SimpleMorphSettings o)
                return;

            ReferenceAIndex = o.ReferenceAIndex;
            ReferenceBIndex = o.ReferenceBIndex;
            BlendRatio = o.BlendRatio;
            FilterSameVertexCount = o.FilterSameVertexCount;
            RecalculateNormals = o.RecalculateNormals;
        }
    }

    /// <summary>
    /// 簡易モーフパネル
    /// </summary>
    public class SimpleMorphPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "SimpleMorph";
        public override string Title => "Simple Morph";

        private SimpleMorphSettings _settings = new SimpleMorphSettings();
        public override IToolSettings Settings => _settings;

        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Simple Morph", ["ja"] = "簡易モーフ", ["hi"] = "かんたんもーふ" },

            // メッセージ
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available", ["ja"] = "モデルがありません", ["hi"] = "もでるがないよ" },
            ["NoMeshSelected"] = new() { ["en"] = "No mesh selected as target", ["ja"] = "ターゲットメッシュが未選択", ["hi"] = "たーげっとがないよ" },
            ["SelectReferences"] = new() { ["en"] = "Select reference meshes A and B", ["ja"] = "リファレンスA, Bを選択してください", ["hi"] = "りふぁれんすをえらんでね" },

            // ラベル
            ["Target"] = new() { ["en"] = "Target", ["ja"] = "ターゲット", ["hi"] = "たーげっと" },
            ["ReferenceA"] = new() { ["en"] = "Reference A", ["ja"] = "リファレンスA", ["hi"] = "りふぁれんすA" },
            ["ReferenceB"] = new() { ["en"] = "Reference B", ["ja"] = "リファレンスB", ["hi"] = "りふぁれんすB" },
            ["BlendRatio"] = new() { ["en"] = "Blend Ratio (A→B)", ["ja"] = "ブレンド率 (A→B)", ["hi"] = "ぶれんど" },
            ["FilterSameVertex"] = new() { ["en"] = "Filter same vertex count", ["ja"] = "同じ頂点数のみ表示", ["hi"] = "おなじてんだけ" },
            ["RecalcNormals"] = new() { ["en"] = "Recalculate normals", ["ja"] = "法線を再計算", ["hi"] = "ほうせんさいけいさん" },

            // 情報
            ["Vertices"] = new() { ["en"] = "V", ["ja"] = "V", ["hi"] = "V" },
            ["None"] = new() { ["en"] = "(None)", ["ja"] = "(なし)", ["hi"] = "(なし)" },
            ["VertexCountMismatch"] = new() { ["en"] = "Vertex count mismatch. Will blend up to {0} vertices.", ["ja"] = "頂点数が異なります。{0}頂点までブレンドします。", ["hi"] = "{0}てんまでぶれんど" },

            // ボタン
            ["Apply"] = new() { ["en"] = "Apply", ["ja"] = "適用", ["hi"] = "てきよう" },
            ["Reset"] = new() { ["en"] = "Reset to A", ["ja"] = "Aにリセット", ["hi"] = "Aにもどす" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // UI状態
        // ================================================================

        private bool _isDragging = false;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<SimpleMorphPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(300, 200);
            panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            if (!DrawNoContextWarning())
                return;

            var model = Model;
            if (model == null)
            {
                EditorGUILayout.HelpBox(T("ModelNotAvailable"), MessageType.Warning);
                return;
            }

            if (!HasValidSelection)
            {
                EditorGUILayout.HelpBox(T("NoMeshSelected"), MessageType.Warning);
                return;
            }

            var targetMesh = CurrentMeshObject;
            int targetVertexCount = targetMesh?.VertexCount ?? 0;

            // ターゲット表示
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("Target"), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(CurrentMeshContent?.Name ?? "---");
            }
            EditorGUILayout.LabelField($"{T("Vertices")}: {targetVertexCount}", EditorStyles.miniLabel);

            EditorGUILayout.Space(10);

            // フィルタ設定
            EditorGUI.BeginChangeCheck();
            _settings.FilterSameVertexCount = EditorGUILayout.Toggle(T("FilterSameVertex"), _settings.FilterSameVertexCount);
            if (EditorGUI.EndChangeCheck())
            {
                // フィルタ変更時、選択をリセット
                _settings.ReferenceAIndex = -1;
                _settings.ReferenceBIndex = -1;
            }

            // 法線再計算オプション
            _settings.RecalculateNormals = EditorGUILayout.Toggle(T("RecalcNormals"), _settings.RecalculateNormals);

            EditorGUILayout.Space(5);

            // リファレンス選択用のメッシュリストを構築
            var meshOptions = BuildMeshOptions(model, targetVertexCount);

            // リファレンスA
            EditorGUILayout.LabelField(T("ReferenceA"), EditorStyles.boldLabel);
            int newRefA = DrawMeshDropdown(_settings.ReferenceAIndex, meshOptions);
            if (newRefA != _settings.ReferenceAIndex)
            {
                _settings.ReferenceAIndex = newRefA;
                ApplyBlend();
            }

            // リファレンスB
            EditorGUILayout.LabelField(T("ReferenceB"), EditorStyles.boldLabel);
            int newRefB = DrawMeshDropdown(_settings.ReferenceBIndex, meshOptions);
            if (newRefB != _settings.ReferenceBIndex)
            {
                _settings.ReferenceBIndex = newRefB;
                ApplyBlend();
            }

            // 選択チェック
            if (_settings.ReferenceAIndex < 0 || _settings.ReferenceBIndex < 0)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox(T("SelectReferences"), MessageType.Info);
                return;
            }

            // 頂点数チェック
            var refA = model.GetMeshContext(_settings.ReferenceAIndex)?.MeshObject;
            var refB = model.GetMeshContext(_settings.ReferenceBIndex)?.MeshObject;

            if (refA == null || refB == null)
            {
                return;
            }

            int minVertexCount = Mathf.Min(targetVertexCount, Mathf.Min(refA.VertexCount, refB.VertexCount));

            if (refA.VertexCount != refB.VertexCount || refA.VertexCount != targetVertexCount)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(T("VertexCountMismatch", minVertexCount), MessageType.Warning);
            }

            EditorGUILayout.Space(10);

            // ブレンドスライダー
            EditorGUILayout.LabelField(T("BlendRatio"), EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            float newBlend = EditorGUILayout.Slider(_settings.BlendRatio, 0f, 1f);

            if (EditorGUI.EndChangeCheck())
            {
                // ドラッグ開始検出
                if (!_isDragging)
                {
                    _isDragging = true;
                    // Undo開始
                    var undo = _context?.UndoController;
                    undo?.CaptureMeshObjectSnapshot();
                }

                _settings.BlendRatio = newBlend;
                ApplyBlendRealtime(refA, refB, targetMesh, minVertexCount);
            }

            // マウスアップ検出
            if (_isDragging && Event.current.type == EventType.MouseUp)
            {
                _isDragging = false;
                // ドラッグ終了時に法線再計算
                if (_settings.RecalculateNormals)
                {
                    targetMesh.RecalculateSmoothNormals();
                    _context?.SyncMesh?.Invoke();
                    _context?.Repaint?.Invoke();
                }
            }

            EditorGUILayout.Space(10);

            // ボタン
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(T("Reset")))
            {
                _settings.BlendRatio = 0f;
                ApplyBlend();
            }

            if (GUILayout.Button(T("Apply")))
            {
                ApplyBlend();
            }

            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // メッシュドロップダウン
        // ================================================================

        private List<(int index, string name, int vertexCount)> BuildMeshOptions(ModelContext model, int targetVertexCount)
        {
            var options = new List<(int, string, int)>();
            int currentIndex = model.SelectedMeshContextIndex;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                // カレント（ターゲット）は除外
                if (i == currentIndex)
                    continue;

                var ctx = model.GetMeshContext(i);
                if (ctx?.MeshObject == null)
                    continue;

                int vertCount = ctx.MeshObject.VertexCount;

                // フィルタリング
                if (_settings.FilterSameVertexCount && vertCount != targetVertexCount)
                    continue;

                options.Add((i, ctx.Name, vertCount));
            }

            return options;
        }

        private int DrawMeshDropdown(int currentIndex, List<(int index, string name, int vertexCount)> options)
        {
            // 表示用文字列配列を構築
            var displayOptions = new string[options.Count + 1];
            displayOptions[0] = T("None");

            int selectedPopupIndex = 0;

            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                displayOptions[i + 1] = $"{opt.name} ({T("Vertices")}:{opt.vertexCount})";

                if (opt.index == currentIndex)
                {
                    selectedPopupIndex = i + 1;
                }
            }

            int newPopupIndex = EditorGUILayout.Popup(selectedPopupIndex, displayOptions);

            if (newPopupIndex == 0)
            {
                return -1;
            }
            else
            {
                return options[newPopupIndex - 1].index;
            }
        }

        // ================================================================
        // ブレンド処理
        // ================================================================

        private void ApplyBlend()
        {
            if (_settings.ReferenceAIndex < 0 || _settings.ReferenceBIndex < 0)
                return;

            var model = Model;
            if (model == null) return;

            var refA = model.GetMeshContext(_settings.ReferenceAIndex)?.MeshObject;
            var refB = model.GetMeshContext(_settings.ReferenceBIndex)?.MeshObject;
            var target = CurrentMeshObject;

            if (refA == null || refB == null || target == null)
                return;

            int minVertexCount = Mathf.Min(target.VertexCount, Mathf.Min(refA.VertexCount, refB.VertexCount));

            // キャプチャ用のローカル変数
            var localRefA = refA;
            var localRefB = refB;
            float localRatio = _settings.BlendRatio;
            bool recalcNormals = _settings.RecalculateNormals;

            RecordTopologyChange("Morph Blend", mesh =>
            {
                BlendVertices(localRefA, localRefB, mesh, minVertexCount, localRatio);
                
                // 法線再計算
                if (recalcNormals)
                {
                    mesh.RecalculateSmoothNormals();
                }
            });
        }

        private void ApplyBlendRealtime(MeshObject refA, MeshObject refB, MeshObject target, int vertexCount)
        {
            BlendVertices(refA, refB, target, vertexCount, _settings.BlendRatio);

            // Unity Meshに反映（リアルタイム中は法線再計算しない）
            _context?.SyncMesh?.Invoke();
            _context?.Repaint?.Invoke();
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
        // コンテキスト変更時
        // ================================================================

        protected override void OnContextSet()
        {
            // リファレンス選択をリセット
            _settings.ReferenceAIndex = -1;
            _settings.ReferenceBIndex = -1;
            _settings.BlendRatio = 0.5f;
        }
    }
}
