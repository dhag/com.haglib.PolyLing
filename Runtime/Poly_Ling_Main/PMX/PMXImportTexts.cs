// PMXImportTexts.cs
// PMXインポートパネル用ローカライズ辞書 (Runtime)
// Runtime/Poly_Ling_Main/PMX/ に配置

using System.Collections.Generic;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXインポートパネル用ローカライズ辞書。
    /// Editor の PMXImportPanel と Runtime の PlayerImportSubPanel の両方から参照する。
    /// </summary>
    public static class PMXImportTexts
    {
        public static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // ウィンドウ
            ["WindowTitle"]   = new() { ["en"] = "PMX Import",              ["ja"] = "PMXインポート" },

            // ファイルセクション
            ["File"]          = new() { ["en"] = "File",                    ["ja"] = "ファイル" },
            ["PMXFile"]       = new() { ["en"] = "PMX File",                ["ja"] = "PMXファイル" },
            ["DragDropHere"]  = new() { ["en"] = "Drag & Drop PMX/CSV file here", ["ja"] = "PMX/CSVファイルをここにドロップ" },
            ["DropFileHere"]  = new() { ["en"] = "Drop PMX/CSV file here",  ["ja"] = "ここにドロップ" },

            // 設定セクション
            ["ImportSettings"]  = new() { ["en"] = "Import Settings",       ["ja"] = "インポート設定" },
            ["Preset"]          = new() { ["en"] = "Preset",                ["ja"] = "プリセット" },
            ["Default"]         = new() { ["en"] = "Default",               ["ja"] = "デフォルト" },
            ["MMDCompatible"]   = new() { ["en"] = "MMD Compatible",        ["ja"] = "MMD互換" },
            ["NoScale"]         = new() { ["en"] = "No Scale (1:1)",        ["ja"] = "等倍（1:1）" },
            ["Coordinate"]      = new() { ["en"] = "Coordinate",            ["ja"] = "座標変換" },
            ["Scale"]           = new() { ["en"] = "Scale",                 ["ja"] = "スケール" },
            ["FlipZAxis"]       = new() { ["en"] = "Flip Z Axis",           ["ja"] = "Z軸反転" },
            ["FlipUV_V"]        = new() { ["en"] = "Flip UV V",             ["ja"] = "UV V反転" },
            ["Options"]         = new() { ["en"] = "Options",               ["ja"] = "オプション" },
            ["ImportMaterials"] = new() { ["en"] = "Import Materials",      ["ja"] = "マテリアル読込" },
            ["Normals"]         = new() { ["en"] = "Normals",               ["ja"] = "法線" },
            ["RecalculateNormals"] = new() { ["en"] = "Recalculate Normals", ["ja"] = "法線を再計算" },
            ["SmoothingAngle"]  = new() { ["en"] = "Smoothing Angle",       ["ja"] = "スムージング角度" },
            ["ConvertToTPose"]  = new() { ["en"] = "Convert to T-Pose",     ["ja"] = "Tポーズに変換" },

            // インポートモード
            ["ImportMode"]      = new() { ["en"] = "Import Mode",           ["ja"] = "インポートモード" },
            ["ModeAppend"]      = new() { ["en"] = "Append (Add to existing)",        ["ja"] = "追加（既存に追加）" },
            ["ModeReplace"]     = new() { ["en"] = "Replace (Clear existing)",        ["ja"] = "置換（既存を削除）" },
            ["ModeNewModel"]    = new() { ["en"] = "New Model (Add as separate)",     ["ja"] = "新規モデル（別モデルとして追加）" },

            // インポート対象
            ["ImportTarget"]    = new() { ["en"] = "Import Target",         ["ja"] = "インポート対象" },
            ["TargetMesh"]      = new() { ["en"] = "Mesh",                  ["ja"] = "メッシュ" },
            ["TargetBones"]     = new() { ["en"] = "Bones",                 ["ja"] = "ボーン" },
            ["TargetMorphs"]    = new() { ["en"] = "Morphs",                ["ja"] = "モーフ" },
            ["TargetBodies"]    = new() { ["en"] = "Bodies",                ["ja"] = "剛体" },
            ["TargetJoints"]    = new() { ["en"] = "Joints",                ["ja"] = "ジョイント" },
            ["BonesOnly"]       = new() { ["en"] = "Bones Only",            ["ja"] = "ボーンのみ" },

            // プレビューセクション
            ["Preview"]              = new() { ["en"] = "Preview",                ["ja"] = "プレビュー" },
            ["SelectFileToPreview"]  = new() { ["en"] = "Select a file to preview", ["ja"] = "ファイルを選択してください" },
            ["Version"]              = new() { ["en"] = "Version",                ["ja"] = "バージョン" },
            ["ModelName"]            = new() { ["en"] = "Model Name",             ["ja"] = "モデル名" },
            ["Vertices"]             = new() { ["en"] = "Vertices",               ["ja"] = "頂点数" },
            ["Faces"]                = new() { ["en"] = "Faces",                  ["ja"] = "面数" },
            ["Materials"]            = new() { ["en"] = "Materials",              ["ja"] = "マテリアル" },
            ["Bones"]                = new() { ["en"] = "Bones",                  ["ja"] = "ボーン" },
            ["Morphs"]               = new() { ["en"] = "Morphs",                 ["ja"] = "モーフ" },
            ["AndMore"]              = new() { ["en"] = "... and {0} more",       ["ja"] = "... 他 {0} 件" },

            // インポートボタン
            ["Import"]           = new() { ["en"] = "Import",              ["ja"] = "インポート" },
            ["Reload"]           = new() { ["en"] = "Reload",              ["ja"] = "リロード" },
            ["DetectNamedMirror"]= new() { ["en"] = "Also treat name (+) as mirror", ["ja"] = "名前ミラー(+)もミラーとみなす" },
            ["BakeMirror"]       = new() { ["en"] = "Bake Mirror",         ["ja"] = "ミラーをベイク" },

            // アルファ設定
            ["AlphaSettings"]             = new() { ["en"] = "Alpha",                          ["ja"] = "アルファ" },
            ["AlphaCutoff"]               = new() { ["en"] = "Alpha Cutoff",                   ["ja"] = "アルファカットオフ" },
            ["AlphaConflict"]             = new() { ["en"] = "Opacity + Texture",              ["ja"] = "不透明度+テクスチャ競合時" },
            ["AlphaConflictTransparent"]  = new() { ["en"] = "Transparent (Opacity priority)", ["ja"] = "トランスペアレント（不透明度優先）" },
            ["AlphaConflictAlphaClip"]    = new() { ["en"] = "Alpha Clip (Texture priority)",  ["ja"] = "アルファクリップ（テクスチャ優先）" },
            ["NoContextWarning"]          = new() { ["en"] = "No context set. Open from Poly_Ling window to import directly.",
                                                     ["ja"] = "コンテキスト未設定。直接インポートするにはMeshFactoryウィンドウから開いてください。" },

            // 結果セクション
            ["LastImportResult"]  = new() { ["en"] = "Last Import Result",  ["ja"] = "前回のインポート結果" },
            ["ImportSuccessful"]  = new() { ["en"] = "Import Successful!",  ["ja"] = "インポート成功！" },
            ["ImportFailed"]      = new() { ["en"] = "Import Failed: {0}", ["ja"] = "インポート失敗: {0}" },
            ["TotalVertices"]     = new() { ["en"] = "Total Vertices",      ["ja"] = "総頂点数" },
            ["TotalFaces"]        = new() { ["en"] = "Total Faces",         ["ja"] = "総面数" },
            ["MaterialGroups"]    = new() { ["en"] = "Material Groups",     ["ja"] = "マテリアルグループ" },
            ["ImportedMeshes"]    = new() { ["en"] = "Imported Meshes:",    ["ja"] = "インポートしたメッシュ:" },
        };
    }
}
