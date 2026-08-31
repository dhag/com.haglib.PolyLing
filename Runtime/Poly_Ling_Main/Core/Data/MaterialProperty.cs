// Assets/Editor/Poly_Ling/Materials/MaterialProperty.cs
// ============================================================
// シェーダー固有マテリアルプロパティ（純POCOデータ契約）
// ============================================================
//
// 【役割】
//   MaterialData の「共通コア」（BaseColor / Metallic / Surface 等の型付きフィールド）に
//   収まらない、シェーダー固有のプロパティを名前・型・値で保持する汎用コンテナ。
//   URP Lit の細目（_SpecColor / _Parallax / Detail 系 / ClearCoat 系 等）や、
//   MToon・独自シェーダのプロパティを、MaterialData へフィールドを増やさずに往復させる。
//
// 【共通コアとの二重管理禁止】
//   共通コアで型付きに保持しているプロパティ名（_BaseColor / _Metallic / _Cull 等）は
//   本リストに入れない。抽出時の除外は MaterialDataConverter が一元管理する。
//   二重に持つと「どちらが正か」が失われ、往復で値が壊れる。
//
// 【値の格納規約】
//   Float / Range / Int … X のみ使用
//   Color / Vector      … X,Y,Z,W を使用
//   Texture             … TexturePath ＋ X,Y=tiling / Z,W=offset（テクスチャST）
//
// 【依存】
//   #if UNITY_EDITOR を含まない純データ。
//
// ============================================================

using System;

namespace Poly_Ling.Materials
{
    /// <summary>
    /// シェーダープロパティの型。
    /// 値は UnityEngine.Rendering.ShaderPropertyType と対応するが、
    /// 永続化する値のため独自に番号を固定する（追加は末尾のみ）。
    /// </summary>
    public enum MaterialPropertyKind
    {
        /// <summary>単一float</summary>
        Float = 0,
        /// <summary>範囲付きfloat</summary>
        Range = 1,
        /// <summary>整数</summary>
        Int = 2,
        /// <summary>色（XYZW = RGBA）</summary>
        Color = 3,
        /// <summary>ベクトル（XYZW）</summary>
        Vector = 4,
        /// <summary>テクスチャ（TexturePath ＋ XY=tiling / ZW=offset）</summary>
        Texture = 5
    }

    /// <summary>
    /// シェーダー固有マテリアルプロパティ（純POCO）。
    /// MaterialData.ShaderProperties の要素として保持する。
    /// </summary>
    [Serializable]
    public class MaterialProperty
    {
        /// <summary>シェーダープロパティ名（"_SpecColor" 等。先頭のアンダースコアを含む）。</summary>
        public string Name = "";

        /// <summary>値の型。</summary>
        public MaterialPropertyKind Kind = MaterialPropertyKind.Float;

        /// <summary>値0（Float/Range/Int の値、Color の R、Vector の X、Texture の tilingX）。</summary>
        public float X;

        /// <summary>値1（Color の G、Vector の Y、Texture の tilingY）。</summary>
        public float Y;

        /// <summary>値2（Color の B、Vector の Z、Texture の offsetX）。</summary>
        public float Z;

        /// <summary>値3（Color の A、Vector の W、Texture の offsetY）。</summary>
        public float W;

        /// <summary>テクスチャのアセットパス（Kind == Texture のみ。null/空=テクスチャ無し）。</summary>
        public string TexturePath;

        public MaterialProperty() { }

        public MaterialProperty(string name, MaterialPropertyKind kind)
        {
            Name = name ?? "";
            Kind = kind;
        }

        /// <summary>ディープコピー。</summary>
        public MaterialProperty Clone()
        {
            return new MaterialProperty
            {
                Name = this.Name,
                Kind = this.Kind,
                X = this.X,
                Y = this.Y,
                Z = this.Z,
                W = this.W,
                TexturePath = this.TexturePath
            };
        }

        public override string ToString()
        {
            return Kind == MaterialPropertyKind.Texture
                ? $"{Name}({Kind}): {TexturePath}"
                : $"{Name}({Kind}): {X},{Y},{Z},{W}";
        }
    }
}
