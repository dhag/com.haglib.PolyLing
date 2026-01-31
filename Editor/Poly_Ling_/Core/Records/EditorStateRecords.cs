// Assets/Editor/UndoSystem/MeshEditor/Records/EditorStateRecords.cs
// エディタ状態（カメラ、表示設定、編集モード）のUndo記録

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;

namespace Poly_Ling.UndoSystem
{
    // ============================================================
    // エディタ状態コンテキスト
    // ============================================================

    /// <summary>
    /// エディタ状態コンテキスト（カメラ、表示設定、編集モード）
    /// Single Source of Truth: PolyLing.csはこのクラスを唯一のデータソースとして参照する
    /// </summary>
    public class EditorStateContext
    {
        // カメラ
        public float RotationX = 20f;
        public float RotationY = 0f;
        public float CameraDistance = 40f;
        public Vector3 CameraTarget = Vector3.zero;

        // 表示設定
        public bool ShowWireframe = true;
        public bool ShowVertices = true;
        public bool ShowMesh = true;                    // メッシュ本体表示
        public bool ShowSelectedMeshOnly = false;       // 選択メッシュのみ表示
        public bool ShowVertexIndices = true;
        public bool ShowUnselectedWireframe = true;     // 非選択メッシュのワイヤフレーム
        public bool ShowUnselectedVertices = true;      // 非選択メッシュの頂点
        public bool BackfaceCullingEnabled = true;
        public bool ShowBones = false;                  // ボーン表示
        public bool ShowWorkPlaneGizmo = true;          // WorkPlaneギズモ表示

        // 編集モード
        public bool VertexEditMode = true;              // 頂点編集モード

        // 現在のツール
        public string CurrentToolName = "Select";

        // メッシュ作成設定
        public bool AddToCurrentMesh = true;
        public bool AutoMergeOnCreate = true;
        public float AutoMergeThreshold = 0.001f;

        // エクスポート設定
        public bool ExportSelectedMeshOnly = false;
        public bool BakeMirror = false;
        public bool MirrorFlipU = true;
        public bool ExportAsSkinned = false;            // スキンメッシュとしてエクスポート
        public bool BakeBlendShapes = false;            // モーフをBlendShapeとして焼き込む

        // トランスフォーム表示モード
        public bool ShowLocalTransform = false;   // 自身のBoneTransformを反映
        public bool ShowWorldTransform = true;    // 親を遡ったBoneTransformを累積適用（デフォルトON）

        // カメラ設定
        public bool AutoZoomEnabled = false;      // メッシュ選択時に自動ズーム（デフォルトOFF）

        // 汎用ツール設定ストレージ
        public ToolSettingsStorage ToolSettings = new ToolSettingsStorage();

        // WorkPlane参照（カメラ連動Undo用）
        public WorkPlaneContext WorkPlane;

        // === Foldout状態 ===
        /// <summary>Foldout開閉をUndo記録するか</summary>
        public bool RecordFoldoutChanges = false;
        
        /// <summary>Foldout状態辞書</summary>
        public Dictionary<string, bool> FoldoutStates = new Dictionary<string, bool>();
        
        /// <summary>Foldout状態を取得（未設定ならデフォルト値を返す）</summary>
        public bool GetFoldout(string key, bool defaultValue = true)
        {
            return FoldoutStates.TryGetValue(key, out var value) ? value : defaultValue;
        }
        
        /// <summary>Foldout状態を設定</summary>
        public void SetFoldout(string key, bool value)
        {
            FoldoutStates[key] = value;
        }

        /// <summary>
        /// スナップショットを作成
        /// </summary>
        public EditorStateSnapshot Capture()
        {
            EditorStateSnapshot editorStatesnapshot = new EditorStateSnapshot
            {
                RotationX = RotationX,
                RotationY = RotationY,
                CameraDistance = CameraDistance,
                CameraTarget = CameraTarget,
                ShowWireframe = ShowWireframe,
                ShowVertices = ShowVertices,
                ShowMesh = ShowMesh,
                ShowSelectedMeshOnly = ShowSelectedMeshOnly,
                ShowVertexIndices = ShowVertexIndices,
                ShowUnselectedWireframe = ShowUnselectedWireframe,
                ShowUnselectedVertices = ShowUnselectedVertices,
                BackfaceCullingEnabled = BackfaceCullingEnabled,
                ShowBones = ShowBones,
                ShowWorkPlaneGizmo = ShowWorkPlaneGizmo,
                VertexEditMode = VertexEditMode,
                CurrentToolName = CurrentToolName,
                AddToCurrentMesh = AddToCurrentMesh,
                AutoMergeOnCreate = AutoMergeOnCreate,
                AutoMergeThreshold = AutoMergeThreshold,
                ExportSelectedMeshOnly = ExportSelectedMeshOnly,
                BakeMirror = BakeMirror,
                MirrorFlipU = MirrorFlipU,
                ExportAsSkinned = ExportAsSkinned,
                BakeBlendShapes = BakeBlendShapes,
                ShowLocalTransform = ShowLocalTransform,
                ShowWorldTransform = ShowWorldTransform,
                AutoZoomEnabled = AutoZoomEnabled,
                RecordFoldoutChanges = RecordFoldoutChanges,
                FoldoutStates = new Dictionary<string, bool>(FoldoutStates),
            };

            // ToolSettings（新規）
            editorStatesnapshot.ToolSettings = ToolSettings?.Clone();

            return editorStatesnapshot;
        }

        /// <summary>
        /// スナップショットから復元
        /// </summary>
        public void ApplySnapshot(EditorStateSnapshot snapshot)
        {
            RotationX = snapshot.RotationX;
            RotationY = snapshot.RotationY;
            CameraDistance = snapshot.CameraDistance;
            CameraTarget = snapshot.CameraTarget;
            ShowWireframe = snapshot.ShowWireframe;
            ShowVertices = snapshot.ShowVertices;
            ShowMesh = snapshot.ShowMesh;
            ShowSelectedMeshOnly = snapshot.ShowSelectedMeshOnly;
            ShowVertexIndices = snapshot.ShowVertexIndices;
            ShowUnselectedWireframe = snapshot.ShowUnselectedWireframe;
            ShowUnselectedVertices = snapshot.ShowUnselectedVertices;
            BackfaceCullingEnabled = snapshot.BackfaceCullingEnabled;
            ShowBones = snapshot.ShowBones;
            ShowWorkPlaneGizmo = snapshot.ShowWorkPlaneGizmo;
            VertexEditMode = snapshot.VertexEditMode;
            CurrentToolName = snapshot.CurrentToolName;
            AddToCurrentMesh = snapshot.AddToCurrentMesh;
            AutoMergeOnCreate = snapshot.AutoMergeOnCreate;
            AutoMergeThreshold = snapshot.AutoMergeThreshold;
            ExportSelectedMeshOnly = snapshot.ExportSelectedMeshOnly;
            BakeMirror = snapshot.BakeMirror;
            MirrorFlipU = snapshot.MirrorFlipU;
            ExportAsSkinned = snapshot.ExportAsSkinned;
            BakeBlendShapes = snapshot.BakeBlendShapes;
            ShowLocalTransform = snapshot.ShowLocalTransform;
            ShowWorldTransform = snapshot.ShowWorldTransform;
            AutoZoomEnabled = snapshot.AutoZoomEnabled;
            RecordFoldoutChanges = snapshot.RecordFoldoutChanges;
            
            // FoldoutStates復元
            FoldoutStates.Clear();
            if (snapshot.FoldoutStates != null)
            {
                foreach (var kvp in snapshot.FoldoutStates)
                {
                    FoldoutStates[kvp.Key] = kvp.Value;
                }
            }

            // ToolSettings（新規）
            if (snapshot.ToolSettings != null)
            {
                if (ToolSettings == null)
                    ToolSettings = new ToolSettingsStorage();
                ToolSettings.CopyFrom(snapshot.ToolSettings);
            }
        }
    }

    // ============================================================
    // エディタ状態スナップショット
    // ============================================================

    /// <summary>
    /// エディタ状態のスナップショット
    /// </summary>
    public struct EditorStateSnapshot
    {
        public float RotationX, RotationY, CameraDistance;
        public Vector3 CameraTarget;
        public bool ShowWireframe, ShowVertices, ShowMesh, VertexEditMode;
        public bool ShowSelectedMeshOnly, ShowVertexIndices;
        public bool ShowUnselectedWireframe, ShowUnselectedVertices;
        public bool BackfaceCullingEnabled;
        public bool ShowBones;
        public bool ShowWorkPlaneGizmo;
        public string CurrentToolName;

        // メッシュ作成設定
        public bool AddToCurrentMesh;
        public bool AutoMergeOnCreate;
        public float AutoMergeThreshold;

        // エクスポート設定
        public bool ExportSelectedMeshOnly;
        public bool BakeMirror;
        public bool MirrorFlipU;
        public bool ExportAsSkinned;
        public bool BakeBlendShapes;

        // トランスフォーム表示モード
        public bool ShowLocalTransform;
        public bool ShowWorldTransform;

        // カメラ設定
        public bool AutoZoomEnabled;

        // 汎用ツール設定
        public ToolSettingsStorage ToolSettings;
        
        // Foldout状態
        public bool RecordFoldoutChanges;
        public Dictionary<string, bool> FoldoutStates;

        public bool IsDifferentFrom(EditorStateSnapshot other)
        {
            // 基本プロパティ
            if (!Mathf.Approximately(RotationX, other.RotationX) ||
                !Mathf.Approximately(RotationY, other.RotationY) ||
                !Mathf.Approximately(CameraDistance, other.CameraDistance) ||
                Vector3.Distance(CameraTarget, other.CameraTarget) > 0.0001f ||
                ShowWireframe != other.ShowWireframe ||
                ShowVertices != other.ShowVertices ||
                ShowMesh != other.ShowMesh ||
                ShowSelectedMeshOnly != other.ShowSelectedMeshOnly ||
                ShowVertexIndices != other.ShowVertexIndices ||
                ShowUnselectedWireframe != other.ShowUnselectedWireframe ||
                ShowUnselectedVertices != other.ShowUnselectedVertices ||
                BackfaceCullingEnabled != other.BackfaceCullingEnabled ||
                ShowBones != other.ShowBones ||
                ShowWorkPlaneGizmo != other.ShowWorkPlaneGizmo ||
                VertexEditMode != other.VertexEditMode ||
                CurrentToolName != other.CurrentToolName ||
                AddToCurrentMesh != other.AddToCurrentMesh ||
                AutoMergeOnCreate != other.AutoMergeOnCreate ||
                !Mathf.Approximately(AutoMergeThreshold, other.AutoMergeThreshold) ||
                ExportSelectedMeshOnly != other.ExportSelectedMeshOnly ||
                BakeMirror != other.BakeMirror ||
                MirrorFlipU != other.MirrorFlipU ||
                ExportAsSkinned != other.ExportAsSkinned ||
                BakeBlendShapes != other.BakeBlendShapes ||
                ShowLocalTransform != other.ShowLocalTransform ||
                ShowWorldTransform != other.ShowWorldTransform ||
                AutoZoomEnabled != other.AutoZoomEnabled ||
                RecordFoldoutChanges != other.RecordFoldoutChanges)
            {
                return true;
            }
            
            // Foldout状態の比較
            if (!AreFoldoutStatesEqual(FoldoutStates, other.FoldoutStates))
            {
                return true;
            }

            // ToolSettings（新規）
            if (ToolSettings != null && other.ToolSettings != null)
            {
                if (ToolSettings.IsDifferentFrom(other.ToolSettings))
                    return true;
            }
            else if ((ToolSettings != null && ToolSettings.Count > 0) ||
                     (other.ToolSettings != null && other.ToolSettings.Count > 0))
            {
                // 片方だけ設定がある場合は差異あり
                return true;
            }

            return false;
        }
        
        private static bool AreFoldoutStatesEqual(Dictionary<string, bool> a, Dictionary<string, bool> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            
            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var bValue) || kvp.Value != bValue)
                    return false;
            }
            return true;
        }
    }

    // ============================================================
    // エディタ状態変更記録
    // ============================================================

    /// <summary>
    /// エディタ状態変更記録（WorkPlane連動対応）
    /// </summary>
    public class EditorStateChangeRecord : IUndoRecord<EditorStateContext>
    {
        public UndoOperationInfo Info { get; set; }
        public EditorStateSnapshot Before;
        public EditorStateSnapshot After;

        // WorkPlane連動（カメラ変更時の軸更新用）
        public WorkPlaneSnapshot? OldWorkPlaneSnapshot;
        public WorkPlaneSnapshot? NewWorkPlaneSnapshot;

        public EditorStateChangeRecord(EditorStateSnapshot before, EditorStateSnapshot after)
        {
            Before = before;
            After = after;
            OldWorkPlaneSnapshot = null;
            NewWorkPlaneSnapshot = null;
        }

        /// <summary>
        /// WorkPlane連動付きコンストラクタ
        /// </summary>
        public EditorStateChangeRecord(
            EditorStateSnapshot before,
            EditorStateSnapshot after,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane)
            : this(before, after)
        {
            OldWorkPlaneSnapshot = oldWorkPlane;
            NewWorkPlaneSnapshot = newWorkPlane;
        }

        public void Undo(EditorStateContext ctx)
        {
            ctx.ApplySnapshot(Before);

            // WorkPlane連動復元
            if (OldWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(OldWorkPlaneSnapshot.Value);
            }
        }

        public void Redo(EditorStateContext ctx)
        {
            ctx.ApplySnapshot(After);

            // WorkPlane連動復元
            if (NewWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(NewWorkPlaneSnapshot.Value);
            }
        }
    }

    // ============================================================
    // 後方互換用
    // ============================================================

    /// <summary>
    /// 表示設定コンテキスト（後方互換用エイリアス）
    /// </summary>
    public class ViewContext : EditorStateContext { }

    /// <summary>
    /// 表示設定変更記録（後方互換用）
    /// </summary>
    public class ViewChangeRecord : IUndoRecord<EditorStateContext>
    {
        public UndoOperationInfo Info { get; set; }

        public float OldRotationX, NewRotationX;
        public float OldRotationY, NewRotationY;
        public float OldCameraDistance, NewCameraDistance;
        public Vector3 OldCameraTarget, NewCameraTarget;

        public void Undo(EditorStateContext ctx)
        {
            ctx.RotationX = OldRotationX;
            ctx.RotationY = OldRotationY;
            ctx.CameraDistance = OldCameraDistance;
            ctx.CameraTarget = OldCameraTarget;
        }

        public void Redo(EditorStateContext ctx)
        {
            ctx.RotationX = NewRotationX;
            ctx.RotationY = NewRotationY;
            ctx.CameraDistance = NewCameraDistance;
            ctx.CameraTarget = NewCameraTarget;
        }
    }
}
