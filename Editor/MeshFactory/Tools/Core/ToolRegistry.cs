// Assets/Editor/MeshFactory/Tools/Core/ToolRegistry.cs
// 全ツールの登録を一箇所で管理
// 新しいツールを追加する際はここだけ修正すればよい
// ToolCategoryはIEditTool.csに移動

using System;
using System.Collections.Generic;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 全ツールの登録を一元管理
    /// 新しいツールを追加する場合はToolFactoriesに追加するだけ
    /// </summary>
    public static class ToolRegistry
    {
        // ================================================================
        // ツールファクトリ定義
        // ================================================================

        /// <summary>
        /// 全ツールのファクトリ（登録順 = UI表示順）
        /// 新しいツールを追加する場合はここに追加するだけ
        /// </summary>
        public static readonly Func<IEditTool>[] ToolFactories = new Func<IEditTool>[]
        {
            // Selection
            //() => new SelectTool(),
            //() => new AdvancedSelectTool(),

            // Transform
            //() => new MoveTool(),
            //() => new SculptTool(),


            () => new MoveTool(),
            () => new AdvancedSelectTool(),

            () => new SculptTool(),

            // Topology
            () => new AddFaceTool(),
            () => new KnifeTool(),
            () => new EdgeTopologyTool(),
            () => new MergeVerticesTool(),
            () => new EdgeExtrudeTool(),
            () => new FaceExtrudeTool(),
            () => new EdgeBevelTool(),
            () => new FlipFaceTool(),
            () => new LineExtrudeTool(),

            // Utility
            () => new PivotOffsetTool(),
        };

        // ================================================================
        // 登録メソッド
        // ================================================================

        /// <summary>
        /// 全ツールをToolManagerに登録
        /// </summary>
        public static void RegisterAllTools(ToolManager manager)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            foreach (var factory in ToolFactories)
            {
                var tool = factory();
                manager.Register(tool);
            }

            // デフォルトツールを設定
            //manager.SetDefault("Move");
        }
        /*
        /// <summary>
        /// 指定カテゴリのツールのみ登録
        /// </summary>
        public static void RegisterCategory(ToolManager manager, ToolCategory category)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            foreach (var factory in ToolFactories)
            {
                var tool = factory();
                if (tool.Category == category)
                {
                    manager.Register(tool);
                }
            }
        }

        /// <summary>
        /// 全ツールのインスタンスを生成して返す（UI用）
        /// </summary>
        public static List<IEditTool> CreateAllTools()
        {
            var tools = new List<IEditTool>();
            foreach (var factory in ToolFactories)
            {
                tools.Add(factory());
            }
            return tools;
        }
        */    
    }
}
