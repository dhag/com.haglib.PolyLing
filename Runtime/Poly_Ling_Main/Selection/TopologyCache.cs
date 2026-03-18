// Assets/Editor/Poly_Ling/Selection/TopologyCache.cs

using System;
using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Data;

namespace Poly_Ling.Selection
{
    /// <summary>
    /// メッシュのトポロジ情報（キャッシュ）
    /// 選択操作やエッジ/ライン相互参照で使用
    /// </summary>
    public class TopologyCache
    {
        private MeshObject _meshObject;
        private bool _isDirty = true;

        // キャッシュ
        private Dictionary<VertexPair, List<FaceEdge>> _pairToEdges;
        private Dictionary<VertexPair, List<AuxLine>> _pairToLines;
        private Dictionary<int, List<VertexPair>> _vertexToPairs;
        private List<int> _realFaceIndices;
        private List<int> _auxLineIndices;

        public TopologyCache(MeshObject meshObject = null)
        {
            _meshObject = meshObject;
        }

        public void SetMeshObject(MeshObject meshObject)
        {
            if (_meshObject != meshObject)
            {
                _meshObject = meshObject;
                Invalidate();
            }
        }

        public void Invalidate()
        {
            _isDirty = true;
        }

        private void RebuildIfNeeded()
        {
            if (!_isDirty || _meshObject == null) return;

            _pairToEdges = new Dictionary<VertexPair, List<FaceEdge>>();
            _pairToLines = new Dictionary<VertexPair, List<AuxLine>>();
            _vertexToPairs = new Dictionary<int, List<VertexPair>>();
            _realFaceIndices = new List<int>();
            _auxLineIndices = new List<int>();

            for (int faceIdx = 0; faceIdx < _meshObject.FaceCount; faceIdx++)
            {
                var face = _meshObject.Faces[faceIdx];
                int vertCount = face.VertexCount;

                if (vertCount == 2)
                {
                    _auxLineIndices.Add(faceIdx);
                    
                    int v1 = face.VertexIndices[0];
                    int v2 = face.VertexIndices[1];
                    var pair = new VertexPair(v1, v2);
                    var auxLine = new AuxLine(pair, faceIdx);

                    if (!_pairToLines.TryGetValue(pair, out var lineList))
                    {
                        lineList = new List<AuxLine>();
                        _pairToLines[pair] = lineList;
                    }
                    lineList.Add(auxLine);

                    AddVertexPair(v1, pair);
                    AddVertexPair(v2, pair);
                }
                else if (vertCount >= 3)
                {
                    _realFaceIndices.Add(faceIdx);

                    for (int i = 0; i < vertCount; i++)
                    {
                        int v1 = face.VertexIndices[i];
                        int v2 = face.VertexIndices[(i + 1) % vertCount];
                        var pair = new VertexPair(v1, v2);
                        var faceEdge = new FaceEdge(pair, faceIdx, i);

                        if (!_pairToEdges.TryGetValue(pair, out var edgeList))
                        {
                            edgeList = new List<FaceEdge>();
                            _pairToEdges[pair] = edgeList;
                        }
                        edgeList.Add(faceEdge);

                        AddVertexPair(v1, pair);
                        AddVertexPair(v2, pair);
                    }
                }
            }

            _isDirty = false;
        }

        private void AddVertexPair(int vertexIndex, VertexPair pair)
        {
            if (!_vertexToPairs.TryGetValue(vertexIndex, out var pairList))
            {
                pairList = new List<VertexPair>();
                _vertexToPairs[vertexIndex] = pairList;
            }
            if (!pairList.Contains(pair))
            {
                pairList.Add(pair);
            }
        }

public IReadOnlyList<int> RealFaceIndices
{
    get 
    { 
        RebuildIfNeeded(); 
        return _realFaceIndices ?? (IReadOnlyList<int>)Array.Empty<int>(); 
    }
}

public IReadOnlyList<int> AuxLineIndices
{
    get 
    { 
        RebuildIfNeeded(); 
        return _auxLineIndices ?? (IReadOnlyList<int>)Array.Empty<int>(); 
    }
}

        public IReadOnlyList<FaceEdge> GetEdgesAt(VertexPair pair)
        {
            RebuildIfNeeded();
            return _pairToEdges.TryGetValue(pair, out var list) 
                ? list 
                : (IReadOnlyList<FaceEdge>)System.Array.Empty<FaceEdge>();
        }

        public IReadOnlyList<AuxLine> GetLinesAt(VertexPair pair)
        {
            RebuildIfNeeded();
            return _pairToLines.TryGetValue(pair, out var list) 
                ? list 
                : (IReadOnlyList<AuxLine>)System.Array.Empty<AuxLine>();
        }

        public IReadOnlyList<VertexPair> GetPairsContaining(int vertexIndex)
        {
            RebuildIfNeeded();
            return _vertexToPairs.TryGetValue(vertexIndex, out var list)
                ? list
                : (IReadOnlyList<VertexPair>)System.Array.Empty<VertexPair>();
        }

public IEnumerable<VertexPair> AllEdgePairs
{
    get 
    { 
        RebuildIfNeeded(); 
        return _pairToEdges?.Keys ?? Enumerable.Empty<VertexPair>(); 
    }
}

public IEnumerable<VertexPair> AllLinePairs
{
    get 
    { 
        RebuildIfNeeded(); 
        return _pairToLines?.Keys ?? Enumerable.Empty<VertexPair>(); 
    }
}

        public bool HasOverlappingLine(VertexPair edgePair)
        {
            RebuildIfNeeded();
            return _pairToLines.ContainsKey(edgePair);
        }

        public bool HasOverlappingEdge(VertexPair linePair)
        {
            RebuildIfNeeded();
            return _pairToEdges.ContainsKey(linePair);
        }

        public bool IsBoundaryEdge(VertexPair pair)
        {
            RebuildIfNeeded();
            return _pairToEdges.TryGetValue(pair, out var list) && list.Count == 1;
        }

        public IEnumerable<VertexPair> GetBoundaryEdges()
        {
            RebuildIfNeeded();
            return _pairToEdges.Where(kvp => kvp.Value.Count == 1).Select(kvp => kvp.Key);
        }

        public bool IsAuxLine(int faceIndex)
        {
            if (_meshObject == null || faceIndex < 0 || faceIndex >= _meshObject.FaceCount)
                return false;
            return _meshObject.Faces[faceIndex].VertexCount == 2;
        }

        public bool IsRealFace(int faceIndex)
        {
            if (_meshObject == null || faceIndex < 0 || faceIndex >= _meshObject.FaceCount)
                return false;
            return _meshObject.Faces[faceIndex].VertexCount >= 3;
        }

        public IEnumerable<int> GetOverlappingLineIndices(VertexPair edgePair)
        {
            return GetLinesAt(edgePair).Select(l => l.FaceIndex);
        }

        public IEnumerable<int> GetOverlappingFaceIndices(VertexPair linePair)
        {
            return GetEdgesAt(linePair).Select(e => e.FaceIndex).Distinct();
        }
    }
}
