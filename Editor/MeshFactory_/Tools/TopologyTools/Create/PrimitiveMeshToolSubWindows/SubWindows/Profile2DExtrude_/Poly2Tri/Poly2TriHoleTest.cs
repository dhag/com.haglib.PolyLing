using UnityEngine;
using System.Collections.Generic;
using Poly2Tri;

public class Poly2TriHoleTest : MonoBehaviour
{
    void Start()
    {
        // 外側のポリゴン（大きな四角形）
        var outerPoints = new List<PolygonPoint>
        {
            new PolygonPoint(0, 0),
            new PolygonPoint(20, 0),
            new PolygonPoint(20, 20),
            new PolygonPoint(0, 20)
        };

        // 穴（内側の小さな四角形）
        var holePoints = new List<PolygonPoint>
        {
            new PolygonPoint(5, 5),
            new PolygonPoint(15, 5),
            new PolygonPoint(15, 15),
            new PolygonPoint(5, 15)
        };

        // 外側ポリゴンを作成
        var polygon = new Polygon(outerPoints);

        // 穴を追加
        polygon.AddHole(new Polygon(holePoints));

        // 三角形分割を実行
        P2T.Triangulate(polygon);

        // 結果を表示
        Debug.Log($"=== 穴あきポリゴン テスト ===");
        Debug.Log($"外側頂点数: {outerPoints.Count}");
        Debug.Log($"穴の頂点数: {holePoints.Count}");
        Debug.Log($"生成された三角形数: {polygon.Triangles.Count}");

        int index = 0;
        foreach (var tri in polygon.Triangles)
        {
            var p0 = tri.Points[0];
            var p1 = tri.Points[1];
            var p2 = tri.Points[2];

            Debug.Log($"三角形[{index}]: ({p0.X}, {p0.Y}) - ({p1.X}, {p1.Y}) - ({p2.X}, {p2.Y})");
            index++;
        }
    }
}