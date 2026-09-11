// Assets/Editor/MeshObject.cs
// 頂点ベースのメッシュデータ構造
// - Vertex: 位置 + 複数UV + 複数法線 + フラグ
// - Face: N角形対応（三角形、四角形、Nゴン）+ マテリアルインデックス + フラグ
// - MeshObject: Unity UnityMesh との相互変換（サブメッシュ対応）
// v1.2: VertexFlags/FaceFlags 追加
//
// 【分割先】このファイルから次へ分けてある。
//   Face.cs                  面クラス（MeshObject.cs から分離）。
//   MeshObject.Hierarchy.cs  MeshObject：階層・トランスフォームと付帯データ（IK／剛体／JOINT）のメソッド。フィールドは MeshObject.cs。
//   MeshObject.Ids.cs        MeshObject：ID 管理・ID による検索・位置配列キャッシュのメソッド。フィールドは MeshObject.cs。
//   MeshObject.Normals.cs    MeshObject：法線の再計算と除外セットほかのメソッド。フィールドは MeshObject.cs。
//   MeshObject.UnityMesh.cs  MeshObject：Unity Mesh への変換（頂点共有版）のメソッド。フィールドは MeshObject.cs。
//   MeshTypes.cs             描画オブジェクトの種類・一人称での見え方・種別の列挙（MeshObject.cs から分離）。
//   Vertex.cs                頂点クラス（MeshObject.cs から分離）。
//   VertexFaceFlags.cs       頂点・面のフラグ定義（MeshObject.cs から分離）。


using Poly_Ling.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.Data
{

    // ============================================================
    // MeshObject クラス
    // ============================================================

    /// <summary>
    /// メッシュデータ本体
    /// Vertex/Faceリストを管理し、Unity Meshとの相互変換を提供
    /// </summary>
    [Serializable]
    public partial class MeshObject
    {
        // ================================================================
        // ID管理
        // ================================================================

        /// <summary>ID生成用の乱数ジェネレータ</summary>
        [NonSerialized]
        private static readonly System.Random _idRandom = new System.Random();

        /// <summary>頂点用の使用中ID（重複防止）</summary>
        [NonSerialized]
        private HashSet<int> _usedVertexIds = new HashSet<int>();

        /// <summary>面用の使用中ID（重複防止）</summary>
        [NonSerialized]
        private HashSet<int> _usedFaceIds = new HashSet<int>();

        // ================================================================
        // 基本プロパティ
        // ================================================================

        /// <summary>メッシュ名</summary>
        public string Name = "Mesh";

        /// <summary>メッシュの種類</summary>
        public MeshType Type { get; set; } = MeshType.Mesh;

        /// <summary>頂点リスト</summary>
        public List<Vertex> Vertices = new List<Vertex>();

        /// <summary>面リスト</summary>
        public List<Face> Faces = new List<Face>();

        /// <summary>
        /// 描画オブジェクトの種別（MeshFilter 系 / SkinnedMesh 系）。既定は MeshFilter。
        ///
        /// 【書き換えてよい場所】
        ///   RecomputeSkinKind() / SetSkinKind() 経由に限る。
        ///   直接代入すると、実データと食い違ったまま描画経路が選ばれる。
        ///
        /// 【再計算する契機】
        ///   インポート、プロジェクト/モデル読込、スキン生成、ウェイトペイント（数値設定含む）、
        ///   頂点データ転送、ミラー生成/同期、Undo/Redo のウェイト書き戻し、
        ///   Vertices リストの丸ごと差し替え。
        ///   同一メッシュ内でのウェイト複製・補間（押し出し／ナイフ／ベベル／穴埋め等）は
        ///   無 → 有 の遷移を起こさないため再計算不要。
        /// </summary>
        public SkinKind SkinKind = SkinKind.MeshFilter;

        // ================================================================
        // Position 配列キャッシュ（ハイブリッドSoA）
        // ================================================================
        //
        // 【目的】
        // Vertices[i].Position への個別アクセスを介さずに、
        // Position配列をバルクで取得・設定できるようにする。
        // GPU転送、Undoスナップショット、ネットワーク同期で使用。
        //
        // 【使い分け】
        // - 読み取り: Positions プロパティ（キャッシュ自動構築）
        // - 書き込み: SetPositions()（Vertex.Positionに書き戻し）
        // - 無効化: InvalidatePositionCache()（Vertex.Positionを直接変更した後）
        //
        // 【注意】
        // Vertices[i].Position を直接変更した場合は
        // InvalidatePositionCache() を呼ぶこと。
        // AddVertex/RemoveVertex 等のトポロジー変更時は自動で無効化される。
        //

        [NonSerialized]
        private Vector3[] _positionCache;

        [NonSerialized]
        private bool _positionCacheDirty = true;

        /// <summary>
        /// Position配列を取得（キャッシュ付き）
        /// Vertices[i].Position を変更した場合は InvalidatePositionCache() が必要
        /// </summary>
        public Vector3[] Positions
        {
            get
            {
                if (_positionCacheDirty || _positionCache == null || _positionCache.Length != Vertices.Count)
                {
                    RebuildPositionCache();
                }
                return _positionCache;
            }
        }

        /// <summary>
        /// 頂点が三角形化済みか（PMX形式）
        /// true: 各頂点が1つのUV/法線を持ち、すべての面が三角形 (展開済み、PMX互換)
        /// false: 頂点が複数のUV/法線を持つ可能性あり、四角形等も含みうる (MQO形式)
        ///
        /// ※従来は "IsExpanded" と呼称されていたが、TreeView の展開/折りたたみ
        /// (MeshContext.IsFolding / SummaryTreeAdapter.IsExpanded) と紛らわしいため改名。
        /// 実体としては "三角形化 + 頂点展開" が同時に行われる PMX 化処理のフラグ。
        /// </summary>
        public bool IsTriangulated { get; set; } = false;




        // ================================================================
        // 階層・トランスフォーム情報
        // ================================================================

        /// <summary>
        /// 親メッシュのインデックス（-1=ルート）。
        /// 実体は HierarchyParentIndex と同じ 1 つ。
        ///
        /// 【なぜ入れ物を分けないか】
        ///   両者は同じ「親」を表しており、食い違ってよい場面が無い。
        ///   親を書く箇所は全部が両方へ同じ値を入れている。
        ///   別々の入れ物にしておくと、片方だけ書く箇所が 1 つ生まれるだけで
        ///   食い違いが保存され、あとから読む側が壊れる
        ///   （実測: メッシュリストの並べ替えが HierarchyParentIndex だけを書き、
        ///    ParentIndex にブリッジ挿入前の古い値が残っていた）。
        ///   入れ物を 1 つにして、食い違いを作れなくする。
        ///
        ///   CSV は従来どおり parentIndex 行と hierarchyParentIndex 行の両方を
        ///   出し入れする。同じ場所へ入るので、食い違っている旧ファイルを読むと
        ///   後に来る hierarchyParentIndex の値へ揃う（＝正しい側が残る）。
        /// </summary>
        public int ParentIndex
        {
            get => HierarchyParentIndex;
            set => HierarchyParentIndex = value;
        }

        /// <summary>
        /// 階層深度（MQO互換、インポート/エクスポート時のみ使用）
        /// 通常はParentIndexから計算
        /// </summary>
        public int Depth { get; set; } = 0;

        /// <summary>
        /// ゲームオブジェクト階層の親インデックス（-1=ルート）
        /// Unityエクスポート時のTransform親子関係用（将来用）
        /// </summary>
        public int HierarchyParentIndex { get; set; } = -1;

        /// <summary>
        /// アーマチャ生成時にボーンを生成しない。
        /// 子ボーンのワールド位置計算にはこのメッシュの姿勢を考慮する。
        /// このメッシュの頂点ウェイトは最寄りの親ボーンに割り当てられる。
        /// </summary>
        public bool IgnorePoseInArmature { get; set; } = false;

        /// <summary>
        /// 頂点法線を保持する（自動再計算を行わない）。
        /// true の場合、頂点移動 / Undo/Redo / ミラー更新時に
        /// Mesh.RecalculateNormals() を呼ばず、MeshObject が保持する法線をそのまま使う。
        /// 髪の房など、隣接オブジェクト間で法線を揃えたメッシュに使用する。
        ///
        /// 【既定 true】自動再計算は既定で行わない。
        /// 左ペインの「法線自動計算」チェック（既定 OFF）が選択メッシュの本フラグを
        /// 反転して書き込む。保存済みプロジェクトは保存値が読み込まれる
        /// （CsvMeshSerializer / ModelSerializer）ため、既定値が効くのは
        /// 新規生成メッシュと、値を持たない経路のみ。
        /// </summary>
        public bool PreserveNormals { get; set; } = true;

        /// <summary>
        /// 法線の自動再計算から除外するセット一覧（パーツ選択辞書と同じ構造）。
        /// リストに載っているセットが指す要素は、RecalculateNormals /
        /// RecalculateSmoothNormals の直前に法線を退避し、計算後に書き戻す。
        ///   - 頂点集合: その頂点を参照する全ての面コーナー
        ///   - 面集合  : その面の全コーナー
        ///   - 辺集合  : 両端頂点として扱う
        ///   - 線集合  : 対象外
        /// </summary>
        public List<Poly_Ling.Selection.PartsSelectionSet> NormalRecalcExcludeList { get; set; }
            = new List<Poly_Ling.Selection.PartsSelectionSet>();

        /// <summary>
        /// ミラー分岐のルートか。
        /// true の場合、ヒエラルキーエクスポート時にこのノード配下を
        /// 実体側とミラー側（MirrorSide を祖先に持つノード）の2本の枝に分割する。
        /// 非スキンドメッシュ専用。
        /// </summary>
        public bool IsMirrorBranchRoot { get; set; } = false;

        /// <summary>
        /// エクスポート時のローカルトランスフォーム
        /// </summary>
        public BoneTransform BoneTransform { get; set; } = new BoneTransform();

        /// <summary>
        /// ミラー実体化（in-place ベイク）の状態。null = 未実体化。
        /// 対称面をまたぐ処理のために一時的に反対側を生やしている間だけ保持し、
        /// 解除で null に戻す。MeshObject に持たせているのは、Undo のスナップショットが
        /// MeshObject 単位で取られるため（Clone で引き継ぐ）。
        /// </summary>
        public Poly_Ling.Tools.MirrorBakeResult MirrorBakeState { get; set; } = null;

        // ================================================================
        // 付帯データ（IK / 剛体 / JOINT / SpringBone）— 純POCOデータ契約
        // ================================================================
        //
        // ================================================================
        // 【ボーン付帯データ格納規約（厳守）】※本ブロックを正典とする
        // ----------------------------------------------------------------
        //   1. per-bone POCO 統一
        //      ボーン付帯データ（IK / 剛体 / JOINT / SpringBone / Humanoid割当）は
        //      MeshObject に per-node POCO として持つ。
        //      null = 当該属性を持たない。#if UNITY_EDITOR を含めない。
        //
        //      付帯先は原則 Type == MeshType.Bone。ただし SpringBone だけは
        //      「階層に載るノード」まで許す（下の SpringBone 節を参照）。
        //      VRM の joint / collider は glTF のノード索引を指すだけで、
        //      スキン関節である必要がない。UniVRM の ModelExporter は
        //      階層の全 Transform を無条件にノード化するため、描画オブジェクトの
        //      ノードにも揺れを載せられる。判定は SpringBoneOps.IsCarrier が正典。
        //
        //   2. 参照は name主・index従
        //      ボーン間参照は付帯先/相手の MeshObject.Name を一次キーとする。
        //      index は実行時キャッシュであり、並べ替え・I/O で無効化されうるため
        //      永続化の基準に用いない。IK の TargetIndex/BoneIndex も本規約に従い
        //      name を一次キー、index をキャッシュへ降格する。
        //
        //   3. モデルレベルは「モデル固有」と「派生ビュー」のみ
        //      ModelContext に置いてよいのは、真にモデル固有のもの
        //      （例: SpringBoneColliderGroupNames）と、per-bone から再構築できる
        //      派生ビュー／キャッシュ（例: HumanoidMapping の name→index Dict）のみ。
        //      派生ビューは per-bone を正として導出する。
        //      Humanoid割当は per-bone を正とし、同一 Humanoidボーンを複数ボーンが
        //      主張しない一意性を不変条件として維持する。
        //
        //   4. 永続化は CSV/JSON 対称
        //      両経路で同じ付帯データを読み書きする（片方のみの実装を残さない）。
        //      座標系変換は I/O 境界のみで行い、POCO は生値（Unity左手系）を保持する。
        //
        //   【運用（IK / Humanoid の派生ビューと同期タイミング）】
        //      per-bone を永続 canonical とする一方、実行時は集約ビューを working
        //      として使う（consumer は集約ビューを読む）。両者の同期は
        //      「保存・読込の境界のみ」で行い、編集中のリアルタイム同期はしない。
        //        - IK       : per-bone(EffectorBoneName/IKLink) ⇔ 集約(IKData.Links)。
        //                     import 時に Sync 済み（ImportCommands）。
        //        - Humanoid : per-bone(MeshObject.HumanBodyBone) ⇔ Dict(HumanoidMapping)。
        //                     割当は UI 経由で Dict を編集し、per-bone へは保存時 Sync、
        //                     読込時 Rebuild のみ（import は Dict を確立しないため無同期）。
        //      同期の実体は IKChainResolver / HumanoidMappingResolver（境界で呼ぶ）。
        // ================================================================
        //
        // 【設計方針】
        //   IK・剛体・JOINT・SpringBone を MeshObject に統一して持たせる。これにより
        //   クロス言語移植（Python/JavaScript）と Unityヒエラルキー
        //   エクスポートのための単一データ契約が MeshObject 上で完結する。
        //   いずれも #if UNITY_EDITOR を含まない POCO（IKData/RigidBodyData/
        //   JointData/SpringBone*）であり、null = 当該属性を持たないことを表す。
        //
        // 【役割の判別は Type(MeshType) を流用】
        //   - 剛体     : Type == MeshType.RigidBody       かつ RigidBodyData != null
        //   - JOINT    : Type == MeshType.RigidBodyJoint  かつ JointData    != null
        //   - IKボーン : Type == MeshType.Bone            かつ IKData       != null
        //
        // 【MeshContext との関係】
        //   従来 MeshContext が保持していた IK フィールド（IsIK 等）の実体は
        //   本 IKData に移設済み。MeshContext 側は後方互換のための薄い委譲
        //   プロパティのみを公開する（Type / BoneTransform と同一パターン）。
        // ----------------------------------------------------------------

        /// <summary>
        /// IKデータ（IKボーンのみ非null）。
        /// 非null ⇔ このボーンはIKボーン。
        /// </summary>
        public IKData IKData { get; set; } = null;

        /// <summary>
        /// IKリンクデータ（IKチェーンのリンクボーンのみ非null）。
        /// 非null ⇔ このボーンはIKリンク。所属チェーン・順序は IKルートの
        /// EffectorBoneName から階層(HierarchyParentIndex)で導出する（IKChainResolver）。
        /// ※#4a: 追加のみ。現段階の源泉は IKData.Links。
        /// </summary>
        public IKLinkData IKLink { get; set; } = null;

        /// <summary>
        /// 剛体データ（Type == MeshType.RigidBody のとき非null）。
        /// 頂点/面は持たず、形状はギズモとして利用時に生成する。
        /// </summary>
        public RigidBodyData RigidBodyData { get; set; } = null;

        /// <summary>
        /// JOINTデータ（Type == MeshType.RigidBodyJoint のとき非null）。
        /// 接続剛体A/Bを名前で参照する（index従）。
        /// </summary>
        public JointData JointData { get; set; } = null;

        /// <summary>
        /// PMX ボーンの付帯データ（Type == MeshType.Bone かつ PMX 由来のとき非null）。
        /// 変形階層・フラグ・接続先・付与親・固定軸・ローカル軸・外部親を保持する。
        /// 位置と親子関係と IK は BoneTransform / HierarchyParentIndex / IKData が正。
        /// 参照はすべて名前を主とする（JointData と同じ規約）。
        /// </summary>
        public PmxBoneAttrData PmxBone { get; set; } = null;

        // ------------------------------------------------------------
        // スプリングボーン付帯データ（階層に載るノードに付く）
        //   VRM SpringBone(VRMC_springBone) 由来。物理演算(RigidBody/Joint)とは別物。
        //
        //   【付帯先】ボーン、および非スキンドの描画オブジェクト。
        //     スキンドの描画オブジェクトは HierarchyBuilder がルート直下へ置く
        //     （親を解決するのは !isSkinned の枝だけ）ので親子の鎖にならず、
        //     揺らしても意味がないため対象外。判定は SpringBoneOps.IsCarrier。
        //   - コライダー : SpringBoneColliders（1ボーンに複数可。null/空=なし）
        //   - ジョイント : SpringBoneJoint（揺れチェーンメンバー。非null=揺れjoint）
        //   - チェーンルート : SpringBoneChainRoot（チェーン起点ボーンのみ。非null=ルート）
        //   チェーンの形状・順序はボーン階層(HierarchyParentIndex)＋SpringBoneJoint有無
        //   から導出する（明示的な順序リストは持たない）。
        // ------------------------------------------------------------

        /// <summary>スプリングボーン・コライダー（付帯ボーンに複数可。null/空=なし）。</summary>
        public List<SpringBoneColliderData> SpringBoneColliders { get; set; } = null;

        /// <summary>スプリングボーン・ジョイント（非null=揺れチェーンのメンバー）。</summary>
        public SpringBoneJointData SpringBoneJoint { get; set; } = null;

        /// <summary>スプリングボーン・チェーンルート（非null=このボーンがチェーン起点）。</summary>
        public SpringBoneChainData SpringBoneChainRoot { get; set; } = null;

        // ------------------------------------------------------------
        // Humanoid 割当（Type == MeshType.Bone のボーンに付く）
        //   規約: MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
        //   このボーンが対応する Unity Humanoid 名（例 "LeftUpperArm"）。空=非割当。
        //   モデルレベルの HumanoidMapping（name→index Dict）は本欄からの派生ビュー。
        //   ※#5a: 追加のみ。現段階の源泉は ModelContext.HumanoidMapping。
        //     相互同期は HumanoidMappingResolver で行う（併存・非破壊）。
        // ------------------------------------------------------------

        /// <summary>Unity Humanoid 割当名（空=非割当）。</summary>
        public string HumanBodyBone { get; set; } = "";

        /// <summary>
        /// 左右で対になるボーンの MeshContextList 索引（-1 = 対なし）。
        ///
        /// スキンド変換が実体側とミラー側のボーンを 1 対 1 で作った時点の確定値。
        /// 左右のボーン対応をウェイトから推定してはならない。推定は、片側だけを
        /// 塗って左右が非対称になった瞬間に外れ、ミラー側へ誤ったボーン番号を書く。
        /// 索引なので ModelContext.RemapIndexReferences の付け替え対象に含める。
        /// </summary>
        public int MirrorBoneIndex { get; set; } = -1;

        /// <summary>
        /// Humanoid マッスル可動域（null=Unity 既定を使う）。
        /// 3マッスル軸は Min/Max/Center の Vector3 成分で表現する。
        /// ※5d-1: 格納のみ。consumer 差し替えは 5d-2。
        /// </summary>
        public HumanLimitData HumanLimit { get; set; } = null;

        // ------------------------------------------------------------
        // 一人称カメラでの見え方（描画オブジェクトに付く）
        //   VRM の firstPerson.meshAnnotations 由来。付帯先はレンダラになる
        //   ノード（Mesh / BakedMirror など）で、ボーンには意味がない。
        //   既定 Auto は「指定なし」と同義で、出力にも書かない。
        // ------------------------------------------------------------

        /// <summary>一人称カメラでの扱い（既定 Auto＝VRM の既定に任せる）。</summary>
        public VrmFirstPersonType VrmFirstPerson { get; set; } = VrmFirstPersonType.Auto;

        // === プロパティ ===

        /// <summary>頂点数</summary>
        public int VertexCount => Vertices.Count;

        /// <summary>面数</summary>
        public int FaceCount => Faces.Count;

        /// <summary>三角形数（全面の合計）</summary>
        public int TriangleCount => Faces.Sum(f => f.TriangleCount);

        /// <summary>サブメッシュ数（使用されているマテリアルインデックスの最大値+1）</summary>
        public int SubMeshCount
        {
            get
            {
                if (Faces.Count == 0) return 1;
                int maxMatIndex = Faces.Max(f => f.MaterialIndex);
                return maxMatIndex + 1;
            }
        }

        /// <summary>
        /// SkinnedMesh 系か。O(1)。SkinKind の明示状態を読むだけ。
        /// 描画経路・座標系の判定はすべてこれを使う。
        /// </summary>
        public bool IsSkinnedKind => SkinKind == SkinKind.Skinned;

        /// <summary>
        /// ミラー側専用のボーンウェイトを持つか（PMX のミラー対と同じ持ち方）。
        ///
        /// これが true のメッシュは、ミラー側が実体側とは別のボーンに紐づく
        /// 正しいウェイトを自前で持っている。スキンド変換が、ミラー分岐の中に
        /// あるメッシュに対して反対側ボーンを指す値を書き込む。
        ///
        /// 生成するミラーを PMX 型（独立データ）にするかの判定に使う。
        /// 分岐の外のメッシュはこれを持たないが、その場合は反対側ボーン自体が
        /// 存在せず、実体側と同じボーンで動く鏡像になる（中心線上の
        /// オブジェクトを鏡像化する MQO 系ミラーと同じ結果）。
        /// </summary>
        public bool HasMirrorBoneWeight => Vertices.Any(v => v.HasMirrorBoneWeight);

        // === コンストラクタ ===

        public MeshObject() { }

        public MeshObject(string name)
        {
            Name = name;
        }

        // ================================================================
        // 法線の自動再計算 除外セット
        // ================================================================

        /// <summary>法線退避エントリ（面index / コーナーindex / 法線）。</summary>
        private struct NormalBackupEntry
        {
            public int FaceIndex;
            public int Corner;
            public Vector3 Normal;
        }

        /// <summary>除外セットが空でないか。</summary>
        public bool HasNormalRecalcExclude
        {
            get
            {
                if (NormalRecalcExcludeList == null) return false;
                foreach (var set in NormalRecalcExcludeList)
                {
                    if (set == null) continue;
                    if (set.Vertices.Count > 0 || set.Edges.Count > 0 || set.Faces.Count > 0)
                        return true;
                }
                return false;
            }
        }
    }

    // ============================================================
    // ヘルパークラス
    // ============================================================

    /// <summary>
    /// Vector3 比較用（Dictionary キー用）
    /// </summary>
    internal class Vector3Comparer : IEqualityComparer<Vector3>
    {
        private const float Tolerance = 0.00001f;

        public bool Equals(Vector3 a, Vector3 b)
        {
            return Vector3.Distance(a, b) < Tolerance;
        }

        public int GetHashCode(Vector3 v)
        {
            // 精度を落としてハッシュ化（近い値が同じハッシュになるように）
            int x = Mathf.RoundToInt(v.x * 10000);
            int y = Mathf.RoundToInt(v.y * 10000);
            int z = Mathf.RoundToInt(v.z * 10000);
            return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        }
    }
}
