// PrimitiveMeshTexts.Sandbox.cs
// 図形生成パネル用ローカライズ辞書：MCP用サンドボックスの図形の分。
// Runtime/Poly_Ling_Player/View/McpSandbox/ に配置
//
// 本番の辞書（PrimitiveMeshTexts.cs）に無いキーだけを置く。
// T() は本番の辞書を先に引くので、同じキーを置いても本番の文字列が出る。
// 図形を本番へ昇格させるときは、該当行を PrimitiveMeshTexts.cs へ移す。

using System.Collections.Generic;

namespace Poly_Ling.Player
{
    public static partial class PrimitiveMeshTexts
    {
        private static readonly Dictionary<string, Dictionary<string, string>> SandboxTexts = new()
        {
            ["McpCylinder"] = new() { ["en"] = "MCP Cylinder", ["ja"] = "MCP円筒", ["hi"] = "MCPのつつ" },
        };
    }
}
