// PlanarizeAlongBonesTool.cs
// ボーン間平面化ツール - 2つのボーンを指定し、A→B方向に直交する平面に頂点を揃える
// ブレンド率で元位置と平面化位置を補間可能
// Runtime版: DrawSettingsUI() 除去済み

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Context;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// ボーン間平面化ツール
    /// </summary>
    public partial class PlanarizeAlongBonesTool : IEditTool
    {
        public string Name        => "PlanarizeAlongBones";
        public string DisplayName => "Planarize Along Bones";

        // ================================================================
        // 設定
        // ================================================================

        private PlanarizeAlongBonesSettings _settings = new PlanarizeAlongBonesSettings();
        public IToolSettings Settings => _settings;

        public int               BoneIndexA { get => _settings.BoneIndexA; set => _settings.BoneIndexA = value; }
        public int               BoneIndexB { get => _settings.BoneIndexB; set => _settings.BoneIndexB = value; }
        public PlanePlacementMode PlaneMode  { get => _settings.PlaneMode;  set => _settings.PlaneMode  = value; }
        public float             Blend       { get => _settings.Blend;      set => _settings.Blend      = value; }

        // ================================================================
        // ボーンリスト（SubPanel表示用）
        // ================================================================

        public string[] BoneNames        { get; private set; }
        public int[]    BoneMasterIndices { get; private set; }

        public int SelectedVertexCount =>
            _context?.SelectedVertices?.Count ?? 0;

        // ================================================================
        // コンテキスト
        // ================================================================

        private ToolContext _context;
        private int         _cachedBoneCount = -1;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)             => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)               => false;
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
            _settings.PlaneMode  = PlanePlacementMode.MinMovement;
            _settings.Blend      = 1f;
            _cachedBoneCount     = -1;
        }

        // ================================================================
        // 公開 API（SubPanel / Handler から呼び出し）
        // ================================================================

        public void TriggerPlanarize()        => ExecutePlanarize();

        public void RebuildBoneListIfNeeded()
        {
            var model = _context?.Model;
            if (model == null)
            {
                BoneNames         = null;
                BoneMasterIndices = null;
                _cachedBoneCount  = -1;
                return;
            }

            int currentCount = model.BoneCount;
            if (currentCount == _cachedBoneCount) return;

            var bones = model.Bones;
            BoneNames         = new string[bones.Count];
            BoneMasterIndices = new int[bones.Count];

            for (int i = 0; i < bones.Count; i++)
            {
                BoneNames[i]         = $"[{i}] {bones[i].Name}";
                BoneMasterIndices[i] = bones[i].MasterIndex;
            }

            _cachedBoneCount = currentCount;

            if (_settings.BoneIndexA >= bones.Count) _settings.BoneIndexA = 0;
            if (_settings.BoneIndexB >= bones.Count) _settings.BoneIndexB = 0;
        }

        public Vector3 GetBoneWorldPosition(int boneListIndex)
        {
            if (BoneMasterIndices == null
                || boneListIndex < 0
                || boneListIndex >= BoneMasterIndices.Length)
                return Vector3.zero;

            int masterIndex = BoneMasterIndices[boneListIndex];
            var meshList    = _context?.Model?.MeshContextList;
            if (meshList == null || masterIndex < 0 || masterIndex >= meshList.Count)
                return Vector3.zero;

            var mc = meshList[masterIndex];
            return new Vector3(mc.WorldMatrix.m03, mc.WorldMatrix.m13, mc.WorldMatrix.m23);
        }

        // ================================================================
        // 平面化実行
        // ================================================================

        private void ExecutePlanarize()
        {
            if (_context?.FirstSelectedMeshObject == null
                || _context.SelectedVertices == null
                || _context.SelectedVertices.Count < 1)
                return;

            if (_settings.BoneIndexA == _settings.BoneIndexB) return;
            if (_settings.Blend <= 0f) return;

            Vector3 posA = GetBoneWorldPosition(_settings.BoneIndexA);
            Vector3 posB = GetBoneWorldPosition(_settings.BoneIndexB);
            if ((posB - posA).magnitude < 1e-8f) return;

            MeshObjectSnapshot before = _context.UndoController != null
                ? MeshObjectSnapshot.Capture(_context.UndoController.MeshUndoContext)
                : default;

            MeshObject meshObj         = _context.FirstSelectedMeshObject;
            float      blend           = _settings.Blend;
            var        selectedIndices = _context.SelectedVertices.ToList();

            var positions = new List<Vector3>(selectedIndices.Count);
            foreach (int idx in selectedIndices)
            {
                if (idx >= 0 && idx < meshObj.VertexCount)
                    positions.Add(meshObj.Vertices[idx].Position);
            }

            if (positions.Count == 0) return;

            int anchorIndex = _settings.PlaneMode == PlanePlacementMode.AnchorToA ? 0 : -1;

            var planarized = new List<Vector3>(positions);
            PlanarizeAlongSegment.Planarize(planarized, posA, posB, anchorIndex);

            int movedCount = 0;
            for (int i = 0; i < selectedIndices.Count; i++)
            {
                int idx = selectedIndices[i];
                if (idx < 0 || idx >= meshObj.VertexCount) continue;

                Vector3 blended = Vector3.Lerp(positions[i], planarized[i], blend);
                if (blended != meshObj.Vertices[idx].Position)
                {
                    meshObj.Vertices[idx].Position = blended;
                    movedCount++;
                }
            }

            if (movedCount > 0)
            {
                _context.SyncMesh?.Invoke();

                if (_context.UndoController != null)
                {
                    var after = MeshObjectSnapshot.Capture(_context.UndoController.MeshUndoContext);
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
        public int               BoneIndexA = 0;
        public int               BoneIndexB = 0;
        public PlanePlacementMode PlaneMode  = PlanePlacementMode.MinMovement;
        public float             Blend       = 1f;

        public IToolSettings Clone() => new PlanarizeAlongBonesSettings
        {
            BoneIndexA = BoneIndexA, BoneIndexB = BoneIndexB,
            PlaneMode  = PlaneMode,  Blend       = Blend,
        };

        public void CopyFrom(IToolSettings other)
        {
            if (other is PlanarizeAlongBonesSettings s)
            { BoneIndexA = s.BoneIndexA; BoneIndexB = s.BoneIndexB; PlaneMode = s.PlaneMode; Blend = s.Blend; }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is PlanarizeAlongBonesSettings s)
                return BoneIndexA != s.BoneIndexA || BoneIndexB != s.BoneIndexB
                    || PlaneMode != s.PlaneMode || !Mathf.Approximately(Blend, s.Blend);
            return true;
        }
    }
}
