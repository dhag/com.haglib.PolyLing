// PanelCommand.MeshList.cs
// オブジェクトリスト操作・モデル操作・メッシュマージ・オブジェクトグループの操作要求。
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
    // リスト操作
    // ================================================================

    /// <summary>
    /// リファレンスに基づく対称化（臨時）。対象を複製し、REF から求めた対称の対応に従って
    /// 移植元側の頂点を反対側へ移植したクローンを足す。REF・対象は書き換えない。
    /// 従来 PlayerReferenceSymmetrySubPanel が ReferenceSymmetryOperation を直接呼んでいたものを移した。
    /// </summary>
    [PLCommand(Category = "mirror", Writes = PLWriteScope.AddOnly, Description = "REF の対称の対応に従い、対象のクローンの片側を反対側から移植して足す（REF・対象は変えない）。")]
    public class ApplyReferenceSymmetryCommand : PanelCommand
    {
        [PLParam(TextKey = "ReferenceMasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "左右対称な参照オブジェクトの索引", Required = true)]
        public int ReferenceMasterIndex { get; }
        [PLParam(TextKey = "TargetMasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "移植する対象の索引（複製して使う）", Required = true)]
        public int TargetMasterIndex { get; }
        [PLParam(TextKey = "SymmetryTolerance", Description = "REF の対称点を照合する位置誤差")]
        public float Tolerance { get; }
        [PLParam(TextKey = "RecalculateNormals", Description = "生成後に法線を再計算する")]
        public bool RecalculateNormals { get; }
        [PLParam(TextKey = "NewObjectName", Description = "作成するオブジェクトの名前（空なら「対象名_対称」。重複時は末尾に番号）")]
        public string NewObjectName { get; }
        [PLParam(TextKey = "SourcePositiveX", Description = "移植元を正 X 側にする（false なら負 X 側から正 X 側へ）")]
        public bool SourcePositiveX { get; }

        public ApplyReferenceSymmetryCommand(int modelIndex, int referenceMasterIndex, int targetMasterIndex,
            float tolerance = 0.0001f, bool recalculateNormals = true, string newObjectName = "", bool sourcePositiveX = true)
            : base(modelIndex)
        {
            ReferenceMasterIndex = referenceMasterIndex; TargetMasterIndex = targetMasterIndex;
            Tolerance = tolerance; RecalculateNormals = recalculateNormals;
            NewObjectName = newObjectName ?? ""; SourcePositiveX = sourcePositiveX;
        }
    }

    [PLCommand(Category = "object.list", Writes = PLWriteScope.AddOnly, Description = "空の描画オブジェクトをモデルへ 1 つ足す。")]
    public class AddMeshCommand : PanelCommand
    {
        public AddMeshCommand(int modelIndex) : base(modelIndex) { }
    }

    [PLCommand(Category = "object.list", Effects = PLCommandEffect.DeletesObject, Hazards = PLCommandHazard.RequiresUserConfirmation, Writes = PLWriteScope.Targets, Description = "指定したオブジェクトをモデルから消す。")]
    public class DeleteMeshesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public DeleteMeshesCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    [PLCommand(Category = "object.list", Writes = PLWriteScope.AddOnly, Description = "指定したオブジェクトを複製してモデルへ足す。")]
    public class DuplicateMeshesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public DuplicateMeshesCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    /// <summary>
    /// メッシュリスト順序変更（D&D/上下移動/Indent/Outdent/先頭末尾移動）
    /// </summary>
    [PLCommand(Category = "object.list", Effects = PLCommandEffect.Hierarchy, Writes = PLWriteScope.ModelWide, Description = "メッシュリスト順序変更（D&D/上下移動/Indent/Outdent/先頭末尾移動）")]
    public class ReorderMeshesCommand : PanelCommand
    {
        public struct ReorderEntry
        {
            public int MasterIndex;
            public int NewDepth;
            public int NewParentMasterIndex;
        }

        [PLParam(TextKey = "ReorderCategory",
                 Description = "並べ替える対象リストの分類", Required = true)]
        public MeshCategory Category { get; }

        /// <summary>
        /// 各行の移動先を平たく並べたもの。1 行ぶん 3 個。
        ///   [i*3]   = masterIndex
        ///   [i*3+1] = newDepth
        ///   [i*3+2] = newParentMasterIndex（親なしは -1）
        ///
        /// ReorderEntry[] のまま持つとスキーマに出せない
        /// （要素が構造体の配列は対応表に無い）ため、平たい整数列にしてある。
        /// </summary>
        [PLParam(TextKey = "ReorderEntryValues",
                 Description = "各行の移動先。masterIndex / newDepth / newParentMasterIndex を 3 個ずつ並べる",
                 Required = true)]
        public int[] EntryValues { get; }

        /// <summary>EntryValues から起こした行。受け口はこちらを使う。</summary>
        public ReorderEntry[] Entries
        {
            get
            {
                int n = (EntryValues?.Length ?? 0) / 3;
                var a = new ReorderEntry[n];
                for (int i = 0; i < n; i++)
                    a[i] = new ReorderEntry
                    {
                        MasterIndex          = EntryValues[i * 3],
                        NewDepth             = EntryValues[i * 3 + 1],
                        NewParentMasterIndex = EntryValues[i * 3 + 2],
                    };
                return a;
            }
        }

        /// <summary>
        /// ReorderEntry[] を平たい整数列にする。呼び出し側の書き換えを短くするための補助。
        /// コンストラクタの多重定義にはしない（引数が同数だと
        /// PanelCommandFactory.PickConstructor の選択が不定になるため）。
        /// </summary>
        public static int[] ToEntryValues(ReorderEntry[] entries)
        {
            if (entries == null) return System.Array.Empty<int>();
            var a = new int[entries.Length * 3];
            for (int i = 0; i < entries.Length; i++)
            {
                a[i * 3]     = entries[i].MasterIndex;
                a[i * 3 + 1] = entries[i].NewDepth;
                a[i * 3 + 2] = entries[i].NewParentMasterIndex;
            }
            return a;
        }

        /// <summary>
        /// 親を付け替えたとき、ワールド姿勢を保つようローカル姿勢を組み直すか。
        ///
        /// ComputeWorldMatrices は「親のワールド × 自身のローカル」と積む
        /// （ModelContext.cs:1746-1748）ので、組み直さないと親が付いた瞬間に
        /// 子のワールド位置が親のぶんだけ飛ぶ。
        /// Unity の Transform.SetParent(parent, worldPositionStays: true) と同じ扱いを既定にする。
        ///
        /// false にすると付け替え前のローカル値がそのまま残る（従来の挙動）。
        /// 親からの相対値を直接入れてある場合はこちらを使う。
        /// </summary>
        [PLParam(TextKey = "PreserveWorldTransform",
                 Description = "親を付け替えてもワールド姿勢を保つ")]
        public bool PreserveWorldTransform { get; }

        public ReorderMeshesCommand(
            int modelIndex, MeshCategory category, int[] entryValues,
            bool preserveWorldTransform = true)
            : base(modelIndex)
        {
            Category               = category;
            EntryValues            = entryValues ?? System.Array.Empty<int>();
            PreserveWorldTransform = preserveWorldTransform;
        }
    }

    /// <summary>
    /// 名前で親子を張る。
    ///
    /// 【なぜ要るか】
    ///   reorderMeshes は索引・深さ・親の索引を 3 個ずつ並べて渡す。索引は
    ///   描画オブジェクトの増減でずれ、深さは親の深さから数える必要があるので、
    ///   手本に書くと流すたびに手で計算し直すことになる（robot_build_hierarchy の e3）。
    ///   名前の組だけを受け取り、索引と深さはここで引く。
    ///
    /// 【ボーンには使わない】
    ///   ボーンの付け替えは setBoneParent（ボーン以外は飛ばす）。
    /// </summary>
    [PLCommand(Category = "object.list", Writes = PLWriteScope.ModelWide, Description = "名前の組で描画オブジェクトの親子を張る。names[i] の親を parentNames[i] にする（空で親なし）。名前は完全一致で引き、見つからない・同じ名前が複数ある・親子が輪になるときは失敗する。深さは親の深さ+1 で数え、親→子の順に並べて reorderMeshes（ワールド姿勢を保つ）で張る。")]
    [PLResult("names",               PLResultKind.TextArray,    Description = "親を張ったオブジェクトの名前")]
    [PLResult("masterIndices",       PLResultKind.IntegerArray, Description = "実行後の masterIndex。names と同じ並び")]
    [PLResult("parentMasterIndices", PLResultKind.IntegerArray, Description = "実行後に読み直した親の masterIndex。親なしは -1。names と同じ並び")]
    [PLResult("depths",              PLResultKind.IntegerArray, Description = "実行後に読み直した深さ。names と同じ並び")]
    public class SetParentsByNameCommand : PanelCommand
    {
        [PLParam(Description = "親を張る描画オブジェクトの名前", Required = true)]
        public string[] Names { get; }

        [PLParam(Description = "names と同じ並びの親の名前。親なしは空の要素（引用符で囲んだ \"\"。例: 上半身,\"\" ）", Required = true)]
        public string[] ParentNames { get; }

        public SetParentsByNameCommand(int modelIndex, string[] names, string[] parentNames)
            : base(modelIndex)
        {
            Names       = names       ?? System.Array.Empty<string>();
            ParentNames = parentNames ?? System.Array.Empty<string>();
        }
    }

    // ================================================================
    // モデル操作
    // ================================================================

    /// <summary>カレントモデルを切り替える</summary>
    [PLCommand(Category = "object.model", Writes = PLWriteScope.None, Description = "編集対象のモデルを切り替える。")]
    public class SwitchModelCommand : PanelCommand
    {
        [PLParam(TextKey = "TargetModelIndex",
                 Description = "切り替え先のモデルの索引", Required = true)]
        public int TargetModelIndex { get; }
        public SwitchModelCommand(int targetModelIndex)
            : base(targetModelIndex) { TargetModelIndex = targetModelIndex; }
    }

    /// <summary>モデルの名前を変更する</summary>
    [PLCommand(Category = "object.model", Writes = PLWriteScope.ModelWide, Description = "モデルの名前を変える。")]
    public class RenameModelCommand : PanelCommand
    {
        [PLParam(TextKey = "ModelNewName",
                 Description = "モデルの新しい名前", Required = true)]
        public string NewName { get; }
        public RenameModelCommand(int modelIndex, string newName)
            : base(modelIndex) { NewName = newName; }
    }

    /// <summary>モデルを削除する</summary>
    [PLCommand(Category = "object.model", Writes = PLWriteScope.ModelWide, Description = "モデルを消す。")]
    public class DeleteModelCommand : PanelCommand
    {
        public DeleteModelCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // メッシュマージ
    // ================================================================

    /// <summary>
    /// 選択メッシュオブジェクト群をひとつにマージする。
    /// BaseMasterIndex のオブジェクトを基準トランスフォームとして使用する。
    /// CreateNewMesh が true の場合は新規メッシュオブジェクトを作成して結果を格納する。
    /// false の場合は BaseMasterIndex のメッシュオブジェクトに直接結合する。
    /// </summary>
    [PLCommand(Category = "object.list", Writes = PLWriteScope.Targets, Description = "選択メッシュオブジェクト群をひとつにマージする。")]
    public class MergeMeshesCommand : PanelCommand
    {
        /// <summary>マージ対象の MasterIndex 配列（基準オブジェクトを含む）</summary>
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 WriteWhen = "createNewMesh=false",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        /// <summary>基準オブジェクトの MasterIndex</summary>
        [PLParam(TextKey = "MergeBaseMasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 WriteWhen = "createNewMesh=false",
                 Description = "結合の基準になるオブジェクトの masterIndex", Required = true)]
        public int BaseMasterIndex { get; }

        /// <summary>true: 新規メッシュオブジェクトに結果を格納する</summary>
        [PLParam(TextKey = "MergeCreateNewMesh",
                 Description = "結果を新規オブジェクトに入れる。false で基準へ直接結合", Required = true)]
        public bool CreateNewMesh { get; }

        public MergeMeshesCommand(int modelIndex, int[] masterIndices, int baseMasterIndex, bool createNewMesh)
            : base(modelIndex)
        {
            MasterIndices    = masterIndices;
            BaseMasterIndex  = baseMasterIndex;
            CreateNewMesh    = createNewMesh;
        }
    }

    // ================================================================
    // オブジェクトグループ
    // ================================================================

    /// <summary>
    /// オブジェクトグループを作り直す。
    ///
    /// 記録してある生成コマンドを今のモデルに合わせて組み直し、実行する。
    /// 参照は ObjectId で控えてあるので、並べ替えや追加削除のあとでも
    /// 同じオブジェクトを指す。ソースが編集されていれば、梯子の位置は
    /// 頂点IDから引き直されるので新しい形になる。
    /// </summary>
    [PLCommand(Category = "object.group", Effects = PLCommandEffect.CreatesObject | PLCommandEffect.DeletesObject | PLCommandEffect.Topology, Hazards = PLCommandHazard.AffectsMultipleObjects, Verification = PLCommandVerification.Visual, Writes = PLWriteScope.ModelWide, Description = "オブジェクトグループを作り直す。ソースの編集が出力先へ反映される。")]
    public class RebuildObjectGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "ObjectGroupName", Description = "作り直すグループの名前", Required = true)]
        public string GroupName { get; }

        /// <summary>
        /// 作り直す前の出力先を退避として残すか。
        /// 残すと頂点ID・パーツIDを保った複製が別オブジェクトとして残る。
        /// </summary>
        [PLParam(TextKey = "ObjectGroupKeepStash", Description = "作り直す前の出力先を退避として残す")]
        public bool KeepStash { get; }

        public RebuildObjectGroupCommand(int modelIndex, string groupName, bool keepStash = false)
            : base(modelIndex) { GroupName = groupName; KeepStash = keepStash; }
    }

    /// <summary>
    /// 参照先（出力先など）が消えたオブジェクトグループを片づける（ObjectGroupOps.PurgeMissing）。
    /// 描画オブジェクトは消さない。
    /// </summary>
    [PLCommand(Category = "object.group", Writes = PLWriteScope.ModelWide, Description = "参照先が消えたオブジェクトグループを片づける。描画オブジェクトは消さない。")]
    public class PurgeObjectGroupsCommand : PanelCommand
    {
        public PurgeObjectGroupsCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>
    /// オブジェクトグループを解除する。描画オブジェクトは消さない。
    /// </summary>
    [PLCommand(Category = "object.group", Writes = PLWriteScope.ModelWide, Description = "オブジェクトグループを解除する。出力先の描画オブジェクトは残る。")]
    public class DeleteObjectGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "ObjectGroupName", Description = "解除するグループの名前", Required = true)]
        public string GroupName { get; }

        public DeleteObjectGroupCommand(int modelIndex, string groupName)
            : base(modelIndex) { GroupName = groupName; }
    }

    /// <summary>
    /// オブジェクトグループを 1 つにまとめる（マクロを組む）。
    ///
    /// ソースの全ステップをターゲットの末尾へ移し、ソースのグループを消す。
    /// 描画オブジェクトは 1 つも消さない。
    ///
    /// 【なぜ生成コマンド側で「どのグループへ足すか」を指定しないか】
    ///   生成コマンドに足し先を持たせると、フリル・パイプ・藤壺・鎖の全部へ
    ///   同じパラメータを足すことになり、往復と検査の面が広がる。
    ///   1 コマンド＝1 グループとして作ってから順に足す形なら、
    ///   足す側の知識はこのコマンド 1 つに収まる。
    ///
    /// 【並び順】
    ///   ステップの実行順はリストの並びそのもの。足した順に実行される。
    /// </summary>
    [PLCommand(Category = "object.group", Writes = PLWriteScope.ModelWide, Description = "オブジェクトグループを 1 つにまとめる。ソースの全ステップをターゲットの末尾へ移し、ソースのグループを消す。描画オブジェクトは消さない。")]
    public class MergeObjectGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "ObjectGroupName", Description = "足し先のグループの名前", Required = true)]
        public string TargetGroupName { get; }

        [PLParam(TextKey = "ObjectGroupName", Description = "足すグループの名前。まとめたあと消える", Required = true)]
        public string SourceGroupName { get; }

        public MergeObjectGroupCommand(int modelIndex, string targetGroupName, string sourceGroupName)
            : base(modelIndex)
        {
            TargetGroupName = targetGroupName;
            SourceGroupName = sourceGroupName;
        }
    }

    /// <summary>
    /// オブジェクトグループの自動更新の可否を切り替える。
    /// </summary>
    [PLCommand(Category = "object.group", Effects = PLCommandEffect.ObjectAttribute, Writes = PLWriteScope.ModelWide, Description = "オブジェクトグループの自動更新の可否を切り替える。")]
    public class SetObjectGroupAutoUpdateCommand : PanelCommand
    {
        [PLParam(TextKey = "ObjectGroupName", Description = "対象のグループ名", Required = true)]
        public string GroupName { get; }

        [PLParam(TextKey = "ObjectGroupAutoUpdate", Description = "ソースが変わったら自動で作り直す", Required = true)]
        public bool AutoUpdate { get; }

        public SetObjectGroupAutoUpdateCommand(int modelIndex, string groupName, bool autoUpdate)
            : base(modelIndex) { GroupName = groupName; AutoUpdate = autoUpdate; }
    }
}
