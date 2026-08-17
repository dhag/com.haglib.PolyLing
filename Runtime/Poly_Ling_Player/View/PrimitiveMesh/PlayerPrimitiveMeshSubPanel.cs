// PlayerPrimitiveMeshSubPanel.cs
// 図形生成サブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

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
    }

    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>
        /// 現在のモデルにある描画オブジェクト名の一覧を返す。
        /// 名前欄へ入れる非重複候補の算出に使う。未配線なら重複チェックをしない。
        /// </summary>
        public Func<List<string>> GetExistingMeshNames;

        /// <summary>現在表示中の名前欄。RebuildSettings のたびに NF が差し替える。</summary>
        private TextField _nameField;

        /// <summary>
        /// 生成ボタン押下時。
        /// (MeshObject, meshName, worldPosition, poseRotationEuler, poseScale,
        ///  ignorePoseInArmature, addMode)
        /// poseRotationEuler / poseScale は「ベイクしなかった」成分。
        /// 呼出し側が MeshContext.BoneTransform へ設定する。ベイク済みなら
        /// それぞれ Vector3.zero / Vector3.one が渡る。
        /// </summary>
        public Action<MeshObject, string, Vector3, Vector3, Vector3, bool, PrimitiveAddMode> OnMeshCreated;

        /// <summary>選択中の描画オブジェクトの MeshObject を返す(なければ null)。取り込み/反映で使用。</summary>
        public Func<MeshObject> GetSelectedMeshObject;

        /// <summary>Undoコントローラ取得（プロファイル編集Undo用）。未設定なら記録しない。</summary>
        public Func<MeshUndoController> GetUndoController;

        // ================================================================
        // メイン3Dウインドウへのライブワイヤ描画（新サブツール用）
        // 既定 false。既存の図形生成インスタンスは何も変わらない。
        // ================================================================

        /// <summary>true のとき、生成予定形状の黄色ワイヤをメイン3Dウインドウへ描画する。</summary>
        public bool LiveWireInMainViewport { get; set; }

        /// <summary>メイン3Dウインドウ（4ビューポート）のカメラかを判定する。未設定ならライブワイヤは描画しない。</summary>
        public Func<Camera, bool> IsMainViewportCamera;

        /// <summary>
        /// 追加先モードが AddToExisting のときの、追加先 MeshContext のワールド行列。
        /// 未設定なら単位行列として扱う。
        /// </summary>
        public Func<Matrix4x4> GetAddTargetWorldMatrix;

        // ---- 配置ギズモから読み書きする TRS ----

        /// <summary>生成位置。AddToExisting のときは追加先ローカル空間。</summary>
        public Vector3 PlacePosition { get => _worldPos; set { _worldPos = value; } }

        /// <summary>生成時の回転（オイラー角・度）。</summary>
        public Vector3 PlaceRotation { get => _rotEuler; set { _rotEuler = value; } }

        /// <summary>生成時のスケール。</summary>
        public Vector3 PlaceScale { get => _scale; set { _scale = value; } }

        /// <summary>現在の追加先モード。ギズモ中心の座標系判定に使う。</summary>
        public PrimitiveAddMode CurrentAddMode => _addMode;

        // ---- 配置ギズモのサブモード切替（LiveWireInMainViewport のときだけ UI を出す） ----

        /// <summary>現在の配置ギズモサブモードを返す。未配線なら Move 扱い。</summary>
        public Func<PrimitivePlaceToolHandler.PlaceGizmoMode> GetPlaceGizmoMode;

        /// <summary>配置ギズモサブモードの切替要求。未配線なら何もしない。</summary>
        public Action<PrimitivePlaceToolHandler.PlaceGizmoMode> SetPlaceGizmoMode;

        /// <summary>配置ギズモのサブモード切替ボタン（Move / Rotate / Scale の順）。</summary>
        private Button[] _placeGizmoBtns;

        /// <summary>
        /// 外部（配置ギズモ）から TRS を変更した後に呼ぶ。
        /// 数値欄へ書き戻し、プレビューを再生成対象にする。
        /// </summary>
        public void NotifyPlaceTrsChanged()
        {
            RefreshTrsFields();
            D();
        }

        // ================================================================
        // 図形種別
        // ================================================================

        public enum ShapeKind { Cube, Sphere, Cylinder, Capsule, Plane, Pyramid, Revolution, Profile2D, NohMask, Frill, Pipe, PlaceObject, ObjectArray, Text, Bridge }

        private static readonly string[] ShapeKeys =
            { "Cube","Sphere","Cylinder","Capsule","Plane","Pyramid","Revolution","Profile2D","NohMask","Frill","Pipe","PlaceObject","ObjectArray","Text","Bridge" };

        /// <summary>図形カテゴリ（左ペインの「基本図形」/「高度な図形」に対応）。</summary>
        public enum ShapeCategory { Basic, Advanced }

        // カテゴリ別の図形リスト。グリッドはこの内容だけを表示する。
        private static readonly ShapeKind[] BasicShapes =
            { ShapeKind.Cube, ShapeKind.Sphere, ShapeKind.Cylinder, ShapeKind.Capsule, ShapeKind.Plane, ShapeKind.Pyramid };
        private static readonly ShapeKind[] AdvancedShapes =
            { ShapeKind.Revolution, ShapeKind.Profile2D, ShapeKind.NohMask, ShapeKind.Frill, ShapeKind.Pipe, ShapeKind.PlaceObject,
              ShapeKind.ObjectArray, ShapeKind.Text, ShapeKind.Bridge };

        // ================================================================
        // パラメータ
        // ================================================================

        private ShapeKind _current = ShapeKind.Cube;
        private ShapeCategory _category = ShapeCategory.Basic;

        // カテゴリ別に「最後に選んだ図形」を保持する。パネルを開き直したときは
        // カテゴリ先頭ではなくこの値を選び直す。MemoryKey があれば起動をまたいで
        // PrimitiveShapeMemory（JSON）にも保存する。
        private ShapeKind _lastBasic    = ShapeKind.Cube;
        private ShapeKind _lastAdvanced = ShapeKind.Revolution;

        /// <summary>
        /// 最後に選んだ図形の保存に使うパネル識別子。Build より前に設定する。
        /// 空のときはファイル保存を行わず、起動中のみの記憶になる。
        /// </summary>
        public string MemoryKey { get; set; }
        /// <summary>
        /// 図形パラメータの既定ピボット。各図形のピボット「下」ボタンと同じ値。
        /// 生成側 (*Params.Default) は変更せず、本パネルの初期値としてのみ差し替える。
        /// </summary>
        private static readonly Vector3 DefaultPivotBottom = new Vector3(0f, -0.5f, 0f);

        private static CubeMeshGenerator.CubeParams DefaultCubeParams()
        { var p = CubeMeshGenerator.CubeParams.Default; p.Pivot = DefaultPivotBottom; return p; }
        private static SphereMeshGenerator.SphereParams DefaultSphereParams()
        { var p = SphereMeshGenerator.SphereParams.Default; p.Pivot = DefaultPivotBottom; return p; }
        private static CylinderMeshGenerator.CylinderParams DefaultCylinderParams()
        { var p = CylinderMeshGenerator.CylinderParams.Default; p.Pivot = DefaultPivotBottom; return p; }
        private static CapsuleMeshGenerator.CapsuleParams DefaultCapsuleParams()
        { var p = CapsuleMeshGenerator.CapsuleParams.Default; p.Pivot = DefaultPivotBottom; return p; }
        private static PlaneMeshGenerator.PlaneParams DefaultPlaneParams()
        { var p = PlaneMeshGenerator.PlaneParams.Default; p.Pivot = DefaultPivotBottom; return p; }
        private static PyramidMeshGenerator.PyramidParams DefaultPyramidParams()
        { var p = PyramidMeshGenerator.PyramidParams.Default; p.Pivot = DefaultPivotBottom; return p; }
        private static RevolutionParams DefaultRevolutionParams()
        { var p = RevolutionParams.Default; p.Pivot = DefaultPivotBottom; return p; }

        private CubeMeshGenerator.CubeParams         _cubeP   = DefaultCubeParams();
        private SphereMeshGenerator.SphereParams     _sphereP = DefaultSphereParams();
        private CylinderMeshGenerator.CylinderParams _cylP    = DefaultCylinderParams();
        private CapsuleMeshGenerator.CapsuleParams   _capsP   = DefaultCapsuleParams();
        private PlaneMeshGenerator.PlaneParams       _planeP  = DefaultPlaneParams();
        private PyramidMeshGenerator.PyramidParams   _pyramidP= DefaultPyramidParams();
        private RevolutionParams                     _revP    = DefaultRevolutionParams();
        private List<Vector2>                        _revProfile = null;
        private Profile2DParams                      _p2dP    = Profile2DParams.Default;
        private List<Loop>                           _p2dLoops = null;
        private FaceMeshParams                       _nohP    = FaceMeshParams.Default;

        // ワールド生成位置
        private Vector3         _worldPos            = Vector3.zero;
        private PrimitiveAddMode _addMode             = PrimitiveAddMode.NewObject;
        private bool            _mergeDuplicateVertices = true;

        // 生成時の回転(度) / スケール（平行移動は従来どおり呼出し側が扱う）。
        // ベイク ON = 頂点へ焼き込む / OFF = 描画オブジェクトの姿勢(BoneTransform)へ入れる。
        private Vector3         _rotEuler            = Vector3.zero;
        private Vector3         _scale               = Vector3.one;
        private bool            _bakeRotation        = false;
        private bool            _bakeScale           = true;

        // 「既存の描画オブジェクトへ追加」は頂点をマージする経路で姿勢を持てないため、
        // チェック状態にかかわらず両方を焼き込む。
        private bool BakeRotationEffective => _bakeRotation || _addMode == PrimitiveAddMode.AddToExisting;
        private bool BakeScaleEffective    => _bakeScale    || _addMode == PrimitiveAddMode.AddToExisting;

        /// <summary>姿勢フォールド内の「ベイク」チェックボックス（回転・スケール）。</summary>
        private Toggle _bakeRotToggle;
        private Toggle _bakeScaleToggle;

        /// <summary>
        /// 「ベイク」チェックボックスの表示を追加先に合わせる。
        /// 既存に追加のときは BakeRotationEffective / BakeScaleEffective が無条件に true になり
        /// 指定が効かないので隠す。位置・回転・スケールの値そのものは既存追加でも有効
        /// （PolyLingPlayerViewerCore.PrimitiveMeshAddToExisting が頂点へ焼き込む）。
        /// </summary>
        private void RefreshBakeToggleVis()
        {
            var d = _addMode == PrimitiveAddMode.AddToExisting
                ? DisplayStyle.None : DisplayStyle.Flex;
            if (_bakeRotToggle   != null) _bakeRotToggle.style.display   = d;
            if (_bakeScaleToggle != null) _bakeScaleToggle.style.display = d;
        }

        /// <summary>ベイクしなかった回転（描画オブジェクトの姿勢へ渡す分）。</summary>
        private Vector3 PoseRotation => BakeRotationEffective ? Vector3.zero : _rotEuler;

        /// <summary>ベイクしなかったスケール（描画オブジェクトの姿勢へ渡す分）。</summary>
        private Vector3 PoseScale    => BakeScaleEffective    ? Vector3.one  : _scale;

        // TRS 行の FloatField 参照。外部（将来のギズモ）から値を書き戻すために保持する。
        private readonly FloatField[] _posFields = new FloatField[3];
        private readonly FloatField[] _rotFields = new FloatField[3];
        private readonly FloatField[] _sclFields = new FloatField[3];

        // ================================================================
        // プレビュー
        // ================================================================

        private PrimitivePreviewViewport _preview;

        /// <summary>
        /// Build に渡された右ペインセクション。表示中かどうかの判定にのみ使う。
        /// 複数インスタンスが同時に存在しても、表示中のものだけがプレビューを描画する。
        /// </summary>
        private VisualElement _sectionEl;

        /// <summary>メイン3Dウインドウへ描く黄色ワイヤ用マテリアル。初回描画時に遅延生成する。</summary>
        private Material _liveWireMat;

        private Mesh                     _wireMesh;
        private bool                     _dirty = true;
        private double                   _nextGenAllowed;   // 適応スロットル：次回再生成を許可する時刻(realtime秒)

        // ================================================================
        // UI
        // ================================================================

        private readonly Button[]  _shapeBtns = new Button[15];
        private VisualElement      _shapeGrid;
        private VisualElement      _settingsContainer;
        private VisualElement      _profileEditorContainer;
        private VisualElement      _previewEl;
        private Label              _statusLabel;

        // プレビューマウス状態
        private bool    _mouseDragging;
        private int     _mouseBtn;
        private Vector2 _mouseDownPos;
        private Vector2 _mousePrevPos;

        // プレビュー高さ（下端ドラッグで手動リサイズ）
        private float _previewHeight = 200f;
        private bool  _resizeDragging;
        private float _resizeStartY;
        private float _resizeStartHeight;
        private const float PreviewMinHeight = 80f;
        private const float PreviewMaxHeight = 1200f;
        private const float DragThreshold = 3f;

        // プロファイル編集キャンバス高さ（下端ドラッグで手動リサイズ）
        private float _profileHeight = 260f;
        private bool  _profileResizeDragging;
        private float _profileResizeStartY;
        private float _profileResizeStartHeight;
        private const float ProfileMinHeight = 120f;
        private const float ProfileMaxHeight = 900f;

        // ================================================================
        // Revolution プロファイルエディタ状態
        // ================================================================

        private int           _revSelIdx    = -1;
        private bool          _revDrag      = false;
        private int           _revDragIdx   = -1;

        // 複数選択・一括移動・マーキー（Phase 2）
        private readonly HashSet<int> _revSel = new HashSet<int>();
        private readonly Dictionary<int, Vector2> _revDragStart = new Dictionary<int, Vector2>();
        private Vector2 _revDragStartCursorProf;
        private readonly Canvas2DMarquee _revMarquee = new Canvas2DMarquee();
        private bool _revLassoMode;
        private bool _revMarqueeAdditive;
        private bool _revMarqueeDrag;

        // 回転/拡大縮小アンカーと変換（Phase B）
        private readonly Canvas2DAnchor _revAnchor = new Canvas2DAnchor();
        private bool          _revAnchorDrag;
        private bool          _revAnchorSuppress;
        private Slider        _revAnchorXSlider, _revAnchorYSlider;
        private FloatField    _revAnchorXField,  _revAnchorYField;
        private Button        _revAnchorEnterBtn;
        private VisualElement _revAnchorPanel;
        private FloatField    _revTfMoveX, _revTfMoveY, _revTfScaleX, _revTfScaleY, _revTfRot;
        private FloatField    _revTfScaleAxis;

        // 回転/拡大縮小ハンドル（キャンバス上ドラッグ）
        private readonly Canvas2DHandle _revHandle = new Canvas2DHandle();
        private bool                    _revHandleDrag;
        private Canvas2DHandle.HandleType _revHandleType = Canvas2DHandle.HandleType.None;
        private readonly Dictionary<int, Vector2> _revHandleStart = new Dictionary<int, Vector2>();
        private readonly Dictionary<int, float>   _revHandleW     = new Dictionary<int, float>();
        private Vector2 _revHandleAnchorC;   // ドラッグ開始時のアンカー(キャンバス座標)
        private float   _revHandlePrevAngle; // 回転累積用の直前角度
        private float   _revHandleTotalDeg;  // 累積回転角(データ空間deg)
        // ギズモ表示トグル（回転体。既定=非表示、メモリ保持・非永続）
        private bool    _revShowGizmo = false;

        // マグネット（比例編集、Phase）
        private readonly Canvas2DMagnet _revMagnet = new Canvas2DMagnet();
        private readonly Dictionary<int, Vector2> _revMagnetStart = new Dictionary<int, Vector2>();
        private readonly Dictionary<int, float>   _revMagnetW     = new Dictionary<int, float>();
        private Slider        _revMagnetRadius;
        private int           _revHoverEI   = -1;
        private VisualElement _revCanvas;
        private VisualElement _revPtRow;
        private Label         _revPtLabel;
        private Slider        _revPtXSlider;
        private FloatField    _revPtXField;
        private Slider        _revPtYSlider;
        private FloatField    _revPtYField;
        private string        _revCsvPath   = "";
        private const string  RevCsvKey  = "Primitive.Revolution.Csv";
        private const string  P2dCsvKey  = "Primitive.Profile2D.Csv";
        private const string  NohLmKey   = "Primitive.NohMask.Landmarks";
        private const string  NohTriKey  = "Primitive.NohMask.Triangles";
        private const string  NohSaveKey = "Primitive.NohMask.SaveJson";
        // 下絵
        private Texture2D     _revBgTex;
        private VisualElement _revBgEl;
        private string        _revBgPath    = "";
        private Vector2       _revBgOffset  = Vector2.zero;
        private float         _revBgScale   = 3f;   // 画像高さ(ワールド単位)
        private Vector2       _revBgOrigin  = Vector2.zero; // 拡大縮小の原点（画像px, 既定=中心）
        private Slider        _revBgScaleSlider;
        private Label         _revBgSizeLabel;
        private float         _revBgAlpha   = 0.4f;
        private bool          _revBgMode    = false; // true=下絵移動モード
        private bool          _revBgDrag    = false;
        private Vector2       _revBgDragStart;
        private Vector2       _revBgOffsetOnDragStart;

        // プロファイルビュー（ズーム/パン）
        private float         _revZoom      = 1f;
        private Vector2       _revOffset    = Vector2.zero;
        private VisualElement _revViewLayer;          // 下絵を view 変換で追従させる層
        private bool          _revPanDrag;            // 中ボタンパン中
        private Vector2       _revPanStart;
        private Vector2       _revPanOffsetStart;

        // ── Profile2D キャンバス状態 ──────────────────────────────────────
        private VisualElement _p2dCanvas;
        private VisualElement _p2dPtRow;
        private Slider        _p2dPtXSlider;
        private FloatField    _p2dPtXField;
        private Slider        _p2dPtYSlider;
        private FloatField    _p2dPtYField;
        private int           _p2dSelLoop = 0;
        private int           _p2dSelPt   = -1;
        private bool          _p2dDrag    = false;
        private int           _p2dDragIdx = -1;

        // 複数選択・一括移動・マーキー（Phase 2、キー=((long)loop<<32)|pt）
        private readonly HashSet<long> _p2dSel = new HashSet<long>();
        private readonly Dictionary<long, Vector2> _p2dDragStart = new Dictionary<long, Vector2>();
        private Vector2 _p2dDragStartCursorWorld;
        private readonly Canvas2DMarquee _p2dMarquee = new Canvas2DMarquee();
        private bool _p2dLassoMode;
        private bool _p2dMarqueeAdditive;
        private bool _p2dMarqueeSubtract;
        private bool _p2dMarqueeDrag;
        // ギズモ/頂点の表示トグル（Profile2D。ギズモ既定=非表示／頂点既定=表示、メモリ保持・非永続）
        private bool _p2dShowGizmo = false;
        private bool _p2dShowVerts = true;

        // 回転/拡大縮小アンカーと変換（Phase B）
        private readonly Canvas2DAnchor _p2dAnchor = new Canvas2DAnchor();
        private bool          _p2dAnchorDrag;
        private bool          _p2dAnchorSuppress;
        private Slider        _p2dAnchorXSlider, _p2dAnchorYSlider;
        private FloatField    _p2dAnchorXField,  _p2dAnchorYField;
        private Button        _p2dAnchorEnterBtn;
        private VisualElement _p2dAnchorPanel;
        private FloatField    _p2dTfMoveX, _p2dTfMoveY, _p2dTfScaleX, _p2dTfScaleY, _p2dTfRot;
        private FloatField    _p2dTfScaleAxis;

        // 回転/拡大縮小ハンドル（キャンバス上ドラッグ）
        private readonly Canvas2DHandle _p2dHandle = new Canvas2DHandle();
        private bool                    _p2dHandleDrag;
        private Canvas2DHandle.HandleType _p2dHandleType = Canvas2DHandle.HandleType.None;
        private readonly Dictionary<long, Vector2> _p2dHandleStart = new Dictionary<long, Vector2>();
        private readonly Dictionary<long, float>   _p2dHandleW     = new Dictionary<long, float>();
        private Vector2 _p2dHandleAnchorC;
        private float   _p2dHandlePrevAngle;
        private float   _p2dHandleTotalDeg;
        // 角処理(ベベル)UI 要素（Thickness/Segments に応じて表示切替）
        private VisualElement _p2dEdgeLabel, _p2dEdgeFrontSeg, _p2dEdgeFrontSize, _p2dEdgeBackSeg, _p2dEdgeBackSize, _p2dEdgeInward;

        // マグネット（比例編集、Phase）
        private readonly Canvas2DMagnet _p2dMagnet = new Canvas2DMagnet();
        private readonly Dictionary<long, Vector2> _p2dMagnetStart = new Dictionary<long, Vector2>();
        private readonly Dictionary<long, float>   _p2dMagnetW     = new Dictionary<long, float>();
        private Slider        _p2dMagnetRadius;
        private float         _p2dZoom    = 1f;
        private Vector2       _p2dOffset  = Vector2.zero;
        private VisualElement _p2dViewLayer;
        private bool          _p2dPanDrag;
        private Vector2       _p2dPanStart;
        private Vector2       _p2dPanOffsetStart;
        private int           _p2dHoverEL = -1;
        private int           _p2dHoverEI = -1;
        private string        _p2dCsvPath = "";
        // 下絵
        private Texture2D     _p2dBgTex;
        private VisualElement _p2dBgEl;
        private string        _p2dBgPath   = "";
        private Vector2       _p2dBgOffset = Vector2.zero;
        private float         _p2dBgScale  = 8f;    // 画像高さ(ワールド単位)
        private Vector2       _p2dBgOrigin = Vector2.zero; // 拡大縮小の原点（画像px, 既定=中心）
        private Slider        _p2dBgScaleSlider;
        private Label         _p2dBgSizeLabel;
        private float         _p2dBgAlpha  = 0.4f;
        private bool          _p2dBgMode   = false;
        private bool          _p2dBgDrag   = false;
        private Vector2       _p2dBgDragStart;
        private Vector2       _p2dBgOffsetOnDragStart;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent, Transform sceneRoot)
        {
            LoadShapeMemory();
            _cubeP.LinkTopBottom = true;
            _sectionEl = parent;
            parent.Clear();

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
                RefreshBakeToggleVis();
            });
            parent.Add(addModeDd);

            var poseFold = new Foldout { text = T("ObjectPose"), value = false };
            poseFold.style.marginTop    = 2;
            poseFold.style.marginBottom = 2;
            var poseFoldLabel = poseFold.Q<Label>();
            if (poseFoldLabel != null) poseFoldLabel.style.color = new StyleColor(Color.white);
            var pose = poseFold.contentContainer;

            pose.Add(SL(T("WorldPos")));
            pose.Add(V3FRef(T("WorldPosX"), T("WorldPosY"), T("WorldPosZ"),
                () => _worldPos.x, v => { _worldPos.x = v; D(); },
                () => _worldPos.y, v => { _worldPos.y = v; D(); },
                () => _worldPos.z, v => { _worldPos.z = v; D(); },
                _posFields));

            pose.Add(BakeHeaderRow(T("Rotation"), () => _bakeRotation, v => _bakeRotation = v,
                out _bakeRotToggle));
            pose.Add(V3FRef(T("RotX"), T("RotY"), T("RotZ"),
                () => _rotEuler.x, v => { _rotEuler.x = v; D(); },
                () => _rotEuler.y, v => { _rotEuler.y = v; D(); },
                () => _rotEuler.z, v => { _rotEuler.z = v; D(); },
                _rotFields));

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
                D();
            });
            pose.Add(trsResetRow);

            // 配置ギズモのサブモード切替。GizmoData は矢印 / リング / キューブを
            // 排他的にしか描画できないため、3種を同時には出さず切り替える。
            // 配置ギズモを持つのは 3D連携インスタンスだけなので、そちらにのみ出す。
            if (LiveWireInMainViewport)
            {
                pose.Add(SL(T("PlaceGizmo")));

                var placeGizmoRow = new VisualElement();
                placeGizmoRow.style.flexDirection = FlexDirection.Row;
                placeGizmoRow.style.marginBottom  = 2;

                _placeGizmoBtns = new Button[3];
                _placeGizmoBtns[0] = PGB(placeGizmoRow, T("PlaceGizmoMove"),
                    PrimitivePlaceToolHandler.PlaceGizmoMode.Move);
                _placeGizmoBtns[1] = PGB(placeGizmoRow, T("PlaceGizmoRotate"),
                    PrimitivePlaceToolHandler.PlaceGizmoMode.Rotate);
                _placeGizmoBtns[2] = PGB(placeGizmoRow, T("PlaceGizmoScale"),
                    PrimitivePlaceToolHandler.PlaceGizmoMode.Scale);
                _placeGizmoBtns[2].style.marginRight = 0;

                pose.Add(placeGizmoRow);
                RefreshPlaceGizmoButtons();
            }

            RefreshBakeToggleVis();

            var mergeToggle = new Toggle(T("MergeDuplicates")) { value = _mergeDuplicateVertices };
            mergeToggle.style.color = new StyleColor(Color.white);
            mergeToggle.RegisterValueChangedCallback(e => { _mergeDuplicateVertices = e.newValue; _dirty = true; });
            pose.Add(mergeToggle);

            parent.Add(poseFold);

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

        // ================================================================
        // プレビュー再生成 / カメラレンダー / Dispose
        // ================================================================

        /// <summary>
        /// パラメータ変更時 (_dirty=true) にメッシュとワイヤを再構築する。
        /// イベント駆動で呼んでもよいし、カメラレンダー前 (TickPreview) の
        /// 先頭で呼んでもよい。
        /// </summary>
        private void Regenerate()
        {
            if (_preview == null) return;
            if (!_dirty) return;
            // 適応スロットル：直前の生成コストに応じて再生成頻度を間引く。
            // スキップ時は _dirty を維持して後続フレームに回すため、最終値は必ず反映される。
            double t0 = Time.realtimeSinceStartupAsDouble;
            if (t0 < _nextGenAllowed) return;
            _dirty = false;
            try
            {
                var mo = Generate(true);
                _preview.SetMesh(mo);
                DestroyWire();
                if (mo != null) _wireMesh = BuildWire(mo);
            }
            catch { }
            double elapsed = Time.realtimeSinceStartupAsDouble - t0;
            _nextGenAllowed = Time.realtimeSinceStartupAsDouble + System.Math.Max(0.05, elapsed * 3.0);
        }

        /// <summary>
        /// プレビューカメラの描画を 1 回実行する。
        /// RenderPipelineManager.beginCameraRendering コールバックから
        /// (メインカメラ描画の直前に) 呼ばれる。毎フレームポーリングは
        /// 行わず、Unity のカメラレンダーループに寄り添う形で動かす。
        /// </summary>
        private void TickPreview()
        {
            if (_preview == null) return;
            Regenerate();
            _preview.Tick(_wireMesh);
            if (_previewEl != null && _preview.RT != null)
                _previewEl.style.backgroundImage = new StyleBackground(
                    Background.FromRenderTexture(_preview.RT));
        }

        /// <summary>
        /// URP の beginCameraRendering コールバック。
        /// プレビューカメラ自身 (_preview.Cam) および非 Game カメラは除外。
        /// メインカメラ描画の直前にプレビューを更新する。
        /// </summary>
        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null) return;
            if (cam.cameraType != CameraType.Game) return;
            if (_preview != null && cam == _preview.Cam) return;
            // 非表示のパネルは RT を誰も見ていないので Render しない。
            // 再表示時は _dirty が保持されているため、次の描画で最新値に追いつく。
            if (!IsSectionVisible()) return;

            // 先に再生成して _wireMesh を最新化し、そのうえで提出する。
            // この順序により、SubmitLiveWire は Graphics.DrawMesh 提出のみを行えばよい。
            TickPreview();
            SubmitLiveWire(cam);
        }

        /// <summary>このパネルのセクションが右ペインに表示されているか。</summary>
        private bool IsSectionVisible()
        {
            if (_sectionEl == null) return true;   // 判定材料が無い場合は従来どおり描画する
            return _sectionEl.resolvedStyle.display != DisplayStyle.None;
        }

        // ================================================================
        // メイン3Dウインドウへのライブワイヤ提出
        // ================================================================

        /// <summary>
        /// 生成予定形状の黄色ワイヤをメイン3Dウインドウのカメラへ提出する。
        /// ここでは Graphics.DrawMesh 提出のみを行い、メッシュ再構築は行わない
        /// （呼出し前の TickPreview で _wireMesh は最新化済み）。
        /// </summary>
        private void SubmitLiveWire(Camera cam)
        {
            if (!LiveWireInMainViewport) return;
            if (cam == null || _wireMesh == null) return;
            if (IsMainViewportCamera == null || !IsMainViewportCamera(cam)) return;

            EnsureLiveWireMaterial();
            if (_liveWireMat == null) return;

            Graphics.DrawMesh(_wireMesh, LiveWireMatrix(), _liveWireMat, 0, cam);
        }

        /// <summary>
        /// ライブワイヤの配置行列。回転・スケールは Generate() で頂点へ焼き込み済みのため、
        /// ここで扱うのは生成位置のみ。
        /// <para>
        /// NewObject / NewModel: 生成物は親を持たないルートとして WorldMatrix = Translate(_worldPos)
        /// になる（BuildPrimitiveMeshContext が BoneTransform.Position へ入れる）。
        /// </para>
        /// <para>
        /// AddToExisting: 追加先メッシュのローカル空間で頂点へ加算されるため、
        /// 追加先の WorldMatrix を左から掛ける。
        /// </para>
        /// </summary>
        private Matrix4x4 LiveWireMatrix()
        {
            // 穴つなぎのプレビュー頂点はワールド空間で作ってあるので、行列は掛けない。
            if (_current == ShapeKind.Bridge) return Matrix4x4.identity;

            var local = Matrix4x4.Translate(_worldPos);
            if (_addMode != PrimitiveAddMode.AddToExisting) return local;
            var parent = GetAddTargetWorldMatrix?.Invoke() ?? Matrix4x4.identity;
            return parent * local;
        }

        private void EnsureLiveWireMaterial()
        {
            if (_liveWireMat != null) return;

            var sh = Shader.Find("Hidden/Internal-Colored")
                  ?? Shader.Find("Unlit/Color");
            if (sh == null) return;

            _liveWireMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _liveWireMat.SetColor("_Color", new Color(1f, 0.92f, 0.2f, 1f));
            // 常に手前に描く。モデルに埋まっても生成位置が見えるようにする。
            _liveWireMat.SetInt("_ZTest",  (int)CompareFunction.Always);
            _liveWireMat.SetInt("_ZWrite", 0);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _preview?.Dispose();
            _preview = null;
            DestroyWire();
            if (_liveWireMat != null)
            {
                UnityEngine.Object.Destroy(_liveWireMat);
                _liveWireMat = null;
            }
        }

        // ================================================================
        // 図形選択
        // ================================================================

        // 現在カテゴリに属する図形リスト。
        private ShapeKind[] CurrentCategoryShapes()
            => _category == ShapeCategory.Advanced ? AdvancedShapes : BasicShapes;

        // 現在カテゴリの図形だけでボタングリッドを再構築する。
        // Build と SetCategory で共用（ボタン生成コードはここ1箇所）。
        private void PopulateShapeGrid()
        {
            if (_shapeGrid == null) return;
            _shapeGrid.Clear();
            for (int i = 0; i < _shapeBtns.Length; i++) _shapeBtns[i] = null;

            foreach (var kind in CurrentCategoryShapes())
            {
                int idx = (int)kind;
                var btn = new Button(() => Select((ShapeKind)idx)) { text = T(ShapeKeys[idx]) };
                btn.style.width = new StyleLength(new Length(33.3f, LengthUnit.Percent));
                btn.style.height = 26; btn.style.marginBottom = 2; btn.style.fontSize = 10;
                _shapeBtns[idx] = btn;
                _shapeGrid.Add(btn);
            }
        }

        /// <summary>
        /// カテゴリを切り替える。グリッドを再構築し、そのカテゴリ先頭の図形を選択する。
        /// 左ペインの「基本図形」/「高度な図形」ボタンから呼ぶ。
        /// </summary>
        public void SetCategory(ShapeCategory cat)
        {
            _category = cat;
            PopulateShapeGrid();
            Select(RememberedShape(cat));
        }

        /// <summary>
        /// 指定カテゴリで最後に選んだ図形。記憶値がそのカテゴリに属さない場合は
        /// カテゴリ先頭へフォールバックする。
        /// </summary>
        private ShapeKind RememberedShape(ShapeCategory cat)
        {
            var shapes = cat == ShapeCategory.Advanced ? AdvancedShapes : BasicShapes;
            var kind   = cat == ShapeCategory.Advanced ? _lastAdvanced : _lastBasic;
            if (System.Array.IndexOf(shapes, kind) >= 0) return kind;
            return shapes.Length > 0 ? shapes[0] : ShapeKind.Cube;
        }

        /// <summary>
        /// 保存済みの「最後に選んだ図形」を読み込む。MemoryKey が空のときは何もしない。
        /// </summary>
        private void LoadShapeMemory()
        {
            var basic = PrimitiveShapeMemory.Get(MemoryKey, ShapeCategory.Basic);
            if (basic.HasValue && System.Array.IndexOf(BasicShapes, basic.Value) >= 0)
                _lastBasic = basic.Value;

            var adv = PrimitiveShapeMemory.Get(MemoryKey, ShapeCategory.Advanced);
            if (adv.HasValue && System.Array.IndexOf(AdvancedShapes, adv.Value) >= 0)
                _lastAdvanced = adv.Value;
        }

        /// <summary>指定形状のカテゴリを返す。</summary>
        public ShapeCategory CategoryOf(ShapeKind k)
            => System.Array.IndexOf(AdvancedShapes, k) >= 0
                ? ShapeCategory.Advanced
                : ShapeCategory.Basic;

        /// <summary>
        /// 指定形状のサブメニューを開く (形状ボタンをクリックしたのと同じ)。
        /// 必要ならカテゴリを切り替えてグリッドを再構築してから選択する。生成はしない。
        /// </summary>
        public void SelectShape(ShapeKind k)
        {
            var cat = CategoryOf(k);
            if (_category != cat)
            {
                _category = cat;
                PopulateShapeGrid();
            }
            Select(k);
        }

        private void Select(ShapeKind k)
        {
            _current = k;

            // 選んだ図形をカテゴリ別に記憶する（次にパネルを開いたときの復元用）。
            var cat = CategoryOf(k);
            if (cat == ShapeCategory.Advanced) _lastAdvanced = k;
            else                               _lastBasic    = k;
            PrimitiveShapeMemory.Set(MemoryKey, cat, k);

            for (int i = 0; i < _shapeBtns.Length; i++)
            {
                if (_shapeBtns[i] == null) continue;
                _shapeBtns[i].style.backgroundColor = (int)k == i
                    ? new StyleColor(new Color(0.25f, 0.45f, 0.65f))
                    : new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            }
            // 名前欄を組み立てる前に非重複候補へ更新する。
            RefreshMeshNameCandidate();
            RebuildSettings();
            _dirty = true;
        }

        // ================================================================
        // 設定UI
        // ================================================================

        private void RebuildSettings()
        {
            // 旧 UI の TextField は破棄される。NF が呼ばれれば新しいものが入る。
            _nameField = null;

            _profileEditorContainer?.Clear();
            _settingsContainer?.Clear();
            switch (_current)
            {
                case ShapeKind.Cube:       BuildCubeUI(_settingsContainer);       break;
                case ShapeKind.Sphere:     BuildSphereUI(_settingsContainer);     break;
                case ShapeKind.Cylinder:   BuildCylinderUI(_settingsContainer);   break;
                case ShapeKind.Capsule:    BuildCapsuleUI(_settingsContainer);    break;
                case ShapeKind.Plane:      BuildPlaneUI(_settingsContainer);      break;
                case ShapeKind.Pyramid:    BuildPyramidUI(_settingsContainer);    break;
                case ShapeKind.Revolution: BuildRevolutionUI(_settingsContainer); break;
                case ShapeKind.Profile2D:  BuildProfile2DUI(_settingsContainer);  break;
                case ShapeKind.NohMask:    BuildNohMaskUI(_settingsContainer);    break;
                case ShapeKind.Frill:      BuildFrillUI(_settingsContainer);      break;
                case ShapeKind.Pipe:       BuildPipeUI(_settingsContainer);       break;
                case ShapeKind.PlaceObject: BuildPlaceObjectUI(_settingsContainer); break;
                case ShapeKind.ObjectArray: BuildObjectArrayUI(_settingsContainer); break;
                case ShapeKind.Text:        BuildTextUI(_settingsContainer);        break;
                case ShapeKind.Bridge:      BuildBridgeUI(_settingsContainer);      break;
                default:
                    var lbl = new Label(T("NotSupported"));
                    lbl.style.color = new StyleColor(new Color(0.8f, 0.5f, 0.3f));
                    lbl.style.whiteSpace = WhiteSpace.Normal;
                    _settingsContainer?.Add(lbl);
                    break;
            }
            PlayerLayoutRoot.ApplyDarkTheme(_settingsContainer);
            if (_profileEditorContainer != null)
                PlayerLayoutRoot.ApplyDarkTheme(_profileEditorContainer);

            RefreshCreateButtonState();
        }

        private void D()
        {
            _dirty = true;
            RefreshCreateButtonState();
        }

        private void BuildCubeUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Cube")));
            c.Add(NF(() => _cubeP.MeshName, v => _cubeP.MeshName = v));

            c.Add(TR(T("LinkWHD"), () => _cubeP.LinkWHD, v => { _cubeP.LinkWHD = v; D(); }));

            c.Add(SL(T("Size")));
            if (_cubeP.LinkWHD)
            {
                c.Add(SR(T("WidthX"), 0.1f, 10f, () => _cubeP.WidthTop, v =>
                {
                    _cubeP.WidthTop = _cubeP.WidthBottom = _cubeP.DepthTop = _cubeP.DepthBottom = _cubeP.Height = v; D();
                }));
            }
            else
            {
                c.Add(SR(T("WidthX"),  0.1f, 10f, () => _cubeP.WidthTop, v => { _cubeP.WidthTop  = v; _cubeP.WidthBottom  = v; D(); }));
                c.Add(SR(T("HeightY"), 0.1f, 10f, () => _cubeP.Height,   v => { _cubeP.Height = v; D(); }));
                c.Add(SR(T("DepthZ"),  0.1f, 10f, () => _cubeP.DepthTop, v => { _cubeP.DepthTop  = v; _cubeP.DepthBottom  = v; D(); }));
            }

            c.Add(SL(T("CornerRadius")));
            c.Add(SR(T("CornerRadius"), 0f, 0.5f, () => _cubeP.CornerRadius, v => { _cubeP.CornerRadius = v; D(); }));
            if (_cubeP.CornerRadius > 0f)
                c.Add(IR(T("CornerSeg"), 1, 8, () => _cubeP.CornerSegments, v => { _cubeP.CornerSegments = v; D(); }));

            c.Add(SL(T("Subdivisions")));
            c.Add(IR(T("SubdivX"), 1, 8, () => _cubeP.Subdivisions.x, v => { _cubeP.Subdivisions = new Vector3Int(v, _cubeP.Subdivisions.y, _cubeP.Subdivisions.z); D(); }));
            c.Add(IR(T("SubdivY"), 1, 8, () => _cubeP.Subdivisions.y, v => { _cubeP.Subdivisions = new Vector3Int(_cubeP.Subdivisions.x, v, _cubeP.Subdivisions.z); D(); }));
            c.Add(IR(T("SubdivZ"), 1, 8, () => _cubeP.Subdivisions.z, v => { _cubeP.Subdivisions = new Vector3Int(_cubeP.Subdivisions.x, _cubeP.Subdivisions.y, v); D(); }));

            BuildPivotXYZ(c,
                () => _cubeP.Pivot, v => { _cubeP.Pivot = v; D(); },
                -0.5f, 0.5f,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

        }

        private void BuildSphereUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Sphere")));
            c.Add(NF(() => _sphereP.MeshName, v => _sphereP.MeshName = v));
            c.Add(SR(T("Radius"), 0.05f, 5f, () => _sphereP.Radius, v => { _sphereP.Radius = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Lateral"), 4, 64, () => _sphereP.LatitudeSegments,  v => { _sphereP.LatitudeSegments  = v; D(); }));
            c.Add(IR(T("Radial"),  4, 64, () => _sphereP.LongitudeSegments, v => { _sphereP.LongitudeSegments = v; D(); }));
            c.Add(TR(T("CubeSphere"), () => _sphereP.CubeSphere, v => { _sphereP.CubeSphere = v; D(); }));

            BuildPivotXYZ(c,
                () => _sphereP.Pivot, v => { _sphereP.Pivot = v; D(); },
                -0.5f, 0.5f,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

        }

        private void BuildCylinderUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Cylinder")));
            c.Add(NF(() => _cylP.MeshName, v => _cylP.MeshName = v));
            c.Add(SL(T("Size")));
            c.Add(SR(T("RadiusTop"),    0f,   5f,   () => _cylP.RadiusTop,    v => { _cylP.RadiusTop    = v; D(); }));
            c.Add(SR(T("RadiusBottom"), 0f,   5f,   () => _cylP.RadiusBottom, v => { _cylP.RadiusBottom = v; D(); }));
            c.Add(SR(T("Height"),       0.1f, 10f,  () => _cylP.Height,       v => { _cylP.Height       = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Radial"),  3, 48, () => _cylP.RadialSegments, v => { _cylP.RadialSegments = v; D(); }));
            c.Add(IR(T("Lateral"), 1, 16, () => _cylP.HeightSegments, v => { _cylP.HeightSegments = v; D(); }));
            c.Add(TR(T("CapTop"),    () => _cylP.CapTop,    v => { _cylP.CapTop    = v; D(); }));
            c.Add(TR(T("CapBottom"), () => _cylP.CapBottom, v => { _cylP.CapBottom = v; D(); }));

            float maxEdge = _cylP.Height * 0.5f;
            if (_cylP.CapTop    && _cylP.RadiusTop    > 0) maxEdge = Mathf.Min(maxEdge, _cylP.RadiusTop);
            if (_cylP.CapBottom && _cylP.RadiusBottom > 0) maxEdge = Mathf.Min(maxEdge, _cylP.RadiusBottom);
            if (maxEdge > 0f)
            {
                c.Add(SR(T("EdgeRadius"), 0f, maxEdge, () => _cylP.EdgeRadius, v => { _cylP.EdgeRadius = v; D(); }));
                if (_cylP.EdgeRadius > 0f)
                    c.Add(IR(T("EdgeSeg"), 1, 16, () => _cylP.EdgeSegments, v => { _cylP.EdgeSegments = v; D(); }));
            }

            BuildPivotY(c,
                () => _cylP.Pivot.y, v => { _cylP.Pivot = new Vector3(0, v, 0); D(); },
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _, out _);

        }

        private void BuildCapsuleUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Capsule")));
            c.Add(NF(() => _capsP.MeshName, v => _capsP.MeshName = v));
            c.Add(SL(T("Size")));
            c.Add(SR(T("RadiusTop"),    0.1f, 2f,  () => _capsP.RadiusTop,    v => { _capsP.RadiusTop    = v; D(); }));
            c.Add(SR(T("RadiusBottom"), 0.1f, 2f,  () => _capsP.RadiusBottom, v => { _capsP.RadiusBottom = v; D(); }));
            c.Add(SR(T("Height"),       0.5f, 10f, () => _capsP.Height,       v => { _capsP.Height       = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Radial"),  8, 48, () => _capsP.RadialSegments, v => { _capsP.RadialSegments = v; D(); }));
            c.Add(IR(T("Lateral"), 1, 16, () => _capsP.HeightSegments, v => { _capsP.HeightSegments = v; D(); }));
            c.Add(IR(T("Cap"),     2, 16, () => _capsP.CapSegments,    v => { _capsP.CapSegments    = v; D(); }));

            BuildPivotY(c,
                () => _capsP.Pivot.y, v => { _capsP.Pivot = new Vector3(0, v, 0); D(); },
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0),
                out var capsPivotSync, out var capsPivotContent);

            // 上球・下球の重心ピボット
            var sphereRow = new VisualElement();
            sphereRow.style.flexDirection = FlexDirection.Row;
            sphereRow.style.marginBottom  = 4;
            SB(sphereRow, T("UpperSphere"), () =>
            {
                float halfH     = _capsP.Height * 0.5f;
                float cylTop    = halfH - _capsP.RadiusTop;
                float normalized = _capsP.Height > 0f ? cylTop / _capsP.Height : 0f;
                _capsP.Pivot = new Vector3(0, normalized, 0); D(); capsPivotSync?.Invoke();
            });
            SB(sphereRow, T("LowerSphere"), () =>
            {
                float halfH      = _capsP.Height * 0.5f;
                float cylBottom  = -halfH + _capsP.RadiusBottom;
                float normalized = _capsP.Height > 0f ? cylBottom / _capsP.Height : 0f;
                _capsP.Pivot = new Vector3(0, normalized, 0); D(); capsPivotSync?.Invoke();
            });
            capsPivotContent.Add(sphereRow);

        }

        private void BuildPlaneUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Plane")));
            c.Add(NF(() => _planeP.MeshName, v => _planeP.MeshName = v));
            c.Add(SR(T("Width"),  0.1f, 10f, () => _planeP.Width,  v => { _planeP.Width  = v; D(); }));
            c.Add(SR(T("Height"), 0.1f, 10f, () => _planeP.Height, v => { _planeP.Height = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Width"),  1, 32, () => _planeP.WidthSegments,  v => { _planeP.WidthSegments  = v; D(); }));
            c.Add(IR(T("Height"), 1, 32, () => _planeP.HeightSegments, v => { _planeP.HeightSegments = v; D(); }));
            var dd = new DropdownField(new List<string>{ T("PlaneXY"), T("PlaneXZ"), T("PlaneYZ") }, (int)_planeP.Orientation);
            dd.label = T("Orientation"); dd.style.marginBottom = 2;
            dd.RegisterValueChangedCallback(e => { _planeP.Orientation = (PlaneOrientation)dd.index; D(); });
            c.Add(dd);
            c.Add(TR(T("FaceFront"),   () => _planeP.FaceFront,   v => { _planeP.FaceFront   = v; D(); }));
            c.Add(TR(T("DoubleSided"), () => _planeP.DoubleSided, v => { _planeP.DoubleSided = v; D(); }));

            BuildPivotXYZ(c,
                () => _planeP.Pivot, v => { _planeP.Pivot = v; D(); },
                -0.5f, 0.5f,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

        }

        private void BuildPyramidUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Pyramid")));
            c.Add(NF(() => _pyramidP.MeshName, v => _pyramidP.MeshName = v));
            c.Add(IR(T("Sides"),      3, 16,     () => _pyramidP.Sides,       v => { _pyramidP.Sides       = v; D(); }));
            c.Add(SR(T("BaseRadius"), 0.1f, 5f,  () => _pyramidP.BaseRadius,  v => { _pyramidP.BaseRadius  = v; D(); }));
            c.Add(SR(T("Height"),     0.1f, 10f, () => _pyramidP.Height,      v => { _pyramidP.Height      = v; D(); }));
            c.Add(SR(T("ApexOffset"), -1f,  1f,  () => _pyramidP.ApexOffset,  v => { _pyramidP.ApexOffset  = v; D(); }));
            c.Add(TR(T("CapBottom"),  () => _pyramidP.CapBottom, v => { _pyramidP.CapBottom = v; D(); }));

            BuildPivotXYZ(c,
                () => _pyramidP.Pivot, v => { _pyramidP.Pivot = v; D(); },
                -0.5f, 0.5f,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

        }

        // ================================================================
        // ピボットUIヘルパー
        // ================================================================

        /// <summary>
        /// 現在のパラメータで生成したメッシュから「重心を原点へ寄せるためのピボット増分」を返す。
        ///
        /// 全ジェネレータは「ピボット p で頂点を -p * S だけ平行移動する（S = 形状サイズ）」規約のため、
        /// 現在の重心 c に対して Δp = c / S を足せば重心が原点へ来る。S は生成メッシュの AABB サイズで測る。
        /// 姿勢（回転・スケール）は焼き込まずに測るので、姿勢を変えても結果は変わらない。
        /// 生成できない図形（穴つなぎ・歪み複製など）では null を返す。
        /// </summary>
        private Vector3? PivotDeltaToCentroid()
        {
            var mo = Generate(true, false);
            if (mo == null || mo.Vertices == null || mo.Vertices.Count == 0) return null;

            Vector3 min = mo.Vertices[0].Position, max = min, sum = Vector3.zero;
            foreach (var v in mo.Vertices)
            {
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
                sum += v.Position;
            }
            Vector3 c = sum / mo.Vertices.Count;
            Vector3 s = max - min;

            return new Vector3(
                Mathf.Abs(s.x) > 1e-6f ? c.x / s.x : 0f,
                Mathf.Abs(s.y) > 1e-6f ? c.y / s.y : 0f,
                Mathf.Abs(s.z) > 1e-6f ? c.z / s.z : 0f);
        }

        /// <summary>
        /// 見出し付きの折り畳みセクションを作り、中身を入れるコンテナを返す。
        /// 3つの2Dプロファイル編集（回転体・2D押し出し・はしご断面）で共用する。
        /// </summary>
        private static VisualElement FoldSection(VisualElement parent, string title, bool open)
        {
            var f = new Foldout { text = title, value = open };
            f.style.marginBottom = 4;
            parent.Add(f);
            return f.contentContainer;
        }

        private void BuildPivotY(VisualElement c,
            Func<float> getY, Action<float> setY,
            Vector3 bottom, Vector3 center, Vector3 top,
            out Action sync, out VisualElement content)
        {
            var fold = new Foldout { text = T("PivotOffset"), value = false };
            fold.style.marginBottom = 4;
            var f = fold.contentContainer;
            content = f;
            f.Add(SR(T("PivotY"), -0.5f, 0.5f, getY, setY, out var ySl, out var yNf));
            void SyncY() { float v = getY(); ySl.SetValueWithoutNotify(v); yNf.SetValueWithoutNotify((float)Math.Round(v, 3)); }
            sync = SyncY;
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 4;
            SB(row, T("Bottom"), () => { setY(bottom.y); SyncY(); });
            SB(row, T("Center"), () => { setY(center.y); SyncY(); });
            SB(row, T("Top"),    () => { setY(top.y);    SyncY(); });
            SB(row, T("PivotCentroid"), () =>
            {
                var d = PivotDeltaToCentroid();
                if (!d.HasValue) return;
                setY(getY() + d.Value.y); SyncY();
            });
            f.Add(row);
            c.Add(fold);
        }

        private void BuildPivotXYZ(VisualElement c,
            Func<Vector3> get, Action<Vector3> set,
            float min, float max,
            Vector3 bottom, Vector3 center, Vector3 top,
            out VisualElement content)
        {
            var fold = new Foldout { text = T("PivotOffset"), value = false };
            fold.style.marginBottom = 4;
            var f = fold.contentContainer;
            content = f;
            f.Add(SR(T("PivotX"), min, max, () => get().x, v => { var p = get(); set(new Vector3(v, p.y, p.z)); }, out var xSl, out var xNf));
            f.Add(SR(T("PivotY"), min, max, () => get().y, v => { var p = get(); set(new Vector3(p.x, v, p.z)); }, out var ySl, out var yNf));
            f.Add(SR(T("PivotZ"), min, max, () => get().z, v => { var p = get(); set(new Vector3(p.x, p.y, v)); }, out var zSl, out var zNf));
            void SyncXYZ()
            {
                var p = get();
                xSl.SetValueWithoutNotify(p.x); xNf.SetValueWithoutNotify((float)Math.Round(p.x, 3));
                ySl.SetValueWithoutNotify(p.y); yNf.SetValueWithoutNotify((float)Math.Round(p.y, 3));
                zSl.SetValueWithoutNotify(p.z); zNf.SetValueWithoutNotify((float)Math.Round(p.z, 3));
            }
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 4;
            SB(row, T("Bottom"), () => { set(bottom); SyncXYZ(); });
            SB(row, T("Center"), () => { set(center); SyncXYZ(); });
            SB(row, T("Top"),    () => { set(top);    SyncXYZ(); });
            SB(row, T("PivotCentroid"), () =>
            {
                var d = PivotDeltaToCentroid();
                if (!d.HasValue) return;
                set(get() + d.Value); SyncXYZ();
            });
            f.Add(row);
            c.Add(fold);
        }

        // ================================================================
        // Revolution UI
        // ================================================================

        private void EnsureRevProfile()
        {
            if (_revProfile == null)
                _revProfile = RevolutionProfileGenerator.CreateDefault();
        }

        private void BuildRevolutionUI(VisualElement c)
        {
            EnsureRevProfile();

            // ドラッグ状態をリセット（タブ切替後の再Build時）
            _revDrag = false; _revDragIdx = -1; _revHoverEI = -1;

            c.Add(ShapeTitle(T("Revolution")));
            c.Add(NF(() => _revP.MeshName, v => _revP.MeshName = v));
            c.Add(IR(T("RadialSegments"), 3, 64,   () => _revP.RadialSegments, v => { _revP.RadialSegments = v; D(); }));
            c.Add(TR(T("CloseTop"),    () => _revP.CloseTop,    v => { _revP.CloseTop    = v; D(); }));
            c.Add(TR(T("CloseBottom"), () => _revP.CloseBottom, v => { _revP.CloseBottom = v; D(); }));
            c.Add(TR(T("CloseLoop"),   () => _revP.CloseLoop,   v => { _revP.CloseLoop   = v; D(); RefreshRevCanvas(); }));
            c.Add(TR(T("Spiral"),      () => _revP.Spiral,      v => { _revP.Spiral      = v; D(); }));
            if (_revP.Spiral)
            {
                c.Add(IR(T("SpiralTurns"), 1, 10,   () => _revP.SpiralTurns, v => { _revP.SpiralTurns = v; D(); }));
                c.Add(SR(T("SpiralPitch"), -2f, 2f, () => _revP.SpiralPitch, v => { _revP.SpiralPitch = v; D(); }));
            }
            c.Add(TR(T("FlipY"), () => _revP.FlipY, v => { _revP.FlipY = v; D(); }));
            c.Add(TR(T("FlipZ"), () => _revP.FlipZ, v => { _revP.FlipZ = v; D(); }));

            // ── プリセット ──────────────────────────────────────────────
            c.Add(SL(T("Preset")));
            var presetChoices = new List<string>
                { T("Custom"), T("Donut"), T("RoundedPipe"), T("Vase"), T("Goblet"), T("Bell"), T("Hourglass") };
            var presetEnum = new ProfilePreset[]
                { ProfilePreset.Custom, ProfilePreset.Donut, ProfilePreset.RoundedPipe,
                  ProfilePreset.Vase,   ProfilePreset.Goblet, ProfilePreset.Bell, ProfilePreset.Hourglass };
            var presetDd = new DropdownField(null, presetChoices, 0);
            presetDd.style.marginBottom = 3;
            presetDd.RegisterValueChangedCallback(e =>
            {
                int idx = presetChoices.IndexOf(e.newValue);
                if (idx < 0) return;
                _revP.CurrentPreset = presetEnum[idx];
                if (_revP.CurrentPreset != ProfilePreset.Custom)
                {
                    RevBegin();
                    _revProfile = RevolutionProfileGenerator.CreatePreset(_revP.CurrentPreset, ref _revP);
                    _revSel.Clear(); _revSelIdx = -1;
                    RevCommit("プリセット適用");
                }
                D(); RefreshRevCanvas(); RefreshRevPointUI();
            });
            c.Add(presetDd);

            if (_revP.CurrentPreset == ProfilePreset.Donut)
            {
                c.Add(SL(T("Donut")));
                c.Add(SR(T("DonutMajorRadius"), 0.2f, 2f,   () => _revP.DonutMajorRadius, v => { _revP.DonutMajorRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("DonutMinorRadius"), 0.05f, 1f,  () => _revP.DonutMinorRadius, v => { _revP.DonutMinorRadius = v; ApplyRevPreset(); }));
                c.Add(IR(T("DonutTubeSegs"),    4, 32,      () => _revP.DonutTubeSegments, v => { _revP.DonutTubeSegments = v; ApplyRevPreset(); }));
            }
            if (_revP.CurrentPreset == ProfilePreset.RoundedPipe)
            {
                c.Add(SL(T("RoundedPipe")));
                c.Add(SR(T("PipeInnerRadius"), 0.05f, 2f, () => _revP.PipeInnerRadius, v => { _revP.PipeInnerRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("PipeOuterRadius"), 0.06f, 3f, () => _revP.PipeOuterRadius, v => { _revP.PipeOuterRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("PipeHeight"),      0.1f,  3f, () => _revP.PipeHeight,      v => { _revP.PipeHeight      = v; ApplyRevPreset(); }));
                c.Add(SL(T("InnerCorner")));
                c.Add(SR(T("CornerRadius"), 0f, 0.5f, () => _revP.PipeInnerCornerRadius,  v => { _revP.PipeInnerCornerRadius  = v; ApplyRevPreset(); }));
                c.Add(IR(T("CornerSeg"),    1, 16,    () => _revP.PipeInnerCornerSegments, v => { _revP.PipeInnerCornerSegments = v; ApplyRevPreset(); }));
                c.Add(SL(T("OuterCorner")));
                c.Add(SR(T("CornerRadius"), 0f, 0.5f, () => _revP.PipeOuterCornerRadius,  v => { _revP.PipeOuterCornerRadius  = v; ApplyRevPreset(); }));
                c.Add(IR(T("CornerSeg"),    1, 16,    () => _revP.PipeOuterCornerSegments, v => { _revP.PipeOuterCornerSegments = v; ApplyRevPreset(); }));
            }

            BuildPivotY(c,
                () => _revP.Pivot.y, v => { _revP.Pivot = new Vector3(0, v, 0); D(); },
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _, out _);

            // ── プロファイルエディタ ──────────────────────────────────────
            var pe = _profileEditorContainer;
            pe.Add(SL(T("ProfileEditor")));

            // インタラクティブキャンバス
            _revCanvas = new VisualElement();
            _revCanvas.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            _revCanvas.style.height          = _profileHeight;
            _revCanvas.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.15f));
            _revCanvas.style.marginBottom    = 4;
            _revCanvas.style.borderTopWidth  = _revCanvas.style.borderBottomWidth =
            _revCanvas.style.borderLeftWidth = _revCanvas.style.borderRightWidth  = 1;
            _revCanvas.style.borderTopColor  = _revCanvas.style.borderBottomColor =
            _revCanvas.style.borderLeftColor = _revCanvas.style.borderRightColor  =
                new StyleColor(new Color(0.4f, 0.4f, 0.45f));
            _revCanvas.style.overflow        = Overflow.Hidden;
            _revCanvas.pickingMode           = PickingMode.Position;

            // 下絵レイヤー（ビューレイヤー配下。プロファイルと同じ view 変換で追従）
            _revViewLayer = new VisualElement();
            _revViewLayer.style.position = Position.Absolute;
            _revViewLayer.style.left = _revViewLayer.style.top =
            _revViewLayer.style.right = _revViewLayer.style.bottom = 0;
            _revViewLayer.pickingMode = PickingMode.Ignore;

            _revBgEl = new VisualElement();
            _revBgEl.style.position = Position.Absolute;
            _revBgEl.style.display  = DisplayStyle.None;
            _revBgEl.pickingMode    = PickingMode.Ignore;
            _revViewLayer.Add(_revBgEl);
            _revCanvas.Add(_revViewLayer);

            _revCanvas.generateVisualContent += OnDrawProfileCanvas;
            _revCanvas.RegisterCallback<PointerDownEvent>(OnRevCanvasPointerDown);
            _revCanvas.RegisterCallback<PointerMoveEvent>(OnRevCanvasPointerMove);
            _revCanvas.RegisterCallback<PointerUpEvent>(OnRevCanvasPointerUp);
            _revCanvas.RegisterCallback<WheelEvent>(e =>
            {
                if (_revBgMode)
                {
                    // サブモード中は下絵スケール。
                    _revBgScale = Mathf.Clamp(_revBgScale * (1f - e.delta.y * 0.05f), 0.1f, 10f);
                    _revBgScaleSlider?.SetValueWithoutNotify(_revBgScale);
                    UpdateRevBgEl(); RefreshRevCanvas();
                }
                else
                {
                    // 通常モードはプロファイルビューをカーソル基準でズーム。
                    float w = _revCanvas.resolvedStyle.width, h = _revCanvas.resolvedStyle.height;
                    float oldZoom = _revZoom;
                    float newZoom = Mathf.Clamp(oldZoom * (1f - e.delta.y * 0.05f), 0.2f, 8f);
                    if (newZoom != oldZoom)
                    {
                        var m = (Vector2)e.localMousePosition;
                        var center = new Vector2(w * 0.5f, h * 0.5f);
                        float k = newZoom / oldZoom;
                        _revOffset = (m - center) * (1f - k) + _revOffset * k;
                        _revZoom   = newZoom;
                        UpdateRevView(); UpdateRevBgEl(); RefreshRevCanvas();
                    }
                }
                e.StopPropagation();
            });
            _revCanvas.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateRevBgEl(); UpdateRevView(); RefreshRevCanvas();
            });
            pe.Add(_revCanvas);

            // キャンバス縦リサイズハンドル
            AddProfileResizeHandle(pe, _revCanvas, RefreshRevCanvas);

            // ボタン行: 削除 / リセット
            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginBottom = 4;
            SB(btnRow, T("DeletePoint"), () =>
            {
                EnsureRevProfile();
                RevBegin();
                if (_revSel.Count > 0)
                {
                    // 選択点を一括削除（インデックス降順・最小2点は維持）。
                    var idxs = new List<int>(_revSel);
                    idxs.Sort(); idxs.Reverse();
                    foreach (var idx in idxs)
                        if (idx >= 0 && idx < _revProfile.Count && _revProfile.Count > 2)
                            _revProfile.RemoveAt(idx);
                    _revSel.Clear(); _revSelIdx = -1;
                }
                else
                {
                    RevolutionProfileEditCore.RemovePoint(_revProfile, ref _revSelIdx);
                }
                _revP.CurrentPreset = ProfilePreset.Custom;
                RevCommit("点削除");
                D(); RefreshRevCanvas(); RefreshRevPointUI();
            });
            SB(btnRow, T("ClearProfile"), () =>
            {
                EnsureRevProfile();
                RevBegin();
                RevolutionProfileEditCore.ResetProfile(_revProfile, ref _revSelIdx);
                _revSel.Clear();
                _revP.CurrentPreset = ProfilePreset.Custom;
                RevCommit("プロファイルリセット");
                D(); RefreshRevCanvas(); RefreshRevPointUI();
            });
            pe.Add(btnRow);

            // ここから下を1つの大フォールドにまとめ、中の各セクションも個別に折り畳む。
            pe = FoldSection(pe, T("EditTools"), true);

            // ビュー操作行（3エディタ共通の並び：ビュー初期化 / 投げ縄 / ギズモ）
            var revViewRow = new VisualElement();
            revViewRow.style.flexDirection = FlexDirection.Row;
            revViewRow.style.marginBottom  = 3;
            SB(revViewRow, T("ResetView"), () =>
            {
                _revZoom = 1f; _revOffset = Vector2.zero;
                UpdateRevView(); UpdateRevBgEl(); RefreshRevCanvas();
            });
            var revLassoToggle = new Toggle(T("LassoMode")) { value = _revLassoMode };
            revLassoToggle.style.marginLeft = 4;
            revLassoToggle.RegisterValueChangedCallback(ev => _revLassoMode = ev.newValue);
            revViewRow.Add(revLassoToggle);
            var revGizmoToggle = new Toggle(T("ShowGizmo")) { value = _revShowGizmo };
            revGizmoToggle.style.marginLeft = 8;
            revGizmoToggle.RegisterValueChangedCallback(ev => { _revShowGizmo = ev.newValue; RefreshRevCanvas(); });
            revViewRow.Add(revGizmoToggle);
            pe.Add(revViewRow);

            BuildRevAnchorTransformUI(pe);

            // ── 下絵セクション ─────────────────────────────────────────────
            BuildBgSection(pe,
                T("BgImage"),
                () => _revBgPath, v => _revBgPath = v,
                () => _revBgAlpha, v => { _revBgAlpha = v; UpdateRevBgEl(); },
                () => _revBgMode,  v => { _revBgMode  = v; },
                () => _revBgScale, v => { _revBgScale = Mathf.Clamp(v, 0.1f, 10f); UpdateRevBgEl(); RefreshRevCanvas(); },
                () => _revBgOrigin, v => { _revBgOrigin = v; UpdateRevBgEl(); RefreshRevCanvas(); },
                () => _revBgTex,
                () => // Load
                {
                    if (string.IsNullOrEmpty(_revBgPath)) return;
                    LoadBgTexture(_revBgPath, ref _revBgTex, _revBgEl);
                    _revBgOffset = Vector2.zero; _revBgScale = 3f;
                    if (_revBgTex != null)
                        _revBgOrigin = new Vector2(_revBgTex.width * 0.5f, _revBgTex.height * 0.5f);
                    _revBgScaleSlider?.SetValueWithoutNotify(1f);
                    SetBgSizeLabel(_revBgSizeLabel, _revBgTex);
                    UpdateRevBgEl();
                },
                () => // Clear
                {
                    _revBgTex = null;
                    _revBgEl.style.display = DisplayStyle.None;
                    _revBgEl.style.backgroundImage = new StyleBackground();
                    SetBgSizeLabel(_revBgSizeLabel, null);
                },
                out _revBgScaleSlider, out _revBgSizeLabel);

            // 選択点スライダー（Floatフィールド付き、非選択時は非表示）
            _revPtRow = new VisualElement(); _revPtRow.style.marginBottom = 4;
            _revPtLabel = new Label(""); _revPtLabel.style.fontSize = 9; _revPtLabel.style.marginBottom = 1;
            _revPtRow.Add(_revPtLabel);

            // X (radius)
            {
                Slider    xSl = new Slider(0f, 2f); xSl.style.flexGrow = 1;
                FloatField xFf = new FloatField { value = 0f }; xFf.style.width = 42;
                xSl.RegisterValueChangedCallback(e =>
                {
                    if (_revSelIdx < 0 || _revProfile == null || _revSelIdx >= _revProfile.Count) return;
                    xFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    _revProfile[_revSelIdx] = new Vector2(e.newValue, _revProfile[_revSelIdx].y);
                    _revP.CurrentPreset = ProfilePreset.Custom; D(); RefreshRevCanvas();
                });
                xSl.RegisterCallback<PointerDownEvent>(_ => RevBegin());
                xSl.RegisterCallback<PointerUpEvent>(_ => RevCommit("点X編集"));
                xFf.RegisterValueChangedCallback(e =>
                {
                    if (_revSelIdx < 0 || _revProfile == null || _revSelIdx >= _revProfile.Count) return;
                    RevBegin();
                    float v = Mathf.Clamp(e.newValue, 0f, 2f);
                    xSl.SetValueWithoutNotify(v);
                    _revProfile[_revSelIdx] = new Vector2(v, _revProfile[_revSelIdx].y);
                    _revP.CurrentPreset = ProfilePreset.Custom; D(); RefreshRevCanvas();
                    RevCommit("点X編集");
                });
                var xRow = new VisualElement(); xRow.style.flexDirection = FlexDirection.Row; xRow.style.marginBottom = 2;
                xRow.Add(ML("R (X)")); xRow.Add(xSl); xRow.Add(xFf);
                _revPtRow.Add(xRow);
                _revPtXSlider = xSl; _revPtXField = xFf;
            }
            // Y (height)
            {
                Slider    ySl = new Slider(-1f, 2f); ySl.style.flexGrow = 1;
                FloatField yFf = new FloatField { value = 0f }; yFf.style.width = 42;
                ySl.RegisterValueChangedCallback(e =>
                {
                    if (_revSelIdx < 0 || _revProfile == null || _revSelIdx >= _revProfile.Count) return;
                    yFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    _revProfile[_revSelIdx] = new Vector2(_revProfile[_revSelIdx].x, e.newValue);
                    _revP.CurrentPreset = ProfilePreset.Custom; D(); RefreshRevCanvas();
                });
                ySl.RegisterCallback<PointerDownEvent>(_ => RevBegin());
                ySl.RegisterCallback<PointerUpEvent>(_ => RevCommit("点Y編集"));
                yFf.RegisterValueChangedCallback(e =>
                {
                    if (_revSelIdx < 0 || _revProfile == null || _revSelIdx >= _revProfile.Count) return;
                    RevBegin();
                    float v = Mathf.Clamp(e.newValue, -1f, 2f);
                    ySl.SetValueWithoutNotify(v);
                    _revProfile[_revSelIdx] = new Vector2(_revProfile[_revSelIdx].x, v);
                    _revP.CurrentPreset = ProfilePreset.Custom; D(); RefreshRevCanvas();
                    RevCommit("点Y編集");
                });
                var yRow = new VisualElement(); yRow.style.flexDirection = FlexDirection.Row; yRow.style.marginBottom = 2;
                yRow.Add(ML("Y")); yRow.Add(ySl); yRow.Add(yFf);
                _revPtRow.Add(yRow);
                _revPtYSlider = ySl; _revPtYField = yFf;
            }
            _revPtRow.style.display = DisplayStyle.None;
            pe.Add(_revPtRow);

            // CSV 読み書き
            var revCsvFold = FoldSection(pe, T("ProfileCsvSection"), false);
            var csvPathField = new TextField();
            csvPathField.RegisterValueChangedCallback(e => { _revCsvPath = e.newValue; RecentPaths.Set(RevCsvKey, e.newValue); });
            // PMX読込と同じ操作感：[...] も「読込」も必ずダイアログを出す。
            void LoadRevCsv()
            {
                string sel = PlayerIoUiKit.AskLoadPath(T("LoadCSV"), _revCsvPath, "csv");
                if (string.IsNullOrEmpty(sel)) return;
                _revCsvPath = sel;
                csvPathField.value = _revCsvPath;

                var result = RevolutionCSVIO.Load(_revCsvPath, _revP);
                if (result.Success)
                {
                    RevBegin();
                    _revProfile           = result.Profile;
                    _revP.RadialSegments  = result.RadialSegments;
                    _revP.CloseTop        = result.CloseTop;
                    _revP.CloseBottom     = result.CloseBottom;
                    _revP.CloseLoop       = result.CloseLoop;
                    _revP.Spiral          = result.Spiral;
                    _revP.Pivot           = new Vector3(0, result.PivotY, 0);
                    _revP.SpiralTurns     = result.SpiralTurns;
                    _revP.SpiralPitch     = result.SpiralPitch;
                    _revP.FlipY           = result.FlipY;
                    _revP.FlipZ           = result.FlipZ;
                    _revP.CurrentPreset   = ProfilePreset.Custom;
                    _revSel.Clear(); _revSelIdx = -1;
                    RevCommit("CSV読込");
                    D(); RefreshRevCanvas(); RefreshRevPointUI();
                }
                else
                {
                    Debug.LogWarning($"[Revolution CSV] {result.ErrorMessage}");
                }
            }

            revCsvFold.Add(PlayerIoUiKit.PathRow(csvPathField, LoadRevCsv));
            if (string.IsNullOrEmpty(_revCsvPath)) _revCsvPath = RecentPaths.Get(RevCsvKey);
            if (!string.IsNullOrEmpty(_revCsvPath)) csvPathField.SetValueWithoutNotify(_revCsvPath);

            revCsvFold.Add(PlayerIoUiKit.WideBtn(T("LoadCSV"), LoadRevCsv));
            revCsvFold.Add(PlayerIoUiKit.WideBtn(T("SaveCSV"), () =>
            {
                // パス欄は読込用。保存は毎回ダイアログを出し、パス欄の値は初期値としてだけ使う。
                string save = PlayerIoUiKit.AskSavePath(T("SaveCSV"), _revCsvPath, "revolution.csv", "csv");
                if (string.IsNullOrEmpty(save)) return;
                _revCsvPath = save;
                csvPathField.SetValueWithoutNotify(_revCsvPath);
                RecentPaths.Set(RevCsvKey, _revCsvPath);

                EnsureRevProfile();
                RevolutionCSVIO.Save(_revCsvPath, _revProfile, _revP);
            }));

            // ── メッシュ⇄プロファイル ─────────────────────────────────────
            var revIoFold = FoldSection(pe, T("MeshProfileIO"), false);
            var ioRow = new VisualElement(); ioRow.style.flexDirection = FlexDirection.Row; ioRow.style.marginBottom = 4;
            SB(ioRow, T("ImportFromMesh"), ImportRevolutionFromMesh);
            SB(ioRow, T("ApplyToMesh"),    ApplyRevolutionToMesh);
            revIoFold.Add(ioRow);
        }

        private void ApplyRevPreset()
        {
            if (_revP.CurrentPreset != ProfilePreset.Custom)
            {
                _revProfile = RevolutionProfileGenerator.CreatePreset(_revP.CurrentPreset, ref _revP);
                _revSel.Clear(); _revSelIdx = -1;
            }
            D(); RefreshRevCanvas(); RefreshRevPointUI();
        }

        // ── キャンバス描画 ────────────────────────────────────────────────

        // プロファイルビュー（ズーム/パン）を反映した座標変換ラッパー。
        private Vector2 RevP2C(Vector2 p, float w, float h)            => RevolutionProfileEditCore.ProfileToCanvas(p, w, h, _revZoom, _revOffset);
        private Vector2 RevC2P(Vector2 c, float w, float h)
            => RevolutionProfileEditCore.CanvasToProfile(c, w, h, _revZoom, _revOffset);
        private int RevFind(List<Vector2> prof, Vector2 c, float w, float h, float md)
            => RevolutionProfileEditCore.FindClosest(prof, c, w, h, md, _revZoom, _revOffset);

        /// <summary>下絵レイヤーにプロファイルビューと同じ変換（中心基準ズーム＋パン）を適用。</summary>
        private void UpdateRevView()
        {
            if (_revViewLayer == null) return;
            _revViewLayer.style.transformOrigin = new TransformOrigin(
                new Length(50, LengthUnit.Percent), new Length(50, LengthUnit.Percent), 0f);
            _revViewLayer.style.scale     = new Scale(new Vector3(_revZoom, _revZoom, 1f));
            _revViewLayer.style.translate = new Translate(
                new Length(_revOffset.x), new Length(_revOffset.y), 0f);
        }

        private void OnDrawProfileCanvas(MeshGenerationContext ctx)
        {            if (_revProfile == null || _revProfile.Count == 0) return;

            float w = _revCanvas.resolvedStyle.width;
            float h = _revCanvas.resolvedStyle.height;
            if (w <= 0 || h <= 0) return;

            var p2d = ctx.painter2D;

            // グリッド
            p2d.strokeColor = new Color(0.28f, 0.28f, 0.33f);
            p2d.lineWidth   = 1f;
            p2d.BeginPath();
            for (float x = 0f; x <= RevolutionProfileEditCore.RangeX; x += 0.5f)
            {
                var s = RevP2C(new Vector2(x, -1f), w, h);
                var e = RevP2C(new Vector2(x,  2f), w, h);
                p2d.MoveTo(s); p2d.LineTo(e);
            }
            for (float y = -1f; y <= 2f; y += 0.5f)
            {
                var s = RevP2C(new Vector2(0f, y), w, h);
                var e = RevP2C(new Vector2(2f, y), w, h);
                p2d.MoveTo(s); p2d.LineTo(e);
            }
            p2d.Stroke();

            // 軸
            p2d.strokeColor = new Color(0.52f, 0.52f, 0.58f);
            p2d.lineWidth   = 1.5f;
            p2d.BeginPath();
            var ay0 = RevP2C(new Vector2(0f, -1f), w, h);
            var ay1 = RevP2C(new Vector2(0f,  2f), w, h);
            p2d.MoveTo(ay0); p2d.LineTo(ay1);
            var ax0 = RevP2C(new Vector2(0f, 0f), w, h);
            var ax1 = RevP2C(new Vector2(2f, 0f), w, h);
            p2d.MoveTo(ax0); p2d.LineTo(ax1);
            p2d.Stroke();

            // プロファイルライン（セグメントごとにホバー判定）
            if (_revProfile.Count >= 2)
            {
                int segCount = _revP.CloseLoop ? _revProfile.Count : _revProfile.Count - 1;
                for (int i = 0; i < segCount; i++)
                {
                    int  j   = (i + 1) % _revProfile.Count;
                    var  a   = RevP2C(_revProfile[i], w, h);
                    var  b   = RevP2C(_revProfile[j], w, h);
                    bool hov = (i == _revHoverEI);
                    p2d.strokeColor = hov ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.2f, 0.75f, 0.85f);
                    p2d.lineWidth   = hov ? 3f : 1.5f;
                    p2d.BeginPath();
                    p2d.MoveTo(a); p2d.LineTo(b);
                    p2d.Stroke();
                }
            }

            // 頂点ドット
            for (int i = 0; i < _revProfile.Count; i++)
            {
                bool sel     = _revSel.Contains(i);
                bool primary = (i == _revSelIdx);
                var  sp      = RevP2C(_revProfile[i], w, h);
                p2d.fillColor = primary ? Color.white
                              : sel     ? new Color(1f, 0.85f, 0.2f)
                              :           new Color(0.2f, 0.75f, 0.85f);
                float r = (sel || primary) ? 5.5f : 3.5f;
                RevFillCircle(p2d, sp, r, 10);
            }

            // マーキー
            if (_revMarquee.Active)
                _revMarquee.Draw(p2d, new Color(1f, 0.85f, 0.2f, 0.9f));

            // アンカー（ギズモ表示OFFで抑止。アンカー設定中は常に表示）
            if (_revShowGizmo || _revAnchor.Mode)
                _revAnchor.Draw(p2d, RevP2C(_revAnchor.Value, w, h));

            // 回転/拡大縮小ハンドル（アンカー設定モード中/ギズモ表示OFFで非表示）
            if (_revShowGizmo && !_revAnchor.Mode)
                _revHandle.Draw(p2d, RevP2C(_revAnchor.Value, w, h));

            // マグネット半径（選択点まわり）
            if (_revMagnet.Enabled && _revSel.Count > 0)
            {
                var centers = new List<Vector2>();
                foreach (var i in _revSel)
                    if (i >= 0 && i < _revProfile.Count) centers.Add(RevP2C(_revProfile[i], w, h));
                float cr = Vector2.Distance(RevP2C(Vector2.zero, w, h),
                                            RevP2C(new Vector2(_revMagnet.Radius, 0f), w, h));
                _revMagnet.DrawRadius(p2d, centers, cr);
            }
        }

        /// <summary>Painter2D でポリゴン近似の塗りつぶし円を描く</summary>
        private static void RevFillCircle(Painter2D p2d, Vector2 center, float radius, int n)
        {
            p2d.BeginPath();
            for (int i = 0; i <= n; i++)
            {
                float a  = i * Mathf.PI * 2f / n;
                var   pt = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                if (i == 0) p2d.MoveTo(pt); else p2d.LineTo(pt);
            }
            p2d.ClosePath();
            p2d.Fill();
        }

        private void RefreshRevCanvas() => _revCanvas?.MarkDirtyRepaint();

        private void RefreshRevPointUI()
        {
            if (_revPtRow == null) return;
            if (_revSelIdx >= 0 && _revProfile != null && _revSelIdx < _revProfile.Count)
            {
                var pt = _revProfile[_revSelIdx];
                if (_revPtLabel  != null) _revPtLabel.text = $"Pt {_revSelIdx}  R={pt.x:F3}  Y={pt.y:F3}";
                _revPtXSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.x, 0f,  2f));
                _revPtXField?.SetValueWithoutNotify((float)Math.Round(pt.x, 3));
                _revPtYSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.y, -1f, 2f));
                _revPtYField?.SetValueWithoutNotify((float)Math.Round(pt.y, 3));
                _revPtRow.style.display = DisplayStyle.Flex;
            }
            else
            {
                _revPtRow.style.display = DisplayStyle.None;
            }
            RefreshRevAnchorAuto();
        }

        // ── キャンバスポインターイベント ─────────────────────────────────

        private void OnRevCanvasPointerDown(PointerDownEvent e)
        {
            // 中ボタン＝ビューのパン
            if (e.button == 2)
            {
                _revPanDrag        = true;
                _revPanStart       = e.localPosition;
                _revPanOffsetStart = _revOffset;
                _revCanvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }
            if (e.button != 0) return;

            // 下絵移動モード
            if (_revBgMode && _revBgTex != null)
            {
                _revBgDrag              = true;
                _revBgDragStart         = e.localPosition;
                _revBgOffsetOnDragStart = _revBgOffset;
                _revCanvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }

            EnsureRevProfile();

            float w = _revCanvas.resolvedStyle.width;
            float h = _revCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // アンカー設定モード：ドラッグでアンカー移動（点編集/マーキーは行わない）
            if (_revAnchor.Mode)
            {
                _revAnchor.Value = RevC2P(cp, w, h);
                _revAnchor.Manual = true;
                _revAnchorDrag = true;
                RefreshRevAnchorFields();
                _revCanvas.CapturePointer(e.pointerId);
                RefreshRevCanvas();
                e.StopPropagation(); return;
            }

            // 0. ハンドルヒット判定（回転/拡大縮小、点編集より優先。ギズモ表示OFFで無効）
            var revHit = _revShowGizmo
                ? _revHandle.HitTest(cp, RevP2C(_revAnchor.Value, w, h))
                : Canvas2DHandle.HandleType.None;
            if (revHit != Canvas2DHandle.HandleType.None)
            {
                BeginRevHandle(revHit, cp, w, h);
                _revCanvas.CapturePointer(e.pointerId);
                RefreshRevCanvas();
                e.StopPropagation(); return;
            }

            // 1. 頂点ヒット判定（優先、15px以内）
            int ptIdx = RevFind(_revProfile, cp, w, h, 15f);
            if (ptIdx >= 0)
            {
                if (e.shiftKey)
                {
                    // Shift+クリック＝トグル（ドラッグ移動しない）
                    if (!_revSel.Add(ptIdx)) _revSel.Remove(ptIdx);
                    _revSelIdx = _revSel.Contains(ptIdx) ? ptIdx : RevPrimary();
                    _revCanvas.CapturePointer(e.pointerId);
                    RefreshRevCanvas(); RefreshRevPointUI();
                    e.StopPropagation(); return;
                }
                if (!_revSel.Contains(ptIdx)) { _revSel.Clear(); _revSel.Add(ptIdx); }
                _revSelIdx = ptIdx;
                RevBegin();
                BeginRevDrag(cp, w, h);
                _revCanvas.CapturePointer(e.pointerId);
                RefreshRevCanvas(); RefreshRevPointUI();
                e.StopPropagation(); return;
            }

            // 2. セグメントヒット判定（10px以内）→ 即時挿入＆ドラッグ開始
            int   bestSeg  = -1;
            float bestDist = 10f;
            Vector2 insertProf = Vector2.zero;
            int   segCount = _revP.CloseLoop ? _revProfile.Count : _revProfile.Count - 1;
            for (int i = 0; i < segCount; i++)
            {
                int   j  = (i + 1) % _revProfile.Count;
                var   a  = RevP2C(_revProfile[i], w, h);
                var   b  = RevP2C(_revProfile[j], w, h);
                float t  = Mathf.Clamp01(Vector2.Dot(cp - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                float d  = Vector2.Distance(cp, Vector2.Lerp(a, b, t));
                if (d < bestDist)
                {
                    bestDist   = d;
                    bestSeg    = i;
                    // 挿入座標をプロファイル空間で計算
                    insertProf = Vector2.Lerp(_revProfile[i], _revProfile[j], t);
                    insertProf.x = Mathf.Max(0f, insertProf.x);
                }
            }
            if (bestSeg >= 0)
            {
                int insertIdx = bestSeg + 1;
                RevBegin();
                _revProfile.Insert(insertIdx, insertProf);
                _revSel.Clear(); _revSel.Add(insertIdx);
                _revSelIdx  = insertIdx;
                _revHoverEI = -1;
                BeginRevDrag(cp, w, h);
                _revCanvas.CapturePointer(e.pointerId);
                _revP.CurrentPreset = ProfilePreset.Custom;
                D(); RefreshRevCanvas(); RefreshRevPointUI();
                e.StopPropagation(); return;
            }

            // 3. 空領域 → 矩形/投げ縄マーキー選択（Shiftで追加）
            _revMarqueeAdditive = e.shiftKey;
            _revMarquee.Begin(cp, _revLassoMode);
            _revMarqueeDrag = true;
            _revCanvas.CapturePointer(e.pointerId);
            RefreshRevCanvas();
            e.StopPropagation();
        }

        /// <summary>選択集合の代表インデックス（無ければ -1）。</summary>
        private int RevPrimary()
        {
            foreach (var i in _revSel) return i;
            return -1;
        }

        /// <summary>選択点の一括ドラッグ開始（各点の開始位置とカーソル基準を記録）。</summary>
        private void BeginRevDrag(Vector2 cp, float w, float h)
        {
            _revDrag    = true;
            _revDragIdx = _revSelIdx;
            _revDragStart.Clear();
            if (_revProfile != null)
                foreach (var i in _revSel)
                    if (i >= 0 && i < _revProfile.Count) _revDragStart[i] = _revProfile[i];
            _revDragStartCursorProf = RevC2P(cp, w, h);

            // マグネット影響点（非選択で半径内）を確定
            _revMagnetStart.Clear(); _revMagnetW.Clear();
            if (_revMagnet.Enabled && _revProfile != null && _revSel.Count > 0)
            {
                var sel = new List<Vector2>();
                foreach (var i in _revSel) if (i >= 0 && i < _revProfile.Count) sel.Add(_revProfile[i]);
                for (int i = 0; i < _revProfile.Count; i++)
                {
                    if (_revSel.Contains(i)) continue;
                    float wt = _revMagnet.WeightFor(_revProfile[i], sel);
                    if (wt > 0f) { _revMagnetStart[i] = _revProfile[i]; _revMagnetW[i] = wt; }
                }
            }
        }

        /// <summary>マーキー内側の点を選択に反映する。</summary>
        private void ApplyRevMarquee()
        {
            float w = _revCanvas.resolvedStyle.width, h = _revCanvas.resolvedStyle.height;
            if (!_revMarqueeAdditive) _revSel.Clear();
            if (_revProfile != null)
                for (int i = 0; i < _revProfile.Count; i++)
                    if (_revMarquee.Contains(RevP2C(_revProfile[i], w, h))) _revSel.Add(i);
            _revSelIdx = _revSel.Contains(_revSelIdx) ? _revSelIdx : RevPrimary();
            RefreshRevCanvas(); RefreshRevPointUI();
        }

        // ================================================================
        // 回転/拡大縮小アンカー・変換（共通ビルダー＋回転体側）
        // ================================================================

        /// <summary>アンカーX/Y行（スライダー＋テキスト）を作る。</summary>
        private VisualElement BuildAnchorRow(string label, float min, float max, float val,
            out Slider slider, out FloatField field, Func<bool> suppressed, Action<float> onChange)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lb = new Label(label + ":"); lb.style.width = 16; lb.style.fontSize = 10; lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            var sl = new Slider(min, max) { value = Mathf.Clamp(val, min, max) }; sl.style.flexGrow = 1; sl.style.marginRight = 3;
            var ff = new FloatField { value = val }; ff.style.width = 52;
            sl.RegisterValueChangedCallback(e => { if (!suppressed()) onChange(e.newValue); });
            ff.RegisterValueChangedCallback(e => { if (!suppressed()) onChange(e.newValue); });
            row.Add(lb); row.Add(sl); row.Add(ff);
            slider = sl; field = ff; return row;
        }

        /// <summary>2フィールド行（例: 移動 X/Y、スケール X/Y）。</summary>
        private VisualElement BuildTf2(string label, float v1, float v2, out FloatField f1, out FloatField f2)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lb = new Label(label); lb.style.width = 70; lb.style.fontSize = 10; lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            f1 = new FloatField { value = v1 }; f1.style.flexGrow = 1;
            f2 = new FloatField { value = v2 }; f2.style.flexGrow = 1;
            row.Add(lb); row.Add(f1); row.Add(f2); return row;
        }

        /// <summary>1フィールド行（例: 回転）。</summary>
        private VisualElement BuildTf1(string label, float v, out FloatField f)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lb = new Label(label); lb.style.width = 70; lb.style.fontSize = 10; lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            f = new FloatField { value = v }; f.style.flexGrow = 1;
            row.Add(lb); row.Add(f); return row;
        }

        /// <summary>選択（無ければ全点）のプロファイル座標リスト。</summary>
        private List<Vector2> SelectedRevPoints()
        {
            var pts = new List<Vector2>();
            if (_revProfile == null) return pts;
            if (_revSel.Count > 0)
            {
                foreach (var i in _revSel)
                    if (i >= 0 && i < _revProfile.Count) pts.Add(_revProfile[i]);
            }
            else pts.AddRange(_revProfile);
            return pts;
        }

        private void BuildRevAnchorTransformUI(VisualElement pe)
        {
            // 選択点の移動など
            var tfFold = FoldSection(pe, T("SelectionTransform"), false);
            tfFold.Add(BuildTf2("移動 X/Y",   0f, 0f, out _revTfMoveX,  out _revTfMoveY));
            tfFold.Add(BuildTf2("スケール X/Y", 1f, 1f, out _revTfScaleX, out _revTfScaleY));
            tfFold.Add(BuildTf1("スケール軸 (°)", 0f, out _revTfScaleAxis));
            tfFold.Add(BuildTf1("回転 (°)",    0f, out _revTfRot));
            var applyRow = new VisualElement(); applyRow.style.flexDirection = FlexDirection.Row; applyRow.style.marginBottom = 4;
            SB(applyRow, "変換適用", ApplyRevTransform);
            SB(applyRow, "リセット", () =>
            {
                _revTfMoveX.value = 0f; _revTfMoveY.value = 0f;
                _revTfScaleX.value = 1f; _revTfScaleY.value = 1f; _revTfRot.value = 0f;
                _revTfScaleAxis.value = 0f;
            });
            tfFold.Add(applyRow);

            // マグネット（比例編集）
            var magFold = FoldSection(pe, T("Magnet"), false);
            var revMagRow = new VisualElement(); revMagRow.style.flexDirection = FlexDirection.Row; revMagRow.style.marginBottom = 2;
            var revMagToggle = new Toggle("有効") { value = _revMagnet.Enabled }; revMagToggle.style.marginRight = 6;
            revMagToggle.RegisterValueChangedCallback(ev => { _revMagnet.Enabled = ev.newValue; RefreshRevCanvas(); });
            var revFalloff = new EnumField(_revMagnet.Falloff); revFalloff.style.flexGrow = 1;
            revFalloff.RegisterValueChangedCallback(ev => _revMagnet.Falloff = (FalloffType)ev.newValue);
            revMagRow.Add(revMagToggle); revMagRow.Add(revFalloff);
            magFold.Add(revMagRow);
            magFold.Add(BuildAnchorRow("半径", 0.05f, 2f, _revMagnet.Radius, out _revMagnetRadius, out _,
                () => false, v => { _revMagnet.Radius = v; RefreshRevCanvas(); }));

            // アンカー
            var anchorFold = FoldSection(pe, T("AnchorSection"), false);
            _revAnchorEnterBtn = new Button(() => SetRevAnchorMode(true)) { text = "アンカー設定" };
            _revAnchorEnterBtn.style.marginBottom = 2;
            anchorFold.Add(_revAnchorEnterBtn);

            _revAnchorPanel = new VisualElement(); _revAnchorPanel.style.marginBottom = 4;
            {
                var headRow = new VisualElement(); headRow.style.flexDirection = FlexDirection.Row; headRow.style.marginBottom = 2;
                var lbl = new Label("アンカー調整中（キャンバスをドラッグで移動）"); lbl.style.fontSize = 10; lbl.style.flexGrow = 1; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
                var done = new Button(() => SetRevAnchorMode(false)) { text = "決定" }; done.style.width = 60;
                headRow.Add(lbl); headRow.Add(done); _revAnchorPanel.Add(headRow);

                var presetRow = new VisualElement(); presetRow.style.flexDirection = FlexDirection.Row; presetRow.style.marginBottom = 2;
                SB(presetRow, "重心", () => ApplyRevAnchorPreset(Canvas2DAnchor.Preset.Centroid));
                SB(presetRow, "中心", () => ApplyRevAnchorPreset(Canvas2DAnchor.Preset.Center));
                SB(presetRow, "左上", () => ApplyRevAnchorPreset(Canvas2DAnchor.Preset.TopLeft));
                SB(presetRow, "左下", () => ApplyRevAnchorPreset(Canvas2DAnchor.Preset.BottomLeft));
                _revAnchorPanel.Add(presetRow);

                _revAnchorPanel.Add(BuildAnchorRow("X", 0f, 2f, 0f, out _revAnchorXSlider, out _revAnchorXField,
                    () => _revAnchorSuppress, v => SetRevAnchorComponent(true, v)));
                _revAnchorPanel.Add(BuildAnchorRow("Y", -1f, 2f, 0f, out _revAnchorYSlider, out _revAnchorYField,
                    () => _revAnchorSuppress, v => SetRevAnchorComponent(false, v)));
            }
            anchorFold.Add(_revAnchorPanel);
            RefreshRevAnchorModeUI();
            RefreshRevAnchorFields();
        }

        private void SetRevAnchorMode(bool on)
        {
            _revAnchor.Mode = on;
            if (on) RefreshRevAnchorAuto();
            RefreshRevAnchorModeUI();
            RefreshRevCanvas();
        }
        private void RefreshRevAnchorModeUI()
        {
            if (_revAnchorEnterBtn != null) _revAnchorEnterBtn.style.display = _revAnchor.Mode ? DisplayStyle.None : DisplayStyle.Flex;
            if (_revAnchorPanel    != null) _revAnchorPanel.style.display    = _revAnchor.Mode ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private void RefreshRevAnchorFields()
        {
            _revAnchorSuppress = true;
            _revAnchorXSlider?.SetValueWithoutNotify(Mathf.Clamp(_revAnchor.Value.x, 0f, 2f));
            _revAnchorYSlider?.SetValueWithoutNotify(Mathf.Clamp(_revAnchor.Value.y, -1f, 2f));
            _revAnchorXField?.SetValueWithoutNotify(_revAnchor.Value.x);
            _revAnchorYField?.SetValueWithoutNotify(_revAnchor.Value.y);
            _revAnchorSuppress = false;
        }
        private void RefreshRevAnchorAuto()
        {
            if (_revAnchor.Manual) return;
            var pts = SelectedRevPoints();
            if (pts.Count > 0) _revAnchor.SetPreset(pts, Canvas2DAnchor.Preset.Centroid);
            RefreshRevAnchorFields();
        }
        private void SetRevAnchorComponent(bool isX, float v)
        {
            var a = _revAnchor.Value; if (isX) a.x = v; else a.y = v; _revAnchor.Value = a;
            _revAnchor.Manual = true;
            RefreshRevAnchorFields(); RefreshRevCanvas();
        }
        private void ApplyRevAnchorPreset(Canvas2DAnchor.Preset p)
        {
            _revAnchor.SetPreset(SelectedRevPoints(), p);
            RefreshRevAnchorFields(); RefreshRevCanvas();
        }

        private void ApplyRevTransform()
        {
            EnsureRevProfile();
            RevBegin();
            RefreshRevAnchorAuto();
            var a  = _revAnchor.Value;
            float mx = _revTfMoveX?.value ?? 0f, my = _revTfMoveY?.value ?? 0f;
            float sx = _revTfScaleX?.value ?? 1f, sy = _revTfScaleY?.value ?? 1f;
            float deg = _revTfRot?.value ?? 0f;
            float saRad = (_revTfScaleAxis?.value ?? 0f) * Mathf.Deg2Rad;
            float saCos = Mathf.Cos(saRad), saSin = Mathf.Sin(saRad);

            bool useSel = _revSel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel) foreach (var i in _revSel) if (i >= 0 && i < _revProfile.Count) sel.Add(_revProfile[i]);
            var orig = new List<Vector2>(_revProfile);

            for (int i = 0; i < orig.Count; i++)
            {
                float wt;
                if (!useSel)                 wt = 1f;                 // 選択なし＝全点フル変形
                else if (_revSel.Contains(i)) wt = 1f;
                else wt = _revMagnet.Enabled ? _revMagnet.WeightFor(orig[i], sel) : 0f;
                if (wt <= 0f) continue;

                var np = Xform2D(orig[i], a, mx, my, sx, sy, saCos, saSin, deg, wt);
                np.x = Mathf.Max(0f, np.x);   // 回転体 R は非負
                _revProfile[i] = np;
            }
            _revP.CurrentPreset = ProfilePreset.Custom;
            RevCommit("変換適用");
            D(); RefreshRevCanvas(); RefreshRevPointUI();
        }

        /// <summary>
        /// アンカー a 基準の2D変換（移動/スケール(スケール軸フレーム)/回転、重み wt）。
        /// 3キャンバス（回転体/Profile2D/UV と同一数式）で共有。
        /// </summary>
        private static Vector2 Xform2D(Vector2 p, Vector2 a,
            float mx, float my, float sx, float sy, float saCos, float saSin, float deg, float wt)
        {
            float sxw = 1f + (sx - 1f) * wt, syw = 1f + (sy - 1f) * wt;
            float degw = deg * wt * Mathf.Deg2Rad;
            float cw = Mathf.Cos(degw), sw = Mathf.Sin(degw);
            Vector2 d = p - a;
            // スケール軸フレームへ回転(-φ) → 重み付きスケール → 戻す(+φ)
            float rx =  d.x * saCos + d.y * saSin;
            float ry = -d.x * saSin + d.y * saCos;
            rx *= sxw; ry *= syw;
            d = new Vector2(rx * saCos - ry * saSin, rx * saSin + ry * saCos);
            // 重み付き全体回転
            d = new Vector2(d.x * cw - d.y * sw, d.x * sw + d.y * cw);
            return a + d + new Vector2(mx, my) * wt;
        }

        // ── 回転体：ハンドルドラッグ（回転/拡大縮小） ─────────────────────

        /// <summary>ハンドルドラッグ開始。影響点（選択=1/マグネット=weight、選択なし=全点1）を記録。</summary>
        private void BeginRevHandle(Canvas2DHandle.HandleType type, Vector2 cp, float w, float h)
        {
            _revHandleDrag = true;
            _revHandleType = type;
            _revHandle.Active = type;
            RefreshRevAnchorAuto();
            RevBegin();

            _revHandleAnchorC   = RevP2C(_revAnchor.Value, w, h);
            _revHandlePrevAngle = Canvas2DHandle.AngleDeg(_revHandleAnchorC, cp);
            _revHandleTotalDeg  = 0f;

            _revHandleStart.Clear(); _revHandleW.Clear();
            if (_revProfile == null) return;

            bool useSel = _revSel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel) foreach (var i in _revSel) if (i >= 0 && i < _revProfile.Count) sel.Add(_revProfile[i]);

            for (int i = 0; i < _revProfile.Count; i++)
            {
                float wt;
                if (!useSel)                  wt = 1f;
                else if (_revSel.Contains(i)) wt = 1f;
                else wt = _revMagnet.Enabled ? _revMagnet.WeightFor(_revProfile[i], sel) : 0f;
                if (wt <= 0f) continue;
                _revHandleStart[i] = _revProfile[i];
                _revHandleW[i]     = wt;
            }
        }

        /// <summary>ハンドルドラッグ中：開始スナップショットへ変換を適用（ライブプレビュー）。</summary>
        private void ApplyRevHandle(Vector2 cp, float w, float h)
        {
            if (!_revHandleDrag || _revProfile == null) return;

            float sx = 1f, sy = 1f, deg = 0f;
            if (_revHandleType == Canvas2DHandle.HandleType.Rotate)
            {
                float ang = Canvas2DHandle.AngleDeg(_revHandleAnchorC, cp);
                _revHandleTotalDeg += -Mathf.DeltaAngle(_revHandlePrevAngle, ang); // Y反転補正
                _revHandlePrevAngle = ang;
                deg = _revHandleTotalDeg;
            }
            else
            {
                _revHandle.ScaleFactors(_revHandleType, _revHandleAnchorC, cp, out sx, out sy);
            }

            var a = _revAnchor.Value;
            foreach (var kv in _revHandleStart)
            {
                int i = kv.Key;
                if (i < 0 || i >= _revProfile.Count) continue;
                var np = Xform2D(kv.Value, a, 0f, 0f, sx, sy, 1f, 0f, deg, _revHandleW[i]);
                np.x = Mathf.Max(0f, np.x);
                _revProfile[i] = np;
            }
            _revP.CurrentPreset = ProfilePreset.Custom;
            D(); RefreshRevCanvas(); RefreshRevPointUI();
        }

        /// <summary>ハンドルドラッグ終了：コミット。</summary>
        private void EndRevHandle()
        {
            if (!_revHandleDrag) return;
            _revHandleDrag = false;
            _revHandleType = Canvas2DHandle.HandleType.None;
            _revHandle.Active = Canvas2DHandle.HandleType.None;
            RevCommit("回転/拡大縮小");
        }

        private void OnRevCanvasPointerMove(PointerMoveEvent e)
        {
            float w  = _revCanvas.resolvedStyle.width;
            float h  = _revCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // 中ボタンパン
            if (_revPanDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                _revOffset = _revPanOffsetStart + (cp - _revPanStart);
                UpdateRevView(); UpdateRevBgEl(); RefreshRevCanvas();
                e.StopPropagation(); return;
            }

            // 下絵移動モード
            if (_revBgDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                _revBgOffset = _revBgOffsetOnDragStart
                             + (RevC2P(cp, w, h) - RevC2P(_revBgDragStart, w, h));
                UpdateRevBgEl();
                e.StopPropagation(); return;
            }

            // アンカードラッグ
            if (_revAnchorDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                _revAnchor.Value = RevC2P(cp, w, h);
                RefreshRevAnchorFields(); RefreshRevCanvas();
                e.StopPropagation(); return;
            }

            // ハンドルドラッグ（回転/拡大縮小）
            if (_revHandleDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                ApplyRevHandle(cp, w, h);
                e.StopPropagation(); return;
            }

            // マーキー更新
            if (_revMarqueeDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                _revMarquee.Update(cp);
                RefreshRevCanvas();
                e.StopPropagation(); return;
            }

            if (_revDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                if (_revProfile != null && _revDragStart.Count > 0)
                {
                    // 選択点を一括で delta 移動（カーソル追従・グラブ相対を維持）。
                    var delta = RevC2P(cp, w, h) - _revDragStartCursorProf;
                    foreach (var kv in _revDragStart)
                    {
                        int idx = kv.Key;
                        if (idx < 0 || idx >= _revProfile.Count) continue;
                        var np = kv.Value + delta;
                        np.x = Mathf.Max(0f, np.x);
                        _revProfile[idx] = np;
                    }
                    // マグネット: 非選択点を delta×weight で追従
                    foreach (var kv in _revMagnetStart)
                    {
                        int idx = kv.Key;
                        if (idx < 0 || idx >= _revProfile.Count) continue;
                        var np = kv.Value + delta * _revMagnetW[idx];
                        np.x = Mathf.Max(0f, np.x);
                        _revProfile[idx] = np;
                    }
                    _revP.CurrentPreset = ProfilePreset.Custom;
                    D(); RefreshRevCanvas(); RefreshRevPointUI();
                }
                e.StopPropagation(); return;
            }

            // ハンドルホバー更新（非ドラッグ中。ギズモ表示OFF/アンカー設定中は無効）
            var revHovType = (_revAnchor.Mode || !_revShowGizmo)
                                             ? Canvas2DHandle.HandleType.None
                                             : _revHandle.HitTest(cp, RevP2C(_revAnchor.Value, w, h));
            if (revHovType != _revHandle.Hovered) { _revHandle.Hovered = revHovType; RefreshRevCanvas(); }

            // ホバーエッジ更新（非ドラッグ中）
            int prevHov = _revHoverEI;
            _revHoverEI = -1;

            // 頂点近傍ならホバーなし
            bool nearPt = RevFind(_revProfile, cp, w, h, 15f) >= 0;
            if (!nearPt)
            {
                int   segCount2 = _revP.CloseLoop ? _revProfile.Count : _revProfile.Count - 1;
                float bestD     = 10f;
                for (int i = 0; i < segCount2; i++)
                {
                    int   j = (i + 1) % _revProfile.Count;
                    var   a = RevP2C(_revProfile[i], w, h);
                    var   b = RevP2C(_revProfile[j], w, h);
                    float t = Mathf.Clamp01(Vector2.Dot(cp - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                    float d = Vector2.Distance(cp, Vector2.Lerp(a, b, t));
                    if (d < bestD) { bestD = d; _revHoverEI = i; }
                }
            }

            if (_revHoverEI != prevHov) RefreshRevCanvas();
        }

        private void OnRevCanvasPointerUp(PointerUpEvent e)
        {
            if (!_revCanvas.HasPointerCapture(e.pointerId)) return;
            _revCanvas.ReleasePointer(e.pointerId);
            if (_revMarqueeDrag) { ApplyRevMarquee(); _revMarquee.End(); _revMarqueeDrag = false; }
            if (_revHandleDrag) EndRevHandle();
            bool wasRevDrag = _revDrag;
            _revDrag    = false;
            _revBgDrag  = false;
            _revPanDrag = false;
            _revAnchorDrag = false;
            if (wasRevDrag) RevCommit("プロファイル点編集");
            e.StopPropagation();
        }

        // ================================================================
        // Profile2D UI（変更なし）
        // ================================================================

        private void EnsureP2DLoops()
        {
            if (_p2dLoops != null) return;
            _p2dLoops = new List<Loop>();
            var outer = new Loop();
            float r = 0.4f;
            outer.Points.AddRange(new[]
            {
                new Vector2(-r, -r), new Vector2( r, -r),
                new Vector2( r,  r), new Vector2(-r,  r),
            });
            _p2dLoops.Add(outer);
        }

        private void BuildProfile2DUI(VisualElement c)
        {
            EnsureP2DLoops();
            _p2dDrag = false; _p2dDragIdx = -1; _p2dHoverEL = -1; _p2dHoverEI = -1;
            _p2dSel.Clear();
            _p2dSelLoop = Mathf.Clamp(_p2dSelLoop, 0, _p2dLoops.Count - 1);

            c.Add(ShapeTitle(T("Profile2D")));
            c.Add(NF(() => _p2dP.MeshName, v => _p2dP.MeshName = v));

            var pe = _profileEditorContainer;

            // ヘルプ
            var helpLbl = new Label(T("P2dEditorHelp"));
            helpLbl.style.fontSize = 9;
            helpLbl.style.color = new StyleColor(new Color(0.6f, 0.7f, 0.6f));
            helpLbl.style.marginBottom = 3;
            helpLbl.style.whiteSpace = WhiteSpace.Normal;
            pe.Add(helpLbl);

            // ── 2D キャンバス ──────────────────────────────────────────────
            _p2dCanvas = new VisualElement();
            _p2dCanvas.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            _p2dCanvas.style.height          = _profileHeight;
            _p2dCanvas.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.15f));
            _p2dCanvas.style.marginBottom    = 4;
            _p2dCanvas.style.borderTopWidth  = _p2dCanvas.style.borderBottomWidth =
            _p2dCanvas.style.borderLeftWidth = _p2dCanvas.style.borderRightWidth  = 1;
            _p2dCanvas.style.borderTopColor  = _p2dCanvas.style.borderBottomColor =
            _p2dCanvas.style.borderLeftColor = _p2dCanvas.style.borderRightColor  =
                new StyleColor(new Color(0.4f, 0.4f, 0.45f));
            _p2dCanvas.style.overflow        = Overflow.Hidden;
            _p2dCanvas.pickingMode           = PickingMode.Position;

            // 下絵レイヤー（ビューレイヤー配下。プロファイルと同じ view で追従）
            _p2dViewLayer = new VisualElement();
            _p2dViewLayer.style.position = Position.Absolute;
            _p2dViewLayer.style.left = _p2dViewLayer.style.top =
            _p2dViewLayer.style.right = _p2dViewLayer.style.bottom = 0;
            _p2dViewLayer.pickingMode = PickingMode.Ignore;

            _p2dBgEl = new VisualElement();
            _p2dBgEl.style.position = Position.Absolute;
            _p2dBgEl.style.display  = DisplayStyle.None;
            _p2dBgEl.pickingMode    = PickingMode.Ignore;
            _p2dViewLayer.Add(_p2dBgEl);
            _p2dCanvas.Add(_p2dViewLayer);

            _p2dCanvas.generateVisualContent += OnDrawP2dCanvas;
            _p2dCanvas.RegisterCallback<PointerDownEvent>(OnP2dPointerDown);
            _p2dCanvas.RegisterCallback<PointerMoveEvent>(OnP2dPointerMove);
            _p2dCanvas.RegisterCallback<PointerUpEvent>(OnP2dPointerUp);
            _p2dCanvas.RegisterCallback<WheelEvent>(e =>
            {
                if (_p2dBgMode)
                {
                    _p2dBgScale = Mathf.Clamp(_p2dBgScale * (1f - e.delta.y * 0.05f), 0.1f, 10f);
                    _p2dBgScaleSlider?.SetValueWithoutNotify(_p2dBgScale);
                    UpdateP2dBgEl(); RefreshP2dCanvas();
                }
                else
                {
                    // 通常モードはプロファイルビューをカーソル基準でズーム。
                    float w = _p2dCanvas.resolvedStyle.width, h = _p2dCanvas.resolvedStyle.height;
                    float oldZoom = _p2dZoom;
                    float newZoom = Mathf.Clamp(oldZoom * (1f - e.delta.y * 0.05f), 0.2f, 5f);
                    if (newZoom != oldZoom)
                    {
                        var m = (Vector2)e.localMousePosition;
                        var center = new Vector2(w * 0.5f, h * 0.5f);
                        float k = newZoom / oldZoom;
                        _p2dOffset = (m - center) * (1f - k) + _p2dOffset * k;
                        _p2dZoom   = newZoom;
                        UpdateP2dView(); UpdateP2dBgEl(); RefreshP2dCanvas();
                    }
                }
                e.StopPropagation();
            });
            _p2dCanvas.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateP2dBgEl(); UpdateP2dView(); RefreshP2dCanvas();
            });
            pe.Add(_p2dCanvas);

            // キャンバス縦リサイズハンドル
            AddProfileResizeHandle(pe, _p2dCanvas, RefreshP2dCanvas);

            var p2dViewRow = new VisualElement(); p2dViewRow.style.flexDirection = FlexDirection.Row; p2dViewRow.style.marginBottom = 3;
            SB(p2dViewRow, T("ResetView"), () =>
            {
                _p2dZoom = 1f; _p2dOffset = Vector2.zero;
                UpdateP2dView(); UpdateP2dBgEl(); RefreshP2dCanvas();
            });
            var p2dLassoToggle = new Toggle(T("LassoMode")) { value = _p2dLassoMode };
            p2dLassoToggle.style.marginLeft = 4;
            p2dLassoToggle.RegisterValueChangedCallback(ev => _p2dLassoMode = ev.newValue);
            p2dViewRow.Add(p2dLassoToggle);
            var p2dGizmoTog = new Toggle(T("ShowGizmo")) { value = _p2dShowGizmo };
            p2dGizmoTog.style.marginLeft = 8;
            p2dGizmoTog.RegisterValueChangedCallback(ev => { _p2dShowGizmo = ev.newValue; RefreshP2dCanvas(); });
            p2dViewRow.Add(p2dGizmoTog);
            var p2dVertTog = new Toggle(T("ShowVertices")) { value = _p2dShowVerts };
            p2dVertTog.style.marginLeft = 8;
            p2dVertTog.RegisterValueChangedCallback(ev => { _p2dShowVerts = ev.newValue; RefreshP2dCanvas(); });
            p2dViewRow.Add(p2dVertTog);
            pe.Add(p2dViewRow);

            BuildP2dAnchorTransformUI(pe);

            // ── 下絵セクション ─────────────────────────────────────────────
            BuildBgSection(pe,
                T("BgImage"),
                () => _p2dBgPath, v => _p2dBgPath = v,
                () => _p2dBgAlpha, v => { _p2dBgAlpha = v; UpdateP2dBgEl(); },
                () => _p2dBgMode,  v => { _p2dBgMode  = v; },
                () => _p2dBgScale, v => { _p2dBgScale = Mathf.Clamp(v, 0.1f, 10f); UpdateP2dBgEl(); RefreshP2dCanvas(); },
                () => _p2dBgOrigin, v => { _p2dBgOrigin = v; UpdateP2dBgEl(); RefreshP2dCanvas(); },
                () => _p2dBgTex,
                () =>
                {
                    if (string.IsNullOrEmpty(_p2dBgPath)) return;
                    LoadBgTexture(_p2dBgPath, ref _p2dBgTex, _p2dBgEl);
                    _p2dBgOffset = Vector2.zero; _p2dBgScale = 8f;
                    if (_p2dBgTex != null)
                        _p2dBgOrigin = new Vector2(_p2dBgTex.width * 0.5f, _p2dBgTex.height * 0.5f);
                    _p2dBgScaleSlider?.SetValueWithoutNotify(1f);
                    SetBgSizeLabel(_p2dBgSizeLabel, _p2dBgTex);
                    UpdateP2dBgEl();
                },
                () =>
                {
                    _p2dBgTex = null;
                    _p2dBgEl.style.display = DisplayStyle.None;
                    _p2dBgEl.style.backgroundImage = new StyleBackground();
                    SetBgSizeLabel(_p2dBgSizeLabel, null);
                },
                out _p2dBgScaleSlider, out _p2dBgSizeLabel);

            // ── ループ（操作ボタン・一覧をまとめる） ──────────────────────
            var loopFold = FoldSection(pe, T("Loops"), true);

            // ── ループ操作ボタン行 ────────────────────────────────────────
            var loopBtnRow = new VisualElement(); loopBtnRow.style.flexDirection = FlexDirection.Row; loopBtnRow.style.marginBottom = 3;
            SB(loopBtnRow, "◀", () =>
            {
                if (_p2dLoops.Count == 0) return;
                _p2dSelLoop = (_p2dSelLoop - 1 + _p2dLoops.Count) % _p2dLoops.Count;
                _p2dSel.Clear(); _p2dSelPt = -1; RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, "▶", () =>
            {
                if (_p2dLoops.Count == 0) return;
                _p2dSelLoop = (_p2dSelLoop + 1) % _p2dLoops.Count;
                _p2dSel.Clear(); _p2dSelPt = -1; RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, T("DeletePoint"), () =>
            {
                if (_p2dLoops == null) return;
                P2dBegin();
                if (_p2dSel.Count > 0)
                {
                    // 選択点をループ別にインデックス降順で一括削除（各ループ最小3点を維持）。
                    var byLoop = new Dictionary<int, List<int>>();
                    foreach (var k in _p2dSel)
                    {
                        int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                        if (li < 0 || li >= _p2dLoops.Count) continue;
                        if (!byLoop.TryGetValue(li, out var list)) { list = new List<int>(); byLoop[li] = list; }
                        list.Add(pi);
                    }
                    foreach (var kv in byLoop)
                    {
                        var lp2 = _p2dLoops[kv.Key];
                        kv.Value.Sort(); kv.Value.Reverse();
                        foreach (var pi in kv.Value)
                            if (pi >= 0 && pi < lp2.Points.Count && lp2.Points.Count > 3)
                                lp2.Points.RemoveAt(pi);
                    }
                    _p2dSel.Clear(); _p2dSelPt = -1;
                }
                else
                {
                    if (_p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                    var lp = _p2dLoops[_p2dSelLoop];
                    if (_p2dSelPt < 0 || _p2dSelPt >= lp.Points.Count || lp.Points.Count <= 3) return;
                    lp.Points.RemoveAt(_p2dSelPt);
                    _p2dSelPt = Mathf.Clamp(_p2dSelPt, 0, lp.Points.Count - 1);
                }
                P2dCommit("点削除");
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, T("AddLoop"), () =>
            {
                if (_p2dLoops == null) return;
                P2dBegin();
                var lp = new Loop(); float r2 = 0.2f;
                lp.Points.AddRange(new[] {
                    new Vector2(-r2,-r2), new Vector2(r2,-r2),
                    new Vector2(r2, r2),  new Vector2(-r2, r2) });
                _p2dLoops.Add(lp);
                _p2dSelLoop = _p2dLoops.Count - 1; P2dSelectAllInLoop(_p2dSelLoop);
                P2dCommit("ループ追加");
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, T("RemoveLoop"), () =>
            {
                if (_p2dLoops.Count <= 1 || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                P2dBegin();
                _p2dLoops.RemoveAt(_p2dSelLoop);
                _p2dSelLoop = Mathf.Clamp(_p2dSelLoop, 0, _p2dLoops.Count - 1);
                _p2dSel.Clear(); _p2dSelPt = -1; P2dCommit("ループ削除"); D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            loopFold.Add(loopBtnRow);

            // ── ループ操作ボタン行2（全選択 / 複製） ─────────────────────────
            var loopBtnRow2 = new VisualElement(); loopBtnRow2.style.flexDirection = FlexDirection.Row; loopBtnRow2.style.marginBottom = 3;
            SB(loopBtnRow2, T("SelectAllLoop"), () =>
            {
                if (_p2dLoops == null || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                var lp = _p2dLoops[_p2dSelLoop];
                _p2dSel.Clear();
                for (int pi = 0; pi < lp.Points.Count; pi++) _p2dSel.Add(P2dKey(_p2dSelLoop, pi));
                _p2dSelPt = lp.Points.Count > 0 ? 0 : -1;
                RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow2, T("DuplicateLoop"), () =>
            {
                if (_p2dLoops == null || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                P2dBegin();
                var src = _p2dLoops[_p2dSelLoop];
                var dup = new Loop(src);                    // 点列＋穴フラグを複製
                var ofs = new Vector2(0.1f, 0.1f);          // 少しずらして重なり回避
                for (int pi = 0; pi < dup.Points.Count; pi++) dup.Points[pi] += ofs;
                _p2dLoops.Add(dup);
                _p2dSelLoop = _p2dLoops.Count - 1;          // 複製先を選択
                P2dSelectAllInLoop(_p2dSelLoop);
                P2dCommit("ループ複製");
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow2, T("FlipLoopH"), () =>
            {
                if (_p2dLoops == null || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                var lp = _p2dLoops[_p2dSelLoop];
                if (lp.Points.Count < 2) return;
                P2dBegin();
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    var p = lp.Points[pi];
                    lp.Points[pi] = new Vector2(-p.x, p.y);   // Y軸(x=0)対称
                }
                lp.Points.Reverse();                        // 反転で逆転する巻き順を戻す
                P2dSelectAllInLoop(_p2dSelLoop);
                P2dCommit("ループ左右反転");
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            loopFold.Add(loopBtnRow2);

            // ── ループ一覧（穴フラグ切替） ─────────────────────────────────
            for (int i = 0; i < _p2dLoops.Count; i++)
            {
                int li = i;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
                var selBtn = new Button(() =>
                {
                    _p2dSelLoop = li; _p2dSel.Clear(); _p2dSelPt = -1; RefreshP2dCanvas(); RefreshP2dPointUI();
                })
                { text = $"Loop {i} ({_p2dLoops[i].Points.Count}pt)" };
                selBtn.style.flexGrow = 1; selBtn.style.fontSize = 9; selBtn.style.height = 18;
                selBtn.style.backgroundColor = (i == _p2dSelLoop)
                    ? new StyleColor(new Color(0.25f, 0.45f, 0.65f))
                    : new StyleColor(new Color(0.25f, 0.25f, 0.25f));
                var holeTog = new Toggle(T("IsHole")) { value = _p2dLoops[i].IsHole };
                holeTog.RegisterValueChangedCallback(e => { _p2dLoops[li].IsHole = e.newValue; D(); RefreshP2dCanvas(); });
                row.Add(selBtn); row.Add(holeTog);
                loopFold.Add(row);
            }

            // ── 選択点スライダー ───────────────────────────────────────────
            _p2dPtRow = new VisualElement(); _p2dPtRow.style.marginBottom = 4;
            {
                Slider xSl = new Slider(-5f, 5f); xSl.style.flexGrow = 1;
                FloatField xFf = new FloatField { value = 0f }; xFf.style.width = 48;
                xSl.RegisterValueChangedCallback(e =>
                {
                    if (!P2dGetSelPt(out var lp, out _)) return;
                    xFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    lp.Points[_p2dSelPt] = new Vector2(e.newValue, lp.Points[_p2dSelPt].y);
                    D(); RefreshP2dCanvas();
                });
                xSl.RegisterCallback<PointerDownEvent>(_ => P2dBegin());
                xSl.RegisterCallback<PointerUpEvent>(_ => P2dCommit("点X編集"));
                xFf.RegisterValueChangedCallback(e =>
                {
                    if (!P2dGetSelPt(out var lp, out _)) return;
                    P2dBegin();
                    xSl.SetValueWithoutNotify(e.newValue); lp.Points[_p2dSelPt] = new Vector2(e.newValue, lp.Points[_p2dSelPt].y);
                    D(); RefreshP2dCanvas();
                    P2dCommit("点X編集");
                });
                var xRow = new VisualElement(); xRow.style.flexDirection = FlexDirection.Row; xRow.style.marginBottom = 2;
                xRow.Add(ML("X")); xRow.Add(xSl); xRow.Add(xFf);
                _p2dPtRow.Add(xRow);
                _p2dPtXSlider = xSl; _p2dPtXField = xFf;
            }
            {
                Slider ySl = new Slider(-5f, 5f); ySl.style.flexGrow = 1;
                FloatField yFf = new FloatField { value = 0f }; yFf.style.width = 48;
                ySl.RegisterValueChangedCallback(e =>
                {
                    if (!P2dGetSelPt(out var lp, out _)) return;
                    yFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    lp.Points[_p2dSelPt] = new Vector2(lp.Points[_p2dSelPt].x, e.newValue);
                    D(); RefreshP2dCanvas();
                });
                ySl.RegisterCallback<PointerDownEvent>(_ => P2dBegin());
                ySl.RegisterCallback<PointerUpEvent>(_ => P2dCommit("点Y編集"));
                yFf.RegisterValueChangedCallback(e =>
                {
                    if (!P2dGetSelPt(out var lp, out _)) return;
                    P2dBegin();
                    ySl.SetValueWithoutNotify(e.newValue); lp.Points[_p2dSelPt] = new Vector2(lp.Points[_p2dSelPt].x, e.newValue);
                    D(); RefreshP2dCanvas();
                    P2dCommit("点Y編集");
                });
                var yRow = new VisualElement(); yRow.style.flexDirection = FlexDirection.Row; yRow.style.marginBottom = 2;
                yRow.Add(ML("Y")); yRow.Add(ySl); yRow.Add(yFf);
                _p2dPtRow.Add(yRow);
                _p2dPtYSlider = ySl; _p2dPtYField = yFf;
            }
            _p2dPtRow.style.display = DisplayStyle.None;
            pe.Add(_p2dPtRow);

            // ── CSV ────────────────────────────────────────────────────────
            var p2dCsvFold = FoldSection(pe, T("ProfileCsvSection"), false);
            var csvTf = new TextField();
            csvTf.RegisterValueChangedCallback(e => { _p2dCsvPath = e.newValue; RecentPaths.Set(P2dCsvKey, e.newValue); });
            // PMX読込と同じ操作感：[...] も「読込」も必ずダイアログを出す。
            void LoadP2dCsv()
            {
                string sel = PlayerIoUiKit.AskLoadPath(T("LoadCSV"), _p2dCsvPath, "csv");
                if (string.IsNullOrEmpty(sel)) return;
                _p2dCsvPath = sel;
                csvTf.value = _p2dCsvPath;

                try
                {
                    var lines = System.IO.File.ReadAllLines(_p2dCsvPath);
                    var loaded = ParseProfile2DCSV(lines);
                    if (loaded != null) { P2dBegin(); _p2dLoops = loaded; _p2dSelLoop = 0; _p2dSel.Clear(); _p2dSelPt = -1; P2dCommit("CSV読込"); D(); RebuildSettings(); }
                }
                catch (System.Exception ex) { Debug.LogWarning($"[P2D CSV] {ex.Message}"); }
            }

            p2dCsvFold.Add(PlayerIoUiKit.PathRow(csvTf, LoadP2dCsv));
            if (string.IsNullOrEmpty(_p2dCsvPath)) _p2dCsvPath = RecentPaths.Get(P2dCsvKey);
            if (!string.IsNullOrEmpty(_p2dCsvPath)) csvTf.SetValueWithoutNotify(_p2dCsvPath);

            p2dCsvFold.Add(PlayerIoUiKit.WideBtn(T("LoadCSV"), LoadP2dCsv));
            p2dCsvFold.Add(PlayerIoUiKit.WideBtn(T("SaveCSV"), () =>
            {
                if (_p2dLoops == null) return;

                // パス欄は読込用。保存は毎回ダイアログを出し、パス欄の値は初期値としてだけ使う。
                string save = PlayerIoUiKit.AskSavePath(T("SaveCSV"), _p2dCsvPath, "profile2d.csv", "csv");
                if (string.IsNullOrEmpty(save)) return;
                _p2dCsvPath = save;
                csvTf.SetValueWithoutNotify(_p2dCsvPath);
                RecentPaths.Set(P2dCsvKey, _p2dCsvPath);
                try
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var lp in _p2dLoops)
                    {
                        if (lp.IsHole && lp.Points.Count > 0)
                            sb.AppendLine($"{lp.Points[0].x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lp.Points[0].y.ToString(System.Globalization.CultureInfo.InvariantCulture)},hole");
                        else if (lp.Points.Count > 0)
                            sb.AppendLine($"{lp.Points[0].x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lp.Points[0].y.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                        for (int pi = 1; pi < lp.Points.Count; pi++)
                            sb.AppendLine($"{lp.Points[pi].x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lp.Points[pi].y.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                        sb.AppendLine();
                    }
                    System.IO.File.WriteAllText(_p2dCsvPath, sb.ToString());
                }
                catch (System.Exception ex) { Debug.LogWarning($"[P2D CSV] {ex.Message}"); }
            }));

            // ── メッシュ⇄プロファイル ─────────────────────────────────────
            var p2dIoFold = FoldSection(pe, T("MeshProfileIO"), false);
            var ioRow = new VisualElement(); ioRow.style.flexDirection = FlexDirection.Row; ioRow.style.marginBottom = 4;
            SB(ioRow, T("ImportFromMesh"), ImportProfile2DFromMesh);
            SB(ioRow, T("ApplyToMesh"),    ApplyProfile2DToMesh);
            p2dIoFold.Add(ioRow);

            // ── パラメータ ────────────────────────────────────────────────
            c.Add(SL(T("Scale")));
            c.Add(SR(T("Scale"),   0.01f, 10f, () => _p2dP.Scale,    v => { _p2dP.Scale    = v; D(); }));
            c.Add(SR(T("OffsetX"), -5f,   5f,  () => _p2dP.Offset.x, v => { _p2dP.Offset = new Vector2(v, _p2dP.Offset.y); D(); }));
            c.Add(SR(T("OffsetY"), -5f,   5f,  () => _p2dP.Offset.y, v => { _p2dP.Offset = new Vector2(_p2dP.Offset.x, v); D(); }));
            c.Add(TR(T("FlipY"),               () => _p2dP.FlipY,    v => { _p2dP.FlipY    = v; D(); }));
            c.Add(TR(T("SymmetryMode"),        () => _p2dP.SymmetryMode, v => { _p2dP.SymmetryMode = v; D(); }));
            c.Add(SR(T("Thickness"), 0f, 2f,   () => _p2dP.Thickness, v => { _p2dP.Thickness = v; D(); UpdateP2dEdgeVis(); }));

            // 角処理(ベベル)UI は常時生成し、Thickness/Segments に応じて表示切替
            // （ビルド時条件生成だと Thickness を後から上げても出ないため）
            _p2dEdgeLabel     = SL(T("EdgeSettings"));
            _p2dEdgeFrontSeg  = IR(T("FrontSegments"), 0, 16, () => _p2dP.SegmentsFront, v => { _p2dP.SegmentsFront = v; D(); UpdateP2dEdgeVis(); });
            _p2dEdgeFrontSize = SR(T("EdgeSize"), 0.01f, 0.5f, () => _p2dP.EdgeSizeFront, v => { _p2dP.EdgeSizeFront = v; D(); });
            _p2dEdgeBackSeg   = IR(T("BackSegments"),  0, 16, () => _p2dP.SegmentsBack, v => { _p2dP.SegmentsBack = v; D(); UpdateP2dEdgeVis(); });
            _p2dEdgeBackSize  = SR(T("EdgeSize"), 0.01f, 0.5f, () => _p2dP.EdgeSizeBack, v => { _p2dP.EdgeSizeBack = v; D(); });
            _p2dEdgeInward    = TR(T("EdgeInward"), () => _p2dP.EdgeInward, v => { _p2dP.EdgeInward = v; D(); });
            c.Add(_p2dEdgeLabel); c.Add(_p2dEdgeFrontSeg); c.Add(_p2dEdgeFrontSize);
            c.Add(_p2dEdgeBackSeg); c.Add(_p2dEdgeBackSize); c.Add(_p2dEdgeInward);
            UpdateP2dEdgeVis();

            BuildPivotXYZ(c,
                () => _p2dP.Pivot, v => { _p2dP.Pivot = v; D(); },
                -0.5f, 0.5f,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);
        }

        /// <summary>角処理(ベベル)UI の表示を Thickness/Segments に応じて更新する。</summary>
        private void UpdateP2dEdgeVis()
        {
            if (_p2dEdgeLabel == null) return;
            bool thick = _p2dP.Thickness > 0.001f;
            _p2dEdgeLabel.style.display     = thick ? DisplayStyle.Flex : DisplayStyle.None;
            _p2dEdgeFrontSeg.style.display  = thick ? DisplayStyle.Flex : DisplayStyle.None;
            _p2dEdgeFrontSize.style.display = (thick && _p2dP.SegmentsFront > 0) ? DisplayStyle.Flex : DisplayStyle.None;
            _p2dEdgeBackSeg.style.display   = thick ? DisplayStyle.Flex : DisplayStyle.None;
            _p2dEdgeBackSize.style.display  = (thick && _p2dP.SegmentsBack > 0) ? DisplayStyle.Flex : DisplayStyle.None;
            _p2dEdgeInward.style.display    = (thick && (_p2dP.SegmentsFront > 0 || _p2dP.SegmentsBack > 0)) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── P2D 座標変換 ──────────────────────────────────────────────────

        private Vector2 P2dWorldToCanvas(Vector2 world, float w, float h)
        {
            float scale = Mathf.Min(w, h) * 0.4f * _p2dZoom;
            return new Vector2(w * 0.5f + world.x * scale + _p2dOffset.x,
                               h * 0.5f - world.y * scale + _p2dOffset.y);
        }

        private Vector2 P2dCanvasToWorld(Vector2 canvas, float w, float h)
        {
            float scale = Mathf.Min(w, h) * 0.4f * _p2dZoom;
            return new Vector2( (canvas.x - w * 0.5f - _p2dOffset.x) / scale,
                               -(canvas.y - h * 0.5f - _p2dOffset.y) / scale);
        }

        /// <summary>点→セグメント最近傍距離と位置を返す</summary>
        private static float P2dDistToSeg(Vector2 pt, Vector2 a, Vector2 b, out Vector2 closest)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(pt - a, ab) / ab.sqrMagnitude);
            closest = a + ab * t;
            return Vector2.Distance(pt, closest);
        }

        // ── P2D キャンバス描画 ────────────────────────────────────────────

        private void OnDrawP2dCanvas(MeshGenerationContext ctx)
        {
            if (_p2dLoops == null || _p2dLoops.Count == 0) return;
            float w = _p2dCanvas.resolvedStyle.width;
            float h = _p2dCanvas.resolvedStyle.height;
            if (w <= 0 || h <= 0) return;

            var p2d = ctx.painter2D;

            // グリッド
            p2d.strokeColor = new Color(0.27f, 0.27f, 0.32f);
            p2d.lineWidth   = 1f;
            p2d.BeginPath();
            for (float gx = -5f; gx <= 5f; gx += 0.5f)
            {
                var s = P2dWorldToCanvas(new Vector2(gx, -5f), w, h);
                var e = P2dWorldToCanvas(new Vector2(gx,  5f), w, h);
                if (s.x >= 0 && s.x <= w) { p2d.MoveTo(s); p2d.LineTo(e); }
            }
            for (float gy = -5f; gy <= 5f; gy += 0.5f)
            {
                var s = P2dWorldToCanvas(new Vector2(-5f, gy), w, h);
                var e = P2dWorldToCanvas(new Vector2( 5f, gy), w, h);
                if (s.y >= 0 && s.y <= h) { p2d.MoveTo(s); p2d.LineTo(e); }
            }
            p2d.Stroke();

            // 軸
            p2d.strokeColor = new Color(0.5f, 0.5f, 0.55f);
            p2d.lineWidth   = 1.5f;
            p2d.BeginPath();
            p2d.MoveTo(P2dWorldToCanvas(new Vector2(-5f, 0f), w, h));
            p2d.LineTo(P2dWorldToCanvas(new Vector2( 5f, 0f), w, h));
            p2d.MoveTo(P2dWorldToCanvas(new Vector2(0f, -5f), w, h));
            p2d.LineTo(P2dWorldToCanvas(new Vector2(0f,  5f), w, h));
            p2d.Stroke();

            // ループ描画
            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                if (lp.Points.Count < 2) continue;

                Color lineColor = (li == _p2dSelLoop) ? Color.yellow
                                : lp.IsHole           ? new Color(1f, 0.3f, 0.3f)
                                :                       new Color(0.2f, 0.75f, 0.85f);

                for (int ei = 0; ei < lp.Points.Count; ei++)
                {
                    int   nxt = (ei + 1) % lp.Points.Count;
                    var   a   = P2dWorldToCanvas(lp.Points[ei],  w, h);
                    var   b   = P2dWorldToCanvas(lp.Points[nxt], w, h);
                    bool  hov = (li == _p2dHoverEL && ei == _p2dHoverEI);

                    p2d.strokeColor = hov ? new Color(0.2f, 0.9f, 0.3f) : lineColor;
                    p2d.lineWidth   = hov ? 3f : 1.5f;
                    p2d.BeginPath();
                    p2d.MoveTo(a); p2d.LineTo(b);
                    p2d.Stroke();
                }

                // 頂点ドット（頂点表示OFFで抑止）
                if (_p2dShowVerts)
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    bool primary = (li == _p2dSelLoop && pi == _p2dSelPt);
                    bool sel     = _p2dSel.Contains(P2dKey(li, pi));
                    var  sp      = P2dWorldToCanvas(lp.Points[pi], w, h);
                    p2d.fillColor = primary ? Color.white
                                  : sel     ? new Color(1f, 0.85f, 0.2f)
                                  :           lineColor;
                    float r = (sel || primary) ? 5.5f : 3.5f;
                    RevFillCircle(p2d, sp, r, 10);
                }
            }

            // マーキー
            if (_p2dMarquee.Active)
                _p2dMarquee.Draw(p2d, new Color(1f, 0.85f, 0.2f, 0.9f));

            // アンカー（ギズモ表示OFFで抑止。アンカー設定中は常に表示）
            if (_p2dShowGizmo || _p2dAnchor.Mode)
                _p2dAnchor.Draw(p2d, P2dWorldToCanvas(_p2dAnchor.Value, w, h));

            // 回転/拡大縮小ハンドル（アンカー設定モード中/ギズモ表示OFFで非表示）
            if (_p2dShowGizmo && !_p2dAnchor.Mode)
                _p2dHandle.Draw(p2d, P2dWorldToCanvas(_p2dAnchor.Value, w, h));

            // マグネット半径（選択点まわり）
            if (_p2dMagnet.Enabled && _p2dSel.Count > 0)
            {
                var centers = new List<Vector2>();
                foreach (var k in _p2dSel)
                {
                    int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                    if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                        centers.Add(P2dWorldToCanvas(_p2dLoops[li].Points[pi], w, h));
                }
                float cr = Vector2.Distance(P2dWorldToCanvas(Vector2.zero, w, h),
                                            P2dWorldToCanvas(new Vector2(_p2dMagnet.Radius, 0f), w, h));
                _p2dMagnet.DrawRadius(p2d, centers, cr);
            }
        }

        private void RefreshP2dCanvas() => _p2dCanvas?.MarkDirtyRepaint();

        private bool P2dGetSelPt(out Loop loop, out Vector2 pt)
        {
            loop = null; pt = Vector2.zero;
            if (_p2dLoops == null || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return false;
            loop = _p2dLoops[_p2dSelLoop];
            if (_p2dSelPt < 0 || _p2dSelPt >= loop.Points.Count) return false;
            pt = loop.Points[_p2dSelPt];
            return true;
        }

        private void RefreshP2dPointUI()
        {
            if (_p2dPtRow == null) return;
            if (P2dGetSelPt(out _, out var pt))
            {
                _p2dPtXSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.x, -5f, 5f));
                _p2dPtXField?.SetValueWithoutNotify((float)Math.Round(pt.x, 3));
                _p2dPtYSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.y, -5f, 5f));
                _p2dPtYField?.SetValueWithoutNotify((float)Math.Round(pt.y, 3));
                _p2dPtRow.style.display = DisplayStyle.Flex;
            }
            else
            {
                _p2dPtRow.style.display = DisplayStyle.None;
            }
            RefreshP2dAnchorAuto();
        }

        // ── P2D ポインターイベント ────────────────────────────────────────

        private void OnP2dPointerDown(PointerDownEvent e)
        {
            // 中ボタン＝ビューのパン
            if (e.button == 2)
            {
                _p2dPanDrag        = true;
                _p2dPanStart       = e.localPosition;
                _p2dPanOffsetStart = _p2dOffset;
                _p2dCanvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }
            if (e.button != 0) return;

            // 下絵移動モード
            if (_p2dBgMode && _p2dBgTex != null)
            {
                _p2dBgDrag              = true;
                _p2dBgDragStart         = e.localPosition;
                _p2dBgOffsetOnDragStart = _p2dBgOffset;
                _p2dCanvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }

            EnsureP2DLoops();
            float w  = _p2dCanvas.resolvedStyle.width;
            float h  = _p2dCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // アンカー設定モード：ドラッグでアンカー移動
            if (_p2dAnchor.Mode)
            {
                _p2dAnchor.Value = P2dCanvasToWorld(cp, w, h);
                _p2dAnchor.Manual = true;
                _p2dAnchorDrag = true;
                RefreshP2dAnchorFields();
                _p2dCanvas.CapturePointer(e.pointerId);
                RefreshP2dCanvas();
                e.StopPropagation(); return;
            }

            // 0. ハンドルヒット判定（回転/拡大縮小、点編集より優先。ギズモ表示OFFで無効）
            var p2dHit = _p2dShowGizmo
                ? _p2dHandle.HitTest(cp, P2dWorldToCanvas(_p2dAnchor.Value, w, h))
                : Canvas2DHandle.HandleType.None;
            if (p2dHit != Canvas2DHandle.HandleType.None)
            {
                BeginP2dHandle(p2dHit, cp, w, h);
                _p2dCanvas.CapturePointer(e.pointerId);
                RefreshP2dCanvas();
                e.StopPropagation(); return;
            }

            // 1. 頂点ヒット判定（全ループ、15px以内）
            int   bestL = -1, bestP = -1;
            float bestD = 15f;
            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    float d = Vector2.Distance(cp, P2dWorldToCanvas(lp.Points[pi], w, h));
                    if (d < bestD) { bestD = d; bestL = li; bestP = pi; }
                }
            }
            if (bestL >= 0)
            {
                long key = P2dKey(bestL, bestP);
                // Ctrl=除外（本体3Dと同じ。ドラッグしない）
                if (e.ctrlKey)
                {
                    bool removed = _p2dSel.Remove(key);
                    if (removed && _p2dSelLoop == bestL && _p2dSelPt == bestP) _p2dSelPt = -1;
                    _p2dCanvas.CapturePointer(e.pointerId);
                    RefreshP2dCanvas(); RefreshP2dPointUI();
                    e.StopPropagation(); return;
                }
                // Shift=追加（トグルではない）／無修飾=別ループなら全点選択・同ループ未選択なら置換
                if (e.shiftKey) _p2dSel.Add(key);
                else if (bestL != _p2dSelLoop) P2dSelectAllInLoop(bestL);   // 別ループ → そのループ全点を選択（一度）
                else if (!_p2dSel.Contains(key)) { _p2dSel.Clear(); _p2dSel.Add(key); }
                _p2dSelLoop = bestL; _p2dSelPt = bestP;
                P2dBegin();
                BeginP2dDrag(cp, w, h);
                _p2dCanvas.CapturePointer(e.pointerId);
                RefreshP2dCanvas(); RefreshP2dPointUI();
                e.StopPropagation(); return;
            }

            // 2. エッジヒット判定（10px以内）→ 即時挿入
            int   edgeL = -1, edgeI = -1;
            float edgeD = 10f;
            Vector2 insertWorld = Vector2.zero;
            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                if (lp.Points.Count < 2) continue;
                for (int ei = 0; ei < lp.Points.Count; ei++)
                {
                    int nxt = (ei + 1) % lp.Points.Count;
                    var a   = P2dWorldToCanvas(lp.Points[ei],  w, h);
                    var b   = P2dWorldToCanvas(lp.Points[nxt], w, h);
                    float d = P2dDistToSeg(cp, a, b, out var closest);
                    if (d < edgeD)
                    {
                        edgeD  = d; edgeL  = li; edgeI  = ei;
                        insertWorld = P2dCanvasToWorld(closest, w, h);
                    }
                }
            }
            if (edgeL >= 0)
            {
                int insertIdx = edgeI + 1;
                P2dBegin();
                _p2dLoops[edgeL].Points.Insert(insertIdx, insertWorld);
                _p2dSel.Clear(); _p2dSel.Add(P2dKey(edgeL, insertIdx));
                _p2dSelLoop = edgeL; _p2dSelPt = insertIdx;
                BeginP2dDrag(cp, w, h);
                _p2dCanvas.CapturePointer(e.pointerId);
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
                e.StopPropagation(); return;
            }

            // 3. 空領域 → 矩形/投げ縄マーキー選択（Shift=追加, Ctrl=除外, 無修飾=置換）
            _p2dMarqueeAdditive = e.shiftKey;
            _p2dMarqueeSubtract = e.ctrlKey;
            _p2dMarquee.Begin(cp, _p2dLassoMode);
            _p2dMarqueeDrag = true;
            _p2dCanvas.CapturePointer(e.pointerId);
            RefreshP2dCanvas();
            e.StopPropagation();
        }

        private static long P2dKey(int loop, int pt) => ((long)loop << 32) | (uint)pt;
        private static int  P2dKeyLoop(long k) => (int)(k >> 32);
        private static int  P2dKeyPt(long k)   => (int)(k & 0xffffffff);

        /// <summary>指定ループの全頂点を選択状態にする（主点は先頭）。</summary>
        private void P2dSelectAllInLoop(int li)
        {
            _p2dSel.Clear();
            if (_p2dLoops == null || li < 0 || li >= _p2dLoops.Count) { _p2dSelPt = -1; return; }
            var lp = _p2dLoops[li];
            for (int pi = 0; pi < lp.Points.Count; pi++) _p2dSel.Add(P2dKey(li, pi));
            _p2dSelPt = lp.Points.Count > 0 ? 0 : -1;
        }

        /// <summary>選択点の一括ドラッグ開始（各点の開始位置とカーソル基準を記録）。</summary>
        private void BeginP2dDrag(Vector2 cp, float w, float h)
        {
            _p2dDrag = true; _p2dDragIdx = _p2dSelPt;
            _p2dDragStart.Clear();
            foreach (var k in _p2dSel)
            {
                int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                    _p2dDragStart[k] = _p2dLoops[li].Points[pi];
            }
            _p2dDragStartCursorWorld = P2dCanvasToWorld(cp, w, h);

            // マグネット影響点（非選択で半径内）を確定
            _p2dMagnetStart.Clear(); _p2dMagnetW.Clear();
            if (_p2dMagnet.Enabled && _p2dSel.Count > 0)
            {
                var sel = new List<Vector2>();
                foreach (var k in _p2dSel)
                {
                    int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                    if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                        sel.Add(_p2dLoops[li].Points[pi]);
                }
                for (int li = 0; li < _p2dLoops.Count; li++)
                {
                    var lp = _p2dLoops[li];
                    for (int pi = 0; pi < lp.Points.Count; pi++)
                    {
                        long key = P2dKey(li, pi);
                        if (_p2dSel.Contains(key)) continue;
                        float wt = _p2dMagnet.WeightFor(lp.Points[pi], sel);
                        if (wt > 0f) { _p2dMagnetStart[key] = lp.Points[pi]; _p2dMagnetW[key] = wt; }
                    }
                }
            }
        }

        /// <summary>マーキー内側の点を選択に反映する。</summary>
        private void ApplyP2dMarquee()
        {
            float w = _p2dCanvas.resolvedStyle.width, h = _p2dCanvas.resolvedStyle.height;
            if (!_p2dMarqueeAdditive && !_p2dMarqueeSubtract) _p2dSel.Clear();
            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                for (int pi = 0; pi < lp.Points.Count; pi++)
                    if (_p2dMarquee.Contains(P2dWorldToCanvas(lp.Points[pi], w, h)))
                    {
                        if (_p2dMarqueeSubtract) _p2dSel.Remove(P2dKey(li, pi));
                        else                     _p2dSel.Add(P2dKey(li, pi));
                    }
            }
            if (!_p2dSel.Contains(P2dKey(_p2dSelLoop, _p2dSelPt)))
            {
                if (_p2dSel.Count > 0) { foreach (var k in _p2dSel) { _p2dSelLoop = P2dKeyLoop(k); _p2dSelPt = P2dKeyPt(k); break; } }
                else _p2dSelPt = -1;
            }
            RefreshP2dCanvas(); RefreshP2dPointUI();
        }

        // ── 回転/拡大縮小アンカー・変換（Profile2D） ──────────────────────

        /// <summary>選択（無ければ全点）のワールド座標リスト。</summary>
        private List<Vector2> SelectedP2dPoints()
        {
            var pts = new List<Vector2>();
            if (_p2dLoops == null) return pts;
            if (_p2dSel.Count > 0)
            {
                foreach (var k in _p2dSel)
                {
                    int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                    if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                        pts.Add(_p2dLoops[li].Points[pi]);
                }
            }
            else
            {
                foreach (var lp in _p2dLoops) pts.AddRange(lp.Points);
            }
            return pts;
        }

        private void BuildP2dAnchorTransformUI(VisualElement pe)
        {
            var tfFold = FoldSection(pe, T("SelectionTransform"), false);
            tfFold.Add(BuildTf2("移動 X/Y",   0f, 0f, out _p2dTfMoveX,  out _p2dTfMoveY));
            tfFold.Add(BuildTf2("スケール X/Y", 1f, 1f, out _p2dTfScaleX, out _p2dTfScaleY));
            tfFold.Add(BuildTf1("スケール軸 (°)", 0f, out _p2dTfScaleAxis));
            tfFold.Add(BuildTf1("回転 (°)",    0f, out _p2dTfRot));
            var applyRow = new VisualElement(); applyRow.style.flexDirection = FlexDirection.Row; applyRow.style.marginBottom = 4;
            SB(applyRow, "変換適用", ApplyP2dTransform);
            SB(applyRow, "リセット", () =>
            {
                _p2dTfMoveX.value = 0f; _p2dTfMoveY.value = 0f;
                _p2dTfScaleX.value = 1f; _p2dTfScaleY.value = 1f; _p2dTfRot.value = 0f;
                _p2dTfScaleAxis.value = 0f;
            });
            tfFold.Add(applyRow);

            // マグネット（比例編集）
            var magFold = FoldSection(pe, T("Magnet"), false);
            var p2dMagRow = new VisualElement(); p2dMagRow.style.flexDirection = FlexDirection.Row; p2dMagRow.style.marginBottom = 2;
            var p2dMagToggle = new Toggle("有効") { value = _p2dMagnet.Enabled }; p2dMagToggle.style.marginRight = 6;
            p2dMagToggle.RegisterValueChangedCallback(ev => { _p2dMagnet.Enabled = ev.newValue; RefreshP2dCanvas(); });
            var p2dFalloff = new EnumField(_p2dMagnet.Falloff); p2dFalloff.style.flexGrow = 1;
            p2dFalloff.RegisterValueChangedCallback(ev => _p2dMagnet.Falloff = (FalloffType)ev.newValue);
            p2dMagRow.Add(p2dMagToggle); p2dMagRow.Add(p2dFalloff);
            magFold.Add(p2dMagRow);
            magFold.Add(BuildAnchorRow("半径", 0.05f, 5f, _p2dMagnet.Radius, out _p2dMagnetRadius, out _,
                () => false, v => { _p2dMagnet.Radius = v; RefreshP2dCanvas(); }));

            var anchorFold = FoldSection(pe, T("AnchorSection"), false);
            _p2dAnchorEnterBtn = new Button(() => SetP2dAnchorMode(true)) { text = "アンカー設定" };
            _p2dAnchorEnterBtn.style.marginBottom = 2;
            anchorFold.Add(_p2dAnchorEnterBtn);

            _p2dAnchorPanel = new VisualElement(); _p2dAnchorPanel.style.marginBottom = 4;
            {
                var headRow = new VisualElement(); headRow.style.flexDirection = FlexDirection.Row; headRow.style.marginBottom = 2;
                var lbl = new Label("アンカー調整中（キャンバスをドラッグで移動）"); lbl.style.fontSize = 10; lbl.style.flexGrow = 1; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
                var done = new Button(() => SetP2dAnchorMode(false)) { text = "決定" }; done.style.width = 60;
                headRow.Add(lbl); headRow.Add(done); _p2dAnchorPanel.Add(headRow);

                var presetRow = new VisualElement(); presetRow.style.flexDirection = FlexDirection.Row; presetRow.style.marginBottom = 2;
                SB(presetRow, "重心", () => ApplyP2dAnchorPreset(Canvas2DAnchor.Preset.Centroid));
                SB(presetRow, "中心", () => ApplyP2dAnchorPreset(Canvas2DAnchor.Preset.Center));
                SB(presetRow, "左上", () => ApplyP2dAnchorPreset(Canvas2DAnchor.Preset.TopLeft));
                SB(presetRow, "左下", () => ApplyP2dAnchorPreset(Canvas2DAnchor.Preset.BottomLeft));
                _p2dAnchorPanel.Add(presetRow);

                _p2dAnchorPanel.Add(BuildAnchorRow("X", -5f, 5f, 0f, out _p2dAnchorXSlider, out _p2dAnchorXField,
                    () => _p2dAnchorSuppress, v => SetP2dAnchorComponent(true, v)));
                _p2dAnchorPanel.Add(BuildAnchorRow("Y", -5f, 5f, 0f, out _p2dAnchorYSlider, out _p2dAnchorYField,
                    () => _p2dAnchorSuppress, v => SetP2dAnchorComponent(false, v)));
            }
            anchorFold.Add(_p2dAnchorPanel);
            RefreshP2dAnchorModeUI();
            RefreshP2dAnchorFields();
        }

        private void SetP2dAnchorMode(bool on)
        {
            _p2dAnchor.Mode = on;
            if (on) RefreshP2dAnchorAuto();
            RefreshP2dAnchorModeUI();
            RefreshP2dCanvas();
        }
        private void RefreshP2dAnchorModeUI()
        {
            if (_p2dAnchorEnterBtn != null) _p2dAnchorEnterBtn.style.display = _p2dAnchor.Mode ? DisplayStyle.None : DisplayStyle.Flex;
            if (_p2dAnchorPanel    != null) _p2dAnchorPanel.style.display    = _p2dAnchor.Mode ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private void RefreshP2dAnchorFields()
        {
            _p2dAnchorSuppress = true;
            _p2dAnchorXSlider?.SetValueWithoutNotify(Mathf.Clamp(_p2dAnchor.Value.x, -5f, 5f));
            _p2dAnchorYSlider?.SetValueWithoutNotify(Mathf.Clamp(_p2dAnchor.Value.y, -5f, 5f));
            _p2dAnchorXField?.SetValueWithoutNotify(_p2dAnchor.Value.x);
            _p2dAnchorYField?.SetValueWithoutNotify(_p2dAnchor.Value.y);
            _p2dAnchorSuppress = false;
        }
        private void RefreshP2dAnchorAuto()
        {
            if (_p2dAnchor.Manual) return;
            var pts = SelectedP2dPoints();
            if (pts.Count > 0) _p2dAnchor.SetPreset(pts, Canvas2DAnchor.Preset.Centroid);
            RefreshP2dAnchorFields();
        }
        private void SetP2dAnchorComponent(bool isX, float v)
        {
            var a = _p2dAnchor.Value; if (isX) a.x = v; else a.y = v; _p2dAnchor.Value = a;
            _p2dAnchor.Manual = true;
            RefreshP2dAnchorFields(); RefreshP2dCanvas();
        }
        private void ApplyP2dAnchorPreset(Canvas2DAnchor.Preset p)
        {
            _p2dAnchor.SetPreset(SelectedP2dPoints(), p);
            RefreshP2dAnchorFields(); RefreshP2dCanvas();
        }

        private void ApplyP2dTransform()
        {
            if (_p2dLoops == null) return;
            P2dBegin();
            RefreshP2dAnchorAuto();
            var a = _p2dAnchor.Value;
            float mx = _p2dTfMoveX?.value ?? 0f, my = _p2dTfMoveY?.value ?? 0f;
            float sx = _p2dTfScaleX?.value ?? 1f, sy = _p2dTfScaleY?.value ?? 1f;
            float deg = _p2dTfRot?.value ?? 0f;
            float saRad = (_p2dTfScaleAxis?.value ?? 0f) * Mathf.Deg2Rad;
            float saCos = Mathf.Cos(saRad), saSin = Mathf.Sin(saRad);

            bool useSel = _p2dSel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel)
                foreach (var k in _p2dSel)
                {
                    int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                    if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                        sel.Add(_p2dLoops[li].Points[pi]);
                }

            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    float wt;
                    if (!useSel)                             wt = 1f;
                    else if (_p2dSel.Contains(P2dKey(li, pi))) wt = 1f;
                    else wt = _p2dMagnet.Enabled ? _p2dMagnet.WeightFor(lp.Points[pi], sel) : 0f;
                    if (wt <= 0f) continue;
                    lp.Points[pi] = Xform2D(lp.Points[pi], a, mx, my, sx, sy, saCos, saSin, deg, wt);
                }
            }
            P2dCommit("変換適用");
            D(); RefreshP2dCanvas(); RefreshP2dPointUI();
        }

        // ── 2D押し出し：ハンドルドラッグ（回転/拡大縮小） ─────────────────

        /// <summary>ハンドルドラッグ開始。影響点（選択=1/マグネット=weight、選択なし=全点1）を記録。</summary>
        private void BeginP2dHandle(Canvas2DHandle.HandleType type, Vector2 cp, float w, float h)
        {
            _p2dHandleDrag = true;
            _p2dHandleType = type;
            _p2dHandle.Active = type;
            RefreshP2dAnchorAuto();
            P2dBegin();

            _p2dHandleAnchorC   = P2dWorldToCanvas(_p2dAnchor.Value, w, h);
            _p2dHandlePrevAngle = Canvas2DHandle.AngleDeg(_p2dHandleAnchorC, cp);
            _p2dHandleTotalDeg  = 0f;

            _p2dHandleStart.Clear(); _p2dHandleW.Clear();
            if (_p2dLoops == null) return;

            bool useSel = _p2dSel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel)
                foreach (var k in _p2dSel)
                {
                    int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                    if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                        sel.Add(_p2dLoops[li].Points[pi]);
                }

            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    long key = P2dKey(li, pi);
                    float wt;
                    if (!useSel)                    wt = 1f;
                    else if (_p2dSel.Contains(key)) wt = 1f;
                    else wt = _p2dMagnet.Enabled ? _p2dMagnet.WeightFor(lp.Points[pi], sel) : 0f;
                    if (wt <= 0f) continue;
                    _p2dHandleStart[key] = lp.Points[pi];
                    _p2dHandleW[key]     = wt;
                }
            }
        }

        /// <summary>ハンドルドラッグ中：開始スナップショットへ変換を適用（ライブプレビュー）。</summary>
        private void ApplyP2dHandle(Vector2 cp, float w, float h)
        {
            if (!_p2dHandleDrag || _p2dLoops == null) return;

            float sx = 1f, sy = 1f, deg = 0f;
            if (_p2dHandleType == Canvas2DHandle.HandleType.Rotate)
            {
                float ang = Canvas2DHandle.AngleDeg(_p2dHandleAnchorC, cp);
                _p2dHandleTotalDeg += -Mathf.DeltaAngle(_p2dHandlePrevAngle, ang);
                _p2dHandlePrevAngle = ang;
                deg = _p2dHandleTotalDeg;
            }
            else
            {
                _p2dHandle.ScaleFactors(_p2dHandleType, _p2dHandleAnchorC, cp, out sx, out sy);
            }

            var a = _p2dAnchor.Value;
            foreach (var kv in _p2dHandleStart)
            {
                int li = P2dKeyLoop(kv.Key), pi = P2dKeyPt(kv.Key);
                if (li < 0 || li >= _p2dLoops.Count) continue;
                if (pi < 0 || pi >= _p2dLoops[li].Points.Count) continue;
                _p2dLoops[li].Points[pi] = Xform2D(kv.Value, a, 0f, 0f, sx, sy, 1f, 0f, deg, _p2dHandleW[kv.Key]);
            }
            D(); RefreshP2dCanvas(); RefreshP2dPointUI();
        }

        /// <summary>ハンドルドラッグ終了：コミット。</summary>
        private void EndP2dHandle()
        {
            if (!_p2dHandleDrag) return;
            _p2dHandleDrag = false;
            _p2dHandleType = Canvas2DHandle.HandleType.None;
            _p2dHandle.Active = Canvas2DHandle.HandleType.None;
            P2dCommit("回転/拡大縮小");
        }

        private void OnP2dPointerMove(PointerMoveEvent e)
        {
            float w  = _p2dCanvas.resolvedStyle.width;
            float h  = _p2dCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // 中ボタンパン
            if (_p2dPanDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                _p2dOffset = _p2dPanOffsetStart + (cp - _p2dPanStart);
                UpdateP2dView(); UpdateP2dBgEl(); RefreshP2dCanvas();
                e.StopPropagation(); return;
            }

            // 下絵移動モード
            if (_p2dBgDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                _p2dBgOffset = _p2dBgOffsetOnDragStart
                             + (P2dCanvasToWorld(cp, w, h) - P2dCanvasToWorld(_p2dBgDragStart, w, h));
                UpdateP2dBgEl();
                e.StopPropagation(); return;
            }

            // アンカードラッグ
            if (_p2dAnchorDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                _p2dAnchor.Value = P2dCanvasToWorld(cp, w, h);
                RefreshP2dAnchorFields(); RefreshP2dCanvas();
                e.StopPropagation(); return;
            }

            // ハンドルドラッグ（回転/拡大縮小）
            if (_p2dHandleDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                ApplyP2dHandle(cp, w, h);
                e.StopPropagation(); return;
            }

            // マーキー更新
            if (_p2dMarqueeDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                _p2dMarquee.Update(cp);
                RefreshP2dCanvas();
                e.StopPropagation(); return;
            }

            if (_p2dDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                if (_p2dDragStart.Count > 0)
                {
                    // 選択点を一括で delta 移動。
                    var delta = P2dCanvasToWorld(cp, w, h) - _p2dDragStartCursorWorld;
                    foreach (var kv in _p2dDragStart)
                    {
                        int li = P2dKeyLoop(kv.Key), pi = P2dKeyPt(kv.Key);
                        if (li < 0 || li >= _p2dLoops.Count) continue;
                        if (pi < 0 || pi >= _p2dLoops[li].Points.Count) continue;
                        _p2dLoops[li].Points[pi] = kv.Value + delta;
                    }
                    // マグネット: 非選択点を delta×weight で追従
                    foreach (var kv in _p2dMagnetStart)
                    {
                        int li = P2dKeyLoop(kv.Key), pi = P2dKeyPt(kv.Key);
                        if (li < 0 || li >= _p2dLoops.Count) continue;
                        if (pi < 0 || pi >= _p2dLoops[li].Points.Count) continue;
                        _p2dLoops[li].Points[pi] = kv.Value + delta * _p2dMagnetW[kv.Key];
                    }
                    D(); RefreshP2dCanvas(); RefreshP2dPointUI();
                }
                e.StopPropagation(); return;
            }

            // ハンドルホバー更新（非ドラッグ中。ギズモ表示OFF/アンカー設定中は無効）
            var p2dHovType = (_p2dAnchor.Mode || !_p2dShowGizmo)
                                             ? Canvas2DHandle.HandleType.None
                                             : _p2dHandle.HitTest(cp, P2dWorldToCanvas(_p2dAnchor.Value, w, h));
            if (p2dHovType != _p2dHandle.Hovered) { _p2dHandle.Hovered = p2dHovType; RefreshP2dCanvas(); }

            // ホバーエッジ更新（非ドラッグ中）
            int   prevEL = _p2dHoverEL, prevEI = _p2dHoverEI;
            _p2dHoverEL = -1; _p2dHoverEI = -1;

            // 頂点近傍ならホバーなし
            bool nearPt = false;
            foreach (var lp in _p2dLoops)
                foreach (var pt in lp.Points)
                    if (Vector2.Distance(cp, P2dWorldToCanvas(pt, w, h)) < 15f) { nearPt = true; break; }

            if (!nearPt)
            {
                float bestD = 10f;
                for (int li = 0; li < _p2dLoops.Count; li++)
                {
                    var lp = _p2dLoops[li];
                    if (lp.Points.Count < 2) continue;
                    for (int ei = 0; ei < lp.Points.Count; ei++)
                    {
                        int nxt = (ei + 1) % lp.Points.Count;
                        float d = P2dDistToSeg(cp,
                            P2dWorldToCanvas(lp.Points[ei],  w, h),
                            P2dWorldToCanvas(lp.Points[nxt], w, h), out _);
                        if (d < bestD) { bestD = d; _p2dHoverEL = li; _p2dHoverEI = ei; }
                    }
                }
            }

            if (_p2dHoverEL != prevEL || _p2dHoverEI != prevEI)
                RefreshP2dCanvas();
        }

        private void OnP2dPointerUp(PointerUpEvent e)
        {
            if (!_p2dCanvas.HasPointerCapture(e.pointerId)) return;
            _p2dCanvas.ReleasePointer(e.pointerId);
            if (_p2dMarqueeDrag) { ApplyP2dMarquee(); _p2dMarquee.End(); _p2dMarqueeDrag = false; }
            if (_p2dHandleDrag) EndP2dHandle();
            bool wasP2dDrag = _p2dDrag;
            _p2dDrag   = false;
            _p2dBgDrag = false;
            _p2dPanDrag = false;
            _p2dAnchorDrag = false;
            if (wasP2dDrag) P2dCommit("プロファイル点編集");
            e.StopPropagation();
        }

        // ================================================================
        // 下絵ヘルパー（Rev/P2d 共用）
        // ================================================================

        /// <summary>下絵セクションUIを構築する共通メソッド</summary>
        private void BuildBgSection(VisualElement c, string sectionLabel,
            Func<string> getPath, Action<string> setPath,
            Func<float> getAlpha, Action<float> setAlpha,
            Func<bool>  getMode,  Action<bool>  setMode,
            Func<float> getScale, Action<float> setScale,
            Func<Vector2> getOrigin, Action<Vector2> setOrigin,
            Func<Texture2D> getTex,
            Action onLoad, Action onClear,
            out Slider scaleSlider, out Label sizeLabel)
        {
            // 見出しをフォールドのタイトルにして中身を折り畳む（既定は閉じる）。
            // 回転体・2D押し出し・断面エディタの全てで同じ見え方にする。
            var bgFold = new Foldout { text = sectionLabel, value = false };
            bgFold.style.marginBottom = 4;
            var bgHost = c;
            c = bgFold.contentContainer;
            bgHost.Add(bgFold);

            // パス入力（loadPMX デザインに統一：[...] 左＋TextField＋RecentPaths）
            string bgKey  = "Primitive.Bg." + sectionLabel;
            string bgInit = string.IsNullOrEmpty(getPath()) ? RecentPaths.Get(bgKey) : getPath();
            var pathField = new TextField();
            pathField.SetValueWithoutNotify(bgInit);
            if (!string.IsNullOrEmpty(bgInit)) setPath(bgInit);
            pathField.RegisterValueChangedCallback(e => { setPath(e.newValue); RecentPaths.Set(bgKey, e.newValue); });
            // PMX読込と同じ操作感：[...] も「読込」も必ずダイアログを出す。
            void LoadBgImage()
            {
                string path = PlayerIoUiKit.AskLoadPath("Select Image", getPath(), "png,jpg,jpeg");
                if (string.IsNullOrEmpty(path)) return;
                pathField.value = path;
                onLoad();
            }

            c.Add(PlayerIoUiKit.PathRow(pathField, LoadBgImage));

            // Load / Clear ボタン行
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 3;
            SB(row, T("BgLoad"),  LoadBgImage);
            SB(row, T("BgClear"), onClear);
            c.Add(row);

            // アルファスライダー
            c.Add(SR(T("BgAlpha"), 0f, 1f, getAlpha, v => setAlpha(v)));

            // ── 下絵操作サブモード ─────────────────────────────────────
            // 通常時は「下絵を調整」ボタン、サブモード中は「下絵調整中」＋「決定」
            // と調整パネル（スケール／原点プリセット／画素数）を表示する。
            var enterBtn = new Button { text = "下絵を調整" };
            enterBtn.style.marginBottom = 2;

            var adjustPanel = new VisualElement();
            adjustPanel.style.marginBottom = 4;

            // 「下絵調整中」＋「決定」
            var headRow = new VisualElement(); headRow.style.flexDirection = FlexDirection.Row; headRow.style.marginBottom = 2;
            var adjustingLbl = new Label("下絵調整中"); adjustingLbl.style.flexGrow = 1; adjustingLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            var doneBtn = new Button { text = "決定" }; doneBtn.style.width = 60;
            headRow.Add(adjustingLbl); headRow.Add(doneBtn);
            adjustPanel.Add(headRow);

            // スケールスライダー（0.1–10）
            var sclSlider = new Slider("スケール", 0.1f, 10f) { value = Mathf.Clamp(getScale(), 0.1f, 10f) };
            sclSlider.style.marginBottom = 2;
            sclSlider.RegisterValueChangedCallback(e => setScale(Mathf.Clamp(e.newValue, 0.1f, 10f)));
            adjustPanel.Add(sclSlider);

            // 原点プリセット（画像px基準・Y下向き）
            var presetRow = new VisualElement(); presetRow.style.flexDirection = FlexDirection.Row; presetRow.style.marginBottom = 2;
            SB(presetRow, "原点:中心", () => { var t = getTex(); if (t != null) setOrigin(new Vector2(t.width * 0.5f, t.height * 0.5f)); });
            SB(presetRow, "左上",     () => { if (getTex() != null) setOrigin(Vector2.zero); });
            SB(presetRow, "左下",     () => { var t = getTex(); if (t != null) setOrigin(new Vector2(0f, t.height)); });
            adjustPanel.Add(presetRow);

            // 画素数
            var sizeLbl = new Label("サイズ: -");
            adjustPanel.Add(sizeLbl);

            c.Add(enterBtn);
            c.Add(adjustPanel);

            void RefreshModeUI()
            {
                bool on = getMode();
                enterBtn.style.display    = on ? DisplayStyle.None : DisplayStyle.Flex;
                adjustPanel.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }
            enterBtn.clicked += () => { setMode(true);  RefreshModeUI(); };
            doneBtn.clicked  += () => { setMode(false); RefreshModeUI(); };
            RefreshModeUI();

            scaleSlider = sclSlider;
            sizeLabel   = sizeLbl;
        }

        /// <summary>画素数ラベルを更新する。</summary>
        private static void SetBgSizeLabel(Label lbl, Texture2D tex)
        {
            if (lbl == null) return;
            lbl.text = tex != null ? $"サイズ: {tex.width} × {tex.height} px" : "サイズ: -";
        }

        /// <summary>テクスチャをファイルパスから読み込んでBgElに設定</summary>
        private static void LoadBgTexture(string path, ref Texture2D tex, VisualElement bgEl)
        {
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                if (tex == null)
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    Debug.LogWarning($"[BgImage] LoadImage failed: {path}");
                    return;
                }
                bgEl.style.backgroundImage = new StyleBackground(tex);
                bgEl.style.display         = DisplayStyle.Flex;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[BgImage] Load failed: {ex.Message}");
            }
        }

        /// <summary>Rev 下絵 VisualElement の位置・原点・スケール・アルファを更新</summary>
        private void UpdateRevBgEl()
        {
            if (_revBgEl == null || _revBgTex == null) return;
            float cw = _revCanvas.resolvedStyle.width;
            float ch = _revCanvas.resolvedStyle.height;
            if (cw <= 0 || ch <= 0) return;
            float bw = _revBgTex.width;
            float bh = _revBgTex.height;
            if (bw < 0.5f || bh < 0.5f) return;

            // ジオメトリ(ProfileToCanvas)と同じ基準倍率（ズーム除く）
            float baseScale = Mathf.Min(cw / RevolutionProfileEditCore.RangeX,
                                        ch / RevolutionProfileEditCore.RangeY);
            // 画像の高さ = _revBgScale ワールド単位（アスペクト維持）
            float s = (_revBgScale * baseScale) / bh;

            // 画像中心をプロファイル点 _revBgOffset に合わせる（zoom=1,offset=0 相当）
            Vector2 c = RevolutionProfileEditCore.ProfileToCanvas(_revBgOffset, cw, ch, 1f, Vector2.zero);
            _revBgEl.style.left   = c.x - bw * 0.5f; _revBgEl.style.top = c.y - bh * 0.5f;
            _revBgEl.style.width  = bw; _revBgEl.style.height = bh;
            _revBgEl.style.transformOrigin = new TransformOrigin(
                new Length(bw * 0.5f, LengthUnit.Pixel), new Length(bh * 0.5f, LengthUnit.Pixel), 0f);
            _revBgEl.style.scale   = new Scale(new Vector3(s, s, 1f));
            _revBgEl.style.opacity = _revBgAlpha;
            _revBgEl.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(BackgroundSizeType.Cover));
        }

        /// <summary>P2D 下絵 VisualElement の位置・原点・スケール・アルファを更新</summary>
        private void UpdateP2dBgEl()
        {
            if (_p2dBgEl == null || _p2dBgTex == null) return;
            float cw = _p2dCanvas.resolvedStyle.width;
            float ch = _p2dCanvas.resolvedStyle.height;
            if (cw <= 0 || ch <= 0) return;
            float bw = _p2dBgTex.width;
            float bh = _p2dBgTex.height;
            if (bw < 0.5f || bh < 0.5f) return;

            // ジオメトリと同じワールド→キャンバス基準倍率（ズーム除く。ズームはビューレイヤーが付与）
            float baseScale = Mathf.Min(cw, ch) * 0.4f;
            // 画像の高さ = _p2dBgScale ワールド単位（アスペクト維持）
            float s = (_p2dBgScale * baseScale) / bh;

            // 画像中心をワールド点 _p2dBgOffset に合わせる（P2dWorldToCanvas の zoom=1,offset=0 相当）
            float ccx = cw * 0.5f + _p2dBgOffset.x * baseScale;
            float ccy = ch * 0.5f - _p2dBgOffset.y * baseScale;
            _p2dBgEl.style.left   = ccx - bw * 0.5f; _p2dBgEl.style.top = ccy - bh * 0.5f;
            _p2dBgEl.style.width  = bw; _p2dBgEl.style.height = bh;
            _p2dBgEl.style.transformOrigin = new TransformOrigin(
                new Length(bw * 0.5f, LengthUnit.Pixel), new Length(bh * 0.5f, LengthUnit.Pixel), 0f);
            _p2dBgEl.style.scale   = new Scale(new Vector3(s, s, 1f));
            _p2dBgEl.style.opacity = _p2dBgAlpha;
            _p2dBgEl.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(BackgroundSizeType.Cover));
        }

        /// <summary>下絵レイヤーにプロファイルビューと同じ変換（中心基準ズーム＋パン）を適用。</summary>
        private void UpdateP2dView()
        {
            if (_p2dViewLayer == null) return;
            _p2dViewLayer.style.transformOrigin = new TransformOrigin(
                new Length(50, LengthUnit.Percent), new Length(50, LengthUnit.Percent), 0f);
            _p2dViewLayer.style.scale     = new Scale(new Vector3(_p2dZoom, _p2dZoom, 1f));
            _p2dViewLayer.style.translate = new Translate(
                new Length(_p2dOffset.x), new Length(_p2dOffset.y), 0f);
        }

        private static List<Loop> ParseProfile2DCSV(string[] lines)
        {
            var loops = new List<Loop>();
            Loop current = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) { current = null; continue; }
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float x)) continue;
                if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float y)) continue;
                if (current == null)
                {
                    current = new Loop();
                    if (parts.Length >= 3 && parts[2].Trim().ToLower() == "hole") current.IsHole = true;
                    loops.Add(current);
                }
                current.Points.Add(new Vector2(x, y));
            }
            return loops.Count > 0 ? loops : null;
        }

        // ================================================================
        // NohMask UI（変更なし）
        // ================================================================

        private void BuildNohMaskUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("NohMask")));
            c.Add(NF(() => _nohP.MeshName, v => _nohP.MeshName = v));

            c.Add(PlayerIoUiKit.SectionLabel(T("Landmarks")));
            var lmField = new TextField();
            if (string.IsNullOrEmpty(_nohP.LandmarksFilePath)) _nohP.LandmarksFilePath = RecentPaths.Get(NohLmKey);
            lmField.SetValueWithoutNotify(_nohP.LandmarksFilePath ?? "");
            lmField.RegisterValueChangedCallback(e => { _nohP.LandmarksFilePath = e.newValue; RecentPaths.Set(NohLmKey, e.newValue); D(); });
            c.Add(PlayerIoUiKit.PathRow(lmField, () =>
            {
                string path = PlayerIoUiKit.AskLoadPath("Open Landmarks JSON", _nohP.LandmarksFilePath, "json");
                if (!string.IsNullOrEmpty(path)) lmField.value = path;
            }));

            c.Add(PlayerIoUiKit.SectionLabel(T("TrianglesJson")));
            var triField = new TextField();
            if (string.IsNullOrEmpty(_nohP.TrianglesFilePath)) _nohP.TrianglesFilePath = RecentPaths.Get(NohTriKey);
            triField.SetValueWithoutNotify(_nohP.TrianglesFilePath ?? "");
            triField.RegisterValueChangedCallback(e => { _nohP.TrianglesFilePath = e.newValue; RecentPaths.Set(NohTriKey, e.newValue); D(); });
            c.Add(PlayerIoUiKit.PathRow(triField, () =>
            {
                string path = PlayerIoUiKit.AskLoadPath("Open Triangles JSON", _nohP.TrianglesFilePath, "json");
                if (!string.IsNullOrEmpty(path)) triField.value = path;
            }));

            // 内蔵デフォルト（プリセット）に戻す: パスを空にすると焼き込み済みデータを使用
            var defBtn = new Button(() =>
            {
                lmField.value  = "";
                triField.value = "";
            }) { text = T("UseBuiltinDefault") };
            defBtn.style.marginBottom = 4; c.Add(defBtn);

            c.Add(SR(T("Scale"),      1f,  10f, () => _nohP.Scale,      v => { _nohP.Scale      = v; D(); }));
            c.Add(SR(T("DepthScale"), 0.1f, 5f, () => _nohP.DepthScale, v => { _nohP.DepthScale = v; D(); }));
            c.Add(TR(T("FlipFaces"),           () => _nohP.FlipFaces,   v => { _nohP.FlipFaces  = v; D(); }));
            c.Add(TR(T("FlipX"),               () => _nohP.FlipX,       v => { _nohP.FlipX      = v; D(); }));
            c.Add(TR(T("FlipY"),               () => _nohP.FlipY,       v => { _nohP.FlipY      = v; D(); }));
            c.Add(TR(T("FlipZ"),               () => _nohP.FlipZ,       v => { _nohP.FlipZ      = v; D(); }));
            c.Add(TR(T("FillHoles"),           () => _nohP.FillHoles,   v => { _nohP.FillHoles  = v; D(); }));
            c.Add(TR(T("RimEnabled"),          () => _nohP.RimEnabled,  v => { _nohP.RimEnabled = v; D(); }));
            c.Add(SR(T("RimWidth"), 0f, 1.5f,  () => _nohP.RimWidth,    v => { _nohP.RimWidth   = v; D(); }));
            c.Add(IR(T("FaceIndex"), 0, 10,    () => _nohP.FaceIndex,   v => { _nohP.FaceIndex  = v; D(); }));

            // ── 既存メッシュを能面JSON形式で保存（能面とは独立の汎用エクスポート） ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel("メッシュをJSON保存"));
            var exportInfo = new Label("選択中の描画オブジェクトを landmarks/triangles JSON で保存");
            exportInfo.style.fontSize = 10; exportInfo.style.whiteSpace = WhiteSpace.Normal; exportInfo.style.marginBottom = 2;
            c.Add(exportInfo);
            c.Add(PlayerIoUiKit.WideBtn("選択メッシュをJSON保存", ExportSelectedMeshToNohJson));
        }

        /// <summary>選択中の描画オブジェクトのメッシュを能面JSON形式(landmarks+triangles)で保存する。生座標。</summary>
        private void ExportSelectedMeshToNohJson()
        {
            var mo = GetSelectedMeshObject?.Invoke();
            if (mo == null || mo.VertexCount == 0)
            {
                Debug.LogWarning("[PolyLing] 保存対象の描画オブジェクトがありません。");
                return;
            }

            string saveDir  = RecentPaths.Get(NohSaveKey);
            string basePath = Poly_Ling.EditorBridge.PLEditorBridge.I.SaveFilePanel(
                "メッシュをJSON保存", string.IsNullOrEmpty(saveDir) ? "" : System.IO.Path.GetDirectoryName(saveDir), "facemesh.json", "json");
            if (string.IsNullOrEmpty(basePath)) return;
            RecentPaths.Set(NohSaveKey, basePath);

            string dir  = System.IO.Path.GetDirectoryName(basePath);
            string stem = System.IO.Path.GetFileNameWithoutExtension(basePath);
            string lmPath  = System.IO.Path.Combine(dir, stem + "_landmarks.json");
            string triPath = System.IO.Path.Combine(dir, stem + "_triangles.json");

            try
            {
                System.IO.File.WriteAllText(lmPath,  NohMaskMeshExporter.BuildLandmarksJson(mo));
                System.IO.File.WriteAllText(triPath, NohMaskMeshExporter.BuildTrianglesJson(mo));
                Debug.Log($"[PolyLing] メッシュをJSON保存: {lmPath} / {triPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PolyLing] JSON保存に失敗: {ex.Message}");
            }
        }

        // ================================================================
        // 生成
        // ================================================================

        // ================================================================
        // プロファイル編集 Undo（回転体 / 2D押し出し）
        // ================================================================
        // MeshUndoController のサブウィンドウスタックにプロファイル全体の
        // スナップショットを積む。既存の Undo/Redo ボタン（_editOps.PerformUndo）
        // でそのまま巻き戻る。記録対象は点の移動/挿入/削除・リセット・変換・
        // プリセット選択・メッシュ取込・CSV読込。選択・アンカー・ビュー・
        // 連続パラメータスライダー（ドーナツ半径等の形状パラメータ）は対象外。

        private const string RevUndoStackId = "PlayerEdit/RevProfileEdit";
        private const string P2dUndoStackId = "PlayerEdit/P2dLoopsEdit";

        private sealed class RevProfileUndoContext { public List<Vector2> Profile; }
        private sealed class P2dLoopsUndoContext   { public List<Loop>     Loops;   }

        private sealed class RevProfileUndoRecord : IUndoRecord<RevProfileUndoContext>
        {
            public UndoOperationInfo Info { get; set; }
            public List<Vector2> Before;
            public List<Vector2> After;
            public void Undo(RevProfileUndoContext ctx) => ctx.Profile = CloneRevProfile(Before);
            public void Redo(RevProfileUndoContext ctx) => ctx.Profile = CloneRevProfile(After);
        }

        private sealed class P2dLoopsUndoRecord : IUndoRecord<P2dLoopsUndoContext>
        {
            public UndoOperationInfo Info { get; set; }
            public List<Loop> Before;
            public List<Loop> After;
            public void Undo(P2dLoopsUndoContext ctx) => ctx.Loops = CloneP2dLoops(Before);
            public void Redo(P2dLoopsUndoContext ctx) => ctx.Loops = CloneP2dLoops(After);
        }

        private UndoStack<RevProfileUndoContext> _revUndoStack;
        private RevProfileUndoContext            _revUndoCtx;
        private UndoStack<P2dLoopsUndoContext>   _p2dUndoStack;
        private P2dLoopsUndoContext              _p2dUndoCtx;

        private List<Vector2> _revEditBefore;   // 回転体：編集前スナップショット
        private List<Loop>    _p2dEditBefore;   // 2D押し出し：編集前スナップショット
        private bool _revUndoApplying;          // undo/redo 適用中は記録抑止
        private bool _p2dUndoApplying;

        private static List<Vector2> CloneRevProfile(List<Vector2> src)
            => src == null ? null : new List<Vector2>(src);

        private static List<Loop> CloneP2dLoops(List<Loop> src)
        {
            if (src == null) return null;
            var r = new List<Loop>(src.Count);
            for (int i = 0; i < src.Count; i++) r.Add(new Loop(src[i]));
            return r;
        }

        private static bool RevProfileEquals(List<Vector2> a, List<Vector2> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if ((a[i] - b[i]).sqrMagnitude > 1e-12f) return false;
            return true;
        }

        private static bool P2dLoopsEquals(List<Loop> a, List<Loop> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                Loop la = a[i], lb = b[i];
                if (la == null || lb == null) return false;
                if (la.IsHole != lb.IsHole) return false;
                if (la.Points.Count != lb.Points.Count) return false;
                for (int j = 0; j < la.Points.Count; j++)
                    if ((la.Points[j] - lb.Points[j]).sqrMagnitude > 1e-12f) return false;
            }
            return true;
        }

        private void EnsureRevUndoStack()
        {
            if (_revUndoStack != null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            undo.RemoveSubWindowStack(RevUndoStackId);   // パネル再生成時の重複ID回避
            _revUndoCtx   = new RevProfileUndoContext { Profile = CloneRevProfile(_revProfile) };
            _revUndoStack = undo.CreateSubWindowStack(RevUndoStackId, "回転体プロファイル編集", _revUndoCtx);
            _revUndoStack.OnUndoPerformed += _ => ApplyRevUndoContext();
            _revUndoStack.OnRedoPerformed += _ => ApplyRevUndoContext();
        }

        private void EnsureP2dUndoStack()
        {
            if (_p2dUndoStack != null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            undo.RemoveSubWindowStack(P2dUndoStackId);   // パネル再生成時の重複ID回避
            _p2dUndoCtx   = new P2dLoopsUndoContext { Loops = CloneP2dLoops(_p2dLoops) };
            _p2dUndoStack = undo.CreateSubWindowStack(P2dUndoStackId, "2D押し出しプロファイル編集", _p2dUndoCtx);
            _p2dUndoStack.OnUndoPerformed += _ => ApplyP2dUndoContext();
            _p2dUndoStack.OnRedoPerformed += _ => ApplyP2dUndoContext();
        }

        /// <summary>回転体：編集前スナップショットを取得（記録の起点）。</summary>
        private void RevBegin()
        {
            if (_revUndoApplying) { _revEditBefore = null; return; }
            _revEditBefore = GetUndoController?.Invoke() == null ? null : CloneRevProfile(_revProfile);
        }

        /// <summary>回転体：変化があればサブウィンドウスタックへ記録。</summary>
        private void RevCommit(string desc)
        {
            var before = _revEditBefore;
            _revEditBefore = null;
            if (_revUndoApplying || before == null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            var after = CloneRevProfile(_revProfile);
            if (RevProfileEquals(before, after)) return;
            EnsureRevUndoStack();
            if (_revUndoStack == null) return;
            _revUndoCtx.Profile = CloneRevProfile(after);
            _revUndoStack.Record(new RevProfileUndoRecord { Before = before, After = after }, desc);
            undo.FocusSubWindow(RevUndoStackId);
        }

        /// <summary>回転体：Undo/Redo で復元されたスナップショットをパネルへ反映。</summary>
        private void ApplyRevUndoContext()
        {
            _revUndoApplying = true;
            try
            {
                _revProfile = CloneRevProfile(_revUndoCtx?.Profile);
                _revSel.Clear();
                _revSelIdx = -1;
                _revP.CurrentPreset = ProfilePreset.Custom;
                D();
                RefreshRevCanvas();
                RefreshRevPointUI();
            }
            finally { _revUndoApplying = false; }
        }

        /// <summary>2D押し出し：編集前スナップショットを取得（記録の起点）。</summary>
        private void P2dBegin()
        {
            if (_p2dUndoApplying) { _p2dEditBefore = null; return; }
            _p2dEditBefore = GetUndoController?.Invoke() == null ? null : CloneP2dLoops(_p2dLoops);
        }

        /// <summary>2D押し出し：変化があればサブウィンドウスタックへ記録。</summary>
        private void P2dCommit(string desc)
        {
            var before = _p2dEditBefore;
            _p2dEditBefore = null;
            if (_p2dUndoApplying || before == null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            var after = CloneP2dLoops(_p2dLoops);
            if (P2dLoopsEquals(before, after)) return;
            EnsureP2dUndoStack();
            if (_p2dUndoStack == null) return;
            _p2dUndoCtx.Loops = CloneP2dLoops(after);
            _p2dUndoStack.Record(new P2dLoopsUndoRecord { Before = before, After = after }, desc);
            undo.FocusSubWindow(P2dUndoStackId);
        }

        /// <summary>2D押し出し：Undo/Redo で復元されたスナップショットをパネルへ反映。</summary>
        private void ApplyP2dUndoContext()
        {
            _p2dUndoApplying = true;
            try
            {
                _p2dLoops = CloneP2dLoops(_p2dUndoCtx?.Loops);
                _p2dSel.Clear();
                _p2dSelPt = -1;
                _p2dSelLoop = (_p2dLoops == null || _p2dLoops.Count == 0)
                    ? 0 : Mathf.Clamp(_p2dSelLoop, 0, _p2dLoops.Count - 1);
                D();
                RefreshP2dCanvas();
                RefreshP2dPointUI();
            }
            finally { _p2dUndoApplying = false; }
        }

        /// <summary>
        /// 現在の図形種別に応じて MeshObject を生成する。
        /// </summary>
        /// <param name="forPreview">
        /// true のときプレビュー用。重複頂点の結合 (MergeAllVerticesAtSamePosition) を行わない。
        /// 結合は全頂点の二重ループ (O(N^2)) のため、頂点数が多い形状ではプレビュー再生成のたびに停止する。
        /// 生成ボタン経路は false を渡し、従来どおり結合する。
        /// </param>
        /// <param name="applyTransform">
        /// false のとき回転・スケールを頂点へ焼き込まない。
        /// ピボットの「重心」ボタンが、姿勢の影響を受けない素の形状で重心を測るために使う。
        /// </param>
        private MeshObject Generate(bool forPreview, bool applyTransform = true)
        {
            MeshObject mo;
            switch (_current)
            {
                case ShapeKind.Cube:      mo = CubeMeshGenerator.Generate(_cubeP); break;
                case ShapeKind.Sphere:    mo = SphereMeshGenerator.Generate(_sphereP); break;
                case ShapeKind.Cylinder:  mo = CylinderMeshGenerator.Generate(_cylP); break;
                case ShapeKind.Capsule:   mo = CapsuleMeshGenerator.Generate(_capsP); break;
                case ShapeKind.Plane:     mo = PlaneMeshGenerator.Generate(_planeP); break;
                case ShapeKind.Pyramid:   mo = PyramidMeshGenerator.Generate(_pyramidP); break;
                case ShapeKind.Revolution:
                    EnsureRevProfile();
                    mo = RevolutionMeshGenerator.Generate(_revProfile, _revP);
                    break;
                case ShapeKind.Profile2D:
                    EnsureP2DLoops();
                    mo = Profile2DExtrudeMeshGenerator.Generate(_p2dLoops, _p2dP.MeshName,
                        new Profile2DGenerateParams
                        {
                            Scale          = _p2dP.Scale,
                            Offset         = _p2dP.Offset,
                            FlipY          = _p2dP.FlipY,
                            Thickness      = _p2dP.Thickness,
                            SegmentsFront  = _p2dP.SegmentsFront,
                            SegmentsBack   = _p2dP.SegmentsBack,
                            EdgeSizeFront  = _p2dP.EdgeSizeFront,
                            EdgeSizeBack   = _p2dP.EdgeSizeBack,
                            EdgeInward     = _p2dP.EdgeInward,
                            SymmetryMode   = _p2dP.SymmetryMode,
                        });
                    Poly_Ling.PrimitiveMesh.PrimitiveMeshPostProcess.ApplyPivotOffset(mo, _p2dP.Pivot);
                    break;
                case ShapeKind.NohMask:
                    mo = NohMaskMeshGenerator.GenerateFromFiles(_nohP);
                    break;
                case ShapeKind.Frill:
                    mo = GenerateFrillMesh();
                    break;
                case ShapeKind.Pipe:
                    mo = GeneratePipeMesh();
                    break;
                case ShapeKind.PlaceObject:
                    mo = GeneratePlaceObjectMesh();
                    break;
                case ShapeKind.Text:
                    mo = GenerateTextMesh();
                    break;
                // 穴つなぎはプレビューだけ MeshObject を作る（座標はワールド空間）。
                // 実生成は書き込み先の既存頂点を参照するため OnBridgeGenerate へ流す。
                case ShapeKind.Bridge:
                    return GenerateBridgeMesh();
                // 歪み複製は1つのメッシュを返さない（モデルへ直接オブジェクトを挿入する）。
                // プレビュー / ライブワイヤは出さないので null を返す。
                case ShapeKind.ObjectArray: return null;
                default: return null;
            }

            // プレビューでは結合しない。結合は O(N^2) で、
            // 接地の「全部を結合して配置」のように頂点数が膨らむ形状ではプレビューが停止する。
            if (!forPreview && _mergeDuplicateVertices && mo != null && mo.VertexCount >= 2)
                MeshMergeHelper.MergeAllVerticesAtSamePosition(mo, 0.001f);

            // 回転・スケールを頂点へ焼き込む。
            // マージ許容値 (0.001) はローカル空間で評価させたいので、必ずマージの後に行う。
            // 平行移動は焼き込まない (呼出し側の AddMode 別処理が従来どおり _worldPos を扱う)。
            // プレビューは見た目を変えないため常に両方を焼き込む。
            // 実生成はベイク OFF の成分を焼き込まず、呼出し側が姿勢へ入れる。
            if (!applyTransform)
                return mo;

            if (forPreview)
                PrimitiveMeshTransform.ApplyRotationScale(mo, _rotEuler, _scale);
            else
                PrimitiveMeshTransform.ApplyRotationScale(
                    mo,
                    BakeRotationEffective ? _rotEuler : Vector3.zero,
                    BakeScaleEffective    ? _scale    : Vector3.one);

            return mo;
        }

        private string Name()
        {
            switch (_current)
            {
                case ShapeKind.Cube:       return _cubeP.MeshName;
                case ShapeKind.Sphere:     return _sphereP.MeshName;
                case ShapeKind.Cylinder:   return _cylP.MeshName;
                case ShapeKind.Capsule:    return _capsP.MeshName;
                case ShapeKind.Plane:      return _planeP.MeshName;
                case ShapeKind.Pyramid:    return _pyramidP.MeshName;
                case ShapeKind.Revolution: return _revP.MeshName;
                case ShapeKind.Profile2D:  return _p2dP.MeshName;
                case ShapeKind.NohMask:    return _nohP.MeshName;
                case ShapeKind.Frill:      return _frillP.MeshName;
                case ShapeKind.Pipe:       return _pipeP.MeshName;
                case ShapeKind.PlaceObject: return _placeP.MeshName;
                case ShapeKind.Text:       return _textP.MeshName;
                // 歪み複製は生成物ごとに複製元名を使うため、ここでは固定名を返す。
                case ShapeKind.ObjectArray: return "ObjectArray";
                case ShapeKind.Bridge:     return BridgeMeshName;
                default:                   return _current.ToString();
            }
        }

        /// <summary>現在の形状の名前を書き換える。</summary>
        private void SetName(string name)
        {
            switch (_current)
            {
                case ShapeKind.Cube:        _cubeP.MeshName    = name; break;
                case ShapeKind.Sphere:      _sphereP.MeshName  = name; break;
                case ShapeKind.Cylinder:    _cylP.MeshName     = name; break;
                case ShapeKind.Capsule:     _capsP.MeshName    = name; break;
                case ShapeKind.Plane:       _planeP.MeshName   = name; break;
                case ShapeKind.Pyramid:     _pyramidP.MeshName = name; break;
                case ShapeKind.Revolution:  _revP.MeshName     = name; break;
                case ShapeKind.Profile2D:   _p2dP.MeshName     = name; break;
                case ShapeKind.NohMask:     _nohP.MeshName     = name; break;
                case ShapeKind.Frill:       _frillP.MeshName   = name; break;
                case ShapeKind.Pipe:        _pipeP.MeshName    = name; break;
                case ShapeKind.PlaceObject: _placeP.MeshName   = name; break;
                case ShapeKind.Text:        _textP.MeshName    = name; break;
            }
        }

        /// <summary>
        /// 末尾の "_数字" を落とした基底名を返す。"Frill_3" → "Frill"。
        /// 候補を作り直すたびに桁が伸びる（Frill_1_1 …）のを防ぐ。
        /// </summary>
        private static string StripNameSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            int us = name.LastIndexOf('_');
            if (us <= 0 || us == name.Length - 1) return name;

            for (int i = us + 1; i < name.Length; i++)
                if (!char.IsDigit(name[i])) return name;

            return name.Substring(0, us);
        }

        /// <summary>
        /// 既存の描画オブジェクト名と衝突しない候補を返す。
        /// 衝突していなければ入力をそのまま返す（利用者が付けた名前を勝手に削らない）。
        /// 衝突したときだけ末尾の "_数字" を落とし、_1, _2 ... を付け直す
        /// （ModelContext.GenerateUniqueMeshName と同じ規則。桁が伸び続けるのを防ぐ）。
        /// </summary>
        private string MakeUniqueMeshNameCandidate(string current)
        {
            if (string.IsNullOrEmpty(current)) return current;

            var existing = GetExistingMeshNames?.Invoke();
            if (existing == null || existing.Count == 0) return current;

            var used = new HashSet<string>(existing);
            if (!used.Contains(current)) return current;

            string baseName = StripNameSuffix(current);
            if (string.IsNullOrEmpty(baseName)) baseName = current;
            if (!used.Contains(baseName)) return baseName;

            int counter = 1;
            string name;
            do
            {
                name = $"{baseName}_{counter}";
                counter++;
            } while (used.Contains(name));

            return name;
        }

        /// <summary>
        /// 現在の形状の名前を、既存オブジェクトと重複しない候補へ更新する。
        /// 形状を選び直したときと、生成に成功した直後に呼ぶ。
        /// </summary>
        private void RefreshMeshNameCandidate()
        {
            string cur  = Name();
            string next = MakeUniqueMeshNameCandidate(cur);
            if (next == cur) return;

            SetName(next);
            // 表示中の名前欄があれば書き戻す（無ければ次の RebuildSettings が拾う）。
            _nameField?.SetValueWithoutNotify(next);
        }

        // ================================================================
        // メッシュ⇄プロファイル連携（取り込み/反映）
        // 方針: Z を破棄し XY をそのまま扱う（座標変換なし）。
        // 取り込み元 = 選択オブジェクト内の全2頂点ライン（未選択でも対象）。
        // 反映先 = 既存 AddMode ドロップダウンに従う。
        // ================================================================

        /// <summary>選択オブジェクトの全2頂点ラインを Revolution プロファイルへ取り込む。</summary>
        private void ImportRevolutionFromMesh()
        {
            var mesh = GetSelectedMeshObject?.Invoke();
            if (mesh == null) { _statusLabel.text = T("NoSelectedMesh"); return; }

            var lineFaces = LineProfileExtractor.CollectLineFaceIndices(mesh);
            var pts       = LineProfileExtractor.ExtractPolyline(mesh, lineFaces);
            if (pts == null || pts.Count < 2) { _statusLabel.text = T("NoLinesFound"); return; }

            RevBegin();
            _revProfile = new List<Vector2>(pts);
            _revSel.Clear(); _revSelIdx  = -1;
            _revP.CurrentPreset = ProfilePreset.Custom;
            RevCommit("メッシュ取込");
            _statusLabel.text = T("ImportedPoints", pts.Count);
            D(); RefreshRevCanvas(); RefreshRevPointUI();
        }

        /// <summary>Revolution プロファイルを2頂点ラインの MeshObject として反映する。</summary>
        private void ApplyRevolutionToMesh()
        {
            EnsureRevProfile();
            if (_revProfile == null || _revProfile.Count < 2) { _statusLabel.text = T("NoLinesFound"); return; }

            var mo = LineProfileExtractor.PolylineToLineMesh(_revProfile, _revP.MeshName, _revP.CloseLoop);
            if (mo == null || mo.FaceCount == 0) { _statusLabel.text = T("NoLinesFound"); return; }

            _statusLabel.text = T("AppliedToMesh", mo.FaceCount);
            OnMeshCreated?.Invoke(mo, _revP.MeshName, _worldPos, Vector3.zero, Vector3.one, false, _addMode);
        }

        /// <summary>選択オブジェクトの全2頂点ラインを Profile2D ループへ取り込む。</summary>
        private void ImportProfile2DFromMesh()
        {
            var mesh = GetSelectedMeshObject?.Invoke();
            if (mesh == null) { _statusLabel.text = T("NoSelectedMesh"); return; }

            var lineFaces = LineProfileExtractor.CollectLineFaceIndices(mesh);
            var loops     = LineProfileExtractor.ExtractLoops(mesh, lineFaces);
            if (loops == null || loops.Count == 0) { _statusLabel.text = T("NoLinesFound"); return; }

            P2dBegin();
            _p2dLoops   = loops;
            _p2dSelLoop = 0;
            _p2dSel.Clear();
            _p2dSelPt   = -1;
            P2dCommit("メッシュ取込");
            _statusLabel.text = T("ImportedLoops", loops.Count);
            D(); RebuildSettings();
        }

        /// <summary>Profile2D ループを2頂点ラインの MeshObject として反映する。</summary>
        private void ApplyProfile2DToMesh()
        {
            EnsureP2DLoops();
            if (_p2dLoops == null || _p2dLoops.Count == 0) { _statusLabel.text = T("NoLinesFound"); return; }

            var mo = LineProfileExtractor.LoopsToLineMesh(_p2dLoops, _p2dP.MeshName);
            if (mo == null || mo.FaceCount == 0) { _statusLabel.text = T("NoLinesFound"); return; }

            _statusLabel.text = T("AppliedToMesh", mo.FaceCount);
            OnMeshCreated?.Invoke(mo, _p2dP.MeshName, _worldPos, Vector3.zero, Vector3.one, false, _addMode);
        }

        // ================================================================
        // ワイヤーフレームMesh生成
        // ================================================================

        private static Mesh BuildWire(MeshObject mo)
        {
            var verts = new Vector3[mo.VertexCount];
            for (int i = 0; i < verts.Length; i++) verts[i] = mo.Vertices[i].Position;
            var set = new HashSet<(int,int)>();
            var idx = new List<int>();
            foreach (var f in mo.Faces)
            {
                int n = f.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    int a = f.VertexIndices[i], b = f.VertexIndices[(i+1)%n];
                    var e = a < b ? (a,b) : (b,a);
                    if (set.Add(e)) { idx.Add(a); idx.Add(b); }
                }
            }
            if (idx.Count == 0) return null;
            var wire = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            wire.vertices = verts;
            wire.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
            wire.RecalculateBounds();
            return wire;
        }

        private void DestroyWire()
        {
            if (_wireMesh != null) { UnityEngine.Object.Destroy(_wireMesh); _wireMesh = null; }
        }

        // ================================================================
        // 生成ボタン
        // ================================================================

        private VisualElement CB()
        {
            var btn = new Button(() =>
            {
                try
                {
                    // 歪み複製はモデルへ直接オブジェクトを挿入するため、
                    // OnMeshCreated（単一 MeshObject）経路は通らない。
                    if (_current == ShapeKind.ObjectArray) { InvokeObjectArrayGenerate(); return; }

                    // 穴つなぎは書き込み先の既存頂点を参照する面を足すため、
                    // OnMeshCreated（単一 MeshObject を新規追加）経路は通らない。
                    if (_current == ShapeKind.Bridge) { InvokeBridgeGenerate(); return; }

                    var mo = Generate(false);
                    if (mo == null) { _statusLabel.text = "生成失敗"; return; }
                    _statusLabel.text = T("VertsFaces", mo.VertexCount, mo.FaceCount);
                    OnMeshCreated?.Invoke(mo, Name(), _worldPos, PoseRotation, PoseScale, false, _addMode);

                    // 次の生成に備えて名前欄を非重複候補へ更新する。
                    // AddToExisting は既存オブジェクトへの統合なので新しい名前は要らない。
                    if (_addMode != PrimitiveAddMode.AddToExisting)
                        RefreshMeshNameCandidate();
                }
                catch (Exception ex)
                {
                    _statusLabel.text = $"Error: {ex.Message}";
                    Debug.LogException(ex);
                }
            }) { text = T("Create") };
            btn.style.height = 28; btn.style.marginTop = 6;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _createBtn = btn;
            RefreshCreateButtonState();
            return btn;
        }

        // ================================================================
        // 生成ボタンの有効・無効
        // ================================================================

        /// <summary>生成ボタン本体。条件が揃うまでグレーアウトさせるため保持する。</summary>
        private Button _createBtn;

        /// <summary>ベルトを1本でも取り込めているか。フリル／パイプ／接地の共通判定。</summary>
        private static bool AnyBeltHasData(List<BeltSnapshot> belts)
        {
            if (belts == null) return false;
            foreach (var b in belts)
                if (b != null && b.HasData) return true;
            return false;
        }

        /// <summary>
        /// 現在の図形が「今のまま生成ボタンを押して意味のある結果になる」状態か。
        /// 条件を持たない図形（基本図形・回転体・2D押し出し・能面）は常に true。
        /// </summary>
        private bool CanGenerate()
        {
            switch (_current)
            {
                // ベルトが1本も無いと空メッシュになる。
                case ShapeKind.Frill: return AnyBeltHasData(_frillBelts);
                case ShapeKind.Pipe:  return AnyBeltHasData(_pipeBelts);

                // ベルトに加えて、配置するオブジェクトの選択も要る。
                case ShapeKind.PlaceObject:
                    return AnyBeltHasData(_placeBelts)
                        && _placeSrcPick.CurrentList(_placeP.IncludeChildren, GetSubtreeMeshList).Count > 0;

                // 複製元のチェックが要る。生成先は Viewer 側の結線。
                case ShapeKind.ObjectArray:
                    return OnObjectArrayGenerate != null
                        && _objArrayPanel != null
                        && _objArrayPanel.SelectedMasterIndices().Count > 0;

                // フォントが開けて、かつ文字列が空でないこと。
                case ShapeKind.Text:
                    return !string.IsNullOrWhiteSpace(_textP.Text)
                        && Poly_Ling.GlyphText.PlyFontLibrary.Open(_textP.FontFamily) != null;

                // 種 A・B の両方が取込済みであること。書き込み先は Viewer 側の結線。
                case ShapeKind.Bridge:
                    return OnBridgeGenerate != null && BridgeSeedsReady;

                default: return true;
            }
        }

        /// <summary>生成ボタンの有効・無効と配色を現在の条件に合わせる。</summary>
        private void RefreshCreateButtonState()
        {
            if (_createBtn == null) return;
            bool ok = CanGenerate();
            _createBtn.SetEnabled(ok);
            _createBtn.style.backgroundColor = new StyleColor(ok
                ? new Color(0.22f, 0.48f, 0.22f)
                : new Color(0.28f, 0.28f, 0.28f));
        }

        /// <summary>プロファイル編集キャンバス直下に縦リサイズハンドルを追加する（3Dプレビューと同方式）。</summary>
        private void AddProfileResizeHandle(VisualElement container, VisualElement canvas, Action refresh)
        {
            var handle = new VisualElement();
            handle.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            handle.style.height          = 6;
            handle.style.marginBottom    = 4;
            handle.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.36f));
            handle.pickingMode           = PickingMode.Position;

            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                handle.CapturePointer(e.pointerId);
                _profileResizeDragging    = true;
                _profileResizeStartY      = e.position.y;
                _profileResizeStartHeight = _profileHeight;
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_profileResizeDragging || !handle.HasPointerCapture(e.pointerId)) return;
                float delta = e.position.y - _profileResizeStartY;
                _profileHeight = Mathf.Clamp(_profileResizeStartHeight + delta, ProfileMinHeight, ProfileMaxHeight);
                canvas.style.height = _profileHeight;
                refresh?.Invoke();
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!handle.HasPointerCapture(e.pointerId)) return;
                handle.ReleasePointer(e.pointerId);
                _profileResizeDragging = false;
                e.StopPropagation();
            });

            container.Add(handle);
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        /// <summary>
        /// 「ベイク」チェックボックスを左に置いた見出し行を作る。
        /// チェックは生成時の焼き込み先を切り替えるだけで、プレビューには影響しない。
        /// </summary>
        private VisualElement BakeHeaderRow(string label, Func<bool> get, Action<bool> set)
            => BakeHeaderRow(label, get, set, out _);

        /// <param name="bakeToggle">
        /// 「ベイク」チェックボックス本体。追加先が「既存に追加」のときは
        /// BakeRotationEffective / BakeScaleEffective が無条件に true になり
        /// この指定が効かないため、呼び出し側で表示を切り替える。
        /// </param>
        private VisualElement BakeHeaderRow(string label, Func<bool> get, Action<bool> set,
            out Toggle bakeToggle)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;

            var bake = new Toggle(T("Bake")) { value = get() };
            bake.style.color      = new StyleColor(Color.white);
            bake.style.marginRight = 6;
            bake.RegisterValueChangedCallback(e => set(e.newValue));
            row.Add(bake);

            row.Add(SL(label));
            bakeToggle = bake;
            return row;
        }

        /// <summary>
        /// 図形名の見出し。上に太い区切り線を引き、他のセクション見出し（SL）より
        /// 大きく太くして、ここから個別パラメータが始まることを分かるようにする。
        /// </summary>
        private static VisualElement ShapeTitle(string t)
        {
            var box = new VisualElement();
            box.style.marginTop    = 8;
            box.style.marginBottom = 4;

            var rule = new VisualElement();
            rule.style.height          = 2;
            rule.style.marginBottom    = 4;
            rule.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.30f));
            box.Add(rule);

            var l = new Label(t);
            l.style.fontSize = 14;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.color = new StyleColor(new Color(0.75f, 0.88f, 1f));
            box.Add(l);

            return box;
        }

        private static Label SL(string t, bool bold = false)
        {
            var l = new Label(t);
            l.style.marginTop = bold ? 2 : 5; l.style.marginBottom = 2;
            l.style.color = bold
                ? new StyleColor(new Color(0.9f, 0.9f, 0.9f))
                : new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = bold ? 11 : 10;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        private static VisualElement Sep()
        {
            var v = new VisualElement();
            v.style.height = 1; v.style.marginTop = 4; v.style.marginBottom = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        /// <summary>
        /// 名前欄。生成した TextField を控えておき、非重複候補への更新で書き戻す
        /// （設定UI 全体を作り直さずに名前だけ差し替えるため）。
        /// </summary>
        private VisualElement NF(Func<string> get, Action<string> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 3;
            row.Add(ML(T("Name")));
            var f = new TextField { value = get() }; f.style.flexGrow = 1;
            f.RegisterValueChangedCallback(e => set(e.newValue));
            _nameField = f;
            row.Add(f); return row;
        }

        private static VisualElement SR(string label, float min, float max, Func<float> get, Action<float> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            row.Add(ML(label));
            var sl = new Slider(min, max) { value = get() }; sl.style.flexGrow = 1;
            var nf = new FloatField { value = get() }; nf.style.width = 42;
            sl.RegisterValueChangedCallback(e => { nf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3)); set(e.newValue); });
            nf.RegisterValueChangedCallback(e => { float v = Mathf.Clamp(e.newValue, min, max); sl.SetValueWithoutNotify(v); set(v); });
            row.Add(sl); row.Add(nf); return row;
        }

        // out 版: 生成したスライダ/数値欄を呼び出し側へ返す（プリセットボタンからの同期用）。
        private static VisualElement SR(string label, float min, float max, Func<float> get, Action<float> set,
            out Slider slOut, out FloatField nfOut)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            row.Add(ML(label));
            var sl = new Slider(min, max) { value = get() }; sl.style.flexGrow = 1;
            var nf = new FloatField { value = get() }; nf.style.width = 42;
            sl.RegisterValueChangedCallback(e => { nf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3)); set(e.newValue); });
            nf.RegisterValueChangedCallback(e => { float v = Mathf.Clamp(e.newValue, min, max); sl.SetValueWithoutNotify(v); set(v); });
            row.Add(sl); row.Add(nf);
            slOut = sl; nfOut = nf;
            return row;
        }

        private static VisualElement IR(string label, int min, int max, Func<int> get, Action<int> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            row.Add(ML(label));
            var sl = new SliderInt(min, max) { value = get() }; sl.style.flexGrow = 1;
            var nf = new IntegerField { value = get() }; nf.style.width = 36;
            sl.RegisterValueChangedCallback(e => { nf.SetValueWithoutNotify(e.newValue); set(e.newValue); });
            nf.RegisterValueChangedCallback(e => { int v = Mathf.Clamp(e.newValue, min, max); sl.SetValueWithoutNotify(v); set(v); });
            row.Add(sl); row.Add(nf); return row;
        }

        private static VisualElement TR(string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() }; t.style.marginBottom = 2;
            t.RegisterValueChangedCallback(e => set(e.newValue)); return t;
        }

        /// <summary>
        /// FloatField 3 連の行。参照を保持しない従来版。
        /// 実装は <see cref="V3FRef"/> に一本化してある（outFields = null）。
        /// </summary>
        private static VisualElement V3F(
            string lx, string ly, string lz,
            Func<float> gx, Action<float> sx,
            Func<float> gy, Action<float> sy,
            Func<float> gz, Action<float> sz)
            => V3FRef(lx, ly, lz, gx, sx, gy, sy, gz, sz, null);

        /// <summary>
        /// <see cref="V3F"/> と同一構造で、生成した FloatField を <paramref name="outFields"/>
        /// (長さ3) に保持する版。外部から <see cref="RefreshTrsFields"/> で値を書き戻すために使う。
        /// </summary>
        private static VisualElement V3FRef(
            string lx, string ly, string lz,
            Func<float> gx, Action<float> sx,
            Func<float> gy, Action<float> sy,
            Func<float> gz, Action<float> sz,
            FloatField[] outFields)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            void AddFF(int slot, string lbl, Func<float> g, Action<float> s)
            {
                var sub = new VisualElement(); sub.style.flexDirection = FlexDirection.Row; sub.style.flexGrow = 1;
                var l = new Label(lbl); l.style.width = 14; l.style.unityTextAlign = TextAnchor.MiddleLeft;
                var f = new FloatField { value = g() }; f.style.flexGrow = 1;
                f.RegisterValueChangedCallback(e => s(e.newValue));
                sub.Add(l); sub.Add(f); row.Add(sub);
                if (outFields != null && slot >= 0 && slot < outFields.Length) outFields[slot] = f;
            }
            AddFF(0, lx, gx, sx); AddFF(1, ly, gy, sy); AddFF(2, lz, gz, sz);
            return row;
        }

        /// <summary>
        /// 座標 / 回転 / スケールの数値欄を現在値で更新する。
        /// SetValueWithoutNotify を使うため setter は再発火しない（無限ループ防止）。
        /// 再生成が必要な場合は呼出し側で <c>D()</c> を呼ぶこと。
        /// </summary>
        public void RefreshTrsFields()
        {
            SetFF(_posFields, _worldPos);
            SetFF(_rotFields, _rotEuler);
            SetFF(_sclFields, _scale);
        }

        private static void SetFF(FloatField[] fields, Vector3 v)
        {
            if (fields == null) return;
            if (fields.Length > 0) fields[0]?.SetValueWithoutNotify(v.x);
            if (fields.Length > 1) fields[1]?.SetValueWithoutNotify(v.y);
            if (fields.Length > 2) fields[2]?.SetValueWithoutNotify(v.z);
        }

        private static Label ML(string t)
        {
            var l = new Label(t); l.style.width = 80;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.style.fontSize = 10; return l;
        }

        private static void SB(VisualElement p, string t, Action onClick)
        {
            var b = new Button(onClick) { text = t }; b.style.flexGrow = 1; b.style.marginRight = 2;
            b.style.height = 18; b.style.fontSize = 9; p.Add(b);
        }

        /// <summary>配置ギズモのサブモードボタンを1つ作って行へ追加する。</summary>
        private Button PGB(VisualElement row, string label,
                           PrimitivePlaceToolHandler.PlaceGizmoMode mode)
        {
            var b = new Button(() =>
            {
                SetPlaceGizmoMode?.Invoke(mode);
                RefreshPlaceGizmoButtons();
            }) { text = label };
            b.style.flexGrow = 1; b.style.marginRight = 2;
            b.style.height = 18; b.style.fontSize = 9;
            row.Add(b);
            return b;
        }

        /// <summary>
        /// 配置ギズモのサブモードボタンの背景色を現在のモードに合わせる。
        /// ApplyDarkTheme は全 Button を既定色へ戻すため、その後にも呼ぶこと。
        /// </summary>
        public void RefreshPlaceGizmoButtons()
        {
            if (_placeGizmoBtns == null) return;

            var cur = GetPlaceGizmoMode != null
                ? GetPlaceGizmoMode()
                : PrimitivePlaceToolHandler.PlaceGizmoMode.Move;

            var on  = new StyleColor(new Color(0.25f, 0.45f, 0.65f));
            var off = new StyleColor(new Color(0.25f, 0.25f, 0.25f));

            void Paint(int i, PrimitivePlaceToolHandler.PlaceGizmoMode m)
            {
                if (i >= _placeGizmoBtns.Length || _placeGizmoBtns[i] == null) return;
                _placeGizmoBtns[i].style.backgroundColor = (m == cur) ? on : off;
            }

            Paint(0, PrimitivePlaceToolHandler.PlaceGizmoMode.Move);
            Paint(1, PrimitivePlaceToolHandler.PlaceGizmoMode.Rotate);
            Paint(2, PrimitivePlaceToolHandler.PlaceGizmoMode.Scale);
        }
    }
}
