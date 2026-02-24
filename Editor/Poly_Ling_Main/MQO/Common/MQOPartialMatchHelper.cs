// Assets/Editor/Poly_Ling/MQO/Common/MQOPartialMatchHelper.cs
// MQO部分エクスポート/インポート共通のメッシュマッチングロジック
// 左リスト（モデル側）と右リスト（MQO側）の構築・半自動照合・描画

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
    /// モデル側メッシュエントリ
    /// </summary>
    public class PartialMeshEntry
    {
        public bool Selected;
        public int Index;              // DrawableMeshes内のインデックス
        public string Name;
        public int VertexCount;        // 生頂点数
        public int ExpandedVertexCount; // 展開後頂点数
        public bool IsBakedMirror;
        public MeshContext Context;
        public HashSet<int> IsolatedVertices;

        /// <summary>ベイクミラーのペア（ソース側に設定される）</summary>
        public PartialMeshEntry BakedMirrorPeer;

        /// <summary>ペア含む展開後頂点数合計</summary>
        public int TotalExpandedVertexCount => ExpandedVertexCount + (BakedMirrorPeer?.ExpandedVertexCount ?? 0);
    }

    /// <summary>
    /// MQO側オブジェクトエントリ
    /// </summary>
    public class PartialMQOEntry
    {
        public bool Selected;
        public int Index;              // MQODocument.Objects内のインデックス
        public string Name;
        public int VertexCount;        // 生頂点数
        public int ExpandedVertexCount; // 展開後頂点数
        public MeshContext MeshContext; // インポート結果のMeshContext

        // ミラー情報
        public bool IsMirrored;
        public int MirrorType;
        public int MirrorAxis;
        public float MirrorDistance;
        public int MirrorMaterialOffset;

        /// <summary>ミラー考慮の展開後頂点数（ベイク時の期待値）</summary>
        public int ExpandedVertexCountWithMirror => ExpandedVertexCount * (IsMirrored ? 2 : 1);
    }

    /// <summary>
    /// MQO部分エクスポート/インポート共通ヘルパー
    /// </summary>
    public class MQOPartialMatchHelper
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["ModelMeshes"] = new() { ["en"] = "Model Meshes", ["ja"] = "モデルメッシュ" },
            ["MQOObjects"] = new() { ["en"] = "MQO Objects", ["ja"] = "MQOオブジェクト" },
            ["SelectAll"] = new() { ["en"] = "All", ["ja"] = "全選択" },
            ["SelectNone"] = new() { ["en"] = "None", ["ja"] = "全解除" },
            ["NoContext"] = new() { ["en"] = "No context. Open from Poly_Ling.", ["ja"] = "コンテキスト未設定" },
            ["NoModel"] = new() { ["en"] = "No model loaded", ["ja"] = "モデルなし" },
            ["SelectMQOFirst"] = new() { ["en"] = "Select MQO file", ["ja"] = "MQOファイルを選択" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);

        // ================================================================
        // データ
        // ================================================================

        public List<PartialMeshEntry> ModelMeshes { get; } = new List<PartialMeshEntry>();
        public List<PartialMQOEntry> MQOObjects { get; } = new List<PartialMQOEntry>();
        public MQODocument MQODocument { get; private set; }
        public MQOImportResult MQOImportResult { get; private set; }

        // ================================================================
        // モデルリスト構築
        // ================================================================

        /// <summary>
        /// モデル側メッシュリストを構築
        /// pairMirrors=true: BakedMirrorおよび名前末尾「+」のミラーメッシュをソースとペア統合する
        /// pairMirrors=false: skipBakedMirror/skipNamedMirrorで個別スキップ（エクスポート用）
        /// </summary>
        public void BuildModelList(ModelContext model, bool skipBakedMirror, bool skipNamedMirror, bool pairMirrors = false)
        {
            ModelMeshes.Clear();

            if (model == null) return;

            var drawables = model.DrawableMeshes;
            if (drawables == null) return;

            if (pairMirrors)
            {
                BuildModelListWithPairing(drawables);
            }
            else
            {
                BuildModelListSimple(drawables, skipBakedMirror, skipNamedMirror);
            }
        }

        /// <summary>
        /// ペア統合なしのシンプルなリスト構築（エクスポート用）
        /// </summary>
        private void BuildModelListSimple(IReadOnlyList<Poly_Ling.Data.TypedMeshEntry> drawables, bool skipBakedMirror, bool skipNamedMirror)
        {
            for (int i = 0; i < drawables.Count; i++)
            {
                var entry = drawables[i];
                var ctx = entry.Context;
                if (ctx?.MeshObject == null) continue;

                var mo = ctx.MeshObject;
                if (mo.VertexCount == 0) continue;

                if (skipBakedMirror && ctx.IsBakedMirror) continue;
                if (skipNamedMirror && !ctx.IsBakedMirror &&
                    !string.IsNullOrEmpty(ctx.Name) && ctx.Name.EndsWith("+")) continue;

                var isolated = PMXMQOTransferPanel.GetIsolatedVertices(mo);
                int expandedCount = PMXMQOTransferPanel.CalculateExpandedVertexCount(mo, isolated);

                ModelMeshes.Add(new PartialMeshEntry
                {
                    Selected = false,
                    Index = i,
                    Name = ctx.Name,
                    VertexCount = mo.VertexCount,
                    ExpandedVertexCount = expandedCount,
                    IsBakedMirror = ctx.IsBakedMirror,
                    Context = ctx,
                    IsolatedVertices = isolated
                });
            }
        }

        /// <summary>
        /// ミラーペア統合ありのリスト構築（インポート用）
        /// BakedMirrorおよび名前末尾「+」のメッシュをソースのBakedMirrorPeerとして統合
        /// </summary>
        private void BuildModelListWithPairing(IReadOnlyList<Poly_Ling.Data.TypedMeshEntry> drawables)
        {
            // ペア化済みインデックスを記録
            var pairedIndices = new HashSet<int>();

            // 1パス目: BakedMirrorをペア化対象としてマーク
            for (int i = 0; i < drawables.Count; i++)
            {
                var ctx = drawables[i].Context;
                if (ctx == null) continue;
                if (ctx.IsBakedMirror)
                    pairedIndices.Add(i);
            }

            // 2パス目: 名前末尾「+」のメッシュをペア化対象としてマーク
            for (int i = 0; i < drawables.Count; i++)
            {
                if (pairedIndices.Contains(i)) continue;
                var ctx = drawables[i].Context;
                if (ctx == null) continue;
                if (!string.IsNullOrEmpty(ctx.Name) && ctx.Name.EndsWith("+"))
                    pairedIndices.Add(i);
            }

            // 3パス目: ソース側エントリを構築し、ペアを探す
            for (int i = 0; i < drawables.Count; i++)
            {
                if (pairedIndices.Contains(i)) continue;

                var ctx = drawables[i].Context;
                if (ctx?.MeshObject == null) continue;

                var mo = ctx.MeshObject;
                if (mo.VertexCount == 0) continue;

                var isolated = PMXMQOTransferPanel.GetIsolatedVertices(mo);
                int expandedCount = PMXMQOTransferPanel.CalculateExpandedVertexCount(mo, isolated);

                var meshEntry = new PartialMeshEntry
                {
                    Selected = false,
                    Index = i,
                    Name = ctx.Name,
                    VertexCount = mo.VertexCount,
                    ExpandedVertexCount = expandedCount,
                    IsBakedMirror = false,
                    Context = ctx,
                    IsolatedVertices = isolated
                };

                // ペアを探す
                // 優先1: HasBakedMirrorChild → 直後のBakedMirror
                if (ctx.HasBakedMirrorChild)
                {
                    for (int j = i + 1; j < drawables.Count; j++)
                    {
                        var peerCtx = drawables[j].Context;
                        if (peerCtx == null) continue;
                        if (!peerCtx.IsBakedMirror) break;

                        meshEntry.BakedMirrorPeer = BuildPeerEntry(j, peerCtx);
                        break;
                    }
                }

                // 優先2: 名前「+」マッチ（PMX由来モデル用）
                if (meshEntry.BakedMirrorPeer == null && !string.IsNullOrEmpty(ctx.Name))
                {
                    string peerName = ctx.Name + "+";
                    for (int j = 0; j < drawables.Count; j++)
                    {
                        if (j == i) continue;
                        if (!pairedIndices.Contains(j)) continue;

                        var peerCtx = drawables[j].Context;
                        if (peerCtx?.Name == peerName && peerCtx.MeshObject != null && peerCtx.MeshObject.VertexCount > 0)
                        {
                            meshEntry.BakedMirrorPeer = BuildPeerEntry(j, peerCtx);
                            break;
                        }
                    }
                }

                ModelMeshes.Add(meshEntry);
            }
        }

        private PartialMeshEntry BuildPeerEntry(int index, MeshContext peerCtx)
        {
            var peerMo = peerCtx.MeshObject;
            var peerIsolated = PMXMQOTransferPanel.GetIsolatedVertices(peerMo);
            int peerExpanded = PMXMQOTransferPanel.CalculateExpandedVertexCount(peerMo, peerIsolated);

            return new PartialMeshEntry
            {
                Selected = false,
                Index = index,
                Name = peerCtx.Name,
                VertexCount = peerMo.VertexCount,
                ExpandedVertexCount = peerExpanded,
                IsBakedMirror = true,
                Context = peerCtx,
                IsolatedVertices = peerIsolated
            };
        }

        // ================================================================
        // MQOリスト構築
        // ================================================================

        /// <summary>
        /// MQOファイルを読み込み、MQO側リストを構築
        /// </summary>
        /// <param name="filePath">MQOファイルパス</param>
        /// <param name="flipZ">Z反転</param>
        /// <param name="visibleOnly">表示オブジェクトのみ</param>
        /// <returns>読み込み成功</returns>
        public bool LoadMQO(string filePath, bool flipZ, bool visibleOnly)
        {
            MQODocument = null;
            MQOImportResult = null;
            MQOObjects.Clear();

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                // MQODocumentをパース
                MQODocument = MQOParser.ParseFile(filePath);

                // MQOをインポートしてMeshContextを取得（展開後頂点数計算用）
                var settings = new MQOImportSettings
                {
                    ImportMaterials = false,
                    SkipHiddenObjects = visibleOnly,
                    MergeObjects = false,
                    FlipZ = flipZ,
                    FlipUV_V = false,
                    BakeMirror = false
                };
                MQOImportResult = MQOImporter.ImportFile(filePath, settings);

                if (MQOImportResult == null || !MQOImportResult.Success)
                    return false;

                BuildMQOList();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MQOPartialMatchHelper] Load failed: {ex.Message}");
                MQODocument = null;
                MQOImportResult = null;
                return false;
            }
        }

        /// <summary>
        /// MQOインポート結果からMQO側リストを構築
        /// </summary>
        public void BuildMQOList()
        {
            MQOObjects.Clear();

            if (MQOImportResult == null || !MQOImportResult.Success) return;

            foreach (var meshContext in MQOImportResult.MeshContexts)
            {
                var mo = meshContext.MeshObject;
                if (mo == null || mo.VertexCount == 0) continue;

                // BakedMirrorはスキップ（ソース側のみリストに含める）
                if (meshContext.IsBakedMirror) continue;

                var isolated = PMXMQOTransferPanel.GetIsolatedVertices(mo);
                int expandedCount = PMXMQOTransferPanel.CalculateExpandedVertexCount(mo, isolated);

                MQOObjects.Add(new PartialMQOEntry
                {
                    Selected = false,
                    Index = MQOObjects.Count,
                    Name = meshContext.Name,
                    VertexCount = mo.VertexCount,
                    ExpandedVertexCount = expandedCount,
                    MeshContext = meshContext,
                    // ミラー情報
                    IsMirrored = meshContext.IsMirrored,
                    MirrorType = meshContext.MirrorType,
                    MirrorAxis = meshContext.MirrorAxis,
                    MirrorDistance = meshContext.MirrorDistance,
                    MirrorMaterialOffset = meshContext.MirrorMaterialOffset
                });
            }
        }

        // ================================================================
        // 半自動マッチング
        // ================================================================

        /// <summary>
        /// 展開後頂点数ベースの半自動マッチング
        /// MQO側がミラーの場合は ×2 で照合
        /// モデル側がBakedMirrorペアの場合はペア合計で照合
        /// </summary>
        public void AutoMatch()
        {
            // リセット
            foreach (var model in ModelMeshes) model.Selected = false;
            foreach (var mqo in MQOObjects) mqo.Selected = false;

            // 展開後頂点数で照合
            foreach (var model in ModelMeshes)
            {
                int modelTotal = model.TotalExpandedVertexCount;
                if (modelTotal == 0) continue;

                var match = MQOObjects.FirstOrDefault(m =>
                    !m.Selected &&
                    m.ExpandedVertexCountWithMirror == modelTotal &&
                    m.ExpandedVertexCount > 0);
                if (match != null)
                {
                    model.Selected = true;
                    match.Selected = true;
                }
            }
        }

        // ================================================================
        // 選択情報取得
        // ================================================================

        /// <summary>選択中のモデルメッシュ</summary>
        public List<PartialMeshEntry> SelectedModelMeshes => ModelMeshes.Where(m => m.Selected).ToList();

        /// <summary>選択中のMQOオブジェクト</summary>
        public List<PartialMQOEntry> SelectedMQOObjects => MQOObjects.Where(m => m.Selected).ToList();

        /// <summary>選択中のモデル展開後頂点数合計（ペア含む）</summary>
        public int SelectedModelVertexCount => ModelMeshes.Where(m => m.Selected).Sum(m => m.TotalExpandedVertexCount);

        /// <summary>選択中のMQO展開後頂点数合計（ミラー考慮）</summary>
        public int SelectedMQOVertexCount => MQOObjects.Where(m => m.Selected).Sum(m => m.ExpandedVertexCountWithMirror);

        // ================================================================
        // GUI描画ヘルパー
        // ================================================================

        /// <summary>
        /// 左右リストセクションを描画
        /// </summary>
        /// <returns>描画できたか（コンテキスト・MQO未設定時はfalse）</returns>
        public bool DrawDualListSection(ToolContext context, float windowWidth,
            ref Vector2 scrollLeft, ref Vector2 scrollRight)
        {
            if (context == null)
            {
                EditorGUILayout.HelpBox(T("NoContext"), MessageType.Warning);
                return false;
            }
            if (context.Model == null)
            {
                EditorGUILayout.HelpBox(T("NoModel"), MessageType.Warning);
                return false;
            }
            if (MQODocument == null)
            {
                EditorGUILayout.HelpBox(T("SelectMQOFirst"), MessageType.Info);
                return false;
            }

            float halfWidth = (windowWidth - 30) / 2;

            using (new EditorGUILayout.HorizontalScope())
            {
                // 左リスト（MQO側）
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(halfWidth)))
                {
                    DrawListHeader(T("MQOObjects"), false);
                    scrollLeft = EditorGUILayout.BeginScrollView(scrollLeft, GUILayout.Height(300));
                    DrawMQOList();
                    EditorGUILayout.EndScrollView();
                }

                // 右リスト（モデル側）
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(halfWidth)))
                {
                    DrawListHeader(T("ModelMeshes"), true);
                    scrollRight = EditorGUILayout.BeginScrollView(scrollRight, GUILayout.Height(300));
                    DrawModelList();
                    EditorGUILayout.EndScrollView();
                }
            }

            return true;
        }

        private void DrawListHeader(string title, bool isModel)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(T("SelectAll"), GUILayout.Width(50)))
                {
                    if (isModel)
                        foreach (var m in ModelMeshes) m.Selected = true;
                    else
                        foreach (var m in MQOObjects) m.Selected = true;
                }
                if (GUILayout.Button(T("SelectNone"), GUILayout.Width(50)))
                {
                    if (isModel)
                        foreach (var m in ModelMeshes) m.Selected = false;
                    else
                        foreach (var m in MQOObjects) m.Selected = false;
                }
            }
        }

        private void DrawModelList()
        {
            foreach (var entry in ModelMeshes)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));

                    string label;
                    if (entry.BakedMirrorPeer != null)
                    {
                        // ペア統合表示
                        label = $"{entry.Name} (+ {entry.BakedMirrorPeer.Name}) [{entry.TotalExpandedVertexCount}]";
                        GUI.color = new Color(1f, 0.85f, 0.6f); // ミラーペアは暖色
                    }
                    else
                    {
                        label = $"{entry.Name} ({entry.ExpandedVertexCount})";
                    }

                    EditorGUILayout.LabelField(label);
                    GUI.color = Color.white;
                }
            }
        }

        private void DrawMQOList()
        {
            foreach (var entry in MQOObjects)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));

                    string mirror = entry.IsMirrored ? " [M]" : "";
                    string countStr = entry.IsMirrored
                        ? $"{entry.ExpandedVertexCount}×2={entry.ExpandedVertexCountWithMirror}"
                        : $"{entry.ExpandedVertexCount}";

                    if (entry.IsMirrored) GUI.color = new Color(1f, 0.85f, 0.6f);
                    EditorGUILayout.LabelField($"{entry.Name}{mirror} ({countStr})");
                    GUI.color = Color.white;
                }
            }
        }

        // ================================================================
        // ファイルドロップヘルパー
        // ================================================================

        /// <summary>
        /// Rectへのドラッグ&ドロップ処理
        /// </summary>
        public static void HandleDropOnRect(Rect rect, string ext, Action<string> onDrop)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            if (evt.type == EventType.DragUpdated)
            {
                if (DragAndDrop.paths.Length > 0 && Path.GetExtension(DragAndDrop.paths[0]).ToLower() == ext)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    evt.Use();
                }
            }
            else if (evt.type == EventType.DragPerform)
            {
                if (DragAndDrop.paths.Length > 0 && Path.GetExtension(DragAndDrop.paths[0]).ToLower() == ext)
                {
                    DragAndDrop.AcceptDrag();
                    onDrop(DragAndDrop.paths[0]);
                    evt.Use();
                }
            }
        }
    }
}
