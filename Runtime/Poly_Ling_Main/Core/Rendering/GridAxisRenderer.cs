// GridAxisRenderer.cs
// 3Dプレビューの「軸」「グリッド平面」を Graphics.DrawMesh で描画する。
// MeshSceneRenderer.BuildBoneLineMesh / SubmitBones と同じ
// 「CPU Mesh (MeshTopology.Lines・頂点色) を Prepare で構築 → Submit で提出」方式。
//
// Prepare(): パラメータが変化したときだけメッシュを再構築する（event 駆動）。
// Submit():  Graphics.DrawMesh 提出のみ。計算は一切行わない。
//
// Runtime/Poly_Ling_Main/Core/Rendering/ に配置

using System;
using UnityEngine;

namespace Poly_Ling.Core.Rendering
{
    /// <summary>
    /// 軸・グリッドの描画パラメータ。
    /// Player 側の ViewportGridSettings から変換して渡す
    /// （Main が Player 型に依存しないようにするため、ここでは素の値のみ保持する）。
    /// </summary>
    public struct GridAxisParams
    {
        public bool  ShowAxis;
        public bool  ShowGrid;

        /// <summary>0 = XZ（床）、1 = XY（正面）、2 = YZ（側面）。</summary>
        public int   Plane;

        public float CellSize;
        public int   HalfCount;
        public float AxisLength;

        public bool SameAs(GridAxisParams o)
        {
            return ShowAxis   == o.ShowAxis
                && ShowGrid   == o.ShowGrid
                && Plane      == o.Plane
                && HalfCount  == o.HalfCount
                && Mathf.Approximately(CellSize,   o.CellSize)
                && Mathf.Approximately(AxisLength, o.AxisLength);
        }
    }

    /// <summary>
    /// 軸線とグリッド平面を描画する。全ビューポート共通の1インスタンスを
    /// PlayerViewportManager が保持し、カメラごとに Submit する。
    /// </summary>
    public sealed class GridAxisRenderer : IDisposable
    {
        // ================================================================
        // 色設定
        // ================================================================

        private static readonly Color GridLineColor   = new Color(0.35f, 0.35f, 0.35f, 0.55f);
        private static readonly Color GridCenterColor = new Color(0.55f, 0.55f, 0.55f, 0.85f);

        private static readonly Color AxisXColor = new Color(1.00f, 0.25f, 0.25f, 0.95f);
        private static readonly Color AxisYColor = new Color(0.35f, 1.00f, 0.35f, 0.95f);
        private static readonly Color AxisZColor = new Color(0.35f, 0.55f, 1.00f, 0.95f);

        /// <summary>負方向の軸線に掛ける減光率（正方向と区別するため）。</summary>
        private const float NegativeAxisDim = 0.45f;

        // ================================================================
        // 状態
        // ================================================================

        private Mesh     _gridMesh;
        private Mesh     _axisMesh;
        private Material _material;

        private GridAxisParams _cached;
        private bool           _hasCache;

        // ================================================================
        // Prepare（event 駆動。パラメータ変化時のみ再構築）
        // ================================================================

        /// <summary>
        /// 【event 駆動で呼ぶ】軸・グリッドのラインメッシュを構築する。
        /// パラメータが前回と同一なら何もしない（毎フレーム呼んでも安全だが、
        /// 規約どおり表示設定変更・初期化の契機から呼ぶこと）。
        /// マテリアル生成もここで済ませ、Submit 側では生成しない。
        /// </summary>
        public void Prepare(GridAxisParams p)
        {
            EnsureMaterial();

            if (_hasCache && _cached.SameAs(p)) return;
            _cached   = p;
            _hasCache = true;

            RebuildGridMesh(p);
            RebuildAxisMesh(p);
        }

        // ================================================================
        // Submit（Graphics.DrawMesh 提出のみ）
        // ================================================================

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 計算処理（メッシュ構築・マテリアル生成等）は一切禁止。
        /// 全ての準備は Prepare で完了させておくこと。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void Submit(Camera cam)
        {
            if (cam == null || _material == null || !_hasCache) return;

            if (_cached.ShowGrid && _gridMesh != null)
                Graphics.DrawMesh(_gridMesh, Matrix4x4.identity, _material, 0, cam);

            if (_cached.ShowAxis && _axisMesh != null)
                Graphics.DrawMesh(_axisMesh, Matrix4x4.identity, _material, 0, cam);
        }

        // ================================================================
        // メッシュ構築
        // ================================================================

        /// <summary>
        /// 平面種別から、グリッドを張る2軸の単位ベクトルを返す。
        /// Unity 左手系。0=XZ（床）、1=XY（正面）、2=YZ（側面）。
        /// </summary>
        private static void GetPlaneAxes(int plane, out Vector3 u, out Vector3 v)
        {
            switch (plane)
            {
                case 1:  u = Vector3.right;   v = Vector3.up;      break;  // XY
                case 2:  u = Vector3.forward; v = Vector3.up;      break;  // YZ
                default: u = Vector3.right;   v = Vector3.forward; break;  // XZ
            }
        }

        private void RebuildGridMesh(GridAxisParams p)
        {
            if (!p.ShowGrid)
            {
                DestroyMesh(ref _gridMesh);
                return;
            }

            GetPlaneAxes(p.Plane, out Vector3 u, out Vector3 v);

            int   n     = Mathf.Max(1, p.HalfCount);
            float cell  = p.CellSize;
            float ext   = n * cell;
            int   lines = (n * 2 + 1) * 2;          // u方向・v方向それぞれ (2n+1) 本

            var verts   = new Vector3[lines * 2];
            var colors  = new Color[lines * 2];
            var indices = new int[lines * 2];

            int w = 0;

            // v 軸に平行な線（u = i*cell の位置に並ぶ）
            for (int i = -n; i <= n; i++)
            {
                Color c = (i == 0) ? GridCenterColor : GridLineColor;
                Vector3 o = u * (i * cell);
                verts[w] = o - v * ext; colors[w] = c; indices[w] = w; w++;
                verts[w] = o + v * ext; colors[w] = c; indices[w] = w; w++;
            }

            // u 軸に平行な線（v = i*cell の位置に並ぶ）
            for (int i = -n; i <= n; i++)
            {
                Color c = (i == 0) ? GridCenterColor : GridLineColor;
                Vector3 o = v * (i * cell);
                verts[w] = o - u * ext; colors[w] = c; indices[w] = w; w++;
                verts[w] = o + u * ext; colors[w] = c; indices[w] = w; w++;
            }

            AssignLineMesh(ref _gridMesh, verts, colors, indices);
        }

        private void RebuildAxisMesh(GridAxisParams p)
        {
            if (!p.ShowAxis)
            {
                DestroyMesh(ref _axisMesh);
                return;
            }

            float len = p.AxisLength;

            // 正方向3本 + 負方向3本 = 6 セグメント
            var dirs = new Vector3[] { Vector3.right, Vector3.up, Vector3.forward };
            var cols = new Color[]   { AxisXColor,    AxisYColor, AxisZColor      };

            var verts   = new Vector3[12];
            var colors  = new Color[12];
            var indices = new int[12];

            int w = 0;
            for (int a = 0; a < 3; a++)
            {
                Color cPos = cols[a];
                Color cNeg = new Color(cPos.r * NegativeAxisDim,
                                       cPos.g * NegativeAxisDim,
                                       cPos.b * NegativeAxisDim,
                                       cPos.a);

                // 正方向
                verts[w] = Vector3.zero;      colors[w] = cPos; indices[w] = w; w++;
                verts[w] = dirs[a] * len;     colors[w] = cPos; indices[w] = w; w++;

                // 負方向
                verts[w] = Vector3.zero;      colors[w] = cNeg; indices[w] = w; w++;
                verts[w] = -dirs[a] * len;    colors[w] = cNeg; indices[w] = w; w++;
            }

            AssignLineMesh(ref _axisMesh, verts, colors, indices);
        }

        private static void AssignLineMesh(ref Mesh mesh, Vector3[] verts, Color[] colors, int[] indices)
        {
            if (mesh == null)
            {
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            else
            {
                mesh.Clear();
            }

            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
        }

        // ================================================================
        // マテリアル
        // ================================================================

        private void EnsureMaterial()
        {
            if (_material != null) return;

            var shader = Shader.Find("Poly_Ling/GridAxis");
            if (shader == null)
            {
                Debug.LogWarning("[GridAxisRenderer] シェーダー \"Poly_Ling/GridAxis\" が見つかりません。軸/グリッドは描画されません。");
                return;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetFloat("_GlobalAlpha", 1.0f);
        }

        // ================================================================
        // 破棄
        // ================================================================

        private static void DestroyMesh(ref Mesh mesh)
        {
            if (mesh == null) return;
            UnityEngine.Object.Destroy(mesh);
            mesh = null;
        }

        public void Dispose()
        {
            DestroyMesh(ref _gridMesh);
            DestroyMesh(ref _axisMesh);

            if (_material != null)
            {
                UnityEngine.Object.Destroy(_material);
                _material = null;
            }

            _hasCache = false;
        }
    }
}
