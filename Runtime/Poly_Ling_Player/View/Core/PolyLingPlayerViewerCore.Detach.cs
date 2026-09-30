// PolyLingPlayerViewerCore.Detach.cs
// Player ビューアのコア：頂点の分離（DetachVerticesCommand）の受け口とパネル。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 実処理は DetachVerticesOps（Poly_Ling_Main/Core/Ops）。対象ごとに頂点を増やし、
// 全対象の前後を 1 件の Undo（MultiMeshTopologySnapshotRecord）にする。
// 表示用メッシュは位相が変わるので作り直す（MultiMeshTopologySnapshot.RestoreTo と同じ作り方）。

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        private PlayerDetachVerticesSubPanel _detachVerticesSubPanel;

        /// <summary>パネルを作り、左ペインのボタン・コマンドの受け口を結線する（BuildLayout の段から呼ぶ）。</summary>
        private void BuildDetachVertices()
        {
            _detachVerticesSubPanel = new PlayerDetachVerticesSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = PanelModelIndex,
                SendCommand   = DispatchFromPanel,
            };
            _detachVerticesSubPanel.Build(_layoutRoot.DetachVerticesSection);
            _layoutRoot.DetachVerticesBtn.clicked += ShowDetachVerticesPanel;
            _sectionRefreshPairs.Add((_layoutRoot.DetachVerticesSection, () => _detachVerticesSubPanel?.Refresh()));
        }

        private void ShowDetachVerticesPanel()
        {
            ShowRightPanel(_layoutRoot?.DetachVerticesSection, _layoutRoot?.DetachVerticesBtn);
            _detachVerticesSubPanel?.Refresh();
        }

        /// <summary>頂点の分離の受け口。失敗理由を返す（成功なら null）。</summary>
        private string ExecuteDetachVertices(DetachVerticesCommand c, CommandDataBuilder data, EditSpaceTouched touched)
        {
            var project = ActiveProject;
            var model   = project?.CurrentModel;
            if (model == null) return "モデルがありません";
            if (c.MasterIndices == null || c.MasterIndices.Length == 0) return "対象がありません";

            var targets = new List<MeshContext>();
            foreach (int idx in c.MasterIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) return $"masterIndex {idx} の描画オブジェクトがありません";
                if (mc.Type != MeshType.Mesh) return $"'{mc.Name}' はメッシュではありません";
                if (mc.Selection == null) return $"'{mc.Name}' に選択がありません";
                targets.Add(mc);
            }

            var before = CaptureEditSpaceMeshes(model, targets.ToArray());

            int total = 0;
            var changedIdx = new List<int>();
            var changedIds = new List<ulong>();
            foreach (var mc in targets)
            {
                int n = DetachVerticesOps.Execute(mc.MeshObject, mc.Selection, c.Mode);
                if (n <= 0) continue;
                total += n;
                changedIdx.Add(model.IndexOf(mc));
                changedIds.Add(mc.ObjectId);
                if (mc.Type != MeshType.Bone)
                    mc.ReplaceUnityMesh(mc.MeshObject.ToUnityMesh(model.MaterialCount));
            }
            if (total == 0)
            {
                return c.Mode == DetachMode.Faces
                    ? "分離できる頂点がありません。周りの面とつながった面を選択してください"
                    : "分離できる頂点がありません。面と面の間の辺を選択してください";
            }

            RecordEditSpaceChange(model, before, CaptureEditSpaceMeshes(model, targets.ToArray()), "頂点の分離");
            model.IsDirty = true;

            _viewportManager.EnterTopologyChanged(project);
            NotifyPanels(ChangeKind.Attributes);
            RefreshEditSpaceBar();
            if (_layoutRoot?.DetachVerticesSection?.style.display == DisplayStyle.Flex)
                _detachVerticesSubPanel?.Refresh();

            data.Int("newVertices", total).Int("objects", changedIdx.Count);
            touched.MasterIndices = changedIdx.ToArray();
            touched.ObjectIds     = changedIds.ToArray();
            return null;
        }
    }
}
