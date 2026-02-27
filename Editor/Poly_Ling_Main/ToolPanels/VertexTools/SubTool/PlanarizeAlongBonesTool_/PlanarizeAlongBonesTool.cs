// Tools/PlanarizeAlongBonesTool.cs
// ボーン間平面化ツール - 2つのボーンを指定し、A→B方向に直交する平面に頂点を揃える
// ブレンド率で元位置と平面化位置を補間可能

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Model;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// ボーン間平面化ツール
    /// </summary>
    public partial class PlanarizeAlongBonesTool : IEditTool
    {
        public string Name => "PlanarizeAlongBones";
        public string DisplayName => "Planarize Along Bones";

        // ================================================================
        // 設定
        // ================================================================

        private PlanarizeAlongBonesSettings _settings = new PlanarizeAlongBonesSettings();
        public IToolSettings Settings => _settings;

        // コンテキスト
        private ToolContext _context;

        // ボーンリストキャッシュ
        private string[] _boneNames;
        private int[] _boneMasterIndices;
        private int _cachedBoneCount = -1;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos) => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) => false;
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)
        {
            _context = ctx;
            RebuildBoneListIfNeeded();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _context = null;
        }

        public void Reset()
        {
            _settings.BoneIndexA = 0;
            _settings.BoneIndexB = 0;
            _settings.PlaneMode = PlanePlacementMode.MinMovement;
            _settings.Blend = 1f;
            _cachedBoneCount = -1;
        }

        // ================================================================
        // 設定UI
        // ================================================================

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(5);

            // 選択頂点数
            int selectedCount = _context?.SelectedVertices?.Count ?? 0;
            EditorGUILayout.LabelField(T("SelectedVertices", selectedCount));

            if (selectedCount < 1)
            {
                EditorGUILayout.HelpBox(T("NeedVertices"), MessageType.Warning);
                return;
            }

            // ボーンリスト構築
            RebuildBoneListIfNeeded();

            if (_boneNames == null || _boneNames.Length == 0)
            {
                EditorGUILayout.HelpBox(T("NoBones"), MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(5);

            // ボーンA選択
            EditorGUILayout.LabelField(T("BoneA"), EditorStyles.miniBoldLabel);
            _settings.BoneIndexA = EditorGUILayout.Popup(_settings.BoneIndexA, _boneNames);

            // ボーンB選択
            EditorGUILayout.LabelField(T("BoneB"), EditorStyles.miniBoldLabel);
            _settings.BoneIndexB = EditorGUILayout.Popup(_settings.BoneIndexB, _boneNames);

            // 同一ボーン警告
            if (_settings.BoneIndexA == _settings.BoneIndexB)
            {
                EditorGUILayout.HelpBox(T("SameBoneWarning"), MessageType.Warning);
            }

            EditorGUILayout.Space(5);

            // 平面位置モード
            EditorGUILayout.LabelField(T("PlaneMode"), EditorStyles.miniBoldLabel);
            _settings.PlaneMode = (PlanePlacementMode)EditorGUILayout.EnumPopup(_settings.PlaneMode);

            EditorGUILayout.Space(5);

            // ブレンド率
            EditorGUILayout.LabelField(T("Blend"), EditorStyles.miniBoldLabel);
            _settings.Blend = EditorGUILayout.Slider(_settings.Blend, 0f, 1f);

            EditorGUILayout.Space(5);

            // プレビュー情報
            if (_settings.BoneIndexA != _settings.BoneIndexB)
            {
                DrawPreviewInfo();
            }

            EditorGUILayout.Space(10);

            // 実行ボタン
            bool canExecute = selectedCount >= 1
                && _settings.BoneIndexA != _settings.BoneIndexB
                && _settings.Blend > 0f;

            EditorGUI.BeginDisabledGroup(!canExecute);
            if (GUILayout.Button(T("Execute"), GUILayout.Height(30)))
            {
                ExecutePlanarize();
            }
            EditorGUI.EndDisabledGroup();
        }

        // ================================================================
        // ボーンリスト構築
        // ================================================================

        private void RebuildBoneListIfNeeded()
        {
            var model = _context?.Model;
            if (model == null)
            {
                _boneNames = null;
                _boneMasterIndices = null;
                _cachedBoneCount = -1;
                return;
            }

            int currentBoneCount = model.BoneCount;
            if (currentBoneCount == _cachedBoneCount) return;

            var bones = model.Bones;
            _boneNames = new string[bones.Count];
            _boneMasterIndices = new int[bones.Count];

            for (int i = 0; i < bones.Count; i++)
            {
                _boneNames[i] = $"[{i}] {bones[i].Name}";
                _boneMasterIndices[i] = bones[i].MasterIndex;
            }

            _cachedBoneCount = currentBoneCount;

            // インデックス範囲補正
            if (_settings.BoneIndexA >= bones.Count) _settings.BoneIndexA = 0;
            if (_settings.BoneIndexB >= bones.Count) _settings.BoneIndexB = 0;
        }

        // ================================================================
        // ボーンワールド位置取得
        // ================================================================

        private Vector3 GetBoneWorldPosition(int boneListIndex)
        {
            if (_boneMasterIndices == null || boneListIndex < 0 || boneListIndex >= _boneMasterIndices.Length)
                return Vector3.zero;

            int masterIndex = _boneMasterIndices[boneListIndex];
            var meshList = _context?.Model?.MeshContextList;
            if (meshList == null || masterIndex < 0 || masterIndex >= meshList.Count)
                return Vector3.zero;

            var ctx = meshList[masterIndex];
            // WorldMatrixの平行移動成分 = ワールド位置
            return new Vector3(ctx.WorldMatrix.m03, ctx.WorldMatrix.m13, ctx.WorldMatrix.m23);
        }

        // ================================================================
        // プレビュー情報
        // ================================================================

        private void DrawPreviewInfo()
        {
            Vector3 posA = GetBoneWorldPosition(_settings.BoneIndexA);
            Vector3 posB = GetBoneWorldPosition(_settings.BoneIndexB);
            float dist = (posB - posA).magnitude;

            EditorGUILayout.LabelField(T("PreviewInfo"), EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"  A: ({posA.x:F3}, {posA.y:F3}, {posA.z:F3})", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"  B: ({posB.x:F3}, {posB.y:F3}, {posB.z:F3})", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"  {T("Distance")}: {dist:F4}", EditorStyles.miniLabel);
        }

        // ================================================================
        // 平面化実行
        // ================================================================

        private void ExecutePlanarize()
        {
            if (_context == null || _context.FirstSelectedMeshObject == null
                || _context.SelectedVertices == null || _context.SelectedVertices.Count < 1)
                return;

            if (_settings.BoneIndexA == _settings.BoneIndexB) return;
            if (_settings.Blend <= 0f) return;

            Vector3 posA = GetBoneWorldPosition(_settings.BoneIndexA);
            Vector3 posB = GetBoneWorldPosition(_settings.BoneIndexB);

            if ((posB - posA).magnitude < 1e-8f) return;

            // Undo用スナップショット
            MeshObjectSnapshot before = null;
            if (_context.UndoController != null)
            {
                before = MeshObjectSnapshot.Capture(_context.UndoController.MeshUndoContext);
            }

            MeshObject meshObj = _context.FirstSelectedMeshObject;
            float blend = _settings.Blend;

            // 選択頂点の位置を収集
            var selectedIndices = _context.SelectedVertices.ToList();
            var positions = new List<Vector3>(selectedIndices.Count);
            foreach (int idx in selectedIndices)
            {
                if (idx >= 0 && idx < meshObj.VertexCount)
                    positions.Add(meshObj.Vertices[idx].Position);
            }

            if (positions.Count == 0) return;

            // PlanarizeAlongSegment で平面化位置を計算
            // anchorIndex: MinMovement=-1, AnchorToA=0(Aの位置を通る平面)
            int anchorIndex = _settings.PlaneMode == PlanePlacementMode.AnchorToA ? 0 : -1;

            // 作業コピーで平面化
            var planarized = new List<Vector3>(positions);
            PlanarizeAlongSegment.Planarize(planarized, posA, posB, anchorIndex);

            // ブレンドして頂点に適用
            int movedCount = 0;
            for (int i = 0; i < selectedIndices.Count; i++)
            {
                int idx = selectedIndices[i];
                if (idx < 0 || idx >= meshObj.VertexCount) continue;

                Vector3 original = positions[i];
                Vector3 target = planarized[i];
                Vector3 blended = Vector3.Lerp(original, target, blend);

                if (blended != meshObj.Vertices[idx].Position)
                {
                    meshObj.Vertices[idx].Position = blended;
                    movedCount++;
                }
            }

            if (movedCount > 0)
            {
                // メッシュ更新
                _context.SyncMesh?.Invoke();

                // Undo記録
                if (_context.UndoController != null && before != null)
                {
                    MeshObjectSnapshot after = MeshObjectSnapshot.Capture(_context.UndoController.MeshUndoContext);
                    _context.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                        _context.UndoController, before, after, "Planarize Along Bones"));
                }
            }

            _context.Repaint?.Invoke();
        }
    }

    // ================================================================
    // 平面位置モード
    // ================================================================

    public enum PlanePlacementMode
    {
        /// <summary>頂点群の移動量が最小になる位置</summary>
        MinMovement,
        /// <summary>ボーンAの位置を通る平面</summary>
        AnchorToA,
    }

    // ================================================================
    // 設定クラス
    // ================================================================

    public class PlanarizeAlongBonesSettings : IToolSettings
    {
        public int BoneIndexA = 0;
        public int BoneIndexB = 0;
        public PlanePlacementMode PlaneMode = PlanePlacementMode.MinMovement;
        public float Blend = 1f;

        public IToolSettings Clone()
        {
            return new PlanarizeAlongBonesSettings
            {
                BoneIndexA = this.BoneIndexA,
                BoneIndexB = this.BoneIndexB,
                PlaneMode = this.PlaneMode,
                Blend = this.Blend,
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is PlanarizeAlongBonesSettings src)
            {
                BoneIndexA = src.BoneIndexA;
                BoneIndexB = src.BoneIndexB;
                PlaneMode = src.PlaneMode;
                Blend = src.Blend;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is PlanarizeAlongBonesSettings src)
            {
                return BoneIndexA != src.BoneIndexA
                    || BoneIndexB != src.BoneIndexB
                    || PlaneMode != src.PlaneMode
                    || !Mathf.Approximately(Blend, src.Blend);
            }
            return true;
        }
    }
}
