// PlayerBoneEditorSubPanel.Helpers.cs
// ボーンエディタ：対象選択ドロップダウン・ボタンアクション・TRS ヘルパー・UI ヘルパー。
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
        // 対象選択ドロップダウン
        // ================================================================

        /// <summary>
        /// 現在のタブに応じて選択候補を作り直し、先頭選択に合わせて表示を同期する。
        ///   ボーンタブ  : ボーン
        ///   メッシュタブ: 描画メッシュ（MirrorSide 除外）
        ///   両方タブ    : ボーン → メッシュ の順で連結
        /// 複数選択中は「(複数選択: n)」を先頭に置き、選ばれるまでコマンドを送らない。
        /// </summary>
        private void RefreshTargetDropdown(ModelContext model)
        {
            if (_boneDropdown == null) return;

            _suppressBoneDropdown = true;
            try
            {
                _targetChoiceMasters.Clear();
                _targetChoiceCategories.Clear();
                var choices = new List<string>();

                bool bothScope = _scope == SubPanelScope.Both;

                if (_scope != SubPanelScope.MeshesOnly)
                {
                    var bones = model.Bones;
                    if (bones != null)
                    {
                        foreach (var e in bones)
                        {
                            choices.Add(bothScope ? $"B: {e.Name}" : e.Name);
                            _targetChoiceMasters.Add(e.MasterIndex);
                            _targetChoiceCategories.Add(MeshCategory.Bone);
                        }
                    }
                }

                if (_scope != SubPanelScope.BonesOnly)
                {
                    var drawables = model.DrawableMeshes;
                    if (drawables != null)
                    {
                        foreach (var e in drawables)
                        {
                            if (e.Context == null || e.Context.Type == MeshType.MirrorSide) continue;
                            choices.Add(bothScope ? $"M: {e.Name}" : e.Name);
                            _targetChoiceMasters.Add(e.MasterIndex);
                            _targetChoiceCategories.Add(MeshCategory.Drawable);
                        }
                    }
                }

                // 現在の選択（GetTargetIndices と同じ集合）に合わせて表示位置を決める
                var selected = GetTargetIndices();

                if (selected.Length > 1)
                {
                    // 複数選択中はコマンドを送らせない。先頭にプレースホルダを置く。
                    choices.Insert(0, $"(複数選択: {selected.Length})");
                    _targetChoiceMasters.Insert(0, -1);
                    _targetChoiceCategories.Insert(0, MeshCategory.Bone);

                    _boneDropdown.choices = choices;
                    _boneDropdown.index   = 0;
                    return;
                }

                _boneDropdown.choices = choices;

                int dropIdx = -1;
                if (selected.Length == 1)
                    dropIdx = _targetChoiceMasters.IndexOf(selected[0]);

                _boneDropdown.index = dropIdx;
            }
            finally
            {
                _suppressBoneDropdown = false;
            }
        }

        /// <summary>ドロップダウンで対象が選ばれた時。3D ピックと同じ選択コマンドを送る。</summary>
        private void OnTargetDropdownChanged()
        {
            if (_suppressBoneDropdown) return;

            int idx = _boneDropdown?.index ?? -1;
            if (idx < 0 || idx >= _targetChoiceMasters.Count) return;

            int master = _targetChoiceMasters[idx];
            if (master < 0) return;   // 「(複数選択: n)」プレースホルダ

            SendCommand(new SelectMeshCommand(
                GetModelIndex?.Invoke() ?? 0,
                _targetChoiceCategories[idx],
                new[] { master }));

            OnRepaint?.Invoke();
        }

        private void OnMasterIndexChanged(ChangeEvent<int> evt)
        {
            if (_suppressBoneEdit) return;
            var model = GetModel?.Invoke();
            if (model == null || !model.HasBoneSelection) return;
            MoveBone(model, model.SelectedBoneIndices[0], newMaster: evt.newValue, newParentMaster: null);
        }

        private void OnParentBoneChanged(ChangeEvent<string> evt)
        {
            if (_suppressBoneEdit) return;
            var model = GetModel?.Invoke();
            if (model == null || !model.HasBoneSelection) return;
            int idx = _parentBoneDropdown.index;
            if (idx < 0 || idx >= _parentChoiceMasters.Count) return;
            MoveBone(model, model.SelectedBoneIndices[0], newMaster: null, newParentMaster: _parentChoiceMasters[idx]);
        }

        /// <summary>nodeMaster が ancestorMaster の子孫かどうか（HierarchyParentIndex を上に辿る）。</summary>
        private static bool IsDescendant(ModelContext model, int ancestorMaster, int nodeMaster)
        {
            int cur = model.GetMeshContext(nodeMaster)?.HierarchyParentIndex ?? -1;
            int guard = 0;
            while (cur >= 0 && guard++ < 4096)
            {
                if (cur == ancestorMaster) return true;
                cur = model.GetMeshContext(cur)?.HierarchyParentIndex ?? -1;
            }
            return false;
        }

        /// <summary>
        /// 対象ボーン（＋子孫サブツリー）を移動して ReorderMeshesCommand を発行する。
        /// newMaster 指定時: 親を維持しつつ親の範囲内でマスターIdx位置へ（親は超えない）。
        /// newParentMaster 指定時: 新親の子末尾へ。各パネル反映は Dispatch 側の通知に委ねる。
        /// </summary>
        private void MoveBone(ModelContext model, int target, int? newMaster, int? newParentMaster)
        {
            // 現在のボーン順（master index）と depth / parent
            var order = new List<int>();
            foreach (var e in model.Bones) order.Add(e.MasterIndex);
            int n = order.Count;
            if (n == 0) return;

            var depth  = new Dictionary<int, int>(n);
            var parent = new Dictionary<int, int>(n);
            foreach (var m in order)
            {
                var c = model.GetMeshContext(m);
                depth[m]  = c?.Depth ?? 0;
                parent[m] = c?.HierarchyParentIndex ?? -1;
            }

            int tPos = order.IndexOf(target);
            if (tPos < 0) return;
            int tDepth = depth[target];

            // 対象サブツリー範囲: tPos から subEnd の手前まで（tDepth より深い連続範囲）
            int subEnd = tPos + 1;
            while (subEnd < n && depth[order[subEnd]] > tDepth) subEnd++;
            var subtree = order.GetRange(tPos, subEnd - tPos);

            // 対象サブツリーを除いた残り
            var rest = new List<int>(order);
            rest.RemoveRange(tPos, subEnd - tPos);

            int appliedParent = parent[target];
            int appliedDepth  = tDepth;
            int insertPos;

            if (newParentMaster.HasValue)
            {
                int np = newParentMaster.Value;
                if (np == target || IsDescendant(model, target, np)) { Refresh(); return; }
                appliedParent = np;
                appliedDepth  = np >= 0
                    ? ((depth.TryGetValue(np, out var dp) ? dp : (model.GetMeshContext(np)?.Depth ?? 0)) + 1)
                    : 0;

                if (np < 0)
                {
                    insertPos = rest.Count;
                }
                else
                {
                    int npPos = rest.IndexOf(np);
                    if (npPos < 0) { Refresh(); return; }
                    int e = npPos + 1;
                    int npDepth = depth[np];
                    while (e < rest.Count && depth[rest[e]] > npDepth) e++;
                    insertPos = e;
                }
            }
            else
            {
                // マスターIdx変更: 親（維持）の範囲内にクランプ（親は超えない）
                int p = parent[target];
                int lo, hi;
                if (p < 0) { lo = 0; hi = rest.Count; }
                else
                {
                    int pPos = rest.IndexOf(p);
                    if (pPos < 0) { Refresh(); return; }
                    lo = pPos + 1;
                    int e = pPos + 1;
                    int pDepth = depth[p];
                    while (e < rest.Count && depth[rest[e]] > pDepth) e++;
                    hi = e;
                }
                // newMaster を rest 上の挿入位置に変換（newMaster 未満の master 数）
                int want = 0;
                foreach (var m in rest) { if (m < newMaster.Value) want++; else break; }
                insertPos = Mathf.Clamp(want, lo, hi);
            }

            int depthDelta = appliedDepth - tDepth;

            var newOrder = new List<int>(rest);
            newOrder.InsertRange(insertPos, subtree);

            var entries = new ReorderMeshesCommand.ReorderEntry[newOrder.Count];
            for (int i = 0; i < newOrder.Count; i++)
            {
                int m   = newOrder[i];
                int d   = depth[m];
                int par = parent[m];
                if (m == target)
                {
                    d   = appliedDepth;
                    par = appliedParent;
                }
                else if (subtree.Contains(m))
                {
                    d = depth[m] + depthDelta;   // 子孫は相対深さ維持、親は不変
                }
                entries[i] = new ReorderMeshesCommand.ReorderEntry
                {
                    MasterIndex          = m,
                    NewDepth             = d,
                    NewParentMasterIndex = par,
                };
            }

            int modelIndex = GetModelIndex?.Invoke() ?? 0;
            SendCommand(new ReorderMeshesCommand(
                modelIndex, MeshCategory.Bone, ReorderMeshesCommand.ToEntryValues(entries)));
        }

        // ================================================================
        // ボタンアクション
        // ================================================================

        private void OnResetPose()
        {
            var model = GetModel?.Invoke();
            if (model == null || !model.HasBoneSelection) return;

            var indices = new List<int>(model.SelectedBoneIndices);
            var beforeSnapshots = new Dictionary<int, BonePoseDataSnapshot>();
            var contexts = new List<(int idx, MeshContext ctx)>();

            foreach (var idx in indices)
            {
                var ctx = model.GetMeshContext(idx);
                if (ctx == null) continue;
                if (ctx.BonePoseData == null) { ctx.BonePoseData = new BonePoseData(); ctx.BonePoseData.IsActive = true; }
                beforeSnapshots[idx] = ctx.BonePoseData.CreateSnapshot();
                contexts.Add((idx, ctx));
            }
            if (contexts.Count == 0) return;

            foreach (var (_, ctx) in contexts) { ctx.BonePoseData.ClearAllLayers(); ctx.BonePoseData.SetDirty(); }

            var undo = GetUndoController?.Invoke();
            if (undo != null)
            {
                var record = new MultiBonePoseChangeRecord();
                foreach (var (idx, ctx) in contexts)
                    record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                    {
                        MasterIndex = idx,
                        OldSnapshot = beforeSnapshots.TryGetValue(idx, out var b) ? b : (BonePoseDataSnapshot?)null,
                        NewSnapshot = ctx.BonePoseData.CreateSnapshot(),
                    });
                {
                    string __dbgDesc = "ボーンポーズリセット";
                    PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                    undo.MeshListStack.Record(record, __dbgDesc);
                }
                undo.FocusMeshList();
            }

            model.OnListChanged?.Invoke();
            OnRepaint?.Invoke();
            Refresh();
        }

        private void OnFocusBone()
        {
            var model = GetModel?.Invoke();
            if (model == null || !model.HasBoneSelection) return;
            var ctx = model.GetMeshContext(model.SelectedBoneIndices[0]);
            if (ctx == null) return;
            var wm = ctx.WorldMatrix;
            OnFocusCamera?.Invoke(new Vector3(wm.m03, wm.m13, wm.m23));
        }

        // ================================================================
        // TRS ヘルパー
        // ================================================================

        private void RegTF(FloatField f, SetBoneTransformValueCommand.Field field)
        {
            f.RegisterValueChangedCallback(e =>
            {
                if (_suppressTRS) return;
                var indices = GetTargetIndices();
                if (indices.Length == 0) return;
                int modelIdx = GetModelIndex?.Invoke() ?? 0;
                SendCommand(new BeginBoneTransformSliderDragCommand(modelIdx, indices)
                {
                    Mode       = GetObjectMoveSettings?.Invoke()?.MoveMode ?? Poly_Ling.Tools.BoneMoveMode.BoneOnlyRebind,
                    OriginOnly = IsOriginOnlyActive(),
                });
                SendCommand(new SetBoneTransformValueCommand(modelIdx, indices, field, e.newValue));
                SendCommand(new EndBoneTransformSliderDragCommand(modelIdx, "TRS変更"));
                OnRepaint?.Invoke();
            });
        }

        private bool _trsDragOpen;

        private void OpenTrsDrag()
        {
            if (_trsDragOpen) return;
            var indices = GetTargetIndices();
            if (indices.Length == 0) return;
            SendCommand(new BeginBoneTransformSliderDragCommand(GetModelIndex?.Invoke() ?? 0, indices)
            {
                Mode       = GetObjectMoveSettings?.Invoke()?.MoveMode ?? Poly_Ling.Tools.BoneMoveMode.BoneOnlyRebind,
                OriginOnly = IsOriginOnlyActive(),
            });
            _trsDragOpen = true;
        }

        private void CloseTrsDrag(string desc)
        {
            if (!_trsDragOpen) return;
            _trsDragOpen = false;
            SendCommand(new EndBoneTransformSliderDragCommand(GetModelIndex?.Invoke() ?? 0, desc));
        }

        private static Vector3 NormEuler180(Vector3 e)
            => new Vector3(NormAngle180(e.x), NormAngle180(e.y), NormAngle180(e.z));

        private static float NormAngle180(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            else if (a < -180f) a += 360f;
            return a;
        }

        private void RegRotSlider(Slider s, FloatField f, SetBoneTransformValueCommand.Field field)
        {
            // UIToolkit の Slider は内部ドラッガーが bubble 段階の PointerDown を消費するため、
            // capture 段階(TrickleDown)で拾う。さらに値変更時にも遅延オープンして確実に Begin を送る。
            s.RegisterCallback<PointerDownEvent>(_ => OpenTrsDrag(), TrickleDown.TrickleDown);
            s.RegisterCallback<PointerUpEvent>(_ => CloseTrsDrag("回転変更"), TrickleDown.TrickleDown);
            s.RegisterCallback<PointerCaptureOutEvent>(_ => CloseTrsDrag("回転変更"));
            s.RegisterValueChangedCallback(e =>
            {
                if (_suppressTRS) return;
                var indices = GetTargetIndices();
                if (indices.Length == 0) return;
                OpenTrsDrag();
                SendCommand(new SetBoneTransformValueCommand(GetModelIndex?.Invoke() ?? 0, indices, field, e.newValue));
                if (f != null) { _suppressTRS = true; SF(f, e.newValue); _suppressTRS = false; }
                OnRepaint?.Invoke();
            });
        }

        // ================================================================
        // UI ヘルパー
        // ================================================================

        /// <summary>
        /// 「原点だけ移動」が実際に効いている状態か。
        /// 「オブジェクト姿勢」スコープ限定（ボーンタブでは常に false）。
        /// </summary>
        private bool IsOriginOnlyActive()
            => (_scope == SubPanelScope.MeshesOnly)
               && (GetObjectMoveSettings?.Invoke()?.OriginOnly ?? false);

        /// <summary>
        /// OriginOnly(原点だけ移動)時はスケール区画を隠す。
        /// 回転は自頂点の再ローカル化で補償されるため表示する。
        /// スケールは補償経路が無いため引き続き隠す。
        /// </summary>
        private void ApplyOriginOnlyVisibility()
        {
            bool originOnly = IsOriginOnlyActive();
            if (_rotSection != null) _rotSection.style.display = DisplayStyle.Flex;
            if (_sclSection != null)
                _sclSection.style.display = originOnly ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void SetTRSEnabled(bool enabled)
        {
            _posX?.SetEnabled(enabled); _posY?.SetEnabled(enabled); _posZ?.SetEnabled(enabled);
            _rotX?.SetEnabled(enabled); _rotY?.SetEnabled(enabled); _rotZ?.SetEnabled(enabled);
            _rotSliderX?.SetEnabled(enabled); _rotSliderY?.SetEnabled(enabled); _rotSliderZ?.SetEnabled(enabled);
            _sclX?.SetEnabled(enabled); _sclY?.SetEnabled(enabled); _sclZ?.SetEnabled(enabled);
            _ignorePoseToggle?.SetEnabled(enabled);
        }

        private void SetWarning(string text)
        {
            if (_warningLabel == null) return;
            _warningLabel.text          = text;
            _warningLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private string ScopeEmptyMessage() => _scope switch
        {
            SubPanelScope.BonesOnly  => "未選択 — ボーンを選択してください",
            SubPanelScope.MeshesOnly => "未選択 — 描画メッシュを選択してください",
            _                        => "未選択 — オブジェクトを選択してください",
        };

        private string StatusText(ModelContext model) => _scope switch
        {
            SubPanelScope.BonesOnly  => $"Bones: {model.BoneCount}  Selected: {model.SelectedBoneIndices.Count}",
            SubPanelScope.MeshesOnly => $"Meshes: {model.DrawableCount}  Selected: {model.SelectedDrawableMeshIndices.Count}",
            _                        => $"Bones: {model.BoneCount}  Meshes: {model.DrawableCount}",
        };

        private static void AddXYZFields(VisualElement parent, string prefix,
            out FloatField fx, out FloatField fy, out FloatField fz)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            fx = MakeFloatField(prefix + "-x", "X"); row.Add(fx);
            fy = MakeFloatField(prefix + "-y", "Y"); row.Add(fy);
            fz = MakeFloatField(prefix + "-z", "Z"); row.Add(fz);
            parent.Add(row);
        }

        private static FloatField MakeFloatField(string name, string label)
        {
            var f = new FloatField(label) { name = name };
            f.style.flexGrow    = 1;
            f.style.marginRight = 2;
            return f;
        }

        private static Slider MakeRotSlider()
        {
            var s = new Slider(-180f, 180f);
            s.style.marginBottom = 2;
            return s;
        }

        private static void SF(FloatField f, float v)
        { if (f != null) f.SetValueWithoutNotify((float)Math.Round(v, 4)); }

        private static void SS(Slider s, float v)
        { if (s != null) s.SetValueWithoutNotify(Mathf.Clamp(v, -180f, 180f)); }

        private static Button MakeScopeTab(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.height        = 22;
            b.style.fontSize      = 10;
            b.style.paddingTop    = 0;
            b.style.paddingBottom = 0;
            b.style.marginRight   = 1;
            return b;
        }

        private static Button MakeSmallBtn(string text)
        {
            var b = new Button { text = text };
            b.style.fontSize      = 9;
            b.style.height        = 20;
            b.style.paddingTop    = 0;
            b.style.paddingBottom = 0;
            b.style.marginBottom  = 2;
            return b;
        }

        private static void AddRow(VisualElement parent, string labelText, out Label valueLabel)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var key = new Label(labelText + ": ");
            key.style.width    = 76;
            key.style.color    = new StyleColor(Color.white);
            key.style.fontSize = 10;
            var val = new Label();
            val.style.color    = new StyleColor(Color.white);
            val.style.flexGrow = 1;
            val.style.fontSize = 10;
            row.Add(key); row.Add(val);
            parent.Add(row);
            valueLabel = val;
        }

        private void AddIntRow(VisualElement parent, string labelText, out IntegerField field,
                               EventCallback<ChangeEvent<int>> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var key = new Label(labelText + ": ");
            key.style.width    = 76;
            key.style.color    = new StyleColor(Color.white);
            key.style.fontSize = 10;
            var f = new IntegerField();
            f.style.flexGrow = 1;
            f.style.fontSize = 10;
            f.RegisterValueChangedCallback(onChange);
            row.Add(key); row.Add(f);
            parent.Add(row);
            field = f;
        }

        private void AddDropdownRow(VisualElement parent, string labelText, out DropdownField field,
                                    EventCallback<ChangeEvent<string>> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var key = new Label(labelText + ": ");
            key.style.width    = 76;
            key.style.color    = new StyleColor(Color.white);
            key.style.fontSize = 10;
            var d = new DropdownField();
            d.style.flexGrow = 1;
            d.style.fontSize = 10;
            d.RegisterValueChangedCallback(onChange);
            row.Add(key); row.Add(d);
            parent.Add(row);
            field = d;
        }

        private static Label MakeSecLabel(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement MakeSep()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 3;
            v.style.marginBottom    = 3;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }
    }
}
