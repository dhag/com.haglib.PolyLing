// PolyLingPlayerViewerCore.GeneratedMesh.cs
// Player ビューアのコア：ツール・図形生成が作ったメッシュの受け口と、モデルへの配置
// （新規オブジェクト・既存へ追加・置き換え・新規モデル・材質スロット）。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.Serialization;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.EditorBridge;
using Poly_Ling.View;
using Poly_Ling.MeshListV2;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectArray;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        // ================================================================
        // ツールが生成したメッシュの受け口
        // ================================================================

        /// <summary>
        /// ツールが作った MeshContext を現在のモデルへ追加する。
        /// 名前は既存オブジェクトと衝突しないよう一意化し、選択と Undo は
        /// 図形生成の「新しい描画オブジェクト」と同じ扱いにする。
        /// </summary>
        private void AddMeshContextFromTool(MeshContext ctx)
        {
            if (ctx == null) return;

            var model = ActiveProject?.CurrentModel;
            if (model == null) return;

            string baseName = !string.IsNullOrEmpty(ctx.Name) ? ctx.Name
                            : (!string.IsNullOrEmpty(ctx.MeshObject?.Name) ? ctx.MeshObject.Name : "Mesh");
            string name = model.GenerateUniqueMeshName(baseName);

            ctx.Name = name;
            if (ctx.MeshObject != null) ctx.MeshObject.Name = name;
            if (ctx.UnityMesh  != null) ctx.UnityMesh.name  = name;
            ctx.ParentModelContext = model;

            var oldSelected = model.CaptureAllSelectedIndices();
            int insertIndex = model.Add(ctx);
            model.ComputeWorldMatrices();
            model.SelectMeshContextExclusive(insertIndex);
            model.SelectMesh(insertIndex);
            var newSelected = model.CaptureAllSelectedIndices();

            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(model);
                _editOps.UndoController.RecordMeshContextAdd(
                    ctx, insertIndex, oldSelected, newSelected);
            }

            PrimitiveMeshFinalize(model);
        }

        /// <summary>
        /// ツールが作った MeshObject を編集対象メッシュへマージする。
        /// 図形生成の「既存の描画オブジェクトに追加」と同じ経路を通す。
        /// </summary>
        private void AddMeshObjectToCurrentMeshFromTool(MeshObject meshObject, string meshName)
        {
            if (meshObject == null) return;
            var project = ActiveProject;
            if (project?.CurrentModel == null) return;

            PrimitiveMeshAddToExisting(
                project, meshObject,
                string.IsNullOrEmpty(meshName) ? "Mesh" : meshName,
                Vector3.zero, Vector3.zero, Vector3.one, false, -1);
        }

        /// <summary>
        /// 生成メッシュをモデルへ入れる前に、プロジェクトが無ければ作る。
        ///
        /// ツールハンドラはプロジェクトを保持せず GetProject で都度引くので、
        /// ここで配り直す必要は無い。
        /// </summary>
        private void PrepareHandlersForGeneratedMesh()
        {
            _localLoader.EnsureProject();
        }


        /// <summary>
        /// 「既存の描画オブジェクトに追加」の追加先を解決する。
        ///
        /// addTargetIndex はパネルの名前欄ドロップダウンが返す MeshContextList
        /// インデックス。-1 や範囲外のときは選択オブジェクトリストの先頭
        /// （ModelContext.ActiveMeshContext）へ落とす。
        /// 穴つなぎもこの 1 箇所を通し、図形生成と同じ対象になるようにする。
        /// </summary>
        private static MeshContext ResolveAddTargetMeshContext(ModelContext model, int addTargetIndex)
        {
            if (model == null) return null;
            if (addTargetIndex >= 0)
            {
                var mc = model.GetMeshContext(addTargetIndex);
                if (mc?.MeshObject != null) return mc;
            }
            return model.ActiveMeshContext;
        }

        /// <summary>
        /// 生成メッシュの全面へマテリアルスロット番号を割り当てる。
        ///
        /// materialIndex が負のときは何もしない。藤壺のように元オブジェクトの
        /// MaterialIndex を引き継ぐ図形と、図形生成以外の経路が該当する。
        ///
        /// スロットが 1 つも無いモデルには 1 つ作る。作成は EnsureDefaultMaterialSlot に
        /// 任せる。ここで AddMaterial を直接呼ぶと、PrimitiveMeshFinalize 内の
        /// EnsureDefaultMaterialSlot が「既に 1 件ある」と判断して何もせず、
        /// 名前 "Default" と描画フォールバックと同じ灰(0.7)が付かなくなる。
        ///
        /// 作ったときだけ true を返すので、呼出し側は「作る前のマテリアル一覧」を
        /// Undo へ渡すこと。
        /// </summary>
        private bool ApplyGeneratedMaterialIndex(
            ModelContext model, MeshObject meshObject, int materialIndex,
            GeneratedImageMaterial imageMaterial = null)
        {
            if (imageMaterial != null)
                return ApplyGeneratedImageMaterial(model, meshObject, materialIndex, imageMaterial);

            if (model == null || meshObject == null || materialIndex < 0) return false;

            bool added = false;
            if (model.MaterialCount == 0)
            {
                EnsureDefaultMaterialSlot(model);
                added = model.MaterialCount > 0;
            }

            if (model.MaterialCount == 0) return false;

            int slot = Mathf.Clamp(materialIndex, 0, model.MaterialCount - 1);
            foreach (var f in meshObject.Faces)
            {
                if (f == null) continue;
                f.MaterialIndex = slot;
            }
            return added;
        }

        /// <summary>
        /// 図形生成で作る画像の材質。受け口が画像を 1 回だけ読んで用意する。
        /// </summary>
        private sealed class GeneratedImageMaterial
        {
            /// <summary>読み込み済みのテクスチャ。</summary>
            public Texture2D Texture;
            /// <summary>元ファイルの絶対パス（保存時に textures フォルダへ複製される）。</summary>
            public string FullPath;
            /// <summary>材質スロットの名前。</summary>
            public string Name;
            /// <summary>この材質を割り当てる面の、生成器が入れた仮番号。</summary>
            public int FaceTag;
        }

        /// <summary>
        /// 画像の材質スロットを 1 つ作り、生成器が FaceTag を入れた面へ割り当てる。
        /// それ以外の面は materialIndex のスロット（負ならスロット 0）。
        ///
        /// スロット 0 件のモデルには、先に既定スロットを作る（EnsureDefaultMaterialSlot）。
        /// 材質一覧が必ず変わるので true を返す。呼出し側は「作る前の一覧」を Undo へ渡すこと。
        /// </summary>
        private bool ApplyGeneratedImageMaterial(
            ModelContext model, MeshObject meshObject, int materialIndex, GeneratedImageMaterial img)
        {
            if (model == null || meshObject == null) return false;

            if (model.MaterialCount == 0) EnsureDefaultMaterialSlot(model);
            if (model.MaterialCount == 0) return false;

            // 裏面・側面のスロットは、画像のスロットを足す前の一覧で決める。
            int baseSlot = materialIndex < 0 ? 0 : Mathf.Clamp(materialIndex, 0, model.MaterialCount - 1);

            model.AddMaterial(null);   // 既定 MaterialData（URPLit）でスロット追加
            int texSlot = model.MaterialCount - 1;
            var matRef  = model.GetMaterialReference(texSlot);
            if (matRef?.Data != null)
            {
                matRef.Data.Name = img.Name;
                matRef.InvalidateCache();   // Data から材質を再生成させる
            }

            string prop = Poly_Ling.Materials.MaterialEditOps.MainTexturePropertyName(matRef?.Material);
            string why  = Poly_Ling.Materials.MaterialEditOps.ApplyLoadedTexture(matRef, prop, img.Texture, img.FullPath);
            if (why != null)
                Debug.LogError($"[ImagePlate] 材質へ画像を設定できませんでした（スロット {texSlot}）: {why}");

            foreach (var f in meshObject.Faces)
            {
                if (f == null) continue;
                f.MaterialIndex = f.MaterialIndex == img.FaceTag ? texSlot : baseSlot;
            }
            return true;
        }

        /// <summary>
        /// 図形生成共通: MeshContextを作って返す。
        /// </summary>
        private MeshContext BuildPrimitiveMeshContext(
            MeshObject meshObject, string meshName, Vector3 worldPos,
            Vector3 poseRotation, Vector3 poseScale, bool ignorePoseInArmature)
        {
            var unityMesh = meshObject.ToUnityMesh();
            unityMesh.name      = meshName;
            unityMesh.hideFlags = HideFlags.HideAndDontSave;

            // MeshObject を先に入れる。Name / Type などは MeshObject への委譲プロパティで、
            // 順序を逆にすると（以前はそうだった）一意化した名前が捨てられていた。
            var ctx = new MeshContext
            {
                MeshObject = meshObject,
                Name       = meshName,
                UnityMesh  = unityMesh,
                IsVisible  = true,
            };

            // 図形生成側でベイクしなかった回転 / スケールは描画オブジェクトの姿勢に入れる。
            bool hasPose = worldPos != Vector3.zero
                        || poseRotation != Vector3.zero
                        || poseScale != Vector3.one;
            if (ctx.BoneTransform != null && hasPose)
            {
                ctx.BoneTransform.UseLocalTransform = true;
                ctx.BoneTransform.Position = worldPos;
                ctx.BoneTransform.Rotation = poseRotation;
                ctx.BoneTransform.Scale    = poseScale;
            }

            ctx.IgnorePoseInArmature = ignorePoseInArmature;
            return ctx;
        }

        /// <summary>
        /// モード1: 新しい描画オブジェクトとして現在のモデルに追加。UNDO対応。
        /// </summary>
        private int PrimitiveMeshCreateNewObject(
            ProjectContext project, MeshObject meshObject, string meshName,
            Vector3 worldPos, Vector3 poseRotation, Vector3 poseScale,
            bool ignorePoseInArmature, int materialIndex = -1,
            GeneratedImageMaterial imageMaterial = null)
        {
            var model = project.CurrentModel;
            if (model == null) return -1;

            // 既存の描画オブジェクトと名前が衝突しないようにしてから作る。
            meshName = model.GenerateUniqueMeshName(meshName);

            // マテリアル割当。スロットを作るのは 0 件のときと、画像の材質を作るときだけ。
            // 作る可能性があるときだけ「作る前の一覧」を取る。
            // 指定が無いときは Materials に触れない（MaterialReference の実体化を避ける）。
            bool mayAddSlot       = imageMaterial != null || (materialIndex >= 0 && model.MaterialCount == 0);
            var  oldMaterials     = mayAddSlot ? new List<Material>(model.Materials) : null;
            int  oldMaterialIndex = model.CurrentMaterialIndex;
            bool matSlotAdded     = ApplyGeneratedMaterialIndex(model, meshObject, materialIndex, imageMaterial);

            // 梯子の行列（段 10）。生成物は梯子の格納空間にあるので、梯子の 格納→バインド を掛けて
            // モデル空間へ置く。置き方（位置・焼かなかった回転・拡大）は出力の姿勢が持つので、
            // 出力の枠はルート（単位）。作り直し（ModifierSpace.TryComputeMatrix の poseOwnedByOutput）と揃える。
            if (_createBeltSource != null)
            {
                model.ComputeWorldMatrices();
                Poly_Ling.Ops.ModifierSpace.ApplyMatrix(meshObject, _createBeltSource.VertexToBindWorldMatrix);
            }

            var ctx = BuildPrimitiveMeshContext(meshObject, meshName, worldPos,
                poseRotation, poseScale, ignorePoseInArmature);
            ctx.ParentModelContext = model;

            var oldSelected = model.CaptureAllSelectedIndices();
            int insertIndex = model.Add(ctx);
            model.ComputeWorldMatrices();
            model.SelectMeshContextExclusive(insertIndex);
            model.SelectMesh(insertIndex);
            var newSelected = model.CaptureAllSelectedIndices();

            // UNDO記録。マテリアルスロットを作った場合だけ、その前後も同じレコードへ入れる。
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(model);
                _editOps.UndoController.RecordMeshContextAdd(
                    ctx, insertIndex, oldSelected, newSelected,
                    null, null,
                    matSlotAdded ? oldMaterials : null,
                    oldMaterialIndex,
                    matSlotAdded ? new List<Material>(model.Materials) : null,
                    matSlotAdded ? model.CurrentMaterialIndex : 0);
            }

            PrimitiveMeshFinalize(model);

            // 生成物の位置。呼び出し側が CommandResult へ載せる。
            return insertIndex;
        }

        /// <summary>
        /// モード2: 既存の選択中描画オブジェクトに頂点・面をマージ。UNDO対応。
        /// 描画オブジェクトが存在しない場合はモード1にフォールバック。
        /// </summary>
        private int PrimitiveMeshAddToExisting(
            ProjectContext project, MeshObject meshObject, string meshName,
            Vector3 worldPos, Vector3 poseRotation, Vector3 poseScale,
            bool ignorePoseInArmature, int addTargetIndex = -1, int materialIndex = -1,
            GeneratedImageMaterial imageMaterial = null)
        {
            var model  = project.CurrentModel;
            if (model == null) return -1;

            // 追加先はパネルの名前欄ドロップダウンで選んだオブジェクト。
            // -1（未選択・未配線）のときだけ従来どおり選択オブジェクトリストの先頭。
            var targetMc = ResolveAddTargetMeshContext(model, addTargetIndex);
            if (targetMc == null || targetMc.MeshObject == null)
            {
                return PrimitiveMeshCreateNewObject(project, meshObject, meshName, worldPos,
                    poseRotation, poseScale, ignorePoseInArmature, materialIndex, imageMaterial);
            }

            // 既存オブジェクトへのマージでは姿勢を持てないため、
            // ベイクされずに渡ってきた回転 / スケールはここで頂点へ焼き込む。
            var srcObject = meshObject;
            bool hasPose = poseRotation != Vector3.zero || poseScale != Vector3.one;
            if (hasPose)
            {
                srcObject = meshObject.Clone();
                Poly_Ling.PrimitiveMesh.PrimitiveMeshTransform.ApplyRotationScale(
                    srcObject, poseRotation, poseScale);
            }

            // ワールド位置オフセットを頂点に適用
            if (worldPos != Vector3.zero)
            {
                if (ReferenceEquals(srcObject, meshObject)) srcObject = meshObject.Clone();
                foreach (var v in srcObject.Vertices)
                    v.Position += worldPos;
            }

            // 梯子の行列（段 10）。作り直し（ReplaceExisting：姿勢・位置を焼いたあとに M）と同じ順で掛ける。
            //   M ＝（追加先の バインド→格納）×（梯子の 格納→バインド）
            if (_createBeltSource != null)
            {
                if (ReferenceEquals(srcObject, meshObject)) srcObject = meshObject.Clone();
                model.ComputeWorldMatrices();
                if (Poly_Ling.Ops.ModifierSpace.TryComputeMatrix(_createBeltSource, targetMc, false, out var beltM))
                    Poly_Ling.Ops.ModifierSpace.ApplyMatrix(srcObject, beltM);
            }

            // UNDO: 変更前スナップショット
            //   書き込み先は targetMc を明示する（SetMeshObjectFor）。SetMeshObject は
            //   書き込み先が先頭の選択メッシュになるため、選択中の別オブジェクトの
            //   MeshContext へ targetMc の MeshObject を入れてしまい、2 つの描画
            //   オブジェクトが同じ MeshObject を共有する。明示は記録の後で解除する。
            MeshObjectSnapshot before = null;
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetMeshObjectFor(targetMc, targetMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = model;
                before = _editOps.UndoController.CaptureMeshObjectSnapshotOf(targetMc);
            }

            // マテリアル割当。MeshObjectSnapshot は Materials も保持するので、
            // スロットを作る可能性のあるこの処理は必ず before 捕獲の後に行う。
            // 面を書き換える対象は、マージで実際に読まれる srcObject 側。
            ApplyGeneratedMaterialIndex(model, srcObject, materialIndex, imageMaterial);

            // 部品IDは追加先の空き番号へずらす。生成物が内部で複数パーツに分かれている
            // （フリル・パイプ・藤壺）場合も、内部の構成を保ったまま全体を平行移動させる。
            // 直前の ApplyGeneratedMaterialIndex と同じく srcObject を直接書き換える
            // （頂点は下のマージで Clone() されるため、ここで複製する必要はない）。
            Poly_Ling.Ops.PartsIdOps.OffsetPartsId(
                srcObject, Poly_Ling.Ops.PartsIdOps.NextPartsId(targetMc.MeshObject));

            // マージ
            int baseVertIdx = targetMc.MeshObject.VertexCount;
            foreach (var v in srcObject.Vertices)
                targetMc.MeshObject.Vertices.Add(v.Clone());
            foreach (var f in srcObject.Faces)
            {
                var newFace = new Face();
                newFace.VertexIndices  = f.VertexIndices.ConvertAll(i => i + baseVertIdx);
                newFace.UVIndices      = new System.Collections.Generic.List<int>(f.UVIndices);
                newFace.NormalIndices  = new System.Collections.Generic.List<int>(f.NormalIndices);
                newFace.MaterialIndex  = f.MaterialIndex;
                targetMc.MeshObject.Faces.Add(newFace);
            }

            // サブIDは連結後の並びで、部品IDごとに 0 から振り直す。
            Poly_Ling.Ops.PartsIdOps.AssignSubIdByPartsId(targetMc.MeshObject);

            // UnityMesh再構築
            var newUnityMesh = targetMc.MeshObject.ToUnityMesh();
            newUnityMesh.name      = targetMc.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            // Object.Destroy は edit mode では破棄しない。ReplaceUnityMesh は
            // MeshContext.DestroyMesh 経由で isPlaying を見て使い分ける。
            targetMc.ReplaceUnityMesh(newUnityMesh);

            // UNDO: 変更後スナップショット記録
            if (_editOps?.UndoController != null && before != null)
            {
                var after = _editOps.UndoController.CaptureMeshObjectSnapshotOf(targetMc);
                _editOps.UndoController.RecordTopologyChange(before, after, $"Add Primitive to {targetMc.Name}");
            }
            _editOps?.UndoController?.ClearTargetMeshContext();

            model.ComputeWorldMatrices();
            PrimitiveMeshFinalize(model);

            // マージ先の位置。呼び出し側が CommandResult へ載せる。
            return model.IndexOf(targetMc);
        }

        /// <summary>
        /// モード4: 既存の描画オブジェクトの中身を捨てて、生成物で置き換える。
        ///
        /// 【なぜ新規オブジェクトを作らないか】
        ///   オブジェクトグループの作り直しで使う。新しく作ると
        ///   ObjectId が変わり、名前・階層・姿勢・材質割当も引き継げない。
        ///   出力先を指している参照（グループ自身・階層の子・ミラー元）が
        ///   そのたびに切れることになる。中身だけ入れ替えれば全部そのまま残る。
        ///
        /// 【頂点IDは残らない】
        ///   中身を総入れ替えするので、出力先へ手で振った頂点IDは失われる。
        ///   出力先にIDを振るなら、先にグループを解除すること。
        ///
        /// 【姿勢の扱い】
        ///   AddToExisting と同じく、対象の姿勢はそのまま使い、渡ってきた
        ///   回転・拡大・平行移動は頂点へ焼き込む。
        /// </summary>
        /// <param name="masterIndex">置き換えた描画オブジェクトの位置。失敗時は -1。</param>
        /// <returns>失敗理由。成功時は null。</returns>
        /// <summary>ApplyReplaceTransforms を掛ける必要があるか。</summary>
        private static bool NeedsReplaceTransforms(
            Vector3 worldPos, Vector3 poseRotation, Vector3 poseScale,
            MeshContext targetMc, Poly_Ling.Ops.ModifierSpace.WriteBack spaceWb)
            => poseRotation != Vector3.zero || poseScale != Vector3.one || worldPos != Vector3.zero
            || spaceWb != null || targetMc.IsSkinned;

        /// <summary>
        /// 中身の入れ替えの前に生成物へ掛ける変換（mo をその場で書き換える）。
        ///   1. 渡ってきた姿勢（回転・拡大）を焼き込む（対象の姿勢は変えない）
        ///   2. 位置を足す
        ///   3. スキンド化のあとの評価空間（段 5。ModifierSpace）：行列 M で出力の格納空間へ移し、
        ///      出力の種別に合わせてウェイトを捨てる・欠けを検査する
        /// 書き戻し（PrimitiveMeshReplaceExisting）と逆方向の作り直しの見本（BuildModifierTemplate。段 6）が
        /// 同じ関数を通す。戻り値は失敗理由。
        /// </summary>
        private static string ApplyReplaceTransforms(
            MeshObject mo, Vector3 worldPos, Vector3 poseRotation, Vector3 poseScale,
            MeshContext targetMc, Poly_Ling.Ops.ModifierSpace.WriteBack spaceWb, bool checkWeights = true)
        {
            if (poseRotation != Vector3.zero || poseScale != Vector3.one)
                Poly_Ling.PrimitiveMesh.PrimitiveMeshTransform.ApplyRotationScale(mo, poseRotation, poseScale);
            if (worldPos != Vector3.zero)
                Poly_Ling.PrimitiveMesh.PrimitiveMeshTransform.Apply(mo, worldPos, Vector3.zero, Vector3.one);
            if (spaceWb != null || targetMc.IsSkinned)
                return Poly_Ling.Ops.ModifierSpace.PrepareForOutput(mo, targetMc, spaceWb, checkWeights);
            return null;
        }

        private string PrimitiveMeshReplaceExisting(
            ProjectContext project, MeshObject meshObject,
            Vector3 worldPos, Vector3 poseRotation, Vector3 poseScale,
            int targetIndex, int materialIndex, out int masterIndex,
            GeneratedImageMaterial imageMaterial = null)
        {
            masterIndex = -1;

            var model = project?.CurrentModel;
            if (model == null) return "モデルがありません";

            var targetMc = ResolveAddTargetMeshContext(model, targetIndex);
            if (targetMc == null || targetMc.MeshObject == null)
                return "置き換え先の描画オブジェクトが見つかりません";

            // 渡ってきた姿勢・位置を頂点へ焼き込み、段 5 の評価空間へ移す（ApplyReplaceTransforms）。
            // 中身を入れ替える前に済ませ、失敗なら何も書かない。元のメッシュは書き換えない（複製に掛ける）。
            var spaceWb = Poly_Ling.Ops.ModifierSpace.Current;
            var srcObject = meshObject;
            if (NeedsReplaceTransforms(worldPos, poseRotation, poseScale, targetMc, spaceWb))
            {
                srcObject = meshObject.Clone();
                string spaceWhy = ApplyReplaceTransforms(srcObject, worldPos, poseRotation, poseScale, targetMc, spaceWb);
                if (spaceWhy != null) return spaceWhy;
            }

            // UNDO: 変更前スナップショット
            //   書き込み先は targetMc を明示する（SetMeshObjectFor）。SetMeshObject は
            //   書き込み先が先頭の選択メッシュになるため、選択中の別オブジェクトの
            //   MeshContext へ targetMc の MeshObject を入れてしまい、2 つの描画
            //   オブジェクトが同じ MeshObject を共有する。明示は記録の後で解除する。
            //   記録を止めているとき（オブジェクトグループの作り直し・依存の評価）は書き込み先も設定しない。
            //   SetMeshObjectFor は頂点編集の履歴を消す（MeshUndoController.SetMeshObjectFor）ので、
            //   入力の編集の直後に評価が下流を書き戻すと、その編集の Undo が消える（段 4）。
            MeshObjectSnapshot before = null;
            bool recordReplace = _editOps?.UndoController != null && !_editOps.UndoController.IsRecordingSuspended;
            if (recordReplace)
            {
                _editOps.UndoController.SetMeshObjectFor(targetMc, targetMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = model;
                before = _editOps.UndoController.CaptureMeshObjectSnapshotOf(targetMc);
            }

            // マテリアル割当。MeshObjectSnapshot は Materials も保持するので、
            // スロットを作る可能性のあるこの処理は必ず before 捕獲の後に行う。
            ApplyGeneratedMaterialIndex(model, srcObject, materialIndex, imageMaterial);

            // ── 中身の入れ替え
            //    MeshObject の実体は差し替えず、頂点と面だけを入れ替える。
            //    実体を差し替えると Type / Depth / HierarchyParentIndex /
            //    BoneTransform など MeshObject 側に委譲している属性まで
            //    生成物のもの（既定値）になってしまう。
            var dst = targetMc.MeshObject;
            dst.Vertices.Clear();
            dst.Faces.Clear();
            foreach (var v in srcObject.Vertices) dst.Vertices.Add(v.Clone());
            foreach (var f in srcObject.Faces)
            {
                var nf = new Face
                {
                    VertexIndices = new System.Collections.Generic.List<int>(f.VertexIndices),
                    UVIndices     = new System.Collections.Generic.List<int>(f.UVIndices),
                    NormalIndices = new System.Collections.Generic.List<int>(f.NormalIndices),
                    MaterialIndex = f.MaterialIndex,
                };
                dst.Faces.Add(nf);
            }
            // 線分群も生成物のものへ入れ替える（面と連携しているため、古いものを残さない）。
            dst.LineGroups = Poly_Ling.Ops.LineGroupOps.CloneList(srcObject);
            dst.RebuildIdSets();

            // サブIDは振り直さない。中身を総入れ替えするので、生成物の値（図形ごとの決まりで
            // PrimitiveMeshFactory.AssignPartsIds が振った値。藤壺・まつげは配置元のサブIDのまま）を
            // そのまま使う。新規オブジェクト（PrimitiveMeshCreateNewObject）と同じにするため
            // （振り直すと、作り直しのたびに新規作成と違うサブIDになっていた。作り直し整備計画 段 7）。

            // UnityMesh 再構築
            var newUnityMesh = dst.ToUnityMesh();
            newUnityMesh.name      = targetMc.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            targetMc.ReplaceUnityMesh(newUnityMesh);

            // UNDO: 変更後スナップショット記録
            if (recordReplace && before != null)
            {
                var after = _editOps.UndoController.CaptureMeshObjectSnapshotOf(targetMc);
                _editOps.UndoController.RecordTopologyChange(
                    before, after, $"Replace Contents of {targetMc.Name}");
            }
            if (recordReplace) _editOps.UndoController.ClearTargetMeshContext();

            // 置き換え先がミラー対の実体側なら、ミラー側も実体側から作り直す。
            ReplaceMirrorSideContents(model, targetMc);

            model.ComputeWorldMatrices();
            PrimitiveMeshFinalize(model);

            // 置き換え先の位置。呼び出し側が CommandResult へ載せる。
            masterIndex = model.IndexOf(targetMc);
            return null;
        }

        /// <summary>
        /// 中身を入れ替えた実体側に合わせて、ミラー側の中身を作り直す。
        ///
        /// 【なぜ要るか】
        ///   ReplaceExisting（オブジェクトグループの作り直し）は実体側の頂点・面を
        ///   丸ごと入れ替えるが、ミラー側は自分の頂点を持つ別の描画オブジェクトで、
        ///   放っておくと古い形と古いウェイトのまま残る。スキンド化の後は
        ///   ミラー側が MirrorGeometryDerived = false になるので、
        ///   RebuildDerivedMirrorGeometry の対象にもならない。
        ///   例：パイプの作り直しで実体側ははしごのウェイトを引き継ぐのに、
        ///   ミラー側はスキンド化が書いた「自分のボーン 1.0」のまま動かなかった。
        ///
        /// 【作り方】
        ///   形は生成ミラーと同じ BuildMirroredMeshObject で作る（添字恒等対応が保たれる）。
        ///   中身を入れ替えたので MirrorPair の頂点対応表を張り直し、
        ///   実体側がスキンドなら対のボーンへ写したウェイトをミラー側へ書く
        ///   （SyncBoneWeights。ミラーの有効化 EnableMirror と同じ手順）。
        ///   描画オブジェクト自体は差し替えないので、名前・階層・ObjectId は残る。
        /// </summary>
        private void ReplaceMirrorSideContents(ModelContext model, MeshContext realCtx)
        {
            var pair = model?.GetMirrorPair(realCtx);
            if (pair == null || !ReferenceEquals(pair.Real, realCtx)) return;

            var mirrorCtx = pair.Mirror;
            var mirrorMo  = mirrorCtx?.MeshObject;
            var realMo    = realCtx.MeshObject;
            if (mirrorMo == null || realMo == null) return;

            int axis = MirrorBranchOps.ResolveMirrorAxis(realCtx);
            var built = MirrorBranchOps.BuildMirroredMeshObject(
                realMo, axis, realCtx.MirrorDistance, realCtx.MirrorMaterialOffset, mirrorCtx.Name);
            if (built == null) return;

            // 記録を止めているとき（作り直し・依存の評価）は書き込み先を設定しない。
            // SetMeshObjectFor は頂点編集の履歴を消す（段 5。本体側の ReplaceExisting と同じ扱い）。
            var undo = _editOps?.UndoController;
            if (undo != null && undo.IsRecordingSuspended) undo = null;
            MeshObjectSnapshot before = null;
            if (undo != null)
            {
                undo.SetMeshObjectFor(mirrorCtx, mirrorCtx.UnityMesh);
                undo.MeshUndoContext.ParentModelContext = model;
                before = undo.CaptureMeshObjectSnapshotOf(mirrorCtx);
            }

            mirrorMo.Vertices.Clear();
            mirrorMo.Faces.Clear();
            foreach (var v in built.Vertices) mirrorMo.Vertices.Add(v);
            foreach (var f in built.Faces)    mirrorMo.Faces.Add(f);
            mirrorMo.LineGroups = LineGroupOps.CloneList(realMo);
            LineGroupOps.MirrorHandles(mirrorMo.LineGroups, n => MirrorBranchOps.MirrorNormal(axis, n));
            mirrorMo.RebuildIdSets();
            mirrorMo.InvalidatePositionCache();

            // 消えた頂点・面を指したままの選択を残さない。
            mirrorCtx.Selection?.ClearAll();

            if (pair.Build(model.MeshContextList))
            {
                if (realCtx.IsSkinned) pair.SyncBoneWeights();
            }
            else
            {
                Debug.LogWarning($"[Mirror] 作り直し後のペアの張り直しに失敗しました mirror=\"{mirrorCtx.Name}\"\n{pair.BuildLog}");
            }
            mirrorMo.RecomputeSkinKind();

            var newUnityMesh = mirrorMo.ToUnityMesh();
            newUnityMesh.name      = mirrorCtx.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            mirrorCtx.ReplaceUnityMesh(newUnityMesh);
            mirrorCtx.OriginalPositions = (Vector3[])mirrorMo.Positions.Clone();

            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshotOf(mirrorCtx);
                undo.RecordTopologyChange(before, after, $"Replace Contents of {mirrorCtx.Name} (mirror)");
            }
            undo?.ClearTargetMeshContext();
        }

        /// <summary>
        /// モード3: 新しいモデルを作って描画オブジェクトを追加。UNDO対応（メッシュ追加のみ）。
        /// </summary>
        /// <returns>新モデル内での位置。作れなかったときは -1。</returns>
        private int PrimitiveMeshCreateNewModel(
            ProjectContext project, MeshObject meshObject, string meshName,
            Vector3 worldPos, Vector3 poseRotation, Vector3 poseScale,
            bool ignorePoseInArmature, int materialIndex = -1,
            GeneratedImageMaterial imageMaterial = null)
        {
            var newModel = project.CreateNewModel(meshName);
            if (newModel == null) return -1;

            // 新規モデルなので通常は衝突しないが、経路を揃えるため同じ一意化を通す。
            meshName = newModel.GenerateUniqueMeshName(meshName);

            // 新規モデルはマテリアルスロットが 0 件なので、指定があれば 1 つ作る。
            // 画像の材質を作るときはさらに 1 つ足す。作る前の一覧は必ず空。
            // 指定が無いときは Materials に触れない。
            bool mayAddSlot       = imageMaterial != null || (materialIndex >= 0 && newModel.MaterialCount == 0);
            var  oldMaterials     = mayAddSlot ? new List<Material>(newModel.Materials) : null;
            int  oldMaterialIndex = newModel.CurrentMaterialIndex;
            bool matSlotAdded     = ApplyGeneratedMaterialIndex(newModel, meshObject, materialIndex, imageMaterial);

            var ctx = BuildPrimitiveMeshContext(meshObject, meshName, worldPos,
                poseRotation, poseScale, ignorePoseInArmature);
            ctx.ParentModelContext = newModel;

            var oldSelected = newModel.CaptureAllSelectedIndices();
            int insertIndex = newModel.Add(ctx);
            newModel.ComputeWorldMatrices();
            newModel.SelectMeshContextExclusive(insertIndex);
            newModel.SelectMesh(insertIndex);
            var newSelected = newModel.CaptureAllSelectedIndices();

            // UNDO記録（新モデル上のメッシュ追加）。
            // マテリアルスロットを作った場合だけ、その前後も同じレコードへ入れる。
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(newModel);
                _editOps.UndoController.RecordMeshContextAdd(
                    ctx, insertIndex, oldSelected, newSelected,
                    null, null,
                    matSlotAdded ? oldMaterials : null,
                    oldMaterialIndex,
                    matSlotAdded ? new List<Material>(newModel.Materials) : null,
                    matSlotAdded ? newModel.CurrentMaterialIndex : 0);
            }

            PrimitiveMeshFinalize(newModel);
            RebuildModelList();

            // 新モデル内での位置。呼び出し側が CommandResult へ載せる。
            return insertIndex;
        }

        /// <summary>
        /// 図形生成後の共通ビュー更新処理。
        /// </summary>
        private void PrimitiveMeshFinalize(ModelContext model)
        {
            // 材質0件のモデルへ基本図形を追加したとき、描画は GetDefaultMaterial()（灰0.7）へ
            // フォールバックするだけで材質リストには何も入らない。マテリアルパネルで編集できるよう、
            // 初回0件時のみ同じ灰の既定スロットを1つ生成する（見た目は変えない）。
            EnsureDefaultMaterialSlot(model);

            // Phase 2a-2b-2 Batch 3: RebuildAdapter + SetSelectionState + UpdateSelectedDrawableMesh を
            // EnterSceneReset に集約。カメラは別途 NotifyCameraChanged で個別に呼ぶ。
            _viewportManager.EnterSceneReset(ActiveProject);
            _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);

            RebuildModelList();
            NotifyPanels(ChangeKind.ListStructure);
        }

        /// <summary>
        /// 材質0件のモデルに、描画フォールバック(GetDefaultMaterial)と同じ灰(0.7)の
        /// 既定材質スロットを1つ生成する。既に1件以上あれば何もしない。
        /// </summary>
        private void EnsureDefaultMaterialSlot(ModelContext model)
        {
            if (model == null || model.MaterialCount > 0) return;

            model.AddMaterial(null);   // 既定 MaterialData（URPLit）でスロット追加
            var matRef = model.GetMaterialReference(0);
            if (matRef?.Data != null)
            {
                matRef.Data.Name = "Default";
                matRef.Data.SetBaseColor(new Color(0.7f, 0.7f, 0.7f, 1f));
                matRef.InvalidateCache();   // Data から材質を再生成させる
            }
            model.CurrentMaterialIndex = 0;
        }
    }
}
