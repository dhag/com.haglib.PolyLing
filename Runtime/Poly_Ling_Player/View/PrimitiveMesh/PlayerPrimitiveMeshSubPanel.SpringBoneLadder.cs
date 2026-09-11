// PlayerPrimitiveMeshSubPanel.SpringBoneLadder.cs
// 図形生成サブパネル：はしごから揺れもの用のボーン鎖を作る。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【他の揺れものボーンとの違い】
//   1本・円筒・回転体は数値（半径・高さ・折れ線）から置く。こちらは既にある
//   メッシュのはしごから置く。位置が頂点そのものに載るので、頂点とボーンの
//   対応が決まり、そのままウェイトになる。
//
// 【取り込み】
//   フリル・パイプのように点列を控えることはしない。ボーンは1回置くだけで
//   作り直しの経路が無いため、控える意味が無い。ここで持つのは
//   「取り込み元・取り込み方・辞書名」だけで、実際の取り直しは
//   コマンドを受けたディスパッチャ側が BeltAcquire.AcquireStrips で行う。
//
// 【段の横断】
//   BeltAcquire に渡す crossRows は並べ方で決まる（スカート系＝横断あり）。
//   別のトグルにすると、モードと食い違った取り込みができてしまう。
//
// 【間引き】
//   段ストライド（段を何段に1つ）と本ストライド（はしご／列を何本に1つ）で
//   ボーンの数を減らす。はしご自体は間引かず、ボーンを置く骨組みだけを粗くする。
//   本方向は「間引く」（間の線を左右の鎖で補間）と「まとめる」（平均位置に鎖1本）
//   から選ぶ。考え方の全体は SpringBoneLadderPlacer.cs の冒頭にまとめてある。
//
// 【数え方】
//   情報欄の本数は SpringBoneLadderPlacer.CountPlan に数えさせる。
//   ここで別に数えると、間引きの決まりが本体と食い違ったときに気づけない。
//
// 【ウェイト】
//   塗る先は取り込み元のメッシュ＝はしご自身。はしごから作ったフリルや
//   パイプは別メッシュで、頂点はここには無い。
//
// 【ミラー】
//   「ミラー側にも鎖を作る」を立てると、同じ鎖をミラー行列で写してもう一組作り、
//   左右のボーンへ MirrorBoneIndex を双方向に立てる。立てないとミラー側の
//   メッシュは実体側と同じボーン番号を引く（MirrorPair.BuildBonePairMap は
//   MirrorBoneIndex からしか対応表を作らない）。
//   ウェイトを塗るのは実体側だけで、ミラー側へは MirrorPair.SyncBoneWeights が写す。
//
// 【オブジェクトグループ】
//   「残す」を立てると、取り込み元とパラメータがグループに控えられ、
//   出力として鎖の根元ボーンが記録される。あとからはしごを直して作り直すと、
//   ボーンを作らず位置だけが流し込まれる（ObjectId が保たれるので、
//   揺れ方の設定・選択辞書・はしごのウェイトが切れない）。
//   ただし鎖の本数や段数が変わるほど直したときは作り直しになり、
//   古い鎖はモデルに残る。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Tools.SpringBoneRig;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private readonly MeshSourcePick _sbLadderPick = new MeshSourcePick();

        private SpringBoneLadderMode _sbLadderMode   = SpringBoneLadderMode.Strand;
        private BeltAcquireMethod    _sbLadderMethod = BeltAcquireMethod.AutoLadder;
        private string _sbLadderSetName = "";
        private bool   _sbLadderReverse;
        private bool   _sbLadderPaint = true;

        /// <summary>取り込み元とパラメータをオブジェクトグループとして残すか。</summary>
        private bool   _sbLadderKeepAsGroup;

        /// <summary>ミラー側にも鎖を作るか。</summary>
        private bool   _sbLadderMakeMirror;

        /// <summary>取り付け先を取り込み元の階層親から自動で決めるか。既定 true。</summary>
        private bool   _sbLadderAutoAttach = true;

        /// <summary>ミラーのトグル。取り込み元がミラーペアの実体側のときだけ使える。</summary>
        [UiControl(Ignore = true)]
        private VisualElement _sbLadderMirrorRow;

        /// <summary>段ストライド。1 で間引かない。</summary>
        private int _sbLadderRungStride = 1;

        /// <summary>本ストライド。1 で間引かない。</summary>
        private int _sbLadderChainStride = 1;

        /// <summary>本方向のやり方。Thin=間引く / Merge=まとめる。</summary>
        private SpringBoneLadderBundleMode _sbLadderBundle = SpringBoneLadderBundleMode.Thin;

        [UiControl("springBoneLadder.info", Safety = UiSafety.ReadOnly, Description = "はしごから作る鎖の情報")]
        private Label _sbLadderInfo;

        /// <summary>取り込み方のドロップダウンに並べる順。表示名と対で持つ。</summary>
        private static readonly BeltAcquireMethod[] SbLadderMethods =
        {
            BeltAcquireMethod.AutoLadder,
            BeltAcquireMethod.AutoRing,
            BeltAcquireMethod.SelectionSet,
        };

        /// <summary>スカート系だけ段を横断する。並べ方から決まるので別の指定は持たない。</summary>
        private bool SbLadderCrossRows => _sbLadderMode == SpringBoneLadderMode.Stack;

        // ================================================================
        // UI
        // ================================================================

        private void BuildSpringBoneLadderUI(VisualElement c)
        {
            c.Add(ShapeTitle(T(ShapeKeys[(int)ShapeKind.SpringBoneLadder])));

            c.Add(SL("既にあるメッシュのはしごからボーン鎖を作ります。"
                   + "メッシュは作りません。ウェイトははしご自身へ塗ります。"));

            // ── 並べ方 ────────────────────────────────────────────────
            var modeDd = new DropdownField(
                T("SBLadderMode"),
                new List<string> { "髪系（はしご1本ごとに独立）", "スカート系（段を縦断）" },
                _sbLadderMode == SpringBoneLadderMode.Stack ? 1 : 0);
            modeDd.RegisterValueChangedCallback(_ =>
            {
                _sbLadderMode = modeDd.index == 1
                    ? SpringBoneLadderMode.Stack
                    : SpringBoneLadderMode.Strand;
                RefreshSpringBoneLadderInfo();
            });
            c.Add(modeDd);

            c.Add(SL("髪系ははしご1本が鎖1本になり、ボーンは横木の中点に載ります。"
                   + "スカート系は横方向のはしごが縦に積まれた形を想定し、"
                   + "列ごとに段を縦断する鎖を作ります。ボーンはレールの頂点に載ります。"));

            // ── 取り込み元 ────────────────────────────────────────────
            BuildMeshSourceRow(c, _sbLadderPick, T("SBLadderSource"));

            // ── 取り込み方 ────────────────────────────────────────────
            var methodDd = new DropdownField(
                T("SBLadderMethod"),
                new List<string> { "はしごを自動検索", "円環を自動検索", "パーツ選択辞書の面から" },
                MethodIndexOf(_sbLadderMethod));
            methodDd.RegisterValueChangedCallback(_ =>
            {
                int i = Mathf.Clamp(methodDd.index, 0, SbLadderMethods.Length - 1);
                _sbLadderMethod = SbLadderMethods[i];
                RefreshSpringBoneLadderInfo();
            });
            c.Add(methodDd);

            var setName = new TextField(T("SBLadderSetName")) { value = _sbLadderSetName };
            setName.RegisterValueChangedCallback(e => _sbLadderSetName = e.newValue);
            c.Add(setName);
            c.Add(SL("「はしごを自動検索」は開始タグ三角形が起点です。"
                   + "「パーツ選択辞書の面から」のときだけ辞書名を使います。"));

            c.Add(PlayerIoUiKit.WideBtn(T("SBLadderCheck"), RefreshSpringBoneLadderInfo));

            _sbLadderInfo = new Label();
            _sbLadderInfo.style.fontSize   = 10;
            _sbLadderInfo.style.whiteSpace = WhiteSpace.Normal;
            _sbLadderInfo.style.marginTop  = 2;
            c.Add(_sbLadderInfo);

            // ── 取り付け先・接頭辞 ────────────────────────────────────
            c.Add(TR(T("SBLadderAutoAttach"),
                () => _sbLadderAutoAttach,
                v => { _sbLadderAutoAttach = v; RefreshSpringBoneLadderInfo(); }));

            c.Add(SL("はしごの階層親をそのまま取り付け先にします。"
                   + "親がまだメッシュでもかまいません。スキンド化のとき、"
                   + "そのメッシュのボーンへ自動で付け替わります。"
                   + "外すと下の一覧で選んだものを使います。"));

            BuildSpringBoneAttachRow(c);

            // ── 向きとウェイト ────────────────────────────────────────
            c.Add(TR(T("SBLadderReverse"), () => _sbLadderReverse, v => _sbLadderReverse = v));
            c.Add(SL("鎖の根元と先が逆に出たときに入れ替えます。"
                   + "はしごの向きは検出順で決まるので、幾何的な上下では決まりません。"));

            c.Add(TR(T("SBLadderPaint"), () => _sbLadderPaint, v => _sbLadderPaint = v));
            c.Add(SL("節点のボーンへ塗ります。節点と節点の間の頂点は、"
                   + "挟む2つの節点で線形補間して配ります。既存のウェイトは置き換わります。"));

            // ── ミラー ────────────────────────────────────────────────
            _sbLadderMirrorRow = TR(T("SBLadderMakeMirror"),
                () => _sbLadderMakeMirror, v => _sbLadderMakeMirror = v);
            c.Add(_sbLadderMirrorRow);

            c.Add(SL("ミラー側にも同じ鎖を作り、左右のボーンを対にします。"
                   + "対にしないとミラー側のメッシュが実体側のボーンで動きます。"
                   + "ウェイトを塗るのは実体側だけで、ミラー側へはミラーの機構が写します。"
                   + "取り込み元がミラーペアの実体側のときだけ使えます。"));

            // ── オブジェクトグループ ──────────────────────────────────
            c.Add(TR(T("SBLadderKeepAsGroup"),
                () => _sbLadderKeepAsGroup, v => _sbLadderKeepAsGroup = v));
            c.Add(SL("取り込み元とパラメータを残します。出力は鎖の根元ボーンです。"
                   + "あとからはしごを直して作り直すと、ボーンを作らず位置だけが入ります。"
                   + "鎖の本数や段数が変わるほど直したときは新しく作り直され、"
                   + "古い鎖はそのまま残ります。"));

            // ── 節点の間引き ──────────────────────────────────────────
            c.Add(IR(T("SBLadderRungStride"), 1, 32,
                () => _sbLadderRungStride,
                v => { _sbLadderRungStride = v; RefreshSpringBoneLadderInfo(); }));

            c.Add(IR(T("SBLadderChainStride"), 1, 32,
                () => _sbLadderChainStride,
                v => { _sbLadderChainStride = v; RefreshSpringBoneLadderInfo(); }));

            var bundleDd = new DropdownField(
                T("SBLadderBundleMode"),
                new List<string> { "間引く（間は左右の鎖で補間）", "まとめる（平均位置に鎖1本）" },
                _sbLadderBundle == SpringBoneLadderBundleMode.Merge ? 1 : 0);
            bundleDd.RegisterValueChangedCallback(_ =>
            {
                _sbLadderBundle = bundleDd.index == 1
                    ? SpringBoneLadderBundleMode.Merge
                    : SpringBoneLadderBundleMode.Thin;
                RefreshSpringBoneLadderInfo();
            });
            c.Add(bundleDd);

            c.Add(SL("はしご自体は間引かず、ボーンを置く骨組みだけを粗くします。"
                   + "頂点は必ずどれかの鎖へ付きます。末尾の段と末尾の本は必ず節点になるので、"
                   + "割り切れなくてもかまいません。"
                   + "本ストライドが 1 のときは「間引く」と「まとめる」は同じ結果です。"));

            c.Add(SL("「まとめる」は鎖がレール頂点から外れて中間に浮き、束ねた範囲は一枚板のように動きます。"
                   + "「間引く」は鎖がレール頂点に載ったままで、間はなめらかに曲がります。"));

            c.Add(SL("髪系で本ストライドを 2 以上にするときは注意してください。"
                   + "はしごの並び順は開始タグ三角形の面番号順で、空間的な隣接順とは限りません。"
                   + "スカート系の列番号はベルト上の順なので、そのまま隣り合います。"));

            // ── 鎖の先・揺れ方・段ごとのセット ────────────────────────
            BuildSpringBoneMotionUI(c);

            RefreshSpringBoneLadderInfo();
        }

        private static int MethodIndexOf(BeltAcquireMethod m)
        {
            for (int i = 0; i < SbLadderMethods.Length; i++)
                if (SbLadderMethods[i] == m) return i;
            return 0;
        }

        /// <summary>
        /// いまの指定で何本取れるかを試して表示する。読むだけでモデルは変えない。
        /// </summary>
        private void RefreshSpringBoneLadderInfo()
        {
            if (_sbLadderInfo == null) return;

            var source = ResolveSpringBoneLadderSource(out string why);
            if (source == null) { _sbLadderInfo.text = why; return; }

            var picked = BeltAcquire.AcquireStrips(
                source, _sbLadderMethod, SbLadderCrossRows, _sbLadderSetName);

            if (!picked.Ok) { _sbLadderInfo.text = picked.Message; return; }

            // 数え方は本体に任せる。ここで別に数えると食い違いに気づけない。
            SpringBoneLadderPlacer.CountPlan(
                source.MeshObject, picked.Strips, _sbLadderMode,
                _sbLadderRungStride, _sbLadderChainStride, _sbLadderBundle,
                out int chains, out int bones);

            int shown = bones + (_sbAddTail ? chains : 0);

            // ミラーは取り込み元がミラーペアの実体側のときだけ作れる。
            var model2 = GetModelContext?.Invoke();
            var pair   = model2?.GetMirrorPair(source);
            bool canMirror = pair != null && pair.Real == source;

            _sbLadderMirrorRow?.SetEnabled(canMirror);

            string attachText = "";
            if (_sbLadderAutoAttach)
            {
                int auto = ResolveLadderAttachAuto(source, out string attachWhy);
                attachText = " 取り付け先=" + (auto >= 0
                    ? $"[{auto}] {model2?.GetMeshContext(auto)?.Name}"
                    : $"モデル直下（{attachWhy}）");
            }

            _sbLadderInfo.text =
                $"{picked.Message} → 鎖 {chains} 本 / ボーン {shown} 本"
                + (canMirror
                    ? (_sbLadderMakeMirror ? $"（ミラーぶんを足して {shown * 2} 本）" : "（ミラーも作れます）")
                    : "（取り込み元がミラーペアの実体側ではないのでミラーは作れません）")
                + attachText;
        }

        /// <summary>
        /// 取り付け先を取り込み元の階層親から決める。
        ///
        /// 【なぜ親をそのまま使うか】
        ///   はしご（例「円筒24C」）は、揺らしたい部位の下にぶら下がっている。
        ///   その親がそのまま「揺れの根元」になる。名前で探すと規則が崩れた
        ///   モデルで黙って外れるので、階層をそのまま読む。
        ///
        /// 【親がまだメッシュでもよい】
        ///   MeshFilter 系のモデルにはボーンが 1 本も無い。メッシュを親にして置き、
        ///   スキンド化のときにそのメッシュのボーンへ付け替わる
        ///   （MeshFilterToSkinnedConverter の Phase 4a）。
        /// </summary>
        /// <returns>取り付け先の masterIndex。決められないときは -1（モデル直下）。</returns>
        private int ResolveLadderAttachAuto(MeshContext source, out string why)
        {
            why = "";

            var model = GetModelContext?.Invoke();
            if (model == null) { why = "モデルがありません"; return -1; }
            if (source == null) { why = "取り込み元がありません"; return -1; }

            int parent = source.HierarchyParentIndex;
            if (parent < 0 || parent >= model.MeshContextCount)
            {
                why = "取り込み元に階層親がありません";
                return -1;
            }

            var pc = model.GetMeshContext(parent);
            if (pc == null) { why = "階層親を引けません"; return -1; }

            if (pc.Type != MeshType.Bone && pc.Type != MeshType.Mesh)
            {
                why = $"階層親がボーンでもメッシュでもありません（{pc.Type}）";
                return -1;
            }

            return parent;
        }

        /// <summary>取り込み元の描画オブジェクトを引く。引けないときは理由を返す。</summary>
        private MeshContext ResolveSpringBoneLadderSource(out string why)
        {
            why = "";

            var model = GetModelContext?.Invoke();
            if (model == null) { why = "モデルがありません。"; return null; }

            var mesh = _sbLadderPick.Current;
            if (mesh == null) { why = "取り込み元を選んでください。"; return null; }

            int src = ResolveMasterIndexOf(mesh);
            if (src < 0) { why = "取り込み元の索引を引けませんでした。"; return null; }

            var mc = model.GetMeshContext(src);
            if (mc?.MeshObject == null) { why = "取り込み元のメッシュがありません。"; return null; }

            return mc;
        }

        // ================================================================
        // 生成
        // ================================================================

        private void GenerateSpringBoneLadderChains()
        {
            var model = GetModelContext?.Invoke();
            if (model == null) { SetSpringBoneStatus("モデルがありません。"); return; }

            var mesh = _sbLadderPick.Current;
            if (mesh == null) { SetSpringBoneStatus("取り込み元を選んでください。"); return; }

            int src = ResolveMasterIndexOf(mesh);
            if (src < 0) { SetSpringBoneStatus("取り込み元の索引を引けませんでした。"); return; }

            if (_sbLadderMethod == BeltAcquireMethod.SelectionSet
                && string.IsNullOrEmpty(_sbLadderSetName))
            {
                SetSpringBoneStatus("パーツ選択辞書の名前を入れてください。");
                return;
            }

            string prefix = string.IsNullOrEmpty(_sbPrefix) ? "Spring" : _sbPrefix.Trim();
            int mi = ModelIndex();

            // 取り付け先。自動のときは取り込み元の階層親をそのまま使う。
            int attach = _sbAttachIndex;
            if (_sbLadderAutoAttach)
                attach = ResolveLadderAttachAuto(model.GetMeshContext(src), out _);

            // 1. ボーンを作る（必要ならはしごへウェイトも塗る）
            SendCommand(new PlaceSpringBoneLadderChainsCommand(
                mi, src, _sbLadderMethod, _sbLadderMode,
                attach, prefix, _sbLadderSetName,
                _sbLadderReverse, _sbAddTail, _sbTailLength, _sbLadderPaint,
                _sbLadderRungStride, _sbLadderChainStride, _sbLadderBundle,
                null, _sbLadderKeepAsGroup, _sbLadderMakeMirror));

            // 作ったボーンは名前で引き直す。名前の規則は数値から作る経路と同じ。
            var chains = SpringBoneChainPlacer.CollectByPrefix(GetModelContext?.Invoke(), prefix);
            if (chains.Count == 0)
            {
                SetSpringBoneStatus("ボーンが作られませんでした。取り込み元と取り込み方を確認してください。");
                return;
            }

            // 2〜4. 揺れ方・鎖の先頭・段ごとのセット
            ApplySpringToChains(mi, chains, prefix);

            RefreshSpringBoneAttach();
            RefreshSpringBoneLadderInfo();
        }
    }
}
