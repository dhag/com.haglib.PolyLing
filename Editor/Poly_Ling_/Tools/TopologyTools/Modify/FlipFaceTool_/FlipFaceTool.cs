// Tools/FlipFaceTool.cs
// 選択した面の法線を反転するツール

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Commands;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 選択した面の法線を反転するツール
    /// </summary>
    public partial class FlipFaceTool : IEditTool
    {
        public string Name => "Flip";
        public string DisplayName => "Flip";

        //public ToolCategory Category => ToolCategory.Utility;

        /// <summary>
        /// 設定なし（nullを返す）
        /// </summary>
        public IToolSettings Settings => null;

        // 最後に反転した面の数（情報表示用）
        private int _lastFlippedCount = 0;
        private string _lastMessage = "";

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            // このツールはマウス操作ではなくボタンで動作
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
            // 選択された面をハイライト表示は不要（既存の選択表示で十分）
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(5);

            // 反転ボタン
            if (GUILayout.Button(T("FlipSelected"), GUILayout.Height(30)))
            {
                FlipSelectedFaces();
            }

            // 全面反転ボタン
            EditorGUILayout.Space(3);
            if (GUILayout.Button(T("FlipAll")))
            {
                FlipAllFaces();
            }

            // 結果メッセージ
            if (!string.IsNullOrEmpty(_lastMessage))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(_lastMessage, MessageType.None);
            }
        }

        public void OnActivate(ToolContext ctx)
        {
            _context = ctx;
            _lastMessage = "";
            _lastFlippedCount = 0;

            // Faceモードに切り替えを推奨
            if (ctx.SelectionState != null && !ctx.SelectionState.Mode.Has(MeshSelectMode.Face))
            {
                _lastMessage = T("SwitchToFaceMode");
            }
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _context = null;
        }

        public void Reset()
        {
            _lastMessage = "";
            _lastFlippedCount = 0;
        }

        // ================================================================
        // 内部処理
        // ================================================================

        private ToolContext _context;

        /// <summary>
        /// 選択された面を反転
        /// </summary>
        private void FlipSelectedFaces()
        {
            if (_context == null || _context.MeshObject == null)
            {
                _lastMessage = T("NoMesh"); 
                return;
            }

            var faces = _context.SelectionState?.Faces;
            if (faces == null || faces.Count == 0)
            {
                _lastMessage = T("NoFaces");
                return;
            }

            // Undo用スナップショット
            MeshObjectSnapshot before = null;
            if (_context.UndoController != null)
            {
                before = MeshObjectSnapshot.Capture(_context.UndoController.MeshUndoContext);
            }

            // 選択された面を反転
            int flippedCount = 0;
            foreach (int faceIdx in faces)
            {
                if (faceIdx >= 0 && faceIdx < _context.MeshObject.FaceCount)
                {
                    _context.MeshObject.Faces[faceIdx].Flip();
                    flippedCount++;
                }
            }

            // 法線を再計算
            _context.MeshObject.RecalculateNormals();

            // メッシュを更新
            _context.SyncMesh?.Invoke();

            // Undo記録
            if (_context.UndoController != null && before != null)
            {
                var after = MeshObjectSnapshot.Capture(_context.UndoController.MeshUndoContext);
                _context.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    _context.UndoController, before, after, $"Flip {flippedCount} Faces"));
            }

            _lastFlippedCount = flippedCount;
            _lastMessage = T("FlippedCount", flippedCount);

            _context.Repaint?.Invoke();
        }

        /// <summary>
        /// 全ての面を反転
        /// </summary>
        private void FlipAllFaces()
        {
            if (_context == null || _context.MeshObject == null)
            {
                _lastMessage = "メッシュが選択されていません";
                return;
            }

            if (_context.MeshObject.FaceCount == 0)
            {
                _lastMessage = T("NoFacesExist");
                return;
            }

            // Undo用スナップショット
            MeshObjectSnapshot before = null;
            if (_context.UndoController != null)
            {
                before = MeshObjectSnapshot.Capture(_context.UndoController.MeshUndoContext);
            }

            // 全ての面を反転
            int flippedCount = 0;
            foreach (var face in _context.MeshObject.Faces)
            {
                face.Flip();
                flippedCount++;
            }

            // 法線を再計算
            _context.MeshObject.RecalculateNormals();

            // メッシュを更新
            _context.SyncMesh?.Invoke();

            // Undo記録
            if (_context.UndoController != null && before != null)
            {
                var after = MeshObjectSnapshot.Capture(_context.UndoController.MeshUndoContext);
                _context.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    _context.UndoController, before, after, $"Flip All {flippedCount} Faces"));
            }

            _lastFlippedCount = flippedCount;
            _lastMessage = T("FlippedAllCount", flippedCount);

            _context.Repaint?.Invoke();
        }

        /// <summary>
        /// 選択変更時のコールバック
        /// </summary>
        public void OnSelectionChanged(ToolContext ctx)
        {
            _context = ctx;

            if (ctx.SelectionState != null)
            {
                int faceCount = ctx.SelectionState.Faces.Count;
                if (faceCount > 0)
                {
                    _lastMessage = T("SelectedCount", faceCount); 
                }
                else
                {
                    _lastMessage = T("NoFaces");
                }
            }
        }
    }
}