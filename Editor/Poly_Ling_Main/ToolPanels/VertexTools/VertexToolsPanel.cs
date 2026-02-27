// Assets/Editor/Poly_Ling/Tools/Panels/VertexToolsPanel.cs
// 頂点選択関連ツールパネル
// 選択頂点に対する操作ツールを集約
// 
// 【ツール追加方法】
// ToolEntries配列に1行追加するだけ：
//   new ToolEntry("セクション名", () => new YourTool(), needsUpdate: false),
// 
// needsUpdate: trueの場合、毎フレームUpdate()が呼ばれる（プレビュー機能等）

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools.Panels
{
    /// <summary>
    /// 頂点ツールパネル
    /// 選択頂点に対する操作ツールへのアクセスを提供
    /// </summary>
    public partial class VertexToolsPanel : IToolPanelBase
    {
        // ================================================================
        // ツール登録（ここに追加するだけ）
        // ================================================================
        /// <summary>
        /// 登録ツール一覧（表示順）
        /// 新しいツールを追加する場合はここに1行追加するだけ
        /// </summary>
        private static readonly ToolEntry[] ToolEntries = new ToolEntry[]
        {
            new ToolEntry("AlignSection", () => new AlignVerticesTool(), needsUpdate: false),
            new ToolEntry("MergeSection", () => new MergeVerticesTool(), needsUpdate: true),
            new ToolEntry("PlanarizeSection", () => new PlanarizeAlongBonesTool(), needsUpdate: false),
            // 新しいツールはここに追加:
            // new ToolEntry("YourSection", () => new YourTool(), needsUpdate: false),
        };

        /// <summary>
        /// ツールエントリ定義
        /// </summary>
        private struct ToolEntry
        {
            public string SectionKey;           // ローカライズキー
            public Func<IEditTool> Factory;     // ツール生成ファクトリ
            public bool NeedsUpdate;            // Update()呼び出しが必要か

            public ToolEntry(string sectionKey, Func<IEditTool> factory, bool needsUpdate = false)
            {
                SectionKey = sectionKey;
                Factory = factory;
                NeedsUpdate = needsUpdate;
            }
        }



        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "VertexTools";
        public override string Title => "Vertex Tools";
        public override IToolSettings Settings => null;

        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // 内部状態
        // ================================================================

        private Vector2 _scrollPos;
        private List<IEditTool> _tools;
        private List<bool> _expanded;

        // ================================================================
        // Open
        // ================================================================

        /// <summary>
        /// パネルを開く
        /// </summary>
        public static VertexToolsPanel Open(ToolContext ctx)
        {
            var panel = GetWindow<VertexToolsPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(280, 300);
            panel.SetContext(ctx);
            panel.Show();
            return panel;
        }

        // ================================================================
        // コンテキスト設定時
        // ================================================================

        protected override void OnContextSet()
        {
            _scrollPos = Vector2.zero;

            // ツールインスタンスを生成
            if (_tools == null)
            {
                _tools = new List<IEditTool>();
                _expanded = new List<bool>();

                for (int i = 0; i < ToolEntries.Length; i++)
                {
                    _tools.Add(ToolEntries[i].Factory());
                    _expanded.Add(i == 0); // 最初のツールだけ展開
                }
            }

            // 全ツールをアクティブ化
            foreach (var tool in _tools)
            {
                tool.OnActivate(_context);
            }
        }

        // ================================================================
        // 破棄時
        // ================================================================

        protected override void OnDestroy()
        {
            base.OnDestroy();

            // 全ツールを非アクティブ化
            if (_tools != null)
            {
                foreach (var tool in _tools)
                {
                    tool.OnDeactivate(_context);
                }
            }
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            // コンテキストチェック
            if (!DrawNoContextWarning())
                return;

            if (!HasValidSelection)
            {
                EditorGUILayout.HelpBox(T("NoMeshSelected"), MessageType.Warning);
                return;
            }

            // Update()が必要なツールを更新
            UpdateTools();

            // 選択情報ヘッダー
            DrawSelectionHeader();

            EditorGUILayout.Space(5);

            // スクロールビュー
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // 全ツールのセクションを描画
            DrawAllToolSections();

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // ツール更新
        // ================================================================

        private void UpdateTools()
        {
            if (_tools == null) return;

            for (int i = 0; i < _tools.Count; i++)
            {
                if (ToolEntries[i].NeedsUpdate)
                {
                    // Update()メソッドがあれば呼び出す
                    if (_tools[i] is MergeVerticesTool mergeTool)
                    {
                        mergeTool.Update(_context);
                    }
                    // 他のUpdate対応ツールがあればここに追加
                    // else if (_tools[i] is YourTool yourTool)
                    // {
                    //     yourTool.Update(_context);
                    // }
                }
                // needsUpdate: false のツールはフレーム更新不要
                // OnActivate()はコンテキスト設定時（OnContextSet）で1回だけ呼ぶ
            }
        }

        // ================================================================
        // 選択情報ヘッダー
        // ================================================================

        private void DrawSelectionHeader()
        {
            int selectedCount = _context?.SelectedVertices?.Count ?? 0;
            int totalVertices = _context?.FirstSelectedMeshObject?.VertexCount ?? 0;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField(T("SelectedInfo", selectedCount, totalVertices), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // 全ツールセクション描画
        // ================================================================

        private void DrawAllToolSections()
        {
            if (_tools == null) return;

            for (int i = 0; i < _tools.Count; i++)
            {
                DrawToolSection(i);

                if (i < _tools.Count - 1)
                {
                    EditorGUILayout.Space(5);
                }
            }
        }

        private void DrawToolSection(int index)
        {
            var entry = ToolEntries[index];
            var tool = _tools[index];

            _expanded[index] = EditorGUILayout.BeginFoldoutHeaderGroup(_expanded[index], T(entry.SectionKey));

            if (_expanded[index])
            {
                EditorGUI.indentLevel++;

                if (tool != null)
                {
                    tool.DrawSettingsUI();
                }
                else
                {
                    EditorGUILayout.HelpBox($"Tool not initialized: {entry.SectionKey}", MessageType.Error);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ================================================================
        // 更新
        // ================================================================

        private void OnInspectorUpdate()
        {
            // 定期的に再描画（選択状態の変化を反映）
            Repaint();
        }
    }
}
