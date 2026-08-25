// PlayerPrimitiveMeshSubPanel.BeltProfile.cs
// 図形生成サブパネル：基準ベルト（梯子状の四角形群）と断面プロファイル編集の共通部。
// フリル／パイプが各自の状態インスタンスを持って共用する。
// 編集機能は回転体プロファイルエディタと同等（複数選択・マーキー・線分挿入・マグネット・
// 選択の変換・アンカー・下絵・Undo）。回転体側のコードは変更していない。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>選択中の描画オブジェクトの選択面インデックスを返す（なければ null）。基準ベルトの取り込みで使用。</summary>
        public Func<IReadOnlyCollection<int>> GetSelectedFaceIndices;

        /// <summary>モデル内の描画オブジェクト一覧（表示名, MeshObject）を返す。自動検索の対象選択に使用。</summary>
        public Func<List<(string Label, MeshObject Mesh)>> GetDrawableMeshList;

        /// <summary>
        /// モデル内の描画オブジェクト一覧（表示名, MasterIndex, MeshObject）を返す。
        /// 配置の配置元で使う。子孫の解決に MasterIndex が要るため、上とは別に持つ。
        /// </summary>
        public Func<List<(string Label, int MasterIndex, MeshObject Mesh)>> GetDrawableMeshEntryList;

        /// <summary>
        /// 指定した MasterIndex のオブジェクトと、その子孫を (MasterIndex, MeshObject) で列挙する。
        /// リスト順。面を持たないもの（グループ用の空オブジェクト等）は含めない。
        /// 各メッシュは自分のローカル座標のまま返す（一覧で直接チェックしたときと同じ扱い）。
        /// </summary>
        public Func<int, List<(int MasterIndex, MeshObject Mesh)>> GetSubtreeMeshList;

        // ================================================================
        // データ
        // ================================================================

        /// <summary>
        /// 取り込んだ基準ベルトのスナップショット。rung 順の左右レール位置（元メッシュのローカル座標）。
        /// 頂点インデックスではなく座標を保持するため、元メッシュの編集で古くならない。
        /// </summary>
        private sealed class BeltSnapshot
        {
            public List<Vector3> Left;
            public List<Vector3> Right;
            public bool          Closed;
            public bool          FlipWinding;

            /// <summary>
            /// フリルの高さ倍率（断面プロファイル Y ＝ 法線方向成分に掛ける）。既定 1。
            /// フリル以外（パイプ・配置）では読み書きするだけで使わない。
            /// </summary>
            public float HeightScale = 1f;

            /// <summary>自動検索で得た先端（rung には含めない）。手動取り込み時は null。</summary>
            public Vector3? StartPoint;
            public Vector3? EndPoint;

            /// <summary>
            /// 上下につながった段グループの識別子。-1 は未設定（単独の梯子として扱う）。
            /// フリルの2プロファイル補間で使う。
            /// </summary>
            public int GroupId = -1;

            /// <summary>グループ内の段番号（0 が t=0 側）。</summary>
            public int RowIndex;

            /// <summary>グループの段数。</summary>
            public int RowCount = 1;

            public int  RungCount => Left?.Count ?? 0;
            public bool HasData   => Left != null && Right != null
                                     && Left.Count >= 2 && Left.Count == Right.Count;
        }

        /// <summary>上下方向への探索オプション。</summary>
        private sealed class BeltStackOption
        {
            /// <summary>見つけた梯子から上下（左右レール側）へ横断して段を足す。</summary>
            public bool Enabled = true;
        }

        /// <summary>スプライン分割の設定。</summary>
        /// <summary>梯子の向き補正オプション。</summary>
        private sealed class BeltOrientOption
        {
            public bool SwapSides;
            public bool ReverseOrder;

            public bool IsIdentity => !SwapSides && !ReverseOrder;
        }

        private sealed class BeltSplineOption
        {
            public bool Enabled;
            public int  Segments  = 1;   // 段間の補間数（原型の numberOfEachSegment）
            public bool UseFirst  = true;
            public bool UseLast;
            public int  TrimStart;
            public int  TrimEnd;
        }

        /// <summary>断面プロファイルの編集状態（キャンバス1面ぶん）。</summary>
        private sealed class BeltProfileEdit
        {
            // ── 生成側から与える設定 ──
            public Func<List<Vector2>> DefaultProfile;
            public string UndoStackId = "PlayerEdit/BeltProfileEdit";
            public string UndoTitle   = "断面編集";
            public string BgSectionLabel = "下絵";

            /// <summary>断面プロファイルCSVのパス。RecentPaths のキーと既定ファイル名も生成側から与える。</summary>
            public string CsvPath       = "";
            public string CsvRecentKey  = "Primitive.BeltProfile.Csv";
            public string CsvDefaultName = "profile.csv";

            /// <summary>終点と始点をつないだ閉じた断面として扱うか。</summary>
            public bool ClosedLoop;

            // ── 編集データ ──
            public List<Vector2> Points        = new List<Vector2>();

            /// <summary>参考表示するだけのプロファイル（A/B のもう一方）。null なら描かない。</summary>
            public List<Vector2> GhostPoints;

            public int           SelectedIndex = -1;
            public readonly HashSet<int> Sel   = new HashSet<int>();

            // ── ビュー ──
            public float   Zoom = 1f;
            public Vector2 Offset;

            public VisualElement Canvas;
            public VisualElement ViewLayer;
            public VisualElement BgEl;

            // ── 選択点UI ──
            public VisualElement PtRow;
            public Label         PtLabel;
            public Slider        PtXSlider;
            public FloatField    PtXField;
            public Slider        PtYSlider;
            public FloatField    PtYField;

            // ── 点ドラッグ ──
            public bool    Drag;
            public int     HoverEI = -1;
            public readonly Dictionary<int, Vector2> DragStart = new Dictionary<int, Vector2>();
            public Vector2 DragStartCursorProf;

            // ── パン ──
            public bool    PanDrag;
            public Vector2 PanStart;
            public Vector2 PanOffsetStart;

            // ── マーキー ──
            public readonly Canvas2DMarquee Marquee = new Canvas2DMarquee();
            public bool MarqueeDrag;
            public bool MarqueeAdditive;
            public bool LassoMode;

            // ── マグネット ──
            public readonly Canvas2DMagnet Magnet = new Canvas2DMagnet();
            public readonly Dictionary<int, Vector2> MagnetStart = new Dictionary<int, Vector2>();
            public readonly Dictionary<int, float>   MagnetW     = new Dictionary<int, float>();

            // ── アンカー／ハンドル ──
            public readonly Canvas2DAnchor Anchor = new Canvas2DAnchor();
            public readonly Canvas2DHandle Handle = new Canvas2DHandle();
            // ギズモ表示トグル（既定=非表示、メモリ保持・非永続）
            public bool          ShowGizmo;
            public bool          AnchorDrag;
            public bool          AnchorSuppress;
            public Button        AnchorEnterBtn;
            public VisualElement AnchorPanel;
            public Slider        AnchorXSlider, AnchorYSlider;
            public FloatField    AnchorXField,  AnchorYField;

            public bool                     HandleDrag;
            public Canvas2DHandle.HandleType HandleType = Canvas2DHandle.HandleType.None;
            public Vector2 HandleAnchorC;
            public float   HandlePrevAngle;
            public float   HandleTotalDeg;
            public readonly Dictionary<int, Vector2> HandleStart = new Dictionary<int, Vector2>();
            public readonly Dictionary<int, float>   HandleW     = new Dictionary<int, float>();

            // ── 変換UI ──
            public FloatField TfMoveX, TfMoveY, TfScaleX, TfScaleY, TfScaleAxis, TfRot;

            // ── 下絵 ──
            public string    BgPath;
            public Texture2D BgTex;
            public float     BgAlpha = 0.5f;
            public float     BgScale = 3f;
            public bool      BgMode;
            public Vector2   BgOffset;
            public Vector2   BgOrigin;
            public bool      BgDrag;
            public Vector2   BgDragStart;
            public Vector2   BgOffsetOnDragStart;
            public Slider    BgScaleSlider;
            public Label     BgSizeLabel;

            // ── Undo ──
            public UndoStack<BeltProfileUndoContext> UndoStack;
            public BeltProfileUndoContext            UndoCtx;
            public List<Vector2>                     EditBefore;
            public bool                              UndoApplying;
        }

        private sealed class BeltProfileUndoContext { public List<Vector2> Profile; }

        private sealed class BeltProfileUndoRecord : IUndoRecord<BeltProfileUndoContext>
        {
            public UndoOperationInfo Info { get; set; }
            public List<Vector2> Before;
            public List<Vector2> After;
            public void Undo(BeltProfileUndoContext ctx) => ctx.Profile = CloneBeltProfile(Before);
            public void Redo(BeltProfileUndoContext ctx) => ctx.Profile = CloneBeltProfile(After);
        }

        private static List<Vector2> CloneBeltProfile(List<Vector2> src)
            => src == null ? null : new List<Vector2>(src);

        private static bool BeltProfileEquals(List<Vector2> a, List<Vector2> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if ((a[i] - b[i]).sqrMagnitude > 1e-12f) return false;
            return true;
        }

        // ================================================================
        // 取り込み（選択四角形 → 基準ベルト）
        // ================================================================

        /// <summary>
        /// 選択中の描画オブジェクトの選択四角形から、順序付きの梯子状ベルトを取り込む。
        /// crossRows = true なら、そこから上下へ横断して段グループにまとめる（選択範囲外へも進む）。
        /// </summary>
        private void ImportBeltFromMesh(List<BeltSnapshot> dst, bool crossRows)
        {
            if (dst == null) return;

            var mesh = GetSelectedMeshObject?.Invoke();
            if (mesh == null) { SetBeltStatus(T("NoSelectedMesh")); return; }

            var sel = GetSelectedFaceIndices?.Invoke();
            if (sel == null || sel.Count == 0) { SetBeltStatus(T("NoSelectedFaces")); return; }

            var strip = BeltStripExtractor.Extract(mesh, sel);
            if (!strip.Ok) { SetBeltStatus(strip.Message); return; }

            var baseRow = new BeltAutoStrip
            {
                Closed      = strip.Closed,
                FlipWinding = strip.FlipWinding,
            };
            baseRow.Left .AddRange(strip.Left);
            baseRow.Right.AddRange(strip.Right);
            baseRow.Faces.AddRange(strip.Faces);

            var bases = new List<BeltAutoStrip>(1) { baseRow };
            var rows  = BeltStackExpander.ExpandAll(mesh, bases, crossRows, out _);

            dst.Clear();
            foreach (var st in rows) dst.Add(ToBeltSnapshot(mesh, st));

            SetBeltStatus(rows.Count > 1 ? $"{strip.Message} / 段 {rows.Count}" : strip.Message);
            D();
        }

        /// <summary>指定オブジェクト全体から梯子を自動検出して差し替える。</summary>
        private void AutoDetectBelts(List<BeltSnapshot> dst, MeshObject mesh, bool crossRows)
        {
            if (dst == null) return;
            if (mesh == null) { SetBeltStatus(T("NoSourceObject")); return; }

            var strips = BeltStackDetector.Detect(mesh, crossRows, out string message);

            dst.Clear();
            foreach (var st in strips) dst.Add(ToBeltSnapshot(mesh, st));

            SetBeltStatus(message);
            D();
        }

        /// <summary>指定オブジェクト全体から円環状の梯子を検出して差し替える。</summary>
        private void AutoDetectRings(List<BeltSnapshot> dst, MeshObject mesh, bool crossRows)
        {
            if (dst == null) return;
            if (mesh == null) { SetBeltStatus(T("NoSourceObject")); return; }

            var rings = BeltRingDetector.Detect(mesh, out string message);
            var rows  = BeltStackExpander.ExpandAll(mesh, rings, crossRows, out int groupCount);

            dst.Clear();
            foreach (var st in rows) dst.Add(ToBeltSnapshot(mesh, st));

            SetBeltStatus(crossRows
                ? $"{message} → グループ {groupCount} / 段 {rows.Count}"
                : message);
            D();
        }

        /// <summary>検出結果（頂点インデックス）を座標スナップショットへ変換する。</summary>
        private static BeltSnapshot ToBeltSnapshot(MeshObject mesh, BeltAutoStrip st)
        {
            var snap = new BeltSnapshot
            {
                Left        = new List<Vector3>(st.RungCount),
                Right       = new List<Vector3>(st.RungCount),
                Closed      = st.Closed,
                FlipWinding = st.FlipWinding,
                GroupId     = st.GroupId,
                RowIndex    = st.RowIndex,
                RowCount    = st.RowCount,
            };

            for (int i = 0; i < st.RungCount; i++)
            {
                snap.Left .Add(mesh.Vertices[st.Left[i]].Position);
                snap.Right.Add(mesh.Vertices[st.Right[i]].Position);
            }

            if (st.StartPoint >= 0) snap.StartPoint = mesh.Vertices[st.StartPoint].Position;
            if (st.EndPoint   >= 0) snap.EndPoint   = mesh.Vertices[st.EndPoint].Position;
            return snap;
        }

        private string BeltsInfoText(List<BeltSnapshot> belts)
        {
            if (belts == null || belts.Count == 0) return T("FrillNoBase");

            int total = 0;
            var groups = new HashSet<int>();
            foreach (var b in belts)
            {
                total += b.RungCount;
                groups.Add(b.GroupId);
            }

            return belts.Count > groups.Count
                ? T("BeltsInfoG", belts.Count, groups.Count, total)
                : T("BeltsInfo", belts.Count, total);
        }

        private void SetBeltStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text;
        }

        // ================================================================
        // 梯子CSV（フリル／パイプ／配置で共用）
        // ================================================================

        /// <summary>BeltSnapshot → CSV用DTO。</summary>
        private static List<BeltCsvEntry> BeltsToCsv(List<BeltSnapshot> belts)
        {
            var list = new List<BeltCsvEntry>();
            if (belts == null) return list;

            foreach (var b in belts)
            {
                if (b == null || !b.HasData) continue;
                list.Add(new BeltCsvEntry
                {
                    Left        = new List<Vector3>(b.Left),
                    Right       = new List<Vector3>(b.Right),
                    Closed      = b.Closed,
                    FlipWinding = b.FlipWinding,
                    HeightScale = b.HeightScale,
                    StartPoint  = b.StartPoint,
                    EndPoint    = b.EndPoint,
                    GroupId     = b.GroupId,
                    RowIndex    = b.RowIndex,
                    RowCount    = b.RowCount,
                });
            }
            return list;
        }

        /// <summary>CSV用DTO → BeltSnapshot。</summary>
        private static List<BeltSnapshot> BeltsFromCsv(List<BeltCsvEntry> entries)
        {
            var list = new List<BeltSnapshot>();
            if (entries == null) return list;

            foreach (var e in entries)
            {
                if (e == null || !e.HasData) continue;

                // $group が無い旧CSVは、梯子ごとに独立した1段グループとして扱う。
                int gid = e.GroupId >= 0 ? e.GroupId : list.Count;
                int cnt = Mathf.Max(1, e.RowCount);
                int row = Mathf.Clamp(e.RowIndex, 0, cnt - 1);

                list.Add(new BeltSnapshot
                {
                    Left        = new List<Vector3>(e.Left),
                    Right       = new List<Vector3>(e.Right),
                    Closed      = e.Closed,
                    FlipWinding = e.FlipWinding,
                    HeightScale = e.HeightScale,
                    StartPoint  = e.StartPoint,
                    EndPoint    = e.EndPoint,
                    GroupId     = gid,
                    RowIndex    = row,
                    RowCount    = cnt,
                });
            }
            return list;
        }

        /// <summary>
        /// 梯子CSVの読み書きUIを組み立てる。読込は梯子リストを全置換する。
        /// </summary>
        private void BuildBeltCsvUI(VisualElement c, List<BeltSnapshot> belts,
                                    string recentKey, string defaultName, Action onChanged)
        {
            if (c == null || belts == null) return;

            c.Add(PlayerIoUiKit.SectionLabel(T("BeltCsv")));

            string path = RecentPaths.Get(recentKey);

            var pathField = new TextField();
            pathField.RegisterValueChangedCallback(e =>
            {
                path = e.newValue;
                RecentPaths.Set(recentKey, e.newValue);
            });
            // PMX読込と同じ操作感：[...] も「読込」も必ずダイアログを出す。
            // パス欄の値は初期フォルダ／初期ファイル名としてだけ使い、確定後そのまま読込む。
            void LoadBeltCsv()
            {
                string sel = PlayerIoUiKit.AskLoadPath(T("LoadCSV"), path, "csv");
                if (string.IsNullOrEmpty(sel)) return;
                pathField.value = sel;
                path = sel;

                var result = BeltCsvIO.Load(path);
                if (!result.Success) { SetBeltStatus(result.ErrorMessage); return; }

                var loaded = BeltsFromCsv(result.Belts);
                belts.Clear();
                belts.AddRange(loaded);

                int total = 0;
                foreach (var b in belts) total += b.RungCount;
                SetBeltStatus(T("BeltsInfo", belts.Count, total));

                onChanged?.Invoke();
                D();
            }

            c.Add(PlayerIoUiKit.PathRow(pathField, LoadBeltCsv));
            if (!string.IsNullOrEmpty(path)) pathField.SetValueWithoutNotify(path);

            c.Add(PlayerIoUiKit.WideBtn(T("LoadCSV"), LoadBeltCsv));

            c.Add(PlayerIoUiKit.WideBtn(T("SaveCSV"), () =>
            {
                if (belts.Count == 0) { SetBeltStatus(T("FrillNoBase")); return; }

                // パス欄は読込用。保存は毎回ダイアログを出し、パス欄の値は初期値としてだけ使う。
                string save = PlayerIoUiKit.AskSavePath(T("SaveCSV"), path, defaultName, "csv");
                if (string.IsNullOrEmpty(save)) return;
                path = save;
                pathField.value = path;

                if (BeltCsvIO.Save(path, BeltsToCsv(belts)))
                {
                    int total = 0;
                    foreach (var b in belts) total += b.RungCount;
                    SetBeltStatus(T("BeltsInfo", belts.Count, total));
                }
            }));
        }

        // ================================================================
        // 断面プロファイルエディタ
        // ================================================================

        private static void EnsureBeltProfile(BeltProfileEdit ed)
        {
            if (ed == null) return;
            if (ed.Points == null || ed.Points.Count < 2)
                ed.Points = ed.DefaultProfile != null
                    ? ed.DefaultProfile()
                    : new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f) };
        }

        /// <summary>
        /// 断面プロファイルCSVの読み書きUIを組み立てる。
        /// $closedLoop は書き出すのみで、読込時に ed.ClosedLoop へは反映しない
        /// （フリル=開ループ／パイプ=閉ループが生成器側の前提のため）。
        /// </summary>
        private void BuildBeltProfileCsvUI(VisualElement pe, BeltProfileEdit ed)
        {
            if (pe == null || ed == null) return;

            // 3エディタ共通：折り畳みセクションにする（既定 閉）。
            pe = FoldSection(pe, T("ProfileCsvSection"), false);

            if (string.IsNullOrEmpty(ed.CsvPath)) ed.CsvPath = RecentPaths.Get(ed.CsvRecentKey);

            var pathField = new TextField();
            pathField.RegisterValueChangedCallback(e =>
            {
                ed.CsvPath = e.newValue;
                RecentPaths.Set(ed.CsvRecentKey, e.newValue);
            });
            // PMX読込と同じ操作感：[...] も「読込」も必ずダイアログを出す。
            void LoadProfileCsv()
            {
                string sel = PlayerIoUiKit.AskLoadPath(T("LoadCSV"), ed.CsvPath, "csv");
                if (string.IsNullOrEmpty(sel)) return;
                pathField.value = sel;
                ed.CsvPath = sel;

                var result = ProfilePointsCsvIO.Load(ed.CsvPath, ed.ClosedLoop);
                if (!result.Success) { SetBeltStatus(result.ErrorMessage); return; }

                BeltBegin(ed);
                ed.Points = result.Points;
                ed.Sel.Clear(); ed.SelectedIndex = -1;
                BeltCommit(ed, "CSV読込");

                SetBeltStatus(T("ImportedPoints", ed.Points.Count));
                D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
            }

            pe.Add(PlayerIoUiKit.PathRow(pathField, LoadProfileCsv));
            if (!string.IsNullOrEmpty(ed.CsvPath)) pathField.SetValueWithoutNotify(ed.CsvPath);

            pe.Add(PlayerIoUiKit.WideBtn(T("LoadCSV"), LoadProfileCsv));

            pe.Add(PlayerIoUiKit.WideBtn(T("SaveCSV"), () =>
            {
                EnsureBeltProfile(ed);

                // パス欄は読込用。保存は毎回ダイアログを出し、パス欄の値は初期値としてだけ使う。
                string save = PlayerIoUiKit.AskSavePath(
                    T("SaveCSV"), ed.CsvPath, ed.CsvDefaultName, "csv");
                if (string.IsNullOrEmpty(save)) return;
                ed.CsvPath = save;
                pathField.value = ed.CsvPath;

                if (ProfilePointsCsvIO.Save(ed.CsvPath, ed.Points, ed.ClosedLoop))
                    SetBeltStatus(T("ImportedPoints", ed.Points.Count));
            }));
        }

        /// <summary>断面プロファイルエディタを組み立てる。</summary>
        private void BuildBeltProfileEditor(VisualElement pe, BeltProfileEdit ed, string axisHint)
        {
            if (pe == null || ed == null) return;

            EnsureBeltProfile(ed);

            // ドラッグ状態をリセット（タブ切替後の再Build時）
            ed.Drag = false; ed.HoverEI = -1; ed.PanDrag = false;
            ed.MarqueeDrag = false; ed.HandleDrag = false; ed.AnchorDrag = false; ed.BgDrag = false;

            pe.Add(SL(T("ProfileEditor")));

            var axisLabel = new Label(axisHint);
            axisLabel.style.fontSize     = 10;
            axisLabel.style.whiteSpace   = WhiteSpace.Normal;
            axisLabel.style.marginBottom = 2;
            pe.Add(axisLabel);

            var canvas = new VisualElement();
            canvas.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            canvas.style.height          = _profileHeight;
            canvas.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.15f));
            canvas.style.marginBottom    = 4;
            canvas.style.borderTopWidth   = canvas.style.borderBottomWidth =
            canvas.style.borderLeftWidth  = canvas.style.borderRightWidth  = 1;
            canvas.style.borderTopColor   = canvas.style.borderBottomColor =
            canvas.style.borderLeftColor  = canvas.style.borderRightColor  =
                new StyleColor(new Color(0.4f, 0.4f, 0.45f));
            canvas.style.overflow        = Overflow.Hidden;
            canvas.pickingMode           = PickingMode.Position;
            ed.Canvas = canvas;

            // 下絵レイヤー（ビューレイヤー配下。断面と同じ view 変換で追従）
            ed.ViewLayer = new VisualElement();
            ed.ViewLayer.style.position = Position.Absolute;
            ed.ViewLayer.style.left = ed.ViewLayer.style.top =
            ed.ViewLayer.style.right = ed.ViewLayer.style.bottom = 0;
            ed.ViewLayer.pickingMode = PickingMode.Ignore;

            ed.BgEl = new VisualElement();
            ed.BgEl.style.position = Position.Absolute;
            ed.BgEl.style.display  = DisplayStyle.None;
            ed.BgEl.pickingMode    = PickingMode.Ignore;
            ed.ViewLayer.Add(ed.BgEl);
            canvas.Add(ed.ViewLayer);

            canvas.generateVisualContent += ctx => DrawBeltProfile(ctx, ed);
            canvas.RegisterCallback<PointerDownEvent>(e => OnBeltProfilePointerDown(e, ed));
            canvas.RegisterCallback<PointerMoveEvent>(e => OnBeltProfilePointerMove(e, ed));
            canvas.RegisterCallback<PointerUpEvent>(e   => OnBeltProfilePointerUp(e, ed));
            canvas.RegisterCallback<WheelEvent>(e =>
            {
                if (ed.BgMode)
                {
                    ed.BgScale = Mathf.Clamp(ed.BgScale * (1f - e.delta.y * 0.05f), 0.1f, 10f);
                    ed.BgScaleSlider?.SetValueWithoutNotify(ed.BgScale);
                    UpdateBeltBgEl(ed); RefreshBeltCanvas(ed);
                }
                else
                {
                    float w = canvas.resolvedStyle.width, h = canvas.resolvedStyle.height;
                    float oldZoom = ed.Zoom;
                    float newZoom = Mathf.Clamp(oldZoom * (1f - e.delta.y * 0.05f), 0.2f, 8f);
                    if (newZoom != oldZoom)
                    {
                        var   m      = (Vector2)e.localMousePosition;
                        var   center = new Vector2(w * 0.5f, h * 0.5f);
                        float k      = newZoom / oldZoom;
                        ed.Offset = (m - center) * (1f - k) + ed.Offset * k;
                        ed.Zoom   = newZoom;
                        UpdateBeltView(ed); UpdateBeltBgEl(ed); RefreshBeltCanvas(ed);
                    }
                }
                e.StopPropagation();
            });
            canvas.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateBeltBgEl(ed); UpdateBeltView(ed); RefreshBeltCanvas(ed);
            });
            pe.Add(canvas);

            AddProfileResizeHandle(pe, canvas, () => RefreshBeltCanvas(ed));

            // ボタン行: 削除 / リセット / ビュー初期化
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 4;
            SB(btnRow, T("DeletePoint"), () =>
            {
                EnsureBeltProfile(ed);
                BeltBegin(ed);
                if (ed.Sel.Count > 0)
                {
                    var idxs = new List<int>(ed.Sel);
                    idxs.Sort(); idxs.Reverse();
                    foreach (var idx in idxs)
                        if (idx >= 0 && idx < ed.Points.Count && ed.Points.Count > 2)
                            ed.Points.RemoveAt(idx);
                    ed.Sel.Clear(); ed.SelectedIndex = -1;
                }
                else
                {
                    int sel = ed.SelectedIndex;
                    RevolutionProfileEditCore.RemovePoint(ed.Points, ref sel);
                    ed.SelectedIndex = sel;
                }
                BeltCommit(ed, "点削除");
                D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
            });
            SB(btnRow, T("ResetProfile"), () =>
            {
                BeltBegin(ed);
                ed.Points = ed.DefaultProfile != null ? ed.DefaultProfile() : ed.Points;
                ed.Sel.Clear(); ed.SelectedIndex = -1;
                BeltCommit(ed, "断面リセット");
                D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
            });
            pe.Add(btnRow);

            // ここから下を1つの大フォールドにまとめ、中の各セクションも個別に折り畳む。
            pe = FoldSection(pe, T("EditTools"), true);

            // ビュー操作行（3エディタ共通の並び：ビュー初期化 / 投げ縄 / ギズモ）
            var viewRow = new VisualElement();
            viewRow.style.flexDirection = FlexDirection.Row;
            viewRow.style.marginBottom  = 3;
            SB(viewRow, T("ResetView"), () =>
            {
                ed.Zoom = 1f; ed.Offset = Vector2.zero;
                UpdateBeltView(ed); UpdateBeltBgEl(ed); RefreshBeltCanvas(ed);
            });
            var lassoToggle = new Toggle(T("LassoMode")) { value = ed.LassoMode };
            lassoToggle.style.marginLeft = 4;
            lassoToggle.RegisterValueChangedCallback(ev => ed.LassoMode = ev.newValue);
            viewRow.Add(lassoToggle);
            var gizmoToggle = new Toggle(T("ShowGizmo")) { value = ed.ShowGizmo };
            gizmoToggle.style.marginLeft = 8;
            gizmoToggle.RegisterValueChangedCallback(ev => { ed.ShowGizmo = ev.newValue; RefreshBeltCanvas(ed); });
            viewRow.Add(gizmoToggle);
            pe.Add(viewRow);

            BuildBeltTransformUI(pe, ed);

            // ── 断面プロファイルCSV ───────────────────────────────────────
            BuildBeltProfileCsvUI(pe, ed);

            // 下絵
            BuildBgSection(pe,
                ed.BgSectionLabel,
                () => ed.BgPath,  v => ed.BgPath  = v,
                () => ed.BgAlpha, v => { ed.BgAlpha = v; UpdateBeltBgEl(ed); },
                () => ed.BgMode,  v => { ed.BgMode  = v; },
                () => ed.BgScale, v => { ed.BgScale = Mathf.Clamp(v, 0.1f, 10f); UpdateBeltBgEl(ed); RefreshBeltCanvas(ed); },
                () => ed.BgOrigin, v => { ed.BgOrigin = v; UpdateBeltBgEl(ed); RefreshBeltCanvas(ed); },
                () => ed.BgTex,
                () =>
                {
                    if (string.IsNullOrEmpty(ed.BgPath)) return;
                    var tex = ed.BgTex;
                    LoadBgTexture(ed.BgPath, ref tex, ed.BgEl);
                    ed.BgTex = tex;
                    ed.BgOffset = Vector2.zero; ed.BgScale = 3f;
                    if (ed.BgTex != null)
                        ed.BgOrigin = new Vector2(ed.BgTex.width * 0.5f, ed.BgTex.height * 0.5f);
                    ed.BgScaleSlider?.SetValueWithoutNotify(1f);
                    SetBgSizeLabel(ed.BgSizeLabel, ed.BgTex);
                    UpdateBeltBgEl(ed);
                },
                () =>
                {
                    ed.BgTex = null;
                    ed.BgEl.style.display = DisplayStyle.None;
                    ed.BgEl.style.backgroundImage = new StyleBackground();
                    SetBgSizeLabel(ed.BgSizeLabel, null);
                },
                out var bgScaleSlider, out var bgSizeLabel);
            ed.BgScaleSlider = bgScaleSlider;
            ed.BgSizeLabel   = bgSizeLabel;

            // 選択点スライダー
            ed.PtRow = new VisualElement(); ed.PtRow.style.marginBottom = 4;
            ed.PtLabel = new Label(""); ed.PtLabel.style.fontSize = 9; ed.PtLabel.style.marginBottom = 1;
            ed.PtRow.Add(ed.PtLabel);
            {
                Slider     xSl = new Slider(-1f, 2f); xSl.style.flexGrow = 1;
                FloatField xFf = new FloatField { value = 0f }; xFf.style.width = 42;
                xSl.RegisterValueChangedCallback(e =>
                {
                    if (ed.SelectedIndex < 0 || ed.Points == null || ed.SelectedIndex >= ed.Points.Count) return;
                    xFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    ed.Points[ed.SelectedIndex] = new Vector2(e.newValue, ed.Points[ed.SelectedIndex].y);
                    D(); RefreshBeltCanvas(ed);
                });
                xSl.RegisterCallback<PointerDownEvent>(_ => BeltBegin(ed));
                xSl.RegisterCallback<PointerUpEvent>(_ => BeltCommit(ed, "点X編集"));
                xFf.RegisterValueChangedCallback(e =>
                {
                    if (ed.SelectedIndex < 0 || ed.Points == null || ed.SelectedIndex >= ed.Points.Count) return;
                    BeltBegin(ed);
                    float v = e.newValue;
                    xSl.SetValueWithoutNotify(Mathf.Clamp(v, -1f, 2f));
                    ed.Points[ed.SelectedIndex] = new Vector2(v, ed.Points[ed.SelectedIndex].y);
                    D(); RefreshBeltCanvas(ed);
                    BeltCommit(ed, "点X編集");
                });
                var xRow = new VisualElement(); xRow.style.flexDirection = FlexDirection.Row; xRow.style.marginBottom = 2;
                xRow.Add(ML("X")); xRow.Add(xSl); xRow.Add(xFf);
                ed.PtRow.Add(xRow);
                ed.PtXSlider = xSl; ed.PtXField = xFf;
            }
            {
                Slider     ySl = new Slider(-1f, 2f); ySl.style.flexGrow = 1;
                FloatField yFf = new FloatField { value = 0f }; yFf.style.width = 42;
                ySl.RegisterValueChangedCallback(e =>
                {
                    if (ed.SelectedIndex < 0 || ed.Points == null || ed.SelectedIndex >= ed.Points.Count) return;
                    yFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    ed.Points[ed.SelectedIndex] = new Vector2(ed.Points[ed.SelectedIndex].x, e.newValue);
                    D(); RefreshBeltCanvas(ed);
                });
                ySl.RegisterCallback<PointerDownEvent>(_ => BeltBegin(ed));
                ySl.RegisterCallback<PointerUpEvent>(_ => BeltCommit(ed, "点Y編集"));
                yFf.RegisterValueChangedCallback(e =>
                {
                    if (ed.SelectedIndex < 0 || ed.Points == null || ed.SelectedIndex >= ed.Points.Count) return;
                    BeltBegin(ed);
                    float v = e.newValue;
                    ySl.SetValueWithoutNotify(Mathf.Clamp(v, -1f, 2f));
                    ed.Points[ed.SelectedIndex] = new Vector2(ed.Points[ed.SelectedIndex].x, v);
                    D(); RefreshBeltCanvas(ed);
                    BeltCommit(ed, "点Y編集");
                });
                var yRow = new VisualElement(); yRow.style.flexDirection = FlexDirection.Row; yRow.style.marginBottom = 2;
                yRow.Add(ML("Y")); yRow.Add(ySl); yRow.Add(yFf);
                ed.PtRow.Add(yRow);
                ed.PtYSlider = ySl; ed.PtYField = yFf;
            }
            ed.PtRow.style.display = DisplayStyle.None;
            pe.Add(ed.PtRow);

            RefreshBeltPointUI(ed);
        }

        // ================================================================
        // 変換／マグネット／アンカーUI
        // ================================================================

        private void BuildBeltTransformUI(VisualElement pe, BeltProfileEdit ed)
        {
            var tfFold = FoldSection(pe, T("SelectionTransform"), false);
            tfFold.Add(BuildTf2("移動 X/Y",     0f, 0f, out ed.TfMoveX,  out ed.TfMoveY));
            tfFold.Add(BuildTf2("スケール X/Y", 1f, 1f, out ed.TfScaleX, out ed.TfScaleY));
            tfFold.Add(BuildTf1("スケール軸 (°)", 0f, out ed.TfScaleAxis));
            tfFold.Add(BuildTf1("回転 (°)",       0f, out ed.TfRot));

            var applyRow = new VisualElement(); applyRow.style.flexDirection = FlexDirection.Row; applyRow.style.marginBottom = 4;
            SB(applyRow, "変換適用", () => ApplyBeltTransform(ed));
            SB(applyRow, "リセット", () =>
            {
                ed.TfMoveX.value = 0f; ed.TfMoveY.value = 0f;
                ed.TfScaleX.value = 1f; ed.TfScaleY.value = 1f;
                ed.TfRot.value = 0f; ed.TfScaleAxis.value = 0f;
            });
            tfFold.Add(applyRow);

            // マグネット
            var magFold = FoldSection(pe, T("Magnet"), false);
            var magRow = new VisualElement(); magRow.style.flexDirection = FlexDirection.Row; magRow.style.marginBottom = 2;
            var magToggle = new Toggle("有効") { value = ed.Magnet.Enabled }; magToggle.style.marginRight = 6;
            magToggle.RegisterValueChangedCallback(ev => { ed.Magnet.Enabled = ev.newValue; RefreshBeltCanvas(ed); });
            var falloff = new EnumField(ed.Magnet.Falloff); falloff.style.flexGrow = 1;
            falloff.RegisterValueChangedCallback(ev => ed.Magnet.Falloff = (FalloffType)ev.newValue);
            magRow.Add(magToggle); magRow.Add(falloff);
            magFold.Add(magRow);
            magFold.Add(BuildAnchorRow("半径", 0.05f, 2f, ed.Magnet.Radius, out _, out _,
                () => false, v => { ed.Magnet.Radius = v; RefreshBeltCanvas(ed); }));

            // アンカー
            var anchorFold = FoldSection(pe, T("AnchorSection"), false);
            ed.AnchorEnterBtn = new Button(() => SetBeltAnchorMode(ed, true)) { text = "アンカー設定" };
            ed.AnchorEnterBtn.style.marginBottom = 2;
            anchorFold.Add(ed.AnchorEnterBtn);

            ed.AnchorPanel = new VisualElement(); ed.AnchorPanel.style.marginBottom = 4;
            {
                var headRow = new VisualElement(); headRow.style.flexDirection = FlexDirection.Row; headRow.style.marginBottom = 2;
                var lbl = new Label("アンカー調整中（キャンバスをドラッグで移動）");
                lbl.style.fontSize = 10; lbl.style.flexGrow = 1; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
                var done = new Button(() => SetBeltAnchorMode(ed, false)) { text = "決定" }; done.style.width = 60;
                headRow.Add(lbl); headRow.Add(done); ed.AnchorPanel.Add(headRow);

                var presetRow = new VisualElement(); presetRow.style.flexDirection = FlexDirection.Row; presetRow.style.marginBottom = 2;
                SB(presetRow, "重心", () => ApplyBeltAnchorPreset(ed, Canvas2DAnchor.Preset.Centroid));
                SB(presetRow, "中心", () => ApplyBeltAnchorPreset(ed, Canvas2DAnchor.Preset.Center));
                SB(presetRow, "左上", () => ApplyBeltAnchorPreset(ed, Canvas2DAnchor.Preset.TopLeft));
                SB(presetRow, "左下", () => ApplyBeltAnchorPreset(ed, Canvas2DAnchor.Preset.BottomLeft));
                ed.AnchorPanel.Add(presetRow);

                ed.AnchorPanel.Add(BuildAnchorRow("X", -1f, 2f, 0f, out ed.AnchorXSlider, out ed.AnchorXField,
                    () => ed.AnchorSuppress, v => SetBeltAnchorComponent(ed, true, v)));
                ed.AnchorPanel.Add(BuildAnchorRow("Y", -1f, 2f, 0f, out ed.AnchorYSlider, out ed.AnchorYField,
                    () => ed.AnchorSuppress, v => SetBeltAnchorComponent(ed, false, v)));
            }
            anchorFold.Add(ed.AnchorPanel);
            RefreshBeltAnchorModeUI(ed);
            RefreshBeltAnchorFields(ed);
        }

        private void SetBeltAnchorMode(BeltProfileEdit ed, bool on)
        {
            ed.Anchor.Mode = on;
            if (on) RefreshBeltAnchorAuto(ed);
            RefreshBeltAnchorModeUI(ed);
            RefreshBeltCanvas(ed);
        }

        private static void RefreshBeltAnchorModeUI(BeltProfileEdit ed)
        {
            if (ed.AnchorEnterBtn != null) ed.AnchorEnterBtn.style.display = ed.Anchor.Mode ? DisplayStyle.None : DisplayStyle.Flex;
            if (ed.AnchorPanel    != null) ed.AnchorPanel.style.display    = ed.Anchor.Mode ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void RefreshBeltAnchorFields(BeltProfileEdit ed)
        {
            ed.AnchorSuppress = true;
            ed.AnchorXSlider?.SetValueWithoutNotify(Mathf.Clamp(ed.Anchor.Value.x, -1f, 2f));
            ed.AnchorYSlider?.SetValueWithoutNotify(Mathf.Clamp(ed.Anchor.Value.y, -1f, 2f));
            ed.AnchorXField?.SetValueWithoutNotify(ed.Anchor.Value.x);
            ed.AnchorYField?.SetValueWithoutNotify(ed.Anchor.Value.y);
            ed.AnchorSuppress = false;
        }

        private static void RefreshBeltAnchorAuto(BeltProfileEdit ed)
        {
            if (ed.Anchor.Manual) return;
            var pts = SelectedBeltPoints(ed);
            if (pts.Count > 0) ed.Anchor.SetPreset(pts, Canvas2DAnchor.Preset.Centroid);
            RefreshBeltAnchorFields(ed);
        }

        private void SetBeltAnchorComponent(BeltProfileEdit ed, bool isX, float v)
        {
            var a = ed.Anchor.Value; if (isX) a.x = v; else a.y = v; ed.Anchor.Value = a;
            ed.Anchor.Manual = true;
            RefreshBeltAnchorFields(ed); RefreshBeltCanvas(ed);
        }

        private void ApplyBeltAnchorPreset(BeltProfileEdit ed, Canvas2DAnchor.Preset p)
        {
            ed.Anchor.SetPreset(SelectedBeltPoints(ed), p);
            RefreshBeltAnchorFields(ed); RefreshBeltCanvas(ed);
        }

        /// <summary>選択（無ければ全点）の断面座標リスト。</summary>
        private static List<Vector2> SelectedBeltPoints(BeltProfileEdit ed)
        {
            var pts = new List<Vector2>();
            if (ed.Points == null) return pts;
            if (ed.Sel.Count > 0)
            {
                foreach (var i in ed.Sel)
                    if (i >= 0 && i < ed.Points.Count) pts.Add(ed.Points[i]);
            }
            else pts.AddRange(ed.Points);
            return pts;
        }

        private void ApplyBeltTransform(BeltProfileEdit ed)
        {
            EnsureBeltProfile(ed);
            BeltBegin(ed);
            RefreshBeltAnchorAuto(ed);

            var a = ed.Anchor.Value;
            float mx  = ed.TfMoveX?.value  ?? 0f, my = ed.TfMoveY?.value  ?? 0f;
            float sx  = ed.TfScaleX?.value ?? 1f, sy = ed.TfScaleY?.value ?? 1f;
            float deg = ed.TfRot?.value ?? 0f;
            float saRad = (ed.TfScaleAxis?.value ?? 0f) * Mathf.Deg2Rad;
            float saCos = Mathf.Cos(saRad), saSin = Mathf.Sin(saRad);

            bool useSel = ed.Sel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel) foreach (var i in ed.Sel) if (i >= 0 && i < ed.Points.Count) sel.Add(ed.Points[i]);
            var orig = new List<Vector2>(ed.Points);

            for (int i = 0; i < orig.Count; i++)
            {
                float wt;
                if (!useSel)                wt = 1f;
                else if (ed.Sel.Contains(i)) wt = 1f;
                else wt = ed.Magnet.Enabled ? ed.Magnet.WeightFor(orig[i], sel) : 0f;
                if (wt <= 0f) continue;

                ed.Points[i] = Xform2D(orig[i], a, mx, my, sx, sy, saCos, saSin, deg, wt);
            }

            BeltCommit(ed, "変換適用");
            D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
        }

        // ================================================================
        // ビュー・描画
        // ================================================================

        private static Vector2 BeltP2C(BeltProfileEdit ed, Vector2 p, float w, float h)
            => RevolutionProfileEditCore.ProfileToCanvas(p, w, h, ed.Zoom, ed.Offset);

        private static Vector2 BeltC2P(BeltProfileEdit ed, Vector2 c, float w, float h)
            => RevolutionProfileEditCore.CanvasToProfile(c, w, h, ed.Zoom, ed.Offset);

        private static int BeltFind(BeltProfileEdit ed, Vector2 c, float w, float h, float md)
            => RevolutionProfileEditCore.FindClosest(ed.Points, c, w, h, md, ed.Zoom, ed.Offset);

        private static void RefreshBeltCanvas(BeltProfileEdit ed) => ed?.Canvas?.MarkDirtyRepaint();

        private static void UpdateBeltView(BeltProfileEdit ed)
        {
            if (ed?.ViewLayer == null) return;
            ed.ViewLayer.style.transformOrigin = new TransformOrigin(
                new Length(50, LengthUnit.Percent), new Length(50, LengthUnit.Percent), 0f);
            ed.ViewLayer.style.scale     = new Scale(new Vector3(ed.Zoom, ed.Zoom, 1f));
            ed.ViewLayer.style.translate = new Translate(
                new Length(ed.Offset.x), new Length(ed.Offset.y), 0f);
        }

        private static void UpdateBeltBgEl(BeltProfileEdit ed)
        {
            if (ed?.BgEl == null || ed.BgTex == null || ed.Canvas == null) return;
            float cw = ed.Canvas.resolvedStyle.width;
            float ch = ed.Canvas.resolvedStyle.height;
            if (cw <= 0 || ch <= 0) return;
            float bw = ed.BgTex.width;
            float bh = ed.BgTex.height;
            if (bw < 0.5f || bh < 0.5f) return;

            float baseScale = Mathf.Min(cw / RevolutionProfileEditCore.RangeX,
                                        ch / RevolutionProfileEditCore.RangeY);
            float s = (ed.BgScale * baseScale) / bh;

            Vector2 c = RevolutionProfileEditCore.ProfileToCanvas(ed.BgOffset, cw, ch, 1f, Vector2.zero);
            ed.BgEl.style.left   = c.x - bw * 0.5f; ed.BgEl.style.top = c.y - bh * 0.5f;
            ed.BgEl.style.width  = bw; ed.BgEl.style.height = bh;
            ed.BgEl.style.transformOrigin = new TransformOrigin(
                new Length(bw * 0.5f, LengthUnit.Pixel), new Length(bh * 0.5f, LengthUnit.Pixel), 0f);
            ed.BgEl.style.scale   = new Scale(new Vector3(s, s, 1f));
            ed.BgEl.style.opacity = ed.BgAlpha;
            ed.BgEl.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(BackgroundSizeType.Cover));
        }

        private static void RefreshBeltPointUI(BeltProfileEdit ed)
        {
            if (ed.PtRow == null) return;
            if (ed.SelectedIndex >= 0 && ed.Points != null && ed.SelectedIndex < ed.Points.Count)
            {
                var pt = ed.Points[ed.SelectedIndex];
                if (ed.PtLabel != null) ed.PtLabel.text = $"Pt {ed.SelectedIndex}  X={pt.x:F3}  Y={pt.y:F3}";
                ed.PtXSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.x, -1f, 2f));
                ed.PtXField?.SetValueWithoutNotify((float)Math.Round(pt.x, 3));
                ed.PtYSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.y, -1f, 2f));
                ed.PtYField?.SetValueWithoutNotify((float)Math.Round(pt.y, 3));
                ed.PtRow.style.display = DisplayStyle.Flex;
            }
            else
            {
                ed.PtRow.style.display = DisplayStyle.None;
            }
            RefreshBeltAnchorAuto(ed);
        }

        private void DrawBeltProfile(MeshGenerationContext ctx, BeltProfileEdit ed)
        {
            if (ed?.Canvas == null || ed.Points == null || ed.Points.Count == 0) return;

            float w = ed.Canvas.resolvedStyle.width;
            float h = ed.Canvas.resolvedStyle.height;
            if (w <= 0 || h <= 0) return;

            var p2d = ctx.painter2D;

            // グリッド
            p2d.strokeColor = new Color(0.28f, 0.28f, 0.33f);
            p2d.lineWidth   = 1f;
            p2d.BeginPath();
            for (float x = -1f; x <= RevolutionProfileEditCore.RangeX; x += 0.5f)
            {
                p2d.MoveTo(BeltP2C(ed, new Vector2(x, -1f), w, h));
                p2d.LineTo(BeltP2C(ed, new Vector2(x,  2f), w, h));
            }
            for (float y = -1f; y <= 2f; y += 0.5f)
            {
                p2d.MoveTo(BeltP2C(ed, new Vector2(-1f, y), w, h));
                p2d.LineTo(BeltP2C(ed, new Vector2( 2f, y), w, h));
            }
            p2d.Stroke();

            // 軸
            p2d.strokeColor = new Color(0.52f, 0.52f, 0.58f);
            p2d.lineWidth   = 1.5f;
            p2d.BeginPath();
            p2d.MoveTo(BeltP2C(ed, new Vector2(0f, -1f), w, h));
            p2d.LineTo(BeltP2C(ed, new Vector2(0f,  2f), w, h));
            p2d.MoveTo(BeltP2C(ed, new Vector2(-1f, 0f), w, h));
            p2d.LineTo(BeltP2C(ed, new Vector2( 2f, 0f), w, h));
            p2d.Stroke();

            // x=1 の目印（正規化の基準）
            p2d.strokeColor = new Color(0.9f, 0.6f, 0.2f, 0.8f);
            p2d.lineWidth   = 1.5f;
            p2d.BeginPath();
            p2d.MoveTo(BeltP2C(ed, new Vector2(1f, -1f), w, h));
            p2d.LineTo(BeltP2C(ed, new Vector2(1f,  2f), w, h));
            p2d.Stroke();

            // 参考プロファイル（A/B のもう一方）。編集対象ではないので灰色で薄く描く。
            if (ed.GhostPoints != null && ed.GhostPoints.Count >= 2)
            {
                var ghost = new Color(0.55f, 0.55f, 0.62f, 0.75f);
                int gseg  = ed.ClosedLoop ? ed.GhostPoints.Count : ed.GhostPoints.Count - 1;

                p2d.strokeColor = ghost;
                p2d.lineWidth   = 1f;
                p2d.BeginPath();
                for (int i = 0; i < gseg; i++)
                {
                    int j = (i + 1) % ed.GhostPoints.Count;
                    p2d.MoveTo(BeltP2C(ed, ed.GhostPoints[i], w, h));
                    p2d.LineTo(BeltP2C(ed, ed.GhostPoints[j], w, h));
                }
                p2d.Stroke();

                p2d.fillColor = ghost;
                for (int i = 0; i < ed.GhostPoints.Count; i++)
                    RevFillCircle(p2d, BeltP2C(ed, ed.GhostPoints[i], w, h), 2.5f, 8);
            }

            // 断面ライン（セグメントごとにホバー表示）
            if (ed.Points.Count >= 2)
            {
                int segCount = BeltSegCount(ed);
                for (int i = 0; i < segCount; i++)
                {
                    int  j   = (i + 1) % ed.Points.Count;
                    var  a   = BeltP2C(ed, ed.Points[i], w, h);
                    var  b   = BeltP2C(ed, ed.Points[j], w, h);
                    bool hov = (i == ed.HoverEI);
                    p2d.strokeColor = hov ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.2f, 0.75f, 0.85f);
                    p2d.lineWidth   = hov ? 3f : 1.5f;
                    p2d.BeginPath();
                    p2d.MoveTo(a); p2d.LineTo(b);
                    p2d.Stroke();
                }
            }

            // 点
            for (int i = 0; i < ed.Points.Count; i++)
            {
                bool sel     = ed.Sel.Contains(i);
                bool primary = (i == ed.SelectedIndex);
                p2d.fillColor = primary ? Color.white
                              : sel     ? new Color(1f, 0.85f, 0.2f)
                              :           new Color(0.2f, 0.75f, 0.85f);
                RevFillCircle(p2d, BeltP2C(ed, ed.Points[i], w, h), (sel || primary) ? 5.5f : 3.5f, 10);
            }

            // マーキー
            if (ed.Marquee.Active)
                ed.Marquee.Draw(p2d, new Color(1f, 0.85f, 0.2f, 0.9f));

            // アンカー／ハンドル（ギズモ表示OFFで抑止。アンカー設定中は常に表示）
            if (ed.ShowGizmo || ed.Anchor.Mode)
                ed.Anchor.Draw(p2d, BeltP2C(ed, ed.Anchor.Value, w, h));
            if (ed.ShowGizmo && !ed.Anchor.Mode)
                ed.Handle.Draw(p2d, BeltP2C(ed, ed.Anchor.Value, w, h));

            // マグネット半径
            if (ed.Magnet.Enabled && ed.Sel.Count > 0)
            {
                var centers = new List<Vector2>();
                foreach (var i in ed.Sel)
                    if (i >= 0 && i < ed.Points.Count) centers.Add(BeltP2C(ed, ed.Points[i], w, h));
                float cr = Vector2.Distance(BeltP2C(ed, Vector2.zero, w, h),
                                            BeltP2C(ed, new Vector2(ed.Magnet.Radius, 0f), w, h));
                ed.Magnet.DrawRadius(p2d, centers, cr);
            }
        }

        /// <summary>断面のセグメント数（閉じた断面は点数と同じ）。</summary>
        private static int BeltSegCount(BeltProfileEdit ed)
            => ed.ClosedLoop ? ed.Points.Count : ed.Points.Count - 1;

        // ================================================================
        // ポインタ操作
        // ================================================================

        private void OnBeltProfilePointerDown(PointerDownEvent e, BeltProfileEdit ed)
        {
            if (ed?.Canvas == null) return;

            // 中ボタン＝パン
            if (e.button == 2)
            {
                ed.PanDrag        = true;
                ed.PanStart       = e.localPosition;
                ed.PanOffsetStart = ed.Offset;
                ed.Canvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }
            if (e.button != 0) return;

            // 下絵移動モード
            if (ed.BgMode && ed.BgTex != null)
            {
                ed.BgDrag              = true;
                ed.BgDragStart         = e.localPosition;
                ed.BgOffsetOnDragStart = ed.BgOffset;
                ed.Canvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }

            EnsureBeltProfile(ed);

            float w  = ed.Canvas.resolvedStyle.width;
            float h  = ed.Canvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // アンカー設定モード
            if (ed.Anchor.Mode)
            {
                ed.Anchor.Value  = BeltC2P(ed, cp, w, h);
                ed.Anchor.Manual = true;
                ed.AnchorDrag    = true;
                RefreshBeltAnchorFields(ed);
                ed.Canvas.CapturePointer(e.pointerId);
                RefreshBeltCanvas(ed);
                e.StopPropagation(); return;
            }

            // 0. ハンドル（回転/拡大縮小。ギズモ表示OFFで無効）
            var hit = ed.ShowGizmo
                ? ed.Handle.HitTest(cp, BeltP2C(ed, ed.Anchor.Value, w, h))
                : Canvas2DHandle.HandleType.None;
            if (hit != Canvas2DHandle.HandleType.None)
            {
                BeginBeltHandle(ed, hit, cp, w, h);
                ed.Canvas.CapturePointer(e.pointerId);
                RefreshBeltCanvas(ed);
                e.StopPropagation(); return;
            }

            // 1. 点ヒット（15px以内）
            int ptIdx = BeltFind(ed, cp, w, h, 15f);
            if (ptIdx >= 0)
            {
                if (e.shiftKey)
                {
                    if (!ed.Sel.Add(ptIdx)) ed.Sel.Remove(ptIdx);
                    ed.SelectedIndex = ed.Sel.Contains(ptIdx) ? ptIdx : BeltPrimary(ed);
                    ed.Canvas.CapturePointer(e.pointerId);
                    RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
                    e.StopPropagation(); return;
                }
                if (!ed.Sel.Contains(ptIdx)) { ed.Sel.Clear(); ed.Sel.Add(ptIdx); }
                ed.SelectedIndex = ptIdx;
                BeltBegin(ed);
                BeginBeltDrag(ed, cp, w, h);
                ed.Canvas.CapturePointer(e.pointerId);
                RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
                e.StopPropagation(); return;
            }

            // 2. 線分ヒット（10px以内）→ 即時挿入＆ドラッグ開始
            int     bestSeg    = -1;
            float   bestDist   = 10f;
            Vector2 insertProf = Vector2.zero;
            int     segCount   = BeltSegCount(ed);
            for (int i = 0; i < segCount; i++)
            {
                int   j = (i + 1) % ed.Points.Count;
                var   a = BeltP2C(ed, ed.Points[i], w, h);
                var   b = BeltP2C(ed, ed.Points[j], w, h);
                float t = Mathf.Clamp01(Vector2.Dot(cp - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                float d = Vector2.Distance(cp, Vector2.Lerp(a, b, t));
                if (d < bestDist)
                {
                    bestDist   = d;
                    bestSeg    = i;
                    insertProf = Vector2.Lerp(ed.Points[i], ed.Points[j], t);
                }
            }
            if (bestSeg >= 0)
            {
                int insertIdx = bestSeg + 1;
                BeltBegin(ed);
                ed.Points.Insert(insertIdx, insertProf);
                ed.Sel.Clear(); ed.Sel.Add(insertIdx);
                ed.SelectedIndex = insertIdx;
                ed.HoverEI = -1;
                BeginBeltDrag(ed, cp, w, h);
                ed.Canvas.CapturePointer(e.pointerId);
                D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
                e.StopPropagation(); return;
            }

            // 3. 空領域 → マーキー選択（Shiftで追加）
            ed.MarqueeAdditive = e.shiftKey;
            ed.Marquee.Begin(cp, ed.LassoMode);
            ed.MarqueeDrag = true;
            ed.Canvas.CapturePointer(e.pointerId);
            RefreshBeltCanvas(ed);
            e.StopPropagation();
        }

        private void OnBeltProfilePointerMove(PointerMoveEvent e, BeltProfileEdit ed)
        {
            if (ed?.Canvas == null) return;

            float w  = ed.Canvas.resolvedStyle.width;
            float h  = ed.Canvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            if (ed.PanDrag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                ed.Offset = ed.PanOffsetStart + (cp - ed.PanStart);
                UpdateBeltView(ed); UpdateBeltBgEl(ed); RefreshBeltCanvas(ed);
                e.StopPropagation(); return;
            }

            if (ed.BgDrag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                ed.BgOffset = ed.BgOffsetOnDragStart
                            + (BeltC2P(ed, cp, w, h) - BeltC2P(ed, ed.BgDragStart, w, h));
                UpdateBeltBgEl(ed);
                e.StopPropagation(); return;
            }

            if (ed.AnchorDrag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                ed.Anchor.Value = BeltC2P(ed, cp, w, h);
                RefreshBeltAnchorFields(ed); RefreshBeltCanvas(ed);
                e.StopPropagation(); return;
            }

            if (ed.HandleDrag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                ApplyBeltHandle(ed, cp, w, h);
                e.StopPropagation(); return;
            }

            if (ed.MarqueeDrag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                ed.Marquee.Update(cp);
                RefreshBeltCanvas(ed);
                e.StopPropagation(); return;
            }

            if (ed.Drag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                if (ed.Points != null && ed.DragStart.Count > 0)
                {
                    var delta = BeltC2P(ed, cp, w, h) - ed.DragStartCursorProf;
                    foreach (var kv in ed.DragStart)
                    {
                        int idx = kv.Key;
                        if (idx < 0 || idx >= ed.Points.Count) continue;
                        ed.Points[idx] = kv.Value + delta;
                    }
                    foreach (var kv in ed.MagnetStart)
                    {
                        int idx = kv.Key;
                        if (idx < 0 || idx >= ed.Points.Count) continue;
                        ed.Points[idx] = kv.Value + delta * ed.MagnetW[idx];
                    }
                    D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
                }
                e.StopPropagation(); return;
            }

            // ハンドルホバー（ギズモ表示OFF/アンカー設定中は無効）
            var hovType = (ed.Anchor.Mode || !ed.ShowGizmo)
                                         ? Canvas2DHandle.HandleType.None
                                         : ed.Handle.HitTest(cp, BeltP2C(ed, ed.Anchor.Value, w, h));
            if (hovType != ed.Handle.Hovered) { ed.Handle.Hovered = hovType; RefreshBeltCanvas(ed); }

            // 線分ホバー
            int prevHov = ed.HoverEI;
            ed.HoverEI = -1;
            if (ed.Points != null && ed.Points.Count >= 2 && BeltFind(ed, cp, w, h, 15f) < 0)
            {
                int   segCount = BeltSegCount(ed);
                float bestD    = 10f;
                for (int i = 0; i < segCount; i++)
                {
                    int   j = (i + 1) % ed.Points.Count;
                    var   a = BeltP2C(ed, ed.Points[i], w, h);
                    var   b = BeltP2C(ed, ed.Points[j], w, h);
                    float t = Mathf.Clamp01(Vector2.Dot(cp - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                    float d = Vector2.Distance(cp, Vector2.Lerp(a, b, t));
                    if (d < bestD) { bestD = d; ed.HoverEI = i; }
                }
            }
            if (ed.HoverEI != prevHov) RefreshBeltCanvas(ed);
        }

        private void OnBeltProfilePointerUp(PointerUpEvent e, BeltProfileEdit ed)
        {
            if (ed?.Canvas == null) return;
            if (!ed.Canvas.HasPointerCapture(e.pointerId)) return;
            ed.Canvas.ReleasePointer(e.pointerId);

            if (ed.MarqueeDrag) { ApplyBeltMarquee(ed); ed.Marquee.End(); ed.MarqueeDrag = false; }
            if (ed.HandleDrag)  EndBeltHandle(ed);

            bool wasDrag = ed.Drag;
            ed.Drag       = false;
            ed.BgDrag     = false;
            ed.PanDrag    = false;
            ed.AnchorDrag = false;
            if (wasDrag) BeltCommit(ed, "断面点編集");
            e.StopPropagation();
        }

        /// <summary>選択集合の代表インデックス（無ければ -1）。</summary>
        private static int BeltPrimary(BeltProfileEdit ed)
        {
            foreach (var i in ed.Sel) return i;
            return -1;
        }

        /// <summary>選択点の一括ドラッグ開始（各点の開始位置とカーソル基準を記録）。</summary>
        private static void BeginBeltDrag(BeltProfileEdit ed, Vector2 cp, float w, float h)
        {
            ed.Drag = true;
            ed.DragStart.Clear();
            if (ed.Points != null)
                foreach (var i in ed.Sel)
                    if (i >= 0 && i < ed.Points.Count) ed.DragStart[i] = ed.Points[i];
            ed.DragStartCursorProf = BeltC2P(ed, cp, w, h);

            ed.MagnetStart.Clear(); ed.MagnetW.Clear();
            if (ed.Magnet.Enabled && ed.Points != null && ed.Sel.Count > 0)
            {
                var sel = new List<Vector2>();
                foreach (var i in ed.Sel) if (i >= 0 && i < ed.Points.Count) sel.Add(ed.Points[i]);
                for (int i = 0; i < ed.Points.Count; i++)
                {
                    if (ed.Sel.Contains(i)) continue;
                    float wt = ed.Magnet.WeightFor(ed.Points[i], sel);
                    if (wt > 0f) { ed.MagnetStart[i] = ed.Points[i]; ed.MagnetW[i] = wt; }
                }
            }
        }

        /// <summary>マーキー内側の点を選択に反映する。</summary>
        private static void ApplyBeltMarquee(BeltProfileEdit ed)
        {
            float w = ed.Canvas.resolvedStyle.width, h = ed.Canvas.resolvedStyle.height;
            if (!ed.MarqueeAdditive) ed.Sel.Clear();
            if (ed.Points != null)
                for (int i = 0; i < ed.Points.Count; i++)
                    if (ed.Marquee.Contains(BeltP2C(ed, ed.Points[i], w, h))) ed.Sel.Add(i);
            ed.SelectedIndex = ed.Sel.Contains(ed.SelectedIndex) ? ed.SelectedIndex : BeltPrimary(ed);
            RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
        }

        // ── ハンドルドラッグ ─────────────────────────────────────────────

        private void BeginBeltHandle(BeltProfileEdit ed, Canvas2DHandle.HandleType type, Vector2 cp, float w, float h)
        {
            ed.HandleDrag   = true;
            ed.HandleType   = type;
            ed.Handle.Active = type;
            RefreshBeltAnchorAuto(ed);
            BeltBegin(ed);

            ed.HandleAnchorC   = BeltP2C(ed, ed.Anchor.Value, w, h);
            ed.HandlePrevAngle = Canvas2DHandle.AngleDeg(ed.HandleAnchorC, cp);
            ed.HandleTotalDeg  = 0f;

            ed.HandleStart.Clear(); ed.HandleW.Clear();
            if (ed.Points == null) return;

            bool useSel = ed.Sel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel) foreach (var i in ed.Sel) if (i >= 0 && i < ed.Points.Count) sel.Add(ed.Points[i]);

            for (int i = 0; i < ed.Points.Count; i++)
            {
                float wt;
                if (!useSel)                 wt = 1f;
                else if (ed.Sel.Contains(i)) wt = 1f;
                else wt = ed.Magnet.Enabled ? ed.Magnet.WeightFor(ed.Points[i], sel) : 0f;
                if (wt <= 0f) continue;
                ed.HandleStart[i] = ed.Points[i];
                ed.HandleW[i]     = wt;
            }
        }

        private void ApplyBeltHandle(BeltProfileEdit ed, Vector2 cp, float w, float h)
        {
            if (!ed.HandleDrag || ed.Points == null) return;

            float sx = 1f, sy = 1f, deg = 0f;
            if (ed.HandleType == Canvas2DHandle.HandleType.Rotate)
            {
                float ang = Canvas2DHandle.AngleDeg(ed.HandleAnchorC, cp);
                ed.HandleTotalDeg += -Mathf.DeltaAngle(ed.HandlePrevAngle, ang);
                ed.HandlePrevAngle = ang;
                deg = ed.HandleTotalDeg;
            }
            else
            {
                ed.Handle.ScaleFactors(ed.HandleType, ed.HandleAnchorC, cp, out sx, out sy);
            }

            var a = ed.Anchor.Value;
            foreach (var kv in ed.HandleStart)
            {
                int i = kv.Key;
                if (i < 0 || i >= ed.Points.Count) continue;
                ed.Points[i] = Xform2D(kv.Value, a, 0f, 0f, sx, sy, 1f, 0f, deg, ed.HandleW[i]);
            }
            D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
        }

        private void EndBeltHandle(BeltProfileEdit ed)
        {
            if (!ed.HandleDrag) return;
            ed.HandleDrag    = false;
            ed.HandleType    = Canvas2DHandle.HandleType.None;
            ed.Handle.Active = Canvas2DHandle.HandleType.None;
            BeltCommit(ed, "回転/拡大縮小");
        }

        // ================================================================
        // Undo
        // ================================================================

        private void EnsureBeltUndoStack(BeltProfileEdit ed)
        {
            if (ed.UndoStack != null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            undo.RemoveSubWindowStack(ed.UndoStackId);   // パネル再生成時の重複ID回避
            ed.UndoCtx   = new BeltProfileUndoContext { Profile = CloneBeltProfile(ed.Points) };
            ed.UndoStack = undo.CreateSubWindowStack(ed.UndoStackId, ed.UndoTitle, ed.UndoCtx);
            ed.UndoStack.OnUndoPerformed += _ => ApplyBeltUndoContext(ed);
            ed.UndoStack.OnRedoPerformed += _ => ApplyBeltUndoContext(ed);
        }

        /// <summary>編集前スナップショットを取得（記録の起点）。</summary>
        private void BeltBegin(BeltProfileEdit ed)
        {
            if (ed.UndoApplying) { ed.EditBefore = null; return; }
            ed.EditBefore = GetUndoController?.Invoke() == null ? null : CloneBeltProfile(ed.Points);
        }

        /// <summary>変化があればサブウィンドウスタックへ記録。</summary>
        private void BeltCommit(BeltProfileEdit ed, string desc)
        {
            var before = ed.EditBefore;
            ed.EditBefore = null;
            if (ed.UndoApplying || before == null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            var after = CloneBeltProfile(ed.Points);
            if (BeltProfileEquals(before, after)) return;
            EnsureBeltUndoStack(ed);
            if (ed.UndoStack == null) return;
            ed.UndoCtx.Profile = CloneBeltProfile(after);
            ed.UndoStack.Record(new BeltProfileUndoRecord { Before = before, After = after }, desc);
            undo.FocusSubWindow(ed.UndoStackId);
        }

        // ================================================================
        // スプライン分割
        // ================================================================

        /// <summary>スプライン分割の設定UIを組み立てる。</summary>
        /// <summary>梯子の向き補正UIを組み立てる。</summary>
        private void BuildBeltOrientUI(VisualElement c, BeltOrientOption opt)
        {
            if (c == null || opt == null) return;

            c.Add(PlayerIoUiKit.SectionLabel(T("BeltOrient")));

            var hint = new Label(T("BeltOrientHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            c.Add(hint);

            c.Add(TR(T("BeltSwapSides"),    () => opt.SwapSides,    v => { opt.SwapSides    = v; D(); }));
            c.Add(TR(T("BeltReverseOrder"), () => opt.ReverseOrder, v => { opt.ReverseOrder = v; D(); }));
        }

        /// <summary>
        /// 梯子の向き補正を適用したベルトを返す。取り込み済みデータは変更しない。
        /// 左右入替・rung順反転はどちらも巻き順の意味を反転させるため、
        /// ステップ法線を元メッシュと同じ向きに保つには FlipWinding も同時に反転する必要がある。
        /// 両方ONなら2回反転して元に戻る。
        ///
        /// 左右入替では段の Left/Right の意味も入れ替わるため、段番号を反転させる。
        /// これをしないと、隣り合う段が共有するレールの補間パラメータ t が食い違い、
        /// 共有レールが溶接されなくなる。
        /// </summary>
        private static BeltSnapshot ApplyBeltOrient(BeltSnapshot belt, BeltOrientOption opt)
        {
            if (belt == null || !belt.HasData) return belt;
            if (opt == null || opt.IsIdentity) return belt;

            var left  = new List<Vector3>(belt.Left);
            var right = new List<Vector3>(belt.Right);
            var start = belt.StartPoint;
            var end   = belt.EndPoint;
            bool flip = belt.FlipWinding;
            int  rowCount = Mathf.Max(1, belt.RowCount);
            int  rowIndex = Mathf.Clamp(belt.RowIndex, 0, rowCount - 1);

            if (opt.SwapSides)
            {
                var tmp = left; left = right; right = tmp;
                flip = !flip;
                rowIndex = rowCount - 1 - rowIndex;
            }

            if (opt.ReverseOrder)
            {
                left.Reverse();
                right.Reverse();
                var tmp = start; start = end; end = tmp;
                flip = !flip;
            }

            return new BeltSnapshot
            {
                Left        = left,
                Right       = right,
                Closed      = belt.Closed,
                FlipWinding = flip,
                HeightScale = belt.HeightScale,
                StartPoint  = start,
                EndPoint    = end,
                GroupId     = belt.GroupId,
                RowIndex    = rowIndex,
                RowCount    = rowCount,
            };
        }

        private void BuildBeltSplineUI(VisualElement c, BeltSplineOption opt)
        {
            c.Add(PlayerIoUiKit.Divider());

            // 既定は閉じる。使うときだけ開く項目のため。
            var fold = new Foldout { text = T("BeltSpline"), value = false };
            fold.style.marginBottom = 4;
            var f = fold.contentContainer;
            c.Add(fold);

            var hint = new Label(T("BeltSplineHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            f.Add(hint);

            f.Add(TR(T("BeltSplineEnable"), () => opt.Enabled,  v => { opt.Enabled  = v; D(); }));
            f.Add(IR(T("BeltSplineSegs"), 0, 10, () => opt.Segments,  v => { opt.Segments  = v; D(); }));
            f.Add(TR(T("BeltSplineUseFirst"), () => opt.UseFirst, v => { opt.UseFirst = v; D(); }));
            f.Add(TR(T("BeltSplineUseLast"),  () => opt.UseLast,  v => { opt.UseLast  = v; D(); }));
            f.Add(IR(T("BeltSplineTrimStart"), 0, 10, () => opt.TrimStart, v => { opt.TrimStart = v; D(); }));
            f.Add(IR(T("BeltSplineTrimEnd"),   0, 10, () => opt.TrimEnd,   v => { opt.TrimEnd   = v; D(); }));
        }

        /// <summary>
        /// スプライン分割を適用したベルトを返す。無効・閉じた梯子・補間不能なら元をそのまま返す。
        /// 取り込み済みデータは変更しない。
        /// </summary>
        private static BeltSnapshot ApplyBeltSpline(BeltSnapshot belt, BeltSplineOption opt)
        {
            if (belt == null || !belt.HasData) return belt;
            if (opt == null || !opt.Enabled)   return belt;
            if (belt.Closed)                   return belt;

            if (!BeltSplineSubdivider.Subdivide(
                    belt.Left, belt.Right, belt.StartPoint, belt.EndPoint,
                    opt.Segments, opt.UseFirst, opt.UseLast, opt.TrimStart, opt.TrimEnd,
                    out var left, out var right))
                return belt;

            return new BeltSnapshot
            {
                Left        = left,
                Right       = right,
                Closed      = false,
                FlipWinding = belt.FlipWinding,
                HeightScale = belt.HeightScale,
                StartPoint  = belt.StartPoint,
                EndPoint    = belt.EndPoint,
                GroupId     = belt.GroupId,
                RowIndex    = belt.RowIndex,
                RowCount    = belt.RowCount,
            };
        }

        // ================================================================
        // 生成ユーティリティ
        // ================================================================

        /// <summary>src の頂点・面を dst へ連結する（UVスロットとマテリアルは元のまま）。</summary>
        private static void AppendMesh(MeshObject dst, MeshObject src)
        {
            if (dst == null || src == null || src.VertexCount == 0) return;

            int baseIdx = dst.VertexCount;

            for (int v = 0; v < src.VertexCount; v++)
            {
                var sv = src.Vertices[v];
                var nv = new Poly_Ling.Data.Vertex(sv.Position);
                if (sv.UVs != null)
                    for (int k = 0; k < sv.UVs.Count; k++) nv.UVs.Add(sv.UVs[k]);
                if (nv.UVs.Count == 0) nv.UVs.Add(Vector2.zero);
                dst.Vertices.Add(nv);
            }

            for (int f = 0; f < src.FaceCount; f++)
            {
                var sf = src.Faces[f];
                if (sf?.VertexIndices == null || sf.VertexIndices.Count < 3) continue;

                var nf = new Face { MaterialIndex = sf.MaterialIndex };
                for (int k = 0; k < sf.VertexIndices.Count; k++)
                {
                    nf.VertexIndices.Add(baseIdx + sf.VertexIndices[k]);
                    nf.UVIndices.Add(sf.UVIndices != null && k < sf.UVIndices.Count ? sf.UVIndices[k] : 0);
                    nf.NormalIndices.Add(0);
                }
                dst.AddFace(nf);
            }
        }

        // ================================================================
        // 厚み付け（ソリッド化）共通部：フリル／パイプで共用
        // ================================================================

        /// <summary>角処理(ベベル)UI 要素。厚み/分割数に応じて表示切替するため保持する。</summary>
        private sealed class SolidifyUI
        {
            public VisualElement EdgeLabel, FrontSeg, FrontSize, BackSeg, BackSize, Inward;
        }

        /// <summary>角処理(ベベル)UI の表示を厚み/分割数に応じて更新する。</summary>
        private static void UpdateSolidifyVis(SolidifyUI ui, float thickness, int segFront, int segBack)
        {
            if (ui == null || ui.EdgeLabel == null) return;
            bool thick = thickness > 0.001f;
            ui.EdgeLabel.style.display = thick ? DisplayStyle.Flex : DisplayStyle.None;
            ui.FrontSeg.style.display  = thick ? DisplayStyle.Flex : DisplayStyle.None;
            ui.FrontSize.style.display = (thick && segFront > 0) ? DisplayStyle.Flex : DisplayStyle.None;
            ui.BackSeg.style.display   = thick ? DisplayStyle.Flex : DisplayStyle.None;
            ui.BackSize.style.display  = (thick && segBack > 0) ? DisplayStyle.Flex : DisplayStyle.None;
            ui.Inward.style.display    = (thick && (segFront > 0 || segBack > 0)) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// 面群全体を厚み付けした立体へ差し替える。厚みが 0 または生成失敗時は part をそのまま返す。
        /// 面群が閉じている場合は孤立エッジが無いため側面は生成されず、外殻と内殻の中空になる。
        /// </summary>
        private static MeshObject ApplySolidify(
            MeshObject part, float thickness, int segFront, int segBack,
            float edgeFront, float edgeBack, bool edgeInward, string meshName)
        {
            if (part == null || part.FaceCount == 0 || thickness <= 0.0001f) return part;

            var faces = new List<int>(part.FaceCount);
            for (int i = 0; i < part.FaceCount; i++) faces.Add(i);

            var r = FaceGroupSolidifier.Build(part, faces, new FaceGroupSolidifier.Params
            {
                Thickness     = thickness,
                SegmentsFront = segFront,
                SegmentsBack  = segBack,
                EdgeSizeFront = edgeFront,
                EdgeSizeBack  = edgeBack,
                EdgeInward    = edgeInward,
            }, meshName);

            return r.Ok ? r.Mesh : part;
        }

        // ================================================================
        // 対象オブジェクト選択（自動検索・配置元で共用）
        // ================================================================

        /// <summary>描画オブジェクト選択の状態。</summary>
        private sealed class MeshSourcePick
        {
            public List<(string Label, MeshObject Mesh)> Candidates = new List<(string, MeshObject)>();
            public int           Index = -1;
            public DropdownField Dropdown;

            public MeshObject Current =>
                (Index >= 0 && Index < Candidates.Count) ? Candidates[Index].Mesh : null;
        }

        /// <summary>描画オブジェクトのドロップダウンと再取得ボタンを組み立てる。</summary>
        private void BuildMeshSourceRow(VisualElement c, MeshSourcePick pick, string sectionLabel)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(sectionLabel));

            pick.Dropdown = new DropdownField(new List<string> { T("PlaceNoSource") }, 0);
            pick.Dropdown.RegisterValueChangedCallback(_ =>
            {
                pick.Index = pick.Dropdown.index - 1;   // 先頭は「(未選択)」
                D();
            });
            c.Add(pick.Dropdown);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;
            SB(row, T("PlaceRefresh"), () => RefreshMeshSourcePick(pick));
            c.Add(row);

            RefreshMeshSourcePick(pick);
        }

        private void RefreshMeshSourcePick(MeshSourcePick pick)
        {
            pick.Candidates = GetDrawableMeshList?.Invoke() ?? new List<(string, MeshObject)>();

            var choices = new List<string> { T("PlaceNoSource") };
            foreach (var e in pick.Candidates) choices.Add(e.Label);

            if (pick.Index >= pick.Candidates.Count) pick.Index = -1;

            if (pick.Dropdown != null)
            {
                pick.Dropdown.choices = choices;
                pick.Dropdown.index   = pick.Index + 1;
            }
        }

        // ================================================================
        // 対象オブジェクト複数選択（配置の配置元で使用）
        // ================================================================

        /// <summary>描画オブジェクトの複数選択状態。選択はラベルで保持し、一覧再取得後も復元する。</summary>
        private sealed class MeshSourceMultiPick
        {
            public List<(string Label, int MasterIndex, MeshObject Mesh)> Candidates
                = new List<(string, int, MeshObject)>();
            public readonly HashSet<string> SelectedLabels = new HashSet<string>();
            public VisualElement ListContainer;

            /// <summary>
            /// 候補の並び順で、選択されているメッシュを返す。
            /// 面を持たないもの（グループ用の空オブジェクト等）は数に入れない。
            /// </summary>
            public List<MeshObject> CurrentList()
                => CurrentList(false, null);

            /// <summary>
            /// 候補の並び順で、選択されているメッシュを返す。
            /// includeChildren が true のときは、チェックした項目を「本体＋子孫」へ展開し、
            /// それぞれを別々の配置元として並べる（結合しない）。これで rung ごとの
            /// 巡回・抽選が子孫に対して効く。
            /// 展開結果は MasterIndex で重複排除するため、ルートと子の両方をチェックしても
            /// 二重には入らない。面を持たないものは数に入れない。
            /// </summary>
            public List<MeshObject> CurrentList(
                bool includeChildren, Func<int, List<(int MasterIndex, MeshObject Mesh)>> expand)
            {
                var list  = new List<MeshObject>();
                var added = new HashSet<int>();

                foreach (var e in Candidates)
                {
                    if (!SelectedLabels.Contains(e.Label)) continue;

                    if (includeChildren && expand != null)
                    {
                        var sub = expand(e.MasterIndex);
                        if (sub != null)
                        {
                            foreach (var s in sub)
                            {
                                if (!HasFace(s.Mesh)) continue;
                                if (!added.Add(s.MasterIndex)) continue;
                                list.Add(s.Mesh);
                            }
                            continue;
                        }
                    }

                    if (!HasFace(e.Mesh)) continue;
                    if (!added.Add(e.MasterIndex)) continue;
                    list.Add(e.Mesh);
                }
                return list;
            }

            /// <summary>面を1枚以上持つか。頂点だけのオブジェクトは配置しても何も出ないため除く。</summary>
            private static bool HasFace(MeshObject mo) => mo != null && mo.FaceCount > 0;
        }

        /// <summary>描画オブジェクトのチェックボックス一覧と再取得ボタンを組み立てる。</summary>
        private void BuildMeshSourceMultiRow(VisualElement c, MeshSourceMultiPick pick, string sectionLabel)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(sectionLabel));

            pick.ListContainer = new VisualElement();
            pick.ListContainer.style.marginBottom = 2;
            c.Add(pick.ListContainer);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;
            SB(row, T("PlaceRefresh"), () => RefreshMeshSourceMultiPick(pick));
            c.Add(row);

            RefreshMeshSourceMultiPick(pick);
        }

        private void RefreshMeshSourceMultiPick(MeshSourceMultiPick pick)
        {
            pick.Candidates = GetDrawableMeshEntryList?.Invoke()
                              ?? new List<(string, int, MeshObject)>();

            // 一覧から消えたラベルの選択は捨てる。
            var alive = new HashSet<string>();
            foreach (var e in pick.Candidates) alive.Add(e.Label);
            pick.SelectedLabels.RemoveWhere(l => !alive.Contains(l));

            if (pick.ListContainer == null) return;
            pick.ListContainer.Clear();

            if (pick.Candidates.Count == 0)
            {
                var empty = new Label(T("PlaceNoSource"));
                empty.style.fontSize = 10;
                pick.ListContainer.Add(empty);
                return;
            }

            foreach (var e in pick.Candidates)
            {
                string label = e.Label;
                var tog = new Toggle(label) { value = pick.SelectedLabels.Contains(label) };
                tog.style.fontSize = 10;
                tog.RegisterValueChangedCallback(ev =>
                {
                    if (ev.newValue) pick.SelectedLabels.Add(label);
                    else             pick.SelectedLabels.Remove(label);
                    D();
                });
                pick.ListContainer.Add(tog);
            }
        }

        /// <summary>
        /// 複数メッシュを1つへ連結する。頂点ローカル座標をそのまま連結する
        /// （既存の配置が source.Vertices[v].Position を直接使うのと同じ扱いで、BoneTransform は考慮しない）。
        /// </summary>
        private static MeshObject CombineMeshes(IReadOnlyList<MeshObject> sources, string meshName)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(meshName) ? "Combined" : meshName);
            if (sources == null) return mo;
            foreach (var s in sources) AppendMesh(mo, s);
            return mo;
        }

        /// <summary>Undo/Redo で復元されたスナップショットをパネルへ反映。</summary>
        private void ApplyBeltUndoContext(BeltProfileEdit ed)
        {
            ed.UndoApplying = true;
            try
            {
                ed.Points = CloneBeltProfile(ed.UndoCtx?.Profile);
                ed.Sel.Clear();
                ed.SelectedIndex = -1;
                D();
                RefreshBeltCanvas(ed);
                RefreshBeltPointUI(ed);
            }
            finally { ed.UndoApplying = false; }
        }
    }
}
