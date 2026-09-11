// Assets/Editor/Poly_Ling/Serialization/ModelSerializer.cs
// モデルファイル (.mfmodel) のインポート/エクスポート
// Phase7: マルチマテリアル対応版
// Phase5: ModelContext統合
// Phase Morph: モーフ基準データ対応
// Phase BonePose: BonePoseData対応
//
// 【分割先】このファイルから次へ分けてある。
//   ModelSerializer_Data.cs          マテリアル参照・選択セット・モーフ・ミラーペア・データストア・オブジェクトグループ・BonePose の変換。
//   ModelSerializer_ModelContext.cs  ModelContext ⇔ ModelDTO の統合変換と復元（モデルレベルの付帯データを含む）。
//   ModelSerializer_Vrm.cs           IK／剛体／JOINT・VRM 1.0 設定・スプリングボーン・TPoseBackup の変換。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Materials;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;
using Poly_Ling.Ops;

namespace Poly_Ling.Serialization
{
    /// <summary>
    /// モデルファイルのシリアライザ
    /// </summary>
    public static partial class ModelSerializer
    {
        // ================================================================
        // 注意: このクラスはModelDataの変換処理のみを提供します
        // ファイルの読み書きはProjectSerializerを使用してください
        // ================================================================

        // ================================================================
        // 変換: MeshObject → MeshDTO
        // ================================================================

        /// <summary>
        /// MeshObjectをMeshDTOに変換
        /// </summary>
        public static MeshDTO ToMeshDTO(
            MeshObject meshObject,
            string name,
            BoneTransform exportSettings,
            HashSet<int> selectedVertices,
            List<Material> materials = null,
            int currentMaterialIndex = 0)
        {
            if (meshObject == null)
                return null;

            var meshDTO = new MeshDTO
            {
                name = name ?? meshObject.Name ?? "Untitled",
                isTriangulated = meshObject.IsTriangulated,
                skinKind = (int)meshObject.SkinKind
            };

            // BoneTransform
            meshDTO.exportSettingsDTO = ToBoneTransformDTO(exportSettings);

            // Vertices
            foreach (var vertex in meshObject.Vertices)
            {
                var vertexDTO = new VertexDTO();
                vertexDTO.id = vertex.Id;
                vertexDTO.sid = vertex.SubId;
                vertexDTO.pid = vertex.PartsId;
                vertexDTO.SetPosition(vertex.Position);
                vertexDTO.SetUVs(vertex.UVs);
                vertexDTO.SetNormals(vertex.Normals);
                vertexDTO.SetBoneWeight(vertex.BoneWeight);
                vertexDTO.SetMirrorBoneWeight(vertex.MirrorBoneWeight);
                vertexDTO.SetControlPoints(vertex.ControlPoints);
                vertexDTO.f = (byte)vertex.Flags;
                meshDTO.vertices.Add(vertexDTO);
            }

            // Faces（MaterialIndex含む）
            foreach (var face in meshObject.Faces)
            {
                var faceData = new FaceDTO
                {
                    id = face.Id,
                    v = new List<int>(face.VertexIndices),
                    uvi = new List<int>(face.UVIndices),
                    ni = new List<int>(face.NormalIndices),
                    mi = face.MaterialIndex != 0 ? face.MaterialIndex : (int?)null,  // 0はデフォルトなので省略
                    f = (byte)face.Flags
                };
                meshDTO.faces.Add(faceData);
            }

            // Selection
            if (selectedVertices != null && selectedVertices.Count > 0)
            {
                meshDTO.selectedVertices = selectedVertices.ToList();
            }

            // 注: materialPathList への書き込みは廃止
            // マテリアルは ModelDTO.materialReferences で一元管理
            //廃止　meshDTO.currentMaterialIndex = currentMaterialIndex;


            return meshDTO;
        }

        /// <summary>
        /// BoneTransformをBoneTransformDTOに変換
        /// </summary>
        public static BoneTransformDTO ToBoneTransformDTO(BoneTransform settings)
        {
            if (settings == null)
                return BoneTransformDTO.CreateDefault();

            var data = new BoneTransformDTO
            {
                useLocalTransform = settings.UseLocalTransform
            };
            data.SetPosition(settings.Position);
            data.SetRotation(settings.Rotation);
            data.SetScale(settings.Scale);

            return data;
        }

        /// <summary>
        /// WorkPlaneをWorkPlaneDataに変換
        /// </summary>
        public static WorkPlaneDTO ToWorkPlaneData(WorkPlaneContext workPlaneContext)
        {
            if (workPlaneContext == null)
                return WorkPlaneDTO.CreateDefault();

            return new WorkPlaneDTO
            {
                mode = workPlaneContext.Mode.ToString(),
                origin = new float[] { workPlaneContext.Origin.x, workPlaneContext.Origin.y, workPlaneContext.Origin.z },
                axisU = new float[] { workPlaneContext.AxisU.x, workPlaneContext.AxisU.y, workPlaneContext.AxisU.z },
                axisV = new float[] { workPlaneContext.AxisV.x, workPlaneContext.AxisV.y, workPlaneContext.AxisV.z },
                isLocked = workPlaneContext.IsLocked,
                lockOrientation = workPlaneContext.LockOrientation,
                autoUpdateOriginOnSelection = workPlaneContext.AutoUpdateOriginOnSelection
            };
        }

        /// <summary>
        /// WorkAxisContextをWorkAxisDTOに変換。
        /// Origin はワールド座標のまま保存する（座標系変換は行わない）。
        /// </summary>
        public static WorkAxisDTO ToWorkAxisData(WorkAxisContext workAxis)
        {
            if (workAxis == null)
                return WorkAxisDTO.CreateDefault();

            var o = workAxis.Origin;
            var r = workAxis.Rotation;

            return new WorkAxisDTO
            {
                origin    = new float[] { o.x, o.y, o.z },
                rotation  = new float[] { r.x, r.y, r.z, r.w },
                isVisible = workAxis.IsVisible,
                length    = workAxis.Length
            };
        }

        /// <summary>
        /// WorkAxisDTOをWorkAxisContextに適用。
        /// </summary>
        public static void ApplyToWorkAxis(WorkAxisDTO data, WorkAxisContext workAxis)
        {
            if (data == null || workAxis == null)
                return;

            if (data.origin != null && data.origin.Length >= 3)
                workAxis.Origin = new Vector3(data.origin[0], data.origin[1], data.origin[2]);

            if (data.rotation != null && data.rotation.Length >= 4)
                workAxis.Rotation = new Quaternion(
                    data.rotation[0], data.rotation[1], data.rotation[2], data.rotation[3]);

            workAxis.IsVisible = data.isVisible;

            // length が無い旧データは 0 で入ってくる。Length の下限クランプに
            // 任せると 1e-3 という使い物にならない長さになるため、ここで弾く。
            if (data.length > 0f) workAxis.Length = data.length;
        }

        // ================================================================
        // 変換: MeshDTO → MeshObject
        // ================================================================

        /// <summary>
        /// MeshDTOをMeshObjectに変換
        /// </summary>
        public static MeshObject ToMeshObject(MeshDTO meshDTO)
        {
            if (meshDTO == null)
                return null;

            var meshObject = new MeshObject(meshDTO.name);
            meshObject.IsTriangulated = meshDTO.isTriangulated;

            // Vertices
            foreach (var vd in meshDTO.vertices)
            {
                var vertex = new Vertex(vd.GetPosition());
                vertex.Id = vd.id;
                vertex.SubId = vd.sid;
                vertex.PartsId = vd.pid;
                vertex.UVs = vd.GetUVs();
                vertex.Normals = vd.GetNormals();
                vertex.BoneWeight = vd.GetBoneWeight();
                vertex.MirrorBoneWeight = vd.GetMirrorBoneWeight();
                vertex.ControlPoints = vd.GetControlPoints();
                vertex.Flags = (VertexFlags)vd.f;
                meshObject.Vertices.Add(vertex);
            }

            // Faces（MaterialIndex含む）
            foreach (var fd in meshDTO.faces)
            {
                var face = new Face
                {
                    Id = fd.id,
                    VertexIndices = new List<int>(fd.v ?? new List<int>()),
                    UVIndices = new List<int>(fd.uvi ?? new List<int>()),
                    NormalIndices = new List<int>(fd.ni ?? new List<int>()),
                    MaterialIndex = fd.mi ?? 0,  // nullの場合は0
                    Flags = (FaceFlags)fd.f
                };
                meshObject.Faces.Add(face);
            }

            // 描画オブジェクトの種別。
            //   欄がある     … 保存された明示状態をそのまま復元する。
            //                   ウェイト頂点が 0 でも Skinned のままにする（明示状態の往復）。
            //   欄が無い(null) … 旧データ。頂点のボーンウェイトから求め直す。
            //                   ここで再計算しないと、旧プロジェクトのスキンドメッシュが
            //                   MeshFilter 扱いになり WorldMatrix が二重に掛かる。
            if (meshDTO.skinKind.HasValue)
                meshObject.SetSkinKind((SkinKind)meshDTO.skinKind.Value);
            else
                meshObject.RecomputeSkinKind();

            return meshObject;
        }

        /// <summary>
        /// マテリアルリストを復元
        /// </summary>
        /// <remarks>
        /// [廃止] MeshDTO.materialPathList形式は廃止されました。
        /// マテリアルはModelDTO.materialReferencesで一元管理されます。
        /// </remarks>
        [System.Obsolete("MeshDTO.materialPathList形式は廃止されました。ModelDTO.materialReferencesを使用してください。")]
        public static List<Material> ToMaterials(MeshDTO meshDTO)
        {
            Debug.LogWarning("[ModelSerializer] ToMaterials()は廃止されました。");
            return new List<Material> { null };
        }

        /// <summary>
        /// BoneTransformDTOをBoneTransformに変換
        /// </summary>
        public static BoneTransform ToBoneTransform(BoneTransformDTO data)
        {
            if (data == null)
                return new BoneTransform();

            return new BoneTransform
            {
                UseLocalTransform = data.useLocalTransform,
                Position = data.GetPosition(),
                Rotation = data.GetRotation(),
                Scale = data.GetScale()
            };
        }

        /// <summary>
        /// WorkPlaneDataをWorkPlaneに適用
        /// </summary>
        public static void ApplyToWorkPlane(WorkPlaneDTO data, WorkPlaneContext workPlane)
        {
            if (data == null || workPlane == null)
                return;

            // Mode
            if (Enum.TryParse<WorkPlaneMode>(data.mode, out var mode))
            {
                workPlane.Mode = mode;
            }

            // Origin
            if (data.origin != null && data.origin.Length >= 3)
            {
                workPlane.Origin = new Vector3(data.origin[0], data.origin[1], data.origin[2]);
            }

            // AxisU
            if (data.axisU != null && data.axisU.Length >= 3)
            {
                workPlane.AxisU = new Vector3(data.axisU[0], data.axisU[1], data.axisU[2]);
            }

            // AxisV
            if (data.axisV != null && data.axisV.Length >= 3)
            {
                workPlane.AxisV = new Vector3(data.axisV[0], data.axisV[1], data.axisV[2]);
            }

            workPlane.IsLocked = data.isLocked;
            workPlane.LockOrientation = data.lockOrientation;
            workPlane.AutoUpdateOriginOnSelection = data.autoUpdateOriginOnSelection;
        }

        /// <summary>
        /// 選択状態を復元
        /// </summary>
        public static HashSet<int> ToSelectedVertices(MeshDTO meshDTO)
        {
            if (meshDTO?.selectedVertices == null)
                return new HashSet<int>();

            return new HashSet<int>(meshDTO.selectedVertices);
        }

        // ================================================================
        // MeshMetaDTO 変換（Phase 1）
        // MeshContext ↔ MeshMetaDTO（ジオメトリなし）
        // ================================================================

        /// <summary>
        /// MeshContext → MeshMetaDTO（メタデータのみ抽出）
        /// ジオメトリ（頂点・面）は含まない
        /// </summary>
        public static MeshMetaDTO ToMeshMetaDTO(MeshContext mc)
        {
            if (mc == null) return null;

            var meta = new MeshMetaDTO
            {
                name                    = mc.Name,
                type                    = mc.Type.ToString(),
                isVisible               = mc.IsVisible,
                isLocked                = mc.IsLocked,
                isFolding               = mc.IsFolding,
                depth                   = mc.Depth,
                parentIndex             = mc.ParentIndex,
                hierarchyParentIndex    = mc.HierarchyParentIndex,
                mirrorType              = mc.MirrorType,
                mirrorAxis              = mc.MirrorAxis,
                mirrorDistance          = mc.MirrorDistance,
                mirrorMaterialOffset    = mc.MirrorMaterialOffset,
                bakedMirrorSourceIndex  = mc.BakedMirrorSourceIndex,
                mirrorGeometryDerived   = mc.MirrorGeometryDerived,
                detachedMirrorObjectId  = mc.DetachedMirrorObjectId,
                hasBakedMirrorChild     = mc.HasBakedMirrorChild,
                morphParentIndex        = mc.MorphParentIndex,
                morphMirrorPolicy       = (int)mc.MorphMirrorPolicy,
                mirrorOfMorphIndex      = mc.MirrorOfMorphIndex,
                excludeFromExport       = mc.ExcludeFromExport,
                ignorePoseInArmature    = mc.IgnorePoseInArmature,
                isMirrorBranchRoot      = mc.IsMirrorBranchRoot,
                preserveNormals         = mc.PreserveNormals,
                exportSettingsDTO       = ToBoneTransformDTO(mc.BoneTransform),
            };

            // モーフ基準データ
            if (mc.IsMorph)
                meta.morphBaseData = ToMorphBaseDataDTO(mc.MorphBaseData);

            // BonePoseData
            if (mc.BonePoseData != null)
                meta.bonePoseData = mc.BonePoseData.ToDTO();

            // 選択セット
            meta.selectionSets = new System.Collections.Generic.List<SelectionSetDTO>();
            if (mc.PartsSelectionSetList != null)
            {
                foreach (var set in mc.PartsSelectionSetList)
                {
                    var dto = SelectionSetDTO.FromSelectionSet(set);
                    if (dto != null) meta.selectionSets.Add(dto);
                }
            }

            return meta;
        }

        /// <summary>
        /// MeshMetaDTO → MeshContext（MeshObject は空、呼び出し元で設定すること）
        /// </summary>
        public static MeshContext ToMeshContextFromMeta(MeshMetaDTO meta)
        {
            if (meta == null) return null;

            MeshType meshType = MeshType.Mesh;
            if (!string.IsNullOrEmpty(meta.type))
                Enum.TryParse(meta.type, out meshType);

            var mc = new MeshContext
            {
                Name                   = meta.name ?? "Untitled",
                Type                   = meshType,
                IsVisible              = meta.isVisible,
                IsLocked               = meta.isLocked,
                IsFolding              = meta.isFolding,
                Depth                  = meta.depth,
                ParentIndex            = meta.parentIndex,
                HierarchyParentIndex   = meta.hierarchyParentIndex,
                MirrorType             = meta.mirrorType,
                MirrorAxis             = meta.mirrorAxis,
                MirrorDistance         = meta.mirrorDistance,
                MirrorMaterialOffset   = meta.mirrorMaterialOffset,
                BakedMirrorSourceIndex = meta.bakedMirrorSourceIndex,
                MirrorGeometryDerived  = meta.mirrorGeometryDerived,
                DetachedMirrorObjectId = meta.detachedMirrorObjectId,
                HasBakedMirrorChild    = meta.hasBakedMirrorChild,
                MorphParentIndex       = meta.morphParentIndex,
                MorphMirrorPolicy      = (MorphMirrorPolicy)meta.morphMirrorPolicy,
                MirrorOfMorphIndex     = meta.mirrorOfMorphIndex,
                ExcludeFromExport      = meta.excludeFromExport,
                IgnorePoseInArmature   = meta.ignorePoseInArmature,
                IsMirrorBranchRoot     = meta.isMirrorBranchRoot,
                PreserveNormals        = meta.preserveNormals,
                BoneTransform          = meta.exportSettingsDTO != null
                                         ? ToBoneTransform(meta.exportSettingsDTO)
                                         : null,
            };

            // モーフ基準データ
            if (meta.morphBaseData != null)
                mc.MorphBaseData = ToMorphBaseData(meta.morphBaseData);

            // BonePoseData
            if (meta.bonePoseData != null)
                mc.BonePoseData = BonePoseData.FromDTO(meta.bonePoseData);

            // 選択セット
            mc.PartsSelectionSetList = new System.Collections.Generic.List<Selection.PartsSelectionSet>();
            if (meta.selectionSets != null)
            {
                foreach (var dto in meta.selectionSets)
                {
                    var set = dto?.ToSelectionSet();
                    if (set != null) mc.PartsSelectionSetList.Add(set);
                }
            }

            return mc;
        }

        // ================================================================
        // MeshGeoDTO 変換（Phase 1）
        // MeshObject ↔ MeshGeoDTO
        // ================================================================

        /// <summary>
        /// MeshObject → MeshGeoDTO
        /// </summary>
        public static MeshGeoDTO ToMeshGeoDTO(MeshObject meshObject, int meshIndex)
        {
            if (meshObject == null) return null;

            var geo = new MeshGeoDTO
            {
                meshIndex  = meshIndex,
                isTriangulated = meshObject.IsTriangulated,
                skinKind   = (int)meshObject.SkinKind,
            };

            foreach (var vertex in meshObject.Vertices)
            {
                var vd = new VertexDTO();
                vd.id = vertex.Id;
                vd.sid = vertex.SubId;
                vd.pid = vertex.PartsId;
                vd.SetPosition(vertex.Position);
                vd.SetUVs(vertex.UVs);
                vd.SetNormals(vertex.Normals);
                vd.SetBoneWeight(vertex.BoneWeight);
                vd.SetMirrorBoneWeight(vertex.MirrorBoneWeight);
                vd.SetControlPoints(vertex.ControlPoints);
                vd.f = (byte)vertex.Flags;
                geo.vertices.Add(vd);
            }

            foreach (var face in meshObject.Faces)
            {
                geo.faces.Add(new FaceDTO
                {
                    id  = face.Id,
                    v   = new System.Collections.Generic.List<int>(face.VertexIndices),
                    uvi = new System.Collections.Generic.List<int>(face.UVIndices),
                    ni  = new System.Collections.Generic.List<int>(face.NormalIndices),
                    mi  = face.MaterialIndex != 0 ? face.MaterialIndex : (int?)null,
                    f   = (byte)face.Flags,
                });
            }

            return geo;
        }

        /// <summary>
        /// MeshGeoDTO → MeshObject
        /// </summary>
        public static MeshObject ToMeshObjectFromGeo(MeshGeoDTO geo)
        {
            if (geo == null) return null;

            // ToMeshObject はMeshDTOを受け取るため、最小限のMeshDTOに詰め替えて委譲
            var tmp = new MeshDTO
            {
                isTriangulated = geo.isTriangulated,
                skinKind   = geo.skinKind,
                vertices   = geo.vertices ?? new System.Collections.Generic.List<VertexDTO>(),
                faces      = geo.faces    ?? new System.Collections.Generic.List<FaceDTO>(),
            };
            return ToMeshObject(tmp);
        }
    }
}
