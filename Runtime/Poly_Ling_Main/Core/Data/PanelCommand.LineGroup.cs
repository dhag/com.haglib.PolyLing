// PanelCommand.LineGroup.cs
// 線分群（MeshObject.LineGroups）を作る／差し替える／消す／名前を変える／ハンドル拘束を変えるコマンド。
// 実処理は LineGroupEditOps（頂点・2 頂点の面・線分群を一緒に更新する）。受け口は
// PlayerCommandDispatcher.LineGroup.cs。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【座標】点とハンドルのずれは対象オブジェクトのローカル座標。
// 【ハンドル】HandleOffsets は点ごとに 6 個（入り xyz・出 xyz のずれ）。省くと折れ線。

using UnityEngine;

namespace Poly_Ling.Data
{
    [PLCommand(Writes = PLWriteScope.Targets, Description = "線分群を作る。点列から頂点・線分（2 頂点の面）・線分群を一緒に作る。")]
    [PLResult("groupIndex", PLResultKind.Integer, Description = "作った線分群の番号")]
    public class CreateLineGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write, Required = true,
                 Description = "対象の描画オブジェクトの masterIndex")]
        public int MasterIndex { get; }

        [PLParam(Description = "点のローカル座標。x,y,z の順に 3 個ずつ", Required = true)]
        public float[] Points { get; }

        [PLParam(Description = "閉じるか（3 点以上のとき終点→始点も結ぶ）")]
        public bool Closed { get; }

        [PLParam(Description = "線分群の名前。空なら自動で付ける")]
        public string GroupName { get; }

        [PLParam(Description = "点ごとに 6 個（入り xyz・出 xyz のずれ）。省くと折れ線")]
        public float[] HandleOffsets { get; }

        [PLParam(Description = "始点に使う既存の頂点番号（-1 = 新しく作る）。既存の線分群の頂点なら、その頂点を親にする")]
        public int StartVertexIndex { get; }

        [PLParam(Description = "点ごとに 6 個（入りの向き・長さ・組 ID、出の向き・長さ・組 ID）。省くと拘束は自由。handleOffsets と一緒に使う")]
        public int[] HandleConstraints { get; }

        [PLParam(Description = "点ごとに 2 個（入りの比率・出の比率）。handleConstraints と一緒に使う")]
        public float[] HandleRatios { get; }

        public CreateLineGroupCommand(int modelIndex, int masterIndex, float[] points,
            bool closed = false, string groupName = "", float[] handleOffsets = null, int startVertexIndex = -1,
            int[] handleConstraints = null, float[] handleRatios = null)
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
            Points        = points ?? System.Array.Empty<float>();
            Closed        = closed;
            GroupName     = groupName ?? "";
            HandleOffsets = handleOffsets ?? System.Array.Empty<float>();
            StartVertexIndex = startVertexIndex;
            HandleConstraints = handleConstraints ?? System.Array.Empty<int>();
            HandleRatios      = handleRatios ?? System.Array.Empty<float>();
        }
    }

    [PLCommand(Writes = PLWriteScope.Targets, Description = "線分群の点列（と任意でハンドル）を差し替える。点の数が変われば頂点と線分も増減する。")]
    public class SetLineGroupPointsCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write, Required = true,
                 Description = "対象の描画オブジェクトの masterIndex")]
        public int MasterIndex { get; }

        [PLParam(Description = "線分群の番号", Required = true)]
        public int GroupIndex { get; }

        [PLParam(Description = "点のローカル座標。x,y,z の順に 3 個ずつ", Required = true)]
        public float[] Points { get; }

        [PLParam(Description = "閉じるか")]
        public bool Closed { get; }

        [PLParam(Description = "点ごとに 6 個（入り xyz・出 xyz のずれ）。省くと今のハンドル（拘束も）を残す")]
        public float[] HandleOffsets { get; }

        public SetLineGroupPointsCommand(int modelIndex, int masterIndex, int groupIndex, float[] points,
            bool closed = false, float[] handleOffsets = null)
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
            GroupIndex    = groupIndex;
            Points        = points ?? System.Array.Empty<float>();
            Closed        = closed;
            HandleOffsets = handleOffsets ?? System.Array.Empty<float>();
        }
    }

    [PLCommand(Writes = PLWriteScope.Targets, Description = "線分群とその線分を消す。頂点も消すかを選べる（他から使われている頂点は残す）。")]
    public class DeleteLineGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write, Required = true,
                 Description = "対象の描画オブジェクトの masterIndex")]
        public int MasterIndex { get; }

        [PLParam(Description = "線分群の番号", Required = true)]
        public int GroupIndex { get; }

        [PLParam(Description = "他から使われていない頂点も消す")]
        public bool DeleteVertices { get; }

        public DeleteLineGroupCommand(int modelIndex, int masterIndex, int groupIndex, bool deleteVertices = true)
            : base(modelIndex)
        {
            MasterIndex    = masterIndex;
            GroupIndex     = groupIndex;
            DeleteVertices = deleteVertices;
        }
    }

    [PLCommand(Writes = PLWriteScope.Targets, Description = "線分群の名前を変える。")]
    public class RenameLineGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write, Required = true,
                 Description = "対象の描画オブジェクトの masterIndex")]
        public int MasterIndex { get; }

        [PLParam(Description = "線分群の番号", Required = true)]
        public int GroupIndex { get; }

        [PLParam(Description = "新しい名前", Required = true)]
        public string NewName { get; }

        public RenameLineGroupCommand(int modelIndex, int masterIndex, int groupIndex, string newName)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            GroupIndex  = groupIndex;
            NewName     = newName ?? "";
        }
    }

    [PLCommand(Writes = PLWriteScope.Targets, Description = "線分群の点のハンドル拘束（向き・長さ）を変えて解き直す。ハンドルの無い群には既定のハンドルを作る。")]
    public class SetLineHandleConstraintCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write, Required = true,
                 Description = "対象の描画オブジェクトの masterIndex")]
        public int MasterIndex { get; }

        [PLParam(Description = "線分群の番号", Required = true)]
        public int GroupIndex { get; }

        [PLParam(Description = "群の中の点の番号（Order の添字）", Required = true)]
        public int PointIndex { get; }

        [PLParam(Description = "true = 出ハンドル（後の点の側）、false = 入りハンドル（前の点の側）")]
        public bool IsOut { get; }

        [PLParam(Description = "向き。Free / Chord（隣の点を向く）/ Tangent（反対側と一直線）")]
        public HandleDirection Direction { get; }

        [PLParam(Description = "長さ。Free / ChordRatio（弦 × Ratio）/ EqualOpposite（反対側と等長）/ Group（長さの組）")]
        public HandleLength Length { get; }

        [PLParam(Description = "Length が ChordRatio のときの比率")]
        public float Ratio { get; }

        [PLParam(Description = "Length が Group のときの長さの組の ID")]
        public int LengthGroupId { get; }

        public SetLineHandleConstraintCommand(int modelIndex, int masterIndex, int groupIndex, int pointIndex,
            bool isOut = true, HandleDirection direction = HandleDirection.Free,
            HandleLength length = HandleLength.Free, float ratio = 1f / 3f, int lengthGroupId = 0)
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
            GroupIndex    = groupIndex;
            PointIndex    = pointIndex;
            IsOut         = isOut;
            Direction     = direction;
            Length        = length;
            Ratio         = ratio;
            LengthGroupId = lengthGroupId;
        }
    }

    [PLCommand(Writes = PLWriteScope.Targets, Description = "線分群の曲線（ハンドル）を折れ線に焼き込む。曲線を分割した点で点列を差し替え、ハンドルと長さの組を捨てる。")]
    public class BakeLineGroupCurveCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write, Required = true,
                 Description = "対象の描画オブジェクトの masterIndex")]
        public int MasterIndex { get; }

        [PLParam(Description = "線分群の番号", Required = true)]
        public int GroupIndex { get; }

        [PLParam(Description = "1 区間あたりの分割数（1 以上）")]
        public int SegmentsPerSpan { get; }

        public BakeLineGroupCurveCommand(int modelIndex, int masterIndex, int groupIndex, int segmentsPerSpan = 8)
            : base(modelIndex)
        {
            MasterIndex     = masterIndex;
            GroupIndex      = groupIndex;
            SegmentsPerSpan = segmentsPerSpan;
        }
    }

    [PLCommand(Writes = PLWriteScope.Targets, Description = "線分群の長さの組を作るか値を変えて、解き直す。")]
    public class SetLineLengthGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write, Required = true,
                 Description = "対象の描画オブジェクトの masterIndex")]
        public int MasterIndex { get; }

        [PLParam(Description = "線分群の番号", Required = true)]
        public int GroupIndex { get; }

        [PLParam(Description = "長さの組の ID", Required = true)]
        public int LengthGroupId { get; }

        [PLParam(Description = "共有する長さ（0 以上）", Required = true)]
        public float Value { get; }

        public SetLineLengthGroupCommand(int modelIndex, int masterIndex, int groupIndex, int lengthGroupId, float value)
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
            GroupIndex    = groupIndex;
            LengthGroupId = lengthGroupId;
            Value         = value;
        }
    }
}
