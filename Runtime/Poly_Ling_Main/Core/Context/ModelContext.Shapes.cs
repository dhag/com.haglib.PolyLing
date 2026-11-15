// ModelContext.Shapes.cs
// ModelContext：形（名前付きの頂点位置）の一覧と、今の形。
// Runtime/Poly_Ling_Main/Core/Context/ に配置
//
// 中身の決まりは Core/Data/ShapeData.cs の冒頭注記。切り替えは Ops/ShapeOps.cs。

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Context
{
    public partial class ModelContext
    {
        /// <summary>形の一覧。基本の形（番号 0）は載せない。</summary>
        public List<ShapeDef> Shapes { get; set; } = new List<ShapeDef>();

        /// <summary>今の形の番号。0 = 基本の形。Vertex.Position はこの形の位置。</summary>
        public int CurrentShapeId { get; set; } = 0;

        /// <summary>
        /// 直前の形の番号（入れ替え用）。作業中だけの値で、保存しない。
        /// </summary>
        public int PreviousShapeId { get; set; } = 0;

        /// <summary>
        /// 基本の形の読み込み元（MQO。形の読み込み元は ShapeDef.Source）。読んでいなければ null。
        /// shapes.csv の source 行（番号 0）・ModelDTO.baseShapeSource・PLRM 版 15 で保存する。
        /// </summary>
        public ShapeSource BaseShapeSource { get; set; }

        /// <summary>形 id（0 は基本の形）の読み込み元。無ければ null。</summary>
        public ShapeSource GetShapeSource(int id)
            => id == 0 ? BaseShapeSource : FindShapeById(id)?.Source;

        /// <summary>形の数（基本の形を除く）。</summary>
        public int ShapeCount => Shapes?.Count ?? 0;

        /// <summary>番号で形を探す。0（基本の形）と、無い番号は null。</summary>
        public ShapeDef FindShapeById(int id)
        {
            if (id == 0 || Shapes == null) return null;
            for (int i = 0; i < Shapes.Count; i++)
                if (Shapes[i] != null && Shapes[i].Id == id) return Shapes[i];
            return null;
        }

        /// <summary>名前で形を探す。無ければ null。</summary>
        public ShapeDef FindShapeByName(string name)
        {
            if (string.IsNullOrEmpty(name) || Shapes == null) return null;
            for (int i = 0; i < Shapes.Count; i++)
                if (Shapes[i] != null && Shapes[i].Name == name) return Shapes[i];
            return null;
        }

        /// <summary>まだ使っていない形の番号（一覧の最大値＋1）。</summary>
        public int NextShapeId()
        {
            int max = 0;
            if (Shapes != null)
                for (int i = 0; i < Shapes.Count; i++)
                    if (Shapes[i] != null && Shapes[i].Id > max) max = Shapes[i].Id;
            return max + 1;
        }
    }
}
