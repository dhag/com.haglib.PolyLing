// UnityClipApplier.cs
// UnityClipDTO（Generic）を ModelContext のボーンへ適用するアプライヤ。
//
// ■ 仕様（UnityClipDTO 準拠）
//   - 値はすべて Unity 左手系。AnimationClip 由来のため座標変換は行わない
//     （VMD のような右手系→左手系変換は不要）。
//   - Generic の bones（Transform パス階層）のみ対応。Humanoid（muscles/body）は無視。
//   - スパースキーをキー間で線形補間（pos=Lerp / rot=Slerp）。接線は保持しない。
//   - scl は v1 では未適用。
//
// ■ マッピング（対応表使用）
//   Transform パス末尾（Unity 名）→ モデルボーン名 の対応は
//   HumanoidBoneMapping.EmbeddedMapping（CSV由来）で解決する。
//   AutoMapFromEmbeddedCSV でモデルのボーン名リストに対して一括構築する。
//
// ■ 適用（BonePoseData デルタ層）
//   MeshContext.LocalMatrix = BoneTransform(ベース) × BonePoseData.LocalMatrix(デルタ)。
//   clip の絶対ローカルを LocalMatrix に一致させるため、
//   delta = BoneTransform^-1 × clipLocal を "UnityClip" レイヤーに設定する。
//   （VMD と同じ層機構。ResetAllBones でレイヤーを消せば復帰する。）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UnityClip;

namespace Poly_Ling.UnityClip
{
    public class UnityClipApplier
    {
        private const string LayerName = "UnityClip";

        // ================================================================
        // 本体ボーンの適用方式は2種類を実装している（コード切替。public 化しない）:
        //   (a) UseBakedBones = true  … 拡張Aで焼いた dto.bakedBones
        //       （HumanBodyBones の localRotation）をそのまま適用する。既定。
        //   (b) UseBakedBones = false … dto.muscles（生マッスル）から PolyLing 側で
        //       ローカル回転を近似再構成して適用する（HumanTrait の min/max と DoF を使用）。
        //       pre/post 回転・sign を省く近似。精度は Unity 実測前提。
        // 二次骨（dto.bones：袖/髪/スカート等）は、どちらの方式でも常時適用する。
        // 切替は下記フラグの書き換えで行う。
        // ================================================================
        private const bool UseBakedBones = true;

        // マッピング状態
        private ModelContext _mappedModel;
        private HumanoidBoneMapping _mapping;          // Unity名 → boneNames のインデックス
        private List<int> _boneMasterIndices;          // boneNames のインデックス → MeshContextList インデックス
        private List<string> _boneNames;               // モデルボーン名（列挙順）

        // リターゲット用ソース rest（外部 UnityBone CSV v2 由来）。null/空 = 未読込。
        private struct SourceRest { public Quaternion RestW; public Vector3 RestPos; }
        private Dictionary<string, SourceRest> _sourceRest;   // Humanoid名 → rest

        /// <summary>ソース rest（バインドポーズ）読込済みなら true＝レスト補正リターゲットが有効。</summary>
        public bool HasSourceRest => _sourceRest != null && _sourceRest.Count > 0;

        /// <summary>位置スケール（Unity 空間値にそのまま乗算。既定 1）。</summary>
        public float PositionScale { get; set; } = 1f;

        /// <summary>対応表で解決できたトラック数（直近の ApplyFrame 時）。</summary>
        public int MatchedTrackCount { get; private set; }

        // ================================================================
        // マッピング構築
        // ================================================================

        public void BuildMapping(ModelContext model)
        {
            if (model == null) return;

            _mappedModel = model;
            _boneNames = new List<string>();
            _boneMasterIndices = new List<int>();

            foreach (var entry in model.Bones)
            {
                int master = entry.MasterIndex;
                if (master < 0 || master >= model.MeshContextList.Count) continue;
                var ctx = model.MeshContextList[master];
                if (ctx == null || string.IsNullOrEmpty(ctx.Name)) continue;
                _boneNames.Add(ctx.Name);
                _boneMasterIndices.Add(master);
            }

            _mapping = new HumanoidBoneMapping();
            _mapping.AutoMapFromEmbeddedCSV(_boneNames, fuzzyMatch: true);
        }

        /// <summary>Transform パス末尾（Unity 名）→ MeshContextList インデックス。無ければ -1。</summary>
        public int ResolveMasterIndex(string path)
        {
            if (_mapping == null || _boneMasterIndices == null) return -1;
            string unityName = LastSegment(path);
            if (string.IsNullOrEmpty(unityName)) return -1;

            // 1) 対応表（Unity名キー）で解決
            int k = _mapping.Get(unityName);
            // 2) フォールバック: 末尾名を直接エイリアスとしてモデルボーン名にあいまい照合
            if (k < 0)
                k = HumanoidBoneMapping.FindBoneByAliases(_boneNames, new List<string> { unityName }, fuzzyMatch: true);

            if (k < 0 || k >= _boneMasterIndices.Count) return -1;
            return _boneMasterIndices[k];
        }

        // ================================================================
        // 適用
        // ================================================================

        public void ApplyFrame(ModelContext model, UnityClipDTO clip, float timeSec)
        {
            if (model == null || clip == null) return;
            if (_mappedModel != model || _mapping == null) BuildMapping(model);

            int matched = 0;

            // 二次骨（袖/髪/スカート等）: どちらの方式でも常時適用
            if (clip.bones != null)
                foreach (var track in clip.bones)
                    matched += ApplyTrackAt(model, track, timeSec);

            // 本体ボーン: (a) 焼いた使用 / (b) 自前実装（近似）
            if (UseBakedBones)
            {
                if (HasSourceRest)
                {
                    // レスト補正あり: Unity→MMD 完全リターゲット（applyRetarget 移植）
                    matched += ApplyRetargetedBody(model, clip, timeSec);
                }
                else if (clip.bakedBones != null)
                {
                    // 未読込: 同一リグ用の従来経路（モデル rest デルタ）
                    foreach (var track in clip.bakedBones)
                        matched += ApplyTrackAt(model, track, timeSec);
                }
            }
            else
            {
                matched += ApplySelfMuscle(model, clip, timeSec);
            }

            MatchedTrackCount = matched;
            model.ComputeWorldMatrices();
        }

        // 1 トラックを timeSec でサンプルして適用。適用できたら 1。
        private int ApplyTrackAt(ModelContext model, UnityBoneTrackDTO track, float timeSec)
        {
            if (track == null || track.keys == null || track.keys.Count == 0) return 0;
            int master = ResolveMasterIndex(track.path);
            if (master < 0) return 0;
            var ctx = model.MeshContextList[master];
            if (ctx == null) return 0;

            Vector3? sPos = SamplePosition(track, timeSec);
            Quaternion? sRot = SampleRotation(track, timeSec);

            // ベース（rest ローカル）
            var bt = ctx.BoneTransform;
            Matrix4x4 baseMat = (bt != null && bt.UseLocalTransform) ? bt.TransformMatrix : Matrix4x4.identity;
            Vector3 restPos = bt != null ? bt.Position : Vector3.zero;
            Quaternion restRot = bt != null ? bt.RotationQuaternion : Quaternion.identity;

            Vector3 localPos = sPos.HasValue ? sPos.Value * PositionScale : restPos;
            Quaternion localRot = sRot.HasValue ? sRot.Value : restRot;

            // clip 絶対ローカル → デルタ（BoneTransform^-1 × clipLocal）
            Matrix4x4 clipLocal = Matrix4x4.TRS(localPos, localRot, Vector3.one);
            Matrix4x4 deltaMat = baseMat.inverse * clipLocal;
            Vector3 deltaPos = new Vector3(deltaMat.m03, deltaMat.m13, deltaMat.m23);
            SetDelta(ctx, deltaPos, deltaMat.rotation);
            return 1;
        }

        // (b) 自前実装（近似）: dto.muscles から本体ボーンのローカル回転を再構成して適用。
        //   ※ Muscle Referential の pre/post 回転・sign を省く近似。
        //     各ボーンの DoF 値を HumanTrait.GetMuscleDefaultMin/Max で角度化し、
        //     dof(0,1,2) を局所軸(X,Y,Z)へ直接対応させて Euler 合成する。
        //     rest からのデルタとして BonePoseData に載せる（muscle=0 で rest）。
        //     精度は Unity 実測前提。
        private int ApplySelfMuscle(ModelContext model, UnityClipDTO clip, float timeSec)
        {
            if (clip.muscles == null || clip.muscles.Count == 0) return 0;

            var muscleByName = new Dictionary<string, UnityMuscleTrackDTO>();
            foreach (var m in clip.muscles)
                if (m != null && !string.IsNullOrEmpty(m.name)) muscleByName[m.name] = m;

            var muscleNames = HumanTrait.MuscleName;
            int boneCount = HumanTrait.BoneCount;
            int matched = 0;

            for (int bi = 0; bi < boneCount; bi++)
            {
                string boneName = HumanTrait.BoneName[bi];          // 例 "Left Upper Arm"（空白入り）
                string key = boneName.Replace(" ", string.Empty);    // 対応表キー "LeftUpperArm"

                int k = _mapping.Get(key);
                if (k < 0)
                    k = HumanoidBoneMapping.FindBoneByAliases(
                        _boneNames, new List<string> { key, boneName }, fuzzyMatch: true);
                if (k < 0 || k >= _boneMasterIndices.Count) continue;

                var ctx = model.MeshContextList[_boneMasterIndices[k]];
                if (ctx == null) continue;

                Vector3 euler = Vector3.zero;
                bool any = false;
                for (int dof = 0; dof < 3; dof++)
                {
                    int mi = HumanTrait.MuscleFromBone(bi, dof);
                    if (mi < 0 || muscleNames == null || mi >= muscleNames.Length) continue;
                    if (!muscleByName.TryGetValue(muscleNames[mi], out var mt)) continue;

                    float v = SampleWeight(mt, timeSec);                 // 正規化値 [-1,1]
                    float min = HumanTrait.GetMuscleDefaultMin(mi);
                    float max = HumanTrait.GetMuscleDefaultMax(mi);
                    euler[dof] = v >= 0f ? v * max : -v * min;           // v=+1→max, v=-1→min, v=0→0
                    any = true;
                }
                if (!any) continue;

                // rest からのデルタ（位置は変えない）
                SetDelta(ctx, Vector3.zero, Quaternion.Euler(euler));
                matched++;
            }
            return matched;
        }

        // ================================================================
        // レスト補正（Unity→MMD 完全リターゲット）
        //   motion_timeline.html applyRetarget（isModel=false 経路）を逐語移植。
        //   「FK でワールド化 → ソース rest(RestW) 相対のワールド差分 → 左右ミラー
        //     → A/T 整列 → CANON 親相対のローカルへ戻す」。
        //   ソース rest（RestW/位置）は外部 UnityBone CSV v2（拡張C）から供給する。
        //   独自の座標変換は足さない（mir / ft / Mx は逐語）。
        // ================================================================

        // JS QR 準拠のクォータニオン [x,y,z,w]（world = QMul(parentWorld, local)）
        private struct Q
        {
            public float x, y, z, w;
            public Q(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
            public static Q Identity => new Q(0f, 0f, 0f, 1f);
            public Quaternion ToUnity() => new Quaternion(x, y, z, w);
            public static Q From(Quaternion q) => new Q(q.x, q.y, q.z, q.w);
        }

        private static Q QMul(Q a, Q b) => new Q(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        private static Q QConj(Q q) => new Q(-q.x, -q.y, -q.z, q.w);

        private static Q QNorm(Q q)
        {
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (n <= 1e-20f) n = 1f;
            return new Q(q.x / n, q.y / n, q.z / n, q.w / n);
        }

        // Y軸180°回転の共役（Unity→PMX: 両左手系で向き-Z差のみ、正則回転）
        private static Q QMir(Q q) => new Q(-q.x, q.y, -q.z, q.w);

        // 単位ベクトル a→b の最短弧（JS QR.ft 逐語）
        private static Q QFromTo(Vector3 a, Vector3 b)
        {
            float d = Mathf.Clamp(a.x * b.x + a.y * b.y + a.z * b.z, -1f, 1f);
            if (d > 0.999999f) return Q.Identity;
            if (d < -0.999999f)
            {
                Vector3 ax = Mathf.Abs(a.x) < 0.9f ? new Vector3(1f, 0f, 0f) : new Vector3(0f, 1f, 0f);
                Vector3 c0 = new Vector3(
                    a.y * ax.z - a.z * ax.y,
                    a.z * ax.x - a.x * ax.z,
                    a.x * ax.y - a.y * ax.x);
                float n0 = Mathf.Sqrt(c0.x * c0.x + c0.y * c0.y + c0.z * c0.z);
                if (n0 <= 1e-20f) n0 = 1f;
                return new Q(c0.x / n0, c0.y / n0, c0.z / n0, 0f);
            }
            Vector3 c = new Vector3(
                a.y * b.z - a.z * b.y,
                a.z * b.x - a.x * b.z,
                a.x * b.y - a.y * b.x);
            float w = 1f + d;
            float n = Mathf.Sqrt(c.x * c.x + c.y * c.y + c.z * c.z + w * w);
            if (n <= 1e-20f) n = 1f;
            return new Q(c.x / n, c.y / n, c.z / n, w / n);
        }

        private static Vector3 Mx(Vector3 v) => new Vector3(-v.x, v.y, -v.z);   // Y軸180°回転（位置/方向）

        // 正準(Humanoid)階層テーブル（motion_timeline.html CANON_PARENT / CANON_CHILD と同一）
        private static Dictionary<string, string> _canonParent;
        private static Dictionary<string, string> _canonChild;

        private static void EnsureCanon()
        {
            if (_canonParent != null) return;
            var P = new Dictionary<string, string>
            {
                { "Hips", null }, { "Spine", "Hips" }, { "Chest", "Spine" }, { "UpperChest", "Chest" },
                { "Neck", "UpperChest" }, { "Head", "Neck" }, { "Jaw", "Head" },
                { "LeftEye", "Head" }, { "RightEye", "Head" },
            };
            var C = new Dictionary<string, string>
            {
                { "Hips", "Spine" }, { "Spine", "Chest" }, { "Chest", "Neck" },
                { "UpperChest", "Neck" }, { "Neck", "Head" },
            };
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            foreach (var s in new[] { "Left", "Right" })
            {
                P[s + "Shoulder"] = "UpperChest"; P[s + "UpperArm"] = s + "Shoulder";
                P[s + "LowerArm"] = s + "UpperArm"; P[s + "Hand"] = s + "LowerArm";
                P[s + "UpperLeg"] = "Hips"; P[s + "LowerLeg"] = s + "UpperLeg";
                P[s + "Foot"] = s + "LowerLeg"; P[s + "Toes"] = s + "Foot";

                C[s + "Shoulder"] = s + "UpperArm"; C[s + "UpperArm"] = s + "LowerArm"; C[s + "LowerArm"] = s + "Hand";
                C[s + "UpperLeg"] = s + "LowerLeg"; C[s + "LowerLeg"] = s + "Foot"; C[s + "Foot"] = s + "Toes";

                foreach (var fg in fingers)
                {
                    P[s + fg + "Proximal"] = s + "Hand";
                    P[s + fg + "Intermediate"] = s + fg + "Proximal";
                    P[s + fg + "Distal"] = s + fg + "Intermediate";

                    C[s + fg + "Proximal"] = s + fg + "Intermediate";
                    C[s + fg + "Intermediate"] = s + fg + "Distal";
                }
            }
            _canonParent = P;
            _canonChild = C;
        }

        // present で親をたどる（欠損はスキップ）
        private static string ParentOf(string cn, HashSet<string> present)
        {
            _canonParent.TryGetValue(cn, out var p);
            while (p != null && !present.Contains(p)) _canonParent.TryGetValue(p, out p);
            return p;
        }

        private static int DepthOf(string cn)
        {
            int d = 0;
            _canonParent.TryGetValue(cn, out var p);
            while (p != null) { d++; _canonParent.TryGetValue(p, out p); }
            return d;
        }

        // rest 方向（骨→CANON子の単位ベクトル）
        private static bool DirRest(Dictionary<string, Vector3> pos, string cn, out Vector3 dir)
        {
            dir = Vector3.zero;
            if (!_canonChild.TryGetValue(cn, out var ch) || ch == null) return false;
            if (!pos.TryGetValue(cn, out var pc) || !pos.TryGetValue(ch, out var pch)) return false;
            Vector3 v = pch - pc;
            float n = v.magnitude;
            if (n <= 1e-9f) return false;
            dir = v / n;
            return true;
        }

        // 本体レスト補正の本体：applyRetarget(isModel=false) を移植し timeSec で適用。
        private int ApplyRetargetedBody(ModelContext model, UnityClipDTO clip, float timeSec)
        {
            if (clip.bakedBones == null || clip.bakedBones.Count == 0) return 0;
            if (_sourceRest == null || _sourceRest.Count == 0) return 0;
            EnsureCanon();

            // byCanon: Humanoid名（= bakedBones.path）→ トラック
            var byCanon = new Dictionary<string, UnityBoneTrackDTO>();
            foreach (var t in clip.bakedBones)
                if (t != null && t.keys != null && t.keys.Count > 0 && !string.IsNullOrEmpty(t.path))
                    byCanon[t.path] = t;
            if (byCanon.Count == 0) return 0;

            var present = new HashSet<string>(byCanon.Keys);
            var order = new List<string>(byCanon.Keys);
            order.Sort((a, b) => DepthOf(a) - DepthOf(b));

            // ターゲット rest 位置（モデル空間 = BindPose.inverse の並進）
            var tp = new Dictionary<string, Vector3>();
            foreach (var cn in order)
            {
                int master = ResolveMasterIndex(cn);
                if (master < 0 || master >= model.MeshContextList.Count) continue;
                var ctx = model.MeshContextList[master];
                if (ctx == null) continue;
                Matrix4x4 world = ctx.BindPose.inverse;
                // Mx(Y軸180°回転)が向きを担うため、tp は Z 反転しない（二重反転回避）
                tp[cn] = new Vector3(world.m03, world.m13, world.m23);
            }

            // ソース rest 位置
            var sp = new Dictionary<string, Vector3>();
            foreach (var kv in _sourceRest) sp[kv.Key] = kv.Value.RestPos;

            // 整列 A[cn] = ft(ターゲットrest方向 → ミラーしたソースrest方向)。子が無ければ単位。
            var Aq = new Dictionary<string, Q>();
            foreach (var cn in order)
            {
                bool hasDs = DirRest(sp, cn, out var ds);
                bool hasDt = DirRest(tp, cn, out var dt);
                Aq[cn] = (hasDs && hasDt) ? QFromTo(dt, Mx(ds)) : Q.Identity;
            }

            // timeSec で local をサンプル
            var Lsrc = new Dictionary<string, Q>();
            foreach (var cn in order)
            {
                Quaternion? s = SampleRotation(byCanon[cn], timeSec);
                Lsrc[cn] = s.HasValue ? Q.From(s.Value) : Q.Identity;
            }

            // FK でワールド化
            var W = new Dictionary<string, Q>();
            foreach (var cn in order)
            {
                string p = ParentOf(cn, present);
                W[cn] = (p != null) ? QMul(W[p], Lsrc[cn]) : Lsrc[cn];
            }

            // ワールド差分 → ミラー＋整列 → CANON親相対ローカル → 適用
            var Wt = new Dictionary<string, Q>();
            int matched = 0;
            foreach (var cn in order)
            {
                Q srcRestW = _sourceRest.TryGetValue(cn, out var sr) ? Q.From(sr.RestW) : Q.Identity;
                Q E = QMul(W[cn], QConj(srcRestW));         // ワールド差分（レスト相対）
                Wt[cn] = QMul(QMir(E), Aq[cn]);             // 左右ミラー＋A/T整列
                string p = ParentOf(cn, present);
                Q outLocal = (p != null) ? QNorm(QMul(QConj(Wt[p]), Wt[cn])) : QNorm(Wt[cn]);

                if (ApplyLocalRotationToBone(model, cn, outLocal)) matched++;
            }
            return matched;
        }

        // CANON名（= Humanoid名）のモデルボーンへ CANON親相対ローカル回転を適用（回転のみ・位置は rest 維持）。
        private bool ApplyLocalRotationToBone(ModelContext model, string canonName, Q outLocal)
        {
            int master = ResolveMasterIndex(canonName);
            if (master < 0 || master >= model.MeshContextList.Count) return false;
            var ctx = model.MeshContextList[master];
            if (ctx == null) return false;

            // outLocal は「ターゲット rest 回転 = identity」前提（motion_timeline と同じ MMD 規約）の
            // CANON 親相対ローカル回転。PMX ボーンはボーン整列の rest 回転 R(=BoneModelRotation, 非identity)
            // を持つため、上書きすると R を捨てて全ボーンが誤配向になる。
            // VMDApplier と同じく delta = R^-1 * outLocal * R を rest（baseMat）へのデルタとして適用する（回転のみ）。
            Q R = Q.From(ctx.BoneModelRotation);
            Q delta = QMul(QConj(R), QMul(outLocal, R));
            SetDelta(ctx, Vector3.zero, delta.ToUnity());
            return true;
        }

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

        /// <summary>適用した "UnityClip" レイヤーを全ボーンから除去して復帰。</summary>
        public void ResetAllBones(ModelContext model)
        {
            if (model == null) return;
            foreach (var entry in model.Bones)
            {
                int master = entry.MasterIndex;
                if (master < 0 || master >= model.MeshContextList.Count) continue;
                var ctx = model.MeshContextList[master];
                var bpd = ctx?.BonePoseData;
                if (bpd == null) continue;
                var layer = bpd.GetLayer(LayerName);
                if (layer != null) layer.Clear();
            }
            model.ComputeWorldMatrices();
        }

        // ================================================================
        // サンプリング（スパースキー・線形補間）
        // ================================================================

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
