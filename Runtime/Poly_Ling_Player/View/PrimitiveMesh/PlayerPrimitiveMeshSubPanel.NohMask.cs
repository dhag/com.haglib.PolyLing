// PlayerPrimitiveMeshSubPanel.NohMask.cs
// 図形生成サブパネル：セザンヌ（旧称 能面。顔メッシュ）の諸元 UI・プリセット・JSON 書き出し。
// 内部の識別子（NohMask 系）は旧称のまま。
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

        // プリセット一覧（先頭の「内蔵（MediaPipe）」は含まない）
        private List<SezannePresetEntry> _sezPresets = new List<SezannePresetEntry>();
        private DropdownField            _sezPresetDd;
        // MediaPipe 用の諸元をまとめた箱。プリセット選択中は無効表示にする。
        private VisualElement            _sezMpBox;

        // ================================================================
        // セザンヌ UI
        // ================================================================

        private void BuildNohMaskUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("NohMask")));
            c.Add(NF(() => _nohP.MeshName, v => _nohP.MeshName = v));

            // ── プリセット ──
            c.Add(PlayerIoUiKit.SectionLabel(T("SezannePreset")));
            _sezPresetDd = new DropdownField(new List<string> { T("SezanneBuiltin") }, 0);
            _sezPresetDd.style.marginBottom = 2;
            _sezPresetDd.RegisterValueChangedCallback(_ => OnSezannePresetChanged());
            c.Add(_sezPresetDd);
            c.Add(PlayerIoUiKit.WideBtn(T("SezanneRefresh"), () =>
            {
                SezannePresetLibrary.ClearCache();
                RefreshSezannePresets();
            }));

            // ── MediaPipe 用の諸元 ──
            _sezMpBox = new VisualElement();
            c.Add(_sezMpBox);
            var m = _sezMpBox;

            m.Add(PlayerIoUiKit.SectionLabel(T("Landmarks")));
            var lmField = new TextField();
            if (string.IsNullOrEmpty(_nohP.LandmarksFilePath)) _nohP.LandmarksFilePath = RecentPaths.Get(NohLmKey);
            lmField.SetValueWithoutNotify(_nohP.LandmarksFilePath ?? "");
            lmField.RegisterValueChangedCallback(e => { _nohP.LandmarksFilePath = e.newValue; RecentPaths.Set(NohLmKey, e.newValue); D(); });
            m.Add(PlayerIoUiKit.PathRow(lmField, () =>
            {
                string path = PlayerIoUiKit.AskLoadPath("Open Landmarks JSON", NohLmKey, _nohP.LandmarksFilePath, "json");
                if (!string.IsNullOrEmpty(path)) lmField.value = path;
            }));

            m.Add(PlayerIoUiKit.SectionLabel(T("TrianglesJson")));
            var triField = new TextField();
            if (string.IsNullOrEmpty(_nohP.TrianglesFilePath)) _nohP.TrianglesFilePath = RecentPaths.Get(NohTriKey);
            triField.SetValueWithoutNotify(_nohP.TrianglesFilePath ?? "");
            triField.RegisterValueChangedCallback(e => { _nohP.TrianglesFilePath = e.newValue; RecentPaths.Set(NohTriKey, e.newValue); D(); });
            m.Add(PlayerIoUiKit.PathRow(triField, () =>
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
            defBtn.style.marginBottom = 4; m.Add(defBtn);

            m.Add(SR(T("Scale"), FaceMeshParams.ScaleMin, FaceMeshParams.ScaleMax, () => _nohP.Scale,      v => { _nohP.Scale      = v; D(); }));
            m.Add(SR(T("DepthScale"), FaceMeshParams.DepthScaleMin, FaceMeshParams.DepthScaleMax, () => _nohP.DepthScale, v => { _nohP.DepthScale = v; D(); }));
            m.Add(TR(T("FlipFaces"),           () => _nohP.FlipFaces,   v => { _nohP.FlipFaces  = v; D(); }));
            m.Add(TR(T("FlipX"),               () => _nohP.FlipX,       v => { _nohP.FlipX      = v; D(); }));
            m.Add(TR(T("FlipY"),               () => _nohP.FlipY,       v => { _nohP.FlipY      = v; D(); }));
            m.Add(TR(T("FlipZ"),               () => _nohP.FlipZ,       v => { _nohP.FlipZ      = v; D(); }));
            m.Add(TR(T("FillHoles"),           () => _nohP.FillHoles,   v => { _nohP.FillHoles  = v; D(); }));
            m.Add(TR(T("RimEnabled"),          () => _nohP.RimEnabled,  v => { _nohP.RimEnabled = v; D(); }));
            m.Add(SR(T("RimWidth"), FaceMeshParams.RimWidthMin, FaceMeshParams.RimWidthMax,  () => _nohP.RimWidth,    v => { _nohP.RimWidth   = v; D(); }));
            m.Add(IR(T("FaceIndex"), FaceMeshParams.FaceIndexMin, FaceMeshParams.FaceIndexMax,    () => _nohP.FaceIndex,   v => { _nohP.FaceIndex  = v; D(); }));

            // ── 選択オブジェクトをプリセットとして保存 ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("SezanneSaveSection")));
            var saveInfo = new Label(T("SezanneSaveHint"));
            saveInfo.style.fontSize = 10; saveInfo.style.whiteSpace = WhiteSpace.Normal; saveInfo.style.marginBottom = 2;
            c.Add(saveInfo);
            var saveName = new TextField(T("SezanneSaveName"));
            saveName.style.marginBottom = 2;
            c.Add(saveName);
            c.Add(PlayerIoUiKit.WideBtn(T("SezanneSaveBtn"), () => SaveSelectedAsSezannePreset(saveName.value)));

            // ── 既存メッシュを能面JSON形式で保存（セザンヌとは独立の汎用エクスポート） ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel("メッシュをJSON保存"));
            var exportInfo = new Label("選択中の描画オブジェクトを landmarks/triangles JSON で保存");
            exportInfo.style.fontSize = 10; exportInfo.style.whiteSpace = WhiteSpace.Normal; exportInfo.style.marginBottom = 2;
            c.Add(exportInfo);
            c.Add(PlayerIoUiKit.WideBtn("選択メッシュをJSON保存", ExportSelectedMeshToNohJson));

            RefreshSezannePresets();
        }

        // ================================================================
        // プリセット
        // ================================================================

        /// <summary>置き場を探し直してドロップダウンを作り直す。現在の指定が消えていれば内蔵へ戻す。</summary>
        private void RefreshSezannePresets()
        {
            _sezPresets = SezannePresetLibrary.Scan();
            if (_sezPresetDd == null) return;

            var choices = new List<string> { T("SezanneBuiltin") };
            foreach (var e in _sezPresets) choices.Add(e.Label);
            _sezPresetDd.choices = choices;

            int sel = 0;
            if (!string.IsNullOrEmpty(_nohP.PresetPath))
            {
                int found = FindSezannePreset(_nohP.PresetPath, _nohP.PresetIndex);
                if (found >= 0) sel = found + 1;
                else
                {
                    Debug.LogWarning($"[Sezanne] 指定のプリセットが置き場にありません。内蔵へ戻します: {_nohP.PresetPath} #{_nohP.PresetIndex}");
                    _nohP.PresetPath  = "";
                    _nohP.PresetIndex = 0;
                    D();
                }
            }
            _sezPresetDd.SetValueWithoutNotify(choices[sel]);
            UpdateSezanneMpBox();
        }

        private int FindSezannePreset(string path, int index)
        {
            for (int i = 0; i < _sezPresets.Count; i++)
            {
                var e = _sezPresets[i];
                if (e.Index == index && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private void OnSezannePresetChanged()
        {
            int i = _sezPresetDd.index - 1;
            if (i >= 0 && i < _sezPresets.Count)
            {
                _nohP.PresetPath  = _sezPresets[i].Path;
                _nohP.PresetIndex = _sezPresets[i].Index;
            }
            else
            {
                _nohP.PresetPath  = "";
                _nohP.PresetIndex = 0;
            }
            UpdateSezanneMpBox();
            D();
        }

        /// <summary>プリセット選択中は MediaPipe 用の諸元を無効表示にする。</summary>
        private void UpdateSezanneMpBox()
        {
            _sezMpBox?.SetEnabled(string.IsNullOrEmpty(_nohP.PresetPath));
        }

        /// <summary>選択中の描画オブジェクトを置き場へプリセットとして保存し、一覧でそれを選ぶ。</summary>
        private void SaveSelectedAsSezannePreset(string name)
        {
            var mo = GetSelectedMeshObject?.Invoke();
            if (mo == null || mo.VertexCount == 0 || mo.FaceCount == 0)
            {
                Debug.LogWarning("[Sezanne] 保存対象の描画オブジェクトがありません。");
                return;
            }

            string file = SezannePresetLibrary.Save(mo, name);
            if (string.IsNullOrEmpty(file)) return;

            // 保存したプロジェクトのオブジェクトは 1 つだけで、通し番号は 0。
            _nohP.PresetPath  = file;
            _nohP.PresetIndex = 0;
            RefreshSezannePresets();
            D();
        }

        // ================================================================
        // JSON 書き出し
        // ================================================================

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
