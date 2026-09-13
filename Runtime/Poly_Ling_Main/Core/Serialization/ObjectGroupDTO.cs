// ObjectGroupDTO.cs
// オブジェクトグループのシリアライズ用データ構造。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置
//
// 【形は MorphExpressionDTO に合わせる】
//   JsonUtility が扱える形（配列は List、Dictionary は使えない）に落とす。
//   Args と MeshRefIds はどちらも辞書なので、キーと値の対を並べた List にする。
//
// 【並びを固定する】
//   Dictionary の列挙順は保証されないので、書き出す前にキー順へ並べ替える
//   （ObjectGroupStep.SortedArgs / SortedMeshRefIds）。並べ替えないと、
//   中身が同じでも保存のたびにファイルの差分が出る。
//
// 【ObjectId は文字列で持つ】
//   ulong は JsonUtility が扱えない。10 進の文字列にして往復させる。
//
// 【古い形との往復】
//   ステップが導入される前は、グループが action / args / meshRefs /
//   outputObjectId を 1 組だけ持っていた。読みでは steps が空のときに
//   その 4 つをステップ 0 として読む。書きでは steps を必ず書き、
//   ステップが 1 つのときだけ古い 4 つも埋める（1 ステップのグループは
//   古い読み手でもそのまま開ける）。2 ステップ以上は古い読み手では開けない。
//
// 【後から足した欄】
//   elementId / kind / purpose（段）と goal / preconditions / successCriteria /
//   tags / prov*（グループ）は、いずれも既定が空か 0。JsonUtility は無い欄を
//   既定のまま残すので、これらを持たない保存データもそのまま読める。
//   ElementId だけは空のままにできない（段を ID で指すため）ので、
//   読みの最後に ObjectGroup.EnsureElementIds が振る。

using System;
using System.Collections.Generic;
using System.Globalization;
using Poly_Ling.Data;

namespace Poly_Ling.Serialization
{
    /// <summary>キーと値の対（Args 用）。</summary>
    [Serializable]
    public class ObjectGroupArgDTO
    {
        public string key = "";
        public string value = "";
    }

    /// <summary>キーと ObjectId 列の対（MeshRefIds 用）。</summary>
    [Serializable]
    public class ObjectGroupMeshRefDTO
    {
        public string key = "";

        /// <summary>ObjectId の 10 進文字列。並びは Args の索引配列と 1 対 1。</summary>
        public List<string> objectIds = new List<string>();
    }

    /// <summary>ステップ 1 つぶん（生成コマンド 1 つ）。</summary>
    [Serializable]
    public class ObjectGroupStepDTO
    {
        public string action = "";

        /// <summary>段を指す名前。グループ内で一意。空の古いデータは読みで振る。</summary>
        public string elementId = "";

        /// <summary>段の種別（ObjectGroupStepKind の数値）。0 = 実行する段。</summary>
        public int kind = 0;

        /// <summary>この段が要る理由。</summary>
        public string purpose = "";

        public List<ObjectGroupArgDTO>     args     = new List<ObjectGroupArgDTO>();
        public List<ObjectGroupMeshRefDTO> meshRefs = new List<ObjectGroupMeshRefDTO>();

        /// <summary>出力先の ObjectId（10 進文字列）。空 = 出力先なし。</summary>
        public List<string> outputObjectIds = new List<string>();

        public static ObjectGroupStepDTO FromStep(ObjectGroupStep s)
        {
            if (s == null) return null;

            var dto = new ObjectGroupStepDTO
            {
                action    = s.Action ?? "",
                elementId = s.ElementId ?? "",
                kind      = (int)s.Kind,
                purpose   = s.Purpose ?? "",
            };

            foreach (var kv in s.SortedArgs())
                dto.args.Add(new ObjectGroupArgDTO { key = kv.Key, value = kv.Value ?? "" });

            foreach (var kv in s.SortedMeshRefIds())
            {
                var e = new ObjectGroupMeshRefDTO { key = kv.Key };
                if (kv.Value != null)
                    foreach (ulong id in kv.Value)
                        e.objectIds.Add(id.ToString(CultureInfo.InvariantCulture));
                dto.meshRefs.Add(e);
            }

            if (s.OutputObjectIds != null)
                foreach (ulong id in s.OutputObjectIds)
                    if (id != 0UL)
                        dto.outputObjectIds.Add(id.ToString(CultureInfo.InvariantCulture));

            return dto;
        }

        public ObjectGroupStep ToStep()
        {
            var s = new ObjectGroupStep
            {
                Action    = action ?? "",
                ElementId = elementId ?? "",
                Kind      = System.Enum.IsDefined(typeof(ObjectGroupStepKind), kind)
                            ? (ObjectGroupStepKind)kind
                            : ObjectGroupStepKind.Command,
                Purpose   = purpose ?? "",
            };

            if (args != null)
                foreach (var a in args)
                    if (a != null && !string.IsNullOrEmpty(a.key)) s.SetArg(a.key, a.value);

            if (meshRefs != null)
            {
                foreach (var r in meshRefs)
                {
                    if (r == null || string.IsNullOrEmpty(r.key)) continue;
                    var ids = new List<ulong>();
                    if (r.objectIds != null)
                        foreach (var t in r.objectIds) ids.Add(ObjectGroupDTO.ParseId(t));
                    s.SetMeshRefIds(r.key, ids);
                }
            }

            if (outputObjectIds != null)
            {
                foreach (var t in outputObjectIds)
                {
                    ulong id = ObjectGroupDTO.ParseId(t);
                    if (id != 0UL) s.OutputObjectIds.Add(id);
                }
            }

            return s;
        }
    }

    /// <summary>オブジェクトグループのシリアライズ用。</summary>
    [Serializable]
    public class ObjectGroupDTO
    {
        public string name = "";

        /// <summary>実行するコマンド列。並び順がそのまま実行順。</summary>
        public List<ObjectGroupStepDTO> steps = new List<ObjectGroupStepDTO>();

        // ── ここから下は古い形（1 ステップ前提）。読みの互換のために残す。
        //    書きでは、ステップが 1 つのときだけ埋める。

        public string action = "";

        public List<ObjectGroupArgDTO>     args     = new List<ObjectGroupArgDTO>();
        public List<ObjectGroupMeshRefDTO> meshRefs = new List<ObjectGroupMeshRefDTO>();

        /// <summary>出力先の ObjectId（10 進文字列）。"0" = なし。</summary>
        public string outputObjectId = "0";

        // ── ここから下はグループ単位の値。

        /// <summary>退避の ObjectId（10 進文字列）。"0" = なし。</summary>
        public string stashObjectId = "0";

        public string sourceDigest = "";
        public bool   autoUpdate;

        /// <summary>作成日時（ISO 8601 形式）。</summary>
        public string createdAt = "";

        // ── 意味情報。空でよい。古い保存データは空のまま読める。

        /// <summary>この手順で達成したいこと。</summary>
        public string goal = "";

        /// <summary>使う前に満たしているべきこと。</summary>
        public List<string> preconditions = new List<string>();

        /// <summary>終わったときに確かめること。</summary>
        public List<string> successCriteria = new List<string>();

        /// <summary>探すための札。</summary>
        public List<string> tags = new List<string>();

        /// <summary>由来。元にしたグループの名前。</summary>
        public string provParentName = "";

        /// <summary>由来。元から何を変えたか。</summary>
        public string provChangeSummary = "";

        /// <summary>由来。作った者。</summary>
        public string provCreatedBy = "";

        // ================================================================
        // 変換
        // ================================================================

        public static ObjectGroupDTO FromObjectGroup(ObjectGroup g)
        {
            if (g == null) return null;

            var dto = new ObjectGroupDTO
            {
                name          = g.Name ?? "",
                stashObjectId = g.StashObjectId.ToString(CultureInfo.InvariantCulture),
                sourceDigest  = g.SourceDigest ?? "",
                autoUpdate    = g.AutoUpdate,
                createdAt     = g.CreatedAt.ToString("o"),
                goal          = g.Goal ?? "",
            };

            if (g.Preconditions   != null) dto.preconditions   = new List<string>(g.Preconditions);
            if (g.SuccessCriteria != null) dto.successCriteria = new List<string>(g.SuccessCriteria);
            if (g.Tags            != null) dto.tags            = new List<string>(g.Tags);

            if (g.Provenance != null)
            {
                dto.provParentName    = g.Provenance.ParentName    ?? "";
                dto.provChangeSummary = g.Provenance.ChangeSummary ?? "";
                dto.provCreatedBy     = g.Provenance.CreatedBy     ?? "";
            }

            if (g.Steps != null)
            {
                foreach (var s in g.Steps)
                {
                    var sd = ObjectGroupStepDTO.FromStep(s);
                    if (sd != null) dto.steps.Add(sd);
                }
            }

            // 1 ステップのグループは古い形でも書いておく。
            if (dto.steps.Count == 1)
            {
                var only = dto.steps[0];
                dto.action         = only.action;
                dto.args           = only.args;
                dto.meshRefs       = only.meshRefs;
                dto.outputObjectId = only.outputObjectIds.Count > 0
                    ? only.outputObjectIds[0]
                    : "0";
            }

            return dto;
        }

        public ObjectGroup ToObjectGroup()
        {
            var g = new ObjectGroup(name ?? "")
            {
                StashObjectId = ParseId(stashObjectId),
                SourceDigest  = sourceDigest ?? "",
                AutoUpdate    = autoUpdate,
                Goal          = goal ?? "",
            };

            if (preconditions   != null) g.Preconditions   = new List<string>(preconditions);
            if (successCriteria != null) g.SuccessCriteria = new List<string>(successCriteria);
            if (tags            != null) g.Tags            = new List<string>(tags);

            g.Provenance = new ObjectGroupProvenance
            {
                ParentName    = provParentName    ?? "",
                ChangeSummary = provChangeSummary ?? "",
                CreatedBy     = provCreatedBy     ?? "",
            };

            if (!string.IsNullOrEmpty(createdAt) && DateTime.TryParse(
                    createdAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var t))
            {
                g.CreatedAt = t;
            }

            g.Steps.Clear();

            if (steps != null && steps.Count > 0)
            {
                foreach (var sd in steps)
                {
                    if (sd == null) continue;
                    var s = sd.ToStep();
                    if (s != null) g.Steps.Add(s);
                }
            }

            // steps が無い保存データ（ステップ導入前）は、古い 4 つをステップ 0 として読む。
            if (g.Steps.Count == 0)
            {
                var legacy = new ObjectGroupStepDTO
                {
                    action   = action ?? "",
                    args     = args     ?? new List<ObjectGroupArgDTO>(),
                    meshRefs = meshRefs ?? new List<ObjectGroupMeshRefDTO>(),
                };

                ulong outId = ParseId(outputObjectId);
                if (outId != 0UL)
                    legacy.outputObjectIds.Add(outId.ToString(CultureInfo.InvariantCulture));

                g.Steps.Add(legacy.ToStep());
            }

            // ステップ導入前・ElementId 導入前の保存データには ID が無い。
            g.EnsureElementIds();
            return g;
        }

        /// <summary>
        /// ObjectId の文字列を戻す。読めなければ 0（＝参照なし）。
        /// 0 を返すのは黙って別のオブジェクトを指すより安全なため。
        /// </summary>
        internal static ulong ParseId(string s)
            => (!string.IsNullOrEmpty(s) &&
                ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong v))
                ? v : 0UL;
    }
}
