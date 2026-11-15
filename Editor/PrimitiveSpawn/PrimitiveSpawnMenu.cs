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

        // ── 追加分（分類ごとの下位メニュー） ──────────────────────────
        //   下位メニューの並び位置は、その中で最小の priority で決まる。
        //   6 図形（10〜15）の下、パネル（30）の上に置くため 16〜 を使う。
        private const string General = Root + "一般形状/";
        private const string Gear    = Root + "歯車/";
        private const string Mech    = Root + "機構部品/";
        private const string Symbol  = Root + "記号/";

        [MenuItem(General + "Stadium Box",  false, Priority + 6)]
        private static void CreateStadiumBox(MenuCommand cmd)  => Create(PrimitiveSpawnIds.StadiumBox, cmd);
        [MenuItem(General + "Pipe Stadium", false, Priority + 6)]
        private static void CreatePipeStadium(MenuCommand cmd) => Create(PrimitiveSpawnIds.PipeStadium, cmd);
        [MenuItem(General + "Star",         false, Priority + 6)]
        private static void CreateNGonStar(MenuCommand cmd)    => Create(PrimitiveSpawnIds.NGonStar, cmd);
        [MenuItem(General + "Ribbon Bow",   false, Priority + 6)]
        private static void CreateRibbonBow(MenuCommand cmd)   => Create(PrimitiveSpawnIds.RibbonBow, cmd);

        [MenuItem(Gear + "N-Gon Gear",          false, Priority + 7)]
        private static void CreateNGonGear(MenuCommand cmd)          => Create(PrimitiveSpawnIds.NGonGear, cmd);
        [MenuItem(Gear + "Involute Gear",       false, Priority + 7)]
        private static void CreateInvoluteGear(MenuCommand cmd)      => Create(PrimitiveSpawnIds.InvoluteGear, cmd);
        [MenuItem(Gear + "Helical Gear",        false, Priority + 7)]
        private static void CreateHelicalGear(MenuCommand cmd)       => Create(PrimitiveSpawnIds.HelicalGear, cmd);
        [MenuItem(Gear + "Internal Gear",       false, Priority + 7)]
        private static void CreateInternalGear(MenuCommand cmd)      => Create(PrimitiveSpawnIds.InternalGear, cmd);
        [MenuItem(Gear + "Involute Rack",       false, Priority + 7)]
        private static void CreateInvoluteRack(MenuCommand cmd)      => Create(PrimitiveSpawnIds.InvoluteRack, cmd);
        [MenuItem(Gear + "Helical Rack",        false, Priority + 7)]
        private static void CreateHelicalRack(MenuCommand cmd)       => Create(PrimitiveSpawnIds.HelicalRack, cmd);
        [MenuItem(Gear + "Straight Bevel Gear", false, Priority + 7)]
        private static void CreateStraightBevelGear(MenuCommand cmd) => Create(PrimitiveSpawnIds.StraightBevelGear, cmd);
        [MenuItem(Gear + "Spiral Bevel Gear",   false, Priority + 7)]
        private static void CreateSpiralBevelGear(MenuCommand cmd)   => Create(PrimitiveSpawnIds.SpiralBevelGear, cmd);
        [MenuItem(Gear + "Worm",                false, Priority + 7)]
        private static void CreateCylindricalWorm(MenuCommand cmd)   => Create(PrimitiveSpawnIds.CylindricalWorm, cmd);
        [MenuItem(Gear + "Worm Wheel",          false, Priority + 7)]
        private static void CreateWormWheel(MenuCommand cmd)         => Create(PrimitiveSpawnIds.WormWheel, cmd);

        [MenuItem(Mech + "Nut", false, Priority + 8)]
        private static void CreateNut(MenuCommand cmd) => Create(PrimitiveSpawnIds.Nut, cmd);

        [MenuItem(Symbol + "Zodiac Sign", false, Priority + 9)]
        private static void CreateZodiacSign(MenuCommand cmd) => Create(PrimitiveSpawnIds.ZodiacSign, cmd);
        [MenuItem(Symbol + "Emblem",      false, Priority + 9)]
        private static void CreateEmblem(MenuCommand cmd)     => Create(PrimitiveSpawnIds.Emblem, cmd);

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
