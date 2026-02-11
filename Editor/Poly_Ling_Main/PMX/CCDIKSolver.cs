// CCDIKSolver.cs
// CCD法によるIKソルバー（MikuMikuFlex準拠）
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

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.VMD
{
    public class CCDIKSolver
    {
        /// <summary>
        /// デバッグログの有効/無効。falseなら一切ログを出さない。
        /// trueの場合、左足ＩＫのフレーム128〜131のみログ出力。
        /// </summary>
        public bool DebugEnabled = false;

        /// <summary>
        /// IK解決前に膝リンクへ微小曲げを付与する。
        /// 直線状態での回転軸不定による膝反転を防止するMMD標準テクニック。
        /// </summary>
        public bool KneePreBend = false;// true;

        public void Solve(Model.ModelContext model, float frameNumber = -1f)
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

            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx == null) continue;
                if (!ctx.IsIK || ctx.IKLinks == null || ctx.IKLinks.Count == 0)
                    continue;
                if (ctx.IKTargetIndex < 0 || ctx.IKTargetIndex >= model.MeshContextList.Count)
                    continue;

                SolveIKBone(model, i, frameNumber);
            }
        }

        private void SolveIKBone(Model.ModelContext model, int ikBoneIndex, float frameNumber)
        {
            var ikBone = model.MeshContextList[ikBoneIndex];

            // デバッグ条件: フラグON かつ 左足ＩＫ かつ フレーム128〜131
            bool debugLog = DebugEnabled
                && ikBone.Name != null
                && ikBone.Name.Contains("左足ＩＫ")
                && frameNumber >= 128f && frameNumber <= 131f;

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
            if (KneePreBend)
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
                        Quaternion preBendRot = Quaternion.AngleAxis(bendAngle * Mathf.Rad2Deg, Vector3.right) * rot;
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
                    if (angle <= 1.0e-5f)
                    {
                        if (logThisIt) Debug.Log($"[CCDIK it={it}]   SKIP angle={angleBeforeClamp:F6}→{angle:F6} too small");
                        continue;
                    }

                    if (rotationAxis.sqrMagnitude < 1e-10f)
                    {
                        if (logThisIt) Debug.Log($"[CCDIK it={it}]   SKIP rotAxis too small");
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
                    if (link.HasLimit)
                    {
                        Quaternion before = newRot;
                        newRot = RestrictRotation(newRot, link.LimitMin, link.LimitMax, logThisIt);
                        if (logThisIt)
                            Debug.Log($"[CCDIK it={it}]   restrict before=({before.x:F4},{before.y:F4},{before.z:F4},{before.w:F4}) after=({newRot.x:F4},{newRot.y:F4},{newRot.z:F4},{newRot.w:F4})");
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
        /// </summary>
        private void SetBoneRotation(MeshContext ctx, Quaternion newRot)
        {
            var bpd = ctx.BonePoseData;
            bpd.IsActive = true;

            Quaternion vmdDelta = GetVMDDelta(bpd);
            //Quaternion ikDelta = newRot * Quaternion.Inverse(vmdDelta);
            Quaternion ikDelta = Quaternion.Inverse(vmdDelta) * newRot;// を試す（レイヤー逆算の掛け順）

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