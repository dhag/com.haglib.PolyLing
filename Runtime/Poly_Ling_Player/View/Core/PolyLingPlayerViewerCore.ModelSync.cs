// PolyLingPlayerViewerCore.ModelSync.cs
// Player ビューアのコア：モデル切り替え・SyncUI・コマンドディスパッチとパネル通知・軌道回転の中心。
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
        // モデル切り替え
        // ================================================================

        private void SwitchActiveModel(int index)
        {
            var project = ActiveProject;
            if (project == null) return;
            // 範囲外なら何もしない
            if (index < 0 || index >= project.ModelCount) return;
            if (project.CurrentModelIndex == index) return;

            // 問題: 従来ここで project.SelectModel() + EnterSceneReset を直接行い
            // Undo 記録を伴わない経路だった。SwitchModelCommand ハンドラに統一して
            // Undo 記録 (RecordModelSwitch) + SetModelContext 同期を経由させる。
            _commandDispatcher?.Dispatch(new SwitchModelCommand(index));

            var model = project.CurrentModel;
            if (model == null) return;

            // ツールハンドラへの Project 参照更新 (SwitchModelCommand ハンドラでは扱わない分)。
            _moveToolHandler?.SetProject(project);
            _objectMoveHandler?.SetProject(project);
            _pivotOffsetHandler?.SetProject(project);
            _sculptHandler?.SetProject(project);
            _advancedSelectHandler?.SetProject(project);
            _skinWeightPaintHandler?.SetProject(project);

            _skinWeightPaintPanel?.RefreshBoneList(model);
        }

        // ================================================================
        // SyncUI / RebuildModelList
        // ================================================================

        /// <summary>
        /// ★★★ 【重大規約違反コード】 ★★★
        /// 旧 Tick から毎フレーム呼ばれる UI 同期処理。
        /// 各値（Status, 接続状態, Undo/Redo 可否等）はイベント駆動で更新すべき。
        /// Phase 5: モデル変更・選択変更・接続状態変更・Undo スタック変更等の
        /// 各イベント購読に分解して置き換える予定。
        /// 新規コードからこの関数を呼ぶことは厳禁。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        private void SyncUI()
        {
            if (_layoutRoot == null) return;

            _layoutRoot.StatusLabel.text = $"Status: {_status}";

            bool clientExists = _remoteMode == RemoteMode.Client && _client != null;
            bool serverActive = _remoteMode == RemoteMode.Server && _playerServer != null;
            bool isConnected  = clientExists && _client.IsConnected;

            _layoutRoot.RemoteSection.style.display =
                (clientExists || serverActive) ? DisplayStyle.Flex : DisplayStyle.None;
            _layoutRoot.ConnectBtn   .style.display = isConnected ? DisplayStyle.None : DisplayStyle.Flex;
            _layoutRoot.DisconnectBtn.style.display = isConnected ? DisplayStyle.Flex : DisplayStyle.None;
            _layoutRoot.FetchBtn.SetEnabled(isConnected);
            _layoutRoot.UndoBtn.SetEnabled(_editOps?.CanUndo ?? false);
            _layoutRoot.RedoBtn.SetEnabled(_editOps?.CanRedo ?? false);
        }

        /// <summary>
        /// リモートモード（インスペクタ設定）に応じて左ペインの表示を出し分ける。
        /// Client 時のみ「サーバと連携」、Server 時のみ「リモートサーバ」を表示。
        /// _remoteMode はセッション中不変のため初期化時に一度だけ適用する。
        /// </summary>
        private void ApplyRemoteModeVisibility()
        {
            if (_layoutRoot == null) return;
            bool isClient = _remoteMode == RemoteMode.Client;
            bool isServer = _remoteMode == RemoteMode.Server;

            if (_layoutRoot.RemoteFoldout != null)
                _layoutRoot.RemoteFoldout.style.display = isClient ? DisplayStyle.Flex : DisplayStyle.None;
            if (_layoutRoot.RemoteServerBtn != null)
                _layoutRoot.RemoteServerBtn.style.display = isServer ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RebuildModelList()
        {
            if (_layoutRoot?.ModelListContainer == null) return;
            _layoutRoot.ModelListContainer.Clear();

            var project = ActiveProject;
            var m = project?.CurrentModel;
            if (m != null)
            {
                var lbl = new Label($"{m.Name}  ({m.Count})");
                lbl.style.color = new StyleColor(Color.white);
                _layoutRoot.ModelListContainer.Add(lbl);
            }

            // ドロップダウン更新
            if (_layoutRoot.ModelSelectDropdown != null && project != null)
            {
                var choices = new List<string>();
                for (int i = 0; i < project.ModelCount; i++)
                {
                    var mdl = project.GetModel(i);
                    choices.Add(mdl?.Name ?? $"Model {i}");
                }
                _layoutRoot.ModelSelectDropdown.choices = choices;
                int cur = project.CurrentModelIndex;
                string curVal = (cur >= 0 && cur < choices.Count) ? choices[cur] : (choices.Count > 0 ? choices[0] : "");
                _layoutRoot.ModelSelectDropdown.SetValueWithoutNotify(curVal);
            }

            NotifyPanels(ChangeKind.ListStructure);
        }

        // ================================================================
        // コマンドディスパッチ / パネル通知
        // ================================================================

        private void DispatchPanelCommand(PanelCommand cmd)
        {
            _commandDispatcher?.Dispatch(cmd);
        }

        /// <summary>
        /// スキンW数値設定モードで焼き込んだ頂点カラーを消す。
        /// 対象は数値設定パネルと同じく選択中の描画メッシュ全件
        /// （CurrentTargetMesh は常に -1 を返すため、MeshSceneRenderer 側の
        /// CollectWeightVisTargets も SelectedDrawableMeshIndices を使う）。
        /// </summary>
        private void ClearNumericWeightVisualization()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return;

            foreach (var mc in Poly_Ling.UI.SkinWeightOperations.CollectTargetMeshContexts(model))
                if (mc?.UnityMesh != null) mc.UnityMesh.colors = null;
        }

        /// <summary>
        /// src の頂点データ（Position・Normal・BoneWeight 等）を dst に上書きコピーする。
        /// 頂点数が一致する場合のみ実行。スキンウェイト Undo 書き戻し用。
        /// </summary>
        private static void CopyMeshObjectVertexData(Poly_Ling.Data.MeshObject src, Poly_Ling.Data.MeshObject dst)
        {
            if (src == null || dst == null) return;
            if (src.VertexCount != dst.VertexCount) return;
            for (int i = 0; i < src.VertexCount; i++)
            {
                var sv = src.Vertices[i];
                var dv = dst.Vertices[i];
                dv.Position   = sv.Position;
                dv.BoneWeight = sv.BoneWeight;
                // UV スロットを同期（UV 編集の Undo に必要）
                dv.UVs.Clear();
                foreach (var uv in sv.UVs)
                    dv.UVs.Add(uv);
            }
            dst.InvalidatePositionCache();

            // ウェイトごと書き戻すため種別も合わせる。
            // src はスナップショット由来で SkinKind を持っているのでそれを正とする。
            dst.SetSkinKind(src.SkinKind);
        }

        /// <summary>
        /// 現在アクティブなツールに応じたホバー対象種別を返す。
        /// Phase 2b-1 暫定実装: ホバーを完全に抑制すべきツール（SkinWeightPaint / Sculpt 等）では
        /// None を、それ以外では Vertex を返す（Vertex は「ホバー有効」の仮値。現状の入口側は
        /// None 判定のみで分岐するため、既存の GPU ホバー優先度 (頂点>辺>面) がそのまま動作する）。
        /// Phase 2b 以降で Edge / Face / Bone / Gizmo の厳密な kind 分岐を実装する。
        /// </summary>
        /// <summary>
        /// 選択中メッシュのローカル拡大縮小を頂点位置へ畳み込む（左ペインのボタン）。
        /// スキップした対象がある場合は理由付きの警告を左ペインの Status に出す。
        /// </summary>
        private string BakeObjectScale()
        {
            var model = ActiveProject?.CurrentModel;
            if (_commandDispatcher == null)
            {
                _status = "拡大縮小をベイク: コマンド未初期化";
                return _status;
            }

            _commandDispatcher.BakeObjectScale(model, out string message);
            _status = message;
            return message;
        }

        /// <summary>
        /// 頂点ホバーが抑止されるモード (HoverTargetKind.None) でも、スクリーン座標だけで
        /// 決まるオーバーレイは更新する。
        /// <para>
        /// 対象は ObjectMove / Camera のギズモ軸ホバーと、Sculpt のブラシ円。
        /// ブラシ円の半径は SculptToolHandler.ScreenRadiusFromWorldRadius がカメラと
        /// PreviewRect だけから求めるため、GPU ヒットテストを必要としない。
        /// </para>
        /// <para>
        /// 実体は PlayerViewportManager.NotifyScreenOnlyHover。どのハンドラへ渡すかは
        /// SetInteractionMode で登録済みの ActiveToolHandler に任せる。
        /// 要素 (頂点/辺/面) のホバーをこの経路へ移すことは禁止（同メソッドの注記参照）。
        /// </para>
        /// </summary>
        private void UpdateScreenOnlyHover(PlayerViewport vp, Vector2 localPos)
        {
            // ホバー種別が None のモードのうち、スクリーン座標だけで決まる表示を
            // 持つものだけ更新する。
            if (_interactionMode != InteractionMode.ObjectMove &&
                _interactionMode != InteractionMode.Camera &&
                _interactionMode != InteractionMode.Sculpt) return;
            _viewportManager.NotifyScreenOnlyHover(vp, localPos);
        }

        private HoverTargetKind GetCurrentHoverTargetKind()
        {
            switch (_interactionMode)
            {
                case InteractionMode.None:
                case InteractionMode.ObjectMove:
                case InteractionMode.SkinWeightPaint:
                case InteractionMode.Sculpt:
                // カメラ調整はモデル要素を触らないため GPU ヒットテストを走らせない。
                case InteractionMode.Camera:
                    return HoverTargetKind.None;
                default:
                    return HoverTargetKind.Vertex;
            }
        }

        /// <summary>
        /// 左ペインの操作対象となる選択メッシュのインデックス配列を返す。
        /// 選択が空のときはアクティブメッシュ 1 個へフォールバックする。
        /// PlayerCommandDispatcher.CollectSelectedMeshContexts と同じ規則。
        /// </summary>
        private int[] CollectSelectedMeshIndices()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return System.Array.Empty<int>();

            var sel = model.SelectedDrawableMeshIndices;
            if (sel != null && sel.Count > 0) return sel.ToArray();

            int active = model.ActiveMeshIndex;
            if (active >= 0 && active < model.MeshContextCount) return new[] { active };

            return System.Array.Empty<int>();
        }

        /// <summary>
        /// 左ペインの「法線自動計算」トグルを選択メッシュの PreserveNormals から書き戻す。
        /// 対象メッシュが全て PreserveNormals == false のときだけ ON。
        /// 混在・対象なしは OFF にする。
        /// </summary>
        private void SyncNormalRecalcToggle()
        {
            var tog = _layoutRoot?.AutoRecalcNormalsToggle;
            if (tog == null) return;

            var model = ActiveProject?.CurrentModel;
            var indices = CollectSelectedMeshIndices();

            bool autoOn = false;
            if (model != null && indices.Length > 0)
            {
                autoOn = true;
                foreach (int idx in indices)
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc?.MeshObject == null || mc.MeshObject.PreserveNormals)
                    {
                        autoOn = false;
                        break;
                    }
                }
            }

            _isSyncingNormalRecalcToggle = true;
            tog.SetValueWithoutNotify(autoOn);
            _isSyncingNormalRecalcToggle = false;
        }

        // ================================================================
        // 軌道回転の中心（ピボット）
        // ================================================================

        /// <summary>
        /// 軌道回転の中心をワールド座標で返す。null なら従来どおり
        /// OrbitCameraController.Target を中心に回る。
        ///
        /// OrbitCameraController.GetOrbitPivot として配線され、軌道ドラッグ
        /// 開始時に 1 回だけ呼ばれる。選択変更イベントからは呼ばれないため、
        /// 選択しただけでは視点は動かない。
        ///
        /// 優先順位:
        ///   1. 「現在の選択を中心に」釦で確定した固定ピボット
        ///   2. 「回転はローカル原点中心」が ON ならローカル原点（ピボット）の重心
        ///   3. どちらでもなければ null
        /// </summary>
        private Vector3? ComputeOrbitPivot()
        {
            if (_explicitOrbitPivot.HasValue) return _explicitOrbitPivot;
            if (_orbitAroundLocalOrigin)      return ComputeLocalOriginCentroid();
            return null;
        }

        /// <summary>
        /// 選択されている要素（頂点 / 辺 / 面 / 線分）のワールド重心。
        /// 1 点も無ければ null。
        ///
        /// 集計規則は MoveToolHandler.UpdateAffectedVertices と同じ。
        /// SelectionState の Vertices / Faces / Lines はメッシュ内ローカル番号
        /// なので、選択メッシュごとにその MeshContext.Selection を見る。
        /// </summary>
        private Vector3? ComputeElementCentroid()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return null;

            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(ctxIdx);
                if (mc?.MeshObject == null) continue;

                var sel = mc.Selection;
                if (sel == null) continue;

                var mo = mc.MeshObject;
                var affected = new HashSet<int>();

                foreach (var v  in sel.Vertices) affected.Add(v);
                foreach (var e  in sel.Edges)    { affected.Add(e.V1); affected.Add(e.V2); }
                foreach (var fi in sel.Faces)
                    if (fi >= 0 && fi < mo.FaceCount)
                        foreach (var vi in mo.Faces[fi].VertexIndices)
                            affected.Add(vi);
                foreach (var li in sel.Lines)
                    if (li >= 0 && li < mo.FaceCount)
                    {
                        var face = mo.Faces[li];
                        if (face.VertexCount == 2)
                        { affected.Add(face.VertexIndices[0]); affected.Add(face.VertexIndices[1]); }
                    }

                // ローカル頂点をワールド変換してから集計する。スキンド頂点に
                // 実際に適用される行列はメッシュの WorldMatrix ではなくボーンの
                // ブレンドなので、MeshContext.LocalToWorld を使う
                // （MoveToolHandler.UpdateGizmoState と同じ）。
                foreach (int vi in affected)
                    if (vi >= 0 && vi < mo.VertexCount)
                    { sum += mc.LocalToWorld(vi, mo.Vertices[vi].Position); count++; }
            }

            return count > 0 ? (Vector3?)(sum / count) : null;
        }

        /// <summary>
        /// 選択オブジェクト（ボーン + 描画メッシュ）のローカル原点の重心。
        /// 1 件も無ければ null。
        ///
        /// ローカル原点 = MeshContext.WorldMatrix の平行移動成分。これは
        /// PivotOffsetTool が言う「ピボット原点」と同じ点であり、
        /// ObjectMoveTool.UpdateGizmoCenter が使う点とも一致する。
        /// </summary>
        private Vector3? ComputeLocalOriginCentroid()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return null;

            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (int idx in model.SelectedBoneIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc == null) continue;
                var wm = mc.WorldMatrix;
                sum += new Vector3(wm.m03, wm.m13, wm.m23);
                count++;
            }
            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                // ボーン選択に既に含まれていれば重複させない
                if (model.SelectedBoneIndices.Contains(idx)) continue;
                var mc = model.GetMeshContext(idx);
                if (mc == null) continue;
                var wm = mc.WorldMatrix;
                sum += new Vector3(wm.m03, wm.m13, wm.m23);
                count++;
            }

            return count > 0 ? (Vector3?)(sum / count) : null;
        }

        private void NotifyPanels(ChangeKind kind)
        {
            var project = ActiveProject;
            if (project == null || _panelContext == null) return;
            var view = new PlayerProjectView(project);
            _panelContext.Notify(view, kind);

            // リモートサーバ稼働時、本体の選択/モデル変更を接続クライアントへ配信する。
            // （エディタは Tick を回さないため、この中心経路から通知する）
            if (_remoteMode == RemoteMode.Server && _playerServer != null)
                _playerServer.NotifySelectionChanged();

            if (_interactionMode == InteractionMode.ObjectMove)
                _boneEditorSubPanel?.Refresh();

            if (kind == ChangeKind.Selection || kind == ChangeKind.ModelSwitch)
                _blendSubPanel?.OnSelectionChanged();

            // シュリンカーは選択状態を使わないため ModelSwitch だけに追随する
            // （Selection で作り直すとプレビュー中の状態が失われる）。
            if (kind == ChangeKind.ModelSwitch)
            {
                _shrinkSubPanel?.SetModel(ActiveProject?.CurrentModel);
                _shrinkFaceSubPanel?.SetModel(ActiveProject?.CurrentModel);
            }

            // TPSモーフはモデル単位でビフォー／アフター／ターゲットを選ぶ。
            // モデルが変わったら選び直しになる（計算中のジョブは中止される）。
            if (kind == ChangeKind.ModelSwitch)
                _thinPlateMorphSubPanel?.SetModel(ActiveProject?.CurrentModel);

            // 左ペインの「法線自動計算」トグルを選択メッシュの状態へ追随させる。
            SyncNormalRecalcToggle();

            foreach (var (section, refresh) in _sectionRefreshPairs)
                if (section?.style.display == DisplayStyle.Flex) refresh();

            if (_layoutRoot?.ModelBlendSection != null &&
                _layoutRoot.ModelBlendSection.style.display == DisplayStyle.Flex)
                _modelBlendSubPanel?.OnViewChanged(view, kind);

            // 描画準備の再実行（OnRenderObject 経路の Submit 用データ更新）。
            // kind で入口を分ける。EnterTopologyChanged は RebuildAdapter を通り、
            // UnifiedSystemAdapter を Dispose して全メッシュから GPU バッファを
            // 作り直す。選択や属性が変わっただけでこれを呼ぶと、リストを触るたびに
            // 全再構築が走って待たされるため、その2つは EnterSelectionChanged に回す。
            bool __full = kind == ChangeKind.ListStructure || kind == ChangeKind.ModelSwitch;
            // 属性変更は Hidden / Locked を GPU へ書き戻す軽量経路へ回す。
            // RebuildAdapter は伴わない。
            bool __attr = kind == ChangeKind.Attributes;
            // kind.ToString() と三項の文字列選択は引数側で必ず評価される。
            // スイッチをここで見てから呼ぶ。
            if (PLDiag.Enabled && PLDiag.Notify)
            {
                PLDiag.NotifyKind(kind.ToString(),
                    __full ? "EnterTopologyChanged"
                           : (__attr ? "EnterMeshAttributesChanged" : "EnterSelectionChanged"));
            }
            if (__full)
                _viewportManager.EnterTopologyChanged(ActiveProject);
            else if (__attr)
                _viewportManager.EnterMeshAttributesChanged(ActiveProject);
            else
                _viewportManager.EnterSelectionChanged(ActiveProject);

            OnChanged?.Invoke(kind);
        }
    }
}
