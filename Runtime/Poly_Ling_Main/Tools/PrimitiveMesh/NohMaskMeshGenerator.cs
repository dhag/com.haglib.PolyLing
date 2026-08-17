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
        public int FaceIndex;
        public bool FlipFaces;

        /// <summary>X軸反転（頂点位置の x の符号を反転）。</summary>
        public bool FlipX;

        /// <summary>Y軸反転（頂点位置の y の符号を反転）。</summary>
        public bool FlipY;

        /// <summary>Z軸反転（頂点位置の z の符号を反転）。</summary>
        public bool FlipZ;

        /// <summary>内側の穴（目・口）を面で塞ぐ。</summary>
        public bool FillHoles;

        /// <summary>外縁を外向きに拡張する（餃子の羽根）。</summary>
        public bool RimEnabled;

        /// <summary>外縁の拡張幅。外周ループの平均半径に対する比率。</summary>
        public float RimWidth;

        public static FaceMeshParams Default => new FaceMeshParams
        {
            MeshName           = "FaceMesh",
            LandmarksFilePath  = "",
            TrianglesFilePath  = "",
            Scale              = 10f,
            DepthScale         = 1f,
            FaceIndex          = 0,
            FlipFaces          = false,
            FlipX              = false,
            FlipY              = false,
            FlipZ              = false,
            FillHoles          = true,
            RimEnabled         = true,
            RimWidth           = 0.4f,
        };

        public bool Equals(FaceMeshParams o) =>
            MeshName          == o.MeshName          &&
            LandmarksFilePath == o.LandmarksFilePath &&
            TrianglesFilePath == o.TrianglesFilePath &&
            Mathf.Approximately(Scale,      o.Scale)      &&
            Mathf.Approximately(DepthScale, o.DepthScale) &&
            FaceIndex == o.FaceIndex &&
            FlipFaces == o.FlipFaces &&
            FlipX     == o.FlipX     &&
            FlipY     == o.FlipY     &&
            FlipZ     == o.FlipZ     &&
            FillHoles == o.FillHoles &&
            RimEnabled == o.RimEnabled &&
            Mathf.Approximately(RimWidth, o.RimWidth);

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
            //
            // MediaPipe は右手系（x 右・y 下・z 奥）、Unity は左手系。
            // 鏡像にせず取り込むには変換全体の行列式が -1 でなければならない
            // （AuthoringFrame の規約）。したがって基底は
            //   x -> -(lm.x - cx)   ... 画像の右＝本人の左。正面ビューの画面右は -X なので符号反転
            //   y -> -(lm.y - cy)   ... 画像は y 下向き
            //   z -> -(lm.z - cz)   ... MediaPipe は手前が負。顔の前方を +Z へ
            // の 3 軸反転（行列式 -1）とする。
            //
            // Flip* は補正用ではなく、この基底に対する任意の反転オプション。既定はすべて false。
            float sx = p.FlipX ? -1f : 1f;
            float sy = p.FlipY ? -1f : 1f;
            float sz = p.FlipZ ? -1f : 1f;

            var positions = new Vector3[faceData.landmarks.Length];
            for (int i = 0; i < faceData.landmarks.Length; i++)
            {
                var lm = faceData.landmarks[i];
                float x = -(lm.x - center.x) * p.Scale;
                float y = ((1f - lm.y) - (1f - center.y)) * p.Scale;
                float z = -(lm.z - center.z) * p.Scale * p.DepthScale;
                positions[i] = new Vector3(x * sx, y * sy, z * sz);
            }

            // 頂点追加（結合しない）
            for (int i = 0; i < faceData.landmarks.Length; i++)
            {
                var lm = faceData.landmarks[i];
                Vector2 uv = new Vector2(lm.x, 1f - lm.y);
                // ここでの法線は仮置き。生成末尾の RecalculateSmoothNormals() で
                // 形状から求め直される。
                md.Vertices.Add(new Vertex(positions[i], uv, Vector3.forward));
            }

            // 面の生成（N角形対応。3頂点未満のみ除外する）
            foreach (var tri in triangles.triangles)
            {
                if (tri == null || tri.Length < 3) continue;

                // 範囲外インデックスを含む面はスキップする
                bool valid = true;
                for (int k = 0; k < tri.Length; k++)
                {
                    if (tri[k] < 0 || tri[k] >= md.VertexCount) { valid = false; break; }
                }
                if (!valid) continue;

                // 行列式 -1 の取り込みで巻き順の向きが反転するため、基底では逆回りに張る。
                // FlipFaces はそこからさらに反転させる任意オプション。
                var vi = new List<int>(tri.Length);
                if (p.FlipFaces)
                {
                    for (int k = 0; k < tri.Length; k++) vi.Add(tri[k]);
                }
                else
                {
                    for (int k = tri.Length - 1; k >= 0; k--) vi.Add(tri[k]);
                }

                // UV/法線サブインデックスは従来の AddTriangle と同じく全て 0。
                var uvi = new List<int>(tri.Length);
                var ni  = new List<int>(tri.Length);
                for (int k = 0; k < tri.Length; k++) { uvi.Add(0); ni.Add(0); }

                md.AddFace(new Face
                {
                    VertexIndices = vi,
                    UVIndices     = uvi,
                    NormalIndices = ni,
                    MaterialIndex = 0,
                });
            }

            // 穴埋め → 外縁拡張 の順に適用する。
            ApplyHolesAndRim(md, p);

            md.RecalculateSmoothNormals();
            return md;
        }

        /// <summary>
        /// 内側の穴を塞ぎ、外縁を外向きに拡張する。
        /// 境界ループの抽出・分類は <see cref="Poly_Ling.Tools.BoundaryLoopUtil"/> に委譲する。
        /// </summary>
        private static void ApplyHolesAndRim(MeshObject md, FaceMeshParams p)
        {
            if (md == null || md.FaceCount == 0) return;
            if (!p.FillHoles && !p.RimEnabled) return;

            var cache = new Poly_Ling.Selection.TopologyCache(md);
            var rawLoops = Poly_Ling.Tools.BoundaryLoopUtil.FindBoundaryLoops(md, cache);
            if (rawLoops.Count == 0) return;

            var isolated = Poly_Ling.Tools.BoundaryLoopUtil.FindIsolatedVertices(md);
            var loops    = Poly_Ling.Tools.BoundaryLoopUtil.Classify(md, rawLoops, isolated);
            float sign   = Poly_Ling.Tools.BoundaryLoopUtil.FaceOrientationSignXY(md);

            if (p.FillHoles)
                Poly_Ling.Tools.MediaPipeFaceHoleFiller.FillAllHoles(md, loops, sign);

            if (p.RimEnabled && p.RimWidth > 0f)
            {
                foreach (var l in loops)
                {
                    if (l.Kind != Poly_Ling.Tools.BoundaryLoopKind.Outer) continue;

                    // 幅は外周ループの平均半径に対する比率で決める（Scale 非依存）。
                    Vector3 c = Poly_Ling.Tools.BoundaryLoopUtil.CentroidXY(md, l.Vertices);
                    float r = 0f;
                    foreach (int vi in l.Vertices)
                    {
                        var q = md.Vertices[vi].Position;
                        r += new Vector2(q.x - c.x, q.y - c.y).magnitude;
                    }
                    r /= Mathf.Max(1, l.Vertices.Count);

                    Poly_Ling.Tools.BoundaryRimExtruder.Extend(md, l.Vertices, r * p.RimWidth, sign);
                    break;
                }
            }
        }

        /// <summary>
        /// ファイルパスから JSON を読み込みメッシュを生成する。
        /// </summary>
        public static MeshObject GenerateFromFiles(FaceMeshParams p)
        {
            string landmarksJson, trianglesJson;

            // ランドマーク: 未選択なら内蔵デフォルト（プリセット）
            if (string.IsNullOrEmpty(p.LandmarksFilePath))
                landmarksJson = FaceLandmarksData.Json;
            else
            {
                try { landmarksJson = System.IO.File.ReadAllText(p.LandmarksFilePath); }
                catch (Exception ex)
                {
                    Debug.LogError($"[NohMaskMeshGenerator] Failed to load landmarks: {ex.Message}");
                    return new MeshObject(p.MeshName);
                }
            }

            // トライアングル: 未選択なら内蔵デフォルト（プリセット）
            if (string.IsNullOrEmpty(p.TrianglesFilePath))
                trianglesJson = FaceTrianglesData.Json;
            else
            {
                try { trianglesJson = System.IO.File.ReadAllText(p.TrianglesFilePath); }
                catch (Exception ex)
                {
                    Debug.LogError($"[NohMaskMeshGenerator] Failed to load triangles: {ex.Message}");
                    return new MeshObject(p.MeshName);
                }
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
        /// 面インデックス JSON を手動パース（N角形対応）。
        /// JsonUtility はネストした配列を扱えないため Regex を使用する。
        /// triangles 配列の各要素は要素数固定ではなく、3要素以上なら N 角形として読み込む。
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

            // N角形対応: 要素数を固定せず [a, b, c, ...] を丸ごと拾って分解する。
            // 外側の "triangles": [ の直後は空白＋'[' が続くため数字が来ず、外側配列には一致しない。
            var pattern = new Regex(@"\[\s*\d+(?:\s*,\s*\d+)*\s*\]");
            foreach (Match m in pattern.Matches(json))
            {
                string inner = m.Value.Trim().TrimStart('[').TrimEnd(']');
                var parts = inner.Split(',');
                var poly  = new int[parts.Length];
                bool ok   = true;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!int.TryParse(parts[i].Trim(), out poly[i])) { ok = false; break; }
                }
                if (ok && poly.Length >= 3) list.Add(poly);
            }

            result.triangles = list.ToArray();
            return result;
        }
    }
}
