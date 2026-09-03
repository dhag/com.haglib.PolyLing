// PlayerThinPlateMorphSubPanel.cs
// TPSモーフ サブパネル（Player ビルド用）。
// ビフォー／アフター2オブジェクトの頂点対応から 3D Thin Plate Spline を解き、
// ターゲットオブジェクトを変形した結果を新規オブジェクトとして作る。
// Runtime/Poly_Ling_Player/View/SubPanels/ThinPlateMorph/ に配置
//
// 【設計】
// 係数算出は制御点数の3乗オーダーでプレビューに向かないため、プレビューは持たない。
// 「実行」で計算と確定をまとめて行い、結果は新規オブジェクトとして追加する。
// ターゲットは変更しないので、失敗しても元の形状は残る。
//
// 【全域モードと局所モード】
// 全域モード（ThinPlateLocalMode.Global）は従来どおり、全制御点で 1 度だけ
// 係数を求める。ApplyThinPlateMorphCommand をそのまま送って同期実行する。
//
// 局所モードはターゲット頂点ごとに独立に係数を求めるため、1 頂点ごとに
// LU 分解が走る。ターゲット頂点数が数千を超えると数十秒から数分かかり、
// 同期実行すると中止ボタンを押すこともできなくなる。そのため
//   1. メインスレッドで入力を配列へ写す（ThinPlateMorphOperation.BuildLocalInput）
//   2. バックグラウンドスレッドで解く（LocalThinPlateMorphSolver.Solve）
//   3. 完了後、メインスレッドで結果を書き戻す（ApplyThinPlateMorphResultCommand）
// の 3 段に分ける。中止した場合は新規オブジェクトを作らない。
// ターゲットは元々変更しないので、中止しても状態は無傷。
//
// 【前提】
// スキニング無し。空間変換はオブジェクト単位の WorldMatrix だけを使う
// （ThinPlateMorphOperation の冒頭コメントを参照）。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Jobs;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    public class PlayerThinPlateMorphSubPanel
    {
        // ================================================================
        // レンジ（上下限）
        //
        // 実体は ParameterLimits（persistentDataPath の CSV）にあり、ここでは
        // キーを引くだけにする。同じキーを PanelCommand の PLParam(LimitKey) が
        // 指すので、UI とスキーマで範囲の定義が1箇所になる。
        // ================================================================

        private static float LambdaMin => ParameterLimits.GetF("ThinPlateMorph.Lambda.Min");

        // ================================================================
        // コールバック（Viewer から設定）
        // ================================================================

        /// <summary>再描画要求。</summary>
        public Action OnRepaint;

        // コマンド送信
        private PanelContext _panelContext;
        private Func<int> _getModelIndex;

        public void SetCommandContext(PanelContext ctx, Func<int> getModelIndex)
        {
            _panelContext  = ctx;
            _getModelIndex = getModelIndex;
        }

        // ================================================================
        // 内部状態
        // ================================================================

        private ModelContext _model;

        private int _beforeIndex = -1;
        private int _afterIndex  = -1;
        private int _targetIndex = -1;

        private float _lambda         = ThinPlateMorphOperation.DefaultLambda;
        private bool  _selectedOnly   = false;
        private bool  _recalcNormals  = true;

        private ThinPlateLocalMode _mode          = ThinPlateLocalMode.Global;
        private int                _neighborCount = ThinPlateMorphOperation.LocalDefaultNeighborCount;
        private float              _radius        = 0.05f;
        private int                _localCap      = ThinPlateMorphOperation.LocalMaxControlPointCount;

        // 実行中のバックグラウンドジョブ。null なら実行していない
        private PLJobHandle<LocalMorphResult> _job;
        private PLJobMonitor                  _jobMonitor;
        private int                           _jobTargetIndex = -1;
        private bool                          _jobRecalcNormals = true;

        private readonly List<(int index, string name, int vertexCount)> _candidates
            = new List<(int, string, int)>();

        private static readonly (ThinPlateLocalMode mode, string label)[] ModeChoices =
        {
            (ThinPlateLocalMode.Global,          "全域（従来）: 全制御点で1度だけ解く"),
            (ThinPlateLocalMode.EuclideanCount,  "局所A: 直線距離で近い順に N 個"),
            (ThinPlateLocalMode.LinkCount,       "局所B: 最近傍からリンク距離で近い順に N 個"),
            (ThinPlateLocalMode.EuclideanRadius, "局所C: 直線距離 L 以下"),
            (ThinPlateLocalMode.LinkRadius,      "局所D: 最近傍からリンク距離 L 以下"),
        };

        private static bool IsLocal(ThinPlateLocalMode mode)
            => mode != ThinPlateLocalMode.Global;

        private static bool IsLinkMode(ThinPlateLocalMode mode)
            => mode == ThinPlateLocalMode.LinkCount || mode == ThinPlateLocalMode.LinkRadius;

        private static bool IsRadiusMode(ThinPlateLocalMode mode)
            => mode == ThinPlateLocalMode.EuclideanRadius || mode == ThinPlateLocalMode.LinkRadius;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _warningLabel;
        private VisualElement _mainContent;
        private VisualElement _beforeListContainer;
        private VisualElement _afterListContainer;
        private VisualElement _targetListContainer;
        private Toggle        _toggleSelectedOnly;
        private VisualElement _modeListContainer;
        private IntegerField  _fieldNeighborCount;
        private FloatField    _fieldRadius;
        private IntegerField  _fieldLocalCap;
        private FloatField    _fieldLambda;
        private Toggle        _toggleRecalc;
        private Label         _infoLabel;
        private Button        _btnApply;
        private Label         _statusLabel;

        private VisualElement _progressRow;
        private VisualElement _progressTrack;
        private VisualElement _progressFill;
        private Label         _progressLabel;
        private Button        _btnCancel;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingTop    = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            _warningLabel.style.marginBottom = 4;
            _root.Add(_warningLabel);

            _mainContent = new VisualElement();
            _root.Add(_mainContent);

            // ── ビフォー
            _mainContent.Add(SecLabel("ビフォー（変形前の対応点）"));
            _beforeListContainer = new VisualElement();
            _beforeListContainer.style.marginBottom = 4;
            _mainContent.Add(_beforeListContainer);

            // ── アフター
            _mainContent.Add(SecLabel("アフター（変形後の対応点／ビフォーと同頂点数）"));
            _afterListContainer = new VisualElement();
            _afterListContainer.style.marginBottom = 4;
            _mainContent.Add(_afterListContainer);

            _mainContent.Add(Sep());

            // ── ターゲット
            _mainContent.Add(SecLabel("ターゲット（変形させる／結果は新規オブジェクト）"));
            _targetListContainer = new VisualElement();
            _targetListContainer.style.marginBottom = 4;
            _mainContent.Add(_targetListContainer);

            _mainContent.Add(Sep());

            // ── 制御点
            _mainContent.Add(SecLabel("制御点"));

            _toggleSelectedOnly = new Toggle("選択頂点のみを制御点にする") { value = _selectedOnly };
            _toggleSelectedOnly.style.fontSize    = 10;
            _toggleSelectedOnly.style.marginBottom = 2;
            _toggleSelectedOnly.RegisterValueChangedCallback(e =>
            {
                _selectedOnly = e.newValue;
                RefreshInfo();
            });
            _mainContent.Add(_toggleSelectedOnly);

            _infoLabel = new Label();
            _infoLabel.style.fontSize    = 9;
            _infoLabel.style.whiteSpace  = WhiteSpace.Normal;
            _infoLabel.style.marginBottom = 4;
            _mainContent.Add(_infoLabel);

            _mainContent.Add(Sep());

            // ── 制御点の選び方
            _mainContent.Add(SecLabel("制御点の選び方"));

            _modeListContainer = new VisualElement();
            _modeListContainer.style.marginBottom = 4;
            _mainContent.Add(_modeListContainer);

            // ── 局所モードのパラメータ
            _fieldNeighborCount = new IntegerField("N（近傍数）") { value = _neighborCount };
            _fieldNeighborCount.style.fontSize     = 10;
            _fieldNeighborCount.style.marginBottom = 2;
            _fieldNeighborCount.RegisterValueChangedCallback(e =>
            {
                int v = e.newValue;
                if (v < 1) v = 1;
                if (v > ThinPlateMorphOperation.LocalMaxControlPointCount)
                    v = ThinPlateMorphOperation.LocalMaxControlPointCount;
                if (v != e.newValue) _fieldNeighborCount.SetValueWithoutNotify(v);
                _neighborCount = v;
                RefreshInfo();
            });
            _mainContent.Add(_fieldNeighborCount);

            _fieldRadius = new FloatField("L（距離しきい値）") { value = _radius };
            _fieldRadius.style.fontSize     = 10;
            _fieldRadius.style.marginBottom = 2;
            _fieldRadius.RegisterValueChangedCallback(e =>
            {
                float v = e.newValue;
                if (v < 0f) { v = 0f; _fieldRadius.SetValueWithoutNotify(v); }
                _radius = v;
                RefreshInfo();
            });
            _mainContent.Add(_fieldRadius);

            _fieldLocalCap = new IntegerField("制御点数の上限") { value = _localCap };
            _fieldLocalCap.style.fontSize     = 10;
            _fieldLocalCap.style.marginBottom = 2;
            _fieldLocalCap.RegisterValueChangedCallback(e =>
            {
                int v = e.newValue;
                if (v < ThinPlateMorphOperation.MinControlPointCount)
                    v = ThinPlateMorphOperation.MinControlPointCount;
                if (v > ThinPlateMorphOperation.LocalMaxControlPointCount)
                    v = ThinPlateMorphOperation.LocalMaxControlPointCount;
                if (v != e.newValue) _fieldLocalCap.SetValueWithoutNotify(v);
                _localCap = v;
                RefreshInfo();
            });
            _mainContent.Add(_fieldLocalCap);

            // ── パラメータ
            _mainContent.Add(SecLabel("パラメータ"));

            // λ は 0 から 1 近くまで桁をまたいで使うため、スライダーではなく数値入力にする。
            _fieldLambda = new FloatField("λ（平滑化）") { value = _lambda };
            _fieldLambda.style.fontSize    = 10;
            _fieldLambda.style.marginBottom = 2;
            _fieldLambda.RegisterValueChangedCallback(e =>
            {
                float v = e.newValue;
                if (v < LambdaMin)
                {
                    v = LambdaMin;
                    _fieldLambda.SetValueWithoutNotify(v);
                }
                _lambda = v;
            });
            _mainContent.Add(_fieldLambda);

            _toggleRecalc = new Toggle("法線を再計算") { value = _recalcNormals };
            _toggleRecalc.style.fontSize     = 10;
            _toggleRecalc.style.marginBottom = 4;
            _toggleRecalc.RegisterValueChangedCallback(e => _recalcNormals = e.newValue);
            _mainContent.Add(_toggleRecalc);

            _mainContent.Add(Sep());

            _btnApply = new Button(OnApplyClicked) { text = "実行" };
            _btnApply.style.height       = 24;
            _btnApply.style.fontSize     = 10;
            _btnApply.style.marginBottom = 4;
            _mainContent.Add(_btnApply);

            // ── 進捗と中止（局所モードの実行中だけ出す）
            _progressRow = new VisualElement();
            _progressRow.style.display      = DisplayStyle.None;
            _progressRow.style.marginBottom = 4;
            _mainContent.Add(_progressRow);

            _progressLabel = new Label();
            _progressLabel.style.fontSize   = 9;
            _progressLabel.style.whiteSpace = WhiteSpace.Normal;
            _progressLabel.style.color      = new StyleColor(new Color(0.8f, 0.85f, 1f));
            _progressRow.Add(_progressLabel);

            // ProgressBar は使わず、塗りつぶし用の要素を 2 枚重ねて自前で描く。
            _progressTrack = new VisualElement();
            _progressTrack.style.height          = 6;
            _progressTrack.style.marginTop       = 2;
            _progressTrack.style.marginBottom    = 4;
            _progressTrack.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.10f));
            _progressRow.Add(_progressTrack);

            _progressFill = new VisualElement();
            _progressFill.style.height          = 6;
            _progressFill.style.width           = new StyleLength(Length.Percent(0f));
            _progressFill.style.backgroundColor = new StyleColor(new Color(0.24f, 0.62f, 0.95f));
            _progressTrack.Add(_progressFill);

            _btnCancel = new Button(OnCancelClicked) { text = "中止" };
            _btnCancel.style.height   = 22;
            _btnCancel.style.fontSize = 10;
            _progressRow.Add(_btnCancel);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color      = new StyleColor(new Color(0.4f, 0.8f, 1f));
            _mainContent.Add(_statusLabel);
        }

        // ================================================================
        // モデル更新（Viewer から呼ぶ）
        // ================================================================

        public void SetModel(ModelContext model)
        {
            // 同じモデルで呼び直された場合（パネルを開き直したときなど）は、
            // 計算中のジョブを巻き添えにしないよう表示更新だけで済ませる。
            if (ReferenceEquals(_model, model))
            {
                Refresh();
                return;
            }

            // モデルが差し替わると、計算中のジョブの結果を書き戻す先が無くなる。
            // 結果を捨てて中止する。
            AbandonJob();

            _model       = model;
            _beforeIndex = -1;
            _afterIndex  = -1;
            _targetIndex = -1;
            _candidates.Clear();
            if (_statusLabel != null) _statusLabel.text = string.Empty;
            Refresh();
        }

        /// <summary>選択変更・属性変更後に呼ぶ。</summary>
        public void OnSelectionChanged()
        {
            Refresh();
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;

            if (_model == null)
            {
                ShowWarning("モデルがありません");
                return;
            }

            BuildCandidates(_model);
            if (_candidates.Count < 3)
            {
                ShowWarning("メッシュが3つ以上必要です（ビフォー・アフター・ターゲット）");
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display  = DisplayStyle.Flex;

            RefreshPickList(_beforeListContainer, _beforeIndex, idx =>
            {
                if (_beforeIndex == idx) return;
                _beforeIndex = idx;
                if (_afterIndex  == idx) _afterIndex  = -1;
                if (_targetIndex == idx) _targetIndex = -1;
                Refresh();
            });

            RefreshPickList(_afterListContainer, _afterIndex, idx =>
            {
                if (_afterIndex == idx) return;
                _afterIndex = idx;
                if (_beforeIndex == idx) _beforeIndex = -1;
                if (_targetIndex == idx) _targetIndex = -1;
                Refresh();
            });

            RefreshPickList(_targetListContainer, _targetIndex, idx =>
            {
                if (_targetIndex == idx) return;
                _targetIndex = idx;
                if (_beforeIndex == idx) _beforeIndex = -1;
                if (_afterIndex  == idx) _afterIndex  = -1;
                Refresh();
            });

            RefreshModeList();
            RefreshInfo();
        }

        /// <summary>モード一覧を描き直す。</summary>
        private void RefreshModeList()
        {
            if (_modeListContainer == null) return;

            _modeListContainer.Clear();

            bool graphAvailable = HasBeforeFaces();

            for (int i = 0; i < ModeChoices.Length; i++)
            {
                var choice = ModeChoices[i];
                bool disabled = IsLinkMode(choice.mode) && !graphAvailable;

                string text = "  " + choice.label;
                if (disabled) text += "（ビフォーに面が無いため選べません）";

                var row = new Label(text);
                row.style.paddingTop    = 2;
                row.style.paddingBottom = 2;
                row.style.paddingLeft   = 4;
                row.style.fontSize      = 10;
                row.style.whiteSpace    = WhiteSpace.Normal;

                if (disabled)
                {
                    row.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
                }
                else
                {
                    if (choice.mode == _mode)
                        row.style.backgroundColor = new StyleColor(new Color(0.24f, 0.48f, 0.9f, 0.5f));

                    ThinPlateLocalMode picked = choice.mode;
                    row.RegisterCallback<ClickEvent>(_ => OnModePicked(picked));
                }

                _modeListContainer.Add(row);
            }

            PlayerLayoutRoot.ApplyDarkTheme(_modeListContainer);

            // 局所モードでしか使わない入力は隠す
            bool local  = IsLocal(_mode);
            bool radius = IsRadiusMode(_mode);
            Show(_fieldNeighborCount, local && !radius);
            Show(_fieldRadius,        local && radius);
            Show(_fieldLocalCap,      local);
        }

        private void OnModePicked(ThinPlateLocalMode mode)
        {
            if (_mode == mode) return;

            bool wasLocal = IsLocal(_mode);
            bool isLocal  = IsLocal(mode);
            _mode = mode;

            // 全域と局所では λ の適正値が 2 桁違う。切り替えたときだけ既定値へ戻す。
            // 手で入れた値をモード内の切り替えで壊さないよう、跨いだときに限る。
            if (wasLocal != isLocal)
            {
                _lambda = isLocal
                    ? ThinPlateMorphOperation.LocalDefaultLambda
                    : ThinPlateMorphOperation.DefaultLambda;
                _fieldLambda?.SetValueWithoutNotify(_lambda);
            }

            RefreshModeList();
            RefreshInfo();
        }

        /// <summary>ビフォーに面があるか。リンク距離モードの可否判定に使う。</summary>
        private bool HasBeforeFaces()
        {
            if (_model == null || _beforeIndex < 0) return false;
            var ctx = _model.GetMeshContext(_beforeIndex);
            return ctx?.MeshObject != null && ctx.MeshObject.FaceCount > 0;
        }

        private static void Show(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ShowWarning(string msg)
        {
            _warningLabel.text          = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display  = DisplayStyle.None;
        }

        private void BuildCandidates(ModelContext model)
        {
            _candidates.Clear();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var ctx = model.GetMeshContext(i);
                if (ctx?.MeshObject == null || ctx.MeshObject.VertexCount == 0) continue;
                if (ctx.Type != MeshType.Mesh &&
                    ctx.Type != MeshType.BakedMirror &&
                    ctx.Type != MeshType.MirrorSide) continue;
                _candidates.Add((i, ctx.Name, ctx.MeshObject.VertexCount));
            }

            if (_beforeIndex >= 0 && !ContainsCandidate(_beforeIndex)) _beforeIndex = -1;
            if (_afterIndex  >= 0 && !ContainsCandidate(_afterIndex))  _afterIndex  = -1;
            if (_targetIndex >= 0 && !ContainsCandidate(_targetIndex)) _targetIndex = -1;
        }

        private bool ContainsCandidate(int index)
        {
            for (int i = 0; i < _candidates.Count; i++)
                if (_candidates[i].index == index) return true;
            return false;
        }

        /// <summary>制御点数の見積りと実行可否を更新する。</summary>
        private void RefreshInfo()
        {
            if (_infoLabel == null || _btnApply == null) return;

            bool running = _job != null && _job.IsRunning;

            string reason = ValidateSelection(out int candidateCount);
            bool ok = reason == null && !running;

            _btnApply.SetEnabled(ok);
            _btnApply.text = running ? "実行中…" : "実行";

            if (candidateCount < 0)
            {
                _infoLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
                _infoLabel.text = reason ?? "ビフォー・アフター・ターゲットを選んでください";
                return;
            }

            string text = IsLocal(_mode)
                ? $"制御点候補 約 {candidateCount} 点 / " + DescribeLocalCost(candidateCount)
                : $"制御点 約 {candidateCount} 点"
                  + $"（{ThinPlateMorphOperation.MinControlPointCount}"
                  + $"〜{ThinPlateMorphOperation.MaxControlPointCount} 点）";

            if (reason != null)
            {
                _infoLabel.style.color = new StyleColor(new Color(1f, 0.6f, 0.3f));
                _infoLabel.text = text + " / " + reason;
            }
            else if (!IsLocal(_mode) && candidateCount > ThinPlateMorphOperation.WarnControlPointCount)
            {
                _infoLabel.style.color = new StyleColor(new Color(1f, 0.8f, 0.4f));
                _infoLabel.text = text + " / 点数が多いため計算に時間がかかります";
            }
            else
            {
                _infoLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
                _infoLabel.text = text;
            }
        }

        /// <summary>
        /// 局所モードの計算量の目安を文にする。
        /// 半径モードは制御点数が入力依存で決まらないため、見積りは出さない。
        /// </summary>
        private string DescribeLocalCost(int candidateCount)
        {
            int targetVertexCount = GetTargetVertexCount();
            if (targetVertexCount <= 0) return "ターゲット頂点数が不明です";

            if (IsRadiusMode(_mode))
            {
                return $"ターゲット {targetVertexCount} 頂点。"
                     + $"制御点数は L 次第で変わるため所要時間は事前に見積れません"
                     + $"（上限 {_localCap} 点で頭打ち）";
            }

            int n = Mathf.Min(_neighborCount, Mathf.Min(_localCap, candidateCount));
            double cost = ThinPlateMorphOperation.EstimateLocalSolveCost(targetVertexCount, n);

            return $"ターゲット {targetVertexCount} 頂点 × 制御点 {n} 点。"
                 + $"LU の積和は約 {cost:0.0e+0} 回";
        }

        private int GetTargetVertexCount()
        {
            if (_model == null || _targetIndex < 0) return 0;
            var ctx = _model.GetMeshContext(_targetIndex);
            return ctx?.MeshObject?.VertexCount ?? 0;
        }

        /// <summary>
        /// 実行可能かを調べる。実行できるなら null、できないなら理由を返す。
        /// candidateCount には制御点候補数（算出できないときは -1）を入れる。
        ///
        /// 局所モードでは制御点数は頂点ごとに決まるため、
        /// 全域モードの上下限（MinControlPointCount / MaxControlPointCount）は適用しない。
        /// </summary>
        private string ValidateSelection(out int candidateCount)
        {
            candidateCount = -1;

            if (_model == null) return "モデルがありません";
            if (_beforeIndex < 0 || _afterIndex < 0) return "ビフォーとアフターを選んでください";
            if (_targetIndex < 0) return "ターゲットを選んでください";
            if (_beforeIndex == _afterIndex) return "ビフォーとアフターが同一です";
            if (_targetIndex == _beforeIndex || _targetIndex == _afterIndex)
                return "ターゲットはビフォー／アフターと別のオブジェクトにしてください";

            var beforeCtx = _model.GetMeshContext(_beforeIndex);
            var afterCtx  = _model.GetMeshContext(_afterIndex);
            var targetCtx = _model.GetMeshContext(_targetIndex);
            if (beforeCtx?.MeshObject == null || afterCtx?.MeshObject == null || targetCtx?.MeshObject == null)
                return "オブジェクトが不正です";

            if (targetCtx.Type != MeshType.Mesh)
                return "ターゲットは通常メッシュを選んでください";

            int bv = beforeCtx.MeshObject.VertexCount;
            int av = afterCtx.MeshObject.VertexCount;
            if (bv != av) return $"頂点数が一致しません（{bv} / {av}）";

            candidateCount = ThinPlateMorphOperation.EstimateControlPointCount(
                _model, _beforeIndex, _afterIndex, _selectedOnly);

            if (IsLocal(_mode))
            {
                if (IsLinkMode(_mode) && beforeCtx.MeshObject.FaceCount == 0)
                    return "ビフォーに面が無いためリンク距離モードは使えません";
                if (candidateCount < 1)
                    return "制御点候補がありません";
                if (IsRadiusMode(_mode) && !(_radius > 0f))
                    return "L に 0 より大きい値を入れてください";
                if (!IsRadiusMode(_mode) && _neighborCount < 1)
                    return "N に 1 以上の値を入れてください";
                if (targetCtx.MeshObject.VertexCount == 0)
                    return "ターゲットに頂点がありません";
                return null;
            }

            if (candidateCount < ThinPlateMorphOperation.MinControlPointCount)
                return $"制御点が足りません（{ThinPlateMorphOperation.MinControlPointCount} 点以上必要）";
            if (candidateCount > ThinPlateMorphOperation.MaxControlPointCount)
                return $"制御点が多すぎます（上限 {ThinPlateMorphOperation.MaxControlPointCount} 点）";

            return null;
        }

        // ================================================================
        // リスト描画
        // ================================================================

        private void RefreshPickList(VisualElement container, int selectedIndex, Action<int> onPick)
        {
            container.Clear();

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                int idx = c.index;

                var row = new Label($"  {c.name}  [V:{c.vertexCount}]");
                row.style.paddingTop    = 2;
                row.style.paddingBottom = 2;
                row.style.paddingLeft   = 4;
                row.style.fontSize      = 10;

                if (idx == selectedIndex)
                    row.style.backgroundColor = new StyleColor(new Color(0.24f, 0.48f, 0.9f, 0.5f));

                row.RegisterCallback<ClickEvent>(_ => onPick(idx));
                container.Add(row);
            }

            PlayerLayoutRoot.ApplyDarkTheme(container);
        }

        // ================================================================
        // 実行
        // ================================================================

        private void OnApplyClicked()
        {
            if (_job != null && _job.IsRunning)
            {
                SetStatus("すでに実行中です", error: true);
                return;
            }

            string reason = ValidateSelection(out int candidateCount);
            if (reason != null)
            {
                SetStatus(reason, error: true);
                return;
            }

            if (_panelContext == null)
            {
                SetStatus("コマンド送信経路が未配線です", error: true);
                return;
            }

            if (IsLocal(_mode)) StartLocalJob(candidateCount);
            else                ApplyGlobal(candidateCount);
        }

        /// <summary>全域モード。従来どおり同期実行する。</summary>
        private void ApplyGlobal(int controlCount)
        {
            _panelContext.SendCommand(new ApplyThinPlateMorphCommand(
                _getModelIndex?.Invoke() ?? 0,
                _beforeIndex, _afterIndex, _targetIndex,
                _lambda, _selectedOnly, _recalcNormals));

            // 追加された新規オブジェクトを候補リストへ反映する。
            Refresh();
            SetStatus($"制御点 約 {controlCount} 点で実行しました。メッシュリストを確認してください。", error: false);
            OnRepaint?.Invoke();
        }

        // ================================================================
        // 局所モードのバックグラウンド実行
        // ================================================================

        /// <summary>
        /// 入力をメインスレッドで組み立ててからバックグラウンドジョブを起こす。
        /// 組み立て後はモデルに触れないため、計算中にモデルが変わっても
        /// 計算そのものは壊れない。書き戻し時に頂点数を検査する。
        /// </summary>
        private void StartLocalJob(int candidateCount)
        {
            var input = ThinPlateMorphOperation.BuildLocalInput(
                _model, _beforeIndex, _afterIndex, _targetIndex, _selectedOnly, out string error);

            if (input == null)
            {
                SetStatus(error ?? "入力を組み立てられません", error: true);
                return;
            }

            if (IsLinkMode(_mode) && !input.HasGraph)
            {
                SetStatus("候補点だけの隣接グラフに辺がありません。リンク距離モードは使えません", error: true);
                return;
            }

            var options = new LocalMorphOptions
            {
                Mode             = _mode,
                NeighborCount    = _neighborCount,
                Radius           = _radius,
                MaxControlPoints = _localCap,
                Lambda           = _lambda,
            };

            _jobTargetIndex   = _targetIndex;
            _jobRecalcNormals = _recalcNormals;

            _job = PLBackgroundJob.Run(
                "ThinPlateMorphLocal",
                ctx => LocalThinPlateMorphSolver.Solve(input, options, ctx));

            _jobMonitor = PLJobMonitor.Attach(_root, _job, OnJobProgress, OnJobFinished);

            ShowProgress(true);
            UpdateProgressView(_job);
            SetStatus($"制御点候補 {candidateCount} 点で計算を始めました。中止できます。", error: false);
            RefreshInfo();
            OnRepaint?.Invoke();
        }

        private void OnCancelClicked()
        {
            if (_job == null || !_job.IsRunning) return;

            // 監視は続ける。中止が確定してから状態表示を更新するため。
            _job.Cancel();
            _btnCancel?.SetEnabled(false);
            SetStatus("中止を要求しました。区切りのよいところで止まります。", error: false);
        }

        private void OnJobProgress(PLJobHandle handle)
        {
            UpdateProgressView(handle);
            OnRepaint?.Invoke();
        }

        private void OnJobFinished(PLJobHandle handle)
        {
            var finished = _job;
            _job        = null;
            _jobMonitor = null;

            ShowProgress(false);
            _btnCancel?.SetEnabled(true);

            if (handle.IsCanceled)
            {
                SetStatus("中止しました。オブジェクトは追加していません。", error: false);
                RefreshInfo();
                OnRepaint?.Invoke();
                return;
            }

            if (handle.IsFaulted)
            {
                string message = handle.Error != null ? handle.Error.Message : "原因不明";
                Debug.LogWarning($"[ThinPlateMorph] 局所モードの計算に失敗しました: {handle.Error}");
                SetStatus("計算に失敗しました: " + message, error: true);
                RefreshInfo();
                OnRepaint?.Invoke();
                return;
            }

            var result = finished?.Result;
            if (result?.LocalPositions == null)
            {
                SetStatus("結果を取得できませんでした", error: true);
                RefreshInfo();
                OnRepaint?.Invoke();
                return;
            }

            // 計算中にターゲットが差し替わっていないかを頂点数で確かめる。
            var targetCtx = _model?.GetMeshContext(_jobTargetIndex);
            if (targetCtx?.MeshObject == null ||
                targetCtx.MeshObject.VertexCount != result.LocalPositions.Length)
            {
                SetStatus("計算中にターゲットが変わったため、結果を破棄しました", error: true);
                RefreshInfo();
                OnRepaint?.Invoke();
                return;
            }

            _panelContext?.SendCommand(new ApplyThinPlateMorphResultCommand(
                _getModelIndex?.Invoke() ?? 0,
                _jobTargetIndex, result.LocalPositions, _jobRecalcNormals));

            Refresh();
            SetStatus(DescribeResult(result, handle.ElapsedSeconds), error: false);
            OnRepaint?.Invoke();
        }

        private static string DescribeResult(LocalMorphResult r, double elapsedSeconds)
        {
            return $"完了（{elapsedSeconds:0.0} 秒）。"
                 + $"TPS {r.TpsCount} / アフィン {r.AffineCount} / 相似 {r.SimilarityCount}"
                 + $" / 重心 {r.CentroidCount} / 不動 {r.UnchangedCount} 頂点。"
                 + $"平均制御点 {r.AverageControlPoints:0.0} 点。"
                 + "メッシュリストを確認してください。";
        }

        /// <summary>
        /// 実行中でなくなったら結果を捨ててジョブを手放す。
        /// モデル差し替えなど、書き戻し先が無くなる場面で使う。
        /// </summary>
        private void AbandonJob()
        {
            if (_jobMonitor != null)
            {
                _jobMonitor.CancelAndStop();
                _jobMonitor = null;
            }
            else
            {
                _job?.Cancel();
            }

            _job = null;
            _jobTargetIndex = -1;
            ShowProgress(false);
            _btnCancel?.SetEnabled(true);
        }

        private void ShowProgress(bool visible)
        {
            Show(_progressRow, visible);
        }

        private void UpdateProgressView(PLJobHandle handle)
        {
            if (_progressLabel == null || _progressFill == null || handle == null) return;

            float p = handle.Progress;
            _progressFill.style.width = new StyleLength(Length.Percent(p * 100f));

            int total = handle.StepTotal;
            string counts = total > 0 ? $"{handle.StepDone} / {total} 頂点" : "準備中";
            _progressLabel.text = $"{counts}  {p * 100f:0.0}%  経過 {handle.ElapsedSeconds:0.0} 秒";
        }

        private void SetStatus(string text, bool error)
        {
            if (_statusLabel == null) return;
            _statusLabel.style.color = error
                ? new StyleColor(new Color(1f, 0.4f, 0.4f))
                : new StyleColor(new Color(0.4f, 0.8f, 1f));
            _statusLabel.text = text;
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private static Label SecLabel(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement Sep()
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
