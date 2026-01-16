// Assets/Editor/Poly_Ling/Tools/MirrorEdit/MirrorEditTool.cs
// ミラー対称化編集ツール
// - Step 1: Bake Mirror（ミラー実体化）
// - Step 2: 編集（既存ツール使用）
// - Step 3: Write Back（書き戻し）
// - Step 4: Blend（オリジナルと書き戻しをブレンド）
// - クリーンアップ機能（中間メッシュの削除）

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// ミラー対称化編集ツール
    /// </summary>
    public partial class MirrorEditTool : IEditTool
    {
        // ================================================================
        // IEditTool 実装
        // ================================================================

        public string Name => "MirrorEdit";
        public string DisplayName => "Mirror Edit";
        public IToolSettings Settings => _settings;

        private MirrorEditSettings _settings = new MirrorEditSettings();
        private ToolContext _context;

        // ================================================================
        // 状態
        // ================================================================

        private string _statusMessage = "";
        private MessageType _statusType = MessageType.Info;

        // ワークフロー状態
        private MirrorBakeResult _lastBakeResult;
        private string _sourceMeshName;      // オリジナルメッシュ名
        private string _bakedMeshName;       // ベイクしたメッシュ名
        private string _writeBackMeshName;   // 書き戻したメッシュ名

        // ブレンドプレビュー用
        private float _blendWeight = 0.5f;
        private bool _isBlendPreview = false;

        // 最後に生成したメッシュ（モーフ登録用）
        private string _lastGeneratedMeshName;

        // モーフ登録用
        private string _morphName = "MirrorMorph";

        // ================================================================
        // IEditTool マウスイベント（このツールは使用しない）
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos) => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) => false;
        public void DrawGizmo(ToolContext ctx) { }

        // ================================================================
        // アクティベーション
        // ================================================================

        public void OnActivate(ToolContext ctx)
        {
            _context = ctx;
            _statusMessage = T("SelectMeshToBake");
            _statusType = MessageType.Info;
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _context = null;
        }

        public void Reset()
        {
            _statusMessage = "";
            _lastBakeResult = null;
            _sourceMeshName = null;
            _bakedMeshName = null;
            _writeBackMeshName = null;
            _blendWeight = 0.5f;
            _isBlendPreview = false;
            _lastGeneratedMeshName = null;
            _morphName = "MirrorMorph";
        }

        // ================================================================
        // 設定UI
        // ================================================================

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(10);

            // ================================================================
            // Step 1: Bake Mirror
            // ================================================================
            DrawSectionHeader("Step1_BakeMirror");

            // ミラー軸
            _settings.MirrorAxis = EditorGUILayout.Popup(T("MirrorAxis"), _settings.MirrorAxis,
                new string[] { "X", "Y", "Z" });

            // 境界閾値
            _settings.Threshold = EditorGUILayout.FloatField(T("Threshold"), _settings.Threshold);
            if (_settings.Threshold < 0.00001f) _settings.Threshold = 0.00001f;

            // UV反転
            _settings.FlipU = EditorGUILayout.Toggle(T("FlipU"), _settings.FlipU);

            EditorGUILayout.Space(5);

            // ベイクボタン
            using (new EditorGUI.DisabledScope(_context?.CurrentMeshContent == null))
            {
                if (GUILayout.Button(T("BakeMirror"), GUILayout.Height(28)))
                {
                    ExecuteBakeMirror();
                }
            }

            EditorGUILayout.Space(15);

            // ================================================================
            // Step 2: Edit（情報表示のみ）
            // ================================================================
            DrawSectionHeader("Step2_Edit");
            EditorGUILayout.HelpBox(T("EditHelp"), MessageType.None);

            EditorGUILayout.Space(15);

            // ================================================================
            // Step 3: Write Back
            // ================================================================
            DrawSectionHeader("Step3_WriteBack");

            // 書き戻しモード
            _settings.WriteBackMode = (WriteBackMode)EditorGUILayout.EnumPopup(
                T("WriteBackMode"), _settings.WriteBackMode);

            EditorGUILayout.Space(5);

            // 書き戻しボタン
            bool canWriteBack = _lastBakeResult != null &&
                                _context?.CurrentMeshContent != null &&
                                _context.CurrentMeshContent.Name == _bakedMeshName;

            using (new EditorGUI.DisabledScope(!canWriteBack))
            {
                if (GUILayout.Button(T("WriteBack"), GUILayout.Height(28)))
                {
                    ExecuteWriteBack();
                }
            }

            EditorGUILayout.Space(15);

            // ================================================================
            // Step 4: Blend（オリジナルと書き戻しをブレンド）
            // ================================================================
            DrawSectionHeader("Step4_Blend");

            bool canBlend = !string.IsNullOrEmpty(_sourceMeshName) &&
                            !string.IsNullOrEmpty(_writeBackMeshName) &&
                            FindMeshContextByName(_sourceMeshName) != null &&
                            FindMeshContextByName(_writeBackMeshName) != null;

            using (new EditorGUI.DisabledScope(!canBlend))
            {
                EditorGUILayout.LabelField(T("BlendSource"), _sourceMeshName ?? "-");
                EditorGUILayout.LabelField(T("BlendTarget"), _writeBackMeshName ?? "-");

                EditorGUILayout.Space(5);

                // ブレンドスライダー
                EditorGUI.BeginChangeCheck();
                _blendWeight = EditorGUILayout.Slider(T("BlendWeight"), _blendWeight, 0f, 1f);
                if (EditorGUI.EndChangeCheck() && _isBlendPreview)
                {
                    // プレビュー更新（将来的にリアルタイムプレビュー対応可能）
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"0% = {_sourceMeshName ?? "Original"}", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"100% = {_writeBackMeshName ?? "WriteBack"}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);

                // ブレンド実行ボタン
                if (GUILayout.Button(T("CreateBlended"), GUILayout.Height(28)))
                {
                    ExecuteBlend();
                }
            }

            if (!canBlend && _lastBakeResult != null)
            {
                EditorGUILayout.HelpBox(T("BlendRequiresBoth"), MessageType.Info);
            }

            EditorGUILayout.Space(15);

            // ================================================================
            // Step 5: Register as Morph（モーフとして登録）
            // ================================================================
            DrawSectionHeader("Step5_RegisterMorph");

            bool canRegisterMorph = !string.IsNullOrEmpty(_lastGeneratedMeshName) &&
                                    !string.IsNullOrEmpty(_sourceMeshName) &&
                                    FindMeshContextByName(_lastGeneratedMeshName) != null &&
                                    FindMeshContextByName(_sourceMeshName) != null;

            using (new EditorGUI.DisabledScope(!canRegisterMorph))
            {
                EditorGUILayout.LabelField(T("LastGenerated"), _lastGeneratedMeshName ?? "-");
                EditorGUILayout.LabelField(T("MorphBase"), _sourceMeshName ?? "-");

                EditorGUILayout.Space(5);

                // モーフ名入力
                _morphName = EditorGUILayout.TextField(T("MorphName"), _morphName);

                // モーフパネル選択（PMX用）
                _settings.MorphPanel = EditorGUILayout.Popup(T("MorphPanel"), _settings.MorphPanel,
                    new string[] { "Brow (0)", "Eye (1)", "Mouth (2)", "Other (3)" });

                EditorGUILayout.Space(5);

                // モーフ登録ボタン
                if (GUILayout.Button(T("RegisterAsMorph"), GUILayout.Height(28)))
                {
                    ExecuteRegisterAsMorph();
                }
            }

            if (!canRegisterMorph)
            {
                EditorGUILayout.HelpBox(T("MorphRequiresGenerated"), MessageType.Info);
            }

            EditorGUILayout.Space(15);

            // ================================================================
            // Cleanup（中間メッシュの削除）
            // ================================================================
            DrawSectionHeader("Cleanup");

            EditorGUILayout.BeginHorizontal();

            // ベイクメッシュ削除
            bool hasBaked = !string.IsNullOrEmpty(_bakedMeshName) &&
                            FindMeshContextByName(_bakedMeshName) != null;
            using (new EditorGUI.DisabledScope(!hasBaked))
            {
                if (GUILayout.Button(T("DeleteBaked")))
                {
                    DeleteMeshByName(_bakedMeshName);
                    _bakedMeshName = null;
                }
            }

            // 書き戻しメッシュ削除
            bool hasWriteBack = !string.IsNullOrEmpty(_writeBackMeshName) &&
                                FindMeshContextByName(_writeBackMeshName) != null;
            using (new EditorGUI.DisabledScope(!hasWriteBack))
            {
                if (GUILayout.Button(T("DeleteWriteBack")))
                {
                    DeleteMeshByName(_writeBackMeshName);
                    _writeBackMeshName = null;
                }
            }

            EditorGUILayout.EndHorizontal();

            // 両方削除
            using (new EditorGUI.DisabledScope(!hasBaked && !hasWriteBack))
            {
                if (GUILayout.Button(T("DeleteBoth")))
                {
                    if (hasBaked)
                    {
                        DeleteMeshByName(_bakedMeshName);
                        _bakedMeshName = null;
                    }
                    if (hasWriteBack)
                    {
                        DeleteMeshByName(_writeBackMeshName);
                        _writeBackMeshName = null;
                    }
                }
            }

            EditorGUILayout.Space(10);

            // ================================================================
            // ワークフロー状態表示
            // ================================================================
            DrawWorkflowStatus();

            // ステータスメッセージ
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }
        }

        // ================================================================
        // ワークフロー状態表示
        // ================================================================

        private void DrawWorkflowStatus()
        {
            EditorGUILayout.Space(5);
            DrawHorizontalLine();
            EditorGUILayout.LabelField(T("WorkflowStatus"), EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                DrawStatusLine("Source", _sourceMeshName);
                DrawStatusLine("Baked", _bakedMeshName);
                DrawStatusLine("WriteBack", _writeBackMeshName);
                DrawStatusLine("LastGen", _lastGeneratedMeshName);
            }
        }

        private void DrawStatusLine(string label, string meshName)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label + ":", GUILayout.Width(80));

            if (string.IsNullOrEmpty(meshName))
            {
                EditorGUILayout.LabelField("-", EditorStyles.miniLabel);
            }
            else
            {
                bool exists = FindMeshContextByName(meshName) != null;
                var style = exists ? EditorStyles.miniLabel : new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.red } };
                string status = exists ? meshName : $"{meshName} (deleted)";
                EditorGUILayout.LabelField(status, style);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSectionHeader(string key)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(T(key), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            DrawHorizontalLine();
        }

        private void DrawHorizontalLine()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }

        // ================================================================
        // ヘルパーメソッド
        // ================================================================

        private MeshContext FindMeshContextByName(string name)
        {
            if (string.IsNullOrEmpty(name) || _context?.Model == null)
                return null;

            for (int i = 0; i < _context.Model.MeshContextCount; i++)
            {
                var mc = _context.Model.GetMeshContext(i);
                if (mc.Name == name)
                    return mc;
            }
            return null;
        }

        private int FindMeshContextIndexByName(string name)
        {
            if (string.IsNullOrEmpty(name) || _context?.Model == null)
                return -1;

            for (int i = 0; i < _context.Model.MeshContextCount; i++)
            {
                var mc = _context.Model.GetMeshContext(i);
                if (mc.Name == name)
                    return i;
            }
            return -1;
        }

        private void DeleteMeshByName(string name)
        {
            int index = FindMeshContextIndexByName(name);
            if (index >= 0)
            {
                _context.RemoveMeshContext?.Invoke(index);
                _context.Repaint?.Invoke();
            }
        }

        // ================================================================
        // 処理実行
        // ================================================================

        /// <summary>
        /// ミラーベイク実行
        /// </summary>
        private void ExecuteBakeMirror()
        {
            if (_context == null || _context.CurrentMeshContent == null)
            {
                _statusMessage = T("NoMeshSelected");
                _statusType = MessageType.Warning;
                return;
            }

            var sourceMeshContext = _context.CurrentMeshContent;
            var sourceMeshObject = sourceMeshContext.MeshObject;

            if (sourceMeshObject == null || sourceMeshObject.VertexCount == 0)
            {
                _statusMessage = T("EmptyMesh");
                _statusType = MessageType.Warning;
                return;
            }

            // ソース名を保存
            _sourceMeshName = sourceMeshContext.Name;

            // ベイク実行
            var (bakedMesh, bakeResult) = MirrorBaker.BakeMirror(
                sourceMeshObject,
                _settings.MirrorAxis,
                0f, // planeOffset
                _settings.Threshold,
                _settings.FlipU
            );

            if (bakedMesh == null || bakeResult == null)
            {
                _statusMessage = T("BakeFailed");
                _statusType = MessageType.Error;
                return;
            }

            // 結果を保存
            _lastBakeResult = bakeResult;
            _bakedMeshName = bakedMesh.Name;

            // 新しいMeshContextを作成してリストに追加
            var newMeshContext = new MeshContext
            {
                Name = bakedMesh.Name,
                MeshObject = bakedMesh,
                Materials = new List<Material>(sourceMeshContext.Materials ?? new List<Material>())
            };

            // Unity Meshを生成
            newMeshContext.UnityMesh = bakedMesh.ToUnityMesh();
            newMeshContext.UnityMesh.name = bakedMesh.Name;
            newMeshContext.UnityMesh.hideFlags = HideFlags.HideAndDontSave;

            // メッシュリストに追加
            _context.AddMeshContext?.Invoke(newMeshContext);

            // ステータス更新
            _statusMessage = T("BakeSuccess", sourceMeshObject.VertexCount, bakedMesh.VertexCount);
            _statusType = MessageType.Info;

            // 書き戻し名をクリア（新しいベイクなので）
            _writeBackMeshName = null;

            // 再描画
            _context.Repaint?.Invoke();
        }

        /// <summary>
        /// 書き戻し実行
        /// </summary>
        private void ExecuteWriteBack()
        {
            if (_context == null || _lastBakeResult == null)
            {
                _statusMessage = T("NoBakeResult");
                _statusType = MessageType.Warning;
                return;
            }

            var editedMeshContext = _context.CurrentMeshContent;
            if (editedMeshContext == null || editedMeshContext.Name != _bakedMeshName)
            {
                _statusMessage = T("SelectBakedMesh");
                _statusType = MessageType.Warning;
                return;
            }

            // 元のメッシュを探す
            MeshContext originalMeshContext = FindMeshContextByName(_sourceMeshName);

            if (originalMeshContext == null || originalMeshContext.MeshObject == null)
            {
                _statusMessage = T("SourceMeshNotFound", _sourceMeshName ?? "?");
                _statusType = MessageType.Error;
                return;
            }

            // 書き戻し実行
            var resultMesh = MirrorBaker.WriteBack(
                editedMeshContext.MeshObject,
                originalMeshContext.MeshObject,
                _lastBakeResult,
                _settings.WriteBackMode
            );

            if (resultMesh == null)
            {
                _statusMessage = T("WriteBackFailed");
                _statusType = MessageType.Error;
                return;
            }

            // 新しいMeshContextを作成
            string newName = _sourceMeshName + "_WriteBack";
            resultMesh.Name = newName;
            _writeBackMeshName = newName;

            var newMeshContext = new MeshContext
            {
                Name = newName,
                MeshObject = resultMesh,
                Materials = new List<Material>(originalMeshContext.Materials ?? new List<Material>()),
                // ミラー設定を引き継ぐ
                MirrorType = originalMeshContext.MirrorType,
                MirrorAxis = originalMeshContext.MirrorAxis,
                MirrorDistance = originalMeshContext.MirrorDistance
            };

            // Unity Meshを生成
            newMeshContext.UnityMesh = resultMesh.ToUnityMesh();
            newMeshContext.UnityMesh.name = newName;
            newMeshContext.UnityMesh.hideFlags = HideFlags.HideAndDontSave;

            // メッシュリストに追加
            _context.AddMeshContext?.Invoke(newMeshContext);

            // ステータス更新
            _statusMessage = T("WriteBackSuccess", newName);
            _statusType = MessageType.Info;

            // 再描画
            _context.Repaint?.Invoke();
        }

        /// <summary>
        /// ブレンド実行
        /// </summary>
        private void ExecuteBlend()
        {
            if (_context == null)
            {
                _statusMessage = T("NoContext");
                _statusType = MessageType.Error;
                return;
            }

            // ソースメッシュを取得
            var sourceMeshContext = FindMeshContextByName(_sourceMeshName);
            var writeBackMeshContext = FindMeshContextByName(_writeBackMeshName);

            if (sourceMeshContext?.MeshObject == null || writeBackMeshContext?.MeshObject == null)
            {
                _statusMessage = T("BlendMeshNotFound");
                _statusType = MessageType.Error;
                return;
            }

            var sourceMesh = sourceMeshContext.MeshObject;
            var writeBackMesh = writeBackMeshContext.MeshObject;

            // 頂点数チェック
            if (sourceMesh.VertexCount != writeBackMesh.VertexCount)
            {
                _statusMessage = T("BlendVertexMismatch", sourceMesh.VertexCount, writeBackMesh.VertexCount);
                _statusType = MessageType.Error;
                return;
            }

            // ブレンドメッシュを作成
            var blendedMesh = sourceMesh.Clone();
            string blendName = $"{_sourceMeshName}_Blend{Mathf.RoundToInt(_blendWeight * 100)}";
            blendedMesh.Name = blendName;

            // 頂点位置をブレンド
            for (int i = 0; i < blendedMesh.VertexCount; i++)
            {
                Vector3 srcPos = sourceMesh.Vertices[i].Position;
                Vector3 dstPos = writeBackMesh.Vertices[i].Position;
                blendedMesh.Vertices[i].Position = Vector3.Lerp(srcPos, dstPos, _blendWeight);
            }

            // 法線を再計算
            blendedMesh.RecalculateSmoothNormals();

            // 新しいMeshContextを作成
            var newMeshContext = new MeshContext
            {
                Name = blendName,
                MeshObject = blendedMesh,
                Materials = new List<Material>(sourceMeshContext.Materials ?? new List<Material>()),
                // ミラー設定を引き継ぐ
                MirrorType = sourceMeshContext.MirrorType,
                MirrorAxis = sourceMeshContext.MirrorAxis,
                MirrorDistance = sourceMeshContext.MirrorDistance
            };

            // Unity Meshを生成
            newMeshContext.UnityMesh = blendedMesh.ToUnityMesh();
            newMeshContext.UnityMesh.name = blendName;
            newMeshContext.UnityMesh.hideFlags = HideFlags.HideAndDontSave;

            // メッシュリストに追加
            _context.AddMeshContext?.Invoke(newMeshContext);

            // 最後に生成したメッシュ名を記録（モーフ登録用）
            _lastGeneratedMeshName = blendName;

            // ステータス更新
            _statusMessage = T("BlendSuccess", blendName, Mathf.RoundToInt(_blendWeight * 100));
            _statusType = MessageType.Info;

            // 再描画
            _context.Repaint?.Invoke();
        }

        /// <summary>
        /// モーフとして登録
        /// </summary>
        private void ExecuteRegisterAsMorph()
        {
            if (_context == null)
            {
                _statusMessage = T("NoContext");
                _statusType = MessageType.Error;
                return;
            }

            // 最後に生成したメッシュを取得
            var generatedMeshContext = FindMeshContextByName(_lastGeneratedMeshName);
            var sourceMeshContext = FindMeshContextByName(_sourceMeshName);

            if (generatedMeshContext?.MeshObject == null || sourceMeshContext?.MeshObject == null)
            {
                _statusMessage = T("MorphMeshNotFound");
                _statusType = MessageType.Error;
                return;
            }

            var generatedMesh = generatedMeshContext.MeshObject;
            var sourceMesh = sourceMeshContext.MeshObject;

            // 頂点数チェック
            if (generatedMesh.VertexCount != sourceMesh.VertexCount)
            {
                _statusMessage = T("MorphVertexMismatch", generatedMesh.VertexCount, sourceMesh.VertexCount);
                _statusType = MessageType.Error;
                return;
            }

            // モーフ名が空の場合はデフォルト名を使用
            string morphName = string.IsNullOrWhiteSpace(_morphName) ? "MirrorMorph" : _morphName.Trim();

            // 新しいモーフメッシュを作成
            // ベース（オリジナル）の位置を基準として、生成メッシュの位置を持つ
            var morphMesh = sourceMesh.Clone();
            morphMesh.Name = morphName;
            morphMesh.Type = MeshType.Morph;

            // 生成メッシュの位置をコピー（モーフ適用後の位置）
            for (int i = 0; i < morphMesh.VertexCount; i++)
            {
                morphMesh.Vertices[i].Position = generatedMesh.Vertices[i].Position;
            }

            // 新しいMeshContextを作成
            var morphContext = new MeshContext
            {
                Name = morphName,
                MeshObject = morphMesh,
                Materials = new List<Material>(sourceMeshContext.Materials ?? new List<Material>()),
                // ミラー設定を引き継ぐ
                MirrorType = sourceMeshContext.MirrorType,
                MirrorAxis = sourceMeshContext.MirrorAxis,
                MirrorDistance = sourceMeshContext.MirrorDistance
            };

            // モーフ基準データを設定（オリジナルの位置を基準として保存）
            // まずオリジナルの位置に戻す
            for (int i = 0; i < morphMesh.VertexCount; i++)
            {
                morphMesh.Vertices[i].Position = sourceMesh.Vertices[i].Position;
            }

            // 基準データを設定
            morphContext.SetAsMorph(morphName);
            morphContext.MorphPanel = _settings.MorphPanel;

            // 生成メッシュの位置に戻す（これがモーフ適用後の状態）
            for (int i = 0; i < morphMesh.VertexCount; i++)
            {
                morphMesh.Vertices[i].Position = generatedMesh.Vertices[i].Position;
            }

            // エクスポート時に除外（モーフはメッシュとしてはエクスポートしない）
            morphContext.ExcludeFromExport = true;

            // Unity Meshを生成
            morphContext.UnityMesh = morphMesh.ToUnityMesh();
            morphContext.UnityMesh.name = morphName;
            morphContext.UnityMesh.hideFlags = HideFlags.HideAndDontSave;

            // メッシュリストに追加
            _context.AddMeshContext?.Invoke(morphContext);

            // ステータス更新
            _statusMessage = T("MorphRegistered", morphName);
            _statusType = MessageType.Info;

            // 再描画
            _context.Repaint?.Invoke();
        }
    }

    // ================================================================
    // 設定クラス
    // ================================================================

    /// <summary>
    /// MirrorEditTool の設定
    /// </summary>
    public class MirrorEditSettings : IToolSettings
    {
        /// <summary>ミラー軸（0:X, 1:Y, 2:Z）</summary>
        public int MirrorAxis = 0;

        /// <summary>境界判定閾値</summary>
        public float Threshold = 0.0001f;

        /// <summary>UV U座標を反転するか</summary>
        public bool FlipU = false;

        /// <summary>書き戻しモード</summary>
        public WriteBackMode WriteBackMode = WriteBackMode.OriginalSideOnly;

        /// <summary>モーフパネル（PMX: 0=眉, 1=目, 2=口, 3=その他）</summary>
        public int MorphPanel = 3;

        public IToolSettings Clone()
        {
            return new MirrorEditSettings
            {
                MirrorAxis = this.MirrorAxis,
                Threshold = this.Threshold,
                FlipU = this.FlipU,
                WriteBackMode = this.WriteBackMode,
                MorphPanel = this.MorphPanel
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not MirrorEditSettings o) return true;
            return MirrorAxis != o.MirrorAxis ||
                   !Mathf.Approximately(Threshold, o.Threshold) ||
                   FlipU != o.FlipU ||
                   WriteBackMode != o.WriteBackMode ||
                   MorphPanel != o.MorphPanel;
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is MirrorEditSettings o)
            {
                MirrorAxis = o.MirrorAxis;
                Threshold = o.Threshold;
                FlipU = o.FlipU;
                WriteBackMode = o.WriteBackMode;
                MorphPanel = o.MorphPanel;
            }
        }
    }
}
