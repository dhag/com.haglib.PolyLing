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
using Poly_Ling.Remote;


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

        // ToolContextにUndoControllerが未設定の場合、ここで設定
        // （UpdateToolContextはDrawPreview内で呼ばれるため、左ペインでは未設定の場合がある）
        if (_toolManager?.toolContext != null && _toolManager.toolContext.UndoController == null)
            _toolManager.toolContext.UndoController = _undoController;

        EditorGUI.indentLevel++;


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

        if (GUILayout.Button("VMD簡易テスト"))
        {
            Poly_Ling.VMD.VMDTestPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("Avatar Creator..."))
        {
            Poly_Ling.MISC.AvatarCreatorPanel.ShowWindow();
        }


        if (GUILayout.Button("mesh <part>→ PMX"))
        {
        Poly_Ling.PMX.PMXPartialExportPanel.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("mesh <part>→ MQO"))
        {
        Poly_Ling.MQO.MQOPartialExportPanel.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("MQO <part>→ mesh"))
        {
        Poly_Ling.MQO.MQOPartialImportPanel.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("PMX <part>→ mesh"))
        {
        Poly_Ling.PMX.PMXPartialImportPanel.Open(_toolManager?.toolContext);
        }

        //EditorGUI.BeginDisabledGroup(!_model.HasValidMeshContextSelection);
        if (GUILayout.Button("MQO Export"))
        {
            Poly_Ling.MQO.MQOExportPanel.Open(_toolManager?.toolContext);
        }
        //EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("PMX Export"))
        {
            Poly_Ling.PMX.PMXExportPanel.Open(_toolManager?.toolContext);
        }


        if (GUILayout.Button("メッシュリストTypedMeshListPanelUXML"))
        {
            Poly_Ling.UI.TypedMeshListPanel.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("材質リスト"))
        {
            Poly_Ling.UI.MaterialListPanel.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("UV展開"))
        {
            Poly_Ling.UI.UVUnwrapPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("UV編集"))
        {
            Poly_Ling.UI.UVEditPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("UVZ"))
        {
            Poly_Ling.UI.UVZPanel.Open(_toolManager?.toolContext);
        }


        if (GUILayout.Button("選択頂点の編集"))
        {
            Poly_Ling.Tools.Panels.VertexToolsPanel.Open(_toolManager?.toolContext);
        }


        if (GUILayout.Button("モデル選択"))
        {
            Poly_Ling.Tools.Panels.ModelListPanel.Open(_toolManager?.toolContext);
        }        
        
        if(GUILayout.Button("モーフ確認"))
        {
            Poly_Ling.Tools.Panels.MorphPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("ボーンエディタ"))
        {
            Poly_Ling.UI.BoneEditorPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("スキンウェイトペイント"))
        {
            Poly_Ling.UI.SkinWeightPaintPanel.Open(_toolManager?.toolContext, _toolManager);
        }

        if (GUILayout.Button("MeshFilter → Skinned"))
        {
            Poly_Ling.Tools.Panels.MeshFilterToSkinnedPanel.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("アバターマッピング辞書インポート"))
        {
            Poly_Ling.Tools.Panels.HumanoidMappingPanel.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("Tpose"))
        {
            Poly_Ling.Tools.Panels.TPosePanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("モデルブレンド"))
        {
            Poly_Ling.Tools.Panels.ModelBlendPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("リモートサーバ"))
        {
            RemoteServer.Open(_toolManager?.toolContext);
        }
        




        if (GUILayout.Button("__old_MeshListPanelUXML"))
        {
            Poly_Ling.UI.MeshListPanelUXML.Open(_toolManager?.toolContext);
        }
        // UnityMesh List Window（統合版）
        if (GUILayout.Button("__old_Window_MeshContextList"))
        {
            MeshListPanel.Open(_toolManager?.toolContext);
        }
        // Simple Morph
        if (GUILayout.Button("__old_SimpleMorph"))
        {
            SimpleMorphPanel.Open(_toolManager?.toolContext);
        }





        if (GUILayout.Button("__old_PMX Bone Weight Export..."))
        {
            Poly_Ling.PMX.PMXBoneWeightExportPanel.ShowWindow();
        }
        if (GUILayout.Button("__old_PMX ←→ MQO"))
        {
            Poly_Ling.PMX.PMXMQOTransferPanel.ShowWindow();
        }
        EditorGUI.indentLevel--;
    }

}