// Runtime/Poly_Ling_Main/Tools/SpringBoneTest/SpringBoneTestRigBuilder.cs
// ============================================================
// スプリングボーン検証用のダミー装備を生成する
// ============================================================
//
// 【なぜ要るか】
//   SpringBoneChainRoot / SpringBoneJoint / SpringBoneColliders を書き込む
//   オーサリング UI が無く、CSV で持つモデルしか揺れデータを持たない。
//   VRM 出力（VRMC_springBone）の検証ができないので、既存モデルへ
//   その場でダミーの揺れものを足す。
//
// 【生成物】
//   Skirt    … 腰の周囲に放射状のチェーン（円錐台）＋ 脚カプセル2本
//   Ponytail … 頭の後ろに1本のチェーン ＋ 球コライダー1個
//
//   どちらも「ボーン鎖」「その鎖にスキンしたメッシュ」「揺れ付帯データ」
//   「コライダーとグループ」を一度に作る。
//
// 【格納規約】
//   ボーン付帯データの格納規約は MeshObject.cs「ボーン付帯データ格納規約」、
//   チェーン形状の導出規約は SpringBoneChainData.cs を正典とする。
//   チェーンのジョイント列は保持しない。ルートに SpringBoneChainRoot、
//   構成ボーン全部に SpringBoneJoint を付けると、階層からチェーンが導出される。
//
// 【座標】
//   ボーンの BoneTransform は親からのローカル、BindPose はワールドの逆行列。
//   スキンドメッシュの頂点はワールド（バインド）空間に置く
//   （MeshObject.cs:616-623 の SkinKind 規約）。
//   モデル全体の姿勢は呼び出し側が ComputeWorldMatrices で確定させる。
//
// 【依存】
//   #if UNITY_EDITOR を含まない純ロジック。UnityEngine の型のみ使う。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Materials;   // MaterialData / MaterialReference / SurfaceType / CullModeType

namespace Poly_Ling.Tools.SpringBoneTest
{
    /// <summary>生成する装備の形。</summary>
    public enum SpringBoneTestRigShape
    {
        /// <summary>腰の周囲に放射状のチェーン（円錐台）＋脚カプセル2本。</summary>
        Skirt = 0,

        /// <summary>頭の後ろに1本のチェーン＋球コライダー1個。</summary>
        Ponytail = 1,
    }

    /// <summary>生成パラメータ。既定値は SkirtVerification.cs / SpringVerification.cs にそろえてある。</summary>
    [Serializable]
    public class SpringBoneTestRigParams
    {
        public SpringBoneTestRigShape Shape = SpringBoneTestRigShape.Skirt;

        /// <summary>生成物の名前につける接頭辞。削除・再生成の目印にもする。</summary>
        public string Prefix = "SBTest";

        // ---- 形状（Skirt） ----
        public int   Strands           = 12;
        public int   SegmentsPerStrand = 5;
        public float WaistRadius       = 0.12f;
        public float HemRadius         = 0.45f;
        public float SegmentLength     = 0.12f;

        /// <summary>
        /// スカートの腰高さを股関節（LeftUpperLeg / RightUpperLeg）から自動で決めるか。
        ///
        /// 【なぜ取付先ボーンの位置を使えないか】
        ///   PMX の「センター」は移動制御用のボーンで、腰の高さにあるとは限らない。
        ///   Humanoid 割当で Hips に割り当てられるが、それは「Unity Humanoid が
        ///   Hips → Spine の親子を要求するため」という階層の都合であって、
        ///   位置が腰だからではない（HumanoidBoneMapping.cs:134-148 に明記）。
        ///   実測例では センター Y=0.608 に対して 股関節 Y=1.233・ひざ Y=0.856 で、
        ///   センターはひざより下だった。そこへ生やすと裾が床まで落ちる。
        ///
        ///   股関節はどのモデルでも必ず腰の高さにあるので、これを基準にする。
        /// </summary>
        public bool AutoSkirtHeight = true;

        /// <summary>
        /// スカートの腰高さの追加補正[m]。
        /// AutoSkirtHeight が true のときは股関節高さからの差分、
        /// false のときは取付先ボーンからの持ち上げ量として効く。
        /// </summary>
        public float SkirtLift = 0f;

        // ---- 形状（Ponytail） ----
        public int   PonytailSegments = 6;

        /// <summary>
        /// 頭から後ろ（+Z）へずらす量[m]。
        /// PMX は Z マイナス方向を向くので後ろは +Z。
        /// 頭に密着させると髪と重なって見えないので既定で離してある。
        /// 頭のコライダーも同じだけずらす。
        /// </summary>
        public float PonytailBack     = 0.50f;

        public float PonytailWidth    = 0.06f;   // メッシュの帯幅（片側）

        // ---- Spring ----
        public float StiffnessTop  = 0.01f;   // 根元側
        public float StiffnessTip  = 0.08f;   // 末端側
        public float Drag          = 0.15f;
        public float GravityPower  = 0.15f;
        public float HitRadius     = 0.04f;

        // ---- コライダー ----
        public float LegRadius  = 0.07f;
        public float LegSpacing = 0.09f;
        public float LegLength  = 0.7f;
        public float HeadRadius = 0.12f;

        /// <summary>チェーンの慣性基準に取付先ボーンを使うか。false なら World 空間評価。</summary>
        public bool UseCenterBone = false;
    }

    /// <summary>生成結果。パネルが検査に使う。</summary>
    public class SpringBoneTestRigResult
    {
        public bool   Success;
        public string Message = "";

        public int AddedBoneCount;
        public int AddedMeshCount;
        public int ChainCount;
        public int JointCount;
        public int ColliderCount;
        public int ColliderGroupCount;

        /// <summary>取付先に選んだボーン名（空＝モデル直下）。</summary>
        public string AttachBoneName = "";

        /// <summary>スカートの腰高さ（ワールド Y）。Skirt 以外では 0。</summary>
        public float WaistY;

        /// <summary>追加した MeshContext のマスター索引（生成順）。</summary>
        public readonly List<int> AddedIndices = new List<int>();

        /// <summary>警告（呼び出し側がログへ流す）。</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>補足（警告ではない）。</summary>
        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>既存モデルへ揺れもののダミー装備を足す。</summary>
    public static class SpringBoneTestRigBuilder
    {
        // ================================================================
        // エントリ
        // ================================================================

        public static SpringBoneTestRigResult Build(ModelContext model, SpringBoneTestRigParams p)
        {
            var result = new SpringBoneTestRigResult();
            if (model == null) { result.Message = "モデルがありません。"; return result; }

            p = p ?? new SpringBoneTestRigParams();

            // 取付先。Skirt は腰、Ponytail は頭。
            //   Humanoid 割当があればそれを使い、無ければ名前で探す。
            //   どちらも取れなければモデル直下（親なし）に生やす。
            //   ※ Skirt の「高さ」は取付先の位置ではなく股関節から決める（後述）。
            int attachIndex = p.Shape == SpringBoneTestRigShape.Skirt
                ? ResolveAttachBone(model, "Hips",  new[] { "センター", "下半身", "腰", "Hips" })
                : ResolveAttachBone(model, "Head",  new[] { "頭", "Head" });

            var attachCtx = attachIndex >= 0 ? model.GetMeshContext(attachIndex) : null;
            result.AttachBoneName = attachCtx?.Name ?? "";

            // 取付先ボーンのワールド位置。取れなければモデル原点。
            //
            // 【origin と attachWorld を分ける理由】
            //   スカートは腰の高さを股関節から決めるので、鎖の起点（origin）は
            //   取付先ボーンの位置（attachWorld）と一致しない。
            //   ボーンのローカル位置は「親からの差」なので、必ず attachWorld を
            //   基準に取らないと、メッシュだけ上がってボーンが取り残される。
            Vector3 attachWorld = Vector3.zero;
            if (attachCtx != null)
            {
                var wm = attachCtx.WorldMatrix;
                attachWorld = new Vector3(wm.m03, wm.m13, wm.m23);
            }

            Vector3 origin = attachWorld;

            // コライダーグループを1つ確保する。
            //   ModelContext.SpringBoneColliderGroupNames への index が
            //   チェーン側・コライダー側の共通の参照キー（SpringBoneChainData.cs:22-25）。
            string groupName = p.Prefix + (p.Shape == SpringBoneTestRigShape.Skirt ? "_legs" : "_head");
            int groupIndex = EnsureColliderGroup(model, groupName);
            result.ColliderGroupCount = model.SpringBoneColliderGroupNames?.Count ?? 0;

            // 専用マテリアル。既存マテリアルを流用すると、そちらが
            // アルファカットのテクスチャだった場合に UV 次第で丸ごと消える
            // （実測：顔肌マテリアルを引くと alphaMode=MASK で描画されなかった）。
            int materialIndex = EnsureMaterial(model, p.Prefix + "_Mat");

            if (p.Shape == SpringBoneTestRigShape.Skirt)
            {
                // 腰の高さを解決して origin の Y を差し替える。
                // X / Z は取付先のまま（体の中心にそろえる）。
                float waistY = ResolveWaistY(model, p, attachWorld.y, result);
                origin = new Vector3(attachWorld.x, waistY, attachWorld.z);
                result.WaistY = waistY;

                BuildSkirt(model, p, attachIndex, attachWorld, origin,
                           groupIndex, materialIndex, result);
            }
            else
            {
                BuildPonytail(model, p, attachIndex, attachWorld, origin,
                              groupIndex, materialIndex, result);
            }

            // 生成したボーンのワールド行列を確定させる。
            model.ComputeWorldMatrices();

            // スキンドメッシュの頂点はワールド（バインド）空間なので、
            // ワールド行列が確定してから BindPose を入れ直す。
            FixBindPoses(model, result.AddedIndices);

            result.Success = result.AddedBoneCount > 0;
            result.Message = result.Success
                ? $"{p.Shape} を生成しました（取付先 \"{result.AttachBoneName}\"）。"
                : "ボーンを生成できませんでした。";
            return result;
        }

        // ================================================================
        // スカート
        // ================================================================

        /// <param name="attachWorld">取付先ボーンのワールド位置。ローカル差の基準。</param>
        /// <param name="origin">鎖の起点（腰の高さに解決済み）。頂点位置の基準。</param>
        private static void BuildSkirt(
            ModelContext model, SpringBoneTestRigParams p,
            int attachIndex, Vector3 attachWorld, Vector3 origin,
            int groupIndex, int materialIndex,
            SpringBoneTestRigResult result)
        {
            int strands  = Mathf.Max(1, p.Strands);
            int segments = Mathf.Max(1, p.SegmentsPerStrand);

            // origin の Y は Build 側で腰高さに解決済み。ここでは動かさない。
            // 脚コライダーは取付先ボーンのローカルで置くので、
            // 腰高さと取付先の差を Y オフセットとして渡す（下の legOffsetY）。

            // 1本あたりの半径増分。各段の外向きローカルに入れると側面が直線＝円錐台。
            float radialStep = (p.HemRadius - p.WaistRadius) / segments;

            // 鎖ごとのワールド座標を控えておき、あとでメッシュを張る。
            var strandWorld = new List<List<Vector3>>();
            var strandBoneIdx = new List<List<int>>();

            for (int s = 0; s < strands; s++)
            {
                float ang = (float)s / strands * Mathf.PI * 2f;
                float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);

                var worlds = new List<Vector3>();
                var indices = new List<int>();

                // 鎖の根元（腰の上）。ここがチェーンのルートになる。
                Vector3 topLocal = new Vector3(cx * p.WaistRadius, 0f, cz * p.WaistRadius);
                Vector3 topWorld = origin + topLocal;

                // 根元ボーンのローカル位置は「取付先ボーンからの差」。
                // origin（腰高さ）ではなく attachWorld から取らないと、
                // メッシュだけ上がってボーンが取付先に取り残される。
                int parentIdx = attachIndex;
                Vector3 parentWorld = attachWorld;

                int rootIdx = AddBone(model, $"{p.Prefix}_Strand{s:00}_top",
                                      parentIdx, topWorld - parentWorld, topWorld, result);
                worlds.Add(topWorld);
                indices.Add(rootIdx);

                parentIdx   = rootIdx;
                parentWorld = topWorld;

                for (int seg = 1; seg <= segments; seg++)
                {
                    Vector3 step = new Vector3(cx * radialStep, -p.SegmentLength, cz * radialStep);
                    Vector3 w = parentWorld + step;

                    int idx = AddBone(model, $"{p.Prefix}_Strand{s:00}_{seg}",
                                      parentIdx, step, w, result);
                    worlds.Add(w);
                    indices.Add(idx);

                    parentIdx   = idx;
                    parentWorld = w;
                }

                // 揺れ付帯データ。ルートに Chain、全段に Joint。
                ApplyChain(model, indices, p, groupIndex,
                           $"{p.Prefix}_Strand{s:00}", attachIndex, result);

                strandWorld.Add(worlds);
                strandBoneIdx.Add(indices);
            }

            // 見た目。円周方向に隣の鎖とつないで筒にする。
            int meshIdx = AddSkirtMesh(model, p, strandWorld, strandBoneIdx, materialIndex, result);
            if (meshIdx >= 0) result.AddedMeshCount++;

            // 脚コライダー2本。取付先ボーンに付け、腰と一緒に動くようにする。
            //   Offset / Tail は付帯ボーンのローカル（SpringBoneColliderData.cs の規約）。
            int legHost = attachIndex >= 0 ? attachIndex : -1;
            if (legHost >= 0)
            {
                // コライダーの Offset / Tail は付帯ボーンのローカル。
                // 腰は取付先ボーンより上にあることが多いので、その差を足す。
                var hostWm = model.GetMeshContext(legHost).WorldMatrix;
                float legOffsetY = origin.y - hostWm.m13;

                AddCollider(model, legHost, groupIndex, SpringBoneColliderShape.Capsule,
                            new Vector3(-p.LegSpacing, legOffsetY, 0f), p.LegRadius,
                            new Vector3(-p.LegSpacing, legOffsetY - p.LegLength, 0f), Vector3.up, result);
                AddCollider(model, legHost, groupIndex, SpringBoneColliderShape.Capsule,
                            new Vector3(p.LegSpacing, legOffsetY, 0f), p.LegRadius,
                            new Vector3(p.LegSpacing, legOffsetY - p.LegLength, 0f), Vector3.up, result);
            }
        }

        // ================================================================
        // ポニーテール
        // ================================================================

        /// <param name="attachWorld">取付先ボーンのワールド位置。ローカル差の基準。</param>
        /// <param name="origin">鎖の起点。ポニテでは attachWorld と同じ。</param>
        private static void BuildPonytail(
            ModelContext model, SpringBoneTestRigParams p,
            int attachIndex, Vector3 attachWorld, Vector3 origin,
            int groupIndex, int materialIndex,
            SpringBoneTestRigResult result)
        {
            int segments = Mathf.Max(1, p.PonytailSegments);

            var worlds  = new List<Vector3>();
            var indices = new List<int>();

            // 鎖の根元。頭の後ろへ少しずらす。
            //   PMX は Z マイナス方向を向くので、後ろ＝+Z。
            Vector3 topWorld = origin + new Vector3(0f, 0f, p.PonytailBack);

            int parentIdx     = attachIndex;
            Vector3 parentWorld = attachWorld;

            int rootIdx = AddBone(model, $"{p.Prefix}_Tail_top",
                                  parentIdx, topWorld - parentWorld, topWorld, result);
            worlds.Add(topWorld);
            indices.Add(rootIdx);

            parentIdx   = rootIdx;
            parentWorld = topWorld;

            for (int seg = 1; seg <= segments; seg++)
            {
                Vector3 step = new Vector3(0f, -p.SegmentLength, 0f);
                Vector3 w = parentWorld + step;

                int idx = AddBone(model, $"{p.Prefix}_Tail_{seg}", parentIdx, step, w, result);
                worlds.Add(w);
                indices.Add(idx);

                parentIdx   = idx;
                parentWorld = w;
            }

            ApplyChain(model, indices, p, groupIndex, $"{p.Prefix}_Tail", attachIndex, result);

            int meshIdx = AddPonytailMesh(model, p, worlds, indices, materialIndex, result);
            if (meshIdx >= 0) result.AddedMeshCount++;

            // 頭の球コライダー1個。
            if (attachIndex >= 0)
            {
                AddCollider(model, attachIndex, groupIndex, SpringBoneColliderShape.Sphere,
                            new Vector3(0f, 0f, p.PonytailBack), p.HeadRadius,
                            Vector3.zero, Vector3.up, result);
            }
        }

        // ================================================================
        // 揺れ付帯データ
        // ================================================================

        /// <summary>
        /// ルートに SpringBoneChainRoot、構成ボーン全部に SpringBoneJoint を付ける。
        /// stiffness は根元→末端で線形補間する。
        /// </summary>
        private static void ApplyChain(
            ModelContext model, List<int> boneIndices, SpringBoneTestRigParams p,
            int groupIndex, string chainName, int attachIndex,
            SpringBoneTestRigResult result)
        {
            if (boneIndices == null || boneIndices.Count == 0) return;

            var rootMo = model.GetMeshContext(boneIndices[0])?.MeshObject;
            if (rootMo == null) return;

            string centerName = "";
            if (p.UseCenterBone && attachIndex >= 0)
                centerName = model.GetMeshContext(attachIndex)?.Name ?? "";

            rootMo.SpringBoneChainRoot = new SpringBoneChainData
            {
                Name = chainName,
                CenterBoneName = centerName,
                SpringBoneColliderGroupIndices = groupIndex >= 0
                    ? new List<int> { groupIndex }
                    : new List<int>(),
            };
            result.ChainCount++;

            int n = boneIndices.Count;
            for (int i = 0; i < n; i++)
            {
                var mo = model.GetMeshContext(boneIndices[i])?.MeshObject;
                if (mo == null) continue;

                float u = n > 1 ? (float)i / (n - 1) : 0f;

                mo.SpringBoneJoint = new SpringBoneJointData
                {
                    HitRadius      = p.HitRadius,
                    StiffnessForce = Mathf.Lerp(p.StiffnessTop, p.StiffnessTip, u),
                    GravityPower   = p.GravityPower,
                    GravityDir     = new Vector3(0f, -1f, 0f),
                    DragForce      = p.Drag,
                };
                result.JointCount++;
            }
        }

        /// <summary>コライダーを1個足す。Offset / Tail は付帯ボーンのローカル。</summary>
        private static void AddCollider(
            ModelContext model, int boneIndex, int groupIndex,
            SpringBoneColliderShape shape, Vector3 offset, float radius,
            Vector3 tail, Vector3 normal, SpringBoneTestRigResult result)
        {
            var mo = model.GetMeshContext(boneIndex)?.MeshObject;
            if (mo == null) return;

            if (mo.SpringBoneColliders == null)
                mo.SpringBoneColliders = new List<SpringBoneColliderData>();

            mo.SpringBoneColliders.Add(new SpringBoneColliderData
            {
                Shape  = shape,
                Offset = offset,
                Radius = radius,
                Tail   = tail,
                Normal = normal,
                SpringBoneGroupIndices = groupIndex >= 0 ? new List<int> { groupIndex } : new List<int>(),
            });
            result.ColliderCount++;
        }

        /// <summary>コライダーグループ名を確保して index を返す。同名があれば使い回す。</summary>
        private static int EnsureColliderGroup(ModelContext model, string name)
        {
            if (model.SpringBoneColliderGroupNames == null) return -1;

            var names = model.SpringBoneColliderGroupNames;
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], name, StringComparison.Ordinal)) return i;

            names.Add(name);
            return names.Count - 1;
        }

        // ================================================================
        // ボーン生成
        // ================================================================

        /// <summary>
        /// ボーン MeshContext を1つ足して索引を返す。
        /// 構築の仕方は PMXImporter のボーン生成にそろえてある
        /// （BoneTransform はローカル、BindPose はワールドの逆行列）。
        /// </summary>
        private static int AddBone(
            ModelContext model, string name, int parentIndex,
            Vector3 localPosition, Vector3 worldPosition,
            SpringBoneTestRigResult result)
        {
            var bt = new BoneTransform
            {
                Position = localPosition,
                Rotation = Vector3.zero,
                Scale    = Vector3.one,
                UseLocalTransform = true,
                HasBoneTransform  = true,
            };

            var mo = new MeshObject(name)
            {
                Type = MeshType.Bone,
                HierarchyParentIndex = parentIndex,
                BoneTransform = bt,
            };

            var mc = new MeshContext
            {
                MeshObject = mo,
                Name       = name,
                Type       = MeshType.Bone,
                IsVisible  = true,
                BindPose   = Matrix4x4.TRS(worldPosition, Quaternion.identity, Vector3.one).inverse,
                BoneTransform = bt,
                HierarchyParentIndex = parentIndex,
                BonePoseData = new BonePoseData { IsActive = true },
            };
            // ModelContext.Add は追加位置を返す（ModelContext.cs:1414-1428）。
            int index = model.Add(mc);
            result.AddedIndices.Add(index);
            result.AddedBoneCount++;
            return index;
        }

        /// <summary>
        /// 生成したボーンの BindPose を、確定したワールド行列から入れ直す。
        /// AddBone の時点では親の姿勢が未確定なので暫定値しか入れられない。
        /// </summary>
        private static void FixBindPoses(ModelContext model, List<int> addedIndices)
        {
            foreach (int i in addedIndices)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                mc.BindPose = mc.WorldMatrix.inverse;
            }
        }

        // ================================================================
        // メッシュ生成
        // ================================================================

        /// <summary>
        /// スカートの筒メッシュ。隣り合う鎖どうしを四角でつなぐ。
        /// 頂点はワールド（バインド）空間、ウェイトはその段のボーンに 1.0。
        /// </summary>
        private static int AddSkirtMesh(
            ModelContext model, SpringBoneTestRigParams p,
            List<List<Vector3>> strandWorld, List<List<int>> strandBoneIdx,
            int materialIndex, SpringBoneTestRigResult result)
        {
            int strands = strandWorld.Count;
            if (strands < 2) return -1;

            int rows = strandWorld[0].Count;
            var mo = NewVisibleMesh(p.Prefix + "_SkirtMesh");

            // 面は片側だけ張る。両面表示はマテリアル（CullMode.Off）に任せる。
            // 裏面ジオメトリを複製すると頂点も三角形も倍になり、同一位置に
            // 2 枚重なって Z ファイティングの種になる。
            for (int s = 0; s < strands; s++)
            {
                for (int r = 0; r < rows; r++)
                {
                    float u = (float)s / strands;
                    float v = (float)r / Mathf.Max(1, rows - 1);

                    var vert = new Vertex(strandWorld[s][r], new Vector2(u, v), Vector3.up);
                    vert.BoneWeight = MakeWeight(strandBoneIdx[s][r]);
                    mo.Vertices.Add(vert);
                }
            }

            for (int s = 0; s < strands; s++)
            {
                int s2 = (s + 1) % strands;
                for (int r = 0; r < rows - 1; r++)
                {
                    int a = s  * rows + r;
                    int b = s  * rows + r + 1;
                    int c = s2 * rows + r + 1;
                    int d = s2 * rows + r;

                    mo.AddQuad(a, b, c, d, materialIndex);
                }
            }

            return AddMesh(model, mo, result);
        }

        /// <summary>ポニーテールの帯メッシュ。各段で左右に幅を持たせる。</summary>
        private static int AddPonytailMesh(
            ModelContext model, SpringBoneTestRigParams p,
            List<Vector3> worlds, List<int> boneIndices,
            int materialIndex, SpringBoneTestRigResult result)
        {
            int rows = worlds.Count;
            if (rows < 2) return -1;

            var mo = NewVisibleMesh(p.Prefix + "_TailMesh");

            // 面は片側だけ張る（理由は AddSkirtMesh と同じ）。
            for (int r = 0; r < rows; r++)
            {
                float v = (float)r / Mathf.Max(1, rows - 1);
                var w = MakeWeight(boneIndices[r]);

                var left  = new Vertex(worlds[r] + new Vector3(-p.PonytailWidth, 0f, 0f),
                                       new Vector2(0f, v), Vector3.up);
                var right = new Vertex(worlds[r] + new Vector3(p.PonytailWidth, 0f, 0f),
                                       new Vector2(1f, v), Vector3.up);
                left.BoneWeight  = w;
                right.BoneWeight = w;

                mo.Vertices.Add(left);
                mo.Vertices.Add(right);
            }

            for (int r = 0; r < rows - 1; r++)
            {
                int a = r * 2;
                int b = r * 2 + 1;
                int c = (r + 1) * 2 + 1;
                int d = (r + 1) * 2;

                mo.AddQuad(a, b, c, d, materialIndex);
            }

            return AddMesh(model, mo, result);
        }

        /// <summary>
        /// 見えるダミーメッシュの器を作る。
        /// PreserveNormals は既定 true（MeshObject.cs:1056）で、そのままだと
        /// 宣言した法線がそのまま出る。ここは平面の帯なので、面から
        /// 計算し直させたほうが正しい向きになる。
        /// </summary>
        private static MeshObject NewVisibleMesh(string name)
        {
            var mo = new MeshObject(name);
            mo.SkinKind        = SkinKind.Skinned;
            mo.PreserveNormals = false;
            return mo;
        }

        /// <summary>
        /// 不透明・両面の専用マテリアルを確保して index を返す。同名があれば使い回す。
        /// </summary>
        private static int EnsureMaterial(ModelContext model, string name)
        {
            var refs = model.MaterialReferences;
            if (refs == null) return 0;

            for (int i = 0; i < refs.Count; i++)
                if (refs[i]?.Data != null &&
                    string.Equals(refs[i].Data.Name, name, StringComparison.Ordinal))
                    return i;

            var data = new MaterialData
            {
                Name             = name,
                Surface          = SurfaceType.Opaque,
                CullMode         = CullModeType.Off,
                AlphaClipEnabled = false,
                BaseColor        = new float[] { 0.3f, 0.8f, 1f, 1f },
            };

            refs.Add(new MaterialReference(data));
            return refs.Count - 1;
        }

        /// <summary>ボーン1本に 100% のウェイト。</summary>
        private static BoneWeight MakeWeight(int boneIndex)
        {
            return new BoneWeight
            {
                boneIndex0 = boneIndex, weight0 = 1f,
                boneIndex1 = 0,         weight1 = 0f,
                boneIndex2 = 0,         weight2 = 0f,
                boneIndex3 = 0,         weight3 = 0f,
            };
        }

        private static int AddMesh(ModelContext model, MeshObject mo, SpringBoneTestRigResult result)
        {
            var mc = new MeshContext
            {
                MeshObject = mo,
                Name       = mo.Name,
                Type       = MeshType.Mesh,
                IsVisible  = true,
                OriginalPositions = new Vector3[0],
            };
            int index = model.Add(mc);
            result.AddedIndices.Add(index);
            return index;
        }

        // ================================================================
        // 取付先の解決
        // ================================================================

        /// <summary>
        /// スカートの腰高さ（ワールド Y）を決める。
        ///
        /// 股関節（LeftUpperLeg / RightUpperLeg）の高さを使う。両方あれば平均、
        /// 片方だけならそれ。どちらも無ければ取付先の高さへ落とす。
        /// SkirtLift はここへの追加補正として最後に足す。
        ///
        /// 取付先ボーンの位置を使わない理由は AutoSkirtHeight の説明を参照。
        /// </summary>
        private static float ResolveWaistY(
            ModelContext model, SpringBoneTestRigParams p, float attachY,
            SpringBoneTestRigResult result)
        {
            if (!p.AutoSkirtHeight) return attachY + p.SkirtLift;

            float sum = 0f;
            int count = 0;

            foreach (string trait in new[] { "LeftUpperLeg", "RightUpperLeg" })
            {
                int idx = ResolveAttachBone(model, trait, EmptyAliases);
                if (idx < 0) continue;

                var mc = model.GetMeshContext(idx);
                if (mc == null) continue;

                sum += mc.WorldMatrix.m13;
                count++;
            }

            if (count == 0)
            {
                result.Warnings.Add(
                    "股関節（LeftUpperLeg / RightUpperLeg）の割当が無いため、"
                    + "スカートの高さを取付先ボーンに合わせました。"
                    + "PMX の「センター」は腰の高さとは限らないので、"
                    + "位置が合わない場合は Humanoid 割当を先に済ませてください。");
                return attachY + p.SkirtLift;
            }

            float waistY = sum / count + p.SkirtLift;

            result.Notes.Add(
                $"スカートの腰高さを股関節から決めました（Y={waistY:F3}／"
                + $"取付先 Y={attachY:F3}）。");

            return waistY;
        }

        private static readonly string[] EmptyAliases = new string[0];

        /// <summary>
        /// Humanoid 割当を優先し、無ければ別名で名前一致を探す。
        /// どちらも取れなければ -1（モデル直下）。
        /// </summary>
        private static int ResolveAttachBone(ModelContext model, string humanoidName, string[] aliases)
        {
            var mapping = model.HumanoidMapping;
            if (mapping != null && !mapping.IsEmpty &&
                mapping.BoneIndexMap.TryGetValue(humanoidName, out int mapped))
            {
                var mc = model.GetMeshContext(mapped);
                if (mc != null && mc.Type == MeshType.Bone) return mapped;
            }

            if (aliases == null) return -1;

            foreach (string alias in aliases)
            {
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.Bone) continue;
                    if (string.Equals(mc.Name, alias, StringComparison.Ordinal)) return i;
                }
            }

            return -1;
        }

        // ================================================================
        // 後始末
        // ================================================================

        /// <summary>
        /// 接頭辞で作った生成物をすべて削除する。作り直しのときに使う。
        /// 索引の大きい順に消して、途中の再マップで取り違えないようにする。
        /// </summary>
        public static int RemoveGenerated(ModelContext model, string prefix)
        {
            if (model == null || string.IsNullOrEmpty(prefix)) return 0;

            // ── 1) 生成した MeshContext を消す ────────────────────────
            var targets = new List<int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.Name != null && mc.Name.StartsWith(prefix, StringComparison.Ordinal))
                    targets.Add(i);
            }

            targets.Sort();
            targets.Reverse();

            foreach (int i in targets) model.RemoveAt(i);   // ModelContext.cs:1489

            // ── 2) 既存ボーンに付けたコライダーを消す ─────────────────
            //   コライダーの付帯先は「既存の頭・腰ボーン」であって生成物ではない。
            //   1) だけでは残り、実行のたびに増える（実測：頭に同じ球が 2 個）。
            //   接頭辞のグループに属するものだけを落とす。
            var names = model.SpringBoneColliderGroupNames;
            var removedGroups = new HashSet<int>();
            if (names != null)
            {
                for (int i = 0; i < names.Count; i++)
                    if (names[i] != null && names[i].StartsWith(prefix, StringComparison.Ordinal))
                        removedGroups.Add(i);
            }

            if (removedGroups.Count > 0)
            {
                // 残すグループの新しい index を先に決める。
                var remap = new Dictionary<int, int>();
                int newIndex = 0;
                for (int i = 0; i < names.Count; i++)
                    if (!removedGroups.Contains(i)) remap[i] = newIndex++;

                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mo = model.GetMeshContext(i)?.MeshObject;
                    if (mo == null) continue;

                    var colliders = mo.SpringBoneColliders;
                    if (colliders != null)
                    {
                        for (int c = colliders.Count - 1; c >= 0; c--)
                        {
                            var col = colliders[c];
                            if (col == null) { colliders.RemoveAt(c); continue; }

                            if (BelongsToRemoved(col.SpringBoneGroupIndices, removedGroups))
                                colliders.RemoveAt(c);
                            else
                                RemapIndices(col.SpringBoneGroupIndices, remap);
                        }
                    }

                    var chain = mo.SpringBoneChainRoot;
                    if (chain != null) RemapIndices(chain.SpringBoneColliderGroupIndices, remap);
                }

                for (int i = names.Count - 1; i >= 0; i--)
                    if (removedGroups.Contains(i)) names.RemoveAt(i);
            }

            // ── 3) 専用マテリアルを消す ──────────────────────────────
            //   面の MaterialIndex は生成メッシュごと消えているので、
            //   参照だけを落とせばよい。末尾に足しているので index はずれない。
            var refs = model.MaterialReferences;
            if (refs != null)
            {
                for (int i = refs.Count - 1; i >= 0; i--)
                {
                    string n = refs[i]?.Data?.Name;
                    if (n != null && n.StartsWith(prefix, StringComparison.Ordinal))
                        refs.RemoveAt(i);
                }
            }

            return targets.Count;
        }

        /// <summary>所属グループがすべて削除対象なら true（＝そのコライダーは要らない）。</summary>
        private static bool BelongsToRemoved(List<int> groups, HashSet<int> removed)
        {
            if (groups == null || groups.Count == 0) return false;

            foreach (int g in groups)
                if (!removed.Contains(g)) return false;

            return true;
        }

        /// <summary>グループ index を詰め直す。対応の無いものは落とす。</summary>
        private static void RemapIndices(List<int> indices, Dictionary<int, int> remap)
        {
            if (indices == null) return;

            for (int i = indices.Count - 1; i >= 0; i--)
            {
                if (remap.TryGetValue(indices[i], out int mapped)) indices[i] = mapped;
                else                                              indices.RemoveAt(i);
            }
        }
    }
}
