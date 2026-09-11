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

    [PLCommand(Description = "空の描画オブジェクトをモデルへ 1 つ足す。")]
    public class AddMeshCommand : PanelCommand
    {
        public AddMeshCommand(int modelIndex) : base(modelIndex) { }
    }

    [PLCommand(Description = "指定したオブジェクトをモデルから消す。")]
    public class DeleteMeshesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public DeleteMeshesCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    [PLCommand(Description = "指定したオブジェクトを複製してモデルへ足す。")]
    public class DuplicateMeshesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public DuplicateMeshesCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    /// <summary>
    /// メッシュリスト順序変更（D&D/上下移動/Indent/Outdent/先頭末尾移動）
    /// </summary>
    [PLCommand(Description = "メッシュリスト順序変更（D&D/上下移動/Indent/Outdent/先頭末尾移動）")]
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

    // ================================================================
    // モデル操作
    // ================================================================

    /// <summary>カレントモデルを切り替える</summary>
    [PLCommand(Description = "カレントモデルを切り替える</summary>")]
    public class SwitchModelCommand : PanelCommand
    {
        [PLParam(TextKey = "TargetModelIndex",
                 Description = "切り替え先のモデルの索引", Required = true)]
        public int TargetModelIndex { get; }
        public SwitchModelCommand(int targetModelIndex)
            : base(targetModelIndex) { TargetModelIndex = targetModelIndex; }
    }

    /// <summary>モデルの名前を変更する</summary>
    [PLCommand(Description = "モデルの名前を変更する</summary>")]
    public class RenameModelCommand : PanelCommand
    {
        [PLParam(TextKey = "ModelNewName",
                 Description = "モデルの新しい名前", Required = true)]
        public string NewName { get; }
        public RenameModelCommand(int modelIndex, string newName)
            : base(modelIndex) { NewName = newName; }
    }

    /// <summary>モデルを削除する</summary>
    [PLCommand(Description = "モデルを削除する</summary>")]
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
    [PLCommand(Description = "選択メッシュオブジェクト群をひとつにマージする。")]
    public class MergeMeshesCommand : PanelCommand
    {
        /// <summary>マージ対象の MasterIndex 配列（基準オブジェクトを含む）</summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        /// <summary>基準オブジェクトの MasterIndex</summary>
        [PLParam(TextKey = "MergeBaseMasterIndex",
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
    [PLCommand(Description = "オブジェクトグループを作り直す。ソースの編集が出力先へ反映される。")]
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
    /// オブジェクトグループを解除する。描画オブジェクトは消さない。
    /// </summary>
    [PLCommand(Description = "オブジェクトグループを解除する。出力先の描画オブジェクトは残る。")]
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
    [PLCommand(Description = "オブジェクトグループを 1 つにまとめる。ソースの全ステップをターゲットの末尾へ移し、ソースのグループを消す。描画オブジェクトは消さない。")]
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
    [PLCommand(Description = "オブジェクトグループの自動更新の可否を切り替える。")]
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
