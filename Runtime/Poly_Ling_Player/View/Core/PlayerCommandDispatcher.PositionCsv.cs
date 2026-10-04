// PlayerCommandDispatcher.PositionCsv.cs
// コマンドディスパッチャ：位置付き CSV（名前・親・位置）からボーン / 空の描画オブジェクトを作る。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Ops;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// 位置付き CSV を読み、ボーンまたは空の描画オブジェクトを作る。
        /// 失敗は Fail で返す。表の検証は作る前に済ませるので、失敗時にモデルは変わらない。
        /// リスト構造が変わるので MeshList の Undo を 1 件残す。
        /// </summary>
        private void CreateObjectsFromPositionCsv(
            ProjectContext project, ModelContext model, CreateObjectsFromPositionCsvCommand c)
        {
            if (!PLSandbox.TryResolveRead(c.FilePath, out string fullPath, out string reason))
            {
                Fail(reason);
                return;
            }

            string text;
            try
            {
                text = System.IO.File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
            }
            catch (Exception e)
            {
                Fail($"CSV を読めませんでした: {e.Message}");
                return;
            }

            if (!PositionCsvNodeBuilder.TryParse(
                    text, c.NameColumn, c.ParentColumn, c.XColumn, c.YColumn, c.ZColumn,
                    out var table, out reason))
            {
                Fail(reason);
                return;
            }

            _undoController?.SetModelContext(model);
            var before = MeshFilterToSkinnedRecord.CaptureList(model);

            var r = PositionCsvNodeBuilder.Build(model, table, c.IncludeNames, c.AsBones);
            if (r.Created.Count == 0)
            {
                Fail(string.IsNullOrEmpty(r.Message) ? "何も作れませんでした" : r.Message);
                return;
            }

            model.OnListChanged?.Invoke();
            RecordMeshListSnapshot(before, model, "位置CSVから作成");

            model.IsDirty = true;
            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.ListStructure);

            // 索引は解決した実体から引き直す
            var indices = new List<int>(r.Created.Count);
            var ids     = new List<ulong>(r.Created.Count);
            foreach (int i in r.Created)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                indices.Add(model.MeshContextList.IndexOf(mc));
                ids.Add(mc.ObjectId);
            }

            Debug.Log("[CreateObjectsFromPositionCsv] " + r.Message);

            ReportData(CommandDataJson.New()
                    .Int("created", r.Created.Count)
                    .Int("roots", r.Roots)
                    .Int("attachedToExisting", r.AttachedToExisting)
                    .Int("renamed", r.Renamed)
                    .Text("message", r.Message)
                    .Build(),
                indices.ToArray(), ids.ToArray());
        }
    }
}
