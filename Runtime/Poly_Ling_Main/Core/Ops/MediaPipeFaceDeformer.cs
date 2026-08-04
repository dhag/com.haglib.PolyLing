// Assets/Editor/Poly_Ling/ToolPanels/MediaPipe/MediaPipeFaceDeformer.cs
// MediaPipeフェイスメッシュの変形を独自モデル頂点に転写する。
// XYのみ変形、Zは不変。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Poly_Ling.Tools.MediaPipe
{
    /// <summary>
    /// MediaPipeフェイスメッシュの変形を独自モデル頂点に転写する。
    /// </summary>
    public class MediaPipeFaceDeformer
    {
        // ================================================================
        // バインド情報
        // ================================================================

        /// <summary>
        /// 独自モデル頂点ごとの所属三角形と重心座標
        /// </summary>
        public struct BindInfo
        {
            public int vertexIndex;    // 独自モデル側の頂点インデックス
            public int triangleIndex;  // MediaPipe三角形インデックス
            public float alpha;        // P の重み
            public float beta;         // Q の重み
            public float gamma;        // R の重み
        }

        // ================================================================
        // フィールド
        // ================================================================

        private Vector2[] _baseLandmarks;   // MediaPipe基準メッシュ (468頂点, XYのみ)
        private int[][] _triangles;         // 三角形インデックス配列 [852][3]
        private BindInfo[] _bindings;       // バインド結果

        /// <summary>バインドされた頂点数</summary>
        public int BindCount => _bindings?.Length ?? 0;

        // ================================================================
        // JSON読み込み
        // ================================================================

        /// <summary>
        /// ランドマークJSONからXY座標配列を読み込む（468頂点）。
        /// JSON スキーマ定義は <see cref="Poly_Ling.NohMask.FaceLandmarksJson"/> と共通。
        /// </summary>
        public static Vector2[] LoadLandmarks(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath);
            var data = JsonUtility.FromJson<Poly_Ling.NohMask.FaceLandmarksJson>(json);
            if (data?.faces == null || data.faces.Length == 0)
                throw new InvalidOperationException($"No faces in {jsonPath}");

            var landmarks = data.faces[0].landmarks;
            // メッシュ頂点は先頭468個（残り10は虹彩）
            int count = Mathf.Min(landmarks.Length, 468);
            var result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = new Vector2(landmarks[i].x, landmarks[i].y);
            }
            return result;
        }

        // ================================================================
        // 基準メッシュ設定
        // ================================================================

        /// <summary>
        /// MediaPipe基準メッシュを設定する。
        /// 4頂点以上の多角形は内部で三角形へ分解して保持する
        /// （分解は <see cref="Poly_Ling.Data.Face.ToTriangleIndices"/> の扇形分割に委譲）。
        /// Bind / Apply は3頂点前提で参照するため、ここで必ず三角形化しておくこと。
        /// </summary>
        public void SetBaseMesh(Vector2[] landmarks, int[][] triangles)
        {
            _baseLandmarks = landmarks;
            _triangles     = Triangulate(triangles);
        }

        /// <summary>
        /// 多角形インデックス列を三角形のみの配列へ分解する。
        /// 3頂点はそのまま通し、4頂点以上のみ Face の扇形分割を利用する。
        /// </summary>
        private static int[][] Triangulate(int[][] polygons)
        {
            if (polygons == null) return new int[0][];

            var result = new List<int[]>(polygons.Length);
            foreach (var poly in polygons)
            {
                if (poly == null || poly.Length < 3) continue;

                if (poly.Length == 3)
                {
                    result.Add(poly);
                    continue;
                }

                var face = new Poly_Ling.Data.Face { VertexIndices = new List<int>(poly) };
                int[] flat = face.ToTriangleIndices();
                for (int i = 0; i + 2 < flat.Length; i += 3)
                    result.Add(new[] { flat[i], flat[i + 1], flat[i + 2] });
            }
            return result.ToArray();
        }

        // ================================================================
        // バインド（前処理・1回）
        // ================================================================

        /// <summary>
        /// 独自モデルの頂点群をMediaPipe基準メッシュにバインドする。
        /// </summary>
        /// <param name="vertices">独自モデル頂点（XY使用、Zは無視）</param>
        /// <returns>バインドされた頂点数</returns>
        public int Bind(Vector3[] vertices)
        {
            var result = new List<BindInfo>();
            //すべての頂点について、三角形とその頂点の重み係数αβγを求める。
            for (int i = 0; i < vertices.Length; i++)
            {
                float vx = vertices[i].x;
                float vy = vertices[i].y;

                for (int t = 0; t < _triangles.Length; t++)
                {
                    int pi = _triangles[t][0];
                    int qi = _triangles[t][1];
                    int ri = _triangles[t][2];

                    Vector2 P = _baseLandmarks[pi];
                    Vector2 Q = _baseLandmarks[qi];
                    Vector2 R = _baseLandmarks[ri];

                    if (TryBarycentric(P, Q, R, vx, vy, out float a, out float b, out float g))
                    {
                        result.Add(new BindInfo
                        {
                            vertexIndex = i,
                            triangleIndex = t,
                            alpha = a,
                            beta = b,
                            gamma = g
                        });
                        break;
                    }
                }
                // どの三角形にも含まれない → バインドしない（変形対象外）
            }

            _bindings = result.ToArray();
            return _bindings.Length;
        }

        // ================================================================
        // 変形適用
        // ================================================================

        /// <summary>
        /// 変形後のMediaPipeランドマークを適用し、頂点のXYを更新する。
        /// </summary>
        /// <param name="deformedLandmarks">変形後MediaPipeランドマーク (468頂点)</param>
        /// <param name="vertices">頂点配列（直接書き換え）</param>
        public void Apply(Vector2[] deformedLandmarks, Vector3[] vertices)
        {
            for (int i = 0; i < _bindings.Length; i++)
            {
                ref BindInfo b = ref _bindings[i];
                int pi = _triangles[b.triangleIndex][0];
                int qi = _triangles[b.triangleIndex][1];
                int ri = _triangles[b.triangleIndex][2];

                Vector2 P = deformedLandmarks[pi];
                Vector2 Q = deformedLandmarks[qi];
                Vector2 R = deformedLandmarks[ri];

                float nx = b.alpha * P.x + b.beta * Q.x + b.gamma * R.x;
                float ny = b.alpha * P.y + b.beta * Q.y + b.gamma * R.y;

                vertices[b.vertexIndex].x = nx;
                vertices[b.vertexIndex].y = ny;
                // Z不変
            }
        }

        // ================================================================
        // 重心座標算出
        // ================================================================

        /// <summary>
        /// 重心座標を算出。全て>=0なら三角形内部。
        /// </summary>
        private static bool TryBarycentric(Vector2 P, Vector2 Q, Vector2 R,
            float vx, float vy, out float alpha, out float beta, out float gamma)
        {
            float v0x = Q.x - P.x, v0y = Q.y - P.y;
            float v1x = R.x - P.x, v1y = R.y - P.y;
            float v2x = vx - P.x,  v2y = vy - P.y;

            float d00 = v0x * v0x + v0y * v0y;
            float d01 = v0x * v1x + v0y * v1y;
            float d11 = v1x * v1x + v1y * v1y;
            float d20 = v2x * v0x + v2y * v0y;
            float d21 = v2x * v1x + v2y * v1y;

            float denom = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denom) < 1e-10f)
            {
                alpha = beta = gamma = 0;
                return false; // 退化三角形
            }

            float invDenom = 1f / denom;
            beta  = (d11 * d20 - d01 * d21) * invDenom;
            gamma = (d00 * d21 - d01 * d20) * invDenom;
            alpha = 1f - beta - gamma;

            const float eps = -1e-6f;
            return alpha >= eps && beta >= eps && gamma >= eps;
        }

        // ================================================================
        // 面インデックスJSONパース
        // ================================================================

        /// <summary>
        /// 面インデックスJSONをパースする（N角形対応）。
        /// 実装は <see cref="Poly_Ling.NohMask.NohMaskMeshGenerator.ParseTrianglesJson"/> に
        /// 一本化してあり、本メソッドはその薄いラッパー。
        /// 4頂点以上の多角形は SetBaseMesh 側で三角形へ分解される。
        /// </summary>
        public static int[][] ParseTrianglesJson(string json)
        {
            var parsed = Poly_Ling.NohMask.NohMaskMeshGenerator.ParseTrianglesJson(json);
            return parsed?.triangles ?? new int[0][];
        }
    }
}
