// AlignVerticesTool.cs
// 頂点整列ツール - 選択頂点を指定軸上に整列
// 標準偏差が小さい軸を自動選択
// Runtime版: DrawSettingsUI() 除去済み

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Data;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 頂点整列ツール
    /// </summary>
    public partial class AlignVerticesTool : IEditTool
    {
        public string Name        => "Align";
        public string DisplayName => "Align";

        // ================================================================
        // 設定
        // ================================================================

        private AlignVerticesSettings _settings = new AlignVerticesSettings();
        public IToolSettings Settings => _settings;

        public bool      AlignX          { get => _settings.AlignX; set => _settings.AlignX = value; }
        public bool      AlignY          { get => _settings.AlignY; set => _settings.AlignY = value; }
        public bool      AlignZ          { get => _settings.AlignZ; set => _settings.AlignZ = value; }
        public AlignMode Mode            { get => _settings.Mode;   set => _settings.Mode   = value; }

        // ================================================================
        // 統計（SubPanel表示用）
        // ================================================================

        public float StdDevX         { get; private set; }
        public float StdDevY         { get; private set; }
        public float StdDevZ         { get; private set; }
        public bool  StatsCalculated { get; private set; }

        public int SelectedVertexCount =>
            _context?.SelectedVertices?.Count ?? 0;

        // ================================================================
        // コンテキスト
        // ================================================================

        private ToolContext _context;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)  => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)    => false;
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)
        {
            _context         = ctx;
            StatsCalculated  = false;
            CalculateAndAutoSelect(ctx);
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _context = null;
        }

        public void Reset()
        {
            _settings.AlignX = false;
            _settings.AlignY = false;
            _settings.AlignZ = false;
            StatsCalculated  = false;
        }

        // ================================================================
        // 公開 API（SubPanel / Handler から呼び出し）
        // ================================================================

        public void TriggerAlign()       => ExecuteAlign();
        public void TriggerAutoSelect()  => CalculateAndAutoSelect(_context);

        public Vector3 GetAlignTarget()  => CalculateAlignTarget();

        // ================================================================
        // 統計計算・自動選択
        // ================================================================

        private void CalculateAndAutoSelect(ToolContext ctx)
        {
            if (ctx?.FirstDrawableMeshObject == null
                || ctx.SelectedVertices == null
                || ctx.SelectedVertices.Count < 2)
            {
                StatsCalculated = false;
                return;
            }

            var positions = new List<Vector3>();
            foreach (int idx in ctx.SelectedVertices)
            {
                if (idx >= 0 && idx < ctx.FirstDrawableMeshObject.VertexCount)
                    positions.Add(ctx.FirstDrawableMeshObject.Vertices[idx].Position);
            }

            if (positions.Count < 2) { StatsCalculated = false; return; }

            float avgX = positions.Average(p => p.x);
            float avgY = positions.Average(p => p.y);
            float avgZ = positions.Average(p => p.z);

            StdDevX = Mathf.Sqrt(positions.Average(p => (p.x - avgX) * (p.x - avgX)));
            StdDevY = Mathf.Sqrt(positions.Average(p => (p.y - avgY) * (p.y - avgY)));
            StdDevZ = Mathf.Sqrt(positions.Average(p => (p.z - avgZ) * (p.z - avgZ)));

            StatsCalculated = true;

            float minDev   = Mathf.Min(StdDevX, StdDevY, StdDevZ);
            float threshold = 0.01f;

            _settings.AlignX = (StdDevX <= threshold) || (StdDevX == minDev && minDev < threshold * 10);
            _settings.AlignY = (StdDevY <= threshold) || (StdDevY == minDev && minDev < threshold * 10);
            _settings.AlignZ = (StdDevZ <= threshold) || (StdDevZ == minDev && minDev < threshold * 10);

            if (!_settings.AlignX && !_settings.AlignY && !_settings.AlignZ)
            {
                if      (minDev == StdDevX) _settings.AlignX = true;
                else if (minDev == StdDevY) _settings.AlignY = true;
                else                        _settings.AlignZ = true;
            }
        }

        private Vector3 CalculateAlignTarget()
        {
            if (_context?.FirstDrawableMeshObject == null
                || _context.SelectedVertices == null
                || _context.SelectedVertices.Count == 0)
                return Vector3.zero;

            var positions = new List<Vector3>();
            foreach (int idx in _context.SelectedVertices)
            {
                if (idx >= 0 && idx < _context.FirstDrawableMeshObject.VertexCount)
                    positions.Add(_context.FirstDrawableMeshObject.Vertices[idx].Position);
            }

            if (positions.Count == 0) return Vector3.zero;

            var target = Vector3.zero;
            switch (_settings.Mode)
            {
                case AlignMode.Average:
                    target.x = positions.Average(p => p.x);
                    target.y = positions.Average(p => p.y);
                    target.z = positions.Average(p => p.z);
                    break;
                case AlignMode.Min:
                    target.x = positions.Min(p => p.x);
                    target.y = positions.Min(p => p.y);
                    target.z = positions.Min(p => p.z);
                    break;
                case AlignMode.Max:
                    target.x = positions.Max(p => p.x);
                    target.y = positions.Max(p => p.y);
                    target.z = positions.Max(p => p.z);
                    break;
            }
            return target;
        }

        // ================================================================
        // 整列実行
        // ================================================================

        private void ExecuteAlign()
        {
            if (_context?.FirstDrawableMeshObject == null
                || _context.SelectedVertices == null
                || _context.SelectedVertices.Count < 2)
                return;

            if (!_settings.AlignX && !_settings.AlignY && !_settings.AlignZ)
                return;

            MeshObjectSnapshot before = _context.UndoController != null && _context.FirstDrawableMeshContext != null
                ? MeshObjectSnapshot.Capture(_context.FirstDrawableMeshContext, _context.UndoController.MeshUndoContext)
                : default;

            Vector3 target    = CalculateAlignTarget();
            int     movedCount = 0;

            foreach (int idx in _context.SelectedVertices)
            {
                if (idx < 0 || idx >= _context.FirstDrawableMeshObject.VertexCount) continue;

                Vertex  v      = _context.FirstDrawableMeshObject.Vertices[idx];
                Vector3 newPos = v.Position;

                if (_settings.AlignX) newPos.x = target.x;
                if (_settings.AlignY) newPos.y = target.y;
                if (_settings.AlignZ) newPos.z = target.z;

                if (newPos != v.Position)
                {
                    v.Position = newPos;
                    movedCount++;
                }
            }

            if (movedCount > 0)
            {
                _context.FirstDrawableMeshObject.InvalidatePositionCache();
                _context.SyncMesh?.Invoke();

                if (_context.UndoController != null)
                {
                    var after = MeshObjectSnapshot.Capture(_context.FirstDrawableMeshContext, _context.UndoController.MeshUndoContext);
                    _context.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                        _context.UndoController, before, after, "Align Vertices"));
                }

                Debug.Log($"[AlignVerticesTool] Aligned {movedCount} vertices");
            }

            CalculateAndAutoSelect(_context);
            _context.Repaint?.Invoke();
        }
    }

    // ================================================================
    // 整列モード
    // ================================================================

    public enum AlignMode
    {
        Average,
        Min,
        Max
    }

    // ================================================================
    // 設定クラス
    // ================================================================

    public class AlignVerticesSettings : IToolSettings
    {
        public bool      AlignX = false;
        public bool      AlignY = false;
        public bool      AlignZ = false;
        public AlignMode Mode   = AlignMode.Average;

        public IToolSettings Clone() => new AlignVerticesSettings
        {
            AlignX = AlignX, AlignY = AlignY, AlignZ = AlignZ, Mode = Mode
        };

        public void CopyFrom(IToolSettings other)
        {
            if (other is AlignVerticesSettings s)
            { AlignX = s.AlignX; AlignY = s.AlignY; AlignZ = s.AlignZ; Mode = s.Mode; }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is AlignVerticesSettings s)
                return AlignX != s.AlignX || AlignY != s.AlignY || AlignZ != s.AlignZ || Mode != s.Mode;
            return true;
        }
    }
}
