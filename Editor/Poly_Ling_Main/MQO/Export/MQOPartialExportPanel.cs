// Assets/Editor/Poly_Ling_/ToolPanels/MQO/Export/MQOPartialExportPanel.cs
// MQO部分エクスポートパネル
// 左リスト（モデル側）と右リスト（MQO側）でチェックを入れ、チェック順に対応付けてエクスポート

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Localization;
using Poly_Ling.Tools;
using Poly_Ling.PMX;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQO部分エクスポートパネル
    /// </summary>
    public class MQOPartialExportPanel : EditorWindow
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "MQO Partial Export", ["ja"] = "MQO部分エクスポート" },

            // セクション
            ["ReferenceMQO"] = new() { ["en"] = "Reference MQO", ["ja"] = "リファレンスMQO" },
            ["Options"] = new() { ["en"] = "Options", ["ja"] = "オプション" },
            ["ModelMeshes"] = new() { ["en"] = "Model Meshes", ["ja"] = "モデルメッシュ" },
            ["MQOObjects"] = new() { ["en"] = "MQO Objects", ["ja"] = "MQOオブジェクト" },

            // ラベル
            ["MQOFile"] = new() { ["en"] = "MQO File", ["ja"] = "MQOファイル" },
            ["Expanded"] = new() { ["en"] = "Model Expanded", ["ja"] = "モデル展開済み" },
            ["ExportScale"] = new() { ["en"] = "Export Scale", ["ja"] = "エクスポートスケール" },
            ["FlipZ"] = new() { ["en"] = "Flip Z", ["ja"] = "Z反転" },
            ["SkipBakedMirror"] = new() { ["en"] = "Skip Baked Mirror (flag only)", ["ja"] = "ベイクミラーをスキップ（フラグのみ）" },
            ["SkipNamedMirror"] = new() { ["en"] = "Skip Named Mirror (+)", ["ja"] = "名前ミラー(+)をスキップ" },

            // WriteBackオプション
            ["WriteBack"] = new() { ["en"] = "WriteBack Options", ["ja"] = "書き戻しオプション" },
            ["WriteBackPosition"] = new() { ["en"] = "Position", ["ja"] = "位置" },
            ["WriteBackUV"] = new() { ["en"] = "UV", ["ja"] = "UV" },
            ["WriteBackBoneWeight"] = new() { ["en"] = "BoneWeight", ["ja"] = "ボーンウェイト" },

            // ボタン
            ["SelectAll"] = new() { ["en"] = "All", ["ja"] = "全選択" },
            ["SelectNone"] = new() { ["en"] = "None", ["ja"] = "全解除" },
            ["Export"] = new() { ["en"] = "Export MQO", ["ja"] = "MQOエクスポート" },

            // ステータス
            ["Selection"] = new() { ["en"] = "Selection: Model {0} ↔ MQO {1}", ["ja"] = "選択: モデル {0} ↔ MQO {1}" },
            ["CountMismatch"] = new() { ["en"] = "Count mismatch!", ["ja"] = "数が不一致！" },
            ["Ready"] = new() { ["en"] = "Ready to export", ["ja"] = "エクスポート可能" },

            // メッセージ
            ["NoContext"] = new() { ["en"] = "No context. Open from Poly_Ling.", ["ja"] = "コンテキスト未設定" },
            ["NoModel"] = new() { ["en"] = "No model loaded", ["ja"] = "モデルなし" },
            ["SelectMQOFirst"] = new() { ["en"] = "Select MQO file", ["ja"] = "MQOファイルを選択" },
            ["ExportSuccess"] = new() { ["en"] = "Export: {0}", ["ja"] = "エクスポート完了: {0}" },
            ["ExportFailed"] = new() { ["en"] = "Export failed: {0}", ["ja"] = "エクスポート失敗: {0}" },
            ["VertexMismatch"] = new() { ["en"] = "Vertex mismatch: {0}({1}) → {2}({3})", ["ja"] = "頂点数不一致: {0}({1}) → {2}({3})" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // フィールド
        // ================================================================

        private ToolContext _context;
        private string _mqoFilePath = "";
        private MQOPartialMatchHelper _matchHelper = new MQOPartialMatchHelper();

        // オプション
        // PMXは展開後の値しか得られない
        private float _exportScale = 0.01f; // MqoUnityRatio: Unity→MQO = ÷0.01 = ×100
        private bool _flipZ = true;
        private bool _skipBakedMirror = true;
        private bool _skipNamedMirror = true;

        // WriteBackオプション
        private bool _writeBackPosition = true;
        private bool _writeBackUV = false;
        private bool _writeBackBoneWeight = false;

        // UI状態
        private Vector2 _scrollLeft;
        private Vector2 _scrollRight;
        private string _lastResult = "";

        // データクラスはMQOPartialMatchHelper内のPartialMeshEntry/PartialMQOEntryを使用

        // ================================================================
        // プロパティ
        // ================================================================

        private ModelContext Model => _context?.Model;

        // ================================================================
        // Open
        // ================================================================

        [MenuItem("Tools/Poly_Ling/debug/MQO Partial Export")]
        public static void ShowWindow()
        {
            var panel = GetWindow<MQOPartialExportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(700, 500);
            panel.Show();
        }

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<MQOPartialExportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(700, 500);
            panel._context = ctx;
            panel._matchHelper.BuildModelList(ctx?.Model, panel._skipBakedMirror, panel._skipNamedMirror);
            panel.Show();
        }

        public void SetContext(ToolContext ctx)
        {
            _context = ctx;
            var es = ctx?.UndoController?.EditorState;
            if (es != null) _exportScale = es.MqoUnityRatio > 0f ? es.MqoUnityRatio : 0.01f;
            _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror);
            if (_matchHelper.MQODocument != null)
            {
                _matchHelper.AutoMatch();
            }
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            // MQOファイル選択
            DrawMQOFileSection();
            EditorGUILayout.Space(5);

            // オプション
            DrawOptionsSection();
            EditorGUILayout.Space(5);

            // 左右リスト
            DrawDualListSection();
            EditorGUILayout.Space(5);

            // ステータスとエクスポート
            DrawExportSection();
        }

        // ================================================================
        // MQOファイルセクション
        // ================================================================

        private void DrawMQOFileSection()
        {
            EditorGUILayout.LabelField(T("ReferenceMQO"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(T("MQOFile"));
                var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _mqoFilePath = EditorGUI.TextField(rect, _mqoFilePath);

                MQOPartialMatchHelper.HandleDropOnRect(rect, ".mqo", path =>
                {
                    _mqoFilePath = path;
                    LoadMQOAndMatch();
                });

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir = string.IsNullOrEmpty(_mqoFilePath) ? Application.dataPath : Path.GetDirectoryName(_mqoFilePath);
                    string path = EditorUtility.OpenFilePanel("Select MQO", dir, "mqo");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _mqoFilePath = path;
                        LoadMQOAndMatch();
                    }
                }
            }

            if (_matchHelper.MQODocument != null)
            {
                int nonEmpty = _matchHelper.MQOObjects.Count;
                int total = _matchHelper.MQODocument.Objects.Count;
                EditorGUILayout.LabelField($"Objects: {nonEmpty} / {total}", EditorStyles.miniLabel);
            }
        }

        // ================================================================
        // オプションセクション
        // ================================================================

        private void DrawOptionsSection()
        {
            EditorGUILayout.LabelField(T("Options"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _exportScale = EditorGUILayout.FloatField(T("ExportScale"), _exportScale, GUILayout.Width(200));
                _flipZ = EditorGUILayout.ToggleLeft(T("FlipZ"), _flipZ, GUILayout.Width(80));
            }

            bool prevSkip = _skipBakedMirror;
            _skipBakedMirror = EditorGUILayout.ToggleLeft(T("SkipBakedMirror"), _skipBakedMirror);
            if (prevSkip != _skipBakedMirror)
            {
                _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror);
                if (_matchHelper.MQODocument != null) _matchHelper.AutoMatch();
            }

            bool prevSkipNamed = _skipNamedMirror;
            _skipNamedMirror = EditorGUILayout.ToggleLeft(T("SkipNamedMirror"), _skipNamedMirror);
            if (prevSkipNamed != _skipNamedMirror)
            {
                _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror);
                if (_matchHelper.MQODocument != null) _matchHelper.AutoMatch();
            }

            // WriteBackオプション
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("WriteBack"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _writeBackPosition = EditorGUILayout.ToggleLeft(T("WriteBackPosition"), _writeBackPosition, GUILayout.Width(100));
                _writeBackUV = EditorGUILayout.ToggleLeft(T("WriteBackUV"), _writeBackUV, GUILayout.Width(80));
                _writeBackBoneWeight = EditorGUILayout.ToggleLeft(T("WriteBackBoneWeight"), _writeBackBoneWeight, GUILayout.Width(140));
            }
        }

        // ================================================================
        // 左右リストセクション（_matchHelperに委譲）
        // ================================================================

        private void DrawDualListSection()
        {
            _matchHelper.DrawDualListSection(_context, position.width, ref _scrollLeft, ref _scrollRight);
        }

        // ================================================================
        // エクスポートセクション
        // ================================================================

        private void DrawExportSection()
        {
            int modelCount = _matchHelper.ModelMeshes.Count(m => m.Selected);
            int mqoCount = _matchHelper.MQOObjects.Count(m => m.Selected);

            // PMXは展開後の値しか得られないので常にExpandedVertexCount
            int modelVerts = _matchHelper.SelectedModelVertexCount;
            int mqoVerts = _matchHelper.SelectedMQOVertexCount;

            EditorGUILayout.LabelField(T("Selection", modelCount, mqoCount) + $"  Verts: {modelVerts} → {mqoVerts}");

            bool vertexMatch = modelVerts == mqoVerts;
            bool canExport = modelCount > 0 && mqoCount > 0 && _matchHelper.MQODocument != null;

            if (!vertexMatch && canExport)
            {
                EditorGUILayout.HelpBox(T("VertexMismatch", "Model", modelVerts, "MQO", mqoVerts), MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!canExport))
            {
                if (GUILayout.Button(T("Export"), GUILayout.Height(30)))
                {
                    ExecuteExport();
                }
            }

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.HelpBox(_lastResult, MessageType.Info);
            }
        }

        // ================================================================
        // MQO読み込みと自動照合（_matchHelperに委譲）
        // ================================================================

        private void LoadMQOAndMatch()
        {
            _matchHelper.LoadMQO(_mqoFilePath, _flipZ, false); // visibleOnly=false（エクスポートは全オブジェクト対象）

            if (_matchHelper.ModelMeshes.Count == 0)
            {
                _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror);
            }

            if (_matchHelper.MQODocument != null)
            {
                _matchHelper.AutoMatch();
            }
            Repaint();
        }

        // ================================================================
        // エクスポート実行
        // ================================================================

        private void ExecuteExport()
        {
            try
            {
                string defaultName = Path.GetFileNameWithoutExtension(_mqoFilePath) + "_partial.mqo";
                string savePath = EditorUtility.SaveFilePanel("Save MQO", Path.GetDirectoryName(_mqoFilePath), defaultName, "mqo");

                if (string.IsNullOrEmpty(savePath))
                    return;

                // 選択されたものをリスト化
                var selectedModels = _matchHelper.SelectedModelMeshes;
                var selectedMQOs = _matchHelper.SelectedMQOObjects;

                int transferred = 0;
                int modelVertexOffset = 0;  // モデル側の頂点オフセット

                // MQOオブジェクトごとに転送
                foreach (var mqoEntry in selectedMQOs)
                {
                    int count = TransferToMQO(mqoEntry, selectedModels, ref modelVertexOffset);
                    transferred += count;
                }

                // 保存
                Utility.MQOWriter.WriteToFile(_matchHelper.MQODocument, savePath);

                // 結果表示
                int totalModelVerts = _matchHelper.SelectedModelVertexCount;
                int totalMqoVerts = _matchHelper.SelectedMQOVertexCount;

                _lastResult = T("ExportSuccess", $"{transferred} vertices → {Path.GetFileName(savePath)}");
                if (totalModelVerts != totalMqoVerts)
                {
                    _lastResult += $"\n(Model:{totalModelVerts} ≠ MQO:{totalMqoVerts})";
                }

                // リロード
                _matchHelper.LoadMQO(_mqoFilePath, _flipZ, false);
            }
            catch (Exception ex)
            {
                _lastResult = T("ExportFailed", ex.Message);
                Debug.LogError($"[MQOPartialExport] {ex.Message}\n{ex.StackTrace}");
            }

            Repaint();
        }

        /// <summary>
        /// MQO側のMeshContextを基準に、モデル側の頂点を転送
        /// PMXMQOTransferPanel.TransferPMXToMQOと同じアプローチ
        /// Position, UV, BoneWeight書き戻し対応
        /// 孤立頂点（面に使われていない頂点）はスキップ
        /// </summary>
        private int TransferToMQO(PartialMQOEntry mqoEntry, List<PartialMeshEntry> modelMeshes, ref int modelVertexOffset)
        {
            var mqoMeshContext = mqoEntry.MeshContext;
            var mqoMo = mqoMeshContext?.MeshObject;
            if (mqoMo == null) return 0;

            // MQODocument側のオブジェクトを名前で検索
            var mqoDocObj = _matchHelper.MQODocument.Objects.FirstOrDefault(o => o.Name == mqoEntry.Name);
            if (mqoDocObj == null) return 0;

            // MQO側の面で使用されている頂点インデックスを収集（孤立頂点判定用）
            var usedVertexIndices = new HashSet<int>();
            foreach (var face in mqoMo.Faces)
            {
                foreach (var vi in face.VertexIndices)
                {
                    usedVertexIndices.Add(vi);
                }
            }

            int transferred = 0;
            int startOffset = modelVertexOffset;

            // MQO側のMeshContextの頂点を走査
            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                var mqoVertex = mqoMo.Vertices[vIdx];
                int uvCount = mqoVertex.UVs.Count > 0 ? mqoVertex.UVs.Count : 1;

                // 孤立頂点（面に使われていない頂点）はスキップ
                // PMXには孤立点がない仕様のため、モデル側オフセットは進めない
                if (!usedVertexIndices.Contains(vIdx))
                {
                    continue;
                }

                // Position更新
                if (_writeBackPosition)
                {
                    Vector3? newPos = GetModelVertexPosition(modelMeshes, modelVertexOffset);

                    if (newPos.HasValue)
                    {
                        Vector3 pos = newPos.Value;

                        // 座標変換: Model → MQO
                        if (_flipZ) pos.z = -pos.z;
                        pos /= _exportScale;

                        // MeshContextとMQODocument両方を更新
                        mqoVertex.Position = pos;
                        if (vIdx < mqoDocObj.Vertices.Count)
                        {
                            mqoDocObj.Vertices[vIdx].Position = pos;
                        }

                        transferred++;
                    }
                }

                // UV展開分だけモデル側インデックスを進める
                modelVertexOffset += uvCount;
            }

            // UV更新（WriteBack APIを使用）
            if (_writeBackUV)
            {
                WriteBackUVsToMQO(mqoEntry, modelMeshes, startOffset, mqoDocObj, usedVertexIndices);
            }

            // BoneWeight更新（WriteBack APIを使用）
            if (_writeBackBoneWeight)
            {
                WriteBackBoneWeightsToMQO(mqoEntry, modelMeshes, startOffset, mqoDocObj, usedVertexIndices);
            }

            return transferred;
        }

        /// <summary>
        /// UVを面のUVs配列に書き戻す
        /// MQO側の面のUVIndicesを使って、対応する展開後インデックスを特定
        /// 孤立頂点はスキップ
        /// </summary>
        private void WriteBackUVsToMQO(PartialMQOEntry mqoEntry, List<PartialMeshEntry> modelMeshes, int startOffset, MQOObject mqoDocObj, HashSet<int> usedVertexIndices)
        {
            var mqoMo = mqoEntry.MeshContext?.MeshObject;
            if (mqoMo == null) return;

            // MQO側の頂点インデックス→展開後オフセット開始位置のマッピングを構築
            // 孤立頂点はスキップしてカウントしない
            var vertexToExpandedStart = new Dictionary<int, int>();
            int expandedIdx = 0;
            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                // 孤立頂点はスキップ
                if (!usedVertexIndices.Contains(vIdx))
                {
                    continue;
                }
                vertexToExpandedStart[vIdx] = expandedIdx;
                int uvCount = mqoMo.Vertices[vIdx].UVs.Count > 0 ? mqoMo.Vertices[vIdx].UVs.Count : 1;
                expandedIdx += uvCount;
            }

            // MQODocument側の面とMeshContext側の面を並行して走査
            int mqoFaceIdx = 0;
            foreach (var mqoDocFace in mqoDocObj.Faces)
            {
                if (mqoDocFace.IsSpecialFace) continue;
                if (mqoDocFace.VertexIndices == null) continue;

                // 対応するMeshContext側の面を探す（特殊面をスキップしながら）
                Face meshFace = null;
                while (mqoFaceIdx < mqoMo.FaceCount)
                {
                    meshFace = mqoMo.Faces[mqoFaceIdx];
                    mqoFaceIdx++;
                    // MeshContextには特殊面がないはずだが念のため
                    if (meshFace.VertexIndices.Count >= 3) break;
                    meshFace = null;
                }

                if (meshFace == null) continue;

                // 面のUVs配列を確保
                if (mqoDocFace.UVs == null || mqoDocFace.UVs.Length != mqoDocFace.VertexIndices.Length)
                {
                    mqoDocFace.UVs = new Vector2[mqoDocFace.VertexIndices.Length];
                }

                for (int i = 0; i < mqoDocFace.VertexIndices.Length && i < meshFace.VertexIndices.Count; i++)
                {
                    int vIdx = mqoDocFace.VertexIndices[i];
                    if (!vertexToExpandedStart.TryGetValue(vIdx, out int localExpStart)) continue;

                    // UVIndicesからUVスロット番号を取得
                    int uvSlot = (i < meshFace.UVIndices.Count) ? meshFace.UVIndices[i] : 0;

                    // 展開後インデックス = 頂点の展開開始位置 + UVスロット番号
                    int globalOffset = startOffset + localExpStart + uvSlot;
                    Vector2? uv = GetModelVertexUV(modelMeshes, globalOffset);
                    if (uv.HasValue)
                    {
                        mqoDocFace.UVs[i] = uv.Value;
                    }
                }
            }
        }

        /// <summary>
        /// ボーンウェイトを特殊面として書き戻す（既存削除→新規追加）
        /// </summary>
        private void WriteBackBoneWeightsToMQO(PartialMQOEntry mqoEntry, List<PartialMeshEntry> modelMeshes, int startOffset, MQOObject mqoDocObj, HashSet<int> usedVertexIndices)
        {
            var mqoMo = mqoEntry.MeshContext?.MeshObject;
            if (mqoMo == null) return;

            // 既存の特殊面を削除（頂点ID特殊面とボーンウェイト特殊面）
            mqoDocObj.Faces.RemoveAll(f => f.IsSpecialFace);

            // 頂点ID特殊面とボーンウェイト特殊面を再追加
            // 孤立頂点はスキップ
            int localOffset = 0;
            for (int vIdx = 0; vIdx < mqoMo.VertexCount && vIdx < mqoDocObj.Vertices.Count; vIdx++)
            {
                var mqoVertex = mqoMo.Vertices[vIdx];
                int uvCount = mqoVertex.UVs.Count > 0 ? mqoVertex.UVs.Count : 1;

                // 孤立頂点はスキップ
                if (!usedVertexIndices.Contains(vIdx))
                {
                    continue;
                }

                int globalOffset = startOffset + localOffset;

                // モデル側から頂点情報を取得
                var vertexInfo = GetModelVertexInfo(modelMeshes, globalOffset);
                if (vertexInfo != null)
                {
                    // 頂点ID特殊面
                    if (vertexInfo.Id != -1)
                    {
                        mqoDocObj.Faces.Add(
                            VertexIdHelper.CreateSpecialFaceForVertexId(vIdx, vertexInfo.Id, 0));
                    }

                    // ボーンウェイト特殊面（実体側）
                    if (vertexInfo.HasBoneWeight)
                    {
                        var boneWeightData = VertexIdHelper.BoneWeightData.FromUnityBoneWeight(vertexInfo.BoneWeight.Value);
                        mqoDocObj.Faces.Add(
                            VertexIdHelper.CreateSpecialFaceForBoneWeight(vIdx, boneWeightData, false, 0));
                    }

                    // タイプA: ミラー側ボーンウェイト特殊面
                    if (vertexInfo.HasMirrorBoneWeight)
                    {
                        var mirrorBoneWeightData = VertexIdHelper.BoneWeightData.FromUnityBoneWeight(vertexInfo.MirrorBoneWeight.Value);
                        mqoDocObj.Faces.Add(
                            VertexIdHelper.CreateSpecialFaceForBoneWeight(vIdx, mirrorBoneWeightData, true, 0));
                    }
                }

                localOffset += uvCount;
            }
        }

        /// <summary>
        /// モデル側の指定オフセットからUVを取得
        /// PMXは展開後の値しか得られないので展開後インデックスで処理
        /// </summary>
        private Vector2? GetModelVertexUV(List<PartialMeshEntry> modelMeshes, int offset)
        {
            int currentOffset = 0;

            foreach (var model in modelMeshes)
            {
                var mo = model.Context?.MeshObject;
                if (mo == null) continue;

                int meshVertCount = model.ExpandedVertexCount;

                if (offset < currentOffset + meshVertCount)
                {
                    int localIdx = offset - currentOffset;

                    int expandedIdx = 0;
                    for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                    {
                        var v = mo.Vertices[vIdx];
                        int uvCount = v.UVs.Count > 0 ? v.UVs.Count : 1;

                        if (localIdx < expandedIdx + uvCount)
                        {
                            int uvSlot = localIdx - expandedIdx;
                            return uvSlot < v.UVs.Count ? v.UVs[uvSlot] : (v.UVs.Count > 0 ? v.UVs[0] : Vector2.zero);
                        }
                        expandedIdx += uvCount;
                    }

                    return null;
                }

                currentOffset += meshVertCount;
            }

            return null;
        }

        /// <summary>
        /// モデル側の指定オフセットから頂点情報を取得
        /// PMXは展開後の値しか得られないので展開後インデックスで処理
        /// </summary>
        private Vertex GetModelVertexInfo(List<PartialMeshEntry> modelMeshes, int offset)
        {
            int currentOffset = 0;

            foreach (var model in modelMeshes)
            {
                var mo = model.Context?.MeshObject;
                if (mo == null) continue;

                int meshVertCount = model.ExpandedVertexCount;

                if (offset < currentOffset + meshVertCount)
                {
                    int localIdx = offset - currentOffset;

                    int expandedIdx = 0;
                    for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                    {
                        var v = mo.Vertices[vIdx];
                        int uvCount = v.UVs.Count > 0 ? v.UVs.Count : 1;

                        if (localIdx < expandedIdx + uvCount)
                        {
                            return v;
                        }
                        expandedIdx += uvCount;
                    }

                    return null;
                }

                currentOffset += meshVertCount;
            }

            return null;
        }

        /// <summary>
        /// モデル側の指定オフセットから頂点位置を取得
        /// PMXは展開後の値しか得られないので展開後インデックスで処理
        /// </summary>
        private Vector3? GetModelVertexPosition(List<PartialMeshEntry> modelMeshes, int offset)
        {
            int currentOffset = 0;

            foreach (var model in modelMeshes)
            {
                var mo = model.Context?.MeshObject;
                if (mo == null) continue;

                int meshVertCount = model.ExpandedVertexCount;

                if (offset < currentOffset + meshVertCount)
                {
                    int localIdx = offset - currentOffset;

                    int expandedIdx = 0;
                    for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                    {
                        var v = mo.Vertices[vIdx];
                        int uvCount = v.UVs.Count > 0 ? v.UVs.Count : 1;

                        if (localIdx < expandedIdx + uvCount)
                        {
                            return v.Position;
                        }
                        expandedIdx += uvCount;
                    }

                    return null;
                }

                currentOffset += meshVertCount;
            }

            return null;
        }

    }
}