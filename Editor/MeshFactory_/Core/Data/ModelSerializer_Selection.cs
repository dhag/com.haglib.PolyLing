// ================================================================
// Phase 8 Step 1d: ModelSerializer 選択状態シリアライズ対応
// ================================================================
//
// 以下のメソッドを追加/修正してください
//
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;

namespace MeshFactory.Serialization
{
    // ModelSerializer クラスに以下のメソッドを追加/修正

    public static partial class ModelSerializer
    {
        // ================================================================
        // 追加: 選択状態の変換ヘルパー
        // ================================================================

        /// <summary>
        /// MeshContextの選択状態をMeshDTOに保存
        /// </summary>
        public static void SaveSelectionToDTO(MeshContext meshContext, MeshDTO meshDTO)
        {
            if (meshContext == null || meshDTO == null) return;

            // Vertices
            if (meshContext.SelectedVertices != null && meshContext.SelectedVertices.Count > 0)
            {
                meshDTO.selectedVertices = meshContext.SelectedVertices.ToList();
            }
            else
            {
                meshDTO.selectedVertices = new List<int>();
            }

            // Edges (VertexPair → int[2])
            if (meshContext.SelectedEdges != null && meshContext.SelectedEdges.Count > 0)
            {
                meshDTO.selectedEdges = new List<int[]>();
                foreach (var edge in meshContext.SelectedEdges)
                {
                    meshDTO.selectedEdges.Add(new int[] { edge.V1, edge.V2 });
                }
            }
            else
            {
                meshDTO.selectedEdges = new List<int[]>();
            }

            // Faces
            if (meshContext.SelectedFaces != null && meshContext.SelectedFaces.Count > 0)
            {
                meshDTO.selectedFaces = meshContext.SelectedFaces.ToList();
            }
            else
            {
                meshDTO.selectedFaces = new List<int>();
            }

            // Lines
            if (meshContext.SelectedLines != null && meshContext.SelectedLines.Count > 0)
            {
                meshDTO.selectedLines = meshContext.SelectedLines.ToList();
            }
            else
            {
                meshDTO.selectedLines = new List<int>();
            }

            // SelectMode
            meshDTO.selectMode = meshContext.SelectMode.ToString();
        }

        /// <summary>
        /// MeshDTOの選択状態をMeshContextに復元
        /// </summary>
        public static void LoadSelectionFromDTO(MeshDTO meshDTO, MeshContext meshContext)
        {
            if (meshDTO == null || meshContext == null) return;

            // Vertices
            meshContext.SelectedVertices = meshDTO.selectedVertices != null
                ? new HashSet<int>(meshDTO.selectedVertices)
                : new HashSet<int>();

            // Edges (int[2] → VertexPair)
            meshContext.SelectedEdges = new HashSet<VertexPair>();
            if (meshDTO.selectedEdges != null)
            {
                foreach (var edgeArr in meshDTO.selectedEdges)
                {
                    if (edgeArr != null && edgeArr.Length >= 2)
                    {
                        meshContext.SelectedEdges.Add(new VertexPair(edgeArr[0], edgeArr[1]));
                    }
                }
            }

            // Faces
            meshContext.SelectedFaces = meshDTO.selectedFaces != null
                ? new HashSet<int>(meshDTO.selectedFaces)
                : new HashSet<int>();

            // Lines
            meshContext.SelectedLines = meshDTO.selectedLines != null
                ? new HashSet<int>(meshDTO.selectedLines)
                : new HashSet<int>();

            // SelectMode
            if (!string.IsNullOrEmpty(meshDTO.selectMode))
            {
                if (Enum.TryParse<MeshSelectMode>(meshDTO.selectMode, out var mode))
                {
                    meshContext.SelectMode = mode;
                }
            }
        }

        // ================================================================
        // 修正: FromMeshContext
        // ================================================================
        // 
        // 以下の行を追加してください（contextData != null のブロック内、最後に）:
        //
        //     // 選択状態（Phase 8追加）
        //     SaveSelectionToDTO(meshContext, contextData);
        //

        /*
        public static MeshDTO FromMeshContext(MeshContext meshContext, HashSet<int> selectedVertices = null)
        {
            if (meshContext == null)
                return null;

            var contextData = ToMeshDTO(
                meshContext.MeshObject,
                meshContext.Name,
                meshContext.BoneTransform,
                selectedVertices,
                null,
                0
            );

            if (contextData != null)
            {
                // オブジェクト属性
                contextData.type = meshContext.Type.ToString();
                contextData.parentIndex = meshContext.ParentIndex;
                contextData.depth = meshContext.Depth;
                contextData.hierarchyParentIndex = meshContext.HierarchyParentIndex;
                contextData.isVisible = meshContext.IsVisible;
                contextData.isLocked = meshContext.IsLocked;

                // ミラー設定
                contextData.mirrorType = meshContext.MirrorType;
                contextData.mirrorAxis = meshContext.MirrorAxis;
                contextData.mirrorDistance = meshContext.MirrorDistance;

                // ★Phase 8追加: 選択状態
                SaveSelectionToDTO(meshContext, contextData);
            }

            return contextData;
        }
        */

        // ================================================================
        // 修正: ToMeshContext
        // ================================================================
        //
        // 以下の行を追加してください（return new MeshContext の後、または別途メソッド呼び出し）:
        //

        /*
        public static MeshContext ToMeshContext(MeshDTO meshDTO)
        {
            if (meshDTO == null)
                return null;

            var meshObject = ToMeshObject(meshDTO);
            if (meshObject == null)
                return null;

            // MeshTypeをパース
            MeshType meshType = MeshType.Mesh;
            if (!string.IsNullOrEmpty(meshDTO.type))
            {
                Enum.TryParse(meshDTO.type, out meshType);
            }

            var meshContext = new MeshContext
            {
                Name = meshDTO.name ?? "UnityMesh",
                MeshObject = meshObject,
                UnityMesh = meshObject.ToUnityMesh(),
                OriginalPositions = meshObject.Vertices.Select(v => v.Position).ToArray(),
                BoneTransform = ToBoneTransform(meshDTO.exportSettingsDTO),
                // オブジェクト属性
                Type = meshType,
                ParentIndex = meshDTO.parentIndex,
                Depth = meshDTO.depth,
                IsVisible = meshDTO.isVisible,
                IsLocked = meshDTO.isLocked,
                // ミラー設定
                MirrorType = meshDTO.mirrorType,
                MirrorAxis = meshDTO.mirrorAxis,
                MirrorDistance = meshDTO.mirrorDistance
            };

            // ★Phase 8追加: 選択状態を復元
            LoadSelectionFromDTO(meshDTO, meshContext);

            return meshContext;
        }
        */

        // ================================================================
        // 修正: ToSelectedVertices（拡張版）
        // ================================================================
        //
        // 既存のToSelectedVerticesは後方互換のため残し、
        // 新しいメソッドを追加するか、LoadSelectionFromDTOを使用
        //

        /// <summary>
        /// 選択状態を復元（拡張版：Edge/Face/Line/Mode含む）
        /// </summary>
        public static MeshSelectionSnapshot ToSelectionSnapshot(MeshDTO meshDTO)
        {
            if (meshDTO == null)
                return new MeshSelectionSnapshot();

            var snapshot = new MeshSelectionSnapshot
            {
                Vertices = meshDTO.selectedVertices != null
                    ? new HashSet<int>(meshDTO.selectedVertices)
                    : new HashSet<int>(),
                Edges = new HashSet<VertexPair>(),
                Faces = meshDTO.selectedFaces != null
                    ? new HashSet<int>(meshDTO.selectedFaces)
                    : new HashSet<int>(),
                Lines = meshDTO.selectedLines != null
                    ? new HashSet<int>(meshDTO.selectedLines)
                    : new HashSet<int>()
            };

            // Edges
            if (meshDTO.selectedEdges != null)
            {
                foreach (var edgeArr in meshDTO.selectedEdges)
                {
                    if (edgeArr != null && edgeArr.Length >= 2)
                    {
                        snapshot.Edges.Add(new VertexPair(edgeArr[0], edgeArr[1]));
                    }
                }
            }

            // Mode
            if (!string.IsNullOrEmpty(meshDTO.selectMode) &&
                Enum.TryParse<MeshSelectMode>(meshDTO.selectMode, out var mode))
            {
                snapshot.Mode = mode;
            }

            return snapshot;
        }
    }
}
