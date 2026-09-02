// Runtime/Poly_Ling_Main/HierarchyIO/HierarchyPhysicsBuilder.cs
// ============================================================
// 剛体 / JOINT → Unity 物理部品
// ============================================================
//
// 【分離規約】規約は HierarchyBuilder.cs 冒頭のコメントを正典とする。
//
// 方針：Unityネイティブ部品へマップ（剛体→Rigidbody＋Collider、JOINT→ConfigurableJoint）。
//   Group / CollisionMask / PhysicsMode / NameEnglish / JointType 等の
//   Unity非対応パラメータはヒエラルキーには出さない（非破壊の正本はプロジェクトファイル側）。
//
// 座標：RigidBodyData / JointData の Position/Rotation/Size は PMXImport 時に
//   working空間へ変換済み（頂点・ボーンと同一空間）。よって追加変換は不要で、
//   ボーンと同様に world 座標へそのまま適用する（Rotation のみ rad→deg）。
//
// 【移植元】
//   Editor/HierarchyIO/HierarchyExportWindow.cs の ExportPhysics / AttachCollider。
//   ロジックは移設時に変更していない。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.HierarchyIO
{
    /// <summary>剛体・JOINT を GameObject 階層へ書き出す。</summary>
    public static class HierarchyPhysicsBuilder
    {
        /// <summary>剛体 GameObject に付ける接尾辞（ボーン名との衝突回避）。</summary>
        public const string RigidBodyNameSuffix = "_RB";

        public static void Build(
            ModelContext model, GameObject rootGo,
            Dictionary<int, Transform> boneTransformMap,
            HierarchyBuildResult result)
        {
            if (model == null || rootGo == null) return;

            // ボーン名 → Transform（関連ボーン解決用。先勝ち）
            var boneByName = new Dictionary<string, Transform>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                if (!boneTransformMap.TryGetValue(i, out var tf)) continue;
                if (!string.IsNullOrEmpty(mc.Name) && !boneByName.ContainsKey(mc.Name))
                    boneByName[mc.Name] = tf;
            }

            // ── 剛体 ──────────────────────────────────────────────────
            // 剛体 GameObject 名がボーン名と衝突すると、Humanoid 割当先の
            // Transform 名が一意でなくなり AvatarBuilder が失敗する。
            //   例）ボーン「頭」の配下に剛体「頭」→ Ambiguous Transform
            // よって接尾辞を付け、さらに既存名と重ならないよう一意化する。
            var usedNames = HierarchyBuilder.CollectHierarchyNames(rootGo);

            GameObject rigidFolder = null;
            var rigidbodyByName = new Dictionary<string, Rigidbody>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.RigidBody) continue;
                var rb = mc.MeshObject?.RigidBodyData;
                if (rb == null) continue;

                // メッシュ側と同じ規則：衝突した時だけ接尾辞を付ける。
                //   無条件に付けると、ヒエラルキーインポートで名前を取り込んだ後に
                //   再エクスポートするたび "_RB_RB" と伸びていく。
                string rawName = string.IsNullOrEmpty(mc.Name) ? $"RigidBody_{i}" : mc.Name;
                string goName  = usedNames.Contains(rawName)
                    ? HierarchyBuilder.MakeUniqueName(rawName + RigidBodyNameSuffix, usedNames)
                    : HierarchyBuilder.MakeUniqueName(rawName, usedNames);

                var go = new GameObject(goName);
                PLEditorBridge.I.RegisterCreatedObjectUndo(go, "Create RigidBody");

                // 親：関連ボーン配下（解決時）。未解決は root 直下 "RigidBodies" フォルダ
                Transform parent;
                if (!string.IsNullOrEmpty(rb.RelatedBoneName) && boneByName.TryGetValue(rb.RelatedBoneName, out var boneTf))
                {
                    parent = boneTf;
                }
                else
                {
                    if (rigidFolder == null)
                    {
                        rigidFolder = new GameObject("RigidBodies");
                        PLEditorBridge.I.RegisterCreatedObjectUndo(rigidFolder, "Create RigidBodies");
                        rigidFolder.transform.SetParent(rootGo.transform, worldPositionStays: false);
                    }
                    parent = rigidFolder.transform;
                }
                go.transform.SetParent(parent, worldPositionStays: false);

                // working空間の値を world 座標として適用
                go.transform.position = rb.Position;
                go.transform.rotation = Quaternion.Euler(rb.Rotation * Mathf.Rad2Deg);

                AttachCollider(go, rb);

                var body = PLEditorBridge.I.AddComponent<Rigidbody>(go);
                body.mass           = rb.Mass;
                body.linearDamping  = rb.LinearDamping;
                body.angularDamping = rb.AngularDamping;
                body.isKinematic    = (rb.PhysicsMode == RigidBodyPhysicsMode.FollowBone);
                // 反発/摩擦(Restitution/Friction)は v1 では PhysicsMaterial 未割当（必要なら別途）。

                if (!string.IsNullOrEmpty(mc.Name) && !rigidbodyByName.ContainsKey(mc.Name))
                    rigidbodyByName[mc.Name] = body;
            }

            // ── JOINT ─────────────────────────────────────────────────
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.RigidBodyJoint) continue;
                var jd = mc.MeshObject?.JointData;
                if (jd == null) continue;

                Rigidbody bodyA = null, bodyB = null;
                if (!string.IsNullOrEmpty(jd.BodyAName)) rigidbodyByName.TryGetValue(jd.BodyAName, out bodyA);
                if (!string.IsNullOrEmpty(jd.BodyBName)) rigidbodyByName.TryGetValue(jd.BodyBName, out bodyB);

                // ConfigurableJoint は Rigidbody を持つ GO に付与。基準＝剛体A（無ければ剛体B）。
                Rigidbody host      = bodyA != null ? bodyA : bodyB;
                Rigidbody connected = bodyA != null ? bodyB : bodyA;
                if (host == null)
                {
                    result?.Warn($"JOINT '{mc.Name}' の接続剛体が見つからずスキップ。");
                    continue;
                }

                var joint = PLEditorBridge.I.AddComponent<ConfigurableJoint>(host.gameObject);
                joint.connectedBody = connected; // null可（ワールド固定）
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = host.transform.InverseTransformPoint(jd.Position);
                joint.connectedAnchor = connected != null
                    ? connected.transform.InverseTransformPoint(jd.Position)
                    : jd.Position;

                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Limited;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;

                // ============================================================
                // 【注記：座標軸リマップ未実施（段階②③からの保留）】
                //   TranslationMin/Max・RotationMin/Max・SpringTranslation/Rotation は raw値。
                //   PMX（左手・モデル-Z向き）軸と Unity ConfigurableJoint 軸の対応リマップは未実施。
                //   加えて ConfigurableJoint の並進リミットは軸別 min/max ではなく単一の対称リミット
                //   のため、PMX の軸別 min/max を厳密表現できない。下記は対称近似のベストエフォート。
                //   厳密な物理整合が必要なら別途、軸リマップ対応が要る。
                //   （※非破壊の正本はプロジェクトファイル側に保持済み。本出力は使用/可視化用途）
                // ============================================================
                joint.lowAngularXLimit  = new SoftJointLimit { limit = jd.RotationMin.x * Mathf.Rad2Deg };
                joint.highAngularXLimit = new SoftJointLimit { limit = jd.RotationMax.x * Mathf.Rad2Deg };
                joint.angularYLimit = new SoftJointLimit
                {
                    limit = Mathf.Max(Mathf.Abs(jd.RotationMin.y), Mathf.Abs(jd.RotationMax.y)) * Mathf.Rad2Deg
                };
                joint.angularZLimit = new SoftJointLimit
                {
                    limit = Mathf.Max(Mathf.Abs(jd.RotationMin.z), Mathf.Abs(jd.RotationMax.z)) * Mathf.Rad2Deg
                };

                float linMax = Mathf.Max(
                    Mathf.Max(Mathf.Abs(jd.TranslationMin.x), Mathf.Abs(jd.TranslationMax.x)),
                    Mathf.Max(
                        Mathf.Max(Mathf.Abs(jd.TranslationMin.y), Mathf.Abs(jd.TranslationMax.y)),
                        Mathf.Max(Mathf.Abs(jd.TranslationMin.z), Mathf.Abs(jd.TranslationMax.z))));
                joint.linearLimit = new SoftJointLimit { limit = linMax };

                joint.linearLimitSpring = new SoftJointLimitSpring
                {
                    spring = Mathf.Max(jd.SpringTranslation.x, Mathf.Max(jd.SpringTranslation.y, jd.SpringTranslation.z))
                };
                joint.angularXLimitSpring  = new SoftJointLimitSpring { spring = jd.SpringRotation.x };
                joint.angularYZLimitSpring = new SoftJointLimitSpring { spring = Mathf.Max(jd.SpringRotation.y, jd.SpringRotation.z) };
            }
        }

        // ================================================================
        // Collider 付与（形状別）
        // ================================================================
        //
        // 【PMX サイズ意味の前提】
        //   球    : Size.x = 半径
        //   箱    : Size   = 半幅（half-extent）
        //   カプセル: Size.x = 半径, Size.y = 高さ（円筒部長）
        // 【Unity 換算】
        //   BoxCollider.size      = 全幅 = 半幅 × 2
        //   CapsuleCollider.height = 全高 = 円筒部長 + 半径 × 2
        //
        private static void AttachCollider(GameObject go, RigidBodyData rb)
        {
            switch (rb.Shape)
            {
                case RigidBodyShape.Sphere:
                {
                    var c = PLEditorBridge.I.AddComponent<SphereCollider>(go);
                    c.radius = rb.Size.x;
                    break;
                }
                case RigidBodyShape.Box:
                {
                    var c = PLEditorBridge.I.AddComponent<BoxCollider>(go);
                    c.size = rb.Size * 2f;
                    break;
                }
                case RigidBodyShape.Capsule:
                {
                    var c = PLEditorBridge.I.AddComponent<CapsuleCollider>(go);
                    c.radius    = rb.Size.x;
                    c.height    = rb.Size.y + rb.Size.x * 2f;
                    c.direction = 1; // Y軸
                    break;
                }
            }
        }
    }
}
