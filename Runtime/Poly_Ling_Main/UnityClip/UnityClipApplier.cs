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
//
// ================================================================
// ★★★ Unity のマッスル基準ポーズについて（恒久メモ・削除禁止） ★★★
// ----------------------------------------------------------------
// ここは AI が繰り返し間違える箇所である。Anthropic / OpenAI / Google の
// いずれのモデルも同じ誤りをした。修正・拡張する前に必ず読むこと。
//
// 【Unity の仕様・定義】
//   「マッスル値に 0 を入れるとこのポーズになる」というのが Unity の仕様であり
//   定義である。モデルから計算で導けるものではない。
//   Unity は、モデルのレスト姿勢が T ポーズのときに、ひざ・ひじ・股関節を
//   曲げた奇妙なポーズを muscle=0 と定め、それを基準に全ての姿勢を決めている。
//   したがって muscle=0 の姿勢は「T ポーズ基準の定数」である。
//
// 【やってはいけないこと】
//   - Zero（マッスル 0 のローカル回転）をボーン方向やレスト回転から
//     再構成しようとすること。定義であって導出物ではない。
//   - dof 番号を Vector3 の X/Y/Z 成分に直接代入して Quaternion.Euler で
//     組み立てること。マッスル軸はボーンの解剖軸であってモデル空間軸ではない。
//     旧実装はこれをやっており、ひざが X 軸回りではなく Z 軸回りに曲がっていた。
//   - 「レスト姿勢が T ポーズならマッスル 0 でも T ポーズのままのはず」と
//     考えること。ならない。ひざは約 80 度曲がった状態が muscle=0 である。
//
// 【失敗の記録】
//   ボーン方向の最短弧 FromToRotation(d0, d) で Zero を補正しようとしたが、
//   腕で最大 63.30 度ずれた。原因は、T ポーズ化したモデルでもボーン「位置」は
//   T ポーズだが腕の傾きが rest「回転」側に残るため、方向ベースの補正が
//   恒等になってしまうこと。方向だけでは足りない。
//
// 【この実装が持つ定数】
//   CanonMuscleTable = T ポーズ基準の Zero と、dof ごとの回転軸・可動端（度）。
//   出所は StickRobot（完全 T ポーズ・全ボーンのレスト回転が恒等）から書き出した
//   UnityLimit CSV の実測値そのもの。レストが恒等なので 実測値 ＝ Unity の定義値。
//   StickRobot に無いボーン（肩 / UpperChest / つま先 / 目 / 指）は
//   modelF74C_Skin の実測値を親の累積レスト回転で共役して T ポーズ基準へ戻した。
//   検証: modelF74C_Skin と __AちゃんH の実測値に対し、腕以外の全ボーンで 0.00 度一致。
//
//   代表値（角度は度・T ポーズ基準）:
//     ひざ LowerLeg  Zero = +X 回り 79.99
//     股   UpperLeg  Zero = -X 回り 30.01
//     ひじ LowerArm  Zero = ±Y 回り 79.99
//     肩   UpperArm  Zero = (0, ±0.593, ±0.805) 回り 48.65
//     体幹・首・頭・手首・足首 = 恒等
// ================================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UnityClip;

namespace Poly_Ling.UnityClip
{
    public class UnityClipApplier
    {
        private const string LayerName = "UnityClip";

        // ================================================================
        // 本体ボーンの適用方式（「あれば使う・なければそれなり」）
        // ----------------------------------------------------------------
        //   (a) BakedBones … dto.bakedBones（HumanBodyBones の localRotation を焼いたもの）
        //       をそのまま適用する。最も高精度だが、抽出時に Avatar が必要。
        //       さらに外部 UnityBone CSV（ソース rest）があればレスト補正リターゲットへ。
        //   (b) Muscle    … dto.muscles（生マッスル）からローカル回転を再構成する。
        //       Avatar 不要。精度は手持ちデータで段階的に上がる:
        //         B-1 実測なし … 可動域（度）から Euler 合成する近似。
        //                        Muscle Referential の pre/post 回転・sign を省く。
        //         B-2 実測あり … 外部 UnityLimit CSV（LoadMuscleLimitCsv）の
        //                        Zero / dof毎の -1・+1 実測クォータニオンから
        //                        軸ごと Slerp して合成する。pre/post・sign を含む。
        //   二次骨（dto.bones：袖/髪/スカート等）は、どちらの方式でも常時適用する。
        //
        // BodyMode = Auto のとき、bakedBones があれば (a)、無ければ (b) を選ぶ。
        //
        // ■ 未対応（恒久メモ）
        //   Muscle 経路にはレスト補正リターゲットが無い。実測値はソースアバターの
        //   ボーンローカル軸で得た量なので、ターゲットモデルのレスト姿勢が大きく
        //   異なる場合はその分ずれる。UnityBone CSV の rest を使った整列は今後の課題。
        // ================================================================

        /// <summary>本体ボーンの適用方式。</summary>
        public enum BodySource
        {
            /// <summary>bakedBones があれば BakedBones、無ければ Muscle。</summary>
            Auto = 0,
            /// <summary>常に bakedBones を使う（無ければ本体ボーンは動かない）。</summary>
            BakedBones = 1,
            /// <summary>常に muscles から再構成する。</summary>
            Muscle = 2
        }

        /// <summary>本体ボーンの適用方式。既定は Auto。</summary>
        public BodySource BodyMode { get; set; } = BodySource.Auto;

        /// <summary>直近の ApplyFrame で実際に選ばれた方式（Auto の解決結果）。</summary>
        public BodySource ResolvedBodyMode { get; private set; } = BodySource.Auto;

        // マッピング状態
        private ModelContext _mappedModel;
        private HumanoidBoneMapping _mapping;          // Unity名 → ノード索引
        private UnityClipVirtualSkeleton _skeleton;    // 実体ノード＋仮想ミラーノード
        private List<string> _boneNames;               // ノード名（_skeleton.NodeNames と同じ並び）

        // ノード単位のデルタ（ノード自身の枠）。
        //   実体ノードは記録と同時に BonePoseData へ書く。
        //   ミラーノードは仮想鎖を解いてからでないと書けないため記録だけ行い、
        //   ApplyVirtualMirror が共役を打ち消す形に直して書き戻す。
        private Quaternion[] _nodeDeltaRot;
        private Vector3[]    _nodeDeltaPos;
        private bool[]       _nodeHasDelta;

        // リターゲット用ソース rest（外部 UnityBone CSV v2 由来）。null/空 = 未読込。
        //
        // ■ 存置理由（削除しかけた記録）
        //   この経路（ApplyRetargetedBody）へ入るのは clip.bakedBones があるときだけで、
        //   現行のエクスポータは bakedBones を出力しない（WALK00_F / rest_mascle_0 とも 0 件）。
        //   そのため Unity クリップパネル側の「バインドポーズ CSV」UI は削除した。
        //   実装本体をここに残しているのは MotionClipApplier が
        //   HasSourceRest / LoadSourceRestCsv / ClearSourceRest を委譲しており、
        //   消すと統合モーションパネルがビルドできなくなるため。
        //   統合モーション側を整理するときに、この一式ごと落とすこと。
        private struct SourceRest { public Quaternion RestW; public Vector3 RestPos; }
        private Dictionary<string, SourceRest> _sourceRest;   // Humanoid名 → rest

        /// <summary>ソース rest（バインドポーズ）読込済みなら true＝レスト補正リターゲットが有効。</summary>
        public bool HasSourceRest => _sourceRest != null && _sourceRest.Count > 0;

        // マッスル可動域・実測（外部 UnityLimit CSV v1 由来）。null/空 = 未読込。
        private class MuscleLimitEntry
        {
            public Vector3 Min;                                   // 度（dof 0,1,2 の順）
            public Vector3 Max;                                   // 度
            public bool    Measured;                              // 実測列を持つか
            public Quaternion Zero = Quaternion.identity;         // muscle 全 0 のローカル回転
            public readonly Quaternion[] MinQ = { Quaternion.identity, Quaternion.identity, Quaternion.identity };
            public readonly Quaternion[] MaxQ = { Quaternion.identity, Quaternion.identity, Quaternion.identity };
        }
        private Dictionary<string, MuscleLimitEntry> _muscleLimits;   // Humanoid 列挙名 → entry

        // ---- 既定マッスル基準（外部 CSV が無いときの自動補正）------------
        // 正準（T ポーズ）定数を、ターゲットモデルの rest 方向へ整列してから使う。
        // 整列 A はモデルごとに BuildMapping で 1 回だけ作る（毎フレーム再計算しない）。
        private struct CanonFrame
        {
            public Quaternion A;      // 正準方向 → ターゲット rest 方向 の最短弧
            public Quaternion RestW;  // ルートからの累積レスト回転
            public Quaternion RestL;  // 自ボーンのレスト・ローカル回転
        }
        private Dictionary<string, CanonFrame>       _canonFrame;       // Humanoid名 → 枠
        private Dictionary<string, MuscleLimitEntry> _canonEntryCache;  // Humanoid名 → 合成 entry

        /// <summary>UnityLimit CSV 読込済みなら true。</summary>
        public bool HasMuscleLimits => _muscleLimits != null && _muscleLimits.Count > 0;

        /// <summary>実測列を持つ行が1つ以上あるなら true。</summary>
        public bool HasMuscleMeasured
        {
            get
            {
                if (_muscleLimits == null) return false;
                foreach (var kv in _muscleLimits) if (kv.Value != null && kv.Value.Measured) return true;
                return false;
            }
        }

        /// <summary>位置スケール（Unity 空間値にそのまま乗算。既定 1）。</summary>
        public float PositionScale { get; set; } = 1f;

        /// <summary>直近の ApplyFrame で適用できたトラック数（path ＋ 本体ボーンの合計）。</summary>
        public int MatchedTrackCount { get; private set; }

        // ---- 解決状況の内訳（診断用）--------------------------------------
        // 「何本一致したか」だけでは、どの経路が効いていないのか分からないため
        // path（二次骨）と本体ボーンを分けて数える。

        /// <summary>直近の ApplyFrame で解決できた clip.bones（Transform パス）トラック数。</summary>
        public int PathMatchedCount { get; private set; }

        /// <summary>clip.bones のトラック総数。</summary>
        public int PathTrackCount { get; private set; }

        /// <summary>Muscle 経路で解決できた本体ボーン数。</summary>
        public int MuscleMatchedCount { get; private set; }

        /// <summary>クリップがマッスルで駆動している本体ボーン数（＝解決対象の母数）。</summary>
        public int MuscleTargetCount { get; private set; }

        /// <summary>Muscle 経路でモデル側ボーンに解決できなかった Humanoid 名。</summary>
        public List<string> UnresolvedMuscleBones { get; } = new List<string>();

        /// <summary>解決できなかった clip.bones のパス末尾名。</summary>
        public List<string> UnresolvedPathTracks { get; } = new List<string>();

        /// <summary>クリップ適用対象のノード総数（実体＋仮想ミラー）。</summary>
        public int BoneNodeCount => _skeleton != null ? _skeleton.Nodes.Count : 0;

        /// <summary>実体を持つノード数。</summary>
        public int RealBoneNodeCount => _skeleton != null ? _skeleton.RealNodeCount : 0;

        /// <summary>仮想ミラーノード数（ミラー側コンテキスト＋純仮想関節）。</summary>
        public int MirrorBoneNodeCount => _skeleton != null ? _skeleton.MirrorNodeCount : 0;

        /// <summary>MeshType.Bone を持たず MeshFilter ツリーを骨格として使っているか。</summary>
        public bool MeshFilterSkeleton => _skeleton != null && _skeleton.MeshFilterSkeleton;

        /// <summary>左右名を入れ替えて仮想ミラーへ補完した Humanoid 名（診断用）。</summary>
        public IReadOnlyList<string> SupplementedHumanoidNames =>
            _skeleton != null ? (IReadOnlyList<string>)_skeleton.SupplementedHumanoidNames
                              : new List<string>();

        /// <summary>診断ログの有効/無効。内容が変化したときだけ 1 回出力する。</summary>
        public bool DebugLog { get; set; } = true;
        private string _lastDiagKey;

        /// <summary>マッスル再構成の内訳ログ（ApplyFrame ごとに出る）。</summary>
        public bool MuscleDebugLog { get; set; } = true;

        /// <summary>内訳ログの対象 Humanoid 名。空なら出力しない。</summary>
        public HashSet<string> MuscleDebugBones { get; } = new HashSet<string>
        {
            "LeftUpperLeg", "LeftLowerLeg", "RightUpperLeg", "RightLowerLeg", "LeftUpperArm"
        };

        // ================================================================
        // マッピング構築
        // ================================================================

        public void BuildMapping(ModelContext model)
        {
            if (model == null) return;

            _mappedModel = model;

            // ノード表。MeshType.Bone があればそれ、無ければ MeshFilter ツリーを骨格に使い、
            // 半身モデルではミラー枝の関節を仮想ノードとして複製する。
            _skeleton  = UnityClipVirtualSkeleton.Build(model);
            _boneNames = _skeleton.NodeNames;

            _mapping = new HumanoidBoneMapping();
            _mapping.AutoMapFromEmbeddedCSV(_boneNames, fuzzyMatch: true);

            int n = _skeleton.Nodes.Count;
            _nodeDeltaRot = new Quaternion[n];
            _nodeDeltaPos = new Vector3[n];
            _nodeHasDelta = new bool[n];

            BuildCanonAlignment(model);

            if (DebugLog && _skeleton.MirrorNodeCount > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("[UnityClipApplier] 仮想ミラーボーン ")
                  .Append(_skeleton.MirrorNodeCount).Append(" 本を構築（実体 ")
                  .Append(_skeleton.RealNodeCount).Append(" 本）\n");
                if (_skeleton.SupplementedHumanoidNames.Count > 0)
                    sb.Append("  左右補完: ")
                      .Append(string.Join(", ", _skeleton.SupplementedHumanoidNames.ToArray()));
                Debug.Log(sb.ToString());
            }
        }

        // ================================================================
        // 既定マッスル基準の整列（外部 CSV が無いときの自動補正）
        // ----------------------------------------------------------------
        //   ボーン 1 本につき 3 つの量を用意する。
        //     A     … 正準（T ポーズ）のボーン方向 → ターゲットの rest 方向 の最短弧。
        //             骨組み（EnsureCanon / DirRest / QFromTo）は ApplyRetargetedBody
        //             用に既にあるものを流用する。
        //     RestL … 自ボーンのレスト・ローカル回転
        //     RestW … ルートからの累積レスト回転
        //
        //   ※ A だけでは足りない。T ポーズ化したモデルはボーン「位置」は T ポーズでも
        //     腕の傾きが rest「回転」側に残るため、方向だけ見ると A が恒等になり
        //     補正が効かない（実測との差が腕で最大 78.95 度になった）。
        //     RestW / RestL による枠の移し替えを併用して 0.72 度まで詰めてある。
        // ================================================================
        private void BuildCanonAlignment(ModelContext model)
        {
            EnsureCanon();
            EnsureCanonMuscle();

            _canonFrame      = new Dictionary<string, CanonFrame>();
            _canonEntryCache = new Dictionary<string, MuscleLimitEntry>();
            if (model == null || _skeleton == null) return;

            // ターゲットの rest 位置（モデル空間）
            var tp   = new Dictionary<string, Vector3>();
            var node = new Dictionary<string, int>();
            foreach (var kv in _canonMuscle)
            {
                int n = ResolveNode(kv.Key);
                if (n < 0) continue;
                node[kv.Key] = n;
                Matrix4x4 w = RestWorldOf(model, n);
                tp[kv.Key] = new Vector3(w.m03, w.m13, w.m23);
            }

            foreach (var kv in _canonMuscle)
            {
                var fr = new CanonFrame
                {
                    A = Quaternion.identity,
                    RestW = Quaternion.identity,
                    RestL = Quaternion.identity
                };

                if (_canonDir.TryGetValue(kv.Key, out var d0) && DirRest(tp, kv.Key, out var dt))
                    fr.A = QFromTo(d0, dt).ToUnity();

                if (node.TryGetValue(kv.Key, out int n))
                {
                    fr.RestL = _skeleton.RestLocalRotation(model, n);
                    fr.RestW = _skeleton.RestWorldRotation(model, n);
                }

                _canonFrame[kv.Key] = fr;
            }
        }

        // ノードの rest ワールド行列。
        //   スキンド（MeshType.Bone あり）は従来どおり BindPose を正本にする。
        //   MeshFilter 骨格は BindPose を持たない（mesh.csv に bindPose 行が無く単位行列のまま）ため、
        //   BoneTransform を階層に沿って累積して組む。ここを BindPose のままにすると
        //   全ボーンの rest 位置が原点になり、方向整列 A が丸ごと壊れる。
        private Matrix4x4 RestWorldOf(ModelContext model, int node)
        {
            if (!_skeleton.MeshFilterSkeleton)
            {
                var ctx = _skeleton.TargetContext(model, node);
                if (ctx != null) return ctx.BindPose.inverse;
            }
            return _skeleton.RestWorldMatrix(model, node);
        }

        // 正準定数をターゲットへ整列して MuscleLimitEntry を合成する。
        // 外部 CSV と同じ形（Zero / MinQ / MaxQ）にするため、以後の処理は
        // 実測ありの経路と完全に同一になる。
        private MuscleLimitEntry GetCanonEntry(string humanoidName)
        {
            EnsureCanonMuscle();
            if (_canonEntryCache != null &&
                _canonEntryCache.TryGetValue(humanoidName, out var cached)) return cached;
            if (!_canonMuscle.TryGetValue(humanoidName, out var cb)) return null;

            // 枠が無いときに default（全成分 0 のクォータニオン）を掴まないこと。
            CanonFrame fr;
            if (_canonFrame == null || !_canonFrame.TryGetValue(humanoidName, out fr))
            {
                fr = new CanonFrame
                {
                    A     = Quaternion.identity,
                    RestW = Quaternion.identity,
                    RestL = Quaternion.identity
                };
            }

            // 正準（T ポーズ）の量を、ターゲットのレスト枠へ移す。
            //   A  … ボーン方向の食い違い（傾いた背骨など）を吸収する
            //   P  … 親までの累積レスト回転。腕を rest 回転で下げているモデルを吸収する
            //   ΔL … 自ボーンのレスト・ローカル回転
            // 定数そのものは一切いじらない。枠を移し替えるだけである。
            Quaternion a  = fr.A;
            Quaternion ai = Quaternion.Inverse(a);
            Quaternion dL = fr.RestL;
            Quaternion w  = fr.RestW;
            Quaternion p  = w * Quaternion.Inverse(dL);
            Quaternion pi = Quaternion.Inverse(p);
            Quaternion wi = Quaternion.Inverse(w);

            var e = new MuscleLimitEntry();
            e.Measured = true;                       // 以後は実測ありと同じ経路を通す
            e.Zero     = QuatNorm(pi * (a * cb.Zero * ai) * p * dL);

            var mn = Vector3.zero;
            var mx = Vector3.zero;
            for (int dof = 0; dof < 3; dof++)
            {
                if (!cb.Has[dof]) { e.MinQ[dof] = e.Zero; e.MaxQ[dof] = e.Zero; continue; }
                Vector3 axis = wi * (a * cb.Axis[dof]);
                e.MinQ[dof] = QuatNorm(e.Zero * Quaternion.AngleAxis(cb.MinDeg[dof], axis));
                e.MaxQ[dof] = QuatNorm(e.Zero * Quaternion.AngleAxis(cb.MaxDeg[dof], axis));
                mn[dof] = cb.MinDeg[dof];
                mx[dof] = cb.MaxDeg[dof];
            }
            e.Min = mn;
            e.Max = mx;

            if (_canonEntryCache != null) _canonEntryCache[humanoidName] = e;
            return e;
        }

        /// <summary>
        /// Transform パス末尾（Unity 名）→ ノード索引。無ければ -1。
        ///
        /// 解決順:
        ///   1) モデルの per-bone Humanoid 割当（model.HumanoidMapping が正本）。
        ///      半身モデルで欠けている右側は UnityClipVirtualSkeleton が
        ///      左右名を入れ替えて仮想ミラーノードへ補完済み。
        ///   2) 埋め込み対応表（EmbeddedMapping）
        ///   3) ノード名へのあいまい照合
        /// </summary>
        public int ResolveNode(string path)
        {
            if (_skeleton == null) return -1;
            string unityName = LastSegment(path);
            if (string.IsNullOrEmpty(unityName)) return -1;

            int n = _skeleton.NodeOfHumanoid(unityName);
            if (n >= 0) return n;

            if (_mapping != null)
            {
                n = _mapping.Get(unityName);
                if (n < 0)
                {
                    string key = UnityClipVirtualSkeleton.NormalizeHumanoidName(unityName);
                    if (!string.Equals(key, unityName)) n = _mapping.Get(key);
                }
                if (n >= 0 && n < _skeleton.Nodes.Count) return n;
            }

            n = HumanoidBoneMapping.FindBoneByAliases(
                _boneNames, new List<string> { unityName }, fuzzyMatch: true);
            return (n >= 0 && n < _skeleton.Nodes.Count) ? n : -1;
        }

        /// <summary>
        /// Transform パス末尾（Unity 名）→ MeshContextList インデックス。無ければ -1。
        /// 実体を持たない純仮想ミラーノードに解決した場合も -1 を返す。
        /// </summary>
        public int ResolveMasterIndex(string path)
        {
            int n = ResolveNode(path);
            if (n < 0) return -1;
            return _skeleton.Nodes[n].ContextIndex;
        }

        /// <summary>ノード名。範囲外なら空文字。</summary>
        public string NodeName(int node)
            => (_skeleton != null && node >= 0 && node < _skeleton.Nodes.Count)
                ? _skeleton.Nodes[node].Name : string.Empty;

        // ================================================================
        // 適用
        // ================================================================

        public void ApplyFrame(ModelContext model, UnityClipDTO clip, float timeSec)
        {
            if (model == null || clip == null) return;
            if (_mappedModel != model || _mapping == null) BuildMapping(model);

            ClearNodeDeltas();

            int matched = 0;

            // 二次骨（袖/髪/スカート等）: どちらの方式でも常時適用
            PathMatchedCount = 0;
            PathTrackCount   = clip.bones != null ? clip.bones.Count : 0;
            UnresolvedPathTracks.Clear();
            if (clip.bones != null)
            {
                foreach (var track in clip.bones)
                {
                    int n = ApplyTrackAt(model, track, timeSec);
                    matched += n;
                    if (n > 0) PathMatchedCount++;
                    else if (track != null) UnresolvedPathTracks.Add(LastSegment(track.path));
                }
            }

            // 本体ボーン: 方式を解決してから適用する
            bool hasBaked = clip.bakedBones != null && clip.bakedBones.Count > 0;
            BodySource mode = BodyMode;
            if (mode == BodySource.Auto)
                mode = hasBaked ? BodySource.BakedBones : BodySource.Muscle;
            ResolvedBodyMode = mode;

            if (mode == BodySource.BakedBones)
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
            LogDiagnosticsIfChanged();

            // 1 パス目: 実体ノードのデルタを反映してワールドを確定させる。
            model.ComputeWorldMatrices();

            // 2 パス目: 確定した実体側ワールドを足場に仮想ミラー鎖を解き、
            //           ミラー側コンテキストへ書き戻してからもう一度組み直す。
            if (ApplyVirtualMirror(model))
                model.ComputeWorldMatrices();
        }

        // 1 トラックを timeSec でサンプルして適用。適用できたら 1。
        private int ApplyTrackAt(ModelContext model, UnityBoneTrackDTO track, float timeSec)
        {
            if (track == null || track.keys == null || track.keys.Count == 0) return 0;
            int node = ResolveNode(track.path);
            if (node < 0) return 0;
            if (_skeleton.SourceContext(model, node) == null) return 0;

            Vector3? sPos = SamplePosition(track, timeSec);
            Quaternion? sRot = SampleRotation(track, timeSec);

            // ベース（rest ローカル・ノード自身の枠）。
            // ミラーノードでは鏡像化済みの値になる（クリップは右半身の枠で来る）。
            Matrix4x4 baseMat = _skeleton.RestLocalMatrixOwn(model, node);
            _skeleton.RestLocalTRSOwn(model, node, out Vector3 restPos, out Quaternion restRot);

            Vector3 localPos = sPos.HasValue ? sPos.Value * PositionScale : restPos;
            Quaternion localRot = sRot.HasValue ? sRot.Value : restRot;

            // clip 絶対ローカル → デルタ（rest^-1 × clipLocal）
            Matrix4x4 clipLocal = Matrix4x4.TRS(localPos, localRot, Vector3.one);
            Matrix4x4 deltaMat = baseMat.inverse * clipLocal;
            Vector3 deltaPos = new Vector3(deltaMat.m03, deltaMat.m13, deltaMat.m23);
            SetNodeDelta(model, node, deltaPos, deltaMat.rotation);
            return 1;
        }

        // (b) muscles から本体ボーンのローカル回転を再構成して適用。
        //
        //   経路は 1 本だけである。Zero / MinQ / MaxQ を使う経路に統一した。
        //     dof 毎に「Zero → ±1 のローカル回転」からデルタ
        //       full_d = Zero^-1 · Extreme_d
        //     を作り、Slerp(identity, full_d, |v|) で v 倍したものを 3 dof 合成して d(v) を得る。
        //
        //   Zero / MinQ / MaxQ の出どころは 2 通りあるが、以後の計算は同一:
        //     (1) 外部 UnityLimit CSV の Measured 行（ソースアバターの実測）
        //     (2) 無いときは CanonMuscleTable（T ポーズ基準の Unity 定義値）を
        //         ターゲットの rest 方向へ整列して合成した既定 entry
        //     ※ かつて存在した「dof を X/Y/Z へ直接入れて Quaternion.Euler で組む」
        //       近似経路は削除した。ひざが Z 軸回りに曲がる原因だった。復活させないこと。
        //
        //   ■ Zero はモデルのレスト姿勢ではない（重要）
        //     Unity のマッスル 0 は、モデルのレストが T ポーズであっても T ポーズにならない。
        //     ひざ・ひじ・股関節が曲がった Unity 定義の固定ポーズになる。
        //     例: ひざは一方向にしか曲がらないため Stretch の範囲 -80〜+80 の中央（0）が
        //     「80 度曲がった位置」、+1 が伸展位になる。
        //
        //     Unity 側のローカル回転は        L(v) = Zero · d(v)
        //     PolyLing が入れるのはレスト差分  D    = RestL^-1 · Zero · d(v)
        //
        //     RestL は PolyLing 自身が持つレスト・ローカル回転（ctx.BoneTransform）。
        //     d(v) をそのまま入れると Zero→rest 分（ひざで約 80 度）が丸ごと過剰になり、
        //     ひざが逆に曲がる。この補正は実測・既定のどちらでも常時必要である。
        //
        //   rest からのデルタとして BonePoseData に載せる。
        private int ApplySelfMuscle(ModelContext model, UnityClipDTO clip, float timeSec)
        {
            if (clip.muscles == null || clip.muscles.Count == 0) return 0;

            var muscleByName = new Dictionary<string, UnityMuscleTrackDTO>();
            foreach (var m in clip.muscles)
                if (m != null && !string.IsNullOrEmpty(m.name)) muscleByName[m.name] = m;

            var muscleNames = HumanTrait.MuscleName;
            int boneCount = HumanTrait.BoneCount;
            int matched = 0;

            MuscleMatchedCount = 0;
            MuscleTargetCount  = 0;
            UnresolvedMuscleBones.Clear();

            for (int bi = 0; bi < boneCount; bi++)
            {
                string boneName = HumanTrait.BoneName[bi];          // 例 "Left Upper Arm"（空白入り）
                string key = boneName.Replace(" ", string.Empty);    // 対応表キー "LeftUpperArm"

                // このボーンをクリップが実際に駆動しているか（dof のいずれかにトラックがあるか）。
                // 駆動していないボーンは母数に入れない（未解決として報告しない）。
                bool driven = false;
                for (int dof = 0; dof < 3 && !driven; dof++)
                {
                    int mi0 = HumanTrait.MuscleFromBone(bi, dof);
                    if (mi0 < 0 || muscleNames == null || mi0 >= muscleNames.Length) continue;
                    if (muscleByName.ContainsKey(muscleNames[mi0])) driven = true;
                }
                if (!driven) continue;
                MuscleTargetCount++;

                int k = ResolveNode(key);
                if (k < 0) k = ResolveNode(boneName);
                if (k < 0)
                {
                    UnresolvedMuscleBones.Add(key);
                    continue;
                }

                // 姿勢の正本は実体側コンテキスト（ミラーノードは相方）。
                var ctx = _skeleton.SourceContext(model, k);
                if (ctx == null)
                {
                    UnresolvedMuscleBones.Add(key + "(ctx=null)");
                    continue;
                }

                // 外部 UnityLimit CSV の行（Humanoid 列挙名で引く）。
                // 無い / 実測列を持たない場合は、T ポーズ基準の Unity 定義値から
                // 既定 entry を合成する（自動補正）。
                MuscleLimitEntry lim = null;
                if (_muscleLimits != null) _muscleLimits.TryGetValue(key, out lim);
                bool fromCsv = lim != null && lim.Measured;
                if (!fromCsv) lim = GetCanonEntry(key);
                if (lim == null)
                {
                    UnresolvedMuscleBones.Add(key + "(既定なし)");
                    continue;
                }

                Quaternion delta = Quaternion.identity;
                bool any = false;

                // 内訳ログ（対象ボーンのみ）
                bool dbg = MuscleDebugLog && MuscleDebugBones != null && MuscleDebugBones.Contains(key);
                System.Text.StringBuilder dsb = null;
                if (dbg)
                {
                    dsb = new System.Text.StringBuilder();
                    dsb.Append("[UnityClipApplier/muscle] ").Append(key)
                       .Append("  t=").Append(timeSec.ToString("F3"))
                       .Append("  src=").Append(fromCsv ? "CSV実測" : "既定(Tポーズ基準)").Append('\n');
                    dsb.Append("   Zero  ").Append(AxAng(lim.Zero))
                       .Append("  min(deg)=").Append(lim.Min)
                       .Append(" max(deg)=").Append(lim.Max).Append('\n');
                }

                for (int dof = 0; dof < 3; dof++)
                {
                    int mi = HumanTrait.MuscleFromBone(bi, dof);
                    if (mi < 0 || muscleNames == null || mi >= muscleNames.Length)
                    {
                        if (dbg) dsb.Append("   dof").Append(dof).Append(": マッスル無し\n");
                        continue;
                    }
                    if (!muscleByName.TryGetValue(muscleNames[mi], out var mt))
                    {
                        if (dbg) dsb.Append("   dof").Append(dof).Append(": クリップにトラック無し (")
                                    .Append(muscleNames[mi]).Append(")\n");
                        continue;
                    }

                    float v = SampleWeight(mt, timeSec);                 // 正規化値 [-1,1]

                    // Zero 基準のデルタを |v| だけ効かせる（v=0 で identity）
                    Quaternion ext  = v >= 0f ? lim.MaxQ[dof] : lim.MinQ[dof];
                    Quaternion full = Quaternion.Inverse(lim.Zero) * ext;
                    Quaternion d    = Quaternion.Slerp(Quaternion.identity, full, Mathf.Min(1f, Mathf.Abs(v)));
                    delta = delta * d;

                    if (dbg)
                    {
                        dsb.Append("   dof").Append(dof).Append(' ').Append(muscleNames[mi])
                           .Append("  v=").Append(v.ToString("F4"))
                           .Append("  side=").Append(v >= 0f ? "Max" : "Min").Append('\n');
                        dsb.Append("      MinQ ").Append(AxAng(lim.MinQ[dof]))
                           .Append("   MaxQ ").Append(AxAng(lim.MaxQ[dof])).Append('\n');
                        dsb.Append("      full ").Append(AxAng(full))
                           .Append("   ->d ").Append(AxAng(d)).Append('\n');
                    }
                    any = true;
                }
                if (!any) continue;

                // Zero（マッスル0）とモデルのレスト姿勢の差を打ち消す。常時適用する。
                //   D = RestL^-1 · Zero · d(v)
                // Unity のマッスル 0 は T ポーズではなく、ひざ・ひじ・股関節が曲がった
                // 固定ポーズである。この補正を外すとひざが逆に曲がる。
                // 実測・既定のどちらの entry でも必要。条件を付けないこと。
                // ミラーノードでは自身の枠（右半身の枠）で見た rest を使う。
                Quaternion restL = _skeleton.RestLocalRotation(model, k);
                Quaternion zeroFix = Quaternion.Inverse(restL) * lim.Zero;
                delta = zeroFix * delta;

                Quaternion applied = delta;

                if (dbg)
                {
                    dsb.Append("   RestL ").Append(AxAng(restL))
                       .Append("   RestL^-1*Zero ").Append(AxAng(zeroFix)).Append('\n');
                    dsb.Append("   合成 delta ").Append(AxAng(applied)).Append('\n');
                    // モデル空間へ写した向き（R = BoneModelRotation）。ボーンがどちらへ回るかの確認用。
                    Quaternion R = ctx.BoneModelRotation;
                    Quaternion dWorld = R * applied * Quaternion.Inverse(R);
                    dsb.Append("   R(BoneModelRotation) ").Append(AxAng(R))
                       .Append("   モデル空間 ").Append(AxAng(dWorld)).Append('\n');
                    Debug.Log(dsb.ToString());
                }

                // rest からのデルタ（位置は変えない）
                SetNodeDelta(model, k, Vector3.zero, applied);
                MuscleMatchedCount++;
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

        // ================================================================
        // 正準マッスル基準テーブル（T ポーズ基準・Unity の定義値）
        // ----------------------------------------------------------------
        //   ファイル冒頭の恒久メモを必ず読むこと。ここは導出値ではなく定義値である。
        //
        //   CanonMuscleTable 1 行の書式（'|' 区切り）:
        //     Humanoid名 | Zero(x y z w) | dof0 | dof1 | dof2
        //   dof の書式:
        //     軸x 軸y 軸z 最小度 最大度      （そのボーンに該当マッスルが無ければ "-"）
        //   軸はボーン局所（＝ T ポーズではモデル空間）での回転軸。
        //   最小度・最大度は Zero からの相対角。HumanTrait の可動域とは一致しない
        //   （ツイストは捩りボーンへ分配されるため実効はおよそ半分になる）。
        //
        //   CanonDirTable は T ポーズでの「自ボーン → 正準子ボーン」方向。
        //   ターゲットモデルの rest 方向との最短弧を取って軸を整列するのに使う。
        // ================================================================

        private class CanonMuscleBone
        {
            public Quaternion Zero = Quaternion.identity;
            public readonly bool[]    Has    = new bool[3];
            public readonly Vector3[] Axis   = new Vector3[3];
            public readonly float[]   MinDeg = new float[3];
            public readonly float[]   MaxDeg = new float[3];
        }

        private static Dictionary<string, CanonMuscleBone> _canonMuscle;
        private static Dictionary<string, Vector3>         _canonDir;

        private static readonly string[] CanonMuscleTable =
        {
            "Hips|0 0 0 1|-|-|-",
            "Spine|0 0 0 1|0 1 0 -40 40|0 0 -1 -40 40|-1 0 0 -40 40",
            "Chest|0 0 0 1|0 1 0 -40 40|0 0 -1 -40 40|-1 0 0 -40 40",
            "UpperChest|0 0 0 1|0 1 0 -20 20|0 0 -1 -20 20|-1 0 0 -20 20",
            "Neck|0 0 0 1|0 1 0 -40 40|0 0 -1 -40 40|-1 0 0 -40 40",
            "Head|0 0 0 1|0 1 0 -40 40|0 0 -1 -40 40|-1 0 0 -40 40",
            "LeftEye|0 0 0 1|-|0 -1 0 -20 20|-1 0 0 -10 15",
            "RightEye|0 0 0 1|-|0 1 0 -20 20|-1 0 0 -10 15",
            "LeftShoulder|0 0 0 1|-|-0.0373 -0.9993 0 -15 15|0 0 -1 -15 30",
            "LeftUpperArm|0 0.24421 0.331689 0.911232|-1 0 0 -45 45|0 -1 0 -100 100|0 0 -1 -60 100",
            "LeftLowerArm|0 0.642743 0 0.766082|-1 0 0 -45 45|-|0 -1 0 -80 80",
            "LeftHand|0 0 0 1|-|0 -1 0 -40 40|0 0 -1 -80 80",
            "RightShoulder|0 0 0 1|-|-0.0373 0.9993 0 -15 15|0 0 1 -15 30",
            "RightUpperArm|0 -0.24421 -0.331689 0.911232|-1 0 0 -45 45|0 1 0 -100 100|0 0 1 -60 100",
            "RightLowerArm|0 -0.642743 0 0.766082|-1 0 0 -45 45|-|0 1 0 -80 80",
            "RightHand|0 0 0 1|-|0 1 0 -40 40|0 0 1 -80 80",
            "LeftUpperLeg|-0.258865 0 0 0.965914|-0.025 -0.9997 0 -30 30|0 0 -1 -60 60|0.9997 -0.025 0 -90 50",
            "LeftLowerLeg|0.642743 0 0 0.766082|0 -1 0 -45 45|-|-1 0 0 -80 80",
            "LeftFoot|0 0 0 1|-|0 0 -1 -30 30|1 0 0 -50 50",
            "LeftToes|0 0 0 1|-|-|1 0 0 -50 50",
            "RightUpperLeg|-0.258865 0 0 0.965914|-0.025 0.9997 0 -30 30|0 0 1 -60 60|0.9997 0.025 0 -90 50",
            "RightLowerLeg|0.642743 0 0 0.766082|0 1 0 -45 45|-|-1 0 0 -80 80",
            "RightFoot|0 0 0 1|-|0 0 1 -30 30|1 0 0 -50 50",
            "RightToes|0 0 0 1|-|-|1 0 0 -50 50",
            "LeftThumbProximal|1e-06 0.123091 0.123092 0.984732|-|-0.6357 0 -0.772 -25 25|-0.2485 0.9468 0.2047 -20 20",
            "LeftThumbIntermediate|0 -0.196116 0 0.980581|-|-|-0.2502 0.9461 0.2057 -40 35",
            "LeftThumbDistal|0 -0.196116 0 0.980581|-|-|-0.2502 0.9461 0.2057 -40 35",
            "LeftIndexProximal|1e-06 0.076402 0.286508 0.955027|-|0.0235 0.9997 0 -20 20|0 0 -1 -50 50",
            "LeftIndexIntermediate|0 0 0.313378 0.949629|-|-|-0.0001 0 -1 -45 45",
            "LeftIndexDistal|0 0 0.313378 0.949629|-|-|-0.0001 0 -1 -45 45",
            "LeftMiddleProximal|0 0.038285 0.287137 0.957124|-|0.0235 0.9997 0 -7.5 7.5|0 0 -1 -50 50",
            "LeftMiddleIntermediate|0 0 0.313378 0.949629|-|-|-0.0001 0 -1 -45 45",
            "LeftMiddleDistal|0 0 0.313378 0.949629|-|-|-0.0001 0 -1 -45 45",
            "LeftRingProximal|0 -0.038285 0.287137 0.957124|-|-0.0235 -0.9997 0 -7.5 7.5|0 0 -1 -50 50",
            "LeftRingIntermediate|0 0 0.313378 0.949629|-|-|-0.0002 0 -1 -45 45",
            "LeftRingDistal|0 0 0.313378 0.949629|-|-|-0.0002 0 -1 -45 45",
            "LeftLittleProximal|-1e-06 -0.076402 0.286508 0.955027|-|-0.0235 -0.9997 0 -20 20|0 0 -1 -50 50",
            "LeftLittleIntermediate|0 0 0.313378 0.949629|-|-|-0.0001 0 -1 -45 45",
            "LeftLittleDistal|0 0 0.313378 0.949629|-|-|-0.0001 0 -1 -45 45",
            "RightThumbProximal|1e-06 -0.123091 -0.123092 0.984732|-|-0.6357 0 0.772 -25 25|-0.2485 -0.9468 -0.2047 -20 20",
            "RightThumbIntermediate|0 0.196116 0 0.980581|-|-|-0.2502 -0.9461 -0.2057 -40 35",
            "RightThumbDistal|0 0.196116 0 0.980581|-|-|-0.2502 -0.9461 -0.2057 -40 35",
            "RightIndexProximal|1e-06 -0.076402 -0.286508 0.955027|-|0.0235 -0.9997 0 -20 20|0 0 1 -50 50",
            "RightIndexIntermediate|0 0 -0.313378 0.949629|-|-|-0.0001 0 1 -45 45",
            "RightIndexDistal|0 0 -0.313378 0.949629|-|-|-0.0001 0 1 -45 45",
            "RightMiddleProximal|0 -0.038285 -0.287137 0.957124|-|0.0235 -0.9997 0 -7.5 7.5|0 0 1 -50 50",
            "RightMiddleIntermediate|0 0 -0.313378 0.949629|-|-|-0.0001 0 1 -45 45",
            "RightMiddleDistal|0 0 -0.313378 0.949629|-|-|-0.0001 0 1 -45 45",
            "RightRingProximal|0 0.038285 -0.287137 0.957124|-|-0.0235 0.9997 0 -7.5 7.5|0 0 1 -50 50",
            "RightRingIntermediate|0 0 -0.313378 0.949629|-|-|-0.0002 0 1 -45 45",
            "RightRingDistal|0 0 -0.313378 0.949629|-|-|-0.0002 0 1 -45 45",
            "RightLittleProximal|-1e-06 0.076402 -0.286508 0.955027|-|-0.0235 0.9997 0 -20 20|0 0 1 -50 50",
            "RightLittleIntermediate|0 0 -0.313378 0.949629|-|-|-0.0001 0 1 -45 45",
            "RightLittleDistal|0 0 -0.313378 0.949629|-|-|-0.0001 0 1 -45 45",
        };

        private static readonly string[] CanonDirTable =
        {
            "Chest|0 1 0",
            "Hips|0 1 0",
            "LeftIndexIntermediate|-0.9998 0.0218 0.0001",
            "LeftIndexProximal|-0.9997 0.0235 0",
            "LeftLittleIntermediate|-0.9998 0.0218 0.0001",
            "LeftLittleProximal|-0.9997 0.0235 0",
            "LeftLowerArm|-1 0 0",
            "LeftLowerLeg|0 -1 0",
            "LeftMiddleIntermediate|-0.9998 0.0218 0.0001",
            "LeftMiddleProximal|-0.9997 0.0235 0",
            "LeftRingIntermediate|-0.9998 0.0218 0.0002",
            "LeftRingProximal|-0.9997 0.0235 0",
            "LeftShoulder|-0.9993 0.0373 0",
            "LeftThumbIntermediate|-0.7309 -0.3239 0.6008",
            "LeftThumbProximal|-0.7308 -0.322 0.6018",
            "LeftUpperArm|-1 0 0",
            "LeftUpperLeg|-0.025 -0.9997 0",
            "Neck|0 1 0",
            "RightIndexIntermediate|0.9998 0.0218 0.0001",
            "RightIndexProximal|0.9997 0.0235 0",
            "RightLittleIntermediate|0.9998 0.0218 0.0001",
            "RightLittleProximal|0.9997 0.0235 0",
            "RightLowerArm|1 0 0",
            "RightLowerLeg|0 -1 0",
            "RightMiddleIntermediate|0.9998 0.0218 0.0001",
            "RightMiddleProximal|0.9997 0.0235 0",
            "RightRingIntermediate|0.9998 0.0218 0.0002",
            "RightRingProximal|0.9997 0.0235 0",
            "RightShoulder|0.9993 0.0373 0",
            "RightThumbIntermediate|0.7309 -0.3239 0.6008",
            "RightThumbProximal|0.7308 -0.322 0.6018",
            "RightUpperArm|1 0 0",
            "RightUpperLeg|0.025 -0.9997 0",
            "Spine|0 1 0",
            "UpperChest|0 1 0",
        };

        private static void EnsureCanonMuscle()
        {
            if (_canonMuscle != null) return;

            var m = new Dictionary<string, CanonMuscleBone>();
            foreach (var row in CanonMuscleTable)
            {
                var col = row.Split('|');
                if (col.Length < 5) continue;
                var cb = new CanonMuscleBone();

                var z = col[1].Split(' ');
                if (z.Length >= 4)
                    cb.Zero = QuatNorm(new Quaternion(ParseF(z[0]), ParseF(z[1]), ParseF(z[2]), ParseF(z[3])));

                for (int dof = 0; dof < 3; dof++)
                {
                    string f = col[2 + dof];
                    if (f == "-") continue;
                    var p = f.Split(' ');
                    if (p.Length < 5) continue;
                    var ax = new Vector3(ParseF(p[0]), ParseF(p[1]), ParseF(p[2]));
                    if (ax.sqrMagnitude <= 1e-12f) continue;
                    cb.Has[dof]    = true;
                    cb.Axis[dof]   = ax.normalized;
                    cb.MinDeg[dof] = ParseF(p[3]);
                    cb.MaxDeg[dof] = ParseF(p[4]);
                }
                m[col[0]] = cb;
            }
            _canonMuscle = m;

            var d = new Dictionary<string, Vector3>();
            foreach (var row in CanonDirTable)
            {
                var col = row.Split('|');
                if (col.Length < 2) continue;
                var p = col[1].Split(' ');
                if (p.Length < 3) continue;
                var v = new Vector3(ParseF(p[0]), ParseF(p[1]), ParseF(p[2]));
                if (v.sqrMagnitude <= 1e-12f) continue;
                d[col[0]] = v.normalized;
            }
            _canonDir = d;
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
            int node = ResolveNode(canonName);
            if (node < 0) return false;
            var ctx = _skeleton.SourceContext(model, node);
            if (ctx == null) return false;

            // outLocal は「ターゲット rest 回転 = identity」前提（motion_timeline と同じ MMD 規約）の
            // CANON 親相対ローカル回転。PMX ボーンはボーン整列の rest 回転 R(=BoneModelRotation, 非identity)
            // を持つため、上書きすると R を捨てて全ボーンが誤配向になる。
            // VMDApplier と同じく delta = R^-1 * outLocal * R を rest（baseMat）へのデルタとして適用する（回転のみ）。
            Q R = Q.From(ctx.BoneModelRotation);
            Q delta = QMul(QConj(R), QMul(outLocal, R));
            SetNodeDelta(model, node, Vector3.zero, delta.ToUnity());
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
