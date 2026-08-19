// Editor/AvatarBuilder/HumanoidSupplementBuilder.cs
// ============================================================
// Humanoid 必須関節のダミー補完
// ============================================================
//
// 【役割】
//   Export 済みのヒエラルキー（root）と Humanoid 割当（map）を受け取り、
//   HumanTrait.RequiredBone が要求するのに割当が無いボーンを
//   空の GameObject（ダミー関節）で補って map へ追加する。
//   欠損したモデル（下半身丸ごと無い／片腕が無い／首・頭が無い 等）でも
//   AvatarBuildCore.BuildAndSaveAvatar が中止しないようにするのが目的。
//
// 【補完対象】
//   HumanTrait.RequiredBone(i) == true のボーンのみ。
//   任意ボーン（UpperChest / Neck / Shoulder / Toes / 指 等）は、
//   ・既に割当があれば「アンカー」として位置計算に使う
//   ・割当が無ければ存在しないものとして詰める（ダミーは作らない）
//
// 【位置の決め方】チェーン（近位→遠位）ごとに欠損区間を見て決める。
//   (a) 前後にアンカーがある      → 区間を欠損数+1で等分（Vector3.Lerp）
//   (b) 前アンカーのみ（末端欠損）→ 前アンカーから既定方向へ Step ずつ外挿
//   (c) 後アンカーのみ（先頭欠損）→ 後アンカーから既定方向の逆へ Step ずつ
//   (d) どちらも無い              → root 位置から既定方向へ Step ずつ
//
// 【既定方向】このワーキング空間は左＝-X／右＝+X（StickRobot の実データで
//   左腕 X=-0.185 / 右腕 X=+0.185、左足 X=-0.09 / 右足 X=+0.09）。よって
//   体幹 = +Y / 脚 = -Y / 左腕 = -X / 右腕 = +X。
//
// 【階層】
//   ダミーは直近の近位 Transform の子として作る。
//   生成後、チェーン全体を近位→遠位に見て親子を張り直す
//   （SetParent(worldPositionStays: true)）。Avatar は Humanoid ボーンの
//   親子関係が Transform 階層と一致している必要があるため。
//   例1: Hips 欠損時は既存 Spine を新 Hips の下へ移す。
//   例2: Spine を補完したとき、欠損していない 左腕 が Hips 直下のままだと
//        Spine の子孫にならないので、腕チェーンの整合で Spine の下へ移す。
//   ワールド位置は不変なので、SkinnedMeshRenderer の bones/bindposes は影響を受けない。
//
// 【Undo】
//   呼び出し元（HierarchyExportWindow.ExportAsPrefab）の root は
//   プレファブ化後に破棄する一時オブジェクトのため Undo 登録しない。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.EditorIO
{
    /// <summary>不足する Humanoid 必須関節をダミー GameObject で補う。</summary>
    public static class HumanoidSupplementBuilder
    {
        /// <summary>外挿1段あたりの移動量（m）。</summary>
        public const float Step = 0.1f;

        /// <summary>ダミー GameObject 名の接尾辞。</summary>
        private const string DummySuffix = "_dummy";

        // チェーン上の1要素。Tf が null なら未割当。
        private sealed class Item
        {
            public string Name;
            public Transform Tf;
        }

        // 補完チェーン定義（近位→遠位）。
        private struct ChainDef
        {
            public string Label;
            public string[] ProximalAnchors; // 近位アンカー候補（優先順）。null ならチェーン先頭も補完対象
            public string[] Bones;           // 補完対象になりうる並び
            public Vector3 Dir;              // 外挿方向
        }

        // 処理順は体幹→脚→腕。腕・脚は体幹側のアンカー（Hips/Chest 等）を先に
        // 確定させてから引く必要があるため、この順序を変えないこと。
        private static readonly ChainDef[] Chains = new ChainDef[]
        {
            new ChainDef
            {
                Label = "体幹",
                ProximalAnchors = null,
                Bones = new[] { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head" },
                Dir = new Vector3(0f, 1f, 0f)
            },
            new ChainDef
            {
                Label = "左脚",
                ProximalAnchors = new[] { "Hips" },
                Bones = new[] { "LeftUpperLeg", "LeftLowerLeg", "LeftFoot" },
                Dir = new Vector3(0f, -1f, 0f)
            },
            new ChainDef
            {
                Label = "右脚",
                ProximalAnchors = new[] { "Hips" },
                Bones = new[] { "RightUpperLeg", "RightLowerLeg", "RightFoot" },
                Dir = new Vector3(0f, -1f, 0f)
            },
            new ChainDef
            {
                Label = "左腕",
                ProximalAnchors = new[] { "UpperChest", "Chest", "Spine", "Hips" },
                Bones = new[] { "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand" },
                Dir = new Vector3(-1f, 0f, 0f)
            },
            new ChainDef
            {
                Label = "右腕",
                ProximalAnchors = new[] { "UpperChest", "Chest", "Spine", "Hips" },
                Bones = new[] { "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand" },
                Dir = new Vector3(1f, 0f, 0f)
            }
        };

        /// <summary>
        /// root 配下に不足する必須関節をダミーで追加し、map（humanName→GameObject 名）を更新する。
        /// 戻り値は補完した関節数。
        /// </summary>
        public static int Supplement(
            GameObject root,
            Dictionary<string, string> map,
            Action<string> log,
            Action<string> warn)
        {
            if (root == null || map == null) return 0;

            // 名前→Transform（先勝ち）。重複の検査は呼び出し側の
            // ValidateHumanoidBoneNames が補完後に行う。
            var byName = BuildNameIndex(root);
            var used = new HashSet<string>(byName.Keys);

            // humanName→Transform。ダミー生成のたびに追加していく。
            var resolved = new Dictionary<string, Transform>();
            foreach (var kv in map)
            {
                if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
                if (byName.TryGetValue(kv.Value, out var tf) && tf != null)
                    resolved[kv.Key] = tf;
            }

            var added = new List<string>();

            for (int c = 0; c < Chains.Length; c++)
                ProcessChain(root, Chains[c], map, resolved, used, added, warn);

            if (added.Count > 0)
                log?.Invoke($"不足する必須関節を {added.Count} 件補完:\n  " + string.Join("\n  ", added));

            // 補完後も埋まらない必須ボーンがあれば通知する（Avatar 生成はこの後で失敗する）。
            var stillMissing = new List<string>();
            for (int i = 0; i < HumanTrait.BoneCount; i++)
                if (HumanTrait.RequiredBone(i) && !map.ContainsKey(HumanTrait.BoneName[i]))
                    stillMissing.Add(HumanTrait.BoneName[i]);

            if (stillMissing.Count > 0)
                warn?.Invoke("補完後も不足している必須ボーン:\n  " + string.Join("\n  ", stillMissing));

            return added.Count;
        }

        // ================================================================
        // チェーン1本の処理
        // ================================================================

        private static void ProcessChain(
            GameObject root,
            ChainDef chain,
            Dictionary<string, string> map,
            Dictionary<string, Transform> resolved,
            HashSet<string> used,
            List<string> added,
            Action<string> warn)
        {
            var items = new List<Item>();

            // 近位アンカー（補完対象にはしない）。
            if (chain.ProximalAnchors != null)
            {
                Transform anchorTf = null;
                string anchorName = null;

                for (int a = 0; a < chain.ProximalAnchors.Length; a++)
                {
                    if (resolved.TryGetValue(chain.ProximalAnchors[a], out var tf) && tf != null)
                    {
                        anchorTf = tf;
                        anchorName = chain.ProximalAnchors[a];
                        break;
                    }
                }

                if (anchorTf == null)
                {
                    warn?.Invoke(
                        $"{chain.Label}: 近位アンカー（{string.Join("/", chain.ProximalAnchors)}）が" +
                        "見つからないため補完しません。");
                    return;
                }

                items.Add(new Item { Name = anchorName, Tf = anchorTf });
            }

            // 補完対象になりうる並び。割当が無い任意ボーンは詰める。
            for (int b = 0; b < chain.Bones.Length; b++)
            {
                string name = chain.Bones[b];
                resolved.TryGetValue(name, out var tf);

                if (tf == null && !IsRequired(name)) continue;

                items.Add(new Item { Name = name, Tf = tf });
            }

            // 欠損区間ごとに位置を決めて生成する。
            int i = 0;
            while (i < items.Count)
            {
                if (items[i].Tf != null) { i++; continue; }

                int j = i;
                while (j < items.Count && items[j].Tf == null) j++;

                Transform prev = i > 0 ? items[i - 1].Tf : null;
                Transform next = j < items.Count ? items[j].Tf : null;
                int n = j - i;

                var positions = new Vector3[n];
                if (prev != null && next != null)
                {
                    for (int k = 0; k < n; k++)
                        positions[k] = Vector3.Lerp(prev.position, next.position, (k + 1f) / (n + 1f));
                }
                else if (prev != null)
                {
                    for (int k = 0; k < n; k++)
                        positions[k] = prev.position + chain.Dir * (Step * (k + 1));
                }
                else if (next != null)
                {
                    for (int k = 0; k < n; k++)
                        positions[k] = next.position - chain.Dir * (Step * (n - k));
                }
                else
                {
                    for (int k = 0; k < n; k++)
                        positions[k] = root.transform.position + chain.Dir * (Step * k);
                }

                // 生成先の親。先頭欠損のときは遠位側アンカーの現在の親へ挿し込む
                //   （遠位側の付け替えはチェーン全体の整合パスで行う）。
                Transform parent = prev != null
                    ? prev
                    : (next != null && next.parent != null ? next.parent : root.transform);

                for (int k = 0; k < n; k++)
                {
                    string goName = MakeUniqueName(items[i + k].Name + DummySuffix, used);

                    var go = new GameObject(goName);
                    go.transform.SetParent(parent, worldPositionStays: false);
                    go.transform.position   = positions[k];
                    go.transform.rotation   = Quaternion.identity;
                    go.transform.localScale = Vector3.one;

                    items[i + k].Tf = go.transform;
                    resolved[items[i + k].Name] = go.transform;
                    map[items[i + k].Name] = goName;

                    added.Add($"{items[i + k].Name} → {goName} {positions[k]}");
                    parent = go.transform;
                }

                i = j;
            }

            // 祖先関係の整合。
            //   Avatar は Humanoid ボーンの並び順どおりの祖先関係を要求する。
            //   ダミーを挟んだ区間だけでなく、区間の外に居る既存ノードも
            //   （例: Spine を補完したのに 左腕 が Hips 直下のまま）取りこぼすため、
            //   チェーン全体を近位→遠位に見て親子を張り直す。
            //   ワールド位置は保つので形状・見た目は変わらない。
            for (int k = 1; k < items.Count; k++)
            {
                Transform parentTf = items[k - 1].Tf;
                Transform childTf  = items[k].Tf;
                if (parentTf == null || childTf == null) continue;
                if (childTf.parent == parentTf) continue;

                if (IsSelfOrDescendantOf(parentTf, childTf))
                {
                    warn?.Invoke(
                        $"{chain.Label}: {items[k - 1].Name} が {items[k].Name} の子孫にあたるため" +
                        "付け替えません（Avatar の親子関係が不正になります）。");
                    continue;
                }

                childTf.SetParent(parentTf, worldPositionStays: true);
            }
        }

        // ================================================================
        // 補助
        // ================================================================

        // HumanTrait 上で必須指定されているボーンか。
        private static bool IsRequired(string traitName)
        {
            int index = Array.IndexOf(HumanTrait.BoneName, traitName);
            return index >= 0 && HumanTrait.RequiredBone(index);
        }

        // root 配下の名前→Transform（先勝ち）。
        private static Dictionary<string, Transform> BuildNameIndex(GameObject root)
        {
            var byName = new Dictionary<string, Transform>();

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName[t.name] = t;

            return byName;
        }

        // used に含まれない名前を返し、使用済みとして登録する。
        private static string MakeUniqueName(string baseName, HashSet<string> used)
        {
            string name = string.IsNullOrEmpty(baseName) ? "Joint" : baseName;
            if (used.Add(name)) return name;

            for (int n = 1; ; n++)
            {
                string candidate = $"{name}_{n}";
                if (used.Add(candidate)) return candidate;
            }
        }

        // target 自身か、その子孫に self が含まれるか（循環付け替えの検査用）。
        private static bool IsSelfOrDescendantOf(Transform self, Transform target)
        {
            for (var t = self; t != null; t = t.parent)
                if (t == target) return true;

            return false;
        }
    }
}
