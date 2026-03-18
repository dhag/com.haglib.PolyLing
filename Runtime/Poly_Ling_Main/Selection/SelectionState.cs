// Assets/Editor/Poly_Ling/Selection/SelectionState.cs

using System;
using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Data;

namespace Poly_Ling.Selection
{
    /// <summary>
    /// 選択状態を保持するクラス
    /// </summary>
    public class SelectionState
    {
        /// <summary>
        /// 有効な選択モード（複数可）
        /// </summary>
        public MeshSelectMode Mode { get; set; } = MeshSelectMode.Vertex | MeshSelectMode.Edge | MeshSelectMode.Face | MeshSelectMode.Line;

        public HashSet<int> Vertices { get; } = new HashSet<int>();
        public HashSet<VertexPair> Edges { get; } = new HashSet<VertexPair>();
        public HashSet<int> Faces { get; } = new HashSet<int>();
        public HashSet<int> Lines { get; } = new HashSet<int>();

        public event Action OnSelectionChanged;

        /// <summary>
        /// 全ての選択をクリア（モードフラグに関係なく）
        /// </summary>
        public void ClearEnabledModes()
        {
            bool changed = Vertices.Count > 0 || Edges.Count > 0 ||
                          Faces.Count > 0 || Lines.Count > 0;

            Vertices.Clear();
            Edges.Clear();
            Faces.Clear();
            Lines.Clear();

            if (changed) OnSelectionChanged?.Invoke();
        }

        /// <summary>
        /// 後方互換：ClearCurrentModeはClearEnabledModesを呼ぶ
        /// </summary>
        public void ClearCurrentMode() => ClearEnabledModes();

        public void ClearAll()
        {
            bool changed = Vertices.Count > 0 || Edges.Count > 0 ||
                          Faces.Count > 0 || Lines.Count > 0;

            Vertices.Clear();
            Edges.Clear();
            Faces.Clear();
            Lines.Clear();

            if (changed) OnSelectionChanged?.Invoke();
        }

        /// <summary>
        /// 有効なモードのいずれかに選択があるか
        /// </summary>
        public bool HasSelection
        {
            get
            {
                if (Mode.Has(MeshSelectMode.Vertex) && Vertices.Count > 0) return true;
                if (Mode.Has(MeshSelectMode.Edge) && Edges.Count > 0) return true;
                if (Mode.Has(MeshSelectMode.Face) && Faces.Count > 0) return true;
                if (Mode.Has(MeshSelectMode.Line) && Lines.Count > 0) return true;
                return false;
            }
        }

        /// <summary>
        /// 全選択数（全モード合計）
        /// </summary>
        public int SelectionCount => Vertices.Count + Edges.Count + Faces.Count + Lines.Count;

        /// <summary>
        /// 全てのモードに何か選択があるか（Undo/Redoや移動操作用）
        /// </summary>
        public bool HasAnySelection => Vertices.Count > 0 || Edges.Count > 0 ||
                                       Faces.Count > 0 || Lines.Count > 0;

        // === 頂点選択 ===

        public bool SelectVertex(int index, bool additive = false)
        {
            if (!additive) Vertices.Clear();
            bool changed = Vertices.Add(index);
            if (changed || !additive) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool DeselectVertex(int index)
        {
            bool changed = Vertices.Remove(index);
            if (changed) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool ToggleVertex(int index)
        {
            bool changed;
            if (Vertices.Contains(index))
            {
                Vertices.Remove(index);
                changed = true;
            }
            else
            {
                Vertices.Add(index);
                changed = true;
            }
            if (changed) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool IsVertexSelected(int index) => Vertices.Contains(index);

        // === エッジ選択 ===

        public bool SelectEdge(VertexPair pair, bool additive = false)
        {
            if (!additive) Edges.Clear();
            bool changed = Edges.Add(pair);
            if (changed || !additive) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool SelectEdge(int v1, int v2, bool additive = false)
        {
            return SelectEdge(new VertexPair(v1, v2), additive);
        }

        public bool DeselectEdge(VertexPair pair)
        {
            bool changed = Edges.Remove(pair);
            if (changed) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool ToggleEdge(VertexPair pair)
        {
            bool changed;
            if (Edges.Contains(pair))
            {
                Edges.Remove(pair);
                changed = true;
            }
            else
            {
                Edges.Add(pair);
                changed = true;
            }
            if (changed) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool IsEdgeSelected(VertexPair pair) => Edges.Contains(pair);
        public bool IsEdgeSelected(int v1, int v2) => Edges.Contains(new VertexPair(v1, v2));

        // === 面選択 ===

        public bool SelectFace(int index, bool additive = false)
        {
            if (!additive) Faces.Clear();
            bool changed = Faces.Add(index);
            if (changed || !additive) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool DeselectFace(int index)
        {
            bool changed = Faces.Remove(index);
            if (changed) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool ToggleFace(int index)
        {
            bool changed;
            if (Faces.Contains(index))
            {
                Faces.Remove(index);
                changed = true;
            }
            else
            {
                Faces.Add(index);
                changed = true;
            }
            if (changed) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool IsFaceSelected(int index) => Faces.Contains(index);

        // === 補助線分選択 ===

        public bool SelectLine(int faceIndex, bool additive = false)
        {
            if (!additive) Lines.Clear();
            bool changed = Lines.Add(faceIndex);
            if (changed || !additive) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool DeselectLine(int faceIndex)
        {
            bool changed = Lines.Remove(faceIndex);
            if (changed) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool ToggleLine(int faceIndex)
        {
            bool changed;
            if (Lines.Contains(faceIndex))
            {
                Lines.Remove(faceIndex);
                changed = true;
            }
            else
            {
                Lines.Add(faceIndex);
                changed = true;
            }
            if (changed) OnSelectionChanged?.Invoke();
            return changed;
        }

        public bool IsLineSelected(int faceIndex) => Lines.Contains(faceIndex);

        // === 変換・取得 ===

        public HashSet<int> GetVerticesFromEdges()
        {
            var result = new HashSet<int>();
            foreach (var pair in Edges)
            {
                result.Add(pair.V1);
                result.Add(pair.V2);
            }
            return result;
        }

        public HashSet<int> GetVerticesFromFaces(MeshObject meshObject)
        {
            var result = new HashSet<int>();
            if (meshObject == null) return result;

            foreach (int faceIdx in Faces)
            {
                if (faceIdx < 0 || faceIdx >= meshObject.FaceCount) continue;
                foreach (int vIdx in meshObject.Faces[faceIdx].VertexIndices)
                {
                    result.Add(vIdx);
                }
            }
            return result;
        }

        public HashSet<int> GetVerticesFromLines(MeshObject meshObject)
        {
            var result = new HashSet<int>();
            if (meshObject == null) return result;

            foreach (int faceIdx in Lines)
            {
                if (faceIdx < 0 || faceIdx >= meshObject.FaceCount) continue;
                var face = meshObject.Faces[faceIdx];
                if (face.VertexCount != 2) continue;
                result.Add(face.VertexIndices[0]);
                result.Add(face.VertexIndices[1]);
            }
            return result;
        }

        public HashSet<int> GetAllAffectedVertices(MeshObject meshObject)
        {
            var result = new HashSet<int>(Vertices);

            foreach (var pair in Edges)
            {
                result.Add(pair.V1);
                result.Add(pair.V2);
            }

            if (meshObject != null)
            {
                foreach (int faceIdx in Faces)
                {
                    if (faceIdx < 0 || faceIdx >= meshObject.FaceCount) continue;
                    foreach (int vIdx in meshObject.Faces[faceIdx].VertexIndices)
                        result.Add(vIdx);
                }

                foreach (int faceIdx in Lines)
                {
                    if (faceIdx < 0 || faceIdx >= meshObject.FaceCount) continue;
                    var face = meshObject.Faces[faceIdx];
                    if (face.VertexCount == 2)
                    {
                        result.Add(face.VertexIndices[0]);
                        result.Add(face.VertexIndices[1]);
                    }
                }
            }

            return result;
        }

        // === 相互変換 ===

        public IEnumerable<int> GetOverlappingLines(TopologyCache topology)
        {
            if (topology == null) yield break;

            foreach (var edgePair in Edges)
            {
                foreach (var line in topology.GetLinesAt(edgePair))
                {
                    yield return line.FaceIndex;
                }
            }
        }

        public IEnumerable<VertexPair> GetOverlappingEdges(TopologyCache topology, MeshObject meshObject)
        {
            if (topology == null || meshObject == null) yield break;

            foreach (int lineIdx in Lines)
            {
                if (lineIdx < 0 || lineIdx >= meshObject.FaceCount) continue;
                var face = meshObject.Faces[lineIdx];
                if (face.VertexCount != 2) continue;

                var pair = new VertexPair(face.VertexIndices[0], face.VertexIndices[1]);
                if (topology.HasOverlappingEdge(pair))
                {
                    yield return pair;
                }
            }
        }

        public void ConvertEdgesToLines(TopologyCache topology)
        {
            if (topology == null) return;

            var lineIndices = GetOverlappingLines(topology).ToList();
            Lines.Clear();
            foreach (int idx in lineIndices)
            {
                Lines.Add(idx);
            }
            Edges.Clear();
            // Flagsモードでは特定モードに切り替えない
            // Mode = MeshSelectMode.Line;
            OnSelectionChanged?.Invoke();
        }

        public void ConvertLinesToEdges(TopologyCache topology, MeshObject meshObject)
        {
            if (topology == null || meshObject == null) return;

            var edgePairs = GetOverlappingEdges(topology, meshObject).ToList();
            Edges.Clear();
            foreach (var pair in edgePairs)
            {
                Edges.Add(pair);
            }
            Lines.Clear();
            // Flagsモードでは特定モードに切り替えない
            // Mode = MeshSelectMode.Edge;
            OnSelectionChanged?.Invoke();
        }

        // === スナップショット ===

        public SelectionSnapshot CreateSnapshot()
        {
            return new SelectionSnapshot
            {
                Mode = this.Mode,
                Vertices = new HashSet<int>(this.Vertices),
                Edges = new HashSet<VertexPair>(this.Edges),
                Faces = new HashSet<int>(this.Faces),
                Lines = new HashSet<int>(this.Lines)
            };
        }

        public void RestoreFromSnapshot(SelectionSnapshot snapshot)
        {
            if (snapshot == null) return;

            Mode = snapshot.Mode;
            Vertices.Clear();
            Edges.Clear();
            Faces.Clear();
            Lines.Clear();

            foreach (int v in snapshot.Vertices) Vertices.Add(v);
            foreach (var e in snapshot.Edges) Edges.Add(e);
            foreach (int f in snapshot.Faces) Faces.Add(f);
            foreach (int l in snapshot.Lines) Lines.Add(l);

            OnSelectionChanged?.Invoke();
        }
    }

    /// <summary>
    /// 選択状態のスナップショット
    /// </summary>
    public class SelectionSnapshot
    {
        public MeshSelectMode Mode;
        public HashSet<int> Vertices;
        public HashSet<VertexPair> Edges;
        public HashSet<int> Faces;
        public HashSet<int> Lines;

        public bool IsDifferentFrom(SelectionSnapshot other)
        {
            if (other == null) return true;
            if (Mode != other.Mode) return true;
            if (!Vertices.SetEquals(other.Vertices)) return true;
            if (!Edges.SetEquals(other.Edges)) return true;
            if (!Faces.SetEquals(other.Faces)) return true;
            if (!Lines.SetEquals(other.Lines)) return true;
            return false;
        }

        public SelectionSnapshot Clone()
        {
            return new SelectionSnapshot
            {
                Mode = this.Mode,
                Vertices = new HashSet<int>(this.Vertices),
                Edges = new HashSet<VertexPair>(this.Edges),
                Faces = new HashSet<int>(this.Faces),
                Lines = new HashSet<int>(this.Lines)
            };
        }
    }
}