// PolyLingPlayerViewerCore.ViewUvProjection.cs
// Player ビューアのコア：ビューからの UV 投影（applyUvUnwrap の Projection = View）で、
// 頂点ごとの UV を求める。書き込み・Undo は PlayerCommandDispatcher → PolyLingCoreUvHandlers。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【求め方】
//   各頂点の表示位置（GPU が計算したワールド座標。TryGetMeshWorldPositions）をそのビューの
//   カメラで画面へ写し、パネル座標（Y=0 が上）の点 q にする。透視・平行投影はカメラ行列の違いだけ。
//   基準の四辺形（左上の角 corner、右向きの辺 axisU、下向きの辺 axisV）で
//     q = corner + a·axisU + b·axisV
//   を解き、UV = (a, 1 − b) とする（画像の上が V = 1）。その後に Scale・Offset を掛ける
//   （他の投影と同じ u·Scale + OffsetU）。
//
// 【基準の四辺形】
//   Underlay … そのビューのパネルが今表示している下絵画像（PlayerViewportPanel.TryGetUnderlayQuad）。
//              描いた値そのものを読むので、方向スロット・Persp/Ortho・作業空間の下絵のどれでも同じ。
//   Viewport … ビュー全体。
//   Bounds   … 投影した全対象頂点の範囲（縦横それぞれ 0〜1）。
//
// 【拒否】カメラの後ろの頂点がある、表示位置が取れない（非表示・頂点なし）、
//   Underlay なのに下絵が表示されていない、対象で作業空間が開いている（代理との対応が崩れる）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        /// <summary>OnComputeViewUvs の実体。失敗理由を返す（成功なら null）。</summary>
        private string ComputeViewUvs(ApplyUvUnwrapCommand c, ModelContext model, Dictionary<int, Vector2[]> result)
        {
            if (c == null) return "コマンドが null";
            if (model == null) return "モデルがありません";
            if (_viewportManager == null) return "ビューがありません";
            if (c.MasterIndices == null || c.MasterIndices.Length == 0) return "対象の描画オブジェクトがありません";

            var vp    = ViewportOf(c.View);
            var panel = PanelOf(c.View);
            var cam   = vp?.Cam;
            if (cam == null || panel == null || cam.pixelWidth <= 1 || cam.pixelHeight <= 1)
                return $"ビュー {c.View} が表示されていません";
            float w = cam.pixelWidth, h = cam.pixelHeight;

            // 対象（重複は 1 つ）。作業空間の元・代理は拒否する。
            var targets = new List<int>();
            foreach (int idx in c.MasterIndices)
            {
                if (targets.Contains(idx)) continue;
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) return $"描画オブジェクト {idx} がありません";
                if (_editSpace != null && (mc == _editSpace.Source || mc == _editSpace.Proxy))
                    return $"作業空間を開いている描画オブジェクト（{mc.Name}）には投影できません。作業空間を閉じてから実行してください";
                targets.Add(idx);
            }

            // 表示位置の鮮度をそろえる（TryGetMeshWorldPositions の注記。1 回だけ）。
            _viewportManager.UpdateTransform();

            var panelPts = new Dictionary<int, Vector2[]>();
            foreach (int idx in targets)
            {
                var mc = model.GetMeshContext(idx);
                if (!_viewportManager.TryGetMeshWorldPositions(model, mc, out var world) || world == null)
                    return $"{mc.Name} の表示位置を取れません（非表示・頂点が無いものは投影できません）";

                var pts = new Vector2[world.Length];
                int behind = 0;
                for (int i = 0; i < world.Length; i++)
                {
                    Vector2 s = PlayerViewportManager.ProjectWorldToCameraScreen(cam, world[i]);
                    if (float.IsNaN(s.x)) { behind++; continue; }
                    pts[i] = new Vector2(s.x, h - s.y);
                }
                if (behind > 0) return $"{mc.Name} の {behind} 頂点がビュー {c.View} のカメラの後ろにあります";
                panelPts[idx] = pts;
            }

            Vector2 corner, axisU, axisV;
            switch (c.ViewFrame)
            {
                case ViewProjectionFrame.Underlay:
                    if (!panel.TryGetUnderlayQuad(out corner, out axisU, out axisV))
                        return $"ビュー {c.View} に下絵が表示されていません";
                    break;

                case ViewProjectionFrame.Viewport:
                    corner = Vector2.zero;
                    axisU  = new Vector2(w, 0f);
                    axisV  = new Vector2(0f, h);
                    break;

                default: // Bounds
                {
                    var min = new Vector2(float.MaxValue, float.MaxValue);
                    var max = new Vector2(float.MinValue, float.MinValue);
                    foreach (var pts in panelPts.Values)
                        foreach (var q in pts) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
                    corner = min;
                    axisU  = new Vector2(max.x - min.x, 0f);
                    axisV  = new Vector2(0f, max.y - min.y);
                    break;
                }
            }

            float det = axisU.x * axisV.y - axisU.y * axisV.x;
            if (Mathf.Abs(det) < 1e-6f) return $"基準（{c.ViewFrame}）の幅または高さが 0 です";

            foreach (var kv in panelPts)
            {
                var pts = kv.Value;
                var uvs = new Vector2[pts.Length];
                for (int i = 0; i < pts.Length; i++)
                {
                    Vector2 d = pts[i] - corner;
                    float a = (d.x * axisV.y - d.y * axisV.x) / det;
                    float b = (axisU.x * d.y - axisU.y * d.x) / det;
                    uvs[i] = new Vector2(a * c.Scale + c.OffsetU, (1f - b) * c.Scale + c.OffsetV);
                }
                result[kv.Key] = uvs;
            }
            return null;
        }
    }
}
