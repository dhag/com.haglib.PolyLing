using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;

namespace Assets.Editor.VertexEditor
{

    // 編集対象を整理したクラス
    public class MeshEditContext
    {
        // メッシュデータ
        public List<Vector3> Vertices;
        public List<int> Triangles;
        public List<Vector2> UVs;
        public List<Vector3> Normals;

        // 選択状態
        public HashSet<int> SelectedVertices;
        public HashSet<int> SelectedFaces;

        // ヘルパー
        public void ApplyToMesh(Mesh mesh) { }
        public void LoadFromMesh(Mesh mesh) {}
    }


    // 全ての記録の基底
    public interface IUndoRecord
    {
        //string Description { get; }  // "Move Vertices", "Delete Face"等
        void Undo(MeshEditContext context);
        void Redo(MeshEditContext context);
    }

    // 頂点移動専用（軽量）
    public class VertexMoveRecord : IUndoRecord
    {
        public int[] Indices;
        public Vector3[] OldPositions;
        public Vector3[] NewPositions;

        public void Undo(MeshEditContext ctx)
        {
            for (int i = 0; i < Indices.Length; i++)
                ctx.Vertices[Indices[i]] = OldPositions[i];
        }

        public void Redo(MeshEditContext ctx)
        {
            for (int i = 0; i < Indices.Length; i++)
                ctx.Vertices[Indices[i]] = NewPositions[i];
        }
    }

    // トポロジー変更用（スナップショット）
    public class MeshSnapshotRecord : IUndoRecord
    {
        public MeshSnapshot Before;
        public MeshSnapshot After;

        public void Undo(MeshEditContext ctx) => Before.ApplyTo(ctx);
        public void Redo(MeshEditContext ctx) => After.ApplyTo(ctx);
    }

    // メッシュ状態のスナップショット
    public class MeshSnapshot
    {
        public Vector3[] Vertices;
        public int[] Triangles;
        public Vector2[] UVs;
        public Vector3[] Normals;

        public static MeshSnapshot Capture(MeshEditContext ctx)
        {
            return null; // スナップショットを作成して返す
        }
        public void ApplyTo(MeshEditContext ctx)
        {
            // スナップショットの内容をコンテキストに適用する
        }
    }


    public class UndoStack
    {
        private Stack<IUndoRecord> _undoStack = new();
        private Stack<IUndoRecord> _redoStack = new();
        private const int MaxSize = 50;

        public void Record(IUndoRecord record)
        {
            _undoStack.Push(record);
            _redoStack.Clear();

            // サイズ制限
            while (_undoStack.Count > MaxSize)
            {
                //RemoveOldest();
            }
        }

        public void Undo(MeshEditContext ctx)
        {
            if (_undoStack.Count == 0) return;
            var record = _undoStack.Pop();
            record.Undo(ctx);
            _redoStack.Push(record);
        }

        public void Redo(MeshEditContext ctx)
        {
            if (_redoStack.Count == 0) return;
            var record = _redoStack.Pop();
            record.Redo(ctx);
            _undoStack.Push(record);
        }
    }
}
