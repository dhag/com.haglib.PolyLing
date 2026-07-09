// UnityBoneCsvExporter.cs  —  Unityモデルのボーン階層を「UnityBone CSV v1」で書き出すエディタ拡張
//
// 使い方:
//   1) Assets/Editor/ フォルダ（無ければ作成）にこのファイルを置く
//   2) メニュー  PolyLing/UnityClip/骨CSV 書き出し (Unity→CSV)  を開く
//   3) Root に Humanoid アバターを持つモデル（Animator付き）をセット → 「CSV書き出し」
//   4) 出力された .csv を motion_timeline.html の「骨CSV読込」ボタンで読み込む
//
// CSV仕様 (UnityBone CSV v3):
//   1行目  ; から始まるメタ行（コメント）
//   列（先頭16列は v2 と同一・位置不変）
//          UnityBone, Name, NameEn, Humanoid, Parent, PosX, PosY, PosZ,
//          RestLX, RestLY, RestLZ, RestLW, RestWX, RestWY, RestWZ, RestWW
//   - Name      : Transform名（日本語可）
//   - NameEn    : 英名エイリアス（無ければ空）
//   - Humanoid  : HumanBodyBones 列挙名（例 LeftUpperArm）。Humanoid未割当は空
//   - Parent    : 親Transform名（ルートは空）
//   - PosX/Y/Z  : ルート基準の絶対位置・Unity座標（メートル, Z反転なし）
//   - RestL*/W* : レスト(束ね)姿勢の local / world(ルート相対) 回転（四元数 x,y,z,w）
//                 ※ リターゲットで A/T ポーズ差を吸収するために使用。書き出し時はモデルをレスト姿勢に。
//   - v3 追加（任意・可変加算＝custom 可動域のある行のみ末尾に付与。度）:
//          LimUseDefault, LMinX, LMinY, LMinZ, LMaxX, LMaxY, LMaxZ, LCenX, LCenY, LCenZ, LAxis
//     出力元は Avatar の humanDescription.human[].limit（度）。既定値のボーンは付与しない。
//     ※消費側（motion_timeline.html 等）は本リポジトリ外。列を読むには別途 HTML 側の対応が必要。
//   文字コード  : UTF-8（BOM付き既定。HTML側は自動判定）

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.UnityClip.Editor
{
public class UnityBoneCsvExporter : EditorWindow
{
    GameObject root;
    bool skinnedOnly = true;   // SkinnedMeshRendererのボーン+Humanoid骨のみ（false=全Transform）
    bool writeHeader = true;   // 人間可読なヘッダ行を付ける
    bool utf8Bom     = true;   // UTF-8 BOM（Excelで開く想定。HTML側はどちらでも可）

    [MenuItem("PolyLing/UnityClip/骨CSV 書き出し (Unity→CSV)")]
    static void Open()
    {
        var w = GetWindow<UnityBoneCsvExporter>("骨CSV書き出し");
        w.minSize = new Vector2(360, 200);
        if (UnityEditor.Selection.activeGameObject != null) w.root = UnityEditor.Selection.activeGameObject;
        w.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Unityモデル → UnityBone CSV v1", EditorStyles.boldLabel);
        root = (GameObject)EditorGUILayout.ObjectField("Root (Animator)", root, typeof(GameObject), true);
        skinnedOnly = EditorGUILayout.ToggleLeft("Skinnedボーン+Humanoid骨のみ（推奨）", skinnedOnly);
        writeHeader = EditorGUILayout.ToggleLeft("ヘッダ行を付ける", writeHeader);
        utf8Bom     = EditorGUILayout.ToggleLeft("UTF-8 BOM を付ける", utf8Bom);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(root == null))
        {
            if (GUILayout.Button("CSV書き出し", GUILayout.Height(28))) Export();
        }
        EditorGUILayout.HelpBox(
            "Root は Humanoid アバター（Animator）を持つモデルを指定してください。\n" +
            "Humanoid割当は Animator.GetBoneTransform から正確に取得します（推測なし）。\n" +
            "Generic の場合 Humanoid列は空になり、HTML側で名前エイリアス補完されます。\n" +
            "※ v2: レスト回転(local/world)も書き出します。書き出し時はモデルを束ね(レスト)姿勢にしてください。",
            MessageType.Info);
    }

    // ルート配下のAnimatorを取得（自身優先）
    Animator FindAnimator()
    {
        var a = root.GetComponent<Animator>();
        if (a == null) a = root.GetComponentInChildren<Animator>(true);
        return a;
    }

    // Transform → HumanBodyBones列挙名（Humanoidのみ）
    Dictionary<Transform, string> BuildHumanoidMap(Animator a)
    {
        var map = new Dictionary<Transform, string>();
        if (a == null || a.avatar == null || !a.avatar.isHuman) return map;
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            var hbb = (HumanBodyBones)i;
            var tr = a.GetBoneTransform(hbb);
            if (tr != null && !map.ContainsKey(tr)) map[tr] = hbb.ToString();
        }
        return map;
    }

    // Transform → HumanLimit（Avatar の humanDescription 由来・度）。
    //   既定値(useDefaultValues=true)のボーンは含めない（＝可変加算：limit 列なし）。
    //   humanDescription.humanName は HumanTrait.BoneName 形式。GetBoneTransform で
    //   Transform に解決してキーにする（humanMap と同じ引き方で衝突を避ける）。
    Dictionary<Transform, HumanLimit> BuildHumanLimitMap(Animator a)
    {
        var byTf = new Dictionary<Transform, HumanLimit>();
        if (a == null || a.avatar == null || !a.avatar.isHuman) return byTf;

        var byTrait = new Dictionary<string, HumanLimit>();
        var human = a.avatar.humanDescription.human;
        if (human != null)
            foreach (var hb in human)
                if (!hb.limit.useDefaultValues && !string.IsNullOrEmpty(hb.humanName))
                    byTrait[hb.humanName] = hb.limit;

        if (byTrait.Count == 0) return byTf;

        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            var tr = a.GetBoneTransform((HumanBodyBones)i);
            if (tr == null) continue;
            string traitName = HumanTrait.BoneName[i];
            if (byTrait.TryGetValue(traitName, out var lim) && !byTf.ContainsKey(tr))
                byTf[tr] = lim;
        }
        return byTf;
    }

    // 書き出し対象ボーン集合を作る
    HashSet<Transform> CollectBones(Transform rootT, Dictionary<Transform, string> humanMap)
    {
        var set = new HashSet<Transform>();
        if (!skinnedOnly)
        {
            foreach (var t in rootT.GetComponentsInChildren<Transform>(true)) set.Add(t);
            return set;
        }
        // Skinnedのボーン + rootBone
        foreach (var smr in rootT.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.rootBone != null) set.Add(smr.rootBone);
            if (smr.bones != null)
                foreach (var b in smr.bones) if (b != null) set.Add(b);
        }
        // Humanoid割当ボーン
        foreach (var kv in humanMap) set.Add(kv.Key);
        // 親チェーンを root まで補完（親名が途切れないように）
        var withAncestors = new HashSet<Transform>(set);
        foreach (var t in set)
        {
            var p = t.parent;
            while (p != null)
            {
                withAncestors.Add(p);
                if (p == rootT) break;
                p = p.parent;
            }
        }
        withAncestors.Add(rootT);
        // SkinnedMeshが無いモデルの保険: 何も集まらなければ全Transform
        if (withAncestors.Count <= 1)
        {
            withAncestors.Clear();
            foreach (var t in rootT.GetComponentsInChildren<Transform>(true)) withAncestors.Add(t);
        }
        return withAncestors;
    }

    // 親→子の順（DFS）で対象を並べる
    void DfsOrder(Transform t, HashSet<Transform> set, List<Transform> outList)
    {
        if (set.Contains(t)) outList.Add(t);
        for (int i = 0; i < t.childCount; i++) DfsOrder(t.GetChild(i), set, outList);
    }

    static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        bool need = s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0;
        s = s.Replace("\"", "\"\"");
        return need ? "\"" + s + "\"" : "\"" + s + "\"";   // 名前は常に引用（安全側）
    }

    void Export()
    {
        var rootT = root.transform;
        var animator = FindAnimator();
        var humanMap = BuildHumanoidMap(animator);
        var limitMap = BuildHumanLimitMap(animator);

        var set = CollectBones(rootT, humanMap);
        var ordered = new List<Transform>();
        DfsOrder(rootT, set, ordered);

        // 重複名チェック（HTML側は名前キーなので衝突に注意）
        var seen = new HashSet<string>();
        var dups = new HashSet<string>();
        foreach (var t in ordered) { if (!seen.Add(t.name)) dups.Add(t.name); }

        var sb = new StringBuilder();
        sb.Append(";UnityBoneCSV,version,3,space,unity,units,m,root,").Append(Esc(rootT.name)).Append('\n');
        // 先頭16列は v2 と同一（位置不変）。以降は任意の可動域列（度・custom のみ・可変加算）。
        //   LimUseDefault,LMinX,LMinY,LMinZ,LMaxX,LMaxY,LMaxZ,LCenX,LCenY,LCenZ,LAxis
        if (writeHeader) sb.Append("UnityBone,Name,NameEn,Humanoid,Parent,PosX,PosY,PosZ,RestLX,RestLY,RestLZ,RestLW,RestWX,RestWY,RestWZ,RestWW,LimUseDefault,LMinX,LMinY,LMinZ,LMaxX,LMaxY,LMaxZ,LCenX,LCenY,LCenZ,LAxis\n");

        int humCount = 0;
        int limCount = 0;
        foreach (var t in ordered)
        {
            string hum = humanMap.TryGetValue(t, out var h) ? h : "";
            if (hum.Length > 0) humCount++;
            string parent = (t.parent != null && set.Contains(t.parent) && t != rootT) ? t.parent.name : "";
            Vector3 lp = rootT.InverseTransformPoint(t.position);            // ルート基準の絶対位置
            Quaternion rl = t.localRotation;                                 // レスト・ローカル回転（束ね姿勢前提）
            Quaternion rw = Quaternion.Inverse(rootT.rotation) * t.rotation; // レスト・ワールド回転（ルート相対）＝exporterのrestModelと同一

            sb.Append("UnityBone,")
              .Append(Esc(t.name)).Append(',')
              .Append("\"\"").Append(',')          // NameEn（空）
              .Append(hum).Append(',')             // Humanoid列挙名（空可・特殊文字なし）
              .Append(Esc(parent)).Append(',')
              .Append(F(lp.x)).Append(',')
              .Append(F(lp.y)).Append(',')
              .Append(F(lp.z)).Append(',')
              .Append(F(rl.x)).Append(',').Append(F(rl.y)).Append(',').Append(F(rl.z)).Append(',').Append(F(rl.w)).Append(',')
              .Append(F(rw.x)).Append(',').Append(F(rw.y)).Append(',').Append(F(rw.z)).Append(',').Append(F(rw.w));

            // 可動域（度・custom のみ・可変加算）: A-1 と同じ加算互換スタイル。
            if (limitMap.TryGetValue(t, out var lim))
            {
                var mn = lim.min; var mx = lim.max; var ce = lim.center;
                sb.Append(",false")
                  .Append(',').Append(F(mn.x)).Append(',').Append(F(mn.y)).Append(',').Append(F(mn.z))
                  .Append(',').Append(F(mx.x)).Append(',').Append(F(mx.y)).Append(',').Append(F(mx.z))
                  .Append(',').Append(F(ce.x)).Append(',').Append(F(ce.y)).Append(',').Append(F(ce.z))
                  .Append(',').Append(F(lim.axisLength));
                limCount++;
            }
            sb.Append('\n');
        }

        string defName = (rootT.name + "_bones.csv");
        string path = EditorUtility.SaveFilePanel("UnityBone CSV を保存", "", defName, "csv");
        if (string.IsNullOrEmpty(path)) return;

        var enc = new UTF8Encoding(utf8Bom);
        System.IO.File.WriteAllText(path, sb.ToString(), enc);

        string msg = $"書き出し完了: {ordered.Count} ボーン / Humanoid割当 {humCount} / 可動域 {limCount}";
        if (dups.Count > 0) msg += $"\n注意: 同名ボーンが {dups.Count} 種あります（HTML側は名前キーのため衝突の可能性）: " + string.Join(", ", new List<string>(dups).ToArray());
        if (animator == null) msg += "\n注意: Animatorが見つかりません（Humanoid割当は空になります）";
        else if (animator.avatar == null || !animator.avatar.isHuman) msg += "\n注意: Humanoidアバターではありません（Humanoid割当は空。HTML側で名前補完されます）";

        Debug.Log("[UnityBoneCsvExporter] " + msg + "\n" + path);
        EditorUtility.DisplayDialog("骨CSV書き出し", msg, "OK");
    }
}
}
