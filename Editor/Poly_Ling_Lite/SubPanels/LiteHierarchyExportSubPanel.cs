// LiteHierarchyExportSubPanel.cs
// ModelContext を Unity ヒエラルキーに書き出すサブパネル。
//
// ■ 書き出し構造
//   <ModelName>                   ← ルート GameObject
//     Armature                   ← ボーン階層ルート（ボーンが存在する場合のみ）
//       <BoneName> ...           ← ボーン Transform ツリー
//     <MeshName>                 ← SkinnedMeshRenderer または MeshFilter+MeshRenderer
//
// ■ スキニング
//   MeshObject.HasBoneWeight == true → SkinnedMeshRenderer
//     MeshContext.BindPose を bindposes として設定
//     MeshContext.BoneTransform の WorldMatrix でボーン Transform を配置
//   それ以外                          → MeshFilter + MeshRenderer
//
// Editor/Poly_Ling_Lite/SubPanels/ に配置

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Lite
{
    public class LiteHierarchyExportSubPanel
    {
        // ================================================================
        // コールバック
        // ================================================================

        public Func<ModelContext> GetModel;

        // ================================================================
        // オプション状態
        // ================================================================

        private bool _createArmature     = true;
        private bool _useBindpose        = true;
        private bool _exportVisibleOnly  = false;
        private bool _exportMeshOnly     = false;  // ボーン除外

        // ================================================================
        // UI
        // ================================================================

        private Label _statusLabel;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            parent.Clear();

            var title = new Label("ヒエラルキーエクスポート");
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6;
            parent.Add(title);

            // オプション
            parent.Add(SectionLabel("オプション"));
            parent.Add(ToggleRow("Armatureを生成",    () => _createArmature,    v => _createArmature    = v));
            parent.Add(ToggleRow("BindPoseを使用",     () => _useBindpose,       v => _useBindpose       = v));
            parent.Add(ToggleRow("可視メッシュのみ",    () => _exportVisibleOnly, v => _exportVisibleOnly = v));
            parent.Add(ToggleRow("メッシュのみ（ボーン除外）", () => _exportMeshOnly, v => _exportMeshOnly = v));
            parent.Add(Separator());

            // 実行ボタン
            var exportBtn = new Button(OnExport) { text = "シーンに書き出し" };
            exportBtn.style.height  = 32;
            exportBtn.style.marginTop = 4;
            exportBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(exportBtn);

            // ステータス
            _statusLabel = new Label("");
            _statusLabel.style.color      = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.marginTop  = 4;
            parent.Add(_statusLabel);
        }

        public void Refresh()
        {
            var model = GetModel?.Invoke();
            if (_statusLabel == null) return;
            if (model == null) { _statusLabel.text = "モデルが未ロードです"; return; }
            _statusLabel.text = $"{model.Name}  ({model.Count} メッシュ)";
        }

        // ================================================================
        // エクスポート実行
        // ================================================================

        private void OnExport()
        {
            var model = GetModel?.Invoke();
            if (model == null) { SetStatus("モデルが未ロードです"); return; }

            try
            {
                var root = Export(model);
                if (root != null)
                {
                    UnityEditor.Selection.activeGameObject = root;
                    SetStatus($"完了: {root.name}");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"例外: {ex.Message}");
                Debug.LogError($"[LiteHierarchyExportSubPanel] {ex}");
            }
        }

        private void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        // ================================================================
        // 書き出しロジック
        // ================================================================

        /// <summary>ModelContext を Unity ヒエラルキーに書き出し、ルート GameObject を返す。</summary>
        private GameObject Export(ModelContext model)
        {
            Undo.SetCurrentGroupName("PolyLing Lite: Export to Hierarchy");
            int undoGroup = Undo.GetCurrentGroup();

            // ── ルート ────────────────────────────────────────────────
            var rootGo = new GameObject(string.IsNullOrEmpty(model.Name) ? "Model" : model.Name);
            Undo.RegisterCreatedObjectUndo(rootGo, "Create Root");

            // ── ボーン Transform ツリーを構築 ─────────────────────────
            // boneTransforms[i] = MeshContextList インデックス i の SkinnedMesh 用 Transform
            // ボーン MeshContext のみ対象
            Transform armatureRoot = null;
            var boneTransformMap = new Dictionary<int, Transform>(); // ctxIndex → Transform

            if (_createArmature && !_exportMeshOnly)
            {
                bool hasBones = false;
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc?.Type == MeshType.Bone) { hasBones = true; break; }
                }

                if (hasBones)
                {
                    var armatureGo = new GameObject("Armature");
                    Undo.RegisterCreatedObjectUndo(armatureGo, "Create Armature");
                    armatureGo.transform.SetParent(rootGo.transform, worldPositionStays: false);
                    armatureRoot = armatureGo.transform;

                    // 1パス目: 全ボーンの Transform を生成
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;

                        var boneGo = new GameObject(mc.Name ?? $"Bone_{i}");
                        Undo.RegisterCreatedObjectUndo(boneGo, "Create Bone");
                        boneTransformMap[i] = boneGo.transform;
                    }

                    // 2パス目: 親子関係設定 → ワールド位置設定
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;

                        var boneTf = boneTransformMap[i];
                        int parentIdx = mc.HierarchyParentIndex;

                        if (parentIdx >= 0 && boneTransformMap.TryGetValue(parentIdx, out var parentTf))
                            boneTf.SetParent(parentTf, worldPositionStays: false);
                        else
                            boneTf.SetParent(armatureRoot, worldPositionStays: false);
                    }

                    // 3パス目: ワールド位置設定（親子確定後）
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;

                        var boneTf = boneTransformMap[i];
                        var wm = mc.WorldMatrix;
                        boneTf.position   = new Vector3(wm.m03, wm.m13, wm.m23);
                        boneTf.rotation   = wm.rotation;
                        boneTf.localScale = Vector3.one;
                    }
                }
            }

            // ── メッシュ書き出し ──────────────────────────────────────
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                if (mc.Type != MeshType.Mesh)    continue; // ボーン・モーフは除外
                if (mc.MeshObject == null)        continue;
                if (_exportVisibleOnly && !mc.IsVisible) continue;

                var unityMesh = mc.UnityMesh != null ? mc.UnityMesh : mc.MeshObject.ToUnityMesh();
                if (unityMesh == null) continue;

                var meshGo = new GameObject(mc.Name ?? $"Mesh_{i}");
                Undo.RegisterCreatedObjectUndo(meshGo, "Create Mesh GameObject");
                meshGo.transform.SetParent(rootGo.transform, worldPositionStays: false);

                if (mc.MeshObject.HasBoneWeight && boneTransformMap.Count > 0)
                    AttachSkinnedMesh(meshGo, mc, unityMesh, model, boneTransformMap, armatureRoot);
                else
                    AttachStaticMesh(meshGo, mc, unityMesh, model);
            }

            Undo.CollapseUndoOperations(undoGroup);
            return rootGo;
        }

        // ================================================================
        // SkinnedMeshRenderer アタッチ
        // ================================================================

        private void AttachSkinnedMesh(
            GameObject go,
            MeshContext mc,
            Mesh unityMesh,
            ModelContext model,
            Dictionary<int, Transform> boneTransformMap,
            Transform armatureRoot)
        {
            var smr = Undo.AddComponent<SkinnedMeshRenderer>(go);

            // ── ボーン配列 ────────────────────────────────────────────
            // BoneWeight の bone0-3 インデックスは MeshContext の BoneIndex に対応
            // MeshContext.BoneIndex は MeshContextList 内のインデックス
            var boneList = new List<Transform>();
            var bindposes = new List<Matrix4x4>();

            for (int bi = 0; bi < model.MeshContextCount; bi++)
            {
                var bmc = model.GetMeshContext(bi);
                if (bmc == null || bmc.Type != MeshType.Bone) continue;
                if (!boneTransformMap.TryGetValue(bi, out var boneTf)) continue;

                boneList.Add(boneTf);
                bindposes.Add(_useBindpose ? bmc.BindPose : boneTf.worldToLocalMatrix);
            }

            // BoneWeight をメッシュに設定する前にボーン配列を設定
            var mesh = UnityEngine.Object.Instantiate(unityMesh);
            mesh.name = unityMesh.name;
            mesh.bindposes = bindposes.ToArray();

            smr.sharedMesh  = mesh;
            smr.bones       = boneList.ToArray();
            smr.rootBone    = armatureRoot;

            // ── マテリアル ────────────────────────────────────────────
            smr.sharedMaterials = BuildMaterials(mc, model);
        }

        // ================================================================
        // 静的メッシュ（MeshFilter + MeshRenderer）アタッチ
        // ================================================================

        private void AttachStaticMesh(GameObject go, MeshContext mc, Mesh unityMesh, ModelContext model)
        {
            var mf = Undo.AddComponent<MeshFilter>(go);
            var mr = Undo.AddComponent<MeshRenderer>(go);

            mf.sharedMesh = unityMesh;

            var wm = mc.WorldMatrix;
            go.transform.position   = new Vector3(wm.m03, wm.m13, wm.m23);
            go.transform.rotation   = wm.rotation;
            go.transform.localScale = wm.lossyScale;

            mr.sharedMaterials = BuildMaterials(mc, model);
        }

        // ================================================================
        // マテリアル配列生成
        // ================================================================

        private static Material[] BuildMaterials(MeshContext mc, ModelContext model)
        {
            int subMeshCount = Mathf.Max(1, mc.MeshObject?.SubMeshCount ?? 1);
            var mats = new Material[subMeshCount];

            var matRefs = model?.MaterialReferences;
            var defaultMat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");

            for (int m = 0; m < subMeshCount; m++)
            {
                Material mat = null;
                if (matRefs != null && m < matRefs.Count)
                    mat = matRefs[m]?.Material;
                mats[m] = mat != null ? mat : defaultMat;
            }
            return mats;
        }

        // ================================================================
        // UI パーツ ヘルパー
        // ================================================================

        private static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.marginTop    = 6;
            l.style.marginBottom = 2;
            l.style.color        = new StyleColor(new Color(0.7f, 0.85f, 1f));
            l.style.fontSize     = 10;
            return l;
        }

        private static VisualElement Separator()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 4;
            v.style.marginBottom    = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        private static VisualElement ToggleRow(string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() };
            t.style.marginBottom = 2;
            t.style.color        = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            t.RegisterValueChangedCallback(e => set(e.newValue));
            return t;
        }
    }
}
