// TextureExtractorPanel.cs
// MQO/PMXファイルから参照されているテクスチャのみを抽出・コピーするエディタ拡張
// 不要なテクスチャや作業中ファイルを除外した公開用フォルダを作成

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// テクスチャ抽出パネル
    /// </summary>
    public class TextureExtractorPanel : EditorWindow
    {
        // ================================================================
        // 定数
        // ================================================================

        private static readonly string[] SupportedExtensions = { ".mqo", ".pmx" };
        private static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".gif", ".psd", ".tiff", ".tif" };

        // ================================================================
        // 設定
        // ================================================================

        private string _sourceFilePath = "";
        private string _outputFolder = "";
        private bool _preserveSubfolders = true;
        private bool _overwriteExisting = false;
        private bool _createLog = true;

        // ================================================================
        // 状態
        // ================================================================

        private List<TextureEntry> _textureEntries = new List<TextureEntry>();
        private Vector2 _scrollPosition;
        private string _lastError = "";
        private string _lastResult = "";
        private bool _analyzed = false;

        // ================================================================
        // エントリ構造
        // ================================================================

        private class TextureEntry
        {
            public string RelativePath;      // ファイル内での相対パス
            public string AbsolutePath;      // 実際のファイルパス
            public string OutputPath;        // コピー先パス
            public bool Exists;              // ファイルが存在するか
            public bool Selected;            // コピー対象か
            public long FileSize;            // ファイルサイズ
            public string UsedBy;            // 使用箇所（マテリアル名等）
        }

        // ================================================================
        // メニュー
        // ================================================================

        [MenuItem("Tools/Poly_Ling/Utility/Texture Extractor")]
        public static void ShowWindow()
        {
            var window = GetWindow<TextureExtractorPanel>("Texture Extractor");
            window.minSize = new Vector2(500, 400);
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("テクスチャ抽出ツール", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "MQO/PMXファイルから参照されているテクスチャのみを抽出し、指定フォルダにコピーします。\n" +
                "不要なテクスチャや作業中ファイルを除外した公開用フォルダを作成できます。",
                MessageType.Info);

            EditorGUILayout.Space(10);

            // ソースファイル
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ソースファイル", GUILayout.Width(100));
            _sourceFilePath = EditorGUILayout.TextField(_sourceFilePath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string path = EditorUtility.OpenFilePanel(
                    "MQO/PMXファイルを選択",
                    string.IsNullOrEmpty(_sourceFilePath) ? "" : Path.GetDirectoryName(_sourceFilePath),
                    "mqo,pmx");
                if (!string.IsNullOrEmpty(path))
                {
                    _sourceFilePath = path;
                    _analyzed = false;
                    _textureEntries.Clear();
                }
            }
            EditorGUILayout.EndHorizontal();

            // 出力フォルダ
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("出力フォルダ", GUILayout.Width(100));
            _outputFolder = EditorGUILayout.TextField(_outputFolder);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string defaultPath = string.IsNullOrEmpty(_outputFolder) 
                    ? (string.IsNullOrEmpty(_sourceFilePath) ? "" : Path.GetDirectoryName(_sourceFilePath))
                    : _outputFolder;
                string path = EditorUtility.OpenFolderPanel("出力フォルダを選択", defaultPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    _outputFolder = path;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // オプション
            _preserveSubfolders = EditorGUILayout.Toggle("サブフォルダ構造を維持", _preserveSubfolders);
            _overwriteExisting = EditorGUILayout.Toggle("既存ファイルを上書き", _overwriteExisting);
            _createLog = EditorGUILayout.Toggle("ログファイルを作成", _createLog);

            EditorGUILayout.Space(10);

            // 解析ボタン
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_sourceFilePath));
            if (GUILayout.Button("解析", GUILayout.Height(30)))
            {
                AnalyzeFile();
            }
            EditorGUI.EndDisabledGroup();

            // エラー表示
            if (!string.IsNullOrEmpty(_lastError))
            {
                EditorGUILayout.HelpBox(_lastError, MessageType.Error);
            }

            // 結果表示
            if (_analyzed && _textureEntries.Count > 0)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField($"検出されたテクスチャ: {_textureEntries.Count}件", EditorStyles.boldLabel);

                // 選択操作
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("全て選択", GUILayout.Width(80)))
                {
                    foreach (var e in _textureEntries) e.Selected = true;
                }
                if (GUILayout.Button("全て解除", GUILayout.Width(80)))
                {
                    foreach (var e in _textureEntries) e.Selected = false;
                }
                if (GUILayout.Button("存在するもののみ", GUILayout.Width(120)))
                {
                    foreach (var e in _textureEntries) e.Selected = e.Exists;
                }

                int selectedCount = _textureEntries.Count(e => e.Selected);
                long totalSize = _textureEntries.Where(e => e.Selected && e.Exists).Sum(e => e.FileSize);
                EditorGUILayout.LabelField($"選択: {selectedCount}件 ({FormatFileSize(totalSize)})", GUILayout.Width(150));
                EditorGUILayout.EndHorizontal();

                // テクスチャリスト
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));
                foreach (var entry in _textureEntries)
                {
                    EditorGUILayout.BeginHorizontal();

                    // 存在しないファイルは赤く
                    var oldColor = GUI.color;
                    if (!entry.Exists)
                    {
                        GUI.color = Color.red;
                    }

                    entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));
                    EditorGUILayout.LabelField(entry.RelativePath, GUILayout.MinWidth(200));

                    GUI.color = oldColor;

                    if (entry.Exists)
                    {
                        EditorGUILayout.LabelField(FormatFileSize(entry.FileSize), GUILayout.Width(70));
                    }
                    else
                    {
                        EditorGUILayout.LabelField("見つからない", GUILayout.Width(70));
                    }

                    if (!string.IsNullOrEmpty(entry.UsedBy))
                    {
                        EditorGUILayout.LabelField(entry.UsedBy, GUILayout.Width(100));
                    }

                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(10);

                // コピーボタン
                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_outputFolder) || selectedCount == 0);
                if (GUILayout.Button("選択したテクスチャをコピー", GUILayout.Height(30)))
                {
                    CopyTextures();
                }
                EditorGUI.EndDisabledGroup();
            }
            else if (_analyzed)
            {
                EditorGUILayout.HelpBox("テクスチャが見つかりませんでした。", MessageType.Warning);
            }

            // 結果メッセージ
            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(_lastResult, MessageType.Info);
            }
        }

        // ================================================================
        // 解析
        // ================================================================

        private void AnalyzeFile()
        {
            _lastError = "";
            _lastResult = "";
            _textureEntries.Clear();
            _analyzed = false;

            if (!File.Exists(_sourceFilePath))
            {
                _lastError = "ファイルが見つかりません。";
                return;
            }

            string ext = Path.GetExtension(_sourceFilePath).ToLowerInvariant();
            string sourceDir = Path.GetDirectoryName(_sourceFilePath);

            try
            {
                List<(string relativePath, string usedBy)> textures;

                if (ext == ".mqo")
                {
                    textures = ParseMQOTextures(_sourceFilePath);
                }
                else if (ext == ".pmx")
                {
                    textures = ParsePMXTextures(_sourceFilePath);
                }
                else
                {
                    _lastError = $"未対応のファイル形式: {ext}";
                    return;
                }

                // 重複を除去しつつエントリ作成
                var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (relativePath, usedBy) in textures)
                {
                    if (string.IsNullOrEmpty(relativePath))
                        continue;

                    // 正規化
                    string normalized = NormalizePath(relativePath);
                    
                    if (uniquePaths.Contains(normalized))
                        continue;

                    uniquePaths.Add(normalized);

                    string absolutePath = Path.Combine(sourceDir, normalized);
                    bool exists = File.Exists(absolutePath);
                    long fileSize = exists ? new FileInfo(absolutePath).Length : 0;

                    string outputPath = _preserveSubfolders
                        ? Path.Combine(_outputFolder, normalized)
                        : Path.Combine(_outputFolder, Path.GetFileName(normalized));

                    _textureEntries.Add(new TextureEntry
                    {
                        RelativePath = normalized,
                        AbsolutePath = absolutePath,
                        OutputPath = outputPath,
                        Exists = exists,
                        Selected = exists,  // デフォルトで存在するもののみ選択
                        FileSize = fileSize,
                        UsedBy = usedBy
                    });
                }

                _analyzed = true;
            }
            catch (Exception ex)
            {
                _lastError = $"解析エラー: {ex.Message}";
            }
        }

        // ================================================================
        // MQO解析
        // ================================================================

        private List<(string, string)> ParseMQOTextures(string filePath)
        {
            var result = new List<(string, string)>();

            // エンコーディング検出（Shift-JISまたはUTF-8）
            Encoding encoding = DetectEncoding(filePath);
            string content = File.ReadAllText(filePath, encoding);

            // tex("パス") パターンを検索
            var texPattern = new Regex(@"tex\s*\(\s*""([^""]+)""\s*\)", RegexOptions.IgnoreCase);
            var matches = texPattern.Matches(content);

            string currentMaterial = "";

            // マテリアル名も取得
            var materialPattern = new Regex(@"""([^""]+)""\s*\{[^}]*?tex\s*\(\s*""([^""]+)""\s*\)", RegexOptions.Singleline);
            var matMatches = materialPattern.Matches(content);

            foreach (Match m in matMatches)
            {
                string matName = m.Groups[1].Value;
                string texPath = m.Groups[2].Value;
                result.Add((texPath, matName));
            }

            // マテリアル外のテクスチャも取得（シンプルパターン）
            foreach (Match m in matches)
            {
                string texPath = m.Groups[1].Value;
                if (!result.Any(r => r.Item1.Equals(texPath, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add((texPath, ""));
                }
            }

            return result;
        }

        // ================================================================
        // PMX解析
        // ================================================================

        private List<(string, string)> ParsePMXTextures(string filePath)
        {
            var result = new List<(string, string)>();

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                // PMXヘッダー確認
                byte[] magic = reader.ReadBytes(4);
                string magicStr = Encoding.ASCII.GetString(magic);

                if (magicStr != "PMX ")
                {
                    throw new Exception("PMXファイルではありません。");
                }

                float version = reader.ReadSingle();

                // グローバル設定
                byte globalsCount = reader.ReadByte();
                byte[] globals = reader.ReadBytes(globalsCount);

                if (globalsCount < 1)
                {
                    throw new Exception("不正なPMXフォーマット");
                }

                byte textEncoding = globals[0]; // 0=UTF16LE, 1=UTF8
                Encoding enc = textEncoding == 0 ? Encoding.Unicode : Encoding.UTF8;

                // インデックスサイズ
                byte vertexIndexSize = globalsCount > 2 ? globals[2] : (byte)4;
                byte textureIndexSize = globalsCount > 3 ? globals[3] : (byte)4;
                byte materialIndexSize = globalsCount > 4 ? globals[4] : (byte)4;
                byte boneIndexSize = globalsCount > 5 ? globals[5] : (byte)4;
                byte morphIndexSize = globalsCount > 6 ? globals[6] : (byte)4;
                byte rigidIndexSize = globalsCount > 7 ? globals[7] : (byte)4;

                // モデル名等をスキップ
                SkipPMXString(reader, enc); // モデル名
                SkipPMXString(reader, enc); // モデル名(英)
                SkipPMXString(reader, enc); // コメント
                SkipPMXString(reader, enc); // コメント(英)

                // 頂点をスキップ
                int vertexCount = reader.ReadInt32();
                for (int i = 0; i < vertexCount; i++)
                {
                    SkipPMXVertex(reader, boneIndexSize);
                }

                // 面をスキップ
                int faceIndexCount = reader.ReadInt32();
                reader.ReadBytes(faceIndexCount * vertexIndexSize);

                // テクスチャリスト読み取り
                int textureCount = reader.ReadInt32();
                var texturePaths = new List<string>();

                for (int i = 0; i < textureCount; i++)
                {
                    string texPath = ReadPMXString(reader, enc);
                    texturePaths.Add(texPath);
                    result.Add((texPath, $"tex[{i}]"));
                }

                // マテリアル読み取り（テクスチャ参照確認用）
                int materialCount = reader.ReadInt32();
                for (int i = 0; i < materialCount; i++)
                {
                    string matName = ReadPMXString(reader, enc);
                    SkipPMXString(reader, enc); // 英語名

                    // マテリアルデータスキップ
                    reader.ReadBytes(4 * 4); // diffuse
                    reader.ReadBytes(4 * 3); // specular
                    reader.ReadBytes(4);     // specularity
                    reader.ReadBytes(4 * 3); // ambient
                    reader.ReadByte();       // flags
                    reader.ReadBytes(4 * 4); // edge color
                    reader.ReadBytes(4);     // edge size

                    int texIndex = ReadPMXIndex(reader, textureIndexSize);
                    int sphereIndex = ReadPMXIndex(reader, textureIndexSize);
                    reader.ReadByte();       // sphere mode
                    byte toonFlag = reader.ReadByte();

                    if (toonFlag == 0)
                    {
                        int toonIndex = ReadPMXIndex(reader, textureIndexSize);
                    }
                    else
                    {
                        reader.ReadByte(); // shared toon
                    }

                    SkipPMXString(reader, enc); // memo
                    reader.ReadInt32(); // face count

                    // マテリアル名でUsedByを更新
                    if (texIndex >= 0 && texIndex < result.Count)
                    {
                        var entry = result[texIndex];
                        result[texIndex] = (entry.Item1, matName);
                    }
                }
            }

            return result;
        }

        // ================================================================
        // PMXヘルパー
        // ================================================================

        private string ReadPMXString(BinaryReader reader, Encoding enc)
        {
            int len = reader.ReadInt32();
            if (len <= 0) return "";
            byte[] data = reader.ReadBytes(len);
            return enc.GetString(data);
        }

        private void SkipPMXString(BinaryReader reader, Encoding enc)
        {
            int len = reader.ReadInt32();
            if (len > 0) reader.ReadBytes(len);
        }

        private int ReadPMXIndex(BinaryReader reader, byte size)
        {
            switch (size)
            {
                case 1: return reader.ReadSByte();
                case 2: return reader.ReadInt16();
                case 4: return reader.ReadInt32();
                default: return reader.ReadInt32();
            }
        }

        private void SkipPMXVertex(BinaryReader reader, byte boneIndexSize)
        {
            reader.ReadBytes(4 * 3); // position
            reader.ReadBytes(4 * 3); // normal
            reader.ReadBytes(4 * 2); // uv
            // 追加UV（globals[1]による）- 簡略化のためスキップ
            // reader.ReadBytes(4 * 4 * additionalUVCount);

            byte weightType = reader.ReadByte();
            switch (weightType)
            {
                case 0: // BDEF1
                    reader.ReadBytes(boneIndexSize);
                    break;
                case 1: // BDEF2
                    reader.ReadBytes(boneIndexSize * 2);
                    reader.ReadBytes(4);
                    break;
                case 2: // BDEF4
                    reader.ReadBytes(boneIndexSize * 4);
                    reader.ReadBytes(4 * 4);
                    break;
                case 3: // SDEF
                    reader.ReadBytes(boneIndexSize * 2);
                    reader.ReadBytes(4);
                    reader.ReadBytes(4 * 3 * 3);
                    break;
                case 4: // QDEF
                    reader.ReadBytes(boneIndexSize * 4);
                    reader.ReadBytes(4 * 4);
                    break;
            }

            reader.ReadBytes(4); // edge scale
        }

        // ================================================================
        // コピー
        // ================================================================

        private void CopyTextures()
        {
            _lastError = "";
            _lastResult = "";

            var selectedEntries = _textureEntries.Where(e => e.Selected && e.Exists).ToList();
            if (selectedEntries.Count == 0)
            {
                _lastError = "コピー対象がありません。";
                return;
            }

            int copiedCount = 0;
            int skippedCount = 0;
            var errors = new List<string>();
            var logLines = new List<string>();

            logLines.Add($"# Texture Extraction Log");
            logLines.Add($"# Source: {_sourceFilePath}");
            logLines.Add($"# Output: {_outputFolder}");
            logLines.Add($"# Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            logLines.Add("");

            foreach (var entry in selectedEntries)
            {
                try
                {
                    // 出力パス計算
                    string outputPath = _preserveSubfolders
                        ? Path.Combine(_outputFolder, entry.RelativePath)
                        : Path.Combine(_outputFolder, Path.GetFileName(entry.RelativePath));

                    // ディレクトリ作成
                    string outputDir = Path.GetDirectoryName(outputPath);
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // 既存ファイルチェック
                    if (File.Exists(outputPath) && !_overwriteExisting)
                    {
                        skippedCount++;
                        logLines.Add($"SKIP: {entry.RelativePath} (already exists)");
                        continue;
                    }

                    // コピー
                    File.Copy(entry.AbsolutePath, outputPath, _overwriteExisting);
                    copiedCount++;
                    logLines.Add($"COPY: {entry.RelativePath}");
                }
                catch (Exception ex)
                {
                    errors.Add($"{entry.RelativePath}: {ex.Message}");
                    logLines.Add($"ERROR: {entry.RelativePath} - {ex.Message}");
                }
            }

            // ログ保存
            if (_createLog)
            {
                string logPath = Path.Combine(_outputFolder, "texture_extraction.log");
                File.WriteAllLines(logPath, logLines);
            }

            // 結果メッセージ
            _lastResult = $"コピー完了: {copiedCount}件";
            if (skippedCount > 0)
            {
                _lastResult += $", スキップ: {skippedCount}件";
            }
            if (errors.Count > 0)
            {
                _lastResult += $", エラー: {errors.Count}件";
                _lastError = string.Join("\n", errors.Take(5));
            }
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        private string NormalizePath(string path)
        {
            // バックスラッシュをスラッシュに
            path = path.Replace('\\', '/');

            // 先頭の./を除去
            if (path.StartsWith("./"))
            {
                path = path.Substring(2);
            }

            return path;
        }

        private Encoding DetectEncoding(string filePath)
        {
            // BOMチェック
            byte[] bom = new byte[4];
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                fs.Read(bom, 0, 4);
            }

            if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return Encoding.UTF8;
            if (bom[0] == 0xFF && bom[1] == 0xFE)
                return Encoding.Unicode;
            if (bom[0] == 0xFE && bom[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            // デフォルトはShift-JIS（MQO用）
            try
            {
                return Encoding.GetEncoding("shift_jis");
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
