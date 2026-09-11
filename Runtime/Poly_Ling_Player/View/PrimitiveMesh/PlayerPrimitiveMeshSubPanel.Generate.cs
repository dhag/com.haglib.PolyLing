// PlayerPrimitiveMeshSubPanel.Generate.cs
// 図形生成サブパネル：現在の図形から MeshObject を作る処理（プレビュー用）と生成ボタン。
// 生成ボタンの有効条件（CanGenerate）もここにある。
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
            // 穴つなぎはプレビューだけ MeshObject を作る（座標はワールド空間）。
            // 実生成は書き込み先の既存頂点を参照するため CreateHoleBridgeCommand を使う。
            if (_current == ShapeKind.Bridge) return GenerateBridgeMesh();

            // 歪み複製は1つのメッシュを返さない（モデルへ直接オブジェクトを挿入する）。
            // プレビュー / ライブワイヤは出さないので null を返す。
            if (_current == ShapeKind.ObjectArray) return null;

            // 辺から帯面は選択辺から組む。パラメータだけでは作れないので
            // ファクトリではなくハンドラへ組ませる（プレビューも同じ実装を通る）。
            if (_current == ShapeKind.EdgeRibbonFace) return GenerateEdgeRibbonFaceMesh();

            var cmd = BuildCreateCommand(applyTransform ? CurrentPlacement() : NeutralPlacement());
            if (cmd == null) return null;

            // 重複頂点の結合・パーツID の割当・回転/拡大の焼き込みはファクトリ側にある。
            // 図形種別の分岐がパネルとファクトリの 2 箇所に分かれないようにするため、
            // プレビューもボタンも同じコマンドを通す。
            var mo = PrimitiveMeshFactory.Build(
                cmd, forPreview, ResolvePlaceSources, ResolveBeltSource);

            // 見つからなかった字数は生成の副産物なので、情報欄はここで更新する。
            if (_current == ShapeKind.Text)
            {
                _textMissing = PrimitiveMeshFactory.LastTextMissingGlyphs;
                RefreshTextInfo();
            }

            return mo;
        }

        /// <summary>
        /// 梯子の取り込み元を索引から解決する。ファクトリへ渡す。
        /// はしごのウェイトを引き継ぐときだけファクトリが呼ぶ。
        /// </summary>
        private MeshObject ResolveBeltSource(int masterIndex)
            => GetMeshObjectAt?.Invoke(masterIndex);

        /// <summary>
        /// 藤壺（配置）の配置元を索引から解決する。ファクトリへ渡す。
        /// 展開・重複排除・面なしの除外は MeshSourceMultiPick.Resolve が持つ。
        /// </summary>
        private List<MeshObject> ResolvePlaceSources(int[] masterIndices, bool includeChildren)
            => MeshSourceMultiPick.Resolve(
                masterIndices, includeChildren, GetSubtreeMeshList,
                idx => GetMeshObjectAt?.Invoke(idx));

        /// <summary>
        /// 生成したメッシュへパーツID / サブIDを割り当てる。
        ///
        /// 厚み付けと重複頂点の結合で頂点数が変わるため、確定した頂点列に対して呼ぶこと。
        ///
        /// フリル／パイプは生成器が複数パーツへ分けているので、パーツIDはそのまま使い
        /// サブIDだけを振り直す。藤壺は配置元のサブIDをそのまま使うので何もしない。
        /// それ以外の図形は「1つのパーツを作った」扱いで、パーツID 0 とサブID 0.. を振る。
        ///
        /// 既存オブジェクトへ追加するときの番号のずらしは追加側（Viewer）が行う。
        /// </summary>
        private void AssignGeneratedPartsIds(MeshObject mo)
            => PrimitiveMeshFactory.AssignPartsIds(mo, ShapeKeys[(int)_current]);

        /// <summary>
        /// Generate() を通らずに MeshObject を作る経路（プロファイルの「メッシュへ反映」）で、
        /// 姿勢の回転・スケールを Generate() 末尾と同じ規則で処理する。
        ///
        /// ベイク ON の成分だけ頂点へ焼き込む。OFF の成分は呼出し側が
        /// PoseRotation / PoseScale として描画オブジェクトの姿勢へ入れる。
        /// 平行移動は焼き込まない（追加先別の処理が _worldPos を扱う）。
        /// </summary>
        private void ApplyPoseForDirectMeshCreate(MeshObject mo)
        {
            if (mo == null) return;
            PrimitiveMeshTransform.ApplyRotationScale(
                mo,
                BakeRotationEffective ? _rotEuler : Vector3.zero,
                BakeScaleEffective    ? _scale    : Vector3.one);

            // Generate() を通らないため、パーツID / サブIDもここで割り当てる。
            AssignGeneratedPartsIds(mo);
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
                    // 単一 MeshObject を作る経路は通らない。
                    if (_current == ShapeKind.ObjectArray) { InvokeObjectArrayGenerate(); return; }

                    // 穴つなぎは書き込み先の既存頂点を参照する面を足すため、
                    // 単一 MeshObject を新規追加する経路は通らない。
                    if (_current == ShapeKind.Bridge) { InvokeBridgeGenerate(); return; }

                    // 辺から帯面は選択辺から組むため、パラメータだけで作る
                    // CreatePrimitiveMeshCommand には載らない。専用コマンドを送る。
                    if (_current == ShapeKind.EdgeRibbonFace)
                    {
                        InvokeEdgeRibbonFaceGenerate();
                        return;
                    }

                    // 揺れもの用ボーン鎖は作るのがボーンで、メッシュではない。
                    // 単一 MeshObject を新規追加する経路は通らない。
                    if (_current == ShapeKind.SpringBoneSingle ||
                        _current == ShapeKind.SpringBoneCylinder ||
                        _current == ShapeKind.SpringBoneRevolution)
                    {
                        GenerateSpringBoneChains();
                        return;
                    }

                    // はしごから作る揺れものボーンも同じくボーンを作る。
                    if (_current == ShapeKind.SpringBoneLadder)
                    {
                        GenerateSpringBoneLadderChains();
                        return;
                    }

                    // 生成はコマンドへ流す。モデルへの反映（追加先の解決・Undo・再構築）は
                    // ディスパッチャ側が持つ。ここでメッシュを作って渡す経路は残さない
                    // （残すとディスパッチャ側の欠陥が自動検査を素通りするため）。
                    var cmd = BuildCreateCommand();
                    if (cmd == null) { _statusLabel.text = "生成失敗"; return; }
                    if (SendCommand == null) { _statusLabel.text = "配線が足りません（SendCommand）"; return; }

                    SendCommand(cmd);
                    _statusLabel.text = T("Create");

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

                // 部品が1つも無いと頂点0のメッシュになる。
                case ShapeKind.Ribbon:
                    return _ribbonP.BuildLoops || _ribbonP.BuildTails || _ribbonP.BuildKnot;

                // フォントが開けて、かつ文字列が空でないこと。
                case ShapeKind.Text:
                    return !string.IsNullOrWhiteSpace(_textP.Text)
                        && Poly_Ling.GlyphText.PlyFontLibrary.Open(_textP.FontFamily) != null;

                // 種 A・B の両方が取込済みで、コマンドの送り先が結線されていること。
                case ShapeKind.Bridge:
                    return SendCommand != null && BridgeSeedsReady;

                // 選択辺が 1 本以上あり、コマンドの送り先が結線されていること。
                case ShapeKind.EdgeRibbonFace:
                    return SendCommand != null && EdgeRibbonFaceSelectedEdges > 0;

                // 揺れもの用ボーン鎖。折れ線を使う 2 種は点が 2 個以上要る。
                case ShapeKind.SpringBoneSingle:
                case ShapeKind.SpringBoneRevolution:
                    return SendCommand != null && _revProfile != null && _revProfile.Count >= 2;

                case ShapeKind.SpringBoneCylinder:
                    return SendCommand != null;

                // はしごから作る揺れものボーン。取り込み元が選ばれていること。
                case ShapeKind.SpringBoneLadder:
                    return SendCommand != null && _sbLadderPick.Current != null;

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
    }
}
