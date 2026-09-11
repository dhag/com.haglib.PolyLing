// ModelDTO.Mesh.cs
// DTO：メッシュコンテキスト・頂点・面・MeshMetaDTO・MeshGeoDTO。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置（ModelDTO.cs と同じ名前空間。ModelDTO.cs から分割）

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Serialization
{
    // ================================================================
    // メッシュコンテキスト
    // ================================================================

    /// <summary>
    /// 個別メッシュコンテキストのデータ
    /// </summary>
    [Serializable]
    public class MeshDTO
    {
        /// <summary>メッシュ名</summary>
        public string name;

        /// <summary>エクスポート時のトランスフォーム設定</summary>
        public BoneTransformDTO exportSettingsDTO;

        /// <summary>頂点データ</summary>
        public List<VertexDTO> vertices = new List<VertexDTO>();

        /// <summary>面データ</summary>
        public List<FaceDTO> faces = new List<FaceDTO>();

        /// <summary>選択中の頂点インデックス</summary>
        public List<int> selectedVertices = new List<int>();

        // ================================================================
        // 拡張選択状態（Phase 8追加）
        // ================================================================

        /// <summary>選択中のエッジ [[v1,v2], [v1,v2], ...]</summary>
        public List<int[]> selectedEdges = new List<int[]>();

        /// <summary>選択中の面インデックス</summary>
        public List<int> selectedFaces = new List<int>();

        /// <summary>選択中の線分インデックス（2頂点Face）</summary>
        public List<int> selectedLines = new List<int>();

        /// <summary>選択モード ("Vertex", "Edge", "Face", "Line", or combined flags)</summary>
        public string selectMode = "Vertex";

        // ================================================================
        // 選択セット（永続的な名前付き選択）
        // ================================================================

        /// <summary>保存された選択セット</summary>
        public List<SelectionSetDTO> selectionSets = new List<SelectionSetDTO>();

        /// <summary>法線の自動再計算から除外するセット</summary>
        public List<SelectionSetDTO> normalExcludeSets = new List<SelectionSetDTO>();

        // ================================================================
        // マテリアル [廃止セクション]
        // マテリアルはModelDTO.materialReferencesで一元管理されます
        // ================================================================

        /// <summary>[廃止] マテリアルリスト - ModelDTO.materialReferencesを使用してください</summary>
        [System.Obsolete("ModelDTO.materialReferencesを使用してください")]
        public List<string> materialPathList = new List<string>();

        /// <summary>[廃止] 現在選択中のマテリアルインデックス</summary>
        //[System.Obsolete("ModelDTO.currentMaterialIndexを使用してください")]
        //public int currentMaterialIndex = 0;

        // ================================================================
        // 階層情報
        // ================================================================

        /// <summary>メッシュの種類 ("Mesh", "Bone", "Helper", "Group", "Morph")</summary>
        public string type = "Mesh";

        /// <summary>親メッシュのインデックス（-1=ルート）</summary>
        public int parentIndex = -1;

        /// <summary>階層深度（MQO互換、0=ルート）</summary>
        public int depth = 0;

        /// <summary>ゲームオブジェクト階層の親（将来用）</summary>
        public int hierarchyParentIndex = -1;

        /// <summary>可視状態</summary>
        public bool isVisible = true;

        /// <summary>編集禁止（ロック）</summary>
        public bool isLocked = false;

        /// <summary>折りたたみ状態（MQO互換）</summary>
        public bool isFolding = false;

        /// <summary>
        /// 頂点が三角形化済みか（PMX形式）
        /// true: 各頂点が1つのUV/法線を持ち、すべての面が三角形 (PMX互換)
        /// false: 頂点が複数のUV/法線を持つ可能性あり (MQO形式)
        /// </summary>
        public bool isTriangulated = false;

        /// <summary>
        /// 頂点法線を保持する（自動再計算を行わない）
        /// </summary>
        public bool preserveNormals = false;

        /// <summary>
        /// 描画オブジェクトの種別（MeshObject.SkinKind の値。0=MeshFilter, 1=Skinned）。
        ///
        /// null = この欄を持たない旧データ。読み込み側は頂点のボーンウェイトから
        /// 再計算する（ModelSerializer.ToMeshObject）。ここを非 null の既定値にすると、
        /// 旧プロジェクトのスキンドメッシュが MeshFilter として復元され、
        /// WorldMatrix が二重に掛かって位置が飛ぶ。
        /// </summary>
        public int? skinKind = null;

        // ================================================================
        // ミラー設定
        // ================================================================

        /// <summary>ミラータイプ (0:なし, 1:分離, 2:結合)</summary>
        public int mirrorType = 0;

        /// <summary>ミラー軸 (1:X, 2:Y, 4:Z)</summary>
        public int mirrorAxis = 1;

        /// <summary>ミラー距離</summary>
        public float mirrorDistance = 0f;

        /// <summary>ミラー側マテリアルオフセット（ミラー側マテリアルインデックス = 実体側 + オフセット）</summary>
        public int mirrorMaterialOffset = 0;

        /// <summary>
        /// ベイク元メッシュのインデックス（Type==BakedMirrorの時に使用）
        /// -1 = ベイクミラーではない
        /// </summary>
        public int bakedMirrorSourceIndex = -1;

        /// <summary>
        /// ミラー形状が実体側から生成されたものか（MeshContext.MirrorGeometryDerived）。
        /// 既定 false。旧データを読んだ場合は false になり、従来どおりの扱いになる。
        /// </summary>
        public bool mirrorGeometryDerived = false;

        /// <summary>
        /// 切り離したミラー側の ObjectId（MeshContext.DetachedMirrorObjectId）。
        /// 0 = なし。再ミラー化のときに相手を引き当てる。
        /// </summary>
        public ulong detachedMirrorObjectId = 0;

        /// <summary>
        /// ベイクドミラーの子を持つか（ソース側で設定）
        /// true = このメッシュのベイクドミラーが存在する
        /// </summary>
        public bool hasBakedMirrorChild = false;

        // ================================================================
        // モーフ基準データ（Phase: Morph対応）
        // ================================================================

        /// <summary>
        /// モーフ基準データ（モーフ前の位置等を保持）
        /// null = モーフではない通常メッシュ
        /// </summary>
        public MorphBaseDataDTO morphBaseData;

        /// <summary>
        /// モーフ親メッシュのインデックス
        /// -1 = 未指定（名前規則ベースで検索）
        /// </summary>
        public int morphParentIndex = -1;

        /// <summary>
        /// モーフのミラー適用ポリシー（MorphMirrorPolicy）。
        /// 既定 0 = FollowParent。規約は MorphMirrorPolicy.cs を正典とする。
        /// </summary>
        public int morphMirrorPolicy = 0;

        /// <summary>
        /// MirrorOf のときの参照先モーフのインデックス。-1 = 未指定。
        /// </summary>
        public int mirrorOfMorphIndex = -1;

        /// <summary>
        /// ボーンポーズデータ（PreBindPose + Manualレイヤー）
        /// null = BonePoseData未使用
        /// </summary>
        public BonePoseDataDTO bonePoseData;

        /// <summary>
        /// エクスポートから除外するか
        /// true: モデルエクスポート時にこのメッシュを出力しない
        /// </summary>
        public bool excludeFromExport = false;

        /// <summary>
        /// アーマチャ生成時にボーンを生成しない
        /// </summary>
        public bool ignorePoseInArmature = false;

        /// <summary>
        /// ミラー分岐のルートか（ヒエラルキーエクスポートで枝を二分する）
        /// </summary>
        public bool isMirrorBranchRoot = false;

        // ================================================================
        // 永続化拡張（DTO単一真実源化）：従来CSV直書きでのみ保持していた
        // IK / BindPose / BoneModelRotation と、未保存だった 剛体 / JOINT を
        // MeshDTO に集約する。null = 当該データなし。
        // ================================================================

        /// <summary>IK情報（null=非IKボーン）。</summary>
        public IKDataDTO ikData;

        /// <summary>IKリンクの per-bone データ（null=非IKリンク）。</summary>
        public IKLinkDataDTO ikLink;

        /// <summary>Unity Humanoid 割当名（空/null=非割当）。#5b: per-bone。</summary>
        public string humanBodyBone;

        /// <summary>
        /// 左右で対になるボーンの MeshContextList 索引（-1=対なし）。
        /// スキンド変換が確定させた値。ここに無いと保存往復で消える。
        /// </summary>
        public int mirrorBoneIndex = -1;

        /// <summary>Humanoid マッスル可動域（null=既定使用）。#5d-1: per-bone。</summary>
        public HumanLimitDataDTO humanLimit;

        /// <summary>BindPose（4x4・行優先16値。null=未設定/単位行列）。</summary>
        public float[] bindPose;

        /// <summary>ボーンモデル回転（[x,y,z,w]。null=未設定/単位）。</summary>
        public float[] boneModelRotation;

        /// <summary>剛体データ（null=非剛体）。</summary>
        public RigidBodyDataDTO rigidBodyData;

        /// <summary>JOINTデータ（null=非JOINT）。</summary>
        public JointDataDTO jointData;

        /// <summary>スプリングボーン・コライダー（null/空=なし。1ボーンに複数可）。</summary>
        public List<SpringBoneColliderDataDTO> springBoneColliders;

        /// <summary>スプリングボーン・ジョイント（null=非揺れjoint）。</summary>
        public SpringBoneJointDataDTO springBoneJoint;

        /// <summary>スプリングボーン・チェーンルート（null=非ルート）。</summary>
        public SpringBoneChainDataDTO springBoneChainRoot;

        /// <summary>
        /// 一人称カメラでの扱い（VrmFirstPersonType の値。0=Auto）。
        /// 旧データはこの欄を持たず 0 になり、従来どおり VRM の既定に任せる。
        /// </summary>
        public int vrmFirstPersonType = 0;

        /// <summary>ノード制約（VRMC_node_constraint。null=なし）。</summary>
        public VrmNodeConstraintDTO vrmConstraint;
    }

    // ================================================================
    // 頂点データ
    // ================================================================

    /// <summary>
    /// 頂点データ（効率的な配列形式）
    /// </summary>
    [Serializable]
    public class VertexDTO
    {
        /// <summary>頂点ID（モーフ追跡等に使用）</summary>
        public int id;

        /// <summary>
        /// サブID（部品ローカルなインデックス）。0 = 未設定。
        /// この項目が無い旧データを読んだときは 0 のままになる。
        /// </summary>
        public int sid;

        /// <summary>
        /// 部品ID（オブジェクト内の部品を表すグループ番号）。0 = 未設定。
        /// この項目が無い旧データを読んだときは 0 のままになる。
        /// </summary>
        public int pid;

        /// <summary>位置 [x, y, z]</summary>
        public float[] p;

        /// <summary>UV座標リスト [[u,v], [u,v], ...]</summary>
        public List<float[]> uv;

        /// <summary>法線リスト [[x,y,z], [x,y,z], ...]</summary>
        public List<float[]> n;

        /// <summary>
        /// ボーンウェイト [i0, i1, i2, i3, w0, w1, w2, w3]
        /// null = スキニングなし
        /// </summary>
        public float[] bw;

        /// <summary>
        /// ミラー側ボーンウェイト [i0, i1, i2, i3, w0, w1, w2, w3]
        /// null = ミラーウェイトなし
        /// </summary>
        public float[] mbw;

        /// <summary>頂点フラグ (VertexFlags)</summary>
        public byte f;

        /// <summary>
        /// オプション位置リスト（制御点位置など） [[x,y,z], [x,y,z], ...]
        /// null = 保持なし（NullValueHandling.Ignore で出力自体が省かれる）
        /// </summary>
        public List<float[]> cp;

        // === 変換ヘルパー ===

        public Vector3 GetPosition()
        {
            if (p == null || p.Length < 3) return Vector3.zero;
            return new Vector3(p[0], p[1], p[2]);
        }

        public void SetPosition(Vector3 pos)
        {
            p = new float[] { pos.x, pos.y, pos.z };
        }

        public List<Vector2> GetUVs()
        {
            var result = new List<Vector2>();
            if (uv != null)
            {
                foreach (var u in uv)
                {
                    if (u != null && u.Length >= 2)
                        result.Add(new Vector2(u[0], u[1]));
                }
            }
            return result;
        }

        public void SetUVs(List<Vector2> uvs)
        {
            uv = new List<float[]>();
            foreach (var u in uvs)
            {
                uv.Add(new float[] { u.x, u.y });
            }
        }

        public List<Vector3> GetNormals()
        {
            var result = new List<Vector3>();
            if (n != null)
            {
                foreach (var normal in n)
                {
                    if (normal != null && normal.Length >= 3)
                        result.Add(new Vector3(normal[0], normal[1], normal[2]));
                }
            }
            return result;
        }

        public void SetNormals(List<Vector3> normals)
        {
            n = new List<float[]>();
            foreach (var normal in normals)
            {
                n.Add(new float[] { normal.x, normal.y, normal.z });
            }
        }

        public BoneWeight? GetBoneWeight()
        {
            if (bw == null || bw.Length < 8)
                return null;

            return new BoneWeight
            {
                boneIndex0 = (int)bw[0],
                boneIndex1 = (int)bw[1],
                boneIndex2 = (int)bw[2],
                boneIndex3 = (int)bw[3],
                weight0 = bw[4],
                weight1 = bw[5],
                weight2 = bw[6],
                weight3 = bw[7]
            };
        }

        public void SetBoneWeight(BoneWeight? boneWeight)
        {
            if (!boneWeight.HasValue)
            {
                bw = null;
                return;
            }

            var b = boneWeight.Value;
            bw = new float[]
            {
                b.boneIndex0, b.boneIndex1, b.boneIndex2, b.boneIndex3,
                b.weight0, b.weight1, b.weight2, b.weight3
            };
        }

        public BoneWeight? GetMirrorBoneWeight()
        {
            if (mbw == null || mbw.Length < 8)
                return null;

            return new BoneWeight
            {
                boneIndex0 = (int)mbw[0],
                boneIndex1 = (int)mbw[1],
                boneIndex2 = (int)mbw[2],
                boneIndex3 = (int)mbw[3],
                weight0 = mbw[4],
                weight1 = mbw[5],
                weight2 = mbw[6],
                weight3 = mbw[7]
            };
        }

        public void SetMirrorBoneWeight(BoneWeight? boneWeight)
        {
            if (!boneWeight.HasValue)
            {
                mbw = null;
                return;
            }

            var b = boneWeight.Value;
            mbw = new float[]
            {
                b.boneIndex0, b.boneIndex1, b.boneIndex2, b.boneIndex3,
                b.weight0, b.weight1, b.weight2, b.weight3
            };
        }

        /// <summary>
        /// オプション位置リストを取り出す。保持していなければ null。
        /// Vertex.ControlPoints が null のときと空のときを区別しないため、
        /// 空リストは null として扱う。
        /// </summary>
        public List<Vector3> GetControlPoints()
        {
            if (cp == null || cp.Count == 0) return null;

            var result = new List<Vector3>(cp.Count);
            foreach (var c in cp)
            {
                if (c != null && c.Length >= 3)
                    result.Add(new Vector3(c[0], c[1], c[2]));
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>オプション位置リストを設定する。null/空なら cp も null。</summary>
        public void SetControlPoints(List<Vector3> points)
        {
            if (points == null || points.Count == 0)
            {
                cp = null;
                return;
            }

            cp = new List<float[]>(points.Count);
            foreach (var p in points)
            {
                cp.Add(new float[] { p.x, p.y, p.z });
            }
        }
    }

    // ================================================================
    // 面データ
    // ================================================================

    /// <summary>
    /// 面データ
    /// </summary>
    [Serializable]
    public class FaceDTO
    {
        /// <summary>面ID（モーフ追跡等に使用）</summary>
        public int id;

        /// <summary>頂点インデックスリスト</summary>
        public List<int> v;

        /// <summary>UVサブインデックスリスト</summary>
        public List<int> uvi;

        /// <summary>法線サブインデックスリスト</summary>
        public List<int> ni;

        /// <summary>マテリアルインデックス（省略時は0）</summary>
        public int? mi;

        /// <summary>面フラグ (FaceFlags)</summary>
        public byte f;
    }

    // ================================================================
    // MeshMetaDTO（Phase 1: メタデータのみ、ジオメトリなし）
    // Remote分割送信のヘッダフェーズで使用。
    // MeshDTOからvertices/facesを除いた全フィールドを保持する。
    // ================================================================

    /// <summary>
    /// メッシュメタデータのみのDTO（ジオメトリを含まない）
    /// Remote通信のヘッダフェーズで使用
    /// </summary>
    [Serializable]
    public class MeshMetaDTO
    {
        public string name;
        public string type = "Mesh";
        public bool   isVisible  = true;
        public bool   isLocked   = false;
        public bool   isFolding  = false;
        public int    depth      = 0;
        public int    parentIndex = -1;
        public int    hierarchyParentIndex = -1;
        public int    mirrorType = 0;
        public int    mirrorAxis = 1;
        public float  mirrorDistance = 0f;
        public int    mirrorMaterialOffset = 0;
        public int    bakedMirrorSourceIndex = -1;
        public bool   hasBakedMirrorChild = false;
        /// <summary>
        /// ミラー形状が実体側から生成されたものか（MeshContext.MirrorGeometryDerived）。
        /// 既定 false。旧データを読んだ場合は false になり、従来どおりの扱いになる。
        /// </summary>
        public bool   mirrorGeometryDerived = false;
        /// <summary>
        /// 切り離したミラー側の ObjectId（MeshContext.DetachedMirrorObjectId）。
        /// 0 = なし。再ミラー化のときに相手を引き当てる。
        /// </summary>
        public ulong  detachedMirrorObjectId = 0;
        public int    morphParentIndex = -1;
        /// <summary>モーフのミラー適用ポリシー（既定 0 = FollowParent）。</summary>
        public int    morphMirrorPolicy = 0;
        /// <summary>MirrorOf のときの参照先モーフのインデックス。-1 = 未指定。</summary>
        public int    mirrorOfMorphIndex = -1;
        public bool   excludeFromExport = false;
        public bool   ignorePoseInArmature = false;
        public bool   isMirrorBranchRoot = false;
        public bool   preserveNormals = false;
        public MorphBaseDataDTO  morphBaseData;
        public BoneTransformDTO  exportSettingsDTO;
        public BonePoseDataDTO   bonePoseData;
        public List<SelectionSetDTO> selectionSets = new List<SelectionSetDTO>();
    }

    // ================================================================
    // MeshGeoDTO（Phase 1: ジオメトリのみ）
    // Remote分割送信のメッシュフェーズで使用。
    // ================================================================

    /// <summary>
    /// メッシュジオメトリのみのDTO（メタデータを含まない）
    /// Remote通信のメッシュフェーズで使用
    /// </summary>
    [Serializable]
    public class MeshGeoDTO
    {
        /// <summary>対応するメッシュのインデックス（ModelContext.MeshContextList内）</summary>
        public int meshIndex;
        public bool isTriangulated = false;

        /// <summary>描画オブジェクトの種別（0=MeshFilter, 1=Skinned）。null=旧データ→再計算。</summary>
        public int? skinKind = null;

        public List<VertexDTO> vertices = new List<VertexDTO>();
        public List<FaceDTO>   faces    = new List<FaceDTO>();
    }
}
