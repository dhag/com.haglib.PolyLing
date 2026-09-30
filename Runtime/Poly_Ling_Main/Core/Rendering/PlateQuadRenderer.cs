// PlateQuadRenderer.cs
// 作業空間の下絵を 3D の四角形として描く（ロック解除中の下絵。PolyLing_UV_Billboard_Design.md 8.1）。
// Runtime/Poly_Ling_Main/Core/Rendering/ に配置
//
// 【何を描くか】
//   代理オブジェクトのローカル XY の 2 隅が張る四角形に画像を貼る。四角形はモデルに入らない
//   表示専用のメッシュなので、編集・選択・保存の対象にならない。
//   UV の作業空間では 0〜1 の枠（線）も描く。
//
// 【GridAxisRenderer と同じ分担】
//   Prepare（event 駆動）で行列・メッシュ・マテリアルを用意し、Submit は提出だけを行う。
//   シェーダーは Resources/Shaders/PolyLing_UnderlayQuad.shader（"Poly_Ling/UnderlayQuad"）。

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Poly_Ling.Core.Rendering
{
    /// <summary>作業空間の下絵の四角形の描画パラメータ。</summary>
    public struct PlateQuadParams
    {
        /// <summary>代理オブジェクトのローカル → ワールド。</summary>
        public Matrix4x4 LocalToWorld;

        /// <summary>画像の 1 隅と向かい合う隅（代理のローカル XY）。画像の上端 = 大きい方の Y。</summary>
        public Vector2 Corner0, Corner1;

        /// <summary>貼る画像。null なら画像は描かない。</summary>
        public Texture Texture;

        /// <summary>四角形の縁を線で描くか（UV の 0〜1 の枠）。</summary>
        public bool ShowFrame;

        /// <summary>縁の色。</summary>
        public Color FrameColor;
    }

    public sealed class PlateQuadRenderer : IDisposable
    {
        private Mesh     _quad;
        private Mesh     _frame;
        private Material _material;
        private MaterialPropertyBlock _imageProps;
        private MaterialPropertyBlock _frameProps;

        private bool      _active;
        private bool      _hasImage;
        private bool      _hasFrame;
        private Matrix4x4 _matrix;
        private Bounds    _bounds;

        // ================================================================
        // Prepare（event 駆動）
        // ================================================================

        /// <summary>
        /// 【event 駆動で呼ぶ】描くものを決める。p が null なら何も描かない。
        /// メッシュ・マテリアルの生成もここで済ませ、Submit 側では生成しない。
        /// </summary>
        public void Prepare(PlateQuadParams? p)
        {
            if (p == null) { _active = false; return; }
            var v = p.Value;

            float x0 = Mathf.Min(v.Corner0.x, v.Corner1.x), x1 = Mathf.Max(v.Corner0.x, v.Corner1.x);
            float y0 = Mathf.Min(v.Corner0.y, v.Corner1.y), y1 = Mathf.Max(v.Corner0.y, v.Corner1.y);
            if (x1 - x0 < 1e-8f || y1 - y0 < 1e-8f) { _active = false; return; }

            EnsureResources();
            if (_material == null) { _active = false; return; }

            _matrix = v.LocalToWorld
                    * Matrix4x4.TRS(new Vector3(x0, y0, 0f), Quaternion.identity, new Vector3(x1 - x0, y1 - y0, 1f));
            _bounds = PLRenderMeshHelper.WorldBoundsOf(_quad, _matrix);

            _hasImage = v.Texture != null;
            if (_hasImage) _imageProps.SetTexture("_MainTex", v.Texture);

            _hasFrame = v.ShowFrame;
            if (_hasFrame) _frameProps.SetColor("_Color", v.FrameColor);

            _active = _hasImage || _hasFrame;
        }

        // ================================================================
        // Submit（提出のみ）
        // ================================================================

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.RenderMesh 提出のみを行う ★★★
        /// 計算処理（メッシュ構築・マテリアル生成等）は一切禁止。準備は Prepare で完了させておく。
        /// </summary>
        public void Submit(Camera cam)
        {
            if (!_active || cam == null || _material == null) return;

            if (_hasImage)
            {
                var rp = PLRenderMeshHelper.Make(_material, cam, _bounds, _imageProps);
                rp.shadowCastingMode = ShadowCastingMode.Off;
                rp.receiveShadows    = false;
                Graphics.RenderMesh(rp, _quad, 0, _matrix);
            }
            if (_hasFrame)
            {
                var rp = PLRenderMeshHelper.Make(_material, cam, _bounds, _frameProps);
                rp.shadowCastingMode = ShadowCastingMode.Off;
                rp.receiveShadows    = false;
                Graphics.RenderMesh(rp, _frame, 0, _matrix);
            }
        }

        // ================================================================
        // 資源
        // ================================================================

        private void EnsureResources()
        {
            if (_material == null)
            {
                var shader = Shader.Find("Poly_Ling/UnderlayQuad");
                if (shader == null)
                {
                    Debug.LogWarning("[PlateQuadRenderer] シェーダー \"Poly_Ling/UnderlayQuad\" が見つかりません。ロック解除中の下絵は描画されません。");
                    return;
                }
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_imageProps == null)
            {
                _imageProps = new MaterialPropertyBlock();
                _imageProps.SetColor("_Color", Color.white);
            }
            if (_frameProps == null)
            {
                _frameProps = new MaterialPropertyBlock();
                _frameProps.SetTexture("_MainTex", Texture2D.whiteTexture);
            }
            if (_quad == null)
            {
                _quad = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "PlateQuad" };
                _quad.SetVertices(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) });
                _quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                _quad.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
                _quad.SetIndices(new[] { 0, 2, 1, 0, 3, 2 }, MeshTopology.Triangles, 0);
                _quad.RecalculateBounds();
            }
            if (_frame == null)
            {
                _frame = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "PlateFrame" };
                _frame.SetVertices(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) });
                _frame.SetUVs(0, new[] { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero });
                _frame.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
                _frame.SetIndices(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, MeshTopology.Lines, 0);
                _frame.RecalculateBounds();
            }
        }

        public void Dispose()
        {
            if (_quad  != null) { UnityEngine.Object.Destroy(_quad);  _quad  = null; }
            if (_frame != null) { UnityEngine.Object.Destroy(_frame); _frame = null; }
            if (_material != null) { UnityEngine.Object.Destroy(_material); _material = null; }
            _active = false;
        }
    }
}
