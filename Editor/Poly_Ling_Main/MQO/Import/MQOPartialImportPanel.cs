// Assets/Editor/Poly_Ling/MQO/Import/MQOPartialImportPanel.cs
// MQO部分インポートパネル
// MQOファイルから選択メッシュの頂点位置/メッシュ構造/材質内容を部分的にインポート
// チェックボックスで複数項目を同時にインポート可能

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
using Poly_Ling.Materials;
using Poly_Ling.PMX;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQO部分インポートパネル
    /// </summary>
    public class MQOPartialImportPanel : EditorWindow
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "MQO Partial Import", ["ja"] = "MQO部分インポート" },

            // セクション
            ["ReferenceMQO"] = new() { ["en"] = "Reference MQO", ["ja"] = "リファレンスMQO" },
            ["Options"] = new() { ["en"] = "Options", ["ja"] = "オプション" },
            ["ImportTarget"] = new() { ["en"] = "Import Target", ["ja"] = "インポート対象" },

            // ラベル
            ["MQOFile"] = new() { ["en"] = "MQO File", ["ja"] = "MQOファイル" },
            ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール" },
            ["FlipZ"] = new() { ["en"] = "Flip Z", ["ja"] = "Z反転" },
            ["FlipUV_V"] = new() { ["en"] = "Flip UV V", ["ja"] = "UV V反転" },
            ["SkipBakedMirror"] = new() { ["en"] = "Skip Baked Mirror (flag only)", ["ja"] = "ベイクミラーをスキップ（フラグのみ）" },
            ["SkipNamedMirror"] = new() { ["en"] = "Skip Named Mirror (+)", ["ja"] = "名前ミラー(+)をスキップ" },
            ["NormalMode"] = new() { ["en"] = "Normal Mode", ["ja"] = "法線モード" },
            ["SmoothingAngle"] = new() { ["en"] = "Smoothing Angle", ["ja"] = "スムージング角度" },

            // チェックボックス
            ["VertexPosition"] = new() { ["en"] = "Vertex Position", ["ja"] = "頂点位置" },
            ["MeshStructure"] = new() { ["en"] = "Mesh Structure (Faces + UV)", ["ja"] = "メッシュ構造（面＋UV）" },
            ["MaterialContent"] = new() { ["en"] = "Material Content (by name)", ["ja"] = "材質内容（名前マッチング）" },
            ["BakeMirror"] = new() { ["en"] = "Bake Mirror", ["ja"] = "ミラーベイク" },

            // ボタン
            ["Import"] = new() { ["en"] = "Import", ["ja"] = "インポート" },

            // ステータス
            ["Selection"] = new() { ["en"] = "Selection: Model {0} ↔ MQO {1}", ["ja"] = "選択: モデル {0} ↔ MQO {1}" },
            ["VertexMismatch"] = new() { ["en"] = "Vertex mismatch: Model({0}) ≠ MQO({1})", ["ja"] = "頂点数不一致: モデル({0}) ≠ MQO({1})" },
            ["NothingSelected"] = new() { ["en"] = "Select at least one import target", ["ja"] = "インポート対象を1つ以上選択してください" },

            // メッセージ
            ["ImportSuccess"] = new() { ["en"] = "Import: {0}", ["ja"] = "インポート完了: {0}" },
            ["ImportFailed"] = new() { ["en"] = "Import failed: {0}", ["ja"] = "インポート失敗: {0}" },
            ["MaterialMatchResult"] = new() { ["en"] = "Material: {0} / {1} matched", ["ja"] = "材質: {0} / {1} マッチ" },
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
        private float _importScale = 0.01f; // MqoUnityRatio
        private bool _flipZ = true;
        private bool _flipUV_V = true;
        private bool _skipBakedMirror = true;
        private bool _skipNamedMirror = true;
        private NormalMode _normalMode = NormalMode.Smooth;
        private float _smoothingAngle = 60f;

        // インポート対象チェックボックス
        private bool _importVertexPosition = true;
        private bool _importMeshStructure = false;
        private bool _importMaterialContent = false;

        // ミラー
        private bool _bakeMirror = true;

        // UI状態
        private Vector2 _scrollLeft;
        private Vector2 _scrollRight;
        private string _lastResult = "";

        // ================================================================
        // プロパティ
        // ================================================================

        private ModelContext Model => _context?.Model;

        /// <summary>メッシュマッチングが必要か（頂点位置 or メッシュ構造が有効）</summary>
        private bool NeedsMeshMatching => _importVertexPosition || _importMeshStructure;

        /// <summary>何か1つでもインポート対象が選択されているか</summary>
        private bool HasAnyTarget => _importVertexPosition || _importMeshStructure || _importMaterialContent;

        // ================================================================
        // Open
        // ================================================================

        [MenuItem("Tools/Poly_Ling/debug/MQO Partial Import")]
        public static void ShowWindow()
        {
            var panel = GetWindow<MQOPartialImportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(700, 500);
            panel.Show();
        }

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<MQOPartialImportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(700, 500);
            panel._context = ctx;
            panel.InitFromContext();
            panel._matchHelper.BuildModelList(ctx?.Model, panel._skipBakedMirror, panel._skipNamedMirror, pairMirrors: true);
            panel.Show();
        }

        public void SetContext(ToolContext ctx)
        {
            _context = ctx;
            InitFromContext();
            _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror, pairMirrors: true);
            if (_matchHelper.MQODocument != null)
            {
                _matchHelper.AutoMatch();
            }
        }

        private void InitFromContext()
        {
            var es = _context?.UndoController?.EditorState;
            if (es != null)
            {
                _importScale = es.MqoUnityRatio > 0f ? es.MqoUnityRatio : 0.01f;
                _flipZ = es.MqoFlipZ;
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

            // インポート対象チェックボックス
            DrawImportTargetSection();
            EditorGUILayout.Space(5);

            // オプション
            DrawOptionsSection();
            EditorGUILayout.Space(5);

            // 左右リスト（メッシュマッチングが必要な場合）
            if (NeedsMeshMatching)
            {
                _matchHelper.DrawDualListSection(_context, position.width, ref _scrollLeft, ref _scrollRight);
                EditorGUILayout.Space(5);
            }

            // 材質マッチングプレビュー
            if (_importMaterialContent)
            {
                DrawMaterialMatchPreview();
                EditorGUILayout.Space(5);
            }

            // インポートボタン
            DrawImportSection();
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
        // インポート対象セクション（チェックボックス）
        // ================================================================

        private void DrawImportTargetSection()
        {
            EditorGUILayout.LabelField(T("ImportTarget"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _importVertexPosition = EditorGUILayout.ToggleLeft(T("VertexPosition"), _importVertexPosition, GUILayout.Width(120));
                _importMeshStructure = EditorGUILayout.ToggleLeft(T("MeshStructure"), _importMeshStructure, GUILayout.Width(220));
                _importMaterialContent = EditorGUILayout.ToggleLeft(T("MaterialContent"), _importMaterialContent);
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
                _importScale = EditorGUILayout.FloatField(T("Scale"), _importScale, GUILayout.Width(200));
                _flipZ = EditorGUILayout.ToggleLeft(T("FlipZ"), _flipZ, GUILayout.Width(80));
            }

            // メッシュ構造インポート時のみUV反転と法線設定を表示
            if (_importMeshStructure)
            {
                _flipUV_V = EditorGUILayout.ToggleLeft(T("FlipUV_V"), _flipUV_V);
                _bakeMirror = EditorGUILayout.ToggleLeft(T("BakeMirror"), _bakeMirror);
                _normalMode = (NormalMode)EditorGUILayout.EnumPopup(T("NormalMode"), _normalMode);
                if (_normalMode == NormalMode.Smooth)
                {
                    _smoothingAngle = EditorGUILayout.Slider(T("SmoothingAngle"), _smoothingAngle, 0f, 180f);
                }
            }

            // メッシュマッチング用フィルタ
            if (NeedsMeshMatching)
            {
                bool prevSkipNamed = _skipNamedMirror;
                _skipNamedMirror = EditorGUILayout.ToggleLeft(T("SkipNamedMirror"), _skipNamedMirror);
                if (prevSkipNamed != _skipNamedMirror)
                {
                    _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror, pairMirrors: true);
                    if (_matchHelper.MQODocument != null) _matchHelper.AutoMatch();
                }
            }
        }

        // ================================================================
        // 材質マッチングプレビュー
        // ================================================================

        private void DrawMaterialMatchPreview()
        {
            if (_matchHelper.MQODocument == null || Model == null) return;

            var matches = BuildMaterialMatches();
            EditorGUILayout.LabelField(T("MaterialMatchResult", matches.Count, _matchHelper.MQODocument.Materials.Count),
                EditorStyles.miniLabel);

            foreach (var match in matches)
            {
                EditorGUILayout.LabelField($"  {match.MqoMaterial.Name} → {match.ModelMaterialRef.Name}",
                    EditorStyles.miniLabel);
            }
        }

        // ================================================================
        // インポートセクション
        // ================================================================

        private void DrawImportSection()
        {
            if (!HasAnyTarget)
            {
                EditorGUILayout.HelpBox(T("NothingSelected"), MessageType.Info);
                return;
            }

            // メッシュマッチングのステータス
            if (NeedsMeshMatching)
            {
                int modelCount = _matchHelper.ModelMeshes.Count(m => m.Selected);
                int mqoCount = _matchHelper.MQOObjects.Count(m => m.Selected);
                int modelVerts = _matchHelper.SelectedModelVertexCount;
                int mqoVerts = _matchHelper.SelectedMQOVertexCount;

                EditorGUILayout.LabelField(T("Selection", modelCount, mqoCount) + $"  Verts: {modelVerts} ← {mqoVerts}");

                if (_importVertexPosition && !_importMeshStructure && modelVerts != mqoVerts && modelCount > 0 && mqoCount > 0)
                {
                    EditorGUILayout.HelpBox(T("VertexMismatch", modelVerts, mqoVerts), MessageType.Warning);
                }
            }

            // インポートボタン
            bool canImport = _matchHelper.MQODocument != null;
            if (NeedsMeshMatching)
            {
                canImport &= _matchHelper.ModelMeshes.Any(m => m.Selected) &&
                             _matchHelper.MQOObjects.Any(m => m.Selected);
            }

            using (new EditorGUI.DisabledScope(!canImport))
            {
                if (GUILayout.Button(T("Import"), GUILayout.Height(30)))
                {
                    ExecuteImport();
                }
            }

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.HelpBox(_lastResult, MessageType.Info);
            }
        }

        // ================================================================
        // MQO読み込みと自動照合
        // ================================================================

        private void LoadMQOAndMatch()
        {
            _matchHelper.LoadMQO(_mqoFilePath, _flipZ, true); // visibleOnly=true

            if (_matchHelper.ModelMeshes.Count == 0)
            {
                _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror, pairMirrors: true);
            }

            if (_matchHelper.MQODocument != null)
            {
                _matchHelper.AutoMatch();
            }
            Repaint();
        }

        // ================================================================
        // インポート実行
        // ================================================================

        private void ExecuteImport()
        {
            try
            {
                var results = new List<string>();

                var selectedModels = _matchHelper.SelectedModelMeshes;
                var selectedMQOs = _matchHelper.SelectedMQOObjects;

                bool topologyChanged = false;

                // メッシュ構造インポート（頂点位置も同時に処理可能）
                if (_importMeshStructure && selectedModels.Count > 0 && selectedMQOs.Count > 0)
                {
                    int count = ExecuteMeshStructureImport(selectedModels, selectedMQOs, _importVertexPosition);
                    results.Add($"Structure: {count} meshes");
                    if (_importVertexPosition)
                        results.Add("Position: included in structure");
                    topologyChanged = true;
                }
                // 頂点位置のみ（メッシュ構造なし）
                else if (_importVertexPosition && selectedModels.Count > 0 && selectedMQOs.Count > 0)
                {
                    int count = ExecuteVertexPositionImport(selectedModels, selectedMQOs);
                    results.Add($"Position: {count} vertices");
                }

                // 材質インポート
                if (_importMaterialContent)
                {
                    int count = ExecuteMaterialImport();
                    results.Add(T("MaterialMatchResult", count, _matchHelper.MQODocument.Materials.Count));
                }

                // 同期
                if (topologyChanged)
                {
                    _context?.OnTopologyChanged();
                }
                else if (_importVertexPosition)
                {
                    _context?.SyncMesh?.Invoke();
                }

                _context?.Repaint?.Invoke();
                SceneView.RepaintAll();

                _lastResult = T("ImportSuccess", string.Join(", ", results));
            }
            catch (Exception ex)
            {
                _lastResult = T("ImportFailed", ex.Message);
                Debug.LogError($"[MQOPartialImport] {ex.Message}\n{ex.StackTrace}");
            }

            Repaint();
        }

        // ================================================================
        // 頂点位置インポート
        // MQO側の頂点位置をモデル側に展開辞書ベースで転送
        // PMX展開済み頂点（1頂点1UV）に対し、MQO頂点のUV数分を同一位置で更新
        // ================================================================

        private int ExecuteVertexPositionImport(List<PartialMeshEntry> modelMeshes, List<PartialMQOEntry> mqoObjects)
        {
            int totalUpdated = 0;

            int pairCount = Math.Min(modelMeshes.Count, mqoObjects.Count);

            for (int p = 0; p < pairCount; p++)
            {
                var modelEntry = modelMeshes[p];
                var mqoEntry = mqoObjects[p];

                int count = TransferVertexPositions(modelEntry, mqoEntry);
                totalUpdated += count;
            }

            return totalUpdated;
        }

        /// <summary>
        /// MQO側の頂点位置をモデル側に転送（1ペア分）
        /// 展開辞書に従い、MQO頂点1個 → PMX展開頂点N個に同一位置を設定
        /// BakedMirrorPeerがある場合、ミラー側にもミラー変換した位置を設定
        /// </summary>
        private int TransferVertexPositions(PartialMeshEntry modelEntry, PartialMQOEntry mqoEntry)
        {
            var modelMo = modelEntry.Context?.MeshObject;
            var mqoMo = mqoEntry.MeshContext?.MeshObject;
            if (modelMo == null || mqoMo == null) return 0;

            bool isMirrored = mqoEntry.IsMirrored;
            bool hasPeer = modelEntry.BakedMirrorPeer != null;
            var peerMo = hasPeer ? modelEntry.BakedMirrorPeer.Context?.MeshObject : null;

            // MQO側: 面で使用されている頂点セット
            var mqoUsed = new HashSet<int>();
            foreach (var face in mqoMo.Faces)
                foreach (var vi in face.VertexIndices)
                    mqoUsed.Add(vi);

            // 展開辞書: MQO頂点を順に走査し、UV数分だけPMXオフセットを消費
            int pmxOffset = 0;
            int updated = 0;

            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                if (!mqoUsed.Contains(vIdx)) continue;

                var mqoVertex = mqoMo.Vertices[vIdx];
                int uvCount = Math.Max(1, mqoVertex.UVs.Count);

                // MQO → Unity座標変換
                Vector3 pos = mqoVertex.Position;
                pos *= _importScale;
                if (_flipZ) pos.z = -pos.z;

                // 実体側: UV展開数分のPMX頂点に同一位置を設定
                for (int u = 0; u < uvCount; u++)
                {
                    int idx = pmxOffset + u;
                    if (idx < modelMo.VertexCount)
                    {
                        modelMo.Vertices[idx].Position = pos;
                        updated++;
                    }
                }

                // ミラー側（MQOがミラー＆BakedMirrorPeerがある場合）
                if (isMirrored && hasPeer && peerMo != null)
                {
                    var mirrorAxis = mqoEntry.MeshContext.GetMirrorSymmetryAxis();
                    Vector3 mirrorPos = MirrorPosition(pos, mirrorAxis);

                    for (int u = 0; u < uvCount; u++)
                    {
                        int idx = pmxOffset + u;
                        if (idx < peerMo.VertexCount)
                        {
                            peerMo.Vertices[idx].Position = mirrorPos;
                            updated++;
                        }
                    }
                }

                pmxOffset += uvCount;
            }

            return updated;
        }

        // ================================================================
        // メッシュ構造インポート
        // MQO側のFace構成・UVをモデル側に転送
        // PMX展開済み頂点からBoneWeight等を引き継ぎ、MQOのUV/面構成で再構築
        // ================================================================

        private int ExecuteMeshStructureImport(List<PartialMeshEntry> modelMeshes, List<PartialMQOEntry> mqoObjects, bool alsoImportPosition)
        {
            int meshesUpdated = 0;

            // ペアで処理（選択順に1:1対応）
            int pairCount = Math.Min(modelMeshes.Count, mqoObjects.Count);

            for (int p = 0; p < pairCount; p++)
            {
                var modelEntry = modelMeshes[p];
                var mqoEntry = mqoObjects[p];

                var mqoMo = mqoEntry.MeshContext?.MeshObject;
                if (modelEntry.Context?.MeshObject == null || mqoMo == null) continue;

                TransferMeshStructure(modelEntry, mqoEntry, alsoImportPosition);
                meshesUpdated++;
            }

            return meshesUpdated;
        }

        /// <summary>
        /// MQO側のメッシュ構造をモデル側に転送（PMX展開済み頂点のBoneWeight等を引き継ぎ）
        /// 
        /// BakedMirrorPeerが存在する場合:
        ///   実体頂点/面 → modelMo、ミラー頂点/面 → peerMo に分離書き込み
        ///   oldVertices = modelMo.Vertices (実体), oldMirrorVertices = peerMo.Vertices (ミラー)
        /// BakedMirrorPeerなし＋ミラーベイク:
        ///   すべて modelMo に書き込み（oldVertices前半=実体、後半=ミラー）
        /// フラグモード:
        ///   実体のみ modelMo に書き込み、MirrorBoneWeight保持
        /// </summary>
        private void TransferMeshStructure(PartialMeshEntry modelEntry, PartialMQOEntry mqoEntry, bool alsoImportPosition)
        {
            var modelMo = modelEntry.Context.MeshObject;
            var mqoMo = mqoEntry.MeshContext.MeshObject;
            bool isMirrored = mqoEntry.IsMirrored;
            bool hasPeer = modelEntry.BakedMirrorPeer != null;
            var peerMo = hasPeer ? modelEntry.BakedMirrorPeer.Context?.MeshObject : null;

            // ================================================================
            // Step1: 現在の頂点リストを退避
            // ================================================================
            var oldRealVertices = new List<Vertex>(modelMo.VertexCount);
            foreach (var v in modelMo.Vertices)
                oldRealVertices.Add(v.Clone());

            var oldMirrorVertices = new List<Vertex>();
            if (hasPeer && peerMo != null)
            {
                // ペアあり: ミラー頂点は別MeshObjectから
                foreach (var v in peerMo.Vertices)
                    oldMirrorVertices.Add(v.Clone());
            }
            else if (isMirrored && _bakeMirror)
            {
                // ペアなし＋ベイク: oldRealVerticesの後半がミラー（分割不可、空のまま）
                // mirrorOffsetで参照する
            }

            // ================================================================
            // Step2: MQO非孤立頂点の展開辞書
            // ================================================================
            var mqoUsed = new HashSet<int>();
            foreach (var face in mqoMo.Faces)
                foreach (var vi in face.VertexIndices)
                    mqoUsed.Add(vi);

            var expandedStart = new Dictionary<int, int>();
            int realVertexCount = 0;

            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                if (!mqoUsed.Contains(vIdx)) continue;
                expandedStart[vIdx] = realVertexCount;
                int uvCount = Math.Max(1, mqoMo.Vertices[vIdx].UVs.Count);
                realVertexCount += uvCount;
            }

            // ミラー頂点の参照元
            // ペアあり: oldMirrorVertices[expandedStart[vIdx] + u]
            // ペアなし: oldRealVertices[realVertexCount + expandedStart[vIdx] + u]
            int mirrorOffsetInOld = hasPeer ? 0 : realVertexCount;

            // ================================================================
            // Step3: 実体側の新頂点リスト構築
            // ================================================================
            var newRealVertices = new List<Vertex>();
            var newRealStartMap = new Dictionary<int, int>();

            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                if (!mqoUsed.Contains(vIdx)) continue;
                newRealStartMap[vIdx] = newRealVertices.Count;

                var mqoVertex = mqoMo.Vertices[vIdx];
                int uvCount = Math.Max(1, mqoVertex.UVs.Count);
                int pmxStart = expandedStart[vIdx];

                for (int u = 0; u < uvCount; u++)
                {
                    int oldIdx = pmxStart + u;
                    Vertex newV = (oldIdx < oldRealVertices.Count) ? oldRealVertices[oldIdx].Clone() : new Vertex();

                    // UVはMQO側の値で上書き
                    newV.UVs.Clear();
                    Vector2 uv = (u < mqoVertex.UVs.Count) ? mqoVertex.UVs[u] : Vector2.zero;
                    if (_flipUV_V) uv.y = 1f - uv.y;
                    newV.UVs.Add(uv);

                    // 位置インポート
                    if (alsoImportPosition)
                    {
                        Vector3 pos = mqoVertex.Position;
                        pos *= _importScale;
                        if (_flipZ) pos.z = -pos.z;
                        newV.Position = pos;
                    }

                    // フラグモード時: ミラー側BoneWeightを保存
                    if (isMirrored && !_bakeMirror)
                    {
                        var mirrorSrc = hasPeer ? oldMirrorVertices : oldRealVertices;
                        int mirrorIdx = mirrorOffsetInOld + pmxStart + u;
                        if (mirrorIdx < mirrorSrc.Count)
                        {
                            newV.MirrorBoneWeight = mirrorSrc[mirrorIdx].BoneWeight;
                        }
                    }

                    newRealVertices.Add(newV);
                }
            }

            // ================================================================
            // Step4: ミラー側の新頂点リスト構築（ベイク時のみ）
            // ================================================================
            var newMirrorVertices = new List<Vertex>();
            var newMirrorStartMap = new Dictionary<int, int>();

            if (isMirrored && _bakeMirror)
            {
                var mirrorAxis = mqoEntry.MeshContext.GetMirrorSymmetryAxis();
                var mirrorSrc = hasPeer ? oldMirrorVertices : oldRealVertices;

                for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
                {
                    if (!mqoUsed.Contains(vIdx)) continue;
                    newMirrorStartMap[vIdx] = newMirrorVertices.Count;

                    var mqoVertex = mqoMo.Vertices[vIdx];
                    int uvCount = Math.Max(1, mqoVertex.UVs.Count);
                    int pmxStart = expandedStart[vIdx];

                    for (int u = 0; u < uvCount; u++)
                    {
                        int oldMirrorIdx = mirrorOffsetInOld + pmxStart + u;
                        Vertex newV = (oldMirrorIdx < mirrorSrc.Count) ? mirrorSrc[oldMirrorIdx].Clone() : new Vertex();

                        // UVは実体側と同じ
                        newV.UVs.Clear();
                        Vector2 uv = (u < mqoVertex.UVs.Count) ? mqoVertex.UVs[u] : Vector2.zero;
                        if (_flipUV_V) uv.y = 1f - uv.y;
                        newV.UVs.Add(uv);

                        // 位置: MQO座標をミラー変換
                        if (alsoImportPosition)
                        {
                            Vector3 pos = mqoVertex.Position;
                            pos *= _importScale;
                            if (_flipZ) pos.z = -pos.z;
                            newV.Position = MirrorPosition(pos, mirrorAxis);
                        }

                        newV.MirrorBoneWeight = null;
                        newMirrorVertices.Add(newV);
                    }
                }
            }

            // ================================================================
            // Step5: 面のインデックスをリマップ
            // ================================================================

            // --- 実体側面 ---
            var newRealFaces = new List<Face>();
            foreach (var mqoFace in mqoMo.Faces)
            {
                if (mqoFace.VertexCount < 3) continue;

                var newFace = new Face();
                newFace.MaterialIndex = mqoFace.MaterialIndex;

                for (int i = 0; i < mqoFace.VertexCount; i++)
                {
                    int bIdx = mqoFace.VertexIndices[i];
                    int uvSubIdx = (i < mqoFace.UVIndices.Count) ? mqoFace.UVIndices[i] : 0;
                    int newBase = newRealStartMap.TryGetValue(bIdx, out int nb) ? nb : 0;

                    newFace.VertexIndices.Add(newBase + uvSubIdx);
                    newFace.UVIndices.Add(0);
                    newFace.NormalIndices.Add(0);
                }
                newRealFaces.Add(newFace);
            }

            // --- ミラー側面（ベイク時のみ） ---
            var newMirrorFaces = new List<Face>();
            if (isMirrored && _bakeMirror)
            {
                int matOffset = mqoEntry.MirrorMaterialOffset;

                foreach (var mqoFace in mqoMo.Faces)
                {
                    if (mqoFace.VertexCount < 3) continue;

                    var mirrorFace = new Face();
                    mirrorFace.MaterialIndex = mqoFace.MaterialIndex + matOffset;

                    // 頂点順序を反転（法線方向維持）
                    for (int i = mqoFace.VertexCount - 1; i >= 0; i--)
                    {
                        int bIdx = mqoFace.VertexIndices[i];
                        int uvSubIdx = (i < mqoFace.UVIndices.Count) ? mqoFace.UVIndices[i] : 0;
                        int newBase = newMirrorStartMap.TryGetValue(bIdx, out int nb) ? nb : 0;

                        mirrorFace.VertexIndices.Add(newBase + uvSubIdx);
                        mirrorFace.UVIndices.Add(0);
                        mirrorFace.NormalIndices.Add(0);
                    }
                    newMirrorFaces.Add(mirrorFace);
                }
            }

            // ================================================================
            // MeshObjectに反映
            // ================================================================
            if (hasPeer && peerMo != null && isMirrored && _bakeMirror)
            {
                // ペアあり: 実体→modelMo、ミラー→peerMo に分離書き込み
                modelMo.Vertices.Clear();
                modelMo.Vertices.AddRange(newRealVertices);
                modelMo.Faces.Clear();
                modelMo.Faces.AddRange(newRealFaces);
                RecalculateNormals(modelMo);

                peerMo.Vertices.Clear();
                peerMo.Vertices.AddRange(newMirrorVertices);
                peerMo.Faces.Clear();
                peerMo.Faces.AddRange(newMirrorFaces);
                RecalculateNormals(peerMo);

                Debug.Log($"[MQOPartialImport] MeshStructure: real={newRealVertices.Count}v/{newRealFaces.Count}f, mirror={newMirrorVertices.Count}v/{newMirrorFaces.Count}f (peer split)");
            }
            else
            {
                // ペアなし or フラグモード: すべてmodelMoに書き込み
                modelMo.Vertices.Clear();
                modelMo.Vertices.AddRange(newRealVertices);
                if (isMirrored && _bakeMirror)
                {
                    modelMo.Vertices.AddRange(newMirrorVertices);

                    // ミラー面のインデックスを実体頂点数分オフセット
                    int offset = newRealVertices.Count;
                    foreach (var face in newMirrorFaces)
                    {
                        for (int i = 0; i < face.VertexIndices.Count; i++)
                            face.VertexIndices[i] += offset;
                    }
                }

                modelMo.Faces.Clear();
                modelMo.Faces.AddRange(newRealFaces);
                if (isMirrored && _bakeMirror)
                    modelMo.Faces.AddRange(newMirrorFaces);

                RecalculateNormals(modelMo);

                Debug.Log($"[MQOPartialImport] MeshStructure: old={oldRealVertices.Count} → new={modelMo.VertexCount} verts, {modelMo.FaceCount} faces" +
                          (isMirrored ? $" (mirror={(_bakeMirror ? "bake" : "flag")})" : ""));
            }
        }

        /// <summary>
        /// 位置をミラー変換
        /// </summary>
        private static Vector3 MirrorPosition(Vector3 pos, Poly_Ling.Symmetry.SymmetryAxis axis)
        {
            switch (axis)
            {
                case Poly_Ling.Symmetry.SymmetryAxis.X: return new Vector3(-pos.x, pos.y, pos.z);
                case Poly_Ling.Symmetry.SymmetryAxis.Y: return new Vector3(pos.x, -pos.y, pos.z);
                case Poly_Ling.Symmetry.SymmetryAxis.Z: return new Vector3(pos.x, pos.y, -pos.z);
                default: return new Vector3(-pos.x, pos.y, pos.z);
            }
        }

        /// <summary>
        /// 法線を再計算
        /// </summary>
        private void RecalculateNormals(MeshObject mo)
        {
            if (_normalMode == NormalMode.FaceNormal)
            {
                // フラットシェーディング: 面法線をそのまま使用
                foreach (var vertex in mo.Vertices)
                {
                    vertex.Normals.Clear();
                }

                foreach (var face in mo.Faces)
                {
                    if (face.VertexCount < 3) continue;
                    var v0 = mo.Vertices[face.VertexIndices[0]].Position;
                    var v1 = mo.Vertices[face.VertexIndices[1]].Position;
                    var v2 = mo.Vertices[face.VertexIndices[2]].Position;
                    var normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                    face.NormalIndices.Clear();
                    for (int i = 0; i < face.VertexCount; i++)
                    {
                        int vIdx = face.VertexIndices[i];
                        int nIdx = mo.Vertices[vIdx].GetOrAddNormal(normal, 0.0001f);
                        face.NormalIndices.Add(nIdx);
                    }
                }
            }
            else if (_normalMode == NormalMode.Smooth)
            {
                // スムーズシェーディング: スムージング角度で平均化
                SmoothNormals(mo, _smoothingAngle);
            }
            // NormalMode.Unity の場合は ToUnityMeshShared 側で RecalculateNormals が呼ばれるため何もしない
        }

        /// <summary>
        /// スムージング角度に基づく法線平均化
        /// </summary>
        private void SmoothNormals(MeshObject mo, float angle)
        {
            float cosThreshold = Mathf.Cos(angle * Mathf.Deg2Rad);

            // 面法線を先に計算
            var faceNormals = new List<Vector3>();
            foreach (var face in mo.Faces)
            {
                if (face.VertexCount < 3)
                {
                    faceNormals.Add(Vector3.up);
                    continue;
                }
                var v0 = mo.Vertices[face.VertexIndices[0]].Position;
                var v1 = mo.Vertices[face.VertexIndices[1]].Position;
                var v2 = mo.Vertices[face.VertexIndices[2]].Position;
                faceNormals.Add(Vector3.Cross(v1 - v0, v2 - v0).normalized);
            }

            // 頂点→参照面のマッピング
            var vertexFaces = new Dictionary<int, List<int>>();
            for (int fIdx = 0; fIdx < mo.Faces.Count; fIdx++)
            {
                foreach (var vIdx in mo.Faces[fIdx].VertexIndices)
                {
                    if (!vertexFaces.ContainsKey(vIdx))
                        vertexFaces[vIdx] = new List<int>();
                    vertexFaces[vIdx].Add(fIdx);
                }
            }

            // 法線クリア
            foreach (var vertex in mo.Vertices)
                vertex.Normals.Clear();

            // 各面・各頂点について、隣接面法線をスムージング角度内で平均化
            for (int fIdx = 0; fIdx < mo.Faces.Count; fIdx++)
            {
                var face = mo.Faces[fIdx];
                var fn = faceNormals[fIdx];
                face.NormalIndices.Clear();

                for (int i = 0; i < face.VertexCount; i++)
                {
                    int vIdx = face.VertexIndices[i];
                    Vector3 smoothed = fn;

                    if (vertexFaces.TryGetValue(vIdx, out var adjFaces))
                    {
                        smoothed = Vector3.zero;
                        foreach (var adjFIdx in adjFaces)
                        {
                            if (Vector3.Dot(fn, faceNormals[adjFIdx]) >= cosThreshold)
                            {
                                smoothed += faceNormals[adjFIdx];
                            }
                        }
                        smoothed = smoothed.normalized;
                        if (smoothed == Vector3.zero) smoothed = fn;
                    }

                    int nIdx = mo.Vertices[vIdx].GetOrAddNormal(smoothed, 0.001f);
                    face.NormalIndices.Add(nIdx);
                }
            }
        }

        // ================================================================
        // 材質インポート（名前ベースマッチング）
        // ================================================================

        private class MaterialMatch
        {
            public MQOMaterial MqoMaterial;
            public MaterialReference ModelMaterialRef;
            public int ModelMaterialIndex;
        }

        private List<MaterialMatch> BuildMaterialMatches()
        {
            var matches = new List<MaterialMatch>();
            if (_matchHelper.MQODocument == null || Model == null) return matches;

            var modelMatRefs = Model.MaterialReferences;
            if (modelMatRefs == null) return matches;

            foreach (var mqoMat in _matchHelper.MQODocument.Materials)
            {
                // 名前ベースマッチング
                for (int i = 0; i < modelMatRefs.Count; i++)
                {
                    var modelRef = modelMatRefs[i];
                    if (modelRef?.Name == mqoMat.Name)
                    {
                        matches.Add(new MaterialMatch
                        {
                            MqoMaterial = mqoMat,
                            ModelMaterialRef = modelRef,
                            ModelMaterialIndex = i
                        });
                        break;
                    }
                }
            }

            return matches;
        }

        /// <summary>
        /// 材質インポート実行
        /// </summary>
        /// <returns>更新した材質数</returns>
        private int ExecuteMaterialImport()
        {
            var matches = BuildMaterialMatches();
            int updated = 0;

            foreach (var match in matches)
            {
                var mqoMat = match.MqoMaterial;
                var modelRef = match.ModelMaterialRef;

                // MaterialDataを更新
                var data = modelRef.Data;
                if (data == null) continue;

                // 色
                data.SetBaseColor(mqoMat.Color);

                // PBRパラメータ
                data.Smoothness = mqoMat.Specular;

                // テクスチャソースパス
                if (!string.IsNullOrEmpty(mqoMat.TexturePath))
                    data.SourceTexturePath = mqoMat.TexturePath;
                if (!string.IsNullOrEmpty(mqoMat.AlphaMapPath))
                    data.SourceAlphaMapPath = mqoMat.AlphaMapPath;
                if (!string.IsNullOrEmpty(mqoMat.BumpMapPath))
                    data.SourceBumpMapPath = mqoMat.BumpMapPath;

                // 透過設定
                if (mqoMat.Color.a < 1f - 0.001f)
                {
                    data.Surface = SurfaceType.Transparent;
                }

                // Materialインスタンスを再生成（Data更新を反映）
                modelRef.RefreshFromData();

                updated++;
                Debug.Log($"[MQOPartialImport] Material updated: {mqoMat.Name}");
            }

            return updated;
        }
    }
}
