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
using Poly_Ling.View;


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

        if (GUILayout.Button("MQO Import...V2"))
        {
            Poly_Ling.MQO.MQOImportPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("PMX Import...V2"))
        {
            Poly_Ling.PMX.PMXImportPanel.Open(_toolManager?.toolContext);
        }


        if (GUILayout.Button("mesh <part>→ PMX V2"))
        {
        Poly_Ling.PMX.PMXPartialExportPanel.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("mesh <part>→ MQO V2"))
        {
        Poly_Ling.MQO.MQOPartialExportPanel.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("MQO <part>→ mesh V2"))
        {
        Poly_Ling.MQO.MQOPartialImportPanel.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("PMX <part>→ mesh V2"))
        {
        Poly_Ling.PMX.PMXPartialImportPanel.Open(_toolManager?.toolContext);
        }

        //EditorGUI.BeginDisabledGroup(!_model.HasValidMeshContextSelection);
        if (GUILayout.Button("MQO Export V2"))
        {
            Poly_Ling.MQO.MQOExportPanel.Open(_toolManager?.toolContext);
        }
        //EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("PMX Export V2"))
        {
            Poly_Ling.PMX.PMXExportPanel.Open(_toolManager?.toolContext);
        }
        EditorGUILayout.Space(5);


        if (GUILayout.Button("メッシュリストV2 (Summary)"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.MeshListV2.MeshListPanelV2.Open(_core?.PanelContext);
        }

        // LiveViewとViewport
        if (GUILayout.Button("LiveView List テスト"))
        {
            if (_core?.LiveProjectView != null)
                Poly_Ling.MeshListV2.LiveViewTestPanel.Open(_core?.LiveProjectView);
        }
        if (GUILayout.Button("Viewport (メッシュのみ)"))
        {
            if (_core?.LiveProjectView != null)
                Poly_Ling.MeshListV2.ViewportTestPanel.Open(_core?.LiveProjectView);
        }
        if (GUILayout.Button("Viewport (フル)"))
        {
            if (_core?.LiveProjectView != null)
            {
                var vp = Poly_Ling.MeshListV2.ViewportPanel.Open(_core?.LiveProjectView, _core?.PanelContext);
                if (vp?.Core != null)
                {
                    vp.Core.OnHandleInput = evt => ProcessInputFromViewport(evt, vp.Core);
                    vp.Core.OnDrawOverlay = evt => DrawOverlayFromViewport(evt, vp.Core);
                }
            }
        }
        EditorGUILayout.Space(5);
        if (GUILayout.Button("材質リスト"))
        {
            Poly_Ling.UI.MaterialListPanel.Open(_toolManager?.toolContext);
        }
        if (GUILayout.Button("UV展開 V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.UVUnwrapPanel.Open(_core?.PanelContext);
        }

        if (GUILayout.Button("UV編集 V2"))
        {
            Poly_Ling.UI.UVEditPanel.Open(_toolManager?.toolContext);
        }

        if (GUILayout.Button("UVZ"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.UVZPanel.Open(_core?.PanelContext);
        }

        EditorGUILayout.Space(5);


        EditorGUILayout.Space(5);
        if (GUILayout.Button("選択頂点の編集 V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.VertexToolsPanelV2.Open(_core?.PanelContext, _toolManager?.toolContext);
        }


        if (GUILayout.Button("パーツ選択辞書 V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.PartsSelectionSetPanelV2.Open(_core?.PanelContext);
        }

        if (GUILayout.Button("モデル選択 V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.ModelListPanelV2.Open(_core?.PanelContext);
        }

        if (GUILayout.Button("メッシュマージ V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.MergeMeshesPanel.Open(_core?.PanelContext);
        }
        EditorGUILayout.Space(5);

                // 簡易ブレンド
        if (GUILayout.Button("簡易ブレンド V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.SimpleBlendPanelV2.Open(_core?.PanelContext, _toolManager?.toolContext);
        }
        if (GUILayout.Button("モーフ確認 V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.MorphPanelV2.Open(_core?.PanelContext, _toolManager?.toolContext);
        }
        if (GUILayout.Button("モデルブレンド V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.MultiModelBlendPanelV2.Open(_core?.PanelContext);
        }
        
        

        if (GUILayout.Button("ボーンエディタ V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.BoneEditorPanelV2.Open(_core?.PanelContext, _toolManager?.toolContext);
        }

        if (GUILayout.Button("スキンウェイトペイント V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.SkinWeightPaintPanelV2.Open(_core?.PanelContext, _toolManager?.toolContext, _toolManager);
        }
        if (GUILayout.Button("MeshFilter → Skinned V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.Tools.Panels.MeshFilterToSkinnedPanelV2.Open(_core?.PanelContext, _toolManager?.toolContext);
        }

        if (GUILayout.Button("アバターマッピング辞書インポート V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.HumanoidMappingPanelV2.Open(_core?.PanelContext, _toolManager?.toolContext);
        }
        
        
        
        if (GUILayout.Button("Tpose V2"))
        {
            if (_core?.PanelContext != null)
                Poly_Ling.UI.TPosePanelV2.Open(_core?.PanelContext, _toolManager?.toolContext);
        }


        EditorGUILayout.Space(5);

        if (GUILayout.Button("VMD簡易テストV2"))
        {
            Poly_Ling.VMD.VMDTestPanel.Open(_core?.PanelContext, _toolManager?.toolContext);
        }

        if (GUILayout.Button("Avatar Creator... 独立"))
        {
            Poly_Ling.MISC.AvatarCreatorPanel.ShowWindow();
        }



        if (GUILayout.Button("リモートサーバ"))
        {
            RemoteServer.Open(_toolManager?.toolContext);
        }
        //-----------------------------------------------------


        EditorGUILayout.Space(15);
        if (GUILayout.Button("Yet減数化000"))
        {
            Poly_Ling.UI.QuadDecimatorPanel.Open(_toolManager?.toolContext);
        }


        if (GUILayout.Button("__old_PMX ←→ MQO"))
        {
            Poly_Ling.PMX.PMXMQOTransferPanel.ShowWindow();
        }
        EditorGUI.indentLevel--;
    }

}