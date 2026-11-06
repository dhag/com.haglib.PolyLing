// ModifierGizmoToolHandler.cs
// 処理列の項目（デフォーマ・ラティス）の引数を、ビューのハンドルで動かす IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// 【正本】PolyLing_モディファイア操作統合計画.md 段 8
//
// 【書き込み先】項目の引数だけ。ドラッグ中は setMeshModifierArgs の preview=true、離すと preview=false を
//   送る（Undo はドラッグ 1 回で 1 件。カードのスライダーと同じ経路）。ここは引数を自分で書き換えない。
//   表示は毎回、項目の引数から作り直すので、Undo や欄の変更にも追随する。ドラッグ中だけは手元の値を使う。
//
// 【作業軸】WorkAxisToolHandler をもう 1 つ持ち、一時の WorkAxisContext を読み書きさせる（六角錐・
//   移動・回転・ロール・Y 先端のハンドルは作業軸ツールと同じ）。頂点とボーンへの吸着は使わない。
//   引数の座標は元メッシュのローカル、一時の作業軸はワールドなので、元メッシュの表示の行列で移す。
//
// 【ラティス】制御点の選択（クリック・Ctrl で追加・矩形）と移動は格子ツール（LatticeToolHandler）と同じ操作。
//   格子は項目の引数から LatticeDeformer.Configure で組む（評価と同じ組み立て）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Modifiers;
using Poly_Ling.Tools;
using Poly_Ling.Tools.Deformers;

namespace Poly_Ling.Player
{
    /// <summary>処理列の項目の作業軸とラティスの制御点をビューで動かす。</summary>
    [PLTool("modifierGizmo", Description = "処理列の項目の作業軸とラティスの制御点のハンドル（確定は setMeshModifierArgs）")]
    public class ModifierGizmoToolHandler : IPlayerToolHandler, IPlayerGizmoProvider
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>    GetToolContext;
        public Func<float>          GetPanelHeight;
        public Action               OnRepaint;
        public Func<ModelContext>   GetModel;
        public Func<int>            GetModelIndex;
        public Action<PanelCommand> SendCommand;
        /// <summary>ハンドルの表示が変わったときの再描画（オーバーレイとギズモ）。</summary>
        public Action               OnRefreshOverlay;
        public Action<Vector2, Vector2> OnBoxSelectUpdate;
        public Action               OnBoxSelectEnd;
        /// <summary>対象の項目が無くなったとき（消された・Undo で戻った）。Viewer がモードを抜ける。</summary>
        public Action               OnTargetLost;

        // ================================================================
        // 状態
        // ================================================================

        [PLToolState(Description = "動かしている項目のスタック名")]
        public string StackName { get; private set; } = "";

        [PLToolState(Description = "動かしている項目の ScenarioItemId")]
        public string ItemId { get; private set; } = "";

        [PLToolState(Description = "動かしているもの")]
        public ModifierGizmoKind Kind { get; private set; } = ModifierGizmoKind.Off;

        [PLToolState(Description = "選択中の制御点の数")]
        public int SelectedPointCount => _selectedPoints.Count;

        [PLToolState(Description = "格子の制御点の数。ラティス以外は 0")]
        public int ControlPointCount => Kind == ModifierGizmoKind.LatticePoints && _deformer != null && _deformer.Grid.IsBuilt
            ? _deformer.Grid.ControlPointCount : 0;

        [PLToolParam(Description = "制御点のヒット判定半径（ピクセル）")]
        public float PointHitRadius { get; set; } = 10f;

        // 引数から写した今の値
        private Matrix4x4  _m = Matrix4x4.identity;     // 元メッシュの頂点 → ワールド（表示の行列）
        private Matrix4x4  _mInv = Matrix4x4.identity;
        private Quaternion _mRot = Quaternion.identity;
        private Vector3    _origin;
        private Quaternion _rot = Quaternion.identity;

        // 作業軸
        private readonly WorkAxisToolHandler _axisTool;
        private readonly WorkAxisContext _axis = new WorkAxisContext();
        private float _axisLength = 1f;
        private bool  _axisDrag;
        private bool  _previewOpen;
        private string[] _lastValues;

        // ラティス
        private LatticeDeformer _deformer;
        private string _latticeSig = "";
        private readonly HashSet<int> _selectedPoints = new HashSet<int>();
        private int _hoverPoint = -1;
        private readonly AxisGizmo _pointGizmo = new AxisGizmo { ScreenOffset = Vector2.zero };
        private AxisGizmo.AxisType _hoverAxis = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _dragAxis  = AxisGizmo.AxisType.None;
        private bool    _boxSelecting;
        private bool    _boxAdditive;
        private Vector2 _boxStart;
        private bool    _pointsMoved;

        private static readonly string[] AxisKeys = { "axisOrigin", "axisRotation" };
        private static readonly string[] PointKeys = { "pointIndices", "pointOffsets" };

        public ModifierGizmoToolHandler()
        {
            _axisTool = new WorkAxisToolHandler
            {
                SnapToBone     = false,
                GetToolContext = () => GetToolContext?.Invoke(),
                GetPanelHeight = () => GetPanelHeight?.Invoke() ?? 0f,
                OnRepaint      = () => OnRepaint?.Invoke(),
                GetWorkAxis    = () => _axis,
                GetModelIndex  = () => GetModelIndex?.Invoke() ?? 0,
                OnValueChanged = SendAxisPreview,
                SendCommand    = OnAxisCommitted,
            };
        }

        // ================================================================
        // 開始・終了
        // ================================================================

        /// <summary>項目を対象にする。動かせない項目なら理由を返す。</summary>
        public string Begin(string stackName, string itemId, ModifierGizmoKind kind)
        {
            // 同じ項目で作業軸と制御点を行き来しても、選んだ制御点は残す。
            if (StackName != (stackName ?? "") || ItemId != (itemId ?? "")) _selectedPoints.Clear();
            StackName = stackName ?? "";
            ItemId    = itemId ?? "";
            Kind      = kind;
            _latticeSig = "";
            _deformer   = null;

            string why = Sync(GetToolContext?.Invoke());
            if (why != null) { End(); return why; }

            _axisTool.Mode = kind == ModifierGizmoKind.AxisRotate
                ? WorkAxisToolHandler.WorkAxisGizmoMode.Rotate
                : WorkAxisToolHandler.WorkAxisGizmoMode.Move;
            return null;
        }

        public void End()
        {
            StackName = "";
            ItemId    = "";
            Kind      = ModifierGizmoKind.Off;
            _selectedPoints.Clear();
            _deformer     = null;
            _latticeSig   = "";
            _hoverPoint   = -1;
            _hoverAxis    = AxisGizmo.AxisType.None;
            _dragAxis     = AxisGizmo.AxisType.None;
            _axisDrag     = false;
            _previewOpen  = false;
            _boxSelecting = false;
            OnBoxSelectEnd?.Invoke();
        }

        private bool IsAxisKind => Kind == ModifierGizmoKind.AxisMove || Kind == ModifierGizmoKind.AxisRotate;

        // ================================================================
        // 項目の引数から今の値を写す
        // ================================================================

        /// <summary>引数から写す。写せなければ理由（対象が無い）。ドラッグ中は呼ばない。</summary>
        private string Sync(ToolContext ctx)
        {
            var model = GetModel?.Invoke();
            if (model == null) return "モデルがありません";
            var g = model.FindModifierStackByName(StackName);
            if (g == null) return $"処理列 \"{StackName}\" がありません";
            var step = g.FindStep(ItemId);
            if (step == null) return $"項目 \"{ItemId}\" がありません";
            ModifierChainOps.ChainEnds(model, g, out var source, out _);
            if (source?.MeshObject == null) return "元メッシュがありません";
            if (!MeshModifierRegistry.FromArgs(step.Action, step.Args, out var mod, out _, out string err)) return err;
            if (!(mod is IDeformerMeshModifier def)) return $"種類 \"{step.Action}\" には作業軸がありません";

            _m    = source.TransformRowMatrix(ctx?.ShowBindPose ?? false);
            _mInv = _m.inverse;
            _mRot = _m.rotation;

            _origin = def.AxisOrigin;
            _rot    = Quaternion.Euler(def.AxisRotation);

            _axis.Origin    = _m.MultiplyPoint3x4(_origin);
            _axis.Rotation  = _mRot * _rot;
            _axisLength     = AxisLengthOf(source.MeshObject);
            _axis.Length    = _axisLength;
            _axis.IsVisible = true;

            // ラティスは作業軸を動かしている間も格子を見せる（格子ごと動くのが作業軸の意味なので）。
            if (mod is LatticeMeshModifier lat) return SyncLattice(lat, step);
            if (Kind == ModifierGizmoKind.LatticePoints) return $"種類 \"{step.Action}\" には制御点がありません";
            return null;
        }

        /// <summary>軸の長さ：元メッシュの外接箱の長い辺の半分（ワールド）。</summary>
        private float AxisLengthOf(MeshObject mo)
        {
            if (mo.VertexCount == 0) return WorkAxisContext.MinLength;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < mo.VertexCount; i++)
            {
                Vector3 p = mo.Vertices[i].Position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            Vector3 s = max - min;
            float longest = Mathf.Max(s.x, s.y, s.z) * _m.lossyScale.x;
            return Mathf.Max(WorkAxisContext.MinLength, longest * 0.5f);
        }

        private string SyncLattice(LatticeMeshModifier lat, ModifierStackStep step)
        {
            string sig = string.Join("|",
                step.GetArg("cellsX"), step.GetArg("cellsY"), step.GetArg("cellsZ"),
                step.GetArg("center"), step.GetArg("size"),
                step.GetArg("pointIndices"), step.GetArg("pointOffsets"));
            if (_deformer != null && sig == _latticeSig) return null;

            var d = new LatticeDeformer();
            if (!d.Configure(lat.CellsX, lat.CellsY, lat.CellsZ, lat.Center, lat.Size, lat.PointIndices, lat.PointOffsets, out string error))
                return error;

            // 分割数が変わったら制御点の番号の意味が変わるので選択を捨てる。
            if (_deformer == null || _deformer.Grid.ControlPointCount != d.Grid.ControlPointCount)
                _selectedPoints.Clear();
            _deformer   = d;
            _latticeSig = sig;
            return null;
        }

        /// <summary>入力の前に呼ぶ。対象が無くなっていれば false（Viewer へ知らせる）。</summary>
        private bool SyncOrLose(ToolContext ctx)
        {
            if (Kind == ModifierGizmoKind.Off) return false;
            if (_axisDrag || _dragAxis != AxisGizmo.AxisType.None) return true;
            if (Sync(ctx) == null) return true;
            OnTargetLost?.Invoke();
            return false;
        }

        // ================================================================
        // 作業軸：送る
        // ================================================================

        private void SendAxisPreview()
        {
            ToArgs(_axis.Origin, _axis.Rotation, out string o, out string r);
            SendArgs(AxisKeys, new[] { o, r }, preview: true);
            _previewOpen = true;
            // ドラッグ中は引数から写し直さないので、格子の表示に使う値をここで合わせる。
            _origin = _mInv.MultiplyPoint3x4(_axis.Origin);
            _rot    = Quaternion.Inverse(_mRot) * _axis.Rotation;
            OnRefreshOverlay?.Invoke();
        }

        /// <summary>WorkAxisToolHandler の確定（作業軸そのものではなく項目の引数へ書く）。</summary>
        private void OnAxisCommitted(PanelCommand cmd)
        {
            if (!(cmd is SetWorkAxisCommand c)) return;
            ToArgs(c.Origin, Quaternion.Euler(c.EulerAngles), out string o, out string r);
            SendArgs(AxisKeys, new[] { o, r }, preview: false);
            _previewOpen = false;
        }

        private void ToArgs(Vector3 worldOrigin, Quaternion worldRotation, out string origin, out string rotation)
        {
            Vector3 lo = _mInv.MultiplyPoint3x4(worldOrigin);
            Vector3 e  = (Quaternion.Inverse(_mRot) * worldRotation).eulerAngles;
            for (int k = 0; k < 3; k++)
            {
                if (e[k] > 180f) e[k] -= 360f;
                // 回していない成分に計算の誤差を残さない（0.00001 度が欄に出ないように）。
                e[k] = Mathf.Round(e[k] * 1000f) / 1000f;
            }
            PanelCommandFactory.TryFormatValue(lo, out origin);
            PanelCommandFactory.TryFormatValue(e, out rotation);
        }

        private void SendArgs(string[] keys, string[] values, bool preview)
        {
            _lastValues = values;
            SendCommand?.Invoke(new SetMeshModifierArgsCommand(
                GetModelIndex?.Invoke() ?? 0, StackName, ItemId, keys, values, preview));
        }

        // ================================================================
        // ラティス：送る
        // ================================================================

        private void SendPoints(bool preview)
        {
            var grid = _deformer.Grid;
            var idx = new List<int>();
            var off = new List<float>();
            for (int i = 0; i < grid.ControlPointCount; i++)
            {
                Vector3 d = grid.GetCurrent(i) - grid.GetBase(i);
                if (d.sqrMagnitude < 1e-12f) continue;
                idx.Add(i);
                off.Add(d.x); off.Add(d.y); off.Add(d.z);
            }
            PanelCommandFactory.TryFormatValue(idx.ToArray(), out string si);
            PanelCommandFactory.TryFormatValue(off.ToArray(), out string so);
            SendArgs(PointKeys, new[] { si ?? "", so ?? "" }, preview);
        }

        private Vector3 PointToWorld(Vector3 p) => _m.MultiplyPoint3x4(_origin + _rot * p);

        private Vector2 ToImgui(Vector2 screenPosYDown)
        {
            float h = GetPanelHeight?.Invoke() ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }

        private static Vector2 ToHandlerScreen(ToolContext ctx, Vector3 world, float h)
        {
            Vector2 s = ctx.WorldToScreen(world);
            return new Vector2(s.x, h - s.y);
        }

        private bool TrySyncPointGizmo()
        {
            if (_deformer == null || !_deformer.Grid.IsBuilt || _selectedPoints.Count == 0) return false;
            Vector3 sum = Vector3.zero;
            foreach (int i in _selectedPoints) sum += _deformer.Grid.GetCurrent(i);
            _pointGizmo.Center      = PointToWorld(sum / _selectedPoints.Count);
            _pointGizmo.Orientation = _mRot * _rot;
            return true;
        }

        private int PickPoint(Vector2 screenPos, ToolContext ctx)
        {
            if (_deformer == null || !_deformer.Grid.IsBuilt) return -1;
            var cp = _deformer.Grid.CurrentControlPoints;
            float h = ctx.PreviewRect.height;
            int best = -1;
            float bestSq = PointHitRadius * PointHitRadius;
            for (int i = 0; i < cp.Length; i++)
            {
                float d = (ToHandlerScreen(ctx, PointToWorld(cp[i]), h) - screenPos).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = i; }
            }
            return best;
        }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (Kind != ModifierGizmoKind.LatticePoints) return;
            var ctx = GetToolContext?.Invoke();
            if (ctx == null || !SyncOrLose(ctx)) return;

            int idx = PickPoint(screenPos, ctx);
            if (idx < 0)
            {
                if (!mods.Ctrl) _selectedPoints.Clear();
            }
            else if (mods.Ctrl)
            {
                if (!_selectedPoints.Remove(idx)) _selectedPoints.Add(idx);
            }
            else
            {
                _selectedPoints.Clear();
                _selectedPoints.Add(idx);
            }
            OnRefreshOverlay?.Invoke();
            OnRepaint?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null || !SyncOrLose(ctx)) return;

            if (IsAxisKind)
            {
                _axisDrag    = true;
                _previewOpen = false;
                _axisTool.OnLeftDragBegin(hit, screenPos, mods);
                return;
            }

            _dragAxis     = AxisGizmo.AxisType.None;
            _boxSelecting = false;
            _pointsMoved  = false;

            if (TrySyncPointGizmo())
            {
                var axis = _pointGizmo.FindAxisAtScreenPos(ToImgui(screenPos), ctx);
                if (axis != AxisGizmo.AxisType.None) { _dragAxis = axis; return; }
            }

            _boxSelecting = true;
            _boxAdditive  = mods.Ctrl;
            _boxStart     = screenPos;
            OnBoxSelectUpdate?.Invoke(_boxStart, screenPos);
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (IsAxisKind)
            {
                if (_axisDrag) _axisTool.OnLeftDrag(screenPos, delta, mods);
                return;
            }
            if (_boxSelecting)
            {
                OnBoxSelectUpdate?.Invoke(_boxStart, screenPos);
                OnRepaint?.Invoke();
                return;
            }
            if (_dragAxis == AxisGizmo.AxisType.None) return;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null || !TrySyncPointGizmo()) return;

            // delta は +Y が画面上。ComputeFreeDelta はそのまま、ComputeAxisDelta は +Y が画面下の系（格子ツールと同じ）。
            Vector3 world = _dragAxis == AxisGizmo.AxisType.Center
                ? _pointGizmo.ComputeFreeDelta(delta, ctx)
                : _pointGizmo.ComputeAxisDelta(new Vector2(delta.x, -delta.y), _dragAxis, ctx);
            if (world == Vector3.zero) return;

            Vector3 local = Quaternion.Inverse(_rot) * _mInv.MultiplyVector(world);
            foreach (int i in _selectedPoints)
                _deformer.Grid.SetCurrent(i, _deformer.Grid.GetCurrent(i) + local);

            _pointsMoved = true;
            SendPoints(preview: true);
            OnRefreshOverlay?.Invoke();
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            if (IsAxisKind)
            {
                if (!_axisDrag) return;
                _axisTool.OnLeftDragEnd(screenPos, mods);
                // 動かして元の値へ戻した場合は作業軸ツールが確定を送らない。開いた下見をここで閉じる。
                if (_previewOpen)
                {
                    SendArgs(AxisKeys, _lastValues, preview: false);
                    _previewOpen = false;
                }
                _axisDrag = false;
                OnRefreshOverlay?.Invoke();
                return;
            }

            if (_boxSelecting)
            {
                _boxSelecting = false;
                OnBoxSelectEnd?.Invoke();
                var ctx = GetToolContext?.Invoke();
                if (ctx != null) CommitBoxSelect(_boxStart, screenPos, ctx);
            }
            else if (_dragAxis != AxisGizmo.AxisType.None && _pointsMoved)
            {
                SendPoints(preview: false);
            }
            _dragAxis    = AxisGizmo.AxisType.None;
            _pointsMoved = false;
            OnRefreshOverlay?.Invoke();
            OnRepaint?.Invoke();
        }

        private void CommitBoxSelect(Vector2 start, Vector2 end, ToolContext ctx)
        {
            if (_deformer == null || !_deformer.Grid.IsBuilt) return;
            float x0 = Mathf.Min(start.x, end.x), x1 = Mathf.Max(start.x, end.x);
            float y0 = Mathf.Min(start.y, end.y), y1 = Mathf.Max(start.y, end.y);
            if (!_boxAdditive) _selectedPoints.Clear();
            var cp = _deformer.Grid.CurrentControlPoints;
            float h = ctx.PreviewRect.height;
            for (int i = 0; i < cp.Length; i++)
            {
                Vector2 sp = ToHandlerScreen(ctx, PointToWorld(cp[i]), h);
                if (sp.x >= x0 && sp.x <= x1 && sp.y >= y0 && sp.y <= y1) _selectedPoints.Add(i);
            }
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null || !SyncOrLose(ctx)) return;

            if (IsAxisKind)
            {
                _axisTool.UpdateHover(screenPos, ctx);
                return;
            }

            _hoverAxis = TrySyncPointGizmo()
                ? _pointGizmo.FindAxisAtScreenPos(ToImgui(screenPos), ctx)
                : AxisGizmo.AxisType.None;
            _hoverPoint = _hoverAxis == AxisGizmo.AxisType.None ? PickPoint(screenPos, ctx) : -1;
        }

        // ================================================================
        // ギズモ
        // ================================================================

        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;
            if (ctx == null || !SyncOrLose(ctx)) return false;

            if (IsAxisKind) return _axisTool.TryBuildGizmoData(ctx, out data);

            if (!TrySyncPointGizmo()) return false;
            _pointGizmo.HoveredAxis  = _hoverAxis;
            _pointGizmo.DraggingAxis = _dragAxis;
            _pointGizmo.GetScreenPositions(ctx, out var o, out var xe, out var ye, out var ze);
            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo     = true,
                Origin       = o, XEnd = xe, YEnd = ye, ZEnd = ze,
                HoveredAxis  = _dragAxis != AxisGizmo.AxisType.None ? _dragAxis : _hoverAxis,
                DraggingAxis = _dragAxis,
            };
            return true;
        }

        /// <summary>アクティブでない画面の表示（作業軸の形だけ）。</summary>
        public bool TryBuildDisplayOnlyGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;
            if (ctx == null || !IsAxisKind) return false;
            return _axisTool.TryBuildDisplayOnlyGizmoData(ctx, out data);
        }

        // ================================================================
        // オーバーレイ（格子の線と制御点）
        // ================================================================

        private static readonly Color LineColor     = new Color(0.35f, 0.75f, 1f, 0.55f);
        private static readonly Color PointColor    = new Color(0.9f,  0.9f,  0.9f, 0.9f);
        private static readonly Color SelectedColor = new Color(1f,    0.85f, 0.2f, 1f);
        private static readonly Color HoverColor    = new Color(0.3f,  1f,    1f,   1f);

        /// <summary>格子の線と制御点（スクリーン座標、Y=0 下）。ラティスでなければ false。作業軸を動かしている間は線だけ。</summary>
        public bool TryBuildOverlay(
            ToolContext ctx,
            out List<(Vector2 a, Vector2 b, Color col)> lines,
            out List<(Vector2 p, Color col, float halfSize)> points)
        {
            lines = null;
            points = null;
            if (ctx == null || !SyncOrLose(ctx)) return false;
            if (_deformer == null || !_deformer.Grid.IsBuilt) return false;
            bool showPoints = Kind == ModifierGizmoKind.LatticePoints;

            var grid = _deformer.Grid;
            var cp = grid.CurrentControlPoints;
            float h = ctx.PreviewRect.height;
            var sp = new Vector2[cp.Length];
            for (int i = 0; i < cp.Length; i++) sp[i] = ToHandlerScreen(ctx, PointToWorld(cp[i]), h);

            lines  = new List<(Vector2, Vector2, Color)>();
            points = new List<(Vector2, Color, float)>();
            int px = grid.PointCountX, py = grid.PointCountY, pz = grid.PointCountZ;
            for (int iz = 0; iz < pz; iz++)
            for (int iy = 0; iy < py; iy++)
            for (int ix = 0; ix < px; ix++)
            {
                int i = grid.PointIndex(ix, iy, iz);
                if (ix + 1 < px) lines.Add((sp[i], sp[grid.PointIndex(ix + 1, iy, iz)], LineColor));
                if (iy + 1 < py) lines.Add((sp[i], sp[grid.PointIndex(ix, iy + 1, iz)], LineColor));
                if (iz + 1 < pz) lines.Add((sp[i], sp[grid.PointIndex(ix, iy, iz + 1)], LineColor));

                if (!showPoints) continue;
                bool sel = _selectedPoints.Contains(i), hover = i == _hoverPoint;
                points.Add((sp[i], hover ? HoverColor : (sel ? SelectedColor : PointColor), (hover || sel) ? 4.5f : 3f));
            }
            return true;
        }
    }
}
