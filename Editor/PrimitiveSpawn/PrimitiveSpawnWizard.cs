// PrimitiveSpawnWizard.cs
// ============================================================
// 基本図形の作成パネル
// ============================================================
//
// 【役割】
//   図形・ピボット・スケールを指定して配置する。
//   Unity 標準の GameObject/3D Object/Ragdoll... と同じ ScriptableWizard 形式。
//
// 【設定値の保持】
//   静的フィールドに持つため、パネルを開き直しても前回値が残る
//   （ドメインリロードで既定値へ戻る）。
//
// 【図形リスト】
//   PrimitiveSpawnRegistry を直接引くので、図形を Register すれば
//   このファイルを変更せずに選択肢へ出る。
//
// 【配置】 Editor/PrimitiveSpawn/
// ============================================================

using UnityEditor;
using UnityEngine;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.EditorPrimitive
{
    public class PrimitiveSpawnWizard : ScriptableWizard
    {
        // PrimitivePivotMode の宣言順（Bottom=0, Center=1）と対応させること。
        private static readonly string[] PivotLabels = { "下（底面）", "中心" };

        // ── 前回値（ドメインリロードまで保持）───────────────────────
        private static string             _shapeId = PrimitiveSpawnIds.Cube;
        private static PrimitivePivotMode _pivot   = PrimitivePivotMode.Bottom;
        private static Vector3            _scale   = Vector3.one;

        // 開いた時点の親。パネル上で差し替えできる。
        private GameObject _parent;

        /// <summary>パネルを開く。parent は null 可（ルート直下に配置）。</summary>
        public static void Open(GameObject parent)
        {
            var wizard = DisplayWizard<PrimitiveSpawnWizard>("基本図形の作成", "作成");
            wizard._parent = parent;
        }

        // ============================================================
        // GUI
        // ============================================================

        protected override bool DrawWizardGUI()
        {
            EditorGUI.BeginChangeCheck();

            _parent = (GameObject)EditorGUILayout.ObjectField(
                "親", _parent, typeof(GameObject), allowSceneObjects: true);

            DrawShapePopup();

            _pivot = (PrimitivePivotMode)EditorGUILayout.Popup(
                "ピボット", (int)_pivot, PivotLabels);

            _scale = EditorGUILayout.Vector3Field("スケール", _scale);

            return EditorGUI.EndChangeCheck();
        }

        private void DrawShapePopup()
        {
            var defs = PrimitiveSpawnRegistry.All;
            if (defs.Count == 0)
            {
                EditorGUILayout.LabelField("図形", "（登録なし）");
                return;
            }

            var labels = new string[defs.Count];
            for (int i = 0; i < defs.Count; i++) labels[i] = defs[i].Label;

            int index = PrimitiveSpawnRegistry.IndexOf(_shapeId);
            if (index < 0) index = 0;

            index    = EditorGUILayout.Popup("図形", index, labels);
            _shapeId = defs[index].Id;
        }

        // ============================================================
        // 状態表示
        // ============================================================

        private void OnWizardUpdate()
        {
            helpString = "スケールは GameObject の Transform ではなく頂点座標へ焼き込みます"
                       + "（localScale は 1 のまま）。";

            errorString = BuildWarning();
            isValid     = true;   // 下限クランプ・鏡映補正で破綻しないため常に作成可
        }

        /// <summary>クランプ／鏡映が起きる入力を事前に知らせる。</summary>
        private static string BuildWarning()
        {
            var s = PrimitiveMeshTransform.SanitizeScale(_scale);

            if (s != _scale)
                return $"スケールの絶対値が下限 {PrimitiveMeshTransform.MinScaleAbs} 未満のため、"
                     + $"{s} に丸めて適用します。";

            if (s.x * s.y * s.z < 0f)
                return "負のスケール成分が奇数個のため鏡映になります（面の巻き順は自動で戻します）。";

            return "";
        }

        // ============================================================
        // 実行
        // ============================================================

        private void OnWizardCreate()
        {
            var opt = PrimitiveSpawnOptions.Default;
            opt.Pivot = _pivot;
            opt.Scale = _scale;

            PrimitiveSpawner.Spawn(_shapeId, _parent, opt);
        }
    }
}
