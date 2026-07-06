// MuscleOrderCsvExporter.cs
// 拡張B: HumanPose.muscles[] の並び（HumanTrait.MuscleName）を CSV で書き出すエディタ拡張。
//
// 目的:
//   拡張A の JSON の muscles[].name（＝Animator バインディングの propertyName。
//   実データ確認により index 35–129 の95件が HumanTrait.MuscleName と一致）を、
//   ランタイムで HumanPose.muscles[i]（i = 0..MuscleCount-1）へ対応づけるための
//   固定並び表。手編集可能。
//
// CSV 仕様 (UnityMuscle CSV v1):
//   1行目  ;UnityMuscleCSV,version,1,count,<MuscleCount>   （メタ行・コメント）
//   ヘッダ  Muscle,Index,Name
//   各行    Muscle,<i>,"<MuscleName[i]>"    （i = 0..MuscleCount-1）
//   - Index : HumanPose.muscles[] の添字
//   - Name  : HumanTrait.MuscleName[Index]（そのまま）
//   文字コード : UTF-8（BOM 付き）
//
// 注:
//   マッスルの並び・件数は HumanTrait 由来でアバター非依存（Avatar は不要）。
//   この CSV は「並び（name→index）」の記録・手編集用。

using System.Text;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.UnityClipEditor
{
    public class MuscleOrderCsvExporter : EditorWindow
    {
        bool utf8Bom = true;

        [MenuItem("PolyLing/UnityClip/Muscle順 CSV 書き出し")]
        static void Open()
        {
            var w = GetWindow<MuscleOrderCsvExporter>("Muscle順 CSV");
            w.minSize = new Vector2(340, 140);
            w.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("HumanPose.muscles 並び → CSV", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"MuscleCount: {HumanTrait.MuscleCount}");
            utf8Bom = EditorGUILayout.ToggleLeft("UTF-8 BOM を付ける", utf8Bom);

            EditorGUILayout.Space();
            if (GUILayout.Button("CSV 書き出し", GUILayout.Height(28))) Export();

            EditorGUILayout.HelpBox(
                "HumanTrait.MuscleName の並び（アバター非依存）を書き出します。\n" +
                "拡張A の JSON の muscles[].name をこの Index に対応づける固定表です。",
                MessageType.Info);
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            s = s.Replace("\"", "\"\"");
            return "\"" + s + "\"";
        }

        void Export()
        {
            int count = HumanTrait.MuscleCount;
            var names = HumanTrait.MuscleName;

            var sb = new StringBuilder();
            sb.Append(";UnityMuscleCSV,version,1,count,").Append(count).Append('\n');
            sb.Append("Muscle,Index,Name\n");
            for (int i = 0; i < count; i++)
            {
                string name = (names != null && i < names.Length) ? names[i] : "";
                sb.Append("Muscle,").Append(i).Append(',').Append(Esc(name)).Append('\n');
            }

            string path = EditorUtility.SaveFilePanel("UnityMuscle CSV を保存", "", "muscle_order.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var enc = new UTF8Encoding(utf8Bom);
            System.IO.File.WriteAllText(path, sb.ToString(), enc);

            string msg = $"書き出し完了: {count} マッスル";
            Debug.Log("[MuscleOrderCsvExporter] " + msg + "\n" + path);
            EditorUtility.DisplayDialog("Muscle順 CSV 書き出し", msg, "OK");
        }
    }
}
