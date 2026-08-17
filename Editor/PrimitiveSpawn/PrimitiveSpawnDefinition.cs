// PrimitiveSpawnDefinition.cs
// ============================================================
// ヒエラルキー配置用「基本図形」の定義とレジストリ
// ============================================================
//
// 【役割】
//   「どの図形を、どの生成関数で作るか」だけを保持する。
//   GameObject 生成・親子付け・Undo は PrimitiveSpawner が担当する（責務分離）。
//
// 【拡張ポイント】
//   1) パラメータを既定から変える
//        PrimitiveSpawnRegistry.Get(PrimitiveSpawnIds.Cube).Generate =
//            pivot => { var p = myCubeParams; p.Pivot = pivot; return CubeMeshGenerator.Generate(p); };
//      Generate は set 可能。設定ウィンドウや ScriptableObject を後付けする場合、
//      触るのはここ 1 点だけで済む。
//
//   2) 図形を増やす
//        PrimitiveSpawnRegistry.Register(new PrimitiveSpawnDefinition(
//            "pipe", "Pipe", pivot => { ... }));
//      ※ メニュー項目は [MenuItem] 属性＝コンパイル時決定のため、
//        PrimitiveSpawnMenu.cs にも 1 行追加する必要がある。
//        パネル（PrimitiveSpawnWizard）の図形リストはレジストリを見るので追記不要。
//
// 【配置】 Editor/PrimitiveSpawn/
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.EditorPrimitive
{
    /// <summary>ピボット（回転原点）の位置。</summary>
    //  ※ 数値は PrimitiveSpawnWizard のポップアップ表示順と対応しているため、
    //    並べ替え・挿入時はそちらも合わせること。
    public enum PrimitivePivotMode
    {
        /// <summary>底面（AABB 下端）。</summary>
        Bottom = 0,
        /// <summary>中心（AABB 中心）。</summary>
        Center = 1,
    }

    /// <summary>ピボット指定を各ジェネレータの Params.Pivot 値へ変換する。</summary>
    public static class PrimitivePivot
    {
        // 各ジェネレータは原点中心で組み立てたあと「頂点 -= Pivot * サイズ」で
        // ずらす（PrimitiveMeshPostProcess.ApplyPivotOffset と同規約）。
        // Pivot は AABB サイズに対する比率:
        //     y = -0.5 → 底面が原点   0 → 中心   +0.5 → 上面が原点
        //
        // 内訳（すべて「原点中心 → Pivot ぶん平行移動」で一致する）:
        //   Cube / Cylinder / Capsule / Pyramid : Pivot.y * Height
        //   Sphere                              : Pivot   * Radius * 2
        //   Plane                               : Pivot.y * Height（面ローカルの縦軸）

        /// <summary>底面ピボット。</summary>
        public static readonly Vector3 Bottom = new Vector3(0f, -0.5f, 0f);

        /// <summary>中心ピボット。</summary>
        public static readonly Vector3 Center = Vector3.zero;

        public static Vector3 ToVector(PrimitivePivotMode mode)
            => mode == PrimitivePivotMode.Bottom ? Bottom : Center;
    }

    /// <summary>組み込み図形の ID。文字列直書きを避けるための定数。</summary>
    public static class PrimitiveSpawnIds
    {
        public const string Cube     = "cube";
        public const string Sphere   = "sphere";
        public const string Cylinder = "cylinder";
        public const string Capsule  = "capsule";
        public const string Plane    = "plane";
        public const string Pyramid  = "pyramid";
    }

    /// <summary>1 図形ぶんの定義。</summary>
    public sealed class PrimitiveSpawnDefinition
    {
        /// <summary>レジストリ検索キー。</summary>
        public string Id { get; }

        /// <summary>メニュー／パネル表示名、および既定の GameObject 名。</summary>
        public string Label { get; }

        /// <summary>
        /// メッシュ生成関数。引数は Params.Pivot に入れる値（AABB サイズ比）。
        /// 既定は各ジェネレータの Params.Default に Pivot だけ差し替えたもの。
        /// 外部から差し替えることでパラメータを変更できる（拡張ポイント 1）。
        /// </summary>
        public Func<Vector3, MeshObject> Generate { get; set; }

        public PrimitiveSpawnDefinition(string id, string label, Func<Vector3, MeshObject> generate)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("id が空です。", nameof(id));

            Id       = id;
            Label    = string.IsNullOrEmpty(label) ? id : label;
            Generate = generate ?? throw new ArgumentNullException(nameof(generate));
        }
    }

    /// <summary>図形定義の登録簿。登録順を保持する。</summary>
    public static class PrimitiveSpawnRegistry
    {
        private static readonly List<PrimitiveSpawnDefinition> _order =
            new List<PrimitiveSpawnDefinition>();

        private static readonly Dictionary<string, PrimitiveSpawnDefinition> _byId =
            new Dictionary<string, PrimitiveSpawnDefinition>(StringComparer.Ordinal);

        static PrimitiveSpawnRegistry()
        {
            RegisterBuiltIn();
        }

        /// <summary>登録済み定義（登録順）。</summary>
        public static IReadOnlyList<PrimitiveSpawnDefinition> All => _order;

        /// <summary>登録する。同一 ID は後勝ちで置換する。</summary>
        public static void Register(PrimitiveSpawnDefinition def)
        {
            if (def == null) return;

            if (_byId.TryGetValue(def.Id, out var old))
            {
                int i = _order.IndexOf(old);
                if (i >= 0) _order[i] = def;
                else        _order.Add(def);
            }
            else
            {
                _order.Add(def);
            }
            _byId[def.Id] = def;
        }

        /// <summary>ID で引く。未登録なら null。</summary>
        public static PrimitiveSpawnDefinition Get(string id)
            => (!string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var d)) ? d : null;

        public static bool TryGet(string id, out PrimitiveSpawnDefinition def)
        {
            def = Get(id);
            return def != null;
        }

        /// <summary>登録順での位置。未登録なら -1。</summary>
        public static int IndexOf(string id)
        {
            for (int i = 0; i < _order.Count; i++)
                if (string.Equals(_order[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        // ============================================================
        // 組み込み図形
        // ============================================================
        //
        // 各ジェネレータは Generate(Params) → MeshObject を返す静的クラスで、
        // Params.Default に既定値を持つ（Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/）。
        // Params の RotationX / RotationY はプレビュー表示用でジオメトリには効かない。
        //
        // Params は構造体のため、Default をコピーして Pivot だけ差し替える。
        //
        private static void RegisterBuiltIn()
        {
            Register(new PrimitiveSpawnDefinition(
                PrimitiveSpawnIds.Cube, "Cube", pivot =>
                {
                    var p = CubeMeshGenerator.CubeParams.Default;
                    p.Pivot = pivot;
                    return CubeMeshGenerator.Generate(p);
                }));

            Register(new PrimitiveSpawnDefinition(
                PrimitiveSpawnIds.Sphere, "Sphere", pivot =>
                {
                    var p = SphereMeshGenerator.SphereParams.Default;
                    p.Pivot = pivot;
                    return SphereMeshGenerator.Generate(p);
                }));

            Register(new PrimitiveSpawnDefinition(
                PrimitiveSpawnIds.Cylinder, "Cylinder", pivot =>
                {
                    var p = CylinderMeshGenerator.CylinderParams.Default;
                    p.Pivot = pivot;
                    return CylinderMeshGenerator.Generate(p);
                }));

            Register(new PrimitiveSpawnDefinition(
                PrimitiveSpawnIds.Capsule, "Capsule", pivot =>
                {
                    var p = CapsuleMeshGenerator.CapsuleParams.Default;
                    p.Pivot = pivot;
                    return CapsuleMeshGenerator.Generate(p);
                }));

            Register(new PrimitiveSpawnDefinition(
                PrimitiveSpawnIds.Plane, "Plane", pivot =>
                {
                    var p = PlaneMeshGenerator.PlaneParams.Default;
                    p.Pivot = pivot;
                    return PlaneMeshGenerator.Generate(p);
                }));

            Register(new PrimitiveSpawnDefinition(
                PrimitiveSpawnIds.Pyramid, "Pyramid", pivot =>
                {
                    var p = PyramidMeshGenerator.PyramidParams.Default;
                    p.Pivot = pivot;
                    return PyramidMeshGenerator.Generate(p);
                }));
        }
    }
}
