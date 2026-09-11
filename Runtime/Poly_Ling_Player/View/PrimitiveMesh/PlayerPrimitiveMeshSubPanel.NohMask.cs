// PlayerPrimitiveMeshSubPanel.NohMask.cs
// 図形生成サブパネル：能面（顔メッシュ）の諸元 UI と JSON 書き出し。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.NohMask;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Core;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private FaceMeshParams                       _nohP    = FaceMeshParams.Default;
        private const string  NohLmKey   = "Primitive.NohMask.Landmarks";
        private const string  NohTriKey  = "Primitive.NohMask.Triangles";
        private const string  NohSaveKey = "Primitive.NohMask.SaveJson";

        // ================================================================
        // NohMask UI（変更なし）
        // ================================================================

        private void BuildNohMaskUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("NohMask")));
            c.Add(NF(() => _nohP.MeshName, v => _nohP.MeshName = v));

            c.Add(PlayerIoUiKit.SectionLabel(T("Landmarks")));
            var lmField = new TextField();
            if (string.IsNullOrEmpty(_nohP.LandmarksFilePath)) _nohP.LandmarksFilePath = RecentPaths.Get(NohLmKey);
            lmField.SetValueWithoutNotify(_nohP.LandmarksFilePath ?? "");
            lmField.RegisterValueChangedCallback(e => { _nohP.LandmarksFilePath = e.newValue; RecentPaths.Set(NohLmKey, e.newValue); D(); });
            c.Add(PlayerIoUiKit.PathRow(lmField, () =>
            {
                string path = PlayerIoUiKit.AskLoadPath("Open Landmarks JSON", NohLmKey, _nohP.LandmarksFilePath, "json");
                if (!string.IsNullOrEmpty(path)) lmField.value = path;
            }));

            c.Add(PlayerIoUiKit.SectionLabel(T("TrianglesJson")));
            var triField = new TextField();
            if (string.IsNullOrEmpty(_nohP.TrianglesFilePath)) _nohP.TrianglesFilePath = RecentPaths.Get(NohTriKey);
            triField.SetValueWithoutNotify(_nohP.TrianglesFilePath ?? "");
            triField.RegisterValueChangedCallback(e => { _nohP.TrianglesFilePath = e.newValue; RecentPaths.Set(NohTriKey, e.newValue); D(); });
            c.Add(PlayerIoUiKit.PathRow(triField, () =>
            {
                string path = PlayerIoUiKit.AskLoadPath("Open Triangles JSON", NohTriKey, _nohP.TrianglesFilePath, "json");
                if (!string.IsNullOrEmpty(path)) triField.value = path;
            }));

            // 内蔵デフォルト（プリセット）に戻す: パスを空にすると焼き込み済みデータを使用
            var defBtn = new Button(() =>
            {
                lmField.value  = "";
                triField.value = "";
            }) { text = T("UseBuiltinDefault") };
            defBtn.style.marginBottom = 4; c.Add(defBtn);

            c.Add(SR(T("Scale"), FaceMeshParams.ScaleMin, FaceMeshParams.ScaleMax, () => _nohP.Scale,      v => { _nohP.Scale      = v; D(); }));
            c.Add(SR(T("DepthScale"), FaceMeshParams.DepthScaleMin, FaceMeshParams.DepthScaleMax, () => _nohP.DepthScale, v => { _nohP.DepthScale = v; D(); }));
            c.Add(TR(T("FlipFaces"),           () => _nohP.FlipFaces,   v => { _nohP.FlipFaces  = v; D(); }));
            c.Add(TR(T("FlipX"),               () => _nohP.FlipX,       v => { _nohP.FlipX      = v; D(); }));
            c.Add(TR(T("FlipY"),               () => _nohP.FlipY,       v => { _nohP.FlipY      = v; D(); }));
            c.Add(TR(T("FlipZ"),               () => _nohP.FlipZ,       v => { _nohP.FlipZ      = v; D(); }));
            c.Add(TR(T("FillHoles"),           () => _nohP.FillHoles,   v => { _nohP.FillHoles  = v; D(); }));
            c.Add(TR(T("RimEnabled"),          () => _nohP.RimEnabled,  v => { _nohP.RimEnabled = v; D(); }));
            c.Add(SR(T("RimWidth"), FaceMeshParams.RimWidthMin, FaceMeshParams.RimWidthMax,  () => _nohP.RimWidth,    v => { _nohP.RimWidth   = v; D(); }));
            c.Add(IR(T("FaceIndex"), FaceMeshParams.FaceIndexMin, FaceMeshParams.FaceIndexMax,    () => _nohP.FaceIndex,   v => { _nohP.FaceIndex  = v; D(); }));

            // ── 既存メッシュを能面JSON形式で保存（能面とは独立の汎用エクスポート） ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel("メッシュをJSON保存"));
            var exportInfo = new Label("選択中の描画オブジェクトを landmarks/triangles JSON で保存");
            exportInfo.style.fontSize = 10; exportInfo.style.whiteSpace = WhiteSpace.Normal; exportInfo.style.marginBottom = 2;
            c.Add(exportInfo);
            c.Add(PlayerIoUiKit.WideBtn("選択メッシュをJSON保存", ExportSelectedMeshToNohJson));
        }

        /// <summary>選択中の描画オブジェクトのメッシュを能面JSON形式(landmarks+triangles)で保存する。生座標。</summary>
        private void ExportSelectedMeshToNohJson()
        {
            var mo = GetSelectedMeshObject?.Invoke();
            if (mo == null || mo.VertexCount == 0)
            {
                Debug.LogWarning("[PolyLing] 保存対象の描画オブジェクトがありません。");
                return;
            }

            // 書き込み先はフォルダだけを覚え、ファイル名は毎回この既定から始める。
            string basePath = SaveDest.AskSavePath(
                "メッシュをJSON保存", SaveDest.Keys.MeshJson, "", "facemesh.json", "json");
            if (string.IsNullOrEmpty(basePath)) return;

            string dir  = System.IO.Path.GetDirectoryName(basePath);
            string stem = System.IO.Path.GetFileNameWithoutExtension(basePath);
            string lmPath  = System.IO.Path.Combine(dir, stem + "_landmarks.json");
            string triPath = System.IO.Path.Combine(dir, stem + "_triangles.json");

            try
            {
                System.IO.File.WriteAllText(lmPath,  NohMaskMeshExporter.BuildLandmarksJson(mo));
                System.IO.File.WriteAllText(triPath, NohMaskMeshExporter.BuildTrianglesJson(mo));
                Debug.Log($"[PolyLing] メッシュをJSON保存: {lmPath} / {triPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PolyLing] JSON保存に失敗: {ex.Message}");
            }
        }
    }
}
