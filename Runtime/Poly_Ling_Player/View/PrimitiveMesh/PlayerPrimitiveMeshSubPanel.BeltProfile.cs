// PlayerPrimitiveMeshSubPanel.BeltProfile.cs
// 図形生成サブパネル：基準ベルト（梯子状の四角形群）と断面プロファイル編集の共通部。
// フリル／パイプが各自の状態インスタンスを持って共用する。
// 編集機能は回転体プロファイルエディタと同等（複数選択・マーキー・線分挿入・マグネット・
// 選択の変換・アンカー・下絵・Undo）。回転体側のコードは変更していない。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【partial の分担】
//   BeltProfile.cs          状態の型（ベルト・断面編集）、ベルトの取り込みと CSV
//   BeltProfile.Editor.cs   断面プロファイルエディタの組み立て・編集 Undo・メッシュとの取り込み／反映
//   BeltProfile.Canvas.cs   断面プロファイルのキャンバス（描画・ポインタ操作）
//   BeltProfile.Options.cs  向き補正・スプライン・厚み付け・取り込み元オブジェクトの選択行

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
        ///
        /// 座標に加えて「どのオブジェクトから、どうやって取り込んだか」を持つ。
        /// ObjectGroup が作り直すときは、同じ手順をソースへ掛け直して梯子を取り直す。
        /// 頂点IDや頂点番号は控えない（人が番号を管理する羽目になるため）。
        /// CSV から読んだ梯子は取り込み方を持たない（＝追随できない）。
        /// </summary>
        private sealed class BeltSnapshot
        {
            public List<Vector3> Left;
            public List<Vector3> Right;
            public bool          Closed;
            public bool          FlipWinding;

            /// <summary>取り込み元の描画オブジェクトの索引。-1 = ひも付けなし。</summary>
            public int SourceMasterIndex = -1;

            /// <summary>取り込み方。作り直しで同じ手順を掛け直すために控える。</summary>
            public BeltAcquireMethod AcquireMethod = BeltAcquireMethod.Baked;

            /// <summary>取り込み時に上下へ横断して段グループにまとめたか。</summary>
            public bool AcquireCrossRows;

            /// <summary>SelectionSet のときの辞書名。</summary>
            public string AcquireSetName = "";

            /// <summary>取り込み元へひも付いているか。</summary>
            public bool HasSource
                => SourceMasterIndex >= 0 && AcquireMethod != BeltAcquireMethod.Baked;

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

            /// <summary>
            /// 「取り込み(メッシュ→断面)」で読んだ元オブジェクトの索引。-1 = ひも付けなし。
            /// 作り直しで同じ手順を掛け直すために控える。
            /// </summary>
            public int SourceMasterIndex = -1;

            /// <summary>「反映(→メッシュ)」で作る描画オブジェクトの名前。</summary>
            public string ObjectName = "Profile";

            /// <summary>終点と始点をつないだ閉じた断面として扱うか。</summary>
            public bool ClosedLoop;

            // ── 編集データ ──
            public List<Vector2> Points        = new List<Vector2>();

            /// <summary>参考表示するだけのプロファイル（A/B のもう一方）。null なら描かない。</summary>
            public List<Vector2> GhostPoints;

            // ── A/B ペア（フリルの2プロファイル）──
            // ペアを持たないエディタ（パイプ等）では PairOther が null のままで、
            // CSV の扱いは 2 列書式だけになる。

            /// <summary>ペアの相方。null ならペア無し。</summary>
            public BeltProfileEdit PairOther;

            /// <summary>自分が B 側か。CSV の列順（A が先）を決めるのに使う。</summary>
            public bool IsPairB;

            /// <summary>ペアモードが今ONか。保存を 4 列にするかの判定に使う。</summary>
            public Func<bool> PairEnabled;

            /// <summary>4 列CSVを読んだときにペアモードを立てる。</summary>
            public Action EnablePair;

            /// <summary>ペア保存時の既定ファイル名。</summary>
            public string CsvPairDefaultName = "profile_ab.csv";

            /// <summary>ペア読込のあとにエディタを組み直す。</summary>
            public Action OnPairLoaded;

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

            int srcIdx = ResolveMasterIndexOf(mesh);

            dst.Clear();
            foreach (var st in rows)
                dst.Add(ToBeltSnapshot(mesh, st, srcIdx, BeltAcquireMethod.SelectionSet, crossRows));

            SetBeltStatus(rows.Count > 1 ? $"{strip.Message} / 段 {rows.Count}" : strip.Message);
            D();
        }

        /// <summary>指定オブジェクト全体から梯子を自動検出して差し替える。</summary>
        private void AutoDetectBelts(List<BeltSnapshot> dst, MeshObject mesh, bool crossRows)
        {
            if (dst == null) return;
            if (mesh == null) { SetBeltStatus(T("NoSourceObject")); return; }

            var strips = BeltStackDetector.Detect(mesh, crossRows, out string message);

            int srcIdx = ResolveMasterIndexOf(mesh);

            dst.Clear();
            foreach (var st in strips)
                dst.Add(ToBeltSnapshot(mesh, st, srcIdx, BeltAcquireMethod.AutoLadder, crossRows));

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

            int srcIdx = ResolveMasterIndexOf(mesh);

            dst.Clear();
            foreach (var st in rows)
                dst.Add(ToBeltSnapshot(mesh, st, srcIdx, BeltAcquireMethod.AutoRing, crossRows));

            SetBeltStatus(crossRows
                ? $"{message} → グループ {groupCount} / 段 {rows.Count}"
                : message);
            D();
        }

        /// <summary>
        /// 検出結果（頂点インデックス）を座標スナップショットへ変換する。
        ///
        /// 座標と同時に「取り込み元」と「取り込み方」を控える。あとから同じ手順を
        /// 掛け直せば、ソースを編集しても今の梯子が得られる。
        /// </summary>
        private static BeltSnapshot ToBeltSnapshot(
            MeshObject mesh, BeltAutoStrip st, int sourceMasterIndex,
            BeltAcquireMethod method, bool crossRows, string setName = "")
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
                SourceMasterIndex = sourceMasterIndex,
                AcquireMethod     = sourceMasterIndex >= 0 ? method : BeltAcquireMethod.Baked,
                AcquireCrossRows  = crossRows,
                AcquireSetName    = setName ?? "",
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

        /// <summary>
        /// MeshObject の実体から描画オブジェクトの索引を引く。見つからなければ -1。
        ///
        /// 一覧のピッカーは MeshObject しか持たないが、頂点IDのひも付けには
        /// 「どのオブジェクトの ID か」が要る。参照一致で引き当てる
        /// （名前は重複しうるので使わない）。
        /// </summary>
        private int ResolveMasterIndexOf(MeshObject mesh)
        {
            if (mesh == null) return -1;

            var list = GetDrawableMeshEntryList?.Invoke();
            if (list == null) return -1;

            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i].Mesh, mesh)) return list[i].MasterIndex;

            return -1;
        }

        /// <summary>
        /// 梯子群に共通する取り込み元の索引。全部が同じ索引にひも付いているときだけ
        /// その値、そうでなければ -1（＝ひも付けなし扱い）。
        /// 途中で別オブジェクトから取り込み直した梯子が混ざったまま
        /// 片方の索引でソースを引くと、無関係な頂点を読むことになる。
        /// </summary>
        private static int BeltsSourceMasterIndex(List<BeltSnapshot> belts)
        {
            if (belts == null || belts.Count == 0) return -1;

            int found = -1;
            foreach (var b in belts)
            {
                if (b == null || !b.HasData) continue;
                if (!b.HasSource) return -1;
                if (found < 0) found = b.SourceMasterIndex;
                else if (found != b.SourceMasterIndex) return -1;
            }
            return found;
        }

        /// <summary>
        /// 梯子群に共通する取り込み方。全部が同じ取り方でなければ Baked
        /// （＝作り直しでは控えた点列をそのまま使う）。
        /// </summary>
        private static BeltAcquireMethod BeltsAcquireMethod(List<BeltSnapshot> belts)
        {
            if (BeltsSourceMasterIndex(belts) < 0) return BeltAcquireMethod.Baked;

            var found = BeltAcquireMethod.Baked;
            bool first = true;
            foreach (var b in belts)
            {
                if (b == null || !b.HasData) continue;
                if (first) { found = b.AcquireMethod; first = false; }
                else if (found != b.AcquireMethod) return BeltAcquireMethod.Baked;
            }
            return found;
        }

        /// <summary>梯子群に共通する上下展開の有無。取り方が揃っていないときは false。</summary>
        private static bool BeltsAcquireCrossRows(List<BeltSnapshot> belts)
        {
            if (BeltsAcquireMethod(belts) == BeltAcquireMethod.Baked) return false;
            foreach (var b in belts)
                if (b != null && b.HasData) return b.AcquireCrossRows;
            return false;
        }

        /// <summary>梯子群に共通する選択辞書名。取り方が揃っていないときは空。</summary>
        private static string BeltsAcquireSetName(List<BeltSnapshot> belts)
        {
            if (BeltsAcquireMethod(belts) != BeltAcquireMethod.SelectionSet) return "";
            foreach (var b in belts)
                if (b != null && b.HasData) return b.AcquireSetName ?? "";
            return "";
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
                var e = new BeltCsvEntry
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
                };

                list.Add(e);
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

                // CSV は取り込み方を持たない。読み込んだ梯子はひも付けなし
                // （SourceMasterIndex = -1 / Baked）になり、ソース追従の対象から外れる。
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
                string sel = PlayerIoUiKit.AskLoadPath(T("LoadCSV"), recentKey, path, "csv");
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

                // パス欄は読込用。保存は毎回ダイアログを出す。
                // 書き込み先はフォルダだけを覚え、ファイル名は毎回この既定から始める。
                string save = SaveDest.AskSavePath(
                    T("SaveCSV"), SaveDest.Keys.ProfileCsv, "", defaultName, "csv");
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
    }
}
