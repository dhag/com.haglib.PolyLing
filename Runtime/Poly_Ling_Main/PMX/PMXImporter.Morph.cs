// PMXImporter.Morph.cs
// PMX インポート：モーフ変換と剛体・JOINT のインポート。
// Runtime/Poly_Ling_Main/PMX/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Materials;
using Poly_Ling.Core;
using Poly_Ling.Symmetry;

namespace Poly_Ling.PMX
{
    public static partial class PMXImporter
    {
        // ================================================================
        // モーフ変換
        // ================================================================

        /// <summary>
        /// PolyLingメタUVモーフ（__PLM_ プレフィックス）を適用する。
        /// 各MeshContextの頂点IDとUVサブインデックスを復元する。
        /// </summary>
        private static void ApplyPolyLingMetaMorphs(PMXDocument document, PMXImportResult result)
        {
            foreach (var pmxMorph in document.Morphs)
            {
                if (pmxMorph.MorphType != 3) continue;
                if (pmxMorph.Name?.StartsWith("__PLM_") != true) continue;

                // 対象MeshContextを特定（MaterialGroupInfoのPmxToLocalIndexで検索）
                foreach (var offset in pmxMorph.Offsets)
                {
                    if (offset is not PMXUVMorphOffset uvOffset) continue;

                    int pmxVertexIndex = uvOffset.VertexIndex;
                    int uvSubIndex = Mathf.RoundToInt(uvOffset.Offset.y);
                    int localIndex = Mathf.RoundToInt(uvOffset.Offset.z);
                    int vertexId = Mathf.RoundToInt(uvOffset.Offset.w);

                    // このPMX頂点インデックスが属するMeshContextを探す
                    for (int gi = 0; gi < result.MaterialGroupInfos.Count; gi++)
                    {
                        var groupInfo = result.MaterialGroupInfos[gi];
                        if (!groupInfo.PmxToLocalIndex.TryGetValue(pmxVertexIndex, out int mappedLocal)) continue;
                        // mappedLocalはインポート時に再構築されたローカルインデックス
                        // localIndexはエクスポート時のMeshObjectインデックス（一致するはず）

                        if (groupInfo.MeshContextIndex < 0 || groupInfo.MeshContextIndex >= result.MeshContexts.Count) break;
                        var meshContext = result.MeshContexts[groupInfo.MeshContextIndex];
                        if (meshContext?.MeshObject == null) break;
                        var meshObject = meshContext.MeshObject;

                        // 頂点IDを復元
                        if (mappedLocal < meshObject.VertexCount)
                        {
                            meshObject.Vertices[mappedLocal].Id = vertexId;
                            meshObject.RegisterVertexId(vertexId);
                        }

                        // 面コーナーのUVサブインデックスを復元
                        foreach (var face in meshObject.Faces)
                        {
                            for (int ci = 0; ci < face.VertexIndices.Count; ci++)
                            {
                                if (face.VertexIndices[ci] == mappedLocal)
                                {
                                    while (face.UVIndices.Count <= ci)
                                        face.UVIndices.Add(0);
                                    face.UVIndices[ci] = uvSubIndex;
                                }
                            }
                        }
                        break;
                    }
                }
            }
        }

        // ================================================================
        // 形への変換（PolyLing_モーフ整理計画.md 3.9・段 6）
        //
        // 【規則】
        //   ・頂点モーフ 1 本＝位置の形 1 つ、UV モーフ（種類 3）1 本＝UV の形 1 つ。名前・英語名・パネルは形が持つ。
        //     隠しオブジェクト（種類 Morph）は作らない。差分は、その頂点がいる描画オブジェクトの頂点に入れる。
        //   ・形の位置は、頂点の位置と同じ計算で出す（PMX の位置＋差分 → 軸の反転 → 倍率。ConvertMaterialGroup と同じ順）。
        //     式が違うと丸めの差で、動いていない頂点が「形で動いた頂点」になる（段 2 の規則）。
        //     UV も同じく ConvertUV(PMX の UV＋差分)。
        //   ・差分 0 件のモーフも形として残す（予約枠）。同じ頂点が 1 本の中に何度も出るときは PMX の座標で足し合わせる。
        //   ・グループモーフはグループの表情（子の名前 × 重み。GroupChildren）にする。子を親へ展開しない。
        //     頂点モーフ・UV モーフの表情は作らない（形そのものが頂点の表情）。
        //   ・追加 UV1〜4 のモーフ（種類 4〜7）は読まずに捨てて警告する（段 7）。PolyLing は追加 UV を持たず
        //     （書き出しは追加 UV の数 0）、形に入れる先が無い。段 6 まではこれを旧モーフにして差分を UV0 に足していたが、
        //     それは別の UV を動かすことになり誤り。グループの子のうち、捨てたモーフを指すものも外す。
        //   ・ミラー側の形は、ミラーの対を組んだあとに実体側から作り直す（SyncImportedMirrorShapes）。
        //   ・T ポーズ変換（ConvertDocument の後段）は形も同じスキン行列で焼く（TPoseConverter。段 2）。
        // ================================================================

        private static void ConvertMorphs(PMXDocument document, PMXImportSettings settings, PMXImportResult result)
        {
            if (result.MaterialGroupInfos.Count == 0)
            {
                Debug.LogWarning("[PMXImporter] No material group info available for morph conversion");
                return;
            }

            var usedNames = new HashSet<string>();
            int nextId = 1;
            var droppedAdditionalUV = new List<string>();

            // 頂点モーフ・UV モーフ → 形
            for (int i = 0; i < document.Morphs.Count; i++)
            {
                var pmxMorph = document.Morphs[i];
                if (pmxMorph.MorphType == 1 || pmxMorph.MorphType == 3)
                {
                    // PolyLingメタモーフはApplyPolyLingMetaMorphsで処理するのでスキップ
                    if (pmxMorph.Name?.StartsWith("__PLM_") == true) continue;
                    ConvertMorphToShape(document, pmxMorph, settings, result, usedNames, ref nextId);
                }
                else if (IsAdditionalUVMorph(pmxMorph))
                {
                    droppedAdditionalUV.Add(pmxMorph.Name ?? $"[{i}]");
                }
                // ボーンモーフ(2)、マテリアルモーフ(8)等は未対応（書き出しは元の PMX から写す）
            }
            if (droppedAdditionalUV.Count > 0)
                Debug.LogWarning($"[PMXImporter] 追加 UV のモーフ {droppedAdditionalUV.Count} 本は読みません（PolyLing は追加 UV を持たない）: "
                               + string.Join(", ", droppedAdditionalUV.Take(20)) + (droppedAdditionalUV.Count > 20 ? " ほか" : ""));
            int droppedChildren = 0;

            // グループモーフ → グループの表情（子の名前 × 重み）
            for (int i = 0; i < document.Morphs.Count; i++)
            {
                var pmxMorph = document.Morphs[i];
                if (pmxMorph.MorphType != 0) continue;

                var groupSet = new MorphExpression(pmxMorph.Name, MorphType.Group)
                {
                    NameEnglish = pmxMorph.NameEnglish ?? "",
                    Panel = pmxMorph.Panel
                };

                foreach (var offset in pmxMorph.Offsets)
                {
                    if (offset is not PMXGroupMorphOffset groupOffset) continue;

                    int childMorphIndex = groupOffset.MorphIndex;

                    // 名前ベースのフォールバック（CSV入力でインデックスが-1の場合）
                    if (childMorphIndex < 0 && !string.IsNullOrEmpty(groupOffset.MorphName))
                    {
                        for (int j = 0; j < document.Morphs.Count; j++)
                        {
                            if (document.Morphs[j].Name == groupOffset.MorphName)
                            {
                                childMorphIndex = j;
                                break;
                            }
                        }
                    }

                    bool inRange = childMorphIndex >= 0 && childMorphIndex < document.Morphs.Count;

                    // 捨てた追加 UV のモーフを指す子は外す（冒頭の規則）。
                    if (inRange && IsAdditionalUVMorph(document.Morphs[childMorphIndex])) { droppedChildren++; continue; }

                    // 子の名前は PMX ドキュメントから取る（差分 0 件の子も名前で残す）。
                    string childName = inRange ? document.Morphs[childMorphIndex].Name : groupOffset.MorphName;

                    if (!string.IsNullOrEmpty(childName))
                        groupSet.GroupChildren.Add(new MorphGroupChild(childName, groupOffset.Weight));
                }

                // 子が 1 つも無いグループは書き戻しでも出ない（BuildGroupMorph）ので作らない。
                if (groupSet.GroupChildren.Count > 0)
                    result.MorphExpressions.Add(groupSet);
            }
            if (droppedChildren > 0)
                Debug.LogWarning($"[PMXImporter] グループモーフの子のうち、読まなかった追加 UV のモーフを指す {droppedChildren} 件を外しました。");
        }

        /// <summary>追加 UV1〜4 のモーフ（種類 4〜7）か。</summary>
        private static bool IsAdditionalUVMorph(PMXMorph m) => m != null && m.MorphType >= 4 && m.MorphType <= 7;

        /// <summary>
        /// 頂点モーフ（種類 1）・UV モーフ（種類 3）1 本を形 1 つにする（冒頭の規則）。
        /// </summary>
        private static void ConvertMorphToShape(
            PMXDocument document, PMXMorph pmxMorph, PMXImportSettings settings, PMXImportResult result,
            HashSet<string> usedNames, ref int nextId)
        {
            bool isUV = pmxMorph.MorphType == 3;

            string name = pmxMorph.Name ?? "";
            if (string.IsNullOrEmpty(name)) name = $"Morph_{nextId}";
            if (usedNames.Contains(name))
            {
                string baseName = name;
                for (int k = 2; ; k++)
                {
                    string c = $"{baseName}_{k}";
                    if (!usedNames.Contains(c)) { name = c; break; }
                }
                Debug.LogWarning($"[PMXImporter] モーフ名 '{baseName}' が重なるので、形の名前を '{name}' にしました。");
            }
            usedNames.Add(name);

            var def = new ShapeDef
            {
                Id          = nextId++,
                Name        = name,
                NameEnglish = pmxMorph.NameEnglish ?? "",
                Panel       = pmxMorph.Panel,
                Kind        = isUV ? ShapeKind.UV : ShapeKind.Position,
            };
            result.Shapes.Add(def);

            // 頂点ごとの差分（PMX の座標で足し合わせる）
            var sumPos = new Dictionary<int, Vector3>();
            var sumUV  = new Dictionary<int, Vector2>();
            foreach (var offset in pmxMorph.Offsets)
            {
                if (!isUV && offset is PMXVertexMorphOffset vo)
                {
                    sumPos.TryGetValue(vo.VertexIndex, out var s);
                    sumPos[vo.VertexIndex] = s + vo.Offset;
                }
                else if (isUV && offset is PMXUVMorphOffset uo)
                {
                    sumUV.TryGetValue(uo.VertexIndex, out var s);
                    sumUV[uo.VertexIndex] = s + new Vector2(uo.Offset.x, uo.Offset.y);
                }
            }

            int hit = 0;
            int total = isUV ? sumUV.Count : sumPos.Count;
            IEnumerable<int> keys = isUV ? (IEnumerable<int>)sumUV.Keys : sumPos.Keys;
            foreach (int pmxVertexIndex in keys)
            {
                if (pmxVertexIndex < 0 || pmxVertexIndex >= document.Vertices.Count) continue;
                for (int gi = 0; gi < result.MaterialGroupInfos.Count; gi++)
                {
                    var groupInfo = result.MaterialGroupInfos[gi];
                    if (!groupInfo.PmxToLocalIndex.TryGetValue(pmxVertexIndex, out int localIndex)) continue;
                    if (groupInfo.MeshContextIndex < 0 || groupInfo.MeshContextIndex >= result.MeshContexts.Count) break;
                    var mo = result.MeshContexts[groupInfo.MeshContextIndex]?.MeshObject;
                    if (mo == null || localIndex >= mo.VertexCount) break;
                    var v = mo.Vertices[localIndex];
                    var pmxVert = document.Vertices[pmxVertexIndex];
                    hit++;

                    if (isUV)
                    {
                        if (v.UVs == null || v.UVs.Count == 0) break;
                        Vector2 uv = ConvertUV(pmxVert.UV + sumUV[pmxVertexIndex], settings);
                        if (!ShapeOps.Same(uv, v.UVs[0])) v.SetUVShape(def.Id, uv);
                    }
                    else
                    {
                        Vector3 p = ShapePositionFromPmx(pmxVert.Position + sumPos[pmxVertexIndex], settings);
                        if (!ShapeOps.Same(p, v.Position)) v.SetShape(def.Id, p);
                    }
                    break;  // 1つの頂点は1つのグループにのみ属する
                }
            }

            // オフセットはあったのに 1 件も当たらなかったとき（面に使われない頂点だけを指している等）は警告する。
            if (total > 0 && hit == 0)
            {
                Debug.LogWarning(
                    $"[PMXImporter] {(isUV ? "UV" : "頂点")}モーフ '{pmxMorph.Name}' のオフセット {pmxMorph.Offsets.Count} 件は、"
                  + "面に使われている頂点に当たらないため捨てました（形は差分なしで残します）。");
            }
        }

        /// <summary>
        /// PMX 座標の位置を、描画オブジェクトの頂点位置と同じ計算で変換する
        /// （ConvertMaterialGroup：軸の反転 → 倍率。倍率 1 のときは掛けない）。
        /// </summary>
        private static Vector3 ShapePositionFromPmx(Vector3 pmxPos, PMXImportSettings settings)
        {
            Vector3 p = ConvertPositionUnscaled(pmxPos, settings);
            if (Mathf.Abs(settings.Scale - 1f) > 0.0001f) p *= settings.Scale;
            return p;
        }

        /// <summary>
        /// ミラー側の形を実体側から作り直す（PolyLing_モーフ整理計画.md 3.7。ミラー側の位置も実体側から取り直される）。
        ///   ・ミラーの対：頂点の対応（VertexMap）と鏡映（MirrorPosition）で作り直す（MirrorPair.SyncPositions と同じ）。
        ///   ・実体化したミラー（BakedMirror）：頂点数が同じとき、同じ番号の頂点で作り直す
        ///     （鏡映はミラー軸・ミラー位置。形の点検 ShapeOps.Audit と同じ）。
        ///   どちらも、基本の位置が鏡映で合う頂点（1e-4 以内。点検と同じ幅）だけを作り直し、合わない頂点は PMX の値のまま。
        ///   基本の位置が左右で違う頂点まで作り直すと、その違いが形の差分に入り、動かしていない頂点まで動く
        ///   （ゼロちゃんの足首など。読み込み時点で左右の位置が 1e-4 を超えてずれている頂点がある）。
        ///   UV の形は鏡映しない（ミラー側も PMX の値のまま）。
        ///   PMX の値と鏡映が合わなかった頂点の数を警告に出す。
        /// </summary>
        private static void SyncImportedMirrorShapes(PMXImportResult result)
        {
            const float tol = 1e-4f;
            int CountChanged(Vertex real, Vertex mirror, Func<Vector3, Vector3> f)
            {
                int rc = real?.Shapes?.Count ?? 0, mc = mirror?.Shapes?.Count ?? 0;
                if (rc == 0 && mc == 0) return 0;
                if (rc != mc) return 1;
                for (int k = 0; k < rc; k++)
                {
                    var e = real.Shapes[k];
                    if (!mirror.TryGetShape(e.ShapeId, out var mp) || (f(e.Position) - mp).magnitude > tol) return 1;
                }
                return 0;
            }

            foreach (var pair in result.MirrorPairs)
            {
                var real = pair?.Real?.MeshObject;
                var mirror = pair?.Mirror?.MeshObject;
                if (real == null || mirror == null || pair.VertexMap == null) continue;
                int changed = 0, skipped = 0;
                int n = Math.Min(pair.VertexMap.Length, real.VertexCount);
                for (int i = 0; i < n; i++)
                {
                    int mi = pair.VertexMap[i];
                    if (mi < 0 || mi >= mirror.VertexCount) continue;
                    var rv = real.Vertices[i];
                    var mv = mirror.Vertices[mi];
                    if (rv.Shapes == null && mv.Shapes == null) continue;
                    if ((pair.MirrorPosition(rv.Position) - mv.Position).magnitude > tol) { skipped++; continue; }
                    changed += CountChanged(rv, mv, pair.MirrorPosition);
                    ShapeOps.MirrorShapesFrom(rv, mv, pair.MirrorPosition);
                }
                if (skipped > 0)
                    Debug.Log($"[PMXImporter] ミラー側 '{pair.Mirror.Name}' の形を持つ頂点のうち {skipped} は、"
                            + "基本の位置が実体側の鏡映と合わないので PMX の値のままにしました。");
                if (changed > 0)
                    Debug.LogWarning($"[PMXImporter] ミラー側 '{pair.Mirror.Name}' の形を実体側から作り直しました"
                                   + $"（PMX の値が実体側の鏡映と合わなかった頂点 {changed}）。");
            }

            var list = result.MeshContexts;
            for (int i = 0; i < list.Count; i++)
            {
                var mirrorCtx = list[i];
                if (mirrorCtx == null || mirrorCtx.Type != MeshType.BakedMirror) continue;
                int src = mirrorCtx.BakedMirrorSourceIndex;
                if (src < 0 || src >= list.Count) continue;
                var real = list[src]?.MeshObject;
                var mirror = mirrorCtx.MeshObject;
                if (real == null || mirror == null || real.VertexCount != mirror.VertexCount) continue;
                if (!ShapeOps.HasAnyShapes(real) && !ShapeOps.HasAnyShapes(mirror)) continue;
                int axis = list[src].MirrorAxis;
                float dist = list[src].MirrorDistance;
                Func<Vector3, Vector3> f = p => MirrorBranchOps.MirrorPoint(axis, dist, p);
                int changed = 0;
                for (int v = 0; v < real.VertexCount; v++)
                {
                    var rv = real.Vertices[v];
                    var mv = mirror.Vertices[v];
                    if ((f(rv.Position) - mv.Position).magnitude > tol) continue;
                    changed += CountChanged(rv, mv, f);
                    ShapeOps.MirrorShapesFrom(rv, mv, f);
                }
                if (changed > 0)
                    Debug.LogWarning($"[PMXImporter] 実体化したミラー '{mirrorCtx.Name}' の形を実体側から作り直しました"
                                   + $"（PMX の値が実体側の鏡映と合わなかった頂点 {changed}）。");
            }
        }

        // ================================================================
        // 剛体・JOINT インポート（段階②）
        // ================================================================
        //
        // 【方針】
        //   剛体/JOINTを頂点ゼロの空 MeshObject（Type=RigidBody / RigidBodyJoint）
        //   ＋付帯POCO（RigidBodyData / JointData）として取り込み、ボーン・モーフと
        //   同様に result.MeshContexts へ追加する。形状はギズモとして利用時に生成し、
        //   ここではジオメトリを持たせない（Drawableカテゴリ外のためGPU構築に入らない）。
        //
        // 【参照系：name主・index従】
        //   剛体→関連ボーン：RelatedBoneName（PMXReaderで解決済み）。BoneIndexはPMX
        //   ボーンindexで、ボーンを先頭に追加する本インポータでは MeshContextList の
        //   ボーンindexと一致する。
        //   JOINT→剛体A/B：BodyAName/BodyBName（解決済み）。indexは
        //   「剛体コンテキスト開始index ＋ PMX剛体index」で MeshContextList 上に解決する。
        //
        // 【座標変換】
        //   位置：ConvertPosition（×Scale ＋ FlipZ で z=-z）。頂点・ボーンと同一。
        //   回転：ConvertEulerRotation（PMX Euler(rad)→Quaternion→FlipZ共役→Euler(rad)）。
        //   サイズ：×Scale のみ（範囲量のためZ反転しない）。
        //   質量・減衰・反発・摩擦・Group・Mask・JointType・各min/max・Spring：生値。
        //   （JOINTのmin/max・SpringのFlipZ軸入替えは段階③エクスポート整合と併せて扱う）
        // ----------------------------------------------------------------

        /// <summary>剛体をMeshContext（Type=RigidBody）に変換して追加する。</summary>
        private static void ConvertRigidBodies(PMXDocument document, PMXImportSettings settings, PMXImportResult result)
        {
            foreach (var body in document.RigidBodies)
            {
                // 頂点/面を持たない空MeshObject（ボーンと同形）
                var meshObject = new MeshObject(body.Name)
                {
                    Type = MeshType.RigidBody
                };

                meshObject.RigidBodyData = new RigidBodyData
                {
                    NameEnglish     = body.NameEnglish ?? "",
                    RelatedBoneName = body.RelatedBoneName ?? "",
                    // ボーンを先頭に追加する本インポータでは PMXボーンindex == MeshContextListボーンindex
                    BoneIndex       = body.BoneIndex,
                    Group           = body.Group,
                    CollisionMask   = body.CollisionMask,
                    Shape           = (RigidBodyShape)body.Shape,
                    Size            = body.Size * settings.Scale,
                    Position        = ConvertPosition(body.Position, settings),
                    Rotation        = ConvertEulerRotation(body.Rotation, settings),
                    Mass            = body.Mass,
                    LinearDamping   = body.LinearDamping,
                    AngularDamping  = body.AngularDamping,
                    Restitution     = body.Restitution,
                    Friction        = body.Friction,
                    PhysicsMode     = (RigidBodyPhysicsMode)body.PhysicsMode
                };

                // MeshContextでラップ（MeshObjectを先に設定 → Name/Type委譲が成立）
                var meshContext = new MeshContext
                {
                    MeshObject = meshObject,
                    Name       = body.Name,
                    Type       = MeshType.RigidBody,
                    IsVisible  = true
                };

                result.MeshContexts.Add(meshContext);
            }
        }

        /// <summary>
        /// ジョイントをMeshContext（Type=RigidBodyJoint）に変換して追加する。
        /// </summary>
        /// <param name="rigidBodyContextBase">
        /// 剛体コンテキストの開始index（MeshContextList上）。-1=剛体未インポート。
        /// </param>
        private static void ConvertJoints(PMXDocument document, PMXImportSettings settings, PMXImportResult result, int rigidBodyContextBase)
        {
            foreach (var joint in document.Joints)
            {
                var meshObject = new MeshObject(joint.Name)
                {
                    Type = MeshType.RigidBodyJoint
                };

                // 剛体A/BのMeshContextList index（剛体未インポート時は-1のまま）
                int idxA = (rigidBodyContextBase >= 0 && joint.RigidBodyIndexA >= 0)
                    ? rigidBodyContextBase + joint.RigidBodyIndexA : -1;
                int idxB = (rigidBodyContextBase >= 0 && joint.RigidBodyIndexB >= 0)
                    ? rigidBodyContextBase + joint.RigidBodyIndexB : -1;

                meshObject.JointData = new JointData
                {
                    NameEnglish       = joint.NameEnglish ?? "",
                    JointType         = joint.JointType,
                    BodyAName         = joint.BodyAName ?? "",
                    BodyBName         = joint.BodyBName ?? "",
                    RigidBodyIndexA   = idxA,
                    RigidBodyIndexB   = idxB,
                    Position          = ConvertPosition(joint.Position, settings),
                    Rotation          = ConvertEulerRotation(joint.Rotation, settings),
                    TranslationMin    = joint.TranslationMin,
                    TranslationMax    = joint.TranslationMax,
                    RotationMin       = joint.RotationMin,
                    RotationMax       = joint.RotationMax,
                    SpringTranslation = joint.SpringTranslation,
                    SpringRotation    = joint.SpringRotation
                };

                var meshContext = new MeshContext
                {
                    MeshObject = meshObject,
                    Name       = joint.Name,
                    Type       = MeshType.RigidBodyJoint,
                    IsVisible  = true
                };

                result.MeshContexts.Add(meshContext);
            }
        }

        /// <summary>
        /// PMXのオイラー角回転（ラジアン）をモデル空間のオイラー角（ラジアン）へ変換する。
        ///
        /// ■ ボーン基底とは別扱いであること（注意）
        ///   剛体・JOINT の回転は「その物体の姿勢」であり、ボーンの局所軸ラベルでは
        ///   ないため、共役変換 S·R·S（AxisFlipOps.EulerRad）のままで正しい。
        ///   ボーン側は局所軸の合成そのものを廃止した（BoneModelRotation は恒等）。
        ///   こちらは追随させない。両者を同一視した旧コメントは誤りだった。
        /// 入力/出力ともラジアン。
        /// </summary>
        private static Vector3 ConvertEulerRotation(Vector3 pmxEulerRad, PMXImportSettings settings)
        {
            return AxisFlipOps.EulerRad(settings.Flip, pmxEulerRad);
        }
    }
}
