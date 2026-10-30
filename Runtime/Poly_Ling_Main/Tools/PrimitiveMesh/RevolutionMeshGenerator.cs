// Assets/Editor/MeshCreators/Revolution/RevolutionMeshGenerator.cs
// 回転体メッシュ生成ロジック
//
// 【頂点順序の規約】
// グリッド配置: i0=左下, i1=右下, i2=右上, i3=左上
// 表面: md.AddQuad(i0, i1, i2, i3)

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Revolution
{
    /// <summary>
    /// 回転体メッシュ生成ユーティリティ
    /// </summary>
    public static class RevolutionMeshGenerator
    {
        /// <summary>
        /// 回転体メッシュを生成
        /// 【3D の点】点 (x, y, z) は (半径, y) に直してから回す。
        ///   半径 = x の符号 × √(x²+z²)。回す軸はローカルの Y 軸なので、
        ///   3D の曲線を回した面は (半径, y) の曲線を回した面と同じになる
        ///   （違うのは各点の周方向の始まりの角度だけ）。z=0 のときは従来と同じ。
        /// </summary>
        public static MeshObject Generate(IReadOnlyList<Vector3> profile3, RevolutionParams p)
        {
            var profile = ToRadial(profile3);
            if (profile.Count < 2)
                return new MeshObject(p.MeshName);

            MeshObject md;
            if (p.Spiral)
            {
                md = GenerateSpiral(profile, p);
            }
            else
            {
                md = GenerateRevolution(profile, p.RadialSegments, p.CornerSplitAngle, p.CloseTop, p.CloseBottom, p.CloseLoop, p.Pivot);
            }

            md.Name = p.MeshName;

            // 変換適用
            TransformMeshObject(md, p.FlipY, p.FlipZ);

            return md;
        }

        /// <summary>3D の点列を (半径, y) の点列へ直す（RevolutionProfileEditCore.ToRadial と同じ）。</summary>
        private static List<Vector2> ToRadial(IReadOnlyList<Vector3> src)
            => RevolutionProfileEditCore.ToRadial(src);

        /// <summary>
        /// 周方向の角度列（ラジアン）を作る。最後の要素は継ぎ目の列で、先頭 + 2π。
        /// cornerSplitDeg = 0 のとき: r × step（r = 0..N）。列数 N+1。
        /// cornerSplitDeg = a > 0 のとき: 各 θ_r を θ_r−a と θ_r+a の 2 本に割る。列数 2N+1。
        ///   a は隣の稜線と重ならないよう半ステップ未満に制限する。
        /// </summary>
        public static float[] BuildRadialAngles(int radialSeg, float cornerSplitDeg)
        {
            float step = 2f * Mathf.PI / radialSeg;
            float a = cornerSplitDeg * Mathf.Deg2Rad;
            float maxA = step * 0.5f * 0.999f;
            if (a > maxA) a = maxA;

            if (!(a > 0f))
            {
                var plain = new float[radialSeg + 1];
                for (int r = 0; r <= radialSeg; r++) plain[r] = r * step;
                return plain;
            }

            var split = new float[radialSeg * 2 + 1];
            for (int r = 0; r < radialSeg; r++)
            {
                split[r * 2]     = r * step - a;
                split[r * 2 + 1] = r * step + a;
            }
            split[radialSeg * 2] = split[0] + 2f * Mathf.PI;
            return split;
        }

        /// <summary>
        /// 通常の回転体メッシュを生成
        /// </summary>
        public static MeshObject GenerateRevolution(List<Vector2> profile, int radialSeg, float cornerSplitDeg, bool closeTop, bool closeBottom, bool closeLoop, Vector3 pivot)
        {
            float[] angles = BuildRadialAngles(radialSeg, cornerSplitDeg);
            int segCount = angles.Length - 1;   // 周方向の面の数
            var md = new MeshObject("Revolution");

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

            int profileCount = profile.Count;
            int cols = segCount + 1;

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

                for (int r = 0; r <= segCount; r++)
                {
                    float angle = angles[r];
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    Vector3 pos = new Vector3(cos * radius, y, sin * radius) - pivotOffset;
                    Vector3 normal = new Vector3(cos * normal2D.x, normal2D.y, sin * normal2D.x).normalized;
                    Vector2 uv = new Vector2((float)r / segCount, v);

                    md.Vertices.Add(new Vertex(pos, uv, normal));
                }
            }

            // 側面の四角形
            int loopCount = closeLoop ? profileCount : profileCount - 1;
            for (int p = 0; p < loopCount; p++)
            {
                int nextP = (p + 1) % profileCount;
                for (int r = 0; r < segCount; r++)
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
                for (int r = 0; r < segCount; r++)
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

                for (int r = 0; r < segCount; r++)
                {
                    md.AddTriangle(centerIdx, r, r + 1);
                }
            }

            return md;
        }

        /// <summary>
        /// らせんメッシュを生成
        /// </summary>
        public static MeshObject GenerateSpiral(List<Vector2> profile, RevolutionParams param)
        {
            var md = new MeshObject("Spiral");

            // 1 巻きぶんの角度列（角分割を含む）を巻き数ぶん繰り返す
            float[] angles = BuildRadialAngles(param.RadialSegments, param.CornerSplitAngle);
            int segCount = angles.Length - 1;   // 1 巻きの周方向の面の数
            int totalRadialSteps = segCount * param.SpiralTurns;
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

            // 頂点生成
            for (int r = 0; r <= totalRadialSteps; r++)
            {
                float angle = SpiralAngle(angles, segCount, r);
                float yOffset = SpiralYOffset(angle, param.SpiralPitch);
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

                float endAngle = SpiralAngle(angles, segCount, totalRadialSteps);
                float topYOffset = SpiralYOffset(endAngle, param.SpiralPitch);

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

                float startAngle = angles[0];
                float bottomYOffset = SpiralYOffset(startAngle, param.SpiralPitch);

                Vector3 bottomCenter = new Vector3(
                    Mathf.Cos(startAngle) * profileCenterX,
                    profileCenterY + bottomYOffset,
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

        /// <summary>らせんの r 番目の列の角度。1 巻きの角度列を巻き数ぶん 2π ずつ足して繰り返す。</summary>
        private static float SpiralAngle(float[] angles, int segCount, int r)
        {
            int turn = r / segCount;
            int idx = r % segCount;
            return angles[idx] + turn * 2f * Mathf.PI;
        }

        /// <summary>らせんの上がり幅。角度に比例し、1 巻き（2π）で pitch。</summary>
        private static float SpiralYOffset(float angle, float pitch)
            => angle / (2f * Mathf.PI) * pitch;

        /// <summary>
        /// メッシュデータの変換（FlipY, FlipZ）
        /// </summary>
        public static void TransformMeshObject(MeshObject md, bool flipY, bool flipZ)
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