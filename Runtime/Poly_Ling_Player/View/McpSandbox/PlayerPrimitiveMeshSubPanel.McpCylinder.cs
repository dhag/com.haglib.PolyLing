// PlayerPrimitiveMeshSubPanel.McpCylinder.cs
// 図形生成サブパネル：MCP円筒（MCP用サンドボックスの図形）。
// Runtime/Poly_Ling_Player/View/McpSandbox/ に配置
//
// 【サンドボックスの図形とは】
//   本番の図形と同じ PlayerPrimitiveMeshSubPanel の partial として書く。
//   本番との違いは「どのカテゴリ配列に載っているか」だけで、サンドボックスの図形は
//   SandboxShapes（PlayerPrimitiveMeshSubPanel.Shapes.cs）にだけ載せる。
//   配置・名前欄・材質・追加先・プレビュー・ライブワイヤ・生成コマンドは本番の図形と
//   同じコードを通るので、ここで確かめた結果がそのまま本番での結果になる。
//
// 【図形を足すときに触る場所】（本番の図形と同じ）
//   1. Poly_Ling_Main/Tools/PrimitiveMesh/<名前>MeshGenerator.cs
//        生成ロジックとパラメータ構造体（値域の定数もここに置く）
//   2. PanelCommand.cs               Create<名前>Command（CreatePrimitiveMeshCommand 派生）
//   3. PrimitiveMeshFactory.cs       コマンド → 生成ロジックの case
//   4. PlayerPrimitiveMeshSubPanel.Shapes.cs
//        ShapeKind の末尾・ShapeKeys の末尾・SandboxShapes・RebuildSettings の case
//   5. PlayerPrimitiveMeshSubPanel.Naming.cs   Name() / SetName() の case
//   6. PlayerPrimitiveMeshSubPanel.Command.cs  BuildCreateCommand の case
//   7. このファイルと同じ形の図形ファイル（既定値 → 状態 → 諸元 UI）
//   8. PrimitiveMeshTexts.Sandbox.cs          本番の辞書に無い表示文字列
//   生成できる条件があるときは CanGenerate（Generate.cs）にも case を足す。
//
// 【本番への昇格】
//   a. Shapes.cs で ShapeKind を SandboxShapes から BasicShapes / AdvancedShapes 等へ移す
//   b. このファイルを View/PrimitiveMesh/ へ移す（中身は変えない）
//   c. PrimitiveMeshTexts.Sandbox.cs の該当行を PrimitiveMeshTexts.cs へ移す
//   ほかの登録箇所は本番と同じ書き方なので触らない。
//
// 【MCP円筒について】
//   CylinderMeshGenerator の複製から出発した試作用の円筒（McpCylinderMeshGenerator）。
//   諸元 UI は本番の円柱（BuildCylinderUI）と同じ並び・同じ行ヘルパで組んである。

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        /// <summary>底面を原点に置く（本パネルの他の図形と同じ既定ピボット）。</summary>
        private static McpCylinderMeshGenerator.McpCylinderParams DefaultMcpCylinderParams()
        {
            var p = McpCylinderMeshGenerator.McpCylinderParams.Default;
            p.Pivot = DefaultPivotBottom;
            return p;
        }

        private McpCylinderMeshGenerator.McpCylinderParams _mcpCylP = DefaultMcpCylinderParams();

        // ================================================================
        // UI
        // ================================================================

        private void BuildMcpCylinderUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("McpCylinder")));
            c.Add(NF(() => _mcpCylP.MeshName, v => _mcpCylP.MeshName = v));

            c.Add(SL(T("Size")));
            c.Add(SR(T("RadiusTop"),
                McpCylinderMeshGenerator.McpCylinderParams.RadiusMin,
                McpCylinderMeshGenerator.McpCylinderParams.RadiusMax,
                () => _mcpCylP.RadiusTop,    v => { _mcpCylP.RadiusTop    = v; D(); }));
            c.Add(SR(T("RadiusBottom"),
                McpCylinderMeshGenerator.McpCylinderParams.RadiusMin,
                McpCylinderMeshGenerator.McpCylinderParams.RadiusMax,
                () => _mcpCylP.RadiusBottom, v => { _mcpCylP.RadiusBottom = v; D(); }));
            c.Add(SR(T("Height"),
                McpCylinderMeshGenerator.McpCylinderParams.HeightMin,
                McpCylinderMeshGenerator.McpCylinderParams.HeightMax,
                () => _mcpCylP.Height,       v => { _mcpCylP.Height       = v; D(); }));

            c.Add(SL(T("Segments")));
            c.Add(IR(T("Radial"),
                McpCylinderMeshGenerator.McpCylinderParams.RadialSegmentsMin,
                McpCylinderMeshGenerator.McpCylinderParams.RadialSegmentsMax,
                () => _mcpCylP.RadialSegments, v => { _mcpCylP.RadialSegments = v; D(); }));
            c.Add(IR(T("Lateral"),
                McpCylinderMeshGenerator.McpCylinderParams.HeightSegmentsMin,
                McpCylinderMeshGenerator.McpCylinderParams.HeightSegmentsMax,
                () => _mcpCylP.HeightSegments, v => { _mcpCylP.HeightSegments = v; D(); }));

            c.Add(TR(T("CapTop"),    () => _mcpCylP.CapTop,    v => { _mcpCylP.CapTop    = v; D(); }));
            c.Add(TR(T("CapBottom"), () => _mcpCylP.CapBottom, v => { _mcpCylP.CapBottom = v; D(); }));

            // 縁の丸めの上限は高さと半径から決まる（本番の BuildCylinderUI と同じ計算）。
            float maxEdge = _mcpCylP.Height * 0.5f;
            if (_mcpCylP.CapTop    && _mcpCylP.RadiusTop    > 0) maxEdge = Mathf.Min(maxEdge, _mcpCylP.RadiusTop);
            if (_mcpCylP.CapBottom && _mcpCylP.RadiusBottom > 0) maxEdge = Mathf.Min(maxEdge, _mcpCylP.RadiusBottom);
            if (maxEdge > 0f)
            {
                c.Add(SR(T("EdgeRadius"),
                    McpCylinderMeshGenerator.McpCylinderParams.EdgeRadiusMin, maxEdge,
                    () => _mcpCylP.EdgeRadius, v => { _mcpCylP.EdgeRadius = v; D(); }));
                if (_mcpCylP.EdgeRadius > 0f)
                    c.Add(IR(T("EdgeSeg"),
                        McpCylinderMeshGenerator.McpCylinderParams.EdgeSegmentsMin,
                        McpCylinderMeshGenerator.McpCylinderParams.EdgeSegmentsMax,
                        () => _mcpCylP.EdgeSegments, v => { _mcpCylP.EdgeSegments = v; D(); }));
            }

            BuildPivotY(c,
                () => _mcpCylP.Pivot.y, v => { _mcpCylP.Pivot = new Vector3(0, v, 0); D(); },
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _, out _);
        }
    }
}
