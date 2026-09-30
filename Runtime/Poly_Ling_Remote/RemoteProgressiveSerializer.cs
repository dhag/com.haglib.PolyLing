// Remote/RemoteProgressiveSerializer.cs
// プログレッシブ転送プロトコル シリアライザ/デシリアライザ
//
// 送受信フレーム:
//   PLRH SerializeProjectHeader / DeserializeProjectHeader
//   PLRM SerializeModelMeta     / DeserializeModelMeta
//   PLRS SerializeMeshSummary   / DeserializeMeshSummary
//   PLRD SerializeMeshData      / DeserializeMeshData
//   PLRB BuildBatch             / (RemoteProjectReceiver.ProcessBatch)
//   プロジェクト全体 SerializeWholeProject / DeserializeWholeProject
//
// 【決まり】シリアライザ（CSV / JSON(.mfproj) / このバイナリ）は 3 つ揃えて直す。
//   CSV や JSON に項目を足したら、ここにも足す（リモート送信はこのバイナリで行う）。
//   確認は saveProjectCsv → saveProjectBinary → loadProjectBinary → saveProjectCsv の
//   2 つの CSV を比べる（PolyLing_追加作業の必読.md の E）。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.EditorBridge;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.Materials;

namespace Poly_Ling.Remote
{
    public static partial class RemoteProgressiveSerializer
    {
        // ================================================================
        // PLRH — ProjectHeader
        // [4B] Magic  [1B] Version  [1B] CurrentModelIndex  [2B] ModelCount
        // [string] ProjectName
        // [WorkAxes] (v2+) 作業軸辞書（workaxis_library.csv と同じ項目）。v1 の受信側は読まずに終わる。
        // ================================================================

        public static byte[] SerializeProjectHeader(ProjectContext project)
        {
            if (project == null) return null;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.ProjectHeader);
                w.Write((byte)2);
                w.Write((byte)project.CurrentModelIndex);
                w.Write((ushort)project.ModelCount);
                WriteString(w, project.Name);
                WriteWorkAxisLibrary(w, project.WorkAxes);
                return ms.ToArray();
            }
        }

        /// <summary>PLRH の v2 以降の付帯（作業軸辞書）を読んで project へ入れる。v1 なら何もしない。</summary>
        public static void ReadProjectHeaderExtras(byte[] data, ProjectContext project)
        {
            if (data == null || data.Length < 8 || project == null) return;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (r.ReadUInt32() != RemoteMagic.ProjectHeader) return;
                int version = r.ReadByte();
                r.ReadByte(); r.ReadUInt16();
                ReadString(r);
                if (version < 2) return;
                if (project.WorkAxes == null) project.WorkAxes = new WorkAxisLibrary();
                ReadWorkAxisLibrary(r, project.WorkAxes);
            }
        }

        public static (string projectName, int modelCount, int currentModelIndex)? DeserializeProjectHeader(byte[] data)
        {
            if (data == null || data.Length < 8) return null;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (r.ReadUInt32() != RemoteMagic.ProjectHeader) return null;
                r.ReadByte(); // version
                int currentModelIndex = r.ReadByte();
                int modelCount = r.ReadUInt16();
                string name = ReadString(r);
                return (name, modelCount, currentModelIndex);
            }
        }

        // ================================================================
        // PLRM — ModelMeta
        // [4B] Magic  [1B] Version  [1B] Padding  [2B] ModelIndex
        // [string] ModelName  [2B] MeshCount  [1B] ActiveCategory
        // [2B] SelectedCount  [int32 × SelectedCount]
        // [2B] MaterialCount  [MaterialData × MaterialCount]
        //   ※ Version 2 で MaterialData に拡張ブロックを追加（ShaderName / テクスチャST /
        //     RenderQueueOffset / ZWriteOverride / ZTest / DoubleSidedGI / GPUInstancing /
        //     ShaderProperties）。Version 1 の受信側とは非互換のため Editor/Player を同時更新すること。
        // [2B] ExpressionCount  [MorphExpression × ExpressionCount]
        //   MorphExpression: [string] Name  [string] NameEnglish  [1B] Panel  [1B] Type  [1B] IsSymmetric
        //                    [2B] EntryCount  ([2B] MeshIndex  [4B] Weight) × EntryCount
        //   ※ Version 3 で末尾に ObjectGroup ブロックを追加。
        //     [2B] GroupCount  [ObjectGroup × GroupCount]
        //     ObjectGroup: [string] Name  [string] Action
        //                  [8B] OutputObjectId  [8B] StashObjectId
        //                  [1B] AutoUpdate  [string] SourceDigest
        //                  [2B] ArgCount     ([string] Key [string] Value) × ArgCount
        //                  [2B] MeshRefCount ([string] Key [2B] IdCount [8B × IdCount]) × MeshRefCount
        //     参照は ObjectId なので受信側でメッシュ索引へ読み替える必要がない。
        //     Version 2 の受信側とは非互換のため Editor/Player を同時更新すること。
        //   ※ Version 4 で ObjectGroup をステップ列にした。
        //     ObjectGroup: [string] Name  [8B] StashObjectId
        //                  [1B] AutoUpdate  [string] SourceDigest
        //                  [2B] StepCount  [Step × StepCount]
        //     Step: [string] Action
        //           [2B] OutCount     [8B × OutCount]
        //           [2B] ArgCount     ([string] Key [string] Value) × ArgCount
        //           [2B] MeshRefCount ([string] Key [2B] IdCount [8B × IdCount]) × MeshRefCount
        //     出力先が複数ありうる（はしごから作る鎖は 1 回で何本もできる）ため
        //     単数の OutputObjectId をやめた。読みは Version 3 の形も受ける。
        //     Version 3 の受信側とは非互換のため Editor/Player を同時更新すること。
        //   ※ Version 5 で意味情報を追加した。足したものはすべて既存の欄の直後で、
        //     ObjectGroup と Step のそれぞれに固まって入る。
        //     ObjectGroup: … [string] SourceDigest
        //                  [string] Goal
        //                  [2B] PreconditionCount  [string × N]
        //                  [2B] SuccessCriterionCount [string × N]
        //                  [2B] TagCount           [string × N]
        //                  [string] ProvParentName [string] ProvChangeSummary [string] ProvCreatedBy
        //                  [2B] StepCount  [Step × StepCount]
        //     Step: [string] Action  [string] ElementId  [1B] Kind  [string] Purpose
        //           [2B] OutCount … （以下 Version 4 と同じ）
        //     Kind は ObjectGroupStepKind。0 = 実行する段で、Version 4 以前は全部 0。
        //     読みは Version 3 / 4 の形も受ける。
        //     Version 4 の受信側とは非互換のため Editor/Player を同時更新すること。
        //   ※ Version 6 で参照段を追加した。Step の Purpose の直後に足す。
        //     Step: … [string] Purpose  [string] RefName  [1B] ExpansionPolicy
        //           [2B] OutCount … （以下 Version 5 と同じ）
        //     RefName は Kind = ScenarioRef の段だけが使う。ExpansionPolicy は
        //     ScenarioExpansionPolicy で、0 = Reference。Version 5 以前は全部 0。
        //     読みは Version 3 / 4 / 5 の形も受ける。
        //     Version 5 の受信側とは非互換のため Editor/Player を同時更新すること。
        //   ※ Version 7 で末尾に [8B] ActiveWorkAxisObjectId（使う作業軸の ObjectId。0 は未指定）を追加。
        //     Version 6 以前の受信側は末尾のこの欄を読まずに終わる（読み終わりを検査しない）。
        //   ※ Version 8 で各材質の末尾に AssetPath・金属度・滑らかさ・法線強度・遮蔽強度・
        //     ブレンド・各テクスチャ経路（materials.csv と同じ項目）を追加。材質は途中に並ぶので
        //     Version 7 以前の受信側とは非互換。Editor/Player を同時更新すること。
        // ================================================================

        public static byte[] SerializeModelMeta(ModelContext model, int modelIndex)
        {
            if (model == null) return null;

            // IK: 集約 Links → per-bone（IKLink）を同期してから送る（CSV / JSON の保存と同じ）。
            // PLRS の v6 は per-bone の IKLink を運び、受信側で RebuildLinksFromPerBone する。
            Poly_Ling.Ops.IKChainResolver.SyncPerBoneFromLinks(model);

            // Humanoid: 集中 Dict → per-bone（HumanBodyBone）を同期してから送る（CSV と同じ）。
            Poly_Ling.Ops.HumanoidMappingResolver.SyncPerBoneFromMapping(model);

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.ModelMeta);
                w.Write((byte)9);   // version 9: モデル単位の付帯（既定材質・VRM・下絵など CSV と揃える）。v8: 材質の拡張（金属度・滑らかさ・各テクスチャ経路・AssetPath）。v7: 使う作業軸の ID。v6: 参照段（RefName / ExpansionPolicy）。v5: 意味情報、v4: ステップ列、v3: ObjectGroup ブロック
                w.Write((byte)0); // padding
                w.Write((short)modelIndex);

                WriteString(w, model.Name);
                w.Write((ushort)model.Count);
                w.Write((byte)model.ActiveCategory);

                var sel = model.SelectedDrawableMeshIndices;
                w.Write((ushort)sel.Count);
                for (int i = 0; i < sel.Count; i++)
                    w.Write(sel[i]);

                var matRefs = model.MaterialReferences;
                w.Write((ushort)matRefs.Count);
                for (int i = 0; i < matRefs.Count; i++)
                    WriteMaterialData(w, matRefs[i]?.Data ?? new MaterialData(), matRefs[i]?.AssetPath);

                var exprs = model.MorphExpressions;
                w.Write((ushort)exprs.Count);
                for (int i = 0; i < exprs.Count; i++)
                {
                    var expr = exprs[i];
                    WriteString(w, expr.Name ?? "");
                    WriteString(w, expr.NameEnglish ?? "");
                    w.Write((byte)expr.Panel);
                    w.Write((byte)expr.Type);
                    w.Write(expr.IsSymmetric);
                    w.Write((ushort)expr.MeshEntries.Count);
                    for (int j = 0; j < expr.MeshEntries.Count; j++)
                    {
                        w.Write((short)expr.MeshEntries[j].MeshIndex);
                        w.Write(expr.MeshEntries[j].Weight);
                    }
                }

                // ── ObjectGroups（version 3 で追加 / version 4 でステップ列へ）
                //    並びは保存と同じくキー順に固定する。Dictionary の列挙順は
                //    保証されないため、固定しないと同じ内容でもバイト列が変わる。
                var groups = model.ObjectGroups;
                int groupCount = groups?.Count ?? 0;
                w.Write((ushort)groupCount);
                for (int i = 0; i < groupCount; i++)
                {
                    var g = groups[i];
                    if (g == null)
                    {
                        // 空のグループとして詰めておく（件数と実体をずらさない）。
                        WriteString(w, "");
                        w.Write(0UL);
                        w.Write(false); WriteString(w, "");
                        WriteString(w, "");                                     // Goal
                        w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); // 前提 / 成功条件 / 札
                        WriteString(w, ""); WriteString(w, ""); WriteString(w, ""); // 由来
                        w.Write((ushort)0);
                        continue;
                    }

                    WriteString(w, g.Name ?? "");
                    w.Write(g.StashObjectId);
                    w.Write(g.AutoUpdate);
                    WriteString(w, g.SourceDigest ?? "");

                    WriteString(w, g.Goal ?? "");
                    WriteStringList(w, g.Preconditions);
                    WriteStringList(w, g.SuccessCriteria);
                    WriteStringList(w, g.Tags);

                    var prov = g.Provenance;
                    WriteString(w, prov?.ParentName    ?? "");
                    WriteString(w, prov?.ChangeSummary ?? "");
                    WriteString(w, prov?.CreatedBy     ?? "");

                    var steps = g.Steps;
                    int stepCount = steps?.Count ?? 0;
                    w.Write((ushort)stepCount);

                    for (int si = 0; si < stepCount; si++)
                    {
                        var st = steps[si];
                        if (st == null)
                        {
                            // 空のステップとして詰めておく。
                            WriteString(w, "");
                            WriteString(w, "");                 // ElementId
                            w.Write((byte)0);                   // Kind = Command
                            WriteString(w, "");                 // Purpose
                            WriteString(w, "");                 // RefName
                            w.Write((byte)0);                   // ExpansionPolicy = Reference
                            w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0);
                            continue;
                        }

                        WriteString(w, st.Action ?? "");
                        WriteString(w, st.ElementId ?? "");
                        w.Write((byte)st.Kind);
                        WriteString(w, st.Purpose ?? "");
                        WriteString(w, st.RefName ?? "");
                        w.Write((byte)st.ExpansionPolicy);

                        var outIds  = st.OutputObjectIds;
                        int outCount = outIds?.Count ?? 0;
                        w.Write((ushort)outCount);
                        for (int j = 0; j < outCount; j++) w.Write(outIds[j]);

                        var sortedArgs = st.SortedArgs();
                        w.Write((ushort)sortedArgs.Count);
                        for (int j = 0; j < sortedArgs.Count; j++)
                        {
                            WriteString(w, sortedArgs[j].Key ?? "");
                            WriteString(w, sortedArgs[j].Value ?? "");
                        }

                        var sortedRefs = st.SortedMeshRefIds();
                        w.Write((ushort)sortedRefs.Count);
                        for (int j = 0; j < sortedRefs.Count; j++)
                        {
                            WriteString(w, sortedRefs[j].Key ?? "");
                            var ids = sortedRefs[j].Value;
                            int n = ids?.Count ?? 0;
                            w.Write((ushort)n);
                            for (int k = 0; k < n; k++) w.Write(ids[k]);
                        }
                    }
                }

                // ── version 7：使う作業軸（ModelContext.ActiveWorkAxisObjectId）。0 は未指定。
                w.Write(model.ActiveWorkAxisObjectId);

                // ── version 9：CSV のモデル単位ファイルと揃える付帯（RemoteProgressiveSerializer.Extras.cs）
                WriteModelExtras(w, model);

                return ms.ToArray();
            }
        }

        public static (int modelIndex, ModelContext model)? DeserializeModelMeta(byte[] data)
        {
            if (data == null || data.Length < 8) return null;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (r.ReadUInt32() != RemoteMagic.ModelMeta) return null;
                int metaVersion = r.ReadByte(); // version
                r.ReadByte(); // padding
                int modelIndex = r.ReadInt16();

                string name = ReadString(r);
                ushort meshCount = r.ReadUInt16();
                byte activeCategory = r.ReadByte();

                ushort selCount = r.ReadUInt16();
                var selectedIndices = new List<int>(selCount);
                for (int i = 0; i < selCount; i++)
                    selectedIndices.Add(r.ReadInt32());

                var model = new ModelContext(name);

                // メッシュスロットをスタブで事前確保（後続PLRSで上書き）
                for (int i = 0; i < meshCount; i++)
                    model.MeshContextList.Add(new MeshContext { Name = $"Mesh{i}" });


                ushort matCount = r.ReadUInt16();
                var refList = new List<MaterialReference>(matCount);
                for (int i = 0; i < matCount; i++)
                {
                    var (mdata, tex, assetPath) = ReadMaterialData(r, metaVersion);

                    // wire の MaterialData から直接参照を生成する。
                    // Unity Material 経由（model.Materials）だと SetMaterial→GetAssetPath/
                    // FromMaterial が走り、Editor外で EditorBridgeNull がエラーになるため回避。
                    var mref = new MaterialReference(mdata);
                    if (!string.IsNullOrEmpty(assetPath)) mref.AssetPath = assetPath;

                    // テクスチャがある場合のみ Material を生成して付与（Editor呼び出しなし）。
                    if (tex != null)
                    {
                        var mat = MaterialDataConverter.ToMaterial(mdata);
                        if (mat != null)
                        {
                            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                            // tex は本参照が所有する runtime テクスチャ。破棄追跡のため一緒に渡す。
                            mref.AttachRuntimeMaterial(mat, tex);
                        }
                    }
                    refList.Add(mref);
                }
                if (refList.Count > 0)
                    model.MaterialReferences = refList;

                ushort exprCount = r.ReadUInt16();
                for (int i = 0; i < exprCount; i++)
                {
                    var expr = new MorphExpression
                    {
                        Name        = ReadString(r),
                        NameEnglish = ReadString(r),
                        Panel       = r.ReadByte(),
                        Type        = (MorphType)r.ReadByte(),
                        IsSymmetric = r.ReadBoolean()
                    };
                    ushort entryCount = r.ReadUInt16();
                    for (int j = 0; j < entryCount; j++)
                    {
                        int   meshIndex = r.ReadInt16();
                        float weight    = r.ReadSingle();
                        expr.MeshEntries.Add(new MorphMeshEntry(meshIndex, weight));
                    }
                    model.MorphExpressions.Add(expr);
                }

                // ── ObjectGroups（version 3 以降）
                //    v2 以前の送信側はこのブロックを持たない。読まずに抜ける。
                //    v3 は 1 グループ 1 コマンド 1 出力。ステップ 0 として読む。
                if (metaVersion >= 3)
                {
                    ushort groupCount = r.ReadUInt16();
                    for (int i = 0; i < groupCount; i++)
                    {
                        if (metaVersion >= 4)
                        {
                            var g = new Poly_Ling.Data.ObjectGroup(ReadString(r))
                            {
                                StashObjectId = r.ReadUInt64(),
                                AutoUpdate    = r.ReadBoolean(),
                                SourceDigest  = ReadString(r),
                            };
                            g.Steps.Clear();

                            if (metaVersion >= 5)
                            {
                                g.Goal            = ReadString(r);
                                g.Preconditions   = ReadStringList(r);
                                g.SuccessCriteria = ReadStringList(r);
                                g.Tags            = ReadStringList(r);
                                g.Provenance = new Poly_Ling.Data.ObjectGroupProvenance
                                {
                                    ParentName    = ReadString(r),
                                    ChangeSummary = ReadString(r),
                                    CreatedBy     = ReadString(r),
                                };
                            }

                            ushort stepCount = r.ReadUInt16();
                            for (int si = 0; si < stepCount; si++)
                            {
                                var st = new Poly_Ling.Data.ObjectGroupStep
                                {
                                    Action = ReadString(r),
                                };

                                if (metaVersion >= 5)
                                {
                                    st.ElementId = ReadString(r);

                                    byte kindByte = r.ReadByte();
                                    st.Kind = System.Enum.IsDefined(
                                                  typeof(Poly_Ling.Data.ObjectGroupStepKind), (int)kindByte)
                                        ? (Poly_Ling.Data.ObjectGroupStepKind)kindByte
                                        : Poly_Ling.Data.ObjectGroupStepKind.Command;

                                    st.Purpose = ReadString(r);

                                    if (metaVersion >= 6)
                                    {
                                        st.RefName = ReadString(r);

                                        byte policyByte = r.ReadByte();
                                        st.ExpansionPolicy = System.Enum.IsDefined(
                                                                 typeof(Poly_Ling.Data.ScenarioExpansionPolicy),
                                                                 (int)policyByte)
                                            ? (Poly_Ling.Data.ScenarioExpansionPolicy)policyByte
                                            : Poly_Ling.Data.ScenarioExpansionPolicy.Reference;
                                    }
                                }

                                ushort outCount = r.ReadUInt16();
                                for (int j = 0; j < outCount; j++)
                                {
                                    ulong id = r.ReadUInt64();
                                    if (id != 0UL) st.OutputObjectIds.Add(id);
                                }

                                ushort argCount = r.ReadUInt16();
                                for (int j = 0; j < argCount; j++)
                                {
                                    string k = ReadString(r);
                                    string v = ReadString(r);
                                    st.SetArg(k, v);
                                }

                                ushort refCount = r.ReadUInt16();
                                for (int j = 0; j < refCount; j++)
                                {
                                    string k = ReadString(r);
                                    ushort idCount = r.ReadUInt16();
                                    var ids = new List<ulong>(idCount);
                                    for (int m = 0; m < idCount; m++) ids.Add(r.ReadUInt64());
                                    st.SetMeshRefIds(k, ids);
                                }

                                g.Steps.Add(st);
                            }

                            if (g.Steps.Count == 0)
                                g.Steps.Add(new Poly_Ling.Data.ObjectGroupStep());

                            // Version 4 以前には ElementId が無い。
                            g.EnsureElementIds();
                            model.ObjectGroups.Add(g);
                            continue;
                        }

                        // version 3
                        var g3 = new Poly_Ling.Data.ObjectGroup(ReadString(r))
                        {
                            Action         = ReadString(r),
                            OutputObjectId = r.ReadUInt64(),
                            StashObjectId  = r.ReadUInt64(),
                            AutoUpdate     = r.ReadBoolean(),
                            SourceDigest   = ReadString(r),
                        };

                        ushort argCount3 = r.ReadUInt16();
                        for (int j = 0; j < argCount3; j++)
                        {
                            string k = ReadString(r);
                            string v = ReadString(r);
                            g3.SetArg(k, v);
                        }

                        ushort refCount3 = r.ReadUInt16();
                        for (int j = 0; j < refCount3; j++)
                        {
                            string k = ReadString(r);
                            ushort idCount = r.ReadUInt16();
                            var ids = new List<ulong>(idCount);
                            for (int m = 0; m < idCount; m++) ids.Add(r.ReadUInt64());
                            g3.SetMeshRefIds(k, ids);
                        }

                        g3.EnsureElementIds();
                        model.ObjectGroups.Add(g3);
                    }
                }

                // ── version 7：使う作業軸。v6 以前の送信元からは届かないので 0（未指定）のまま。
                if (metaVersion >= 7)
                    model.ActiveWorkAxisObjectId = r.ReadUInt64();

                // ── version 9：モデル単位の付帯
                if (metaVersion >= 9)
                    ReadModelExtras(r, model);

                return (modelIndex, model);
            }
        }

        // ================================================================
        // PLRS — MeshSummary
        // [4B] Magic  [1B] Version  [1B] Padding  [2B] ModelIndex  [2B] MeshIndex
        // --- メッシュメタデータ（ジオメトリなし） ---
        // [string] Name  [1B] Type  [1B] IsVisible  [1B] IsLocked
        // [2B] Depth  [2B] ParentIndex  [2B] HierarchyParentIndex(v2+)  [1B] MirrorType  [1B] MirrorAxis
        // [4B] MirrorDistance  [1B] ExcludeFromExport
        // [2B] BakedMirrorSourceIndex  [1B] HasBakedMirrorChild
        // [1B] IsMorph  (if) [string] MorphName  [4B] MorphPanel  [2B] MorphParentIndex
        //               (if) [4B] BasePositionCount  [12B × N] BasePositions
        //                    [1B] HasNormals  (if) [12B × N] BaseNormals
        //                    [1B] HasUVs      (if) [8B × N]  BaseUVs
        // [1B] HasBoneTransform  (if) Position[12B] Rotation[12B] Scale[12B]
        // [64B] WorldMatrix  [64B] BindPose  [16B] BoneModelRotation
        // [1B] IsIK  [2B] IKTargetIndex  [2B] IKLoopCount  [4B] IKLimitAngle
        // [4B] VertexCount  [4B] FaceCount
        // [1B] MorphMirrorPolicy(v4+)  [2B] MirrorOfMorphIndex(v4+)
        //   ※ v4 追加分は末尾に置く。既存レイアウトは動かさない。
        //     規約は MorphMirrorPolicy.cs を正典とする。
        // [1B] HasWorkAxis(v5+)  (if) Origin[12B] Rotation[16B] Length[4B] IsVisible[1B]
        //   ※ v5 で末尾に追加。値を持つのは作業軸オブジェクト（MeshType.WorkAxis）だけ。
        //     v4 以前の受信側は末尾のこの欄を読まずに終わる（読み終わりを検査しない）。
        // [1B] BoneTransform.UseLocalTransform (v6+)
        // [BonePose] [1B] Has (if) IsActive[1B] HasManual[1B] (if) dPos[12B] dRot[16B] Weight[4B] Enabled[1B]  (v6+)
        // [IKLink]   [1B] Has (if) HasLimit[1B] Min[12B] Max[12B]                                        (v6+)
        // [RigidBody][1B] Has (if) *.pmx_physics.csv の rigidBody 行と同じ項目                            (v6+)
        // [Joint]    [1B] Has (if) *.pmx_physics.csv の joint 行と同じ項目                                (v6+)
        //   ※ v6 で末尾に追加。v5 以前の受信側は読まずに終わる。
        // ================================================================

        public static byte[] SerializeMeshSummary(MeshContext mc, int modelIndex, int meshIndex)
        {
            if (mc == null) return null;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.MeshSummary);
                w.Write((byte)7);   // v2: HierarchyParentIndex / v3: ObjectId + EditorName / v4: モーフのミラー適用 / v5: 作業軸の値 / v6: ポーズ層・IK リンク・剛体・ジョイント / v7: メッシュ単位の付帯
                w.Write((byte)0); // padding
                w.Write((short)modelIndex);
                w.Write((short)meshIndex);

                WriteString(w, mc.Name);
                w.Write((byte)mc.Type);
                w.Write(mc.IsVisible);
                w.Write(mc.IsLocked);

                // v3: 協働編集（安定ID + 担当者名）
                w.Write(mc.ObjectId);
                WriteString(w, mc.EditorName ?? "");
                w.Write((short)mc.Depth);
                w.Write((short)mc.ParentIndex);
                w.Write((short)mc.HierarchyParentIndex);  // v2: ボーン等の階層親(MasterIndex)
                w.Write((byte)mc.MirrorType);
                w.Write((byte)mc.MirrorAxis);
                w.Write(mc.MirrorDistance);
                w.Write(mc.ExcludeFromExport);
                w.Write((short)mc.BakedMirrorSourceIndex);
                w.Write(mc.HasBakedMirrorChild);

                bool isMorph = mc.IsMorph;
                w.Write(isMorph);
                if (isMorph)
                {
                    WriteString(w, mc.MorphName);
                    w.Write(mc.MorphPanel);
                    w.Write((short)mc.MorphParentIndex);

                    var mbd = mc.MorphBaseData;
                    int bpCount = mbd?.BasePositions?.Length ?? 0;
                    w.Write(bpCount);
                    for (int i = 0; i < bpCount; i++)
                        WriteVector3(w, mbd.BasePositions[i]);

                    bool hasNormals = mbd?.HasNormals ?? false;
                    w.Write(hasNormals);
                    if (hasNormals)
                        for (int i = 0; i < mbd.BaseNormals.Length; i++)
                            WriteVector3(w, mbd.BaseNormals[i]);

                    bool hasUVs = mbd?.HasUVs ?? false;
                    w.Write(hasUVs);
                    if (hasUVs)
                        for (int i = 0; i < mbd.BaseUVs.Length; i++)
                        { w.Write(mbd.BaseUVs[i].x); w.Write(mbd.BaseUVs[i].y); }
                }

                bool hasBone = mc.BoneTransform != null;
                w.Write(hasBone);
                if (hasBone)
                {
                    WriteVector3(w, mc.BoneTransform.Position);
                    WriteVector3(w, mc.BoneTransform.Rotation);
                    WriteVector3(w, mc.BoneTransform.Scale);
                }

                WriteMatrix4x4(w, mc.WorldMatrix);
                WriteMatrix4x4(w, mc.BindPose);
                WriteQuaternion(w, mc.BoneModelRotation);

                w.Write(mc.IsIK);
                w.Write((short)mc.IKTargetIndex);
                w.Write((short)mc.IKLoopCount);
                w.Write(mc.IKLimitAngle);

                // ジオメトリなし、サマリ用カウントのみ
                w.Write((uint)(mc.MeshObject?.VertexCount ?? 0));
                w.Write((uint)(mc.MeshObject?.FaceCount ?? 0));

                // ---- v4: モーフのミラー適用（規約は MorphMirrorPolicy.cs を正典とする）----
                // 追加は必ずこのブロックの末尾に行うこと。
                w.Write((byte)mc.MorphMirrorPolicy);
                w.Write((short)mc.MirrorOfMorphIndex);

                // ---- v5: 作業軸の値（作業軸オブジェクトだけが持つ）----
                var wa = mc.WorkAxis;
                w.Write(wa != null);
                if (wa != null)
                {
                    WriteVector3(w, wa.Origin);
                    WriteQuaternion(w, wa.Rotation);
                    w.Write(wa.Length);
                    w.Write(wa.IsVisible);
                }

                // ---- v6: ボーンのポーズ層・IK リンク・剛体・ジョイント（*.bone.csv / *.pmx_physics.csv と揃える）----
                w.Write(mc.BoneTransform?.UseLocalTransform ?? false);
                WriteBonePose(w, mc.BonePoseData);

                var lk = mc.MeshObject?.IKLink;
                w.Write(lk != null);
                if (lk != null)
                {
                    w.Write(lk.HasLimit);
                    WriteVector3(w, lk.LimitMin);
                    WriteVector3(w, lk.LimitMax);
                }

                var rb = mc.MeshObject?.RigidBodyData;
                w.Write(rb != null);
                if (rb != null)
                {
                    WriteString(w, rb.NameEnglish ?? "");
                    WriteString(w, rb.RelatedBoneName ?? "");
                    w.Write(rb.BoneIndex);
                    w.Write(rb.Group);
                    w.Write(rb.CollisionMask);
                    w.Write((int)rb.Shape);
                    WriteVector3(w, rb.Size);
                    WriteVector3(w, rb.Position);
                    WriteVector3(w, rb.Rotation);
                    w.Write(rb.Mass);
                    w.Write(rb.LinearDamping);
                    w.Write(rb.AngularDamping);
                    w.Write(rb.Restitution);
                    w.Write(rb.Friction);
                    w.Write((int)rb.PhysicsMode);
                }

                var jd = mc.MeshObject?.JointData;
                w.Write(jd != null);
                if (jd != null)
                {
                    WriteString(w, jd.NameEnglish ?? "");
                    w.Write(jd.JointType);
                    WriteString(w, jd.BodyAName ?? "");
                    WriteString(w, jd.BodyBName ?? "");
                    w.Write(jd.RigidBodyIndexA);
                    w.Write(jd.RigidBodyIndexB);
                    WriteVector3(w, jd.Position);
                    WriteVector3(w, jd.Rotation);
                    WriteVector3(w, jd.TranslationMin);
                    WriteVector3(w, jd.TranslationMax);
                    WriteVector3(w, jd.RotationMin);
                    WriteVector3(w, jd.RotationMax);
                    WriteVector3(w, jd.SpringTranslation);
                    WriteVector3(w, jd.SpringRotation);
                }

                // ---- v7: メッシュ単位の付帯（CSV の mesh 行と揃える。RemoteProgressiveSerializer.Extras.cs）----
                WriteMeshExtras(w, mc);

                return ms.ToArray();
            }
        }

        /// <summary>ポーズ層。CSV の bonePose 行と同じく IsActive と Manual 層だけを運ぶ。</summary>
        private static void WriteBonePose(BinaryWriter w, BonePoseData bp)
        {
            w.Write(bp != null);
            if (bp == null) return;
            w.Write(bp.IsActive);
            var manual = bp.GetLayer("Manual");
            bool hasManual = manual != null && !manual.IsZero;
            w.Write(hasManual);
            if (!hasManual) return;
            WriteVector3(w, manual.DeltaPosition);
            WriteQuaternion(w, manual.DeltaRotation);
            w.Write(manual.Weight);
            w.Write(manual.Enabled);
        }

        private static BonePoseData ReadBonePose(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            var bp = new BonePoseData { IsActive = r.ReadBoolean() };
            if (r.ReadBoolean())
            {
                var layer = bp.GetOrCreateLayer("Manual");
                layer.DeltaPosition = ReadVector3(r);
                layer.DeltaRotation = ReadQuaternion(r);
                layer.Weight        = r.ReadSingle();
                layer.Enabled       = r.ReadBoolean();
            }
            return bp;
        }

        public static (int modelIndex, int meshIndex, MeshContext mc, int vertexCount, int faceCount)? DeserializeMeshSummary(byte[] data)
        {
            if (data == null || data.Length < 10) return null;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (r.ReadUInt32() != RemoteMagic.MeshSummary) return null;
                int summaryVersion = r.ReadByte(); // version
                r.ReadByte(); // padding
                int modelIndex = r.ReadInt16();
                int meshIndex = r.ReadInt16();

                var mc = new MeshContext();
                mc.MeshObject = new MeshObject();
                mc.Name = ReadString(r);
                mc.Type = (MeshType)r.ReadByte();
                mc.IsVisible = r.ReadBoolean();
                mc.IsLocked = r.ReadBoolean();
                if (summaryVersion >= 3)
                {
                    // v3: 協働編集（安定ID + 担当者名）
                    mc.ObjectId   = r.ReadUInt64();
                    mc.EditorName = ReadString(r);
                    Poly_Ling.Data.ObjectIdAllocator.Observe(mc.ObjectId);
                }
                mc.Depth = r.ReadInt16();
                mc.ParentIndex = r.ReadInt16();
                if (summaryVersion >= 2) mc.HierarchyParentIndex = r.ReadInt16();
                mc.MirrorType = r.ReadByte();
                mc.MirrorAxis = r.ReadByte();
                mc.MirrorDistance = r.ReadSingle();
                mc.ExcludeFromExport = r.ReadBoolean();
                mc.BakedMirrorSourceIndex = r.ReadInt16();
                mc.HasBakedMirrorChild = r.ReadBoolean();

                bool isMorph = r.ReadBoolean();
                if (isMorph)
                {
                    string morphName      = ReadString(r);
                    int    morphPanel     = r.ReadInt32();
                    short  morphParentIdx = r.ReadInt16();
                    mc.SetAsMorph(morphName);
                    mc.MorphPanel       = morphPanel;
                    mc.MorphParentIndex = morphParentIdx;

                    int bpCount = r.ReadInt32();
                    if (bpCount > 0)
                    {
                        var mbd = mc.MorphBaseData ?? new MorphBaseData(morphName);
                        // SetAsMorph は頂点の無い MeshObject では何もしない（サマリの時点では頂点が無い）。
                        // その場合 MorphPanel の代入も捨てられるので、ここで入れ直す。
                        mbd.Panel = morphPanel;
                        mbd.BasePositions = new Vector3[bpCount];
                        for (int i = 0; i < bpCount; i++)
                            mbd.BasePositions[i] = ReadVector3(r);

                        bool hasNormals = r.ReadBoolean();
                        if (hasNormals)
                        {
                            mbd.BaseNormals = new Vector3[bpCount];
                            for (int i = 0; i < bpCount; i++)
                                mbd.BaseNormals[i] = ReadVector3(r);
                        }

                        bool hasUVs = r.ReadBoolean();
                        if (hasUVs)
                        {
                            mbd.BaseUVs = new Vector2[bpCount];
                            for (int i = 0; i < bpCount; i++)
                                mbd.BaseUVs[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                        }

                        mc.MorphBaseData = mbd;
                    }
                    else
                    {
                        // BasePositions なし: フラグだけ読み捨て
                        r.ReadBoolean(); // hasNormals
                        r.ReadBoolean(); // hasUVs
                    }
                }

                bool hasBone = r.ReadBoolean();
                Vector3 btPos = Vector3.zero, btRot = Vector3.zero, btScale = Vector3.one;
                if (hasBone)
                {
                    btPos   = ReadVector3(r);
                    btRot   = ReadVector3(r);
                    btScale = ReadVector3(r);
                }

                mc.WorldMatrix       = ReadMatrix4x4(r);
                mc.BindPose          = ReadMatrix4x4(r);
                mc.BoneModelRotation = ReadQuaternion(r);

                mc.IsIK          = r.ReadBoolean();
                mc.IKTargetIndex = r.ReadInt16();
                mc.IKLoopCount   = r.ReadInt16();
                mc.IKLimitAngle  = r.ReadSingle();

                int vertexCount = (int)r.ReadUInt32();
                int faceCount   = (int)r.ReadUInt32();

                // ---- v4: モーフのミラー適用 ----
                // v3 以前の送信元からは届かないので、既定（FollowParent / -1）のままにする。
                if (summaryVersion >= 4)
                {
                    mc.MorphMirrorPolicy  = (MorphMirrorPolicy)r.ReadByte();
                    mc.MirrorOfMorphIndex = r.ReadInt16();
                }

                // ---- v5: 作業軸の値 ----
                // v4 以前の送信元からは届かないので、作業軸オブジェクトでも値は空のまま。
                if (summaryVersion >= 5 && r.ReadBoolean())
                {
                    var wa = new Poly_Ling.Context.WorkAxisContext();
                    wa.Origin    = ReadVector3(r);
                    wa.Rotation  = ReadQuaternion(r);
                    wa.Length    = r.ReadSingle();
                    wa.IsVisible = r.ReadBoolean();
                    mc.WorkAxis  = wa;
                }

                // ---- v6: ボーンのポーズ層・IK リンク・剛体・ジョイント ----
                // v5 以前の送信元からは届かないので空のまま。
                bool btUseLocal = true;   // v5 以前は常に true として受けていた
                if (summaryVersion >= 6)
                {
                    btUseLocal = r.ReadBoolean();
                    mc.BonePoseData = ReadBonePose(r);

                    if (r.ReadBoolean())
                    {
                        mc.MeshObject.IKLink = new IKLinkData
                        {
                            HasLimit = r.ReadBoolean(),
                            LimitMin = ReadVector3(r),
                            LimitMax = ReadVector3(r),
                        };
                    }

                    if (r.ReadBoolean())
                    {
                        mc.MeshObject.RigidBodyData = new RigidBodyData
                        {
                            NameEnglish     = ReadString(r),
                            RelatedBoneName = ReadString(r),
                            BoneIndex       = r.ReadInt32(),
                            Group           = r.ReadInt32(),
                            CollisionMask   = r.ReadUInt16(),
                            Shape           = (RigidBodyShape)r.ReadInt32(),
                            Size            = ReadVector3(r),
                            Position        = ReadVector3(r),
                            Rotation        = ReadVector3(r),
                            Mass            = r.ReadSingle(),
                            LinearDamping   = r.ReadSingle(),
                            AngularDamping  = r.ReadSingle(),
                            Restitution     = r.ReadSingle(),
                            Friction        = r.ReadSingle(),
                            PhysicsMode     = (RigidBodyPhysicsMode)r.ReadInt32(),
                        };
                    }

                    if (r.ReadBoolean())
                    {
                        mc.MeshObject.JointData = new JointData
                        {
                            NameEnglish       = ReadString(r),
                            JointType         = r.ReadInt32(),
                            BodyAName         = ReadString(r),
                            BodyBName         = ReadString(r),
                            RigidBodyIndexA   = r.ReadInt32(),
                            RigidBodyIndexB   = r.ReadInt32(),
                            Position          = ReadVector3(r),
                            Rotation          = ReadVector3(r),
                            TranslationMin    = ReadVector3(r),
                            TranslationMax    = ReadVector3(r),
                            RotationMin       = ReadVector3(r),
                            RotationMax       = ReadVector3(r),
                            SpringTranslation = ReadVector3(r),
                            SpringRotation    = ReadVector3(r),
                        };
                    }
                }

                // ---- v7: メッシュ単位の付帯 ----
                if (summaryVersion >= 7)
                    ReadMeshExtras(r, mc);

                // BoneTransform は MeshObject 設定後に適用
                // mc.MeshObject は先頭で生成済み（Name/Type書き込み済み）なのでそのまま使う
                if (hasBone)
                {
                    mc.BoneTransform = new Poly_Ling.Data.BoneTransform
                    {
                        Position        = btPos,
                        Rotation        = btRot,
                        Scale           = btScale,
                        UseLocalTransform = btUseLocal,
                    };
                }

                return (modelIndex, meshIndex, mc, vertexCount, faceCount);
            }
        }

        // ================================================================
        // PLRD — MeshData（ジオメトリ本体）
        // [Header 24B]
        //   Magic 4B, Version 1B, Padding 1B, ModelIndex 2B, MeshIndex 2B
        //   FieldFlags 4B, VertexCount 4B, FaceCount 4B, Reserved 2B
        // [Body] PLRM ボディと同一フォーマット
        // ================================================================

        public static byte[] SerializeMeshData(MeshContext mc, int modelIndex, int meshIndex, MeshFieldFlags flags)
        {
            if (mc?.MeshObject == null) return null;

            // 本文の書き方とヘッダのフラグを一致させる（BoneWeights は形式 2 で書かれる）。
            flags = RemoteBinarySerializer.NormalizeFlags(flags);

            // RemoteBinarySerializer でボディを含む電文を生成し、ヘッダ部分を差し替える。
            // ヘッダ長は電文の版で決まる（v1 = 20B、v2 = 28B。4 バイト目が版番号）。
            // 以前は 20B 固定で切っていたため、v2 の ObjectId 8B が本体の先頭に混ざり、
            // 受信側の読み出しが 8B ずれて末尾を越えていた（2026-09-29 修正）。
            byte[] plrm = RemoteBinarySerializer.Serialize(mc, flags);
            if (plrm == null || plrm.Length < 5) return null;
            int hdrLen = BinaryHeader.SizeOf(plrm[4]);
            if (plrm.Length < hdrLen) return null;

            int bodyLen = plrm.Length - hdrLen;
            using (var ms = new MemoryStream(24 + bodyLen))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.MeshData);                      // 0-3
                w.Write((byte)1);                                    // 4
                w.Write((byte)0);                                    // 5 padding
                w.Write((short)modelIndex);                          // 6-7
                w.Write((short)meshIndex);                           // 8-9
                w.Write((uint)flags);                                // 10-13
                w.Write((uint)(mc.MeshObject?.VertexCount ?? 0));    // 14-17
                w.Write((uint)(mc.MeshObject?.FaceCount ?? 0));      // 18-21
                w.Write((ushort)0);                                  // 22-23 reserved
                w.Write(plrm, hdrLen, bodyLen);                      // 24..
                return ms.ToArray();
            }
        }

        public static (int modelIndex, int meshIndex, MeshObject mesh)? DeserializeMeshData(byte[] data)
        {
            if (data == null || data.Length < 24) return null;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (r.ReadUInt32() != RemoteMagic.MeshData) return null;
                r.ReadByte(); // version
                r.ReadByte(); // padding
                int modelIndex  = r.ReadInt16();
                int meshIndex   = r.ReadInt16();
                uint fieldFlags = r.ReadUInt32();
                uint vertexCount= r.ReadUInt32();
                uint faceCount  = r.ReadUInt32();
                r.ReadUInt16(); // reserved

                int bodyLen = data.Length - 24;

                // PLRM ヘッダ(20B) + ボディ を再構築して RemoteBinarySerializer に渡す
                byte[] plrm;
                using (var plrmMs = new MemoryStream(20 + bodyLen))
                using (var w = new BinaryWriter(plrmMs))
                {
                    w.Write(RemoteMagic.Mesh);                      // PLRM magic
                    w.Write((byte)1);                                // version
                    w.Write((byte)BinaryMessageType.MeshData);      // message type
                    w.Write(fieldFlags);
                    w.Write(vertexCount);
                    w.Write(faceCount);
                    w.Write((ushort)0);                              // reserved
                    w.Write(data, 24, bodyLen);
                    plrm = plrmMs.ToArray();
                }

                var mesh = RemoteBinarySerializer.Deserialize(plrm);
                return (modelIndex, meshIndex, mesh);
            }
        }

        // ================================================================
        // マテリアルシリアライズ（内部共用）
        // ================================================================

        private static void WriteMaterialData(BinaryWriter w, MaterialData d, string assetPath)
        {
            WriteString(w, d.Name ?? string.Empty);
            w.Write((byte)d.ShaderType);
            w.Write(d.BaseColor.Length >= 4 ? d.BaseColor[0] : 1f);
            w.Write(d.BaseColor.Length >= 4 ? d.BaseColor[1] : 1f);
            w.Write(d.BaseColor.Length >= 4 ? d.BaseColor[2] : 1f);
            w.Write(d.BaseColor.Length >= 4 ? d.BaseColor[3] : 1f);
            w.Write((byte)d.Surface);
            w.Write((byte)d.CullMode);
            w.Write(d.AlphaClipEnabled);
            w.Write(d.AlphaCutoff);
            w.Write(d.EmissionEnabled);
            w.Write(d.EmissionColor.Length >= 4 ? d.EmissionColor[0] : 0f);
            w.Write(d.EmissionColor.Length >= 4 ? d.EmissionColor[1] : 0f);
            w.Write(d.EmissionColor.Length >= 4 ? d.EmissionColor[2] : 0f);
            w.Write(d.EmissionColor.Length >= 4 ? d.EmissionColor[3] : 1f);

            byte[] pngData = null;
            if (!string.IsNullOrEmpty(d.BaseMapPath))
            {
                var tex = PLEditorBridge.I.LoadAssetAtPath<Texture2D>(d.BaseMapPath);
                if (tex != null) pngData = EncodeTextureAsPNG(tex);
            }
            if (pngData == null && !string.IsNullOrEmpty(d.SourceTexturePath) && File.Exists(d.SourceTexturePath))
                pngData = File.ReadAllBytes(d.SourceTexturePath);

            if (pngData != null) { w.Write((uint)pngData.Length); w.Write(pngData); }
            else                 { w.Write((uint)0); }

            // ---- version 2 拡張ブロック ----
            // ここより後は version >= 2 の受信側だけが読む。追加は必ずこのブロックの末尾に行うこと。
            // ※ シェーダー固有プロパティのテクスチャはパスのみ送る（PNG実体は BaseMap のみ送信）。
            WriteString(w, d.ShaderName ?? string.Empty);
            WriteST(w, d.BaseMapST);
            WriteST(w, d.NormalMapST);
            WriteST(w, d.EmissionMapST);
            w.Write(d.RenderQueueOffset);
            w.Write(d.ZWriteOverride);
            w.Write(d.ZTest);
            w.Write(d.DoubleSidedGI);
            w.Write(d.EnableGPUInstancing);

            var props = d.ShaderProperties;
            int propCount = props?.Count ?? 0;
            w.Write((ushort)propCount);
            for (int i = 0; i < propCount; i++)
            {
                var p = props[i] ?? new MaterialProperty();
                WriteString(w, p.Name ?? string.Empty);
                w.Write((byte)p.Kind);
                w.Write(p.X);
                w.Write(p.Y);
                w.Write(p.Z);
                w.Write(p.W);
                WriteString(w, p.TexturePath ?? string.Empty);
            }

            // ---- ModelMeta version 8 拡張ブロック（materials.csv と揃える）----
            // 追加は必ずこのブロックの末尾に行うこと。
            WriteString(w, assetPath ?? string.Empty);
            w.Write(d.Metallic);
            w.Write(d.Smoothness);
            w.Write(d.NormalScale);
            w.Write(d.OcclusionStrength);
            w.Write((byte)d.BlendMode);
            WriteString(w, d.BaseMapPath ?? string.Empty);
            WriteString(w, d.MetallicMapPath ?? string.Empty);
            WriteString(w, d.NormalMapPath ?? string.Empty);
            WriteString(w, d.OcclusionMapPath ?? string.Empty);
            WriteString(w, d.EmissionMapPath ?? string.Empty);
            WriteString(w, d.SourceTexturePath ?? string.Empty);
            WriteString(w, d.SourceAlphaMapPath ?? string.Empty);
            WriteString(w, d.SourceBumpMapPath ?? string.Empty);
        }

        private static void WriteST(BinaryWriter w, float[] st)
        {
            if (st != null && st.Length >= 4)
            {
                w.Write(st[0]); w.Write(st[1]); w.Write(st[2]); w.Write(st[3]);
            }
            else
            {
                w.Write(1f); w.Write(1f); w.Write(0f); w.Write(0f);
            }
        }

        private static float[] ReadST(BinaryReader r)
        {
            return new float[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };
        }

        private static (MaterialData data, Texture2D tex, string assetPath) ReadMaterialData(BinaryReader r, int version)
        {
            var d = new MaterialData();
            d.Name       = ReadString(r);
            d.ShaderType = (ShaderType)r.ReadByte();
            d.BaseColor  = new float[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };
            d.Surface    = (SurfaceType)r.ReadByte();
            d.CullMode   = (CullModeType)r.ReadByte();
            d.AlphaClipEnabled = r.ReadBoolean();
            d.AlphaCutoff      = r.ReadSingle();
            d.EmissionEnabled  = r.ReadBoolean();
            d.EmissionColor    = new float[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };

            uint texLen = r.ReadUInt32();
            Texture2D tex = null;
            if (texLen > 0)
            {
                byte[] pngData = r.ReadBytes((int)texLen);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(pngData);
                tex.name = d.Name;
            }

            // ---- version 2 拡張ブロック ----
            if (version >= 2)
            {
                d.ShaderName = ReadString(r);
                d.BaseMapST = ReadST(r);
                d.NormalMapST = ReadST(r);
                d.EmissionMapST = ReadST(r);
                d.RenderQueueOffset = r.ReadInt32();
                d.ZWriteOverride = r.ReadInt32();
                d.ZTest = r.ReadInt32();
                d.DoubleSidedGI = r.ReadBoolean();
                d.EnableGPUInstancing = r.ReadBoolean();

                ushort propCount = r.ReadUInt16();
                if (propCount > 0)
                {
                    var props = new List<MaterialProperty>(propCount);
                    for (int i = 0; i < propCount; i++)
                    {
                        var p = new MaterialProperty
                        {
                            Name = ReadString(r),
                            Kind = (MaterialPropertyKind)r.ReadByte(),
                            X = r.ReadSingle(),
                            Y = r.ReadSingle(),
                            Z = r.ReadSingle(),
                            W = r.ReadSingle()
                        };
                        string texPath = ReadString(r);
                        p.TexturePath = string.IsNullOrEmpty(texPath) ? null : texPath;
                        if (!string.IsNullOrEmpty(p.Name)) props.Add(p);
                    }
                    d.ShaderProperties = props.Count > 0 ? props : null;
                }
                else
                {
                    d.ShaderProperties = null;
                }
            }

            // ---- ModelMeta version 8 拡張ブロック ----
            string assetPath = null;
            if (version >= 8)
            {
                assetPath             = NullIfEmpty(ReadString(r));
                d.Metallic            = r.ReadSingle();
                d.Smoothness          = r.ReadSingle();
                d.NormalScale         = r.ReadSingle();
                d.OcclusionStrength   = r.ReadSingle();
                d.BlendMode           = (BlendModeType)r.ReadByte();
                d.BaseMapPath         = NullIfEmpty(ReadString(r));
                d.MetallicMapPath     = NullIfEmpty(ReadString(r));
                d.NormalMapPath       = NullIfEmpty(ReadString(r));
                d.OcclusionMapPath    = NullIfEmpty(ReadString(r));
                d.EmissionMapPath     = NullIfEmpty(ReadString(r));
                d.SourceTexturePath   = NullIfEmpty(ReadString(r));
                d.SourceAlphaMapPath  = NullIfEmpty(ReadString(r));
                d.SourceBumpMapPath   = NullIfEmpty(ReadString(r));
            }

            return (d, tex, assetPath);
        }

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        private static byte[] EncodeTextureAsPNG(Texture2D src)
        {
            if (src == null) return null;
            if (src.isReadable)
            {
                var d = src.EncodeToPNG();
                return (d != null && d.Length > 0) ? d : null;
            }
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var result = readable.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(readable);
            return (result != null && result.Length > 0) ? result : null;
        }

        // ================================================================
        // プリミティブ読み書きヘルパー
        // ================================================================

        private static void WriteString(BinaryWriter w, string s)
        {
            if (s == null) s = "";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s);
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }

        private static string ReadString(BinaryReader r)
        {
            ushort len = r.ReadUInt16();
            if (len == 0) return "";
            return System.Text.Encoding.UTF8.GetString(r.ReadBytes(len));
        }

        /// <summary>件数付きの文字列列を書く。null は 0 件。</summary>
        private static void WriteStringList(BinaryWriter w, List<string> list)
        {
            int n = list?.Count ?? 0;
            w.Write((ushort)n);
            for (int i = 0; i < n; i++) WriteString(w, list[i] ?? "");
        }

        /// <summary>件数付きの文字列列を読む。</summary>
        private static List<string> ReadStringList(BinaryReader r)
        {
            ushort n = r.ReadUInt16();
            var list = new List<string>(n);
            for (int i = 0; i < n; i++) list.Add(ReadString(r));
            return list;
        }

        private static void WriteVector3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static Vector3 ReadVector3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static void WriteQuaternion(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        private static Quaternion ReadQuaternion(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static void WriteMatrix4x4(BinaryWriter w, Matrix4x4 m) { for (int i = 0; i < 16; i++) w.Write(m[i]); }
        private static Matrix4x4 ReadMatrix4x4(BinaryReader r) { var m = new Matrix4x4(); for (int i = 0; i < 16; i++) m[i] = r.ReadSingle(); return m; }

        // ================================================================
        // PLRB — バッチフレーム（複数フレームを 1 本に束ねる）
        // [4B Magic=PLRB][1B Version][3B padding][4B FrameCount]{ [4B Len][Data] }×N
        // 読みは RemoteProjectReceiver.ProcessBatch。1 フレームなら束ねずそのまま返す。
        // ================================================================

        public static byte[] BuildBatch(List<byte[]> frames)
        {
            if (frames == null || frames.Count == 0)
            {
                using (var ms = new MemoryStream(12))
                using (var w  = new BinaryWriter(ms))
                {
                    w.Write(RemoteMagic.Batch);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((uint)0);
                    return ms.ToArray();
                }
            }
            if (frames.Count == 1) return frames[0];

            int totalBody = 0;
            foreach (var f in frames) totalBody += 4 + f.Length;

            using (var ms = new MemoryStream(12 + totalBody))
            using (var w  = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.Batch);
                w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                w.Write((uint)frames.Count);
                foreach (var f in frames) { w.Write((uint)f.Length); w.Write(f); }
                return ms.ToArray();
            }
        }

        // ================================================================
        // プロジェクト全体
        // PLRH → (PLRM → PLRS×メッシュ数)×モデル数 → PLRD×頂点を持つメッシュ数 を PLRB で束ねる。
        // リモートで送るフレームと同じもの。ファイル保存（saveProjectBinary）はこれを書くだけ。
        // ================================================================

        public static byte[] SerializeWholeProject(ProjectContext project)
        {
            if (project == null) return null;

            var frames = new List<byte[]>();
            var header = SerializeProjectHeader(project);
            if (header == null) return null;
            frames.Add(header);

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                var mm = SerializeModelMeta(model, mi);
                if (mm != null) frames.Add(mm);
                for (int si = 0; si < model.Count; si++)
                {
                    var ms = SerializeMeshSummary(model.MeshContextList[si], mi, si);
                    if (ms != null) frames.Add(ms);
                }
            }

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                for (int si = 0; si < model.Count; si++)
                {
                    var mc = model.MeshContextList[si];
                    if (mc?.MeshObject == null || mc.MeshObject.VertexCount == 0) continue;
                    var md = SerializeMeshData(mc, mi, si, MeshFieldFlags.Complete);
                    if (md != null) frames.Add(md);
                }
            }

            return BuildBatch(frames);
        }

        /// <summary>SerializeWholeProject の逆。失敗時は null。</summary>
        public static ProjectContext DeserializeWholeProject(byte[] data)
        {
            if (data == null || data.Length < 4) return null;
            var receiver = new RemoteProjectReceiver();
            receiver.ProcessBatch(data);
            return receiver.Project;
        }
    }
}
