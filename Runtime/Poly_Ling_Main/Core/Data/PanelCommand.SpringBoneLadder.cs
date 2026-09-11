// PanelCommand.SpringBoneLadder.cs
// はしごから揺れもの用のボーン鎖を作るコマンド。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【PlaceSpringBoneChainsCommand と分ける理由】
//   あちらは数値（半径・高さ・折れ線）から置く。こちらは既にあるメッシュの
//   はしごから置く。入力の出どころが違うので、同じコマンドに寄せると
//   使わないパラメータが半分ずつ残る。
//
// 【点列を載せない】
//   取り込み元・取り込み方・辞書名だけを載せ、はしごの点列は焼き込まない。
//   ボーンは1回置くだけで作り直しの経路が無く、控えた点列を使う場面が無いため。
//   取り直しは実行時に BeltAcquire.AcquireStrips が行う。
//
// 【間引き】
//   RungStride（段）と ChainStride（本）でボーンの数を減らす。はしご自身は
//   間引かず、ボーンを置く骨組みだけを粗くする。塗りは節点の間を線形補間する。
//   考え方の全体は SpringBoneLadderPlacer.cs の冒頭にまとめてある。
//
// 【masterIndex の印】
//   SourceMasterIndex / AttachMasterIndex には IsMeshRef を付ける。
//   ObjectGroup は索引を保存せず、控えた ObjectId から今の索引へ引き直す
//   （ObjectGroupOps.Capture / RewriteMeshRefs）。付けないと控えた時点の索引が
//   そのまま残り、リストの挿入・削除・並べ替えでずれたまま別のオブジェクトを指す。
//
//   AttachMasterIndex = -1（親を付けない）は控えでは 0 になり、引き直しで -1 に戻る
//   （ObjectGroupOps.cs:322-328）。参照が無い状態はそのまま往復する。
//
//   どちらも自分のモデル内を指すので MeshRefModelKey は要らない。
//   IsMeshRef を読むのは ObjectGroupOps だけで、スキーマ生成・ToArgs・Create の
//   扱いは変わらない（PLParamAttribute.cs:111-113）。実行経路には影響しない。

using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Tools.SpringBoneRig;

namespace Poly_Ling.Data
{
    [PLCommand(Description = "はしご（梯子状ベルト）から揺れもの用のボーン鎖を置く。ボーンを作り、必要ならはしご自身へウェイトを塗る。メッシュと揺れ方は付けない。")]
    public class PlaceSpringBoneLadderChainsCommand : PanelCommand
    {
        [PLParam(Description = "はしごの取り込み元になる描画オブジェクトの masterIndex",
                 Required = true, IsMeshRef = true)]
        public int SourceMasterIndex { get; }

        [PLParam(Description = "取り込み方。AutoLadder=開始タグ三角形から自動検索 / AutoRing=円環を自動検索 / SelectionSet=パーツ選択辞書の面から。Baked は使えない",
                 Required = true)]
        public BeltAcquireMethod Method { get; }

        [PLParam(Description = "SelectionSet のときのパーツ選択辞書の名前")]
        public string SetName { get; }

        [PLParam(Description = "並べ方。Strand=はしご1本ごとに独立した鎖（髪系） / Stack=段グループを縦断する鎖を列ごとに（スカート系）",
                 Required = true)]
        public SpringBoneLadderMode Mode { get; }

        [PLParam(Description = "親にするボーンの masterIndex。-1 で親を付けない",
                 IsMeshRef = true)]
        public int AttachMasterIndex { get; }

        [PLParam(Description = "作るボーンの名前の接頭辞", Required = true)]
        public string NamePrefix { get; }

        [PLParam(Description = "鎖の根元と先を入れ替える")]
        public bool ReverseChain { get; }

        [PLParam(Description = "鎖の先に短いボーンを 1 本足す。鎖の先は tail 扱いで揺れないための手当て")]
        public bool AddTailBone { get; }

        [PLParam(Description = "足すボーンの長さ[m]。0 以下で既定値", Min = 0.0)]
        public float TailLength { get; }

        [PLParam(Description = "取り込み元のはしごへウェイトを塗る。節点のボーンへ、間の段は線形補間で書く")]
        public bool PaintWeights { get; }

        [PLParam(Description = "段ストライド。段を何段に 1 つの割でボーンにするか。1 で間引かない。末尾の段は必ず節点になる",
                 Min = 1, Max = 64, Step = 1)]
        public int RungStride { get; }

        [PLParam(Description = "本ストライド。はしご（Strand）または列（Stack）を何本に 1 つの割で鎖にするか。1 で間引かない",
                 Min = 1, Max = 64, Step = 1)]
        public int ChainStride { get; }

        [PLParam(Description = "本方向のやり方。Thin=間引いて間の線を左右の鎖で補間 / Merge=まとめて平均位置に鎖 1 本。ChainStride が 1 のときはどちらも同じ")]
        public SpringBoneLadderBundleMode BundleMode { get; }

        /// <summary>
        /// 既にある鎖の根元ボーンの masterIndex 列。空でボーンを新しく作る。
        ///
        /// RebuildRole=TargetIndex … オブジェクトグループの作り直しでは、
        /// 控えた出力先（＝前に作った鎖の根元）の索引がここへ書き戻される。
        /// これがあるとボーンを作らず位置だけ流し込むので、ObjectId が保たれ、
        /// 揺れ方の設定・選択辞書・はしごのウェイトが切れない。
        /// </summary>
        [PLParam(Description = "既にある鎖の根元ボーンの masterIndex 列。指定するとボーンを作らず位置だけ更新する。空で新規作成",
                 IsMeshRef = true, RebuildRole = PLRebuildRole.TargetIndex)]
        public int[] ChainRootMasterIndices { get; }

        [PLParam(Description = "取り込み元とパラメータをオブジェクトグループとして残す。出力は鎖の根元ボーン")]
        public bool KeepAsGroup { get; }

        /// <summary>
        /// ミラー側にも鎖を作り、左右のボーンを対にする。
        ///
        /// 取り込み元がミラーペアの実体側でないときは何も起きない。
        /// 対を立てないとミラーは成立しない（MirrorPair.BuildBonePairMap は
        /// MirrorBoneIndex からしか対応表を作らないため、ミラー側メッシュが
        /// 実体側と同じボーン番号を引いてしまう）。
        /// </summary>
        [PLParam(Description = "ミラー側にも鎖を作り、左右のボーンを対にする。取り込み元がミラーペアの実体側のときだけ効く")]
        public bool MakeMirrorChains { get; }

        public PlaceSpringBoneLadderChainsCommand(
            int modelIndex,
            int sourceMasterIndex,
            BeltAcquireMethod method,
            SpringBoneLadderMode mode,
            int attachMasterIndex,
            string namePrefix,
            string setName = "",
            bool reverseChain = false,
            bool addTailBone = true,
            float tailLength = 0.05f,
            bool paintWeights = true,
            int rungStride = 1,
            int chainStride = 1,
            SpringBoneLadderBundleMode bundleMode = SpringBoneLadderBundleMode.Thin,
            int[] chainRootMasterIndices = null,
            bool keepAsGroup = false,
            bool makeMirrorChains = false)
            : base(modelIndex)
        {
            SourceMasterIndex = sourceMasterIndex;
            Method            = method;
            Mode              = mode;
            AttachMasterIndex = attachMasterIndex;
            NamePrefix        = namePrefix;
            SetName           = setName;
            ReverseChain      = reverseChain;
            AddTailBone       = addTailBone;
            TailLength        = tailLength;
            PaintWeights      = paintWeights;
            RungStride        = rungStride;
            ChainStride       = chainStride;
            BundleMode        = bundleMode;

            ChainRootMasterIndices = chainRootMasterIndices ?? System.Array.Empty<int>();
            KeepAsGroup            = keepAsGroup;
            MakeMirrorChains       = makeMirrorChains;
        }
    }
}
