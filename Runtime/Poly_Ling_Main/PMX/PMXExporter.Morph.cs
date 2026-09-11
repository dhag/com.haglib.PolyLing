// PMXExporter.Morph.cs
// PMX エクスポート：モーフと剛体・JOINT のエクスポート。
// Runtime/Poly_Ling_Main/PMX/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.Materials;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.PMX
{
    public static partial class PMXExporter
    {
        // ================================================================
        // モーフ エクスポート
        //
        // 【方針】
        //   PolyLing が持つのは頂点モーフ・UVモーフ・グループモーフの 3 種類。
        //   ボーンモーフ・材質モーフ・フリップ・インパルスは取り込んでいないので、
        //   元の PMX（SourceDocument）からそのまま写す。
        //
        // 【並び順】
        //   表示枠はモーフを番号で指す。元の PMX があるときは元の並び順を守り、
        //   同名のモーフだけ作り直したもので差し替える。これで表示枠の番号が
        //   ずれない。元の PMX が無いときは MorphExpressions の順で出す。
        // ================================================================

        private static void ConvertMorphs(
            ModelContext model,
            PMXDocument document,
            Dictionary<string, Dictionary<(int vIdx, int uvIdx), int>> vertexMaps,
            PMXExportSettings settings)
        {
            var rebuilt = new Dictionary<string, PMXMorph>();
            var order   = new List<string>();

            var expressions = model.MorphExpressions;
            if (expressions != null)
            {
                foreach (var expr in expressions)
                {
                    if (expr == null || string.IsNullOrEmpty(expr.Name)) continue;
                    if (rebuilt.ContainsKey(expr.Name)) continue;

                    PMXMorph morph = expr.Type == MorphType.Group
                        ? BuildGroupMorph(expr)
                        : BuildShapeMorph(expr, model, vertexMaps, settings);

                    if (morph == null) continue;
                    rebuilt[expr.Name] = morph;
                    order.Add(expr.Name);
                }
            }

            var emitted = new HashSet<string>();

            if (model.SourceDocument is PMXDocument sourcePmx)
            {
                foreach (var srcMorph in sourcePmx.Morphs)
                {
                    if (srcMorph == null) continue;

                    // 書き出し側が作るメタモーフは元のものを使わない
                    if (srcMorph.Name != null && srcMorph.Name.StartsWith("__PLM_")) continue;

                    if (srcMorph.Name != null && rebuilt.TryGetValue(srcMorph.Name, out var made))
                    {
                        document.Morphs.Add(made);
                        emitted.Add(srcMorph.Name);
                    }
                    else
                    {
                        // PolyLing が扱わない種類（ボーン・材質・フリップ・インパルス）
                        document.Morphs.Add(srcMorph);
                        if (srcMorph.Name != null) emitted.Add(srcMorph.Name);
                    }
                }
            }

            // 元の PMX に無かったモーフ（PolyLing で足したもの）を後ろに足す
            foreach (var name in order)
            {
                if (emitted.Contains(name)) continue;
                document.Morphs.Add(rebuilt[name]);
                emitted.Add(name);
            }

            ResolveMorphReferences(document);

            Debug.Log($"[PMXExporter] Converted {document.Morphs.Count} morphs");
        }

        /// <summary>
        /// モーフが持つ他要素への参照を名前から番号へ解決する。
        /// PMXWriter は番号をそのまま書くので、ここで解決しないと -1 のまま出る
        /// （JOINT の接続剛体で同じ失敗をしている）。
        /// </summary>
        private static void ResolveMorphReferences(PMXDocument document)
        {
            var morphIndex = new Dictionary<string, int>();
            for (int i = 0; i < document.Morphs.Count; i++)
            {
                string n = document.Morphs[i]?.Name;
                if (!string.IsNullOrEmpty(n) && !morphIndex.ContainsKey(n)) morphIndex[n] = i;
            }

            var materialIndex = new Dictionary<string, int>();
            for (int i = 0; i < document.Materials.Count; i++)
            {
                string n = document.Materials[i]?.Name;
                if (!string.IsNullOrEmpty(n) && !materialIndex.ContainsKey(n)) materialIndex[n] = i;
            }

            var boneIndex = new Dictionary<string, int>();
            for (int i = 0; i < document.Bones.Count; i++)
            {
                string n = document.Bones[i]?.Name;
                if (!string.IsNullOrEmpty(n) && !boneIndex.ContainsKey(n)) boneIndex[n] = i;
            }

            foreach (var morph in document.Morphs)
            {
                if (morph?.Offsets == null) continue;

                foreach (var offset in morph.Offsets)
                {
                    switch (offset)
                    {
                        case PMXGroupMorphOffset g:
                            if (!string.IsNullOrEmpty(g.MorphName) &&
                                morphIndex.TryGetValue(g.MorphName, out int gi))
                                g.MorphIndex = gi;
                            break;

                        case PMXMaterialMorphOffset m:
                            if (!string.IsNullOrEmpty(m.MaterialName) &&
                                materialIndex.TryGetValue(m.MaterialName, out int mi))
                                m.MaterialIndex = mi;
                            break;

                        case PMXBoneMorphOffset b:
                            if (!string.IsNullOrEmpty(b.BoneName) &&
                                boneIndex.TryGetValue(b.BoneName, out int bi))
                                b.BoneIndex = bi;
                            break;
                    }
                }
            }
        }

        /// <summary>グループモーフを作る。子は名前で参照する。</summary>
        private static PMXMorph BuildGroupMorph(MorphExpression expr)
        {
            if (expr.GroupChildren == null || expr.GroupChildren.Count == 0) return null;

            var morph = new PMXMorph
            {
                Name        = expr.Name,
                NameEnglish = expr.NameEnglish ?? "",
                Panel       = expr.Panel,
                MorphType   = 0
            };

            foreach (var child in expr.GroupChildren)
            {
                if (string.IsNullOrEmpty(child.Name)) continue;
                morph.Offsets.Add(new PMXGroupMorphOffset
                {
                    Type       = 0,
                    MorphIndex = -1,          // 名前主・番号従。書き出し直前に解決する
                    MorphName  = child.Name,
                    Weight     = child.Weight
                });
            }

            return morph.Offsets.Count > 0 ? morph : null;
        }

        /// <summary>頂点モーフ / UVモーフを作る。</summary>
        private static PMXMorph BuildShapeMorph(
            MorphExpression expr,
            ModelContext model,
            Dictionary<string, Dictionary<(int vIdx, int uvIdx), int>> vertexMaps,
            PMXExportSettings settings)
        {
            bool isUV = expr.IsUVMorph;

            var morph = new PMXMorph
            {
                Name        = expr.Name,
                NameEnglish = expr.NameEnglish ?? "",
                Panel       = expr.Panel,
                MorphType   = isUV ? 3 : 1
            };

            foreach (var entry in expr.MeshEntries)
            {
                if (entry.MeshIndex < 0 || entry.MeshIndex >= model.MeshContextCount) continue;

                var morphCtx = model.GetMeshContext(entry.MeshIndex);
                var baseData = morphCtx?.MorphBaseData;
                if (morphCtx?.MeshObject == null || baseData == null || !baseData.IsValid) continue;

                // どの描画オブジェクトの展開範囲に載せるかを名前で引く
                string baseName = !string.IsNullOrEmpty(baseData.BaseMeshName)
                    ? baseData.BaseMeshName
                    : FindBaseMeshName(morphCtx.Name, vertexMaps);

                if (baseName == null || !vertexMaps.TryGetValue(baseName, out var map)) continue;

                if (isUV)
                {
                    foreach (var (localIndex, offset) in baseData.GetSparseUVOffsets(morphCtx.MeshObject))
                    {
                        if (!map.TryGetValue((localIndex, 0), out int pmxIndex)) continue;

                        Vector2 uv = offset;
                        if (settings.FlipUV_V) uv.y = -uv.y;

                        morph.Offsets.Add(new PMXUVMorphOffset
                        {
                            Type        = 3,
                            VertexIndex = pmxIndex,
                            Offset      = new Vector4(uv.x, uv.y, 0f, 0f)
                        });
                    }
                }
                else
                {
                    foreach (var (localIndex, offset) in baseData.GetSparseOffsets(morphCtx.MeshObject))
                    {
                        // 位置は UV スロット数ぶん複製されるので、同じ差分を全スロットへ配る。
                        // 差分はスケールと軸反転をかけて PMX 座標へ戻す。
                        Vector3 pmxOffset = AxisFlipOps.Position(settings.Flip, offset, settings.Scale);

                        int uvIdx = 0;
                        while (map.TryGetValue((localIndex, uvIdx), out int pmxIndex))
                        {
                            morph.Offsets.Add(new PMXVertexMorphOffset
                            {
                                Type        = 1,
                                VertexIndex = pmxIndex,
                                Offset      = pmxOffset
                            });
                            uvIdx++;
                        }
                    }
                }
            }

            return morph.Offsets.Count > 0 ? morph : null;
        }

        /// <summary>
        /// BaseMeshName が入っていない古いデータ向けの保険。
        /// モーフメッシュ名は「元の名前_モーフ名」で作られるため前方一致で探す。
        /// </summary>
        private static string FindBaseMeshName(
            string morphMeshName,
            Dictionary<string, Dictionary<(int vIdx, int uvIdx), int>> vertexMaps)
        {
            if (string.IsNullOrEmpty(morphMeshName)) return null;

            string best = null;
            foreach (var name in vertexMaps.Keys)
            {
                if (!morphMeshName.StartsWith(name)) continue;
                if (best == null || name.Length > best.Length) best = name;
            }
            return best;
        }

        // ================================================================
        // 剛体・JOINT エクスポート（段階③）
        // ================================================================
        //
        // 【方針】
        //   RigidBodyData/JointData を持つ MeshObject から PMXRigidBody/PMXJoint を
        //   再構築する。インポート（段階②）の逆変換であり、座標規約は対称：
        //     位置 = ConvertPosition（×Scale ＋ FlipZ）… import の逆（Scaleは逆数）
        //     回転 = ConvertEulerRotation（FlipZ共役は自己逆元のため import と同一処理）
        //     サイズ = ×Scale（Z反転なし）
        //     質量・減衰・反発・摩擦・Group・Mask・JointType・min/max・Spring = 生値
        //
        // 【参照系：name主】
        //   剛体→ボーン：RelatedBoneName を boneNameToIndex（PMX出力ボーン順）で解決。
        //   JOINT→剛体A/B：BodyAName/BodyBName を「剛体名→PMX剛体index」マップで解決。
        //   PMXWriter のJOINT名補完は発火条件が index<-1 のため index 解決は必須
        //   （ここで正しい index を設定する）。
        // ----------------------------------------------------------------

        /// <summary>
        /// 剛体コンテキスト群を PMXRigidBody に変換して document に追加する。
        /// </summary>
        /// <returns>剛体名 → PMX剛体index のマップ（JOINTの剛体参照解決に使用）。</returns>
        private static Dictionary<string, int> ConvertRigidBodies(
            List<MeshContext> rigidBodyContexts,
            PMXDocument document,
            Dictionary<string, int> boneNameToIndex,
            PMXExportSettings settings)
        {
            var nameToIndex = new Dictionary<string, int>();

            foreach (var ctx in rigidBodyContexts)
            {
                var data = ctx.MeshObject.RigidBodyData;

                // 関連ボーンは name主で解決（PMX出力ボーンindexは boneNameToIndex に一致）
                int boneIndex = -1;
                if (!string.IsNullOrEmpty(data.RelatedBoneName) &&
                    boneNameToIndex.TryGetValue(data.RelatedBoneName, out int bi))
                {
                    boneIndex = bi;
                }

                var pmxBody = new PMXRigidBody
                {
                    Name            = ctx.Name,
                    NameEnglish     = string.IsNullOrEmpty(data.NameEnglish) ? ctx.Name : data.NameEnglish,
                    BoneIndex       = boneIndex,
                    RelatedBoneName = data.RelatedBoneName ?? "",
                    Group           = data.Group,
                    CollisionMask   = data.CollisionMask,
                    Shape           = (int)data.Shape,
                    Size            = data.Size * settings.Scale,            // ×Scale（範囲量のためZ反転しない）
                    Position        = ConvertPosition(data.Position, settings),
                    Rotation        = ConvertEulerRotation(data.Rotation, settings),
                    Mass            = data.Mass,
                    LinearDamping   = data.LinearDamping,
                    AngularDamping  = data.AngularDamping,
                    Restitution     = data.Restitution,
                    Friction        = data.Friction,
                    PhysicsMode     = (int)data.PhysicsMode
                };

                // 名前→index（同名は最初の出現を採用。Writerの名前検索と整合）
                if (!nameToIndex.ContainsKey(pmxBody.Name))
                    nameToIndex[pmxBody.Name] = document.RigidBodies.Count;

                document.RigidBodies.Add(pmxBody);
            }

            return nameToIndex;
        }

        /// <summary>
        /// JOINTコンテキスト群を PMXJoint に変換して document に追加する。
        /// 剛体A/Bは rigidBodyNameToIndex（剛体名→PMX剛体index）で解決する。
        /// </summary>
        private static void ConvertJoints(
            List<MeshContext> jointContexts,
            PMXDocument document,
            Dictionary<string, int> rigidBodyNameToIndex,
            PMXExportSettings settings)
        {
            foreach (var ctx in jointContexts)
            {
                var data = ctx.MeshObject.JointData;

                // 剛体A/BのPMX剛体index（未解決は-1。WriterはJOINTで名前補完しないため必須）
                int idxA = (!string.IsNullOrEmpty(data.BodyAName) &&
                            rigidBodyNameToIndex.TryGetValue(data.BodyAName, out int a)) ? a : -1;
                int idxB = (!string.IsNullOrEmpty(data.BodyBName) &&
                            rigidBodyNameToIndex.TryGetValue(data.BodyBName, out int b)) ? b : -1;

                var pmxJoint = new PMXJoint
                {
                    Name              = ctx.Name,
                    NameEnglish       = string.IsNullOrEmpty(data.NameEnglish) ? ctx.Name : data.NameEnglish,
                    JointType         = data.JointType,
                    RigidBodyIndexA   = idxA,
                    BodyAName         = data.BodyAName ?? "",
                    RigidBodyIndexB   = idxB,
                    BodyBName         = data.BodyBName ?? "",
                    Position          = ConvertPosition(data.Position, settings),
                    Rotation          = ConvertEulerRotation(data.Rotation, settings),
                    TranslationMin    = data.TranslationMin,
                    TranslationMax    = data.TranslationMax,
                    RotationMin       = data.RotationMin,
                    RotationMax       = data.RotationMax,
                    SpringTranslation = data.SpringTranslation,
                    SpringRotation    = data.SpringRotation
                };

                document.Joints.Add(pmxJoint);
            }
        }

        /// <summary>
        /// モデル空間のオイラー角回転（ラジアン）を PMX のオイラー角（ラジアン）へ変換する。
        /// 共役変換 S·R·S は自己逆元のため、インポート側と同一処理がそのまま逆変換になる。
        /// 規則は AxisFlipOps に集約。入力/出力ともラジアン。
        /// </summary>
        private static Vector3 ConvertEulerRotation(Vector3 modelEulerRad, PMXExportSettings settings)
        {
            return AxisFlipOps.EulerRad(settings.Flip, modelEulerRad);
        }
    }
}
