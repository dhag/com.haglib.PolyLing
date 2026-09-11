// CsvModelSerializer.VrmCsv.cs
// CSV モデル入出力：springbonegroups・vrmmeta・vrmlookat・coordinate・avatarsettings・previewsettings。
// Runtime/Poly_Ling_Main/Core/Serialization/FolderSerializer/ に配置

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
    public static partial class CsvModelSerializer
    {
        // ================================================================
        // springbonegroups.csv（SpringBone コライダーグループ名）
        //   規約: MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
        //   本リストの並び順が index を定める。per-bone 側の grp index はここへの参照。
        // ================================================================

        private static void WriteSpringBoneGroupsCsv(string folderPath, ModelContext model)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_SpringBoneGroups,version,1.0");

            foreach (var name in model.SpringBoneColliderGroupNames)
                sb.AppendLine(Esc(name ?? ""));

            File.WriteAllText(Path.Combine(folderPath, "springbonegroups.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void ReadSpringBoneGroupsCsv(string path, ModelContext model)
        {
            var names = new List<string>();
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 1) continue;
                names.Add(Unesc(cols[0]));
            }
            model.SpringBoneColliderGroupNames = names;
        }

        // ================================================================
        // vrmmeta.csv（VRM 1.0 メタ情報：モデルレベル）
        //   1行1項目の key,value 形式。作者と参照元は複数行になりうるので
        //   author / reference を並べる。空欄の項目は行ごと省く。
        //   未設定（ModelContext.VrmMeta == null）ならファイルを作らない。
        // ================================================================

        private static void WriteOrDeleteVrmMetaCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "vrmmeta.csv");

            var m = model.VrmMeta;
            if (m == null)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_VrmMeta,version,1.0");

            sb.AppendLine($"name,{Esc(m.Name ?? "")}");
            sb.AppendLine($"version,{Esc(m.Version ?? "")}");
            if (m.Authors != null)
                foreach (var a in m.Authors)
                    if (!string.IsNullOrEmpty(a)) sb.AppendLine($"author,{Esc(a)}");
            sb.AppendLine($"copyrightInformation,{Esc(m.CopyrightInformation ?? "")}");
            sb.AppendLine($"contactInformation,{Esc(m.ContactInformation ?? "")}");
            if (m.References != null)
                foreach (var r in m.References)
                    if (!string.IsNullOrEmpty(r)) sb.AppendLine($"reference,{Esc(r)}");
            sb.AppendLine($"thirdPartyLicenses,{Esc(m.ThirdPartyLicenses ?? "")}");
            sb.AppendLine($"thumbnailPath,{Esc(m.ThumbnailPath ?? "")}");

            sb.AppendLine($"avatarPermission,{(int)m.AvatarPermission}");
            sb.AppendLine($"violentUsage,{m.ViolentUsage}");
            sb.AppendLine($"sexualUsage,{m.SexualUsage}");
            sb.AppendLine($"commercialUsage,{(int)m.CommercialUsage}");
            sb.AppendLine($"politicalOrReligiousUsage,{m.PoliticalOrReligiousUsage}");
            sb.AppendLine($"antisocialOrHateUsage,{m.AntisocialOrHateUsage}");

            sb.AppendLine($"creditNotation,{(int)m.CreditNotation}");
            sb.AppendLine($"redistribution,{m.Redistribution}");
            sb.AppendLine($"modification,{(int)m.Modification}");
            sb.AppendLine($"otherLicenseUrl,{Esc(m.OtherLicenseUrl ?? "")}");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void ReadVrmMetaCsv(string path, ModelContext model)
        {
            var m = new VrmMetaData
            {
                Authors    = new List<string>(),
                References = new List<string>(),
            };

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 2) continue;

                switch (cols[0])
                {
                    case "name":                 m.Name                 = Unesc(cols[1]); break;
                    case "version":              m.Version              = Unesc(cols[1]); break;
                    case "author":               m.Authors.Add(Unesc(cols[1]));           break;
                    case "copyrightInformation": m.CopyrightInformation = Unesc(cols[1]); break;
                    case "contactInformation":   m.ContactInformation   = Unesc(cols[1]); break;
                    case "reference":            m.References.Add(Unesc(cols[1]));        break;
                    case "thirdPartyLicenses":   m.ThirdPartyLicenses   = Unesc(cols[1]); break;
                    case "thumbnailPath":        m.ThumbnailPath        = Unesc(cols[1]); break;

                    case "avatarPermission":
                        m.AvatarPermission = ModelSerializer.ToVrmAvatarPermission(PInt(cols, 1));
                        break;
                    case "violentUsage":              m.ViolentUsage              = PBool(cols, 1); break;
                    case "sexualUsage":               m.SexualUsage               = PBool(cols, 1); break;
                    case "commercialUsage":
                        m.CommercialUsage = ModelSerializer.ToVrmCommercialUsage(PInt(cols, 1));
                        break;
                    case "politicalOrReligiousUsage": m.PoliticalOrReligiousUsage = PBool(cols, 1); break;
                    case "antisocialOrHateUsage":     m.AntisocialOrHateUsage     = PBool(cols, 1); break;

                    case "creditNotation":
                        m.CreditNotation = ModelSerializer.ToVrmCreditNotation(PInt(cols, 1));
                        break;
                    case "redistribution":  m.Redistribution  = PBool(cols, 1); break;
                    case "modification":
                        m.Modification = ModelSerializer.ToVrmModification(PInt(cols, 1));
                        break;
                    case "otherLicenseUrl": m.OtherLicenseUrl = Unesc(cols[1]); break;
                }
            }

            model.VrmMeta = m;
        }

        // ================================================================
        // vrmlookat.csv（VRM 1.0 視線設定：モデルレベル）
        //   offsetFromHead は Unity 左手系のまま。系変換は VRM 出力側が行う。
        //   4本の対応づけは rangeMap,<名前>,<入力上限[度]>,<出力量>。
        // ================================================================

        private static void WriteOrDeleteVrmLookAtCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "vrmlookat.csv");

            var l = model.VrmLookAt;
            if (l == null)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_VrmLookAt,version,1.0");
            sb.AppendLine($"offsetFromHead,{Fl(l.OffsetFromHead.x)},{Fl(l.OffsetFromHead.y)},{Fl(l.OffsetFromHead.z)}");
            sb.AppendLine($"lookAtType,{(int)l.LookAtType}");
            AppendRangeMap(sb, "horizontalInner", l.HorizontalInner);
            AppendRangeMap(sb, "horizontalOuter", l.HorizontalOuter);
            AppendRangeMap(sb, "verticalDown",    l.VerticalDown);
            AppendRangeMap(sb, "verticalUp",      l.VerticalUp);

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void AppendRangeMap(StringBuilder sb, string key, VrmLookAtRangeMap m)
        {
            var src = m ?? new VrmLookAtRangeMap();
            sb.AppendLine($"rangeMap,{key},{Fl(src.InputMaxDegrees)},{Fl(src.OutputScale)}");
        }

        private static void ReadVrmLookAtCsv(string path, ModelContext model)
        {
            var l = new VrmLookAtData();

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 2) continue;

                switch (cols[0])
                {
                    case "offsetFromHead":
                        l.OffsetFromHead = new Vector3(PFl(cols, 1), PFl(cols, 2), PFl(cols, 3));
                        break;
                    case "lookAtType":
                        l.LookAtType = (PInt(cols, 1) == 1) ? VrmLookAtType.Expression : VrmLookAtType.Bone;
                        break;
                    case "rangeMap":
                    {
                        if (cols.Length < 4) break;
                        var m = new VrmLookAtRangeMap(PFl(cols, 2, 90f), PFl(cols, 3, 10f));
                        switch (cols[1])
                        {
                            case "horizontalInner": l.HorizontalInner = m; break;
                            case "horizontalOuter": l.HorizontalOuter = m; break;
                            case "verticalDown":    l.VerticalDown    = m; break;
                            case "verticalUp":      l.VerticalUp      = m; break;
                        }
                        break;
                    }
                }
            }

            model.VrmLookAt = l;
        }

        // ================================================================
        // coordinate.csv（PMX / MQO の座標規約：モデルレベル）
        //   倍率と軸反転。捨てると座標が 10 倍ずれる規約なので、
        //   捨てても表示が戻るだけの editorstate.csv とは分けてある。
        //   未設定（ModelContext.CoordinateConvention == null）ならファイルを作らない。
        // ================================================================

        private static void WriteOrDeleteCoordinateCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "coordinate.csv");

            var c = model.CoordinateConvention;
            if (c == null)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_Coordinate,version,1.0");
            sb.AppendLine($"pmxUnityRatio,{Fl(c.PmxUnityRatio)}");
            sb.AppendLine($"pmxFlipX,{c.PmxFlipX}");
            sb.AppendLine($"pmxFlipZ,{c.PmxFlipZ}");
            sb.AppendLine($"mqoUnityRatio,{Fl(c.MqoUnityRatio)}");
            sb.AppendLine($"mqoFlipX,{c.MqoFlipX}");
            sb.AppendLine($"mqoFlipZ,{c.MqoFlipZ}");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void ReadCoordinateCsv(string path, ModelContext model)
        {
            var c = new CoordinateConventionData();

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 2) continue;

                switch (cols[0])
                {
                    case "pmxUnityRatio": c.PmxUnityRatio = PFl(cols, 1, 0.1f);   break;
                    case "pmxFlipX":      c.PmxFlipX      = PBool(cols, 1, true); break;
                    case "pmxFlipZ":      c.PmxFlipZ      = PBool(cols, 1, true); break;
                    case "mqoUnityRatio": c.MqoUnityRatio = PFl(cols, 1, 0.01f);  break;
                    case "mqoFlipX":      c.MqoFlipX      = PBool(cols, 1, true); break;
                    case "mqoFlipZ":      c.MqoFlipZ      = PBool(cols, 1);       break;
                }
            }

            model.CoordinateConvention = c;
        }

        // ================================================================
        // avatarsettings.csv（Avatar リターゲット設定：モデルレベル）
        //   Unity の HumanDescription の 8 項目。1行1項目の key,value 形式。
        //   使うのは Editor のプレファブ書き出しだけで、VRM 出力には出ない。
        //   未設定（ModelContext.AvatarRetarget == null）ならファイルを作らない。
        // ================================================================

        private static void WriteOrDeleteAvatarRetargetCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "avatarsettings.csv");

            var a = model.AvatarRetarget;
            if (a == null)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_AvatarRetarget,version,1.0");
            sb.AppendLine($"upperArmTwist,{Fl(a.UpperArmTwist)}");
            sb.AppendLine($"lowerArmTwist,{Fl(a.LowerArmTwist)}");
            sb.AppendLine($"upperLegTwist,{Fl(a.UpperLegTwist)}");
            sb.AppendLine($"lowerLegTwist,{Fl(a.LowerLegTwist)}");
            sb.AppendLine($"armStretch,{Fl(a.ArmStretch)}");
            sb.AppendLine($"legStretch,{Fl(a.LegStretch)}");
            sb.AppendLine($"feetSpacing,{Fl(a.FeetSpacing)}");
            sb.AppendLine($"hasTranslationDoF,{a.HasTranslationDoF}");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void ReadAvatarRetargetCsv(string path, ModelContext model)
        {
            var a = new AvatarRetargetData();

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 2) continue;

                switch (cols[0])
                {
                    case "upperArmTwist":     a.UpperArmTwist     = PFl(cols, 1, 0.5f);  break;
                    case "lowerArmTwist":     a.LowerArmTwist     = PFl(cols, 1, 0.5f);  break;
                    case "upperLegTwist":     a.UpperLegTwist     = PFl(cols, 1, 0.5f);  break;
                    case "lowerLegTwist":     a.LowerLegTwist     = PFl(cols, 1, 0.5f);  break;
                    case "armStretch":        a.ArmStretch        = PFl(cols, 1, 0.05f); break;
                    case "legStretch":        a.LegStretch        = PFl(cols, 1, 0.05f); break;
                    case "feetSpacing":       a.FeetSpacing       = PFl(cols, 1, 0f);    break;
                    case "hasTranslationDoF": a.HasTranslationDoF = PBool(cols, 1);      break;
                }
            }

            model.AvatarRetarget = a;
        }

        // ================================================================
        // previewsettings.csv（PolyLing 内のプレビュー評価設定：モデルレベル）
        //   fixedDeltaTime … 0=実時間、>0=固定タイムステップ[秒]
        //   warmupFrames   … 評価開始直後の安定化フレーム数
        //
        //   VRM には出ない PolyLing 内部の値。springbonegroups.csv と並ぶ名前だと
        //   VRM 出力用に見えるので、内部設定と分かる名前にしてある。
        //   今後の内部設定もこのファイルへ集約する。
        // ================================================================

        private static void WritePreviewSettingsCsv(string folderPath, ModelContext model)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_PreviewSettings,version,1.0");
            sb.AppendLine($"fixedDeltaTime,{Fl(model.SpringBoneFixedDeltaTime)}");
            sb.AppendLine($"warmupFrames,{model.SpringBoneWarmupFrames.ToString(CultureInfo.InvariantCulture)}");

            File.WriteAllText(
                Path.Combine(folderPath, "previewsettings.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void ReadPreviewSettingsCsv(string path, ModelContext model)
        {
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 2) continue;

                switch (cols[0])
                {
                    case "fixedDeltaTime":
                        model.SpringBoneFixedDeltaTime = PFl(cols, 1, 0f);
                        break;
                    case "warmupFrames":
                        model.SpringBoneWarmupFrames = PInt(cols, 1, 3);
                        break;
                }
            }
        }

        /// <summary>
        /// エントリリストにMirrorPeer情報を設定し、MirrorSideが未含の場合は追加する。
        /// Copy/Cut/SaveCsv/SaveModelの全エクスポートパスで共通使用する。
        /// </summary>
        public static void EnrichEntriesWithMirrorPeers(List<CsvMeshEntry> entries, ModelContext model)
        {
            if (entries == null || model == null) return;

            // 既存エントリのMeshContextセット
            var existingMcs = new HashSet<MeshContext>();
            var mcToEntry = new Dictionary<MeshContext, CsvMeshEntry>();
            foreach (var e in entries)
            {
                if (e.MeshContext != null)
                {
                    existingMcs.Add(e.MeshContext);
                    mcToEntry[e.MeshContext] = e;
                }
            }

            // 追加すべきMirrorSideを収集（ループ中にentriesを変更しないため）
            var toAdd = new List<(int insertAfter, CsvMeshEntry entry)>();
            var handledReals = new HashSet<MeshContext>();

            // (1) MirrorPairsからペア情報を設定
            if (model.MirrorPairs != null)
            {
                foreach (var pair in model.MirrorPairs)
                {
                    if (pair.Real == null || pair.Mirror == null) continue;
                    if (!mcToEntry.TryGetValue(pair.Real, out var realEntry)) continue;

                    realEntry.MirrorPeerName = pair.Mirror.Name;
                    realEntry.MirrorPeerAxis = (int)pair.Axis;
                    handledReals.Add(pair.Real);

                    if (!existingMcs.Contains(pair.Mirror))
                    {
                        int mirrorGlobalIndex = model.MeshContextList.IndexOf(pair.Mirror);
                        var mirrorEntry = new CsvMeshEntry
                        {
                            GlobalIndex = mirrorGlobalIndex >= 0 ? mirrorGlobalIndex : 0,
                            MeshContext = pair.Mirror
                        };
                        int realPos = entries.IndexOf(realEntry);
                        toAdd.Add((realPos, mirrorEntry));
                        existingMcs.Add(pair.Mirror);
                    }
                }
            }

            // (2) MirrorPairsで処理されなかったmirrorType>0のエントリ → 命名規則で検索
            // モデル全体のMeshContext名→MeshContext辞書
            Dictionary<string, MeshContext> nameToMc = null;

            foreach (var e in entries)
            {
                if (e.MeshContext == null) continue;
                if (handledReals.Contains(e.MeshContext)) continue;
                if (e.MeshContext.MirrorType <= 0) continue;
                if (!string.IsNullOrEmpty(e.MirrorPeerName)) continue; // 既に設定済み

                // 命名規則: Real名+"+" がMirrorSide
                if (nameToMc == null)
                {
                    nameToMc = new Dictionary<string, MeshContext>();
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc != null && !string.IsNullOrEmpty(mc.Name) && !nameToMc.ContainsKey(mc.Name))
                            nameToMc[mc.Name] = mc;
                    }
                }

                string mirrorName = e.MeshContext.Name + "+";
                if (!nameToMc.TryGetValue(mirrorName, out var mirrorMc)) continue;

                e.MirrorPeerName = mirrorName;
                e.MirrorPeerAxis = e.MeshContext.MirrorAxis;

                if (!existingMcs.Contains(mirrorMc))
                {
                    int mirrorGlobalIndex = model.MeshContextList.IndexOf(mirrorMc);
                    var mirrorEntry = new CsvMeshEntry
                    {
                        GlobalIndex = mirrorGlobalIndex >= 0 ? mirrorGlobalIndex : 0,
                        MeshContext = mirrorMc
                    };
                    int realPos = entries.IndexOf(e);
                    toAdd.Add((realPos, mirrorEntry));
                    existingMcs.Add(mirrorMc);
                }

                Debug.Log($"[CsvModelSerializer] EnrichMirrorPeer by naming: {e.MeshContext.Name} → {mirrorName}");
            }

            // Real直後に挿入（後ろから挿入してインデックスずれを防止）
            for (int i = toAdd.Count - 1; i >= 0; i--)
            {
                entries.Insert(toAdd[i].insertAfter + 1, toAdd[i].entry);
            }
        }

        /// <summary>
        /// エントリのmirrorPeer情報からMirrorPairを構築してModelContextに追加
        /// 既にペアが存在する場合は重複追加しない
        /// </summary>
        public static void BuildMirrorPairsFromEntries(List<CsvMeshEntry> entries, ModelContext model)
        {
            if (entries == null || model == null) return;
            if (model.MirrorPairs == null)
                model.MirrorPairs = new List<MirrorPair>();

            // 既存ペアのReal名セット（重複防止）
            var existingRealNames = new HashSet<string>();
            foreach (var p in model.MirrorPairs)
            {
                if (p.Real != null)
                    existingRealNames.Add(p.Real.Name);
            }

            // MeshContext名→MeshContext逆引き
            var nameToCtx = new Dictionary<string, MeshContext>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && !string.IsNullOrEmpty(mc.Name) && !nameToCtx.ContainsKey(mc.Name))
                    nameToCtx[mc.Name] = mc;
            }

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.MirrorPeerName)) continue;
                if (entry.MeshContext == null) continue;

                string realName = entry.MeshContext.Name;
                if (string.IsNullOrEmpty(realName)) continue;
                if (existingRealNames.Contains(realName)) continue;

                if (!nameToCtx.TryGetValue(entry.MirrorPeerName, out var mirrorCtx)) continue;
                if (!nameToCtx.TryGetValue(realName, out var realCtx)) continue;

                var pair = new MirrorPair
                {
                    Real = realCtx,
                    Mirror = mirrorCtx,
                    Axis = (Poly_Ling.Symmetry.SymmetryAxis)entry.MirrorPeerAxis
                };

                if (pair.Build())
                {
                    model.MirrorPairs.Add(pair);
                    existingRealNames.Add(realName);
                    Debug.Log($"[CsvModelSerializer] MirrorPair from entry: {realName} ↔ {entry.MirrorPeerName}");
                }
                else
                {
                    Debug.LogWarning($"[CsvModelSerializer] MirrorPair build failed from entry: {realName} ↔ {entry.MirrorPeerName}: {pair.BuildLog}");
                }
            }
        }
    }
}
