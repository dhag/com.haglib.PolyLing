// PlayerPrimitiveMeshSubPanel.cs
// 図形生成サブパネル（UIToolkit）。このファイルはパネル全体の組み立て（Build）だけを持つ。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【partial の分担】
//   Shapes.cs         図形種別・カテゴリの登録、図形の選択、諸元 UI の切り替え（RebuildSettings）
//   Placement.cs      生成物の置き方（位置・回転・拡大・焼き込み・追加先・材質・頂点結合・グループ保持）
//   Naming.cs         名前欄（重複しない名前の候補）と追加先の選択、Name() / SetName()
//   Generate.cs       プレビュー用メッシュの生成、生成ボタンとその有効条件
//   Command.cs        パネル状態 → 生成コマンド（BuildCreateCommand）
//   Preview.cs        パネル内プレビュー、メイン3Dウインドウへの仮表示、破棄
//   Rows.cs           諸元 UI の行ヘルパ
//   ProfileCommon.cs  2D プロファイル編集の共通部品
//   ProfileUndo.cs    回転体・2D押し出しの編集 Undo
//   BasicShapes.cs / Revolution(.Canvas).cs / Profile2D(.Canvas).cs / NohMask.cs / その他の図形ファイル
//   View/McpSandbox/PlayerPrimitiveMeshSubPanel.*.cs  MCP用サンドボックスの図形

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.NohMask;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Core;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    /// <summary>図形の追加先モード</summary>
    public enum PrimitiveAddMode
    {
        NewObject,      // 新しい描画オブジェクトを作る（デフォルト）
        AddToExisting,  // 既存の描画オブジェクトに追加（なければ新規作成）
        NewModel,       // 新しいモデルを作って描画オブジェクトを追加

        /// <summary>
        /// 既存の描画オブジェクトの中身を捨てて、生成物で置き換える。
        ///
        /// オブジェクトグループの作り直し専用。新しいオブジェクトを作らないので
        /// ObjectId・名前・階層・姿勢・材質割当がそのまま残り、
        /// 出力先を指している参照が切れない。
        ///
        /// 図形生成パネルの追加先ドロップダウンには出さない
        /// （選択肢を手書きで 3 つ並べているため、ここに足しても UI は変わらない）。
        /// </summary>
        ReplaceExisting,
    }

    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // コールバック・共通の既定値
        // ================================================================

        // 生成の受け渡しは PanelCommand へ移した（PlayerPrimitiveMeshSubPanel.Command.cs）。
        // パネルからモデルへ直接メッシュを渡す経路は残していない。

        /// <summary>選択中の描画オブジェクトの MeshObject を返す(なければ null)。取り込み/反映で使用。</summary>
        public Func<MeshObject> GetSelectedMeshObject;

        /// <summary>Undoコントローラ取得（プロファイル編集Undo用）。未設定なら記録しない。</summary>
        public Func<MeshUndoController> GetUndoController;

        /// <summary>
        /// 図形パラメータの既定ピボット。各図形のピボット「下」ボタンと同じ値。
        /// 生成側 (*Params.Default) は変更せず、本パネルの初期値としてのみ差し替える。
        /// </summary>
        private static readonly Vector3 DefaultPivotBottom = new Vector3(0f, -0.5f, 0f);

        // ================================================================
        // UI
        // ================================================================

        // 添字は ShapeKind の値そのもの。図形を足したときに溢れないよう、
        // 長さは列挙の要素数から取る。
        // UI 自動操作の ID は "<パネル ID>.<下の Id>"（UiControlAttribute.cs）。
        // 図形ボタンと諸元欄は図形・カテゴリごとに作り直すので、_uiDynamicShapes / _uiDynamic が持つ。
        [UiControl(Ignore = true)]
        private readonly Button[]  _shapeBtns =
            new Button[System.Enum.GetValues(typeof(ShapeKind)).Length];
        [UiControl(Ignore = true)]
        private VisualElement      _shapeGrid;
        [UiControl(Ignore = true)]
        private VisualElement      _settingsContainer;
        [UiControl(Ignore = true)]
        private VisualElement      _profileEditorContainer;
        [UiControl(Ignore = true)]
        private VisualElement      _previewEl;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label              _statusLabel;
        [UiControl("mergeDuplicateVertices", Description = "重複頂点をマージする")]
        private Toggle             _mergeToggle;
        [UiControl("keepAsGroup", Description = "オブジェクトグループとして残す（オフだと作り直せない）")]
        private Toggle             _keepAsGroupToggle;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent, Transform sceneRoot)
        {
            LoadShapeMemory();
            _cubeP.LinkTopBottom = true;
            _sectionEl = parent;
            parent.Clear();

            // ここから組む姿勢・材質などは図形に依らない固定部分。図形を選び直しても
            // 消えないよう、行ヘルパの登録先を固定用の置き場にしておく。
            // 諸元を作り直す RebuildSettings が、登録先を諸元用へ戻す。
            _uiRowTarget = _uiDynamicFixed;
            _uiDynamicFixed.Begin("");

            parent.Add(SL(T("PanelTitle"), bold: true));
            parent.Add(Sep());

            // 図形ボタングリッド（現在カテゴリの図形のみ表示）
            _shapeGrid = new VisualElement();
            _shapeGrid.style.flexDirection = FlexDirection.Row;
            _shapeGrid.style.flexWrap      = Wrap.Wrap;
            _shapeGrid.style.marginBottom  = 4;
            parent.Add(_shapeGrid);
            PopulateShapeGrid();

            parent.Add(Sep());

            // プレビュー領域
            _previewEl = new VisualElement();
            _previewEl.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            _previewEl.style.height          = _previewHeight;
            _previewEl.style.backgroundColor = new StyleColor(new Color(0.13f, 0.13f, 0.16f));
            _previewEl.style.marginBottom    = 4;
            _previewEl.style.backgroundSize  = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
            _previewEl.pickingMode           = PickingMode.Position;
            parent.Add(_previewEl);

            _preview = new PrimitivePreviewViewport();
            _preview.Initialize(sceneRoot);

            _previewEl.RegisterCallback<GeometryChangedEvent>(e =>
            {
                _preview.Resize(Mathf.Max(1,(int)e.newRect.width), Mathf.Max(1,(int)e.newRect.height));
            });

            _previewEl.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0 && !e.ctrlKey) return;
                _previewEl.CapturePointer(e.pointerId);
                _mouseDragging = false;
                _mouseBtn      = (e.button == 0) ? 2 : e.button;
                _mouseDownPos  = e.localPosition;
                _mousePrevPos  = e.localPosition;
                e.StopPropagation();
            });
            _previewEl.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_previewEl.HasPointerCapture(e.pointerId)) return;
                var cur   = new Vector2(e.localPosition.x, e.localPosition.y);
                var delta = cur - _mousePrevPos;
                _mousePrevPos = cur;
                if (!_mouseDragging && Vector2.Distance(cur, _mouseDownPos) > DragThreshold)
                    _mouseDragging = true;
                if (!_mouseDragging) return;
                if (_mouseBtn == 1) _preview.Orbit.SimulateOrbit(delta.x, delta.y);
                else                _preview.Orbit.SimulatePan(delta.x, -delta.y);
                e.StopPropagation();
            });
            _previewEl.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!_previewEl.HasPointerCapture(e.pointerId)) return;
                _previewEl.ReleasePointer(e.pointerId);
                _mouseDragging = false;
                e.StopPropagation();
            });
            _previewEl.RegisterCallback<WheelEvent>(e =>
            {
                _preview.Orbit.SimulateScroll(-e.delta.y * 0.1f);
                e.StopPropagation();
            });

            // プレビュー下端のリサイズハンドル（下方向ドラッグで拡大）
            var resizeHandle = new VisualElement();
            resizeHandle.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            resizeHandle.style.height          = 6;
            resizeHandle.style.marginBottom    = 4;
            resizeHandle.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.36f));
            resizeHandle.pickingMode           = PickingMode.Position;
            parent.Add(resizeHandle);

            resizeHandle.RegisterCallback<PointerDownEvent>(e =>
            {
                resizeHandle.CapturePointer(e.pointerId);
                _resizeDragging    = true;
                _resizeStartY      = e.position.y;
                _resizeStartHeight = _previewHeight;
                e.StopPropagation();
            });
            resizeHandle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_resizeDragging || !resizeHandle.HasPointerCapture(e.pointerId)) return;
                float delta = e.position.y - _resizeStartY;
                _previewHeight = Mathf.Clamp(_resizeStartHeight + delta, PreviewMinHeight, PreviewMaxHeight);
                _previewEl.style.height = _previewHeight; // GeometryChangedEvent → _preview.Resize が追従
                e.StopPropagation();
            });
            resizeHandle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!resizeHandle.HasPointerCapture(e.pointerId)) return;
                resizeHandle.ReleasePointer(e.pointerId);
                _resizeDragging = false;
                e.StopPropagation();
            });

            parent.Add(Sep());

            // ステータスラベル（生成ボタンのクリックハンドラが参照するため先に生成）
            _statusLabel = new Label("");
            _statusLabel.style.color     = new StyleColor(new Color(0.7f, 0.9f, 0.7f));
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;

            // 生成ボタン（単一・永続。3Dプレビュー直下）
            parent.Add(CB());

            parent.Add(Sep());

            // ワールド生成位置 / 回転 / スケール ほか
            // 平行移動は従来どおり呼出し側 (MeshContext.BoneTransform / 頂点加算) が扱う。
            // 回転・スケールは「ベイク」チェックが ON の成分だけ Generate() 内で頂点へ焼き込み、
            // OFF の成分は呼出し側が描画オブジェクトの姿勢 (BoneTransform) へ入れる。
            // 「描画オブジェクトの姿勢」フォールド（既定は折り畳み）。
            // 本フォールドは _settingsContainer の外にあり ApplyDarkTheme の適用範囲外のため、
            // 見出しラベルの色をここで明示する。
            // 追加先ドロップダウン（姿勢より上に置く。姿勢の効き方が追加先で変わるため）
            var addModeChoices = new List<string>
            {
                T("AddModeNewObj"),
                T("AddModeExisting"),
                T("AddModeNewModel"),
            };
            var addModeDd = new DropdownField(addModeChoices, 0);
            addModeDd.label = T("AddMode");
            addModeDd.style.marginTop    = 4;
            addModeDd.style.marginBottom = 2;
            addModeDd.RegisterValueChangedCallback(e =>
            {
                _addMode = (PrimitiveAddMode)addModeChoices.IndexOf(e.newValue);
                OnAddModeChanged();
            });
            _addModeDd = addModeDd;
            parent.Add(addModeDd);

            var poseFold = new Foldout { text = T("ObjectPose"), value = false };
            _poseFold = poseFold;
            poseFold.style.marginTop    = 2;
            poseFold.style.marginBottom = 2;
            var poseFoldLabel = poseFold.Q<Label>();
            if (poseFoldLabel != null) poseFoldLabel.style.color = new StyleColor(Color.white);
            var pose = poseFold.contentContainer;

            pose.Add(SL(T("WorldPos")));
            pose.Add(V3FRef(T("WorldPosX"), T("WorldPosY"), T("WorldPosZ"),
                () => _worldPos.x, v => { _worldPos.x = v; DPlace(); },
                () => _worldPos.y, v => { _worldPos.y = v; DPlace(); },
                () => _worldPos.z, v => { _worldPos.z = v; DPlace(); },
                _posFields));

            pose.Add(BakeHeaderRow(T("Rotation"), () => _bakeRotation, v => _bakeRotation = v,
                out _bakeRotToggle));
            pose.Add(V3FRef(T("RotX"), T("RotY"), T("RotZ"),
                () => _rotEuler.x, v => { _rotEuler.x = v; D(); },
                () => _rotEuler.y, v => { _rotEuler.y = v; D(); },
                () => _rotEuler.z, v => { _rotEuler.z = v; D(); },
                _rotFields));

            // よく使う相対回転（「描画オブジェクトの姿勢」の
            // PlayerBoneEditorSubPanel.BuildQuickOffsetRow と同じ並び・同じ動き）。
            // 向こうは選択オブジェクトの BoneTransform を動かすが、こちらは
            // 生成時の回転そのものを動かす。
            var quickRotRow = new VisualElement();
            quickRotRow.style.flexDirection = FlexDirection.Row;
            quickRotRow.style.marginBottom  = 2;
            SB(quickRotRow, T("QuickRotZPlus90"),  () => OffsetRotationZ( 90f));
            SB(quickRotRow, T("QuickRotZMinus90"), () => OffsetRotationZ(-90f));
            pose.Add(quickRotRow);

            pose.Add(BakeHeaderRow(T("ScaleLabel"), () => _bakeScale, v => _bakeScale = v,
                out _bakeScaleToggle));
            pose.Add(V3FRef(T("ScaleX"), T("ScaleY"), T("ScaleZ"),
                () => _scale.x, v => { _scale.x = v; D(); },
                () => _scale.y, v => { _scale.y = v; D(); },
                () => _scale.z, v => { _scale.z = v; D(); },
                _sclFields));

            var trsResetRow = new VisualElement();
            trsResetRow.style.flexDirection = FlexDirection.Row;
            trsResetRow.style.marginBottom  = 2;
            SB(trsResetRow, T("TrsReset"), () =>
            {
                _worldPos = Vector3.zero;
                _rotEuler = Vector3.zero;
                _scale    = Vector3.one;
                RefreshTrsFields();
                DPlace();
            });
            pose.Add(trsResetRow);

            // 配置ギズモの表示チェックと、生成予定姿勢の仮表示チェック。
            // 「描画オブジェクトの姿勢」（PlayerBoneEditorSubPanel）と同じ形式だが、
            // 設定は PrimitivePlaceSettings で別に持ち、互いに影響しない。
            // 配置ギズモを持つのは 3D連携インスタンスだけなので、そちらにのみ出す。
            if (LiveWireInMainViewport)
            {
                pose.Add(SL(T("PlaceGizmo")));

                _togShowMoveGizmo     = PlaceToggle(T("PlaceGizmoShowMove"),
                    "OFF にすると矢印と中央ハンドルを消し、当たり判定も止める");
                _togShowRotationGizmo = PlaceToggle(T("PlaceGizmoShowRotate"),
                    "OFF にすると回転リングを消し、当たり判定も止める");
                _togShowOriginMarker  = PlaceToggle(T("PlacePreviewOrigin"),
                    "生成予定位置に原点マーカー（水色ダイヤ）を出す。まだ作られていない仮の表示");
                _togShowWedge         = PlaceToggle(T("PlacePreviewWedge"),
                    "生成予定の姿勢にくさびを出す。まだ作られていない仮の表示");

                _togShowMoveGizmo.RegisterValueChangedCallback(e =>
                {
                    if (_suppressPlaceToggles) return;
                    if (PlaceSettings != null) PlaceSettings.ShowMoveGizmo = e.newValue;
                    RefreshPlaceToggles();
                    OnPlaceOverlayChanged?.Invoke();
                });
                _togShowRotationGizmo.RegisterValueChangedCallback(e =>
                {
                    if (_suppressPlaceToggles) return;
                    if (PlaceSettings != null) PlaceSettings.ShowRotationGizmo = e.newValue;
                    RefreshPlaceToggles();
                    OnPlaceOverlayChanged?.Invoke();
                });
                _togShowOriginMarker.RegisterValueChangedCallback(e =>
                {
                    if (_suppressPlaceToggles) return;
                    if (PlaceSettings != null) PlaceSettings.ShowOriginMarker = e.newValue;
                    OnPlaceOverlayChanged?.Invoke();
                });
                _togShowWedge.RegisterValueChangedCallback(e =>
                {
                    if (_suppressPlaceToggles) return;
                    if (PlaceSettings != null) PlaceSettings.ShowWedge = e.newValue;
                    OnPlaceOverlayChanged?.Invoke();
                });

                pose.Add(_togShowMoveGizmo);
                pose.Add(_togShowRotationGizmo);
                pose.Add(_togShowOriginMarker);
                pose.Add(_togShowWedge);
            }

            RefreshBakeToggleVis();

            var mergeToggle = new Toggle(T("MergeDuplicates")) { value = _mergeDuplicateVertices };
            mergeToggle.style.color = new StyleColor(Color.white);
            mergeToggle.RegisterValueChangedCallback(e => { _mergeDuplicateVertices = e.newValue; _dirty = true; });
            pose.Add(mergeToggle);
            _mergeToggle = mergeToggle;

            // ── グループとして残すか
            //    毎回ダイアログを出すと「ちょっと作るだけ」の操作が重くなるので、
            //    警告は常設のラベルにする。off のときだけ出す。
            var keepToggle = new Toggle(T("KeepAsGroup")) { value = _keepAsGroup };
            keepToggle.style.color = new StyleColor(Color.white);
            pose.Add(keepToggle);
            _keepAsGroupToggle = keepToggle;

            var keepWarn = new Label(T("KeepAsGroupWarn"));
            keepWarn.style.whiteSpace  = WhiteSpace.Normal;
            keepWarn.style.fontSize    = 10;
            keepWarn.style.marginLeft  = 16;
            keepWarn.style.marginBottom = 2;
            keepWarn.style.color = new StyleColor(new Color(1f, 0.75f, 0.35f));
            keepWarn.style.display = _keepAsGroup ? DisplayStyle.None : DisplayStyle.Flex;
            pose.Add(keepWarn);

            keepToggle.RegisterValueChangedCallback(e =>
            {
                _keepAsGroup = e.newValue;
                keepWarn.style.display = _keepAsGroup ? DisplayStyle.None : DisplayStyle.Flex;
            });

            parent.Add(poseFold);

            // マテリアル指定（姿勢の下）。生成面の MaterialIndex を決める。
            // 選択肢はモデルのマテリアルスロット。0 件のときは操作できない
            // 「生成時に作成」表示にし、実際の作成は生成時に呼出し側が行う。
            var materialDd = new DropdownField(new List<string> { T("MaterialNone") }, 0);
            materialDd.label            = T("Material");
            materialDd.style.marginTop    = 2;
            materialDd.style.marginBottom = 2;
            materialDd.RegisterValueChangedCallback(e =>
            {
                int i = materialDd.choices.IndexOf(e.newValue);
                _materialIndex = i < 0 ? 0 : i;
            });
            _materialDd = materialDd;
            parent.Add(materialDd);

            parent.Add(Sep());

            // プロファイル編集コンテナ（回転体/2D押し出し/フリル/パイプ時のみ中身を持つ）
            // 並び順は 生成ボタン → 姿勢 → 追加先 → プロファイル編集 → 個別パラメータ → PIVOT。
            _profileEditorContainer = new VisualElement();
            parent.Add(_profileEditorContainer);

            // 詳細設定コンテナ
            _settingsContainer = new VisualElement();
            parent.Add(_settingsContainer);

            parent.Add(Sep());
            parent.Add(_statusLabel);

            Select(ShapeKind.Cube);

            // URP の beginCameraRendering にフック。メインカメラ描画前に
            // プレビューカメラを 1 回手動 Render する。外部から毎フレーム
            // Tick を呼ぶ必要がなくなる (規約: camera callbacks は OK)。
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void D()
        {
            _dirty = true;
            RefreshCreateButtonState();
        }
    }
}
