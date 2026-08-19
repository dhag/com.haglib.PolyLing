// ObjDocument.cs
// Wavefront OBJ / MTL のドキュメント表現（読み書き共通）。
// Runtime/Poly_Ling_Main/OBJ/Common/ に配置
//
// 【OBJ の構造】
//   頂点（v）・UV（vt）・法線（vn）はファイル全体で通し番号を持つ。
//   面（f）は「頂点／UV／法線」の 3 つを独立に参照するため、
//   1 頂点が面ごとに違う UV・法線を持てる（PolyLing のスロットと同じ考え方）。
//   オブジェクト（o）とグループ（g）は独立した区切りで、どちらも省略できる。
//
// 【この表現の方針】
//   グループを入れ子で持たず、面の側に「所属オブジェクト名・グループ名・
//   マテリアル・スムージンググループ」を持たせた平坦なリストにする。
//   OBJ の o / g / usemtl / s はいずれも「以降の面に効く状態」であり、
//   入れ子構造ではないため、状態を面へ畳んだ方が仕様に忠実になる。
//   どの単位でメッシュへ分けるかは読み込み側（ObjImporter）が決める。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.OBJ
{
    // ================================================================
    // 面コーナー
    // ================================================================

    /// <summary>
    /// 面の 1 コーナーが参照する索引（すべて 0 始まりに解決済み。-1 = 参照なし）。
    /// OBJ の f は v / v/vt / v//vn / v/vt/vn の 4 形式があり、
    /// 負の索引（末尾からの相対参照）はパース時に解決する。
    /// </summary>
    public struct ObjCorner
    {
        /// <summary>頂点索引（ObjDocument.Positions）。</summary>
        public int V;

        /// <summary>UV索引（ObjDocument.UVs）。-1 = なし。</summary>
        public int VT;

        /// <summary>法線索引（ObjDocument.Normals）。-1 = なし。</summary>
        public int VN;

        public ObjCorner(int v, int vt, int vn)
        {
            V  = v;
            VT = vt;
            VN = vn;
        }
    }

    // ================================================================
    // 面
    // ================================================================

    /// <summary>
    /// 面（f）または折れ線（l）。
    /// 所属は面の側が持つ（o / g / usemtl / s の状態を畳んだもの）。
    /// </summary>
    public class ObjFace
    {
        /// <summary>コーナー列。f は 3 個以上、l は 2 個以上。</summary>
        public List<ObjCorner> Corners = new List<ObjCorner>();

        /// <summary>所属オブジェクト名（o）。無指定なら null。</summary>
        public string ObjectName;

        /// <summary>所属グループ名（g）。無指定なら null。</summary>
        public string GroupName;

        /// <summary>マテリアル索引（ObjDocument.Materials）。-1 = 無指定。</summary>
        public int MaterialIndex = -1;

        /// <summary>スムージンググループ番号（s）。0 = off。</summary>
        public int SmoothingGroup = 0;

        /// <summary>折れ線（l）か。true なら面ではない。</summary>
        public bool IsLine = false;

        public int CornerCount => Corners.Count;
    }

    // ================================================================
    // マテリアル
    // ================================================================

    /// <summary>
    /// MTL の 1 マテリアル。OBJ 側で使う値だけを保持する。
    /// </summary>
    public class ObjMaterial
    {
        /// <summary>マテリアル名（newmtl）。</summary>
        public string Name = "default";

        /// <summary>拡散色（Kd）。</summary>
        public Color Diffuse = Color.white;

        /// <summary>環境色（Ka）。</summary>
        public Color Ambient = new Color(0.2f, 0.2f, 0.2f, 1f);

        /// <summary>鏡面色（Ks）。</summary>
        public Color Specular = Color.black;

        /// <summary>鏡面指数（Ns）。0-1000。</summary>
        public float SpecularExponent = 0f;

        /// <summary>不透明度（d）。1 = 不透明。Tr は 1-Tr として取り込む。</summary>
        public float Alpha = 1f;

        /// <summary>照明モデル（illum）。</summary>
        public int IlluminationModel = 2;

        /// <summary>拡散マップ（map_Kd）。MTL に書かれたままの相対パス。</summary>
        public string DiffuseMapPath;

        /// <summary>アルファマップ（map_d）。</summary>
        public string AlphaMapPath;

        /// <summary>バンプマップ（map_Bump / bump）。</summary>
        public string BumpMapPath;
    }

    // ================================================================
    // ドキュメント
    // ================================================================

    /// <summary>
    /// OBJ ファイル 1 個分。座標は OBJ のまま（右手系・Y上）で保持し、
    /// Unity 座標への変換は Importer / Exporter 側で行う。
    /// </summary>
    public class ObjDocument
    {
        /// <summary>元ファイル名（拡張子なし）。オブジェクト名の既定値に使う。</summary>
        public string FileName;

        /// <summary>頂点位置（v）。</summary>
        public List<Vector3> Positions = new List<Vector3>();

        /// <summary>UV（vt）。</summary>
        public List<Vector2> UVs = new List<Vector2>();

        /// <summary>法線（vn）。</summary>
        public List<Vector3> Normals = new List<Vector3>();

        /// <summary>面と折れ線（出現順）。</summary>
        public List<ObjFace> Faces = new List<ObjFace>();

        /// <summary>マテリアル（MTL から読み込んだもの）。</summary>
        public List<ObjMaterial> Materials = new List<ObjMaterial>();

        /// <summary>参照している MTL ファイル名（mtllib）。複数可。</summary>
        public List<string> MtlLibs = new List<string>();

        /// <summary>o が 1 つでも書かれていたか。</summary>
        public bool HasObjectNames;

        /// <summary>g が 1 つでも書かれていたか（default 以外）。</summary>
        public bool HasGroupNames;

        /// <summary>マテリアル名から索引を引く。見つからなければ -1。</summary>
        public int IndexOfMaterial(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;

            for (int i = 0; i < Materials.Count; i++)
            {
                if (string.Equals(Materials[i]?.Name, name, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }
    }
}
