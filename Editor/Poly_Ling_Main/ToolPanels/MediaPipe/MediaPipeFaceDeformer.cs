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
        /// ランドマークJSONからXY座標配列を読み込む（468頂点）
        /// </summary>
        public static Vector2[] LoadLandmarks(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath);
            var data = JsonUtility.FromJson<LandmarkFile>(json);
            if (data.faces == null || data.faces.Length == 0)
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

        /// <summary>
        /// 三角形JSONから三角形インデックス配列を読み込む
        /// </summary>
        public static int[][] LoadTriangles(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath);
            var data = JsonUtility.FromJson<TriangleFile>(json);
            return data.GetTriangles();
        }

        // ================================================================
        // 基準メッシュ設定
        // ================================================================

        /// <summary>
        /// MediaPipe基準メッシュを設定する。
        /// </summary>
        public void SetBaseMesh(Vector2[] landmarks, int[][] triangles)
        {
            _baseLandmarks = landmarks;
            _triangles = triangles;
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
        // JSONシリアライズ用クラス
        // ================================================================

        [Serializable]
        private class LandmarkFile
        {
            public string schema;
            public int num_faces_detected;
            public FaceData[] faces;
        }

        [Serializable]
        private class FaceData
        {
            public int face_index;
            public ImageData image;
            public LandmarkData[] landmarks;
        }

        [Serializable]
        private class ImageData
        {
            public string path;
            public int width;
            public int height;
        }

        [Serializable]
        private class LandmarkData
        {
            public int index;
            public float x;
            public float y;
            public float z;
            public float pixel_x;
            public float pixel_y;
        }

        [Serializable]
        private class TriangleFile
        {
            public string source;
            public int triangle_count;
            public int vertex_count;
            // JsonUtilityではジャグ配列をデシリアライズできないため手動パース
            [NonSerialized] public int[][] _triangles;

            public int[][] GetTriangles()
            {
                return _triangles;
            }
        }

        /// <summary>
        /// 三角形JSONを手動パースする（JsonUtilityはジャグ配列非対応のため）
        /// </summary>
        public static int[][] ParseTrianglesJson(string json)
        {
            // "triangles" 配列を手動抽出
            // 形式: "triangles": [[a,b,c],[d,e,f],...]
            int idx = json.IndexOf("\"triangles\"", StringComparison.Ordinal);
            if (idx < 0)
                throw new InvalidOperationException("triangles field not found");

            // 最初の '[' を2つ見つける（"triangles": [ [ ... ）
            int bracketStart = json.IndexOf('[', idx);
            if (bracketStart < 0)
                throw new InvalidOperationException("triangles array not found");

            var triangles = new List<int[]>();
            int pos = bracketStart + 1; // 外側 '[' の次

            while (pos < json.Length)
            {
                // 次の '[' を探す
                int subStart = json.IndexOf('[', pos);
                if (subStart < 0) break;

                int subEnd = json.IndexOf(']', subStart);
                if (subEnd < 0) break;

                // [a, b, c] の中身をパース
                string inner = json.Substring(subStart + 1, subEnd - subStart - 1);
                string[] parts = inner.Split(',');
                if (parts.Length == 3)
                {
                    triangles.Add(new int[]
                    {
                        int.Parse(parts[0].Trim()),
                        int.Parse(parts[1].Trim()),
                        int.Parse(parts[2].Trim())
                    });
                }

                pos = subEnd + 1;

                // 外側 ']' に達したら終了
                // 次の非空白文字が ']' なら終了
                while (pos < json.Length && (json[pos] == ' ' || json[pos] == '\n' ||
                       json[pos] == '\r' || json[pos] == '\t' || json[pos] == ','))
                    pos++;

                if (pos < json.Length && json[pos] == ']')
                    break;
            }

            return triangles.ToArray();
        }
    }
}
