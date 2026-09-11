// PlayerUVEditorSubPanel.Helpers.cs
// UV エディタ：Undo とヘルパー。
// Runtime/Poly_Ling_Player/View/SubPanels/UV/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public partial class PlayerUVEditorSubPanel
    {
        // ================================================================
        // Undo
        // ================================================================

        private void RecordTopologyChange(string opName, Action<MeshObject> action)
        {
            var model = GetModel?.Invoke();
            var mc    = model?.ActiveMeshContext;
            if (mc?.MeshObject == null) return;

            var undo   = GetUndoController?.Invoke();
            var before = undo?.CaptureMeshObjectSnapshot();

            action(mc.MeshObject);

            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                GetCommandQueue?.Invoke()?.Enqueue(
                    new RecordTopologyChangeCommand(undo, before, after, opName));
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>頂点が存在する（面で参照されている）マテリアルのインデックスリストを構築する</summary>
        private void BuildMatsWithVerts(MeshObject mo, MeshContext mc)
        {
            var seen = new HashSet<int>();
            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                seen.Add(face.MaterialIndex);
            }
            var sorted = new List<int>(seen);
            sorted.Sort();
            foreach (var mi in sorted)
                _matsWithVerts.Add(mi);
        }

        /// <summary>現在選択中のマテリアルに基づいてキャンバス背景を更新する</summary>
        private void RefreshCanvasBackground(MeshContext mc)
        {
            if (_canvas == null) return;
            bool hasMesh = mc?.MeshObject != null;
            Texture2D tex = null;
            Material mat = null;
            if (hasMesh && _matsWithVerts.Count > 0)
            {
                int matIdx = _matsWithVerts[_selectedMatListIndex];
                mat = mc.GetMaterial(matIdx);
                tex = mat?.mainTexture as Texture2D;
            }
            if (tex != null)
            {
                _bgTexture = tex;
                _canvas.style.backgroundImage = new StyleBackground(tex);
                _canvas.style.backgroundColor = new StyleColor(Color.clear);
            }
            else
            {
                _bgTexture = null;
                _canvas.style.backgroundImage = StyleKeyword.None;
                if (mat != null)
                {
                    Color col = mat.color; col.a = 1f;
                    _canvas.style.backgroundColor = new StyleColor(col);
                }
                else
                {
                    _canvas.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
                }
            }
        }

        private void UpdateCanvasBackground()
        {
            if (_canvas == null) return;
            if (_bgTexture == null) return;
            var rect = _canvas.contentRect;
            if (rect.width < 1f) return;

            float size = Mathf.Min(rect.width, rect.height) * _zoom;
            float cx   = rect.width  * 0.5f + _panOffset.x;
            float cy   = rect.height * 0.5f + _panOffset.y;

            // UV(0,1) = テクスチャ左上 → キャンバス座標
            float left = cx - size * 0.5f;
            float top  = cy - size * 0.5f;

            _canvas.style.backgroundSize =
                new BackgroundSize(new Length(size, LengthUnit.Pixel),
                                   new Length(size, LengthUnit.Pixel));
            _canvas.style.backgroundPositionX =
                new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(left, LengthUnit.Pixel));
            _canvas.style.backgroundPositionY =
                new BackgroundPosition(BackgroundPositionKeyword.Top,  new Length(top,  LengthUnit.Pixel));
        }

        private MeshContext GetMeshContext() =>
            GetModel?.Invoke()?.ActiveMeshContext;

        private MeshObject GetMeshObject() =>
            GetModel?.Invoke()?.ActiveMeshContext?.MeshObject;

        private void UpdateInfo(MeshObject mo)
        {
            if (_infoLabel == null) return;
            if (mo == null) { _infoLabel.text = ""; return; }
            int uvCnt = 0;
            foreach (var v in mo.Vertices) uvCnt += v.UVs.Count;
            string sel = _selected.Count > 0 ? $"  Sel:{_selected.Count}" : "";
            _infoLabel.text = $"V:{mo.VertexCount} F:{mo.FaceCount} UV:{uvCnt}{sel}";
        }

        private void SetStatus(string text) { if (_statusLabel != null) _statusLabel.text = text; }

        // ================================================================
        // UI ヘルパー
        // ================================================================

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10; l.style.marginTop = 4; l.style.marginBottom = 2;
            return l;
        }

        private static Button MkBtn(string text, VisualElement row, Action click)
        {
            var b = new Button(click) { text = text };
            b.style.flexGrow = 1; b.style.height = 22; b.style.fontSize = 10;
            row.Add(b);
            return b;
        }

        private static VisualElement FR2(string l1, string l2, float v1, float v2,
            out FloatField f1, out FloatField f2)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var lb = new Label(l1.Split(' ')[0] + ":"); lb.style.width = 46; lb.style.fontSize = 10;
            lb.style.color = new StyleColor(Color.white);
            lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(lb);
            f1 = new FloatField(l1.Split(' ').Length > 1 ? l1.Split(' ')[1] : "") { value = v1 };
            f1.style.flexGrow = 1;
            f2 = new FloatField(l2) { value = v2 };
            f2.style.flexGrow = 1;
            row.Add(f1); row.Add(f2);
            return row;
        }

        private static VisualElement FR1(string label, float val, out FloatField field)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var lb = new Label(label); lb.style.width = 60; lb.style.fontSize = 10;
            lb.style.color = new StyleColor(Color.white);
            lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            field = new FloatField { value = val };
            field.style.flexGrow = 1;
            row.Add(lb); row.Add(field);
            return row;
        }
    }
}
