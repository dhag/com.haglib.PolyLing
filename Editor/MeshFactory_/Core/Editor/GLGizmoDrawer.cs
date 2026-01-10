// Assets/Editor/MeshFactory/Core/Gizmo/GLGizmoDrawer.cs
// ランタイム用ギズモ描画実装
// GL描画を使用（エディタ/ランタイム両対応）
// Handles互換エイリアス付き

using UnityEngine;
using System.Collections.Generic;

namespace MeshFactory.Gizmo
{
    /// <summary>
    /// GL描画によるギズモ描画
    /// ランタイムでも動作（Handles不要）
    /// </summary>
    public class GLGizmoDrawer : IGizmoDrawer
    {
        // ================================================================
        // シングルトン（簡易置換用）
        //GIZMOの代わりにちょっとした描画を行いたい場合に使用するクラス
        //
        // ================================================================

        /// <summary>
        /// Handles置換用のstaticインスタンス
        /// 使用法: Handles.XXX → UnityEditor_Handles.XXX
        /// </summary>
        public static readonly GLGizmoDrawer UnityEditor_Handles = new GLGizmoDrawer();

        // ================================================================
        // マテリアル
        // ================================================================

        private Material _lineMaterial;
        
        private Material LineMaterial
        {
            get
            {
                if (_lineMaterial == null)
                {
                    // 単色描画用シェーダー
                    Shader shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader == null)
                    {
                        // フォールバック
                        shader = Shader.Find("Sprites/Default");
                    }
                    _lineMaterial = new Material(shader);
                    _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                    _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    _lineMaterial.SetInt("_ZWrite", 0);
                }
                return _lineMaterial;
            }
        }

        // ================================================================
        // 状態
        // ================================================================

        private bool _is2DMode = false;
        private Color _color = Color.white;
        private Matrix4x4 _guiMatrix;

        // ================================================================
        // IGizmoDrawer
        // ================================================================

        public bool Is2DMode => _is2DMode;

        // ================================================================
        // IGizmoDrawer2D 実装
        // ================================================================

        public void Begin()
        {
            _is2DMode = true;
            GL.PushMatrix();
            LineMaterial.SetPass(0);
            
            // GUI座標系に変換（左上原点、Y下向き）
            GL.LoadPixelMatrix();
        }

        public void End()
        {
            GL.PopMatrix();
            _is2DMode = false;
        }

        public Color Color
        {
            get => _color;
            set => _color = value;
        }

        public void DrawLine(Vector2 from, Vector2 to, float width = 1f)
        {
            LineMaterial.SetPass(0);  // ← 追加
            // GL.LINESは幅指定できないので、幅が必要な場合は四角形で描画
            if (width <= 1.5f)
            {
                GL.Begin(GL.LINES);
                GL.Color(_color);
                GL.Vertex3(from.x, from.y, 0);
                GL.Vertex3(to.x, to.y, 0);
                GL.End();
            }
            else
            {
                DrawThickLine(from, to, width);
            }
        }

        private void DrawThickLine(Vector2 from, Vector2 to, float width)
        {
            Vector2 dir = (to - from).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x) * width * 0.5f;

            GL.Begin(GL.QUADS);
            GL.Color(_color);
            GL.Vertex3(from.x - perp.x, from.y - perp.y, 0);
            GL.Vertex3(from.x + perp.x, from.y + perp.y, 0);
            GL.Vertex3(to.x + perp.x, to.y + perp.y, 0);
            GL.Vertex3(to.x - perp.x, to.y - perp.y, 0);
            GL.End();
        }

        public void DrawPolyLine(float width, params Vector3[] points)
        {
            if (points == null || points.Length < 2) return;

            for (int i = 0; i < points.Length - 1; i++)
            {
                DrawLine(
                    new Vector2(points[i].x, points[i].y),
                    new Vector2(points[i + 1].x, points[i + 1].y),
                    width);
            }
        }

        public void DrawLineSimple(Vector3 from, Vector3 to)
        {
            GL.Begin(GL.LINES);
            GL.Color(_color);
            GL.Vertex(from);
            GL.Vertex(to);
            GL.End();
        }

        public void DrawSolidDisc(Vector2 center, float radius)
        {
            DrawSolidCircle(center, radius, 32);
        }

        private void DrawSolidCircle(Vector2 center, float radius, int segments)
        {
            GL.Begin(GL.TRIANGLES);
            GL.Color(_color);

            float angleStep = Mathf.PI * 2f / segments;
            for (int i = 0; i < segments; i++)
            {
                float angle1 = i * angleStep;
                float angle2 = (i + 1) * angleStep;

                GL.Vertex3(center.x, center.y, 0);
                GL.Vertex3(center.x + Mathf.Cos(angle1) * radius, center.y + Mathf.Sin(angle1) * radius, 0);
                GL.Vertex3(center.x + Mathf.Cos(angle2) * radius, center.y + Mathf.Sin(angle2) * radius, 0);
            }

            GL.End();
        }

        public void DrawWireCircle(Vector2 center, float radius, int segments = 32)
        {
            GL.Begin(GL.LINES);
            GL.Color(_color);

            float angleStep = Mathf.PI * 2f / segments;
            for (int i = 0; i < segments; i++)
            {
                float angle1 = i * angleStep;
                float angle2 = (i + 1) * angleStep;

                GL.Vertex3(center.x + Mathf.Cos(angle1) * radius, center.y + Mathf.Sin(angle1) * radius, 0);
                GL.Vertex3(center.x + Mathf.Cos(angle2) * radius, center.y + Mathf.Sin(angle2) * radius, 0);
            }

            GL.End();
        }

        public void DrawSolidRectWithOutline(Rect rect, Color fillColor, Color outlineColor)
        {
            // 塗りつぶし
            if (fillColor.a > 0)
            {
                GL.Begin(GL.QUADS);
                GL.Color(fillColor);
                GL.Vertex3(rect.xMin, rect.yMin, 0);
                GL.Vertex3(rect.xMax, rect.yMin, 0);
                GL.Vertex3(rect.xMax, rect.yMax, 0);
                GL.Vertex3(rect.xMin, rect.yMax, 0);
                GL.End();
            }

            // 枠線
            if (outlineColor.a > 0)
            {
                GL.Begin(GL.LINES);
                GL.Color(outlineColor);
                
                GL.Vertex3(rect.xMin, rect.yMin, 0);
                GL.Vertex3(rect.xMax, rect.yMin, 0);
                
                GL.Vertex3(rect.xMax, rect.yMin, 0);
                GL.Vertex3(rect.xMax, rect.yMax, 0);
                
                GL.Vertex3(rect.xMax, rect.yMax, 0);
                GL.Vertex3(rect.xMin, rect.yMax, 0);
                
                GL.Vertex3(rect.xMin, rect.yMax, 0);
                GL.Vertex3(rect.xMin, rect.yMin, 0);
                
                GL.End();
            }
        }

        public void DrawConvexPolygon(params Vector3[] points)
        {
            if (points == null || points.Length < 3) return;

            // 三角形ファンで描画
            GL.Begin(GL.TRIANGLES);
            GL.Color(_color);

            for (int i = 1; i < points.Length - 1; i++)
            {
                GL.Vertex(points[0]);
                GL.Vertex(points[i]);
                GL.Vertex(points[i + 1]);
            }

            GL.End();
        }

        // ================================================================
        // IGizmoDrawer3D 実装
        // ================================================================

        public void DrawLine(Vector3 from, Vector3 to)
        {
            //LineMaterial.SetPass(0);
            GL.Begin(GL.LINES);
            GL.Color(_color);
            GL.Vertex(from);
            GL.Vertex(to);
            GL.End();
        }

        public void DrawLine(Vector3 from, Vector3 to, float thickness)
        {
            // 3Dでの太線は複雑なので、とりあえず通常線で描画
            DrawLine(from, to);
        }

        public void DrawWireDisc(Vector3 center, Vector3 normal, float radius)
        {
            LineMaterial.SetPass(0);

            // 法線に垂直な2つの軸を計算
            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.001f)
            {
                tangent = Vector3.Cross(normal, Vector3.right);
            }
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            GL.Begin(GL.LINES);
            GL.Color(_color);

            int segments = 32;
            float angleStep = Mathf.PI * 2f / segments;

            for (int i = 0; i < segments; i++)
            {
                float angle1 = i * angleStep;
                float angle2 = (i + 1) * angleStep;

                Vector3 p1 = center + (tangent * Mathf.Cos(angle1) + bitangent * Mathf.Sin(angle1)) * radius;
                Vector3 p2 = center + (tangent * Mathf.Cos(angle2) + bitangent * Mathf.Sin(angle2)) * radius;

                GL.Vertex(p1);
                GL.Vertex(p2);
            }

            GL.End();
        }

        public void DrawSolidDisc(Vector3 center, Vector3 normal, float radius)
        {
            LineMaterial.SetPass(0);

            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.001f)
            {
                tangent = Vector3.Cross(normal, Vector3.right);
            }
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            GL.Begin(GL.TRIANGLES);
            GL.Color(_color);

            int segments = 32;
            float angleStep = Mathf.PI * 2f / segments;

            for (int i = 0; i < segments; i++)
            {
                float angle1 = i * angleStep;
                float angle2 = (i + 1) * angleStep;

                Vector3 p1 = center + (tangent * Mathf.Cos(angle1) + bitangent * Mathf.Sin(angle1)) * radius;
                Vector3 p2 = center + (tangent * Mathf.Cos(angle2) + bitangent * Mathf.Sin(angle2)) * radius;

                GL.Vertex(center);
                GL.Vertex(p1);
                GL.Vertex(p2);
            }

            GL.End();
        }

        /// <summary>塗りつぶし矩形（EditorGUI.DrawRect互換）</summary>
        public void DrawRect(Rect rect, Color fillColor)
        {
            GL.Begin(GL.QUADS);
            GL.Color(fillColor);
            GL.Vertex3(rect.xMin, rect.yMin, 0);
            GL.Vertex3(rect.xMax, rect.yMin, 0);
            GL.Vertex3(rect.xMax, rect.yMax, 0);
            GL.Vertex3(rect.xMin, rect.yMax, 0);
            GL.End();
        }

        // ================================================================
        // Handles互換エイリアス
        // ================================================================

        public Color color
        {
            get => _color;
            set => _color = value;
        }

        public void BeginGUI() => Begin();

        public void EndGUI() => End();

        public void DrawAAPolyLine(float width, Vector3 p1, Vector3 p2)
        {
            DrawLine(new Vector2(p1.x, p1.y), new Vector2(p2.x, p2.y), width);
        }

        public void DrawAAPolyLine(float width, params Vector3[] points)
        {
            DrawPolyLine(width, points);
        }

        public void DrawSolidRectangleWithOutline(Rect rect, Color fillColor, Color outlineColor)
        {
            DrawSolidRectWithOutline(rect, fillColor, outlineColor);
        }

        public void DrawAAConvexPolygon(params Vector3[] points)
        {
            DrawConvexPolygon(points);
        }

        // ================================================================
        // クリーンアップ
        // ================================================================

        public void Dispose()
        {
            if (_lineMaterial != null)
            {
                Object.DestroyImmediate(_lineMaterial);
                _lineMaterial = null;
            }
        }
    }
}
