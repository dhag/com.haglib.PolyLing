// Assets/Editor/Poly_Ling/Core/Gizmo/HandlesGizmoDrawer.cs
// エディタ用ギズモ描画実装
// UnityEditor.Handlesをラップ + 互換エイリアス付き

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
/*
namespace Poly_Ling.Gizmo
{
    /// <summary>
    /// すでに役割を終えたクラス
    /// すでに役割を終えたクラス
    /// すでに役割を終えたクラス
    /// すでに役割を終えたクラス
    /// すでに役割を終えたクラス
    /// エディタ用ギズモ描画    /// ギズモは使わない方針だが現状移行用に残したもの
    /// Handlesをラップして統一インターフェースを提供
    /// Handles互換エイリアス付きで段階的移行が可能
    /// </summary>
    public class HandlesGizmoDrawer : IGizmoDrawer
    {
        // ================================================================
        // シングルトン（簡易置換用）
        // ================================================================

        /// <summary>
        /// Handles置換用のstaticインスタンス
        /// 使用法: UnityEditor_Handles.XXX → UnityEditor_UnityEditor_Handles.XXX
        /// </summary>
        public static readonly HandlesGizmoDrawer UnityEditor_Handles_OLD = new HandlesGizmoDrawer();

        // ================================================================
        // 状態
        // ================================================================

        private bool _is2DMode = false;
        private Color _color = Color.white;

        // ================================================================
        // IGizmoDrawer
        // ================================================================

        public bool Is2DMode => _is2DMode;

        // ================================================================
        // IGizmoDrawer2D 実装
        // ================================================================

        public void Begin()
        {
            UnityEditor.Handles.BeginGUI();
            _is2DMode = true;
        }

        public void End()
        {
            UnityEditor.Handles.EndGUI();
            _is2DMode = false;
        }

        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                UnityEditor.Handles.color = value;
            }
        }

        public void DrawLine(Vector2 from, Vector2 to, float width = 1f)
        {
            UnityEditor.Handles.DrawAAPolyLine(width,
                new Vector3(from.x, from.y, 0),
                new Vector3(to.x, to.y, 0));
        }

        public void DrawPolyLine(float width, params Vector3[] points)
        {
            if (points == null || points.Length < 2) return;
            UnityEditor.Handles.DrawAAPolyLine(width, points);
        }

        public void DrawLineSimple(Vector3 from, Vector3 to)
        {
            UnityEditor.Handles.DrawLine(from, to);
        }

        public void DrawSolidDisc(Vector2 center, float radius)
        {
            UnityEditor.Handles.DrawSolidDisc(
                new Vector3(center.x, center.y, 0),
                Vector3.forward,
                radius);
        }

        public void DrawWireCircle(Vector2 center, float radius, int segments = 32)
        {
            if (segments < 3) segments = 3;

            Vector2 prevPoint = center + new Vector2(radius, 0);

            for (int i = 1; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                UnityEditor_Handles_OLD.DrawAAPolyLine(2f,
                    new Vector3(prevPoint.x, prevPoint.y, 0),
                    new Vector3(point.x, point.y, 0));
                prevPoint = point;
            }
        }

        public void DrawSolidRectWithOutline(Rect rect, Color fillColor, Color outlineColor)
        {
            UnityEditor.Handles.DrawSolidRectangleWithOutline(rect, fillColor, outlineColor);
        }

        public void DrawConvexPolygon(params Vector3[] points)
        {
            if (points == null || points.Length < 3) return;
            UnityEditor.Handles.DrawAAConvexPolygon(points);
        }

        // ================================================================
        // IGizmoDrawer3D 実装
        // ================================================================

        public void DrawLine(Vector3 from, Vector3 to)
        {
            UnityEditor.Handles.DrawLine(from, to);
        }

        public void DrawLine(Vector3 from, Vector3 to, float thickness)
        {
            UnityEditor.Handles.DrawLine(from, to, thickness);
        }

        public void DrawWireDisc(Vector3 center, Vector3 normal, float radius)
        {
            UnityEditor.Handles.DrawWireDisc(center, normal, radius);
        }

        public void DrawSolidDisc(Vector3 center, Vector3 normal, float radius)
        {
            UnityEditor.Handles.DrawSolidDisc(center, normal, radius);
        }

        // ================================================================
        // Handles互換エイリアス
        // 置換: UnityEditor_Handles.XXX → UnityEditor_UnityEditor_Handles.XXX でそのまま動く
        // ================================================================

        /// <summary>UnityEditor_Handles.color 互換</summary>
        public Color color
        {
            get => _color;
            set
            {
                _color = value;
                UnityEditor.Handles.color = value;
            }
        }

        /// <summary>UnityEditor_Handles.BeginGUI() 互換</summary>
        public void BeginGUI() => Begin();

        /// <summary>UnityEditor_Handles.EndGUI() 互換</summary>
        public void EndGUI() => End();

        /// <summary>UnityEditor_Handles.DrawAAPolyLine 互換（2点）</summary>
        public void DrawAAPolyLine(float width, Vector3 p1, Vector3 p2)
        {
            UnityEditor.Handles.DrawAAPolyLine(width, p1, p2);
        }

        /// <summary>UnityEditor_Handles.DrawAAPolyLine 互換（複数点）</summary>
        public void DrawAAPolyLine(float width, params Vector3[] points)
        {
            if (points == null || points.Length < 2) return;
            UnityEditor.Handles.DrawAAPolyLine(width, points);
        }
        / *
        /// <summary>UnityEditor_Handles.DrawLine 互換（2点）</summary>
        public void DrawLine(Vector3 p1, Vector3 p2, float thickness)
        {
            UnityEditor.Handles.DrawLine(p1, p2, thickness);
        }

        /// <summary>UnityEditor_Handles.DrawSolidDisc 互換（3D）</summary>
        public void DrawSolidDisc(Vector3 center, Vector3 normal, float radius)
        {
            UnityEditor.Handles.DrawSolidDisc(center, normal, radius);
        }
        * /

        /// <summary>UnityEditor_Handles.DrawWireDisc 互換</summary>
        // 既にIGizmoDrawer3Dで実装済み

        /// <summary>UnityEditor_Handles.DrawSolidRectangleWithOutline 互換</summary>
        public void DrawSolidRectangleWithOutline(Rect rect, Color fillColor, Color outlineColor)
        {
            UnityEditor.Handles.DrawSolidRectangleWithOutline(rect, fillColor, outlineColor);
        }

        /// <summary>UnityEditor_Handles.DrawAAConvexPolygon 互換</summary>
        public void DrawAAConvexPolygon(params Vector3[] points)
        {
            if (points == null || points.Length < 3) return;
            UnityEditor.Handles.DrawAAConvexPolygon(points);
        }
    }
}
*/
#endif