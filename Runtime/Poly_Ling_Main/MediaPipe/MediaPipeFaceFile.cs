// MediaPipeFaceFile.cs
// MediaPipe の顔を保存したファイルの形（ランドマーク JSON と三角形 JSON）。
// 能面の生成（NohMaskMeshGenerator）・メッシュの書き出し（NohMaskMeshExporter）・
// 顔フィット（MediaPipeFaceDeformer）が使う。読み書きは Unity の JsonUtility（三角形は手で整形・正規表現で読む）。
// Runtime/Poly_Ling_Main/MediaPipe/ に配置
//
// ■ ランドマーク JSON（schema "mediapipe.face_landmarker"）
//   {schema, num_faces_detected,
//    faces:[{face_index, image:{path, width, height},
//            landmarks:[{index, x, y, z, pixel_x, pixel_y}, …]}]}
//   x, y, z はライブ（MediaPipeFrame）の landmarks と同じ正規化座標。pixel_x / pixel_y は画素。
//   478 点（先頭 468 点が顔メッシュ、残り 10 点は虹彩）。
//   ※ NohMaskMeshExporter は任意のメッシュの頂点位置（PolyLing の座標そのまま）をこの形の入れ物に書く。
//     そのファイルの x, y, z は正規化座標ではない（schema は同じなので、使う側が取り違えないこと）。
//
// ■ 三角形 JSON
//   {triangle_count, vertex_count, triangles:[[i0, i1, i2, …], …]}   ← 3 点以上の多角形も許す

using System;

namespace Poly_Ling.Tools.MediaPipe
{
    /// <summary>ランドマーク JSON の全体。</summary>
    [Serializable]
    public class MediaPipeFaceFile
    {
        public const string SchemaName = "mediapipe.face_landmarker";

        public string schema;
        public int num_faces_detected;
        public MediaPipeFaceFileFace[] faces;
    }

    /// <summary>顔 1 つ分。</summary>
    [Serializable]
    public class MediaPipeFaceFileFace
    {
        public int face_index;
        public MediaPipeFaceFileImage image;
        public MediaPipeFaceFileLandmark[] landmarks;
    }

    /// <summary>元画像。</summary>
    [Serializable]
    public class MediaPipeFaceFileImage
    {
        public string path;
        public int width;
        public int height;
    }

    /// <summary>点 1 つ。</summary>
    [Serializable]
    public class MediaPipeFaceFileLandmark
    {
        public int index;
        public float x;
        public float y;
        public float z;
        public float pixel_x;
        public float pixel_y;
    }

    /// <summary>三角形（多角形）JSON。</summary>
    [Serializable]
    public class MediaPipeFaceTriangles
    {
        public int triangle_count;
        public int vertex_count;
        public int[][] triangles;
    }
}
