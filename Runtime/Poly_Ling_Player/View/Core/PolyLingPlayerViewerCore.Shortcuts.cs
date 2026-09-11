// PolyLingPlayerViewerCore.Shortcuts.cs
// Player ビューアのコア：キーボードショートカットの配線。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.Serialization;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.EditorBridge;
using Poly_Ling.View;
using Poly_Ling.MeshListV2;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectArray;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        // ================================================================
        // キーボードショートカット配線
        //   対応表: デフォルト (ShortcutMap.CreateDefault) + CSV 上書き
        //           (<persistentDataPath>/PolyLing/keymap.csv、あれば起動時読込)。
        //   コマンド実体はここのボタン用処理を流用する (重複させない)。
        // ================================================================

        private void WireShortcuts()
        {
            // 対応表は CSV 優先。CSV に有効行があれば LoadCsv が内部で既定表を
            // 全破棄して置き換えるため、実効割当は「CSV だけ」か「コードの既定表だけ」
            // のどちらかになり、両者が混ざることはない (詳細は ShortcutMap.cs 冒頭)。
            var map = ShortcutMap.CreateDefault();
            int applied = map.LoadCsv(ShortcutMap.DefaultCsvPath);
            if (applied > 0)
                Debug.Log($"[Shortcut] 対応表を CSV で置換: {applied} 件 ({ShortcutMap.DefaultCsvPath})。"
                        + " CSV に無いコマンドはキー割当なし。");
            else
                Debug.Log("[Shortcut] 対応表はコードの既定表 (ShortcutMap.CreateDefault)。"
                        + $" CSV 未使用 (無し / 有効行 0 / 読取失敗): {ShortcutMap.DefaultCsvPath}");

            _shortcutController = new PlayerShortcutController(map);

            // コマンドID → 実行内容。対応するツールボタンと同じ処理を割り当てる。
            _shortcutController.Register(ShortcutMap.CmdUndo,
                () => _commandDispatcher?.Dispatch(new PerformUndoCommand()));
            _shortcutController.Register(ShortcutMap.CmdRedo,
                () => _commandDispatcher?.Dispatch(new PerformRedoCommand()));
            _shortcutController.Register(ShortcutMap.CmdToolVertexMove,
                () => ShowCategory1Panel(InteractionMode.VertexMove));
            _shortcutController.Register(ShortcutMap.CmdToolObjectMove,
                () => { ShowCategory1Panel(InteractionMode.ObjectMove); _boneEditorSubPanel?.ShowObjectPoseTab(); });
            _shortcutController.Register(ShortcutMap.CmdToolSculpt,
                () => ShowCategory1Panel(InteractionMode.Sculpt));
            _shortcutController.Register(ShortcutMap.CmdToolAdvSelect,
                () => ShowCategory1Panel(InteractionMode.AdvancedSelect));
            // C : 回転ツール / Q : 拡大縮小ツール。左ペインのボタン押下と同じ処理。
            _shortcutController.Register(ShortcutMap.CmdToolRotate, ShowRotatePanel);
            _shortcutController.Register(ShortcutMap.CmdToolScale,  ShowScalePanel);

            // 一時選択サブツール (R = 矩形 / G = 投げ縄)。1 回の確定・クリック・Esc で復帰。
            _shortcutController.Register(ShortcutMap.CmdSubToolBoxSelect,
                () => EnterSelectSubTool(false));
            _shortcutController.Register(ShortcutMap.CmdSubToolLassoSelect,
                () => EnterSelectSubTool(true));
            // Delete は面追加モードの間は常に「直前の点の取り消し」に使う。
            // 取り消す対象が無いときは何もしない（選択削除へは落とさない）。
            // 線分（連続）は _points が常に空で取り消し対象が無いように見えるため、
            // ここで選択削除へ落とすと描画中に選択ジオメトリが消えていた。
            // それ以外のモードは従来どおり選択削除。
            _shortcutController.Register(ShortcutMap.CmdSubToolDelete, () =>
            {
                if (_interactionMode == InteractionMode.AddFace)
                {
                    ExecuteAddFaceRemoveLastPoint();
                    return;
                }
                ExecuteDeleteSelection();
            });
            // D : 面削除モード。面クリックで即削除。Escape で直前のツールへ戻る。
            _shortcutController.Register(ShortcutMap.CmdToolDeleteFace,
                EnterDeleteFaceMode);
            // F : 面追加ツール。左ペインの「面追加」ボタンと同じ処理。
            _shortcutController.Register(ShortcutMap.CmdToolAddFace,
                ShowAddFacePanel);
            // Escape は一時選択サブツールと面削除モードの両方の復帰に使う。
            // どちらも進入中でなければ各メソッドが即 return するため順序は問わない。
            _shortcutController.OnEscape = () => { ExitSelectSubTool(); ExitDeleteFaceMode(); };

            // 選択頂点の結合 (Ctrl+J = 距離無視 / Ctrl+Shift+J = しきい値)。
            // モードを変えない即時実行なので、押した瞬間に結合が完了する。
            _shortcutController.Register(ShortcutMap.CmdMergeVerticesCentroid,
                ExecuteMergeSelectedToCentroid);
            _shortcutController.Register(ShortcutMap.CmdMergeVerticesThreshold,
                ExecuteMergeSelectedByThreshold);

            // 右ペインのオブジェクトリストを開く (Ctrl+O)。ボタン押下と同じ処理。
            _shortcutController.Register(ShortcutMap.CmdPanelMeshList,
                ShowMeshListPanel);

            // 図形生成 (2キー連続 P→形状)。サブメニューを開くだけ (生成はしない)。
            _shortcutController.Register(ShortcutMap.CmdShapeCube,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Cube));
            _shortcutController.Register(ShortcutMap.CmdShapeSphere,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Sphere));
            _shortcutController.Register(ShortcutMap.CmdShapeCylinder,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Cylinder));
            _shortcutController.Register(ShortcutMap.CmdShapeCapsule,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Capsule));
            _shortcutController.Register(ShortcutMap.CmdShapePlane,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Plane));
            _shortcutController.Register(ShortcutMap.CmdShapePyramid,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Pyramid));
            _shortcutController.Register(ShortcutMap.CmdShapeRevolution,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Revolution));
            _shortcutController.Register(ShortcutMap.CmdShapeProfile2D,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Profile2D));
            _shortcutController.Register(ShortcutMap.CmdShapeNohMask,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.NohMask));
            _shortcutController.Register(ShortcutMap.CmdShapeFrill,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Frill));
            _shortcutController.Register(ShortcutMap.CmdShapePipe,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Pipe));
            _shortcutController.Register(ShortcutMap.CmdShapePlaceObject,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.PlaceObject));
            _shortcutController.Register(ShortcutMap.CmdShapeObjectArray,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.ObjectArray));

            // 画面キャプチャ (K M / K T / K W)。
            _shortcutController.Register(ShortcutMap.CmdCaptureMain,
                () => ExecuteCapture(CaptureTarget.MainView));
            _shortcutController.Register(ShortcutMap.CmdCaptureTriView,
                () => ExecuteCapture(CaptureTarget.TriView));
            _shortcutController.Register(ShortcutMap.CmdCaptureWindow,
                () => ExecuteCapture(CaptureTarget.Window));

            _shortcutController.Attach(_uiRoot);
        }

        /// <summary>
        /// 図形生成パネル（オブジェクト接地）の配置元候補。
        /// 現在のモデルの描画オブジェクトを (表示名, MeshObject) で列挙する。
        /// </summary>
        private List<(string Label, MeshObject Mesh)> BuildDrawableMeshList()
        {
            var list = new List<(string, MeshObject)>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;

            foreach (var entry in model.DrawableMeshes)
            {
                var mc = model.GetMeshContext(entry.MasterIndex);
                if (mc?.MeshObject == null) continue;
                list.Add(($"[{entry.MasterIndex}] {mc.Name ?? "?"}", mc.MeshObject));
            }
            return list;
        }

        /// <summary>
        /// 図形生成パネル（オブジェクト接地）の配置元候補。
        /// BuildDrawableMeshList と同じ並び・同じ表示名に MasterIndex を足したもの。
        /// 子孫の解決に MasterIndex が要るため、配置元だけこちらを使う。
        /// </summary>
        private List<(string Label, int MasterIndex, MeshObject Mesh)> BuildDrawableMeshEntryList()
        {
            var list = new List<(string, int, MeshObject)>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;

            foreach (var entry in model.DrawableMeshes)
            {
                var mc = model.GetMeshContext(entry.MasterIndex);
                if (mc?.MeshObject == null) continue;
                list.Add(($"[{entry.MasterIndex}] {mc.Name ?? "?"}", entry.MasterIndex, mc.MeshObject));
            }
            return list;
        }

        /// <summary>
        /// 面追加パネルの「追加先」候補。BuildDrawableMeshEntryList と同じ並び・表示名で、
        /// MeshObject を持たない項目を除いた (表示名, MasterIndex) を返す。
        /// </summary>
        private List<(string Label, int MasterIndex)> BuildAddFaceMeshEntries()
        {
            var list = new List<(string, int)>();
            foreach (var e in BuildDrawableMeshEntryList())
                list.Add((e.Label, e.MasterIndex));
            return list;
        }

        /// <summary>
        /// 面追加パネルの「マテリアル」候補。スロット順の名前を返す。未設定は "(None)"。
        /// マテリアルリストパネルの表示（PlayerMaterialListSubPanel.MatName）と揃える。
        /// </summary>
        private List<string> BuildAddFaceMaterialNames()
        {
            var list = new List<string>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;

            for (int i = 0; i < model.MaterialCount; i++)
            {
                var mat = model.GetMaterial(i);
                list.Add(mat != null ? mat.name : "(None)");
            }
            return list;
        }

        /// <summary>
        /// 指定オブジェクトとその子孫を (MasterIndex, MeshObject) でリスト順に列挙する。
        /// 接地の配置元で、ルートを1つチェックすると子孫も配置元に加わるようにするために使う。
        ///
        /// 結合はしない。各メッシュは自分のローカル座標のまま返すので、
        /// 一覧で子を直接チェックしたときとまったく同じ扱いになる
        /// （配置は rung 中心に各メッシュの原点が乗る）。
        ///
        /// 面を持たないもの（グループ用の空オブジェクト等）は返さない。配置しても
        /// 何も出ないうえ、rung ごとの巡回・抽選の母数に入ると空きが出るため。
        ///
        /// 親子は HierarchyParentIndex で判定する
        /// （ワールド行列の組み立てと同じ基準。ModelContext.ComputeWorldMatrices）。
        /// </summary>
        private List<(int MasterIndex, MeshObject Mesh)> BuildSubtreeMeshList(int rootMasterIndex)
        {
            var list  = new List<(int, MeshObject)>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;
            if (rootMasterIndex < 0 || rootMasterIndex >= model.MeshContextCount) return list;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (i != rootMasterIndex && !IsDescendantOf(model, i, rootMasterIndex)) continue;

                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (!IsDrawableMeshContext(mc)) continue;
                if (mc.MeshObject.FaceCount == 0) continue;

                list.Add((i, mc.MeshObject));
            }

            return list;
        }

        /// <summary>index が rootIndex の子孫か。HierarchyParentIndex を辿る。</summary>
        private static bool IsDescendantOf(ModelContext model, int index, int rootIndex)
        {
            int guard = 0;
            int cur = index;
            while (guard++ < 4096)
            {
                var mc = model.GetMeshContext(cur);
                if (mc == null) return false;

                int parent = mc.HierarchyParentIndex;
                if (parent < 0 || parent >= model.MeshContextCount) return false;
                if (parent == rootIndex) return true;

                cur = parent;
            }
            return false;
        }

        /// <summary>描画オブジェクトか（TypedMeshIndices の Drawable と同じ判定）。</summary>
        private static bool IsDrawableMeshContext(MeshContext mc)
        {
            var t = mc.Type;
            return t == MeshType.Mesh || t == MeshType.BakedMirror || t == MeshType.MirrorSide;
        }

        /// <summary>
        /// 描画オブジェクトの (表示名, MasterIndex) 一覧。
        /// 歪み複製パネルが複製元のチェック一覧と出力先ドロップダウンに使う。
        /// 表示名は BuildDrawableMeshList と同じ形にそろえてある。
        /// </summary>
        private List<(string Label, int MasterIndex)> BuildDrawableIndexList()
        {
            var list = new List<(string, int)>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;

            foreach (var entry in model.DrawableMeshes)
            {
                var mc = model.GetMeshContext(entry.MasterIndex);
                if (mc?.MeshObject == null) continue;
                list.Add(($"[{entry.MasterIndex}] {mc.Name ?? "?"}", entry.MasterIndex));
            }
            return list;
        }

        /// <summary>
        /// 現在のモデルにある描画オブジェクト名の一覧。図形生成パネルが
        /// 名前欄の非重複候補を作るのに使う。
        /// </summary>
        private List<string> BuildExistingMeshNames()
        {
            var list = new List<string>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;

            foreach (var mc in model.MeshContextList)
            {
                if (mc == null || string.IsNullOrEmpty(mc.Name)) continue;
                list.Add(mc.Name);
            }
            return list;
        }

        /// <summary>
        /// 図形生成パネルのマテリアル指定ドロップダウン用の表示名一覧。
        /// 添字がそのままマテリアルスロット番号になる。
        /// スロットが 1 つも無いモデルでは空リストを返す
        /// （パネル側が「生成時に作成」表示へ切り替える）。
        /// </summary>
        private List<string> BuildMaterialNames()
        {
            var list  = new List<string>();
            var model = ActiveProject?.CurrentModel;
            if (model?.MaterialReferences == null) return list;

            for (int i = 0; i < model.MaterialReferences.Count; i++)
            {
                var matRef = model.MaterialReferences[i];
                string name = string.IsNullOrEmpty(matRef?.Name) ? "(no name)" : matRef.Name;
                list.Add($"[{i}] {name}");
            }
            return list;
        }
    }
}
