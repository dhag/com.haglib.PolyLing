// PlayerCommandDispatcher.HumanoidFit.cs
// コマンドディスパッチャ：ボーンの無いモデルへのヒューマノイドボーンの当てはめ。
//   captureViewportImage / queryViewportImageCapture … メイン 3D 画面の描画結果とカメラ
//   fitHumanoidBones                                 … 姿勢の画素位置とカメラからボーンを作って割り当てる
//   fitHumanoidHandBones                             … 手 21 点の画素位置とカメラから指ボーンを作って割り当てる（HandBoneFitter）
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// ■ 撮影
//   フレーム終端（PlayerCaptureRunner）でメイン 3D 画面を ViewportShot で撮り、PNG を書いてカメラを控える。
//   結果は受付番号ごとに持ち、queryViewportImageCapture で返す（受け取っても消さない。数が増えたら古いものから捨てる）。
// ■ 当てはめ
//   頂点のワールド位置は GPU の表示位置（PlayerViewportManager.TryGetMeshWorldPositions）を読む。
//   ボーンの作成は PositionCsvNodeBuilder.Build、割当は ApplyHumanoidMappingCommand と同じく model.HumanoidMapping へ。
//   Undo は MeshList のスナップショットと割当の変更を 1 件にまとめる。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;
using Poly_Ling.Tools.MediaPipe;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        // ================================================================
        // 撮影
        // ================================================================

        private sealed class ViewportCapture
        {
            public string            Status = "pending";
            public string            Path   = "";
            public string            Error  = "";
            public FaceCaptureCamera Camera;
        }

        private const int MaxViewportCaptures = 16;
        private readonly Dictionary<string, ViewportCapture> _viewportCaptures = new Dictionary<string, ViewportCapture>();
        private readonly List<string> _viewportCaptureOrder = new List<string>();
        private int _viewportCaptureSerial;

        private void CaptureViewportImage(CaptureViewportImageCommand c)
        {
            if (!PLSandbox.TryResolveWrite(c.FilePath, out string fullPath, out string reason)) { Fail(reason); return; }
            if (!string.Equals(System.IO.Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
            { Fail("filePath の拡張子は .png にしてください"); return; }
            var vp = _viewportManager?.PerspectiveViewport;
            if (vp == null || !vp.IsReady) { Fail("メイン 3D 画面がありません"); return; }

            string id = "vc" + (++_viewportCaptureSerial);
            var cap = new ViewportCapture();
            _viewportCaptures[id] = cap;
            _viewportCaptureOrder.Add(id);
            while (_viewportCaptureOrder.Count > MaxViewportCaptures)
            {
                _viewportCaptures.Remove(_viewportCaptureOrder[0]);
                _viewportCaptureOrder.RemoveAt(0);
            }

            PlayerCaptureRunner.Instance.RunAtEndOfFrame(() =>
            {
                if (!ViewportShot.TryShoot(vp, out var png, out var camera, out string error))
                {
                    cap.Status = "failed"; cap.Error = error;
                    return;
                }
                try
                {
                    string dir = System.IO.Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllBytes(fullPath, png);
                    cap.Camera = camera;
                    cap.Path   = fullPath;
                    cap.Status = "completed";
                }
                catch (Exception e)
                {
                    cap.Status = "failed"; cap.Error = "PNG を書けませんでした: " + e.Message;
                }
            });

            ReportData(CommandDataJson.New().Text("captureId", id).Text("status", cap.Status).Build());
        }

        private void QueryViewportImageCapture(QueryViewportImageCaptureCommand c)
        {
            if (!_viewportCaptures.TryGetValue(c.CaptureId ?? "", out var cap))
            { Fail($"captureId がありません: {c.CaptureId}"); return; }

            var b = CommandDataJson.New().Text("captureId", c.CaptureId).Text("status", cap.Status);
            if (cap.Status == "completed")
            {
                b.Text("path", cap.Path)
                 .Int("width", cap.Camera.Width)
                 .Int("height", cap.Camera.Height)
                 .Nums("view", RowMajor(cap.Camera.View))
                 .Nums("projection", RowMajor(cap.Camera.Projection));
            }
            else if (cap.Status == "failed")
            {
                b.Text("error", cap.Error);
            }
            ReportData(b.Build());
        }

        private static float[] RowMajor(Matrix4x4 m)
        {
            var a = new float[16];
            for (int r = 0; r < 4; r++)
                for (int col = 0; col < 4; col++)
                    a[r * 4 + col] = m[r, col];
            return a;
        }

        private static bool TryMatrix(float[] a, string name, out Matrix4x4 m, out string error)
        {
            m = Matrix4x4.identity;
            error = "";
            if (a == null || a.Length != 16) { error = $"{name} は 16 個にしてください（{a?.Length ?? 0} 個）"; return false; }
            for (int r = 0; r < 4; r++)
                for (int col = 0; col < 4; col++)
                    m[r, col] = a[r * 4 + col];
            return true;
        }

        // ================================================================
        // 当てはめ
        // ================================================================

        private void FitHumanoidBones(ProjectContext project, ModelContext model, FitHumanoidBonesCommand c)
        {
            if (c.PosePixels == null || c.PosePixels.Length != MediaPipeFrame.PosePoints * 2)
            { Fail($"posePixels は {MediaPipeFrame.PosePoints * 2} 個にしてください（{c.PosePixels?.Length ?? 0} 個）"); return; }
            if (c.ImageWidth <= 0 || c.ImageHeight <= 0) { Fail("imageWidth・imageHeight は正の数にしてください"); return; }
            if (!TryMatrix(c.View, "view", out var view, out string mErr)) { Fail(mErr); return; }
            if (!TryMatrix(c.Projection, "projection", out var proj, out mErr)) { Fail(mErr); return; }

            var camera = new FaceCaptureCamera { View = view, Projection = proj, Width = c.ImageWidth, Height = c.ImageHeight };
            var posePixels = new Vector2[MediaPipeFrame.PosePoints];
            for (int i = 0; i < posePixels.Length; i++) posePixels[i] = new Vector2(c.PosePixels[2 * i], c.PosePixels[2 * i + 1]);

            if (!TryCollectFitWorlds(model, c.TargetMasterIndices, out var worlds, out string wErr)) { Fail(wErr); return; }

            var fit = HumanoidBoneFitter.Fit(camera, posePixels, worlds, c.RadiusRatio, out string fitError);
            if (fit == null) { Fail(fitError); return; }

            var table = new PositionCsvTable();
            foreach (var j in fit.Joints)
            {
                table.Names.Add(j.Bone);
                table.Parents.Add(j.Parent);
                table.Positions.Add(j.World);
            }

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);
            var hmBefore   = model.HumanoidMapping.Clone();

            var r = PositionCsvNodeBuilder.Build(model, table, null, true);
            if (r.Created.Count != table.Count)
            {
                Fail(string.IsNullOrEmpty(r.Message) ? "ボーンを作れませんでした" : r.Message);
                return;
            }

            // 割当：今の割当を残し、作ったボーンだけ差し替える
            for (int k = 0; k < r.Created.Count; k++)
                model.HumanoidMapping.Set(r.CreatedRowNames[k], r.Created[k]);
            var hmAfter = model.HumanoidMapping.Clone();

            model.OnListChanged?.Invoke();
            if (_undoController != null)
            {
                const string desc = "ヒューマノイドボーンの当てはめ";
                _undoController.MeshListStack.BeginGroup(desc);
                RecordMeshListSnapshot(listBefore, model, desc);
                var hmRecord = new HumanoidMappingChangedRecord(hmBefore, hmAfter, desc);
                PLDiag.UndoRecord("MeshList", desc, hmRecord);
                _undoController.MeshListStack.Record(hmRecord, desc);
                _undoController.MeshListStack.EndGroup();
                _undoController.FocusMeshList();
            }

            model.IsDirty = true;
            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.ListStructure);

            var indices = new List<int>(r.Created.Count);
            var ids     = new List<ulong>(r.Created.Count);
            foreach (int i in r.Created)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                indices.Add(model.MeshContextList.IndexOf(mc));
                ids.Add(mc.ObjectId);
            }

            string message = $"ヒューマノイドボーン {r.Created.Count} 本を作りました（改名 {r.Renamed}"
                           + (fit.ArmsSwapped ? "・腕の左右を入れ替え" : "")
                           + (fit.LegsSwapped ? "・脚の左右を入れ替え" : "")
                           + $"・半径 {fit.RadiusPixels:F1} 画素）";
            Debug.Log("[FitHumanoidBones] " + message);

            ReportData(CommandDataJson.New()
                    .Int("created", r.Created.Count)
                    .Texts("bones", r.CreatedRowNames)
                    .Int("renamed", r.Renamed)
                    .Flag("armsSwapped", fit.ArmsSwapped)
                    .Flag("legsSwapped", fit.LegsSwapped)
                    .Num("radiusPixels", fit.RadiusPixels)
                    .Text("message", message)
                    .Build(),
                indices.ToArray(), ids.ToArray());
        }

        /// <summary>
        /// 当てはめの奥行きに使う頂点のワールド位置（GPU の表示位置）を集める。
        /// 対象は描画される種類（Mesh・MirrorSide・BakedMirror。MeshCategory.Drawable）。
        /// ミラー側は別の描画オブジェクト（MirrorSide）なので、これを外すと体の片側に頂点が無くなる。
        /// 指定が無ければ、頂点を持つ表示中のものすべて。
        /// </summary>
        private bool TryCollectFitWorlds(ModelContext model, int[] targetMasterIndices,
                                         out List<Vector3[]> worlds, out string error)
        {
            worlds = null;
            error  = "";
            var drawable = new HashSet<int>(model.DrawableMeshes.Select(e => e.MasterIndex));
            var targets = new List<MeshContext>();
            if (targetMasterIndices != null && targetMasterIndices.Length > 0)
            {
                foreach (int mi in targetMasterIndices.Distinct())
                {
                    var mc = model.GetMeshContext(mi);
                    if (mc?.MeshObject == null || !drawable.Contains(mi) || mc.MeshObject.VertexCount == 0)
                    { error = $"頂点を持つ描画オブジェクトではありません（{mi}）"; return false; }
                    targets.Add(mc);
                }
            }
            else
            {
                foreach (int mi in drawable)
                {
                    var mc = model.GetMeshContext(mi);
                    if (mc?.MeshObject == null || !mc.IsVisible || mc.MeshObject.VertexCount == 0) continue;
                    targets.Add(mc);
                }
            }
            if (targets.Count == 0) { error = "頂点を持つ描画オブジェクトがありません"; return false; }

            worlds = new List<Vector3[]>(targets.Count);
            foreach (var mc in targets)
            {
                if (!_viewportManager.TryGetMeshWorldPositions(model, mc, out var w))
                { error = $"頂点のワールド位置を GPU から読めません: {mc.Name}"; worlds = null; return false; }
                worlds.Add(w);
            }
            return true;
        }

        // ================================================================
        // 指の当てはめ
        // ================================================================

        private void FitHumanoidHandBones(ProjectContext project, ModelContext model, FitHumanoidHandBonesCommand c)
        {
            if (c.HandPixels == null || c.HandPixels.Length != HandBoneFitter.Points * 2)
            { Fail($"handPixels は {HandBoneFitter.Points * 2} 個にしてください（{c.HandPixels?.Length ?? 0} 個）"); return; }
            if (c.ImageWidth <= 0 || c.ImageHeight <= 0) { Fail("imageWidth・imageHeight は正の数にしてください"); return; }
            if (!TryMatrix(c.View, "view", out var view, out string mErr)) { Fail(mErr); return; }
            if (!TryMatrix(c.Projection, "projection", out var proj, out mErr)) { Fail(mErr); return; }

            var camera = new FaceCaptureCamera { View = view, Projection = proj, Width = c.ImageWidth, Height = c.ImageHeight };
            var handPixels = new Vector2[HandBoneFitter.Points];
            for (int i = 0; i < handPixels.Length; i++) handPixels[i] = new Vector2(c.HandPixels[2 * i], c.HandPixels[2 * i + 1]);

            if (!TryCollectFitWorlds(model, c.TargetMasterIndices, out var worlds, out string wErr)) { Fail(wErr); return; }

            var fit = HandBoneFitter.Fit(camera, handPixels, worlds, c.RadiusRatio, c.Side, c.IncludeTips, out string fitError);
            if (fit == null) { Fail(fitError); return; }

            // 既存の Hand（割当）があれば、その下につなぐ。PositionCsvNodeBuilder は親を名前で引くので、
            // その名前がモデルで最初に当たるオブジェクトが割当の Hand であることを確かめる。
            string handBone = fit.Side + "Hand";
            int handIndex = model.HumanoidMapping.Get(handBone);
            string handParentName = "";
            if (handIndex >= 0)
            {
                var hc = model.GetMeshContext(handIndex);
                if (hc == null || hc.Type != MeshType.Bone) { Fail($"割当の {handBone} がボーンではありません（{handIndex}）"); return; }
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null || mc.Name != hc.Name) continue;
                    if (i != handIndex) { Fail($"{handBone} のボーン名「{hc.Name}」が他のオブジェクトと重なっているため、親を名前で決められません"); return; }
                    break;
                }
                handParentName = hc.Name;
            }

            var table = new PositionCsvTable();
            foreach (var j in fit.Joints)
            {
                if (j.Bone == handBone && handIndex >= 0) continue;          // 既存の Hand を使う
                table.Names.Add(j.Bone);
                table.Parents.Add(j.Parent == handBone && handIndex >= 0 ? handParentName : j.Parent);
                table.Positions.Add(j.World);
            }

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);
            var hmBefore   = model.HumanoidMapping.Clone();

            var r = PositionCsvNodeBuilder.Build(model, table, null, true);
            if (r.Created.Count != table.Count)
            {
                Fail(string.IsNullOrEmpty(r.Message) ? "ボーンを作れませんでした" : r.Message);
                return;
            }

            // 割当：今の割当を残し、作ったボーンだけ差し替える（指先は割当なし）
            for (int k = 0; k < r.Created.Count; k++)
            {
                string name = r.CreatedRowNames[k];
                if (HandBoneFitter.IsTip(name)) continue;
                model.HumanoidMapping.Set(name, r.Created[k]);
            }
            var hmAfter = model.HumanoidMapping.Clone();

            model.OnListChanged?.Invoke();
            if (_undoController != null)
            {
                const string desc = "指ボーンの当てはめ";
                _undoController.MeshListStack.BeginGroup(desc);
                RecordMeshListSnapshot(listBefore, model, desc);
                var hmRecord = new HumanoidMappingChangedRecord(hmBefore, hmAfter, desc);
                PLDiag.UndoRecord("MeshList", desc, hmRecord);
                _undoController.MeshListStack.Record(hmRecord, desc);
                _undoController.MeshListStack.EndGroup();
                _undoController.FocusMeshList();
            }

            model.IsDirty = true;
            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.ListStructure);

            var indices = new List<int>(r.Created.Count);
            var ids     = new List<ulong>(r.Created.Count);
            foreach (int i in r.Created)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                indices.Add(model.MeshContextList.IndexOf(mc));
                ids.Add(mc.ObjectId);
            }

            bool attached = handIndex >= 0;
            string message = $"{(fit.Side == "Left" ? "左" : "右")}手の指ボーン {r.Created.Count} 本を作りました（"
                           + (attached ? $"既存の {handParentName} の下" : $"{handBone} も新規")
                           + (c.Side == "Auto" ? "・左右は手の向きから決定" : "")
                           + $"・改名 {r.Renamed}・半径 {fit.RadiusPixels:F1} 画素）";
            Debug.Log("[FitHumanoidHandBones] " + message);

            ReportData(CommandDataJson.New()
                    .Int("created", r.Created.Count)
                    .Texts("bones", r.CreatedRowNames)
                    .Text("side", fit.Side)
                    .Flag("attached", attached)
                    .Num("radiusPixels", fit.RadiusPixels)
                    .Text("message", message)
                    .Build(),
                indices.ToArray(), ids.ToArray());
        }
    }
}
