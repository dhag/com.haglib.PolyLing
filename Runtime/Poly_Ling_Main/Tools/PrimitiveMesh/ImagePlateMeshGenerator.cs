// ImagePlateMeshGenerator.cs
// 画像板：画像の縦横比で板を作り、厚みを付ける（Runtime / Editor 共有）。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【形】
//   表面（+Z 側）・裏面・側面 4 枚の直方体。面はすべて四角面。
//   表面と裏面は縦横に分割でき、側面は表裏の分割に合わせて割る
//   （重複頂点の結合で閉じた形になる）。
//
// 【表面の向きと UV】
//   平面（PlaneMeshGenerator、XY・表向き）と同じ規約。表は +Z を向き、
//   Front ビューの画面右は -X なので u は x と逆向きにする。
//   これで Front ビューから見たとき画像が正しい向きに出る。
//
// 【材質の仮番号】
//   表面の面には FrontFaceTag、それ以外の面には 0 を入れておく。
//   画像の材質スロットを作って番号を振り替えるのはモデルを持つ側
//   （PolyLingPlayerViewerCore.ApplyGeneratedMaterialIndex）。
//
// 【画素数】
//   縦横比は ImageWidth / ImageHeight から決める。生成器はファイルを読まない。
//   パネルは画像を選んだときに、コマンドの受け口は実行時に画像から書き込む。
//   どちらかが 0 以下なら正方形として作る（画像未選択のプレビュー）。

using System;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class ImagePlateMeshGenerator
    {
        /// <summary>表面の面に入れる材質の仮番号。それ以外の面は 0。</summary>
        public const int FrontFaceTag = 1;

        [Serializable]
        public struct ImagePlateParams : IEquatable<ImagePlateParams>
        {
            // ── 値域 ─────────────────────────────────────────────────

            /// <summary>長辺の長さの下限・上限</summary>
            public const float LongSideMin = 0.1f;
            public const float LongSideMax = 10f;

            /// <summary>厚みの下限・上限</summary>
            public const float ThicknessMin = 0.001f;
            public const float ThicknessMax = 1f;

            /// <summary>分割数の下限・上限</summary>
            public const int SegmentsMin = 1;
            public const int SegmentsMax = 32;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "ImagePlateImage", Description = "表面に貼る画像のパス。作業フォルダからの相対でも絶対でもよい")]
            public string ImagePath;
            [PLParam(TextKey = "ImagePlateImageWidth", Description = "画像の横の画素数。実行時は画像から取り直すので指定しなくてよい", Min = 0)]
            public int ImageWidth;
            [PLParam(TextKey = "ImagePlateImageHeight", Description = "画像の縦の画素数。実行時は画像から取り直すので指定しなくてよい", Min = 0)]
            public int ImageHeight;
            [PLParam(TextKey = "ImagePlateLongSide", Description = "長辺の長さ。短辺は画像の縦横比で決まる", Min = LongSideMin, Max = LongSideMax)]
            public float LongSide;
            [PLParam(TextKey = "Thickness", Description = "厚み（Z）", Min = ThicknessMin, Max = ThicknessMax)]
            public float Thickness;
            [PLParam(TextKey = "ImagePlateSegmentsX", Description = "横方向の分割数", Min = SegmentsMin, Max = SegmentsMax, Step = 1)]
            public int SegmentsX;
            [PLParam(TextKey = "ImagePlateSegmentsY", Description = "縦方向の分割数", Min = SegmentsMin, Max = SegmentsMax, Step = 1)]
            public int SegmentsY;
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static ImagePlateParams Default => new ImagePlateParams
            {
                MeshName    = "ImagePlate",
                ImagePath   = "",
                ImageWidth  = 0,
                ImageHeight = 0,
                LongSide    = 1f,
                Thickness   = 0.02f,
                SegmentsX   = 1,
                SegmentsY   = 1,
                Pivot       = Vector3.zero,
            };

            public bool Equals(ImagePlateParams o) =>
                MeshName == o.MeshName &&
                ImagePath == o.ImagePath &&
                ImageWidth == o.ImageWidth &&
                ImageHeight == o.ImageHeight &&
                Mathf.Approximately(LongSide,  o.LongSide)  &&
                Mathf.Approximately(Thickness, o.Thickness) &&
                SegmentsX == o.SegmentsX &&
                SegmentsY == o.SegmentsY &&
                Pivot == o.Pivot;

            public override bool Equals(object obj) => obj is ImagePlateParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>
        /// 画素数から板の幅・高さを決める。長辺を LongSide にそろえる。
        /// 画素数が無いときは正方形。
        /// </summary>
        public static Vector2 PlateSize(ImagePlateParams p)
        {
            float L = Mathf.Clamp(p.LongSide, ImagePlateParams.LongSideMin, ImagePlateParams.LongSideMax);
            if (p.ImageWidth <= 0 || p.ImageHeight <= 0) return new Vector2(L, L);
            float aspect = (float)p.ImageWidth / p.ImageHeight;
            return aspect >= 1f ? new Vector2(L, L / aspect) : new Vector2(L * aspect, L);
        }

        public static MeshObject Generate(ImagePlateParams p)
        {
            var md = new MeshObject(string.IsNullOrEmpty(p.MeshName) ? "ImagePlate" : p.MeshName);

            Vector2 size = PlateSize(p);
            float hw = size.x * 0.5f;
            float hh = size.y * 0.5f;
            float hd = Mathf.Clamp(p.Thickness, ImagePlateParams.ThicknessMin, ImagePlateParams.ThicknessMax) * 0.5f;
            int sx = Mathf.Clamp(p.SegmentsX, ImagePlateParams.SegmentsMin, ImagePlateParams.SegmentsMax);
            int sy = Mathf.Clamp(p.SegmentsY, ImagePlateParams.SegmentsMin, ImagePlateParams.SegmentsMax);

            // ── 表面（+Z）。頂点の並びは CubeMeshGenerator の前面と同じ。
            int frontVertStart = md.VertexCount;
            int frontFaceStart = md.FaceCount;
            CubeMeshGenerator.AddQuadFace(md,
                new Vector3(-hw, -hh, hd), new Vector3( hw, -hh, hd),
                new Vector3( hw,  hh, hd), new Vector3(-hw,  hh, hd),
                Vector3.forward, sx, sy);

            // UV を平面（XY・表向き）の規約へ直す。u は x と逆向き、v は y と同じ向き。
            for (int i = frontVertStart; i < md.VertexCount; i++)
            {
                var v = md.Vertices[i];
                float u  = 1f - (v.Position.x + hw) / (2f * hw);
                float vv = (v.Position.y + hh) / (2f * hh);
                v.UVs[0] = new Vector2(u, vv);
            }
            for (int f = frontFaceStart; f < md.FaceCount; f++)
                md.Faces[f].MaterialIndex = FrontFaceTag;

            // ── 裏面（-Z）。CubeMeshGenerator の背面と同じ並び。
            CubeMeshGenerator.AddQuadFace(md,
                new Vector3( hw, -hh, -hd), new Vector3(-hw, -hh, -hd),
                new Vector3(-hw,  hh, -hd), new Vector3( hw,  hh, -hd),
                Vector3.back, sx, sy);

            // ── 側面。表裏の分割に合わせて割る（厚み方向は 1）。
            // 右（+X）
            CubeMeshGenerator.AddQuadFace(md,
                new Vector3( hw, -hh,  hd), new Vector3( hw, -hh, -hd),
                new Vector3( hw,  hh, -hd), new Vector3( hw,  hh,  hd),
                Vector3.right, 1, sy);
            // 左（-X）
            CubeMeshGenerator.AddQuadFace(md,
                new Vector3(-hw, -hh, -hd), new Vector3(-hw, -hh,  hd),
                new Vector3(-hw,  hh,  hd), new Vector3(-hw,  hh, -hd),
                Vector3.left, 1, sy);
            // 上（+Y）
            CubeMeshGenerator.AddQuadFace(md,
                new Vector3(-hw,  hh,  hd), new Vector3( hw,  hh,  hd),
                new Vector3( hw,  hh, -hd), new Vector3(-hw,  hh, -hd),
                Vector3.up, sx, 1);
            // 下（-Y）
            CubeMeshGenerator.AddQuadFace(md,
                new Vector3(-hw, -hh, -hd), new Vector3( hw, -hh, -hd),
                new Vector3( hw, -hh,  hd), new Vector3(-hw, -hh,  hd),
                Vector3.down, sx, 1);

            PrimitiveMeshPostProcess.ApplyPivotOffset(md, p.Pivot);
            return md;
        }
    }
}
