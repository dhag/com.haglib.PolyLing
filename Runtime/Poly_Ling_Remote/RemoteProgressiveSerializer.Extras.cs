// Remote/RemoteProgressiveSerializer.Extras.cs
// プログレッシブ転送の付帯（CSV と揃えるための欄）。
//
//   PLRH v2 … 作業軸辞書（workaxis_library.csv）
//   PLRM v9 … モデル単位の付帯（materials.csv の既定材質、vrmexpressions.csv、meshselsets.csv、
//              datastore.csv、mirrorpairs.csv、tposebackup.csv、springbonegroups.csv、
//              previewsettings.csv、vrmmeta.csv、vrmlookat.csv、avatarsettings.csv、
//              coordinate.csv、underlay.csv / underlay_plates.csv）
//   PLRS v7 … メッシュ単位の付帯（mesh 行の折り畳み・ミラーの細目・三角化・法線保持・
//              スキン種別・Humanoid・SpringBone・VRM 一人称・VRM 制約・選択セット）
//
// 【決まり】CSV に項目を足したらここにも足し、版番号を上げること（PolyLing_追加作業の必読.md の E）。
//   editorstate.csv / workplane.csv はモデルではなくエディタ状態で、Player の CSV 保存も書かない。

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Selection;
using Poly_Ling.Serialization;
using Poly_Ling.Symmetry;
using Poly_Ling.Materials;

namespace Poly_Ling.Remote
{
    public static partial class RemoteProgressiveSerializer
    {
        // ================================================================
        // PLRH v2：作業軸辞書
        // ================================================================

        private static void WriteWorkAxisLibrary(BinaryWriter w, WorkAxisLibrary lib)
        {
            int n = lib?.Count ?? 0;
            w.Write(n);
            if (lib == null) return;
            foreach (var name in lib.Names)
            {
                lib.TryGet(name, out var e);
                WriteString(w, name);
                WriteVector3(w, e.Origin);
                WriteQuaternion(w, e.Rotation);
                w.Write(e.Length);
            }
        }

        private static void ReadWorkAxisLibrary(BinaryReader r, WorkAxisLibrary lib)
        {
            lib.Clear();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                string name = ReadString(r);
                var e = new WorkAxisEntry
                {
                    Origin   = ReadVector3(r),
                    Rotation = ReadQuaternion(r),
                    Length   = r.ReadSingle(),
                };
                lib.Set(name, e);
            }
        }

        // ================================================================
        // PLRM v9：モデル単位の付帯
        // ================================================================

        /// <summary>
        /// 受信したミラー対。メッシュ（PLRS）が揃う前に届くので、索引だけ控えておき
        /// FinishReceivedModel で組む（CSV の ReadMirrorPairsCsv と同じく Build まで行う）。
        /// </summary>
        private static readonly ConditionalWeakTable<ModelContext, List<(int real, int mirror, int axis)>>
            _pendingMirrorPairs = new ConditionalWeakTable<ModelContext, List<(int, int, int)>>();

        private static void WriteModelExtras(BinaryWriter w, ModelContext model)
        {
            // materials.csv：現在の材質・既定材質
            w.Write(model.CurrentMaterialIndex);
            w.Write(model.DefaultCurrentMaterialIndex);
            w.Write(model.AutoSetDefaultMaterials);
            var defs = model.DefaultMaterialReferences;
            int defCount = defs?.Count ?? 0;
            w.Write(defCount);
            for (int i = 0; i < defCount; i++)
                WriteMaterialData(w, defs[i]?.Data ?? new MaterialData(), defs[i]?.AssetPath);

            // vrmexpressions.csv：表情ごとの VRM 付帯（MorphExpressions と同じ並び）
            var exprs = model.MorphExpressions;
            int exprCount = exprs?.Count ?? 0;
            w.Write(exprCount);
            for (int i = 0; i < exprCount; i++)
            {
                var v = exprs[i]?.Vrm;
                w.Write(v != null);
                if (v == null) continue;
                w.Write(v.IsBinary);
                w.Write((int)v.OverrideBlink);
                w.Write((int)v.OverrideLookAt);
                w.Write((int)v.OverrideMouth);

                int cc = 0;
                if (v.MaterialColorBinds != null) foreach (var b in v.MaterialColorBinds) if (b != null) cc++;
                w.Write(cc);
                if (v.MaterialColorBinds != null)
                    foreach (var b in v.MaterialColorBinds)
                    {
                        if (b == null) continue;
                        WriteString(w, b.MaterialName ?? "");
                        w.Write((int)b.BindType);
                        RemoteBinaryIO.WriteVector4(w, b.TargetValue);
                    }

                int tc = 0;
                if (v.TextureTransformBinds != null) foreach (var b in v.TextureTransformBinds) if (b != null) tc++;
                w.Write(tc);
                if (v.TextureTransformBinds != null)
                    foreach (var b in v.TextureTransformBinds)
                    {
                        if (b == null) continue;
                        WriteString(w, b.MaterialName ?? "");
                        RemoteBinaryIO.WriteVector2(w, b.Scaling);
                        RemoteBinaryIO.WriteVector2(w, b.Offset);
                    }
            }

            // meshselsets.csv
            var mss = model.MeshSelectionSets;
            int msCount = mss?.Count ?? 0;
            w.Write(msCount);
            for (int i = 0; i < msCount; i++)
            {
                var s = mss[i];
                WriteString(w, s?.Name ?? "");
                WriteString(w, (s?.Category ?? default(ModelContext.SelectionCategory)).ToString());
                int nn = s?.MeshNames?.Count ?? 0;
                w.Write(nn);
                for (int k = 0; k < nn; k++) WriteString(w, s.MeshNames[k] ?? "");
            }

            // datastore.csv
            WriteDataStore(w, model.DataStore);

            // mirrorpairs.csv（索引で運ぶ。CSV の索引ベースと同じ）
            var pairs = model.MirrorPairs;
            var pairRows = new List<(int, int, int)>();
            if (pairs != null)
                foreach (var p in pairs)
                {
                    if (p == null) continue;
                    int ri = model.MeshContextList.IndexOf(p.Real);
                    int mi = model.MeshContextList.IndexOf(p.Mirror);
                    if (ri < 0 || mi < 0) continue;
                    pairRows.Add((ri, mi, (int)p.Axis));
                }
            w.Write(pairRows.Count);
            foreach (var (ri, mi, ax) in pairRows) { w.Write(ri); w.Write(mi); w.Write(ax); }

            // tposebackup.csv
            var tb = model.TPoseBackup;
            w.Write(tb != null);
            if (tb != null)
            {
                w.Write(tb.BoneRotations.Count);
                foreach (var kv in tb.BoneRotations) { w.Write(kv.Key); WriteVector3(w, kv.Value); }
                w.Write(tb.WorldMatrices.Count);
                foreach (var kv in tb.WorldMatrices) { w.Write(kv.Key); WriteMatrix4x4(w, kv.Value); }
                w.Write(tb.BindPoses.Count);
                foreach (var kv in tb.BindPoses) { w.Write(kv.Key); WriteMatrix4x4(w, kv.Value); }
                w.Write(tb.VertexPositions.Count);
                foreach (var kv in tb.VertexPositions)
                {
                    w.Write(kv.Key);
                    var arr = kv.Value;
                    int n = arr?.Length ?? 0;
                    w.Write(n);
                    for (int k = 0; k < n; k++) WriteVector3(w, arr[k]);
                }
            }

            // springbonegroups.csv
            var sbg = model.SpringBoneColliderGroupNames;
            int sbgCount = sbg?.Count ?? 0;
            w.Write(sbgCount);
            for (int i = 0; i < sbgCount; i++) WriteString(w, sbg[i] ?? "");

            // previewsettings.csv
            w.Write(model.SpringBoneFixedDeltaTime);
            w.Write(model.SpringBoneWarmupFrames);

            // vrmmeta.csv
            var m = model.VrmMeta;
            w.Write(m != null);
            if (m != null)
            {
                WriteString(w, m.Name ?? "");
                WriteString(w, m.Version ?? "");
                WriteStringListInt(w, m.Authors);
                WriteString(w, m.CopyrightInformation ?? "");
                WriteString(w, m.ContactInformation ?? "");
                WriteStringListInt(w, m.References);
                RemoteBinaryIO.WriteLongString(w, m.ThirdPartyLicenses ?? "");
                WriteString(w, m.ThumbnailPath ?? "");
                w.Write((int)m.AvatarPermission);
                w.Write(m.ViolentUsage);
                w.Write(m.SexualUsage);
                w.Write((int)m.CommercialUsage);
                w.Write(m.PoliticalOrReligiousUsage);
                w.Write(m.AntisocialOrHateUsage);
                w.Write((int)m.CreditNotation);
                w.Write(m.Redistribution);
                w.Write((int)m.Modification);
                WriteString(w, m.OtherLicenseUrl ?? "");
            }

            // vrmlookat.csv
            var l = model.VrmLookAt;
            w.Write(l != null);
            if (l != null)
            {
                WriteVector3(w, l.OffsetFromHead);
                w.Write((int)l.LookAtType);
                WriteRangeMap(w, l.HorizontalInner);
                WriteRangeMap(w, l.HorizontalOuter);
                WriteRangeMap(w, l.VerticalDown);
                WriteRangeMap(w, l.VerticalUp);
            }

            // avatarsettings.csv
            var a = model.AvatarRetarget;
            w.Write(a != null);
            if (a != null)
            {
                w.Write(a.UpperArmTwist); w.Write(a.LowerArmTwist);
                w.Write(a.UpperLegTwist); w.Write(a.LowerLegTwist);
                w.Write(a.ArmStretch);    w.Write(a.LegStretch);
                w.Write(a.FeetSpacing);   w.Write(a.HasTranslationDoF);
            }

            // coordinate.csv
            var c = model.CoordinateConvention;
            w.Write(c != null);
            if (c != null)
            {
                w.Write(c.PmxUnityRatio); w.Write(c.PmxFlipX); w.Write(c.PmxFlipZ);
                w.Write(c.MqoUnityRatio); w.Write(c.MqoFlipX); w.Write(c.MqoFlipZ);
            }

            // underlay.csv / underlay_plates.csv
            WriteUnderlay(w, model.Underlay);
        }

        private static void ReadModelExtras(BinaryReader r, ModelContext model)
        {
            model.CurrentMaterialIndex        = r.ReadInt32();
            model.DefaultCurrentMaterialIndex = r.ReadInt32();
            model.AutoSetDefaultMaterials     = r.ReadBoolean();
            int defCount = r.ReadInt32();
            var defs = new List<MaterialReference>(defCount);
            for (int i = 0; i < defCount; i++)
            {
                // 既定材質は描画に使わないので、テクスチャの実体は捨てる。
                var (d, tex, assetPath) = ReadMaterialData(r, 9);
                if (tex != null) UnityEngine.Object.Destroy(tex);
                var mref = new MaterialReference(d);
                if (!string.IsNullOrEmpty(assetPath)) mref.AssetPath = assetPath;
                defs.Add(mref);
            }
            model.DefaultMaterialReferences = defs;

            int exprCount = r.ReadInt32();
            for (int i = 0; i < exprCount; i++)
            {
                if (!r.ReadBoolean()) continue;
                var v = new VrmExpressionData
                {
                    IsBinary       = r.ReadBoolean(),
                    OverrideBlink  = (VrmExpressionOverride)r.ReadInt32(),
                    OverrideLookAt = (VrmExpressionOverride)r.ReadInt32(),
                    OverrideMouth  = (VrmExpressionOverride)r.ReadInt32(),
                };
                int cc = r.ReadInt32();
                for (int k = 0; k < cc; k++)
                    v.MaterialColorBinds.Add(new VrmMaterialColorBind
                    {
                        MaterialName = ReadString(r),
                        BindType     = (VrmMaterialColorType)r.ReadInt32(),
                        TargetValue  = RemoteBinaryIO.ReadVector4(r),
                    });
                int tc = r.ReadInt32();
                for (int k = 0; k < tc; k++)
                    v.TextureTransformBinds.Add(new VrmTextureTransformBind
                    {
                        MaterialName = ReadString(r),
                        Scaling      = RemoteBinaryIO.ReadVector2(r),
                        Offset       = RemoteBinaryIO.ReadVector2(r),
                    });
                if (i < model.MorphExpressions.Count && model.MorphExpressions[i] != null)
                    model.MorphExpressions[i].Vrm = v;
            }

            int msCount = r.ReadInt32();
            var mss = new List<MeshSelectionSet>(msCount);
            for (int i = 0; i < msCount; i++)
            {
                var s = new MeshSelectionSet(ReadString(r));
                if (Enum.TryParse<ModelContext.SelectionCategory>(ReadString(r), out var cat))
                    s.Category = cat;
                int nn = r.ReadInt32();
                for (int k = 0; k < nn; k++)
                {
                    string meshName = ReadString(r);
                    if (!string.IsNullOrEmpty(meshName)) s.MeshNames.Add(meshName);
                }
                mss.Add(s);
            }
            model.MeshSelectionSets = mss;

            ReadDataStore(r, model.DataStore);

            int pairCount = r.ReadInt32();
            var pending = new List<(int, int, int)>(pairCount);
            for (int i = 0; i < pairCount; i++)
                pending.Add((r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
            _pendingMirrorPairs.Remove(model);
            if (pending.Count > 0) _pendingMirrorPairs.Add(model, pending);

            if (r.ReadBoolean())
            {
                var tb = new TPoseBackup();
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++) { int k = r.ReadInt32(); tb.BoneRotations[k] = ReadVector3(r); }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++) { int k = r.ReadInt32(); tb.WorldMatrices[k] = ReadMatrix4x4(r); }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++) { int k = r.ReadInt32(); tb.BindPoses[k] = ReadMatrix4x4(r); }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    int k = r.ReadInt32();
                    int len = r.ReadInt32();
                    var arr = new Vector3[len];
                    for (int j = 0; j < len; j++) arr[j] = ReadVector3(r);
                    tb.VertexPositions[k] = arr;
                }
                model.TPoseBackup = tb;
            }

            int sbgCount = r.ReadInt32();
            var sbg = new List<string>(sbgCount);
            for (int i = 0; i < sbgCount; i++) sbg.Add(ReadString(r));
            model.SpringBoneColliderGroupNames = sbg;

            model.SpringBoneFixedDeltaTime = r.ReadSingle();
            model.SpringBoneWarmupFrames   = r.ReadInt32();

            if (r.ReadBoolean())
            {
                var m = new VrmMetaData();
                m.Name                      = ReadString(r);
                m.Version                   = ReadString(r);
                m.Authors                   = ReadStringListInt(r);
                m.CopyrightInformation      = ReadString(r);
                m.ContactInformation        = ReadString(r);
                m.References                = ReadStringListInt(r);
                m.ThirdPartyLicenses        = RemoteBinaryIO.ReadLongString(r);
                m.ThumbnailPath             = ReadString(r);
                m.AvatarPermission          = ModelSerializer.ToVrmAvatarPermission(r.ReadInt32());
                m.ViolentUsage              = r.ReadBoolean();
                m.SexualUsage               = r.ReadBoolean();
                m.CommercialUsage           = ModelSerializer.ToVrmCommercialUsage(r.ReadInt32());
                m.PoliticalOrReligiousUsage = r.ReadBoolean();
                m.AntisocialOrHateUsage     = r.ReadBoolean();
                m.CreditNotation            = ModelSerializer.ToVrmCreditNotation(r.ReadInt32());
                m.Redistribution            = r.ReadBoolean();
                m.Modification              = ModelSerializer.ToVrmModification(r.ReadInt32());
                m.OtherLicenseUrl           = ReadString(r);
                model.VrmMeta = m;
            }

            if (r.ReadBoolean())
            {
                var l = new VrmLookAtData();
                l.OffsetFromHead  = ReadVector3(r);
                l.LookAtType      = (r.ReadInt32() == 1) ? VrmLookAtType.Expression : VrmLookAtType.Bone;
                l.HorizontalInner = ReadRangeMap(r);
                l.HorizontalOuter = ReadRangeMap(r);
                l.VerticalDown    = ReadRangeMap(r);
                l.VerticalUp      = ReadRangeMap(r);
                model.VrmLookAt = l;
            }

            if (r.ReadBoolean())
            {
                model.AvatarRetarget = new AvatarRetargetData
                {
                    UpperArmTwist = r.ReadSingle(), LowerArmTwist = r.ReadSingle(),
                    UpperLegTwist = r.ReadSingle(), LowerLegTwist = r.ReadSingle(),
                    ArmStretch    = r.ReadSingle(), LegStretch    = r.ReadSingle(),
                    FeetSpacing   = r.ReadSingle(), HasTranslationDoF = r.ReadBoolean(),
                };
            }

            if (r.ReadBoolean())
            {
                model.CoordinateConvention = new CoordinateConventionData
                {
                    PmxUnityRatio = r.ReadSingle(), PmxFlipX = r.ReadBoolean(), PmxFlipZ = r.ReadBoolean(),
                    MqoUnityRatio = r.ReadSingle(), MqoFlipX = r.ReadBoolean(), MqoFlipZ = r.ReadBoolean(),
                };
            }

            model.Underlay = ReadUnderlay(r);
        }

        private static void WriteStringListInt(BinaryWriter w, List<string> list)
        {
            int n = 0;
            if (list != null) foreach (var s in list) if (!string.IsNullOrEmpty(s)) n++;
            w.Write(n);
            if (list == null) return;
            foreach (var s in list) if (!string.IsNullOrEmpty(s)) WriteString(w, s);
        }

        private static List<string> ReadStringListInt(BinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<string>(n);
            for (int i = 0; i < n; i++) list.Add(ReadString(r));
            return list;
        }

        private static void WriteRangeMap(BinaryWriter w, VrmLookAtRangeMap m)
        {
            var src = m ?? new VrmLookAtRangeMap();
            w.Write(src.InputMaxDegrees);
            w.Write(src.OutputScale);
        }

        private static VrmLookAtRangeMap ReadRangeMap(BinaryReader r)
        {
            float inMax = r.ReadSingle();
            float outScale = r.ReadSingle();
            return new VrmLookAtRangeMap(inMax, outScale);
        }

        // ---- datastore.csv ----

        private static void WriteDataStore(BinaryWriter w, PLDataStore store)
        {
            var list = new List<PLDataEntry>();
            if (store != null)
                foreach (var e in store.Entries)
                    if (e != null && !string.IsNullOrEmpty(e.Name)) list.Add(e);

            w.Write(list.Count);
            foreach (var e in list)
            {
                WriteString(w, e.Name);
                w.Write((int)e.Kind);
                WriteString(w, e.Source ?? "");
                w.Write(e.MasterIndex);
                w.Write(e.ObjectId);
                w.Write(e.CreatedAt.ToBinary());

                switch (e.Kind)
                {
                    case PLDataKind.IndexSet:
                        w.Write(e.IndexSet != null);
                        if (e.IndexSet != null) RemoteBinaryIO.WritePartsSet(w, e.IndexSet);
                        break;

                    case PLDataKind.LoopSet:
                    {
                        int n = 0;
                        if (e.Loops != null) foreach (var lp in e.Loops) if (lp != null) n++;
                        w.Write(n);
                        if (e.Loops != null)
                            foreach (var lp in e.Loops)
                            {
                                if (lp == null) continue;
                                WriteVector3(w, lp.Centroid);
                                RemoteBinaryIO.WriteIntList(w, lp.Vertices);
                            }
                        break;
                    }

                    case PLDataKind.ValueSet:
                    {
                        int n = e.Values?.Count ?? 0;
                        w.Write(n);
                        for (int i = 0; i < n; i++)
                        {
                            var v = e.Values[i];
                            WriteString(w, v.Key ?? "");
                            w.Write(v.IsText);
                            w.Write(v.IsText ? 0.0 : v.Number);
                            RemoteBinaryIO.WriteLongString(w, v.IsText ? (v.Text ?? "") : "");
                        }
                        break;
                    }
                }
            }
        }

        private static void ReadDataStore(BinaryReader r, PLDataStore store)
        {
            int count = r.ReadInt32();
            var entries = new List<PLDataEntry>(count);
            for (int i = 0; i < count; i++)
            {
                var e = new PLDataEntry
                {
                    Name        = ReadString(r),
                    Kind        = (PLDataKind)r.ReadInt32(),
                    Source      = ReadString(r),
                    MasterIndex = r.ReadInt32(),
                    ObjectId    = r.ReadUInt64(),
                    CreatedAt   = DateTime.FromBinary(r.ReadInt64()),
                };

                switch (e.Kind)
                {
                    case PLDataKind.IndexSet:
                        e.IndexSet = r.ReadBoolean()
                            ? RemoteBinaryIO.ReadPartsSet(r)
                            : new PartsSelectionSet(e.Name);
                        e.IndexSet.Name = e.Name;
                        break;

                    case PLDataKind.LoopSet:
                    {
                        int n = r.ReadInt32();
                        e.Loops = new List<PLDataLoop>(n);
                        for (int k = 0; k < n; k++)
                        {
                            var lp = new PLDataLoop { Centroid = ReadVector3(r) };
                            lp.Vertices = RemoteBinaryIO.ReadIntList(r);
                            e.Loops.Add(lp);
                        }
                        break;
                    }

                    case PLDataKind.ValueSet:
                    {
                        int n = r.ReadInt32();
                        e.Values = new List<PLDataValue>(n);
                        for (int k = 0; k < n; k++)
                        {
                            string key    = ReadString(r);
                            bool   isText = r.ReadBoolean();
                            double num    = r.ReadDouble();
                            string text   = RemoteBinaryIO.ReadLongString(r);
                            e.Values.Add(isText ? PLDataValue.Str(key, text) : PLDataValue.Num(key, num));
                        }
                        break;
                    }
                }

                entries.Add(e);
            }

            if (store != null) store.ReplaceAll(entries);
        }

        // ---- underlay.csv / underlay_plates.csv ----

        private static void WriteUnderlay(BinaryWriter w, UnderlayData u)
        {
            var dirs = new List<(UnderlayDirection dir, UnderlaySlotData s)>();
            if (u != null)
                foreach (UnderlayDirection dir in Enum.GetValues(typeof(UnderlayDirection)))
                {
                    var s = u.Get(dir);
                    if (s != null && !s.IsEmpty) dirs.Add((dir, s));
                }

            w.Write(dirs.Count);
            foreach (var (dir, s) in dirs)
            {
                WriteString(w, UnderlayData.NameOf(dir));
                WriteString(w, s.FilePath);
                RemoteBinaryIO.WriteVector2(w, s.TopLeft);
                RemoteBinaryIO.WriteVector2(w, s.ScaleOrigin);
                RemoteBinaryIO.WriteVector2(w, s.Scale);
                WriteVector3(w, s.Corner0);
                WriteVector3(w, s.Corner1);
                w.Write(s.ViewRelative);
                w.Write(s.Contrast);
                w.Write(s.Intensity);
            }

            var plates = new List<UnderlayPlateSlotData>();
            if (u != null) foreach (var p in u.Plates) if (p != null && !p.IsEmpty) plates.Add(p);
            w.Write(plates.Count);
            foreach (var p in plates)
            {
                w.Write(p.ObjectId);
                WriteString(w, p.FilePath);
                RemoteBinaryIO.WriteVector2(w, p.Corner0);
                RemoteBinaryIO.WriteVector2(w, p.Corner1);
                w.Write(p.Contrast);
                w.Write(p.Intensity);
            }
        }

        private static UnderlayData ReadUnderlay(BinaryReader r)
        {
            var u = new UnderlayData();

            int dirCount = r.ReadInt32();
            for (int i = 0; i < dirCount; i++)
            {
                string dirName = ReadString(r);
                string path    = ReadString(r);
                var topLeft     = RemoteBinaryIO.ReadVector2(r);
                var scaleOrigin = RemoteBinaryIO.ReadVector2(r);
                var scale       = RemoteBinaryIO.ReadVector2(r);
                var c0          = ReadVector3(r);
                var c1          = ReadVector3(r);
                bool viewRel    = r.ReadBoolean();
                float contrast  = r.ReadSingle();
                float intensity = r.ReadSingle();

                if (!UnderlayData.TryParse(dirName, out var dir) || string.IsNullOrEmpty(path)) continue;
                var s = u.Get(dir);
                s.FilePath     = path;
                s.TopLeft      = topLeft;
                s.ScaleOrigin  = scaleOrigin;
                s.Scale        = scale;
                s.Corner0      = c0;
                s.Corner1      = c1;
                s.ViewRelative = viewRel;
                s.Contrast     = contrast;
                s.Intensity    = intensity;
            }

            int plateCount = r.ReadInt32();
            for (int i = 0; i < plateCount; i++)
            {
                u.SetPlate(new UnderlayPlateSlotData
                {
                    ObjectId  = r.ReadUInt64(),
                    FilePath  = ReadString(r),
                    Corner0   = RemoteBinaryIO.ReadVector2(r),
                    Corner1   = RemoteBinaryIO.ReadVector2(r),
                    Contrast  = r.ReadSingle(),
                    Intensity = r.ReadSingle(),
                });
            }

            return u.IsEmpty ? null : u;
        }

        // ================================================================
        // PLRS v7：メッシュ単位の付帯
        // ================================================================

        private static void WriteMeshExtras(BinaryWriter w, MeshContext mc)
        {
            var mo = mc.MeshObject;

            w.Write(mc.IsFolding);
            w.Write(mc.MirrorMaterialOffset);
            w.Write(mc.MirrorGeometryDerived);
            w.Write(mc.DetachedMirrorObjectId);
            w.Write(mc.IsMirrorBranchRoot);

            w.Write(mo?.IsTriangulated ?? false);
            w.Write(mo?.PreserveNormals ?? false);
            w.Write((int)(mo?.SkinKind ?? SkinKind.MeshFilter));

            // Humanoid（per-bone）
            WriteString(w, mo?.HumanBodyBone ?? "");
            w.Write(mo?.MirrorBoneIndex ?? -1);
            var hl = mo?.HumanLimit;
            w.Write(hl != null);
            if (hl != null)
            {
                w.Write(hl.UseDefaultValues);
                WriteVector3(w, hl.Min);
                WriteVector3(w, hl.Max);
                WriteVector3(w, hl.Center);
                w.Write(hl.AxisLength);
            }

            // SpringBone
            var cols = mo?.SpringBoneColliders;
            int cc = 0;
            if (cols != null) foreach (var c in cols) if (c != null) cc++;
            w.Write(cols != null);
            w.Write(cc);
            if (cols != null)
                foreach (var c in cols)
                {
                    if (c == null) continue;
                    w.Write((int)c.Shape);
                    WriteVector3(w, c.Offset);
                    w.Write(c.Radius);
                    WriteVector3(w, c.Tail);
                    WriteVector3(w, c.Normal);
                    RemoteBinaryIO.WriteIntList(w, c.SpringBoneGroupIndices);
                }

            var j = mo?.SpringBoneJoint;
            w.Write(j != null);
            if (j != null)
            {
                w.Write(j.HitRadius);
                w.Write(j.StiffnessForce);
                w.Write(j.GravityPower);
                WriteVector3(w, j.GravityDir);
                w.Write(j.DragForce);
                w.Write((int)j.AngleLimitType);
                WriteQuaternion(w, j.LimitRotation);
                w.Write(j.Pitch);
                w.Write(j.Yaw);
            }

            var ch = mo?.SpringBoneChainRoot;
            w.Write(ch != null);
            if (ch != null)
            {
                WriteString(w, ch.Name ?? "");
                WriteString(w, ch.CenterBoneName ?? "");
                RemoteBinaryIO.WriteIntList(w, ch.SpringBoneColliderGroupIndices);
            }

            // VRM 一人称・ノード制約
            w.Write((int)(mo?.VrmFirstPerson ?? VrmFirstPersonType.Auto));
            var con = mo?.VrmConstraint;
            w.Write(con != null);
            if (con != null)
            {
                w.Write((int)con.Kind);
                WriteString(w, con.SourceName ?? "");
                w.Write(con.Weight);
                w.Write((int)con.RollAxis);
                w.Write((int)con.AimAxis);
            }

            // 選択セット（ss 行）
            RemoteBinaryIO.WritePartsSetList(w, mc.PartsSelectionSetList);
        }

        private static void ReadMeshExtras(BinaryReader r, MeshContext mc)
        {
            var mo = mc.MeshObject;

            mc.IsFolding              = r.ReadBoolean();
            mc.MirrorMaterialOffset   = r.ReadInt32();
            mc.MirrorGeometryDerived  = r.ReadBoolean();
            mc.DetachedMirrorObjectId = r.ReadUInt64();
            mc.IsMirrorBranchRoot     = r.ReadBoolean();

            mo.IsTriangulated  = r.ReadBoolean();
            mo.PreserveNormals = r.ReadBoolean();
            mo.SetSkinKind((SkinKind)r.ReadInt32());

            mo.HumanBodyBone   = ReadString(r);
            mo.MirrorBoneIndex = r.ReadInt32();
            if (r.ReadBoolean())
            {
                mo.HumanLimit = new HumanLimitData
                {
                    UseDefaultValues = r.ReadBoolean(),
                    Min              = ReadVector3(r),
                    Max              = ReadVector3(r),
                    Center           = ReadVector3(r),
                    AxisLength       = r.ReadSingle(),
                };
            }

            bool hasCols = r.ReadBoolean();
            int cc = r.ReadInt32();
            if (hasCols) mo.SpringBoneColliders = new List<SpringBoneColliderData>(cc);
            for (int i = 0; i < cc; i++)
            {
                var c = new SpringBoneColliderData
                {
                    Shape  = (SpringBoneColliderShape)r.ReadInt32(),
                    Offset = ReadVector3(r),
                    Radius = r.ReadSingle(),
                    Tail   = ReadVector3(r),
                    Normal = ReadVector3(r),
                    SpringBoneGroupIndices = RemoteBinaryIO.ReadIntList(r),
                };
                mo.SpringBoneColliders?.Add(c);
            }

            if (r.ReadBoolean())
            {
                mo.SpringBoneJoint = new SpringBoneJointData
                {
                    HitRadius      = r.ReadSingle(),
                    StiffnessForce = r.ReadSingle(),
                    GravityPower   = r.ReadSingle(),
                    GravityDir     = ReadVector3(r),
                    DragForce      = r.ReadSingle(),
                    AngleLimitType = (SpringBoneAngleLimitType)r.ReadInt32(),
                    LimitRotation  = ReadQuaternion(r),
                    Pitch          = r.ReadSingle(),
                    Yaw            = r.ReadSingle(),
                };
            }

            if (r.ReadBoolean())
            {
                mo.SpringBoneChainRoot = new SpringBoneChainData
                {
                    Name                           = ReadString(r),
                    CenterBoneName                 = ReadString(r),
                    SpringBoneColliderGroupIndices = RemoteBinaryIO.ReadIntList(r),
                };
            }

            mo.VrmFirstPerson = ModelSerializer.ToVrmFirstPersonType(r.ReadInt32());
            if (r.ReadBoolean())
            {
                mo.VrmConstraint = new VrmNodeConstraintData
                {
                    Kind       = (VrmConstraintKind)r.ReadInt32(),
                    SourceName = ReadString(r),
                    Weight     = r.ReadSingle(),
                    RollAxis   = (VrmRollAxis)r.ReadInt32(),
                    AimAxis    = (VrmAimAxis)r.ReadInt32(),
                };
            }

            mc.PartsSelectionSetList = RemoteBinaryIO.ReadPartsSetList(r);
        }

        // ================================================================
        // 受信側の後始末
        // ================================================================

        /// <summary>
        /// PLRS（サマリ）で MeshObject に載せた付帯を、PLRD（ジオメトリ）で作り直した
        /// MeshObject へ移す。PLRD は頂点と面しか運ばないので、差し替えると消える。
        /// </summary>
        public static void CopySummaryAttributes(MeshObject from, MeshObject to)
        {
            if (from == null || to == null || ReferenceEquals(from, to)) return;

            // MeshContext の多くの欄は MeshObject に置かれている（Depth・親・BoneTransform など。
            // MeshContext.Transform.cs）。ジオメトリ以外は MeshObject.Clone と同じ範囲をすべて移す。
            // 線分群・法線除外セットは PLRD（MeshFieldFlags.Extras）が運ぶので移さない。
            to.Type                 = from.Type;
            to.ParentIndex          = from.ParentIndex;
            to.Depth                = from.Depth;
            to.HierarchyParentIndex = from.HierarchyParentIndex;
            to.IgnorePoseInArmature = from.IgnorePoseInArmature;
            to.IsMirrorBranchRoot   = from.IsMirrorBranchRoot;
            to.Billboard            = from.Billboard;
            to.DoubleSidedDisplay   = from.DoubleSidedDisplay;
            to.HideFaceShading      = from.HideFaceShading;
            to.MirrorBakeState      = from.MirrorBakeState;
            to.BoneTransform        = from.BoneTransform;
            to.PmxBone              = from.PmxBone;

            to.IKData              = from.IKData;
            to.IKLink              = from.IKLink;
            to.RigidBodyData       = from.RigidBodyData;
            to.JointData           = from.JointData;
            to.HumanBodyBone       = from.HumanBodyBone;
            to.MirrorBoneIndex     = from.MirrorBoneIndex;
            to.HumanLimit          = from.HumanLimit;
            to.SpringBoneColliders = from.SpringBoneColliders;
            to.SpringBoneJoint     = from.SpringBoneJoint;
            to.SpringBoneChainRoot = from.SpringBoneChainRoot;
            to.VrmFirstPerson      = from.VrmFirstPerson;
            to.VrmConstraint       = from.VrmConstraint;
            to.IsTriangulated      = from.IsTriangulated;
            to.PreserveNormals     = from.PreserveNormals;
            to.SetSkinKind(from.SkinKind);
        }

        /// <summary>
        /// モデル 1 体ぶんのフレームを受け終えたあとの組み直し。CSV の読込の末尾と同じ。
        ///   ミラー対を組む → IK の集約リンクを組み直す → Humanoid の集中辞書を組み直す。
        /// </summary>
        public static void FinishReceivedModel(ModelContext model)
        {
            if (model == null) return;

            if (_pendingMirrorPairs.TryGetValue(model, out var pending))
            {
                _pendingMirrorPairs.Remove(model);
                model.MirrorPairs = new List<MirrorPair>();
                foreach (var (ri, mi, ax) in pending)
                {
                    if (ri < 0 || ri >= model.Count || mi < 0 || mi >= model.Count) continue;
                    var pair = new MirrorPair
                    {
                        Real   = model.GetMeshContext(ri),
                        Mirror = model.GetMeshContext(mi),
                        Axis   = (SymmetryAxis)ax,
                    };
                    if (pair.Real == null || pair.Mirror == null) continue;
                    if (pair.Build()) model.MirrorPairs.Add(pair);
                    else Debug.LogWarning($"[RemoteProgressiveSerializer] MirrorPair build failed: {pair.Real.Name} ↔ {pair.Mirror.Name}: {pair.BuildLog}");
                }
            }

            Poly_Ling.Ops.IKChainResolver.RebuildLinksFromPerBone(model);
            Poly_Ling.Ops.HumanoidMappingResolver.RebuildMappingFromPerBone(model);
        }

        // ================================================================
        // モデル 1 体だけの丸ごと（ファイルの「モデル単位」）
        //   形は SerializeWholeProject と同じ（PLRH は 1 モデル・現在 0）。
        //   読みは DeserializeWholeProject でよい。受け側はモデルを足す。
        // ================================================================

        public static byte[] SerializeSingleModel(ProjectContext project, int modelIndex)
        {
            if (project == null || modelIndex < 0 || modelIndex >= project.ModelCount) return null;
            var model = project.Models[modelIndex];
            if (model == null) return null;

            var frames = new List<byte[]>();
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.ProjectHeader);
                w.Write((byte)2);
                w.Write((byte)0);
                w.Write((ushort)1);
                WriteString(w, project.Name);
                WriteWorkAxisLibrary(w, project.WorkAxes);
                frames.Add(ms.ToArray());
            }

            var mm = SerializeModelMeta(model, 0);
            if (mm != null) frames.Add(mm);
            for (int si = 0; si < model.Count; si++)
            {
                var s = SerializeMeshSummary(model.MeshContextList[si], 0, si);
                if (s != null) frames.Add(s);
            }
            for (int si = 0; si < model.Count; si++)
            {
                var mc = model.MeshContextList[si];
                if (mc?.MeshObject == null || mc.MeshObject.VertexCount == 0) continue;
                var md = SerializeMeshData(mc, 0, si, MeshFieldFlags.Complete);
                if (md != null) frames.Add(md);
            }
            return BuildBatch(frames);
        }

        // ================================================================
        // メッシュ単位の断片（CSV の名前ベース断片と同じ使い方）
        //
        // [PLRF] Magic(4) Version(1) Pad(3)
        //        [string] 元モデル名
        //        [4B] 名前表の件数 + [string]×件数 … 元モデルの MeshContextList の並び（索引→名前）
        //        [4B] ミラー対の件数 + (Real 名, Mirror 名, 軸) … Real が断片に入っている対だけ
        // [PLRS]/[PLRD] × 断片に入れたメッシュ（索引は元モデルのまま）
        //
        // 受け側（MergeMeshFragment）は CSV の MergeCsvFromFolder と同じく、
        // 名前が同じものは置き換え、無ければ足す。索引で持つ参照は名前表で引き直す
        // （CSV の ResolveNameReferences と同じ。加えて IK 目標・左右対ボーン・剛体・ジョイントも）。
        // ================================================================

        public static byte[] SerializeMeshFragment(ModelContext model, IList<int> masterIndices)
        {
            if (model == null || masterIndices == null || masterIndices.Count == 0) return null;

            Poly_Ling.Ops.IKChainResolver.SyncPerBoneFromLinks(model);
            Poly_Ling.Ops.HumanoidMappingResolver.SyncPerBoneFromMapping(model);

            var picked = new List<int>();
            var pickedSet = new HashSet<int>();
            foreach (int i in masterIndices)
                if (i >= 0 && i < model.Count && model.MeshContextList[i] != null && pickedSet.Add(i))
                    picked.Add(i);
            if (picked.Count == 0) return null;

            var frames = new List<byte[]>();
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.Fragment);
                w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                WriteString(w, model.Name);

                w.Write(model.Count);
                for (int i = 0; i < model.Count; i++)
                    WriteString(w, model.MeshContextList[i]?.Name ?? "");

                var pairs = new List<(string, string, int)>();
                if (model.MirrorPairs != null)
                    foreach (var p in model.MirrorPairs)
                    {
                        if (p?.Real == null || p.Mirror == null) continue;
                        int ri = model.MeshContextList.IndexOf(p.Real);
                        if (!pickedSet.Contains(ri)) continue;
                        pairs.Add((p.Real.Name ?? "", p.Mirror.Name ?? "", (int)p.Axis));
                    }
                w.Write(pairs.Count);
                foreach (var (rn, mn, ax) in pairs) { WriteString(w, rn); WriteString(w, mn); w.Write(ax); }

                frames.Add(ms.ToArray());
            }

            foreach (int si in picked)
            {
                var s = SerializeMeshSummary(model.MeshContextList[si], 0, si);
                if (s != null) frames.Add(s);
            }
            foreach (int si in picked)
            {
                var mc = model.MeshContextList[si];
                if (mc?.MeshObject == null || mc.MeshObject.VertexCount == 0) continue;
                var md = SerializeMeshData(mc, 0, si, MeshFieldFlags.Complete);
                if (md != null) frames.Add(md);
            }

            // 1 フレームだけにならないよう（見出し + 1 メッシュ以上）、必ず束で返す。
            return BuildBatch(frames);
        }

        /// <summary>断片かどうか（束の先頭フレームが PLRF）。</summary>
        public static bool IsMeshFragment(byte[] data)
        {
            var frames = SplitBatch(data);
            return frames.Count > 0 && RemoteMagic.Read(frames[0]) == RemoteMagic.Fragment;
        }

        /// <summary>
        /// 断片を target へ足す。名前が同じものは置き換え、無ければ末尾に足す。
        /// 失敗理由を返す（成功時は null）。
        /// </summary>
        public static string MergeMeshFragment(byte[] data, ModelContext target, out int added, out int replaced)
        {
            added = 0; replaced = 0;
            if (target == null) return "モデルがありません";

            var frames = SplitBatch(data);
            if (frames.Count == 0 || RemoteMagic.Read(frames[0]) != RemoteMagic.Fragment)
                return "断片ファイルではありません";

            // 見出し
            var srcNames = new List<string>();
            var pairRows = new List<(string real, string mirror, int axis)>();
            using (var ms = new MemoryStream(frames[0]))
            using (var r = new BinaryReader(ms))
            {
                r.ReadUInt32(); r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();
                ReadString(r);
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++) srcNames.Add(ReadString(r));
                int pc = r.ReadInt32();
                for (int i = 0; i < pc; i++) pairRows.Add((ReadString(r), ReadString(r), r.ReadInt32()));
            }

            // メッシュ（元の索引 → 受けた MeshContext）
            var bySrc = new Dictionary<int, MeshContext>();
            var order = new List<int>();
            for (int f = 1; f < frames.Count; f++)
            {
                uint magic = RemoteMagic.Read(frames[f]);
                if (magic == RemoteMagic.MeshSummary)
                {
                    var s = DeserializeMeshSummary(frames[f]);
                    if (s == null) continue;
                    var (_, si, mc, _, _) = s.Value;
                    if (!bySrc.ContainsKey(si)) order.Add(si);
                    bySrc[si] = mc;
                }
                else if (magic == RemoteMagic.MeshData)
                {
                    var d = DeserializeMeshData(frames[f]);
                    if (d == null) continue;
                    var (_, si, mesh) = d.Value;
                    if (mesh == null || !bySrc.TryGetValue(si, out var mc)) continue;
                    CopySummaryAttributes(mc.MeshObject, mesh);
                    mesh.Name = mc.Name;
                    mesh.Type = mc.Type;
                    mc.MeshObject = mesh;
                }
            }
            if (order.Count == 0) return "断片にメッシュがありません";

            // 置き換え・追加（CSV の MergeCsvFromFolder と同じ規則）
            var existing = new Dictionary<string, int>();
            for (int i = 0; i < target.MeshContextList.Count; i++)
            {
                var m = target.MeshContextList[i];
                if (m != null && !string.IsNullOrEmpty(m.Name) && !existing.ContainsKey(m.Name))
                    existing[m.Name] = i;
            }
            var merged = new List<MeshContext>();
            foreach (int si in order)
            {
                var mc = bySrc[si];
                mc.ParentModelContext = target;
                if (existing.TryGetValue(mc.Name ?? "", out int at)) { target.MeshContextList[at] = mc; replaced++; }
                else { target.Add(mc); added++; }
                merged.Add(mc);
            }

            // 索引で持つ参照を名前で引き直す
            var dst = new Dictionary<string, int>();
            for (int i = 0; i < target.MeshContextList.Count; i++)
            {
                var m = target.MeshContextList[i];
                if (m != null && !string.IsNullOrEmpty(m.Name) && !dst.ContainsKey(m.Name))
                    dst[m.Name] = i;
            }
            int Map(int srcIndex)
            {
                if (srcIndex < 0 || srcIndex >= srcNames.Count) return -1;
                string name = srcNames[srcIndex];
                return !string.IsNullOrEmpty(name) && dst.TryGetValue(name, out int di) ? di : -1;
            }

            foreach (var mc in merged)
            {
                mc.ParentIndex            = Map(mc.ParentIndex);
                mc.HierarchyParentIndex   = Map(mc.HierarchyParentIndex);
                mc.BakedMirrorSourceIndex = Map(mc.BakedMirrorSourceIndex);
                if (mc.IsMorph)
                {
                    mc.MorphParentIndex   = Map(mc.MorphParentIndex);
                    mc.MirrorOfMorphIndex = Map(mc.MirrorOfMorphIndex);
                }

                var mo = mc.MeshObject;
                if (mo == null) continue;
                if (mo.IKData != null)        mo.IKData.TargetIndex = Map(mo.IKData.TargetIndex);
                if (mo.MirrorBoneIndex >= 0)  mo.MirrorBoneIndex    = Map(mo.MirrorBoneIndex);
                if (mo.RigidBodyData != null) mo.RigidBodyData.BoneIndex = Map(mo.RigidBodyData.BoneIndex);
                if (mo.JointData != null)
                {
                    mo.JointData.RigidBodyIndexA = Map(mo.JointData.RigidBodyIndexA);
                    mo.JointData.RigidBodyIndexB = Map(mo.JointData.RigidBodyIndexB);
                }

                foreach (var v in mo.Vertices)
                {
                    if (v.BoneWeight.HasValue)       v.BoneWeight       = MapWeight(v.BoneWeight.Value, Map);
                    if (v.MirrorBoneWeight.HasValue) v.MirrorBoneWeight = MapWeight(v.MirrorBoneWeight.Value, Map);
                }

                mc.ReplaceUnityMesh(mo.VertexCount > 0 ? mo.ToUnityMesh() : null);
            }

            // ミラー対（CSV の BuildMirrorPairsFromEntries と同じく、既にある Real は足さない）
            if (target.MirrorPairs == null) target.MirrorPairs = new List<MirrorPair>();
            var haveReal = new HashSet<string>();
            foreach (var p in target.MirrorPairs) if (p?.Real != null) haveReal.Add(p.Real.Name);
            foreach (var (rn, mn, ax) in pairRows)
            {
                if (haveReal.Contains(rn)) continue;
                if (!dst.TryGetValue(rn, out int ri) || !dst.TryGetValue(mn, out int mi)) continue;
                var pair = new MirrorPair
                {
                    Real   = target.GetMeshContext(ri),
                    Mirror = target.GetMeshContext(mi),
                    Axis   = (SymmetryAxis)ax,
                };
                if (pair.Build()) { target.MirrorPairs.Add(pair); haveReal.Add(rn); }
            }

            target.InvalidateTypedIndices();
            Poly_Ling.Ops.IKChainResolver.RebuildLinksFromPerBone(target);
            Poly_Ling.Ops.HumanoidMappingResolver.RebuildMappingFromPerBone(target);
            return null;
        }

        private static BoneWeight MapWeight(BoneWeight bw, Func<int, int> map)
        {
            // CSV の名前ベース（ResolveNameReferences）と同じく、引けない名前は -1。
            bw.boneIndex0 = map(bw.boneIndex0);
            bw.boneIndex1 = map(bw.boneIndex1);
            bw.boneIndex2 = map(bw.boneIndex2);
            bw.boneIndex3 = map(bw.boneIndex3);
            return bw;
        }

        /// <summary>PLRB 束をフレームに分ける。束でなければそれ 1 つ。</summary>
        public static List<byte[]> SplitBatch(byte[] data)
        {
            var list = new List<byte[]>();
            if (data == null || data.Length < 4) return list;
            if (RemoteMagic.Read(data) != RemoteMagic.Batch) { list.Add(data); return list; }
            if (data.Length < 12) return list;

            int count = (int)BitConverter.ToUInt32(data, 8);
            int offset = 12;
            for (int i = 0; i < count; i++)
            {
                if (offset + 4 > data.Length) break;
                int len = (int)BitConverter.ToUInt32(data, offset); offset += 4;
                if (offset + len > data.Length) break;
                var frame = new byte[len];
                Array.Copy(data, offset, frame, 0, len);
                list.Add(frame);
                offset += len;
            }
            return list;
        }
    }
}
