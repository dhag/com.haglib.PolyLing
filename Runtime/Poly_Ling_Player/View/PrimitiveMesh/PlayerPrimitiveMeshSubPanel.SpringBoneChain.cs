// PlayerPrimitiveMeshSubPanel.SpringBoneChain.cs
// 揺れもの用のボーン鎖（1 本 / 円筒 / 回転体）の UI とコマンド組み立て。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【他の図形との違い】
//   出来るのはボーンで、メッシュではない。したがって Placement（配置と後処理）
//   も PivotOffset も使わない。取り付け先のボーンを選び、その子として置く。
//
// 【プロファイル】
//   1 本 / 回転体は「回転体」と同じ折れ線を使う。編集 UI は
//   BuildRevolutionUI が組む _revProfile とプロファイルエディタを
//   そのまま共有する。X が取り付け先からの水平距離、Y が高さ（下が負）。
//   円筒は半径と高さから折れ線を組むので、エディタは出さない。
//
// 【段ごとの値】
//   かたさ・減衰・重力・当たり半径は「根元」と「先」を入れ、
//   段の位置に合わせて配る。配り方は SpringBoneTaper が決める
//   （まっすぐ／根元を長く／先を長く／中ほどで一気に）。
//   段ごとに個別に直したいときは、生成時に作る段ごとの名前付きセットを
//   「揺れもの編集」パネルから呼び出して掛け直す。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Revolution;
using Poly_Ling.Tools.SpringBoneRig;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        /// <summary>現在のモデル。揺れもの用ボーン鎖だけが使う。</summary>
        public Func<ModelContext> GetModelContext;

        private int    _sbAttachIndex = -1;
        private string _sbPrefix      = "Skirt";

        private int   _sbChainCount   = 12;
        private int   _sbSegments     = 5;
        private float _sbTopRadius    = 0.12f;
        private float _sbBottomRadius = 0.45f;
        private float _sbHeight       = 0.60f;
        private float _sbStartAngle   = 0f;

        private bool  _sbAddTail    = true;
        private float _sbTailLength = 0.05f;

        private bool  _sbApplySpring = true;
        private float _sbStiffTop = 1.0f, _sbStiffTip = 1.0f;
        private float _sbDragTop  = 0.4f, _sbDragTip  = 0.4f;
        private float _sbGravTop  = 0f,   _sbGravTip  = 0f;
        private float _sbHitRadius = 0.02f, _sbHitRadiusTip = 0.02f;

        /// <summary>
        /// 根元から先への配り方。0=まっすぐ / 1=根元を長く残す /
        /// 2=先を長く効かせる / 3=中ほどで一気に変える。
        /// 対応は SpringBoneTaper が持つ。
        /// </summary>
        private int _sbTaperShape = 0;

        private string _sbGroupIndices = "";

        private bool _sbMakeSets = true;

        [UiControl("springBoneChain.attach", Description = "鎖の取り付け先")]
        private DropdownField _sbAttachField;
        private readonly List<int> _sbAttachMasters = new List<int>();
        [UiControl("springBoneChain.preview", Safety = UiSafety.ReadOnly, Description = "作られる鎖とボーンの見込み")]
        private Label _sbPreview;

        // ================================================================
        // UI
        // ================================================================

        private void BuildSpringBoneChainUI(VisualElement c)
        {
            var kind = _current;

            c.Add(ShapeTitle(T(ShapeKeys[(int)kind])));

            c.Add(SL("作るのはボーンです。メッシュとウェイトは作りません。"));

            // ── 取り付け先 ────────────────────────────────────────────
            BuildSpringBoneAttachRow(c);

            // ── 形ごとの寸法 ──────────────────────────────────────────
            if (kind != ShapeKind.SpringBoneSingle)
            {
                c.Add(IR(T("SBChainCount"), 1, 64,
                    () => _sbChainCount, v => { _sbChainCount = v; UpdateSpringBonePreview(); }));
                c.Add(SR(T("SBStartAngle"), -180f, 180f,
                    () => _sbStartAngle, v => _sbStartAngle = v));
            }

            if (kind == ShapeKind.SpringBoneCylinder)
            {
                c.Add(IR(T("SBSegments"), 1, 32,
                    () => _sbSegments, v => { _sbSegments = v; UpdateSpringBonePreview(); }));
                c.Add(SR(T("SBTopRadius"), 0f, 2f,
                    () => _sbTopRadius, v => _sbTopRadius = v));
                c.Add(SR(T("SBBottomRadius"), 0f, 2f,
                    () => _sbBottomRadius, v => _sbBottomRadius = v));
                c.Add(SR(T("SBHeight"), 0.05f, 3f,
                    () => _sbHeight, v => _sbHeight = v));
                c.Add(SL("上端と下端の半径を変えると円錐になります。蓋はありません（ボーンなので面がありません）。"));
            }

            // ── 鎖の先・揺れ方・段ごとのセット ────────────────────────
            BuildSpringBoneMotionUI(c);

            // ── 折れ線（1 本 / 回転体）────────────────────────────────
            if (kind != ShapeKind.SpringBoneCylinder)
            {
                if (_revProfile == null)
                    _revProfile = RevolutionProfileGenerator.CreateDefault();

                c.Add(SL("折れ線は下のプロファイルエディタで編集します。"
                       + "X が取り付け先からの水平距離、Y が高さ（下が負）です。"));

                BuildRevolutionProfileEditor(_profileEditorContainer);
            }

            // ── 実行 ──────────────────────────────────────────────────
            _sbPreview = new Label();
            _sbPreview.style.fontSize = 10;
            _sbPreview.style.whiteSpace = WhiteSpace.Normal;
            _sbPreview.style.marginTop = 4;
            c.Add(_sbPreview);

            // 生成はパネル下端の共通「生成」ボタンから行う。
            // ここに専用ボタンを置くと押す場所が 2 つになる。
            UpdateSpringBonePreview();
        }

        /// <summary>
        /// 取り付け先の一覧を作り直す。ボーンと、まだボーンでない描画メッシュを並べる。
        ///
        /// 【メッシュも並べる理由】
        ///   MeshFilter 系のモデルにはボーンが 1 本も無い。はしごから揺れボーンを
        ///   先に作る手順では、取り付け先に「これからボーンになるメッシュ」を
        ///   指定できる必要がある。
        ///   スキンド化のとき、そのメッシュのボーンへ自動で付け替わる
        ///   （MeshFilterToSkinnedConverter の Phase 4a）。
        /// </summary>
        private void RefreshSpringBoneAttach()
        {
            if (_sbAttachField == null) return;

            var model = GetModelContext?.Invoke();
            _sbAttachMasters.Clear();

            var choices = new List<string> { "（モデル直下）" };
            _sbAttachMasters.Add(-1);

            if (model != null)
            {
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.Bone) continue;
                    _sbAttachMasters.Add(i);
                    choices.Add($"[{i}] {mc.Name}");
                }

                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.Mesh) continue;
                    if (mc.MeshObject == null) continue;
                    _sbAttachMasters.Add(i);
                    choices.Add($"[{i}] {mc.Name}（スキンド化でボーンになる）");
                }
            }

            _sbAttachField.choices = choices;

            int keep = _sbAttachMasters.IndexOf(_sbAttachIndex);
            _sbAttachField.SetValueWithoutNotify(choices[keep >= 0 ? keep : 0]);
            if (keep < 0) _sbAttachIndex = -1;
        }

        private void UpdateSpringBonePreview()
        {
            if (_sbPreview == null) return;

            var kind = _current;
            int chains = kind == ShapeKind.SpringBoneSingle ? 1 : Mathf.Max(1, _sbChainCount);

            int perChain = kind == ShapeKind.SpringBoneCylinder
                ? Mathf.Max(1, _sbSegments) + 1
                : Mathf.Max(2, _revProfile?.Count ?? 2);

            if (_sbAddTail) perChain += 1;

            string p = string.IsNullOrEmpty(_sbPrefix) ? "Spring" : _sbPrefix;
            string first = chains > 1 ? $"{p}_00_top" : $"{p}_top";

            _sbPreview.text =
                $"鎖 {chains} 本 × 1 本 {perChain} 本 ＝ ボーン {chains * perChain} 本。最初の 1 本は「{first}」。";
        }

        // ================================================================
        // 生成
        // ================================================================

        private void GenerateSpringBoneChains()
        {
            var model = GetModelContext?.Invoke();
            if (model == null) { SetSpringBoneStatus("モデルがありません。"); return; }

            var kind = _current;
            string prefix = string.IsNullOrEmpty(_sbPrefix) ? "Spring" : _sbPrefix.Trim();
            int mi = ModelIndex();

            var layout = kind switch
            {
                ShapeKind.SpringBoneSingle     => SpringBoneChainLayout.Single,
                ShapeKind.SpringBoneRevolution => SpringBoneChainLayout.Revolution,
                _                              => SpringBoneChainLayout.Cylinder,
            };

            Vector2[] profile = null;
            if (layout != SpringBoneChainLayout.Cylinder)
            {
                if (_revProfile == null || _revProfile.Count < 2)
                {
                    SetSpringBoneStatus("折れ線の点が 2 個未満です。プロファイルエディタで点を足してください。");
                    return;
                }
                profile = _revProfile.ToArray();
            }

            // 1. ボーンを作る
            SendCommand(new PlaceSpringBoneChainsCommand(
                mi, layout, _sbAttachIndex, prefix,
                _sbAttachIndex,     // 位置の基準は親と同じボーン
                Mathf.Max(1, _sbChainCount), Mathf.Max(1, _sbSegments),
                _sbTopRadius, _sbBottomRadius, _sbHeight, _sbStartAngle,
                profile, _sbAddTail, _sbTailLength));

            // 作ったボーンは名前で引き直す。
            var chains = SpringBoneChainPlacer.CollectByPrefix(GetModelContext?.Invoke(), prefix);
            if (chains.Count == 0)
            {
                SetSpringBoneStatus("ボーンが作られませんでした。取り付け先と値を確認してください。");
                return;
            }

            ApplySpringToChains(mi, chains, prefix);
            RefreshSpringBoneAttach();
        }

        /// <summary>
        /// 作った鎖へ揺れ方・鎖の先頭・段ごとの名前付きセットを掛ける。
        /// 数値から作る経路（GenerateSpringBoneChains）と、はしごから作る経路
        /// （GenerateSpringBoneLadderChains）で同じものを使う。
        /// </summary>
        private void ApplySpringToChains(int mi, List<List<int>> chains, string prefix)
        {
            if (chains == null || chains.Count == 0) return;

            int maxLen = 0;
            foreach (var ch in chains) maxLen = Mathf.Max(maxLen, ch.Count);

            if (_sbApplySpring)
            {
                // 2. 段ごとに揺れ方を入れる。
                //    根元→先の配り方は SpringBoneTaper が決める。
                //    「揺れもの編集」の同じ機能と同じものを使うこと。
                var stiff = MakeSpringBoneTaper(_sbStiffTop,   _sbStiffTip);
                var grav  = MakeSpringBoneTaper(_sbGravTop,    _sbGravTip);
                var drag  = MakeSpringBoneTaper(_sbDragTop,    _sbDragTip);
                var hit   = MakeSpringBoneTaper(_sbHitRadius,  _sbHitRadiusTip);

                for (int step = 0; step < maxLen; step++)
                {
                    var atStep = new List<int>();
                    foreach (var ch in chains) if (step < ch.Count) atStep.Add(ch[step]);
                    if (atStep.Count == 0) continue;

                    SendCommand(new SetSpringBoneJointCommand(
                        mi, atStep.ToArray(),
                        hit.EvaluateStep(step, maxLen),
                        stiff.EvaluateStep(step, maxLen),
                        grav.EvaluateStep(step, maxLen),
                        new Vector3(0f, -1f, 0f),
                        drag.EvaluateStep(step, maxLen)));
                }

                // 3. 各鎖の先頭を指定する
                int[] groupIdx = ParseSpringBoneGroups(_sbGroupIndices);
                for (int i = 0; i < chains.Count; i++)
                {
                    string chainName = chains.Count > 1 ? $"{prefix}_{i:00}" : prefix;
                    SendCommand(new SetSpringBoneChainRootCommand(mi, chains[i][0], chainName, "", groupIdx));
                }
            }

            // 4. 段ごとの名前付きセット
            if (_sbMakeSets)
            {
                var m2 = GetModelContext?.Invoke();
                for (int step = 0; step < maxLen; step++)
                {
                    var names = new List<string>();
                    foreach (var ch in chains)
                        if (step < ch.Count)
                        {
                            var mc = m2?.GetMeshContext(ch[step]);
                            if (mc != null && !string.IsNullOrEmpty(mc.Name)) names.Add(mc.Name);
                        }

                    if (names.Count == 0) continue;

                    SendCommand(new SaveSelectionDictionaryCommand(
                        mi, MeshCategory.Bone, $"{prefix}_段{step}", names.ToArray()));
                }
            }

            int total = 0;
            foreach (var ch in chains) total += ch.Count;
            SetSpringBoneStatus($"鎖 {chains.Count} 本 / ボーン {total} 本を作りました。"
                              + (_sbApplySpring ? " 揺れ方と鎖の先頭も設定しました。" : ""));
        }

        /// <summary>
        /// 取り付け先と名前の接頭辞。数値から作る経路と、はしごから作る経路で共用する。
        /// </summary>
        private void BuildSpringBoneAttachRow(VisualElement c)
        {
            _sbAttachField = new DropdownField(T("SBAttach"));
            _sbAttachField.RegisterValueChangedCallback(_ =>
            {
                int i = _sbAttachField.index;
                _sbAttachIndex = (i >= 0 && i < _sbAttachMasters.Count) ? _sbAttachMasters[i] : -1;
            });
            c.Add(_sbAttachField);
            RefreshSpringBoneAttach();

            var prefix = new TextField(T("SBPrefix")) { value = _sbPrefix };
            prefix.RegisterValueChangedCallback(e => { _sbPrefix = e.newValue; UpdateSpringBonePreview(); });
            c.Add(prefix);
        }

        /// <summary>
        /// 鎖の先・揺れ方・配り方・段ごとのセット。
        /// 数値から作る経路と、はしごから作る経路で共用する。
        /// </summary>
        private void BuildSpringBoneMotionUI(VisualElement c)
        {
            // ── 鎖の先 ────────────────────────────────────────────────
            c.Add(TR(T("SBAddTail"), () => _sbAddTail, v => { _sbAddTail = v; UpdateSpringBonePreview(); }));
            c.Add(SR(T("SBTailLength"), 0.01f, 0.3f, () => _sbTailLength, v => _sbTailLength = v));
            c.Add(SL("鎖のいちばん先のボーンは、1 つ手前の向きを決めるためだけに使われ、自分は揺れません。"));

            // ── 揺れ方 ────────────────────────────────────────────────
            c.Add(TR(T("SBApplySpring"), () => _sbApplySpring, v => _sbApplySpring = v));
            c.Add(SR(T("SBStiffTop"), 0f, 4f, () => _sbStiffTop, v => _sbStiffTop = v));
            c.Add(SR(T("SBStiffTip"), 0f, 4f, () => _sbStiffTip, v => _sbStiffTip = v));
            c.Add(SR(T("SBDragTop"),  0f, 1f, () => _sbDragTop,  v => _sbDragTop  = v));
            c.Add(SR(T("SBDragTip"),  0f, 1f, () => _sbDragTip,  v => _sbDragTip  = v));
            c.Add(SR(T("SBGravTop"),  0f, 2f, () => _sbGravTop,  v => _sbGravTop  = v));
            c.Add(SR(T("SBGravTip"),  0f, 2f, () => _sbGravTip,  v => _sbGravTip  = v));
            c.Add(SR(T("SBHitRadius"),    0f, 0.5f, () => _sbHitRadius,    v => _sbHitRadius    = v));
            c.Add(SR(T("SBHitRadiusTip"), 0f, 0.5f, () => _sbHitRadiusTip, v => _sbHitRadiusTip = v));

            // 配り方。根元 0、先 1 として途中をどう配るかを選ぶ。
            // 中身は SpringBoneTaper（Gamma と折れ線）。
            var taperDd = new DropdownField(
                T("SBTaperShape"),
                new List<string>
                {
                    "まっすぐ変える",
                    "根元の値を長く残す",
                    "先の値を長く効かせる",
                    "中ほどで一気に変える",
                },
                Mathf.Clamp(_sbTaperShape, 0, 3));
            taperDd.RegisterValueChangedCallback(_ => _sbTaperShape = taperDd.index);
            c.Add(taperDd);

            var groups = new TextField(T("SBGroups")) { value = _sbGroupIndices };
            groups.RegisterValueChangedCallback(e => _sbGroupIndices = e.newValue);
            c.Add(groups);
            c.Add(SL("根元と先で違う値を入れると、段の位置に合わせて少しずつ変えて配ります。"
                   + "同じ値なら全段同じになります。"));

            c.Add(TR(T("SBMakeSets"), () => _sbMakeSets, v => _sbMakeSets = v));
        }

        /// <summary>
        /// 画面の指定から配り方を作る。番号の対応は _sbTaperShape の説明のとおり。
        /// </summary>
        private SpringBoneTaper MakeSpringBoneTaper(float root, float tip)
        {
            var taper = new SpringBoneTaper(root, tip);

            switch (_sbTaperShape)
            {
                case 1:  taper.Gamma = SpringBoneTaper.GammaHoldRoot; break;
                case 2:  taper.Gamma = SpringBoneTaper.GammaHoldTip;  break;
                case 3:  taper.Curve = SpringBoneTaper.SCurve();      break;
                default: taper.Gamma = SpringBoneTaper.GammaStraight; break;
            }
            return taper;
        }

        private static int[] ParseSpringBoneGroups(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<int>();

            var parts = text.Split(',');
            var list = new List<int>(parts.Length);
            foreach (string p in parts)
                if (int.TryParse(p.Trim(), out int v) && v >= 0 && !list.Contains(v)) list.Add(v);
            return list.ToArray();
        }

        /// <summary>状態はパネル共通の表示欄へ出す。専用の欄は持たない。</summary>
        private void SetSpringBoneStatus(string s)
        {
            if (_statusLabel != null) _statusLabel.text = s;
        }
    }
}
