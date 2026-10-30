// ObjectGroupOps.cs
// ObjectGroup の記録・判定・再構築。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【役割】
//   Capture     … 実行した生成コマンドと出来た出力先から、グループを 1 件作る
//   CaptureStep … 同じものを項目 1 つとして作る（グループへは足さない）
//   AppendStep  … 既にあるグループの末尾へ項目を足す
//   IsStale     … ソースが前回ビルド時から変わったか
//   BuildCommand… グループの項目から今のモデルに合った生成コマンドを組み直す
//   PurgeMissing… 参照先が消えたグループを片づける（明示操作からのみ呼ぶ）
//
// 【項目単位で働く】
//   索引の引き直し・取り込みの掛け直しはコマンド 1 つぶんの仕事なので、
//   中身は項目を受け取る形にしてある。グループを受け取る旧来の口は
//   項目 0 を見る包みで、呼び出し側はそのままでよい。
//
// 【索引を保存しない】
//   Args に入る「索引で描画オブジェクトを指す値」は保存した時点で古くなる。
//   どのキーがそれに当たるかは PLParam(IsMeshRef) が持ち、
//   PanelCommandFactory.MeshRefKeys が読む。コマンド種別ごとの手書き表は無い。
//   グループは索引の代わりに ObjectId を控え、組み直すときに引き直す。
//
// 【ObjectId は別モデルも引ける】
//   ObjectIdAllocator はプロセス内で単調増加するので、ID は
//   プロジェクト全体で一意。ブレンドのソースのように別モデルを指す参照も
//   同じ経路で解決できる（PLParam.MeshRefModelKey）。
//
// 【毎フレーム呼ばないこと】
//   IsStale はソースの全頂点を走査してダイジェストを取り直す。
//   呼んでよいのはパネルを開いたときと再構築を要求したときだけ。
//   編集のたびに印を立てるためのフックは置かない
//   （NotifyTopologyChanged は 133 箇所から呼ばれており choke point にならない）。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Ops
{
    /// <summary>ObjectGroup の記録・判定・再構築。</summary>
    public static class ObjectGroupOps
    {
        /// <summary>
        /// 直前の BuildCommand で梯子をどう扱ったかの説明。
        /// 取り直せたか、控えた点列を使ったかを人へ見せるために持つ。
        /// </summary>
        public static string LastBeltMessage { get; private set; }

        /// <summary>直前の BuildCommand でプロファイルをどう扱ったかの説明。</summary>
        public static string LastProfileMessage { get; private set; }

        // ================================================================
        // 参照の解決
        // ================================================================

        /// <summary>
        /// ObjectId からプロジェクト内の位置を引く。
        /// 見つからなければ false（modelIndex / masterIndex は -1）。
        /// </summary>
        public static bool TryResolve(
            ProjectContext project, ulong objectId, out int modelIndex, out int masterIndex)
        {
            modelIndex  = -1;
            masterIndex = -1;
            if (project == null || objectId == 0UL) return false;

            for (int m = 0; m < project.ModelCount; m++)
            {
                var model = project.GetModel(m);
                if (model?.MeshContextList == null) continue;

                for (int i = 0; i < model.MeshContextList.Count; i++)
                {
                    var mc = model.MeshContextList[i];
                    if (mc != null && mc.ObjectId == objectId)
                    {
                        modelIndex  = m;
                        masterIndex = i;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>ObjectId から MeshContext を引く。見つからなければ null。</summary>
        public static MeshContext Resolve(ProjectContext project, ulong objectId)
        {
            if (!TryResolve(project, objectId, out int m, out int i)) return null;
            return project.GetModel(m)?.GetMeshContext(i);
        }

        // ================================================================
        // 記録
        // ================================================================

        /// <summary>
        /// 実行した生成コマンドからグループを作る。
        ///
        /// Args は PanelCommandFactory.ToArgs で取る（新しい直列化機構は作らない）。
        /// IsMeshRef が付いたキーは、そのときの索引を ObjectId へ置き換えて控える。
        /// 引けなかった索引は 0（＝参照なし）で埋める。
        ///
        /// outputObjectId が 0 のときは「出力先未確定」のグループになる。
        /// 呼び出し側が出力先を作った直後に SetOutput で埋めること。
        /// </summary>
        public static ObjectGroup Capture(
            ProjectContext project, int modelIndex, PanelCommand cmd,
            ulong outputObjectId, string name)
            => Capture(
                project, modelIndex, cmd,
                outputObjectId == 0UL ? null : new List<ulong> { outputObjectId },
                name);

        /// <summary>
        /// 実行した生成コマンドから、出力先が複数のグループを作る。
        /// 1 回の実行で何本もできるもの（はしごから作る鎖など）はこちらを使う。
        /// outputObjectIds が空・null のときは「出力先未確定」のグループになる。
        /// </summary>
        public static ObjectGroup Capture(
            ProjectContext project, int modelIndex, PanelCommand cmd,
            IReadOnlyList<ulong> outputObjectIds, string name)
        {
            if (cmd == null) return null;

            var step = CaptureStep(project, modelIndex, cmd, outputObjectIds);
            if (step == null) return null;

            var g = new ObjectGroup(name ?? "");
            g.Steps.Clear();
            g.AddStep(step);

            g.SourceDigest = ComputeSourceDigest(project, g);
            return g;
        }

        /// <summary>
        /// 実行した生成コマンドから項目を 1 つ作る。グループへは足さない。
        ///
        /// Args は PanelCommandFactory.ToArgs で取る（新しい直列化機構は作らない）。
        /// IsMeshRef が付いたキーは、そのときの索引を ObjectId へ置き換えて控える。
        /// 引けなかった索引は 0（＝参照なし）で埋める。
        ///
        /// outputObjectIds は 0 を除いて控える。空でもよい（出力先未確定）。
        /// </summary>
        public static ObjectGroupStep CaptureStep(
            ProjectContext project, int modelIndex, PanelCommand cmd,
            IReadOnlyList<ulong> outputObjectIds)
        {
            if (cmd == null) return null;

            Type t = cmd.GetType();

            var step = new ObjectGroupStep
            {
                Action = PanelCommandFactory.ActionOf(t),
                Args   = PanelCommandFactory.ToArgs(cmd),
            };

            if (outputObjectIds != null)
            {
                for (int i = 0; i < outputObjectIds.Count; i++)
                    if (outputObjectIds[i] != 0UL)
                        step.OutputObjectIds.Add(outputObjectIds[i]);
            }

            var model = project?.GetModel(modelIndex);

            foreach (var mr in PanelCommandFactory.MeshRefKeys(t))
            {
                var ids = new List<ulong>();

                int[] indices  = ParseIntCsv(step.GetArg(mr.Key));
                int[] modelIdx = mr.HasModelKey ? ParseIntCsv(step.GetArg(mr.ModelKey)) : null;

                for (int k = 0; k < indices.Length; k++)
                {
                    ModelContext owner = model;
                    // モデル索引は、索引と同じ並びの配列か、全部に効く 1 個（TransferVertexDataCommand.TargetModelIndex）。
                    if (modelIdx != null && modelIdx.Length == 1)
                        owner = project?.GetModel(modelIdx[0]);
                    else if (modelIdx != null && k < modelIdx.Length)
                        owner = project?.GetModel(modelIdx[k]);

                    var mc = (owner != null && indices[k] >= 0)
                        ? owner.GetMeshContext(indices[k])
                        : null;

                    ids.Add(mc?.ObjectId ?? 0UL);
                }

                step.SetMeshRefIds(mr.Key, ids);
            }

            return step;
        }

        /// <summary>
        /// 実行した生成コマンドを、既にあるグループの末尾へ項目として足す。
        /// ダイジェストは足したあとに取り直す（ソースの集合が変わりうるため）。
        /// </summary>
        public static ObjectGroupStep AppendStep(
            ProjectContext project, int modelIndex, ObjectGroup group,
            PanelCommand cmd, IReadOnlyList<ulong> outputObjectIds)
        {
            if (group == null || cmd == null) return null;

            var step = CaptureStep(project, modelIndex, cmd, outputObjectIds);
            if (step == null) return null;

            group.AddStep(step);
            group.SourceDigest = ComputeSourceDigest(project, group);
            return step;
        }

        /// <summary>項目 0 の出力先を差し替える。ダイジェストは触らない。</summary>
        public static void SetOutput(ObjectGroup group, ulong outputObjectId)
        {
            if (group == null) return;
            group.OutputObjectId = outputObjectId;
        }

        /// <summary>
        /// 項目の出力先を丸ごと差し替える。ダイジェストは触らない。
        /// 0 は控えない（＝出力先なし）。
        /// </summary>
        public static void SetOutputs(
            ObjectGroup group, int stepIndex, IReadOnlyList<ulong> outputObjectIds)
        {
            var step = group?.GetStep(stepIndex);
            if (step == null) return;

            if (step.OutputObjectIds == null) step.OutputObjectIds = new List<ulong>();
            step.OutputObjectIds.Clear();

            if (outputObjectIds == null) return;
            for (int i = 0; i < outputObjectIds.Count; i++)
                if (outputObjectIds[i] != 0UL)
                    step.OutputObjectIds.Add(outputObjectIds[i]);
        }

        // ================================================================
        // 更新の判定
        // ================================================================

        /// <summary>
        /// ソースが前回ビルド時から変わったか。
        /// ダイジェストが空のグループ（古い保存データ）は変わっていない扱いにする。
        ///
        /// 【毎フレーム呼ばないこと】ソースの全頂点を走査する。
        /// </summary>
        public static bool IsStale(ProjectContext project, ObjectGroup group)
        {
            if (group == null) return false;
            if (string.IsNullOrEmpty(group.SourceDigest)) return false;
            return !string.Equals(
                group.SourceDigest, ComputeSourceDigest(project, group), StringComparison.Ordinal);
        }

        /// <summary>
        /// ソースの現在の中身からダイジェストを作る。
        ///
        /// 位置は 1e-5 単位へ丸めてから混ぜる。丸めないと、同じ操作でも
        /// 浮動小数の下位ビットが揺れて「変わった」と判定されうる。
        /// 頂点ID も混ぜるので、位置が同じまま頂点が入れ替わった場合も検出できる。
        /// </summary>
        public static string ComputeSourceDigest(ProjectContext project, ObjectGroup group)
        {
            if (group == null) return "";

            var ids = group.SourceObjectIds;   // 昇順・重複なし
            if (ids.Count == 0) return "";

            // FNV-1a 64bit。暗号用途ではなく「変わったか」を見るだけ。
            const ulong Offset = 14695981039346656037UL;
            const ulong Prime  = 1099511628211UL;
            ulong h = Offset;

            void Mix(long v)
            {
                for (int b = 0; b < 8; b++)
                {
                    h ^= (byte)(v >> (b * 8));
                    h *= Prime;
                }
            }

            foreach (ulong id in ids)
            {
                Mix(unchecked((long)id));

                var mc = Resolve(project, id);
                var mo = mc?.MeshObject;
                if (mo == null) { Mix(-1); continue; }

                Mix(mo.VertexCount);
                Mix(mo.FaceCount);

                for (int i = 0; i < mo.VertexCount; i++)
                {
                    var v = mo.Vertices[i];
                    if (v == null) { Mix(-2); continue; }

                    Mix(v.Id);
                    var p = v.Position;
                    Mix((long)Mathf.RoundToInt(p.x * 100000f));
                    Mix((long)Mathf.RoundToInt(p.y * 100000f));
                    Mix((long)Mathf.RoundToInt(p.z * 100000f));
                }
            }

            return h.ToString("x16", CultureInfo.InvariantCulture);
        }

        // ================================================================
        // 再構築
        // ================================================================

        /// <summary>
        /// グループから今のモデルに合った生成コマンドを組み直す。
        /// 作れないときは null を返し、error に理由を入れる。
        ///
        /// やること
        ///   1. Args を複製する（グループが持つ値は書き換えない）
        ///   2. IsMeshRef のキーを ObjectId から今の索引へ引き直す
        ///   3. 記録した取り込み方をソースへ掛け直し、梯子とプロファイルを差し替える
        ///   4. PanelCommandFactory.Create に渡す
        ///
        /// 2 と 3 の順は入れ替えてはならない。梯子の取り込み元
        /// （BeltSourceIndex）も IsMeshRef で、保存往復のあとは古い索引が
        /// 入っている。先に引き直しておかないと別のオブジェクトから取り込む。
        /// </summary>
        public static PanelCommand BuildCommand(
            ProjectContext project, int modelIndex, ObjectGroup group, out string error)
            => BuildCommand(project, modelIndex, group, 0, out error);

        /// <summary>
        /// グループの stepIndex 番目の項目から生成コマンドを組み直す。
        /// やることは 1 項目版の BuildCommand と同じ。
        /// </summary>
        public static PanelCommand BuildCommand(
            ProjectContext project, int modelIndex, ObjectGroup group, int stepIndex,
            out string error)
        {
            error = null;

            if (group == null)          { error = "グループがありません"; return null; }
            if (!group.IsValid)         { error = "グループに生成コマンドが記録されていません"; return null; }

            var step = group.GetStep(stepIndex);
            if (step == null) { error = $"項目 {stepIndex} がありません"; return null; }

            // 実行しない項目（説明・指示・確認）はコマンドにならない。
            // 飛ばすかどうかは呼ぶ側が決める（RunObjectGroupStep は飛ばす）。
            if (!step.IsExecutable)
            { error = $"項目 {stepIndex} は {step.Kind} で、実行する項目ではありません"; return null; }

            Type t = PanelCommandFactory.ResolveType(step.Action);
            if (t == null) { error = $"未対応の action: {step.Action}"; return null; }

            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in step.Args) args[kv.Key] = kv.Value;

            ClearKeepAsGroup(args);

            if (!RewriteMeshRefs(project, modelIndex, t, step, args, out error)) return null;

            RefreshInputs(project, modelIndex, t, args, out string beltMessage, out string profileMessage);
            LastBeltMessage    = beltMessage;
            LastProfileMessage = profileMessage;

            var cmd = PanelCommandFactory.Create(step.Action, modelIndex, args, out error);
            return cmd;
        }

        /// <summary>
        /// 梯子・断面を持つコマンドの引数で、記録した取り込み方を取り込み元へ掛け直し、点列を差し替える。
        /// グループの作り直し（BuildCommand）とシナリオの項目（PlayerCommandDispatcher.TryExecuteRunStep）が
        /// 同じ読み方をするための口（作り直し整備計画 段 11）。取り込み元の索引は今の索引にしてから渡すこと。
        /// 取り込み方が Baked のもの・取り込み元が -1 のものは何もしない（控えた点列のまま）。
        /// </summary>
        public static void RefreshInputs(
            ProjectContext project, int modelIndex, Type t, Dictionary<string, string> args,
            out string beltMessage, out string profileMessage)
        {
            beltMessage    = null;
            profileMessage = null;
            if (t == null || args == null) return;
            beltMessage    = RefreshBelts(project, modelIndex, t, args);
            profileMessage = RefreshProfile(project, modelIndex, t, args);
        }

        // ================================================================
        // グループ → シナリオ（PolyLing_作り直し整備計画.md 段 11）
        // ================================================================

        /// <summary>
        /// グループの項目を、モデルに縛られないシナリオの項目へ写す。
        ///
        /// 【つながりの書き換え】MeshRef のキーごとに
        ///   ・同じグループの前の項目の出力（1 個）を指す → @&lt;その項目 ID&gt;.masterIndices
        ///   ・グループの外を指す → シナリオの引数 &lt;項目 ID&gt;_&lt;キー&gt;（既定値は今の索引）を ${名前} で参照
        ///   ・0（参照なし）だけ → 今の値（-1）のまま
        ///   内と外が混じる、出力が複数の項目を指す、モデル索引付きの参照が内を指す、のどれかは断る。
        /// MeshRefIds・OutputObjectIds は空にする（シナリオは実体に縛らない）。
        /// </summary>
        /// <param name="modelIndex">グループが属するモデルの索引（外の参照の既定値を引く）。</param>
        public static bool BuildScenarioSteps(
            ProjectContext project, int modelIndex, ObjectGroup group,
            out List<ObjectGroupStep> steps, out List<ScenarioParam> parameters, out string error)
        {
            steps      = new List<ObjectGroupStep>();
            parameters = new List<ScenarioParam>();
            error      = null;
            if (group == null) { error = "グループがありません"; return false; }

            // まとめたグループ（mergeObjectGroup は項目を Clone で写す）では項目 ID が重なりうる。
            var work = group.Clone();
            work.EnsureScenarioItemIds();

            // 出力の ObjectId → それを作った項目（前の項目だけを内として扱う）
            var producer = new Dictionary<ulong, ObjectGroupStep>();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            for (int s = 0; s < work.StepCount; s++)
            {
                var src = work.Steps[s];
                if (src == null) continue;

                var dst = src.Clone();
                dst.MeshRefIds.Clear();
                dst.OutputObjectIds.Clear();

                if (src.IsExecutable)
                {
                    Type t = PanelCommandFactory.ResolveType(src.Action);
                    if (t == null) { error = $"項目 {src.ScenarioItemId}: 未対応の action {src.Action}"; return false; }

                    // 今の索引（外の参照の既定値）。引けない参照があればここで断る。
                    var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var kv in src.Args) resolved[kv.Key] = kv.Value;
                    if (!RewriteMeshRefs(project, modelIndex, t, src, resolved, out string rwError))
                    { error = $"項目 {src.ScenarioItemId}: {rwError}"; return false; }

                    foreach (var mr in PanelCommandFactory.MeshRefKeys(t))
                    {
                        var ids = src.GetMeshRefIds(mr.Key);
                        if (ids.Count == 0) continue;

                        int nonZero = 0, inner = 0;
                        ObjectGroupStep from = null;
                        bool sameProducer = true;
                        foreach (ulong id in ids)
                        {
                            if (id == 0UL) continue;
                            nonZero++;
                            if (producer.TryGetValue(id, out var p))
                            {
                                inner++;
                                if (from == null) from = p;
                                else if (!ReferenceEquals(from, p)) sameProducer = false;
                            }
                        }

                        if (nonZero == 0)
                        {
                            if (resolved.TryGetValue(mr.Key, out string keep)) dst.SetArg(mr.Key, keep);
                            continue;
                        }

                        if (inner == nonZero)
                        {
                            if (!sameProducer || ids.Count != 1 || from.OutputObjectIds.Count != 1)
                            { error = $"項目 {src.ScenarioItemId} の {mr.Key}: 前の項目の出力を複数指しているので @ 参照にできません"; return false; }
                            if (mr.HasModelKey)
                            { error = $"項目 {src.ScenarioItemId} の {mr.Key}: モデル索引付きの参照は @ 参照にできません"; return false; }
                            dst.SetArg(mr.Key, "@" + from.ScenarioItemId + ".masterIndices");
                            continue;
                        }

                        if (inner > 0)
                        { error = $"項目 {src.ScenarioItemId} の {mr.Key}: グループの内と外の参照が混じっているので書き換えられません"; return false; }

                        if (!AddScenarioParam(src, mr.Key, resolved, project, ids, usedNames, parameters, dst, out error)) return false;
                        if (mr.HasModelKey && resolved.ContainsKey(mr.ModelKey)
                            && !AddScenarioParam(src, mr.ModelKey, resolved, project, null, usedNames, parameters, dst, out error)) return false;
                    }

                    foreach (ulong id in src.OutputObjectIds)
                        if (id != 0UL && !producer.ContainsKey(id)) producer[id] = src;
                }

                steps.Add(dst);
            }
            return true;
        }

        /// <summary>外を指す参照 1 キーをシナリオの引数にし、項目の値を ${名前} にする。</summary>
        private static bool AddScenarioParam(
            ObjectGroupStep src, string key, Dictionary<string, string> resolved, ProjectContext project,
            List<ulong> ids, HashSet<string> usedNames, List<ScenarioParam> parameters, ObjectGroupStep dst,
            out string error)
        {
            error = null;
            string name = (src.ScenarioItemId ?? "") + "_" + key.Replace('.', '_');
            if (!ScenarioParam.IsValidName(name))
            { error = $"項目 {src.ScenarioItemId} の {key}: 引数名 {name} が作れません"; return false; }
            if (!usedNames.Add(name))
            { error = $"項目 {src.ScenarioItemId} の {key}: 引数名 {name} が重なります"; return false; }

            string what = "";
            if (ids != null)
            {
                var names = new List<string>();
                foreach (ulong id in ids)
                    if (id != 0UL) names.Add(Resolve(project, id)?.Name ?? id.ToString(CultureInfo.InvariantCulture));
                what = string.Join(",", names);
            }

            parameters.Add(new ScenarioParam
            {
                Name        = name,
                Default     = resolved.TryGetValue(key, out string v) ? v ?? "" : "",
                Description = $"項目 {src.ScenarioItemId}（{src.Action}）の {key}"
                            + (what.Length > 0 ? $"。元のグループでは {what}" : "")
                            + "（索引）",
            });
            dst.SetArg(key, ScenarioParam.MakeRef(name));
            return true;
        }

        // ================================================================
        // 項目の引数の書き換え（PolyLing_作り直し整備計画.md 段 3）
        // ================================================================

        /// <summary>
        /// 項目の引数のうち、外から書き換えてはいけないキーなら理由を返す。書けるなら null。
        ///
        /// 書けないもの
        ///   ・入力と出力先（IsMeshRef のキーと、その相棒のモデル索引）
        ///   ・作り直しのたびに上書きされるもの（RebuildRole の切り替えキー、keepAsGroup）
        ///   ・入力の中身と取り方（梯子の点列・先端、プロファイル、取り込み方）
        /// 入力の差し替えは段 3 の範囲外なので、ここで止める。
        /// </summary>
        public static string ArgWriteBlockReason(Type t, string key)
            => ArgWriteBlockReason(t, key, allowProfile: false);

        /// <summary>
        /// allowProfile が true なら、断面（プロファイル）のキーは止めない。
        /// 断面の作業空間の反映（断面の線分 → 断面の逆方向。段 6）だけが使う。
        /// </summary>
        public static string ArgWriteBlockReason(Type t, string key, bool allowProfile)
        {
            if (t == null) return "コマンドの型が引けません";
            if (string.IsNullOrEmpty(key)) return "キーが空です";

            foreach (var mr in PanelCommandFactory.MeshRefKeys(t))
            {
                if (string.Equals(mr.Key, key, StringComparison.Ordinal))
                    return "入力・出力先の参照（MeshRef）は書けません";
                if (mr.HasModelKey && string.Equals(mr.ModelKey, key, StringComparison.Ordinal))
                    return "参照のモデル索引は書けません";
            }

            var rb = PanelCommandFactory.RebuildKeys(t);
            if (rb.IsSupported && string.Equals(rb.TargetIndexKey, key, StringComparison.Ordinal))
                return "出力先の索引は書けません";
            if (rb.HasMode && string.Equals(rb.ModeKey, key, StringComparison.Ordinal))
                return "出力先への書き戻し方は作り直しが決めるので書けません";

            int dot = key.LastIndexOf('.');
            string leaf = dot >= 0 ? key.Substring(dot + 1) : key;
            if (string.Equals(leaf, "keepAsGroup", StringComparison.Ordinal))
                return "グループとして残すかは作り直しが決めるので書けません";

            if (!allowProfile)
            foreach (var pk in PanelCommandFactory.ProfileKeys(t))
            {
                if (string.Equals(pk.Key, key, StringComparison.Ordinal)
                 || string.Equals(pk.LoopStartsKey, key, StringComparison.Ordinal)
                 || string.Equals(pk.LoopIsHoleKey, key, StringComparison.Ordinal))
                    return "断面（入力の中身）は書けません";
            }

            string[] inputProps =
            {
                nameof(CreatePrimitiveMeshCommand.ProfileAcquire),
                nameof(CreateBeltPrimitiveCommand.AcquireMethod),
                nameof(CreateBeltPrimitiveCommand.AcquireCrossRows),
                nameof(CreateBeltPrimitiveCommand.AcquireSetName),
                nameof(CreateBeltPrimitiveCommand.BeltLeftPoints),
                nameof(CreateBeltPrimitiveCommand.BeltRightPoints),
                nameof(CreateBeltPrimitiveCommand.BeltStarts),
                nameof(CreateBeltPrimitiveCommand.BeltClosed),
                nameof(CreateBeltPrimitiveCommand.BeltFlipWinding),
                nameof(CreateBeltPrimitiveCommand.BeltHeightScale),
                nameof(CreateBeltPrimitiveCommand.BeltStartPoints),
                nameof(CreateBeltPrimitiveCommand.BeltHasStartPoint),
                nameof(CreateBeltPrimitiveCommand.BeltEndPoints),
                nameof(CreateBeltPrimitiveCommand.BeltHasEndPoint),
            };
            foreach (string p in inputProps)
            {
                string k = PanelCommandFactory.KeyOfProperty(t, p);
                if (!string.IsNullOrEmpty(k) && string.Equals(k, key, StringComparison.Ordinal))
                    return "入力の中身・取り込み方は書けません";
            }

            return null;
        }

        /// <summary>
        /// 項目が受け付ける引数のキー。組み直したコマンドの ToArgs のキーと、今の Args のキーを合わせたもの。
        /// 組み直せないとき（入力が引けない等）は今の Args のキーだけになる。
        /// </summary>
        public static HashSet<string> StepArgKeys(ProjectContext project, int modelIndex, ObjectGroup group, int stepIndex)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var step = group?.GetStep(stepIndex);
            if (step?.Args == null) return keys;
            foreach (var kv in step.Args) keys.Add(kv.Key);

            var cmd = BuildCommand(project, modelIndex, group, stepIndex, out _);
            if (cmd != null)
                foreach (var kv in PanelCommandFactory.ToArgs(cmd)) keys.Add(kv.Key);
            return keys;
        }

        /// <summary>
        /// 項目の引数へ書く前の検査。書いた結果がコマンドの型で読み直せる（Create が通る）ことまで確かめる。
        /// グループには何も書かない（写しで試す）。通らなければ false と理由。
        /// </summary>
        public static bool TryValidateStepArgs(
            ProjectContext project, int modelIndex, ObjectGroup group, int stepIndex,
            IReadOnlyList<string> keys, IReadOnlyList<string> values, out string error)
            => TryValidateStepArgs(project, modelIndex, group, stepIndex, keys, values, false, out error);

        /// <summary>allowProfile は ArgWriteBlockReason と同じ（断面の作業空間の反映だけが true）。</summary>
        public static bool TryValidateStepArgs(
            ProjectContext project, int modelIndex, ObjectGroup group, int stepIndex,
            IReadOnlyList<string> keys, IReadOnlyList<string> values, bool allowProfile, out string error)
        {
            error = null;
            if (group == null) { error = "グループがありません"; return false; }

            var step = group.GetStep(stepIndex);
            if (step == null) { error = $"項目 {stepIndex} がありません（項目数 {group.StepCount}）"; return false; }
            if (!step.IsExecutable) { error = $"項目 {stepIndex} は {step.Kind} で、引数を持つ実行項目ではありません"; return false; }

            Type t = PanelCommandFactory.ResolveType(step.Action);
            if (t == null) { error = $"未対応の action: {step.Action}"; return false; }

            if (keys == null || values == null || keys.Count == 0)
            { error = "書くキーがありません"; return false; }
            if (keys.Count != values.Count)
            { error = $"キー {keys.Count} 個と値 {values.Count} 個の数が違います"; return false; }

            var known = StepArgKeys(project, modelIndex, group, stepIndex);
            var seen  = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];
                if (string.IsNullOrEmpty(k)) { error = $"{i} 番目のキーが空です"; return false; }
                if (!seen.Add(k)) { error = $"キー {k} が重複しています"; return false; }
                if (!known.Contains(k)) { error = $"{step.Action} に引数 {k} はありません"; return false; }
                string why = ArgWriteBlockReason(t, k, allowProfile);
                if (why != null) { error = $"{k}: {why}"; return false; }
            }

            var trial = group.Clone();
            var ts = trial.GetStep(stepIndex);
            for (int i = 0; i < keys.Count; i++) ts.SetArg(keys[i], values[i]);

            var cmd = BuildCommand(project, modelIndex, trial, stepIndex, out string bErr);
            if (cmd == null) { error = $"値をコマンドの型で読み直せません: {bErr}"; return false; }
            return true;
        }

        /// <summary>
        /// 「グループとして残す」を必ず false にする。
        ///
        /// この印は「作るときにグループを控えるか」という操作の意図で、
        /// 生成そのもののパラメータではない。控えたまま作り直すと、
        /// 作り直しのたびに同じ内容のグループがもう 1 件増える。
        ///
        /// キーはコマンドによって "keepAsGroup"（ブレンド）と
        /// "placement.keepAsGroup"（図形生成）になる。最後のドット区切りで判定する。
        /// </summary>
        private static void ClearKeepAsGroup(Dictionary<string, string> args)
        {
            const string Leaf = "keepAsGroup";

            var hits = new List<string>();
            foreach (var kv in args)
            {
                string k = kv.Key;
                if (string.IsNullOrEmpty(k)) continue;
                int dot = k.LastIndexOf('.');
                string leaf = dot >= 0 ? k.Substring(dot + 1) : k;
                if (string.Equals(leaf, Leaf, StringComparison.Ordinal)) hits.Add(k);
            }
            foreach (string k in hits) args[k] = "false";
        }

        /// <summary>
        /// IsMeshRef のキーを、控えてある ObjectId から今の索引へ書き直す。
        ///
        /// 引けなかった ID があるときは false を返し、再構築を止める。
        /// 黙って -1 を入れて進めると、藤壺なら「配置元 0 個」で空のメッシュが
        /// 出来上がり、ブレンドなら別のオブジェクトを宛先にしてしまう。
        /// </summary>
        private static bool RewriteMeshRefs(
            ProjectContext project, int modelIndex, Type t, ObjectGroupStep step,
            Dictionary<string, string> args, out string error)
        {
            error = null;

            foreach (var mr in PanelCommandFactory.MeshRefKeys(t))
            {
                var ids = step.GetMeshRefIds(mr.Key);
                if (ids.Count == 0) continue;   // 控えが無いキーは触らない

                var indices  = new List<int>(ids.Count);
                var modelIds = mr.HasModelKey ? new List<int>(ids.Count) : null;

                for (int k = 0; k < ids.Count; k++)
                {
                    if (ids[k] == 0UL)
                    {
                        // 記録した時点でも参照が無かった。-1 のまま戻す。
                        indices.Add(-1);
                        modelIds?.Add(modelIndex);
                        continue;
                    }

                    if (!TryResolve(project, ids[k], out int mi, out int xi))
                    {
                        error = $"{mr.Key}[{k}]: 参照先のオブジェクトが見つかりません (ObjectId={ids[k]})";
                        return false;
                    }

                    if (modelIds == null && mi != modelIndex)
                    {
                        error = $"{mr.Key}[{k}]: 参照先が別のモデルにあります";
                        return false;
                    }

                    indices.Add(xi);
                    modelIds?.Add(mi);
                }

                args[mr.Key] = JoinInts(indices);
                if (modelIds != null && !string.IsNullOrEmpty(mr.ModelKey))
                {
                    // モデル索引が 1 個の引数（TransferVertexDataCommand.TargetModelIndex）には 1 個で書き戻す。
                    string orig = step.GetArg(mr.ModelKey) ?? "";
                    bool scalar = orig.IndexOf(',') < 0 && indices.Count > 1;
                    if (scalar)
                    {
                        for (int k = 1; k < modelIds.Count; k++)
                            if (modelIds[k] != modelIds[0])
                            { error = $"{mr.Key}: 参照先が複数のモデルにまたがり、{mr.ModelKey} 1 個では表せません"; return false; }
                        args[mr.ModelKey] = modelIds.Count > 0 ? modelIds[0].ToString(System.Globalization.CultureInfo.InvariantCulture) : orig;
                    }
                    else
                        args[mr.ModelKey] = JoinInts(modelIds);
                }
            }

            return true;
        }

        /// <summary>
        /// 梯子を持つコマンドで、記録した取り込み方をソースへ掛け直し、点列を差し替える。
        ///
        /// 取り直せないとき（取り込み方の記録が無い・ソースが無い・辞書が無い・
        /// 検出 0 本）は何もしない。そのときは控えた点列がそのまま使われ、
        /// ソースの編集には追随しない。黙って別の面を読むより、
        /// 古いままの方が原因を追える。
        ///
        /// 【頂点ID／頂点番号を使わない理由】
        ///   頂点IDは人が手で管理するもので、図形生成で作ったメッシュには付いていない。
        ///   頂点番号は編集で動く。どちらを控えても、手で作る人に番号の管理を強いる。
        ///   取り込みは検出器と選択辞書で再現できるので、手順の方を控える。
        /// </summary>
        /// <returns>人へ出す説明。差し替えなかったときも理由を返す。</returns>
        private static string RefreshBelts(
            ProjectContext project, int modelIndex, Type t, Dictionary<string, string> args)
        {
            if (!typeof(CreateBeltPrimitiveCommand).IsAssignableFrom(t)) return null;

            string kMethod = PanelCommandFactory.KeyOfProperty(t, nameof(CreateBeltPrimitiveCommand.AcquireMethod));
            string kCross  = PanelCommandFactory.KeyOfProperty(t, nameof(CreateBeltPrimitiveCommand.AcquireCrossRows));
            string kSet    = PanelCommandFactory.KeyOfProperty(t, nameof(CreateBeltPrimitiveCommand.AcquireSetName));
            string kSrc    = PanelCommandFactory.KeyOfProperty(t, nameof(CreateBeltPrimitiveCommand.BeltSourceIndex));
            if (string.IsNullOrEmpty(kMethod)) return null;

            var method = (BeltAcquireMethod)ParseInt(args.TryGetValue(kMethod, out var m) ? m : null, 0);
            if (method == BeltAcquireMethod.Baked)
                return "取り込み方の記録が無いので、控えた点列をそのまま使う";

            // BeltSourceIndex は IsMeshRef なので、ここへ来る前に RewriteMeshRefs が
            // ObjectId から今の索引へ直している（BuildCommand の呼び順を参照）。
            int srcIndex = ParseInt(args.TryGetValue(kSrc, out var si) ? si : null, -1);
            if (srcIndex < 0)
                return "取り込み元が記録されていないので、控えた点列をそのまま使う";

            var source = project?.GetModel(modelIndex)?.GetMeshContext(srcIndex);
            if (source?.MeshObject == null)
                return "取り込み元が見つからないので、控えた点列をそのまま使う";

            bool   cross   = string.Equals(args.TryGetValue(kCross, out var cr) ? cr : "false",
                                           "true", StringComparison.OrdinalIgnoreCase);
            string setName = args.TryGetValue(kSet, out var sn) ? sn : "";

            var acquired = BeltAcquire.Acquire(source, method, cross, setName);
            if (!acquired.Ok)
                return $"取り直せないので控えた点列を使う（{acquired.Message}）";

            WriteBeltArgs(t, args, acquired.Belts.ToArray());

            return $"梯子を {acquired.Belts.Count} 本取り直した（{method} / "
                 + $"上下展開={(cross ? "する" : "しない")} / {acquired.Message}）";
        }

        /// <summary>
        /// 梯子の点列を引数へ書く（取り直し RefreshBelts と、逆方向で書き直した梯子の両方が使う）。
        /// 先端は、元の生成が先端を使っていたとき（有無の列が引数にあるとき）だけ書く。
        ///   辺のパイプ化のように、検出の起点として足しただけの開始タグを持つ梯子は
        ///   先端を渡さずに作っている。勝手に先端へすぼめると形が変わる。
        /// </summary>
        public static void WriteBeltArgs(Type t, IDictionary<string, string> args, BeltCsvEntry[] belts)
        {
            CreateBeltPrimitiveCommand.SplitBelts(
                belts,
                out var left, out var right, out var starts,
                out var closed, out var flip, out var height,
                out var startPts, out var hasStart, out var endPts, out var hasEnd);

            void Put(string prop, string value)
            {
                string key = PanelCommandFactory.KeyOfProperty(t, prop);
                if (!string.IsNullOrEmpty(key)) args[key] = value;
            }

            Put(nameof(CreateBeltPrimitiveCommand.BeltLeftPoints),  JoinFloats(left));
            Put(nameof(CreateBeltPrimitiveCommand.BeltRightPoints), JoinFloats(right));
            Put(nameof(CreateBeltPrimitiveCommand.BeltStarts),      JoinInts(starts));
            Put(nameof(CreateBeltPrimitiveCommand.BeltClosed),      JoinBools(closed));
            Put(nameof(CreateBeltPrimitiveCommand.BeltFlipWinding), JoinBools(flip));
            Put(nameof(CreateBeltPrimitiveCommand.BeltHeightScale), JoinFloats(height));

            string hasStartKey = PanelCommandFactory.KeyOfProperty(t, nameof(CreateBeltPrimitiveCommand.BeltHasStartPoint));
            string hasEndKey   = PanelCommandFactory.KeyOfProperty(t, nameof(CreateBeltPrimitiveCommand.BeltHasEndPoint));
            bool usedStart = !string.IsNullOrEmpty(hasStartKey)
                          && args.TryGetValue(hasStartKey, out var hs) && !string.IsNullOrEmpty(hs);
            bool usedEnd   = !string.IsNullOrEmpty(hasEndKey)
                          && args.TryGetValue(hasEndKey, out var he) && !string.IsNullOrEmpty(he);
            if (usedStart)
            {
                Put(nameof(CreateBeltPrimitiveCommand.BeltStartPoints),   JoinFloats(startPts));
                Put(nameof(CreateBeltPrimitiveCommand.BeltHasStartPoint), JoinBools(hasStart));
            }
            if (usedEnd)
            {
                Put(nameof(CreateBeltPrimitiveCommand.BeltEndPoints),     JoinFloats(endPts));
                Put(nameof(CreateBeltPrimitiveCommand.BeltHasEndPoint),   JoinBools(hasEnd));
            }
        }

        /// <summary>
        /// プロファイルを持つコマンドで、記録した取り込み方を掛け直して差し替える。
        ///
        /// どのキーがプロファイル本体かは PLParam(ProfileRole) が持ち、
        /// PanelCommandFactory.ProfileKeys が読む。コマンド種別ごとの手書き表は無い。
        ///
        /// 取り直せないとき（記録が無い・取り込み元が無い・線が無い）は何もしない。
        /// そのときは控えた値がそのまま使われ、取り込み元の編集には追随しない。
        /// </summary>
        /// <returns>人へ出す説明。差し替えなかったときも理由を返す。</returns>
        private static string RefreshProfile(
            ProjectContext project, int modelIndex, Type t, Dictionary<string, string> args)
        {
            var keys = PanelCommandFactory.ProfileKeys(t);
            if (keys.Count == 0) return null;

            string kMethod = PanelCommandFactory.KeyOfProperty(
                t, nameof(CreatePrimitiveMeshCommand.ProfileAcquire));
            string kSrc = PanelCommandFactory.KeyOfProperty(
                t, nameof(CreatePrimitiveMeshCommand.ProfileSourceIndex));
            if (string.IsNullOrEmpty(kMethod)) return null;

            var method = (ProfileAcquireMethod)ParseInt(args.TryGetValue(kMethod, out var m) ? m : null, 0);
            if (method == ProfileAcquireMethod.Baked)
                return "プロファイルの取り込み方の記録が無いので、控えた値をそのまま使う";

            // ProfileSourceIndex は IsMeshRef なので、ここへ来る前に RewriteMeshRefs が
            // ObjectId から今の索引へ直している（BuildCommand の呼び順を参照）。
            int srcIndex = ParseInt(args.TryGetValue(kSrc, out var si) ? si : null, -1);
            if (srcIndex < 0)
                return "プロファイルの取り込み元が記録されていないので、控えた値をそのまま使う";

            var source = project?.GetModel(modelIndex)?.GetMeshContext(srcIndex);
            if (source?.MeshObject == null)
                return "プロファイルの取り込み元が見つからないので、控えた値をそのまま使う";

            int replaced = 0;
            string message = "";

            foreach (var pk in keys)
            {
                var got = ProfileAcquire.Acquire(source, method, pk.NormalizeFor(args));
                if (!got.Ok) return $"プロファイルを取り直せないので控えた値を使う（{got.Message}）";

                message = got.Message;

                if (pk.Role == PLProfileRole.Points)
                {
                    args[pk.Key] = JoinVector3(got.Points);
                    replaced++;
                    continue;
                }

                // FlatLoops。点の連結・開始位置・穴フラグの 3 本を対で書く。
                if (string.IsNullOrEmpty(pk.LoopStartsKey))
                    return "ループの開始位置を持つキーが指定されていないので、控えた値を使う";

                var vals   = new List<float>();
                var starts = new List<int>();
                var holes  = new List<bool>();
                foreach (var lp in got.Loops)
                {
                    if (lp?.Points == null || lp.Points.Count < 3) continue;
                    starts.Add(vals.Count / 3);
                    holes.Add(lp.IsHole);
                    foreach (var q in lp.Points) { vals.Add(q.x); vals.Add(q.y); vals.Add(q.z); }
                }
                if (starts.Count == 0)
                    return "使えるループが無いので、控えた値を使う";

                args[pk.Key]           = JoinFloats(vals);
                args[pk.LoopStartsKey] = JoinInts(starts);
                if (!string.IsNullOrEmpty(pk.LoopIsHoleKey))
                    args[pk.LoopIsHoleKey] = JoinBools(holes);
                replaced++;
            }

            return replaced > 0
                ? $"プロファイルを取り直した（{method} / {message}）"
                : "プロファイルを差し替えなかった";
        }

        // ================================================================
        // 後始末
        // ================================================================

        /// <summary>
        /// 参照先が 1 つも引けなくなったグループを取り除く。
        ///
        /// 【RemoveAt から呼ばないこと】
        ///   描画オブジェクトの削除に巻き込んでここで消すと、その削除は
        ///   ObjectGroup の Undo レコードに載らないため Undo で戻せない。
        ///   参照切れは ModelInvariantChecker が報告し、片づけは
        ///   パネルからの明示操作かプロジェクト読み込み直後に行う。
        /// </summary>
        /// <returns>取り除いた件数</returns>
        public static int PurgeMissing(ProjectContext project, ModelContext model)
        {
            if (model?.ObjectGroups == null) return 0;

            int removed = 0;
            for (int i = model.ObjectGroups.Count - 1; i >= 0; i--)
            {
                var g = model.ObjectGroups[i];
                if (g == null) { model.ObjectGroups.RemoveAt(i); removed++; continue; }

                bool outputAlive = false;
                foreach (ulong id in g.OutputObjectIds)
                    if (Resolve(project, id) != null) { outputAlive = true; break; }

                bool stashAlive = g.HasStash && Resolve(project, g.StashObjectId) != null;

                bool anySourceAlive = false;
                foreach (ulong id in g.SourceObjectIds)
                    if (Resolve(project, id) != null) { anySourceAlive = true; break; }

                if (!outputAlive && !stashAlive && !anySourceAlive)
                {
                    model.ObjectGroups.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0) model.IsDirty = true;
            return removed;
        }

        // ================================================================
        // 文字列の対（PanelCommandFactory の TryParse / TryFormat と同じ並び）
        // ================================================================

        private static int[] ParseIntCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<int>();
            var parts = s.Split(',');
            var result = new List<int>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                    result.Add(v);
            }
            return result.ToArray();
        }

        private static float[] ParseFloatCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<float>();
            var parts = s.Split(',');
            var result = new List<float>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    result.Add(v);
            }
            return result.ToArray();
        }

        public static string JoinInts(IReadOnlyList<int> v)
        {
            if (v == null || v.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < v.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(v[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static int ParseInt(string s, int fallback)
            => (!string.IsNullOrEmpty(s) &&
                int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                ? v : fallback;

        /// <summary>
        /// Vector3 の列を平坦な CSV にする。
        /// PanelCommandFactory.TryFormat の Vector3[] と同じ並びにすること
        /// （読み側は TryParseVectorArray がこの形を前提にしている）。
        /// </summary>
        public static string JoinVector3(IReadOnlyList<Vector3> v)
        {
            if (v == null || v.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < v.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(v[i].x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(v[i].y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(v[i].z.ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        // ================================================================
        // 旧版の保存データ（プロファイルの点が x,y の 2 成分）
        // ================================================================

        /// <summary>
        /// 旧版の項目（ProfileDim が 3 未満）の Args を今の形へ直す。
        ///
        /// LegacyXYPairs の印が付いたキーの値は「実数の平坦な列を 2 個ずつ」で書かれている。
        /// これを z=0 を足して 3 個ずつに書き直す。Vector3[] も float[]（ループの平坦な点列）も
        /// 同じ変換で済む。直したら ProfileDim を今の値にする。
        /// 読み込み直後に 1 回だけ呼ぶ（ObjectGroupCsv / ObjectGroupDTO）。
        /// </summary>
        public static void UpgradeLegacyProfileArgs(ObjectGroupStep step)
        {
            if (step == null || step.ProfileDim >= ObjectGroupStep.CurrentProfileDim) return;

            Type t = string.IsNullOrEmpty(step.Action) ? null : PanelCommandFactory.ResolveType(step.Action);
            if (t != null && step.Args != null)
            {
                foreach (string key in PanelCommandFactory.LegacyXYPairKeys(t))
                {
                    if (!step.Args.TryGetValue(key, out var raw) || string.IsNullOrEmpty(raw)) continue;
                    float[] xy = ParseFloatCsv(raw);
                    var xyz = new List<float>(xy.Length / 2 * 3);
                    for (int i = 0; i + 1 < xy.Length; i += 2)
                    {
                        xyz.Add(xy[i]); xyz.Add(xy[i + 1]); xyz.Add(0f);
                    }
                    step.Args[key] = JoinFloats(xyz);
                }
            }
            step.ProfileDim = ObjectGroupStep.CurrentProfileDim;
        }

        /// <summary>グループの全項目へ UpgradeLegacyProfileArgs を掛ける。</summary>
        public static void UpgradeLegacyProfileArgs(ObjectGroup group)
        {
            if (group?.Steps == null) return;
            foreach (var st in group.Steps) UpgradeLegacyProfileArgs(st);
        }

        // ================================================================
        // 旧版の保存データ（ブレンドの基準・出力。作り直し整備計画 段 13）
        // ================================================================

        /// <summary>
        /// 段 13 より前に記録したブレンドの項目の参照を今の形へ直す。
        ///
        /// 旧形は、出力が「実行で増えた ObjectId の最大」で、基準のキー（baseMasterIndex）を持たない。
        ///   宛先を書き換える設定 … 出力＝バックアップ（_backup）、宛先＝書き換えた描画オブジェクト
        ///   複製を作る設定       … 出力＝複製、宛先＝元の描画オブジェクト
        /// 今の形は、基準を入力、書き込み先を出力とする（ApplyBlendCommand の注記）。
        ///   宛先を書き換える設定 … 基準＝旧出力、宛先＝出力＝旧宛先
        ///   複製を作る設定       … 基準＝旧宛先、宛先＝旧出力（出力はそのまま）
        /// 対象は action が applyBlend で、基準のキーが無く、出力と宛先が 1 個ずつの項目だけ。
        /// シナリオの項目（参照と出力を持たない）は何もしない。読み込み直後に 1 回だけ呼ぶ。
        /// </summary>
        public static void UpgradeLegacyBlendRefs(ObjectGroupStep step)
        {
            if (step == null || string.IsNullOrEmpty(step.Action)) return;
            Type t = PanelCommandFactory.ResolveType(step.Action);
            if (t != typeof(ApplyBlendCommand)) return;

            string baseKey = PanelCommandFactory.KeyOfProperty(t, nameof(ApplyBlendCommand.BaseMasterIndex));
            string destKey = PanelCommandFactory.KeyOfProperty(t, nameof(ApplyBlendCommand.DestMasterIndex));
            string newKey  = PanelCommandFactory.KeyOfProperty(t, nameof(ApplyBlendCommand.CreateNewObject));
            if (string.IsNullOrEmpty(baseKey) || string.IsNullOrEmpty(destKey)) return;

            if (step.MeshRefIds != null && step.MeshRefIds.ContainsKey(baseKey)) return;
            if (step.OutputObjectIds == null || step.OutputObjectIds.Count != 1) return;

            var destIds = step.GetMeshRefIds(destKey);
            if (destIds.Count != 1 || destIds[0] == 0UL) return;

            ulong oldDest = destIds[0];
            ulong oldOut  = step.OutputObjectIds[0];

            string newText = step.GetArg(newKey, "false");
            bool createNew = string.Equals(newText?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                          || newText?.Trim() == "1";

            ulong baseId = createNew ? oldDest : oldOut;
            ulong outId  = createNew ? oldOut  : oldDest;

            step.SetMeshRefIds(baseKey, new List<ulong> { baseId });
            step.SetMeshRefIds(destKey, new List<ulong> { outId });
            step.OutputObjectIds.Clear();
            step.OutputObjectIds.Add(outId);
        }

        /// <summary>グループの全項目へ UpgradeLegacyBlendRefs を掛ける。</summary>
        public static void UpgradeLegacyBlendRefs(ObjectGroup group)
        {
            if (group?.Steps == null) return;
            foreach (var st in group.Steps) UpgradeLegacyBlendRefs(st);
        }

        public static string JoinBools(IReadOnlyList<bool> v)
        {
            if (v == null || v.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < v.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(v[i] ? "true" : "false");
            }
            return sb.ToString();
        }

        public static string JoinFloats(IReadOnlyList<float> v)
        {
            if (v == null || v.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < v.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(v[i].ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
