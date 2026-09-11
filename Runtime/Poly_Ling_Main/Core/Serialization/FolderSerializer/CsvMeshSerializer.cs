// Assets/Editor/Poly_Ling/Serialization/FolderSerializer/CsvMeshSerializer.cs
// メッシュ/ボーン/モーフのCSVファイル読み書き
// 1ファイルに複数メッシュを "---" 区切りで格納
//
// 【分割先】このファイルから次へ分けてある。
//   CsvMeshEntry.cs                    CSV メッシュのエントリ（CsvMeshSerializer から分離）。
//   CsvMeshSerializer.Read.cs          CSV メッシュ入出力：1 メッシュ分の読み込みと、BoneTransform・BindPose・剛体／JOINT・SpringBone・BonePose。
//   CsvMeshSerializer.ReadElements.cs  CSV メッシュ入出力：頂点・面・選択セット・除外セットの読み込み、CSV ユーティリティ、名前ベース参照の解決。
//   CsvMeshSerializer.Write.cs         CSV メッシュ入出力：書き出し（ヘッダ・ボーン・SpringBone・モーフ・選択セット・除外セット・頂点・面）。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;

namespace Poly_Ling.Serialization.FolderSerializer
{

    /// <summary>
    /// メッシュ/ボーン/モーフCSVの読み書き
    /// </summary>
    public static partial class CsvMeshSerializer
    {
        private const string Separator = "---";
        private const string VersionMesh = "#PolyLing_Mesh,version,1.0";
        private const string VersionBone = "#PolyLing_Bone,version,1.0";
        private const string VersionMorph = "#PolyLing_Morph,version,1.0";

        /// <summary>
        /// PMX 物理（剛体 / JOINT）用のヘッダ。
        /// 頂点ゼロのメタデータで、形状ファイル（mesh）とは性質が違うため分けてある。
        /// </summary>
        private const string VersionPmxPhysics = "#PolyLing_PmxPhysics,version,1.0";

        // ================================================================
        // Write
        // ================================================================

        /// <summary>
        /// メッシュリストをCSVファイルに書き込み
        /// </summary>
        /// <param name="useNameBased">名前ベース参照モード</param>
        /// <param name="indexToName">MeshContextListインデックス→名前の辞書（名前ベース時必須）</param>
        public static void WriteFile(string path, List<CsvMeshEntry> entries, string fileType,
            bool useNameBased = false, Dictionary<int, string> indexToName = null)
        {
            if (entries == null || entries.Count == 0) return;

            var sb = new StringBuilder();

            // ヘッダ
            switch (fileType)
            {
                case "bone": sb.AppendLine(VersionBone); break;
                case "morph": sb.AppendLine(VersionMorph); break;
                case "pmx_physics": sb.AppendLine(VersionPmxPhysics); break;
                default: sb.AppendLine(VersionMesh); break;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var mc = entry.MeshContext;
                if (mc == null) continue;

                // === R2-a: DTO単一真実源化（書き出しをMeshDTO経由に）===
                // 保存データを必ず MeshDTO に通す：mc → MeshDTO → mc' とラウンドトリップしてから
                // 既存writerで書き出す。既存writerは不変のため出力フォーマットは保たれ、
                // MeshDTO往復が無損失（構造=R1.5 / population=R1.6）なので出力はバイト一致。
                // buildUnityMesh=false で保存時の不要なUnityメッシュ生成を回避する。
                var roundTripDTO = ModelSerializer.FromMeshContext(mc);
                if (roundTripDTO != null)
                {
                    var roundTripMc = ModelSerializer.ToMeshContext(roundTripDTO, false);
                    if (roundTripMc != null) mc = roundTripMc;
                }

                sb.AppendLine(Separator);
                WriteMeshHeader(sb, entry.GlobalIndex, mc, useNameBased, indexToName,
                    entry.MirrorPeerName, entry.MirrorPeerAxis);

                // ボーン固有データ
                if (mc.Type == MeshType.Bone)
                {
                    WriteBoneData(sb, mc, useNameBased, indexToName);
                }

                // SpringBone 付帯データ（規約4: CSV/JSON 対称）
                //
                // 【なぜ Bone 限定の中から出したか】
                //   揺れデータの付帯先はボーンに限らない（MeshObject.cs の
                //   SpringBone 節を参照）。読み込み側（sbCollider / sbJoint /
                //   sbChain の case）は元から種別を見ていないため、
                //   Bone の中で書いていると非ボーンのぶんだけ片道で消えていた。
                WriteSpringBoneData(sb, mc);

                // 一人称カメラでの扱い（VRM firstPerson）。
                // 既定 Auto は「指定なし」と同義なので行を書かない。
                // 書かないことで旧ファイルとの往復も対称になる。
                var vrmFp = mc.MeshObject?.VrmFirstPerson ?? VrmFirstPersonType.Auto;
                if (vrmFp != VrmFirstPersonType.Auto)
                    sb.AppendLine($"vrmFirstPerson,{(int)vrmFp}");

                // ノード制約（VRMC_node_constraint）。null は行を書かない。
                // vrmConstraint,kind,sourceName,weight,rollAxis,aimAxis
                var vrmCon = mc.MeshObject?.VrmConstraint;
                if (vrmCon != null)
                    sb.AppendLine(
                        $"vrmConstraint,{(int)vrmCon.Kind},{EscapeCsv(vrmCon.SourceName ?? "")}," +
                        $"{F(vrmCon.Weight)},{(int)vrmCon.RollAxis},{(int)vrmCon.AimAxis}");

                // モーフ固有データ
                if (mc.Type == MeshType.Morph)
                {
                    WriteMorphData(sb, mc, useNameBased, indexToName);
                }

                // Humanoid 割当・可動域（Bone 以外）
                //   Bone の場合は WriteBoneData 内で出力済み（従来の行位置を維持するため）。
                if (mc.Type != MeshType.Bone)
                {
                    WriteHumanoidPerBone(sb, mc);
                }

                // 選択セット
                WriteSelectionSets(sb, mc);

                // 法線再計算 除外セット
                WriteNormalExcludeSets(sb, mc);

                // 剛体 / JOINT（Type=RigidBody/RigidBodyJoint の頂点ゼロ・メタデータ）
                WriteRigidJointData(sb, mc);

                // 頂点
                if (mc.MeshObject != null)
                {
                    foreach (var v in mc.MeshObject.Vertices)
                    {
                        if (useNameBased)
                            WriteVertexNameBased(sb, v, indexToName);
                        else
                            WriteVertex(sb, v);
                    }

                    // 面
                    foreach (var f in mc.MeshObject.Faces)
                    {
                        WriteFace(sb, f);
                    }
                }
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        // ================================================================
        // Read
        // ================================================================

        /// <summary>
        /// CSVファイルからメッシュリストを読み込み
        /// </summary>
        public static List<CsvMeshEntry> ReadFile(string path)
        {
            var result = new List<CsvMeshEntry>();
            if (!File.Exists(path)) return result;

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            int i = 0;

            // ヘッダ行スキップ
            if (i < lines.Length && lines[i].StartsWith("#PolyLing_"))
                i++;

            while (i < lines.Length)
            {
                // "---" を探す
                if (lines[i].Trim() == Separator)
                {
                    i++;
                    var entry = ReadOneMesh(lines, ref i);
                    if (entry != null)
                        result.Add(entry);
                }
                else
                {
                    i++;
                }
            }

            return result;
        }
    }
}
