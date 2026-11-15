// PlayerTwoModelSubPanel.cs
// 2 モデル表示：2 つのモデルのオブジェクトの一覧を左右に並べ、行をドラッグして落とすと
// 頂点位置を写す（差し替え・形として取り込み・書き戻し）。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置
//
// 【並べ方】左がモデル B（取り込み元）、右がモデル A（書き込み先）。どちらの列も右ペインの全高を使い、
//   別々にスクロールする。ドラッグは横へ動かすだけで済む（上下に重ねると長いリストを越えて落とすことになる）。
//   このパネルが出ている間、右ペインは常駐のリストを隠して広がる（PlayerLayoutRoot.SoloSection）。
//
// 【ドラッグ】
//   B の行 → A の行：A の列の「書き込み先」へ書く。
//   A の行 → B の行：B の列の「書き込み先」へ書く（書き戻し）。頂点数が合わなければ B の末尾に足す。
//   A の行 → B の列の空いた所：B の末尾に足す。
//   ドラッグを始めると、相手の列を候補の行（同じ名前、無ければ同じ頂点数）までスクロールして枠で示す。
//   相手の列の上端・下端近くでは自動でスクロールする。行の外で離したら何もしない（上の末尾追加を除く）。
//
// 【書き込み先】各列の見出しで選ぶ。今の形・基本の形・既存の形（位置の形だけ）・新しい形…（名前の欄）。
//   行には名前・頂点数と、選んだ形でそのオブジェクトの何頂点が基本の形から動いているかを出す。
//
// パネルはコマンド（importModelVertices）を送るだけで、処理はしない。
// 行（ドラッグの対象）は数が変わるので、ボタンや入力欄として登録しない（データ行のコンテナ）。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerTwoModelSubPanel
    {
        public Func<Poly_Ling.View.IProjectView> GetView;
        public Action<PanelCommand> SendCommand;
        /// <summary>直近の importModelVertices の結果の一文（ディスパッチャ）。</summary>
        public Func<string> GetLastSummary;
        /// <summary>閉じる（右ペインを元に戻す）。</summary>
        public Action OnClose;

        private enum Side { B = 0, A = 1 }

        private class RowInfo
        {
            public VisualElement Element;
            public Label Label;
            public int    MasterIndex;
            public string Name;
            public int    VertexCount;
        }

        private class Column
        {
            public int ModelIndex = -1;
            public DropdownField ModelDrop;
            public DropdownField TargetDrop;
            public TextField NewNameField;
            public TextField FilterField;
            public ScrollView Scroll;
            public VisualElement Root;
            public readonly List<RowInfo> Rows = new List<RowInfo>();
            /// <summary>書き込み先の選択肢に対応する形（-1＝今の形、0＝基本の形、正＝形の番号、-2＝新しい形）。</summary>
            public readonly List<int> TargetIds = new List<int>();
            /// <summary>次の Refresh で選び直す形の名前（新しい形を作った直後）。</summary>
            public string PendingShapeName;
        }

        private const int TargetCurrent = -1;
        private const int TargetNew     = -2;

        private readonly Column _b = new Column();
        private readonly Column _a = new Column();

        // UI
        // UI 自動操作の ID は "twoModel.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("modelA", Description = "モデル A（右の列・取り込み先）")]
        private DropdownField _modelADrop;
        [UiControl("modelB", Description = "モデル B（左の列・取り込み元）")]
        private DropdownField _modelBDrop;
        [UiControl("targetA", Description = "B の行を A の行へ落としたときの書き込み先（今の形・基本の形・形・新しい形）")]
        private DropdownField _targetADrop;
        [UiControl("targetB", Description = "A の行を B の行へ落としたときの書き込み先（今の形・基本の形・形・新しい形）")]
        private DropdownField _targetBDrop;
        [UiControl("shapeNameA", Description = "A の書き込み先が「新しい形…」のときの形の名前（同じ名前の形があればその形を書き換える）")]
        private TextField _newNameA;
        [UiControl("shapeNameB", Description = "B の書き込み先が「新しい形…」のときの形の名前")]
        private TextField _newNameB;
        [UiControl("filterA", Description = "A の列の名前の絞り込み")]
        private TextField _filterA;
        [UiControl("filterB", Description = "B の列の名前の絞り込み")]
        private TextField _filterB;
        [UiControl("matchMode", Description = "頂点の対応（頂点番号 / 頂点 ID）。頂点 ID は補助")]
        private DropdownField _matchModeDrop;
        [UiControl("keepOffsets", Description = "基本の形へ書くとき、形の差分を保つ")]
        private Toggle _keepOffsetsToggle;
        [UiControl("close", Safety = UiSafety.SafeWrite, Description = "2 モデル表示を閉じ、右ペインを元に戻す")]
        private Button _closeBtn;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label _statusLabel;
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _rowsA;
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _rowsB;

        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl(Ignore = true)]
        private Label _ghost;

        // ドラッグの状態
        private Side?   _pressSide;
        private RowInfo _pressRow;
        private Vector2 _pressPos;
        private bool    _dragging;
        private Vector2 _lastPos;
        private RowInfo _hoverRow;
        private RowInfo _candidateRow;
        private bool    _hoverColumnEmpty;
        private IVisualElementScheduledItem _autoScroll;

        private const float DragThreshold   = 6f;
        private const float AutoScrollEdge  = 28f;
        private const float AutoScrollStep  = 14f;

        private static readonly Color RowColor       = new Color(0f, 0f, 0f, 0f);
        private static readonly Color SourceColor    = new Color(0.25f, 0.45f, 0.8f, 0.55f);
        private static readonly Color HoverColor     = new Color(0.3f, 0.75f, 0.35f, 0.6f);
        private static readonly Color CandidateColor = new Color(1f, 0.8f, 0.2f, 1f);

        private Poly_Ling.View.IProjectView GetProject() => GetView?.Invoke();

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingLeft = _root.style.paddingRight = _root.style.paddingTop = _root.style.paddingBottom = 4;
            _root.style.flexGrow = 1;
            parent.Add(_root);

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            var title = PlayerIoUiKit.Title("2 モデル間の頂点取り込み");
            title.style.flexGrow = 1;
            title.style.marginBottom = 0;
            head.Add(title);
            _closeBtn = new Button(() => OnClose?.Invoke()) { text = "閉じる" };
            head.Add(_closeBtn);
            _root.Add(head);

            var help = new Label("行を相手の列の行へドラッグして落とすと、頂点位置を写します。"
                               + "B→A は A の書き込み先へ、A→B は B の書き込み先へ（頂点数が合わなければ B の末尾に足す）。");
            help.style.fontSize = 9;
            help.style.whiteSpace = WhiteSpace.Normal;
            help.style.color = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            help.style.marginBottom = 4;
            _root.Add(help);

            var cols = new VisualElement();
            cols.style.flexDirection = FlexDirection.Row;
            cols.style.flexGrow = 1;
            _root.Add(cols);

            BuildColumn(cols, _b, "モデル B（取り込み元）", out _modelBDrop, out _targetBDrop, out _newNameB, out _filterB, out _rowsB);
            var gap = new VisualElement(); gap.style.width = 6; cols.Add(gap);
            BuildColumn(cols, _a, "モデル A（書き込み先）", out _modelADrop, out _targetADrop, out _newNameA, out _filterA, out _rowsA);

            // 詳細（頂点の対応は補助なので畳む）
            var fo = new Foldout { text = "詳細", value = false };
            fo.style.marginTop = 4;
            _matchModeDrop = new DropdownField("頂点の対応", new List<string> { "頂点番号", "頂点 ID（補助）" }, 0);
            fo.Add(_matchModeDrop);
            _keepOffsetsToggle = new Toggle("基本の形へ書くとき形の差分を保つ") { value = true };
            fo.Add(_keepOffsetsToggle);
            _root.Add(fo);

            _statusLabel = PlayerIoUiKit.StatusLabel();
            _root.Add(_statusLabel);

            _ghost = new Label();
            _ghost.style.position = Position.Absolute;
            _ghost.style.backgroundColor = new StyleColor(new Color(0.15f, 0.25f, 0.45f, 0.9f));
            _ghost.style.color = new StyleColor(Color.white);
            _ghost.style.fontSize = 10;
            _ghost.style.paddingLeft = _ghost.style.paddingRight = 4;
            _ghost.pickingMode = PickingMode.Ignore;
            _ghost.style.display = DisplayStyle.None;
            _root.Add(_ghost);

            _root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _root.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            _root.RegisterCallback<GeometryChangedEvent>(_ => UpdateListHeights());
            _root.RegisterCallback<AttachToPanelEvent>(e =>
                e.destinationPanel?.visualTree?.RegisterCallback<GeometryChangedEvent>(_ => UpdateListHeights()));

            _autoScroll = _root.schedule.Execute(AutoScrollTick).Every(40);
            _autoScroll.Pause();

            PlayerLayoutRoot.ApplyDarkTheme(_root);
            Refresh();
        }

        private void BuildColumn(VisualElement parent, Column col, string caption,
                                 out DropdownField modelDrop, out DropdownField targetDrop,
                                 out TextField newName, out TextField filter, out VisualElement rows)
        {
            var c = new VisualElement();
            c.style.flexGrow = 1;
            c.style.flexBasis = 0;
            c.style.minWidth = 0;
            col.Root = c;
            parent.Add(c);

            c.Add(PlayerIoUiKit.SectionLabel(caption));

            modelDrop = new DropdownField();
            var md = modelDrop;
            md.RegisterValueChangedCallback(_ => { col.ModelIndex = md.index; RebuildColumn(col); });
            c.Add(md);

            targetDrop = new DropdownField("書き込み先");
            var td = targetDrop;
            td.labelElement.style.minWidth = 52;
            td.RegisterValueChangedCallback(_ => { UpdateNewNameVisibility(col); RefreshRowLabels(col); });
            c.Add(td);

            newName = new TextField("形の名前");
            newName.labelElement.style.minWidth = 52;
            newName.style.display = DisplayStyle.None;
            c.Add(newName);

            filter = new TextField("絞り込み");
            filter.labelElement.style.minWidth = 52;
            filter.RegisterValueChangedCallback(_ => ApplyFilter(col));
            c.Add(filter);

            col.ModelDrop = modelDrop; col.TargetDrop = targetDrop; col.NewNameField = newName; col.FilterField = filter;

            col.Scroll = new ScrollView(ScrollViewMode.Vertical);
            col.Scroll.style.height = 300;
            col.Scroll.style.borderTopWidth = col.Scroll.style.borderBottomWidth =
            col.Scroll.style.borderLeftWidth = col.Scroll.style.borderRightWidth = 1;
            var bc = new StyleColor(new Color(1f, 1f, 1f, 0.12f));
            col.Scroll.style.borderTopColor = col.Scroll.style.borderBottomColor =
            col.Scroll.style.borderLeftColor = col.Scroll.style.borderRightColor = bc;
            rows = col.Scroll.contentContainer;
            c.Add(col.Scroll);
        }

        /// <summary>列の高さを、右ペインの下端まで使う大きさにする（外側のスクロールに任せると全高が取れない）。</summary>
        private void UpdateListHeights()
        {
            var tree = _root?.panel?.visualTree;
            if (tree == null) return;
            float bottom = tree.layout.height;
            foreach (var col in new[] { _b, _a })
            {
                if (col.Scroll == null) continue;
                float top = col.Scroll.worldBound.yMin;
                if (float.IsNaN(top) || float.IsNaN(bottom) || bottom <= 0f) continue;
                // 下の「詳細」と状態の欄のぶんを残す。
                float h = Mathf.Max(160f, bottom - top - 90f);
                if (Mathf.Abs(col.Scroll.resolvedStyle.height - h) > 2f) col.Scroll.style.height = h;
            }
        }

        // ================================================================
        // Refresh
        // ================================================================

        /// <summary>開いたとき、ほかのコマンドの後に呼ぶ（モデル・形・オブジェクトを読み直す）。</summary>
        public void Refresh()
        {
            var project = GetProject();
            if (project == null || _root == null) return;

            var names = new List<string>();
            for (int i = 0; i < project.ModelCount; i++)
                names.Add($"[{i}] {project.GetModelView(i)?.Name ?? "?"}");
            if (names.Count == 0) names.Add("(モデルなし)");

            if (_a.ModelIndex < 0 || _a.ModelIndex >= project.ModelCount)
                _a.ModelIndex = Mathf.Clamp(project.CurrentModelIndex, 0, names.Count - 1);
            if (_b.ModelIndex < 0 || _b.ModelIndex >= project.ModelCount)
                _b.ModelIndex = project.ModelCount > 1 ? (_a.ModelIndex == 0 ? 1 : 0) : _a.ModelIndex;

            foreach (var col in new[] { _b, _a })
            {
                col.ModelDrop.choices = names;
                col.ModelDrop.SetValueWithoutNotify(names[Mathf.Clamp(col.ModelIndex, 0, names.Count - 1)]);
                RebuildColumn(col);
            }
        }

        /// <summary>モデル A・B を選ぶ（showTwoModelView）。範囲外の値は変えない。</summary>
        public void SetModels(int modelA, int modelB)
        {
            var project = GetProject();
            if (project == null) return;
            if (modelA >= 0 && modelA < project.ModelCount) _a.ModelIndex = modelA;
            if (modelB >= 0 && modelB < project.ModelCount) _b.ModelIndex = modelB;
            Refresh();
        }

        private void RebuildColumn(Column col)
        {
            var model = GetProject()?.GetModelView(col.ModelIndex);
            RebuildTargets(col, model);
            RebuildRows(col, model);
        }

        private void RebuildTargets(Column col, Poly_Ling.View.IModelView model)
        {
            int prevId = col.TargetDrop.index >= 0 && col.TargetDrop.index < col.TargetIds.Count
                ? col.TargetIds[col.TargetDrop.index] : TargetCurrent;
            string prevName = prevId > 0 ? FindShapeName(model, prevId) : null;
            if (!string.IsNullOrEmpty(col.PendingShapeName)) { prevName = col.PendingShapeName; col.PendingShapeName = null; }

            var labels = new List<string>();
            col.TargetIds.Clear();

            string curName = "基本の形";
            if (model != null && model.CurrentShapeId != ShapeOps.BaseShapeId)
                curName = FindShapeName(model, model.CurrentShapeId) ?? "?";
            labels.Add($"今の形（{curName}）"); col.TargetIds.Add(TargetCurrent);
            labels.Add("基本の形");             col.TargetIds.Add(ShapeOps.BaseShapeId);
            int select = prevId == ShapeOps.BaseShapeId ? 1 : 0;
            if (model?.Shapes != null)
                foreach (var s in model.Shapes)
                {
                    if (s == null || s.IsUV) continue;
                    if (prevName != null && s.Name == prevName) select = labels.Count;
                    labels.Add("形: " + s.Name);
                    col.TargetIds.Add(s.Id);
                }
            labels.Add("新しい形…"); col.TargetIds.Add(TargetNew);
            if (prevId == TargetNew && prevName == null) select = labels.Count - 1;

            col.TargetDrop.choices = labels;
            col.TargetDrop.SetValueWithoutNotify(labels[select]);
            UpdateNewNameVisibility(col);
        }

        private static string FindShapeName(Poly_Ling.View.IModelView model, int id)
        {
            if (model?.Shapes == null) return null;
            foreach (var s in model.Shapes) if (s != null && s.Id == id) return s.Name;
            return null;
        }

        private void UpdateNewNameVisibility(Column col)
        {
            bool isNew = SelectedTargetId(col) == TargetNew;
            col.NewNameField.style.display = isNew ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private int SelectedTargetId(Column col)
        {
            int i = col.TargetDrop.index;
            return i >= 0 && i < col.TargetIds.Count ? col.TargetIds[i] : TargetCurrent;
        }

        private void RebuildRows(Column col, Poly_Ling.View.IModelView model)
        {
            var container = col.Scroll.contentContainer;
            container.Clear();
            col.Rows.Clear();
            if (model == null) return;

            foreach (var mv in model.DrawableList)
            {
                if (mv == null || mv.VertexCount <= 0) continue;
                // ミラー側は実体側から作られるので、書き込みの対象にも取り込み元にもしない。
                if (mv.IsMirrorSide || mv.MirrorGeometryDerived) continue;

                var info = new RowInfo { MasterIndex = mv.MasterIndex, Name = mv.Name ?? "", VertexCount = mv.VertexCount };
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.paddingLeft = 2 + Mathf.Max(0, mv.Depth) * 8;
                row.style.height = 18;
                row.style.borderTopWidth = row.style.borderBottomWidth = row.style.borderLeftWidth = row.style.borderRightWidth = 1;
                SetRowBorder(row, RowColor);
                var lbl = new Label();
                lbl.style.fontSize = 10;
                lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
                lbl.style.overflow = Overflow.Hidden;
                lbl.style.flexGrow = 1;
                lbl.pickingMode = PickingMode.Ignore;
                row.Add(lbl);
                info.Element = row; info.Label = lbl;

                var side = col == _a ? Side.A : Side.B;
                row.RegisterCallback<PointerDownEvent>(e => OnRowPointerDown(side, info, e));
                container.Add(row);
                col.Rows.Add(info);
            }
            RefreshRowLabels(col);
            ApplyFilter(col);
        }

        private void RefreshRowLabels(Column col)
        {
            var model = GetProject()?.GetModelView(col.ModelIndex);
            int tid = SelectedTargetId(col);
            int shapeId = tid == TargetCurrent ? (model?.CurrentShapeId ?? 0) : (tid > 0 ? tid : 0);
            foreach (var r in col.Rows)
            {
                string t = $"{r.Name}  ({r.VertexCount})";
                if (model != null && shapeId != ShapeOps.BaseShapeId)
                {
                    int moved = model.CountShapeMovedVertices(r.MasterIndex, shapeId);
                    if (moved > 0) t += $"  形で動く {moved}";
                }
                r.Label.text = t;
            }
        }

        private void ApplyFilter(Column col)
        {
            string f = col.FilterField?.value ?? "";
            foreach (var r in col.Rows)
                r.Element.style.display = string.IsNullOrEmpty(f) || r.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                    ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetRowBorder(VisualElement row, Color c)
        {
            var sc = new StyleColor(c);
            row.style.borderTopColor = row.style.borderBottomColor = row.style.borderLeftColor = row.style.borderRightColor = sc;
        }

        private static void SetRowBack(RowInfo r, Color c)
        {
            if (r?.Element != null) r.Element.style.backgroundColor = new StyleColor(c);
        }

        // ================================================================
        // ドラッグ
        // ================================================================

        private Column ColOf(Side s)    => s == Side.A ? _a : _b;
        private Column OtherOf(Side s)  => s == Side.A ? _b : _a;

        private void OnRowPointerDown(Side side, RowInfo row, PointerDownEvent e)
        {
            if (e.button != 0 || _root == null) return;
            EndDrag();
            _pressSide = side;
            _pressRow  = row;
            _pressPos  = e.position;
            _lastPos   = e.position;
            SetRowBack(row, SourceColor);
            _root.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            if (_pressSide == null) return;
            _lastPos = e.position;
            if (!_dragging)
            {
                if (((Vector2)e.position - _pressPos).magnitude < DragThreshold) return;
                StartDrag();
            }
            var local = _root.WorldToLocal(e.position);
            _ghost.style.left = local.x + 12;
            _ghost.style.top  = local.y + 4;
            UpdateHover();
        }

        private void StartDrag()
        {
            _dragging = true;
            _ghost.text = $"{_pressRow.Name} ({_pressRow.VertexCount})";
            _ghost.style.display = DisplayStyle.Flex;
            _ghost.BringToFront();

            // 相手の列を候補の行までスクロールして枠で示す。
            var other = OtherOf(_pressSide.Value);
            _candidateRow = FindCandidate(other, _pressRow);
            if (_candidateRow != null)
            {
                SetRowBorder(_candidateRow.Element, CandidateColor);
                other.Scroll.ScrollTo(_candidateRow.Element);
            }
            _autoScroll?.Resume();
        }

        private static RowInfo FindCandidate(Column col, RowInfo src)
        {
            foreach (var r in col.Rows)
                if (r.Element.style.display != DisplayStyle.None && r.Name == src.Name) return r;
            foreach (var r in col.Rows)
                if (r.Element.style.display != DisplayStyle.None && r.VertexCount == src.VertexCount) return r;
            return null;
        }

        /// <summary>相手の列で、指の下にある行（見えている範囲のものだけ）。</summary>
        private void UpdateHover()
        {
            var other = OtherOf(_pressSide.Value);
            RowInfo hit = null;
            var vb = other.Scroll.contentViewport.worldBound;
            bool inView = vb.Contains(_lastPos);
            if (inView)
                foreach (var r in other.Rows)
                {
                    if (r.Element.style.display == DisplayStyle.None) continue;
                    if (r.Element.worldBound.Contains(_lastPos)) { hit = r; break; }
                }
            _hoverColumnEmpty = inView && hit == null;
            if (hit == _hoverRow) return;
            if (_hoverRow != null) SetRowBack(_hoverRow, RowColor);
            _hoverRow = hit;
            if (_hoverRow != null) SetRowBack(_hoverRow, HoverColor);
        }

        private void AutoScrollTick()
        {
            if (!_dragging || _pressSide == null) return;
            var other = OtherOf(_pressSide.Value);
            var vb = other.Scroll.contentViewport.worldBound;
            if (_lastPos.x < vb.xMin || _lastPos.x > vb.xMax) return;
            float dy = 0f;
            if (_lastPos.y < vb.yMin + AutoScrollEdge) dy = -AutoScrollStep;
            else if (_lastPos.y > vb.yMax - AutoScrollEdge) dy = AutoScrollStep;
            if (dy == 0f) return;
            var so = other.Scroll.scrollOffset;
            other.Scroll.scrollOffset = new Vector2(so.x, Mathf.Max(0f, so.y + dy));
            UpdateHover();
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            if (_pressSide == null) return;
            _lastPos = e.position;
            if (_dragging)
            {
                UpdateHover();
                var side = _pressSide.Value;
                var src  = _pressRow;
                if (_hoverRow != null) Execute(side, src.MasterIndex, _hoverRow.MasterIndex);
                else if (side == Side.A && _hoverColumnEmpty) Execute(side, src.MasterIndex, -1);
            }
            if (_root.HasPointerCapture(e.pointerId)) _root.ReleasePointer(e.pointerId);
            EndDrag();
        }

        private void EndDrag()
        {
            if (_pressRow != null) SetRowBack(_pressRow, RowColor);
            if (_hoverRow != null) SetRowBack(_hoverRow, RowColor);
            if (_candidateRow != null) SetRowBorder(_candidateRow.Element, RowColor);
            _pressSide = null; _pressRow = null; _hoverRow = null; _candidateRow = null;
            _dragging = false; _hoverColumnEmpty = false;
            if (_ghost != null) _ghost.style.display = DisplayStyle.None;
            _autoScroll?.Pause();
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>side の列の行 srcMi を、相手の列の行 dstMi（-1 は末尾）へ写すコマンドを送る。</summary>
        private void Execute(Side side, int srcMi, int dstMi)
        {
            var srcCol = ColOf(side);
            var dstCol = OtherOf(side);
            if (srcCol.ModelIndex < 0 || dstCol.ModelIndex < 0) { SetStatus("モデルを選んでください"); return; }

            int tid = SelectedTargetId(dstCol);
            ModelVertexTarget target;
            string shapeName = "";
            if (tid == TargetCurrent) target = ModelVertexTarget.CurrentShape;
            else if (tid == ShapeOps.BaseShapeId) target = ModelVertexTarget.BaseShape;
            else
            {
                target = ModelVertexTarget.NamedShape;
                shapeName = tid == TargetNew
                    ? (dstCol.NewNameField.value ?? "").Trim()
                    : FindShapeName(GetProject()?.GetModelView(dstCol.ModelIndex), tid) ?? "";
                if (string.IsNullOrEmpty(shapeName)) { SetStatus("新しい形の名前を入れてください"); return; }
            }

            bool toB = side == Side.A;
            var mode = _matchModeDrop.index == 1 ? VertexMatchMode.VertexId : VertexMatchMode.Index;
            SendCommand?.Invoke(new ImportModelVerticesCommand(
                srcCol.ModelIndex, dstCol.ModelIndex, new[] { srcMi }, new[] { dstMi },
                target, shapeName, mode, _keepOffsetsToggle.value, appendIfMismatch: toB));

            if (tid == TargetNew) { dstCol.PendingShapeName = shapeName; dstCol.NewNameField.SetValueWithoutNotify(""); }
            SetStatus(GetLastSummary?.Invoke() ?? "");
            Refresh();
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
    }
}
