// Tools/FlipFaceTool.cs
// 選択した面の法線を反転するツール

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using MeshFactory.Selection;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 選択した面の法線を反転するツール
    /// </summary>
    public class FlipFaceTool : IEditTool
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
            EditorGUILayout.LabelField("Flip Face Tool", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "選択した面の法線（表裏）を反転します。\n" +
                "Faceモードで面を選択してから実行してください。",
                MessageType.Info);

            EditorGUILayout.Space(5);

            // 反転ボタン
            if (GUILayout.Button("Flip Selected Faces", GUILayout.Height(30)))
            {
                FlipSelectedFaces();
            }

            // 全面反転ボタン
            EditorGUILayout.Space(3);
            if (GUILayout.Button("Flip All Faces"))
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
                _lastMessage = "Faceモードに切り替えてください (F key)";
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
            if (_context == null || _context.MeshData == null)
            {
                _lastMessage = "メッシュが選択されていません";
                return;
            }

            var faces = _context.SelectionState?.Faces;
            if (faces == null || faces.Count == 0)
            {
                _lastMessage = "面が選択されていません";
                return;
            }

            // Undo用スナップショット
            MeshDataSnapshot before = null;
            if (_context.UndoController != null)
            {
                before = MeshDataSnapshot.Capture(_context.UndoController.MeshContext);
            }

            // 選択された面を反転
            int flippedCount = 0;
            foreach (int faceIdx in faces)
            {
                if (faceIdx >= 0 && faceIdx < _context.MeshData.FaceCount)
                {
                    _context.MeshData.Faces[faceIdx].Flip();
                    flippedCount++;
                }
            }

            // 法線を再計算
            _context.MeshData.RecalculateNormals();

            // メッシュを更新
            _context.SyncMesh?.Invoke();

            // Undo記録
            if (_context.UndoController != null && before != null)
            {
                var after = MeshDataSnapshot.Capture(_context.UndoController.MeshContext);
                _context.UndoController.RecordTopologyChange(before, after, $"Flip {flippedCount} Faces");
            }

            _lastFlippedCount = flippedCount;
            _lastMessage = $"{flippedCount} 面を反転しました";

            _context.Repaint?.Invoke();
        }

        /// <summary>
        /// 全ての面を反転
        /// </summary>
        private void FlipAllFaces()
        {
            if (_context == null || _context.MeshData == null)
            {
                _lastMessage = "メッシュが選択されていません";
                return;
            }

            if (_context.MeshData.FaceCount == 0)
            {
                _lastMessage = "面がありません";
                return;
            }

            // Undo用スナップショット
            MeshDataSnapshot before = null;
            if (_context.UndoController != null)
            {
                before = MeshDataSnapshot.Capture(_context.UndoController.MeshContext);
            }

            // 全ての面を反転
            int flippedCount = 0;
            foreach (var face in _context.MeshData.Faces)
            {
                face.Flip();
                flippedCount++;
            }

            // 法線を再計算
            _context.MeshData.RecalculateNormals();

            // メッシュを更新
            _context.SyncMesh?.Invoke();

            // Undo記録
            if (_context.UndoController != null && before != null)
            {
                var after = MeshDataSnapshot.Capture(_context.UndoController.MeshContext);
                _context.UndoController.RecordTopologyChange(before, after, $"Flip All {flippedCount} Faces");
            }

            _lastFlippedCount = flippedCount;
            _lastMessage = $"全 {flippedCount} 面を反転しました";

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
                    _lastMessage = $"{faceCount} 面を選択中";
                }
                else
                {
                    _lastMessage = "面が選択されていません";
                }
            }
        }
    }
}