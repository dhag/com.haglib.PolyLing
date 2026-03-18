// PolyLingCore_Commands.cs
// DispatchPanelCommandInternal + CommandHandlers群
// PolyLing_SummaryNotify.cs / PolyLing_CommandHandlers_*.cs から移植

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Commands;
using Poly_Ling.Data;
using Poly_Ling.Materials;
using Poly_Ling.Context;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Core
{
    public partial class PolyLingCore
    {
        // ================================================================
        // DispatchPanelCommandInternal
        // ================================================================

        partial void DispatchPanelCommandInternal(PanelCommand cmd)
        {
        if (_model == null || _meshListOps == null) return;

        switch (cmd)
        {
            // --- 選択 ---
            case SelectMeshCommand sel:
                HandleSelectMeshCommand(sel);
                NotifyPanels(ChangeKind.Selection);
                return;

            // --- 属性変更 ---
            case ToggleVisibilityCommand c:
                _meshListOps.ToggleVisibility(c.MasterIndex);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case SetBatchVisibilityCommand c:
                _meshListOps.SetBatchVisibility(c.MasterIndices, c.Visible);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case ToggleLockCommand c:
                _meshListOps.ToggleLock(c.MasterIndex);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case CycleMirrorTypeCommand c:
                _meshListOps.CycleMirrorType(c.MasterIndex);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case RenameMeshCommand c:
                _meshListOps.RenameMesh(c.MasterIndex, c.NewName);
                NotifyPanels(ChangeKind.Attributes);
                return;

            // --- リスト操作（構造変更） ---
            case AddMeshCommand _:
                var newCtx = _meshListOps.AddNewMesh();
                if (newCtx != null) AddMeshContextWithUndo(newCtx);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case DeleteMeshesCommand c:
                foreach (int idx in c.MasterIndices.OrderByDescending(i => i))
                    RemoveMeshContextWithUndo(idx);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case DuplicateMeshesCommand c:
                foreach (int idx in c.MasterIndices)
                    DuplicateMeshContentWithUndo(idx);
                NotifyPanels(ChangeKind.ListStructure);
                return;

            // --- リオーダー（構造変更） ---
            case ReorderMeshesCommand c:
                _meshListOps.ReorderMeshes(c.Category, c.Entries);
                _model?.OnListChanged?.Invoke();
                OnRepaintRequired?.Invoke();
                NotifyPanels(ChangeKind.ListStructure);
                return;

            // --- BonePose ---
            case InitBonePoseCommand c:
                _meshListOps.InitBonePose(c.MasterIndices);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case SetBonePoseActiveCommand c:
                _meshListOps.SetBonePoseActive(c.MasterIndices, c.Active);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case ResetBonePoseLayersCommand c:
                _meshListOps.ResetBonePoseLayers(c.MasterIndices);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case BakePoseToBindPoseCommand c:
                _meshListOps.BakePoseToBindPose(c.MasterIndices);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case BeginBonePoseSliderDragCommand c:
                _meshListOps.BeginSliderDrag(c.MasterIndices);
                return; // NotifyPanels不要
            case EndBonePoseSliderDragCommand c:
                _meshListOps.EndSliderDrag(c.Description);
                return; // NotifyPanels不要（Undo記録のみ）

            // --- モーフ変換（構造変更） ---
            case ConvertMeshToMorphCommand c:
                _meshListOps.ConvertMeshToMorph(c.SourceIndex, c.ParentIndex, c.MorphName, c.Panel);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case ConvertMorphToMeshCommand c:
                _meshListOps.ConvertMorphToMesh(c.MasterIndices);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case CreateMorphSetCommand c:
                _meshListOps.CreateMorphSet(c.SetName, c.MorphType, c.MorphIndices);
                NotifyPanels(ChangeKind.ListStructure);
                return;

            // --- モーフプレビュー ---
            case StartMorphPreviewCommand c:
                _meshListOps.StartMorphPreview(c.MorphIndices);
                return; // NotifyPanels不要
            case ApplyMorphPreviewCommand c:
                _meshListOps.ApplyMorphPreview(c.Weight);
                return; // NotifyPanels不要（頂点更新のみ）
            case EndMorphPreviewCommand _:
                _meshListOps.EndMorphPreview();
                _toolContext?.SyncMesh?.Invoke();
                return;

            // --- モーフ全選択/全解除 ---
            case SelectAllMorphsCommand c:
                _meshListOps.SelectAllMorphs(c.AllMorphIndices);
                _toolContext?.OnMeshSelectionChanged?.Invoke();
                NotifyPanels(ChangeKind.Selection);
                return;
            case DeselectAllMorphsCommand _:
                _meshListOps.DeselectAllMorphs();
                _toolContext?.OnMeshSelectionChanged?.Invoke();
                NotifyPanels(ChangeKind.Selection);
                return;

            // --- パーツ選択辞書 ---
            case SavePartsSetCommand c:
                HandleSavePartsSet(c);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case LoadPartsSetCommand c:
                HandleLoadPartsSet(c, additive: false, subtract: false);
                return;
            case AddPartsSetCommand c:
                HandleLoadPartsSet(new LoadPartsSetCommand(c.ModelIndex, c.SetIndex), additive: true, subtract: false);
                return;
            case SubtractPartsSetCommand c:
                HandleLoadPartsSet(new LoadPartsSetCommand(c.ModelIndex, c.SetIndex), additive: false, subtract: true);
                return;
            case DeletePartsSetCommand c:
                HandleDeletePartsSet(c);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case RenamePartsSetCommand c:
                HandleRenamePartsSet(c);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case ExportPartsSetsCsvCommand _:
                HandleExportPartsSetsCsv();
                return;
            case ImportPartsSetCsvCommand _:
                HandleImportPartsSetCsv();
                NotifyPanels(ChangeKind.Attributes);
                return;

            // --- モデルブレンド ---
            case CreateBlendCloneCommand c:
                HandleCreateBlendClone(c);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case ApplyModelBlendCommand c:
                HandleApplyModelBlend(c);
                _toolContext?.SyncMesh?.Invoke();
                _toolContext?.Repaint?.Invoke();
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case PreviewModelBlendCommand c:
                HandlePreviewModelBlend(c);
                _toolContext?.SyncMesh?.Invoke();
                _toolContext?.Repaint?.Invoke();
                return;

            // --- モデル操作 ---
            case SwitchModelCommand c:
                _project?.SelectModel(c.TargetModelIndex);
                // SelectModel が OnCurrentModelChanged を発火し UpdateMeshListOpsContext → NotifyPanels まで実行される
                return;
            case RenameModelCommand c:
                var renameTarget = _project?.GetModel(c.ModelIndex);
                if (renameTarget != null && !string.IsNullOrEmpty(c.NewName))
                {
                    renameTarget.Name = c.NewName;
                    _project?.OnModelsChanged?.Invoke();
                }
                return;
            case DeleteModelCommand c:
                _project?.RemoveModelAt(c.ModelIndex);
                // RemoveModelAt が OnModelsChanged を発火する
                return;

            // --- 選択辞書 ---
            case SaveSelectionDictionaryCommand c:
                HandleSaveSelectionDictionary(c);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case ApplySelectionDictionaryCommand c:
                HandleApplySelectionDictionary(c);
                _toolContext?.OnMeshSelectionChanged?.Invoke();
                NotifyPanels(ChangeKind.Selection);
                return;
            case DeleteSelectionDictionaryCommand c:
                if (c.SetIndex >= 0 && c.SetIndex < _model.MeshSelectionSets.Count)
                    _model.MeshSelectionSets.RemoveAt(c.SetIndex);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case RenameSelectionDictionaryCommand c:
                HandleRenameSelectionDictionary(c);
                NotifyPanels(ChangeKind.Attributes);
                return;

            // --- パネル側直接変更後の通知 ---
            case NotifyListStructureChangedCommand _:
                _model?.OnListChanged?.Invoke();
                _toolContext?.SyncMesh?.Invoke();
                OnRepaintRequired?.Invoke();
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case NotifyDictionaryChangedCommand _:
                NotifyPanels(ChangeKind.Attributes);
                return;

            // --- UV操作 ---
            case ApplyUvUnwrapCommand c:
                Poly_Ling.Core.PolyLingCoreUvHandlers.HandleApplyUvUnwrap(_model, _undoController, _toolContext, () => OnRepaintRequired?.Invoke(), c);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case UvToXyzCommand c:
                Poly_Ling.Core.PolyLingCoreUvHandlers.HandleUvToXyz(_model, _undoController, _toolContext, mc => AddMeshContextWithUndo(mc), () => OnRepaintRequired?.Invoke(), c);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case XyzToUvCommand c:
                Poly_Ling.Core.PolyLingCoreUvHandlers.HandleXyzToUv(_model, _undoController, _toolContext, () => OnRepaintRequired?.Invoke(), c);
                NotifyPanels(ChangeKind.Attributes);
                return;

            // --- メッシュマージ ---
            case MergeMeshesCommand c:
                HandleMergeMeshesCommand(c);
                NotifyPanels(ChangeKind.ListStructure);
                return;

            // --- BoneTransform ---
            case SetBoneTransformValueCommand c:
                _meshListOps.SetBoneTransformValue(c.MasterIndices, c.TargetField, c.Value);
                OnRepaintRequired?.Invoke();
                NotifyPanels(ChangeKind.Attributes);
                return;
            case BeginBoneTransformSliderDragCommand c:
                _meshListOps.BeginBoneTransformSliderDrag(c.MasterIndices);
                return; // NotifyPanels不要
            case EndBoneTransformSliderDragCommand c:
                _meshListOps.EndBoneTransformSliderDrag(c.Description);
                return; // NotifyPanels不要（Undo記録のみ）

            default:
                Debug.LogWarning($"[PolyLing] Unknown PanelCommand: {cmd.GetType().Name}");
                return;
        }        }

        // ================================================================
        // CommandHandlers
        // ================================================================

        // ================================================================
        // 選択
        // ================================================================

        private void HandleSelectMeshCommand(SelectMeshCommand cmd)
        {
            if (_model == null) return;
            switch (cmd.Category)
            {
                case MeshCategory.Drawable:
                    _model.ClearMeshSelection();
                    foreach (int idx in cmd.Indices) _model.AddToMeshSelection(idx);
                    break;
                case MeshCategory.Bone:
                    _model.ClearBoneSelection();
                    foreach (int idx in cmd.Indices) _model.AddToBoneSelection(idx);
                    break;
                case MeshCategory.Morph:
                    _model.ClearMorphSelection();
                    foreach (int idx in cmd.Indices) _model.AddToMorphSelection(idx);
                    break;
            }
            _toolContext?.OnMeshSelectionChanged?.Invoke();
        }

        // ================================================================
        // 選択辞書
        // ================================================================

        private void HandleSaveSelectionDictionary(SaveSelectionDictionaryCommand cmd)
        {
            if (_model == null) return;

            var category = cmd.Category switch
            {
                MeshCategory.Drawable => ModelContext.SelectionCategory.Mesh,
                MeshCategory.Bone     => ModelContext.SelectionCategory.Bone,
                MeshCategory.Morph    => ModelContext.SelectionCategory.Morph,
                _                     => ModelContext.SelectionCategory.Mesh
            };

            string name = string.IsNullOrEmpty(cmd.SetName)
                ? _model.GenerateUniqueMeshSelectionSetName("MeshSet")
                : cmd.SetName;
            if (_model.FindMeshSelectionSetByName(name) != null)
                name = _model.GenerateUniqueMeshSelectionSetName(name);

            var set = new MeshSelectionSet(name) { Category = category };
            foreach (var n in cmd.MeshNames)
                if (!string.IsNullOrEmpty(n) && !set.MeshNames.Contains(n))
                    set.MeshNames.Add(n);

            _model.MeshSelectionSets.Add(set);
        }

        private void HandleApplySelectionDictionary(ApplySelectionDictionaryCommand cmd)
        {
            if (_model == null) return;
            var sets = _model.MeshSelectionSets;
            if (cmd.SetIndex < 0 || cmd.SetIndex >= sets.Count) return;

            if (cmd.AddToExisting)
                sets[cmd.SetIndex].AddTo(_model);
            else
                sets[cmd.SetIndex].ApplyTo(_model);
        }

        private void HandleRenameSelectionDictionary(RenameSelectionDictionaryCommand cmd)
        {
            if (_model == null) return;
            var sets = _model.MeshSelectionSets;
            if (cmd.SetIndex < 0 || cmd.SetIndex >= sets.Count) return;

            var current = sets[cmd.SetIndex];
            string newName = cmd.NewName;
            if (_model.FindMeshSelectionSetByName(newName) != null && newName != current.Name)
                newName = _model.GenerateUniqueMeshSelectionSetName(newName);
            current.Name = newName;
        }

        /// <summary>
        /// 選択メッシュオブジェクトをマージする。
        /// CreateNewMesh=true の場合: 新規 MeshContext を作成し、全対象メッシュの頂点を
        ///   基準オブジェクトのローカル空間に変換して格納する。元の MeshContext は削除する。
        /// CreateNewMesh=false の場合: 非基準メッシュの頂点を基準オブジェクトのローカル空間に
        ///   変換して基準 MeshContext の MeshObject に追記する。非基準 MeshContext は削除する。
        /// </summary>
        private void HandleMergeMeshesCommand(MergeMeshesCommand cmd)
        {
            if (_model == null) return;
            if (cmd.MasterIndices == null || cmd.MasterIndices.Length < 2) return;

            // ----------------------------------------------------------------
            // 1. 対象 MeshContext を収集（有効なもののみ）
            // ----------------------------------------------------------------
            var targets = new List<MeshContext>();
            foreach (int mi in cmd.MasterIndices)
            {
                var ctx = _model.GetMeshContext(mi);
                if (ctx?.MeshObject != null)
                    targets.Add(ctx);
            }
            if (targets.Count < 2) return;

            var baseCtx = _model.GetMeshContext(cmd.BaseMasterIndex);
            if (baseCtx?.MeshObject == null) return;

            // ----------------------------------------------------------------
            // 2. 基準オブジェクトのローカル→ワールド逆行列を取得
            //    （他メッシュの頂点を基準ローカル空間に変換するために使う）
            // ----------------------------------------------------------------
            Matrix4x4 baseWorldInv = baseCtx.WorldMatrixInverse;

            // ----------------------------------------------------------------
            // 3. マージ先 MeshObject の準備
            // ----------------------------------------------------------------
            MeshContext destCtx;

            if (cmd.CreateNewMesh)
            {
                // 新規 MeshContext: 基準オブジェクトのトランスフォームを引き継ぐ
                destCtx = new MeshContext
                {
                    Name            = baseCtx.MeshObject.Name + "_merged",
                    MeshObject      = new MeshObject(baseCtx.MeshObject.Name + "_merged"),
                    OriginalPositions = new Vector3[0],
                };
                var bt = new BoneTransform();
                bt.CopyFrom(baseCtx.BoneTransform);
                destCtx.BoneTransform = bt;
                destCtx.WorldMatrix        = baseCtx.WorldMatrix;
                destCtx.WorldMatrixInverse = baseCtx.WorldMatrixInverse;
                destCtx.BindPose           = baseCtx.BindPose;
            }
            else
            {
                // 基準 MeshContext に直接追記
                destCtx = baseCtx;
            }

            MeshObject destMesh = destCtx.MeshObject;

            // ----------------------------------------------------------------
            // 4. 各ソースメッシュの頂点・面を destMesh に追記
            //    CreateNewMesh の場合は全メッシュ（基準含む）を追記
            //    CreateNewMesh=false の場合は非基準メッシュのみ追記
            // ----------------------------------------------------------------
            foreach (var srcCtx in targets)
            {
                bool isBase = ReferenceEquals(srcCtx, baseCtx);
                if (!cmd.CreateNewMesh && isBase) continue; // 基準はスキップ（既にdestCtx）

                var srcMesh = srcCtx.MeshObject;
                if (srcMesh == null || srcMesh.VertexCount == 0) continue;

                // 頂点変換行列: src ワールド空間 → base ローカル空間
                // src ローカル → ワールド: srcCtx.WorldMatrix
                // ワールド → base ローカル: baseWorldInv
                Matrix4x4 xform = baseWorldInv * srcCtx.WorldMatrix;

                int vertexOffset = destMesh.VertexCount;

                // 頂点追記
                foreach (var v in srcMesh.Vertices)
                {
                    var newV = v.Clone();
                    newV.Id       = destMesh.GenerateVertexId();
                    newV.Position = xform.MultiplyPoint3x4(v.Position);

                    // 法線も回転変換（スケール非均等の場合は逆転置行列が正確だが、
                    // ここでは MultiplyVector で近似する）
                    if (v.Normals != null)
                    {
                        newV.Normals = v.Normals.Select(n => xform.MultiplyVector(n).normalized).ToList();
                    }
                    destMesh.Vertices.Add(newV);
                    destMesh.RegisterVertexId(newV.Id);
                }

                // 面追記（頂点インデックスをオフセット）
                foreach (var f in srcMesh.Faces)
                {
                    var newF = f.Clone();
                    newF.Id = destMesh.GenerateFaceId();
                    newF.VertexIndices = f.VertexIndices.Select(i => i + vertexOffset).ToList();
                    destMesh.Faces.Add(newF);
                    destMesh.RegisterFaceId(newF.Id);
                }
            }

            // ----------------------------------------------------------------
            // 5. Unity Mesh 再生成
            // ----------------------------------------------------------------
            var unityMesh = destMesh.ToUnityMesh();
            unityMesh.name = destMesh.Name;
            unityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            destCtx.UnityMesh = unityMesh;
            destCtx.OriginalPositions = (Vector3[])destMesh.Positions.Clone();

            // ----------------------------------------------------------------
            // 6. モデルへの追加・削除（Undo 対応）
            // ----------------------------------------------------------------
            if (cmd.CreateNewMesh)
            {
                // ソースはそのまま残し、新規メッシュを末尾に追加する
                AddMeshContextWithUndo(destCtx);
            }
            else
            {
                // 非基準メッシュを削除
                var nonBaseTargets = targets.Where(t => !ReferenceEquals(t, baseCtx)).ToList();
                var indicesToRemove = nonBaseTargets
                    .Select(t => _model.IndexOf(t))
                    .Where(i => i >= 0)
                    .OrderByDescending(i => i)
                    .ToList();

                foreach (int idx in indicesToRemove)
                    RemoveMeshContextWithUndo(idx);

                // 基準メッシュのGPU同期
                _toolContext?.SyncMesh?.Invoke();
            }

            _model?.OnListChanged?.Invoke();
        }

        // ================================================================
        // モデルブレンド
        // ================================================================

        private void HandleCreateBlendClone(CreateBlendCloneCommand cmd)
        {
            if (_project == null) return;
            var src = _project.GetModel(cmd.ModelIndex);
            if (src == null) return;

            string uniqueName = _project.GenerateUniqueModelName(
                string.IsNullOrEmpty(cmd.CloneNameBase)
                    ? src.Name + "_blend"
                    : cmd.CloneNameBase);

            var clone = DeepCloneModelContext(src, uniqueName);
            if (clone == null) return;
            _project.AddModel(clone);
        }

        private void HandleApplyModelBlend(ApplyModelBlendCommand cmd)
        {
            ExecuteBlend(cmd.ModelIndex, cmd.CloneModelIndex, cmd.Weights, cmd.MeshEnabled, cmd.RecalcNormals, cmd.BlendBones);
        }

        private void HandlePreviewModelBlend(PreviewModelBlendCommand cmd)
        {
            ExecuteBlend(cmd.ModelIndex, cmd.CloneModelIndex, cmd.Weights, cmd.MeshEnabled, recalcNormals: false, blendBones: cmd.BlendBones);
        }

        private void ExecuteBlend(int sourceModelIndex, int cloneModelIndex,
            float[] weights, bool[] meshEnabled, bool recalcNormals, bool blendBones)
        {
            if (_project == null) return;
            var cloneModel = _project.GetModel(cloneModelIndex);
            if (cloneModel == null) return;

            // ウェイト正規化
            float total = 0f;
            foreach (var w in weights) total += w;
            float[] nw = new float[weights.Length];
            if (total > 0f)
                for (int i = 0; i < weights.Length; i++) nw[i] = weights[i] / total;
            else
            {
                float eq = 1f / weights.Length;
                for (int i = 0; i < weights.Length; i++) nw[i] = eq;
            }

            // Step 1: ターゲット（clone）のフィルタ済みメッシュリスト
            // MirrorSide 除外・VertexCount==0 除外
            // drawableIdx = DrawableMeshes 上の元インデックス（meshEnabled の添字）を保持
            var cloneDrawables = cloneModel.DrawableMeshes;
            var targetEntries = new List<(int drawableIdx, TypedMeshEntry entry)>();
            for (int di = 0; di < cloneDrawables.Count; di++)
            {
                var e = cloneDrawables[di];
                if (e.Type == MeshType.MirrorSide) continue;
                if (e.Type == MeshType.BakedMirror) continue;
                if ((e.MeshObject?.VertexCount ?? 0) == 0) continue;
                targetEntries.Add((di, e));
            }

            // ターゲット各メッシュの展開前頂点数（MeshObject.VertexCount）と
            // 展開後頂点数（UnityMesh.vertexCount）を記録
            // ※展開後頂点数は UnityMesh が null の場合は展開前と同じとみなす
            var targetVertCountRaw      = targetEntries.Select(t => t.entry.MeshObject.VertexCount).ToArray();
            var targetVertCountExpanded = targetEntries.Select(t =>
                t.entry.Context.UnityMesh != null ? t.entry.Context.UnityMesh.vertexCount : t.entry.MeshObject.VertexCount
            ).ToArray();

            // Step 2: 各ソースモデルのフィルタ済みメッシュリスト（同条件）
            // モデルインデックス → フィルタ済みエントリリスト + 展開後頂点数リスト
            var srcFilteredMap  = new Dictionary<int, List<TypedMeshEntry>>();
            var srcExpCountsMap = new Dictionary<int, List<int>>();
            for (int modelIdx = 0; modelIdx < _project.ModelCount; modelIdx++)
            {
                if (modelIdx >= nw.Length || nw[modelIdx] <= 0f) continue;
                var m = _project.GetModel(modelIdx);
                if (m == null) continue;
                var srcDrawables = m.DrawableMeshes;
                var filtered  = new List<TypedMeshEntry>();
                var expCounts = new List<int>();
                for (int di = 0; di < srcDrawables.Count; di++)
                {
                    var e = srcDrawables[di];
                    if (e.Type == MeshType.MirrorSide) continue;
                    if (e.Type == MeshType.BakedMirror) continue;
                    if ((e.MeshObject?.VertexCount ?? 0) == 0) continue;
                    filtered.Add(e);
                    int ec = e.Context.UnityMesh != null ? e.Context.UnityMesh.vertexCount : e.MeshObject.VertexCount;
                    expCounts.Add(ec);
                }
                srcFilteredMap[modelIdx]  = filtered;
                srcExpCountsMap[modelIdx] = expCounts;
            }
            // ソースモデルごとのマッチングカーソル（先頭から順に頂点数一致で対応付け）
            var srcCursors = new Dictionary<int, int>();
            foreach (var key in srcFilteredMap.Keys) srcCursors[key] = 0;

            // Step 3 & 4: メッシュ対応表でブレンド計算
            for (int k = 0; k < targetEntries.Count; k++)
            {
                // meshEnabled は DrawableMeshes 上のインデックス基準
                int drawableIdx = targetEntries[k].drawableIdx;
                if (drawableIdx < meshEnabled.Length && !meshEnabled[drawableIdx]) continue;

                var targetEntry = targetEntries[k].entry;

                var targetMesh = targetEntry.MeshObject;
                int rawCount   = targetVertCountRaw[k];
                int expCount   = targetVertCountExpanded[k];

                // 孤立頂点を除外するセット（target の Vertices インデックス基準）
                var nonIsolated = BuildBlendNonIsolatedSet(targetMesh);

                Debug.Log($"[Blend] k={k} name={targetEntry.Context.Name} rawCount={rawCount} expCount={expCount} nonIsolated={nonIsolated.Count}");
                foreach (var kv2 in srcFilteredMap)
                {
                    var sc = srcExpCountsMap[kv2.Key];
                    string kVal = k < sc.Count ? sc[k].ToString() : "OOB";
                    Debug.Log($"[Blend]   srcModel[{kv2.Key}] srcList.Count={kv2.Value.Count} sc[k]={kVal}");
                }

                var blended = new Vector3[rawCount];

                bool targetIsExpanded = targetMesh.IsExpanded;
                if (targetIsExpanded)
                {
                    foreach (var kv in srcFilteredMap)
                    {
                        float w = nw[kv.Key];
                        var srcList = kv.Value;
                        var srcExpCounts = srcExpCountsMap[kv.Key];
                        int cursor = srcCursors[kv.Key];
                        int matchSi = -1;
                        for (int si = cursor; si < srcExpCounts.Count; si++)
                        {
                            if (srcExpCounts[si] == expCount) { matchSi = si; break; }
                        }
                        if (matchSi < 0) continue;
                        srcCursors[kv.Key] = matchSi + 1;
                        var srcMesh = srcList[matchSi].MeshObject;
                        bool srcIsExpanded = srcMesh.IsExpanded;
                        var srcInvMap = srcIsExpanded ? null : srcMesh.BuildInverseExpansionMap();

                        for (int vi = 0; vi < rawCount; vi++)
                        {
                            if (!nonIsolated.Contains(vi)) continue;
                            Vector3 srcPos;
                            if (srcIsExpanded)
                            {
                                if (vi >= srcMesh.Vertices.Count) continue;
                                srcPos = srcMesh.Vertices[vi].Position;
                            }
                            else
                            {
                                if (!srcInvMap.TryGetValue(vi, out var r)) continue;
                                srcPos = srcMesh.Vertices[r.vIdx].Position;
                            }
                            blended[vi] += srcPos * w;
                        }
                    }
                }
                else
                {
                    foreach (var kv in srcFilteredMap)
                    {
                        float w = nw[kv.Key];
                        var srcList = kv.Value;
                        var srcExpCounts2 = srcExpCountsMap[kv.Key];
                        int cursor2 = srcCursors[kv.Key];
                        int matchSi2 = -1;
                        for (int si = cursor2; si < srcExpCounts2.Count; si++)
                        {
                            if (srcExpCounts2[si] == expCount) { matchSi2 = si; break; }
                        }
                        if (matchSi2 < 0) continue;
                        srcCursors[kv.Key] = matchSi2 + 1;
                        var srcMesh = srcList[matchSi2].MeshObject;
                        bool srcIsExpanded = srcMesh.IsExpanded;
                        var srcExpMap = srcIsExpanded ? targetMesh.BuildExpansionMap() : null;

                        for (int vi = 0; vi < rawCount; vi++)
                        {
                            if (!nonIsolated.Contains(vi)) continue;
                            Vector3 srcPos;
                            if (srcIsExpanded)
                            {
                                if (!srcExpMap.TryGetValue((vi, 0), out int srcEi)) continue;
                                if (srcEi >= srcMesh.Vertices.Count) continue;
                                srcPos = srcMesh.Vertices[srcEi].Position;
                            }
                            else
                            {
                                if (vi >= srcMesh.Vertices.Count) continue;
                                srcPos = srcMesh.Vertices[vi].Position;
                            }
                            blended[vi] += srcPos * w;
                        }
                    }
                }

                // 書き戻し（孤立頂点は blended[vi]==Vector3.zero のまま → 元位置を維持するため書き戻さない）
                for (int vi = 0; vi < rawCount; vi++)
                {
                    if (!nonIsolated.Contains(vi)) continue;
                    targetMesh.Vertices[vi].Position = blended[vi];
                }

                if (recalcNormals)
                    targetMesh.RecalculateSmoothNormals();

                _toolContext?.SyncMeshContextPositionsOnly?.Invoke(targetEntry.Context);
            }

            // Step 5: ミラー同期（Real ブレンド後にミラー側へ反映）
            // MirrorPairs経由（PMX・再インポート済みMQO）
            var syncedRealContexts = new HashSet<MeshContext>();
            foreach (var pair in cloneModel.MirrorPairs)
            {
                if (!pair.IsValid) continue;
                pair.SyncPositions();
                if (recalcNormals) pair.SyncNormals();
                _toolContext?.SyncMeshContextPositionsOnly?.Invoke(pair.Real);
                _toolContext?.SyncMeshContextPositionsOnly?.Invoke(pair.Mirror);
                syncedRealContexts.Add(pair.Real);
            }

            // フォールバック: MirrorPairsに含まれないMirrorSideをName+"+"で直接同期（MQO既存インポート対応）
            foreach (var (_, targetEntry) in targetEntries)
            {
                var realCtx = targetEntry.Context;
                if (syncedRealContexts.Contains(realCtx)) continue;
                string mirrorName = realCtx.Name + "+";
                var axis = realCtx.GetMirrorSymmetryAxis();
                var realMo = realCtx.MeshObject;

                for (int i = 0; i < cloneModel.MeshContextCount; i++)
                {
                    var mc = cloneModel.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.MirrorSide) continue;
                    if (mc.Name != mirrorName) continue;
                    if (mc.MeshObject == null || mc.MeshObject.VertexCount != realMo.VertexCount) continue;

                    for (int vi = 0; vi < realMo.VertexCount; vi++)
                    {
                        var p = realMo.Vertices[vi].Position;
                        mc.MeshObject.Vertices[vi].Position = axis switch
                        {
                            Poly_Ling.Symmetry.SymmetryAxis.X => new Vector3(-p.x, p.y, p.z),
                            Poly_Ling.Symmetry.SymmetryAxis.Y => new Vector3(p.x, -p.y, p.z),
                            Poly_Ling.Symmetry.SymmetryAxis.Z => new Vector3(p.x, p.y, -p.z),
                            _ => new Vector3(-p.x, p.y, p.z),
                        };
                    }
                    _toolContext?.SyncMeshContextPositionsOnly?.Invoke(mc);
                    break;
                }
            }

            // Step 6: ボーンブレンド（名前照合・位置補間 → WorldMatrix/BindPose 再計算）
            if (blendBones && cloneModel.BoneCount > 0)
            {
                // クローンのボーンコンテキストを 名前 → インデックス でマップ
                var cloneBoneByName = new Dictionary<string, MeshContext>();
                for (int i = 0; i < cloneModel.MeshContextCount; i++)
                {
                    var mc = cloneModel.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.Bone) continue;
                    if (!string.IsNullOrEmpty(mc.Name))
                        cloneBoneByName[mc.Name] = mc;
                }

                // ソースモデルのボーン名 → Position マップ（ウェイト > 0 かつボーンありのみ）
                var srcBoneMaps = new Dictionary<int, Dictionary<string, Vector3>>();
                for (int modelIdx = 0; modelIdx < _project.ModelCount; modelIdx++)
                {
                    if (modelIdx >= nw.Length || nw[modelIdx] <= 0f) continue;
                    var srcM = _project.GetModel(modelIdx);
                    if (srcM == null || srcM.BoneCount == 0) continue;
                    var bmap = new Dictionary<string, Vector3>();
                    for (int i = 0; i < srcM.MeshContextCount; i++)
                    {
                        var mc = srcM.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;
                        if (!string.IsNullOrEmpty(mc.Name) && mc.BoneTransform != null)
                            bmap[mc.Name] = mc.BoneTransform.Position;
                    }
                    if (bmap.Count > 0)
                        srcBoneMaps[modelIdx] = bmap;
                }

                // 各クローンボーンの位置を加重平均でブレンド
                foreach (var kv in cloneBoneByName)
                {
                    var cloneBoneCtx = kv.Value;
                    if (cloneBoneCtx.BoneTransform == null) continue;

                    Vector3 blendedPos = Vector3.zero;
                    float totalW = 0f;
                    foreach (var srcKv in srcBoneMaps)
                    {
                        if (!srcKv.Value.TryGetValue(kv.Key, out Vector3 srcPos)) continue;
                        float w = nw[srcKv.Key];
                        blendedPos += srcPos * w;
                        totalW += w;
                    }
                    if (totalW > 0f)
                        cloneBoneCtx.BoneTransform.Position = blendedPos / totalW;
                }

                // WorldMatrix と BindPose を再計算
                cloneModel.ComputeWorldAndBindPoses();

                // GPU バッファに通知（トポロジ変更扱いでフルリビルド）
                _toolContext?.NotifyTopologyChanged?.Invoke();
            }
        }

        /// <summary>
        /// いずれかの Face に参照されている頂点インデックスのセットを返す（孤立頂点除外用）
        /// </summary>
        private static HashSet<int> BuildBlendNonIsolatedSet(MeshObject mo)
        {
            var set = new HashSet<int>();
            foreach (var face in mo.Faces)
                foreach (int vi in face.VertexIndices)
                    set.Add(vi);
            return set;
        }

        // ================================================================
        // ModelContext ディープコピー
        // ================================================================

        /// <summary>
        /// ModelContext をディープコピーする（BindPose 等 DTO 非保存フィールドを含む）
        /// </summary>
        private static ModelContext DeepCloneModelContext(ModelContext src, string newName)
        {
            var dst = new ModelContext { Name = newName };

            for (int i = 0; i < src.MeshContextCount; i++)
            {
                var s = src.GetMeshContext(i);
                if (s == null) continue;

                var meshObj = s.MeshObject?.Clone();
                if (meshObj == null) continue;

                var d = new MeshContext
                {
                    Name                   = s.Name,
                    MeshObject             = meshObj,
                    UnityMesh              = meshObj.ToUnityMesh(),
                    OriginalPositions      = (Vector3[])meshObj.Positions.Clone(),
                    BoneTransform          = CloneBoneTransform(s.BoneTransform),
                    // 階層
                    ParentIndex            = s.ParentIndex,
                    Depth                  = s.Depth,
                    HierarchyParentIndex   = s.HierarchyParentIndex,
                    // 表示
                    IsVisible              = s.IsVisible,
                    IsLocked               = s.IsLocked,
                    IsFolding              = s.IsFolding,
                    // ミラー
                    MirrorType             = s.MirrorType,
                    MirrorAxis             = s.MirrorAxis,
                    MirrorDistance         = s.MirrorDistance,
                    MirrorMaterialOffset   = s.MirrorMaterialOffset,
                    // ベイクミラー
                    BakedMirrorSourceIndex = s.BakedMirrorSourceIndex,
                    HasBakedMirrorChild    = s.HasBakedMirrorChild,
                    // モーフ
                    MorphParentIndex       = s.MorphParentIndex,
                    // BindPose（DTOに保存されないため直接コピー必須）
                    BindPose               = s.BindPose,
                    // BonePoseData / MorphBaseData
                    BonePoseData           = s.BonePoseData?.Clone(),
                    MorphBaseData          = s.MorphBaseData?.Clone(),
                };

                dst.Add(d);
            }

            if (src.MaterialReferences != null)
                foreach (var m in src.MaterialReferences)
                    dst.MaterialReferences.Add(m);
            dst.CurrentMaterialIndex = src.CurrentMaterialIndex;

            if (src.DefaultMaterialReferences != null)
                foreach (var m in src.DefaultMaterialReferences)
                    dst.DefaultMaterialReferences.Add(m);
            dst.DefaultCurrentMaterialIndex = src.DefaultCurrentMaterialIndex;
            dst.AutoSetDefaultMaterials     = src.AutoSetDefaultMaterials;

            if (src.MirrorPairs != null)
            {
                foreach (var sp in src.MirrorPairs)
                {
                    int ri = src.IndexOf(sp.Real);
                    int mi = src.IndexOf(sp.Mirror);
                    if (ri < 0 || mi < 0 || ri >= dst.Count || mi >= dst.Count) continue;
                    var pair = new MirrorPair
                    {
                        Real   = dst.GetMeshContext(ri),
                        Mirror = dst.GetMeshContext(mi),
                        Axis   = sp.Axis,
                    };
                    if (pair.Build())
                        dst.MirrorPairs.Add(pair);
                }
            }

            return dst;
        }

        private static BoneTransform CloneBoneTransform(BoneTransform src)
        {
            if (src == null) return new BoneTransform();
            var dst = new BoneTransform();
            dst.CopyFrom(src);
            return dst;
        }

        // ================================================================
        // パーツ選択辞書
        // ================================================================

        private void HandleSavePartsSet(SavePartsSetCommand cmd)
        {
            var meshCtx = _model?.FirstSelectedMeshContext;
            if (meshCtx == null) return;

            var sel = _selectionState;
            if (sel == null || !sel.HasAnySelection) return;

            string name = string.IsNullOrEmpty(cmd.SetName)
                ? meshCtx.GenerateUniqueSelectionSetName("Selection")
                : cmd.SetName;
            if (meshCtx.FindSelectionSetByName(name) != null)
                name = meshCtx.GenerateUniqueSelectionSetName(name);

            var set = PartsSelectionSet.FromCurrentSelection(
                name, sel.Vertices, sel.Edges, sel.Faces, sel.Lines, sel.Mode);
            meshCtx.PartsSelectionSetList.Add(set);
        }

        private void HandleLoadPartsSet(LoadPartsSetCommand cmd, bool additive, bool subtract)
        {
            var meshCtx = _model?.FirstSelectedMeshContext;
            if (meshCtx == null) return;
            var sets = meshCtx.PartsSelectionSetList;
            if (cmd.SetIndex < 0 || cmd.SetIndex >= sets.Count) return;

            var set = sets[cmd.SetIndex];
            var sel = _selectionState;
            if (sel == null) return;

            if (additive)
            {
                var snap = sel.CreateSnapshot();
                snap.Vertices.UnionWith(set.Vertices);
                snap.Edges.UnionWith(set.Edges);
                snap.Faces.UnionWith(set.Faces);
                snap.Lines.UnionWith(set.Lines);
                sel.RestoreFromSnapshot(snap);
            }
            else if (subtract)
            {
                var snap = sel.CreateSnapshot();
                snap.Vertices.ExceptWith(set.Vertices);
                snap.Edges.ExceptWith(set.Edges);
                snap.Faces.ExceptWith(set.Faces);
                snap.Lines.ExceptWith(set.Lines);
                sel.RestoreFromSnapshot(snap);
            }
            else
            {
                var snap = new SelectionSnapshot
                {
                    Mode     = set.Mode,
                    Vertices = new HashSet<int>(set.Vertices),
                    Edges    = new HashSet<VertexPair>(set.Edges),
                    Faces    = new HashSet<int>(set.Faces),
                    Lines    = new HashSet<int>(set.Lines)
                };
                sel.RestoreFromSnapshot(snap);
            }
            _toolContext?.Repaint?.Invoke();
            NotifyPanels(ChangeKind.Selection);
        }

        private void HandleDeletePartsSet(DeletePartsSetCommand cmd)
        {
            var meshCtx = _model?.FirstSelectedMeshContext;
            if (meshCtx == null) return;
            var sets = meshCtx.PartsSelectionSetList;
            if (cmd.SetIndex < 0 || cmd.SetIndex >= sets.Count) return;
            sets.RemoveAt(cmd.SetIndex);
        }

        private void HandleRenamePartsSet(RenamePartsSetCommand cmd)
        {
            var meshCtx = _model?.FirstSelectedMeshContext;
            if (meshCtx == null) return;
            var sets = meshCtx.PartsSelectionSetList;
            if (cmd.SetIndex < 0 || cmd.SetIndex >= sets.Count) return;
            var set = sets[cmd.SetIndex];
            string newName = cmd.NewName;
            if (meshCtx.FindSelectionSetByName(newName) != null && newName != set.Name)
                newName = meshCtx.GenerateUniqueSelectionSetName(newName);
            set.Name = newName;
        }

        private void HandleExportPartsSetsCsv()
        {
            var meshCtx = _model?.FirstSelectedMeshContext;
            if (meshCtx == null || meshCtx.PartsSelectionSetList.Count == 0) return;
            Poly_Ling.UI.PartsSetCsvHelper.ExportSets(meshCtx);
        }

        private void HandleImportPartsSetCsv()
        {
            var meshCtx = _model?.FirstSelectedMeshContext;
            if (meshCtx == null) return;
            Poly_Ling.UI.PartsSetCsvHelper.ImportSet(meshCtx);
        }

        // ================================================================
        // ApplyUvUnwrapCommand
        // ================================================================

    }
}
