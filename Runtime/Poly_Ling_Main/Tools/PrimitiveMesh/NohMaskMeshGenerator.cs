// NohMaskMeshGenerator.cs
// FaceMesh（MediaPipe Face Landmarks）ベースメッシュ生成ロジック。
// NohMaskMeshCreatorWindow から生成ロジックを分離した Runtime 用クラス。
//
// 【座標系変換】
// MediaPipe: x(左→右, 0→1), y(上→下, 0→1), z(手前→奥, 負→正)
// Unity:     x(左→右), y(下→上), z(手前→奥)
// → yを反転 (1 - y) して変換
//
// 【重要】
// MediaPipe FaceMeshのランドマークは一意のインデックスを持つため、
// 頂点の自動結合は行わない。
//
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.NohMask
{
    // ================================================================
    // JSON構造体定義
    // ================================================================

    [Serializable]
    public class FaceLandmarksJson
    {
        public string schema;
        public int num_faces_detected;
        public FaceData[] faces;
    }

    [Serializable]
    public class FaceData
    {
        public int face_index;
        public ImageData image;
        public Landmark[] landmarks;
    }

    [Serializable]
    public class ImageData
    {
        public string path;
        public int width;
        public int height;
    }

    [Serializable]
    public class Landmark
    {
        public int index;
        public float x;
        public float y;
        public float z;
        public float pixel_x;
        public float pixel_y;
    }

    [Serializable]
    public class FaceMeshTrianglesJson
    {
        public int triangle_count;
        public int vertex_count;
        public int[][] triangles;
    }

    // ================================================================
    // パラメータ構造体
    // ================================================================

    [Serializable]
    public struct FaceMeshParams : IEquatable<FaceMeshParams>
    {
        public string MeshName;
        public string LandmarksFilePath;
        public string TrianglesFilePath;
        public float Scale;
        public float DepthScale;
        public float RotationX, RotationY;
        public int FaceIndex;
        public bool FlipFaces;

        public static FaceMeshParams Default => new FaceMeshParams
        {
            MeshName           = "FaceMesh",
            LandmarksFilePath  = "",
            TrianglesFilePath  = "",
            Scale              = 10f,
            DepthScale         = 1f,
            RotationX          = 0f,
            RotationY          = 180f,
            FaceIndex          = 0,
            FlipFaces          = false,
        };

        public bool Equals(FaceMeshParams o) =>
            MeshName          == o.MeshName          &&
            LandmarksFilePath == o.LandmarksFilePath &&
            TrianglesFilePath == o.TrianglesFilePath &&
            Mathf.Approximately(Scale,      o.Scale)      &&
            Mathf.Approximately(DepthScale, o.DepthScale) &&
            Mathf.Approximately(RotationX,  o.RotationX)  &&
            Mathf.Approximately(RotationY,  o.RotationY)  &&
            FaceIndex == o.FaceIndex &&
            FlipFaces == o.FlipFaces;

        public override bool Equals(object obj) => obj is FaceMeshParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // ================================================================
    // メッシュ生成クラス
    // ================================================================

    /// <summary>
    /// FaceMesh メッシュ生成ユーティリティ。
    /// ★★★ 頂点の自動結合は行わない ★★★
    /// </summary>
    public static class NohMaskMeshGenerator
    {
        /// <summary>
        /// JSON文字列からメッシュを生成する。
        /// </summary>
        /// <param name="p">生成パラメータ</param>
        /// <param name="landmarksJson">face_landmarks.json の中身</param>
        /// <param name="trianglesJson">facemesh_triangles.json の中身</param>
        public static MeshObject Generate(FaceMeshParams p,
                                          string landmarksJson,
                                          string trianglesJson)
        {
            var md = new MeshObject(p.MeshName);

            if (string.IsNullOrEmpty(landmarksJson) || string.IsNullOrEmpty(trianglesJson))
                return md;

            FaceLandmarksJson landmarks;
            FaceMeshTrianglesJson triangles;

            try
            {
                landmarks = JsonUtility.FromJson<FaceLandmarksJson>(landmarksJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NohMaskMeshGenerator] Failed to parse landmarks JSON: {ex.Message}");
                return md;
            }

            try
            {
                triangles = ParseTrianglesJson(trianglesJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NohMaskMeshGenerator] Failed to parse triangles JSON: {ex.Message}");
                return md;
            }

            if (landmarks?.faces == null || landmarks.faces.Length == 0)
                return md;
            if (triangles?.triangles == null)
                return md;

            int faceIndex = Mathf.Clamp(p.FaceIndex, 0, landmarks.faces.Length - 1);
            var faceData  = landmarks.faces[faceIndex];

            if (faceData?.landmarks == null || faceData.landmarks.Length == 0)
                return md;

            // 中心を計算
            Vector3 center = Vector3.zero;
            foreach (var lm in faceData.landmarks)
                center += new Vector3(lm.x, lm.y, lm.z);
            center /= faceData.landmarks.Length;

            // 頂点位置を計算（MediaPipe座標系 → Unity座標系）
            var positions = new Vector3[faceData.landmarks.Length];
            for (int i = 0; i < faceData.landmarks.Length; i++)
            {
                var lm = faceData.landmarks[i];
                float x = (lm.x - center.x) * p.Scale;
                float y = ((1f - lm.y) - (1f - center.y)) * p.Scale;
                float z = -(lm.z - center.z) * p.Scale * p.DepthScale;
                positions[i] = new Vector3(x, y, z);
            }

            // 頂点追加（結合しない）
            for (int i = 0; i < faceData.landmarks.Length; i++)
            {
                var lm = faceData.landmarks[i];
                Vector2 uv = new Vector2(lm.x, 1f - lm.y);
                md.Vertices.Add(new Vertex(positions[i], uv, Vector3.forward));
            }

            // 三角形面の生成
            foreach (var tri in triangles.triangles)
            {
                if (tri == null || tri.Length != 3) continue;
                int i0 = tri[0], i1 = tri[1], i2 = tri[2];
                if (i0 < 0 || i1 < 0 || i2 < 0) continue;
                if (i0 >= md.VertexCount || i1 >= md.VertexCount || i2 >= md.VertexCount) continue;

                if (p.FlipFaces)
                    md.AddTriangle(i0, i2, i1);
                else
                    md.AddTriangle(i0, i1, i2);
            }

            md.RecalculateSmoothNormals();
            return md;
        }

        /// <summary>
        /// ファイルパスから JSON を読み込みメッシュを生成する。
        /// </summary>
        public static MeshObject GenerateFromFiles(FaceMeshParams p)
        {
            if (string.IsNullOrEmpty(p.LandmarksFilePath) ||
                string.IsNullOrEmpty(p.TrianglesFilePath))
                return new MeshObject(p.MeshName);

            string landmarksJson = null, trianglesJson = null;

            try { landmarksJson = System.IO.File.ReadAllText(p.LandmarksFilePath); }
            catch (Exception ex)
            {
                Debug.LogError($"[NohMaskMeshGenerator] Failed to load landmarks: {ex.Message}");
                return new MeshObject(p.MeshName);
            }

            try { trianglesJson = System.IO.File.ReadAllText(p.TrianglesFilePath); }
            catch (Exception ex)
            {
                Debug.LogError($"[NohMaskMeshGenerator] Failed to load triangles: {ex.Message}");
                return new MeshObject(p.MeshName);
            }

            return Generate(p, landmarksJson, trianglesJson);
        }

        /// <summary>
        /// ランドマーク JSON をパースして num_faces_detected を返す。
        /// UI でのファイル選択後の顔数表示用。
        /// </summary>
        public static int GetNumFacesDetected(string landmarksJson)
        {
            if (string.IsNullOrEmpty(landmarksJson)) return 0;
            try
            {
                var data = JsonUtility.FromJson<FaceLandmarksJson>(landmarksJson);
                return data?.num_faces_detected ?? 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// 三角形 JSON を手動パース。
        /// JsonUtility はネストした配列を扱えないため Regex を使用する。
        /// </summary>
        public static FaceMeshTrianglesJson ParseTrianglesJson(string json)
        {
            var result = new FaceMeshTrianglesJson();
            var list   = new List<int[]>();

            var tcMatch = Regex.Match(json, @"""triangle_count""\s*:\s*(\d+)");
            if (tcMatch.Success)
                int.TryParse(tcMatch.Groups[1].Value, out result.triangle_count);

            var vcMatch = Regex.Match(json, @"""vertex_count""\s*:\s*(\d+)");
            if (vcMatch.Success)
                int.TryParse(vcMatch.Groups[1].Value, out result.vertex_count);

            var pattern = new Regex(@"\[\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\]");
            foreach (Match m in pattern.Matches(json))
            {
                if (m.Groups.Count >= 4)
                {
                    list.Add(new[]
                    {
                        int.Parse(m.Groups[1].Value),
                        int.Parse(m.Groups[2].Value),
                        int.Parse(m.Groups[3].Value),
                    });
                }
            }

            result.triangles = list.ToArray();
            return result;
        }
    }
}
