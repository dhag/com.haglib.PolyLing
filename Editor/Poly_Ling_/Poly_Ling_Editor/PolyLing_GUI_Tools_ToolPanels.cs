// Assets/Editor/PolyLing.GUI.Tools.cs
// ToolManager統合版
// Phase 1: UI描画をToolManagerと連携
// ToolButtonLayoutを削除し、登録順で2列自動レイアウト
// Phase 4: ToolPanel対応

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Tools.Panels;
using Poly_Ling.Selection;
using Poly_Ling.Localization;


public partial class PolyLing
{


    /// <summary>
    /// ToolPanelsセクションを描画
    /// </summary>
    private void DrawToolPanelsSection()
    {
        _foldToolPanel = DrawFoldoutWithUndo("ToolPanels", L.Get("ToolPanels"), true);  // デフォルト開く
        if (!_foldToolPanel)
            return;

        EditorGUI.indentLevel++;

        // UnityMesh List Window（統合版）
        if (GUILayout.Button(L.Get("Window_MeshContextList")))
        {
            MeshListPanel.Open(_toolManager?.toolContext);
        }

        // Simple Morph
        if (GUILayout.Button(L.Get("Window_SimpleMorph")))
        {
            SimpleMorphPanel.Open(_toolManager?.toolContext);
        }

        // === Import/Export ===
        //EditorGUILayout.Space(5);
        //EditorGUILayout.LabelField("Import / Export", EditorStyles.miniLabel);

        if (GUILayout.Button("MQO Import..."))
        {
            Poly_Ling.MQO.MQOImportPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("PMX Import..."))
        {
            Poly_Ling.PMX.PMXImportPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("PMX Bone Weight Export..."))
        {
            Poly_Ling.PMX.PMXBoneWeightExportPanel.ShowWindow();
        }
        if (GUILayout.Button("PMX ←→ MQO"))
        {
            Poly_Ling.PMX.PMXMQOTransferPanel.ShowWindow();
        }
        if (GUILayout.Button("Avatar Creator..."))
        {
            Poly_Ling.MISC.AvatarCreatorPanel.ShowWindow();
        }

        EditorGUI.BeginDisabledGroup(!_model.HasValidMeshContextSelection);
        if (GUILayout.Button("MQO Export"))
        {
            Poly_Ling.MQO.MQOExportPanel.Open(_toolManager?.toolContext);
        }
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("PMX Export"))
        {
            Poly_Ling.PMX.PMXExportPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("Selected VertexTools"))
        {
            Poly_Ling.Tools.Panels.VertexToolsPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("MeshListPanelUXML"))
        {
            Poly_Ling.UI.MeshListPanelUXML.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("TypedMeshListPanelUXML"))
        {
            Poly_Ling.UI.TypedMeshListPanel.Open(_toolManager?.toolContext);
        }
        if(GUILayout.Button("モデル選択"))
        {
            Poly_Ling.Tools.Panels.ModelListPanel.Open(_toolManager?.toolContext);
        }
        if(GUILayout.Button("アバターマッピング辞書インポート"))
        {
            Poly_Ling.Tools.Panels.HumanoidMappingPanel.Open(_toolManager?.toolContext);
        }
        EditorGUI.indentLevel--;
    }

}