// CCDIKSolver.cs
// CCD法によるIKソルバー（MikuMikuFlex準拠）
//
// ■ 既知の問題（未解決・恒久メモ）
//   VMD 再生時、足ＩＫが収束しないフレームがある。実測（キャッチボール_トマホーク_.vmd、
//   50 サンプル）では左足首の残差 dist が
//       min 0.00009 / max 1.14002 / mean 0.13528
//   で、12 サンプルが dist > 0.1。右足首は max 0.22012 / mean 0.05881 で、
//   左が右の約 2 倍悪い（左右非対称）。
//   連続フレームで残差が単調減少する区間（0.20 → 0.075 など）があり、
//   1 フレーム内で収束しきらず次フレームへ持ち越している疑いがある。
//   また f=0 では eff が tgt より Y に約 0.02 高い状態で停止する（X・Z は一致）。
//   角度制限のフレーム変換（下記 ToModelAxis / ToLocalAxis）を入れても f=0 の
//   残差は変化しなかったため、原因は別にある。要調査。
//
// ■ MikuMikuFlexとPolyLingのWorldMatrix構造差
//
// MikuMikuFlex:
//   ローカルポーズ = T(-Position) * R(回転) * T(移動) * T(Position)
//   モデルポーズ   = ローカルポーズ * 親.モデルポーズ
//   ワールド位置   = TransformCoordinate(Position, モデルポーズ)
//
// PolyLing (translation-only bindpose):
//   LocalMatrix = T(localPos) * TRS(deltaPos, deltaRot, 1)
//   WorldMatrix = 親.WorldMatrix * LocalMatrix
//   ワールド位置 = WorldMatrix.MultiplyPoint3x4(Vector3.zero)
//
// したがって、MikuMikuFlexの以下の処理：
//   effLocal = TransformCoordinate(eff.Position, eff.モデルポーズ * Inv(link.モデルポーズ))
// は、PolyLingでは：
//   effWorld = GetWorldPosition(effCtx)  // = WorldMatrix.GetColumn(3)
//   effLocal = Inv(link.WorldMatrix).MultiplyPoint3x4(effWorld)
// となる。同様にlinkのローカル位置は (0,0,0)。

//
// ================================================================
// ■ 【重要】軸規約の変更に未追随。動作保証対象外。
// ----------------------------------------------------------------
//   PMX 取込のボーン局所軸は以下に変更された（PMXImporter.CalculateBoneModelRotation）:
//     (1) AxisFlipOps.Basis を両側共役 S·M·S から片側変換 S·M へ
//     (2) MMD 規約（X = 骨の長手方向）から DCC 標準（Y = 骨の長手方向）へ巡回置換
//   本ファイルの R^-1·Q·R 変換（R = ctx.BoneModelRotation）はこの変更に
//   追随していないため、結果は正しくない。
//
//   既知の症状:
//     - 足ＩＫが収束しないフレームがある（左足首 max dist 1.14 / 12 of 50 サンプル）
//     - 統合経路には IK 自体が無い（MotionClipApplier に Solve 呼び出しなし）
//     - 付与親（GrantParentIndex / GrantRate）が未評価
//
//   修正する場合は、局所軸が「Y = 骨方向」であることを前提に組み直すこと。
//   旧規約を前提とした つじつま合わせ を足さないこと。
// ================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.VMD
{
    public class CCDIKSolver
    {
        /// <summary>
        /// デバッグログの有効/無効。falseなら一切ログを出さない。
        /// </summary>
        public bool DebugEnabled = false;

        /// <summary>
        /// デバッグ対象の IK ボーン名（部分一致）。null/空なら全 IK ボーンを対象にする。
        /// </summary>
        public string DebugBoneFilter = null;

        /// <summary>
        /// デバッグ対象のフレーム範囲。既定は全フレーム。
        /// </summary>
        public float DebugFrameMin = float.NegativeInfinity;
        public float DebugFrameMax = float.PositiveInfinity;

        /// <summary>
        /// IK解決前に膝リンクへ微小曲げを付与する。
        /// 直線状態での回転軸不定による膝反転を防止するMMD標準テクニック。
        /// IgnoreAngleLimits が true のときは角度制限前提の処理なので実行しない。
        /// </summary>
        public bool KneePreBend = false;// true;

        // ================================================================
        // 角度制限（VMD 復活手順書 段階 2）
        // ================================================================

        /// <summary>
        /// 全リンクの角度制限を無視する。既定 false（段階 3）。
        /// true のとき RestrictRotation を通さず、KneePreBend も実行しない。
        /// </summary>
        public bool IgnoreAngleLimits = false;

        // ================================================================
        // トレース出力（vmd_ik.csv）
        // ================================================================

        /// <summary>反復トレースの出力有無。既定 OFF。</summary>
        public bool TraceEnabled = false;

        /// <summary>トレース CSV の出力フォルダ。未設定ならトレースを出さない。</summary>
        public string TraceDirectory;

        /// <summary>トレース CSV のファイル名</summary>
        public const string TraceFileName = "vmd_ik.csv";

        /// <summary>サマリ CSV のファイル名</summary>
        public const string SummaryFileName = "vmd_summary.csv";

        /// <summary>トレース対象の IK ボーン名。空なら全 IK ボーンを対象にする。</summary>
        public HashSet<string> TraceIkBoneNames = new HashSet<string>();

        private VMDIKTraceWriter _trace;
        private VMDIKSummaryWriter _summary;

        public void Solve(ModelContext model, float frameNumber = -1f)
        {
            if (model == null || model.MeshContextList == null)
                return;

            // IKレイヤーをクリア
            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx?.BonePoseData != null)
                    ctx.BonePoseData.ClearLayer("IK");
            }

            model.ComputeWorldMatrices();

            // トレース出力の準備（TraceEnabled かつ出力先が有効なときだけ開く）
            bool trace = TraceEnabled && EnsureTraceWriter();
            if (trace) EnsureSummaryWriter();

            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx == null) continue;
                if (!ctx.IsIK || ctx.IKLinks == null || ctx.IKLinks.Count == 0)
                    continue;
                if (ctx.IKTargetIndex < 0 || ctx.IKTargetIndex >= model.MeshContextList.Count)
                    continue;

                SolveIKBone(model, i, frameNumber, trace);
            }
        }

        private void SolveIKBone(ModelContext model, int ikBoneIndex, float frameNumber, bool trace)
        {
            var ikBone = model.MeshContextList[ikBoneIndex];

            // トレース対象か（TraceIkBoneNames が空なら全 IK ボーン）
            bool traceThis = trace
                && (TraceIkBoneNames == null || TraceIkBoneNames.Count == 0
                    || (ikBone.Name != null && TraceIkBoneNames.Contains(ikBone.Name)));

            // デバッグ条件: フラグON かつ 名前フィルタ一致 かつ フレーム範囲内
            bool debugLog = DebugEnabled
                && (string.IsNullOrEmpty(DebugBoneFilter)
                    || (ikBone.Name != null && ikBone.Name.Contains(DebugBoneFilter)))
                && frameNumber >= DebugFrameMin && frameNumber <= DebugFrameMax;

            if (debugLog)
                Debug.Log($"[CCDIK BEGIN] frame={frameNumber} IK='{ikBone.Name}' links={ikBone.IKLinks.Count} target={ikBone.IKTargetIndex}");

            if (debugLog)
            {
                Debug.Log($"[CCDIK PARAM] frame={frameNumber} IK='{ikBone.Name}' IKLoopCount={ikBone.IKLoopCount} IKLimitAngle={ikBone.IKLimitAngle:F6}rad ({ikBone.IKLimitAngle * Mathf.Rad2Deg:F2}deg)");
                Debug.Log($"[CCDIK PARAM]   limitMin/Max for links:");
                foreach (var link in ikBone.IKLinks)
                {
                    if (link.BoneIndex >= 0 && link.BoneIndex < model.MeshContextList.Count)
                    {
                        var lc = model.MeshContextList[link.BoneIndex];
                        var bt = lc.BoneTransform;
                        Debug.Log($"[CCDIK PARAM]   link='{lc.Name}' hasLimit={link.HasLimit} min={link.LimitMin} max={link.LimitMax} BT.Rot={bt?.Rotation} UseLocal={bt?.UseLocalTransform}");
                    }
                }
            }

            // --- 膝の初期微小曲げ ---
            // 角度制限付きリンクがほぼ無回転の場合、微小なX回転を付与して
            // エフェクタ・リンク直線時の回転軸不定を防止する
            //
            // ■ フレーム注意
            //   limitMin/Max・ヒンジ軸 X は MMD 規約（レスト回転 = identity）の
            //   モデル軸フレームの値。BonePoseData の回転はボーンローカル軸
            //   フレームなので、R = BoneModelRotation で共役変換して受け渡す。
            if (KneePreBend && !IgnoreAngleLimits)
            {
                bool preBent = false;
                foreach (var link in ikBone.IKLinks)
                {
                    if (!link.HasLimit) continue;
                    if (link.BoneIndex < 0 || link.BoneIndex >= model.MeshContextList.Count) continue;

                    var linkCtx = model.MeshContextList[link.BoneIndex];
                    Quaternion rot = GetBoneRotation(linkCtx);
                    // ほぼidentity（未回転）の場合のみ適用
                    if (Mathf.Abs(1f - Mathf.Abs(rot.w)) < 0.001f)
                    {
                        if (linkCtx.BonePoseData == null)
                            linkCtx.BonePoseData = new BonePoseData();
                        // limitMin.xが負（膝のように曲がる方向）なら負方向に微小曲げ
                        float bendAngle = link.LimitMin.x < 0 ? -0.01f : 0.01f;
                        Quaternion bendModel = Quaternion.AngleAxis(bendAngle * Mathf.Rad2Deg, Vector3.right);
                        Quaternion preBendRot = ToLocalAxis(linkCtx, bendModel) * rot;
                        SetBoneRotation(linkCtx, preBendRot);
                        preBent = true;
                    }
                }
                if (preBent)
                    model.ComputeWorldMatrices();
            }

            // --- 最適値記録用 ---
            int effectorIndex0 = ikBone.IKTargetIndex;
            Vector3 targetWorld0 = GetWorldPosition(model.MeshContextList[ikBoneIndex]);
            float bestDist = Vector3.Distance(GetWorldPosition(model.MeshContextList[effectorIndex0]), targetWorld0);
            float distStart = bestDist;   // ループ開始前の残差（サマリ用）
            int bestIt = -1;
            var bestRotations = new Dictionary<int, Quaternion>();
            foreach (var link0 in ikBone.IKLinks)
            {
                if (link0.BoneIndex >= 0 && link0.BoneIndex < model.MeshContextList.Count)
                    bestRotations[link0.BoneIndex] = GetBoneRotation(model.MeshContextList[link0.BoneIndex]);
            }

            for (int it = 0; it < ikBone.IKLoopCount; it++)
            {
                int effectorIndex = ikBone.IKTargetIndex;
                bool logThisIt = debugLog && (it < 5 || it % 10 == 0 || it == ikBone.IKLoopCount - 1);

                foreach (var link in ikBone.IKLinks)
                {
                    if (link.BoneIndex < 0 || link.BoneIndex >= model.MeshContextList.Count)
                        continue;

                    var linkCtx = model.MeshContextList[link.BoneIndex];
                    if (linkCtx.BonePoseData == null)
                        linkCtx.BonePoseData = new BonePoseData();

                    var effCtx = model.MeshContextList[effectorIndex];
                    Vector3 effectorWorld = GetWorldPosition(effCtx);
                    Vector3 targetWorld = GetWorldPosition(model.MeshContextList[ikBoneIndex]);

                    Matrix4x4 toLinkLocal = linkCtx.WorldMatrix.inverse;
                    Vector3 effectorLocal = toLinkLocal.MultiplyPoint3x4(effectorWorld);
                    Vector3 targetLocal = toLinkLocal.MultiplyPoint3x4(targetWorld);

                    Vector3 v1 = effectorLocal.normalized;
                    Vector3 v2 = targetLocal.normalized;

                    float dotCheck = Vector3.Dot(v1, v2);

                    if (logThisIt)
                        Debug.Log($"[CCDIK it={it}] link='{linkCtx.Name}' dot={dotCheck:F6} dist={Vector3.Distance(effectorWorld, targetWorld):F4} curRot=({GetBoneRotation(linkCtx).x:F4},{GetBoneRotation(linkCtx).y:F4},{GetBoneRotation(linkCtx).z:F4},{GetBoneRotation(linkCtx).w:F4})");

                    // --- 回転軸・回転角 ---
                    Vector3 rotationAxis = Vector3.Cross(v1, v2);
                    float dot = Vector3.Dot(v1, v2);
                    dot = Mathf.Clamp(dot, -1f, 1f);
                    float angle = Mathf.Acos(dot);
                    float angleBeforeClamp = angle;
                    angle = Mathf.Min(angle, ikBone.IKLimitAngle);

                    float distBefore = Vector3.Distance(effectorWorld, targetWorld);

                    if (angle <= 1.0e-5f)
                    {
                        if (logThisIt) Debug.Log($"[CCDIK it={it}]   SKIP angle={angleBeforeClamp:F6}→{angle:F6} too small");
                        if (traceThis)
                        {
                            Quaternion skipRot = GetBoneRotation(linkCtx);
                            _trace.Write(frameNumber, ikBone.Name, it, linkCtx.Name,
                                         effectorWorld, targetWorld, distBefore,
                                         Vector3.zero, 0f, skipRot, skipRot, false);
                        }
                        continue;
                    }

                    if (rotationAxis.sqrMagnitude < 1e-10f)
                    {
                        if (logThisIt) Debug.Log($"[CCDIK it={it}]   SKIP rotAxis too small");
                        if (traceThis)
                        {
                            Quaternion skipRot = GetBoneRotation(linkCtx);
                            _trace.Write(frameNumber, ikBone.Name, it, linkCtx.Name,
                                         effectorWorld, targetWorld, distBefore,
                                         Vector3.zero, 0f, skipRot, skipRot, false);
                        }
                        continue;
                    }
                    rotationAxis.Normalize();

                    if (logThisIt)
                        Debug.Log($"[CCDIK it={it}]   angle={angleBeforeClamp:F4}→{angle:F4}rad axis=({rotationAxis.x:F3},{rotationAxis.y:F3},{rotationAxis.z:F3})");

                    Quaternion rotQ = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, rotationAxis).normalized;

                    // --- ボーン回転更新 ---
                    Quaternion currentRot = GetBoneRotation(linkCtx);
                    //Quaternion newRot = rotQ * currentRot;
                    Quaternion newRot =  currentRot * rotQ;// にする（CCD適用の掛け順）

                    // --- 角度制限 ---
                    //
                    // ■ フレーム変換（必須）
                    //   newRot はボーンローカル軸フレームのデルタ。一方 LimitMin/Max は
                    //   PMX 由来のモデル軸フレーム値（PMXImporter は AxisFlipOps.AngleLimits で
                    //   軸反転を掛けるだけで、レスト回転 R の共役は掛けていない）。
                    //   そのまま clamp すると、例えば膝のヒンジ軸（モデル軸 X）が
                    //   ローカル軸フレームでは X 成分 0 になり、補正が丸ごと捨てられる。
                    //   R = BoneModelRotation として q_model = R·q_local·R^-1 に写して
                    //   clamp し、q_local = R^-1·q_model·R で戻す。
                    bool clamped = false;
                    if (link.HasLimit && !IgnoreAngleLimits)
                    {
                        Quaternion before      = newRot;
                        Quaternion beforeModel = ToModelAxis(linkCtx, newRot);
                        Quaternion afterModel  = RestrictRotation(beforeModel, link.LimitMin, link.LimitMax, logThisIt);
                        newRot = ToLocalAxis(linkCtx, afterModel);
                        clamped = QuaternionChanged(before, newRot);
                        if (logThisIt)
                        {
                            Debug.Log($"[CCDIK it={it}]   restrict(model) before=({beforeModel.x:F4},{beforeModel.y:F4},{beforeModel.z:F4},{beforeModel.w:F4}) after=({afterModel.x:F4},{afterModel.y:F4},{afterModel.z:F4},{afterModel.w:F4})");
                            Debug.Log($"[CCDIK it={it}]   restrict(local) before=({before.x:F4},{before.y:F4},{before.z:F4},{before.w:F4}) after=({newRot.x:F4},{newRot.y:F4},{newRot.z:F4},{newRot.w:F4})");
                        }
                    }

                    if (traceThis)
                    {
                        _trace.Write(frameNumber, ikBone.Name, it, linkCtx.Name,
                                     effectorWorld, targetWorld, distBefore,
                                     rotationAxis, angle * Mathf.Rad2Deg,
                                     currentRot, newRot, clamped);
                    }

                    // --- BonePoseDataに設定 ---
                    SetBoneRotation(linkCtx, newRot);

                    // --- 各リンク後にWorldMatrix再計算（MikuMikuFlex準拠） ---
                    model.ComputeWorldMatrices();

                    if (logThisIt)
                    {
                        Vector3 newEffW = GetWorldPosition(model.MeshContextList[effectorIndex]);
                        Debug.Log($"[CCDIK it={it}]   afterSet effW={newEffW} dist={Vector3.Distance(newEffW, targetWorld):F4}");
                    }
                }

                // --- イテレーション完了後: 距離を計算し、最適値を記録 ---
                float itDist = Vector3.Distance(
                    GetWorldPosition(model.MeshContextList[effectorIndex0]),
                    targetWorld0);

                if (itDist < bestDist)
                {
                    bestDist = itDist;
                    bestIt = it;
                    foreach (var link in ikBone.IKLinks)
                    {
                        if (link.BoneIndex >= 0 && link.BoneIndex < model.MeshContextList.Count)
                            bestRotations[link.BoneIndex] = GetBoneRotation(model.MeshContextList[link.BoneIndex]);
                    }
                }

                if (logThisIt)
                    Debug.Log($"[CCDIK it={it}] END itDist={itDist:F6} bestDist={bestDist:F6} bestIt={bestIt}");
            }

            // --- ループ完了後: 最適値を復元 ---
            float finalDist = Vector3.Distance(
                GetWorldPosition(model.MeshContextList[effectorIndex0]),
                targetWorld0);

            if (finalDist > bestDist + 1e-6f)
            {
                foreach (var kvp in bestRotations)
                {
                    var ctx = model.MeshContextList[kvp.Key];
                    SetBoneRotation(ctx, kvp.Value);
                }
                model.ComputeWorldMatrices();

                if (debugLog)
                    Debug.Log($"[CCDIK RESTORE] Restored to bestIt={bestIt} bestDist={bestDist:F6} (finalDist was {finalDist:F6})");
            }

            // --- 最終残差（distEnd）を 1 行出す。iteration=-1 / link="(final)" ---
            if (traceThis)
            {
                Vector3 effEnd = GetWorldPosition(model.MeshContextList[ikBone.IKTargetIndex]);
                Vector3 tgtEnd = GetWorldPosition(model.MeshContextList[ikBoneIndex]);
                _trace.Write(frameNumber, ikBone.Name, -1, "(final)",
                             effEnd, tgtEnd, Vector3.Distance(effEnd, tgtEnd),
                             Vector3.zero, 0f, Quaternion.identity, Quaternion.identity, false);
            }

            // --- サマリ 1 行（vmd_summary.csv）---
            if (traceThis && _summary != null && _summary.IsOpen)
            {
                float distEndVal = Vector3.Distance(
                    GetWorldPosition(model.MeshContextList[effectorIndex0]), targetWorld0);

                bool  hasKnee    = false;
                float kneeDeg    = 0f;
                bool  kneeSignOK = false;
                MeasureLimitedLink(model, ikBone, out hasKnee, out kneeDeg, out kneeSignOK);

                _summary.Write(frameNumber, ikBone.Name, bestIt,
                               distStart, distEndVal, bestDist,
                               hasKnee, kneeDeg, kneeSignOK);
            }

            // IK結果ログ
            if (debugLog)
            {
                var effFinal = model.MeshContextList[ikBone.IKTargetIndex];
                var tgtFinal = model.MeshContextList[ikBoneIndex];
                Vector3 effW = GetWorldPosition(effFinal);
                Vector3 tgtW = GetWorldPosition(tgtFinal);
                Debug.Log($"[CCDIK RESULT] eff='{effFinal.Name}' effWorld={effW} tgtWorld={tgtW} dist={Vector3.Distance(effW,tgtW):F6}");
            }
        }

        // =================================================================
        // ユーティリティ
        // =================================================================

        private Vector3 GetWorldPosition(MeshContext ctx)
        {
            return ctx.WorldMatrix.GetColumn(3);
        }

        // =================================================================
        // トレース
        // =================================================================

        /// <summary>出力先が有効ならライタを開く。失敗したらトレースを止める。</summary>
        private bool EnsureTraceWriter()
        {
            if (_trace != null && _trace.IsOpen) return true;
            if (string.IsNullOrEmpty(TraceDirectory)) return false;

            try
            {
                if (_trace == null) _trace = new VMDIKTraceWriter();
                _trace.Open(System.IO.Path.Combine(TraceDirectory, TraceFileName));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CCDIKSolver] トレース出力を開けません: {ex.Message}");
                TraceEnabled = false;
                return false;
            }
        }

        /// <summary>サマリ出力先が有効ならライタを開く。</summary>
        private bool EnsureSummaryWriter()
        {
            if (_summary != null && _summary.IsOpen) return true;
            if (string.IsNullOrEmpty(TraceDirectory)) return false;

            try
            {
                if (_summary == null) _summary = new VMDIKSummaryWriter();
                _summary.Open(System.IO.Path.Combine(TraceDirectory, SummaryFileName));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CCDIKSolver] サマリ出力を開けません: {ex.Message}");
                return false;
            }
        }

        /// <summary>トレース CSV（vmd_ik.csv / vmd_summary.csv）を閉じる。</summary>
        public void CloseTrace()
        {
            _trace?.Close();
            _summary?.Close();
        }

        /// <summary>
        /// 角度制限付きリンクの屈曲角を測る。
        /// 合成回転を ToModelAxis でモデル軸へ写し（BoneModelRotation は恒等なので素通り）、
        /// RestrictRotation と同じ SplitRotation で分解した X 成分を返す。
        /// clamp の判定と必ず同じ分解を使うため、専用の分解は作らない。
        /// IgnoreAngleLimits が true でも計算する（手順書 §1-4）。
        /// </summary>
        private void MeasureLimitedLink(ModelContext model, MeshContext ikBone,
                                        out bool hasKnee, out float kneeAngleDeg, out bool kneeSignOK)
        {
            hasKnee = false;
            kneeAngleDeg = 0f;
            kneeSignOK = false;

            foreach (var link in ikBone.IKLinks)
            {
                if (!link.HasLimit) continue;
                if (link.BoneIndex < 0 || link.BoneIndex >= model.MeshContextList.Count) continue;

                var linkCtx = model.MeshContextList[link.BoneIndex];
                if (linkCtx == null) continue;

                Quaternion modelRot = ToModelAxis(linkCtx, GetBoneRotation(linkCtx));
                float xRot, yRot, zRot;
                SplitRotation(modelRot, out xRot, out yRot, out zRot);
                xRot = NormalizeAngle(xRot, -Mathf.PI, Mathf.PI);

                const float eps = 1.0e-4f;
                hasKnee      = true;
                kneeAngleDeg = xRot * Mathf.Rad2Deg;
                kneeSignOK   = xRot >= link.LimitMin.x - eps && xRot <= link.LimitMax.x + eps;
                return;   // 制限付きリンクは 1 本だけを対象にする
            }
        }

        /// <summary>角度制限で実際にクォータニオンが変化したかを判定する。</summary>
        private static bool QuaternionChanged(Quaternion a, Quaternion b)
        {
            const float eps = 1e-6f;
            return Mathf.Abs(a.x - b.x) > eps
                || Mathf.Abs(a.y - b.y) > eps
                || Mathf.Abs(a.z - b.z) > eps
                || Mathf.Abs(a.w - b.w) > eps;
        }

        /// <summary>
        /// ボーンローカル軸フレームの回転を、MMD 規約（レスト回転 = identity）の
        /// モデル軸フレームへ写す。q_model = R · q_local · R^-1（R = BoneModelRotation）。
        /// R が identity のボーンでは恒等変換。
        /// </summary>
        private Quaternion ToModelAxis(MeshContext ctx, Quaternion localRot)
        {
            Quaternion r = ctx.BoneModelRotation;
            if (r == Quaternion.identity) return localRot;
            return r * localRot * Quaternion.Inverse(r);
        }

        /// <summary>
        /// ToModelAxis の逆。q_local = R^-1 · q_model · R。
        /// </summary>
        private Quaternion ToLocalAxis(MeshContext ctx, Quaternion modelRot)
        {
            Quaternion r = ctx.BoneModelRotation;
            if (r == Quaternion.identity) return modelRot;
            return Quaternion.Inverse(r) * modelRot * r;
        }

        /// <summary>
        /// BonePoseDataの合成回転を取得（MikuMikuFlexの「ボーン.回転」に相当）
        /// </summary>
        private Quaternion GetBoneRotation(MeshContext ctx)
        {
            if (ctx.BonePoseData != null && ctx.BonePoseData.IsActive)
                return ctx.BonePoseData.Rotation;
            return Quaternion.identity;
        }

        /// <summary>
        /// 全体の合成回転を設定（MikuMikuFlexの「ボーン.回転 = newRot」に相当）
        /// IKレイヤーのDeltaRotationを逆算して設定
        ///
        /// ■ 掛け順の根拠（実測で確定）
        ///   BonePoseData.Recalculate（BonePoseData.cs:314）は
        ///     _blendedRotation = weightedDelta * _blendedRotation;
        ///   でレイヤーをリスト順に前掛けする。GetOrCreateLayer は末尾追加
        ///   （BonePoseData.cs:174-185）、ClearLayer は要素を残す（同 240-249）ため、
        ///   VMD レイヤー（VMDApplier.ApplyBonePose）が先、IK レイヤー（本メソッド）が
        ///   後で、順序は [VMD, IK] に固定される。したがって
        ///     total = IK · VMD
        ///   であり、逆算は
        ///     IK = total · VMD^-1
        ///   でなければならない。
        ///
        ///   旧実装の IK = VMD^-1 · total を入れると total' = VMD^-1·total·VMD となり、
        ///   VMD が恒等のときだけ total と一致する。vmd_ik.csv の実測では、
        ///   左足（VMD デルタ 25.13deg）で次反復の読み戻し値が after ではなく
        ///   VMD^-1·after·VMD に一致し（ずれ 平均 0.006deg / 対する after とのずれ 平均 5.04deg）、
        ///   VMD 回転が非恒等なリンクでのみ IK が破綻していた。
        /// </summary>
        private void SetBoneRotation(MeshContext ctx, Quaternion newRot)
        {
            var bpd = ctx.BonePoseData;
            bpd.IsActive = true;

            Quaternion vmdDelta = GetVMDDelta(bpd);
            Quaternion ikDelta = newRot * Quaternion.Inverse(vmdDelta);

            bpd.SetLayerRotation("IK", ikDelta);
        }

        private Quaternion GetVMDDelta(BonePoseData bpd)
        {
            if (bpd == null) return Quaternion.identity;
            var layers = bpd.Layers;
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i].Name == "VMD" && layers[i].Enabled)
                    return layers[i].DeltaRotation;
            }
            return Quaternion.identity;
        }

        // =================================================================
        // RestrictRotation — HagLib/MikuMikuFlex準拠
        // =================================================================

        private Quaternion RestrictRotation(Quaternion rotation, Vector3 limitMin, Vector3 limitMax, bool log = false)
        {
            float xRot, yRot, zRot;
            int type = SplitRotation(rotation, out xRot, out yRot, out zRot);

            float xBefore = xRot, yBefore = yRot, zBefore = zRot;

            xRot = NormalizeAngle(xRot, -Mathf.PI, Mathf.PI);
            yRot = NormalizeAngle(yRot, -Mathf.PI * 0.5f, Mathf.PI * 0.5f);
            zRot = NormalizeAngle(zRot, -Mathf.PI, Mathf.PI);

            float xNorm = xRot, yNorm = yRot, zNorm = zRot;

            xRot = Mathf.Clamp(xRot, limitMin.x, limitMax.x);
            yRot = Mathf.Clamp(yRot, limitMin.y, limitMax.y);
            zRot = Mathf.Clamp(zRot, limitMin.z, limitMax.z);

            if (log)
            {
                Debug.Log($"[CCDIK RESTRICT] type={type} raw=({xBefore:F4},{yBefore:F4},{zBefore:F4}) norm=({xNorm:F4},{yNorm:F4},{zNorm:F4}) clamped=({xRot:F4},{yRot:F4},{zRot:F4}) limit=[({limitMin.x:F4},{limitMin.y:F4},{limitMin.z:F4}),({limitMax.x:F4},{limitMax.y:F4},{limitMax.z:F4})]");
            }

            // SharpDX行優先: RotX * RotY * RotZ → Unity列優先: RotZ * RotY * RotX
            Quaternion result;
            switch (type)
            {
                case 0: // XYZ
                    result = Quaternion.AngleAxis(zRot * Mathf.Rad2Deg, Vector3.forward)
                           * Quaternion.AngleAxis(yRot * Mathf.Rad2Deg, Vector3.up)
                           * Quaternion.AngleAxis(xRot * Mathf.Rad2Deg, Vector3.right);
                    break;
                case 1: // YZX
                    result = Quaternion.AngleAxis(xRot * Mathf.Rad2Deg, Vector3.right)
                           * Quaternion.AngleAxis(zRot * Mathf.Rad2Deg, Vector3.forward)
                           * Quaternion.AngleAxis(yRot * Mathf.Rad2Deg, Vector3.up);
                    break;
                default: // ZXY
                    result = Quaternion.AngleAxis(yRot * Mathf.Rad2Deg, Vector3.up)
                           * Quaternion.AngleAxis(xRot * Mathf.Rad2Deg, Vector3.right)
                           * Quaternion.AngleAxis(zRot * Mathf.Rad2Deg, Vector3.forward);
                    break;
            }
            return result.normalized;
        }

        private float NormalizeAngle(float angle, float min, float max)
        {
            if (angle < min) angle += Mathf.PI * 2f;
            else if (angle > max) angle -= Mathf.PI * 2f;
            return angle;
        }

        // =================================================================
        // SplitRotation — HagLib QuaternionHelper 準拠
        // =================================================================

        private int SplitRotation(Quaternion rotation, out float xRot, out float yRot, out float zRot)
        {
            if (FactoringXYZ(rotation, out xRot, out yRot, out zRot)) return 0;
            if (FactoringYZX(rotation, out xRot, out yRot, out zRot)) return 1;
            FactoringZXY(rotation, out xRot, out yRot, out zRot);
            return 2;
        }

        private bool FactoringXYZ(Quaternion q, out float xRot, out float yRot, out float zRot)
        {
            Matrix4x4 rot = Matrix4x4.Rotate(q.normalized);
            float m13 = rot.m20;
            if (m13 > 1f - 1.0e-4f || m13 < -1f + 1.0e-4f)
            {
                xRot = 0;
                yRot = m13 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                zRot = -Mathf.Atan2(-rot.m01, rot.m11);
                return false;
            }
            yRot = -Mathf.Asin(m13);
            float cosY = Mathf.Cos(yRot);
            xRot = Mathf.Asin(rot.m21 / cosY);
            if (float.IsNaN(xRot))
            {
                xRot = 0;
                yRot = m13 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                zRot = -Mathf.Atan2(-rot.m01, rot.m11);
                return false;
            }
            if (rot.m22 < 0)
                xRot = Mathf.PI - xRot;
            zRot = Mathf.Atan2(rot.m10, rot.m00);
            return true;
        }

        private bool FactoringYZX(Quaternion q, out float xRot, out float yRot, out float zRot)
        {
            Matrix4x4 rot = Matrix4x4.Rotate(q.normalized);
            float m21 = rot.m01;
            if (m21 > 1f - 1.0e-4f || m21 < -1f + 1.0e-4f)
            {
                yRot = 0;
                zRot = m21 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                xRot = -Mathf.Atan2(-rot.m12, rot.m22);
                return false;
            }
            zRot = -Mathf.Asin(m21);
            float cosZ = Mathf.Cos(zRot);
            yRot = Mathf.Asin(rot.m02 / cosZ);
            if (float.IsNaN(yRot))
            {
                yRot = 0;
                zRot = m21 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                xRot = -Mathf.Atan2(-rot.m12, rot.m22);
                return false;
            }
            if (rot.m00 < 0)
                yRot = Mathf.PI - yRot;
            xRot = Mathf.Atan2(rot.m21, rot.m11);
            return true;
        }

        private void FactoringZXY(Quaternion q, out float xRot, out float yRot, out float zRot)
        {
            Matrix4x4 rot = Matrix4x4.Rotate(q.normalized);
            float m32 = rot.m12;
            if (m32 > 1f - 1.0e-4f || m32 < -1f + 1.0e-4f)
            {
                xRot = m32 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                zRot = 0;
                yRot = Mathf.Atan2(-rot.m20, rot.m00);
                return;
            }
            xRot = -Mathf.Asin(m32);
            float cosX = Mathf.Cos(xRot);
            zRot = Mathf.Asin(rot.m10 / cosX);
            if (float.IsNaN(zRot))
            {
                xRot = m32 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                zRot = 0;
                yRot = Mathf.Atan2(-rot.m20, rot.m00);
                return;
            }
            if (rot.m11 < 0)
                zRot = Mathf.PI - zRot;
            yRot = Mathf.Atan2(rot.m02, rot.m22);
        }
    }
}
/*
1) CCDの回転適用が逆（最重要）

いま：

Quaternion rotQ = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, rotationAxis).normalized;
Quaternion currentRot = GetBoneRotation(linkCtx);
Quaternion newRot = rotQ * currentRot;


ここで rotationAxis は linkローカル空間で作っている（toLinkLocal で effectorLocal/targetLocal を作って cross している）ので、rotQ は「ローカル座標でのΔ回転」である。

Unityのローカル回転に「ローカルΔ」を足すなら通常は：

newRot = currentRot * rotQ;


rotQ * currentRot は「親側（あるいは別基準）からの前掛け」になりやすく、制限軸・ヒンジ軸と噛み合わず、特定姿勢で暴れる典型パターンになる。

修正案
Quaternion newRot = currentRot * rotQ;


※もし「前掛けが正しい」設計にしたいなら、axis/rotQ を **同じ空間（親空間）**に持ち上げてから前掛けする必要がある。現状は「axisはローカル、適用は前掛け」になっていて座標系が食い違っている。

2) IKレイヤーの逆算も掛け順が逆の疑い（かなり致命的）

いま：

Quaternion vmdDelta = GetVMDDelta(bpd);
Quaternion ikDelta = newRot * Quaternion.Inverse(vmdDelta);
bpd.SetLayerRotation("IK", ikDelta);


ここは BonePoseData の合成順が不明だが、一般に

合成が total = VMD * IK なら
IK = inv(VMD) * total

合成が total = IK * VMD なら
IK = total * inv(VMD)

である。

あなたのコメントから「MikuMikuFlex準拠」を狙っているなら、多くの場合は アニメ（VMD）にIKを“上書き補正”として後段で足す構造になり、合成は total = VMD * IK 側になりがちである。
その場合、今の式は逆で、正しくは：

Quaternion ikDelta = Quaternion.Inverse(vmdDelta) * newRot;


ここが逆だと、VMDが回っているフレームほどIKが逆方向に補正され、ある角度を跨いだ瞬間に破綻しやすい。

すぐ出来る検証

VMDが完全にidentityの状態（vmdDelta=I）でIKだけ動かす
→ このときはどちらの式でも結果は同じになり、問題が出にくい

VMDが回っている状態でIKを掛ける
→ 逆順だと急に破綻頻度が増える

3) 180度付近（v1 ≒ -v2）の「軸が立たない」問題が残っている

いまは：

rotationAxis = cross(v1,v2);
if (rotationAxis.sqrMagnitude < 1e-10f) skip;


v1 と v2 がほぼ反対向き（180°）だと cross は 0 に近づくが、角度はπで「本当は回す必要がある」。
ここを skip すると、反転回避が効かず別リンクに負担が飛び、連鎖的に崩れる。

修正案（簡易）

crossが小さい & dotが負（ほぼ180°）なら、任意の直交軸を作って回す（ただしヒンジならヒンジ軸に寄せる）

4) 「HasLimit=膝」なのに回転軸をヒンジ軸へ射影していない

現状は3軸自由に回してから RestrictRotation() でEulerクランプしている。
これでも動くことはあるが、CCDは反復なので、

その場で作った回転（3D）

後からクランプで潰される回転（別物）

が毎ステップ発生し、収束せず振動しやすい。MMDの膝が安定するのは「最初からヒンジ成分だけ回す」寄りだからである。

修正案（膝リンクだけでも）

膝なら rotationAxis = Vector3.right に固定し、signedAngle をその軸周りで求める

その角度だけ回す（最初から1軸CCD）

まず直すべき優先順位（効果が大きい順）

newRot = currentRot * rotQ にする（CCD適用の掛け順）

ikDelta = inv(vmdDelta) * newRot を試す（レイヤー逆算の掛け順）

180°近傍のfallback軸

膝リンクだけでも「ヒンジ軸限定CCD」

次の一手

BonePoseData の「レイヤー合成順」が分かると 2) は確定できる。
BonePoseData.Rotation が実際にどう合成しているか（VMD→IK の順か、逆か）だけ教えてくれれば、SetBoneRotation の式を断言できる。

ジンバルロックの回避。

*/