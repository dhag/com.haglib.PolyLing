// PlayerCommandDispatcher.ObjectOrigin.cs
// コマンドディスパッチャ：オブジェクト原点の一括設定と姿勢くさび。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectPose;
using Poly_Ling.Ops;
using Poly_Ling.UI;
using Poly_Ling.Diagnostics;
using Poly_Ling.Serialization;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        // ================================================================
        // オブジェクト原点の一括設定
        // ================================================================

        /// <summary>
        /// 名前一致したメッシュの原点を設定する。
        /// 「原点だけ移動」と同じく、自頂点を再局所化して見た目を保つ。子は動かさない。
        /// </summary>
        /// <summary>
        /// 選択中メッシュのローカル拡大縮小を頂点位置へ畳み込み、Scale を (1,1,1) に戻す。
        ///
        /// ベイクできない対象はスキップし、その理由を message に列挙して返す。
        ///   - UseLocalTransform が false: LocalMatrix が identity で Scale が効いていない
        ///   - Scale が (1,1,1): 変化なし
        ///   - MeshType.Bone: 頂点を持たない
        ///   - 子を持つ: 子の world は「親World × 子Local」で決まるため、親のスケールを
        ///     外すと子がずれる。非一様スケール×子の回転がある場合は子側の TRS で補正できない
        ///   - スキンドメッシュ: 描画が SkinningMatrix 経由で自身の WorldMatrix を使わないため、
        ///     ベイクすると見た目が変わる
        /// </summary>
        /// <returns>1件でもベイクしたら true。</returns>
        public bool BakeObjectScale(ModelContext model, out string message)
        {
            message = "";
            if (model == null) { message = "拡大縮小をベイク: モデルがありません"; return false; }

            var selected = model.SelectedDrawableMeshIndices;
            if (selected == null || selected.Count == 0)
            {
                message = "拡大縮小をベイク: 対象が選択されていません";
                return false;
            }

            // HierarchyParentIndex で子の有無を判定する（ComputeWorldMatrices が参照する親）。
            var hasChild = new HashSet<int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var c = model.GetMeshContext(i);
                if (c == null) continue;
                int hp = c.HierarchyParentIndex;
                if (hp >= 0) hasChild.Add(hp);
            }

            var targets     = new List<int>();
            var skipNoLocal = new List<string>();
            var skipUnit    = new List<string>();
            var skipBone    = new List<string>();
            var skipChild   = new List<string>();
            var skipSkin    = new List<string>();

            foreach (int idx in selected)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                string nm = string.IsNullOrEmpty(mc.Name) ? $"#{idx}" : mc.Name;

                if (mc.Type == MeshType.Bone)                        { skipBone.Add(nm);    continue; }
                if (mc.BoneTransform == null ||
                    !mc.BoneTransform.UseLocalTransform)             { skipNoLocal.Add(nm); continue; }
                if (mc.BoneTransform.Scale == Vector3.one)           { skipUnit.Add(nm);    continue; }
                if (hasChild.Contains(idx))                          { skipChild.Add(nm);   continue; }

                // 種別（SkinnedMesh 系か）で弾く。実頂点のウェイト有無ではない。
                if (mc.IsSkinned) { skipSkin.Add(nm); continue; }

                targets.Add(idx);
            }

            var before = new Dictionary<int, ObjectScaleSnapshot>();
            var after  = new Dictionary<int, ObjectScaleSnapshot>();

            foreach (int idx in targets)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc.MeshObject;

                var oldVerts = new Vector3[mo.Vertices.Count];
                for (int v = 0; v < mo.Vertices.Count; v++) oldVerts[v] = mo.Vertices[v].Position;
                before[idx] = new ObjectScaleSnapshot
                {
                    Scale           = mc.BoneTransform.Scale,
                    VertexPositions = oldVerts,
                };

                Vector3 sc = mc.BoneTransform.Scale;
                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    var vert = mo.Vertices[v];
                    vert.Position = Vector3.Scale(vert.Position, sc);
                    mo.Vertices[v] = vert;
                }
                mo.InvalidatePositionCache();

                mc.BoneTransform.Scale = Vector3.one;

                var newVerts = new Vector3[mo.Vertices.Count];
                for (int v = 0; v < mo.Vertices.Count; v++) newVerts[v] = mo.Vertices[v].Position;
                after[idx] = new ObjectScaleSnapshot
                {
                    Scale           = Vector3.one,
                    VertexPositions = newVerts,
                };
            }

            // 警告メッセージ組み立て（スキップ理由ごとに件数と名前）
            var warn = new List<string>();
            void AddWarn(string reason, List<string> names)
            {
                if (names.Count == 0) return;
                warn.Add($"{reason}{names.Count}件({string.Join(",", names)})");
            }
            AddWarn("ローカル変換無効:", skipNoLocal);
            AddWarn("等倍:",             skipUnit);
            AddWarn("ボーン:",           skipBone);
            AddWarn("子あり:",           skipChild);
            AddWarn("スキン済み:",       skipSkin);

            if (targets.Count == 0)
            {
                message = "拡大縮小をベイク: 適用0件" +
                          (warn.Count > 0 ? " / スキップ " + string.Join(" ", warn) : "");
                if (warn.Count > 0) Debug.LogWarning("[BakeObjectScale] " + message);
                return false;
            }

            model.ComputeWorldMatrices();

            if (_undoController != null)
            {
                _undoController.SetModelContext(model);
                _undoController.MeshListStack.Record(
                    new ObjectScaleBakeRecord(before, after, "拡大縮小をベイク"), "拡大縮小をベイク");
                _undoController.FocusMeshList();
            }

            model.IsDirty = true;
            model.OnListChanged?.Invoke();
            _viewportManager.EnterTopologyChanged(_getProject());
            _notifyPanels(ChangeKind.Attributes);

            message = $"拡大縮小をベイク: 適用{targets.Count}件" +
                      (warn.Count > 0 ? " / スキップ " + string.Join(" ", warn) : "");
            if (warn.Count > 0) Debug.LogWarning("[BakeObjectScale] " + message);
            return true;
        }

        private void ApplyObjectOrigins(ModelContext model, ApplyObjectOriginsCommand cmd)
        {
            if (cmd?.Names == null || cmd.Positions == null) return;

            // 診断（既定は無効）。検証パネルが実行中だけ立てる。
            Poly_Ling.Diagnostics.ObjectOriginDiag.Begin("ApplyObjectOrigins");
            if (Poly_Ling.Diagnostics.ObjectOriginDiag.Enabled)
            {
                // 実値の階層をそのまま控える。MQO の depth からの推定ではなく、
                // 適用時点で ComputeWorldMatrices が使う HierarchyParentIndex を記録する。
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null) continue;

                    var e = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(i);
                    e.Name                 = mc.Name ?? "";
                    e.Type                 = mc.Type.ToString();
                    e.VertexCount          = mc.MeshObject?.VertexCount ?? 0;
                    e.HierarchyParentIndex = mc.HierarchyParentIndex;

                    int p = mc.HierarchyParentIndex;
                    int guard = 0;
                    while (p >= 0 && p < model.MeshContextCount && guard < 256)
                    {
                        e.Ancestors.Add(p);
                        p = model.GetMeshContext(p)?.HierarchyParentIndex ?? -1;
                        guard++;
                    }
                }
            }

            // 名前 → インデックス（重複名は先着）
            // 姿勢くさびは書出側で除外しているので、読込側でも適用先にしない。
            // 既存の（くさび行を含む）CSV を読んでも巻き込まないようにする。
            var wedgeIndices = ObjectPoseWedgeReader.CollectWedgeIndices(model);

            var indexByName = new Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type == MeshType.Bone) continue;
                // ミラー側は実体側と BoneTransform を共有するので適用先にしない
                // （別の原点を持たせると v_M = S·v_R が崩れる）
                if (mc.Type == MeshType.MirrorSide || mc.Type == MeshType.BakedMirror) continue;
                if (wedgeIndices.Contains(i)) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (!indexByName.ContainsKey(mc.Name)) indexByName[mc.Name] = i;
            }

            // 適用対象を決める。
            // CSV に載っていないオブジェクトは targets に入らないので触らない。
            // 逆にモデルに無い名前も黙って飛ばす（どちらもエラーにしない）。
            var targets = new List<(int index, Vector3 pos, Vector3? rot)>();
            var missing = new List<string>();

            int n = Mathf.Min(cmd.Names.Length, cmd.Positions.Length);
            for (int i = 0; i < n; i++)
            {
                string name = cmd.Names[i];
                if (string.IsNullOrEmpty(name)) continue;

                // 回転は任意。配列が無い / 行に指定が無い場合は元の回転を保つ。
                Vector3? rot = (cmd.Rotations != null && i < cmd.Rotations.Length)
                    ? cmd.Rotations[i]
                    : null;

                if (indexByName.TryGetValue(name, out int idx))
                {
                    targets.Add((idx, cmd.Positions[i], rot));

                    var de = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(idx);
                    if (de != null)
                    {
                        de.InCsv       = true;
                        de.CsvPosition = cmd.Positions[i];
                        de.IsTarget    = true;
                    }
                }
                else missing.Add(name);
            }

            if (missing.Count > 0)
                Debug.Log($"[ObjectOrigin] モデルに存在しない名前を無視: {missing.Count} 件 " +
                          $"({string.Join(", ", missing.GetRange(0, Mathf.Min(5, missing.Count)))} …)");

            if (targets.Count == 0)
            {
                Debug.LogWarning("[ObjectOrigin] 適用対象がありません。");
                return;
            }

            // 再局所化の対象を決める。
            //
            // 姿勢を書き換えるのは CSV に名前があった行（targets）だけだが、
            // 見た目を保つ対象はそれでは足りない。CSV に無いオブジェクトは
            // 姿勢こそ変わらないが、祖先の原点が入ればワールド行列が変わり
            // （ModelContext.ComputeWorldMatrices は 親のワールド × 自身のローカル）、
            // 頂点を補正しないと祖先の原点の合計ぶん飛ぶ。
            // 「CSV に無いものは動かさない」を満たすには全メッシュを対象にする。
            // 姿勢くさび取込（ImportObjectPoseWedges）と同じ範囲にそろえる。
            //
            // 除外は適用先（indexByName）と同じ規則にして targets ⊆ relocalize を保証する。
            // ミラー側は後段の RebakeDerivedMirrorVertices が実体側から作り直す。
            var relocalize = new List<int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;
                if (mc.Type == MeshType.MirrorSide || mc.Type == MeshType.BakedMirror) continue;
                relocalize.Add(i);

                var de = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(i);
                if (de != null) de.InRelocalize = true;
            }

            // 変更前スナップショット + 現在の頂点ワールド位置
            // 回転も動かし得るので、位置だけの ObjectOriginUndoRecord ではなく
            // 回転込みの ObjectPoseUndoRecord に記録する（回転を変えない場合も
            // 変更前後の実値をそのまま入れるため挙動は変わらない）。
            var before      = new Dictionary<int, ObjectPoseSnapshot>();
            var startWorld  = new Dictionary<int, Vector3[]>();
            var startMatrix = new Dictionary<int, Matrix4x4>();

            model.ComputeWorldMatrices();

            foreach (int idx in relocalize)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                var verts = new Vector3[mo.Vertices.Count];
                var world = new Vector3[mo.Vertices.Count];
                var wm    = mc.WorldMatrix;

                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    verts[v] = mo.Vertices[v].Position;
                    world[v] = wm.MultiplyPoint3x4(verts[v]);
                }

                before[idx] = new ObjectPoseSnapshot
                {
                    Position          = mc.BoneTransform?.Position ?? Vector3.zero,
                    Rotation          = mc.BoneTransform?.Rotation ?? Vector3.zero,
                    UseLocalTransform = mc.BoneTransform?.UseLocalTransform ?? false,
                    VertexPositions   = verts,
                };
                startWorld[idx]  = world;
                startMatrix[idx] = wm;

                var de = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(idx);
                if (de != null)
                {
                    de.HasStartWorld  = true;
                    de.WorldBefore    = wm;
                    de.PosBefore      = mc.BoneTransform?.Position ?? Vector3.zero;
                    de.UseLocalBefore = mc.BoneTransform?.UseLocalTransform ?? false;
                }
            }

            // 原点（と、指定があれば回転）を設定
            foreach (var (idx, pos, rot) in targets)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;

                mc.BoneTransform.Position          = pos;
                if (rot.HasValue) mc.BoneTransform.Rotation = rot.Value;
                mc.BoneTransform.UseLocalTransform = true;
            }

            model.ComputeWorldMatrices();

            // 自頂点を再局所化して見た目を保つ。
            // ワールド行列が変わっていないものは触らない（丸め誤差を持ち込まないため）。
            var changed = new HashSet<int>();
            int relocalizedCount = 0;

            foreach (int idx in relocalize)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                var de = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(idx);

                if (de != null && mc != null)
                {
                    de.WorldAfter    = mc.WorldMatrix;
                    de.PosAfter      = mc.BoneTransform?.Position ?? Vector3.zero;
                    de.UseLocalAfter = mc.BoneTransform?.UseLocalTransform ?? false;
                }

                if (mo == null || !startWorld.TryGetValue(idx, out var world)) continue;
                if (startMatrix.TryGetValue(idx, out var wm0) && wm0 == mc.WorldMatrix)
                {
                    if (de != null) de.SkippedByMatrixCompare = true;
                    continue;
                }

                Matrix4x4 inv = mc.WorldMatrixInverse;
                int cnt = Mathf.Min(mo.Vertices.Count, world.Length);
                for (int v = 0; v < cnt; v++)
                {
                    var vert = mo.Vertices[v];
                    vert.Position = inv.MultiplyPoint3x4(world[v]);
                    mo.Vertices[v] = vert;
                }
                mo.InvalidatePositionCache();

                if (de != null) de.Relocalized = true;

                changed.Add(idx);
                relocalizedCount++;
            }

            // 診断: 適用前後で頂点のワールド位置がどれだけ動いたかを測る。
            // 再局所化まで終えた状態で測るので、0 でなければ補正が効いていない。
            if (Poly_Ling.Diagnostics.ObjectOriginDiag.Enabled)
            {
                model.ComputeWorldMatrices();

                foreach (int idx in relocalize)
                {
                    var mc = model.GetMeshContext(idx);
                    var mo = mc?.MeshObject;
                    var de = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(idx);
                    if (mo == null || de == null) continue;
                    if (!startWorld.TryGetValue(idx, out var world)) continue;

                    Matrix4x4 wm = mc.WorldMatrix;
                    de.WorldAfter = wm;

                    int cnt = Mathf.Min(mo.Vertices.Count, world.Length);
                    for (int v = 0; v < cnt; v++)
                    {
                        Vector3 now = wm.MultiplyPoint3x4(mo.Vertices[v].Position);
                        Vector3 d   = now - world[v];
                        float   mag = d.magnitude;
                        if (mag > de.MaxWorldDelta)
                        {
                            de.MaxWorldDelta   = mag;
                            de.WorldDeltaOfMax = d;
                        }
                    }
                }
            }

            // 頂点が動かなくても姿勢を書き換えたものは Undo の対象に含める。
            foreach (var (idx, _, _) in targets) changed.Add(idx);

            // 実体側のローカル頂点が変わったので、生成ミラーを作り直して
            // v_M = S·v_R を保つ（実効ワールド S·H·S の前提）。
            MirrorBranchOps.RebakeDerivedMirrorVertices(model.MeshContextList);

            // ②派生ミラー実体のミラー側モーフも Real 側から作り直す
            // （規約は MorphMirrorPolicy.cs を正典とする）。
            model.SyncAllMirrorMorphs();

            // 書き換えたローカル頂点を描画側へ送る。
            //
            // ここを省くと、GPU には新しいワールド行列だけが届き、頂点位置は
            // 再局所化前のまま残る。結果、原点を入れた祖先を持つオブジェクトが
            // 「原点の合計」ぶんずれて描かれる。MeshObject の値は正しいので
            // 検査では 0 と出る一方、画面だけが飛ぶ。
            //
            // 生成ミラーは RebakeDerivedMirrorVertices が同じことを済ませている
            // （MirrorBranchOps: OriginalPositions 更新 + ApplyVertexPositionsToMesh）。
            // 実体側にだけ無かったのをそろえる。
            foreach (int idx in changed)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null || mo.VertexCount == 0) continue;

                mc.OriginalPositions = (Vector3[])mo.Positions.Clone();
                mc.ApplyVertexPositionsToMesh();
                _viewportManager?.SyncMeshPositionsAndTransform(mc, model);
            }

            // 変更後スナップショット。実際に変わったものだけ記録する。
            var after       = new Dictionary<int, ObjectPoseSnapshot>();
            var beforeSaved = new Dictionary<int, ObjectPoseSnapshot>();

            foreach (int idx in changed)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                if (!before.TryGetValue(idx, out var beforeSnap)) continue;

                var verts = new Vector3[mo.Vertices.Count];
                for (int v = 0; v < mo.Vertices.Count; v++) verts[v] = mo.Vertices[v].Position;

                beforeSaved[idx] = beforeSnap;
                after[idx] = new ObjectPoseSnapshot
                {
                    Position          = mc.BoneTransform?.Position ?? Vector3.zero,
                    Rotation          = mc.BoneTransform?.Rotation ?? Vector3.zero,
                    UseLocalTransform = mc.BoneTransform?.UseLocalTransform ?? false,
                    VertexPositions   = verts,
                };
            }

            if (_undoController != null)
            {
                _undoController.SetModelContext(model);
                _undoController.MeshListStack.Record(
                    new ObjectPoseUndoRecord(beforeSaved, after, "原点の読み込み"), "原点の読み込み");
                _undoController.FocusMeshList();
            }

            model.IsDirty = true;
            model.OnListChanged?.Invoke();
            _viewportManager.EnterTopologyChanged(_getProject());
            _notifyPanels(ChangeKind.Attributes);

            Debug.Log($"[ObjectOrigin] 原点を適用: {targets.Count} 件" +
                      $"（見た目維持のため頂点を補正: {relocalizedCount} 件 / 対象 {relocalize.Count} 件）");
        }

        // ================================================================
        // 姿勢くさび（オブジェクト姿勢の可視化オブジェクト）
        // ================================================================

        /// <summary>
        /// メッシュオブジェクトの姿勢をくさびオブジェクト列としてモデル末尾へ生成する。
        /// 生成そのものは ObjectPoseWedgeGenerator、挿入は ObjectPoseWedgeInserter が持つ。
        /// ここは Undo 記録とビュー更新だけを担う。
        /// </summary>
        private void GenerateObjectPoseWedges(
            ProjectContext project, ModelContext model, GenerateObjectPoseWedgesCommand cmd)
        {
            float length = cmd.WedgeLength > 0f
                ? cmd.WedgeLength
                : ObjectPoseWedgeGenerator.DefaultWedgeLength;

            var pieces = ObjectPoseWedgeGenerator.Generate(model, length);
            if (pieces.Count == 0)
            {
                Debug.LogWarning("[ObjectPose] 対象のメッシュオブジェクトがありません。");
                return;
            }

            var oldSelected = model.CaptureAllSelectedIndices();

            var added = ObjectPoseWedgeInserter.Insert(model, pieces, cmd.ContainerName);
            if (added.Count == 0)
            {
                Debug.LogWarning("[ObjectPose] 生成できませんでした。");
                return;
            }

            // 選択はコンテナだけにする（取り込み時にそのまま対象として使えるように）。
            model.ClearMeshSelection();
            model.AddToMeshSelection(added[0].Index);
            var newSelected = model.CaptureAllSelectedIndices();

            if (_undoController != null)
            {
                _undoController.SetModelContext(model);
                _undoController.RecordMeshContextsAdd(added, oldSelected, newSelected);
            }

            model.IsDirty = true;
            model.OnListChanged?.Invoke();

            _viewportManager.EnterSceneReset(project);
            _viewportManager.EnterCameraChanged(
                _viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
            _rebuildModelList();
            _notifyPanels(ChangeKind.ListStructure);

            int wedgeCount = 0;
            foreach (var p in pieces) if (p != null && p.HasWedge) wedgeCount++;
            Debug.Log($"[ObjectPose] 姿勢くさびを生成: {added[0].MeshContext.Name} / " +
                      $"くさび {wedgeCount} 件・空のオブジェクト {pieces.Count - wedgeCount} 件");
        }

        /// <summary>
        /// くさびオブジェクト列を読み、名前一致でメッシュオブジェクトの姿勢へ戻す。
        /// 見た目は保つ（原点CSV読込と同じく、自頂点をワールド基準で再局所化する）。
        /// </summary>
        private void ApplyObjectPoseWedges(ModelContext model, ApplyObjectPoseWedgesCommand cmd)
        {
            // ── コンテナの決定 ───────────────────────────────────────
            // 選択 → 名前 → 中身（くさびを最も多く持つノード）の順に見る。
            // 選択が的外れでも自動検出に落ちるので、無関係なものを選んだまま
            // 押しても取り込める。
            int containerIndex = ObjectPoseWedgeReader.ResolveContainer(
                model, cmd.ContainerMasterIndex, cmd.ContainerName, out string reason);

            Debug.Log($"[ObjectPose] コンテナ判定: {reason}");

            if (containerIndex < 0)
            {
                Debug.LogWarning("[ObjectPose] くさびのコンテナが見つかりません。" +
                                 "先に「姿勢くさび生成」で作るか、くさびを含むモデルを読み込んでください。");
                return;
            }

            var subtree = ObjectPoseWedgeReader.CollectSubtree(model, containerIndex);
            var entries = ObjectPoseWedgeReader.Read(model, containerIndex);
            if (entries.Count == 0)
            {
                Debug.LogWarning("[ObjectPose] 読み取れるくさびがありません: " +
                                 (model.GetMeshContext(containerIndex)?.Name ?? "?"));
                return;
            }

            // ── 適用先を名前で引く（コンテナ配下は除外）─────────────
            var indexByName = new Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (subtree.Contains(i)) continue;
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Mesh) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (!indexByName.ContainsKey(mc.Name)) indexByName[mc.Name] = i;
            }

            var targets = new List<(int Index, ObjectPoseEntry Entry)>();
            var missing = new List<string>();
            foreach (var e in entries)
            {
                // e.Name は くさび名から "_bone" を外した元メッシュ名。
                if (indexByName.TryGetValue(e.Name, out int idx)) targets.Add((idx, e));
                else missing.Add(e.WedgeName ?? e.Name);
            }

            if (missing.Count > 0)
                Debug.Log($"[ObjectPose] 適用先が見つからないくさびを無視: {missing.Count} 件 " +
                          $"({string.Join(", ", missing.GetRange(0, Mathf.Min(5, missing.Count)))} …)");

            if (targets.Count == 0)
            {
                Debug.LogWarning("[ObjectPose] 適用対象がありません。");
                return;
            }

            // ── 再局所化の対象を決める ───────────────────────────────
            // 姿勢を書き換えるのはくさびを持つオブジェクトだけだが、見た目を保つ
            // 対象はそれでは足りない。くさびを持たないオブジェクト（＝生成時に
            // ローカル姿勢が単位だったもの）は姿勢こそ変わらないが、祖先が動けば
            // 一緒に動く。頂点を補正しないとそれらが四散する。
            // 原点CSV読込が全行を対象にするのと同じ範囲にそろえる。
            var relocalize = new List<int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (subtree.Contains(i)) continue;              // くさび自身は除く
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (mc.Type != MeshType.Mesh) continue;         // ミラー側は後で実体側から作り直す
                relocalize.Add(i);
            }

            // ── 変更前スナップショット + 現在の頂点ワールド位置 ──────
            var before     = new Dictionary<int, ObjectPoseSnapshot>();
            var startWorld = new Dictionary<int, Vector3[]>();

            model.ComputeWorldMatrices();

            foreach (int idx in relocalize)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                var verts = new Vector3[mo.Vertices.Count];
                var world = new Vector3[mo.Vertices.Count];
                var wm    = mc.WorldMatrix;

                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    verts[v] = mo.Vertices[v].Position;
                    world[v] = wm.MultiplyPoint3x4(verts[v]);
                }

                before[idx] = new ObjectPoseSnapshot
                {
                    Position          = mc.BoneTransform?.Position ?? Vector3.zero,
                    Rotation          = mc.BoneTransform?.Rotation ?? Vector3.zero,
                    UseLocalTransform = mc.BoneTransform?.UseLocalTransform ?? false,
                    VertexPositions   = verts,
                };
                startWorld[idx] = world;
            }

            // ── 姿勢を適用（親から順に）─────────────────────────────
            // 親のローカル姿勢が変わると子のワールド行列も変わる。子のローカルは
            // 「更新後の親のワールド」を基準に出す必要があるので、浅い方から処理する。
            targets.Sort((a, b) =>
                MeshFilterToSkinnedConverter.CalculateDepth(a.Index, model)
                    .CompareTo(MeshFilterToSkinnedConverter.CalculateDepth(b.Index, model)));

            foreach (var (idx, entry) in targets)
            {
                model.ComputeWorldMatrices();

                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;

                int p = mc.HierarchyParentIndex;
                Matrix4x4 parentWorld = (p >= 0 && p < model.MeshContextCount)
                    ? (model.GetMeshContext(p)?.WorldMatrix ?? Matrix4x4.identity)
                    : Matrix4x4.identity;

                Matrix4x4 local = parentWorld.inverse *
                    Matrix4x4.TRS(entry.WorldPosition, entry.WorldRotation, Vector3.one);

                mc.BoneTransform.Position          = ObjectPoseWedgeShape.PositionOf(local);
                mc.BoneTransform.Rotation          = ObjectPoseWedgeShape.RotationOf(local).eulerAngles;
                mc.BoneTransform.UseLocalTransform = true;
            }

            model.ComputeWorldMatrices();

            // ── 自頂点を再局所化して見た目を保つ ─────────────────────
            foreach (int idx in relocalize)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null || !startWorld.TryGetValue(idx, out var world)) continue;

                Matrix4x4 inv = mc.WorldMatrixInverse;
                int cnt = Mathf.Min(mo.Vertices.Count, world.Length);
                for (int v = 0; v < cnt; v++)
                {
                    var vert = mo.Vertices[v];
                    vert.Position = inv.MultiplyPoint3x4(world[v]);
                    mo.Vertices[v] = vert;
                }
                mo.InvalidatePositionCache();
            }

            // 実体側のローカル頂点が変わったので、生成ミラーを作り直して
            // v_M = S·v_R を保つ（実効ワールド S·H·S の前提）。
            MirrorBranchOps.RebakeDerivedMirrorVertices(model.MeshContextList);

            // ②派生ミラー実体のミラー側モーフも Real 側から作り直す
            // （規約は MorphMirrorPolicy.cs を正典とする）。
            model.SyncAllMirrorMorphs();

            // ── 変更後スナップショット ───────────────────────────────
            var after = new Dictionary<int, ObjectPoseSnapshot>();
            foreach (int idx in relocalize)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                var verts = new Vector3[mo.Vertices.Count];
                for (int v = 0; v < mo.Vertices.Count; v++) verts[v] = mo.Vertices[v].Position;

                after[idx] = new ObjectPoseSnapshot
                {
                    Position          = mc.BoneTransform?.Position ?? Vector3.zero,
                    Rotation          = mc.BoneTransform?.Rotation ?? Vector3.zero,
                    UseLocalTransform = mc.BoneTransform?.UseLocalTransform ?? false,
                    VertexPositions   = verts,
                };
            }

            if (_undoController != null)
            {
                _undoController.SetModelContext(model);
                _undoController.MeshListStack.Record(
                    new ObjectPoseUndoRecord(before, after, "姿勢くさびの取り込み"), "姿勢くさびの取り込み");
                _undoController.FocusMeshList();
            }

            model.IsDirty = true;
            model.OnListChanged?.Invoke();
            _viewportManager.EnterTopologyChanged(_getProject());
            _notifyPanels(ChangeKind.Attributes);

            Debug.Log($"[ObjectPose] 姿勢を適用: {targets.Count} 件 / " +
                      $"見た目を保つため再局所化: {relocalize.Count} 件");
        }
    }
}
