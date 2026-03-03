// SimpleBlendPanel.cs
// 簡易ブレンドパネル
// 選択メッシュ（複数可）をソースメッシュに向けてブレンド
// 決定時にバックアップメッシュを作成

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Commands;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Symmetry;
using Poly_Ling.Localization;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools.Panels
{
    /// <summary>
    /// 簡易ブレンド設定
    /// </summary>
    [Serializable]
    public class SimpleBlendSettings : IToolSettings
    {
        public int SourceIndex = -1;
        [Range(0f, 1f)]
        public float BlendWeight = 0f;
        public bool RecalculateNormals = true;
        public bool SelectedVerticesOnly = false;
        public bool MatchByVertexId = false;

        public IToolSettings Clone() => new SimpleBlendSettings
        {
            SourceIndex = this.SourceIndex,
            BlendWeight = this.BlendWeight,
            RecalculateNormals = this.RecalculateNormals,
            SelectedVerticesOnly = this.SelectedVerticesOnly,
            MatchByVertexId = this.MatchByVertexId
        };

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not SimpleBlendSettings o) return true;
            return SourceIndex != o.SourceIndex ||
                   !Mathf.Approximately(BlendWeight, o.BlendWeight) ||
                   RecalculateNormals != o.RecalculateNormals ||
                   SelectedVerticesOnly != o.SelectedVerticesOnly ||
                   MatchByVertexId != o.MatchByVertexId;
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not SimpleBlendSettings o) return;
            SourceIndex = o.SourceIndex;
            BlendWeight = o.BlendWeight;
            RecalculateNormals = o.RecalculateNormals;
            SelectedVerticesOnly = o.SelectedVerticesOnly;
            MatchByVertexId = o.MatchByVertexId;
        }
    }

    /// <summary>
    /// 簡易ブレンドパネル
    /// </summary>
    public class SimpleBlendPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel
        // ================================================================

        public override string Name => "SimpleBlend";
        public override string Title => "Simple Blend";

        private SimpleBlendSettings _settings = new SimpleBlendSettings();
        public override IToolSettings Settings => _settings;

        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // ローカライズ
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Simple Blend", ["ja"] = "簡易ブレンド" },
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available", ["ja"] = "モデルがありません" },
            ["NoMeshSelected"] = new() { ["en"] = "No mesh selected", ["ja"] = "メッシュが未選択です" },
            ["TargetMeshes"] = new() { ["en"] = "Target Meshes", ["ja"] = "ターゲットメッシュ" },
            ["Source"] = new() { ["en"] = "Source Mesh", ["ja"] = "ソースメッシュ" },
            ["NoCandidate"] = new() { ["en"] = "No matching mesh found", ["ja"] = "一致するメッシュがありません" },
            ["BlendWeight"] = new() { ["en"] = "Blend Weight", ["ja"] = "ブレンドウェイト" },
            ["RecalcNormals"] = new() { ["en"] = "Recalculate normals", ["ja"] = "法線を再計算" },
            ["SelectedVerticesOnly"] = new() { ["en"] = "Selected vertices only", ["ja"] = "選択頂点のみ" },
            ["MatchByVertexId"] = new() { ["en"] = "Match by vertex ID", ["ja"] = "頂点IDで照合" },
            ["Apply"] = new() { ["en"] = "Apply (Create Backup)", ["ja"] = "決定（バックアップ作成）" },
            ["Cancel"] = new() { ["en"] = "Cancel", ["ja"] = "キャンセル" },
            ["Previewing"] = new() { ["en"] = "Previewing...", ["ja"] = "プレビュー中..." },
            ["ApplyDone"] = new() { ["en"] = "Blend applied. {0} backup(s) created.", ["ja"] = "ブレンド適用。バックアップ {0} 個作成。" },
            ["VertexMismatch"] = new() { ["en"] = "Vertex count mismatch: {0} ({1}) ≠ source ({2})", ["ja"] = "頂点数不一致: {0} ({1}) ≠ ソース ({2})" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // プレビュー状態
        // ================================================================

        // ターゲットメッシュごとの元頂点位置バックアップ
        private Dictionary<int, Vector3[]> _previewBackups = new Dictionary<int, Vector3[]>();
        // プレビュー前の可視状態
        private Dictionary<int, bool> _savedVisibility = new Dictionary<int, bool>();
        private bool _isPreviewActive = false;
        private bool _isDragging = false;
        private Vector2 _listScrollPos;

        // ソース候補キャッシュ
        private List<(int index, string name, int vertexCount)> _candidates = new List<(int, string, int)>();
        private int _selectedCandidateListIndex = -1;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<SimpleBlendPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(320, 350);
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
                EndPreview();
                EditorGUILayout.HelpBox(T("NoMeshSelected"), MessageType.Warning);
                return;
            }

            // ターゲット表示
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("TargetMeshes"), EditorStyles.boldLabel);

            var targetIndices = model.SelectedMeshIndices;

            using (new EditorGUI.DisabledScope(true))
            {
                foreach (int idx in targetIndices)
                {
                    var ctx = model.GetMeshContext(idx);
                    if (ctx?.MeshObject == null) continue;
                    EditorGUILayout.TextField($"{ctx.Name}  [V:{ctx.MeshObject.VertexCount}]");
                }
            }

            EditorGUILayout.Space(5);

            // 法線再計算オプション
            _settings.RecalculateNormals = EditorGUILayout.Toggle(T("RecalcNormals"), _settings.RecalculateNormals);
            _settings.SelectedVerticesOnly = EditorGUILayout.Toggle(T("SelectedVerticesOnly"), _settings.SelectedVerticesOnly);
            _settings.MatchByVertexId = EditorGUILayout.Toggle(T("MatchByVertexId"), _settings.MatchByVertexId);

            EditorGUILayout.Space(5);

            // ソースメッシュ選択
            EditorGUILayout.LabelField(T("Source"), EditorStyles.boldLabel);

            // 候補リスト構築
            BuildCandidates(model, targetIndices);

            if (_candidates.Count == 0)
            {
                EditorGUILayout.HelpBox(T("NoCandidate"), MessageType.Info);
                return;
            }

            // リストボックス
            float listHeight = Mathf.Min(_candidates.Count * 20f + 4f, 160f);
            _listScrollPos = EditorGUILayout.BeginScrollView(_listScrollPos, GUILayout.Height(listHeight));

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                bool wasSelected = (i == _selectedCandidateListIndex);
                var style = wasSelected ? EditorStyles.selectionRect : EditorStyles.label;

                var rect = EditorGUILayout.GetControlRect(GUILayout.Height(18));

                // 背景
                if (wasSelected)
                    EditorGUI.DrawRect(rect, new Color(0.2f, 0.4f, 0.8f, 0.4f));

                if (GUI.Button(rect, $"  {c.name}  [V:{c.vertexCount}]", EditorStyles.label))
                {
                    if (_selectedCandidateListIndex != i)
                    {
                        _selectedCandidateListIndex = i;
                        _settings.SourceIndex = c.index;
                        OnSourceChanged();
                    }
                }
            }

            EditorGUILayout.EndScrollView();

            // ソース未選択
            if (_settings.SourceIndex < 0 || _selectedCandidateListIndex < 0)
            {
                return;
            }

            // 頂点数不一致の警告
            var sourceCtx = model.GetMeshContext(_settings.SourceIndex);
            int sourceVertexCount = sourceCtx?.MeshObject?.VertexCount ?? 0;
            foreach (int idx in targetIndices)
            {
                var tctx = model.GetMeshContext(idx);
                if (tctx?.MeshObject == null) continue;
                if (tctx.MeshObject.VertexCount != sourceVertexCount)
                {
                    EditorGUILayout.HelpBox(
                        T("VertexMismatch", tctx.Name, tctx.MeshObject.VertexCount, sourceVertexCount),
                        MessageType.Info);
                }
            }

            EditorGUILayout.Space(10);

            // プレビュー中表示
            if (_isPreviewActive)
            {
                var prevColor = GUI.color;
                GUI.color = Color.yellow;
                EditorGUILayout.LabelField(T("Previewing"), EditorStyles.boldLabel);
                GUI.color = prevColor;
            }

            // ブレンドスライダー
            EditorGUILayout.LabelField(T("BlendWeight"), EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            float newWeight = EditorGUILayout.Slider(_settings.BlendWeight, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                    StartPreview(model, targetIndices);
                }

                _settings.BlendWeight = newWeight;
                ApplyPreview(model, targetIndices);
            }

            // マウスアップ
            if (_isDragging && Event.current.type == EventType.MouseUp)
            {
                _isDragging = false;
            }

            EditorGUILayout.Space(10);

            // ボタン
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!_isPreviewActive))
            {
                if (GUILayout.Button(T("Apply")))
                {
                    ApplyAndCreateBackups(model, targetIndices);
                }
            }

            if (GUILayout.Button(T("Cancel")))
            {
                EndPreview();
                _settings.BlendWeight = 0f;
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // 候補リスト構築
        // ================================================================

        private void BuildCandidates(ModelContext model, List<int> targetIndices)
        {
            _candidates.Clear();
            var targetSet = new HashSet<int>(targetIndices);

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                // ターゲット自身は除外
                if (targetSet.Contains(i)) continue;

                var ctx = model.GetMeshContext(i);
                if (ctx?.MeshObject == null) continue;
                if (ctx.MeshObject.VertexCount == 0) continue;

                // Bone/Morph/Helperなどは除外
                if (ctx.Type != MeshType.Mesh && ctx.Type != MeshType.BakedMirror && ctx.Type != MeshType.MirrorSide)
                    continue;

                _candidates.Add((i, ctx.Name, ctx.MeshObject.VertexCount));
            }

            // 選択インデックスの有効性チェック
            if (_selectedCandidateListIndex >= 0)
            {
                if (_selectedCandidateListIndex >= _candidates.Count ||
                    _candidates[_selectedCandidateListIndex].index != _settings.SourceIndex)
                {
                    // ソースインデックスで再検索
                    _selectedCandidateListIndex = _candidates.FindIndex(c => c.index == _settings.SourceIndex);
                    if (_selectedCandidateListIndex < 0)
                        _settings.SourceIndex = -1;
                }
            }
        }

        // ================================================================
        // ソース変更
        // ================================================================

        private void OnSourceChanged()
        {
            if (_isPreviewActive)
            {
                // プレビュー中にソースが変わったら再適用
                var model = Model;
                if (model != null)
                    ApplyPreview(model, model.SelectedMeshIndices);
            }
        }

        // ================================================================
        // プレビュー
        // ================================================================

        private void StartPreview(ModelContext model, List<int> targetIndices)
        {
            if (_isPreviewActive) return;

            _previewBackups.Clear();
            _savedVisibility.Clear();

            // ターゲットの頂点位置をバックアップ & 強制可視化
            foreach (int idx in targetIndices)
            {
                var ctx = model.GetMeshContext(idx);
                if (ctx?.MeshObject == null) continue;

                var mo = ctx.MeshObject;
                var backup = new Vector3[mo.VertexCount];
                for (int i = 0; i < mo.VertexCount; i++)
                    backup[i] = mo.Vertices[i].Position;
                _previewBackups[idx] = backup;

                _savedVisibility[idx] = ctx.IsVisible;
                ctx.IsVisible = true;
            }

            // ソースを不可視化
            if (_settings.SourceIndex >= 0)
            {
                var srcCtx = model.GetMeshContext(_settings.SourceIndex);
                if (srcCtx != null)
                {
                    _savedVisibility[_settings.SourceIndex] = srcCtx.IsVisible;
                    srcCtx.IsVisible = false;
                }
            }

            _isPreviewActive = true;
        }

        private void ApplyPreview(ModelContext model, List<int> targetIndices)
        {
            if (!_isPreviewActive) return;

            var srcCtx = model.GetMeshContext(_settings.SourceIndex);
            if (srcCtx?.MeshObject == null) return;
            var srcMo = srcCtx.MeshObject;

            float w = _settings.BlendWeight;
            var selectedVerts = _settings.SelectedVerticesOnly ? _context?.SelectedVertices : null;

            // ソース側: 頂点ID→index マップ（ID照合時のみ構築）
            Dictionary<int, int> srcIdMap = null;
            if (_settings.MatchByVertexId)
                srcIdMap = BuildVertexIdMap(srcMo);

            foreach (int idx in targetIndices)
            {
                if (!_previewBackups.TryGetValue(idx, out var backup)) continue;

                var ctx = model.GetMeshContext(idx);
                if (ctx?.MeshObject == null) continue;
                var mo = ctx.MeshObject;

                // 孤立頂点セット
                var nonIsolated = BuildNonIsolatedSet(mo);

                if (_settings.MatchByVertexId && srcIdMap != null)
                {
                    // ID照合モード
                    for (int i = 0; i < mo.VertexCount; i++)
                    {
                        if (!nonIsolated.Contains(i)) continue;
                        if (selectedVerts != null && !selectedVerts.Contains(i)) continue;

                        int vertId = mo.Vertices[i].Id;
                        if (srcIdMap.TryGetValue(vertId, out int srcIdx))
                            mo.Vertices[i].Position = Vector3.Lerp(backup[i], srcMo.Vertices[srcIdx].Position, w);
                        else
                            mo.Vertices[i].Position = backup[i];
                    }
                }
                else
                {
                    // インデックス照合モード（少ない方の頂点数）
                    int count = Mathf.Min(mo.VertexCount, srcMo.VertexCount);
                    for (int i = 0; i < mo.VertexCount; i++)
                    {
                        if (!nonIsolated.Contains(i)) continue;
                        if (selectedVerts != null && !selectedVerts.Contains(i)) continue;

                        if (i < count)
                            mo.Vertices[i].Position = Vector3.Lerp(backup[i], srcMo.Vertices[i].Position, w);
                        else
                            mo.Vertices[i].Position = backup[i];
                    }
                }

                _context?.SyncMeshContextPositionsOnly?.Invoke(ctx);
                SyncMirrorSide(model, ctx);
            }

            _context?.Repaint?.Invoke();
        }

        private void EndPreview()
        {
            if (!_isPreviewActive) return;

            var model = Model;
            if (model != null)
            {
                // 頂点位置を復元
                foreach (var (idx, backup) in _previewBackups)
                {
                    var ctx = model.GetMeshContext(idx);
                    if (ctx?.MeshObject == null) continue;
                    var mo = ctx.MeshObject;
                    int count = Mathf.Min(backup.Length, mo.VertexCount);
                    for (int i = 0; i < count; i++)
                        mo.Vertices[i].Position = backup[i];

                    _context?.SyncMeshContextPositionsOnly?.Invoke(ctx);
                    SyncMirrorSide(model, ctx);
                }

                // 可視状態を復元
                foreach (var (idx, visible) in _savedVisibility)
                {
                    var ctx = model.GetMeshContext(idx);
                    if (ctx != null)
                        ctx.IsVisible = visible;
                }
            }

            _previewBackups.Clear();
            _savedVisibility.Clear();
            _isPreviewActive = false;

            _context?.Repaint?.Invoke();
        }

        // ================================================================
        // 決定（バックアップ作成＋差し替え）
        // ================================================================

        private void ApplyAndCreateBackups(ModelContext model, List<int> targetIndices)
        {
            if (!_isPreviewActive) return;

            var srcCtx = model.GetMeshContext(_settings.SourceIndex);
            if (srcCtx?.MeshObject == null) return;
            var srcMo = srcCtx.MeshObject;

            float w = _settings.BlendWeight;
            int backupCount = 0;
            var selectedVerts = _settings.SelectedVerticesOnly ? _context?.SelectedVertices : null;

            // 既存メッシュ名を収集（同名回避用）
            var existingNames = new HashSet<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null) existingNames.Add(mc.Name);
            }

            // ソース側: 頂点ID→index マップ（ID照合時のみ構築）
            Dictionary<int, int> srcIdMap = null;
            if (_settings.MatchByVertexId)
                srcIdMap = BuildVertexIdMap(srcMo);

            // Undo用スナップショット
            var undo = _context?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();

            foreach (int idx in targetIndices)
            {
                if (!_previewBackups.TryGetValue(idx, out var backup)) continue;

                var ctx = model.GetMeshContext(idx);
                if (ctx?.MeshObject == null) continue;
                var mo = ctx.MeshObject;

                // バックアップメッシュ作成（元の位置を保持）
                var backupMo = mo.Clone();
                for (int i = 0; i < backup.Length && i < backupMo.VertexCount; i++)
                    backupMo.Vertices[i].Position = backup[i];

                string backupName = GenerateUniqueName(ctx.Name + "_backup", existingNames);
                backupMo.Name = backupName;
                backupMo.Type = ctx.MeshObject.Type;

                var backupCtx = new MeshContext
                {
                    MeshObject = backupMo,
                    Name = backupName,
                    Type = ctx.Type,
                    IsVisible = false
                };
                backupCtx.UnityMesh = backupMo.ToUnityMeshShared();
                if (backupCtx.UnityMesh != null)
                    backupCtx.UnityMesh.hideFlags = HideFlags.HideAndDontSave;

                model.Add(backupCtx);
                existingNames.Add(backupName);
                backupCount++;

                // ターゲットメッシュにブレンド結果を確定
                var nonIsolated = BuildNonIsolatedSet(mo);

                if (_settings.MatchByVertexId && srcIdMap != null)
                {
                    for (int i = 0; i < mo.VertexCount; i++)
                    {
                        if (!nonIsolated.Contains(i)) continue;
                        if (selectedVerts != null && !selectedVerts.Contains(i)) continue;

                        int vertId = mo.Vertices[i].Id;
                        if (srcIdMap.TryGetValue(vertId, out int srcIdx))
                            mo.Vertices[i].Position = Vector3.Lerp(backup[i], srcMo.Vertices[srcIdx].Position, w);
                    }
                }
                else
                {
                    int count = Mathf.Min(mo.VertexCount, srcMo.VertexCount);
                    for (int i = 0; i < mo.VertexCount; i++)
                    {
                        if (!nonIsolated.Contains(i)) continue;
                        if (selectedVerts != null && !selectedVerts.Contains(i)) continue;

                        if (i < count)
                            mo.Vertices[i].Position = Vector3.Lerp(backup[i], srcMo.Vertices[i].Position, w);
                    }
                }

                if (_settings.RecalculateNormals)
                    mo.RecalculateSmoothNormals();

                _context?.SyncMeshContextPositionsOnly?.Invoke(ctx);
                SyncMirrorSide(model, ctx);
            }

            // 可視状態を復元（ソースの不可視を元に戻す）
            foreach (var (idx, visible) in _savedVisibility)
            {
                // ターゲットはブレンド後なので可視のまま
                if (targetIndices.Contains(idx)) continue;
                var ctx = model.GetMeshContext(idx);
                if (ctx != null)
                    ctx.IsVisible = visible;
            }

            _previewBackups.Clear();
            _savedVisibility.Clear();
            _isPreviewActive = false;

            // Undo記録
            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                _context?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, "Simple Blend"));
            }

            // トポロジ変更通知（バックアップメッシュ追加のため）
            _context?.NotifyTopologyChanged?.Invoke();
            model.OnListChanged?.Invoke();
            _context?.Repaint?.Invoke();

            _settings.BlendWeight = 0f;
            _settings.SourceIndex = -1;
            _selectedCandidateListIndex = -1;

            Debug.Log(T("ApplyDone", backupCount));
            Repaint();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// MeshObjectの頂点ID→インデックスマップを構築
        /// </summary>
        private static Dictionary<int, int> BuildVertexIdMap(MeshObject mo)
        {
            var map = new Dictionary<int, int>();
            for (int i = 0; i < mo.VertexCount; i++)
            {
                int id = mo.Vertices[i].Id;
                if (!map.ContainsKey(id))
                    map[id] = i;
            }
            return map;
        }

        /// <summary>
        /// 面に参照されている（孤立でない）頂点インデックスのセットを構築
        /// </summary>
        private static HashSet<int> BuildNonIsolatedSet(MeshObject mo)
        {
            var set = new HashSet<int>();
            foreach (var face in mo.Faces)
            {
                foreach (int vi in face.VertexIndices)
                    set.Add(vi);
            }
            return set;
        }

        /// <summary>
        /// 既存名と重複しないユニーク名を生成
        /// </summary>
        private static string GenerateUniqueName(string baseName, HashSet<string> existingNames)
        {
            if (!existingNames.Contains(baseName))
                return baseName;

            for (int n = 1; n < 10000; n++)
            {
                string name = $"{baseName}_{n}";
                if (!existingNames.Contains(name))
                    return name;
            }
            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        // ================================================================
        // ミラー側同期
        // ================================================================

        /// <summary>
        /// ターゲットメッシュのMirrorSide（name+"+"）を検索し、頂点をミラー変換コピーする
        /// </summary>
        private void SyncMirrorSide(ModelContext model, MeshContext ctx)
        {
            if (model == null || ctx?.MeshObject == null) return;

            string mirrorName = ctx.Name + "+";
            var axis = ctx.GetMirrorSymmetryAxis();
            var mo = ctx.MeshObject;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.MirrorSide) continue;
                if (mc.Name != mirrorName) continue;
                if (mc.MeshObject == null || mc.MeshObject.VertexCount != mo.VertexCount) continue;

                var mirrorMo = mc.MeshObject;
                for (int v = 0; v < mo.VertexCount; v++)
                {
                    var pos = mo.Vertices[v].Position;
                    switch (axis)
                    {
                        case SymmetryAxis.X: mirrorMo.Vertices[v].Position = new Vector3(-pos.x, pos.y, pos.z); break;
                        case SymmetryAxis.Y: mirrorMo.Vertices[v].Position = new Vector3(pos.x, -pos.y, pos.z); break;
                        case SymmetryAxis.Z: mirrorMo.Vertices[v].Position = new Vector3(pos.x, pos.y, -pos.z); break;
                        default: mirrorMo.Vertices[v].Position = new Vector3(-pos.x, pos.y, pos.z); break;
                    }
                }
                _context?.SyncMeshContextPositionsOnly?.Invoke(mc);
                break;
            }
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        protected override void OnContextSet()
        {
            EndPreview();
            _settings.SourceIndex = -1;
            _settings.BlendWeight = 0f;
            _selectedCandidateListIndex = -1;
            _candidates.Clear();
        }

        private void OnDestroy()
        {
            EndPreview();
        }

        private void OnDisable()
        {
            EndPreview();
        }
    }
}
