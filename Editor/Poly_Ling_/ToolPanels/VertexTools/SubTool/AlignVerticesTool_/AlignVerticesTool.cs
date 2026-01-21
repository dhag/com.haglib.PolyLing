// Tools/AlignVerticesTool.cs
// 頂点整列ツール - 選択頂点を指定軸上に整列
// 標準偏差が小さい軸を自動選択

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 頂点整列ツール
    /// </summary>
    public partial class AlignVerticesTool : IEditTool
    {
        public string Name => "Align";
        public string DisplayName => "Align";

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private AlignVerticesSettings _settings = new AlignVerticesSettings();
        public IToolSettings Settings => _settings;

        // コンテキスト
        private ToolContext _context;

        // 軸ごとの標準偏差（プレビュー表示用）
        private float _stdDevX;
        private float _stdDevY;
        private float _stdDevZ;
        private bool _statsCalculated = false;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            return false;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            // プレビュー表示なし
        }

        public void OnActivate(ToolContext ctx)
        {
            _context = ctx;
            _statsCalculated = false;
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
            _statsCalculated = false;
        }

        // ================================================================
        // 設定UI
        // ================================================================

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(5);

            // 選択頂点数表示
            int selectedCount = _context?.SelectedVertices?.Count ?? 0;
            EditorGUILayout.LabelField(T("SelectedVertices", selectedCount));

            if (selectedCount < 2)
            {
                EditorGUILayout.HelpBox(T("NeedMoreVertices"), MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(5);

            // 統計情報（標準偏差）
            if (_statsCalculated)
            {
                EditorGUILayout.LabelField(T("StdDeviation"), EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("  X: " + _stdDevX.ToString("F4"), EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  Y: " + _stdDevY.ToString("F4"), EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  Z: " + _stdDevZ.ToString("F4"), EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(5);

            // 軸選択チェックボックス
            EditorGUILayout.LabelField(T("AlignAxis"), EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            _settings.AlignX = EditorGUILayout.ToggleLeft("X", _settings.AlignX, GUILayout.Width(40));
            _settings.AlignY = EditorGUILayout.ToggleLeft("Y", _settings.AlignY, GUILayout.Width(40));
            _settings.AlignZ = EditorGUILayout.ToggleLeft("Z", _settings.AlignZ, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();

            // 自動選択ボタン
            if (GUILayout.Button(T("AutoSelect")))
            {
                CalculateAndAutoSelect(_context);
            }

            EditorGUILayout.Space(5);

            // 整列モード
            EditorGUILayout.LabelField(T("AlignMode"), EditorStyles.miniBoldLabel);
            _settings.Mode = (AlignMode)EditorGUILayout.EnumPopup(_settings.Mode);

            EditorGUILayout.Space(10);

            // 整列実行ボタン
            bool hasAxis = _settings.AlignX || _settings.AlignY || _settings.AlignZ;
            EditorGUI.BeginDisabledGroup(!hasAxis || selectedCount < 2);
            if (GUILayout.Button(T("Execute"), GUILayout.Height(30)))
            {
                ExecuteAlign();
            }
            EditorGUI.EndDisabledGroup();

            // プレビュー情報
            if (hasAxis && selectedCount >= 2)
            {
                EditorGUILayout.Space(5);
                Vector3 preview = CalculateAlignTarget();
                string axes = "";
                if (_settings.AlignX) axes += "X=" + preview.x.ToString("F3") + " ";
                if (_settings.AlignY) axes += "Y=" + preview.y.ToString("F3") + " ";
                if (_settings.AlignZ) axes += "Z=" + preview.z.ToString("F3");
                EditorGUILayout.LabelField(T("AlignTo", axes), EditorStyles.miniLabel);
            }
        }

        // ================================================================
        // 統計計算・自動選択
        // ================================================================

        private void CalculateAndAutoSelect(ToolContext ctx)
        {
            if (ctx == null || ctx.MeshObject == null || ctx.SelectedVertices == null || ctx.SelectedVertices.Count < 2)
            {
                _statsCalculated = false;
                return;
            }

            List<Vector3> positions = new List<Vector3>();
            foreach (int idx in ctx.SelectedVertices)
            {
                if (idx >= 0 && idx < ctx.MeshObject.VertexCount)
                {
                    positions.Add(ctx.MeshObject.Vertices[idx].Position);
                }
            }

            if (positions.Count < 2)
            {
                _statsCalculated = false;
                return;
            }

            // 平均を計算
            float avgX = positions.Average(p => p.x);
            float avgY = positions.Average(p => p.y);
            float avgZ = positions.Average(p => p.z);

            // 標準偏差を計算
            _stdDevX = Mathf.Sqrt(positions.Average(p => (p.x - avgX) * (p.x - avgX)));
            _stdDevY = Mathf.Sqrt(positions.Average(p => (p.y - avgY) * (p.y - avgY)));
            _stdDevZ = Mathf.Sqrt(positions.Average(p => (p.z - avgZ) * (p.z - avgZ)));

            _statsCalculated = true;

            // 標準偏差が最小の軸を選択
            float minStdDev = Mathf.Min(_stdDevX, _stdDevY, _stdDevZ);

            // しきい値（ほぼ同一平面上の場合のみ選択）
            float threshold = 0.01f;

            _settings.AlignX = (_stdDevX <= threshold) || (_stdDevX == minStdDev && minStdDev < threshold * 10);
            _settings.AlignY = (_stdDevY <= threshold) || (_stdDevY == minStdDev && minStdDev < threshold * 10);
            _settings.AlignZ = (_stdDevZ <= threshold) || (_stdDevZ == minStdDev && minStdDev < threshold * 10);

            // 最小の軸のみを確実に選択
            if (!_settings.AlignX && !_settings.AlignY && !_settings.AlignZ)
            {
                if (minStdDev == _stdDevX) _settings.AlignX = true;
                else if (minStdDev == _stdDevY) _settings.AlignY = true;
                else _settings.AlignZ = true;
            }
        }

        private Vector3 CalculateAlignTarget()
        {
            if (_context == null || _context.MeshObject == null || _context.SelectedVertices == null || _context.SelectedVertices.Count == 0)
                return Vector3.zero;

            List<Vector3> positions = new List<Vector3>();
            foreach (int idx in _context.SelectedVertices)
            {
                if (idx >= 0 && idx < _context.MeshObject.VertexCount)
                {
                    positions.Add(_context.MeshObject.Vertices[idx].Position);
                }
            }

            if (positions.Count == 0) return Vector3.zero;

            Vector3 target = Vector3.zero;

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
            if (_context == null || _context.MeshObject == null || _context.SelectedVertices == null || _context.SelectedVertices.Count < 2)
                return;

            if (!_settings.AlignX && !_settings.AlignY && !_settings.AlignZ)
                return;

            // Undo用スナップショット
            MeshObjectSnapshot before = null;
            if (_context.UndoController != null)
            {
                before = MeshObjectSnapshot.Capture(_context.UndoController.MeshUndoContext);
            }

            // 整列先を計算
            Vector3 target = CalculateAlignTarget();

            // 頂点を移動
            int movedCount = 0;
            foreach (int idx in _context.SelectedVertices)
            {
                if (idx < 0 || idx >= _context.MeshObject.VertexCount) continue;

                Vertex vertex = _context.MeshObject.Vertices[idx];
                Vector3 newPos = vertex.Position;

                if (_settings.AlignX) newPos.x = target.x;
                if (_settings.AlignY) newPos.y = target.y;
                if (_settings.AlignZ) newPos.z = target.z;

                if (newPos != vertex.Position)
                {
                    vertex.Position = newPos;
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
                        _context.UndoController, before, after, "Align Vertices"));
                }

                Debug.Log("[AlignVerticesTool] Aligned " + movedCount + " vertices");
            }

            // 統計を再計算
            CalculateAndAutoSelect(_context);

            _context.Repaint?.Invoke();
        }
    }

    /// <summary>
    /// 整列モード
    /// </summary>
    public enum AlignMode
    {
        Average,
        Min,
        Max
    }

    /// <summary>
    /// 頂点整列ツール設定
    /// </summary>
    public class AlignVerticesSettings : IToolSettings
    {
        public bool AlignX = false;
        public bool AlignY = false;
        public bool AlignZ = false;
        public AlignMode Mode = AlignMode.Average;

        public IToolSettings Clone()
        {
            return new AlignVerticesSettings
            {
                AlignX = this.AlignX,
                AlignY = this.AlignY,
                AlignZ = this.AlignZ,
                Mode = this.Mode
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is AlignVerticesSettings src)
            {
                AlignX = src.AlignX;
                AlignY = src.AlignY;
                AlignZ = src.AlignZ;
                Mode = src.Mode;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is AlignVerticesSettings src)
            {
                return AlignX != src.AlignX ||
                       AlignY != src.AlignY ||
                       AlignZ != src.AlignZ ||
                       Mode != src.Mode;
            }
            return true;
        }
    }
}
