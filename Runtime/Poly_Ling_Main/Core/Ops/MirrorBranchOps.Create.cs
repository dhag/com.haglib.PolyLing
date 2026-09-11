// MirrorBranchOps.Create.cs
// ミラー分岐：生成ミラーの作成。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static partial class MirrorBranchOps
    {
        // ================================================================
        // 生成ミラーの作成
        //
        // MQO のミラーはファイルにミラー側の頂点が無く、実体側から作るしかない。
        // 生成物であることを MirrorGeometryDerived = true で示し、
        // 実効ワールドは S·H·S で解く（ModelContext.ComputeWorldMatrices）。
        //
        // ミラーを解消するときは破棄し、再びミラー化するときはここで作り直す。
        // 元は MQOImporter.CreateBakedMirrorMesh にあったが、
        // 読込時以外（ミラーの有効化）からも呼ぶため Ops へ移した。
        // ================================================================

        public static MeshContext CreateDerivedMirrorContext(MeshContext source, int sourceIndex)
            => CreateDerivedMirrorContext(source, sourceIndex, requireMirrorEnabled: true, nameExists: null);

        /// <summary>同名の有無を渡せる版。</summary>
        public static MeshContext CreateDerivedMirrorContext(
            MeshContext source, int sourceIndex, Func<string, bool> nameExists)
            => CreateDerivedMirrorContext(source, sourceIndex, requireMirrorEnabled: true, nameExists: nameExists);

        /// <summary>
        /// ミラー有効（MirrorType &gt; 0）の判定を省ける版。
        ///
        /// ミラー分岐の許容モードでは「ミラー設定を忘れた／作業中に切って戻し忘れた」
        /// ノードからもミラー側を作る必要がある。MirrorType はユーザーの表示設定に
        /// すぎず、鏡映そのものに必要なのは軸と距離だけなので、判定を外せるようにする。
        /// 軸・距離は source 自身の値を使う（分岐ルートの値では上書きしない）。
        /// </summary>
        public static MeshContext CreateDerivedMirrorContext(
            MeshContext source, int sourceIndex, bool requireMirrorEnabled)
            => CreateDerivedMirrorContext(source, sourceIndex, requireMirrorEnabled, nameExists: null);

        /// <summary>
        /// ミラー側の名前は MirrorNameOps.MakeMirrorName に決めさせる。
        ///
        /// 【なぜ直書きしないか】
        ///   「左腕」のミラーは「右腕」であって「左腕+」ではない。
        ///   接尾辞は左右を持たない名前（「腕」→「腕+」）のための逃げ道で、
        ///   左右を持つ名前に付けるのは誤りになる。判断は1箇所に置く。
        ///   nameExists には「その名前が既に使われているか」を渡す（null 可）。
        /// </summary>
        public static MeshContext CreateDerivedMirrorContext(
            MeshContext source, int sourceIndex, bool requireMirrorEnabled,
            Func<string, bool> nameExists)
        {
            if (source == null || source.MeshObject == null)
                return null;
            if (requireMirrorEnabled && !source.IsMirrored)
                return null;

            var srcMeshObj = source.MeshObject;

            if (srcMeshObj.Vertices.Count == 0)
                return null;

            int   axis = ResolveMirrorAxis(source);
            float dist = source.MirrorDistance;

            // スキニング済みか。
            //   スキンド変換（MeshFilterToSkinnedConverter の Phase 4）は、
            //   全メッシュの頂点をワールドへ焼き、BoneTransform を単位に潰し、
            //   MirrorGeometryDerived を無条件で false にする。
            //   ＝ スキンド化した時点でモデル内のミラーは全て PMX 型になる。
            //   変換より後に作るミラーもそれに揃える。
            bool skinned = srcMeshObj.IsSkinnedKind;

            // 頂点・面の鏡像化は BuildMirroredMeshObject に集約している。
            var mirrorMeshObj = BuildMirroredMeshObject(
                srcMeshObj, axis, dist, source.MirrorMaterialOffset,
                MirrorNameOps.MakeMirrorName(source.Name, MirrorBranchSuffix, nameExists));
            if (mirrorMeshObj == null) return null;
            mirrorMeshObj.Type = MeshType.BakedMirror;  // 明示的に設定

            // 姿勢は実体側と同一にする。
            // 生成ミラー（MirrorGeometryDerived=true）では、ミラー側は自前の姿勢を
            // 持たず実効ワールドを ComputeWorldMatrices が S·H·S として算出する。
            // ここで H を実体側と揃えておかないと v_M = S·v_R の不変条件が崩れる。
            //
            // PMX 型（skinned）ではコンバータが実体側の BoneTransform を
            // 単位に潰しているため、ここでのコピーも単位になり副作用は無い。
            // 描画はボーンウェイトで駆動されるので姿勢は使われない。
            if (mirrorMeshObj.BoneTransform == null)
                mirrorMeshObj.BoneTransform = new BoneTransform();
            if (source.BoneTransform != null)
            {
                mirrorMeshObj.BoneTransform.Position          = source.BoneTransform.Position;
                mirrorMeshObj.BoneTransform.Rotation          = source.BoneTransform.Rotation;
                mirrorMeshObj.BoneTransform.Scale             = source.BoneTransform.Scale;
                mirrorMeshObj.BoneTransform.UseLocalTransform = source.BoneTransform.UseLocalTransform;
            }

            // MeshContextを作成
            var mirrorContext = new MeshContext
            {
                MeshObject = mirrorMeshObj,
                Name = mirrorMeshObj.Name,
                Type = MeshType.BakedMirror,
                BakedMirrorSourceIndex = sourceIndex,
                // 実体側の頂点から生成した鏡像。実効ワールドは S·H·S で解決する。
                //
                // ただしスキニング済みメッシュから作った場合は PMX 型にする。
                // スキンド変換がモデル内の全メッシュを PMX 型に変えているので、
                // 変換より後に作るミラーだけ生成ミラーにすると持ち方が混ざる。
                //   ・頂点はコンバータがワールドへ焼いてあり BoneTransform は単位。
                //     ローカル鏡像がそのままモデル中心での鏡像になる。
                //   ・ミラー側は自分の BoneWeight で動くので、実体側の姿勢を
                //     引き写す S·H·S は不要かつ有害（二重変換になる）。
                //   ・RebakeDerivedMirrorVertices が焼いた頂点を上書きしない。
                //   ・DisableMirror が破棄せず独立メッシュとして残す。
                MirrorGeometryDerived = !skinned,
                // 階層情報は元メッシュに合わせる
                ParentIndex = source.ParentIndex,
                // ゲームオブジェクト階層の親も実体側に合わせる。
                //   既定値は -1（＝ルート）なので、設定しないとミラーだけが
                //   ルート直下に置かれ、ワールド行列に親の姿勢が乗らない。
                //   MQO 経路は挿入後に RecalculateParentIndicesFromDepth が
                //   ミラー専用分岐でここを設定するため露見しなかった。
                HierarchyParentIndex = ResolveMirrorHierarchyParent(source, skinned),
                Depth = source.Depth,
                IsVisible = source.IsVisible,
                // ミラー属性はなし（実体化されているため）
                MirrorType = 0,
                MirrorAxis = 1,
                MirrorDistance = 0,
                MirrorMaterialOffset = 0
            };

            // UnityMesh生成
            mirrorContext.UnityMesh = mirrorMeshObj.ToUnityMesh();
            mirrorContext.OriginalPositions = (Vector3[])mirrorMeshObj.Positions.Clone();

            return mirrorContext;
        }

        /// <summary>
        /// ミラー側をぶら下げる親を決める。
        ///
        /// スキンド済みモデルでは描画オブジェクトがボーンの子として並ぶ。
        /// 実体側と同じ親（＝実体側のボーン）に付けると、右のメッシュが左のボーンに
        /// ぶら下がる。左右対のボーン（MirrorBoneIndex）が判っていればそちらへ付ける。
        /// スキンド変換が作ったミラーは全てこの並びになっている。
        /// 対応が無ければ実体側と同じ親に落とす。
        /// </summary>
        private static int ResolveMirrorHierarchyParent(MeshContext source, bool skinned)
        {
            int parent = source.HierarchyParentIndex;
            if (!skinned) return parent;

            var model = source.ParentModelContext;
            if (model == null || parent < 0 || parent >= model.MeshContextCount) return parent;

            var parentCtx = model.GetMeshContext(parent);
            if (parentCtx == null || parentCtx.Type != MeshType.Bone) return parent;

            int peer = parentCtx.MirrorBoneIndex;
            if (peer < 0 || peer >= model.MeshContextCount) return parent;

            return peer;
        }

        /// <summary>
        /// 実体側の MeshObject から鏡像の MeshObject を作る。
        ///
        /// 【添字恒等対応】
        ///   result.Vertices[v] ↔ source.Vertices[v]
        ///   result.Faces[f]    ↔ source.Faces[f]（VertexIndices は逆順）
        ///   ミラー側への位相伝播（ApplyToMirrors）が前提にしている形を必ず満たす。
        ///
        /// 【軸・距離】
        ///   鏡映は MirrorPoint / MirrorNormal（軸＋距離）で行う。
        ///   生成ミラーの実効ワールドは ModelContext.ApplyMirrorConjugate が
        ///   S = MirrorMatrix(axis, distance) の共役 S·H·S で解くため、
        ///   ローカル頂点も同じ S で鏡像化されていなければ v_M = S·v_R が崩れる。
        ///   RebakeDerivedMirrorVertices / RebuildDerivedMirrorGeometry も同じ式。
        ///
        ///   ピボット側で距離を吸収する使い方（エクスポート時の GameObject 生成。
        ///   ローカル姿勢を MirrorLocalTRS で鏡像化して親に付ける）では
        ///   distance に 0 を渡すこと。L' = S_d·L·S_0 となるため、
        ///   頂点に掛かるのは原点まわりの反射 S_0 になる。
        /// </summary>
        /// <param name="materialOffset">ミラー側の面に足すマテリアル番号のオフセット。</param>
        /// <param name="name">生成物の名前。null なら source の名前をそのまま使う。</param>
        public static MeshObject BuildMirroredMeshObject(
            MeshObject source, int mirrorAxis, float mirrorDistance,
            int materialOffset = 0, string name = null)
        {
            if (source == null || source.Vertices == null || source.Vertices.Count == 0)
                return null;

            var result = new MeshObject
            {
                Name = name ?? source.Name
            };

            // 頂点をミラー変換してコピー
            foreach (var srcVertex in source.Vertices)
            {
                var mirrorVertex = new Vertex
                {
                    Id = srcVertex.Id,
                    Position = MirrorPoint(mirrorAxis, mirrorDistance, srcVertex.Position)
                };

                // UVをコピー
                foreach (var uv in srcVertex.UVs)
                {
                    mirrorVertex.UVs.Add(uv);
                }

                // 法線をミラー変換してコピー
                foreach (var normal in srcVertex.Normals)
                {
                    mirrorVertex.Normals.Add(MirrorNormal(mirrorAxis, normal));
                }

                // ボーンウェイト: ミラー側があればミラー側、なければ実体側
                if (srcVertex.HasMirrorBoneWeight)
                {
                    mirrorVertex.BoneWeight = srcVertex.MirrorBoneWeight;
                }
                else if (srcVertex.HasBoneWeight)
                {
                    mirrorVertex.BoneWeight = srcVertex.BoneWeight;
                }

                result.Vertices.Add(mirrorVertex);
            }

            // 面をコピー（頂点順序を反転して法線方向を維持）
            foreach (var srcFace in source.Faces)
            {
                var mirrorFace = new Face
                {
                    MaterialIndex = srcFace.MaterialIndex + materialOffset,
                };
                if (srcFace.IsHidden)
                    mirrorFace.SetFlag(FaceFlags.Hidden);

                // 頂点順序を反転（法線方向維持のため）
                //
                // UVIndices / NormalIndices は VertexIndices と同じ長さとは限らない。
                //   ・面ごとの法線を持たないメッシュ … NormalIndices.Count == 0
                //   ・UV を持たないメッシュ           … UVIndices.Count == 0
                // これらを VertexCount で回すと IndexOutOfRange で落ち、
                // ミラー生成が MirrorType だけ立てて中断する（リストに何も増えない）。
                // 各リストを自分の長さで独立に反転する。
                for (int i = srcFace.VertexIndices.Count - 1; i >= 0; i--)
                    mirrorFace.VertexIndices.Add(srcFace.VertexIndices[i]);

                for (int i = srcFace.UVIndices.Count - 1; i >= 0; i--)
                    mirrorFace.UVIndices.Add(srcFace.UVIndices[i]);

                for (int i = srcFace.NormalIndices.Count - 1; i >= 0; i--)
                    mirrorFace.NormalIndices.Add(srcFace.NormalIndices[i]);

                result.Faces.Add(mirrorFace);
            }

            result.InvalidatePositionCache();

            // ミラー側は実体側のウェイト（または MirrorBoneWeight）を引き継ぐ。
            // 引き継いだ結果ウェイトを持つなら、生成したミラーも SkinnedMesh 系である。
            // 種別を確定させないと、実体側だけが SkinningMatrix 経路に乗り、
            // ミラー側が WorldMatrix 経路に落ちて左右で位置がずれる。
            result.RecomputeSkinKind();
            return result;
        }


        /// <summary>
        /// 実体側インデックスに対応するミラー側インデックスを列挙して result に足す。
        ///
        /// 対象は次の2系統。
        ///   MirrorPair … pair.Real が実体側のとき pair.Mirror
        ///   ベイクミラー … BakedMirrorSourceIndex が実体側を指すコンテキスト
        ///
        /// 可視・ロックはミラー側へ伝播させる必要があるが、
        /// 姿勢（SyncDerivedMirrorTransforms）と違って自動で追随する経路が無い。
        /// 呼び出し側で実体側と同じ値を書くために使う。
        /// </summary>
        public static void CollectMirrorPeers(ModelContext model, int realIndex, List<int> result)
        {
            if (model == null || result == null) return;
            var list = model.MeshContextList;
            if (list == null || realIndex < 0 || realIndex >= list.Count) return;

            var realCtx = list[realIndex];
            if (realCtx == null) return;

            // MirrorPair
            var pair = model.GetMirrorPair(realCtx);
            if (pair != null && pair.Real == realCtx && pair.Mirror != null)
            {
                int mi = list.IndexOf(pair.Mirror);
                if (mi >= 0 && !result.Contains(mi)) result.Add(mi);
            }

            // ベイクミラー
            for (int i = 0; i < list.Count; i++)
            {
                var mc = list[i];
                if (mc == null || mc.BakedMirrorSourceIndex != realIndex) continue;
                if (!result.Contains(i)) result.Add(i);
            }
        }

        /// <summary>
        /// 生成ミラー（MirrorGeometryDerived）の頂点を、実体側のローカル頂点から取り直す。
        ///
        /// ミラーの実効ワールドは S·H·S で解くので、v_M = S·v_R が
        /// 「ローカル座標で」成り立っている必要がある。実体側のローカル頂点を
        /// 書き換えたら（再局所化など）必ずこれを呼んで整合を取ること。
        ///
        /// 鏡映の軸・距離は ModelContext の実効ワールド算出と同じ値を使う。
        /// </summary>
        /// <returns>取り直したオブジェクト数</returns>
        public static int RebakeDerivedMirrorVertices(IList<MeshContext> meshContexts)
        {
            if (meshContexts == null) return 0;

            int rebaked = 0;

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var mc = meshContexts[i];
                if (mc == null || !mc.MirrorGeometryDerived) continue;

                int src = mc.BakedMirrorSourceIndex;
                if (src < 0 || src >= meshContexts.Count) continue;

                var realCtx  = meshContexts[src];
                var mirrorMo = mc.MeshObject;
                var realMo   = realCtx?.MeshObject;
                if (mirrorMo?.Vertices == null || realMo?.Vertices == null) continue;
                if (mirrorMo.Vertices.Count != realMo.Vertices.Count) continue;

                int   axis = realCtx.MirrorAxis;
                float dist = realCtx.MirrorDistance;

                for (int v = 0; v < mirrorMo.Vertices.Count; v++)
                {
                    var mv = mirrorMo.Vertices[v];
                    var rv = realMo.Vertices[v];
                    if (mv == null || rv == null) continue;
                    mv.Position = MirrorPoint(axis, dist, rv.Position);
                }
                mirrorMo.InvalidatePositionCache();

                mc.OriginalPositions = (Vector3[])mirrorMo.Positions.Clone();
                mc.ApplyVertexPositionsToMesh();

                rebaked++;
            }

            return rebaked;
        }

        /// <summary>
        /// 生成ミラー（MirrorGeometryDerived）の法線とスロットを、実体側から取り直す。
        ///
        /// 【必要な理由】
        /// RebakeDerivedMirrorVertices は位置しか写さない。実体側の法線を編集しても
        /// ミラー側は生成時の法線を保持したままになるため、法線編集の後に本関数を呼ぶ。
        /// ミラー側の面は選択できないので、実体側の結果を反映する以外に手段が無い。
        ///
        /// 【スロット（分割法線）の扱い】
        /// スロットは丸ごと作り直し、実体側と 1:1 の並びに揃える。
        /// 「角度で再計算」「分離」のようにスロット数が変わる操作の後でも整合が取れる。
        /// 面のインデックスは CreateDerivedMirrorContext と同じく逆順で張り直す
        /// （生成時に頂点順を反転しているため）。
        ///
        /// 【対象】
        /// MirrorGeometryDerived = true のみ。false のミラー側（PMX 系）は
        /// ファイル内に実在する独立メッシュで、実体側と頂点や面の並びが対応する
        /// 保証が無いため触らない。
        /// </summary>
        /// <param name="materialCount">
        /// UnityMesh を作り直す際のサブメッシュ数。-1 なら MeshObject 側の既定に従う。
        /// </param>
        /// <returns>取り直したオブジェクト数</returns>
        public static int RebakeDerivedMirrorNormals(
            IList<MeshContext> meshContexts, int materialCount = -1)
        {
            if (meshContexts == null) return 0;

            int rebaked = 0;

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var mc = meshContexts[i];
                if (mc == null || !mc.MirrorGeometryDerived) continue;

                int src = mc.BakedMirrorSourceIndex;
                if (src < 0 || src >= meshContexts.Count) continue;

                var realCtx  = meshContexts[src];
                var mirrorMo = mc.MeshObject;
                var realMo   = realCtx?.MeshObject;
                if (mirrorMo?.Vertices == null || realMo?.Vertices == null) continue;
                if (mirrorMo.Vertices.Count != realMo.Vertices.Count) continue;
                if (mirrorMo.Faces == null || realMo.Faces == null) continue;
                if (mirrorMo.Faces.Count != realMo.Faces.Count) continue;

                int axis = realCtx.MirrorAxis;

                // --- スロットを実体側と 1:1 で作り直す ---
                // GetOrAddUVNormal は重複をまとめてしまい実体側と番号がずれるため使わない。
                // 実体側の並びをそのまま写し、法線だけ軸で反転する。
                for (int v = 0; v < mirrorMo.Vertices.Count; v++)
                {
                    var mv = mirrorMo.Vertices[v];
                    var rv = realMo.Vertices[v];
                    if (mv == null || rv == null) continue;

                    mv.UVs.Clear();
                    mv.Normals.Clear();

                    for (int s = 0; s < rv.UVs.Count; s++)
                        mv.UVs.Add(rv.UVs[s]);

                    for (int s = 0; s < rv.Normals.Count; s++)
                        mv.Normals.Add(MirrorNormal(axis, rv.Normals[s]));
                }

                // --- 面のインデックスを逆順で張り直す ---
                for (int f = 0; f < mirrorMo.Faces.Count; f++)
                {
                    var mf = mirrorMo.Faces[f];
                    var rf = realMo.Faces[f];
                    if (mf == null || rf == null) continue;

                    int n = rf.VertexCount;
                    if (mf.VertexCount != n) continue;

                    // UV と法線は独立に扱う。以前は「両方そろっていなければ何もしない」
                    // だったため、法線インデックスを持たないメッシュ（MQO 由来で
                    // normalCount==0）では UV の張り直しまで丸ごと飛んでいた。
                    if (rf.UVIndices.Count >= n)
                    {
                        mf.UVIndices.Clear();
                        for (int j = n - 1; j >= 0; j--)
                            mf.UVIndices.Add(rf.UVIndices[j]);
                    }

                    if (rf.NormalIndices.Count >= n)
                    {
                        mf.NormalIndices.Clear();
                        for (int j = n - 1; j >= 0; j--)
                            mf.NormalIndices.Add(rf.NormalIndices[j]);
                    }
                }

                // --- UnityMesh へ反映 ---
                // スロット数が変わっていると法線だけの差し替えが成立しないので作り直す。
                if (mc.UnityMesh == null || !mirrorMo.ApplyNormalsToUnityMesh(mc.UnityMesh))
                    mc.ReplaceUnityMesh(mirrorMo.ToUnityMesh(materialCount));

                rebaked++;
            }

            return rebaked;
        }

        /// <summary>
        /// 生成ミラー（MirrorGeometryDerived）の形状を、実体側から丸ごと作り直す。
        ///
        /// 【必要な理由】
        /// RebakeDerivedMirrorVertices / RebakeDerivedMirrorNormals は
        /// 「頂点数（と面数）が実体側と一致していること」が前提で、頂点や面が
        /// 増減する位相変更には対応できない（両関数とも件数不一致で素通りする）。
        /// 削除を伴うツールを実体側に掛けるとミラー側が古い形状のまま取り残される
        /// ため、位相を変えたら本関数で作り直す。
        ///
        /// 【構築規則】CreateDerivedMirrorContext と同じ。
        ///   ・位置は MirrorPoint（実体側の MirrorAxis / MirrorDistance）
        ///   ・UV はそのまま、法線は MirrorNormal で反転
        ///   ・面は頂点順を反転（法線方向の維持）
        ///   ・マテリアルは実体側 + MirrorMaterialOffset
        ///   ・ボーンウェイトはミラー側があればそれ、無ければ実体側
        ///
        /// 【対象】MirrorGeometryDerived = true のみ。false のミラー側（PMX 系）は
        /// ファイル内に実在する独立メッシュで、実体側と並びが対応する保証が無いため
        /// 触らない。
        /// </summary>
        /// <returns>作り直したオブジェクト数</returns>
        public static int RebuildDerivedMirrorGeometry(IList<MeshContext> meshContexts)
        {
            if (meshContexts == null) return 0;

            int rebuilt = 0;

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var mc = meshContexts[i];
                if (mc == null || !mc.MirrorGeometryDerived) continue;

                int src = mc.BakedMirrorSourceIndex;
                if (src < 0 || src >= meshContexts.Count) continue;

                var realCtx  = meshContexts[src];
                var realMo   = realCtx?.MeshObject;
                var mirrorMo = mc.MeshObject;
                if (realMo?.Vertices == null || mirrorMo == null) continue;

                int   axis      = realCtx.MirrorAxis;
                float dist      = realCtx.MirrorDistance;
                int   matOffset = realCtx.MirrorMaterialOffset;

                // --- 頂点を作り直す ---
                mirrorMo.Vertices.Clear();
                foreach (var rv in realMo.Vertices)
                {
                    if (rv == null) continue;

                    var mv = new Vertex
                    {
                        Id       = rv.Id,
                        Position = MirrorPoint(axis, dist, rv.Position),
                    };

                    for (int s = 0; s < rv.UVs.Count; s++)
                        mv.UVs.Add(rv.UVs[s]);

                    for (int s = 0; s < rv.Normals.Count; s++)
                        mv.Normals.Add(MirrorNormal(axis, rv.Normals[s]));

                    if (rv.HasMirrorBoneWeight)   mv.BoneWeight = rv.MirrorBoneWeight;
                    else if (rv.HasBoneWeight)    mv.BoneWeight = rv.BoneWeight;

                    mirrorMo.Vertices.Add(mv);
                }

                // --- 面を作り直す（頂点順を反転） ---
                mirrorMo.Faces.Clear();
                foreach (var rf in realMo.Faces)
                {
                    if (rf == null) continue;

                    var mf = new Face { MaterialIndex = rf.MaterialIndex + matOffset };
                    if (rf.IsHidden) mf.SetFlag(FaceFlags.Hidden);

                    int n = rf.VertexIndices.Count;
                    for (int j = n - 1; j >= 0; j--)
                    {
                        mf.VertexIndices.Add(rf.VertexIndices[j]);
                        mf.UVIndices.Add(j < rf.UVIndices.Count ? rf.UVIndices[j] : 0);
                        mf.NormalIndices.Add(j < rf.NormalIndices.Count ? rf.NormalIndices[j] : 0);
                    }

                    mirrorMo.Faces.Add(mf);
                }

                mirrorMo.InvalidatePositionCache();

                // 実体側から引き継いだウェイトに合わせて種別を確定させる。
                mirrorMo.RecomputeSkinKind();

                // 消えた頂点・面を指したままの選択を残さない。
                mc.Selection?.ClearAll();

                mc.ReplaceUnityMesh(mirrorMo.ToUnityMesh());
                mc.OriginalPositions = (Vector3[])mirrorMo.Positions.Clone();

                rebuilt++;
            }

            return rebuilt;
        }

        /// <summary>
        /// ModelContext 版。形状を作り直したうえで MirrorPair を張り直す。
        /// 位相が変わると MirrorPair の頂点対応表が古くなるため。
        /// </summary>
        /// <returns>作り直したオブジェクト数</returns>
        public static int RebuildDerivedMirrorGeometry(ModelContext model)
        {
            if (model?.MeshContextList == null) return 0;

            int rebuilt = RebuildDerivedMirrorGeometry(model.MeshContextList);
            if (rebuilt == 0) return 0;

            if (model.MirrorPairs != null)
            {
                foreach (var pair in model.MirrorPairs)
                {
                    if (pair?.Mirror == null || !pair.Mirror.MirrorGeometryDerived) continue;
                    if (!pair.Build())
                        Debug.LogWarning($"[Mirror] ペアの張り直しに失敗しました mirror=\"{pair.Mirror.Name}\"");
                }
            }

            return rebuilt;
        }

        /// <summary>
        /// Undo スナップショットの対象に、実体側とそのミラー側の両方を含めた索引を返す。
        /// 位相変更ツールはミラー側も作り直すため、片側だけ記録すると Undo で食い違う。
        /// </summary>
        public static List<int> CollectMirrorCaptureIndices(
            ModelContext model, IEnumerable<int> realIndices)
        {
            var result = new List<int>();
            if (model == null || realIndices == null) return result;

            foreach (int idx in realIndices)
            {
                if (idx < 0) continue;
                if (!result.Contains(idx)) result.Add(idx);
                CollectMirrorPeers(model, idx, result);
            }

            return result;
        }
    }
}
