// MQOImportTexts.cs
// MQOインポートパネル用ローカライズ辞書 (Runtime)
// Runtime/Poly_Ling_Main/MQO/Import/ に配置

using System.Collections.Generic;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQOインポートパネル用ローカライズ辞書。
    /// Editor の MQOImportPanel と Runtime の PlayerImportSubPanel の両方から参照する。
    /// </summary>
    public static class MQOImportTexts
    {
        public static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // ウィンドウ
            ["WindowTitle"]   = new() { ["en"] = "MQO Import",              ["ja"] = "MQOインポート" },

            // ファイルセクション
            ["File"]          = new() { ["en"] = "File",                    ["ja"] = "ファイル" },
            ["MQOFile"]       = new() { ["en"] = "MQO File",                ["ja"] = "MQOファイル" },
            ["DragDropHere"]  = new() { ["en"] = "Drag & Drop MQO file here", ["ja"] = "MQOファイルをここにドロップ" },
            ["DropFileHere"]  = new() { ["en"] = "Drop MQO file here",      ["ja"] = "ここにドロップ" },

            // 設定セクション
            ["ImportSettings"]    = new() { ["en"] = "Import Settings",     ["ja"] = "インポート設定" },
            ["Preset"]            = new() { ["en"] = "Preset",              ["ja"] = "プリセット" },
            ["Default"]           = new() { ["en"] = "Default",             ["ja"] = "デフォルト" },
            ["Coordinate"]        = new() { ["en"] = "Coordinate",          ["ja"] = "座標変換" },
            ["Scale"]             = new() { ["en"] = "Scale",               ["ja"] = "スケール" },
            ["FlipZAxis"]         = new() { ["en"] = "Flip Z Axis",         ["ja"] = "Z軸反転" },
            ["FlipUV_V"]          = new() { ["en"] = "Flip UV V",           ["ja"] = "UV V反転" },
            ["Options"]           = new() { ["en"] = "Options",             ["ja"] = "オプション" },
            ["ImportMaterials"]   = new() { ["en"] = "Import Materials",    ["ja"] = "マテリアル読込" },
            ["SkipHiddenObjects"] = new() { ["en"] = "Skip Hidden Objects", ["ja"] = "非表示オブジェクトをスキップ" },
            ["SkipEmptyObjects"]  = new() { ["en"] = "Skip Empty Objects",  ["ja"] = "空オブジェクトをスキップ" },
            ["MergeAllObjects"]   = new() { ["en"] = "Merge All Objects",   ["ja"] = "全オブジェクト統合" },
            ["SkipMqoBoneIndices"]= new() { ["en"] = "Skip Bone Indices from MQO", ["ja"] = "MQOからボーンインデックスを読込まない" },
            ["SkipMqoBoneWeights"]= new() { ["en"] = "Skip Bone Weights from MQO", ["ja"] = "MQOからウェイトを読込まない" },

            // アルファ設定
            ["AlphaSettings"]             = new() { ["en"] = "Alpha",                          ["ja"] = "アルファ" },
            ["AlphaCutoff"]               = new() { ["en"] = "Alpha Cutoff",                   ["ja"] = "アルファカットオフ" },
            ["AlphaConflict"]             = new() { ["en"] = "Opacity + Texture",              ["ja"] = "不透明度+テクスチャ競合時" },
            ["AlphaConflictTransparent"]  = new() { ["en"] = "Transparent (Opacity priority)", ["ja"] = "トランスペアレント（不透明度優先）" },
            ["AlphaConflictAlphaClip"]    = new() { ["en"] = "Alpha Clip (Texture priority)",  ["ja"] = "アルファクリップ（テクスチャ優先）" },

            // 法線
            ["Normals"]         = new() { ["en"] = "Normals",               ["ja"] = "法線" },
            ["NormalMode"]      = new() { ["en"] = "Normal Mode",           ["ja"] = "法線モード" },
            ["SmoothingAngle"]  = new() { ["en"] = "Smoothing Angle",       ["ja"] = "スムージング角度" },

            // インポートモード
            ["ImportMode"]      = new() { ["en"] = "Import Mode",           ["ja"] = "インポートモード" },
            ["ModeAppend"]      = new() { ["en"] = "Append (Add to existing)",        ["ja"] = "追加（既存に追加）" },
            ["ModeReplace"]     = new() { ["en"] = "Replace (Clear existing)",        ["ja"] = "置換（既存を削除）" },
            ["ModeNewModel"]    = new() { ["en"] = "New Model (Add as separate)",     ["ja"] = "新規モデル（別モデルとして追加）" },

            // ボーンウェイトCSV
            ["BoneWeight"]       = new() { ["en"] = "Bone Weight",          ["ja"] = "ボーンウェイト" },
            ["BoneWeightCSV"]    = new() { ["en"] = "Bone Weight CSV",      ["ja"] = "ボーンウェイトCSV" },
            ["BoneCSV"]          = new() { ["en"] = "Bone CSV (PmxBone)",   ["ja"] = "ボーンCSV (PmxBone)" },
            ["Browse"]           = new() { ["en"] = "Browse...",            ["ja"] = "参照..." },
            ["Clear"]            = new() { ["en"] = "Clear",                ["ja"] = "クリア" },
            ["CSVNotSet"]        = new() { ["en"] = "(Not set)",            ["ja"] = "（未設定）" },

            // プレビューセクション
            ["Preview"]             = new() { ["en"] = "Preview",               ["ja"] = "プレビュー" },
            ["SelectFileToPreview"] = new() { ["en"] = "Select a file to preview", ["ja"] = "ファイルを選択してください" },
            ["Version"]             = new() { ["en"] = "Version",               ["ja"] = "バージョン" },
            ["Objects"]             = new() { ["en"] = "Objects",               ["ja"] = "オブジェクト" },
            ["Materials"]           = new() { ["en"] = "Materials",             ["ja"] = "マテリアル" },
            ["Hidden"]              = new() { ["en"] = "[Hidden]",              ["ja"] = "[非表示]" },
            ["AndMore"]             = new() { ["en"] = "... and {0} more",      ["ja"] = "... 他 {0} 件" },

            // インポートボタン
            ["Import"]           = new() { ["en"] = "Import",              ["ja"] = "インポート" },
            ["Reload"]           = new() { ["en"] = "Reload",              ["ja"] = "リロード" },
            ["NoContextWarning"] = new() { ["en"] = "No context set. Open from Poly_Ling window to import directly.",
                                            ["ja"] = "コンテキスト未設定。直接インポートするにはMeshFactoryウィンドウから開いてください。" },

            // 結果セクション
            ["LastImportResult"]   = new() { ["en"] = "Last Import Result",  ["ja"] = "前回のインポート結果" },
            ["ImportSuccessful"]   = new() { ["en"] = "Import Successful!",  ["ja"] = "インポート成功！" },
            ["ImportFailed"]       = new() { ["en"] = "Import Failed: {0}", ["ja"] = "インポート失敗: {0}" },
            ["TotalVertices"]      = new() { ["en"] = "Total Vertices",      ["ja"] = "総頂点数" },
            ["TotalFaces"]         = new() { ["en"] = "Total Faces",         ["ja"] = "総面数" },
            ["SkippedSpecialFaces"]= new() { ["en"] = "Skipped Special Faces", ["ja"] = "スキップした特殊面" },
            ["ImportedMeshes"]     = new() { ["en"] = "Imported Meshes:",    ["ja"] = "インポートしたメッシュ:" },

            // ミラー設定
            ["BakeMirror"]               = new() { ["en"] = "Bake Mirror",                          ["ja"] = "ミラーをベイク" },
            ["ImportBonesFromArmature"]  = new() { ["en"] = "Import Bones from __Armature__",       ["ja"] = "__Armature__からボーンをインポート" },
            ["ConvertToTPose"]           = new() { ["en"] = "Convert to T-Pose",                    ["ja"] = "Tポーズに変換" },

            // ボーン/ウェイト設定
            ["BoneWeightSettings"]  = new() { ["en"] = "Bone / Weight",         ["ja"] = "ボーン/ウェイト" },
            ["MqoSpecialFaces"]     = new() { ["en"] = "A: MQO Special Faces",  ["ja"] = "A: MQO特殊面" },
            ["ArmatureBones"]       = new() { ["en"] = "B: __Armature__",       ["ja"] = "B: __Armature__" },
            ["ExternalCSV"]         = new() { ["en"] = "C: External CSV",       ["ja"] = "C: 外部CSV" },

            // デバッグ設定
            ["DebugSettings"]          = new() { ["en"] = "Debug Settings",         ["ja"] = "デバッグ設定" },
            ["DebugVertexInfo"]        = new() { ["en"] = "Output Vertex Debug Info", ["ja"] = "頂点デバッグ情報を出力" },
            ["DebugVertexNearUVCount"] = new() { ["en"] = "Near UV Pair Count",      ["ja"] = "近接UVペア出力件数" },
        };
    }
}
