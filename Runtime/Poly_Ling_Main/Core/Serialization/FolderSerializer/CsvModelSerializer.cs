// Assets/Editor/Poly_Ling/Serialization/FolderSerializer/CsvModelSerializer.cs
// モデルフォルダ内のCSVファイル読み書き
// model.csv, materials.csv, materialprops.csv, humanoid.export.csv, morphgroups.csv,
// editorstate.csv, workplane.csv, springbonegroups.csv, previewsettings.csv
// vrmmeta.csv, vrmlookat.csv, avatarsettings.csv, coordinate.csv
// + mesh/bone/morph/pmx_physics CSVの振り分け
//
// 【分割先】このファイルから次へ分けてある。
//   CsvModelSerializer.EditorCsv.cs  CSV モデル入出力：editorstate・workplane・workaxis・メッシュエントリの読み込み・TPoseBackup。
//   CsvModelSerializer.ModelCsv.cs   CSV モデル入出力：model.csv・materials.csv・materialprops.csv・humanoid.export.csv・morphgroups.csv。
//   CsvModelSerializer.StoreCsv.cs   CSV モデル入出力：meshselsets.csv・datastore.csv・objectgroups.csv・mirrorpairs.csv。
//   CsvModelSerializer.Texture.cs    CSV モデル入出力：テクスチャの保存・読み込みとユーティリティ。
//   CsvModelSerializer.VrmCsv.cs     CSV モデル入出力：springbonegroups・vrmmeta・vrmlookat・coordinate・avatarsettings・previewsettings。
//   ModelEntry.cs                    モデルフォルダ内のメッシュエントリ（CsvModelSerializer から分離）。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Materials;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Serialization.FolderSerializer
{

    /// <summary>
    /// モデルフォルダのCSV読み書き
    /// </summary>
    public static partial class CsvModelSerializer
    {
        // ================================================================
        // Save: モデルフォルダ一式を書き出す
        // ================================================================

        /// <summary>
        /// ModelContextをフォルダに保存
        /// </summary>
        /// <param name="useNameBased">名前ベース参照モード</param>
        public static void SaveModel(
            string modelFolderPath,
            ModelContext model,
            EditorStateDTO editorState = null,
            WorkPlaneContext workPlane = null,
            bool useNameBased = false)
        {
            if (model == null) return;
            Directory.CreateDirectory(modelFolderPath);

            string modelName = SanitizeFileName(model.Name ?? "Model");

            // 名前ベース用: インデックス→名前辞書を構築
            Dictionary<int, string> indexToName = null;
            if (useNameBased)
            {
                indexToName = new Dictionary<int, string>();
                for (int idx = 0; idx < model.MeshContextCount; idx++)
                {
                    var m = model.GetMeshContext(idx);
                    if (m != null)
                        indexToName[idx] = m.Name ?? $"Unnamed_{idx}";
                }
            }

            // メッシュをタイプ別に分類
            var meshEntries = new List<CsvMeshEntry>();
            var boneEntries = new List<CsvMeshEntry>();
            var morphEntries = new List<CsvMeshEntry>();

            // 剛体 / JOINT は PMX 物理のメタデータ（頂点ゼロ）。
            // 形状ファイルに混ぜず専用ファイルへ振る。
            var pmxPhysicsEntries = new List<CsvMeshEntry>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                var entry = new CsvMeshEntry { GlobalIndex = i, MeshContext = mc };

                switch (mc.Type)
                {
                    case MeshType.Bone:
                        boneEntries.Add(entry);
                        break;
                    case MeshType.Morph:
                        morphEntries.Add(entry);
                        break;
                    case MeshType.RigidBody:
                    case MeshType.RigidBodyJoint:
                        pmxPhysicsEntries.Add(entry);
                        break;
                    default:
                        meshEntries.Add(entry);
                        break;
                }
            }

            // MirrorPair情報をReal側entryに設定 + MirrorSide同梱
            EnrichEntriesWithMirrorPeers(meshEntries, model);

            // IK: 集約 Links → per-bone を同期してから書き出す（規約4：CSV/JSON 対称）
            IKChainResolver.SyncPerBoneFromLinks(model);

            // Humanoid: 集中 Dict → per-bone を同期（humanBodyBone を bone.csv へ）
            HumanoidMappingResolver.SyncPerBoneFromMapping(model);

            // mesh/bone/morph CSV
            string meshFile = $"{modelName}.mesh.csv";
            string boneFile = $"{modelName}.bone.csv";
            string morphFile = $"{modelName}.morph.csv";
            string pmxPhysicsFile = $"{modelName}.pmx_physics.csv";

            // 空の種別は書き出さないが、以前の保存で作られたファイルが残っていると
            // 読込時に index 空間が重なって上書きが起きる。書かない種別は削除する。
            WriteOrDeleteMeshCsv(Path.Combine(modelFolderPath, meshFile),  meshEntries,  "mesh",  useNameBased, indexToName);
            WriteOrDeleteMeshCsv(Path.Combine(modelFolderPath, boneFile),  boneEntries,  "bone",  useNameBased, indexToName);
            WriteOrDeleteMeshCsv(Path.Combine(modelFolderPath, morphFile), morphEntries, "morph", useNameBased, indexToName);
            WriteOrDeleteMeshCsv(Path.Combine(modelFolderPath, pmxPhysicsFile), pmxPhysicsEntries, "pmx_physics", useNameBased, indexToName);

            // model.csv (順序マスター)
            WriteModelCsv(modelFolderPath, model, meshEntries, boneEntries, morphEntries,
                pmxPhysicsEntries, useNameBased);

            // materials.csv
            WriteMaterialsCsv(modelFolderPath, model);

            // materialprops.csv（シェーダー固有プロパティ。materials.csv の列を伸ばさないため独立CSV）
            WriteMaterialPropsCsv(modelFolderPath, model);

            // humanoid.export.csv（書き出し専用の派生。読み戻さない）
            if (model.HumanoidMapping != null && !model.HumanoidMapping.IsEmpty)
                WriteHumanoidCsv(modelFolderPath, model, useNameBased, indexToName);

            // morphgroups.csv
            if (model.MorphExpressions != null && model.MorphExpressions.Count > 0)
                WriteMorphGroupsCsv(modelFolderPath, model, useNameBased, indexToName);

            // vrmexpressions.csv（VRM 表情の付帯データ。無ければ古いファイルを消す）
            WriteVrmExpressionsCsv(modelFolderPath, model);

            // meshselsets.csv
            if (model.MeshSelectionSets != null && model.MeshSelectionSets.Count > 0)
                WriteMeshSelSetsCsv(modelFolderPath, model);

            // objectgroups.csv
            if (model.ObjectGroups != null && model.ObjectGroups.Count > 0)
                WriteObjectGroupsCsv(modelFolderPath, model);

            // datastore.csv（コマンドが返した実データの辞書）
            //   空のときはファイルを消す。残しておくと、前回の保存で作られた
            //   項目が次の読込で復活する。
            WriteOrDeleteDataStoreCsv(modelFolderPath, model);

            // mirrorpairs.csv
            if (model.MirrorPairs != null && model.MirrorPairs.Count > 0)
                WriteMirrorPairsCsv(modelFolderPath, model, useNameBased);

            // editorstate.csv
            if (editorState != null)
                WriteEditorStateCsv(modelFolderPath, editorState, useNameBased, indexToName);

            // workplane.csv
            if (workPlane != null)
                WriteWorkPlaneCsv(modelFolderPath, workPlane);

            // workaxis.csv（作業用ローカル軸。引数ではなく model から直接読む）
            if (model.WorkAxis != null)
                WriteWorkAxisCsv(modelFolderPath, model.WorkAxis);

            // tposebackup.csv
            if (model.TPoseBackup != null)
                WriteTPoseBackupCsv(modelFolderPath, model.TPoseBackup, useNameBased, indexToName);

            // springbonegroups.csv（SpringBone コライダーグループ名。規約4: CSV/JSON 対称）
            if (model.SpringBoneColliderGroupNames != null && model.SpringBoneColliderGroupNames.Count > 0)
                WriteSpringBoneGroupsCsv(modelFolderPath, model);

            // previewsettings.csv（PolyLing 内のプレビュー評価設定。
            //   既定値でも往復対称のため常時出力）
            WritePreviewSettingsCsv(modelFolderPath, model);

            // vrmmeta.csv / vrmlookat.csv（VRM 出力用。未設定なら書かない）
            //   未設定（null）とファイル無しを同じ意味にそろえるため、
            //   null のときはファイルを消す。previewsettings.csv と違い
            //   既定値と未設定で出力が変わる（VRM の既定に任せるかどうか）ため。
            WriteOrDeleteVrmMetaCsv(modelFolderPath, model);
            WriteOrDeleteVrmLookAtCsv(modelFolderPath, model);

            // avatarsettings.csv（Avatar リターゲット設定。未設定なら書かない）
            WriteOrDeleteAvatarRetargetCsv(modelFolderPath, model);

            // coordinate.csv（PMX/MQO の座標規約。未設定なら書かない）
            WriteOrDeleteCoordinateCsv(modelFolderPath, model);

            // textures フォルダにテクスチャをコピー
            string texturesFolder = Path.Combine(modelFolderPath, "textures");
            Directory.CreateDirectory(texturesFolder);
            SaveTextures(texturesFolder, model);
        }

        // ================================================================
        // Load: モデルフォルダからModelContextを復元
        // ================================================================

        /// <summary>
        /// フォルダからModelContextを復元
        /// </summary>
        public static ModelContext LoadModel(
            string modelFolderPath,
            out EditorStateDTO editorState,
            out WorkPlaneContext workPlane,
            out List<CsvMeshEntry> additionalEntries)
        {
            editorState = null;
            workPlane = null;
            additionalEntries = new List<CsvMeshEntry>();

            if (!Directory.Exists(modelFolderPath)) return null;

            // model.csv 読み込み
            string modelCsvPath = Path.Combine(modelFolderPath, "model.csv");
            var (modelName, entries) = ReadModelCsv(modelCsvPath);

            if (entries == null || entries.Count == 0) return null;

            var model = new ModelContext(modelName);
            string modelPrefix = SanitizeFileName(modelName).ToLowerInvariant() + ".";

            // メッシュファイルを読み込み（自モデル / 追加を分離）
            var ownMeshEntries = new Dictionary<string, List<CsvMeshEntry>>();

            // glob は "*.<type>.csv"。pmx_physics は mesh とは別の綴りなので衝突しない。
            // mesh の走査はそのまま残すため、旧フォルダの mesh.csv に入っている
            // 剛体 / JOINT もこれまでどおり読める。
            foreach (var type in new[] { "mesh", "bone", "morph", "pmx_physics" })
            {
                var csvFiles = Directory.GetFiles(modelFolderPath, $"*.{type}.csv");
                var ownList = new List<CsvMeshEntry>();

                foreach (var csvFile in csvFiles)
                {
                    string fileName = Path.GetFileName(csvFile).ToLowerInvariant();
                    var loaded = CsvMeshSerializer.ReadFile(csvFile);

                    if (fileName.StartsWith(modelPrefix))
                    {
                        // 自モデルのファイル
                        ownList.AddRange(loaded);
                    }
                    else
                    {
                        // 他モデルのファイル → 追加エントリ
                        additionalEntries.AddRange(loaded);
                    }
                }

                ownMeshEntries[type] = ownList;
            }

            // model.csv のentry順にMeshContextListを構築
            // entryByName形式かentry形式かで検索方法を変える
            bool hasNameBasedEntries = entries.Exists(e => e.IsNameBased);

            // 全読み込みエントリをフラットリスト化
            var allOwnEntries = new List<CsvMeshEntry>();
            foreach (var kvp in ownMeshEntries)
                allOwnEntries.AddRange(kvp.Value);

            if (hasNameBasedEntries)
            {
                // 名前ベース: model.csv の entryByName 順に名前で検索
                var nameLookup = new Dictionary<string, CsvMeshEntry>();
                foreach (var me in allOwnEntries)
                {
                    if (me.MeshContext != null && !string.IsNullOrEmpty(me.MeshContext.Name))
                    {
                        // 重複名の場合は最初のもの優先
                        if (!nameLookup.ContainsKey(me.MeshContext.Name))
                            nameLookup[me.MeshContext.Name] = me;
                    }
                }

                foreach (var entry in entries)
                {
                    if (nameLookup.TryGetValue(entry.Name, out var found))
                    {
                        model.Add(found.MeshContext);
                    }
                    else
                    {
                        Debug.LogWarning($"[CsvModelSerializer] MeshContext not found for name={entry.Name}");
                    }
                }
            }
            else
            {
                // インデックスベース: 従来通り
                var lookup = new Dictionary<int, MeshContext>();
                foreach (var me in allOwnEntries)
                {
                    // 同じ index が複数ファイルに現れると後勝ちで上書きされる。
                    // 古い *.bone.csv / *.morph.csv の残留が典型なので警告を出す。
                    if (lookup.TryGetValue(me.GlobalIndex, out var prev))
                    {
                        Debug.LogWarning(
                            $"[CsvModelSerializer] index={me.GlobalIndex} が重複している: " +
                            $"'{prev?.Name}'({prev?.Type}) → '{me.MeshContext?.Name}'({me.MeshContext?.Type}) で上書き。" +
                            "モデルフォルダに古い *.bone.csv / *.morph.csv が残っていないか確認すること。");
                    }
                    lookup[me.GlobalIndex] = me.MeshContext;
                }

                foreach (var entry in entries)
                {
                    if (lookup.TryGetValue(entry.GlobalIndex, out var mc))
                    {
                        model.Add(mc);
                    }
                    else
                    {
                        Debug.LogWarning($"[CsvModelSerializer] MeshContext not found for globalIndex={entry.GlobalIndex} ({entry.Name})");
                    }
                }
            }

            // 名前ベースエントリの参照解決（親/IK/BoneWeight等）
            if (hasNameBasedEntries)
            {
                var nameToIndex = BuildNameToIndex(model);
                CsvMeshSerializer.ResolveNameReferences(allOwnEntries, nameToIndex);
            }

            // 協働編集: 安定オブジェクトIDの整合
            //   - objectId 行が無い旧形式ファイル → ここで新規発行
            //   - 別々に保存されたモデルを取り込んで衝突 → 後勝ちで振り直し
            //   ID が確定していないと担当（editorName）の追跡ができないため、
            //   参照解決の直後・利用開始前に必ず通す。
            Poly_Ling.Data.ObjectIdAllocator.ResolveDuplicates(model.MeshContextList);

            // materials.csv
            ReadMaterialsCsv(modelFolderPath, model);

            // materialprops.csv（materials.csv の直後に読む。無い場合は ShaderProperties は null のまま）
            ReadMaterialPropsCsv(modelFolderPath, model);

            // textures フォルダからテクスチャを復元
            LoadTextures(modelFolderPath, model);

            // humanoid.csv
            //   ※#5b（案A）: humanoid.csv は AvatarBuilder 向けの派生エクスポート（書き出し専用）。
            //     canonical は per-bone（bone.csv の humanBodyBone）。読込は行わず、
            //     Deserialize 末尾の RebuildMappingFromPerBone で Dict を再構築する。

            // morphgroups.csv
            ReadMorphGroupsCsv(modelFolderPath, model);

            // vrmexpressions.csv（morphgroups.csv の並びを索引に使うので、その後に読む）
            ReadVrmExpressionsCsv(modelFolderPath, model);

            // meshselsets.csv
            ReadMeshSelSetsCsv(modelFolderPath, model);

            // objectgroups.csv
            ReadObjectGroupsCsv(modelFolderPath, model);

            // datastore.csv
            ReadDataStoreCsv(modelFolderPath, model);

            // mirrorpairs.csv
            ReadMirrorPairsCsv(modelFolderPath, model);

            // エントリのmirrorPeer情報からもMirrorPairを構築
            // （部分インポート用：mirrorpairs.csvが無い場合でもペアを復元）
            BuildMirrorPairsFromEntries(allOwnEntries, model);

            // editorstate.csv
            string esPath = Path.Combine(modelFolderPath, "editorstate.csv");
            if (File.Exists(esPath))
                editorState = ReadEditorStateCsv(esPath, model);

            // workplane.csv
            string wpPath = Path.Combine(modelFolderPath, "workplane.csv");
            if (File.Exists(wpPath))
                workPlane = ReadWorkPlaneCsv(wpPath);

            // workaxis.csv（作業用ローカル軸。out 引数ではなく model へ直接入れる）
            if (model.WorkAxis == null) model.WorkAxis = new WorkAxisContext();
            string waPath = Path.Combine(modelFolderPath, "workaxis.csv");
            if (File.Exists(waPath))
                ReadWorkAxisCsv(waPath, model.WorkAxis);
            else
                model.WorkAxis.Reset();

            // tposebackup.csv
            string tpPath = Path.Combine(modelFolderPath, "tposebackup.csv");
            if (File.Exists(tpPath))
                model.TPoseBackup = ReadTPoseBackupCsv(tpPath, model);

            // springbonegroups.csv（SpringBone コライダーグループ名）
            string sbgPath = Path.Combine(modelFolderPath, "springbonegroups.csv");
            if (File.Exists(sbgPath))
                ReadSpringBoneGroupsCsv(sbgPath, model);

            // previewsettings.csv（プレビュー評価設定。無い場合は既定値のまま）
            string psPath = Path.Combine(modelFolderPath, "previewsettings.csv");
            if (File.Exists(psPath))
                ReadPreviewSettingsCsv(psPath, model);

            // vrmmeta.csv / vrmlookat.csv（無ければ未設定のまま）
            string vmPath = Path.Combine(modelFolderPath, "vrmmeta.csv");
            if (File.Exists(vmPath))
                ReadVrmMetaCsv(vmPath, model);

            string vlPath = Path.Combine(modelFolderPath, "vrmlookat.csv");
            if (File.Exists(vlPath))
                ReadVrmLookAtCsv(vlPath, model);

            // avatarsettings.csv（無ければ未設定のまま）
            string arPath = Path.Combine(modelFolderPath, "avatarsettings.csv");
            if (File.Exists(arPath))
                ReadAvatarRetargetCsv(arPath, model);

            // coordinate.csv（無ければ未設定のまま）
            string ccPath = Path.Combine(modelFolderPath, "coordinate.csv");
            if (File.Exists(ccPath))
                ReadCoordinateCsv(ccPath, model);

            // IK: per-bone → 集約 Links / TargetIndex を再構築（消費側は集約を読む）
            IKChainResolver.RebuildLinksFromPerBone(model);

            // Humanoid: per-bone → 集中 Dict を再構築（消費側は Dict を読む）
            HumanoidMappingResolver.RebuildMappingFromPerBone(model);

            return model;
        }
    }
}
