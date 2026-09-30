// PolyLingPlayerViewerCore.EditSpace.cs
// Player ビューアのコア：作業空間（ビルボード上の 2D 編集）の開始・反映・取消し・終了・ロック。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【設計】PolyLing_UV_Billboard_Design.md（作業空間の設計方針）の段階 1-1。
//   ・作業空間は同時に 1 つ。状態は _editSpace（EditSpaceSession）が持つ。
//   ・操作はすべてコマンド（PanelCommand.EditSpace.cs）で受ける。バーも MCP も同じ経路。
//   ・代理メッシュは対象モデルへ一時的に足す普通の描画オブジェクト。
//   ・作業中の履歴（1-4）：開くときに履歴の範囲（UndoScope）を開き、閉じるときに範囲の中の
//     記録を通常の履歴から除く。作業中は開く準備（代理の追加など）より前を Undo しない。反映していれば、元オブジェクトの
//     「開いた時点 → 閉じた時点」を 1 件として積む。
//   ・保存混入の防止（1-4）：開いている間は保存・書き出し・読込・モデル切替を断る
//     （EditSpaceBlockReason。コマンドの入口 PlayerCommandDispatcher.DispatchCore で見る）。
//
// 【開くとき】
//   代理を作ってからでないと副作用を起こさない（生成に失敗したら何も変えない）。
//   バインドポーズ表示ではビルボードが効かないため、現在ポーズ表示へ切り替えて控える。
//   基準ビュー（ProjectContext.BillboardView）をカレントにして代理へ寄せる。
//
// 【閉じるとき】
//   未反映の変更があれば閉じない（反映か取消しを先に）。代理を消し、選択・表示・ビューを戻す。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        /// <summary>開いている作業空間。無ければ null。</summary>
        private EditSpaceSession   _editSpace;
        private PlayerEditSpaceBar _editSpaceBar;

        /// <summary>作業倍率の既定値（既存の UV 編集モードと同じ）。</summary>
        private const float EditSpaceDefaultUvScale = 10f;

        /// <summary>「画像の縦横比で表示」の最後の値の保存キー。左ペインから開くときの既定に使う。</summary>
        private const string EditSpaceImageRatioPrefKey = "EditSpace.UseImageRatio";

        /// <summary>
        /// UV の作業倍率 (sU, sV)。画像の縦横比で表示するなら sV = sU × 高さ ÷ 幅（画素が正方形に見える）。
        /// テクスチャが無ければ sV = sU。
        /// </summary>
        private static Vector2 EditSpaceUvScale(float scaleU, Texture2D tex, bool useImageRatio)
        {
            if (!useImageRatio || tex == null || tex.width <= 0 || tex.height <= 0)
                return new Vector2(scaleU, scaleU);
            return new Vector2(scaleU, scaleU * tex.height / tex.width);
        }

        // ================================================================
        // 構築・配線
        // ================================================================

        /// <summary>作業空間バーを作る（BuildPanels から呼ぶ）。</summary>
        private void BuildEditSpaceBar()
        {
            _editSpaceBar = new PlayerEditSpaceBar
            {
                GetSession    = () => _editSpace,
                SendCommand   = DispatchFromPanel,
                GetModelIndex = PanelModelIndex,
                GetPlate      = () =>
                {
                    var s = _editSpace;
                    return s?.Proxy != null ? s.Model?.Underlay?.FindPlate(s.Proxy.ObjectId) : null;
                },
                GetPlateImageSize = p =>
                {
                    var t = p != null ? _underlay.Load(p.FilePath, reload: false, out _) : null;
                    return t != null ? new Vector2Int(t.width, t.height) : (Vector2Int?)null;
                },
                AllowPath = AllowPanelPath,
            };
            _editSpaceBar.Build(_layoutRoot.EditSpaceSection);
            _editSpaceBar.Refresh();
        }

        /// <summary>中区画の表示を作業空間の有無に合わせ、バーを読み直す。</summary>
        private void RefreshEditSpaceBar()
        {
            var section = _layoutRoot?.EditSpaceSection;
            if (section != null)
            {
                var want = _editSpace != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (section.style.display != want)
                {
                    section.style.display = want;
                    _layoutRoot.RefreshRightAreas();
                }
            }
            _editSpaceBar?.Refresh();
        }

        /// <summary>左ペイン「UV作業空間を開く」。選択中のオブジェクト（と選択面）で開く。</summary>
        private void OnUVEditSpaceButtonClicked()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _status = "モデルがありません"; return; }
            if (_editSpace != null) { _status = "作業空間が既に開いています"; RefreshEditSpaceBar(); return; }
            if (model.SelectedDrawableMeshIndices.Count == 0) { _status = "描画オブジェクトを選択してください"; return; }

            int master = model.SelectedDrawableMeshIndices[0];
            var mc     = model.GetMeshContext(master);
            var faces  = mc?.SelectedFaces != null ? new List<int>(mc.SelectedFaces).ToArray() : new int[0];

            string reason = DispatchFromPanel(new OpenUvEditSpaceCommand(
                PanelModelIndex(), master, faces, -1, EditSpaceDefaultUvScale,
                PlayerUiPrefs.GetBool(EditSpaceImageRatioPrefKey, false)));
            if (reason != null) _status = $"UV作業空間を開けません（{reason}）";
        }

        /// <summary>図形生成パネルの「作業空間で開く」。アクティブな線分オブジェクトを代理にして開く。</summary>
        private void OpenProfileEditSpaceFromPanel(string panelName, ProfileEditKind kind)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _status = "モデルがありません"; return; }
            if (_editSpace != null) { _status = "作業空間が既に開いています"; RefreshEditSpaceBar(); return; }
            int idx = model.ActiveMeshIndex;
            if (idx < 0) { _status = "断面の線分オブジェクトを選択してください"; return; }

            string reason = DispatchFromPanel(new OpenProfileEditSpaceCommand(
                PanelModelIndex(), idx, PlayerPrimitiveMeshSubPanel.KindName(kind), panelName));
            if (reason != null) _status = $"作業空間を開けません（{reason}）";
        }

        /// <summary>uiShowPanel 用。作業空間が開いていればバーを出す（開いていなければ何もしない）。</summary>
        private void ShowEditSpaceBar() => RefreshEditSpaceBar();

        // ================================================================
        // コマンドの受け口
        // ================================================================

        /// <summary>作業空間のコマンドの受け口。失敗理由を返す（成功なら null）。</summary>
        private string ExecuteEditSpaceCommand(
            PanelCommand cmd, ModelContext model, CommandDataBuilder data, EditSpaceTouched touched)
        {
            string reason;
            switch (cmd)
            {
                case OpenUvEditSpaceCommand c:            reason = OpenUvEditSpace(c, model);     break;
                case OpenProfileEditSpaceCommand c:       reason = OpenProfileEditSpace(c, model); break;
                case SetEditSpacePlaneConstraintCommand c: reason = SetEditSpacePlaneConstraint(model, c.Enabled); break;
                case SetEditSpacePlateUnderlayCommand c:   reason = SetEditSpacePlateUnderlay(model, c, data); break;
                case ClearEditSpacePlateUnderlayCommand _: reason = ClearEditSpacePlateUnderlay(model, data); break;
                case ApplyEditSpaceCommand _:             reason = ApplyEditSpace(model);         break;
                case CancelEditSpaceCommand _:            reason = CancelEditSpace(model);        break;
                case CloseEditSpaceCommand _:             reason = CloseEditSpace(model);         break;
                case SetEditSpaceBillboardLockCommand c:  reason = SetEditSpaceLock(model, c.Locked); break;
                case SetEditSpaceUnderlayCommand c:       reason = SetEditSpaceUnderlay(model, c.Visible); break;
                case SetEditSpaceImageRatioCommand c:     reason = SetEditSpaceImageRatio(model, c.Enabled); break;
                default: return "作業空間のコマンドではありません";
            }

            if (_editSpace != null && reason != null) _editSpace.LastMessage = reason;
            RefreshEditSpaceBar();
            if (reason != null) return reason;

            WriteEditSpaceState(data, cmd is SetEditSpaceUnderlayCommand);
            if (cmd is SetEditSpaceImageRatioCommand && _editSpace?.Adapter is UvEditSpaceAdapter ua && ua.Binding != null)
                data.Flag("useImageRatio", _editSpace.UseImageRatio)
                    .Num ("scaleU", ua.Binding.ScaleU)
                    .Num ("scaleV", ua.Binding.ScaleV);
            if (cmd is SetEditSpacePlaneConstraintCommand && _editSpace != null)
                data.Flag("planeConstrained", _editSpace.PlaneConstrained);
            var s = _editSpace;
            if (s != null && !s.IsBroken)
            {
                if (s.Source != null)
                {
                    touched.MasterIndices = new[] { s.SourceIndex, s.ProxyIndex };
                    touched.ObjectIds     = new[] { s.Source.ObjectId, s.Proxy.ObjectId };
                }
                else
                {
                    touched.MasterIndices = new[] { s.ProxyIndex };
                    touched.ObjectIds     = new[] { s.Proxy.ObjectId };
                }
            }
            return null;
        }

        private void WriteEditSpaceState(CommandDataBuilder d, bool cmdIsUnderlay = false)
        {
            var s = _editSpace;
            d.Flag("isOpen", s != null);
            if (s == null) return;
            d.Text("kind", s.Adapter?.Kind ?? "")
             .Int ("sourceMasterIndex", s.SourceIndex)
             .Int ("proxyMasterIndex",  s.ProxyIndex)
             .Flag("locked",     s.Locked)
             .Flag("hasChanges", s.HasChanges)
             .Int ("faces",      s.FaceCount);
            if (cmdIsUnderlay)
                d.Flag("showUnderlay", s.ShowUnderlay)
                 .Text("underlayTexture", s.UnderlayTexture != null ? s.UnderlayTexture.name : "");
        }

        // ================================================================
        // 保存混入の防止（設計方針 9.2）
        // ================================================================

        private const string EditSpaceBlockMessage =
            "作業空間を開いています。反映か取消しをしてから作業空間を終了してください";

        /// <summary>
        /// 作業空間を開いている間に断るコマンドなら理由を返す（コマンドの入口の門。
        /// PlayerCommandDispatcher.CommandGate）。代理は一時オブジェクトなので、保存・書き出しへ
        /// 混ぜない。読込・初期化・モデル切替・モデル削除は作業空間のモデルを入れ替えるので保留させる。
        /// </summary>
        private string EditSpaceBlockReason(PanelCommand cmd)
        {
            // 断面系の代理はモデルに常設のオブジェクトなので断らない（設計方針 9.2 の対象外）。
            if (_editSpace == null || !_editSpace.TemporaryProxy) return null;
            switch (cmd)
            {
                // 保存・書き出し
                case SaveProjectFileCommand _:
                case SaveProjectCsvCommand _:
                case SaveProjectBinaryCommand _:
                case ExportPmxFileCommand _:
                case ExportMqoFileCommand _:
                case ExportObjFileCommand _:
                case ExportStlFileCommand _:
                case ExportVrmFileCommand _:
                case SendHierarchyBundleCommand _:
                // 読込・初期化
                case LoadProjectFileCommand _:
                case LoadProjectCsvCommand _:
                case LoadProjectBinaryCommand _:
                case ImportPmxFileCommand _:
                case ImportMqoFileCommand _:
                case ImportObjFileCommand _:
                case ImportStlFileCommand _:
                case ImportVrmFileCommand _:
                case ImportMqoVertexPositionsCommand _:
                case ImportPmxVertexAttributesCommand _:
                case ResetProjectCommand _:
                // モデル切替・削除
                case SwitchModelCommand _:
                case DeleteModelCommand _:
                    return EditSpaceBlockMessage;
            }
            return null;
        }

        /// <summary>コマンドを通らない経路（自動検証パネル）用。作業空間を開いていれば理由。</summary>
        private string EditSpaceBlockReason()
            => _editSpace != null && _editSpace.TemporaryProxy ? EditSpaceBlockMessage : null;

        /// <summary>作業空間が操作できる状態か。できなければ理由。</summary>
        private string CheckEditSpaceUsable(ModelContext model)
        {
            var s = _editSpace;
            if (s == null) return "作業空間が開いていません";
            if (!ReferenceEquals(model, s.Model)) return "作業空間を開いたモデルが現在のモデルではありません";
            if (s.IsBroken) return "元または代理の描画オブジェクトがモデルにありません。終了してください";
            return null;
        }

        // ================================================================
        // 開く
        // ================================================================

        private string OpenUvEditSpace(OpenUvEditSpaceCommand c, ModelContext model)
        {
            if (_editSpace != null) return "作業空間が既に開いています。先に終了してください";
            var project = ActiveProject;
            if (project == null) return "プロジェクトがありません";

            var src = model.GetMeshContext(c.MasterIndex);
            if (src?.MeshObject == null) return $"masterIndex {c.MasterIndex} の描画オブジェクトがありません";
            if (src.Type != MeshType.Mesh) return $"'{src.Name}' はメッシュではありません";

            var faceSet = new HashSet<int>();
            if (c.FaceIndices != null)
            {
                foreach (int f in c.FaceIndices)
                {
                    if (f < 0 || f >= src.MeshObject.FaceCount) return $"面番号 {f} は範囲外です（面数 {src.MeshObject.FaceCount}）";
                    faceSet.Add(f);
                }
            }

            // ── 代理を作る（ここまで副作用なし） ──
            var proxyMo = UvEditSpaceOps.BuildProxy(
                src.MeshObject, faceSet, c.MaterialIndex, new Vector2(c.UvScale, c.UvScale),
                out var binding, out string err);
            if (proxyMo == null) return err;

            // 下絵（設計方針 8.2）。対象マテリアルのテクスチャ。保存しない。
            int       underlayMat = c.MaterialIndex >= 0 ? c.MaterialIndex : MostUsedMaterial(proxyMo);
            Texture2D underlayTex = MainTextureOf(model.GetMaterial(underlayMat));

            // 画像の縦横比で表示するなら、V の倍率を縦横比に合わせて作り直す。
            if (c.UseImageRatio)
            {
                Vector2 scale = EditSpaceUvScale(c.UvScale, underlayTex, true);
                if (scale.y != c.UvScale)
                {
                    proxyMo = UvEditSpaceOps.BuildProxy(
                        src.MeshObject, faceSet, c.MaterialIndex, scale, out binding, out err);
                    if (proxyMo == null) return err;
                }
            }

            int modelIdx = project.CurrentModelIndex;
            var session = new EditSpaceSession
            {
                Adapter          = new UvEditSpaceAdapter(binding),
                Model            = model,
                Source           = src,
                FaceCount        = binding.IncludedFaceCount,
                PrevPanel        = _activePanel,
                PrevViewport     = _activeViewport,
                PrevShowBindPose = project.ShowBindPose,
            };

            // ── 作業中の履歴の範囲を開く（設計方針 9.1） ──
            // ここから閉じるまでの記録は代理を参照するので、閉じるときに通常の履歴から除く。
            // 元オブジェクトは開いた時点を控え、閉じるときに反映の累積を 1 件として積む。
            var undo = _editOps?.UndoController;
            if (undo != null)
            {
                session.HistoryScope = undo.BeginHistoryScope();
                if (session.HistoryScope == null) return "履歴の範囲が既に開いています";
                session.SourceAtOpen = CaptureEditSpaceMeshes(model, src);
            }

            // ── バインドポーズ表示ではビルボードが効かないので現在ポーズ表示へ ──
            if (project.ShowBindPose)
            {
                string r = DispatchFromPanel(new SetPoseDisplayModeCommand(modelIdx, false));
                if (r != null)
                {
                    undo?.EndHistoryScope(session.HistoryScope, discard: true);
                    return $"現在ポーズ表示へ切り替えられません（{r}）";
                }
            }

            // ── 代理をモデルへ足す（範囲の中の記録として Undo に入る） ──
            string name = model.GenerateUniqueMeshName(proxyMo.Name);
            var unityMesh = proxyMo.ToUnityMesh();
            unityMesh.name      = name;
            unityMesh.hideFlags = HideFlags.HideAndDontSave;
            var proxy = new MeshContext
            {
                MeshObject = proxyMo,
                Name       = name,
                UnityMesh  = unityMesh,
                IsVisible  = true,
            };
            proxy.ParentModelContext = model;
            proxy.Billboard          = BillboardMode.ScreenAligned;
            // UV の回り順は表裏の意味を持たない（島ごとに混在する）ので、辺・頂点を裏面カリングで消さない。
            proxy.DoubleSidedDisplay = true;
            // 面は塗らない。背面の下絵を隠さないため（設計方針 6.7）。面の選択とクリック判定は残る。
            proxy.HideFaceShading    = true;

            session.UnderlayMaterialIndex = underlayMat;
            session.UnderlayTexture       = underlayTex;
            session.UseImageRatio         = c.UseImageRatio;

            var oldSelected = model.CaptureAllSelectedIndices();
            int proxyIdx    = model.Add(proxy);
            model.ComputeWorldMatrices();
            model.SelectMeshContextExclusive(proxyIdx);
            model.SelectMesh(proxyIdx);
            var newSelected = model.CaptureAllSelectedIndices();

            if (undo != null)
            {
                undo.SetModelContext(model);
                undo.RecordMeshContextAdd(proxy, proxyIdx, oldSelected, newSelected,
                    null, null, null, model.CurrentMaterialIndex, null, 0);
            }

            session.Proxy  = proxy;
            session.Locked = true;
            _editSpace     = session;

            _viewportManager.EnterTopologyChanged(project);
            ApplyEditSpaceBillboardDisplay(project);

            // ── 基準ビューをカレントにして代理へ寄せる ──
            FocusEditSpaceView(project, proxy, session);

            // 作業ビューの下絵を作業空間のものへ差し替え、0〜1 の枠を出す。
            ApplyAllUnderlays();

            // 開く準備（表示の切替・代理の追加・選択）は作業中の Undo で戻させない。
            // 準備の記録も範囲の中なので、閉じるときに除かれる。
            undo?.RaiseHistoryScopeFloor(session.HistoryScope);

            NotifyPanels(ChangeKind.ListStructure);
            session.LastMessage = $"開きました（面 {session.FaceCount}）";
            return null;
        }

        /// <summary>
        /// 断面の作業空間を開く（設計方針 7 章）。代理は指定の線分オブジェクト（常設）。
        /// 一時オブジェクトを作らないので、履歴の範囲は開かず、代理の編集は通常の履歴に残る。
        /// </summary>
        private string OpenProfileEditSpace(OpenProfileEditSpaceCommand c, ModelContext model)
        {
            if (_editSpace != null) return "作業空間が既に開いています。先に終了してください";
            var project = ActiveProject;
            if (project == null) return "プロジェクトがありません";

            if (!PlayerPrimitiveMeshSubPanel.TryParseKind(c.Kind, out var kind))
                return $"断面の種類 '{c.Kind}' がありません（revolution / profile2d / frillA / frillB / pipe）";

            PlayerPrimitiveMeshSubPanel panel;
            if (string.Equals(c.Panel, "live", System.StringComparison.OrdinalIgnoreCase)) panel = _livePrimitiveSubPanel;
            else if (string.Equals(c.Panel, "primitive", System.StringComparison.OrdinalIgnoreCase)) panel = _primitiveSubPanel;
            else return $"パネル '{c.Panel}' がありません（primitive / live）";
            if (panel == null) return "図形生成パネルがありません";

            var proxy = model.GetMeshContext(c.MasterIndex);
            if (proxy?.MeshObject == null) return $"masterIndex {c.MasterIndex} の描画オブジェクトがありません";
            if (proxy.Type != MeshType.Mesh) return $"'{proxy.Name}' はメッシュではありません";
            int lines = Poly_Ling.PrimitiveMesh.LineProfileExtractor.CollectLineFaceIndices(proxy.MeshObject).Count;
            if (lines == 0) return $"'{proxy.Name}' に線分（2 頂点の面）がありません";

            int modelIdx = project.CurrentModelIndex;
            var session = new EditSpaceSession
            {
                Adapter            = new ProfileEditSpaceAdapter(panel, kind, mc => model.IndexOf(mc)),
                Model              = model,
                Source             = null,
                Proxy              = proxy,
                FaceCount          = lines,
                PrevPanel          = _activePanel,
                PrevViewport       = _activeViewport,
                PrevShowBindPose   = project.ShowBindPose,
                PrevProxyBillboard = proxy.Billboard,
            };

            // ── バインドポーズ表示ではビルボードが効かないので現在ポーズ表示へ ──
            if (project.ShowBindPose)
            {
                string r = DispatchFromPanel(new SetPoseDisplayModeCommand(modelIdx, false));
                if (r != null) return $"現在ポーズ表示へ切り替えられません（{r}）";
            }

            int proxyIdx = model.IndexOf(proxy);
            model.SelectMeshContextExclusive(proxyIdx);
            model.SelectMesh(proxyIdx);

            proxy.Billboard = BillboardMode.ScreenAligned;
            session.Locked  = true;
            _editSpace      = session;

            // 選択の切替を描画とオブジェクト一覧へ届ける（UV を開くときと同じ 2 手）。
            _viewportManager.EnterTopologyChanged(project);
            ApplyEditSpaceBillboardDisplay(project);
            FocusEditSpaceView(project, proxy, session);
            ApplyAllUnderlays();

            NotifyPanels(ChangeKind.ListStructure);
            session.LastMessage = $"開きました（線分 {lines}）";
            return null;
        }

        /// <summary>基準ビュー（BillboardView）をカレントにし、代理の表示位置へカメラを寄せる。</summary>
        private void FocusEditSpaceView(ProjectContext project, MeshContext proxy, EditSpaceSession session)
        {
            PlayerViewportPanel panel;
            PlayerViewport      vp;
            switch (project.BillboardView)
            {
                case BillboardViewKind.Top:   panel = PanelOf(ViewportKind.Top);   vp = ViewportOf(ViewportKind.Top);   break;
                case BillboardViewKind.Front: panel = PanelOf(ViewportKind.Front); vp = ViewportOf(ViewportKind.Front); break;
                case BillboardViewKind.Side:  panel = PanelOf(ViewportKind.Side);  vp = ViewportOf(ViewportKind.Side);  break;
                case BillboardViewKind.Current:
                    panel = _activePanel; vp = _activeViewport; break;
                default:
                    panel = PanelOf(ViewportKind.Perspective); vp = ViewportOf(ViewportKind.Perspective); break;
            }
            if (panel == null || vp == null) return;
            SwitchActivePanel(panel, vp);

            // ResetToMesh は透視（Orbit）と 3 面図の共有状態（Ortho）の両方を動かすので、両方を控える。
            session.CameraViewport = vp;
            if (vp.Orbit != null)
            {
                session.HasOrbit      = true;
                session.OrbitTarget   = vp.Orbit.Target;
                session.OrbitDistance = vp.Orbit.Distance;
                session.OrbitRotX     = vp.Orbit.RotX;
                session.OrbitRotY     = vp.Orbit.RotY;
                session.OrbitRotZ     = vp.Orbit.RotZ;
            }
            if (vp.Ortho != null)
            {
                session.HasOrtho                 = true;
                session.OrthoTarget              = vp.Ortho.Target;
                session.OrthoWorldHeightPerPixel = vp.Ortho.WorldHeightPerPixel;
            }

            // 代理の表示姿勢は T(原点)·R_cam（ModelContext.ComputeBillboardMatrices）。
            // カメラを寄せても向きは変えないので、今のカメラの回転で表示上の中心を求めてよい。
            var bounds = proxy.MeshObject.CalculateBounds();
            Quaternion rot    = vp.Cam != null ? vp.Cam.transform.rotation : Quaternion.identity;
            Vector3    origin = proxy.WorldMatrix.GetColumn(3);
            Vector3    center = origin + rot * bounds.center;
            vp.ResetToMesh(new Bounds(center, bounds.size));
            _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Committed);
        }

        /// <summary>ビルボードの切替を描画へ反映する（SetMeshBillboardCommand と同じ 2 手）。</summary>
        private void ApplyEditSpaceBillboardDisplay(ProjectContext project)
        {
            _viewportManager?.EnterVerticesMoved(project, VerticesMovedPhase.DragEnd);
#pragma warning disable CS0618
            _viewportManager?.UpdateTransform();
#pragma warning restore CS0618
        }

        // ================================================================
        // 反映
        // ================================================================

        private string ApplyEditSpace(ModelContext model)
        {
            string bad = CheckEditSpaceUsable(model);
            if (bad != null) return bad;
            var s = _editSpace;

            // 断面系：元データはパネルの断面。記録はパネルの Undo（作業空間 反映）に入り、代理は変えない。
            if (!s.TemporaryProxy)
            {
                string perr = s.Adapter.Apply(s.Source, s.Proxy, new List<int>(), new List<int>());
                if (perr != null) return perr;
                s.Applied = true;
                NotifyPanels(ChangeKind.Attributes);
                s.LastMessage = "反映しました";
                return null;
            }

            // 反映は元の UV を書き、代理も元の UV から作った位置へ揃える。両方を 1 件で記録する
            // （代理を記録しないと、Redo で代理が揃える前の位置に残り、未反映の変更ありになる）。
            var undo = _editOps?.UndoController;
            var before = undo != null ? CaptureEditSpaceMeshes(model, s.Source, s.Proxy) : null;

            var problems      = new List<int>();
            var proxyProblems = new List<int>();
            string err = s.Adapter.Apply(s.Source, s.Proxy, problems, proxyProblems);
            if (err != null)
            {
                // 該当する代理の面を選択して示す（設計方針 6.5）。選択は Undo に入れない（表示のため）。
                if (proxyProblems.Count > 0) SelectEditSpaceProblemFaces(model, s, proxyProblems);
                if (proxyProblems.Count > 0) err += $"（代理の面 {JoinHead(proxyProblems)}）";
                if (problems.Count > 0)      err += $"（元の面 {JoinHead(problems)}）";
                return err;
            }

            RebuildEditSpaceUnityMesh(s.Source);
            RebuildEditSpaceUnityMesh(s.Proxy);
            s.Applied = true;

            if (before != null)
                RecordEditSpaceChange(model, before, CaptureEditSpaceMeshes(model, s.Source, s.Proxy),
                                      $"作業空間 {s.Adapter.DisplayName} 反映");

            _viewportManager.EnterTopologyChanged(ActiveProject);
            NotifyPanels(ChangeKind.Attributes);
            s.LastMessage = "反映しました";
            return null;
        }

        /// <summary>代理の面だけを選択し、描画とパネルへ届ける（反映前の検査で見つかった面を示す）。</summary>
        private void SelectEditSpaceProblemFaces(ModelContext model, EditSpaceSession s, List<int> proxyFaces)
        {
            int idx = model.IndexOf(s.Proxy);
            if (idx < 0 || s.Proxy.Selection == null) return;
            foreach (int i in model.SelectedDrawableMeshIndices)
                model.GetMeshContext(i)?.Selection?.ClearAll();
            s.Proxy.Selection.ClearAll();
            model.SelectedDrawableMeshIndices = new List<int> { idx };
            int faceCount = s.Proxy.MeshObject?.FaceCount ?? 0;
            foreach (int f in proxyFaces)
                if (f >= 0 && f < faceCount) s.Proxy.Selection.SelectFace(f, additive: true);
            _selectionOps?.OnSelectionChanged?.Invoke();
        }

        /// <summary>番号の並びを先頭 20 件まで文字にする。</summary>
        private static string JoinHead(List<int> list)
        {
            const int Max = 20;
            if (list.Count <= Max) return string.Join(",", list);
            return string.Join(",", list.GetRange(0, Max)) + $",…（ほか {list.Count - Max}）";
        }

        // ================================================================
        // 取消し
        // ================================================================

        private string CancelEditSpace(ModelContext model)
        {
            string bad = CheckEditSpaceUsable(model);
            if (bad != null) return bad;
            var s = _editSpace;

            var undo = _editOps?.UndoController;
            var before = undo != null ? CaptureEditSpaceMeshes(model, s.Proxy) : null;

            string err = s.Adapter.Reset(s.Source, s.Proxy);
            if (err != null) return err;

            RebuildEditSpaceUnityMesh(s.Proxy);

            if (before != null)
                RecordEditSpaceChange(model, before, CaptureEditSpaceMeshes(model, s.Proxy),
                                      $"作業空間 {s.Adapter.DisplayName} 取消し");

            _viewportManager.EnterTopologyChanged(ActiveProject);
            NotifyPanels(ChangeKind.Attributes);
            s.LastMessage = "未反映の変更を捨てました";
            return null;
        }

        // ================================================================
        // 閉じる
        // ================================================================

        private string CloseEditSpace(ModelContext model)
        {
            var s = _editSpace;
            if (s == null) return "作業空間が開いていません";
            if (!ReferenceEquals(model, s.Model))
            {
                // 開いたモデルがもうプロジェクトに無い（読み込み直し等）なら、作業空間を捨てるだけ。
                if (!ProjectHasModel(ActiveProject, s.Model))
                {
                    EndEditSpaceHistory(s, null, recordSource: false);
                    _editSpace = null;
                    return null;
                }
                return "作業空間を開いたモデルが現在のモデルではありません。そのモデルへ切り替えてから終了してください";
            }
            if (!s.IsBroken && s.HasChanges)
                return "未反映の変更があります。反映するか取消してから終了してください";

            var project  = ActiveProject;
            int modelIdx = project?.CurrentModelIndex ?? 0;

            // 断面系：代理は常設のオブジェクトなので消さない。ビルボードを開く前へ戻す。
            if (!s.TemporaryProxy)
            {
                if (s.Proxy != null) s.Proxy.Billboard = s.PrevProxyBillboard;
                _editSpace = null;

                RestoreEditSpaceCamera(s);
                if (s.PrevShowBindPose && project != null && !project.ShowBindPose)
                    DispatchFromPanel(new SetPoseDisplayModeCommand(modelIdx, true));
                if (s.PrevPanel != null && s.PrevViewport != null)
                    SwitchActivePanel(s.PrevPanel, s.PrevViewport);

                ApplyEditSpaceBillboardDisplay(project);
                ApplyAllUnderlays();
                NotifyPanels(ChangeKind.Attributes);
                return null;
            }

            // 元のオブジェクトを選び直してから代理を消す。
            // オブジェクト一覧は通知を 1 回受けると次の描画まで後続の通知を捨てる
            // （MeshListSubPanel.OnViewChanged の _isReceiving）。代理の削除で出る通知で
            // 選択を同期させるため、選択は削除より先に決めておく。
            int srcIdx = model.IndexOf(s.Source);
            if (srcIdx >= 0) model.SelectMeshContextExclusive(srcIdx);

            // 代理を消す（Undo に入る）。壊れていて見つからなければ消さない。
            int proxyIdx = s.ProxyIndex;
            if (proxyIdx >= 0)
            {
                string r = DispatchFromPanel(new DeleteMeshesCommand(modelIdx, new[] { proxyIdx }));
                if (r != null) return $"代理を消せません（{r}）";
            }

            _editSpace = null;

            // 表示・ビュー・カメラを戻す。
            RestoreEditSpaceCamera(s);
            if (s.PrevShowBindPose && project != null && !project.ShowBindPose)
                DispatchFromPanel(new SetPoseDisplayModeCommand(modelIdx, true));
            if (s.PrevPanel != null && s.PrevViewport != null)
                SwitchActivePanel(s.PrevPanel, s.PrevViewport);

            // 作業中の記録（代理の追加・編集・削除、反映・取消し、表示の切替）を通常の履歴から除き、
            // 元オブジェクトの変更を 1 件として積む（設計方針 9.1）。
            EndEditSpaceHistory(s, model, recordSource: true);

            // 作業空間の下絵と枠を外し、方向スロットの下絵へ戻す（_editSpace は既に null）。
            ApplyAllUnderlays();

            _viewportManager.EnterTopologyChanged(project);
            NotifyPanels(ChangeKind.ListStructure);
            return null;
        }

        /// <summary>
        /// 作業中の履歴の範囲を閉じ、範囲の中の記録を除く。
        /// recordSource が true で作業中に反映していれば、開いた時点から今までの元オブジェクトの
        /// 変更を 1 件として積む。作業中の Undo で反映を戻していた場合も 1 件積む（中身は変化なし）。
        /// </summary>
        private void EndEditSpaceHistory(EditSpaceSession s, ModelContext model, bool recordSource)
        {
            var undo = _editOps?.UndoController;
            if (undo == null || s?.HistoryScope == null) return;

            undo.EndHistoryScope(s.HistoryScope, discard: true);
            s.HistoryScope = null;

            if (!recordSource || !s.Applied || s.SourceAtOpen == null) return;
            if (model == null || model.IndexOf(s.Source) < 0) return;

            RecordEditSpaceChange(model, s.SourceAtOpen, CaptureEditSpaceMeshes(model, s.Source),
                                  $"作業空間 {s.Adapter?.DisplayName} 反映");
        }

        /// <summary>
        /// 指定の描画オブジェクトのスナップショット（実体で引き当てる。MultiMeshTopologySnapshot）。
        /// MeshObjectSnapshot（頂点編集スタック）を使わない理由：
        ///   ・SetMeshObjectFor で対象を指定すると頂点編集スタックが空になり、作業中の編集が戻せなくなる。
        ///   ・単一メッシュの記録は UnityMesh を作り直さないので、UV だけの変更が Undo 後の表示へ出ない。
        /// </summary>
        private static MultiMeshTopologySnapshot CaptureEditSpaceMeshes(ModelContext model, params MeshContext[] targets)
        {
            var snap = new MultiMeshTopologySnapshot();
            foreach (var mc in targets)
            {
                int idx = model.IndexOf(mc);
                if (idx >= 0) snap.CaptureMesh(model, idx);
            }
            return snap;
        }

        /// <summary>スナップショットの前後を 1 件として MeshListStack へ記録する。</summary>
        private void RecordEditSpaceChange(
            ModelContext model, MultiMeshTopologySnapshot before, MultiMeshTopologySnapshot after, string desc)
        {
            var undo = _editOps?.UndoController;
            if (undo == null || before == null || after == null) return;
            var record = new MultiMeshTopologySnapshotRecord(before, after, desc);
            undo.SetModelContext(model);
            undo.MeshListStack.Record(record, desc);
        }

        /// <summary>開く前に控えた作業ビューのカメラへ戻す。</summary>
        private void RestoreEditSpaceCamera(EditSpaceSession s)
        {
            var vp = s.CameraViewport;
            if (vp == null) return;
            if (s.HasOrbit && vp.Orbit != null)
            {
                vp.Orbit.Target   = s.OrbitTarget;
                vp.Orbit.Distance = s.OrbitDistance;
                vp.Orbit.RotX     = s.OrbitRotX;
                vp.Orbit.RotY     = s.OrbitRotY;
                vp.Orbit.RotZ     = s.OrbitRotZ;
            }
            if (s.HasOrtho && vp.Ortho != null)
            {
                vp.Ortho.Target              = s.OrthoTarget;
                vp.Ortho.WorldHeightPerPixel = s.OrthoWorldHeightPerPixel;
            }
            // Ortho の状態は 3 面図で共有されているので、4 面すべてへ配る。
            _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
            _viewportManager.EnterCameraChanged(_viewportManager.TopViewport,         CameraChangePhase.Committed);
            _viewportManager.EnterCameraChanged(_viewportManager.FrontViewport,       CameraChangePhase.Committed);
            _viewportManager.EnterCameraChanged(_viewportManager.SideViewport,        CameraChangePhase.Committed);
        }

        // ================================================================
        // ロック
        // ================================================================

        private string SetEditSpaceLock(ModelContext model, bool locked)
        {
            string bad = CheckEditSpaceUsable(model);
            if (bad != null) return bad;
            var s = _editSpace;

            s.Locked         = locked;
            s.Proxy.Billboard = locked ? BillboardMode.ScreenAligned : BillboardMode.Off;
            ApplyEditSpaceBillboardDisplay(ActiveProject);
            // ロック中は作業ビューの 2D の下絵と枠、ロック解除中は 3D の四角形（GetEditSpacePlateQuad）。
            ApplyAllUnderlays();
            _viewportManager.EnterOverlayContentChanged();
            NotifyPanels(ChangeKind.Attributes);
            s.LastMessage = locked ? "ビルボードをロックしました" : "ビルボードのロックを解除しました";
            return null;
        }

        // ================================================================
        // 平面制約（設計方針 5.2。UV は必須、断面系は既定でオン）
        // ================================================================

        private string SetEditSpacePlaneConstraint(ModelContext model, bool enabled)
        {
            string bad = CheckEditSpaceUsable(model);
            if (bad != null) return bad;
            var s = _editSpace;
            if (s.Adapter?.PlaneConstraint == Poly_Ling.EditSpace.EditSpacePlaneConstraint.Required && !enabled)
                return $"{s.Adapter.DisplayName} は平面制約を外せません";

            s.PlaneConstraintOn = enabled;
            // ギズモ・数値入力は ToolContext.EditSpacePlane を毎回読むので、表示の更新だけ要る。
            NotifyPanels(ChangeKind.Attributes);
            s.LastMessage = s.PlaneConstrained ? "平面制約をオンにしました" : "平面制約を外しました（3D の点を扱えます）";
            return null;
        }

        // ================================================================
        // 下絵（設計方針 8 章。UV は対象マテリアルのテクスチャを UV 0〜1 に重ねる）
        // ================================================================

        private string SetEditSpaceUnderlay(ModelContext model, bool visible)
        {
            string bad = CheckEditSpaceUsable(model);
            if (bad != null) return bad;
            var s = _editSpace;
            s.ShowUnderlay = visible;
            ApplyAllUnderlays();
            _viewportManager.EnterOverlayContentChanged();
            s.LastMessage = visible
                ? (s.UnderlayTexture != null ? "下絵を表示しました" : "下絵にするテクスチャがありません（枠だけ出します）")
                : "下絵を隠しました";
            return null;
        }

        /// <summary>
        /// UV を画像の縦横比で表示するかを切り替える。
        /// 【切り替えられる条件】未反映の変更が無いこと。代理を今の UV から新しい倍率で作り直すので、
        ///   未反映の編集は消えてしまう。
        /// 【切り替えより前は Undo で戻さない】作業中の Undo 記録は切り替え前の倍率の座標を持つ。
        ///   倍率だけ変えた代理へ戻すと位置がずれるので、履歴の範囲の床を今へ上げる。
        ///   反映した分は閉じるときに元オブジェクトの 1 件の記録にまとまるので、閉じた後に戻せる。
        /// </summary>
        private string SetEditSpaceImageRatio(ModelContext model, bool enabled)
        {
            string bad = CheckEditSpaceUsable(model);
            if (bad != null) return bad;
            var s = _editSpace;
            if (!(s.Adapter is UvEditSpaceAdapter ua) || ua.Binding == null)
                return $"{s.Adapter?.DisplayName} の作業空間では使えません（UV だけ）";

            PlayerUiPrefs.SetBool(EditSpaceImageRatioPrefKey, enabled);
            if (s.UseImageRatio == enabled) return null;
            if (s.HasChanges)
                return "未反映の変更があります。反映するか取消してから切り替えてください";

            Vector2 scale = EditSpaceUvScale(ua.Binding.ScaleU, s.UnderlayTexture, enabled);
            string err = ua.Rescale(s.Source, s.Proxy, scale);
            if (err != null) return err;
            s.UseImageRatio = enabled;
            RebuildEditSpaceUnityMesh(s.Proxy);

            var undo = _editOps?.UndoController;
            if (undo != null && s.HistoryScope != null) undo.RaiseHistoryScopeFloor(s.HistoryScope);

            var project = ActiveProject;
            _viewportManager.EnterTopologyChanged(project);
            ApplyAllUnderlays();
            _viewportManager.EnterOverlayContentChanged();
            NotifyPanels(ChangeKind.Attributes);
            s.LastMessage = enabled
                ? (s.UnderlayTexture != null
                    ? $"画像の縦横比で表示しました（{s.UnderlayTexture.width}×{s.UnderlayTexture.height}）"
                    : "下絵のテクスチャが無いので縦横比は 1 です")
                : "UV 0〜1 を正方形で表示しました";
            return null;
        }

        /// <summary>vp が作業空間の下絵・枠を出すビュー（作業ビュー）か。</summary>
        private bool IsEditSpaceUnderlayView(PlayerViewport vp)
        {
            var s = _editSpace;
            return s != null && vp != null && ReferenceEquals(vp, s.CameraViewport);
        }

        /// <summary>
        /// 作業ビューなら作業空間の下絵と 0〜1 の枠を置いて true を返す（imageShown は画像を敷いたか）。
        /// 作業空間が無い・作業ビューでない・ロックしていない・バインド表示中なら、枠を外して false
        /// （呼び出し側は従来の方向スロットの下絵へ進む）。
        ///
        /// 【置き方】ロック中の代理の表示姿勢は T(原点)·R_cam·S（ModelContext.ComputeBillboardMatrices）で、
        ///   作業面は視線に直交する。したがってローカル XY の投影は平行移動・一様な拡大・回転だけになり、
        ///   画像を回転付きの矩形として置けば UV 0〜1 に一致する。
        ///   UV は V が上なので、画像の左上 = ローカル (0, sV)、右上 = (sU, sV)、左下 = (0, 0)。
        ///   UV 0〜1 は画像の全体なので、画像は (sU, sV) の矩形へ合わせて置く。通常は sU = sV で
        ///   正方形へ引き伸ばす。「画像の縦横比で表示」では sV = sU × 高さ ÷ 幅 なので画素が正方形になる。
        ///   表示姿勢はキャッシュ（BillboardMatrix）ではなく今のカメラから組む。カメラ変更の通知の中で
        ///   呼ばれるとき、キャッシュはまだ前のカメラの値のことがあるため。
        /// </summary>
        private bool TryPlaceEditSpaceUnderlay(PlayerViewport vp, PlayerViewportPanel panel, out bool imageShown)
        {
            imageShown = false;
            var s   = _editSpace;
            var cam = vp?.Cam;
            bool usable = s != null && !s.IsBroken && s.Locked && IsEditSpaceUnderlayView(vp)
                       && !(ActiveProject?.ShowBindPose ?? false)
                       && cam != null && cam.pixelWidth > 1 && cam.pixelHeight > 1;
            if (!usable) { panel.ClearEditSpaceFrame(); return false; }

            vp.ApplyCameraTransform();
            Matrix4x4 w = s.Proxy.WorldMatrix;
            Matrix4x4 display = Matrix4x4.TRS(w.GetColumn(3), cam.transform.rotation, w.lossyScale);

            float h  = cam.pixelHeight;
            Vector2 Proj(Vector3 local)
            {
                Vector2 p = PlayerViewportManager.ProjectWorldToCameraScreen(cam, display.MultiplyPoint3x4(local));
                return new Vector2(p.x, h - p.y);   // パネル座標（Y=0 が上）
            }

            // 断面系：利用者が置く作業板スロット（設計方針 8.2・8.3）。枠は出さない。
            if (s.Adapter?.BackdropSource == Poly_Ling.EditSpace.EditSpaceBackdropSource.UserImage)
                return PlaceEditSpacePlate(vp, panel, s, Proj, out imageShown);

            Vector2 us = s.Adapter?.DisplayUnitScale ?? Vector2.one;
            Vector2 tl = Proj(new Vector3(0f,   us.y, 0f));
            Vector2 tr = Proj(new Vector3(us.x, us.y, 0f));
            Vector2 bl = Proj(Vector3.zero);
            if (float.IsNaN(tl.x) || float.IsNaN(tr.x) || float.IsNaN(bl.x))
            { panel.ClearEditSpaceFrame(); panel.ClearUnderlay(); return true; }

            Vector2 across = tr - tl, down = bl - tl;
            float wPx = across.magnitude, hPx = down.magnitude;
            if (wPx < 0.5f || hPx < 0.5f) { panel.ClearEditSpaceFrame(); panel.ClearUnderlay(); return true; }
            float angle = Mathf.Atan2(across.y, across.x) * Mathf.Rad2Deg;

            panel.SetEditSpaceFrame(tl, wPx, hPx, angle);

            // 画像は左ペインの表示グリッド「下絵」とバーの切替の両方に従う。枠は常に出す。
            int viewSlot = GetViewSlot(vp);
            bool viewShows = viewSlot < 0 || _viewportManager.GetDisplaySettings(viewSlot).ShowUnderlay;
            if (s.ShowUnderlay && viewShows && s.UnderlayTexture != null)
            {
                panel.SetUnderlayRotated(s.UnderlayTexture, tl, wPx, hPx, angle);
                imageShown = true;
            }
            else
            {
                panel.ClearUnderlay();
            }
            return true;
        }

        // ================================================================
        // 作業板スロット（設計方針 8.3・8.4。断面系の下絵）
        // ================================================================

        private string SetEditSpacePlateUnderlay(ModelContext model, SetEditSpacePlateUnderlayCommand c, CommandDataBuilder data)
        {
            string bad = CheckEditSpaceUsable(model);
            if (bad != null) return bad;
            var s = _editSpace;
            if (s.Adapter?.BackdropSource != Poly_Ling.EditSpace.EditSpaceBackdropSource.UserImage)
                return $"{s.Adapter?.DisplayName} の作業空間では使えません（下絵は対象マテリアルのテクスチャ）";

            ulong id = s.Proxy.ObjectId;
            var cur  = model.Underlay?.FindPlate(id);

            // 画像のパス。指定があれば作業フォルダの関門を通す。無ければ今の画像のまま。
            string path = cur?.FilePath ?? "";
            bool newImage = !string.IsNullOrEmpty(c.FilePath);
            if (newImage)
            {
                if (!Poly_Ling.Core.PLSandbox.TryResolveRead(c.FilePath, out string full, out string reason))
                    return reason;
                path = full;
            }
            if (string.IsNullOrEmpty(path)) return "作業板スロットに画像がありません。filePath を指定してください";

            var tex = _underlay.Load(path, reload: newImage, out string loadError);
            if (tex == null) return loadError;

            var p = cur != null ? cur.Clone() : new UnderlayPlateSlotData { ObjectId = id };
            p.FilePath = path;

            bool given = c.Corner0.x != c.Corner1.x && c.Corner0.y != c.Corner1.y;
            if (!c.KeepPlacement && given)
            {
                p.Corner0 = c.Corner0;
                p.Corner1 = c.Corner1;
            }
            else if (!c.KeepPlacement || !p.HasCorners)
            {
                DefaultPlateCorners(s.Proxy.MeshObject, tex, out var a, out var b);
                p.Corner0 = a;
                p.Corner1 = b;
            }
            if (c.Contrast  >= 0f) p.Contrast  = Mathf.Clamp01(c.Contrast);
            if (c.Intensity >= 0f) p.Intensity = Mathf.Clamp01(c.Intensity);

            if (model.Underlay == null) model.Underlay = new UnderlayData();
            model.Underlay.SetPlate(p);
            model.IsDirty = true;

            ApplyAllUnderlays();
            _viewportManager.EnterOverlayContentChanged();
            s.LastMessage = $"下絵を置きました（{System.IO.Path.GetFileName(path)}）";

            data.Text("objectId", id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Text("filePath", path)
                .Text("corner0", $"({p.Corner0.x:G6}, {p.Corner0.y:G6})")
                .Text("corner1", $"({p.Corner1.x:G6}, {p.Corner1.y:G6})")
                .Int ("width",  tex.width)
                .Int ("height", tex.height);
            return null;
        }

        private string ClearEditSpacePlateUnderlay(ModelContext model, CommandDataBuilder data)
        {
            string bad = CheckEditSpaceUsable(model);
            if (bad != null) return bad;
            var s = _editSpace;
            if (s.Adapter?.BackdropSource != Poly_Ling.EditSpace.EditSpaceBackdropSource.UserImage)
                return $"{s.Adapter?.DisplayName} の作業空間では使えません";

            bool removed = model.Underlay != null && model.Underlay.RemovePlate(s.Proxy.ObjectId);
            if (removed)
            {
                if (model.Underlay.IsEmpty) model.Underlay = null;
                model.IsDirty = true;
            }
            ApplyAllUnderlays();
            _viewportManager.EnterOverlayContentChanged();
            s.LastMessage = removed ? "下絵を外しました" : "下絵はありません";
            data.Flag("removed", removed);
            return null;
        }

        /// <summary>
        /// 作業板スロットの既定の 2 隅：代理の範囲（ローカル）の高さに画像の高さを合わせ、
        /// 縦横比を保って範囲の中央に置く。範囲の高さが無ければ幅を使う。
        /// </summary>
        private static void DefaultPlateCorners(MeshObject mo, Texture2D tex, out Vector2 c0, out Vector2 c1)
        {
            var b = mo != null ? mo.CalculateBounds() : new Bounds(Vector3.zero, Vector3.one);
            float h = b.size.y > 1e-6f ? b.size.y : b.size.x;
            if (h < 1e-6f) h = 1f;
            float aspect = tex != null ? tex.width / (float)Mathf.Max(1, tex.height) : 1f;
            float hw = h * aspect * 0.5f, hh = h * 0.5f;
            c0 = new Vector2(b.center.x - hw, b.center.y - hh);
            c1 = new Vector2(b.center.x + hw, b.center.y + hh);
        }

        /// <summary>
        /// 作業板スロットの画像を置く。スロットが無い・画像が読めないときは false を返し、
        /// 呼び出し側は方向スロットの下絵へ進む（作業ビューの画像要素は 1 つなので、スロットがあるときだけ占有する）。
        /// 2 隅は代理のローカル XY。画像の上端 = 大きい方の Y。
        /// </summary>
        private bool PlaceEditSpacePlate(PlayerViewport vp, PlayerViewportPanel panel, EditSpaceSession s,
                                         System.Func<Vector3, Vector2> proj, out bool imageShown)
        {
            imageShown = false;
            panel.ClearEditSpaceFrame();

            var plate = s.Model?.Underlay?.FindPlate(s.Proxy.ObjectId);
            if (plate == null || plate.IsEmpty || !plate.HasCorners) return false;
            var tex = _underlay.GetPlateDisplayTexture(plate);
            if (tex == null) return false;

            float x0 = Mathf.Min(plate.Corner0.x, plate.Corner1.x), x1 = Mathf.Max(plate.Corner0.x, plate.Corner1.x);
            float y0 = Mathf.Min(plate.Corner0.y, plate.Corner1.y), y1 = Mathf.Max(plate.Corner0.y, plate.Corner1.y);
            Vector2 tl = proj(new Vector3(x0, y1, 0f));
            Vector2 tr = proj(new Vector3(x1, y1, 0f));
            Vector2 bl = proj(new Vector3(x0, y0, 0f));
            if (float.IsNaN(tl.x) || float.IsNaN(tr.x) || float.IsNaN(bl.x)) { panel.ClearUnderlay(); return true; }

            Vector2 across = tr - tl, down = bl - tl;
            float wPx = across.magnitude, hPx = down.magnitude;
            if (wPx < 0.5f || hPx < 0.5f) { panel.ClearUnderlay(); return true; }
            float angle = Mathf.Atan2(across.y, across.x) * Mathf.Rad2Deg;

            // 画像は左ペインの表示グリッド「下絵」とバーの切替の両方に従う。
            int viewSlot = GetViewSlot(vp);
            bool viewShows = viewSlot < 0 || _viewportManager.GetDisplaySettings(viewSlot).ShowUnderlay;
            if (s.ShowUnderlay && viewShows)
            {
                panel.SetUnderlayRotated(tex, tl, wPx, hPx, angle);
                imageShown = true;
            }
            else panel.ClearUnderlay();
            return true;
        }

        /// <summary>
        /// ロック解除中の下絵の四角形（設計方針 8.1。PlayerViewportManager.GetPlateQuad の供給元）。
        /// ロック中は作業ビューの 2D 画像（TryPlaceEditSpaceUnderlay）が受け持つので null。
        /// 代理の姿勢は WorldMatrix（ロック解除中はビルボードしない）。
        ///   UV：ローカル (0,0)〜(s,s) に対象マテリアルのテクスチャと 0〜1 の枠。
        ///   断面系：作業板スロットの 2 隅に画像。枠は無い。
        /// 画像はバーの「下絵を表示」に従う。ビューごとの表示はビューの「下絵」設定に従う（提出側）。
        /// </summary>
        private Poly_Ling.Core.Rendering.PlateQuadParams? GetEditSpacePlateQuad()
        {
            var s = _editSpace;
            if (s == null || s.IsBroken || s.Locked) return null;
            if (ActiveProject?.ShowBindPose ?? false) return null;
            if (!ReferenceEquals(ActiveProject?.CurrentModel, s.Model)) return null;

            var p = new Poly_Ling.Core.Rendering.PlateQuadParams { LocalToWorld = s.Proxy.WorldMatrix };
            if (s.Adapter?.BackdropSource == Poly_Ling.EditSpace.EditSpaceBackdropSource.UserImage)
            {
                var plate = s.Model?.Underlay?.FindPlate(s.Proxy.ObjectId);
                if (plate == null || plate.IsEmpty || !plate.HasCorners || !s.ShowUnderlay) return null;
                var tex = _underlay.GetPlateDisplayTexture(plate);
                if (tex == null) return null;
                p.Corner0 = plate.Corner0;
                p.Corner1 = plate.Corner1;
                p.Texture = tex;
                p.ShowFrame = false;
            }
            else
            {
                Vector2 us = s.Adapter?.DisplayUnitScale ?? Vector2.one;
                p.Corner0    = Vector2.zero;
                p.Corner1    = us;   // UV 0〜1 = ローカル (0,0)〜(sU, sV)
                p.Texture    = s.ShowUnderlay ? s.UnderlayTexture : null;
                p.ShowFrame  = true;
                p.FrameColor = new Color(1f, 0.85f, 0.2f, 0.9f);   // ロック中の枠（PlayerViewportPanel._editSpaceFrame）と同じ色
            }
            return p;
        }

        /// <summary>代理の面で最も多く使われているマテリアル番号。面が無ければ -1。</summary>
        private static int MostUsedMaterial(MeshObject mo)
        {
            if (mo == null) return -1;
            var counts = new Dictionary<int, int>();
            int best = -1, bestCount = 0;
            foreach (var f in mo.Faces)
            {
                if (f == null) continue;
                counts.TryGetValue(f.MaterialIndex, out int n);
                counts[f.MaterialIndex] = ++n;
                if (n > bestCount) { bestCount = n; best = f.MaterialIndex; }
            }
            return best;
        }

        /// <summary>マテリアルの主テクスチャ（URP の _BaseMap、無ければ _MainTex）。Texture2D でなければ null。</summary>
        private static Texture2D MainTextureOf(Material mat)
        {
            if (mat == null) return null;
            if (mat.HasProperty("_BaseMap")) return mat.GetTexture("_BaseMap") as Texture2D;
            if (mat.HasProperty("_MainTex")) return mat.GetTexture("_MainTex") as Texture2D;
            return null;
        }

        // ================================================================
        // 平面制約（ToolContext.EditSpacePlane の供給元）
        // ================================================================

        /// <summary>
        /// 作業空間の平面を返す。次をすべて満たすときだけ値を返し、それ以外は null：
        ///   作業空間が開いていて壊れていない／平面制約が効いている／
        ///   作業空間のモデルが現在のモデル／選択中の描画オブジェクトが代理だけ。
        /// 代理と他のオブジェクトを同時に動かす操作には制約を掛けない（軸が 1 つに決まらないため）。
        ///
        /// 原点・姿勢は頂点の書き戻しに使う行列（MeshContext.VertexMatrix）と同じ規則で取る：
        /// バインド表示中は BindWorldMatrix、それ以外はビルボードを含む DisplayWorldMatrix。
        /// </summary>
        private Poly_Ling.Tools.EditSpacePlane? GetEditSpacePlane()
        {
            var s = _editSpace;
            if (s == null || s.IsBroken || !s.PlaneConstrained) return null;

            var project = ActiveProject;
            var model   = project?.CurrentModel;
            if (model == null || !ReferenceEquals(model, s.Model)) return null;

            var sel = model.SelectedDrawableMeshIndices;
            if (sel == null || sel.Count != 1 || sel[0] != s.ProxyIndex) return null;

            Matrix4x4 m = project.ShowBindPose ? s.Proxy.BindWorldMatrix : s.Proxy.DisplayWorldMatrix;
            Vector3 x = m.GetColumn(0), y = m.GetColumn(1);
            if (x.sqrMagnitude < 1e-12f || y.sqrMagnitude < 1e-12f) return null;
            Quaternion rot = Quaternion.LookRotation(Vector3.Cross(x, y).normalized, y.normalized);
            return new Poly_Ling.Tools.EditSpacePlane(m.GetColumn(3), rot, s.Adapter?.DisplayUnitScale ?? Vector2.one);
        }

        // ================================================================
        // 補助
        // ================================================================

        /// <summary>モデルがプロジェクトに含まれているか。</summary>
        private static bool ProjectHasModel(ProjectContext project, ModelContext model)
        {
            if (project == null || model == null) return false;
            for (int i = 0; i < project.ModelCount; i++)
                if (ReferenceEquals(project.GetModel(i), model)) return true;
            return false;
        }

        /// <summary>MeshObject から UnityMesh を作り直して差し替える。</summary>
        private static void RebuildEditSpaceUnityMesh(MeshContext mc)
        {
            if (mc?.MeshObject == null) return;
            var mesh = mc.MeshObject.ToUnityMesh();
            mesh.name      = mc.Name;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mc.ReplaceUnityMesh(mesh);
        }
    }
}
