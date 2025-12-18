// Assets/Editor/MeshCreators/Revolution/RevolutionMeshGenerator.cs
// 回転体メッシュ生成ロジック
//
// 【頂点順序の規約】
// グリッド配置: i0=左下, i1=右下, i2=右上, i3=左上
// 表面: md.AddQuad(i0, i1, i2, i3)

using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;

namespace MeshFactory.Revolution
{
    /// <summary>
    /// 回転体メッシュ生成ユーティリティ
    /// </summary>
    public static class RevolutionMeshGenerator
    {
        /// <summary>
        /// 回転体メッシュを生成
        /// </summary>
        public static MeshData Generate(List<Vector2> profile, RevolutionParams p)
        {
            if (profile == null || profile.Count < 2)
                return new MeshData(p.MeshName);

            MeshData md;
            if (p.Spiral)
            {
                md = GenerateSpiral(profile, p);
            }
            else
            {
                md = GenerateRevolution(profile, p.RadialSegments, p.CloseTop, p.CloseBottom, p.CloseLoop, p.Pivot);
            }

            md.Name = p.MeshName;

            // 変換適用
            TransformMeshData(md, p.FlipY, p.FlipZ);

            return md;
        }

        /// <summary>
        /// 通常の回転体メッシュを生成
        /// </summary>
        public static MeshData GenerateRevolution(List<Vector2> profile, int radialSeg, bool closeTop, bool closeBottom, bool closeLoop, Vector3 pivot)
        {
            var md = new MeshData("Revolution");

            float minY = float.MaxValue;
            float maxY = float.MinValue;
            foreach (var p in profile)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            float height = maxY - minY;
            if (height < 0.001f) height = 1f;

            Vector3 pivotOffset = new Vector3(0, pivot.y * height + (minY + maxY) * 0.5f, 0);

            float angleStep = 2f * Mathf.PI / radialSeg;
            int profileCount = profile.Count;
            int cols = radialSeg + 1;

            // 側面の頂点
            for (int p = 0; p < profileCount; p++)
            {
                float radius = profile[p].x;
                float y = profile[p].y;
                float v = (y - minY) / height;

                Vector2 tangent;
                if (closeLoop)
                {
                    int prevIdx = (p - 1 + profileCount) % profileCount;
                    int nextIdx = (p + 1) % profileCount;
                    tangent = profile[nextIdx] - profile[prevIdx];
                }
                else
                {
                    if (p == 0)
                        tangent = profile[1] - profile[0];
                    else if (p == profileCount - 1)
                        tangent = profile[p] - profile[p - 1];
                    else
                        tangent = profile[p + 1] - profile[p - 1];
                }
                tangent.Normalize();

                Vector2 normal2D = new Vector2(tangent.y, -tangent.x);

                for (int r = 0; r <= radialSeg; r++)
                {
                    float angle = r * angleStep;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    Vector3 pos = new Vector3(cos * radius, y, sin * radius) - pivotOffset;
                    Vector3 normal = new Vector3(cos * normal2D.x, normal2D.y, sin * normal2D.x).normalized;
                    Vector2 uv = new Vector2((float)r / radialSeg, v);

                    md.Vertices.Add(new Vertex(pos, uv, normal));
                }
            }

            // 側面の四角形
            int loopCount = closeLoop ? profileCount : profileCount - 1;
            for (int p = 0; p < loopCount; p++)
            {
                int nextP = (p + 1) % profileCount;
                for (int r = 0; r < radialSeg; r++)
                {
                    int i0 = p * cols + r;
                    int i1 = i0 + 1;
                    int i2 = nextP * cols + r + 1;
                    int i3 = nextP * cols + r;

                    md.AddQuad(i0, i3, i2, i1);
                }
            }

            // 上キャップ
            if (closeTop && !closeLoop && profile[profileCount - 1].x > 0.001f)
            {
                int centerIdx = md.VertexCount;
                Vector3 topCenter = new Vector3(0, profile[profileCount - 1].y, 0) - pivotOffset;
                md.Vertices.Add(new Vertex(topCenter, new Vector2(0.5f, 0.5f), Vector3.up));

                int topRowStart = (profileCount - 1) * cols;
                for (int r = 0; r < radialSeg; r++)
                {
                    md.AddTriangle(centerIdx, topRowStart + r + 1, topRowStart + r);
                }
            }

            // 下キャップ
            if (closeBottom && !closeLoop && profile[0].x > 0.001f)
            {
                int centerIdx = md.VertexCount;
                Vector3 bottomCenter = new Vector3(0, profile[0].y, 0) - pivotOffset;
                md.Vertices.Add(new Vertex(bottomCenter, new Vector2(0.5f, 0.5f), Vector3.down));

                for (int r = 0; r < radialSeg; r++)
                {
                    md.AddTriangle(centerIdx, r, r + 1);
                }
            }

            return md;
        }

        /// <summary>
        /// らせんメッシュを生成
        /// </summary>
        public static MeshData GenerateSpiral(List<Vector2> profile, RevolutionParams param)
        {
            var md = new MeshData("Spiral");

            int totalRadialSteps = param.RadialSegments * param.SpiralTurns;
            int profileCount = profile.Count;

            float minY = float.MaxValue;
            float maxY = float.MinValue;
            foreach (var p in profile)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            float height = maxY - minY;
            if (height < 0.001f) height = 1f;

            Vector3 pivotOffset = new Vector3(0, param.Pivot.y * height + (minY + maxY) * 0.5f, 0);

            float angleStep = 2f * Mathf.PI / param.RadialSegments;

            // 頂点生成
            for (int r = 0; r <= totalRadialSteps; r++)
            {
                float angle = r * angleStep;
                float yOffset = r * param.SpiralPitch / param.RadialSegments;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                for (int p = 0; p < profileCount; p++)
                {
                    float radius = profile[p].x;
                    float y = profile[p].y + yOffset;
                    float v = (float)p / (profileCount - 1);

                    Vector2 tangent;
                    if (param.CloseLoop)
                    {
                        int prevIdx = (p - 1 + profileCount) % profileCount;
                        int nextIdx = (p + 1) % profileCount;
                        tangent = profile[nextIdx] - profile[prevIdx];
                    }
                    else
                    {
                        if (p == 0)
                            tangent = profile[1] - profile[0];
                        else if (p == profileCount - 1)
                            tangent = profile[p] - profile[p - 1];
                        else
                            tangent = profile[p + 1] - profile[p - 1];
                    }
                    tangent.Normalize();

                    Vector2 normal2D = new Vector2(tangent.y, -tangent.x);

                    Vector3 pos = new Vector3(cos * radius, y, sin * radius) - pivotOffset;
                    Vector3 normal = new Vector3(cos * normal2D.x, normal2D.y, sin * normal2D.x).normalized;
                    Vector2 uv = new Vector2((float)r / totalRadialSteps, v);

                    md.Vertices.Add(new Vertex(pos, uv, normal));
                }
            }

            // 側面の四角形
            int loopCount = param.CloseLoop ? profileCount : profileCount - 1;
            for (int r = 0; r < totalRadialSteps; r++)
            {
                for (int p = 0; p < loopCount; p++)
                {
                    int nextP = (p + 1) % profileCount;
                    int i0 = r * profileCount + p;
                    int i1 = r * profileCount + nextP;
                    int i2 = (r + 1) * profileCount + nextP;
                    int i3 = (r + 1) * profileCount + p;

                    md.AddQuad(i0, i1, i2, i3);
                }
            }

            // 上キャップ（らせん終端の断面を閉じる）
            if (param.CloseTop && profile[profileCount - 1].x > 0.001f)
            {
                int centerIdx = md.VertexCount;

                float profileCenterX = 0f;
                foreach (var p in profile) profileCenterX += p.x;
                profileCenterX /= profileCount;
                float profileCenterY = (minY + maxY) * 0.5f;

                float endAngle = totalRadialSteps * angleStep;
                float topYOffset = totalRadialSteps * param.SpiralPitch / param.RadialSegments;

                Vector3 topCenter = new Vector3(
                    Mathf.Cos(endAngle) * profileCenterX,
                    profileCenterY + topYOffset,
                    Mathf.Sin(endAngle) * profileCenterX
                ) - pivotOffset;

                Vector3 topNormal = new Vector3(Mathf.Cos(endAngle), 0, Mathf.Sin(endAngle));
                md.Vertices.Add(new Vertex(topCenter, new Vector2(0.5f, 0.5f), topNormal));

                for (int p = 0; p < profileCount - 1; p++)
                {
                    md.AddTriangle(centerIdx, totalRadialSteps * profileCount + p, totalRadialSteps * profileCount + p + 1);
                }
                if (param.CloseLoop)
                {
                    md.AddTriangle(centerIdx, totalRadialSteps * profileCount + profileCount - 1, totalRadialSteps * profileCount);
                }
            }

            // 下キャップ（らせん始端の断面を閉じる）
            if (param.CloseBottom && profile[0].x > 0.001f)
            {
                int centerIdx = md.VertexCount;

                float profileCenterX = 0f;
                foreach (var p in profile) profileCenterX += p.x;
                profileCenterX /= profileCount;
                float profileCenterY = (minY + maxY) * 0.5f;

                float startAngle = 0f;

                Vector3 bottomCenter = new Vector3(
                    Mathf.Cos(startAngle) * profileCenterX,
                    profileCenterY,
                    Mathf.Sin(startAngle) * profileCenterX
                ) - pivotOffset;

                Vector3 bottomNormal = new Vector3(-Mathf.Cos(startAngle), 0, -Mathf.Sin(startAngle));
                md.Vertices.Add(new Vertex(bottomCenter, new Vector2(0.5f, 0.5f), bottomNormal));

                for (int p = 0; p < profileCount - 1; p++)
                {
                    md.AddTriangle(centerIdx, p + 1, p);
                }
                if (param.CloseLoop)
                {
                    md.AddTriangle(centerIdx, 0, profileCount - 1);
                }
            }

            return md;
        }

        /// <summary>
        /// メッシュデータの変換（FlipY, FlipZ）
        /// </summary>
        public static void TransformMeshData(MeshData md, bool flipY, bool flipZ)
        {
            if (!flipY && !flipZ) return;

            foreach (var vertex in md.Vertices)
            {
                Vector3 pos = vertex.Position;

                if (flipY)
                {
                    pos = new Vector3(-pos.x, pos.y, -pos.z);
                }

                if (flipZ)
                {
                    pos = new Vector3(-pos.x, -pos.y, pos.z);
                }

                vertex.Position = pos;

                // 法線も変換
                for (int i = 0; i < vertex.Normals.Count; i++)
                {
                    Vector3 norm = vertex.Normals[i];
                    if (flipY)
                    {
                        norm = new Vector3(-norm.x, norm.y, -norm.z);
                    }
                    if (flipZ)
                    {
                        norm = new Vector3(-norm.x, -norm.y, norm.z);
                    }
                    vertex.Normals[i] = norm;
                }
            }
        }
    }
}