// MoveToolHandler.Commands.cs
// 移動ツールハンドラ：コマンドからの実行・数値移動・連結頂点の展開・範囲選択の確定。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    public partial class MoveToolHandler
    {
        /// <summary>
        /// 要素選択コマンドを実行する。
        ///
        /// 【なぜ要るか】
        ///   クリック経路は GPU ホバーが返した 1 要素と修飾キーから選択を決めるので、
        ///   コマンド経由（自動検証・MCP）からは通せない。要素の集合だけを渡せる
        ///   入口をここに置く。
        ///
        /// 【クリック経路と同じ実装を通す】
        ///   OnLeftClick と同じ「スナップショット → 選択の書き換え → 頂点への展開
        ///   → Undo 記録」の 4 手順を通す。頂点展開の種別（左ペインのチェック）と
        ///   MultiMeshSelectionChangeRecord の作り方は共有される。
        ///
        /// 【受け取る集合】
        ///   要素の索引はメッシュ内ローカル番号なので、同じ並びの *MeshIndices と
        ///   対で受ける。長さが合わないものは弾いて理由を返す。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.SelectElementsCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }
            if (_selectionOps == null) { reason = "選択操作がありません"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            int VLen(int[] a) => a?.Length ?? 0;

            if (VLen(cmd.VertexIndices) != VLen(cmd.VertexMeshIndices))
            { reason = "VertexIndices と VertexMeshIndices の長さが違います"; return false; }
            if (VLen(cmd.FaceIndices) != VLen(cmd.FaceMeshIndices))
            { reason = "FaceIndices と FaceMeshIndices の長さが違います"; return false; }
            if (VLen(cmd.LineIndices) != VLen(cmd.LineMeshIndices))
            { reason = "LineIndices と LineMeshIndices の長さが違います"; return false; }
            if (VLen(cmd.EdgePairs) % 2 != 0)
            { reason = "EdgePairs の長さが偶数ではありません"; return false; }
            if (VLen(cmd.EdgePairs) / 2 != VLen(cmd.EdgeMeshIndices))
            { reason = "EdgeMeshIndices の長さが EdgePairs の半分ではありません"; return false; }

            // 触るメッシュを集める。存在しないものが混ざっていたら何も書かずに返す。
            var touched = new HashSet<int>();
            if (cmd.MasterIndices != null)     foreach (int i in cmd.MasterIndices)     touched.Add(i);
            if (cmd.VertexMeshIndices != null) foreach (int i in cmd.VertexMeshIndices) touched.Add(i);
            if (cmd.EdgeMeshIndices != null)   foreach (int i in cmd.EdgeMeshIndices)   touched.Add(i);
            if (cmd.FaceMeshIndices != null)   foreach (int i in cmd.FaceMeshIndices)   touched.Add(i);
            if (cmd.LineMeshIndices != null)   foreach (int i in cmd.LineMeshIndices)   touched.Add(i);

            if (touched.Count == 0) { reason = "対象が指定されていません"; return false; }

            foreach (int idx in touched)
            {
                var mc = model.GetMeshContext(idx);
                if (mc == null)
                { reason = $"masterIndex {idx} のオブジェクトがありません"; return false; }
                if (mc.Selection == null)
                { reason = $"masterIndex {idx} に選択状態がありません"; return false; }
            }

            var before = CaptureAllSelectionSnapshots(touched);

            _selectionOps.ApplyElementSet(
                cmd.MasterIndices, cmd.Op,
                cmd.VertexMeshIndices, cmd.VertexIndices,
                cmd.EdgeMeshIndices,   cmd.EdgePairs,
                cmd.FaceMeshIndices,   cmd.FaceIndices,
                cmd.LineMeshIndices,   cmd.LineIndices);

            ExpandLinkedVertices();

            OnRequestNormal?.Invoke();
            RecordSelectionChange(before, CaptureAllSelectionSnapshots(touched));

            OnRepaint?.Invoke();
            return true;
        }

        /// <summary>
        /// 選択頂点の移動コマンドを実行する。
        ///
        /// 【なぜ要るか】
        ///   マウス経路は移動量を画面のドラッグ差分から決めるので、コマンド経由
        ///   （自動検証・MCP）からは通せない。対象と移動量だけを渡せる入口を置く。
        ///   SculptToolHandler.ExecuteFromCommand と同じ形。
        ///
        /// 【マウス経路と同じ実装を通す】
        ///   ApplyNumericMove をそのまま呼ぶ。辺・面・線分の選択を頂点へ展開する
        ///   規則、マグネットの重み付け、Undo 記録、リモート配信
        ///   （OnVerticesCommitted）はすべてその中にあるので、ここで書き足すものは無い。
        ///
        /// 【マグネット】
        ///   コマンドの値を正典として実行し、終わったら UI の値へ戻す。1 呼び出しが
        ///   パネルの状態に依存しないようにするため。
        ///
        /// 【Local の基準】
        ///   Space == Local のとき、Delta は MasterIndices[0] のローカル量として
        ///   解釈し、そのメッシュの WorldMatrix でワールドへ変換する。ApplyDelta は
        ///   受け取ったワールド量をメッシュごとにローカル化する。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.MoveSelectedVerticesCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            var indices = cmd.MasterIndices;
            if (indices == null || indices.Length == 0)
            { reason = "対象が指定されていません"; return false; }

            var targets = new HashSet<int>();
            foreach (int idx in indices)
            {
                if (model.GetMeshContext(idx) == null)
                { reason = $"masterIndex {idx} のオブジェクトがありません"; return false; }
                targets.Add(idx);
            }

            Vector3 worldDelta;
            if (cmd.Space == Poly_Ling.Data.MoveSelectedVerticesCommand.CoordSpace.World)
            {
                worldDelta = cmd.Delta;
            }
            else
            {
                var baseMc = model.GetMeshContext(indices[0]);
                worldDelta = baseMc.WorldMatrix.MultiplyVector(cmd.Delta);
            }

            if (worldDelta == Vector3.zero) { reason = "移動量が 0 です"; return false; }

            // マグネットの UI 状態を退避する。
            bool         savedUse      = UseMagnet;
            float        savedRadius   = MagnetRadius;
            FalloffType  savedFalloff  = MagnetFalloff;
            DistanceMode savedDistance = MagnetDistanceMode;

            _targetOverride = targets;
            bool moved = false;
            try
            {
                UseMagnet          = cmd.UseMagnet;
                MagnetRadius       = cmd.MagnetRadius;
                MagnetFalloff      = cmd.MagnetFalloff;
                MagnetDistanceMode = cmd.MagnetDistanceMode;

                moved = ApplyNumericMove(worldDelta);
                if (!moved) reason = "対象メッシュに選択された要素がありません";
            }
            finally
            {
                _targetOverride    = null;
                UseMagnet          = savedUse;
                MagnetRadius       = savedRadius;
                MagnetFalloff      = savedFalloff;
                MagnetDistanceMode = savedDistance;
                OnExitTransformDragging?.Invoke();
            }

            if (!moved) return false;

            // 法線の再計算はマウス経路が持たない追加処理なので、明示指定のときだけ掛ける。
            if (cmd.RecalcNormals)
            {
                foreach (int idx in targets)
                {
                    var mc = model.GetMeshContext(idx);
                    var mo = mc?.MeshObject;
                    if (mo == null) continue;
                    mo.RecalculateSmoothNormals();
                    OnSyncMeshPositions?.Invoke(mc);
                }
            }

            OnRepaint?.Invoke();
            return true;
        }

        /// <summary>
        /// ワールド空間の移動量を数値指定で適用し、Undo 1 件として確定する。
        /// サブパネルの数値入力から呼ぶ。ドラッグ状態機 (_state) には入らない。
        ///
        /// 対象は選択メッシュの選択要素 (UpdateAffectedVertices と同じ規則)。
        /// ワールドデルタは ApplyDelta 内でメッシュごとに WorldMatrixInverse で
        /// ローカル化されるため、ここではワールド量をそのまま渡す。
        /// </summary>
        /// <returns>実際に移動したら true。対象が無い・移動量が 0 なら false。</returns>
        private bool ApplyNumericMove(Vector3 worldDelta)
        {
            if (worldDelta == Vector3.zero) return false;

            OnEnterTransformDragging?.Invoke();

            UpdateAffectedVertices();
            if (!HasAnyAffected())
            {
                OnExitTransformDragging?.Invoke();
                return false;
            }

            BeginMove();
            ApplyDelta(worldDelta);
            EndMove();

            OnExitTransformDragging?.Invoke();
            OnRepaint?.Invoke();
            return true;
        }

        /// <summary>
        /// 辺／面／線分の選択を、対応する頂点選択へ展開する。
        /// 選択メッシュごとに自分の MeshObject を参照する
        /// （面インデックスはメッシュ内ローカル番号のため）。
        ///
        /// 展開する種別は <see cref="GetExpandToVertexKinds"/> が返す集合で決まる
        /// （左ペインの「選んだ要素の頂点も選択する」チェックボックス）。
        /// 未結線のときは 3 種すべて展開する（従来の挙動）。
        ///
        /// 【OFF にしたときに既存の頂点選択を消さない理由】
        /// SelectionState.Vertices は「展開で入った頂点」と「利用者が直接選んだ頂点」を
        /// 区別しない。切替時に一括で消すと、意図して選んだ頂点まで失われる。
        /// よって切替以降のクリック／矩形／投げ縄選択から展開が止まるだけにする。
        /// </summary>
        private void ExpandLinkedVertices()
        {
            var model = _project?.CurrentModel;
            if (model == null) return;

            var kinds = GetExpandToVertexKinds?.Invoke()
                        ?? (MeshSelectMode.Edge | MeshSelectMode.Face | MeshSelectMode.Line);

            bool expandEdge = kinds.Has(MeshSelectMode.Edge);
            bool expandFace = kinds.Has(MeshSelectMode.Face);
            bool expandLine = kinds.Has(MeshSelectMode.Line);
            if (!expandEdge && !expandFace && !expandLine) return;

            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(ctxIdx);
                var meshObject = mc?.MeshObject;
                var sel = mc?.Selection;
                if (meshObject == null || sel == null) continue;

                if (expandEdge)
                {
                    foreach (var edge in sel.Edges)
                    {
                        sel.Vertices.Add(edge.V1);
                        sel.Vertices.Add(edge.V2);
                    }
                }
                if (expandFace)
                {
                    foreach (var faceIdx in sel.Faces)
                    {
                        if (faceIdx >= 0 && faceIdx < meshObject.FaceCount)
                            foreach (var vIdx in meshObject.Faces[faceIdx].VertexIndices)
                                sel.Vertices.Add(vIdx);
                    }
                }
                if (expandLine)
                {
                    foreach (var lineIdx in sel.Lines)
                    {
                        if (lineIdx >= 0 && lineIdx < meshObject.FaceCount)
                        {
                            var face = meshObject.Faces[lineIdx];
                            if (face.VertexCount == 2)
                            {
                                sel.Vertices.Add(face.VertexIndices[0]);
                                sel.Vertices.Add(face.VertexIndices[1]);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// ヒット要素が既に選択済みかを判定する。
        /// 判定先は「当たったメッシュ」の Selection。先頭メッシュの Selection を
        /// 見ると、別メッシュの同番号要素が選択済みかどうかで判定してしまう。
        /// </summary>
        private bool IsElemSelected(PlayerHoverElement elem)
        {
            var sel = ResolveMeshSelection(elem.MeshIndex);
            if (sel == null) return false;
            return elem.Kind switch
            {
                PlayerHoverKind.Vertex => sel.Vertices.Contains(elem.VertexIndex),
                PlayerHoverKind.Edge   => sel.Edges.Contains(
                    new VertexPair(elem.EdgeV1, elem.EdgeV2)),
                PlayerHoverKind.Face   => sel.Faces.Contains(elem.FaceIndex),
                PlayerHoverKind.Line   => sel.Lines.Contains(elem.FaceIndex),
                _                      => false,
            };
        }

        /// <summary>
        /// MeshContextList インデックスから SelectionState を解決する。
        /// 解決できない場合は先頭選択メッシュのものを返す。
        /// </summary>
        private Poly_Ling.Selection.SelectionState ResolveMeshSelection(int meshContextIndex)
        {
            var model = _project?.CurrentModel;
            if (model != null && meshContextIndex >= 0)
            {
                var mc = model.GetMeshContext(meshContextIndex);
                if (mc?.Selection != null) return mc.Selection;
            }
            return _selectionOps.SelectionState;
        }

        private float MoveScreenDistToWorldRadius(float screenDist, ToolContext ctx)
        {
            Vector3 target   = ctx.CameraTarget;
            Vector3 camRight = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;
            Vector2 sp1 = ctx.WorldToScreenPos(target,           ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(target + camRight, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            float pxPerUnit = Vector2.Distance(sp1, sp2);
            if (pxPerUnit < 0.001f) return screenDist * 0.01f;
            return screenDist / pxPerUnit;
        }

        /// <summary>
        /// ToViewportCoord（Y=0が下）の座標を IMGUI 系（Y=0が上）に変換する。
        /// AxisGizmo は GL.LoadPixelMatrix（Y=0が上）を前提に描画・判定する。
        /// </summary>
        private Vector2 ToImgui(Vector2 screenPosYDown)
        {
            float h = GetPanelHeight?.Invoke() ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }

        /// <summary>
        /// 範囲選択の走査結果。SelectElementsCommand へそのまま載せられる並び。
        /// </summary>
        private struct RegionPick
        {
            public List<int> VertexMeshIndices;
            public List<int> VertexIndices;
            public List<int> EdgeMeshIndices;
            public List<int> EdgePairs;
            public List<int> FaceMeshIndices;
            public List<int> FaceIndices;
            public List<int> LineMeshIndices;
            public List<int> LineIndices;

            public static RegionPick Create() => new RegionPick
            {
                VertexMeshIndices = new List<int>(), VertexIndices = new List<int>(),
                EdgeMeshIndices   = new List<int>(), EdgePairs     = new List<int>(),
                FaceMeshIndices   = new List<int>(), FaceIndices   = new List<int>(),
                LineMeshIndices   = new List<int>(), LineIndices   = new List<int>(),
            };
        }

        /// <summary>
        /// 画面上の範囲に入る要素を集める。SelectionState には触らない。
        ///
        /// 矩形と投げ縄で違うのは内外判定だけなので、判定を contains で受けて
        /// 走査規則を 1 本にする。
        ///
        /// 選択メッシュを全て走査する。頂点オフセット（GetVertexOffset）は
        /// メッシュごとに異なるため、必ずメッシュ単位で取り直すこと。
        /// GetScreenPositions() は全メッシュぶんを連結したグローバル配列なので
        /// そのまま使える。
        ///
        /// 可視判定は GPU 計算済みの IsVertexVisible による。辺・線分は両端頂点、
        /// 面は全頂点が可視のときだけ対象にする（裏面を拾わないため）。
        /// </summary>
        private RegionPick CollectInRegion(Func<Vector2, bool> contains)
        {
            var pick = RegionPick.Create();

            var model = _project?.CurrentModel;
            if (model == null) return pick;

            var screenPos = GetScreenPositions?.Invoke();
            float vpH     = GetViewportHeight?.Invoke() ?? 0f;

            var mode = _selectionOps.SelectionState?.Mode
                    ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge |
                        MeshSelectMode.Face   | MeshSelectMode.Line);

            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(ctxIdx);
                if (mc?.MeshObject == null || mc.Selection == null) continue;

                var mo = mc.MeshObject;
                int vertexOffset = GetVertexOffset?.Invoke(ctxIdx) ?? 0;

                // スクリーン座標取得ヘルパー（このメッシュのローカル頂点番号 → 画面座標）
                Func<int, Vector2> vertexScreen = (i) =>
                {
                    if (screenPos == null || vertexOffset + i >= screenPos.Length)
                        return new Vector2(-10000, -10000);
                    return new Vector2(screenPos[vertexOffset + i].x, vpH - screenPos[vertexOffset + i].y);
                };

                bool Visible(int localIndex)
                    => IsVertexVisible == null || IsVertexVisible(vertexOffset + localIndex);

                // 頂点
                if (mode.Has(MeshSelectMode.Vertex))
                {
                    for (int i = 0; i < mo.Vertices.Count; i++)
                    {
                        if (!Visible(i)) continue;
                        if (!contains(vertexScreen(i))) continue;
                        pick.VertexMeshIndices.Add(ctxIdx);
                        pick.VertexIndices.Add(i);
                    }
                }

                // 辺
                if (mode.Has(MeshSelectMode.Edge))
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 2) continue;
                        for (int ei = 0; ei < face.VertexCount; ei++)
                        {
                            int v1 = face.VertexIndices[ei];
                            int v2 = face.VertexIndices[(ei + 1) % face.VertexCount];
                            if (!Visible(v1) || !Visible(v2)) continue;
                            if (!contains(vertexScreen(v1)) || !contains(vertexScreen(v2))) continue;
                            pick.EdgeMeshIndices.Add(ctxIdx);
                            pick.EdgePairs.Add(v1);
                            pick.EdgePairs.Add(v2);
                        }
                    }
                }

                // 面
                if (mode.Has(MeshSelectMode.Face))
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 3) continue;
                        bool allIn = true;
                        foreach (int vi in face.VertexIndices)
                        {
                            if (!Visible(vi) || !contains(vertexScreen(vi))) { allIn = false; break; }
                        }
                        if (!allIn) continue;
                        pick.FaceMeshIndices.Add(ctxIdx);
                        pick.FaceIndices.Add(fi);
                    }
                }

                // 線分
                if (mode.Has(MeshSelectMode.Line))
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount != 2) continue;
                        int v1 = face.VertexIndices[0];
                        int v2 = face.VertexIndices[1];
                        if (!Visible(v1) || !Visible(v2)) continue;
                        if (!contains(vertexScreen(v1)) || !contains(vertexScreen(v2))) continue;
                        pick.LineMeshIndices.Add(ctxIdx);
                        pick.LineIndices.Add(fi);
                    }
                }
            }

            return pick;
        }

        /// <summary>
        /// 範囲選択の走査結果をコマンドとして発行する。
        ///
        /// Op は修飾キーで決まる。修飾なし = Replace、Shift = Add、
        /// Ctrl = Toggle（範囲内の既選択は外れ、未選択は入る。従来の Ctrl 矩形と同じ）。
        /// 書き込み・頂点展開・選択 Undo はディスパッチャ経由で ExecuteFromCommand へ戻る。
        /// </summary>
        private void SendRegionPick(RegionPick pick, ModifierKeys mods)
        {
            if (SendCommand == null) return;

            var model = _project?.CurrentModel;
            if (model == null) return;

            var targets = model.SelectedDrawableMeshIndices;
            var clear   = new int[targets.Count];
            for (int i = 0; i < targets.Count; i++) clear[i] = targets[i];

            var op = mods.Ctrl  ? Poly_Ling.Data.SelectElementsCommand.SelectOp.Toggle
                   : mods.Shift ? Poly_Ling.Data.SelectElementsCommand.SelectOp.Add
                                : Poly_Ling.Data.SelectElementsCommand.SelectOp.Replace;

            SendCommand(new Poly_Ling.Data.SelectElementsCommand(
                _project?.CurrentModelIndex ?? 0,
                clear,
                pick.VertexIndices.ToArray(), pick.VertexMeshIndices.ToArray(),
                pick.EdgePairs.ToArray(),     pick.EdgeMeshIndices.ToArray(),
                pick.FaceIndices.ToArray(),   pick.FaceMeshIndices.ToArray(),
                pick.LineIndices.ToArray(),   pick.LineMeshIndices.ToArray(),
                op));
        }

        /// <summary>
        /// 矩形選択を確定する。走査は CollectInRegion、書き込みはコマンド経由。
        /// </summary>
        private void CommitBoxSelect(ModifierKeys mods)
        {
            var rect = _selectionOps.BoxRect;
            _selectionOps.EndBoxSelect();

            if (GetScreenPositions == null) return;

            var model = _project?.CurrentModel;
            if (model == null || model.SelectedDrawableMeshIndices.Count == 0) return;

            SendRegionPick(CollectInRegion(p => rect.Contains(p, true)), mods);
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// 投げ縄選択を確定する。走査規則は矩形と同じ（CollectInRegion を共有）。
        ///
        /// 座標系の確認：
        /// GetScreenPositions() は NDC から screenY = (1 - ndcY) * height で計算 → UIToolkit Y（Y=0上）
        /// CollectInRegion の vertexScreen は vpH - UIToolkitY → GPU Y（Y=0下）
        /// LassoPoints は ToViewportCoord()（h - local.y）→ GPU Y（Y=0下）
        /// → 同じ GPU Y。変換不要。
        /// </summary>
        private void CommitLassoSelect(ModifierKeys mods)
        {
            var lassoGPU = new List<Vector2>(_selectionOps.LassoPoints);
            _selectionOps.EndLassoSelect();

            if (lassoGPU.Count < 3) return;
            if (GetScreenPositions == null) return;

            var model = _project?.CurrentModel;
            if (model == null || model.SelectedDrawableMeshIndices.Count == 0) return;

            SendRegionPick(CollectInRegion(p => IsPointInLasso(p, lassoGPU)), mods);
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// Ray Casting アルゴリズムによる投げ縄内外判定。
        /// エディタ側 PolyLing_Input.IsPointInLasso と同一実装。
        /// </summary>
        private static bool IsPointInLasso(Vector2 point, System.Collections.Generic.List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;
            bool inside = false;
            int count = polygon.Count;
            int j = count - 1;
            for (int i = 0; i < count; i++)
            {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) /
                              (polygon[j].y - polygon[i].y) + polygon[i].x)
                {
                    inside = !inside;
                }
                j = i;
            }
            return inside;
        }
    }
}
