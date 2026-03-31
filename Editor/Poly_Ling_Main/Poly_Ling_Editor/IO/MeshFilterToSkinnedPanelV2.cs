// MeshFilterToSkinnedPanelV2.cs
// MeshFilter → Skinned 変換パネル V2
// PanelContext（通知）+ ToolContext（実処理）ハイブリッド。IMGUI継続。

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Localization;
using Poly_Ling.Ops;
using static Poly_Ling.Ops.MeshFilterToSkinnedTexts;

namespace Poly_Ling.Tools.Panels
{

    public class MeshFilterToSkinnedPanelV2 : EditorWindow
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _panelCtx;
        private ToolContext  _toolCtx;

        private ModelContext Model => _toolCtx?.Model;

        // ================================================================
        // UI 状態
        // ================================================================

        private Vector2 _scrollPosition;
        private bool    _foldPreview               = true;
        private bool    _swapAxisForRotatedBones   = false;
        private bool    _setAxisForIdentityBones   = false;

        // ================================================================
        // Open
        // ================================================================

        public static MeshFilterToSkinnedPanelV2 Open(PanelContext panelCtx, ToolContext toolCtx)
        {
            var w = GetWindow<MeshFilterToSkinnedPanelV2>();
            w.titleContent = new GUIContent(T("WindowTitle"));
            w.minSize = new Vector2(350, 300);
            w.SetContexts(panelCtx, toolCtx);
            w.Show();
            return w;
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        private void SetContexts(PanelContext panelCtx, ToolContext toolCtx)
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;

            _panelCtx = panelCtx;
            _toolCtx  = toolCtx;

            if (_panelCtx != null) _panelCtx.OnViewChanged += OnViewChanged;

            Repaint();
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            if (_panelCtx != null)
            {
                _panelCtx.OnViewChanged -= OnViewChanged;
                _panelCtx.OnViewChanged += OnViewChanged;
            }
        }

        private void OnDisable()
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void OnDestroy()
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (kind == ChangeKind.ModelSwitch || kind == ChangeKind.ListStructure)
                Repaint();
        }

        // ================================================================
        // GUI（V1 の OnGUI と同一ロジック）
        // ================================================================

        private void OnGUI()
        {
            if (_toolCtx == null)
            {
                EditorGUILayout.HelpBox("ToolContext が未設定です。PolyLing ウィンドウから開いてください。",
                    MessageType.Warning);
                return;
            }

            var model = Model;
            if (model == null)
            {
                EditorGUILayout.HelpBox(T("ModelNotAvailable"), MessageType.Warning);
                return;
            }

            var meshEntries = MeshFilterToSkinnedConverter.CollectMeshEntries(model);

            if (meshEntries.Count == 0)
            {
                EditorGUILayout.HelpBox(T("NoMeshFound"), MessageType.Warning);
                return;
            }

            bool hasBones = model.MeshContextList.Any(ctx => ctx?.Type == MeshType.Bone);
            if (hasBones)
                EditorGUILayout.HelpBox(T("AlreadyHasBones"), MessageType.Warning);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.Space(10);
            _foldPreview = EditorGUILayout.Foldout(_foldPreview, T("Preview"), true);
            if (_foldPreview)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(T("RootBone"), meshEntries[0].Context.Name);
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField(T("BoneHierarchy"), EditorStyles.miniLabel);
                for (int i = 0; i < meshEntries.Count; i++)
                {
                    var entry = meshEntries[i];
                    int depth = MeshFilterToSkinnedConverter.CalculateDepth(entry.Index, model);
                    string indent     = new string(' ', depth * 4);
                    string vertexInfo = entry.Context.MeshObject?.VertexCount > 0
                        ? $" ({entry.Context.MeshObject.VertexCount}V)"
                        : " (empty)";
                    EditorGUILayout.LabelField($"{indent}[{i}] {entry.Context.Name}{vertexInfo}");
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField(T("BoneAxisSettings"), EditorStyles.boldLabel);
            _swapAxisForRotatedBones = EditorGUILayout.ToggleLeft(T("SwapAxisRotated"), _swapAxisForRotatedBones);
            _setAxisForIdentityBones = EditorGUILayout.ToggleLeft(T("SetAxisIdentity"),  _setAxisForIdentityBones);
            EditorGUILayout.Space(10);

            EditorGUI.BeginDisabledGroup(hasBones);
            if (GUILayout.Button(T("Convert"), GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog(T("WindowTitle"), T("ConvertWarning"), "OK", "Cancel"))
                {
                    int boneCount = MeshFilterToSkinnedConverter.Execute(
                        model, meshEntries, _swapAxisForRotatedBones, _setAxisForIdentityBones);
                    _toolCtx?.OnTopologyChanged();
                    EditorUtility.DisplayDialog(T("WindowTitle"), T("ConvertSuccess", boneCount), "OK");
                    _toolCtx?.Repaint?.Invoke();
                    Repaint();
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndScrollView();
        }

    }
}

