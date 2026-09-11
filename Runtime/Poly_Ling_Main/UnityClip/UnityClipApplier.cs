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
//
// 【分割先】このファイルから次へ分けてある。
//   UnityClipApplier.Apply.cs    Unity クリップ適用：フレームの適用とレスト補正（Unity→MMD 完全リターゲット）。
//   UnityClipApplier.Canon.cs    Unity クリップ適用：正準マッスル基準テーブルと正準定数の公開。
//   UnityClipApplier.Support.cs  Unity クリップ適用：外部 CSV・診断ログ・ノード単位のデルタ・仮想ミラー鎖・ワールド行列・サンプリング。

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
        //   可動端（Min/Max）の出所は、Muscle 経路で次の順に決まる:
        //     1) 外部 UnityLimit CSV の実測（LoadMuscleLimitCsv 済みのボーン）
        //     2) モデル側 per-bone 可動域（MeshObject.HumanLimit。
        //        UseDefaultValues == false のボーンだけ）
        //     3) CanonMuscleTable の定数
        //   回転軸と muscle=0 の姿勢は常に定数側を使う。HumanLimitData は
        //   可動端しか持たないため、そこだけを差し替える。
        //   2) の控えは BuildMapping（→ BuildCanonAlignment）で作る。
        //   可動域を編集したあとは BuildMapping を呼び直すまで反映されない。
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

        // 直近 ApplyFrame 時点のノードのワールド行列。
        // 純仮想ミラー関節（ContextIndex < 0）は書き先の MeshContext を持たないため、
        // SolveMirrorWorld の結果をここに控えないと外から知る手段が無い。
        private Matrix4x4[] _nodeWorld;
        private bool        _nodeWorldValid;
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
            public bool    FromModelLimit;                        // 可動端をモデル側 HumanLimit で置いたか
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

        // ---- モデル側 per-bone 可動域（MeshObject.HumanLimit）-------------
        // 正典は MeshObject.HumanLimit（ラジアン）。ここへは度で控える。
        // UseDefaultValues == true のボーンは入れない。入れないことが
        // 「定数（CanonMuscleTable）をそのまま使う」の意味になる。
        //
        // 【なぜ定数を丸ごと置き換えないか】
        //   CanonMuscleTable の可動端は HumanTrait の可動域と一致しない
        //   （ツイストが捩りボーンへ分配されるため実効がおよそ半分）。
        //   既定のボーンまでモデル値へ替えると、これまで出ていた姿勢が変わる。
        //   よって「明示的に既定を外したボーンだけ」差し替える。
        //
        // 【軸と Zero は差し替えない】
        //   HumanLimitData が持つのは可動端だけで、回転軸と muscle=0 の姿勢は
        //   持たない。軸 cb.Axis と Zero は定数のまま使う。
        private struct ModelLimitDeg
        {
            public Vector3 Min;   // 度（dof 0,1,2 の順）
            public Vector3 Max;   // 度
        }
        private Dictionary<string, ModelLimitDeg> _modelLimitDeg;

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

        /// <summary>
        /// 構築済みの仮想骨格（実体ノード＋仮想ミラーノード）。BuildMapping のあとに有効。
        /// Humanoid の左右補完もここが持つ（UnityClipVirtualSkeleton.BuildHumanoidMap）。
        /// </summary>
        public UnityClipVirtualSkeleton Skeleton => _skeleton;

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
            _modelLimitDeg   = new Dictionary<string, ModelLimitDeg>();
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

                // モデル側の可動域。姿勢と同じく実体側コンテキストから取る
                // （ミラーノードは相方のボーンが正本）。
                var hl = _skeleton.SourceContext(model, n)?.MeshObject?.HumanLimit;
                if (hl != null && !hl.UseDefaultValues)
                {
                    _modelLimitDeg[kv.Key] = new ModelLimitDeg
                    {
                        Min = hl.Min * Mathf.Rad2Deg,
                        Max = hl.Max * Mathf.Rad2Deg,
                    };
                }
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

        /// <summary>
        /// 正準（T ポーズ）のボーン方向 → ターゲット rest 方向 の最短弧 A。
        /// BuildMapping を通したあとだけ引ける。引けなければ false（恒等を返す）。
        ///
        /// VMD → VRMA の書き出し（VmdVrmAnimationExport）が、モデルのレスト姿勢を
        /// 正準 T ポーズへ揃えるために読む。値の算出は BuildCanonAlignment が正本で、
        /// ここでは複製しない。
        /// </summary>
        public bool TryGetCanonAlignment(string humanoidName, out Quaternion alignment)
        {
            alignment = Quaternion.identity;
            if (_canonFrame == null || string.IsNullOrEmpty(humanoidName)) return false;

            string key = UnityClipVirtualSkeleton.NormalizeHumanoidName(humanoidName);
            if (string.IsNullOrEmpty(key)) return false;
            if (!_canonFrame.TryGetValue(key, out var fr)) return false;

            alignment = fr.A;
            return true;
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

            // 可動端だけモデル側の値へ差し替える（既定を外したボーンのみ）。
            // 短絡評価の中で out var を宣言すると後段で確定代入にならないため、
            // TryGetValue は if で分けて呼ぶ。
            ModelLimitDeg ml = default;
            bool useModelLimit = false;
            if (_modelLimitDeg != null)
                useModelLimit = _modelLimitDeg.TryGetValue(humanoidName, out ml);

            var e = new MuscleLimitEntry();
            e.Measured       = true;                 // 以後は実測ありと同じ経路を通す
            e.FromModelLimit = useModelLimit;
            e.Zero     = QuatNorm(pi * (a * cb.Zero * ai) * p * dL);

            var mn = Vector3.zero;
            var mx = Vector3.zero;
            for (int dof = 0; dof < 3; dof++)
            {
                if (!cb.Has[dof]) { e.MinQ[dof] = e.Zero; e.MaxQ[dof] = e.Zero; continue; }
                Vector3 axis = wi * (a * cb.Axis[dof]);

                float minDeg = useModelLimit ? ml.Min[dof] : cb.MinDeg[dof];
                float maxDeg = useModelLimit ? ml.Max[dof] : cb.MaxDeg[dof];

                e.MinQ[dof] = QuatNorm(e.Zero * Quaternion.AngleAxis(minDeg, axis));
                e.MaxQ[dof] = QuatNorm(e.Zero * Quaternion.AngleAxis(maxDeg, axis));
                mn[dof] = minDeg;
                mx[dof] = maxDeg;
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
    }
}
