// PanelCommand.Normal.cs
// 法線（再計算の除外辞書・法線編集・法線移植）の操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間。PanelCommand.cs から分割）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Tools.SpringBoneRig;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
{
    // ================================================================
    // 法線再計算 除外辞書（実体は MeshObject.NormalRecalcExcludeList）
    // ================================================================

    /// <summary>現在の選択を法線再計算の除外セットとして保存</summary>
    [PLCommand(Description = "現在の選択を法線再計算の除外セットとして保存</summary>")]
    public class SaveNormalExcludeSetCommand : PanelCommand
    {
        [PLParam(TextKey = "NormalExcludeSetName",
                 Description = "保存する法線再計算 除外セットの名前", Required = true)]
        public string SetName { get; }
        public SaveNormalExcludeSetCommand(int modelIndex, string setName)
            : base(modelIndex) { SetName = setName; }
    }

    /// <summary>除外セットを現在の選択に適用（置き換え）</summary>
    [PLCommand(Description = "除外セットを現在の選択に適用（置き換え）</summary>")]
    public class LoadNormalExcludeSetCommand : PanelCommand
    {
        [PLParam(TextKey = "NormalExcludeSetIndex",
                 Description = "適用する法線再計算 除外セットの索引", Required = true)]
        public int SetIndex { get; }
        public LoadNormalExcludeSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>除外セットを削除</summary>
    [PLCommand(Description = "除外セットを削除</summary>")]
    public class DeleteNormalExcludeSetCommand : PanelCommand
    {
        [PLParam(TextKey = "NormalExcludeSetIndex",
                 Description = "削除する法線再計算 除外セットの索引", Required = true)]
        public int SetIndex { get; }
        public DeleteNormalExcludeSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>除外セットの名前を変更</summary>
    [PLCommand(Description = "除外セットの名前を変更</summary>")]
    public class RenameNormalExcludeSetCommand : PanelCommand
    {
        [PLParam(TextKey = "NormalExcludeSetIndex",
                 Description = "名前を変える法線再計算 除外セットの索引", Required = true)]
        public int SetIndex { get; }

        [PLParam(TextKey = "NormalExcludeSetNewName",
                 Description = "法線再計算 除外セットの新しい名前", Required = true)]
        public string NewName { get; }
        public RenameNormalExcludeSetCommand(int modelIndex, int setIndex, string newName)
            : base(modelIndex) { SetIndex = setIndex; NewName = newName; }
    }

    // ================================================================
    // 法線編集
    // ================================================================

    /// <summary>
    /// 選択範囲の法線を編集する。対象は選択中の描画メッシュ（未選択なら編集対象メッシュ単体）。
    ///
    /// 各メッシュ内の対象範囲は次のルールで決まる（NormalEditOps.CollectTargetCorners）。
    ///   面選択がある     → その面のコーナーのみ
    ///   頂点選択のみある → その頂点が参照する全スロット
    ///   選択が無い       → メッシュ全体
    /// ただし RecalcByAngle だけはメッシュ全体が対象（スロットを作り直すため）。
    /// </summary>
    [PLCommand(Description = "選択範囲の法線を編集する。対象は選択中の描画メッシュ（未選択なら編集対象メッシュ単体）。")]
    public class NormalEditCommand : PanelCommand
    {
        /// <summary>
        /// AlignToAxis / FlattenOnAxis で指せる軸の数（X / Y / Z）。
        /// パネル側の選択肢（PlayerNormalEditSubPanel の AxisNames）も同数で対応する。
        /// ミラー軸（BakeMirrorCommand.MirrorAxisCount）とは別物なので共有しない。
        /// </summary>
        public const int AxisCount = 3;

        public enum Op
        {
            /// <summary>スムージング角で法線を作り直す（メッシュ全体・スロット再構築）</summary>
            RecalcByAngle,
            /// <summary>面法線にする（フラット化）</summary>
            SetFromFaces,
            /// <summary>
            /// 対象コーナーの面法線だけを頂点ごとに重み付き平均し、その1本を書く。
            /// スロット数は変えない。選択した面だけを使った頂点法線が得られる。
            /// </summary>
            AverageFromFaces,
            /// <summary>統合（頂点上のスロット法線を同一値にする）</summary>
            Unify,
            /// <summary>分離（面ごとに別スロットへ分け、面法線を入れる）</summary>
            Break,
            /// <summary>対象法線を1方向（全体の平均）に揃える</summary>
            AverageAll,
            /// <summary>隣接頂点の法線と補間して平滑化</summary>
            Smooth,
            /// <summary>球状化（中心から外向き）</summary>
            Sphereize,
            /// <summary>ターゲットへ向ける</summary>
            PointToTarget,
            /// <summary>指定軸方向へ向ける</summary>
            AlignToAxis,
            /// <summary>指定軸の成分をゼロにする</summary>
            FlattenOnAxis,
            /// <summary>
            /// ミラー対応（X軸対称）。中央近傍（|Position.x| ≦ MirrorThreshold）の
            /// 頂点だけ法線の X 成分をゼロにする。
            /// </summary>
            MirrorFlattenSeamX,
            /// <summary>反転</summary>
            Flip,
        }

        [PLParam(TextKey = "NormalEditOperation",
                 Description = "法線に対して何をするか", Required = true)]
        public Op    Operation  { get; }

        /// <summary>RecalcByAngle のスムージング角（度）</summary>
        [PLParam(TextKey = "NormalEditAngleDeg",
                 Description = "RecalcByAngle のスムージング角（度）。既定は 59.5",
                 LimitKey = "NormalEdit.AngleDeg")]
        public float AngleDeg   { get; }

        /// <summary>Smooth の強度（0-1）</summary>
        [PLParam(TextKey = "NormalEditStrength",
                 Description = "Smooth の強度。既定は 0.5",
                 LimitKey = "NormalEdit.Strength")]
        public float Strength   { get; }

        /// <summary>AlignToAxis / FlattenOnAxis の軸（0=X, 1=Y, 2=Z）</summary>
        [PLParam(TextKey = "NormalEditAxis",
                 Description = "AlignToAxis / FlattenOnAxis の軸。0=X, 1=Y, 2=Z。既定は 0",
                 Min = 0, Max = AxisCount - 1)]
        public int   Axis       { get; }

        /// <summary>AlignToAxis の符号（true で負方向）</summary>
        [PLParam(TextKey = "NormalEditNegative",
                 Description = "AlignToAxis を負方向にする。既定は false")]
        public bool  Negative   { get; }

        /// <summary>Sphereize / PointToTarget の座標</summary>
        [PLParam(TextKey = "NormalEditTarget",
                 Description = "Sphereize の中心 / PointToTarget の向き先")]
        public Vector3 Target   { get; }

        /// <summary>Sphereize の中心に選択の重心を使うか</summary>
        [PLParam(TextKey = "NormalEditUseSelectionCenter",
                 Description = "Sphereize の中心に選択の重心を使う。既定は true")]
        public bool  UseSelectionCenter { get; }

        /// <summary>PointToTarget で 1 本のベクトルに揃えるか</summary>
        [PLParam(TextKey = "NormalEditAlignVectors",
                 Description = "PointToTarget で 1 本のベクトルに揃える。既定は false")]
        public bool  AlignVectors { get; }

        /// <summary>平均時の重み付け方式</summary>
        [PLParam(TextKey = "NormalEditWeightMode",
                 Description = "面法線を平均するときの重み付け。既定は Uniform")]
        public NormalWeightMode WeightMode { get; }
        /// <summary>
        /// MirrorFlattenSeamX の中央判定しきい値。
        /// |Vertex.Position.x| がこの値以下の頂点を中央（合わせ目）とみなす。
        /// </summary>
        [PLParam(TextKey = "NormalEditMirrorThreshold",
                 Description = "MirrorFlattenSeamX の中央判定しきい値。既定は 0.00001",
                 LimitKey = "NormalEdit.MirrorThreshold")]
        public float MirrorThreshold { get; }

        public NormalEditCommand(
            int modelIndex,
            Op operation,
            float angleDeg = 59.5f,
            float strength = 0.5f,
            int axis = 0,
            bool negative = false,
            Vector3 target = default,
            bool useSelectionCenter = true,
            bool alignVectors = false,
            NormalWeightMode weightMode = NormalWeightMode.Uniform,
            float mirrorThreshold = 0.00001f)
            : base(modelIndex)
        {
            Operation          = operation;
            AngleDeg           = angleDeg;
            Strength           = strength;
            Axis               = axis;
            Negative           = negative;
            Target             = target;
            UseSelectionCenter = useSelectionCenter;
            AlignVectors       = alignVectors;
            WeightMode         = weightMode;
            MirrorThreshold    = mirrorThreshold;
        }
    }

    /// <summary>
    /// 頂点IDの修復。対象は選択中の描画メッシュ（未選択なら編集対象メッシュ単体）。
    ///
    /// 頂点IDはモデル間・オブジェクト間の突き合わせに使う唯一の手掛かりだが、
    /// 未設定・重複・誤付与が混在しやすい。ID を使う操作の前に整えるための操作。
    /// </summary>
    [PLCommand(Description = "頂点IDの修復。対象は選択中の描画メッシュ（未選択なら編集対象メッシュ単体）。")]
    public class RepairVertexIdsCommand : PanelCommand
    {
        public enum RepairMode
        {
            /// <summary>未設定（0 / -1）の頂点にだけ新規IDを割り当てる。既存IDは変更しない。</summary>
            AssignMissing,
            /// <summary>重複IDの 2 個目以降を振り直す。先頭は元のIDを保持する。</summary>
            ResolveDuplicates,
            /// <summary>全頂点に 1 からの連番を振り直す。既存の対応付けは失われる。</summary>
            ReassignSequential,
            /// <summary>全頂点のIDを未設定に戻す。</summary>
            ClearAll,
        }

        [PLParam(TextKey = "RepairVertexIdMode",
                 Description = "未設定のみ / 重複の解消 / 連番振り直し / 全消去", Required = true)]
        public RepairMode Mode { get; }
        public RepairVertexIdsCommand(int modelIndex, RepairMode mode)
            : base(modelIndex) { Mode = mode; }
    }

    /// <summary>
    /// パーツID（Vertex.PartsId）／サブID（Vertex.SubId）の一括採番。
    ///
    /// 【頂点IDとの分離】
    ///   このコマンドは Vertex.Id を読まないし書かない。頂点IDの修復は
    ///   RepairVertexIdsCommand が持つ。両者は独立して掛けられる。
    ///
    /// 【対象】
    ///   TargetMasterIndex で指定した描画オブジェクト 1 つだけ。
    ///   ビューポートの「オブジェクト選択」とは無関係で、選択状態を参照しない。
    ///
    /// 【リファレンス】
    ///   ReferenceVertexCount のときだけ使う。1 つだけ指定する。
    ///   藤壺の配置元が複数オブジェクトだった場合は、あらかじめ 1 つへ結合したものを
    ///   リファレンスに指定すること（このコマンドは結合を行わない）。
    /// </summary>
    [PLCommand(Description = "パーツID（Vertex.PartsId）／サブID（Vertex.SubId）の一括採番。")]
    public class AssignPartsIdsCommand : PanelCommand
    {
        public enum PartsIdMode
        {
            /// <summary>面・線のつながり（独立性）でパーツを分ける。</summary>
            Connectivity,
            /// <summary>リファレンスの頂点数で頂点列を等分してパーツを分ける。</summary>
            ReferenceVertexCount,
            /// <summary>パーツIDはそのままで、サブIDだけ振り直す。</summary>
            SubIdOnly,
            /// <summary>パーツID・サブIDを 0 に戻す。</summary>
            Clear,
        }

        /// <summary>採番する描画オブジェクトの masterIndex。</summary>
        [PLParam(TextKey = "PartsIdTargetMasterIndex",
                 Description = "採番する描画オブジェクトの masterIndex", Required = true)]
        public int TargetMasterIndex { get; }

        [PLParam(TextKey = "PartsIdMode",
                 Description = "つながり / リファレンス頂点数 / サブIDのみ / 消去", Required = true)]
        public PartsIdMode Mode { get; }

        /// <summary>
        /// 1 パーツの頂点数を取る描画オブジェクトの masterIndex。-1 で未指定。
        /// ReferenceVertexCount 以外のモードでは無視する。
        /// </summary>
        [PLParam(TextKey = "PartsIdReferenceMasterIndex",
                 Description = "1 パーツの頂点数を取るオブジェクトの masterIndex。-1 で未指定")]
        public int ReferenceMasterIndex { get; }

        /// <summary>面にも線にも属さない頂点の扱い。Connectivity のときだけ効く。</summary>
        [PLParam(TextKey = "PartsIdIsolatedPolicy",
                 Description = "孤立頂点をまとめて 1 パーツにするか、1 つずつ独立させるか")]
        public IsolatedVertexPolicy IsolatedPolicy { get; }

        public AssignPartsIdsCommand(
            int modelIndex,
            int targetMasterIndex,
            PartsIdMode mode,
            int referenceMasterIndex = -1,
            IsolatedVertexPolicy isolatedPolicy = IsolatedVertexPolicy.SingleGroup)
            : base(modelIndex)
        {
            TargetMasterIndex    = targetMasterIndex;
            Mode                 = mode;
            ReferenceMasterIndex = referenceMasterIndex;
            IsolatedPolicy       = isolatedPolicy;
        }
    }

    /// <summary>
    /// ボーンウェイトの組み合わせでパーツID（Vertex.PartsId）／サブID（Vertex.SubId）を
    /// 振り直す。
    ///
    /// 【頂点IDとの分離】
    ///   このコマンドは Vertex.Id を読まないし書かない。AssignPartsIdsCommand と同じ。
    ///
    /// 【対象】
    ///   TargetMasterIndex で指定した描画オブジェクト 1 つだけ。
    ///   ビューポートの「オブジェクト選択」とは無関係で、選択状態を参照しない。
    ///
    /// 【採番の規則】
    ///   ウェイト > 0 のスロットだけを有効として、ボーン索引を重複除去して昇順に並べる。
    ///     0 個   … PartsIdOps.UnweightedPartsId（int.MaxValue）
    ///     1 個   … そのボーン索引
    ///     2 個以上 … 同じ組み合わせを 1 群として GroupIdOffset からの連番
    ///   規則の全文と根拠は PartsIdByBoneWeightOps.cs の冒頭にある。
    /// </summary>
    [PLCommand(Description =
        "ボーンウェイトの組み合わせでパーツID（Vertex.PartsId）／サブID（Vertex.SubId）を振り直す。")]
    public class AssignPartsIdsByBoneWeightCommand : PanelCommand
    {
        /// <summary>採番する描画オブジェクトの masterIndex。</summary>
        [PLParam(TextKey = "PartsIdByBoneWeightTargetMasterIndex",
                 Description = "採番する描画オブジェクトの masterIndex", Required = true)]
        public int TargetMasterIndex { get; }

        public AssignPartsIdsByBoneWeightCommand(int modelIndex, int targetMasterIndex)
            : base(modelIndex)
        {
            TargetMasterIndex = targetMasterIndex;
        }
    }

    /// <summary>
    /// パーツID（Vertex.PartsId）で描画オブジェクト 1 つを分解し、
    /// 空のオブジェクトを親にして、パーツごとの描画オブジェクトを子として並べる。
    ///
    /// 【対象】
    ///   TargetMasterIndex で指定した描画オブジェクト 1 つだけ。
    ///   ビューポートの「オブジェクト選択」とは無関係で、選択状態を参照しない。
    ///   元のオブジェクトは削除も非表示もしない。
    ///
    /// 【分解の規則】
    ///   面は必ずどれか 1 つの子へ入る（取りこぼしも多重化も無し）。
    ///   頂点は重複してよい。規則の全文と根拠は PartsIdSplitOps.cs の冒頭にある。
    /// </summary>
    [PLCommand(Description =
        "パーツID（Vertex.PartsId）で描画オブジェクトを分解し、空のオブジェクトの子として並べる。")]
    public class SplitObjectByPartsIdCommand : PanelCommand
    {
        /// <summary>分解する描画オブジェクトの masterIndex。</summary>
        [PLParam(TextKey = "PartsIdSplitTargetMasterIndex",
                 Description = "分解する描画オブジェクトの masterIndex", Required = true)]
        public int TargetMasterIndex { get; }

        public SplitObjectByPartsIdCommand(int modelIndex, int targetMasterIndex)
            : base(modelIndex)
        {
            TargetMasterIndex = targetMasterIndex;
        }
    }

    /// <summary>
    /// モデル間・オブジェクト間で頂点データを転送する。
    ///
    /// メッシュのペアは SourceMeshIndices[i] ↔ TargetMeshIndices[i] で明示する
    /// （リスト順に暗黙で対応させない）。両配列は同じ長さであること。
    /// インデックスは各モデルの MeshContextList のインデックス。
    /// </summary>
    [PLCommand(Description = "モデル間・オブジェクト間で頂点データを転送する。")]
    public class TransferVertexDataCommand : PanelCommand
    {
        /// <summary>転送元モデル（PanelCommand.ModelIndex）。</summary>
        public int SourceModelIndex => ModelIndex;

        /// <summary>転送先モデル。</summary>
        [PLParam(TextKey = "TransferTargetModelIndex",
                 Description = "転送先モデルの索引", Required = true)]
        public int   TargetModelIndex  { get; }

        [PLParam(TextKey = "TransferSourceMeshIndices",
                 Description = "転送元メッシュの索引。TargetMeshIndices と同じ長さ", Required = true)]
        public int[] SourceMeshIndices { get; }

        [PLParam(TextKey = "TransferTargetMeshIndices",
                 Description = "転送先メッシュの索引。SourceMeshIndices と同じ長さ", Required = true)]
        public int[] TargetMeshIndices { get; }

        [PLParam(TextKey = "TransferMatchMode",
                 Description = "頂点の突き合わせ方", Required = true)]
        public VertexMatchMode MatchMode { get; }

        [PLParam(TextKey = "TransferKinds",
                 Description = "転送する頂点データの種類", Required = true)]
        public VertexDataKind  Kinds     { get; }

        public TransferVertexDataCommand(
            int sourceModelIndex, int targetModelIndex,
            int[] sourceMeshIndices, int[] targetMeshIndices,
            VertexMatchMode matchMode, VertexDataKind kinds)
            : base(sourceModelIndex)
        {
            TargetModelIndex  = targetModelIndex;
            SourceMeshIndices = sourceMeshIndices;
            TargetMeshIndices = targetMeshIndices;
            MatchMode         = matchMode;
            Kinds             = kinds;
        }
    }

    /// <summary>メッシュ選択辞書をCSVファイルへ保存</summary>
    [PLCommand(Description = "メッシュ選択辞書をCSVファイルへ保存</summary>")]
    public class SaveMeshSelSetsCsvCommand : PanelCommand
    {
        [PLParam(TextKey = "MeshSelSetsSavePath",
                 Description = "書き出し先の CSV。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string FilePath { get; }
        public SaveMeshSelSetsCsvCommand(int modelIndex, string filePath)
            : base(modelIndex) { FilePath = filePath; }
    }

    /// <summary>メッシュ選択辞書をCSVファイルから読込み、既存リストへ追加</summary>
    [PLCommand(Description = "メッシュ選択辞書をCSVファイルから読込み、既存リストへ追加</summary>")]
    public class LoadMeshSelSetsCsvCommand : PanelCommand
    {
        [PLParam(TextKey = "MeshSelSetsLoadPath",
                 Description = "読み込む CSV。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string FilePath { get; }
        public LoadMeshSelSetsCsvCommand(int modelIndex, string filePath)
            : base(modelIndex) { FilePath = filePath; }
    }

    // ================================================================
    // 法線移植
    // ================================================================

    /// <summary>
    /// ビフォー／アフターの2オブジェクトが作るシェル（プリズム群）から、
    /// ターゲットオブジェクトの各頂点へ法線を移植する。Undo 記録付き。
    ///
    /// ビフォーとアフターは同一トポロジ（面数・各面のコーナー数が一致）であること。
    /// 4角形以上の面は i0 / i_k / i_k+1 の扇で三角形化される。
    ///
    /// スキニング無しを前提とする。法線の空間変換はオブジェクト単位の
    /// MeshContext.WorldMatrix だけを使う。
    /// </summary>
    [PLCommand(Description = "ビフォー／アフターの2オブジェクトが作るシェル（プリズム群）から、 ターゲットオブジェクトの各頂点へ法線を移植する。")]
    public class ApplyNormalTransplantCommand : PanelCommand
    {
        /// <summary>ビフォー（内側の面）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "TransplantBeforeMasterIndex",
                 Description = "ビフォー（内側の面）の masterIndex", Required = true)]
        public int   BeforeMasterIndex   { get; }

        /// <summary>アフター（外側の面）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "TransplantAfterMasterIndex",
                 Description = "アフター（外側の面）の masterIndex", Required = true)]
        public int   AfterMasterIndex    { get; }

        /// <summary>法線を差し替える MeshContext の MasterIndex 配列</summary>
        [PLParam(TextKey = "TransplantTargetMasterIndices",
                 Description = "法線を差し替える描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] TargetMasterIndices { get; }

        /// <summary>適用率 [0, 1]。1 未満なら元の法線と Slerp する。</summary>
        [PLParam(TextKey = "TransplantStrength",
                 Description = "適用率。1 未満なら元の法線と球面補間する。既定は 1",
                 LimitKey = "NormalTransplant.Strength")]
        public float Strength            { get; }
        /// <summary>
        /// true : 三角形内を球面補間する
        /// false: 三角形内を線形補間する（既定）
        /// </summary>
        [PLParam(TextKey = "TransplantSpherical",
                 Description = "三角形内を球面補間する。false で線形補間。既定は false")]
        public bool  Spherical           { get; }
        /// <summary>
        /// true : どのプリズムにも入らない頂点を最も近いプリズムへ寄せる
        /// false: どのプリズムにも入らない頂点は変更しない（既定）
        /// </summary>
        [PLParam(TextKey = "TransplantAllowNearest",
                 Description = "どのプリズムにも入らない頂点を最も近いプリズムへ寄せる。既定は false")]
        public bool  AllowNearest        { get; }

        public ApplyNormalTransplantCommand(
            int modelIndex,
            int beforeMasterIndex, int afterMasterIndex,
            int[] targetMasterIndices,
            float strength      = 1f,
            bool  spherical     = false,
            bool  allowNearest  = false)
            : base(modelIndex)
        {
            BeforeMasterIndex   = beforeMasterIndex;
            AfterMasterIndex    = afterMasterIndex;
            TargetMasterIndices = targetMasterIndices;
            Strength            = strength;
            Spherical           = spherical;
            AllowNearest        = allowNearest;
        }
    }
}
