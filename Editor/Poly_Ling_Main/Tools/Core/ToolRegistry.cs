// Assets/Editor/Poly_Ling/Tools/Core/ToolRegistry.cs
// 全ツールの登録を一箇所で管理
// 新しいツールを追加する際はここだけ修正すればよい
// ToolCategoryはIEditTool.csに移動
// Phase 4: PrimitiveMeshTool追加

using System;
using System.Collections.Generic;

namespace Poly_Ling.Tools
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

            () => new MoveTool(),
            () => new RotateTool(),
            () => new ScaleTool(),

            () => new AdvancedSelectTool(),

            () => new SculptTool(),

            // Topology
            () => new AddFaceTool(),
            () => new KnifeTool(),
            () => new EdgeTopologyTool(),
            //() => new MergeVerticesTool(),
            //() => new AlignVerticesTool(),
            () => new EdgeExtrudeTool(),
            () => new FaceExtrudeTool(),
            () => new LineExtrudeTool(),
            () => new EdgeBevelTool(),
            () => new FlipFaceTool(),


            () => new PivotOffsetTool(),

            // Primitive (Phase 4追加)
            () => new PrimitiveMeshTool(),
            () => new MirrorEditTool(),

            () => new SkinWeightPaintTool(),

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
    }
}
