// PlayerPrimitiveMeshSubPanel.PointDefined.cs
// 図形生成サブパネル：点指定図形（高度な図形）。
//
// 【形の決まり方】
//   3D ビューポートで点を順番に指定し（線分 2 / 三角 3 / 四角 4）、
//   右ペインで分割数・奥行き・断面画数などを決める。点の指定・プレビューの組み立て・
//   実生成は PointDefinedToolHandler が行う。プレビューと実生成は同じ計画を通る。
//
// 【置き方】
//   書き込み先は常に編集対象。追加先・姿勢は使わない。材質はパネルの指定を使う。
//   プレビュー頂点はワールド座標（ライブワイヤの行列は単位行列）。
//
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        /// <summary>図形を選び直したとき。Viewer が 3D 操作モードを切り替える。</summary>
        public Action<ShapeKind> OnShapeSelected;

        /// <summary>点指定図形の種類・パラメータが変わったとき。</summary>
        public Action<PointPrimitiveMode, PointDefinedParams> OnPointDefinedRequestChanged;

        /// <summary>プレビュー用メッシュ（ワールド座標）を組む。組めないときは null。</summary>
        public Func<MeshObject> BuildPointDefinedPreviewMesh;

        /// <summary>直近のプレビュー生成の結果。</summary>
        public Func<PointDefinedStatus> GetPointDefinedStatus;

        /// <summary>指定済みの点の数。</summary>
        public Func<int> GetPointDefinedPlacedCount;

        public Action PointDefinedClearPoints;
        public Action PointDefinedRemoveLastPoint;

        public Func<bool>   GetPointDefinedSnapUnselected;
        public Action<bool> SetPointDefinedSnapUnselected;

        /// <summary>引数は材質番号。組めないときは cmd が null で reason に理由。</summary>
        public Func<int, (CreatePointDefinedPrimitiveCommand cmd, string reason)> BuildPointDefinedCommand;

        // ================================================================
        // 状態
        // ================================================================

        private PointPrimitiveMode _pdMode = PointPrimitiveMode.Quad;
        private PointDefinedParams _pdP    = PointDefinedParams.Default;

        // 点指定のボタンは選んだ形で数が変わるので、諸元と同じく作り直す行に置く。
        [UiControl(Ignore = true)]
        private readonly Button[] _pdModeBtns = new Button[3];
        [UiControl(Ignore = true)]
        private readonly Button[] _pdApexBtns = new Button[3];
        [UiControl("pointDefined.points", Safety = UiSafety.ReadOnly, Description = "点指定の点の情報")]
        private Label _pdPointsLabel;
        [UiControl("pointDefined.share", Safety = UiSafety.ReadOnly, Description = "点指定の共有の情報")]
        private Label _pdShareLabel;
        [UiControl("pointDefined.reason", Safety = UiSafety.ReadOnly, Description = "点指定で作れないときの理由")]
        private Label _pdReasonLabel;

        private static readonly Color PdSelectedColor = new Color(0.25f, 0.45f, 0.65f);
        private static readonly Color PdIdleColor     = new Color(0.25f, 0.25f, 0.25f);
        private static readonly Color PdShareColor    = new Color(0.45f, 1.00f, 0.50f);
        private static readonly Color PdWarnColor     = new Color(1.00f, 0.75f, 0.35f);

        /// <summary>このパネルが表示中で、点指定図形を選んでいるか。</summary>
        public bool PointDefinedActive => IsSectionVisible() && _current == ShapeKind.PointDefined;

        // ================================================================
        // UI
        // ================================================================

        private void BuildPointDefinedUI(VisualElement c)
        {
            if (c == null) return;

            c.Add(ShapeTitle(T("PointDefined")));
            c.Add(GearHint(T("PointDefinedHint")));

            // ── モード ──
            var modeRow = PdRow();
            _pdModeBtns[0] = PdButton(modeRow, T("PointDefinedModeLine"),     () => SetPointDefinedMode(PointPrimitiveMode.Line));
            _pdModeBtns[1] = PdButton(modeRow, T("PointDefinedModeTriangle"), () => SetPointDefinedMode(PointPrimitiveMode.Triangle));
            _pdModeBtns[2] = PdButton(modeRow, T("PointDefinedModeQuad"),     () => SetPointDefinedMode(PointPrimitiveMode.Quad));
            c.Add(modeRow);

            // ── 点 ──
            _pdPointsLabel = SL("");
            c.Add(_pdPointsLabel);

            var ptRow = PdRow();
            SB(ptRow, T("PointDefinedUndoPoint"), () => PointDefinedRemoveLastPoint?.Invoke());
            SB(ptRow, T("PointDefinedClear"),     () => PointDefinedClearPoints?.Invoke());
            c.Add(ptRow);

            var snap = new Toggle(T("PointDefinedSnapUnselected"))
            {
                value = GetPointDefinedSnapUnselected?.Invoke() ?? false,
            };
            snap.style.marginBottom = 2;
            snap.RegisterValueChangedCallback(e => SetPointDefinedSnapUnselected?.Invoke(e.newValue));
            c.Add(snap);

            // ── 図形ごとの諸元 ──
            for (int i = 0; i < _pdApexBtns.Length; i++) _pdApexBtns[i] = null;

            switch (_pdMode)
            {
                case PointPrimitiveMode.Line:
                    c.Add(SR(T("PointDefinedRadius"),
                        PointDefinedParams.RadiusMin, PointDefinedParams.RadiusMax,
                        () => _pdP.Radius, v => { _pdP.Radius = v; PdChanged(); }));
                    c.Add(IR(T("PointDefinedSides"),
                        PointDefinedParams.SidesMin, PointDefinedParams.SidesMax,
                        () => _pdP.Sides, v => { _pdP.Sides = v; PdChanged(); }));
                    c.Add(IR(T("PointDefinedLengthSeg"),
                        PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax,
                        () => _pdP.LengthSegments, v => { _pdP.LengthSegments = v; PdChanged(); }));
                    c.Add(TR(T("PointDefinedCap"), () => _pdP.Cap, v => { _pdP.Cap = v; PdChanged(); }));
                    break;

                case PointPrimitiveMode.Triangle:
                {
                    c.Add(SL(T("PointDefinedApex")));
                    var apexRow = PdRow();
                    for (int i = 0; i < 3; i++)
                    {
                        int idx = i;
                        _pdApexBtns[i] = PdButton(apexRow, $"P{i}", () => SetPointDefinedApex(idx));
                    }
                    c.Add(apexRow);

                    c.Add(IR(T("PointDefinedBaseSeg"),
                        PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax,
                        () => _pdP.BaseSegments, v => { _pdP.BaseSegments = v; PdChanged(); }));
                    c.Add(IR(T("PointDefinedHeightSeg"),
                        PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax,
                        () => _pdP.HeightSegments, v => { _pdP.HeightSegments = v; PdChanged(); }));
                    AddPointDefinedDepthRows(c);
                    break;
                }

                default:
                    c.Add(IR(T("PointDefinedUSeg"),
                        PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax,
                        () => _pdP.USegments, v => { _pdP.USegments = v; PdChanged(); }));
                    c.Add(IR(T("PointDefinedVSeg"),
                        PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax,
                        () => _pdP.VSegments, v => { _pdP.VSegments = v; PdChanged(); }));
                    AddPointDefinedDepthRows(c);
                    break;
            }

            // ── 状態表示 ──
            _pdShareLabel = GearHint(string.Empty);
            c.Add(_pdShareLabel);
            _pdReasonLabel = GearHint(string.Empty);
            c.Add(_pdReasonLabel);

            RefreshPointDefinedInfo();
            PushPointDefinedRequest();
        }

        private void AddPointDefinedDepthRows(VisualElement c)
        {
            c.Add(SR(T("PointDefinedDepth"),
                PointDefinedParams.DepthMin, PointDefinedParams.DepthMax,
                () => _pdP.Depth, v => { _pdP.Depth = v; PdChanged(); }));
            c.Add(IR(T("PointDefinedDepthSeg"),
                PointDefinedParams.DepthSegmentsMin, PointDefinedParams.DepthSegmentsMax,
                () => _pdP.DepthSegments, v => { _pdP.DepthSegments = v; PdChanged(); }));
        }

        private static VisualElement PdRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            return row;
        }

        private static Button PdButton(VisualElement parent, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow    = 1;
            b.style.height      = 22;
            b.style.fontSize    = 10;
            b.style.marginRight = 2;
            parent.Add(b);
            return b;
        }

        /// <summary>モード・頂点ボタンの選択中の強調。暗色テーマの適用後に呼ぶ。</summary>
        private void RefreshPointDefinedButtons()
        {
            for (int i = 0; i < _pdModeBtns.Length; i++)
            {
                if (_pdModeBtns[i] == null) continue;
                _pdModeBtns[i].style.backgroundColor = new StyleColor((int)_pdMode == i ? PdSelectedColor : PdIdleColor);
            }
            for (int i = 0; i < _pdApexBtns.Length; i++)
            {
                if (_pdApexBtns[i] == null) continue;
                _pdApexBtns[i].style.backgroundColor = new StyleColor(_pdP.ApexIndex == i ? PdSelectedColor : PdIdleColor);
            }
        }

        private void SetPointDefinedMode(PointPrimitiveMode mode)
        {
            if (_pdMode == mode) return;
            _pdMode = mode;
            // 諸元の行が図形ごとに違うので組み直す。組み直しの中で新しい種類を送る
            // （ハンドラは種類が変わると点をクリアする）。
            RebuildSettings();
            _dirty = true;
        }

        private void SetPointDefinedApex(int index)
        {
            _pdP.ApexIndex = Mathf.Clamp(index, PointDefinedParams.ApexIndexMin, PointDefinedParams.ApexIndexMax);
            RefreshPointDefinedButtons();
            PdChanged();
        }

        private void PdChanged()
        {
            D();
            PushPointDefinedRequest();
        }

        private void PushPointDefinedRequest()
            => OnPointDefinedRequestChanged?.Invoke(_pdMode, _pdP);

        // ================================================================
        // 状態表示
        // ================================================================

        /// <summary>点が変わった・カメラの向きが変わった。プレビューを作り直す。</summary>
        public void NotifyPointDefinedChanged()
        {
            if (_current != ShapeKind.PointDefined) return;
            _dirty = true;
            RefreshPointDefinedInfo();
            RefreshCreateButtonState();
        }

        private void RefreshPointDefinedInfo()
        {
            if (_current != ShapeKind.PointDefined) return;

            int required = PointDefinedMeshBuilder.RequiredPoints(_pdMode);
            int placed   = GetPointDefinedPlacedCount?.Invoke() ?? 0;
            if (_pdPointsLabel != null)
                _pdPointsLabel.text = T("PointDefinedPoints", placed, required);

            // 直近のプレビューが今の点数で作られたものでなければ表示しない。
            var st = GetPointDefinedStatus?.Invoke();
            bool fresh = st != null && st.Placed == placed && st.Required == required;

            if (_pdShareLabel != null)
            {
                var sb = new StringBuilder();
                bool anyShare = false;
                if (fresh && st.Edges != null && st.Edges.Count > 0)
                {
                    sb.AppendLine(T("PointDefinedShareTitle"));
                    foreach (var e in st.Edges)
                    {
                        string edge = $"{T(e.KindKey)} P{e.PointA}-P{e.PointB}";
                        if (e.CanShare)
                        {
                            sb.AppendLine(T("PointDefinedShareOk", edge, e.Subdivisions, e.PathEdges));
                            anyShare = true;
                        }
                        else if (e.PathEdges >= 0)
                            sb.AppendLine(T("PointDefinedShareMismatch", edge, e.Subdivisions, e.PathEdges));
                        else
                            sb.AppendLine(T("PointDefinedShareNone", edge));
                    }
                }
                _pdShareLabel.text = sb.ToString().TrimEnd();
                _pdShareLabel.style.color = new StyleColor(anyShare ? PdShareColor : Color.white);
            }

            if (_pdReasonLabel != null)
            {
                _pdReasonLabel.text = fresh && !string.IsNullOrEmpty(st.Reason) ? st.Reason : "";
                _pdReasonLabel.style.color = new StyleColor(PdWarnColor);
            }
        }

        /// <summary>点が揃い、直近のプレビューが今の点数で組めていること。</summary>
        private bool PointDefinedReady
        {
            get
            {
                if (SendCommand == null || BuildPointDefinedCommand == null) return false;
                int required = PointDefinedMeshBuilder.RequiredPoints(_pdMode);
                int placed   = GetPointDefinedPlacedCount?.Invoke() ?? 0;
                if (placed < required) return false;
                var st = GetPointDefinedStatus?.Invoke();
                return st != null && st.Valid && st.Placed == placed && st.Required == required;
            }
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>プレビュー・ライブワイヤ用。実生成と同じ計画を通す。</summary>
        private MeshObject GeneratePointDefinedMesh()
        {
            var mo = BuildPointDefinedPreviewMesh?.Invoke();
            RefreshPointDefinedInfo();
            RefreshCreateButtonState();
            return mo;
        }

        /// <summary>生成ボタンから呼ぶ。カレントカメラの視線をコマンドに載せて送る。</summary>
        private void InvokePointDefinedGenerate()
        {
            if (SendCommand == null)
            {
                if (_statusLabel != null) _statusLabel.text = "配線が足りません（SendCommand）";
                return;
            }
            if (BuildPointDefinedCommand == null)
            {
                if (_statusLabel != null) _statusLabel.text = "配線が足りません（BuildPointDefinedCommand）";
                return;
            }

            var (cmd, reason) = BuildPointDefinedCommand(Mathf.Max(0, _materialIndex));
            if (cmd == null)
            {
                if (_statusLabel != null) _statusLabel.text = reason ?? "生成失敗";
                return;
            }

            SendCommand(cmd);
            if (_statusLabel != null) _statusLabel.text = T("Create");
        }
    }
}
