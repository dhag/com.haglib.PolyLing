// ShapeData.cs
// 形（名前付きの頂点位置）の定義と、頂点が持つ形ごとの位置。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【形とは】（PolyLing_モーフ整理計画.md 3 章）
//   形 = 名前の付いた頂点位置の組。番号 0 は「基本の形」で、一覧（ModelContext.Shapes）には載せない。
//   モデルは「今の形」（ModelContext.CurrentShapeId）を 1 つ持ち、Vertex.Position は今の形の位置。
//   今の形でない形の位置は、基本の形と違う頂点だけが Vertex.Shapes に持つ。
//
// 【頂点が持つ中身の決まり】（ShapeOps.SwitchMesh が保つ）
//   今の形が基本（0）のとき … 番号 0 の控えを持つ頂点は無い。
//   今の形が s（0 以外）のとき … 切り替えた時点の全頂点が番号 0 の控え（基本の位置）を持つ。
//                                番号 s の控えを持つ頂点は無い（s の位置は Position にある）。
//   番号 0 の控えを持たない頂点（今の形で足した頂点）は、どの形でも Position の位置にいる。
//
// 【UV の形】（ShapeDef.Kind == ShapeKind.UV。計画書 3.3「UV の形は UV スロット 0 に同じ規則」）
//   位置の代わりに UV スロット 0（Vertex.UVs[0]）を形ごとに持つ。控えは Vertex.UVShapes。
//   決まりは位置と同じ（今の形の UV は UVs[0]、今の形が 0 以外の間は UV を持つ全頂点が番号 0 の UV の控えを持つ）。
//   UV を 1 つも持たない頂点は UV の控えを持たない。
//   位置の形は UV の控えを、UV の形は位置の控えを作らない（切り替えは両方を同じ手順で動かすので、どちらを持っても崩れない）。
//   UV の形は PMX の UV モーフとして書き出す。ブレンドシェイプ（VRM・glTF・FBX・プレファブ）には出さない。

using System;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>頂点が持つ、形 1 つぶんの位置。</summary>
    [Serializable]
    public struct ShapePosition
    {
        /// <summary>形の番号。0 は基本の形。</summary>
        public int ShapeId;

        /// <summary>その形での頂点位置（Vertex.Position と同じローカル空間）。</summary>
        public Vector3 Position;

        public ShapePosition(int shapeId, Vector3 position)
        {
            ShapeId = shapeId;
            Position = position;
        }
    }

    /// <summary>頂点が持つ、形 1 つぶんの UV（UV スロット 0）。</summary>
    [Serializable]
    public struct ShapeUV
    {
        /// <summary>形の番号。0 は基本の形。</summary>
        public int ShapeId;

        /// <summary>その形での UV スロット 0 の値。</summary>
        public Vector2 UV;

        public ShapeUV(int shapeId, Vector2 uv)
        {
            ShapeId = shapeId;
            UV = uv;
        }
    }

    /// <summary>形の種類。</summary>
    public enum ShapeKind
    {
        /// <summary>頂点位置の形（PMX の頂点モーフ・ブレンドシェイプ）。</summary>
        Position = 0,
        /// <summary>UV スロット 0 の形（PMX の UV モーフ）。</summary>
        UV = 1,
    }

    /// <summary>形の定義（モデルが一覧で持つ）。基本の形（番号 0）は載せない。</summary>
    [Serializable]
    public class ShapeDef
    {
        /// <summary>種類（位置／UV）。</summary>
        public ShapeKind Kind = ShapeKind.Position;

        /// <summary>UV の形か。</summary>
        public bool IsUV => Kind == ShapeKind.UV;

        /// <summary>形の番号。1 以上。モデルの中で一意。</summary>
        public int Id;

        /// <summary>名前。モデルの中で一意。</summary>
        public string Name = "";

        /// <summary>英語名。空でよい。</summary>
        public string NameEnglish = "";

        /// <summary>PMX のモーフパネル（0 眉 / 1 目 / 2 口 / 3 その他）。</summary>
        public int Panel = 3;

        /// <summary>読み込み元の MQO（PolyLing_モーフ整理計画.md 3.8）。読んでいなければ null。</summary>
        public ShapeSource Source;

        public ShapeDef Clone()
        {
            return new ShapeDef { Kind = Kind, Id = Id, Name = Name, NameEnglish = NameEnglish, Panel = Panel, Source = Source?.Clone() };
        }
    }

    /// <summary>モデル側の描画オブジェクトと MQO のオブジェクトの組（名前で持つ）。</summary>
    [Serializable]
    public struct ShapeSourcePair
    {
        /// <summary>モデル側の描画オブジェクトの名前。</summary>
        public string Model;
        /// <summary>MQO 側のオブジェクトの名前。</summary>
        public string Mqo;

        public ShapeSourcePair(string model, string mqo) { Model = model ?? ""; Mqo = mqo ?? ""; }
    }

    /// <summary>
    /// 形の読み込み元（MQO ファイル・対応の組・倍率・反転）。
    /// 形ごとに ShapeDef.Source、基本の形は ModelContext.BaseShapeSource が持つ。
    /// 「再読み込み」はこれだけで同じ読み込みを繰り返す（ShapeMqoOps）。
    /// </summary>
    [Serializable]
    public class ShapeSource
    {
        /// <summary>MQO のパス（読み込んだときに渡されたままの文字列。作業フォルダからの相対でもよい）。</summary>
        public string Path = "";
        /// <summary>MQO 座標に掛ける倍率。</summary>
        public float Scale = 0.01f;
        /// <summary>X を反転する。</summary>
        public bool FlipX = true;
        /// <summary>Z を反転する。</summary>
        public bool FlipZ = false;
        /// <summary>前回読んだ組。</summary>
        public System.Collections.Generic.List<ShapeSourcePair> Pairs = new System.Collections.Generic.List<ShapeSourcePair>();

        public ShapeSource Clone()
        {
            return new ShapeSource
            {
                Path = Path, Scale = Scale, FlipX = FlipX, FlipZ = FlipZ,
                Pairs = Pairs != null ? new System.Collections.Generic.List<ShapeSourcePair>(Pairs)
                                      : new System.Collections.Generic.List<ShapeSourcePair>(),
            };
        }
    }
}
