// PlayerBoneEditorSubPanel.Actions.cs
// ボーンエディタ：クイック操作・スコープ切替・対象取得・Refresh・並べ替え・原点 CSV・姿勢くさび。
// Runtime/Poly_Ling_Player/View/SubPanels/Bone/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.EditorBridge;
using Poly_Ling.View;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    public partial class PlayerBoneEditorSubPanel
    {
        // ================================================================
        // クイック操作（よく使う相対移動・相対回転）
        // ================================================================

        /// <summary>
        /// 選択対象のローカル姿勢を決め打ちの量だけ動かすボタン行。
        /// 値は現在値への加算で、複数選択でもそれぞれの現在値を基準にする。
        /// </summary>
        private VisualElement BuildQuickOffsetRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop     = 2;

            Button Make(string text, string tip, System.Action onClick)
            {
                var b = new Button(onClick) { text = text, tooltip = tip };
                b.style.flexGrow      = 1;
                b.style.height        = 20;
                b.style.fontSize      = 9;
                b.style.marginRight   = 2;
                b.style.paddingLeft   = 2;
                b.style.paddingRight  = 2;
                row.Add(b);
                return b;
            }

            Make("Z90度回転", "選択対象のローカル Z 回転に +90 度を足す",
                 () => OffsetTransform(SetBoneTransformValueCommand.Field.RotationZ, 90f, "Z+90度回転"));
            Make("Y0.1移動", "選択対象のローカル Y 位置に +0.1 を足す",
                 () => OffsetTransform(SetBoneTransformValueCommand.Field.PositionY, 0.1f, "Y+0.1移動"));
            var last = Make("Z-90度回転", "選択対象のローカル Z 回転に −90 度を足す",
                 () => OffsetTransform(SetBoneTransformValueCommand.Field.RotationZ, -90f, "Z-90度回転"));
            last.style.marginRight = 0;

            return row;
        }

        /// <summary>
        /// ローカル姿勢の 1 軸を相対で動かす。
        ///
        /// 【既存の Begin → Set → End 経路へ流す理由】
        ///   「原点だけ移動」の自頂点再ローカル化、スキン固定の BindPose 追従、
        ///   ポーズ層への書き分け、Undo スナップショットは全て
        ///   PlayerCommandDispatcher の SetBoneTransformValueCommand 側にある。
        ///   専用の相対コマンドを足すとこの後処理を丸ごと書き写すことになるので、
        ///   ここでは「現在値 + 差分」を求めて既存コマンドへ渡すだけにする。
        ///
        /// 現在値はオブジェクトごとに違うため 1 件ずつ送る
        /// （SetBoneTransformValueCommand は配列全員へ同じ値を代入するので束ねられない）。
        /// </summary>
        private void OffsetTransform(
            SetBoneTransformValueCommand.Field field, float delta, string undoLabel)
        {
            var model = GetModel?.Invoke();
            if (model == null) return;

            var indices = GetTargetIndices();
            if (indices.Length == 0) return;

            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            var mvs      = GetObjectMoveSettings?.Invoke();
            bool poseMode = mvs != null && mvs.MoveMode == Poly_Ling.Tools.BoneMoveMode.PoseLayer;

            SendCommand(new BeginBoneTransformSliderDragCommand(modelIdx, indices)
            {
                Mode       = mvs?.MoveMode ?? Poly_Ling.Tools.BoneMoveMode.BoneOnlyRebind,
                OriginOnly = IsOriginOnlyActive(),
            });

            bool isRotation =
                field == SetBoneTransformValueCommand.Field.RotationX ||
                field == SetBoneTransformValueCommand.Field.RotationY ||
                field == SetBoneTransformValueCommand.Field.RotationZ;

            foreach (int idx in indices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc == null) continue;
                if (!TryReadTransformValue(mc, field, poseMode, out float cur)) continue;

                float next = cur + delta;
                // 回転は押すたびに 360 を超えて伸びていくので毎回畳む。
                if (isRotation) next = NormAngle180(next);

                SendCommand(new SetBoneTransformValueCommand(modelIdx, new[] { idx }, field, next));
            }

            SendCommand(new EndBoneTransformSliderDragCommand(modelIdx, undoLabel));
            Refresh();
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// 相対操作の基準値を読む。TRS 欄が表示しているのと同じ値を返す
        /// （モードC はポーズ層 "Manual" の差分、それ以外は BoneTransform）。
        /// </summary>
        private static bool TryReadTransformValue(
            MeshContext mc, SetBoneTransformValueCommand.Field field, bool poseMode, out float value)
        {
            value = 0f;

            if (poseMode)
            {
                var layer = mc.BonePoseData?.GetLayer("Manual");
                Vector3 pRot = layer != null ? NormEuler180(layer.DeltaRotation.eulerAngles) : Vector3.zero;
                Vector3 pPos = layer != null ? layer.DeltaPosition : Vector3.zero;
                switch (field)
                {
                    case SetBoneTransformValueCommand.Field.PositionX: value = pPos.x; return true;
                    case SetBoneTransformValueCommand.Field.PositionY: value = pPos.y; return true;
                    case SetBoneTransformValueCommand.Field.PositionZ: value = pPos.z; return true;
                    case SetBoneTransformValueCommand.Field.RotationX: value = pRot.x; return true;
                    case SetBoneTransformValueCommand.Field.RotationY: value = pRot.y; return true;
                    case SetBoneTransformValueCommand.Field.RotationZ: value = pRot.z; return true;
                    // スケールはポーズ層の対象外（ディスパッチャ側も BoneTransform へ書く）
                    default: return false;
                }
            }

            var bt = mc.BoneTransform;
            if (bt == null) return false;
            switch (field)
            {
                case SetBoneTransformValueCommand.Field.PositionX: value = bt.Position.x; return true;
                case SetBoneTransformValueCommand.Field.PositionY: value = bt.Position.y; return true;
                case SetBoneTransformValueCommand.Field.PositionZ: value = bt.Position.z; return true;
                case SetBoneTransformValueCommand.Field.RotationX: value = bt.Rotation.x; return true;
                case SetBoneTransformValueCommand.Field.RotationY: value = bt.Rotation.y; return true;
                case SetBoneTransformValueCommand.Field.RotationZ: value = bt.Rotation.z; return true;
                case SetBoneTransformValueCommand.Field.ScaleX:    value = bt.Scale.x;    return true;
                case SetBoneTransformValueCommand.Field.ScaleY:    value = bt.Scale.y;    return true;
                case SetBoneTransformValueCommand.Field.ScaleZ:    value = bt.Scale.z;    return true;
                default: return false;
            }
        }

        // ================================================================
        // スコープ切り替え
        // ================================================================

        /// <summary>外部の入口ボタンから「ボーン」タブを開く。</summary>
        public void ShowBonesTab()      => SetScope(SubPanelScope.BonesOnly);
        /// <summary>外部の入口ボタンから「オブジェクト姿勢」タブを開く。</summary>
        public void ShowObjectPoseTab() => SetScope(SubPanelScope.MeshesOnly);

        private void SetScope(SubPanelScope scope)
        {
            _scope = scope;
            ApplyPickFilter();
            UpdateTabHighlight();
            Refresh();
        }

        /// <summary>
        /// このパネルのスコープを ObjectMoveTool のピック対象へ反映する。
        ///
        /// 【Refresh から切り出してある理由】
        ///   Refresh は選択変更のたびに（このパネルを開いていなくても）呼ばれる。
        ///   ここで書き込むと、オブジェクトリスト側がビューポート操作を持っている間も
        ///   ピック対象がこのパネルのタブ状態に上書きされてしまう。
        ///   ピック対象は「今ビューポートを担当しているパネル」が入場時に決める。
        /// </summary>
        public void ApplyPickFilter()
        {
            var s = GetObjectMoveSettings?.Invoke();
            if (s == null) return;
            bool boneScope = _scope == SubPanelScope.BonesOnly;
            s.PickBones         = boneScope;
            s.PickMeshesNoSkin  = !boneScope;
            s.PickMeshesSkinned = false;
        }

        private void UpdateTabHighlight()
        {
            void Style(Button b, bool active)
            {
                if (b == null) return;
                b.style.backgroundColor = new StyleColor(
                    active ? new Color(0.25f, 0.45f, 0.7f) : new Color(0.2f, 0.2f, 0.2f));
                b.style.color = new StyleColor(Color.white);
            }
            Style(_tabBones,  _scope == SubPanelScope.BonesOnly);
            Style(_tabMeshes, _scope == SubPanelScope.MeshesOnly);
        }

        // ================================================================
        // 対象インデックス取得（MirrorSide 除外）
        // ================================================================

        private int[] GetTargetIndices()
        {
            var model = GetModel?.Invoke();
            if (model == null) return Array.Empty<int>();

            IEnumerable<int> raw;
            switch (_scope)
            {
                case SubPanelScope.BonesOnly:
                    raw = model.SelectedBoneIndices;
                    break;
                case SubPanelScope.MeshesOnly:
                    raw = model.SelectedDrawableMeshIndices;
                    break;
                default:
                    raw = model.SelectedBoneIndices
                        .Concat(model.SelectedDrawableMeshIndices
                            .Where(i => !model.SelectedBoneIndices.Contains(i)));
                    break;
            }

            return raw.Where(i =>
            {
                var mc = model.GetMeshContext(i);
                return mc != null && mc.Type != MeshType.MirrorSide;
            }).ToArray();
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;

            // ObjectMoveSettings -> チェックボックス同期
            // 他経路 (Editor 拡張 UI 等) で値が変わっている可能性があるので
            // Refresh のたびに読み直す。_suppressMoveSettings で循環更新を防止。
            var moveSettings = GetObjectMoveSettings?.Invoke();
            if (moveSettings != null)
            {
                _suppressMoveSettings = true;
                try
                {
                    _toggleMoveWithChildren?.SetValueWithoutNotify(moveSettings.MoveWithChildren);
                    _toggleOriginOnly?.SetValueWithoutNotify(moveSettings.OriginOnly);
                    _toggleShowMoveGizmo?.SetValueWithoutNotify(moveSettings.AllowMoveGizmo);
                    _toggleShowRotationGizmo?.SetValueWithoutNotify(moveSettings.AllowRotationGizmo);
                    _toggleModeA?.SetValueWithoutNotify(moveSettings.MoveMode == Poly_Ling.Tools.BoneMoveMode.BoneOnlyRebind);
                    _toggleModeB?.SetValueWithoutNotify(moveSettings.MoveMode == Poly_Ling.Tools.BoneMoveMode.SkinBakeRebind);
                    _toggleModeC?.SetValueWithoutNotify(moveSettings.MoveMode == Poly_Ling.Tools.BoneMoveMode.PoseLayer);
                }
                finally { _suppressMoveSettings = false; }
            }

            var model = GetModel?.Invoke();

            bool showBoneSection = _scope != SubPanelScope.MeshesOnly;
            bool showIgnorePose  = _scope != SubPanelScope.BonesOnly;

            if (_boneSection    != null) _boneSection.style.display    = showBoneSection ? DisplayStyle.Flex : DisplayStyle.None;
            if (_ignorePoseRow  != null) _ignorePoseRow.style.display  = showIgnorePose  ? DisplayStyle.Flex : DisplayStyle.None;


            // 「ボーンポーズ」セクションはモードC（ポーズ一時）専用。A/Bでは常に非表示（モデル有無に依存しない）。
            bool poseModeC = (GetObjectMoveSettings?.Invoke())?.MoveMode == Poly_Ling.Tools.BoneMoveMode.PoseLayer;
            if (_bonePoseSection != null)
                _bonePoseSection.style.display = poseModeC ? DisplayStyle.Flex : DisplayStyle.None;

            // ボーン専用の移動オプション（子を一緒に移動・A/B/Cモード）はメッシュスコープでは非表示
            if (_moveOptionsSection != null)
                _moveOptionsSection.style.display = (_scope == SubPanelScope.MeshesOnly) ? DisplayStyle.None : DisplayStyle.Flex;
            // 「原点だけ移動」はメッシュ(オブジェクト姿勢)スコープ専用
            if (_meshMoveOptionsSection != null)
                _meshMoveOptionsSection.style.display = (_scope == SubPanelScope.MeshesOnly) ? DisplayStyle.Flex : DisplayStyle.None;

            if (model == null)
            {
                SetWarning("モデルがありません");
                SetTRSEnabled(false);
                return;
            }

            RefreshTargetDropdown(model);

            if (showBoneSection)
                RefreshBoneSection(model);

            var indices = GetTargetIndices();

            if (indices.Length == 0)
            {
                SetWarning("");
                _selectionCountLabel.text = ScopeEmptyMessage();
                SetTRSEnabled(false);
                _statusLabel.text = StatusText(model);
                return;
            }

            SetWarning("");
            _selectionCountLabel.text = $"{indices.Length} 項目選択中";
            SetTRSEnabled(true);
            ApplyOriginOnlyVisibility();

            // TRS 同期
            _suppressTRS = true;
            var mc0 = model.GetMeshContext(indices[0]);
            var bt0 = mc0?.BoneTransform;
            var mvs = GetObjectMoveSettings?.Invoke();
            bool poseMode = mvs != null && mvs.MoveMode == Poly_Ling.Tools.BoneMoveMode.PoseLayer;
            if (poseMode)
            {
                // モードC: ポーズ層 "Manual" の差分を表示（0＝ポーズ無し）
                var layer = mc0?.BonePoseData?.GetLayer("Manual");
                Vector3 pRot = layer != null ? NormEuler180(layer.DeltaRotation.eulerAngles) : Vector3.zero;
                Vector3 pPos = layer != null ? layer.DeltaPosition : Vector3.zero;
                SF(_posX, pPos.x); SF(_posY, pPos.y); SF(_posZ, pPos.z);
                SF(_rotX, pRot.x); SF(_rotY, pRot.y); SF(_rotZ, pRot.z);
                SS(_rotSliderX, pRot.x); SS(_rotSliderY, pRot.y); SS(_rotSliderZ, pRot.z);
                SF(_sclX, bt0?.Scale.x ?? 1f); SF(_sclY, bt0?.Scale.y ?? 1f); SF(_sclZ, bt0?.Scale.z ?? 1f);
            }
            else if (bt0 != null)
            {
                SF(_posX, bt0.Position.x); SF(_posY, bt0.Position.y); SF(_posZ, bt0.Position.z);
                SF(_rotX, bt0.Rotation.x); SF(_rotY, bt0.Rotation.y); SF(_rotZ, bt0.Rotation.z);
                SS(_rotSliderX, bt0.Rotation.x); SS(_rotSliderY, bt0.Rotation.y); SS(_rotSliderZ, bt0.Rotation.z);
                SF(_sclX, bt0.Scale.x);    SF(_sclY, bt0.Scale.y);    SF(_sclZ, bt0.Scale.z);
            }
            else
            {
                SF(_posX,0); SF(_posY,0); SF(_posZ,0);
                SF(_rotX,0); SF(_rotY,0); SF(_rotZ,0);
                SS(_rotSliderX,0); SS(_rotSliderY,0); SS(_rotSliderZ,0);
                SF(_sclX,1); SF(_sclY,1); SF(_sclZ,1);
            }

            if (_ignorePoseToggle != null && showIgnorePose)
                _ignorePoseToggle.SetValueWithoutNotify(
                    model.GetMeshContext(indices[0])?.IgnorePoseInArmature ?? false);


            _suppressTRS = false;
            _statusLabel.text = StatusText(model);
        }

        private void RefreshBoneSection(ModelContext model)
        {
            bool hasBone = model.HasBoneSelection;
            _btnReset?.SetEnabled(hasBone);
            _btnFocus?.SetEnabled(hasBone);

            if (!hasBone)
            {
                if (_boneNameLabel != null) _boneNameLabel.text = "";
                return;
            }

            int first = model.SelectedBoneIndices[0];
            var ctx   = model.GetMeshContext(first);

            if (_bonePoseActiveToggle != null)
                _bonePoseActiveToggle.SetValueWithoutNotify(ctx?.BonePoseData?.IsActive ?? false);

            if (_boneNameLabel    != null) _boneNameLabel.text    = ctx?.Name ?? "(no name)";
            _suppressBoneEdit = true;
            if (_masterIndexField != null) _masterIndexField.SetValueWithoutNotify(first);
            int boneIdx = model.TypedIndices?.MasterToBoneIndex(first) ?? -1;
            if (_boneIndexLabel   != null) _boneIndexLabel.text   = boneIdx >= 0 ? boneIdx.ToString() : "-";
            RefreshParentDropdown(model, first);
            _suppressBoneEdit = false;
            if (_worldPosLabel != null && ctx != null)
            {
                var wm = ctx.WorldMatrix;
                _worldPosLabel.text = $"({wm.m03:F4}, {wm.m13:F4}, {wm.m23:F4})";
            }
        }

        // ================================================================
        // ボーン並べ替え / 親変更（既存 ReorderMeshesCommand を発行）
        // ================================================================

        /// <summary>親ボーン Dropdown の選択肢と現在値を更新する。</summary>
        private void RefreshParentDropdown(ModelContext model, int targetMaster)
        {
            if (_parentBoneDropdown == null) return;

            _parentChoiceMasters.Clear();
            var choices = new List<string> { "(なし)" };
            _parentChoiceMasters.Add(-1);

            foreach (var e in model.Bones)
            {
                int m = e.MasterIndex;
                if (m == targetMaster) continue;                    // 自身は親にできない
                if (IsDescendant(model, targetMaster, m)) continue; // 子孫は親にできない（循環防止）
                var c = model.GetMeshContext(m);
                choices.Add($"{c?.Name ?? "-"} [{m}]");
                _parentChoiceMasters.Add(m);
            }

            _parentBoneDropdown.choices = choices;

            int curParent = model.GetMeshContext(targetMaster)?.HierarchyParentIndex ?? -1;
            int sel = _parentChoiceMasters.IndexOf(curParent);
            if (sel < 0) sel = 0;
            _parentBoneDropdown.SetValueWithoutNotify(choices[sel]);
        }

        // ================================================================
        // オブジェクト原点の CSV 入出力
        // ================================================================

        // 書式（先頭行・列見出し）と本文の組み立ては
        // Poly_Ling.Tools.ObjectPose.ObjectOriginCsv に集約している。

        /// <summary>回転(°)も書き出し・読み込みの対象にするか。</summary>
        private bool IncludeRotationInCsv => _originIncludeRotToggle?.value ?? false;

        /// <summary>原点CSVの最近使ったパス（書出・読込で共有）。</summary>
        private const string OriginCsvRecentKey = "BoneEditor.OriginCsv.Path";

        /// <summary>全メッシュの原点(位置)を CSV へ書き出す。</summary>
        private void ExportObjectOriginsCsv()
        {
            var model = GetModel?.Invoke();
            if (model == null) return;

            // 書き込み先はフォルダだけを覚え、ファイル名は毎回この既定から始める。
            // 読込側の履歴（OriginCsvRecentKey）とは分ける。共有していたため、
            // 「読み込んだ原点CSVをそのまま上書きする」経路になっていた。
            string path = SaveDest.AskSavePath(
                "原点CSVの書き出し", SaveDest.Keys.OriginCsv, "",
                SanitizeFileName(model.Name) + "_origin", "csv");
            if (string.IsNullOrEmpty(path)) return;

            bool withRot = IncludeRotationInCsv;

            // ボーンは読込側（ApplyObjectOrigins）が適用対象から外すため、ここでは出さない。
            string csv = Poly_Ling.Tools.ObjectPose.ObjectOriginCsv.Build(
                model, withRot, includeBones: false, bakeRotationToPosition: false,
                out int count, out int skippedMirror, out int skippedWedge);

            try
            {
                System.IO.File.WriteAllText(path, csv, new System.Text.UTF8Encoding(true));
                Debug.Log($"[ObjectOrigin] 原点を書き出し: {count} 件 → {path}" +
                          $"（除外: ミラー {skippedMirror} 件 / 姿勢くさび {skippedWedge} 件）");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ObjectOrigin] 書き出しに失敗: {e.Message}");
            }
        }

        /// <summary>CSV から原点(位置)を読み込み、名前一致で適用する。</summary>
        private void ImportObjectOriginsCsv()
        {
            var model = GetModel?.Invoke();
            if (model == null) return;

            string path = RecentFileDialog.AskLoad("原点CSVの読み込み", OriginCsvRecentKey, "csv");
            if (string.IsNullOrEmpty(path)) return;

            string[] lines;
            try
            {
                lines = System.IO.File.ReadAllLines(path);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ObjectOrigin] 読み込みに失敗: {e.Message}");
                return;
            }

            bool withRot = IncludeRotationInCsv;

            // 解析は Poly_Ling.Tools.ObjectPose.ObjectOriginCsv に集約している。
            Poly_Ling.Tools.ObjectPose.ObjectOriginCsv.Parse(
                lines, withRot,
                out var names, out var positions, out var rotations, out int rotRows);

            if (names.Count == 0)
            {
                Debug.LogWarning("[ObjectOrigin] 有効な行がありません: " + path);
                return;
            }

            Debug.Log($"[ObjectOrigin] CSV を読み込み: {names.Count} 行" +
                      (withRot ? $"（うち回転あり {rotRows} 行）" : "（回転は対象外）"));

            ApplyObjectOriginsCommand.SplitRotations(
                withRot ? rotations.ToArray() : null,
                out var rotValues, out var hasRot);
            SendCommand(new ApplyObjectOriginsCommand(
                GetModelIndex?.Invoke() ?? 0, names.ToArray(), positions.ToArray(),
                rotValues, hasRot));

            OnRepaint?.Invoke();
        }

        // ================================================================
        // 姿勢くさびの生成・取り込み
        // ================================================================

        /// <summary>メッシュオブジェクトの姿勢をくさびオブジェクト列として生成する。</summary>
        private void GenerateObjectPoseWedges()
        {
            float length = _wedgeLengthField?.value
                ?? Poly_Ling.Tools.ObjectPose.ObjectPoseWedgeGenerator.DefaultWedgeLength;
            if (length <= 0f)
                length = Poly_Ling.Tools.ObjectPose.ObjectPoseWedgeGenerator.DefaultWedgeLength;

            SendCommand(new GenerateObjectPoseWedgesCommand(
                GetModelIndex?.Invoke() ?? 0, length,
                Poly_Ling.Tools.ObjectPose.ObjectPoseWedgeGenerator.DefaultContainerName));

            OnRepaint?.Invoke();
        }

        /// <summary>
        /// くさびオブジェクト列を姿勢として取り込む。
        /// 描画メッシュを1つだけ選んでいればそれをコンテナとみなし、
        /// そうでなければ生成時の既定名で自動検出させる。
        /// </summary>
        private void ImportObjectPoseWedges()
        {
            var model = GetModel?.Invoke();

            // 1つだけ選ばれていれば候補として渡す。的外れならディスパッチ側が
            // 名前・中身での自動検出に切り替えるので、ここでは絞り込まない。
            int container = -1;
            if (model != null && model.SelectedDrawableMeshIndices.Count == 1)
                container = model.SelectedDrawableMeshIndices[0];

            SendCommand(new ApplyObjectPoseWedgesCommand(
                GetModelIndex?.Invoke() ?? 0, container,
                Poly_Ling.Tools.ObjectPose.ObjectPoseWedgeGenerator.DefaultContainerName));

            OnRepaint?.Invoke();
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "model";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
