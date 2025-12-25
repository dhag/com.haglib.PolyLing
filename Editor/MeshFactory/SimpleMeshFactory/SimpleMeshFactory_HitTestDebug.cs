// Assets/Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_HitTestDebug.cs
// GPU ヒットテスト検証用（開発・デバッグ用）
// 本番環境ではこのファイルを削除またはコメントアウト可能

using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;
using System;
using System.Text;

public partial class SimpleMeshFactory
{
    // ================================================================
    // ヒットテスト検証（デバッグ用）
    // ================================================================

    private bool _enableHitTestValidation = false;

    /// <summary>
    /// ヒットテスト検証を有効化
    /// </summary>
    [MenuItem("Tools/SimpleMeshFactory/Debug/Toggle HitTest Validation")]
    public static void ToggleHitTestValidation()
    {
        var window = GetWindow<SimpleMeshFactory>();
        if (window != null)
        {
            window._enableHitTestValidation = !window._enableHitTestValidation;
            Debug.Log($"[HitTest] Validation: {(window._enableHitTestValidation ? "ENABLED - click to compare" : "DISABLED")}");
        }
    }

    /// <summary>
    /// クリック時に3つの計算結果を比較
    /// OnMouseDown から呼び出す
    /// 
    /// 比較対象：
    /// 1. 既存の選択（CPU）- _selectionOps.FindAtEnabledModes の結果
    /// 2. デバッグ用CPU計算 - 同じ worldToScreen で手動計算
    /// 3. GPU計算 - DispatchHitTest の結果
    /// </summary>
    private void ValidateHitTestOnClick(
        Vector2 mousePos, 
        Rect rect, 
        MeshObject meshObject, 
        Vector3 camPos, 
        Vector3 lookAt,
        HitResult existingHitResult)
    {
        if (!_enableHitTestValidation)
            return;

        if (meshObject == null || _gpuRenderer == null || !_gpuRenderer.HitTestAvailable)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"=== HitTest Validation at ({mousePos.x:F1}, {mousePos.y:F1}) ===");
        sb.AppendLine($"rect: x={rect.x:F1}, y={rect.y:F1}, w={rect.width:F1}, h={rect.height:F1}");

        // ワールド→スクリーン変換（既存のOnMouseDownと同じ）
        Func<Vector3, Vector2> worldToScreen = (worldPos) => WorldToPreviewPos(worldPos, rect, camPos, lookAt);

        // ================================================================
        // 1. 既存の選択結果（CPU）
        // ================================================================
        sb.AppendLine("\n[1. Existing Selection (CPU)]");
        sb.AppendLine($"  HitType: {existingHitResult.HitType}");
        sb.AppendLine($"  VertexIndex: {existingHitResult.VertexIndex}");
        sb.AppendLine($"  EdgePair: {existingHitResult.EdgePair}");
        sb.AppendLine($"  FaceIndex: {existingHitResult.FaceIndex}");
        sb.AppendLine($"  LineIndex: {existingHitResult.LineIndex}");

        // ================================================================
        // 2. デバッグ用CPU計算（手動で距離計算）
        // ================================================================
        sb.AppendLine("\n[2. Debug CPU Calculation]");
        
        // 頂点距離
        int cpuNearestVertex = -1;
        float cpuNearestVertexDist = float.MaxValue;
        for (int i = 0; i < meshObject.VertexCount; i++)
        {
            Vector2 screenPos = worldToScreen(meshObject.Vertices[i].Position);
            float dist = Vector2.Distance(mousePos, screenPos);
            if (dist < cpuNearestVertexDist)
            {
                cpuNearestVertexDist = dist;
                cpuNearestVertex = i;
            }
        }
        sb.AppendLine($"  Nearest Vertex: idx={cpuNearestVertex}, dist={cpuNearestVertexDist:F2}");

        // 線分距離
        int cpuNearestLine = -1;
        float cpuNearestLineDist = float.MaxValue;
        if (_edgeCache != null)
        {
            var lines = _edgeCache.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.V1 < 0 || line.V1 >= meshObject.VertexCount ||
                    line.V2 < 0 || line.V2 >= meshObject.VertexCount)
                    continue;

                Vector2 p1 = worldToScreen(meshObject.Vertices[line.V1].Position);
                Vector2 p2 = worldToScreen(meshObject.Vertices[line.V2].Position);
                float dist = DistanceToLineSegmentDebug(mousePos, p1, p2);
                if (dist < cpuNearestLineDist)
                {
                    cpuNearestLineDist = dist;
                    cpuNearestLine = i;
                }
            }
        }
        sb.AppendLine($"  Nearest Line: idx={cpuNearestLine}, dist={cpuNearestLineDist:F2}");

        // ================================================================
        // 3. GPU計算
        // ================================================================
        sb.AppendLine("\n[3. GPU Calculation]");
        
        // tabHeight を計算
        float tabHeight = GUIUtility.GUIToScreenPoint(Vector2.zero).y - position.y;
        sb.AppendLine($"  tabHeight: {tabHeight:F1}");
        
        // GPUヒットテスト実行（新しいシグネチャ：rect と tabHeight を渡す）
        _gpuRenderer.DispatchHitTest(mousePos, rect, tabHeight);
        
        // GPU結果取得
        var gpuVertexDist = _gpuRenderer.GetVertexHitDistances();
        var gpuLineDist = _gpuRenderer.GetLineHitDistances();

        int gpuNearestVertex = -1;
        float gpuNearestVertexDist = float.MaxValue;
        if (gpuVertexDist != null)
        {
            for (int i = 0; i < gpuVertexDist.Length; i++)
            {
                if (gpuVertexDist[i] < gpuNearestVertexDist)
                {
                    gpuNearestVertexDist = gpuVertexDist[i];
                    gpuNearestVertex = i;
                }
            }
        }
        sb.AppendLine($"  Nearest Vertex: idx={gpuNearestVertex}, dist={gpuNearestVertexDist:F2}");

        int gpuNearestLine = -1;
        float gpuNearestLineDist = float.MaxValue;
        if (gpuLineDist != null)
        {
            for (int i = 0; i < gpuLineDist.Length; i++)
            {
                if (gpuLineDist[i] < gpuNearestLineDist)
                {
                    gpuNearestLineDist = gpuLineDist[i];
                    gpuNearestLine = i;
                }
            }
        }
        sb.AppendLine($"  Nearest Line: idx={gpuNearestLine}, dist={gpuNearestLineDist:F2}");

        // ================================================================
        // 比較結果
        // ================================================================
        sb.AppendLine("\n[Comparison]");
        
        bool vertexMatch = cpuNearestVertex == gpuNearestVertex;
        float vertexDistError = Mathf.Abs(cpuNearestVertexDist - gpuNearestVertexDist);
        sb.AppendLine($"  Vertex: {(vertexMatch ? "MATCH" : "MISMATCH")} (CPU={cpuNearestVertex}, GPU={gpuNearestVertex}, distError={vertexDistError:F2})");

        bool lineMatch = cpuNearestLine == gpuNearestLine;
        float lineDistError = Mathf.Abs(cpuNearestLineDist - gpuNearestLineDist);
        sb.AppendLine($"  Line: {(lineMatch ? "MATCH" : "MISMATCH")} (CPU={cpuNearestLine}, GPU={gpuNearestLine}, distError={lineDistError:F2})");

        // 頂点0のスクリーン座標を比較（デバッグ用）
        if (meshObject.VertexCount > 0)
        {
            Vector2 cpuScreen0 = worldToScreen(meshObject.Vertices[0].Position);
            sb.AppendLine($"\n[Vertex 0 Debug]");
            sb.AppendLine($"  CPU screen pos: ({cpuScreen0.x:F1}, {cpuScreen0.y:F1})");
            sb.AppendLine($"  CPU dist from mouse: {Vector2.Distance(mousePos, cpuScreen0):F1}");
            if (gpuVertexDist != null && gpuVertexDist.Length > 0)
            {
                sb.AppendLine($"  GPU dist from mouse: {gpuVertexDist[0]:F1}");
            }
        }

        if (vertexMatch && lineMatch)
        {
            Debug.Log(sb.ToString());
        }
        else
        {
            Debug.LogWarning(sb.ToString());
        }
    }

    /// <summary>
    /// 点と線分の最短距離（CPU計算用）
    /// </summary>
    private float DistanceToLineSegmentDebug(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 line = lineEnd - lineStart;
        float lenSq = line.sqrMagnitude;
        
        if (lenSq < 0.000001f)
            return Vector2.Distance(point, lineStart);

        float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / lenSq);
        Vector2 projection = lineStart + t * line;
        return Vector2.Distance(point, projection);
    }

    /// <summary>
    /// ヒットテスト検証用キーボードショートカット
    /// HandleKeyboardShortcutsから呼び出す
    /// </summary>
    private bool HandleHitTestDebugShortcuts(Event e, Vector2 mousePos)
    {
        // Shift+F6: 検証の有効/無効を切り替え
        if (e.shift && e.keyCode == KeyCode.F6)
        {
            _enableHitTestValidation = !_enableHitTestValidation;
            Debug.Log($"[HitTest] Validation: {(_enableHitTestValidation ? "ENABLED - click to compare" : "DISABLED")}");
            e.Use();
            return true;
        }

        return false;
    }

    /// <summary>
    /// ヒットテスト検証のクリーンアップ（OnDisableで呼び出し）
    /// </summary>
    private void CleanupHitTestValidation()
    {
        _enableHitTestValidation = false;
    }
}
