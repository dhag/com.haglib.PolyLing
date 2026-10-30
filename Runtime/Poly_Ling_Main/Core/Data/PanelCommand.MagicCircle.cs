// PanelCommand.MagicCircle.cs
// 魔法陣：部品の図形生成コマンド（リング・星座の円周配置・オブジェクトの円周配置）と、
// 部品を空の親オブジェクトの子にまとめて作るコマンド。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【部品は普通の図形生成コマンド】
//   リング・星座の円周配置・オブジェクトの円周配置は CreatePrimitiveMeshCommand の派生で、
//   単独でも使える。紋章図形（CreateEmblemCommand）と文字（CreateTextMeshCommand）も
//   そのまま部品になる。
//
// 【魔法陣（CreateMagicCircleCommand）】
//   頂点を持たない空の親オブジェクトを 1 つ作り、部品を 1 つずつその子として作る。
//   部品の並びはそのままオブジェクトグループの項目になる（グループ 1 件）。
//   部品のパラメータを直したら、そのグループを作り直せば子の中身だけが入れ替わる。
//   全体の位置・向き・大きさは親の姿勢が持ち、子のローカル姿勢は単位のまま。
//
// 【部品の渡し方（elements）】
//   1 要素 = 1 部品で、「action;キー=値;キー=値…」の形の文字列。
//   action とキーは、その部品のコマンドを単独で送るときと同じもの
//   （例: createEmblem;params.kind=Hexagram;params.size=0.9）。
//   キーと値は URL エンコード（%XX）してよく、値に ; = , " を含むときは必ずエンコードする。
//   配置（placement.*）のうち位置・回転・拡大・追加先・グループ化は受け口が上書きする
//   （子は親の原点に単位の姿勢で置く）。材質番号と重複頂点の結合は部品の指定を使う。

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Data
{
    // ================================================================
    // 部品
    // ================================================================

    [PLCommand(Category = "geometry.create.primitive", Writes = PLWriteScope.Targets,
        Description = "同心のリングを任意の本数作る。半径・線の太さ・本数・間隔・厚みを指定できる。魔法陣の部品。")]
    public sealed class CreateMagicRingCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "MagicRing", Description = "リングのパラメータ", Required = true)]
        public MagicRingMeshGenerator.MagicRingParams Params { get; }

        public override string ShapeName => "MagicRing";
        public override string MeshName  => Params.MeshName;

        public CreateMagicRingCommand(
            int modelIndex,
            MagicRingMeshGenerator.MagicRingParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Category = "geometry.create.primitive", Writes = PLWriteScope.Targets,
        Description = "選んだ 12 星座のマークを円周上に並べる。12 等分の定位置か選んだ数での等分か、外向き・内向き（上下反転）・正立を選べる。魔法陣の部品。")]
    public sealed class CreateZodiacCircleCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "ZodiacCircle", Description = "星座の円周配置のパラメータ", Required = true)]
        public ZodiacCircleMeshGenerator.ZodiacCircleParams Params { get; }

        public override string ShapeName => "ZodiacCircle";
        public override string MeshName  => Params.MeshName;

        public CreateZodiacCircleCommand(
            int modelIndex,
            ZodiacCircleMeshGenerator.ZodiacCircleParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    /// <summary>
    /// オブジェクトの円周配置。配置元の索引から MeshObject への解決はディスパッチャ側が行う
    /// （藤壺・ネジ配置と同じ口）。
    /// </summary>
    [PLCommand(Category = "geometry.create.primitive", Writes = PLWriteScope.Targets,
        Description = "配置元オブジェクトを円周上に等分に並べる（藤壺のように複製する）。外向き・内向き（上下反転）・正立を選べる。魔法陣の部品。")]
    [PLModifier(Capture = PLGroupCapture.OnKeepAsGroup, Inverse = ModifierInverseMethod.RadialPlace,
        InverseKeeps = "個数・回る向き・向きの決め方・配置元・割り当て",
        InverseLoses = "部品ごとのずれ・回転・歪み、間隔の不揃い、円の中心のずれ、面の傾き")]
    public sealed class CreateRadialPlaceCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "RadialPlace", Description = "オブジェクトの円周配置のパラメータ", Required = true)]
        public RadialPlaceMeshGenerator.RadialPlaceParams Params { get; }

        /// <summary>配置元の MeshContextList インデックス。</summary>
        [PLParam(TextKey = "PlaceSourceIndices", Description = "配置元オブジェクトの索引", Required = true,
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read, MeshRefSpace = PLMeshRefSpace.Local)]
        public int[] SourceMasterIndices { get; }

        public override string ShapeName => "RadialPlace";
        public override string MeshName  => Params.MeshName;

        public CreateRadialPlaceCommand(
            int modelIndex,
            RadialPlaceMeshGenerator.RadialPlaceParams @params,
            int[] sourceMasterIndices,
            PrimitivePlacement placement)
            : base(modelIndex, placement)
        {
            Params              = @params;
            SourceMasterIndices = sourceMasterIndices ?? Array.Empty<int>();
        }
    }

    // ================================================================
    // 魔法陣
    // ================================================================

    [PLCommand(Category = "geometry.create.special", Writes = PLWriteScope.AddOnly,
        Effects = PLCommandEffect.CreatesObject | PLCommandEffect.Hierarchy,
        Description = "魔法陣を作る。頂点を持たない空の親オブジェクトを 1 つ作り、elements の部品（リング・紋章図形・円形の文字・星座の円周配置・オブジェクトの円周配置など図形生成コマンド）を順にその子として作る。部品の並びは 1 件のオブジェクトグループとして残り、作り直せる。全体の位置・回転・大きさは親が持つ。elements の 1 要素は「action;キー=値;…」（例: createEmblem;params.kind=Hexagram;params.size=0.9）。値に ; = , \" を含むときは URL エンコードする。部品の placement の位置・回転・拡大・追加先・グループ化は無視される。モデルが無ければ図形生成と同じくプロジェクトとモデルを作る。")]
    [PLModifier(Capture = PLGroupCapture.Assembly)]   // PlayerCommandDispatcher.MagicCircle.cs ExecuteEmptyParentAssembly
    public sealed class CreateMagicCircleCommand : PanelCommand
    {
        [PLParam(TextKey = "MeshName", Description = "親オブジェクトの名前。重なるときは番号を付ける", Required = true)]
        public string Name { get; }

        [PLParam(TextKey = "PlacePosition", Description = "親の位置（ワールド座標）", Required = true)]
        public Vector3 Position { get; }

        [PLParam(TextKey = "PlaceRotation", Description = "親の回転（度）", Required = true)]
        public Vector3 Rotation { get; }

        [PLParam(TextKey = "PlaceScale", Description = "親の拡大率（XYZ 共通）", Required = true, Min = 0.001)]
        public float Scale { get; }

        [PLParam(Description = "部品。1 要素が「action;キー=値;…」の 1 部品で、並び順に子として作る", Required = true)]
        public string[] Elements { get; }

        public CreateMagicCircleCommand(
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

    /// <summary>
    /// 魔法陣の部品（図形生成コマンド）と文字列「action;キー=値;…」の相互変換。
    /// パネル（組む側）とディスパッチャ（受ける側）が同じ規則を使う。
    /// </summary>
    public static class MagicCircleElementCodec
    {
        /// <summary>部品のコマンドを 1 要素の文字列にする。</summary>
        public static string Encode(CreatePrimitiveMeshCommand cmd)
        {
            if (cmd == null) return "";
            var sb = new StringBuilder(PanelCommandFactory.ActionOf(cmd.GetType()));
            foreach (var kv in PanelCommandFactory.ToArgs(cmd))
            {
                sb.Append(';')
                  .Append(Uri.EscapeDataString(kv.Key))
                  .Append('=')
                  .Append(Uri.EscapeDataString(kv.Value ?? ""));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 1 要素を部品のコマンドへ戻す。配置は「親の原点・単位の姿勢・新しいオブジェクト・グループ化なし」へ上書きする。
        /// </summary>
        public static bool TryDecode(
            string spec, int modelIndex, out CreatePrimitiveMeshCommand cmd, out string error)
        {
            cmd   = null;
            error = null;

            if (string.IsNullOrWhiteSpace(spec)) { error = "空の要素"; return false; }

            string[] parts = spec.Split(';');
            string action = Uri.UnescapeDataString(parts[0].Trim());
            if (action.Length == 0) { error = "action がありません"; return false; }

            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) { error = $"「キー=値」になっていない: {parts[i]}"; return false; }
                string k = Uri.UnescapeDataString(parts[i].Substring(0, eq).Trim());
                string v = Uri.UnescapeDataString(parts[i].Substring(eq + 1));
                args[k] = v;
            }

            foreach (var kv in ForcedPlacementArgs()) args[kv.Key] = kv.Value;

            var made = PanelCommandFactory.Create(action, modelIndex, args, out string why);
            if (made == null) { error = why ?? $"{action} を組めませんでした"; return false; }

            cmd = made as CreatePrimitiveMeshCommand;
            if (cmd == null) { error = $"{action} は図形生成コマンドではありません"; return false; }
            return true;
        }

        /// <summary>上書きする配置のフィールド名。</summary>
        private static readonly string[] ForcedFields =
        {
            nameof(PrimitivePlacement.WorldPosition),
            nameof(PrimitivePlacement.PlaceRotation),
            nameof(PrimitivePlacement.PlaceScale),
            nameof(PrimitivePlacement.BakeRotation),
            nameof(PrimitivePlacement.BakeScale),
            nameof(PrimitivePlacement.IgnorePoseInArmature),
            nameof(PrimitivePlacement.AddMode),
            nameof(PrimitivePlacement.AddTargetIndex),
            nameof(PrimitivePlacement.KeepAsGroup),
        };

        private static Dictionary<string, string> _forced;

        /// <summary>
        /// 上書きする配置のキーと値。キーの書き方（placement.xxx）と値の書き方は
        /// 実際のコマンドを ToArgs に通して取る（規則を 2 か所に書かないため）。
        /// </summary>
        private static Dictionary<string, string> ForcedPlacementArgs()
        {
            if (_forced != null) return _forced;

            var pl = PrimitivePlacement.Default;
            pl.WorldPosition        = Vector3.zero;
            pl.PlaceRotation        = Vector3.zero;
            pl.PlaceScale           = Vector3.one;
            pl.BakeRotation         = true;
            pl.BakeScale            = true;
            pl.IgnorePoseInArmature = false;
            pl.AddMode              = Poly_Ling.Player.PrimitiveAddMode.NewObject;
            pl.AddTargetIndex       = -1;
            pl.KeepAsGroup          = false;

            var probe = PanelCommandFactory.ToArgs(
                new CreateMagicRingCommand(0, MagicRingMeshGenerator.MagicRingParams.Default, pl));

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in probe)
            {
                foreach (var f in ForcedFields)
                {
                    if (kv.Key.EndsWith("." + f, StringComparison.OrdinalIgnoreCase)
                        && kv.Key.StartsWith("placement.", StringComparison.OrdinalIgnoreCase))
                    {
                        result[kv.Key] = kv.Value;
                        break;
                    }
                }
            }
            _forced = result;
            return result;
        }

        /// <summary>
        /// 部品の配置を、魔法陣・鉄塔の子として置く形にした PrimitivePlacement。
        /// 値は TryDecode が上書きする値（ForcedPlacementArgs）と同じにする。
        /// パネルが部品を組むときと、デッキの後付け（文字列を通らない）で使う
        /// （材質番号と重複頂点の結合は引数のまま）。
        /// </summary>
        public static PrimitivePlacement ChildPlacement(int materialIndex, bool mergeDuplicateVertices)
        {
            var pl = PrimitivePlacement.Default;
            pl.WorldPosition          = Vector3.zero;
            pl.PlaceRotation          = Vector3.zero;
            pl.PlaceScale             = Vector3.one;
            pl.BakeRotation           = true;
            pl.BakeScale              = true;
            pl.IgnorePoseInArmature   = false;
            pl.AddMode                = Poly_Ling.Player.PrimitiveAddMode.NewObject;
            pl.AddTargetIndex         = -1;
            pl.KeepAsGroup            = false;
            pl.MaterialIndex          = materialIndex;
            pl.MergeDuplicateVertices = mergeDuplicateVertices;
            return pl;
        }
    }
}
