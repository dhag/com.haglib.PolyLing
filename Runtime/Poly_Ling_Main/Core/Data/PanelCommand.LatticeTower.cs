// PanelCommand.LatticeTower.cs
// 鉄塔：塔体・デッキの図形生成コマンドと、空の親の下へ塔体とデッキを子として作るコマンド、
// 既存の鉄塔へデッキを足すコマンド、グループの項目の配置元を差し替えるコマンド。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【鉄塔（CreateTowerAssemblyCommand）】
//   魔法陣（CreateMagicCircleCommand）と同じ形。頂点を持たない空の親を 1 つ作り、
//   elements の部品（先頭は塔体 createLatticeTower、続いてデッキ createTowerDeck）を
//   順にその子として作る。部品の並びは 1 件のオブジェクトグループになる。
//   elements の書き方・配置の上書きは MagicCircleElementCodec と同じ。
//
// 【デッキの後付け（AddTowerDeckCommand）】
//   鉄塔の塔体・デッキ・親のどれか 1 つを指すと、その鉄塔のグループを探し、
//   デッキを親の子として作ってグループの末尾へ項目を足す。
//
// 【配置元の差し替え（ReplaceObjectGroupSourceCommand）】
//   出力先のオブジェクトを指すと、それを作った項目を探し、その項目が控えている配置元
//   （placement 以外で、ObjectId を控えている索引の引数がちょうど 1 つ）を差し替えて、
//   その項目だけ作り直す。デッキの取り替えに使う。魔法陣の円周配置にも効く。

using System;
using UnityEngine;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Data
{
    // ================================================================
    // 部品
    // ================================================================

    [PLCommand(Category = "geometry.create.primitive", Writes = PLWriteScope.Targets,
        Description = "鉄塔の塔体（ラチス構造）を作る。脚線を高さ・X 幅・Z 幅の点列で与え、区間ごとに段数と斜材の型（なし・X・片斜め・山形・菱形）を選ぶ。組柱の脚・足元アーチ・腕金・頂部の柱・門形の梁を付けられる。地面は y=0。")]
    public sealed class CreateLatticeTowerCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "LatticeTower", Description = "塔体のパラメータ", Required = true)]
        public LatticeTowerMeshGenerator.LatticeTowerParams Params { get; }

        public override string ShapeName => "LatticeTower";
        public override string MeshName  => Params.MeshName;

        public CreateLatticeTowerCommand(
            int modelIndex,
            LatticeTowerMeshGenerator.LatticeTowerParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    /// <summary>
    /// デッキ。配置元の索引から MeshObject への解決はディスパッチャ側が行う（藤壺・円周配置と同じ口）。
    /// </summary>
    [PLCommand(Category = "geometry.create.primitive", Writes = PLWriteScope.Targets,
        Description = "配置元オブジェクトを塔の軸（原点を通る Y 軸）上の指定の高さへ置く。複数なら結合して置く。鉄塔のデッキに使う。")]
    public sealed class CreateTowerDeckCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "TowerDeck", Description = "デッキのパラメータ", Required = true)]
        public TowerDeckMeshGenerator.TowerDeckParams Params { get; }

        /// <summary>配置元の MeshContextList インデックス。</summary>
        [PLParam(TextKey = "PlaceSourceIndices", Description = "配置元オブジェクトの索引", Required = true,
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int[] SourceMasterIndices { get; }

        public override string ShapeName => "TowerDeck";
        public override string MeshName  => Params.MeshName;

        public CreateTowerDeckCommand(
            int modelIndex,
            TowerDeckMeshGenerator.TowerDeckParams @params,
            int[] sourceMasterIndices,
            PrimitivePlacement placement)
            : base(modelIndex, placement)
        {
            Params              = @params;
            SourceMasterIndices = sourceMasterIndices ?? Array.Empty<int>();
        }
    }

    // ================================================================
    // 鉄塔
    // ================================================================

    [PLCommand(Category = "geometry.create.special", Writes = PLWriteScope.AddOnly,
        Effects = PLCommandEffect.CreatesObject | PLCommandEffect.Hierarchy,
        Description = "鉄塔を作る。頂点を持たない空の親オブジェクトを 1 つ作り、elements の部品を順にその子として作る。先頭は塔体（createLatticeTower）、続いてデッキ（createTowerDeck）を任意の数。部品の並びは 1 件のオブジェクトグループとして残り、作り直せる。全体の位置・回転・大きさは親が持つ。elements の 1 要素は「action;キー=値;…」（例: createTowerDeck;params.height=1.5;sourceMasterIndices=3）。値に ; = , \" を含むときは URL エンコードする。部品の placement の位置・回転・拡大・追加先・グループ化は無視される。モデルが無ければ図形生成と同じくプロジェクトとモデルを作る。")]
    public sealed class CreateTowerAssemblyCommand : PanelCommand
    {
        [PLParam(TextKey = "MeshName", Description = "親オブジェクトの名前。重なるときは番号を付ける", Required = true)]
        public string Name { get; }

        [PLParam(TextKey = "PlacePosition", Description = "親の位置（ワールド座標）", Required = true)]
        public Vector3 Position { get; }

        [PLParam(TextKey = "PlaceRotation", Description = "親の回転（度）", Required = true)]
        public Vector3 Rotation { get; }

        [PLParam(TextKey = "PlaceScale", Description = "親の拡大率（XYZ 共通）", Required = true, Min = 0.001)]
        public float Scale { get; }

        [PLParam(Description = "部品。1 要素が「action;キー=値;…」の 1 部品。先頭は createLatticeTower", Required = true)]
        public string[] Elements { get; }

        public CreateTowerAssemblyCommand(
            int modelIndex, string name, Vector3 position, Vector3 rotation, float scale, string[] elements)
            : base(modelIndex)
        {
            Name     = name ?? "";
            Position = position;
            Rotation = rotation;
            Scale    = scale;
            Elements = elements ?? Array.Empty<string>();
        }
    }

    [PLCommand(Category = "geometry.create.special", Writes = PLWriteScope.AddOnly,
        Effects = PLCommandEffect.CreatesObject | PLCommandEffect.Hierarchy,
        Description = "既存の鉄塔へデッキを足す。towerIndex は鉄塔の塔体・デッキ・親のどれか。デッキは鉄塔の親の子として作り、鉄塔のオブジェクトグループの末尾へ項目を足す。")]
    public sealed class AddTowerDeckCommand : PanelCommand
    {
        [PLParam(TextKey = "TowerTarget", Description = "鉄塔の塔体・デッキ・親のどれかの索引", Required = true,
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int TowerIndex { get; }

        [PLParam(TextKey = "TowerDeck", Description = "デッキのパラメータ", Required = true)]
        public TowerDeckMeshGenerator.TowerDeckParams Params { get; }

        [PLParam(TextKey = "PlaceSourceIndices", Description = "配置元オブジェクトの索引", Required = true,
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int[] SourceMasterIndices { get; }

        [PLParam(TextKey = "MaterialIndex", Description = "デッキの材質番号")]
        public int MaterialIndex { get; }

        public AddTowerDeckCommand(
            int modelIndex, int towerIndex,
            TowerDeckMeshGenerator.TowerDeckParams @params,
            int[] sourceMasterIndices, int materialIndex = 0)
            : base(modelIndex)
        {
            TowerIndex          = towerIndex;
            Params              = @params;
            SourceMasterIndices = sourceMasterIndices ?? Array.Empty<int>();
            MaterialIndex       = materialIndex;
        }
    }

    // ================================================================
    // オブジェクトグループ
    // ================================================================

    [PLCommand(Category = "object.group", Writes = PLWriteScope.Targets,
        Effects = PLCommandEffect.Topology, Verification = PLCommandVerification.Visual,
        Description = "オブジェクトグループの項目の配置元を差し替えて、その項目だけ作り直す。outputIndex はその項目が作ったオブジェクト（鉄塔のデッキ、魔法陣の円周配置など）。差し替えるのは、その項目が控えている配置元（placement 以外の索引の引数）で、ちょうど 1 つのときだけ。")]
    public sealed class ReplaceObjectGroupSourceCommand : PanelCommand
    {
        [PLParam(TextKey = "GroupOutputTarget", Description = "差し替える項目が作ったオブジェクトの索引", Required = true,
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write)]
        public int OutputIndex { get; }

        [PLParam(TextKey = "PlaceSourceIndices", Description = "新しい配置元オブジェクトの索引", Required = true,
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int[] SourceMasterIndices { get; }

        public ReplaceObjectGroupSourceCommand(int modelIndex, int outputIndex, int[] sourceMasterIndices)
            : base(modelIndex)
        {
            OutputIndex         = outputIndex;
            SourceMasterIndices = sourceMasterIndices ?? Array.Empty<int>();
        }
    }
}
