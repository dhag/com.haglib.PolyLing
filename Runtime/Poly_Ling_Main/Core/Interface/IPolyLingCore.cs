// IPolyLingCore.cs
// PolyLingの中核ロジックを外部から扱うインターフェース
// EditorWindow側とRemoteClient側が共通で参照する
// 実装クラスはPolyLingCore

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Core
{
    public interface IPolyLingCore
    {
        // ================================================================
        // 読み取り専用プロパティ
        // ================================================================

        ProjectContext  Project           { get; }
        ModelContext    Model             { get; }
        ToolContext     CurrentToolContext { get; }
        PanelContext    PanelContext       { get; }
        SelectionState  SelectionState    { get; }
        LiveProjectView LiveProjectView   { get; }
        ToolManager     ToolManager       { get; }

        // Editor/Renderレイヤーが直接参照する内部システム
        MeshUndoController UndoController { get; }
        CommandQueue       CommandQueue   { get; }
        SelectionOperations SelectionOps  { get; }
        TopologyCache       MeshTopology  { get; }

        // ================================================================
        // 操作
        // ================================================================

        void DispatchPanelCommand(PanelCommand cmd);
        void NotifyPanels(ChangeKind kind = ChangeKind.ListStructure);
        ModelContext CreateNewModel(string name);

        /// <summary>
        /// EditorApplication.update から毎フレーム呼ぶ
        /// CommandQueue の処理とUndo後処理を担う
        /// </summary>
        void Tick();

        // ================================================================
        // イベント（EditorはこれをRepaint()に接続する）
        // ================================================================

        event Action OnRepaintRequired;
        event Action OnMeshListChanged;
        event Action OnCurrentModelChanged;
        event Action<Vector3>       OnFocusCameraRequested;
        event Action                OnUndoRedoPerformed_Ext;

        /// <summary>
        /// メッシュ切り替え時に新しいSelectionStateを通知する。
        /// Editorはこれを受けて _unifiedAdapter.SetSelectionState() を呼ぶ。
        /// </summary>
        event Action<SelectionState> OnSelectionStateChanged;
    }
}
