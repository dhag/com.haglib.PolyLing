// PolyLingPlayerViewerCore.Import.cs
// Player ビューアのコア：読み込み（PMX / MQO / OBJ）と読込後オプション。
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
        private void OnImportPmx(string filePath, PMXImportSettings settings,
                                 PlayerImportSubPanel.PostOptions post)
        {
            var cmd = new ImportPmxCommand(
                filePath, settings,
                onResult: (model, _) =>
                {
                    _localLoader.LoadModel(filePath, model);
                    ApplyImportPostOptions(post);
                },
                onError:  msg       => _status = $"PMX読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
        }

        private void OnImportMqo(string filePath, MQOImportSettings settings,
                                 PlayerImportSubPanel.PostOptions post)
        {
            var cmd = new ImportMqoCommand(
                filePath, settings,
                onResult: (model, _) =>
                {
                    _localLoader.LoadModel(filePath, model);
                    UnityEngine.Debug.Log("[LoadDbg] 16 after-LoadModel");
                    ApplyImportPostOptions(post);
                },
                onError:  msg       => _status = $"MQO読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
            UnityEngine.Debug.Log("[LoadDbg] 17 after-Enqueue");
        }

        private void OnImportObj(string filePath, Poly_Ling.OBJ.ObjImportSettings settings,
                                 PlayerImportSubPanel.PostOptions post)
        {
            var cmd = new ImportObjCommand(
                filePath, settings,
                onResult: (model, _) =>
                {
                    _localLoader.LoadModel(filePath, model);
                    ApplyImportPostOptions(post);
                },
                onError:  msg       => _status = $"OBJ読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
        }

        private void OnImportVrm(string filePath, Poly_Ling.Vrm.Vrm10ImportSettings settings,
                                 PlayerImportSubPanel.PostOptions post)
        {
            var cmd = new ImportVrmCommand(
                filePath, settings,
                onResult: (model, result) =>
                {
                    _localLoader.LoadModel(filePath, model);
                    ApplyImportPostOptions(post);
                    _status =
                        $"VRM読込完了: {System.IO.Path.GetFileName(filePath)}" +
                        (result.SourceIsVrm0 ? "（VRM 0.x から移行）" : "") +
                        $" ({result.MeshCount}メッシュ / {result.VertexCount}頂点 / " +
                        $"Humanoid {result.HumanoidBoneCount}ボーン / 表情 {result.ExpressionCount})" +
                        (result.Warnings.Count > 0 ? $" 警告 {result.Warnings.Count} 件（コンソール参照）" : "");
                },
                onError:  msg       => _status = $"VRM読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
        }

        // ================================================================
        // 読込後オプション（インポータパネルのチェック）
        // ================================================================

        /// <summary>
        /// PMX / MQO / OBJ の読み込みが終わったあとに、インポータパネルの
        /// チェックに応じた処理を流す。呼び出しは _localLoader.LoadModel の直後で、
        /// この時点でモデルはプロジェクトへ追加され CurrentModel になっている
        /// （PlayerLocalLoader.FinishLoad が AddModel → SelectModel まで済ませる）。
        ///
        /// post が null の経路（自動検証パネルからの直接呼び出し）では何もしない。
        ///
        /// CommandQueue.ProcessAll は Execute() 内の例外を握り潰すため、
        /// ここで捕まえないと失敗が画面にもログにも出ない。
        /// </summary>
        private void ApplyImportPostOptions(PlayerImportSubPanel.PostOptions post)
        {
            if (post == null) return;

            try
            {
                if (post.HumanoidAutoMap) ApplyImportHumanoidAutoMap();
                if (post.ApplyOriginCsv)  ApplyImportOriginCsv(post);
            }
            catch (Exception ex)
            {
                _status = $"読込後オプションで例外: {ex.Message}";
                UnityEngine.Debug.LogError($"[ImportPostOptions] {ex}");
            }
        }

        /// <summary>
        /// ボーン名からアバター用ヒューマンマッピングを作って適用する。
        ///
        /// HumanoidBoneMapping が受ける名前リストは
        /// 「インデックス = MeshContextList のインデックス」（HumanoidBoneMapping.cs の
        /// AutoMapFromEmbeddedCSV 引数説明）なので、長さ MeshContextCount の配列を作り
        /// 各名前を自分の索引位置へ置く。ボーンを持つモデルではボーン以外の位置を
        /// 空文字にして、描画メッシュの名前が Humanoid 名に引っかからないようにする
        /// （FindBoneByAliases は空文字にヒットしない）。ボーンが 1 本も無いモデルでは
        /// 全コンテキストを候補にする。
        /// </summary>
        private void ApplyImportHumanoidAutoMap()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _status = "ヒューマンマッピング: モデルがありません"; return; }

            bool bonesOnly = model.BoneCount > 0;

            var names = new List<string>(model.MeshContextCount);
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                bool use = mc != null && !string.IsNullOrEmpty(mc.Name)
                           && (!bonesOnly || mc.Type == MeshType.Bone);
                names.Add(use ? mc.Name : "");
            }

            var mapping = new Poly_Ling.Data.HumanoidBoneMapping();
            int mapped  = mapping.AutoMapFromEmbeddedCSV(names);

            if (mapped == 0)
            {
                _status = bonesOnly
                    ? "ヒューマンマッピング: 一致するボーン名がありませんでした"
                    : "ヒューマンマッピング: 一致する名前がありませんでした（ボーンなしのモデル）";
                UnityEngine.Debug.LogWarning("[ImportPostOptions] " + _status);
                return;
            }

            ApplyHumanoidMappingCommand.SplitMapping(mapping, out var hmNames, out var hmIdx);
            _panelContext?.SendCommand(new ApplyHumanoidMappingCommand(
                ActiveProject?.CurrentModelIndex ?? 0, hmNames, hmIdx));

            _status = $"ヒューマンマッピングを自動割当: {mapped} ボーン";
            UnityEngine.Debug.Log("[ImportPostOptions] " + _status);
        }

        /// <summary>
        /// 指定された原点CSVを読み、名前一致で適用する。
        /// 解析も適用も「描画オブジェクトの姿勢」タブの原点CSV読込と同じ経路
        /// （ObjectOriginCsv.Parse → ApplyObjectOriginsCommand）を通す。
        /// </summary>
        private void ApplyImportOriginCsv(PlayerImportSubPanel.PostOptions post)
        {
            string path = post.OriginCsvPath;
            if (string.IsNullOrEmpty(path))
            {
                _status = "原点CSV: パスが未指定です";
                UnityEngine.Debug.LogWarning("[ImportPostOptions] " + _status);
                return;
            }
            if (!System.IO.File.Exists(path))
            {
                _status = $"原点CSV: ファイルが見つかりません: {System.IO.Path.GetFileName(path)}";
                UnityEngine.Debug.LogWarning("[ImportPostOptions] " + _status);
                return;
            }

            string[] lines;
            try
            {
                lines = System.IO.File.ReadAllLines(path);
            }
            catch (Exception e)
            {
                _status = $"原点CSV: 読み込みに失敗: {e.Message}";
                UnityEngine.Debug.LogError("[ImportPostOptions] " + _status);
                return;
            }

            bool withRot = post.OriginCsvIncludeRotation;

            Poly_Ling.Tools.ObjectPose.ObjectOriginCsv.Parse(
                lines, withRot,
                out var names, out var positions, out var rotations, out int rotRows);

            if (names.Count == 0)
            {
                _status = "原点CSV: 有効な行がありません";
                UnityEngine.Debug.LogWarning("[ImportPostOptions] " + _status + ": " + path);
                return;
            }

            ApplyObjectOriginsCommand.SplitRotations(
                withRot ? rotations.ToArray() : null,
                out var rotValues, out var hasRot);
            _panelContext?.SendCommand(new ApplyObjectOriginsCommand(
                ActiveProject?.CurrentModelIndex ?? 0,
                names.ToArray(), positions.ToArray(),
                rotValues, hasRot));

            _status = $"原点CSVを適用: {names.Count} 行" +
                      (withRot ? $"（うち回転あり {rotRows} 行）" : "（回転は対象外）");
            UnityEngine.Debug.Log("[ImportPostOptions] " + _status);
        }

        /// <summary>
        /// OBJ 書き出し。失敗理由を返す（成功時は null）。
        /// パネル経路は戻り値を捨てる。コマンド経路（ExecuteExportObjFile）は
        /// これをそのまま Fail() へ載せるため、状態表示だけでは足りない。
        /// </summary>
        private string OnExportObj(string outputPath, Poly_Ling.OBJ.ObjExportSettings settings)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _exportSubPanel?.SetStatus("モデルがありません"); return "モデルがありません"; }
            try
            {
                var result = Poly_Ling.OBJ.ObjExporter.ExportFile(outputPath, model, settings);
                if (result.Success)
                {
                    string mtl = string.IsNullOrEmpty(result.MtlPath)
                        ? ""
                        : $" + {System.IO.Path.GetFileName(result.MtlPath)}";
                    _exportSubPanel?.SetStatus($"完了: {System.IO.Path.GetFileName(outputPath)}{mtl}");
                    return null;
                }
                _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
                return result.ErrorMessage;
            }
            catch (Exception ex)
            {
                _exportSubPanel?.SetStatus($"例外: {ex.Message}");
                return $"例外: {ex.Message}";
            }
        }

        /// <summary>
        /// VRM 1.0 書き出し。失敗理由を返す（成功時は null）。警告は表示のみ。
        /// </summary>
        private string OnExportVrm(string outputPath, Poly_Ling.Vrm.Vrm10ExportSettings settings)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _exportSubPanel?.SetStatus("モデルがありません"); return "モデルがありません"; }
            try
            {
                var result = Poly_Ling.Vrm.PLVrm10Bridge.I.Export(model, outputPath, settings);
                if (result.Success)
                {
                    string msg = $"完了: {System.IO.Path.GetFileName(outputPath)} " +
                                 $"({result.MeshCount}メッシュ / {result.VertexCount}頂点 / " +
                                 $"Humanoid {result.HumanoidBoneCount}ボーン" +
                                 (result.SupplementedJointCount > 0
                                     ? $" / うち補完 {result.SupplementedJointCount}"
                                     : "") + ")";
                    if (!string.IsNullOrEmpty(result.Warning))
                        msg += "\n警告: " + result.Warning;
                    _exportSubPanel?.SetStatus(msg);
                    return null;
                }
                _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
                return result.ErrorMessage;
            }
            catch (Exception ex)
            {
                _exportSubPanel?.SetStatus($"例外: {ex.Message}");
                return $"例外: {ex.Message}";
            }
        }

        /// <summary>PMX 書き出し。失敗理由を返す（成功時は null）。</summary>
        private string OnExportPmx(string outputPath, PMXExportSettings settings)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _exportSubPanel?.SetStatus("モデルがありません"); return "モデルがありません"; }
            try
            {
                var result = PMXExporter.Export(model, outputPath, settings);
                if (result.Success)
                {
                    AuxiliaryBackupWriter.Save(model, outputPath);
                    _exportSubPanel?.SetStatus($"完了: {System.IO.Path.GetFileName(outputPath)}");
                    return null;
                }
                _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
                return result.ErrorMessage;
            }
            catch (Exception ex)
            {
                _exportSubPanel?.SetStatus($"例外: {ex.Message}");
                return $"例外: {ex.Message}";
            }
        }

        /// <summary>MQO 書き出し。失敗理由を返す（成功時は null）。</summary>
        private string OnExportMqo(string outputPath, MQOExportSettings settings)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _exportSubPanel?.SetStatus("モデルがありません"); return "モデルがありません"; }
            try
            {
                var result = MQOExporter.ExportFile(outputPath, model, settings);
                if (result.Success)
                {
                    AuxiliaryBackupWriter.Save(model, outputPath);
                    _exportSubPanel?.SetStatus($"完了: {System.IO.Path.GetFileName(outputPath)}");
                    return null;
                }
                _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
                return result.ErrorMessage;
            }
            catch (Exception ex)
            {
                _exportSubPanel?.SetStatus($"例外: {ex.Message}");
                return $"例外: {ex.Message}";
            }
        }

        /// <summary>プロジェクト保存。失敗理由を返す（成功時は null）。</summary>
        private string OnSaveProject(string path)
        {
            if (string.IsNullOrEmpty(path)) { _projectSaveSubPanel?.SetStatus("パスが指定されていません"); return "パスが指定されていません"; }
            var project = ActiveProject;
            if (project == null) { _projectSaveSubPanel?.SetStatus("プロジェクトがありません"); return "プロジェクトがありません"; }
            var dto = ProjectSerializer.FromProjectContext(project);
            if (dto == null) { _projectSaveSubPanel?.SetStatus("シリアライズ失敗"); return "シリアライズ失敗"; }
            bool ok = ProjectSerializer.Export(path, dto);
            _projectSaveSubPanel?.SetStatus(ok ? "保存完了" : "保存失敗");
            return ok ? null : "保存失敗";
        }

        /// <summary>プロジェクト読込。失敗理由を返す（成功時は null）。</summary>
        private string OnLoadProject(string path)
        {
            if (string.IsNullOrEmpty(path)) { _projectLoadSubPanel?.SetStatus("パスが指定されていません"); return "パスが指定されていません"; }
            var dto = ProjectSerializer.Import(path);
            if (dto == null) { _projectLoadSubPanel?.SetStatus("読込失敗"); return "読込失敗"; }
            var loadedProject = ProjectSerializer.ToProjectContext(dto);
            if (loadedProject == null) { _projectLoadSubPanel?.SetStatus("復元失敗"); return "復元失敗"; }
            _localLoader.Clear();
            foreach (var m in loadedProject.Models)
                _localLoader.LoadModel(m.FilePath ?? dto.name, m);
            AdoptWorkAxisLibrary(loadedProject);
            AdoptCoordinateConvention();
            _projectLoadSubPanel?.SetStatus($"読込完了: {dto.name}");
            return null;
        }

        // path はCSVプロジェクトファイル（任意名の .csv）。モデルフォルダは同ディレクトリ直下。
        /// <summary>プロジェクト CSV 保存。失敗理由を返す（成功時は null）。</summary>
        private string OnSaveCsvProject(string path)
        {
            if (string.IsNullOrEmpty(path)) { _projectSaveSubPanel?.SetStatus("パスが指定されていません"); return "パスが指定されていません"; }
            var project = ActiveProject;
            if (project == null) { _projectSaveSubPanel?.SetStatus("プロジェクトがありません"); return "プロジェクトがありません"; }
            bool ok = CsvProjectSerializer.ExportToFile(path, project);
            _projectSaveSubPanel?.SetStatus(ok ? "CSV保存完了" : "保存失敗");
            return ok ? null : "保存失敗";
        }

        // path はCSVプロジェクトファイル（任意名の .csv）。
        // マージは指定ファイルと同じフォルダ内のメッシュCSVを対象にする。
        /// <summary>プロジェクト CSV 読込。失敗理由を返す（成功時は null）。</summary>
        private string OnLoadCsvProject(string path, bool merge)
        {
            if (string.IsNullOrEmpty(path)) { _projectLoadSubPanel?.SetStatus("パスが指定されていません"); return "パスが指定されていません"; }
            if (merge)
            {
                string mergeFolder = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(mergeFolder)) { _projectLoadSubPanel?.SetStatus("パスが不正です"); return "パスが不正です"; }
                MergeCsvFromFolder(mergeFolder);
                return null;
            }

            var loadedProject = CsvProjectSerializer.ImportFromFile(path, out _, out _);
            if (loadedProject == null) { _projectLoadSubPanel?.SetStatus("読込失敗"); return "読込失敗"; }
            _localLoader.Clear();
            foreach (var m in loadedProject.Models)
                _localLoader.LoadModel(m.FilePath ?? loadedProject.Name, m);
            AdoptWorkAxisLibrary(loadedProject);
            AdoptCoordinateConvention();
            _projectLoadSubPanel?.SetStatus($"CSV読込完了: {loadedProject.Name}");
            return null;
        }

        /// <summary>
        /// 読み込んだプロジェクトの作業軸辞書を、実際に表示されるプロジェクトへ移す。
        ///
        /// 読み込み経路は復元した ProjectContext をそのまま使わず、モデルだけを
        /// _localLoader へ渡して別の ProjectContext を作り直す
        /// （PlayerLocalLoader.FinishLoad）。辞書はモデルにぶら下がっていないので、
        /// ここで明示的に移さないと読み込みのたびに消える。
        /// </summary>
        private void AdoptWorkAxisLibrary(ProjectContext loaded)
        {
            var src = loaded?.WorkAxes;
            var dst = ActiveProject?.WorkAxes;

            // 同一インスタンスなら移す必要はない（Clear して自分を舐めると壊れる）。
            if (src == null || dst == null || ReferenceEquals(src, dst)) return;

            dst.Clear();
            foreach (var name in src.Names)
            {
                if (src.TryGet(name, out var e)) dst.Set(name, e);
            }

            RefreshWorkAxisLibraryLists();
        }

        /// <summary>
        /// 読み込んだモデルの座標規約を EditorStateContext へ流し込む。
        ///
        /// 【なぜ要るか】
        ///   PMX/MQO の倍率と軸反転は ModelContext.CoordinateConvention が正本だが、
        ///   実際に読むのは EditorState を見る側（PMX/MQO の読み書き、VMD・統合
        ///   モーションの位置スケール）である。読み込み直後にここで移さないと、
        ///   保存した規約が効かず既定値のまま動く。
        ///
        /// 【未設定なら触らない】
        ///   CoordinateConvention == null は「規約を持たないモデル」。
        ///   そのときは EditorState の現在値をそのまま使う。
        /// </summary>
        private void AdoptCoordinateConvention()
        {
            var c  = ActiveProject?.CurrentModel?.CoordinateConvention;
            var es = _editOps?.UndoController?.EditorState;
            if (c == null || es == null) return;

            es.PmxUnityRatio = c.PmxUnityRatio;
            es.PmxFlipX      = c.PmxFlipX;
            es.PmxFlipZ      = c.PmxFlipZ;
            es.MqoUnityRatio = c.MqoUnityRatio;
            es.MqoFlipX      = c.MqoFlipX;
            es.MqoFlipZ      = c.MqoFlipZ;
        }

        /// <summary>
        /// 作業軸辞書の一覧を持つパネルをまとめて更新する。
        /// 左ペインと変形パネルは同じ辞書を指すので、片方の変更を両方へ反映させる。
        /// </summary>
        private void RefreshWorkAxisLibraryLists()
        {
            _workAxisSubPanel?.RefreshLibraryList();
            _deformWorkAxisSubPanel?.RefreshLibraryList();
        }

        // ==== ピボット重心スナップ（頂点のみ移動＋カメラ逆移動で「ピボットが動いた」ように見せる） ====
        // C = 目標重心(world)、P = 現ピボット原点(world)、Δ = C − P。
        // 原点・ボーンは動かさず、当該メッシュの全頂点を −Δ 相当だけローカルにシフトし、
        // カメラ Target を −Δ 動かす（見た目静止・ピボットが重心へ来たように見せる）。
        private void MovePivotToCentroid(bool useBones)
        {
            var model = ActiveProject?.CurrentModel;
            var ctx   = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (model == null || ctx == null) return;

            var mc = ctx.ActiveMeshContext;
            var mo = mc?.MeshObject;
            if (mo == null || mo.VertexCount == 0)
            {
                Debug.LogWarning("[Pivot] 頂点を持つメッシュが選択されていません。");
                return;
            }

            // 目標重心 C（world）
            Vector3 C;
            if (useBones)
            {
                Vector3 sum = Vector3.zero; int nb = 0;
                var bones = ctx.SelectedMeshContexts;
                if (bones != null)
                    foreach (var b in bones)
                        if (b != null && b.Type == MeshType.Bone) { sum += (Vector3)b.WorldMatrix.GetColumn(3); nb++; }
                if (nb == 0) { Debug.LogWarning("[Pivot] ボーンが選択されていません。"); return; }
                C = sum / nb;
            }
            else
            {
                var sel = ctx.SelectedVertices;
                if (sel == null || sel.Count == 0) { Debug.LogWarning("[Pivot] 頂点が選択されていません。"); return; }
                Vector3 sum = Vector3.zero; int nv = 0;
                foreach (int idx in sel)
                {
                    if (idx < 0 || idx >= mo.VertexCount) continue;
                    sum += mc.WorldMatrix.MultiplyPoint3x4(mo.Vertices[idx].Position);
                    nv++;
                }
                if (nv == 0) { Debug.LogWarning("[Pivot] 有効な選択頂点がありません。"); return; }
                C = sum / nv;
            }

            Vector3 P = mc.WorldMatrix.GetColumn(3);        // 現ピボット原点(world)
            Vector3 deltaWorld = C - P;
            if (deltaWorld.sqrMagnitude < 1e-12f) return;    // 既に一致

            // 原点は不変のまま、全頂点を −Δ 相当だけローカルにシフト
            Vector3 localShift = mc.WorldMatrix.inverse.MultiplyVector(-deltaWorld);

            int count = mo.VertexCount;
            var indices = new int[count];
            var oldPos  = new Vector3[count];
            var newPos  = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                indices[i] = i;
                var v = mo.Vertices[i];
                oldPos[i] = v.Position;
                v.Position += localShift;
                mo.Vertices[i] = v;
                newPos[i] = v.Position;
            }

            // Undo 記録
            if (_editOps?.UndoController != null)
            {
                int mcIndex = model.MeshContextList.IndexOf(mc);
                var entry = new MeshMoveEntry
                {
                    MeshContextIndex = mcIndex,
                    Indices = indices,
                    OldPositions = oldPos,
                    NewPositions = newPos
                };
                var record = new MultiMeshVertexMoveRecord(new[] { entry });
                _editOps.UndoController.FocusVertexEdit();
                _editOps.UndoController.VertexEditStack.Record(record, useBones ? "Pivot→ボーン重心" : "Pivot→頂点重心");
            }

            // 同期＋カメラ逆移動（Target を −Δ 動かして見た目静止）
            _viewportManager.SyncMeshPositionsAndTransform(mc, model);
            var orbit = _activeViewport?.Orbit;
            if (orbit != null) orbit.SetTarget(orbit.Target - deltaWorld);
            _activePanel?.MarkDirtyRepaint();
        }

        private void MergeCsvFromFolder(string folderPath)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _projectLoadSubPanel?.SetStatus("モデルがありません"); return; }
            if (string.IsNullOrEmpty(folderPath)) { _projectLoadSubPanel?.SetStatus("パスが指定されていません"); return; }

            var entries = CsvModelSerializer.LoadAllMeshEntriesFromFolder(folderPath);
            if (entries == null || entries.Count == 0) { _projectLoadSubPanel?.SetStatus("読み込めるデータがありません"); return; }

            int added = 0, replaced = 0;
            var existingNames = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var mc = model.MeshContextList[i];
                if (mc != null && !string.IsNullOrEmpty(mc.Name))
                    existingNames[mc.Name] = i;
            }
            foreach (var entry in entries)
            {
                if (entry.MeshContext == null) continue;
                string name = entry.MeshContext.Name ?? "";
                entry.MeshContext.ParentModelContext = model;
                if (existingNames.TryGetValue(name, out int existIdx))
                { model.MeshContextList[existIdx] = entry.MeshContext; replaced++; }
                else
                { model.Add(entry.MeshContext); added++; }
            }
            bool hasNameBased = false;
            foreach (var e in entries) { if (e.IsNameBased) { hasNameBased = true; break; } }
            if (hasNameBased)
            {
                var nameToIndex = new System.Collections.Generic.Dictionary<string, int>();
                for (int i = 0; i < model.MeshContextList.Count; i++)
                {
                    var mc = model.MeshContextList[i];
                    if (mc != null && !string.IsNullOrEmpty(mc.Name) && !nameToIndex.ContainsKey(mc.Name))
                        nameToIndex[mc.Name] = i;
                }
                CsvMeshSerializer.ResolveNameReferences(entries, nameToIndex);
            }
            CsvModelSerializer.BuildMirrorPairsFromEntries(entries, model);

            // Phase 2a-2b-2 Batch 3: ClearScene + RebuildAdapter を EnterSceneReset(clearScene: true) に集約。
            // MergeCsv は selection を変更しないため、EnterSceneReset 内の SetSelectionState は
            // current selection (first mesh) を再セットする形となる。
            _viewportManager.EnterSceneReset(ActiveProject, clearScene: true);
            model.OnListChanged?.Invoke();

            _projectLoadSubPanel?.SetStatus($"マージ完了: +{added} /{replaced}置換");
            Debug.Log($"[PlayerViewerCore] MergeCsv: added={added}, replaced={replaced}");
        }

        private void OnPartialImportDone(bool topologyChanged)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return;
            // Phase 2a-2b-2 Batch 3: ClearScene + RebuildAdapter + SetSelectionState を
            // EnterSceneReset(clearScene: true) に集約。
            _viewportManager.EnterSceneReset(ActiveProject, clearScene: true);
        }

        private void OnMeshFilterToSkinnedComplete()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return;
            // Phase 2a-2b-2 Batch 3: ClearScene + RebuildAdapter + SetSelectionState +
            // UpdateSelectedDrawableMesh を EnterSceneReset(clearScene: true) に集約。
            _viewportManager.EnterSceneReset(ActiveProject, clearScene: true);
            _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
            RebuildModelList();
            NotifyPanels(ChangeKind.ModelSwitch);
        }
    }
}
