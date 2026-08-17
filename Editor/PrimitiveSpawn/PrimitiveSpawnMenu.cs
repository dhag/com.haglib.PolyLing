// PrimitiveSpawnMenu.cs
// ============================================================
// ヒエラルキー右クリック → 基本図形の配置メニュー
// ============================================================
//
// 【メニュー位置】
//   "GameObject/..." 配下の項目は、メニューバーの GameObject と
//   ヒエラルキーの右クリックメニューの両方に出る（Unity 仕様）。
//   priority を 10 台にすると Create Empty の近く（上段）に並ぶ。
//
// 【親の決め方】
//   menuCommand.context = 右クリックされた GameObject。
//   メニューバーから呼ばれた場合は null になるので Selection.activeGameObject を使う。
//   どちらも無ければ null（＝ルート直下）。
//
// 【図形を増やすとき】
//   PrimitiveSpawnRegistry に定義を登録したうえで、ここに 1 行足す。
//   [MenuItem] は属性＝コンパイル時決定のため、レジストリだけでは項目が生えない。
//
// 【配置】 Editor/PrimitiveSpawn/
// ============================================================

using UnityEditor;
using UnityEngine;

namespace Poly_Ling.EditorPrimitive
{
    public static class PrimitiveSpawnMenu
    {
        private const string Root     = "GameObject/PolyLing/";
        private const int    Priority = 10;

        [MenuItem(Root + "Cube",     false, Priority + 0)]
        private static void CreateCube(MenuCommand cmd)     => Create(PrimitiveSpawnIds.Cube, cmd);

        [MenuItem(Root + "Sphere",   false, Priority + 1)]
        private static void CreateSphere(MenuCommand cmd)   => Create(PrimitiveSpawnIds.Sphere, cmd);

        [MenuItem(Root + "Cylinder", false, Priority + 2)]
        private static void CreateCylinder(MenuCommand cmd) => Create(PrimitiveSpawnIds.Cylinder, cmd);

        [MenuItem(Root + "Capsule",  false, Priority + 3)]
        private static void CreateCapsule(MenuCommand cmd)  => Create(PrimitiveSpawnIds.Capsule, cmd);

        [MenuItem(Root + "Plane",    false, Priority + 4)]
        private static void CreatePlane(MenuCommand cmd)    => Create(PrimitiveSpawnIds.Plane, cmd);

        [MenuItem(Root + "Pyramid",  false, Priority + 5)]
        private static void CreatePyramid(MenuCommand cmd)  => Create(PrimitiveSpawnIds.Pyramid, cmd);

        // ============================================================
        // パラメータ指定パネル
        // ============================================================
        //
        // priority を 20 空けると上の 6 項目との間に区切り線が入る。
        //
        [MenuItem(Root + "基本図形を作成...", false, Priority + 20)]
        private static void OpenWizard(MenuCommand cmd)
        {
            if (!ShouldRun(cmd)) return;
            PrimitiveSpawnWizard.Open(ResolveParent(cmd));
        }

        // ============================================================
        // 共通処理
        // ============================================================

        private static void Create(string id, MenuCommand cmd)
        {
            if (!ShouldRun(cmd)) return;
            PrimitiveSpawner.Spawn(id, ResolveParent(cmd));
        }

        /// <summary>
        /// GameObject/ 配下の MenuItem は、複数選択時に選択数ぶん呼ばれる（Unity 仕様）。
        /// アクティブ選択と一致する呼び出しだけ通し、1 回に絞る。
        ///   ※ Poly_Ling.Selection 名前空間と衝突するため UnityEditor.Selection を完全修飾する。
        /// </summary>
        private static bool ShouldRun(MenuCommand cmd)
        {
            var context = cmd.context as GameObject;
            return context == null || context == UnityEditor.Selection.activeGameObject;
        }

        /// <summary>
        /// 親を決める。右クリックされた GameObject（context）を優先し、
        /// メニューバーからの呼び出しでは選択中のオブジェクトを使う。どちらも無ければ null。
        /// </summary>
        private static GameObject ResolveParent(MenuCommand cmd)
        {
            var context = cmd.context as GameObject;
            return context != null ? context : UnityEditor.Selection.activeGameObject;
        }
    }
}
