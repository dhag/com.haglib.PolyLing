// PlayerPrimitiveMeshSubPanel.Placement.cs
// 図形生成サブパネル：生成物の置き方（位置・回転・拡大・焼き込み・追加先・材質・
// 頂点結合・グループ保持）と、配置ギズモ・姿勢仮表示のチェック。
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
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // マテリアル指定
        // ================================================================

        /// <summary>
        /// 現在のモデルのマテリアルスロット表示名一覧を返す。
        /// 添字がそのままスロット番号になる。スロットが 1 つも無ければ空リスト。
        /// 未配線のときも空リスト扱いにする。
        /// </summary>
        public Func<List<string>> GetMaterialNames;

        /// <summary>マテリアル指定ドロップダウン。図形と追加先によって隠す。</summary>
        private DropdownField _materialDd;

        /// <summary>生成面へ割り当てるマテリアルスロット番号。</summary>
        private int _materialIndex = 0;

        /// <summary>
        /// 直近にドロップダウンへ反映した選択肢の内容。
        /// NotifyPanels は選択変更でも走るため、毎回 choices を組み直さないための比較用。
        /// </summary>
        private string _materialChoicesKey;

        /// <summary>
        /// マテリアル指定が効く図形か。
        ///
        /// 藤壺は元オブジェクトの MaterialIndex をそのまま複製する
        /// （PlaceObjectMeshGenerator）。歪み複製は複製元、穴つなぎは書き込み先の
        /// マテリアルをそのまま使う。いずれも上書きすると継承が壊れるため、
        /// ドロップダウン自体を出さない。
        /// </summary>
        private bool ShapeUsesMaterialSlot =>
            _current != ShapeKind.PlaceObject
         && _current != ShapeKind.ObjectArray
         && _current != ShapeKind.Bridge;

        /// <summary>
        /// 生成コマンドへ載せるマテリアルスロット番号。
        ///
        /// 継承する図形は -1（指定なし）。追加先が新しいモデルのときは、
        /// そのモデルにスロットが無くドロップダウンも出していないため 0
        /// （呼出し側がスロットを 1 つ作る）。
        /// </summary>
        private int EffectiveMaterialIndex
        {
            get
            {
                if (!ShapeUsesMaterialSlot)                  return -1;
                if (_addMode == PrimitiveAddMode.NewModel)   return 0;
                return _materialIndex;
            }
        }

        /// <summary>
        /// マテリアル指定ドロップダウンの選択肢を取り直す外部入口。
        /// 生成でスロットが作られた直後などに Viewer 側から呼ぶ。
        /// </summary>
        public void RefreshMaterials() => RefreshMaterialChoices();

        /// <summary>
        /// マテリアルドロップダウンの選択肢を取り直す。
        /// スロットが 1 つも無いモデルでは選ぶものが無いので、
        /// 「生成時に作る」の 1 項目だけを出して操作させない。
        /// </summary>
        private void RefreshMaterialChoices()
        {
            if (_materialDd == null) return;

            var  names = GetMaterialNames?.Invoke();
            bool empty = names == null || names.Count == 0;

            if (empty)
            {
                names          = new List<string> { T("MaterialNone") };
                _materialIndex = 0;
            }
            else if (_materialIndex < 0 || _materialIndex >= names.Count)
            {
                _materialIndex = 0;
            }

            // 選択肢が変わっていなければ作り直さない。
            string key = string.Join("\u0001", names);
            if (key != _materialChoicesKey)
            {
                _materialChoicesKey = key;
                _materialDd.choices = new List<string>(names);
            }

            _materialDd.SetValueWithoutNotify(names[_materialIndex]);
            _materialDd.SetEnabled(!empty);
        }

        // ---- 配置ギズモから読み書きする TRS ----

        /// <summary>生成位置。AddToExisting のときは追加先ローカル空間。</summary>
        public Vector3 PlacePosition { get => _worldPos; set { _worldPos = value; } }

        /// <summary>生成時の回転（オイラー角・度）。</summary>
        public Vector3 PlaceRotation { get => _rotEuler; set { _rotEuler = value; } }

        /// <summary>生成時のスケール。</summary>
        public Vector3 PlaceScale { get => _scale; set { _scale = value; } }

        /// <summary>現在の追加先モード。ギズモ中心の座標系判定に使う。</summary>
        public PrimitiveAddMode CurrentAddMode => _addMode;

        /// <summary>
        /// 姿勢（位置・回転・スケール）が効く図形か。
        /// 歪み複製と穴つなぎは図形生成コマンドを通らず姿勢を持たないため false。
        /// RefreshCommonUiVisibility が姿勢フォールドを隠す条件と同じ。
        /// 原点マーカー・くさびの仮表示もこの条件に従う。
        /// </summary>
        public bool PoseApplicable
            => _current != ShapeKind.ObjectArray
            && _current != ShapeKind.EdgeRibbonFace
            && _current != ShapeKind.Bridge
            && _current != ShapeKind.PointDefined;

        /// <summary>
        /// AddToExisting のときの追加先（MeshContextList インデックス）。
        /// -1 は「選択オブジェクトリストの先頭」。穴つなぎも同じ値を使う。
        /// </summary>
        public int CurrentAddTargetIndex => _addTargetIndex;

        // ---- 配置ギズモ・姿勢仮表示（LiveWireInMainViewport のときだけ UI を出す） ----

        /// <summary>
        /// 配置ギズモと姿勢仮表示の設定。Viewer が持つ 1 個を共有する。
        /// 基本図形 / 高度な図形はこのインスタンスの表示切替なので、値も共通になる。
        /// 未配線でも落ちないよう既定インスタンスを持たせておく。
        /// </summary>
        public PrimitivePlaceSettings PlaceSettings { get; set; } = new PrimitivePlaceSettings();

        /// <summary>
        /// 配置ギズモ・姿勢仮表示のチェックを変えた直後に呼ぶ。
        /// ビューポートのギズモと原点マーカーを組み直させる。
        /// </summary>
        public Action OnPlaceOverlayChanged;

        /// <summary>
        /// ギズモ表示チェック（移動 / 回転）。
        /// 拡大縮小は PrimitivePlaceSettings.ShowScaleGizmo に機能だけ残し、
        /// チェックは出さない（表示・操作を封印）。
        /// </summary>
        private Toggle _togShowMoveGizmo, _togShowRotationGizmo;

        /// <summary>姿勢仮表示チェック（原点マーカー / くさび）。</summary>
        private Toggle _togShowOriginMarker, _togShowWedge;

        /// <summary>RefreshPlaceToggles で書き戻す間、変更コールバックを止める。</summary>
        private bool _suppressPlaceToggles;

        /// <summary>
        /// 生成時の回転 Z を相対で動かす（クイック回転ボタン）。
        /// 押すたびに 360 を超えて伸びないよう、毎回 ±180 へ畳む
        /// （PlayerBoneEditorSubPanel.OffsetTransform と同じ扱い）。
        ///
        /// 位置は変わらないので原点マーカーの仮表示は組み直さない（D で足りる）。
        /// くさびの向きはカメラ描画のたびに作り直すため自動で追従する。
        /// </summary>
        private void OffsetRotationZ(float deltaDeg)
        {
            _rotEuler.z = NormAngle180(_rotEuler.z + deltaDeg);
            RefreshTrsFields();
            D();
        }

        /// <summary>角度を −180〜180 に畳む。</summary>
        private static float NormAngle180(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            else if (a < -180f) a += 360f;
            return a;
        }

        /// <summary>
        /// 生成位置を数値欄で変えたときに呼ぶ。プレビュー再生成に加えて、
        /// 原点マーカーの仮表示（ビューポート側のスナップショット）も作り直させる。
        /// くさびはカメラ描画のたびに組み直すので、ここでの通知は要らない。
        /// </summary>
        private void DPlace()
        {
            D();
            OnPlaceOverlayChanged?.Invoke();
        }

        /// <summary>
        /// 外部（配置ギズモ）から TRS を変更した後に呼ぶ。
        /// 数値欄へ書き戻し、プレビューを再生成対象にする。
        /// </summary>
        public void NotifyPlaceTrsChanged()
        {
            RefreshTrsFields();
            D();
        }

        // ワールド生成位置
        private Vector3         _worldPos            = Vector3.zero;
        private PrimitiveAddMode _addMode             = PrimitiveAddMode.NewObject;
        private bool            _mergeDuplicateVertices = true;

        /// <summary>
        /// 生成に使った入力とパラメータをオブジェクトグループとして残すか。既定 false。
        /// false（＝これまでのやり方）では生成後に何も残らないので、
        /// 作り直したいときは同じ操作をやり直すことになる。
        /// </summary>
        private bool            _keepAsGroup = false;

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

        /// <summary>
        /// 追加先ドロップダウンを操作した直後に呼ぶ。
        /// ベイクの表示・名前欄の見せ分け・追加先候補をまとめて追随させる。
        /// </summary>
        private void OnAddModeChanged()
        {
            // 追加先が変わると生成位置の基準（追加先の WorldMatrix）が変わるので、
            // 原点マーカーの仮表示も作り直させる。
            OnPlaceOverlayChanged?.Invoke();
            RefreshBakeToggleVis();
            RefreshAddTargetChoices();
            RefreshNameFieldMode();
            // マテリアル欄は追加先が「新しいモデル」かどうかで出し入れが変わる。
            RefreshCommonUiVisibility();
            RefreshMaterialChoices();
        }

        /// <summary>
        /// 図形ごとに効かない共通 UI を隠す。
        ///
        /// 歪み複製と穴つなぎは図形生成コマンドを通らないため、共通の姿勢
        /// （位置・回転・スケール・ベイク・頂点結合）は一切効かない。歪み複製は
        /// さらに独自の「出力先」「出力モード」を持つので追加先も効かない。
        /// 出しっぱなしにすると操作しても何も起きない欄になるため隠す。
        ///
        /// マテリアル指定も同様で、継承する図形（藤壺・歪み複製・穴つなぎ）と、
        /// 追加先が「新しいモデル」のとき（スロットが 0 件なので選ぶ対象が無い）は隠す。
        /// </summary>
        private void RefreshCommonUiVisibility()
        {
            // 点指定図形の書き込み先は常に編集対象なので、追加先も姿勢も使わない。
            // 材質はパネルの指定を使うので、追加先の値に関係なく出す。
            bool pointDefined = _current == ShapeKind.PointDefined;

            bool useAddMode = _current != ShapeKind.ObjectArray
                           && !pointDefined;
            bool usePose    = _current != ShapeKind.ObjectArray
                           && _current != ShapeKind.EdgeRibbonFace
                           && _current != ShapeKind.Bridge
                           && !pointDefined;
            bool useMaterial = ShapeUsesMaterialSlot
                            && (_addMode != PrimitiveAddMode.NewModel || pointDefined);

            if (_addModeDd != null)
                _addModeDd.style.display = useAddMode ? DisplayStyle.Flex : DisplayStyle.None;
            if (_poseFold != null)
                _poseFold.style.display  = usePose ? DisplayStyle.Flex : DisplayStyle.None;
            if (_materialDd != null)
                _materialDd.style.display = useMaterial ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>ベイクしなかった回転（描画オブジェクトの姿勢へ渡す分）。</summary>
        private Vector3 PoseRotation => BakeRotationEffective ? Vector3.zero : _rotEuler;

        /// <summary>ベイクしなかったスケール（描画オブジェクトの姿勢へ渡す分）。</summary>
        private Vector3 PoseScale    => BakeScaleEffective    ? Vector3.one  : _scale;

        /// <summary>追加先ドロップダウン。図形別に表示を切り替えるため保持する。</summary>
        private DropdownField _addModeDd;

        /// <summary>姿勢フォールド。図形別に表示を切り替えるため保持する。</summary>
        private Foldout _poseFold;

        // TRS 行の FloatField 参照。外部（将来のギズモ）から値を書き戻すために保持する。
        private readonly FloatField[] _posFields = new FloatField[3];
        private readonly FloatField[] _rotFields = new FloatField[3];
        private readonly FloatField[] _sclFields = new FloatField[3];

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

        /// <summary>配置ギズモ・姿勢仮表示のチェックを1つ作る（追加は呼出側）。</summary>
        private static Toggle PlaceToggle(string label, string tooltip)
        {
            var t = new Toggle(label) { value = false };
            t.style.color = new StyleColor(Color.white);
            t.tooltip     = tooltip;
            return t;
        }

        /// <summary>
        /// チェックの表示値を PrimitivePlaceSettings に合わせ直す。
        /// 設定側に排他（拡大縮小 ON → 移動 OFF）があるため、
        /// 変更のたびに全部読み直す。
        /// </summary>
        public void RefreshPlaceToggles()
        {
            var s = PlaceSettings;
            if (s == null) return;

            _suppressPlaceToggles = true;
            try
            {
                _togShowMoveGizmo?.SetValueWithoutNotify(s.ShowMoveGizmo);
                _togShowRotationGizmo?.SetValueWithoutNotify(s.ShowRotationGizmo);
                _togShowOriginMarker?.SetValueWithoutNotify(s.ShowOriginMarker);
                _togShowWedge?.SetValueWithoutNotify(s.ShowWedge);
            }
            finally { _suppressPlaceToggles = false; }
        }
    }
}
