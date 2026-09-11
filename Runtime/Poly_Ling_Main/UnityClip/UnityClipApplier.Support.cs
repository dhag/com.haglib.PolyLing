// UnityClipApplier.Support.cs
// Unity クリップ適用：外部 CSV・診断ログ・ノード単位のデルタ・仮想ミラー鎖・ワールド行列・サンプリング。
// Runtime/Poly_Ling_Main/UnityClip/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UnityClip;

namespace Poly_Ling.UnityClip
{
    public partial class UnityClipApplier
    {
        // ================================================================
        // 外部 UnityBone CSV v2（拡張C）読込：Humanoid毎の RestW/位置
        //   列: UnityBone,Name,NameEn,Humanoid,Parent,PosX,PosY,PosZ,
        //       RestLX,RestLY,RestLZ,RestLW,RestWX,RestWY,RestWZ,RestWW
        //   ※ HumanoidBoneMapping.LoadFromCSV は使わない（あれは名前対応CSV用）。
        // ================================================================
        public int LoadSourceRestCsv(string csvText)
        {
            var dict = new Dictionary<string, SourceRest>();
            if (!string.IsNullOrEmpty(csvText))
            {
                var lines = csvText.Split('\n');
                foreach (var raw in lines)
                {
                    string line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line[0] == ';') continue;   // コメント行
                    var f = SplitCsvLine(line);
                    if (f.Count < 16) continue;
                    if ((f[0] ?? "").Trim() != "UnityBone") continue;   // データ行のみ
                    if ((f[1] ?? "").Trim() == "Name") continue;        // ヘッダ行スキップ
                    string hum = (f[3] ?? "").Trim();
                    if (hum.Length == 0) continue;                      // Humanoid割当のみ採用
                    Vector3 pos = new Vector3(ParseF(f[5]), ParseF(f[6]), ParseF(f[7]));
                    var qn = QNorm(new Q(ParseF(f[12]), ParseF(f[13]), ParseF(f[14]), ParseF(f[15])));
                    dict[hum] = new SourceRest { RestW = qn.ToUnity(), RestPos = pos };
                }
            }
            _sourceRest = dict;
            return dict.Count;
        }

        /// <summary>ソース rest（バインドポーズ）を破棄。以後は同一リグ用の従来経路に戻る。</summary>
        public void ClearSourceRest() { _sourceRest = null; }

        // ================================================================
        // 外部 UnityLimit CSV v1 読込：Humanoid 毎の可動域（度）＋マッスル実測
        //   列（53列・0起点）:
        //     0  UnityLimit
        //     1  Humanoid（HumanBodyBones 列挙名）   2 TraitName   3 BoneName   4 UseDefault
        //     5- 7 MinX,MinY,MinZ                    8-10 MaxX,MaxY,MaxZ
        //    11-13 CenX,CenY,CenZ                   14 AxisLength
        //    15-17 Dof0Muscle,Dof1Muscle,Dof2Muscle
        //    18-23 Dof0Min,Dof0Max,Dof1Min,Dof1Max,Dof2Min,Dof2Max（HumanTrait 既定・度）
        //    24    Measured
        //    25-28 Zero(x,y,z,w)
        //    29-36 D0Min(xyzw), D0Max(xyzw)
        //    37-44 D1Min(xyzw), D1Max(xyzw)
        //    45-52 D2Min(xyzw), D2Max(xyzw)
        //
        //   ※ ヘッダ行も 1 列目が "UnityLimit" のため、f[1]=="Humanoid" の行を読み飛ばす。
        // ================================================================
        public int LoadMuscleLimitCsv(string csvText)
        {
            var dict = new Dictionary<string, MuscleLimitEntry>();
            if (!string.IsNullOrEmpty(csvText))
            {
                var lines = csvText.Split('\n');
                foreach (var raw in lines)
                {
                    string line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line[0] == ';') continue;      // コメント行
                    var f = SplitCsvLine(line);
                    if (f.Count < 25) continue;
                    if ((f[0] ?? "").Trim() != "UnityLimit") continue;     // データ行のみ
                    if ((f[1] ?? "").Trim() == "Humanoid") continue;       // ヘッダ行スキップ

                    string hum = (f[1] ?? "").Trim();
                    if (hum.Length == 0) continue;

                    var e = new MuscleLimitEntry
                    {
                        Min = new Vector3(ParseF(f[5]), ParseF(f[6]), ParseF(f[7])),
                        Max = new Vector3(ParseF(f[8]), ParseF(f[9]), ParseF(f[10]))
                    };

                    bool measured = string.Equals((f[24] ?? "").Trim(), "true",
                        System.StringComparison.OrdinalIgnoreCase);
                    if (measured && f.Count >= 53)
                    {
                        e.Measured = true;
                        e.Zero = ReadQ(f, 25);
                        for (int dof = 0; dof < 3; dof++)
                        {
                            e.MinQ[dof] = ReadQ(f, 29 + dof * 8);
                            e.MaxQ[dof] = ReadQ(f, 33 + dof * 8);
                        }
                    }

                    dict[hum] = e;
                }
            }
            _muscleLimits = dict;
            _canonEntryCache?.Clear();     // 取り違え時の残留を断つ
            return dict.Count;
        }

        /// <summary>
        /// マッスル可動域・実測を破棄する。以後は全ボーンが既定
        /// （T ポーズ基準の Unity 定義値 CanonMuscleTable）で駆動される。
        /// 別モデルの CSV を誤って読んだときの復帰口。
        /// </summary>
        public void ClearMuscleLimits()
        {
            _muscleLimits = null;
            _canonEntryCache?.Clear();     // 取り違え時の残留を断つ
        }

        // ================================================================
        // 診断ログ
        //   毎フレーム同じ内容を出すと読めないため、内容が変化したときだけ出す。
        //   本体ボーンが欠けたまま一部だけデルタが載ると連鎖が崩れて姿勢が破綻するため、
        //   未解決ボーン名を必ず列挙する。
        // ================================================================
        private void LogDiagnosticsIfChanged()
        {
            if (!DebugLog) return;

            string key = string.Concat(
                ResolvedBodyMode.ToString(), "|",
                PathMatchedCount.ToString(), "/", PathTrackCount.ToString(), "|",
                MuscleMatchedCount.ToString(), "/", MuscleTargetCount.ToString(), "|",
                string.Join(",", UnresolvedMuscleBones.ToArray()));
            if (key == _lastDiagKey) return;
            _lastDiagKey = key;

            var sb = new System.Text.StringBuilder();
            sb.Append("[UnityClipApplier] body=").Append(ResolvedBodyMode)
              .Append(" limits=").Append(HasMuscleMeasured ? "CSV実測" : "既定(Tポーズ基準)")
              .Append(" sourceRest=").Append(HasSourceRest ? "yes" : "no").Append('\n');
            sb.Append("  path   : ").Append(PathMatchedCount).Append('/').Append(PathTrackCount).Append('\n');
            sb.Append("  muscle : ").Append(MuscleMatchedCount).Append('/').Append(MuscleTargetCount).Append('\n');

            if (UnresolvedMuscleBones.Count > 0)
                sb.Append("  未解決(本体): ").Append(string.Join(", ", UnresolvedMuscleBones.ToArray())).Append('\n');

            if (UnresolvedPathTracks.Count > 0)
            {
                int show = UnresolvedPathTracks.Count > 20 ? 20 : UnresolvedPathTracks.Count;
                var head = UnresolvedPathTracks.GetRange(0, show);
                sb.Append("  未解決(path): ").Append(string.Join(", ", head.ToArray()));
                if (UnresolvedPathTracks.Count > show)
                    sb.Append(" ...他 ").Append(UnresolvedPathTracks.Count - show).Append(" 本");
                sb.Append('\n');
            }

            Debug.Log(sb.ToString());
        }

        // クォータニオンを「軸+角度(度)」の読める形にする（ログ用）。
        private static string AxAng(Quaternion q)
        {
            q = QuatNorm(q);
            float w = Mathf.Abs(q.w) > 1f ? Mathf.Sign(q.w) : q.w;
            if (w < 0f) { q = new Quaternion(-q.x, -q.y, -q.z, -q.w); w = -w; }
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z);
            float ang = 2f * Mathf.Atan2(n, w) * Mathf.Rad2Deg;
            if (n <= 1e-8f) return "(軸なし 0.00deg)";
            return string.Format("(軸 {0:F3},{1:F3},{2:F3}  {3:F2}deg)", q.x / n, q.y / n, q.z / n, ang);
        }

        private static Quaternion QuatNorm(Quaternion q)
        {
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (n <= 1e-8f) return Quaternion.identity;
            return new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n);
        }

        // 4 連続列を正規化クォータニオンとして読む。ゼロ長なら identity。
        private static Quaternion ReadQ(List<string> f, int i0)
        {
            float x = ParseF(f[i0]), y = ParseF(f[i0 + 1]), z = ParseF(f[i0 + 2]), w = ParseF(f[i0 + 3]);
            float n = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (n <= 1e-8f) return Quaternion.identity;
            return new Quaternion(x / n, y / n, z / n, w / n);
        }

        private static float ParseF(string s)
        {
            return float.TryParse((s ?? "").Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        // 引用対応 CSV 分割（"..."、"" エスケープ対応。JS splitCsvLine 準拠）
        private static List<string> SplitCsvLine(string line)
        {
            var outp = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                        else q = false;
                    }
                    else cur.Append(c);
                }
                else
                {
                    if (c == '"') q = true;
                    else if (c == ',') { outp.Add(cur.ToString()); cur.Clear(); }
                    else cur.Append(c);
                }
            }
            outp.Add(cur.ToString());
            return outp;
        }

        private void SetDelta(MeshContext ctx, Vector3 deltaPos, Quaternion deltaRot)
        {
            if (ctx.BonePoseData == null)
            {
                ctx.BonePoseData = new BonePoseData();
                ctx.BonePoseData.IsActive = true;
            }
            ctx.BonePoseData.SetLayer(LayerName, deltaPos, deltaRot);
        }

        // ================================================================
        // ノード単位のデルタ
        // ================================================================

        private void ClearNodeDeltas()
        {
            if (_nodeHasDelta == null) return;
            for (int i = 0; i < _nodeHasDelta.Length; i++)
            {
                _nodeHasDelta[i] = false;
                _nodeDeltaRot[i] = Quaternion.identity;
                _nodeDeltaPos[i] = Vector3.zero;
            }
        }

        // ノード自身の枠でのデルタを記録する。
        //   実体ノード … そのまま BonePoseData へ書く（従来と同じ）。
        //   ミラーノード … ここでは書かない。仮想鎖を解いた後に
        //                  ApplyVirtualMirror が共役を打ち消した値へ直して書く。
        private void SetNodeDelta(ModelContext model, int node, Vector3 deltaPos, Quaternion deltaRot)
        {
            if (_skeleton == null || node < 0 || node >= _skeleton.Nodes.Count) return;
            if (_nodeHasDelta == null || node >= _nodeHasDelta.Length) return;

            _nodeDeltaPos[node] = deltaPos;
            _nodeDeltaRot[node] = deltaRot;
            _nodeHasDelta[node] = true;

            if (_skeleton.Nodes[node].IsMirror) return;

            var ctx = _skeleton.TargetContext(model, node);
            if (ctx != null) SetDelta(ctx, deltaPos, deltaRot);
        }

        // ================================================================
        // 仮想ミラー鎖の解決
        // ----------------------------------------------------------------
        // 【目標ワールド】
        //   Ŵ_j = Ŵ_parent · B̂_j · D_j      B̂_j = S·B_j·S（＝ MirrorLocalTRS）
        //   Ŵ_parent は、仮想ミラーの親があればその Ŵ、無ければ（ミラー枝の外へ出た
        //   ＝共有関節にぶら下がっている）実体側 MeshContext の WorldMatrix。
        //   これで
        //     ・体幹など共有関節の下のミラー側は剛体として素直に付いてくる
        //     ・ミラー枝の中は右半身自身のデルタだけで動く（左半身の鏡像にならない）
        //   の両方が同時に成り立つ。エクスポータがミラー枝の GameObject を組む規則
        //   （HierarchyExportWindow.ApplyJointTransform）と同じ形である。
        //
        // 【書き戻し】
        //   ComputeWorldMatrices はミラー側へ共役 S·H·S を掛け直す。
        //   ミラー側の階層親は実体側と同じ（SyncDerivedMirrorTransforms が強制）で、
        //   ミラー側を階層親に持つノードは存在しないため
        //     ctx.WorldMatrix = S·(H_p · B_r · D_m)·S
        //   よって目標に一致させるデルタは
        //     D_m = (H_p · B_r)⁻¹ · S · Ŵ_m · S
        //
        // 【デルタが全て単位のとき】
        //   Ŵ_m = H_p·B̂ となり、H_p が鏡映対称（＝左右対称に組まれたモデルの
        //   共有関節）なら D_m は単位に戻る。つまり「クリップ未適用なら現行表示のまま」。
        // ================================================================

        private bool ApplyVirtualMirror(ModelContext model)
        {
            if (_skeleton == null || _skeleton.MirrorNodeCount <= 0) return false;

            var nodes = _skeleton.Nodes;
            var list  = model.MeshContextList;

            var solved = new Matrix4x4[nodes.Count];
            var done   = new bool[nodes.Count];
            bool wrote = false;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].IsMirror) continue;

                var ctx = _skeleton.TargetContext(model, i);
                if (ctx == null) continue;                       // 純仮想関節は書き先が無い

                int src = nodes[i].SourceContextIndex;
                var real = (src >= 0 && src < list.Count) ? list[src] : null;
                if (real == null) continue;

                Matrix4x4 target = SolveMirrorWorld(model, i, solved, done);

                Matrix4x4 hp = Matrix4x4.identity;
                int p = real.HierarchyParentIndex;
                if (p >= 0 && p < list.Count && list[p] != null) hp = list[p].WorldMatrix;

                Matrix4x4 s = MirrorBranchOps.MirrorMatrix(real.MirrorAxis, real.MirrorDistance);
                Matrix4x4 b = _skeleton.RestLocalMatrix(model, i);
                Matrix4x4 d = (hp * b).inverse * (s * target * s);

                SetDelta(ctx, new Vector3(d.m03, d.m13, d.m23), d.rotation);
                wrote = true;
            }

            return wrote;
        }

        // 仮想ミラーノードの目標ワールドを親から順に解く（メモ化再帰）。
        private Matrix4x4 SolveMirrorWorld(ModelContext model, int node, Matrix4x4[] solved, bool[] done)
        {
            if (done[node]) return solved[node];
            done[node]   = true;                 // 万一の循環で無限再帰にならないよう先に立てる
            solved[node] = Matrix4x4.identity;

            var n    = _skeleton.Nodes[node];
            var list = model.MeshContextList;

            Matrix4x4 parentW = Matrix4x4.identity;
            if (n.ParentNode >= 0 && n.ParentNode < solved.Length)
                parentW = SolveMirrorWorld(model, n.ParentNode, solved, done);
            else if (n.ParentContextIndex >= 0 && n.ParentContextIndex < list.Count &&
                     list[n.ParentContextIndex] != null)
                parentW = list[n.ParentContextIndex].WorldMatrix;

            Matrix4x4 bHat = _skeleton.RestLocalMatrixOwn(model, node);

            Matrix4x4 d = Matrix4x4.identity;
            if (_nodeHasDelta != null && node < _nodeHasDelta.Length && _nodeHasDelta[node])
                d = Matrix4x4.TRS(_nodeDeltaPos[node], _nodeDeltaRot[node], Vector3.one);

            solved[node] = parentW * bHat * d;
            return solved[node];
        }

        // ================================================================
        // ノードのワールド行列
        // ================================================================

        // 実体ノードは MeshContext.WorldMatrix、ミラーノードは SolveMirrorWorld の
        // 目標ワールド Ŵ を控える。ミラー側コンテキストを持つノードは
        // ApplyVirtualMirror が ctx.WorldMatrix を Ŵ に一致させているので同値だが、
        // 純仮想関節は ctx が無いためこちらでしか取れない。
        private void CacheNodeWorlds(ModelContext model)
        {
            if (_skeleton == null) { _nodeWorldValid = false; return; }

            int n = _skeleton.Nodes.Count;
            if (_nodeWorld == null || _nodeWorld.Length != n) _nodeWorld = new Matrix4x4[n];

            var solved = new Matrix4x4[n];
            var done   = new bool[n];

            for (int i = 0; i < n; i++)
            {
                if (_skeleton.Nodes[i].IsMirror)
                {
                    _nodeWorld[i] = SolveMirrorWorld(model, i, solved, done);
                    continue;
                }
                var ctx = _skeleton.TargetContext(model, i);
                _nodeWorld[i] = ctx != null ? ctx.WorldMatrix : Matrix4x4.identity;
            }

            _nodeWorldValid = true;
        }

        /// <summary>
        /// 直近の ApplyFrame 時点でのノードのワールド行列。
        /// ApplyFrame をまだ通していない場合は false。
        /// </summary>
        public bool TryGetNodeWorldMatrix(int node, out Matrix4x4 world)
        {
            if (!_nodeWorldValid || _nodeWorld == null || node < 0 || node >= _nodeWorld.Length)
            {
                world = Matrix4x4.identity;
                return false;
            }
            world = _nodeWorld[node];
            return true;
        }

        /// <summary>
        /// 適用した "UnityClip" レイヤーを全コンテキストから除去して復帰。
        /// ミラー側・MeshFilter メッシュにも書いているため、ボーンだけでなく全件を走査する。
        /// </summary>
        public void ResetAllBones(ModelContext model)
        {
            if (model == null || model.MeshContextList == null) return;
            var list = model.MeshContextList;
            for (int i = 0; i < list.Count; i++)
            {
                var bpd = list[i]?.BonePoseData;
                if (bpd == null) continue;
                var layer = bpd.GetLayer(LayerName);
                if (layer != null) layer.Clear();
            }
            ClearNodeDeltas();
            model.ComputeWorldMatrices();
            _nodeWorldValid = false;
        }

        // ================================================================
        // サンプリング（スパースキー・線形補間）
        // ================================================================

        /// <summary>
        /// Animator トラック（マッスル・RootT / RootQ）を timeSec で線形補間して返す。
        ///
        /// サンプリングの規則をここ 1 か所に集める。Root 系を扱う
        /// UnityClipRootMotion も同じ補間で読むため、実装を書き写さずここを呼ぶ。
        /// </summary>
        public static float SampleTrackValue(UnityMuscleTrackDTO track, float timeSec)
            => SampleWeight(track, timeSec);

        // マッスル重み（正規化値）を timeSec で線形補間
        private static float SampleWeight(UnityMuscleTrackDTO track, float timeSec)
        {
            if (track == null || track.w == null || track.w.Count == 0) return 0f;
            UnityWeightKeyDTO prev = null, next = null;
            foreach (var key in track.w)
            {
                if (key == null) continue;
                if (key.t <= timeSec) prev = key;
                if (key.t >= timeSec) { next = key; break; }
            }
            if (prev == null && next == null) return 0f;
            if (prev == null) return next.v;
            if (next == null) return prev.v;
            if (prev.t == next.t) return prev.v;
            float a = (timeSec - prev.t) / (next.t - prev.t);
            return Mathf.Lerp(prev.v, next.v, a);
        }

        private static Vector3? SamplePosition(UnityBoneTrackDTO track, float timeSec)
        {
            // pos を持つキーだけで補間
            UnityBoneKeyDTO prev = null, next = null;
            foreach (var key in track.keys)
            {
                if (key == null || key.pos == null || key.pos.Length < 3) continue;
                if (key.t <= timeSec) prev = key;
                if (key.t >= timeSec) { next = key; break; }
            }
            if (prev == null && next == null) return null;
            if (prev == null) return ToVec3(next.pos);
            if (next == null) return ToVec3(prev.pos);
            if (prev.t == next.t) return ToVec3(prev.pos);
            float w = (timeSec - prev.t) / (next.t - prev.t);
            return Vector3.Lerp(ToVec3(prev.pos), ToVec3(next.pos), w);
        }

        private static Quaternion? SampleRotation(UnityBoneTrackDTO track, float timeSec)
        {
            UnityBoneKeyDTO prev = null, next = null;
            foreach (var key in track.keys)
            {
                if (key == null || key.rot == null || key.rot.Length < 4) continue;
                if (key.t <= timeSec) prev = key;
                if (key.t >= timeSec) { next = key; break; }
            }
            if (prev == null && next == null) return null;
            if (prev == null) return ToQuat(next.rot);
            if (next == null) return ToQuat(prev.rot);
            if (prev.t == next.t) return ToQuat(prev.rot);
            float w = (timeSec - prev.t) / (next.t - prev.t);
            return Quaternion.Slerp(ToQuat(prev.rot), ToQuat(next.rot), w);
        }

        // ================================================================
        // ヘルパ
        // ================================================================

        private static Vector3 ToVec3(float[] a) => new Vector3(a[0], a[1], a[2]);
        private static Quaternion ToQuat(float[] a) => new Quaternion(a[0], a[1], a[2], a[3]);

        private static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            int idx = path.LastIndexOf('/');
            return idx >= 0 ? path.Substring(idx + 1) : path;
        }
    }
}
