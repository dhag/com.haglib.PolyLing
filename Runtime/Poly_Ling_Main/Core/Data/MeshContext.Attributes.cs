// MeshContext.Attributes.cs
// MeshContext：オブジェクト属性・協働編集・モーフ基準とミラー適用・エクスポート制御・ミラー設定と
// キャッシュ・ベイクミラー・マテリアル・メッシュ操作。
// Runtime/Poly_Ling_Main/Core/Data/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.UndoSystem;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;
using Poly_Ling.Selection;
using Poly_Ling.Context;
using Poly_Ling.MeshBridge;
using Poly_Ling.Localization;
using static Poly_Ling.Gizmo.GLGizmoDrawer;
using Poly_Ling.Rendering;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
{
    public partial class MeshContext
    {
        // ================================================================
        // オブジェクト属性（MQOからインポート）
        // ================================================================

        /// <summary>メッシュの種類（MeshObject.Typeに委譲）</summary>
        public MeshType Type
        {
            get => MeshObject?.Type ?? MeshType.Mesh;
            set { if (MeshObject != null) MeshObject.Type = value; }
        }

        // ----------------------------------------------------------------
        // 親子関係について
        // ----------------------------------------------------------------
        // MQOでは「depth」値で親子関係を表現する（リスト順序に依存）。
        // しかしdepth値だけでは削除・順序変更で親子関係が破綻する。
        //
        // 【設計方針】
        // - Depth: MQOとの互換用。表示インデント等に使用。
        // - ParentIndex: 実際の親子関係。削除・移動時はこちらを更新。
        //
        // 【運用ルール】
        // - インポート時: MQOのdepthからParentIndexを計算して設定
        // - 削除時: 子のParentIndexを親の親に付け替える
        // - 順序変更時: ParentIndexを新しいインデックスに更新
        // - エクスポート時: ParentIndexからdepthを再計算
        // ----------------------------------------------------------------

        /// <summary>可視状態</summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>編集禁止（ロック）</summary>
        public bool IsLocked { get; set; } = false;

        // ================================================================
        // 協働編集（グループワーク）用の識別と担当
        // ----------------------------------------------------------------
        // ObjectId  : リスト内の位置（MasterIndex）に依存しない安定識別子。
        //             追加・削除・並べ替えを跨いで同一オブジェクトを指す。
        //             0 は「未割当」。ObjectIdAllocator.EnsureIds で遅延割当される。
        //             複製（Duplicate）は別オブジェクトなので新IDを振る。
        //             Undo/Redo のスナップショット復元は同一オブジェクトなのでIDを保つ。
        // EditorName: 現在の編集者名。空文字は「担当者なし（誰でも編集可）」。
        //             サーバはこの値と register 済みユーザー名を突き合わせて
        //             リモートコマンドの可否を判定する（RemoteOwnership）。
        //             手動 claim / release のみで変化し、切断では解放しない。
        //             プロジェクト保存に含まれる。
        // ================================================================

        /// <summary>位置非依存の安定オブジェクトID（0=未割当）</summary>
        public ulong ObjectId { get; set; } = 0;

        /// <summary>現在の編集者名（空文字＝担当者なし）</summary>
        public string EditorName { get; set; } = "";

        /// <summary>編集者が設定されているか</summary>
        public bool HasEditor => !string.IsNullOrEmpty(EditorName);

        /// <summary>
        /// 指定ユーザーがこのオブジェクトを編集できるか。
        /// 担当者なし、または自分が担当者のときに true。
        /// </summary>
        public bool IsEditableBy(string userName)
        {
            if (string.IsNullOrEmpty(EditorName)) return true;
            return string.Equals(EditorName, userName, StringComparison.Ordinal);
        }

        /// <summary>折りたたみ状態（MQO互換）</summary>
        public bool IsFolding { get; set; } = false;

        // ================================================================
        // モーフ基準データ（Phase: Morph対応）
        // ================================================================
        // 
        // 【設計思想】
        // 通常のモーフ形式：相対移動量を保存（ベース位置 + オフセット）
        // 本システム：絶対位置を保存（編集しやすく、紛失しにくい）
        // 
        // メッシュ頂点（MeshObject.Vertices）: モーフ**適用後**の位置
        // MorphBaseData: モーフ**適用前**の基準位置
        // 
        // エクスポート時に差分を計算して相対移動量として出力
        // ----------------------------------------------------------------

        /// <summary>
        /// モーフ基準データ（モーフ前の位置を保持）
        /// nullの場合、このメッシュはモーフではない
        /// </summary>
        public MorphBaseData MorphBaseData { get; set; }

        /// <summary>
        /// モーフメッシュかどうか
        /// MorphBaseDataが有効な場合true
        /// </summary>
        public bool IsMorph => MorphBaseData != null && MorphBaseData.IsValid;

        /// <summary>
        /// モーフ名（MorphBaseDataから取得、後方互換）
        /// </summary>
        public string MorphName
        {
            get => MorphBaseData?.MorphName ?? "";
            set
            {
                if (MorphBaseData != null)
                    MorphBaseData.MorphName = value;
            }
        }

        /// <summary>
        /// モーフパネル（PMX: 0=眉, 1=目, 2=口, 3=その他）
        /// </summary>
        public int MorphPanel
        {
            get => MorphBaseData?.Panel ?? 3;
            set
            {
                if (MorphBaseData != null)
                    MorphBaseData.Panel = value;
            }
        }

        /// <summary>
        /// モーフ親メッシュのマスターインデックス
        /// このモーフが適用されるベースメッシュを明示的に指定
        /// -1 = 未指定（名前規則ベースで検索）
        /// </summary>
        public int MorphParentIndex { get; set; } = -1;

        // ================================================================
        // モーフのミラー適用（規約は MorphMirrorPolicy.cs を正典とする）
        // ================================================================

        /// <summary>
        /// モーフのミラー適用ポリシー（既定 FollowParent＝親のミラー設定に従う）。
        /// モーフでない MeshContext では意味を持たない。
        /// 規約は MorphMirrorPolicy.cs 冒頭のコメントを正典とする。
        /// </summary>
        public MorphMirrorPolicy MorphMirrorPolicy { get; set; } = MorphMirrorPolicy.FollowParent;

        /// <summary>
        /// MorphMirrorPolicy == MirrorOf のときの参照先モーフ MeshContext のマスターインデックス。
        /// -1 = 未指定。MorphParentIndex と同じく、メッシュリストの増減で再マップされる索引参照。
        /// </summary>
        public int MirrorOfMorphIndex { get; set; } = -1;

        /// <summary>
        /// モーフに関わるメッシュか。
        /// true のとき法線再計算でUVスロットを増やしてはならない。
        /// 親子で UVs.Count が食い違うと展開index空間がずれ、
        /// PMXエクスポート時のモーフ頂点参照が崩れるため。
        /// </summary>
        public bool IsMorphRelated(Poly_Ling.Context.ModelContext model)
        {
            if (IsMorph) return true;
            if (Type == MeshType.Morph) return true;
            if (model == null) return false;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || ReferenceEquals(mc, this)) continue;
                if (mc.MorphParentIndex < 0) continue;
                if (ReferenceEquals(model.GetMeshContext(mc.MorphParentIndex), this)) return true;
            }
            return false;
        }

        /// <summary>
        /// モーフ基準データを設定（現在のメッシュ状態を基準として保存）
        /// </summary>
        /// <param name="morphName">モーフ名</param>
        public void SetAsMorph(string morphName, MeshObject baseMeshObject = null)
        {
            if (MeshObject == null || MeshObject.VertexCount == 0)
                return;

            // baseMeshObjectが指定された場合、親メッシュの位置をBasePositionsに使用
            // PMXインポータではbaseMeshObject=null（呼び出し時点でMeshObjectが親のクローン）
            MorphBaseData = MorphBaseData.FromMeshObject(baseMeshObject ?? MeshObject, morphName);
        }

        /// <summary>
        /// モーフ基準データをクリア（通常メッシュに戻す）
        /// </summary>
        public void ClearMorphData()
        {
            MorphBaseData = null;
            MorphParentIndex = -1;
            MorphMirrorPolicy = MorphMirrorPolicy.FollowParent;
            MirrorOfMorphIndex = -1;
        }

        /// <summary>
        /// モーフをリセット（基準位置に戻す）
        /// </summary>
        public void ResetToMorphBase()
        {
            if (!IsMorph || MeshObject == null)
                return;

            MorphBaseData.ApplyBaseToMeshObject(MeshObject);
        }

        /// <summary>
        /// モーフ差分を取得（エクスポート用）
        /// </summary>
        /// <returns>変化のある頂点とその差分のリスト</returns>
        public List<(int VertexIndex, Vector3 Offset)> GetMorphOffsets(float threshold = 0f)
        {
            if (!IsMorph || MeshObject == null)
                return new List<(int, Vector3)>();

            return MorphBaseData.GetSparseOffsets(MeshObject, threshold);
        }

        /// <summary>
        /// UVモーフ差分を取得（エクスポート用）
        /// </summary>
        /// <returns>変化のあるUVとその差分のリスト</returns>
        public List<(int VertexIndex, Vector2 Offset)> GetUVMorphOffsets(float threshold = 0f)
        {
            if (!IsMorph || MeshObject == null || MorphBaseData == null)
                return new List<(int, Vector2)>();

            return MorphBaseData.GetSparseUVOffsets(MeshObject, threshold);
        }

        // ================================================================
        // エクスポート制御フラグ（Phase: Morph対応）
        // ================================================================

        /// <summary>
        /// モデルエクスポート時にこのメッシュを除外するか
        /// true: エクスポートしない（作業用メッシュ、モーフ専用メッシュ等）
        /// false: 通常通りエクスポート（デフォルト）
        /// </summary>
        public bool ExcludeFromExport { get; set; } = false;

        /// <summary>PMX材質名リスト（PMXインポート時に設定、空メッシュエクスポート用）</summary>
        public List<string> PMXMaterialNames { get; set; } = new List<string>();

        // ================================================================
        // ミラー設定（MQOからインポート）
        // ================================================================

        /// <summary>ミラータイプ (0:なし, 1:分離, 2:結合)</summary>
        public int MirrorType { get; set; } = 0;

        /// <summary>ミラー軸 (1:X, 2:Y, 4:Z)</summary>
        public int MirrorAxis { get; set; } = 1;

        /// <summary>ミラー距離</summary>
        public float MirrorDistance { get; set; } = 0f;

        /// <summary>
        /// ミラー側マテリアルのオフセット
        /// ミラー側マテリアルインデックス = 実体側インデックス + MirrorMaterialOffset
        /// </summary>
        public int MirrorMaterialOffset { get; set; } = 0;

        /// <summary>ミラーが有効か</summary>
        public bool IsMirrored => MirrorType > 0;

        /// <summary>
        /// 親メッシュの表示ミラー設定をこの MeshContext へ継承する。
        ///
        /// モーフは親のミラー機構に乗る（規約は MorphMirrorPolicy.cs を正典とする）。
        /// ①表示ミラーはミラー側の実体を持たず、元頂点 i をミラー行列で写して描くだけなので、
        /// モーフ側にも同じ設定を持たせるだけで連動する。オブジェクト数も頂点数も増えない。
        ///
        /// 継承するのは表示ミラーの4値のみ。ベイクミラーの関係
        /// （BakedMirrorSourceIndex / HasBakedMirrorChild / MirrorGeometryDerived）は
        /// モーフとメッシュで別の関係なので写さない。
        /// </summary>
        public void InheritMirrorSettingsFrom(MeshContext parent)
        {
            if (parent == null) return;

            MirrorType           = parent.MirrorType;
            MirrorAxis           = parent.MirrorAxis;
            MirrorDistance       = parent.MirrorDistance;
            MirrorMaterialOffset = parent.MirrorMaterialOffset;

            InvalidateSymmetryCache();
        }

        /// <summary>ミラー軸をSymmetryAxisに変換</summary>
        public Poly_Ling.Symmetry.SymmetryAxis GetMirrorSymmetryAxis()
        {
            switch (MirrorAxis)
            {
                case 1: return Poly_Ling.Symmetry.SymmetryAxis.X;
                case 2: return Poly_Ling.Symmetry.SymmetryAxis.Y;
                case 4: return Poly_Ling.Symmetry.SymmetryAxis.Z;
                default: return Poly_Ling.Symmetry.SymmetryAxis.X;
            }
        }


        // ================================================================
        // ミラーメッシュキャッシュ
        // ================================================================

        /// <summary>ミラー表示用メッシュキャッシュ（遅延初期化）</summary>
        private SymmetryMeshCache _symmetryCache;

        /// <summary>ミラーメッシュキャッシュを取得（遅延初期化）</summary>
        public SymmetryMeshCache SymmetryCache
        {
            get
            {
                if (_symmetryCache == null)
                    _symmetryCache = new SymmetryMeshCache();
                return _symmetryCache;
            }
        }

        /// <summary>ミラーキャッシュを無効化（トポロジー変更時に呼ぶ）</summary>
        public void InvalidateSymmetryCache()
        {
            _symmetryCache?.Invalidate();
        }

        /// <summary>ミラーキャッシュをクリア（リソース解放）</summary>
        public void ClearSymmetryCache()
        {
            _symmetryCache?.Clear();
            _symmetryCache = null;
        }

        // ================================================================
        // ベイクミラー（実体化されたミラーメッシュ）
        // ================================================================

        /// <summary>
        /// ベイク元メッシュのインデックス
        /// -1 = ベイクされたミラーではない（通常メッシュまたはミラー属性を持つメッシュ）
        /// 0以上 = このメッシュはベイクされたミラーで、指定インデックスのメッシュが元
        /// </summary>
        public int BakedMirrorSourceIndex { get; set; } = -1;

        /// <summary>ベイクされたミラーメッシュかどうか</summary>
        public bool IsBakedMirror => BakedMirrorSourceIndex >= 0;

        /// <summary>
        /// ベイクミラーの元メッシュかどうか
        /// （MirrorType > 0 かつ BakedMirrorSourceIndex == -1）
        /// </summary>
        public bool HasBakedMirrorChild { get; set; } = false;

        /// <summary>Memo欄のIsMirrorフラグ由来のミラーか（PMXインポート時に設定）</summary>
        public bool IsMirrorFromMemo { get; set; } = false;

        public MeshContext()
        {
            BoneTransform = new BoneTransform();
        }

        // ================================================================
        // マテリアル（ModelContext への委譲）
        // ================================================================
        // マテリアルはModelContextで一元管理
        // MeshContextはMaterialOwner経由でアクセス

        /// <summary>親ModelContextへの参照（マテリアル取得用）- 必須</summary>
        public Poly_Ling.Context.ModelContext ParentModelContext { get; set; }

        /// <summary>マテリアルリスト（ModelContextに委譲）</summary>
        public List<Material> Materials
        {
            get
            {
                if (ParentModelContext == null)
                {
                    Debug.LogError("[MeshContext] MaterialOwnerが設定されていません。ModelContext.Add()で追加してください。");
                    return new List<Material> { null };
                }
                return ParentModelContext.Materials;
            }
            set
            {
                if (ParentModelContext == null)
                {
                    Debug.LogError("[MeshContext] MaterialOwnerが設定されていません。");
                    return;
                }
                ParentModelContext.Materials = value;
            }
        }

        /// <summary>現在選択中のマテリアルインデックス（ModelContextに委譲）</summary>
        public int CurrentMaterialIndex
        {
            get
            {
                if (ParentModelContext == null) return 0;
                return ParentModelContext.CurrentMaterialIndex;
            }
            set
            {
                if (ParentModelContext == null)
                {
                    Debug.LogError("[MeshContext] MaterialOwnerが設定されていません。");
                    return;
                }
                ParentModelContext.CurrentMaterialIndex = value;
            }
        }

        /// <summary>サブメッシュ数</summary>
        public int SubMeshCount => ParentModelContext?.Materials?.Count ?? 1;

        /// <summary>現在選択中のマテリアルを取得</summary>
        public Material GetCurrentMaterial()
        {
            if (ParentModelContext == null) return null;
            var mats = ParentModelContext.Materials;
            int idx = ParentModelContext.CurrentMaterialIndex;
            if (idx >= 0 && idx < mats.Count)
                return mats[idx];
            return null;
        }

        /// <summary>指定スロットのマテリアルを取得</summary>
        public Material GetMaterial(int index)
        {
            if (ParentModelContext == null) return null;
            var mats = ParentModelContext.Materials;
            if (index >= 0 && index < mats.Count)
                return mats[index];
            return null;
        }

        // ================================================================
        // メッシュ操作メソッド（UndoRecord から直接呼び出される）
        // ================================================================

        /// <summary>頂点数</summary>
        public int VertexCount => MeshObject?.VertexCount ?? 0;

        /// <summary>面数</summary>
        public int FaceCount => MeshObject?.FaceCount ?? 0;

        /// <summary>頂点位置を取得</summary>
        public Vector3 GetVertexPosition(int index)
        {
            if (MeshObject == null || index < 0 || index >= MeshObject.VertexCount)
                return Vector3.zero;
            return MeshObject.Vertices[index].Position;
        }

        /// <summary>頂点位置を設定</summary>
        public void SetVertexPosition(int index, Vector3 position)
        {
            if (MeshObject == null || index < 0 || index >= MeshObject.VertexCount)
                return;
            MeshObject.Vertices[index].Position = position;
            MeshObject.InvalidatePositionCache();
        }

        /// <summary>全頂点位置を配列で取得（Clone）</summary>
        public Vector3[] GetAllPositions()
        {
            if (MeshObject == null) return new Vector3[0];
            return (Vector3[])MeshObject.Positions.Clone();
        }

        /// <summary>全頂点位置を配列で設定</summary>
        public void SetAllPositions(Vector3[] positions)
        {
            if (MeshObject == null) return;
            MeshObject.SetPositions(positions);
        }

        /// <summary>
        /// MeshObjectの全データをUnityMeshに適用
        /// </summary>
        public void ApplyToMesh()
        {
            if (UnityMesh == null || MeshObject == null) return;

            // 生Unity Mesh 操作は MeshBridge に集約（一時Mesh生成・コピー・破棄を内包）。
            PLMeshBridge.I.RebuildMeshInPlace(UnityMesh, MeshObject);
        }

        /// <summary>
        /// 頂点位置のみをUnityMeshに適用（高速）
        /// </summary>
        public void ApplyVertexPositionsToMesh()
        {
            if (UnityMesh == null || MeshObject == null) return;

            PLMeshBridge.I.ApplyVertexPositionsInPlace(UnityMesh, MeshObject);
        }
    }
}
