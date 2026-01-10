// Assets/Editor/MeshFactory/Tools/PrimitiveMeshTool_Texts.cs
// プリミティブメッシュ生成ツール - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class PrimitiveMeshTool
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // セクション
            ["PrimitiveMesh"] = new() { ["en"] = "Primitive Mesh", ["ja"] = "プリミティブメッシュ", ["hi"] = "きほんのかたち" },
            ["CreateMesh"] = new() { ["en"] = "Create Mesh", ["ja"] = "メッシュを作成", ["hi"] = "メッシュをつくる" },

            // オプション
            ["AddToCurrent"] = new() { ["en"] = "Add to Current", ["ja"] = "現在のメッシュに追加", ["hi"] = "いまのメッシュについか" },
            ["NoMeshSelected"] = new() { ["en"] = "(No mesh selected)", ["ja"] = "(メッシュ未選択)", ["hi"] = "(メッシュがない)" },

            // Creatorボタン - Basic
            ["BtnCube"] = new() { ["en"] = "+ Cube...", ["ja"] = "+ 立方体...", ["hi"] = "+ はこ..." },
            ["BtnSphere"] = new() { ["en"] = "+ Sphere...", ["ja"] = "+ 球体...", ["hi"] = "+ たま..." },
            ["BtnCylinder"] = new() { ["en"] = "+ Cylinder...", ["ja"] = "+ 円柱...", ["hi"] = "+ つつ..." },
            ["BtnCapsule"] = new() { ["en"] = "+ Capsule...", ["ja"] = "+ カプセル...", ["hi"] = "+ カプセル..." },
            ["BtnPlane"] = new() { ["en"] = "+ Plane...", ["ja"] = "+ 平面...", ["hi"] = "+ いた..." },
            ["BtnPyramid"] = new() { ["en"] = "+ Pyramid...", ["ja"] = "+ 角錐...", ["hi"] = "+ ピラミッド..." },

            // Creatorボタン - Advanced
            ["BtnRevolution"] = new() { ["en"] = "+ Revolution...", ["ja"] = "+ 回転体...", ["hi"] = "+ まわす..." },
            ["BtnProfile2D"] = new() { ["en"] = "+ 2D Profile...", ["ja"] = "+ 2D押し出し...", ["hi"] = "+ 2Dおしだし..." },

            // Creatorボタン - Special
            ["BtnNohMask"] = new() { ["en"] = "+ NohMask...", ["ja"] = "+ 能面...", ["hi"] = "+ のうめん..." },

            // 警告・エラー
            ["ContextNull"] = new() { ["en"] = "Context is null, cannot add mesh", ["ja"] = "コンテキストがnullです。メッシュを追加できません", ["hi"] = "メッシュをついかできません" },
        };

        // ================================================================
        // ローカライズヘルパー
        // ================================================================

        /// <summary>テキスト取得</summary>
        private static string T(string key) => L.GetFrom(Texts, key);

        /// <summary>フォーマット付きテキスト取得</summary>
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
