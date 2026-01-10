// Assets/Editor/MeshFactory/MQO/Core/MQODocument.cs
// MQOファイルの中間データ構造
// パース結果を保持し、インポート/エクスポート両方で使用

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshFactory.MQO
{
    /// <summary>
    /// MQOドキュメント全体を表す中間データ構造
    /// </summary>
    public class MQODocument
    {
        /// <summary>フォーマットバージョン</summary>
        public decimal Version { get; set; } = 1.0m;

        /// <summary>ファイルパス（読み込み元）</summary>
        public string FilePath { get; set; }

        /// <summary>ファイル名</summary>
        public string FileName { get; set; }

        /// <summary>シーン情報</summary>
        public MQOScene Scene { get; set; }

        /// <summary>マテリアルリスト</summary>
        public List<MQOMaterial> Materials { get; } = new List<MQOMaterial>();

        /// <summary>オブジェクトリスト</summary>
        public List<MQOObject> Objects { get; } = new List<MQOObject>();

        /// <summary>下絵リスト</summary>
        public List<MQOBackImage> BackImages { get; } = new List<MQOBackImage>();
    }

    /// <summary>
    /// シーン情報
    /// </summary>
    public class MQOScene
    {
        /// <summary>属性リスト（pos, lookat, head, pich, ortho, zoom2, amb等）</summary>
        public List<MQOAttribute> Attributes { get; } = new List<MQOAttribute>();

        /// <summary>属性を名前で取得</summary>
        public MQOAttribute GetAttribute(string name)
        {
            return Attributes.Find(a => a.Name == name);
        }

        /// <summary>属性値を取得（float配列）</summary>
        public float[] GetValues(string name)
        {
            return GetAttribute(name)?.Values;
        }
    }

    /// <summary>
    /// 汎用属性（名前 + 数値配列）
    /// </summary>
    public class MQOAttribute
    {
        /// <summary>属性名</summary>
        public string Name { get; set; }

        /// <summary>値配列</summary>
        public float[] Values { get; set; }

        public MQOAttribute() { }

        public MQOAttribute(string name, params float[] values)
        {
            Name = name;
            Values = values;
        }

        /// <summary>単一値取得</summary>
        public float GetFloat(int index = 0)
        {
            if (Values == null || index >= Values.Length) return 0f;
            return Values[index];
        }

        /// <summary>整数値取得</summary>
        public int GetInt(int index = 0)
        {
            return (int)GetFloat(index);
        }

        /// <summary>Vector3取得</summary>
        public Vector3 GetVector3()
        {
            if (Values == null || Values.Length < 3) return Vector3.zero;
            return new Vector3(Values[0], Values[1], Values[2]);
        }
    }

    /// <summary>
    /// マテリアル
    /// </summary>
    public class MQOMaterial
    {
        /// <summary>マテリアル名</summary>
        public string Name { get; set; }

        /// <summary>色（RGBA）</summary>
        public Color Color { get; set; } = Color.white;

        /// <summary>拡散反射（diffuse）</summary>
        public float Diffuse { get; set; } = 0.8f;

        /// <summary>環境光（ambient）</summary>
        public float Ambient { get; set; } = 0.6f;

        /// <summary>自己発光（emissive）</summary>
        public float Emissive { get; set; } = 0f;

        /// <summary>鏡面反射（specular）</summary>
        public float Specular { get; set; } = 0f;

        /// <summary>鏡面反射強度（power）</summary>
        public float Power { get; set; } = 5f;

        /// <summary>テクスチャパス</summary>
        public string TexturePath { get; set; }

        /// <summary>アルファマップパス</summary>
        public string AlphaMapPath { get; set; }

        /// <summary>バンプマップパス</summary>
        public string BumpMapPath { get; set; }
    }

    /// <summary>
    /// オブジェクト（メッシュ単位）
    /// </summary>
    public class MQOObject
    {
        /// <summary>オブジェクト名</summary>
        public string Name { get; set; }

        /// <summary>頂点リスト</summary>
        public List<MQOVertex> Vertices { get; } = new List<MQOVertex>();

        /// <summary>面リスト</summary>
        public List<MQOFace> Faces { get; } = new List<MQOFace>();

        /// <summary>属性リスト</summary>
        public List<MQOAttribute> Attributes { get; } = new List<MQOAttribute>();

        /// <summary>頂点属性（vertexattr）の生テキスト</summary>
        public string VertexAttrRaw { get; set; }

        // === 属性アクセサ ===

        /// <summary>表示状態（visible 15=表示, 0=非表示）</summary>
        public bool IsVisible
        {
            get
            {
                var attr = Attributes.Find(a => a.Name == "visible");
                if (attr == null) return true; // デフォルト表示
                return attr.GetInt() == 15;
            }
        }

        /// <summary>ロック状態</summary>
        public bool IsLocked
        {
            get
            {
                var attr = Attributes.Find(a => a.Name == "locking");
                if (attr == null) return false;
                return attr.GetInt() == 1;
            }
        }

        /// <summary>ミラーモード（0=なし, 1=左右, 2=上下...）</summary>
        public int MirrorMode
        {
            get
            {
                var attr = Attributes.Find(a => a.Name == "mirror");
                if (attr == null) return 0;
                return attr.GetInt();
            }
        }

        /// <summary>ミラー軸（1=X, 2=Y, 4=Z）</summary>
        public int MirrorAxis
        {
            get
            {
                var attr = Attributes.Find(a => a.Name == "mirror_axis");
                if (attr == null) return 1; // デフォルトX軸
                return attr.GetInt();
            }
        }

        /// <summary>ミラー距離</summary>
        public float MirrorDistance
        {
            get
            {
                var attr = Attributes.Find(a => a.Name == "mirror_dis");
                if (attr == null) return 0f;
                return attr.GetFloat();
            }
        }

        /// <summary>折りたたみ状態</summary>
        public bool IsFolding
        {
            get
            {
                var attr = Attributes.Find(a => a.Name == "folding");
                if (attr == null) return false;
                return attr.GetInt() == 1;
            }
        }

        /// <summary>階層深度</summary>
        public int Depth
        {
            get
            {
                var attr = Attributes.Find(a => a.Name == "depth");
                if (attr == null) return 0;
                return attr.GetInt();
            }
        }

        /// <summary>スケール</summary>
        public Vector3 Scale
        {
            get
            {
                var attr = Attributes.Find(a => a.Name == "scale");
                if (attr == null) return Vector3.one;
                return attr.GetVector3();
            }
        }

        /// <summary>回転</summary>
        public Vector3 Rotation
        {
            get
            {
                var attr = Attributes.Find(a => a.Name == "rotation");
                if (attr == null) return Vector3.zero;
                return attr.GetVector3();
            }
        }

        /// <summary>移動</summary>
        public Vector3 Translation
        {
            get
            {
                var attr = Attributes.Find(a => a.Name == "translation");
                if (attr == null) return Vector3.zero;
                return attr.GetVector3();
            }
        }
    }

    /// <summary>
    /// 頂点
    /// </summary>
    public class MQOVertex
    {
        /// <summary>位置（MQO座標系）</summary>
        public Vector3 Position { get; set; }

        /// <summary>インデックス（パース時に設定）</summary>
        public int Index { get; set; }
    }

    /// <summary>
    /// 面
    /// </summary>
    public class MQOFace
    {
        /// <summary>頂点インデックスリスト</summary>
        public int[] VertexIndices { get; set; }

        /// <summary>マテリアルインデックス（-1 = 未設定）</summary>
        public int MaterialIndex { get; set; } = -1;

        /// <summary>UV座標リスト（頂点と同数）</summary>
        public Vector2[] UVs { get; set; }

        /// <summary>頂点カラーリスト（頂点と同数、COL属性）</summary>
        public uint[] VertexColors { get; set; }

        /// <summary>頂点数</summary>
        public int VertexCount => VertexIndices?.Length ?? 0;

        /// <summary>特殊面か（全頂点インデックスが同じ = メタデータ格納用）</summary>
        public bool IsSpecialFace
        {
            get
            {
                if (VertexIndices == null || VertexIndices.Length < 2) return false;
                int first = VertexIndices[0];
                for (int i = 1; i < VertexIndices.Length; i++)
                {
                    if (VertexIndices[i] != first) return false;
                }
                return true;
            }
        }
    }

    /// <summary>
    /// 下絵
    /// </summary>
    public class MQOBackImage
    {
        /// <summary>パート名（FRONT, BACK, LEFT, RIGHT, TOP, BOTTOM）</summary>
        public string Part { get; set; }

        /// <summary>画像パス</summary>
        public string Path { get; set; }

        /// <summary>X位置</summary>
        public float X { get; set; }

        /// <summary>Y位置</summary>
        public float Y { get; set; }

        /// <summary>幅</summary>
        public float Width { get; set; }

        /// <summary>高さ</summary>
        public float Height { get; set; }
    }
}
