// PanelCommand.MeshAttribute.cs
// 描画オブジェクトの属性変更と面の表示・非表示の操作要求。
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
    // 属性変更
    // ================================================================

    [PLCommand(Description = "オブジェクト 1 つの表示・非表示を切り替える。")]
    public class ToggleVisibilityCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }
        public ToggleVisibilityCommand(int modelIndex, int masterIndex)
            : base(modelIndex) { MasterIndex = masterIndex; }
    }

    [PLCommand(Description = "複数オブジェクトの表示・非表示を一括で設定する。")]
    public class SetBatchVisibilityCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BatchVisible",
                 Description = "表示する / 隠す", Required = true)]
        public bool Visible { get; }
        public SetBatchVisibilityCommand(int modelIndex, int[] masterIndices, bool visible)
            : base(modelIndex) { MasterIndices = masterIndices; Visible = visible; }
    }

    [PLCommand(Description = "オブジェクト 1 つのロックを切り替える。ロック中は編集できない。")]
    public class ToggleLockCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }
        public ToggleLockCommand(int modelIndex, int masterIndex)
            : base(modelIndex) { MasterIndex = masterIndex; }
    }

    /// <summary>
    /// 複数オブジェクトのロック状態を一括設定する。
    /// オブジェクトリストの行内ロックボタンを、選択が複数あるときに使う。
    /// </summary>
    [PLCommand(Description = "複数オブジェクトのロック状態を一括設定する。")]
    public class SetBatchLockCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BatchLocked",
                 Description = "ロックする / 解除する", Required = true)]
        public bool  Locked        { get; }
        public SetBatchLockCommand(int modelIndex, int[] masterIndices, bool locked)
            : base(modelIndex) { MasterIndices = masterIndices; Locked = locked; }
    }

    /// <summary>
    /// ミラーの有無そのものを切り替える。属性を書くだけの
    /// SetBatchMirrorTypeCommand と違い、ミラー側 MeshContext を作る／始末する。
    ///
    /// 【解消（Enabled = false）】
    ///   MirrorGeometryDerived = true （MQO 系）… ミラー側を破棄する。
    ///       実体側から再生成できるため、残しても情報が増えない。
    ///   同 false （PMX 系）… ミラー側を独立メッシュにする（Type = Mesh）。
    ///       ボーンウェイトなど実体側から復元できない情報を持つため破棄しない。
    ///       実体側の DetachedMirrorObjectId に相手を控える。
    ///
    /// 【有効化（Enabled = true）】
    ///   DetachedMirrorObjectId が有効 … その相手を引き当てて再ペアする。
    ///   無い場合                      … 実体側から生成ミラーを作る。
    /// </summary>
    [PLCommand(Description = "ミラーの有無そのものを切り替える。種別を変えるだけの操作と違い、ミラー側のオブジェクトを作る／片付ける。")]
    public class SetMirrorEnabledCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "MirrorEnabled",
                 Description = "ミラーを有効にする / 解消する", Required = true)]
        public bool  Enabled       { get; }
        public SetMirrorEnabledCommand(int modelIndex, int[] masterIndices, bool enabled)
            : base(modelIndex) { MasterIndices = masterIndices; Enabled = enabled; }
    }

    /// <summary>
    /// 複数オブジェクトのミラータイプを一括設定する。
    /// 値は CycleMirrorTypeCommand と同じ 0..2 の範囲（0=なし / 1=分離 / 2=結合）。
    /// 上限は MirrorViewUtil.MirrorTypeCount が正典で、3 以上は MQO へ不正値として
    /// 書き出されるため作らない（MirrorViewUtil.cs:43-52）。
    /// </summary>
    [PLCommand(Description = "複数オブジェクトのミラータイプを一括設定する。")]
    public class SetBatchMirrorTypeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BatchMirrorType",
                 Description = "ミラータイプ。0=なし, 1=分離, 2=結合",
                 Min = 0, Max = Poly_Ling.View.MirrorViewUtil.MirrorTypeCount - 1, Required = true)]
        public int   MirrorType    { get; }
        public SetBatchMirrorTypeCommand(int modelIndex, int[] masterIndices, int mirrorType)
            : base(modelIndex) { MasterIndices = masterIndices; MirrorType = mirrorType; }
    }

    /// <summary>
    /// オブジェクトの編集者（担当者）を設定・解放するコマンド。
    ///
    /// EditorName == ""     : 解放（担当者なしに戻す）
    /// EditorName == 自分の名前: 取得（claim）
    /// Force == true        : 他人が担当中でも上書きする（ホスト権限）
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    [PLCommand(Description = "オブジェクトの編集者（担当者）を設定・解放するコマンド。")]
    public class SetObjectEditorCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "EditorName",
                 Description = "担当者の名前。空文字で解放する", Required = true)]
        public string  EditorName    { get; }

        [PLParam(TextKey = "ForceClaim",
                 Description = "他人が担当中でも上書きする。既定は false")]
        public bool    Force         { get; }

        public SetObjectEditorCommand(
            int modelIndex, int[] masterIndices, string editorName,
            ulong[] objectIds = null, bool force = false)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            EditorName    = editorName ?? "";
            Force         = force;
        }
    }

    /// <summary>
    /// IgnorePoseInArmature フラグを設定するコマンド。
    /// true の場合、BoneTransform.Rotation を 0 にリセットする。
    /// </summary>
    [PLCommand(Description = "IgnorePoseInArmature フラグを設定するコマンド。")]
    public class SetIgnorePoseCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "IgnorePoseValue",
                 Description = "アーマチュア内で姿勢を無視する", Required = true)]
        public bool  Value         { get; }
        public SetIgnorePoseCommand(int modelIndex, int[] masterIndices, bool value)
            : base(modelIndex) { MasterIndices = masterIndices; Value = value; }
    }

    /// <summary>
    /// オブジェクト原点（BoneTransform.Position）を名前指定で一括設定するコマンド。
    /// 「原点だけ移動 = true / 子を一緒に移動 = false」と同じ挙動で適用する。
    ///
    /// Rotations は任意。null なら回転を触らない。要素が null の行も同じく触らない
    /// （CSV に回転列が無い行を「指定なし」として扱うため）。
    /// </summary>
    [PLCommand(Description = "オブジェクト原点（BoneTransform.Position）を名前指定で一括設定するコマンド。")]
    public class ApplyObjectOriginsCommand : PanelCommand
    {
        [PLParam(TextKey = "ObjectOriginNames",
                 Description = "原点を設定する描画オブジェクトの名前", Required = true)]
        public string[]  Names     { get; }

        [PLParam(TextKey = "ObjectOriginPositions",
                 Description = "Names と同じ並びの原点位置", Required = true)]
        public Vector3[] Positions { get; }

        /// <summary>
        /// 行ごとの回転（度）を平たく並べたもの。1 行ぶん 3 個。
        ///
        /// Vector3?[] のまま持つとスキーマに出せない（要素ごとの null を
        /// 平たいキーで表せない）ため、値の列と「その行に回転があるか」の
        /// 真偽値の列に分けてある。
        /// 空なら全行で回転を触らない。
        /// </summary>
        [PLParam(TextKey = "ObjectOriginRotationValues",
                 Description = "Names と同じ並びの回転（度）。1 行 3 個。空なら回転を触らない")]
        public float[] RotationValues { get; }

        /// <summary>
        /// 行ごとに回転を適用するか。Names と同じ並び・同じ長さ。
        /// 空なら「RotationValues がある行はすべて適用」とみなす。
        /// </summary>
        [PLParam(TextKey = "ObjectOriginHasRotation",
                 Description = "行ごとに回転を適用するか。Names と同じ並び。空なら値のある行はすべて適用")]
        public bool[] HasRotation { get; }

        /// <summary>
        /// 平たい列から起こした行ごとの回転。受け口はこちらを使う。
        /// 値が無いときは null を返す（従来の「回転を触らない」と同じ扱い）。
        /// </summary>
        public Vector3?[] Rotations
        {
            get
            {
                int n = (RotationValues?.Length ?? 0) / 3;
                if (n == 0) return null;

                var a = new Vector3?[n];
                for (int i = 0; i < n; i++)
                {
                    bool has = (HasRotation == null || HasRotation.Length == 0)
                        || (i < HasRotation.Length && HasRotation[i]);
                    a[i] = has
                        ? (Vector3?)new Vector3(
                            RotationValues[i * 3], RotationValues[i * 3 + 1], RotationValues[i * 3 + 2])
                        : null;
                }
                return a;
            }
        }

        /// <summary>
        /// Vector3?[] を「値の列」と「適用するかの列」へ分ける。
        /// 呼び出し側の書き換えを短くするための補助。
        /// </summary>
        public static void SplitRotations(
            Vector3?[] rotations, out float[] values, out bool[] hasRotation)
        {
            int n = rotations?.Length ?? 0;
            values      = new float[n * 3];
            hasRotation = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var r = rotations[i] ?? Vector3.zero;
                values[i * 3]     = r.x;
                values[i * 3 + 1] = r.y;
                values[i * 3 + 2] = r.z;
                hasRotation[i]    = rotations[i].HasValue;
            }
        }

        public ApplyObjectOriginsCommand(
            int modelIndex, string[] names, Vector3[] positions,
            float[] rotationValues = null, bool[] hasRotation = null)
            : base(modelIndex)
        {
            Names          = names;
            Positions      = positions;
            RotationValues = rotationValues ?? System.Array.Empty<float>();
            HasRotation    = hasRotation    ?? System.Array.Empty<bool>();
        }
    }

    /// <summary>
    /// メッシュオブジェクトの姿勢を、表示用のくさびオブジェクト列としてモデル内に生成する。
    /// くさびは新規の空オブジェクト（コンテナ）の配下に、メッシュの階層を保って並ぶ。
    /// </summary>
    [PLCommand(Description = "メッシュオブジェクトの姿勢を、表示用のくさびオブジェクト列としてモデル内に生成する。")]
    public class GenerateObjectPoseWedgesCommand : PanelCommand
    {
        /// <summary>くさびの全長（オブジェクトの拡大率平均を掛ける前の基準値）。</summary>
        [PLParam(TextKey = "WedgeLength",
                 Description = "くさびの全長。拡大率平均を掛ける前の基準値", Required = true)]
        public float WedgeLength { get; }

        /// <summary>コンテナの名前。空なら既定名。</summary>
        [PLParam(TextKey = "WedgeContainerNewName",
                 Description = "生成するコンテナの名前。空で既定名", Required = true)]
        public string ContainerName { get; }

        public GenerateObjectPoseWedgesCommand(int modelIndex, float wedgeLength, string containerName)
            : base(modelIndex) { WedgeLength = wedgeLength; ContainerName = containerName; }
    }

    /// <summary>
    /// くさびオブジェクト列を読み、名前一致でメッシュオブジェクトの姿勢へ適用する。
    /// 適用は「原点だけ移動」と同じく、自頂点を再局所化して見た目を保つ。
    /// </summary>
    [PLCommand(Description = "くさびオブジェクト列を読み、名前一致でメッシュオブジェクトの姿勢へ適用する。")]
    public class ApplyObjectPoseWedgesCommand : PanelCommand
    {
        /// <summary>コンテナの MeshContextList 索引。-1 なら名前で自動検出。</summary>
        [PLParam(TextKey = "WedgeContainerMasterIndex",
                 Description = "くさびコンテナの masterIndex。-1 で名前から自動検出", Required = true)]
        public int ContainerMasterIndex { get; }

        /// <summary>自動検出に使うコンテナ名。空なら既定名。</summary>
        [PLParam(TextKey = "WedgeContainerName",
                 Description = "自動検出に使うコンテナ名。空で既定名", Required = true)]
        public string ContainerName { get; }

        public ApplyObjectPoseWedgesCommand(int modelIndex, int containerMasterIndex, string containerName)
            : base(modelIndex) { ContainerMasterIndex = containerMasterIndex; ContainerName = containerName; }
    }

    /// <summary>
    /// PreserveNormals フラグ（頂点法線を自動再計算しない）を設定するコマンド。
    /// </summary>
    [PLCommand(Description = "PreserveNormals フラグ（頂点法線を自動再計算しない）を設定するコマンド。")]
    public class SetPreserveNormalsCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "PreserveNormalsValue",
                 Description = "頂点法線を自動再計算しない", Required = true)]
        public bool  Value         { get; }
        public SetPreserveNormalsCommand(int modelIndex, int[] masterIndices, bool value)
            : base(modelIndex) { MasterIndices = masterIndices; Value = value; }
    }

    /// <summary>ミラー分岐ルートのフラグを設定するコマンド。</summary>
    [PLCommand(Description = "ミラー分岐ルートのフラグを設定するコマンド。")]
    public class SetMirrorBranchRootCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "MirrorBranchRootValue",
                 Description = "ミラー分岐のルートとして扱う", Required = true)]
        public bool  Value         { get; }
        public SetMirrorBranchRootCommand(int modelIndex, int[] masterIndices, bool value)
            : base(modelIndex) { MasterIndices = masterIndices; Value = value; }
    }

    [PLCommand(Description = "オブジェクト 1 つのミラー種別を次の値へ送る。")]
    public class CycleMirrorTypeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }
        public CycleMirrorTypeCommand(int modelIndex, int masterIndex)
            : base(modelIndex) { MasterIndex = masterIndex; }
    }

    [PLCommand(Description = "オブジェクト 1 つの名前を変える。")]
    public class RenameMeshCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "MeshNewName",
                 Description = "描画オブジェクトの新しい名前", Required = true)]
        public string NewName { get; }
        public RenameMeshCommand(int modelIndex, int masterIndex, string newName)
            : base(modelIndex) { MasterIndex = masterIndex; NewName = newName; }
    }

    /// <summary>
    /// 複数オブジェクトの名前を一括変更する（名称一括変更 CSV 用）。
    ///
    /// MasterIndices と NewNames は同じ並び・同じ長さ。
    /// 名前の重複は受け側（PlayerCommandDispatcher）が
    /// MeshRenameCsvHelper.ResolveUniqueNames で自動回避するため、
    /// 送信側は CSV に書かれた希望名をそのまま渡してよい。
    /// </summary>
    [PLCommand(Description = "複数オブジェクトの名前を一括変更する（名称一括変更 CSV 用）。")]
    public class RenameMeshesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[]    MasterIndices { get; }

        [PLParam(TextKey = "MeshNewNames",
                 Description = "MasterIndices と同じ並び・同じ長さの新しい名前", Required = true)]
        public string[] NewNames      { get; }
        public RenameMeshesCommand(int modelIndex, int[] masterIndices, string[] newNames)
            : base(modelIndex) { MasterIndices = masterIndices; NewNames = newNames; }
    }

    /// <summary>
    /// メッシュの TreeView 折りたたみ状態変更
    /// </summary>
    [PLCommand(Description = "メッシュの TreeView 折りたたみ状態変更")]
    public class SetMeshFoldingCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "MeshFolding",
                 Description = "ツリーの子を折りたたむ", Required = true)]
        public bool IsFolding { get; }
        public SetMeshFoldingCommand(int modelIndex, int masterIndex, bool isFolding)
            : base(modelIndex) { MasterIndex = masterIndex; IsFolding = isFolding; }
    }

    // ================================================================
    // 面の表示・非表示
    // ================================================================

    /// <summary>
    /// 面の非表示フラグ（Face.IsHidden）を操作する。
    /// 対象は選択中の描画メッシュ（未選択なら編集対象メッシュ単体）。
    ///
    /// 非表示は編集補助であり、面データは残る（エクスポートにも出る）。
    /// メッシュ丸ごとの非表示は ToggleVisibilityCommand / SetBatchVisibilityCommand を使うこと。
    /// </summary>
    [PLCommand(Description = "面の非表示フラグ（Face.IsHidden）を操作する。")]
    public class SetFaceHiddenCommand : PanelCommand
    {
        public enum Mode
        {
            /// <summary>選択面を隠す（面選択が無い場合は何もしない）</summary>
            HideSelected,
            /// <summary>選択面以外を隠す（面選択が無い場合は何もしない）</summary>
            HideUnselected,
            /// <summary>すべての面を表示に戻す</summary>
            ShowAll,
            /// <summary>表示・非表示を反転する</summary>
            InvertHidden,
        }

        [PLParam(TextKey = "FaceHiddenOperation",
                 Description = "隠す / 選択以外を隠す / 全表示 / 反転", Required = true)]
        public Mode Operation { get; }

        public SetFaceHiddenCommand(int modelIndex, Mode operation)
            : base(modelIndex) { Operation = operation; }
    }
}
