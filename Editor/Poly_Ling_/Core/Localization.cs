// Assets/Editor/Poly_Ling/Localization/Localization.cs
// 多言語対応基盤
// 対応言語: English / 日本語 / ひらがな

using System.Collections.Generic;
using UnityEditor;
using Poly_Ling.Tools;

namespace Poly_Ling.Localization
{
    /// <summary>
    /// 対応言語
    /// </summary>
    public enum Language
    {
        English,
        Japanese,    // 日本語
        Hiragana     // ひらがな（子供・学習向け）
    }

    /// <summary>
    /// ローカライゼーションマネージャー
    /// 短縮名 L.Get("key") で使用可能
    /// </summary>
    public static class L
    {
        // ================================================================
        // 設定
        // ================================================================
        
        private static Language _currentLanguage = Language.Japanese;
        
        /// <summary>現在の言語</summary>
        public static Language CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    // EditorPrefsに保存
                    EditorPrefs.SetInt("MeshFactory_Language", (int)value);
                }
            }
        }
        
        /// <summary>言語設定を読み込み</summary>
        public static void LoadSettings()
        {
            _currentLanguage = (Language)EditorPrefs.GetInt("MeshFactory_Language", (int)Language.Japanese);
        }
        
        // ================================================================
        // テキスト取得
        // ================================================================
        
        /// <summary>
        /// ローカライズされたテキストを取得
        /// </summary>
        /// <param name="key">テキストキー</param>
        /// <returns>現在の言語のテキスト（未定義ならキーをそのまま返す）</returns>
        public static string Get(string key)
        {
            return GetFrom(_texts, key);
        }
        
        /// <summary>
        /// 指定辞書からローカライズされたテキストを取得
        /// ツール固有の辞書用
        /// </summary>
        /// <param name="texts">ローカライズ辞書</param>
        /// <param name="key">テキストキー</param>
        /// <returns>現在の言語のテキスト（未定義ならキーをそのまま返す）</returns>
        public static string GetFrom(Dictionary<string, Dictionary<string, string>> texts, string key)
        {
            if (texts != null && texts.TryGetValue(key, out var translations))
            {
                string langKey = GetLanguageKey(_currentLanguage);
                if (translations.TryGetValue(langKey, out var text))
                {
                    return text;
                }
                // フォールバック: 英語
                if (translations.TryGetValue("en", out var fallback))
                {
                    return fallback;
                }
            }
            return key; // 未定義ならキーをそのまま返す
        }
        
        /// <summary>
        /// 指定辞書からフォーマット付きでテキストを取得
        /// </summary>
        public static string GetFrom(Dictionary<string, Dictionary<string, string>> texts, string key, params object[] args)
        {
            string format = GetFrom(texts, key);
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }
        
        /// <summary>
        /// ローカライズされたテキストをフォーマット付きで取得
        /// </summary>
        /// <param name="key">テキストキー</param>
        /// <param name="args">フォーマット引数</param>
        /// <returns>フォーマット済みテキスト</returns>
        public static string Get(string key, params object[] args)
        {
            return GetFrom(_texts, key, args);
        }
        
        /// <summary>
        /// 言語キーを取得
        /// </summary>
        public static string GetLanguageKey(Language lang)
        {
            return lang switch
            {
                Language.English => "en",
                Language.Japanese => "ja",
                Language.Hiragana => "hi",
                _ => "en"
            };
        }
        
        /// <summary>
        /// 言語の表示名を取得
        /// </summary>
        public static string GetLanguageDisplayName(Language lang)
        {
            return lang switch
            {
                Language.English => "English",
                Language.Japanese => "日本語",
                Language.Hiragana => "ひらがな",
                _ => "English"
            };
        }
        
        // ================================================================
        // テキスト辞書
        // ================================================================
        
        private static readonly Dictionary<string, Dictionary<string, string>> _texts = new()
        {
            // ============================================================
            // ウィンドウ・タイトル
            // ============================================================
            ["Poly_Ling"] = new() { ["en"] = "Poly Ling", ["ja"] = "ポリーリング/ぽりりん", ["hi"] = "ぽりりん" },
            
            // ============================================================
            // セクション名
            // ============================================================
            ["Display"] = new() { ["en"] = "Display", ["ja"] = "表示", ["hi"] = "ひょうじ" },
            ["Primitive"] = new() { ["en"] = "Primitive", ["ja"] = "プリミティブ", ["hi"] = "きほんけい" },
            ["Tools"] = new() { ["en"] = "Tools", ["ja"] = "ツール", ["hi"] = "どうぐ" },
            ["Selection"] = new() { ["en"] = "Selection", ["ja"] = "選択", ["hi"] = "せんたく" },
            ["Symmetry"] = new() { ["en"] = "Symmetry (Mirror)", ["ja"] = "対称（ミラー）", ["hi"] = "たいしょう（かがみ）" },
            ["ToolPanels"] = new() { ["en"] = "Tool Panels", ["ja"] = "ツールパネル", ["hi"] = "どうぐいれ" },
            ["Materials"] = new() { ["en"] = "Materials", ["ja"] = "マテリアル", ["hi"] = "ざいりょう" },
            ["Save"] = new() { ["en"] = "Save", ["ja"] = "保存", ["hi"] = "ほぞん" },
            ["ModelFile"] = new() { ["en"] = "Project", ["ja"] = "プロジェクト", ["hi"] = "ぷろじぇくと" },
            ["VertexEditor"] = new() { ["en"] = "Vertex Editor", ["ja"] = "頂点エディタ", ["hi"] = "ちょうてんへんしゅう" },
            ["MeshList"] = new() { ["en"] = "Mesh List", ["ja"] = "メッシュリスト", ["hi"] = "めっしゅりすと" },
            
            // ============================================================
            // ウィンドウタイトル（キーは "Window_" + IToolPanel.Name）
            // ============================================================
            ["Window_MeshContextList"] = new() { ["en"] = "Mesh List", ["ja"] = "メッシュオブジェクトリスト", ["hi"] = "ずけいりすと" },
            
            // ============================================================
            // Displayセクション
            // ============================================================
            ["ShowMesh"] = new() { ["en"] = "Mesh", ["ja"] = "メッシュ", ["hi"] = "めっしゅ" },
            ["ShowUnselected"] = new() { ["en"] = "Show Unselected", ["ja"] = "非選択も表示", ["hi"] = "ほかもみせる" },
            ["Wireframe"] = new() { ["en"] = "Wireframe", ["ja"] = "ワイヤーフレーム", ["hi"] = "わいやー" },
            ["ShowVertices"] = new() { ["en"] = "Show Vertices", ["ja"] = "頂点を表示", ["hi"] = "てんをみせる" },
            ["ShowVertexIndices"] = new() { ["en"] = "Show Vertex Indices", ["ja"] = "頂点インデックス表示" },
            ["ShowSelectedMeshOnly"] = new() { ["en"] = "Selected Mesh Only", ["ja"] = "選択中のメッシュのみ表示" },
            ["Zoom"] = new() { ["en"] = "Zoom", ["ja"] = "ズーム", ["hi"] = "ずーむ" },
            ["AutoZoom"] = new() { ["en"] = "Auto Zoom", ["ja"] = "オートズーム", ["hi"] = "おーとずーむ" },
            ["UndoFoldout"] = new() { ["en"] = "Undo Foldout Changes", ["ja"] = "開閉をUndo記録", ["hi"] = "ひらきとじをきろく" },
            ["Language"] = new() { ["en"] = "Language", ["ja"] = "言語", ["hi"] = "ことば" },


            // Model セクション
            ["Model"] = new() { ["en"] = "Model", ["ja"] = "モデル", ["hi"] = "もでる" },
            ["DeleteModel"] = new() { ["en"] = "Delete Model", ["ja"] = "モデルを削除", ["hi"] = "もでるをけす" },
            ["DeleteModelConfirm"] = new() { ["en"] = "Delete model '{0}'?\nThis cannot be undone.", ["ja"] = "モデル '{0}' を削除しますか？\nこの操作は元に戻せません。", ["hi"] = "'{0}' をけす？" },
            ["Delete"] = new() { ["en"] = "Delete", ["ja"] = "削除", ["hi"] = "けす" },
            ["Cancel"] = new() { ["en"] = "Cancel", ["ja"] = "キャンセル", ["hi"] = "やめる" },

            // ============================================================
            // Selection Dics セクション
            // ============================================================
            ["SelectionDics"] = new() { ["en"] = "Selection Dictionary", ["ja"] = "選択辞書", ["hi"] = "せんたくじしょ" },
            ["HashingSelection"] = new() { ["en"] = "Hashing", ["ja"] = "辞書化", ["hi"] = "じしょにする" },
            ["To Current"] = new() { ["en"] = "To Current", ["ja"] = "呼出し", ["hi"] = "よびだし" },
            ["Add"] = new() { ["en"] = "Add", ["ja"] = "追加", ["hi"] = "ついか" },
            ["Subtract"] = new() { ["en"] = "Sub", ["ja"] = "除外", ["hi"] = "のぞく" },
            ["NoSelectionSets"] = new() { ["en"] = "No selection sets saved", ["ja"] = "保存された選択セットはありません", ["hi"] = "せっとがない" },
            ["DeleteSelectionSet"] = new() { ["en"] = "Delete Selection Set", ["ja"] = "選択セットを削除", ["hi"] = "せっとをけす" },
            ["DeleteSelectionSetConfirm"] = new() { ["en"] = "Delete selection set '{0}'?", ["ja"] = "選択セット '{0}' を削除しますか？", ["hi"] = "'{0}' をけす？" },
            ["SaveToFile"] = new() { ["en"] = "JSON", ["ja"] = "JSON", ["hi"] = "JSON" },
            ["LoadFromFile"] = new() { ["en"] = "JSON", ["ja"] = "JSON", ["hi"] = "JSON" },
            ["ExportCSV"] = new() { ["en"] = "CSV↑", ["ja"] = "CSV↑", ["hi"] = "CSV↑" },
            ["ImportCSV"] = new() { ["en"] = "CSV↓", ["ja"] = "CSV↓", ["hi"] = "CSV↓" },


            // ============================================================
            // Symmetryセクション
            // ============================================================
            ["EnableMirror"] = new() { ["en"] = "Enable Mirror", ["ja"] = "ミラー有効", ["hi"] = "かがみをつかう" },
            ["Axis"] = new() { ["en"] = "Axis", ["ja"] = "軸", ["hi"] = "じく" },
            ["PlaneOffset"] = new() { ["en"] = "Plane Offset", ["ja"] = "平面オフセット", ["hi"] = "へいめんずれ" },
            ["ResetOffset"] = new() { ["en"] = "Reset Offset", ["ja"] = "リセット", ["hi"] = "もどす" },
            ["BackfaceCulling"] = new() { ["en"] = "Backface Culling", ["ja"] = "背面カリング", ["hi"] = "うらめんかくす" },

            // トランスフォーム表示
            ["TransformDisplay"] = new() { ["en"] = "Transform Display", ["ja"] = "トランスフォーム表示", ["hi"] = "へんけいひょうじ" },
            ["ShowLocalTransform"] = new() { ["en"] = "Local Transform", ["ja"] = "ローカル変換", ["hi"] = "ろーかるへんかん" },
            ["ShowWorldTransform"] = new() { ["en"] = "World Transform", ["ja"] = "ワールド変換", ["hi"] = "わーるどへんかん" },

            ["DisplayOptions"] = new() { ["en"] = "Display Options", ["ja"] = "表示オプション", ["hi"] = "ひょうじせってい" },
            ["MirrorMesh"] = new() { ["en"] = "Mirror Mesh", ["ja"] = "ミラーメッシュ", ["hi"] = "かがみめっしゅ" },
            ["MirrorWireframe"] = new() { ["en"] = "Mirror Wireframe", ["ja"] = "ミラーワイヤー", ["hi"] = "かがみわいやー" },
            ["SymmetryPlane"] = new() { ["en"] = "Symmetry Plane", ["ja"] = "対称平面", ["hi"] = "たいしょうめん" },
            ["MirrorAlpha"] = new() { ["en"] = "Mirror Alpha", ["ja"] = "ミラー透明度", ["hi"] = "かがみのとうめいど" },
            
            // ============================================================
            // Primitiveセクション
            // ============================================================
            ["EmptyMesh"] = new() { ["en"] = "+ Empty Mesh", ["ja"] = "+ 空メッシュ", ["hi"] = "+ からっぽ" },
            ["ClearAll"] = new() { ["en"] = "Clear All", ["ja"] = "全削除", ["hi"] = "ぜんぶけす" },
            ["LoadMesh"] = new() { ["en"] = "Load Mesh", ["ja"] = "メッシュ読込", ["hi"] = "よみこむ" },
            ["FromAsset"] = new() { ["en"] = "From Mesh Asset...", ["ja"] = "アセットから...", ["hi"] = "あせっとから..." },
            ["FromPrefab"] = new() { ["en"] = "From Prefab...", ["ja"] = "プレハブから...", ["hi"] = "ぷれはぶから..." },
            ["FromSelection"] = new() { ["en"] = "From Selection", ["ja"] = "選択から", ["hi"] = "せんたくから" },
            ["FromHierarchy"] = new() { ["en"] = "From Hierarchy", ["ja"] = "ヒエラルキーから", ["hi"] = "ひえらるきーから" },
            ["CreateMesh"] = new() { ["en"] = "Create Mesh", ["ja"] = "メッシュ作成", ["hi"] = "つくる" },
            ["AddToCurrent"] = new() { ["en"] = "Add to Current", ["ja"] = "現在に追加", ["hi"] = "いまのにたす" },
            ["AutoMerge"] = new() { ["en"] = "Auto Merge", ["ja"] = "自動マージ", ["hi"] = "じどうがったい" },
            ["NoMeshSelected"] = new() { ["en"] = "(No mesh selected)", ["ja"] = "(メッシュ未選択)", ["hi"] = "(えらんでない)" },
            
            // ============================================================
            // Selectionセクション
            // ============================================================
            ["Selected"] = new() { ["en"] = "Selected", ["ja"] = "選択中", ["hi"] = "えらんでる" },
            ["All"] = new() { ["en"] = "All", ["ja"] = "全て", ["hi"] = "ぜんぶ" },
            ["None"] = new() { ["en"] = "None", ["ja"] = "なし", ["hi"] = "なし" },
            ["Invert"] = new() { ["en"] = "Invert", ["ja"] = "反転", ["hi"] = "はんたい" },
            ["DeleteSelected"] = new() { ["en"] = "Delete Selected", ["ja"] = "選択を削除", ["hi"] = "けす" },
            
            // ============================================================
            // ボタン共通
            // ============================================================
            ["Undo"] = new() { ["en"] = "↶ Undo", ["ja"] = "↶ 元に戻す", ["hi"] = "↶ もどす" },
            ["Redo"] = new() { ["en"] = "Redo ↷", ["ja"] = "やり直し ↷", ["hi"] = "やりなおし ↷" },
            ["Reset"] = new() { ["en"] = "Reset", ["ja"] = "リセット", ["hi"] = "もどす" },
            ["ResetToOriginal"] = new() { ["en"] = "Reset to Original", ["ja"] = "元に戻す", ["hi"] = "さいしょにもどす" },
            ["Apply"] = new() { ["en"] = "Apply", ["ja"] = "適用", ["hi"] = "てきよう" },
            ["Cancel"] = new() { ["en"] = "Cancel", ["ja"] = "キャンセル", ["hi"] = "やめる" },
            ["OK"] = new() { ["en"] = "OK", ["ja"] = "OK", ["hi"] = "OK" },
            ["Delete"] = new() { ["en"] = "Delete", ["ja"] = "削除", ["hi"] = "けす" },
            
            // ============================================================
            // 保存セクション
            // ============================================================
            ["SaveMeshAsset"] = new() { ["en"] = "Save Mesh Asset...", ["ja"] = "メッシュを保存...", ["hi"] = "めっしゅをほぞん..." },
            ["SaveAsPrefab"] = new() { ["en"] = "Save as Prefab...", ["ja"] = "プレハブで保存...", ["hi"] = "ぷれはぶでほぞん..." },
            ["AddToHierarchy"] = new() { ["en"] = "Add to Hierarchy", ["ja"] = "ヒエラルキーに追加", ["hi"] = "シーンについか" },
            ["OverwriteToHierarchy"] = new() { ["en"] = "Overwrite to Hierarchy", ["ja"] = "ヒエラルキーに上書き", ["hi"] = "うわがき" },
            ["ExportModel"] = new() { ["en"] = "Save...", ["ja"] = "保存...", ["hi"] = "ほぞん..." },
            ["ImportModel"] = new() { ["en"] = "Open...", ["ja"] = "開く...", ["hi"] = "ひらく..." },
            ["ExportSelectedMeshOnly"] = new() { ["en"] = "Selected Mesh Only", ["ja"] = "選択メッシュのみ", ["hi"] = "えらんだめっしゅだけ" },
            ["BakeMirror"] = new() { ["en"] = "Bake Symmetry", ["ja"] = "対称をベイク", ["hi"] = "たいしょうをやきつけ" },
            ["BakeBlendShapes"] = new() { ["en"] = "Bake BlendShapes", ["ja"] = "BlendShapeを焼き込む", ["hi"] = "ぶれんどしぇいぷをやきこむ" },
            ["MirrorFlipU"] = new() { ["en"] = "Flip UV U", ["ja"] = "UV U反転", ["hi"] = "UV Uはんてん" },
            ["CreateArmatureMeshesFolder"] = new() { ["en"] = "Create Armature/Meshes Folder", ["ja"] = "Armature/Meshesフォルダ作成", ["hi"] = "フォルダをつくる" },
            ["ExportAsSkinned"] = new() { ["en"] = "Export as SkinnedMesh", ["ja"] = "スキンメッシュとしてエクスポート", ["hi"] = "スキンメッシュ" },
            ["AddAnimatorComponent"] = new() { ["en"] = "Add Animator", ["ja"] = "Animator追加", ["hi"] = "あにめーたーついか" },
            ["CreateAvatarOnExport"] = new() { ["en"] = "Create Avatar", ["ja"] = "Avatar作成", ["hi"] = "あばたーつくる" },
            ["NoHumanoidMapping"] = new() { ["en"] = "Set Humanoid Mapping first", ["ja"] = "Humanoidマッピングを設定してください", ["hi"] = "まっぴんぐをせってい" },
            ["SaveSection"] = new() { ["en"] = "Save / Export", ["ja"] = "保存 / エクスポート", ["hi"] = "ほぞん" },
            
            // ============================================================
            // ツール名（キーは "Tool_" + IEditTool.Name）
            // ============================================================
            ["Tool_Select"] = new() { ["en"] = "Select", ["ja"] = "選択", ["hi"] = "えらぶ" },
            ["Tool_SelectAdvanced"] = new() { ["en"] = "Adv.Select", ["ja"] = "詳細選択", ["hi"] = "くわしくえらぶ" },
            ["Tool_Move"] = new() { ["en"] = "Move", ["ja"] = "移動", ["hi"] = "うごかす" },
            ["Tool_Rotate"] = new() { ["en"] = "Rotate", ["ja"] = "回転", ["hi"] = "かいてん" },
            ["Tool_Scale"] = new() { ["en"] = "Scale", ["ja"] = "拡大縮小", ["hi"] = "おおきさ" },
            ["Tool_Sculpt"] = new() { ["en"] = "Sculpt", ["ja"] = "スカルプト", ["hi"] = "こねる" },
            ["Tool_Add Face"] = new() { ["en"] = "Add Face", ["ja"] = "面追加", ["hi"] = "めんをたす" },
            ["Tool_Knife"] = new() { ["en"] = "Knife", ["ja"] = "ナイフ", ["hi"] = "きる" },
            ["Tool_EdgeTopo"] = new() { ["en"] = "Edge Topo", ["ja"] = "辺トポロジ", ["hi"] = "へんへんしゅう" },
            ["Tool_Merge"] = new() { ["en"] = "Merge", ["ja"] = "マージ", ["hi"] = "がったい" },
            ["Tool_Extrude"] = new() { ["en"] = "Edge Extrude", ["ja"] = "面張り", ["hi"] = "めんはり" },
            ["Tool_Push"] = new() { ["en"] = "Face Extrude", ["ja"] = "押し出し", ["hi"] = "おしだし" },
            ["Tool_Bevel"] = new() { ["en"] = "Bevel", ["ja"] = "ベベル", ["hi"] = "べべる" },
            ["Tool_Flip"] = new() { ["en"] = "Flip", ["ja"] = "面反転", ["hi"] = "めんはんてん" },
            ["Tool_Line Extrude"] = new() { ["en"] = "Line Extr.", ["ja"] = "線押出", ["hi"] = "せんおしだし" },
            ["Tool_Pivot Offset"] = new() { ["en"] = "Pivot", ["ja"] = "ピボット", ["hi"] = "ちゅうしん" },
            ["Tool_Primitive"] = new() { ["en"] = "Primitive", ["ja"] = "図形生成", ["hi"] = "ずけいをつくる" },
            // ============================================================
            // ツール名（キーは "Tool_" + IEditTool.Name）
            // ============================================================
            ["ToolPanels"] = new() { ["en"] = "Tool Panels", ["ja"] = "ツールパネル", ["hi"] = "つーるぱねる" },
            // ============================================================
            // メッシュ情報
            // ============================================================
            ["Vertices"] = new() { ["en"] = "Vertices", ["ja"] = "頂点", ["hi"] = "てん" },
            ["Faces"] = new() { ["en"] = "Faces", ["ja"] = "面", ["hi"] = "めん" },
            ["Triangles"] = new() { ["en"] = "Triangles", ["ja"] = "三角形", ["hi"] = "さんかく" },
            ["Tri"] = new() { ["en"] = "Tri", ["ja"] = "三角", ["hi"] = "さんかく" },
            ["Quad"] = new() { ["en"] = "Quad", ["ja"] = "四角", ["hi"] = "しかく" },
            ["NGon"] = new() { ["en"] = "NGon", ["ja"] = "多角形", ["hi"] = "たかく" },
            
            // ============================================================
            // メッセージ
            // ============================================================
            ["SelectMesh"] = new() { ["en"] = "Select a mesh", ["ja"] = "メッシュを選択してください", ["hi"] = "めっしゅをえらんでね" },
            ["InvalidMeshData"] = new() { ["en"] = "Invalid MeshObject", ["ja"] = "MeshDataが無効です", ["hi"] = "データがおかしいよ" },
            
            // ============================================================
            // BoneTransform
            // ============================================================
            ["BoneTransform"] = new() { ["en"] = "Apply Local Transform", ["ja"] = "ローカルトランスフォームを反映", ["hi"] = "ろーかるとらんすふぉーむをはんえい" },
            ["UseLocalTransform"] = new() { ["en"] = "Use Local Transform", ["ja"] = "ローカル変換を使用", ["hi"] = "ローカルへんかん" },
            ["ExportAsSkinned"] = new() { ["en"] = "Export as Skinned", ["ja"] = "スキンメッシュで出力", ["hi"] = "すきんめっしゅでだす" },
            ["Position"] = new() { ["en"] = "Position", ["ja"] = "位置", ["hi"] = "いち" },
            ["Rotation"] = new() { ["en"] = "Rotation", ["ja"] = "回転", ["hi"] = "かいてん" },
            ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール", ["hi"] = "おおきさ" },
        };
        
        // ================================================================
        // ツール/ウィンドウ用ヘルパー
        // ================================================================
        
        /// <summary>
        /// ツールの表示名を取得（フォールバック付き）
        /// 優先度: ツール自身のローカライズ → 共通辞書 → DisplayName
        /// </summary>
        public static string GetToolName(IEditTool tool)
        {
            if (tool == null) return "";
            
            // 1. ツール自身のローカライズ
            var localized = tool.GetLocalizedDisplayName();
            if (!string.IsNullOrEmpty(localized))
                return localized;
            
            // 2. 共通辞書（Tool_XXX形式）
            string key = "Tool_" + tool.Name;
            string fromDict = Get(key);
            if (fromDict != key)  // キーと異なる = 辞書に存在
                return fromDict;
            
            // 3. フォールバック: DisplayName
            return tool.DisplayName;
        }
        
        /// <summary>
        /// ウィンドウのタイトルを取得（フォールバック付き）
        /// 優先度: ウィンドウ自身のローカライズ → 共通辞書 → Title
        /// </summary>
        public static string GetWindowTitle(IToolPanel toolPanel)
        {
            if (toolPanel == null) return "";
            
            // 1. ウィンドウ自身のローカライズ
            var localized = toolPanel.GetLocalizedTitle();
            if (!string.IsNullOrEmpty(localized))
                return localized;
            
            // 2. 共通辞書（Window_XXX形式）
            string key = "Window_" + toolPanel.Name;
            string fromDict = Get(key);
            if (fromDict != key)  // キーと異なる = 辞書に存在
                return fromDict;
            
            // 3. フォールバック: Title
            return toolPanel.Title;
        }
        
        // ================================================================
        // 動的テキスト生成
        // ================================================================
        
        /// <summary>
        /// 「選択中: X / Y」形式のテキストを生成
        /// </summary>
        public static string GetSelectedCount(int selected, int total)
        {
            string label = Get("Selected");
            return $"{label}: {selected} / {total}";
        }
        
        /// <summary>
        /// メッシュ情報テキストを生成
        /// </summary>
        public static string GetMeshInfo(int vertices, int faces, int triangles)
        {
            return $"{Get("Vertices")}: {vertices}\n{Get("Faces")}: {faces}\n{Get("Triangles")}: {triangles}";
        }
    }
}
