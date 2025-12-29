// Assets/Editor/SimpleMeshFactory.cs
// 階層型Undoシステム統合済みメッシュエディタ
// MeshObject（Vertex/Face）ベース対応版
// DefaultMaterials対応版
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;
using MeshFactory.Transforms;
using MeshFactory.Tools;
using MeshFactory.Serialization;
using MeshFactory.Selection;
using MeshFactory.Model;
using MeshFactory.Localization;
using static MeshFactory.Gizmo.GLGizmoDrawer;
using MeshFactory.Rendering;
using MeshFactory.Symmetry;

namespace MeshFactory.Data
{
    //public partial class SimpleMeshFactory : EditorWindow
    //{

    // ================================================================
    // メッシュコンテキスト
    //   MeshObjectにUnityMeshなどを加えたもの。
    // ================================================================
    public class MeshContext
    {
        public string Name
        {
            get => MeshObject?.Name ?? "Untitled";
            set { if (MeshObject != null) MeshObject.Name = value; }
        }
        public Mesh UnityMesh;                      // Unity UnityMesh（表示用）
        public MeshObject MeshObject;               // メッシュオブジェクト
        public Vector3[] OriginalPositions;         // 元の頂点位置（リセット用）

        // ================================================================
        // 階層・トランスフォーム（MeshObjectへの参照）
        // 階層には２種類ある。
        // メッシュの編集用階層：モデリングアプリと同様の親子関係
        // ゲームオブジェクト階層：ボーン等に利用する親子関係
        // ================================================================

        /// <summary>親メッシュのインデックス（-1=ルート）</summary>
        public int ParentIndex
        {
            get => MeshObject?.ParentIndex ?? -1;
            set { if (MeshObject != null) MeshObject.ParentIndex = value; }
        }

        /// <summary>階層深度（MQO互換）</summary>
        public int Depth
        {
            get => MeshObject?.Depth ?? 0;
            set { if (MeshObject != null) MeshObject.Depth = value; }
        }

        /// <summary>ゲームオブジェクト階層の親（将来用）</summary>
        public int HierarchyParentIndex
        {
            get => MeshObject?.HierarchyParentIndex ?? -1;
            set { if (MeshObject != null) MeshObject.HierarchyParentIndex = value; }
        }

        /// <summary>エクスポート設定</summary>
        public BoneTransform BoneTransform
        {
            get => MeshObject?.BoneTransform;
            set { if (MeshObject != null) MeshObject.BoneTransform = value ?? new BoneTransform(); }
        }

        // ================================================================
        // オブジェクト属性（MQOからインポート）
        // ================================================================

        /// <summary>メッシュの種類</summary>
        public MeshType Type { get; set; } = MeshType.Mesh;

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

        /// <summary>
        /// 親メッシュのインデックス（-1=ルート、親なし）
        /// </summary>
        //public int ParentIndex { get; set; } = -1;

        /// <summary>
        /// 階層深度（0=ルート、1以上=子）
        /// MQO互換用。表示のインデント等に使用。
        /// </summary>
        //public int Depth { get; set; } = 0;

        /// <summary>可視状態</summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>編集禁止（ロック）</summary>
        public bool IsLocked { get; set; } = false;

        // ================================================================
        // ミラー設定（MQOからインポート）
        // ================================================================

        /// <summary>ミラータイプ (0:なし, 1:分離, 2:結合)</summary>
        public int MirrorType { get; set; } = 0;

        /// <summary>ミラー軸 (1:X, 2:Y, 4:Z)</summary>
        public int MirrorAxis { get; set; } = 1;

        /// <summary>ミラー距離</summary>
        public float MirrorDistance { get; set; } = 0f;

        /// <summary>ミラーが有効か</summary>
        public bool IsMirrored => MirrorType > 0;

        /// <summary>ミラー軸をSymmetryAxisに変換</summary>
        public MeshFactory.Symmetry.SymmetryAxis GetMirrorSymmetryAxis()
        {
            switch (MirrorAxis)
            {
                case 1: return MeshFactory.Symmetry.SymmetryAxis.X;
                case 2: return MeshFactory.Symmetry.SymmetryAxis.Y;
                case 4: return MeshFactory.Symmetry.SymmetryAxis.Z;
                default: return MeshFactory.Symmetry.SymmetryAxis.X;
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

        public MeshContext()
        {
            BoneTransform = new BoneTransform();
        }

        // ================================================================
        // マテリアル（後方互換用 - ModelContext への委譲）
        // ================================================================
        // Phase 1: Materials は ModelContext に集約されたが、
        // 外部ファイル（MQOImporter等）との互換性のため委譲プロパティを維持

        /// <summary>親ModelContextへの参照（マテリアル取得用）</summary>
        internal MeshFactory.Model.ModelContext MaterialOwner { get; set; }

        /// <summary>フォールバック用マテリアルリスト（MaterialOwnerがない場合）</summary>
        private List<Material> _fallbackMaterials = new List<Material> { null };
        private int _fallbackMaterialIndex = 0;

        /// <summary>マテリアルリスト（後方互換）</summary>
        public List<Material> Materials
        {
            get => MaterialOwner?.Materials ?? _fallbackMaterials;
            set
            {
                if (MaterialOwner != null)
                    MaterialOwner.Materials = value;
                else
                    _fallbackMaterials = value ?? new List<Material> { null };
            }
        }

        /// <summary>現在選択中のマテリアルインデックス（後方互換）</summary>
        public int CurrentMaterialIndex
        {
            get => MaterialOwner?.CurrentMaterialIndex ?? _fallbackMaterialIndex;
            set
            {
                if (MaterialOwner != null)
                    MaterialOwner.CurrentMaterialIndex = value;
                else
                    _fallbackMaterialIndex = value;
            }
        }

        /// <summary>サブメッシュ数（後方互換）</summary>
        public int SubMeshCount => Materials?.Count ?? 1;

        /// <summary>現在選択中のマテリアルを取得（後方互換）</summary>
        public Material GetCurrentMaterial()
        {
            var mats = Materials;
            int idx = CurrentMaterialIndex;
            if (idx >= 0 && idx < mats.Count)
                return mats[idx];
            return null;
        }

        /// <summary>指定スロットのマテリアルを取得（後方互換）</summary>
        public Material GetMaterial(int index)
        {
            var mats = Materials;
            if (index >= 0 && index < mats.Count)
                return mats[index];
            return null;
        }
    }
    //}
}